# C#/Java alignment report

Generated: 2026-08-13 12:42:16 +09:00

This is a static inventory, not a parity approval. Differences requiring rule, target, or contract decisions remain HUMAN_REQUIRED/PL-owned.

## Effects

| C# actions | Java action cases | C# only | Java only |
|---:|---:|---|---|
| 24 | 24 | none | none |

C# source: `src/Engine/Effects/EffectNames.cs`; Java source: `src/main/java/com/dominionwars/engine/Effects.java` (`switch (e.action)`).

## Targets

| C# resolver cases | Java resolver cases | C# only | Java only |
|---:|---:|---|---|
| 10 | 7 | ENEMY_SINGLE, ENEMY_TARGET, SINGLE_ENEMY | none |

Target inventory is syntax-level only. Core targets and pending-choice semantics still require the approved target-union design; this report does not authorize silently selecting a target.

## Card fields

| C# CardDefinition fields | Java CardDef fields | C# only | Java only |
|---:|---:|---|---|
| 20 | 38 | ArtId, Cost, HasLeaderAbility, IsLeader, IsMinion, Rarity | action, ambushEffects, ambushTrigger, amount, attacksPerTurn, chant, chantEffects, durability, enterEffects, guard, leader, leaderDef, onOpponentDiscardEffects, onPlayEffects, param, persistentEffects, punish, punishCondition, punishEffects, target, type, winCondition, winParam, winText |

Known non-equivalence requiring follow-up: Java retains ambush/chant/discard-hook and full LeaderDef fields; the current C# CardDefinition intentionally exposes only the approved loader/engine subset. Do not treat the inventory as permission to expand the model.

## Follow-up queue

- PL/HUMAN_REQUIRED: approve target union and core-target behavior before changing `ENEMY_TARGET`/`ENEMY_FACE` semantics.
- PL/HUMAN_REQUIRED: decide whether Java-only persistent/ambush/chant fields enter the next C# batch or remain Java-authoritative.
- Codex/DeepSeek: add focused parity tests only after those decisions; Unity runtime validation remains separately blocked by Hub/licensing.
