using System;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// Small, deterministic cross-engine fixtures.  The input and output lines
/// emitted by these tests are the material for Java parity tests; they are
/// deliberately not a claim that either engine has complete-match parity.
/// </summary>
[TestFixture]
public sealed class JavaCSharpParityTests
{
    [Test]
    public void EnfeebleFixtureMatchesRulesForNegativeAttackAndHealth()
    {
        var game = new EffectTestFixture();

        game.Apply(
            EffectNames.Enfeeble,
            "ENEMY_MINION",
            amount: -1,
            param: "both",
            selectedTargetId: game.Enemy.InstanceId);

        var applied = game.State.Events.Items[^1];
        Assert.Multiple(() =>
        {
            Assert.That(game.Enemy.Attack, Is.EqualTo(1));
            Assert.That(game.Enemy.Health, Is.EqualTo(4));
            Assert.That(game.Enemy.MaxHealth, Is.EqualTo(4));
            Assert.That(game.State.Players[1].Field, Does.Contain(game.Enemy));
            Assert.That(applied.EventType, Is.EqualTo("ENFEEBLE_APPLIED"));
            Assert.That(applied.Data["target"], Is.EqualTo(3L));
            Assert.That(applied.Data["amount"], Is.EqualTo(-1));
            Assert.That(applied.Data["mode"], Is.EqualTo("both"));
        });

        WriteFixture(
            "ENFEEBLE-both",
            "p0.field=[source#1];p1.field=[enemy#3 atk=2 hp=5 max=5];"
                + "effect=ENFEEBLE target=ENEMY_MINION amount=-1 param=both selected=3",
            "p1.field=[enemy#3 atk=1 hp=4 max=4];event=ENFEEBLE_APPLIED "
                + "target=3 amount=-1 mode=both");
    }

