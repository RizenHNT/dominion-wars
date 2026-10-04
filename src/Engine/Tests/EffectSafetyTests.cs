using System;
using System.Linq;
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
    public void LoseLifeDoesNotEndTheGameEvenWithBothLeadersFielded()
    {
        // THE PLAYER LIFE POOL IS NOT A DEFEAT CONDITION (docs/RULES.md §1, §7).
        //
        // This test used to be `LoseLifeEndsGameAfterBothLeadersAreFielded` and asserted
        // `WinnerPlayerIndex == 0` after draining the opponent's life to 0 with both leaders
        // fielded. It passed because `EffectRuntime.CheckAll` had a `lifeDefeated` branch that
        // declared `win.enemy_life_zero`. That branch has been DELETED: the rule book lists
        // exactly two victory paths (a leader's declared winCondition, and the deck-cycle
        // counter) and states the life pool constitutes neither a win nor a loss.
        //
        // The fixture gives both players an explicit life pool of 20, so LOSE_LIFE does land
        // and the value really does drop. The assertion is now the rule's actual content.
        var game = new EffectTestFixture();
        game.State.Players[0].Field.Add(
            new CardInstance(20, 0, game.LeaderDefinition) { IsLeaderEntity = true });
        game.State.Players[1].Field.Add(
            new CardInstance(21, 1, game.LeaderDefinition) { IsLeaderEntity = true });

        game.Apply(EffectNames.LoseLife, amount: 20);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.GetPlayer(1).Life, Is.Zero, "the life pool must actually reach 0");
            Assert.That(
                game.State.WinnerPlayerIndex,
                Is.Null,
                "life reaching 0 must NOT declare a winner, even with both leaders fielded");
            Assert.That(
                game.State.WinReason,
                Is.Null,
                "and no win reason may be recorded, least of all win.enemy_life_zero");
            Assert.That(
                game.State.Events.Items.Any(item =>
                    string.Equals(item.EventType, "DEFEAT_PREVENTED", StringComparison.Ordinal)),
                Is.False,
                "nor may the leader gate report a prevented defeat: there was no defeat to prevent");
        });
    }

    [Test]
    public void LoseLifeIsSkippedWhenThePlayerHasNoLifePool()
    {
        // THE DEFAULT STATE: a match starts with no life pool at all
        // (`MatchSetupOptions.PlayerLife` is null), because only a leader declaring `grantLife`
        // opens one. A life effect against a player with no pool must be SKIPPED rather than
        // silently clamped, so the situation is visible in the event log.
        var game = new EffectTestFixture();
        game.State.GetPlayer(1).Life = null;

        game.Apply(EffectNames.LoseLife, amount: 5);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.GetPlayer(1).Life, Is.Null, "no pool means no pool to drain");
            Assert.That(game.State.WinnerPlayerIndex, Is.Null);
            Assert.That(
                game.State.Events.Items[^1].EventType,
                Is.EqualTo("EFFECT_SKIPPED"),
                "a life effect with no life pool must be reported as skipped");
        });
    }

    [Test]
    public void LoseLifeUsesLeaderGateBeforeEndingGame()
    {
        // The leader gate still applies to the defeat conditions that REMAIN. A player whose
        // life pool is drained to 0 must not be spared by the gate, because a drained life pool
        // is no longer a defeat at all — see the test above. What this test now pins is the
        // counterpart: the gate is not triggered by life, so nothing about life can be gated.
        var game = new EffectTestFixture();
        game.Apply(EffectNames.LoseLife, amount: 20);
        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[1].Life, Is.Zero);
            Assert.That(game.State.WinnerPlayerIndex, Is.Null);
            Assert.That(
                game.State.Events.Items[^1].EventType,
                Is.Not.EqualTo("DEFEAT_PREVENTED"),
                "life must not route through the leader gate; it is not a defeat condition");
        });
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
