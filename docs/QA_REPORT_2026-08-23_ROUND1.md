# 统御战纪（Dominion Wars）独立 QA 验收报告 — WBS 10.11.1-10.11.5 + 机械 10.11.7（Round 1）

- **报告人**：DeepSeek QA（独立验收）
- **日期**：2026-08-23
- **范围**：Codex 已完成的 WBS 10.11.1-10.11.5 + 机械 10.11.7 引擎实现，覆盖人类 owner 冻结的 5 项决策（① 统领直接出场 ② 手动下载 ③ 破城 ④ 古木 512 ⑤ 深海不做潮位）
- **验收基线**：docs/QA_PROPOSAL_AND_ACCEPTANCE_2026-08-23.md §三（TC1-1~1-6 / TC2-1~2-5 / TC3-1~3-5）
- **方式**：只读验证。未修改任何生产代码（src/、data/、scripts/ 均未改动），未执行 git add/commit/push。Codex 自报数据仅作对照，不作证据。

---

## 1. 验收环境

| 项 | 值 |
|---|---|
| 操作系统 | Windows_NT（Win64） |
| 仓库根目录 | C:\Users\USER\Documents\dominion-wars-win64 |
| Git 仓库 | RizenHNT/dominion-wars |
| .NET | net8.0，Release 配置 |
| 运行方式 | 本地离线（NuGet 离线告警 NU1900 存在但不影响构建/测试；Unity 门禁因离线不可用，仍 BLOCKED） |
| 控制台编码 | 默认 cp932；Python 中文输出脚本需 `PYTHONIOENCODING=utf-8` |

---

## 2. 复跑命令与实测结果

### 2.1 Release 引擎回归（基线）

```
dotnet test src\Engine\Tests\DominionWars.Engine.Tests.csproj -c Release
```

**实测：456 通过 / 0 失败 / 0 跳过（456/456）** —— 与 Codex 自报一致。

### 2.2 定向测试组（本轮独立复跑，计数可复现）

```
# 破城（含破城伤害开关）
dotnet test src\Engine\Tests\DominionWars.Engine.Tests.csproj -c Release --no-build `
  --filter "FullyQualifiedName~BreakingCastle|FullyQualifiedName~DamageCastle"
