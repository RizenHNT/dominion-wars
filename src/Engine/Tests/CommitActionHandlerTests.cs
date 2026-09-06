using System.Linq;
using DominionWars.Adapters;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class CommitActionHandlerTests
{
    [Test]
    public void AdvertisesAndExecutesZeroCostMechanicalFieldCommit()
    {
        var state = CreateActionState(out var flow, out var router);
        var card = Mechanical(10);
        state.GetPlayer(0).Field.Add(card);

        var action = flow.GetLegalActions(state, 0)
            .Single(item => item.Type == LegalActionGenerator.Commit);
        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Commit,
            action.ActionId,
            action.SourceId));
        var projected = EngineProjectionAdapter.ToEvents(
            state.Events.Items,
            state.Turn.Number,
            state.Turn.PhaseId)
            .Where(item => item.Type == "COMMIT_DECLARED" || item.Type == "CARD_COMMITTED")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(action.ActionId, Is.EqualTo("commit_10"));
            Assert.That(action.TargetId, Is.Null);
            Assert.That(action.Payload["commitCost"], Is.EqualTo(0));
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.GetPlayer(0).Field, Is.Empty);
            Assert.That(state.GetPlayer(0).CommitQueue, Has.Exactly(1).EqualTo(card));
            Assert.That(projected.Select(item => item.Type),
                Is.EqualTo(new[] { "COMMIT_DECLARED", "CARD_COMMITTED" }));
            Assert.That(projected[1].ParentEventId, Is.EqualTo(projected[0].EventId));
        });
    }

    [Test]
    public void DoesNotAdvertisePositiveCostNonMechanicalLeaderOrUnknownEffect()
    {
        var state = CreateActionState(out var flow, out _);
        state.GetPlayer(0).Field.Add(Mechanical(20, commitCost: 1));
        state.GetPlayer(0).Field.Add(new CardInstance(21, 0, new CardDefinition(
            "ordinary", "Ordinary", health: 2, isMinion: true)));
        state.GetPlayer(0).Field.Add(Mechanical(22, isLeader: true));
        state.GetPlayer(0).Field.Add(Mechanical(23, commitEffects: new[]
        {
            new EffectSpec("NOT_REGISTERED"),
        }));

        Assert.That(
            flow.GetLegalActions(state, 0).Where(item => item.Type == LegalActionGenerator.Commit),
            Is.Empty);
    }

    [Test]
    public void RejectsInvalidOrStaleCommitWithoutMutation()
    {
        var state = CreateActionState(out _, out var router);
        var card = Mechanical(30);
        state.GetPlayer(0).Field.Add(card);
        var beforeEvents = state.Events.Count;

        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Commit,
            "commit_999",
            card.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonKey, Is.EqualTo("action.id_mismatch"));
            Assert.That(state.GetPlayer(0).Field, Has.Exactly(1).EqualTo(card));
            Assert.That(state.GetPlayer(0).CommitQueue, Is.Empty);
            Assert.That(state.Events.Count, Is.EqualTo(beforeEvents));
        });
    }

    [Test]
    public void RejectsPositiveCostWhenCalledDirectly()
    {
        var state = CreateActionState(out _, out var router);
        var card = Mechanical(40, commitCost: 2);
        state.GetPlayer(0).Field.Add(card);

        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Commit,
            "commit_40",
            card.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonKey, Is.EqualTo("action.cost_system_unavailable"));
            Assert.That(state.GetPlayer(0).Field, Has.Exactly(1).EqualTo(card));
            Assert.That(state.GetPlayer(0).CommitQueue, Is.Empty);
        });
    }

    private static CardInstance Mechanical(
        long id,
        int commitCost = 0,
        bool isLeader = false,
        EffectSpec[]? commitEffects = null)
    {
        return new CardInstance(id, 0, new CardDefinition(
            "mechanical_" + id,
            "Mechanical " + id,
            attack: 1,
            health: 3,
            isMinion: true,
            isLeader: isLeader,
            faction: "机械遗迹",
            tags: new[] { "机械" },
            commitCost: commitCost,
            commitEffects: commitEffects));
    }

    private static GameState CreateActionState(out TurnFlow flow, out TurnActionRouter router)
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        flow = TurnFlow.CreateDefault();
        router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        Assert.That(router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted, Is.True);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
        return state;
    }
}
}
