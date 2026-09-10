# Codex AI 对手最小闭环收尾报告（草案）

状态：`DRAFT / PENDING_EXTERNAL_PL_QA`

日期：2026-09-08

本文件是 Codex 实现交接与外部审核的事实汇总，不代表 DeepSeek PL 或 DeepSeek QA 已审核，也不代表 Unity 全局 P0 或最终实机发布门禁关闭。

## 1. 范围

本批实现的是可选 CPU 对手的最小闭环：AI 只读取 player 1 的 viewer-safe snapshot 与该 snapshot 广告的完整 `LegalActions`；人类 presentation 固定 player 0；每次 Pump 最多一个动作；rejected、过期、终局和 32 动作上限 fail-closed 停止。规则、卡牌数据、难度和付费服务未新增。

主要文件边界：

- `unity/DominionWars.Unity/Assets/DominionWars.Runtime/RuntimeAiPolicy.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.Runtime/RuntimeAiTurnCoordinator.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.Runtime/RuntimeAdapter.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.Runtime/RuntimeBootstrap.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeScreenFlow.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeScreenShellView.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.EditMode/RuntimeAiEditModeTests.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.PlayMode/RuntimeAiPlayModeTests.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.PlayMode/RuntimeAiIntegrationPlayModeTests.cs`

## 2. 已验证证据

- Unity connected Editor：6000.3.21f1，显式 recompile `completed`，`failed=false`，无编译错误。
- AI 专项 Unity 门禁合计 **8/8 PASS**：`RuntimeAiEditModeTests` 3/3、`RuntimeAiPlayModeTests` 1/1、真实 `RuntimeBootstrap` + `RuntimeScreenFlow` 集成 4/4。
- 集成覆盖 CPU toggle→Start、player 0 结束后 AI 至少一动作并交回或终止、viewer 0 固定、terminal、rejected 不重试、32 动作安全上限。
- 汇总证据：`build-output/runtime-ai-evidence-20260908.txt`。
- 最近一次 connected PlayMode JSON：`unity/DominionWars.Unity/Temp/pipeline_test_status.json`（集成 4/4）。
- 本 AI slice 当时未重跑全仓 .NET，沿用的是机械批次的历史快照 **552/552**；当前全仓 Release 基线已由后续机械批次验证为 **558/558**（见 §10），不得把 552/552 当作当前结果。

## 3. 尚未完成或待实机

- DeepSeek V4 Flash PL 规划、DeepSeek V4 Pro QA 和 PL final review 尚未执行；本文件不宣称外部审核已读或已通过。
- standalone NUnit XML 未产生：独立 `unity test --output ...xml` 因 connected Editor 持有同一项目锁而 fail-closed。不得引用不存在的 XML；connected JSON 与上述汇总文本才是本批现有证据。
- 尚未以独立 Windows Player/前台原生鼠标路径证明最终视觉、原生 OS 鼠标和全局 P0 闭环；这些仍是实机/人工门禁。
- 两个实现批次的审核目标已压缩为 `docs/DAILY_GOAL.md`（3,837 字符，`Status: READY`）；历史原文完整保存在 `docs/DAILY_GOAL_ARCHIVE_2026-09-08.md`，归档 SHA256 为 `985EF9927421ECBA696E2CFE2788B2C4120615C3B48A5B33BE11530763A782A6`。

## 4. Relay/provider 预检

- 旧长 goal 上执行的 `scripts/auto-relay/start-relay.ps1 -ValidateOnly` 与 `scripts/nightshift/run-nightshift.ps1 -SandboxPreflightOnly` 均未调用模型，并因 20,000 字符限制在 STARTUP fail-closed。
- 压缩后的 `start-relay.ps1 -ValidateOnly` 已越过长度与 `READY` 检查，但随后因 `.nightshift/rehearsal` 分支不包含当前 `main` 而 fail-closed；未同步、合并、改写或提交该分支。
- 早期预检快照显示：`.nightshift/rehearsal` 不 clean，保留未提交 `docs/NIGHT_REPORT.md`；当时 `main=6c9ef65`、`agents/nightshift-rehearsal=9d4a13d`，共同祖先为 `4767475a`。该历史快照不代表当前 review snapshot；本次没有执行 stash、merge、reset、branch 移动或替代 worktree 创建。
- 当前配置固定为官方 DeepSeek endpoint：PL=`deepseek-v4-flash`，QA=`deepseek-v4-pro`；MiniMax 不在本审核路径。
- relay disabled marker 当前为 false；受保护 DeepSeek credential 存在，ACL 仅 SYSTEM、Administrators 和当前用户。没有发起网络 provider probe，也没有消耗额度。
- 当前 `docs/DAILY_GOAL.md` 顶部状态已为 `READY`；正式 relay 仍受 isolated branch ancestry 门禁阻塞，且没有 provider call。

## 5. 正式审核入口（待条件满足）

条件满足后，只能按审计入口执行：先重新运行 `start-relay.ps1 -ValidateOnly`，再由人类批准的 `Status: READY` 目标进入 `-PlanOnly -ApprovedByHuman`；计划明确批准后，才允许真实 DeepSeek V4 Flash PL / V4 Pro QA 审核。不得用 MiniMax、VS Code 普通聊天或 mailbox 文本冒充 PL/QA 结果。

