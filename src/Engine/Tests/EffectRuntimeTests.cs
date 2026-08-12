using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class EffectRuntimeTests
{
    [Test]
    public void DamageDamagesSelectedEnemy()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Damage, "ENEMY_MINION", 3, selectedTargetId: game.Enemy.InstanceId);
        Assert.That(game.Enemy.Health, Is.EqualTo(2));
        Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("DAMAGE_DEALT"));
    }

    [Test]
    public void HealClampsAtMaximumHealth()
    {
        var game = new EffectTestFixture();
        game.Friendly.Health = 2;
        game.Apply(EffectNames.Heal, "FRIENDLY_MINION", 99, selectedTargetId: game.Friendly.InstanceId);
        Assert.That(game.Friendly.Health, Is.EqualTo(game.Friendly.MaxHealth));
    }

    [Test]
    public void DrawMovesCardsFromOwnDeckToHand()
    {
        var game = new EffectTestFixture();
        game.State.Players[0].Deck.Add(new CardInstance(10, 0, game.SoldierDefinition));
        game.Apply(EffectNames.Draw, amount: 1);
        Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(1));
    }

    [Test]
    public void OppDrawMovesCardsFromOpponentDeckToHand()
    {
        var game = new EffectTestFixture();
        game.State.Players[1].Deck.Add(new CardInstance(10, 1, game.SoldierDefinition));
        game.Apply(EffectNames.OppDraw, amount: 1);
        Assert.That(game.State.Players[1].Hand, Has.Count.EqualTo(1));
    }

    [Test]
    public void DiscardOpponentRandomMovesAHandCardToGraveyard()
    {
        var game = new EffectTestFixture();
        game.State.Players[1].Hand.Add(new CardInstance(10, 1, game.SoldierDefinition));
        game.Apply(EffectNames.DiscardOppRandom, amount: 1);
        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[1].Hand, Is.Empty);
            Assert.That(game.State.Players[1].Graveyard, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void DiscardDrawnOnlyDiscardsCardsStillInHand()
    {
        var game = new EffectTestFixture();
        var drawn = new CardInstance(10, 1, game.SoldierDefinition);
        game.State.Players[1].Hand.Add(drawn);
        game.Apply(EffectNames.DiscardDrawn, drawnCards: new[] { drawn });
        Assert.That(game.State.Players[1].Graveyard, Does.Contain(drawn));
    }

    [Test]
    public void DestroyMovesAnUnprotectedMinionToGraveyard()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Destroy, "ENEMY_MINION", selectedTargetId: game.Enemy.InstanceId);
        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[1].Field, Does.Not.Contain(game.Enemy));
            Assert.That(game.State.Players[1].Graveyard, Does.Contain(game.Enemy));
        });
    }

    [Test]
    public void BuffUpdatesAttackAndHealth()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Buff, "FRIENDLY_MINION", 2, "both", game.Friendly.InstanceId);
        Assert.Multiple(() =>
        {
            Assert.That(game.Friendly.Attack, Is.EqualTo(4));
            Assert.That(game.Friendly.Health, Is.EqualTo(7));
            Assert.That(game.Friendly.MaxHealth, Is.EqualTo(7));
        });
    }

    [Test]
    public void NegativeAttackBuffClampsAtZero()
    {
        var game = new EffectTestFixture();
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", -99, "atk"),
            game.Context(game.Friendly.InstanceId));

        Assert.That(game.Friendly.Attack, Is.Zero);
    }

    [Test]
    public void PendingDeathCanBeReversedByLaterHealInSameBatch()
    {
        var game = new EffectTestFixture();
        game.Friendly.Health = 1;
        game.Friendly.MaxHealth = 5;
        var context = game.Context(game.Friendly.InstanceId);

        game.Dispatcher.ApplyAll(
            new[]
            {
                new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", -3, "hp"),
                new EffectSpec(EffectNames.Heal, "FRIENDLY_MINION", 3),
            },
            context);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].Field, Does.Contain(game.Friendly));
            Assert.That(game.Friendly.Health, Is.EqualTo(1));
            Assert.That(game.Friendly.MaxHealth, Is.EqualTo(2));
        });
    }

    [Test]
    public void PendingDeathIsCleanedUpAfterBatchResolves()
    {
        var game = new EffectTestFixture();
        game.Friendly.Health = 1;
        var context = game.Context(game.Friendly.InstanceId);

        game.Dispatcher.ApplyAll(
            new[] { new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", -3, "hp") },
            context);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].Field, Does.Not.Contain(game.Friendly));
            Assert.That(game.State.Players[0].Graveyard, Does.Contain(game.Friendly));
        });
    }

    [Test]
    public void ZeroAmountBuffIsSkipped()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Buff, "FRIENDLY_MINION", 0, "both", game.Friendly.InstanceId);

        Assert.That(game.Friendly.Attack, Is.EqualTo(2));
        Assert.That(game.Friendly.Health, Is.EqualTo(5));
    }

    [Test]
    public void SelfTargetResolvesTheSourceMinion()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Buff, "SELF", 1, "both");

        Assert.Multiple(() =>
        {
            Assert.That(game.Source.Attack, Is.EqualTo(2));
            Assert.That(game.Source.Health, Is.EqualTo(5));
        });
    }

    [Test]
    public void AnyMinionCanResolveAnExplicitFriendlySelection()
    {
        var game = new EffectTestFixture();
        game.Apply(
            EffectNames.Buff,
            "ANY_MINION",
            1,
            "atk",
            selectedTargetId: game.Friendly.InstanceId);

        Assert.That(game.Friendly.Attack, Is.EqualTo(3));
    }

    [Test]
    public void AnyMinionCanResolveAnExplicitEnemySelection()
    {
        var game = new EffectTestFixture();
        game.Apply(
            EffectNames.Buff,
            "ANY_MINION",
            1,
            "hp",
            selectedTargetId: game.Enemy.InstanceId);

        Assert.That(game.Enemy.Health, Is.EqualTo(6));
    }

    [TestCase("ENEMY_SINGLE")]
    [TestCase("SINGLE_ENEMY")]
    public void EnemySingleAliasesResolveLikeEnemyMinion(string target)
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Damage, target, 2, selectedTargetId: game.Enemy.InstanceId);

        Assert.That(game.Enemy.Health, Is.EqualTo(3));
    }

    [Test]
    public void GrantKeywordAlsoActivatesShield()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.GrantKeyword, "FRIENDLY_MINION", param: "圣盾", selectedTargetId: game.Friendly.InstanceId);
        Assert.Multiple(() =>
        {
            Assert.That(game.Friendly.HasKeyword("圣盾"), Is.True);
            Assert.That(game.Friendly.Shield, Is.True);
        });
    }

    [Test]
    public void SummonUsesStableCardIdFromLibrary()
    {
        var game = new EffectTestFixture();
        var before = game.State.Players[0].Field.Count;
        game.Apply(EffectNames.Summon, amount: 2, param: "token");
        Assert.That(game.State.Players[0].Field, Has.Count.EqualTo(before + 2));
    }

    [Test]
    public void SummonLeaderReplacesOldLeaderAndGrantsLife()
    {
        var game = new EffectTestFixture();
        var old = new CardInstance(20, 0, game.LeaderDefinition) { IsLeaderEntity = true };
        game.State.Players[0].Field.Add(old);
        game.State.Players[0].Life = 3;
        game.Apply(EffectNames.SummonLeader, param: "leader");
        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].Graveyard, Does.Contain(old));
            Assert.That(game.State.Players[0].Leader, Is.Not.Null);
            Assert.That(game.State.Players[0].Life, Is.EqualTo(15));
        });
    }

    [Test]
    public void EndTurnRequestsImmediateEnd()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.EndTurn);
        Assert.That(game.State.EndTurnRequested, Is.True);
    }

    [Test]
    public void AddOpponentPunishTurnUpdatesOpponent()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.AddOppPunishTurn, amount: 5);
        Assert.That(game.State.Players[1].PunishDeltaThisTurn, Is.EqualTo(5));
    }

    [Test]
    public void AddSelfPunishTurnAcceptsNegativeDelta()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.AddSelfPunishTurn, amount: -2);
        Assert.That(game.State.Players[0].PunishDeltaThisTurn, Is.EqualTo(-2));
    }

    [Test]
    public void ConvertPunishToDiscardSetsOpponentFlag()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.ConvertPunishToDiscard);
        Assert.That(game.State.Players[1].PunishToSelfDiscardThisTurn, Is.True);
    }

    [Test]
    public void ProtectTurnSetsSelfFlag()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.ProtectTurn);
        Assert.That(game.State.Players[0].ProtectedThisTurn, Is.True);
    }

    [Test]
    public void NegateMarksContextAndEmitsEvent()
    {
        var game = new EffectTestFixture();
        var context = game.Context();
        game.Dispatcher.Apply(new EffectSpec(EffectNames.Negate), context);
        Assert.Multiple(() =>
        {
            Assert.That(context.Negated, Is.True);
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("EFFECT_NEGATED"));
        });
    }

    [Test]
    public void NegateEnemyEffectsTurnSetsOpponentFlag()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.NegateEnemyEffectsTurn);
        Assert.That(game.State.Players[1].EffectsNegatedThisTurn, Is.True);
    }

    [Test]
    public void SkipReshuffleForcesAtLeastOneCredit()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.SkipReshuffle, amount: 0);
        Assert.That(game.State.Players[0].SkipReshuffleCredits, Is.EqualTo(1));
    }

    [Test]
    public void RestoreAttacksClearsAttackState()
    {
        var game = new EffectTestFixture();
        game.Friendly.AttacksUsed = 2;
        game.Friendly.SummonedThisTurn = true;
        game.Apply(EffectNames.RestoreAttacks, "FRIENDLY_MINION", selectedTargetId: game.Friendly.InstanceId);
        Assert.Multiple(() =>
        {
            Assert.That(game.Friendly.AttacksUsed, Is.Zero);
            Assert.That(game.Friendly.SummonedThisTurn, Is.False);
        });
    }

    [Test]
    public void GainLifeIncreasesOwnLife()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.GainLife, amount: 4);
        Assert.That(game.State.Players[0].Life, Is.EqualTo(24));
    }

    [Test]
    public void LoseLifeDecreasesOpponentLife()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.LoseLife, amount: 4);
        Assert.That(game.State.Players[1].Life, Is.EqualTo(16));
    }

    [Test]
    public void DamageCastleOnlyWorksWhenEnabled()
    {
        var game = new EffectTestFixture();
        game.State.CastleEnabled = true;
        game.Apply(EffectNames.DamageCastle, amount: 7);
        Assert.That(game.State.CastleHealth, Is.EqualTo(68));
    }

    [Test]
    public void WinGameSetsWinnerAndReason()
    {
        var game = new EffectTestFixture();
        game.State.Players[0].Field.Add(
            new CardInstance(20, 0, game.LeaderDefinition) { IsLeaderEntity = true });
        game.State.Players[1].Field.Add(
            new CardInstance(21, 1, game.LeaderDefinition) { IsLeaderEntity = true });
        game.Apply(EffectNames.WinGame, param: "contract-test");
        Assert.Multiple(() =>
        {
            Assert.That(game.State.WinnerPlayerIndex, Is.Zero);
            Assert.That(game.State.WinReason, Is.EqualTo("contract-test"));
        });
    }

    [Test]
    public void WardRejectsSelectedSingleTargetWithoutRetargeting()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Damage, "ENEMY_MINION", 3, selectedTargetId: game.WardEnemy.InstanceId);
        Assert.Multiple(() =>
        {
            Assert.That(game.WardEnemy.Health, Is.EqualTo(5));
            Assert.That(game.Enemy.Health, Is.EqualTo(5));
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("EFFECT_SKIPPED"));
        });
    }

    [Test]
    public void AreaDamageIgnoresWard()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Damage, "ALL_ENEMY_MINIONS", 2);
        Assert.Multiple(() =>
        {
            Assert.That(game.Enemy.Health, Is.EqualTo(3));
            Assert.That(game.WardEnemy.Health, Is.EqualTo(3));
        });
    }

    [Test]
    public void NegateBeforeDamageStopsTheWholeResolutionWindow()
    {
        var game = new EffectTestFixture();
        var context = game.Context(game.Enemy.InstanceId);
        game.Dispatcher.ApplyAll(
            new[]
            {
                new EffectSpec(EffectNames.Negate),
                new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 3),
            },
            context);
        Assert.Multiple(() =>
        {
            Assert.That(game.Enemy.Health, Is.EqualTo(5));
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("EFFECT_NEGATED"));
        });
    }
}
}
