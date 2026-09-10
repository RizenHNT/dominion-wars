# QA 项目整体状态审计 — 2026-09-10

> 作者：DeepSeek（测试负责人）· 2026-09-10 23:08–23:30
> 触发：owner 报“Codex 到本周额度限制”，要求检查项目整体状态
> 性质：只读审计。本报告不改任何生产文件。

---

## 0. 结论摘要

| # | 严重度 | 结论 |
|---|---|---|
| 1 | **P1** | 当前工作树 **测试为红**：`.NET Debug 601/603`（2 失败），全部由卡牌设计线改写 `data/cards/*.json` 后未同步测试断言导致。 |
| 2 | **P1** | **4 天无提交**。`HEAD` 仍是 `880250c`（2026-09-06），Codex 09-08/09-09/09-10 全天成果 + PL 卡牌落地全部停留在未提交脏工作树，**无 commit 快照可复现**。 |
| 3 | **P1** | Unity 侧存在**同源未验证风险**：Codex 的 `EditMode 306/306` / `PlayMode 26/26` 是**卡牌落地前**的读数，落地后**尚未复测**。（PL 点名担心的“Unity 机械 fixture commit 值”已在 §11 扫描确认为内联合成数据，**不含连带失败**；残余风险＝“从未复跑”本身。） |
| 4 | **P2** | CPU 对手 `Halted` / `LastReasonKey` 从不外露、不写日志：AI 一旦 halted，玩家只看到“CPU 不动了”，无任何提示或诊断。 |
| 5 | **P2** | QA-only 的 Unity 静态编译桩缺 2 个模块引用（`ImageConversion` / `ScreenCapture`），产生 2 个假 error，永久污染静态检查信号。 |
| 6 | ⚪ | `RuntimeAiTurnCoordinator.Reset()` 为死代码（UI 从不调用，改用置空重建）。 |

---

## 1. 环境

- 机器：Windows，仓库 `C:\Users\USER\Documents\dominion-wars-win64`
- 分支：`codex/p0-complete-match-loop-2026-09-06`，`HEAD=880250c`（2026-09-06 之后无新提交）
- .NET SDK：8.0.425
- 数据快照：审计时刻 `machine.json` SHA256 前缀 `A7AD0E6F997E`、`wood.json` `240C42D6DAA1`、`sea.json` `4C86A410D0BD`、`flame.json` `2740725CA701`、`neutral.json` `CAE381377FBD`

> ⚠️ 并发写入：本仓库当前**有另一个写入者在活动**（owner 说明为 harness 上的 DeepSeek 卡牌设计线）。审计期间观测到 `data/cards/*.json` 于 23:10:40–23:11:15 与 23:18:00 被写入。审计结论绑定上述哈希。

---

## 2. 数字门禁实测

### 2.1 通过项（本轮实测）

| 门禁 | 命令 | 结果 |
|---|---|---|
| 卡牌 schema | `.\scripts\validate-cards.ps1` | `SCHEMA_VALIDATION pass=91 fail=0 files=5` |
| 牌组 | `.\scripts\validate-decks.ps1` | `DECK_VALIDATION pass=4 fail=0 files=4 cards=91` |
| 空白差异 | `git diff --check` | PASS |
| 冲突标记 | 全仓库扫描 | 无 |

### 2.2 **失败项（本轮实测，P1）**

命令：

```
dotnet test DominionWars.sln -c Debug -p:MSBuildEnableWorkloadResolver=false --nologo
```

结果：

```
失败!  - 失败: 2，通过: 601，已跳过: 0，总计: 603
```

失败用例：

1. `FormalOrdinaryMachineMinionsHaveTheApprovedLifecycleDefaults`
   （`src/Engine/Tests/DataLoaderTests.cs:59-86`）
2. `ProductionMachineDeckCompletesCommitPushPullLandmarkAndAlphaVictory`
   （`src/Engine/Tests/ProductionFactionIntegrationTests.cs:377-400`，断言点 `:390`）

### 2.3 本轮**未能执行**（环境阻塞，非失败）

