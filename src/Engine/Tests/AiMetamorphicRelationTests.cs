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
/// METAMORPHIC RELATIONS for the threat estimator and the state evaluator.
///
/// A metamorphic test does not ask "is this number right". It asks a weaker but far more
/// checkable question: WHEN ONE INPUT MOVES IN A KNOWN DIRECTION, DOES THE READING MOVE THE
/// WAY IT MUST? These five relations are the ones a balance instrument cannot survive losing:
/// a hidden pool that grows must not read as less threat, a pool that shrinks must not read as
/// more, and moving a win objective closer must not move the distance further away.
///
/// EVERY position here is REAL: it is produced by playing an actual match through
/// <see cref="RuntimeMatchGateway"/> until the engine offers a genuine ACTION phase, and only
/// then is ONE estimator input changed. Nothing is a hand-built fake snapshot, so a relation
/// that holds here holds on positions the game actually reaches.
///
/// NON-VACUITY IS ASSERTED, NOT ASSUMED. "Never decreases" is trivially true of a quantity
/// that never changes, so each relation also has to show that the quantity MOVES: at least one
/// strict increase (R1), at least one strict decrease (R2), a non-zero spread (R4), a non-zero
/// movement count (R5). The spread is printed, not just asserted, so a reviewer can read the
/// magnitude from the run log.
///
/// R3 IS A NEGATIVE FINDING AND IS REPORTED AS ONE. See the test itself: the estimator does not
/// read a card's cost anywhere, so the relation is NOT MEASURABLE and no proxy was invented to
/// stand in for it.
///
/// RATES AND STABLE IDENTITIES, NOT RAW COUNTS. Every comparison here holds the position fixed
/// and compares the same quantity on the SAME position before and after one input change, so no
/// number is ever compared across two different games.
/// </summary>
[TestFixture]
public sealed class AiMetamorphicRelationTests
{
    /// <summary>Faction pairs used to produce real positions. Kept small so a run stays fast.</summary>
    private static readonly string[][] Pairs =
    {
        new[] { "flame", "wood" },
        new[] { "machine", "sea" },
        new[] { "sea", "machine" },
        new[] { "wood", "flame" },
    };

    private const int Seeds = 3;

    // ---------------------------------------------------------------- conventions

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

    /// <summary>
    /// ONE REAL POSITION, taken at a genuine decision point: the engine is in an ACTION phase
    /// with legal actions advertised to the seat that is to move, and the match is not over.
    ///
    /// The driver follows the convention used by AiLethalAndDefenceTests / AiTwoPlyConsistencyTests
    /// (drive with the normal policy, stop when the engine offers ACTION) so the position is
    /// reached by play rather than by rigging.
    /// </summary>
    private sealed class Position
    {
        public string Label { get; set; } = string.Empty;
        public int Turn { get; set; }
        public int Actor { get; set; }
        public string ViewerFaction { get; set; } = string.Empty;
        public string OpponentFaction { get; set; } = string.Empty;
        public RuntimeSnapshotEnvelope Snapshot { get; set; } = null!;
        public Dictionary<string, int> OpponentPool { get; set; } = null!;
        /// <summary>The snapshot and pool this position was derived from; null for an original.</summary>
        public RuntimeSnapshotEnvelope? SourceSnapshot { get; set; }
        public Dictionary<string, int>? SourcePool { get; set; }
        public Dictionary<string, IReadOnlyList<ThreatKind>> Categories { get; set; } = null!;
        public Dictionary<string, IReadOnlyList<string>> Tags { get; set; } = null!;
        public CardCatalog Catalog { get; set; } = null!;
    }

    /// <summary>
    /// How many per-turn samples one faction pairing contributes to R1-R4: the first, the middle
    /// and the last of the match's decision points. R5 uses every sampled turn instead, because
    /// the interesting thing there (a leader that has become visible, with real progress on its
    /// axis) does not exist at the start of a match.
    /// </summary>
    private const int PositionsPerPair = 3;

    private static List<Position> RealPositions(CardCatalog catalog, Dictionary<string, IReadOnlyList<ThreatKind>> categories,
        Dictionary<string, IReadOnlyList<string>> tags)
    {
        var positions = new List<Position>();
        foreach (var pair in Pairs)
        {
            for (var seed = 1; seed <= Seeds; seed++)
            {
                var state = MatchSetup.Create(
                    Spec(Deck(pair[0])),
                    Spec(Deck(pair[1])),
                    catalog.Cards,
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
                var gateway = new RuntimeMatchGateway("match_metamorphic", state, flow, router, null);
                if (!gateway.Initialize(0).Accepted) continue;

                var policy = new AdvertisedActionPolicy();
                var steps = 0;
                var samples = new List<Position>();
                var lastSampleTurn = -1;

                // THE WHOLE MATCH IS PLAYED, and positions are sampled FROM PLAY rather than only at
                // the first decision point. That matters for R5: a leader card starts in its owner's
                // deck and only manifests into the visible leader zone when it is DRAWN
                // (EffectRuntime.Cards.cs, ManifestLeader), so the first ACTION phase of a match has
                // an empty leader zone and no side's win condition is knowable at all. Sampling only
                // there would have made R5 unmeasurable for a reason belonging to the sampling rather
                // than to the estimator. One position is taken per turn, so a turn offering several
                // ACTION windows does not weight the sample.
                while (steps++ < 400)
                {
                    if (state.WinnerPlayerIndex.HasValue) break;

                    var viewer = state.CurrentPlayerIndex;
                    var snapshot = gateway.GetSnapshot(viewer);
                    if (snapshot.WinnerPlayerIndex.HasValue) break;

                    if (string.Equals(snapshot.Phase, "ACTION", StringComparison.Ordinal)
                        && snapshot.LegalActions.Count > 0
                        && snapshot.Turn != lastSampleTurn)
                    {
                        lastSampleTurn = snapshot.Turn;
                        // THE OPPONENT'S DECK DEPENDS ON WHOSE TURN IT IS, and getting this wrong
                        // silently corrupts every relation. pair[0] sits in seat 0 and pair[1] in
                        // seat 1, but the sampled viewer is whichever seat the engine is asking to
                        // act (`CurrentPlayerIndex`), so the opponent can be either one. An earlier
                        // revision always declared pair[1]'s list as the opponent's pool; on the
                        // turns where the viewer was seat 1, that handed the estimator seat 1's OWN
                        // deck list while the snapshot described seat 0's board, and the estimator
                        // correctly reported an accounting mismatch. The seat-to-faction mapping is
                        // therefore derived from the viewer, not assumed.
                        var viewerFaction = viewer == 0 ? pair[0] : pair[1];
                        var opponentFaction = viewer == 0 ? pair[1] : pair[0];
                        samples.Add(new Position
                        {
                            Label = pair[0] + " vs " + pair[1] + " seed " + seed + " turn " + snapshot.Turn
                                + " viewerSeat" + viewer,
                            Turn = snapshot.Turn,
                            Actor = viewer,
                            // The PROJECTION, not the gateway snapshot: it is the same viewer-safe
                            // contract the estimator is fed in production, and it is the object the
                            // metamorphic edits below are applied to.
                            Snapshot = Adapters.RuntimeSnapshotProjection.ToSnapshot(
                                state, "match_metamorphic", state.Turn.Number, viewer, flow),
                            OpponentPool = new Dictionary<string, int>(Deck(opponentFaction).Cards, StringComparer.Ordinal),
                            ViewerFaction = viewerFaction,
                            OpponentFaction = opponentFaction,
                            Categories = categories,
                            Tags = tags,
                            Catalog = catalog,
                        });
                    }

                    var pick = snapshot.LegalActions.FirstOrDefault();
                    if (pick is null) break;
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

                Assert.That(samples, Is.Not.Empty,
                    "every driven match must expose at least one ACTION decision: " + pair[0] + " vs " + pair[1]
                    + " seed " + seed);

                // R5's set: every sampled turn on which a leader was visible, so the win condition is
                // knowable. Turns before the leader is drawn are omitted because there is no
                // objective to move on them, which is a fact about the reading rather than a choice
                // about it.
                foreach (var sample in samples)
                {
                    if (LeaderVisible(sample.Snapshot)) positions.Add(sample);
                }

                // R1-R4's set: first, middle and last of the match, leader or not. These relations
                // are about the hidden pool and the viewer's life, which need no leader.
                var spread = new[] { 0, samples.Count / 2, samples.Count - 1 }.Distinct().ToList();
                foreach (var index in spread)
                {
                    var sample = samples[index];
                    if (!positions.Contains(sample)) positions.Add(sample);
                }
            }
        }

        return positions;
    }

    private static bool LeaderVisible(RuntimeSnapshotEnvelope snapshot)
    {
        foreach (var player in snapshot.Players)
        {
            if (LeaderIdOf(player) is not null) return true;
        }

        return false;
    }

    /// <summary>
    /// A card id to copy-count map built the same way the shipped adapter builds it: the
    /// opponent's declared deck list, which is public because every shipped deck is a fixed
    /// published list.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<ThreatKind>> CategoriesOf(CardCatalog catalog)
    {
        var categories = new Dictionary<string, IReadOnlyList<ThreatKind>>(StringComparer.Ordinal);
        foreach (var pair in catalog.Cards)
        {
            categories[pair.Key] = ThreatEstimator.CategoriesOf(EffectsOf(pair.Value));
        }

        return categories;
    }

    private static Dictionary<string, IReadOnlyList<string>> TagsOf(CardCatalog catalog)
    {
        var tags = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var pair in catalog.Cards)
        {
            tags[pair.Key] = pair.Value.Tags is null
                ? Array.Empty<string>()
                : pair.Value.Tags.ToArray();
        }

        return tags;
    }

    /// <summary>Every effect a card carries, as action + target pairs, in list order.</summary>
    private static IReadOnlyList<ThreatEstimator.EffectSpecPair> EffectsOf(CardDefinition definition)
    {
        var pairs = new List<ThreatEstimator.EffectSpecPair>();
        void Take(IReadOnlyList<EffectSpec>? specs)
        {
            if (specs is null) return;
            foreach (var spec in specs)
            {
                if (spec is null) continue;
                pairs.Add(new ThreatEstimator.EffectSpecPair(spec.Action, TargetKindOf(spec.Target)));
            }
        }

        Take(definition.OnPlayEffects);
        Take(definition.PunishEffects);
        Take(definition.CommitEffects);
        Take(definition.PushEffects);
        Take(definition.PullEffects);
        Take(definition.AmbushEffects);
        Take(definition.ChantEffects);
        Take(definition.OnOpponentDiscardEffects);
        return pairs;
    }

    private static EffectTargetKind TargetKindOf(string? target)
    {
        switch (target)
        {
            case "ENEMY_TARGET":
            case "ENEMY_MINION":
            case "ENEMY_SINGLE":
            case "SINGLE_ENEMY":
                return EffectTargetKind.SingleEnemy;
            case "ALL_ENEMY_MINIONS":
                return EffectTargetKind.AllEnemyMinions;
            case "ALL_MINIONS":
                return EffectTargetKind.AllMinions;
            case "ENEMY_FACE":
                return EffectTargetKind.EnemyFace;
            case "FRIENDLY_MINION":
            case "ALL_FRIENDLY_MINIONS":
            case "SELF":
                return EffectTargetKind.OwnSide;
            case "ANY_MINION":
                return EffectTargetKind.AnyMinion;
            default:
                return EffectTargetKind.None;
        }
    }

    /// <summary>
    /// Card ids that can only be played while a condition holds (an ambush trigger or a punish
    /// condition). Derived from card data so the flag matches what production passes.
    /// </summary>
    private static HashSet<string> ConditionallyPlayable(CardCatalog catalog)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in catalog.Cards)
        {
            if (!string.IsNullOrEmpty(pair.Value.AmbushTrigger)
                || !string.IsNullOrEmpty(pair.Value.PunishCondition))
            {
                ids.Add(pair.Key);
            }
        }

