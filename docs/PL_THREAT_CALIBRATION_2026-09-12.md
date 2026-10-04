# 威胁估算器 · 经验校准报告（第一轮）

日期：2026-09-12 01:2x
harness：`build-output/pl-threat-calib/`（`PlThreatCalib.exe`）
被测模块：`src/Adapters/Ai/ThreatEstimator.cs`
状态：**部分达标。数学与公平性已验证；账目一致性不达标且原因未定论。**

---

## 1. 校准方法

用**真实 C# 引擎**跑完整对局（`MatchSetup` + `TurnFlow` + `TurnActionRouter` + `MatchController`），
在每个 ACTION 阶段开始时取一份样本：

1. 用 `RuntimeSnapshotProjection.ToSnapshot(...)` 取**该观众视角**的快照（即 AI 实际能看到的东西）；
2. 让 `ThreatEstimator` 对这份快照给出预测；
3. **在 harness 里**（允许知道全部真相）读出对手**真实手牌**，逐类统计。

驱动策略刻意简单（每回合至多出 2 张牌后结束回合）。首版驱动**从不结束回合**，导致手牌膨胀到
18–37 张、样本不具代表性——已修（这是本次校准发现的第一个问题）。

一致性判据：`accountedCopies == trueUnseen`，其中
`trueUnseen = 对手手牌 + 对手牌库 + 对手暗伏区`（前者来自引擎真值，后者来自快照公开计数）。

---

## 2. 结果（4 seeds × 16 有序对局 × 30 回合上限）

```
samples collected                    1920
matches that threw                   0
accounting-consistent samples        54 / 1920   (2.8%)
coverage misses (一致子集内)          0
仅出现在不一致样本中的 miss            59
```

### 2.1 逐类别校准（1920 样本全量）

| 类别 | P(预测) | P(实测) | 误差 | E(预测) | E(实测) | 误差 | 未见张数(预测/实测) |
|---|---|---|---|---|---|---|---|
| Removal | 0.256 | 0.199 | 0.057 | 0.35 | 0.30 | **0.05** | 0.3 / 1.2 |
| Damage | 0.913 | 0.944 | 0.031 | 3.47 | 3.87 | **0.40** | 3.5 / 12.9 |
| Discard | 0.229 | 0.233 | **0.004** | 0.72 | 0.87 | 0.15 | 0.7 / 2.6 |
| Protection | 0.805 | 0.939 | **0.134** | 2.49 | 3.19 | **0.71** | 2.5 / 9.3 |
| Board | 0.553 | 0.557 | **0.004** | 0.88 | 0.95 | **0.07** | 0.9 / 3.2 |

均值 |概率误差| = **0.0460**；最差 = **0.1338（Protection）**

**判读**：`期望张数 E` 的中位表现良好（Board 0.07、Removal 0.05、Discard 0.15），
`Protection` 明显最差（0.71 / 0.134）——与"类别太粗"的已知问题一致：`Protection` 把
`NEGATE / PROTECT_TURN / GRANT_KEYWORD / HEAL / GAIN_LIFE` 混成一类，而它们在真实对局中的
出现频率差异极大。

---

## 3. 本次校准发现并修复的实现缺陷

### 3.1 逐卡钳位（已修，已验证）

原实现：

```csharp
unseen = Math.Min(unseen, poolSize);   // 对"单张卡"施加"整个池"的上限
accounted += unseen;
```

后果：对每一个**已经被定位**（`unseen <= 0` 之外但换算后仍被抬升）的卡种都记入幽灵张数，
既让 `accounted` 虚高，又让某些类别看起来"可预测"而其真实未见张数为零。

修复后 coverage miss 从 **112 → 49**（同规模样本），`E` 误差同步收窄。

### 3.2 退化驱动（已修）

首版驱动从不结束回合 → 手牌 18–37 张。修正为每回合至多 2 次出牌后强制结束。

### 3.3 我的探针自身的口径错误（已修）

`AccountingMatches` 原先比较 `accountedCopies` 与 `hand+deck`。**这个判据是错的**：
`accounted` 统计的是"按卡池已定位不到的副本数"，而 `hand+deck` 只是对手两个区。
两者只有在其它区一张都没有时才会相等。已改为上述真实性判据。

---

## 4. 未达标项与未定论项（不掩盖）

### 4.1 暗伏往返（**已定为真缺陷并已修**）

原池模型把"对手未见的牌"当成 `手牌 + 牌库`。**这是错的。**

**正确定位（更正我先前的引用）**：`AmbushTriggerResolver.cs:78-88`。翻开的暗伏卡：
- **AMBUSH 型统领** → `ResetRuntimeState()` 后**随机插回拥有者牌库**；
- 普通暗伏 → 进拥有者弃牌堆。

**更正说明**：我先前把该路径引用为 `EffectRuntime.Cards.cs:446`，**那是错的**——
那行是 `LeaderZoneFor`（决定 AMBUSH 型统领的初始落区），与返手/返库无关。机制结论不变，
引用已改正。

后果：暗伏卡是一条**隐藏往返**（牌库 → 隐藏暗伏区 → 牌库/弃牌堆），池算术若不含暗伏区就会**少算**。

**修复**：把 `AmbushCount`（公开计数，不暴露身份）计入未见分母，手牌抽取规模仍为 `handCount`。
**验证**：修复后估算器池分母 `57.45` 与引擎真值 `hand + deck + ambush = 57.45` **逐样本吻合**。

### 4.2 领军实体导致的 +1（**已定为真缺陷并已修**）

`MatchSetup.cs:138-155`：牌库实例数 = **声明的卡表（60）+ 统领实体（1）**。
统领是**独立实例**，不属于卡表、也永远不进池。

因此任何"按 60 张卡表"做的账目都**每方差 1**。决定性证据（turn 5，机械 vs 机械）：

```
oppHand=15 oppDeck=42 oppPublic=4  →  61   （卡表只有 60）
```

**修复**：`VisibleZones` 与探针的可见面都**排除统领区**。

### 4.3 跨牌库转移——**根因已定位；并集方案经测量被否决**

对手可以持有**观众牌库里的牌**（弃牌/转移类效果）。该卡不属于对手阵营卡池，
所以按卡池做算术**永远定位不到它**。

