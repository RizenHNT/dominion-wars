# Codex implementation report — WBS 10.11 (2026-08-23)

## Outcome

Implemented the approved, independently executable portions of WBS 10.11: leader entry by card type (10.11.1), manual Pull engine flow (10.11.2), castle-break resolution (10.11.3), Wood growth actions and leader data (10.11.4), removal of stale Sea `潮位` wording (10.11.5), and the approved data-repair subset in 10.11.7. No Java removal, release, push, or unrelated worktree cleanup was performed.

## Implementation summary

### 10.11.1 — Leader entry by card type

- A leader leaving its source zone enters its type-specific destination: MINION to Field, AMBUSH to AmbushZone, and other leaders to LeaderZone.
- An ordinary non-punishment draw resolves enter effects; a punishment draw resolves enter effects followed by punishment effects. Neither route creates a hand detour or duplicate entity/event/effect execution.
- `ForceLeaderOut` uses the same leader-entry path.
- Active-leader lookup, entity lookup/ID allocation, targeting/life fallbacks, and snapshot projection now account for all three leader zones and fail closed if the state contains multiple active leaders.

### 10.11.2 — Manual Pull

- Added legal `PULL` actions using a valid download carrier as source and the current player's cloud-stack top as target.
- Added a fail-closed handler for stale/forged action IDs, invalid sources, non-top targets, unsupported effects, and unavailable payment paths.
- Zero-cost Pull is executable. Positive download cost is deliberately not advertised and returns `action.cost_system_unavailable`; no existing punishment or ordinary-cost resource was repurposed before the payment source is specified.
- Successful Pull follows the shared engine path: remove cloud-stack top, resolve Pull effects, move the card to graveyard, then increment `PullCount`.
- Added `PULL_DECLARED` and `CARD_PULLED` to the v1.31 event boundary and projections, using schema-valid fields and numeric entity IDs.

### 10.11.3 — Castle break

- Castle break resolves once on the first transition from positive health to zero.
- Under the current PL implementation interpretation, the breaker receives `CycleWinCount = max(existing, 9)` without reducing a larger existing value. The final ownership/meaning of this benefit remains a separately tracked owner decision.
- The opposing leader is forced through the same direct-entry path.
- If both active leaders are minions, the breaker wins immediately with `win.castle_break_minion`.
- `ROYAL_CASTLE_BREAK` is evaluated passively against both active leaders, regardless of which player broke the castle; hidden leaders do not trigger it.
- Repeated damage after the break does not duplicate leader entry, events, or counters.

### 10.11.4–10.11.5 — Wood and Sea

- Added `ADD_ROOT` and `ADD_RAMPANT` across schemas, loaders, engine effects, dispatch, runtime mapping, and tests. Rampant is capped at 3 and invalid inputs fail closed.
- Updated `wood_leader` to `GIANT_HEALTH_GE` with `winParam: 512`; enter effects are `SUMMON 2` then `ADD_RAMPANT 1`, and punishment effects are `PROTECT_TURN` then `ADD_RAMPANT 2`.
- Removed the ordinary-card Rampant tags identified in canonical Wood data while retaining Root behavior.
- Removed residual tide wording from the Sea leader design bundle. No unfrozen tide action was introduced.

### 10.11.7 — Approved data-repair subset completed in this batch

- All AMBUSH entries in the design bundle now have `punishCost: 0` (54 checked; 23 changed from non-zero).
- Changed 54 non-mechanical BUFF effects on 47 cards from `SELF` to `FRIENDLY_MINION`; mechanical BUFF and `GRANT_KEYWORD SELF` identities remain unchanged.
- `CardCatalog` now preserves explicit `punishCost` and `punishActivatable` values while retaining the legacy fallback when those fields are omitted.

## Validation

- .NET Release regression after the follow-up coverage additions: **461/461 passed, 0 failed, 0 skipped**.
- Targeted Wood/effects/data/turn tests: **171/171 passed**.
- Castle tests: **5/5 passed**.
- Three-zone leader-draw tests: **2/2 passed**.
- Runtime-contract schemas: **11 valid fixtures / 6 invalid fixtures / 0 failures**, across 4 schemas.
- Canonical card data: **91/91 passed**.
- Deck validation: **4/4 passed**, covering 91 cards.
- Design manifest: **320/320 passed**.
- Java build and historical Java regression: **38/38 passed**.
- `git diff --check`: **passed**.
- Python alignment was not requested and was skipped.
- NuGet emitted an offline `NU1900` vulnerability-feed warning; it did not fail build or tests.

### Follow-up QA coverage

- Added direct assertions that an AMBUSH leader enters AmbushZone and is not advertised as an ordinary attack target.
- Added direct assertions that Destroy, Control, and lethal damage do not defeat or move a non-minion leader without an applicable durability/life rule.
- Added direct assertions that an empty cloud stack advertises no PULL action.
- Added direct assertions for `ForceLeaderOut` choosing Hand before Graveyard and falling back to Graveyard when needed.

