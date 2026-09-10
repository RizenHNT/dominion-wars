# Dominion Wars 当前状态存档 — 2026-09-06

这是一份本地 checkpoint 的状态记录，不是发布声明，也不替代 Unity 实机复验。

## 存档位置

- 起点：本地 `main` 的 `6c9ef6506914ecc9deb6b8ee5f15580df4ac175f`。
- 分支：`codex/checkpoint-2026-09-06-playable-demo`。
- 远端只读核对：`origin/main` 当前为 `0f9868a1d43e85bc984607835b96aea6c9da837d`；本次没有 push。
- 本次 checkpoint 提交（按依赖顺序）：
  - `fade1180c979b4821882ae9f8c0419cde2969b2e` — 内容管线 / Card Editor。
  - `bfa921c25234cbef92d52f95c88995a3ba562ade` — Engine 伏击与机械生命周期。
  - `ae3df0ba19f06dcfb40b775d9530227610544ab8` — runtime contract / outcome wire。
  - `a5642d8523c0d79e2552de03c63949d3311611b9` — Unity runtime / UI / tests。

这些提交是可恢复的本地历史；它们不等于所有相关门禁已经通过。

## 当前可记录的验证证据

本次低额度存档只记录已有的、明确允许引用的基线：

- .NET：`548/548`。
- 离线卡牌：`91/91`。
- 离线牌组：`4/4`。
- 离线素材清单：`320/320`。
- Java：`38/38`。
- Unity 当前工作区版本：全量 EditMode、PlayMode、Windows Player 和前台视觉 smoke **尚未完成本轮复验**，不在本文件中宣称通过。

历史日报中的更早 Unity 数字仅属于当时的工作区证据，不能自动转移为本 checkpoint 的当前 PASS。

## 主线与优先级

P0 主线仍是玩家端完整一局：

`启动 → 选择/进入牌组 → 开局 → 读牌与鼠标操作 → 出牌/攻击/回合推进/PULL（由引擎广告时） → 终局 outcome → 恢复或再开一局`

只有会导致启动/崩溃、死局、核心规则明显错误、不可逆数据损坏，或后续必然推翻架构的问题可以插队。视觉 polish、CardEditor 极端便利功能、额外覆盖率、泛化重构和非主路径边界留作 P1/P2 backlog。

当前不能把游戏描述为“完成”：Unity 当前版本的完整闭环仍需重新运行并记录证据，且 Restart/session/seed/match-id 等待定语义仍不能由 UI 自行猜测。

## 本次卫生与安全审计

- 未把 `build-output/`、Unity `Library/Temp/Logs/Builds`、`bin/`、`obj/`、`target/` 或 `.codex-remote-attachments/` 纳入提交。
- 未把 `unity/**/Assets/QA/**` 截图或其 `.meta` 纳入提交。
- 未把空目录/来源不明的孤立 `.meta` 纳入提交。
- 候选文本和已暂存文件未发现高置信度密钥/私钥字面量；暂存文件没有超过 10 MiB 的文件。
- 本次未删除、清理或 reset 文件；未 push。

## 有意保留的工作区脏项

以下内容没有被强行处理，待后续由 owner/PL 重基线或单独决定：

- `docs/AI_MAILBOX.md`、`docs/DAILY_GOAL.md`、内容规格和 known-issues 的历史增量：其中包含旧 Unity 证据，当前版本未复验，暂不作为当前事实提交。
- `scripts/sanity_check.py` 的删除：本次遵守不删除文件，保持未提交删除状态。
- `unity/DominionWars.Unity/Packages/manifest.json`、`packages-lock.json` 与 `ProjectSettings/*` 的 AI Assistant / 编辑器环境变更：未证明是游戏运行所需依赖，暂不纳入游戏 checkpoint。
- `src/Data/DeckEditor.meta`、`unity/DominionWars.Unity/Assets/DominionWars.Editor/ContentStudio.meta`、`unity/DominionWars.Unity/Assets/Resources.meta`：对应空目录或来源无法确认，暂不提交。
- `unity/DominionWars.Unity/Assets/QA/` 及其截图、`.meta`：明确属于人工 QA 产物，暂不提交。

下一次开发应从本分支恢复，先完成 Unity 当前版本的真实复验，再回到上述 P0 玩家闭环；除非获得明确授权，不要把这些脏项自动 add 或 push。

