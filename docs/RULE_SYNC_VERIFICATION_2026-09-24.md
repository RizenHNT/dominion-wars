# Java 规则同步只读复核（2026-09-24）

## 结论

本批 `Game.java` / `Effects.java` / `TestMain.java` 的目标行为与当前规则口径一致；现有 Java 回归日志为 **72/72 通过**。C# 交叉切片的既有证据为 **138/138 通过，0 失败、0 跳过，构建 0 错误/0 警告**。本次收尾复核未另改生产代码；前序实施修改了 Java，本报告记录其验收；不把工作树中的其他 dirty diff 归入本批成果。

## 逐项核对

| 项目 | 复核结果 | 代码/测试证据 |
|---|---|---|
| 牌库循环归属与胜利 | PASS | `Game.reshuffle` 将有效循环计数写入发生循环的 `PlayerState`（`reshuffleCount`、`cycleWinCount`），阈值命中后 `winner = p.idx`；`TestMain` 的洗牌用例覆盖第一次、第二次阈值、对手计数不变及自身获胜（约 L612–L629）。免计数额度仍只消耗自身额度。 |
| 破城顺序与持有者 | PASS（含未决边界） | `breakRoyalCastle` 先 `forceLeaderOut(victim)`，随后 `checkRoyalCastleWin`；双方活动统领均为随从时破城方优先，否则只按当前活动 `ROYAL_CASTLE_BREAK` 持有者被动获胜。非随从双方同时持有仍保持未决，不按座位猜胜；三类回归覆盖约 L641–L707。 |
| 结束阶段 PUSH | PASS | `endTurn` 在吟唱、惩罚牌清理和其他结束阶段处理后直接调用 `pushQueue`；未读取代理 `askPush`。`pushQueue` 按提交队列顺序逐张移入云端栈并结算上传效果，回归覆盖代理 veto、FIFO 与效果（约 L1235–L1257）。 |
| PULL 选择与拒绝原子性 | PASS（引擎路径） | `pull` 对多载体要求代理返回合法目标；`pullWith` 先校验载体、下载效果的显式目标和已注册动作，再支付惩罚、移除云端栈顶、结算效果。缺失/非法选择在扣费和移动前返回；回归约 L1291–L1330 检查手牌、云端栈、下载计数和目标状态不变。 |
| ROLLBACK 选择与拒绝原子性 | PASS（引擎效果路径） | 队列单卡才可直接确定；多卡必须经过 `chooseRollbackTarget`，null/队列外对象只记录拒绝，不移队列、不回手。回归约 L1374–L1388 覆盖缺失与非法选择。该测试是 `chooseRollbackTarget` 的引擎路径，不是独立的 `selectedTargetId`/动作传输 E2E。 |
| 条件数字解析 | PASS | `Effects.threshold` 只接受非空 ASCII 十进制后缀，负号、`+`、坏字符和 `Integer` 溢出均返回 null；未知/坏条件最终 fail-closed。`ConditionGrammarTests` 覆盖空后缀、负数、`+1`、坏后缀、`2147483648`、超长溢出和合法 `0`，并随 `TestMain` 计入 72/72。 |

## 证据绑定与时序

- Java 产物：`build-output/rule-sync-20260924/java-build.log` 仅记录 `Build complete`，`TestMain.log` 记录完整测试名及 `通过 72 / 72`；两份 Java 日志**没有 source/class hash manifest**。
- 当前工作树时序复核：`Game.java` 00:17:33、`Effects.java` 00:20:02、`ConditionGrammarTests.java` 00:19:34、`TestMain.java` 00:21:59 写入；`build/classes/.../Game.class` 00:22:16、`Effects.class` 00:22:17；`build/test-classes/.../TestMain.class` 与 `ConditionGrammarTests.class` 00:22:42；`TestMain.log` 00:22:44（2026-09-24 JST）。这支持“产物在相关源之后生成”，但因日志缺少 manifest，不宣称 Java 测试具备加密绑定。
- C#：`build-output/rule-sync-20260924/csharp/summary.txt` 与 `rulesync-core.trx` 均为本批证据：build exit 0、0 warnings/0 errors；TRX total 138、passed 138、failed 0、skipped/notExecuted 0、error/timeout/aborted/inconclusive 0。C# summary 自带 217-file source manifest `167862214EF989155EEA9B8AE05D78B02184BDE5372C1CFBBA94554D66BB4D90` 及构建 DLL hashes。

## 明确边界 / 未完成

- 收尾复核阶段未重复构建或测试；未做 Unity Editor/PlayMode、前台 Player、原生鼠标、Web/全端到端、全量 AI 或全量跨端验证。
- 本批不裁决深海印记、免费 PULL 等待决规则，也不改卡牌数据、C#、Unity 或生产代码。
- 非随从双方同时持有 `ROYAL_CASTLE_BREAK` 的未决处理是规则书保留边界，不视为本批缺陷。
- UI/代理交互不等同于引擎拒绝证据；本报告只确认 Java 引擎在收到缺失/非法选择时的 fail-closed 与原子性。

## 2026-09-24 ENFEEBLE/BANISH/CONTROL parity slice

