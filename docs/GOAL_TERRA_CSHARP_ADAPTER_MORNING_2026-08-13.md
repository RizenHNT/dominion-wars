# Terra Morning Goal — C# Runtime Contract and Adapter Integration

Status: HUMAN_APPROVED_FOR_BOUNDED_RELAY  
Transport preflight: READY FOR NO-COST SANDBOX PROBE — isolated `agents/nightshift-rehearsal` contains current `main`, is clean, and passes relay validation. A real manual `-SandboxPreflightOnly` run in a normal Windows user session remains required before unattended launch; it makes no model call.  
Timebox: one unattended working morning  
Primary implementation agent: Codex/Terra  
Planning gate: Claude or MiniMax PL  
Independent QA: DeepSeek  
Authoritative audit: `ARCHITECTURE_REVIEW.md`

## Mission

Advance every safe, currently unblocked task needed to make the new C# Engine the sole Unity runtime authority and to close the Adapter Integration boundary. Work autonomously through the queue instead of stopping at the first missing decision. Do not start Renderer/UI development, do not change game design, and do not remove Java.

The expected morning outcome is not necessarily a completed game. It is the maximum evidence-backed progress possible within the approved contract/adapter scope, with every remaining blocker made explicit and immediately actionable.

This file constrains both a manual Terra handoff and the audited relay. It does not itself launch an agent. An unattended relay requires a passing `scripts/auto-relay/start-relay.ps1 -ValidateOnly`; never bypass its branch, sandbox, credential or ownership gates.

## Fixed human decisions

1. C# Engine is the new and future sole runtime authority.
2. Java is temporary parity evidence, not a second future runtime.
3. Java must remain intact until a later goal confirms C# parity, Unity integration and regression coverage and the owner separately authorizes removal.
4. Unity/UI may not duplicate legality, targets, phase progression, punish resolution, damage, death or victory logic.
5. This goal does not authorize visual direction, balance changes, releases, paid services, credentials, pushes or destructive operations.

## Operating rules

- Read root `AGENTS.md`, `ARCHITECTURE_REVIEW.md`, `docs/AI_WORKFLOW.md`, `docs/RULES.md`, `docs/DESIGN.md`, `docs/SPEC.md` and current contract files before editing.
- Record starting HEAD and dirty paths. Treat every pre-existing dirty/untracked path as owner data.
- Only write paths listed in `docs/DAILY_GOAL.md`.
- PL must resolve technical shape from existing approved behavior. If a choice changes product behavior or lacks evidence, mark only that item `HUMAN_REQUIRED` and continue the rest.
- One coherent batch at a time. Validate after each batch so later failures have a small cause surface.
- Do not change status to done from code inspection alone. Runtime claims require runtime evidence.
- Do not commit. The owner will review the accumulated morning work before deciding how to preserve it.

## Queue

### M0 — Preflight and drift audit

Actions:

- Capture HEAD, exact dirty list and hashes of any allowed existing files before editing.
- Confirm which contract versions physically exist and whether a 1.31 artifact has appeared.
- Reconcile the audit findings against current C# files so stale WBS claims do not become implementation assumptions.
- Detect available .NET, Java and Unity runners without installing anything or elevating permissions.

Exit criteria:

- Morning report contains the immutable starting evidence.
- Every later task is classified `READY`, `DEPENDENCY_BLOCKED`, `HUMAN_REQUIRED` or `ALREADY_SATISFIED_WITH_CURRENT_EVIDENCE`.

### M1 — PL Contract 1.31 freeze

Owner: Claude/MiniMax PL. Terra must not impersonate PL.

Required decisions:

- one machine-readable contract version rule and 1.30 migration statement;
- lowerCamelCase wire serialization and null/unknown-field policy;
- strict GameSnapshot substructures and viewer/audience redaction;
- LegalActionSet versus submitted GameAction and ActionResult;
- required `matchId`, `snapshotRevision`, action identity and idempotency behavior;
- approved action types and per-type payloads;
- entity/core/prompt target ID grammar;
- UIEvent type/payload matrix, event-time metadata, parent policy and unknown-event behavior.

Exit criteria:

- Contract-dependent production work starts only from explicit PL-approved entries.
- Any unresolved Castle, prompt, punish, leader-ability or visibility product choice is isolated as `HUMAN_REQUIRED`; it must not block unrelated serialization/version/action-envelope work.

### M2 — Contract conformance foundation

Actions:

- Add or complete strict schema/golden validation for approved 1.31 messages.
- Prove wire property casing and required version fields using serialized JSON, not DTO property inspection.
- Add invalid fixtures for missing version, wrong version, unknown enum, malformed ID, missing action revision and invalid event ancestry.

Exit criteria:

- Valid fixtures pass and invalid fixtures fail for the intended reason.
- A repeatable `scripts/validate-runtime-contract.ps1` command reports exact fixture totals.
- No 1.30 file is silently rewritten or renamed as 1.31.

### M3 — C# Snapshot and visibility boundary

Actions:

