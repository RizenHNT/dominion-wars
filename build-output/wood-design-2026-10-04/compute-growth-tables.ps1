# compute-growth-tables.ps1 -- pure-arithmetic wood growth table (NO simulation).
#
# Reproduces the numbers in GROWTH_TIMELINE.md section 1. The model is the engine's own
# formula, read from src/Engine/Effects/EffectRuntime.Combat.cs:
#
#   ApplyGrowth(base, root, rampant):
#     if (base <= 0 || (root == 0 && rampant == 0)) return base      // line 219-222
#     additive   = base + root                                       // line 224
#     multiplier = 1 << min(3, max(0, rampant))                      // line 225
#     result     = additive * multiplier  (saturates int.MaxValue)   // line 226-232
#
# and the seal rule (line 175 / 192-197): `growthApplied = effectiveAmount != spec.Amount`
# and only a growthApplied BUFF sets Sealed. ADD_ROOT has NO cap (saturating add),
# ADD_RAMPANT is capped at 3 (src/Engine/Effects/EffectRuntime.State.cs:52).
#
# A "card" in this model is one wood card whose effect list is [ADD_*, BUFF 2 ...], so the
# BUFF resolves AFTER its own card's ADD_* (true for every growth card in data/cards/wood.json,
# e.g. wood_growth = [ADD_RAMPANT 1, BUFF 2 param=rampant]).
#
# Run:  powershell.exe -NoProfile -File build-output\wood-design-2026-10-04\compute-growth-tables.ps1

$ErrorActionPreference = 'Stop'
$target = 512

function Invoke-Sequence {
    param(
        [string]$Name,
        [string[][]]$Turns,     # each element = list of 'root' | 'rampant' cards played that turn
        [int]$BaseHealth,
        [int]$StartRoot = 0,
        [int]$StartRampant = 0
    )
    $root = $StartRoot
    $rampant = $StartRampant
    $health = $BaseHealth
    $rows = @()
    $cross = $null
    for ($t = 0; $t -lt $Turns.Count; $t++) {
        $perTurn = 0
        foreach ($kind in $Turns[$t]) {
            if ($kind -eq 'root') { $root += 2 } else { $rampant = [Math]::Min(3, $rampant + 1) }
            $mult = [int][Math]::Pow(2, [Math]::Min(3, $rampant))
            $amount = (2 + $root) * $mult
            $perTurn += $amount
        }
        $health += $perTurn
        if ($null -eq $cross -and $health -ge $target) { $cross = $t + 1 }
        $rows += [pscustomobject]@{
            Turn = $t + 1
            Cards = ($Turns[$t] -join '+')
            Root = $root
            Rampant = $rampant
            AmplifiedThisTurn = $perTurn
            SealedHealth = $health
            Reached512 = ($health -ge $target)
        }
        if ($null -ne $cross -and ($t + 1) -ge $cross) { break }
    }
    [pscustomobject]@{ Name = $Name; Base = $BaseHealth; CrossoverTurn = $cross; Rows = $rows }
}

function New-Sequence {
    param([string[]]$PerTurnKinds, [int]$Turns)
    $list = @()
    for ($i = 0; $i -lt $Turns; $i++) { $list += , $PerTurnKinds }
    return $list
}

$results = @()

# A: pure 扎根 -- one "ADD_ROOT 2 + BUFF 2 root" card per turn (wood_guard/druid/bear/treant/warden/stag/owl)
$results += Invoke-Sequence -Name 'A  one root card per turn' -Turns (New-Sequence @('root') 40) -BaseHealth 0
$results += Invoke-Sequence -Name 'A8 one root card per turn (base 8 = wood_treant)' -Turns (New-Sequence @('root') 40) -BaseHealth 8

# B: optimal single card per turn -- three wood_growth first, then root cards
$b = @(); $b += , @('rampant'); $b += , @('rampant'); $b += , @('rampant')
for ($i = 0; $i -lt 40; $i++) { $b += , @('root') }
$results += Invoke-Sequence -Name 'B  optimal 1 card/turn (rampant x3 then root)' -Turns $b -BaseHealth 0
$results += Invoke-Sequence -Name 'B8 optimal 1 card/turn (rampant x3 then root, base 8)' -Turns $b -BaseHealth 8
$results += Invoke-Sequence -Name 'B2 optimal 1 card/turn (rampant x3 then root, base 2 = wood_sapling)' -Turns $b -BaseHealth 2

# B1: leader has already manifested (enter effect ADD_RAMPANT 1), so rampant starts at 1
$b1 = @(); $b1 += , @('rampant'); $b1 += , @('rampant')
for ($i = 0; $i -lt 40; $i++) { $b1 += , @('root') }
$results += Invoke-Sequence -Name 'B1 rampant starts at 1 (leader enter), then root cards' -Turns $b1 -BaseHealth 2 -StartRampant 1

# C: two root cards per turn (hand allows it; no per-turn play limit in the rules)
$results += Invoke-Sequence -Name 'C  two root cards per turn' -Turns (New-Sequence @('root','root') 40) -BaseHealth 0

# D: two cards per turn in the optimal order (rampant first)
$d = @()
for ($i = 0; $i -lt 40; $i++) { $d += , @('rampant','root') }
$results += Invoke-Sequence -Name 'D  two cards/turn (rampant+root), optimal order' -Turns $d -BaseHealth 0

# E: two wood_growth per turn (rampant only, saturation check)
$results += Invoke-Sequence -Name 'E  two rampant cards per turn' -Turns (New-Sequence @('rampant','rampant') 40) -BaseHealth 0

foreach ($r in $results) {
    Write-Output ""
    Write-Output ("### " + $r.Name + "  (base health " + $r.Base + ", crossover turn " + $r.CrossoverTurn + ")")
    Write-Output ""
    Write-Output "| turn | cards this turn | root | rampant | amplified this turn | sealed health | >=512 |"
    Write-Output "|---:|---|---:|---:|---:|---:|:--:|"
    foreach ($row in $r.Rows) {
        Write-Output ("| " + $row.Turn + " | " + $row.Cards + " | " + $row.Root + " | " + $row.Rampant + " | " +
                      $row.AmplifiedThisTurn + " | " + $row.SealedHealth + " | " + ($(if ($row.Reached512) { 'yes' } else { '' })) + " |")
    }
}
