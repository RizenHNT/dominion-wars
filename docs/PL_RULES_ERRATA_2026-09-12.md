# 规则书勘误提案（RULES.md）

**日期**：2026-09-12
**提议人**：DeepSeek V4 Flash（临时 PL）
**状态**：owner 已指示"把规则书整体修一下"，本轮**已按下列勘误直接修订 `docs/RULES.md`**。
修订前原文保留在 `docs/RULES_PRE_REVISION_2026-09-12.md`；`RULES.md` 顶部已加修订记录。

> ✅ **规则与两套引擎现已对齐。** §3、§5、§6 的代码点已于 2026-09-12 全部完成
> （owner 授权，Codex 限额期间由 PL 执行）。
> **C# 门禁 877/877、断言 140957；Java 回归 59/59。**
> **重跑校准：与改动前一字不差**（ECE 0.0285 / MCE 0.0940 / Brier 0.1167 / 偏差 −0.0273），
> 原因与含义见 §7。

---

## 目录

- §1 勘误表（已执行）
- §2 证据链：为什么"统领被打败"当前不可达
- §3 配合规则改动的 4 个代码点（C#，已执行）
- §4 本提案作者的一次误判（记录在案）
- §5 V3 自弃牌提交：编译错误已修，契约缺口未修
- **§6 Java 与 C# 统一（本轮新增，已执行）**
- **§7 校准重跑：数字不变，以及为什么（本轮新增）**


---

## 0. owner 已作出的裁定（本提案的依据）

> 「15 去掉，16 改，14 基本上和统领被破坏是一个意思，不过目前还没啥统领被打败的方式」
> 「标吧，生命池算是一个提前预想的方案，如果先不用就先不管，只要不影响对局。之后如果有生命估计用得上」

**裁定归纳**：

1. **胜利条件只有两条**：统领记载的胜利条件 + 牌库循环计数到 10。
2. **L115 去掉。**
3. **L16 改**（去掉"会致败的数值钳制"这个前提）。
4. **L114 保留**，但要**标注"当前无可达路径"**。
5. **L262 的"生命池"**：属于提前预想方案，**先不管，只要不影响对局**。

---

## 1. 勘误表（**已执行**）

### 勘误 1 —— `RULES.md:115` ❌ **已删除**

**原文**

> - 赋予生命统领：给予玩家生命值，生命归零则该方败北（非"被击败"，是己方生命资源耗尽）；

**为什么错**

| 理由 | 证据 |
|---|---|
| owner 裁定胜利只有两条（统领记载 + 牌库计数），而这条**是形态自带的败北条件，不是记载出来的** | 它不在 `winCondition` 字段里 |
| 它**假定玩家生命是一个败北资源**，而 owner 的模型是"生命默认不存在，只有统领赋予才有" | — |
| 它在**真实牌池下根本不可达** | 见 §2 证据链 |
| 它连"赋予给谁"都没定 | 引擎 `EffectRuntime.Cards.cs:401-403` 是 `player.Life = ...`（**给统领自己**）；owner 举的例子是"**赋予对方** 40 生命" |

**建议改为**（**已执行**）

> - 赋予生命统领：降临时为**己方**开启**生命池**（`grantLife`），即该玩家开始拥有生命值。**生命池本身不构成胜利或败北条件**：生命池归零只是数值归零，不触发任何胜负判定。若日后需要把生命池作为一条胜利轴启用，必须另行冻结规则并登记（见 §12.5）。

---

### 勘误 2 —— `RULES.md:16` ⚠️ **已改写**

**原文**

> - 除胜利条件明示可以绕过门限外，在双方统领都降临之前，对局不可能分出胜负（门限保护：**任何会致败的数值在此期间最低钳制为 1**）。

**为什么错**：括号里的前提是"**存在会致败的数值**"，即玩家生命。按 owner 的裁定，**这样的数值不存在**，所以这句话在描述一个空的集合，却让实现照它写了钳制代码（`EffectRuntime.cs:79-82` 的 `player.Life = 1`）。