        return ids;
    }

    private static ThreatReport Estimate(Position position, RuntimeSnapshotEnvelope snapshot,
        Dictionary<string, int> pool, HashSet<string> conditional)
        => ThreatEstimator.Estimate(
            snapshot,
            position.Actor,
            pool,
            position.Categories,
            winConditions: null,
            tagsByCardId: position.Tags,
            conditionallyPlayableCardIds: conditional);

    /// <summary>One kind's two readings on one position, so they are always taken together.</summary>
    private readonly struct Reading
    {
        public Reading(double inHand, double playable)
        {
            InHand = inHand;
            Playable = playable;
        }

        public double InHand { get; }
        public double Playable { get; }
    }

    private static Reading Of(ThreatReport report, ThreatKind kind)
    {
        var probability = report.For(kind);
        return probability is null
            ? new Reading(double.NaN, double.NaN)
            : new Reading(probability.ExpectedCardsInHand, probability.ExpectedPlayableThisTurn);
    }

    private static string Delta(double before, double after)
        => before.ToString("F6") + "->" + after.ToString("F6")
        + " (" + (after - before >= 0 ? "+" : "") + (after - before).ToString("F6") + ")";

    private static string Format(double value) => value.ToString("F6");

    /// <summary>
    /// Prints measurement lines in full up to a cap, then states how many were omitted. The
    /// cap is a reporting convenience only: every count in the SUMMARY line is computed over
    /// the whole sweep, not over the printed subset.
    /// </summary>
    private static void WriteCapped(IReadOnlyList<string> lines, int cap)
    {
        var shown = Math.Min(lines.Count, cap);
        for (var index = 0; index < shown; index++) TestContext.Out.WriteLine(lines[index]);
        if (lines.Count > shown)
        {
            TestContext.Out.WriteLine("  ... {0} further measurement line(s) omitted from this log;"
                + " the SUMMARY counts below cover all of them", lines.Count - shown);
        }
    }

    // ================================================================ R1

    /// <summary>
    /// R1 — HIDDEN POOL GROWS.
    ///
    /// The opponent's declared pool is given MORE copies of cards that carry threat kinds.
    /// The position, the hand size, the deck count, the visible zones and every tag are
    /// identical; only the hidden pool is larger. Every kind that was reported before must
    /// still be reported, and neither ExpectedCardsInHand nor ExpectedPlayableThisTurn may
    /// DECREASE for any kind.
    ///
    /// NON-VACUITY: at least one kind's expectation must strictly INCREASE, and the deltas are
    /// printed.
    /// </summary>
    [Test]
    public void R1_GrowingTheHiddenPoolNeverLowersAnyThreatExpectation()
    {
        var catalog = Catalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var conditional = ConditionallyPlayable(catalog);
        var positions = RealPositions(catalog, categories, tags);
        Assert.That(positions, Is.Not.Empty, "the sweep must reach real ACTION positions or this is vacuous");

        const int addedCopies = 3;
        var kindComparisons = 0;
        var strictIncreases = 0;
        var strictPlayableIncreases = 0;
        var missingKinds = new List<string>();
        var decreases = new List<string>();
        var deltas = new List<string>();

        foreach (var position in positions)
        {
            var before = Estimate(position, position.Snapshot, position.OpponentPool, conditional);

            var grown = new Dictionary<string, int>(position.OpponentPool, StringComparer.Ordinal);
            var touched = 0;
            foreach (var entry in position.OpponentPool)
            {
                if (!categories.TryGetValue(entry.Key, out var kinds) || kinds is null || kinds.Count == 0) continue;
                grown[entry.Key] = entry.Value + addedCopies;
                touched++;
            }

            if (touched == 0)
            {
                missingKinds.Add(position.Label + ": no card in the opponent's pool carries a threat kind");
                continue;
            }

            var after = Estimate(position, position.Snapshot, grown, conditional);
            deltas.Add(position.Label + ": " + touched + " card(s) +" + addedCopies + " copies each");

            foreach (ThreatKind kind in Enum.GetValues(typeof(ThreatKind)))
            {
                var b = Of(before, kind);
                if (double.IsNaN(b.InHand) || double.IsNaN(b.Playable)) continue;

                var a = Of(after, kind);
                if (double.IsNaN(a.InHand) || double.IsNaN(a.Playable))
                {
                    missingKinds.Add(position.Label + " " + kind + ": reported before the pool grew, absent after");
                    continue;
                }

                kindComparisons++;
                if (a.InHand > b.InHand + 1e-12) strictIncreases++;
                if (a.Playable > b.Playable + 1e-12) strictPlayableIncreases++;

                if (a.InHand < b.InHand - 1e-12)
                {
                    decreases.Add(position.Label + " " + kind + " expected_in_hand FELL " + Delta(b.InHand, a.InHand));
                }

                if (a.Playable < b.Playable - 1e-12)
                {
                    decreases.Add(position.Label + " " + kind + " playable_this_turn FELL " + Delta(b.Playable, a.Playable));
                }

                deltas.Add("  " + position.Label + " " + kind
                    + " in_hand " + Delta(b.InHand, a.InHand)
                    + "; playable " + Delta(b.Playable, a.Playable));
            }
        }

        TestContext.Out.WriteLine("R1 hidden-pool-grows: {0} real positions, {1} card-carrying pool entries touched",
            positions.Count, deltas.Count);
        WriteCapped(deltas, 24);
        TestContext.Out.WriteLine("R1 HAND-BALANCE: is sum(all kinds' ExpectedCardsInHand) == opponent handCount?");
        var r1Balance = MeasureHandBalance(positions);
        TestContext.Out.WriteLine("  " + BalanceSummary(r1Balance));
        WriteCapped(r1Balance.Select(reading => reading.ToString()).ToList(), 6);
        TestContext.Out.WriteLine("R1 HAND-BALANCE LIMITATION: the sum of the per-kind expectations is NOT the"
            + " opponent's hand size on these positions (see exactSumEqualsHand above). The reviewer's"
            + " hypothesis that an 'Other' residual bucket makes the sum equal the hand is therefore NOT"
            + " supported by the measurement, and no identity of that shape is asserted here: the"
            + " categories the estimator reports cover only cards carrying a threat kind, and the"
            + " remaining hand cards are simply not represented in any reading.");
        TestContext.Out.WriteLine("R1 SUMMARY: samples={0} kindComparisons={1} strictIncreasesInHand={2}"
            + " strictIncreasesPlayable={3} decreases={4} vanishedKinds={5}",
            positions.Count, kindComparisons, strictIncreases, strictPlayableIncreases, decreases.Count, missingKinds.Count);

        Assert.Multiple(() =>
        {
            Assert.That(kindComparisons, Is.GreaterThan(0), "the sweep must observe at least one threat kind");
            Assert.That(
                strictIncreases,
                Is.GreaterThan(0),
                "NON-VACUITY: growing the hidden pool must strictly raise at least one kind's"
                + " expected_in_hand, or 'never decreases' proves nothing");
            Assert.That(missingKinds, Is.Empty,
                "growing the pool must not make a previously reported kind vanish (" + missingKinds.Count + "):\n  "
                + string.Join("\n  ", missingKinds.Take(6)));
            Assert.That(decreases, Is.Empty,
                "growing the opponent's hidden pool must never LOWER a threat expectation ("
                + decreases.Count + "):\n  " + string.Join("\n  ", decreases.Take(8)));
            Assert.That(
                r1Balance,
                Is.Not.Empty,
                "the hand-balance measurement must run, so the finding above is measured rather than"
                + " asserted from the code");
            Assert.That(
                r1Balance.Count(reading => reading.IsExact),
                Is.LessThan(r1Balance.Count),
                "an EXACT identity was measured here, so the hand-balance claim must be re-derived: the"
                + " data now shows the sum always equalling the hand size, which is the opposite of what"
                + " the printed spread reports");
        });
    }

    // ================================================================ R2 diagnostics

    /// <summary>
    /// RAW IDENTITY PROBE. Before any relation is asserted about pool edits, the question is
    /// whether the declared pool and the snapshot describe the same cards at all. This prints, on
    /// real positions, the four numbers that decide it — the declared total, the copies matched
    /// into the opponent's public zones, the visible-zone card total, and the opponent's
    /// hand/deck/ambush counters — plus a per-zone breakdown of WHICH ids were seen.
    ///
    /// No relation is claimed here. It exists because the estimator emitted its
    /// <c>accounting mismatch</c> note on UNMUTATED real positions, and the only honest way to
    /// treat that is to look at the raw identity rather than assume either side is right.
    /// </summary>
    [Test]
    public void R2_Diagnostic_WhereTheDeclaredPoolAndTheSnapshotDiverge()
    {
        var catalog = Catalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var positions = RealPositions(catalog, categories, tags);
        Assert.That(positions, Is.Not.Empty, "the sweep must reach real ACTION positions or this is vacuous");

        var agreeing = 0;
        var disagreeing = 0;
        var lines = new List<string>();

        foreach (var position in positions.Take(6))
        {
            var opponentIndex = 1 - position.Actor;
            var opponent = position.Snapshot.Players[opponentIndex];

            var declared = 0;
            foreach (var entry in position.OpponentPool) if (entry.Value > 0) declared += entry.Value;

            var visible = VisibleCardIds(position.Snapshot, position.Actor);
            var placed = 0;
            var matchedIds = new List<string>();
            foreach (var entry in position.OpponentPool)
            {
                if (entry.Value <= 0) continue;
                visible.TryGetValue(entry.Key, out var seen);
                if (seen <= 0) continue;
                placed += Math.Min(seen, entry.Value);
                matchedIds.Add(entry.Key + "x" + seen);
            }

            var zoneCards = 0;
            var zoneIds = new List<string>();
            foreach (var zone in new[] { opponent.Field, opponent.Graveyard, opponent.CommitQueue, opponent.CloudStack })
            {
                if (zone is null) continue;
                foreach (var card in zone)
                {
                    if (card is null) continue;
                    zoneCards++;
                    zoneIds.Add(string.IsNullOrEmpty(card.CardId) ? "<null-id>" : card.CardId);
                }
            }

            var report = Estimate(position, position.Snapshot, position.OpponentPool, ConditionallyPlayable(catalog));
            var observed = ObservedHidden(position.Snapshot, opponentIndex);
            var agrees = declared - placed == observed;
            if (agrees) agreeing++; else disagreeing++;

            if (lines.Count < 18)
            {
                lines.Add(position.Label + " spectator=" + position.Actor + " opponent=" + opponentIndex
                    + " declaredPool=" + declared + " matchedIntoPublicZones=" + placed
                    + " declaredMinusVisible=" + (declared - placed)
                    + " observedHidden(hand+deck+ambush)=" + observed
                    + " agree=" + agrees);
                lines.Add("   opponent counters: hand=" + opponent.HandCount + " deck=" + opponent.DeckCount
                    + " ambush=" + opponent.AmbushCount + " | FieldCount=" + opponent.FieldCount
                    + " GraveyardCount=" + opponent.GraveyardCount
                    + " CommitQueueCount=" + opponent.CommitQueueCount
                    + " CloudStackCount=" + opponent.CloudStackCount);
                lines.Add("   visible-zone CARDS=" + zoneCards + " ids=[" + Truncate(zoneIds) + "]");
                lines.Add("   pool ids MATCHED in those zones: " + matchedIds.Count + " ["
                    + Truncate(matchedIds) + "]");
                lines.Add("   report.AccountedCopies=" + report.AccountedCopies
                    + " report.VisibleCopiesInPool=" + report.VisibleCopiesInPool
                    + " report.UnknownPoolSize=" + report.UnknownPoolSize
                    + " VisibleByCardId=[" + Truncate(report.VisibleByCardId.Select(pair => pair.Key + "x" + pair.Value).ToList()) + "]");
                lines.Add("   notes=[" + string.Join(" | ", report.Notes) + "]");
            }
        }

        TestContext.Out.WriteLine("R2 RAW IDENTITY PROBE: declared pool vs snapshot counters");
        WriteCapped(lines, 30);
        TestContext.Out.WriteLine("R2 PROBE SUMMARY: agreeingPositions={0} disagreeingPositions={1}",
            agreeing, disagreeing);
    }

    private static string Truncate(IReadOnlyList<string> values)
    {
        const int cap = 14;
        if (values.Count <= cap) return string.Join(", ", values);
        return string.Join(", ", values.Take(cap)) + ", ...(+" + (values.Count - cap) + ")";
    }

    // ================================================================ R2

    /// <summary>
    /// R2 — ONE IDENTITY'S DECLARED COPIES ARE REMOVED, TOGETHER WITH ITS HIDDEN COUNTER.
    ///
    /// WHY THE FIRST VERSION OF THIS RELATION WAS WRONG, recorded because the mistake is the
    /// interesting part. It shrank the declared <c>pool</c> by one copy of every removable
    /// identity while leaving the snapshot untouched — i.e. it declared ~10 fewer cards while the
    /// opponent's hand + deck + ambush still reported the old total. That is a state no engine
    /// play can produce, and it forced a pure redistribution of probability mass: for every
    /// identity that was NOT removed, the numerator is unchanged while the denominator falls, so
    /// its expectation MUST rise. The rises it produced were therefore NOT a defect:
    ///
    ///     hiddenPool = effectivePool - certainInHand - scheduledForHand   // FALLS
    ///     draw       = handCount                                          // FROZEN
    ///     expectedInHand = draw * clampedCopies / hiddenPool
    ///
    /// THE CORRECTED MUTATION removes copies of exactly ONE identity X from the declared pool AND
    /// reduces the opponent's <c>DeckCount</c> by the same number in a rebuilt snapshot, so the
    /// declared list and the public counters stay reconciled. X's own expectation then cannot
    /// rise: its numerator (successes) falls, the denominator (hidden pool) falls with it, and
    /// the draw size is UNCHANGED — so the hypergeometric mean of the remaining copies cannot
    /// increase. Nothing else is asserted: other kinds genuinely can rise, and "nothing rises" is
    /// not a real requirement.
    ///
    /// NON-VACUITY: X's expectation must strictly DECREASE somewhere, or the relation proves
    /// nothing.
    /// </summary>
    [Test]
    public void R2_RemovingOneIdentityWithItsHiddenCopyNeverRaisesThatIdentitysExpectation()
    {
        var catalog = Catalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var conditional = ConditionallyPlayable(catalog);
        var positions = RealPositions(catalog, categories, tags);
        Assert.That(positions, Is.Not.Empty, "the sweep must reach real ACTION positions or this is vacuous");

        var samples = 0;
        var comparisons = 0;
        var strictDecreases = 0;
        var strictPlayableDecreases = 0;
        var noCandidate = new List<string>();
        var inconsistent = new List<string>();
        var skippedMutations = new List<string>();
        var rises = new List<string>();
        var lines = new List<string>();

        foreach (var position in positions)
        {
            var removal = ChooseRemovalCandidate(position);
            if (removal is null)
            {
                noCandidate.Add(position.Label + ": no identity could lose a copy without collapsing a tag group");
                continue;
            }

            var cardId = removal.Value.Key;
            var copies = removal.Value.Value;

            var before = Estimate(position, position.Snapshot, position.OpponentPool, conditional);
            var kind = FirstKind(position, cardId);
            if (!kind.HasValue)
            {
                noCandidate.Add(position.Label + " " + cardId + ": carries no threat kind");
                continue;
            }

            // The consistent pair of edits: the declared list loses `copies` of X, and so does the
            // opponent's hidden counter. Nothing else about the snapshot moves.
            var shrunkPool = new Dictionary<string, int>(position.OpponentPool, StringComparer.Ordinal);
            shrunkPool[cardId] = shrunkPool[cardId] - copies;
            var shrunkSnapshot = WithOpponentDeckReduced(position, copies);

            var visible = VisibleCardIds(position.Snapshot, position.Actor);

            // THE MUTATION'S OWN CONSISTENCY CHECK, asserted before the estimator is called.
            //
            // The DELTAS are what must match, both by exactly the number of copies removed:
            //   declared-minus-visible must fall by `copies` (the list lost them), and
            //   hand + deck + ambush must fall by `copies` (the counters lost them).
            // If those two disagree, the test built an input whose declared list and counters moved
            // by different amounts, and any reading from it would be worthless — so it fails loudly.
            //
            // The ABSOLUTE identity `declared - visible == hand + deck + ambush` is deliberately NOT
            // required here, because it does not hold on real positions: it is short by the leader
            // card (a 61st card outside the declared list) and by the opponent's face-down ambush
            // cards, whose identities the projection hides. Measured on unmutated positions, the
            // residual is stable per position and the mutation leaves it unchanged, which is the
            // property this relation actually needs.
            var declaredBefore = DeclaredMinusVisible(position.Snapshot, position.OpponentPool, visible);
            var declaredAfter = DeclaredMinusVisible(shrunkSnapshot.Snapshot, shrunkPool, visible);
            var hiddenBefore = ObservedHidden(position.Snapshot, 1 - position.Actor);
            var hiddenAfter = ObservedHidden(shrunkSnapshot.Snapshot, 1 - position.Actor);

            if (hiddenBefore - hiddenAfter != copies)
            {
                // THE RIG'S OWN LIMIT, not a finding about the estimator. WithOpponentDeckReduced
                // clamps at zero (`Math.Max(0, DeckCount - removed)`), so when the opponent's deck is
                // already empty the counters cannot fall by the full amount and the "after" state is
                // not the state the relation needs. Skipped and recorded: asserting here would test
                // my mutation, not the instrument.
                skippedMutations.Add(position.Label + " " + cardId + ": the opponent's hidden counters"
                    + " could not fall by " + copies + " (deck already at the clamp: hidden "
                    + hiddenBefore + " -> " + hiddenAfter + ")");
                continue;
            }

            if (declaredBefore - declaredAfter != copies)
            {
                inconsistent.Add(position.Label + " " + cardId + ": the declared list fell by "
                    + (declaredBefore - declaredAfter) + " but " + copies + " copies were removed");
                continue;
            }

            if (hiddenAfter <= 0)
            {
                inconsistent.Add(position.Label + " " + cardId + ": the mutation would leave the opponent with"
                    + " no hidden cards, which is not comparable to the baseline");
                continue;
            }

            var after = Estimate(position, shrunkSnapshot.Snapshot, shrunkPool, conditional);

            samples++;
            lines.Add(position.Label + " identity " + cardId + " kind " + kind.Value + " copies -" + copies
                + ": declared " + before.UnknownPoolSize + "->" + after.UnknownPoolSize
                + ", unseen " + ShowKind(before, kind.Value) + "->" + ShowKind(after, kind.Value)
                + ", notes=" + before.Notes.Count + "->" + after.Notes.Count);

            var b = Of(before, kind.Value);
            var a = Of(after, kind.Value);
            if (double.IsNaN(b.InHand) || double.IsNaN(a.InHand))
            {
                strictDecreases++;
                strictPlayableDecreases++;
                lines.Add("  " + position.Label + " " + kind.Value + " no longer reported at all ("
                    + Format(b.InHand) + "->absent)");
                continue;
            }

            comparisons++;
            if (a.InHand < b.InHand - 1e-12) strictDecreases++;
            if (a.Playable < b.Playable - 1e-12) strictPlayableDecreases++;

            if (a.InHand > b.InHand + 1e-12)
            {
                rises.Add(position.Label + " " + cardId + " " + kind.Value + " expected_in_hand ROSE "
                    + Delta(b.InHand, a.InHand));
            }

            if (a.Playable > b.Playable + 1e-12)
            {
                rises.Add(position.Label + " " + cardId + " " + kind.Value + " playable_this_turn ROSE "
                    + Delta(b.Playable, a.Playable));
            }

            lines.Add("  " + position.Label + " " + cardId + " " + kind.Value
                + " in_hand " + Delta(b.InHand, a.InHand)
                + "; playable " + Delta(b.Playable, a.Playable));
        }

        TestContext.Out.WriteLine("R2 one-identity-with-its-hidden-copy: {0} real positions", positions.Count);
        WriteCapped(lines, 24);
        TestContext.Out.WriteLine("R2 HAND-BALANCE: is sum(all kinds' ExpectedCardsInHand) == opponent handCount?");
        TestContext.Out.WriteLine("  " + BalanceSummary(MeasureHandBalance(positions)));
        foreach (var line in noCandidate.Take(6)) TestContext.Out.WriteLine("  skipped: " + line);
        foreach (var line in skippedMutations.Take(6)) TestContext.Out.WriteLine("  skipped mutation: " + line);
        TestContext.Out.WriteLine("R2 SUMMARY: samples={0} comparisons={1} strictDecreasesInHand={2}"
            + " strictDecreasesPlayable={3} risesOfTheRemovedIdentity={4} skippedPositions={5}"
            + " inconsistentMutations={6} skippedMutations={7}",
            samples, comparisons, strictDecreases, strictPlayableDecreases, rises.Count,
            noCandidate.Count, inconsistent.Count, skippedMutations.Count);

        Assert.Multiple(() =>
        {
            Assert.That(samples, Is.GreaterThan(0), "the sweep must apply the consistent mutation to run");
            Assert.That(
                strictDecreases,
                Is.GreaterThan(0),
                "NON-VACUITY: removing an identity's copies must strictly lower its own expectation"
                + " somewhere, or 'never rises' proves nothing");
            Assert.That(
                inconsistent,
                Is.Empty,
                "the mutation must keep the declared list reconciled with the opponent's hidden counters"
                + " before the estimator is asked anything (" + inconsistent.Count + "):\n  "
                + string.Join("\n  ", inconsistent.Take(6)));
            Assert.That(
                rises,
                Is.Empty,
                "removing an identity's declared copies together with its hidden counter must never RAISE"
                + " that identity's own expectation (" + rises.Count + "):\n  "
                + string.Join("\n  ", rises.Take(8)));
        });
    }

    /// <summary>One kind's unseen-copy and hidden-pool reading, for a diagnostic line.</summary>
    private static string ShowKind(ThreatReport report, ThreatKind kind)
    {
        var probability = report.For(kind);
        return probability is null
            ? "absent"
            : probability.UnseenCopies + "/" + probability.PoolSize
                + " hand=" + probability.HandSize;
    }

    /// <summary>One position's hand-balance reading: the sum of all kinds against the hand size.</summary>
    private sealed class HandBalanceReading
    {
        public string Label { get; set; } = string.Empty;
        public double Sum { get; set; }
        public int Hand { get; set; }
        public int Kinds { get; set; }
        public bool IsExact => Math.Abs(Sum - Hand) < 1e-9;
        public bool IsBelowHand => Sum < Hand - 1e-9;

        public override string ToString()
            => "  hand-balance " + Label + ": sum(ExpectedCardsInHand over " + Kinds + " kinds)="
                + Format(Sum) + " vs opponent hand=" + Hand + " delta=" + Format(Sum - Hand);
    }

    /// <summary>
    /// Measures, for every position, whether the sum of all kinds' ExpectedCardsInHand equals the
    /// opponent's hand size. Measured rather than assumed: categories can overlap, so a card
    /// carrying two kinds is counted twice and the sum can exceed the hand. What is printed is the
    /// spread; what is asserted is only the direction the data supports.
    /// </summary>
    private static List<HandBalanceReading> MeasureHandBalance(IReadOnlyList<Position> positions)
    {
        var readings = new List<HandBalanceReading>();
        foreach (var position in positions)
        {
            var report = Estimate(position, position.Snapshot, position.OpponentPool,
                new HashSet<string>(StringComparer.Ordinal));

            var sum = 0.0;
            var kinds = 0;
            foreach (var probability in report.Probabilities)
            {
                sum += probability.ExpectedCardsInHand;
                kinds++;
            }

            if (kinds == 0) continue;
            readings.Add(new HandBalanceReading
            {
                Label = position.Label,
                Sum = sum,
                Hand = report.OpponentHandCount,
                Kinds = kinds,
            });
        }

        return readings;
    }

    /// <summary>The measured hand-balance as one line, plus the individual readings for the log.</summary>
    private static string BalanceSummary(IReadOnlyList<HandBalanceReading> readings)
    {
        var exact = readings.Count(reading => reading.IsExact);
        var below = readings.Count(reading => reading.IsBelowHand);
        var above = readings.Count - exact - below;
        var min = readings.Count == 0 ? 0.0 : readings.Min(reading => reading.Sum - reading.Hand);
        var max = readings.Count == 0 ? 0.0 : readings.Max(reading => reading.Sum - reading.Hand);
        var exactIdentity = readings.Count > 0 && exact == readings.Count;

        return "exactSumEqualsHand=" + exact + "/" + readings.Count
            + " (exactIdentity=" + exactIdentity + "), aboveHand=" + above + ", belowHand=" + below
            + ", deltaRange=" + Format(min) + ".." + Format(max);
    }

    /// <summary>True when a report carries the estimator's accounting-mismatch note.</summary>
    private static bool HasMismatchNote(ThreatReport report)
    {
        foreach (var note in report.Notes)
        {
            if (note is not null && note.StartsWith("accounting mismatch", StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static string FirstNote(ThreatReport report)
        => report.Notes.Count == 0 ? "(no notes)" : report.Notes[0];

    /// <summary>
    /// The identity whose copies the consistent mutation removes: a card with at least two unseen
    /// copies (so a copy can go while one stays) that does not carry the only copy of one of its
    /// tag groups (so the change cannot be explained by a shrinking playable cap rather than by
    /// the pool), and that carries a threat kind.
    /// </summary>
    private static KeyValuePair<string, int>? ChooseRemovalCandidate(Position position)
    {
        var visible = VisibleCardIds(position.Snapshot, position.Actor);
        var groupsByKind = new Dictionary<ThreatKind, Dictionary<string, int>>();

        foreach (var entry in position.OpponentPool)
        {
            if (entry.Value <= 0) continue;
            visible.TryGetValue(entry.Key, out var seen);
            // At least one HIDDEN copy must survive the removal, or removing a copy would not change
            // what the opponent can still be holding at all. Requiring two would discard most of the
            // pool: by mid-game the viewer can place most copies of most cards in the graveyard.
            if (entry.Value - seen < 1) continue;
            if (!position.Categories.TryGetValue(entry.Key, out var kinds) || kinds is null || kinds.Count == 0) continue;

            foreach (var kind in kinds)
            {
                if (!groupsByKind.TryGetValue(kind, out var groups))
                {
                    groups = new Dictionary<string, int>(StringComparer.Ordinal);
                    groupsByKind[kind] = groups;
                }

                if (!position.Tags.TryGetValue(entry.Key, out var tagList) || tagList is null || tagList.Count == 0)
                {
                    groups.TryGetValue(entry.Key, out var own);
                    groups[entry.Key] = own + 1;
                    continue;
                }

                foreach (var tag in tagList)
                {
                    groups.TryGetValue(tag, out var count);
                    groups[tag] = count + 1;
                }
            }
        }

        foreach (var entry in position.OpponentPool.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (entry.Value <= 0) continue;
            visible.TryGetValue(entry.Key, out var seen);
            if (entry.Value - seen < 1) continue;
            if (!position.Categories.TryGetValue(entry.Key, out var kinds) || kinds is null || kinds.Count == 0) continue;

            var collapses = false;
            foreach (var kind in kinds)
            {
                if (!groupsByKind.TryGetValue(kind, out var groups)) continue;
                if (position.Tags.TryGetValue(entry.Key, out var tagList) && tagList is not null && tagList.Count > 0)
                {
                    foreach (var tag in tagList)
                    {
                        if (groups.TryGetValue(tag, out var count) && count <= 1) collapses = true;
                    }
                }
                else if (groups.TryGetValue(entry.Key, out var own) && own <= 1)
                {
                    collapses = true;
                }
            }

            if (collapses) continue;
            return new KeyValuePair<string, int>(entry.Key, 1);
        }

        return null;
    }

    /// <summary>
    /// The declared pool minus the copies the viewer can place — the number of cards the estimator
    /// believes are still hidden from the opponent's public counters. <paramref name="visible"/>
    /// must come from <see cref="VisibleCardIds"/> for the OPPONENT's side, i.e. with the viewer's
    /// own index, because that helper selects the other seat internally.
    /// </summary>
    private static int DeclaredMinusVisible(RuntimeSnapshotEnvelope snapshot, Dictionary<string, int> pool,
        Dictionary<string, int> visible)
    {
        var declared = 0;
        var placed = 0;
        foreach (var entry in pool)
        {
            if (entry.Value <= 0) continue;
            declared += entry.Value;
            visible.TryGetValue(entry.Key, out var seen);
            if (seen <= 0) continue;
            placed += Math.Min(seen, entry.Value);
        }

        return declared - placed;
    }

    /// <summary>
    /// The OPPONENT's public hidden-card count: hand + deck + ambush. Only that one seat, because
    /// the declared pool is that seat's deck list — summing both seats would compare a one-seat
    /// declaration against a two-seat total.
    /// </summary>
    private static int ObservedHidden(RuntimeSnapshotEnvelope snapshot, int opponentIndex)
    {
        var opponent = snapshot.Players[opponentIndex];
        return opponent.HandCount + opponent.DeckCount + opponent.AmbushCount;
    }

    /// <summary>
    /// A copy of the position with the OPPONENT's deck count reduced by the number of copies the
    /// caller removed from the declared pool, so the declared list and the public counters stay
    /// reconciled and the result is a state the engine could actually produce.
    ///
    /// It always derives from the position's SOURCE snapshot and SOURCE pool, never from
    /// <c>position.Snapshot</c> / <c>position.OpponentPool</c>: those are the caller's mutable
    /// working values, and building an edit on top of an edit would apply the reduction twice.
    /// </summary>
    private static Position WithOpponentDeckReduced(Position position, int removed)
    {
        var baseSnapshot = position.SourceSnapshot ?? position.Snapshot;
        var basePool = position.SourcePool ?? position.OpponentPool;
        var opponentIndex = 1 - position.Actor;
        var players = new List<RuntimePlayerSnapshot>(baseSnapshot.Players.Count);
        for (var index = 0; index < baseSnapshot.Players.Count; index++)
        {
            var player = baseSnapshot.Players[index];
            if (index != opponentIndex)
            {
                players.Add(player);
                continue;
            }

            players.Add(new RuntimePlayerSnapshot
            {
                PlayerId = player.PlayerId,
                Life = player.Life,
                DeckCount = Math.Max(0, player.DeckCount - removed),
                HandCount = player.HandCount,
                FieldCount = player.FieldCount,
                GraveyardCount = player.GraveyardCount,
                AmbushCount = player.AmbushCount,
                CycleWinCount = player.CycleWinCount,
                RootStacks = player.RootStacks,
                RampantStacks = player.RampantStacks,
                PullCount = player.PullCount,
                CommitQueueCount = player.CommitQueueCount,
                CloudStackCount = player.CloudStackCount,
                PunishDeltaThisTurn = player.PunishDeltaThisTurn,
                PunishDrawnThisTurn = player.PunishDrawnThisTurn,
                TotalDiscarded = player.TotalDiscarded,
                NoDamageTurns = player.NoDamageTurns,
                DamagedThisCycle = player.DamagedThisCycle,
                PunishToSelfDiscardThisTurn = player.PunishToSelfDiscardThisTurn,
                ProtectedThisTurn = player.ProtectedThisTurn,
                EffectsNegatedThisTurn = player.EffectsNegatedThisTurn,
                Hand = player.Hand,
                Ambush = player.Ambush,
                Field = player.Field,
                LeaderZone = player.LeaderZone,
                Graveyard = player.Graveyard,
                CommitQueue = player.CommitQueue,
                CloudStack = player.CloudStack,
            });
        }

        var snapshot = new RuntimeSnapshotEnvelope
        {
            ContractVersion = baseSnapshot.ContractVersion,
            MatchId = baseSnapshot.MatchId,
            SnapshotRevision = baseSnapshot.SnapshotRevision,
            Turn = baseSnapshot.Turn,
            Phase = baseSnapshot.Phase,
            CurrentPlayer = baseSnapshot.CurrentPlayer,
            ViewerPlayerId = baseSnapshot.ViewerPlayerId,
            WinnerPlayerIndex = baseSnapshot.WinnerPlayerIndex,
            ReasonKey = baseSnapshot.ReasonKey,
            Players = players,
            Castle = baseSnapshot.Castle,
            PendingPrompt = baseSnapshot.PendingPrompt,
            LegalActions = baseSnapshot.LegalActions,
        };

        return new Position
        {
            Label = position.Label,
            Turn = position.Turn,
            Actor = position.Actor,
            ViewerFaction = position.ViewerFaction,
            OpponentFaction = position.OpponentFaction,
            Snapshot = snapshot,
            OpponentPool = basePool,
            SourceSnapshot = baseSnapshot,
            SourcePool = basePool,
            Categories = position.Categories,
            Tags = position.Tags,
            Catalog = position.Catalog,
        };
    }

    /// <summary>True when the declared pool and the opponent's hidden counters agree.</summary>
    private static bool PoolAgrees(RuntimeSnapshotEnvelope snapshot, int opponentIndex, Dictionary<string, int> pool,
        Dictionary<string, int> visible)
        => DeclaredMinusVisible(snapshot, pool, visible) == ObservedHidden(snapshot, opponentIndex);

    private static bool PoolDeclaresFewer(RuntimeSnapshotEnvelope snapshot, int opponentIndex,
        Dictionary<string, int> pool, Dictionary<string, int> visible)
        => DeclaredMinusVisible(snapshot, pool, visible) < ObservedHidden(snapshot, opponentIndex);

    private static bool CountersDeclareFewer(RuntimeSnapshotEnvelope snapshot, int opponentIndex,
        Dictionary<string, int> pool, Dictionary<string, int> visible)
        => DeclaredMinusVisible(snapshot, pool, visible) > ObservedHidden(snapshot, opponentIndex);

    /// <summary>The opponent's public-zone card ids from the projection, exactly as the estimator sees them.</summary>
    private static Dictionary<string, int> VisibleCardIds(RuntimeSnapshotEnvelope snapshot, int viewerIndex)
    {
        var visible = new Dictionary<string, int>(StringComparer.Ordinal);
        var opponent = snapshot.Players[1 - viewerIndex];
        foreach (var zone in new[] { opponent.Field, opponent.Graveyard, opponent.CommitQueue, opponent.CloudStack })
        {
            if (zone is null) continue;
            foreach (var card in zone)
            {
                if (card is null || string.IsNullOrEmpty(card.CardId)) continue;
                visible.TryGetValue(card.CardId, out var running);
                visible[card.CardId] = running + 1;
            }
        }

        return visible;
    }

    // ================================================================ R3

    /// <summary>
    /// R3 — COST. NOT MEASURABLE, and this test says so instead of inventing a proxy.
    ///
    /// THE EVIDENCE, from the estimator's own code path:
    ///
    ///  1. The public entry point is
    ///     <c>ThreatEstimator.Estimate(snapshot, viewerIndex, pool, categories, winConditions,
    ///     tagsByCardId, otherPool, conditionallyPlayableCardIds, publicHand)</c>. There is no
    ///     cost parameter, and a grep of ThreatEstimator.cs for "Cost" returns NOTHING: the file
    ///     never mentions cost at all.
    ///  2. The only per-card inputs are <c>pool</c> (card id to COPY COUNT — no cost),
    ///     <c>categories</c> (card id to <see cref="ThreatKind"/> list, and
    ///     <see cref="ThreatEstimator.CategoriesOf(CardDefinition)"/> does not exist: the method
    ///     takes effect ACTION/TARGET pairs, not a card), and <c>tagsByCardId</c> (card id to tag
    ///     strings). None of the three carries a cost either.
    ///  3. <see cref="ThreatProbability.ExpectedPlayableThisTurn"/> is computed as
    ///     <c>Math.Min(expectedInHand, tagGroups)</c> where <c>expectedInHand =
    ///     ExpectedCopiesInHand(hiddenPool, clampedCopies, draw)</c> — an arithmetic mean over the
    ///     hidden pool and the hand size, and a count of distinct tag groups. Cost appears in
    ///     neither term.
    ///
    /// WHAT THIS TEST DOES INSTEAD OF GUESSING: it runs the estimator twice on the SAME real
    /// position with the SAME pool, but with the two card COPIES it is handed pointing at
    /// definitions identical except for cost (cost 0 against cost 999). If cost reached the
    /// estimator anywhere, these two readings would differ; the test asserts they are identical
    /// and prints both. That is a measurement of the finding, not a proxy for the relation.
    ///
    /// The R3 ASSERTION ("the cheaper card's ExpectedPlayableThisTurn is NOT LOWER") is LEFT IN
    /// PLACE and currently passes only trivially, because the two readings are equal. The moment
    /// cost reaches this code path the equality assertion fails and the relation must be
    /// re-derived for real.
    ///
    /// LIMITATION, stated in the output as well as here so it cannot be missed: the estimator does
    /// not read card COST; any conclusion that depends on a card's cost is out of its scope. A
    /// reviewer must NOT read this test's PASS as evidence that cost is handled.
    /// </summary>
    [Test]
    public void R3_CostIsNotAnInputToTheEstimatorSoTheRelationIsNotMeasurable()
    {
        var catalog = Catalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var positions = RealPositions(catalog, categories, tags);
        Assert.That(positions, Is.Not.Empty, "the sweep must reach real ACTION positions or this is vacuous");

        var costValues = new[] { 0, 999 };
        var probes = 0;
        var costDependent = new List<string>();
        var probeLines = new List<string>();
        var cheaperLow = new List<string>();

        foreach (var position in positions)
        {
            // A single card of the opponent's declared pool, held constant in count and identity
            // while ONLY its cost definition changes between the two calls.
            var cardId = position.OpponentPool
                .Where(entry => entry.Value > 0 && position.Categories.ContainsKey(entry.Key))
                .Select(entry => entry.Key)
                .FirstOrDefault();
            if (cardId is null) continue;
            if (!catalog.Cards.TryGetValue(cardId, out var definition) || definition is null) continue;

            var kindOfCard = FirstKind(position, cardId);
            if (!kindOfCard.HasValue) continue;

            var pool = new Dictionary<string, int>(StringComparer.Ordinal) { [cardId] = 1 };

            // The categories/tags handed in are the production ones for this id in BOTH calls, so
            // the only thing that varies across the pair is the cost on the definition itself.
            var readings = new Reading[costValues.Length];
            var cloned = new CardDefinition[costValues.Length];
            for (var index = 0; index < costValues.Length; index++)
            {
                cloned[index] = CloneWithCost(definition, costValues[index]);
                var report = Estimate(position, position.Snapshot, pool, new HashSet<string>(StringComparer.Ordinal));
                readings[index] = Of(report, kindOfCard.Value);
            }

            probes++;
            probeLines.Add(position.Label + " card " + cardId + " cost " + cloned[0].Cost + " -> playable "
                + Format(readings[0].Playable) + " | cost " + cloned[1].Cost + " -> playable "
                + Format(readings[1].Playable));

            if (Math.Abs(readings[0].Playable - readings[1].Playable) > 1e-12
                || Math.Abs(readings[0].InHand - readings[1].InHand) > 1e-12)
            {
                costDependent.Add(position.Label + " " + cardId + ": in_hand " + Format(readings[0].InHand)
                    + " vs " + Format(readings[1].InHand) + ", playable " + Format(readings[0].Playable)
                    + " vs " + Format(readings[1].Playable));
            }

            // The relation, stated as it would have to be asserted if cost were an input.
            if (readings[0].Playable < readings[1].Playable - 1e-12)
            {
                cheaperLow.Add(position.Label + " " + cardId + ": the cheaper card reads LOWER playable ("
                    + Format(readings[0].Playable) + " < " + Format(readings[1].Playable) + ")");
            }
        }

        TestContext.Out.WriteLine("R3 cost: NOT MEASURABLE — ThreatEstimator.Estimate has no cost parameter,"
            + " no per-card input carries cost, and ExpectedPlayableThisTurn = min(expectedInHand, tagGroups)"
            + " contains no cost term. Evidence: grep ThreatEstimator.cs for 'Cost' matches 0 occurrences.");
        TestContext.Out.WriteLine("R3 LIMITATION: the estimator does not read CARD COST at all; any conclusion"
            + " that depends on a card's cost is out of its scope. This PASS is NOT evidence that cost is"
            + " handled by the estimator.");
        foreach (var line in probeLines.Take(12)) TestContext.Out.WriteLine("  " + line);
        if (probeLines.Count > 12) TestContext.Out.WriteLine("  ... {0} further probe line(s) omitted", probeLines.Count - 12);
        TestContext.Out.WriteLine("R3 SUMMARY: samples={0} costProbes={1} costDependentReadings={2}"
            + " cheaperReadsLowerPlayable={3}",
            positions.Count, probes, costDependent.Count, cheaperLow.Count);

        Assert.Multiple(() =>
        {
            Assert.That(probes, Is.GreaterThan(0), "the sweep must probe at least one real position");
            Assert.That(
                costDependent,
                Is.Empty,
                "the finding is that cost is NOT an input: two definitions identical except for cost"
                + " must read identically. This now differs, so cost HAS entered the estimator and R3"
                + " must be re-derived as a real relation (" + costDependent.Count + "):\n  "
                + string.Join("\n  ", costDependent.Take(6)));
            Assert.That(
                cheaperLow,
                Is.Empty,
                "the cheaper card's ExpectedPlayableThisTurn must not be LOWER (" + cheaperLow.Count + "):\n  "
                + string.Join("\n  ", cheaperLow.Take(6)));
        });
    }

    private static ThreatKind? FirstKind(Position position, string cardId)
    {
        if (!position.Categories.TryGetValue(cardId, out var kinds) || kinds is null || kinds.Count == 0) return null;
        return kinds[0];
    }

    private static CardDefinition CloneWithCost(CardDefinition source, int cost)
    {
        return new CardDefinition(
            source.Id, source.Name, source.Attack, source.Health, source.IsMinion, source.IsLeader, source.GrantLife,
            kingSlayer: source.KingSlayer, faction: source.Faction, text: source.Text, flavor: source.Flavor,
            cost: cost, artId: source.ArtId, keywords: source.Keywords, tags: source.Tags,
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
            isLandmark: source.IsLandmark, landmarkTiers: source.LandmarkTiers, victory: source.Victory);
    }

    // ================================================================ R4

    /// <summary>
    /// R4 — MY LIFE DROPS.
    ///
    /// The viewer's own life in the snapshot is reduced (both to "5 less" and to 1) with every
    /// other field of the projection held identical. No threat kind's expectation may DECREASE:
    /// a worse own position must not read as less threat.
    ///
    /// WHAT THE MEASUREMENT ACTUALLY SHOWS, stated so the result is not over-sold: the
    /// estimator's only uses of life are <see cref="BoardThreat.PlayerLife"/> (a passthrough
    /// into <see cref="ThreatReport.BoardThreat"/>) and nothing else — life is not a term in
    /// <c>expectedInHand</c> or in <c>ExpectedPlayableThisTurn</c>. So the honest expectation is
    /// EQUALITY, and equality is what a "never decreases" law can support. The non-vacuity guard
    /// therefore requires that <see cref="BoardThreat.PlayerLife"/> itself moves, so the law is
    /// not vacuous merely because nothing at all changed.
    /// </summary>
    [Test]
    public void R4_LoweringMyOwnLifeNeverLowersAnyThreatExpectation()
    {
        var catalog = Catalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var conditional = ConditionallyPlayable(catalog);
        var positions = RealPositions(catalog, categories, tags);
        Assert.That(positions, Is.Not.Empty, "the sweep must reach real ACTION positions or this is vacuous");

        var samples = 0;
        var kindComparisons = 0;
        var lifeMoved = 0;
        var strictChanges = 0;
        var belowFloor = 0;
        var decreases = new List<string>();
        var noLife = new List<string>();
        var spreads = new List<string>();

        foreach (var position in positions)
        {
            var life = position.Snapshot.Players[position.Actor].Life;
            if (!life.HasValue)
            {
                noLife.Add(position.Label + ": the projection publishes no life for the viewer");
                continue;
            }

            var before = Estimate(position, position.Snapshot, position.OpponentPool, conditional);
            samples++;

            foreach (var target in new[] { Math.Max(1, life.Value - 5), 1 })
            {
                if (target >= life.Value) continue;

                var lowered = WithViewerLife(position.Snapshot, position.Actor, target);
                var after = Estimate(position, lowered, position.OpponentPool, conditional);

                var lifeBefore = before.BoardThreat.PlayerLife;
                var lifeAfter = after.BoardThreat.PlayerLife;
                if (lifeBefore.HasValue && lifeAfter.HasValue && lifeAfter.Value < lifeBefore.Value) lifeMoved++;

                var lines = new List<string>();
                foreach (ThreatKind kind in Enum.GetValues(typeof(ThreatKind)))
                {
                    var b = Of(before, kind);
                    if (double.IsNaN(b.InHand) || double.IsNaN(b.Playable)) continue;

                    var a = Of(after, kind);
                    if (double.IsNaN(a.InHand) || double.IsNaN(a.Playable))
                    {
                        belowFloor++;
                        decreases.Add(position.Label + " life " + life.Value + "->" + target + " " + kind
                            + ": no longer reported after life dropped (" + Format(b.InHand) + "->absent)");
                        continue;
                    }

                    kindComparisons++;
                    if (Math.Abs(a.InHand - b.InHand) > 1e-12 || Math.Abs(a.Playable - b.Playable) > 1e-12)
                    {
                        strictChanges++;
                        lines.Add("  " + position.Label + " life " + life.Value + "->" + target + " " + kind
                            + " in_hand " + Delta(b.InHand, a.InHand)
                            + "; playable " + Delta(b.Playable, a.Playable));
                    }

                    if (a.InHand < b.InHand - 1e-12)
                    {
                        decreases.Add(position.Label + " life " + life.Value + "->" + target + " " + kind
                            + " expected_in_hand FELL " + Delta(b.InHand, a.InHand));
                    }

                    if (a.Playable < b.Playable - 1e-12)
                    {
                        decreases.Add(position.Label + " life " + life.Value + "->" + target + " " + kind
                            + " playable_this_turn FELL " + Delta(b.Playable, a.Playable));
                    }
                }

                spreads.Add(position.Label + " life " + life.Value + "->" + target
                    + ": boardThreat.PlayerLife " + (lifeBefore?.ToString() ?? "<null>") + "->"
                    + (lifeAfter?.ToString() ?? "<null>") + ", kind readings changed=" + lines.Count);
                foreach (var line in lines.Take(4)) spreads.Add(line);
            }
        }

        TestContext.Out.WriteLine("R4 my-life-drops: {0} sampled positions", samples);
        WriteCapped(spreads, 24);
        foreach (var line in noLife) TestContext.Out.WriteLine("  skipped: " + line);
        TestContext.Out.WriteLine("R4 LIMITATION: the estimator does not read the viewer's own LIFE total"
            + " anywhere except as BoardThreat.PlayerLife passthrough — it is not a term in"
            + " ExpectedCardsInHand or ExpectedPlayableThisTurn. Any conclusion that depends on my life"
            + " total is out of its scope, and this PASS is NOT evidence that life is handled.");
        TestContext.Out.WriteLine("R4 SUMMARY: samples={0} kindComparisons={1} lifeReadingsThatMoved={2}"
            + " strictKindChanges={3} decreases={4} vanishedKinds={5}",
            samples, kindComparisons, lifeMoved, strictChanges, decreases.Count, belowFloor);

        Assert.Multiple(() =>
        {
            Assert.That(samples, Is.GreaterThan(0), "the sweep must sample at least one position with a published life");
            Assert.That(kindComparisons, Is.GreaterThan(0), "the sweep must compare at least one threat kind");
            Assert.That(
                lifeMoved,
                Is.GreaterThan(0),
                "NON-VACUITY: the life reduction must actually reach the report (BoardThreat.PlayerLife must"
                + " move), or this law is asserted over an input that never changed");
            Assert.That(decreases, Is.Empty,
                "lowering my own life must never LOWER a threat expectation (" + decreases.Count + "):\n  "
                + string.Join("\n  ", decreases.Take(8)));
        });
    }

    /// <summary>
    /// A copy of the projection with one side's LIFE changed and every other field identical.
    /// Only life is touched, so any movement in the reading is attributable to it.
    /// </summary>
    private static RuntimeSnapshotEnvelope WithViewerLife(RuntimeSnapshotEnvelope source, int playerIndex, int life)
    {
        var players = new List<RuntimePlayerSnapshot>(source.Players.Count);
        for (var index = 0; index < source.Players.Count; index++)
        {
            var player = source.Players[index];
            if (index != playerIndex)
            {
                players.Add(player);
                continue;
            }

            players.Add(new RuntimePlayerSnapshot
            {
                PlayerId = player.PlayerId,
                Life = life,
                DeckCount = player.DeckCount,
                HandCount = player.HandCount,
                FieldCount = player.FieldCount,
                GraveyardCount = player.GraveyardCount,
                AmbushCount = player.AmbushCount,
                CycleWinCount = player.CycleWinCount,
                RootStacks = player.RootStacks,
                RampantStacks = player.RampantStacks,
                PullCount = player.PullCount,
                CommitQueueCount = player.CommitQueueCount,
                CloudStackCount = player.CloudStackCount,
                PunishDeltaThisTurn = player.PunishDeltaThisTurn,
                PunishDrawnThisTurn = player.PunishDrawnThisTurn,
                TotalDiscarded = player.TotalDiscarded,
                NoDamageTurns = player.NoDamageTurns,
                DamagedThisCycle = player.DamagedThisCycle,
                PunishToSelfDiscardThisTurn = player.PunishToSelfDiscardThisTurn,
                ProtectedThisTurn = player.ProtectedThisTurn,
                EffectsNegatedThisTurn = player.EffectsNegatedThisTurn,
                Hand = player.Hand,
                Ambush = player.Ambush,
                Field = player.Field,
                LeaderZone = player.LeaderZone,
                Graveyard = player.Graveyard,
                CommitQueue = player.CommitQueue,
                CloudStack = player.CloudStack,
            });
        }

        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = source.ContractVersion,
            MatchId = source.MatchId,
            SnapshotRevision = source.SnapshotRevision,
            Turn = source.Turn,
            Phase = source.Phase,
            CurrentPlayer = source.CurrentPlayer,
            ViewerPlayerId = source.ViewerPlayerId,
            WinnerPlayerIndex = source.WinnerPlayerIndex,
            Players = players,
            Castle = source.Castle,
            PendingPrompt = source.PendingPrompt,
            LegalActions = source.LegalActions,
        };
    }

    // ================================================================ R6

    /// <summary>
    /// R6 — LETHAL BLINDNESS. A CONTROL, not a claim, and it is allowed to come out either way.
    ///
    /// WHY THIS EXISTS: a grep of the whole AI layer for a life total
    /// (<c>Life</c> / <c>PlayerLife</c> / <c>lethal</c>) finds only DATA PLUMBING —
    /// <see cref="BoardThreat.PlayerLife"/> (a stored field, <c>ThreatEstimator.cs:365/379</c>,
    /// filled at <c>:1363</c>) and the <c>ActionFeatures.Lifecycle</c> weight entries — and no
    /// decision path that BRANCHES on one. If that reading is right, then the shipped claim
    /// "an incoming lethal is not ignored" cannot be doing what its name suggests:
    /// <c>AiLethalAndDefenceTests.AnIncomingLethalIsNotIgnored</c> counts ANY
    /// PLAY_CARD / COMMIT / PULL / ATTACK as "answering" a 40-attack threat, which almost any
    /// action satisfies, and its only assertion is <c>probed &gt; 0</c>.
    ///
    /// THE MEASUREMENT: one real driven position is scored twice by the policy — once with the
    /// viewer's own <c>Life</c> at 20 and once at 1, with every other byte of the snapshot
    /// identical (same seed, board, hand, and legal actions). Both the TOP CHOICE and the FULL
    /// RANKED ORDER are compared, so a tie at the top cannot hide a difference further down.
    ///
    /// NO DIRECTION IS ASSERTED. What is asserted is the observed invariance or change WITH its
    /// counts, plus the non-vacuity conditions that make "unchanged" mean something: positions
    /// must actually have been probed and the advertised list must have had more than one element,
    /// or "the order is unchanged" would be the trivial result of a one-action list. If the choice
    /// turns out to be life-independent, the test additionally requires that at least one probed
    /// position was REALLY lethal (opponent board attack &gt;= the viewer's remaining life), so the
    /// blindness is demonstrated where a human would clearly act.
    /// </summary>
    [Test]
    public void R6_LethalBlindnessControl_DoesTheChoiceRespondToMyOwnLife()
    {
        var catalog = Catalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var positions = RealPositions(catalog, categories, tags);
        Assert.That(positions, Is.Not.Empty, "the sweep must reach real ACTION positions or this is vacuous");

        var policy = new AdvertisedActionPolicy();
        var probed = 0;
        var multiActionOrders = 0;
        var choiceChanged = 0;
        var orderingChanged = 0;
        var lethalPositions = 0;
        var lethalLines = new List<string>();
        var changes = new List<string>();
        var nondeterministic = new List<string>();
        var lines = new List<string>();

        foreach (var position in positions)
        {
            var snapshot = position.Snapshot;
            var actor = position.Actor;
            var life = snapshot.Players[actor].Life;
            if (!life.HasValue || life.Value <= 1) continue;
            if (!string.Equals(snapshot.Phase, AdvertisedActionPolicy.ActionPhase, StringComparison.Ordinal)) continue;
            if (snapshot.LegalActions.Count == 0) continue;

            var atFullLife = WithViewerLife(snapshot, actor, life.Value);
            var atOneLife = WithViewerLife(snapshot, actor, 1);

            if (!policy.TryChoose(atFullLife, actor, false, out var chosenFull) || chosenFull is null) continue;
            if (!policy.TryChoose(atOneLife, actor, false, out var chosenOne) || chosenOne is null) continue;

            // DETERMINISM CONTROL: the policy must be a pure function of its inputs, or a difference
            // between the two calls could be noise rather than the life change.
            if (!policy.TryChoose(atFullLife, actor, false, out var repeat) || repeat is null
                || !string.Equals(repeat.ActionId, chosenFull.ActionId, StringComparison.Ordinal))
            {
                nondeterministic.Add(position.Label + ": the policy is NOT deterministic on identical input, so no"
                    + " life-sensitivity claim can be measured here");
                continue;
            }

            var orderFull = policy.OrderAdvertisedActions(atFullLife, atFullLife.LegalActions);
            var orderOne = policy.OrderAdvertisedActions(atOneLife, atOneLife.LegalActions);

            probed++;
            if (orderFull.Count > 1) multiActionOrders++;

            var sameChoice = string.Equals(chosenFull.ActionId, chosenOne.ActionId, StringComparison.Ordinal);
            var sameOrder = Ordering(orderFull).SequenceEqual(Ordering(orderOne), StringComparer.Ordinal);
            if (!sameChoice) choiceChanged++;
            if (!sameOrder) orderingChanged++;

            // Was this position genuinely lethal against the FULL-life reading of the viewer? The
            // opponent's board attack is exact public information, so this needs no estimator.
            var incoming = 0;
            var opponentIndex = 1 - actor;
            var opponentField = snapshot.Players[opponentIndex].Field;
            if (opponentField is not null)
            {
                foreach (var card in opponentField)
                {
                    if (card is null) continue;
                    incoming += card.CurrentAttack ?? 0;
                }
            }

            var isLethal = incoming >= life.Value;
            if (isLethal)
            {
                lethalPositions++;
                lethalLines.Add(position.Label + ": opponent board attack=" + incoming
                    + " >= my life=" + life.Value + " (LETHAL); choice at life " + life.Value + " = "
                    + chosenFull.Type + "/" + (chosenFull.CardId ?? chosenFull.ActionId)
                    + ", at life 1 = " + chosenOne.Type + "/" + (chosenOne.CardId ?? chosenOne.ActionId)
                    + ", identical=" + sameChoice);
            }

            if (!sameChoice)
            {
                changes.Add(position.Label + " life " + life.Value + " -> 1 CHANGED the choice: "
                    + chosenFull.Type + "/" + (chosenFull.CardId ?? chosenFull.ActionId) + " -> "
                    + chosenOne.Type + "/" + (chosenOne.CardId ?? chosenOne.ActionId));
            }
            else if (!sameOrder)
            {
                changes.Add(position.Label + " life " + life.Value + " -> 1 kept the top choice but CHANGED the order: ["
                    + string.Join(", ", Ordering(orderFull)) + "] -> [" + string.Join(", ", Ordering(orderOne)) + "]");
            }

            if (lines.Count < 18)
            {
                lines.Add(position.Label + " life=" + life.Value + " actions=" + orderFull.Count
                    + " lethal=" + isLethal + " (incoming=" + incoming + ")");
                lines.Add("   at life " + life.Value + ": [" + string.Join(", ", Ordering(orderFull)) + "]");
                lines.Add("   at life 1: [" + string.Join(", ", Ordering(orderOne)) + "]");
                lines.Add("   top choice identical=" + sameChoice + "; full ordering identical=" + sameOrder);
            }
        }

        TestContext.Out.WriteLine("R6 lethal-blindness CONTROL: does the policy's choice depend on my own life?");
        WriteCapped(lines, 24);
        TestContext.Out.WriteLine("R6 LETHAL POSITIONS NOMINATED (opponent board attack >= my life):" + lethalPositions);
        WriteCapped(lethalLines, 8);
        foreach (var line in changes.Take(6)) TestContext.Out.WriteLine("  changed: " + line);
        TestContext.Out.WriteLine("R6 SUMMARY: positionsProbed={0} ordersWithMoreThanOneAction={1} choiceChanged={2}"
            + " orderingChanged={3} lethalPositions={4}",
            probed, multiActionOrders, choiceChanged, orderingChanged, lethalPositions);

        if (probed > 0 && choiceChanged == 0 && orderingChanged == 0)
        {
            TestContext.Out.WriteLine(
                "R6 CONCLUSION: the choice is life-independent: the AI cannot perceive being at 1 life"
                + " (positionsProbed={0}, topChoiceChanged=0, fullOrderingChanged=0, lethalPositions={1}).",
                probed, lethalPositions);
        }
        else if (probed > 0)
        {
            TestContext.Out.WriteLine(
                "R6 CONCLUSION: the choice responds to life (positionsProbed={0}, choiceChanged={1},"
                + " orderingChanged={2}, lethalPositions={3}).",
                probed, choiceChanged, orderingChanged, lethalPositions);
        }
        else
        {
            TestContext.Out.WriteLine("R6 CONCLUSION: no position could be probed, so nothing was measured.");
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                probed,
                Is.GreaterThan(0),
                "NON-VACUITY: the control must actually probe positions, or 'unchanged' means nothing");
            Assert.That(
                multiActionOrders,
                Is.GreaterThan(0),
                "NON-VACUITY: at least one probed position must advertise MORE THAN ONE action, or an"
                + " unchanged ordering is only the trivial result of a one-element list and could not"
                + " detect life-sensitivity even if it existed");
            Assert.That(
                changes,
                Is.Empty,
                "the policy must be deterministic on identical inputs, or a measured difference cannot be"
                + " attributed to the life change (" + changes.Count + "):\n  "
                + string.Join("\n  ", changes.Take(4)));

            // The invariance/change is asserted only as what was OBSERVED, with its counts. This is
            // the assertion that re-states the printed conclusion so a regression flips it.
            if (choiceChanged == 0 && orderingChanged == 0)
            {
                Assert.That(
                    lethalPositions,
                    Is.GreaterThan(0),
                    "the choice was measured to be life-independent, so at least one probed position must"
                    + " have been REALLY lethal (opponent board attack >= my remaining life); otherwise the"
                    + " blindness claim is asserted only where nothing was at stake");
            }
            else
            {
                Assert.That(
                    choiceChanged + orderingChanged,
                    Is.GreaterThan(0),
                    "the choice responded to life in at least one position; this is recorded, not judged");
            }
        });
    }

    /// <summary>
    /// One advertised action as a stable comparison key: type plus card, so two different
    /// snapshots' orderings can be compared element by element. ActionId is deliberately NOT used
    /// as the key, because two snapshots are different objects and their ids need not coincide.
    /// </summary>
    private static List<string> Ordering(IReadOnlyList<RuntimeLegalAction> actions)
    {
        var result = new List<string>(actions.Count);
        foreach (var action in actions)
        {
            if (action is null) continue;
            result.Add(action.Type + "/" + (string.IsNullOrEmpty(action.CardId) ? action.ActionId : action.CardId));
        }

        return result;
    }

    // ================================================================ R5

    /// <summary>
    /// R5 — WIN OBJECTIVE CLOSER.
    ///
    /// For every side with a measurable win condition, the threshold is moved CLOSER to the
    /// current progress value (and, as the control in the other direction, FURTHER from it),
    /// with everything else including the measured current value left identical. The reported
    /// remaining distance must move in the CORRECT direction:
    ///
    ///   closer  => Remaining must NOT INCREASE   (it must fall, unless it is already 0)
    ///   further => Remaining must NOT DECREASE
    ///   and the met/unmet verdict must never flip from met to unmet when the objective moves
    ///   closer.
    ///
    /// NON-VACUITY: the threshold move must actually move the reading for at least one side, so
    /// the law is not asserted over a quantity that is pinned at zero by an already-met
    /// objective.
    /// </summary>
    [Test]
    public void R5_MovingTheWinObjectiveCloserMovesTheDistanceTheRightWay()
    {
        var catalog = Catalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var positions = RealPositions(catalog, categories, tags);
        Assert.That(positions, Is.Not.Empty, "the sweep must reach real ACTION positions or this is vacuous");

        var samples = 0;
        var comparisons = 0;
        var closerMoved = 0;
        var closerSpread = 0;
        var met = 0;
        var wrongWay = new List<string>();
        var flipped = new List<string>();
        var unreachable = new List<string>();
        var reachablePositions = 0;
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var leaderNotManifested = 0;
        var leaderManifested = 0;
        var lines = new List<string>();

        foreach (var position in positions)
        {
            var conditions = ConditionsOf(position);
            var progress = ThreatEstimator.DeriveWinProgress(position.Snapshot, position.Actor, conditions);
            samples++;

            // WHY THE REST ARE UNREACHABLE, counted rather than asserted away. The evaluator can
            // only place a win condition for a seat whose LEADER CARD IS VISIBLE in its leader
            // zone, and a leader starts in its owner's deck and is manifested only when drawn
            // (EffectRuntime.Cards.cs, ManifestLeader; the same `rule.leader_gate` that refuses to
            // declare a win unless both leaders are manifested, EffectRuntime.EndPhase.cs).
            var visibleLeaders = 0;
            foreach (var player in position.Snapshot.Players)
            {
                if (LeaderIdOf(player) is not null) visibleLeaders++;
            }

            if (visibleLeaders == 0) leaderNotManifested++;
            else leaderManifested++;

            var measurableHere = 0;

            foreach (var entry in progress)
            {
                if (entry is null) continue;
                if (entry.Condition is null || !entry.Required.HasValue || !entry.Current.HasValue)
                {
                    var reason = entry.UnmeasurableReason ?? "no reason given";
                    var reasonKey = entry.Condition is null
                        ? (visibleLeaders == 0 ? "no leader manifested in the leader zone" : "leader visible but no objective data supplied")
                        : "objective present, counter unmeasurable: " + reason;
                    reasons.TryGetValue(reasonKey, out var running);
                    reasons[reasonKey] = running + 1;

                    if (unreachable.Count < 60)
                    {
                        unreachable.Add(position.Label + " p" + entry.PlayerIndex + " (leaders visible "
                            + visibleLeaders + "/2): " + (entry.Condition ?? "<no condition>") + " — " + reason);
                    }

                    continue;
                }

                var condition = entry.Condition;
                var current = entry.Current.Value;
                var required = entry.Required.Value;
                var isMet = entry.Remaining.HasValue && entry.Remaining.Value == 0;
                if (isMet) met++;
                measurableHere++;

                var closerTarget = isMet ? required - 1 : Math.Max(0, current);
                var furtherTarget = required + 1;

                var closer = ThreatEstimator.DeriveWinProgress(
                    position.Snapshot, position.Actor, WithTarget(conditions, condition, closerTarget));
                var further = ThreatEstimator.DeriveWinProgress(
                    position.Snapshot, position.Actor, WithTarget(conditions, condition, furtherTarget));

                var mineNow = Find(progress, entry.PlayerIndex);
                var mineCloser = Find(closer, entry.PlayerIndex);
                var mineFurther = Find(further, entry.PlayerIndex);

                comparisons++;

                var nowRemaining = mineNow?.Remaining;
                var closerRemaining = mineCloser?.Remaining;
                var furtherRemaining = mineFurther?.Remaining;

                lines.Add(position.Label + " p" + entry.PlayerIndex + " " + condition
                    + ": current=" + current + " target=" + required + " remaining=" + Show(nowRemaining)
                    + " | closer target=" + closerTarget + " remaining=" + Show(closerRemaining)
                    + " | further target=" + furtherTarget + " remaining=" + Show(furtherRemaining)
                    + " | met now=" + isMet + " closer=" + ShowMet(mineCloser));

                if (nowRemaining.HasValue && closerRemaining.HasValue && nowRemaining.Value != closerRemaining.Value)
                {
                    closerMoved++;
                    closerSpread += Math.Abs(nowRemaining.Value - closerRemaining.Value);
                }

                if (nowRemaining.HasValue && closerRemaining.HasValue && closerRemaining.Value > nowRemaining.Value)
                {
                    wrongWay.Add(position.Label + " " + condition + ": moving the target CLOSER (" + required + "->"
                        + closerTarget + ") raised Remaining " + nowRemaining.Value + "->" + closerRemaining.Value);
                }

                if (nowRemaining.HasValue && furtherRemaining.HasValue && furtherRemaining.Value < nowRemaining.Value)
                {
                    wrongWay.Add(position.Label + " " + condition + ": moving the target FURTHER (" + required + "->"
                        + furtherTarget + ") lowered Remaining " + nowRemaining.Value + "->" + furtherRemaining.Value);
                }

                if (isMet && mineCloser is not null && mineCloser.Remaining.HasValue && mineCloser.Remaining.Value > 0)
                {
                    flipped.Add(position.Label + " " + condition + ": met at target " + required + " but NOT met at the"
                        + " closer target " + closerTarget + " (remaining " + mineCloser.Remaining.Value + ")");
                }
            }

            if (measurableHere > 0) reachablePositions++;
        }

        TestContext.Out.WriteLine("R5 win-objective-closer: {0} real positions", samples);
        TestContext.Out.WriteLine("R5 REACHABILITY: positionsWithAnyManifestedLeader={0} positionsWithNoLeaderManifested={1}",
            leaderManifested, leaderNotManifested);
        foreach (var pair in reasons.OrderByDescending(pair => pair.Value))
        {
            TestContext.Out.WriteLine("  unmeasurable reason x{0}: {1}", pair.Value, pair.Key);
        }
        WriteCapped(lines, 30);
        WriteCapped(unreachable, 8);
        TestContext.Out.WriteLine("R5 SUMMARY: samples={0} comparisons={1} closerMovedTheReading={2}"
            + " closerRemainingSpread={3} alreadyMet={4} wrongDirection={5} metTransitionsToUnmet={6}"
            + " positionsWithMeasurableObjective={7}",
            samples, comparisons, closerMoved, closerSpread, met, wrongWay.Count, flipped.Count,
            reachablePositions);

        Assert.Multiple(() =>
        {
            Assert.That(samples, Is.GreaterThan(0), "the sweep must sample at least one real position");
            Assert.That(
                reachablePositions,
                Is.GreaterThan(0),
                "the sweep must reach at least one position with a MEASURABLE win objective, or the relation"
                + " could not be observed at all. Reachability: positionsWithAnyManifestedLeader="
                + leaderManifested + " positionsWithNoLeaderManifested=" + leaderNotManifested
                + "; a leader card starts in its owner's deck and is manifested only when drawn, so before"
                + " that moment no side's objective is knowable (see the reason counts above)");
            Assert.That(comparisons, Is.GreaterThan(0),
                "the sweep must find at least one MEASURABLE win condition (see the reason counts above),"
                + " so the relation could not be observed at all");
            Assert.That(
                closerMoved,
                Is.GreaterThan(0),
                "NON-VACUITY: moving the objective target closer must actually move Remaining for at least one"
                + " side, or 'never increases' is asserted over a quantity pinned by an already-met objective");
            Assert.That(wrongWay, Is.Empty,
                "moving the objective target must move the reported distance the RIGHT way (" + wrongWay.Count + "):\n  "
                + string.Join("\n  ", wrongWay.Take(6)));
            Assert.That(flipped, Is.Empty,
                "an objective that is met must not become unmet when the target moves CLOSER (" + flipped.Count + "):\n  "
                + string.Join("\n  ", flipped.Take(6)));
        });
    }

    private static WinProgress? Find(IReadOnlyList<WinProgress> progress, int playerIndex)
    {
        foreach (var entry in progress)
        {
            if (entry is not null && entry.PlayerIndex == playerIndex) return entry;
        }

        return null;
    }

    private static string Show(int? value) => value.HasValue ? value.Value.ToString() : "?";

    private static string ShowMet(WinProgress? progress)
        => progress is null || !progress.Remaining.HasValue ? "?" : (progress.Remaining.Value == 0).ToString();

    /// <summary>
    /// Leader win conditions for this position, keyed by leader card id, read from the card
    /// catalog: a leader's condition is printed on a card that sits face up in the leader zone.
    /// </summary>
    private static Dictionary<string, LeaderWinCondition> ConditionsOf(Position position)
    {
        var conditions = new Dictionary<string, LeaderWinCondition>(StringComparer.Ordinal);
        for (var playerIndex = 0; playerIndex < position.Snapshot.Players.Count; playerIndex++)
        {
            var leaderId = LeaderIdOf(position.Snapshot.Players[playerIndex]);
            if (leaderId is null) continue;
            if (!position.Catalog.Cards.TryGetValue(leaderId, out var definition) || definition is null) continue;

            var condition = definition.LeaderWinCondition;
            if (string.IsNullOrEmpty(condition)) continue;

            conditions[leaderId] = new LeaderWinCondition(leaderId, condition, definition.LeaderWinParam);
        }

        return conditions;
    }

    private static string? LeaderIdOf(RuntimePlayerSnapshot player)
    {
        if (player.LeaderZone is null) return null;
        foreach (var card in player.LeaderZone)
        {
            if (card is null || string.IsNullOrEmpty(card.CardId)) continue;
            return card.CardId;
        }

        return null;
    }

    /// <summary>
    /// The same condition map with one condition family's threshold replaced, everything else
    /// left alone — so the only input that moved is the target.
    /// </summary>
    private static Dictionary<string, LeaderWinCondition> WithTarget(
        Dictionary<string, LeaderWinCondition> conditions, string conditionFamily, int required)
    {
        var updated = new Dictionary<string, LeaderWinCondition>(StringComparer.Ordinal);
        foreach (var pair in conditions)
        {
            updated[pair.Key] = string.Equals(pair.Value.Condition, conditionFamily, StringComparison.Ordinal)
                ? new LeaderWinCondition(pair.Value.CardId, pair.Value.Condition, required)
                : pair.Value;
        }

        return updated;
    }
}

}
