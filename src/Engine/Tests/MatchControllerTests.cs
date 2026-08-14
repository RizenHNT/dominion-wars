using System;
using System.Linq;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class MatchControllerTests
{
    [Test]
    public void RejectsRouterFromAnotherFlow()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var flow = TurnFlow.CreateDefault();
        var otherFlow = TurnFlow.CreateDefault();

        Assert.That(
            () => new MatchController(state, flow, TurnActionRouter.CreateDefault(otherFlow)),
            Throws.ArgumentException);
    }

    [Test]
    public void LegalActionReturnsOnlyNewEventsAndDoesNotReplayThem()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var flow = TurnFlow.CreateDefault();
        var controller = new MatchController(state, flow, TurnActionRouter.CreateDefault(flow));
        flow.Advance(state, 0);

        var result = controller.Submit(new GameActionRequest(0, TurnAction.SkipAmbush));
        var duplicate = controller.Submit(new GameActionRequest(0, TurnAction.SkipAmbush));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Events, Is.Not.Empty);
            Assert.That(result.Events.Select(item => item.EventId), Is.Ordered.Ascending);
            Assert.That(duplicate.Accepted, Is.False);
            Assert.That(duplicate.Events, Is.Empty);
        });
    }

    [Test]
    public void WrongActorAndInvalidPhaseHaveNoEventDelta()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var flow = TurnFlow.CreateDefault();
        var controller = new MatchController(state, flow, TurnActionRouter.CreateDefault(flow));
        var before = state.Events.Count;

        var wrongActor = controller.Submit(new GameActionRequest(1, TurnAction.SkipAmbush));
        var invalidPhase = controller.Submit(new GameActionRequest(0, TurnAction.SkipAmbush));

        Assert.Multiple(() =>
        {
            Assert.That(wrongActor.Accepted, Is.False);
            Assert.That(wrongActor.ReasonKey, Is.EqualTo("action.not_current_player"));
            Assert.That(wrongActor.Events, Is.Empty);
            Assert.That(invalidPhase.Accepted, Is.False);
            Assert.That(invalidPhase.ReasonKey, Is.EqualTo("action.not_legal_in_phase"));
            Assert.That(invalidPhase.Events, Is.Empty);
            Assert.That(state.Events.Count, Is.EqualTo(before));
        });
    }

    [Test]
    public void NonCurrentPlayerHasNoAdvertisedActions()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var flow = TurnFlow.CreateDefault();
        var controller = new MatchController(state, flow, TurnActionRouter.CreateDefault(flow));
        flow.Advance(state, 0);
        Assert.That(controller.Submit(new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted, Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(controller.GetLegalActions(0), Is.Not.Empty);
            Assert.That(controller.GetLegalActions(1), Is.Empty);
        });
    }

    [Test]
    public void GameOverHasNoActionsAndRejectsSubmissionWithoutEvents()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var flow = TurnFlow.CreateDefault();
        var controller = new MatchController(state, flow, TurnActionRouter.CreateDefault(flow));
        state.WinnerPlayerIndex = 1;
        var before = state.Events.Count;

        var result = controller.Submit(new GameActionRequest(0, TurnAction.SkipAmbush));

        Assert.Multiple(() =>
        {
            Assert.That(controller.GetLegalActions(0), Is.Empty);
            Assert.That(controller.GetLegalActions(1), Is.Empty);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonKey, Is.EqualTo("action.game_over"));
            Assert.That(result.Events, Is.Empty);
            Assert.That(state.Events.Count, Is.EqualTo(before));
        });
    }
}
}