**建议改为**（**已执行**）

> - 除胜利条件明示可以绕过门限外，在双方统领都降临之前，对局不可能分出胜负（**门限保护**）。
> - 门限例外：`ROYAL_CASTLE_BREAK` 可在双方统领尚未全部降临时被击破（见 §9.1）。

（**前半句是对的**：`RULES.md:17` 的 `ROYAL_CASTLE_BREAK` 例外与引擎 `TryDeclareWinner` 的 `ForceLeaderOut` 路径一致，保留。）

---

### 勘误 3 —— `RULES.md:114` ✅ **保留 + 加标注**

**原文**

> - 耐久统领：自身耐久归零则败北（非"被击败"，是其耐久资源耗尽）；

**owner 裁定**：这条和"统领被破坏"是一个意思，**保留**。

**加标注建议**（**已执行** —— 已作为 §7 列表下方的一条【实现状态】标注加入）

> **【实现状态 2026-09-12】** 上列"被击败 / 耐久归零"两条当前**没有可达路径**：牌池中不存在能击杀统领的可靠手段（见 `PL_RULES_ERRATA_2026-09-12.md` §2）。它们是**已设计但尚未开放**的机制，不是"当前可用规则"。
> 阅读本规则书与实现时**不得假定其可达** —— 本提案的作者曾因此构造了一个非法局面并据此误判 AI（见 §4）。

---

### 勘误 4 —— `RULES.md:262` 📌 **加标注（不删）**

**原文**（§12.3 海统领候选：潮汐债务）

> ……**生命池**、降临抽牌、特殊效果和胜利条件中只能选择明确的一项作为削弱对象……

**owner 裁定**：生命池是**提前预想的方案**，先不用就先不管，只要不影响对局；之后如果有生命估计用得上。

**加标注建议**（**已执行** —— 已作为 §12.3 该条下的【实现状态】标注加入）

> **【实现状态 2026-09-12】** 其中"生命池"属于**预先设想、尚未启用**；生命默认不存在，因此该项当前为空。保留为候选，不影响当前对局。

**不删的理由**：owner 明确说"之后如果有生命估计用得上"，而 §12.3 整节本来就是"待决项"，留着不算错。

---

## 2. 证据链：为什么"统领被打败"当前不可达

三条"统领被打败"的路径**全部不可达**。以下逐条给出代码证据。

### 2.1 `leaderDefeated`（随从统领血量归零）

`src/Engine/Effects/EffectRuntime.cs:59`

```
var leaderDefeated = leader is not null && leader.IsMinion && leader.Health <= 0;
```

要触发需 `leader.Health <= 0`。伤害统领的两条路：

- **普攻**：`src/Engine/Turns/AttackTargetPolicy.cs:57` 要求攻击者带 `KingSlayer` 才能打有护卫的统领；全牌池中声明 `kingSlayer: true` 的是 `flame_strike`（`DataLoaderTests` 断言）。**但统领还要在 `vulnerabilities` 白名单里声明对应效果动作**（`CardTargetValidator.cs:159`、`EffectTargetResolver.cs:152`、`EffectRuntime.Combat.cs:426`）。
- **效果**：目前没有任何一张牌的 `target` 指向统领并造成伤害（全牌池扫描结果：只有 `flame_bolt` / `flame_imp` / `neutral_mage` 三张针对 `ENEMY_FACE`，即玩家生命）。

**结论**：不存在可靠的击杀链。

### 2.2 `durabilityDefeated`（耐久归零）

`src/Engine/Effects/EffectRuntime.cs:60-63`

```
var durabilityDefeated = leader is not null
    && !leader.IsMinion
    && leader.Definition.LeaderDurability > 0
    && leader.Durability <= 0;
```

`machine_leader`（durability=8）与 `wood_leader`（durability=20）有耐久值，但**没有任何效果动作能减少耐久**：`LeaderDurability` 在引擎里只出现在**构造与读取**处（`CardInstance.cs:27/88`、`CardDefinition.cs:165/255`），**没有写入点**。

