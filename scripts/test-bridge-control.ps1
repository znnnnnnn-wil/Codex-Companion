[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

# Exercise the real functions and action switch with inert OS adapters. No task,
# process, installation, or user credential is modified by this test.
$source = Join-Path $PSScriptRoot 'bridge-control.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw 'Control script failed parsing.' }
foreach ($definition in $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    Invoke-Expression $definition.Extent.Text
}
$actionSwitch = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.SwitchStatementAst] }, $false).Extent.Text
$target = 'test-bridge.exe'
$installRoot = $PSScriptRoot
$taskName = 'test-task'
$script:stopped = $false
$script:started = $false
$script:tick = 0
function Test-Path { param($LiteralPath) return $true }
function Get-ScheduledTask { param($TaskName, $ErrorAction) return [pscustomobject]@{ State = 'Running'; Triggers = $null } }
function Get-Process { param($Name, $ErrorAction) if ($script:hasProcess) { [pscustomobject]@{ Path = $target; Id = 42 } } }
function Get-BridgeRuntime { return $script:runtime }
function Start-ScheduledTask { param($TaskName) $script:started = $true }
function Set-ScheduledTask { param($TaskName, $Action) }
function Stop-BridgeProcesses { $script:stopped = $true }
function Start-Sleep { param($Milliseconds) }
function Get-Date { $script:tick += 10; return [datetime]::UtcNow.AddSeconds($script:tick) }
function New-ScheduledTaskAction { param($Execute, $Argument, $WorkingDirectory) return $Argument }
function Assert-Equal($Actual, $Expected, $Message) { if ($Actual -ne $Expected) { throw "$Message (actual=$Actual expected=$Expected)" } }

foreach ($state in @('Ready', 'PairingRequired', 'Reconnecting', 'Unavailable', 'Stopped')) {
    $script:hasProcess = $state -ne 'Stopped'
    $script:runtime = [pscustomobject]@{
        processRunning = $script:hasProcess
        state = $state
        connected = $state -eq 'Ready'
        authenticated = $state -eq 'Ready'
        pairingRequired = $state -eq 'PairingRequired'
        ready = $state -eq 'Ready'
        relayReachable = if ($state -eq 'Ready') { $true } else { $null }
        lastError = $null
    }
    $status = Get-BridgeStatus
    Assert-Equal $status.ProcessRunning $script:hasProcess 'Incorrect process state'
    Assert-Equal $status.RuntimeState $state 'Incorrect runtime state'
    Assert-Equal $status.Ready ($state -eq 'Ready') 'Incorrect readiness'
    if ($state -eq 'Unavailable') { Assert-Equal $status.Authenticated $null 'Unknown auth must not be guessed' }
    if ($state -eq 'PairingRequired') { Assert-Equal $status.PairingRequired $true 'Pairing state missing' }
    $Action = 'Start'
    $failed = $false
    try { $output = Invoke-Expression $actionSwitch 3>&1 | Out-String }
    catch { $failed = $true }
    Assert-Equal $failed ($state -eq 'Stopped') 'Start failure semantics incorrect'
    Assert-Equal $script:stopped $false 'Start must never kill a reconnecting process'
    if ($state -eq 'Ready' -and $output -notmatch 'Authenticated / Ready') { throw 'Ready confirmation missing' }
    if ($state -eq 'PairingRequired' -and $output -notmatch 'pairing is required') { throw 'Pairing guidance missing' }
}

$previous = [Environment]::GetEnvironmentVariable('CODEX_COMPANION_CREDENTIAL_PATH')
try {
    [Environment]::SetEnvironmentVariable('CODEX_COMPANION_CREDENTIAL_PATH', 'C:\custom path\credential.json')
    $arguments = New-BridgeTaskAction
    if ($arguments -notmatch '--credential-path "C:\\custom path\\credential.json"') { throw 'Scheduled task lost explicit credential override' }
}
finally { [Environment]::SetEnvironmentVariable('CODEX_COMPANION_CREDENTIAL_PATH', $previous) }
Write-Output 'Bridge control tests passed (runtime states, Start semantics, environment overrides).'
