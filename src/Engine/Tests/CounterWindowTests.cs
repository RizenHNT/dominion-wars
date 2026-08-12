using System.Collections.Generic;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class CounterWindowTests
{
    [Test]
    public void OnEnterBuffIsAppliedImmediately()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Buff, "FRIENDLY_MINION", 2, "atk", game.Friendly.InstanceId);

        Assert.That(game.Friendly.Attack, Is.EqualTo(4));
    }

    [Test]
    public void OnCheckDamageRequiresLeaderVulnerability()
    {
        var game = new EffectTestFixture();
        var immuneDefinition = new CardDefinition("immune", "Immune", 2, 8, isMinion: true, isLeader: true);
        var enemyLeader = new CardInstance(40, 1, immuneDefinition) { IsLeaderEntity = true };
        game.State.Players[1].Field.Add(enemyLeader);
        var context = game.Context(selectedCoreTarget: CoreTarget.Leader);

        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Damage, "ENEMY_FACE", 3, kingSlayer: true),
            context);

        Assert.That(enemyLeader.Health, Is.EqualTo(8));
    }

    [Test]
    public void OnMutatedRechecksLeaderVulnerabilityAfterStateChange()
    {
        var game = new EffectTestFixture();
        var enemyDefinition = new CardDefinition("mutable_leader", "Mutable Leader", 2, 8, isMinion: true, isLeader: true);
        var enemyLeader = new CardInstance(41, 1, enemyDefinition) { IsLeaderEntity = true };
        game.State.Players[1].Field.Add(enemyLeader);
        var effect = new EffectSpec(EffectNames.Damage, "ENEMY_FACE", 3, kingSlayer: true);

        game.Dispatcher.Apply(effect, game.Context(selectedCoreTarget: CoreTarget.Leader));
        ((HashSet<string>)enemyDefinition.Vulnerabilities).Add(EffectNames.Damage);
        game.Dispatcher.Apply(effect, game.Context(selectedCoreTarget: CoreTarget.Leader));

        Assert.That(enemyLeader.Health, Is.EqualTo(5));
    }

    [Test]
    public void OnTurnEndEffectRequestsEndTurnAndEmitsEvent()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.EndTurn);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.EndTurnRequested, Is.True);
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("TURN_FORCE_ENDED"));
        });
    }

    [Test]
    public void CounterWindowDefersDeathUntilAllEffectsResolve()
    {
        var game = new EffectTestFixture();
        game.Friendly.Health = 1;
        game.Dispatcher.ApplyAll(
            new[]
            {
                new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", -3, "hp"),
                new EffectSpec(EffectNames.Heal, "FRIENDLY_MINION", 3),
            },
            game.Context(game.Friendly.InstanceId));

        Assert.That(game.State.Players[0].Field, Does.Contain(game.Friendly));
    }

    [Test]
    public void ExplicitFalseEffectKingSlayerOverridesLegacyCardFlag()
    {
        var game = new EffectTestFixture();
        var legacyDefinition = new CardDefinition(
            "legacy", "Legacy", 1, 2, isMinion: true, kingSlayer: true);
        var legacy = new CardInstance(50, 0, legacyDefinition);
        var enemyLeader = new CardInstance(51, 1, game.LeaderDefinition) { IsLeaderEntity = true };
        game.State.Players[0].Field.Add(legacy);
        game.State.Players[1].Field.Add(enemyLeader);
        var root = game.State.Events.Append("CARD_PLAYED");
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Damage, "ENEMY_FACE", 3, kingSlayer: false),
            new EffectContext(0, root.EventId, legacy, playedCard: legacy, selectedCoreTarget: CoreTarget.Leader));

        Assert.That(enemyLeader.Health, Is.EqualTo(8));
    }

    [Test]
    public void MultiEffectChainAppliesEffectsInArrayOrder()
    {
        var game = new EffectTestFixture();
        game.Dispatcher.ApplyAll(
            new[]
            {
                new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 2, "atk"),
                new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 3, "hp"),
            },
            game.Context(game.Friendly.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(game.Friendly.Attack, Is.EqualTo(4));
            Assert.That(game.Friendly.Health, Is.EqualTo(8));
        });
    }

    [Test]
    public void MissingTargetEmitsObservableSkippedEventWithoutCrash()
    {
        var game = new EffectTestFixture();
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 2, kingSlayer: false),
            game.Context(selectedTargetId: 9999));

        Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("EFFECT_SKIPPED"));
    }
}
}
