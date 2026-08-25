using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DominionWars.Adapters;
using DominionWars.Engine;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class RuntimeMatchGatewayTests
{
    [Test]
    public void AdvertisedAmbushSkipAdvancesAndIncrementsRevision()
    {
        var state = NewState(out var flow);
        flow.Advance(state, 0);
        var gateway = new RuntimeMatchGateway(
            "match_gateway", state, flow, TurnActionRouter.CreateDefault(flow));
        var snapshot = gateway.GetSnapshot(0);
        var advertised = snapshot.LegalActions[0];

        var submission = gateway.Submit(new RuntimeGameAction
        {
            MatchId = "match_gateway",
            SnapshotRevision = snapshot.SnapshotRevision,
            ActionId = advertised.ActionId,
            Type = advertised.Type,
            Actor = advertised.Actor,
            SourceId = advertised.SourceId,
            TargetId = advertised.TargetId,
            CardId = advertised.CardId,
            Payload = new Dictionary<string, object?>(),
        });

        Assert.Multiple(() =>
        {
            Assert.That(submission.Result.Accepted, Is.True);
            Assert.That(submission.Result.SnapshotRevision, Is.EqualTo(0));
            Assert.That(submission.Result.ResultingSnapshotRevision, Is.EqualTo(1));
            Assert.That(gateway.SnapshotRevision, Is.EqualTo(1));
            Assert.That(submission.Events, Is.Not.Empty);
            Assert.That(submission.Snapshot.SnapshotRevision, Is.EqualTo(1));
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
        });
    }

    [Test]
    public void InitializeRunsStartLifecycleOnceAndReturnsAmbushSnapshot()
    {
        var state = NewState(out var flow);
        var gateway = new RuntimeMatchGateway(
            "match_gateway", state, flow, TurnActionRouter.CreateDefault(flow));
        var beforeEvents = state.Events.Count;

        var first = gateway.Initialize(0);
        var second = gateway.Initialize(0);
        var cached = gateway.GetInitialization(0);

        Assert.Multiple(() =>
        {
            Assert.That(first.Accepted, Is.True);
            Assert.That(first.ReasonKey, Is.EqualTo("match.initialized"));
            Assert.That(first.Snapshot.Phase, Is.EqualTo(TurnPhase.Ambush));
            Assert.That(first.Snapshot.SnapshotRevision, Is.EqualTo(1));
            Assert.That(first.Events, Is.Not.Empty);
            Assert.That(first.Events.Select(item => item.EventType), Does.Contain("TURN_STARTED"));
            Assert.That(first.Events.Select(item => item.EventType), Does.Contain("PHASE_CHANGED"));
            Assert.That(second.Accepted, Is.True);
            Assert.That(second.Snapshot.SnapshotRevision, Is.EqualTo(first.Snapshot.SnapshotRevision));
            Assert.That(second.Events.Select(item => item.EventId), Is.EqualTo(first.Events.Select(item => item.EventId)));
            Assert.That(cached.Events.Select(item => item.EventId), Is.EqualTo(first.Events.Select(item => item.EventId)));
            Assert.That(cached.Snapshot.SnapshotRevision, Is.EqualTo(first.Snapshot.SnapshotRevision));
            Assert.That(state.Events.Count, Is.GreaterThan(beforeEvents));
            Assert.That(gateway.SnapshotRevision, Is.EqualTo(1));
        });
    }

    [Test]
    public void InitializedGatewaySubmitsAdvertisedAmbushSkipAndReturnsActionDelta()
    {
        var state = NewState(out var flow);
        var gateway = new RuntimeMatchGateway(
            "match_gateway", state, flow, TurnActionRouter.CreateDefault(flow));
        var initialization = gateway.Initialize(0);
        var advertised = initialization.Snapshot.LegalActions.Single();

        var submission = gateway.Submit(new RuntimeGameAction
        {
            MatchId = initialization.Snapshot.MatchId,
            SnapshotRevision = initialization.Snapshot.SnapshotRevision,
            ActionId = advertised.ActionId,
            Type = advertised.Type,
            Actor = advertised.Actor,
            SourceId = advertised.SourceId,
            TargetId = advertised.TargetId,
            CardId = advertised.CardId,
            Payload = advertised.Payload,
        });

        Assert.Multiple(() =>
        {
            Assert.That(advertised.Type, Is.EqualTo(TurnAction.SkipAmbush));
            Assert.That(submission.Result.Accepted, Is.True);
            Assert.That(submission.Result.SnapshotRevision, Is.EqualTo(1));
            Assert.That(submission.Result.ResultingSnapshotRevision, Is.EqualTo(2));
            Assert.That(submission.Snapshot.Phase, Is.EqualTo(TurnPhase.Action));
            Assert.That(submission.Snapshot.SnapshotRevision, Is.EqualTo(2));
            Assert.That(submission.Events.Select(item => item.EventType), Does.Contain("PHASE_CHANGED"));
            Assert.That(submission.Snapshot.LegalActions, Is.Not.Empty);
        });
    }

    [Test]
    public void InitializeRejectsTerminalMatchWithoutMutationAndIsIdempotent()
    {
        var state = NewState(out var flow);
        state.WinnerPlayerIndex = 0;
        var gateway = new RuntimeMatchGateway(
            "match_gateway", state, flow, TurnActionRouter.CreateDefault(flow));
        var beforeEvents = state.Events.Count;

        var first = gateway.Initialize(0);
        var second = gateway.Initialize(1);

        Assert.Multiple(() =>
        {
            Assert.That(first.Accepted, Is.False);
            Assert.That(first.ReasonKey, Is.EqualTo("match.initialization_game_over"));
            Assert.That(first.Events, Is.Empty);
            Assert.That(first.Snapshot.SnapshotRevision, Is.Zero);
            Assert.That(second.Accepted, Is.False);
            Assert.That(second.ReasonKey, Is.EqualTo(first.ReasonKey));
            Assert.That(second.Snapshot.ViewerPlayerId, Is.EqualTo("player_1"));
            Assert.That(state.Events.Count, Is.EqualTo(beforeEvents));
            Assert.That(gateway.SnapshotRevision, Is.Zero);
        });
    }

    [Test]
    public void InitializeRejectsRouteWithoutNextPhaseBeforeMutatingState()
    {
        var state = NewState(out _);
        var flow = new TurnFlow(
            new IPhaseHandler[] { new StartPhaseHandler() },
            new[] { TurnPhase.Start });
        var gateway = new RuntimeMatchGateway(
            "match_gateway", state, flow, TurnActionRouter.CreateDefault(flow));
        var beforeEvents = state.Events.Count;

        var result = gateway.Initialize(0);

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonKey, Is.EqualTo("match.initialization_route_invalid"));
            Assert.That(result.Snapshot.Phase, Is.EqualTo(TurnPhase.Start));
            Assert.That(result.Snapshot.SnapshotRevision, Is.Zero);
            Assert.That(state.Events.Count, Is.EqualTo(beforeEvents));
            Assert.That(gateway.SnapshotRevision, Is.Zero);
        });
    }

    [Test]
    public void ExactReplayReturnsCachedSubmissionWithoutNewEvents()
    {
        var state = NewState(out var flow);
        flow.Advance(state, 0);
        var gateway = new RuntimeMatchGateway(
            "match_gateway", state, flow, TurnActionRouter.CreateDefault(flow));
        var action = gateway.GetSnapshot(0).LegalActions[0];
        var request = new RuntimeGameAction
        {
            MatchId = "match_gateway",
            SnapshotRevision = 0,
            ActionId = action.ActionId,
            Type = action.Type,
            Actor = 0,
            Payload = new Dictionary<string, object?>(),
        };

        var first = gateway.Submit(request);
        var eventCountAfterFirst = state.Events.Count;
        var second = gateway.Submit(request);

        Assert.That(second, Is.SameAs(first));
        Assert.That(state.Events.Count, Is.EqualTo(eventCountAfterFirst));
    }

    [Test]
    public void SameRevisionActionIdWithDifferentRequestIsRejectedWithoutSideEffects()
    {
        var state = NewState(out var flow);
        flow.Advance(state, 0);
        var gateway = new RuntimeMatchGateway(
            "match_gateway", state, flow, TurnActionRouter.CreateDefault(flow));
        var advertised = gateway.GetSnapshot(0).LegalActions.Single();
        var request = ToGameAction(gateway.GetSnapshot(0), advertised);

        var first = gateway.Submit(request);
        var eventCountAfterFirst = state.Events.Count;
        request.Type = LegalActionGenerator.EndTurn;
        var replay = gateway.Submit(request);

        Assert.Multiple(() =>
        {
            Assert.That(first.Result.Accepted, Is.True);
            Assert.That(replay.Result.Accepted, Is.False);
            Assert.That(replay.Result.ReasonKey, Is.EqualTo("action.duplicate"));
            Assert.That(replay.Result.ResultingSnapshotRevision, Is.EqualTo(1));
            Assert.That(state.Events.Count, Is.EqualTo(eventCountAfterFirst));
            Assert.That(gateway.SnapshotRevision, Is.EqualTo(1));
        });
    }

    [Test]
    public void StableActionIdCanBeAcceptedAgainAtALaterRevision()
    {
        var state = NewState(out var flow);
        flow.Advance(state, 0);
        var gateway = new RuntimeMatchGateway(
            "match_gateway", state, flow, TurnActionRouter.CreateDefault(flow));

        var firstAmbush = gateway.GetSnapshot(0);
        var firstSkip = firstAmbush.LegalActions.Single(action => action.Type == TurnAction.SkipAmbush);
        Assert.That(SubmitAdvertised(gateway, firstAmbush, firstSkip).Result.Accepted, Is.True);
        SubmitCurrent(gateway, 0, LegalActionGenerator.EndTurn);
        SubmitCurrent(gateway, 0, TurnAction.DiscardComplete);
        SubmitCurrent(gateway, 1, TurnAction.SkipAmbush);
        SubmitCurrent(gateway, 1, LegalActionGenerator.EndTurn);
        SubmitCurrent(gateway, 1, TurnAction.DiscardComplete);

        var laterAmbush = gateway.GetSnapshot(0);
        var laterSkip = laterAmbush.LegalActions.Single(action => action.Type == TurnAction.SkipAmbush);
        var submission = SubmitAdvertised(gateway, laterAmbush, laterSkip);

        Assert.Multiple(() =>
        {
            Assert.That(laterAmbush.SnapshotRevision, Is.GreaterThan(firstAmbush.SnapshotRevision));
            Assert.That(laterSkip.ActionId, Is.EqualTo(firstSkip.ActionId));
            Assert.That(submission.Result.Accepted, Is.True);
            Assert.That(submission.Result.ReasonKey, Is.EqualTo("action.accepted"));
            Assert.That(submission.Result.SnapshotRevision, Is.EqualTo(laterAmbush.SnapshotRevision));
            Assert.That(submission.Result.ResultingSnapshotRevision, Is.EqualTo(laterAmbush.SnapshotRevision + 1));
        });
    }

    [Test]
    public void StaleOrUnknownActionHasNoEngineSideEffects()
    {
        var state = NewState(out var flow);
        flow.Advance(state, 0);
        var gateway = new RuntimeMatchGateway(
            "match_gateway", state, flow, TurnActionRouter.CreateDefault(flow));
        var beforeEvents = state.Events.Count;
        var result = gateway.Submit(new RuntimeGameAction
        {
            MatchId = "match_gateway",
            SnapshotRevision = 99,
            ActionId = "unknown_action",
            Type = TurnAction.SkipAmbush,
            Actor = 0,
            Payload = new Dictionary<string, object?>(),
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Result.Accepted, Is.False);
            Assert.That(result.Result.ReasonKey, Is.EqualTo("action.future_snapshot"));
            Assert.That(state.Events.Count, Is.EqualTo(beforeEvents));
            Assert.That(gateway.SnapshotRevision, Is.Zero);
        });
    }

    [Test]
    public void NumericWireEntityIdIsAcceptedAndMappedToEngineEntityId()
    {
        var state = NewState(out var flow);
        state.GetPlayer(0).Hand.Add(new CardInstance(
            7, 0, new CardDefinition("scout", "Scout", cost: 0, attack: 1, health: 1, isMinion: true)));
        flow.Advance(state, 0);
        TurnActionRouter.CreateDefault(flow).Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush));
        var gateway = new RuntimeMatchGateway(
            "match_gateway", state, flow, TurnActionRouter.CreateDefault(flow));
        var advertised = gateway.GetSnapshot(0).LegalActions.Single(action => action.Type == "PLAY_CARD");

        Assert.That(advertised.SourceId, Is.EqualTo(7L));
        var wireAction = JsonSerializer.Deserialize<RuntimeGameAction>(JsonSerializer.Serialize(new RuntimeGameAction
        {
            MatchId = "match_gateway",
            SnapshotRevision = 0,
            ActionId = advertised.ActionId,
            Type = advertised.Type,
            Actor = advertised.Actor,
            SourceId = 7L,
            CardId = advertised.CardId,
            Payload = advertised.Payload,
        }))!;
        var submission = gateway.Submit(wireAction);

        Assert.That(submission.Result.Accepted, Is.True);
    }

    [Test]
    public void TargetedPlayRoundTripsOnlyThroughAdvertisedEngineTarget()
    {
        var state = NewState(out var flow);
        state.GetPlayer(0).Hand.Add(new CardInstance(22, 0, new CardDefinition(
            "flame_bolt",
            "Flame Bolt",
            punish: 1,
            onPlayEffects: new[]
            {
                new EffectSpec(EffectNames.Damage, "ENEMY_FACE", 3),
            })));
        state.GetPlayer(1).Deck.Add(new CardInstance(
            30, 1, new CardDefinition("punish_draw", "Punish Draw")));
        flow.Advance(state, 0);
        var router = TurnActionRouter.CreateDefault(flow);
        Assert.That(router.Execute(
            state,
            new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted,
            Is.True);
        var gateway = new RuntimeMatchGateway("match_targeted_play", state, flow, router);
        var snapshot = gateway.GetSnapshot(0);
        var advertised = snapshot.LegalActions.Single(action => action.CardId == "flame_bolt");
        var beforeEvents = state.Events.Count;

        var forged = gateway.Submit(new RuntimeGameAction
        {
            MatchId = snapshot.MatchId,
            SnapshotRevision = snapshot.SnapshotRevision,
            ActionId = advertised.ActionId,
            Type = advertised.Type,
            Actor = advertised.Actor,
            SourceId = advertised.SourceId,
            TargetId = "castle",
            CardId = advertised.CardId,
            Payload = advertised.Payload,
        });
        var accepted = gateway.Submit(new RuntimeGameAction
        {
            MatchId = snapshot.MatchId,
            SnapshotRevision = snapshot.SnapshotRevision,
            ActionId = advertised.ActionId,
            Type = advertised.Type,
            Actor = advertised.Actor,
            SourceId = advertised.SourceId,
            TargetId = advertised.TargetId,
            CardId = advertised.CardId,
            Payload = advertised.Payload,
        });

        Assert.Multiple(() =>
        {
            Assert.That(advertised.SourceId, Is.EqualTo(22L));
            Assert.That(advertised.TargetId, Is.EqualTo("player_1"));
            Assert.That(advertised.ActionId, Is.EqualTo("play_22_core:player_1:life"));
            Assert.That(forged.Result.Accepted, Is.False);
            Assert.That(forged.Result.ReasonKey, Is.EqualTo("action.advertisement_mismatch"));
            Assert.That(accepted.Result.Accepted, Is.True);
            Assert.That(state.GetPlayer(1).Life, Is.EqualTo(17));
            Assert.That(state.GetPlayer(0).Hand, Is.Empty);
            Assert.That(state.GetPlayer(0).Graveyard, Has.Count.EqualTo(1));
            Assert.That(state.Events.Count, Is.GreaterThan(beforeEvents));
        });
    }

    [Test]
    public void AdvertisedPullRoundTripsThroughRuntimeGateway()
    {
        var state = NewState(out var flow);
        state.GetPlayer(0).Field.Add(new CardInstance(71, 0, new CardDefinition(
            "gateway_carrier",
            "Gateway Carrier",
            attack: 1,
            health: 3,
            isMinion: true,
            faction: "机械遗迹",
            tags: new[] { "机械" })));
        state.GetPlayer(0).CloudStack.Add(new CardInstance(72, 0, new CardDefinition(
            "gateway_cloud",
            "Gateway Cloud")));
        flow.Advance(state, 0);
        var router = TurnActionRouter.CreateDefault(flow);
        Assert.That(router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted, Is.True);
        var gateway = new RuntimeMatchGateway("match_gateway", state, flow, router);
        var snapshot = gateway.GetSnapshot(0);
        var advertised = snapshot.LegalActions.Single(action => action.Type == LegalActionGenerator.Pull);

        var submission = gateway.Submit(new RuntimeGameAction
        {
            MatchId = snapshot.MatchId,
            SnapshotRevision = snapshot.SnapshotRevision,
            ActionId = advertised.ActionId,
            Type = advertised.Type,
            Actor = advertised.Actor,
            SourceId = advertised.SourceId,
            TargetId = advertised.TargetId,
            CardId = advertised.CardId,
            Payload = advertised.Payload,
        });

        Assert.Multiple(() =>
        {
            Assert.That(submission.Result.Accepted, Is.True);
            Assert.That(submission.Snapshot.SnapshotRevision, Is.EqualTo(1));
            Assert.That(state.GetPlayer(0).CloudStack, Is.Empty);
            Assert.That(state.GetPlayer(0).Graveyard, Has.Count.EqualTo(1));
            Assert.That(submission.Events.Select(item => item.EventType),
                Is.EqualTo(new[] { "PULL_DECLARED", "CARD_PULLED" }));
        });
    }

    [Test]
    public void UnchangedAdvertisedDiscardBridgesCandidateIdsAndRequiredCount()
    {
        var state = NewState(out var flow);
        for (var id = 1; id <= 10; id++)
        {
            state.GetPlayer(0).Hand.Add(new CardInstance(
                id,
                0,
                new CardDefinition("discard_card_" + id, "Discard Card " + id)));
        }

        flow.JumpTo(state, 0, TurnPhase.Discard);
        var gateway = new RuntimeMatchGateway(
            "match_gateway", state, flow, TurnActionRouter.CreateDefault(flow));
        var snapshot = gateway.GetSnapshot(0);
        var advertised = snapshot.LegalActions.Single(action => action.Type == TurnAction.DiscardComplete);

        var forgedPayload = new Dictionary<string, object?>(advertised.Payload)
        {
            ["selectedEntityIds"] = new[] { 1L },
        };
        var forged = gateway.Submit(new RuntimeGameAction
        {
            MatchId = snapshot.MatchId,
            SnapshotRevision = snapshot.SnapshotRevision,
            ActionId = advertised.ActionId,
            Type = advertised.Type,
            Actor = advertised.Actor,
            Payload = forgedPayload,
        });

        var submission = SubmitAdvertised(gateway, snapshot, advertised);

        Assert.Multiple(() =>
        {
            Assert.That(advertised.ActionId, Is.EqualTo("discard_0_2"));
            Assert.That(advertised.Payload["requiredCount"], Is.EqualTo(2));
            Assert.That((long[])advertised.Payload["candidateIds"]!, Has.Length.EqualTo(10));
            Assert.That(forged.Result.Accepted, Is.False);
            Assert.That(forged.Result.ReasonKey, Is.EqualTo("action.advertisement_mismatch"));
            Assert.That(forged.Result.ResultingSnapshotRevision, Is.Zero);
            Assert.That(submission.Result.Accepted, Is.True, submission.Result.ReasonKey);
            Assert.That(submission.Result.SnapshotRevision, Is.EqualTo(0));
            Assert.That(submission.Result.ResultingSnapshotRevision, Is.EqualTo(1));
            Assert.That(gateway.SnapshotRevision, Is.EqualTo(1));
            Assert.That(state.GetPlayer(0).Hand.Select(card => card.InstanceId),
                Is.EqualTo(new[] { 3L, 4L, 5L, 6L, 7L, 8L, 9L, 10L }));
            Assert.That(state.GetPlayer(0).Graveyard.Select(card => card.InstanceId),
                Is.EqualTo(new[] { 1L, 2L }));
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Ambush));
        });
    }

    private static GameState NewState(out TurnFlow flow)
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        flow = TurnFlow.CreateDefault();
        return state;
    }

    private static RuntimeActionSubmission SubmitCurrent(
        RuntimeMatchGateway gateway,
        int viewerPlayerIndex,
        string actionType)
    {
        var snapshot = gateway.GetSnapshot(viewerPlayerIndex);
        var advertised = snapshot.LegalActions.Single(action => action.Type == actionType);
        var submission = SubmitAdvertised(gateway, snapshot, advertised);
        Assert.That(submission.Result.Accepted, Is.True, submission.Result.ReasonKey);
        return submission;
    }

    private static RuntimeActionSubmission SubmitAdvertised(
        RuntimeMatchGateway gateway,
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction advertised)
    {
        return gateway.Submit(ToGameAction(snapshot, advertised));
    }

    private static RuntimeGameAction ToGameAction(
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction advertised)
    {
        return new RuntimeGameAction
        {
            ContractVersion = advertised.ContractVersion,
            MatchId = snapshot.MatchId,
            SnapshotRevision = advertised.SnapshotRevision,
            ActionId = advertised.ActionId,
            Type = advertised.Type,
            Actor = advertised.Actor,
            SourceId = advertised.SourceId,
            TargetId = advertised.TargetId,
            CardId = advertised.CardId,
            Payload = advertised.Payload,
        };
    }
}
}
