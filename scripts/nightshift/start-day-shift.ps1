[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Goal,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string[]]$AllowedPath,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string[]]$AcceptanceCriteria,
    [Parameter(Mandatory)][ValidateSet('build', 'regression', 'sanity', 'alignment', 'nightshift-index')][string[]]$TestProfile,
    [switch]$ValidateOnly,
    [string[]]$Forbidden = @(
        'Architecture, rule, balance, visual, release, credential, billing, dependency, push, merge, and destructive deletion changes not explicitly approved above.'
    )
)

$ErrorActionPreference = 'Stop'
$mainRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$worktree = Join-Path $mainRoot '.nightshift\rehearsal'
$runner = Join-Path $worktree 'scripts\nightshift\run-nightshift.ps1'
$goalFile = Join-Path $worktree 'docs\DAILY_GOAL.md'

if (-not (Test-Path -LiteralPath $runner)) {
    throw "Isolated night-shift worktree is unavailable: $worktree"
}

$branch = (& git -C $worktree branch --show-current).Trim()
if (-not $branch.StartsWith('agents/nightshift')) {
    throw "Refusing to start outside an agents/nightshift branch: $branch"
}

$existingChanges = @(& git -C $worktree status --porcelain)
if ($existingChanges.Count -gt 0) {
    throw "The isolated worktree is not clean. Review the previous report and changes first: $($existingChanges -join '; ')"
}

foreach ($path in $AllowedPath) {
    $normalized = $path.Replace('\', '/').Trim().TrimStart('./').TrimEnd('/')
    if (-not $normalized -or $normalized -in @('.git', '.nightshift', 'AGENTS.md', 'docs/AI_WORKFLOW.md', 'docs/NIGHTSHIFT_WORKFLOW.md', '.github/agents')) {
        throw "Unsafe or overly broad allowed path: $path"
    }
}

if ($ValidateOnly) {
    [pscustomobject]@{
        Mode = 'VALIDATE_ONLY'
        Branch = $branch
        WorktreeClean = $true
        Goal = $Goal
        AllowedPath = $AllowedPath
        AcceptanceCriteria = $AcceptanceCriteria
        TestProfile = $TestProfile
        Runner = $runner
    } | ConvertTo-Json -Depth 5
    exit 0
}

function ConvertTo-BulletLines {
    param([string[]]$Values)
    ($Values | ForEach-Object { "- $($_.Trim())" }) -join [Environment]::NewLine
}

$goalDocument = @"
# Daily Goal

Status: READY
Date: $(Get-Date -Format 'yyyy-MM-dd')

## Goal

$($Goal.Trim())

## Allowed scope

$(ConvertTo-BulletLines $AllowedPath)

## Acceptance criteria

$(ConvertTo-BulletLines $AcceptanceCriteria)

## Test profiles

$(ConvertTo-BulletLines $TestProfile)

## Human decisions already made

- The human owner explicitly approved this daytime automated run and the scope above.

## Forbidden tonight

$(ConvertTo-BulletLines $Forbidden)
"@

[System.IO.File]::WriteAllText($goalFile, $goalDocument, [System.Text.UTF8Encoding]::new($false))

& git -C $worktree add -- docs/DAILY_GOAL.md
if ($LASTEXITCODE -ne 0) { throw 'Failed to stage the approved day goal.' }

$stagedPaths = @(& git -C $worktree diff --cached --name-only)
if ($stagedPaths.Count -ne 1 -or $stagedPaths[0] -ne 'docs/DAILY_GOAL.md') {
    throw "Refusing to commit unexpected paths: $($stagedPaths -join ', ')"
}

& git -C $worktree commit -m "plan: prepare approved daytime automation goal"
if ($LASTEXITCODE -ne 0) { throw 'Failed to commit the approved day goal.' }

& powershell -NoProfile -ExecutionPolicy Bypass -File $runner
exit $LASTEXITCODE
