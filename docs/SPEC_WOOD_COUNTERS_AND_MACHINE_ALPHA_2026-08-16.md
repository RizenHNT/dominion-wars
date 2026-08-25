> ⚠️ **已废弃（2026-08-16）**：本 spec 的扎根数值"×4"已被人类纠正（每层 +1/+1），且机械地标方案已由 `SPEC_MECH_LANDMARK_WOOD_COUNTERS_v2_2026-08-16.md` 取代。仅保留作历史参考，勿据此实现。

# 木计数器因果链 Spec + machine_alpha 下载轴 Spec

> 日期：2026-08-16 · 作者：DeepSeek（策划）· 目标读者：Codex（实现）+ PL（验收）
> 状态：🟡 待 PL 审 → Codex 实现 → QA 复验
> 依据：RULES §12.2/§12.4、effects.contract.md §11.3、bundle_v2 v12 数据

---

## 一、木计数器因果链（rootStacks → 强化增幅 → sealed 封印 → GIANT_HEALTH_GE）

### 现状（已实现，勿重复）

引擎已有：`PlayerState.RootStacks`（int）、`PlayerState.RampantStacks`（int，setter 限 0-3）、`CardInstance.Sealed`（bool）、胜利判定 `GIANT_HEALTH_GE`（EndPhase.cs:155-163，检查场上 Sealed 随从 Health >= winParam）。

### 缺口（本 spec 要补的因果链）

契约 §11.3 定了三个语义，但引擎只落了字段和胜利判定两端，**中间的增幅与封印触发没接**：

1. **扎根增幅**：施加强化（BUFF）时，若来源为"扎根/疯长增幅的强化"，强化量 = 基础 + rootStacks×4；应用后 rootStacks 消耗（归 0）。
2. **疯长增幅**：rampantStacks>0 时，每施加一个 buff 累乘（强化量 ×= 2^rampantStacks），不消耗。
3. **封印触发**：受上述增幅强化的目标进入 `Sealed=true`（攻击归 0、失去能力）。

### 实现方案（最小改动，挂点已核）

**挂点**：`EffectRuntime.Combat.cs` 的 `Buff()` 方法（L138 起），这是所有 BUFF 的唯一入口。

**前置判定**：如何区分"普通 buff"与"扎根/疯长增幅的强化"？契约 §11.3 已定：增幅强化 = 扎根卡/疯长卡的 BUFF。最小实现用 **EffectSpec.Param 标记**（bundle_v2 已用 `param:"root"` 标记扎根卡，`param:"both"` 标记普通强化）——见下方数据约定。

**补丁逻辑（伪码，加在 Buff() 的 foreach 内、Commit 之前）**：

```
// 读取来源玩家的计数器（扎根/疯长属于施放方玩家）
var src = State.GetPlayer(context.SourcePlayerIndex);
int finalAmount = spec.Amount;

// 1) 扎根增幅：param 含 root 标记时按层数加成，应用后消耗
if (spec.Param == "root") {
    finalAmount = spec.Amount + src.RootStacks * 4;
    // 应用后消耗扎根层
    Commit(_ => src.RootStacks = 0);
    // 2) 封印触发：受扎根增幅强化的目标进入封印
    foreach (var target in targets) Commit(_ => target.Sealed = true);
}

// 3) 疯长增幅：rampantStacks>0 时累乘（不消耗）
if (src.RampantStacks > 0) {
    finalAmount = finalAmount * (1 << src.RampantStacks); // ×2^层
    foreach (var target in targets) Commit(_ => target.Sealed = true);
}

// 用 finalAmount 替代 spec.Amount 应用 buff
```

**封印语义（Sealed 触发后，需在攻击/能力判定处拦截）**：
- `Sealed=true` 的随从：Attack 归 0、不能攻击、关键词/触发/主动/被动能力全部失效（仅保留身份/归属/区域/当前生命/生命上限，仍可受伤害/恢复/生命强化/破坏）。
- **挂点**：`AttackActionHandler`（攻击合法性）需加 `if (card.Sealed) 拒绝攻击`；能力触发处（如 onDeath/关键词结算）需跳过 Sealed 卡。具体拦截点由 Codex 在实现时按现有攻击/能力管线定位（spec 不越权指定行号）。