本节记录后续获批的 Java/C# 明确规则对齐实施。与上面的收尾复核阶段区分：本节所述 parity slice 确实修改了 Java 生产代码并新增 Java 定向测试；前一节工作树中的其他 dirty diff 仍不归入本批成果。

### 实施边界

- 修改：src/main/java/com/dominionwars/engine/Effects.java、Game.java、CardInstance.java；新增 src/test/java/com/dominionwars/test/EffectParityTests.java，并在 TestMain.java 注册。
- 只实现当前规则已冻结的三条：ENFEEBLE 负值攻/血（攻击下限 0，生命可致死）、BANISH 回原拥有者牌库并洗牌且非破坏、CONTROL 临时控制并在控制者回合结束时归还。
- 控制状态保持“拥有者不变、当前控制者变更”：当前控制者的场上区域负责移除/攻击，死亡仍进入原拥有者墓地，驱逐重置运行时状态后进入原拥有者牌库。
- 显式无效目标选择在目标解析处落空，不回退到代理默认的第一张候选；来源卡的当前控制权不匹配 srcIdx 时整条效果 fail-closed。
- 未修改 C#、既有 C# 或 Java 测试逻辑（仅在 TestMain 注册新增类）、卡牌/数值、UI、Unity、Sea/free-PULL 待定规则；无 commit、push、DeepSeek/DS 或清理操作。

### 同局面 fixture（交 action_localization_slice 对照）

以下均为 Java fixture 的初始输入与冻结预期输出；单体效果传入显式 selectedTargetId，避免把默认代理选择误当作协议行为。

| Fixture | 初始输入 | 预期输出 |
|---|---|---|
| E1 | P0 场上来源 1/4；P1 场上目标 2/5；ENFEEBLE, ENEMY_MINION, amount=-1, param=both, target=P1 | 目标变为 1/4、maxHealth=4，仍在 P1 场上，控制者仍为拥有者。 |
| E2 | E1 同局面；依次传 amount=+1、非法 param=invalid、不存在的显式 target id | 三次均无状态变化；失效 id 不改任一候选，不默认第一张。 |
| E3 | P1 目标初始 2/1；ENFEEBLE, param=hp, amount=-2 | 目标从当前控制者场上移除，进入原拥有者 P1 墓地，不进入来源 P0 墓地。 |
| E4 | 先用 C# 同局面 owner=P1/controller=P1 的 2/5 目标执行 BANISH；再将另一张 owner=P1 的目标置于 P0 场上（controller=P0）执行 BANISH | 两次都从当前控制者场上移除、重置运行时状态并进入 P1 牌库；P0/P1 墓地均不含目标。 |
| E5 | P0 来源；P1 owner 的 2/5 目标；CONTROL, ENEMY_MINION, amount=1 | 目标移动到 P0 场上但 owner 保持 P1；P0 可攻击；P0 回合结束后回到 P1 场上并清除控制字段。 |
| E6 | P1 场上 isLeaderEntity 统领；CONTROL 的 amount=0 与 amount=1 两次指向该目标 | 非正时限与统领目标均不移动、不改控制权；统领免疫边界不扩展到未冻结的 vulnerability 规则。 |
| E7 | 来源卡 owner=P0 但已由 P1 控制；仍以 srcIdx=P0 发起 ENFEEBLE | 来源控制权不匹配，目标不变，来源仍留在当前控制者 P1 场上。 |

### Java 证据与结果

- 收尾复核阶段未重复构建；本 parity slice 为验证新增的 Java 构建命令输出于 build-output/rule-parity-20260924/java-build.log，结果为 Build complete，日志尾部明确记录 BUILD_EXIT_0。
- 同一构建产物运行 com.dominionwars.test.TestMain，build-output/rule-parity-20260924/TestMain.log 为 UTF-8 可读日志，尾部明确记录 TEST_EXIT_0，并记录 79/79 通过、0 失败；新增本 slice 7 项，原 72 项因此为 79 项总数。
- 本批 Java 日志没有 source/class hash manifest；它们是当前工作树执行证据，不宣称具备加密绑定。没有在本轮重复 C# 构建或测试。
- 共享目录已有 action_localization_slice 的 C# 对照产物 build-output/rule-parity-20260924/csharp/java-csharp-parity-console.log 与 java-csharp-parity.trx：6/6 通过，其中 ENFEEBLE-both、BANISH-enemy-minion、CONTROL-one-turn 三个相关 fixture 均通过；本轮未重复该 C# 运行，仍不把它扩展成全端到端结论。

### 剩余边界

- 本批未扩展未冻结的统领 vulnerability/弑君组合、非随从统领、Sea/free-PULL 或 UI/动作传输 E2E 语义；这些继续独立记录，不以 C# 当前实现臆补规则。
- 直接效果在“未带显式 target id 且有多个合法候选”时仍保留 Java 既有代理选择路径；本批只钉住显式无效选择不得回退的边界，未将普通效果 API 全部改成 C# 动作传输模型。
- 未做 Unity Editor/PlayMode、前台 Player、原生鼠标、Web/全端到端、全量 AI 或全量跨端验证；也未重复此前 build-output/rule-sync-20260924 的 C# 138/138 证据。

## C# action_localization_slice 对照验收（只读，2026-09-24）

