using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Data;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class OpponentDiscardEffectsTests
{
    [Test]
    public void ProductionSeaHooksTriggerOncePerActualEffectDiscard()
    {
        var root = FindRepositoryRoot();
        var catalog = CardCatalog.LoadDirectory(Path.Combine(root, "data", "cards"));
        var state = new GameState(
            new PlayerState(0, 20),
            new PlayerState(1, 20),
            cardLibrary: catalog.Cards.Values)
        {
            CastleEnabled = true,
            CastleHealth = 75,
        };
        var siren = new CardInstance(1, 0, catalog.Cards["sea_siren"]);
        var warden = new CardInstance(2, 0, catalog.Cards["sea_warden"]);
        state.GetPlayer(0).Field.Add(siren);
        state.GetPlayer(0).Field.Add(warden);
        for (var id = 10; id < 13; id++)
        {
            state.GetPlayer(1).Hand.Add(new CardInstance(
                id,
                1,
                new CardDefinition("discarded_" + id, "Discarded")));
        }

        var rootEvent = state.Events.Append("TEST_ROOT");
        var runtime = new EffectRuntime(state);
        runtime.DiscardOpponentRandom(
            new EffectSpec(EffectNames.DiscardOppRandom, amount: 5),
            new EffectContext(0, rootEvent.EventId));

        Assert.Multiple(() =>
        {
            Assert.That(siren.Attack, Is.EqualTo(5));
            Assert.That(siren.Health, Is.EqualTo(6));
            Assert.That(state.CastleHealth, Is.EqualTo(72));
            Assert.That(state.GetPlayer(1).TotalDiscarded, Is.EqualTo(3));
            Assert.That(state.Events.Items.Last(item => item.EventType == "CARDS_DISCARDED")
                .Data["count"], Is.EqualTo(3));
        });
    }

    [Test]
    public void DiscardDrawnTriggersHooksForEachCardActuallyStillInHand()
    {
        var state = CreateStateWithHook(out var hook);
        var first = AddHandCard(state, 10, 1);
        var second = AddHandCard(state, 11, 1);
        var missing = new CardInstance(12, 1, new CardDefinition("missing", "Missing"));
        var rootEvent = state.Events.Append("TEST_ROOT");

        new EffectRuntime(state).DiscardDrawn(
            new EffectSpec(EffectNames.DiscardDrawn),
            new EffectContext(
                1,
                rootEvent.EventId,
                drawnCards: new[] { first, second, missing }));

        Assert.Multiple(() =>
        {
            Assert.That(hook.Attack, Is.EqualTo(3));
            Assert.That(hook.Health, Is.EqualTo(4));
            Assert.That(state.GetPlayer(1).TotalDiscarded, Is.EqualTo(2));
            Assert.That(state.GetPlayer(1).Graveyard, Is.EquivalentTo(new[] { first, second }));
        });
    }

    [Test]
    public void SealedHookLosesOpponentDiscardAbility()
    {
        var state = CreateStateWithHook(out var hook);
        hook.Sealed = true;
        AddHandCard(state, 10, 1);
        var rootEvent = state.Events.Append("TEST_ROOT");

        new EffectRuntime(state).DiscardOpponentRandom(
            new EffectSpec(EffectNames.DiscardOppRandom, amount: 1),
            new EffectContext(0, rootEvent.EventId));

        Assert.Multiple(() =>
        {
            Assert.That(hook.Attack, Is.EqualTo(1));
            Assert.That(hook.Health, Is.EqualTo(2));
            Assert.That(state.GetPlayer(1).TotalDiscarded, Is.EqualTo(1));
        });
    }

    [Test]
    public void NegatedHookOwnerDoesNotTriggerOpponentDiscardAbility()
    {
        var state = CreateStateWithHook(out var hook);
        state.GetPlayer(0).EffectsNegatedThisTurn = true;
        AddHandCard(state, 10, 1);
        var rootEvent = state.Events.Append("TEST_ROOT");

        new EffectRuntime(state).DiscardOpponentRandom(
            new EffectSpec(EffectNames.DiscardOppRandom, amount: 1),
            new EffectContext(0, rootEvent.EventId));

        Assert.Multiple(() =>
        {
            Assert.That(hook.Attack, Is.EqualTo(1));
            Assert.That(hook.Health, Is.EqualTo(2));
            Assert.That(state.GetPlayer(1).TotalDiscarded, Is.EqualTo(1));
        });
    }

    [Test]
    public void HandLimitDiscardDoesNotTriggerHooksOrDiscardCounter()
    {
        var state = new GameState(
            new PlayerState(0, 20),
            new PlayerState(1, 20),
            rules: new DominionWars.Engine.Rules.MatchRules(handLimit: 8));
        var hook = AddHook(state, 0, 1);
        for (var id = 10; id < 20; id++)
        {
            AddHandCard(state, id, 1);
        }

        state.CurrentPlayerIndex = 1;
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        flow.JumpTo(state, 1, TurnPhase.Discard);
        var action = flow.GetLegalActions(state, 1).Single();
        var candidates = (long[])action.Payload["candidateIds"]!;
        var result = router.Execute(state, new GameActionRequest(
            1,
            TurnAction.DiscardComplete,
            action.ActionId,
            selectedEntityIds: new[] { candidates[0], candidates[1] }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(hook.Attack, Is.EqualTo(1));
            Assert.That(hook.Health, Is.EqualTo(2));
            Assert.That(state.GetPlayer(1).TotalDiscarded, Is.Zero);
        });
    }

    [Test]
    public void ConvertedPlayPunishDiscardTriggersHooksPerPaymentCard()
    {
        var state = CreateStateInActionPhase(out _, out var router);
        var hook = AddHook(state, 1, 40);
        state.GetPlayer(0).PunishToSelfDiscardThisTurn = true;
        var played = new CardInstance(41, 0, new CardDefinition("played", "Played", punish: 2));
        var first = AddHandCard(state, 42, 0);
        var second = AddHandCard(state, 43, 0);
        state.GetPlayer(0).Hand.Add(played);

        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.PlayCard,
            sourceEntityId: played.InstanceId,
            selectedEntityIds: new[] { first.InstanceId, second.InstanceId }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(hook.Attack, Is.EqualTo(3));
            Assert.That(hook.Health, Is.EqualTo(4));
            Assert.That(state.GetPlayer(0).TotalDiscarded, Is.EqualTo(2));
        });
    }

    [Test]
    public void ConvertedAmbushPunishDiscardTriggersHooksPerPaymentCard()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        var hook = AddHook(state, 1, 50);
        state.GetPlayer(0).PunishToSelfDiscardThisTurn = true;
        var ambush = new CardInstance(51, 0, new CardDefinition(
            "ambush",
            "Ambush",
            type: "AMBUSH",
            punish: 2,
            ambushTrigger: "OPPONENT_PLAYS_CARD"));
        var first = AddHandCard(state, 52, 0);
        var second = AddHandCard(state, 53, 0);
        state.GetPlayer(0).Hand.Add(ambush);

        var result = router.Execute(state, new GameActionRequest(
            0,
            TurnAction.SetAmbush,
            AmbushActionHandler.ActionId(ambush.InstanceId),
            sourceEntityId: ambush.InstanceId,
            selectedEntityIds: new[] { first.InstanceId, second.InstanceId }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(hook.Attack, Is.EqualTo(3));
            Assert.That(hook.Health, Is.EqualTo(4));
            Assert.That(state.GetPlayer(0).TotalDiscarded, Is.EqualTo(2));
            Assert.That(state.GetPlayer(0).AmbushZone, Does.Contain(ambush));
        });
    }

    private static GameState CreateStateWithHook(out CardInstance hook)
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        hook = AddHook(state, 0, 1);
        return state;
    }

    private static CardInstance AddHook(GameState state, int owner, long instanceId)
    {
        var hook = new CardInstance(instanceId, owner, new CardDefinition(
            "discard_hook_" + instanceId,
            "Discard Hook",
            attack: 1,
            health: 2,
            isMinion: true,
            onOpponentDiscardEffects: new[]
            {
                new EffectSpec(EffectNames.Buff, "SELF", 1, "both"),
            }));
        state.GetPlayer(owner).Field.Add(hook);
        return hook;
    }

    private static CardInstance AddHandCard(GameState state, long instanceId, int owner)
    {
        var card = new CardInstance(
            instanceId,
            owner,
            new CardDefinition("hand_" + instanceId, "Hand " + instanceId));
        state.GetPlayer(owner).Hand.Add(card);
        return card;
    }

    private static GameState CreateStateInActionPhase(
        out TurnFlow flow,
        out TurnActionRouter router)
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        flow = TurnFlow.CreateDefault();
        router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        Assert.That(router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted, Is.True);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
        return state;
    }

    private static string FindRepositoryRoot()
    {
        var current = TestContext.CurrentContext.TestDirectory;
        while (!Directory.Exists(Path.Combine(current, "data", "cards")))
        {
            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                throw new DirectoryNotFoundException("Could not locate the repository data directory.");
            }

            current = parent.FullName;
        }

        return current;
    }
}
}
