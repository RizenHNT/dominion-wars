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
            Assert.That(viewer0.Players[0].Ambush.Single().EntityId, Is.EqualTo(7L));
            Assert.That(viewer1.Players[0].Ambush, Is.Empty,
                "An opponent sees only the public ambush count, never the set card identity.");
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
    public void VisibleCardCurrentStatsComeFromCardInstanceNotDefinition()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var definition = new CardDefinition(
            "mutable_minion",
            "Mutable Minion",
            attack: 2,
            health: 9,
            isMinion: true);
        var ownField = new CardInstance(12, 0, definition)
        {
            Attack = 8,
            Health = 3,
            Sealed = true,
        };
        var opponentField = new CardInstance(13, 1, definition)
        {
            Attack = 1,
            Health = 6,
        };
        state.GetPlayer(0).Field.Add(ownField);
        state.GetPlayer(1).Field.Add(opponentField);
        state.GetPlayer(0).Hand.Add(new CardInstance(14, 0, definition));

        var snapshot = RuntimeSnapshotProjection.ToSnapshot(
            state,
            "match_projection",
            11,
            0,
            TurnFlow.CreateDefault());

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Players[0].Field.Single().CurrentAttack, Is.EqualTo(8));
            Assert.That(snapshot.Players[0].Field.Single().CurrentHealth, Is.EqualTo(3));
            Assert.That(snapshot.Players[0].Field.Single().Sealed, Is.True);
            Assert.That(snapshot.Players[1].Field.Single().CurrentAttack, Is.EqualTo(1));
            Assert.That(snapshot.Players[1].Field.Single().CurrentHealth, Is.EqualTo(6));
            Assert.That(snapshot.Players[0].Hand.Single().CurrentAttack, Is.EqualTo(definition.Attack));
            Assert.That(snapshot.Players[0].Hand.Single().CurrentHealth, Is.EqualTo(definition.Health));
            Assert.That(snapshot.Players[1].Hand, Is.Empty);
        });
    }

    [Test]
    public void PublicMechanicalZonesPreserveEngineOrderAndCloudStackTop()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var definition = new CardDefinition(
            "mechanical_card",
            "Mechanical Card",
            attack: 1,
            health: 4,
            isMinion: true);
        var queueFirst = new CardInstance(21, 0, definition) { Attack = 5, Health = 3 };
        var queueSecond = new CardInstance(22, 0, definition) { Attack = 6, Health = 2 };
        var cloudBottom = new CardInstance(31, 0, definition) { Attack = 7, Health = 4 };
        var cloudTop = new CardInstance(32, 0, definition) { Attack = 9, Health = 1 };
        state.GetPlayer(0).CommitQueue.Add(queueFirst);
        state.GetPlayer(0).CommitQueue.Add(queueSecond);
        state.GetPlayer(0).CloudStack.Add(cloudBottom);
        state.GetPlayer(0).CloudStack.Add(cloudTop);

        var opponentQueueCard = new CardInstance(41, 1, definition);
        var opponentCloudCard = new CardInstance(42, 1, definition);
        state.GetPlayer(1).CommitQueue.Add(opponentQueueCard);
        state.GetPlayer(1).CloudStack.Add(opponentCloudCard);

        var snapshot = RuntimeSnapshotProjection.ToSnapshot(
            state,
            "match_projection",
            12,
            0,
            TurnFlow.CreateDefault());

        var own = snapshot.Players[0];
        var opponent = snapshot.Players[1];
        Assert.Multiple(() =>
        {
            Assert.That(own.CommitQueue.Select(card => card.EntityId), Is.EqualTo(new long[] { 21, 22 }));
            Assert.That(own.CommitQueue.Select(card => card.CardId), Is.EqualTo(new[] { "mechanical_card", "mechanical_card" }));
            Assert.That(own.CloudStack.Select(card => card.EntityId), Is.EqualTo(new long[] { 31, 32 }));
            Assert.That(own.CloudStack[^1].EntityId, Is.EqualTo(32L));
            Assert.That(own.CloudStack[^1].CurrentAttack, Is.EqualTo(9));
            Assert.That(own.CloudStack[^1].CurrentHealth, Is.EqualTo(1));
            Assert.That(own.CommitQueue.Count, Is.EqualTo(own.CommitQueueCount));
            Assert.That(own.CloudStack.Count, Is.EqualTo(own.CloudStackCount));
            Assert.That(opponent.CommitQueue.Single().EntityId, Is.EqualTo(41L));
            Assert.That(opponent.CloudStack.Single().EntityId, Is.EqualTo(42L));
        });
    }

    [Test]
    public void PublicLeaderZoneAndCycleWinCountAreProjectedFromEngineState()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 18));
        var leaderDefinition = new CardDefinition(
            "public_leader",
            "Public Leader",
            isLeader: true,
            type: "LEADER");
        var opponentLeaderDefinition = new CardDefinition(
            "opponent_leader",
            "Opponent Leader",
            isLeader: true,
            type: "LEADER");
        var ownLeader = new CardInstance(8, 0, leaderDefinition)
        {
            IsLeaderEntity = true,
            Sealed = true,
        };
        var opponentLeader = new CardInstance(9, 1, opponentLeaderDefinition)
        {
            IsLeaderEntity = true,
        };
        state.GetPlayer(0).LeaderZone.Add(ownLeader);
        state.GetPlayer(1).LeaderZone.Add(opponentLeader);
        state.GetPlayer(0).CycleWinCount = 4;
        state.GetPlayer(1).CycleWinCount = 2;

        var snapshot = RuntimeSnapshotProjection.ToSnapshot(
            state,
            "match_projection",
            8,
            0,
            TurnFlow.CreateDefault());

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Players[0].LeaderZone, Has.Count.EqualTo(1));
            Assert.That(snapshot.Players[0].LeaderZone[0].EntityId, Is.EqualTo(8L));
            Assert.That(snapshot.Players[0].LeaderZone[0].CardId, Is.EqualTo("public_leader"));
            Assert.That(snapshot.Players[0].LeaderZone[0].Sealed, Is.True);
            Assert.That(snapshot.Players[1].LeaderZone, Has.Count.EqualTo(1));
            Assert.That(snapshot.Players[1].LeaderZone[0].EntityId, Is.EqualTo(9L));
            Assert.That(snapshot.Players[0].CycleWinCount, Is.EqualTo(4));
            Assert.That(snapshot.Players[1].CycleWinCount, Is.EqualTo(2));
            Assert.That(snapshot.Players[0].AmbushCount, Is.Zero);
        });
    }

    [Test]
    public void GeneratedAttackTargetProjectsNumericEntityIdMatchingTheCardSnapshot()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var attacker = new CardInstance(
            11,
            0,
            new CardDefinition("attacker", "Attacker", attack: 2, health: 4, isMinion: true));
        var defender = new CardInstance(
            123,
            1,
            new CardDefinition("defender", "Defender", attack: 1, health: 4, isMinion: true));
        state.GetPlayer(0).Field.Add(attacker);
        state.GetPlayer(1).Field.Add(defender);

        var flow = TurnFlow.CreateDefault();
        flow.JumpTo(state, 0, TurnPhase.Action);
        var generated = new LegalActionGenerator().Generate(state, 0)
            .Single(action => action.Type == LegalActionGenerator.Attack && action.TargetId == 123L);
        var snapshot = RuntimeSnapshotProjection.ToSnapshot(
            state,
            "match_projection",
            9,
            0,
            flow);
        var projected = snapshot.LegalActions.Single(action => action.ActionId == generated.ActionId);

        Assert.Multiple(() =>
        {
            Assert.That(generated.TargetReferenceId, Is.EqualTo("entity_000000000123"));
            Assert.That(generated.TargetId, Is.EqualTo(123L));
            Assert.That(projected.TargetId, Is.EqualTo(123L));
            Assert.That(projected.TargetId, Is.EqualTo(snapshot.Players[1].Field.Single().EntityId));
        });
    }

    [Test]
    public void NamedTargetReferencesRemainStableWhileLegacyEntityReferencesBecomeNumeric()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var actions = new[]
        {
            new LegalAction
            {
                ActionId = "target_life",
                Type = LegalActionGenerator.PlayCard,
                Actor = 0,
                TargetReferenceId = "core:player_1:life",
            },
            new LegalAction
            {
                ActionId = "target_leader",
                Type = LegalActionGenerator.PlayCard,
                Actor = 0,
                TargetReferenceId = "core:player_1:leader",
            },
            new LegalAction
            {
                ActionId = "target_castle",
                Type = LegalActionGenerator.Attack,
                Actor = 0,
                TargetReferenceId = "core:shared_castle",
            },
            new LegalAction
            {
                ActionId = "target_prompt",
                Type = LegalActionGenerator.PlayCard,
                Actor = 0,
                TargetReferenceId = "prompt_choose_one",
            },
            new LegalAction
            {
                ActionId = "target_legacy_entity",
                Type = LegalActionGenerator.PlayCard,
                Actor = 0,
                TargetReferenceId = "entity_000000000124",
            },
            new LegalAction
            {
                ActionId = "target_long_entity",
                Type = LegalActionGenerator.PlayCard,
                Actor = 0,
                TargetReferenceId = "entity_1000000000000",
            },
        };
        var flow = new TurnFlow(
            new IPhaseHandler[]
            {
                new FixedActionsHandler(TurnPhase.Action, actions),
            },
            new[] { TurnPhase.Action });
        flow.JumpTo(state, 0, TurnPhase.Action);

        var snapshot = RuntimeSnapshotProjection.ToSnapshot(
            state,
            "match_projection",
            10,
            0,
            flow);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.LegalActions.Single(action => action.ActionId == "target_life").TargetId,
                Is.EqualTo("player_1"));
            Assert.That(snapshot.LegalActions.Single(action => action.ActionId == "target_leader").TargetId,
                Is.EqualTo("leader_1"));
            Assert.That(snapshot.LegalActions.Single(action => action.ActionId == "target_castle").TargetId,
                Is.EqualTo("castle"));
            Assert.That(snapshot.LegalActions.Single(action => action.ActionId == "target_prompt").TargetId,
                Is.EqualTo("prompt_choose_one"));
            Assert.That(snapshot.LegalActions.Single(action => action.ActionId == "target_legacy_entity").TargetId,
                Is.EqualTo(124L));
            Assert.That(snapshot.LegalActions.Single(action => action.ActionId == "target_long_entity").TargetId,
                Is.EqualTo(1000000000000L));
        });
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
