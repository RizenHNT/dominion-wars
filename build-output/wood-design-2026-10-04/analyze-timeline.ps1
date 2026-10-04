# analyze-timeline.ps1 -- turns the probe output into the tables used by GROWTH_TIMELINE.md.
#
# Input : a probe run directory containing games.jsonl / turns.jsonl / events.jsonl /
#         submissions.jsonl (produced by WoodGrowthProbe.cs).
# Output: markdown on stdout.
#
# Run:  powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
#           build-output\wood-design-2026-10-04\analyze-timeline.ps1 `
#           -RunDirectory build-output\wood-design-2026-10-04\runs\timeline-03

param(
    [Parameter(Mandatory = $true)][string]$RunDirectory,
    [int]$DetailSeed = 345864
)
$ErrorActionPreference = 'Stop'
$dir = (Resolve-Path -LiteralPath $RunDirectory).Path

function Read-Jsonl([string]$name) {
    Get-Content -LiteralPath (Join-Path $dir $name) -Encoding UTF8 |
        Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object { $_ | ConvertFrom-Json }
}

$games = @(Read-Jsonl 'games.jsonl')
$turns = @(Read-Jsonl 'turns.jsonl')
$events = @(Read-Jsonl 'events.jsonl')
$subs = @(Read-Jsonl 'submissions.jsonl')

function Get-FullGameKey($row) {
    return ('{0}|{1}|{2}|{3}|{4}' -f $row.seed, $row.first, $row.player0, $row.player1, $row.wood_seat)
}

function Get-EventGameKey($row) {
    return ('{0}|{1}|{2}' -f $row.seed, $row.player0, $row.player1)
}

$gamesByFullKey = @{}
$gamesByEventKey = @{}
foreach ($g in $games) {
    $fullKey = Get-FullGameKey $g
    $eventKey = Get-EventGameKey $g
    if ($gamesByFullKey.ContainsKey($fullKey)) { throw "duplicate full game key: $fullKey" }
    if ($gamesByEventKey.ContainsKey($eventKey)) { throw "event key is ambiguous without first: $eventKey" }
    $gamesByFullKey[$fullKey] = $g
    $gamesByEventKey[$eventKey] = $g
}

Write-Output "## A. Per-match measurement ($($games.Count) matches, run dir $([IO.Path]::GetFileName($dir)))"
Write-Output ""
Write-Output "| wood seat | opponent | seed | first | global turns | wood turns | winner | reason | max sealed hp (observed) | first turn >=512 | final root | final rampant |"
Write-Output "|---:|---|---:|---:|---:|---:|---:|---|---|---:|---:|---:|"
foreach ($g in $games | Sort-Object seed) {
    $gameKey = Get-FullGameKey $g
    $t = @($turns | Where-Object { (Get-FullGameKey $_) -eq $gameKey })
    $woodParity = 1
    if ($g.first -ne $g.wood_seat) { $woodParity = 0 }
    $woodTurns = @($t | Where-Object { ($_.turn % 2) -eq $woodParity })
    Write-Output ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} |" -f `
        $g.wood_seat, $g.player1, $g.seed, $g.first, $g.turns, $woodTurns.Count, $g.winner, $g.reason,
        $g.max_sealed_health_observed, $g.first_turn_at_or_above_512, $g.final_root, $g.final_rampant)
}

