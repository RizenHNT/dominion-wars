using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class AmbushTriggerResolverTests
{
    [Test]
    public void AttackNegateConsumesAttackAndMovesAmbushToGrave()
    {
        var state = ActionState(out var flow, out var router);
        var attacker = Minion(10, 0, "attacker", 3, 4);
        var target = Minion(11, 1, "target", 1, 5);
        var ambush = Ambush(12, 1, "OPPONENT_ATTACKS", "NORMAL",
            new EffectSpec(EffectNames.Negate));
        state.GetPlayer(0).Field.Add(attacker);
        state.GetPlayer(1).Field.Add(target);
        state.GetPlayer(1).AmbushZone.Add(ambush);

        var action = flow.GetLegalActions(state, 0)
            .Single(item => item.Type == LegalActionGenerator.Attack
                && item.SourceId == attacker.InstanceId
                && item.TargetReferenceId == "entity_000000000011");
        var result = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.Attack, action.ActionId, attacker.InstanceId, action.TargetReferenceId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(target.Health, Is.EqualTo(5));
            Assert.That(attacker.Health, Is.EqualTo(4));
            Assert.That(attacker.AttacksUsed, Is.EqualTo(1));
            Assert.That(state.GetPlayer(1).AmbushZone, Is.Empty);
            Assert.That(state.GetPlayer(1).Graveyard, Has.Exactly(1).EqualTo(ambush));
            Assert.That(state.GetPlayer(1).UsedTags, Does.Contain("ambush_tag"));
            Assert.That(state.Events.Items.Count(item => item.EventType == "AMBUSH_TRIGGERED"), Is.EqualTo(1));
        });
    }

    [Test]
    public void SpellNegateSuppressesOriginalEffects()
    {
        var state = ActionState(out _, out var router);
        var spell = new CardInstance(20, 0, new CardDefinition(
            "spell", "Spell", type: "SPELL",
            onPlayEffects: new[] { new EffectSpec(EffectNames.Draw, amount: 1) }));
        var deckCard = new CardInstance(21, 0, new CardDefinition("deck", "Deck"));
        var ambush = Ambush(22, 1, "OPPONENT_PLAYS_SPELL", "FOCUS",
            new EffectSpec(EffectNames.Negate));
        state.GetPlayer(0).Hand.Add(spell);
        state.GetPlayer(0).Deck.Add(deckCard);
        state.GetPlayer(1).AmbushZone.Add(ambush);

        var result = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: spell.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.GetPlayer(0).Deck, Has.Exactly(1).EqualTo(deckCard));
            Assert.That(state.GetPlayer(0).Hand, Is.Empty);
            Assert.That(state.GetPlayer(0).Graveyard, Has.Exactly(1).EqualTo(spell));
            Assert.That(state.GetPlayer(1).AmbushFocusTriggeredThisTurn, Is.True);
        });
    }

    [Test]
    public void SummonAmbushTargetsTheSummonedMinion()
    {
        var state = ActionState(out _, out var router);
        var minion = Minion(30, 0, "summoned", 2, 2);
        var ambush = Ambush(31, 1, "OPPONENT_SUMMONS", "NORMAL",
            new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 3));
        state.GetPlayer(0).Hand.Add(minion);
        state.GetPlayer(1).AmbushZone.Add(ambush);

        var result = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: minion.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.GetPlayer(0).Field, Does.Not.Contain(minion));
            Assert.That(state.GetPlayer(0).Graveyard, Does.Contain(minion));
            Assert.That(state.GetPlayer(1).Graveyard, Does.Contain(ambush));
        });
    }

    [Test]
    public void DrawAmbushCanDiscardExactlyTheDrawnCards()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var drawn = new CardInstance(40, 0, new CardDefinition("drawn", "Drawn"));
        var ambush = Ambush(41, 1, "OPPONENT_DRAWS", "FOCUS",
            new EffectSpec(EffectNames.DiscardDrawn));
        state.GetPlayer(0).Deck.Add(drawn);
        state.GetPlayer(1).AmbushZone.Add(ambush);

        TurnFlow.CreateDefault().Advance(state, 0);

        Assert.Multiple(() =>
        {
            Assert.That(state.GetPlayer(0).Hand, Is.Empty);
            Assert.That(state.GetPlayer(0).Graveyard, Has.Exactly(1).EqualTo(drawn));
            Assert.That(state.GetPlayer(1).Graveyard, Has.Exactly(1).EqualTo(ambush));
        });
    }

    [Test]
    public void FocusAndRootWindowPreventAdditionalResponses()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var focus = Ambush(50, 1, "OPPONENT_PLAYS_CARD", "FOCUS");
        var normal = Ambush(51, 1, "OPPONENT_PLAYS_CARD", "NORMAL");
        state.GetPlayer(1).AmbushZone.Add(focus);
        state.GetPlayer(1).AmbushZone.Add(normal);
        var resolver = new AmbushTriggerResolver();
        var firstRoot = state.Events.Append("CARD_PLAYED", null);
        var first = resolver.Resolve(state, 0, new[] { "OPPONENT_PLAYS_CARD" }, firstRoot.EventId);
        var sameRoot = resolver.Resolve(state, 0, new[] { "OPPONENT_PLAYS_CARD" }, firstRoot.EventId);
        var secondRoot = state.Events.Append("CARD_PLAYED", null);
        var suppressed = resolver.Resolve(state, 0, new[] { "OPPONENT_PLAYS_CARD" }, secondRoot.EventId);

        Assert.Multiple(() =>
        {
            Assert.That(first.Triggered, Is.True);
            Assert.That(sameRoot.Triggered, Is.False);
            Assert.That(suppressed.Triggered, Is.False);
            Assert.That(state.GetPlayer(1).AmbushZone, Has.Exactly(1).EqualTo(normal));
        });
    }

    [Test]
    public void AmbushLeaderReturnsToDeckWhenLeaderGatePreventsItsWin()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var leader = new CardInstance(60, 1, new CardDefinition(
            "ambush_leader",
            "Ambush Leader",
            isLeader: true,
            type: "AMBUSH",
            ambushKind: "FOCUS",
            ambushTrigger: "OPPONENT_PLAYS_CARD",
            ambushEffects: new[] { new EffectSpec(EffectNames.WinGame, param: "leader_gate_test") }))
        {
            IsLeaderEntity = true,
        };
        state.GetPlayer(1).AmbushZone.Add(leader);
        var root = state.Events.Append("CARD_PLAYED", null);

        var result = new AmbushTriggerResolver().Resolve(
            state, 0, new[] { "OPPONENT_PLAYS_CARD" }, root.EventId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Triggered, Is.True);
            Assert.That(state.WinnerPlayerIndex, Is.Null);
            Assert.That(state.GetPlayer(1).AmbushZone, Is.Empty);
            Assert.That(state.GetPlayer(1).Deck, Has.Exactly(1).EqualTo(leader));
            Assert.That(leader.IsLeaderEntity, Is.False);
        });
    }

    private static GameState ActionState(out TurnFlow flow, out TurnActionRouter router)
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        flow = TurnFlow.CreateDefault();
        router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        Assert.That(router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted, Is.True);
        return state;
    }

    private static CardInstance Minion(long id, int owner, string cardId, int attack, int health)
        => new CardInstance(id, owner, new CardDefinition(
            cardId, cardId, attack, health, isMinion: true, type: "MINION"));

    private static CardInstance Ambush(
        long id,
        int owner,
        string trigger,
        string kind,
        params EffectSpec[] effects)
        => new CardInstance(id, owner, new CardDefinition(
            "ambush_" + id,
            "Ambush " + id,
            type: "AMBUSH",
            tags: new[] { "ambush_tag" },
            ambushKind: kind,
            ambushTrigger: trigger,
            ambushEffects: effects));
}
}