# 统领（manifest / summon / punish-drawn / 各守卫）
dotnet test ... --filter "FullyQualifiedName~Leader|FullyQualifiedName~PunishDrawn|FullyQualifiedName~Manifest|FullyQualifiedName~SummonLeader"
# 手动下载
dotnet test ... --filter "FullyQualifiedName~Pull"
# 木方计数
dotnet test ... --filter "FullyQualifiedName~AddRoot|FullyQualifiedName~AddRampant|FullyQualifiedName~CounterActionsRejectNonPositive"
```

| 分组 | 通过/失败/跳过 | 结果 |
|---|---|---|
| CASTLE | 6 / 0 / 0 | ✅ |
| LEADER | 43 / 0 / 0 | ✅ |
| PULL | 13 / 0 / 0 | ✅ |
| WOOD | 3 / 0 / 0 | ✅ |

### 2.3 数据完整性 / Schema / 契约门

| 命令 | 实测 |
|---|---|
| `java -cp build/classes;build/test-classes com.dominionwars.test.TestMain` | 38 / 38 通过 |
| `python scripts/sanity_check_v2.py` | 0 ERROR / 0 WARN / 0 INFO |
| `python scripts/align_check.py` | 通过（wood_leader = GIANT_HEALTH_GE） |
| `scripts/validate-runtime-contract.ps1` | 4 个 schema，11 valid / 6 invalid fixtures，fail=0 |
| `scripts/validate-cards.ps1` | 91 / 91 |
| `scripts/validate-decks.ps1` | 4 / 4（91 张卡） |
| `scripts/validate-design-manifest.ps1` | 320 / 320 |

`ui_event.schema.json` 关键字核对：`PULL_DECLARED`、`CARD_PULLED`、`LEADER_MANIFESTED`、`CASTLE_BROKEN`、`CASTLE_DAMAGED` 全部存在。

**注**：sanity_check 输出中的 2 处内联 `[WARN]` 标记（machine_alpha / shadow_of_fate 无 enterEffects 的随从统领）是既有信息性按卡状态，不是聚合告警，且非 10.11 引入。

---

## 3. 逐用例验收结论

图例：✅ PASS（通过）｜◐ 部分覆盖（无缺陷，覆盖性增强建议）｜⏭ 跳过（依赖前端）｜❌ FAIL（未发现）

### ① 统领直接出场（TC1-1 ~ 1-6）

| 用例 | 结论 | 证据 / 验证方式 |
|---|---|---|
| TC1-1 统领离开卡组直接出场（不入手牌） | ✅ PASS | `EffectRuntimeTests.DrawingLeaderManifestsWithoutHandAndRunsOnlyEnterEffects`：Draw 后断言 Deck、Hand 均**不包含**统领，LeaderZone 恰 1 张，Leader==该卡，Life=10+2=12（仅 enter 效果），LEADER_MANIFESTED 事件 1 次。`DrawingLeaderFormsManifestIntoTheirDedicatedZones`：MINION→Field、AMBUSH→AmbushZone、SPELL→LeaderZone。守卫：`AmbiguousActiveLeaderDrawFailsClosedWithoutSideEffects`、`DifferentActiveLeaderDrawFailsClosedWithoutMovingTheCandidate`。另 `DiscardAndEndPhaseTests.CastleForcesMissingLeaderBeforeSpecialVictoryAndDoesNotHandoff` 佐证强制出场前置场景 |
| TC1-2 主动出场按自出结算 | ◐ 部分覆盖 | `SummonLeaderReplacesOldLeaderAndGrantsLife` 覆盖主动换将+grantLife；抽到直接出场路径覆盖"自出=仅 enter 效果"。**说明**：当前引擎无通用费用（mana）系统，统领直接出场路径不扣费；若"费用"另有含义（资源/统御点）需人类确认。普通打出路径的费用+入场由 `PlayMinionPaysPunishSummonsAndResolvesSelectedCastleDamage` 覆盖（非统领专用） |
| TC1-3 被惩罚按惩罚结算 | ✅ PASS | `PunishDrawnLeaderManifestsAndRunsEnterAndPunishEffects`、`PunishDrawnLeaderFormsUseTheirDedicatedZonesAndPreserveEffectOrder`：先 enter 后 punish、顺序保持、各入对应区。守卫：`PunishDrawnMinionLeaderCannotAttackOnManifestTurn`（SummonedThisTurn=true） |
| TC1-4 出场回合不能攻击 / 统领区参与实体查询与 ID 分配 | ✅ PASS | `PunishDrawnMinionLeaderCannotAttackOnManifestTurn`；`LeaderZonesParticipateInEntityLookupAndIdAllocation` |
| TC1-5 卡牌特殊规则优先 | ◐ 部分覆盖 | AMBUSH 统领走 AmbushZone（`DrawingLeaderFormsManifestIntoTheirDedicatedZones` 的 AMBUSH 分支）即为"类型特殊规则优先于默认 LeaderZone 安置"。**缺口**：无"带伏击/免伤等特殊规则的统领在直接/惩罚出场时其特殊规则覆盖默认流程"的直接断言测试（依赖通用 ability 基础设施，间接覆盖）。低风险 |
| TC1-6 非随从统领不可被击杀/移除（不判负） | ◐ 部分覆盖 | **代码走查**：`EffectRuntime.CheckAll`（EffectRuntime.cs L31-94）中非随从统领仅当 `LeaderDurability>0` 且 `Durability<=0` 才判 durabilityDefeated；无耐久统领不可被耐久击杀。Destroy 走 `leader_immune` 守卫（State.cs L117）、Control 守卫 L29、攻击目标策略排除非 AMBUSH 统领。间接测试：`LoseLifeUsesLeaderGateBeforeEndingGame`、`AmbushLeaderInDedicatedZoneIsNotAnAttackTarget`。**缺口**：无"非随从统领被 Destroy/控制/击杀不掉血不判负"的直接断言。低风险（代码路径明确） |

### ② 手动下载（TC2-1 / 2-2 / 2-3 / 2-4 / 2-5）

| 用例 | 结论 | 证据 / 验证方式 |
|---|---|---|
| TC2-1 云端发光（引擎侧信号） | ✅ PASS（引擎侧） | `PullActionHandlerTests.AdvertisesOnePullPerCarrierForOnlyTheCloudTop`（每载具 1 个 PULL action、仅对云端顶端）；`AdvertisedPullRoundTripsThroughRuntimeGateway`。真实高亮投影依赖前端 6.3 |
| TC2-2 实机拖拽 | ⏭ 跳过（依赖前端 6.3） | 非本包引擎可验收范围；引擎部分由 TC2-1/2-3/2-4 覆盖 |
| TC2-3 点顶端卡拉箭头选目标 → 支付惩罚 → 结算顶端下载效果 | ◐ 部分覆盖 | **通过部分**：`PullActionUsesExistingSettlementAndMovesTheTopToGraveyard`（PullCount=1、顶端入坟场、效果在载具结算）、`PullEffectsResolveOnTheSelectedMechanicalCarrierBeforeTheCardReachesTheGraveyard`、`PullRemovesTheTopBeforeNestedPullEffectsResolve`（无重复结算）、`PullAdvertisesAndExecutesForAnExplicitMechanicalLandmark`。**缺口**：正下载费（downloadCost>0）路径**未实现**——`PullActionHandler.cs` L91-94 对 downloadCost>0 直接 `Reject("action.cost_system_unavailable")`（fail-closed，不虚构付费）；`PullWithPositiveDownloadCostRejectsWithoutInventingPayment` 是唯一覆盖且断言的是"拒绝"。**数据现状**：data/cards/machine.json 22 张机械卡全部 downloadCost=0（无该字段）且无 pullEffects，当前生产数据不触发付费路径，故 fail-closed 不阻塞任何现卡；但"支付惩罚"（来源/数值/结算顺序）本身未定，需人类确认 + Codex 排期 |
| TC2-4 非法目标/未知效果/非载具拒绝（先校验后变更） | ✅ PASS | `PullRejectsNonTopTargetsBeforeAnyMutation`、`PullWithPositiveDownloadCostRejectsWithoutInventingPayment`、`UnknownPullEffectIsNotAdvertisedAndIsRejectedBeforeMutation`、`PullRejectsAnOrdinaryMechanicalLeaderThatIsNotAMarkedLandmark`、`PullRequiresAMechanicalCarrierAndDoesNotTreatAnOrdinaryFieldCardAsOne`，另含 action.id_mismatch 校验（L85-89）。空云端：`LegalActionGenerator` L90 `CloudStack.Count>0` 才 advertise + `EffectRuntime.Mechanical.cs` cloud_empty skip（无专门空云端测试，逻辑路径清晰） |
| TC2-5 手动下载结算/胜利条件 | ✅ PASS | `PullActionUsesExistingSettlementAndMovesTheTopToGraveyard` 复用 `EffectNames.Pull` 结算；`EffectRuntime.EndPhase` `PULL_TOTAL_GE`（L152）→ `PullTotalWinConditionUsesSuccessfulPulls`（PullCount）；`AdvertisedPullRoundTripsThroughRuntimeGateway` |

### ③ 破城（TC3-1 ~ 3-5）

| 用例 | 结论 | 证据 / 验证方式 |
|---|---|---|
| TC3-1 抽牌计数扣到剩 1 → 我方获增益 | ✅ PASS（归属语义待人类确认，见 §5） | `BreakingCastleAppliesCountdownForcesLeaderAndChecksCastleVictory`（L521）：CastleHealth 2→0，断言破城方 `CycleWinCount == 9`（GameState `_reshuffleLossThreshold=10` → 剩 1 循环）、`DamagedThisCycle=true`；事件序 CASTLE_DAMAGED < CASTLE_BROKEN < LEADER_MANIFESTED < GAME_WON |
| TC3-2 对方首领经普通抽牌方式强制出场 | ✅ PASS | 同一测试断言 `victimLeader.SummonedThisTurn==true`、`Life==12`（=10 grantLife + enter +2，**未加 punish +7**）→ 证明走普通抽牌路径（byPunish:false）。实现：`EffectRuntime.State.cs` ForceLeaderOut（L233-255）搜索 Hand→Deck→Graveyard，`ManifestLeader(byPunish:false)` |
| TC3-3 破城只结算一次 / 不重复触发 | ✅ PASS | `BreakingCastleDoesNotLowerCountOrRepeatBreakResolution`（`max()` 保持既有 12，不重复 break 结算）；实现 `EffectRuntime.State.cs` L186-188 `CycleWinCount = max(existing, 9)` |
| TC3-4 破城时我方（破城方）统领优先/特殊结算 | ✅ PASS | `BreakingCastleWithActiveMinionLeadersGivesBreakerPriority`（双随从统领在场 → 破城方 `win.castle_break_minion`，避免平局）；代码 `EffectRuntime.State.cs` L173-222 双随从统领分支 |
| TC3-5 破城胜利条件（皇家破城） | ✅ PASS | `BreakingCastleRoyalConditionCanAwardActiveDefender`（防守方在场 ROYAL_CASTLE_BREAK 统领 → 防守方获胜）；`BreakingCastleIgnoresHiddenRoyalLeader`（牌库/隐藏统领不判胜）。代码仅评估在场活动统领，双方/均非在场 fail-closed 不判胜 |

### ④ 古木 512 ｜ ⑤ 深海不做潮位

| 项 | 结论 | 证据 |
|---|---|---|
| ④ 古木 512 + 疯长仅统领结算 | ✅ PASS | data/cards/wood.json wood_leader：winCondition=GIANT_HEALTH_GE、winParam=512、enterEffects=SUMMON 2 木苗 + ADD_RAMPANT 1、punishEffects=PROTECT_TURN + ADD_RAMPANT 2；`EffectRuntime.EndPhase.cs` L155 GIANT_HEALTH_GE 实现；align_check 通过。ADD_ROOT/ADD_RAMPANT 效果存在（疯长上限 3 层，`AddRampantIsCappedAtThreeLayersAndReportsTheAppliedDelta`） |
| ⑤ 深海不做潮位 | ✅ PASS（范围确认） | 数据/代码未见潮位机制，符合"本包不做" |

---

## 4. 与 Codex 自报的差异

- **计数一致**：456/456、Java 38/38、卡 91、牌组 4、设计清单 320、runtime schema 4（11/6）、fail=0，均与 Codex 自报一致。
- **细节差异（非失败）**：
  1. Codex 报告破城测试为 5 个；本 QA 独立统计含 `DamageCastleOnlyWorksWhenEnabled` 共 **6** 个（破城 5 + 破城伤害开关 1）。
  2. Codex 未报告数据面补充信息：22 张机械卡全部 downloadCost=0 且无 pullEffects（付费下载路径当前**无生产数据触发**）。属补充观察，非失败。
  3. sanity_check 中 2 处内联 `[WARN]`（machine_alpha / shadow_of_fate）为既有信息性标记，**非 10.11 回归**。

---

## 5. 需要人类确认的事项（⚠️）

1. **③ 破城"我方抽牌/增益"归属语义（最关键）**：冻结决策③原文为"抽牌计数扣到剩 1"。Codex 实现为**破城方 `CycleWinCount = max(existing, 9)`**（GameState L20-21：reshuffleLossThreshold=10、castleBreakVictoryCount=9）+ 对方首领强制出场（byPunish:false）。**此"增益"表示法是按 PL 理解实现（将"对方即将在下一循环输掉"折算为破城方 9 计数），人类尚未最终确认该归属语义**。
2. **② 手动下载"支付惩罚"模型**：惩罚来源/数值/结算顺序未定；引擎对 downloadCost>0 fail-closed。当前无生产卡触发，但需确认惩罚模型（疑似归入 10.11.6 内容包）。
3. **TC1-2"费用"含义**：若统领主动出场需扣资源，需明确资源模型。
4. **Codex 报告遗留 HUMAN_REQUIRED 项**：122 条幽灵 P-prime 条目、10.11.6 winAmount→winParam 迁移、防御回填语义。

## 6. 需要 Codex 修复的事项

**本轮无 FAIL、无强制修复项。** 仅建议性覆盖性增强（低优先级，可写入 AI_MAILBOX ⚪）：
- TC1-5 / TC1-6：补充"特殊规则统领优先于默认出场流程"、"非随从统领被 Destroy/控制/击杀不掉血不判负"的直接断言测试。
- TC2-4：补充空云端时无 PULL action 的直接断言。
- TC3-2：补充首领在 Hand/Graveyard 时 ForceLeaderOut 搜索顺序的直接断言。

---

## 7. 严重性标注

| 级别 | 数量 | 说明 |
|---|---|---|
| P0 | 0 | 无数据丢失/凭据/安全/阻塞全部工作项 |
| P1 | 0 | 无 |
| P2 | 0 | 无已知缺陷（引擎层面全部 PASS / 部分覆盖） |
| P3（待决策/信息） | 4 项 | ③ 破城归属语义、② 惩罚模型、TC1-2 费用含义、Codex 遗留 HUMAN_REQUIRED |
| 依赖前端 | 1 项 | TC2-2 实机拖拽/高亮依赖前端 6.3（非本包引擎验收范围） |

---

## 8. 结论

- **Release 回归 456/456 全通过**；所有 15 个验收用例（TC1-1~1-6、TC2-1~2-5、TC3-1~3-5）**无 FAIL**。
- ✅ PASS（13 项 + ④⑤）：TC1-1、1-3、1-4、2-1、2-4、2-5、3-1、3-2、3-3、3-4、3-5、④、⑤。
- ◐ 部分覆盖（4 项，均为覆盖性增强建议，非缺陷）：TC1-2、1-5、1-6、2-3。
- ⏭ 跳过（1 项，依赖前端 6.3）：TC2-2。
- 唯一需要"人类拍板"的是 ③ 破城"我方抽牌/增益"的归属语义（现按 PL 理解实现为破城方 CycleWinCount=max(existing,9)）。

---

*本报告为 DeepSeek QA 独立验收产物；未修改生产代码，未执行 git add/commit/push。*