- 证据时序可追溯：`src/Engine/Tests/JavaCSharpParityTests.cs` 最后写入 13:13:17；`build-output/rule-parity-20260924/csharp/java-csharp-parity-console.log` 与 `java-csharp-parity.trx` 在 13:14:36 生成。TRX 是一次 NUnit 运行中的 **6 个测试全部通过**（不是四局 AI 结果，也不是跨端全量一致证明）。
- 可比的三条效果 fixture 是局部状态投影，而非整局相等：`ENFEEBLE-both` 的输入/目标状态与 E1 对齐（P0 来源、P1 目标 2/5、amount=-1、both、显式 target=3），输出目标 1/4/max4；`BANISH-enemy-minion` 对齐 P0 来源、P1 目标、显式 target=3，均验证目标离开当前场、进入拥有者牌库且不进墓地；`CONTROL-one-turn` 对齐 amount=1 的显式目标、owner=P1/controller=P1，均验证转到 P0、时限 1，控制者结束后回 P1 且清除控制字段。Java 还额外检查了受控随从攻击和受控 BANISH 扩展；C# 还额外断言了相应事件，这些不是双方共同断言。
- 以上三项不宣称完整区域或事件流一致：C# `EffectTestFixture` 还布置了未被目标断言覆盖的其他场上卡；fixture 的 `WriteFixture` 输出是目标相关摘要，不能把 `p1.field=[]` 解释为整方场为空。C# 用 `EffectTestFixture.Apply`/`TurnFlow`，Java 用直接 `Effects.resolve`/`Game.endTurn`，没有同一 transport/E2E 时序或 source/class hash 绑定。
- 其余三项不是本次三动作的严格同局面：`DECK-CYCLE-owner` 的 C# 输入是阈值 1 的单次抽取，而 Java 旧回归覆盖阈值 2 的两次循环；`CASTLE-BREAK-minion-priority` 的 C# fixture 未设置 Java 用例中的 `ROYAL_CASTLE_BREAK` 条件且额外断言事件/受害标记；`MECHANICAL-PULL-selection` 只与 Java PULL 回归共享缺失/非法选择拒绝和合法目标强化语义，完整牌组、惩罚、云端栈状态不同。故本报告只记录三条效果的局部输入/输出可比，不写“整局一致”。

## AI 32-action budget 与四局 fixed-seed probe 验收（只读，2026-09-24）

- `src/Adapters/Ai/AiTurnCoordinator.cs` 的当前实现把计数限制在 `ACTION`：当 `_actionsTakenInActionPhase >= _maxActionsPerTurn` 且仍有可提交的非 `END_TURN` 动作时停止并返回 `ai.action_limit_reached`；非 `ACTION` 阶段不进入该 budget 分支。`AdvertisedActionPolicy` 只在广告集合中存在时选择 `END_TURN`，AMBUSH/DISCARD 仍按各自广告分支处理，不合成越权动作。
- 定向单测源 `src/Engine/Tests/AiTurnCoordinatorTests.cs` 覆盖了 ACTION 多动作后通过广告 `END_TURN` 离场、max=2 后不再提交且返回 `ai.action_limit_reached`、以及无可用 PULL 时走广告 `END_TURN`。当前定向 build `build-output/ai-playable-20260924/ai-probe-build.log` 记录 0 警告/0 错误。
- 同 seed=4242 的前后实跑形成明确差异：修复前 `ai-playable-four-faction-console.log` 的 `machine_vs_sea` 在 turn=3、phase=DISCARD、`actionsThisTurn=32` 停在 `ai.action_limit_reached`；修复后 `ai-playable-four-faction-final.trx`/console 在同一对局达到 turn=5、`winner=0`、`reason=win.pull_total_ge`、75 actions、0 rejected、无 halt。最终 TRX 是 **1 个 NUnit 测试**产生四条 `AI_MATCH` 记录，不计为 4 个测试；四条记录均 terminal=True、rejected=0、stepLimit=False、halt=none、hiddenLeaks=0，`viewerChecks` 与 actions 相等。
- 新增边界证据 `build-output/ai-playable-20260924/ai-budget-boundary.trx` 为 4/4：同一预算条件（配置 `maxActionsPerTurn=2`）分别覆盖 DISCARD/AMBUSH 达限后仍提交、ACTION_PROGRESS 有 `PLAY_CARD` 时返回 `ai.action_limit_reached` 且不提交、ACTION_END 仅放行广告中的 `END_TURN`；每个 case 均经 `TryChoose → ToGameAction → BoundaryHost` 并由 `RuntimeActionBoundary.Validate` 校验。`ai-coordinator-lifecycle-final.trx` 同时为 15/15。
- 这已闭合配置预算下的跨阶段/END_TURN 边界；它不是默认 32 次压力测试，四局最终 ACTION 段最高仍为 23。未将并行的 CastleHealth 或其他 dirty 修改归入本节成果。

## 2026-09-27 Unity 主线续作（证据生成于 2026-09-28 JST）

本节只记录现有 Unity 项目的连接、UI 窄修和现场验证；不改变引擎规则、卡牌/平衡值或规则同步结论。