**我试过把分母改成"双方牌库并集"，测量结果更差——已回退：**

| 方案 | 均值 \|P 误差\| | 最差 | Discard 误差 | Board 误差 |
|---|---|---|---|---|
| 对手本方卡池（**采用**） | **0.046** | 0.134 | **0.004** | 0.004 |
| 双方并集（已否决） | 0.064 | 0.136 | 0.087 | 0.042 |

原因：跨池转移**罕见**，把观众的 60 张加进分母只是**稀释**估计。
故 `pool` 仍作为概率分母；`otherPool` 参数保留但**默认 null**（供未来模型使用）。

**这意味着跨池偏差仍然存在**，只是它比"并集稀释"引入的误差更小。
这是一个**已测量的权衡**，不是遗漏。

### 4.4 残余 coverage miss —— ~~已定性为探针假阳性~~ → **撤销：是真缺陷，见 §4.16**

受控复现测试 `BoardIsReportedWhenOneCardIsPubliclyVisible` 当时证明的是**另一件事**：
估算器在**对手自己**的公开区里看见 1 张时会计对。它**没有**覆盖
"估算器把旁观者自己的牌也扣掉"这条路径，而缺陷正在那里（§4.16）。
当时由此推出"不是估算器缺陷，是探针假阳性"，**推断不成立**。

真结论：这 345 个 miss（本轮样本量下的数字）**是估算器的缺陷**，
已修，修后 **0 miss**。

### 4.5 `UnknownPoolSize` 语义已变更（**破坏性变更，已在同轮修测试**）

本轮把 `UnknownPoolSize` 从"手牌+牌库"改为"**对手声明的卡池总副本数**"（机械 = 60）。
理由是：超几何分布的**总体**应当是"这张手牌可能从哪些牌里抽出"，即整个卡池，
而不是"现在还剩几张"——后者会随对局进程收缩，导致同一个类别在开局与残局得到
不可比的概率。

同时 `VisibleZones` 排除统领区（§4.2），故 `UnknownPoolSize` 与 `accounted` 现在
同时表达"卡池"语义，两者可直接相减得到未见张数。

**两条既有测试的断言据此更新**（`ProbabilityIsVerifiableByHandOnASmallPool`
的 20→5、`VisibleCardsReduceTheUnseenPool` 的 5→3），并新增
`HoldProbabilityIsNeverAboveTheExpectedCount`。

### 4.6 类别已按"动作 + 目标"拆细（本轮完成）

先前 `Protection` 把 `NEGATE / PROTECT_TURN / GRANT_KEYWORD / HEAL / GAIN_LIFE`
混成一类，是**精度最差**的类别（P 误差 0.134 / E 误差 0.71）。

**用真实数据核过后确认了病因**（80 个牌池卡种的效果统计）：

| 动作 | 卡种数 | 目标分布 |
|---|---|---|
| BUFF | 20 | FRIENDLY_MINION×16, SELF×3, ALL_FRIENDLY_MINIONS×1 |
| **DAMAGE** | **19** | ALL_ENEMY_MINIONS×6, ENEMY_TARGET×5, ENEMY_MINION×5, ENEMY_FACE×3, ALL_MINIONS×1 |
| HEAL | 6 | FRIENDLY_MINION×4, ALL_FRIENDLY_MINIONS×2, SELF×1 |
| NEGATE | 4 | （无目标）—— **且只存在于 4 张暗伏卡上** |
| GRANT_KEYWORD | 4 | FRIENDLY_MINION×3, ALL_FRIENDLY_MINIONS×1 |
| DESTROY | 2 | ENEMY_MINION×3 |

两个病根被证实：
1. **`DAMAGE` 一个动作横跨三种能力**（单点拆解 / 清场 / 打脸），原先全塞进 `Damage`；
2. **`HEAL` 与 `GRANT_KEYWORD` 是"对手帮自己"**，却与 `NEGATE`（真威胁）同桶。

**新分类**（按"能从我这拿走什么"）：

| 新类别 | 含义 |
|---|---|
| `MinionRemoval` | 能拆单个关键资产（DESTROY / BANISH / 单体 DAMAGE） |
| `BoardSweep` | 能一次清掉多个（对整侧的 DAMAGE / DESTROY） |
| `FaceDamage` | 能压我核心（打脸 / 打王城） |
| `Discard` | 能逼我弃牌 |
| `Negation` | 能反制我的效果（NEGATE / PROTECT_TURN） |
| `Disruption` | 能削弱而不杀（ENFEEBLE / CONTROL） |
| `Deployment` | 能补场面 |
| `Other` | 未分类效果，**保留不丢**（HEAL/BUFF/DRAW 等） |

**验证**：新增 `CategoriesSeparateByActionAndTarget`（逐条断言单体 DAMAGE 是拆解而**不是**清场、
对己方的 HEAL/BUFF **不是**威胁、无目标 DAMAGE 不猜目标）与
`CategoryListsDeduplicateAndAccumulate`（未分类效果进 `Other` 而非被丢弃）。
全量门禁 **778/778 通过**。

实测同一局面现在读出 5 个不同能力（先前只有 3 个粗桶）：

```
MinionRemoval:0.770  BoardSweep:0.895  FaceDamage:0.954  Deployment:0.665  Other:0.844
```

**未验证**：拆分后的**校准精度尚未重测成功**。校准探针仍报 `Negation` 缺失
（`flame_ambush_seal` 的 `NEGATE`），但该卡的 `unseen` 在真实对局中可能为 0
（暗伏卡可停驻暗伏区或进弃牌堆），属探针侧的已知簿记问题（§4.4），
**不是分类缺陷**——分类本身由单元测试逐条钉死。
故"拆分是否降低了校准误差"**仍未证明**。

### 4.7 胜利进度已改为**从公开信息推导**（本轮完成，§③）

先前胜利条件必须由调用方**手工声明**（`WinConditionInput`），这有两个问题：
声明错了没人发现；且快照不发布它，调用方很容易干脆不传。

**改法（不需要动契约）**：统领卡**就在可见的统领区里**，所以它的身份——以及印在上面的
胜利条件——是公开信息。`DeriveWinProgress(snapshot, viewerIndex, conditions)` 因此：