**胜利判定已就绪**（EndPhase.cs:155 已查 Sealed && Health >= winParam），无需改。

### 数据约定（bundle_v2 v12 已符合，Codex 导入时照此）

- 扎根卡：BUFF 的 `param:"root"`（基础量 1-2）
- 疯长卡：BUFF 的 `param:"both"`（普通强化，由 rampantStacks 层数放大）
- 普通 buff（无增幅）：`param:"both"` 且玩家 rampantStacks==0 且非 root 标记 → 不增幅不封印

**⚠️ 待 PL/人类确认的数值**（RULES §12.2 明说未冻结，先按契约 §11.3 默认值落地）：
- rootStacks 每层 +4（契约默认）
- rampantStacks 上限 3（契约已定，引擎已限 0-3 ✅）
- GIANT_HEALTH_GE 阈值 512（bundle 统领 winAmount:512，契约标注"512 留扩展主题，建议 256-384 平衡测试"）

---

## 二、machine_alpha 下载轴统领

### 现状（分叉，需统一）

| 来源 | machine_leader 形态 | winCondition |
|---|---|---|
| `data/cards/machine.json`（旧） | SPELL 吟唱，SUMMON_LEADER `machine_alpha` | NONE（吟唱形态）|
| `bundle_v2`（新 v12） | MINION 8/10 圣盾 | PULL_TOTAL_GE winAmount:6 |

**问题**：旧数据引用的 `machine_alpha` 卡在 data/cards 里不存在（只有 machine_leader）；新数据是直接 MINION 统领，无吟唱。两套对不上。

### 本 spec 结论

采纳 bundle_v2 的**直接 MINION 形态**（简单，无吟唱 token 依赖），但需 Codex 落 data 时处理：

1. **`machine_leader` 改为 MINION 8/10 圣盾统领**，`leaderDef.winCondition = "PULL_TOTAL_GE"`、`winAmount = 6`、`enterEffects = [BUFF ALL_FRIENDLY_MINIONS +1/+1]`、`punishEffects = [ADD_OPP_PUNISH_TURN 2]`。
2. **删除旧吟唱形态**：不再需要 `SUMMON_LEADER machine_alpha` 的 token 卡；若保留旧 machine_leader 吟唱卡，则必须补 `machine_alpha` 定义——但本 spec 选择**简化**，直接用 MINION 统领，砍掉吟唱链。
3. **胜利判定已就绪**（EndPhase.cs:152 `PULL_TOTAL_GE` → `player.PullCount`；Mechanical.cs:134 `Pull()` 已 `PullCount++`），无需改引擎，只改 data。

### 待 PL/人类确认

- 是否接受"砍掉吟唱形态、机械统领直接 MINION"（简化取舍）。
- winAmount 6（累计下载 6 次）是否为首测值（bundle_v2 定 6，QA 此前提过 5-7 区间可调）。

---

## 三、验收标准（Codex 完成后 QA 复验）

1. 木：`Buff()` 对 `param:"root"` 的卡按 rootStacks×4 增幅、应用后归 0；rampantStacks>0 时 ×2^层；两者触发 target.Sealed=true。
2. 木：Sealed 随从不可攻击、能力失效；GIANT_HEALTH_GE 胜利判定仍工作（已有单测 `GiantHealthWinConditionRequiresASealedUnit`）。
3. 机械：machine_leader 改 MINION + PULL_TOTAL_GE=6，Pull() 累计 6 次触发胜利（已有单测 `PullTotalWinConditionUsesSuccessfulPulls`）。
4. 回归：dotnet test 全绿，cards-schema 校验通过，无 ADD_OPP_TIDE/CONSUME_OPP_TIDE/protocolFields 误加。