**结论**：耐久只会停在初值。

### 2.3 `lifeDefeated`（玩家生命归零）

`src/Engine/Effects/EffectRuntime.cs:64`

```
var lifeDefeated = player.Life.HasValue && player.Life.Value <= 0;
```

能减少玩家生命的只有 3 张牌，合计 **5 点**：

```
flame_bolt    DAMAGE ENEMY_FACE 3
flame_imp     DAMAGE ENEMY_FACE 1
neutral_mage  DAMAGE ENEMY_FACE 1
```

而 `sea_leader` 的 `grantLife: 25` 是**赋值**不是加值（`EffectRuntime.Cards.cs:401-403`：`player.Life = leader.Definition.GrantLife`）。

**结论**：净效果是**把生命设成 25**，5 点打脸永远到不了 0。

### 2.4 结构性问题：`lifeDefeated` 绕过了门限

`EffectRuntime.cs:70-93` 的门限保护对三条路径一视同仁（钳到 1，发 `DEFEAT_PREVENTED`，`reasonKey = rule.leader_gate`）。但 `CheckAll` 的结尾（`:48` → `EvaluateLeaderWinConditions`）里，统领胜利条件走的是 **`TryDeclareWinner`**，而 `TryDeclareWinner`（`:138-160`）**自带门限检查**（`:152-156`）。

**这也就说明勘误 1 的根子**：`lifeDefeated` 是一条**绕过 `TryDeclareWinner` 门限**的独立败北路径，而 owner 的规则里**没有这条路径**。

---

## 3. 配合规则改动的 4 个代码点 —— **已全部执行**

**owner 于 2026-09-12 授权 PL 在 Codex 限额期间直接执行。** 全部完成，详见 §3.1。

| # | 位置 | 改动 |
|---|---|---|
| 1 | `src/Engine/Match/MatchSetup.cs` | `PlayerLife` 由 `int = 20` 改为 **`int? = null`**，并补写文档注释说明"默认不开启生命池、由 `grantLife` 开启" |
| 2 | 同上，`ValidateOptions` | `PlayerLife < 0` 改为 `PlayerLife.HasValue && PlayerLife.Value < 0` |
| 3 | `src/Engine/Effects/EffectRuntime.cs` `CheckAll` | **删除 `lifeDefeated` 分支**及其 `win.enemy_life_zero` 判定，并留下注释说明为什么删、以及将来要启用生命轴应挂到 `TryDeclareWinner` |
| 4 | 同上，门限保护段 | **删除 `player.Life = 1` 钳制**（门限只再钳统领血量与耐久） |

**保留不动**（与预判一致）：
- `EffectRuntime.State.cs:171-175` —— `Life` 为 `null` 时 `LoseLife` 发 `target.no_life_pool` 并跳过。**行为正确**，且恰好实现"默认不存在生命池"。
- `grantLife` 字段（`CardDefinition.cs:235`）与 `sea_leader` 的 25 —— owner 明确说字段保留。

**波及面（实测比预估小）**：14 个测试文件显式传了 `PlayerLife = 20`，但**没有一个因此失败** —— 因为它们是**显式**指定生命池，语义现在变成"这个测试要一个生命池"，依然成立。真正失败的只有 3 个**断言了已删除规则**的测试，已按下节改写。

### 3.1 需要改写的测试（已完成）

