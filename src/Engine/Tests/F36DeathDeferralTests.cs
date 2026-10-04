using System;
using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// Regression tests for death deferral across nested effect contexts.
/// </summary>
[TestFixture]
public sealed class F36DeathDeferralTests
{
    [Test]
    public void PullPayloadInsideAParentBatchMustNotSettleDeathsMidBatch()
    {
        var carrierDefinition = new CardDefinition(
            "qa_carrier",
            "QA Carrier",
            attack: 1,
            health: 3,
            isMinion: true,
            faction: "机械遗迹",
            tags: new[] { "机械" });
        var targetDefinition = new CardDefinition(
            "qa_target",
            "QA Target",
            health: 3,
            isMinion: true);
        var cargoDefinition = new CardDefinition(
            "qa_cargo",
            "QA Cargo",
            pullEffects: new[] { new EffectSpec(EffectNames.Damage, "ALL_ENEMY_MINIONS", 5) });

        var state = new GameState(
            new PlayerState(0, 20),
            new PlayerState(1, 20),
            cardLibrary: new[] { carrierDefinition, targetDefinition, cargoDefinition });

        var carrier = new CardInstance(1, 0, carrierDefinition);
        var cargo = new CardInstance(21, 0, cargoDefinition);
        var enemyA = new CardInstance(11, 1, targetDefinition);
        var enemyB = new CardInstance(12, 1, targetDefinition);
        state.GetPlayer(0).Field.Add(carrier);
        state.GetPlayer(0).CloudStack.Add(cargo);
        state.GetPlayer(1).Field.Add(enemyA);
        state.GetPlayer(1).Field.Add(enemyB);

        var root = state.Events.Append("CARD_PLAYED");
        var runtime = new EffectRuntime(state);
        var dispatcher = EffectDispatcher.CreateDefault(runtime);
        var context = new EffectContext(0, root.EventId, sourceCard: carrier);

        dispatcher.ApplyAll(
            new[]
            {
                new EffectSpec(EffectNames.Pull, "SELF"),
                new EffectSpec(EffectNames.Damage, "ALL_ENEMY_MINIONS", 5),
            },
            context);

        var damageEvents = state.Events.Items.Count(item =>
            string.Equals(item.EventType, "DAMAGE_DEALT", StringComparison.Ordinal));
        var skippedDamage = state.Events.Items.Count(item =>
            string.Equals(item.EventType, "EFFECT_SKIPPED", StringComparison.Ordinal)
            && item.Data.TryGetValue("action", out var action)
            && Equals(action, EffectNames.Damage));

        Assert.Multiple(() =>
        {
            Assert.That(damageEvents, Is.EqualTo(4),
                "PULL 载荷的 AOE 与父批第二段 AOE 都必须结算。");
            Assert.That(skippedDamage, Is.Zero,
                "父批第二段命中不到目标 = 嵌套批提前结算了死亡。");
        });
    }

    [Test]
    public void LeaderManifestInsideAParentBatchMustNotSettleDeathsMidBatch()
    {
        var leaderDefinition = new CardDefinition(
            "qa_leader",
            "QA Leader",
            isLeader: true,
            leaderEnterEffects: new[] { new EffectSpec(EffectNames.Damage, "ALL_ENEMY_MINIONS", 5) });
        var targetDefinition = new CardDefinition(
            "qa_target2",
            "QA Target 2",
            health: 3,
            isMinion: true);

        var state = new GameState(
            new PlayerState(0, 20),
            new PlayerState(1, 20),
            cardLibrary: new[] { leaderDefinition, targetDefinition });

        var leader = new CardInstance(31, 0, leaderDefinition);
        var enemyA = new CardInstance(41, 1, targetDefinition);
        var enemyB = new CardInstance(42, 1, targetDefinition);
        state.GetPlayer(0).Deck.Add(leader);
        state.GetPlayer(1).Field.Add(enemyA);
        state.GetPlayer(1).Field.Add(enemyB);

        var root = state.Events.Append("CARD_PLAYED");
        var runtime = new EffectRuntime(state);
        var dispatcher = EffectDispatcher.CreateDefault(runtime);
        var context = new EffectContext(0, root.EventId);

        dispatcher.ApplyAll(
            new[]
            {
                new EffectSpec(EffectNames.Draw, "SELF", 1),
                new EffectSpec(EffectNames.Damage, "ALL_ENEMY_MINIONS", 5),
            },
            context);

        var damageEvents = state.Events.Items.Count(item =>
            string.Equals(item.EventType, "DAMAGE_DEALT", StringComparison.Ordinal));
        var skippedDamage = state.Events.Items.Count(item =>
            string.Equals(item.EventType, "EFFECT_SKIPPED", StringComparison.Ordinal)
            && item.Data.TryGetValue("action", out var action)
            && Equals(action, EffectNames.Damage));
        var manifested = state.Events.Items.Any(item =>
            string.Equals(item.EventType, "LEADER_MANIFESTED", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(manifested, Is.True, "抽到首领必须降临，否则探针无效。");
            Assert.That(damageEvents, Is.EqualTo(4),
                "首领进场 AOE 与父批第二段 AOE 都必须结算。");
            Assert.That(skippedDamage, Is.Zero,
                "父批第二段命中不到目标 = 降临批提前结算了死亡。");
        });
    }
}
}
