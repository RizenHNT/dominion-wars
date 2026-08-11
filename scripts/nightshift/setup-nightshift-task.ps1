[CmdletBinding()]
param(
    [string]$TaskName = 'DominionWars-NightShift',
    [datetime]$At = (Get-Date).Date.AddHours(1),
    [bool]$EnableAcWakeTimers = $true,
    [bool]$AllowBatteryWake = $false,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'

$setupNow = Get-Date
if ($At -le $setupNow) {
    $At = $setupNow.Date.Add($At.TimeOfDay)
    if ($At -le $setupNow) { $At = $At.AddDays(1) }
}

function Get-BranchName {
    param([Parameter(Mandatory)][string]$Repository)
    $output = @(& git -C $Repository branch --show-current)
    if ($LASTEXITCODE -ne 0) { throw "Unable to read Git branch for: $Repository" }
    $name = ($output -join '').Trim()
    if (-not $name) { throw "Git worktree is detached or has no branch: $Repository" }
    $name
}

$invocationRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$invocationBranch = Get-BranchName -Repository $invocationRoot
$worktree = if ($invocationBranch.StartsWith('agents/nightshift-', [System.StringComparison]::OrdinalIgnoreCase)) {
    $invocationRoot
}
else {
    Join-Path $invocationRoot '.nightshift\rehearsal'
}

if (-not (Test-Path -LiteralPath $worktree -PathType Container)) { throw "Night-shift worktree not found: $worktree" }
$branch = Get-BranchName -Repository $worktree
if (-not $branch.StartsWith('agents/nightshift-', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Scheduled task must target an agents/nightshift-* branch, not: $branch"
}

$runner = Join-Path $worktree 'scripts\nightshift\run-nightshift.ps1'
$scheduledLauncher = Join-Path $worktree 'scripts\nightshift\invoke-scheduled-nightshift.ps1'
$helper = Join-Path $worktree 'scripts\nightshift\invoke-command.ps1'
$configPath = Join-Path $worktree 'scripts\nightshift\nightshift.config.json'
foreach ($requiredFile in @($runner, $scheduledLauncher, $helper, $configPath)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) { throw "Required night-shift file is missing: $requiredFile" }
}
$config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json
if ([int]$config.version -ne 2) { throw 'Scheduled task requires nightshift.config.json version 2.' }
if ([int]$config.totalRunSeconds -gt 19800) { throw 'Runner total deadline must not exceed 5 hours 30 minutes.' }

$worktreeChanges = @(& git -C $worktree status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect scheduled worktree status.' }
if ($worktreeChanges.Count -gt 0) { throw "Scheduled worktree must be clean: $($worktreeChanges -join '; ')" }

$powershellExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$powercfgExe = Join-Path $env:SystemRoot 'System32\powercfg.exe'
foreach ($command in @($powershellExe, $powercfgExe)) {
    if (-not (Test-Path -LiteralPath $command -PathType Leaf)) { throw "Required Windows executable not found: $command" }
}
foreach ($cmdlet in @('New-ScheduledTaskAction', 'New-ScheduledTaskTrigger', 'New-ScheduledTaskPrincipal', 'New-ScheduledTaskSettingsSet', 'New-ScheduledTask', 'Register-ScheduledTask', 'Get-ScheduledTask', 'Get-ScheduledTaskInfo')) {
    if (-not (Get-Command $cmdlet -ErrorAction SilentlyContinue)) { throw "ScheduledTasks cmdlet unavailable: $cmdlet" }
}

function Get-WakeTimerIndexes {
    $output = @(& $powercfgExe /QUERY SCHEME_CURRENT SUB_SLEEP RTCWAKE)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to query wake-timer power settings.' }
    $matches = [Text.RegularExpressions.Regex]::Matches(($output -join "`n"), '0x([0-9A-Fa-f]{8})')
    if ($matches.Count -lt 2) { throw 'Could not parse AC/DC wake-timer indexes from powercfg output.' }
    [pscustomobject]@{
        Ac = [Convert]::ToInt32($matches[$matches.Count - 2].Groups[1].Value, 16)
        Dc = [Convert]::ToInt32($matches[$matches.Count - 1].Groups[1].Value, 16)
    }
}

function ConvertTo-TaskTimeSpan {
    param([Parameter(Mandatory)][object]$Value)
    if ($Value -is [TimeSpan]) { return [TimeSpan]$Value }
    $text = [string]$Value
    try { return [Xml.XmlConvert]::ToTimeSpan($text) }
    catch { return [TimeSpan]::Parse($text, [Globalization.CultureInfo]::InvariantCulture) }
}

$taskArguments = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}"' -f $scheduledLauncher
$action = New-ScheduledTaskAction -Execute $powershellExe -Argument $taskArguments -WorkingDirectory $worktree
$trigger = New-ScheduledTaskTrigger -Daily -At $At
$principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
$settingsArguments = @{
    StartWhenAvailable = $true
    WakeToRun = $true
    RunOnlyIfNetworkAvailable = $true
    DontStopIfGoingOnBatteries = $true
    DontStopOnIdleEnd = $true
    ExecutionTimeLimit = [TimeSpan]::FromMinutes(345)
    MultipleInstances = 'IgnoreNew'
}
if ($AllowBatteryWake) { $settingsArguments.AllowStartIfOnBatteries = $true }
$settings = New-ScheduledTaskSettingsSet @settingsArguments
$task = New-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Dominion Wars bounded PL -> Codex -> QA night shift. No commit, push, merge, release, or interactive approval.'

if ($ValidateOnly) {
    $currentWakeTimers = Get-WakeTimerIndexes
    [pscustomobject]@{
        Mode = 'VALIDATE_ONLY'
        TaskName = $TaskName
        Branch = $branch
        Worktree = $worktree
        FirstStart = $At.ToString('o')
        Execute = $powershellExe
        Arguments = $taskArguments
        InteractiveToken = $true
        LimitedPrivilege = $true
        WakeToRun = $true
        StartWhenAvailable = $true
        NetworkRequired = $true
        MultipleInstances = 'IgnoreNew'
        RunnerDeadlineMinutes = [int]$config.totalRunSeconds / 60
        SchedulerDeadlineMinutes = 345
        EnableAcWakeTimers = $EnableAcWakeTimers
        AllowBatteryWake = $AllowBatteryWake
        CurrentAcWakeTimerIndex = $currentWakeTimers.Ac
        CurrentDcWakeTimerIndex = $currentWakeTimers.Dc
    } | ConvertTo-Json -Depth 5
    exit 0
}

if ($EnableAcWakeTimers) {
    & $powercfgExe /SETACVALUEINDEX SCHEME_CURRENT SUB_SLEEP RTCWAKE 1
    if ($LASTEXITCODE -ne 0) { throw 'Failed to enable AC wake timers. Run this setup from an elevated PowerShell session.' }
}
& $powercfgExe /SETDCVALUEINDEX SCHEME_CURRENT SUB_SLEEP RTCWAKE $(if ($AllowBatteryWake) { 1 } else { 0 })
if ($LASTEXITCODE -ne 0) { throw 'Failed to configure battery wake timers.' }
& $powercfgExe /SETACTIVE SCHEME_CURRENT
if ($LASTEXITCODE -ne 0) { throw 'Failed to reactivate the current power scheme.' }
$wakeTimerIndexes = Get-WakeTimerIndexes
if ($EnableAcWakeTimers -and $wakeTimerIndexes.Ac -ne 1) { throw "AC wake timer verification failed: index $($wakeTimerIndexes.Ac)." }
$expectedDcWakeIndex = if ($AllowBatteryWake) { 1 } else { 0 }
if ($wakeTimerIndexes.Dc -ne $expectedDcWakeIndex) { throw "DC wake timer verification failed: index $($wakeTimerIndexes.Dc)." }

Register-ScheduledTask -TaskName $TaskName -InputObject $task -Force | Out-Null
$registered = Get-ScheduledTask -TaskName $TaskName
$registeredInfo = Get-ScheduledTaskInfo -TaskName $TaskName
$registeredActions = @($registered.Actions)
$registeredTriggers = @($registered.Triggers)
$actualAction = if ($registeredActions.Count -eq 1) { $registeredActions[0] } else { $null }
$actualTrigger = if ($registeredTriggers.Count -eq 1) { $registeredTriggers[0] } else { $null }

$mismatches = New-Object System.Collections.Generic.List[string]
if ($registeredActions.Count -ne 1) { $mismatches.Add('action count') }
if ($registeredTriggers.Count -ne 1) { $mismatches.Add('trigger count') }
if ($actualAction -and $actualAction.Execute -ne $powershellExe) { $mismatches.Add('executable') }
if ($actualAction -and $actualAction.Arguments -ne $taskArguments) { $mismatches.Add('arguments') }
if ($actualAction -and $actualAction.WorkingDirectory -ne $worktree) { $mismatches.Add('working directory') }
if (-not $registered.Settings.WakeToRun) { $mismatches.Add('WakeToRun') }
if (-not $registered.Settings.StartWhenAvailable) { $mismatches.Add('StartWhenAvailable') }
if (-not $registered.Settings.RunOnlyIfNetworkAvailable) { $mismatches.Add('network requirement') }
if ([string]$registered.Settings.MultipleInstances -ne 'IgnoreNew') { $mismatches.Add('multiple-instance policy') }
if ([string]$registered.Settings.Enabled -ne 'True') { $mismatches.Add('task disabled') }
if (-not $registered.Settings.DisallowStartIfOnBatteries -and -not $AllowBatteryWake) { $mismatches.Add('battery-start policy') }
if ($registered.Settings.StopIfGoingOnBatteries) { $mismatches.Add('battery-stop policy') }
if ([int]$registered.Settings.RestartCount -ne 0) { $mismatches.Add('restart policy') }
if ((ConvertTo-TaskTimeSpan -Value $registered.Settings.ExecutionTimeLimit) -ne [TimeSpan]::FromMinutes(345)) { $mismatches.Add('execution time limit') }
if ([string]$registered.Principal.RunLevel -ne 'Limited') { $mismatches.Add('run level') }
if ([string]$registered.Principal.LogonType -ne 'Interactive') { $mismatches.Add('interactive logon type') }
$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$expectedPrincipalIds = @(
    $currentIdentity.Name
    $currentIdentity.User.Value
    ($currentIdentity.Name -split '\\')[-1]
)
if (@($expectedPrincipalIds | Where-Object { ([string]$registered.Principal.UserId).Equals([string]$_, [StringComparison]::OrdinalIgnoreCase) }).Count -eq 0) {
    $mismatches.Add('principal user')
}
if ($actualTrigger -and -not $actualTrigger.Enabled) { $mismatches.Add('trigger disabled') }
if ($actualTrigger -and [int]$actualTrigger.DaysInterval -ne 1) { $mismatches.Add('daily interval') }
if ($actualTrigger) {
    $actualStart = [DateTime]::Parse([string]$actualTrigger.StartBoundary, [Globalization.CultureInfo]::InvariantCulture)
    if ($actualStart.Date -ne $At.Date -or $actualStart.TimeOfDay -ne $At.TimeOfDay) { $mismatches.Add('trigger start boundary') }
}
if ($registeredInfo.NextRunTime -le (Get-Date)) { $mismatches.Add('next run is not in the future') }
if ($mismatches.Count -gt 0) { throw "Scheduled task registration drift detected: $($mismatches -join ', ')" }

[pscustomobject]@{
    Status = 'REGISTERED_AND_VERIFIED'
    TaskName = $TaskName
    Branch = $branch
    Worktree = $worktree
    NextRunTime = $registeredInfo.NextRunTime
    LastTaskResult = $registeredInfo.LastTaskResult
    WakeToRun = $registered.Settings.WakeToRun
    StartWhenAvailable = $registered.Settings.StartWhenAvailable
    NetworkRequired = $registered.Settings.RunOnlyIfNetworkAvailable
    MultipleInstances = [string]$registered.Settings.MultipleInstances
    LogonType = [string]$registered.Principal.LogonType
    RunLevel = [string]$registered.Principal.RunLevel
    AcWakeTimersEnabled = $EnableAcWakeTimers
    BatteryWakeAllowed = $AllowBatteryWake
    AcWakeTimerIndex = $wakeTimerIndexes.Ac
    DcWakeTimerIndex = $wakeTimerIndexes.Dc
} | ConvertTo-Json -Depth 5
