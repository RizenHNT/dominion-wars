using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Adapters.Ai;
using DominionWars.Data;
using DominionWars.Engine.Model;
using DominionWars.Engine.Setup;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// THE BASELINE ("WHITE BOARD") CURVES: WHAT IS ONE POINT OF A STAT WORTH?
///
/// WHAT THIS IS FOR. New card values should be read off a measured curve, not picked because
/// "a 3-cost should be a 3/4". Before designing the three faction series, the instrument has to
/// answer: in THIS game, with THIS opponent, how much win rate does +1 attack, +1 health, or
/// +1 punish actually buy?
///
/// HOW IT IS MEASURED. One card definition is rebuilt with exactly one stat moved and swapped into
/// the card library, so the ONLY difference between variants is that stat. Both seats use the shipped
/// default policy, both seat orders are pooled so the first-player advantage cancels, and every other
/// card is untouched.
///
/// THE REBUILD USES NAMED ARGUMENTS, deliberately. `CardDefinition` has ONE public constructor with
/// 48 optional parameters; the first version of this harness passed them positionally and was wrong
/// in seven places at once. Named arguments make the compiler check every field instead of trusting
/// a remembered order — and a silently swapped `punish`/`punishCost` would have produced a curve that
/// looked plausible and meant nothing.
///
/// WHAT IT DELIBERATELY DOES NOT DO. It asserts no direction and no target win rate. A curve is a
/// measurement; the acceptance decision belongs to the owner. The only assertions are non-vacuity.
///
/// KNOWN LIMITATION, STATED UP FRONT. The shipped default weights `board` 500 and `damage` 120, and
/// NO face attack is ever chosen (measured: 0 of 142 offered, and still 0 with `face` raised to 400).
/// So this curve describes what a stat is worth TO THE SHIPPED OPPONENT, which prefers developing the
/// board over attacking. A stat's value to another policy would differ — a property of the
/// measurement, not a defect in it.
/// </summary>
public sealed class AiStatCurveTests
{
    private const int GamesPerVariantPerSeat = 5;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data", "cards")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from " + TestContext.CurrentContext.TestDirectory);
    }

    private static CardCatalog Catalog()
        => CardCatalog.LoadDirectory(Path.Combine(RepositoryRoot(), "data", "cards"));

    private static DeckDefinition Deck(string faction)
        => DeckLoader.LoadFile(Path.Combine(RepositoryRoot(), "data", "decks", faction + "_deck.json"));

    private static MatchDeckSpec Spec(DeckDefinition deck)
        => new MatchDeckSpec(deck.Leader, deck.Cards, deck.Name, deck.Faction);

    private sealed class Variant
    {
        public string Label = string.Empty;
        public int AttackDelta;
        public int HealthDelta;
        public int PunishDelta;
    }

    private static List<Variant> Variants()
    {
        var variants = new List<Variant> { new Variant { Label = "baseline" } };
        for (var delta = 1; delta <= 3; delta++)
        {
            variants.Add(new Variant { Label = "+" + delta + " attack", AttackDelta = delta });
        }

        for (var delta = 1; delta <= 3; delta++)
        {
            variants.Add(new Variant { Label = "+" + delta + " health", HealthDelta = delta });
        }

        for (var delta = 1; delta <= 2; delta++)
        {
            variants.Add(new Variant { Label = "+" + delta + " punish", PunishDelta = delta });
        }

        return variants;
    }

    /// <summary>
    /// Rebuilds one definition with a single stat moved. Every other field is copied across; named
    /// arguments make a forgotten field a compile error rather than a silent default.
    /// </summary>
    private static CardDefinition Rebuild(CardDefinition source, Variant variant)
    {
        return new CardDefinition(
            id: source.Id,
            name: source.Name,
            attack: Math.Max(0, source.Attack + variant.AttackDelta),
            health: Math.Max(1, source.Health + variant.HealthDelta),
            isMinion: source.IsMinion,
            isLeader: source.IsLeader,
            grantLife: source.GrantLife,
            kingSlayer: source.KingSlayer,
            keywords: source.Keywords?.ToArray(),
            vulnerabilities: source.Vulnerabilities?.ToArray(),
            faction: source.Faction,
            text: source.Text,
            flavor: source.Flavor,
            cost: source.Cost,
            rarity: source.Rarity,
            artId: source.ArtId,
            tags: source.Tags?.ToArray(),
            punishActivatable: source.PunishActivatable,
            punishCost: source.PunishCost,
            hasLeaderAbility: source.HasLeaderAbility,
            type: source.Type,
            punish: Math.Max(0, source.Punish + variant.PunishDelta),
            punishCondition: source.PunishCondition,
            onPlayEffects: source.OnPlayEffects?.ToArray(),
            punishEffects: source.PunishEffects?.ToArray(),
            ambushKind: source.AmbushKind,
            ambushTrigger: source.AmbushTrigger,
            ambushEffects: source.AmbushEffects?.ToArray(),
            chant: source.Chant,
            chantEffects: source.ChantEffects?.ToArray(),
            attacksPerTurn: source.AttacksPerTurn,
            onOpponentDiscardEffects: source.OnOpponentDiscardEffects?.ToArray(),
            guard: source.Guard,
            leaderEnterEffects: source.LeaderEnterEffects?.ToArray(),
            leaderPunishEffects: source.LeaderPunishEffects?.ToArray(),
            leaderWinCondition: source.LeaderWinCondition,
            leaderWinText: source.LeaderWinText,
            leaderDurability: source.LeaderDurability,
            leaderWinParam: source.LeaderWinParam,
            commitCost: source.CommitCost,
            uploadCost: source.UploadCost,
            downloadCost: source.DownloadCost,
            commitEffects: source.CommitEffects?.ToArray(),
            pushEffects: source.PushEffects?.ToArray(),
            pullEffects: source.PullEffects?.ToArray(),
            isLandmark: source.IsLandmark,
            landmarkTiers: source.LandmarkTiers?.ToArray(),
            victory: source.Victory);
    }

    /// <summary>Plays one full match and returns the winning seat, or null if it did not settle.</summary>
    private static int? PlayMatch(
        MatchDeckSpec first,
        MatchDeckSpec second,
        IReadOnlyDictionary<string, CardDefinition> library,
        ulong seed,
        out string haltReason,
        out int stepsTaken)
    {
        haltReason = string.Empty;
        stepsTaken = 0;
        var state = MatchSetup.Create(
            first,
            second,
            library,
            new MatchSetupOptions
            {
                Seed = seed,
                FirstPlayerIndex = 0,
                OpeningHandSize = 5,
                CastleEnabled = true,
                CastleHealth = 75,
            });

        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(
            flow, null, PunishResponseStances.PolicyFor(PlaystyleRegistry.Default));
        var gateway = new RuntimeMatchGateway("match_curve", state, flow, router, null);
        if (!gateway.Initialize(0).Accepted)
        {
            haltReason = "initialize_rejected";
            return null;
        }

        var policy = new AdvertisedActionPolicy();
        var steps = 0;
        while (steps++ < 400)
        {
            stepsTaken = steps;
            if (state.WinnerPlayerIndex.HasValue) return state.WinnerPlayerIndex;

            var viewer = state.CurrentPlayerIndex;
            var snapshot = gateway.GetSnapshot(viewer);
            if (snapshot.WinnerPlayerIndex.HasValue) return snapshot.WinnerPlayerIndex;
            if (snapshot.LegalActions.Count == 0)
            {
                haltReason = "no_legal_actions phase=" + snapshot.Phase;
                return null;
            }

            var chosen = policy.TryChoose(snapshot, snapshot.CurrentPlayer, false, out var pick) && pick is not null
                ? pick
                : snapshot.LegalActions[0];

            var result = gateway.Submit(ToSubmittedGameAction(snapshot, chosen));
            if (!result.Result.Accepted)
            {
                var alternative = snapshot.LegalActions
                    .FirstOrDefault(a => !string.Equals(a.ActionId, chosen.ActionId, StringComparison.Ordinal));
                if (alternative is null
                    || !gateway.Submit(ToSubmittedGameAction(snapshot, alternative)).Result.Accepted)
                {
                    haltReason = "all_rejected phase=" + snapshot.Phase
                        + " chosen=" + chosen.Type + " reason=" + result.Result.ReasonKey;
                    return null;
                }
            }
        }

        haltReason = "step_budget_exhausted turn=" + state.Turn.Number;
        return null;
    }

    private static RuntimeGameAction ToSubmittedGameAction(
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction advertised)
    {
        var action = AdvertisedActionPolicy.ToGameAction(snapshot, advertised);
        if (!RuntimeActionSelection.TryBuildStableSelection(
                advertised,
                out var selectedEntityIds,
                out var reasonKey))
        {
            throw new AssertionException(
                advertised.ActionId + ": malformed selection advertisement: " + reasonKey);
        }

        // Self-discard PLAY_CARD keeps the legacy payload selection produced by
        // AdvertisedActionPolicy. Ordinary DISCARD supplies an explicit typed
        // choice from the advertised candidates; the gateway never auto-picks.
        if (selectedEntityIds.Count > 0 &&
            !action.Payload.ContainsKey("selectedEntityIds"))
        {
            action.SelectedEntityIds = selectedEntityIds;
        }

        return action;
    }

    [Test]
    public void MeasuresTheStatValueCurveForOneCardPerFaction()
    {
        // One plain body per faction: a minion whose numbers can move without changing its role.
        var focal = new (string Faction, string CardId, string Opponent)[]
        {
            ("sea", "sea_kraken", "flame"),
            ("wood", "wood_bear", "flame"),
        };

        var lines = new List<string>();
        var finishedTotals = 0;
        var attemptedTotals = 0;
        var haltTallies = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (faction, cardId, opponent) in focal)
        {
            var catalog = Catalog();
            var focalDeck = Deck(faction);
            var opponentDeck = Deck(opponent);

            if (!catalog.Cards.ContainsKey(cardId))
            {
                TestContext.Out.WriteLine("SKIP {0}: {1} is not in the catalog", faction, cardId);
                continue;
            }

            var copies = focalDeck.Cards.TryGetValue(cardId, out var count) ? count : 0;
            if (copies <= 0)
            {
                TestContext.Out.WriteLine("SKIP {0}: {1} is not in {0}_deck", faction, cardId);
                continue;
            }

            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "[{0}] focal {1} x{2}  vs  {3}_deck  ({4} games per variant, both seats pooled)",
                faction, cardId, copies, opponent, GamesPerVariantPerSeat * 2));

            foreach (var variant in Variants())
            {
                var library = new Dictionary<string, CardDefinition>(catalog.Cards, StringComparer.Ordinal)
                {
                    [cardId] = Rebuild(catalog.Cards[cardId], variant),
                };

                var wins = 0;
                var games = 0;
                var winsAsFirst = 0;
                var gamesAsFirst = 0;
                var winsAsSecond = 0;
                var gamesAsSecond = 0;

                for (var game = 0; game < GamesPerVariantPerSeat; game++)
                {
                    var seed = (ulong)(1000 + game);

                    attemptedTotals++;
                    var winnerA = PlayMatch(Spec(focalDeck), Spec(opponentDeck), library, seed, out var haltA, out _);
                    if (winnerA.HasValue)
                    {
                        finishedTotals++;
                        games++;
                        gamesAsFirst++;
                        if (winnerA.Value == 0) { wins++; winsAsFirst++; }
                    }
                    else if (haltA.Length > 0)
                    {
                        haltTallies.TryGetValue(haltA, out var seenA);
                        haltTallies[haltA] = seenA + 1;
                    }

                    attemptedTotals++;
                    var winnerB = PlayMatch(Spec(opponentDeck), Spec(focalDeck), library, seed, out var haltB, out _);
                    if (winnerB.HasValue)
                    {
                        finishedTotals++;
                        games++;
                        gamesAsSecond++;
                        if (winnerB.Value == 1) { wins++; winsAsSecond++; }
                    }
                    else if (haltB.Length > 0)
                    {
                        haltTallies.TryGetValue(haltB, out var seenB);
                        haltTallies[haltB] = seenB + 1;
                    }
                }

                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0,-14} win {1,3}/{2,-3} = {3,6}%    (P0 {4}/{5}, P1 {6}/{7})",
                    variant.Label,
                    wins,
                    games,
                    games == 0 ? "n/a" : (100.0 * wins / games).ToString("F1", CultureInfo.InvariantCulture),
                    winsAsFirst, gamesAsFirst,
                    winsAsSecond, gamesAsSecond));
            }
        }

        foreach (var line in lines) TestContext.Out.WriteLine(line);
        TestContext.Out.WriteLine(
            "BASELINE CURVE SUMMARY: {0}/{1} attempted games finished",
            finishedTotals, attemptedTotals);

        // WHY THE REST DID NOT FINISH. Printed because win-rate percentages computed over only the
        // games that happen to settle are a statement about WHICH SEEDS SETTLE, not about the stat.
        // If most games do not finish, a win-rate curve is not a usable instrument for card values,
        // and that has to be visible rather than averaged away.
        TestContext.Out.WriteLine("  did-not-finish reasons:");
        foreach (var entry in haltTallies.OrderByDescending(e => e.Value))
        {
            TestContext.Out.WriteLine("    {0,4}x  {1}", entry.Value, entry.Key);
        }

        if (haltTallies.Count > 0) TestContext.Out.WriteLine("  (no reason recorded) count: {0}",
            attemptedTotals - finishedTotals - haltTallies.Values.Sum());

        // NON-VACUITY ONLY.
        Assert.That(attemptedTotals, Is.GreaterThan(0), "the sweep must attempt games");
        Assert.That(
            finishedTotals,
            Is.GreaterThan(attemptedTotals / 2),
            "most attempts must reach a winner, or this measures timeouts (" + finishedTotals + "/" + attemptedTotals + ")");
    }
}
}