| 测试 | 原断言 | 处理 |
|---|---|---|
| `EffectSafetyTests.LoseLifeEndsGameAfterBothLeadersAreFielded` | 双方统领在场、生命归零 → `WinnerPlayerIndex == 0` | **改名为 `LoseLifeDoesNotEndTheGameEvenWithBothLeadersFielded`**，断言生命确实到 0 但**不判胜负**、无 `DEFEAT_PREVENTED` |
| `EffectSafetyTests.LoseLifeUsesLeaderGateBeforeEndingGame` | 生命归零 → 钳到 1 + `DEFEAT_PREVENTED` | 改写为"生命**不再**经过门限"（它已不是败北条件） |
| `T1PunishRoundCapTests.CapDoesNotChangeWinnerChecks` | 生命归零 → `win.enemy_life_zero` | 保留上限本身的断言（关闭 3 次触发 / 开启 1 次），胜负断言改为"生命归零两侧都不判胜负、原因不等于 `win.enemy_life_zero`" |
| **新增** `PlayerLifePoolContractTests`（5 个测试） | — | 锁定新契约：默认值为 null、对局开局无生命池、`grantLife` 能从"无"开启生命池、生命扣到 0 不判胜负、无生命池时 `LOSE_LIFE` 报 `EFFECT_SKIPPED` |
| **删除** `AiLethalAndDefenceTests.AnIncomingLethalIsNotIgnored` | AI 应当自救 | **前提非法**（见 §4），连同 5 个只为它存在的辅助方法一并删除，原地留注释说明原因 |

**门禁结果**：`NUNIT_RESULT result=Passed total=875 passed=875 failed=0 skipped=0 inconclusive=0 assertions=140947`


**保留不动**：
- `EffectRuntime.State.cs:171-175` —— `Life` 为 `null` 时 `LoseLife` 发 `target.no_life_pool` 并跳过。**这个行为是对的**（没有生命池就别扣），且恰好实现了 owner 的"默认不存在生命"。
- `grantLife` 字段（`CardDefinition.cs:235`）与 `sea_leader` 的 25 —— owner 明确说字段保留。

**波及面提示**：`MatchSetupOptions.PlayerLife` 改默认值会影响**至少 14 个测试文件**（它们显式传了 `PlayerLife = 20`）：
`AiBalanceSignalTests`、`AiCrossPolicyAgreementTests`、`AiEngineExactMatchTests`、`AiHumanReplayExportTests`、`AiLethalAndDefenceTests`、`AiMetamorphicRelationTests`、`AiPolicyRobustnessTests`、`AiTacticalBenchmarkTests`、`AiThreatEstimatorTests`、`AiTurnCoordinatorTests`、`AiTwoPlyConsistencyTests`、`PlaystyleTests`、`RollbackActionTests`、`P1P2CorrectnessTests`。
**这不是顺手能改的改动**，需 owner 批准后单独批次处理。

---

## 4. 本提案作者的一次误判（记录在案）

**我（PL）曾基于错误前提指控 AI 有缺陷，并已撤回。**

- 我写了 `AiLethalAndDefenceTests.AnIncomingLethalIsNotIgnored`：摆"对面一只 40 攻击随从 + 我方 20 血"，断言 AI 应当自救。
- 该测试**失败 4/4**，我据此报告"AI 看不见血量，是最严重缺陷"，并把"生存修复"写进了 Codex 的授权批次（`docs/DAILY_GOAL.md:8`）。
- **实际是三重错误**：
  1. 假定生命默认存在（owner 的模型是默认不存在）；
  2. 手工塞入 20 血，构造了**非法局面**；
  3. 拿"生命归零判负"当理由，**而这条根本不是 owner 的规则**（勘误 1）。
- 补充：该测试的"致命已广告"判据（`AiLethalAndDefenceTests.cs:505-508`）也写宽了 —— 它会把**打王城**（`CoreTarget.RoyalCastle`）当成"要打死我"。

**因此**：
- `AiLethalAndDefenceTests.AnIncomingLethalIsNotIgnored` **已删除**（前提不可达，留着会继续误导审阅者 —— 第一个被误导的就是我）。原位置留了注释说明删除原因。
- `docs/DAILY_GOAL.md:8` 中"生存优先"的授权**失去依据**，应撤回或重写。**（未改，等 owner 处理流程文档。）**
- 本报告 `docs/PL_AI_VALIDATION_ACCEPTANCE_2026-09-12.md` 中"AI 看不见生命 = 最严重缺陷"整段**已撤回**。

---

