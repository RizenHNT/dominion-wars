[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$Simulation,
    [switch]$Scheduled,
    [ValidateSet('Mixed', 'Success')]
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
$stateRoot = Join-Path $repoRoot '.nightshift'
$runId = Get-Date -Format 'yyyy-MM-dd_HHmmss'
$runRoot = Join-Path $stateRoot $runId
$reportPath = if ($Simulation) { Join-Path $runRoot 'SIMULATION_REPORT.md' } else { Join-Path $repoRoot 'docs\NIGHT_REPORT.md' }
$schedulerLog = Join-Path $stateRoot 'scheduler.log'
$configuredMiniMax = [string]$config.pl.command
$configuredCodex = [string]$config.developer.command

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

function Invoke-CapturedCommand {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$OutputFile
    )
    # Native CLIs legitimately stream progress to stderr. With the script-wide
    # Stop policy PowerShell turns those records into terminating exceptions,
    # so capture both streams and judge the process only by its exit code.
    $previousPreference = $ErrorActionPreference
    $previousNativePreference = $PSNativeCommandUseErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        $output = & $Command @Arguments 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
        $PSNativeCommandUseErrorActionPreference = $previousNativePreference
    }
    $output | Set-Content -LiteralPath $OutputFile -Encoding UTF8
    [pscustomobject]@{ ExitCode = $exitCode; Output = $output }
}

function Get-GitChangedPaths {
    $tracked = @(& git -C $repoRoot diff --name-only)
    $staged = @(& git -C $repoRoot diff --cached --name-only)
    $untracked = @(& git -C $repoRoot ls-files --others --exclude-standard)
    @($tracked + $staged + $untracked) |
        Where-Object { $_ -and -not $_.StartsWith('.nightshift/') } |
        Sort-Object -Unique
}

