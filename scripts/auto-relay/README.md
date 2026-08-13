# Dominion Wars Auto Relay

## What it is

VS Code custom agents are role definitions, not background daemons. A Markdown `@MiniMax`, `@Codex`, or `@DeepSeek` mention cannot wake an existing chat window. This relay therefore starts the real local provider processes and keeps the approved workflow inside one audited controller:

```text
DeepSeek V4 Flash PL plan -> Codex implementation -> allowlisted tests -> DeepSeek V4 Pro QA
                           -> Codex repair (maximum 3) -> DeepSeek V4 Flash PL final review
```

The implementation reuses `scripts/nightshift/start-day-shift.ps1` and `run-nightshift.ps1`. It does not use GitHub comments, unknown bot accounts, webhooks, or a self-hosted runner.

## Start an approved daytime relay

1. The `MiniMax PL` role (backed by DeepSeek V4 Flash) prepares `docs/DAILY_GOAL.md` with exact allowed paths, acceptance criteria, test profiles, and `Status: READY`.
2. The human owner approves that goal once.
3. The approved local relay entry runs this fixed command:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\start-relay.ps1 -ApprovedByHuman
```

After that command starts, no chat-window handoff is required. The computer must remain powered on, signed in, and online. Locking the screen or closing VS Code does not stop the local controller. Signing out, sleeping without a proven wake configuration, hibernating, or shutting down does.

The entry point refuses to start when the relay is disabled, the private DeepSeek credential is missing or unsafe, the goal is not `READY`, or the isolated night-shift branch does not contain current `main`.

## Mobile / Remote use

From the ChatGPT mobile app, open the supported Remote connection to the desktop Codex session and ask Codex for a PL preview. Codex must first run the no-model validation command:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\start-relay.ps1 -ValidateOnly
```

If that passes, Codex runs the `-PlanOnly -ApprovedByHuman` preview entry below. A status question or provider probe never starts development, and a mobile message outside a connected Remote Codex session cannot access this local repository.

For the two-phase review gate, the preview phase runs:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\start-relay.ps1 -PlanOnly -ApprovedByHuman
```

It returns a saved plan and starts no implementation or QA. After the human explicitly approves that displayed plan, Codex runs:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\start-relay.ps1 -ApprovedPlan -ApprovedByHuman
```

The second phase consumes only the hash-bound plan that was shown for review. “停止” invokes `disable-relay.ps1`; the active controller stops cooperatively before its next paid call or test stage.

## Status and stop switch

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\get-relay-status.ps1
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\disable-relay.ps1
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\disable-relay.ps1 -Enable
```

The disable marker prevents future starts. It deliberately does not kill a process while files may be mid-write; an active daytime relay checks the marker before every later paid-model call and test stage, then stops cooperatively as `HUMAN_REQUIRED`.

## Legacy MiniMax transport probe

`invoke-minimax-relay.ps1` is retained as a separately audited legacy probe only. The normal development relay does not use it or consume MiniMax quota; it calls DeepSeek V4 Flash for PL and DeepSeek V4 Pro for QA through the DeepSeek API.

## VS Code Agents Window limitation

The VS Code CLI can open the Agents Window, but the installed version does not expose a supported command that injects a prompt into a specifically verified MiniMax PL Agents Window session. `code chat` opens the ordinary Chat view and may route to whichever model that chat uses; it is not an Agents Window transport.

`notify-vscode-pl.ps1` is retained only as a fail-closed compatibility stub. It must not open a chat or report delivery. Routine unattended collaboration uses `start-relay.ps1`, which invokes the configured providers directly and records their identities and outputs. Interactive Agents Window delivery remains a UI action until a verifiable API is available.

## Safety boundaries

- New product scope still requires one human approval. Relay transport never grants product authority.
- No relay script commits, pushes, merges, releases, or changes credentials.
- Provider output and usage live outside the repository under a private ACL.
- The low-level provider helper requires a short-lived, one-time parent-process ticket and must never receive a standalone terminal auto-approval rule.
- `scripts/nightshift/setup-deepseek-key.ps1` stores the DeepSeek credential outside the repository with a private Windows ACL. The same protected key is used for PL and QA.
- `@codex-bot` and `@deepseek-bot` are not used; those public GitHub identities are unrelated to this project.
- Existing night-shift locks, timeouts, path sandbox, test allowlist, paid-attempt ceiling, and `HUMAN_REQUIRED` fallback remain authoritative.
