[CmdletBinding()]
param(
    [string]$CodexExecutable,
    [string]$ThreadId,
    [int]$Limit = 10,
    [int]$TimeoutSeconds = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-CodexExecutable {
    param([string]$ExplicitPath)

    if ($ExplicitPath) {
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    if ($env:CODEX_EXECUTABLE) {
        return (Resolve-Path -LiteralPath $env:CODEX_EXECUTABLE).Path
    }

    $command = Get-Command codex.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command -and $command.Source -notmatch '\\WindowsApps\\') {
        return $command.Source
    }

    $npmCommand = (Get-Command npm.cmd -ErrorAction Stop).Source
    $npmCache = (& $npmCommand config get cache).Trim()
    $nativeBinary = Get-ChildItem (Join-Path $npmCache '_npx') -Recurse -Filter codex.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '@openai\\codex-win32-x64.*\\bin\\codex\.exe$' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($nativeBinary) {
        return $nativeBinary.FullName
    }

    throw '找不到可在普通进程中启动的 Codex CLI。请设置 CODEX_EXECUTABLE。'
}

function Send-AppServerMessage {
    param(
        [System.Diagnostics.Process]$Process,
        [hashtable]$Message
    )

    $line = $Message | ConvertTo-Json -Compress -Depth 50
    $Process.StandardInput.WriteLine($line)
    $Process.StandardInput.Flush()
}

function Read-AppServerResponse {
    param(
        [System.Diagnostics.Process]$Process,
        [object]$RequestId,
        [int]$Timeout
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Timeout)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $remaining = $deadline - [DateTimeOffset]::UtcNow
        $readTask = $Process.StandardOutput.ReadLineAsync()
        if (-not $readTask.Wait($remaining)) {
            throw "等待 app-server 请求 $RequestId 响应超时。"
        }

        $line = $readTask.Result
        if ($null -eq $line) {
            throw "app-server 在响应请求 $RequestId 前退出。"
        }

        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $message = $line | ConvertFrom-Json -Depth 100
        if ($null -ne $message.PSObject.Properties['id'] -and $message.id -eq $RequestId) {
            if ($null -ne $message.PSObject.Properties['error']) {
                throw "app-server 请求 $RequestId 失败：$($message.error | ConvertTo-Json -Compress -Depth 20)"
            }
            return $message
        }
    }

    throw "等待 app-server 请求 $RequestId 响应超时。"
}

function ConvertTo-SafeShape {
    param(
        [AllowNull()][object]$Value,
        [int]$Depth = 0
    )

    if ($null -eq $Value) { return $null }
    if ($Depth -ge 8) { return '<max-depth>' }

    if ($Value -is [string]) { return '<string>' }
    if ($Value -is [bool]) { return '<boolean>' }
    if ($Value -is [ValueType]) { return "<$($Value.GetType().Name)>" }

    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [pscustomobject]) {
        $items = @($Value)
        return [ordered]@{
            count = $items.Count
            sample = if ($items.Count -gt 0) { ConvertTo-SafeShape $items[0] ($Depth + 1) } else { $null }
        }
    }

    $result = [ordered]@{}
    foreach ($property in $Value.PSObject.Properties) {
        if ($property.Name -in @('type', 'role') -and $property.Value -is [string]) {
            $result[$property.Name] = $property.Value
        }
        else {
            $result[$property.Name] = ConvertTo-SafeShape $property.Value ($Depth + 1)
        }
    }
    return $result
}

$resolvedCodex = Resolve-CodexExecutable $CodexExecutable
$version = (& $resolvedCodex --version).Trim()
Write-Output "CodexExecutable: $resolvedCodex"
Write-Output "CodexVersion: $version"

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $resolvedCodex
$startInfo.ArgumentList.Add('app-server')
$startInfo.ArgumentList.Add('--stdio')
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$stderrTask = $null

try {
    if (-not $process.Start()) { throw '无法启动 Codex app-server。' }
    $stderrTask = $process.StandardError.ReadToEndAsync()

    Send-AppServerMessage $process @{
        method = 'initialize'
        id = 0
        params = @{
            clientInfo = @{
                name = 'codex_companion_probe'
                title = 'Codex Companion Probe'
                version = '0.1.0'
            }
        }
    }
    $initialize = Read-AppServerResponse $process 0 $TimeoutSeconds
    Send-AppServerMessage $process @{ method = 'initialized'; params = @{} }
    Write-Output "Initialize: success ($($initialize.result.userAgent))"

    Send-AppServerMessage $process @{
        method = 'thread/list'
        id = 1
        params = @{
            limit = $Limit
            sortKey = 'updated_at'
            sortDirection = 'desc'
            sourceKinds = @('cli', 'vscode', 'appServer')
        }
    }
    $listResponse = Read-AppServerResponse $process 1 $TimeoutSeconds
    $threads = @($listResponse.result.data)
    Write-Output "ThreadList: success; count=$($threads.Count); nextCursor=$($null -ne $listResponse.result.nextCursor)"

    foreach ($thread in $threads) {
        $status = if ($thread.status) { $thread.status.type } else { 'unknown' }
        $name = if ($thread.name) { $thread.name } else { '<untitled>' }
        Write-Output "  $($thread.id) | $name | $($thread.cwd) | $($thread.updatedAt) | $status"
    }

    $selectedId = if ($ThreadId) { $ThreadId } elseif ($threads.Count -gt 0) { $threads[0].id } else { $null }
    if (-not $selectedId) {
        Write-Output 'ThreadRead: skipped; thread/list returned no threads.'
        exit 0
    }

    Send-AppServerMessage $process @{
        method = 'thread/read'
        id = 2
        params = @{ threadId = $selectedId; includeTurns = $true }
    }
    $readResponse = Read-AppServerResponse $process 2 $TimeoutSeconds
    $turns = @($readResponse.result.thread.turns)
    $items = @($turns | ForEach-Object { @($_.items) })
    $typeCounts = $items |
        Group-Object { if ($_.type) { $_.type } else { '<unknown>' } } |
        Sort-Object Name |
        ForEach-Object { "$($_.Name)=$($_.Count)" }
    Write-Output "ThreadRead: success; threadId=$selectedId; turns=$($turns.Count); items=$($items.Count); itemTypes=$($typeCounts -join ',')"
    Write-Output 'ItemTypeSafeShapes:'
    $itemShapes = [ordered]@{}
    foreach ($group in ($items | Group-Object { if ($_.type) { $_.type } else { '<unknown>' } } | Sort-Object Name)) {
        $itemShapes[$group.Name] = ConvertTo-SafeShape $group.Group[0]
    }
    $itemShapes | ConvertTo-Json -Depth 20
    Write-Output 'ThreadReadSafeShape:'
    ConvertTo-SafeShape $readResponse.result.thread | ConvertTo-Json -Depth 20
}
finally {
    if ($process -and -not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit(5000) | Out-Null
    }

    if ($stderrTask) {
        $stderrText = $stderrTask.GetAwaiter().GetResult()
        if (-not [string]::IsNullOrWhiteSpace($stderrText)) {
            Write-Verbose $stderrText.Trim()
        }
    }
}