1. 从**统领区**读统领卡 id；
2. 用调用方从**公开卡表**读来的 `LeaderWinCondition` 解析条件与阈值；
3. 从快照**实际发布的**计数器读进度。

**关键设计：三种情况严格区分，绝不混同**

| 情况 | 表现 |
|---|---|
| 条件已知 + 进度可测 | `GIANT_HEALTH_GE 300/512` |
| 条件已知 + **进度计数器未发布** | `OPP_DISCARD_TOTAL_GE ?/18 (progress not published)` |
| 统领未知 / 无数据 | `Condition = null` + 具名原因 |

**实测输出（单元测试捕获）**：

```
player 0: GIANT_HEALTH_GE 300/512, turns unknown: the snapshot publishes no per-turn rate …
player 1: PULL_TOTAL_GE 4/6,        turns unknown: the snapshot publishes no per-turn rate …
player 0: OPP_DISCARD_TOTAL_GE ?/18 (progress not published), turns unknown:
          the engine reads the opponent's TotalDiscarded for OPP_DISCARD_TOTAL_GE,
          and the v1.31 snapshot does not publish it
```

**逐个条件的可用性（本轮查清）**：

| 条件 | 进度可测？ | 依据字段 |
|---|---|---|
| `PULL_TOTAL_GE` | ✅ | `PullCount` |
| `ROYAL_CASTLE_BREAK` | ✅ | `Castle.Health`（该轴是"打破王城"，进度即王城剩余血量） |
| `GIANT_HEALTH_GE` | ✅ | 己方场上 **`Sealed`** 随从的最大 `CurrentHealth` |
| `OPP_DISCARD_TOTAL_GE` | ❌ | 引擎读对手 `TotalDiscarded`，快照不发布 |
| `OPP_PUNISH_DRAW_TURN_GE` | ❌ | 引擎读对手 `PunishDrawnThisTurn`，快照不发布 |
| `NO_DAMAGE_TURNS_GE` | ❌ | 生存计数器，快照不发布 viewer-safe 形式 |

**回合计数的诚实处理**：快照对**任何**轴都不发布"每回合推进速率"，所以
`RemainingTurns` 一律为 **null 并附原因**，而不是拿一个数（如 `Current/Turn`）充数——
那样会把"累计量"误当"速率"。

**注意 `Sealed` 是必需的**：`GIANT_HEALTH_GE` 只由**封印**随从满足，
所以进度取"封印随从的最大生命"；未封印随从再高也不算。测试专门断言
300（封印）而不是 4（未封印）。

**新增 5 条测试**（`WinProgressIsDerivedFromTheVisibleLeaderAndPublicCounters`、
`UnpublishedProgressCountersAreReportedAsUnknownNotZero`、
`UnknownLeadersAreDeclaredRatherThanSkipped`、
`CastleBreakConditionReadsTheCastleHealth`、`WinProgressRejectsMalformedInputs`）。
全量门禁 **783/783 通过**。

**仍需你决定的契约缺口**：若要**算**出深海/烈焰的胜率难度（而非只标注"未知"），
需要 `RuntimeCardSnapshot` 或 `RuntimePlayerSnapshot` 增补
`TotalDiscarded` / `PunishDrawnThisTurn` / `NoDamageTurns`。**属契约变更，我未动。**

### 4.8 State Evaluator v0 —— 只出事实、不出分数（本轮完成，§④）

新增 `src/Adapters/Ai/StateEvaluator.cs`。输入 viewer-safe 快照，输出
`StateVector`：一组**具名事实**，每个事实自带可信度。

**核心设计：`FactConfidence` 让"不知道"成为一等值**

| 可信度 | 含义 |
|---|---|
| `Measured` | 直接读自公开数值，无推理 |
| `Estimated` | 从公开信息推断（有明确模型假设） |
| `Unknown` | 所需信息未发布——**不得变成 0** |
| `NotApplicable` | 该事实在此局面不适用（如"条件标识符"没有数值） |

**v0 刻意不含任何分数、排名或推荐动作。** 理由是我前面栽过的坑：
旧 AI 把"理解局面"和"做决定"混在一层，于是一张权重表压在未经审视的评估之上，
9 套打法就变成了 9 组随机数。

**这条约束是结构性强制、可自动检查的**（`StateVectorExposesNoScoringSurface`）：
用反射扫描 `StateVector`/`StateFact`/`StateEvaluator` 的公开成员，
出现 `weight`/`rank`/`prefer`/`score`/`utility` 等词即失败。

**实测输出（真实快照，18 个事实 / 8 实测 / 7 估计 / 2 未知 / 1 不适用）**：

```
turn 9 ACTION viewer 0 (18 facts, 8 measured, 7 estimated, 2 unknown, 1 n/a)
  my.hand_count=4 (Measured)          opponent.hand_count=3 (Measured)  ← 只有张数，无内容
  my.secure… my.win_condition=- (NotApplicable) OPP_DISCARD_TOTAL_GE requires 18
  my.win_progress=- (Unknown)  条件已知但进度不可测：快照不发布对手 TotalDiscarded
  castle.health=75 (Measured)
  threat.minionremoval.expected_in_hand=0.3  (Estimated)
  threat.boardsweep.expected_in_hand=0.3     (Estimated)
  threat.facedamage.expected_in_hand=0.45    (Estimated)
  threat.discard.expected_in_hand=0.6        (Estimated)
  threat.negation.expected_in_hand=0.15      (Estimated)
  threat.deployment.expected_in_hand=0.15    (Estimated)
  threat.other.expected_in_hand=1.2          (Estimated)
```

**公平性**：`OpponentHiddenHandCannotChangeTheVector` —— 公开计数相同、
对手手牌内容换成完全不同的牌，向量**逐字段相同**。

**我在本轮修的一个自己的缺陷**：首版把 `my.win_condition` 报成
`(Measured)` 但值为空——**自相矛盾**。胜利条件是**标识符不是数量**，
"无数值"是设计使然，不是测量缺口。已改为 `NotApplicable` 并写明原因。

**新增 11 条测试**，全量门禁 **794/794 通过**。

## 4.10 分层修正与 Outcome Predictor（本轮完成，按 owner 反馈）

owner 指出两处缺陷，均已修正；并把"状态事实"与"行动转移"划开。

