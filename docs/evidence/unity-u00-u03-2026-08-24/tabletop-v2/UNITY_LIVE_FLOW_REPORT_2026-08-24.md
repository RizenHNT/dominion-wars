# Unity Live Flow Report — Tabletop v2

Date: 2026-08-24 (Asia/Tokyo)  
Project: `C:\Users\USER\Documents\dominion-wars-win64\unity\DominionWars.Unity`  
Scene: `Assets/Scenes/RuntimeBootstrap.unity`  
Editor: Unity 6000.3.21f1, interactive Editor PID 33236, Pipeline port 7800

## Result

The tabletop-v2 live flow was exercised through the running uGUI panel and its real button/drag components. Production PLAY_CARD and DISCARD were accepted. The transient fixture was explicitly labelled `NON_AUTHORITATIVE` and was used only for ATTACK/PULL evidence because the production card data does not yet advertise that fixture lifecycle. The fixture was destroyed, the production `RuntimeBootstrap` binding was restored without saving the scene, and Play Mode was stopped.

Final Editor state: `ready`, `playMode=stopped`, `compiling=false`, `domainReloadInProgress=false`.

## Production flow

| Interaction path | Action | Revision | Accepted | Evidence/result |
|---|---|---:|---:|---|
| `UnityEngine.UI.Button.onClick` | `skip_ambush_0` | 1 → 2 | True | Phase became ACTION. |
| `RuntimeBattleCardDrag` invalid drop/end | `play_21` | 2 → 2 | N/A — no submission | Card returned to `OwnHandFaceUp`; drag ended; revision did not change. |
| `RuntimeBattleCardDrag` → `RuntimeBattleDropZone` | `play_22_core:shared_castle` | 2 → 3 | True | Castle target highlighted; status `Action accepted: PLAY_CARD (action.accepted)`. |
| `UnityEngine.UI.Button.onClick` | `play_21` | 3 → 4 | True | PLAY_CARD accepted. |
| `UnityEngine.UI.Button.onClick` | `play_28` | 4 → 5 | True | PLAY_CARD accepted. |
| `UnityEngine.UI.Button.onClick` | `end_turn_0` | 5 → 6 | True | Phase became DISCARD. |
| `UnityEngine.UI.Button.onClick` | `discard_0_0` | 6 → 7 | True | Advertised `requiredCount=0`; phase advanced to AMBUSH. |
| `UnityEngine.UI.Button.onClick` | `skip_ambush_1` | 7 → 8 | True | Phase became ACTION for player 1. |
| `UnityEngine.UI.Button.onClick` | `end_turn_1` | 8 → 9 | True | Phase became DISCARD. |
| `UnityEngine.UI.Button.onClick` | `discard_1_6` | 9 → 10 | True | Advertised `requiredCount=6`, 14 candidates; hand 14 → 7, grave 0 → 7, current player became 0. |

The successful `discard_1_6` is the v2 result; the earlier v1 `action.discard_count_mismatch` reproduction is not the result of this run.

## `NON_AUTHORITATIVE` fixture flow

The existing `PullLifecycleData` fixture was instantiated only in Play Mode, remained inactive as a scene object, and was explicitly bound to the live `RuntimeBattlePanel`. It was never saved.

Fixture setup used real uGUI buttons and every submission was accepted:

- `skip_ambush_0` revision 1 → 2
- `play_1` revision 2 → 3
- `play_2` revision 3 → 4; commit queue became 1
- `end_turn_0` revision 4 → 5
- `discard_0_0` revision 5 → 6; Cloud became 1
- `skip_ambush_1` revision 6 → 7
- `end_turn_1` revision 7 → 8
- `discard_1_0` revision 8 → 9
- `skip_ambush_0` revision 9 → 10

| Interaction path | Action | Revision | Accepted | State transition |
|---|---|---:|---:|---|
| `RuntimeBattleCardDrag` → castle `RuntimeBattleDropZone` | `attack_3_core:shared_castle` | 10 → 11 | True | Status `Action accepted: ATTACK (action.accepted)`; castle life 74. |
| `RuntimeBattleCardDrag` → `MechanicalCloudCommit` | `pull_3_2` | 11 → 12 | True | Status `Action accepted: PULL (action.accepted)`; Cloud 1 → 0, Grave 0 → 1, PullCount 0 → 1. |

## Fresh production startup and Console boundary

Historical Pipeline eval compilation failures caused by earlier spelling mistakes occurred before this boundary and are recorded as historical tooling mistakes only; they are not counted in the clean runtime window.

- Fresh baseline cursor: **409**.
- Production Play Mode startup after fixture removal: one scene bootstrap, zero fixture objects, panel bound to `DominionWarsRuntimeBootstrap`.
- First-frame inspection: frame 3755, snapshot revision 1, turn 1, phase AMBUSH, viewer `player_0`.
- Cursor 409 → 410 contained exactly one normal log: `Dominion Wars runtime bootstrap ready.`
- New-window warnings: **0**.
- New-window errors: **0**.
- `dropped=false`.
- Production Play Mode was then exited; Editor returned to `ready`.

## Screenshot evidence

All 28 retained PNG files were decoded and their IHDR dimensions checked. Every evidence stem has both a real 1280×720 image and a real 1440×900 image:

- `production-initial-{1280x720,1440x900}.png`
- `production-illegal-drag-{1280x720,1440x900}.png`
- `production-illegal-after-{1280x720,1440x900}.png`
- `production-play-before-{1280x720,1440x900}.png`
- `production-play-drag-highlight-{1280x720,1440x900}.png`
- `production-play-accepted-{1280x720,1440x900}.png`
- `production-discard-before-{1280x720,1440x900}.png`
- `production-discard-accepted-{1280x720,1440x900}.png`
- `fixture-attack-before-{1280x720,1440x900}.png`
- `fixture-attack-drag-highlight-{1280x720,1440x900}.png`
- `fixture-attack-accepted-{1280x720,1440x900}.png`
- `fixture-pull-before-{1280x720,1440x900}.png`
- `fixture-pull-drag-highlight-{1280x720,1440x900}.png`
- `fixture-pull-accepted-{1280x720,1440x900}.png`

Before cleanup, each of the 28 `Assets/Temp/pipeline-screenshots/tabletop-v2-*.png` files matched its retained evidence copy by SHA-256. Those 28 temporary PNGs and their 28 Unity `.meta` files were then removed. The obsolete evidence file `production-initial-1280x720-wait.png` was also removed. No tabletop-v2 temporary screenshot remains under `Assets/Temp`.

## Scope guard

No production code, test code, scene, prefab, project setting, or runtime configuration was saved or changed. No commit or push was performed.
