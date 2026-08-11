# Night Shift Workflow

## Purpose

The night shift is a bounded implementation and verification loop. MiniMax M3 plans and performs final review, Codex implements, and DeepSeek independently evaluates repository diffs plus test evidence. The human owner remains the final authority. Version 2 deliberately permits only one executable task per live night until per-task worktree isolation exists; that task may receive at most three repair cycles.

## State machine

```text
READY -> PLANNED -> IMPLEMENTING -> QA
                                  |  |
                                  |  +-> PASS -> COMPLETE
                                  +----> FAIL -> IMPLEMENTING (maximum 3 cycles)

Any state -> HUMAN_REQUIRED
All executable tasks settled -> PL_REVIEW -> PL_APPROVED | PARTIAL | HUMAN_REQUIRED
```

The scheduler validates dependency graphs and runs only dependency-ready tasks. Simulation mode exercises independent-task continuation, but live version 2 limits the plan to one executable task so a failed partial edit cannot contaminate another task.

## Roles

- MiniMax PL: reads the approved goal, creates bounded tasks, and performs final evidence review.
- Codex: runs with a per-task permission profile that grants writes only to the approved paths and the Git-ignored `nightshift-agent-tmp/` scratch directory. The profile has no tool network access and uses non-interactive `approval_policy=never`; permitted operations proceed without a dialog, while requests outside the boundary fail closed and become `HUMAN_REQUIRED`. No legacy `--sandbox` mode or bypass flag is combined with the profile.
- Test runner: executes only repository-owned allowlisted profiles under the `nightshift-test` Codex permission profile. The repository is read-only to tests except for `build/`; direct network, Git metadata writes, and source writes are denied. Test scratch is `build/nightshift-test-tmp/`. The DeepSeek credential directory also has a private Windows ACL, and preflight stops the night if that ACL is inherited or grants access beyond the current user, LocalSystem, and local Administrators.
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
- `main` and `master` are rejected; live mode additionally requires an `agents/nightshift-*` isolated branch.
- Live locks, ledgers, raw provider output, usage, and scheduler state live outside the repository at `%LOCALAPPDATA%\DominionWarsNightshift\state\<worktree-id>/`, below the private credential ACL. No-paid simulations alone use Git-ignored `.nightshift/simulation-state/`.
- The framework never commits, pushes, merges, resets, or deletes user files.
- Failed scope validation becomes `HUMAN_REQUIRED`; the framework does not attempt an automatic rollback.
- Allowed paths reject absolute paths, drive/UNC paths, traversal, globs, reparse points, broad repository roots, credentials, and automation control files. Task IDs are bounded before they can become filenames.
- The starting branch, commit, and controller hashes must remain unchanged throughout the run.

## Unattended reliability

- Planner: 10 minutes per attempt; Codex: 60 minutes per repair cycle; each test: 20 minutes; QA: 15 minutes per attempt; final PL: 10 minutes per attempt.
- Transient provider failures receive at most three total attempts. Authentication, quota, billing, and invalid-key failures are not retried.
- The whole runner has a 5 hour 30 minute deadline; Windows Task Scheduler has a 5 hour 45 minute final limit.
- Every external command is placed in a Windows kill-on-close Job Object. A timeout terminates its complete descendant process tree; the same job caps active processes, process/job memory, and priority. Free disk is sampled during execution and the tree is stopped if it falls below the emergency threshold.
- The private-state `nightshift.lock` prevents manual, daytime, and scheduled entry points from overlapping. The versioned goal ledger keeps the most recent 200 executions, separates simulations from live goals, and makes an exact live goal execute once; manual `-RetryGoal` is required after review for a rerun.
- State and reports use atomic replacement. An ordinary exception produces a current `HUMAN_REQUIRED` report; an abrupt power loss is recognized from the stale `RUNNING` ledger on the next launch.
- `last-scheduled-status.json` in private state distinguishes no-goal, duplicate, stale, running, approved, partial, human-required, and bootstrap-failed outcomes. A concurrent launch adds a log entry without overwriting the active run status.
- Provider inputs are secret-pattern redacted and hard-truncated. `usage.json` and the night report show the live attempt count plus any token usage returned by MiniMax, Codex, and DeepSeek; a fixed nightly model-attempt ceiling stops further calls.
- Repository tests and Codex tool processes receive a minimal environment allowlist rather than inheriting VS Code/terminal variables; token, key, password, cookie, session, and credential variables are removed before launch.

## Test profiles

