# Progress WBS — Dominion Wars 重做

> **更新时间**：2026-08-12 23:55 · **负责人**：PL (MiniMax M3 / DeepSeek v4) / Codex implementation evidence
> **目的**：树状分解 + 完成度 % + 阻塞标记；替代 / 增强 `IMPLEMENTATION_TODO.csv` 的平铺视图
> **配套仪表盘**：[PROGRESS_DASHBOARD.md](/path/to/docs/PROGRESS_DASHBOARD.md)
> **权威源**：Java 行为基线 + RULES.md + SPEC.md + 设计/contracts/

---

## 总览（项目级）

| 模块 | 完成度 | 状态 | 说明 |
|---|---|---|---|
| 1. 引擎核心 (C# + Java) | **100%** | ✅ | 24 IEffect 全部对齐 Java + 71+38 测试全过 |
| 2. 数据契约 (Schema + Effects Contract) | **100%** | ✅ | 91 卡 schema 全过；SchemaValidator 已落地 |
| 3. 决策与规则同步 (RULES + Decisions) | **100%** | ✅ | Decision A/B/C/D/E 已落 RULES §11 |
| 4. 适配层 (Adapters → Unity) | **85%** | 🟢 | 4.4-4.7 已完成；证据 e1b53d2 + cbc270f |
| 5. 测试矩阵 (Unit + Spec + Contract) | **95%** | 🟢 | 264/264 C#、38/38 Java；EventLogTests 已提交；覆盖率 88.53% |
| 6. 前端 / Unity 渲染 | **0%** | 🔴 | 未启动 — 需 Unity Editor 装好 |
| 7. 资产 / 美术 | **0%** | 🔴 | 未启动 — 需外部依赖 |
| 8. 运行时 QA (Sim + A11y + Perf) | **10%** | 🟡 | 8.2 本地化已建立；8.1 仍为 P3 backlog |
| 9. 自动化 / 接力 | **20%** | 🟡 | scripts/auto-relay 框架已存在未激活 |

**项目总进度**：**~45%**（按 DASHBOARD 权重：引擎 100 / 数据 100 / 决策 100 / 适配 85 / 测试 95 / 其余 0-20）

---

## 横切架构原则：为未来扩展保留空间

这不是当前要交付的 Mod/创意工坊功能，而是从初期开发就必须遵守的架构约束：

- 卡牌内容、规则结算、合法行动、事件记录、UI 展示彼此解耦。
- 新卡牌优先通过 JSON、Schema、稳定 ID、ArtId 和现有效果组合接入。
- 新机制应通过独立 Effect/Rule 模块、LegalAction、Event 和 Snapshot 接入，不把卡牌 ID 写死在 UI 或核心规则中。
- Unity UI 不复制胜利、目标、阶段和合法性判断；只消费引擎快照、合法行动和事件。
- 未来创意工坊内容应以版本化数据包接入；当前不实现上传、下载、Mod 管理器或任意代码执行。
- 新机制完成前必须补齐边界测试、Schema 兼容性和旧卡牌回归验证。

**当前已具备的支撑**：稳定卡牌 ID、cards Schema、EffectSpec 合约、纯 .NET Engine、Adapter、LegalAction、Event 和 Localization。

**仍需后续建设**：完整 C# JSON 加载器、显式场地格子/位置模型、可扩展效果注册表、版本/依赖/冲突校验，以及 Unity 运行时接入。

这条原则的验证样例是“移位（SHIFT）”机制：它应作为可复用的独立机制接入，而不是为某张卡牌增加硬编码特判。

---

## WBS 树状分解

### 1. 引擎核心 ✅ 100%
- **1.1** 24 IEffect 实现 + 单测
- **1.2** EffectDispatcher.Apply(spec, ctx) 路由
- **1.3** Decision A 应用（vulnerabilities + disable_resistance 三步校验链）
- **1.4** Decision B 应用（HP deferred death + clamp 0）
- **1.5** kingSlayer 迁移（card-level → effect-level，4 张卡）
- **1.6** Java TestMain 38/38 baseline

### 2. 数据契约 ✅ 100%
- **2.1** cards.schema.json（25 枚举 + 11 allOf 条件）✅
- **2.2** effects.contract.md（24 动作合约段）✅
- **2.3** schemas/json/contracts/goldens 一致性 ✅
- **2.4** schema 校验脚本 + 91 卡自动验证 ✅ `e1b53d2` / `91/91`

### 3. 决策与规则同步 ✅ 100%
- **3.1** Decision A (字段 Flag × 时点 二维模型) ✅
- **3.2** Decision B (Buff/Debuff clamp 规则) ✅
- **3.3** Decision C (.NET 8 SDK 8.0.424 安装) ✅
- **3.4** Decision D (分支策略：本班不推) ✅
- **3.5** Decision E (接力：Codex 装 scripts/auto-relay) 🟡 in-progress
- **3.6** RULES §11.1 + §11.2 重写 ✅

### 4. 适配层 🟢 85%
- **4.1** ContractDtos.cs ✅
- **4.2** ContractVersionGuard.cs ✅
- **4.3** EngineProjectionAdapter.cs (基础) ✅
- **4.4** GameState → GameSnapshotDto 完整映射 ✅ `e1b53d2`
- **4.5** LegalAction[] → LegalActionDto[] ✅ `e1b53d2`
- **4.6** GameEvent[] → UiEventDto[] (含 parentEventId) ✅ `e1b53d2`
- **4.7** Adapter 单元测试 ✅ `cbc270f`

### 5. 测试矩阵 🟢 95%
- **5.1** 引擎单测 EffectRuntimeTests 34 ✅
- **5.2** 引擎单测 EffectSafetyTests 17 ✅
- **5.3** 引擎单测 AdapterContractTests 5 ✅
- **5.4** 引擎单测 ContractBoundaryTests 6 ✅
- **5.5** Java TestMain 38 baseline ✅
- **5.6** EventLogTests 4 ✅ `1ec8f26`
- **5.7** counter-window-test (8+ 测试) ✅ `dd0aeae`
- **5.8** effect-dispatch-table (30+ [TestCase]) ✅ `dd0aeae`
- **5.9** effects-spec-test (10+ 测试) ✅ `dd0aeae`

### 6. 前端 / Unity 渲染 🔴 0%
- **6.0** Unity 工程壳 + Windows 构建验收 🔴 **下一阶段 P0**
- **6.1** Renderer layout + components 🔴 需 Unity Editor
- **6.2** Battle phase UI 🔴
- **6.3** Targeting legalActions / reason 🔴
- **6.4** Punish parentEventId 因果链渲染 🔴
- **6.5** Motion primitives → engine anim 🔴

### 7. 资产 / 美术 🔴 0%
- **7.1** Content Art 91 张卡图 🔴
- **7.2** Asset ID → SVG/PNG 解析 🔴
- **7.3** Skin manifest 🔴

### 8. 运行时 QA 🟡 10%
- **8.1** sim/regression/a11y/input/perf 🔴 P3 backlog
- **8.2** Localization JP/ZH/EN 🟢 30% ✅ `ae33fad`（20 keys × 3 languages）

### 9. 自动化 / 接力 🟡 20%
- **9.1** scripts/auto-relay/ 框架 (11 文件) ✅
- **9.2** start-relay.ps1 后台入口 ✅
- **9.3** notify-vscode-pl.ps1 fail-closed stub ✅
- **9.4** GitHub Actions 激活 + bot 配置 ⏳ **Codex 接力任务**
- **9.5** 1 轮 Day Shift 无人工干预验收 ⏳

---

## Backlog 优先级映射（18 行 → WBS 节点）

| # | P | 节点 | WBS 编号 | 状态 |
|---|---|---|---|---|
| 1 | P0 | Adapter 实现 | 4.4-4.7 | ✅ done — `e1b53d2`, `cbc270f` |
| 2 | P0 | Renderer 布局 | 6.1 | 🔴 pending |
| 3 | P0 | Assets 解析 | 7.2 | 🔴 pending |
| 4 | P0 | Motion primitives | 6.5 | 🔴 pending |
| 5 | P0 | Battle phase UI | 6.2 | 🔴 pending |
| 6 | P0 | Targeting legalActions | 6.3 | 🔴 pending |
| 7 | P0 | Punish 因果链渲染 | 6.4 | 🔴 pending |
| 8 | P1 | Deck Builder | (待规划) | ⚪ pending |
| 9 | P1 | Localization | 8.2 | ✅ done — `ae33fad` |
| 10 | P1 | Skin manifest | 7.3 | ⚪ pending |
| 11 | P2 | Content Art | 7.1 | ⚪ pending |
| 12 | P3 | Runtime QA | 8.1 | ⚪ pending |
| 13 | P0 | Leader 字段 Flag×时点 | 3.1 + 1.3 | ✅ done |
| 14 | P0 | Effects clamp dispatcher | 3.2 + 1.4 | ✅ done |
| 15 | P0 | DeepSeek Batch 2 QA | (完成) | ✅ done |
| 16 | P1 | Tests (M3-M5) | 5.7-5.9 | ✅ done — `dd0aeae`, `264/264` |
| 17 | P0 | kingSlayer 迁移 | 1.5 | ✅ done |
| 18 | P1 | RULES sync | 3.6 | ✅ done |
| 19 | P0 | Unity 第一阶段可玩垂直切片 + Windows `.exe` | 6.0-6.3 | 🔴 pending — 需安装 Unity 6 LTS |

---

## 本班（白班 20:11 派发）目标

详见 [docs/DAILY_GOAL.md](/path/to/docs/DAILY_GOAL.md) 或 Codex 收到的 /goal 文本。

**6 个 milestone**：
- **M1** schema 校验脚本 → WBS 2.4
- **M2** Adapters 补完 → WBS 4.4-4.7
- **M3** counter-window-test → WBS 5.7
- **M4** effect-dispatch-table → WBS 5.8
- **M5** effects-spec-test → WBS 5.9
- **M6** AI_MAILBOX 收尾段 → 流程

**实际达成**：32% → **45%**（+13 pp，Batch 3+5 全部落地）

---

## 阻塞项一览

| 阻塞 | 影响范围 | 解锁条件 |
|---|---|---|
| Unity Editor 未装 | 6.1-6.5, 7.2-7.3, 8.1 全部 P0/P1 | 装 Unity 6 LTS（人类决策） |
| DeepSeek 接力无 bot | 9.4-9.5 | Codex 接力任务完成 |
| 15 dirty + 10 untracked 未 commit | 流程（不是 WBS 节点） | 人类手动审 + commit（PL 整理轮已 commit 自身文档） |
| 30K+ 改动未 push | main 领先 origin 42 commits | 人类决定推送时机 |

---

## 进度更新规则

1. **完成一个 WBS 节点** → 把对应 backlog 行 status 改 `done` + 加 evidence（commit / 报告 / 文件路径）
2. **开始一个新节点** → status 改 `in_progress` + owner
3. **被阻塞** → status 改 `blocked` + 阻塞原因
4. **新增节点** → 加新行 + 注明加在哪天（"Added 2026-08-12 WBS v1"）
5. **每周** PL 写一次"完成度变化 %"到 PROGRESS_DASHBOARD.md

PL 负责维护本文件 + CSV；Codex / DeepSeek 在完成 milestone 时同步更新。