- 现有项目 `unity/DominionWars.Unity` 使用已安装的 Unity `6000.3.21f1` 和现有 Pipeline `0.5.0-exp.1`；未安装、升级或新建项目。GUI Editor 已进入非 batch PlayMode，Pipeline 状态为 ready，相关重编译 `failed=false`，未见本节之后的新 console error（查询 cursor=280 返回 0 条）。
- AI 预算夹具按已批准语义收紧：`RuntimeAiIntegrationPlayModeTests` 的默认构造验证 32 个 ACTION 后 fail-closed；另一个 fixture 让第 32 个 ACTION 后转入必经 `DISCARD`，验证预算不阻断强制阶段。现场 PlayMode **5/5 PASS**，结果见 `build-output/unity-mainline-20260927/live-play-ai-integration-status.json`。
- Match Setup 仅收窄 deck button 到 680 reference px 并将列偏移到 ±550，为中央 360 reference px CPU toggle 留出明确 gutter；不重建布局。现有路径受控执行 `MatchSetup → machine_deck/sea_deck → PLAYER 2: CPU → START MATCH → Battle`，1280×720 与 1024×768 截图均显示 CPU 文案、选中态和 START/BACK 可达，见 `match-setup-narrow-status.json` 及同目录 PNG。
- `RuntimeBattlePanelPresentationModel.BuildPlayerSection` 原先无条件拼接 `RuntimePlayerSnapshot.Life`（当前基础快照显示为 20）；基础规则没有已发布的玩家生命池 capability，因此本次仅从 player-facing 玩家摘要移除该行，保留 debug 摘要、统领生命候选和王城 HP。结构 EditMode **45/45 PASS**（含无 `生命 18` 断言），见 `editmode-panel-structure-status-v2.json`。新战斗截图为 `battle-no-player-life-1280x720.png`；更早生成的 battle 图可能仍含旧 `生命20`，不应作为本修正证据。
- 所有截图由连接中的 GUI Editor `capture_game_view --source screen` 生成；拖牌、目标与机械选择仍是受控 EventSystem/StandaloneInputModule 证据。没有宣称原生 OS 鼠标、前台 Standalone Player 或用户真实点击已验收；这些边界仍待独立验收。

## 2026-09-28 Unity card reader readability continuation（WIP）

本节记录 owner-approved 的现有 uGUI reader 窄改，不能替代 2026-09-27 主线证据，也不能把未现场复核的层标为 PASS。

- `unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeBattlePanelView.cs` 复用已有 `RuntimeCardInspectInteraction` / `RuntimeCardInspectModel` / `CardInspectScrollRect`，增加惩罚与当前攻/血 summary、`EFFECT`/机械字段分区、较大层级文字和自然内容高度；没有新增规则、卡值、法力成本、引擎字段或第二套 inspect 系统。
- QA 指出的 `KEYWORDS`/`TAGS` 重复已做最小源修：这两条直接使用模型已经带有 canonical 前缀的行，不再额外包同名标题。该最新 follow-up 尚未成功重编译，不能报 compile PASS。
- 真实长卡 `核心反击程序` 的 115 字符 probe 与 `baseline-before-reader-refresh-1280x720.png` 是刷新前基线，不是当前接受图。reader 的既有 x `.012-.135` 仍约为 1280 的 148 reference px / 1024 的 119 px；长标题裁切与可读宽度仍 PENDING，未在无安全 GUI 的情况下扩展覆盖范围。
- 上轮无过滤 Pipeline 探测触发 `SaveModifiedSceneTask`/PlayMode restore 阻断；取消只使旧 run invalidated，未强杀 PID 2700、未丢弃/保存未知 dirty 场景。focused tests 与刷新后 1280/1024 截图 BLOCKED；既有隐藏信息/拖拽安全结论本轮 `UNCHANGED_NOT_RERUN`，native OS/foreground Player 未验。
- 证据：`build-output/unity-ui-20260928/reader-refresh-status.json`、`README.md` 与基线图。未 commit、push、clean、安装/升级 Unity 或修改规则/卡牌数据。

## 2026-09-29 Unity card reader bounded expansion（WIP）

- 现有 Editor 已恢复可发现且非 Safe Mode：Unity `6000.3.21f1`、Pipeline `0.5.0-exp.1`、PID 2700、port 7801。未强杀、未保存或丢弃未知 dirty 场景。
- `RuntimeBattlePanelView.cs` 保留 `CardInspectRoot` 原窄 hit 区，把既有 reader 内容放到约 x `.366` 的 bounded surface；该宽度落在 hand/field cards 起点之前，不移动牌桌或目标区域。标题允许换行；正文继续使用现有 `ScrollRect`/`ContentSizeFitter`，新卡打开时回到顶部。reader 打开时可暂时覆盖 summary/leader/ambush 下层显示，关闭/取消或开始拖牌时交还控制。
- expanded viewport 的 Image raycast 保持开启，覆盖所有可见正文宽度，移除旧 left rail 的透明 `CardInspectScrollInput` 窄桥，避免右侧正文“看得到但滚不动”。`RuntimeBattleCardDrag` 既有 close-before-drag 与对手暗牌不绑定 inspect 的边界未改；结构测试新增 bounded surface/full-width raycast 断言，但尚未重跑。
- 受支持 `unity command recompile` 30 秒超时，`cancel_tests` 10 秒超时；Editor.log 的最新旧队列诊断仍显示 `RuntimeAiIntegrationPlayModeTests.cs:131` 报 `MaxActionsPerTurn` 缺失，但当前磁盘源码已经是 `DefaultMaxActionsPerTurn`，未添加假兼容 API。未连续重试 Pipeline。
- 1280/1024 长名、summary、正文顶部/末行、hover/click 零提交、合法拖一次、非法 revision 不变和暗牌保护均尚未取得本轮画面/聚焦测试证据，状态 `PENDING/BLOCKED`；native OS/foreground Player 未验。证据在 `build-output/unity-ui-20260929/reader-width-status.json` 与 README。

