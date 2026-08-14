using System.Collections.Generic;
using DominionWars.Adapters;
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

    private static GameState NewState(out TurnFlow flow)
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        flow = TurnFlow.CreateDefault();
        return state;
    }
}
}
