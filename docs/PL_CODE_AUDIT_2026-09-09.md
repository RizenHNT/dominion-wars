# 代码审核修复清单（PL 整合 · 2026-09-09）

> 来源：对抗子代理对 `src/Engine` + `src/Data` 的三轮独立审核（只读，未改生产文件）
> **PL 独立复核**：对每条 P0 做了源码抽查，标注 ✅ 属实 / ❌ 误报
> 用途：交 Codex 按优先级修复；每条建议配一条"失败即红"的回归测试

---

## 一、P0（4 条；PL 已复核 3 属实 + 1 误报）

### ✅ P0-1 `pioneerOpponentPunishBonus` 完全缺失（规则承诺未实现）
- **位置**：`src/Engine/Turns/CardPlayRules.cs:69-75`（`EffectivePunish`）
- **PL 复核**：`grep pioneerOpponentPunishBonus src/Engine` **零命中**（仅 Java 遗留 `Game.java:414` 有）✅ 属实
- **后果**：RULES §3.2「仅一方统领在场时对方所有卡牌惩罚值 +1」完全不生效；`data/balance.json:11` 的配置无人消费。**每张牌的有效惩罚抽牌数恒定少 1**
- **修法**：`EffectivePunish` → `EffectivePunish(state, player, card)`，加入"对方 solo leader → +bonus / 己方 solo leader → −selfDiscount"；`MatchRules` 补两字段，`MatchSetup` 从 balance 注入
- **回归测试**：只让玩家 0 统领在场 → 打 `punish:1` 的牌 → 断言对手抽 **2** 张
- **✅ 已修复（2026-09-11 02:36）**：
  - `src\Engine\Rules\MatchRules.cs:9-54` 新增 `pioneerOpponentPunishBonus` / `pioneerSelfPunishDiscount`（含校验与 XML 文档；`pioneerHandLimitBonus` 此前已实现，未重复）。
  - `src\Engine\Turns\CardPlayRules.cs` 抽出单一 `PioneerPunishModifier(state, player)`，`EffectivePunish(state, player, card)` 复用；旧的 `EffectivePunish(player, card)` 重载保留（源码兼容）。
  - `CardPlayRules.IsSoloLeader(state, idx)` 成为**唯一**的"先驱"判定，`DiscardPhaseHandler` 的内联判断改用它（行为不变）。
  - **红→绿已取证**：临时把修正置 0 → `p0-RED-p1-final.xml` 14 条中 **5 条失败**（`OnlyPlayerZeroLeads…`、`OnlyPlayerOneLeads…`、`AmbushSetPays…`、`LegalActionsAdvertise…`、`PioneerSelfDiscount…`）；恢复后 `p0-GREEN-final.xml` 14/14。
  - 全量：`PL-VERIFY-nunit.xml` **631/631、0 失败**（其中既有用例子集 603/603，与 QA 上次读数一致）。
- **⚠️ 一个刻意**不做**的范围决定（需 owner 知悉）**：**COMMIT/PULL 的生命周期惩罚不套用先驱威压。** 依据：RULES §3.2 说的是"对方**所有卡牌**惩罚值 +1"（= 出牌时支付的卡面惩罚值），而 §12.4 把 `commitCost`/`downloadCost` 定义为**机械生命周期动作**的独立惩罚抽牌额，不是"打出一张牌"。实测还发现：**套用它会翻转机械六次下载的胜利线**。故该决定留在 `PlayCardActionHandler.ResolveLifecyclePunish` 的 `<remarks>` 里显式记录为 owner 决策，而非静默折入。Java 参考实现（`Game.java:764/839` 的 `payLifecyclePunish`）同样不套用 ⇒ **两引擎一致**。

### ⚠️ P0-2 `AMBUSH_TRIGGER_WIN` 可声明、无胜利路径 —— **2026-09-11 复核实为 P1，已降级**
- **位置**：`CardCatalog.cs:19`（白名单）+ `EffectRuntime.EndPhase.cs` 的 `EvaluateLeaderWinConditions`（switch 无该分支）；`cards.schema.json:43` 同样允许
- **PL 复核（09-09）**：`grep AMBUSH_TRIGGER_WIN src/Engine` 零命中 ✅ 属实
- **PL 再复核（2026-09-11 00:45，更正我自己的定性）**：
  - 全 `data/cards/*.json` 中 `AMBUSH_TRIGGER_WIN` **只出现 1 次**（`neutral.json:52` = `gate_of_fate.leaderDef.winCondition`），且该卡**没有 `winParam`**。
  - `gate_of_fate` 的**真实胜利路径是 `ambushEffects: [WIN_GAME]`**，而 `WIN_GAME` **已实现**（`EffectRuntime.State.cs:134`），未开门时还会正确 `EmitSkipped(..., "rule.leader_gate")`（`EffectRuntime.cs:145`）——与卡面文字「若双方统领未齐则失败」完全一致。
  - ⇒ **不存在"设计上不可胜"的卡**。缺陷实际是：**一个被声明的胜利条件在引擎里从不被评估**（`EvaluateLeaderWinConditions` 还因 `winParam <= 0` 直接跳过该卡）⇒ 类型是**声明与代码不一致的卫生问题**，不是 P0。
  - ❌ **同时更正我 09-09 的另一条断言**：审计原写「`shadow_of_fate` 声明 `OPP_PUNISH_TRIGGERED_GE` → 设计上不可胜」。**实测 `OPP_PUNISH_TRIGGERED_GE` 在整个 `data/` 中零命中**；`shadow_of_fate` 实为 `winCondition: "NONE"` + 普通"击败对方统领"路径 ⇒ **该断言对当前数据不成立**。
