using System;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class EffectSafetyTests
{
    [Test]
    public void TurnNegationBlocksNonLeaderSourceEffect()
    {
        var game = new EffectTestFixture();
        game.State.Players[0].EffectsNegatedThisTurn = true;
        game.Apply(EffectNames.Damage, "ENEMY_MINION", 3, selectedTargetId: game.Enemy.InstanceId);
        Assert.Multiple(() =>
        {
            Assert.That(game.Enemy.Health, Is.EqualTo(5));
            Assert.That(game.State.Events.Items[^1].Data["reasonKey"], Is.EqualTo("effect.source_negated"));
        });
    }

    [Test]
    public void TurnNegationDoesNotBlockLeaderSourceEffect()
    {
        var game = new EffectTestFixture();
        var leader = new CardInstance(20, 0, game.LeaderDefinition) { IsLeaderEntity = true };
        game.State.Players[0].Field.Add(leader);
        game.State.Players[0].EffectsNegatedThisTurn = true;
        var root = game.State.Events.Append("CARD_PLAYED");
        var context = new EffectContext(
            0,
            root.EventId,
            leader,
            playedCard: leader,
            selectedTargetId: game.Enemy.InstanceId);
        game.Dispatcher.Apply(new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 3), context);
        Assert.That(game.Enemy.Health, Is.EqualTo(2));
    }

    [Test]
    public void ProtectionMakesNegateFailAndResolutionContinues()
    {
        var game = new EffectTestFixture();
        game.State.Players[0].ProtectedThisTurn = true;
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
            Assert.That(context.Negated, Is.False);
            Assert.That(game.Enemy.Health, Is.EqualTo(2));
        });
    }

    [Test]
    public void OpponentCounterAndOriginalEffectShareNegationWindow()
    {
        var game = new EffectTestFixture();
        var counterDefinition = new CardDefinition("counter", "Counter");
        var counter = new CardInstance(30, 1, counterDefinition);
        var original = game.Context(game.Enemy.InstanceId);
        var counterContext = original.ForSource(1, counter);
        game.Dispatcher.Apply(new EffectSpec(EffectNames.Negate), counterContext);
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 3),
            original.ForSource(0, game.Source));
        Assert.Multiple(() =>
        {
            Assert.That(original.Negated, Is.True);
            Assert.That(game.Enemy.Health, Is.EqualTo(5));
        });
    }

    [Test]
    public void EnemyTargetRequiresExplicitChoice()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Damage, "ENEMY_TARGET", 3);
        Assert.Multiple(() =>
        {
            Assert.That(game.Enemy.Health, Is.EqualTo(5));
            Assert.That(game.State.Players[1].Life, Is.EqualTo(20));
            Assert.That(game.State.Events.Items[^1].Data["reasonKey"], Is.EqualTo("target.selection_required"));
        });
    }

    [Test]
    public void EnemyTargetCanSelectLifeCore()
    {
        var game = new EffectTestFixture();
        game.Apply(
            EffectNames.Damage,
            "ENEMY_TARGET",
            3,
            selectedCoreTarget: CoreTarget.Life);
        Assert.That(game.State.Players[1].Life, Is.EqualTo(17));
    }

    [Test]
    public void EnemyFaceRequiresChoiceWhenCastleAndLifeAreBothLegal()
    {
        var game = new EffectTestFixture();
        game.State.CastleEnabled = true;
        game.Apply(EffectNames.Damage, "ENEMY_FACE", 3);
        Assert.Multiple(() =>
        {
            Assert.That(game.State.CastleHealth, Is.EqualTo(75));
            Assert.That(game.State.Players[1].Life, Is.EqualTo(20));
            Assert.That(game.State.Events.Items[^1].Data["reasonKey"], Is.EqualTo("target.selection_required"));
        });
    }

    [Test]
    public void EnemyFaceCanSelectCastle()
    {
        var game = new EffectTestFixture();
        game.State.CastleEnabled = true;
        game.Apply(
            EffectNames.Damage,
            "ENEMY_FACE",
            3,
            selectedCoreTarget: CoreTarget.RoyalCastle);
        Assert.That(game.State.CastleHealth, Is.EqualTo(72));
    }

    [Test]
    public void EnemyFaceFailsClosedWithoutLifeFallbackForMultipleActiveLeaders()
    {
        var game = new EffectTestFixture();
        var first = new CardInstance(20, 1, game.LeaderDefinition)
        {
            IsLeaderEntity = true,
        };
        var second = new CardInstance(21, 1, game.LeaderDefinition)
        {
            IsLeaderEntity = true,
        };
        game.State.GetPlayer(1).LeaderZone.Add(first);
        game.State.GetPlayer(1).AmbushZone.Add(second);

        game.Apply(EffectNames.Damage, "ENEMY_FACE", 3);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.GetPlayer(1).HasMultipleActiveLeaders, Is.True);
            Assert.That(game.State.GetPlayer(1).Life, Is.EqualTo(20));
            Assert.That(game.State.CastleHealth, Is.EqualTo(75));
            Assert.That(game.State.Events.Items[^1].Data["reasonKey"], Is.EqualTo("target.none"));
        });
    }

    [Test]
    public void LoseLifeUsesLeaderGateBeforeEndingGame()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.LoseLife, amount: 20);
        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[1].Life, Is.EqualTo(1));
            Assert.That(game.State.WinnerPlayerIndex, Is.Null);
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("DEFEAT_PREVENTED"));
        });
    }

    [Test]
    public void LoseLifeEndsGameAfterBothLeadersAreFielded()
    {
        var game = new EffectTestFixture();
        game.State.Players[0].Field.Add(
            new CardInstance(20, 0, game.LeaderDefinition) { IsLeaderEntity = true });
        game.State.Players[1].Field.Add(
            new CardInstance(21, 1, game.LeaderDefinition) { IsLeaderEntity = true });
        game.Apply(EffectNames.LoseLife, amount: 20);
        Assert.That(game.State.WinnerPlayerIndex, Is.Zero);
    }

    [Test]
    public void LeaderLethalIsNotMovedToGraveyard()
    {
        var game = new EffectTestFixture();
        var kingSlayerDefinition = new CardDefinition(
            "king_slayer", "King Slayer", 1, 2, isMinion: true);
        var kingSlayer = new CardInstance(20, 0, kingSlayerDefinition);
        var friendlyLeader = new CardInstance(21, 0, game.LeaderDefinition) { IsLeaderEntity = true };
        var enemyLeader = new CardInstance(22, 1, game.LeaderDefinition) { IsLeaderEntity = true };
        game.State.Players[0].Field.Add(friendlyLeader);
        game.State.Players[0].Field.Add(kingSlayer);
        game.State.Players[1].Field.Add(enemyLeader);
        var root = game.State.Events.Append("CARD_PLAYED");
        var context = new EffectContext(
            0,
            root.EventId,
            kingSlayer,
            playedCard: kingSlayer,
            selectedCoreTarget: CoreTarget.Leader);
        game.Dispatcher.Apply(new EffectSpec(EffectNames.Damage, "ENEMY_FACE", 99, kingSlayer: true), context);
        Assert.Multiple(() =>
        {
            Assert.That(game.State.WinnerPlayerIndex, Is.Zero);
            Assert.That(game.State.Players[1].Field, Does.Contain(enemyLeader));
            Assert.That(game.State.Players[1].Graveyard, Does.Not.Contain(enemyLeader));
        });
    }

    [Test]
    public void NonMinionLeaderRejectsDestroyAndControlWithoutMutation()
    {
        var game = new EffectTestFixture();
        var leaderDefinition = new CardDefinition(
            "spell_leader_guard",
            "Spell Leader Guard",
            isLeader: true,
            type: "SPELL");
        var leader = new CardInstance(23, 1, leaderDefinition)
        {
            IsLeaderEntity = true,
        };
        game.State.GetPlayer(1).LeaderZone.Add(leader);

        game.Apply(EffectNames.Destroy, "ENEMY_TARGET", selectedTargetId: leader.InstanceId);
        game.Apply(EffectNames.Control, "ENEMY_TARGET", amount: 1, selectedTargetId: leader.InstanceId);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.GetPlayer(1).LeaderZone, Has.Exactly(1).EqualTo(leader));
            Assert.That(game.State.GetPlayer(1).Graveyard, Does.Not.Contain(leader));
            Assert.That(game.State.GetPlayer(0).Field, Does.Not.Contain(leader));
            Assert.That(leader.ControlledByPlayerIndex, Is.Null);
            Assert.That(leader.ControlTurnsRemaining, Is.Zero);
            Assert.That(game.State.WinnerPlayerIndex, Is.Null);
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("EFFECT_SKIPPED"));
            Assert.That(game.State.Events.Items[^1].Data["reasonKey"], Is.EqualTo("target.none"));
        });
    }

    [Test]
    public void LethalDamageToNonMinionLeaderDoesNotDeclareLeaderDefeat()
    {
        var game = new EffectTestFixture();
        var enemyDefinition = new CardDefinition(
            "durabilityless_spell_leader",
            "Durabilityless Spell Leader",
            health: 4,
            isLeader: true,
            type: "SPELL",
            vulnerabilities: new[] { EffectNames.Damage });
        var enemyLeader = new CardInstance(24, 1, enemyDefinition)
        {
            IsLeaderEntity = true,
        };
        var friendlyLeader = new CardInstance(25, 0, game.LeaderDefinition)
        {
            IsLeaderEntity = true,
        };
        game.State.GetPlayer(1).LeaderZone.Add(enemyLeader);
        game.State.GetPlayer(0).Field.Add(friendlyLeader);

        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Damage, "ENEMY_FACE", 99, kingSlayer: true),
            game.Context(selectedCoreTarget: CoreTarget.Leader));

        Assert.Multiple(() =>
        {
            Assert.That(enemyLeader.Health, Is.LessThanOrEqualTo(0));
            Assert.That(game.State.GetPlayer(1).LeaderZone, Has.Exactly(1).EqualTo(enemyLeader));
            Assert.That(game.State.GetPlayer(1).Graveyard, Does.Not.Contain(enemyLeader));
            Assert.That(game.State.WinnerPlayerIndex, Is.Null);
            Assert.That(game.State.WinReason, Is.Null);
        });
    }

    [Test]
    public void LegacyCardLevelKingSlayerRemainsACompatibilityFallback()
    {
        var game = new EffectTestFixture();
        var legacyDefinition = new CardDefinition(
            "legacy_king_slayer", "Legacy King Slayer", 1, 2, isMinion: true, kingSlayer: true);
        var legacy = new CardInstance(30, 0, legacyDefinition);
        var friendlyLeader = new CardInstance(32, 0, game.LeaderDefinition) { IsLeaderEntity = true };
        var enemyLeader = new CardInstance(31, 1, game.LeaderDefinition) { IsLeaderEntity = true };
        game.State.Players[0].Field.Add(legacy);
        game.State.Players[0].Field.Add(friendlyLeader);
        game.State.Players[1].Field.Add(enemyLeader);
        var root = game.State.Events.Append("CARD_PLAYED");
        var context = new EffectContext(
            0,
            root.EventId,
            legacy,
            playedCard: legacy,
            selectedCoreTarget: CoreTarget.Leader);
        game.Dispatcher.Apply(new EffectSpec(EffectNames.Damage, "ENEMY_FACE", 99), context);
        Assert.That(game.State.WinnerPlayerIndex, Is.Zero);
    }

    [Test]
    public void KingSlayerStillRequiresLeaderVulnerabilityWhitelist()
    {
        var game = new EffectTestFixture();
        var immuneDefinition = new CardDefinition(
            "immune_leader", "Immune Leader", 3, 8, isMinion: true, isLeader: true);
        var friendlyLeader = new CardInstance(40, 0, game.LeaderDefinition) { IsLeaderEntity = true };
        var enemyLeader = new CardInstance(41, 1, immuneDefinition) { IsLeaderEntity = true };
        game.State.Players[0].Field.Add(friendlyLeader);
        game.State.Players[1].Field.Add(enemyLeader);
        var root = game.State.Events.Append("CARD_PLAYED");
        var context = new EffectContext(
            0,
            root.EventId,
            game.Source,
            playedCard: game.Source,
            selectedCoreTarget: CoreTarget.Leader);
        game.Dispatcher.Apply(new EffectSpec(EffectNames.Damage, "ENEMY_FACE", 99, kingSlayer: true), context);
        Assert.Multiple(() =>
        {
            Assert.That(enemyLeader.Health, Is.EqualTo(8));
            Assert.That(game.State.WinnerPlayerIndex, Is.Null);
        });
    }

    [Test]
    public void DrawReshufflesGraveyardDeterministically()
    {
        var game = new EffectTestFixture();
        game.State.Players[0].Graveyard.Add(new CardInstance(20, 0, game.SoldierDefinition));
        game.Apply(EffectNames.Draw, amount: 1);
        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(1));
            Assert.That(game.State.Players[0].ReshuffleCount, Is.EqualTo(1));
            Assert.That(game.State.Players[0].CycleWinCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ReshuffleRestoresCardRuntimeState()
    {
        var game = new EffectTestFixture();
        var card = new CardInstance(20, 0, game.SoldierDefinition)
        {
            Attack = 99,
            Health = 0,
            MaxHealth = 99,
            SummonedThisTurn = true,
            AttacksUsed = 2,
        };
        card.Keywords.Add("圣盾");
        card.Shield = true;
        game.State.Players[0].Graveyard.Add(card);
        game.Apply(EffectNames.Draw, amount: 1);
        Assert.Multiple(() =>
        {
            Assert.That(card.Attack, Is.EqualTo(game.SoldierDefinition.Attack));
            Assert.That(card.Health, Is.EqualTo(game.SoldierDefinition.Health));
            Assert.That(card.MaxHealth, Is.EqualTo(game.SoldierDefinition.Health));
            Assert.That(card.SummonedThisTurn, Is.False);
            Assert.That(card.AttacksUsed, Is.Zero);
            Assert.That(card.Shield, Is.False);
        });
    }

    [Test]
    public void EffectDiscardContributesToDiscardCounter()
    {
        var game = new EffectTestFixture();
        game.State.Players[1].Hand.Add(new CardInstance(20, 1, game.SoldierDefinition));
        game.Apply(EffectNames.DiscardOppRandom, amount: 1);
        Assert.That(game.State.Players[1].TotalDiscarded, Is.EqualTo(1));
    }

    [Test]
    public void InvalidRootEventCannotMutateState()
    {
        var game = new EffectTestFixture();
        var invalid = new EffectContext(
            0,
            999,
            game.Source,
            playedCard: game.Source,
            selectedTargetId: game.Enemy.InstanceId);
        Assert.Throws<InvalidOperationException>(() =>
            game.Dispatcher.Apply(new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 3), invalid));
        Assert.That(game.Enemy.Health, Is.EqualTo(5));
    }
}
}