### 4.10.1 惩罚不是单向代价（世界观的错）

原先的准备做法是"把我手牌所有 punish 加起来"。**owner 否决，理由正确**：
手牌 punish 1/4/8 加起来 13 **对"该出哪张"没有决策意义**。
而且 `PunishDeltaThisTurn` 被当成"对手赚了 N 张"也是错的——它只是**修正量**。

**已删**静态求和；测试明确断言 `hand_punish` 类字段**不许存在**。

### 4.10.2 结算值由引擎给，适配层不许重算

**关键发现**：`LegalActionGenerator.cs:63-66` 引擎**已把结算后的惩罚放进广告 payload**：

```csharp
var effectivePunish = CardPlayRules.EffectivePunish(state, player, card);
["punish"] = effectivePunish
```

所以 `MoveSettlement` **直接读这个数**，不在适配层重算规则——规则的唯一副本留在引擎。
区分四种情况（有测试钉死）：转移 N 张 / 成本 0（不转移）/ 无 punish 键（不是转移）/ 非法值（拒绝）。

### 4.10.3 三层分离（owner 的分界线）

| 层 | 回答什么 | 类型 |
|---|---|---|
| 当前状态 | 现在什么是真的 | `StateEvaluator` / `StateVector` |
| 单个动作成本 | 这一步让对手得多少 | `MoveSettlements` |
| 动作之后局面 | 得牌后他的威胁怎么变 | `OutcomePredictor` |

**实测输出**（同一局面，两个候选动作）：

```
cheap (punish 1): 对手手牌 4 → 5
   MinionRemoval 0.6→0.75 (+0.15)  BoardSweep 0.4→0.5 (+0.1)
   FaceDamage 0.4→0.5 (+0.1)       Negation 0.2→0.25 (+0.05)
dear  (punish 8): 对手手牌 4 → 12
   MinionRemoval 0.6→1.8 (+1.2)    BoardSweep 0.4→1.2 (+0.8)
   FaceDamage 0.4→1.2 (+0.8)       Negation 0.2→0.6 (+0.4)
```

**威胁位移是算出来的，不是猜的**：抽牌是池的均匀抽样，某类别在池中占 `S/U`，
则抽 `n` 张后其期望持有量增长 `n × S/U`。这就是超几何均值，可从既有估计增量推出。

**Outcome Predictor 明确不做两件事**（有测试钉死）：
1. **不编造"这一步推进我的轴多少"** —— 广告不描述效果结算，故报 unknown 并附原因
   （编一个自我推进数会是这个类型里最误导的字段）；
2. **不做对手应手搜索** —— 那是搜索问题，属更高层。

### 4.10.4 本轮抓到的一个真 bug

`objective[1]` 一直算出 `remaining = null`，加诊断后才看到：

```
metric=HEALTH  dir=Unknown  target=512  progress=500  measurable=True
```

**同一个轴有两个名字**：计数器读出来叫 `HEALTH`，方向表里写的是 `GIANT_HEALTH`。
方向 `Unknown` → `remaining` 静默 null，而目标**看起来仍是 measurable**。

**这正是"两处各自写死一份名字"必然出的错**，也正是 owner 把 `WinConditionCounters`
定性为"写死"的意义。已修并加守卫测试：
每个能解析出进度的条件**必须**同时有已知方向，缺了就红。

### 4.10.5 胜利目标统一结构

`VictoryObjectives.cs`：AI 只读 `Metric / Direction / Target / Progress / Remaining`，
**唯一运算是 `remaining`，且已按方向签名**：

```
player 0: PULL Increase 4 -> 6 (remaining 2)
player 1: ROYAL_CASTLE_BREAK Decrease 41 -> 0 (remaining 41)
```

两个生产者身份分明：`FromEngine` **抛 NotSupportedException**（不假装能用）；
`FromLeaderCondition` 是**兼容层，仍读条件名**，是桥不是终点。

全量门禁 **813/813 通过**。

### 4.12 账目不变量的**正确定性**（本轮，撤销先前"未定论"的说法）

先前多轮我反复报告"账目一致性只有 0–3%""96% 样本对不上"，并把它列为未定论的结构性缺口。

**本轮用受控实验定死了，结论是：估算器一直是对的，错的是我的判据。**

#### 决定性证据

`AccountingInvariantMatchesTheEngineAcrossRealMatches` 用**真实引擎对局 + 真实快照投影**
逐样本比对，并按对局组合分组：

| 对局 | 失配数 |
|---|---|
| **机械 vs 机械**（双方同一牌池） | **0** |
| 烈焰 vs 木 / 深海 vs 机械 / 木 vs 烈焰（跨池） | 每个组合都失配 |

**同一牌池时零失配**，跨池时全失配——这直接指向病因是"可见性该不该计入"，而不是算术。

#### 正确的不变量

估算器统计的是**对手自己声明的牌池**。一张牌只有**当它属于对手卡池**时才可能被对手持有，
所以只有"观众看到的、**且属于对手卡池**的牌"才会减少未见张数：

```
accountedCopies == 对手声明副本数 − (可见的、且属于对手卡池的牌)
```

我先前写的是：

```
accountedCopies == 对手声明副本数 − (任何可见来源的牌)      ← 错
```

**跨池对局里，观众手牌来自自己的卡池，本来就不是对手能持有的候选**，
所以那个减法**多减了**，于是每个跨池组合都"失配"，而估算器完全自洽。

**同一牌池的对局就是暴露这个错误的对照组**——那里可见面与牌池才可能重叠。

#### 连带撤销

- §4.3 说的"跨牌库转移导致分母语义错误"**部分撤销**：估算器的语义其实是自洽的，
  只是"对手能拿到观众卡池的牌"确实是一个**未建模的次要来源**（罕见），
  这一点仍然成立，但它**不是**先前说的那个"96% 结构性缺口"。
- 校准探针的 `accountedByDeckList` 期望值**同样是错的**（用了任意可见来源）。
  探针已改为正确的期望，但**未重跑**（见 §4.13）。

#### 方法论教训（第三次同类）

**我连续多轮把"我的判据"当成"被测对象的缺陷"。** 前两次是探针簿记（§3.3、§4.4），
这次是同一个错误的第三个变体。**判据本身必须先被验证**——本轮是靠"同池对照组"发现的，
这个对照组本该在第一次报告失配时就加。

