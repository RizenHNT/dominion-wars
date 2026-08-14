[CmdletBinding()]
param(
    [string[]]$Path = @('docs/RULES.md', 'docs/*.md', 'data/**/*.json', 'design/**/*.json', 'design/**/*.csv'),
    [switch]$Strict
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$files = [Collections.Generic.List[string]]::new()
foreach ($pattern in $Path) {
    $fullPattern = if ([IO.Path]::IsPathRooted($pattern)) { $pattern } else { Join-Path $repoRoot $pattern }
    $parent = Split-Path -Parent $fullPattern
    $leaf = Split-Path -Leaf $fullPattern
    if (Test-Path -LiteralPath $fullPattern -PathType Leaf) {
        [void]$files.Add((Resolve-Path -LiteralPath $fullPattern).Path)
    } elseif ($fullPattern -match '^(?<root>.+?)[\\/][*][*][\\/](?<leaf>[^\\/]+)$') {
        $globRoot = $Matches.root
        $globLeaf = $Matches.leaf
        if (Test-Path -LiteralPath $globRoot -PathType Container) {
            Get-ChildItem -LiteralPath $globRoot -Filter $globLeaf -File -Recurse -Force -ErrorAction Stop | ForEach-Object { [void]$files.Add($_.FullName) }
        }
    } elseif (Test-Path -LiteralPath $parent -PathType Container) {
        Get-ChildItem -LiteralPath $parent -Filter $leaf -File -Recurse -Force -ErrorAction Stop | ForEach-Object { [void]$files.Add($_.FullName) }
    }
}
$files = @($files | Sort-Object -Unique)
if ($files.Count -eq 0) { throw 'No text files matched.' }

$findings = [Collections.Generic.List[object]]::new()
foreach ($file in $files) {
    $relative = $file.Substring($repoRoot.TrimEnd('\').Length + 1).Replace('\', '/')
    try {
        $bytes = [IO.File]::ReadAllBytes($file)
        $text = [Text.Encoding]::UTF8.GetString($bytes)
        $roundTrip = [Text.Encoding]::UTF8.GetBytes($text)
        if (-not [Linq.Enumerable]::SequenceEqual($bytes, $roundTrip)) {
            [void]$findings.Add([pscustomobject]@{ path = $relative; line = 1; codepoint = 'INVALID_UTF8'; kind = 'decode' })
            continue
        }
        $lineNumber = 1
        foreach ($char in $text.ToCharArray()) {
            $code = [int][char]$char
            if (($code -lt 0x20 -and $code -notin @(0x09, 0x0A, 0x0D)) -or $code -eq 0x7F) {
                [void]$findings.Add([pscustomobject]@{ path = $relative; line = $lineNumber; codepoint = ('U+{0:X4}' -f $code); kind = 'control' })
            }
            if ($code -eq 0x0A) { $lineNumber++ }
        }
    } catch {
        [void]$findings.Add([pscustomobject]@{ path = $relative; line = 1; codepoint = 'READ_ERROR'; kind = 'read' })
    }
}

$invalid = @($findings | Where-Object kind -eq 'decode')
$controls = @($findings | Where-Object kind -eq 'control')
Write-Output ('TEXT_INTEGRITY files={0} invalidUtf8={1} controls={2}' -f $files.Count, $invalid.Count, $controls.Count)
foreach ($finding in $findings) {
    Write-Output ('{0}:{1} {2} {3}' -f $finding.path, $finding.line, $finding.codepoint, $finding.kind)
}
if ($Strict -and $findings.Count -gt 0) { exit 2 }
exit 0
