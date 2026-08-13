[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$csharpActionsPath = Join-Path $repoRoot 'src/Engine/Effects/EffectNames.cs'
$javaEffectsPath = Join-Path $repoRoot 'src/main/java/com/dominionwars/engine/Effects.java'
$csharpTargetsPath = Join-Path $repoRoot 'src/Engine/Effects/EffectTargetResolver.cs'
$javaCardDefPath = Join-Path $repoRoot 'src/main/java/com/dominionwars/model/CardDef.java'
$csharpCardDefPath = Join-Path $repoRoot 'src/Engine/Model/CardDefinition.cs'

foreach ($path in @($csharpActionsPath, $javaEffectsPath, $csharpTargetsPath, $javaCardDefPath, $csharpCardDefPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required alignment source is missing: $path" }
}

function Get-Matches([string]$Text, [string]$Pattern) {
    return @([regex]::Matches($Text, $Pattern, [Text.RegularExpressions.RegexOptions]::Multiline) |
        ForEach-Object { $_.Groups[1].Value })
}

function Get-SwitchCases([string]$Text, [string]$Anchor) {
    $anchorIndex = $Text.IndexOf($Anchor, [StringComparison]::Ordinal)
    if ($anchorIndex -lt 0) { return @() }
    $tail = $Text.Substring($anchorIndex)
    $end = $tail.IndexOf('default:', [StringComparison]::Ordinal)
    if ($end -ge 0) { $tail = $tail.Substring(0, $end) }
    return @(Get-Matches $tail 'case\s+"([A-Z][A-Z0-9_]*)"\s*:') | Sort-Object -Unique
}

$csharpActions = @(Get-Matches ([IO.File]::ReadAllText($csharpActionsPath)) 'const\s+string\s+\w+\s*=\s*"([A-Z][A-Z0-9_]*)"') | Sort-Object -Unique
$javaActions = @(Get-SwitchCases ([IO.File]::ReadAllText($javaEffectsPath)) 'switch (e.action)')
$csharpTargets = @(Get-SwitchCases ([IO.File]::ReadAllText($csharpTargetsPath)) 'switch (target)')
$javaTargets = @(Get-SwitchCases ([IO.File]::ReadAllText($javaEffectsPath)) 'switch (e.target)')

$csharpFields = @(Get-Matches ([IO.File]::ReadAllText($csharpCardDefPath)) 'public\s+(?:string\??|int|bool|IReadOnlyCollection<string>)\s+(\w+)\s*(?:\{|=>)') | Sort-Object -Unique
$javaFields = @(Get-Matches ([IO.File]::ReadAllText($javaCardDefPath)) 'public\s+(?:String|int|boolean|List<[^>]+>|Set<[^>]+>|CardType|LeaderDef)\s+(\w+)\s*(?:=|;)') | Sort-Object -Unique

$actionOnlyCSharp = @($csharpActions | Where-Object { $_ -notin $javaActions })
$actionOnlyJava = @($javaActions | Where-Object { $_ -notin $csharpActions })
$targetOnlyCSharp = @($csharpTargets | Where-Object { $_ -notin $javaTargets })
$targetOnlyJava = @($javaTargets | Where-Object { $_ -notin $csharpTargets })
$fieldOnlyCSharp = @($csharpFields | Where-Object { $_ -notin $javaFields })
$fieldOnlyJava = @($javaFields | Where-Object { $_ -notin $csharpFields })

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# C#/Java alignment report')
$lines.Add('')
$lines.Add(('Generated: {0}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')))
$lines.Add('')
$lines.Add('This is a static inventory, not a parity approval. Differences requiring rule, target, or contract decisions remain HUMAN_REQUIRED/PL-owned.')
$lines.Add('')
$lines.Add('## Effects')
$lines.Add('')
$lines.Add(('| C# actions | Java action cases | C# only | Java only |' ))
$lines.Add('|---:|---:|---|---|')
$lines.Add(('| {0} | {1} | {2} | {3} |' -f $csharpActions.Count, $javaActions.Count, ($(if ($actionOnlyCSharp.Count) { $actionOnlyCSharp -join ', ' } else { 'none' })), ($(if ($actionOnlyJava.Count) { $actionOnlyJava -join ', ' } else { 'none' }))))
$lines.Add('')
$lines.Add('C# source: `src/Engine/Effects/EffectNames.cs`; Java source: `src/main/java/com/dominionwars/engine/Effects.java` (`switch (e.action)`).')
$lines.Add('')
$lines.Add('## Targets')
$lines.Add('')
$lines.Add('| C# resolver cases | Java resolver cases | C# only | Java only |')
$lines.Add('|---:|---:|---|---|')
$lines.Add(('| {0} | {1} | {2} | {3} |' -f $csharpTargets.Count, $javaTargets.Count, ($(if ($targetOnlyCSharp.Count) { $targetOnlyCSharp -join ', ' } else { 'none' })), ($(if ($targetOnlyJava.Count) { $targetOnlyJava -join ', ' } else { 'none' }))))
$lines.Add('')
$lines.Add('Target inventory is syntax-level only. Core targets and pending-choice semantics still require the approved target-union design; this report does not authorize silently selecting a target.')
$lines.Add('')
$lines.Add('## Card fields')
$lines.Add('')
$lines.Add('| C# CardDefinition fields | Java CardDef fields | C# only | Java only |')
$lines.Add('|---:|---:|---|---|')
$lines.Add(('| {0} | {1} | {2} | {3} |' -f $csharpFields.Count, $javaFields.Count, ($(if ($fieldOnlyCSharp.Count) { $fieldOnlyCSharp -join ', ' } else { 'none' })), ($(if ($fieldOnlyJava.Count) { $fieldOnlyJava -join ', ' } else { 'none' }))))
$lines.Add('')
$lines.Add('Known non-equivalence requiring follow-up: Java retains ambush/chant/discard-hook and full LeaderDef fields; the current C# CardDefinition intentionally exposes only the approved loader/engine subset. Do not treat the inventory as permission to expand the model.')
$lines.Add('')
$lines.Add('## Follow-up queue')
$lines.Add('')
$lines.Add('- PL/HUMAN_REQUIRED: approve target union and core-target behavior before changing `ENEMY_TARGET`/`ENEMY_FACE` semantics.')
$lines.Add('- PL/HUMAN_REQUIRED: decide whether Java-only persistent/ambush/chant fields enter the next C# batch or remain Java-authoritative.')
$lines.Add('- Codex/DeepSeek: add focused parity tests only after those decisions; Unity runtime validation remains separately blocked by Hub/licensing.')

$report = $lines -join [Environment]::NewLine
if ($OutputPath) {
    $fullOutput = [IO.Path]::GetFullPath($OutputPath)
    $parent = Split-Path -Parent $fullOutput
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [IO.File]::WriteAllText($fullOutput, $report + [Environment]::NewLine, [Text.Encoding]::UTF8)
    Write-Output ("ALIGNMENT_REPORT path={0} actions(csharp/java)={1}/{2} targets(csharp/java)={3}/{4}" -f $fullOutput, $csharpActions.Count, $javaActions.Count, $csharpTargets.Count, $javaTargets.Count)
} else {
    Write-Output $report
}
