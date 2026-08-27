[CmdletBinding()]
param(
    [switch]$EnableAutostart
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path $PSScriptRoot).Path
$installRoot = Join-Path $env:LOCALAPPDATA 'CodexCompanion\Bridge'
$target = Join-Path $installRoot 'CodexCompanion.Bridge.exe'

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
if ($source -ne $installRoot) {
    if (Test-Path -LiteralPath (Join-Path $source 'app')) {
        Copy-Item (Join-Path $source 'app\*') $installRoot -Recurse -Force
    }
    else {
        Get-ChildItem -LiteralPath $source -File |
            Where-Object { $_.Name -notin @('install-bridge.ps1', 'README.txt') } |
            Copy-Item -Destination $installRoot -Force
    }
}
Write-Output "Bridge files installed to $installRoot"

& $target setup
if ($LASTEXITCODE -ne 0) {
    throw "Bridge setup failed with exit code $LASTEXITCODE"
}

$configPath = Join-Path $env:LOCALAPPDATA 'CodexCompanion\config.json'
$config = if (Test-Path -LiteralPath $configPath) {
    Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
}
else {
    $null
}
$credentialPath = if ($config -and $config.credentialPath) {
    $config.credentialPath
}
else {
    Join-Path $env:LOCALAPPDATA 'CodexCompanion\bridge-credential.json'
}

if (-not (Test-Path -LiteralPath $credentialPath)) {
    Write-Output '首次安装需要完成一次手机配对。Bridge 将在当前窗口运行并显示配对码。'
    $pairingProcess = Start-Process -FilePath $target -ArgumentList 'run' -WorkingDirectory $installRoot -NoNewWindow -PassThru
    Read-Host '请在手机完成配对，确认凭据文件生成后按 Enter 继续'
    if (-not $pairingProcess.HasExited) {
        Stop-Process -Id $pairingProcess.Id -Force
    }
    if (-not (Test-Path -LiteralPath $credentialPath)) {
        Write-Warning '未检测到 Bridge 凭据，首次手动启动时仍需要完成配对。'
    }
}

$controlScript = Join-Path $installRoot 'bridge-control.ps1'
& $controlScript -Action $(if ($EnableAutostart) { 'EnableAutostart' } else { 'DisableAutostart' })
Write-Output 'Bridge installation completed. It is stopped by default.'
Write-Output "Start: powershell.exe -ExecutionPolicy Bypass -File `"$controlScript`" -Action Start"
Write-Output "Stop:  powershell.exe -ExecutionPolicy Bypass -File `"$controlScript`" -Action Stop"
Write-Output "Diagnose: $target doctor"
