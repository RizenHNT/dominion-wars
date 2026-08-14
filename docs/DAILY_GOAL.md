# Daily Goal — C# Runtime Contract and Adapter Integration Morning

Status: READY
Date: 2026-08-13
Human approval: The owner has confirmed that C# Engine is the future sole runtime authority. Java is a temporary behavior oracle only and must not be removed in this goal. The owner has approved one unattended morning run over the bounded scope below.

Relay preflight state: `READY_FOR_NO_COST_SANDBOX_PROBE` — isolated `agents/nightshift-rehearsal` now contains `main`, is clean, and passes `start-relay.ps1 -ValidateOnly`; formal permission profiles are committed as `a1f4d67` / `54359c6`. Before any live relay, run the manual `-SandboxPreflightOnly` probe in a normal Windows user session. It performs no model call and must prove the restricted test sandbox plus developer write-allow/write-deny checks.

## Goal

Execute the approved C# runtime-contract and Adapter Integration morning queue in `docs/GOAL_TERRA_CSHARP_ADAPTER_MORNING_2026-08-13.md`, using `ARCHITECTURE_REVIEW.md` as the audit baseline. MiniMax PL must first freeze every implementable Contract 1.31 decision without inventing game rules; Codex/Terra then implements all unblocked items in dependency order; local tests and DeepSeek QA run after each coherent batch; Codex may repair reproducible defects for at most three loops. Continue all independent work when one item is blocked. Do not begin Renderer/UI work and do not remove or expand Java.

## Allowed scope

- `design/runtime-kit-v1.31/contracts`
- `src/Engine`
- `src/Adapters`
- `src/Engine/Tests`
- `src/Tests.EditMode`
- `unity/DominionWars.Unity/Assets/DominionWars.Runtime`
- `unity/DominionWars.Unity/Assets/DominionWars.Tests.EditMode`
- `unity/DominionWars.Unity/Packages/packages-lock.json`
- `scripts/validate-runtime-contract.ps1`
- `docs/ADAPTER_INTEGRATION_MORNING_REPORT_2026-08-13.md`

## Acceptance criteria

- MiniMax PL records a machine-actionable Contract 1.31 decision set before any contract-dependent production edit; unresolved product semantics are marked `HUMAN_REQUIRED` and are not guessed.
- Contract 1.31, if created, explicitly defines version fields, lowerCamelCase wire names, match/snapshot revision, LegalAction versus GameAction, ActionResult, target IDs, viewer visibility, event-time metadata and unknown-field behavior, with a documented 1.30 migration rule.
- C# is the only new runtime implementation target. Java files are not changed or deleted; Java is used only for parity evidence.
- Every implemented GameAction is bound to match and snapshot revision and is rejected when wrong-match, stale, duplicate, actor-mismatched, payload-mismatched or not advertised by the current LegalActionSet.
- Any implemented Snapshot projection is viewer-scoped and has tests proving that opponent hand, ambush identity and deck order are not leaked.
- Any implemented UIEvent carries stable event identity, causal parent, event-time turn/phase/revision and an approved payload; no log-text parsing or silent unknown-event filtering is introduced.
- Unity runtime work is limited to contract validation, transport/session abstraction, presentation state, event cursor, main-thread dispatch and GameAction submission; it contains no legality, targeting, phase, punish, damage, death or victory rules.
- Contract-dependent tasks that cannot be safely implemented are left `BLOCKED`, while independent conformance tests, mapping inventories, fail-closed guards and adapter foundations are completed where possible.
- Available build, regression, sanity and alignment profiles are run after implementation; exact commands, passed/total/failed/skipped counts and unavailable Unity layers are reported without synthetic success.
- `docs/ADAPTER_INTEGRATION_MORNING_REPORT_2026-08-13.md` lists WBS IDs attempted, actual files, test evidence, unresolved decisions, `PASS/FAIL/BLOCKED` per item and the exact next safe task.
- `ADAPTER_INTEGRATION_GATE=PASS` may appear only if all ten Gate conditions in `ARCHITECTURE_REVIEW.md` Step 8 have current evidence; otherwise the report must say `BLOCKED` or `FAIL`.
- Existing unrelated dirty files remain untouched. The run does not commit, push, merge, release, rewrite history or delete files.

## Test profiles

- build
- regression
- sanity
- alignment

## Human decisions already made

- C# Engine is the future sole runtime authority.
- Java remains temporarily as the behavior/parity oracle and is removed only by a later separately approved cleanup goal.
- The current goal covers contract and adapter integration only, not Renderer/UI, visual direction, balance, release or Java deletion.
- Routine MiniMax → Codex/Terra → tests → DeepSeek → repair handoffs inside this goal do not require repeated approval.

## Execution order

1. Preflight the exact HEAD, dirty paths, existing contract versions and available Unity state.
2. MiniMax PL freezes the non-invented Contract 1.31 decision packet and separates `READY` from `HUMAN_REQUIRED` items.
3. Codex/Terra executes WBS 10.10.2 through 10.10.8 in dependency order, skipping blocked semantics but continuing independent tasks.
4. Run allowlisted tests after each coherent batch; DeepSeek independently classifies failures.
5. Codex/Terra repairs reproducible implementation defects for at most three loops without expanding scope.
6. Produce the morning report and stop. Do not continue into UI, Windows packaging or Java removal.

## Forbidden tonight

- Do not modify `docs/RULES.md`, balance data, card/deck data, existing 1.30 contracts, Java source, Web/Swing frontend, Renderer/UI, visual assets, credentials, agent configuration, relay/nightshift scripts or another agent's reports.
- Do not invent rules, product behavior, action semantics, event semantics, Castle policy, hidden-information policy or migration compatibility when the approved sources do not answer them.
- Do not treat Java behavior as new product authority; use it only to test C# parity until the owner approves its removal.
- Do not use `git add .`, `git add -A`, reset, clean, rebase, merge, commit, push, release, force-push or destructive deletion.
- Do not bypass Unity licensing, sandbox, package, ownership or approval failures. Record them as `BLOCKED` and continue independent work.
- Do not claim Unity compile, EditMode, PlayMode, Windows build or Adapter Gate success without current execution evidence.

## Start gate

This goal is `READY` for one human-approved unattended morning relay. `READY` authorizes only the bounded paths and behavior-preserving work above. A missing approved Contract 1.31 decision blocks dependent edits but does not block inventory, conformance scaffolding, fail-closed tests, reporting or other independent tasks. Any decision that changes game rules, balance, visual behavior, release state or Java-removal timing is `HUMAN_REQUIRED`.

Operational note: `READY` records owner approval of the work content; it does not override relay safety gates. Until `start-relay.ps1 -ValidateOnly` passes, the same goal may be given manually to Terra in an attended session, but it is not approved to bypass or weaken the isolated-worktree check.
