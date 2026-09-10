# Daily Goal — Playable Unity Card Demo (U-03R → U-07)

Status: **COMPLETE — readable draggable card-table demo and current executable gates PASS**
Date: 2026-09-01
Human approval: The owner requires the placeholder build to be playable as a card game, not merely testable through debug-like boxes and action buttons. Continue independently until a player can read visible cards, drag legal cards/attackers to clear legal targets, receive invalid-drop feedback, use the required match actions, and complete a match without the UI inventing engine rules.

## Current playable-demo goal

- Show human-readable card names and all presentation metadata already available from the canonical 91-card `CardCatalog`; do not show internal IDs as the primary card title.
- Provide a large, readable inspect surface for face-up cards while preserving opponent hidden-hand redaction.
- Make dragging visibly lift the source card, follow the pointer at the correct Canvas scale, highlight only engine-advertised targets, submit once on a legal drop, and return on an illegal/cancelled drop.
- Keep the action rail as an accessibility/fallback path, but make the board itself the primary play/attack interaction surface.
- Preserve the existing `PLAY_CARD`, `ATTACK`, `END_TURN`, `DISCARD`, and `PULL` adapter boundaries and the U-00→U-03 regression gates.
- Use placeholder art and neutral presentation only. Final visual direction, balance, rules, and card behavior remain human-owned.

## Playable-demo hard boundaries

- Snapshot and `LegalActions` remain authoritative for hidden information, legality, targets, phase progression, resolution, and victory.
- `CardCatalog` is presentation metadata only; it may label a visible card but may not make a card visible or legal.
- No engine/rule/Adapter change unless a specific UI feature is impossible without it and the defect is recorded under the existing lower-layer exception policy.
- Do not import the 540-card design bundle, change balance, commit, push, clean, reset, or remove unrelated dirty files.

## Playable-demo acceptance

- At 1280×720 and 1440×900, a player can identify every own-hand card and open a readable full detail view.
- Opponent hand remains card backs/count only; no catalog lookup may reveal it.
- Legal source cards and exact targets are understandable before drop; invalid drops do not advance revision and visibly restore the card.
- A real runtime session can visibly exercise play, attack, end turn and PULL when those actions are advertised, then show terminal outcome/recovery.
- Relevant EditMode, PlayMode, .NET, runtime-contract, Windows build and foreground visual smoke evidence is recorded before this goal is marked complete.

## Final verification — 2026-09-01

- Readability: every face-up snapshot card now binds canonical name/type/faction/rules/keywords/cost/punish/printed or current stats into a reusable card face; hover/click opens the large reader. Hidden opponent hand remains backs/count only.
- Interaction: live production UI exercised an actual card `BeginDrag → advertised drop-zone highlight → Drop`; result was `highlight=True`, `action.accepted`, hand `6 → 5`. Invalid-drop rollback, exact target matching, attack targeting and single-submit protection remain covered by the drag suite.
- Mechanical lifecycle: `COMMIT` is now a real advertised zero-fee player action for an own-field mechanical non-leader. The non-authoritative lifecycle fixture proves manual `COMMIT → END/FIFO PUSH → top-only PULL → graveyard`; positive fees remain fail-closed until a payment model is approved.
- Gates: .NET **539/539**; runtime contract **14 valid / 12 expected-invalid / 0 fail**; Unity EditMode **192/192**; PlayMode **6/6**; final Windows x64 build **Succeeded**, **0 errors / 1 unrelated PipelineManager warning**; that final Player captured a real 1280×720 BATTLE framebuffer at `build-output/unity-demo-evidence/player-ambush-ui-final-20260901.png` and exited normally.
- Content limitation, not a UI/runtime blocker: the canonical 91-card set still has no authored COMMIT/PUSH/PULL lifecycle payloads, so ordinary production deck selection does not naturally advertise PULL. No card values were invented or changed; the UI/action path is ready for the approved card redesign data.

## UI operation closure — ambush, drag and battlefield (2026-09-01)

