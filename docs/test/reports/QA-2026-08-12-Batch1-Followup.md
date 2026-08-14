# QA Report — C# Engine Batch 1 Follow-up

**Date**: 2026-08-12 08:15 JST
**Tester**: DeepSeek QA
**Scope**: Uncommitted changes following PL-approved Batch 1 follow-up (DAILY_GOAL.md 2026-08-12)
**Environment**: Windows NT, Java 23.0.2, Python 3.x, no .NET SDK (only runtimes 3.1.8/6.0.36)

---

## 1. Changes Under Review

| File | Change Type | Summary |
|---|---|---|
| `data/schema/cards.schema.json` | Modified | Added 5 new $defs (PersistentEffectSpec, TriggeredEffectSpec, PunishCondition, AmbushKind, AmbushTrigger), 4 new EffectTarget aliases, LeaderDef extensions, CardDef extensions |
| `docs/effects.contract.md` | Modified | Added §4.1 Persistent aura, compat target aliases, DISABLE_ENEMY_LEADER clarification |
| `docs/SPEC.md` | Modified | Updated 21→24 action count, added Batch 1 QA follow-up records |
| `docs/DAILY_GOAL.md` | Modified | Status DRAFT→READY, scoped to Batch 1 follow-up |
| `docs/AI_MAILBOX.md` | Modified | Multiple entries: QA closeout, Codex config, relay manual, agent config fix |
| `src/Engine/Tests/ContractBoundaryTests.cs` | Modified | Added PersistentAuraIsNotAnOrdinaryDispatcherAction test |
| `src/Engine/Tests/EffectRuntimeTests.cs` | Modified | Added SELF, ANY_MINION, ENEMY_SINGLE/SINGLE_ENEMY target tests |
| `src/Engine/Tests/EventLogTests.cs` | New | 3 tests: root/child monotonic IDs, parent existence, root query rejection |
| `.github/agents/deepseek.agent.md` | Modified | Added `agent` tool, `agents: ["MiniMax PL"]` |
| `.github/agents/codex.agent.md` | New (untracked) | Codex agent config with role definition and boundary rules |

---

## 2. Test Execution Results

### 2.1 Java Regression (baseline)
```powershell
java -cp "build/classes;build/test-classes" com.dominionwars.test.TestMain
```
**Result**: 35/35 ✅ PASS

### 2.2 Java Simulation
```powershell
java -cp "build/classes;build/test-classes" com.dominionwars.test.SimMain 100
```
**Result**: 1200 rounds across 4 decks. All decks produced valid win rates. ✅ PASS

### 2.3 Python Data Integrity (sanity_check_v2)
```powershell
$env:PYTHONIOENCODING='utf-8'; python scripts/sanity_check_v2.py
```
**Result**: 0 ERROR, 0 WARN, 0 INFO. 91 cards, 4 decks, 4 art assets. ✅ PASS

### 2.4 Python Alignment Check
```powershell
$env:PYTHONIOENCODING='utf-8'; python scripts/align_check.py
```
**Result**: No dangling SUMMON references. All actions/leaders consistent. ✅ PASS

### 2.5 .NET Engine Tests (C#)
```powershell
dotnet test src/Engine/Tests/DominionWars.Engine.Tests.csproj
```
**Result**: 🔴 BLOCKED — .NET 8 SDK not installed. Only runtimes 3.1.8 and 6.0.36 available.

### 2.6 JSON Schema Validation
```powershell
python -c "from jsonschema import validate; ..."
```
**Result**: 🔴 5/5 card files fail validation — 5 token minions missing `text` field.

---

## 3. Findings

### 🔴 P1 — 5 token cards fail schema validation (text field)

**Files**: `data/cards/flame.json` (flame_drake), `machine.json` (machine_golem), `neutral.json` (neutral_mercenary), `sea.json` (sea_leviathan_young), `wood.json` (wood_sapling)

**Description**: These 5 cards are summoned token minions (SUMMON targets). They lack the `text` field now required by the schema (`Card.required: ["id", "name", "faction", "type", "text"]`). This is the "five reported gaps" referenced in DAILY_GOAL.md.

**This is fail-closed behavior** — the schema correctly rejects cards with missing required fields. However, the acceptance criteria also call for "a precise fail-closed migration decision." Currently no migration decision is documented.

**Options**:
- (A) Add `"text": ""` to all 5 token cards (simplest, schema already allows `minLength: 0`)
- (B) Make `text` optional for MINION type cards
- (C) Add an `isToken` flag and conditional text requirement

