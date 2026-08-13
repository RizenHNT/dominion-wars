[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$kitRoot = Join-Path $repoRoot 'design/runtime-kit-v1.30'
$assetRoot = Join-Path $kitRoot 'assets'
$manifestPath = Join-Path $kitRoot 'manifests/ASSET_MANIFEST.csv'
$alphaPath = Join-Path $kitRoot 'manifests/TRANSPARENT_ASSETS.csv'

foreach ($path in @($assetRoot, $manifestPath, $alphaPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Design manifest input is missing: $path" }
}

$manifest = @(Import-Csv -LiteralPath $manifestPath)
$alpha = @(Import-Csv -LiteralPath $alphaPath)
if ($manifest.Count -eq 0) { throw 'Design manifest is empty.' }

$duplicatePaths = @($manifest | Group-Object path | Where-Object Count -gt 1)
if ($duplicatePaths.Count) { throw 'Design manifest contains duplicate path entries.' }

$assetRootFull = [IO.Path]::GetFullPath($assetRoot).TrimEnd('\', '/')
$rows = [Collections.Generic.List[object]]::new()
foreach ($row in $manifest) {
    if ([string]::IsNullOrWhiteSpace($row.asset_id) -or [string]::IsNullOrWhiteSpace($row.path)) { throw 'Design manifest contains a row without asset_id/path.' }
    $relative = $row.path -replace '/', '\'
    if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains('..')) { throw "Manifest path is not relative: $($row.path)" }
    $full = [IO.Path]::GetFullPath((Join-Path $kitRoot $relative))
    if (-not $full.StartsWith($assetRootFull + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Manifest path escaped assets root: $($row.path)" }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Manifest asset is missing: $($row.path)" }
    $hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $row.sha256.ToLowerInvariant()) { throw "SHA256 mismatch for $($row.asset_id): expected $($row.sha256), actual $hash" }
    if ($row.alpha -notin @('yes', 'no')) { throw "Invalid alpha metadata for $($row.asset_id): $($row.alpha)" }
    $rows.Add([pscustomobject]@{ id = $row.asset_id; path = $row.path; hash = $hash })
}

$physical = @(Get-ChildItem -LiteralPath $assetRoot -Recurse -File | ForEach-Object {
    $_.FullName.Substring($kitRoot.Length).TrimStart('\', '/').Replace('\', '/')
})
$manifestPaths = @($manifest.path | ForEach-Object { $_.Replace('\', '/') })
$unlisted = @($physical | Where-Object { $_ -notin $manifestPaths })
$stale = @($manifestPaths | Where-Object { $_ -notin $physical })
if ($unlisted.Count -or $stale.Count) { throw "Manifest coverage mismatch: unlisted=$($unlisted.Count), stale=$($stale.Count)" }

foreach ($row in $alpha) {
    if ($row.path -notin $manifestPaths) { throw "Transparency metadata references an unlisted asset: $($row.path)" }
    if ($row.has_transparent_pixels -notin @('yes', 'no') -or $row.fully_transparent -notin @('yes', 'no')) {
        throw "Invalid transparency metadata: $($row.path)"
    }
    if ($row.width -notmatch '^\d+$' -or $row.height -notmatch '^\d+$' -or [int]$row.width -le 0 -or [int]$row.height -le 0) {
        throw "Invalid dimensions in transparency metadata: $($row.path)"
    }
}

$kindSummary = ($manifest | Group-Object kind | Sort-Object Name | ForEach-Object { '{0}={1}' -f $_.Name, $_.Count }) -join ', '
Write-Output ('DESIGN_MANIFEST pass=1 rows={0} files={1} transparentMetadata={2} kinds={3}' -f $manifest.Count, $physical.Count, $alpha.Count, $kindSummary)