- `SET_AMBUSH` is now an authoritative AMBUSH-phase action rather than a UI-only placeholder. Setting pays the existing punish cost, keeps tags unconsumed until trigger, respects one-set-per-turn and LOCKDOWN, and projects only the viewer-owned ambush identity.
- The engine now resolves one deterministic response per opponent action for `OPPONENT_ATTACKS`, `OPPONENT_PLAYS_CARD`, `OPPONENT_PLAYS_SPELL`, `OPPONENT_SUMMONS`, and `OPPONENT_DRAWS`; NORMAL accumulates, FOCUS suppresses further responses for that turn, NEGATE cancels the matching attack/effect window, and triggered non-leaders move to grave.
- Live UI proof used production title/setup/battle controls and actual `BeginDrag → RuntimeBattleDropZone.OnDrop`: `sea_devour` moved from hand to the dedicated ambush lane; after END/DISCARD and the opponent's start draw, ambush count `1 → 0`, owner grave `0 → 1`, and the opponent's two drawn cards were discarded (`hand` remained 9).
- UX follow-up: a hot-seat viewer change used to erase/cover the trigger cue with `PHASE CHANGED`. The panel now consumes the public event delta before redaction refresh, maps `AMBUSH_SET`/`AMBUSH_TRIGGERED`, and gives the trigger priority within that submission. Visual evidence: `build-output/unity-demo-evidence/ui-operation-ambush-feedback-20260901.png` visibly reads `AMBUSH TRIGGERED`.
- Drag/battlefield checks remain real-operation evidence, not source inspection: visible lifted card, cyan legal target surface, invalid-drag restoration, minion hand→field, and both leader/minion field→enemy attack submissions were exercised. Evidence is under `build-output/unity-demo-evidence/ui-operation-*.png`.
- Card readability was also exercised through the live production interaction, not inferred from tests: pointer hover on an actual hand card opened the large reader with canonical title, type/faction, costs, stats, rules, keywords and data-status text; beginning a drag closes it so it cannot steal a legal drop. Evidence: `build-output/unity-demo-evidence/ui-operation-card-inspect-20260901.png`.

一句话日报：**Unity 可玩牌桌 70% → 100%（本 Demo 周期）；今天完成可读卡面/大图查看、真实拖放出牌/攻击/伏击触发、机械手动 COMMIT 与 COMMIT→PUSH→PULL 闭环，并通过 .NET 539/539、EditMode 192/192、PlayMode 6/6、Windows 构建及前台 Player BATTLE 截图。**

---

# Previous Daily Goal — Content Pipeline + Card Editor MVP (C-00 → C-06)

Status: **IMPLEMENTING — C-00→C-06 code/editor gates PASS; Windows Player build PASS; final foreground visual smoke pending. U-04→U-06 presentation follow-up PASS.**
Date: 2026-08-25
Human approval: The owner approved the generic Content/Visual Asset Pipeline and Schema-driven Card Editor MVP. This cycle keeps the current 91-card production set, adds no new rules or balance decisions, and does not import the 540-card design bundle.

## Previous cycle — archived and complete

The Unity placeholder match cycle U-00 → U-03 is **COMPLETE / PASS**, not the active goal. Its evidence remains in:

- docs/evidence/unity-u00-u03-2026-08-24/tabletop-v2/UNITY_LIVE_FLOW_REPORT_2026-08-24.md
- docs/UNITY_U00_U03_DAILY_REPORT_2026-08-24.md
- docs/KNOWN_ISSUES_CYCLE_U00_U03_2026-08-24.md

The U-00 → U-03 gate passed with EditMode **65/65**, PlayMode **3/3**, .NET **466/466**, Unity compilation **0 errors**, and fresh Console boundary **0 warnings / 0 errors**. Its placeholder visual follow-ups are not authorization to expand or rewrite this new content goal.

## Current goal

Make the following workflow safe and data-driven:

~~~text
drop asset in the documented inbox
→ import to library and generate manifest
→ choose Schema fields, costs, registered mechanics and artId in Card Editor
→ preview and validate
→ atomically save card data and content references
→ Unity packages and resolves the same content by assetId
~~~

The runtime remains authoritative for rules, legality, targeting, phase progression, victory and effect resolution. The editor and UI may present registered metadata but may not implement those rules.

## Approved MVP scope

