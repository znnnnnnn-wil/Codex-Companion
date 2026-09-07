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

$pairingStatus = & $target status --pairing | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Unable to read effective Bridge pairing configuration.' }
if (-not $pairingStatus.credentialExists) {
    Write-Output '首次安装需要完成手机配对；完成后命令会自动退出。'
    & $target pair
    if ($LASTEXITCODE -ne 0) {
        Write-Warning '配对尚未完成。安装后请运行 CodexCompanion.Bridge.exe pair 重试。'
    }
}

$controlScript = Join-Path $installRoot 'bridge-control.ps1'
& $controlScript -Action $(if ($EnableAutostart) { 'EnableAutostart' } else { 'DisableAutostart' })
Write-Output 'Bridge installation completed. It is stopped by default.'
Write-Output "Start: powershell.exe -ExecutionPolicy Bypass -File `"$controlScript`" -Action Start"
Write-Output "Stop:  powershell.exe -ExecutionPolicy Bypass -File `"$controlScript`" -Action Stop"
Write-Output "Diagnose: $target doctor"
