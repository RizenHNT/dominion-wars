# Unity runtime verification — 2026-08-21

## Outcome

The currently implemented Dominion Wars Unity runtime slice is verified in the
licensed Unity Editor and as a freshly built Windows player. This closes the
previous package-resolution, compile, EditMode, PlayMode, scene-bootstrap, and
Windows-player smoke gates for the interactive GUI path.

This report does **not** claim that the complete game UI or all card mechanics
are finished. It verifies the current bootstrap/runtime-adapter slice only.

## Environment

- Unity Editor: `6000.3.21f1`
- Project: `unity/DominionWars.Unity`
- Scene: `Assets/Scenes/RuntimeBootstrap.unity`
- License path: interactive Unity Hub/Editor session
- Git operations: no commit, push, reset, clean, or staging performed

## Verification evidence

| Gate | Result | Evidence |
|---|---:|---|
| .NET Release tests | PASS | 428/428 passed, 0 failed, 0 skipped |
| Card schema | PASS | 91/91, 0 failed, 5 files |
| Deck validation | PASS | 4/4, 0 failed, 91 cards |
| Design manifest | PASS | 320/320 files, manifest PASS |
| Java historical regression | PASS | 38/38; executed only as the existing compatibility regression, with no Java changes |
| Unity EditMode | PASS | 24/24 in the licensed Unity Test Runner |
| Unity PlayMode | PASS | 2/2 in the licensed Unity Test Runner |
| Latest Windows compile | PASS | 0 `error CS`, 0 `warning CS` in the latest build segment |
| Windows build | PASS | Unity result `Succeeded`; 38 seconds; 254 files; 150,220,095 bytes |
| Windows player smoke | PASS | `Dominion Wars runtime bootstrap ready.` observed; 0 scanned runtime exceptions |
| PowerShell parser | PASS | 31 scripts, 0 parser errors |
| Diff whitespace check | PASS | exit 0; existing LF/CRLF notices only |

Fresh Windows build:

`C:\Users\USER\Documents\DominionWarsBuildValidation-20260821-1115\DominionWars.Unity.exe`

Executable SHA-256:

`C8B0D73DC40E4F2CDDBF656CFB7257FCB8273DA22E44E12A8694CD8E275C6FB2`

Player smoke log:

`unity/DominionWars.Unity/Logs/player-smoke-20260821-1115.log`

## Technical changes in this verification batch

- Added a Unity compiler response file using `-nullable:annotations`, removing
  the Unity-side CS8632 compiler warning without changing runtime semantics.
- Added an explicit runtime-ready log marker after the canonical adapter has
  received its initialization snapshot and event delta.
- Added two PlayMode smoke tests:
  - the bootstrap scene publishes the expected first snapshot and legal action;
  - the runtime battle panel binds to the scene bootstrap adapter.
- Extended `scripts/run-unity-runtime-validation.ps1` to validate EditMode,
  PlayMode, Windows build output, staged-data cleanup, player readiness, and
  common runtime exceptions. It refuses an open project, uses a per-user mutex,
  writes unique output, and stops only the player process it starts.
- Added nullable annotation context to Unity-local runtime, UI, and EditMode
  sources that contain nullable reference annotations.

## Remaining limitation

Unattended Unity CLI execution remains environment-blocked. A headless Editor
process reports that the `com.unity.editor.headless` entitlement is unavailable,
even though the interactive Hub/Editor license works. The automated script must
therefore remain a fail-closed future gate; it is not reported as passing.

The same functional gates were completed through the licensed GUI instead:
EditMode 24/24, PlayMode 2/2, a clean Windows compilation, a successful fresh
build, and a launched-player readiness smoke.

The PlayMode tests verify scene snapshot publication and battle-panel adapter
binding. They do not click the rendered button in PlayMode; the advertised
`SKIP_AMBUSH` submission and `ACTION` refresh are covered by the EditMode
adapter/panel test instead. The player smoke proves bootstrap readiness and the
absence of the scanned startup exceptions, not complete UI interaction, every
card mechanic, visual approval, or long-duration stability.

An Input Manager deprecation notice may still appear when reopening the project.
It is not a compiler or runtime failure and is intentionally left for a later
input-system migration decision.

## Reversibility and ownership

All changes remain uncommitted for PL/QA review. Existing unrelated dirty and
untracked files were preserved. The independently added verification surface is
limited to the PlayMode test folder, `Assets/csc.rsp`, the runtime-ready marker,
the validation script changes, Unity-local nullable directives, this report,
and Unity-generated matching `.meta` files.