本会话的 PowerShell 宿主在 23:19 之后持续返回 `shell context is being reconfigured`，跨 20+ 次直接调用（含 `list_powershell` 本身、不同 `shellId`）与 1 个独立子代理均零输出。**根因判定**：这是**宿主层**整体不可用（连 `list_powershell` 都失败），发生在 owner 于本会话中开启**终端沙盒化 / Terminal Sandboxing** 之后 → 工具宿主在重配置 shell 上下文，未恢复。**恢复方式**：关闭（或保持）该设置后**重载 VS Code 窗口 / 重启本会话**。这与仓库代码无关。以下门禁**本轮无法复跑**：

- `.\scripts\validate-design-manifest.ps1`（本轮更早时段为 `pass=1 rows=320 files=320`）
- `java -cp "build/classes;build/test-classes" com.dominionwars.test.TestMain`（本轮更早时段为 **38/38**）
- `com.dominionwars.test.SimMain 300`（阵营胜率基线，**本轮未取得数字**）
- Unity EditMode / PlayMode（另受 connected Editor 项目锁限制）

---

## 3. 两个失败的根因（已定位到具体数据字段）

两处失败**均非引擎缺陷**，而是测试断言停留在“落地前的统一默认值”。

### 3.1 `FormalOrdinaryMachineMinionsHaveTheApprovedLifecycleDefaults`

该测试对 8 张普通机械随从硬断言 `CommitCost==1 && UploadCost==0 && DownloadCost==1`。

落地后 `data/cards/machine.json` 的实际值：

| 卡 | commitCost | uploadCost | downloadCost |
|---|---|---|---|
| machine_drone | 1 | 0 | 1 |
| machine_golem | **2** | 0 | 1 |
| machine_wall | **2** | 0 | 1 |
| machine_blaster | **3** | 0 | 1 |
| machine_titan | **3** | 0 | **2** |
| machine_spark | 1 | 0 | 1 |
| machine_assembler | 1 | 0 | 1 |
| machine_recycler | 1 | 0 | 1 |

→ 5 张与断言冲突。`PullEffects`（`BUFF/FRIENDLY_MINION/1/both`）仍全部一致，未冲突。

### 3.2 `ProductionMachineDeckCompletesCommitPushPullLandmarkAndAlphaVictory`

测试在 `:328` 把 `machine_blaster / machine_titan / machine_recycler / machine_assembler` 压入云栈并全部 PULL，随后在 `:390` 断言：

```csharp
Assert.That(pullDeclarations.All(item => item.Data["punish"]!.Equals(1)), Is.True);
```

PULL 的 `punish` 取自被下载卡的 `downloadCost`（`src/Engine/Turns/PullActionHandler.cs:195`）。`machine_titan` 的 `downloadCost` 由 `1` 改为 `2` → 该 PULL 的 `punish=2`，`:390` 断言失败。

> **更正（复核源码后）**：同处 `:391` 的 `PUNISH_DRAW == 8` **并未失配，仍然通过**。`EffectRuntime.Cards.cs:301-347` 的 `DrawCards` 无论 `amount` 多大，只在循环结束后 `Emit` **一次**（`:344`），幅度只写入 `count` 字段；因此 `PUNISH_DRAW` 事件数 = 惩罚结算次数 = 2 次 COMMIT + 6 次 PULL = **8**。`machine_titan` 的 `1→2` 只改变该事件的 `count` 载荷，不改变事件数量。
>
> 结论：该测试**只有 `:390` 一条断言失效**。初版报告把 `:391` 一并列为失配是不准确的，已按源码更正。

### 3.3 该失败**已被预测**

PL 在 `docs/AI_MAILBOX.md:440` 明确写下：

> ⚠️ 需 Codex 处理：`.NET 全量测试需在可运行环境重跑`（本会话 testhost 被沙箱拦）；可能受影响的断言见报告 §3（**ProductionFactionIntegrationTests / Unity 机械 fixture commit 值**）

Codex 在额度耗尽前未处理这 3 条断言；`docs/CODEX_AI_CLOSEOUT_REPORT_2026-09-08.md §21` 的 `603/603` 是**落地前**的数字。

