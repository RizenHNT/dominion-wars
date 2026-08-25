using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class AdvancedEffectTests
{
    [Test]
    public void EnfeebleAppliesNegativeAttackAndHealthWithoutAllowingPositiveValues()
    {
        var game = new EffectTestFixture();

        game.Apply(EffectNames.Enfeeble, "ENEMY_MINION", -1, "both", game.Enemy.InstanceId);

        Assert.Multiple(() =>
        {
            Assert.That(game.Enemy.Attack, Is.EqualTo(1));
            Assert.That(game.Enemy.Health, Is.EqualTo(4));
            Assert.That(game.Enemy.MaxHealth, Is.EqualTo(4));
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("ENFEEBLE_APPLIED"));
        });
    }

    [Test]
    public void EnfeebleCanMakeAUnitLethalAndUsesNormalDeathCleanup()
    {
        var game = new EffectTestFixture();
        game.Enemy.Health = 1;
        game.Enemy.MaxHealth = 1;

        game.Apply(EffectNames.Enfeeble, "ENEMY_MINION", -1, "hp", game.Enemy.InstanceId);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[1].Field, Does.Not.Contain(game.Enemy));
            Assert.That(game.State.Players[1].Graveyard, Does.Contain(game.Enemy));
            Assert.That(game.State.Events.Items.Select(item => item.EventType), Does.Contain("MINION_DESTROYED"));
        });
    }

    [Test]
    public void BanishReturnsAUnitToItsOwnerDeckWithoutCreatingADeath()
    {
        var game = new EffectTestFixture();

        game.Apply(EffectNames.Banish, "ENEMY_MINION", selectedTargetId: game.Enemy.InstanceId);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[1].Field, Does.Not.Contain(game.Enemy));
            Assert.That(game.State.Players[1].Graveyard, Does.Not.Contain(game.Enemy));
            Assert.That(game.State.Players[1].Deck, Does.Contain(game.Enemy));
            Assert.That(game.State.Events.Items.Select(item => item.EventType), Does.Contain("CARD_BANISHED"));
            Assert.That(game.State.Events.Items.Select(item => item.EventType), Does.Not.Contain("MINION_DESTROYED"));
        });
    }

    [Test]
    public void ControlMovesAnEnemyMinionAndExpiresAtControllerTurnEnd()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Control, "ENEMY_MINION", amount: 1, selectedTargetId: game.Enemy.InstanceId);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].Field, Does.Contain(game.Enemy));
            Assert.That(game.State.Players[1].Field, Does.Not.Contain(game.Enemy));
            Assert.That(game.Enemy.ControllerPlayerIndex, Is.EqualTo(0));
            Assert.That(game.Enemy.ControlTurnsRemaining, Is.EqualTo(1));
        });

        var flow = DominionWars.Engine.Turns.TurnFlow.CreateDefault();
        flow.JumpTo(game.State, 0, DominionWars.Engine.Turns.TurnPhase.End);
        flow.Advance(game.State, 0);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].Field, Does.Not.Contain(game.Enemy));
            Assert.That(game.State.Players[1].Field, Does.Contain(game.Enemy));
            Assert.That(game.Enemy.ControlledByPlayerIndex, Is.Null);
        });
    }

    [Test]
    public void ControlledMinionAttacksForItsCurrentController()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Control, "ENEMY_MINION", amount: 1, selectedTargetId: game.Enemy.InstanceId);
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        flow.JumpTo(game.State, 0, TurnPhase.Action);

        var result = router.Execute(game.State, new GameActionRequest(
            0,
            LegalActionGenerator.Attack,
            sourceEntityId: game.Enemy.InstanceId,
            targetId: "core:player_1:life"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(game.State.GetPlayer(0).Life, Is.EqualTo(20));
            Assert.That(game.State.GetPlayer(1).Life, Is.EqualTo(18));
        });
    }

    [Test]
    public void ControlledMinionEffectsUseItsCurrentControllersTurnState()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Control, "ENEMY_MINION", amount: 1, selectedTargetId: game.Enemy.InstanceId);
        game.State.GetPlayer(1).EffectsNegatedThisTurn = true;
        var victim = new CardInstance(460, 1, new CardDefinition(
            "control_effect_victim", "Victim", attack: 1, health: 3, isMinion: true));
        game.State.GetPlayer(1).Field.Add(victim);
        var root = game.State.Events.Append("CARD_PLAYED");

        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 1),
            new EffectContext(0, root.EventId, sourceCard: game.Enemy, selectedTargetId: victim.InstanceId));

        Assert.That(victim.Health, Is.EqualTo(2));
    }

    [Test]
    public void CommitPushPullAndRollbackUseExplicitMechanicalZones()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Commit, "SELF");

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].CommitQueue, Does.Contain(game.Source));
            Assert.That(game.State.Players[0].Field, Does.Not.Contain(game.Source));
        });

        game.Apply(EffectNames.Push);
        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].CommitQueue, Is.Empty);
            Assert.That(game.State.Players[0].CloudStack, Does.Contain(game.Source));
        });

        var carrierDefinition = new CardDefinition(
            "mechanical_carrier", "Mechanical Carrier", 1, 2, isMinion: true,
            faction: "机械遗迹", tags: new[] { "机械" });
        var carrier = new CardInstance(101, 0, carrierDefinition);
        game.State.Players[0].Field.Add(carrier);
        game.Apply(EffectNames.Pull, selectedTargetId: carrier.InstanceId);
        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].CloudStack, Is.Empty);
            Assert.That(game.State.Players[0].Graveyard, Does.Contain(game.Source));
            Assert.That(game.State.Players[0].PullCount, Is.EqualTo(1));
        });

        var rollbackCard = new CardInstance(100, 0, game.SoldierDefinition);
        game.State.Players[0].CommitQueue.Add(rollbackCard);
        game.Apply(EffectNames.Rollback);
        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].CommitQueue, Is.Empty);
            Assert.That(game.State.Players[0].Hand, Does.Contain(rollbackCard));
        });
    }

    [Test]
    public void CommitRejectsNonMechanicalCardsAndCardsOutsideTheField()
    {
        var game = new EffectTestFixture();
        var nonMechanical = new CardInstance(104, 0, new CardDefinition(
            "non_mechanical_commit_source",
            "Non-Mechanical Commit Source",
            attack: 1,
            health: 2,
            isMinion: true));
        game.State.Players[0].Field.Add(nonMechanical);
        var nonMechanicalRoot = game.State.Events.Append("CARD_PLAYED");
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Commit, "SELF"),
            new EffectContext(0, nonMechanicalRoot.EventId, sourceCard: nonMechanical, playedCard: nonMechanical));

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].CommitQueue, Is.Empty);
            Assert.That(game.State.Players[0].Field, Does.Contain(nonMechanical));
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("EFFECT_SKIPPED"));
        });

        var mechanical = new CardInstance(105, 0, new CardDefinition(
            "mechanical_commit_source",
            "Mechanical Commit Source",
            attack: 1,
            health: 2,
            isMinion: true,
            faction: "机械遗迹",
            tags: new[] { "机械" }));
        game.State.Players[0].Hand.Add(mechanical);
        var root = game.State.Events.Append("CARD_PLAYED");
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Commit, "SELF"),
            new EffectContext(0, root.EventId, sourceCard: mechanical, playedCard: mechanical));

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].CommitQueue, Is.Empty);
            Assert.That(game.State.Players[0].Hand, Does.Contain(mechanical));
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("EFFECT_SKIPPED"));
        });

        game.State.Players[0].Hand.Remove(mechanical);
        game.State.Players[0].Field.Add(mechanical);
        root = game.State.Events.Append("CARD_PLAYED");
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Commit, "SELF"),
            new EffectContext(0, root.EventId, sourceCard: mechanical, playedCard: mechanical));

        Assert.That(game.State.Players[0].CommitQueue, Does.Contain(mechanical));
    }

    [Test]
    public void PullEffectsResolveOnTheSelectedMechanicalCarrierBeforeTheCardReachesTheGraveyard()
    {
        var carrierDefinition = new CardDefinition(
            "mechanical_carrier_effect", "Mechanical Carrier", 1, 2, isMinion: true,
            faction: "机械遗迹", tags: new[] { "机械" });
        var cloudDefinition = new CardDefinition(
            "cloud_effect", "Cloud Effect", pullEffects: new[] {
                new EffectSpec(EffectNames.Buff, "SELF", 2, "hp"),
            });
        var state = new GameState(new PlayerState(0), new PlayerState(1));
        var carrier = new CardInstance(301, 0, carrierDefinition);
        var cloudCard = new CardInstance(302, 0, cloudDefinition);
        state.Players[0].Field.Add(carrier);
        state.Players[0].CloudStack.Add(cloudCard);
        var root = state.Events.Append("CARD_PLAYED");

        EffectDispatcher.CreateDefault(new EffectRuntime(state)).Apply(
            new EffectSpec(EffectNames.Pull),
            new EffectContext(0, root.EventId, selectedTargetId: carrier.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(carrier.Health, Is.EqualTo(4));
            Assert.That(state.Players[0].Graveyard, Does.Contain(cloudCard));
            Assert.That(state.Players[0].PullCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void PullRemovesTheTopBeforeNestedPullEffectsResolve()
    {
        var carrierDefinition = new CardDefinition(
            "nested_pull_carrier", "Nested Pull Carrier", 1, 2,
            isMinion: true, faction: "机械遗迹", tags: new[] { "机械" });
        var lower = new CardInstance(321, 0, new CardDefinition(
            "lower_cloud_card", "Lower Cloud Card"));
        var top = new CardInstance(322, 0, new CardDefinition(
            "top_cloud_card", "Top Cloud Card",
            pullEffects: new[] { new EffectSpec(EffectNames.Pull) }));
        var state = new GameState(new PlayerState(0), new PlayerState(1));
        var carrier = new CardInstance(323, 0, carrierDefinition);
        state.Players[0].Field.Add(carrier);
        state.Players[0].CloudStack.Add(lower);
        state.Players[0].CloudStack.Add(top);
        var root = state.Events.Append("CARD_PLAYED");

        EffectDispatcher.CreateDefault(new EffectRuntime(state)).Apply(
            new EffectSpec(EffectNames.Pull),
            new EffectContext(0, root.EventId, sourceCard: carrier, selectedTargetId: carrier.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(state.Players[0].CloudStack, Is.Empty);
            Assert.That(state.Players[0].Graveyard, Is.EquivalentTo(new[] { lower, top }));
            Assert.That(state.Players[0].PullCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void CommitAndPushResolveTheirCardLifecycleEffectLists()
    {
        var carrierDefinition = new CardDefinition(
            "mechanical_carrier_lifecycle", "Mechanical Carrier", 1, 2, isMinion: true,
            faction: "机械遗迹", tags: new[] { "机械" });
        var queuedDefinition = new CardDefinition(
            "queued_lifecycle", "Queued Lifecycle", faction: "机械遗迹",
            commitEffects: new[] { new EffectSpec(EffectNames.Draw, amount: 1) },
            pushEffects: new[] { new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 1, "hp") });
        var state = new GameState(new PlayerState(0), new PlayerState(1));
        var carrier = new CardInstance(311, 0, carrierDefinition);
        var queued = new CardInstance(312, 0, queuedDefinition);
        var drawn = new CardInstance(313, 0, new CardDefinition("drawn_lifecycle", "Drawn"));
        state.Players[0].Field.Add(carrier);
        state.Players[0].Field.Add(queued);
        state.Players[0].Deck.Add(drawn);
        var root = state.Events.Append("CARD_PLAYED");
        var dispatcher = EffectDispatcher.CreateDefault(new EffectRuntime(state));

        dispatcher.Apply(
            new EffectSpec(EffectNames.Commit, "SELF"),
            new EffectContext(0, root.EventId, sourceCard: queued, playedCard: queued));
        dispatcher.Apply(
            new EffectSpec(EffectNames.Push),
            new EffectContext(0, root.EventId, sourceCard: queued, selectedTargetId: carrier.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(state.Players[0].Hand, Does.Contain(drawn));
            Assert.That(state.Players[0].CloudStack, Does.Contain(queued));
            Assert.That(carrier.Health, Is.EqualTo(3));
        });
    }

    [Test]
    public void NewKeywordValuesAreAcceptedAsMarkers()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.GrantKeyword, "FRIENDLY_MINION", param: "震慑", selectedTargetId: game.Friendly.InstanceId);

        Assert.That(game.Friendly.HasKeyword("震慑"), Is.True);
    }

    [Test]
    public void WoodGrowthUsesAdditiveAndMultiplicativeLayersAndSealsTheTarget()
    {
        var woodSource = new CardDefinition(
            "wood_source", "Wood Source", faction: "古木圣地", isMinion: true);
        var targetDefinition = new CardDefinition(
            "wood_target", "Wood Target", attack: 2, health: 3, isMinion: true);
        var state = new GameState(new PlayerState(0), new PlayerState(1));
        state.Players[0].RootStacks = 2;
        state.Players[0].RampantStacks = 3;
        var source = new CardInstance(401, 0, woodSource);
        var target = new CardInstance(402, 0, targetDefinition);
        state.Players[0].Field.Add(source);
        state.Players[0].Field.Add(target);
        var root = state.Events.Append("CARD_PLAYED");

        EffectDispatcher.CreateDefault(new EffectRuntime(state)).Apply(
            new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 1, "both"),
            new EffectContext(0, root.EventId, sourceCard: source, selectedTargetId: target.InstanceId));

        var buffEvent = state.Events.Items.Last(item => item.EventType == "BUFF_APPLIED");
        Assert.Multiple(() =>
        {
            Assert.That(target.Health, Is.EqualTo(27));
            Assert.That(target.MaxHealth, Is.EqualTo(27));
            Assert.That(target.Attack, Is.EqualTo(0));
            Assert.That(target.Sealed, Is.True);
            Assert.That(buffEvent.Data["amount"], Is.EqualTo(24));
        });
    }

    [Test]
    public void SealedUnitDoesNotExposeKeywordsOrConsumeShield()
    {
        var definition = new CardDefinition(
            "sealed_keyword_target",
            "Sealed Keyword Target",
            attack: 2,
            health: 3,
            isMinion: true,
            keywords: new[] { "嘲讽", "圣盾", "突袭" });
        var state = new GameState(new PlayerState(0), new PlayerState(1));
        var attacker = new CardInstance(451, 0, new CardDefinition(
            "sealed_attacker", "Attacker", attack: 1, health: 2, isMinion: true));
        var target = new CardInstance(452, 1, definition) { Sealed = true };
        state.Players[0].Field.Add(attacker);
        state.Players[1].Field.Add(target);
        var root = state.Events.Append("CARD_PLAYED");

        Assert.Multiple(() =>
        {
            Assert.That(target.HasKeyword("嘲讽"), Is.False);
            Assert.That(target.HasKeyword("圣盾"), Is.False);
            Assert.That(target.HasKeyword("突袭"), Is.False);
        });

        EffectDispatcher.CreateDefault(new EffectRuntime(state)).Apply(
            new EffectSpec(EffectNames.Damage, "ENEMY_TARGET", 1),
            new EffectContext(0, root.EventId, sourceCard: attacker, selectedTargetId: target.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(target.Shield, Is.True);
            Assert.That(target.Health, Is.EqualTo(2));
        });
    }

    [Test]
    public void PullTotalWinConditionUsesSuccessfulPulls()
    {
        var pullLeader = new CardDefinition(
            "pull_leader", "Pull Leader", 1, 4, isMinion: true, isLeader: true,
            faction: "机械遗迹",
            leaderWinCondition: "PULL_TOTAL_GE", leaderWinParam: 1);
        var otherLeader = new CardDefinition(
            "other_leader", "Other Leader", 1, 4, isMinion: true, isLeader: true);
        var state = new GameState(new PlayerState(0), new PlayerState(1), cardLibrary: new[] { pullLeader, otherLeader });
        var left = new CardInstance(200, 0, pullLeader) { IsLeaderEntity = true };
        var right = new CardInstance(201, 1, otherLeader) { IsLeaderEntity = true };
        var cloudCard = new CardInstance(202, 0, new CardDefinition("cloud", "Cloud", 1, 1, isMinion: true));
        state.Players[0].Field.Add(left);
        state.Players[1].Field.Add(right);
        state.Players[0].CloudStack.Add(cloudCard);
        var runtime = new EffectRuntime(state);
        var dispatcher = EffectDispatcher.CreateDefault(runtime);
        var root = state.Events.Append("CARD_PLAYED");

        dispatcher.Apply(new EffectSpec(EffectNames.Pull), new EffectContext(0, root.EventId, left));

        Assert.That(state.WinnerPlayerIndex, Is.EqualTo(0));
    }

    [Test]
    public void GiantHealthWinConditionRequiresASealedUnit()
    {
        var giantLeader = new CardDefinition(
            "giant_leader", "Giant Leader", 1, 4, isMinion: true, isLeader: true,
            leaderWinCondition: "GIANT_HEALTH_GE", leaderWinParam: 5);
        var otherLeader = new CardDefinition(
            "other_leader_2", "Other Leader", 1, 4, isMinion: true, isLeader: true);
        var giant = new CardInstance(210, 0, new CardDefinition("giant", "Giant", 1, 5, isMinion: true))
        {
            Sealed = true,
        };
        var state = new GameState(new PlayerState(0), new PlayerState(1));
        var left = new CardInstance(211, 0, giantLeader) { IsLeaderEntity = true };
        var right = new CardInstance(212, 1, otherLeader) { IsLeaderEntity = true };
        state.Players[0].Field.Add(left);
        state.Players[0].Field.Add(giant);
        state.Players[1].Field.Add(right);
        var runtime = new EffectRuntime(state);
        var dispatcher = EffectDispatcher.CreateDefault(runtime);
        var root = state.Events.Append("CARD_PLAYED");

        dispatcher.Apply(
            new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 1, "hp"),
            new EffectContext(0, root.EventId, left, selectedTargetId: giant.InstanceId));

        Assert.That(state.WinnerPlayerIndex, Is.EqualTo(0));
    }
}
}
