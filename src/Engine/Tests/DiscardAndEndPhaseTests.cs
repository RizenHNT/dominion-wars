using System.Collections.Generic;
using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Rules;
using DominionWars.Engine.Turns;
using DominionWars.Adapters;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class DiscardAndEndPhaseTests
{
    [Test]
    public void DiscardRequiresExactUniqueHandSelectionAndThenHandsOff()
    {
        var state = new GameState(
            new PlayerState(0, 20),
            new PlayerState(1, 20),
            rules: new MatchRules(handLimit: 8));
        AddHand(state.GetPlayer(0), 10);
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        flow.JumpTo(state, 0, TurnPhase.Discard);
        var action = flow.GetLegalActions(state, 0).Single();
        var before = state.Events.Count;

        var tooFew = router.Execute(state, new GameActionRequest(
            0, TurnAction.DiscardComplete, selectedEntityIds: new[] { 1L }));
        var duplicate = router.Execute(state, new GameActionRequest(
            0, TurnAction.DiscardComplete, selectedEntityIds: new[] { 1L, 1L }));

        Assert.Multiple(() =>
        {
            Assert.That(action.Payload["requiredCount"], Is.EqualTo(2));
            Assert.That((long[])action.Payload["candidateIds"]!, Has.Length.EqualTo(10));
            Assert.That(tooFew.ReasonKey, Is.EqualTo("action.discard_count_mismatch"));
            Assert.That(duplicate.ReasonKey, Is.EqualTo("action.discard_duplicate"));
            Assert.That(state.GetPlayer(0).Hand, Has.Count.EqualTo(10));
            Assert.That(state.Events.Count, Is.EqualTo(before));
        });

        var accepted = router.Execute(state, new GameActionRequest(
            0,
            TurnAction.DiscardComplete,
            action.ActionId,
            selectedEntityIds: new[] { 2L, 4L }));

        Assert.Multiple(() =>
        {
            Assert.That(accepted.Accepted, Is.True);
            Assert.That(state.GetPlayer(0).Hand.Select(card => card.InstanceId),
                Is.EqualTo(new[] { 1L, 3L, 5L, 6L, 7L, 8L, 9L, 10L }));
            Assert.That(state.GetPlayer(0).Graveyard.Select(card => card.InstanceId),
                Is.EqualTo(new[] { 2L, 4L }));
            Assert.That(state.GetPlayer(0).TotalDiscarded, Is.Zero,
                "Hand-limit discards do not count toward discard victories.");
            Assert.That(state.CurrentPlayerIndex, Is.EqualTo(1));
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Start));
        });
    }

    [Test]
    public void PioneerHandBonusIsConfigurableAndEndsWhenBothLeadersAppear()
    {
        var state = new GameState(
            new PlayerState(0, 20),
            new PlayerState(1, 20),
            rules: new MatchRules(handLimit: 8, pioneerHandLimitBonus: 2));
        AddHand(state.GetPlayer(0), 10);
        state.GetPlayer(0).Field.Add(Leader(100, 0, "leader0"));

        Assert.That(DiscardPhaseHandler.RequiredDiscardCount(state, state.GetPlayer(0)), Is.Zero);

        state.GetPlayer(1).Field.Add(Leader(101, 1, "leader1"));
        Assert.That(DiscardPhaseHandler.RequiredDiscardCount(state, state.GetPlayer(0)), Is.EqualTo(2));
    }

    [Test]
    public void EndPhaseAdvancesChantExpiresInactivePunishAndClearsFlags()
    {
        var player = new PlayerState(0, 10)
        {
            PunishDeltaThisTurn = 3,
            ProtectedThisTurn = true,
        };
        var state = new GameState(player, new PlayerState(1, 20));
        var chant = new CardInstance(200, 0, new CardDefinition(
            "chant", "Chant", chant: 1,
            chantEffects: new[] { new EffectSpec(EffectNames.GainLife, amount: 2) }));
        chant.ChantRemaining = 1;
        player.Field.Add(chant);
        player.Hand.Add(new CardInstance(201, 0, new CardDefinition(
            "punish", "Punish", type: "PUNISH", punish: 1)));
        player.Hand.Add(new CardInstance(202, 0, new CardDefinition(
            "active", "Active", type: "PUNISH", punish: 1)) { PunishActivated = true });
        var flow = TurnFlow.CreateDefault();
        flow.JumpTo(state, 0, TurnPhase.End);

        flow.Advance(state, 0);

        Assert.Multiple(() =>
        {
            Assert.That(player.Life, Is.EqualTo(12));
            Assert.That(player.Field, Does.Not.Contain(chant));
            Assert.That(player.Graveyard.Select(card => card.InstanceId),
                Is.EquivalentTo(new[] { 200L, 201L }));
            Assert.That(player.Hand.Single().InstanceId, Is.EqualTo(202));
            Assert.That(player.PunishDeltaThisTurn, Is.Zero);
            Assert.That(player.ProtectedThisTurn, Is.False);
            Assert.That(player.EffectsNegatedThisTurn, Is.False);
            Assert.That(state.CurrentPlayerIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void NoDamageConditionWinsBeforeTurnHandoffAndDamageResetsProgress()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var winner = Leader(300, 0, "wood", "NO_DAMAGE_TURNS_GE", 1);
        state.GetPlayer(0).Field.Add(winner);
        state.GetPlayer(1).Field.Add(Leader(301, 1, "other"));
        var flow = TurnFlow.CreateDefault();
        flow.JumpTo(state, 0, TurnPhase.End);

        flow.Advance(state, 0);

        Assert.Multiple(() =>
        {
            Assert.That(state.GetPlayer(0).NoDamageTurns, Is.EqualTo(1));
            Assert.That(state.WinnerPlayerIndex, Is.Zero);
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Over));
            Assert.That(state.CurrentPlayerIndex, Is.Zero);
        });

        var damaged = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        damaged.GetPlayer(0).Field.Add(Leader(310, 0, "wood", "NO_DAMAGE_TURNS_GE", 2));
        damaged.GetPlayer(1).Field.Add(Leader(311, 1, "other"));
        damaged.GetPlayer(0).NoDamageTurns = 1;
        damaged.GetPlayer(0).DamagedThisCycle = true;
        var damagedFlow = TurnFlow.CreateDefault();
        damagedFlow.JumpTo(damaged, 0, TurnPhase.End);
        damagedFlow.Advance(damaged, 0);

        Assert.Multiple(() =>
        {
            Assert.That(damaged.GetPlayer(0).NoDamageTurns, Is.Zero);
            Assert.That(damaged.GetPlayer(0).DamagedThisCycle, Is.False);
            Assert.That(damaged.WinnerPlayerIndex, Is.Null);
            Assert.That(damaged.CurrentPlayerIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void OpponentDiscardAndPunishDrawWinConditionsUseMappedParameters()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        state.GetPlayer(0).Field.Add(Leader(400, 0, "sea", "OPP_DISCARD_TOTAL_GE", 3));
        state.GetPlayer(1).Field.Add(Leader(401, 1, "other"));
        state.GetPlayer(1).TotalDiscarded = 3;
        var flow = TurnFlow.CreateDefault();
        flow.JumpTo(state, 0, TurnPhase.End);
        flow.Advance(state, 0);
        Assert.That(state.WinnerPlayerIndex, Is.Zero);

        var machine = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        machine.GetPlayer(0).Field.Add(Leader(410, 0, "machine", "OPP_PUNISH_DRAW_TURN_GE", 2));
        machine.GetPlayer(1).Field.Add(Leader(411, 1, "other"));
        machine.GetPlayer(1).PunishDrawnThisTurn = 2;
        var machineFlow = TurnFlow.CreateDefault();
        machineFlow.JumpTo(machine, 0, TurnPhase.End);
        machineFlow.Advance(machine, 0);
        Assert.That(machine.WinnerPlayerIndex, Is.Zero);
    }

    [Test]
    public void CastleForcesMissingLeaderBeforeSpecialVictoryAndDoesNotHandoff()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20))
        {
            CastleEnabled = true,
        };
        state.GetPlayer(0).Field.Add(Leader(500, 0, "wood", "NO_DAMAGE_TURNS_GE", 1));
        state.GetPlayer(1).Hand.Add(Leader(501, 1, "hidden"));
        var flow = TurnFlow.CreateDefault();
        flow.JumpTo(state, 0, TurnPhase.End);

        flow.Advance(state, 0);

        Assert.Multiple(() =>
        {
            Assert.That(state.GetPlayer(1).Leader, Is.Not.Null);
            Assert.That(state.GetPlayer(1).Hand, Is.Empty);
            Assert.That(state.WinnerPlayerIndex, Is.Zero);
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Over));
            Assert.That(state.CurrentPlayerIndex, Is.Zero);
        });
    }

    [Test]
    public void EndPhaseEventChainUsesOnlyProjectableRootTypes()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        state.GetPlayer(0).Field.Add(Leader(600, 0, "leader"));
        var flow = TurnFlow.CreateDefault();
        flow.JumpTo(state, 0, TurnPhase.End);
        flow.Advance(state, 0);

        var events = EngineProjectionAdapter.ToEvents(
            state.Events.Items,
            state.Turn.Number,
            state.Turn.PhaseId);

        Assert.Multiple(() =>
        {
            Assert.That(events, Has.Count.EqualTo(state.Events.Count));
            Assert.That(events.Select(item => item.Type),
                Does.Contain("VICTORY_PROGRESS"));
            Assert.That(events.Select(item => item.Type), Does.Contain("TURN_CHANGED"));
            Assert.That(events.Select(item => item.Type), Does.Contain("PHASE_CHANGED"));
        });
    }

    private static void AddHand(PlayerState player, int count)
    {
        for (var index = 1; index <= count; index++)
        {
            player.Hand.Add(new CardInstance(index, player.PlayerIndex, new CardDefinition(
                "card_" + index, "Card " + index)));
        }
    }

    private static CardInstance Leader(
        long id,
        int owner,
        string cardId,
        string? condition = null,
        int parameter = 0)
    {
        return new CardInstance(id, owner, new CardDefinition(
            cardId,
            cardId,
            isLeader: true,
            leaderWinCondition: condition,
            leaderWinParam: parameter))
        {
            IsLeaderEntity = true,
        };
    }
}
}
