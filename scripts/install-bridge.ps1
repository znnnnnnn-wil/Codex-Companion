[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path $PSScriptRoot).Path
$installRoot = Join-Path $env:LOCALAPPDATA 'CodexCompanion\Bridge'
$target = Join-Path $installRoot 'CodexCompanion.Bridge.exe'
$taskName = 'Codex Companion Bridge'

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
if (Test-Path -LiteralPath (Join-Path $source 'app')) {
    Copy-Item (Join-Path $source 'app\*') $installRoot -Recurse -Force
}
else {
    Get-ChildItem -LiteralPath $source -File |
        Where-Object { $_.Name -notin @('install-bridge.ps1', 'uninstall-bridge.ps1', 'README.txt') } |
        Copy-Item -Destination $installRoot -Force
}
Copy-Item (Join-Path $source 'uninstall-bridge.ps1') $installRoot -Force
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
        Write-Warning '未检测到 Bridge 凭据，后台任务已创建但仍需要重新运行 Bridge 完成配对。'
    }
}

$action = New-ScheduledTaskAction -Execute $target -Argument 'run' -WorkingDirectory $installRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Description 'Codex Companion Bridge' -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
Write-Output "Bridge installed and started. Use: $target doctor"
