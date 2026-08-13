using System.Linq;
using DominionWars.Engine;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests.Effects
{

[TestFixture]
public sealed class PunishAndBuffContractTests
{
    [Test]
    public void OpponentPunishPressureAddsPositiveDelta()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.AddOppPunishTurn, amount: 4);

        Assert.That(game.State.Players[1].PunishDeltaThisTurn, Is.EqualTo(4));
    }

    [Test]
    public void SelfPunishPressureAddsPositiveDelta()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.AddSelfPunishTurn, amount: 3);

        Assert.That(game.State.Players[0].PunishDeltaThisTurn, Is.EqualTo(3));
    }

    [Test]
    public void OpponentPunishPressureAcceptsNegativeDiscount()
    {
        var game = new EffectTestFixture();
        game.State.Players[1].PunishDeltaThisTurn = 5;
        game.Apply(EffectNames.AddOppPunishTurn, amount: -2);

        Assert.That(game.State.Players[1].PunishDeltaThisTurn, Is.EqualTo(3));
    }

    [Test]
    public void SelfPunishPressureAcceptsNegativeDiscount()
    {
        var game = new EffectTestFixture();
        game.State.Players[0].PunishDeltaThisTurn = 2;
        game.Apply(EffectNames.AddSelfPunishTurn, amount: -4);

        Assert.That(game.State.Players[0].PunishDeltaThisTurn, Is.EqualTo(-2));
    }

    [Test]
    public void PunishPressureEmitsAuditableDeltaEvent()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.AddOppPunishTurn, amount: 2);

        var evt = game.State.Events.Items[^1];
        Assert.Multiple(() =>
        {
            Assert.That(evt.EventType, Is.EqualTo("PUNISH_DELTA_APPLIED"));
            Assert.That(evt.Data["player"], Is.EqualTo(1));
            Assert.That(evt.Data["amount"], Is.EqualTo(2));
        });
    }

    [Test]
    public void PunishToDiscardTargetsOpponentAndDoesNotChangePressure()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.ConvertPunishToDiscard);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[1].PunishToSelfDiscardThisTurn, Is.True);
            Assert.That(game.State.Players[0].PunishToSelfDiscardThisTurn, Is.False);
            Assert.That(game.State.Players[1].PunishDeltaThisTurn, Is.Zero);
        });
    }

    [Test]
    public void PunishCardUsesCanonicalPlayPathAndPreservesCost()
    {
        var state = new GameState();
        state.GetPlayer(0).Hand.Add(new CardInstance(90, 0, new CardDefinition(
            "punish-contract", "Punish Contract", punishActivatable: true, punishCost: 3)));

        state.GetPlayer(0).Hand[0].PunishActivated = true;
        var action = new LegalActionGenerator().Generate(state, 0)
            .Single(item => item.Type == LegalActionGenerator.PlayCard);

        Assert.That(action.Payload["punish"], Is.EqualTo(3));
    }

    [Test]
    public void ZeroDamageIsSkippedWithoutChangingLife()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Damage, "ENEMY_PLAYER", amount: 0);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[1].Life, Is.EqualTo(20));
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("EFFECT_SKIPPED"));
        });
    }

    [Test]
    public void NegativeDamageIsSkippedWithoutChangingLife()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Damage, "ENEMY_PLAYER", amount: -2);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[1].Life, Is.EqualTo(20));
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("EFFECT_SKIPPED"));
        });
    }

    [Test]
    public void BuffNegativeAttackClampsAtZero()
    {
        var game = new EffectTestFixture();
        game.Friendly.Attack = 2;
        game.Apply(EffectNames.Buff, "FRIENDLY_MINION", amount: -9, param: "atk", selectedTargetId: game.Friendly.InstanceId);

        Assert.That(game.Friendly.Attack, Is.Zero);
    }

    [Test]
    public void BuffNegativeHealthRemainsNegativeUntilRuleCheck()
    {
        var game = new EffectTestFixture();
        game.Friendly.Health = 1;
        game.Apply(EffectNames.Buff, "FRIENDLY_MINION", amount: -3, param: "hp", selectedTargetId: game.Friendly.InstanceId);

        Assert.That(game.Friendly.Health, Is.EqualTo(-2));
    }

    [Test]
    public void ZeroBuffIsSkippedWithoutChangingCombatValues()
    {
        var game = new EffectTestFixture();
        var beforeAttack = game.Friendly.Attack;
        var beforeHealth = game.Friendly.Health;
        game.Apply(EffectNames.Buff, "FRIENDLY_MINION", amount: 0, param: "both", selectedTargetId: game.Friendly.InstanceId);

        Assert.Multiple(() =>
        {
            Assert.That(game.Friendly.Attack, Is.EqualTo(beforeAttack));
            Assert.That(game.Friendly.Health, Is.EqualTo(beforeHealth));
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("EFFECT_SKIPPED"));
        });
    }

    [Test]
    public void BuffInvalidModeIsSkippedWithoutSideEffect()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Buff, "FRIENDLY_MINION", amount: 2, param: "cost", selectedTargetId: game.Friendly.InstanceId);

        Assert.Multiple(() =>
        {
            Assert.That(game.Friendly.Attack, Is.EqualTo(2));
            Assert.That(game.Friendly.Health, Is.EqualTo(5));
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("EFFECT_SKIPPED"));
        });
    }

    [Test]
    public void MissingSingleTargetIsSkippedInsteadOfChoosingFirst()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Buff, "ANY_MINION", amount: 2, param: "atk");

        Assert.Multiple(() =>
        {
            Assert.That(game.Enemy.Attack, Is.EqualTo(2));
            Assert.That(game.WardEnemy.Attack, Is.EqualTo(1));
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("EFFECT_SKIPPED"));
        });
    }

    [Test]
    public void PunishPressureEventsRemainInRootCausalChain()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.AddSelfPunishTurn, amount: 1);

        var root = game.State.Events.Items[0];
        var applied = game.State.Events.Items.Last();
        Assert.Multiple(() =>
        {
            Assert.That(root.EventType, Is.EqualTo("CARD_PLAYED"));
            Assert.That(applied.EventType, Is.EqualTo("PUNISH_DELTA_APPLIED"));
            Assert.That(applied.ParentEventId, Is.EqualTo(root.EventId));
        });
    }
}
}