本批无 commit、无 push、无生产代码回滚。

## 6. 官方审核通道与 dirty diff 边界

- 仓库提供的正式 DeepSeek 路径只有 `scripts/auto-relay/start-relay.ps1` → `scripts/nightshift/start-day-shift.ps1` → isolated `agents/nightshift-*` worktree → `run-nightshift.ps1`。没有 `ReviewOnly`、`PatchFile` 或直接把当前 root dirty diff 传给 PL/QA 的受支持参数。
- `-PlanOnly` 只调用 PL 并保存 hash-bound pending plan；`-ApprovedPlan` 才进入 Codex、allowlisted tests、DeepSeek V4 Pro QA 和最终 PL review。两者都要求 clean isolated worktree、`main` ancestry、control-plane 文件一致、固定 branch/HEAD/goal hash；不能用普通聊天或 mailbox 文本替代。
- 只读 `scripts/capture-task-scope.ps1` 记录当前 root：branch `codex/p0-complete-match-loop-2026-09-06`、HEAD `880250cc`、dirty paths **107**；AI 目标文件与收尾报告均与 dirty set 重叠。该脚本只生成路径/hash 盘点，不能执行 provider review。

### 若 owner 要求审核当前两批实现，最小可恢复步骤（本轮未执行）

1. 在 `.nightshift/rehearsal` 外保存并核验现有未提交 `docs/NIGHT_REPORT.md` 的完整副本/patch；不要直接丢弃它。
2. 仅在 owner 明确允许本地 checkpoint commit 后，清空该 worktree 的脏状态；在 `agents/nightshift-rehearsal` 上以 `main` 为合并源。当前 `main=6c9ef65` 与 `agents/nightshift-rehearsal=9d4a13d` 分叉，不能 fast-forward；`git merge-tree` 显示内容无冲突，但必须有本地 merge commit。
3. 同一 checkpoint 需使 `AGENTS.md`、`docs/AI_WORKFLOW.md`、`.github/agents/**` 与 `main` 完全一致；当前 control diff 为 `AGENTS.md`、`docs/AI_WORKFLOW.md` 及缺失的 `.github/agents/codex.agent.md`，否则 relay 仍会拒绝。
4. checkpoint 只纳入 owner 明确指定的实现路径，不纳入 design 概念图、QA 截图、`docs/AI_MAILBOX*` 历史、包锁/ProjectSettings 或其他代理脏项。已知 AI 批次最小路径为本报告 §1 列出的 Runtime/ScreenFlow/AI tests；机械批次如需同审，再单独确认 `data/cards/machine.json` 与对应 `src/Engine`/测试文件清单，不应把整个 root dirty tree 打包。
5. checkpoint 后重新执行 `start-relay.ps1 -ValidateOnly`；只有 ancestry、clean worktree、control diff 和 goal hash 门禁均通过，才可由 owner 继续 `-PlanOnly -ApprovedByHuman`，再明确批准 pending plan 后进入真实 V4 Pro QA。上述步骤不会自动包含当前 root 的未提交改动，除非它们被明确纳入该 checkpoint。

## 7. 本次审核 snapshot 清单（精确路径）

以下是从当前 root `HEAD=880250cc522a155440953fc6857fe5009c67fb8f` 的 dirty diff 中筛出的最小审查集合；它不是对 107 个 dirty path 的整体打包，也不是新的完成声明。来源标签只表示本轮交接批次，不冒充 Git commit：`AI-CPU`、`P0-UI`、`P0-MECHANICAL`、`RULES/GOAL/REPORT`。

唯一文件数为 **50**：AI-CPU 14 个（其中 4 个是与 UI 入口共享的现有 Runtime 文件）、P0-UI 独有 15 个、P0-MECHANICAL 18 个、规则/目标/报告 3 个。共享文件只在总数中计算一次；新增 Unity `.meta` 与其对应 `.cs` 一并保留。

### AI-CPU（14）

- `unity/DominionWars.Unity/Assets/DominionWars.Runtime/RuntimeAiPolicy.cs`（AI-CPU）
- `unity/DominionWars.Unity/Assets/DominionWars.Runtime/RuntimeAiPolicy.cs.meta`（AI-CPU）
- `unity/DominionWars.Unity/Assets/DominionWars.Runtime/RuntimeAiTurnCoordinator.cs`（AI-CPU）
- `unity/DominionWars.Unity/Assets/DominionWars.Runtime/RuntimeAiTurnCoordinator.cs.meta`（AI-CPU）
- `unity/DominionWars.Unity/Assets/DominionWars.Runtime/RuntimeAdapter.cs`（AI-CPU；与 P0-UI 共享）
- `unity/DominionWars.Unity/Assets/DominionWars.Runtime/RuntimeBootstrap.cs`（AI-CPU；与 P0-UI 入口共享）
- `unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeScreenFlow.cs`（AI-CPU；与 P0-UI 入口共享）
- `unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeScreenShellView.cs`（AI-CPU；与 P0-UI 入口共享）
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.EditMode/RuntimeAiEditModeTests.cs`（AI-CPU）
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.EditMode/RuntimeAiEditModeTests.cs.meta`（AI-CPU）
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.PlayMode/RuntimeAiPlayModeTests.cs`（AI-CPU）
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.PlayMode/RuntimeAiPlayModeTests.cs.meta`（AI-CPU）
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.PlayMode/RuntimeAiIntegrationPlayModeTests.cs`（AI-CPU）
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.PlayMode/RuntimeAiIntegrationPlayModeTests.cs.meta`（AI-CPU）