- **处置**：降级为 **P1**，随夜班 P0 之后的清扫批次修（实现真实判定或让 schema 拒绝其当胜利条件用）。**不可停留在"文档说能赢、代码不认"**仍成立，但它不是 P0。

### ✅ P0-3 `DeferDeaths` 半成品 —— **属实的真缺陷，但机理与我先前的判断都不同（2026-09-11 02:38 已修复）**

**我的一次错误更正要先记下来**：我 01:55 曾把本条判为"误报、撤销"，理由是"两段 AOE 走 `ApplyAll` 时中间不做死亡清理"。**那个论证只对平铺情形成立，我漏了嵌套情形。** 平铺两段 AOE 确实正确（`DAMAGE_DEALT×4` 本就是对的），但**嵌套**的被动钩子链是真 bug：

- 原代码 `EffectDispatcher.ApplyAll` 的 `finally` 是 `DeferDeaths = false; CheckAll(context);` —— 硬置 false。
- `EffectRuntime.Cards.cs:213` 的钩子窗口会复制父上下文：`hookContext.DeferDeaths = parentContext.DeferDeaths`（=true），然后 `ApplyAll(hookEffects, hookContext)`。
- ⇒ **嵌套 `ApplyAll` 的 `finally` 把 `hookContext.DeferDeaths` 置为 false 并调 `CheckAll`**，而 `CheckAll` 读的正是这个 context ⇒ **在父批次还没结束时就把父批次的死亡结算掉了**，父批次后续的段因此**打空**。**这才是 P0-3 的真实机理。**

**可达形态（PL 按实现者的实测更正，并且比我原先描述更窄）**：触发条件是"**批中某一段引发了嵌套链**"，而不是"任意两段 AOE"。最常见的真实入口是 `DISCARD_OPP_RANDOM` → 对方场上的 `onOpponentDiscardEffects` 钩子窗口（`EffectRuntime.Cards.cs:207-216`）。**注意措辞更正**：漏掉的那一段**不是完全静默**——它会发 `EFFECT_SKIPPED`（`reasonKey=target.none`），只是**没有任何伤害落地**（pre-fix 实测 `EFFECT_SKIPPED(action=DAMAGE)=1`）。我先前写的"静默吞掉"不准确。

**可达性边界（诚实记录）**：现有 91 张卡里**没有"同批两段伤害 AOE"**，红演示是直接构造状态得到的 ⇒ 该机制**已被证明存在并被修复**，但**尚未证明在当前卡池里可达**（与 QA §13.23「当前不可达」一致）。

**修复（两处缺一不可，实测验证）**
1. `src\Engine\Effects\EffectRuntime.cs:38-41`：`CheckAll` 在 `context.DeferDeaths` 为真时**跳过** `CleanupNonLeaderDeaths`。
2. `src\Engine\Effects\EffectDispatcher.cs:145/160`：`ApplyAll` 记录 `previousDefer` 并在 `finally` **还原**（而不是硬置 false），使最外层批次仍是唯一的死亡结算点。

**证据**（`build-output/pl-p0/`，PL 已亲自复核代码逻辑与用例名）
- 红：`p0-RED-p3-final.xml` —— 还原两处改动后 `NestedHookChainDoesNotSettleDeathsInsideTheParentBatch` **失败**。
- 中间态红：`p0-RED-p3-guard-only.xml` —— **只加守卫、不加 `previousDefer` 同样失败** ⇒ 证明两处改动缺一不可，不是冗余防御。
- 绿：`p0-GREEN-final.xml` 14/14；全量 `PL-VERIFY-nunit.xml` **631/631、0 失败**（PL 本人运行并解析）。
- 边界用例同时锁住反向风险：`TwoPartAoeResolvesBothPartsAgainstTheSameTargets`、`SingleEffectBatchStillSettlesItsDeath`、`SingleLethalDamageStillRemovesTheMinion` 全绿 ⇒ 没有引入"死亡永不结算"。

