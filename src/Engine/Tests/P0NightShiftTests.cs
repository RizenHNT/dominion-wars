using System;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Rules;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// 夜班 P0 回归用例（独立文件，避免与其它代理正在编辑的用例文件冲突）。
///
/// P0-1 先驱威压（docs/RULES.md §3.2，惩罚侧）：
///   仅一方统领在场时，**对方所有卡牌**的有效惩罚值 +1
///   （pioneerOpponentPunishBonus，data/balance.json = 1）；
///   统领方自己的卡牌 −N（pioneerSelfPunishDiscount，默认 0）。
///   验收：仅 0 号位统领在场时，1 号位打出的打印惩罚值 1 的牌必须让对手抽 2 张。
///
/// P0-3 批内死亡结算（EffectRuntime.CheckAll / EffectDispatcher.ApplyAll）：
///   一个效果批（ApplyAll）内，死亡随从必须留到批结束才清理，否则后续段会
///   静默命中不到已经"被判死"的目标。清理必须且只能在批结束后发生一次。
/// </summary>
[TestFixture]
public sealed class P0NightShiftTests
{
    // =====================================================================
    // P0-1 先驱威压 · 惩罚侧
    // =====================================================================

    /// <summary>
    /// 验收主例：仅 0 号位统领在场 → 1 号位（无统领方）打出的打印惩罚值 1 的牌，
    /// 其对手（0 号位）必须抽 2 张，而不是 1 张。
    /// </summary>
    [Test]
    public void OnlyPlayerZeroLeadsSoPlayerOneCardCostsOneMore()
    {
        AssertPlayedPunishOne(leaderZero: true, leaderOne: false, actor: 1, expectedDraws: 2);
    }

    /// <summary>镜像例：仅 1 号位统领在场 → 0 号位打出的牌 +1，1 号位抽 2 张。</summary>
    [Test]
    public void OnlyPlayerOneLeadsSoPlayerZeroCardCostsOneMore()
    {
        AssertPlayedPunishOne(leaderZero: false, leaderOne: true, actor: 0, expectedDraws: 2);
    }

    /// <summary>控制组：双方统领都在场不是先驱状态 → 维持打印值 1。</summary>
    [Test]
    public void BothLeadersFieldedKeepThePrintedPunish()
    {
        AssertPlayedPunishOne(leaderZero: true, leaderOne: true, actor: 0, expectedDraws: 1);
    }

    /// <summary>控制组：双方都无统领 → 维持打印值 1。</summary>
    [Test]
    public void NoLeaderFieldedKeepsThePrintedPunish()
    {
        AssertPlayedPunishOne(leaderZero: false, leaderOne: false, actor: 0, expectedDraws: 1);
    }

    /// <summary>
    /// 先驱方自己的牌不加成：仅 0 号位统领在场、由 0 号位打出时，
    /// 只受 pioneerSelfPunishDiscount（出厂 0）影响 → 仍为 1。
    /// </summary>
    [Test]
    public void PioneerOwnCardGetsNoOpponentBonus()
    {
        AssertPlayedPunishOne(leaderZero: true, leaderOne: false, actor: 0, expectedDraws: 1);
    }

    /// <summary>
    /// 备选配置 pioneerSelfPunishDiscount 的接线证明：仅本方统领在场时，
    /// 打印值 1 − 折扣 3 钳制为 0，所以对手一张都不抽（未接线时会抽 1 张）。
    /// </summary>
    [Test]
    public void PioneerSelfDiscountIsWiredAndClampedAtZero()
    {
        AssertPlayedPunishOne(
            leaderZero: true,
            leaderOne: false,
            actor: 0,
            expectedDraws: 0,
            rules: new MatchRules(
                handLimit: 8,
                pioneerHandLimitBonus: 2,
                pioneerOpponentPunishBonus: 1,
                pioneerSelfPunishDiscount: 3));
    }