Write-Output ""
Write-Output "## B. Aggregate"
Write-Output ""
$reached = @($games | Where-Object { $_.reached_512_observed })
$giant = @($games | Where-Object { $_.giant_health_win })
$everSealed = @($games | Where-Object { $_.max_sealed_health_observed -gt 0 })
Write-Output ("- matches: {0}; every game finished (undecided: {1}); capped: {2}; exceptions: {3}" -f `
    $games.Count, @($games | Where-Object { -not $_.decided }).Count, @($games | Where-Object { $_.capped }).Count, @($games | Where-Object { $_.exception }).Count)
Write-Output ("- games where the wood seat ever had a sealed minion: {0}/{1}" -f $everSealed.Count, $games.Count)
Write-Output ("- games where the largest sealed minion reached >= 512: {0}/{1}" -f $reached.Count, $games.Count)
Write-Output ("- games won by win.giant_health_ge: {0}/{1}" -f $giant.Count, $games.Count)
$validityRows = @($games | Where-Object { $null -ne $_.PSObject.Properties['valid'] })
if ($validityRows.Count -gt 0) {
    $validGames = @($validityRows | Where-Object { $_.valid -eq $true })
    $invalidGames = @($validityRows | Where-Object { $_.valid -ne $true })
    $validWoodWins = @($validGames | Where-Object { $_.wood_won -eq $true })
    $validTurnStats = $validGames | Measure-Object -Property turns -Average
    $validWinRate = 0.0
    if ($validGames.Count -gt 0) { $validWinRate = $validWoodWins.Count / [double]$validGames.Count }
    Write-Output ("- validity-aware driver results: attempted {0}; valid {1}; invalid {2}" -f `
        $validityRows.Count, $validGames.Count, $invalidGames.Count)
    Write-Output ("- valid wood wins: {0}/{1} (rate {2:P1}); average turns among valid matches: {3:N2}" -f `
        $validWoodWins.Count, $validGames.Count, $validWinRate, $validTurnStats.Average)
    Write-Output "- valid winner-reason counts:"
    $validGames | Group-Object { if ([string]::IsNullOrWhiteSpace([string]$_.reason)) { '<null>' } else { [string]$_.reason } } |
        Sort-Object Count -Descending | ForEach-Object { Write-Output ("  - {0}: {1}" -f $_.Name, $_.Count) }
    Write-Output "- invalid reason counts:"
    $invalidGames | Group-Object { if ([string]::IsNullOrWhiteSpace([string]$_.invalid_reason)) { '<missing>' } else { [string]$_.invalid_reason } } |
        Sort-Object Count -Descending | ForEach-Object { Write-Output ("  - {0}: {1}" -f $_.Name, $_.Count) }
}
$sorted = $games | Sort-Object max_sealed_health_observed
Write-Output ("- max sealed health observed per game, sorted: {0}" -f (($sorted | ForEach-Object { [string]$_.max_sealed_health_observed }) -join ', '))
Write-Output ("- global turn count: min {0}, median {1}, max {2}" -f `
    ($sorted | Measure-Object turns -Minimum).Minimum,
    ($sorted[[int][Math]::Floor($sorted.Count / 2)]).turns,
    ($sorted | Measure-Object turns -Maximum).Maximum)
Write-Output ""
Write-Output "Winner reasons:"
$games | Group-Object reason | Sort-Object Count -Descending | ForEach-Object { Write-Output ("- {0}: {1}" -f $_.Name, $_.Count) }

Write-Output ""
Write-Output "## C. Verified amplification arithmetic (engine events)"
Write-Output ""
$growth = 0; $growthOk = 0; $flat = 0; $flatOk = 0; $mismatch = @()
$rootAdds = 0; $rampantAdds = 0; $rampantCapped = 0
foreach ($group in ($events | Group-Object seed)) {
    $root = 0; $rampant = 0
    $g = $games | Where-Object { $_.seed -eq [int]$group.Name } | Select-Object -First 1
    $woodSeat = $g.wood_seat
    foreach ($e in ($group.Group | Sort-Object event_id)) {
        switch ($e.type) {
            'ROOT_STACKS_ADDED' { if ($e.d_player -eq $woodSeat) { $root = [int]$e.d_total; $rootAdds++ } }
            'RAMPANT_STACKS_ADDED' {
                if ($e.d_player -eq $woodSeat) {
                    $rampant = [int]$e.d_total; $rampantAdds++
                    if ($e.d_capped -eq $true) { $rampantCapped++ }
                }
            }
            'BUFF_APPLIED' {
                $base = [int]$e.d_baseAmount
                $amount = [int]$e.d_amount
                if ($amount -ne $base) {
                    $growth++
                    $expected = ($base + $root) * [int][Math]::Pow(2, [Math]::Min(3, $rampant))
                    $sealOk = ($e.d_sealed -eq $true)
                    if ($expected -eq $amount -and $sealOk) { $growthOk++ }
                    else { $mismatch += ("seed={0} ev={1} base={2} root={3} rampant={4} expected={5} actual={6} sealed={7}" -f $e.seed, $e.event_id, $base, $root, $rampant, $expected, $amount, $e.d_sealed) }
                }
                else {
                    $flat++
                    if ($e.d_sealed -eq $false) { $flatOk++ } else { $mismatch += ("seed={0} ev={1} flat-but-sealed amount={2}" -f $e.seed, $e.event_id, $amount) }
                }
            }
        }
    }
}
Write-Output ("- ROOT_STACKS_ADDED events (wood seat): {0}" -f $rootAdds)
Write-Output ("- RAMPANT_STACKS_ADDED events (wood seat): {0} (of which capped by the 3-layer rule: {1})" -f $rampantAdds, $rampantCapped)
Write-Output ("- BUFF_APPLIED events with amount != baseAmount (i.e. amplified): {0}; matching (base+root)*2^min(3,rampant) AND sealed=true: {1}" -f $growth, $growthOk)
Write-Output ("- BUFF_APPLIED events with amount == baseAmount (not amplified): {0}; of which sealed=false: {1}" -f $flat, $flatOk)
if ($mismatch.Count -gt 0) {
    Write-Output ""
    Write-Output "MISMATCHES:"
    $mismatch | ForEach-Object { Write-Output ("- " + $_) }
}

