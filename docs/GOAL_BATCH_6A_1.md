# Batch 6A /goal 派发文案（Codex 版）— v3 已修正

> 状态: 🔴 门禁未过 → ✅ v3 修正 · 2026-08-13 01:35 · PL (DeepSeek v4)
> 门禁审查: DeepSeek 00:55 发现 3 硬架构问题（v2 修）→ DeepSeek 复核发现 R1-R5（v3 修，PL 独立验证全部属实）
> 派发前必过 PL_CHECKLIST 5 条 + DeepSeek 门禁审查

---

## /goal 文案

你是 Codex，Dominion Wars Unity 6 LTS 重建项目的实现 lead。本轮任务（Batch 6A）：**实现 C# JSON 卡牌/卡组加载器**。这是当前唯一未被覆盖的真缺口。

### 背景（已实测确认，见 docs/BASELINE.md 2026-08-13 复核版）

- 卡数据：`data/cards/*.json` = **91 卡**（flame 21 / machine 22 / neutral 6 / sea 21 / wood 21）
- 预构筑：`data/decks/*.json` = 4 套（flame/machine/sea/wood）
- 卡 JSON 字段（Card 级）：id/name/faction/type/tags/punish/attack/health/keywords/leader/leaderDef/text/flavor（+punishActivatable/punishCost）
- Schema：`data/schema/cards.schema.json`
- **现状**：src/ 下**无任何 Loader/Json/Repository/Catalog 文件，无 System.Text.Json 引用**。C# 端完全未接入卡数据。

### 任务

新增 C# 端卡牌/卡组加载器，接入现有 Engine 的 `CardDefinition`。**注意 3 个已确认的架构约束（PL 拍板，见下方 ⚠️）**：

1. 新增 **`src/Data/DominionWars.Data.csproj`**（新项目，`netstandard2.1` + `System.Text.Json` NuGet 包）——不要塞进 netstandard2.1 的 Engine（保持引擎零依赖）
2. `src/Data/CardCatalog.cs`——加载 `data/cards/*.json` 全部 91 卡 → `CardDefinition`，**fail-closed**（任何文件缺失/坏 JSON/必需字段缺失 → 抛错，不允许静默跳过或部分加载）
3. `src/Data/DeckLoader.cs`——加载 `data/decks/*.json` 4 套预构筑
4. 按下方「字段映射表」逐字段映射 → CardDefinition
5. **单文件 ≤500 行**（PROPOSAL §5.9 硬约束 #9）
6. 新增测试覆盖：91 卡全加载成功 / 4 卡组全加载 / 坏 JSON fail-closed / 必需字段缺失拒载

### 📋 字段映射表（DeepSeek R1-R3 修正，Codex 必须严格照此实现）

| JSON 字段 | 层级 | 落点 | 规则 |
|---|---|---|---|
| id | Card | `CardDefinition.Id` | 必填 |
| name | Card | `Name` | 必填 |
| faction | Card | `Faction` | 必填 |
| type | Card | `IsMinion` | **无 type 字段**：`MINION→IsMinion=true`；`SPELL→IsMinion=false`；`AMBUSH→无落点`（不报错，丢弃+留 TODO）；`PUNISH→IsMinion=false` |
| leader | Card | `IsLeader` | boolean（`const:true` 时 true），缺省 false |
| attack | Card | `Attack` | 缺省 0 |
| health | Card | `Health` | 缺省 1 |
| cost | Card | `Cost` | 缺省 0 |
| rarity | Card | `Rarity` | 缺省按阵营规则或留 TODO |
| keywords | Card | `CardDefinition`（若有关键字字段） | 数组映射；若无落点则校验后丢弃+TODO |
| text | Card | `Text` | **Card 级字段**，必填（schema required） |
| flavor | Card | `Flavor` | **Card 级字段**（非 leaderDef 子字段） |
| punish | Card | `PunishActivatable/PunishCost` | **R3 修正**：schema 有三个字段 `punish(int)`/`punishActivatable(bool)`/`punishCost(int)`，CardDefinition 只有两个 `PunishActivatable`/`PunishCost`。映射规则：`punish>0 → PunishActivatable=true, PunishCost=punish`；`punish=0 且无 punishActivatable → 不设置` |
| leaderDef.vulnerabilities | leaderDef | `CardDefinition.Vulnerabilities` | **R4 修正**：optional 字段，存在才映射；**不得**要求 leader 卡必须有 vulnerabilities（否则误伤合法卡） |

