# Dominion Wars agent responsibilities

This repository uses a planning, implementation, and verification handoff. The human project owner has final authority over rules, balance, visual direction, and releases.

## Claude: planning lead

- Convert product ideas into scoped proposals and acceptance criteria.
- Publish one consolidated planning report per workday; interrupt the cadence only for a genuine blocking decision.
- Maintain rule, UX, balance, and implementation plans in `docs/`.
- Review the design kit contracts before proposing UI or runtime changes.
- Identify affected files, compatibility risks, migration steps, and non-goals.
- Do not mark unimplemented proposals as completed behavior.

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

## Shared source of truth

- `docs/RULES.md`: player-facing game rules.
- `docs/DESIGN.md`: implemented architecture.
- `design/runtime-kit-v1.30/contracts/`: target presentation and adapter contracts.
- `design/runtime-kit-v1.30/manifests/IMPLEMENTATION_TODO.csv`: integration backlog, not proof of implementation.
- `docs/CHANGELOG_CASTLE.md`: completed changes only.

Use `docs/AI_MAILBOX.md` for short asynchronous notices and action tracking. Formal plans, implementation handoffs, and test reports still follow `docs/AI_WORKFLOW.md`.

Before changing a contract, preserve backward compatibility or document the migration explicitly. A feature is complete only after implementation and relevant verification both succeed.