function Test-PathAllowed {
    param([string]$Path, [object[]]$AllowedPaths)
    $normalized = $Path.Replace('\', '/').TrimStart('./')
    foreach ($allowed in $AllowedPaths) {
        $candidate = ([string]$allowed).Replace('\', '/').Trim().TrimStart('./').TrimEnd('/')
        if ($candidate -and ($normalized -eq $candidate -or $normalized.StartsWith("$candidate/"))) {
            return $true
        }
    }
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

function Test-SafeAllowedPath {
    param([string]$Path)
    $normalized = $Path.Replace('\', '/').Trim().TrimStart('./').TrimEnd('/')
    if (-not $normalized) { return $false }
    $forbidden = @('.git', '.nightshift', 'AGENTS.md', 'docs/AI_WORKFLOW.md', 'docs/NIGHTSHIFT_WORKFLOW.md', '.github/agents')
    foreach ($entry in $forbidden) {
        if ($normalized -eq $entry -or $normalized.StartsWith("$entry/")) { return $false }
    }
    return $true
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
    param([string]$SystemPrompt, [string]$Message, [string]$OutputFile)
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
        '--output', 'json'
    )
    $result = Invoke-CapturedCommand -Command $config.pl.command -Arguments $arguments -OutputFile $OutputFile
    if ($result.ExitCode -ne 0) { throw "MiniMax failed. See $OutputFile" }
    $wrapper = $result.Output | ConvertFrom-Json
    $text = [string]$wrapper.content[0].text
    ConvertFrom-ModelJson -Text $text
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

function Invoke-DeepSeekJson {
    param([string]$Message, [string]$OutputFile)
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
        $response = Invoke-RestMethod -Method Post -Uri $config.qa.endpoint -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $bodyBytes
        $response | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $OutputFile -Encoding UTF8
        ConvertFrom-ModelJson -Text ([string]$response.choices[0].message.content)
    }
    finally {
        $key = $null
    }
}

function Invoke-TestProfile {
    param([string]$Profile, [string]$OutputFile)
    switch ($Profile) {
        'build' {
            Invoke-CapturedCommand -Command (Join-Path $repoRoot 'scripts\build.bat') -Arguments @() -OutputFile $OutputFile
        }
        'regression' {
            Invoke-CapturedCommand -Command 'java' -Arguments @('-cp', 'build/classes;build/test-classes', 'com.dominionwars.test.TestMain') -OutputFile $OutputFile
        }
        'sanity' {
            Invoke-CapturedCommand -Command $pythonCommand -Arguments @('scripts/sanity_check_v2.py') -OutputFile $OutputFile
        }
        'alignment' {
            Invoke-CapturedCommand -Command $pythonCommand -Arguments @('scripts/align_check.py') -OutputFile $OutputFile
        }
        'nightshift-index' {
            Invoke-CapturedCommand -Command 'powershell' -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'scripts/nightshift/verify-nightshift-index.ps1') -OutputFile $OutputFile
        }
        default { throw "Test profile is not allowlisted: $Profile" }
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
    }
    $lines += @('', '## PL review', '', [string]$FinalReview.summary, '', '## Human decisions required', '')
    $humanTasks = @($State.tasks | Where-Object { $_.status -eq 'HUMAN_REQUIRED' })
    if ($humanTasks.Count -eq 0) { $lines += '- None.' }
    else { foreach ($task in $humanTasks) { $lines += "- $($task.id): $($task.blocker)" } }
    $lines | Set-Content -LiteralPath $reportPath -Encoding UTF8
}

Set-Location $repoRoot
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

trap {
    if ($Scheduled) {
        "$(Get-Date -Format 'o') FAILED $($_.Exception.Message)" | Add-Content -LiteralPath $schedulerLog -Encoding UTF8
    }
    exit 1
}

$goalPath = Join-Path $repoRoot $GoalFile
if (-not (Test-Path -LiteralPath $goalPath)) { throw "Goal file not found: $goalPath" }
$goalText = Get-Content -Raw -LiteralPath $goalPath
$goalAllowedPaths = if ($Simulation) { @('docs') } else { @(Get-GoalAllowedPaths -Goal $goalText) }

$branch = (& git -C $repoRoot branch --show-current).Trim()
$changedBefore = @(Get-GitChangedPaths)
$ready = $Simulation -or ($goalText -match '(?m)^Status:\s*READY\s*$')

if ($DryRun) {
    [pscustomobject]@{
        Mode = 'DRY_RUN'
        SimulationScenario = $SimulationScenario
        Repository = $repoRoot
        Branch = $branch
        GoalReady = $ready
        ExistingChanges = $changedBefore
        GoalAllowedPaths = $goalAllowedPaths
        MiniMax = (Get-Command $config.pl.command).Source
        Codex = (Get-Command $config.developer.command).Source
        Python = $pythonCommand
        DeepSeekCredentialConfigured = Test-DeepSeekCredential
        MaxTasks = $config.maxTasks
        MaxRepairCycles = $config.maxRepairCycles
    } | ConvertTo-Json -Depth 5
    exit 0
}

if (-not $ready) {
    if ($Scheduled) {
        "$(Get-Date -Format 'o') SKIPPED no READY daily goal" | Add-Content -LiteralPath $schedulerLog -Encoding UTF8
        Write-Host 'No READY daily goal; scheduled night shift skipped.'
        exit 0
    }
    throw 'docs/DAILY_GOAL.md is not READY.'
}

$requiredCommands = if ($Simulation) { @('git', 'java') } else { @('git', [string]$config.pl.command, [string]$config.developer.command, 'java') }
foreach ($command in $requiredCommands) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Required command not found: $command" }
}
if (-not $pythonCommand) { throw 'Required Python interpreter not found.' }
if ($goalAllowedPaths.Count -eq 0) { throw 'The READY goal has no allowed paths.' }
foreach ($path in $goalAllowedPaths) {
    if (-not (Test-SafeAllowedPath -Path $path)) { throw "Unsafe allowed path in goal: $path" }
}
if ($config.protectedBranches -contains $branch) { throw "Live night shift is forbidden on protected branch: $branch" }
if ($changedBefore.Count -gt 0) { throw 'Live night shift requires a clean worktree.' }
if (-not $Simulation) { $null = Get-DeepSeekCredential }

$plannerSystem = @'
You are the Dominion Wars temporary PL. Produce a bounded plan from the human-approved goal.
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
      "allowedPaths": ["docs"],
      "testProfiles": ["sanity"],
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
      "allowedPaths": ["docs"],
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
}
else {
    $plan = Invoke-MiniMaxJson -SystemPrompt $plannerSystem -Message $goalText -OutputFile (Join-Path $runRoot 'pl-plan-raw.json')
}
if (@($plan.tasks).Count -gt [int]$config.maxTasks) { throw 'PL returned too many tasks.' }