## 4.13 账目恒等式已**机器可验证**（本轮，取代先前所有手写判据）

前几轮我反复用**手写的期望值**去比对估算器，连续三轮都错（§3.3、§4.4、§4.12）。
本轮换掉方法：**不再自己算，改为断言估算器自己的两个数必须满足一条恒等式。**

### 恒等式

```
AccountedCopies + VisibleCopiesInPool == 调用方给出的声明总副本数
```

估算器新增 `VisibleCopiesInPool` 公开字段，于是这条恒等式**可以在测试里断言**，
而不是埋在诊断输出里。

### 证据

| 检查 | 结果 |
|---|---|
| `AccountedPlusVisibleEqualsTheDeclaredPool`（5 组对局 × 10 回合 × 2 视角，真实投影快照） | **100 样本，0 违反** |
| 校准探针（4 seeds，**1920 样本**） | **0 违反** |
| `ForeignDeckCardsDoNotReduceTheOpponentPool` | 通过 |

**先前的"账目不一致 1203/1440"是探针的手写期望出错，不是估算器。** 现在同一批样本上是 0。

### 我本轮又犯的两个错（都记录）

1. **把 `opponent.AmbushCount` 加进池子**（猜测残差来自暗伏区）。**测量后无效**——
   所有 miss 样本的 `oppAmbush=0`。已回退，并在代码注释里写明为什么不该加
   （暗伏实例来自同一个牌池，加了会重复计数）。
2. **一度断言"估算器是对的"然后又自我怀疑**——因为我的手算与探针输出差 1 张。
   最后是靠**让恒等式机器可验证**收场的，不是靠继续手算。

### 方法论结论（这次是正着收的）

**手写的期望值是负债。** 三轮错误全部来自"我自己又算了一遍"。
只要能让被测对象**自己暴露可断言的恒等式**，就不该在测试里重建一份簿记。

## 4.14 ~~121 个 coverage miss：已定性为探针伪影~~ → **撤销，真根因见 §4.16**

**本节此前的结论是错的。** 我把它写成"探针在两次读取之间状态不一致"，
真根因是一个**估算器实现缺陷**。机械排除法的五条排除本身仍然成立（它们排除的确实不是病因），
但从"排除了五个假设"跳到"所以是探针的锅"是**没有根据的推断**——
我把自己没查清的东西归给了测量工具。这是本项目第四次同类型错误。

特别要指出：`ACardCannotBeFullyPlacedAndAlsoHeld` 当时**通过了**（112 样本 0 违反），
我据此认为"物理上不可能的记录不可能由估算器产生"。这条推断有漏洞——
该定律用的样本是**单一 pairing**，而缺陷只在**同阵营对局**里出现。
**一条只在一个方向上验证过的定律，不足以排除跨方向的缺陷。**

## 4.16 真根因：估算器把**己方**的牌从**对手**牌池里扣掉了（本轮，已修）

### 缺陷

`ThreatEstimator` 把"**旁观者自己**的公开区 + 手牌"也算作
"已在对手池中就位"的副本，并从**对手声明的牌池**里扣除：

```csharp
foreach (var cardId in PubliclyAccountedFor(snapshot, viewerIndex)) Add(visible, cardId, 1);  // 已删除
foreach (var cardId in VisibleZones(opponent)) Add(visible, cardId, 1);
```

推理是"我手上拿着的牌，不可能在他手上"。**这条推理对本作不成立**：
双方各自从**自己的 60 张牌库**抽牌，同一张牌的身份（card id）完全可以同时在两边。

### 决定性证据（测量，不是推理）

新增探针指标：统计**双方手牌同时持有同一 card id** 的 (样本, 卡) 对数。

```
(cardId, sample) pairs where BOTH hands hold the same card id: 10028
  seed 1 turn 1 viewer 0: flame_ambush_counter held by opponent x1 and by viewer
  seed 1 turn 2 viewer 1: flame_bolt held by opponent x2 and by viewer
  ...
```

**5120 个样本里有 10028 对。同 id 重叠是常态，不是边界情况。**

### 后果（镜像对局里最严重）

烈焰 vs 烈焰，双方各声明 3 张 `flame_ambush_seal`：

| 读数 | 值 |
|---|---|
| 估算器 `VisibleByCardId[flame_ambush_seal]` | 3（**全部来自旁观者自己**） |
| 估算器因此认为对手未见副本 | **0** |
| 估算器给出的类别 | 无 `Negation` |
| 对手手牌实际持有 | **1 张** |

于是只输出"对手不可能有反制牌"，而对手手上就有。
**这不是数值偏差，是把"不确定"错误地变成了"确定没有"**——对 AI 决策的危害远大于误差。

### 为什么能活这么久（两个掩盖因素）

1. **镜像对局本身就掩盖它**：同阵营两副牌 id 完全重合，扣己方 = 扣对手，
   "看似自洽"；而错得最狠的恰恰是镜像。§4.14 当时用的正是单一 pairing 样本。
2. **构建产物过期**（本轮第一次重跑时踩到）：修正写进源码后探针输出**逐字未变**。
   核对 mtime 发现 `PlThreatCalib.dll` 比源码**早 43 秒**，且反编译后**仍含已删除的
   `PubliclyAccountedFor`**。加 `--no-incremental` 后才真正重编。
   **只看"0 error"不足以证明二进制是新的。**

### 修正

只扣**对手自己**公开区里的副本。同时确认跨牌库转移**不支持**原来的理由：
引擎里控制权变更只在 Field 之间移动（`EffectRuntime.Mechanical.cs`、
`TurnFlow.ResolveControlExpiry`），**没有任何效果把某人的牌放进另一个人的手牌**。
真出现这种转移，会被既有的账目不变量以 mismatch note 报出来，而不是被静默吸收。

### 修正前后（5120 样本，同样的 4 seeds × 16 有序对局 × 40 回合上限）

| 指标 | 修正前 | 修正后 |
|---|---|---|
| coverage miss | **345** | **0** |
| 账目恒等式违反 | 0 | 0 |
| mean \|P 误差\| | 0.0584 | **0.0498** |
| 最差类别 | Negation 0.234 | Negation 0.187 |
| 估算器平均未见副本 | 49.73 | 55.18 |