- Add the documented data/content/inbox, library and manifests boundaries.
- Add stable assetId/artId references and legacy data/art fallback.
- Generate Card Editor fields and existing mechanic choices from Schema/engine descriptors, not a second hand-written list.
- Add card-art, board, card-back, castle, leader, faction-frame and UI-icon manifest roles.
- Add a default skin and a second test skin with role-based asset overrides.
- Make Unity build staging copy validated content manifests and referenced assets into an owned Assets/StreamingAssets/content directory.
- Add schema, registry, manifest, path-safety, hash, fallback, transaction and backup tests.
- Provide the non-programmer “文件放哪里” workflow in docs/CONTENT_PIPELINE_AND_CARD_EDITOR_SPEC_2026-08-25.md.
- Reserve typed audio/SFX/VFX IDs with no-op/fallback behavior only; actual playback and effects are out of scope.

## Hard boundaries

- Do not change docs/RULES.md, game balance, effect semantics, victory logic or canonical card behavior.
- Do not import, merge or overwrite the 540-card bundle_v2.json.
- Do not delete data/art, the legacy Java editor, historical design assets or unrelated dirty files.
- Do not put hand-authored files in Assets/StreamingAssets; it is generated build output.
- Do not allow absolute paths, arbitrary scripts, unknown required mechanics or silent asset overwrites.
- Do not claim a new mechanism is usable until its engine handler, descriptor and tests are present.
- This documentation handoff itself changes only docs/; it does not commit, push, release or publish.

## Execution order

1. **C-00 — Contract:** freeze content manifest, asset roles, ID grammar, fallback and legacy alias rules.
2. **C-01 — Registry:** connect Schema fields and C# mechanic descriptors; add drift checks against EffectDispatcher.
3. **C-02 — Importer:** implement inbox validation, library copy, hash calculation, manifest generation and recoverable backup.
4. **C-03 — Card Editor:** generate fields/effect choices, asset picker and preview from the shared registry/store.
5. **C-04 — Unity runtime:** add ContentCatalog/ContentResolver, skin/theme load and owned build staging.
6. **C-05 — Verification:** run Schema, .NET, Unity EditMode/PlayMode/build, manifest, fallback and interrupted-save tests.
7. **C-06 — Handoff:** publish the one-page human workflow, known issues and exact PASS/FAIL/BLOCKED evidence.

An independent failure does not authorize guessing a rule or weakening fail-closed behavior. Record it in docs/KNOWN_ISSUES_CONTENT_PIPELINE_2026-08-25.md and continue only with safe independent work.

## Acceptance criteria

- A user can create, copy, validate, save and reload a legal card from the Card Editor without hand-editing JSON.
- The editor exposes fields and mechanisms from Schema/registry; a newly implemented and registered mechanism appears automatically.
- An artId resolves a custom card image; a missing optional image uses a visible fallback and does not crash.
- Two skins can switch board, card back, castle, leader/faction frames and UI icons.
- The Unity build uses packaged content and does not depend on the repository working directory.
- Duplicate IDs, unknown required mechanisms, path traversal, hash mismatch, malformed manifest and missing required fallback fail closed.
- Interrupted card or manifest saves leave the previous valid state recoverable.
- The current 91 cards, four decks and legacy data/art aliases remain loadable.
- Audio/SFX/VFX are validated only as optional manifest interfaces; no playback claim is made.

## Progress report

Use one sentence per workday:

Content MVP C-0N X% → Y%；今天完成 <具体边界>；验证 <命令/门禁>；未完成项 <阻塞或下一步>。

No percentage may be reported as complete without current implementation and relevant QA evidence.

## Verification update — 2026-08-31

- 本轮内容管线、Card Editor 和 Runtime UI 结构改动已完成当前可独立验证范围；connected Unity Editor 全量 EditMode **132/132**、PlayMode **6/6**，.NET Release **521/521**，runtime contract **13 valid / 11 expected-invalid / 0 fail**，recompile `failed=false`。
- 内容基线仍为卡 **91/91**、牌组 **4/4**、设计素材清单 **320/320**；内容专项为 **17/17**（ContentPipeline 9/9、RuntimeContentResolver 7/7、CardEditor picker/preview 1/1）。这些证据不等同于 Windows Player final smoke 已通过。
- 仍待人工/PL：Restart 是否创建新 session，以及 seed/match-id 如何生成；Exile、城堡屏障、统领生命的权威引擎字段；LeaderZone 是否聚合 Field/AmbushZone 中的统领。当前 UI 对缺少的权威字段保持 `Unavailable`，不自行推断。
- 当前下一门禁是 Windows Player build/package 与独立启动 smoke；在主代理补齐真实证据前，不把本周期标记为完整发布完成。

