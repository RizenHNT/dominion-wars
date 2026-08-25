[CmdletBinding()]
param(
    [string]$UnityExe,
    [ValidateRange(60, 3600)]
    [int]$TimeoutSeconds = 900,
    [ValidateRange(3, 60)]
    [int]$PlayerSmokeSeconds = 10,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = [IO.Path]::GetFullPath((Join-Path $repoRoot 'unity\DominionWars.Unity'))
$versionFile = Join-Path $projectPath 'ProjectSettings\ProjectVersion.txt'
if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
    throw "Unity ProjectVersion.txt was not found at $versionFile"
}

$versionLine = Get-Content -LiteralPath $versionFile | Where-Object { $_ -match '^m_EditorVersion:\s*(\S+)' } | Select-Object -First 1
if (-not $versionLine) { throw 'Unity editor version is missing from ProjectVersion.txt.' }
$editorVersion = ([regex]::Match($versionLine, '^m_EditorVersion:\s*(\S+)')).Groups[1].Value

if ([string]::IsNullOrWhiteSpace($UnityExe)) {
    $programFilesRoot = if (-not [string]::IsNullOrWhiteSpace(${env:ProgramW6432})) {
        ${env:ProgramW6432}
    }
    else {
        ${env:ProgramFiles}
    }
    $UnityExe = Join-Path $programFilesRoot "Unity\Hub\Editor\$editorVersion\Editor\Unity.exe"
}
$UnityExe = [IO.Path]::GetFullPath($UnityExe)
if (-not (Test-Path -LiteralPath $UnityExe -PathType Leaf)) {
    throw "Unity editor $editorVersion was not found at $UnityExe"
}

$lockFile = Join-Path $projectPath 'Temp\UnityLockfile'
$projectOpen = Test-Path -LiteralPath $lockFile -PathType Leaf
$validation = [pscustomobject]@{
    editorVersion = $editorVersion
    unityExe = $UnityExe
    projectPath = $projectPath
    projectOpen = $projectOpen
}
if ($ValidateOnly) {
    $validation | Format-List
    if ($projectOpen) { exit 2 }
    exit 0
}
if ($projectOpen) {
    throw 'Unity validation refused: the project is currently open. Close the Editor normally, then rerun this script.'
}

