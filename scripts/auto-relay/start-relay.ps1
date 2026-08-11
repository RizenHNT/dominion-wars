[CmdletBinding()]
param(
    [switch]$ApprovedByHuman,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'relay-common.ps1')

$repoRoot = Get-RelayRepositoryRoot -ScriptDirectory $PSScriptRoot
$stateRoot = Get-RelayStateRoot -RepositoryRoot $repoRoot
if (Test-RelayDisabled -StateRoot $stateRoot) { throw 'Auto relay is disabled. Run disable-relay.ps1 -Enable after human review.' }
if (-not $ValidateOnly -and -not $ApprovedByHuman) {
    throw 'Live relay requires -ApprovedByHuman. MiniMax may use it only after the owner approved docs/DAILY_GOAL.md.'
}

$goalFile = Join-Path $repoRoot 'docs\DAILY_GOAL.md'
if (-not (Test-Path -LiteralPath $goalFile -PathType Leaf)) { throw 'docs/DAILY_GOAL.md is missing.' }
$goalText = Get-Content -Raw -LiteralPath $goalFile
if ($goalText.Length -gt 20000) { throw 'docs/DAILY_GOAL.md exceeds the 20,000-character relay limit.' }
$goalReady = $goalText -match '(?m)^Status:\s*READY\s*$'

$nightWorktree = Join-Path $repoRoot '.nightshift\rehearsal'
if (-not (Test-Path -LiteralPath $nightWorktree -PathType Container)) { throw 'The isolated night-shift worktree is missing.' }
& git -C $repoRoot merge-base --is-ancestor main agents/nightshift-rehearsal
$branchContainsMain = $LASTEXITCODE -eq 0
$controlPaths = @('AGENTS.md', 'docs/AI_WORKFLOW.md', '.github/agents', '.codex', 'scripts/nightshift', 'scripts/auto-relay')
& git -C $repoRoot diff --quiet main agents/nightshift-rehearsal -- @controlPaths
$branchControlMatchesMain = $LASTEXITCODE -eq 0
if ($ValidateOnly -and -not $goalReady) {
    [pscustomobject]@{
        mode = 'VALIDATE_ONLY'
        paidCall = $false
        goalReady = $false
        relayDisabled = (Test-RelayDisabled -StateRoot $stateRoot)
        isolatedBranchContainsMain = $branchContainsMain
        isolatedControlMatchesMain = $branchControlMatchesMain
        nextAction = 'Prepare and approve docs/DAILY_GOAL.md before a live start.'
    } | ConvertTo-Json
    exit 0
}
if (-not $goalReady) { throw 'docs/DAILY_GOAL.md is not READY.' }
if (-not $branchContainsMain) { throw 'The isolated night-shift branch does not contain current main. Codex must sync it before relay can start.' }
if (-not $branchControlMatchesMain) { throw 'The isolated branch overrides reviewed rules or automation controls. Codex must reconcile it with main before relay can start.' }

if (-not $ValidateOnly) {
    $authAudit = Join-Path $PSScriptRoot 'harden-minimax-auth.ps1'
    & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $authAudit | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'The private MiniMax relay credential copy is missing or unsafe. Run harden-minimax-auth.ps1 -Apply before unattended relay.' }
}

function Get-SectionText {
    param([Parameter(Mandatory)][string]$Heading)
    $escaped = [regex]::Escape($Heading)
    $match = [regex]::Match($goalText, "(?ms)^##\s+$escaped\s*\r?\n(.*?)(?=^##\s+|\z)")
    if (-not $match.Success) { throw "Goal section is missing: $Heading" }
    $match.Groups[1].Value.Trim()
}

function Get-Bullets {
    param([Parameter(Mandatory)][string]$Heading)
    $values = @()
    foreach ($line in (Get-SectionText -Heading $Heading) -split "`r?`n") {
        $match = [regex]::Match($line, '^\s*-\s+`?([^`]+?)`?\s*$')
        if ($match.Success) { $values += $match.Groups[1].Value.Trim() }
    }
    if ($values.Count -eq 0) { throw "Goal section contains no bullet values: $Heading" }
    $values
}

$goalLines = @((Get-SectionText -Heading 'Goal') -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($goalLines.Count -eq 0) { throw 'Goal text is empty.' }
$goal = ($goalLines -join ' ')
$allowedPaths = @(Get-Bullets -Heading 'Allowed scope')
$criteria = @(Get-Bullets -Heading 'Acceptance criteria')
$profiles = @(Get-Bullets -Heading 'Test profiles')
$forbidden = @(Get-Bullets -Heading 'Forbidden tonight')

$dayEntry = Join-Path $repoRoot 'scripts\nightshift\start-day-shift.ps1'
if (-not (Test-Path -LiteralPath $dayEntry -PathType Leaf)) { throw 'The audited daytime entry point is missing.' }

$parameters = @{
    Goal = $goal
    AllowedPath = $allowedPaths
    AcceptanceCriteria = $criteria
    TestProfile = $profiles
    Forbidden = $forbidden
}
if ($ValidateOnly) { $parameters.ValidateOnly = $true }
$env:DOMINION_RELAY_DISABLE_FILE = Join-Path $stateRoot 'AUTO_RELAY_DISABLED'
& $dayEntry @parameters
exit $LASTEXITCODE
