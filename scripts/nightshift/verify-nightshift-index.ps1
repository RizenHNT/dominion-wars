[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$indexPath = Join-Path $repoRoot 'docs\FILE_INDEX.md'

if (-not (Test-Path -LiteralPath $indexPath)) {
    throw 'docs/FILE_INDEX.md does not exist.'
}

$content = Get-Content -Raw -LiteralPath $indexPath
$requiredPaths = @(
    '.codex/config.toml',
    'docs/DAILY_GOAL.md',
    'docs/NIGHTSHIFT_WORKFLOW.md',
    'scripts/nightshift/nightshift.config.json',
    'scripts/nightshift/invoke-command.ps1',
    'scripts/nightshift/invoke-scheduled-nightshift.ps1',
    'scripts/nightshift/run-nightshift.ps1',
    'scripts/nightshift/setup-nightshift-task.ps1',
    'scripts/nightshift/start-day-shift.ps1',
    'scripts/nightshift/setup-deepseek-key.ps1',
    'scripts/nightshift/verify-nightshift-index.ps1'
)

$missing = @($requiredPaths | Where-Object { -not $content.Contains($_) })
if ($missing.Count -gt 0) {
    Write-Error "Missing night-shift index entries: $($missing -join ', ')"
    exit 1
}

$mentionsRuntimeDirectory = $content.Contains('.nightshift/') -and $content.Contains('%LOCALAPPDATA%\DominionWarsNightshift\state')
$mentionsLocal = $content -match '(?i)local|本地'
$mentionsIgnored = $content -match '(?i)git.?ignored|ignored by git|Git.?忽略|忽略.*Git'
if (-not ($mentionsRuntimeDirectory -and $mentionsLocal -and $mentionsIgnored)) {
    Write-Error 'The index must state that live state is under private LOCALAPPDATA and simulation state under local Git-ignored .nightshift/.'
    exit 1
}

Write-Host 'Night-shift file index verification passed.'