### 已批准 P0 UI／卡面／入口（独有 15）

共享入口 Runtime 文件已经列在 AI-CPU 小节，以下只列该批独有路径：

- `src/Data/CardCatalog.cs`（P0-UI 卡牌数据投影）
- `src/Data/CardPresentationMetadata.cs`（P0-UI 卡面可读元数据）
- `unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeBattleCardDrag.cs`（P0-UI）
- `unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeBattlePanel.cs`（P0-UI）
- `unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeBattlePanelActionFeedback.cs`（P0-UI）
- `unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeBattlePanelView.cs`（P0-UI）
- `unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeCardDisplayModel.cs`（P0-UI）
- `unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeCardFaceView.cs`（P0-UI）
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.EditMode/RuntimeBattlePanelActionFeedbackEditModeTests.cs`（P0-UI）
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.EditMode/RuntimeBattlePanelStructureEditModeTests.cs`（P0-UI）
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.EditMode/RuntimeCardDisplayInspectEditModeTests.cs`（P0-UI）
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.EditMode/RuntimeCardFaceViewContractEditModeTests.cs`（P0-UI）
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.EditMode/RuntimeCardFaceViewEditModeTests.cs`（P0-UI）
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.PlayMode/RuntimeBootstrapPlayModeTests.cs`（P0-UI 入口）
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.PlayMode/RuntimeTargetDragEventSystemPlayModeTests.cs`（P0-UI 输入路径）

### 已批准机械 data／engine／tests（18）

- `data/cards/machine.json`（P0-MECHANICAL 数据）
- `data/schema/cards.schema.json`（P0-MECHANICAL schema）
- `src/Engine/Effects/EffectRuntime.EndPhase.cs`（P0-MECHANICAL）
- `src/Engine/Effects/EffectRuntime.Mechanical.cs`（P0-MECHANICAL）
- `src/Engine/LegalActionGenerator.cs`（P0-MECHANICAL 合法行动）
- `src/Engine/Model/CardDefinition.cs`（P0-MECHANICAL 模型）
- `src/Engine/Model/CardInstance.cs`（P0-MECHANICAL 模型）
- `src/Engine/Model/LandmarkTierDefinition.cs`（P0-MECHANICAL 模型）
- `src/Engine/Turns/CommitActionHandler.cs`（P0-MECHANICAL）
- `src/Engine/Turns/PlayCardActionHandler.cs`（P0-MECHANICAL）
- `src/Engine/Turns/PullActionHandler.cs`（P0-MECHANICAL）
- `src/Engine/Turns/TurnActionRouter.cs`（P0-MECHANICAL）
- `src/Engine/Tests/AdvancedEffectTests.cs`（P0-MECHANICAL）
- `src/Engine/Tests/AttackActionHandlerTests.cs`（P0-MECHANICAL）
- `src/Engine/Tests/CardEffectSpecEditorTests.cs`（P0-MECHANICAL）
- `src/Engine/Tests/CommitActionHandlerTests.cs`（P0-MECHANICAL）
- `src/Engine/Tests/DataLoaderTests.cs`（P0-MECHANICAL）
- `src/Engine/Tests/PullActionHandlerTests.cs`（P0-MECHANICAL）

### 规则／目标／报告（3）

- `docs/RULES.md`（当前规则语义，只用于审核对照）
- `docs/DAILY_GOAL.md`（当前 `Status: READY` 目标）
- `docs/CODEX_AI_CLOSEOUT_REPORT_2026-09-08.md`（本次事实汇总与门禁状态）

### 明确排除项

- `design/concepts/**`、`docs/art-direction-2026-09-06/**`、`docs/DESIGN_SEA_MACHINE_FINAL_2026-09-08.md`、`docs/VISUAL_PRODUCTION_GOAL_2026-09-08.md`：视觉概念/方向，不属于本次实现审查快照。
- `unity/DominionWars.Unity/Assets/QA/**`、截图/录屏和 `build-output/**`、`unity/DominionWars.Unity/Temp/**`：QA 素材或运行时证据；报告只引用已验证证据路径，不把生成物伪装成源代码提交。
- `docs/DAILY_GOAL_ARCHIVE_2026-09-08.md`：已保留的历史归档，不进入审查 patch；`docs/AI_MAILBOX*`、`docs/CURRENT_STATE*` 及其他历史/异步记录同样不进入本快照。
- `scripts/sanity_check.py` 的删除、无关代理 dirty paths、`src/Data/DeckEditor.meta`、ContentStudio/Resources 等无关 `.meta` 不进入。
- `unity/DominionWars.Unity/Packages/manifest.json`、`packages-lock.json`、`ProjectSettings/EditorBuildSettings.asset`、`ProjectSettings/ProjectSettings.asset` 及其他 package/project settings 暂不进入；目前没有证据表明本批编译依赖它们。若后续 Unity 编译明确报出依赖，必须单独记录原因并重新收敛清单。

## 8. 依赖与可恢复 isolated branch 重建方案（设计，未执行）

依赖关系已按最小边界核对：AI 测试需要现有 `RuntimeAdapter`/`RuntimeBootstrap`/`RuntimeScreenFlow`/`RuntimeScreenShellView` 与新 `.meta`；P0 UI 卡面测试需要 `src/Data` 两个投影文件和 UI 六个实现文件；机械测试需要 `machine.json`、schema、上述 engine handler/model 与六个测试文件。规则文档只作语义对照，不复制规则；未证实的 package/project settings 不纳入。

待 owner 明确允许执行本地 checkpoint 后，建议按以下可恢复顺序重建，任何一步出现冲突都停止，不使用 reset/force push：

1. 在 relay worktree 外建立一次性备份目录。先保存 `.nightshift/rehearsal/docs/NIGHT_REPORT.md` 的完整副本，并用 `git -C .nightshift/rehearsal diff --binary -- docs/NIGHT_REPORT.md` 保存 patch；逐字节校验后，才创建本地 `archive/nightshift-rehearsal-20260909` 分支指向当前 `agents/nightshift-rehearsal`。本轮没有执行这些写操作。
2. 确认备份可读后，在 `.nightshift/rehearsal` 恢复 `docs/NIGHT_REPORT.md` 到其当前分支基线，使 worktree clean；不删除外部副本。以 `main=6c9ef650...` 为合并源执行 `merge --no-ff --no-commit`，检查 `git merge-tree`/工作树内容；当前读证据显示无内容冲突，但因分叉不能 fast-forward。
3. 合并暂存阶段只把 `AGENTS.md`、`docs/AI_WORKFLOW.md`、`.github/agents/**` 对齐到 `main`，再核对 control-path diff 为空，创建一个本地 baseline merge commit。不得触碰 root dirty worktree；当前 root `HEAD=880250cc...` 保持不变。
4. 从当前 root 生成仅覆盖第 7 节 50 条路径的二进制 patch；另行复制其中新增的 `.cs`、`.meta`、报告文件，明确不读取/带入其他 dirty paths。为避免修改 root，先在临时本地 worktree 以当前 root `HEAD` 建立 snapshot 分支，应用该精确 patch、逐路径核对 `git status` 与 50 条 manifest，再创建一个 local review snapshot commit。
5. 将该 snapshot commit cherry-pick 到已同步的 isolated relay branch；若 cherry-pick 产生任何冲突，立即 abort 并报告具体路径，不强行覆盖。成功后只允许 `git diff --name-only` 命中第 7 节清单和 relay 自身允许的报告状态，创建最终 local review commit；不 push、不改 remote。
6. 重新运行 `start-relay.ps1 -ValidateOnly`。只有 isolated branch ancestry、clean worktree、control hashes、goal hash 全部通过，才可以在 owner 明确批准后进入 `-PlanOnly -ApprovedByHuman`；本步骤本身仍不调用 provider。NIGHT_REPORT patch/archive 的恢复步骤必须在审核结束后按需反向应用并再次校验。

## 9. Unity stale-test 修复复验（2026-09-09）

- 两个全量门禁失败均确认为 stale test：U-03 fixture 未反映批准的普通机械默认 PULL `BUFF(FRIENDLY_MINION,+1/+1)`；FullMatch 旧 row index 在四 deck 排序中实际走 Machine→Sea，当前终局 `win.pull_total_ge` 与已批准 Machine 统领条件一致。
- 仅修改两个 Unity 测试：U-03 期望 `PULL_DECLARED → BUFF_APPLIED → CARD_PULLED`；FullMatch 通过稳定渲染 ID 选择 `machine_deck`/`sea_deck`、断言选择结果，并期望 `win.pull_total_ge`，保留 COMMIT/PULL 覆盖。未修改 engine、data、rules 或生产 UI。
- Connected Unity `6000.3.21f1` 显式 recompile `failed=false/errors=[]`；失败专项 U-03 **1/1 PASS**、FullMatch **1/1 PASS**；全量 EditMode **270/270 PASS**、PlayMode **24/24 PASS**，failed/skipped/inconclusive 均为 0，测试命令 warnings `0`。
- 证据与 source/DLL 时间/hash：`unity/DominionWars.Unity/build-output/unity-runtime-validation/20260909-failure-repair/verification-manifest.md`。Windows x64 build、native foreground mouse、独立 Player smoke 本轮未验证，不作通过声明；无 commit/push。本轮未修改 relay isolated worktree。

## 10. 最终交棒事实（2026-09-09）

本报告仍为 **DRAFT / PENDING_EXTERNAL_PL_QA**；以下是交给 DeepSeek PL/QA 的当前事实，不是全局 P0 关闭声明：

- 当前全仓 Release .NET **558/558 PASS**；Cards **91/91**、Decks **4/4（91 cards）**。对应 TRX：`build-output/dotnet-tests/20260909-machine-punish/mechanical-punish-20260909.trx`。
- Connected Unity `6000.3.21f1` recompile `failed=false`，errors/warnings 均为 `0`；U-03 **1/1**、FullMatch **1/1**、全 EditMode **270/270**、全 PlayMode **24/24**，无 failed/skipped/inconclusive。两项失败均为 stale test，仅修测试 fixture/稳定选择，不是 engine/data/rules/生产 UI 修复；证据见 `unity/DominionWars.Unity/build-output/unity-runtime-validation/20260909-failure-repair/verification-manifest.md`。
- Windows x64 build、独立 Player smoke、前台 native mouse 仍未验证；PlayMode 拖拽自动路径不能替代原生鼠标验收。Deep Sea“印记”继续 **HUMAN_REQUIRED**：当前 data 仍为弃牌 18 轴，没有印记/潮位动作或数值。
- 机械当前默认语义为 COMMIT 惩罚值 1、PUSH 默认 0、PULL 惩罚值 1，PULL 选择己方存活随从 `+1/+1`；待 PL/QA 额外复核两个条件性 P1：显式正 `uploadCost` 的 response/终局保护，以及 response 使 COMMIT source/PULL carrier 失效时 handler 仍 accepted/推进 revision。当前正式数据无正 `uploadCost`，两项均非当前数据 blocker。
- root `HEAD=880250cc522a155440953fc6857fe5009c67fb8f` 未变；relay review snapshot 为 `agents/nightshift-rehearsal@90f9c62cb2801b699dfe52dca2b680dd6f3be3e4`。`-PlanOnly` 因 `SANDBOX_PREFLIGHT` 60 秒超时未进入 provider，paid attempts/tokens 均为 `0`；不得把 mailbox 或普通聊天当作外部 PL/QA 结果。

## 11. connected Windows x64 build / Player smoke（2026-09-09）

- 在已运行的 connected Unity `6000.3.21f1` Editor（PID `30556`, Pipeline `7801`）使用 Pipeline `build` 完成 `StandaloneWindows64` 构建；未关闭 Editor、未删除锁、未启动第二个 Editor。现有独立脚本仍因项目锁按 fail-closed，不作绕过。
- dry-run `valid=true`；build `build_77fd7c52fc77` **Succeeded**，`totalErrors=0`、`totalWarnings=486`、`buildTimeMs=60063`、`totalSizeBytes=128996161`。输出与完整报告摘要见 `unity/DominionWars.Unity/build-output/unity-runtime-validation/20260909-connected-player-smoke/build-smoke-manifest.md`。
- 新 Player 以 `-batchmode -nographics` 启动并写入 `player-smoke.log`，达到 `Dominion Wars runtime screen flow ready: TITLE shell active.`，没有匹配的运行时异常。Player 不因 `-quit` 自行退出；确认精确 exe 路径后停止该 smoke 进程，不声称自然 exit code。因 Null graphics 无截图，native foreground mouse 仍未验收。
- 本项只产生被忽略的 build-output 证据，不改 gameplay/rules/data；无 commit/push。

### 11.1 warning 归类与脚本兼容

- BuildReport `486` 条 warning 全部归为 P2：`460` 条来自 `com.unity.ai.inference@9a123aee5df7` 的 `ConvGeneric.compute`、`25` 条来自同包 Sentis PixelShaders、`1` 条 Unity Pipeline “Player builds disabled”提示；未发现 `Assets/` 项目源码 warning，P0/P1 均为 `0`。
- `scripts/run-unity-runtime-validation.ps1` 最小接受两个真实 ready marker：既有 `Dominion Wars runtime bootstrap ready.` 与当前 TITLE 的 `Dominion Wars runtime screen flow ready: TITLE shell active.`；只改检测文案/结果标记，不改运行行为。ParserErrors `0`；当前锁存在时 `-ValidateOnly` exit `2`、`projectOpen=True`，未绕过。
- 本报告仍保持 **DRAFT / PENDING_EXTERNAL_PL_QA**；本地 build/smoke 证据不替代外部 PL/QA，也不宣称 native mouse 或自然 Player exit 通过。

## 12. Unity Wood/Machine leader 信息可读性复验（2026-09-09）

- 本批只改 Unity Runtime/UI 与对应测试：`RuntimeBootstrap.cs`、`RuntimeBattlePanel.cs`、`RuntimeBattlePanelPresentationModel.cs`、`RuntimeCardInspectModel.cs`、`RuntimeScreenFlow.cs`、`RuntimeScreenShellView.cs`；setup deck row、公开 LeaderZone、leader card inspect、终局结果均沿现有公开 metadata 投影显示 leader 名称/目标，不读取对方隐藏手牌，也不复制 512/6 常数或原始 `win.*` reason token。对应测试覆盖 `RuntimeCardDisplayInspectEditModeTests`、`RuntimeBattlePanelStructureEditModeTests`、`RuntimeBootstrapEditModeTests`、`RuntimeScreenFlowEditModeTests`、`RuntimeFullMatchUserJourneyPlayModeTests`。
- 正确 connected Unity `6000.3.21f1` 完成显式 recompile：`failed=false`，errors/warnings 均为 `0`。本轮程序集证据：`DominionWars.UI.dll` SHA256 `5DBFE95E49C4C7C602D1A157817DA1CD00F7BCEB113A17F71561EC63F570AC20`，`DominionWars.Runtime.dll` SHA256 `B258084995D5A86FD7DB3D4279253C0C9A91713B3141C2200AD5C6B0C5317846`。
- 当前连接 Editor 全量 EditMode **271/271 PASS**、PlayMode **24/24 PASS**，failed/skipped/inconclusive 均为 `0`。证据：`unity/DominionWars.Unity/build-output/ui-wood-machine-editmode-20260909-103455.json`、`unity/DominionWars.Unity/build-output/ui-wood-machine-playmode-20260909-103612.json`；Editor 已退出 Play（`isPlaying=false`），`git diff --check` PASS。
- 非本批声明：Windows build/Player smoke 的既有证据不替代前台 native mouse；native OS mouse 拖拽仍未验收。PULL/VICTORY_PROGRESS event rail 仍列为 P1（既有 overlay 的 `CARD_PULLED` 不等于完整 rail）；Deep Sea“印记”仍 **HUMAN_REQUIRED**，本批未改规则/data/engine。报告继续保持 **DRAFT / PENDING_EXTERNAL_PL_QA**，不宣称全局 P0/native mouse 已关闭。

## 13. 回合与结算反馈队列复验（2026-09-09）

- 针对“结束回合后只看到手牌变多”的可读性缺口，Runtime UI 现在按 authoritative snapshot revision 排队显示回合开始/结束、抽牌、玩家切换、伤害/治疗、随从被击败与终局提示；稳定事件 ID 去重，保留同 revision 的伏击/城破/终局优先级，并支持跳过与 Reduced Motion。提示只使用权威 amount/count/公开 target 名称，不显示隐藏卡牌或原始 entity ID；动画队列不改变 adapter、规则或输入推进。
- Adapter 与 UI event schema 仅新增 `CARDS_DRAWN`、`MINION_DESTROYED` 的公开映射/白名单；schema 为同一运行时包的 additive extension，旧客户端对未知事件仍 fail-closed，发布前需由 PL/QA 复核兼容性。
- Connected Unity `6000.3.21f1` 显式 recompile `failed=false`；反馈 EditMode **41/41 PASS**、全量 EditMode **281/281 PASS**、新增反馈 PlayMode **1/1 PASS**、全量 PlayMode **25/25 PASS**，failed/skipped/inconclusive 均为 `0`。Release .NET **559/559 PASS**，`git diff --check` PASS。
- 证据与 source/DLL 时间/hash：`unity/DominionWars.Unity/build-output/unity-runtime-validation/20260909-feedback-queue/verification-manifest.md`（同目录 raw JSON）。本轮无 commit/push；Windows build、独立 Player/native foreground mouse 与外部 PL/QA 仍 pending，不作通过声明。

## 14. 最新 Player 像素证据与未关闭的画面 P0（2026-09-09）

- 在 §13 源码之后通过同一 connected Editor 重新构建 `StandaloneWindows64`：build `build_e035e7ce7641` **Succeeded**，errors `0`、warnings `1`（既有 Pipeline 提示）。输出位于 `unity/DominionWars.Unity/build-output/unity-runtime-validation/20260909-latest-visual-smoke/`。
- 图形 Player 以 1280x720 和 1440x900 分别启动，显式写出 `battle-visible-1280x720.png`、`battle-visible-1440x900.png`，日志均记录匹配 framebuffer 尺寸；标题另有 `title-visible-1280x720.png`。三个 Player 均在捕获后自然 exit `0`。隐藏窗口首次捕获按预期失败且没有生成 PNG，未被计为通过证据。
- 实际像素审阅发现自动测试未覆盖的 P0：手牌卡面标题/规则文本在两种批准分辨率下仍过密且对比不足，六张重叠手牌不能快速辨认；初始未显现统领时，统领槽只剩通用 `LEADER` 占位，玩家无法从战场面板确认当前卡组目标。两项均不得被 281/281、25/25 或截图成功掩盖，需作为下一批 UI 可读性修复与像素复验的验收项。
- 当前像素证据证明标题与对局 framebuffer 可生成，不等于 native OS 鼠标拖拽通过；Match Setup 与 Result 尚无同等像素截图。报告继续保持 **DRAFT / PENDING_EXTERNAL_PL_QA**。

## 15. Wood/Machine 生产数据 Engine/Adapter 可达性（2026-09-09）

- 修复 `src/Engine/Turns/CardTargetValidator.cs` 的 P0 可达性缺口：为 `FRIENDLY_MINION` 生成并解析稳定目标 ID；不改变效果数值、规则或数据。此前正式 `wood_growth` 经 LegalAction 无法得到己方目标动作。
- 新增 `src/Engine/Tests/ProductionFactionIntegrationTests.cs`：`ProductionWoodDeckManifestsLeaderGrowsAndWinsThroughTheAdapterBoundary` 覆盖正式 `wood_deck`/`wood_leader` 显现、连乘成长至 512、成长封印后失去卡名关键词/攻击资格、`GIANT_HEALTH_GE` 即时终局及 Adapter 投影；`ProductionMachineDeckCompletesCommitPushPullLandmarkAndAlphaVictory` 覆盖正式机械牌 COMMIT 惩罚 1、FIFO PUSH 默认 0、LIFO PULL 惩罚 1、选择存活己方随从 +1/+1 入墓、地标 tier2→Alpha 与 `PULL_TOTAL_GE=6`。
- 窄测 **2/2 PASS**；`dotnet test DominionWars.sln --nologo --no-restore -c Release -m:1 /nodeReuse:false` 全量 **561/561 PASS**，0 failed、0 skipped；`git diff --check` PASS。
- 静态自查：友方目标候选只枚举当前玩家 `Field` 且要求 `IsMinion && IsAlive`；因此不含统领、敌方实体或死亡随从，解析阶段复用同一候选集并按稳定 ID 精确匹配。
- 本批未改 `data/`、Unity/UI、Deep Sea 或规则文档；无 commit/push。外部 PL/QA、native mouse 与现有画面 P0 仍按前述状态 pending。

## 16. Canonical event P1 修复与独立复验（2026-09-09）

- 完成 canonical event 两项 P1 修复：`CARD_PULLED.count` 改为单事件数量，不再暴露累计 `pullCount`；`GAME_OVER` 严格校验根/data `reasonKey`、`phase=OVER` 与 `winnerPlayerIndex`。
- 当前验证汇总：全量 .NET **592/592 PASS**；Unity EditMode **287/287 PASS**；PlayMode **25/25 PASS**；独立复验 Adapter/Cursor **53/53 PASS**、Outcome **11/11 PASS**。
- 已知 P2 未处理：游标原子性、事件间隙；本批无 DeepSeek 调用、无 commit/push，外部 PL/QA 仍为 **DRAFT / PENDING_EXTERNAL_PL_QA**。

## 17. UI 可读性复验与 native mouse 状态（2026-09-09）

- v7 手牌批次完成紧凑卡面可读性修复：卡名、费用/惩罚、类型/摘要、攻防与拖拽标记保持在卡内；针对性 EditMode/PlayMode 验证为 CompactFace **2/2**、OwnHand **4/4**、HandGeometry **2/2**、重叠手牌 **1/1**、紧凑卡面 **1/1**。截图：`build-output/latest-visual-smoke-20260909-v7/battle-1280x720.png`、`build-output/latest-visual-smoke-20260909-v7/battle-1440x900.png`。
- v9 统领信息批次沿现有公开 deck/catalog metadata 显示真实双方统领名与目标摘要，移除 `NAME` 占位并限制长名/目标摘要布局；`PublicLeaderSlotsShowCatalogNameAndGoalWithoutRevealingOpponentHand` **1/1 PASS**。build `build_db12d2e40ea9` **Succeeded**、errors **0**（486 条均为 shader/Pipeline warnings）。截图：`build-output/latest-visual-smoke-20260909-v9/battle-1280x720.png`、`build-output/latest-visual-smoke-20260909-v9/battle-1440x900.png`；两种分辨率人工复核通过，无越界或相邻卡溢出。
- native OS mouse 仍未闭环：当前仅旧 v7 达到 Title→Setup 的 **PARTIAL** 证据；v9 仍在重试，PlayMode/截图/按钮路径不能替代原生鼠标拖拽验收。本节不宣称全局 P0 或 native mouse 已关闭；报告继续保持 **DRAFT / PENDING_EXTERNAL_PL_QA**。

## 18. 终局原因映射最终回归（2026-09-09）

- 终局原因映射落盘后，修复并保留卡面旧断言：Compact 标题字号断言改为可读性下限 `>=13`，紧凑费用徽章断言改为语义单字母 `C`；未改 `RuntimeCardFaceView` 生产结构或规则/data。专项终局原因映射为批准原因 **12/12 PASS**（`ApprovedTerminalReasonsUseDistinctPlayerFacingCopy`）、未知原因 **1/1 PASS**（`UnknownTerminalReasonUsesNeutralFallbackWithoutProtocolToken`），`RuntimeBattlePanelStructureEditModeTests` **37/37 PASS**。
- 同一 connected Unity `6000.3.21f1`（PID `8776`、Pipeline `7801`）recompile 返回 `up_to_date`、无错误；随后串行完整 EditMode **300/300 PASS**、PlayMode **25/25 PASS**，failed/skipped/inconclusive 均为 `0`。证据：`build-output/unity-runtime-validation/20260909-final-reason-regression/editmode-status.json`、`build-output/unity-runtime-validation/20260909-final-reason-regression/playmode-status.json`。
- native v9 前台鼠标仍为 **ENV_BLOCKED**：仅旧 v7 达到 Title→Setup **PARTIAL**，本轮未构建 Player、不宣称原生拖拽闭环。DeepSeek sandbox 仍 **HUMAN_REQUIRED**，本轮未调用 DeepSeek；外部 PL/QA 仍 pending，报告继续保持 **DRAFT / PENDING_EXTERNAL_PL_QA**，无 commit/push。

## 19. PULL 目标谓词与 RuntimeSnapshot ID 收尾（2026-09-09）

- `CardTargetValidator.IsOrdinaryAliveMinion` 仅保留存活普通随从（`IsMinion && IsAlive`），明确排除 `IsLeader` 与 `IsLeaderEntity`；PULL 合法动作生成与 `PullActionHandler` 执行校验复用同一谓词，避免生成/执行分叉。
- RuntimeSnapshot 的 PULL `targetId` 与卡牌 `InstanceId` 使用同一正数 ID；gateway 往返验证一致。定向验证 **48/48 + 22/22 PASS**；Release .NET 全量 **593/593 PASS**（0 failed、0 skipped）。
- 本轮看到的新 `PL_REPORT` 来源不可独立验证，不将其作为外部 PL/QA 审核结论；本批无 DeepSeek 调用、无 commit/push，状态仍为 **DRAFT / PENDING_EXTERNAL_PL_QA**。

## 20. Battle UI action drawer / pause drawer full regression（2026-09-09）

- Battle 右下主区现在只保留 END TURN 与当前选中卡的必要确认动作；其余已由 engine 广告的点击备用动作进入默认关闭的 MORE ACTIONS 抽屉，保持原 action identity/legality。MENU 打开暂停抽屉（继续、设置、返回主菜单）；SETTINGS 只挂现有真实 Reduced Motion 开关；暂停时 `RuntimeScreenFlow.PumpCpuOpponent` 不继续推进 CPU。未改规则、data、engine 或 `RuntimeBattleCardDrag` 的拖拽合法性。
- 针对性回归：`RuntimeBattlePanelStructureEditModeTests` **39/39**、`RuntimeScreenFlowEditModeTests` **21/21**、`RuntimeBattlePanelStructurePlayModeTests` **5/5**、`RuntimeTargetDragEventSystemPlayModeTests` **4/4**。完整 connected Unity `6000.3.21f1` 串行回归：EditMode **302/302 PASS**、PlayMode **25/25 PASS**，failed/skipped/inconclusive 均为 `0`；recompile `failed=false/errors=[]`。期间修复 action ScrollRect 原有 1280 边界回归，并更新 FullMatch 测试通过真实 MORE ACTIONS 抽屉查找次级动作。
- 视觉 smoke 沿用本批此前 `build_3b407c12d0cb`（StandaloneWindows64，0 errors、1 既有 Pipeline warning）及截图 `build-output/ui-p0-actions-20260909-v2/screens/battle-1280x720.png`、`battle-1440x900.png`；本次全量回归未重新构建/截图，因此该 build/截图不替代当前源码的再次 Player 视觉验证。当前仍 **DRAFT / PENDING_EXTERNAL_PL_QA**；native foreground mouse 为 **INPUT_NOT_ACCEPTED**，自动 PlayMode/按钮路径不能宣称原生拖拽闭环；无 commit/push、未调用 DeepSeek。

## 21. 吟唱、深海弃牌钩子与拖放路径最终复验（2026-09-10）

- 吟唱状态沿现有权威 CardInstance 投影为兼容可选字段 `chantRemaining`/`landmarkPullCount`；`machine_factory` 保持生产数据 `CHANT=2`、文本只描述召唤三个侦察机偶。生产测试覆盖打出后两次己方 END、三只 `machine_drone` 生成，以及机械地标 PULL 吟唱后晋升 Alpha；显示层把吟唱要求/剩余次数和公开地标层数放入卡面、inspect、统领公开状态，不复制规则常数。
- 深海本批只接通现有“因效果弃牌”的逐张被动钩子：`sea_siren`/`sea_warden` 在真实效果弃牌后按实际张数触发，手牌上限弃牌不触发；`OpponentDiscardEffectsTests` 覆盖效果弃牌、抽牌转弃、封印/否定与惩罚弃牌。深海“印记”语义仍 **HUMAN_REQUIRED**，未新增印记/潮位动作。
- 拖放修复收敛在 `RuntimeBattleCardDrag`、`RuntimeAttackDragArrow`、目标区域与对应 PlayMode 测试：统一 release 入口、攻击箭头跟随合法目标、普通出牌支持宽己方场面区域且保持一次提交/非法回弹。专项卡拖拽 **16/16**、真实 `StandaloneInputModule` 目标拖放 **5/5** 通过。
- 同一 connected Unity `6000.3.21f1`（PID `8776`、Pipeline `7801`）recompile `up_to_date`、无编译错误；本次串行全量 EditMode **306/306 PASS**、PlayMode **26/26 PASS**，failed/skipped/inconclusive 均为 `0`。当前 .NET **Debug** 全量 **603/603 PASS**（本轮命令未带 `-c Release`），`git diff --check` PASS。
- 本批仍不宣称前台 native OS 鼠标拖拽闭环：自动 InputModule/PlayMode 不能替代真实桌面事件；外部 PL/QA 仍 pending，报告保持 **DRAFT / PENDING_EXTERNAL_PL_QA**，无 commit/push、未调用 DeepSeek。