**教训（PL 自省）**：这轮审计我犯了**方向相反**的两个错——先轻信子代理的 P0-3（其证据自相矛盾），再据"平铺情形不成立"就撤销了整条。**正确做法是：证伪一个具体场景 ≠ 证伪整个缺陷类；必须把该类里的所有调用形状都走一遍。**

### ✅ P0-4 王城破坏后既不胜也不重置 → 永久死局 —— **2026-09-11 复核实为已修复，结案**
- **位置**：`EffectRuntime.State.cs:186-226`（`ApplyCastleDamage`）；消费点 `Cards.cs:508-517`
- **PL 复核（09-09）**：`EndPhase.EvaluateLeaderWinConditions` 的 switch **无 `ROYAL_CASTLE_BREAK` 分支**，且 `leader.Definition.LeaderWinParam <= 0` 前置跳过（`flame_leader` 未声明 winParam）✅ 当时属实
- **PL 再复核（2026-09-11 00:45，结案）**：**已被实现**，`EffectRuntime.State.cs:186-235` 现在会显式结算破城终局：
  - `:186-194` 记 `CASTLE_BROKEN`、把破城方 `CycleWinCount` 抬到 `CastleBreakVictoryCount`、强制守方统领出场；
  - `:200-207` 随从型双统领优先判 `win.castle_break_minion`；
  - `:209-224` `ROYAL_CASTLE_BREAK` 只在**当前在场统领**上成立（牌库/手牌/墓地的隐藏统领不能靠它取胜），双方同时持有则 fail-closed（`return`，不判胜）。
  - **回归测试已在位**：`EffectRuntimeTests.cs:590`（`win.royal_castle_break`）、`:638`（`win.castle_break_minion`）、`:673`。
- **结论**：**结案**。原"给 `CycleWinCount` 写一个从不被读的幻影阈值"的担忧也不再成立——`CycleWinCount` 会被 `reshuffleLoseAt` 消费。

### ❌ 误报（PL 复核后撤销）
- 子代理早期版本称「惩罚链深度不增长 → 递归无上限 → `StackOverflowException`」，建议把 `PlayCardActionHandler.cs:339` 改成 `chainDepth + 1`
- **PL 复核**：L339 调用的是 **`ResolvePrepared`**（不是 `ResolvePunishResponses`），而 `ResolvePrepared` 内部 L235 已经是 `ResolvePunishResponses(..., chainDepth + 1)` → **深度每层确实递增**，`chainLimit=20` 有效。按该建议修改会引入**双重 +1**（链限制过早触发）
- **建议**：不要改；可补一条"链深度 21 层即停"的回归测试来锁定

---

## 二、P1（3 条）

| # | 问题 | 位置 | 建议 |
|---|---|---|---|
| P1-1 | 伏击 `cardId` 经初始化事件增量泄露 | `AmbushActionHandler.cs:49-54` + `RuntimeMatchGateway.cs:110-111` + `EngineProjectionAdapter.cs:541-546` | `AdapterContractTests.cs:147-188` 的禁用键清单补 `cardId` |
| P1-2 | 空发/被反制的攻击仍消耗攻击次数 | `EffectRuntime.Attack.cs:36`（`AttacksUsed++` 在 `NEGATE` 早退之前） | RULES 未规定 → 需 PL/owner 冻结；建议改为不消耗 + 补测试 |
| P1-3 | `EffectSpec.Condition` 是死字段 | `EffectSpec.cs:12/19/28` + `CardCatalog.cs:431-437` 读取，但**引擎无消费点** | 实现条件判定，或让 schema 拒绝该字段；带条件效果当前**无条件结算** |

---

## 三、P2（11 条摘要，供排期）