## 🟡 [Codex → PL/QA] Unity P0 当前复验交棒（2026-09-07）
- 正式开发分支为 `codex/p0-complete-match-loop-2026-09-06`；connected graphical Editor **256/256**、PlayMode **16/16**；Windows x64 build **0 errors / 486 shader warnings**，Player 日志确认 `TITLE shell active`。
- 原始结果见 `build-output/unity-runtime-validation/20260906-gui-player/RAW_TOOL_OUTPUTS_20260906.md`，PlayMode 完整 JSON 见 `unity/DominionWars.Unity/Temp/pipeline_test_status.json`。
- 真实鼠标闭环尚未完成：`@oai/sky` 初始化成功但正式 Player 返回 `windows:[]`，Unity 窗口绑定失效；未将自动化、handler 或 fixture 当作鼠标证据。
- （历史记录，已被 2026-09-09 默认内容替代）正式 91-card 数据当时暂无 `COMMIT`/`PUSH`/`PULL` payload；当前普通机械随从已接入默认生命周期字段与 PULL 目标选择。

## 🟡 [Lunar Max → PL/QA] Unity P0 窄验证收尾（2026-09-08）
- 临时 `DragFixtureDiag`/生命周期 trace 已移除；`rg` 无残留，`git diff --check` 通过。原生桌面鼠标仍不可用，故不宣称真实鼠标闭环通过。
- 反馈 EditMode `RuntimeBattlePanelActionFeedbackEditModeTests`：**31/31 PASS**，结果写入 `C:/Users/USER/AppData/LocalLow/DefaultCompany/DominionWars_Unity\\TestResults.xml`；覆盖权威 DAMAGE amount/target、缺失字段不臆造、AMBUSH/CASTLE 优先级。
- 真实 InputModule PlayMode 窄集当前 **3/4**：Hand 的 press→move 仍在 threshold 断言失败；其余通过。攻击项仍是 direct helper 路径，虽有 DAMAGE_APPLIED/生命变化断言，但不能替代真实鼠标证据。
- 当前程序集证据：`RuntimeTargetDragEventSystemPlayModeTests.cs` SHA256 `C147A48321E503B3F001541EC5844A22D95AB39E9A5A69293056B7A2F4288C8E`（15:18:24Z）；`DominionWars.Unity.PlayMode.dll` SHA256 `00563AAF387DEF112FD0CCB29CAC28E3D26C7C319DB7E83C62B3C39F189F7CB6`（15:18:34Z）。反馈源 `RuntimeBattlePanelActionFeedback.cs` SHA256 `396740241014F67BC65BD5519DFC0BD264D0C592647F0411EC717942174C4260`；`DominionWars.Unity.EditMode.dll` SHA256 `D09B7E9D2189AE9A229341DC2DBAFE8E216591D7095162DDAA130AB55D209983`（15:15:28Z）。
- 用户新增后续优先级按此顺序记录：① P0 UI 分层与可读场上名称/攻防状态；② 核对机械上传下载、古木养巨物是否正式数据接通；③ AI 代替双方轮换、抽牌与对手效果可见反馈。本轮未开始这些后续代码。
- 后续时序修正已替代上述 **3/4** 结果：修正测试 fixture 的两个时序问题——`QueuedBaseInput` 必须通过 `EventSystem.AddComponent` 挂载，不能用 `new MonoBehaviour`；切换到新 `StandaloneInputModule` 后必须先等待激活帧，再排队 press。加入每步 `Process()` 消费计数后，四项 PlayMode **4/4 PASS**。当前证据：源文件 `RuntimeTargetDragEventSystemPlayModeTests.cs` SHA256 `ED937AF6E34C6AAA8482B7040C00A2F1796481030EFFF41C907F55EDACD32085`（15:30:12Z），`DominionWars.Unity.PlayMode.dll` SHA256 `EA92CBCFA34C09572A623CE42457779900D4759D6642644409C3E0C1A559E692`（15:31:00Z），结果仍写入 `C:/Users/USER/AppData/LocalLow/DefaultCompany/DominionWars_Unity\\TestResults.xml`。这是测试输入时序修正证据，不等于原生鼠标验收；攻击断言仍是 direct helper 路径，native mouse 仍未验收，全局 P0 不关闭。

