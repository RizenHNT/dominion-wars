# Unity U00–U03 live-flow evidence — 2026-08-24

## Environment and final gate

- Project: `unity/DominionWars.Unity`
- Scene: `Assets/Scenes/RuntimeBootstrap.unity`
- Editor: Unity `6000.3.21f1`, single connected Editor PID `31024`, Pipeline port `7800`
- Final state: Play Mode, scene not dirty, fixture GameObject absent after the run
- Production runtime screenshots were captured at `1280×720` and `1440×900` with Pipeline `capture_game_view`, `source=screen`.
- After the final screenshot pass, `clear_console` was issued. Console queries newer than cursor `386` returned **0 errors / 0 warnings**. The captured history before that cursor still contains earlier-session entries (including stale compile entries and the first rejected outside-project screenshot path); they are not new runtime failures.

## Real uGUI Button flow

Every row below used the live scene GameObject's `UnityEngine.UI.Button.onClick.Invoke()` listener path. The submitted action was selected from the current RuntimeAdapter snapshot; no UI legality or target was manufactured.

| Flow | Button / advertised action | Accepted | Snapshot revision | Resulting state | Evidence |
|---|---|---:|---:|---|---|
| Production ambush | `Action_skip_ambush_0` / `SKIP_AMBUSH#skip_ambush_0` | yes | `1→2` | `AMBUSH→ACTION` | `runtime-bootstrap-screen-after-skip.png`; `live-1440-production-after-skip.png` |
| Production play | `ActionGroup_PLAY_CARD_0_flame_bolt/Submit` / selected `PLAY_CARD#play_22_core:shared_castle` (`flame_bolt`) | yes | `2→3` | `ACTION`, accepted result rendered | `live-1280-actions-before-play.png`; `live-1280-after-play-card.png` |
| Fixture play carrier | `Action_play_1` / `PLAY_CARD#play_1`, `u03_fixture_machine_carrier` | yes | `2→3` | fixture field updated | `live-1280-after-play-carrier.png` |
| Fixture play commit | `Action_play_2` / `PLAY_CARD#play_2`, `u03_fixture_machine_commit` | yes | `3→4` | on-play `COMMIT`; `ACTION` | `live-1280-after-play-commit.png` |
| Fixture attack | `ActionGroup_ATTACK_0_/Submit` / selected `ATTACK#attack_3_entity_000000000004`, source `3`, target `entity_000000000004` | yes | `4→5` | `ACTION`, one legal action remains | `live-1280-before-attack.png`; `live-1280-after-attack.png` |
| Fixture end turn | `Action_end_turn_0` / `END_TURN#end_turn_0` | yes | `5→6` | `ACTION→DISCARD`, turn `1` | `live-1280-after-end-turn-fixture.png` |
| Fixture push route | `Action_discard_0_0`, then opponent `SKIP_AMBUSH#skip_ambush_1`, `END_TURN#end_turn_1`, `DISCARD#discard_1_0` | yes | `6→7→8→9→10` | fixture `Cloud=1`, then current player `0` | `live-1280-after-push-cloud.png` |
| Fixture PULL setup | `Action_skip_ambush_0` / `SKIP_AMBUSH#skip_ambush_0` | yes | `10→11` | `PULL` advertised from cloud top | `live-1280-before-pull.png` |
| Fixture PULL | `ActionGroup_PULL_0_u03_fixture_machine_commit/Submit` / `PULL#pull_3_2`, card `u03_fixture_machine_commit`, source `3`, target `2` | yes | `11→12` | `Cloud=0`, `Graveyard=1`, `PullCount=1`; accepted result rendered | `live-1280-after-pull.png` |

The fixture was injected only in memory by configuring a temporary `RuntimeBootstrap` with `Assets/DominionWars.Tests.EditMode/Fixtures/PullLifecycleData`; the scene was not saved. It is explicitly `NON_AUTHORITATIVE` and does not replace the production 91-card runtime data.

## Resolution evidence

- `live-1440-production-initial.png`: production RuntimeBootstrap, `1440×900`, initial `AMBUSH` screen.
- `live-1440-production-after-skip.png`: production RuntimeBootstrap, `1440×900`, post-button `ACTION` screen.
- `live-1280-actions-before-play.png`: production RuntimeBootstrap, `1280×720`, real PLAY_CARD group/source-target selection.
- `live-1280-after-pull.png`: fixture route, `1280×720`, accepted PULL result and post-PULL counters.

All live captures were copied to this evidence directory. The exact `live-*` PNG and `.meta` files created under `Assets/Temp/pipeline-screenshots` during this pass were removed; unrelated Temp content was preserved.
