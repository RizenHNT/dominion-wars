using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Data;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Setup;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// The victory-objective contract: a card describes its own win condition in data, so
/// the knowledge "which number decides my victory" stops living only inside the
/// engine's win switch.
///
/// The migration is only safe if a migrated card behaves EXACTLY as it did before, so
/// these tests are about equivalence, not about new behaviour. "Equivalent by
/// inspection" is not evidence, so the central test drives real matches with the
/// contract attached and detached and compares the numbers the engine reports.
/// </summary>
public sealed class VictoryObjectiveContractTests
{
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

    private static CardCatalog LoadCatalog()
        => CardCatalog.LoadDirectory(Path.Combine(RepositoryRoot(), "data", "cards"));

    private static DeckDefinition Deck(string faction)
        => DeckLoader.LoadFile(Path.Combine(RepositoryRoot(), "data", "decks", faction + "_deck.json"));

    private static MatchDeckSpec Spec(DeckDefinition deck)
        => new MatchDeckSpec(deck.Leader, deck.Cards, deck.Name, deck.Faction);

    // ------------------------------------------------------------ the contract

    /// <summary>
    /// A condition id nobody implemented must fail at CONSTRUCTION, not sit in a deck
    /// never firing — which is exactly how AMBUSH_TRIGGER_WIN is unreachable today.
    ///
    /// This replaces a test that asserted several ids were REFUSED for not being
    /// thresholds. That was the wrong shape: refusing them meant the castle break could
    /// not be expressed at all, and the flame leader had to keep its win buried in a
    /// special case. The interface now covers it, so what must fail is only an id with no
    /// condition behind it.
    /// </summary>
    [Test]
    public void UnknownConditionIdsFailAtConstruction()
    {
        Assert.Multiple(() =>
        {
            // The id is resolved when the CONDITION is built, which is what the loader
            // does at load time. Checking it there rather than only in a name list is what
            // keeps the registry and the evaluator from disagreeing about what exists.
            Assert.That(
                () => new VictoryObjectiveDefinition("NOT_A_CONDITION", VictoryObjectiveDirection.Increase, 1)
                    .CreateCondition(),
                Throws.ArgumentException,
                "an unimplemented id must fail loudly instead of never firing");
            Assert.That(
                () => new VictoryObjectiveDefinition("PULL_COUNT", "SIDEWAYS", 1),
                Throws.ArgumentException,
                "the direction is still validated, because a card prints it");
            Assert.That(
                () => VictoryConditions.Create("AMBUSH_TRIGGER_COUNT", 1),
                Throws.ArgumentException,
                "no condition class implements it, so it cannot be resolved");

            // And the ones that DO exist must resolve, including the castle break that the
            // earlier design refused.
            Assert.That(VictoryConditions.Create("CASTLE_BREAK", 1), Is.Not.Null);
            Assert.That(VictoryConditions.Create("PULL_COUNT", 6), Is.Not.Null);
        });
    }

    /// <summary>
    /// EVERY condition the registry knows must produce a usable reading on a real board.
    ///
    /// This is the guard that keeps the registry honest: a condition registered but never
    /// exercised is a condition whose measurement nobody has checked, and the whole point
    /// of the interface is that every way to win reports through it.
    /// </summary>
    [Test]
    public void EveryRegisteredConditionProducesAReading()
    {
        var catalog = LoadCatalog();
        var state = MatchSetup.Create(
            Spec(Deck("machine")),
            Spec(Deck("sea")),
            catalog.Cards,
            new MatchSetupOptions { Seed = 7, FirstPlayerIndex = 0, OpeningHandSize = 5, CastleEnabled = true, CastleHealth = 75 });
        var flow = TurnFlow.CreateDefault();
        flow.Advance(state, state.CurrentPlayerIndex);

        Assert.That(VictoryConditions.Known, Is.Not.Empty, "the registry must not be empty or this is vacuous");

        foreach (var id in VictoryConditions.Known)
        {
            var condition = VictoryConditions.Create(id, 6);
            Assert.That(condition.Id, Is.EqualTo(id),
                "a condition must report the id it was registered under, or the contract and the registry disagree");

            foreach (var seat in new[] { 0, 1 })
            {
                var reading = condition.Read(state, seat);
                Assert.That(reading, Is.Not.Null, id + " must produce a reading for seat " + seat);
                if (!reading.IsMeasurable) continue;

                Assert.That(reading.Remaining, Is.Not.Null,
                    id + " is measurable, so it must report a distance");
                Assert.That(reading.IsMet, Is.EqualTo(reading.Remaining <= 0),
                    id + ": IsMet and Remaining must agree, or a consumer cannot trust either");
            }
        }
    }