## 🟡 [Lunar Max → PL/QA] FieldAttack 原生路径复验更正（2026-09-08）
- `FieldAttackToOpponentCardHighlightsAndSubmitsExactlyOnce` 已升级并通过真实 `StandaloneInputModule` + 挂载 `BaseInput` + 就绪帧 + `ProcessedStepCount` 路径；保留 `DAMAGE_APPLIED` source/target/amount、攻击者 HP=3、目标消失断言。
- 同次四项结果为 **3/4**：FieldAttack、IllegalDrop、MultipleActions PASS；Hand 失败原因是测试 fixture 的 `ProcessedStepCount` 在跨测试复用同一 module 后从 4 累计到 5，断言仍要求 1。该失败是测试计数基线问题，未据此指向生产拖拽逻辑；本轮不再扩修。
- 当前结果 XML：`C:/Users/USER/AppData/LocalLow/DefaultCompany/DominionWars_Unity\\TestResults.xml`（2026-09-08T00:12:30Z，SHA256 `1EB26DB889631C7E3C390A4161E037D78A3F51733AEF1830FA7C36761BB86CC1`）。源 `RuntimeTargetDragEventSystemPlayModeTests.cs` SHA256 `6D17E75939101C7E6317DFBD4BF5899E023B6DDAC09320B5E182B37F84183CF6`，PlayMode DLL SHA256 `D8B3792D5A1A9443A235FE07ABCD5D644626C9C901BF382A7FD3C3CAF10204DB`；recompile `up_to_date/failed=false`，Editor 已回到 ready。
- 当前没有可核验的“58 项”XML；现有旧 `build-output/20260906-234243-2836ba5a/editmode-results.xml` 是 14:44Z 的旧 **17/17**，不能证明新增 18/18。原生 OS 鼠标仍未验收，不宣布全局 P0 关闭。

## 🟢 [Lunar Max → PL/QA] 证据归档与拖拽四项最终复验（2026-09-08）
- 已核实并归档新增 LeaderWinText 相关 XML 到被 `.gitignore` 忽略的 `build-output/unity-runtime-validation/20260908-leaderwintext-evidence/`：`dw-card-display-leader.xml` **9/9**（SHA256 `091A23455508C9C8B8990D7F32FE30EDD240C280427B008652BEBE522019E8DC`，源测试 00:01:16Z，XML 00:05:25Z）；`dw-leader-slot.xml` **18/18**（SHA256 `64D2DE9E6C9954F7FE7AACC240C1F2646BA089116CB40A68B82862319EE5B29B`，源测试 00:02:15Z，XML 00:05:58Z）；`dw-feedback-after-leader.xml` **31/31**（SHA256 `A61F30F3EA57216890793C55301599B5DD7C031E15A6080856F9A79B716160C4`，源测试 15:14:31Z，XML 00:06:37Z）。旧结果不再作为当前证据引用。
- `ResetForTest()` 清空跨测试复用的 queue/input/count 后，正确项目仅跑 `RuntimeTargetDragEventSystemPlayModeTests` 四项，**4/4 PASS**：FieldAttack、Hand、IllegalDrop、MultipleActions。源测试 SHA256 `D607B85F66A68F4E5FF9BF34A5C96BC9A4E9B2E3FA1676368FD0E8A255B0C416`（00:14:51Z），PlayMode DLL SHA256 `42572A7766F231F1FADA8EF27C22467FA2A9FD7E0F585BF6328D56876786E577`（00:15:06Z）；recompile `failed=false`，结果 XML `C:/Users/USER/AppData/LocalLow/DefaultCompany/DominionWars_Unity/TestResults.xml`（00:15:28Z，SHA256 `14262B5498DDF576704358A1757060F658454BB78474A2B682B56A5D20D27FE6`）。
- FieldAttack 现已覆盖真实 StandaloneInputModule 路径并保留 DAMAGE/HP/目标消失断言；这仍是自动化输入证据，不等于原生 OS 鼠标验收。Editor 已回到 ready；无 commit/push，全局 P0 仍不关闭。

## 🟡 [Codex → PL/QA] 统领目标可见性增量（2026-09-08）
- CardCatalog presentation metadata 现只读携带既有 `leaderDef.winText`；己方可见 LeaderZone 在既有统领槽显示“目标”，对手统领槽与对手隐藏手牌不渲染该文本。
- 本段记录的“512 与 200~500 待确认”已被 2026-09-08 人类决策替代；当前确认值为 `wood_leader` 的 `GIANT_HEALTH_GE=512`，本批未改数据数值。
- 证据：`RuntimeCardDisplayInspectEditModeTests` 9/9、`RuntimeBattlePanelStructureEditModeTests` 18/18、`RuntimeBattlePanelActionFeedbackEditModeTests` 31/31；本轮 Unity headless Test Runner 触发重新编译并通过。

