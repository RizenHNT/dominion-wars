using System.Linq;
using DominionWars.Adapters;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class RuntimeMatchTraceTests
{
    [Test]
    public void GatewayTraceKeepsRevisionAndEventIdsMonotonic()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var flow = TurnFlow.CreateDefault();
        var gateway = new RuntimeMatchGateway(
            "match_trace", state, flow, TurnActionRouter.CreateDefault(flow));

        flow.Advance(state, 0);
        var first = gateway.GetSnapshot(0);
        var action = first.LegalActions.Single();
        var submission = gateway.Submit(new RuntimeGameAction
        {
            MatchId = first.MatchId,
            SnapshotRevision = first.SnapshotRevision,
            ActionId = action.ActionId,
            Type = action.Type,
            Actor = action.Actor,
            Payload = action.Payload,
        });

        Assert.Multiple(() =>
        {
            Assert.That(submission.Result.Accepted, Is.True);
            Assert.That(submission.Snapshot.SnapshotRevision, Is.GreaterThan(first.SnapshotRevision));
            Assert.That(submission.Events.Select(item => item.EventId), Is.Ordered.Ascending);
            Assert.That(submission.Result.MatchId, Is.EqualTo(first.MatchId));
            Assert.That(submission.Result.ResultingSnapshotRevision,
                Is.EqualTo(submission.Snapshot.SnapshotRevision));
        });
    }
}
}
