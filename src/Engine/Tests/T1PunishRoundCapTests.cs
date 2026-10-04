using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Events;
using DominionWars.Engine.Model;
using DominionWars.Engine.Rules;
using DominionWars.Engine.Turns;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// T1「每轮惩罚响应上限」(<see cref="MatchRules.MaxPunishResponsesPerRound"/>) 的
/// 开关语义。owner **尚未批准**该规则，因此出厂默认必须是 0 = 不限制：
///
/// <list type="bullet">
/// <item>默认关闭时，一轮里该结算多少个响应就结算多少个 —— 本文件把"今天是多少"
/// 钉成常量（<see cref="MultiResponseRoundResponsesToday"/>），默认一旦漂移就红。</item>
/// <item>打开（上限 1）时，同一个根动作事件内只接受一次响应，其余可响应牌被**抑制**
/// 并发出与其它空发效果相同的 <c>EFFECT_SKIPPED</c>（reasonKey
/// <c>rule.punish_response_limit</c>），而不是被静默忽略、也不是"玩家放弃"。</item>
/// <item>"一轮"的锚点是**根动作事件**，不是一次抽牌批、也不是一整回合：同一回合内的
/// 两个根事件各自拥有独立额度（<see cref="CapIsPerRootEventNotPerTurn"/>）。</item>
/// <item>上限不得改动其它语义：初始惩罚抽牌、<c>chainLimit</c> 边界、标签消耗、
/// 胜负判定都另有独立用例对照。</item>
/// </list>
/// </summary>
[TestFixture]
public sealed class T1PunishRoundCapTests
{
    /// <summary>
    /// 出厂默认（不限制）下，多响应夹具**今天**结算的响应次数。这个常量就是
    /// "默认真的没改行为"的钉子：夹具或默认值一漂移，用例立刻变红。
    /// </summary>
    private const int MultiResponseRoundResponsesToday = 3;

    /// <summary>抑制事件的 reasonKey，逐字钉住线上格式（回放/计数器按它过滤）。</summary>
    private const string SuppressionReasonKey = "rule.punish_response_limit";

    /// <summary>抑制事件的动作名（沿用 <c>EFFECT_SKIPPED</c> 的既有形状）。</summary>
    private const string SuppressionAction = "PUNISH_RESPONSE";

    // =====================================================================
    // 1. 默认关闭 = 维持今天的次数
    // =====================================================================

    [Test]
    public void ShippedDefaultsKeepEveryResponseOfAMultiResponseRound()
    {
        var state = CreateMultiResponseRound(new MatchRules(), out _, out _, out var policy, out var responses);

        Assert.Multiple(() =>
        {
            Assert.That(
                CountTriggered(state),
                Is.EqualTo(MultiResponseRoundResponsesToday),
                "出厂默认 0 = 不限制：同一个抽牌批里的三张可响应牌必须全部结算");
            Assert.That(policy.CallCount, Is.EqualTo(MultiResponseRoundResponsesToday));
            Assert.That(CountSuppressed(state), Is.Zero, "开关关闭时不允许出现任何抑制事件");
            Assert.That(state.GetPlayer(0).Life, Is.EqualTo(20 - MultiResponseRoundResponsesToday));
            Assert.That(state.GetPlayer(1).Graveyard.Count, Is.EqualTo(MultiResponseRoundResponsesToday));
            Assert.That(state.GetPlayer(1).Hand, Is.Empty);
            Assert.That(responses, Has.Count.EqualTo(MultiResponseRoundResponsesToday));
        });
    }

    // =====================================================================
    // 2. 打开、上限 1 = 只接受一次响应，其余被抑制且可观测
    // =====================================================================

