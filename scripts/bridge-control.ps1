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

function Register-BridgeTask {
    param([bool]$Autostart)

    if (-not (Test-Path -LiteralPath $target)) {
        throw "Bridge executable was not found: $target"
    }

    # Recreating the task is intentional: Register-ScheduledTask can preserve
    # an old logon trigger when a replacement task omits -Trigger.
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue

    $actionDefinition = New-ScheduledTaskAction -Execute $target -Argument 'run' -WorkingDirectory $installRoot
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

switch ($Action) {
    'Start' {
        if (-not (Test-Path -LiteralPath $target)) {
            throw "Bridge executable was not found: $target"
        }
        if ($null -eq (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) {
            Register-BridgeTask -Autostart $false
        }
        $running = @(Get-Process -Name 'CodexCompanion.Bridge' -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $target }).Count -gt 0
        if ($running) {
            Write-Output 'Codex Companion Bridge is already running.'
            break
        }
        Start-ScheduledTask -TaskName $taskName
        Start-Sleep -Milliseconds 800
        $task = Get-ScheduledTask -TaskName $taskName
        if ($task.State -ne 'Running') {
            throw "Bridge failed to start. Scheduled task state: $($task.State)"
        }
        Write-Output 'Codex Companion Bridge started in the background.'
    }
    'Stop' {
        Stop-BridgeProcesses
        Write-Output 'Codex Companion Bridge stopped.'
    }
    'Status' {
        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        $processes = @(Get-Process -Name 'CodexCompanion.Bridge' -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $target })
        [pscustomobject]@{
            Installed = Test-Path -LiteralPath $target
            Running = $processes.Count -gt 0
            ProcessIds = @($processes | Select-Object -ExpandProperty Id)
            TaskState = if ($null -eq $task) { 'NotRegistered' } else { [string]$task.State }
            Autostart = Test-BridgeAutostart
        } | Format-List
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