## 2026-09-30 Unity pause/card-reader exclusion follow-up（WIP）

- 既有 `MORE ACTIONS` / `RuntimeBattlePanelActionsDrawer` 已确认存在，默认关闭，合法 secondary actions 仍由原有 drawer 承载；本次没有新增抽屉、动作系统或规则字段。其既有每次 `RenderActions` 清理行为与 stale-action boundary validation 未改，ESC 全局优先级留作后续，不在本 slice 发明处理。
- `RuntimeBattlePanelView.SetPauseMenuOpen(true)` 现在复用既有状态入口关闭 More Actions、隐藏 `CardInspectRoot` 并将 `PauseDrawerRoot` 提升为最后 sibling；`ShowCardInspect` 在暂停打开时拒绝晚到 callback，关闭暂停后不会残留 reader。该修正只处理 QA 确认的 raycast/层级互斥，不移动牌桌区域、不改卡值/规则。
- 新增结构回归 `PauseMenuClosesMoreActionsAndCardReaderCannotReopenWhilePaused`，覆盖 drawer/pause 互斥、reader 隐藏、暂停期间不重开、暂停置顶与关闭后无残留；由于受支持 recompile 30 秒超时，测试尚未执行，不能报 compile/test PASS。未取得新截图，native OS/foreground Player 仍 `UNVERIFIED`。
- 本节只做源码窄修、结构测试登记和静态 `git diff --check`（目标文件 PASS）；证据继续写入 `build-output/unity-ui-20260929/reader-width-status.json` 与 README。未强杀 Editor、保存/丢弃未知 dirty 场景、重复 Pipeline、commit/push/cleanup。

## 2026-09-30 Unity reader verification retry boundary（BLOCKED）

- 复核范围只覆盖现有 reader bounded surface / 全可见正文 ScrollRect raycast / pause 互斥，以及已登记的结构回归；没有进入 PHASE、DROP AMBUSH 或事件日志文字层级清理，因为前置编译未验证。
- 唯一一次状态检查仍显示现有项目 Editor ready（Unity `6000.3.21f1`、PID `2700`、port `7801`）。随后唯一一次有界 `recompile --timeout 30` 无结果超时；没有 run_tests、截图或第二轮 Pipeline 重试。
- reader、暂停互斥回归和现有文字问题均继续 `PENDING/BLOCKED`，不能报 compile/test/画面 PASS。需要人类正常保存并重启 Editor 后再继续受控验证；本轮未强杀、未保存/丢弃未知 dirty 场景，也没有扩大 UI 改动。

## 2026-09-30 Runtime event-feed localization continuation（offline source slice）

本节记录在 Unity Pipeline 仍受阻时获批的一个离线玩家体验窄修；不把静态源码或引擎测试扩大为 Unity 运行时验收。

- 生产路径已把同一 presentation-only language 传过事件 rail 与瞬时反馈：`RuntimeBattlePanel.SetPresentationLanguage` → `BuildEvents(..., resolver, language)` / `RuntimeBattlePanelActionFeedback.Consume(..., resolver, language)`；EventDelta 保留同一语言路径。事件顺序、coalescing、优先级、amount/count、target redaction 与原六项持久 lifecycle allowlist 不变。
- 复用现有 `RuntimeLocalizationResolver` table/semantic keys/fallback，补齐事件摘要、事件动态标签、目标语义和 zh/jp/en 文案；没有新增平行翻译表，也没有读取卡牌私有 payload、稳定 ID、手牌/牌库身份或改变引擎规则/卡值/动作队列。
- 源测试登记在既有 Unity EditMode 文件（含显式 zh-CN 事件 rail 与 transient feedback 断言），但这些文件不在 `DominionWars.sln`，本轮没有冒充可离线执行的 Unity 测试。

### 离线验证与边界

- 当前源 `src/Engine`、`src/Data`、`src/Adapters` Release build：各 **0 warning / 0 error**；过滤 `FullyQualifiedName~LocalizationTests` 的现有 .NET 引擎测试为 **10/10**。这 10 项只验证引擎 localization 基线，不是 Unity UI 通过证据。
- `build-output/unity-static-compile/DominionWars.Unity.StaticCompile.csproj` 已读取当前 Runtime/UI 源码并重新探针；仍以 **2 errors / 144 existing nullable warnings** 退出：`RuntimeContentResolver.cs:463` 的 Unity stub 缺 `Texture2D.LoadImage`、`RuntimePlayerVisualSmoke.cs:395` 的 static project 缺 `ScreenCapture`。本批四个生产文件未出现编译错误，但因此不能写“Unity source compile PASS”。
- `git diff --check`（本批目标文件）通过。没有再次调用 Unity CLI、没有 Test Runner/PlayMode/截图/native OS 或前台 Player 验证；reader WIP、Unity recompile timeout 与画面验收边界保持原节所述。未 commit、push、清理或处理未知 dirty scene。