foreach ($task in $plan.tasks) {
    $task | Add-Member -NotePropertyName status -NotePropertyValue ($(if ($task.humanRequired) { 'HUMAN_REQUIRED' } else { 'PENDING' }))
    $task | Add-Member -NotePropertyName repairCycles -NotePropertyValue 0
    $task | Add-Member -NotePropertyName qaSummary -NotePropertyValue ''
    $task | Add-Member -NotePropertyName blocker -NotePropertyValue ([string]$task.humanQuestion)
    foreach ($profile in $task.testProfiles) {
        if ($config.allowedTestProfiles -notcontains [string]$profile) { throw "PL selected a non-allowlisted test profile: $profile" }
    }
    if (-not $task.humanRequired -and [string]$task.kind -ne 'implementation') {
        throw "PL returned a non-implementation executable task: $($task.id)"
    }
    foreach ($path in $task.allowedPaths) {
        if (-not (Test-SafeAllowedPath -Path ([string]$path))) { throw "PL selected an unsafe path: $path" }
        if (-not (Test-PathAllowed -Path ([string]$path) -AllowedPaths $goalAllowedPaths)) {
            throw "PL selected a path outside the human-approved scope: $path"
        }
    }
}
$plan | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $runRoot 'state.json') -Encoding UTF8