## 5. 附带修复：`RuntimeActionSelection` 的编译错误（V3 自弃牌提交）

`src/Adapters/RuntimeActionSelection.cs:166` 有一处 **netstandard2.1 编译错误**，阻塞整个解决方案的构建：

```
if (!spec.CandidateIds.Contains(id))
→ CS7036: MemoryExtensions.Contains(ReadOnlySpan<char>, ..., StringComparison) 缺少参数
```

在 `using System` 作用域下，对 `long` 的成员调用会通过隐式 string→span 转换绑定到 `MemoryExtensions`。
**已改为显式循环**（`IReadOnlyList<long>` 既没有 `Contains` 也没有 `IndexOf`）。

> ⚠️ **V3 本身仍未修完。** 编译错误只是入口；真正的缺口是三条规则互锁：
> 1. 引擎 `LegalActionGenerator.cs:68-83` 在该局面下广告 `PLAY_CARD` + `discardRequired` + `discardCandidateIds`；`PlayCardActionHandler.Prepare` 从 `SelectedDiscardIds` 读取选择，并**要求数量等于 `required` 且每个 id 都在手牌中**（否则 `action.discard_selection_required` / `action.invalid_discard_selection`）。
> 2. `RuntimeActionBoundary.Validate`（`RuntimeContractV131ActionBoundary.cs:281-284`）要求提交的 payload **等于**广告内容 ⇒ 提交方**无法**加入 `selectedEntityIds`。
> 3. 网关唯一的候选集桥接 `TryReadSelectedIds`（`RuntimeMatchGateway.cs:390-437`）**只在 DISCARD 阶段生效**，且它的行为是 `candidates.Take(required)` —— **自动替玩家选**。
>
> 第 3 点与 owner 的裁定冲突：**"不能替玩家默选弃牌"**。所以正确修法需要在边界层引入一条**受控例外**：
> 允许自弃牌类 `PLAY_CARD` 的提交额外携带 `selectedEntityIds`，**仅**校验它（数量 == 广告的 `discardRequired`、每个 id ∈ 广告的 `discardCandidateIds`），其余广告字段仍**严格逐字匹配**；
> 并**不得**沿用 `Take(required)` 的自动选择。
> 这是 owner 已批准的修复方向，但**本轮未实施** —— 它改的是适配层契约，需要明确决定"自弃牌是否强制显式选择"（即：是否连 DISCARD 阶段那条自动桥接也一并取消）。**留给 Codex 在限额恢复后处理。**


---

## 5. 待 owner 确认的一个细节 —— **已确认并落地**

"生命归零时到底发生什么"已按下列写法落地（owner 裁定 + 本轮实现）：

> **生命池归零后停在 0，不触发任何胜负判定。** 生命池只是一个可被增减的数值；
> `grantLife` 让它出现，`GAIN_LIFE`/`LOSE_LIFE` 让它变化，归零即停。

---

## 6. Java 与 C# 统一（本轮新增，已执行）

### 6.1 为什么做

`src/` 下**同时存在两套引擎**，且会各自漂移：

| | Java（`src/main/java`，原始引擎 + Swing 界面） | C#（`src/Engine`，web/Unity 适配层实际使用） |
|---|---|---|
| 生命归零判负 | **有**（`Game.java:1174`） | 本轮删除 |
| 门限把生命钳到 1 | **有**（`Game.java:1185`） | 本轮删除 |
| 攻击无耐久非随从统领 → 扣玩家生命 | **有**（`Game.java:1129`） | 本轮删除 |
| 胜利条件 | **硬编码 `switch`**，且缺 `ROYAL_CASTLE_BREAK` 等分支 | `IVictoryCondition` 统一接口 |

**用户提出的目标（"新增一个胜利条件 = 写一个进度计算，AI 不必改动"）此前只在 C# 达成**，
而 Java 那份还要求改 switch —— 也就是**每加一个胜利条件要改两处**。这正是要消除的东西。

### 6.2 改了什么

**规则层（与 C# 逐条对齐）**：