## 🟡 [Lunar Max → PL/QA] 木/深海/机械统领规则冲突收尾（2026-09-08，已被后续决策替代）
- 后续人类决策已确认木阈值 **512** 与机械 B 模式；本段此前将二者标为 HUMAN_REQUIRED 的记录仅保留历史上下文，不代表当前状态。
- 深海仍保持弃牌轴；强制手牌上限弃牌不计入累计弃牌，未引入潮位或“印记”状态。精确“印记”语义尚未冻结。

## 🟡 [Codex → PL/QA] 木/机械 B 模式最小实现（2026-09-08）
- `docs/RULES.md` 已同步：木 `GIANT_HEALTH_GE=512` 为确认阈值；机械地标 B 模式第二层吟唱 1 后晋升 `machine_alpha`，Alpha `PULL_TOTAL_GE=6`；2026-09-09 进一步明确生命周期值是惩罚抽牌，不是支付费用。
- `data/cards/machine.json` 已把 `machine_leader` 接为机械地标 tier 1/2，把 `machine_alpha` 接为 `PULL_TOTAL_GE=6`；普通机械随从已按当前默认 COMMIT/PUSH/PULL 惩罚值 1/0/1 与 PULL 后选择己方存活随从 +1/+1 接入。
- 引擎新增地标实例独立 `LandmarkPullCount` / `PendingLandmarkSummonCardId`；LegalAction 与 Pull handler 生成并执行生命周期惩罚抽牌及 PULL 目标选择；EndPhase 扫描 LeaderZone 并沿用现有 `SummonLeader` 晋升路径；COMMIT→PUSH FIFO→CloudStack 栈顶 PULL 顺序未改。
- `docs/CARD_DESIGN_BASIC_SET_2026-08-15.md` 的 `machine_uploader`/`machine_compiler`/`machine_downloader` 仍属于尚未迁入本 91 卡池的专属重做描述；它们不覆盖当前正式普通机械随从默认值，也未把 fixture 当作正式卡完成。
- 深海“改成印记”继续 `HUMAN_REQUIRED`：当前 `data/cards/sea.json` 仍为弃牌 18，RULES 的潮位仍不自动并入弃牌计数；未改深海代码或数据。

## 🟢 [Codex → PL/QA] 卡面可读性 P0 收尾（2026-09-08）
- `RuntimeCardFaceView` 的 compact 场上随从优先显示可读名称、当前攻防与已投影关键词；普通未封印随从不再常驻显示技术状态，已封印随从显示“封印”。场上规则区保留关键词与“查看卡牌详情”提示；手牌/full 卡面及 inspect 仍保留详细规则与 PUNISH 预览。
- Unity connected Pipeline：显式 recompile `completed`、`failed=false`；`RuntimeCardFaceViewEditModeTests` **3/3 PASS**，`RuntimeCardFaceViewContractEditModeTests` **6/6 PASS**，包含 1280×720 与 1440×900 几何边界测试。原始 connected JSON 与 source/DLL hash 证据在被忽略的 `build-output/card-face-editmode.json`、`card-face-contract-editmode.json`、`card-face-evidence.json`；`card-face-editmode-connected.xml` 是由 connected 结果规范化的 NUnit-style XML，不是 headless 原生 XML。
- 另尝试原生 headless Test Runner，但因已连接 Editor 持有项目锁退出，未产生原生 XML；不把该失败误报为测试失败。
- 只读结构消融候选：`RuntimeBattlePanel.RenderDropZones` 对无具体卡牌目标的 PLAY_CARD/SET_AMBUSH/COMMIT/ROLLBACK action 会给整行 semantic root 加可射线 Image，可能在同时存在子卡 inspect/drag 时竞争 top-hit。当前仅记录为需视觉/top-hit 复核的候选，未改生产代码。