    [Test]
    public void BanishFixtureReturnsTheTargetToItsOwnerDeckWithoutDeath()
    {
        var game = new EffectTestFixture();

        game.Apply(
            EffectNames.Banish,
            "ENEMY_MINION",
            selectedTargetId: game.Enemy.InstanceId);

        var eventTypes = game.State.Events.Items.Select(item => item.EventType).ToArray();
        var banished = game.State.Events.Items.Last(item => item.EventType == "CARD_BANISHED");
        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[1].Field, Does.Not.Contain(game.Enemy));
            Assert.That(game.State.Players[1].Graveyard, Does.Not.Contain(game.Enemy));
            Assert.That(game.State.Players[1].Deck, Does.Contain(game.Enemy));
            Assert.That(game.Enemy.OwnerPlayerIndex, Is.EqualTo(1));
            Assert.That(game.Enemy.ControllerPlayerIndex, Is.EqualTo(1));
            Assert.That(eventTypes, Does.Contain("CARD_BANISHED"));
            Assert.That(eventTypes, Does.Not.Contain("MINION_DESTROYED"));
            Assert.That(banished.Data["target"], Is.EqualTo(3L));
            Assert.That(banished.Data["owner"], Is.EqualTo(1));
        });

        WriteFixture(
            "BANISH-enemy-minion",
            "p0.field=[source#1];p1.field=[enemy#3];"
                + "effect=BANISH target=ENEMY_MINION selected=3",
            "p1.field=[];p1.graveyard=[];p1.deck=[enemy#3];event=CARD_BANISHED "
                + "target=3 owner=1;event=MINION_DESTROYED absent");
    }

    [Test]
    public void ControlFixtureMovesOneTurnAndReturnsTheOwnedMinionAtControllerEnd()
    {
        var game = new EffectTestFixture();

        game.Apply(
            EffectNames.Control,
            "ENEMY_MINION",
            amount: 1,
            selectedTargetId: game.Enemy.InstanceId);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].Field, Does.Contain(game.Enemy));
            Assert.That(game.State.Players[1].Field, Does.Not.Contain(game.Enemy));
            Assert.That(game.Enemy.OwnerPlayerIndex, Is.EqualTo(1));
            Assert.That(game.Enemy.ControllerPlayerIndex, Is.EqualTo(0));
            Assert.That(game.Enemy.ControlledByPlayerIndex, Is.EqualTo(0));
            Assert.That(game.Enemy.ControlTurnsRemaining, Is.EqualTo(1));
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("CONTROL_APPLIED"));
        });

        var flow = TurnFlow.CreateDefault();
        flow.JumpTo(game.State, 0, TurnPhase.End);
        flow.Advance(game.State, 0);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].Field, Does.Not.Contain(game.Enemy));
            Assert.That(game.State.Players[1].Field, Does.Contain(game.Enemy));
            Assert.That(game.Enemy.OwnerPlayerIndex, Is.EqualTo(1));
            Assert.That(game.Enemy.ControllerPlayerIndex, Is.EqualTo(1));
            Assert.That(game.Enemy.ControlledByPlayerIndex, Is.Null);
            Assert.That(game.Enemy.ControlTurnsRemaining, Is.Zero);
        });

        WriteFixture(
            "CONTROL-one-turn",
            "p0.field=[source#1];p1.field=[enemy#3 owner=1 controller=1];"
                + "effect=CONTROL target=ENEMY_MINION amount=1 selected=3;"
                + "then=controller_0 END and advance",
            "after_apply:p0.field=[3] p1.field=[] controller=0 turns=1;"
                + "after_end:p0.field=[] p1.field=[3] controller=1 controlledBy=null turns=0");
    }

    [Test]
    public void DeckCycleFixtureCountsThePlayerWhoseDeckWasReshuffled()
    {
        var state = new GameState(new PlayerState(0), new PlayerState(1));
        state.ReshuffleLossThreshold = 1;
        var card = new CardInstance(
            1,
            0,
            new CardDefinition("cycle_card", "Cycle Card", attack: 1, health: 1, isMinion: true));
        state.GetPlayer(0).Graveyard.Add(card);
        var root = state.Events.Append("CYCLE_FIXTURE");

        new EffectRuntime(state).Draw(
            new EffectSpec(EffectNames.Draw, amount: 1),
            new EffectContext(0, root.EventId));

        var cycleEvent = state.Events.Items.Last(item => item.EventType == "DECK_CYCLED");
        Assert.Multiple(() =>
        {
            Assert.That(state.GetPlayer(0).Deck, Is.Empty);
            Assert.That(state.GetPlayer(0).Graveyard, Is.Empty);
            Assert.That(state.GetPlayer(0).Hand, Has.Exactly(1).EqualTo(card));
            Assert.That(state.GetPlayer(0).ReshuffleCount, Is.EqualTo(1));
            Assert.That(state.GetPlayer(0).CycleWinCount, Is.EqualTo(1));
            Assert.That(state.GetPlayer(1).CycleWinCount, Is.Zero);
            Assert.That(state.WinnerPlayerIndex, Is.EqualTo(0));
            Assert.That(state.WinReason, Is.EqualTo("win.deck_cycles"));
            Assert.That(cycleEvent.Data["player"], Is.EqualTo(0));
            Assert.That(cycleEvent.Data["counted"], Is.EqualTo(true));
        });

        WriteFixture(
            "DECK-CYCLE-owner",
            "p0.deck=[];p0.graveyard=[cycle#1];p0.hand=[];threshold=1;"
                + "effect=DRAW amount=1 source=p0",
            "p0.deck=[] p0.graveyard=[] p0.hand=[1] reshuffles=1 cycleWin=1;"
                + "p1.cycleWin=0;winner=0 reason=win.deck_cycles;"
                + "event=DECK_CYCLED player=0 counted=true");
    }

    [Test]
    public void CastleBreakFixtureGivesMinionLeaderPriorityToTheBreaker()
    {
        var breakerDefinition = new CardDefinition(
            "breaker_leader",
            "Breaker Leader",
            attack: 1,
            health: 4,
            isMinion: true,
            isLeader: true);
        var defenderDefinition = new CardDefinition(
            "defender_leader",
            "Defender Leader",
            attack: 1,
            health: 4,
            isMinion: true,
            isLeader: true);
        var state = new GameState(
            new PlayerState(0),
            new PlayerState(1),
            cardLibrary: new[] { breakerDefinition, defenderDefinition })
        {
            CastleEnabled = true,
            CastleHealth = 1,
            CastleBreakVictoryCount = 9,
        };
        var breaker = new CardInstance(10, 0, breakerDefinition) { IsLeaderEntity = true };
        var defender = new CardInstance(11, 1, defenderDefinition) { IsLeaderEntity = true };
        state.GetPlayer(0).Field.Add(breaker);
        state.GetPlayer(1).Field.Add(defender);
        var root = state.Events.Append("CASTLE_FIXTURE");

        new EffectRuntime(state).DamageCastle(
            new EffectSpec(EffectNames.DamageCastle, amount: 1),
            new EffectContext(0, root.EventId));

        Assert.Multiple(() =>
        {
            Assert.That(state.CastleHealth, Is.Zero);
            Assert.That(state.GetPlayer(0).CycleWinCount, Is.EqualTo(9));
            Assert.That(state.GetPlayer(1).CycleWinCount, Is.Zero);
            Assert.That(state.GetPlayer(1).DamagedThisCycle, Is.True);
            Assert.That(state.WinnerPlayerIndex, Is.EqualTo(0));
            Assert.That(state.WinReason, Is.EqualTo("win.castle_break_minion"));
            Assert.That(state.GetPlayer(0).Field, Does.Contain(breaker));
            Assert.That(state.GetPlayer(1).Field, Does.Contain(defender));
            Assert.That(state.Events.Items.Select(item => item.EventType), Does.Contain("CASTLE_BROKEN"));
            Assert.That(state.Events.Items.Select(item => item.EventType), Does.Contain("GAME_WON"));
        });

        WriteFixture(
            "CASTLE-BREAK-minion-priority",
            "castle.enabled=true castle.hp=1;"
                + "p0.field=[leader#10 minion active];p1.field=[leader#11 minion active];"
                + "effect=DAMAGE_CASTLE amount=1 source=p0",
            "castle.hp=0;p0.cycleWin=9;p1.cycleWin=0;p1.damagedThisCycle=true;"
                + "winner=0 reason=win.castle_break_minion;event=CASTLE_BROKEN breaker=0");
    }

    [Test]
    public void MechanicalPullFixtureRejectsMissingAndInvalidSelectionBeforeMutation()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        Assert.That(router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted, Is.True);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));

        var carrier = new CardInstance(
            25,
            0,
            new CardDefinition(
                "carrier",
                "Carrier",
                attack: 1,
                health: 3,
                isMinion: true,
                faction: "机械遗迹",
                tags: new[] { "机械" }));
        var target = new CardInstance(
            26,
            0,
            new CardDefinition(
                "target",
                "Target",
                attack: 1,
                health: 3,
                isMinion: true,
                faction: "机械遗迹",
                tags: new[] { "机械" }));
        var top = new CardInstance(
            27,
            0,
            new CardDefinition(
                "targeted_pull",
                "Targeted Pull",
                pullEffects: new[]
                {
                    new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 1, "both"),
                }));
        state.GetPlayer(0).Field.Add(carrier);
        state.GetPlayer(0).Field.Add(target);
        state.GetPlayer(0).CloudStack.Add(top);

        var advertised = flow.GetLegalActions(state, 0)
            .Where(action => action.Type == LegalActionGenerator.Pull && action.SourceId == carrier.InstanceId)
            .ToArray();
        Assert.That(advertised.Select(action => action.ActionId), Is.EquivalentTo(new[]
        {
            "pull_25_27_25",
            "pull_25_27_26",
        }));

        var eventsBeforeMissing = state.Events.Count;
        var missing = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            "pull_25_27",
            carrier.InstanceId,
            "27"));
        Assert.Multiple(() =>
        {
            Assert.That(missing.Accepted, Is.False);
            Assert.That(missing.ReasonKey, Is.EqualTo("action.target_required"));
            Assert.That(state.Events.Count, Is.EqualTo(eventsBeforeMissing));
            Assert.That(state.GetPlayer(0).CloudStack, Is.EqualTo(new[] { top }));
            Assert.That(state.GetPlayer(0).PullCount, Is.Zero);
        });

        var eventsBeforeInvalid = state.Events.Count;
        var invalid = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            "pull_25_27_25",
            carrier.InstanceId,
            "27",
            new[] { 999L }));
        Assert.Multiple(() =>
        {
            Assert.That(invalid.Accepted, Is.False);
            Assert.That(invalid.ReasonKey, Is.EqualTo("action.invalid_pull_effect_target"));
            Assert.That(state.Events.Count, Is.EqualTo(eventsBeforeInvalid));
            Assert.That(state.GetPlayer(0).CloudStack, Is.EqualTo(new[] { top }));
            Assert.That(state.GetPlayer(0).PullCount, Is.Zero);
        });

        var valid = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            "pull_25_27_26",
            carrier.InstanceId,
            "27",
            new[] { target.InstanceId }));
        Assert.Multiple(() =>
        {
            Assert.That(valid.Accepted, Is.True);
            Assert.That(target.Attack, Is.EqualTo(2));
            Assert.That(target.Health, Is.EqualTo(4));
            Assert.That(target.MaxHealth, Is.EqualTo(4));
            Assert.That(carrier.Attack, Is.EqualTo(1));
            Assert.That(carrier.Health, Is.EqualTo(3));
            Assert.That(state.GetPlayer(0).CloudStack, Is.Empty);
            Assert.That(state.GetPlayer(0).Graveyard, Has.Exactly(1).EqualTo(top));
            Assert.That(state.GetPlayer(0).PullCount, Is.EqualTo(1));
        });

        WriteFixture(
            "MECHANICAL-PULL-selection",
            "phase=ACTION;p0.field=[carrier#25 atk=1 hp=3,target#26 atk=1 hp=3];"
                + "p0.cloud=[top#27 pull=BUFF(FRIENDLY_MINION,amount=1,param=both)];"
                + "ops=missing selected=[];invalid selected=[999];valid selected=[26]",
            "missing=reject(action.target_required);invalid=reject(action.invalid_pull_effect_target);"
                + "valid=accept;target#26 atk=2 hp=4 max=4;carrier#25 atk=1 hp=3;"
                + "cloud=[] grave=[27] pullCount=1");
    }

    private static void WriteFixture(string id, string input, string output)
    {
        TestContext.Out.WriteLine("PARITY_FIXTURE|{0}|INPUT|{1}", id, input);
        TestContext.Out.WriteLine("PARITY_FIXTURE|{0}|OUTPUT|{1}", id, output);
    }
}
}
