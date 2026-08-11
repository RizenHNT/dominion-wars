# Effects Contract — Dominion Wars

> **Status**: PL 起草（2026-08-11）· **v0.1** · 待 Codex / DeepSeek 复审
> **权威源**：`src/main/java/com/dominionwars/engine/Effects.java`（306 行 switch）
> **配套 schema**：`data/schema/cards.schema.json`（EffectSpec.action 引用本文 §2）
> **配套 spec**：`docs/SPEC.md` §10 适配器接口草案（IEffect / EffectDispatcher）

## 0. 阅读对象

写卡的人 + 写引擎的人 + 写前端的人。任何"新增效果 / 修改结算顺序"动作必须先更新本文。

## 1. 设计原则

1. **效果 = 数据，不是代码**。卡 JSON 里只出现 `action` 字符串 + 参数；禁止在卡里写条件表达式 / 算术
2. **动作名 = 稳定字符串**（UPPER_SNAKE）。一旦进入 SPEC 永久保留，新效果只能加不能改
3. **统一接口**：每个动作对应一个 `IEffect` 实现，结算走 `EffectDispatcher.Apply(spec, ctx)`
4. **副作用显式**：所有副作用必须以事件形式 emit（eventId/parentEventId 因果链），不能隐式改状态
5. **目标优先选语义**：target 字符串表达意图，不在卡里写选择逻辑
6. **结算顺序 = 数组顺序**。卡 JSON 里 effect 数组 = 严格顺序；引擎不重排
7. **失败必须可观测**：目标不存在 / 条件不满足 / 超上限 → 写一个 log 事件，**不算 bug**

## 2. 动作清单（24 个，21 张卡实际引用）

枚举定义：`data/schema/cards.schema.json` → `$defs/EffectAction`
91 卡扫描结果（leaderDef.enterEffects + .punishEffects + onPlayEffects 全集）：
**已用 21 个**：`DAMAGE / HEAL / DRAW / OPP_DRAW / DISCARD_OPP_RANDOM / DISCARD_DRAWN / BUFF / GRANT_KEYWORD / SUMMON / SUMMON_LEADER / END_TURN / ADD_OPP_PUNISH_TURN / ADD_SELF_PUNISH_TURN / CONVERT_PUNISH_TO_DISCARD / PROTECT_TURN / NEGATE_ENEMY_EFFECTS_TURN / SKIP_RESHUFFLE / GAIN_LIFE / LOSE_LIFE / DAMAGE_CASTLE / WIN_GAME`
**预留 3 个**：`DESTROY / NEGATE / RESTORE_ATTACKS`（Effects.java 实现了但当前 91 张卡无引用）

## 3. 单动作合约（24 节）

每节格式：**做什么 / 参数 / target 解释 / 副作用 / 反制规则 / 卡引用**

### 3.1 DAMAGE
- 做什么：对目标造成 N 点伤害
- 参数：`amount` (int 1–99), `target` (见 §4)
- target：`ENEMY_TARGET` 走单目标选择；`ALL_*_MINIONS` 走群体；`ENEMY_FACE` 打脸；`SELF_PLAYER` 自伤；`ENEMY_PLAYER` 伤对方
- 副作用：emit `DAMAGE_DEALT` 事件（target, amount, source, parentEventId）
- 反制：扰魔（WARD）使单目标指定无效，群体忽略扰魔
- 卡引用：flame_strike, machine_rifle等

### 3.2 HEAL
- 做什么：回复生命，士兵到 maxHealth
- 参数：`amount` (int 1–99)
- target：`FRIENDLY_MINION` / `ALL_FRIENDLY_MINIONS` / `SELF_PLAYER`；超 maxHealth 部分丢弃
- 副作用：emit `HEALED` 事件
- 卡引用：wood_priest等

### 3.3 DRAW
- 做什么：自己抽 N 张
- 参数：`amount` (int 1–N)，强制 ≥1
- target：忽略
- 副作用：emit `CARDS_DRAWN` + 后续触发可能造成链式惩罚
- 卡引用：flame_charger等

### 3.4 OPP_DRAW
- 做什么：对方抽 N 张
- 参数：`amount` (int 1–N)
- 副作用：同 DRAW 但作用于对方
- 卡引用：sea_whisper等

### 3.5 DISCARD_OPP_RANDOM
- 做什么：随机弃对方手牌 N 张（对方手牌空则无事发生）
- 参数：`amount` (int 1–N)
- 副作用：emit `CARDS_DISCARDED`（reason = "效果弃牌"）
- 卡引用：machine_thief等

### 3.6 DISCARD_DRAWN
- 做什么：弃掉"刚刚抽到的牌"（深渊吞噬专用）
- 参数：无
- 副作用：依赖 ctx.drawnCards；不依赖 target；reason = "深渊吞噬"
- 卡引用：sea_abyss等