    /// <summary>伏击盖放同样支付被先驱威压修正后的惩罚值（§6：惩罚值在盖放时支付）。</summary>
    [Test]
    public void AmbushSetPaysThePioneerModifiedPunish()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Ambush));

        FieldLeader(state, 1, 502);
        // The punish draw always goes to the actor's opponent, so the fizzle
        // guard (cost > opponent deck) needs cards in player 1's deck.
        SeedDeck(state, 1, 4);
        var ambush = new CardInstance(920, 0, new CardDefinition(
            "night_ambush",
            "Night Ambush",
            type: "AMBUSH",
            punish: 1));
        state.GetPlayer(0).Hand.Add(ambush);
        var ambushDrawnBefore = state.GetPlayer(1).Hand.Count;

        var result = router.Execute(state, new GameActionRequest(
            0,
            TurnAction.SetAmbush,
            AmbushActionHandler.ActionId(ambush.InstanceId),
            ambush.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True, result.ReasonKey);
            Assert.That(state.GetPlayer(0).AmbushZone, Has.Exactly(1).EqualTo(ambush));
            Assert.That(state.GetPlayer(0).Hand, Is.Empty);
            // The punish draw belongs to the actor's opponent (player 1).
            Assert.That(
                state.GetPlayer(1).Hand,
                Has.Count.EqualTo(ambushDrawnBefore + 2));
            Assert.That(state.GetPlayer(1).PunishDrawnThisTurn, Is.EqualTo(2));
        });
    }

    /// <summary>合法动作里向客户端公布的惩罚值必须与引擎实际收取的一致。</summary>
    [Test]
    public void LegalActionsAdvertiseThePioneerModifiedPunish()
    {
        var state = CreateActionPhaseMatch(out var flow, out _);
        FieldLeader(state, 1, 502);
        SeedDeck(state, 1, 4);
        var card = new CardInstance(910, 0, PunishOne);
        state.GetPlayer(0).Hand.Add(card);

        var action = flow.GetLegalActions(state, 0)
            .Single(item => item.Type == LegalActionGenerator.PlayCard
                && item.SourceId == card.InstanceId);

        Assert.That(action.Payload["punish"], Is.EqualTo(2));
    }

    /// <summary>
    /// 范围判定（需 owner 裁决）：机械生命周期惩罚（COMMIT）**不**套用先驱威压，
    /// 它支付 §12.4 声明的逐卡 commitCost 原值。§3.2 的 +1 说的是"卡牌惩罚值"
    /// ——即出牌时支付的打印惩罚值；§12.4 把 commitCost/downloadCost 定义为
    /// 生命周期动作触发的惩罚抽牌数量，不是出牌。实测把 +1 套到这里会翻转
    /// 机械卡组的六次下载胜利线（AiLifecyclePolicyTests），所以本用例锁定
    /// 当前（未套用）行为，等 owner 明确后再改。
    /// </summary>
    [Test]
    public void CommitLifecyclePunishKeepsTheDeclaredValue()
    {
        var state = CreateActionPhaseMatch(out var flow, out var router);
        FieldLeader(state, 1, 502);
        SeedDeck(state, 1, 4);
        var mechanical = new CardInstance(930, 0, new CardDefinition(
            "night_mechanical",
            "Night Mechanical",
            attack: 1,
            health: 3,
            isMinion: true,
            faction: "机械遗迹",
            tags: new[] { "机械" },
            commitCost: 1));
        state.GetPlayer(0).Field.Add(mechanical);

        var advertised = flow.GetLegalActions(state, 0)
            .Single(item => item.Type == LegalActionGenerator.Commit);
        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Commit,
            $"commit_{mechanical.InstanceId}",
            mechanical.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True, result.ReasonKey);
            Assert.That(advertised.Payload["commitCost"], Is.EqualTo(1));
            Assert.That(advertised.Payload["punish"], Is.EqualTo(1),
                "COMMIT 公布的惩罚值就是声明值，不叠加先驱威压。");
            Assert.That(state.GetPlayer(1).PunishDrawnThisTurn, Is.EqualTo(1),
                "COMMIT 实际收取的惩罚值也是声明值。");
            Assert.That(state.GetPlayer(0).CommitQueue, Has.Exactly(1).EqualTo(mechanical));
        });
    }

    // =====================================================================
    // P0-3 批内死亡结算
    // =====================================================================

    /// <summary>
    /// 两段 AOE 作用于同一批目标：两段都必须结算，两个 3 血随从各吃 2 次伤害，
    /// 墓地里各只有一张，没有任何一段被静默吞掉。
    /// </summary>
    [Test]
    public void TwoPartAoeResolvesBothPartsAgainstTheSameTargets()
    {
        var game = new BatchFixture();
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Damage, "ALL_ENEMY_MINIONS", 5),
            new EffectSpec(EffectNames.Damage, "ALL_ENEMY_MINIONS", 5),
        }, game.Context());

        Assert.Multiple(() =>
        {
            Assert.That(CountEvents(game.State, "DAMAGE_DEALT"), Is.EqualTo(4));
            Assert.That(CountSkippedDamage(game.State), Is.Zero);
            Assert.That(game.State.GetPlayer(1).Field, Is.Empty);
            Assert.That(game.State.GetPlayer(1).Graveyard, Has.Exactly(1).EqualTo(game.EnemyA));
            Assert.That(game.State.GetPlayer(1).Graveyard, Has.Exactly(1).EqualTo(game.EnemyB));
            Assert.That(CountEvents(game.State, "MINION_DESTROYED"), Is.EqualTo(2));
        });
    }

    /// <summary>
    /// 关键回归：批内第一段已经打死目标后，批中段触发的被动钩子链
    /// （OnOpponentDiscardEffects → 嵌套 ApplyAll）不得提前结算死亡，
    /// 否则第三段 AOE 会静默命中不到目标（EFFECT_SKIPPED target.none）。
    /// </summary>
    [Test]
    public void NestedHookChainDoesNotSettleDeathsInsideTheParentBatch()
    {
        var game = new BatchFixture();
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Damage, "ALL_ENEMY_MINIONS", 5),
            new EffectSpec(EffectNames.DiscardOppRandom, amount: 1),
            new EffectSpec(EffectNames.Damage, "ALL_ENEMY_MINIONS", 5),
        }, game.Context());

        Assert.Multiple(() =>
        {
            Assert.That(CountEvents(game.State, "DAMAGE_DEALT"), Is.EqualTo(4),
                "第二段/第三段 AOE 对同一批目标必须照常结算。");
            Assert.That(CountSkippedDamage(game.State), Is.Zero,
                "命中不到目标时的 EFFECT_SKIPPED 就是被静默吞掉的那一段。");
            Assert.That(game.EnemyA.Health, Is.EqualTo(-7), "3 - 5 - 5：两段伤害都落在它身上。");
            Assert.That(game.EnemyB.Health, Is.EqualTo(-7));
            Assert.That(game.State.GetPlayer(1).Field, Is.Empty);
            Assert.That(game.State.GetPlayer(1).Graveyard, Has.Exactly(1).EqualTo(game.EnemyA));
            Assert.That(game.State.GetPlayer(1).Graveyard, Has.Exactly(1).EqualTo(game.EnemyB));
            Assert.That(CountEvents(game.State, "MINION_DESTROYED"), Is.EqualTo(2),
                "死亡在批结束后只结算一次，不重复入墓。");
        });
    }

    /// <summary>
    /// 反向守卫：单效果致命伤害仍然必须真的移除该随从（延迟结算不能变成永久泄漏）。
    /// </summary>
    [Test]
    public void SingleLethalDamageStillRemovesTheMinion()
    {
        var game = new BatchFixture();
        game.Dispatcher.Apply(new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 5),
            game.Context(game.EnemyA.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(game.State.GetPlayer(1).Field, Has.Exactly(1).EqualTo(game.EnemyB));
            Assert.That(game.State.GetPlayer(1).Graveyard, Has.Exactly(1).EqualTo(game.EnemyA));
            Assert.That(CountEvents(game.State, "MINION_DESTROYED"), Is.EqualTo(1));
            Assert.That(game.EnemyA.Health, Is.EqualTo(-2));
        });
    }

    /// <summary>反向守卫：单效果批（ApplyAll）里的致命伤害也必须在批尾结算掉。</summary>
    [Test]
    public void SingleEffectBatchStillSettlesItsDeath()
    {
        var game = new BatchFixture();
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 5),
        }, game.Context(game.EnemyA.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(game.State.GetPlayer(1).Field, Has.Exactly(1).EqualTo(game.EnemyB));
            Assert.That(game.State.GetPlayer(1).Graveyard, Has.Exactly(1).EqualTo(game.EnemyA));
            Assert.That(CountEvents(game.State, "MINION_DESTROYED"), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// 批内"先打死、后治疗"仍可救回（已有语义，见 EffectChainTests），
    /// 说明批内死亡确实被延迟到批尾统一判定。
    /// </summary>
    [Test]
    public void BatchStillDefersDeathUntilTheWholeChainEnds()
    {
        var game = new BatchFixture();
        game.Dispatcher.ApplyAll(new[]
        {
            new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 5),
            new EffectSpec(EffectNames.Heal, "ENEMY_MINION", 5),
        }, game.Context(game.EnemyA.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(game.State.GetPlayer(1).Field, Does.Contain(game.EnemyA));
            Assert.That(game.State.GetPlayer(1).Graveyard, Has.None.EqualTo(game.EnemyA));
            Assert.That(CountEvents(game.State, "MINION_DESTROYED"), Is.Zero);
            Assert.That(game.EnemyA.Health, Is.EqualTo(3));
        });
    }

    // =====================================================================
    // fixtures
    // =====================================================================

    private static readonly CardDefinition PunishOne = new CardDefinition(
        "night_probe",
        "Night Probe",
        punish: 1);

    private static GameState CreateActionPhaseMatch(out TurnFlow flow, out TurnActionRouter router)
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        flow = TurnFlow.CreateDefault();
        router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        var skip = router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush));
        Assert.That(skip.Accepted, Is.True, skip.ReasonKey);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
        return state;
    }

    /// <summary>
    /// Plays one printed punish-1 card for <paramref name="actor"/> and asserts how
    /// many cards that play made the opposite player draw.
    /// </summary>
    private static void AssertPlayedPunishOne(
        bool leaderZero,
        bool leaderOne,
        int actor,
        int expectedDraws,
        MatchRules? rules = null)
    {
        var state = new GameState(
            new PlayerState(0, 20),
            new PlayerState(1, 20),
            rules: rules);
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        Assert.That(
            router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted,
            Is.True);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));

        if (leaderZero)
        {
            FieldLeader(state, 0, 501);
        }

        if (leaderOne)
        {
            FieldLeader(state, 1, 502);
        }

        SeedDeck(state, actor, 4);
        SeedDeck(state, 1 - actor, 4);
        var card = new CardInstance(910 + actor, actor, PunishOne);
        state.GetPlayer(actor).Hand.Add(card);
        // The action phase belongs to the first player; hand it to the actor so
        // the router accepts the play instead of reporting action.not_current_player.
        state.CurrentPlayerIndex = actor;
        var target = 1 - actor;
        var handBefore = state.GetPlayer(target).Hand.Count;

        var result = router.Execute(state, new GameActionRequest(
            actor,
            LegalActionGenerator.PlayCard,
            sourceEntityId: card.InstanceId));
        var drawn = state.GetPlayer(target).Hand.Count - handBefore;

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True, result.ReasonKey);
            Assert.That(drawn, Is.EqualTo(expectedDraws),
                $"仅 leader0={leaderZero}/leader1={leaderOne} 在场时，{actor} 号位打出打印惩罚值 1 的牌");
            Assert.That(state.GetPlayer(target).PunishDrawnThisTurn, Is.EqualTo(expectedDraws));
            Assert.That(CountPunishDrawEvent(state), Is.EqualTo(expectedDraws));
        });
    }

    private static void FieldLeader(GameState state, int playerIndex, long instanceId)
    {
        state.GetPlayer(playerIndex).Field.Add(new CardInstance(
            instanceId,
            playerIndex,
            new CardDefinition(
                "night_leader_" + playerIndex,
                "Night Leader " + playerIndex,
                isLeader: true))
        {
            IsLeaderEntity = true,
        });
    }

    private static void SeedDeck(GameState state, int playerIndex, int count)
    {
        for (var index = 0; index < count; index++)
        {
            state.GetPlayer(playerIndex).Deck.Add(new CardInstance(
                800 + (playerIndex * 100) + index,
                playerIndex,
                new CardDefinition(
                    "night_deck_" + playerIndex + "_" + index,
                    "Night Deck " + playerIndex + " " + index)));
        }
    }

    private static int CountEvents(GameState state, string eventType)
        => state.Events.Items.Count(item => string.Equals(item.EventType, eventType, StringComparison.Ordinal));

    private static int CountSkippedDamage(GameState state)
        => state.Events.Items.Count(item =>
            string.Equals(item.EventType, "EFFECT_SKIPPED", StringComparison.Ordinal)
            && item.Data.TryGetValue("action", out var action)
            && Equals(action, EffectNames.Damage));

    private static int CountPunishDrawEvent(GameState state)
    {
        foreach (var item in state.Events.Items)
        {
            if (string.Equals(item.EventType, "PUNISH_DRAW", StringComparison.Ordinal)
                && item.Data.TryGetValue("count", out var count)
                && count is int value)
            {
                return value;
            }
        }

        return 0;
    }

    /// <summary>
    /// 两个 3 血敌方随从 + 一个带 OnOpponentDiscardEffects 被动钩子的己方随从，
    /// 用来构造"批内已判死 → 中段触发的嵌套钩子链 → 后段 AOE"的可达形态。
    /// </summary>
    private sealed class BatchFixture
    {
        public BatchFixture()
        {
            HookDefinition = new CardDefinition(
                "night_hook",
                "Night Hook",
                attack: 1,
                health: 5,
                isMinion: true,
                onOpponentDiscardEffects: new[] { new EffectSpec(EffectNames.GainLife, amount: 1) });
            var sourceDefinition = new CardDefinition(
                "night_batch_source",
                "Night Batch Source",
                attack: 1,
                health: 5,
                isMinion: true);
            var targetDefinition = new CardDefinition(
                "night_batch_target",
                "Night Batch Target",
                health: 3,
                isMinion: true);

            State = new GameState(
                new PlayerState(0, 20),
                new PlayerState(1, 20),
                cardLibrary: new[] { HookDefinition, sourceDefinition, targetDefinition });

            Source = new CardInstance(1, 0, sourceDefinition);
            Hook = new CardInstance(2, 0, HookDefinition);
            EnemyA = new CardInstance(11, 1, targetDefinition);
            EnemyB = new CardInstance(12, 1, targetDefinition);
            EnemyHandCard = new CardInstance(20, 1, sourceDefinition);
            State.Players[0].Field.Add(Source);
            State.Players[0].Field.Add(Hook);
            State.Players[1].Field.Add(EnemyA);
            State.Players[1].Field.Add(EnemyB);
            State.Players[1].Hand.Add(EnemyHandCard);
            _root = State.Events.Append("CARD_PLAYED");

            Runtime = new EffectRuntime(State);
            Dispatcher = EffectDispatcher.CreateDefault(Runtime);
        }

        private readonly DominionWars.Engine.Events.GameEvent _root;

        public GameState State { get; }
        public EffectRuntime Runtime { get; }
        public EffectDispatcher Dispatcher { get; }
        public CardDefinition HookDefinition { get; }
        public CardInstance Source { get; }
        public CardInstance Hook { get; }
        public CardInstance EnemyA { get; }
        public CardInstance EnemyB { get; }
        public CardInstance EnemyHandCard { get; }

        public EffectContext Context(long? selectedTargetId = null)
            => new EffectContext(0, _root.EventId, sourceCard: Source, selectedTargetId: selectedTargetId);
    }
}
}