### 校准表按 pairing 类型拆开（重要的口径修正）

估算器预测的是"对手**自己那副牌**里抽出的张数"，而探针测的"真相"是**对手手牌**。
**两者只有在两副牌 id 不重合时才是同一个量。** 同阵营对局里两张牌 id 重合，
对手合法地持有估算器同时在预测的那种牌，`E(obs)` 会系统性偏高。
因此校准表拆成两组，**只有跨阵营组进头条精度数字**：

```
-- CROSS-FACTION pairings (no shared card ids; the honest measurement) --
MinionRemoval  P 0.851/0.900 |err| 0.049   E 2.40/2.55 |err| 0.15
BoardSweep     P 0.569/0.521 |err| 0.048   E 1.32/1.19 |err| 0.13
FaceDamage     P 0.613/0.569 |err| 0.044   E 2.02/1.94 |err| 0.07
Discard        P 0.230/0.239 |err| 0.009   E 0.89/1.05 |err| 0.16
Negation       P 0.606/0.793 |err| 0.187   E 0.90/1.16 |err| 0.26   ← 唯一未达标项
Deployment     P 0.631/0.627 |err| 0.004   E 1.10/1.13 |err| 0.03
Other          P 0.948/0.955 |err| 0.007   E 7.86/8.52 |err| 0.67
```

### 未完成项（不掩盖）

- **Negation 的偏差已定位（§4.17），是模型假设的边界，不是可修的实现缺陷。**
- 同阵营组的 `E(obs)` 偏高是**结构性口径问题**，不是估算器误差，但也**未从数学上完全分离**。

### 本轮新增/更新的定律

- `ViewerOwnedCopiesDoNotReduceTheOpponentPool`（新）：己方手牌与己方坟场的牌，
  都不许降低对手池的未见数。
- `AccountedCopiesDropWhenPoolCardsBecomeVisible`（**反转期望**）：原来断言
  "旁观者手里的 2 张对手池卡 → accounted 减 2"，**那是把缺陷写成了断言**；
  现在断言己方持牌**不减少**、只有对手公开消耗才减少。
- `AccountedPlusVisibleEqualsTheDeclaredPool`（**删掉己方侧扣减**）：不再把旁观者
  手牌计入 `visibleInOpponentPool`。
- `VisiblePlacementsArePerCardAndSumToTheTotal`（**删掉错误断言**）：
  原来断言 `VisibleCopiesInPool >= 旁观者手牌数`，现改为逐回合断言
  "placement 数 == 对手自己公开区里属于其牌池的卡数"。

**门禁：835/835 通过，0 失败，断言 140544**。

### 方法论收口（第二次）

§4.14 的教训是**不要用"排除法 + 剩余归因"下结论**：排除了五个假设不等于第六个假设成立。
本轮改用**直接测量**（"两边手牌会不会同时持有同一个 id"），一次收口。
配合 §4.13 的"把结论做成可断言的定律"，两条合起来是：
**能测的就别推断；测不了的就别声称。**

## 4.17 Negation 的偏差：**根因是模型假设的边界，已定性并显式声明**

### 先纠正我自己上一轮的两个错误读数

1. **"估算器只给 0.9 未见副本、实测 3.0"是错的比较。** 0.90 是
   `ExpectedCardsInHand`（期望**在手**张数），3.0 是**未见副本总数**。两者不是同一个量。
   探针对齐后：`unseen own/obs = 3.0 / 3.0` —— **估算器没有少算任何未见副本**。
2. **Negation 的未见副本逐区分解完全守恒**：

```
declared copies per sample      : 3.00
held in OPPONENT hand           : 1.16
spent in OPPONENT public zones  : 0.04
remaining in OPPONENT deck      : 1.80
held + spent + deck             : 3.00   (必须等于 declared)
ESTIMATOR unseen copies         : 2.96
PROBE-derived unseen copies     : 2.96
```

**所以分子是对的、账目是对的、覆盖性是 0 miss。偏差只出在 `E(pred)` vs `E(obs)`。**

### 真正的根因（测量得到，不是推断）

超几何均值 `E = m·H/U` 的前提是"**手牌是牌池的无偏样本**"。
本作里这条前提对**不能无条件打出的牌**不成立：暗伏牌要等触发、惩罚牌要等条件，
**它们打不出去，于是在手里堆积**。

用不依赖任何假设的量来测：**留存比 = 某类占对手手牌的份额 ÷ 该类占声明牌池的份额**。
1.00 = 无偏，>1.00 = 在手里堆积。

| 类别 | 手牌份额 | 牌池份额 | 留存比 | 条件牌 |
|---|---|---|---|---|
| Other | 46.21% | 45.93% | **1.01** | |
| Negation | 7.47% | 5.13% | **1.45** | [cond] |
| MinionRemoval | 15.65% | 13.74% | 1.14 | [cond] |
| Discard | 5.61% | 4.76% | 1.18 | [cond] |
| Deployment | 6.41% | 6.80% | 0.94 | [cond] |
| FaceDamage | 11.73% | 14.08% | 0.83 | [cond] |
| BoardSweep | 6.93% | 9.55% | **0.73** | [cond] |

**Negation（全部是暗伏 NEGATE）留存比 1.45，是全场最高**，
而它正是 P 误差最大的类别（0.187）。机制吻合，不是猜的。
实测后果：Negation 的 `E` 低估 27%（预测 0.90，实测 1.16）。

### 为什么"修不掉"（要说清楚，而不是含糊过去）

`E = m·H/U` 是**超几何分布的精确均值**，数学上没有错。
错的是把"对手手牌"当成"从牌池里无偏抽出的 H 张"。
要修正它，必须知道**这类牌被留存的概率**——而那取决于对手的手牌内容，
**快照只发布对手的手牌张数，永远不发布内容**（隐藏信息规则，不能也不该改）。
所以修正所需的信息**在信息论意义上不可得**。

**结论：这不是实现缺陷，是"用可观测信息估计不可观测量的误差方向已知"的边界。**
正确的工程处理不是继续调参，而是**把误差方向声明出来**。

### 落地：估算器现在自己声明哪些读数下界