$validationMutex = [Threading.Mutex]::new($false, 'Local\DominionWars.Unity.RuntimeValidation')
$mutexAcquired = $false
try {
try {
    $mutexAcquired = $validationMutex.WaitOne(0)
}
catch [Threading.AbandonedMutexException] {
    $mutexAcquired = $true
}
if (-not $mutexAcquired) {
    throw 'Unity validation refused: another validation run is already active in this user session.'
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$suffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $repoRoot "build-output\unity-runtime-validation\$stamp-$suffix"
if (Test-Path -LiteralPath $runRoot) {
    throw "Unity validation refused to reuse existing output: $runRoot"
}
[void](New-Item -ItemType Directory -Path $runRoot)
$editModeResults = Join-Path $runRoot 'editmode-results.xml'
$editModeLog = Join-Path $runRoot 'editmode.log'
$playModeResults = Join-Path $runRoot 'playmode-results.xml'
$playModeLog = Join-Path $runRoot 'playmode.log'
$buildLog = Join-Path $runRoot 'windows-build.log'
$playerPath = Join-Path $runRoot 'player\DominionWars.Unity.exe'
$playerLog = Join-Path $runRoot 'player.log'

function Quote-ProcessArgument([string]$Value) {
    if ($Value -notmatch '[\s"]') { return $Value }
    return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

function Invoke-UnityStage([string]$Name, [string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $UnityExe
    $start.Arguments = (($Arguments | ForEach-Object { Quote-ProcessArgument $_ }) -join ' ')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    if (-not $process.Start()) { throw "Unity $Name could not start." }
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            $process.WaitForExit()
            throw "Unity $Name timed out after $TimeoutSeconds seconds. Artifacts remain at $runRoot"
        }
        if ($process.ExitCode -ne 0) {
            throw "Unity $Name failed with exit code $($process.ExitCode). Artifacts remain at $runRoot"
        }
    }
    finally {
        $process.Dispose()
    }
}

function Require-TestCount([System.Xml.XmlElement]$TestRun, [string]$Name) {
    $raw = $TestRun.GetAttribute($Name)
    $value = 0
    if (-not [int]::TryParse($raw, [ref]$value) -or $value -lt 0) {
        throw "Unity test results contain an invalid $Name count: '$raw'"
    }
    return $value
}

function Read-UnityTestSummary([string]$Name, [string]$ResultsPath) {
    if (-not (Test-Path -LiteralPath $ResultsPath -PathType Leaf)) {
        throw "Unity $Name returned success without producing $ResultsPath"
    }
    [xml]$document = Get-Content -Raw -LiteralPath $ResultsPath
    $run = $document.'test-run'
    if ($null -eq $run) { throw "Unity $Name results do not contain a test-run root." }
    $total = Require-TestCount $run 'total'
    $passed = Require-TestCount $run 'passed'
    $failed = Require-TestCount $run 'failed'
    $skipped = Require-TestCount $run 'skipped'
    if ($total -le 0 -or
        -not [string]::Equals($run.GetAttribute('result'), 'Passed', [StringComparison]::OrdinalIgnoreCase) -or
        $failed -ne 0 -or
        $passed + $skipped -ne $total) {
        throw "Unity $Name did not pass cleanly: passed=$passed total=$total failed=$failed skipped=$skipped"
    }
    return [pscustomobject]@{
        Passed = $passed
        Total = $total
        Failed = $failed
        Skipped = $skipped
    }
}

Invoke-UnityStage 'EditMode tests' @(
    '-batchmode',
    '-nographics',
    '-projectPath', $projectPath,
    '-runTests',
    '-testPlatform', 'EditMode',
    '-testResults', $editModeResults,
    '-logFile', $editModeLog
)
$editMode = Read-UnityTestSummary 'EditMode tests' $editModeResults
if (Test-Path -LiteralPath $lockFile -PathType Leaf) {
    throw 'Unity validation refused to start PlayMode tests because the project became occupied after EditMode tests.'
}

Invoke-UnityStage 'PlayMode tests' @(
    '-batchmode',
    '-nographics',
    '-projectPath', $projectPath,
    '-runTests',
    '-testPlatform', 'PlayMode',
    '-testResults', $playModeResults,
    '-logFile', $playModeLog
)
$playMode = Read-UnityTestSummary 'PlayMode tests' $playModeResults
if (Test-Path -LiteralPath $lockFile -PathType Leaf) {
    throw 'Unity validation refused to start the Windows build because the project became occupied after PlayMode tests.'
}

Invoke-UnityStage 'Windows build' @(
    '-batchmode',
    '-nographics',
    '-quit',
    '-projectPath', $projectPath,
    '-executeMethod', 'DominionWars.Unity.EditorTools.RuntimePlayerBuild.BuildWindows64FromCommandLine',
    '-buildOutput', $playerPath,
    '-logFile', $buildLog
)
if (-not (Test-Path -LiteralPath $playerPath -PathType Leaf)) {
    throw "Unity Windows build returned success without producing $playerPath"
}
$generatedData = Join-Path $projectPath 'Assets\StreamingAssets\data'
$generatedDataMeta = $generatedData + '.meta'
if ((Test-Path -LiteralPath $generatedData) -or (Test-Path -LiteralPath $generatedDataMeta)) {
    throw "Unity Windows build succeeded but generated project data was not cleaned: $generatedData"
}

$playerStart = [Diagnostics.ProcessStartInfo]::new()
$playerStart.FileName = $playerPath
$playerStart.WorkingDirectory = Split-Path -Parent $playerPath
$playerStart.Arguments = '-batchmode -nographics -logFile ' + (Quote-ProcessArgument $playerLog)
$playerStart.UseShellExecute = $false
$playerStart.CreateNoWindow = $true
$playerProcess = [Diagnostics.Process]::new()
$playerProcess.StartInfo = $playerStart
$playerReady = $false
if (-not $playerProcess.Start()) { throw 'Unity Windows player smoke could not start.' }
try {
    $deadline = [DateTime]::UtcNow.AddSeconds($PlayerSmokeSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($playerProcess.HasExited) {
            throw "Unity Windows player exited before bootstrap readiness with code $($playerProcess.ExitCode)."
        }
        if (Test-Path -LiteralPath $playerLog -PathType Leaf) {
            $logText = Get-Content -Raw -LiteralPath $playerLog
            if ($logText.Contains('Dominion Wars runtime bootstrap ready.')) {
                $playerReady = $true
                break
            }
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $playerReady) {
        throw "Unity Windows player did not report bootstrap readiness within $PlayerSmokeSeconds seconds."
    }
}
finally {
    if (-not $playerProcess.HasExited) {
        $playerProcess.Kill()
        $playerProcess.WaitForExit()
    }
    $playerProcess.Dispose()
}
$playerLogText = Get-Content -Raw -LiteralPath $playerLog
if ($playerLogText -match 'NullReferenceException|MissingComponentException|InvalidOperationException|DirectoryNotFoundException|UnauthorizedAccessException|Assertion failed') {
    throw "Unity Windows player logged a runtime exception. Inspect $playerLog"
}

[pscustomobject]@{
    status = 'PASS'
    editorVersion = $editorVersion
    editMode = "$($editMode.Passed)/$($editMode.Total) passed; failed=$($editMode.Failed) skipped=$($editMode.Skipped)"
    editModeResults = $editModeResults
    playMode = "$($playMode.Passed)/$($playMode.Total) passed; failed=$($playMode.Failed) skipped=$($playMode.Skipped)"
    playModeResults = $playModeResults
    player = $playerPath
    playerSmoke = "bootstrap ready in <= $PlayerSmokeSeconds seconds"
    playerLog = $playerLog
    outputRoot = $runRoot
} | Format-List
}
finally {
    if ($mutexAcquired) { $validationMutex.ReleaseMutex() }
    $validationMutex.Dispose()
}
