[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$contracts = Join-Path $repoRoot 'design/runtime-kit-v1.30/contracts'
$cardSchemaPath = Join-Path $repoRoot 'data/schema/cards.schema.json'
$effectNamesPath = Join-Path $repoRoot 'src/Engine/Effects/EffectNames.cs'
$legalActionsPath = Join-Path $repoRoot 'src/Engine/LegalActionGenerator.cs'
$javaEffectsPath = Join-Path $repoRoot 'src/main/java/com/dominionwars/engine/Effects.java'
$targetResolverPath = Join-Path $repoRoot 'src/Engine/Effects/EffectTargetResolver.cs'
$adapterPath = Join-Path $repoRoot 'src/Adapters/EngineProjectionAdapter.cs'

function Read-Json([string]$Path) { return ([IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8) | ConvertFrom-Json) }
function Require-File([string]$Path) { if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing contract source: $Path" } }
function Get-RegexValues([string]$Text, [string]$Pattern) {
    return @([regex]::Matches($Text, $Pattern, [Text.RegularExpressions.RegexOptions]::Multiline) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
}
function Get-SwitchSection([string]$Text, [string]$SwitchText, [string]$NextSwitchText) {
    $start = $Text.IndexOf($SwitchText, [StringComparison]::Ordinal)
    if ($start -lt 0) { return '' }
    $section = $Text.Substring($start)
    if ($NextSwitchText) {
        $end = $section.IndexOf($NextSwitchText, [StringComparison]::Ordinal)
        if ($end -gt 0) { $section = $section.Substring(0, $end) }
    }
    return $section
}
function Join-OrNone([object[]]$Values) { if ($Values.Count) { return ($Values -join ', ') }; return 'none' }

$sources = @($cardSchemaPath, $effectNamesPath, $legalActionsPath, $javaEffectsPath, $targetResolverPath, $adapterPath,
    (Join-Path $contracts 'game_action.schema.json'), (Join-Path $contracts 'ui_event.schema.json'), (Join-Path $contracts 'battle_state_machine.json'))
$sources | ForEach-Object { Require-File $_ }

$cardSchema = Read-Json $cardSchemaPath
$actionSchema = Read-Json (Join-Path $contracts 'game_action.schema.json')
$eventSchema = Read-Json (Join-Path $contracts 'ui_event.schema.json')
$stateMachine = Read-Json (Join-Path $contracts 'battle_state_machine.json')
$effectNames = [IO.File]::ReadAllText($effectNamesPath)
$legalActions = [IO.File]::ReadAllText($legalActionsPath)
$javaEffects = [IO.File]::ReadAllText($javaEffectsPath)
$targetResolver = [IO.File]::ReadAllText($targetResolverPath)
$adapter = [IO.File]::ReadAllText($adapterPath)

$effectActions = @(Get-RegexValues $effectNames 'const\s+string\s+\w+\s*=\s*"([A-Z][A-Z0-9_]*)"')
$javaActionCases = @(Get-RegexValues (Get-SwitchSection $javaEffects 'switch (e.action)' 'switch (e.target)') 'case\s+"([A-Z][A-Z0-9_]*)"\s*:')
$cardActions = @($cardSchema.'$defs'.EffectAction.enum)
$persistentActions = @($cardSchema.'$defs'.PersistentEffectAction.enum)
$legalTypes = @(Get-RegexValues $legalActions 'const\s+string\s+\w+\s*=\s*"([A-Z][A-Z0-9_]*)"')
$actionTypes = @($actionSchema.properties.type.enum)
$cardTargets = @($cardSchema.'$defs'.EffectTarget.enum)
$javaTargetText = Get-SwitchSection $javaEffects 'switch (e.target)' $null
$javaTargets = @(Get-RegexValues $javaTargetText 'case\s+"([A-Z][A-Z0-9_]*)"\s*:')
$csharpTargetText = Get-SwitchSection $targetResolver 'switch (target)' $null
$csharpTargets = @(Get-RegexValues $csharpTargetText 'case\s+"([A-Z][A-Z0-9_]*)"\s*:')
$eventTypes = @($eventSchema.properties.type.enum)
$mappedEventKeys = @(Get-RegexValues $adapter '\["([A-Z][A-Z0-9_]*)"\]\s*=')
$mappedEvents = @(Get-RegexValues $adapter '\]\s*=\s*"([A-Z][A-Z0-9_]*)"')
$engineSource = @((Get-ChildItem (Join-Path $repoRoot 'src/Engine') -Recurse -Filter '*.cs' | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n")
$internalEvents = @(Get-RegexValues $engineSource 'Emit\("([A-Z][A-Z0-9_]*)')
$phaseTypes = @($stateMachine.phaseOrder) + 'OVER'
$phaseClause = Get-RegexValues $adapter 'snapshot\.Phase is not \(([^)]*)\)'
$adapterPhases = @(Get-RegexValues ($phaseClause -join ' ') '"([A-Z]+)"')

$actionMissingCSharp = @($cardActions | Where-Object { $_ -notin $effectActions })
$actionMissingJava = @($cardActions | Where-Object { $_ -notin $javaActionCases })
$actionJavaOnly = @($javaActionCases | Where-Object { $_ -notin $effectActions })
$uiActionOnly = @($actionTypes | Where-Object { $_ -notin $legalTypes })
$engineActionOnly = @($legalTypes | Where-Object { $_ -notin $actionTypes })
$targetCSharpOnly = @($csharpTargets | Where-Object { $_ -notin $cardTargets })
$targetJavaOnly = @($javaTargets | Where-Object { $_ -notin $cardTargets })
$eventUnmapped = @($internalEvents | Where-Object { $_ -notin $mappedEventKeys -and $_ -notin @('EFFECT_WINDOW') })
$eventSchemaUnmapped = @($eventTypes | Where-Object { $_ -notin $mappedEvents })
$phaseMissing = @($phaseTypes | Where-Object { $_ -notin $adapterPhases })

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Contract gap report')
$lines.Add('')
$lines.Add(('Generated: {0}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')))
$lines.Add('')
$lines.Add('This report is mechanical inventory only. It does not approve rule, contract, target, or event semantics.')
$lines.Add('')
$lines.Add('## Actions')
$lines.Add('')
$lines.Add('| Check | Result |')
$lines.Add('|---|---|')
$lines.Add(('| Card EffectAction missing in C# | {0} |' -f (Join-OrNone $actionMissingCSharp)))
$lines.Add(('| Card EffectAction missing in Java | {0} |' -f (Join-OrNone $actionMissingJava)))
$lines.Add(('| Java action cases not in C# EffectNames | {0} |' -f (Join-OrNone $actionJavaOnly)))
$lines.Add(('| UI action types absent from C# LegalActionGenerator | {0} |' -f (Join-OrNone $uiActionOnly)))
$lines.Add(('| C# legal action types absent from UI schema | {0} |' -f (Join-OrNone $engineActionOnly)))
$lines.Add(('| Persistent actions (separate schema family) | {0} |' -f (Join-OrNone $persistentActions)))
$lines.Add('')
$lines.Add('## Targets')
$lines.Add('')
$lines.Add('| Check | Result |')
$lines.Add('|---|---|')
$lines.Add(('| Card schema targets | {0} |' -f (Join-OrNone $cardTargets)))
$lines.Add(('| C# resolver-only targets | {0} |' -f (Join-OrNone $targetCSharpOnly)))
$lines.Add(('| Java resolver-only targets | {0} |' -f (Join-OrNone $targetJavaOnly)))
$lines.Add('')
$lines.Add('## Events and phases')
$lines.Add('')
$lines.Add('| Check | Result |')
$lines.Add('|---|---|')
$lines.Add(('| Internal emitted event types not mapped by Adapter | {0} |' -f (Join-OrNone $eventUnmapped)))
$lines.Add(('| UI schema event types absent from Adapter map | {0} |' -f (Join-OrNone $eventSchemaUnmapped)))
$lines.Add(('| State-machine phases absent from Adapter validation | {0} |' -f (Join-OrNone $phaseMissing)))
$lines.Add('')
$lines.Add('## Required decisions')
$lines.Add('')
$lines.Add('- HUMAN_REQUIRED: choose the canonical action set and whether punish/leader actions are transport actions or internal commands.')
$lines.Add('- HUMAN_REQUIRED: approve a target union for minions, leaders, castle and life cores; do not expose aliases by guesswork.')
$lines.Add('- HUMAN_REQUIRED: approve event naming/mapping and root-event policy before filling the unmapped event list.')
$lines.Add('- BLOCKED: Unity package resolution, compilation, EditMode and Windows build remain runtime gates outside this report.')

$report = $lines -join [Environment]::NewLine
if ($OutputPath) {
    $full = [IO.Path]::GetFullPath($OutputPath)
    $parent = Split-Path -Parent $full
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [IO.File]::WriteAllText($full, $report + [Environment]::NewLine, [Text.Encoding]::UTF8)
    Write-Output ("CONTRACT_GAPS path={0} actionGaps={1} targetGaps={2} eventGaps={3}" -f $full, ($actionMissingCSharp.Count + $actionMissingJava.Count + $uiActionOnly.Count + $engineActionOnly.Count), ($targetCSharpOnly.Count + $targetJavaOnly.Count), ($eventUnmapped.Count + $eventSchemaUnmapped.Count))
} else {
    Write-Output $report
}