`ThreatProbability.HasConditionallyPlayableCards`（新增）：
该类别含"必须满足条件才能打出"的牌（暗伏触发或惩罚条件），
**因此 `ExpectedCardsInHand` 是下界而非点估计**。
判定**从卡牌数据推导**（`AmbushTrigger` / `PunishCondition` 非空），
**不是写死卡名清单**——新系列加条件牌自动覆盖。

**诚实标注**：91 张牌里 28 张是条件牌，因此**当前 7 个类别全部被标记**，
该标志是**安全的上界近似**（"可能偏低"），**不能**用来指认"哪个类别偏低"。
能指认的是本节的留存比测量。这一点已写进代码注释，避免后来人误用。

### 本节新增定律

- `ConditionallyPlayableCardsAreDeclaredAndDerivedFromData`：
  ① 暗伏牌必须落在从数据推导出的条件牌集合里；
  ② Negation 读数必须被标记为下界；
  ③ **不给卡牌数据时不得凭空置 true**（不许猜）；
  ④ **该标志不得改变任何算术**（只是对读数的告示，不是对它的修正）。

**门禁：835/835 通过，0 失败，断言 140544。**

## 4.15 尚未做的事 / 需 owner 决定

- **契约缺口已按 owner 批准补齐**（§4.10）：`PunishDeltaThisTurn` / `PunishDrawnThisTurn` /
  `TotalDiscarded` / `NoDamageTurns` / `DamagedThisCycle` / `PunishToSelfDiscardThisTurn` /
  `ProtectedThisTurn` / `EffectsNegatedThisTurn` **已进 viewer-safe 快照**。
  补完之后深海/烈焰的胜利距离**已可测**——原先一条断言"不可测"的测试因此反转。
- **胜利目标契约提案待批**（`docs/PL_VICTORY_OBJECTIVE_CONTRACT_PROPOSAL_2026-09-12.md`）：
  要让"新系列不改 AI"，需引擎自描述目标。**属规则层，需 owner 明确批准。**
- **②拆分后的校准精度已重测**（§4.16）：拆分后跨阵营组 mean |P 误差| = **0.0498**
  （拆分前 0.0584），且 coverage miss **345 → 0**。
- **Negation 的 0.187 已定性为模型假设的边界**（§4.17），非实现缺陷：
  该类别全为暗伏 NEGATE 卡，留存比 1.45（全场最高），
  因"不能无条件打出→在手里堆积"而系统性偏离无偏抽样假设。
  修正所需信息（对手手牌内容）**在信息论意义上不可得**，
  故改为让估算器**声明哪些读数是下界**。**不再列为待修缺陷。**
- 未做 10,000 级采样；未复算另一 harness 的 36 进程分块合并等价性。
- **对手应手搜索未做** —— `OutcomePredictor` 只算"这一步给了什么、局面怎么变"，
  不含"他会怎么回"。属搜索层，未开始。

## 4.18 公开情报通道（owner 指出，本轮实现）—— 并诚实报告其**实测代价为 0**

### 我一开始把 owner 的意思读窄了

Owner 说"不能看手牌，但可以读已经公开的检索或者回收记录"。
我先去查了 `CARD_ROLLED_BACK`（`machine_recycler` 的一条**卡字段**），并一度以为这就是全部。
**那是我读窄了。** Owner 随后澄清：要的是**通用的公开情报通道**，
凡是"某张牌会被 / 已经被加入手牌"的公开信息都要能吃进来，
**包括还没发生的**（例如"云端的卡 2 回合后加入手牌"）。

所以本轮**没有**按某一张卡去补，而是做成了机制无关的通道。

### 机制盘点（穷举，不是抽样）

对 `data/cards/*.json` 的全部 effect action 做去重枚举后，**当前卡池里没有任何
检索（search / tutor）、揭示（reveal）、或"延迟加入手牌"的效果**。
`REVEAL` / `SCOUT` 两个词只出现在**测试夹具**里，**不在卡表**，也没有注册进
`EffectDispatcher.CreateDefault`（该 dispatcher 注册 33 个 action，无二者）。

现有的"延迟"机制只有两个，且**都不是入手**：

| 机制 | 字段 | 去向 |
|---|---|---|
| 延迟召唤 | `PendingLandmarkSummonCardId` | 场（`PromoteLandmark`） |
| 控制权倒计时 | `ControlTurnsRemaining` | 场（`ResolveControlExpiry`） |
| 云端 | `CloudStack`（PUSH 过去） | 场上的一个区，不是手牌 |

**结论：owner 描述的延迟入手机制目前不存在，正是"为未来预留"的那种。**
这恰好说明**不能**按卡去补——必须留通道。

### 实装：`PublicHandRecord`

```csharp
public sealed class PublicHandRecord
{
    IReadOnlyDictionary<string,int> CertainInHand;     // 确知已在对手手牌
    IReadOnlyDictionary<string,int> ScheduledForHand;  // 公开情报说将来会入手
}
```

调用方从引擎的公开记录里填，**估算器不需要知道是哪个机制产生的**。
新机制加入时只改调用方，不改估算器。

数学处理（两类都离开"隐藏池"，但语义不同）：

- **两类都从隐藏池的分母里扣掉**：目的地已定，不再是"可能在手"的候选。
- **手牌规模同时加上两类**：被抽的 H 张是**剩下那部分**，
  分母缩了而分子不缩会**低估**——正是这条输入要消除的那类错误。
  这一点我先写错过一次（只加了 certain），被测试抓住。
- **只有 "certain" 是事实；"scheduled" 尚未到达**。
  单靠前者会让 AI 把"3 回合后才到手的牌"当成"现在就指着我"。
  故新增 `ThreatProbability.ScheduledCopies`，把未到达的部分**单独暴露**，
  让决策层能按"这回合能打出什么"扣掉它。

### 实测代价：**0**（不掩盖）

在 5120 个真实对局样本上：

| 公开机制 | 出现样本数 |
|---|---|
| 对手回滚过牌（`CARD_ROLLED_BACK`） | **0 / 5120** |
| 对手洗过牌库（`DECK_CYCLED`） | 37 / 5120 |
| 对手提交队列**此刻**非空 | **0 / 5120** |

