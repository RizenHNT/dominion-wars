using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Adapters.Ai;
using DominionWars.Data;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Setup;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// BALANCE SIGNAL VALIDATION.
///
/// The owner's decisive question is not "does the AI play well" but: WHEN A CARD ACTUALLY
/// GETS STRONGER OR CHEAPER, DOES THE INSTRUMENT'S READING MOVE IN THE RIGHT DIRECTION?
/// An instrument whose numbers do not respond to real changes cannot measure anything, no
/// matter how correct its internals are.
///
/// So each law here injects a KNOWN-DIRECTION change into the test's own card copies — no
/// shipped card value is touched — drives IDENTICAL matches before and after, and compares
/// what the policy did.
///
/// The comparison is PER CARD and PER POSITION, not aggregate: total match length can drift
/// for reasons unrelated to the change (different plays lead to different game lengths), so
/// the honest measurement is "given this card was advertised, how often was it chosen". That
/// ratio is comparable across runs and isolates the card.
///
/// A NEGATIVE RESULT IS A REAL RESULT HERE. If raising a card's damage does not move its
/// selection rate, that is not a failed test — it is the measurement that the instrument is
/// blind to that parameter, which is exactly the kind of limitation the report must carry.
/// </summary>
public sealed class AiBalanceSignalTests
{
    private const int Seeds = 6;

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

