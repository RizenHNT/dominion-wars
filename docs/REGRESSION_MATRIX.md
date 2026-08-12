# Java → C# EffectAction Regression Matrix

本表按 `src/main/java/com/dominionwars/engine/Effects.java` 的 24 个 `switch` 动作，与 `src/Engine/Effects/` 的 C# 分派实现逐项对齐。这里比较的是结算动作（EffectAction），不是玩家可选的 `LegalAction`。

| # | EffectAction | Java `Effects.java` | C# 实现 | 当前证据 |
|---:|---|---|---|---|
| 1 | `DAMAGE` | `case DAMAGE` | `DamageEffect` / `EffectRuntime` | Java 38/38；.NET |
| 2 | `HEAL` | `case HEAL` | `HealEffect` | Java 38/38；.NET |
| 3 | `DRAW` | `case DRAW` | `DrawEffect` | Java 38/38；.NET |
| 4 | `OPP_DRAW` | `case OPP_DRAW` | `OppDrawEffect` | Java 38/38；.NET |
| 5 | `DISCARD_OPP_RANDOM` | `case DISCARD_OPP_RANDOM` | `DiscardOppRandomEffect` | Java 38/38；.NET |
| 6 | `DISCARD_DRAWN` | `case DISCARD_DRAWN` | `DiscardDrawnEffect` | Java 38/38；.NET |
| 7 | `DESTROY` | `case DESTROY` | `DestroyEffect` | Java 38/38；.NET |
| 8 | `BUFF` | `case BUFF` | `BuffEffect` | Java 38/38；.NET |
| 9 | `GRANT_KEYWORD` | `case GRANT_KEYWORD` | `GrantKeywordEffect` | Java 38/38；.NET |
| 10 | `SUMMON` | `case SUMMON` | `SummonEffect` | Java 38/38；.NET |
| 11 | `SUMMON_LEADER` | `case SUMMON_LEADER` | `SummonLeaderEffect` | Java 38/38；.NET |
| 12 | `END_TURN` | `case END_TURN` | `EndTurnEffect` | Java 38/38；.NET |
| 13 | `ADD_OPP_PUNISH_TURN` | `case ADD_OPP_PUNISH_TURN` | `AddOppPunishTurnEffect` | Java 38/38；.NET |
| 14 | `ADD_SELF_PUNISH_TURN` | `case ADD_SELF_PUNISH_TURN` | `AddSelfPunishTurnEffect` | Java 38/38；.NET |
| 15 | `CONVERT_PUNISH_TO_DISCARD` | `case CONVERT_PUNISH_TO_DISCARD` | `ConvertPunishToDiscardEffect` | Java 38/38；.NET |
| 16 | `PROTECT_TURN` | `case PROTECT_TURN` | `ProtectTurnEffect` | Java 38/38；.NET |
| 17 | `NEGATE` | `case NEGATE` | `NegateEffect` | Java 38/38；.NET |
| 18 | `NEGATE_ENEMY_EFFECTS_TURN` | `case NEGATE_ENEMY_EFFECTS_TURN` | `NegateEnemyEffectsTurnEffect` | Java 38/38；.NET |
| 19 | `SKIP_RESHUFFLE` | `case SKIP_RESHUFFLE` | `SkipReshuffleEffect` | Java 38/38；.NET |
| 20 | `RESTORE_ATTACKS` | `case RESTORE_ATTACKS` | `RestoreAttacksEffect` | Java 38/38；.NET |
| 21 | `GAIN_LIFE` | `case GAIN_LIFE` | `GainLifeEffect` | Java 38/38；.NET |
| 22 | `LOSE_LIFE` | `case LOSE_LIFE` | `LoseLifeEffect` | Java 38/38；.NET |
| 23 | `DAMAGE_CASTLE` | `case DAMAGE_CASTLE` | `DamageCastleEffect` | Java 38/38；.NET |
| 24 | `WIN_GAME` | `case WIN_GAME` | `WinGameEffect` | Java 38/38；.NET |

## 运行证据

- Java：`scripts\build.bat` 后运行 `java -cp "build\classes;build\test-classes" com.dominionwars.test.TestMain`，结果 `通过 38 / 38`。
- C#：`dotnet test src\Engine\Tests\DominionWars.Engine.Tests.csproj --no-restore`，Batch 3 Phase 3 前结果 `225 / 225`。
- 数据：`powershell -NoProfile -ExecutionPolicy Bypass -File scripts\validate-cards.ps1 --NoRestore`，结果 `pass=91 fail=0`。
