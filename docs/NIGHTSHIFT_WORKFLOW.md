# Night Shift Workflow

## Purpose

The night shift is a bounded implementation and verification loop. MiniMax M3 plans and performs final review, Codex implements, and DeepSeek independently evaluates repository diffs plus test evidence. The human owner remains the final authority.

## State machine

```text
READY -> PLANNED -> IMPLEMENTING -> QA
                                  |  |
                                  |  +-> PASS -> COMPLETE
                                  +----> FAIL -> IMPLEMENTING (maximum 3 cycles)

Any state -> HUMAN_REQUIRED
All executable tasks settled -> PL_REVIEW -> PL_APPROVED | PARTIAL | HUMAN_REQUIRED
```

Blocked tasks do not block independent tasks. A task runs only after all dependencies are `COMPLETE`.

## Roles

- MiniMax PL: reads the approved goal, creates bounded tasks, and performs final evidence review.
- Codex: edits only approved paths using `codex exec --sandbox workspace-write --approve-for-me`.
- Test runner: executes only repository-owned allowlisted profiles; model-generated shell commands are never executed.
- DeepSeek: receives the actual diff and captured test output, then returns `PASS`, `FAIL`, or `HUMAN_REQUIRED`. It never edits production code.

## Mandatory human gates

Automation must stop or defer a task involving any of the following:

- architecture or engine migration not already approved in `docs/DAILY_GOAL.md`;
- deletion of a core module or broad file removal;
- rule, balance, visual, release, or publishing decisions;
- secrets, credentials, billing changes, or new paid resources;
- dependency installation from an unapproved source;
- commit, merge, push, force push, tag, or release;
- changes outside the task's explicit allowed paths.

## Repository safety

- Live mode requires a clean worktree and a non-protected branch.
- `main` and `master` are rejected.
- Runtime state and raw provider output live under `.nightshift/` and are ignored by Git.
- The framework never commits, pushes, merges, resets, or deletes user files.
- Failed scope validation becomes `HUMAN_REQUIRED`; the framework does not attempt an automatic rollback.

## Test profiles

| Profile | Command |
|---|---|
| `build` | `scripts/build.bat` |
| `regression` | Java `com.dominionwars.test.TestMain` |
| `sanity` | `python scripts/sanity_check_v2.py` |
| `alignment` | `python scripts/align_check.py` |

## Operating modes

```powershell
# No model calls and no edits; validates prerequisites and prints the intended flow.
powershell -ExecutionPolicy Bypass -File scripts/nightshift/run-nightshift.ps1 -DryRun

# Uses the real state machine and allowlisted tests, but no paid model calls or edits.
powershell -ExecutionPolicy Bypass -File scripts/nightshift/run-nightshift.ps1 -Simulation

# Proves that PL_APPROVED is emitted only when every executable task completes.
powershell -ExecutionPolicy Bypass -File scripts/nightshift/run-nightshift.ps1 -Simulation -SimulationScenario Success

# One small human-approved goal on a clean night-shift branch.
powershell -ExecutionPolicy Bypass -File scripts/nightshift/run-nightshift.ps1
```

Do not register a Windows scheduled task until one live, single-task rehearsal finishes with independent QA evidence and a correct `docs/NIGHT_REPORT.md`.

## One-time credential setup

MiniMax and Codex use their existing CLI logins. DeepSeek needs a credential that a scheduled process can read without placing plaintext in the repository:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/nightshift/setup-deepseek-key.ps1
```

The script encrypts the key with Windows DPAPI under `%LOCALAPPDATA%\DominionWarsNightshift`. It can only be decrypted by the same Windows user on the same computer. If the key is rotated, run the setup command again.

## Scheduling gate

The first live rehearsal must run on a clean branch whose name is not `main` or `master`. After the report is reviewed, the human owner chooses the nightly start time and explicitly authorizes Windows Task Scheduler registration. The framework does not create a scheduled task, commit, or push merely because a rehearsal passed.
