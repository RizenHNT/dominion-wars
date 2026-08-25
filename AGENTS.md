# Dominion Wars agent responsibilities

This repository uses a planning, implementation, and verification handoff. The human project owner has final authority over rules, balance, visual direction, priorities, and releases. Routine work inside an approved role and scope does not require repeated human confirmation.

## Claude / DeepSeek V4 Flash: planning lead

- Convert product ideas into scoped proposals and acceptance criteria.
- Publish one consolidated planning report per workday; interrupt the cadence only for a genuine blocking decision.
- Maintain rule, UX, balance, and implementation plans in `docs/`.
- Review the design kit contracts before proposing UI or runtime changes.
- Identify affected files, compatibility risks, migration steps, and non-goals.
- Do not mark unimplemented proposals as completed behavior.
- Claude is the permanent planning lead. While Claude is unavailable, DeepSeek V4 Flash temporarily exercises the same planning duties through the audited relay without gaining final product authority. MiniMax is not part of the current planning or review route.

## Codex: implementation lead

- Own production changes under `src/`, `data/`, and `scripts/`, plus the Java-to-Web API boundary.
- Translate approved plans and `design/runtime-kit-v1.30/contracts/` into working adapters and supporting code.
- Keep game rules authoritative in the Java engine; do not duplicate rules in renderers.
- Maintain repository structure, build scripts, tests, and Git history.
- Report implementation differences, validation performed, and remaining risks.

## Human owner and GPT Web: frontend lead

- Own browser UI structure, visual direction, interaction design, and frontend copy.
- Consume canonical snapshots, legal actions, events, asset IDs, and localization keys through the approved adapter boundary.
- Do not reproduce legality, targeting, phase progression, or victory rules in frontend code.
- Ask Claude to resolve product ambiguity and Codex to resolve API or engine ambiguity.

## DeepSeek: test lead

- Derive test cases from acceptance criteria, schemas, manifests, and runtime contracts.
- Run regression, simulation, schema, asset, localization, accessibility, and input-path checks.
- Write reproducible reports with commands, expected results, actual results, and severity.
- Do not edit production code while acting as test lead; send failures back to Codex.
- Keep API keys, virtual environments, and generated private reports outside this repository.

## Working authority and write boundaries

- **Planning lead may act without asking:** read the repository; create or update proposals, specifications, acceptance criteria, PL reports, and its own `docs/AI_MAILBOX.md` entries; assign work inside an already approved goal; choose reversible planning details that do not change product behavior.
- **Planning lead local closeout:** after the human owner asks to commit or archive, the active planning lead may use only a reviewed closeout guard that explicitly matches the active PL owner. Raw Git staging/commit commands and all pushes remain prohibited.
- **Planning lead must not change without approval:** production files under `src/`, `data/`, `scripts/`, or `web/`; canonical rule or balance outcomes; final visual direction; release state; completed-change records without implementation and QA evidence.
- **Codex may act without asking:** implement an approved scope under `src/`, `data/`, `scripts/`, tests, build/configuration files, and the Java-to-Web boundary; run existing builds and tests; fix reproducible implementation defects; maintain technical documentation and local Git commits containing only the approved work.
- **Codex must not change without approval:** game-design intent, balance targets, final frontend appearance or copy, credentials, paid services, destructive history rewrites, protected-branch merges, pushes, or releases. Night/day automation keeps its stricter no-commit and no-push rules.
- **Frontend lead may act without asking:** edit browser UI, presentation structure, interaction implementation, and frontend copy under the approved visual and adapter contracts; use mock data that is clearly labeled as non-authoritative.
- **Frontend lead must not change without approval:** engine rules, legality, targeting, phase progression, victory logic, balance values, or canonical contracts.
- **DeepSeek may act without asking:** read the repository; run existing allowlisted tests and inspections; classify failures; write its own QA reports and mailbox entries; return reproducible implementation failures directly to Codex.
- **DeepSeek must not change:** production code/data, planning decisions, another agent's report section, credentials, or releases. Test-code changes are proposed to Codex unless the human owner explicitly assigns a test-only edit scope.
- **Approval Reviewer may only read and explain approval risk.** It never edits, executes, or grants permission.

## Escalation routing

Do not ask the human owner merely because something is uncertain. Route it first:

