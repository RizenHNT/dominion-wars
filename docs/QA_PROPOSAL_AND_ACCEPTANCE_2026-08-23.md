# ④⑤ 策划提案 + ①-③ 验收测试（DeepSeek QA/策划，2026-08-23）

> 环境：Windows / C# 引擎（Release .NET 419 tests 基线）+ Java parity + bundle_v2 设计源
> 派工依据：`docs/PL_REPORT_2026-08-23.md` §6/§6.1/§7.2 + `docs/AI_MAILBOX.md` L239-241
> 角色边界：本文件为 DeepSeek 策划/QA 产出，**不修改任何生产代码**；落地由 Codex 实施。

---

## 一、④ 古木 512 数值提案

### 结论（确认 PL 草案 A）

**采纳 512 阈值 + 疯长仅统领结算（×8 疯长 10 步达标）**，不采纳备选 384。

### 数值证据（引擎真实公式模拟）

引擎公式（`EffectRuntime.Combat.cs` `ApplyGrowth`）：
`effective = (baseAmount + rootLayers) × 2^min(3, rampantLayers)`，且对巨物为**累加**（`Health = Health + effective`，非重算）。

模拟模型：起始 HP=1 的巨物，每步打出 1 张"扎根"卡（RootStacks+1）并 BUFF 作用于巨物（baseAmount=1，对应 bundle 木卡 BUFF amount=1）：

| 疯长层数 | 倍数 | 512 达标步数 | 达标时 HP | 384 达标步数 |
|---|---|---|---|---|
| 3 层 | ×8 | **10 步** | 521 | 9 步 |
| 2 层 | ×4 | 15 步 | 541 | 14 步 |
| 1 层 | ×2 | 22 步 | 551 | 21 步 |
| 无 | ×1 | 31 步 | 528 | 30 步 |

（start HP 1/8/16 对步数无影响：×8 疯长 512 均为 10 步。）

**与 PL 草案吻合**：PL 称"修 BUFF 目标 SELF→FRIENDLY 后 ×8 仅 11 步达 528"；我方独立模拟为 10 步达 521（差异源于模型参数假设，量级一致，均约 5-6 回合双卡达成）。

### 关键判读

1. **512 可达性成立**：×8 疯长 10 步 ≈ 5-6 回合，符合"养巨物"终局节奏；384 与 512 仅差 1 步，不足以构成显著平衡差异，且 **512=2^9 保持翻倍乘法主题** → 确认 512。
2. **疯长 ×8 是唯一可行档**：无疯长 31 步、×2 疯长 22 步、×4 疯长 15 步均过慢；只有 ×8（3 层封顶）节奏合理。→ "疯长仅统领结算"是必要设计，普通木卡不得提供乘法。
3. **Sealed 语义自洽**：GIANT_HEALTH_GE 要求 `Sealed`，而 growth BUFF 自动设置 `Sealed=true / Attack=0 / Shield=false`（`Combat.cs:192-197`）→ 目标巨物被强化后自动封印、不能攻击/特殊能力，与"养巨物"设计（被 buff 卡不能攻击/用效果）完全吻合。✅

### 落地障碍与建议（交 Codex）

| # | 障碍 | 现状 | 建议 |
|---|---|---|---|
| G1 | 引擎无 `ADD_RAMPANT` action | 疯长只能通过"疯长"tag 卡打出时 +1（`PlayCardActionHandler.cs:347-350`），统领无法经 leaderDef 授予疯长层数 | 新增 `ADD_RAMPANT`/`ADD_ROOT` 效果 action；wood_leader `enterEffects`=SUMMON 2 + ADD_RAMPANT 1、`punishEffects`=PROTECT_TURN + ADD_RAMPANT 2（对齐 bundle 文本"疯长1/疯长2"） |
| G2 | 疯长 tag 遍布 27 张普通木卡且不限阵营 | 普通卡打出现行即叠 RampantStacks（上限 3） | "疯长仅统领结算"→ **移除全部普通木卡的疯长 tag**（27 张），仅统领经 action 授予；扎根 tag（27 张）保留为加法来源 |
| G3 | wood_leader 数据未接 GIANT_HEALTH_GE | 引擎数据仍 `NO_DAMAGE_TURNS_GE / winParam 7` | 迁移为 `GIANT_HEALTH_GE / winParam 512`；字段随 10.11.6 winParam 统一 |
| G4 | bundle wood_leader 文本与数据 gap | 文本承诺"疯长1/疯长2"，leaderDef 只有 SUMMON/PROTECT_TURN | 文本保留但数据补齐 ADD_RAMPANT（与 G1 同修）；或同步修订设计包 |
| G5 | 巨物来源 | leaderDef.enterEffects=SUMMON 2 wood_seedling（种子无效果） | 树苗保留扎根 tag；巨物成长依赖玩家后续扎根卡 BUFF 指向 FRIENDLY（**前提：10.11.7 修 BUFF 目标 SELF→FRIENDLY**，否则树苗 BUFF 只作用自身） |

> ⚠️ **G5 依赖关系**：512 达标路径依赖"BUFF 可指向 FRIENDLY 巨物"。当前 bundle 木卡 BUFF 全为 `target=SELF`（94 张 both、53 张 root 均为 SELF）——这正是 10.11.7 全局修复项"BUFF:SELF 47"的痛点。**④ 的可行性以 10.11.7 落地为前提**，建议 Codex 同步实施。

---

## 二、⑤ 深海潮位规则提案

### 结论（确认 PL 草案 A）

**本包不做潮位，保持 0 张为预期；潮位机制整体留扩展包。** 不采纳备选 B（冻结基础潮位），备选 C（潮汐债）作扩展包候选记录。

