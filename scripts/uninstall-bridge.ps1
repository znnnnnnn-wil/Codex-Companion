[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
$taskName = 'Codex Companion Bridge'
$installRoot = Join-Path $env:LOCALAPPDATA 'CodexCompanion\Bridge'

Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $installRoot) {
    if ($PSCmdlet.ShouldProcess($installRoot, 'Remove installed Bridge files')) {
        Remove-Item -LiteralPath $installRoot -Recurse -Force
    }
}
Write-Output 'Codex Companion Bridge has been uninstalled. Credential and config files were preserved.'