### 3.4 规则一致性判断（重要）

`docs/RULES.md:268` 与 `:271` 均写明：

> 对正式 91 卡中**未单独声明** COMMIT/PUSH/PULL 数值或下载效果的普通机械随从，当前默认是 COMMIT=1、PUSH=0、PULL=1……**任何已明确的专属字段优先于该默认。后续新卡可例外。**

当前 8 张卡**都有显式专属字段**，因此**数据侧不违反规则书**，`1/0/1` 只是“未声明时的兜底默认”。→ 正确修复方向是**更新测试断言以反映显式值**，而不是回退数据。

> 附带澄清：PL 报告 D7 把“非 0 惩罚”列为偏离 `RULES.md:271` 的“全家 0 费”。实测 `punish`（出牌惩罚）在落地前后**完全一致**（golem/wall/blaster 均为 2、titan 5、spark 1、drone 0），**落地未引入该偏离**；这是更早批次遗留的规则书与数据口径差，不应记在本次落地账上。

---

## 4. 仓库洁净度

- `HEAD` = `880250c`（2026-09-06），此后 **0 次提交**。
- 脏条目数量级 ~110（Codex 自述 09-09 捕获为 107），含 80 个已跟踪文件改动与约 30 个未跟踪新增文件。
- 未跟踪新增文档：`PL_REPORT_2026-09-09.md`、`PL_CARD_DESIGN_LANDING_2026-09-09.md`、`CODEX_AI_CLOSEOUT_REPORT_2026-09-08.md`、`DESIGN_SEA_MACHINE_FINAL_2026-09-08.md`、`VISUAL_PRODUCTION_GOAL_2026-09-08.md`。
- 未跟踪新增代码：`RuntimeAiPolicy.cs`、`RuntimeAiTurnCoordinator.cs`、`RuntimeAttackDragArrow.cs`、`RuntimeAi*Tests.cs`、`Assets/QA/`、`InitTestScene*.unity`。
- `docs/CHANGELOG_CASTLE.md`（“completed changes only”）**未记录** 09-08 之后任何批次。

**风险**：Codex 休息 + PL 仍在写 data 的情况下，整个 4 天成果没有任何可 checkout 的快照。任何误操作（reset/clean/覆盖）都会不可恢复。这是当前**最高优先级的工程风险**，且已在 `PL_REPORT_2026-09-09.md` 中被 PL 独立指出。

---

## 5. Unity 侧未验证风险（P1）

- `docs/CODEX_AI_CLOSEOUT_REPORT_2026-09-08.md §21` 的 `EditMode 306/306`、`PlayMode 26/26`、`.NET Debug 603/603` 全部是**卡牌落地之前**的读数。
- PL 明确点名“Unity 机械 fixture commit 值”可能受影响（`AI_MAILBOX.md:440`）。
- 落地后 Unity 侧**没有任何一次复测证据**（新未跟踪测试 `RuntimeAiEditModeTests` / `RuntimeAiPlayModeTests` / `RuntimeAiIntegrationPlayModeTests` 同样未经落地后复测）。
- **部分解除（见 §11）**：`EditMode` 中所有涉及机械费用的 fixture（`RuntimeCardDisplayInspectEditModeTests.cs`、`Fixtures/PullLifecycleData/cards/fixture_cards.json`）用的是**内联/合成卡数据且显式写 0**，不读 `data/cards/*.json` → **不存在已知的连带失败**。因此 PL 点名的“Unity 机械 fixture commit 值”**确认安全**。
- 但 Unity 侧其余断言（卡面文本、卡数 91、木质效果）未纳入本轮穷举，且整套 EditMode/PlayMode 落地后**从未运行** → 该风险仍为**未验证**，不因 §11 而消除。
- 本轮**无法代跑**：connected Editor 持有项目锁，且本会话 PowerShell 宿主不可用。

→ 在恢复环境前，**不得**把 `306/306` 当作当前有效基线。

---

## 6. 新增 Unity AI 代码审查（只读）

文件：`RuntimeAiPolicy.cs`（154 行）、`RuntimeAiTurnCoordinator.cs`（157 行）、`RuntimeScreenFlow.cs`（642 行）。