$approvedPaths = New-Object System.Collections.Generic.List[string]
foreach ($task in $plan.tasks) {
    if ($task.status -ne 'PENDING') { continue }
    $dependencyStates = @($task.dependencies | ForEach-Object {
        $dependencyId = [string]$_
        $plan.tasks | Where-Object { $_.id -eq $dependencyId } | Select-Object -ExpandProperty status
    })
    if (@($dependencyStates | Where-Object { $_ -ne 'COMPLETE' }).Count -gt 0) {
        $task.status = 'HUMAN_REQUIRED'
        $task.blocker = 'A dependency did not complete.'
        continue
    }
    foreach ($path in $task.allowedPaths) { if (-not $approvedPaths.Contains([string]$path)) { $approvedPaths.Add([string]$path) } }

    while ($task.repairCycles -lt [int]$config.maxRepairCycles -and $task.status -ne 'COMPLETE') {
        $task.status = 'IMPLEMENTING'
        $task.repairCycles++
        $devPrompt = @"
Follow AGENTS.md. Implement only this approved task.
Task: $($task.id) - $($task.title)
Acceptance criteria: $($task.acceptanceCriteria -join '; ')
Allowed paths: $($task.allowedPaths -join ', ')
Previous QA: $($task.qaSummary)
Do not commit, push, merge, install dependencies, modify credentials, or edit outside allowed paths. If a human decision is required, stop and state HUMAN_REQUIRED.
"@
        $devOutput = Join-Path $runRoot ("{0}-dev-{1}.txt" -f $task.id, $task.repairCycles)
        # Current Codex CLI versions reject --approve-for-me when an explicit
        # sandbox is also supplied. Keep the auditable workspace-write sandbox;
        # any operation that needs broader approval must fail closed.
        $devArgs = @('exec', '--ephemeral', '--sandbox', [string]$config.developer.sandbox, '-C', $repoRoot, '-o', $devOutput, $devPrompt)
        if ($Simulation) {
            "SIMULATED Codex cycle $($task.repairCycles)" | Set-Content -LiteralPath $devOutput -Encoding UTF8
            $devRun = [pscustomobject]@{ ExitCode = 0; Output = 'SIMULATED' }
        }
        else {
            $devRun = Invoke-CapturedCommand -Command $config.developer.command -Arguments $devArgs -OutputFile (Join-Path $runRoot ("{0}-dev-events-{1}.txt" -f $task.id, $task.repairCycles))
        }
        if ($devRun.ExitCode -ne 0) {
            $task.status = 'HUMAN_REQUIRED'; $task.blocker = 'Codex execution failed.'; break
        }

        $scopeViolations = @(Get-GitChangedPaths | Where-Object {
            $path = $_
            -not (Test-PathAllowed -Path $path -AllowedPaths $approvedPaths)
        })
        if ($scopeViolations.Count -gt 0) {
            $task.status = 'HUMAN_REQUIRED'
            $task.blocker = "Out-of-scope changes detected: $($scopeViolations -join ', ')"
            break
        }

        $testEvidence = @()
        $testsPassed = $true
        foreach ($profile in $task.testProfiles) {
            $testFile = Join-Path $runRoot ("{0}-test-{1}-{2}.txt" -f $task.id, $task.repairCycles, $profile)
            $testRun = Invoke-TestProfile -Profile $profile -OutputFile $testFile
            $testEvidence += "PROFILE=$profile EXIT=$($testRun.ExitCode)`n$($testRun.Output)"
            if ($testRun.ExitCode -ne 0) { $testsPassed = $false }
        }
        $diff = (& git -C $repoRoot diff --no-ext-diff --unified=3 | Out-String)
        if ($diff.Length -gt 60000) { $diff = $diff.Substring(0, 60000) + "`n[DIFF TRUNCATED]" }
        $qaPrompt = @"
Independently evaluate this task. Return JSON only:
{"verdict":"PASS|FAIL|HUMAN_REQUIRED","summary":"...","findings":[{"severity":"P0|P1|P2|P3","problem":"...","reproduction":"..."}]}
Task: $($task.title)
Acceptance criteria: $($task.acceptanceCriteria -join '; ')
Allowlisted tests all exited zero: $testsPassed
CODEX DELIVERY STATEMENT:
$(Get-Content -Raw -LiteralPath $devOutput)
TEST EVIDENCE:
$($testEvidence -join "`n---`n")
ACTUAL DIFF:
$diff
"@
        if ($Simulation) {
            if ($task.id -eq 'SIM-REPAIR' -and $task.repairCycles -eq 1) {
                $qa = [pscustomobject]@{ verdict = 'FAIL'; summary = 'Simulated reproducible failure on the first QA cycle.'; findings = @() }
            }
            else {
                $qa = [pscustomobject]@{ verdict = 'PASS'; summary = 'Simulated QA pass backed by the allowlisted sanity profile.'; findings = @() }
            }
            $qa | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $runRoot ("{0}-qa-{1}.json" -f $task.id, $task.repairCycles)) -Encoding UTF8
        }
        else {
            $qa = Invoke-DeepSeekJson -Message $qaPrompt -OutputFile (Join-Path $runRoot ("{0}-qa-{1}.json" -f $task.id, $task.repairCycles))
        }
        $task.qaSummary = [string]$qa.summary
        switch ([string]$qa.verdict) {
            'PASS' {
                if (-not $testsPassed) { $task.status = 'HUMAN_REQUIRED'; $task.blocker = 'QA returned PASS despite a failing test.' }
                else { $task.status = 'COMPLETE' }
            }
            'FAIL' { $task.status = 'PENDING' }
            default { $task.status = 'HUMAN_REQUIRED'; $task.blocker = [string]$qa.summary }
        }
    }
    if ($task.status -ne 'COMPLETE' -and $task.status -ne 'HUMAN_REQUIRED') {
        $task.status = 'HUMAN_REQUIRED'; $task.blocker = 'Maximum repair cycles reached.'
    }
    $plan | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $runRoot 'state.json') -Encoding UTF8
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
    $finalReview = Invoke-MiniMaxJson -SystemPrompt 'You are the final PL reviewer. Use only supplied evidence and return JSON only.' -Message $finalPrompt -OutputFile (Join-Path $runRoot 'pl-final-raw.json')
}
if (@($plan.tasks | Where-Object { $_.status -ne 'COMPLETE' }).Count -gt 0 -and $finalReview.status -eq 'PL_APPROVED') {
    $finalReview.status = 'PARTIAL'
    $finalReview.summary = 'Framework overrode an invalid PL approval because one or more tasks were incomplete.'
}
Write-NightReport -State $plan -FinalReview $finalReview
$plan | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $runRoot 'state.json') -Encoding UTF8
Write-Host "Night shift finished: $($finalReview.status)"
Write-Host "Report: $reportPath"
if ($Scheduled) {
    "$(Get-Date -Format 'o') FINISHED $($finalReview.status) report=$reportPath" | Add-Content -LiteralPath $schedulerLog -Encoding UTF8
}