### 3.7 DESTROY
- 做什么：破坏场上目标（无视伤害直接进坟场）
- 参数：`target`
- 副作用：emit `MINION_DESTROYED`（无死亡结算）
- 反制：对方场上 `protectedThisTurn` → 失效（log "受庇护"）；统领 `isLeaderEntity=true` → 免疫
- 卡引用：**暂无引用，预留**

### 3.8 BUFF
- 做什么：加攻/加血（士兵）
- 参数：`amount` (int), `param` ("atk" | "hp" | "both"，缺省 both)
- 副作用：emit `BUFF_APPLIED`
- 卡引用：wood_druid等

### 3.9 GRANT_KEYWORD
- 做什么：给目标加关键字（圣盾/突袭/嘲讽/扰魔）
- 参数：`param` 必须为 Keyword 枚举之一；如 param="圣盾" 自动设 `shield=true`
- 副作用：emit `KEYWORD_GRANTED`
- 卡引用：sea_song等

### 3.10 SUMMON
- 做什么：从卡库召唤 param 指定的卡到己方场上 N 次
- 参数：`param` = StableId（卡库内 id），`amount` = 数量，强制 ≥1
- 失败：卡库无此 id → log "召唤失败" 跳过
- 副作用：emit `MINION_SUMMONED`
- 卡引用：machine_conductor等

### 3.11 SUMMON_LEADER
- 做什么：替换己方统领（旧统领进坟场，新 param 卡成为统领）
- 参数：`param` = StableId（必须为 leader=true 的卡）
- 副作用：emit `LEADER_REPLACED`；旧统领 → graveyard；新统领生命 = leaderDef.grantLife
- 卡引用：machine_relic等

### 3.12 END_TURN
- 做什么：强制当前行动方回合立即结束（烈焰统领）
- 参数：无
- 副作用：emit `TURN_FORCE_ENDED`；engine 进入下一阶段（DISCARD → 对方回合）
- 卡引用：flame_leader等

### 3.13 ADD_OPP_PUNISH_TURN
- 做什么：对方当回合每张卡牌惩罚值 +N（机械统领）
- 参数：`amount` (int)
- 副作用：emit `PUNISH_DELTA_APPLIED`
- 卡引用：machine_leader等

### 3.14 ADD_SELF_PUNISH_TURN
- 做什么：自己当回合每张卡牌惩罚值 ±N
- 参数：`amount` (int，可负)
- 副作用：emit `PUNISH_DELTA_APPLIED`
- 卡引用：暂无，保留扩展

### 3.15 CONVERT_PUNISH_TO_DISCARD
- 做什么：对方当回合惩罚值转为"弃自己手牌"（深海统领）
- 参数：无
- 副作用：emit `PUNISH_FLIP_APPLIED`，标志位 `enemy.punishToSelfDiscardThisTurn = true`
- 卡引用：sea_leader等

### 3.16 PROTECT_TURN
- 做什么：本回合己方卡牌不被破坏 / 反制（古木统领）
- 参数：无
- 副作用：emit `PROTECTION_APPLIED`
- 卡引用：wood_leader等

### 3.17 NEGATE
- 做什么：反制 ctx 中正在结算的卡 / 攻击
- 参数：无（依赖 ctx）
- 副作用：ctx.negated = true → 上层 handle 后置为"结算但无效"
- 卡引用：**暂无引用，预留**

### 3.18 NEGATE_ENEMY_EFFECTS_TURN
- 做什么：对方场上卡牌效果当回合全部无效（命运之影惩罚）
- 参数：无
- 副作用：emit `EFFECTS_NEGATED_TURN`，标志位 `enemy.effectsNegatedThisTurn = true`；重算 auras
- 卡引用：shadow_fate等

### 3.19 SKIP_RESHUFFLE
- 做什么：本回合己方 +N 次"跳过洗牌计数"（机械统领）
- 参数：`amount` 强制 ≥1
- 副作用：emit `RESHUFFLE_CREDIT_GRANTED`
- 卡引用：machine_leader等

### 3.20 RESTORE_ATTACKS
- 做什么：重置目标已用攻击次数 + summonedThisTurn 标记
- 参数：`target`
- 副作用：emit `ATTACKS_RESTORED`
- 卡引用：**暂无引用，预留**

### 3.21 GAIN_LIFE
- 做什么：自己生命 +N
- 参数：`amount` (int)
- 副作用：emit `LIFE_GAINED`；无上限（除非策划另行约束）
- 卡引用：wood_shrine等

