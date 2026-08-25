# Unity U00-U03 Final QA — 2026-08-24

## Conclusion

**U00-U03 PASS，功能占位原型非最终美术。**

This gate covers the Unity uGUI tabletop placeholder, its production PLAY/DISCARD path, the explicitly non-authoritative ATTACK/PULL fixture path, and the clean Console boundary. It does not approve final card art, final typography, animation, audio, localization polish, or release readiness.

## Scope and evidence

- Runtime report: [`tabletop-v2/UNITY_LIVE_FLOW_REPORT_2026-08-24.md`](evidence/unity-u00-u03-2026-08-24/tabletop-v2/UNITY_LIVE_FLOW_REPORT_2026-08-24.md)
- Screenshot directory: [`tabletop-v2`](evidence/unity-u00-u03-2026-08-24/tabletop-v2/)
- Visual review set: all 28 retained PNG files, comprising 14 interaction states at both 1280x720 and 1440x900.
- Reference layout: opponent hand/status/field above, own field/status/hand below, shared castle in the centre, mirrored side resources, and the primary action area at bottom-right.
- Production UI sources reviewed:
  - [`RuntimeBattlePanelView.cs`](../unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeBattlePanelView.cs)
  - [`RuntimeBattlePanel.cs`](../unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeBattlePanel.cs)
  - [`RuntimeBattleCardDrag.cs`](../unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeBattleCardDrag.cs)
  - [`RuntimeBattlePanelActionModel.cs`](../unity/DominionWars.Unity/Assets/DominionWars.UI/RuntimeBattlePanelActionModel.cs)
- Relevant EditMode coverage reviewed:
  - [`RuntimeBattleBoardEditModeTests.cs`](../unity/DominionWars.Unity/Assets/DominionWars.Tests.EditMode/RuntimeBattleBoardEditModeTests.cs)
  - [`RuntimeBattleCardDragEditModeTests.cs`](../unity/DominionWars.Unity/Assets/DominionWars.Tests.EditMode/RuntimeBattleCardDragEditModeTests.cs)
  - [`RuntimeBootstrapActionFlowEditModeTests.cs`](../unity/DominionWars.Unity/Assets/DominionWars.Tests.EditMode/RuntimeBootstrapActionFlowEditModeTests.cs)

## Gate results

| Gate | Result | Reproducible evidence |
|---|---|---|
| U00 — live uGUI tabletop and state presentation | PASS | `production-initial-{1280x720,1440x900}.png` shows a live tabletop with opponent/own zones, card backs, face-up cards, leader slots, piles, phase, shared castle, events, and actions. The runtime report records Unity 6000.3.21f1, `RuntimeBootstrap.unity`, and the live `RuntimeBattlePanel` binding. |
| U01 — mirrored tabletop composition and responsive bounds | PASS | All 28 PNGs were inspected at both retained resolutions. Opponent resources occupy the upper-left rail, own resources the lower-right rail, the mechanical queue/cloud areas mirror across the opposite corners, and the main action rail remains bottom-right. No normal-state card or panel crosses its containing tabletop bounds. `production-illegal-drag-*` is the deliberate drag-out frame; `production-illegal-after-*` shows restoration. `RuntimeBattlePanelView` uses explicit mirrored anchors, `ScaleWithScreenSize` at 1280x720, and `RuntimeResponsiveCardLayout`; `RuntimeBattleBoardEditModeTests.TabletopCardsAndPilesStayInsideTheirResponsiveBounds` covers 1280x720 and 1440x900. |
| U02 — production PLAY/DISCARD interaction flow | PASS | `production-play-before-*`, `production-play-drag-highlight-*`, and `production-play-accepted-*` show hand drag, legal castle highlight, accepted PLAY_CARD, and no retained drag ghost. The report records revision 2 -> 3 and castle life 75 -> 72. `production-discard-before-*` and `production-discard-accepted-*` correspond to the advertised `requiredCount=6` action; the report records revision 9 -> 10, hand 14 -> 7, grave 0 -> 7, and continuation to player 0 AMBUSH. |
| U03 — ATTACK/PULL fixture, action authority, and clean boundary | PASS | The report and `fixture-*` filenames explicitly classify ATTACK/PULL as `NON_AUTHORITATIVE`; they are not production-data claims. `fixture-attack-drag-highlight-*` and `fixture-attack-accepted-*` show castle targeting, accepted ATTACK, and life 75 -> 74. `fixture-pull-drag-highlight-*` and `fixture-pull-accepted-*` show the Cloud/queue target, accepted PULL, Cloud 1 -> 0, grave 0 -> 1, and PullCount 0 -> 1. After fixture removal, the report records one production bootstrap, zero fixture objects, warnings 0, errors 0, and `dropped=false`. |