**正面确认（符合 DAILY_GOAL 验收项）**：

- AI 只消费 `GetSnapshotForViewer(1)` 与快照自带 `LegalActions`；`ToGameAction` 逐字段复制广告动作，不追加目标/选择/规则推导值。
- 人类呈现固定 `_presentationViewerIndex = 0`。
- fail-closed 完整：终局（`ai.match_over`）、非我方回合（`ai.waiting_for_turn`）、无合法动作（`ai.no_legal_actions`）、重复动作（`ai.duplicate_action`）、拒绝（`ai.action_rejected`）、32 动作上限（`ai.action_limit_reached`）均停止且不自旋。
- 暂停门禁存在（`RuntimeScreenFlow.cs:487` `IsPauseMenuOpen` 时不再推进 CPU），异常路径 `Halt("ai.coordinator_failed")`。

**发现（P2）**：

1. `Halted` 与 `LastReasonKey` 在 `RuntimeScreenFlow.cs` 中**零引用**——既不上屏也不写 `Debug.Log`。AI 一旦 halted，玩家侧表现是“CPU 不动了”，无任何可读原因。
2. `RuntimeAiTurnCoordinator.Reset()`（`:133`）是**死代码**：UI 用置空 + 重建（`:269/:462/:535`）代替调用。
3. `Halted` 置位后**不会自动清除**：若 AI 在自身回合因 `ai.no_legal_actions` 停止，必须由 host 重建 coordinator 才能恢复。当前只能靠 `StopRuntimeSession`（回主菜单）重建 → 表现为该局 CPU 永久停摆。

---

## 7. QA 工具缺口（P2）

`build-output/unity-static-compile/DominionWars.Unity.StaticCompile.csproj`（被 gitignore，仅用于静态编译 `Assets/DominionWars.Runtime/*.cs` 与 `Assets/DominionWars.UI/*.cs`）只引用 5 个 Unity 模块，缺：

- `UnityEngine.ImageConversionModule.dll`（`RuntimeContentResolver.cs:463 Texture2D.LoadImage`）
- `UnityEngine.ScreenCaptureModule.dll`（`RuntimePlayerVisualSmoke.cs:395 ScreenCapture`）