| 位置 | 改动 |
|---|---|
| `Game.checkLeaderDefeat` | 删除 `p.life != null && p.life <= 0` 判负，以及门限期的 `p.life = 1` |
| `Game.damageLeaderEntity` | 删除 `else if (owner.life != null) damagePlayerLife(...)`，改为记录"没有耐久轨，攻击不产生效果" |
| `Game.manifestLeader` 日志 | "生命归零将落败" → "生命池不判胜负" |
| `CardDef.LeaderDef.grantLife` 注释 | 同步修正 |

**统一接口层（新增两个文件）**：

- `model/VictoryObjective.java` —— 胜利条件的**声明** + **读数登记表**。
  数据声明：`"victory": {"metric":"PULL_COUNT","direction":"INCREASE","target":6}`。
  **未知 metric 在加载期抛错**，而不是静默忽略 —— "声明了但没实现"必须立刻可见。
- `engine/VictoryConditionRegistry.java` —— 把本引擎的读数登记进去。
  **新增胜利条件只需在这里加一行**，AI / 合法动作 / 快照层都不必改。

`Game.checkSpecialWins` 由 `switch` 改为走统一读数；老字段 `winCondition`/`winParam` 保留为兼容回退。

### 6.3 两个必须记住的坑（都实际踩到了）

1. **读数方向写错会让统领"永远赢不了"，而症状完全不可见。**
   第一版把 `OPPONENT_DISCARD_COUNT` 读成 `players[seat]`（自己），而非 `players[1-seat]`（对方）。
   表现只是"胜利条件不达成"。**修法是逐条对着 C# 的 `CounterOf` 抄，不凭名字猜。**
2. **加载期解析不够，使用期也要能回退。**
   卡牌从 JSON 加载时 `victory` 会被预解析，但**测试与编辑器会手工构造 `LeaderDef` 并只写老字段**，
   那种对象就永远没有读数。故 `VictoryObjective.of(LeaderDef)` 在使用期自行回退并缓存。

### 6.4 结果

```
Java 回归：通过 59 / 59
C#  门禁：NUNIT_RESULT result=Passed total=877 passed=877 failed=0 assertions=140957
```

---

## 7. 校准重跑：数字与改动前**一字不差**，以及为什么

在修正后的引擎上以同样参数重跑威胁估算校准（5120 样本）：

| 指标 | 改动前 | 重跑 |
|---|---|---|
| 预测对 | 22064 | **22064** |
| ECE | 0.0285 | **0.0285** |
| MCE（最差 bin） | 0.0940 | **0.0940** |
| Brier | 0.1167 | **0.1167** |
| 系统偏差 | −0.0273（偏低） | **−0.0273** |
| 平均 \|概率误差\| | 0.0498 | **0.0498** |
| 覆盖率漏检 | 0 | **0** |
| 保比（hand share / pool share） | 1.45 | **1.45** |

**为什么一模一样**：校准测的是**隐藏信息估算** —— "对手手里可能有什么"，
它是关于**牌库构成与手牌规模**的推断。本轮改掉的三件事（生命池默认值、生命归零判负、
攻击无耐久统领的重定向）**都不在这条推断的输入里**，而且探针从不开生命池、也不跑到胜负结算。
**所以这不是"校准没受影响所以没问题"，而是"这次改动本来就不该出现在这个测量里"。**

**含义（可以直接用的结论）**：

- **估算器本身在修正后仍然可信**，§2.7（校准）与 C1 的结论**继续有效，无需撤回**。
- 但**行为类与胜负类的历史读数已过期**（凡涉及"AI 会怎么打""谁能赢"的）。
  报告里所有 `⑨ 平衡信号` 数字、`③ 战术` 数字都是在修正前的引擎上测的，
  **重跑之前不得引用**。
- 一条**结构性限制**：校准探针**只调用 `ThreatEstimator`，不经过 `StateEvaluator`、不读胜负路径**，
  因此它的绿灯**不能**被当作"胜负规则改动已验证"的证据。

