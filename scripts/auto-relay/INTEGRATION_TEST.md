# Auto Relay Integration Test

## Zero-cost checks

```powershell
# Parse every relay script.
Get-ChildItem scripts\auto-relay\*.ps1 | ForEach-Object {
  [void][scriptblock]::Create((Get-Content -Raw -LiteralPath $_.FullName))
}

# Install/update the dedicated private relay credential copy without printing its contents.
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\harden-minimax-auth.ps1 -Apply

# Audit the private relay copy without reading its contents.
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\harden-minimax-auth.ps1

# Validate the fixed daytime entry without calling a model.
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\start-relay.ps1 -ValidateOnly

# Confirm the unsupported VS Code Chat-view route fails closed.
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\auto-relay\notify-vscode-pl.ps1 -MailboxHeading "automatic relay handoff"

# Exercise the existing no-provider simulations.
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .nightshift\rehearsal\scripts\nightshift\run-nightshift.ps1 -Simulation -SimulationScenario Success
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .nightshift\rehearsal\scripts\nightshift\run-nightshift.ps1 -Simulation -SimulationScenario Mixed
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .nightshift\rehearsal\scripts\nightshift\run-nightshift.ps1 -Simulation -SimulationScenario Timeout
```

Expected: parser checks pass; the private relay copy reports only USER/SYSTEM/Administrators and leaves `~/.mmx` unchanged; validate-only reports whether a goal is ready without provider calls; `notify-vscode-pl.ps1` exits nonzero without opening any chat; simulations exit `0`, `2`, and `3`; timeout leaves no child process.

## One real MiniMax acknowledgement

This is a paid provider call and must be explicitly authorized. Generate one unique ID and use it once:

```powershell
$relayId = 'RELAY-PROBE-20260811-XXXXXXXX'
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
  -File scripts\auto-relay\invoke-minimax-relay.ps1 `
  -ApprovedByHuman `
  -RelayId $relayId `
  -Message 'Codex reports that the local automatic relay is ready for PL acknowledgement.'
```

Pass only when all of the following are true:

1. `docs/AI_MAILBOX.md` contains a Codex request with the unique ID.
2. The MiniMax CLI was actually invoked in non-interactive mode.
3. The raw provider response and usage were saved under private local state.
4. MiniMax returned `status: ACKNOWLEDGED` with the exact same ID.
5. The mailbox contains the mechanically recorded MiniMax reply with that ID.

Writing only an `@MiniMax` line, manually copying a reply, receiving a different ID, or producing no usage-bearing provider response is a failure.

## Failure and rollback checks

- Run `disable-relay.ps1`, then verify both the daytime entry and MiniMax probe fail before any paid call; an active daytime relay must stop before its next provider call or test stage.
- Re-enable only after review.
- Rename a copy of the ID and verify idempotency prevents the same successful ID from being billed twice.
- Set `docs/DAILY_GOAL.md` to `DRAFT` and verify the daytime entry refuses to start.
- Verify an out-of-date isolated branch fails before the implementation provider is called.
