[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string[]]$TargetPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Convert-ToRepoRelativePath {
    param([Parameter(Mandatory)][string]$Path)

    $candidate = $Path
    if (-not [IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $repoRoot $candidate
    }
    $full = [IO.Path]::GetFullPath($candidate)
    $rootWithSlash = $repoRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $full.Equals($repoRoot, [StringComparison]::OrdinalIgnoreCase) -and
        -not $full.StartsWith($rootWithSlash, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Target path is outside repository: $Path"
    }
    if ($full.Equals($repoRoot, [StringComparison]::OrdinalIgnoreCase)) { return '' }
    return $full.Substring($rootWithSlash.Length).Replace('\', '/')
}

function Get-GitStatusPaths {
    $raw = & git -C $repoRoot status --porcelain=v1 -z --untracked-files=all
    if ($LASTEXITCODE -ne 0) { throw 'git status failed.' }
    $records = @([string]$raw -split "`0" | Where-Object { $_.Length -gt 0 })
    $paths = [Collections.Generic.List[string]]::new()
    foreach ($record in $records) {
        if ($record.Length -lt 4) { continue }
        $pathText = $record.Substring(3)
        if ($pathText -match '^(.*?)\s+->\s+(.*)$') {
            [void]$paths.Add($matches[1].Replace('\', '/'))
            [void]$paths.Add($matches[2].Replace('\', '/'))
        } else {
            [void]$paths.Add($pathText.Replace('\', '/'))
        }
    }
    return @($paths | Sort-Object -Unique)
}

function Test-PathOverlap {
    param(
        [Parameter(Mandatory)][string]$Target,
        [Parameter(Mandatory)][string]$Dirty
    )
    if ($Target -eq '') { return $true }
    return $Dirty.Equals($Target, [StringComparison]::OrdinalIgnoreCase) -or
        $Dirty.StartsWith($Target.TrimEnd('/') + '/', [StringComparison]::OrdinalIgnoreCase)
}

$dirtyPaths = @(Get-GitStatusPaths)
$targets = [Collections.Generic.List[string]]::new()
$overlaps = [Collections.Generic.List[string]]::new()

Write-Output ('SCOPE_CAPTURE utc={0} local={1}' -f (Get-Date).ToUniversalTime().ToString('o'), (Get-Date).ToString('o'))
Write-Output ('repo={0}' -f $repoRoot)
Write-Output ('branch={0}' -f (& git -C $repoRoot branch --show-current))
Write-Output ('head={0}' -f (& git -C $repoRoot rev-parse HEAD))
Write-Output ('dirtyCount={0}' -f $dirtyPaths.Count)
Write-Output 'dirtyPaths:'
foreach ($path in $dirtyPaths) { Write-Output ('- {0}' -f $path) }

if ($null -ne $TargetPath) {
    foreach ($inputPath in $TargetPath) {
        if ([string]::IsNullOrWhiteSpace($inputPath)) { throw 'TargetPath cannot be empty.' }
        $relative = Convert-ToRepoRelativePath $inputPath
        [void]$targets.Add($relative)
        $absolute = if ($relative -eq '') { $repoRoot } else { Join-Path $repoRoot ($relative.Replace('/', '\')) }
        if (Test-Path -LiteralPath $absolute -PathType Leaf) {
            $hash = (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash
            Write-Output ('target={0} kind=file sha256={1}' -f $relative, $hash)
        } elseif (Test-Path -LiteralPath $absolute -PathType Container) {
            $fileCount = @(Get-ChildItem -LiteralPath $absolute -File -Recurse -Force -ErrorAction Stop).Count
            Write-Output ('target={0} kind=directory files={1}' -f $relative, $fileCount)
        } else {
            Write-Output ('target={0} kind=missing' -f $relative)
        }
        foreach ($dirty in $dirtyPaths) {
            if (Test-PathOverlap -Target $relative -Dirty $dirty) {
                [void]$overlaps.Add($dirty)
            }
        }
    }
}

$overlaps = @($overlaps | Sort-Object -Unique)
if ($overlaps.Count -gt 0) {
    Write-Output 'OVERLAP=TRUE'
    foreach ($path in $overlaps) { Write-Output ('overlapPath={0}' -f $path) }
    exit 2
}

Write-Output 'OVERLAP=FALSE'
exit 0