## 🟢 [Codex → PL/QA] 最小 CPU 对手闭环验证（2026-09-08）
- `RuntimeAiPolicy` 只消费 AI viewer（player 1）的 snapshot 与其完整 LegalActions；`RuntimeAiTurnCoordinator` 每次 `Pump` 最多提交一步，拒绝、过期、终局或 32 步上限即停止。`RuntimeAdapter` 保持 actor/viewer 分离，CPU 对手动作后的 presentation 固定回 player 0，不读取或渲染对手隐藏手牌。
- AI 专项 Unity 门禁合计 **8/8 PASS**：`RuntimeAiEditModeTests` **3/3**、`RuntimeAiPlayModeTests` **1/1**（每帧最多一步）、真实 `RuntimeBootstrap` + `RuntimeScreenFlow` 的 `RuntimeAiIntegrationPlayModeTests` **4/4**。集成覆盖 CPU toggle→Start、player 0 结束后 AI 至少一步并交回/终止、viewer 0 固定、terminal、rejected 不重试与 32 步安全上限。
- Editor 证据：Unity **6000.3.21f1** connected Editor（`127.0.0.1:7801`）显式 recompile `completed`、`failed=false`；汇总记录为 `build-output/runtime-ai-evidence-20260908.txt`，最近一次 connected PlayMode JSON 为 `unity/DominionWars.Unity/Temp/pipeline_test_status.json`（integration 4/4）。
- 曾尝试 standalone `unity test --output ...xml`，因同一项目已被 connected Editor 持有项目锁而 fail-closed；没有产生可引用的 standalone NUnit XML，不把不存在的 XML 当证据。connected JSON 与上述汇总文本可核验实际结果。
- 全仓最新 .NET 基线沿用机械批次记录的 **552/552**；本 AI slice 未把此前较旧的局部数字当作当前全仓基线，也未在本次文档收尾重跑全仓测试。无 commit/push。

## 🟢 [Codex → PL/QA] 牌桌层级与语义落点复验（2026-09-08）
- `RuntimeBattlePanelView` 现明确保持 opponent hand/status → opponent battlefield → shared castle → own battlefield → own hand/action rail → feedback/event overlay 的层级；语义无目标 action 使用独立空白 drop surface，位于对应卡片 strip 之后，避免整行 root 抢占卡牌 inspect/drag。卡片、规则、目标合法性、隐藏信息和 ScreenFlow/Setup 均未改。
- connected Unity `6000.3.21f1` 唯一正确项目，Editor/非 Play（PID `39452`, port `7801`）；显式 `recompile` 返回 `success=true/status=up_to_date/failed=false`，无警告/错误。
- `RuntimeBattlePanelStructureEditModeTests` **21/21 PASS**、`RuntimeCardFaceViewEditModeTests` **3/3 PASS**、`RuntimeCardFaceViewContractEditModeTests` **6/6 PASS**；合计 **30/30 PASS**，失败/跳过/不确定均为 0。结构与契约覆盖 1280×720、1440×900；具体用例名和执行时间见独立证据目录。
- 独立 XML 与证据清单（被忽略）：`build-output/unity-runtime-validation/20260908-card-layer-evidence/`；`runtime-battle-panel-structure-editmode.xml` SHA256 `FC1DAB31AB37D0E68A0F282FE44CEBA68B3CEBF9914CDB2F36EFA1B50F76CAFD`，`runtime-card-face-editmode.xml` SHA256 `2C568996C4002B1E8A4824C64EAE4E2D1315CCB54426A090D8ADAA7087DE8165`，`runtime-card-face-contract-editmode.xml` SHA256 `56FE51D28EDD5601329D7F83141948E6AE3D07321D044730CB2526B13EE28450`。
- 当前编译证据：`RuntimeBattlePanelView.cs` SHA256 `FC4B68DA58F94D993D17FC0F34CD69198AC596B873E27CCAB9C368515C952270`（13:20:46Z）；`RuntimeBattlePanel.cs` `5636171D1041682E6C50CDFE5E4A84D5C8FC27700F1BCD9F873704D3B063E7F5`（13:18:08Z）；`DominionWars.UI.dll` `222A1B8C35D8502180024D5251BF9B0B7900F8E35A9F22DA0467F699E2E4D41A`（13:22:51Z）；`DominionWars.Unity.EditMode.dll` `E54F33B9F1026FDD9A4A22F3E18D363AC014D7BC78C798C2A6AE57C0739B1F99`（13:22:53Z）。
- 本批未运行原生/headless runner；XML 明确标注为 connected Pipeline 结果规范化副本。无 commit/push。

