[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$Simulation,
    [switch]$Scheduled,
    [switch]$ApprovedScheduledGoal,
    [switch]$RetryGoal,
    [switch]$SafetySelfTest,
    [ValidateSet('Mixed', 'Success', 'Timeout')]
    [string]$SimulationScenario = 'Mixed',
    [string]$GoalFile = 'docs/DAILY_GOAL.md'
)

$ErrorActionPreference = 'Stop'
$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$configPath = Join-Path $scriptRoot 'nightshift.config.json'
$config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json
$stateHasher = [Security.Cryptography.SHA256]::Create()
try {
    $stateKey = ([BitConverter]::ToString($stateHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($repoRoot)))).Replace('-', '').Substring(0, 16).ToLowerInvariant()
}
finally { $stateHasher.Dispose() }
$privateStateRoot = Join-Path (Join-Path $env:LOCALAPPDATA 'DominionWarsNightshift\state') $stateKey
$stateRoot = if ($Simulation) { Join-Path $repoRoot '.nightshift\simulation-state' } else { $privateStateRoot }
$testTempRoot = Join-Path $repoRoot 'build\nightshift-test-tmp'
$agentTempRoot = Join-Path $repoRoot 'nightshift-agent-tmp'
$commandHelper = Join-Path $scriptRoot 'invoke-command.ps1'
$scheduledLauncher = Join-Path $scriptRoot 'invoke-scheduled-nightshift.ps1'
$ledgerPath = Join-Path $stateRoot $(if ($Simulation) { 'simulation-ledger.json' } else { 'goal-ledger.json' })
$lockPath = Join-Path $stateRoot 'nightshift.lock'
$runId = '{0}_{1}' -f (Get-Date -Format 'yyyy-MM-dd_HHmmss'), ([guid]::NewGuid().ToString('N').Substring(0, 8))
$runRoot = Join-Path $stateRoot $runId
$reportPath = if ($Simulation) { Join-Path $runRoot 'SIMULATION_REPORT.md' } else { Join-Path $repoRoot 'docs\NIGHT_REPORT.md' }
$schedulerLog = Join-Path $stateRoot 'scheduler.log'
$schedulerStatusPath = Join-Path $stateRoot 'last-scheduled-status.json'
$configuredMiniMax = [string]$config.pl.command
$configuredCodex = [string]$config.developer.command
$script:runStarted = $false
$script:currentStage = 'STARTUP'
$script:goalHash = ''
$script:plan = $null
$script:runLock = $null
$script:startingBranch = ''
$script:startingHead = ''
$script:controlPlaneHashes = @{}
$script:goalTestProfiles = @()
$script:deadlineUtc = [DateTime]::MaxValue
$script:paidModelAttempts = 0
$script:usageRecords = [Collections.Generic.List[object]]::new()
$script:launchId = [string]$env:DOMINION_NIGHTSHIFT_LAUNCH_ID

if ($Scheduled -and $script:launchId -notmatch '^[0-9a-f]{32}$') {
    throw 'Scheduled launch is missing its watchdog invocation identifier.'
}

if ([int]$config.version -ne 2) { throw "Unsupported night-shift config version: $($config.version)" }
if ([int]$config.maxTasks -ne 1) { throw 'Night-shift version 2 requires maxTasks=1 until per-task worktree isolation exists.' }
if ([int]$config.maxRepairCycles -lt 1 -or [int]$config.maxRepairCycles -gt 3) { throw 'maxRepairCycles must be between 1 and 3.' }
foreach ($controlHelper in @($commandHelper, $scheduledLauncher)) {
    if (-not (Test-Path -LiteralPath $controlHelper -PathType Leaf)) { throw "Night-shift helper not found: $controlHelper" }
}
foreach ($timeoutName in @('plannerSeconds', 'developerSeconds', 'testSeconds', 'qaSeconds', 'finalReviewSeconds')) {
    $value = [int]$config.timeouts.$timeoutName
    if ($value -lt 1 -or $value -gt 21600) { throw "Invalid timeout $timeoutName=$value" }
}
if ([int]$config.maxCapturedOutputChars -lt 10000 -or [int]$config.maxCapturedOutputChars -gt 5000000) {
    throw 'maxCapturedOutputChars must be between 10000 and 5000000.'
}
if ([int]$config.maxGoalChars -lt 1000 -or [int]$config.maxGoalChars -gt 100000) { throw 'maxGoalChars must be between 1000 and 100000.' }
if ([int]$config.maxProviderPromptChars -lt 20000 -or [int]$config.maxProviderPromptChars -gt 500000) { throw 'maxProviderPromptChars must be between 20000 and 500000.' }
if ([int]$config.maxPaidModelAttempts -lt 1 -or [int]$config.maxPaidModelAttempts -gt 30) { throw 'maxPaidModelAttempts must be between 1 and 30.' }
if ([int]$config.providerMaxAttempts -lt 1 -or [int]$config.providerMaxAttempts -gt 3) { throw 'providerMaxAttempts must be between 1 and 3.' }
if ([int]$config.providerRetryBaseSeconds -lt 1 -or [int]$config.providerRetryBaseSeconds -gt 30) { throw 'providerRetryBaseSeconds must be between 1 and 30.' }
if ([int]$config.totalRunSeconds -lt 600 -or [int]$config.totalRunSeconds -gt 20700) { throw 'totalRunSeconds must be between 600 and 20700.' }
if ([int]$config.scheduledLatestStartHour -lt 2 -or [int]$config.scheduledLatestStartHour -gt 12) { throw 'scheduledLatestStartHour must be between 2 and 12.' }
if ([int]$config.goalMaxAgeHours -lt 2 -or [int]$config.goalMaxAgeHours -gt 48) { throw 'goalMaxAgeHours must be between 2 and 48.' }
if ([string]$config.developer.model -ne 'gpt-5.6-luna') { throw 'Night-shift developer model must remain pinned to gpt-5.6-luna.' }
if ([string]$config.developer.reasoningEffort -notin @('low', 'medium', 'high', 'xhigh', 'max')) { throw 'Invalid night-shift developer reasoning effort.' }

if (-not (Get-Command $configuredMiniMax -ErrorAction SilentlyContinue)) {
    foreach ($candidate in @(
        (Join-Path $env:APPDATA 'npm\mmx.ps1'),
        (Join-Path $env:APPDATA 'npm\mmx.cmd')
    )) {
        if (Test-Path -LiteralPath $candidate) { $config.pl.command = $candidate; break }
    }
}

