[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Start', 'Stop', 'Status', 'EnableAutostart', 'DisableAutostart')]
    [string]$Action
)

$ErrorActionPreference = 'Stop'
$taskName = 'Codex Companion Bridge'
$installRoot = Join-Path $env:LOCALAPPDATA 'CodexCompanion\Bridge'
$target = Join-Path $installRoot 'CodexCompanion.Bridge.exe'

function New-BridgeTaskAction {
    # Scheduled Tasks do not inherit this PowerShell session's environment.
    # Preserve explicit overrides so background run and local status use the same identity.
    $arguments = 'run'
    $options = [ordered]@{
        CODEX_COMPANION_CONFIG_PATH = '--config'
        CODEX_COMPANION_CREDENTIAL_PATH = '--credential-path'
        CODEX_COMPANION_RELAY_URL = '--relay-url'
        CODEX_EXECUTABLE = '--codex-executable'
        CODEX_COMPANION_LOG_LEVEL = '--log-level'
    }
    foreach ($name in $options.Keys) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            if ($value.Contains('"') -or $value.Contains("`r") -or $value.Contains("`n")) {
                throw "Invalid quote or newline in $name"
            }
            if ($name -eq 'CODEX_COMPANION_CONFIG_PATH') { $value = [IO.Path]::GetFullPath($value) }
            $arguments += ' ' + $options[$name] + ' "' + $value + '"'
        }
    }
    New-ScheduledTaskAction -Execute $target -Argument $arguments -WorkingDirectory $installRoot
}

function Register-BridgeTask {
    param([bool]$Autostart)

    if (-not (Test-Path -LiteralPath $target)) {
        throw "Bridge executable was not found: $target"
    }

    # Recreating the task is intentional: Register-ScheduledTask can preserve
    # an old logon trigger when a replacement task omits -Trigger.
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue

    $actionDefinition = New-BridgeTaskAction
    $settings = New-ScheduledTaskSettingsSet `
        -StartWhenAvailable `
        -RestartCount 3 `
        -RestartInterval (New-TimeSpan -Minutes 1) `
        -ExecutionTimeLimit ([TimeSpan]::Zero)
    $parameters = @{
        TaskName = $taskName
        Action = $actionDefinition
        Settings = $settings
        Description = 'Codex Companion Bridge background process'
        Force = $true
    }
    if ($Autostart) {
        $parameters.Trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    }
    Register-ScheduledTask @parameters | Out-Null
}

function Test-BridgeAutostart {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        return $false
    }
    if ($null -eq $task.Triggers) {
        return $false
    }
    return @($task.Triggers).Count -gt 0
}

function Stop-BridgeProcesses {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Get-Process -Name 'CodexCompanion.Bridge' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $target } |
        Stop-Process -Force
}

function Get-BridgeRuntime {
    if (-not (Test-Path -LiteralPath $target)) { return $null }
    # The executable loads the same effective config and credential path as run/pair.
    try {
        $output = & $target status --runtime 2>$null
        if ($LASTEXITCODE -eq 0) { return ($output | Out-String | ConvertFrom-Json) }
    }
    catch { return $null }
    return $null
}

function Get-BridgeStatus {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    $runtime = Get-BridgeRuntime
    $processes = @(Get-Process -Name 'CodexCompanion.Bridge' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $target })
    $running = ($processes.Count -gt 0) -or ($null -ne $runtime -and $runtime.processRunning)
    $known = $null -ne $runtime -and $runtime.processRunning -and $runtime.state -ne 'Unavailable'
    [pscustomobject]@{
        Installed = Test-Path -LiteralPath $target
        ProcessRunning = $running
        ProcessIds = @($processes | Select-Object -ExpandProperty Id)
        RuntimeState = if ($known) { $runtime.state } elseif ($running) { 'Unavailable' } else { 'Stopped' }
        RelayReachable = if ($known) { $runtime.relayReachable } else { $null }
        Connected = if ($known) { $runtime.connected } elseif ($running) { $null } else { $false }
        Authenticated = if ($known) { $runtime.authenticated } elseif ($running) { $null } else { $false }
        PairingRequired = if ($known) { $runtime.pairingRequired } elseif ($running) { $null } else { $false }
        Ready = $known -and $runtime.ready
        LastError = if ($null -ne $runtime) { $runtime.lastError } else { $null }
        TaskState = if ($null -eq $task) { 'NotRegistered' } else { [string]$task.State }
        Autostart = Test-BridgeAutostart
    }
}

switch ($Action) {
    'Start' {
        if (-not (Test-Path -LiteralPath $target)) {
            throw "Bridge executable was not found: $target"
        }
        if ($null -eq (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) {
            Register-BridgeTask -Autostart $false
        }
        $status = Get-BridgeStatus
        if (-not $status.ProcessRunning) {
            Set-ScheduledTask -TaskName $taskName -Action (New-BridgeTaskAction) | Out-Null
            Start-ScheduledTask -TaskName $taskName
        }
        $deadline = (Get-Date).AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 500
            $status = Get-BridgeStatus
            if ($status.Ready -or $status.PairingRequired -or $status.RuntimeState -eq 'StartupFailed') { break }
        } while ((Get-Date) -lt $deadline)
        if (-not $status.ProcessRunning) {
            throw "Bridge failed to start or exited. $($status.LastError) Run: `"$target`" doctor"
        }
        if ($status.Ready) {
            Write-Output 'Bridge process started. Connection status: Authenticated / Ready.'
        }
        elseif ($status.PairingRequired) {
            Write-Warning "Bridge process started, but pairing is required. Stop Bridge, then run: `"$target`" pair"
        }
        else {
            Write-Warning "Bridge process exists; runtime is $($status.RuntimeState), not ready. It has been left running. Run: `"$target`" doctor"
        }
        $status | Format-List
    }
    'Stop' {
        Stop-BridgeProcesses
        Write-Output 'Codex Companion Bridge stopped.'
    }
    'Status' {
        Get-BridgeStatus | Format-List
    }
    'EnableAutostart' {
        $wasRunning = @(Get-Process -Name 'CodexCompanion.Bridge' -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $target }).Count -gt 0
        Stop-BridgeProcesses
        Register-BridgeTask -Autostart $true
        if ($wasRunning) {
            Start-ScheduledTask -TaskName $taskName
        }
        Write-Output 'Bridge autostart enabled. It will start after the next Windows sign-in.'
    }
    'DisableAutostart' {
        $wasRunning = @(Get-Process -Name 'CodexCompanion.Bridge' -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $target }).Count -gt 0
        Stop-BridgeProcesses
        Register-BridgeTask -Autostart $false
        if ($wasRunning) {
            Start-ScheduledTask -TaskName $taskName
        }
        Write-Output 'Bridge autostart disabled. Manual start remains available.'
    }
}
