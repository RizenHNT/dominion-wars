# Engine Adapter Contract v1.30

Canonical packages contain no browser/Unity/Godot API. An engine port implements only these adapters:

1. **SnapshotAdapter**: engine state -> `game_snapshot.schema.json`.
2. **ActionAdapter**: canonical `GameAction` -> native rule-engine command.
3. **EventAdapter**: native rule events -> `ui_event.schema.json` preserving `eventId/parentEventId`.
4. **LayoutAdapter**: normalized zones/anchors -> native UI layout.
5. **MotionAdapter**: named motion primitives -> native animation/tween system.
6. **AssetAdapter**: logical asset ID -> SVG/PNG/native imported resource.
7. **LocalizationAdapter**: localization key -> current language string.
8. **AccessibilityAdapter**: focus order, labels, reduced-motion, input-device support.

Porting to another engine must not require edits to card data, phase rules, visual asset IDs, motion names, screen IDs, or localization keys. If it does, the adapter boundary has leaked and the port fails the portability gate.
