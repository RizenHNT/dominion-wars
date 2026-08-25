using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
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
    public void DrawingLeaderManifestsWithoutHandAndRunsOnlyEnterEffects()
    {
        var game = new EffectTestFixture();
        var leaderDefinition = new CardDefinition(
            "drawn_leader",
            "Drawn Leader",
            isLeader: true,
            grantLife: 10,
            leaderEnterEffects: new[]
            {
                new EffectSpec(EffectNames.GainLife, "SELF_PLAYER", 2),
            },
            leaderPunishEffects: new[]
            {
                new EffectSpec(EffectNames.GainLife, "SELF_PLAYER", 7),
            });
        var leader = new CardInstance(11, 0, leaderDefinition);
        game.State.Players[0].Deck.Add(leader);

        game.Apply(EffectNames.Draw, amount: 1);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].Deck, Does.Not.Contain(leader));
            Assert.That(game.State.Players[0].Hand, Does.Not.Contain(leader));
            Assert.That(game.State.Players[0].LeaderZone, Has.Exactly(1).EqualTo(leader));
            Assert.That(game.State.Players[0].Field, Does.Not.Contain(leader));
            Assert.That(game.State.Players[0].Leader, Is.SameAs(leader));
            Assert.That(game.State.Players[0].Life, Is.EqualTo(12));
            Assert.That(game.State.Events.Items.Count(item => item.EventType == "LEADER_MANIFESTED"),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void DrawingLeaderFormsManifestIntoTheirDedicatedZones()
    {
        var cases = new[]
        {
            (id: "drawn_minion_leader", type: "MINION", isMinion: true),
            (id: "drawn_ambush_leader", type: "AMBUSH", isMinion: false),
            (id: "drawn_spell_leader", type: "SPELL", isMinion: false),
        };

        foreach (var testCase in cases)
        {
            var game = new EffectTestFixture();
            var definition = new CardDefinition(
                testCase.id,
                testCase.id,
                isMinion: testCase.isMinion,
                isLeader: true,
                type: testCase.type);
            var leader = new CardInstance(12, 0, definition);
            game.State.Players[0].Deck.Add(leader);

            game.Apply(EffectNames.Draw, amount: 1);

            Assert.Multiple(() =>
            {
                Assert.That(game.State.Players[0].Leader, Is.SameAs(leader));
                Assert.That(game.State.Players[0].Field.Contains(leader), Is.EqualTo(testCase.isMinion));
                Assert.That(game.State.Players[0].AmbushZone.Contains(leader),
                    Is.EqualTo(!testCase.isMinion && testCase.type == "AMBUSH"));
                Assert.That(game.State.Players[0].LeaderZone.Contains(leader),
                    Is.EqualTo(!testCase.isMinion && testCase.type != "AMBUSH"));
                Assert.That(game.State.Players[0].ActiveLeaderCount, Is.EqualTo(1));
            });
        }
    }

    [Test]
    public void DrawingAmbushLeaderPreservesItsSpecialZoneAndTargetRule()
    {
        var game = new EffectTestFixture();
        var ambushDefinition = new CardDefinition(
            "drawn_ambush_special",
            "Drawn Ambush Special",
            isLeader: true,
            type: "AMBUSH",
            ambushKind: "REVEAL",
            ambushTrigger: "OPPONENT_ATTACK");
        var ambush = new CardInstance(15, 1, ambushDefinition);
        game.State.GetPlayer(1).Deck.Add(ambush);

        game.Apply(EffectNames.OppDraw, amount: 1);
        var targets = new AttackTargetPolicy().GetLegalTargets(game.State, game.Friendly);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.GetPlayer(1).Deck, Does.Not.Contain(ambush));
            Assert.That(game.State.GetPlayer(1).Hand, Does.Not.Contain(ambush));
            Assert.That(game.State.GetPlayer(1).AmbushZone, Has.Exactly(1).EqualTo(ambush));
            Assert.That(game.State.GetPlayer(1).Leader, Is.SameAs(ambush));
            Assert.That(game.State.GetPlayer(1).Field, Does.Not.Contain(ambush));
            Assert.That(targets.Select(target => target.Id),
                Does.Not.Contain(AttackTarget.ForEntity(ambush).Id));
        });
    }

    [Test]
    public void AmbiguousActiveLeaderDrawFailsClosedWithoutSideEffects()
    {
        var game = new EffectTestFixture();
        var leaderDefinition = new CardDefinition(
            "reentrant_leader",
            "Reentrant Leader",
            isLeader: true,
            grantLife: 10,
            leaderEnterEffects: new[]
            {
                new EffectSpec(EffectNames.GainLife, "SELF_PLAYER", 2),
            });
        var leader = new CardInstance(12, 0, leaderDefinition)
        {
            IsLeaderEntity = true,
        };
        game.State.Players[0].Field.Add(leader);
        game.State.Players[0].Field.Add(leader);
        game.State.Players[0].Deck.Add(leader);
        var lifeBefore = game.State.Players[0].Life;

        game.Apply(EffectNames.Draw, amount: 1);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].Field.Count(card => ReferenceEquals(card, leader)),
                Is.EqualTo(2));
            Assert.That(game.State.Players[0].Hand, Does.Not.Contain(leader));
            Assert.That(game.State.Players[0].Deck, Has.Exactly(1).EqualTo(leader));
            Assert.That(game.State.Players[0].Life, Is.EqualTo(lifeBefore));
            Assert.That(game.State.Events.Items.Count(item => item.EventType == "LEADER_MANIFESTED"),
                Is.Zero);
        });
    }

    [Test]
    public void DifferentActiveLeaderDrawFailsClosedWithoutMovingTheCandidate()
    {
        var game = new EffectTestFixture();
        var activeLeader = new CardInstance(13, 0, game.LeaderDefinition)
        {
            IsLeaderEntity = true,
        };
        var candidateDefinition = new CardDefinition(
            "candidate_leader",
            "Candidate Leader",
            isLeader: true);
        var candidate = new CardInstance(14, 0, candidateDefinition);
        game.State.Players[0].Field.Add(activeLeader);
        game.State.Players[0].Deck.Add(candidate);
        var lifeBefore = game.State.Players[0].Life;

        game.Apply(EffectNames.Draw, amount: 1);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].Leader, Is.SameAs(activeLeader));
            Assert.That(game.State.Players[0].Field, Has.Exactly(1).EqualTo(activeLeader));
            Assert.That(game.State.Players[0].Deck, Has.Exactly(1).EqualTo(candidate));
            Assert.That(game.State.Players[0].LeaderZone, Is.Empty);
            Assert.That(game.State.Players[0].Life, Is.EqualTo(lifeBefore));
            Assert.That(game.State.Events.Items.Count(item => item.EventType == "LEADER_MANIFESTED"),
                Is.Zero);
        });
    }

    [Test]
    public void LeaderZonesParticipateInEntityLookupAndIdAllocation()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var ambushDefinition = new CardDefinition(
            "ambush_leader",
            "Ambush Leader",
            isLeader: true,
            type: "AMBUSH");
        var leaderDefinition = new CardDefinition(
            "spell_leader",
            "Spell Leader",
            isLeader: true);
        var ambush = new CardInstance(90, 0, ambushDefinition) { IsLeaderEntity = true };
        var leader = new CardInstance(91, 0, leaderDefinition) { IsLeaderEntity = true };
        state.GetPlayer(0).AmbushZone.Add(ambush);
        state.GetPlayer(0).LeaderZone.Add(leader);

        Assert.Multiple(() =>
        {
            Assert.That(state.FindEntity(90), Is.SameAs(ambush));
            Assert.That(state.FindEntity(91), Is.SameAs(leader));
            Assert.That(state.AllocateEntityId(), Is.EqualTo(92));
        });
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
    public void BreakingCastleAppliesCountdownForcesLeaderAndChecksCastleVictory()
    {
        var game = new EffectTestFixture();
        game.State.CastleEnabled = true;
        game.State.CastleHealth = 2;
        var breakerLeader = new CardInstance(30, 0, new CardDefinition(
            "breaker_leader",
            "Breaker Leader",
            isLeader: true,
            leaderWinCondition: "ROYAL_CASTLE_BREAK",
            leaderWinText: "Break the castle"))
        {
            IsLeaderEntity = true,
        };
        var victimLeader = new CardInstance(31, 1, new CardDefinition(
            "victim_leader",
            "Victim Leader",
            attack: 2,
            health: 6,
            isMinion: true,
            isLeader: true,
            grantLife: 10,
            leaderEnterEffects: new[] { new EffectSpec(EffectNames.GainLife, "SELF_PLAYER", 2) },
            leaderPunishEffects: new[] { new EffectSpec(EffectNames.GainLife, "SELF_PLAYER", 7) }));
        game.State.GetPlayer(0).LeaderZone.Add(breakerLeader);
        game.State.GetPlayer(1).Deck.Add(victimLeader);

        game.Apply(EffectNames.DamageCastle, amount: 2);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.CastleHealth, Is.Zero);
            Assert.That(game.State.GetPlayer(0).CycleWinCount, Is.EqualTo(9));
            Assert.That(game.State.GetPlayer(1).DamagedThisCycle, Is.True);
            Assert.That(game.State.GetPlayer(0).Leader, Is.SameAs(breakerLeader));
            Assert.That(game.State.GetPlayer(1).Leader, Is.SameAs(victimLeader));
            Assert.That(victimLeader.SummonedThisTurn, Is.True);
            Assert.That(game.State.GetPlayer(1).Life, Is.EqualTo(12));
            Assert.That(game.State.WinnerPlayerIndex, Is.Zero);
            Assert.That(game.State.WinReason, Is.EqualTo("win.royal_castle_break"));
            var eventTypes = game.State.Events.Items.Select(item => item.EventType).ToList();
            Assert.That(
                eventTypes.IndexOf("CASTLE_DAMAGED"),
                Is.LessThan(eventTypes.IndexOf("CASTLE_BROKEN")));
            Assert.That(
                eventTypes.IndexOf("CASTLE_BROKEN"),
                Is.LessThan(eventTypes.IndexOf("LEADER_MANIFESTED")));
            Assert.That(
                eventTypes.IndexOf("LEADER_MANIFESTED"),
                Is.LessThan(eventTypes.IndexOf("GAME_WON")));
        });
    }

    [Test]
    public void BreakingCastleWithActiveMinionLeadersGivesBreakerPriority()
    {
        var game = new EffectTestFixture();
        game.State.CastleEnabled = true;
        game.State.CastleHealth = 1;
        var breakerLeader = new CardInstance(32, 0, new CardDefinition(
            "breaker_minion_leader",
            "Breaker Minion Leader",
            attack: 3,
            health: 8,
            isMinion: true,
            isLeader: true))
        {
            IsLeaderEntity = true,
        };
        var defenderLeader = new CardInstance(33, 1, new CardDefinition(
            "defender_minion_leader",
            "Defender Minion Leader",
            attack: 3,
            health: 8,
            isMinion: true,
            isLeader: true))
        {
            IsLeaderEntity = true,
        };
        game.State.GetPlayer(0).Field.Add(breakerLeader);
        game.State.GetPlayer(1).Field.Add(defenderLeader);

        game.Apply(EffectNames.DamageCastle, amount: 1);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.WinnerPlayerIndex, Is.EqualTo(0));
            Assert.That(game.State.WinReason, Is.EqualTo("win.castle_break_minion"));
            Assert.That(game.State.GetPlayer(0).Leader, Is.SameAs(breakerLeader));
            Assert.That(game.State.GetPlayer(1).Leader, Is.SameAs(defenderLeader));
        });
    }

    [Test]
    public void BreakingCastleRoyalConditionCanAwardActiveDefender()
    {
        var game = new EffectTestFixture();
        game.State.CastleEnabled = true;
        game.State.CastleHealth = 1;
        var breakerLeader = new CardInstance(34, 0, new CardDefinition(
            "ordinary_breaker_leader",
            "Ordinary Breaker Leader",
            isLeader: true))
        {
            IsLeaderEntity = true,
        };
        var defenderLeader = new CardInstance(35, 1, new CardDefinition(
            "royal_defender_leader",
            "Royal Defender Leader",
            isLeader: true,
            leaderWinCondition: "ROYAL_CASTLE_BREAK"))
        {
            IsLeaderEntity = true,
        };
        game.State.GetPlayer(0).LeaderZone.Add(breakerLeader);
        game.State.GetPlayer(1).LeaderZone.Add(defenderLeader);

        game.Apply(EffectNames.DamageCastle, amount: 1);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.WinnerPlayerIndex, Is.EqualTo(1));
            Assert.That(game.State.WinReason, Is.EqualTo("win.royal_castle_break"));
        });
    }

    [Test]
    public void BreakingCastleIgnoresHiddenRoyalLeader()
    {
        var game = new EffectTestFixture();
        game.State.CastleEnabled = true;
        game.State.CastleHealth = 1;
        var activeBreaker = new CardInstance(36, 0, new CardDefinition(
            "ordinary_active_breaker",
            "Ordinary Active Breaker",
            isLeader: true))
        {
            IsLeaderEntity = true,
        };
        var hiddenRoyalLeader = new CardInstance(37, 0, new CardDefinition(
            "hidden_royal_leader",
            "Hidden Royal Leader",
            isLeader: true,
            leaderWinCondition: "ROYAL_CASTLE_BREAK"));
        var defenderLeader = new CardInstance(38, 1, new CardDefinition(
            "ordinary_active_defender",
            "Ordinary Active Defender",
            isLeader: true))
        {
            IsLeaderEntity = true,
        };
        game.State.GetPlayer(0).LeaderZone.Add(activeBreaker);
        game.State.GetPlayer(0).Deck.Add(hiddenRoyalLeader);
        game.State.GetPlayer(1).LeaderZone.Add(defenderLeader);

        game.Apply(EffectNames.DamageCastle, amount: 1);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.WinnerPlayerIndex, Is.Null);
            Assert.That(game.State.WinReason, Is.Null);
            Assert.That(game.State.GetPlayer(0).Deck, Does.Contain(hiddenRoyalLeader));
            Assert.That(game.State.GetPlayer(0).Leader, Is.SameAs(activeBreaker));
        });
    }

    [Test]
    public void BreakingCastleDoesNotLowerCountOrRepeatBreakResolution()
    {
        var game = new EffectTestFixture();
        game.State.CastleEnabled = true;
        game.State.CastleHealth = 1;
        game.State.GetPlayer(0).CycleWinCount = 12;

        game.Apply(EffectNames.DamageCastle, amount: 1);
        game.Apply(EffectNames.DamageCastle, amount: 1);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.GetPlayer(0).CycleWinCount, Is.EqualTo(12));
            Assert.That(game.State.Events.Items.Count(item => item.EventType == "CASTLE_BROKEN"),
                Is.EqualTo(1));
            Assert.That(game.State.Events.Items.Count(item => item.EventType == "CASTLE_DAMAGED"),
                Is.EqualTo(1));
        });
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
