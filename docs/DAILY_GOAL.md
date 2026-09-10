# Daily Goal — Current Unity playable closeout and independent PL/QA review

Status: READY
Date: 2026-09-08

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
