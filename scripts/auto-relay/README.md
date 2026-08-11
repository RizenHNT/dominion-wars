# Dominion Wars Auto Relay

## What it is

VS Code custom agents are role definitions, not background daemons. A Markdown `@MiniMax`, `@Codex`, or `@DeepSeek` mention cannot wake an existing chat window. This relay therefore starts the real local provider processes and keeps the approved workflow inside one audited controller:

```text
MiniMax plan -> Codex implementation -> allowlisted tests -> DeepSeek QA
             -> Codex repair (maximum 3) -> MiniMax final review
```

The implementation reuses `scripts/nightshift/start-day-shift.ps1` and `run-nightshift.ps1`. It does not use GitHub comments, unknown bot accounts, webhooks, or a self-hosted runner.

## Start an approved daytime relay

1. MiniMax prepares `docs/DAILY_GOAL.md` with exact allowed paths, acceptance criteria, test profiles, and `Status: READY`.
2. The human owner approves that goal once.
3. MiniMax runs this fixed command:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\start-relay.ps1 -ApprovedByHuman
```

After that command starts, no chat-window handoff is required. The computer must remain powered on, signed in, and online. Locking the screen or closing VS Code does not stop the local controller. Signing out, sleeping without a proven wake configuration, hibernating, or shutting down does.

The entry point refuses to start when the relay is disabled, the private MiniMax relay credential copy is missing or unsafe, the goal is not `READY`, or the isolated night-shift branch does not contain current `main`.

## Status and stop switch

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\get-relay-status.ps1
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\disable-relay.ps1
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\disable-relay.ps1 -Enable
```

The disable marker prevents future starts. It deliberately does not kill a process while files may be mid-write; an active daytime relay checks the marker before every later paid-model call and test stage, then stops cooperatively as `HUMAN_REQUIRED`.

## MiniMax transport probe

`invoke-minimax-relay.ps1` performs one bounded MiniMax M3 request, requires `-ApprovedByHuman`, enforces a maximum of three paid probe attempts per day, requires an exact relay-ID acknowledgement, records the raw response and usage under private `%LOCALAPPDATA%` state, and mirrors the request/reply to `docs/AI_MAILBOX.md`. A request line without the matching model reply is a failure.

## VS Code Agents Window limitation

The VS Code CLI can open the Agents Window, but the installed version does not expose a supported command that injects a prompt into a specifically verified MiniMax PL Agents Window session. `code chat` opens the ordinary Chat view and may route to whichever model that chat uses; it is not an Agents Window transport.

`notify-vscode-pl.ps1` is retained only as a fail-closed compatibility stub. It must not open a chat or report delivery. Routine unattended collaboration uses `start-relay.ps1`, which invokes the configured providers directly and records their identities and outputs. Interactive Agents Window delivery remains a UI action until a verifiable API is available.

## Safety boundaries

- New product scope still requires one human approval. Relay transport never grants product authority.
- No relay script commits, pushes, merges, releases, or changes credentials.
- Provider output and usage live outside the repository under a private ACL.
- The low-level provider helper requires a short-lived, one-time parent-process ticket and must never receive a standalone terminal auto-approval rule.
- `harden-minimax-auth.ps1 -Apply` copies the existing MiniMax configuration bytes without parsing or printing them into a dedicated private `%LOCALAPPDATA%\DominionWarsAutoRelay\mmx` directory. The original `~/.mmx` file is not changed or deleted.
- Relay provider processes receive `MMX_CONFIG_DIR` pointing only to that dedicated private directory. Re-run the setup after intentionally replacing the MiniMax login or API key.
- `@codex-bot` and `@deepseek-bot` are not used; those public GitHub identities are unrelated to this project.
- Existing night-shift locks, timeouts, path sandbox, test allowlist, paid-attempt ceiling, and `HUMAN_REQUIRED` fallback remain authoritative.
