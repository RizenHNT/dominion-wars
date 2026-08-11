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
$runner = Join-Path $worktree 'scripts\nightshift\run-nightshift.ps1'
$stateHasher = [Security.Cryptography.SHA256]::Create()
try {
    $stateKey = ([BitConverter]::ToString($stateHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($worktree)))).Replace('-', '').Substring(0, 16).ToLowerInvariant()
}
finally { $stateHasher.Dispose() }
$dayStateRoot = Join-Path (Join-Path $env:LOCALAPPDATA 'DominionWarsNightshift\state') $stateKey
$goalFile = Join-Path $dayStateRoot 'day-goal.md'

if (-not (Test-Path -LiteralPath $runner)) {
    throw "Isolated night-shift worktree is unavailable: $worktree"
}

$branch = Get-BranchName -Repository $worktree
if (-not $branch.StartsWith('agents/nightshift-', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to start outside an agents/nightshift branch: $branch"
}

$existingChanges = @(& git -C $worktree status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect isolated worktree status.' }
if ($existingChanges.Count -gt 0) {
    throw "The isolated worktree is not clean. Review the previous report and changes first: $($existingChanges -join '; ')"
}

function Normalize-RepoRelativePath {
    param([Parameter(Mandatory)][string]$Path)
    $normalized = $Path.Replace('\', '/')
    if (-not $normalized -or $normalized -ne $normalized.Trim() -or $normalized.Length -gt 500 -or
        $normalized -match '[\x00-\x1F]' -or [System.IO.Path]::IsPathRooted($normalized) -or
        $normalized.StartsWith('/') -or $normalized.StartsWith('//') -or $normalized -match '^[A-Za-z]:' -or
        $normalized.Contains(':') -or $normalized.IndexOfAny([char[]]'*?[]') -ge 0) {
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

function Test-SafeAllowedPath {
    param([Parameter(Mandatory)][string]$Path)
    try { $normalized = Normalize-RepoRelativePath -Path $Path } catch { return $false }
    if (@('src', 'data', 'scripts', 'docs', 'web', 'design', '.github', '.vscode', '.codex') -contains $normalized) { return $false }
    foreach ($entry in @(
        '.git', '.nightshift', '.github/agents', '.github/workflows', '.vscode', '.codex', 'scripts/nightshift',
        'AGENTS.md', '.gitignore', 'docs/AI_WORKFLOW.md', 'docs/NIGHTSHIFT_WORKFLOW.md',
        'docs/DAILY_GOAL.md', 'docs/NIGHT_REPORT.md', 'docs/AI_MAILBOX.md'
    )) {
        if ($normalized -eq $entry -or $normalized.StartsWith("$entry/")) { return $false }
    }
    if ($normalized -match '(?i)(^|/)(\.env(?:\.|$)|[^/]*\.(?:key|pem|pfx|p12)|credentials?(?:\.|/|$)|secrets?(?:\.|/|$))') { return $false }
    $cursor = $worktree
    foreach ($part in $normalized.Split('/')) {
        $cursor = Join-Path $cursor $part
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -Force -LiteralPath $cursor
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { return $false }
        }
    }
    return $true
}

function Assert-PlainInput {
    param(
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][int]$MaximumLength,
        [switch]$RejectHeadingPrefix
    )
    if (-not $Value.Trim() -or $Value.Length -gt $MaximumLength -or
        $Value -match '[\r\n\p{Zl}\p{Zp}]' -or $Value -match '[\x00-\x08\x0B\x0C\x0E-\x1F]') {
        throw "$Label must be non-empty, single-line plain text no longer than $MaximumLength characters."
    }
    if ($RejectHeadingPrefix -and $Value -match '(?i)^\s*(?:#|Status\s*:|Date\s*:)') {
        throw "$Label starts with a reserved Markdown control field."
    }
}

function Initialize-SafeDayStateDirectory {
    $trustedRoot = [IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\', '/')
    $target = [IO.Path]::GetFullPath($dayStateRoot).TrimEnd('\', '/')
    if (-not $target.StartsWith($trustedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Day state path escaped LOCALAPPDATA.' }
    $cursor = $trustedRoot
    $relative = $target.Substring($trustedRoot.Length).TrimStart('\', '/')
    foreach ($part in $relative.Split([char[]]@('\', '/'), [StringSplitOptions]::RemoveEmptyEntries)) {
        $cursor = Join-Path $cursor $part
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Unsafe local state component: $cursor"
            }
        }
        else { New-Item -ItemType Directory -Path $cursor | Out-Null }
    }
}

$normalizedAllowedPaths = @($AllowedPath | ForEach-Object {
    if (-not (Test-SafeAllowedPath -Path $_)) { throw "Unsafe or overly broad allowed path: $_" }
    Normalize-RepoRelativePath -Path $_
})
if ($normalizedAllowedPaths.Count -ne @($normalizedAllowedPaths | Sort-Object -Unique).Count) {
    throw 'Allowed paths contain duplicates.'
}
$normalizedTestProfiles = @($TestProfile | Sort-Object -Unique)
Assert-PlainInput -Value $Goal -Label 'Goal' -MaximumLength 2000 -RejectHeadingPrefix
if ($AcceptanceCriteria.Count -gt 20) { throw 'AcceptanceCriteria may contain at most 20 entries.' }
foreach ($criterion in $AcceptanceCriteria) { Assert-PlainInput -Value $criterion -Label 'Acceptance criterion' -MaximumLength 1000 }
if ($Forbidden.Count -gt 20) { throw 'Forbidden may contain at most 20 entries.' }
foreach ($item in $Forbidden) { Assert-PlainInput -Value $item -Label 'Forbidden item' -MaximumLength 1000 }

if ($ValidateOnly) {
    [pscustomobject]@{
        Mode = 'VALIDATE_ONLY'
        Branch = $branch
        WorktreeClean = $true
        Goal = $Goal
        AllowedPath = $normalizedAllowedPaths
        AcceptanceCriteria = $AcceptanceCriteria
        TestProfile = $normalizedTestProfiles
        Runner = $runner
        GoalInputFile = $goalFile
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

$(ConvertTo-BulletLines $normalizedAllowedPaths)

## Acceptance criteria

$(ConvertTo-BulletLines $AcceptanceCriteria)

## Test profiles

$(ConvertTo-BulletLines $normalizedTestProfiles)

## Human decisions already made

- The human owner explicitly approved this daytime automated run and the scope above.

## Forbidden tonight

$(ConvertTo-BulletLines $Forbidden)
"@

Initialize-SafeDayStateDirectory
$temporaryGoal = Join-Path $dayStateRoot ('.day-goal.{0}.tmp' -f [Guid]::NewGuid().ToString('N'))
[System.IO.File]::WriteAllText($temporaryGoal, $goalDocument, [System.Text.UTF8Encoding]::new($false))
try {
    if (Test-Path -LiteralPath $goalFile -PathType Leaf) {
        try { [System.IO.File]::Replace($temporaryGoal, $goalFile, $null) }
        catch { Move-Item -LiteralPath $temporaryGoal -Destination $goalFile -Force }
    }
    else { Move-Item -LiteralPath $temporaryGoal -Destination $goalFile }
}
finally {
    if (Test-Path -LiteralPath $temporaryGoal) { Remove-Item -LiteralPath $temporaryGoal -Force }
}

$roundTripText = Get-Content -Raw -LiteralPath $goalFile
$allowedMatch = [regex]::Match($roundTripText, '(?ms)^## Allowed scope\s*(.*?)^## ')
$profilesMatch = [regex]::Match($roundTripText, '(?ms)^## Test profiles\s*(.*?)(?=^## |\z)')
if (-not $allowedMatch.Success -or -not $profilesMatch.Success) { throw 'Generated day goal failed section round-trip validation.' }
$roundTripPaths = @($allowedMatch.Groups[1].Value -split "`r?`n" | ForEach-Object {
    $match = [regex]::Match($_, '^\s*-\s+`?([^`]+)`?\s*$')
    if ($match.Success) { $match.Groups[1].Value.Trim() }
} | Where-Object { $_ } | Sort-Object -Unique)
$roundTripProfiles = @($profilesMatch.Groups[1].Value -split "`r?`n" | ForEach-Object {
    $match = [regex]::Match($_, '^\s*-\s+`?([^`\s]+)`?\s*$')
    if ($match.Success) { $match.Groups[1].Value.Trim() }
} | Where-Object { $_ } | Sort-Object -Unique)
if (Compare-Object $normalizedAllowedPaths $roundTripPaths) { throw 'Generated day goal changed the approved allowed paths.' }
if (Compare-Object $normalizedTestProfiles $roundTripProfiles) { throw 'Generated day goal changed the approved test profiles.' }

& powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $runner -GoalFile $goalFile
exit $LASTEXITCODE