### 理由

1. **海阵营轴已完整**：当前引擎 `sea_leader = OPP_DISCARD_TOTAL_GE / winParam 18`（bundle winAmount 12），以"弃牌 + 潮蚀"为完整主轴，无需潮位即可自成体系。
2. **潮位机制全未冻结**：上限、来源、衰减、交换比例四项核心参数均未定义 → 仓促冻结必产生规则债；留扩展包可先确立弃牌轴稳定，再叠加潮位增量。
3. **0 张落地是预期**：本包海卡以弃牌为轴，不因缺潮位产生"能跑但不好玩"。
4. **备选 C 潮汐债**：统领候选机制，已部分落地 3 张（sea 弃牌/潮蚀相关），记录为扩展包候选，不在本包实现。

### 待清理项（交 Codex/数据）

- **sea_leader bundle 文本与数据 gap**：bundle 文本提及"对方获得 1 潮位"，但 leaderDef 无对应效果字段 → 建议删除该残留文案或标注"扩展包预留"，避免实现时误解。
- 引擎 `winParam 18` vs bundle `winAmount 12` 不一致 → 随 10.11.6 winParam 统一时对齐（非本提案范围，仅提示）。

---

## 三、①-③ 验收测试用例（按 PL_REPORT §6 转译）

> ②③ 先离线（引擎层），实机目标选择 UI 依赖 6.3 前端。所有用例以 Java parity + C# 回归为双轨执行。

### ① 统领形态（10.11.1）

| ID | 验收标准（PL §6 转译） | 离线测试要点 |
|---|---|---|
| TC1-1 | 统领离开卡组（含抽牌/任何方式）即**直接进入战场，不入手牌** | 模拟抽牌触顶统领；断言统领进入 Field 而非 Hand；手牌数不 +1 |
| TC1-2 | 主动出场 = 按我方正常打出结算（费用/进入战场效果） | 主动召唤统领；断言入场效果（enterEffects）结算、费用扣除、进 Field |
| TC1-3 | 被惩罚 = 按惩罚结算 | 对方触发惩罚使统领入场；断言走惩罚路径（punishEffects）而非主动路径 |
| TC1-4 | 后续流程与普通出牌一致 | 统领入场后：可成为攻击目标、回合计数、区域规则等同普通随从 |
| TC1-5 | 卡牌自身特殊规则优先 | 若统领声明特殊规则（如伏击/免伤），优先于默认流程 |
| TC1-6 | 非随从统领不可通过"被击败"触发胜负 | 非随从统领被移除/击杀时不判负；胜负仅按明示 winCondition 或资源耗尽 |

### ② 手动下载（10.11.2，离线部分先做）

| ID | 验收标准（PL §6 转译） | 测试要点 |
|---|---|---|
| TC2-1 | 云端高亮提示：云端顶端可手动下载时提示 | 引擎侧：可下载状态信号（能否被 UI 高亮）；实机依赖 6.3 |
| TC2-2 | 选中顶端卡 → 拖箭头选下载目标 | **实机 6.3**：拖拽箭头目标选择流程 |
| TC2-3 | 支付惩罚后结算顶端卡下载效果 | 离线：PULL 结算后 `PullCount++`、顶端卡移出云端并结算下载效果（pullEffects）；重复 PULL 不重复见同一卡 |
| TC2-4 | 云端空/无资源守卫 | 离线：云端为空时禁止下载、无副作用；目标不合法时拒绝 |
| TC2-5 | 手动下载与自动下载一致性 | 手动/自动 PULL 走同一结算路径，PullCount 与胜利进度一致 |

### ③ 破城（10.11.3）

| ID | 验收标准（PL §6 转译） | 测试要点 |
|---|---|---|
| TC3-1 | 破城时防守方抽牌计数扣至剩 1 | 王城被破；断言防守方抽牌计数 = 1（非 0） |
| TC3-2 | 对方首领经普通抽牌方式强制入场 | 破城后对方统领按"普通抽牌"流程强制进入战场（不入手牌，遵循 ① 统领形态） |
| TC3-3 | 进攻方获得增益 | 破城方获得声明增益（参数化验证） |
| TC3-4 | 双方随从型统领对局中，主动破城方直接获胜 | 双方统领均随从型时，主动破城 → 主动方立即获胜（破城优先，避免双条件同时满足的平局） |
| TC3-5 | ROYAL_CASTLE_BREAK 被动触发 | 王城被破坏触发持有该条件的首领获胜（被动、不问谁破城，当前仅 flame） |

---

## 四、给 Codex 的交接摘要

1. **④**：新增 `ADD_RAMPANT`（及 `ADD_ROOT`）action；wood_leader → `GIANT_HEALTH_GE / winParam 512`，enterEffects=SUMMON 2 新芽 + ADD_RAMPANT 1，punishEffects=PROTECT_TURN + ADD_RAMPANT 2；**移除 27 张普通木卡疯长 tag**、保留扎根 tag；依赖 10.11.7 的 BUFF 目标 SELF→FRIENDLY 修复。
2. **⑤**：本包不做潮位（数据 0 张为预期）；清理 sea_leader bundle 文本残留"对方获得 1 潮位"。
3. **①-③**：按 §三 用例实现后提供引擎测试入口（Java parity + C# regression）。

## 五、验收证据清单

- ④ 数值模拟脚本（PowerShell + Python，模型对齐 `ApplyGrowth` 累加语义）——见本会话执行记录
- 回归基线：Release .NET 419/419、schema 91/91、deck 4/4、manifest 320/320、Java 38/38
