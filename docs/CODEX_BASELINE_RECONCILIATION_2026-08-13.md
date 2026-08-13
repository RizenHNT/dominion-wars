# Baseline reconciliation proposal — 2026-08-13

This is a proposed fact-sync note for formal PL review. Existing `docs/BASELINE.md`, `docs/CURRENT_IMPLEMENTATION_STATUS_2026-08-12.md`, `docs/AI_MAILBOX.md` and `IMPLEMENTATION_TODO.csv` already contain dirty or owner-authored content, so this note deliberately does not rewrite them.

## Current verified facts

| Area | Current evidence | Correct interpretation |
|---|---|---|
| C# tests | `dotnet test DominionWars.sln -c Release --no-restore` | 300/300 pass |
| Card schema | `scripts/validate-cards.ps1` | 91/91 pass across 5 files |
| Deck validation | `scripts/validate-decks.ps1` | 4/4 decks pass; 91 card IDs checked |
| Design manifest | `scripts/validate-design-manifest.ps1` | 320 manifest rows, 320 physical files, hashes/coverage pass |
| Java regression | `cmd /c scripts\\build.bat` + `TestMain` | 38/38 pass |
| Java stability | `scripts/run-java-stability.ps1` | 20/100/1000 groups complete with exit 0 and no timeout |
| C# Data loader | `src/Data/CardCatalog.cs`, `src/Data/DeckLoader.cs` | Implemented and covered by current regression; Unity compatibility remains unverified |
| Unity | `unity/DominionWars.Unity` | Static shell exists; UPM lock, compile, scenes, EditMode and Windows build remain blocked/unverified |
| Agent safety | `docs/PROGRESS_WBS.md` §10.9 | Temporary-agent rollback protocol present; recent Codex work is isolated in task commits |

## Historical statements that need labeling

The older baseline notes claiming “no C# loader/System.Text.Json” or “Unity not installed” should be retained only as dated historical snapshots, not as current status. The current environment has the Data project and an Editor/license footprint, but that does not prove Unity compilation or playability.

The WBS phrase “C# + Java engine 100%” should be clarified as “foundation/data/effect/adapter coverage”; the C# phase/action executor and playable Unity loop remain incomplete.

## Proposed PL action

1. Keep the historical baseline entries intact with their original dates.
2. Add a dated “current verification” block using the table above.
3. Update TODO evidence only after PL confirms ownership of the existing dirty CSV.
4. Keep Unity items blocked until interactive Hub package resolution, compilation, EditMode and Windows smoke evidence exist.

No production rule, balance, contract or asset change is implied by this note.
