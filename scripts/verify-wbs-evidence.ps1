[CmdletBinding()]
param(
    [string]$WbsPath = 'docs/PROGRESS_WBS.md',
    [switch]$Strict
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$wbsFull = if ([IO.Path]::IsPathRooted($WbsPath)) { [IO.Path]::GetFullPath($WbsPath) } else { Join-Path $repoRoot $WbsPath }
if (-not (Test-Path -LiteralPath $wbsFull -PathType Leaf)) { throw "WBS file is missing: $wbsFull" }

$rows = @([IO.File]::ReadAllLines($wbsFull, [Text.Encoding]::UTF8) | Where-Object { $_ -match '^\|\s*10\.\d+\.\d+\s*\|' })
$results = [Collections.Generic.List[object]]::new()
foreach ($line in $rows) {
    $cells = @($line.Trim('|').Split('|') | ForEach-Object { $_.Trim() })
    if ($cells.Count -lt 5) { continue }
    $id = $cells[0]
    $statusCell = $cells[4]
    if ($statusCell -notmatch '^done\s*\(`?([0-9a-f]{7,40})`?\)') { continue }
    $hash = $matches[1]
    $commitType = (& git -C $repoRoot cat-file -t $hash 2>$null)
    $commitOk = ($LASTEXITCODE -eq 0 -and $commitType -eq 'commit')
    $evidenceCell = $cells[3]
    $paths = @([regex]::Matches($evidenceCell, '(?:scripts|docs|design|src|data|unity)/[A-Za-z0-9_./-]+') | ForEach-Object { $_.Value.TrimEnd('`', '.', ',', ';') } | Sort-Object -Unique)
    $missing = [Collections.Generic.List[string]]::new()
    foreach ($path in $paths) {
        if (-not (Test-Path -LiteralPath (Join-Path $repoRoot ($path.Replace('/', '\')))) ) { [void]$missing.Add($path) }
    }
    $status = if (-not $commitOk) { 'FAIL_COMMIT' } elseif ($missing.Count -gt 0) { 'FAIL_PATH' } else { 'PASS' }
    [void]$results.Add([pscustomobject]@{ id = $id; hash = $hash; commit = $commitOk; evidencePaths = $paths.Count; missing = @($missing); status = $status })
}

Write-Output ('DONE_EVIDENCE rows={0} pass={1} fail={2}' -f $results.Count, @($results | Where-Object status -eq 'PASS').Count, @($results | Where-Object status -ne 'PASS').Count)
$results | ForEach-Object {
    $missingText = if ($_.missing.Count) { $_.missing -join ',' } else { '-' }
    Write-Output ('{0} hash={1} commit={2} evidencePaths={3} missing={4} status={5}' -f $_.id, $_.hash, $_.commit, $_.evidencePaths, $missingText, $_.status)
}
if (@($results | Where-Object status -ne 'PASS').Count -gt 0 -and $Strict) { exit 2 }
exit 0
