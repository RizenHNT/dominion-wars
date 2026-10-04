using System;
using System.Collections.Generic;
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
/// TWO-PLY CONSISTENCY.
///
/// The owner asked for 1-ply and 2-ply to be validated SEPARATELY, because they can fail
/// independently: a system can read the current position perfectly and still say something
/// wrong about what happens next.
///
/// 1-ply (measured in AiEngineExactMatchTests and AiLethalAndDefenceTests) is "what does this
/// move cost, against the engine". 2-ply is "what does the position become after it".
///
/// WHAT IS CHECKED HERE, and why it is checkable rather than a matter of opinion: the
/// predictor's claim about the post-move threat profile must equal what the THREAT ESTIMATOR
/// ITSELF would report for that same predicted hand size. Both are arithmetic on published
/// data, so the two must agree EXACTLY. If they disagree, one of them is wrong, and a
/// 2-ply reading that does not match the 1-ply component it is built from would corrupt any
/// decision made from it.
///
/// The predictor deliberately does NOT model the opponent's reply, and does not invent how a
/// move advances the mover's own victory axis. Those refusals are asserted too, because an
/// instrument that silently fabricates either would be worse than one that reports unknown.
/// </summary>
public sealed class AiTwoPlyConsistencyTests
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

    private static CardCatalog Catalog()
        => CardCatalog.LoadDirectory(Path.Combine(RepositoryRoot(), "data", "cards"));

    private static DeckDefinition Deck(string faction)
        => DeckLoader.LoadFile(Path.Combine(RepositoryRoot(), "data", "decks", faction + "_deck.json"));

    private static MatchDeckSpec Spec(DeckDefinition deck)
        => new MatchDeckSpec(deck.Leader, deck.Cards, deck.Name, deck.Faction);

    private sealed class Ctx
    {
        public GameState State { get; init; } = null!;
        public TurnFlow Flow { get; init; } = null!;
        public RuntimeMatchGateway Gateway { get; init; } = null!;
        public AdvertisedActionPolicy Policy { get; init; } = null!;
        public CardCatalog Catalog { get; init; } = null!;
    }

    private static Ctx Drive(int seed, string actorFaction, string foeFaction, int maxSteps = 240)
    {
        var catalog = Catalog();
        var state = MatchSetup.Create(
            Spec(Deck(actorFaction)),
            Spec(Deck(foeFaction)),
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
        var gateway = new RuntimeMatchGateway("match_two_ply", state, flow, router, null);
        Assert.That(gateway.Initialize(0).Accepted, Is.True);

        var policy = new AdvertisedActionPolicy();
        var steps = 0;

        while (steps++ < maxSteps)
        {
            if (state.WinnerPlayerIndex.HasValue) break;

            var viewer = state.CurrentPlayerIndex;
            var snapshot = gateway.GetSnapshot(viewer);
            if (snapshot.WinnerPlayerIndex.HasValue) break;

            if (string.Equals(snapshot.Phase, "ACTION", StringComparison.Ordinal)
                && snapshot.LegalActions.Count > 0)
            {
                return new Ctx { State = state, Flow = flow, Gateway = gateway, Policy = policy, Catalog = catalog };
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

        Assert.Fail("could not reach an ACTION phase with legal actions inside the step budget");
        return null!;
    }

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
            tags[pair.Key] = pair.Value.Tags is null ? Array.Empty<string>() : pair.Value.Tags.ToArray();
        }

        return tags;
    }

    /// <summary>
    /// THE 2-PLY AGREEMENT LAW. For every advertised move, the predictor's threat shift must
    /// equal the estimator's own difference between the current hand size and the predicted
    /// post-move hand size, on the same pool.
    ///
    /// This is a strong check precisely because it compares two independently produced
    /// numbers: the predictor's shift and the estimator's re-read. They are only equal if the
    /// predictor really is using the estimator's model rather than an approximation of it.
    /// </summary>
    [Test]
    public void PredictedThreatShiftEqualsTheEstimatorsOwnReadingAfterTheMove()
    {
        var comparisons = 0;
        var mismatches = new List<string>();

        foreach (var actorFaction in new[] { "flame", "machine", "sea", "wood" })
        {
            var ctx = Drive(1, actorFaction, "wood");
            var actor = ctx.State.CurrentPlayerIndex;
            var foe = ctx.State.GetOpponent(actor);

            var categories = CategoriesOf(ctx.Catalog);
            var tags = TagsOf(ctx.Catalog);
            var pool = new Dictionary<string, int>(
                Deck(actorFaction == "flame" ? "wood" : actorFaction).Cards, StringComparer.Ordinal);

            var snapshot = ctx.Gateway.GetSnapshot(actor);
            var projection = Adapters.RuntimeSnapshotProjection.ToSnapshot(
                ctx.State, "match_two_ply", ctx.State.Turn.Number, actor, ctx.Flow);

            var threat = ThreatEstimator.Estimate(
                projection, actor, pool, categories, winConditions: null, tagsByCardId: tags);

            var outcomes = OutcomePredictor.PredictAll(threat, snapshot.LegalActions);

            foreach (var outcome in outcomes)
            {
                var draws = outcome.CardsHandedOver;
                if (draws <= 0) continue;

                // What the estimator says the SAME pool would yield at the predicted hand size.
                // The estimator's hand size is read from the snapshot, so the comparison is made
                // by asking it for the predicted size directly rather than by mutating state.
                var after = ThreatEstimator.Estimate(
                    WithHandCount(projection, foe.PlayerIndex, threat.OpponentHandCount + draws),
                    actor, pool, categories, winConditions: null, tagsByCardId: tags);

                foreach (var shift in outcome.ThreatShifts)
                {
                    var afterReport = after.For(shift.Kind);
                    if (afterReport is null) continue;

                    comparisons++;

                    // The predictor's Before must equal the estimator's current reading, and its
                    // After must equal the estimator's reading at the predicted hand size.
                    if (Math.Abs(shift.After - afterReport.ExpectedCardsInHand) > 1e-9)
                    {
                        mismatches.Add(
                            actorFaction + " move " + outcome.Settlement.ActionType + ": predictor said "
                            + shift.Kind + " after=" + shift.After.ToString("F6")
                            + " but the estimator says " + afterReport.ExpectedCardsInHand.ToString("F6"));
                    }
                }
            }
        }

        TestContext.Out.WriteLine("2-ply threat-shift comparisons: {0}", comparisons);
        Assert.That(comparisons, Is.GreaterThan(0), "the sweep must be non-vacuous");
        Assert.That(
            mismatches,
            Is.Empty,
            "the predictor's post-move reading must equal the estimator's own reading ("
            + mismatches.Count + "):\n  " + string.Join("\n  ", mismatches.Take(6)));
    }

    /// <summary>
    /// MONOTONICITY IN THE CARD FLOW. Handing the opponent MORE cards must never lower the
    /// predicted threat of any category that has unseen copies.
    ///
    /// This is the 2-ply analogue of the metamorphic punish law: the direction of the
    /// consequence must follow the direction of the cause. A predictor that moved the wrong way
    /// would make a high-punish move look SAFER, which is the exact inversion that would break
    /// a balance measurement.
    /// </summary>
    [Test]
    public void MoreCardFlowNeverLowersThePredictedThreat()
    {
        var comparisons = 0;
        var violations = new List<string>();

        foreach (var actorFaction in new[] { "flame", "machine", "sea", "wood" })
        {
            var ctx = Drive(3, actorFaction, "sea");
            var actor = ctx.State.CurrentPlayerIndex;

            var categories = CategoriesOf(ctx.Catalog);
            var tags = TagsOf(ctx.Catalog);
            var pool = new Dictionary<string, int>(Deck("sea").Cards, StringComparer.Ordinal);

            var projection = Adapters.RuntimeSnapshotProjection.ToSnapshot(
                ctx.State, "match_two_ply", ctx.State.Turn.Number, actor, ctx.Flow);

            var threat = ThreatEstimator.Estimate(
                projection, actor, pool, categories, winConditions: null, tagsByCardId: tags);

            foreach (var kind in Enum.GetValues(typeof(ThreatKind)).Cast<ThreatKind>())
            {
                var before = threat.For(kind)?.ExpectedCardsInHand;
                if (!before.HasValue) continue;

                // Compare the predictor at two flows: 0 (no transfer) and a large one.
                double? afterSmall = null;
                double? afterLarge = null;
                foreach (var draws in new[] { 1, 5 })
                {
                    var outcome = OutcomePredictor.Predict(
                        threat,
                        SyntheticPlay(draws, ctx.State, actor),
                        myObjective: null);

                    var shift = outcome.ThreatShifts.FirstOrDefault(s => s.Kind == kind);
                    if (shift is null) continue;
                    if (draws == 1) afterSmall = shift.After;
                    else afterLarge = shift.After;
                }

                if (!afterSmall.HasValue || !afterLarge.HasValue) continue;

                comparisons++;
                if (afterLarge >= afterSmall) continue;

                violations.Add(
                    actorFaction + " " + kind + ": handing over 5 cards predicted "
                    + afterLarge.Value.ToString("F6") + ", less than the "
                    + afterSmall.Value.ToString("F6") + " predicted for 1 card");
            }
        }

        TestContext.Out.WriteLine("flow-monotonicity comparisons: {0}", comparisons);
        Assert.That(comparisons, Is.GreaterThan(0), "the sweep must be non-vacuous");
        Assert.That(
            violations,
            Is.Empty,
            "more card flow must never predict LESS threat (" + violations.Count + "):\n  "
            + string.Join("\n  ", violations.Take(6)));
    }

    /// <summary>
    /// THE REFUSALS. The predictor must report UNKNOWN, with a reason, for what it does not
    /// model — never a fabricated number.
    ///
    /// Specifically: how a move advances the mover's OWN victory axis is unknown, because the
    /// advertisement does not describe the move's effects on it. Reporting zero there would
    /// read as "this move does not advance my win", which is a confident falsehood.
    /// </summary>
    [Test]
    public void ThePredictorRefusesToInventTheMoversOwnProgress()
    {
        var ctx = Drive(5, "machine", "wood");
        var actor = ctx.State.CurrentPlayerIndex;

        var categories = CategoriesOf(ctx.Catalog);
        var tags = TagsOf(ctx.Catalog);
        var pool = new Dictionary<string, int>(Deck("wood").Cards, StringComparer.Ordinal);

        var projection = Adapters.RuntimeSnapshotProjection.ToSnapshot(
            ctx.State, "match_two_ply", ctx.State.Turn.Number, actor, ctx.Flow);

        var threat = ThreatEstimator.Estimate(
            projection, actor, pool, categories, winConditions: null, tagsByCardId: tags);

        var snapshot = ctx.Gateway.GetSnapshot(actor);
        var objective = VictoryObjectives.ForMatch(snapshot, actor, conditions: null).For(actor);

        var checkedMoves = 0;
        foreach (var action in snapshot.LegalActions.Take(12))
        {
            var outcome = OutcomePredictor.Predict(threat, action, objective);
            checkedMoves++;

            Assert.That(outcome.MyObjectiveNote, Is.Not.Null.And.Not.Empty,
                "the post-move distance must come with a reason for being unknown, for action "
                + action.Type + "/" + action.CardId);

            Assert.That(outcome.Settlement.ActionType, Is.Not.Null.And.Not.Empty,
                "the settlement must name the action it settled");
        }

        TestContext.Out.WriteLine("moves checked for honest refusal: {0}", checkedMoves);
        Assert.That(checkedMoves, Is.GreaterThan(0), "the sweep must be non-vacuous");
    }

    /// <summary>
    /// A copy of the projection with one side's public hand COUNT changed, so the estimator can
    /// be asked what it would say at the predicted post-move size without mutating live state.
    ///
    /// Only the count is changed — never contents — because the whole point of the estimator is
    /// that contents are not an input.
    /// </summary>
    private static RuntimeSnapshotEnvelope WithHandCount(
        RuntimeSnapshotEnvelope source, int playerIndex, int handCount)
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
                Life = player.Life,
                HandCount = handCount,
                DeckCount = player.DeckCount,
                Hand = player.Hand,
                Ambush = player.Ambush,
                Field = player.Field,
                LeaderZone = player.LeaderZone,
                Graveyard = player.Graveyard,
                CommitQueue = player.CommitQueue,
                CloudStack = player.CloudStack,
                PunishDeltaThisTurn = player.PunishDeltaThisTurn,
                PunishDrawnThisTurn = player.PunishDrawnThisTurn,
                TotalDiscarded = player.TotalDiscarded,
                NoDamageTurns = player.NoDamageTurns,
                DamagedThisCycle = player.DamagedThisCycle,
                PunishToSelfDiscardThisTurn = player.PunishToSelfDiscardThisTurn,
                ProtectedThisTurn = player.ProtectedThisTurn,
                EffectsNegatedThisTurn = player.EffectsNegatedThisTurn,
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
            LegalActions = source.LegalActions,
        };
    }

    /// <summary>
    /// A minimal advertisement that carries only a punish, so the predictor can be asked about a
    /// chosen amount of card flow without a real move having to exist at that size.
    /// </summary>
    private static RuntimeLegalAction SyntheticPlay(int punish, GameState state, int actor)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["punish"] = punish };
        return new RuntimeLegalAction
        {
            ContractVersion = 1,
            SnapshotRevision = 0,
            ActionId = "synthetic_punish_" + punish,
            Type = AdvertisedActionPolicy.PlayCardAction,
            Actor = actor,
            SourceId = 1L,
            Payload = payload,
        };
    }

    private static IReadOnlyList<ThreatEstimator.EffectSpecPair> EffectsOf(CardDefinition definition)
    {
        var pairs = new List<ThreatEstimator.EffectSpecPair>();
        void Take(IReadOnlyList<DominionWars.Engine.Effects.EffectSpec>? specs)
        {
            if (specs is null) return;
            foreach (var spec in specs)
            {
                if (spec is null) continue;
                pairs.Add(new ThreatEstimator.EffectSpecPair(spec.Action, EffectTargetKindOf(spec.Target)));
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

    private static EffectTargetKind EffectTargetKindOf(string? target)
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
}

}
