# Codex architecture and rules review — 2026-08-13

## Scope and authority

This is a read-only implementation review for the temporary afternoon handoff. It does not change rules, balance, visual direction, canonical contracts, release state, or Unity project files. `docs/RULES.md`, the approved runtime contracts, and a formal PL/human decision remain authoritative.

## Confirmed foundations

- C# Engine, Adapters and Data projects compile offline on `netstandard2.1`; the current Release regression is 300/300.
- Card and deck data checks pass: 91/91 cards, 4/4 preconstructed decks.
- The design asset manifest covers 320 physical files and passes path, SHA256, alpha metadata and coverage checks.
- Java build and TestMain pass 38/38. Existing Java simulation completes 20, 100 and 1000 groups without timeout in the new stability runner.
- Engine source has no UnityEngine dependency. Unity runtime compilation and package resolution are not proven.

## Architecture gaps

1. The C# layer has state, effects and legal-choice generation, but no single phase/action executor that can complete START → AMBUSH → ACTION → DISCARD → END. `GameState.EndTurnRequested` is a flag, not a turn controller.
2. The transport action schema contains `SET_AMBUSH`, `SKIP_AMBUSH`, `CHOOSE_TARGET` and `DISCARD`, while the C# generator currently exposes `ACTIVATE_PUNISH` and `USE_LEADER_ABILITY` instead. The ownership of these commands must be decided before adding an executor.
3. The Adapter maps only a subset of internal events. The machine-readable report lists 15 internal events without an approved UI mapping and 10 UI event types absent from the map. Filling these by name would be a contract decision, not a mechanical repair.
4. The target names are present in the card schema and resolver, but the engine still lacks an approved target union for minions, leaders, castle and life cores. `ENEMY_TARGET` must not silently choose a target.
5. Unity is a static shell only: local packages are declared in `manifest.json`, but package-lock resolution, compilation, scenes, EditMode, PlayMode and Windows build remain unverified because the interactive Hub/license gate has not completed. `System.Text.Json` is a specific Unity compilation risk.

## Rule and contract decisions still required

- Decide whether the MVP enables Castle mechanics. `RULES.md` includes Castle and shuffle victory progress, while the effects contract describes Castle as disabled for MVP and the UI contracts still expose Castle events.
- Decide the canonical phase/event naming migration. SPEC text uses names such as `phase_change` and `punish_change`; the UI schema uses uppercase discriminated event types such as `PHASE_CHANGED`.
- Decide the authoritative snapshot/action/event version fields. SPEC mentions `snapshotRevision` and `contractVersion`; the current canonical schemas do not expose the same complete field set.
- Decide whether Java-only ambush, chant, discard-hook and full LeaderDef fields remain Java-authoritative or enter a future C# batch.
- Repair the control characters in the leader-resistance section of `docs/RULES.md` only under formal PL ownership; the issue is documentation integrity, not permission to reinterpret the rule.

## Fact drift to resolve before formal QA

`docs/BASELINE.md` and `docs/CURRENT_IMPLEMENTATION_STATUS_2026-08-12.md` contain historical statements that predate the current Data loader, 300-test C# baseline and current Unity shell. They should be updated by PL/Codex with dated historical notes preserved. The WBS phrase “C# + Java engine 100%” should be read as foundation coverage, not as proof of a playable C#/Unity match loop.

## Safe next queue

1. Formal PL resolves phase/action/target/event/Castle decisions.
2. Codex implements the approved C# turn/action boundary and focused tests.
3. Human opens Unity Hub once; Codex/DeepSeek then verify package lock, compile, EditMode and Windows smoke.
4. Frontend work consumes Snapshot/LegalAction/Event only after the adapter contract is stable.

Evidence scripts:

- `scripts/run-regression.ps1` (`-RequireUnity` returns exit 2 while Unity is blocked)
- `scripts/run-java-stability.ps1`
- `scripts/report-engine-alignment.ps1`
- `scripts/report-contract-gaps.ps1`
