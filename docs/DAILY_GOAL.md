# Daily Goal

> Human-approved input for the 2026-08-12 night shift. Scope is limited to closing the unfinished PL-approved C# Engine Batch 1 QA follow-up.

Status: READY
Date: 2026-08-12

## Goal

Complete the remaining PL-approved Batch 1 technical follow-up: reconcile the five real-card schema gaps and persistent-aura representation, align effects.contract target aliases with the Java/data baseline, and add or verify explicit tests for pioneer pressure and EventLog root/monotonic behavior. Do not redesign game rules, balance, UI, or automation.

## Allowed scope

- `data/schema/cards.schema.json`
- `docs/effects.contract.md`
- `docs/SPEC.md`
- `src/Engine`
- `src/Adapters`
- `src/Engine/Tests`

## Test profiles

- `build`
- `regression`
- `sanity`
- `alignment`

## Acceptance criteria

- The card schema either validates all current card JSON fields (including the five reported gaps) or records a precise fail-closed migration decision without silently rejecting valid cards.
- DISABLE_ENEMY_LEADER persistent-aura behavior is represented explicitly and is not miscounted as one of the 24 ordinary IEffect actions.
- effects.contract.md and the Java/data baseline agree on SELF, ANY_MINION, and accepted enemy-target aliases, with a contract test or reproducible check.
- Pioneer-pressure behavior and EventLog root/monotonic invariants have explicit tests or a documented environment blocker; no test may claim success without evidence.
- Run the available build/regression/sanity/alignment checks; report exact commands, results, and any unavailable Unity/.NET runner.

## Human decisions already made

- Unity 6 LTS + C# is the approved direction; Java remains the behavior baseline.
- The C# Batch 1 scope is approved for technical completion and QA repair.
- No Unity Editor installation, new provider, credential change, Git push, merge, release, or history rewrite is authorized tonight.

## Forbidden tonight

- Changes to `docs/RULES.md`, balance values, frontend/web presentation, or visual direction.
- Changes to credentials, scheduled-task/relay automation, `.github/agents/`, `AGENTS.md`, or `docs/AI_MAILBOX.md`.
- Destructive deletion, commit, push, merge, release, or force-push.

## Start gate

This goal is READY for the 2026-08-12 scheduled run. If a requested change would alter product intent or require an unavailable Unity/.NET environment, mark it `HUMAN_REQUIRED` and continue independent verification.