Write-Output ""
Write-Output "## D. Per-turn detail (seed $DetailSeed)"
Write-Output ""
$detail = @($turns | Where-Object { $_.seed -eq $DetailSeed }) | Sort-Object turn
$g0 = $games | Where-Object { $_.seed -eq $DetailSeed } | Select-Object -First 1
Write-Output ("match: {0} (wood seat {1}) vs {2}; turns {3}; reason {4}; final root {5}, rampant {6}" -f `
    $g0.player0, $g0.wood_seat, $g0.player1, $g0.turns, $g0.reason, $g0.final_root, $g0.final_rampant)
Write-Output ""
Write-Output "| turn | actor | root | rampant | sealed count | max sealed hp | end-of-action sealed hp | wood hand | wood deck | opp deck | wood plays | BUFF evidence that turn |"
Write-Output "|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|"
foreach ($row in $detail) {
    $actor = 'opp'
    $detailParity = 1
    if ($g0.first -ne $row.wood_seat) { $detailParity = 0 }
    if (($row.turn % 2) -eq $detailParity) { $actor = 'wood' }
    $buffEvidence = ($row.wood_buff_events | ForEach-Object { $_ -replace 'base=(\d+) amount=(\d+).*', 'amount=$2' }) -join '; '
    $plays = ($row.wood_plays | ForEach-Object { ($_ -split ' -> ')[0] -replace '^PLAY ', '' -replace '^ATTACK ', 'atk:' }) -join '; '
    Write-Output ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} |" -f `
        $row.turn, $actor, $row.root, $row.rampant, $row.sealed_count, $row.max_sealed_health,
        $row.end_action_sealed_health, $row.wood_hand, $row.wood_deck, $row.opp_deck, $plays, $buffEvidence)
}

Write-Output ""
Write-Output "## E. Where the wood seat's turn went (per-play file, wood actor)"
Write-Output ""
# This is a coarse event proxy, not exact per-card effect attribution. Normal play
# plumbing and all EFFECT_SKIPPED diagnostics do not prove a gameplay effect was applied.
$plumbing = @('CARD_PLAYED','PUNISH_DRAW','MINION_SUMMONED','PHASE_CHANGED','TURN_CHANGED','TURN_STARTED','CARDS_DRAWN',
    'EFFECT_SKIPPED','EFFECT_SKIPPED:effect.no_effects')
