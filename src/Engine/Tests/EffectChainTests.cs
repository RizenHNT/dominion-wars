using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class EffectChainTests
{
    [Test]
    public void MultiEffectChainPreservesDeclaredOrder()
    {
        var game = new EffectTestFixture();
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 2, "atk"),
            new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 3, "hp"),
        }, game.Context(game.Friendly.InstanceId));
        Assert.That(game.Friendly.Attack, Is.EqualTo(4));
        Assert.That(game.Friendly.MaxHealth, Is.EqualTo(8));
    }

    [Test]
    public void MultiEffectChainCanChangeBothCombatValues()
    {
        var game = new EffectTestFixture();
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 1, "both"),
            new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", -1, "atk"),
        }, game.Context(game.Friendly.InstanceId));
        Assert.That(game.Friendly.Attack, Is.EqualTo(2));
        Assert.That(game.Friendly.Health, Is.EqualTo(6));
    }

    [Test]
    public void IntermediateDeathCanBeReversedBeforeBatchCheck()
    {
        var game = new EffectTestFixture();
        game.Friendly.Health = 1;
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", -3, "hp"),
            new EffectSpec(EffectNames.Heal, "FRIENDLY_MINION", 3),
        }, game.Context(game.Friendly.InstanceId));
        Assert.That(game.State.Players[0].Field, Does.Contain(game.Friendly));
    }

    [Test]
    public void IntermediateDeathMovesCardAfterBatchCompletes()
    {
        var game = new EffectTestFixture();
        game.Friendly.Health = 1;
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", -3, "hp"),
        }, game.Context(game.Friendly.InstanceId));
        Assert.That(game.State.Players[0].Graveyard, Does.Contain(game.Friendly));
    }

    [Test]
    public void ChainedTriggerKeepsRootEventAndChildEvent()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Damage, "ENEMY_MINION", 2, selectedTargetId: game.Enemy.InstanceId);
        Assert.That(game.State.Events.Items, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(game.State.Events.Items[1].ParentEventId, Is.EqualTo(game.State.Events.Items[0].EventId));
    }

    [Test]
    public void ChainedDamageUpdatesTargetOncePerEffect()
    {
        var game = new EffectTestFixture();
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 1),
            new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 2),
        }, game.Context(game.Enemy.InstanceId));
        Assert.That(game.Enemy.Health, Is.EqualTo(2));
    }

    [Test]
    public void NestedCounterNegatesOriginalEffect()
    {
        var game = new EffectTestFixture();
        var context = game.Context(game.Enemy.InstanceId);
        game.Dispatcher.Apply(new EffectSpec(EffectNames.Negate), context.ForSource(1, game.Enemy));
        game.Dispatcher.Apply(new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 3), context);
        Assert.That(game.Enemy.Health, Is.EqualTo(5));
    }

    [Test]
    public void ProtectedCounterDoesNotNegateNestedEffect()
    {
        var game = new EffectTestFixture();
        game.State.Players[0].ProtectedThisTurn = true;
        var context = game.Context(game.Enemy.InstanceId);
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Negate),
            new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 2),
        }, context);
        Assert.That(game.Enemy.Health, Is.EqualTo(3));
    }

    [Test]
    public void NegatedChainStopsFollowingEffects()
    {
        var game = new EffectTestFixture();
        var context = game.Context(game.Enemy.InstanceId);
        context.Negated = true;
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 2),
            new EffectSpec(EffectNames.Buff, "ENEMY_MINION", 2, "atk"),
        }, context);
        Assert.That(game.Enemy.Health, Is.EqualTo(5));
        Assert.That(game.Enemy.Attack, Is.EqualTo(2));
    }

    [Test]
    public void ChainCanDrawThenDiscardTheSameCard()
    {
        var game = new EffectTestFixture();
        var drawn = new CardInstance(10, 0, game.SoldierDefinition);
        game.State.Players[0].Deck.Add(drawn);
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Draw, amount: 1),
            new EffectSpec(EffectNames.DiscardDrawn, amount: 1),
        }, game.Context(drawnCards: new[] { drawn }));
        Assert.That(game.State.Players[0].Hand, Is.Empty);
        Assert.That(game.State.Players[0].Graveyard, Has.Count.EqualTo(1));
    }

    [Test]
    public void ChainCanApplyHealAfterDamage()
    {
        var game = new EffectTestFixture();
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Damage, "FRIENDLY_MINION", 2),
            new EffectSpec(EffectNames.Heal, "FRIENDLY_MINION", 1),
        }, game.Context(game.Friendly.InstanceId));
        Assert.That(game.Friendly.Health, Is.EqualTo(4));
    }

    [Test]
    public void ChainedEventsRetainCausalOrder()
    {
        var game = new EffectTestFixture();
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 1),
            new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 1),
        }, game.Context(game.Enemy.InstanceId));
        var events = game.State.Events.Items.Where(item => item.EventType == "DAMAGE_DEALT").ToList();
        Assert.That(events, Has.Count.EqualTo(2));
        Assert.That(events[0].EventId, Is.LessThan(events[1].EventId));
    }

    [Test]
    public void ChainDoesNotApplyEffectsAfterGameOver()
    {
        var game = new EffectTestFixture();
        game.State.Players[0].Field.Add(
            new CardInstance(20, 0, game.LeaderDefinition) { IsLeaderEntity = true });
        game.State.Players[1].Field.Add(
            new CardInstance(21, 1, game.LeaderDefinition) { IsLeaderEntity = true });
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.WinGame, param: "chain"),
            new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 3),
        }, game.Context());
        Assert.That(game.State.WinnerPlayerIndex, Is.EqualTo(0));
        Assert.That(game.Enemy.Health, Is.EqualTo(5));
    }
}
}
