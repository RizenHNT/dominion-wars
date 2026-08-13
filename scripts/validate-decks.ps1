[CmdletBinding()]
param(
    [int]$MinimumCards = 60,
    [int]$MaximumCards = 80
)

$ErrorActionPreference = 'Stop'
if ($MinimumCards -lt 1 -or $MaximumCards -lt $MinimumCards) {
    throw 'Card-count bounds are invalid.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$cardsDirectory = Join-Path $repoRoot 'data\cards'
$decksDirectory = Join-Path $repoRoot 'data\decks'
$neutralFaction = [string]::Concat([char]0x4E2D, [char]0x7ACB)

if (-not (Test-Path -LiteralPath $cardsDirectory -PathType Container)) { throw "Card directory is missing: $cardsDirectory" }
if (-not (Test-Path -LiteralPath $decksDirectory -PathType Container)) { throw "Deck directory is missing: $decksDirectory" }

$cardMap = @{}
foreach ($file in @(Get-ChildItem -LiteralPath $cardsDirectory -Filter '*.json' -File | Sort-Object Name)) {
    $cards = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8))
    Write-Verbose ("Loaded {0}: {1}" -f $file.Name, $cards.Count)
    foreach ($card in $cards) {
        if ($null -eq $card -or [string]::IsNullOrWhiteSpace([string]$card.id)) {
            throw "Card file contains a card without an id: $($file.Name)"
        }
        $id = [string]$card.id
        if ($cardMap.ContainsKey($id)) { throw "Duplicate card id: $id" }
        $cardMap[$id] = $card
    }
}
if ($cardMap.Count -eq 0) { throw 'No cards were loaded.' }
Write-Verbose ("Loaded card ids: {0}; flame={1}" -f $cardMap.Count, $cardMap.ContainsKey('flame_leader'))

$deckFiles = @(Get-ChildItem -LiteralPath $decksDirectory -Filter '*.json' -File | Sort-Object Name)
if ($deckFiles.Count -ne 4) { throw "Expected exactly 4 preconstructed decks, found $($deckFiles.Count)." }

$summary = [System.Collections.Generic.List[object]]::new()
foreach ($file in $deckFiles) {
    $deck = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8))
    if ($null -eq $deck.cards -or $null -eq $deck.cards.PSObject) { throw "Deck has no cards object: $($file.Name)" }
    if ([string]::IsNullOrWhiteSpace([string]$deck.faction)) { throw "Deck has no faction: $($file.Name)" }
    if ([string]::IsNullOrWhiteSpace([string]$deck.leader)) { throw "Deck has no leader: $($file.Name)" }
    $leaderId = [string]$deck.leader
    if (-not $cardMap.ContainsKey($leaderId)) { throw "Deck leader is not in card catalog: $leaderId" }
    $leader = $cardMap[$leaderId]
    if ([string]$leader.faction -ne [string]$deck.faction) { throw "Deck leader faction mismatch: $($file.Name)" }
    if ($leader.leader -ne $true) { throw "Deck leader is not marked leader=true: $leaderId" }

    $total = 0
    foreach ($property in @($deck.cards.PSObject.Properties)) {
        $cardId = [string]$property.Name
        $count = 0
        if (-not [int]::TryParse([string]$property.Value, [ref]$count) -or $count -lt 1) {
            throw "Deck card count must be a positive integer: $($file.Name) / $cardId"
        }
        if (-not $cardMap.ContainsKey($cardId)) { throw "Deck references unknown card: $cardId" }
        if ([string]$cardMap[$cardId].faction -ne [string]$deck.faction -and
            [string]$cardMap[$cardId].faction -ne $neutralFaction) {
            throw "Deck card faction mismatch: $($file.Name) / $cardId"
        }
        $total += $count
    }
    if ($total -lt $MinimumCards -or $total -gt $MaximumCards) {
        throw "Deck card count outside [$MinimumCards,$MaximumCards]: $($file.Name) = $total"
    }
    $summary.Add([pscustomobject]@{ deck = $file.BaseName; faction = [string]$deck.faction; leader = $leaderId; cards = $total })
}

$summary | Format-Table deck,faction,leader,cards -AutoSize
Write-Output ("DECK_VALIDATION pass={0} fail=0 files={1} cards={2}" -f $summary.Count, $deckFiles.Count, $cardMap.Count)
exit 0