- Separate engine state from viewer-scoped presentation projection where approved.
- Add stable match/revision fields and strict validation.
- Prevent presentation output from containing an opponent's hidden card identity, ambush identity or deck order.
- Keep legality and visibility decisions inside the authoritative runtime boundary, not Unity UI.

Exit criteria:

- Two-viewer tests prove expected public/private differences.
- Repeated projection of one committed state is deterministic.
- IDs remain stable for their documented lifetime.

### M4 — LegalAction/GameAction/ActionResult boundary

Actions:

- Establish a single C# action ingress around the authoritative turn router.
- Bind advertised actions and submitted actions to match and snapshot revision.
- Validate exact actionId/type/actor/source/target/payload membership against the current LegalActionSet.
- Define accepted/rejected ActionResult with reason key and resulting revision where approved.

Required negative tests:

- wrong match;
- stale/future revision;
- duplicate submission;
- wrong actor;
- action not advertised;
- actionId/type mismatch;
- target/payload mismatch;
- action after game over.

Exit criteria:

- Every negative case has no unauthorized state side effect.
- UI input cannot bypass the authoritative ingress.
- Unresolved action types remain explicitly unavailable instead of being guessed.

### M5 — UIEvent correctness boundary

Actions:

- Split event projection responsibility from snapshot projection.
- Preserve stable ID, parent, event-time turn/phase/revision and approved payload.
- Make single-event and batch-event unknown-type behavior consistent.
- Add event cursor/deduplication/gap policy at the adapter boundary where approved.

Exit criteria:

- Duplicate, missing parent, cycle, out-of-order/gap and unknown-type tests have explicit results.
- Historical events retain their occurrence metadata.
- No localized log string is parsed to create semantic UI events.

### M6 — Unity RuntimeAdapter foundation

Actions:

- Create only the nonvisual `Contracts`, `Transport`, `Adapter`, `Bootstrap` and `Presentation` foundations approved by the contract.
- Keep contract/wire DTO code free of `UnityEngine` when practical.
- Add main-thread dispatch, cancellation, stale-response rejection, event deduplication and fake-session fixtures.
- Use Unity package resolution only if the existing environment permits it without bypasses.

Exit criteria:

- Static assembly dependencies point from Unity runtime toward contracts/session interfaces, never from Engine toward Unity.
- No Unity file implements rule decisions.
- EditMode tests run when Unity is genuinely available; otherwise static work is reported separately as `BLOCKED_RUNTIME_VERIFICATION`.

### M7 — Minimal nonvisual end-to-end trace

Actions:

- Exercise: create C# match → viewer snapshot → select advertised action → authoritative ActionResult → new snapshot → ordered UIEvents.
- Use deterministic fixtures and no formal Renderer/UI.
- Compare selected rule outcomes with Java only as parity evidence.

Exit criteria:

- Trace records match ID, starting and resulting revision, action ID, event IDs and final state assertions.
- No mock is described as a real Unity or full-match pass.

### M8 — Independent QA and repair loop

Owner: DeepSeek for classification; Codex/Terra for approved repairs.

Actions:

- Run build, regression, sanity, alignment and available contract/Unity tests.
- DeepSeek reports reproducible failures without editing production code.
- Codex/Terra repairs implementation defects for at most three cycles.
- Product ambiguity, environment failure or repeated unresolved failure becomes `HUMAN_REQUIRED`/`BLOCKED` while other tests continue.

Exit criteria:

- Exact commands and pass/total/fail/skip counts are recorded.
- The final report never converts skipped Unity tests into success.

### M9 — Morning closeout

Actions:

- Write `docs/ADAPTER_INTEGRATION_MORNING_REPORT_2026-08-13.md`.
- Include WBS ID, status, actual paths, tests, deviations, blockers and next safe action.
- Audit the final dirty list against the starting list and allowed scope.
- Stop without starting UI, build packaging, Java deletion or a new goal.

Exit criteria:

- Report declares exactly one overall result: `ADAPTER_INTEGRATION_GATE=PASS`, `FAIL` or `BLOCKED`.
- PASS is allowed only when all ten conditions in `ARCHITECTURE_REVIEW.md` Step 8 have current evidence.

## Priority when time is insufficient

Complete the current coherent batch and its tests; do not leave an unvalidated half-edit. Priority order:

1. M0–M2: truth, contract and conformance;
2. M3–M5: authoritative C# boundary correctness;
3. M6: Unity nonvisual adapter foundation;
4. M7–M8: end-to-end and independent QA;
5. M9 is mandatory regardless of how far the queue progressed.

## Hard stops

Stop only the affected task and continue independent tasks when:

- approved sources do not determine behavior;
- an edit would touch a forbidden or pre-existing unrelated dirty file;
- Unity needs GUI/license/package/network authority not already available;
- a dependency requires a new provider, credential, subscription or external publication;
- three repair loops fail on the same defect.

Stop the entire run immediately for suspected credential exposure, destructive-operation risk, repository target uncertainty or conflicting instructions that could damage owner work.
