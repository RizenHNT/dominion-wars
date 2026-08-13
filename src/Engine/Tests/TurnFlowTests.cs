using System;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Targeting;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class TurnFlowTests
{
    [Test]
    public void DefaultFlowAdvancesFivePhasesThenHandsTurnToOpponent()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var flow = TurnFlow.CreateDefault();

        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Start));
        flow.Advance(state, 0);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Ambush));
        flow.Advance(state, 0);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
        flow.Advance(state, 0);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Discard));
        flow.Advance(state, 0);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.End));
        flow.Advance(state, 0);

        Assert.Multiple(() =>
        {
            Assert.That(state.CurrentPlayerIndex, Is.EqualTo(1));
            Assert.That(state.Turn.Number, Is.EqualTo(2));
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Start));
            Assert.That(state.Events.Items.Count(item => item.EventType == "PHASE_CHANGED"), Is.EqualTo(5));
            Assert.That(state.Events.Items.Count(item => item.EventType == "TURN_CHANGED"), Is.EqualTo(1));
        });
    }

    [Test]
    public void StartPhaseRefreshesBoardAndDrawsOneCard()
    {
        var state = new GameState();
        var definition = new CardDefinition("unit", "Unit", 2, 3, isMinion: true);
        var field = new CardInstance(1, 0, definition) { AttacksUsed = 1, SummonedThisTurn = true };
        state.GetPlayer(0).Field.Add(field);
        state.GetPlayer(0).Deck.Add(new CardInstance(2, 0, definition));

        TurnFlow.CreateDefault().Advance(state, 0);

        Assert.Multiple(() =>
        {
            Assert.That(field.AttacksUsed, Is.Zero);
            Assert.That(field.SummonedThisTurn, Is.False);
            Assert.That(state.GetPlayer(0).Deck, Is.Empty);
            Assert.That(state.GetPlayer(0).Hand.Select(card => card.InstanceId), Is.EqualTo(new[] { 2L }));
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Ambush));
            Assert.That(state.Events.Items.Any(item => item.EventType == "TURN_STARTED"), Is.True);
            Assert.That(state.Events.Items.Any(item => item.EventType == "CARDS_DRAWN"), Is.True);
        });
    }

    [Test]
    public void StartPhaseReshufflesGraveyardBeforeDrawing()
    {
        var state = new GameState();
        var definition = new CardDefinition("unit", "Unit", 2, 3, isMinion: true);
        state.GetPlayer(0).Graveyard.Add(new CardInstance(3, 0, definition) { Health = 1, AttacksUsed = 1 });

        TurnFlow.CreateDefault().Advance(state, 0);

        Assert.Multiple(() =>
        {
            Assert.That(state.GetPlayer(0).Hand.Single().InstanceId, Is.EqualTo(3));
            Assert.That(state.GetPlayer(0).ReshuffleCount, Is.EqualTo(1));
            Assert.That(state.GetPlayer(1).CycleWinCount, Is.EqualTo(1));
            Assert.That(state.Events.Items.Any(item => item.EventType == "DECK_CYCLED"), Is.True);
        });
    }

    [Test]
    public void SecondPlayerFirstTurnDrawsTwoCards()
    {
        var state = new GameState();
        var definition = new CardDefinition("unit", "Unit", 2, 3, isMinion: true);
        state.GetPlayer(1).Deck.Add(new CardInstance(4, 1, definition));
        state.GetPlayer(1).Deck.Add(new CardInstance(5, 1, definition));
        var flow = TurnFlow.CreateDefault();

        flow.Advance(state, 0);
        flow.Advance(state, 0);
        flow.Advance(state, 0);
        flow.Advance(state, 0);
        flow.Advance(state, 0);
        flow.Advance(state, 1);

        Assert.Multiple(() =>
        {
            Assert.That(state.CurrentPlayerIndex, Is.EqualTo(1));
            Assert.That(state.Turn.Number, Is.EqualTo(2));
            Assert.That(state.GetPlayer(1).Hand, Has.Count.EqualTo(2));
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Ambush));
        });
    }

    [Test]
    public void AmbushSkipAndActionEndTurnUsePhaseOnlyExecutionPath()
    {
        var state = new GameState();
        var flow = TurnFlow.CreateDefault();
        flow.Advance(state, 0);

        Assert.That(flow.GetLegalActions(state, 0).Select(item => item.Type), Is.EquivalentTo(new[]
        {
            TurnAction.SkipAmbush,
        }));
        Assert.That(flow.TryExecutePhaseAction(state, 0, TurnAction.SetAmbush), Is.False,
            "Placing a card requires the future ambush resolver and must not silently skip the phase.");
        Assert.That(flow.TryExecutePhaseAction(state, 0, TurnAction.SkipAmbush), Is.True);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
        Assert.That(flow.TryExecutePhaseAction(state, 0, LegalActionGenerator.EndTurn), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(state.EndTurnRequested, Is.True);
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Discard));
            Assert.That(flow.TryExecutePhaseAction(state, 0, TurnAction.DiscardComplete), Is.False,
                "Discard completion needs a dedicated resolver with requiredCount and card ids.");
        });
    }

    [Test]
    public void CustomRegisteredPhaseCanBeInsertedWithoutChangingFlowCore()
    {
        var state = new GameState();
        var flow = new TurnFlow(
            new IPhaseHandler[]
            {
                new FixedHandler(TurnPhase.Start),
                new FixedHandler("COUNTER"),
                new FixedHandler(TurnPhase.Action),
                new FixedHandler(TurnPhase.Over),
            },
            new[] { TurnPhase.Start, "COUNTER", TurnPhase.Action });

        flow.Advance(state, 0);
        Assert.That(state.Turn.PhaseId, Is.EqualTo("COUNTER"));
        flow.JumpTo(state, 0, TurnPhase.Action, "counter_resolved");
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
    }

    [Test]
    public void WrongPlayerAndUnknownPhaseFailClosed()
    {
        var state = new GameState();
        var flow = TurnFlow.CreateDefault();

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => flow.Advance(state, 1));
            Assert.Throws<InvalidOperationException>(() => flow.JumpTo(state, 0, "NOT_REGISTERED"));
        });
    }

    [Test]
    public void TargetPolicyProjectsMinionLeaderCastleAndLifeWithIndependentSwitches()
    {
        var leaderDefinition = new CardDefinition("leader", "Leader", 1, 5, isMinion: true, isLeader: true);
        var minionDefinition = new CardDefinition("minion", "Minion", 1, 2, isMinion: true);
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20))
        {
            CastleEnabled = true,
            CastleHealth = 75,
        };
        state.GetPlayer(1).Field.Add(new CardInstance(10, 1, leaderDefinition) { IsLeaderEntity = true });
        state.GetPlayer(1).Field.Add(new CardInstance(11, 1, minionDefinition));
        var policy = new TargetPolicy();

        Assert.That(policy.GetEnemyCandidates(state, 0).Select(item => item.Kind), Is.EquivalentTo(new[]
        {
            TargetKind.EnemyLeader, TargetKind.EnemyMinion, TargetKind.RoyalCastle,
        }));

        Assert.Multiple(() =>
        {
            Assert.That(policy.TryResolveEnemyCandidate(state, 0, "entity_000000000011", out var minion), Is.True);
            Assert.That(minion!.EntityId, Is.EqualTo(11));
            Assert.That(policy.TryResolveEnemyCandidate(state, 0, "core:shared_castle", out var castle), Is.True);
            Assert.That(castle!.OwnerPlayerIndex, Is.Null);
            Assert.That(policy.TryResolveEnemyCandidate(state, 0, "entity:11", out _), Is.False);
            Assert.That(policy.TryResolveEnemySelection(state, 0, "entity_000000000011", out var selectedEntity), Is.True);
            Assert.That(selectedEntity.EntityId, Is.EqualTo(11));
            Assert.That(selectedEntity.CoreTarget, Is.Null);
            Assert.That(policy.TryResolveEnemySelection(state, 0, "core:shared_castle", out var selectedCastle), Is.True);
            Assert.That(selectedCastle.EntityId, Is.Null);
            Assert.That(selectedCastle.CoreTarget, Is.EqualTo(CoreTarget.RoyalCastle));
        });

        policy.AllowRoyalCastle = false;
        policy.AllowEnemyLife = false;
        Assert.That(policy.GetEnemyCandidates(state, 0).Select(item => item.Kind), Is.EquivalentTo(new[]
        {
            TargetKind.EnemyLeader, TargetKind.EnemyMinion,
        }));

        state.GetPlayer(1).Field.RemoveAt(0);
        policy.AllowEnemyLife = true;
        Assert.That(policy.GetEnemyCandidates(state, 0).Select(item => item.Kind), Is.EquivalentTo(new[]
        {
            TargetKind.EnemyMinion, TargetKind.EnemyLife,
        }));
    }

    [Test]
    public void WinnerImmediatelyTransitionsToOverWhenEffectRuntimeDeclaresGame()
    {
        var game = new EffectTestFixture();
        game.State.GetPlayer(0).Field.Add(new CardInstance(20, 0, game.LeaderDefinition) { IsLeaderEntity = true });
        game.State.GetPlayer(1).Field.Add(new CardInstance(21, 1, game.LeaderDefinition) { IsLeaderEntity = true });
        game.Dispatcher.Apply(new EffectSpec(EffectNames.WinGame), game.Context());

        Assert.Multiple(() =>
        {
            Assert.That(game.State.WinnerPlayerIndex, Is.Zero);
            Assert.That(game.State.Turn.PhaseId, Is.EqualTo(TurnPhase.Over));
            Assert.That(TurnFlow.CreateDefault().GetLegalActions(game.State, 0), Is.Empty);
        });
    }

    [Test]
    public void SnapshotUsesEngineOwnedTurnMetadataAndMapsTurnEvents()
    {
        var state = new GameState();
        var flow = TurnFlow.CreateDefault();
        flow.Advance(state, 0);

        var snapshot = EngineProjectionAdapter.ToSnapshot(state, "match_turn", flow);
        var phaseEvent = EngineProjectionAdapter.ToEvent(
            state.Events.Items.Single(item => item.EventType == "PHASE_CHANGED"),
            state.Turn.Number,
            state.Turn.PhaseId);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Turn, Is.EqualTo(1));
            Assert.That(snapshot.Phase, Is.EqualTo(TurnPhase.Ambush));
            Assert.That(phaseEvent.Type, Is.EqualTo("PHASE_CHANGED"));
        });
    }

    [Test]
    public void ActionRouterOnlyAcceptsPhaseActionsOrRegisteredHandlers()
    {
        var state = new GameState();
        var flow = TurnFlow.CreateDefault();
        var router = new TurnActionRouter(flow);
        flow.Advance(state, 0);

        var skip = router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush));
        var play = router.Execute(state, new GameActionRequest(0, LegalActionGenerator.PlayCard));
        var opponent = router.Execute(state, new GameActionRequest(1, LegalActionGenerator.EndTurn));

        Assert.Multiple(() =>
        {
            Assert.That(skip.Accepted, Is.True);
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
            Assert.That(play.Accepted, Is.False);
            Assert.That(play.ReasonKey, Is.EqualTo("action.not_legal_in_phase"));
            Assert.That(opponent.ReasonKey, Is.EqualTo("action.not_current_player"));
        });
    }

    private sealed class FixedHandler : IPhaseHandler
    {
        public FixedHandler(string phaseId)
        {
            PhaseId = phaseId;
        }

        public string PhaseId { get; }

        public IReadOnlyList<LegalAction> GetLegalActions(GameState state, int playerIndex)
        {
            return Array.Empty<LegalAction>();
        }
    }
}
}
