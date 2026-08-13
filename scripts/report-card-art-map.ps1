[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$cardsDirectory = Join-Path $repoRoot 'data\cards'
$artDirectory = Join-Path $repoRoot 'data\art'

$cards = [System.Collections.Generic.List[object]]::new()
foreach ($file in @(Get-ChildItem -LiteralPath $cardsDirectory -Filter '*.json' -File | Sort-Object Name)) {
    $json = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8))
    foreach ($card in @($json)) {
        $cards.Add($card)
    }
}

$leaderFallbacks = @{
    flame_leader = 'data/art/flame_leader.png'
    machine_leader = 'data/art/machine_alpha.png'
    sea_leader = 'data/art/sea_leader.png'
    wood_leader = 'data/art/wood_leader.png'
}

$rows = foreach ($card in $cards) {
    $id = [string]$card.id
    $candidate = if ($leaderFallbacks.ContainsKey($id)) { $leaderFallbacks[$id] } else { '' }
    $absolute = if ($candidate) { Join-Path $repoRoot ($candidate -replace '/', '\') } else { '' }
    $exists = $candidate -and (Test-Path -LiteralPath $absolute -PathType Leaf)
    [pscustomobject]@{
        cardId = $id
        faction = [string]$card.faction
        type = [string]$card.type
        isLeader = ($card.leader -eq $true)
        currentArtId = if ($card.PSObject.Properties.Name -contains 'artId') { [string]$card.artId } else { '' }
        candidatePath = $candidate
        status = if ($exists) { 'FALLBACK_PRESENT' } else { 'MISSING_ART' }
    }
}

$duplicateIds = @($rows | Group-Object cardId | Where-Object Count -gt 1)
if ($rows.Count -ne 91) { throw "Expected 91 cards, found $($rows.Count)." }
if ($duplicateIds.Count -gt 0) { throw 'Card art map contains duplicate card ids.' }
$leaderCount = @($rows | Where-Object isLeader).Count
$requiredLeaders = @('flame_leader', 'machine_leader', 'sea_leader', 'wood_leader')
$missingLeaders = @($requiredLeaders | Where-Object { @($rows | Where-Object cardId -eq $_).Count -ne 1 })
if ($missingLeaders.Count -gt 0) { throw "Required faction leaders are missing: $($missingLeaders -join ', ')" }

$rows | Sort-Object cardId | ConvertTo-Csv -NoTypeInformation
$present = @($rows | Where-Object status -eq 'FALLBACK_PRESENT').Count
$missing = @($rows | Where-Object status -eq 'MISSING_ART').Count
Write-Output ("CARD_ART_MAP cards={0} leaderRecords={1} fallbackPresent={2} missing={3} artIdFields={4}" -f
    $rows.Count, $leaderCount, $present, $missing, (@($rows | Where-Object currentArtId).Count))
