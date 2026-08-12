using System;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class EdgeCaseTests
{
    [Test]
    public void DrawingFromEmptyDeckDoesNotThrow()
    {
        var game = new EffectTestFixture();
        Assert.DoesNotThrow(() => game.Apply(EffectNames.Draw, amount: 1));
        Assert.That(game.State.Players[0].Hand, Is.Empty);
    }

    [Test]
    public void HealingZeroHealthUnitDoesNotThrow()
    {
        var game = new EffectTestFixture();
        game.Friendly.Health = 0;
        Assert.DoesNotThrow(() => game.Apply(
            EffectNames.Heal, "FRIENDLY_MINION", 2, selectedTargetId: game.Friendly.InstanceId));
    }

    [Test]
    public void DamageCannotLeaveAUnitBelowItsDeathState()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Damage, "ENEMY_MINION", 99, selectedTargetId: game.Enemy.InstanceId);
        Assert.That(game.State.Players[1].Field, Does.Not.Contain(game.Enemy));
        Assert.That(game.State.Players[1].Graveyard, Does.Contain(game.Enemy));
    }

    [Test]
    public void NegativeDamageIsRejectedWithoutMutation()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Damage, "ENEMY_MINION", -1, selectedTargetId: game.Enemy.InstanceId);
        Assert.That(game.Enemy.Health, Is.EqualTo(5));
    }

    [Test]
    public void SameNameBuffsStackInSequence()
    {
        var game = new EffectTestFixture();
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 1, "atk"),
            new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 2, "atk"),
        }, game.Context(game.Friendly.InstanceId));
        Assert.That(game.Friendly.Attack, Is.EqualTo(5));
    }

    [Test]
    public void MissingTargetEmitsFailureInsteadOfThrowing()
    {
        var game = new EffectTestFixture();
        Assert.DoesNotThrow(() => game.Apply(
            EffectNames.Destroy, "ENEMY_MINION", selectedTargetId: 999999));
        Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("EFFECT_SKIPPED"));
    }

    [Test]
    public void DrawingPastDeckSizeStopsAtAvailableCards()
    {
        var game = new EffectTestFixture();
        game.State.Players[0].Deck.Add(new CardInstance(50, 0, game.SoldierDefinition));
        game.Apply(EffectNames.Draw, amount: 10);
        Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(1));
    }

    [Test]
    public void NegativeHealDoesNotIncreaseHealth()
    {
        var game = new EffectTestFixture();
        game.Friendly.Health = 2;
        game.Apply(EffectNames.Heal, "FRIENDLY_MINION", -5, selectedTargetId: game.Friendly.InstanceId);
        Assert.That(game.Friendly.Health, Is.EqualTo(2));
    }

    [Test]
    public void ZeroBuffLeavesCombatValuesUnchanged()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Buff, "FRIENDLY_MINION", 0, "both", game.Friendly.InstanceId);
        Assert.That(game.Friendly.Attack, Is.EqualTo(2));
        Assert.That(game.Friendly.Health, Is.EqualTo(5));
    }

    [Test]
    public void LeaderDamageEndsGameWhenBothLeadersAreFielded()
    {
        var game = new EffectTestFixture();
        game.State.Players[0].Field.Add(
            new CardInstance(60, 0, game.LeaderDefinition) { IsLeaderEntity = true });
        var enemyLeader = new CardInstance(61, 1, game.LeaderDefinition) { IsLeaderEntity = true };
        game.State.Players[1].Field.Add(enemyLeader);
        game.Dispatcher.Apply(new EffectSpec(EffectNames.Damage, "ENEMY_FACE", 99, kingSlayer: true),
            game.Context(selectedCoreTarget: CoreTarget.Leader));
        Assert.That(game.State.WinnerPlayerIndex, Is.EqualTo(0));
    }

    [Test]
    public void SnapshotWithEmptyZonesStillHasTwoPlayers()
    {
        var snapshot = EngineProjectionAdapter.ToSnapshot(new GameState(), "edge", 0, "START");
        Assert.That(snapshot.Players, Has.Count.EqualTo(2));
        Assert.That(snapshot.Players.All(player => player.Field.Count == 0), Is.True);
    }

    [Test]
    public void EventIdsRemainMonotonicAcrossMultipleEffects()
    {
        var game = new EffectTestFixture();
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.GainLife, amount: 1),
            new EffectSpec(EffectNames.LoseLife, amount: 1),
            new EffectSpec(EffectNames.EndTurn),
        }, game.Context());
        Assert.That(game.State.Events.Items.Select(item => item.EventId), Is.Ordered.Ascending);
    }

    [Test]
    public void RestoreAttacksClearsSummonedTurnRestriction()
    {
        var game = new EffectTestFixture();
        game.Friendly.AttacksUsed = 1;
        game.Friendly.SummonedThisTurn = true;
        game.Apply(EffectNames.RestoreAttacks, "FRIENDLY_MINION", selectedTargetId: game.Friendly.InstanceId);
        Assert.That(game.Friendly.AttacksUsed, Is.Zero);
        Assert.That(game.Friendly.SummonedThisTurn, Is.False);
    }

    [Test]
    public void EmptyHandDiscardIsSafe()
    {
        var game = new EffectTestFixture();
        Assert.DoesNotThrow(() => game.Apply(EffectNames.DiscardOppRandom, amount: 3));
        Assert.That(game.State.Players[1].Hand, Is.Empty);
    }

    [Test]
    public void EffectChainMaintainsDeclaredOrderAtBoundary()
    {
        var game = new EffectTestFixture();
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 3, "atk"),
            new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", -2, "atk"),
        }, game.Context(game.Friendly.InstanceId));
        Assert.That(game.Friendly.Attack, Is.EqualTo(3));
    }
}
}