if (-not (Get-Command $configuredCodex -ErrorAction SilentlyContinue)) {
    $codexCandidate = Get-ChildItem -Path (Join-Path $env:USERPROFILE '.vscode\extensions\openai.chatgpt-*-win32-x64\bin\windows-x86_64\codex.exe') -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($codexCandidate) { $config.developer.command = $codexCandidate.FullName }
}
$pythonCommand = $null
foreach ($candidate in @((Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'), 'python', 'py')) {
    if ($candidate -and (Get-Command $candidate -ErrorAction SilentlyContinue)) {
        $pythonCommand = (Get-Command $candidate).Source
        break
    }
}

function Write-AtomicText {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][AllowEmptyString()][string]$Content)
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    $temporary = Join-Path $directory ('.{0}.{1}.tmp' -f ([System.IO.Path]::GetFileName($Path)), [guid]::NewGuid().ToString('N'))
    [System.IO.File]::WriteAllText($temporary, $Content, [System.Text.UTF8Encoding]::new($false))
    try {
        if (Test-Path -LiteralPath $Path) {
            try { [System.IO.File]::Replace($temporary, $Path, $null) }
            catch { Move-Item -LiteralPath $temporary -Destination $Path -Force }
        }
        else {
            Move-Item -LiteralPath $temporary -Destination $Path
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Write-AtomicJson {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][object]$Value, [int]$Depth = 20)
    Write-AtomicText -Path $Path -Content ($Value | ConvertTo-Json -Depth $Depth)
}

function Add-SchedulerLog {
    param([Parameter(Mandatory)][string]$Message)
    if (-not (Test-Path -LiteralPath $stateRoot -PathType Container)) {
        New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
    }
    "$(Get-Date -Format 'o') $Message" | Add-Content -LiteralPath $schedulerLog -Encoding UTF8
}

function Write-SchedulerStatus {
    param([Parameter(Mandatory)][string]$Status, [Parameter(Mandatory)][string]$Message, [int]$ExitCode = 0)
    if (-not $Scheduled) { return }
    Write-AtomicJson -Path $schedulerStatusPath -Value ([ordered]@{
        status = $Status
        message = $Message
        exitCode = $ExitCode
        runId = $runId
        launchId = $script:launchId
        stage = $script:currentStage
        updatedAt = (Get-Date -Format 'o')
    })
}

function Get-TextSha256 {
    param([Parameter(Mandatory)][string]$Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Read-RunLedger {
    if (-not (Test-Path -LiteralPath $ledgerPath -PathType Leaf)) {
        return [pscustomobject]@{ version = 2; entries = @() }
    }
    $document = Get-Content -Raw -LiteralPath $ledgerPath | ConvertFrom-Json
    if ([int]$document.version -eq 2 -and $document.PSObject.Properties.Name -contains 'entries') {
        return $document
    }
    if ($document.PSObject.Properties.Name -contains 'goalHash') {
        return [pscustomobject]@{ version = 2; entries = @($document) }
    }
    throw "Invalid goal ledger format: $ledgerPath"
}

function Get-RunLedgerEntry {
    param([Parameter(Mandatory)][string]$GoalHash)
    $matches = @((Read-RunLedger).entries | Where-Object { [string]$_.goalHash -eq $GoalHash })
    if ($matches.Count -eq 0) { return $null }
    $matches[$matches.Count - 1]
}

function Update-RunLedger {
    param([Parameter(Mandatory)][string]$Status, [string]$Message = '')
    if (-not $script:goalHash) { return }
    $entry = [ordered]@{
        goalHash = $script:goalHash
        runId = $runId
        status = $Status
        stage = $script:currentStage
        message = $Message
        updatedAt = (Get-Date -Format 'o')
    }
    $existingEntries = @((Read-RunLedger).entries | Where-Object { [string]$_.runId -ne $runId })
    $entries = @($existingEntries + [pscustomobject]$entry)
    if ($entries.Count -gt 200) { $entries = @($entries | Select-Object -Last 200) }
    Write-AtomicJson -Path $ledgerPath -Value ([ordered]@{ version = 2; entries = $entries })
}

function Set-RunStage {
    param([Parameter(Mandatory)][string]$Stage)
    $script:currentStage = $Stage
    if ($script:runStarted) { Update-RunLedger -Status 'RUNNING' }
}

function Get-BoundedTimeoutSeconds {
    param([Parameter(Mandatory)][ValidateRange(1, 21600)][int]$RequestedSeconds)
    if ($script:deadlineUtc -eq [DateTime]::MaxValue) { return $RequestedSeconds }
    $remaining = [int][Math]::Floor(($script:deadlineUtc - [DateTime]::UtcNow).TotalSeconds)
    if ($remaining -lt 1) { throw 'The overall night-shift deadline was reached.' }
    [Math]::Min($RequestedSeconds, $remaining)
}

function Get-WorkspaceFreeBytes {
    try {
        $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($repoRoot))
        $free = [int64]$drive.AvailableFreeSpace
        if ($free -le 0) { throw 'Drive reported no available-space value.' }
        $free
    }
    catch { throw "Unable to query workspace free disk space: $($_.Exception.Message)" }
}

function Initialize-NativeJobApi {
    if ('DominionWarsNightShift.KillJob' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace DominionWarsNightShift {
    public static class KillJob {
        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits {
            public BasicLimits BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        public static IntPtr CreateKillOnCloseJob() {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return IntPtr.Zero;
            ExtendedLimits limits = new ExtendedLimits();
            // Kill descendants when the controller closes the job, cap process
            // count and memory, and keep background automation below normal
            // priority so a bad agent/test cannot trivially monopolize the PC.
            limits.BasicLimitInformation.LimitFlags = 0x00002328;
            limits.BasicLimitInformation.ActiveProcessLimit = 64;
            limits.BasicLimitInformation.PriorityClass = 0x00004000;
            if (UIntPtr.Size == 4) {
                limits.ProcessMemoryLimit = new UIntPtr(1073741824U);
                limits.JobMemoryLimit = new UIntPtr(2147483648U);
            } else {
                limits.ProcessMemoryLimit = new UIntPtr(2147483648UL);
                limits.JobMemoryLimit = new UIntPtr(4294967296UL);
            }
            int size = Marshal.SizeOf(typeof(ExtendedLimits));
            IntPtr pointer = Marshal.AllocHGlobal(size);
            try {
                Marshal.StructureToPtr(limits, pointer, false);
                if (!SetInformationJobObject(job, 9, pointer, (uint)size)) {
                    CloseHandle(job);
                    return IntPtr.Zero;
                }
                return job;
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }
    }
}
'@
}

function Invoke-CapturedCommand {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$OutputFile,
        [Parameter(Mandatory)][ValidateRange(1, 21600)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][string]$Stage,
        [hashtable]$Environment = @{},
        [switch]$SanitizeEnvironment
    )
    Set-RunStage -Stage $Stage
    $TimeoutSeconds = Get-BoundedTimeoutSeconds -RequestedSeconds $TimeoutSeconds
    $specPath = "$OutputFile.command.json"
    $resultPath = "$OutputFile.result.json"
    $startGatePath = "$OutputFile.start"
    foreach ($staleArtifact in @($resultPath, $startGatePath)) {
        if (Test-Path -LiteralPath $staleArtifact) { Remove-Item -LiteralPath $staleArtifact -Force }
    }
    $spec = [ordered]@{
        command = $Command
        arguments = @($Arguments)
        workingDirectory = $repoRoot
        outputFile = $OutputFile
        resultFile = $resultPath
        startGateFile = $startGatePath
        maxOutputChars = [int]$config.maxCapturedOutputChars
        environment = $Environment
        sanitizeEnvironment = [bool]$SanitizeEnvironment
    }
    Write-AtomicJson -Path $specPath -Value $spec

    $powershellExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $processInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $processInfo.FileName = $powershellExe
    $processInfo.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$commandHelper`" -CommandSpecFile `"$specPath`""
    $processInfo.WorkingDirectory = $repoRoot
    $processInfo.UseShellExecute = $false
    $processInfo.CreateNoWindow = $true
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $processInfo
    $timedOut = $false
    $resourceExceeded = $false
    $job = [IntPtr]::Zero
    try {
        if ((Get-WorkspaceFreeBytes) -lt 2GB) { throw "Insufficient free disk space before $Stage (minimum 2 GB)." }
        Initialize-NativeJobApi
        $job = [DominionWarsNightShift.KillJob]::CreateKillOnCloseJob()
        if ($job -eq [IntPtr]::Zero) { throw "Unable to create a process-containment job for $Stage." }
        if (-not $process.Start()) { throw "Failed to start $Stage command." }
        if (-not [DominionWarsNightShift.KillJob]::AssignProcessToJobObject($job, $process.Handle)) {
            try { $process.Kill() } catch {}
            throw "Unable to contain the $Stage process tree; execution was stopped."
        }
        Write-AtomicText -Path $startGatePath -Content 'GO'
        $commandDeadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        while (-not $process.HasExited) {
            $remainingMilliseconds = [int][Math]::Min(5000, [Math]::Max(1, ($commandDeadline - [DateTime]::UtcNow).TotalMilliseconds))
            if ($process.WaitForExit($remainingMilliseconds)) { break }
            if ((Get-WorkspaceFreeBytes) -lt 512MB) {
                $resourceExceeded = $true
                break
            }
            if ([DateTime]::UtcNow -ge $commandDeadline) {
                $timedOut = $true
                break
            }
        }
        if ($timedOut -or $resourceExceeded) {
            $null = [DominionWarsNightShift.KillJob]::CloseHandle($job)
            $job = [IntPtr]::Zero
            $taskkill = Join-Path $env:SystemRoot 'System32\taskkill.exe'
            if (-not $process.HasExited) { try { & $taskkill /PID $process.Id /T /F 2>&1 | Out-Null } catch { try { $process.Kill() } catch {} } }
            try { $process.WaitForExit(10000) | Out-Null } catch {}
            $terminationMessage = if ($resourceExceeded) {
                "RESOURCE LIMIT: $Stage was terminated because free disk space fell below 512 MB."
            }
            else { "TIMEOUT: $Stage exceeded $TimeoutSeconds seconds; its process tree was terminated." }
            $terminationMessage | Add-Content -LiteralPath $OutputFile -Encoding UTF8
        }
    }
    finally {
        if ($job -ne [IntPtr]::Zero) { $null = [DominionWarsNightShift.KillJob]::CloseHandle($job) }
        $process.Dispose()
    }
    $output = if (Test-Path -LiteralPath $OutputFile) { Get-Content -Raw -LiteralPath $OutputFile } else { '' }
    if ($timedOut) {
        return [pscustomobject]@{ ExitCode = 124; Output = $output; TimedOut = $true; OutputTruncated = $false }
    }
    if ($resourceExceeded) {
        return [pscustomobject]@{ ExitCode = 125; Output = $output; TimedOut = $false; OutputTruncated = $false }
    }
    if (-not (Test-Path -LiteralPath $resultPath)) {
        return [pscustomobject]@{ ExitCode = 1; Output = $output; TimedOut = $false; OutputTruncated = $false }
    }
    $result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
    [pscustomobject]@{
        ExitCode = [int]$result.exitCode
        Output = $output
        TimedOut = $false
        OutputTruncated = [bool]$result.outputTruncated
    }
}

function Get-GitChangedPaths {
    $tracked = @(& git -C $repoRoot diff --name-only)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to inspect unstaged paths.' }
    $staged = @(& git -C $repoRoot diff --cached --name-only)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to inspect staged paths.' }
    $untracked = @(& git -C $repoRoot ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to inspect untracked paths.' }
    @($tracked + $staged + $untracked) |
        Where-Object { $_ -and -not $_.StartsWith('.nightshift/') } |
        Sort-Object -Unique
}

function Get-GitDeletedPaths {
    $unstaged = @(& git -C $repoRoot diff --name-only --diff-filter=DR)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to inspect unstaged deletions.' }
    $staged = @(& git -C $repoRoot diff --cached --name-only --diff-filter=DR)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to inspect staged deletions.' }
    @($unstaged + $staged | Where-Object { $_ } | Sort-Object -Unique)
}

function Get-GitStagedPaths {
    $paths = @(& git -C $repoRoot diff --cached --name-only)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to inspect staged paths.' }
    @($paths | Where-Object { $_ } | Sort-Object -Unique)
}

function Assert-RepositoryControlState {
    if ($Simulation) { return }
    $currentBranchOutput = @(& git -C $repoRoot branch --show-current)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read the current Git branch.' }
    $currentHeadOutput = @(& git -C $repoRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read the current Git commit.' }
    $currentBranch = ($currentBranchOutput -join '').Trim()
    $currentHead = ($currentHeadOutput -join '').Trim()
    if (-not $currentBranch -or -not $currentHead -or $currentBranch -ne $script:startingBranch -or $currentHead -ne $script:startingHead) {
        throw "Git branch or HEAD changed during automation (expected $($script:startingBranch) at $($script:startingHead))."
    }
    foreach ($entry in $script:controlPlaneHashes.GetEnumerator()) {
        if (-not (Test-Path -LiteralPath $entry.Key -PathType Leaf)) { throw "Night-shift control file disappeared: $($entry.Key)" }
        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $entry.Key).Hash
        if ($actual -ne $entry.Value) { throw "Night-shift control file changed during automation: $($entry.Key)" }
    }
}

function Get-ReviewEvidence {
    $status = (& git -C $repoRoot status --porcelain=v1 --untracked-files=all | Out-String)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to collect git status for QA.' }
    $diff = (& git -C $repoRoot diff HEAD --no-ext-diff --unified=3 | Out-String)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to collect git diff for QA.' }
    $untracked = @(& git -C $repoRoot ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to list untracked files for QA.' }
    $untrackedEvidence = New-Object System.Collections.Generic.List[string]
    foreach ($relative in $untracked) {
        if (-not $relative -or $relative.StartsWith('.nightshift/')) { continue }
        $full = Join-Path $repoRoot ($relative.Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
        $item = Get-Item -LiteralPath $full
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $full).Hash
        $untrackedEvidence.Add("UNTRACKED $relative bytes=$($item.Length) sha256=$hash")
        if ($item.Length -le 30000 -and $item.Extension -match '^\.(?:java|json|md|txt|csv|ps1|py|js|ts|html|css|xml|yml|yaml)$') {
            $untrackedEvidence.Add((Get-Content -Raw -LiteralPath $full))
        }
    }
    $evidence = "GIT STATUS:`n$status`nTRACKED DIFF:`n$diff`nUNTRACKED EVIDENCE:`n$($untrackedEvidence -join "`n")"
    if ($evidence.Length -gt 100000) { $evidence = $evidence.Substring(0, 100000) + "`n[REVIEW EVIDENCE TRUNCATED]" }
    $evidence
}

function Normalize-RepoRelativePath {
    param([Parameter(Mandatory)][string]$Path)
    $normalized = $Path.Replace('\', '/')
    if (-not $normalized -or
        $normalized -ne $normalized.Trim() -or
        $normalized.Length -gt 500 -or
        $normalized -match '[\x00-\x1F]' -or
        [System.IO.Path]::IsPathRooted($normalized) -or
        $normalized.StartsWith('/') -or
        $normalized.StartsWith('//') -or
        $normalized.Contains(':') -or
        $normalized.IndexOfAny([char[]]'*?[]') -ge 0) {
        throw "Path must be a literal repository-relative path: $Path"
    }
    $parts = @($normalized.Split('/'))
    if ($parts.Count -eq 0 -or @($parts | Where-Object { -not $_ -or $_ -in @('.', '..') }).Count -gt 0) {
        throw "Path contains an empty or traversal component: $Path"
    }
    $invalidChars = [IO.Path]::GetInvalidFileNameChars()
    foreach ($part in $parts) {
        if ($part -ne $part.Trim() -or $part.Length -gt 255 -or $part.EndsWith('.') -or
            $part.IndexOfAny($invalidChars) -ge 0 -or
            $part -match '(?i)^(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?$') {
            throw "Path contains an invalid or reserved Windows component: $Path"
        }
    }
    $parts -join '/'
}

function Initialize-SafeDirectory {
    param(
        [Parameter(Mandatory)][string]$TrustedRoot,
        [Parameter(Mandatory)][string]$Path
    )
    $trustedFull = [IO.Path]::GetFullPath($TrustedRoot).TrimEnd('\', '/')
    $targetFull = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $prefix = $trustedFull + [IO.Path]::DirectorySeparatorChar
    if ($targetFull -ne $trustedFull -and -not $targetFull.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Directory escapes its trusted root: $Path"
    }
    if (-not (Test-Path -LiteralPath $trustedFull -PathType Container)) { throw "Trusted root is missing: $trustedFull" }
    $relative = $targetFull.Substring($trustedFull.Length).TrimStart('\', '/')
    $cursor = $trustedFull
    foreach ($part in @($relative.Split([char[]]@('\', '/'), [StringSplitOptions]::RemoveEmptyEntries))) {
        $cursor = Join-Path $cursor $part
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Unsafe directory component (file or reparse point): $cursor"
            }
        }
        else {
            New-Item -ItemType Directory -Path $cursor | Out-Null
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "New directory became a reparse point: $cursor" }
        }
    }
}

function Assert-RepositoryPathHasNoReparse {
    param([Parameter(Mandatory)][string]$Path)
    $normalized = Normalize-RepoRelativePath -Path $Path
    $cursor = $repoRoot
    foreach ($part in $normalized.Split('/')) {
        $cursor = Join-Path $cursor $part
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Repository path contains a reparse point: $Path"
            }
        }
    }
}

function Test-PathAllowed {
    param([string]$Path, [object[]]$AllowedPaths)
    try {
        $normalized = Normalize-RepoRelativePath -Path $Path
        foreach ($allowed in $AllowedPaths) {
            $candidate = Normalize-RepoRelativePath -Path ([string]$allowed)
            if ($normalized -eq $candidate -or $normalized.StartsWith("$candidate/")) {
                return $true
            }
        }
    }
    catch { return $false }
    return $false
}

function Get-GoalAllowedPaths {
    param([string]$Goal)
    $match = [regex]::Match($Goal, '(?ms)^## Allowed scope\s*(.*?)^## ')
    if (-not $match.Success) { throw 'Goal file does not contain an Allowed scope section.' }
    @($match.Groups[1].Value -split "`r?`n" |
        Where-Object { $_ -match '^\s*-\s+`?([^`]+)`?\s*$' } |
        ForEach-Object { ([regex]::Match($_, '^\s*-\s+`?([^`]+)`?\s*$')).Groups[1].Value.Trim() })
}

function Get-GoalTestProfiles {
    param([string]$Goal)
    $match = [regex]::Match($Goal, '(?ms)^## Test profiles\s*(.*?)(?=^## |\z)')
    if (-not $match.Success) { throw 'Goal file does not contain a Test profiles section.' }
    $profiles = @($match.Groups[1].Value -split "`r?`n" | ForEach-Object {
        $bullet = [regex]::Match($_, '^\s*-\s+`?([^`\s]+)`?\s*$')
        if ($bullet.Success) { $bullet.Groups[1].Value.Trim() }
    } | Where-Object { $_ })
    if ($profiles.Count -lt 1) { throw 'The READY goal has no required test profiles.' }
    foreach ($profile in $profiles) {
        if ($config.allowedTestProfiles -notcontains $profile) { throw "Goal selected a non-allowlisted test profile: $profile" }
    }
    @($profiles | Sort-Object -Unique)
}

function Test-SafeAllowedPath {
    param([string]$Path)
    try { $normalized = Normalize-RepoRelativePath -Path $Path }
    catch { return $false }
    $broadRoots = @('src', 'data', 'scripts', 'docs', 'web', 'design', '.github', '.vscode', '.codex')
    if ($broadRoots -contains $normalized) { return $false }
    $forbidden = @(
        '.git', '.nightshift', '.github/agents', '.github/workflows', '.vscode', '.codex',
        'scripts/nightshift', 'AGENTS.md', '.gitignore',
        'docs/AI_WORKFLOW.md', 'docs/NIGHTSHIFT_WORKFLOW.md', 'docs/DAILY_GOAL.md',
        'docs/NIGHT_REPORT.md', 'docs/AI_MAILBOX.md'
    )
    foreach ($entry in $forbidden) {
        if ($normalized -eq $entry -or $normalized.StartsWith("$entry/")) { return $false }
    }
    if ($normalized -match '(?i)(^|/)(\.env(?:\.|$)|[^/]*\.(?:key|pem|pfx|p12)|credentials?(?:\.|/|$)|secrets?(?:\.|/|$))') { return $false }
    try {
        $repoPrefix = $repoRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
        $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot ($normalized.Replace('/', '\'))))
        if (-not $fullPath.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
        $cursor = $repoRoot
        foreach ($part in $normalized.Split('/')) {
            $cursor = Join-Path $cursor $part
            if (Test-Path -LiteralPath $cursor) {
                $item = Get-Item -Force -LiteralPath $cursor
                if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { return $false }
            }
        }
    }
    catch { return $false }
    return $true
}

function Protect-ProviderText {
    param(
        [AllowEmptyString()][string]$Text,
        [int]$MaximumCharacters = ([int]$config.maxProviderPromptChars)
    )
    if ($null -eq $Text) { return '' }
    $protected = $Text
    $protected = [regex]::Replace($protected, '(?is)-----BEGIN [^-]*(?:PRIVATE KEY|CERTIFICATE)-----.*?-----END [^-]*(?:PRIVATE KEY|CERTIFICATE)-----', '[REDACTED PEM BLOCK]')
    $protected = [regex]::Replace($protected, '(?i)\bBearer\s+[A-Za-z0-9._~+/=-]{8,}', 'Bearer [REDACTED]')
    $protected = [regex]::Replace($protected, '(?i)\b(?:sk|ghp|github_pat|xox[baprs])[-_][A-Za-z0-9_-]{12,}\b', '[REDACTED TOKEN]')
    $protected = [regex]::Replace($protected, '(?im)\b(api[_ -]?key|authorization|password|secret|token)\b\s*[:=]\s*["'']?[^\s"'']{8,}', '$1=[REDACTED]')
    $protected = [regex]::Replace($protected, '(?i)\b[0-9a-f]{128,}\b', '[REDACTED LONG HEX]')
    $protected = [regex]::Replace($protected, '(?<![A-Za-z0-9+/=])[A-Za-z0-9+/]{160,}={0,2}(?![A-Za-z0-9+/=])', '[REDACTED LONG BASE64]')
    if ($protected.Length -gt $MaximumCharacters) {
        $headLength = [int]($MaximumCharacters * 0.65)
        $tailLength = $MaximumCharacters - $headLength
        $protected = $protected.Substring(0, $headLength) + "`n[PROVIDER INPUT TRUNCATED]`n" + $protected.Substring($protected.Length - $tailLength)
    }
    $protected
}

function Write-UsageSnapshot {
    if (-not (Test-Path -LiteralPath $runRoot -PathType Container)) { return }
    $records = @($script:usageRecords)
    $knownPrompt = ($records | Measure-Object -Property promptTokens -Sum).Sum
    $knownCompletion = ($records | Measure-Object -Property completionTokens -Sum).Sum
    $knownTotal = ($records | Measure-Object -Property totalTokens -Sum).Sum
    Write-AtomicJson -Path (Join-Path $runRoot 'usage.json') -Value ([ordered]@{
        paidAttemptLimit = [int]$config.maxPaidModelAttempts
        paidAttemptsStarted = $script:paidModelAttempts
        knownPromptTokens = [int64]$(if ($knownPrompt) { $knownPrompt } else { 0 })
        knownCompletionTokens = [int64]$(if ($knownCompletion) { $knownCompletion } else { 0 })
        knownTotalTokens = [int64]$(if ($knownTotal) { $knownTotal } else { 0 })
        records = $records
        updatedAt = [DateTimeOffset]::Now.ToString('o')
    })
}

function Add-UsageRecord {
    param(
        [Parameter(Mandatory)][string]$Provider,
        [Parameter(Mandatory)][string]$Stage,
        [Parameter(Mandatory)][int]$Attempt,
        [Parameter(Mandatory)][string]$Status,
        [object]$Usage
    )
    $promptTokens = if ($Usage -and $Usage.PSObject.Properties.Name -contains 'prompt_tokens') { [int64]$Usage.prompt_tokens } else { 0 }
    $completionTokens = if ($Usage -and $Usage.PSObject.Properties.Name -contains 'completion_tokens') { [int64]$Usage.completion_tokens } else { 0 }
    $totalTokens = if ($Usage -and $Usage.PSObject.Properties.Name -contains 'total_tokens') { [int64]$Usage.total_tokens } else { $promptTokens + $completionTokens }
    $script:usageRecords.Add([pscustomobject]@{
        provider = $Provider
        stage = $Stage
        attempt = $Attempt
        status = $Status
        promptTokens = $promptTokens
        completionTokens = $completionTokens
        totalTokens = $totalTokens
        recordedAt = [DateTimeOffset]::Now.ToString('o')
    })
    Write-UsageSnapshot
}

function Enter-PaidModelAttempt {
    param([Parameter(Mandatory)][string]$Provider, [Parameter(Mandatory)][string]$Stage)
    if ($env:DOMINION_RELAY_DISABLE_FILE -and (Test-Path -LiteralPath $env:DOMINION_RELAY_DISABLE_FILE -PathType Leaf)) {
        throw 'Auto relay was disabled after this run started. The next paid model call was stopped.'
    }
    if ($script:paidModelAttempts -ge [int]$config.maxPaidModelAttempts) {
        throw "Nightly paid-model attempt limit ($($config.maxPaidModelAttempts)) reached before $Provider/$Stage."
    }
    $script:paidModelAttempts++
    Write-UsageSnapshot
    $script:paidModelAttempts
}

function Get-CodexUsageFromEvents {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $lastUsage = $null
    foreach ($line in Get-Content -LiteralPath $Path) {
        try {
            $event = $line | ConvertFrom-Json
            if ($event.usage) {
                $inputTokens = if ($event.usage.PSObject.Properties.Name -contains 'input_tokens') { [int64]$event.usage.input_tokens } else { 0 }
                $outputTokens = if ($event.usage.PSObject.Properties.Name -contains 'output_tokens') { [int64]$event.usage.output_tokens } else { 0 }
                $lastUsage = [pscustomobject]@{ prompt_tokens = $inputTokens; completion_tokens = $outputTokens; total_tokens = $inputTokens + $outputTokens }
            }
        }
        catch {}
    }
    $lastUsage
}

function ConvertFrom-ModelJson {
    param([Parameter(Mandatory)][string]$Text)
    $clean = $Text.Trim()
    $clean = $clean -replace '^```(?:json)?\s*', ''
    $clean = $clean -replace '\s*```$', ''
    $start = $clean.IndexOf('{')
    $end = $clean.LastIndexOf('}')
    if ($start -lt 0 -or $end -le $start) {
        throw 'Model output did not contain a JSON object.'
    }
    $clean.Substring($start, $end - $start + 1) | ConvertFrom-Json
}

function Invoke-MiniMaxJson {
    param(
        [string]$SystemPrompt,
        [string]$Message,
        [string]$OutputFile,
        [int]$TimeoutSeconds,
        [string]$Stage
    )
    $SystemPrompt = Protect-ProviderText -Text $SystemPrompt -MaximumCharacters 20000
    $Message = Protect-ProviderText -Text $Message
    $miniMaxConfigDirectory = Join-Path $env:LOCALAPPDATA 'DominionWarsAutoRelay\mmx'
    $miniMaxConfigFile = Join-Path $miniMaxConfigDirectory 'config.json'
    if (-not (Test-Path -LiteralPath $miniMaxConfigFile -PathType Leaf)) {
        throw 'The private MiniMax relay credential copy is missing. Run scripts/auto-relay/harden-minimax-auth.ps1 -Apply.'
    }
    foreach ($credentialPath in @($miniMaxConfigDirectory, $miniMaxConfigFile)) {
        $credentialItem = Get-Item -LiteralPath $credentialPath -Force
        if (($credentialItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'The private MiniMax relay credential path is a reparse point.'
        }
    }
    if (-not (Test-MiniMaxCredentialAcl)) {
        throw 'The private MiniMax relay credential ACL is not restricted to the current user, LocalSystem, and local Administrators.'
    }
    $messagesFile = "$OutputFile.messages.json"
    $messagesJson = @(
        @{ role = 'system'; content = $SystemPrompt },
        @{ role = 'user'; content = $Message }
    ) | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($messagesFile, $messagesJson, [System.Text.UTF8Encoding]::new($false))
    $arguments = @(
        'text', 'chat',
        '--model', [string]$config.pl.model,
        '--messages-file', $messagesFile,
        '--max-tokens', [string]$config.pl.maxTokens,
        '--temperature', [string]$config.pl.temperature,
        '--output', 'json',
        '--no-color', '--non-interactive',
        '--timeout', [string]([Math]::Max(1, $TimeoutSeconds - 5))
    )
    $lastReason = 'MiniMax failed.'
    for ($attempt = 1; $attempt -le [int]$config.providerMaxAttempts; $attempt++) {
        $paidAttempt = Enter-PaidModelAttempt -Provider 'MiniMax' -Stage $Stage
        $wrapper = $null
        $attemptOutput = "$OutputFile.attempt-$attempt.txt"
        $result = Invoke-CapturedCommand -Command $config.pl.command -Arguments $arguments -OutputFile $attemptOutput -TimeoutSeconds $TimeoutSeconds -Stage ("{0}_ATTEMPT_{1}" -f $Stage, $attempt) -Environment @{ MMX_CONFIG_DIR = $miniMaxConfigDirectory } -SanitizeEnvironment
        if ($result.ExitCode -eq 0 -and -not $result.OutputTruncated) {
            try {
                $wrapper = $result.Output | ConvertFrom-Json
                if (-not $wrapper.content -or -not $wrapper.content[0].text) { throw 'MiniMax response did not contain content[0].text.' }
                Write-AtomicText -Path $OutputFile -Content $result.Output
                Add-UsageRecord -Provider 'MiniMax' -Stage $Stage -Attempt $paidAttempt -Status 'SUCCESS' -Usage $wrapper.usage
                return (ConvertFrom-ModelJson -Text ([string]$wrapper.content[0].text))
            }
            catch { $lastReason = "MiniMax returned invalid JSON: $($_.Exception.Message)" }
        }
        elseif ($result.OutputTruncated) { $lastReason = 'MiniMax output exceeded the capture limit.' }
        elseif ($result.TimedOut) { $lastReason = "MiniMax timed out after $TimeoutSeconds seconds." }
        else { $lastReason = "MiniMax failed with exit code $($result.ExitCode)." }
        Add-UsageRecord -Provider 'MiniMax' -Stage $Stage -Attempt $paidAttempt -Status 'FAILED' -Usage $(if ($wrapper) { $wrapper.usage } else { $null })

        $nonRetryable = $result.Output -match '(?i)(401|403|unauthori[sz]ed|invalid.{0,12}(?:api.?key|credential)|login fail|quota exceeded|insufficient (?:balance|credit)|billing)'
        if ($nonRetryable -or $attempt -eq [int]$config.providerMaxAttempts) { break }
        $retryDelay = Get-BoundedTimeoutSeconds -RequestedSeconds ([int]$config.providerRetryBaseSeconds * $attempt)
        Start-Sleep -Seconds $retryDelay
    }
    throw "$lastReason See the per-attempt files next to $OutputFile"
}

function Get-DeepSeekCredential {
    $expanded = [Environment]::ExpandEnvironmentVariables([string]$config.qa.credentialFile)
    if (-not (Test-Path -LiteralPath $expanded)) {
        throw "DeepSeek credential is missing. Run scripts/nightshift/setup-deepseek-key.ps1 first."
    }
    # Set-Content leaves a trailing newline. ConvertTo-SecureString expects the
    # DPAPI hex payload only, so trim transport whitespace before decoding.
    $encrypted = (Get-Content -Raw -LiteralPath $expanded).Trim()
    $secure = $encrypted | ConvertTo-SecureString
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

function Test-DeepSeekCredential {
    try {
        $expanded = [Environment]::ExpandEnvironmentVariables([string]$config.qa.credentialFile)
        if (-not (Test-Path -LiteralPath $expanded)) { return $false }
        $encrypted = (Get-Content -Raw -LiteralPath $expanded).Trim()
        $secure = $encrypted | ConvertTo-SecureString
        return $secure.Length -gt 0
    }
    catch { return $false }
}

function Test-PrivateAclRuleSet {
    param(
        [Parameter(Mandatory)][object[]]$Rules,
        [Parameter(Mandatory)][string[]]$ExpectedSids,
        [Parameter(Mandatory)][Security.AccessControl.InheritanceFlags]$ExpectedInheritance,
        [switch]$AllowInherited
    )

    $allowRules = @($Rules | Where-Object { $_.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow })
    if ($allowRules.Count -ne $ExpectedSids.Count) { return $false }
    foreach ($sidValue in $ExpectedSids) {
        $matches = @($allowRules | Where-Object { $_.IdentityReference.Value -eq $sidValue })
        if ($matches.Count -ne 1) { return $false }
        $rule = $matches[0]
        $hasFullControl = ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -eq [Security.AccessControl.FileSystemRights]::FullControl
        if (-not $hasFullControl -or
            ($rule.IsInherited -and -not $AllowInherited) -or
            $rule.InheritanceFlags -ne $ExpectedInheritance -or
            $rule.PropagationFlags -ne [Security.AccessControl.PropagationFlags]::None) { return $false }
    }

    # Extra Deny rules cannot grant access and are intentionally allowed for
    # sandbox identities. A Deny against one of the three required principals
    # would make the credential or state unusable and remains invalid.
    $invalidRules = @($Rules | Where-Object {
        $_.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -and
        ($_.AccessControlType -ne [Security.AccessControl.AccessControlType]::Deny -or
         $ExpectedSids -contains $_.IdentityReference.Value)
    })
    return $invalidRules.Count -eq 0
}

function Test-MiniMaxCredentialAcl {
    try {
        $directory = Join-Path $env:LOCALAPPDATA 'DominionWarsAutoRelay\mmx'
        $file = Join-Path $directory 'config.json'
        $expectedSids = @(
            [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            'S-1-5-18'
            'S-1-5-32-544'
        )
        foreach ($entry in @(
            [pscustomobject]@{ Path = $directory; IsDirectory = $true }
            [pscustomobject]@{ Path = $file; IsDirectory = $false }
        )) {
            $pathType = if ($entry.IsDirectory) { 'Container' } else { 'Leaf' }
            if (-not (Test-Path -LiteralPath $entry.Path -PathType $pathType)) { return $false }
            $item = Get-Item -LiteralPath $entry.Path -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return $false }
            $acl = Get-Acl -LiteralPath $entry.Path
            if (-not $acl.AreAccessRulesProtected) { return $false }
            if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne $expectedSids[0]) { return $false }
            $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
            $inheritance = if ($entry.IsDirectory) {
                [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
            }
            else { [Security.AccessControl.InheritanceFlags]::None }
            if (-not (Test-PrivateAclRuleSet -Rules $rules -ExpectedSids $expectedSids -ExpectedInheritance $inheritance)) { return $false }
        }
        return $true
    }
    catch { return $false }
}

function Test-DeepSeekCredentialAcl {
    try {
        $expanded = [Environment]::ExpandEnvironmentVariables([string]$config.qa.credentialFile)
        if (-not (Test-Path -LiteralPath $expanded -PathType Leaf)) { return $false }
        $expectedSids = @(
            [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            'S-1-5-18'
            'S-1-5-32-544'
        )
        $currentSid = $expectedSids[0]
        foreach ($entry in @(
            [pscustomobject]@{ Path = (Split-Path -Parent $expanded); IsDirectory = $true }
            [pscustomobject]@{ Path = $expanded; IsDirectory = $false }
        )) {
            $item = Get-Item -LiteralPath $entry.Path -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return $false }
            $acl = Get-Acl -LiteralPath $entry.Path
            if (-not $acl.AreAccessRulesProtected) { return $false }
            if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne $currentSid) { return $false }
            $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
            $expectedInheritance = if ($entry.IsDirectory) {
                [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
            }
            else { [Security.AccessControl.InheritanceFlags]::None }
            if (-not (Test-PrivateAclRuleSet -Rules $rules -ExpectedSids $expectedSids -ExpectedInheritance $expectedInheritance)) { return $false }
        }
        return $true
    }
    catch { return $false }
}

function Test-NightShiftStateAclPrivate {
    try {
        if (-not (Test-Path -LiteralPath $stateRoot -PathType Container)) { return $false }
        $item = Get-Item -LiteralPath $stateRoot -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return $false }
        $expectedSids = @(
            [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            'S-1-5-18'
            'S-1-5-32-544'
        )
        $privateBase = Join-Path $env:LOCALAPPDATA 'DominionWarsNightshift'
        foreach ($entry in @(
            [pscustomobject]@{ Path = $privateBase; AllowInherited = $false }
            [pscustomobject]@{ Path = $stateRoot; AllowInherited = $true }
        )) {
            if (-not (Test-Path -LiteralPath $entry.Path -PathType Container)) { return $false }
            $entryItem = Get-Item -LiteralPath $entry.Path -Force
            if (($entryItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return $false }
            $acl = Get-Acl -LiteralPath $entry.Path
            if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne $expectedSids[0]) { return $false }
            if (-not $entry.AllowInherited -and -not $acl.AreAccessRulesProtected) { return $false }
            $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
            $inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
            if (-not (Test-PrivateAclRuleSet -Rules $rules -ExpectedSids $expectedSids -ExpectedInheritance $inheritance -AllowInherited:$entry.AllowInherited)) { return $false }
        }
        return $true
    }
    catch { return $false }
}

function Invoke-DeepSeekJson {
    param([string]$Message, [string]$OutputFile, [string]$Stage)
    $Message = Protect-ProviderText -Text $Message
    $key = Get-DeepSeekCredential
    try {
        $headers = @{ Authorization = "Bearer $key" }
        $body = @{
            model = [string]$config.qa.model
            temperature = [double]$config.qa.temperature
            max_tokens = [int]$config.qa.maxOutputTokens
            messages = @(
                @{ role = 'system'; content = 'You are independent QA. Return valid JSON only. Never claim tests passed unless the supplied command output proves it.' },
                @{ role = 'user'; content = $Message }
            )
        } | ConvertTo-Json -Depth 10
        # Windows PowerShell otherwise sends a JSON string using its legacy
        # request encoding, which corrupts CJK evidence and can produce invalid
        # Unicode code points at the API boundary.
        $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($body)
        $lastFailure = 'DeepSeek failed.'
        for ($attempt = 1; $attempt -le [int]$config.providerMaxAttempts; $attempt++) {
            $paidAttempt = Enter-PaidModelAttempt -Provider 'DeepSeek' -Stage $Stage
            $response = $null
            $attemptFile = "$OutputFile.attempt-$attempt.json"
            Set-RunStage -Stage ("{0}_ATTEMPT_{1}" -f $Stage, $attempt)
            try {
                $qaTimeout = Get-BoundedTimeoutSeconds -RequestedSeconds ([int]$config.timeouts.qaSeconds)
                $response = Invoke-RestMethod -Method Post -Uri $config.qa.endpoint -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $bodyBytes -TimeoutSec $qaTimeout
                Write-AtomicJson -Path $attemptFile -Value $response
                Write-AtomicJson -Path $OutputFile -Value $response
                if (-not $response.choices -or -not $response.choices[0].message.content) { throw 'DeepSeek response did not contain choices[0].message.content.' }
                $parsed = ConvertFrom-ModelJson -Text ([string]$response.choices[0].message.content)
                Add-UsageRecord -Provider 'DeepSeek' -Stage $Stage -Attempt $paidAttempt -Status 'SUCCESS' -Usage $response.usage
                return $parsed
            }
            catch {
                $lastFailure = $_.Exception.Message
                $statusCode = 0
                if ($_.Exception.Response -and $_.Exception.Response.StatusCode) { $statusCode = [int]$_.Exception.Response.StatusCode }
                if (-not (Test-Path -LiteralPath $attemptFile)) {
                    Write-AtomicJson -Path $attemptFile -Value ([ordered]@{ statusCode = $statusCode; error = (Protect-ProviderText -Text $lastFailure -MaximumCharacters 4000) })
                }
                Add-UsageRecord -Provider 'DeepSeek' -Stage $Stage -Attempt $paidAttempt -Status 'FAILED' -Usage $(if ($response) { $response.usage } else { $null })
                $nonRetryable = $statusCode -in @(400, 401, 403, 404) -or $lastFailure -match '(?i)(unauthori[sz]ed|invalid.{0,12}(?:api.?key|credential)|quota exceeded|insufficient (?:balance|credit)|billing)'
                if ($nonRetryable -or $attempt -eq [int]$config.providerMaxAttempts) { break }
                $retryDelay = Get-BoundedTimeoutSeconds -RequestedSeconds ([int]$config.providerRetryBaseSeconds * $attempt)
                Start-Sleep -Seconds $retryDelay
            }
        }
        throw "DeepSeek failed after bounded retries: $lastFailure"
    }
    finally {
        $key = $null
    }
}

function Get-TestSandboxArguments {
    param([Parameter(Mandatory)][string]$TestCommand, [string[]]$TestArguments = @())
    $permissionDefinition = @(
        'permissions.nightshift-test.description="Night test isolation"',
        'permissions.nightshift-test.filesystem={":root"="deny",":minimal"="read",":tmpdir"="deny",":slash_tmp"="deny","~/AppData/Local/DominionWarsNightshift"="deny",":workspace_roots"={"."="read","build"="write",".git"="read",".codex"="read"}}',
        'permissions.nightshift-test.network={enabled=false}'
    )
    @('sandbox', '-c', 'permissions={}', '-c', 'default_permissions="nightshift-test"') +
        @($permissionDefinition | ForEach-Object { @('-c', $_) }) +
        @('--permission-profile', 'nightshift-test', '--sandbox-state-disable-network', '-C', $repoRoot, $TestCommand) + $TestArguments
}

function Get-DeveloperPermissionDefinition {
    param([Parameter(Mandatory)][string[]]$AllowedPaths)
    $workspaceRules = New-Object System.Collections.Generic.List[string]
    $workspaceRules.Add('"." = "read"')
    $workspaceRules.Add('"nightshift-agent-tmp" = "write"')
    foreach ($path in @($AllowedPaths | ForEach-Object { Normalize-RepoRelativePath -Path ([string]$_) } | Sort-Object -Unique)) {
        $workspaceRules.Add(('"{0}" = "write"' -f $path))
    }
    @(
        'permissions.nightshift-developer.description="Write only the human-approved task paths"',
        ('permissions.nightshift-developer.filesystem={":root"="deny",":minimal"="read",":tmpdir"="deny",":slash_tmp"="deny","~/AppData/Local/DominionWarsNightshift"="deny",":workspace_roots"={' + (($workspaceRules -join ', ') -replace ' = ', '=' -replace '"', '"') + '}}'),
        'permissions.nightshift-developer.network={enabled=false}'
    )
}

function Get-DeveloperProfileProbeArguments {
    param(
        [Parameter(Mandatory)][string[]]$AllowedPaths,
        [Parameter(Mandatory)][string]$ChildCommand,
        [string[]]$ChildArguments = @()
    )
    $permissionDefinition = Get-DeveloperPermissionDefinition -AllowedPaths $AllowedPaths
    @('sandbox', '-c', 'permissions={}', '-c', 'default_permissions="nightshift-developer"') +
        @($permissionDefinition | ForEach-Object { @('-c', $_) }) +
        @('--permission-profile', 'nightshift-developer', '-C', $repoRoot, $ChildCommand) + $ChildArguments
}

function Get-DeveloperSandboxArguments {
    param(
        [Parameter(Mandatory)][string[]]$AllowedPaths,
        [Parameter(Mandatory)][string]$ScratchOutput,
        [Parameter(Mandatory)][string]$Prompt
    )
    $permissionDefinition = Get-DeveloperPermissionDefinition -AllowedPaths $AllowedPaths
    $modelArguments = @('-m', [string]$config.developer.model, '-c', ('model_reasoning_effort="{0}"' -f [string]$config.developer.reasoningEffort))
    @('exec', '--ephemeral', '--ignore-user-config', '--strict-config',
        '-c', 'permissions={}',
        '-c', 'approval_policy="never"',
        '-c', 'default_permissions="nightshift-developer"') +
        @($permissionDefinition | ForEach-Object { @('-c', $_) }) +
        @('--json', '--color', 'never') + $modelArguments + @('-C', $repoRoot, '-o', $ScratchOutput, $Prompt)
}

function Invoke-TestProfile {
    param([string]$Profile, [string]$OutputFile, [string]$Stage)
    if ($env:DOMINION_RELAY_DISABLE_FILE -and (Test-Path -LiteralPath $env:DOMINION_RELAY_DISABLE_FILE -PathType Leaf)) {
        throw 'Auto relay was disabled after this run started. The next test stage was stopped.'
    }
    $testCommand = ''
    $testArguments = @()
    switch ($Profile) {
        'build' {
            $testCommand = Join-Path $repoRoot 'scripts\build.bat'
        }
        'regression' {
            $testCommand = 'java'
            $testArguments = @('-cp', 'build/classes;build/test-classes', 'com.dominionwars.test.TestMain')
        }
        'sanity' {
            $testCommand = $pythonCommand
            $testArguments = @('scripts/sanity_check_v2.py')
        }
        'alignment' {
            $testCommand = $pythonCommand
            $testArguments = @('scripts/align_check.py')
        }
        'nightshift-index' {
            $testCommand = 'powershell'
            $testArguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', 'scripts/nightshift/verify-nightshift-index.ps1')
        }
        default { throw "Test profile is not allowlisted: $Profile" }
    }
    if ($Simulation) {
        return Invoke-CapturedCommand -Command $testCommand -Arguments $testArguments -OutputFile $OutputFile -TimeoutSeconds ([int]$config.timeouts.testSeconds) -Stage $Stage
    }

    Initialize-SafeDirectory -TrustedRoot $repoRoot -Path $testTempRoot
    foreach ($repositoryPath in @('build', 'scripts/build.bat', 'scripts/sanity_check_v2.py', 'scripts/align_check.py', 'scripts/nightshift/verify-nightshift-index.ps1')) {
        if (Test-Path -LiteralPath (Join-Path $repoRoot ($repositoryPath.Replace('/', '\')))) {
            Assert-RepositoryPathHasNoReparse -Path $repositoryPath
        }
    }
    # Run repository code under Codex's restricted Windows token with direct
    # network access disabled. The parent process retains no plaintext QA key.
    $sandboxArguments = @(Get-TestSandboxArguments -TestCommand $testCommand -TestArguments $testArguments)
    $result = Invoke-CapturedCommand -Command $config.developer.command -Arguments $sandboxArguments -OutputFile $OutputFile -TimeoutSeconds ([int]$config.timeouts.testSeconds) -Stage $Stage -Environment @{ TEMP = $testTempRoot; TMP = $testTempRoot; TMPDIR = $testTempRoot } -SanitizeEnvironment
    Initialize-SafeDirectory -TrustedRoot $repoRoot -Path $testTempRoot
    $result
}

function Get-TestProfileCommandText {
    param([Parameter(Mandatory)][string]$Profile)
    switch ($Profile) {
        'build' { 'scripts/build.bat' }
        'regression' { 'java -cp build/classes;build/test-classes com.dominionwars.test.TestMain' }
        'sanity' { 'python scripts/sanity_check_v2.py' }
        'alignment' { 'python scripts/align_check.py' }
        'nightshift-index' { 'powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts/nightshift/verify-nightshift-index.ps1' }
        default { "unknown profile: $Profile" }
    }
}

function Write-NightReport {
    param([object]$State, [object]$FinalReview)
    $lines = @(
        '# Night Report',
        '',
        "Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss K')",
        "Run: $runId",
        "Final status: $($FinalReview.status)",
        '',
        '## Goal',
        '',
        [string]$State.nightGoal,
        '',
        '## Tasks',
        ''
    )
    foreach ($task in $State.tasks) {
        $lines += "- [$($task.status)] $($task.id): $($task.title) (repair cycles: $($task.repairCycles))"
        if ($task.qaSummary) { $lines += "  - QA: $($task.qaSummary)" }
        if ($task.blocker) { $lines += "  - Blocker: $($task.blocker)" }
        foreach ($test in @($task.testResults)) {
            $lines += "  - Test cycle $($test.cycle): ``$($test.profile)`` exit=$($test.exitCode), timedOut=$($test.timedOut), truncated=$($test.truncated)"
            $lines += "    - Command: ``$($test.command)``"
            $lines += "    - Evidence: $($test.evidenceFile)"
        }
        foreach ($finding in @($task.qaFindings)) {
            $lines += "  - QA finding $($finding.severity): $($finding.problem)"
            if ($finding.reproduction) { $lines += "    - Reproduction: $($finding.reproduction)" }
        }
    }
    $knownPromptTokens = (@($script:usageRecords) | Measure-Object -Property promptTokens -Sum).Sum
    $knownCompletionTokens = (@($script:usageRecords) | Measure-Object -Property completionTokens -Sum).Sum
    $knownTotalTokens = (@($script:usageRecords) | Measure-Object -Property totalTokens -Sum).Sum
    $lines += @(
        '', '## PL review', '', [string]$FinalReview.summary,
        '', '## Model usage', '',
        "- Paid attempts started: $($script:paidModelAttempts) / $($config.maxPaidModelAttempts)",
        "- Known prompt tokens: $(if ($knownPromptTokens) { $knownPromptTokens } else { 0 })",
        "- Known completion tokens: $(if ($knownCompletionTokens) { $knownCompletionTokens } else { 0 })",
        "- Known total tokens: $(if ($knownTotalTokens) { $knownTotalTokens } else { 0 })",
        "- Detailed live ledger: $(Join-Path $runRoot 'usage.json')",
        '', '## Human decisions required', ''
    )
    $humanTasks = @($State.tasks | Where-Object { $_.status -eq 'HUMAN_REQUIRED' })
    if ($humanTasks.Count -eq 0) { $lines += '- None.' }
    else { foreach ($task in $humanTasks) { $lines += "- $($task.id): $($task.blocker)" } }
    Write-AtomicText -Path $reportPath -Content ($lines -join [Environment]::NewLine)
}

function Write-EmergencyReport {
    param([Parameter(Mandatory)][string]$Message)
    $state = if ($script:plan) {
        $script:plan
    }
    else {
        [pscustomobject]@{ nightGoal = 'The run stopped before a valid plan was produced.'; tasks = @() }
    }
    $review = [pscustomobject]@{
        status = 'HUMAN_REQUIRED'
        summary = "Night shift stopped safely at stage '$($script:currentStage)': $Message"
    }
    Write-NightReport -State $state -FinalReview $review
}

function Enter-NightShiftLock {
    if (-not (Test-Path -LiteralPath $stateRoot -PathType Container)) {
        New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
    }
    try {
        $script:runLock = [System.IO.File]::Open(
            $lockPath,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None
        )
        $lockText = [System.Text.Encoding]::UTF8.GetBytes("run=$runId pid=$PID started=$(Get-Date -Format 'o')")
        $script:runLock.SetLength(0)
        $script:runLock.Write($lockText, 0, $lockText.Length)
        $script:runLock.Flush()
    }
    catch [System.IO.IOException] {
        if ($Scheduled) {
            Add-SchedulerLog -Message 'SKIPPED another night-shift process owns the run lock'
            Write-Host 'Another night shift is already running; this scheduled launch was skipped.'
            exit 0
        }
        throw 'Another night-shift process is already running.'
    }
}

function Exit-NightShiftLock {
    if ($script:runLock) {
        try { $script:runLock.Dispose() } catch {}
        $script:runLock = $null
    }
}

function Set-NightShiftAwake {
    param([Parameter(Mandatory)][bool]$Enabled)
    try {
        if (-not ('DominionWarsNightShift.NativePower' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace DominionWarsNightShift {
    public static class NativePower {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint SetThreadExecutionState(uint flags);
    }
}
'@
        }
        $flags = if ($Enabled) { [Convert]::ToUInt32('80000001', 16) } else { [Convert]::ToUInt32('80000000', 16) }
        $result = [DominionWarsNightShift.NativePower]::SetThreadExecutionState($flags)
        if ($result -eq 0) { throw "SetThreadExecutionState returned Win32 error $([Runtime.InteropServices.Marshal]::GetLastWin32Error())." }
    }
    catch {
        if ($Enabled) { throw "Unable to prevent sleep during the night shift: $($_.Exception.Message)" }
        if ($Scheduled) { try { Add-SchedulerLog -Message "WARNING failed to clear execution-state request: $($_.Exception.Message)" } catch {} }
    }
}

function Assert-PlanValid {
    param([Parameter(Mandatory)][object]$CandidatePlan, [Parameter(Mandatory)][object[]]$GoalPaths)
    $tasks = @($CandidatePlan.tasks)
    if (-not $CandidatePlan.nightGoal -or $tasks.Count -lt 1) { throw 'PL returned an empty goal or task list.' }
    if (([string]$CandidatePlan.nightGoal).Length -gt 2000) { throw 'PL nightGoal is too long.' }
    $taskLimit = if ($Simulation) { 2 } else { [int]$config.maxTasks }
    if ($tasks.Count -gt $taskLimit) { throw 'PL returned too many tasks.' }

    $ids = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($task in $tasks) {
        $id = [string]$task.id
        if ($id -notmatch '^[A-Z][A-Z0-9_-]{0,31}$') { throw "Invalid task id: $id" }
        if (-not $ids.Add($id)) { throw "Duplicate task id: $id" }
        foreach ($reserved in @('status', 'repairCycles', 'qaSummary', 'qaFindings', 'testResults', 'blocker')) {
            if ($task.PSObject.Properties.Name -contains $reserved) { throw "Task $id attempted to set reserved state field: $reserved" }
        }
        if (-not [string]$task.title -or ([string]$task.title).Length -gt 200) { throw "Task $id has an empty or overlong title." }
        if ($task.humanRequired -isnot [bool]) { throw "Task $id humanRequired must be a JSON boolean." }
        $criteria = @($task.acceptanceCriteria | Where-Object { [string]$_ })
        if ($criteria.Count -lt 1 -or $criteria.Count -gt 20 -or @($criteria | Where-Object { ([string]$_).Length -gt 1000 }).Count -gt 0) { throw "Task $id has invalid acceptance criteria." }
        if (@($task.dependencies).Count -gt [int]$config.maxTasks) { throw "Task $id has too many dependencies." }
        if (@($task.allowedPaths).Count -gt 20) { throw "Task $id has too many allowed paths." }
        if (@($task.testProfiles).Count -gt @($config.allowedTestProfiles).Count) { throw "Task $id has too many test profiles." }
        if ([string]$task.risk -notin @('low', 'medium', 'high')) { throw "Task $id has invalid risk: $($task.risk)" }
        if (([string]$task.humanQuestion).Length -gt 1000) { throw "Task $id has an overlong human question." }
        if (-not $task.humanRequired) {
            if ([string]$task.kind -ne 'implementation') { throw "PL returned a non-implementation executable task: $id" }
            if (@($task.allowedPaths).Count -lt 1) { throw "Executable task $id has no allowed paths." }
            if (@($task.testProfiles).Count -lt 1) { throw "Executable task $id has no test profile." }
        }
        foreach ($profile in @($task.testProfiles)) {
            if ($config.allowedTestProfiles -notcontains [string]$profile) { throw "PL selected a non-allowlisted test profile: $profile" }
        }
        if (@($task.testProfiles) -contains 'regression' -and @($task.testProfiles) -notcontains 'build') {
            throw "Task $id selects regression without the required clean build profile."
        }
        foreach ($path in @($task.allowedPaths)) {
            if (-not (Test-SafeAllowedPath -Path ([string]$path))) { throw "PL selected an unsafe path: $path" }
            if (-not (Test-PathAllowed -Path ([string]$path) -AllowedPaths $GoalPaths)) {
                throw "PL selected a path outside the human-approved scope: $path"
            }
        }
    }

    $selectedProfiles = @($tasks | Where-Object { -not [bool]$_.humanRequired } | ForEach-Object { @($_.testProfiles) } | ForEach-Object { [string]$_ } | Sort-Object -Unique)
    foreach ($requiredProfile in $script:goalTestProfiles) {
        if ($selectedProfiles -notcontains [string]$requiredProfile) { throw "PL omitted human-required test profile: $requiredProfile" }
    }

    foreach ($task in $tasks) {
        foreach ($dependency in @($task.dependencies)) {
            $dependencyId = [string]$dependency
            if ($dependencyId -eq [string]$task.id) { throw "Task $($task.id) depends on itself." }
            if (-not $ids.Contains($dependencyId)) { throw "Task $($task.id) has unknown dependency: $dependencyId" }
        }
    }

    $remaining = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($id in $ids) { $null = $remaining.Add($id) }
    while ($remaining.Count -gt 0) {
        $removable = @($tasks | Where-Object {
            $taskId = [string]$_.id
            $remaining.Contains($taskId) -and @($_.dependencies | Where-Object { $remaining.Contains([string]$_) }).Count -eq 0
        } | ForEach-Object { [string]$_.id })
        if ($removable.Count -eq 0) { throw 'PL task dependencies contain a cycle.' }
        foreach ($id in $removable) { $null = $remaining.Remove($id) }
    }
}

function Resolve-FinalStatus {
    param(
        [Parameter(Mandatory)][ValidateSet('PL_APPROVED', 'PARTIAL', 'HUMAN_REQUIRED')][string]$EvidenceStatus,
        [Parameter(Mandatory)][ValidateSet('PL_APPROVED', 'PARTIAL', 'HUMAN_REQUIRED')][string]$PlanningLeadStatus
    )
    $rank = @{ PL_APPROVED = 0; PARTIAL = 1; HUMAN_REQUIRED = 2 }
    if ($rank[$PlanningLeadStatus] -gt $rank[$EvidenceStatus]) { return $PlanningLeadStatus }
    $EvidenceStatus
}

if ($SafetySelfTest) {
    $deniedPaths = @(
        '.git', './.git', '.nightshift', '../outside', 'C:\Windows', '\\server\share', '/root',
        'docs', 'scripts', 'src', '.codex/config.toml', 'scripts/nightshift/run-nightshift.ps1', '.github/agents/evil.agent.md',
        'docs/AI_MAILBOX.md', 'src/*', "docs/control`nfile.md", 'docs/file.md:stream', 'CON', 'data/NUL.json', 'docs/trailing.', 'docs/trailing '
    )
    $acceptedPaths = @('src/com/dominionwars/example', 'data/cards/example.json', 'docs/FILE_INDEX.md')
    $unexpectedAllowed = @($deniedPaths | Where-Object { Test-SafeAllowedPath -Path $_ })
    $unexpectedDenied = @($acceptedPaths | Where-Object { -not (Test-SafeAllowedPath -Path $_) })
    if ($unexpectedAllowed.Count -gt 0) { throw "Unsafe paths passed validation: $($unexpectedAllowed -join ', ')" }
    if ($unexpectedDenied.Count -gt 0) { throw "Safe paths were rejected: $($unexpectedDenied -join ', ')" }

    $script:goalTestProfiles = @('nightshift-index')
    $badIdPlan = @'
{"nightGoal":"test","tasks":[{"id":"../../ESCAPE","kind":"implementation","title":"bad id","acceptanceCriteria":["x"],"dependencies":[],"allowedPaths":["docs/FILE_INDEX.md"],"testProfiles":["nightshift-index"],"risk":"low","humanRequired":false,"humanQuestion":""}]}
'@ | ConvertFrom-Json
    $badBooleanPlan = @'
{"nightGoal":"test","tasks":[{"id":"SAFE","kind":"implementation","title":"bad bool","acceptanceCriteria":["x"],"dependencies":[],"allowedPaths":["docs/FILE_INDEX.md"],"testProfiles":["nightshift-index"],"risk":"low","humanRequired":"false","humanQuestion":""}]}
'@ | ConvertFrom-Json
    foreach ($case in @($badIdPlan, $badBooleanPlan)) {
        $rejected = $false
        try { Assert-PlanValid -CandidatePlan $case -GoalPaths @('docs/FILE_INDEX.md') }
        catch { $rejected = $true }
        if (-not $rejected) { throw 'Malformed plan unexpectedly passed validation.' }
    }
    $finalStatusCases = @(
        [pscustomobject]@{ Evidence = 'PL_APPROVED'; PL = 'PL_APPROVED'; Expected = 'PL_APPROVED' }
        [pscustomobject]@{ Evidence = 'PL_APPROVED'; PL = 'PARTIAL'; Expected = 'PARTIAL' }
        [pscustomobject]@{ Evidence = 'PL_APPROVED'; PL = 'HUMAN_REQUIRED'; Expected = 'HUMAN_REQUIRED' }
        [pscustomobject]@{ Evidence = 'PARTIAL'; PL = 'PL_APPROVED'; Expected = 'PARTIAL' }
        [pscustomobject]@{ Evidence = 'PARTIAL'; PL = 'PARTIAL'; Expected = 'PARTIAL' }
        [pscustomobject]@{ Evidence = 'PARTIAL'; PL = 'HUMAN_REQUIRED'; Expected = 'HUMAN_REQUIRED' }
        [pscustomobject]@{ Evidence = 'HUMAN_REQUIRED'; PL = 'PL_APPROVED'; Expected = 'HUMAN_REQUIRED' }
        [pscustomobject]@{ Evidence = 'HUMAN_REQUIRED'; PL = 'PARTIAL'; Expected = 'HUMAN_REQUIRED' }
        [pscustomobject]@{ Evidence = 'HUMAN_REQUIRED'; PL = 'HUMAN_REQUIRED'; Expected = 'HUMAN_REQUIRED' }
    )
    foreach ($case in $finalStatusCases) {
        $actual = Resolve-FinalStatus -EvidenceStatus $case.Evidence -PlanningLeadStatus $case.PL
        if ($actual -ne $case.Expected) { throw "Unsafe final status resolution: $($case.Evidence) + $($case.PL) expected $($case.Expected), produced $actual" }
    }
    $developerControlArgs = @(Get-DeveloperSandboxArguments -AllowedPaths @('docs/FILE_INDEX.md') -ScratchOutput (Join-Path $agentTempRoot 'self-test.txt') -Prompt 'self-test')
    $developerControlText = $developerControlArgs -join ' '
    if ($developerControlText -match '(?:--dangerously-bypass|--approve-for-me|--sandbox\s)') {
        throw 'Developer invocation unexpectedly selected a bypass, legacy sandbox, or auto-escalation flag.'
    }
    foreach ($requiredControl in @('approval_policy="never"', 'default_permissions="nightshift-developer"', 'permissions.nightshift-developer.network={enabled=false}')) {
        if (-not $developerControlText.Contains($requiredControl)) { throw "Developer invocation omitted safety control: $requiredControl" }
    }
    [pscustomobject]@{
        Status = 'PASS'
        DeniedPathAttacks = $deniedPaths.Count
        AcceptedPathControls = $acceptedPaths.Count
        RejectedMalformedPlans = 2
        VerifiedFinalStatusCombinations = $finalStatusCases.Count
        VerifiedDeveloperProfileControls = 3
    } | ConvertTo-Json
    exit 0
}

Set-Location $repoRoot
trap {
    $failureMessage = $_.Exception.Message
    [Console]::Error.WriteLine("Night shift error at $($script:currentStage): $failureMessage")
    if ($script:runStarted) {
        try { Write-EmergencyReport -Message $failureMessage } catch {}
        try { Update-RunLedger -Status 'FAILED' -Message $failureMessage } catch {}
    }
    if ($Scheduled) { try { Add-SchedulerLog -Message "FAILED stage=$($script:currentStage) message=$failureMessage report=$reportPath" } catch {} }
    if ($Scheduled) { try { Write-SchedulerStatus -Status 'FAILED' -Message $failureMessage -ExitCode 1 } catch {} }
    try { Set-NightShiftAwake -Enabled $false } catch {}
    Exit-NightShiftLock
    exit 1
}

$normalizedGoalFile = ''
if ([IO.Path]::IsPathRooted($GoalFile)) {
    if ($Simulation) { throw 'Simulation mode does not accept an absolute goal file.' }
    $goalPath = [IO.Path]::GetFullPath($GoalFile)
    $expectedGoal = if ($Scheduled) {
        if (-not $ApprovedScheduledGoal) { throw 'Scheduled private goals require -ApprovedScheduledGoal.' }
        [IO.Path]::GetFullPath((Join-Path $privateStateRoot 'scheduled-goal.md'))
    }
    else {
        if ($ApprovedScheduledGoal) { throw '-ApprovedScheduledGoal requires -Scheduled.' }
        [IO.Path]::GetFullPath((Join-Path $privateStateRoot 'day-goal.md'))
    }
    if (-not $goalPath.Equals($expectedGoal, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Automation accepts an absolute goal only at its dedicated private-state goal path.'
    }
    Initialize-SafeDirectory -TrustedRoot $env:LOCALAPPDATA -Path $privateStateRoot
    $normalizedGoalFile = if ($Scheduled) { '<private-state>/scheduled-goal.md' } else { '<private-state>/day-goal.md' }
}
else {
    if ($ApprovedScheduledGoal) { throw '-ApprovedScheduledGoal requires the private scheduled goal path.' }
    $normalizedGoalFile = Normalize-RepoRelativePath -Path $GoalFile
    if ($Scheduled -and $normalizedGoalFile -ne 'docs/DAILY_GOAL.md') { throw 'Scheduled mode only accepts docs/DAILY_GOAL.md.' }
    $goalPath = Join-Path $repoRoot ($normalizedGoalFile.Replace('/', '\'))
}
if (-not (Test-Path -LiteralPath $goalPath)) { throw "Goal file not found: $goalPath" }
$goalItem = Get-Item -LiteralPath $goalPath -Force
if (($goalItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Goal file must not be a reparse point: $goalPath" }
$goalText = Get-Content -Raw -LiteralPath $goalPath
if ($goalText.Length -gt [int]$config.maxGoalChars) { throw "Goal file exceeds the $($config.maxGoalChars)-character limit." }
$ready = $Simulation -or ($goalText -match '(?m)^Status:\s*READY\s*$')
if (-not $ready) {
    # The committed template intentionally contains placeholder prose that is
    # not a valid path. DRAFT must short-circuit before scope parsing so both
    # dry-run and scheduled no-op behavior stay predictable.
    $goalAllowedPaths = @()
    $script:goalTestProfiles = @()
}
else {
    $goalAllowedPaths = if ($Simulation) { @('docs/FILE_INDEX.md') } else { @(Get-GoalAllowedPaths -Goal $goalText) }
    $goalAllowedPaths = @($goalAllowedPaths | ForEach-Object { Normalize-RepoRelativePath -Path ([string]$_) })
    $script:goalTestProfiles = if ($Simulation) { @('nightshift-index') } else { @(Get-GoalTestProfiles -Goal $goalText) }
}

$branchOutput = @(& git -C $repoRoot branch --show-current)
if ($LASTEXITCODE -ne 0) { throw 'Unable to determine the current Git branch.' }
$branch = ($branchOutput -join '').Trim()
if (-not $branch) { throw 'Current Git branch is empty or detached.' }
$changedBefore = @(Get-GitChangedPaths)

if ($DryRun) {
    [pscustomobject]@{
        Mode = 'DRY_RUN'
        SimulationScenario = $SimulationScenario
        Repository = $repoRoot
        Branch = $branch
        GoalReady = $ready
        ExistingChanges = $changedBefore
        GoalAllowedPaths = $goalAllowedPaths
        GoalTestProfiles = $script:goalTestProfiles
        MiniMax = (Get-Command $config.pl.command).Source
        Codex = (Get-Command $config.developer.command).Source
        Python = $pythonCommand
        MiniMaxCredentialAclPrivate = Test-MiniMaxCredentialAcl
        DeepSeekCredentialConfigured = Test-DeepSeekCredential
        DeepSeekCredentialAclPrivate = Test-DeepSeekCredentialAcl
        NightShiftStateAclPrivate = Test-NightShiftStateAclPrivate
        MaxTasks = $config.maxTasks
        MaxRepairCycles = $config.maxRepairCycles
        Timeouts = $config.timeouts
        MaxCapturedOutputChars = $config.maxCapturedOutputChars
    } | ConvertTo-Json -Depth 5
    exit 0
}

$stateTrustedRoot = if ($Simulation) { $repoRoot } else { $env:LOCALAPPDATA }
Initialize-SafeDirectory -TrustedRoot $stateTrustedRoot -Path $stateRoot
if (-not $Simulation -and -not (Test-DeepSeekCredentialAcl)) {
    throw 'DeepSeek credential ACL is not private. Run scripts/nightshift/setup-deepseek-key.ps1 -HardenOnly before unattended execution.'
}
if (-not $Simulation -and -not (Test-MiniMaxCredentialAcl)) {
    throw 'MiniMax credential ACL is not private. Run scripts/auto-relay/harden-minimax-auth.ps1 -Apply before unattended execution.'
}
if (-not $Simulation -and -not (Test-NightShiftStateAclPrivate)) {
    throw 'Night-shift private state ACL is not restricted to the current user, LocalSystem, and local Administrators.'
}

if (-not $ready) {
    if ($Scheduled) {
        Add-SchedulerLog -Message 'SKIPPED no READY daily goal'
        Write-SchedulerStatus -Status 'SKIPPED_NO_READY_GOAL' -Message 'docs/DAILY_GOAL.md is not READY.'
        Write-Host 'No READY daily goal; scheduled night shift skipped.'
        exit 0
    }
    throw 'docs/DAILY_GOAL.md is not READY.'
}

if ($RetryGoal -and $Scheduled) { throw '-RetryGoal is manual-only and cannot be combined with -Scheduled.' }
if ($Scheduled -and -not $Simulation) {
    $now = Get-Date
    $goalAgeHours = ($now - (Get-Item -LiteralPath $goalPath).LastWriteTime).TotalHours
    $goalDateMatch = [regex]::Match($goalText, '(?m)^Date:\s*(\d{4}-\d{2}-\d{2})\s*$')
    $validGoalDates = @($now.Date, $now.Date.AddDays(-1)) | ForEach-Object { $_.ToString('yyyy-MM-dd') }
    $skipReason = ''
    if ($now.Hour -ge [int]$config.scheduledLatestStartHour) { $skipReason = "Current hour $($now.Hour) is outside the unattended start window." }
    elseif ($goalAgeHours -gt [int]$config.goalMaxAgeHours) { $skipReason = "READY goal is $([Math]::Round($goalAgeHours, 1)) hours old." }
    elseif (-not $goalDateMatch.Success -or $validGoalDates -notcontains $goalDateMatch.Groups[1].Value) { $skipReason = 'Goal Date must be today or yesterday for a scheduled run.' }
    if ($skipReason) {
        Add-SchedulerLog -Message "SKIPPED stale or out-of-window goal: $skipReason"
        Write-SchedulerStatus -Status 'SKIPPED_STALE_OR_OUT_OF_WINDOW' -Message $skipReason
        Write-Host $skipReason
        exit 0
    }
}
Enter-NightShiftLock
$goalHashInput = if ($Simulation) { "SIMULATION:$SimulationScenario`n$goalText" } else { $goalText }
$script:goalHash = Get-TextSha256 -Text $goalHashInput

if (-not $RetryGoal) {
    $previousRun = Get-RunLedgerEntry -GoalHash $script:goalHash
    if ($previousRun) {
        if ([string]$previousRun.status -eq 'RUNNING') {
            $script:runStarted = $true
            $script:currentStage = 'INTERRUPTED_PREVIOUS_RUN'
            $message = "Previous run $($previousRun.runId) stopped without a final state. Review its worktree changes before retrying manually with -RetryGoal."
            Write-EmergencyReport -Message $message
            Update-RunLedger -Status 'INTERRUPTED' -Message $message
            if ($Scheduled) { Add-SchedulerLog -Message "INTERRUPTED previousRun=$($previousRun.runId) report=$reportPath" }
            if ($Scheduled) { Write-SchedulerStatus -Status 'INTERRUPTED_PREVIOUS_RUN' -Message $message -ExitCode 2 }
            Exit-NightShiftLock
            Write-Host $message
            exit 2
        }
        $message = "This exact goal was already settled by run $($previousRun.runId) with status $($previousRun.status). Change the goal or retry manually with -RetryGoal after review."
        if ($Scheduled) {
            Add-SchedulerLog -Message "SKIPPED duplicate goal previousRun=$($previousRun.runId) status=$($previousRun.status)"
            Write-SchedulerStatus -Status 'SKIPPED_DUPLICATE_GOAL' -Message $message
            Exit-NightShiftLock
            Write-Host $message
            exit 0
        }
        throw $message
    }
}

New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$script:runStarted = $true
Write-UsageSnapshot
$script:deadlineUtc = [DateTime]::UtcNow.AddSeconds([int]$config.totalRunSeconds)
Set-NightShiftAwake -Enabled $true
if ($Scheduled) { Add-SchedulerLog -Message "STARTED run=$runId goal=$($script:goalHash) branch=$branch" }
if ($Scheduled) { Write-SchedulerStatus -Status 'RUNNING' -Message "Run started on branch $branch." }
Set-RunStage -Stage 'PREFLIGHT'

$requiredCommands = if ($Simulation) { @('git', 'powershell') } else { @('git', 'powershell', [string]$config.pl.command, [string]$config.developer.command) }
if (@($script:goalTestProfiles | Where-Object { $_ -in @('build', 'regression') }).Count -gt 0) { $requiredCommands += 'java' }
foreach ($command in $requiredCommands) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Required command not found: $command" }
}
if (@($script:goalTestProfiles | Where-Object { $_ -in @('sanity', 'alignment') }).Count -gt 0 -and -not $pythonCommand) { throw 'Required Python interpreter not found.' }
if ($goalAllowedPaths.Count -eq 0) { throw 'The READY goal has no allowed paths.' }
foreach ($path in $goalAllowedPaths) {
    if (-not (Test-SafeAllowedPath -Path $path)) { throw "Unsafe allowed path in goal: $path" }
}
if (-not $Simulation -and $config.protectedBranches -contains $branch) { throw "Live night shift is forbidden on protected branch: $branch" }
if (-not $Simulation -and $changedBefore.Count -gt 0) { throw 'Live night shift requires a clean worktree.' }
if (-not $Simulation -and -not $branch.StartsWith('agents/nightshift-', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Live night shift requires an isolated agents/nightshift-* branch, not: $branch"
}
if (-not $Simulation) {
    if (-not (Test-DeepSeekCredentialAcl)) {
        throw 'DeepSeek credential ACL is not private. Run scripts/nightshift/setup-deepseek-key.ps1 -HardenOnly before unattended execution.'
    }
    $null = Get-DeepSeekCredential
    $script:startingBranch = $branch
    $startingHeadOutput = @(& git -C $repoRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to record the starting Git commit.' }
    $script:startingHead = ($startingHeadOutput -join '').Trim()
    if (-not $script:startingHead) { throw 'Starting Git commit is empty.' }
    foreach ($controlFile in @($MyInvocation.MyCommand.Path, $configPath, $commandHelper, $scheduledLauncher, (Join-Path $repoRoot '.codex\config.toml'))) {
        $script:controlPlaneHashes[$controlFile] = (Get-FileHash -Algorithm SHA256 -LiteralPath $controlFile).Hash
    }
    if ((Get-WorkspaceFreeBytes) -lt 2GB) { throw 'Night shift requires at least 2 GB free disk space.' }

    Initialize-SafeDirectory -TrustedRoot $repoRoot -Path $testTempRoot
    Initialize-SafeDirectory -TrustedRoot $repoRoot -Path $agentTempRoot
    $probeOutput = Join-Path $runRoot 'sandbox-probe.txt'
    $probeArguments = @(Get-TestSandboxArguments -TestCommand (Join-Path $env:SystemRoot 'System32\cmd.exe') -TestArguments @('/d', '/c', 'exit 0'))
    $probe = Invoke-CapturedCommand -Command $config.developer.command -Arguments $probeArguments -OutputFile $probeOutput -TimeoutSeconds 60 -Stage 'SANDBOX_PREFLIGHT' -Environment @{ TEMP = $testTempRoot; TMP = $testTempRoot; TMPDIR = $testTempRoot } -SanitizeEnvironment
    if ($probe.ExitCode -ne 0) { throw "Restricted test sandbox preflight failed. See $probeOutput" }

    $developerAllowedProbe = Join-Path $agentTempRoot 'developer-write-allowed.probe'
    $developerDeniedProbe = Join-Path $repoRoot 'scripts\nightshift\developer-write-denied.probe'
    foreach ($probePath in @($developerAllowedProbe, $developerDeniedProbe)) {
        if (Test-Path -LiteralPath $probePath) { Remove-Item -LiteralPath $probePath -Force }
    }
    $cmdExe = Join-Path $env:SystemRoot 'System32\cmd.exe'
    $allowedProbeArgs = @(Get-DeveloperProfileProbeArguments -AllowedPaths $goalAllowedPaths -ChildCommand $cmdExe -ChildArguments @('/d', '/c', 'echo OK>nightshift-agent-tmp\developer-write-allowed.probe'))
    $allowedProbe = Invoke-CapturedCommand -Command $config.developer.command -Arguments $allowedProbeArgs -OutputFile (Join-Path $runRoot 'developer-sandbox-allowed.txt') -TimeoutSeconds 60 -Stage 'DEVELOPER_SANDBOX_ALLOW_PROBE' -Environment @{ TEMP = $agentTempRoot; TMP = $agentTempRoot; TMPDIR = $agentTempRoot } -SanitizeEnvironment
    if ($allowedProbe.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $developerAllowedProbe -PathType Leaf)) {
        throw 'Developer sandbox could not write its dedicated scratch path.'
    }
    Remove-Item -LiteralPath $developerAllowedProbe -Force
    $deniedProbeArgs = @(Get-DeveloperProfileProbeArguments -AllowedPaths $goalAllowedPaths -ChildCommand $cmdExe -ChildArguments @('/d', '/c', 'echo BLOCKED>scripts\nightshift\developer-write-denied.probe'))
    $deniedProbe = Invoke-CapturedCommand -Command $config.developer.command -Arguments $deniedProbeArgs -OutputFile (Join-Path $runRoot 'developer-sandbox-denied.txt') -TimeoutSeconds 60 -Stage 'DEVELOPER_SANDBOX_DENY_PROBE' -Environment @{ TEMP = $agentTempRoot; TMP = $agentTempRoot; TMPDIR = $agentTempRoot } -SanitizeEnvironment
    if (Test-Path -LiteralPath $developerDeniedProbe) {
        Remove-Item -LiteralPath $developerDeniedProbe -Force
        throw 'Developer sandbox wrote an automation control path that should be denied.'
    }
    if ($deniedProbe.ExitCode -eq 0) { throw 'Developer sandbox denial probe returned success unexpectedly.' }
}

$plannerSystem = @'
You are the Dominion Wars temporary PL. Produce a bounded plan from the human-approved goal.
If a task needs the regression profile, include build as well; the runner always executes a clean build before regression.
Return JSON only with this shape:
{"nightGoal":"...","tasks":[{"id":"T1","kind":"implementation","title":"...","acceptanceCriteria":["..."],"dependencies":[],"allowedPaths":["path"],"testProfiles":["build"],"risk":"low","humanRequired":false,"humanQuestion":""}]}
Every executable task must have "kind":"implementation" and must create an observable deliverable in allowedPaths. Reading, research, and verification are steps inside an implementation task, never separate tasks. For one small file goal, return exactly one task. Copy allowedPaths only from the goal's Allowed scope section; never broaden them. Test profiles may only be build, regression, sanity, alignment, nightshift-index. Never authorize commits, pushes, merges, releases, credentials, paid resources, destructive deletion, or unapproved architecture decisions.
'@
if ($Simulation) {
    $plan = @'
{
  "nightGoal": "Exercise repair, independent-task continuation, human escalation, and final approval guards.",
  "tasks": [
    {
      "id": "SIM-REPAIR",
      "kind": "implementation",
      "title": "Simulate one QA failure followed by a passing repair",
      "acceptanceCriteria": ["First QA cycle fails", "Second QA cycle passes"],
      "dependencies": [],
      "allowedPaths": ["docs/FILE_INDEX.md"],
      "testProfiles": ["nightshift-index"],
      "risk": "low",
      "humanRequired": false,
      "humanQuestion": ""
    },
    {
      "id": "SIM-HUMAN",
      "kind": "decision",
      "title": "Simulate a decision that automation may not make",
      "acceptanceCriteria": ["Task is deferred without stopping SIM-REPAIR"],
      "dependencies": [],
      "allowedPaths": ["docs/FILE_INDEX.md"],
      "testProfiles": [],
      "risk": "high",
      "humanRequired": true,
      "humanQuestion": "Human approval is required for this simulated architecture decision."
    }
  ]
}
'@ | ConvertFrom-Json
    if ($SimulationScenario -eq 'Success') {
        $plan.nightGoal = 'Exercise a complete repair and QA path ending in PL approval.'
        $plan.tasks = @($plan.tasks | Where-Object { $_.id -eq 'SIM-REPAIR' })
    }
    elseif ($SimulationScenario -eq 'Timeout') {
        $plan.nightGoal = 'Prove that a hung process tree is terminated and reported without blocking the night.'
        $timeoutTask = $plan.tasks | Where-Object { $_.id -eq 'SIM-REPAIR' }
        $timeoutTask.id = 'SIM-TIMEOUT'
        $timeoutTask.title = 'Simulate a hung allowlisted test process'
        $timeoutTask.acceptanceCriteria = @('The process is terminated by the deadline', 'The task becomes HUMAN_REQUIRED with timeout evidence')
        $plan.tasks = @($timeoutTask)
    }
}
else {
    $plan = Invoke-MiniMaxJson -SystemPrompt $plannerSystem -Message $goalText -OutputFile (Join-Path $runRoot 'pl-plan-raw.json') -TimeoutSeconds ([int]$config.timeouts.plannerSeconds) -Stage 'PLANNING'
}
$script:plan = $plan
Assert-PlanValid -CandidatePlan $plan -GoalPaths $goalAllowedPaths

foreach ($task in $plan.tasks) {
    $task | Add-Member -NotePropertyName status -NotePropertyValue ($(if ($task.humanRequired) { 'HUMAN_REQUIRED' } else { 'PENDING' }))
    $task | Add-Member -NotePropertyName repairCycles -NotePropertyValue 0
    $task | Add-Member -NotePropertyName qaSummary -NotePropertyValue ''
    $task | Add-Member -NotePropertyName qaFindings -NotePropertyValue @()
    $task | Add-Member -NotePropertyName testResults -NotePropertyValue @()
    $task | Add-Member -NotePropertyName blocker -NotePropertyValue ([string]$task.humanQuestion)
}
Write-AtomicJson -Path (Join-Path $runRoot 'state.json') -Value $plan

$approvedPaths = New-Object System.Collections.Generic.List[string]
while (@($plan.tasks | Where-Object { $_.status -eq 'PENDING' }).Count -gt 0) {
    $roundProgress = $false
    foreach ($task in @($plan.tasks | Where-Object { $_.status -eq 'PENDING' })) {
        $dependencyTasks = @($task.dependencies | ForEach-Object {
            $dependencyId = [string]$_
            $plan.tasks | Where-Object { [string]$_.id -eq $dependencyId }
        })
        if (@($dependencyTasks | Where-Object { $_.status -eq 'HUMAN_REQUIRED' }).Count -gt 0) {
            $task.status = 'HUMAN_REQUIRED'
            $task.blocker = 'A dependency requires human action.'
            $roundProgress = $true
            continue
        }
        if (@($dependencyTasks | Where-Object { $_.status -ne 'COMPLETE' }).Count -gt 0) { continue }

        $roundProgress = $true
        foreach ($path in $task.allowedPaths) {
            $normalizedPath = Normalize-RepoRelativePath -Path ([string]$path)
            if (-not $approvedPaths.Contains($normalizedPath)) { $approvedPaths.Add($normalizedPath) }
        }

        while ($task.repairCycles -lt [int]$config.maxRepairCycles -and $task.status -ne 'COMPLETE' -and $task.status -ne 'HUMAN_REQUIRED') {
            $task.status = 'IMPLEMENTING'
            $task.repairCycles++
            Write-AtomicJson -Path (Join-Path $runRoot 'state.json') -Value $plan
            $previousFindingText = @($task.qaFindings | ForEach-Object { "[$($_.severity)] $($_.problem) Reproduction: $($_.reproduction)" }) -join "`n"
            $devPrompt = @"
Follow AGENTS.md. Implement only this approved task.
Task: $($task.id) - $($task.title)
Acceptance criteria: $($task.acceptanceCriteria -join '; ')
Allowed paths: $($task.allowedPaths -join ', ')
Previous QA: $($task.qaSummary)
Previous structured findings:
$previousFindingText
Do not commit, push, merge, install dependencies, modify credentials, or edit outside allowed paths. The permission profile is non-interactive and fail-closed; if a required action is denied, report it as HUMAN_REQUIRED instead of trying to bypass the boundary.
"@
            # The prompt and dynamic permission profile are native Windows
            # command-line arguments. Keep their combined size below the
            # CreateProcess command-line ceiling rather than failing mid-night.
            $devPrompt = Protect-ProviderText -Text $devPrompt -MaximumCharacters 12000
            $devOutput = Join-Path $runRoot ("{0}-dev-{1}.txt" -f $task.id, $task.repairCycles)
            $devEvents = Join-Path $runRoot ("{0}-dev-events-{1}.txt" -f $task.id, $task.repairCycles)
            $devScratchOutput = Join-Path $agentTempRoot ("{0}-dev-{1}.txt" -f $task.id, $task.repairCycles)
            $devArgs = @(Get-DeveloperSandboxArguments -AllowedPaths @($task.allowedPaths) -ScratchOutput $devScratchOutput -Prompt $devPrompt)
            $codexPaidAttempt = $null
            $codexUsageRecorded = $false
            try {
                if ($Simulation) {
                    Write-AtomicText -Path $devOutput -Content "SIMULATED Codex cycle $($task.repairCycles)"
                    $devRun = [pscustomobject]@{ ExitCode = 0; Output = 'SIMULATED'; TimedOut = $false; OutputTruncated = $false }
                }
                else {
                    $codexPaidAttempt = Enter-PaidModelAttempt -Provider 'Codex' -Stage "CODEX_$($task.id)_$($task.repairCycles)"
                    Initialize-SafeDirectory -TrustedRoot $repoRoot -Path $agentTempRoot
                    foreach ($allowedPath in @($task.allowedPaths)) { Assert-RepositoryPathHasNoReparse -Path ([string]$allowedPath) }
                    if (Test-Path -LiteralPath $devScratchOutput) { Remove-Item -LiteralPath $devScratchOutput -Force }
                    $devRun = Invoke-CapturedCommand -Command $config.developer.command -Arguments $devArgs -OutputFile $devEvents -TimeoutSeconds ([int]$config.timeouts.developerSeconds) -Stage "CODEX_$($task.id)_$($task.repairCycles)" -Environment @{ TEMP = $agentTempRoot; TMP = $agentTempRoot; TMPDIR = $agentTempRoot } -SanitizeEnvironment
                    Initialize-SafeDirectory -TrustedRoot $repoRoot -Path $agentTempRoot
                    if (Test-Path -LiteralPath $devScratchOutput -PathType Leaf) {
                        $scratchItem = Get-Item -LiteralPath $devScratchOutput -Force
                        if (($scratchItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Codex delivery statement became a reparse point.' }
                        Write-AtomicText -Path $devOutput -Content (Get-Content -Raw -LiteralPath $devScratchOutput)
                    }
                    elseif ($devRun.ExitCode -eq 0) { throw 'Codex exited successfully without a delivery statement.' }
                    Add-UsageRecord -Provider 'Codex' -Stage "CODEX_$($task.id)_$($task.repairCycles)" -Attempt $codexPaidAttempt -Status $(if ($devRun.ExitCode -eq 0) { 'SUCCESS' } else { 'FAILED' }) -Usage (Get-CodexUsageFromEvents -Path $devEvents)
                    $codexUsageRecorded = $true
                    Assert-RepositoryControlState
                }
            }
            catch {
                if ($codexPaidAttempt -and -not $codexUsageRecorded) {
                    Add-UsageRecord -Provider 'Codex' -Stage "CODEX_$($task.id)_$($task.repairCycles)" -Attempt $codexPaidAttempt -Status 'FAILED' -Usage (Get-CodexUsageFromEvents -Path $devEvents)
                }
                $task.status = 'HUMAN_REQUIRED'
                $task.blocker = "Codex infrastructure error: $($_.Exception.Message)"
                break
            }

            if (-not $Simulation) {
                $currentChangedPaths = @(Get-GitChangedPaths)
                $stagedPaths = @(Get-GitStagedPaths)
                if ($stagedPaths.Count -gt 0) {
                    $task.status = 'HUMAN_REQUIRED'
                    $task.blocker = "Automation must not stage files: $($stagedPaths -join ', ')"
                    break
                }
                $deletedPaths = @(Get-GitDeletedPaths)
                if ($deletedPaths.Count -gt 0) {
                    $task.status = 'HUMAN_REQUIRED'
                    $task.blocker = "Tracked deletions require explicit human handling: $($deletedPaths -join ', ')"
                    break
                }
                foreach ($pathToCheck in @($task.allowedPaths) + $currentChangedPaths) {
                    Assert-RepositoryPathHasNoReparse -Path ([string]$pathToCheck)
                }
                $scopeViolations = @($currentChangedPaths | Where-Object {
                    $changedPath = $_
                    -not (Test-PathAllowed -Path $changedPath -AllowedPaths $approvedPaths)
                })
                if ($scopeViolations.Count -gt 0) {
                    $task.status = 'HUMAN_REQUIRED'
                    $task.blocker = "Out-of-scope changes detected: $($scopeViolations -join ', ')"
                    break
                }
                if ($currentChangedPaths.Count -eq 0) {
                    $task.status = 'HUMAN_REQUIRED'
                    $task.blocker = 'Codex reported success but produced no repository deliverable.'
                    break
                }
            }
            if ($devRun.ExitCode -ne 0) {
                $task.status = 'HUMAN_REQUIRED'
                $task.blocker = if ($devRun.TimedOut) { "Codex exceeded the $($config.timeouts.developerSeconds)-second limit and was terminated." } else { "Codex failed with exit code $($devRun.ExitCode)." }
                break
            }
            if ($devRun.OutputTruncated) {
                $task.status = 'HUMAN_REQUIRED'
                $task.blocker = 'Codex event output exceeded the capture limit.'
                break
            }

            $testEvidence = New-Object System.Collections.Generic.List[string]
            $testsPassed = $true
            try {
                $orderedProfiles = @('build', 'regression', 'sanity', 'alignment', 'nightshift-index') | Where-Object { @($task.testProfiles) -contains $_ }
                foreach ($profile in $orderedProfiles) {
                    $testFile = Join-Path $runRoot ("{0}-test-{1}-{2}.txt" -f $task.id, $task.repairCycles, $profile)
                    if ($Simulation -and $SimulationScenario -eq 'Timeout' -and $task.id -eq 'SIM-TIMEOUT') {
                        $nestedSleep = '& powershell -NoProfile -NonInteractive -Command "Start-Sleep -Seconds 30"'
                        $testRun = Invoke-CapturedCommand -Command 'powershell' -Arguments @('-NoProfile', '-NonInteractive', '-Command', $nestedSleep) -OutputFile $testFile -TimeoutSeconds 2 -Stage 'SIMULATED_HUNG_PROCESS_TREE'
                    }
                    else {
                        $testRun = Invoke-TestProfile -Profile $profile -OutputFile $testFile -Stage "TEST_$($task.id)_$profile"
                    }
                    $testCommandText = if ($Simulation -and $SimulationScenario -eq 'Timeout' -and $task.id -eq 'SIM-TIMEOUT') { 'powershell (nested 30-second sleep timeout probe)' } else { Get-TestProfileCommandText -Profile $profile }
                    $task.testResults = @($task.testResults) + [pscustomobject]@{
                        cycle = $task.repairCycles
                        profile = $profile
                        command = $testCommandText
                        exitCode = [int]$testRun.ExitCode
                        timedOut = [bool]$testRun.TimedOut
                        truncated = [bool]$testRun.OutputTruncated
                        evidenceFile = $testFile
                    }
                    $safeTestOutput = Protect-ProviderText -Text ([string]$testRun.Output) -MaximumCharacters 30000
                    $testEvidence.Add("PROFILE=$profile COMMAND=$testCommandText EXIT=$($testRun.ExitCode) TIMED_OUT=$($testRun.TimedOut) TRUNCATED=$($testRun.OutputTruncated)`n$safeTestOutput")
                    if ($testRun.ExitCode -ne 0 -or $testRun.OutputTruncated) { $testsPassed = $false }
                }
                if (-not $Simulation) { Assert-RepositoryControlState }
            }
            catch {
                $task.status = 'HUMAN_REQUIRED'
                $task.blocker = "Test infrastructure error: $($_.Exception.Message)"
                break
            }

            $reviewEvidence = Protect-ProviderText -Text $(if ($Simulation) { 'SIMULATED DIFF EVIDENCE' } else { Get-ReviewEvidence }) -MaximumCharacters 80000
            $safeDevStatement = Protect-ProviderText -Text (Get-Content -Raw -LiteralPath $devOutput) -MaximumCharacters 30000
            $qaPrompt = @"
Independently evaluate this task. Return JSON only:
{"verdict":"PASS|FAIL|HUMAN_REQUIRED","summary":"...","findings":[{"severity":"P0|P1|P2|P3","problem":"...","reproduction":"..."}]}
Task: $($task.title)
Acceptance criteria: $($task.acceptanceCriteria -join '; ')
Allowlisted tests all exited zero: $testsPassed
CODEX DELIVERY STATEMENT:
$safeDevStatement
TEST EVIDENCE:
$($testEvidence -join "`n---`n")
REPOSITORY EVIDENCE:
$reviewEvidence
"@
            $qaFile = Join-Path $runRoot ("{0}-qa-{1}.json" -f $task.id, $task.repairCycles)
            try {
                if ($Simulation) {
                    if ($task.id -eq 'SIM-TIMEOUT') {
                        $qa = [pscustomobject]@{ verdict = 'HUMAN_REQUIRED'; summary = 'The simulated hung process tree was terminated at the deadline.'; findings = @() }
                    }
                    elseif ($task.id -eq 'SIM-REPAIR' -and $task.repairCycles -eq 1) {
                        $qa = [pscustomobject]@{ verdict = 'FAIL'; summary = 'Simulated reproducible failure on the first QA cycle.'; findings = @() }
                    }
                    else {
                        $qa = [pscustomobject]@{ verdict = 'PASS'; summary = 'Simulated QA pass backed by the allowlisted test profile.'; findings = @() }
                    }
                    Write-AtomicJson -Path $qaFile -Value $qa
                }
                else {
                    $qa = Invoke-DeepSeekJson -Message $qaPrompt -OutputFile $qaFile -Stage "QA_$($task.id)_$($task.repairCycles)"
                }
            }
            catch {
                $task.status = 'HUMAN_REQUIRED'
                $task.blocker = "QA infrastructure error: $($_.Exception.Message)"
                break
            }

            if ([string]$qa.verdict -notin @('PASS', 'FAIL', 'HUMAN_REQUIRED') -or -not [string]$qa.summary -or ([string]$qa.summary).Length -gt 4000) {
                $task.status = 'HUMAN_REQUIRED'
                $task.blocker = 'QA returned an invalid verdict or empty summary.'
                break
            }
            $qaFindings = @($qa.findings)
            if ($qaFindings.Count -gt 20) {
                $task.status = 'HUMAN_REQUIRED'
                $task.blocker = 'QA returned too many findings.'
                break
            }
            $invalidFinding = @($qaFindings | Where-Object {
                [string]$_.severity -notin @('P0', 'P1', 'P2', 'P3') -or
                -not [string]$_.problem -or ([string]$_.problem).Length -gt 2000 -or
                ([string]$_.reproduction).Length -gt 4000
            })
            if ($invalidFinding.Count -gt 0) {
                $task.status = 'HUMAN_REQUIRED'
                $task.blocker = 'QA returned a malformed finding.'
                break
            }
            $task.qaSummary = Protect-ProviderText -Text ([string]$qa.summary) -MaximumCharacters 4000
            $task.qaFindings = @($qaFindings | ForEach-Object {
                [pscustomobject]@{
                    severity = [string]$_.severity
                    problem = Protect-ProviderText -Text ([string]$_.problem) -MaximumCharacters 2000
                    reproduction = Protect-ProviderText -Text ([string]$_.reproduction) -MaximumCharacters 4000
                }
            })
            if ([string]$qa.verdict -eq 'PASS' -and $qaFindings.Count -gt 0) {
                $task.status = 'HUMAN_REQUIRED'
                $task.blocker = 'QA returned PASS while also reporting unresolved findings.'
                break
            }
            switch ([string]$qa.verdict) {
                'PASS' {
                    if (-not $testsPassed) {
                        $task.status = 'HUMAN_REQUIRED'
                        $task.blocker = 'QA returned PASS despite failing, timed-out, or truncated test evidence.'
                    }
                    else { $task.status = 'COMPLETE' }
                }
                'FAIL' { $task.status = 'PENDING' }
                'HUMAN_REQUIRED' { $task.status = 'HUMAN_REQUIRED'; $task.blocker = [string]$qa.summary }
            }
            Write-AtomicJson -Path (Join-Path $runRoot 'state.json') -Value $plan
        }
        if ($task.status -ne 'COMPLETE' -and $task.status -ne 'HUMAN_REQUIRED') {
            $task.status = 'HUMAN_REQUIRED'
            $task.blocker = 'Maximum repair cycles reached.'
        }
        Write-AtomicJson -Path (Join-Path $runRoot 'state.json') -Value $plan
    }
    if (-not $roundProgress) {
        foreach ($task in @($plan.tasks | Where-Object { $_.status -eq 'PENDING' })) {
            $task.status = 'HUMAN_REQUIRED'
            $task.blocker = 'No dependency-safe execution order remained.'
        }
    }
}

$finalPrompt = @"
Review the night goal and actual task states. Return JSON only:
{"status":"PL_APPROVED|PARTIAL|HUMAN_REQUIRED","summary":"..."}
PL_APPROVED is allowed only when every task is COMPLETE with QA PASS evidence.
STATE:
$($plan | ConvertTo-Json -Depth 20)
"@
if ($Simulation) {
    # Deliberately propose an invalid approval. The deterministic guard below
    # must downgrade it because SIM-HUMAN is incomplete.
    $finalReview = [pscustomobject]@{ status = 'PL_APPROVED'; summary = 'Simulated PL attempted approval.' }
}
else {
    try {
        $finalReview = Invoke-MiniMaxJson -SystemPrompt 'You are the final PL reviewer. Use only supplied evidence and return JSON only.' -Message $finalPrompt -OutputFile (Join-Path $runRoot 'pl-final-raw.json') -TimeoutSeconds ([int]$config.timeouts.finalReviewSeconds) -Stage 'FINAL_PL_REVIEW'
    }
    catch {
        $finalReview = [pscustomobject]@{ status = 'HUMAN_REQUIRED'; summary = "Final PL review was unavailable: $($_.Exception.Message)" }
    }
}
$completedCount = @($plan.tasks | Where-Object { $_.status -eq 'COMPLETE' }).Count
$incompleteCount = @($plan.tasks | Where-Object { $_.status -ne 'COMPLETE' }).Count
$derivedStatus = if ($incompleteCount -eq 0) { 'PL_APPROVED' } elseif ($completedCount -gt 0) { 'PARTIAL' } else { 'HUMAN_REQUIRED' }
if ([string]$finalReview.status -notin @('PL_APPROVED', 'PARTIAL', 'HUMAN_REQUIRED') -or -not [string]$finalReview.summary) {
    $finalReview = [pscustomobject]@{ status = 'HUMAN_REQUIRED'; summary = 'Final PL review returned an invalid status or empty summary.' }
}
$finalReview.summary = Protect-ProviderText -Text ([string]$finalReview.summary) -MaximumCharacters 8000
$modelStatus = [string]$finalReview.status
$effectiveStatus = Resolve-FinalStatus -EvidenceStatus $derivedStatus -PlanningLeadStatus $modelStatus
if ($effectiveStatus -ne $modelStatus) {
    $finalReview.status = $effectiveStatus
    $finalReview.summary = "Framework derived $effectiveStatus from task evidence and overrode model status $modelStatus. $($finalReview.summary)"
}
$plan | Add-Member -NotePropertyName finalStatus -NotePropertyValue ([string]$finalReview.status) -Force
$plan | Add-Member -NotePropertyName finalSummary -NotePropertyValue ([string]$finalReview.summary) -Force
$plan | Add-Member -NotePropertyName completedAt -NotePropertyValue ([DateTimeOffset]::Now.ToString('o')) -Force
Set-RunStage -Stage 'REPORTING'
Write-NightReport -State $plan -FinalReview $finalReview
Write-AtomicJson -Path (Join-Path $runRoot 'state.json') -Value $plan
Update-RunLedger -Status ([string]$finalReview.status) -Message ([string]$finalReview.summary)
Write-Host "Night shift finished: $($finalReview.status)"
Write-Host "Report: $reportPath"
if ($Scheduled) {
    Add-SchedulerLog -Message "FINISHED $($finalReview.status) report=$reportPath"
    $scheduledExit = if ($finalReview.status -eq 'PL_APPROVED') { 0 } elseif ($finalReview.status -eq 'PARTIAL') { 2 } else { 3 }
    Write-SchedulerStatus -Status ([string]$finalReview.status) -Message ([string]$finalReview.summary) -ExitCode $scheduledExit
}
Set-NightShiftAwake -Enabled $false
Exit-NightShiftLock
$script:runStarted = $false
switch ([string]$finalReview.status) {
    'PL_APPROVED' { exit 0 }
    'PARTIAL' { exit 2 }
    default { exit 3 }
}
