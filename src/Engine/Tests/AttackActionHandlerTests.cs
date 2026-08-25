using System.Linq;
using DominionWars.Adapters;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class AttackActionHandlerTests
{
    [Test]
    public void TauntRestrictsMinionsLeaderAndCoreBeforeMutation()
    {
        var state = CreateActionState(out var flow, out var router);
        state.CastleEnabled = true;
        var attacker = AddReadyMinion(state, 0, 10, "attacker", 3, 5);
        var taunt = AddReadyMinion(state, 1, 11, "taunt", 1, 4, new[] { "嘲讽" });
        var other = AddReadyMinion(state, 1, 12, "other", 2, 3);

        var targets = flow.GetLegalActions(state, 0)
            .Where(item => item.Type == LegalActionGenerator.Attack)
            .Select(item => item.TargetReferenceId)
            .ToArray();
        var before = state.Events.Count;
        var invalid = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Attack,
            sourceEntityId: attacker.InstanceId,
            targetId: "entity_000000000012"));

        Assert.Multiple(() =>
        {
            Assert.That(targets, Is.EqualTo(new[] { "entity_000000000011" }));
            Assert.That(invalid.ReasonKey, Is.EqualTo("action.invalid_target"));
            Assert.That(attacker.AttacksUsed, Is.Zero);
            Assert.That(other.Health, Is.EqualTo(3));
            Assert.That(state.Events.Count, Is.EqualTo(before));
        });

        var accepted = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Attack,
            sourceEntityId: attacker.InstanceId,
            targetId: "entity_000000000011"));
        Assert.Multiple(() =>
        {
            Assert.That(accepted.Accepted, Is.True);
            Assert.That(attacker.AttacksUsed, Is.EqualTo(1));
            Assert.That(attacker.Health, Is.EqualTo(4));
            Assert.That(taunt.Health, Is.EqualTo(1));
        });
    }

    [Test]
    public void AttackCanSelectSharedCastleAndProjectsStableTargetId()
    {
        var state = CreateActionState(out var flow, out var router);
        state.CastleEnabled = true;
        state.CastleHealth = 10;
        var attacker = AddReadyMinion(state, 0, 20, "attacker", 4, 5);

        var snapshot = EngineProjectionAdapter.ToSnapshot(state, "attack_match", flow);
        var castleAction = snapshot.LegalActions.Single(item =>
            item.Type == LegalActionGenerator.Attack && item.TargetId == "core:shared_castle");
        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Attack,
            castleAction.ActionId,
            attacker.InstanceId,
            castleAction.TargetId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.CastleHealth, Is.EqualTo(6));
            Assert.That(state.GetPlayer(1).DamagedThisCycle, Is.True);
            Assert.That(state.Events.Items.Any(item => item.EventType == "ATTACK_DECLARED"), Is.True);
            Assert.That(state.Events.Items.Any(item => item.EventType == "CASTLE_DAMAGED"), Is.True);
        });
    }

    [Test]
    public void ShieldAbsorbsAttackButDefenderStillRetaliates()
    {
        var state = CreateActionState(out _, out var router);
        var attacker = AddReadyMinion(state, 0, 30, "attacker", 3, 5);
        var defender = AddReadyMinion(state, 1, 31, "defender", 2, 4, new[] { "圣盾" });

        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Attack,
            sourceEntityId: attacker.InstanceId,
            targetId: "entity_000000000031"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(defender.Shield, Is.False);
            Assert.That(defender.Health, Is.EqualTo(4));
            Assert.That(attacker.Health, Is.EqualTo(3));
        });
    }

    [Test]
    public void AttacksPerTurnIsHonoredAndFurtherAttackFailsClosed()
    {
        var state = CreateActionState(out _, out var router);
        var definition = new CardDefinition(
            "double", "Double", attack: 1, health: 5, isMinion: true, attacksPerTurn: 2);
        var attacker = new CardInstance(40, 0, definition);
        state.GetPlayer(0).Field.Add(attacker);
        AddReadyMinion(state, 1, 41, "target", 0, 10);

        Assert.That(router.Execute(state, Attack(40, 41)).Accepted, Is.True);
        Assert.That(router.Execute(state, Attack(40, 41)).Accepted, Is.True);
        var before = state.Events.Count;
        var third = router.Execute(state, Attack(40, 41));

        Assert.Multiple(() =>
        {
            Assert.That(attacker.AttacksUsed, Is.EqualTo(2));
            Assert.That(third.ReasonKey, Is.EqualTo("action.invalid_target"));
            Assert.That(state.Events.Count, Is.EqualTo(before));
        });
    }

    [Test]
    public void GuardedLeaderNeedsKingSlayerAndDurabilityDefeatUsesLeaderGate()
    {
        var state = CreateActionState(out var flow, out var router);
        var normal = AddReadyMinion(state, 0, 50, "normal", 3, 5);
        var slayer = new CardInstance(51, 0, new CardDefinition(
            "slayer", "Slayer", attack: 3, health: 5, isMinion: true, kingSlayer: true));
        state.GetPlayer(0).Field.Add(slayer);
        state.GetPlayer(0).Field.Add(new CardInstance(52, 0, new CardDefinition(
            "own_leader", "Own Leader", 1, 4, isMinion: true, isLeader: true)) { IsLeaderEntity = true });
        var leader = new CardInstance(53, 1, new CardDefinition(
            "durable", "Durable", isLeader: true, guard: true, leaderDurability: 2)) { IsLeaderEntity = true };
        state.GetPlayer(1).Field.Add(leader);
        AddReadyMinion(state, 1, 54, "guard", 1, 2);

        var normalTargets = flow.GetLegalActions(state, 0)
            .Where(item => item.SourceId == normal.InstanceId)
            .Select(item => item.TargetReferenceId)
            .ToArray();
        var slayerTargets = flow.GetLegalActions(state, 0)
            .Where(item => item.SourceId == slayer.InstanceId)
            .Select(item => item.TargetReferenceId)
            .ToArray();
        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Attack,
            sourceEntityId: slayer.InstanceId,
            targetId: "entity_000000000053"));

        Assert.Multiple(() =>
        {
            Assert.That(normalTargets, Does.Not.Contain("entity_000000000053"));
            Assert.That(slayerTargets, Does.Contain("entity_000000000053"));
            Assert.That(result.Accepted, Is.True);
            Assert.That(leader.Durability, Is.Zero);
            Assert.That(state.WinnerPlayerIndex, Is.Zero);
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Over));
        });
    }

    [Test]
    public void UnguardedDurabilityLeaderCanBeAttackedWithoutKingSlayer()
    {
        var state = CreateActionState(out var flow, out var router);
        var attacker = AddReadyMinion(state, 0, 60, "normal", 2, 5);
        var leader = new CardInstance(61, 1, new CardDefinition(
            "durable", "Durable", isLeader: true, leaderDurability: 4)) { IsLeaderEntity = true };
        state.GetPlayer(1).Field.Add(leader);

        var targets = flow.GetLegalActions(state, 0)
            .Where(item => item.SourceId == attacker.InstanceId)
            .Select(item => item.TargetReferenceId)
            .ToArray();
        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Attack,
            sourceEntityId: attacker.InstanceId,
            targetId: "entity_000000000061"));

        Assert.Multiple(() =>
        {
            Assert.That(targets, Does.Contain("entity_000000000061"));
            Assert.That(result.Accepted, Is.True);
            Assert.That(leader.Durability, Is.EqualTo(2));
        });
    }

    [Test]
    public void AmbushLeaderInDedicatedZoneIsNotAnAttackTarget()
    {
        var state = CreateActionState(out var flow, out _);
        var attacker = AddReadyMinion(state, 0, 62, "attacker", 2, 5);
        var ambush = new CardInstance(63, 1, new CardDefinition(
            "ambush",
            "Ambush",
            isLeader: true,
            type: "AMBUSH"))
        {
            IsLeaderEntity = true,
        };
        state.GetPlayer(1).AmbushZone.Add(ambush);

        var targets = flow.GetLegalActions(state, 0)
            .Where(item => item.SourceId == attacker.InstanceId)
            .Select(item => item.TargetReferenceId)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(state.GetPlayer(1).Leader, Is.SameAs(ambush));
            Assert.That(targets, Does.Not.Contain("entity_000000000063"));
            Assert.That(state.GetPlayer(1).AmbushZone, Has.Exactly(1).EqualTo(ambush));
        });
    }

    [Test]
    public void MultipleActiveLeadersFailClosedWithoutAdvertisingEnemyLife()
    {
        var state = CreateActionState(out var flow, out _);
        var attacker = AddReadyMinion(state, 0, 64, "attacker", 2, 5);
        var first = new CardInstance(65, 1, new CardDefinition(
            "first_leader", "First Leader", isLeader: true))
        {
            IsLeaderEntity = true,
        };
        var second = new CardInstance(66, 1, new CardDefinition(
            "second_leader", "Second Leader", isLeader: true))
        {
            IsLeaderEntity = true,
        };
        state.GetPlayer(1).LeaderZone.Add(first);
        state.GetPlayer(1).AmbushZone.Add(second);

        var targets = flow.GetLegalActions(state, 0)
            .Where(item => item.SourceId == attacker.InstanceId)
            .Select(item => item.TargetReferenceId)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(state.GetPlayer(1).HasMultipleActiveLeaders, Is.True);
            Assert.That(state.GetPlayer(1).Leader, Is.Null);
            Assert.That(targets, Is.Empty);
            Assert.That(targets, Does.Not.Contain("core:player_1:life"));
        });
    }

    [Test]
    public void ZeroAttackIsRejectedButSummonedChargeCanAttack()
    {
        var state = CreateActionState(out var flow, out var router);
        var zero = AddReadyMinion(state, 0, 70, "zero", 0, 5);
        var charge = AddReadyMinion(state, 0, 71, "charge", 2, 5, new[] { "突袭" });
        zero.SummonedThisTurn = false;
        charge.SummonedThisTurn = true;
        AddReadyMinion(state, 1, 72, "target", 1, 5);

        var actions = flow.GetLegalActions(state, 0)
            .Where(item => item.Type == LegalActionGenerator.Attack)
            .ToArray();
        var before = state.Events.Count;
        var rejected = router.Execute(state, Attack(70, 72));
        var accepted = router.Execute(state, Attack(71, 72));

        Assert.Multiple(() =>
        {
            Assert.That(actions.Any(item => item.SourceId == zero.InstanceId), Is.False);
            Assert.That(actions.Any(item => item.SourceId == charge.InstanceId), Is.True);
            Assert.That(rejected.ReasonKey, Is.EqualTo("action.invalid_target"));
            Assert.That(accepted.Accepted, Is.True);
            Assert.That(charge.AttacksUsed, Is.EqualTo(1));
            Assert.That(state.Events.Count, Is.GreaterThan(before));
        });
    }

    private static GameActionRequest Attack(long sourceId, long targetId)
    {
        return new GameActionRequest(
            0,
            LegalActionGenerator.Attack,
            sourceEntityId: sourceId,
            targetId: "entity_" + targetId.ToString("D12"));
    }

    private static GameState CreateActionState(out TurnFlow flow, out TurnActionRouter router)
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        flow = TurnFlow.CreateDefault();
        router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush));
        return state;
    }

    private static CardInstance AddReadyMinion(
        GameState state,
        int owner,
        long id,
        string cardId,
        int attack,
        int health,
        string[]? keywords = null)
    {
        var card = new CardInstance(id, owner, new CardDefinition(
            cardId, cardId, attack, health, isMinion: true, keywords: keywords));
        state.GetPlayer(owner).Field.Add(card);
        return card;
    }
}
}