两者在 `C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Data\Managed\UnityEngine\` 下确实存在。→ **非生产缺陷**，是静态检查永远报 2 个假 error 的工具缺口，会让真实回归被噪声掩盖。

---

## 8. 建议路由

| 项 | 建议 |
|---|---|
| §3 两处失效断言 | **test-only 修复**（更新为显式值 / 计数改为按数据推导）。属测试代码改动，需 owner 或 Codex 显式授权 test-only scope。 |
| §5 Unity 落地后复测 | 恢复 connected Unity 后串行复跑 EditMode/PlayMode，并把机器 fixture 与 `machine.json` 对齐。 |
| §4 无提交快照 | 请 owner 明确授权一次**本地 checkpoint commit**（仅纳入指定实现路径，不含 `Assets/QA/`、包锁、ProjectSettings 等代理脏项）；不做 push。 |
| §6 CPU halted 无提示 | 建议记为 P2 backlog：至少落一条 `Debug.LogWarning(LastReasonKey)`，上屏提示可留待 UI 批次。 |
| §7 静态编译桩 | 补 2 个模块引用（工具文件，非生产）。 |

---

## 9. 复现步骤

```powershell
cd C:\Users\USER\Documents\dominion-wars-win64
git rev-parse --short HEAD                     # 期望 880250c
dotnet test DominionWars.sln -c Debug -p:MSBuildEnableWorkloadResolver=false --nologo
# 期望（当前的坏状态）：失败 2 / 通过 601 / 总计 603
# 失败：FormalOrdinaryMachineMinionsHaveTheApprovedLifecycleDefaults
#       ProductionMachineDeckCompletesCommitPushPullLandmarkAndAlphaVictory
.\scripts\validate-cards.ps1                   # pass=91 fail=0
.\scripts\validate-decks.ps1                   # pass=4 fail=0 cards=91
```

---

## 10. 未完成项（下次接续）

- `.NET` 全绿复验（待 §3 修复后）。
- `SimMain 300` 阵营胜率基线（本轮未取得；旧基线 machine 16.3% 无存档佐证）。
- design manifest 320/320 与 Java 38/38 的落地后复跑。
- Unity EditMode/PlayMode 落地后复跑。
- `RULES.md:292`/`:296` 的“59 张主牌”与 `data/decks/*.json` 实际 60 张的差异：**经核查这是规则书自身记录的待迁移项**（`:292` 原文“……四套现有预构筑**仍须完成数据迁移**”），属已声明的 ⚪ 缺口，**不是新发现的缺陷**；测得的实际值为四套牌组各 20 种 ×3 = 60 张主牌。
- 古木 `ADD_ROOT` 落地后 512 轴是否在**真实对局可达性**（非手工 fixture）上成立。
- 落地新增时点效果（`machine_spark` commit DRAW 1、`machine_assembler` push DRAW 1、`machine_recycler` commit ROLLBACK 1）**当前零测试覆盖**（见 §11.2）。

---

## 11. 失效面全仓库扫描（2026-09-10 补充，只读）

为把待修复范围钉死，对全仓库做了硬编码旧机械 `1/0/1`、`punish`、以及 `machine_*` fixture 的扫描。

### 11.1 会因本次卡牌落地而失败（WILL BREAK）——精确 3 条断言

| 文件 | 测试 | 行 | 断言 | 实际冲突 |
|---|---|---|---|---|
| `src/Engine/Tests/DataLoaderTests.cs` | `FormalOrdinaryMachineMinionsHaveTheApprovedLifecycleDefaults` | `:71` | `CommitCost == 1` | golem/wall=2，blaster/titan=3 |
| 同上 | 同上 | `:73` | `DownloadCost == 1` | `machine_titan`=2 |
| `src/Engine/Tests/ProductionFactionIntegrationTests.cs` | `ProductionMachineDeckCompletesCommitPushPullLandmarkAndAlphaVictory` | `:390` | 所有 PULL `punish == 1` | titan PULL `punish=2` |

已确认**不受影响**的相邻断言：`DataLoaderTests.cs:66`（数量 8）、`:72`（upload==0）、`:74-77`（PULL 效果 1/FRIENDLY_MINION/1/both）、`:80-84`（alpha / leader 费用）；`ProductionFactionIntegrationTests.cs:274-277`、`:387`（首 PUSH `punish==0`）、`:391`（`PUNISH_DRAW==8`，见 §3.2 更正）。

### 11.2 安全（SAFE）——使用内联 / 合成卡数据，不读 `data/cards/*.json`

- 引擎侧：`DataLoaderTests.cs:30,42-44`（内联 JSON 练习默认值逻辑）、`CommitActionHandlerTests.cs:39,53,70,103,127,140`、`ContentCatalogTests.cs:186-224`、`PullActionHandlerTests.cs:265,317`、`AdvancedEffectTests.cs:327`、`AttackActionHandlerTests.cs:89`。
- Unity 侧：`RuntimeCardDisplayInspectEditModeTests.cs:160-162,189-191,284-286,303-308,339-341`、`Fixtures/PullLifecycleData/cards/fixture_cards.json:37-39,49-51`（显式写 0，不触发默认值）、`RuntimePullLifecycleFixtureEditModeTests.cs`（仅断言事件名/数量，不含费用）、`Tests.PlayMode/**`（无匹配）。
- `data/decks/*.json` 只引用卡 id，不含费用字段；`build-output/` 下无 `machine*.json` fixture。
- **口径提醒**：落地新增的三个时点效果**不被任何测试断言** → 既不会导致失败，也意味着**新机制处于零覆盖**。⚪ 建议 PL 决定是否立项补断言。
- **覆盖率说明**：本轮针对“费用 / punish / 机械 fixture / Unity 连带失败”做了穷举；Unity 侧其余（卡面文本、卡数 91、木质效果）不在本次扫描结论范围内，仍属 §5 的未验证区。

### 11.4 互补扫描（文本 / 卡数 / 海数值 / 木效果 / golden 文件）——同样 **0 处新增失效**

对落地批次其余改动做了第二遍穷举（**只读静态核查，非执行**）：

| 类别 | 结论 |
|---|---|
| 真实卡面文本（空/非空、精确串） | **无失败**。全仓库唯一断言真实文本的是 `DataLoaderTests.cs:154`（`machine_factory` = “召唤三个侦察机偶。”，`data/cards/machine.json:240` 一致）与 `RuntimeBootstrapEditModeTests.cs:62,65`（机械/古木 `winText`，一致）。**没有任何测试断言真实卡 `text` 为空或非空** → 本次补 20 张文本不会打破任何断言。 |
| 卡数 91 / 阵营卡数 | **无失败**。`DataLoaderTests.cs:24`（91）满足（实测 wood 21 + sea 21 + machine 22 + flame 21 + neutral 6 = 91）；`scripts/report-card-art-map.ps1:41`、`scripts/validate-decks.ps1:37`（4 套）亦满足。 |
| 海系数值 | **无失败**。`sea_leviathan_young` 与 `sea_devour` **全仓库无任何断言** → 5/6→4/6、punish 4→2 属**未被覆盖的改动**。`OpponentDiscardEffectsTests.cs:49-50`（`sea_siren` 5/6）满足。 |
| 古木 512 轴 | **无失败**。`DataLoaderTests.cs:29`（`wood_leader` 效果序列、`LeaderWinParam 512`）与 `ProductionFactionIntegrationTests.cs:77,134-135`（`RampantStacks==3`、血量 512）均满足；`wood_growth` 现值为 `ADD_RAMPANT 1` + `BUFF 2 param=rampant`，经 `EffectRuntime.Combat.cs:217-225` 的 ×8 叠乘可达 512。 |
| `ADD_ROOT` 是否被真实卡引用 | **已被真实卡引用**：`data/cards/wood.json` 命中行 `58,80,123,157,302,326,372,400,423,493`，确证落地生效；但**无测试断言真实卡上的 `ADD_ROOT`**（`WoodCounterEffectTests` 用合成状态）→ 又是**零覆盖**。 |
| golden / 快照 / 向量文件 | **不存在**。`data/**/*.json` 仅卡牌、牌组、`balance.json`、`ui.json`、schema；`data/balance.json` 不含任何卡 id/攻防/punish；`build-output/` 无 JSON；Unity 仅 3 个合成 fixture。唯一相关的是 `docs/test/sanity_v2_2026-08-12_1956.txt:11`（“总计: 91 张唯一卡牌”，仍成立，且不被 CI 执行）。 |

### 11.5 ⚪ 文档漂移（新发现，非阻塞）

`docs/effects.contract.md:27` 仍写着 `ADD_ROOT` **“尚未被 91 张运行时卡数据引用”** —— 本次落地后该句**已过时**（`wood.json` 现有 9 处引用）。建议由 PL/文档批次一并修正。


### 11.3 结论

修复面**精确为 2 个测试文件、3 条断言**，不存在更广的机械 fixture 雪崩，也**不存在 Unity 侧连带红**。§5 中 PL 担心的“Unity 机械 fixture commit 值”经实测为**内联合成数据，确认安全**；Unity 侧残余不确定性来自落地后**从未复跑**本身，而非已知 fixture 冲突。


---

## 12. 建议补丁（**提案，未实施**；待 owner / Codex 授权 test-only scope）

> 依据 §11：全仓库**只有 3 条断言**失效，且**判定为测试陈旧而非数据错误**（`RULES.md:268/:296`“已明确的逐卡值优先”）。以下为最小改动提案，供 Codex 直接采纳。

### 12.1 `src/Engine/Tests/DataLoaderTests.cs:66-78`

把“统一 1/0/1”断言改为“落地批准的逐卡显式值”。落在 `Assert.Multiple` 内的 4 行为例：

```csharp
// PL_CARD_DESIGN_LANDING_2026-09-09 §2.2（机械 M1）：逐卡生命周期费用已是显式值，
// 不再套用 RULES.md:268 的 1/0/1 兜底默认。
var approvedCommitCost = new Dictionary<string, int>
{
    ["machine_drone"] = 1, ["machine_golem"] = 2, ["machine_wall"] = 2,
    ["machine_blaster"] = 3, ["machine_titan"] = 3, ["machine_spark"] = 1,
    ["machine_assembler"] = 1, ["machine_recycler"] = 1,
};
var approvedDownloadCost = approvedCommitCost.ToDictionary(
    pair => pair.Key,
    pair => pair.Key == "machine_titan" ? 2 : 1);

