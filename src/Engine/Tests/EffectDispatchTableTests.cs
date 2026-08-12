using System;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class EffectDispatchTableTests
{
    [TestCase(EffectNames.Damage)]
    [TestCase(EffectNames.Heal)]
    [TestCase(EffectNames.Draw)]
    [TestCase(EffectNames.OppDraw)]
    [TestCase(EffectNames.DiscardOppRandom)]
    [TestCase(EffectNames.DiscardDrawn)]
    [TestCase(EffectNames.Destroy)]
    [TestCase(EffectNames.Buff)]
    [TestCase(EffectNames.GrantKeyword)]
    [TestCase(EffectNames.Summon)]
    [TestCase(EffectNames.SummonLeader)]
    [TestCase(EffectNames.EndTurn)]
    [TestCase(EffectNames.AddOppPunishTurn)]
    [TestCase(EffectNames.AddSelfPunishTurn)]
    [TestCase(EffectNames.ConvertPunishToDiscard)]
    [TestCase(EffectNames.ProtectTurn)]
    [TestCase(EffectNames.Negate)]
    [TestCase(EffectNames.NegateEnemyEffectsTurn)]
    [TestCase(EffectNames.SkipReshuffle)]
    [TestCase(EffectNames.RestoreAttacks)]
    [TestCase(EffectNames.GainLife)]
    [TestCase(EffectNames.LoseLife)]
    [TestCase(EffectNames.DamageCastle)]
    [TestCase(EffectNames.WinGame)]
    public void EveryContractActionIsRegisteredAndDispatchable(string action)
    {
        var game = new EffectTestFixture();
        Assert.That(game.Dispatcher.RegisteredActions, Does.Contain(action));
        Assert.DoesNotThrow(() => game.Dispatcher.Apply(
            new EffectSpec(action), game.Context()));
    }

    [TestCase(1, "ENEMY_MINION")]
    [TestCase(3, "ENEMY_MINION")]
    [TestCase(5, "ENEMY_MINION")]
    [TestCase(1, "ENEMY_FACE")]
    [TestCase(3, "ENEMY_FACE")]
    [TestCase(5, "ENEMY_FACE")]
    [TestCase(1, "SELF")]
    [TestCase(3, "SELF")]
    [TestCase(5, "SELF")]
    public void DamageMatrixAppliesPositiveAmountsToEachTarget(int amount, string target)
    {
        var game = new EffectTestFixture();
        var targetId = target == "ENEMY_MINION" ? game.Enemy.InstanceId : (long?)null;
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Damage, target, amount),
            game.Context(targetId));

        if (target == "ENEMY_MINION")
        {
            Assert.That(game.Enemy.Health, Is.EqualTo(5 - amount));
        }
        else if (target == "ENEMY_FACE")
        {
            Assert.That(game.State.Players[1].Life, Is.EqualTo(20 - amount));
        }
        else
        {
            Assert.That(game.Source.Health, Is.EqualTo(4 - amount));
        }
    }

    [TestCase(1)]
    [TestCase(-1)]
    [TestCase(99)]
    public void HealMatrixCoversPositiveNegativeAndOverflow(int amount)
    {
        var game = new EffectTestFixture();
        game.Friendly.Health = 2;
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Heal, "FRIENDLY_MINION", amount),
            game.Context(game.Friendly.InstanceId));

        var expected = amount > 0 ? Math.Min(5, 2 + amount) : 2;
        Assert.That(game.Friendly.Health, Is.EqualTo(expected));
    }

    [TestCase(3, "atk", 5, 5, 5)]
    [TestCase(-3, "atk", 0, 5, 5)]
    [TestCase(-3, "hp", 2, 2, 2)]
    public void BuffMatrixCoversClampAndStatModes(int amount, string mode, int expectedAttack, int expectedHealth, int expectedMaxHealth)
    {
        var game = new EffectTestFixture();
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", amount, mode),
            game.Context(game.Friendly.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(game.Friendly.Attack, Is.EqualTo(expectedAttack));
            Assert.That(game.Friendly.Health, Is.EqualTo(expectedHealth));
            Assert.That(game.Friendly.MaxHealth, Is.EqualTo(expectedMaxHealth));
        });
    }

    [TestCase(1)]
    [TestCase(0)]
    [TestCase(-1)]
    public void DrawMatrixClampsToAtLeastOneCard(int amount)
    {
        var game = new EffectTestFixture();
        game.State.Players[0].Deck.Add(new CardInstance(20, 0, game.SoldierDefinition));
        game.Dispatcher.Apply(new EffectSpec(EffectNames.Draw, amount: amount), game.Context());

        Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(1));
    }

    [TestCase("token")]
    [TestCase("soldier")]
    public void SummonMatrixUsesStableMinionIds(string cardId)
    {
        var game = new EffectTestFixture();
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Summon, amount: 1, param: cardId),
            game.Context());

        Assert.That(game.State.Players[0].Field, Has.Count.EqualTo(3));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DestroyMatrixHonorsProtection(bool protectedThisTurn)
    {
        var game = new EffectTestFixture();
        game.State.Players[1].ProtectedThisTurn = protectedThisTurn;
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Destroy, "ENEMY_MINION"),
            game.Context(game.Enemy.InstanceId));

        Assert.That(game.State.Players[1].Field.Contains(game.Enemy), Is.EqualTo(protectedThisTurn));
    }
}
}
