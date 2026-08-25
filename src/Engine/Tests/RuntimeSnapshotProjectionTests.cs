using System.Collections.Generic;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Engine;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class RuntimeSnapshotProjectionTests
{
    [Test]
    public void OpponentHandIsRedactedButCountsAndPublicZonesRemainVisible()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 18));
        var definition = new CardDefinition("scout", "Scout", attack: 1, health: 2, isMinion: true);
        state.GetPlayer(0).Hand.Add(new CardInstance(1, 0, definition));
        state.GetPlayer(1).Hand.Add(new CardInstance(2, 1, definition));
        state.GetPlayer(1).Field.Add(new CardInstance(3, 1, definition));
        state.GetPlayer(1).Graveyard.Add(new CardInstance(4, 1, definition));
        var ambushDefinition = new CardDefinition(
            "ambush_leader",
            "Ambush Leader",
            isLeader: true,
            type: "AMBUSH");
        state.GetPlayer(0).AmbushZone.Add(new CardInstance(7, 0, ambushDefinition)
        {
            IsLeaderEntity = true,
        });
        state.GetPlayer(0).RootStacks = 2;
        state.GetPlayer(0).RampantStacks = 1;
        state.GetPlayer(0).PullCount = 3;
        state.GetPlayer(0).CommitQueue.Add(new CardInstance(5, 0, definition));
        state.GetPlayer(0).CloudStack.Add(new CardInstance(6, 0, definition));
        state.GetPlayer(1).Field[0].Sealed = true;
        var flow = TurnFlow.CreateDefault();

        var viewer0 = RuntimeSnapshotProjection.ToSnapshot(state, "match_projection", 7, 0, flow);
        var viewer1 = RuntimeSnapshotProjection.ToSnapshot(state, "match_projection", 7, 1, flow);

        Assert.Multiple(() =>
        {
            Assert.That(viewer0.Players[0].Hand.Select(card => card.EntityId), Is.EqualTo(new long[] { 1 }));
            Assert.That(viewer0.Players[1].Hand, Is.Empty);
            Assert.That(viewer0.Players[1].HandCount, Is.EqualTo(1));
            Assert.That(viewer0.Players[1].Field.Single().EntityId, Is.EqualTo(3L));
            Assert.That(viewer0.Players[1].Graveyard.Single().EntityId, Is.EqualTo(4L));
            Assert.That(viewer0.Players[0].RootStacks, Is.EqualTo(2));
            Assert.That(viewer0.Players[0].RampantStacks, Is.EqualTo(1));
            Assert.That(viewer0.Players[0].PullCount, Is.EqualTo(3));
            Assert.That(viewer0.Players[0].CommitQueueCount, Is.EqualTo(1));
            Assert.That(viewer0.Players[0].CloudStackCount, Is.EqualTo(1));
            Assert.That(viewer0.Players[0].AmbushCount, Is.EqualTo(1));
            Assert.That(viewer0.Players[1].Field.Single().Sealed, Is.True);
            Assert.That(viewer1.Players[0].Hand, Is.Empty);
            Assert.That(viewer1.Players[0].HandCount, Is.EqualTo(1));
            Assert.That(viewer0.SnapshotRevision, Is.EqualTo(7));
            Assert.That(viewer0.ViewerPlayerId, Is.EqualTo("player_0"));
        });
        RuntimeSnapshotProjection.Validate(viewer0);
        RuntimeSnapshotProjection.Validate(viewer1);
    }

    [Test]
    public void ProjectionIsStableAndDoesNotExposeDeckOrder()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var definition = new CardDefinition("scout", "Scout", isMinion: true, health: 1);
        state.GetPlayer(0).Deck.Add(new CardInstance(10, 0, definition));
        state.GetPlayer(0).Deck.Add(new CardInstance(11, 0, definition));
        var flow = TurnFlow.CreateDefault();

        var first = RuntimeSnapshotProjection.ToSnapshot(state, "match_projection", 0, 0, flow);
        var second = RuntimeSnapshotProjection.ToSnapshot(state, "match_projection", 0, 0, flow);

        Assert.Multiple(() =>
        {
            Assert.That(first.Players[0].DeckCount, Is.EqualTo(2));
            Assert.That(first.Players[0].Hand, Is.Empty);
            Assert.That(first.Players[0].Field, Is.Empty);
            Assert.That(first.Players[0].Graveyard, Is.Empty);
            Assert.That(second.Players[0].DeckCount, Is.EqualTo(first.Players[0].DeckCount));
            Assert.That(second.LegalActions.Select(action => action.ActionId),
                Is.EqualTo(first.LegalActions.Select(action => action.ActionId)));
        });
    }

    [Test]
    public void NonNullLegalActionEntityIdsMustBePositive()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var flow = new TurnFlow(
            new IPhaseHandler[]
            {
                new FixedActionsHandler(TurnPhase.Start, new LegalAction[0]),
                new FixedActionsHandler(TurnPhase.Action, new[]
                {
                    new LegalAction
                    {
                        ActionId = "invalid_zero_source",
                        Type = LegalActionGenerator.Attack,
                        Actor = 0,
                        SourceId = 0,
                    },
                }),
            },
            new[] { TurnPhase.Start, TurnPhase.Action });
        flow.JumpTo(state, 0, TurnPhase.Action);

        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            RuntimeSnapshotProjection.ToSnapshot(state, "match_projection", 0, 0, flow));
    }

    private sealed class FixedActionsHandler : IPhaseHandler
    {
        private readonly IReadOnlyList<LegalAction> _actions;

        public FixedActionsHandler(string phaseId, IReadOnlyList<LegalAction> actions)
        {
            PhaseId = phaseId;
            _actions = actions;
        }

        public string PhaseId { get; }

        public IReadOnlyList<LegalAction> GetLegalActions(GameState state, int playerIndex) => _actions;
    }
}
}