Assert.Multiple(() =>
{
    foreach (var card in ordinaryMachineMinions)
    {
        Assert.That(card.CommitCost, Is.EqualTo(approvedCommitCost[card.Id]), card.Id);
        Assert.That(card.UploadCost, Is.Zero, card.Id);
        Assert.That(card.DownloadCost, Is.EqualTo(approvedDownloadCost[card.Id]), card.Id);
        // :74-77 的 PULL 效果断言保持不变（实测仍成立）
    }
    // :80-84 的 machine_alpha / machine_leader 断言保持不变
});
```

`DataLoaderTests.cs:66`（`Has.Length.EqualTo(8)`）与 `:72`（`UploadCost Is.Zero`）**无需改动**（已实测仍成立）。默认值逻辑本身已被 `ContentCatalogTests.cs:186-224` 的内联 JSON 用例覆盖，故此处不必保留“兜底默认”语义。

### 12.2 `src/Engine/Tests/ProductionFactionIntegrationTests.cs:390`

不要硬编码 `1`，改为**从被 PULL 的卡推导期望惩罚**（PULL 的 `punish` 定义就是该卡 `downloadCost`）。`ExecutePull` 循环（`:335-350`）里 `expectedTop` 就是该 `CardInstance`，顺手收集即可：

```csharp
var expectedPullPunish = new List<int>();
foreach (var expectedTop in machine.CloudStack.Reverse().ToArray())
{
    var pull = ExecutePull(state, flow, router, alpha, expectedTop, target);
    Assert.That(pull.Accepted, Is.True);
    expectedPullPunish.Add(expectedTop.Definition.DownloadCost);
    ...
}
// …并在 :390 处替换为：
Assert.That(
    pullDeclarations.Select(item => (int)item.Data["punish"]!).ToArray(),
    Is.EqualTo(new[] { 1, 1 }.Concat(expectedPullPunish).ToArray()));