## 2026-09-29 Runtime presentation language selector continuation（offline source slice）

- 当前版 review 已复核事件/反馈投影、持久六项 lifecycle allowlist、顺序/coalescing、fallback 与隐藏信息边界；撤回了此前针对旧英文调用路径的误报。本节不把该静态复核扩大为 Unity 运行时验收。
- 复核发现 `RuntimeBattlePanel.SetPresentationLanguage` 之前没有玩家可达入口，默认仍为 `en`。现沿用既有暂停 Settings 抽屉，在不重建布局的前提下加入清晰的 `English`、`中文`、`日本語` 三个选择按钮；按钮只调用既有 setter，setter 立即重绘事件 rail，并让后续新消费的瞬时反馈使用所选 copy，不触碰 adapter、规则、动作队列、卡值或布局本体。按钮复用既有 Outline 加 `✓` 文字标记同步显示当前选择。
- 为保持事件消费 key、队列顺序、计时与动画不变，切换时当前/已排队 cue 不重放、不重格式化，继续自然结束；这是有界的 presentation 行为边界，不再宣称瞬时反馈对历史 cue 全即时刷新。
- 同一 panel 关闭并重新打开 pause/settings 后保留所选语言；没有新增持久化框架，因此新建 panel 保留既有 `en` 默认，需由外部 host 显式调用 setter 才会选择其他语言。该边界不宣称全游戏翻译完成，卡牌详情标题仍是后续 slice。
- 既有 Unity EditMode 结构测试增加 `PauseSettingsLanguageButtonsSelectSupportedPresentationLanguage`，覆盖三按钮、zh→jp→en setter、无 adapter 副作用及同一 panel 重开保留。该测试源不在 `DominionWars.sln`，本轮未冒充可离线执行的 Unity 测试。

### 离线验证与边界

- 过滤 `FullyQualifiedName~LocalizationTests` 的现有 .NET 引擎测试为 **10/10**；它只验证引擎 localization 基线，不是 Unity UI 或语言选择画面通过证据。
- 当前源 `DominionWars.Unity.StaticCompile.csproj` 重新探针仍为 **2 errors / 144 existing nullable warnings**，错误仍仅为 `RuntimeContentResolver.cs:463` Unity stub 缺 `Texture2D.LoadImage` 与 `RuntimePlayerVisualSmoke.cs:395` static project 缺 `ScreenCapture`；本批语言 selector 文件未出现在错误中。该项目是静态副本/探针，不等于 Unity Editor 编译。
- 本批语言 selector 目标文件 `git diff --check` 通过；未调用 Unity CLI/Test Runner、未取得新截图或 native OS/foreground Player 证据，未强杀/保存/丢弃 dirty scene，未 commit、push 或 cleanup。

## 2026-09-29 Runtime card-inspect label localization（offline source slice）

- 继续复用现有 `RuntimeCardInspectInteraction` / `RuntimeCardInspectModel` / `RuntimeBattlePanelView` 与 reader `ScrollRect`：`RuntimeBattlePanel` 现在在构建 inspection model 和显示 reader 时传入既有 resolver/language；旧 `Build(card)` 与 `ShowCardInspect(model, art)` 重载保持兼容。
- 新增的 `card.*` 键只覆盖详情栏目和结构化数值标签（类型、阵营、印刷/当前攻血、印刷惩罚、提交/上传/下载、效果、费用、目标、进度、地标、规则、关键词、标签、吟唱/剩余）；数值由 `RuntimeCardDisplayModel` 字段格式化，不用文本替换解析。卡名、作者规则原文、关键词值、标签值和隐藏信息边界不变，不改卡数据、规则、动作或布局。
- 新增 Unity EditMode 源测试 `LocalizedCardInspectUsesResolverLabelsAndPreservesAuthoredContent`，覆盖 `zh-CN` 模型与 View 输出、authored 文本原样和中文标签。该测试尚未在 Unity TestRunner 执行，不能记为运行通过；长文/1024/1280 画面、hover/click/拖动与 native/Player 仍 PENDING/BLOCKED。
- 当前静态探针 `dotnet build build-output/unity-static-compile/DominionWars.Unity.StaticCompile.csproj --no-restore -c Release` 为 exit 1、144 warnings、2 个既有 stub 错误（`RuntimeContentResolver.cs` 的 `Texture2D.LoadImage`、`RuntimePlayerVisualSmoke.cs` 的 `ScreenCapture`）；未发现本批目标文件新增错误。目标文件 `git diff --check` PASS。无 Unity recompile/TestRunner、截图、commit/push/cleanup。

## 2026-09-29 Runtime recovery post-commit contract repair（offline source slice）