### 3.22 LOSE_LIFE
- 做什么：对方生命 -N（无伤害源，常用于烧血）
- 参数：`amount` (int)
- 副作用：emit `LIFE_LOST`
- 卡引用：machine_burn等

### 3.23 DAMAGE_CASTLE
- 做什么：若启用王城，对共享王城造成 N 点伤害
- 参数：`amount` (int)
- 副作用：emit `CASTLE_DAMAGED`；王城规则关闭 → log 落空
- 卡引用：sea_siege等（**MVP 关闭王城**，仅作扩展预留）

### 3.24 WIN_GAME
- 做什么：宣告胜利，param 为胜利名
- 参数：`param` 缺省使用源卡 name
- 副作用：emit `GAME_WON`，进入终局
- 卡引用：condition 路径（leaderDef.winCondition 触发，不通过 EffectSpec）

## 4. Target 选择语义

7 种 target 字符串（`data/schema/cards.schema.json` → `EffectTarget`）：

| target | 含义 | 典型动作 |
|---|---|---|
| `ENEMY_TARGET` | 对方场上 1 个指定目标（player prompt） | DAMAGE, DESTROY, BUFF |
| `ENEMY_MINION` | 对方场上 1 个随从 | DAMAGE, DESTROY |
| `FRIENDLY_MINION` | 己方场上 1 个随从 | HEAL, BUFF, GRANT_KEYWORD |
| `ALL_ENEMY_MINIONS` | 对方场上全部随从 | DAMAGE, BUFF |
| `ALL_FRIENDLY_MINIONS` | 己方场上全部随从 | HEAL, BUFF |
| `ALL_MINIONS` | 双方场上全部随从 | DAMAGE |
| `ENEMY_FACE` | 对方玩家本体（无核心则游戏结束） | DAMAGE |

注：Effects.java 内部还识别 `SELF_PLAYER` / `ENEMY_PLAYER`（仅 DAMAGE/HEAL 用），由 leaderDef 内部使用，**不计入卡的 EffectTarget 枚举**。

## 5. 反制 / 触发窗口（ctx）

Effects.Ctx 字段：
- `playedCard` — 当前正被结算的卡（用于"反制窗口"识别）
- `attacker` — 当前正执行攻击的随从
- `drawnCards` — 当前 trigger 刚抽到的牌（DISCARD_DRAWN 用）
- `negated` — 一旦某个 NEGATE 动作置 true，整个 ctx 关联的效果失效

## 6. 结算顺序（与 game_snapshot 事件流一致）

1. 触发窗口开启（onPlay / onEnter / onPunishDraw）→ 生成 eventId
2. EffectDispatcher 按 spec 顺序遍历 EffectSpec
3. 每个动作通过 `pickTargets` 解析 target → 若 ctx.negated=true 则 break
4. 动作执行 → emit 子事件（parentEventId = 当前根 eventId）
5. 所有动作完成后 → `g.checkAll()`（死亡清理 / 胜负判定 / 阶段推进）

## 7. C# 适配器映射（与 SPEC §10 一致）

```csharp
public interface IEffect {
    string ActionName { get; }          // 稳定字符串，对应 §2 枚举
    void Apply(EffectSpec spec, EffectCtx ctx, IEffectDispatcher d);
}

public sealed class EffectDispatcher {
    private readonly Dictionary<string, IEffect> _effects;
    public void Apply(EffectSpec spec, EffectCtx ctx) {
        if (!_effects.TryGetValue(spec.Action, out var fx))
            throw new UnknownActionException(spec.Action);  // fail-closed
        fx.Apply(spec, ctx, this);
    }
}
```

24 个 IEffect 实现位于 `src/Engine/Effects/{ActionName}Effect.cs`，**禁止把动作逻辑留在 Dispatcher 里**。

## 8. 版本与契约一致性

- `cards.schema.json` 的 `EffectAction` 枚举值必须与本文 §2 完全一致
- `EffectSpec.action` 字段不在 §2 列表内 → 启动失败（SPEC §4.7 contractVersion 校验的子规则）
- 新增动作 = 三处同步更新：本文 §2/§3 + cards.schema.json EffectAction + C# IEffect 实现
- 移除/重命名动作 = 必须经过 ADR 申请（PROPOSAL §5 抗负债硬约束）

## 9. 待办（移交 Codex）

1. ⏳ C# 侧把 Effects.java 24 个动作完整平移为 24 个 IEffect 实现
2. ⏳ 写 EffectDispatcher 单测：spec.action 不在表 → fail-closed
3. ⏳ 写反制窗口单测：DAMAGE + NEGATE 顺序 → DAMAGE 不结算 + emit NEGATED
4. ⏳ SPEC.md §10 适配器签名定稿

---

*PL · 2026-08-11 · v0.1*