```

期望序列（当前数据）＝`[1, 1, 1, 1, 2, 1]`（前两个是被提交的 pair，其后按 LIFO 顺序 assembler/recycler/titan/blaster）。
**Codex 落码前请复核两点**：① `Data["punish"]` 的装箱类型确实是 `int`（`PullActionHandler.cs:195` 传入 `int`，但事件经 `Data(...)` 后可能是 `long`，用 `Convert.ToInt32` 更稳）；② `expectedTop.Definition` 在被 PULL 后是否仍可读（同一实例引用，应可读）。
`:391` 的 `PUNISH_DRAW == 8` **无需改动**（见 §3.2 更正）。

### 12.3 验收判据

采纳后应得到：`.NET Debug` **603/603**（或在新增覆盖后 ≥ 603 且 0 失败）；`DataLoaderTests.cs:24`（91）与 §11.4 全部断言保持绿色。

### 12.4 建议一并考虑的**覆盖缺口**（⚪，非本批阻塞）

以下落地内容目前**零测试覆盖**，建议由 PL 决定是否立项补断言：

- `machine_spark` commitEffects `DRAW 1`、`machine_assembler` pushEffects `DRAW 1`、`machine_recycler` commitEffects `ROLLBACK 1`；
- 真实卡上的 `ADD_ROOT`（古木 512 轴）；
- `sea_leviathan_young` 4/6、`sea_devour` punish 2；
- 古木 512 轴的**真实对局可达性**（现有 512 断言是手工铺场 fixture，不代表对局中可达）。

---

— DeepSeek（测试负责人）· 2026-09-10