## Active references

- Specification: docs/CONTENT_PIPELINE_AND_CARD_EDITOR_SPEC_2026-08-25.md
- Known issues: docs/KNOWN_ISSUES_CONTENT_PIPELINE_2026-08-25.md
- Current card Schema: data/schema/cards.schema.json
- Current production cards: data/cards/*.json
- Current runtime data staging: unity/DominionWars.Unity/Assets/DominionWars.Editor/RuntimeDataStreamingBuildPreprocessor.cs

## Archived previous goal — Unity Placeholder Match U-00 → U-03

Status: **COMPLETE — U-00 → U-03 CYCLE ONLY**
Date: 2026-08-24
Human approval: The owner approved one and only one goal for this cycle: Unity must use placeholder assets to play one complete match from U-00 through U-03. Lunar Max is the execution agent; the main agent only coordinates and reports.
Completion gate: **PASS** — U-00, U-01, U-02, and U-03 passed the approved placeholder-cycle acceptance. This is **100% of the U-00 → U-03 cycle**, not 100% of the whole game or its final visual production.

## Goal

From launching the Unity project to the end of a match, the user can:

- **U-00 — Board:** see the table and the current match state with placeholder assets.
- **U-01 — Phase:** see the current phase/turn and its transition.
- **U-02 — Legal actions:** see and click actions advertised as legal by the runtime boundary.
- **U-03 — Match flow:** play a card, attack, end the turn, perform `PULL`, and reach the match end without leaving the flow.

The Unity front end consumes canonical snapshots, legal actions, events, and action results. It must not reproduce legality, targeting, phase progression, or victory rules.

## Pre-start questions — resolved

1. **Runtime card data:** production reads the 91-card canonical set under `data/cards/*.json`; Unity stages the same set into `Assets/StreamingAssets/data`. The 540-card `bundle_v2.json` is a design bundle, not a runtime input. Its import is not part of U-00 → U-03 and has not been assigned a delivery cycle; it requires a later, separately approved data-migration cycle.
2. **Runtime Contract “6 invalid”:** these are six deliberately invalid fixtures that the validator must reject: `ui_event/bad_phase`, `ui_event/pull_raw_internal_data`, `game_snapshot/missing_version`, `game_action/unknown`, `game_action/payload_target_entity_string`, and `action_result/missing_revision`. Current result is **11 valid / 6 invalid / 0 failures**. Earlier summaries compressed expected rejection fixtures into the overall PASS line; they are not six newly discovered runtime errors.
3. **Deck-cycle victory direction:** `docs/RULES.md` §9 is synchronized with the implementation: the player whose own deck completes an effective cycle increments their own counter, and that player wins at the limit. Making the opponent cycle ten times therefore makes the opponent win and the player who emptied that deck lose.

## Hard freeze on lower layers

Do not change engine, rules, or Adapter code during this cycle except when one of these is proven:

- **a.** Unity cannot open, crashes, or hangs;
- **b.** match/save data is corrupted or state becomes inconsistent;
- **c.** a specific UI feature cannot display correct content without a lower-layer fix. The report must name the exact UI feature and the defect that prevents it.

Every other newly discovered defect — incorrect values, missing rule checks, missing fields, or edge cases — goes to `docs/KNOWN_ISSUES_CYCLE_U00_U03_2026-08-24.md` with severity and the affected file/card. Defer it to the cycle closeout window or the next cycle; do not fix it in place.

## Allowed work

- Unity front-end presentation and interaction under `unity/DominionWars.Unity/Assets/DominionWars.UI` and connected Editor assets, using placeholder visuals.
- Unity Pipeline bootstrap, limited to the project's `Packages/manifest.json` and `Packages/packages-lock.json` entries produced by the official Unity CLI.
- Read-only inspection, runtime verification, and reports needed to demonstrate U-00 → U-03.

Do not hand-edit scene YAML. Do not change engine/rules/Adapter code, card data, balance, contracts, release state, or unrelated dirty files. Do not commit, push, clean, reset, or delete.

## Acceptance criteria

- A Unity run visibly satisfies U-00, U-01, U-02, and U-03 with placeholder assets.
- Buttons/actions are driven by the current legal-action set; an illegal action cannot be submitted as a legal UI action.
- The complete path includes play card, attack, end turn, and `PULL`, and the UI reflects the resulting snapshots/events/action results.
- The UI remains responsive and does not corrupt match or save state.
- The final report records exact verification evidence, known issues, and any blocker under hard-freeze categories a/b/c.

## Final acceptance — 2026-08-24

- **U-00 PASS — Board:** the running `RuntimeBootstrap.unity` scene displayed the placeholder tabletop at both 1280×720 and 1440×900; the fresh production startup bound one production bootstrap and zero fixture objects.
- **U-01 PASS — Phase:** live production revisions advanced through AMBUSH → ACTION → DISCARD and back to the next player while the panel reflected the current player, turn, phase, and snapshot revision.
- **U-02 PASS — Legal actions:** real uGUI button/drag controls submitted advertised actions; an invalid drag of `play_21` left revision **2 → 2** and returned the card, while the legal castle drop `play_22_core:shared_castle` advanced **2 → 3** and was accepted.
- **U-03 PASS — Match flow:** production live evidence covers `PLAY_CARD`, `END_TURN`, and `DISCARD` through revision **1 → 10**. The explicitly **NON_AUTHORITATIVE**, Play-Mode-only fixture covers the otherwise unavailable production `ATTACK` and `PULL` advertisements: `attack_3_core:shared_castle` **10 → 11**, then `pull_3_2` **11 → 12**, with Cloud **1 → 0**, Grave **0 → 1**, and PullCount **0 → 1**. The fixture was never saved, was destroyed after capture, and production binding was restored.
- **Final gates:** EditMode **65/65**, PlayMode **3/3**, .NET **466/466**, Unity compilation **0 errors**, and fresh Console boundary **0 warnings / 0 errors**.
- **Severity closeout:** **P0 = 0**. Remaining P1 visual follow-ups include evidence-image watermark treatment, card text hierarchy/readability, and final art/polish; these are future visual-production work and do not reopen this placeholder-cycle gate.

Authoritative live evidence: `docs/evidence/unity-u00-u03-2026-08-24/tabletop-v2/UNITY_LIVE_FLOW_REPORT_2026-08-24.md`. Final QA conclusion and cycle accounting: `docs/UNITY_U00_U03_DAILY_REPORT_2026-08-24.md`.

## Daily progress report

Before the end of each workday, report one sentence in the form: `Unity 前端 X% → Y%；今天完成 U-0N 的 <具体部分>。` If the percentage does not change, state the concrete blocker and whether it is hard-freeze category a/b/c or another cause. Do not report only “进行中”.

Final 2026-08-24 checkpoint: **20% → 100% for the U-00 → U-03 cycle only**. The earlier 45%, 52%, and 56% entries—including the unverified Software Terms inference, Licensing Client mutex, and `STATUS_NO_INSTANCES`—were historical intermediate checkpoints. They were superseded when the interactive Editor connected on Pipeline port 7800, live tabletop evidence was captured, all final gates passed, the fixture was removed, production binding was restored, and the Editor returned to `ready` with Play Mode stopped.

一句话日报：**Unity 前端 U-00～U-03 周期 20% → 100%；今天完成 U-00 牌桌、U-01 阶段、U-02 合法行动与 U-03 PLAY/ATTACK/END TURN/PULL 的实机终验，最终门禁 EditMode 65/65、PlayMode 3/3、.NET 466/466、Unity 编译 0 错、Console 新边界 0 warning/0 error。**

## Historical note

The former 2026-08-13 C# Runtime Contract and Adapter Integration goal is preserved in `docs/DAILY_GOAL_ARCHIVE_2026-08-13.md`. It is historical context only and is not active authorization for this cycle.