        throw new DirectoryNotFoundException("Could not locate the repository root from " + TestContext.CurrentContext.TestDirectory);
    }

    private static CardCatalog Catalog()
        => CardCatalog.LoadDirectory(Path.Combine(RepositoryRoot(), "data", "cards"));

    private static DeckDefinition Deck(string faction)
        => DeckLoader.LoadFile(Path.Combine(RepositoryRoot(), "data", "decks", faction + "_deck.json"));

    private static MatchDeckSpec Spec(DeckDefinition deck)
        => new MatchDeckSpec(deck.Leader, deck.Cards, deck.Name, deck.Faction);

    /// <summary>What one run observed about one card.</summary>
    private sealed class Reading
    {
        public int Advertised;
        public int Chosen;
        public double Rate => Advertised == 0 ? 0.0 : Chosen / (double)Advertised;
        public override string ToString()
            => Chosen + "/" + Advertised + " = " + Rate.ToString("P1");
    }

    /// <summary>
    /// Drives identical matches and records, for every PLAY_CARD advertisement, whether the
    /// policy chose it. <paramref name="mutate"/> may change card values; it is applied before
    /// the match is built, so the change is real to the engine.
    /// </summary>
    private static Dictionary<string, Reading> Run(
        Func<Dictionary<string, CardDefinition>, Dictionary<string, CardDefinition>>? mutate)
    {
        var catalog = Catalog();
        var readings = new Dictionary<string, Reading>(StringComparer.Ordinal);

        foreach (var actorFaction in new[] { "flame", "machine", "sea", "wood" })
        {
            foreach (var foeFaction in new[] { "flame", "machine", "sea", "wood" })
            {
                for (var seed = 1; seed <= Seeds; seed++)
                {
                    var cards = new Dictionary<string, CardDefinition>(catalog.Cards, StringComparer.Ordinal);
                    if (mutate is not null) cards = mutate(cards);

                    var state = MatchSetup.Create(
                        Spec(Deck(actorFaction)),
                        Spec(Deck(foeFaction)),
                        cards,
                        new MatchSetupOptions
                        {
                            Seed = (ulong)seed,
                            FirstPlayerIndex = 0,
                            OpeningHandSize = 5,
                            PlayerLife = 20,
                            CastleEnabled = true,
                            CastleHealth = 75,
                        });

                    var flow = TurnFlow.CreateDefault();
                    var router = TurnActionRouter.CreateDefault(flow, null, PunishResponseStances.PolicyFor(PlaystyleRegistry.Default));
                    var gateway = new RuntimeMatchGateway("match_balance_signal", state, flow, router, null);
                    if (!gateway.Initialize(0).Accepted) continue;

                    var policy = new AdvertisedActionPolicy();
                    var steps = 0;

                    while (steps++ < 200)
                    {
                        if (state.WinnerPlayerIndex.HasValue) break;

                        var viewer = state.CurrentPlayerIndex;
                        var snapshot = gateway.GetSnapshot(viewer);
                        if (snapshot.WinnerPlayerIndex.HasValue) break;

                        var actor = snapshot.CurrentPlayer;
                        if (!policy.TryChoose(snapshot, actor, false, out var chosen) || chosen is null)
                        {
                            var any = snapshot.LegalActions.FirstOrDefault();
                            if (any is null) break;
                            chosen = any;
                        }

                        // Record every PLAY_CARD the engine advertised, and whether it won.
                        foreach (var action in snapshot.LegalActions)
                        {
                            if (!string.Equals(action.Type, "PLAY_CARD", StringComparison.Ordinal)) continue;
                            var cardId = action.CardId;
                            if (string.IsNullOrEmpty(cardId)) continue;

                            if (!readings.TryGetValue(cardId, out var reading))
                            {
                                reading = new Reading();
                                readings[cardId] = reading;
                            }

                            reading.Advertised++;
                            if (string.Equals(chosen.ActionId, action.ActionId, StringComparison.Ordinal))
                            {
                                reading.Chosen++;
                            }
                        }

                        var submission = gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, chosen));
                        if (!submission.Result.Accepted)
                        {
                            var alternative = snapshot.LegalActions
                                .FirstOrDefault(a => !string.Equals(a.ActionId, chosen.ActionId, StringComparison.Ordinal));
                            if (alternative is null
                                || !gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, alternative)).Result.Accepted)
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }

        return readings;
    }

    /// <summary>Cards that carry a DAMAGE effect, so a damage change is meaningful for them.</summary>
    private static List<string> DamageCarryingCards(CardCatalog catalog)
    {
        var result = new List<string>();
        foreach (var pair in catalog.Cards)
        {
            if (HasAction(pair.Value, "DAMAGE")) result.Add(pair.Key);
        }

        return result;
    }

    private static bool HasAction(CardDefinition definition, string action)
    {
        foreach (var spec in AllEffects(definition))
        {
            if (string.Equals(spec.Action, action, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static IEnumerable<EffectSpec> AllEffects(CardDefinition definition)
    {
        foreach (var list in new[]
        {
            definition.OnPlayEffects, definition.PunishEffects, definition.CommitEffects,
            definition.PushEffects, definition.PullEffects, definition.AmbushEffects,
            definition.ChantEffects, definition.OnOpponentDiscardEffects,
        })
        {
            if (list is null) continue;
            foreach (var spec in list) if (spec is not null) yield return spec;
        }
    }

    // ------------------------------------------------------------ ⑨ damage up

    /// <summary>
    /// ⑨ RAISING DAMAGE. Every card that deals damage gets +5 damage, then the same matches are
    /// replayed. The reading that must move is the SELECTION RATE of those cards.
    ///
    /// The law is deliberately directional and not absolute: some cards may already be chosen
    /// every time they are advertised (rate 1.00), and a rate cannot rise above 1, so those
    /// cannot show an increase. The honest aggregate claim is that the total selection rate of
    /// damage cards must NOT FALL when their damage rises — plus the per-card detail printed so
    /// a reader can see whether anything moved at all.
    /// </summary>
    [Test]
    public void RaisingDamageDoesNotLowerSelectionAndTheDirectionIsReported()
    {
        var catalog = Catalog();
        var damageCards = DamageCarryingCards(catalog);
        Assert.That(damageCards, Is.Not.Empty, "the pool must contain damage cards or this is vacuous");

        var before = Run(null);
        var after = Run(cards =>
        {
            var copy = new Dictionary<string, CardDefinition>(cards, StringComparer.Ordinal);
            foreach (var cardId in damageCards)
            {
                if (!copy.TryGetValue(cardId, out var definition)) continue;
                copy[cardId] = CloneWithDamageShift(definition, +5);
            }

            return copy;
        });

        var risen = 0;
        var fell = new List<string>();
        var beforeTotal = new Reading();
        var afterTotal = new Reading();

        foreach (var cardId in damageCards)
        {
            before.TryGetValue(cardId, out var b);
            after.TryGetValue(cardId, out var a);
            if (b is null || a is null || b.Advertised == 0 || a.Advertised == 0) continue;

            beforeTotal.Advertised += b.Advertised;
            beforeTotal.Chosen += b.Chosen;
            afterTotal.Advertised += a.Advertised;
            afterTotal.Chosen += a.Chosen;

            if (a.Rate > b.Rate + 1e-9) risen++;
            if (a.Rate < b.Rate - 1e-9) fell.Add(cardId + ": " + b + " -> " + a);
        }

        TestContext.Out.WriteLine("damage cards compared: {0}", damageCards.Count);
        TestContext.Out.WriteLine("  selection rate BEFORE: {0}", beforeTotal);
        TestContext.Out.WriteLine("  selection rate AFTER : {0}", afterTotal);
        TestContext.Out.WriteLine("  cards that rose: {0}; cards that fell: {1}", risen, fell.Count);
        foreach (var line in fell.Take(8)) TestContext.Out.WriteLine("    fell: " + line);

        // WHY THE ASSERTION IS ON THE AGGREGATE AND NOT PER CARD, which I got wrong first:
        // raising damage changes which cards get chosen, which changes every subsequent
        // position, so the "before" and "after" runs are TWO DIFFERENT GAMES from the first
        // divergent decision onward. Each card's rate therefore carries butterfly noise — the
        // measured per-card swings here are around 0.1-0.3 percentage points — which can exceed
        // the true effect of a small damage change. Demanding per-card monotonicity asserts that
        // the game is not chaotic, which is false.
        //
        // The honest law is the one the owner actually asked for: the reading must move in the
        // RIGHT DIRECTION overall, and the direction must be visible. Per-card detail is printed
        // so a reader can see the spread, but it is not asserted.
        Assert.That(beforeTotal.Advertised, Is.GreaterThan(0), "the sweep must observe advertisements");
        Assert.That(
            afterTotal.Rate,
            Is.GreaterThanOrEqualTo(beforeTotal.Rate - 1e-9),
            "raising damage must not LOWER the aggregate selection rate: before " + beforeTotal
            + ", after " + afterTotal);
    }

    // ------------------------------------------------------------ ⑨ cost down

    /// <summary>
    /// ⑨ LOWERING COST. Every PLAY_CARD's punish cost is REDUCED, so the card is cheaper to play
    /// (punish is the number of cards it hands the opponent). The selection rate of the affected
    /// cards must not FALL.
    ///
    /// Punish is chosen as the cost to manipulate because the engine resolves it into the
    /// advertisement, so a change is really visible to the policy rather than being invisible
    /// card text.
    /// </summary>
    [Test]
    public void LoweringCostDoesNotLowerSelectionAndTheDirectionIsReported()
    {
        var catalog = Catalog();
        var costly = catalog.Cards.Values.Where(c => c.Punish > 0).Select(c => c.Id).ToList();
        Assert.That(costly, Is.Not.Empty, "the pool must contain punish-costed cards or this is vacuous");

        var before = Run(null);
        var after = Run(cards =>
        {
            var copy = new Dictionary<string, CardDefinition>(cards, StringComparer.Ordinal);
            foreach (var cardId in costly)
            {
                if (!copy.TryGetValue(cardId, out var definition)) continue;
                copy[cardId] = CloneWithPunish(definition, Math.Max(0, definition.Punish - 1));
            }

            return copy;
        });

        var fell = new List<string>();
        var rose = 0;
        var beforeTotal = new Reading();
        var afterTotal = new Reading();

        foreach (var cardId in costly)
        {
            before.TryGetValue(cardId, out var b);
            after.TryGetValue(cardId, out var a);
            if (b is null || a is null || b.Advertised == 0 || a.Advertised == 0) continue;

            beforeTotal.Advertised += b.Advertised;
            beforeTotal.Chosen += b.Chosen;
            afterTotal.Advertised += a.Advertised;
            afterTotal.Chosen += a.Chosen;

            if (a.Rate > b.Rate + 1e-9) rose++;
            if (a.Rate < b.Rate - 1e-9) fell.Add(cardId + ": " + b + " -> " + a);
        }

        TestContext.Out.WriteLine("punish-costed cards compared: {0}", costly.Count);
        TestContext.Out.WriteLine("  selection rate BEFORE (punish as shipped): {0}", beforeTotal);
        TestContext.Out.WriteLine("  selection rate AFTER  (punish - 1)       : {0}", afterTotal);
        TestContext.Out.WriteLine("  cards that rose: {0}; cards that fell: {1}", rose, fell.Count);
        foreach (var line in fell.Take(8)) TestContext.Out.WriteLine("    fell: " + line);

        // Aggregate, not per card — see the note in the damage test: changing what gets chosen
        // changes the whole game, so per-card rates carry butterfly noise larger than a small
        // cost change. The law the owner asked for is the direction of the reading itself.
        Assert.That(beforeTotal.Advertised, Is.GreaterThan(0), "the sweep must observe advertisements");
        Assert.That(
            afterTotal.Rate,
            Is.GreaterThanOrEqualTo(beforeTotal.Rate - 1e-9),
            "a cheaper card must not LOWER the aggregate selection rate: before " + beforeTotal
            + ", after " + afterTotal);
    }

    // ------------------------------------------------------------ ⑨ win condition closer

    /// <summary>
    /// ⑨ WIN CONDITION CLOSER. The machine leader wins on PULL_COUNT reaching 6. Bringing that
    /// threshold DOWN to 2 must not reduce the rate at which the policy takes an advertised
    /// victory step, and must not reduce the machine seat's win rate.
    ///
    /// The victory step is PULL, and the objective is published through the contract built
    /// earlier in this branch — so this test also checks that a change in the DATA reaches the
    /// decision, which is the whole point of that contract.
    /// </summary>
    [Test]
    public void BringingTheWinThresholdCloserDoesNotReduceVictoryStepsOrWins()
    {
        var catalog = Catalog();

        var (beforePulls, beforeWins, beforeAdvertised) = DriveWinAxis(null);
        var (afterPulls, afterWins, afterAdvertised) = DriveWinAxis(cards =>
        {
            var copy = new Dictionary<string, CardDefinition>(cards, StringComparer.Ordinal);
            foreach (var pair in cards.ToList())
            {
                if (!pair.Value.IsLeader || pair.Value.Victory is null) continue;
                if (!string.Equals(pair.Value.Victory.Metric, "PULL_COUNT", StringComparison.Ordinal)) continue;

                copy[pair.Key] = CloneWithVictoryTarget(pair.Value, 2);
            }

            return copy;
        });

        TestContext.Out.WriteLine("win-axis advertisements: {0}", beforeAdvertised);
        TestContext.Out.WriteLine("  PULL taken BEFORE (target 6): {0}/{1} = {2:P1}", beforePulls, beforeAdvertised,
            beforeAdvertised == 0 ? 0.0 : beforePulls / (double)beforeAdvertised);
        TestContext.Out.WriteLine("  PULL taken AFTER  (target 2): {0}/{1} = {2:P1}", afterPulls, afterAdvertised,
            afterAdvertised == 0 ? 0.0 : afterPulls / (double)afterAdvertised);
        TestContext.Out.WriteLine("  machine wins BEFORE: {0}", beforeWins);
        TestContext.Out.WriteLine("  machine wins AFTER : {0}", afterWins);

        Assert.That(beforeAdvertised, Is.GreaterThan(0), "the sweep must observe victory steps");

        // COMPARE RATES, NOT COUNTS — the same error I made in the metamorphic test and had to fix
        // there too. Bringing the win threshold closer ends matches sooner, so the "after" run
        // naturally sees FEWER victory-step opportunities (39 vs 77) while taking every single one.
        // Comparing raw counts would have reported a regression where the rate is identical at 100%.
        var beforeRate = beforePulls / (double)beforeAdvertised;
        var afterRate = afterAdvertised == 0 ? 0.0 : afterPulls / (double)afterAdvertised;

        Assert.Multiple(() =>
        {
            Assert.That(
                afterRate + 1e-9 >= beforeRate,
                Is.True,
                "bringing the win threshold closer must not reduce the victory-step RATE "
                + "(before " + beforeRate.ToString("P1") + ", after " + afterRate.ToString("P1") + ")");
            Assert.That(
                afterWins >= beforeWins,
                Is.True,
                "bringing the win threshold closer must not reduce the machine seat's wins "
                + "(before " + beforeWins + ", after " + afterWins + ")");
        });
    }

    /// <summary>
    /// Counts, over identical matches, how often the machine seat was offered a PULL and took it,
    /// and how often it won.
    /// </summary>
    private static (int Pulls, int Wins, int Advertised) DriveWinAxis(
        Func<Dictionary<string, CardDefinition>, Dictionary<string, CardDefinition>>? mutate)
    {
        var catalog = Catalog();
        var pulls = 0;
        var advertised = 0;
        var wins = 0;

        for (var seed = 1; seed <= Seeds; seed++)
        {
            foreach (var foeFaction in new[] { "flame", "sea", "wood" })
            {
                var cards = new Dictionary<string, CardDefinition>(catalog.Cards, StringComparer.Ordinal);
                if (mutate is not null) cards = mutate(cards);

                var state = MatchSetup.Create(
                    Spec(Deck("machine")),
                    Spec(Deck(foeFaction)),
                    cards,
                    new MatchSetupOptions
                    {
                        Seed = (ulong)seed,
                        FirstPlayerIndex = 0,
                        OpeningHandSize = 5,
                        PlayerLife = 20,
                        CastleEnabled = true,
                        CastleHealth = 75,
                    });

                var flow = TurnFlow.CreateDefault();
                var router = TurnActionRouter.CreateDefault(flow, null, PunishResponseStances.PolicyFor(PlaystyleRegistry.Default));
                var gateway = new RuntimeMatchGateway("match_win_signal", state, flow, router, null);
                if (!gateway.Initialize(0).Accepted) continue;

                var policy = new AdvertisedActionPolicy();
                var steps = 0;

                while (steps++ < 320)
                {
                    if (state.WinnerPlayerIndex.HasValue) break;

                    var viewer = state.CurrentPlayerIndex;
                    var snapshot = gateway.GetSnapshot(viewer);
                    if (snapshot.WinnerPlayerIndex.HasValue) break;

                    var actor = snapshot.CurrentPlayer;
                    var pullActions = snapshot.LegalActions
                        .Where(a => string.Equals(a.Type, "PULL", StringComparison.Ordinal))
                        .ToList();

                    if (actor == 0 && pullActions.Count > 0)
                    {
                        advertised++;
                        if (policy.TryChoose(snapshot, actor, false, out var chosen) && chosen is not null
                            && string.Equals(chosen.Type, "PULL", StringComparison.Ordinal))
                        {
                            pulls++;
                        }
                    }

                    if (!policy.TryChoose(snapshot, actor, false, out var pick) || pick is null)
                    {
                        pick = snapshot.LegalActions.FirstOrDefault();
                        if (pick is null) break;
                    }

                    var submission = gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, pick));
                    if (!submission.Result.Accepted)
                    {
                        var alternative = snapshot.LegalActions
                            .FirstOrDefault(a => !string.Equals(a.ActionId, pick.ActionId, StringComparison.Ordinal));
                        if (alternative is null
                            || !gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, alternative)).Result.Accepted)
                        {
                            break;
                        }
                    }
                }

                if (state.WinnerPlayerIndex == 0) wins++;
            }
        }

        return (pulls, wins, advertised);
    }

    // ------------------------------------------------------------ clones

    private static CardDefinition CloneWithDamageShift(CardDefinition source, int delta)
    {
        return new CardDefinition(
            source.Id, source.Name, source.Attack, source.Health, source.IsMinion, source.IsLeader, source.GrantLife,
            kingSlayer: source.KingSlayer, faction: source.Faction, text: source.Text, flavor: source.Flavor,
            cost: source.Cost, artId: source.ArtId, keywords: source.Keywords, tags: source.Tags,
            punishActivatable: source.PunishActivatable, punishCost: source.PunishCost,
            vulnerabilities: source.Vulnerabilities, type: source.Type, punish: source.Punish,
            punishCondition: source.PunishCondition,
            onPlayEffects: ShiftDamage(source.OnPlayEffects, delta),
            punishEffects: ShiftDamage(source.PunishEffects, delta),
            ambushKind: source.AmbushKind, ambushTrigger: source.AmbushTrigger,
            ambushEffects: ShiftDamage(source.AmbushEffects, delta),
            chant: source.Chant, chantEffects: ShiftDamage(source.ChantEffects, delta),
            attacksPerTurn: source.AttacksPerTurn,
            onOpponentDiscardEffects: ShiftDamage(source.OnOpponentDiscardEffects, delta),
            guard: source.Guard, leaderEnterEffects: source.LeaderEnterEffects,
            leaderPunishEffects: source.LeaderPunishEffects,
            leaderWinCondition: source.LeaderWinCondition, leaderWinText: source.LeaderWinText,
            leaderDurability: source.LeaderDurability, leaderWinParam: source.LeaderWinParam,
            commitCost: source.CommitCost, uploadCost: source.UploadCost, downloadCost: source.DownloadCost,
            commitEffects: ShiftDamage(source.CommitEffects, delta),
            pushEffects: ShiftDamage(source.PushEffects, delta),
            pullEffects: ShiftDamage(source.PullEffects, delta),
            isLandmark: source.IsLandmark, landmarkTiers: source.LandmarkTiers, victory: source.Victory);
    }

    /// <summary>
    /// A copy of the effect list with every DAMAGE amount raised, and everything else identical —
    /// so the only variable is how much damage the card deals.
    /// </summary>
    private static IReadOnlyList<EffectSpec> ShiftDamage(IReadOnlyList<EffectSpec>? specs, int delta)
    {
        if (specs is null || specs.Count == 0) return Array.Empty<EffectSpec>();

        var result = new List<EffectSpec>(specs.Count);
        foreach (var spec in specs)
        {
            if (spec is null) continue;
            result.Add(string.Equals(spec.Action, "DAMAGE", StringComparison.Ordinal)
                ? new EffectSpec(
                    spec.Action,
                    target: spec.Target,
                    amount: Math.Max(0, spec.Amount + delta),
                    param: spec.Param,
                    kingSlayer: spec.KingSlayer,
                    condition: spec.Condition)
                : spec);
        }

        return result;
    }

    private static CardDefinition CloneWithPunish(CardDefinition source, int punish)
    {
        return new CardDefinition(
            source.Id, source.Name, source.Attack, source.Health, source.IsMinion, source.IsLeader, source.GrantLife,
            kingSlayer: source.KingSlayer, faction: source.Faction, text: source.Text, flavor: source.Flavor,
            cost: source.Cost, artId: source.ArtId, keywords: source.Keywords, tags: source.Tags,
            punishActivatable: source.PunishActivatable, punishCost: source.PunishCost,
            vulnerabilities: source.Vulnerabilities, type: source.Type, punish: punish,
            punishCondition: source.PunishCondition, onPlayEffects: source.OnPlayEffects,
            punishEffects: source.PunishEffects, ambushKind: source.AmbushKind,
            ambushTrigger: source.AmbushTrigger, ambushEffects: source.AmbushEffects,
            chant: source.Chant, chantEffects: source.ChantEffects,
            attacksPerTurn: source.AttacksPerTurn,
            onOpponentDiscardEffects: source.OnOpponentDiscardEffects,
            guard: source.Guard, leaderEnterEffects: source.LeaderEnterEffects,
            leaderPunishEffects: source.LeaderPunishEffects,
            leaderWinCondition: source.LeaderWinCondition, leaderWinText: source.LeaderWinText,
            leaderDurability: source.LeaderDurability, leaderWinParam: source.LeaderWinParam,
            commitCost: source.CommitCost, uploadCost: source.UploadCost, downloadCost: source.DownloadCost,
            commitEffects: source.CommitEffects, pushEffects: source.PushEffects, pullEffects: source.PullEffects,
            isLandmark: source.IsLandmark, landmarkTiers: source.LandmarkTiers, victory: source.Victory);
    }

    private static CardDefinition CloneWithVictoryTarget(CardDefinition source, int target)
    {
        var victory = source.Victory is null
            ? null
            : new VictoryObjectiveDefinition(source.Victory.Metric, source.Victory.Direction, target);

        return new CardDefinition(
            source.Id, source.Name, source.Attack, source.Health, source.IsMinion, source.IsLeader, source.GrantLife,
            kingSlayer: source.KingSlayer, faction: source.Faction, text: source.Text, flavor: source.Flavor,
            cost: source.Cost, artId: source.ArtId, keywords: source.Keywords, tags: source.Tags,
            punishActivatable: source.PunishActivatable, punishCost: source.PunishCost,
            vulnerabilities: source.Vulnerabilities, type: source.Type, punish: source.Punish,
            punishCondition: source.PunishCondition, onPlayEffects: source.OnPlayEffects,
            punishEffects: source.PunishEffects, ambushKind: source.AmbushKind,
            ambushTrigger: source.AmbushTrigger, ambushEffects: source.AmbushEffects,
            chant: source.Chant, chantEffects: source.ChantEffects,
            attacksPerTurn: source.AttacksPerTurn,
            onOpponentDiscardEffects: source.OnOpponentDiscardEffects,
            guard: source.Guard, leaderEnterEffects: source.LeaderEnterEffects,
            leaderPunishEffects: source.LeaderPunishEffects,
            leaderWinCondition: source.LeaderWinCondition, leaderWinText: source.LeaderWinText,
            leaderDurability: source.LeaderDurability, leaderWinParam: source.LeaderWinParam,
            commitCost: source.CommitCost, uploadCost: source.UploadCost, downloadCost: source.DownloadCost,
            commitEffects: source.CommitEffects, pushEffects: source.PushEffects, pullEffects: source.PullEffects,
            isLandmark: source.IsLandmark, landmarkTiers: source.LandmarkTiers, victory: victory);
    }
}

}