    [Test]
    public void CapOfOneTakesExactlyOneResponseAndSuppressesTheRest()
    {
        var state = CreateMultiResponseRound(
            new MatchRules(maxPunishResponsesPerRound: 1),
            out _,
            out _,
            out var policy,
            out var responses);

        var triggered = Triggered(state);
        var suppressed = Suppressed(state);

        Assert.Multiple(() =>
        {
            Assert.That(triggered, Has.Count.EqualTo(1), "上限 1：同一个根事件内只接受一次响应");
            Assert.That(
                policy.CallCount,
                Is.EqualTo(1),
                "上限是「不再询问」，而不是「问过之后拒绝」：策略不该被叫到第二次");
            Assert.That(suppressed, Has.Count.EqualTo(MultiResponseRoundResponsesToday - 1));
            Assert.That(
                suppressed.Select(item => (string?)item.Data["reasonKey"]),
                Is.All.EqualTo(SuppressionReasonKey));
            Assert.That(
                suppressed.Select(item => (string?)item.Data["action"]),
                Is.All.EqualTo(SuppressionAction));
        });

        var acceptedId = (long)triggered[0].Data["source"]!;
        var suppressedIds = suppressed.Select(item => (long)item.Data["target"]!).ToArray();
        var expectedSuppressed = responses
            .Where(card => card.InstanceId != acceptedId)
            .Select(card => card.InstanceId)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                suppressedIds,
                Is.EquivalentTo(expectedSuppressed),
                "被抑制的必须正好是这一轮里没被接受的那些已抽到牌");
            Assert.That(
                suppressed.Select(item => item.ParentEventId).Distinct().Count(),
                Is.EqualTo(1),
                "抑制事件全部挂在同一个根事件下");
            Assert.That(triggered[0].ParentEventId, Is.EqualTo(suppressed[0].ParentEventId));
            Assert.That(state.GetPlayer(0).Life, Is.EqualTo(19), "只有被接受的那次响应结算了伤害");
            Assert.That(state.GetPlayer(1).Graveyard, Has.Count.EqualTo(1));
            Assert.That(
                state.GetPlayer(1).Hand,
                Has.Count.EqualTo(MultiResponseRoundResponsesToday - 1),
                "被抑制的牌留在手里，没有被消费");
        });
    }

    // =====================================================================
    // 3. 额度按根事件计，不按回合计
    // =====================================================================

    [Test]
    public void CapIsPerRootEventNotPerTurn()
    {
        var state = CreateActionPhaseMatch(
            new MatchRules(maxPunishResponsesPerRound: 1),
            out _,
            out var router,
            out var policy);

        // 两次独立出牌 = 两个根事件；每个根事件各抽 3 张可响应牌。
        var originOne = AddOrigin(state, id: 900);
        var originTwo = AddOrigin(state, id: 901);
        for (var id = 910; id < 916; id++)
        {
            state.GetPlayer(1).Deck.Add(ResponseCard(id));
        }

        Assert.That(PlayRoot(state, router, originOne).Accepted, Is.True);
        var phaseAfterFirstPlay = state.Turn.PhaseId;
        var playerAfterFirstPlay = state.CurrentPlayerIndex;
        Assert.That(PlayRoot(state, router, originTwo).Accepted, Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(phaseAfterFirstPlay, Is.EqualTo(TurnPhase.Action), "夹具必须留在同一个回合里");
            Assert.That(playerAfterFirstPlay, Is.Zero);
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
            Assert.That(state.CurrentPlayerIndex, Is.Zero);
            Assert.That(
                CountTriggered(state),
                Is.EqualTo(2),
                "同一回合的两个根事件各有 1 次额度，合计 2 次");
            Assert.That(CountSuppressed(state), Is.EqualTo(4), "每轮各自抑制 2 张");
            Assert.That(policy.CallCount, Is.EqualTo(2));
            Assert.That(
                Triggered(state).Select(item => item.ParentEventId).Distinct().Count(),
                Is.EqualTo(2),
                "两次响应必须属于两个不同的根事件");
        });
    }

    // =====================================================================
    // 4. 初始惩罚抽牌不受影响
    // =====================================================================

    [Test]
    public void CapDoesNotChangeTheInitialPunishDraw()
    {
        var off = CreateMultiResponseRound(new MatchRules(), out _, out _, out _, out var offResponses);
        var on = CreateMultiResponseRound(
            new MatchRules(maxPunishResponsesPerRound: 1),
            out _,
            out _,
            out _,
            out var onResponses);

        var offDraw = SinglePunishDraw(off);
        var onDraw = SinglePunishDraw(on);

        Assert.Multiple(() =>
        {
            Assert.That(offDraw.Data["count"], Is.EqualTo(MultiResponseRoundResponsesToday));
            Assert.That(onDraw.Data["count"], Is.EqualTo(MultiResponseRoundResponsesToday));
            Assert.That(
                on.GetPlayer(1).PunishDrawnThisTurn,
                Is.EqualTo(off.GetPlayer(1).PunishDrawnThisTurn),
                "本回合被惩罚抽到的张数在开关两侧必须相同");
            Assert.That(
                on.GetPlayer(1).PunishDrawnThisTurn,
                Is.EqualTo(MultiResponseRoundResponsesToday));
            Assert.That(
                on.GetPlayer(1).Hand.Count + on.GetPlayer(1).Graveyard.Count,
                Is.EqualTo(MultiResponseRoundResponsesToday),
                "三张都抽到了；开关只决定是否接受响应，不决定是否抽牌");
            Assert.That(offResponses, Has.Count.EqualTo(onResponses.Count));
        });
    }

    // =====================================================================
    // 5. chainLimit 边界不受影响
    // =====================================================================

    [Test]
    public void CapDoesNotChangeChainLimitBehaviour()
    {
        // 自我延续的链条：双方卡组全是"惩罚发动时再让对手抽 1 张"的惩罚牌，
        // 链深度每加一层就多一次响应。chainLimit 的语义是 `chainDepth > limit`
        // 才停：limit=1 只放行第 1 层，limit=3 放行第 1..3 层。
        var limitOne = CreateGrowingChain(chainLimit: 1, cap: 0, out _);
        var limitThree = CreateGrowingChain(chainLimit: 3, cap: 0, out _);

        Assert.Multiple(() =>
        {
            Assert.That(CountTriggered(limitOne), Is.EqualTo(1), "chainLimit=1 的边界与今天一致");
            Assert.That(CountTriggered(limitThree), Is.EqualTo(3), "chainLimit=3 的边界与今天一致");
            Assert.That(
                CountTriggered(limitThree),
                Is.GreaterThan(CountTriggered(limitOne)),
                "夹具必须真的会加深链条，否则本用例证明不了边界");
            Assert.That(CountSuppressed(limitOne), Is.Zero);
            Assert.That(CountSuppressed(limitThree), Is.Zero);
        });

        var cappedOne = CreateGrowingChain(chainLimit: 1, cap: 1, out _);
        var cappedThree = CreateGrowingChain(chainLimit: 3, cap: 1, out var cappedThreePolicy);

        Assert.Multiple(() =>
        {
            Assert.That(CountTriggered(cappedOne), Is.EqualTo(1));
            Assert.That(CountTriggered(cappedThree), Is.EqualTo(1));
            Assert.That(
                cappedThreePolicy.CallCount,
                Is.EqualTo(1),
                "上限盖过更宽的 chainLimit：第 2 层起不再询问");
            Assert.That(
                CountTriggered(cappedThree),
                Is.LessThanOrEqualTo(CountTriggered(limitThree)),
                "上限只能收紧，不能放宽 chainLimit 已经允许的次数");
        });
    }

    // =====================================================================
    // 6. 标签消耗不受影响
    // =====================================================================

    [Test]
    public void CapDoesNotChangeTagConsumption()
    {
        var offCards = new[]
        {
            ResponseCard(930, tag: "t1_tag_a"),
            ResponseCard(931, tag: "t1_tag_b"),
            ResponseCard(932, tag: "t1_tag_c"),
        };
        var off = CreateMultiResponseRound(new MatchRules(), out _, out _, out _, out _, offCards);
        Assert.That(
            off.GetPlayer(1).UsedTags,
            Is.EquivalentTo(new[] { "t1_tag_a", "t1_tag_b", "t1_tag_c" }),
            "默认关闭时三张响应的标签都要被消耗（今天的语义）");

        var onCards = new[]
        {
            ResponseCard(940, tag: "t1_tag_a"),
            ResponseCard(941, tag: "t1_tag_b"),
            ResponseCard(942, tag: "t1_tag_c"),
        };
        var on = CreateMultiResponseRound(
            new MatchRules(maxPunishResponsesPerRound: 1),
            out _,
            out _,
            out _,
            out _,
            onCards);

        var acceptedId = (long)Triggered(on)[0].Data["source"]!;
        var acceptedTag = onCards.Single(card => card.InstanceId == acceptedId).Definition.Tags.Single();
        var suppressedTags = onCards
            .Where(card => card.InstanceId != acceptedId)
            .SelectMany(card => card.Definition.Tags)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                on.GetPlayer(1).UsedTags,
                Is.EquivalentTo(new[] { acceptedTag }),
                "上限只抑制未结算的响应；被接受的那次照样消耗标签");
            Assert.That(suppressedTags, Has.Length.EqualTo(2), "夹具必须真的抑制了两张带标签的响应");
            Assert.That(
                on.GetPlayer(1).UsedTags.Intersect(suppressedTags),
                Is.Empty,
                "被抑制的响应没有结算，因此不得消耗标签");
        });
    }

    // =====================================================================
    // 7. 胜负判定不受影响
    // =====================================================================

    [Test]
    public void CapDoesNotChangeWinnerChecks()
    {
        // WHAT THIS TEST USED TO ASSERT, AND WHY IT CHANGED (2026-09-12).
        //
        // It built a "lethal" round whose punish responses deal 20 damage to the ENEMY PLAYER,
        // then asserted the round produced a winner with reason `win.enemy_life_zero`. That win
        // condition has been DELETED from the engine: docs/RULES.md §1 lists exactly two
        // victory paths (a leader's declared winCondition, and the deck-cycle counter) and §7
        // states the player life pool constitutes neither a win nor a loss. So the round can no
        // longer produce a winner, and asserting that it does would re-assert a rule the game
        // does not have.
        //
        // The cap's own behaviour is still worth pinning, and it is unaffected by the removal:
        // the cap decides how many punish responses are ACCEPTED and TRIGGERED, and it must not
        // influence the winner check either way. Both halves are asserted below, on both sides
        // of the switch.
        var offWinner = CreateLethalResponseRound(
            new MatchRules(),
            out var offReason,
            out var offTriggered);
        var onWinner = CreateLethalResponseRound(
            new MatchRules(maxPunishResponsesPerRound: 1),
            out var onReason,
            out var onTriggered);

        Assert.Multiple(() =>
        {
            // The punish is 3, so an uncapped round offers exactly 3 candidate responses; the
            // capped round admits 1. The winner check must behave identically on both sides.
            Assert.That(offTriggered, Is.EqualTo(3), "上限关闭时三次候选响应都应触发");
            Assert.That(onTriggered, Is.EqualTo(1), "上限开启时只应接受一次响应");
            Assert.That(
                offWinner,
                Is.Null,
                "把玩家生命打到 0 不再判定胜负（docs/RULES.md §1/§7）");
            Assert.That(onWinner, Is.EqualTo(offWinner), "开关两侧的胜者必须一致");
            Assert.That(offReason, Is.EqualTo(onReason));
            Assert.That(
                offReason,
                Is.Not.EqualTo("win.enemy_life_zero"),
                "该胜利原因已从引擎删除，不得再出现");
        });
    }

    // =====================================================================
    // 8/9. MatchRules 守卫与 balance.json 一致性
    // =====================================================================

    [Test]
    public void MatchRulesRejectsANegativePunishResponseCap()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new MatchRules(maxPunishResponsesPerRound: -1));
            Assert.That(
                new MatchRules().MaxPunishResponsesPerRound,
                Is.Zero,
                "出厂默认必须是 0 = 不限制");
            Assert.That(
                new MatchRules(8, 2, 1, 0).MaxPunishResponsesPerRound,
                Is.Zero,
                "既有的位置参数调用形状必须仍然有效，并落在默认关闭上");
        });
    }

    /// <summary>
    /// 2026-09-11：owner 已把这条规则**打开**（发行值由 0 改为 1），所以本用例从
    /// "出厂默认是关闭"改成"发行文件说 1、而**代码回退**仍是 0"。
    /// 回退保持 0 很重要：balance.json 缺失或损坏时不能悄悄把规则改严。
    /// </summary>
    [Test]
    public void ShippedCapIsOnWhileTheBuiltInFallbackStaysUnlimited()
    {
        var rules = new MatchRules();
        var balancePath = FindBalanceJson();
        if (balancePath is null)
        {
            Assert.Ignore("data/balance.json not found from the test working directory; built-in default asserted only.");
        }

        var json = JObject.Parse(File.ReadAllText(balancePath));
        Assert.Multiple(() =>
        {
            Assert.That(
                json["maxPunishResponsesPerRound"],
                Is.Not.Null,
                "data/balance.json 必须带上这个键");
            Assert.That(
                (int)json["maxPunishResponsesPerRound"]!,
                Is.EqualTo(1),
                "owner decision 2026-09-11：一轮 1 张响应上限已启用");
            Assert.That(
                rules.MaxPunishResponsesPerRound,
                Is.Zero,
                "代码级回退仍是 0 = 不限制：读盘失败不得静默改规则");
        });
    }

    // =====================================================================
    // fixtures
    // =====================================================================

    /// <summary>
    /// 一个根事件 = 一次惩罚值 3 的出牌；对手一次抽 3 张，三张都是可发动的惩罚牌，
    /// 因此**同一个抽牌批、同一个 chainDepth** 上有三次候选响应 —— 这正是
    /// chainDepth 单独数不出来的形状。
    /// </summary>
    private static GameState CreateMultiResponseRound(
        MatchRules rules,
        out TurnFlow flow,
        out TurnActionRouter router,
        out CountingPunishPolicy policy,
        out IReadOnlyList<CardInstance> responses,
        IReadOnlyList<CardInstance>? responseCards = null,
        bool fieldLeaders = false)
    {
        var state = CreateActionPhaseMatch(rules, out flow, out router, out policy);
        if (fieldLeaders)
        {
            FieldLeader(state, 1200, 0);
            FieldLeader(state, 1201, 1);
        }

        responses = responseCards ?? new[]
        {
            ResponseCard(910),
            ResponseCard(911),
            ResponseCard(912),
        };
        foreach (var card in responses)
        {
            state.GetPlayer(1).Deck.Add(card);
        }

        var origin = AddOrigin(state, id: 900);
        var result = PlayRoot(state, router, origin);
        Assert.That(result.Accepted, Is.True, result.ReasonKey);
        return state;
    }

    /// <summary>
    /// 自我延续的链条夹具：双方卡组全是"惩罚发动时再让对手抽 1 张"的惩罚牌，
    /// 因此响应会一层层交替抽牌，直到撞上 chainLimit 或上限。
    /// </summary>
    private static GameState CreateGrowingChain(int chainLimit, int cap, out CountingPunishPolicy policy)
    {
        var state = new GameState(
            new PlayerState(0, 20),
            new PlayerState(1, 20),
            rules: new MatchRules(maxPunishResponsesPerRound: cap));
        var flow = TurnFlow.CreateDefault();
        policy = new CountingPunishPolicy();
        var router = new TurnActionRouter(flow, new ITurnActionHandler[]
        {
            new PlayCardActionHandler(null, policy, chainLimit),
        });

        flow.Advance(state, 0);
        Assert.That(
            router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted,
            Is.True);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));

        for (var index = 0; index < 8; index++)
        {
            state.GetPlayer(0).Deck.Add(ResponseCard(1000 + index, punishCost: 1, damage: 0, owner: 0));
            state.GetPlayer(1).Deck.Add(ResponseCard(2000 + index, punishCost: 1, damage: 0, owner: 1));
        }

        var origin = AddOrigin(state, id: 950, punish: 1);
        var result = PlayRoot(state, router, origin);
        Assert.That(result.Accepted, Is.True, result.ReasonKey);
        return state;
    }

    /// <summary>
    /// 唯一一次响应就足以判定胜负的夹具（双方统领都在场，故不会被
    /// DEFEAT_PREVENTED 拦下）。
    /// </summary>
    private static int? CreateLethalResponseRound(MatchRules rules, out string? winReason, out int triggered)
    {
        var state = CreateMultiResponseRound(
            rules,
            out _,
            out _,
            out _,
            out _,
            new[]
            {
                ResponseCard(1210, damage: 20),
                ResponseCard(1211, damage: 20),
                ResponseCard(1212, damage: 20),
            },
            fieldLeaders: true);

        winReason = state.WinReason;
        triggered = CountTriggered(state);
        Assert.That(state.GetPlayer(0).Life, Is.Zero);
        return state.WinnerPlayerIndex;
    }

    private static GameState CreateActionPhaseMatch(
        MatchRules rules,
        out TurnFlow flow,
        out TurnActionRouter router,
        out CountingPunishPolicy policy)
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20), rules: rules);
        flow = TurnFlow.CreateDefault();
        policy = new CountingPunishPolicy();
        router = TurnActionRouter.CreateDefault(flow, punishResponses: policy);
        flow.Advance(state, 0);
        Assert.That(
            router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted,
            Is.True);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
        return state;
    }

    private static CardInstance AddOrigin(GameState state, long id, int punish = 3)
    {
        var origin = new CardInstance(
            id,
            0,
            new CardDefinition("t1_origin_" + id, "T1 Origin " + id, punish: punish));
        state.GetPlayer(0).Hand.Add(origin);
        return origin;
    }

    private static GameActionResult PlayRoot(GameState state, TurnActionRouter router, CardInstance origin)
        => router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.PlayCard,
            sourceEntityId: origin.InstanceId));

    private static CardInstance ResponseCard(
        long id,
        int punishCost = 0,
        int damage = 1,
        string? tag = null,
        int owner = 1)
    {
        return new CardInstance(id, owner, new CardDefinition(
            "t1_response_" + id,
            "T1 Response " + id,
            punishActivatable: true,
            punishCost: punishCost,
            tags: tag is null ? null : new[] { tag },
            punishCondition: "ALWAYS",
            punishEffects: damage > 0
                ? new[] { new EffectSpec(EffectNames.Damage, "ENEMY_PLAYER", damage) }
                : null));
    }

    private static void FieldLeader(GameState state, long instanceId, int playerIndex)
    {
        state.GetPlayer(playerIndex).Field.Add(new CardInstance(
            instanceId,
            playerIndex,
            new CardDefinition("t1_leader_" + playerIndex, "T1 Leader " + playerIndex, isLeader: true))
        {
            IsLeaderEntity = true,
        });
    }

    private static int CountTriggered(GameState state) => Triggered(state).Count;

    private static List<GameEvent> Triggered(GameState state)
        => state.Events.Items
            .Where(item => string.Equals(item.EventType, "PUNISH_TRIGGERED", StringComparison.Ordinal))
            .ToList();

    private static List<GameEvent> Suppressed(GameState state)
        => state.Events.Items
            .Where(item => string.Equals(item.EventType, "EFFECT_SKIPPED", StringComparison.Ordinal)
                && item.Data.TryGetValue("reasonKey", out var reason)
                && Equals(reason, SuppressionReasonKey))
            .ToList();

    private static int CountSuppressed(GameState state) => Suppressed(state).Count;

    private static GameEvent SinglePunishDraw(GameState state)
        => state.Events.Items.Single(item =>
            string.Equals(item.EventType, "PUNISH_DRAW", StringComparison.Ordinal));

    private static string? FindBalanceJson()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "data", "balance.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed class CountingPunishPolicy : IPunishResponsePolicy
    {
        public int CallCount { get; private set; }

        public PunishResponseDecision Decide(
            GameState state,
            CardInstance card,
            int effectivePunish,
            int chainDepth)
        {
            CallCount++;
            return PunishResponseDecision.Accept();
        }
    }
}
}
