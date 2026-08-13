[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$wbsPath = Join-Path $repoRoot 'docs/PROGRESS_WBS.md'
$todoPath = Join-Path $repoRoot 'design/runtime-kit-v1.30/manifests/IMPLEMENTATION_TODO.csv'

foreach ($path in @($wbsPath, $todoPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Workflow source is missing: $path" }
}

$wbs = [IO.File]::ReadAllText($wbsPath, [Text.Encoding]::UTF8)
$todo = @(Import-Csv -LiteralPath $todoPath)
$wbsRows = @([regex]::Matches($wbs, '(?m)^\|\s*(10\.\d+\.\d+)\s*\|([^|]*)\|([^|]*)\|([^|]*)\|\s*([^|]+)\|([^|]*)\|') | ForEach-Object {
    [pscustomobject]@{ id = $_.Groups[1].Value; title = $_.Groups[2].Value.Trim(); status = $_.Groups[5].Value.Trim(); evidence = $_.Groups[4].Value.Trim() }
})
if ($wbsRows.Count -eq 0) { throw 'No expanded WBS rows were found.' }

$validStatuses = @('done', 'ready', 'pending', 'blocked', 'human_required')
$unknownWbs = @($wbsRows | Where-Object { $status = $_.status.ToLowerInvariant(); -not ($validStatuses | Where-Object { $status.StartsWith($_) }) })
$todoStatuses = @($todo | Group-Object status | Sort-Object Name | ForEach-Object { '{0}={1}' -f $_.Name, $_.Count })
$wbsStatuses = @($wbsRows | Group-Object { ($_.status -split '\s')[0].ToLowerInvariant() } | Sort-Object Name | ForEach-Object { '{0}={1}' -f $_.Name, $_.Count })
$missingEvidence = @($wbsRows | Where-Object { $_.status -like 'done*' -and [string]::IsNullOrWhiteSpace($_.evidence) })
$placeholderEvidence = @($wbsRows | Where-Object { $_.status -like 'done*' -and $_.evidence -match '待提交|TBD|TODO' })
$hasRollback = $wbs -match '(?m)^###\s+10\.9\b'

$gitStatus = @(git -C $repoRoot status --short)
$trackedDirty = @($gitStatus | Where-Object { $_ -match '^\s*[ MARCUD][ MARCUD]\s' })
$untracked = @($gitStatus | Where-Object { $_ -match '^\?\?' })
$recent = @(git -C $repoRoot log -12 --format='%h|%s')
$recentTaskCommits = @($recent | Where-Object { $_ -match 'WBS-|alignment|manifest|architecture|contract gap|strict Unity|stability' })

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Workflow state audit')
$lines.Add('')
$lines.Add(('Generated: {0}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')))
$lines.Add('')
$lines.Add('Read-only audit for temporary-agent handoff. It does not approve work, alter WBS/TODO state, or clean dirty files.')
$lines.Add('')
$lines.Add('## Counts')
$lines.Add('')
$lines.Add(('| Source | Counts |'))
$lines.Add('|---|---|')
$lines.Add(('| Expanded WBS | {0} |' -f ($wbsStatuses -join ', ')))
$lines.Add(('| IMPLEMENTATION_TODO.csv | {0} |' -f ($(if ($todoStatuses.Count) { $todoStatuses -join ', ' } else { 'empty' }))))
$lines.Add(('| Git dirty tracked paths | {0} |' -f $trackedDirty.Count))
$lines.Add(('| Git untracked paths | {0} |' -f $untracked.Count))
$lines.Add('')
$lines.Add('## Gates')
$lines.Add('')
$lines.Add(('| WBS rows with unknown status | {0} |' -f $unknownWbs.Count))
$lines.Add(('| Done rows without evidence | {0} |' -f $missingEvidence.Count))
$lines.Add(('| Done rows with placeholder evidence | {0} |' -f $placeholderEvidence.Count))
$lines.Add(('| Temporary rollback protocol present | {0} |' -f $hasRollback))
$lines.Add('')
$lines.Add('## Recent implementation commits')
$lines.Add('')
foreach ($commit in $recentTaskCommits) { $lines.Add(('- {0}' -f $commit)) }
if ($recentTaskCommits.Count -eq 0) { $lines.Add('- none detected') }
$lines.Add('')
$lines.Add('## Interpretation')
$lines.Add('')
$lines.Add('- This audit compares inventories; it does not infer that a WBS `done` row means Unity or full playable-loop completion.')
$lines.Add('- Existing dirty and untracked paths are preserved and are not candidates for automatic staging.')
$lines.Add('- Any status correction in BASELINE, CURRENT_IMPLEMENTATION_STATUS, TODO or AI_MAILBOX remains a separate PL/Codex documentation task.')

$report = $lines -join [Environment]::NewLine
if ($OutputPath) {
    $full = [IO.Path]::GetFullPath($OutputPath)
    $parent = Split-Path -Parent $full
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [IO.File]::WriteAllText($full, $report + [Environment]::NewLine, [Text.Encoding]::UTF8)
    Write-Output ("WORKFLOW_AUDIT path={0} wbs={1} todo={2} trackedDirty={3} untracked={4}" -f $full, $wbsRows.Count, $todo.Count, $trackedDirty.Count, $untracked.Count)
} else {
    Write-Output $report
}
