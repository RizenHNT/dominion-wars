using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class LegalActionGeneratorTests
{
    [Test]
    public void NullStateIsRejected()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            new LegalActionGenerator().Generate(null!, 0));
    }

    [Test]
    public void InvalidPlayerIndexIsRejected()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            new LegalActionGenerator().Generate(new GameState(), 2));
    }

    [Test]
    public void FinishedGameHasNoActions()
    {
        var state = new GameState { WinnerPlayerIndex = 0 };
        Assert.That(new LegalActionGenerator().Generate(state, 0), Is.Empty);
    }

    [Test]
    public void OpponentCannotGenerateCurrentTurnActions()
    {
        var state = new GameState();
        Assert.That(new LegalActionGenerator().Generate(state, 1), Is.Empty);
    }

    [Test]
    public void HandCardProducesPlayAction()
    {
        var state = new GameState();
        var card = new CardInstance(1, 0, new CardDefinition("unit", "Unit", isMinion: true));
        state.GetPlayer(0).Hand.Add(card);

        var action = new LegalActionGenerator().Generate(state, 0)
            .Single(item => item.Type == LegalActionGenerator.PlayCard);

        Assert.That(action.SourceId, Is.EqualTo(1));
        Assert.That(action.CardId, Is.EqualTo("unit"));
    }

    [Test]
    public void TargetedPlayAdvertisesCompleteVariantsWhileAreaPlayRemainsTargetless()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        state.GetPlayer(0).Hand.Add(new CardInstance(22, 0, new CardDefinition(
            "flame_bolt",
            "Flame Bolt",
            punish: 1,
            onPlayEffects: new[]
            {
                new EffectSpec(EffectNames.Damage, "ENEMY_FACE", 3),
            })));
        state.GetPlayer(0).Hand.Add(new CardInstance(23, 0, new CardDefinition(
            "flame_rain",
            "Flame Rain",
            onPlayEffects: new[]
            {
                new EffectSpec(EffectNames.Damage, "ALL_ENEMY_MINIONS", 3),
            })));
        state.GetPlayer(1).Deck.Add(new CardInstance(
            30, 1, new CardDefinition("punish_draw", "Punish Draw")));

        var actions = new LegalActionGenerator().Generate(state, 0);
        var bolt = actions.Single(item => item.CardId == "flame_bolt");
        var rain = actions.Single(item => item.CardId == "flame_rain");

        Assert.Multiple(() =>
        {
            Assert.That(bolt.ActionId, Is.EqualTo("play_22_core:player_1:life"));
            Assert.That(bolt.TargetReferenceId, Is.EqualTo("core:player_1:life"));
            Assert.That(bolt.TargetId, Is.Null);
            Assert.That(rain.ActionId, Is.EqualTo("play_23"));
            Assert.That(rain.TargetReferenceId, Is.Null);
            Assert.That(rain.TargetId, Is.Null);
        });
    }

    [Test]
    public void EmptyCloudDoesNotAdvertisePull()
    {
        var state = new GameState();
        state.GetPlayer(0).Field.Add(new CardInstance(
            2,
            0,
            new CardDefinition(
                "mechanical_carrier",
                "Mechanical Carrier",
                attack: 1,
                health: 3,
                isMinion: true,
                faction: "机械遗迹",
                tags: new[] { "机械" })));

        Assert.That(new LegalActionGenerator().Generate(state, 0)
            .Any(item => item.Type == LegalActionGenerator.Pull), Is.False);
    }

    [Test]
    public void PlayActionUsesStableActionId()
    {
        var state = new GameState();
        state.GetPlayer(0).Hand.Add(new CardInstance(
            14, 0, new CardDefinition("unit", "Unit")));

        var action = new LegalActionGenerator().Generate(state, 0)
            .Single(item => item.Type == LegalActionGenerator.PlayCard);
        Assert.That(action.ActionId, Is.EqualTo("play_14"));
    }

    [Test]
    public void CurrentPlayerCanEndTurn()
    {
        var state = new GameState();
        Assert.That(new LegalActionGenerator().Generate(state, 0)
            .Any(item => item.Type == LegalActionGenerator.EndTurn), Is.True);
    }

    [Test]
    public void EligibleMinionCanAttackAliveEnemy()
    {
        var state = new GameState();
        state.GetPlayer(0).Field.Add(new CardInstance(
            2, 0, new CardDefinition("attacker", "Attacker", 2, 2, isMinion: true)));
        state.GetPlayer(1).Field.Add(new CardInstance(
            3, 1, new CardDefinition("defender", "Defender", 1, 2, isMinion: true)));

        var action = new LegalActionGenerator().Generate(state, 0)
            .Single(item => item.Type == LegalActionGenerator.Attack);
        Assert.That(action.TargetId, Is.EqualTo(3));
    }

    [Test]
    public void SummonedThisTurnMinionCannotAttack()
    {
        var state = new GameState();
        state.GetPlayer(0).Field.Add(new CardInstance(
            2, 0, new CardDefinition("attacker", "Attacker", 2, 2, isMinion: true))
        { SummonedThisTurn = true });
        state.GetPlayer(1).Field.Add(new CardInstance(
            3, 1, new CardDefinition("defender", "Defender", 1, 2, isMinion: true)));

        Assert.That(new LegalActionGenerator().Generate(state, 0)
            .Any(item => item.Type == LegalActionGenerator.Attack), Is.False);
    }

    [Test]
    public void ExhaustedMinionCannotAttack()
    {
        var state = new GameState();
        state.GetPlayer(0).Field.Add(new CardInstance(
            2, 0, new CardDefinition("attacker", "Attacker", 2, 2, isMinion: true))
        { AttacksUsed = 1 });
        state.GetPlayer(1).Field.Add(new CardInstance(
            3, 1, new CardDefinition("defender", "Defender", 1, 2, isMinion: true)));

        Assert.That(new LegalActionGenerator().Generate(state, 0)
            .Any(item => item.Type == LegalActionGenerator.Attack), Is.False);
    }

    [Test]
    public void PunishCardUsesCanonicalPlayActionInsteadOfUnsupportedTransportType()
    {
        var state = new GameState();
        state.GetPlayer(0).Hand.Add(new CardInstance(4, 0, new CardDefinition(
            "punish", "Punish", punishActivatable: true, punishCost: 2)));

        state.GetPlayer(0).Hand[0].PunishActivated = true;
        var actions = new LegalActionGenerator().Generate(state, 0);
        Assert.Multiple(() =>
        {
            Assert.That(actions.Any(item => item.Type == "ACTIVATE_PUNISH"), Is.False);
            Assert.That(actions.Single(item => item.Type == LegalActionGenerator.PlayCard).Payload["punish"], Is.EqualTo(2));
        });
    }

    [Test]
    public void LeaderAbilityIsNotAdvertisedInMvp()
    {
        var state = new GameState();
        state.GetPlayer(0).Field.Add(new CardInstance(5, 0, new CardDefinition(
            "leader", "Leader", 1, 3, isMinion: true, isLeader: true, hasLeaderAbility: true))
        { IsLeaderEntity = true });

        Assert.That(new LegalActionGenerator().Generate(state, 0)
            .Any(item => item.Type == "USE_LEADER_ABILITY"), Is.False);
    }

    [Test]
    public void DeadLeaderHasNoAbilityAction()
    {
        var state = new GameState();
        state.GetPlayer(0).Field.Add(new CardInstance(5, 0, new CardDefinition(
            "leader", "Leader", isMinion: true, isLeader: true, hasLeaderAbility: true))
        { IsLeaderEntity = true, Health = 0 });

        Assert.That(new LegalActionGenerator().Generate(state, 0)
            .Any(item => item.Type == "USE_LEADER_ABILITY"), Is.False);
    }

    [Test]
    public void SealedLeaderHasNoAbilityAction()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var leader = new CardInstance(14, 0, new CardDefinition(
            "sealed_leader", "Sealed Leader", 1, 3,
            isMinion: true, isLeader: true, hasLeaderAbility: true))
        {
            IsLeaderEntity = true,
            Sealed = true,
        };
        state.Players[0].Field.Add(leader);

        Assert.That(new LegalActionGenerator().Generate(state, 0)
            .Any(item => item.Type == "USE_LEADER_ABILITY"), Is.False);
    }

    [Test]
    public void ActionTypesRemainPlayerChoicesNotEffectNames()
    {
        var state = new GameState();
        var actions = new LegalActionGenerator().Generate(state, 0);
        Assert.That(actions.Select(item => item.Type), Does.Not.Contain("DAMAGE"));
        Assert.That(actions.Select(item => item.Type), Does.Contain(LegalActionGenerator.EndTurn));
    }
}
}
