using System.Linq;
using DominionWars.Adapters;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class AmbushActionHandlerTests
{
    [Test]
    public void AdvertisesAndSetsAmbushWithoutLeavingAmbushPhase()
    {
        var state = CreateAmbushState(out var flow, out var router);
        var card = Ambush(10);
        state.GetPlayer(0).Hand.Add(card);

        var action = flow.GetLegalActions(state, 0).Single(item => item.Type == TurnAction.SetAmbush);
        var result = router.Execute(state, new GameActionRequest(
            0, TurnAction.SetAmbush, action.ActionId, action.SourceId));

        Assert.Multiple(() =>
        {
            Assert.That(action.ActionId, Is.EqualTo("set_ambush_10"));
            Assert.That(action.TargetId, Is.Null);
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Ambush));
            Assert.That(state.GetPlayer(0).Hand, Is.Empty);
            Assert.That(state.GetPlayer(0).AmbushZone, Has.Exactly(1).EqualTo(card));
            Assert.That(state.GetPlayer(0).AmbushSetThisTurn, Is.True);
            Assert.That(state.GetPlayer(0).UsedTags, Is.Empty,
                "Ambush tags are consumed when the ambush triggers, not when it is set.");
            Assert.That(flow.GetLegalActions(state, 0).Select(item => item.Type),
                Is.EquivalentTo(new[] { TurnAction.SkipAmbush }));
            Assert.That(state.Events.Items.Last().EventType, Is.EqualTo("AMBUSH_SET"));
            Assert.That(EngineProjectionAdapter.ToEvents(
                    state.Events.Items, state.Turn.Number, state.Turn.PhaseId)
                .Last().Type, Is.EqualTo("AMBUSH_SET"));
        });
    }

    [Test]
    public void LockdownAndSecondSetAreNotAdvertisedOrAccepted()
    {
        var state = CreateAmbushState(out var flow, out var router);
        var card = Ambush(20);
        state.GetPlayer(0).Hand.Add(card);
        state.GetPlayer(0).AmbushZone.Add(Ambush(21, "LOCKDOWN"));

        Assert.That(flow.GetLegalActions(state, 0).Any(item => item.Type == TurnAction.SetAmbush), Is.False);
        var result = router.Execute(state, new GameActionRequest(
            0, TurnAction.SetAmbush, "set_ambush_20", card.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonKey, Is.EqualTo("action.ambush_lockdown"));
            Assert.That(state.GetPlayer(0).Hand, Has.Exactly(1).EqualTo(card));
        });
    }

    [Test]
    public void InsufficientOpponentDeckFizzlesToGraveAndForcesEndPath()
    {
        var state = CreateAmbushState(out _, out var router);
        var card = Ambush(30, punish: 2);
        state.GetPlayer(0).Hand.Add(card);

        var result = router.Execute(state, new GameActionRequest(
            0, TurnAction.SetAmbush, "set_ambush_30", card.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.GetPlayer(0).AmbushZone, Is.Empty);
            Assert.That(state.GetPlayer(0).Graveyard, Has.Exactly(1).EqualTo(card));
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Discard));
            Assert.That(state.EndTurnRequested, Is.True);
        });
    }

    [Test]
    public void ConvertedPunishRequiresExactOtherCards()
    {
        var state = CreateAmbushState(out _, out var router);
        state.GetPlayer(0).PunishToSelfDiscardThisTurn = true;
        var ambush = Ambush(40, punish: 1);
        var payment = new CardInstance(41, 0, new CardDefinition("payment", "Payment"));
        state.GetPlayer(0).Hand.Add(ambush);
        state.GetPlayer(0).Hand.Add(payment);

        var rejected = router.Execute(state, new GameActionRequest(
            0, TurnAction.SetAmbush, "set_ambush_40", ambush.InstanceId));
        var accepted = router.Execute(state, new GameActionRequest(
            0, TurnAction.SetAmbush, "set_ambush_40", ambush.InstanceId,
            selectedEntityIds: new[] { payment.InstanceId }));

        Assert.Multiple(() =>
        {
            Assert.That(rejected.Accepted, Is.False);
            Assert.That(rejected.ReasonKey, Is.EqualTo("action.discard_selection_invalid"));
            Assert.That(accepted.Accepted, Is.True);
            Assert.That(state.GetPlayer(0).AmbushZone, Has.Exactly(1).EqualTo(ambush));
            Assert.That(state.GetPlayer(0).Graveyard, Has.Exactly(1).EqualTo(payment));
        });
    }

    private static GameState CreateAmbushState(out TurnFlow flow, out TurnActionRouter router)
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        flow = TurnFlow.CreateDefault();
        router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        return state;
    }

    private static CardInstance Ambush(long id, string kind = "NORMAL", int punish = 0)
    {
        return new CardInstance(id, 0, new CardDefinition(
            "ambush_" + id,
            "Ambush " + id,
            type: "AMBUSH",
            punish: punish,
            tags: new[] { "ambush_test" },
            ambushKind: kind,
            ambushTrigger: "OPPONENT_PLAYS_CARD"));
    }
}
}