## Required interaction checks

| Check | Result | Evidence |
|---|---|---|
| Card entities rather than text-only controls | PASS | Card faces have a frame, accent stripe, cost badge, title, entity label, stats row, and source/target marker in `RuntimeBattlePanelView.CreateCardFace`; opponent cards use a distinct card-back component. The retained screenshots show these card silhouettes in hand and field zones. |
| Own-hand drag to play | PASS | `production-play-drag-highlight-*` -> `production-play-accepted-*`. |
| Field-card drag to attack | PASS, fixture only | `fixture-attack-drag-highlight-*` -> `fixture-attack-accepted-*`. |
| PULL interaction | PASS, fixture only | `fixture-pull-drag-highlight-*` -> `fixture-pull-accepted-*`. |
| Legal target highlight | PASS | Castle and mechanical rail receive a bright cyan outline only while the dragged card carries an advertised action for that exact target. `RuntimeBattleDropZone.NotifyDragStarted` checks `RuntimeBattleCardDrag.CanAccept`. |
| Invalid drop return | PASS | `production-illegal-drag-*` -> `production-illegal-after-*`; revision remains 2 and no submission occurs. `RuntimeBattleCardDrag.OnEndDrag` restores original parent, sibling index, anchored position, scale, and raycast state when the drop is not accepted. |
| Successful drop has no ghost | PASS | No floating source card remains in PLAY, ATTACK, or PULL accepted screenshots. `RuntimeBattleCardDrag.SubmitAndRetire` immediately sets alpha to zero, disables the source object, submits, and destroys the obsolete drag object; `SuccessfulDropImmediatelyHidesGhostAndLeavesAuthoritativeRebuildVisible` covers the lifecycle. |
| Opponent-hand redaction | PASS | All production screenshots show uniform opponent card backs only. `RuntimeBattlePanel.RenderTable` creates backs from `HandCount` and does not render opponent hand identities. |
| LegalAction remains authoritative | PASS | `RuntimeBattlePanel` derives source actions and drop zones from `snapshot.LegalActions`. `RuntimeBattleCardDrag` submits only an interactable advertised action whose wire target exactly matches the drop zone. `RuntimeBattlePanel.SubmitAdvertisedAction` converts the unchanged legal action, validates it through `RuntimeActionBoundary`, and then submits through `RuntimeAdapter`. |

## Severity disposition

### P0

None open for the U00-U03 functional-placeholder gate.

The previous blocking issues are closed: mirrored layout, bottom-right primary action, both target resolutions, visible card bounds, accepted-drop ghost removal, invalid-drop restoration, production PLAY, production multi-card DISCARD continuation, fixture ATTACK/PULL evidence labelling, and the fresh 0-warning/0-error Console boundary.

### P1

- Future fixture screenshots should place a persistent `NON_AUTHORITATIVE FIXTURE` watermark inside the captured game frame. The current evidence is correctly labelled by report section and `fixture-*` filenames, but the classification is not embedded in the pixels.
- Before ATTACK is accepted, the bottom-right rail may display the simultaneously available PULL action. A direct `ATTACK -> target` drag hint would make the current gesture self-explanatory without relying on the evidence filename or accepted-status banner.
- Card names, entity labels, and placeholder stats are small in a full-frame screenshot, especially at 1280x720. The subsequent art/UI pass should strengthen card information hierarchy without changing engine authority.

### P2

- Replace primitive fills and outlines with approved card art, table texture, icons, typography, animation, audio, and final interaction feedback.
- Resolve mixed Chinese/English placeholder copy through the approved localization and frontend-copy process.

## Final gate statement

**U00-U03 PASS，功能占位原型非最终美术。**

This result approves the tested Unity tabletop structure and interaction slice only. ATTACK and PULL remain explicitly fixture-backed evidence until production data advertises those actions. Final visual direction and release approval remain human-owner gates.
