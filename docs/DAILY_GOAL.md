# Daily Goal — Current Unity playable closeout and independent PL/QA review

Status: READY
Date: 2026-09-08

## Owner-approved continuation — 2026-09-24 Java rule synchronization

The owner authorized a narrow Java-engine parity repair against the current `docs/RULES.md` and the already implemented C# active-leader/phase semantics. Preserve all unrelated dirty work and the existing DeepSeek changes. Only the Java engine and its directly related Java tests may be changed in this batch.

- Correct reshuffle accounting so the player whose own deck cycles receives its own cycle count and wins at the configured threshold.
- Correct castle resolution order: force the defender leader out first; active minion leaders give the breaker priority; otherwise an active `ROYAL_CASTLE_BREAK` holder wins. Do not invent a non-minion dual-holder tie rule.
- Keep end-phase PUSH automatic FIFO with no agent veto.
- Reject missing/invalid multi-target PULL and ROLLBACK selections without silently choosing the first candidate, before consuming state or punishment.
- Make numeric condition parsing fail closed for malformed, negative, and overflowing suffixes while retaining the accepted C#-compatible grammar.
- Add only necessary Java regression coverage. Do not change card values, Sea mark/free-PULL decisions, C#, Unity, UI, commit/push, cleanup, or DeepSeek relay state. Record verification under `build-output/rule-sync-20260924`.

## Owner-approved continuation — 2026-09-24 ENFEEBLE/BANISH/CONTROL parity slice

The owner approved a second narrow Java parity slice against the frozen `docs/RULES.md §13.2`, `docs/effects.contract.md §11.1`, and the already implemented C# effect/runtime semantics. This slice is limited to Java production behavior in `Effects.java`, the directly required control/ownership seams in `CardInstance.java` and `Game.java`, and new Java-only targeted tests plus `TestMain` registration. The C# implementation, existing tests, card data, balance, UI, Unity, and unrelated dirty work remain out of scope.

- Register and implement only the explicit ENFEEBLE (negative attack/health with attack clamp and normal death cleanup), BANISH (owner deck + shuffle, non-destroy), and CONTROL (temporary controller with end-of-controller-turn return) behavior.
- Preserve source/target ownership boundaries: a controlled unit remains owned by its original player; field removal uses the current controller while deck/graveyard destination uses the owner; invalid explicit target selection must fail closed without selecting the first candidate.
- Add same-fixture Java regression coverage for source/target selection, negative/invalid effect parameters, lethal cleanup, banish non-death, control attack ownership and expiry, and invalid/immune selections. Record the initial fixture inputs and expected outputs for the `action_localization_slice` C# comparison.
- Do not infer unresolved leader-vulnerability, non-minion leader, Sea, free-PULL, card-value, or UI behavior. Record any remaining parity boundary in the existing 2026-09-24 report. Persist Java build/test output under `build-output/rule-parity-20260924`; no commit, push, DeepSeek call, cleanup, or additional agent.

## Owner-approved continuation — 2026-09-13

### Next P0 mainline — persistent public match feedback

The owner asked Codex to continue into the next major development task after the explicit self-discard batch. The next slice is player-facing match readability: make authoritative public events remain readable long enough for a player to understand the opponent turn and state changes, then re-run the current-source complete-match and input gates. This is an implementation task, not a rule or balance redesign.