**R2 修正说明**：`text`/`flavor` 是 **Card 级字段**，不是 leaderDef 子字段，不要误映射。`leaderDef.winCondition/winText/persistentEffects/punishEffects/enterEffects/winAmount/winParam/durability/grantLife` 本批**不映射不加载**，留 TODO 注释给后续 leader 运行时模块，**不造半成品模型**。

### ⚠️ 三个已拍板的架构决策（DeepSeek 门禁 00:55 发现，PL 复核后修正）

**决策 1 — Loader 放哪层**：新建独立 `src/Data/` 项目（`netstandard2.1`）。**理由**：`netstandard2.1` 保证 Engine（netstandard2.1）和 Unity 6（.NET Standard 2.1 兼容）都能引用，加载逻辑单一实现三处复用；同时保持 Engine 零依赖（规则引擎不碰 IO/JSON）。

**决策 2 — leaderDef 映射范围**：本批**只映射 `vulnerabilities` → `CardDefinition.Vulnerabilities`**（可选，存在才映射，不强制）。`winCondition`/`winText`/`persistentEffects`/`punishEffects`/`enterEffects`/`winAmount`/`winParam`/`durability`/`grantLife` 本批**不映射、不加载**——它们在 schema 里合法（`LeaderDef.required` 仅 `winCondition`），loader 校验通过即可，但不进 C# 模型，留 TODO 注释给后续 leader 运行时模块。**不造半成品模型**。**注意**：`text`/`flavor` 是 **Card 级字段**，不属于 leaderDef，按表映射到 `Text`/`Flavor`（R2 修正）。

**决策 3 — JSON 库**：**`System.Text.Json`**（微软官方，netstandard2.1 支持，无第三方许可顾虑），不用 Newtonsoft。

**决策 4 — Schema 校验方式**：**不依赖 net8.0 的 SchemaValidator 工具**（netstandard2.1 无法引用，实测不可行）。改为 loader 内用 System.Text.Json 做轻量校验（fail-closed）；完整 JSON Schema 校验保留 SchemaValidator 工具作为**开发期/CI 命令行预检**。规则本身仍在引擎，不复制进 renderer。
**R5 增强（采纳）— 轻量校验覆盖范围**：
- `required`：Card 必填 = `id/name/faction/type/text`（schema Card.required）；leaderDef 必填仅 `winCondition`（本批不加载，校验其类型即可）
- 全部 enum 合法性：`CardType`(MINION/SPELL/AMBUSH/PUNISH)、`Faction`(5)、`Keyword`(4)、`EffectAction`(24)、`EffectTarget`(11)、`WinCondition`(6)
- 数值 min/max：attack/health/cost/punish 等按 schema 边界校验
- 未知字段：warning 不抛（schema 有 additionalProperties:false，但 loader 为前瞻容忍）

### 验收标准

- [ ] 独立跑 `dotnet test --nologo`：当前基线 **264/264**，本轮新增测试后**只增不减**，全绿
- [ ] CardCatalog 加载 91 卡成功（有测试证明）
- [ ] DeckLoader 加载 4 套成功
- [ ] 坏输入（缺失文件/坏 JSON/必需字段缺失）→ 抛异常（fail-closed，有测试证明）
- [ ] 字段映射按「字段映射表」正确（尤其 type→IsMinion、punish→PunishActivatable/Cost、leaderDef.vulnerabilities 可选）
- [ ] 合法 leader 卡**没有** vulnerabilities 时正常加载（R4 防误伤，有测试证明）
- [ ] 单文件 ≤500 行
- [ ] `src/Data/` 项目 `netstandard2.1`，可被 Engine 与 Unity 引用
- [ ] 完成报告写入 docs/AI_MAILBOX.md `[Codex→PL] Batch 6A 报告` 段（含：实际文件、测试数、工时、与基线对比）

### 工时估算（×1.5-2）

Phase 1: Data 项目脚手架 + System.Text.Json 接入（0.5h）→ Phase 2: CardCatalog 定义 + 字段映射（1h）→ Phase 3: 轻量 Schema 校验实现（1h）→ Phase 4: DeckLoader（0.5h）→ Phase 5: 测试（1h）= **4h × 1.5-2 = 6-8h**

### 不阻塞

- 纯 .NET，无需 Unity Editor。Unity 6000.3.21f1 正在后台安装，装好后再派 Batch 6B。

### 参考

- `docs/BASELINE.md`（事实基线，单一起源）
- `docs/PROGRESS_WBS.md` 模块 6/7
- `design/runtime-kit-v1.30/manifests/IMPLEMENTATION_TODO.csv` Data 行
- 现有：`src/Engine/*.cs`（DominionWars.Engine）、`src/Engine/Tests/`、`data/schema/cards.schema.json`