## 🟢 [Lunar Max → PL/QA] 机械普通随从默认生命周期接通（2026-09-09）
- 语义已按用户澄清落地：正式 91 卡中未单独规定的普通机械 MINION 使用 COMMIT 惩罚值 `1`、PUSH 惩罚值 `0`、PULL 惩罚值 `1`；PULL 后在合法动作中选择一只己方存活随从 `+1/+1`。显式专属字段优先，未把 PUSH 时点效果误写成默认 buff，也未把生命周期值当支付资源。
- 8 张正式普通机械随从已显式写入 `data/cards/machine.json`（`commitCost=1`、`uploadCost=0`、`downloadCost=1`）；`CardCatalog` 对缺失字段提供同一默认，统领/非普通卡不套用。
- PULL 目标链复用 `SelectedEntityIds`：LegalAction 为每个合法己方存活随从生成独立 action variant，payload 带 `selectedEntityIds`；handler 在任何事件/区域变更前校验恰好一个合法目标；runtime 分离 carrier 与 buff target，不自动选、不随机。
- 验证：窄集 **73/73 PASS**（DataLoader、Commit/Pull、LegalActionGenerator、AdvancedEffect）；全量 Release .NET **558/558 PASS**，TRX `build-output/dotnet-tests/20260909-machine-punish/mechanical-punish-20260909.trx`；cards schema **91/91 PASS**、decks **4/4 PASS（91 cards）**、`git diff --check` PASS。未改 Unity UI/AI、无 commit/push。
- COMMIT/PULL 正惩罚值现通过既有 `DrawForPunish` 与响应链执行并继续动作；PUSH 默认不追加惩罚，显式 `uploadCost` 才在自动入云时点抽牌。未新增支付资源。

## 🟢 [Codex → PL/QA] ScreenFlow 入口 host 生命周期复验（2026-09-09）
- 已确认根因边界：从空/未保存 scene 进入 Play 时没有 `RuntimeBootstrap`，因此 TITLE 可见但 MATCH SETUP 无 deck rows；不是 Engine 或 deck data 缺失。
- `RuntimeScreenFlow` 的 Play-only fallback 只在找不到 host 时创建一个 scene-local `RuntimeBootstrap`，不创建 session；正式 `RuntimeBootstrap` scene 保持单 host，刷新不重复创建，Adapter 在 START MATCH 前保持为空。
- 新增入口 PlayMode 覆盖真实空 scene 创建/卸载：fallback 单实例、Adapter 为空；切回正式 scene 时 fallback 被回收并恢复单一正式 host。另以真实 `ExecuteEvents` pointer click 覆盖双方 deck 选择、CPU toggle、START；CPU toggle 不清空选择或 deck rows。
- Unity connected Editor `6000.3.21f1`（PID `30556`，Pipeline `7801`）：显式 recompile `completed`、`failed=false`、`errors=[]`。
- 结果：`RuntimeBootstrapPlayModeTests` **6/6 PASS**、`RuntimeAiIntegrationPlayModeTests` **4/4 PASS**、`RuntimeScreenFlowEditModeTests` **19/19 PASS**；失败/跳过/不确定均为 0。入口证据目录：`build-output/unity-runtime-validation/20260909-screen-flow-entry/`（connected Pipeline 规范化 XML，已更新至 6/6）。
- 原生桌面鼠标/前台 Player 仍未由本批验证；无 Engine/data 改动，无 commit/push。

## 🟢 [Codex → PL/QA] Unity 全量门禁整合（2026-09-09）
- 同一 connected Unity Editor `6000.3.21f1`（PID `30556`，Pipeline `7801`）完成显式 recompile：`up_to_date`，errors `0`、warnings `0`；`git diff --check` PASS。
- 全量 connected EditMode **269/270 PASS**（failed `1`、skipped `0`）：唯一失败为 `RuntimePullLifecycleFixtureEditModeTests.NonAuthoritativeFixtureCompletesCommitPushPullAndGraveyardMove`，fixture 期望 `CARD_PULLED`、实际 `BUFF_APPLIED`，属机械语义/fixture 范围，未改。
- 全量 connected PlayMode **23/24 PASS**（failed `1`、skipped `0`）：唯一失败为 `RuntimeFullMatchUserJourneyPlayModeTests.FullMatchCompletesThroughThePlayerFacingUi`，期望 `win.enemy_leader_defeated`、实际 `0 | win.pull_total_ge`，属 engine/data 胜负语义范围，未改。
- 要求切片均独跑通过：入口 `RuntimeBootstrapPlayModeTests` **6/6**、AI `RuntimeAiIntegrationPlayModeTests` **4/4**、拖拽 `RuntimeTargetDragEventSystemPlayModeTests` **4/4**、结构 `RuntimeBattlePanelStructureEditModeTests` **21/21**、卡面 `RuntimeCardDisplayInspectEditModeTests` **9/9**、反馈 `RuntimeBattlePanelActionFeedbackEditModeTests` **31/31**。
- 原始 connected 状态 JSON 与 verification manifest 已保存至被忽略目录 `unity/DominionWars.Unity/build-output/unity-runtime-validation/20260909-full-unity-gates/`；其中记录每个切片、失败详情及 source/DLL 时间与 SHA256。
- `scripts/run-unity-runtime-validation.ps1 -ValidateOnly` 返回 `projectOpen=True`；该脚本支持 Windows x64 build/player smoke，但因 connected Editor 持有 `Temp/UnityLockfile` 按 fail-closed 规则未启动 build、未删除锁、未启动第二个 Editor。native foreground mouse 与 Windows player smoke 仍未验证；无 commit/push。