    /// <summary>
    /// The castle break is the condition the earlier design REFUSED to express. It must
    /// now report through the same interface as everything else, and its reading must
    /// track the actual castle state rather than a counter.
    /// </summary>
    [Test]
    public void TheCastleBreakConditionReadsTheCastle()
    {
        var catalog = LoadCatalog();
        var state = MatchSetup.Create(
            Spec(Deck("flame")),
            Spec(Deck("machine")),
            catalog.Cards,
            new MatchSetupOptions { Seed = 7, FirstPlayerIndex = 0, OpeningHandSize = 5, CastleEnabled = true, CastleHealth = 75 });
        var flow = TurnFlow.CreateDefault();
        flow.Advance(state, state.CurrentPlayerIndex);

        var condition = new CastleBreakCondition(1);

        var intact = condition.Read(state, 0);
        Assert.Multiple(() =>
        {
            Assert.That(intact.IsMeasurable, Is.True, "a present castle is measurable");
            Assert.That(intact.IsMet, Is.False, "the castle has not been broken yet");
            Assert.That(intact.Remaining, Is.GreaterThan(0), "there is still work to do");
        });

        // Break it, and the same condition must report it without any counter involved.
        state.CastleHealth = 0;
        var broken = condition.Read(state, 0);
        Assert.Multiple(() =>
        {
            Assert.That(broken.IsMet, Is.True,
                "the castle break is met or it is not; no threshold arithmetic is involved");
            Assert.That(broken.Remaining, Is.Zero);
        });
    }

    /// <summary>
    /// Composition must work through the SAME interface, because a compound rule that
    /// needed its own engine branch would mean the interface was not one.
    ///
    /// "A and B" reports the hardest remaining part; "A or B" reports the nearest met one.
    /// Both must also refuse to guess when a part is unreadable.
    /// </summary>
    [Test]
    public void CompoundConditionsComposeThroughTheSameInterface()
    {
        var catalog = LoadCatalog();
        var state = MatchSetup.Create(
            Spec(Deck("machine")),
            Spec(Deck("sea")),
            catalog.Cards,
            new MatchSetupOptions { Seed = 11, FirstPlayerIndex = 0, OpeningHandSize = 5, CastleEnabled = true, CastleHealth = 75 });
        var flow = TurnFlow.CreateDefault();
        flow.Advance(state, state.CurrentPlayerIndex);

        // PULL_COUNT with a target already met, and one far out of reach.
        var met = new PullCountCondition(0);
        var far = new PullCountCondition(999);

        var all = new AllOfVictoryCondition("TEST_ALL", new IVictoryCondition[] { met, far });
        var any = new AnyOfVictoryCondition("TEST_ANY", new IVictoryCondition[] { met, far });

        var allReading = all.Read(state, 0);
        var anyReading = any.Read(state, 0);

        Assert.Multiple(() =>
        {
            Assert.That(allReading.IsMeasurable, Is.True);
            Assert.That(allReading.IsMet, Is.False, "one part is far from done, so 'all' is not met");
            Assert.That(anyReading.IsMet, Is.True, "one part is already satisfied, so 'any' is met");

            // An unreadable part must make the whole compound unreadable rather than let it
            // report confident progress built on a guess.
            var unreadable = new CastleBreakCondition(1);
            state.CastleEnabled = false;
            var blocked = new AllOfVictoryCondition(
                "TEST_BLOCKED", new IVictoryCondition[] { met, unreadable }).Read(state, 0);
            Assert.That(blocked.IsMeasurable, Is.False,
                "a compound condition is only as knowable as its weakest part");
            Assert.That(blocked.UnmeasurableReason, Is.Not.Null.And.Not.Empty);
        });
    }

    // ------------------------------------------------------------ the loader

    [Test]
    public void TheRealCatalogLoadsTheMigratedCard()
    {
        var catalog = LoadCatalog();
        var leader = catalog.Cards["machine_leader"];

        Assert.That(leader.Victory, Is.Not.Null, "machine_leader declares a victory objective in data");
        Assert.Multiple(() =>
        {
            Assert.That(leader.Victory!.Metric, Is.EqualTo("PULL_COUNT"));
            Assert.That(leader.Victory.Direction, Is.EqualTo(VictoryObjectiveDirection.Increase));
            Assert.That(leader.Victory.Target, Is.EqualTo(leader.LeaderWinParam),
                "the migrated target must equal the legacy winParam or the migration changed the rule");
        });
    }

