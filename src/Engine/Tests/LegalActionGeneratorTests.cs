using System.Linq;
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
    public void PunishCardProducesActivationAction()
    {
        var state = new GameState();
        state.GetPlayer(0).Hand.Add(new CardInstance(4, 0, new CardDefinition(
            "punish", "Punish", punishActivatable: true, punishCost: 2)));

        var action = new LegalActionGenerator().Generate(state, 0)
            .Single(item => item.Type == LegalActionGenerator.ActivatePunish);
        Assert.That(action.Payload["cost"], Is.EqualTo(2));
    }

    [Test]
    public void LeaderAbilityRequiresManifestedLeaderFlag()
    {
        var state = new GameState();
        state.GetPlayer(0).Field.Add(new CardInstance(5, 0, new CardDefinition(
            "leader", "Leader", 1, 3, isMinion: true, isLeader: true, hasLeaderAbility: true))
        { IsLeaderEntity = true });

        Assert.That(new LegalActionGenerator().Generate(state, 0)
            .Any(item => item.Type == LegalActionGenerator.UseLeaderAbility), Is.True);
    }

    [Test]
    public void DeadLeaderHasNoAbilityAction()
    {
        var state = new GameState();
        state.GetPlayer(0).Field.Add(new CardInstance(5, 0, new CardDefinition(
            "leader", "Leader", isMinion: true, isLeader: true, hasLeaderAbility: true))
        { IsLeaderEntity = true, Health = 0 });

        Assert.That(new LegalActionGenerator().Generate(state, 0)
            .Any(item => item.Type == LegalActionGenerator.UseLeaderAbility), Is.False);
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