- 复核 `RuntimeScreenFlow` 的 AI halt/rejected 提示、`RecoveryRequested → RequestReturnToMenu`、Result restart/return、deck/CPU 控件绑定后，唯一可落地的恢复缺陷是成功启动后的重复 readiness 检查：`RuntimeMatchSetupOrchestrator.TryStartMatch` 明确委托 `RuntimeBootstrap.TryStartSession`，而 bootstrap 只在 candidate adapter 已发布 snapshot 后才 commit 并返回 true；现有 `RuntimeBootstrapEditModeTests.MatchSetupOrchestratorTreatsBootstrapSuccessAsReadyContract` 也固定该契约。
- `RuntimeScreenFlow.RequestStartMatch` 与 `RequestRestart` 原先在 `TryStartMatch == true` 后再次检查 adapter/snapshot；一旦该不变量未来被包装层以合法方式满足但读取瞬间变化，流程会把已提交的新 session 留在后台，却把 UI 留在 setup 并显示失败，破坏“恢复/再开”闭环。最小修复删除两个 post-commit 复检块，保留 `TryStartMatch == false` 的安全错误分支，并补注释锁定契约；没有新增恢复功能、动作或规则。
- 现有 `RuntimeScreenFlowEditModeTests.MatchSetupListsCanonicalDecksAndStartsTheRealBootstrapSession` 与 `ResultRestartCreatesFreshSessionAndClearsTerminalPresentation` 覆盖成功进入 Battle、重开 fresh adapter 与回菜单；本轮没有 Unity TestRunner，故不把它们记为本轮运行通过。AI halt/rejected 文案映射与返回菜单绑定未改。长跑/截图/native/Player 仍 PENDING/BLOCKED。
- 本轮目标文件 `git diff --check` 通过。静态探针仍 exit 1、144 warnings、仅既有 `Texture2D.LoadImage` / `ScreenCapture` 两个 stub 错误；未出现 `RuntimeScreenFlow` 新错误。未重试 Unity Pipeline、未处理未知 dirty scene、未 commit/push/cleanup。

## 2026-09-29 Unity recovery — focused compile, tests and GUI evidence

本节更新并 supersede 本报告前述 reader/Pipeline `PENDING/BLOCKED` 的历史状态；历史 WIP 文字保留作时序，不再作为当前运行结论。

- 人类正常关闭旧 Editor 后，只读确认旧 PID 已退出、项目锁已释放；按原绝对路径重新打开现有 `unity/DominionWars.Unity`，使用已安装的 `6000.3.21f1`，新 Editor PID `44564`、Pipeline `7801`。没有新建/升级/安装、没有强杀、没有保存或丢弃未知 dirty 场景。
- 受支持 recompile 实际完成：`status=completed`、`failed=false`、`errors=[]`。`RuntimeBattlePanelActionFeedbackEditModeTests` 首轮 **41/42** 暴露无字段 `DAMAGE APPLIED` fallback 误回 resolver 语义 `DAMAGE`；最小修复新增既有 resolver 的 `event.damageAppliedFallback` 键并接入旧 fallback，重编译后该 class **42/42**。不改规则、卡值、动作顺序或适配器边界。
- 本次过滤 EditMode 结果（各 invocation 独立，不累计为全套）：CardDisplayInspect **11/11**、BattlePanelStructure **47/47**、ActionFeedback 修复后 **42/42**、LocalizationResolver **6/6**、Adapter **12/12**、BattleCardDrag **16/16**、ScreenFlow **28/28**；均 **0 failed / 0 skipped**。
- 本次过滤 PlayMode 结果：BattlePanelStructure **6/6**、Bootstrap **6/6**、ScreenFlow **3/3**、TargetDragEventSystem **6/6**、AiIntegration **5/5**；均 **0 failed / 0 skipped**。AiIntegration 包含默认 32 ACTION 上限、必经 DISCARD 不被阻、CPU/rejected/terminal 路径。未执行无过滤测试或更长的 full-match user-journey class。
- 受控 Editor/uGUI 路径完成 `MatchSetup → CPU → START MATCH → Battle`；Settings 中 `中文` 以 `✓` 与 outline 显示选中。真实公开自己手牌 `克拉肯触手` 的 reader 在 1280×720 与 1024×768 均显示标题、印刷惩罚、当前攻/血、效果、关键词和标签，顶部/末行均可见；正文在该样本高度内无需滚动，因此 top/bottom 图相同，并非假称发生了长文溢出滚动。点击打开/再次点击关闭的受控探针保持 snapshot revision `1→1`、无 action submit；对手牌仍为 `*`，不绑定 inspect。
- 证据集中在 `build-output/unity-ui-20260929/recovery-evidence-20260929.md`，含源/DLL 时序、精确计数、控制输入边界和截图清单。Settings 图为 `recovery-settings-zh-1280x720.png` / `recovery-settings-zh-1024x768.png`；reader 图为 `recovery-reader-zh-top/bottom-kraken-1280x720.png` 与 `...-1024x768.png`；setup/battle 图保留同目录 `recovery-*` 文件。
- 离开前 `RuntimeBootstrap.unity` 为 `isDirty=false`；受支持 `editor_stop` 后 Editor `ready`、`playMode=stopped`、`compiling=false`。输入证据是 Pipeline eval/uGUI 控件，不宣称 native OS mouse、前台 standalone Player 或无过滤全量通过。

