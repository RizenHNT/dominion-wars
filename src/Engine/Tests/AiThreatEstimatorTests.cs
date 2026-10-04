using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using DominionWars.Adapters;
using DominionWars.Adapters.Ai;
using DominionWars.Data;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// The threat estimator: the hypergeometric arithmetic, the fairness boundary,
/// and the honesty of what it refuses to claim.
///
/// The fairness tests are adversarial on purpose. The estimator is allowed to
/// reason about the opponent's PUBLIC counts; it is never allowed to read the
/// opponent's hand contents. The strongest available proof of that is to change
/// the opponent's hand contents while holding every public count identical and
/// require the report to be byte-identical. If a future change starts peeking,
/// that test fails.
/// </summary>
[TestFixture]
public sealed class AiThreatEstimatorTests
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

        throw new DirectoryNotFoundException("Could not locate the repository root from " + TestContext.CurrentContext.TestDirectory);
    }

    private static CardCatalog LoadCatalog()
        => CardCatalog.LoadDirectory(Path.Combine(RepositoryRoot(), "data", "cards"));

    private static DeckDefinition Deck(string faction)
        => DeckLoader.LoadFile(Path.Combine(RepositoryRoot(), "data", "decks", faction + "_deck.json"));

    /// <summary>
    /// Every effect a card carries, as action + target pairs, in list order.
    /// The target is carried because the classification depends on it: the same
    /// DAMAGE action is single-target removal, a board sweep, or core pressure.
    /// </summary>
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

    /// <summary>Maps a data target id onto the estimator's target classification.</summary>
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

    private static IReadOnlyDictionary<string, int> PoolOf(DeckDefinition deck)
        => new Dictionary<string, int>(deck.Cards, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, IReadOnlyList<ThreatKind>> CategoriesOf(CardCatalog catalog)
    {
        var map = new Dictionary<string, IReadOnlyList<ThreatKind>>(StringComparer.Ordinal);
        foreach (var pair in catalog.Cards)
        {
            map[pair.Key] = ThreatEstimator.CategoriesOf(EffectsOf(pair.Value));
        }

        return map;
    }

    /// <summary>Card id to the tags it carries, from production data.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> TagsOf(CardCatalog catalog)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var pair in catalog.Cards)
        {
            map[pair.Key] = pair.Value.Tags is null
                ? Array.Empty<string>()
                : pair.Value.Tags.ToArray();
        }

        return map;
    }

    private static RuntimeCardSnapshot Card(string cardId, int owner, int index = 1)
        => new RuntimeCardSnapshot { EntityId = index, CardId = cardId, OwnerPlayer = owner };

    /// <summary>
    /// A snapshot where the only thing that differs between two calls is the
    /// opponent's HAND CONTENTS (never their counts). Ambush is left empty for
    /// both sides because an unflipped ambush is hidden information too.
    /// </summary>
    private static RuntimeSnapshotEnvelope Snapshot(
        int turn,
        int viewerIndex,
        int viewerHandCount,
        int opponentHandCount,
        int opponentDeckCount,
        IReadOnlyList<RuntimeCardSnapshot> viewerHand,
        IReadOnlyList<RuntimeCardSnapshot> opponentHand,
        IReadOnlyList<RuntimeCardSnapshot>? opponentField = null,
        int castleHealth = 75,
        int? viewerLife = 20)
    {
        var players = new List<RuntimePlayerSnapshot>();
        for (var index = 0; index < 2; index++)
        {
            var isViewer = index == viewerIndex;
            players.Add(new RuntimePlayerSnapshot
            {
                PlayerId = "player_" + index,
                Life = isViewer ? viewerLife : 20,
                DeckCount = isViewer ? 20 : opponentDeckCount,
                HandCount = isViewer ? viewerHandCount : opponentHandCount,
                Field = isViewer ? Array.Empty<RuntimeCardSnapshot>() : (opponentField ?? Array.Empty<RuntimeCardSnapshot>()),
                LeaderZone = Array.Empty<RuntimeCardSnapshot>(),
                Graveyard = Array.Empty<RuntimeCardSnapshot>(),
                CommitQueue = Array.Empty<RuntimeCardSnapshot>(),
                CloudStack = Array.Empty<RuntimeCardSnapshot>(),
                // The v1.31 projection empties a non-viewer's hand. The fixture
                // populates it anyway so the fairness test is meaningful: if the
                // estimator ever read it, the two runs would disagree.
                Hand = isViewer ? viewerHand : opponentHand,
            });
        }

        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = 1,
            MatchId = "match_threat_test",
            SnapshotRevision = turn,
            Turn = turn,
            Phase = "ACTION",
            CurrentPlayer = viewerIndex,
            ViewerPlayerId = "player_" + viewerIndex,
            Players = players,
            Castle = new RuntimeCastleSnapshot { Enabled = true, Health = castleHealth },
        };
    }

    // ---------------------------------------------------------------- math

    /// <summary>
    /// Exact binomial coefficient, used as the reference the floating-point
    /// implementation is checked against. Small inputs only; that is the point.
    /// </summary>
    private static BigInteger Choose(int n, int k)
    {
        if (k < 0 || k > n) return BigInteger.Zero;
        var result = BigInteger.One;
        for (var i = 1; i <= k; i++)
        {
            result = result * (n - k + i) / i;
        }

        return result;
    }

    [Test]
    public void AtLeastOneProbabilityMatchesExactCombinatorics()
    {
        var compared = 0;
        for (var population = 1; population <= 60; population++)
        {
            for (var successes = 1; successes <= population; successes++)
            {
                for (var draws = 1; draws <= population; draws++)
                {
        BigInteger numerator;
        BigInteger denominator;
        double expected;
        if (population - successes < draws)
        {
            expected = 1.0;
        }
        else
        {
            numerator = Choose(population - successes, draws);
            denominator = Choose(population, draws);
            expected = 1.0 - (double)numerator / (double)denominator;
        }

                    var actual = ThreatEstimator.AtLeastOneProbability(population, successes, draws);
                    Assert.That(
                        actual,
                        Is.EqualTo(expected).Within(1e-9),
                        "population=" + population + " successes=" + successes + " draws=" + draws);
                    compared++;
                }
            }
        }

        TestContext.Out.WriteLine("hypergeometric comparisons against exact combinatorics: {0}", compared);
        Assert.That(compared, Is.GreaterThan(50000), "the sweep must be non-vacuous");
    }

    [Test]
    public void AtLeastOneProbabilityHandlesItsBoundaries()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ThreatEstimator.AtLeastOneProbability(10, 0, 5), Is.EqualTo(0.0),
                "a category with no unseen copies cannot be held");
            Assert.That(ThreatEstimator.AtLeastOneProbability(10, 3, 0), Is.EqualTo(0.0),
                "an empty hand holds nothing");
            Assert.That(ThreatEstimator.AtLeastOneProbability(5, 3, 3), Is.EqualTo(1.0),
                "drawing 3 of 5 with only 2 non-successes must include a success");
            Assert.That(ThreatEstimator.AtLeastOneProbability(3, 3, 1), Is.EqualTo(1.0),
                "every card in the pool is a success");
        });
    }

    [Test]
    public void AtLeastOneProbabilityRejectsImpossibleInputs()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ThreatEstimator.AtLeastOneProbability(-1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ThreatEstimator.AtLeastOneProbability(10, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ThreatEstimator.AtLeastOneProbability(10, 0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ThreatEstimator.AtLeastOneProbability(10, 11, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ThreatEstimator.AtLeastOneProbability(10, 1, 11));
        });
    }

    // ------------------------------------------------------------ fairness

    /// <summary>
    /// THE FAIRNESS BOUNDARY. Two snapshots that are identical in every public
    /// count but hold completely different cards in the opponent's hand must
    /// produce the same estimate, field for field. This is what stops the
    /// estimator from quietly reading hidden information.
    /// </summary>
    [Test]
    public void OpponentHandContentsCannotChangeTheEstimate()
    {
        var catalog = LoadCatalog();
        var pool = PoolOf(Deck("machine"));
        var categories = CategoriesOf(catalog);

        var opponentHandA = new[] { Card("machine_piston", 1, 1), Card("machine_gear", 1, 2), Card("machine_bolt", 1, 3) };
        var opponentHandB = new[] { Card("machine_null", 1, 11), Card("machine_trap", 1, 12), Card("machine_alpha", 1, 13) };

        var baseline = ThreatEstimator.Estimate(
            Snapshot(6, 0, 4, 3, 12, new[] { Card("wood_sapling", 0) }, opponentHandA),
            0,
            pool,
            categories);
        var altered = ThreatEstimator.Estimate(
            Snapshot(6, 0, 4, 3, 12, new[] { Card("wood_sapling", 0) }, opponentHandB),
            0,
            pool,
            categories);

        Assert.Multiple(() =>
        {
            Assert.That(altered.OpponentHandCount, Is.EqualTo(baseline.OpponentHandCount));
            Assert.That(altered.UnknownPoolSize, Is.EqualTo(baseline.UnknownPoolSize));
            Assert.That(altered.AccountedCopies, Is.EqualTo(baseline.AccountedCopies));
            Assert.That(
                altered.Probabilities.Count,
                Is.EqualTo(baseline.Probabilities.Count),
                "swapping hidden hand contents must not change which categories are asked");
            for (var index = 0; index < baseline.Probabilities.Count; index++)
            {
                Assert.That(
                    altered.Probabilities[index].Kind,
                    Is.EqualTo(baseline.Probabilities[index].Kind));
                Assert.That(
                    altered.Probabilities[index].Probability,
                    Is.EqualTo(baseline.Probabilities[index].Probability),
                    "a different hidden hand with the same public counts changed the estimate for "
                    + baseline.Probabilities[index].Kind);
            }
        });

        TestContext.Out.WriteLine(
            "fairness: {0} categories identical across two contradictory hidden hands (hand {1}, deck {2})",
            baseline.Probabilities.Count,
            baseline.OpponentHandCount,
            baseline.OpponentDeckCount);
    }

    /// <summary>
    /// The estimator must be a pure function: same input, same output, in any
    /// order, with no state carried between calls.
    /// </summary>
    [Test]
    public void RepeatedEstimatesAreIdentical()
    {
        var catalog = LoadCatalog();
        var pool = PoolOf(Deck("wood"));
        var categories = CategoriesOf(catalog);
        var snapshot = Snapshot(9, 1, 5, 4, 15, new[] { Card("wood_bark", 1) }, new[] { Card("wood_druid", 0) });

        var first = ThreatEstimator.Estimate(snapshot, 1, pool, categories);
        var second = ThreatEstimator.Estimate(snapshot, 1, pool, categories);

        Assert.Multiple(() =>
        {
            Assert.That(second.UnknownPoolSize, Is.EqualTo(first.UnknownPoolSize));
            Assert.That(second.Probabilities.Count, Is.EqualTo(first.Probabilities.Count));
            for (var index = 0; index < first.Probabilities.Count; index++)
            {
                Assert.That(second.Probabilities[index].Probability, Is.EqualTo(first.Probabilities[index].Probability));
            }
        });
    }

    /// <summary>
    /// The estimator must not depend on an ambush it cannot see. An opponent
    /// ambush zone is hidden, so it is not even an input to the estimate.
    /// </summary>
    [Test]
    public void HiddenAmbushZoneIsNotAnInput()
    {
        var catalog = LoadCatalog();
        var pool = PoolOf(Deck("flame"));
        var categories = CategoriesOf(catalog);

        var plain = Snapshot(5, 0, 4, 3, 10, new[] { Card("flame_imp", 0) }, new[] { Card("flame_recruit", 1) });
        var withAmbush = Snapshot(5, 0, 4, 3, 10, new[] { Card("flame_imp", 0) }, new[] { Card("flame_recruit", 1) });
        withAmbush.Players[1].Ambush = new[] { Card("flame_ambush_seal", 1, 77) };

        var baseline = ThreatEstimator.Estimate(plain, 0, pool, categories);
        var altered = ThreatEstimator.Estimate(withAmbush, 0, pool, categories);

        Assert.That(
            altered.UnknownPoolSize,
            Is.EqualTo(baseline.UnknownPoolSize),
            "a hidden ambush must not enter the public accounting");
        Assert.That(
            altered.AccountedCopies,
            Is.EqualTo(baseline.AccountedCopies),
            "a hidden ambush must not be counted as a visible card");
    }

    // -------------------------------------------------------------- report

    [Test]
    public void ProbabilityUsesTheOpponentPublicHandAndPoolCounts()
    {
        var catalog = LoadCatalog();
        var pool = PoolOf(Deck("machine"));
        var categories = CategoriesOf(catalog);

        // hand 5, deck 15 -> pool 20, draw 5.
        var report = ThreatEstimator.Estimate(
            Snapshot(4, 0, 3, 5, 15, new[] { Card("wood_sapling", 0) }, new[] { Card("machine_gear", 1) }),
            0,
            pool,
            categories);

        // The machine deck declares 20 kinds x 3 = 60 copies, so the population
        // the opponent's hand is drawn from is 60 regardless of how many of those
        // copies the viewer can currently place.
        var declaredCopies = pool.Values.Sum();
        Assert.Multiple(() =>
        {
            Assert.That(report.OpponentHandCount, Is.EqualTo(5));
            Assert.That(report.OpponentDeckCount, Is.EqualTo(15));
            Assert.That(report.UnknownPoolSize, Is.EqualTo(declaredCopies));
            Assert.That(report.Probabilities, Is.Not.Empty, "the machine pool must expose at least one category");
            foreach (var probability in report.Probabilities)
            {
                Assert.That(probability.PoolSize, Is.EqualTo(declaredCopies));
                Assert.That(probability.HandSize, Is.EqualTo(5));
                Assert.That(probability.Probability, Is.InRange(0.0, 1.0));
                Assert.That(probability.UnseenCopies, Is.GreaterThan(0));
                Assert.That(probability.UnseenCopies, Is.LessThanOrEqualTo(declaredCopies));
            }
        });

        TestContext.Out.WriteLine("machine pool report at hand=5 deck=15 pool=20:");
        foreach (var probability in report.Probabilities)
        {
            TestContext.Out.WriteLine(
                "  {0,-12} unseen {1,3} copies -> {2,6:F2}%",
                probability.Kind,
                probability.UnseenCopies,
                100.0 * probability.Probability);
        }
    }

    /// <summary>
    /// The arithmetic is checked end to end on a case small enough to verify by
    /// hand: one category with exactly one unseen copy in a pool of five, a hand
    /// of two -> 2/5.
    /// </summary>
    [Test]
    public void ProbabilityIsVerifiableByHandOnASmallPool()
    {
        var categories = new Dictionary<string, IReadOnlyList<ThreatKind>>(StringComparer.Ordinal)
        {
            ["alpha"] = new[] { ThreatKind.MinionRemoval },
            ["beta"] = Array.Empty<ThreatKind>(),
        };
        var pool = new Dictionary<string, int>(StringComparer.Ordinal) { ["alpha"] = 1, ["beta"] = 4 };

        var report = ThreatEstimator.Estimate(
            Snapshot(3, 0, 0, 2, 3, Array.Empty<RuntimeCardSnapshot>(), new[] { Card("beta", 1) }),
            0,
            pool,
            categories);

        Assert.That(report.UnknownPoolSize, Is.EqualTo(5), "the pool declares 1 + 4 = 5 copies in total");
        var removal = report.For(ThreatKind.MinionRemoval);
        Assert.That(removal, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(removal!.UnseenCopies, Is.EqualTo(1));
            // 1 unseen copy in a population of 5, drawing a hand of 2: 2/5 = 0.4.
            Assert.That(
                removal.Probability,
                Is.EqualTo(0.4).Within(1e-12),
                "1 unseen copy in a declared population of 5 with a hand of 2 is 2/5");
        });
    }

    /// <summary>
    /// The classification is driven by ACTION **and TARGET**, because those are
    /// different capabilities. Two assertions here are the ones that matter: a
    /// single-target DAMAGE is removal rather than a sweep, and an ally-directed
    /// effect (HEAL, BUFF) is not a threat at all — lumping those in was what made
    /// the previous coarse "Protection" bucket the least accurate category.
    /// </summary>
    [Test]
    public void CategoriesSeparateByActionAndTarget()
    {
        IReadOnlyList<ThreatKind> Classify(string action, EffectTargetKind target)
            => ThreatEstimator.Classify(action, target);

        Assert.Multiple(() =>
        {
            // Removal: destroying or burning a single enemy asset.
            Assert.That(Classify("DESTROY", EffectTargetKind.SingleEnemy), Is.EqualTo(new[] { ThreatKind.MinionRemoval }));
            Assert.That(Classify("BANISH", EffectTargetKind.SingleEnemy), Is.EqualTo(new[] { ThreatKind.MinionRemoval }));
            Assert.That(
                Classify("DAMAGE", EffectTargetKind.SingleEnemy),
                Is.EqualTo(new[] { ThreatKind.MinionRemoval }),
                "single-target damage is one-asset removal, NOT a board sweep");

            // Sweep: the same action aimed at a whole side.
            Assert.That(
                Classify("DAMAGE", EffectTargetKind.AllEnemyMinions),
                Is.EqualTo(new[] { ThreatKind.BoardSweep, ThreatKind.FaceDamage }),
                "a whole-enemy-side sweep also presses the core in this game's model");
            Assert.That(Classify("DAMAGE", EffectTargetKind.AllMinions), Is.EqualTo(new[] { ThreatKind.BoardSweep }));
            Assert.That(Classify("DESTROY", EffectTargetKind.AllEnemyMinions), Is.EqualTo(new[] { ThreatKind.BoardSweep }));

            // Core pressure.
            Assert.That(Classify("DAMAGE", EffectTargetKind.EnemyFace), Is.EqualTo(new[] { ThreatKind.FaceDamage }));
            Assert.That(Classify("DAMAGE_CASTLE", EffectTargetKind.None), Is.EqualTo(new[] { ThreatKind.FaceDamage }));

            // Other threat axes.
            Assert.That(Classify("DISCARD_OPP_RANDOM", EffectTargetKind.None), Is.EqualTo(new[] { ThreatKind.Discard }));
            Assert.That(Classify("NEGATE", EffectTargetKind.None), Is.EqualTo(new[] { ThreatKind.Negation }));
            Assert.That(Classify("ENFEEBLE", EffectTargetKind.SingleEnemy), Is.EqualTo(new[] { ThreatKind.Disruption }));
            Assert.That(Classify("SUMMON", EffectTargetKind.None), Is.EqualTo(new[] { ThreatKind.Deployment }));

            // NOT threats: the opponent helping themselves.
            Assert.That(
                Classify("HEAL", EffectTargetKind.OwnSide),
                Is.EqualTo(new[] { ThreatKind.Other }),
                "healing the opponent's own minions takes nothing from me");
            Assert.That(
                Classify("BUFF", EffectTargetKind.OwnSide),
                Is.EqualTo(new[] { ThreatKind.Other }),
                "a buff is not a threat");
            Assert.That(
                Classify("GRANT_KEYWORD", EffectTargetKind.OwnSide),
                Is.EqualTo(new[] { ThreatKind.Other }),
                "granting a keyword to their own minion is not a threat");

            // Untargeted damage is not guessed at.
            Assert.That(
                Classify("DAMAGE", EffectTargetKind.None),
                Is.EqualTo(new[] { ThreatKind.Other }),
                "without a target, damage is not assumed to threaten a specific asset");
            Assert.That(
                Classify("DAMAGE", EffectTargetKind.OwnSide),
                Is.EqualTo(new[] { ThreatKind.Other }),
                "damage aimed at their own side is not a threat to me");
        });
    }

    [Test]
    public void CategoryListsDeduplicateAndAccumulate()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                ThreatEstimator.CategoriesOf(new[] { "BANISH", "DESTROY" }),
                Is.EqualTo(new[] { ThreatKind.MinionRemoval }),
                "two removal actions are one category");
            Assert.That(
                ThreatEstimator.CategoriesOf(new[] { "DESTROY", "DISCARD_OPP_RANDOM" }),
                Is.EqualTo(new[] { ThreatKind.MinionRemoval, ThreatKind.Discard }));
            Assert.That(
                ThreatEstimator.CategoriesOf(new[] { "ADD_ROOT", "BUFF", "DRAW" }),
                Is.EqualTo(new[] { ThreatKind.Other }),
                "non-threatening effects are kept visible as Other, never dropped");
        });
    }

    /// <summary>
    /// The production card pool must actually categorise, otherwise the whole
    /// estimate would be vacuously empty.
    /// </summary>
    [Test]
    public void ProductionPoolProducesCategoriesForEveryDeck()
    {
        var catalog = LoadCatalog();
        var categories = CategoriesOf(catalog);

        foreach (var faction in new[] { "flame", "machine", "sea", "wood" })
        {
            var pool = PoolOf(Deck(faction));
            var report = ThreatEstimator.Estimate(
                Snapshot(8, 0, 4, 5, 15, Array.Empty<RuntimeCardSnapshot>(), Array.Empty<RuntimeCardSnapshot>()),
                0,
                pool,
                categories);

            Assert.That(
                report.Probabilities,
                Is.Not.Empty,
                faction + " must expose at least one threat category or the estimator is vacuous");
            TestContext.Out.WriteLine(
                "{0}: {1} categories ({2})",
                faction,
                report.Probabilities.Count,
                string.Join(", ", report.Probabilities.Select(p => p.Kind + "=" + (100.0 * p.Probability).ToString("F1") + "%")));
        }
    }

    // -------------------------------------------------------- win distance

    [Test]
    public void WinDistanceUsesTheEngineReportedProgressAndAStatedRate()
    {
        var pool = PoolOf(Deck("machine"));
        var categories = CategoriesOf(LoadCatalog());

        // Turn 4, the opponent has pulled 2 of the 6 they need, at 0.5 pulls/turn.
        var report = ThreatEstimator.Estimate(
            Snapshot(4, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>(), Array.Empty<RuntimeCardSnapshot>()),
            0,
            pool,
            categories,
            new[]
            {
                new WinConditionInput(1, "PULL_TOTAL_GE", 6, 2, progressPerTurn: 0.5),
                new WinConditionInput(0, "GIANT_HEALTH_GE", 512, 300),
            });

        var opponent = report.DistanceFor(1);
        var viewer = report.DistanceFor(0);
        Assert.Multiple(() =>
        {
            Assert.That(opponent, Is.Not.Null);
            Assert.That(opponent!.Condition, Is.EqualTo("PULL_TOTAL_GE"));
            Assert.That(opponent.Remaining, Is.EqualTo(4));
            Assert.That(opponent.Current, Is.EqualTo(2));
            Assert.That(opponent.Required, Is.EqualTo(6));

            // Rate 2 pulls / 4 turns = 0.5 per turn; 4 remaining / 0.5 = 8 turns.
            Assert.That(opponent.RemainingTurns, Is.EqualTo(8), "2 pulls over 4 turns means 8 more turns for 4 pulls");

            // No viewer-safe rate exists for the giant-health axis here, so the
            // estimate must decline rather than invent a number.
            Assert.That(viewer, Is.Not.Null);
            Assert.That(viewer!.Remaining, Is.EqualTo(212));
            Assert.That(viewer.RemainingTurns, Is.Null, "a rate the estimator cannot know must be reported as unknown");
            Assert.That(viewer.UnmeasurableReason, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void WinDistanceIsZeroWhenTheThresholdIsAlreadyMet()
    {
        var report = ThreatEstimator.Estimate(
            Snapshot(10, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>(), Array.Empty<RuntimeCardSnapshot>()),
            0,
            PoolOf(Deck("machine")),
            CategoriesOf(LoadCatalog()),
            new[] { new WinConditionInput(1, "PULL_TOTAL_GE", 6, 6, progressPerTurn: 0.5) });

        var opponent = report.DistanceFor(1);
        Assert.That(opponent, Is.Not.Null);
        Assert.That(opponent!.Remaining, Is.EqualTo(0));
        Assert.That(opponent.RemainingTurns, Is.EqualTo(0));
    }

    [Test]
    public void MissingWinConditionsAreReportedNotInvented()
    {
        var report = ThreatEstimator.Estimate(
            Snapshot(4, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>(), Array.Empty<RuntimeCardSnapshot>()),
            0,
            PoolOf(Deck("wood")),
            CategoriesOf(LoadCatalog()));

        Assert.That(report.WinDistances, Is.Empty);
        Assert.That(
            report.Notes.Any(note => note.Contains("does not publish them", StringComparison.Ordinal)),
            Is.True,
            "the snapshot gap must be stated in the report, not silently ignored: " + string.Join(" | ", report.Notes));
    }

    // ------------------------------------------------------- board threat

    [Test]
    public void BoardThreatCountsOnlyTheOpponentBoard()
    {
        var opponentField = new[]
        {
            new RuntimeCardSnapshot { EntityId = 1, CardId = "flame_imp", OwnerPlayer = 1, CurrentAttack = 3 },
            new RuntimeCardSnapshot { EntityId = 2, CardId = "flame_berserker", OwnerPlayer = 1, CurrentAttack = 5 },
            new RuntimeCardSnapshot { EntityId = 3, CardId = "flame_strike", OwnerPlayer = 1, CurrentAttack = 0 },
        };

        var report = ThreatEstimator.Estimate(
            Snapshot(5, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>(), Array.Empty<RuntimeCardSnapshot>(), opponentField),
            0,
            PoolOf(Deck("flame")),
            CategoriesOf(LoadCatalog()));

        Assert.Multiple(() =>
        {
            Assert.That(report.BoardThreat.MinionCount, Is.EqualTo(3));
            Assert.That(report.BoardThreat.AttackSum, Is.EqualTo(8));
            Assert.That(report.BoardThreat.LargestAttack, Is.EqualTo(5));
            Assert.That(report.BoardThreat.CastleHealth, Is.EqualTo(75));
            Assert.That(report.BoardThreat.PlayerLife, Is.EqualTo(20));
        });
    }

    // ------------------------------------------------------- accounting

    [Test]
    public void VisibleCardsReduceTheUnseenPool()
    {
        var categories = new Dictionary<string, IReadOnlyList<ThreatKind>>(StringComparer.Ordinal)
        {
            ["gamma"] = new[] { ThreatKind.FaceDamage },
        };
        var pool = new Dictionary<string, int>(StringComparer.Ordinal) { ["gamma"] = 3 };

        var snapshot = Snapshot(3, 0, 0, 2, 3, Array.Empty<RuntimeCardSnapshot>(), new[] { Card("gamma", 1) });
        // Put one gamma in the OPPONENT's own public graveyard, where the viewer can see it.
        snapshot.Players[1].Graveyard = new[] { Card("gamma", 1, 50) };

        var report = ThreatEstimator.Estimate(snapshot, 0, pool, categories);
        var damage = report.For(ThreatKind.FaceDamage);

        Assert.That(damage, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(report.AccountedCopies, Is.EqualTo(2), "3 copies minus 1 the opponent has publicly spent");
            Assert.That(damage!.UnseenCopies, Is.EqualTo(2));
            Assert.That(report.UnknownPoolSize, Is.EqualTo(3), "the pool declares 3 gamma copies");
        });

        // With a declared list total of 3 and 1 accounted for, the accounting
        // invariant asks whether the 2 unplaced copies equal the hidden count the
        // public counters report (hand 2 + deck 3 = 5). They do not, and that
        // mismatch must be stated rather than hidden.
        Assert.That(
            report.Notes.Any(note => note.Contains("accounting mismatch", StringComparison.Ordinal)),
            Is.True,
            "the declared list and the public counters disagree here, and that must be stated: " + string.Join(" | ", report.Notes));
    }

    /// <summary>
    /// The viewer's own cards must NOT reduce what the opponent can be holding.
    ///
    /// This pins a defect that shipped in an earlier revision: the estimator
    /// subtracted the viewer's public zones and hand from the OPPONENT's declared
    /// pool, on the reasoning that a card the viewer holds cannot be in the
    /// opponent's deck. Each player draws from their own 60-card deck, so the same
    /// card id can sit in both hands simultaneously — measured over real engine
    /// matches, the two hands share a card id in 10028 (sample, card) pairs. The
    /// wrong deduction made the estimator certain the opponent held none of a card
    /// the opponent was in fact holding, and it only misfired in same-faction
    /// pairings, which is why it survived so long.
    /// </summary>
    [Test]
    public void ViewerOwnedCopiesDoNotReduceTheOpponentPool()
    {
        var categories = new Dictionary<string, IReadOnlyList<ThreatKind>>(StringComparer.Ordinal)
        {
            ["gamma"] = new[] { ThreatKind.FaceDamage },
        };
        var pool = new Dictionary<string, int>(StringComparer.Ordinal) { ["gamma"] = 3 };

        // The viewer holds all three of its own gamma copies; the opponent declares
        // three of its own and has published none of them. The opponent's unseen
        // count must stay at its full declared 3.
        var snapshot = Snapshot(3, 0, 0, 5, 5, Array.Empty<RuntimeCardSnapshot>(),
            new[] { Card("gamma", 1), Card("gamma", 2), Card("gamma", 3) });

        var report = ThreatEstimator.Estimate(snapshot, 0, pool, categories);
        var damage = report.For(ThreatKind.FaceDamage);

        Assert.Multiple(() =>
        {
            Assert.That(damage, Is.Not.Null, "the opponent's own three copies must remain a live threat");
            Assert.That(damage!.UnseenCopies, Is.EqualTo(3),
                "the viewer holding three gamma copies says nothing about the opponent's deck");
            Assert.That(report.AccountedCopies, Is.EqualTo(3));
            Assert.That(report.VisibleCopiesInPool, Is.Zero, "the opponent has published none of them");
        });

        // Viewer-owned cards in public zones must not be deducted either.
        snapshot.Players[0].Graveyard = new[] { Card("gamma", 1, 90), Card("gamma", 2, 91) };
        var withViewerGraveyard = ThreatEstimator.Estimate(snapshot, 0, pool, categories);
        Assert.That(withViewerGraveyard.For(ThreatKind.FaceDamage)!.UnseenCopies, Is.EqualTo(3),
            "a card in the VIEWER's graveyard is not a copy the opponent has spent");
    }

    /// <summary>
    /// A card public record PROVES is in the opponent's hand must be treated as
    /// certain, not as an estimate.
    ///
    /// The engine path this models is COMMIT then ROLLBACK: a committed card sits in
    /// a public commit queue with its identity exposed, and `Rollback` returns it to
    /// its owner's hand (EffectRuntime.Mechanical.cs). Without this input the
    /// estimator treats the rolled-back card as simply gone from the deck, so it
    /// becomes MORE confident the opponent does not hold it — the opposite of the
    /// truth, for a card the viewer literally watched go into that hand.
    /// </summary>
    [Test]
    public void PublicHandRecordNarrowsThePoolAndSeparatesCertainFromScheduled()
    {
        var catalog = LoadCatalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var pool = PoolOf(Deck("machine"));

        var snapshot = Snapshot(4, 0, 5, 4, 12,
            new[] { Card("machine_drone", 0, 1) },
            new[] { Card("machine_piston", 1, 2) });

        var without = ThreatEstimator.Estimate(
            snapshot, 0, pool, categories, winConditions: null, tagsByCardId: tags);

        // A category the machine pool actually produces, so the comparison is not
        // vacuous. Negation is deliberately NOT used here: every machine_null copy in
        // this fixture is publicly visible, so that kind is absent from the report
        // entirely and asserting on it would test nothing.
        var withoutDamage = without.For(ThreatKind.MinionRemoval);
        Assert.That(withoutDamage, Is.Not.Null, "the fixture must produce this category or the test is vacuous");
        if (withoutDamage is null) return;
        var baselinePool = withoutDamage.PoolSize;
        var baselineExpected = withoutDamage.ExpectedCardsInHand;

        // --- one copy CERTAIN in hand ---------------------------------------
        var certain = new PublicHandRecord(
            certainInHand: new Dictionary<string, int>(StringComparer.Ordinal) { ["machine_drone"] = 1 });
        var withCertain = ThreatEstimator.Estimate(
            snapshot, 0, pool, categories, winConditions: null, tagsByCardId: tags,
            publicHand: certain);
        var certainDamage = withCertain.For(ThreatKind.MinionRemoval);

        Assert.Multiple(() =>
        {
            Assert.That(
                withCertain.Notes.Any(note => note.Contains("public record", StringComparison.Ordinal)),
                Is.True,
                "the estimator must state what public record gave it: " + string.Join(" | ", withCertain.Notes));
            Assert.That(certainDamage!.PoolSize, Is.EqualTo(baselinePool - 1),
                "a certain copy leaves the hidden pool");
            Assert.That(certainDamage.ExpectedCardsInHand, Is.GreaterThan(baselineExpected),
                "a certain in-hand copy raises the estimate: the same unseen count sits in a smaller pool");
            Assert.That(withCertain.UnknownPoolSize, Is.EqualTo(without.UnknownPoolSize),
                "the declared pool is unchanged; only the sampled population narrows");
        });

        // --- one copy only SCHEDULED for the hand ---------------------------
        var scheduled = new PublicHandRecord(
            scheduledForHand: new Dictionary<string, int>(StringComparer.Ordinal) { ["machine_drone"] = 1 });
        var withScheduled = ThreatEstimator.Estimate(
            snapshot, 0, pool, categories, winConditions: null, tagsByCardId: tags,
            publicHand: scheduled);
        var scheduledDamage = withScheduled.For(ThreatKind.MinionRemoval);

        Assert.Multiple(() =>
        {
            Assert.That(scheduledDamage!.PoolSize, Is.EqualTo(baselinePool - 1),
                "a scheduled copy also leaves the hidden pool: its destination is already fixed");
            Assert.That(scheduledDamage.ExpectedCardsInHand, Is.GreaterThan(baselineExpected),
                "a reserved place in the hand still leaves the remaining hidden cards no worse off: "
                + "the numerator and the denominator shrink together");

            // The crucial distinction: a copy that ARRIVES LATER must be visible as such,
            // or a consumer deciding what can be played NOW would treat a card three turns
            // away as a card already aimed at it. Asserted as a total across categories so
            // the check does not depend on which single category machine_drone happens to
            // fall into.
            Assert.That(
                withScheduled.Probabilities.Sum(p => p.ScheduledCopies),
                Is.EqualTo(1),
                "the scheduled copy must be reported as scheduled in exactly one category");
            Assert.That(
                withCertain.Probabilities.Sum(p => p.ScheduledCopies),
                Is.Zero,
                "a copy already in hand is not scheduled to arrive");
        });

        // --- the accounting invariant is deliberately NOT narrowed ----------
        Assert.Multiple(() =>
        {
            Assert.That(withCertain.AccountedCopies, Is.EqualTo(without.AccountedCopies),
                "the invariant reconciles the declared list, so hand-record cards must not be removed from it");
            Assert.That(withScheduled.AccountedCopies, Is.EqualTo(without.AccountedCopies));
        });

        // --- bad input is clamped, never trusted ----------------------------
        var absurd = new PublicHandRecord(
            certainInHand: new Dictionary<string, int>(StringComparer.Ordinal) { ["machine_drone"] = 999 });
        var clamped = ThreatEstimator.Estimate(
            snapshot, 0, pool, categories, winConditions: null, tagsByCardId: tags,
            publicHand: absurd);
        Assert.That(clamped.For(ThreatKind.MinionRemoval)!.PoolSize,
            Is.EqualTo(baselinePool - pool["machine_drone"]),
            "recorded copies are capped at the declared count");

        var foreign = new PublicHandRecord(
            certainInHand: new Dictionary<string, int>(StringComparer.Ordinal) { ["flame_imp"] = 1 });
        var withForeign = ThreatEstimator.Estimate(
            snapshot, 0, pool, categories, winConditions: null, tagsByCardId: tags,
            publicHand: foreign);
        Assert.That(withForeign.For(ThreatKind.MinionRemoval)!.PoolSize, Is.EqualTo(baselinePool),
            "a card the opponent never declared is not part of their pool");

        // An empty record must behave exactly as no record at all.
        var empty = ThreatEstimator.Estimate(
            snapshot, 0, pool, categories, winConditions: null, tagsByCardId: tags,
            publicHand: PublicHandRecord.Empty);
        Assert.That(empty.For(ThreatKind.MinionRemoval)!.ExpectedCardsInHand, Is.EqualTo(baselineExpected));
    }

    /// <summary>
    /// The estimator must DECLARE which readings are lower bounds rather than point
    /// estimates, and it must derive that from card data rather than from a list of
    /// card names.
    ///
    /// Measured over 3840 real engine samples, cards that can only be played while a
    /// condition holds wait in the hand instead of being spent, so the hand
    /// over-represents them: the retention ratio was 1.45 for Negation (all ambush
    /// NEGATEs) against 0.73-1.18 for every other category, and ExpectedCardsInHand
    /// understated the Negation hold count by 27%. No snapshot input can correct it,
    /// because the estimator sees the opponent's hand SIZE but never its contents,
    /// so the honest treatment is to flag the reading.
    /// </summary>
    [Test]
    public void ConditionallyPlayableCardsAreDeclaredAndDerivedFromData()
    {
        var catalog = LoadCatalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var pool = PoolOf(Deck("flame"));

        // Derived from the card data, not from a hardcoded list of names.
        var conditional = catalog.Cards
            .Where(pair => !string.IsNullOrEmpty(pair.Value.AmbushTrigger)
                || !string.IsNullOrEmpty(pair.Value.PunishCondition))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(conditional, Does.Contain("flame_ambush_seal"),
            "an ambush card declares a trigger, so it must be in the derived set");

        var snapshot = Snapshot(4, 0, 5, 4, 12,
            new[] { Card("flame_recruit", 0, 1) },
            new[] { Card("flame_imp", 1, 2) });
        var report = ThreatEstimator.Estimate(
            snapshot, 0, pool, categories, winConditions: null, tagsByCardId: tags,
            conditionallyPlayableCardIds: conditional);

        var negation = report.For(ThreatKind.Negation);
        Assert.That(negation, Is.Not.Null);
        Assert.That(negation!.HasConditionallyPlayableCards, Is.True,
            "Negation is produced only by ambush NEGATE cards, so the reading must be flagged as a lower bound");

        // With no flag data supplied the estimator must not claim the reading is exact
        // by silently defaulting to false in a way a caller could mistake for a fact:
        // it simply has no basis to flag, and the property reflects that.
        var unflagged = ThreatEstimator.Estimate(
            snapshot, 0, pool, categories, winConditions: null, tagsByCardId: tags);
        Assert.That(unflagged.For(ThreatKind.Negation)!.HasConditionallyPlayableCards, Is.False,
            "without card data the estimator cannot know, so it must not invent the flag");

        // The flag must not change the arithmetic: it is a caveat on the reading, not
        // a correction of it.
        Assert.Multiple(() =>
        {
            Assert.That(negation.UnseenCopies, Is.EqualTo(unflagged.For(ThreatKind.Negation)!.UnseenCopies));
            Assert.That(negation.ExpectedCardsInHand,
                Is.EqualTo(unflagged.For(ThreatKind.Negation)!.ExpectedCardsInHand));
            Assert.That(negation.Probability,
                Is.EqualTo(unflagged.For(ThreatKind.Negation)!.Probability));
        });
    }

    /// <summary>
    /// Expected copies in hand is the hypergeometric mean, so it is checkable in
    /// closed form. This is the metric a decision should use, because "at least
    /// one" saturates near 1 whenever the hand is a large share of the pool.
    /// </summary>
    [Test]
    public void ExpectedCopiesInHandIsTheHypergeometricMean()
    {
        Assert.Multiple(() =>
        {
            // 15 successes in a 60-card pool, drawing 5: 15 * 5/60 = 1.25.
            Assert.That(ThreatEstimator.ExpectedCopiesInHand(60, 15, 5), Is.EqualTo(1.25).Within(1e-12));
            Assert.That(ThreatEstimator.ExpectedCopiesInHand(20, 1, 2), Is.EqualTo(0.1).Within(1e-12));
            Assert.That(ThreatEstimator.ExpectedCopiesInHand(20, 20, 5), Is.EqualTo(5.0).Within(1e-12),
                "every card is a success, so all five drawn cards are");
            Assert.That(ThreatEstimator.ExpectedCopiesInHand(20, 0, 5), Is.EqualTo(0.0));
            Assert.That(ThreatEstimator.ExpectedCopiesInHand(20, 5, 0), Is.EqualTo(0.0));

            // The mean must stay below the "at least one" probability's ceiling,
            // which is why it discriminates where the probability does not.
            var probability = ThreatEstimator.AtLeastOneProbability(60, 15, 5);
            Assert.That(probability, Is.GreaterThan(0.7), "at least one is high here");
            Assert.That(
                ThreatEstimator.ExpectedCopiesInHand(60, 15, 5),
                Is.LessThan(2.0),
                "and yet the opponent is expected to hold barely more than one such card");
        });
    }

    [Test]
    public void ExpectedCopiesInHandRejectsImpossibleInputs()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ThreatEstimator.ExpectedCopiesInHand(-1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ThreatEstimator.ExpectedCopiesInHand(10, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ThreatEstimator.ExpectedCopiesInHand(10, 0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ThreatEstimator.ExpectedCopiesInHand(10, 11, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ThreatEstimator.ExpectedCopiesInHand(10, 1, 11));
        });
    }

    /// <summary>
    /// The per-turn bound: tags may each be used once per turn, so a category
    /// spanning N distinct tag groups cannot put more than N of its cards on the
    /// table this turn, however many copies the opponent holds.
    /// </summary>
    [Test]
    public void ExpectedPlayablePerTurnIsBoundedByDistinctTagGroups()
    {
        var catalog = LoadCatalog();
        var tags = TagsOf(catalog);
        var categories = CategoriesOf(catalog);
        var pool = PoolOf(Deck("sea"));

        var report = ThreatEstimator.Estimate(
            Snapshot(6, 0, 4, 6, 14, Array.Empty<RuntimeCardSnapshot>(), Array.Empty<RuntimeCardSnapshot>()),
            0,
            pool,
            categories,
            winConditions: null,
            tagsByCardId: tags);

        Assert.That(report.Probabilities, Is.Not.Empty);
        foreach (var probability in report.Probabilities)
        {
            Assert.That(
                probability.ExpectedPlayableThisTurn,
                Is.LessThanOrEqualTo(probability.ExpectedCardsInHand + 1e-12),
                probability.Kind + ": playable cannot exceed held");
            Assert.That(
                probability.ExpectedPlayableThisTurn,
                Is.GreaterThanOrEqualTo(0.0));
        }

        TestContext.Out.WriteLine("sea pool at hand=6 deck=14 (hold probability vs expected in hand vs playable/turn):");
        foreach (var probability in report.Probabilities)
        {
            TestContext.Out.WriteLine(
                "  {0,-12} P(hold one)={1,6:F2}%  expected in hand={2,5:F2}  playable this turn={3,5:F2}",
                probability.Kind,
                100.0 * probability.Probability,
                probability.ExpectedCardsInHand,
                probability.ExpectedPlayableThisTurn);
        }
    }

    /// <summary>
    /// P(at least one) and E[count] are different aggregations of the same
    /// hypergeometric, and the ONLY fixed relation is
    /// `P = E[min(X, 1)] <= E[X]`.
    ///
    /// This test exists because an earlier version of the suite asserted the
    /// inequality the WRONG WAY ROUND (`P >= E`) and it was false: for a broad
    /// category the expected count exceeds 1 while a probability cannot exceed 1.
    /// The assertion below is deliberately the sharp direction, and it is checked
    /// against the closed-form mean so a silent regression cannot hide.
    /// </summary>
    [Test]
    public void HoldProbabilityIsNeverAboveTheExpectedCount()
    {
        var catalog = LoadCatalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);

        var report = ThreatEstimator.Estimate(
            Snapshot(6, 0, 4, 5, 15, Array.Empty<RuntimeCardSnapshot>(), Array.Empty<RuntimeCardSnapshot>()),
            0,
            PoolOf(Deck("machine")),
            categories,
            winConditions: null,
            tagsByCardId: tags);

        Assert.That(report.Probabilities, Is.Not.Empty);
        var high = 0;
        var aboveOne = 0;
        foreach (var probability in report.Probabilities)
        {
            Assert.That(probability.Probability, Is.InRange(0.0, 1.0), probability.Kind + ": probabilities are bounded");
            Assert.That(
                probability.Probability,
                Is.LessThanOrEqualTo(probability.ExpectedCardsInHand + 1e-12),
                probability.Kind + ": P(at least one) = E[min(X,1)] can never exceed E[X]");
            Assert.That(
                probability.ExpectedCardsInHand,
                Is.LessThanOrEqualTo(probability.HandSize + 1e-12),
                probability.Kind + ": the opponent cannot hold more category copies than cards");

            // Cross-check the reported mean against the closed form, so a wrong
            // scaling cannot pass unnoticed.
            var closedForm = ThreatEstimator.ExpectedCopiesInHand(
                probability.PoolSize,
                probability.UnseenCopies,
                probability.HandSize);
            Assert.That(
                probability.ExpectedCardsInHand,
                Is.EqualTo(closedForm).Within(0.5),
                probability.Kind + ": the reported mean must match the hypergeometric mean");

            if (probability.Probability > 0.7) high++;
            if (probability.ExpectedCardsInHand > 1.0) aboveOne++;
        }

        Assert.That(
            high,
            Is.GreaterThan(0),
            "a broad category reaches a high hold probability at 5 cards of 60, which is why the expected count is the usable metric");
        Assert.That(
            aboveOne,
            Is.GreaterThan(0),
            "and at least one category is expected to occupy more than one hand slot");

        TestContext.Out.WriteLine(
            "machine pool at hand=5/60: [{0}]  (P>0.7: {1}, E>1: {2})",
            string.Join(", ", report.Probabilities.Select(p =>
                p.Kind + " P=" + p.Probability.ToString("F3", CultureInfo.InvariantCulture)
                + " E=" + p.ExpectedCardsInHand.ToString("F2", CultureInfo.InvariantCulture))),
            high,
            aboveOne);
    }

    /// <summary>
    /// The Board category is carried by exactly one machine card
    /// (machine_factory, via SUMMON). A large opponent pool must still report it:
    /// a category whose cards are simply late in a big deck is still a real
    /// possibility, and dropping it would silently understate what the opponent
    /// can bring down.
    /// </summary>
    [Test]
    public void BoardCategorySurvivesALargeOpponentPool()
    {
        var catalog = LoadCatalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var pool = PoolOf(Deck("machine"));

        // Confirm the fixture assumption first: machine_factory is the only
        // machine deck card with a Board category.
        var boardCards = categories.Where(p => p.Value.Contains(ThreatKind.Deployment)).Select(p => p.Key).ToArray();
        Assert.That(
            boardCards,
            Does.Contain("machine_factory"),
            "precondition: machine_factory must categorise as Board");

        // An opponent holding a big pool: hand 15 + deck 42 = 57 unseen.
        var report = ThreatEstimator.Estimate(
            Snapshot(5, 0, 8, 15, 42, Array.Empty<RuntimeCardSnapshot>(), Array.Empty<RuntimeCardSnapshot>()),
            0,
            pool,
            categories,
            winConditions: null,
            tagsByCardId: tags);

        TestContext.Out.WriteLine(
            "machine pool, poolSize={0}: categories=[{1}]",
            report.UnknownPoolSize,
            string.Join(", ", report.Probabilities.Select(p => p.Kind + ":" + p.Probability.ToString("F3", CultureInfo.InvariantCulture))));

        var board = report.For(ThreatKind.Deployment);
        Assert.That(
            board,
            Is.Not.Null,
            "Board must be reported even when the opponent pool is large; categories=["
            + string.Join(", ", report.Probabilities.Select(p => p.Kind.ToString())) + "]");
        Assert.That(board!.UnseenCopies, Is.EqualTo(3), "all three machine_factory copies are unseen");
        Assert.That(board.Probability, Is.GreaterThan(0.0));

        // The accounting identity that the calibration probe was misreading:
        // `accounted` sums the unseen copies across the supplied pool, so it is
        // bounded by the pool total, NOT forced to equal the public hand+deck
        // count (other zones hold the difference).
        Assert.Multiple(() =>
        {
            Assert.That(
                report.AccountedCopies,
                Is.LessThanOrEqualTo(60),
                "accounted cannot exceed the supplied pool total");
            Assert.That(
                report.AccountedCopies,
                Is.GreaterThanOrEqualTo(report.OpponentHandCount),
                "at least the opponent's hand is unaccounted for");
        });
    }

    /// <summary>
    /// Reproduces the calibration's residual miss in a controlled fixture: the
    /// opponent's hand CONTAINS machine_factory (a Board card), one copy of it is
    /// publicly visible in their graveyard, and the rest of the machine pool is
    /// untouched. Board must still be reported.
    /// </summary>
    [Test]
    public void BoardIsReportedWhenOneCopyIsPubliclyVisible()
    {
        var catalog = LoadCatalog();
        var categories = CategoriesOf(catalog);
        var tags = TagsOf(catalog);
        var pool = PoolOf(Deck("machine"));

        var opponentHand = new[] { Card("machine_factory", 1, 5) };
        var snapshot = Snapshot(5, 0, 8, 1, 56, Array.Empty<RuntimeCardSnapshot>(), opponentHand);
        snapshot.Players[1].Graveyard = new[] { Card("machine_factory", 1, 50) };

        var report = ThreatEstimator.Estimate(
            snapshot, 0, pool, categories, winConditions: null, tagsByCardId: tags);

        TestContext.Out.WriteLine(
            "machine_factory visible=1, pool={0}: categories=[{1}]",
            report.UnknownPoolSize,
            string.Join(", ", report.Probabilities.Select(p => p.Kind + ":" + p.UnseenCopies + ":" + p.Probability.ToString("F3", CultureInfo.InvariantCulture))));

        var board = report.For(ThreatKind.Deployment);
        Assert.That(
            board,
            Is.Not.Null,
            "Board must survive one publicly visible copy; categories=["
            + string.Join(", ", report.Probabilities.Select(p => p.Kind.ToString())) + "]");
        Assert.That(board!.UnseenCopies, Is.EqualTo(2), "3 copies minus 1 in the graveyard");
    }

    // -------------------------------------------------------- win progress

    /// <summary>
    /// Win conditions are DERIVED from public information: the leader sits in a
    /// visible leader zone, so its identity names its printed win condition. This
    /// test pins each axis the snapshot can actually measure, and — just as
    /// importantly — that the axes it CANNOT measure come back as unmeasured with
    /// a reason rather than as zero progress.
    /// </summary>
    [Test]
    public void WinProgressIsDerivedFromTheVisibleLeaderAndPublicCounters()
    {
        var catalog = LoadCatalog();
        var conditions = LeaderConditionsOf(catalog);

        var snapshot = Snapshot(9, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>(), Array.Empty<RuntimeCardSnapshot>());
        // Player 0: wood leader, one sealed minion at 300 health.
        snapshot.Players[0].LeaderZone = new[] { Card("wood_leader", 0, 90) };
        snapshot.Players[0].Field = new[]
        {
            new RuntimeCardSnapshot { EntityId = 91, CardId = "wood_treant", OwnerPlayer = 0, Sealed = true, CurrentHealth = 300 },
            new RuntimeCardSnapshot { EntityId = 92, CardId = "wood_sapling", OwnerPlayer = 0, Sealed = false, CurrentHealth = 4 },
        };
        // Player 1: machine leader, 4 downloads done.
        snapshot.Players[1].LeaderZone = new[] { Card("machine_leader", 1, 95) };
        snapshot.Players[1].PullCount = 4;

        var progress = ThreatEstimator.DeriveWinProgress(snapshot, 0, conditions);

        Assert.That(progress, Has.Count.EqualTo(2));
        var mine = progress[0];
        var theirs = progress[1];

        Assert.Multiple(() =>
        {
            Assert.That(mine.Condition, Is.EqualTo("GIANT_HEALTH_GE"));
            Assert.That(mine.Required, Is.EqualTo(512));
            Assert.That(mine.Current, Is.EqualTo(300), "the SEALED minion at 300, not the unsealed one at 4");
            Assert.That(mine.Remaining, Is.EqualTo(212));
            Assert.That(mine.RemainingTurns, Is.Null, "no per-turn rate is published, so a turn estimate would be invented");
            Assert.That(mine.UnmeasurableReason, Is.Not.Null.And.Not.Empty);
            Assert.That(mine.IsFullyMeasured, Is.True, "condition, threshold and progress are all known");

            Assert.That(theirs.Condition, Is.EqualTo("PULL_TOTAL_GE"));
            Assert.That(theirs.Required, Is.EqualTo(6));
            Assert.That(theirs.Current, Is.EqualTo(4));
            Assert.That(theirs.Remaining, Is.EqualTo(2));
            Assert.That(theirs.RemainingTurns, Is.Null);
        });

        TestContext.Out.WriteLine(string.Join("\n", progress.Select(p => "  " + p)));
    }

    /// <summary>
    /// The axes whose counters the snapshot publishes must now be measured, and a
    /// genuine zero must be a zero. This test previously asserted the opposite —
    /// that the sea leader's discard axis was unmeasurable — because the v1.31
    /// snapshot did not publish TotalDiscarded. It now does, so the assertion is
    /// inverted to pin the fix.
    /// </summary>
    [Test]
    public void PublishedWinCountersAreMeasuredNotUnknown()
    {
        var catalog = LoadCatalog();
        var conditions = LeaderConditionsOf(catalog);

        var snapshot = Snapshot(9, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>(), Array.Empty<RuntimeCardSnapshot>());
        snapshot.Players[0].LeaderZone = new[] { Card("sea_leader", 0, 90) };
        snapshot.Players[1].LeaderZone = new[] { Card("machine_alpha", 1, 95) };
        snapshot.Players[1].TotalDiscarded = 7;

        var progress = ThreatEstimator.DeriveWinProgress(snapshot, 0, conditions);
        var sea = progress[0];
        var machine = progress[1];

        Assert.Multiple(() =>
        {
            Assert.That(sea.Condition, Is.EqualTo("OPP_DISCARD_TOTAL_GE"));
            Assert.That(sea.Required, Is.EqualTo(18));
            Assert.That(sea.Current, Is.EqualTo(7), "the opponent's TotalDiscarded is now published");
            Assert.That(sea.Remaining, Is.EqualTo(11));
            Assert.That(sea.IsFullyMeasured, Is.True);
            // A turn count still needs a RATE, and no rate is published.
            Assert.That(sea.RemainingTurns, Is.Null, "progress is known, but a per-turn rate is not");
            Assert.That(sea.UnmeasurableReason, Does.Contain("rate"));

            Assert.That(machine.Condition, Is.EqualTo("PULL_TOTAL_GE"));
            Assert.That(machine.Current, Is.EqualTo(0), "a published counter of 0 is a real zero");
            Assert.That(machine.IsFullyMeasured, Is.True);
        });

        TestContext.Out.WriteLine(string.Join("\n", progress.Select(p => "  " + p)));
    }

    /// <summary>
    /// A side with no visible leader, or a leader the caller supplied no data for,
    /// must be declared unknown rather than silently skipped or assumed harmless.
    /// </summary>
    [Test]
    public void UnknownLeadersAreDeclaredRatherThanSkipped()
    {
        var catalog = LoadCatalog();
        var conditions = LeaderConditionsOf(catalog);

        var snapshot = Snapshot(3, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>(), Array.Empty<RuntimeCardSnapshot>());
        // No leader zones at all.
        var progress = ThreatEstimator.DeriveWinProgress(snapshot, 0, conditions);

        Assert.That(progress, Has.Count.EqualTo(2), "both sides are always reported, even when unknown");
        Assert.Multiple(() =>
        {
            foreach (var entry in progress)
            {
                Assert.That(entry.Condition, Is.Null);
                Assert.That(entry.IsFullyMeasured, Is.False);
                Assert.That(entry.UnmeasurableReason, Does.Contain("no leader card"));
            }
        });

        // Now a leader IS visible and the catalog does carry its condition, so it
        // must resolve rather than stay unknown.
        snapshot.Players[1].LeaderZone = new[] { Card("flame_leader", 1, 95) };
        var withLeader = ThreatEstimator.DeriveWinProgress(snapshot, 0, conditions);
        Assert.That(withLeader[1].Condition, Is.EqualTo("ROYAL_CASTLE_BREAK"));

        // A leader whose condition the caller did NOT supply cannot be resolved,
        // and that must be stated by name rather than silently treated as harmless.
        var empty = new Dictionary<string, LeaderWinCondition>(StringComparer.Ordinal);
        var unsupplied = ThreatEstimator.DeriveWinProgress(snapshot, 0, empty);
        Assert.That(unsupplied[1].Condition, Is.Null);
        Assert.That(unsupplied[1].UnmeasurableReason, Does.Contain("flame_leader"));
    }

    /// <summary>
    /// ROYAL_CASTLE_BREAK is not a counter axis: it is won by breaking the shared
    /// castle, so progress is the castle's remaining health. This pins that the
    /// castle is read rather than reported as an unknown counter.
    /// </summary>
    [Test]
    public void CastleBreakConditionReadsTheCastleHealth()
    {
        var catalog = LoadCatalog();
        var conditions = LeaderConditionsOf(catalog);

        var snapshot = Snapshot(7, 0, 4, 3, 12, Array.Empty<RuntimeCardSnapshot>(), Array.Empty<RuntimeCardSnapshot>(), castleHealth: 41);
        snapshot.Players[0].LeaderZone = new[] { Card("flame_leader", 0, 90) };

        var progress = ThreatEstimator.DeriveWinProgress(snapshot, 0, conditions);
        var flame = progress[0];

        Assert.Multiple(() =>
        {
            Assert.That(flame.Condition, Is.EqualTo("ROYAL_CASTLE_BREAK"));
            Assert.That(flame.Current, Is.EqualTo(41), "the castle's remaining health");
            Assert.That(flame.ProgressSource, Does.Contain("Castle.Health"));
            Assert.That(flame.IsFullyMeasured, Is.True);
        });
    }

    [Test]
    public void WinProgressRejectsMalformedInputs()
    {
        var snapshot = Snapshot(3, 0, 2, 2, 3, Array.Empty<RuntimeCardSnapshot>(), Array.Empty<RuntimeCardSnapshot>());
        var conditions = new Dictionary<string, LeaderWinCondition>(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => ThreatEstimator.DeriveWinProgress(null!, 0, conditions));
            Assert.Throws<ArgumentNullException>(() => ThreatEstimator.DeriveWinProgress(snapshot, 0, null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => ThreatEstimator.DeriveWinProgress(snapshot, 2, conditions));
        });
    }

    /// <summary>Leader card id to win condition, read from the public catalog.</summary>
    private static IReadOnlyDictionary<string, LeaderWinCondition> LeaderConditionsOf(CardCatalog catalog)
    {
        var map = new Dictionary<string, LeaderWinCondition>(StringComparer.Ordinal);
        foreach (var pair in catalog.Cards)
        {
            var definition = pair.Value;
            if (!definition.IsLeader) continue;
            if (string.IsNullOrEmpty(definition.LeaderWinCondition)) continue;
            map[pair.Key] = new LeaderWinCondition(pair.Key, definition.LeaderWinCondition, definition.LeaderWinParam);
        }

        Assert.That(map, Is.Not.Empty, "precondition: the catalog must publish leader win conditions");
        return map;
    }

    [Test]
    public void RejectsMalformedInputs()
    {
        var pool = PoolOf(Deck("wood"));
        var categories = CategoriesOf(LoadCatalog());
        var snapshot = Snapshot(3, 0, 2, 2, 3, Array.Empty<RuntimeCardSnapshot>(), Array.Empty<RuntimeCardSnapshot>());

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => ThreatEstimator.Estimate(null!, 0, pool, categories));
            Assert.Throws<ArgumentNullException>(() => ThreatEstimator.Estimate(snapshot, 0, null!, categories));
            Assert.Throws<ArgumentNullException>(() => ThreatEstimator.Estimate(snapshot, 0, pool, null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => ThreatEstimator.Estimate(snapshot, 2, pool, categories));
            Assert.Throws<ArgumentOutOfRangeException>(() => ThreatEstimator.Estimate(snapshot, -1, pool, categories));
        });
    }

    /// <summary>
    /// A one-player snapshot cannot describe an opponent, so it is refused
    /// rather than answered with a guess.
    /// </summary>
    [Test]
    public void RejectsASnapshotWithoutTwoPlayers()
    {
        var snapshot = Snapshot(3, 0, 2, 2, 3, Array.Empty<RuntimeCardSnapshot>(), Array.Empty<RuntimeCardSnapshot>());
        snapshot.Players = new[] { snapshot.Players[0] };

        Assert.Throws<ArgumentException>(() => ThreatEstimator.Estimate(
            snapshot,
            0,
            PoolOf(Deck("wood")),
            CategoriesOf(LoadCatalog())));
    }
}

}