## 🟢 [Codex → PL/QA] Unity stale-test 修复与全量复验（2026-09-09）
- 两个失败均确认为测试陈旧而非生产缺陷：U-03 非权威 fixture 漏断言批准的默认机械 PULL `BUFF(FRIENDLY_MINION,+1/+1)`；FullMatch 旧 row index 在新增 Machine deck 后实际走 Machine→Sea，`win.pull_total_ge` 是当前已批准统领条件。
- 最小改动仅限两个 Unity 测试：`RuntimePullLifecycleFixtureEditModeTests` 期望 `PULL_DECLARED → BUFF_APPLIED → CARD_PULLED`；`RuntimeFullMatchUserJourneyPlayModeTests` 通过稳定渲染 ID 选择 `machine_deck`/`sea_deck`、断言选择结果，并期望 `win.pull_total_ge`，保留 COMMIT/PULL 覆盖。未改 `src/`、`data/`、规则或生产 UI。
- 显式 recompile `failed=false/errors=[]`；失败专项 U-03 **1/1 PASS**、FullMatch **1/1 PASS**。全量 connected EditMode **270/270 PASS**、PlayMode **24/24 PASS**；failed/skipped/inconclusive 均为 0，测试命令 warnings `0`。
- 完整 JSON、专项 JSON、source/DLL 时间与 SHA256 已保存至被忽略目录 `unity/DominionWars.Unity/build-output/unity-runtime-validation/20260909-failure-repair/verification-manifest.md`。`git diff --check` PASS；无 commit/push。

## 🟢 [Codex → PL/QA] connected Windows x64 build 与 Player smoke（2026-09-09）
- 复用已连接的 Unity `6000.3.21f1` Editor（PID `30556` / Pipeline `7801`）执行 Pipeline `build`；未关闭 Editor、未删除锁、未启动第二个 Editor。现有 `scripts/run-unity-runtime-validation.ps1` 仍按 open-project fail-closed，未绕过。
- `StandaloneWindows64` dry-run `valid=true`；正式 build `build_77fd7c52fc77` **Succeeded**，`totalErrors=0`、`totalWarnings=486`、`buildTimeMs=60063`、报告 `totalSizeBytes=128996161`。输出目录与 Player 可读。
- headless Player smoke 日志达到 `Dominion Wars runtime screen flow ready: TITLE shell active.`，无匹配运行时异常；Player 不响应 `-quit`，确认精确可执行路径后只停止本次 smoke 进程，因此不宣称自然 exit code。`-nographics` 无截图/原生鼠标验收。
- 证据：`unity/DominionWars.Unity/build-output/unity-runtime-validation/20260909-connected-player-smoke/build-smoke-manifest.md` 与同目录 `player-smoke.log`。connected Editor 结束时仍 `ready`，无 smoke Player 残留；无 commit/push。
- warning 归类：486 条均为 P2（460 条 `ConvGeneric.compute` + 25 条 `com.unity.ai.inference` Sentis PixelShaders + 1 条 Unity Pipeline Player-disabled 提示）；无 `Assets/` 项目源码 warning，P0/P1 为 0。脚本静态解析 0 error；`-ValidateOnly` 仍以 exit 2 / `projectOpen=True` fail-closed。
- `scripts/run-unity-runtime-validation.ps1` 已最小兼容既有 bootstrap marker 与当前 TITLE screen-flow marker，并在结果中报告实际匹配 marker；未改运行行为。