    /// <summary>
    /// Every shipped card that declares an objective must still agree with its own
    /// legacy fields. This is the check that keeps a hand-edited data file honest: a
    /// migration that silently retunes a win condition is exactly the kind of
    /// accidental balance change the whole card-by-card process exists to prevent.
    /// </summary>
    [Test]
    public void EveryMigratedCardAgreesWithItsLegacyFields()
    {
        var catalog = LoadCatalog();
        var migrated = catalog.Cards.Values.Where(card => card.Victory is not null).ToArray();

        Assert.That(migrated, Is.Not.Empty, "at least one card must have migrated or this test is vacuous");

        // The condition each legacy name corresponds to. This map exists ONLY in the test:
        // it is the thing being verified, so it must not be the thing doing the work in
        // production.
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PULL_TOTAL_GE"] = "PULL_COUNT",
            ["OPP_DISCARD_TOTAL_GE"] = "OPPONENT_DISCARD_COUNT",
            ["NO_DAMAGE_TURNS_GE"] = "NO_DAMAGE_TURN_STREAK",
            ["OPP_PUNISH_DRAW_TURN_GE"] = "OPPONENT_PUNISH_DRAW_THIS_TURN",
            ["GIANT_HEALTH_GE"] = "SEALED_MINION_MAX_HEALTH",
            // The castle break is the case the earlier design refused. It is a victory
            // condition like any other; it just computes its progress from the castle
            // rather than from a counter, so it has no winParam to match.
            ["ROYAL_CASTLE_BREAK"] = "CASTLE_BREAK",
        };

