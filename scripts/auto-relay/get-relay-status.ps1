[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'relay-common.ps1')
$repoRoot = Get-RelayRepositoryRoot -ScriptDirectory $PSScriptRoot
$relayStateRoot = Get-RelayStateRoot -RepositoryRoot $repoRoot
$nightWorktree = Join-Path $repoRoot '.nightshift\rehearsal'

function Get-NightStateRoot {
    param([Parameter(Mandatory)][string]$Worktree)
    $canonical = [IO.Path]::GetFullPath($Worktree).TrimEnd('\', '/')
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $key = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical)))).Replace('-', '').Substring(0, 16).ToLowerInvariant() }
    finally { $sha.Dispose() }
    Join-Path (Join-Path $env:LOCALAPPDATA 'DominionWarsNightshift\state') $key
}

function Read-JsonIfPresent {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try { Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json } catch { [pscustomobject]@{ parseError = $_.Exception.Message; path = $Path } }
}

$nightStateRoot = Get-NightStateRoot -Worktree $nightWorktree
$latestRunState = $null
$latestUsage = $null
$latestStateFile = $null
if (Test-Path -LiteralPath $nightStateRoot -PathType Container) {
    $latestStateFile = Get-ChildItem -LiteralPath $nightStateRoot -Filter state.json -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($latestStateFile) {
        $latestRunState = Read-JsonIfPresent -Path $latestStateFile.FullName
        $latestUsage = Read-JsonIfPresent -Path (Join-Path $latestStateFile.DirectoryName 'usage.json')
    }
}
$probeRoot = Join-Path $relayStateRoot 'minimax-probes'
$latestProbe = $null
if (Test-Path -LiteralPath $probeRoot -PathType Container) {
    $latestProbeFile = Get-ChildItem -LiteralPath $probeRoot -Filter result.json -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($latestProbeFile) { $latestProbe = Read-JsonIfPresent -Path $latestProbeFile.FullName }
}

[pscustomobject]@{
    disabled = (Test-RelayDisabled -StateRoot $relayStateRoot)
    relayStateRoot = $relayStateRoot
    isolatedWorktree = $nightWorktree
    nightStatus = Read-JsonIfPresent -Path (Join-Path $nightStateRoot 'last-scheduled-status.json')
    goalLedger = Read-JsonIfPresent -Path (Join-Path $nightStateRoot 'goal-ledger.json')
    latestRun = $latestRunState
    latestRunUpdatedAt = $(if ($latestStateFile) { $latestStateFile.LastWriteTimeUtc.ToString('o') } else { $null })
    latestUsage = $latestUsage
    latestMiniMaxProbe = $latestProbe
    report = (Join-Path $nightWorktree 'docs\NIGHT_REPORT.md')
} | ConvertTo-Json -Depth 20