- Extend the existing bounded player event feed using only public `RuntimeAdapter.Presentation` events already supplied by the engine. Cover the actual advertised event types for draw, damage, ambush resolution, COMMIT/PUSH/PULL, punish, death/destruction, castle progress, turn/phase changes, and game over when those types exist. Do not invent event types, legality, numbers, targets, or card identities.
- Preserve hidden-information redaction: never expose opponent hand/ambush identities, raw private payloads, internal event IDs, target ID arrays, or debug-only data. Missing fields must produce a safe generic message rather than a guessed value.
- Preserve chronological order, deduplication, bounded history, Reduced Motion behavior, drag-first actions, explicit discard selection, pause/settings, result/restart/menu, and the engine-authoritative snapshot.
- Subagents implement and adversarially review. Serialize connected Unity operations. After implementation, run focused event-feed tests plus current-source screen-flow/full-match/input suites; build/foreground/native-mouse evidence remains a separate acceptance boundary and must not be inferred from `ExecuteEvents` or test-owned `BaseInput`.
- Allowed production scope: `unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeBattlePanelPresentationModel.cs`, `RuntimeBattlePanelActionFeedback.cs`, `RuntimeBattlePanel.cs`, and directly related Unity tests. Expand only for a reproducible P0 blocker. No card data, engine rule, AI life logic, balance, DeepSeek relay, commit, push, cleanup, or release.

### Completed implementation checkpoint — 2026-09-13

- Adapter/Gateway/AI explicit selection, schema/fixtures, and Unity pending-selection UI are implemented. Human DISCARD and self-discard actions require an explicit advertised choice; the gateway no longer chooses the first candidates. Advertisement payloads remain immutable, legacy AI payload selection remains compatible, conflicting channels fail closed, and replay identity includes a deep-copied typed selection.
- Unity supports candidate toggle/reselection, confirm, cancel, revision expiry, and modal blocking of alternate actions/drags. Pending controls are made reachable outside the discard viewport's active mask while selecting; mask and drag state are restored on cancel or expiry. This fixes the reproduced condition where `SelectionConfirm` had no GraphicRaycaster-reachable point.
- Verification: .NET full Debug 892/892 passed, 0 failed, 0 skipped (`build-output/self-discard-closeout-20260913/self-discard-closeout-20260913.trx`); runtime-contract validation schemas=4, valid=18, invalid=13, fail=0; Unity EditMode 45/45 (`self-discard-edit-20260913-clean.xml`); Unity PlayMode input/UI 6/6 (`self-discard-play-class-20260913-clean.xml`); `git diff --check` passed.
- PlayMode exercised the production `StandaloneInputModule` with a test-owned `BaseInput`; batch mode required explicit focus restoration because `EventSystem.isFocused` starts false. This is controlled Unity input evidence, not native OS mouse evidence. Native mouse remains unverified. No commit, push, paid relay, DS call, card balance, life-rule, or deck-design change was made in this batch.

The owner instructed Codex to finish the interrupted explicit self-discard submission repair first and pause DeepSeek's deck redesign. This supersedes the survival-priority task below: do not implement life-based AI survival logic or change playstyle weights, life rules, or card balance. Complete the advertised selection contract, gateway, human Unity selection, and AI submission path with targeted regression coverage; preserve immutable advertisement fields and never silently choose discards for the human. Preserve unrelated work; no commit, push, cleanup, or paid relay. Subagents implement; Codex coordinates verification and closeout. The pre-repair .NET run completed with 885/885 passed, 0 failed, 0 skipped (12m07s); this is not Unity acceptance or proof that the selection defect is fixed.

## Superseded AI repair proposal — 2026-09-12

The owner approved the QA clarification in the current Codex conversation. This manual batch supersedes earlier scope where necessary: implement an emergency survival priority only for engine-confirmed lethal threats and advertised actions that remove them; preserve ordering in nonlethal positions and all existing playstyle weights. Do not add cost inputs to ThreatEstimator or change hidden-card probability based on life. The adapter must not reimplement combat or victory rules, or exploit hidden opponent information.

Also repair explicit self-discard selection for advertised PLAY_CARD actions, retaining strict candidate/count/identity/revision validation and immutable advertisement fields. Update necessary engine/adapter/UI selection seams and directly relevant tests/contracts only. Keep the lethal regression in the normal suite; verify its fixture demonstrates actual engine lethality, not an attack sum. Preserve all unrelated work on `pl/ai-threat-estimator`; no commit, push, cleanup, paid relay, card balance or default weight changes. Reuse concise handoff documentation and report newly executed test counts separately from the reported 869/870 baseline.

## Owner-approved continuation — 2026-09-10