1. `ApplyPunishDelta`（`State.cs:144-150`）无下限 → 有效惩罚值可被压成负数
2. `SupportedKeywords`（`Combat.cs:10-14`）15 个中 **11 个无实现路径**（降临/同归/献祭/复活/秒杀/震慑/沉默/占星/寄生/潜行/吸血）；`AdvancedEffectTests.cs:354` 还在断言 `震慑` 可被授予 → 固化空转
3. `Enfeeble`（`Advanced.cs:36-45`）HP 无下限 → 负 HP 进快照
4. `RollbackEffect` 已注册但 `LegalActionGenerator` 不生成 ROLLBACK 动作、router 无 handler → **纯玩家无法回滚**
5. **隐藏信息**：`EngineProjectionAdapter.ToSnapshot`（`:66-69`）投影**双方牌库与手牌全部 `CardDto`**，`SnapshotMapperTests.cs:60-61` 把它固化为期望；`RuntimeSnapshotProjectionTests.cs:453-474` 的 `...DoesNotExposeDeckOrder` 名不副实；`:55-58`/`:209-210` 的 `CommitQueueCount == CommitQueue.Count` 恒真
6. `ROYAL_CASTLE_BREAK` 被 `EndPhase.cs:149` 的 `LeaderWinParam > 0` 前置跳过（当前只经破城路径生效，规则正确但组合无测试锁定）
7. `DamageCard`（`Combat.cs:334-337`）与 `ApplyCastleDamage`（`State.cs:183`）对 `DamagedThisCycle` 的语义未在规则书写明
8. 随从统领"门限保护"用 `DEFEAT_PREVENTED` 上报，但 `AdapterContractTests.cs:21` 白名单不含该类型 → 不可观测
9. `DiscardPhaseHandler.cs:109-114` 用 `player.Leader is not null` 判先驱，多活跃统领并存时返回 null
10. `TurnFlow.CompleteTurn`（`:237-260`）用 `Events.Append` 直写事件，绕过命令缓冲 → 破坏可回放性
11. `PunishConditionEvaluator.cs:19,24,34,44` 四个分支不在 `CardCatalog.cs:22` 白名单内 → **数据层不可达**；`OPP_PUNISH_DRAW_TURN_GE`/`NO_DAMAGE_TURNS_GE` 在正式数据中已无使用者

---

## 四、设计待裁决（需 owner/PL）

- **D-1 循环胜利方向**：`Cards.cs:508-517` 判"发生循环者自己"获胜；RULES §9 同时写"自己计数+1/达标者胜"与"让对方循环 10 次即让对方获胜（自己输）"→ 两句指向不同结论
- **D-2 破城后 `CycleWinCount=9`**：威慑还是应直接接循环胜利？（P0-4 的规则侧前提）
- **D-3 牌库顶是第二张统领时中断抽牌**（`Cards.cs:310-315`）：是否应改为判胜/判负？
- **D-4 无目标上下文中的单目标效果**：RULES §12.4 已标"待冻结"；当前静默 `EFFECT_SKIPPED(target.selection_required)` → 决定 `machine_cannon` 是否永远空发

---

## 五、测试盲点（12 条可直接转测试）

**断言过宽导致缺陷逃逸**：
- `EffectRuntimeTests.cs:583` 只断言 `CycleWinCount == 9`，**不断言是否终局** → P0-4 逃逸
- `TurnFlowTests.cs:79-98` 只查计数不查结算
- `AdvancedEffectTests.cs:452` 只验证"需要封印"，未验证"恰好 512 触发"
- `SnapshotMapperTests.cs:60-61` 把"牌库/手牌全量投影"固化为期望
- `AdapterContractTests.cs:21` 事件白名单是硬编码副本，新增事件不会自动失败

**可直接转成测试的未覆盖行为**：
1. 破城后是否终局 / 第二回合能否到达循环胜利（P0-4）
2. 多段伤害对同一目标的第二段是否结算（P0-3）
3. 先驱威压使对方惩罚值 +1（P0-1）
4. 被反制/空发的攻击是否消耗攻击次数（P1-2）
5. 对手手牌与牌库顺序不被投影（P2-5）
6. `EffectSpec.Condition` 不满足时效果不结算
7. 11 个未实现关键词应被拒绝而非静默接受
8. `ENFEEBLE` 后 HP 不为负
9. `COMMIT`/`PULL` 实际惩罚抽牌数 == `commitCost`/`downloadCost`
10. 被封印单位不可被再次强化、`GRANT_KEYWORD` 对其空发
11. 弃牌阶段 `requiredCount` 与 `SelectedEntityIds.Count` 不匹配时的拒绝路径
12. `CompleteTurn` 是否清空双方 `UsedTags` 与全部临时状态（含回合被打断路径）

---

## 六、建议修复顺序

**P0-4（死局，玩家直接可见）→ P0-3（"两段效果只结算一段"）→ P0-1（节奏规则失效）→ P0-2（不可胜统领）→ P1 → P2**

每条配一条失败即红的回归测试后再放行平衡与联机工作。

> 另注（与 QA 报告呼应）：`MatchFactory.cs:62-64` 传 `punishResponses = null` → 线上惩罚响应**恒被自动放弃**（回落 `DeclinePunishResponsePolicy`），且 `ACTIVATE_PUNISH` 不在 1.31 契约与合法动作表 → 发布运行时惩罚机制形同停用。这是**主线缺口**，建议并入 P0/P1 修复批次。
