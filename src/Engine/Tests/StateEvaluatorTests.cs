using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Adapters.Ai;
using DominionWars.Data;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// The State Evaluator v0: a pure, score-free fact sheet about one position.
///
/// Two invariants matter more than any individual fact and are pinned separately:
///  - FAIRNESS: the opponent's hidden hand contents cannot influence the vector,
///    only the public counts can.
///  - NO SCORE: the vector carries facts and confidences and nothing that could be
///    summed into a preference, checked reflectively so the constraint cannot rot.
/// </summary>
[TestFixture]
public sealed class StateEvaluatorTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data", "cards"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static CardCatalog LoadCatalog()
        => CardCatalog.LoadDirectory(Path.Combine(RepositoryRoot(), "data", "cards"));

    private static DeckDefinition Deck(string faction)
        => DeckLoader.LoadFile(Path.Combine(RepositoryRoot(), "data", "decks", faction + "_deck.json"));

    private static IReadOnlyDictionary<string, int> PoolOf(DeckDefinition deck)
        => new Dictionary<string, int>(deck.Cards, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, LeaderWinCondition> LeaderConditionsOf(CardCatalog catalog)
    {
        var map = new Dictionary<string, LeaderWinCondition>(StringComparer.Ordinal);
        foreach (var pair in catalog.Cards)
        {
            if (!pair.Value.IsLeader || string.IsNullOrEmpty(pair.Value.LeaderWinCondition)) continue;
            map[pair.Key] = new LeaderWinCondition(pair.Key, pair.Value.LeaderWinCondition, pair.Value.LeaderWinParam);
        }

        return map;
    }

    private static IReadOnlyList<ThreatEstimator.EffectSpecPair> EffectsOf(CardDefinition definition)
    {
        var pairs = new List<ThreatEstimator.EffectSpecPair>();
        void Take(IReadOnlyList<EffectSpec>? specs)
        {
            if (specs is null) return;
            foreach (var spec in specs)
            {
                if (spec is null || string.IsNullOrEmpty(spec.Action)) continue;
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

    private static IReadOnlyDictionary<string, IReadOnlyList<ThreatKind>> CategoriesOf(CardCatalog catalog)
    {
        var map = new Dictionary<string, IReadOnlyList<ThreatKind>>(StringComparer.Ordinal);
        foreach (var pair in catalog.Cards) map[pair.Key] = ThreatEstimator.CategoriesOf(EffectsOf(pair.Value));
        return map;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> TagsOf(CardCatalog catalog)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var pair in catalog.Cards)
        {
            map[pair.Key] = pair.Value.Tags is null ? Array.Empty<string>() : pair.Value.Tags.ToArray();
        }

        return map;
    }

    private static RuntimeCardSnapshot Card(string cardId, int owner, long entityId = 1)
        => new RuntimeCardSnapshot { EntityId = entityId, CardId = cardId, OwnerPlayer = owner };

    private static RuntimeSnapshotEnvelope Snapshot(
        int turn,
        int viewerIndex,
        int viewerHand,
        int opponentHand,
        int opponentDeck,
        IReadOnlyList<RuntimeCardSnapshot> opponentHandContents,
        int castleHealth = 75)
    {
        var players = new List<RuntimePlayerSnapshot>();
        for (var index = 0; index < 2; index++)
        {
            var isViewer = index == viewerIndex;
            players.Add(new RuntimePlayerSnapshot
            {
                PlayerId = "player_" + index,
                Life = 20,
                DeckCount = isViewer ? 20 : opponentDeck,
                HandCount = isViewer ? viewerHand : opponentHand,
                Field = isViewer ? Array.Empty<RuntimeCardSnapshot>() : Array.Empty<RuntimeCardSnapshot>(),
                LeaderZone = Array.Empty<RuntimeCardSnapshot>(),
                Graveyard = Array.Empty<RuntimeCardSnapshot>(),
                CommitQueue = Array.Empty<RuntimeCardSnapshot>(),
                CloudStack = Array.Empty<RuntimeCardSnapshot>(),
                // Populated on purpose: if the evaluator ever read it, the
                // fairness test below would see the two runs diverge.
                Hand = isViewer ? Array.Empty<RuntimeCardSnapshot>() : opponentHandContents,
            });
        }

        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = 1,
            MatchId = "match_state_eval",
            SnapshotRevision = turn,
            Turn = turn,
            Phase = "ACTION",
            CurrentPlayer = viewerIndex,
            ViewerPlayerId = "player_" + viewerIndex,
            Players = players,
            Castle = new RuntimeCastleSnapshot { Enabled = true, Health = castleHealth },
        };
    }

    // ------------------------------------------------------------- no scoring

    /// <summary>
    /// v0 must not be able to express a preference. Checked by reflection over the
    /// public surface, so a future edit that quietly adds a score, a weight or a
    /// ranking to the state vector fails the gate instead of shipping.
    /// </summary>
    [Test]
    public void StateVectorExposesNoScoringSurface()
    {
        string[] forbidden = { "weight", "rank", "prefer", "score", "utility", "goodness", "desirab" };
        var offenders = new List<string>();

        foreach (var type in new[] { typeof(StateVector), typeof(StateFact), typeof(StateEvaluator) })
        {
            foreach (var member in type.GetMembers())
            {
                var lowered = member.Name.ToLowerInvariant();
                foreach (var word in forbidden)
                {
                    if (lowered.Contains(word)) offenders.Add(type.Name + "." + member.Name);
                }
            }
        }

        Assert.That(
            offenders,
            Is.Empty,
            "v0 must report facts, never preferences: " + string.Join(", ", offenders.Distinct()));
    }

    // -------------------------------------------------------------- fairness

    /// <summary>
    /// The opponent's hidden hand contents must not change the vector. Identical
    /// public counts with contradictory hidden hands must give identical facts.
    /// </summary>
    [Test]
    public void OpponentHiddenHandCannotChangeTheVector()
    {
        var catalog = LoadCatalog();
        var conditions = LeaderConditionsOf(catalog);
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var pool = PoolOf(Deck("machine"));

        var handA = new[] { Card("machine_factory", 1, 11), Card("machine_emp", 1, 12), Card("machine_cannon", 1, 13) };
        var handB = new[] { Card("machine_null", 1, 21), Card("machine_trap", 1, 22), Card("machine_repair", 1, 23) };

        var first = StateEvaluator.Evaluate(
            Snapshot(6, 0, 5, 3, 15, handA), 0, conditions, pool, categories, tags);
        var second = StateEvaluator.Evaluate(
            Snapshot(6, 0, 5, 3, 15, handB), 0, conditions, pool, categories, tags);

        Assert.That(second.Facts.Count, Is.EqualTo(first.Facts.Count));
        for (var index = 0; index < first.Facts.Count; index++)
        {
            var a = first.Facts[index];
            var b = second.Facts[index];
            Assert.Multiple(() =>
            {
                Assert.That(b.Name, Is.EqualTo(a.Name), "the fact list is stable");
                Assert.That(b.Confidence, Is.EqualTo(a.Confidence), a.Name + ": confidence changed");
                Assert.That(b.Value, Is.EqualTo(a.Value), a.Name + ": a different hidden hand changed the value");
            });
        }

        TestContext.Out.WriteLine(
            "fairness: {0} facts identical across two contradictory hidden hands",
            first.Facts.Count);
    }

    [Test]
    public void EvaluateIsPureAndDeterministic()
    {
        var catalog = LoadCatalog();
        var conditions = LeaderConditionsOf(catalog);
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);

        var snapshot = Snapshot(7, 1, 4, 5, 14, new[] { Card("wood_druid", 0, 31) });
        var first = StateEvaluator.Evaluate(snapshot, 1, conditions, PoolOf(Deck("wood")), categories, tags);
        var second = StateEvaluator.Evaluate(snapshot, 1, conditions, PoolOf(Deck("wood")), categories, tags);

        Assert.That(second.Describe(), Is.EqualTo(first.Describe()),
            "the same snapshot must always produce the same vector");
    }

    // ---------------------------------------------------------------- content

    [Test]
    public void VectorCarriesPublicCountsAsMeasuredFacts()
    {
        var catalog = LoadCatalog();
        var vector = StateEvaluator.Evaluate(
            Snapshot(4, 0, 6, 5, 13, new[] { Card("machine_gear", 1, 41) }),
            0,
            LeaderConditionsOf(catalog),
            PoolOf(Deck("machine")),
            CategoriesOf(catalog),
            TagsOf(catalog));

        Assert.Multiple(() =>
        {
            Assert.That(vector.Turn, Is.EqualTo(4));
            Assert.That(vector.Phase, Is.EqualTo("ACTION"));
            Assert.That(vector.ViewerIndex, Is.EqualTo(0));

            Assert.That(vector.Fact(StateEvaluator.MyHandCount)!.Value, Is.EqualTo(6));
            Assert.That(vector.Fact(StateEvaluator.MyHandCount)!.Confidence, Is.EqualTo(FactConfidence.Measured));
            Assert.That(vector.Fact(StateEvaluator.OpponentHandCount)!.Value, Is.EqualTo(5));
            Assert.That(vector.Fact(StateEvaluator.OpponentDeckCount)!.Value, Is.EqualTo(13));
            Assert.That(vector.Fact(StateEvaluator.CastleHealth)!.Value, Is.EqualTo(75));

            Assert.That(vector.Fact("no.such.fact"), Is.Null, "an absent fact returns null rather than throwing");
        });
    }

    /// <summary>
    /// Board facts are exact: the board is fully public, so they are Measured and
    /// not Estimated. Hidden-hand threat, by contrast, must be flagged Estimated.
    /// </summary>
    [Test]
    public void BoardFactsAreMeasuredAndHandThreatIsEstimated()
    {
        var catalog = LoadCatalog();
        var snapshot = Snapshot(5, 0, 4, 4, 14, new[] { Card("flame_imp", 1, 51) });
        snapshot.Players[1].Field = new[]
        {
            new RuntimeCardSnapshot { EntityId = 61, CardId = "flame_imp", OwnerPlayer = 1, CurrentAttack = 3, CurrentHealth = 2 },
            new RuntimeCardSnapshot { EntityId = 62, CardId = "flame_giant", OwnerPlayer = 1, CurrentAttack = 5, CurrentHealth = 8 },
        };

        var vector = StateEvaluator.Evaluate(
            snapshot, 0, LeaderConditionsOf(catalog), PoolOf(Deck("flame")), CategoriesOf(catalog), TagsOf(catalog));

        Assert.Multiple(() =>
        {
            Assert.That(vector.Fact(StateEvaluator.OpponentBoardMinions)!.Value, Is.EqualTo(2));
            Assert.That(vector.Fact(StateEvaluator.OpponentBoardMinions)!.Confidence, Is.EqualTo(FactConfidence.Measured));
            Assert.That(vector.Fact(StateEvaluator.OpponentBoardAttack)!.Value, Is.EqualTo(8));
            Assert.That(vector.Fact(StateEvaluator.OpponentLargestAttack)!.Value, Is.EqualTo(5));

            var threatFacts = vector.Facts.Where(f => f.Name.StartsWith(StateEvaluator.ThreatPrefix, StringComparison.Ordinal)).ToList();
            Assert.That(threatFacts, Is.Not.Empty, "the flame pool must expose at least one threat category");
            foreach (var fact in threatFacts)
            {
                Assert.That(
                    fact.Confidence,
                    Is.EqualTo(FactConfidence.Estimated),
                    fact.Name + ": hidden-hand threat must never be reported as measured");
            }
        });
    }

    /// <summary>
    /// A win axis whose counter IS published must be read as a measured fact, and
    /// a zero must be a real zero rather than a stand-in for "unknown".
    ///
    /// The sea leader's discard axis used to come back Unknown because the v1.31
    /// snapshot did not publish TotalDiscarded. Now that it does, this pins the new
    /// behaviour: the condition is known from the visible leader AND the progress is
    /// readable.
    /// </summary>
    [Test]
    public void PublishedWinCountersAreMeasuredAndZeroIsARealZero()
    {
        var catalog = LoadCatalog();
        var snapshot = Snapshot(9, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>());
        snapshot.Players[0].LeaderZone = new[] { Card("sea_leader", 0, 90) };
        snapshot.Players[1].TotalDiscarded = 0;

        var vector = StateEvaluator.Evaluate(
            snapshot, 0, LeaderConditionsOf(catalog), PoolOf(Deck("sea")), CategoriesOf(catalog), TagsOf(catalog));

        var progress = vector.Fact(StateEvaluator.MyWinProgress);
        Assert.That(progress, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(progress!.Value, Is.EqualTo(0), "the opponent has discarded nothing yet");
            Assert.That(progress.Confidence, Is.EqualTo(FactConfidence.Measured),
                "a published counter of 0 is measured, not unknown");
            Assert.That(progress.Note, Does.Contain("OPP_DISCARD_TOTAL_GE").Or.Contain("DISCARD"));
            Assert.That(vector.Fact(StateEvaluator.MyWinCondition)!.Note, Does.Contain("OPP_DISCARD_TOTAL_GE"),
                "the condition itself IS known from the visible leader");
        });

        TestContext.Out.WriteLine(vector.Describe());
    }

    /// <summary>
    /// A condition in a family the adapter has no counter for must come back
    /// unmeasured with the FAMILY NAMED — that is what makes a future series
    /// visible instead of silently unknown.
    /// </summary>
    [Test]
    public void UnregisteredConditionFamiliesAreReportedByName()
    {
        var catalog = LoadCatalog();
        var snapshot = Snapshot(9, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>());
        snapshot.Players[0].LeaderZone = new[] { Card("sea_leader", 0, 90) };

        // A synthetic condition family that no counter is registered for.
        var conditions = new Dictionary<string, LeaderWinCondition>(StringComparer.Ordinal)
        {
            ["sea_leader"] = new LeaderWinCondition("sea_leader", "OPP_SOMETHING_NEW_GE", 5),
        };

        var vector = StateEvaluator.Evaluate(
            snapshot, 0, conditions, PoolOf(Deck("sea")), CategoriesOf(catalog), TagsOf(catalog));

        var progress = vector.Fact(StateEvaluator.MyWinProgress);
        Assert.That(progress, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(progress!.Value, Is.Null, "an unregistered family cannot be measured");
            Assert.That(progress.Confidence, Is.EqualTo(FactConfidence.Unknown));
            Assert.That(
                progress.Note,
                Does.Contain("SOMETHING_NEW"),
                "the missing family must be named so a new series is actionable: " + progress.Note);
        });
    }

    /// <summary>
    /// Punish is a COST ON A MOVE, not a property of a hand. The evaluator must
    /// report the current counters and the turn's modifier, and must NOT report a
    /// summed "hand punish burden" — that total has no decision meaning.
    ///
    /// The per-move transfer is settled by <see cref="MoveSettlements"/>, tested
    /// separately.
    /// </summary>
    [Test]
    public void PunishCountersAreReportedButNoHandTotalIsInvented()
    {
        var catalog = LoadCatalog();
        var snapshot = Snapshot(6, 0, 4, 3, 12, new[] { Card("machine_gear", 1, 41) });
        snapshot.Players[0].PunishDrawnThisTurn = 2;
        snapshot.Players[0].PunishDeltaThisTurn = 1;
        snapshot.Players[1].TotalDiscarded = 5;

        var vector = StateEvaluator.Evaluate(
            snapshot, 0, LeaderConditionsOf(catalog), PoolOf(Deck("machine")), CategoriesOf(catalog), TagsOf(catalog));

        Assert.Multiple(() =>
        {
            Assert.That(vector.Fact(StateEvaluator.MyPunishDrawnThisTurn)!.Value, Is.EqualTo(2));
            Assert.That(vector.Fact(StateEvaluator.MyPunishDrawnThisTurn)!.Confidence, Is.EqualTo(FactConfidence.Measured));
            Assert.That(vector.Fact(StateEvaluator.MyPunishDeltaThisTurn)!.Value, Is.EqualTo(1));
            Assert.That(
                vector.Fact(StateEvaluator.MyPunishDeltaThisTurn)!.Note,
                Does.Contain("not itself a resource"),
                "the modifier must not be presented as a resource transfer");
            Assert.That(vector.Fact(StateEvaluator.OpponentTotalDiscarded)!.Value, Is.EqualTo(5));
            Assert.That(vector.Fact(StateEvaluator.MyTotalDiscarded)!.Value, Is.EqualTo(0));

            // No hand-level punish total may exist: it is action-dependent.
            Assert.That(
                vector.Facts.Any(f => f.Name.Contains("hand_punish", StringComparison.Ordinal)),
                Is.False,
                "a summed hand punish burden has no decision meaning and must not be reported");
        });

        TestContext.Out.WriteLine(vector.Describe());
    }

    /// <summary>
    /// The settled cost of a move must be READ from the engine's advertisement, not
    /// recomputed, and a move with no punish entry must be distinguished from a move
    /// that costs zero.
    /// </summary>
    [Test]
    public void MoveSettlementReadsTheEngineAdvertisedCost()
    {
        RuntimeLegalAction Advertise(string id, string type, object? punish)
        {
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (punish is not null) payload["punish"] = punish;
            return new RuntimeLegalAction
            {
                ActionId = id,
                Type = type,
                Actor = 0,
                Payload = payload,
            };
        }

        Assert.Multiple(() =>
        {
            var play = MoveSettlements.Settle(Advertise("play_1", "PLAY_CARD", 4));
            Assert.That(play.AdvertisedPunish, Is.EqualTo(4));
            Assert.That(play.TransfersCardFlow, Is.True);
            Assert.That(play.OpponentHandAfter(5), Is.EqualTo(9), "punish draws cards, so their hand grows by exactly that");
            Assert.That(play.Source, Does.Contain("resolved by the engine"));

            var free = MoveSettlements.Settle(Advertise("play_2", "PLAY_CARD", 0));
            Assert.That(free.AdvertisedPunish, Is.EqualTo(0));
            Assert.That(free.TransfersCardFlow, Is.False, "a 0 cost transfers nothing");

            // No punish entry: NOT a punish transfer, which is a different claim
            // from "costs zero".
            var silent = MoveSettlements.Settle(Advertise("end_1", "END_TURN", null));
            Assert.That(silent.AdvertisedPunish, Is.Null);
            Assert.That(silent.TransfersCardFlow, Is.False);
            Assert.That(silent.Note, Does.Contain("no punish entry"));

            // A non-integer entry is refused rather than coerced.
            var weird = MoveSettlements.Settle(Advertise("odd", "PLAY_CARD", "many"));
            Assert.That(weird.AdvertisedPunish, Is.Null, "a non-numeric punish must not be coerced to a number");

            // A negative entry is refused: it is not a valid cost.
            var negative = MoveSettlements.Settle(Advertise("neg", "PLAY_CARD", -3));
            Assert.That(negative.AdvertisedPunish, Is.Null);
        });
    }

    [Test]
    public void MoveSettlementOrdersCardFlowTransfersCheapestFirst()
    {
        RuntimeLegalAction Advertise(string id, object? punish)
            => new RuntimeLegalAction
            {
                ActionId = id,
                Type = "PLAY_CARD",
                Actor = 0,
                Payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["punish"] = punish },
            };

        var actions = new[]
        {
            Advertise("c", 8),
            Advertise("a", 1),
            Advertise("b", 4),
        };

        var ordered = MoveSettlements.CardFlowTransfers(actions);
        Assert.That(ordered.Select(s => s.ActionId), Is.EqualTo(new[] { "a", "b", "c" }),
            "a caller comparing moves wants the cheapest transfer first");
    }

    /// <summary>
    /// The win-condition counter registry must resolve by PARSED FAMILY, so a
    /// condition added in a known family works without touching code, and an
    /// unknown family names itself.
    /// </summary>
    [Test]
    public void WinConditionCountersResolveByFamilyNotByLiteralName()
    {
        Assert.Multiple(() =>
        {
            var discard = WinConditionCounters.Parse("OPP_DISCARD_TOTAL_GE");
            Assert.That(discard.Subject, Is.EqualTo("OPP"));
            Assert.That(discard.Family, Is.EqualTo("DISCARD"));
            Assert.That(discard.Predicate, Is.EqualTo("TOTAL_GE"));

            var pull = WinConditionCounters.Parse("PULL_TOTAL_GE");
            Assert.That(pull.Subject, Is.Null);
            Assert.That(pull.Family, Is.EqualTo("PULL"),
                "the trailing _TOTAL_GE is a comparison suffix, so the metric is PULL");
            Assert.That(pull.Predicate, Is.EqualTo("TOTAL_GE"));

            var punishDraw = WinConditionCounters.Parse("OPP_PUNISH_DRAW_TURN_GE");
            Assert.That(punishDraw.Subject, Is.EqualTo("OPP"));
            Assert.That(punishDraw.Family, Is.EqualTo("PUNISH_DRAW"));
            Assert.That(punishDraw.Predicate, Is.EqualTo("TURN_GE"),
                "the longer predicate must win over the shorter suffix");

            Assert.That(WinConditionCounters.Parse(string.Empty).Family, Is.Empty);
        });
    }

    /// <summary>
    /// COMPLETENESS GUARD: every win condition the card schema declares must resolve
    /// to a real counter. If a new series adds a condition, this test fails by NAME
    /// instead of the estimator quietly reporting "unknown" forever.
    /// </summary>
    [Test]
    public void EveryDeclaredWinConditionResolvesToACounter()
    {
        // The schema's WinCondition enum, which CardCatalog also validates against.
        string[] declared =
        {
            "NONE",
            "ROYAL_CASTLE_BREAK",
            "AMBUSH_TRIGGER_WIN",
            "OPP_DISCARD_TOTAL_GE",
            "OPP_PUNISH_DRAW_TURN_GE",
            "NO_DAMAGE_TURNS_GE",
            "GIANT_HEALTH_GE",
            "PULL_TOTAL_GE",
        };

        var snapshot = Snapshot(7, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>());
        var unresolved = new List<string>();

        foreach (var condition in declared)
        {
            // NONE is not an axis: a leader may legitimately declare it.
            if (condition == "NONE") continue;

            var reading = WinConditionCounters.Resolve(snapshot, 0, condition);
            if (!reading.IsMeasured) unresolved.Add(condition + " -> " + reading.Source);
        }

        Assert.That(
            unresolved,
            Is.Empty,
            "every declared win condition must have a counter, or the estimator cannot judge the race:\n  "
            + string.Join("\n  ", unresolved));
    }

    /// <summary>
    /// The two conditions that carry no numeric threshold — the castle axis and the
    /// ambush axis — must still be classified rather than dumped into "unknown",
    /// because a consumer needs to know WHICH axis it is even when the threshold is
    /// special.
    /// </summary>
    [Test]
    public void NonNumericThresholdConditionsAreStillClassified()
    {
        var snapshot = Snapshot(7, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>());
        snapshot.Castle!.Health = 41;

        Assert.Multiple(() =>
        {
            var castle = WinConditionCounters.Resolve(snapshot, 0, "ROYAL_CASTLE_BREAK");
            Assert.That(castle.IsMeasured, Is.True, "the shared castle's health IS the progress");
            Assert.That(castle.Current, Is.EqualTo(41));
            Assert.That(castle.Subject, Is.EqualTo("SHARED"));

            var ambush = WinConditionCounters.Resolve(snapshot, 0, "AMBUSH_TRIGGER_WIN");
            Assert.That(ambush.Axis, Is.Not.Empty, "the axis must be named even when it cannot be counted");
        });
    }

    [Test]
    public void WinCounterRegistryReadsTheSubjectsTheEngineReads()
    {
        var snapshot = Snapshot(7, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>());
        snapshot.Players[0].PullCount = 3;
        snapshot.Players[0].NoDamageTurns = 2;
        snapshot.Players[1].TotalDiscarded = 9;

        Assert.Multiple(() =>
        {
            // OPP_DISCARD_TOTAL_GE counts the OTHER player's discards.
            var discard = WinConditionCounters.Resolve(snapshot, 0, "OPP_DISCARD_TOTAL_GE");
            Assert.That(discard.Current, Is.EqualTo(9));
            Assert.That(discard.Subject, Is.EqualTo("OPP"));

            // PULL_TOTAL_GE counts THIS player's pulls.
            var pull = WinConditionCounters.Resolve(snapshot, 0, "PULL_TOTAL_GE");
            Assert.That(pull.Current, Is.EqualTo(3));
            Assert.That(pull.Subject, Is.EqualTo("SELF"));

            // NO_DAMAGE_TURNS_GE counts THIS player's streak.
            var streak = WinConditionCounters.Resolve(snapshot, 0, "NO_DAMAGE_TURNS_GE");
            Assert.That(streak.Current, Is.EqualTo(2));
            Assert.That(streak.Subject, Is.EqualTo("SELF"));

            // The castle is shared: same reading for either seat.
            var seat0 = WinConditionCounters.Resolve(snapshot, 0, "ROYAL_CASTLE_BREAK");
            var seat1 = WinConditionCounters.Resolve(snapshot, 1, "ROYAL_CASTLE_BREAK");
            Assert.That(seat0.Current, Is.EqualTo(seat1.Current));
            Assert.That(seat0.Subject, Is.EqualTo("SHARED"));
        });
    }

    [Test]
    public void MeasurableWinProgressBecomesAMeasuredFact()
    {
        var catalog = LoadCatalog();
        var snapshot = Snapshot(6, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>());
        snapshot.Players[0].LeaderZone = new[] { Card("machine_leader", 0, 90) };
        snapshot.Players[0].PullCount = 4;

        var vector = StateEvaluator.Evaluate(
            snapshot, 0, LeaderConditionsOf(catalog), PoolOf(Deck("machine")), CategoriesOf(catalog), TagsOf(catalog));

        var progress = vector.Fact(StateEvaluator.MyWinProgress);
        Assert.That(progress, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(progress!.Value, Is.EqualTo(4));
            Assert.That(progress.Confidence, Is.EqualTo(FactConfidence.Measured));
            Assert.That(progress.Note, Does.Contain("PULL_TOTAL_GE"));
        });
    }

    /// <summary>
    /// The vector must be honest about how much of it is solid. This is the number a
    /// consumer should look at before trusting any of it.
    /// </summary>
    [Test]
    public void VectorReportsItsOwnConfidenceMakeup()
    {
        var catalog = LoadCatalog();
        var snapshot = Snapshot(9, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>());
        snapshot.Players[0].LeaderZone = new[] { Card("sea_leader", 0, 90) };

        var vector = StateEvaluator.Evaluate(
            snapshot, 0, LeaderConditionsOf(catalog), PoolOf(Deck("sea")), CategoriesOf(catalog), TagsOf(catalog));

        Assert.Multiple(() =>
        {
            Assert.That(vector.CountWith(FactConfidence.Measured), Is.GreaterThan(0));
            Assert.That(vector.CountWith(FactConfidence.Unknown), Is.GreaterThan(0),
                "a sea leader's discard axis is unmeasurable, so at least one fact is Unknown");
            Assert.That(vector.Facts.Count, Is.EqualTo(
                vector.CountWith(FactConfidence.Measured)
                + vector.CountWith(FactConfidence.Estimated)
                + vector.CountWith(FactConfidence.Unknown)
                + vector.CountWith(FactConfidence.NotApplicable)),
                "every fact has exactly one confidence, and the counts must add up");
            Assert.That(vector.Uncertain, Is.Not.Empty);
        });
    }

    /// <summary>
    /// Two seats produce different vectors from the same snapshot: the evaluator is
    /// a view, not a global truth.
    /// </summary>
    [Test]
    public void EachSeatGetsItsOwnVector()
    {
        var catalog = LoadCatalog();
        var conditions = LeaderConditionsOf(catalog);
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var snapshot = Snapshot(6, 0, 6, 4, 14, new[] { Card("machine_gear", 1, 71) });
        snapshot.Players[0].LeaderZone = new[] { Card("wood_leader", 0, 81) };
        snapshot.Players[1].LeaderZone = new[] { Card("machine_leader", 1, 82) };
        snapshot.Players[0].PullCount = 0;
        snapshot.Players[1].PullCount = 3;

        var asViewer0 = StateEvaluator.Evaluate(snapshot, 0, conditions, PoolOf(Deck("machine")), categories, tags);
        var asViewer1 = StateEvaluator.Evaluate(snapshot, 1, conditions, PoolOf(Deck("wood")), categories, tags);

        Assert.Multiple(() =>
        {
            Assert.That(asViewer0.Fact(StateEvaluator.MyWinCondition)!.Note, Does.Contain("GIANT_HEALTH_GE"));
            Assert.That(asViewer1.Fact(StateEvaluator.MyWinCondition)!.Note, Does.Contain("PULL_TOTAL_GE"));
            Assert.That(asViewer0.ViewerIndex, Is.EqualTo(0));
            Assert.That(asViewer1.ViewerIndex, Is.EqualTo(1));
            // Seat 1's own pull progress is the one it can see as a measured fact.
            Assert.That(asViewer1.Fact(StateEvaluator.MyWinProgress)!.Value, Is.EqualTo(3));
        });
    }

    /// <summary>
    /// The uniform objective is the card-agnostic form a decision reads: a metric, a
    /// direction, a target and a progress, from which the ONLY arithmetic is
    /// "how far". This pins that the AI never has to know what the metric counts.
    /// </summary>
    [Test]
    public void VictoryObjectivesExposeADirectionAndARemainingDistance()
    {
        var catalog = LoadCatalog();
        var conditions = LeaderConditionsOf(catalog);
        var snapshot = Snapshot(6, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>());
        // Mine: machine, climbing toward 6 pulls from 4.
        snapshot.Players[0].LeaderZone = new[] { Card("machine_leader", 0, 90) };
        snapshot.Players[0].PullCount = 4;
        // Theirs: flame, breaking the castle DOWN from 41.
        snapshot.Players[1].LeaderZone = new[] { Card("flame_leader", 1, 95) };
        snapshot.Castle!.Health = 41;

        var objectives = VictoryObjectives.FromLeaderCondition(snapshot, 0, conditions);
        var mine = objectives.For(0);
        var theirs = objectives.For(1);

        Assert.That(mine, Is.Not.Null);
        Assert.That(theirs, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(mine!.Direction, Is.EqualTo(VictoryDirection.Increase));
            Assert.That(mine.Target, Is.EqualTo(6));
            Assert.That(mine.Progress, Is.EqualTo(4));
            Assert.That(mine.Remaining, Is.EqualTo(2), "an INCREASE axis counts up toward the target");
            Assert.That(mine.Completed, Is.False);

            // The castle axis moves the other way, so the same formula must be
            // signed by direction rather than assumed.
            Assert.That(theirs!.Direction, Is.EqualTo(VictoryDirection.Decrease));
            Assert.That(theirs.Progress, Is.EqualTo(41));
            Assert.That(theirs.Remaining, Is.EqualTo(41), "a DECREASE axis counts down toward the target");

            // The race comparison is a fact, not a recommendation.
            var closer = objectives.WhoIsCloser(0);
            Assert.That(closer, Is.EqualTo(-1), "I need 2, they need 41, so I am closer");
        });

        TestContext.Out.WriteLine(mine!.ToString());
        TestContext.Out.WriteLine(theirs!.ToString());
    }

    /// <summary>
    /// The engine-published producer now works, and it must read the objective the
    /// CARD DATA declares rather than reconstructing it from a condition name.
    ///
    /// This replaces a test that asserted the producer threw while unimplemented. The
    /// contract has landed, so the assertion had to change with it — keeping the old
    /// expectation would have meant shipping an implemented feature that its own test
    /// still claimed was missing.
    /// </summary>
    [Test]
    public void TheEngineObjectiveProducerReadsTheDeclaredObjective()
    {
        var snapshot = Snapshot(3, 0, 2, 2, 3, Array.Empty<RuntimeCardSnapshot>());
        // The machine leader declares PULL_COUNT / INCREASE / 6 in card data.
        snapshot.Players[0].LeaderZone = new[]
        {
            new RuntimeCardSnapshot
            {
                CardId = "machine_leader",
                OwnerPlayer = 0,
                EntityId = 90,
                Victory = new RuntimeVictoryObjectiveSnapshot
                {
                    Metric = "PULL_COUNT",
                    Direction = "INCREASE",
                    Target = 6,
                },
            },
        };

        var objectives = VictoryObjectives.FromEngine(snapshot, 0);
        var mine = objectives.For(0);

        Assert.That(mine, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(mine!.Metric, Is.EqualTo("PULL_COUNT"),
                "the metric must come from the published objective, not from parsing a condition name");
            Assert.That(mine.Direction, Is.EqualTo(VictoryDirection.Increase));
            Assert.That(mine.Target, Is.EqualTo(6));
            Assert.That(mine.Source, Does.Contain("engine-published"),
                "the provenance must say the objective came from the engine, not from the compatibility producer");
        });
    }

    /// <summary>
    /// A leader that declares no objective must say so, rather than have one invented
    /// for it. The 烈焰 leader is the shipped case: its castle win is resolved by a
    /// separate simultaneous path and carries no threshold, so "no objective declared"
    /// is the correct answer and not a gap to paper over.
    ///
    /// Note the difference from a fabricated result: Metric and Target are empty/null,
    /// and the reason names what is missing.
    /// </summary>
    [Test]
    public void LeadersWithoutADeclaredObjectiveReportWhyRatherThanGuess()
    {
        var snapshot = Snapshot(3, 0, 2, 2, 3, Array.Empty<RuntimeCardSnapshot>());
        snapshot.Players[0].LeaderZone = new[]
        {
            new RuntimeCardSnapshot { CardId = "flame_leader", OwnerPlayer = 0, EntityId = 90 },
        };

        var objectives = VictoryObjectives.FromEngine(snapshot, 0);
        var mine = objectives.For(0);

        Assert.That(mine, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(mine!.Direction, Is.EqualTo(VictoryDirection.Unknown));
            Assert.That(mine.Target, Is.Null, "no threshold may be invented for a card that declares none");
            Assert.That(mine.UnmeasurableReason, Is.Not.Null.And.Not.Empty,
                "an unknown objective must name what is missing");
            Assert.That(mine.UnmeasurableReason, Does.Contain("flame_leader"),
                "the reason must name the card so the gap is traceable");
        });
    }

    /// <summary>
    /// An objective whose progress cannot be read must report no distance rather
    /// than a distance of zero, which would read as "about to win".
    /// </summary>
    [Test]
    public void UnmeasurableObjectivesReportNoDistance()
    {
        var catalog = LoadCatalog();
        var snapshot = Snapshot(6, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>());
        snapshot.Players[0].LeaderZone = new[] { Card("flame_leader", 0, 90) };

        // An unregistered family, so progress cannot be read.
        var conditions = new Dictionary<string, LeaderWinCondition>(StringComparer.Ordinal)
        {
            ["flame_leader"] = new LeaderWinCondition("flame_leader", "SOME_NEW_AXIS_GE", 4),
        };

        var objectives = VictoryObjectives.FromLeaderCondition(snapshot, 0, conditions);
        var mine = objectives.For(0);

        Assert.That(mine, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(mine!.IsMeasurable, Is.False);
            Assert.That(mine.Remaining, Is.Null, "an unreadable objective has no distance, not a zero distance");
            Assert.That(mine.Direction, Is.EqualTo(VictoryDirection.Unknown));
            Assert.That(mine.UnmeasurableReason, Is.Not.Null.And.Not.Empty);
            Assert.That(objectives.WhoIsCloser(0), Is.Null, "the race cannot be compared when a side is unmeasurable");
        });

        // And the vector must carry it as Unknown, not as 0.
        var vector = StateEvaluator.Evaluate(
            snapshot, 0, conditions, PoolOf(Deck("flame")), CategoriesOf(catalog), TagsOf(catalog));
        var objectiveFact = vector.Fact(StateEvaluator.MyObjective);
        Assert.That(objectiveFact, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(objectiveFact!.Value, Is.Null);
            Assert.That(objectiveFact.Confidence, Is.EqualTo(FactConfidence.Unknown));
            Assert.That(vector.Fact(StateEvaluator.WhoIsCloser)!.Confidence, Is.EqualTo(FactConfidence.Unknown));
        });
    }

    /// <summary>
    /// The vector must carry the uniform objective for both sides, so a consumer can
    /// read the race without touching a condition name.
    /// </summary>
    [Test]
    public void VectorCarriesUniformObjectivesForBothSides()
    {
        var catalog = LoadCatalog();
        var snapshot = Snapshot(6, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>());
        snapshot.Players[0].LeaderZone = new[] { Card("machine_leader", 0, 90) };
        snapshot.Players[0].PullCount = 5;
        snapshot.Players[1].LeaderZone = new[] { Card("wood_leader", 1, 95) };
        snapshot.Players[1].Field = new[]
        {
            new RuntimeCardSnapshot { EntityId = 96, CardId = "wood_treant", OwnerPlayer = 1, Sealed = true, CurrentHealth = 500 },
        };

        var vector = StateEvaluator.Evaluate(
            snapshot, 0, LeaderConditionsOf(catalog), PoolOf(Deck("machine")), CategoriesOf(catalog), TagsOf(catalog));

        var objectives = VictoryObjectives.FromLeaderCondition(snapshot, 0, LeaderConditionsOf(catalog));
        var probe0 = objectives.For(0)!;
        var probe1 = objectives.For(1)!;
        TestContext.Out.WriteLine("objective[0]: metric=" + probe0.Metric + " dir=" + probe0.Direction
            + " target=" + (probe0.Target?.ToString() ?? "null")
            + " progress=" + (probe0.Progress?.ToString() ?? "null")
            + " measurable=" + probe0.IsMeasurable
            + " reason=" + (probe0.UnmeasurableReason ?? "-"));
        TestContext.Out.WriteLine("objective[1]: metric=" + probe1.Metric + " dir=" + probe1.Direction
            + " target=" + (probe1.Target?.ToString() ?? "null")
            + " progress=" + (probe1.Progress?.ToString() ?? "null")
            + " measurable=" + probe1.IsMeasurable
            + " reason=" + (probe1.UnmeasurableReason ?? "-"));

        var mine = vector.Fact(StateEvaluator.MyObjective);
        var theirs = vector.Fact(StateEvaluator.OpponentObjective);
        Assert.That(mine, Is.Not.Null);
        Assert.That(theirs, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(mine!.Value, Is.EqualTo(1), "I need 1 more pull");
            Assert.That(mine.Confidence, Is.EqualTo(FactConfidence.Measured));
            Assert.That(mine.Note, Does.Contain("Increase"));

            Assert.That(theirs!.Value, Is.EqualTo(12), "the sealed minion needs 12 more health");

            Assert.That(vector.Fact(StateEvaluator.WhoIsCloser)!.Value, Is.EqualTo(-1), "I am closer: 1 vs 12");
        });
    }

    /// <summary>
    /// GUARD: every condition the counter registry can resolve must also have a
    /// known DIRECTION, because a distance is only computable when "how far" has a
    /// sign. A missing direction makes <see cref="VictoryObjective.Remaining"/>
    /// silently null while the objective still looks measured — which is exactly the
    /// bug this test was written after: the counter reported the axis as `HEALTH`
    /// while the direction table listed it as `GIANT_HEALTH`.
    /// </summary>
    [Test]
    public void EveryResolvableConditionHasAKnownDirection()
    {
        string[] declared =
        {
            "ROYAL_CASTLE_BREAK", "AMBUSH_TRIGGER_WIN", "OPP_DISCARD_TOTAL_GE",
            "OPP_PUNISH_DRAW_TURN_GE", "NO_DAMAGE_TURNS_GE", "GIANT_HEALTH_GE", "PULL_TOTAL_GE",
        };

        var conditions = new Dictionary<string, LeaderWinCondition>(StringComparer.Ordinal);
        var snapshot = Snapshot(7, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>());
        snapshot.Players[0].LeaderZone = new[] { Card("probe_leader", 0, 90) };
        foreach (var condition in declared)
        {
            conditions["probe_leader"] = new LeaderWinCondition("probe_leader", condition, 10);
            var objective = VictoryObjectives.FromLeaderCondition(snapshot, 0, conditions).For(0)!;
            Assert.That(
                objective.Direction,
                Is.Not.EqualTo(VictoryDirection.Unknown),
                condition + " resolves progress as axis '" + objective.Metric
                + "' but has no direction, so Remaining would silently be null");
        }
    }

    // ------------------------------------------------------ outcome prediction

    /// <summary>
    /// The predictor answers the owner's question — "if I play this, how much does
    /// the opponent get, and what does that do to their threat profile" — from data
    /// the engine already published. The card flow is exact (the engine resolved the
    /// punish) and the threat shift is arithmetic on the pool's public composition.
    /// </summary>
    [Test]
    public void PredictorShiftsTheOpponentThreatProfileByTheCardFlow()
    {
        var catalog = LoadCatalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var pool = PoolOf(Deck("machine"));

        var snapshot = Snapshot(6, 0, 5, 4, 16, Array.Empty<RuntimeCardSnapshot>());
        var threat = ThreatEstimator.Estimate(snapshot, 0, pool, categories, winConditions: null, tagsByCardId: tags);

        RuntimeLegalAction Advertise(string id, object? punish)
            => new RuntimeLegalAction
            {
                ActionId = id,
                Type = "PLAY_CARD",
                Actor = 0,
                Payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["punish"] = punish },
            };

        var cheap = OutcomePredictor.Predict(threat, Advertise("cheap", 1));
        var dear = OutcomePredictor.Predict(threat, Advertise("dear", 8));

        Assert.Multiple(() =>
        {
            Assert.That(cheap.CardsHandedOver, Is.EqualTo(1));
            Assert.That(cheap.OpponentHandBefore, Is.EqualTo(4));
            Assert.That(cheap.OpponentHandAfter, Is.EqualTo(5));

            Assert.That(dear.CardsHandedOver, Is.EqualTo(8));
            Assert.That(dear.OpponentHandAfter, Is.EqualTo(12));

            // Every category must shift by draws x density, so a dearer move shifts
            // strictly more than a cheaper one for any category with real density.
            var dearRemoval = OutcomePredictor.ShiftFor(dear, ThreatKind.MinionRemoval);
            var cheapRemoval = OutcomePredictor.ShiftFor(cheap, ThreatKind.MinionRemoval);
            Assert.That(dearRemoval, Is.Not.Null);
            Assert.That(cheapRemoval, Is.Not.Null);
            Assert.That(dearRemoval!.Delta, Is.GreaterThan(cheapRemoval!.Delta),
                "handing over more cards must raise the opponent's expected holdings more");
            Assert.That(cheapRemoval.Delta, Is.GreaterThan(0.0));
            Assert.That(cheapRemoval.After, Is.EqualTo(cheapRemoval.Before + cheapRemoval.Delta).Within(1e-9));
        });

        TestContext.Out.WriteLine("cheap: " + cheap);
        TestContext.Out.WriteLine("dear:  " + dear);
    }

    /// <summary>
    /// A move that transfers nothing must produce NO threat shift, and must not be
    /// reported as shifting by zero cards' worth of nothing.
    /// </summary>
    [Test]
    public void MoveWithNoCardFlowProducesNoThreatShift()
    {
        var catalog = LoadCatalog();
        var snapshot = Snapshot(6, 0, 5, 4, 16, Array.Empty<RuntimeCardSnapshot>());
        var threat = ThreatEstimator.Estimate(
            snapshot, 0, PoolOf(Deck("machine")), CategoriesOf(catalog), winConditions: null, tagsByCardId: TagsOf(catalog));

        var free = OutcomePredictor.Predict(
            threat,
            new RuntimeLegalAction
            {
                ActionId = "play_free",
                Type = "PLAY_CARD",
                Actor = 0,
                Payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["punish"] = 0 },
            });

        var silent = OutcomePredictor.Predict(
            threat,
            new RuntimeLegalAction
            {
                ActionId = "end_1",
                Type = "END_TURN",
                Actor = 0,
                Payload = new Dictionary<string, object?>(StringComparer.Ordinal),
            });

        Assert.Multiple(() =>
        {
            Assert.That(free.CardsHandedOver, Is.EqualTo(0));
            Assert.That(free.OpponentHandAfter, Is.EqualTo(free.OpponentHandBefore));
            Assert.That(free.ThreatShifts, Is.Empty, "no transfer means no shift");

            Assert.That(silent.CardsHandedOver, Is.EqualTo(0));
            Assert.That(silent.ThreatShifts, Is.Empty);
            Assert.That(silent.Settlement.AdvertisedPunish, Is.Null, "a non-transfer is not a zero-cost transfer");
        });
    }

    /// <summary>
    /// The predictor must NOT invent how far a move advances my own victory axis: the
    /// advertisement does not describe the move's effects, so the post-move distance is
    /// unknown with a stated reason.
    /// </summary>
    [Test]
    public void PredictorRefusesToInventMyOwnAxisAlignment()
    {
        var catalog = LoadCatalog();
        var conditions = LeaderConditionsOf(catalog);
        var snapshot = Snapshot(6, 0, 5, 4, 16, Array.Empty<RuntimeCardSnapshot>());
        snapshot.Players[0].LeaderZone = new[] { Card("machine_leader", 0, 90) };
        snapshot.Players[0].PullCount = 4;

        var threat = ThreatEstimator.Estimate(
            snapshot, 0, PoolOf(Deck("machine")), CategoriesOf(catalog), winConditions: null, tagsByCardId: TagsOf(catalog));
        var myObjective = VictoryObjectives.FromLeaderCondition(snapshot, 0, conditions).For(0);

        var outcome = OutcomePredictor.Predict(
            threat,
            new RuntimeLegalAction
            {
                ActionId = "pull_1",
                Type = "PULL",
                Actor = 0,
                Payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["punish"] = 2 },
            },
            myObjective);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.MyObjectiveRemainingBefore, Is.EqualTo(2), "the distance BEFORE the move is known");
            Assert.That(
                outcome.MyObjectiveNote,
                Is.Not.Null.And.Not.Empty,
                "the post-move distance must carry a reason, not a fabricated number");
            Assert.That(outcome.MyObjectiveNote, Does.Contain("advertisement"));
        });
    }

    /// <summary>
    /// A draw larger than the unseen pool cannot all come from the pool. Reporting a
    /// confident shift would overstate the opponent's gains, so the shift is withheld
    /// with a reason.
    /// </summary>
    [Test]
    public void PredictorCapsOrRefusesWhenDrawsExceedTheUnseenPool()
    {
        var catalog = LoadCatalog();
        // A tiny unseen pool: hand 1 + deck 1.
        var snapshot = Snapshot(9, 0, 5, 1, 1, Array.Empty<RuntimeCardSnapshot>());
        var threat = ThreatEstimator.Estimate(
            snapshot, 0, PoolOf(Deck("machine")), CategoriesOf(catalog), winConditions: null, tagsByCardId: TagsOf(catalog));

        var outcome = OutcomePredictor.Predict(
            threat,
            new RuntimeLegalAction
            {
                ActionId = "huge",
                Type = "PLAY_CARD",
                Actor = 0,
                Payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["punish"] = 9 },
            });

        Assert.Multiple(() =>
        {
            Assert.That(outcome.CardsHandedOver, Is.EqualTo(9), "the cost is still what the engine said");
            foreach (var shift in outcome.ThreatShifts)
            {
                Assert.That(
                    shift.After,
                    Is.LessThanOrEqualTo(threat.UnknownPoolSize + 1e-9),
                    shift.Kind + ": an expected holding cannot exceed the whole unseen pool");
            }
        });
    }

    [Test]
    public void PredictAllCoversEveryAdvertisedMove()
    {
        var catalog = LoadCatalog();
        var snapshot = Snapshot(6, 0, 5, 4, 16, Array.Empty<RuntimeCardSnapshot>());
        var threat = ThreatEstimator.Estimate(
            snapshot, 0, PoolOf(Deck("machine")), CategoriesOf(catalog), winConditions: null, tagsByCardId: TagsOf(catalog));

        var actions = new[]
        {
            new RuntimeLegalAction { ActionId = "a", Type = "PLAY_CARD", Actor = 0, Payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["punish"] = 1 } },
            new RuntimeLegalAction { ActionId = "b", Type = "PLAY_CARD", Actor = 0, Payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["punish"] = 4 } },
            new RuntimeLegalAction { ActionId = "c", Type = "END_TURN", Actor = 0, Payload = new Dictionary<string, object?>(StringComparer.Ordinal) },
        };

        var outcomes = OutcomePredictor.PredictAll(threat, actions);
        Assert.That(outcomes, Has.Count.EqualTo(3));
        Assert.That(outcomes[0].Settlement.ActionId, Is.EqualTo("a"));
        Assert.That(outcomes[2].CardsHandedOver, Is.EqualTo(0));
    }

    [Test]
    public void PredictorRejectsMalformedInputs()
    {
        var snapshot = Snapshot(6, 0, 5, 4, 16, Array.Empty<RuntimeCardSnapshot>());
        var catalog = LoadCatalog();
        var threat = ThreatEstimator.Estimate(
            snapshot, 0, PoolOf(Deck("machine")), CategoriesOf(catalog), winConditions: null, tagsByCardId: TagsOf(catalog));

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => OutcomePredictor.Predict(null!, null!));
            Assert.Throws<ArgumentNullException>(() => OutcomePredictor.PredictAll(threat, null!));
            Assert.Throws<ArgumentNullException>(() => OutcomePredictor.ShiftFor(null!, ThreatKind.Discard));
        });
    }

    /// <summary>
    /// DECISIVE EXPERIMENT for the accounting invariant. Builds a REAL match state,
    /// projects it through the shipped viewer-safe projection, and checks that the
    /// estimator's accounted copies equal the opponent's truly hidden cards computed
    /// directly from the engine — with the mismatch printed on failure so the cause
    /// is visible rather than guessed at.
    ///
    /// The invariant: the opponent's declared pool is a fixed public list, and every
    /// copy of it is either in one of the opponent's hidden zones or somewhere the
    /// viewer can see. So  declared - visibleToViewer == opponent's hidden cards.
    /// </summary>
    [Test]
    public void AccountingInvariantMatchesTheEngineAcrossRealMatches()
    {
        var catalog = LoadCatalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var conditions = LeaderConditionsOf(catalog);

        var failures = new List<string>();
        var checkedSamples = 0;

        foreach (var (firstFaction, secondFaction) in new[]
        {
            ("machine", "machine"), ("flame", "wood"), ("sea", "machine"), ("wood", "flame"),
        })
        {
            var first = Deck(firstFaction);
            var second = Deck(secondFaction);
            var state = DominionWars.Engine.Setup.MatchSetup.Create(
                new DominionWars.Engine.Setup.MatchDeckSpec(first.Leader, first.Cards, first.Name, first.Faction),
                new DominionWars.Engine.Setup.MatchDeckSpec(second.Leader, second.Cards, second.Name, second.Faction),
                catalog.Cards,
                new DominionWars.Engine.Setup.MatchSetupOptions { Seed = 7, FirstPlayerIndex = 0, OpeningHandSize = 5 });

            var flow = DominionWars.Engine.Turns.TurnFlow.CreateDefault();
            flow.Advance(state, state.CurrentPlayerIndex);

            var pools = new[] { new Dictionary<string, int>(first.Cards, StringComparer.Ordinal),
                                new Dictionary<string, int>(second.Cards, StringComparer.Ordinal) };

            for (var turn = 0; turn < 8; turn++)
            {
                for (var viewerIndex = 0; viewerIndex < 2; viewerIndex++)
                {
                    var snapshot = DominionWars.Adapters.RuntimeSnapshotProjection.ToSnapshot(
                        state, "match_invariant", state.Turn.Number, viewerIndex, flow);

                    // THE INVARIANT, stated precisely.
                    //
                    // The estimator accounts for the OPPONENT'S DECLARED POOL: a copy
                    // is "placed" only when the viewer sees that copy in a zone on the
                    // OPPONENT'S OWN side. Nothing the viewer owns can reduce it.
                    //
                    //   accounted == opponentDeclared - (cards visible in the
                    //                                     opponent's own public zones
                    //                                     that its declared pool names)
                    //
                    // This test previously also subtracted cards visible on the
                    // VIEWER's side, including the viewer's hand. That was a real
                    // defect, not just a wrong expectation: each player draws from
                    // their own 60-card deck, so the same card id can be in both hands
                    // at once (measured: 10028 (sample, card) pairs over real engine
                    // matches). Crediting the opponent with the viewer's revealed
                    // copies made the estimator certain the opponent held none of a
                    // card the opponent was holding. `ViewerOwnedCopiesDoNotReduceTheOpponentPool`
                    // in AiThreatEstimatorTests pins the corrected behaviour.
                    var opponentPool = pools[1 - viewerIndex];
                    var visibleInOpponentPool = 0;
                    foreach (var cardId in SnapshotVisible(snapshot, 1 - viewerIndex, includeHand: false))
                    {
                        if (opponentPool.ContainsKey(cardId)) visibleInOpponentPool++;
                    }

                    var declared = opponentPool.Values.Sum();
                    var trulyHidden = declared - visibleInOpponentPool;

                    // PRE-CHECK: if the projection drops zone contents, the estimator
                    // cannot possibly account for visibility, so attribute the fault
                    // before blaming the estimator.
                    var snapshotPublicCards = 0;
                    for (var seat = 0; seat < 2; seat++)
                    {
                        var p = snapshot.Players[seat];
                        snapshotPublicCards += p.Field.Count + p.Graveyard.Count + p.CommitQueue.Count + p.CloudStack.Count;
                    }
                    var enginePublicCards = 0;
                    for (var seat = 0; seat < 2; seat++)
                    {
                        var p = state.GetPlayer(seat);
                        enginePublicCards += p.Field.Count + p.Graveyard.Count + p.CommitQueue.Count + p.CloudStack.Count;
                    }

                    if (snapshotPublicCards != enginePublicCards)
                    {
                        failures.Add(
                            firstFaction + " vs " + secondFaction + " turn " + state.Turn.Number
                            + " viewer " + viewerIndex + ": PROJECTION dropped public cards — engine has "
                            + enginePublicCards + " in public zones but the snapshot exposes " + snapshotPublicCards);
                        continue;
                    }

                    var report = ThreatEstimator.Estimate(
                        snapshot, viewerIndex, pools[1 - viewerIndex], categories, winConditions: null, tagsByCardId: tags);

                    checkedSamples++;
                    if (report.AccountedCopies != trulyHidden)
                    {
                        failures.Add(
                            firstFaction + " vs " + secondFaction + " turn " + state.Turn.Number
                            + " viewer " + viewerIndex
                            + ": estimator accounted " + report.AccountedCopies
                            + " but the engine says " + trulyHidden
                            + " hidden (declared " + declared + " - visible in pool " + visibleInOpponentPool + ")"
                            + "; reportPool=" + report.UnknownPoolSize);
                    }
                }

                // Advance a turn so the zones change.
                flow.Advance(state, state.CurrentPlayerIndex);
            }
        }

        TestContext.Out.WriteLine("samples checked: {0}, mismatches: {1}", checkedSamples, failures.Count);
        TestContext.Out.WriteLine("first failure: " + (failures.Count > 0 ? failures[0] : "(none)"));

        // Dump the projected snapshot's own zone contents for the first failure, so a
        // mismatch can be attributed to the projection or to the estimator.
        if (failures.Count > 0)
        {
            var first = Deck("flame");
            var second = Deck("wood");
            var probeState = DominionWars.Engine.Setup.MatchSetup.Create(
                new DominionWars.Engine.Setup.MatchDeckSpec(first.Leader, first.Cards, first.Name, first.Faction),
                new DominionWars.Engine.Setup.MatchDeckSpec(second.Leader, second.Cards, second.Name, second.Faction),
                catalog.Cards,
                new DominionWars.Engine.Setup.MatchSetupOptions { Seed = 7, FirstPlayerIndex = 0, OpeningHandSize = 5 });
            var probeFlow = DominionWars.Engine.Turns.TurnFlow.CreateDefault();
            probeFlow.Advance(probeState, probeState.CurrentPlayerIndex);
            var probeSnapshot = DominionWars.Adapters.RuntimeSnapshotProjection.ToSnapshot(
                probeState, "match_probe", probeState.Turn.Number, 0, probeFlow);
            for (var index = 0; index < 2; index++)
            {
                var player = probeSnapshot.Players[index];
                TestContext.Out.WriteLine(
                    "  snapshot player " + index + ": hand=" + player.Hand.Count
                    + " field=" + player.Field.Count + " grave=" + player.Graveyard.Count
                    + " commit=" + player.CommitQueue.Count + " cloud=" + player.CloudStack.Count
                    + " leader=" + player.LeaderZone.Count
                    + " handCount=" + player.HandCount + " deckCount=" + player.DeckCount);
            }
        }

        Assert.That(checkedSamples, Is.GreaterThan(50), "the sweep must be non-vacuous");
        Assert.That(
            failures,
            Is.Empty,
            "the accounting invariant must hold against the engine (" + failures.Count + " mismatches):\n  "
            + string.Join("\n  ", failures.Take(5)));
    }

    /// <summary>
    /// Card ids the SNAPSHOT exposes for one seat: its public zones, plus its hand
    /// only when that seat is the viewer (the projection empties a non-viewer's hand
    /// and ambush).
    /// </summary>
    private static IEnumerable<string> SnapshotVisible(
        DominionWars.Adapters.RuntimeSnapshotEnvelope snapshot,
        int seat,
        bool includeHand)
    {
        var player = snapshot.Players[seat];
        foreach (var zone in new[] { player.Field, player.Graveyard, player.CommitQueue, player.CloudStack })
        {
            foreach (var card in zone)
            {
                if (card is not null && !string.IsNullOrEmpty(card.CardId)) yield return card.CardId;
            }
        }

        if (!includeHand) yield break;
        foreach (var card in player.Hand)
        {
            if (card is not null && !string.IsNullOrEmpty(card.CardId)) yield return card.CardId;
        }
    }

    /// <summary>
    /// Minimal test of the accounting mechanism: putting copies of the opponent's
    /// pool cards into the OPPONENT'S OWN public zones must reduce the accounted
    /// copies by exactly that many. If this holds, the estimator's mechanism is sound
    /// and a failure against the engine must come from elsewhere.
    ///
    /// This test previously asserted the reduction when the copies sat in the
    /// VIEWER's hand, which encoded the defect rather than a rule: each player draws
    /// from their own deck, so the viewer holding a card id says nothing about the
    /// opponent's. The viewer-side case is now pinned the other way round, in
    /// `AiThreatEstimatorTests.ViewerOwnedCopiesDoNotReduceTheOpponentPool`.
    /// </summary>
    [Test]
    public void AccountedCopiesDropWhenPoolCardsBecomeVisible()
    {
        var catalog = LoadCatalog();
        var categories = CategoriesOf(catalog);
        var pool = PoolOf(Deck("machine"));
        var declared = pool.Values.Sum();

        var snapshot = Snapshot(3, 0, 2, 3, 12, Array.Empty<RuntimeCardSnapshot>());
        // Cards the VIEWER owns must not reduce the opponent's pool at all.
        snapshot.Players[0].Hand = new[] { Card("machine_drone", 0, 1), Card("machine_golem", 0, 2) };
        snapshot.Players[0].Graveyard = new[] { Card("machine_wall", 0, 3) };

        var report = ThreatEstimator.Estimate(
            snapshot, 0, pool, categories, winConditions: null, tagsByCardId: TagsOf(catalog));

        TestContext.Out.WriteLine("declared={0} accounted={1} poolSize={2}",
            declared, report.AccountedCopies, report.UnknownPoolSize);
        TestContext.Out.WriteLine("viewer hand ids = [{0}]",
            string.Join(",", snapshot.Players[0].Hand.Select(c => c.CardId)));

        Assert.Multiple(() =>
        {
            Assert.That(report.UnknownPoolSize, Is.EqualTo(declared));
            Assert.That(
                report.AccountedCopies,
                Is.EqualTo(declared),
                "nothing the VIEWER owns can place a copy of the opponent's deck list");
            Assert.That(report.VisibleCopiesInPool, Is.Zero);
        });

        // A copy the OPPONENT has publicly spent counts.
        snapshot.Players[1].Graveyard = new[] { Card("machine_wall", 1, 11) };
        var second = ThreatEstimator.Estimate(
            snapshot, 0, pool, categories, winConditions: null, tagsByCardId: TagsOf(catalog));
        Assert.That(second.AccountedCopies, Is.EqualTo(declared - 1));
        Assert.That(second.VisibleCopiesInPool, Is.EqualTo(1));
    }

    /// <summary>
    /// THE ACCOUNTING IDENTITY, made machine-checkable.
    ///
    /// Every copy of the opponent's declared pool is either placeable by the viewer or
    /// hidden, so the estimator's two counts must sum to the declared total. Asserted
    /// against real projected snapshots from real matches — which is where my earlier
    /// hand-written expectation kept disagreeing with the estimator by a copy or two.
    /// The expectation was doing the bookkeeping; this instead checks the estimator's
    /// OWN two numbers against the one number the caller supplied.
    /// </summary>
    [Test]
    public void AccountedPlusVisibleEqualsTheDeclaredPool()
    {
        var catalog = LoadCatalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);

        var failures = new List<string>();
        var checkedSamples = 0;

        foreach (var (firstFaction, secondFaction) in new[]
        {
            ("machine", "machine"), ("flame", "flame"), ("flame", "wood"), ("sea", "machine"), ("wood", "flame"),
        })
        {
            var first = Deck(firstFaction);
            var second = Deck(secondFaction);
            var state = DominionWars.Engine.Setup.MatchSetup.Create(
                new DominionWars.Engine.Setup.MatchDeckSpec(first.Leader, first.Cards, first.Name, first.Faction),
                new DominionWars.Engine.Setup.MatchDeckSpec(second.Leader, second.Cards, second.Name, second.Faction),
                catalog.Cards,
                new DominionWars.Engine.Setup.MatchSetupOptions { Seed = 11, FirstPlayerIndex = 0, OpeningHandSize = 5 });
            var flow = DominionWars.Engine.Turns.TurnFlow.CreateDefault();
            flow.Advance(state, state.CurrentPlayerIndex);

            var pools = new[] { new Dictionary<string, int>(first.Cards, StringComparer.Ordinal),
                                new Dictionary<string, int>(second.Cards, StringComparer.Ordinal) };

            for (var turn = 0; turn < 10; turn++)
            {
                for (var viewerIndex = 0; viewerIndex < 2; viewerIndex++)
                {
                    var snapshot = DominionWars.Adapters.RuntimeSnapshotProjection.ToSnapshot(
                        state, "match_identity", state.Turn.Number, viewerIndex, flow);
                    var pool = pools[1 - viewerIndex];
                    var declared = pool.Values.Sum();

                    var report = ThreatEstimator.Estimate(
                        snapshot, viewerIndex, pool, categories, winConditions: null, tagsByCardId: tags);

                    checkedSamples++;
                    var sum = report.AccountedCopies + report.VisibleCopiesInPool;
                    if (sum != declared)
                    {
                        failures.Add(
                            firstFaction + " vs " + secondFaction + " turn " + state.Turn.Number
                            + " viewer " + viewerIndex
                            + ": accounted " + report.AccountedCopies
                            + " + visible " + report.VisibleCopiesInPool
                            + " = " + sum + " but the pool declares " + declared);
                    }
                }

                flow.Advance(state, state.CurrentPlayerIndex);
            }
        }

        TestContext.Out.WriteLine("identity samples checked: {0}, violations: {1}", checkedSamples, failures.Count);
        Assert.That(checkedSamples, Is.GreaterThan(80), "the sweep must be non-vacuous");
        Assert.That(
            failures,
            Is.Empty,
            "accounted + visible must equal the declared pool (" + failures.Count + " violations):\n  "
            + string.Join("\n  ", failures.Take(5)));
    }

    /// <summary>
    /// A viewer card from a DIFFERENT deck must not reduce the opponent's pool: it was
    /// never a candidate for them to hold. This is the precise form of the invariant
    /// that my earlier over-broad expectation got wrong.
    /// </summary>
    [Test]
    public void ForeignDeckCardsDoNotReduceTheOpponentPool()
    {
        var catalog = LoadCatalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var pool = PoolOf(Deck("machine"));
        var declared = pool.Values.Sum();

        // flame ids are real cards, but not ones the machine deck declares.
        var snapshot = Snapshot(3, 0, 2, 3, 12, Array.Empty<RuntimeCardSnapshot>());
        snapshot.Players[0].Hand = new[] { Card("flame_imp", 0, 1), Card("flame_strike", 0, 2) };

        var report = ThreatEstimator.Estimate(
            snapshot, 0, pool, categories, winConditions: null, tagsByCardId: tags);

        Assert.Multiple(() =>
        {
            Assert.That(report.VisibleCopiesInPool, Is.EqualTo(0),
                "flame cards are not in the machine deck, so they place nothing in its pool");
            Assert.That(report.AccountedCopies, Is.EqualTo(declared));
            Assert.That(report.AccountedCopies + report.VisibleCopiesInPool, Is.EqualTo(declared));
        });
    }

    /// <summary>
    /// The four cards whose only threat action is NEGATE must categorise as Negation
    /// when read through the REAL catalogue. If they do, then a missing Negation
    /// category in a live estimate cannot be a classification gap and must come from
    /// the pool side.
    /// </summary>
    [Test]
    public void NegateCardsCategoriseAsNegationThroughTheRealCatalog()
    {
        var catalog = LoadCatalog();
        var categories = CategoriesOf(catalog);

        foreach (var cardId in new[] { "flame_ambush_seal", "machine_null", "sea_ink", "wood_veil" })
        {
            Assert.That(catalog.Cards.ContainsKey(cardId), Is.True, cardId + " must exist in the catalogue");
            var definition = catalog.Cards[cardId];
            Assert.That(definition.AmbushEffects, Is.Not.Empty,
                cardId + " is an AMBUSH card, so its ambushEffects must be loaded");

            var kinds = categories[cardId];
            Assert.That(
                kinds,
                Does.Contain(ThreatKind.Negation),
                cardId + " must categorise as Negation; got [" + string.Join(",", kinds) + "]"
                + " actions=[" + string.Join(",",
                    definition.AmbushEffects.Select(e => e.Action + "/" + e.Target)) + "]");
        }
    }

    /// <summary>
    /// The per-card form of the accounting identity, on a real match: whatever the
    /// estimator placed must be a card the pool declares, and the per-card placements
    /// must sum to the reported total. This names a mistaken placement instead of only
    /// counting it.
    /// </summary>
    [Test]
    public void VisiblePlacementsArePerCardAndSumToTheTotal()
    {
        var catalog = LoadCatalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);

        var first = Deck("flame");
        var second = Deck("flame");
        var state = DominionWars.Engine.Setup.MatchSetup.Create(
            new DominionWars.Engine.Setup.MatchDeckSpec(first.Leader, first.Cards, first.Name, first.Faction),
            new DominionWars.Engine.Setup.MatchDeckSpec(second.Leader, second.Cards, second.Name, second.Faction),
            catalog.Cards,
            new DominionWars.Engine.Setup.MatchSetupOptions { Seed = 3, FirstPlayerIndex = 0, OpeningHandSize = 5 });
        var flow = DominionWars.Engine.Turns.TurnFlow.CreateDefault();
        flow.Advance(state, state.CurrentPlayerIndex);

        var pool = new Dictionary<string, int>(first.Cards, StringComparer.Ordinal);
        var snapshot = DominionWars.Adapters.RuntimeSnapshotProjection.ToSnapshot(
            state, "match_percard", state.Turn.Number, 0, flow);
        var report = ThreatEstimator.Estimate(
            snapshot, 0, pool, categories, winConditions: null, tagsByCardId: tags);

        TestContext.Out.WriteLine("visible placements: [{0}] (total {1} of {2} declared)",
            string.Join(", ", report.VisibleByCardId.Select(p => p.Key + "x" + p.Value)),
            report.VisibleCopiesInPool,
            pool.Values.Sum());

        var summed = 0;
        foreach (var entry in report.VisibleByCardId)
        {
            Assert.That(pool.ContainsKey(entry.Key), Is.True,
                entry.Key + " was placed but the pool does not declare it");
            Assert.That(entry.Value, Is.LessThanOrEqualTo(pool[entry.Key]),
                entry.Key + ": placed " + entry.Value + " copies but the pool declares " + pool[entry.Key]);
            summed += entry.Value;
        }

        Assert.Multiple(() =>
        {
            Assert.That(summed, Is.EqualTo(report.VisibleCopiesInPool),
                "the per-card placements must sum to the reported total");
        });

        // Advance the match and re-check: the placements must track the OPPONENT's
        // published cards, and must never be inflated by the viewer's own hand.
        // An earlier revision asserted VisibleCopiesInPool >= the viewer's hand size
        // here, which was the defect written down as an expectation: the estimator is
        // not allowed to place a copy in the opponent's pool just because the viewer
        // holds the same card id.
        for (var turn = 0; turn < 6; turn++)
        {
            flow.Advance(state, state.CurrentPlayerIndex);
            var later = DominionWars.Adapters.RuntimeSnapshotProjection.ToSnapshot(
                state, "match_percard", state.Turn.Number, 0, flow);
            var laterReport = ThreatEstimator.Estimate(
                later, 0, pool, categories, winConditions: null, tagsByCardId: tags);

            var laterOpponentPublic = 0;
            var opponentSnapshot = later.Players[1];
            foreach (var zone in new[]
            {
                opponentSnapshot.Field, opponentSnapshot.Graveyard,
                opponentSnapshot.CommitQueue, opponentSnapshot.CloudStack,
            })
            {
                foreach (var card in zone)
                {
                    if (card is not null && pool.ContainsKey(card.CardId)) laterOpponentPublic++;
                }
            }

            Assert.That(
                laterReport.VisibleCopiesInPool,
                Is.EqualTo(laterOpponentPublic),
                "turn " + state.Turn.Number + ": placements must equal the opponent's own published pool cards");
        }
    }

    /// <summary>
    /// COUNTS EVERY INSTANCE, mechanically, across both players' zones and compares
    /// against the declared deck list. If a card id ever has more live instances than
    /// its declared copies, the engine is materialising extra copies and no
    /// list-based accounting can be correct — which would explain why the estimator
    /// "places" three copies of a card the opponent simultaneously holds.
    ///
    /// This is deliberately a COUNTING test, not a reasoning test: the last three
    /// attempts to explain this by inference were all wrong.
    /// </summary>
    [Test]
    public void NoZoneHoldsMoreInstancesThanTheDeckDeclares()
    {
        var catalog = LoadCatalog();
        var failures = new List<string>();
        var sampleFailures = new List<string>();
        var checkedTurns = 0;

        foreach (var (firstFaction, secondFaction) in new[] { ("flame", "flame"), ("machine", "machine") })
        {
            var first = Deck(firstFaction);
            var second = Deck(secondFaction);
            var state = DominionWars.Engine.Setup.MatchSetup.Create(
                new DominionWars.Engine.Setup.MatchDeckSpec(first.Leader, first.Cards, first.Name, first.Faction),
                new DominionWars.Engine.Setup.MatchDeckSpec(second.Leader, second.Cards, second.Name, second.Faction),
                catalog.Cards,
                new DominionWars.Engine.Setup.MatchSetupOptions { Seed = 5, FirstPlayerIndex = 0, OpeningHandSize = 5 });
            var flow = DominionWars.Engine.Turns.TurnFlow.CreateDefault();
            flow.Advance(state, state.CurrentPlayerIndex);

            var declared = new Dictionary<string, int>(first.Cards, StringComparer.Ordinal);

            for (var turn = 0; turn < 14; turn++)
            {
                for (var seat = 0; seat < 2; seat++)
                {
                    var player = state.GetPlayer(seat);
                    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                    var zones = new (string Name, IList<DominionWars.Engine.Model.CardInstance> Cards)[]
                    {
                        ("hand", player.Hand),
                        ("deck", player.Deck),
                        ("field", player.Field),
                        ("leader", player.LeaderZone),
                        ("ambush", player.AmbushZone),
                        ("graveyard", player.Graveyard),
                        ("commit", player.CommitQueue),
                        ("cloud", player.CloudStack),
                    };

                    foreach (var (zoneName, cards) in zones)
                    {
                        foreach (var card in cards)
                        {
                            var id = card.Definition.Id;
                            counts.TryGetValue(id, out var running);
                            counts[id] = running + 1;
                            if (!declared.TryGetValue(id, out var allowed))
                            {
                                // A card the deck does not declare at all: a token or a
                                // cross-deck transfer. Recorded, not treated as a fault.
                                sampleFailures.Add("turn " + state.Turn.Number + " seat " + seat + " " + zoneName
                                    + ": undeclared card id " + id);
                                continue;
                            }

                            if (counts[id] > allowed)
                            {
                                failures.Add(
                                    firstFaction + " turn " + state.Turn.Number + " seat " + seat + " " + zoneName
                                    + ": '" + id + "' now has " + counts[id] + " live instances but the deck declares "
                                    + allowed + " (zones counted: " + string.Join("+", zones.Select(z => z.Name)) + ")");
                            }
                        }
                    }

                    checkedTurns++;
                }

                flow.Advance(state, state.CurrentPlayerIndex);
            }
        }

        TestContext.Out.WriteLine("seat-turns checked: {0}", checkedTurns);
        TestContext.Out.WriteLine("over-instance failures: {0}", failures.Count);
        foreach (var line in failures.Take(6)) TestContext.Out.WriteLine("  " + line);
        TestContext.Out.WriteLine("undeclared-card observations: {0}", sampleFailures.Count);
        foreach (var line in sampleFailures.Distinct().Take(6)) TestContext.Out.WriteLine("  " + line);

        Assert.That(checkedTurns, Is.GreaterThan(20), "the sweep must be non-vacuous");
        Assert.That(
            failures,
            Is.Empty,
            "no zone set may hold more live instances than the deck declares (" + failures.Count + " violations):\n  "
            + string.Join("\n  ", failures.Take(5)));
    }

    /// <summary>
    /// THE CONTRADICTION, asserted. If the estimator places every declared copy of a
    /// card as visible, the opponent cannot be holding one. This test states that as a
    /// law; if it fails, the failure message names the card and the exact zone
    /// occupancy, which is the measurement the last few rounds were missing.
    /// </summary>
    [Test]
    public void ACardCannotBeFullyPlacedAndAlsoHeld()
    {
        var catalog = LoadCatalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var conditions = LeaderConditionsOf(catalog);

        var failures = new List<string>();
        var checkedTurns = 0;

        foreach (var (firstFaction, secondFaction) in new[]
        {
            ("flame", "flame"), ("machine", "machine"), ("flame", "wood"), ("sea", "machine"),
        })
        {
            var first = Deck(firstFaction);
            var second = Deck(secondFaction);
            var state = DominionWars.Engine.Setup.MatchSetup.Create(
                new DominionWars.Engine.Setup.MatchDeckSpec(first.Leader, first.Cards, first.Name, first.Faction),
                new DominionWars.Engine.Setup.MatchDeckSpec(second.Leader, second.Cards, second.Name, second.Faction),
                catalog.Cards,
                new DominionWars.Engine.Setup.MatchSetupOptions { Seed = 5, FirstPlayerIndex = 0, OpeningHandSize = 5 });
            var flow = DominionWars.Engine.Turns.TurnFlow.CreateDefault();
            flow.Advance(state, state.CurrentPlayerIndex);

            var pools = new[] { new Dictionary<string, int>(first.Cards, StringComparer.Ordinal),
                                new Dictionary<string, int>(second.Cards, StringComparer.Ordinal) };

            for (var turn = 0; turn < 14; turn++)
            {
                for (var viewerIndex = 0; viewerIndex < 2; viewerIndex++)
                {
                    var snapshot = DominionWars.Adapters.RuntimeSnapshotProjection.ToSnapshot(
                        state, "match_law", state.Turn.Number, viewerIndex, flow);
                    var pool = pools[1 - viewerIndex];
                    var opponent = state.GetOpponent(viewerIndex);

                    var report = ThreatEstimator.Estimate(
                        snapshot, viewerIndex, pool, categories, winConditions: null, tagsByCardId: tags);
                    checkedTurns++;

                    var heldByOpponent = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var card in opponent.Hand)
                    {
                        var id = card.Definition.Id;
                        heldByOpponent.TryGetValue(id, out var running);
                        heldByOpponent[id] = running + 1;
                    }

                    foreach (var entry in report.VisibleByCardId)
                    {
                        if (!heldByOpponent.TryGetValue(entry.Key, out var held)) continue;
                        if (!pool.TryGetValue(entry.Key, out var declared)) continue;
                        // Placed + held may not exceed the declared copies of that id.
                        if (entry.Value + held <= declared) continue;

                        var player = state.GetPlayer(1 - viewerIndex);
                        var occupancy = "oppHand=" + player.Hand.Count(c => c.Definition.Id == entry.Key)
                            + " oppDeck=" + player.Deck.Count(c => c.Definition.Id == entry.Key)
                            + " oppField=" + player.Field.Count(c => c.Definition.Id == entry.Key)
                            + " oppGrave=" + player.Graveyard.Count(c => c.Definition.Id == entry.Key)
                            + " oppCommit=" + player.CommitQueue.Count(c => c.Definition.Id == entry.Key)
                            + " oppCloud=" + player.CloudStack.Count(c => c.Definition.Id == entry.Key)
                            + " oppAmbush=" + player.AmbushZone.Count(c => c.Definition.Id == entry.Key)
                            + " | myHand=" + state.GetPlayer(viewerIndex).Hand.Count(c => c.Definition.Id == entry.Key)
                            + " myField=" + state.GetPlayer(viewerIndex).Field.Count(c => c.Definition.Id == entry.Key)
                            + " myGrave=" + state.GetPlayer(viewerIndex).Graveyard.Count(c => c.Definition.Id == entry.Key)
                            + " myCommit=" + state.GetPlayer(viewerIndex).CommitQueue.Count(c => c.Definition.Id == entry.Key)
                            + " myCloud=" + state.GetPlayer(viewerIndex).CloudStack.Count(c => c.Definition.Id == entry.Key);

                        failures.Add(
                            firstFaction + " vs " + secondFaction + " turn " + state.Turn.Number
                            + " viewer " + viewerIndex + " card '" + entry.Key + "': estimator placed "
                            + entry.Value + " as visible while the opponent holds " + held
                            + " of " + declared + " declared — occupancy: " + occupancy);
                    }
                }

                flow.Advance(state, state.CurrentPlayerIndex);
            }
        }

        TestContext.Out.WriteLine("law samples checked: {0}, violations: {1}", checkedTurns, failures.Count);
        foreach (var line in failures.Take(6)) TestContext.Out.WriteLine("  " + line);

        Assert.That(checkedTurns, Is.GreaterThan(40), "the sweep must be non-vacuous");
        Assert.That(
            failures,
            Is.Empty,
            "a card cannot be both fully placed as visible and held (" + failures.Count + " violations):\n  "
            + string.Join("\n  ", failures.Take(4)));
    }

    [Test]
    public void RejectsMalformedInputs()
    {
        var catalog = LoadCatalog();
        var conditions = LeaderConditionsOf(catalog);
        var categories = CategoriesOf(catalog);
        var pool = PoolOf(Deck("wood"));
        var snapshot = Snapshot(3, 0, 2, 2, 3, Array.Empty<RuntimeCardSnapshot>());

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => StateEvaluator.Evaluate(null!, 0, conditions, pool, categories));
            Assert.Throws<ArgumentNullException>(() => StateEvaluator.Evaluate(snapshot, 0, null!, pool, categories));
            Assert.Throws<ArgumentNullException>(() => StateEvaluator.Evaluate(snapshot, 0, conditions, null!, categories));
            Assert.Throws<ArgumentNullException>(() => StateEvaluator.Evaluate(snapshot, 0, conditions, pool, null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => StateEvaluator.Evaluate(snapshot, 2, conditions, pool, categories));
            Assert.Throws<ArgumentOutOfRangeException>(() => StateEvaluator.Evaluate(snapshot, -1, conditions, pool, categories));
        });
    }

    [Test]
    public void RejectsASnapshotWithoutTwoPlayers()
    {
        var catalog = LoadCatalog();
        var snapshot = Snapshot(3, 0, 2, 2, 3, Array.Empty<RuntimeCardSnapshot>());
        snapshot.Players = new[] { snapshot.Players[0] };

        Assert.Throws<ArgumentException>(() => StateEvaluator.Evaluate(
            snapshot, 0, LeaderConditionsOf(catalog), PoolOf(Deck("wood")), CategoriesOf(catalog)));
    }
}

}
