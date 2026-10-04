# 双引擎字段一致性缺陷（阻断卡表设计，需实现方修）

**日期**：2026-09-13
**作者**：DeepSeek V4 Flash（临时 PL）
**性质**：**三个引擎缺陷**。不是设计意见 —— 是我在动手写卡表前核查"设计能否落地"时发现的。
**结论**：**每效果的 `condition` 不能用来设计卡牌**，直到缺陷 D1 修好。

---

## D1（阻断级）每效果 `condition` 只在 C# 生效，Java 完全不生效

| | C# | Java |
|---|---|---|
| 解析 | `src/Data/CardCatalog.cs:524` → `EffectSpec.Condition` | `CardDef.java:62-64` → 存进 `extra` 映射 |
| 结算时读取 | ✅ `src/Engine/Effects/EffectDispatcher.cs:131-138`，在 `effect.Apply` 之前拦截 | ❌ **无任何读取点** |

**证据**：`grep '"condition"' src/main/java` → **0 命中**；`Effects.checkCondition` 在 Java 里**只有一个调用点**
（`Game.java:382`），且那是**卡级** `punishCondition`，不是每效果的 `condition`。

**后果**：如果在卡上写 `"condition": "ENEMY_MINIONS_GE_1"`：
- **C# 会拦截**（不满足则不结算）
- **Java 会照常结算**（条件被当成 `extra` 数据原样存下，从不评估）

⇒ **两引擎行为分歧，且分歧是静默的。** 我原本打算用这个字段给高收益卡加发动条件，
现在**这条路被封**。owner 要的"如果 xxx，【字段】"，**只有卡级 `punishCondition` 是双引擎都认的**。

---

## D2（安全级）未知条件词条：C# fail-closed，Java **fail-open**

| | 行为 |
|---|---|
| C# `PunishConditionEvaluator.cs:64` | `return false`（**不满足** → 不结算） |
| Java `Effects.java:66` | `return true`（**满足** → 结算） |

**后果**：卡上写一个拼错的条件词条（如 `EMEMY_MINIONS_GE_1`）：
- C#：效果**永不结算**（安全，立刻暴露）
- Java：效果**永不拦截**（危险，看起来"正常"）

⇒ **同样的数据，两引擎结果相反。** 而且 Java 那条恰好是"无条件生效"，
意味着**一个笔误会把受条件保护的高收益卡变成无门槛卡**，且**在 Java 侧看不出来**。

**建议**：Java 改为 `false`，与 C# 一致。未知词条必须失败关闭（fail-closed）。

---

## D3（设计限制）卡级 `punishCondition` 白名单被硬截断为 4 个值

```csharp
// src/Data/CardCatalog.cs:22
private static readonly HashSet<string> PunishConditions = new HashSet<string>(
    new[] { "ALWAYS", "ENEMY_MINIONS_GE_1", "ENEMY_MINIONS_GE_2", "HAND_GE_3" }, StringComparer.Ordinal);
```
`ValidateEnum(element, "punishCondition", PunishConditions, source)`（`CardCatalog.cs:205`）

**问题**：
- 白名单**只有 4 个值**，而求值器 `PunishConditionEvaluator` 实际支持 **8 类**（含 `SELF_MINIONS_GE_n`、
  `SELF_LIFE_LE_n`、`SELF_LEADER_ON_FIELD`、`OPP_LEADER_ON_FIELD`，且 `_GE_n` 的 n 任意整数）。
- ⇒ **求值器支持的能力，卡牌数据用不了。** 这是"能力存在但被校验收窄"的浪费。
- 而且 Java 的 `CardDef.java:222` 注释里写的是 `SELF_FIELD_GE_n`，**与 C# 的 `SELF_MINIONS_GE_` 不一致**
  （注释与实现不符，需澄清哪个是真的）。

**建议**：把白名单扩展为与求值器一致（`SELF_MINIONS_GE_n` / `SELF_LIFE_LE_n` / 统领在场等）。
这是纯数据校验的放宽，不改任何玩法语义。

---

## D4（次要）Java 的效果动作表落后于 C#

```java
// src/main/java/com/dominionwars/engine/Effects.java:24-30
```
Java 的 `ACTIONS` **缺少**：`COMMIT`、`PUSH`、`PULL`、`CONTROL`、`ENFEEBLE`、`BANISH`、
`DISCARD_OPP_RANDOM`（有）、`ConvertPunishToDiscard`（有）…

**影响**：`Effects.allKnown()` 是 **PULL 合法动作的前置校验**（注释原文：未注册即拒绝整次下载）。
所以**任何带未注册效果的卡，在 Java 里会导致该次下载整体被拒**。
⇒ 我给机械设计"下载后收益"时，**必须只用 Java 也注册了的动作**，否则 Java 侧测不通。

---

## 对卡表设计的直接后果（我已据此调整）

1. **放弃"每效果 condition"**。改用**卡级 `punishCondition`**（双引擎都认），
   可选值暂时只有 `ALWAYS`/`ENEMY_MINIONS_GE_1`/`ENEMY_MINIONS_GE_2`/`HAND_GE_3`。
   → 因此"如果 xxx"只能写成"**若对方场上有 N 个随从**"或"**若你手牌≥3**"。
   → 想要更丰富的条件（如"若己方有封印随从"），**必须先修 D1 + D3**。
2. **机械的下载效果只用双引擎共有动作**（避开 D4）。
3. 我**不会**把卡面写成带条件、实际不生效的样子 —— 那等于在数据里撒谎。

---

## 需要谁修

- **D1 / D3 / D4 属于实现范围**（`src/`、`data/schema/`），需 Codex 或 owner 批准后实施。
- **D2 是安全修复**，建议优先，因为它是**静默的行为分歧**。
- 在 D1 修好前，**卡表只能用卡级 `punishCondition` 表达发动条件**，我已按此推进。