The owner's latest instruction is to continue the playable-game mainline. For this manual Codex session, extend the scope below to the existing Unity BattlePanel/View/ActionModel, ScreenFlow/ScreenShellView, necessary Runtime AI fixes, and their directly related tests. Preserve the completed chant, discard-hook, and drag work. This is not authorization to start the paid relay.

- Put advertised phase actions beside the existing phase/selection UI; preserve drag-first card actions and the secondary actions drawer. Do not introduce free phase skipping or new game rules.
- Verify and repair reproducible entry, deck selection, CPU turn, result, restart, and return-to-menu defects. If a path already works, collect current runtime evidence instead of inventing more features.
- Reuse the existing closeout report and mailbox for the handoff. Baseline: .NET Debug 603/603, Unity EditMode 306/306, PlayMode 26/26.
- Subagents implement and review; serialize connected Unity operations. Preserve unrelated changes. No commit, push, cleanup, paid-provider calls, or balance changes in this batch.
- Ambush withdrawal after payment and Deep Sea mark semantics remain pending; independent mainline tasks continue.

The previous long-form daily goal is preserved verbatim in `docs/DAILY_GOAL_ARCHIVE_2026-09-08.md`. That archive is historical context only; this compact goal is the current handoff boundary.

## Goal

After the two authorized implementation batches are complete, independently review the current shared-worktree Unity changes and produce a factual PL/QA closeout: verify the P0 enter-game and deck-selection path, audit the current/default mechanical COMMIT/PUSH/PULL content without inventing rule or card values, assess readable card UI and the current optional CPU opponent, and record verified gates, blockers, and remaining foreground or native-input work.

## Allowed scope