| Profile | Command |
|---|---|
| `build` | `scripts/build.bat` |
| `regression` | Java `com.dominionwars.test.TestMain` |
| `sanity` | `python scripts/sanity_check_v2.py` |
| `alignment` | `python scripts/align_check.py` |
| `nightshift-index` | Verifies that `docs/FILE_INDEX.md` contains every committed night-shift entry and the local-state note |

## Operating modes

```powershell
# No model calls and no edits; validates prerequisites and prints the intended flow.
powershell -ExecutionPolicy Bypass -File scripts/nightshift/run-nightshift.ps1 -DryRun

# Uses the real state machine and allowlisted tests, but no paid model calls or edits.
powershell -ExecutionPolicy Bypass -File scripts/nightshift/run-nightshift.ps1 -Simulation

# Proves that PL_APPROVED is emitted only when every executable task completes.
powershell -ExecutionPolicy Bypass -File scripts/nightshift/run-nightshift.ps1 -Simulation -SimulationScenario Success

# Proves a nested hung process tree is killed and reported instead of waiting all night.
powershell -ExecutionPolicy Bypass -File scripts/nightshift/run-nightshift.ps1 -Simulation -SimulationScenario Timeout

# Runs path and malformed-plan attack cases without model calls.
powershell -ExecutionPolicy Bypass -File scripts/nightshift/run-nightshift.ps1 -SafetySelfTest

# One small human-approved goal on a clean night-shift branch.
powershell -ExecutionPolicy Bypass -File scripts/nightshift/run-nightshift.ps1
```

Windows Task Scheduler invokes `invoke-scheduled-nightshift.ps1`, a minimal watchdog that then starts the runner with `-Scheduled`. It binds status to a fresh per-launch identifier, contains the runner process tree, and enforces a deadline shorter than the scheduler's final limit. Even a missing/broken main config produces `BOOTSTRAP_FAILED` state. A missing `Status: READY` goal is a successful no-op. A READY goal is accepted only during the configured early-morning window, while its file is fresh, and when its `Date:` is today or yesterday. Start/finish/failure records are appended to the private-state `scheduler.log`.

Register or repair the 01:00 task from the isolated, clean night-shift worktree:

```powershell
# Inspect the exact task without changing Windows.
powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts/nightshift/setup-nightshift-task.ps1 -ValidateOnly

# Register/replace the task, enable AC wake timers, and keep battery wake disabled.
powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts/nightshift/setup-nightshift-task.ps1
```

The task uses the current user's interactive token at limited privilege, requires network, ignores overlapping launches, starts when a recent trigger was missed, wakes the computer when Windows permits it, and never starts from battery by default. Setup always places the first trigger in the future, so registering after 01:00 does not immediately replay today's missed run. It can continue if AC is disconnected mid-run so Task Scheduler does not hard-kill a partially edited worktree.

For the unattended night: connect AC power, keep the Windows user signed in, keep the network available, and lock the screen. VS Code may be closed. Do not shut down or sign out. Sleep is allowed only after AC wake timers have been enabled and a real short wake test has succeeded on this machine; until that test, leave the computer awake. Do not rely on closing the lid because the lid policy may hibernate or power off the machine.

## Start from MiniMax PL during the day

After the human owner approves a concrete goal, MiniMax PL may invoke the daytime wrapper instead of waiting for the 01:00 trigger:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/nightshift/start-day-shift.ps1 `
  -Goal "Concrete outcome" `
  -AllowedPath "src/example" `
  -AcceptanceCriteria "Observable result" `
  -TestProfile regression
```

The wrapper requires the isolated worktree to be clean, validates and round-trips the requested scope/test profiles, rejects Markdown control injection, writes the approved input atomically to the private state directory outside the repository, and then runs the same audited pipeline. It never commits or pushes; implementation and report changes remain uncommitted for human review.

## One-time credential setup

MiniMax and Codex use their existing CLI logins. DeepSeek needs a credential that a scheduled process can read without placing plaintext in the repository:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/nightshift/setup-deepseek-key.ps1
```

For an existing credential, tighten and verify its Windows ACL without entering the key again:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/nightshift/setup-deepseek-key.ps1 -HardenOnly
```

The script encrypts the key with Windows DPAPI under `%LOCALAPPDATA%\DominionWarsNightshift`. It can only be decrypted by the same Windows user on the same computer. If the key is rotated, run the setup command again.

## Scheduling gate

The first live rehearsal must run on a clean branch whose name is not `main` or `master`. After the report is reviewed, the human owner chooses the nightly start time and explicitly authorizes Windows Task Scheduler registration. The framework does not create a scheduled task, commit, or push merely because a rehearsal passed.