- Product scope, UX intent, acceptance criteria, or planning ambiguity → planning lead.
- Engine, API, schema implementation, build, or repository ambiguity → Codex.
- Test coverage, reproduction, severity, or verification ambiguity → DeepSeek.
- Frontend layout, interaction feel, visual treatment, or copy → human owner and GPT Web, collected by the planning lead in the daily report unless it blocks all remaining work.
- Reproducible QA failure → Codex repair loop; do not ask the human owner unless repair would change product intent or exceed the approved scope.
- A task blocked by a human decision → mark that task `HUMAN_REQUIRED`, record one plain-language question, and continue every independent task.

Contact the human owner immediately only for suspected credential exposure or security incident, destructive or difficult-to-recover action, new paid-resource use, external publication/release, force push or protected-branch merge, a final rule/balance/visual decision, conflicting role-owner recommendations, or a decision that blocks all remaining useful work. Batch every other non-urgent human question into the planning lead's single daily report.

The owner grants standing approval for routine model calls and agent-to-agent handoffs inside an already approved goal using the existing configured Codex and DeepSeek services. DeepSeek V4 Flash is the current temporary PL/review provider when Claude is unavailable; MiniMax is not an active provider. Do not ask for confirmation again merely to report completion, request QA, return a reproducible failure, or obtain PL review. A new provider or subscription, a higher budget, exhausted quota, or work outside the approved goal still requires the normal escalation.

## Shared source of truth

- `docs/RULES.md`: player-facing game rules.
- `docs/DESIGN.md`: implemented architecture.
- `design/runtime-kit-v1.30/contracts/`: target presentation and adapter contracts.
- `design/runtime-kit-v1.30/manifests/IMPLEMENTATION_TODO.csv`: integration backlog, not proof of implementation.
- `docs/CHANGELOG_CASTLE.md`: completed changes only.

Use `docs/AI_MAILBOX.md` for short asynchronous notices and action tracking. Formal plans, implementation handoffs, and test reports still follow `docs/AI_WORKFLOW.md`.

For an already approved daytime goal, `scripts/auto-relay/start-relay.ps1` is the handoff entry. It invokes the real DeepSeek V4 Flash PL, Codex, allowlisted tests, and DeepSeek V4 Pro QA through the audited controller; a VS Code custom-agent mention or GitHub comment alone is never proof that another agent started. Relay transport does not expand any role's write authority or remove the existing human gates.

## Mobile / Remote Codex trigger

- A mobile message received through a supported ChatGPT Remote connection to this desktop Codex session may request a PL preview or execution of the already approved `Status: READY` goal.
- For a PL preview, Codex must first run `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\start-relay.ps1 -ValidateOnly`; only if it passes may Codex run the same entry with `-PlanOnly -ApprovedByHuman`. This phase calls PL only and returns a reviewable plan; it must not start Codex, tests, QA, or repairs.
- After the human explicitly says to approve/execute that displayed plan, Codex must run the validation command again and then invoke `scripts\auto-relay\start-relay.ps1 -ApprovedPlan -ApprovedByHuman`. The runner verifies the saved plan hash, goal hash, branch, HEAD, and control-file hashes before starting Codex, tests, DeepSeek V4 Pro QA, repairs, and PL final review.
- A human “停止” command invokes `scripts\auto-relay\disable-relay.ps1`; the active relay stops cooperatively before its next paid call or test stage. Do not claim an immediate kill while a process may be mid-write.
- The relay calls DeepSeek V4 Flash PL and DeepSeek V4 Pro QA directly through the audited controller. Do not try to wake an Agents Window with `@` mentions or `code chat`.
- A request to inspect status, probe providers, or explain the workflow must not start a live development run. If the Remote host is offline, signed out, asleep, or unavailable, report that instead of claiming execution.

The owner authorizes routine Codex-to-PL handoffs inside an approved goal without repeated confirmation, but transport must prove the actual recipient. `code chat` targets the ordinary Chat view and must never be described as delivery to the Agents Window or to DeepSeek V4 Flash PL. `scripts/auto-relay/notify-vscode-pl.ps1` is disabled until VS Code exposes a supported route that can select and verify the Agents Window recipient. Use the audited headless relay for unattended work; do not claim an interactive handoff succeeded merely because a window or chat opened.

Before changing a contract, preserve backward compatibility or document the migration explicitly. A feature is complete only after implementation and relevant verification both succeed.