$plays = @(Read-Jsonl 'plays.jsonl')
foreach ($r in $plays) {
    $evs = @($r.events -split ',' | Where-Object { $_ })
    $extra = @($evs | Where-Object { $plumbing -notcontains $_ })
    $r | Add-Member -NotePropertyName noEffect -NotePropertyValue ($extra.Count -eq 0) -Force
}
$noEffect = @($plays | Where-Object noEffect)
$wasted = @($noEffect | Where-Object { $_.chant -eq 0 })
$resolving = @($plays | Where-Object { -not $_.noEffect })
$growthEvents = @($plays | Where-Object { $_.events -match 'ROOT_STACKS_ADDED|RAMPANT_STACKS_ADDED|BUFF_APPLIED' })
$emptyEffectAudit = @($plays | Where-Object { $_.events -like '*EFFECT_SKIPPED:effect.no_effects*' })
Write-Output ("- accepted PLAY_CARD submissions by the wood seat: {0}" -f $plays.Count)
Write-Output ("- plays with no non-diagnostic event beyond play plumbing (coarse proxy; not exact card-effect attribution): {0}" -f $noEffect.Count)
Write-Output ("  - explicit empty-effect-list audit events (not counted as effects): {0}" -f $emptyEffectAudit.Count)
Write-Output ("  - of those, chant cards (wood_seed; its effects are chanted 2 turns later, so this is expected): {0}" -f @($noEffect | Where-Object { $_.chant -gt 0 }).Count)
Write-Output ("  - of those, non-chant plays with no non-diagnostic event (proxy): {0}" -f $wasted.Count)
Write-Output ("  - of those, plays that still charged the opponent the full punish draw: {0}" -f @($noEffect | Where-Object { $_.events -like '*PUNISH_DRAW*' }).Count)
Write-Output ("- plays that produced at least one growth event (ROOT/RAMPANT/BUFF): {0}" -f $growthEvents.Count)
Write-Output ""
Write-Output "Cross-tab: instance PunishActivated x declared effect lists"
Write-Output ""
Write-Output "| instance punishActivated | onPlayEffects | punishEffects | plays | no-non-diagnostic-event plays |"
Write-Output "|:--|---:|---:|---:|---:|"
$plays | Group-Object { "$($_.punish_activated_instance)|$($_.on_play_effects)|$($_.punish_effects)" } | Sort-Object Name | ForEach-Object {
    $parts = $_.Name -split '\|'
    Write-Output ("| {0} | {1} | {2} | {3} | {4} |" -f $parts[0], $parts[1], $parts[2], $_.Count, @($_.Group | Where-Object noEffect).Count)
}
Write-Output ""
Write-Output ""
Write-Output "## F. Growth-axis timing"
Write-Output ""
$woodTurnsTotal = 0
$turnsByGame = @{}
foreach ($t in $turns) {
    $gameKey = Get-FullGameKey $t
    if (-not $gamesByFullKey.ContainsKey($gameKey)) { throw "turn row has no matching game: $gameKey" }
    if (-not $turnsByGame.ContainsKey($gameKey)) { $turnsByGame[$gameKey] = [System.Collections.Generic.List[object]]::new() }
    $turnsByGame[$gameKey].Add($t)
}
foreach ($g in $games) {
    $gameKey = Get-FullGameKey $g
    $t = @($turnsByGame[$gameKey])
    $parity = 1
    if ($g.first -ne $g.wood_seat) { $parity = 0 }
    $woodTurnsTotal += @($t | Where-Object { ($_.turn % 2) -eq $parity }).Count
}
$amplifiedGrowthBuffs = @($events | Where-Object {
    $_.type -eq 'BUFF_APPLIED' -and $_.d_growthApplied -eq $true -and
    [int]$_.d_amount -ne [int]$_.d_baseAmount
})
$woodTargetBuffs = 0
$opponentTargetBuffs = 0
$neutralOrUnownedTargetBuffs = 0
foreach ($e in $amplifiedGrowthBuffs) {
    $eventKey = Get-EventGameKey $e
    if (-not $gamesByEventKey.ContainsKey($eventKey)) { throw "BUFF event has no unique matching game: $eventKey" }
    $g = $gamesByEventKey[$eventKey]
    if ($null -eq $e.target_owner -or [int]$e.target_owner -lt 0) {
        $neutralOrUnownedTargetBuffs++
    }
    elseif ([int]$e.target_owner -eq [int]$g.wood_seat) {
        $woodTargetBuffs++
    }
    else {
        $opponentTargetBuffs++
    }
}
Write-Output ("- total wood-seat turns across all matches: {0}" -f $woodTurnsTotal)
Write-Output ("- growth-amplified BUFF_APPLIED events (d_growthApplied=true and amount != baseAmount): {0}; per wood turn: {1:N3}" -f $amplifiedGrowthBuffs.Count, ($amplifiedGrowthBuffs.Count / [double]$woodTurnsTotal))
Write-Output ("  - target owned by Wood: {0}; opponent-owned: {1}; neutral/unowned: {2}" -f $woodTargetBuffs, $opponentTargetBuffs, $neutralOrUnownedTargetBuffs)
Write-Output "  - source attribution is not present in this event export; counts cover growth-applied BUFFs across the match, split by target owner."
$manifest = @{}
foreach ($e in ($events | Where-Object { $_.type -eq 'LEADER_MANIFESTED' })) {
    $eventKey = Get-EventGameKey $e
    if (-not $gamesByEventKey.ContainsKey($eventKey)) { throw "manifest event has no unique matching game: $eventKey" }
    $g = $gamesByEventKey[$eventKey]
    if ([int]$e.d_player -ne [int]$g.wood_seat) { continue }
    $gameKey = Get-FullGameKey $g
    if (-not $manifest.ContainsKey($gameKey)) { $manifest[$gameKey] = [int]$e.turn }
}
foreach ($g in ($games | Sort-Object seed, player1, first)) {
    $gameKey = Get-FullGameKey $g
    $at = 'never'
    if ($manifest.ContainsKey($gameKey)) { $at = $manifest[$gameKey] }
    $rows = @($turnsByGame[$gameKey] | Sort-Object turn)
    $firstSealed = 0
    foreach ($r in $rows) { if ($r.sealed_count -gt 0) { $firstSealed = $r.turn; break } }
    Write-Output ("- seed {0} vs {1} (first {2}): wood leader manifested on turn {3}; first sealed minion observed on turn {4}; max sealed hp {5}; final root {6}/rampant {7}" -f `
        $g.seed, $g.player1, $g.first, $at, $firstSealed, $g.max_sealed_health_observed, $g.final_root, $g.final_rampant)
}