### 2026-09-29 raw Pipeline result and ScrollRect follow-up

- QA 要求的每组原始返回已落在 `build-output/unity-ui-20260929/`：7 个 EditMode `pipeline-edit-*.json`，以及 5 组 PlayMode 各自的 `pipeline-play-*-20260929-async-start.json` / `-async-status.json`。PlayMode 同步 HTTP 尝试的 `0/0` 返回明确为 domain reload 拒绝，不计结果；异步 status 实际完成为 **6/6、6/6、3/3、6/6、5/5**，0 failed/0 skipped。
- 真实公开最长可见卡 `克拉肯触手` 的 model detail 长度为 141；实际 reader content `400.6483×138` 小于 viewport `400.6483×202.5663`，因此真实卡 top/bottom 图同 hash 是正文不溢出的事实，不冒充滚动通过。真实卡静态可读性图和几何原始值见 `recovery-reader-zh-*-kraken-*.png` 与 `reader-geometry-20260929.json`。
- 为验证既有 ScrollRect 的长文几何，使用仅运行时的明确 `QA SCROLL FIXTURE`（1371 字符，production card data unchanged），content `621.3333` 高于 viewport `202.5663`，top normalized `1`、bottom `0.00000009109354`；1280/1024 top/bottom PNG hash 均不同且末行 `END OF FIXTURE` 可见。该 fixture 不是生产卡数据或源码改动，只支撑滚动几何；native mouse/standalone Player 仍未验。

## 2026-09-30 Unity AI pacing and discard selection continuation

- 现有 `DominionWars.Unity` 使用已安装 `6000.3.21f1`；受支持 recompile 为 `failed=false`。`RuntimeScreenFlow` 的展示层 CPU gate 使用 `0.62s` 间隔并复用现有 feedback queue；引擎时序、AI 策略、规则、卡值与动作合法性未改。
- 真实受控回合 accepted CPU revisions `36→37→38→39` 的 realtime 为 `870.686/871.557/872.408/873.258`，间隔约 `0.871/0.851/0.850s`；标准反馈处于 animating/pending。暂停后 `current=1/revision=49` 跨 6 个样本约 2.5s 不变；Reduced Motion 下 revisions `50→51→52` 间隔约 `0.938/0.885s`，无 pulse 但仍有间隔。ScreenFlow focused PlayMode **3/3**；AI integration 首轮 **4/5**（旧 16-frame 观察窗不足）保留，bounded deadline 修复后重测 **5/5**；首次/重测原始 JSON 分开保存。
- 真实 `DISCARD` snapshot 为 `revision=26`, `requiredCount=19`, `candidateIds=29`。现有 pending selector 增加用户主动 `ALL/CLEAR`：`ALL` 只暂存公开候选，超 required 时 `CONFIRM` 保持禁用并提示 `Deselect 10 cards`；`CLEAR` 清空；`CANCEL` 关闭。受控验证保持 revision `26`、无提交。Focused `RuntimeTargetDragEventSystemPlayModeTests` **6/6**，0 failed/0 skipped。
- 证据：`build-output/unity-ui-20260929/ai-pacing-evidence-20260930.md`、`discard-selection-evidence-20260930.md`、对应 PNG 与 `pipeline-play-discard-selection-20260930-status.json`。输入为 Pipeline eval/uGUI，不宣称 native OS mouse、前台 standalone Player 或无过滤全量；卡牌尺寸、拖放目标与阶段状态按钮本批未改，仍待后续单项验证。PlayMode 结束后 `RuntimeBootstrap.unity` `isDirty=false`、Editor ready/stopped；未 commit、push、DS 或 cleanup。
- 追加的业务路径回归不是只测打开/取消：`ALL(29) → 取消10 → 19/19 confirm 可用 → exactly one submission → resulting snapshot revision=2 广告 END_TURN`，并覆盖 revision 变化后旧 confirm 0 submission、pending 清理。首次受控组 **7/8** 的唯一失败是 fixture 替换 session snapshot 后正常 refresh status 为 `Unavailable` 而测试暂期望 `Action rejected`；保留首轮 raw，修正 fixture 更新 session snapshot 并 recompile `failed=false/errors=[]` 后重测 **8/8**，0 failed/0 skipped。Raw：`pipeline-play-discard-selection-business-20260930-first-status.json` 与 `...-status.json`；详见 `discard-selection-evidence-20260930.md`。
- 按后续拖拽命中测量要求，只读复用现有生产 battle surface：受控 eval 在 `1920×1080`（CanvasScaler reference `1280×720`, match `0.5`, scale `1.5`）读到 card `89.49×124.84` reference units（屏幕 `134.2371×187.2586`）、own hand drop `574.95×128.84`、own field drop `574.95×95.76`；`RuntimeBattlePanelStructurePlayModeTests` **6/6** 与 `RuntimeTargetDragEventSystemPlayModeTests` **8/8** 均通过，包含 reader 不抢 hand drag、hand/field/castle/illegal hit paths。1280/1024 projection 与边界见 `build-output/unity-ui-20260929/drag-hit-measurement-20260930.json`；1024 为 CanvasScaler 几何投影而非独立 live frame，native/standalone Player 仍未验。无拖拽生产逻辑修改。