**Recommendation**: Option A — add empty text strings. These tokens already have names, types, and stats; an empty text field is appropriate for non-collectible summons.

---

### 🟡 P2 — .NET 8 SDK unavailable

Cannot verify the 3 new/updated C# test files (ContractBoundaryTests, EffectRuntimeTests, EventLogTests). Code review suggests they are correct:

- `PersistentAuraIsNotAnOrdinaryDispatcherAction`: Validates that DISABLE_ENEMY_LEADER is NOT registered as a dispatcher action but IS present in schema PersistentEffectAction enum. ✅ logically sound
- `SelfTargetResolvesTheSourceMinion`: Applies Buff to SELF and verifies source minion stats. ✅
- `AnyMinionCanResolveAnExplicitFriendlySelection`: Applies Buff to ANY_MINION with explicit target ID. ✅
- `EnemySingleAliasesResolveLikeEnemyMinion`: [TestCase] verifies ENEMY_SINGLE and SINGLE_ENEMY resolve same as ENEMY_MINION. ✅
- EventLogTests: 3 tests covering root/child monotonic IDs, parent existence validation, and root query edge cases. ✅ logically sound

---

### 🟢 Verified — Contract-Schema Alignment

| Check | Status |
|---|---|
| EffectAction: 24 actions aligned | ✅ |
| EffectTarget: 11 targets (7 base + 4 compat) documented | ✅ |
| DISABLE_ENEMY_LEADER not in EffectAction enum | ✅ |
| PersistentEffectAction separate enum | ✅ |
| TriggeredEffectSpec wraps EffectSpec OR PersistentEffectSpec | ✅ |
| PunishCondition enum (4 values) | ✅ |
| AmbushKind enum (3 values) | ✅ |
| AmbushTrigger enum (5 values) | ✅ |
| LeaderDef: winParam, durability, persistentEffects added | ✅ |
| CardDef: punishActivatable, punishCost, punishCondition, ambush*, chant*, attacksPerTurn, onOpponentDiscardEffects added | ✅ |
| effects.contract.md §4.1 persistent aura aligned with schema | ✅ |

---

### 🟢 Verified — Agent Config Changes

| Change | Status |
|---|---|
| deepseek.agent.md: added `agent` tool + `agents: ["MiniMax PL"]` | ✅ Enables cross-agent relay |
| codex.agent.md (new): role definition, boundary rules, communication protocol | ✅ Reference config (not runtime-active since Codex is CLI tool) |
| MiniMax PL agents list: `["DeepSeek QA"]` (Codex removed) | ✅ Prevents dropdown crash from missing Codex model |

---

## 4. SPEC §8 Acceptance Criteria Re-check

| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1 | Pure .NET tests | 🟡 | Tests exist but cannot run — no .NET 8 SDK |
| 2 | Unity EditMode | ⏳ | Not in scope |
| 3 | Deterministic seed | ✅ | Regressed in prior QA |
| 4 | Snapshot→Action→Command→Event | ✅ | Regressed in prior QA |
| 5 | Pioneer pressure | 🟡 | Documented as blocked in SPEC.md |
| 6 | ID validation | ✅ | Regressed in prior QA |
| 7 | Expired action/revision rejection | ✅ | Regressed in prior QA |
| 8 | Root marker + monotonic eventId | 🟢 | **NEW**: EventLogTests.cs provides explicit coverage |
| 9 | contractVersion rejection | ✅ | Regressed in prior QA |
| 10 | JSON+version validation | ⏳ | Not in scope |
| 11 | Java comparison | ⏳ | Not in scope |

---

## 5. Summary

| Category | Count |
|---|---|
| Total checks run | 6 |
| Passed | 4 |
| Blocked (environment) | 1 |
| Failed (data gap) | 1 |

**Verdict**: 🟢 CONDITIONAL PASS — The uncommitted changes are technically sound and consistent. The 5-card `text` field gap is a known pre-existing data issue now correctly surfaced by the schema. Once resolved (recommendation: add empty `"text": ""` to the 5 token cards), and with .NET 8 SDK available for C# test verification, this batch is ready for commit.

**Commands for Codex**:
1. Add `"text": ""` to flame_drake, machine_golem, neutral_mercenary, sea_leviathan_young, wood_sapling
2. Re-run schema validation: `python -c "from jsonschema import validate; ..."` expecting 5/5 pass
3. When .NET 8 SDK available: `dotnet test src/Engine/Tests/ -c Release`