- `unity/DominionWars.Unity/Assets/DominionWars.Runtime/RuntimeAiPolicy.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.Runtime/RuntimeAiTurnCoordinator.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.Runtime/RuntimeAdapter.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.Runtime/RuntimeBootstrap.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeScreenFlow.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeScreenShellView.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.EditMode/RuntimeAiEditModeTests.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.PlayMode/RuntimeAiPlayModeTests.cs`
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.PlayMode/RuntimeAiIntegrationPlayModeTests.cs`
- `docs/CODEX_AI_CLOSEOUT_REPORT_2026-09-08.md`

## Acceptance criteria

- Confirm the production `RuntimeBootstrap`/`RuntimeScreenFlow` entry and deck-selection path, and distinguish connected-editor automation from foreground Player or native OS mouse evidence.
- Audit the current/default mechanical COMMIT/PUSH/PULL wiring and report any missing canonical payload as a blocker or HUMAN_REQUIRED decision; do not fabricate card values, timing, costs, or legality.
- Audit readable card presentation and opponent hidden-information redaction without changing visual direction or duplicating engine rules.
- Audit CPU actor/viewer separation: AI reads only viewer-1 snapshot/legal actions, human presentation remains viewer 0, and terminal/rejected/expired/step-limit paths stop safely.
- Reconcile the existing 8/8 AI evidence and the latest full-repository baseline without relabeling old or partial counts as current full-suite results.
- Run the authorized DeepSeek V4 Flash PL review and DeepSeek V4 Pro QA review only after both implementation batches are confirmed complete; record exact commands, outputs, findings, and severity in the closeout report.
- End with explicit PASS, BLOCKED, or HUMAN_REQUIRED per gate; do not mark unverified P0, visual, Player, or native-input behavior complete.

## Test profiles

- build
- regression
- sanity
- alignment
- nightshift-index

## Human decisions already made

- The owner authorized the current AI-opponent scope and the subsequent independent PL/QA review, but did not authorize new rules, balance, difficulty, paid services, release, commit, or push.
- DeepSeek V4 Flash is the temporary PL provider and DeepSeek V4 Pro is the QA provider; MiniMax is not an active route.
- The existing engine remains authoritative for legality, targeting, phase progression, COMMIT/PUSH/PULL resolution, hidden information, and victory.

## Forbidden tonight

- Do not use MiniMax, VS Code ordinary chat, mailbox text, or an unverified model response as PL/QA evidence; do not call a provider before both implementation batches are complete; do not modify rules, card data, balance, contracts, visual direction, credentials, release state, or unrelated dirty files; do not weaken fail-closed behavior; do not commit, push, clean, reset, delete, or publish.

## Owner-approved continuation — 2026-09-27 Unity mainline evidence

This continuation owns only the existing Unity GUI Editor/UI verification boundary. Preserve all unrelated dirty work and do not start a new project, install/upgrade Unity, call paid providers, or alter engine rules, card values, balance, contracts, or release state.

- Verify the pending non-batch first-frame/resize surface and the current user path: deck selection, CPU toggle, controlled card/target/mechanical selection, CPU handoff, result/restart/menu. Record screenshots and status JSON under `build-output/unity-mainline-20260927`.
- Keep the AI budget fixture aligned with the approved semantics: default 32 ACTION submissions are bounded, while a mandatory advertised DISCARD phase remains submit-able. A reproducible UI failure may receive the smallest local repair; do not infer new legality or targeting.
- Keep player-facing base summaries free of an unconditional `RuntimePlayerSnapshot.Life` row unless a canonical life-pool capability is published; retain authoritative castle/leader life surfaces and debug details. Native OS mouse and foreground Player acceptance remain separate, explicitly unverified gates.

## Owner-approved continuation — 2026-09-28 Unity card reader readability (WIP)

This slice owns only the existing `RuntimeCardInspectInteraction` → `RuntimeCardInspectModel` → `RuntimeBattlePanelView` reader. Reuse the approved layered-print direction and existing `CardInspectScrollRect`; do not create a second inspect system, change engine rules/card values/balance, or expose hidden opponent cards.

- Keep the hand compact and expose canonical punishment/stats plus long effects in the existing reader. The narrow source refresh adds a separate summary, `EFFECT`/mechanical sections, larger type, and natural `ScrollRect` content height. `KEYWORDS`/`TAGS` must use the model's existing canonical prefixes exactly once.
- Preserve interaction boundaries: hover/click inspection is not submission; drag closes inspection before pointer capture; reader cleanup follows rebuild, close, cancel, and snapshot replacement; opponent hand remains redacted.
- Acceptance remains WIP: the real 115-character `核心反击程序` probe is pre-change baseline only; current anchors remain x `.012-.135` and the 1280/1024 long-title readability risk is pending. Connected Editor focused tests, post-change screenshots, and the latest duplicate-prefix follow-up compile are blocked by the invalidated Pipeline queue. Existing safety evidence is `UNCHANGED_NOT_RERUN`, not a new pass. Native mouse/foreground Player remains unverified.
- Evidence is under `build-output/unity-ui-20260928`; preserve the dirty worktree and do not commit, push, clean, install/upgrade Unity, or alter rules/card data.

## Owner-approved continuation — 2026-09-29 Unity card reader bounded expansion (WIP)

Keep the 2026-09-28 reader slice local: retain the existing inspect/model/ScrollRect path and approved visual language, but let the presentation surface expand to a bounded width when inspection is visible so 1280/1024 do not render the detail in 148/119px. The original narrow root remains the stable interaction boundary. The expanded reader may temporarily cover summary/leader/ambush presentation while open, but it ends before hand/field card drag starts; close/cancel and drag-start cleanup must restore the tabletop.

- Verify a real long-title/long-effect card at 1280×720 and 1024×768, including summary and body top/last-line screenshots.
- Verify hover/click inspection does not submit, legal drag submits once after inspection closes, invalid revision leaves the snapshot unchanged, and opponent hidden hand remains redacted while public field cards can inspect.
- Current implementation and evidence are WIP: source-only bounded expansion now keeps the full visible ScrollRect viewport raycastable and removes the narrow input bridge; the connected Pipeline recompile/cancel queue timed out and no post-change screenshots or focused test results are claimed. No rule/card/balance/engine/scene save changes, commit, push, cleanup, DS call, or Unity upgrade.

## Owner-approved continuation — 2026-09-30 Unity pause/card-reader exclusion (WIP)

This continuation remains inside the existing uGUI reader and secondary-action
drawer. `MORE ACTIONS` / `RuntimeBattlePanelActionsDrawer` is already the
canonical drawer; do not create another drawer or change action legality,
rules, card values, or the existing per-render cleanup behavior. Close the
drawer and the expanded card reader when pause opens, keep pause topmost, and
reject late reader reopen callbacks while pause is active. Do not add a new
global ESC dispatcher in this slice; record that priority decision for a
later focused pass.

The source-only guard and focused structure regression are recorded in
`build-output/unity-ui-20260929`. The connected Editor recompile timed out, so
tests and screenshots remain pending; preserve unknown dirty scene state and
do not retry or force-kill the pipeline. The single 2026-09-30 status check
still found the Editor ready, but the one bounded recompile probe again timed
out; human-normal save/restart is required before verification resumes. No
PHASE / DROP AMBUSH / event-copy cleanup was made behind the unverified build.

## Owner-approved continuation — 2026-09-30 compact hand quick-scan (WIP)

- Narrow UI update: compact hand cards no longer spend face space on TYPE or truncated rules paragraphs; they retain the title, printed punishment, attack/health, eligible concise keyword and full live progress, with the existing click-to-inspect entry. The existing reader still exposes full rules, fees, goals, progress, landmark state, keywords, tags, type and faction. The horizontal hand minimum is now 96 reference units (spacing remains 6); no rules, authored card values, or weights changed.
- Source scope: `RuntimeBattlePanelView.cs`, `RuntimeCardFaceView.cs`, `RuntimeCardFaceViewContractEditModeTests.cs`, and `RuntimeBattlePanelStructurePlayModeTests.cs`. Frozen SHA-256: `3E7348B7239DD4BB03397DD6881CAF995D69B5A433CCC9C47F34A9EB32976865`, `A6DBE89170A35F40F4A329E2378DA5A977AEDEDCCE924F49827BCCE62A2F7F6A`, `49DAD1755478AB3315C2AF09C75FAB8A558296E76313A288F8B90C8DE64A9545`, `CA5096DF3E16AF3EC0EB60926691DB1D8141897B81DCCC5663451DBBC0053218` respectively. These hashes cover full files, including unrelated pre-existing dirty hunks.
- Static source review by independent QA accepts the targeted patch and found no P0/P1; QA caught and the implementation corrected one fixture-label expectation. `git diff --check` passes. This is not runtime acceptance: the filtered EditMode/PlayMode runs and a post-change capture were not run because `unity/DominionWars.Unity/Temp/UnityLockfile` remains present while no `Unity.exe` process is running. Do not remove/bypass the lock. The 2026-09-30 hand-scroll screenshot is pre-change baseline only; post-change rendering, nonzero scroll preservation through refit, native pointer input, and a full match remain pending.
- After normal Unity closure clears the lock, run only the fully filtered face-contract EditMode and scroll/raycast PlayMode cases, save raw JSON, and visually inspect the updated hand/reader at the intended viewport before claiming completion. Preserve all existing dirty work; no commit, push, cleanup, or external provider call.

## 2026-10-01 current-source Unity and regression closeout (supersedes stale WIP verification claims above)

- The source was available and Unity CLI completed the targeted recompile/test runs; the earlier lock/queue-blocked wording remains historical only. Current authoritative full Unity runner result is still **EditMode 313/331 passed, 18 failed, 0 skipped, exit 1** at `build-output/unity-runtime-validation/20261001-084401-4e2383b0/editmode-results.xml`. The runner stopped at EditMode; PlayMode, Windows64 build, and Player smoke did not run.
- All 18 failures are classified from that raw XML: 11 CardEditor GUI tests emitted `No graphic device is available to initialize the view` under the runner's `-nographics`; 2 card-width assertions expected the superseded 80 reference units instead of 96; 4 event assertions excluded the approved `EVENT SUMMARY` player-facing heading; 1 shared-context lifecycle assertion expected resolver ownership `None` rather than borrowed ownership. The three stale assertion groups were corrected without changing game behavior. Current-source no-`-nographics` targeted evidence: hand width 4/4, Events class 19/19, shared-context ownership case 1/1, CardEditor effect spec 10/10, and ArtPicker 1/1. Raw NUnit XML is under `build-output/unity-card-readability-20261001/` (`hand-min96-editmode-20261001.xml`, `event-copy-editmode-20261001.xml`, `shared-context-ownership-editmode-20261001.xml`, `card-editor-effects-gui-editmode-20261001.xml`, `card-editor-artpicker-gui-editmode-20261001.xml`). These targeted passes do not replace the failed full Unity gate.
- Previously completed narrow UI copy checks remain separate: counter 1/1, unavailable-state localization 3/3, and placeholder/event structure 1/1 in `build-output/unity-card-readability-20261001/`. Shared-content A/B and lifecycle/snapshot cases remain the separate 5/5 result under `build-output/unity-card-readability-20260930/`; neither subset represents all Unity tests. Native scrollbar click/scroll was observed, but native card drag/drop remains unverified. No post-change screenshot or full native match is claimed.
- Independent regression handoff: .NET 909/909 passed after correcting only the mixed published/compatibility victory-objective test fixture (`build-output/mainline-nonunity-gates-20260930/full-dotnet-after-fixture-20261001.trx`; exact case `victory-objective-after-fixture-20261001.trx`); Java 79/79 passed. Cards schema 91/91, four decks, manifest 320/320, and sanity check passed. The schema run emitted NU1900 feed warning. Alignment output is inventory only, not a contract gate. `nightshift-index` exited 1 because the local `.codex/config.toml` is ignored and untracked; no local config or index was changed.
- Remaining handoff risks: approved three-language summary-counter labels for root/rampant/commit/cloud/pull are still PL/HUMAN_REQUIRED, so existing useful values remain visible and labels were not invented. Native card drag/drop, full Unity EditMode/PlayMode/build/smoke, foreground Player usability, and full native match remain unverified. No rules, card values, balance, engine/runtime behavior, unrelated files, or dirty work were changed for these test-only repairs; no commit, push, cleanup, or release occurred.

## 2026-10-01 supplemental Unity GUI verification and runner diagnosis

- After the historical headless failure, the original project was opened and exercised through Unity 6.3 LTS GUI Editor using explicit assembly filters. Full EditMode **331/331 passed** and full PlayMode **33/33 passed**. Raw evidence: build-output/unity-card-readability-20261001/gui-full-editmode-assembly-run-20261001.json, gui-full-editmode-assembly-pipeline-status-20261001.json, and gui-full-playmode-assembly-run-20261001.json / gui-full-playmode-assembly-status-20261001.json. The PlayMode run includes the CPU full-match natural-result → restart → menu journey and controlled EventSystem drag/drop/discard regressions; this is not native OS mouse acceptance.
- Normal GUI Editor shutdown was confirmed: the one 6000.3.21f1 Editor process exited without a save/discard prompt; no Save or Discard action was chosen. run-unity-runtime-validation.ps1 -ValidateOnly then returned projectOpen=False.
- A fresh official runner execution, build-output/unity-runtime-validation/20261001-092517-d4304e7c, stopped in its first EditMode stage: **331 total / 320 passed / 11 failed / 0 skipped**, exit 1. All 11 are “No graphic device is available to initialize the view” from 10 CardEditorEffectSpecEditModeTests plus CardEditorWindowEditModeTests.CardEditorArtPickerLoadsAssetAndShowsFallbackForProgrammaticArtwork. This isolates a harness/configuration mismatch: the official runner supplies -nographics to GUI-dependent Editor tests. Because the script stops on its failed EditMode summary, this invocation did not run PlayMode, Windows build, or Player smoke. The separate GUI-filtered suites above pass. No assertions were removed or weakened, and the runner was not edited.
- Test-only stale expectations from the earlier 18-failure run remain synchronized with current behavior: minimum hand width 96, approved EVENT SUMMARY heading, and borrowed shared-context ownership. Their complete suites passed in the GUI Editor. git diff --check for the four affected Unity test files and this report is clean.
- Remaining validation: Windows64 build and Player smoke have not yet run for this continuation; official headless runner remains blocked by its -nographics choice for these 11 GUI tests. Native card drag/drop is still unverified, native scrollbar click/scroll was observed, and foreground Player usability is not claimed. Existing rule/counter-term HUMAN_REQUIRED items and the nightshift-index local ignored-file mismatch remain unchanged. No commit, push, source cleanup, save, or release occurred.

## 2026-10-01 supplemental Windows build and Player startup probes

- After the official runner stopped at EditMode, the same Windows build entrypoint was invoked independently against a fresh output root, after ValidateOnly confirmed the project was closed. BuildPipeline succeeded (exit 0) and produced build-output/unity-runtime-validation/manual-build-player-smoke-20261001/player/DominionWars.Unity.exe. The build log shows OnPostprocessBuild received result=Unknown and queued its existing EditorApplication.update cleanup; the command-line build method then observed success and returned under -quit before that callback consumed the cleanup. The owned project staging root remained present with 5 card JSON and 4 deck JSON files, marker, ignore file, and generated metadata. It was not deleted or edited. This run demonstrates why the official script's post-build “generated data absent” check would fail, and is not a clean full-runner pass.
- Player startup using the official 10-second smoke limit did not reach a ready marker; its raw log stops at Begin MonoManager ReloadAssembly and matches none of the exception patterns. A single bounded retry against the same built Player with a 30-second window reached “Dominion Wars runtime screen flow ready: TITLE shell active.” and had 0 exception-pattern matches. Raw logs are player/player.log (10-second no-marker attempt) and player/player-30s-smoke.log (bounded retry). Treat the 30-second retry as a limited startup smoke only; the official 10-second stage remains failed and this is not native foreground-player acceptance.
- No project-generated staging data was cleaned, no rule/card source was changed, and the Unity build script or build preprocessor has not been altered. Before claiming full Unity build acceptance, the deferred cleanup timing and runner’s absent-root postcondition need a narrow approved fix or a runner strategy that allows the existing owned-root cleanup to complete and verifies no generated JSON payload remains.

## 2026-10-02 下一周额度接手入口

本段更新并接手上方 2026-10-01 的旧 WIP/runner 状态；原始失败与历史记录保留，不把局部验证写成发布完成。

### 当前可复用基线

- .NET Release 为 **909/909 PASS**：修复后的 fixture 已实际写入 src/Engine/Tests/VictoryObjectiveContractTests.cs，冻结 SHA-256 DF97E0D9B0779D504A5219AA49FF50976B3AF3BF7CF6B25B44B707B9EA831CE0；完整 raw 为 build-output/mainline-nonunity-gates-20260930/full-dotnet-after-fixture-20261001.trx，精确用例为同目录 victory-objective-after-fixture-20261001.trx。该 fixture 文件仍是 untracked，未 stage/commit。
- Java 为 **79/79 PASS**：运行结果摘录 build-output/mainline-nonunity-gates-20260930/fullstack-results-extract-20261001.md，build/TestMain transcript 为 build-output/mainline-nonunity-gates-20260930/java-regression-transcript-20260930.txt。
- 原项目 GUI Editor 完整过滤套件为 EditMode **331/331 PASS**、PlayMode **33/33 PASS**；启动过滤与全结果分别见 build-output/unity-card-readability-20261001/gui-full-editmode-assembly-run-20261001.json、gui-full-editmode-assembly-pipeline-status-20261001.json、gui-full-playmode-assembly-run-20261001.json、gui-full-playmode-assembly-status-20261001.json。PlayMode 的全局旅程和拖牌覆盖是受控 EventSystem 测试，不是原生鼠标证据。

### 未闭合事实与保护边界

- 官方 runner 最新 raw build-output/unity-runtime-validation/20261001-092517-d4304e7c/editmode-results.xml 为 **320/331 PASS、11 FAIL、0 skip**；11 条全是 -nographics 下 CardEditor 图形设备不可用，不是游戏逻辑回归，因此 runner 在 EditMode 停止，未执行后续 PlayMode。
- Windows64 BuildPipeline 入口已独立构建成功，但 OnPostprocessBuild 的 result=Unknown 把 ownership/fingerprint 清理延迟到 EditorApplication.update；命令行 -quit 先返回，owned staging 未清理。10 秒 Player smoke 未到 ready；30 秒有界重试到 TITLE shell ready、异常扫描为0，仅限启动证据。原始记录在 build-output/unity-runtime-validation/manual-build-player-smoke-20261001/。
- 当前残留的生成数据位于 unity/DominionWars.Unity/Assets/StreamingAssets/data/.runtime-data-generated 所属目录；build-output/ 下测试、Player 与报告产物均按 ignore 规则管理。禁止手工删除这些路径来凑绿；保留其它既有 dirty/untracked 文件与 Unity 生成的 .meta。
- Native 卡牌拖放/独立 exe 完整对局仍未验收；root/rampant/commit/cloud/pull 摘要计数器缺批准三语术语，继续交 PL/HUMAN_REQUIRED，不隐藏数值、不自行翻译。无实际 PL 阅读/回复凭证，目前只有仓库 AI_MAILBOX 通知。

### 下一周执行顺序（每项均不得改变规则、平衡、素材/卡图、玩家身份或隐藏信息）

- **T1 — runner 测试参数**：仅从 EditMode/PlayMode test stages 移除 -nographics，保留 -batchmode；验收为官方过滤阶段分别完整跑出 331/331 与 33/33、0 graphics/device failures。
- **T2 — owned staging 完成时序**：构建成功后同步消费现有 ownership 与 output-fingerprint 清理判定，并只处理由本次 build 证明归属的生成内容；验收为 fresh Windows64 输出成功、owned root 内无生成 JSON payload，而人工 StreamingAssets 与未知文件保持原样。
- **T3 — 完整验证与冷启动**：串行跑完整官方 runner，并把 Player ready 时间窗口设为有界冷启动观测；验收为 EditMode/PlayMode、Win64 build 全绿且 Player 在≤30秒报告正式 ready marker、原始日志无异常扫描命中。
- **T4 — 原生独立 exe 对局**：以真实鼠标在独立 Player 走完抽牌/攻击/伏击/COMMIT-PUSH-PULL/弃牌/结算/重开/菜单并保存状态与事件证据；若 computer-use 不支持原生拖牌，明确拆成 HUMAN_REQUIRED 手动验收，不以 EventSystem 模拟冒充原生通过。

### 2026-10-04 owner 分工更新（覆盖本窗口此前页面任务）

- 页面布局与素材由 owner 指定的另一个对话负责；本窗口仅推进游戏底层、C#/Java 一致性、AI、Data/Adapters、相关测试及构建支持。页面表现继续沿用既有批准规则，本窗口不再自行调整布局或美术方向。
- 跨层入口/契约/配置修改实行单一修改方，先列受影响文件及兼容性；禁止页面复制规则，禁止底层绑定素材路径。未知 dirty/untracked 内容保留，不因分工批量提交、推送或清理。
- 当前基线与剩余事项以 docs/HANDOFF_TO_CODEX_2026-10-02.md 第十至十二节为准：.NET 918/918、Java 84/84；Unity batch Edit 332/332、Play 32/33，GUI/新 Windows build/原生完整对局未完成，不能沿用上述历史数字声明当前全绿。
- PL 仓库交接已准备；实时 relay 预检因本文件超过 20,000 字符失败，未调用 PL、未执行自动流程。此附记不表示 relay READY，不放宽门禁。