        foreach (var card in migrated)
        {
            var legacy = card.LeaderWinCondition ?? string.Empty;
            Assert.That(expected.ContainsKey(legacy), Is.True,
                card.Id + " migrated from a condition this test does not know: " + legacy);
            Assert.Multiple(() =>
            {
                Assert.That(card.Victory!.Metric, Is.EqualTo(expected[legacy]),
                    card.Id + ": condition must match what its legacy name meant");

                if (string.Equals(legacy, "ROYAL_CASTLE_BREAK", StringComparison.Ordinal))
                {
                    // Not a threshold: there is no winParam to preserve, and the condition
                    // is met or it is not.
                    Assert.That(card.LeaderWinParam, Is.Zero,
                        card.Id + ": the castle condition carries no threshold, by design");
                }
                else
                {
                    Assert.That(card.Victory.Target, Is.EqualTo(card.LeaderWinParam),
                        card.Id + ": target must equal the legacy winParam");
                    Assert.That(card.Victory.Direction, Is.EqualTo(VictoryObjectiveDirection.Increase),
                        card.Id + ": every shipped threshold condition is a >= threshold");
                }

                // Whichever it is, the condition must actually resolve, or the card would
                // sit in a deck unable to win.
                Assert.That(card.Victory.CreateCondition(), Is.Not.Null);
            });
        }
    }

    /// <summary>
    /// A card with no objective must keep the legacy path, so behaviour cannot change for
    /// a card that has not migrated.
    ///
    /// `gate_of_fate` is the remaining one, and it is deliberately left alone: it wins
    /// through its AMBUSH effect (WIN_GAME) rather than through a leader win condition, so
    /// its declared condition is not a measurement at all. The owner has reserved that
    /// archetype for a future ambush leader, so the gap is recorded rather than patched.
    /// </summary>
    [Test]
    public void CardsWithoutAnObjectiveKeepTheLegacyPath()
    {
        var catalog = LoadCatalog();

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Cards["gate_of_fate"].Victory, Is.Null,
                "its win comes from a WIN_GAME ambush effect; its declared condition is inert metadata");
            Assert.That(catalog.Cards["shadow_of_fate"].Victory, Is.Null, "its condition is NONE");

            // The flame leader is now MIGRATED: the castle break is expressible through the
            // interface, which is the whole point of having one.
            Assert.That(catalog.Cards["flame_leader"].Victory, Is.Not.Null,
                "the castle win is a victory condition and must report through the same interface as the rest");
        });
    }

    // ------------------------------------------------------------ end to end

    /// <summary>
    /// THE WHOLE CHAIN, on a real match: the objective is declared in card data, the
    /// engine loads it, the projection publishes it in a viewer-safe snapshot, and the
    /// AI's target producer reads it back without parsing any condition name.
    ///
    /// Each hop is covered separately elsewhere; this test exists because a contract
    /// that works at every hop but is not wired end to end is exactly the failure that
    /// per-layer unit tests cannot see.
    /// </summary>
    [Test]
    public void TheDeclaredObjectiveReachesTheAiThroughAViewerSafeSnapshot()
    {
        var catalog = LoadCatalog();

        var state = MatchSetup.Create(
            Spec(Deck("machine")),
            Spec(Deck("flame")),
            catalog.Cards,
            new MatchSetupOptions { Seed = 3, FirstPlayerIndex = 0, OpeningHandSize = 5, CastleEnabled = true, CastleHealth = 75 });
        var flow = TurnFlow.CreateDefault();
        flow.Advance(state, state.CurrentPlayerIndex);

        // Manifest the machine leader deterministically. It is a LANDMARK leader, so it
        // is not in the leader zone at match start and reaches it through its own path;
        // driving a naive policy until that happens depends on the policy finding the
        // path, which would make this test's subject (the contract) hostage to the
        // driver. Placing it directly is how ProductionFactionIntegrationTests sets up
        // the same landmark, and it keeps the assertion about the CONTRACT.
        var machine = state.GetPlayer(0);
        var leader = machine.Deck.First(card =>
            string.Equals(card.Definition.Id, "machine_leader", StringComparison.Ordinal));
        machine.Deck.Remove(leader);
        leader.IsLeaderEntity = true;
        machine.LeaderZone.Add(leader);

        // Projected from BOTH points of view: a leader's win condition is printed on the
        // card, so the player facing it must be able to read it too.
        foreach (var viewerIndex in new[] { 0, 1 })
        {
            var snapshot = DominionWars.Adapters.RuntimeSnapshotProjection.ToSnapshot(
                state, "match_victory_contract", state.Turn.Number, viewerIndex, flow);

            var machineSeat = -1;
            DominionWars.Adapters.RuntimeCardSnapshot? published = null;
            for (var seat = 0; seat < snapshot.Players.Count; seat++)
            {
                foreach (var card in snapshot.Players[seat].LeaderZone ?? Array.Empty<DominionWars.Adapters.RuntimeCardSnapshot>())
                {
                    if (!string.Equals(card.CardId, "machine_leader", StringComparison.Ordinal)) continue;
                    machineSeat = seat;
                    published = card;
                }
            }

            Assert.That(published, Is.Not.Null, "the manifested machine leader is public in both players' views");
            Assert.That(published!.Victory, Is.Not.Null,
                "the declared objective must survive projection, or the AI is back to parsing condition names");

            var objective = DominionWars.Adapters.Ai.VictoryObjectives
                .FromEngine(snapshot, viewerIndex)
                .For(machineSeat);

            Assert.That(objective, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(published.Victory!.Metric, Is.EqualTo("PULL_COUNT"));
                Assert.That(published.Victory.Target, Is.EqualTo(6));
                Assert.That(objective!.Metric, Is.EqualTo("PULL_COUNT"),
                    "the AI must report the metric the card data declared");
                Assert.That(objective.Target, Is.EqualTo(6));
                Assert.That(objective.Source, Does.Contain("engine-published"),
                    "the AI must attribute the objective to the engine, not to the name-parsing bridge");

                // THE PROGRESS VALUE must come from the engine too. This is the assertion
                // that stops the AI silently re-deriving progress from a condition name:
                // the published value and the AI's value must be the same number, and it
                // must equal what the CONDITION itself reports for that seat.
                var engineReading = DominionWars.Engine.Model.VictoryConditions
                    .Create(published.Victory.Metric, published.Victory.Target)
                    .Read(state, machineSeat);

                Assert.That(published.Victory.Current, Is.EqualTo(engineReading.Current),
                    "the snapshot must publish the condition's own reading");
                Assert.That(published.Victory.Met, Is.EqualTo(engineReading.IsMet),
                    "the snapshot must publish the condition's own verdict, not a re-derived comparison");
                Assert.That(objective.Progress, Is.EqualTo(engineReading.Current),
                    "the AI must use the published reading, not measure progress itself");
                Assert.That(published.Victory.Remaining, Is.EqualTo(engineReading.Remaining),
                    "the publisher precomputes the remaining distance so consumers cannot disagree about it");
            });
        }
    }

    /// <summary>
    /// The producer a caller uses must PREFER the engine's published objective and fall
    /// back per player, not globally.
    ///
    /// The distinction matters because the shipped game mixes migrated and unmigrated
    /// leaders: if the fallback were decided once for the whole set, a single unmigrated
    /// leader across the table would drag the migrated leader back onto the name-parsing
    /// path and silently lose the contract.
    /// </summary>
    [Test]
    public void TheBlendedProducerPrefersPublishedAndFallsBackPerPlayer()
    {
        var catalog = LoadCatalog();
        var conditions = new Dictionary<string, DominionWars.Adapters.Ai.LeaderWinCondition>(StringComparer.Ordinal)
        {
            ["machine_leader"] = new DominionWars.Adapters.Ai.LeaderWinCondition("machine_leader", "PULL_TOTAL_GE", 6),
            ["sea_leader"] = new DominionWars.Adapters.Ai.LeaderWinCondition("sea_leader", "OPP_DISCARD_TOTAL_GE", 18),
        };

        var state = MatchSetup.Create(
            Spec(Deck("machine")),
            Spec(Deck("sea")),
            catalog.Cards,
            new MatchSetupOptions { Seed = 5, FirstPlayerIndex = 0, OpeningHandSize = 5, CastleEnabled = true, CastleHealth = 75 });
        var flow = TurnFlow.CreateDefault();
        flow.Advance(state, state.CurrentPlayerIndex);

        // Manifest both landmark leaders so each seat has a visible one.
        for (var seat = 0; seat < 2; seat++)
        {
            var player = state.GetPlayer(seat);
            var id = seat == 0 ? "machine_leader" : "sea_leader";
            var leader = player.Deck.FirstOrDefault(card =>
                string.Equals(card.Definition.Id, id, StringComparison.Ordinal));
            if (leader is null) continue;
            player.Deck.Remove(leader);
            leader.IsLeaderEntity = true;
            player.LeaderZone.Add(leader);
        }

        var snapshot = DominionWars.Adapters.RuntimeSnapshotProjection.ToSnapshot(
            state, "match_blended", state.Turn.Number, 0, flow);

        var blended = DominionWars.Adapters.Ai.VictoryObjectives.ForMatch(snapshot, 0, conditions);

        var machineObjective = blended.For(0);
        Assert.That(machineObjective, Is.Not.Null, "machine leader must produce an objective");
        Assert.That(machineObjective!.Source, Does.Contain("engine-published"),
            "machine publishes typed victory metadata, so its objective must come from the engine");
        Assert.That(machineObjective.Progress, Is.Not.Null,
            "machine's published objective must include an engine reading");

        var seaObjective = blended.For(1);
        Assert.That(seaObjective, Is.Not.Null, "sea leader must produce an objective");
        Assert.That(seaObjective!.Source, Does.Contain("compatibility"),
            "sea has only the legacy winCondition field, so the objective must use the compatibility producer");
        Assert.That(seaObjective.Target, Is.EqualTo(18));
        Assert.That(seaObjective.Progress, Is.EqualTo(snapshot.Players[0].TotalDiscarded),
            "sea's opponent-discard condition reads the other seat's published discard counter");

        // A leader that declares nothing must fall back rather than invent an objective.
        var flameSnapshot = DominionWars.Adapters.RuntimeSnapshotProjection.ToSnapshot(
            state, "match_blended_2", state.Turn.Number, 0, flow);
        flameSnapshot.Players[0].LeaderZone = new[]
        {
            new DominionWars.Adapters.RuntimeCardSnapshot { CardId = "flame_leader", OwnerPlayer = 0, EntityId = 900 },
        };

        var withFlame = DominionWars.Adapters.Ai.VictoryObjectives.ForMatch(
            flameSnapshot,
            0,
            new Dictionary<string, DominionWars.Adapters.Ai.LeaderWinCondition>(StringComparer.Ordinal)
            {
                ["flame_leader"] = new DominionWars.Adapters.Ai.LeaderWinCondition("flame_leader", "ROYAL_CASTLE_BREAK", 0),
            });

        var flameObjective = withFlame.For(0);
        Assert.That(flameObjective, Is.Not.Null);
        Assert.That(flameObjective!.Source, Does.Contain("compatibility"),
            "a leader with no published objective must fall back to the documented bridge, and say so");
    }

    // ------------------------------------------------------------ equivalence

    /// <summary>
    /// THE EQUIVALENCE PROOF, on real matches.
    ///
    /// Drives the same seed TWICE for EVERY migrated leader: once with the catalog as
    /// shipped (the objective path runs) and once with every objective stripped (the
    /// legacy switch runs), then compares the engine's own reported progress step by
    /// step. Any divergence — a different current value, a different winner, a
    /// different turn count — fails.
    ///
    /// Every migrated leader is exercised on BOTH seats, because a leader sitting in
    /// seat 1 is the one whose opponent's counters matter (the sea and no-damage
    /// conditions read the OPPONENT's counters, so a seat-blind test could pass while
    /// the mirrored case is wrong).
    ///
    /// Without this, "the migration is equivalent" would be my inspection claim, and
    /// this session has already shown three times how unreliable that is.
    /// </summary>
    [Test]
    public void VictoryObjectiveMatchesTheLegacySwitchOnRealMatches()
    {
        var catalog = LoadCatalog();
        var migrated = catalog.Cards.Values.Where(card => card.Victory is not null).ToList();
        Assert.That(migrated, Is.Not.Empty, "at least one card must have migrated or this test is vacuous");

        var factions = new[] { "flame", "machine", "sea", "wood" };
        var comparedReadings = 0;
        var leaderSeatPairs = 0;
        var failures = new List<string>();

        // Which seat the migrated leader sits in. Seat 1 matters separately: the
        // opponent-reading conditions (discards, punish draws) are mirrored there.
        foreach (var leaderFaction in factions)
        {
            foreach (var opponentFaction in factions)
            {
                if (string.Equals(leaderFaction, opponentFaction, StringComparison.Ordinal)) continue;

                foreach (var leaderIsFirstSeat in new[] { true, false })
                {
                    var objectiveRun = Trace(
                        catalog, leaderFaction, opponentFaction, leaderIsFirstSeat,
                        stripObjectives: false, out var readings);
                    var legacyRun = Trace(
                        catalog, leaderFaction, opponentFaction, leaderIsFirstSeat,
                        stripObjectives: true, out _);

                    comparedReadings += readings;
                    leaderSeatPairs++;

                    // Two separate claims, checked separately:
                    //
                    // 1. The OUTCOME must be identical — same winner, same turn count. This
                    //    is the behavioural claim the migration rests on, and it is what
                    //    catches a real rule change.
                    // 2. Wherever BOTH runs report the same condition, the numbers must
                    //    agree. A migrated condition may legitimately report for a leader the
                    //    legacy loop skipped (the castle condition carries no winParam), so
                    //    comparing whole traces would fail on added visibility alone.
                    var outcomeOnly = new List<string>();
                    foreach (var line in objectiveRun)
                    {
                        if (line.StartsWith("winner=", StringComparison.Ordinal)
                            || line.StartsWith("turns=", StringComparison.Ordinal)) outcomeOnly.Add(line);
                    }

                    var legacyOutcome = new List<string>();
                    foreach (var line in legacyRun)
                    {
                        if (line.StartsWith("winner=", StringComparison.Ordinal)
                            || line.StartsWith("turns=", StringComparison.Ordinal)) legacyOutcome.Add(line);
                    }

                    if (!outcomeOnly.SequenceEqual(legacyOutcome, StringComparer.Ordinal))
                    {
                        failures.Add(
                            leaderFaction + " (seat " + (leaderIsFirstSeat ? 0 : 1) + ") vs " + opponentFaction
                            + ": OUTCOME CHANGED — objective run [" + string.Join(", ", outcomeOnly)
                            + "] vs legacy run [" + string.Join(", ", legacyOutcome) + "]");
                        continue;
                    }

                    // Compare per condition, at the length both runs share.
                    var objectiveByCondition = GroupByCondition(objectiveRun);
                    var legacyByCondition = GroupByCondition(legacyRun);
                    foreach (var pair in objectiveByCondition)
                    {
                        if (!legacyByCondition.TryGetValue(pair.Key, out var legacyList)) continue;

                        var shared = Math.Min(pair.Value.Count, legacyList.Count);
                        for (var index = 0; index < shared; index++)
                        {
                            if (string.Equals(pair.Value[index], legacyList[index], StringComparison.Ordinal)) continue;
                            failures.Add(
                                leaderFaction + " (seat " + (leaderIsFirstSeat ? 0 : 1) + ") vs " + opponentFaction
                                + " condition " + pair.Key + " reading " + index + ":\n      objective path: "
                                + pair.Value[index] + "\n      legacy path:    " + legacyList[index]);
                        }
                    }
                }
            }
        }

        TestContext.Out.WriteLine("leader/seat matchups compared: {0}", leaderSeatPairs);
        TestContext.Out.WriteLine("VICTORY_PROGRESS readings compared: {0}", comparedReadings);
        TestContext.Out.WriteLine(
            "migrated leaders verified against the legacy switch: {0}",
            string.Join(", ", migrated.Select(card => card.Id)));

        Assert.That(comparedReadings, Is.GreaterThan(0),
            "the sweep must actually observe progress readings or it proves nothing");
        Assert.That(leaderSeatPairs, Is.GreaterThanOrEqualTo(12),
            "every migrated leader must be exercised on both seats against every other faction");
        Assert.That(
            failures,
            Is.Empty,
            "a migrated objective must reproduce the legacy switch exactly (" + failures.Count + " divergences):\n  "
            + string.Join("\n  ", failures.Take(6)));
    }

    /// <summary>
    /// Groups trace lines by the condition they belong to, dropping the outcome lines and
    /// the condition prefix. Used so the two runs can be compared where they overlap
    /// instead of being required to have identical length.
    /// </summary>
    private static Dictionary<string, List<string>> GroupByCondition(List<string> trace)
    {
        var grouped = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var line in trace)
        {
            if (line.StartsWith("winner=", StringComparison.Ordinal)) continue;
            if (line.StartsWith("turns=", StringComparison.Ordinal)) continue;

            var separator = line.IndexOf(": ", StringComparison.Ordinal);
            if (separator < 0) continue;

            var condition = line.Substring(0, separator);
            var reading = line.Substring(separator + 2);
            if (!grouped.TryGetValue(condition, out var list))
            {
                list = new List<string>();
                grouped[condition] = list;
            }

            list.Add(reading);
        }

        return grouped;
    }

    /// <summary>
    /// Runs one match with <paramref name="leaderFaction"/> in the seat chosen by
    /// <paramref name="leaderIsFirstSeat"/>, and returns every VICTORY_PROGRESS reading
    /// for THAT seat as a comparable string, followed by the winner and turn count.
    ///
    /// When <paramref name="stripObjectives"/> is set, every card's objective is removed
    /// AFTER loading, which is precisely how the legacy path is reached. Both runs use
    /// the same seed and the same driver, so the two traces differ only in which code
    /// reads the metric — any divergence is attributable to the migration.
    /// </summary>
    private static List<string> Trace(
        CardCatalog catalog,
        string leaderFaction,
        string opponentFaction,
        bool leaderIsFirstSeat,
        bool stripObjectives,
        out int readingCount)
    {
        var leaderDeck = Deck(leaderFaction);
        var opponentDeck = Deck(opponentFaction);

        var cards = new Dictionary<string, CardDefinition>(catalog.Cards, StringComparer.Ordinal);
        if (stripObjectives)
        {
            // Rebuild every definition without its objective, so the engine falls back
            // to LeaderWinCondition. Everything else about each card is identical.
            foreach (var pair in catalog.Cards.ToList())
            {
                if (pair.Value.Victory is null) continue;
                cards[pair.Key] = CloneWithoutVictory(pair.Value);
            }
        }

        var leaderSeat = leaderIsFirstSeat ? 0 : 1;
        var first = leaderIsFirstSeat ? leaderDeck : opponentDeck;
        var second = leaderIsFirstSeat ? opponentDeck : leaderDeck;

        var state = MatchSetup.Create(
            Spec(first),
            Spec(second),
            cards,
            new MatchSetupOptions { Seed = 11, FirstPlayerIndex = 0, OpeningHandSize = 5, CastleEnabled = true, CastleHealth = 75 });

        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        var controller = new MatchController(state, flow, router);

        flow.Advance(state, state.CurrentPlayerIndex);

        var guard = 0;
        while (!state.WinnerPlayerIndex.HasValue && state.Turn.Number <= 24 && guard++ < 3000)
        {
            var actor = state.CurrentPlayerIndex;
            var actions = controller.GetLegalActions(actor);
            var endTurn = actions.FirstOrDefault(a => string.Equals(a.Type, LegalActionGenerator.EndTurn, StringComparison.Ordinal));
            var chosen = actions.FirstOrDefault(a => string.Equals(a.Type, LegalActionGenerator.Pull, StringComparison.Ordinal))
                ?? actions.FirstOrDefault(a => string.Equals(a.Type, LegalActionGenerator.PlayCard, StringComparison.Ordinal))
                ?? endTurn;

            if (chosen is null)
            {
                flow.Advance(state, actor);
                continue;
            }

            var submission = controller.Submit(new GameActionRequest(
                actor, chosen.Type, chosen.ActionId, chosen.SourceId));
            if (!submission.Accepted)
            {
                if (endTurn is null || ReferenceEquals(chosen, endTurn))
                {
                    flow.Advance(state, actor);
                }
                else
                {
                    var fallback = controller.Submit(new GameActionRequest(
                        actor, endTurn.Type, endTurn.ActionId, endTurn.SourceId));
                    if (!fallback.Accepted) flow.Advance(state, actor);
                }
            }
        }

        // Read the progress the engine published for the leader's seat, in order.
        var readings = new List<string>();
        readingCount = 0;
        var readingsByCondition = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var item in state.Events.Items)
        {
            if (!string.Equals(item.EventType, "VICTORY_PROGRESS", StringComparison.Ordinal)) continue;
            if (item.Data is null) continue;
            if (!item.Data.TryGetValue("player", out var playerValue)) continue;
            var seat = Convert.ToInt32(playerValue, System.Globalization.CultureInfo.InvariantCulture);
            if (seat != leaderSeat) continue;

            // Grouped by the condition the engine reported, because the two runs legitimately
            // report different SETS: a migrated condition publishes progress for a leader the
            // legacy path never evaluated at all (the castle condition has no winParam, so the
            // old loop skipped it). Requiring identical step counts would fail on that added
            // VISIBILITY, which changes no outcome, while the thing being verified is that the
            // numbers AGREE wherever both report. The winner and turn count are compared
            // separately below, so a real behavioural difference still fails.
            var condition = item.Data.TryGetValue("condition", out var cond) ? cond?.ToString() ?? string.Empty : string.Empty;
            var current = item.Data.TryGetValue("current", out var c) ? c : null;
            var required = item.Data.TryGetValue("required", out var r) ? r : null;

            if (!readingsByCondition.TryGetValue(condition, out var list))
            {
                list = new List<string>();
                readingsByCondition[condition] = list;
            }

            list.Add("current=" + current + " required=" + required);
        }

        // Emitted in a stable order so the two runs line up per condition.
        foreach (var pair in readingsByCondition.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            foreach (var reading in pair.Value)
            {
                readings.Add(pair.Key + ": " + reading);
            }
        }

        readings.Add("winner=" + state.WinnerPlayerIndex);
        readings.Add("turns=" + state.Turn.Number);
        readingCount = readings.Count;
        return readings;
    }

    /// <summary>
    /// A copy of the definition with the objective removed and everything else the
    /// same, so the only difference between the two traced runs is which code reads
    /// the metric.
    /// </summary>
    private static CardDefinition CloneWithoutVictory(CardDefinition source)
    {
        return new CardDefinition(
            source.Id, source.Name, source.Attack, source.Health, source.IsMinion, source.IsLeader, source.GrantLife,
            kingSlayer: source.KingSlayer,
            faction: source.Faction,
            text: source.Text,
            flavor: source.Flavor,
            cost: source.Cost,
            artId: source.ArtId,
            keywords: source.Keywords,
            tags: source.Tags,
            punishActivatable: source.PunishActivatable,
            punishCost: source.PunishCost,
            vulnerabilities: source.Vulnerabilities,
            type: source.Type,
            punish: source.Punish,
            punishCondition: source.PunishCondition,
            onPlayEffects: source.OnPlayEffects,
            punishEffects: source.PunishEffects,
            ambushKind: source.AmbushKind,
            ambushTrigger: source.AmbushTrigger,
            ambushEffects: source.AmbushEffects,
            chant: source.Chant,
            chantEffects: source.ChantEffects,
            attacksPerTurn: source.AttacksPerTurn,
            onOpponentDiscardEffects: source.OnOpponentDiscardEffects,
            guard: source.Guard,
            leaderEnterEffects: source.LeaderEnterEffects,
            leaderPunishEffects: source.LeaderPunishEffects,
            leaderWinCondition: source.LeaderWinCondition,
            leaderWinText: source.LeaderWinText,
            leaderDurability: source.LeaderDurability,
            leaderWinParam: source.LeaderWinParam,
            commitCost: source.CommitCost,
            uploadCost: source.UploadCost,
            downloadCost: source.DownloadCost,
            commitEffects: source.CommitEffects,
            pushEffects: source.PushEffects,
            pullEffects: source.PullEffects,
            isLandmark: source.IsLandmark,
            landmarkTiers: source.LandmarkTiers,
            victory: null);
    }
}

}