## Remaining decisions and blocked verification

### HUMAN_REQUIRED

1. The design bundle still contains **122 non-AMBUSH entries with `punishCost > 0` but no punishment effects**: Wood 32, Sea 35, Flame 38, Neutral 17; Minion 77, Spell 45. Clearing those costs versus authoring effects changes design/balance and was not guessed.
2. WBS 10.11.6 still needs the explicit migration decision from bundle `winAmount` to canonical `winParam`; the loader/schema currently accepts both for compatibility.
3. Defense-backfill card counts, values, and castle heal/buff semantics require a design decision before production-data edits.

### ENVIRONMENT_BLOCKED / MANUAL VERIFICATION

- `scripts/run-unity-runtime-validation.ps1` was attempted after confirming that the existing zero-byte Unity lock was stale and moving it to a recoverable backup under `build-output/unity-runtime-validation/stale-lock-backups/`.
- Unity exited before running EditMode tests with code **198**. The log reports that the `com.unity.editor.headless` entitlement was not found and no valid headless Editor license was available. Evidence: `build-output/unity-runtime-validation/20260823-131718-feb681c0/editmode.log`.
- EditMode, PlayMode, and Windows-player validation therefore remain not run for this batch and require the licensed GUI/manual path. The previous 2026-08-21 Unity evidence is useful history but is not claimed as validation of these new changes.

## Repository state

- HEAD observed at closeout: `00232c4` (`PL archive 2026-08-23 12:54`), preserved after it appeared during parallel work.
- No commit or push was made by this implementation batch.
- Existing unrelated dirty files were neither reset nor cleaned.
- The stale zero-byte `UnityLockfile` was moved to a recoverable `build-output` backup after its recorded PID was absent and exclusive-open verification succeeded; it was not deleted.

## Viewer-scoped Unity runtime follow-up (2026-08-23)

- Re-checked the delegated viewer slice: `RuntimeAdapter.RefreshSnapshot` requests the selected viewer from the session; viewer changes clear presentation event history; `RuntimeBattlePanel` binds and verifies `viewerPlayerIndex` before rendering; submitted actions reproject the selected viewer after the authoritative result.
- Fixed one stale-state edge: accepting a direct snapshot now clears the previous operation's `EventDelta` without clearing same-viewer event history. Added `DirectSnapshotRefreshClearsPreviousEventDeltaButKeepsHistory` to `RuntimeAdapterEditModeTests`.
- Unity Editor evidence: automatic script compilation completed successfully after the change (`DominionWars.Runtime`, `DominionWars.UI`, and `DominionWars.Unity.EditMode` assemblies; `Tundra build success`; `LogAssemblyErrors (0ms)`). No `.unity`/`.prefab` YAML was edited.
- .NET Release regression after the change: **457/457 passed, 0 failed, 0 skipped**. Unity EditMode/PlayMode/Windows runtime tests were not run: no `unity` CLI or connected Pipeline command was available; the running Editor only supplied compile/log evidence. No commit or push.

## Viewer QA hardening follow-up (2026-08-23)

- `RuntimeAdapter.ApplyEvents` now materializes and validates the entire batch against a temporary cursor before publishing either cursor history or presentation state. A mid-batch gap therefore leaves both unchanged and permits a safe retry; transport events remain rejected until a first snapshot is accepted.
- `RuntimeAdapter.Submit` now fail-closes on mismatched result/snapshot contract, match ID, input revision, resulting revision, or action ID. Event projection runs before publication, so malformed session replies cannot partially update `RuntimePresentationState`.
- `RuntimeBattlePanel.Bind(RuntimeBootstrap)` now replaces the previous adapter, detaches it while the new bootstrap is not ready, and binds once the new adapter appears. Direct EditMode coverage also exercises this rebind path and viewer snapshot boundary.
- Unity Editor automatic compilation after these changes imported the adapter/panel test assemblies successfully: `Tundra build success`, `LogAssemblyErrors (0ms)`, with no reported CS errors/warnings. No `.unity`/`.prefab` YAML was edited; Unity Test Runner remains unrun because no CLI/connected Pipeline is available.
- Exact current command `dotnet test DominionWars.sln --nologo --no-restore -c Release`: **461/461 passed, 0 failed, 0 skipped**; `--no-build --list-tests` also enumerates 461. The earlier 457 count was stale relative to the current checkout/assembly and is not used as current evidence. `git diff --check` passed. No commit/push.

## Viewer identity and event-boundary audit (2026-08-23)