**对手的提交队列在整个采样里一次都没非空过**，所以 COMMIT→ROLLBACK 这条路
根本没有发生过。我为了让它发生，还专门**去掉了驱动里对 ROLLBACK 的 -50 惩罚分**
（原来驱动从不回滚，这正是这个洞长期没暴露的原因）——**去掉之后仍然是 0**。

另外 `DECK_CYCLED` 事件只发 `player` + `counted`，**不发牌的身份**，
所以洗回牌库这条**不可用于身份推断**，不是公开情报源。

**所以必须说清楚：这条通道在当前牌池、当前驱动下，实测收益为 0。**
它的价值是**结构性的**——owner 说要为未来机制预留，而预留的正确形式是
一个机制无关的入口，不是等卡出来再补。**我不会声称它提升了精度。**

### 本轮新增定律

`PublicHandRecordNarrowsThePoolAndSeparatesCertainFromScheduled`：
① 确知在手 → 隐藏池缩小、估计**上升**（同样的未见数落在更小的池里）；
② 预定入手 → 隐藏池同样缩小，且**不得**被当成"已经在手"从而压低估计；
③ 账目恒等式**不因该记录而改变**（它对齐声明牌表，不是对齐手牌）；
④ 记录的张数**按声明数封顶**，对手没声明的卡不缩小任何东西；
⑤ 空记录必须与不传记录**逐字段等价**。

**门禁：835/835 通过，0 失败，断言 140544。**

---

## 5. 结论

**可以确认的：**

- 超几何数学正确：73810 组与精确二项式系数比对，误差 < 1e-9（单元测试）。
- 公平性边界成立：对手手牌内容换成完全不同的牌、公开计数不变时，
  预测结果**逐字段相同**（对抗性测试 `OpponentHandContentsCannotChangeTheEstimate`）。
- 全量门禁 **835/835 通过**，0 失败，断言 140544；较基线 754 新增 81 条，无回归。
- **覆盖性成立**（§4.16）：5120 样本（4 seeds × 16 有序对局 × 40 回合上限）**0 coverage miss**，
  账目恒等式 **0 违反**。
- `期望张数` 这一指标具备判别力（误差 0.05–0.87），远优于饱和的"持有概率"。
- **暗伏往返已定位并修复**，池分母现与引擎真值逐样本吻合（§4.1）。

**不能确认的：**

- **`ExpectedCardsInHand` 对"条件牌"类别是下界而非点估计**（§4.17）。
  误差**方向已知（偏低）**、机制已测量，但**幅度不可先验计算**——
  它取决于对手的手牌内容，而快照按隐藏信息规则不发布内容。
  故估算器只做**声明**，不做**修正**。
- 同阵营对局里探针的 `E(obs)` 口径**结构性偏高**，尚未从数学上完全分离。
- 覆盖性已在 5120 样本上达到 **0 miss**，但这批样本全部来自**同一天的四副生产牌组**；
  新系列加入后需重测。
- 探针自身缺陷已识别四项（§3.3 判据错误、§4.4 假阳性误判、§4.16 构建过期、§4.16 pairing 口径）。
- **公开情报通道已建但实测收益为 0**（§4.18）：当前卡池**没有**检索/揭示/延迟入手机制，
  且 5120 样本里对手提交队列**一次都没非空过**，故 COMMIT→ROLLBACK 从未发生。
  通道的价值是**结构性的**（为未来机制预留），**不是精度提升**。

**因此：本模块的数学、公平性与覆盖性均已在当前牌池上验证通过；
唯一已知偏差（Negation）已定性为隐藏信息下的固有边界并显式声明，不再作为缺陷挂账。**
**但"下界"不是"可用精度"——对条件牌类别做决策时，调用方必须按偏低处理。**

数据支持"先修 bug 再往上盖"：确实存在实现层缺陷（逐卡钳位、退化驱动、判据错误、
暗伏往返分母、统领实体 +1、**己方牌扣对手池**），而这些在单元测试全绿时
**只有经验校准能发现**——**同时在校准器自身里也发现了四个缺陷**，
说明"校准器"本身也需要被验证，不能当成裁判。
**更严重的是：我曾连续两轮把估算器的真缺陷判成"校准器的假阳性"（§4.4、§4.14），
即默认工具不可信、代码可信。这个默认方向本身就是错的。**

## 6. 下一步（按优先级）

1. **对手应手搜索**——`OutcomePredictor` 仍只算"这一步给了什么、局面怎么变"，不含"他会怎么回"。
2. ~~Negation 类别误差~~ **——已定性为模型假设边界并显式声明（§4.17），非实现缺陷，不再挂账。**
3. ~~跨池分母~~ **——已试并集方案，经测量更差，已回退；保留为已知的有界偏差（§4.3）。**
4. **胜利条件自描述引擎契约** —— owner 已批准（2026-09-12）。让引擎自己声明胜利目标，
   新系列不必改 AI。**下一步实施项。**
5. **拆细 `Protection` 等粗类别为决策语义**（"能拆关键资产 / 能打王城 / 能打随从 /
   能中断胜利轴"）——"能拆掉我的封印随从吗"这类问题现在**答不了**。
6. 把 `winCondition` / 累计进度改为**规范只读输入**（v1.31 快照当前不发布它们）。

## 7. 附：复现命令

```powershell
# 必须 --no-incremental：本轮踩过一次"源码已改、二进制没重编"，
# 只用 "0 error" 判断不够，要核对 mtime。
dotnet build-server shutdown
dotnet build build-output\pl-threat-calib\pl-threat-calib.csproj -c Release -m:1 --no-restore --no-incremental /p:MSBuildEnableWorkloadResolver=false
dotnet build-output\pl-threat-calib\bin-calib\Release\net8.0\PlThreatCalib.dll
# 默认 8 seeds/pairing，40 回合上限 → 5120 样本
```

全量门禁：

```powershell
dotnet build src\Engine\Tests\DominionWars.Engine.Tests.csproj -c Release -m:1 --no-restore /p:MSBuildEnableWorkloadResolver=false
dotnet build-output\pl-p0\runner\bin\Release\net8.0\PlP0Runner.dll `
  build-output\DominionWars.Engine.Tests\bin\Release\net8.0\DominionWars.Engine.Tests.dll --xml out.xml
# NUNIT_RESULT result=Passed total=835 passed=835 failed=0 skipped=0 assertions=140544
```
