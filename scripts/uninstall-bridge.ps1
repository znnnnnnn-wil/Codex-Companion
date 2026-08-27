[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
$taskName = 'Codex Companion Bridge'
$installRoot = Join-Path $env:LOCALAPPDATA 'CodexCompanion\Bridge'
$target = Join-Path $installRoot 'CodexCompanion.Bridge.exe'

Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
Get-Process -Name 'CodexCompanion.Bridge' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $target } |
    Stop-Process -Force
Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $installRoot) {
    if ($PSCmdlet.ShouldProcess($installRoot, 'Remove installed Bridge files')) {
        Remove-Item -LiteralPath $installRoot -Recurse -Force
    }
}
Write-Output 'Codex Companion Bridge has been uninstalled. Credential and config files were preserved.'
