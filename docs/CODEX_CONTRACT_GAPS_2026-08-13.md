# Contract gap report

Generated: 2026-08-13 13:11:51 +09:00

This report is mechanical inventory only. It does not approve rule, contract, target, or event semantics.

## Actions

| Check | Result |
|---|---|
| Card EffectAction missing in C# | none |
| Card EffectAction missing in Java | none |
| Java action cases not in C# EffectNames | none |
| UI action types absent from C# LegalActionGenerator | SET_AMBUSH, SKIP_AMBUSH, CHOOSE_TARGET, DISCARD |
| C# legal action types absent from UI schema | ACTIVATE_PUNISH, USE_LEADER_ABILITY |
| Persistent actions (separate schema family) | DISABLE_ENEMY_LEADER |

## Targets

| Check | Result |
|---|---|
| Card schema targets | ENEMY_TARGET, ENEMY_MINION, FRIENDLY_MINION, ALL_ENEMY_MINIONS, ALL_FRIENDLY_MINIONS, ALL_MINIONS, ENEMY_FACE, SELF, ANY_MINION, ENEMY_SINGLE, SINGLE_ENEMY |
| C# resolver-only targets | none |
| Java resolver-only targets | none |

## Events and phases

| Check | Result |
|---|---|
| Internal emitted event types not mapped by Adapter | ATTACKS_RESTORED, BUFF_APPLIED, CARDS_DRAWN, DEFEAT_PREVENTED, EFFECT_NEGATED, EFFECT_SKIPPED, EFFECTS_NEGATED_TURN, KEYWORD_GRANTED, MINION_DESTROYED, MINION_SUMMONED, PROTECTION_APPLIED, PUNISH_DELTA_APPLIED, PUNISH_FLIP_APPLIED, RESHUFFLE_CREDIT_GRANTED, TURN_FORCE_ENDED |
| UI schema event types absent from Adapter map | PHASE_CHANGED, TURN_CHANGED, AMBUSH_SET, TARGET_REJECTED, PUNISH_ISSUED, PUNISH_DRAW, CHAIN_LINK, CHAIN_RESOLVED, LEADER_DISABLED, VICTORY_PROGRESS |
| State-machine phases absent from Adapter validation | none |

## Required decisions

- HUMAN_REQUIRED: choose the canonical action set and whether punish/leader actions are transport actions or internal commands.
- HUMAN_REQUIRED: approve a target union for minions, leaders, castle and life cores; do not expose aliases by guesswork.
- HUMAN_REQUIRED: approve event naming/mapping and root-event policy before filling the unmapped event list.
- BLOCKED: Unity package resolution, compilation, EditMode and Windows build remain runtime gates outside this report.