- `RefreshSnapshot` now rejects a session snapshot whose `ViewerPlayerId` is not exactly `player_{viewerPlayerIndex}` before `AcceptSnapshot`; the stable error is `Snapshot viewer identity does not match the requested player.` The wrong-viewer test verifies Snapshot, Events, and EventDelta remain unchanged.
- `AcceptSnapshot` now treats a direct same-match viewer change as a presentation boundary and clears both event history and delta. Same-viewer refreshes still clear only the transient EventDelta; direct viewer-change coverage was added.
- The three event inputs were audited: `Submit` and `AcceptEventDelta` receive raw engine `GameEvent` values and project them once; `ApplyEvents` receives already projected `RuntimeEventEnvelope` values and is the only cursor-validated path. They are intentionally not forced through one cursor: engine streams contain unmapped internal event IDs and parent chains that the UI projection filters/remaps, while `RuntimeEventCursor` requires contiguous wire IDs. Sharing it now could reject legal projected sequences or change event-parent semantics; a future contract-level transport cursor is required before unification.
- Unity Editor log evidence available before the final identity patch remains `Tundra build success`/`LogAssemblyErrors (0ms)` with the adapter tests imported; the active Editor lock did not produce a newer compile record and no connected CLI is available, so the final patch is not claimed as Unity-compiled. Exact .NET command remains **461/461 passed, 0 failed, 0 skipped**; `git diff --check` passed. No commit/push.

## Target-complete PLAY_CARD advertising follow-up (2026-08-24)

- Fixed a c-layer contract defect: `LegalActionGenerator` advertised targeted cards such as real-data `play_22` / `flame_bolt` with no target, while the authoritative `PlayCardActionHandler` rejected that exact submission as `action.target_required`. The UI is contractually forbidden to parse card text or manufacture legality, so this belongs to Engine/Adapter legal-action generation rather than presentation code.
- Targeted plays now reuse `CardTargetValidator` and its configured `TargetPolicy` to publish one complete, uniquely identified `LegalAction` per target the handler will actually accept. Entity targets remain numeric on the 1.31 wire; named life/castle targets retain the existing union form. Untargeted area effects such as `flame_rain` retain one null-target action, targetless fizzles remain backward-compatible, and the handler still accepts the legacy base action ID for direct callers.
- Direct .NET coverage proves `flame_bolt` advertises `player_1`, the exact advertised action is accepted and deals the existing 3 damage, while a forged `castle` target is rejected as `action.advertisement_mismatch` without preceding mutation. Generator coverage also proves `flame_rain` remains targetless.
- Connected Unity Pipeline evidence (Editor PID 31024): forced recompile returned `failed=false`, `errors=[]`; the new UI complete-variant selection test passed **1/1**; the real data-backed RuntimeBootstrap smoke passed **1/1** and now explicitly proves source entity 22 `flame_bolt` carries a non-null engine-advertised target and is accepted.
- Final `dotnet test DominionWars.sln --no-restore`: **463/463 passed, 0 failed, 0 skipped**; `git diff --check` passed. Full current EditMode discovery is **44/52 passed, 8 failed**; all eight failures are pre-existing/shared-dirty-tree UI button/legacy NUnit assertion/Pull-fixture issues outside this narrow c fix, while both tests relevant to this repair are green. No commit/push and no scene/prefab YAML edit.

## Revision-scoped action idempotency follow-up (2026-08-24)

- Fixed a b/c-layer match-stall defect: the gateway cached submissions by `actionId` alone, while the engine intentionally republishes stable IDs such as `skip_ambush_0` on later turns. The real U-03 PULL lifecycle therefore stopped at the second player-0 AMBUSH with `action.duplicate`, before PULL could become reachable.
- The in-memory idempotency key is now `(match, snapshotRevision, actionId)` (the gateway instance already owns one match), matching the existing result-cache key. An exact same-revision request returns the cached submission without engine side effects; a same-key request whose type/actor/source/target/card/payload differs is rejected as `action.duplicate`; the same stable action ID at a later authoritative revision is evaluated normally. The cached request is copied before storage so caller mutation cannot turn a modified replay into an apparent exact replay.
- Added direct gateway regressions for modified same-revision replay and later-revision reuse of `skip_ambush_0`. Targeted gateway suite: **12/12 passed**. Exact full command `dotnet test DominionWars.sln --nologo --no-restore -c Release`: **465/465 passed, 0 failed, 0 skipped**.
- Connected Unity Pipeline evidence: final `recompile_status` returned `status=completed`, `failed=false`, `errors=[]`; the real non-authoritative U-03 fixture `PLAY_CARD → COMMIT → END_TURN → PUSH → opponent turn → repeated skip_ambush_0 → PULL → Graveyard` passed **1/1**. Full EditMode passed **52/52**. PlayMode was started asynchronously as required by the Pipeline domain reload and passed **2/2**. `git diff --check` passed.
- This is a lower-layer hard-freeze exception because the authoritative Gateway rejected an action it had advertised in a later revision, causing inconsistent match progress and making the approved U-03 UI path unreachable. No rule/data/schema/Java/UI/scene/prefab change, commit, push, or cleanup was performed.
