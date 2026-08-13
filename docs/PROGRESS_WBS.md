# Progress WBS — Dominion Wars 重做

> **更新时间**：2026-08-13 00:22 · **负责人**：PL (MiniMax M3 / DeepSeek v4) / Codex implementation evidence
> **更新**：Unity 6 LTS 6000.3.21f1 Editor 安装已启动（Hub headless CLI，后台下载 3.5GB）— 见 6.0 备注
> **目的**：树状分解 + 完成度 % + 阻塞标记；替代 / 增强 `IMPLEMENTATION_TODO.csv` 的平铺视图
> **配套仪表盘**：[PROGRESS_DASHBOARD.md](/path/to/docs/PROGRESS_DASHBOARD.md)
> **权威源**：Java 行为基线 + RULES.md + SPEC.md + 设计/contracts/

---

## 总览（项目级）

| 模块 | 完成度 | 状态 | 说明 |
|---|---|---|---|
| 1. 引擎基础 (C# + Java) | **100%** | ✅ | Java 对局基线、C# 模型/IEffect/事件基础已完成；完整 C# MVP 回合控制器另列为 10.2 |
| 2. 数据契约与加载 (Schema + Effects Contract) | **100%** | ✅ | 91 卡 schema 全过；C# CardCatalog/DeckLoader 已完成（3d0c23b、b48b008） |
| 3. 决策与规则同步 (RULES + Decisions) | **100%** | ✅ | Decision A/B/C/D/E 已落 RULES §11 |
| 4. 适配层 (Adapters → Unity) | **85%** | 🟢 | 4.4-4.7 已完成；证据 e1b53d2 + cbc270f |
| 5. 测试矩阵 (Unit + Spec + Contract) | **95%** | 🟢 | 当前 .NET 300/300、Java 38/38；Unity 实机、安装包和性能验收仍未完成 |
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

**当前已具备的支撑**：稳定卡牌 ID、cards Schema、EffectSpec 合约、CardCatalog/DeckLoader、纯 .NET Engine 基础、Adapter、LegalAction、Event 和 Localization。

**仍需后续建设**：完整 C# MVP 回合/对局控制器、显式场地格子/位置模型、统一目标选择与终局检查、适配器事件差集、资源 ID 解析、Unity 运行时接入、可安装 Windows 包和发布验收。

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
- **6.0** Unity 工程壳 + Windows 构建验收 🔴 **下一阶段 P0** — ✅ Editor 已装（6000.3.21f1，2026-08-13 00:27 完成，7.67GB）；待激活 Personal 许可证
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

## 10. 全项目未完成交付流程（执行登记，2026-08-13）

本节是对上面模块摘要的可执行展开。`pending` 表示尚未完成，`blocked` 表示有明确外部门禁，`human_required` 表示需要人类/PL决定，`ready` 表示 Codex 可以在当前批准范围内主动领取。完成项必须留下文件、测试命令和本地 commit 证据；不得用设计稿或静态文件冒充运行时完成。

### 10.1 规则、基线与数据准备

| ID | 未完成交付 | 负责人 | 依赖/验收证据 | 状态 | Codex自动执行 |
|---|---|---|---|---|---|
| 10.1.1 | 将 WBS、IMPLEMENTATION_TODO、CURRENT_IMPLEMENTATION_STATUS 的过期状态统一 | PL + Codex | 三份文档数字一致，保留历史证据 | ready | 是（仅文档核对） |
| 10.1.2 | 建立 91 卡 `id → artId → asset path` 映射清单 | Codex | `scripts/report-card-art-map.ps1`；91 卡、7 个 leader 记录、4 个现有 fallback、87 个缺图、0 个 artId | done (`019cf1e`) | 是 |
| 10.1.3 | 为现有卡牌补齐正式 `artId` 字段或记录迁移方案 | PL + 人类 | schema、loader、旧 Java 数据一致 | human_required | 否 |
| 10.1.4 | 4 套牌组加载、数量、阵营和领袖约束的发布前检查 | Codex + DeepSeek | `scripts/validate-decks.ps1`；4 套、91 卡；`DECK_VALIDATION pass=4 fail=0` | done (`a92b151`) | 是 |
| 10.1.5 | 确认 C# 与 Java 的字段/效果/目标差异清单 | Codex + DeepSeek | `scripts/report-engine-alignment.ps1` + `docs/CODEX_ALIGNMENT_REPORT_2026-08-13.md`；效果 24/24、目标差异 3 项、字段差异列明并标记 PL/HUMAN_REQUIRED | done (`f3f2e6c`) | 是 |

### 10.2 C# MVP 引擎闭环

| ID | 未完成交付 | 负责人 | 依赖/验收证据 | 状态 | Codex自动执行 |
|---|---|---|---|---|---|
| 10.2.1 | 回合状态机：阶段、当前玩家、回合结束、重置窗口 | Codex | 状态迁移单测 + Java 对照 | pending | 需先由 PL 确认阶段边界 |
| 10.2.2 | 统一 GameAction 执行入口（出牌、攻击、结束回合） | Codex | 合法行动只能经入口执行 | pending | 需先由 PL 确认动作范围 |
| 10.2.3 | 场地位置/实体目标模型（随从、统领、王城、生命核心） | Codex | 目标联合类型 + 非法目标 fail-closed 测试 | human_required | 否，先由 PL确认模型 |
| 10.2.4 | 统一抽牌、弃牌、洗牌、伏击和惩罚链服务 | Codex | Java parity + 空发/连锁测试 | pending | 需先拆分并确认规则边界 |
| 10.2.5 | 统一 checkAll/终局/门限保护/统领死亡处理 | Codex + PL | 终局短路、门限和王城路径测试 | human_required | 否，规则冲突时升级 |
| 10.2.6 | Adapter 未映射内部事件的明确映射或 fail-closed 清单 | Codex + PL | ui_event schema 逐项测试 | human_required | 否，涉及契约语义 |
| 10.2.7 | C# 5 核心路径端到端测试（含 Java 对照） | Codex + DeepSeek | 5 路径、事件因果、状态快照证据 | pending | 是（前置项完成后） |

### 10.3 Unity 工程与运行时接线

| ID | 未完成交付 | 负责人 | 依赖/验收证据 | 状态 | Codex自动执行 |
|---|---|---|---|---|---|
| 10.3.1 | Hub 交互式打开工程并生成 3 个本地包的 UPM lock | 人类 + Codex | packages-lock 出现本地包且无编译错误 | blocked | 否（Unity许可/GUI门禁） |
| 10.3.2 | Unity 编译 Engine/Data/Adapters，解决 netstandard2.1 与 System.Text.Json 边界 | Codex + 人类 | Unity Console 无错误；不把 NuGet假设当成事实 | blocked | 部分，需先完成10.3.1 |
| 10.3.3 | Bootstrap 场景、数据加载、快照/事件订阅骨架 | Codex | Unity EditMode/PlayMode 测试 | pending | 是（编译门禁解除后） |
| 10.3.4 | 牌桌布局、卡槽、手牌、统领、王城和阶段区域 | 人类 + GPT Web + Codex | 1280×720/1440×900 截图验收 | human_required | 否（视觉方向） |
| 10.3.5 | LegalAction 驱动的点击/拖拽/目标选择 | Codex + 前端 | 同一 GameAction 入口 + 目标反馈测试 | pending | 部分 |
| 10.3.6 | 惩罚链、parentEventId、空发和反制的可视化 | Codex + 前端 | 因果链可追溯 UI 测试 | pending | 部分 |
| 10.3.7 | 阶段动画、减少动效、输入/键盘/无障碍支持 | 前端 + DeepSeek | reduced-motion/a11y/input 报告 | human_required | 否 |
| 10.3.8 | 结果页、重开、错误恢复和数据加载失败提示 | Codex + 前端 | 失败路径与重开测试 | pending | 部分 |
| 10.3.9 | Windows 开发构建与干净机器启动 | Codex + 人类 | Unity build log、可启动 `.exe`、版本记录 | blocked | 否（需 Unity Hub） |

### 10.4 资源、图片与皮肤流水线

| ID | 未完成交付 | 负责人 | 依赖/验收证据 | 状态 | Codex自动执行 |
|---|---|---|---|---|---|
| 10.4.1 | 清点设计包 320 项资源并生成可消费的 asset manifest | Codex | 既有 `ASSET_MANIFEST.csv` 320 行/320 文件；`scripts/validate-design-manifest.ps1` 校验路径、SHA256、alpha、透明度尺寸元数据与覆盖率 | done (`4395bdf`) | 是 |
| 10.4.2 | 将通用 SVG/PNG 资源导入 Unity 或建立只读导入步骤 | Codex | Unity AssetDatabase/导入报告 | blocked | 需 Unity |
| 10.4.3 | 4 张统领图接入并验证 fallback/缺图行为 | Codex + 前端 | 4 阵营牌面截图/测试 | pending | 部分 |
| 10.4.4 | 91 张卡牌专属图片的生产、命名和版权确认 | 人类 + GPT Web/美术 | 91/91 artId 与文件 hash | human_required | 否 |
| 10.4.5 | skin manifest、主题切换和资源缺失 fail-closed | Codex + 前端 | skin schema + 两套皮肤 smoke | pending | 部分 |
| 10.4.6 | 资源尺寸、透明通道、文本烘焙和本地化图像检查 | DeepSeek | 可复现 asset QA 报告 | pending | 是（检查） |

### 10.5 前端功能与内容工具

| ID | 未完成交付 | 负责人 | 依赖/验收证据 | 状态 | Codex自动执行 |
|---|---|---|---|---|---|
| 10.5.1 | Deck Builder：卡组编辑、数量/阵营/领袖校验 | Codex + 前端 | 合同驱动 UI + 4 套 deck fixture | pending | 部分 |
| 10.5.2 | 卡牌详情、关键词、惩罚提示和目标说明 | 前端 + GPT Web | 文案/视觉由人类确认 | human_required | 否 |
| 10.5.3 | 旧 Web 原型与新 Unity 运行时的边界和保留策略 | PL + 人类 | 明确保留/迁移/冻结，禁止双重规则 | human_required | 否 |
| 10.5.4 | 启动菜单、设置、语言、音效和重置流程 | 前端 | Unity 可运行场景验收 | pending | 否（视觉决策） |

### 10.6 QA、性能与发布门禁

| ID | 未完成交付 | 负责人 | 依赖/验收证据 | 状态 | Codex自动执行 |
|---|---|---|---|---|---|
| 10.6.1 | 全量 .NET/Java/schema/alignment 回归脚本统一 | Codex + DeepSeek | `scripts/run-regression.ps1`；默认离线门禁明确 Unity BLOCKED，`-RequireUnity` 遇 BLOCKED 返回 exit 2；.NET 300/300、schema 91/91、deck 4/4、design manifest 320/320、Java 38/38 | done (`8bb4a9b`, `a92b151`, `42ece24`, `本次补充`) | 是 |
| 10.6.2 | Unity EditMode/PlayMode/Windows smoke | DeepSeek + Codex | Unity log、测试数、构建产物 hash | blocked | 否（需 Unity） |
| 10.6.3 | 多分辨率、输入、减少动效和无障碍 QA | DeepSeek + 前端 | 1280×720/1440×900/键鼠/键盘报告 | blocked | 需 Unity/前端 |
| 10.6.4 | 性能、内存、资源加载和长局稳定性 | DeepSeek + Codex | 20/100/1000 局、帧率/内存证据 | pending | 是（离线部分） |
| 10.6.5 | 干净 checkout 构建、安装、启动、重开和卸载验收 | Codex + 人类 | 可复现 Windows 发布包 | blocked | 否（需 Unity） |
| 10.6.6 | 发布清单：版本、变更、第三方许可、已知问题、回滚包 | PL + 人类 | release checklist 全勾选 | human_required | 否 |

### 10.7 自动化、交接与收尾

| ID | 未完成交付 | 负责人 | 依赖/验收证据 | 状态 | Codex自动执行 |
|---|---|---|---|---|---|
| 10.7.1 | 日间 relay 在真实 MiniMax → Codex → DeepSeek 链路跑一轮 | Codex + PL | exact recipient、relayId、回执和额度记录 | pending | 是（不新增 provider） |
| 10.7.2 | 夜班任务的目标领取、超时、失败降级和报告恢复演练 | Codex + DeepSeek | success/mixed/timeout 三类证据 | pending | 是（不改安全边界） |
| 10.7.3 | 代理配置、WBS、CSV、报告与 Git 提交范围一致 | PL + Codex | 文档/提交交叉核对 | ready | 是（只读/文档） |
| 10.7.4 | 最终本地提交、干净工作树审计和推送前人工确认 | Codex + 人类 | exact staged paths；push 仍需明确授权 | human_required | 否 |

### 10.8 后续自动执行授权

在用户本次明确授权下，若没有新的日目标，Codex 可以主动从状态为 `ready` 的条目领取任务，优先顺序为：

1. 数据/资源清点、manifest 生成、schema/契约核对、回归脚本和测试补强；
2. 不改变规则意图的 C# 基础设施和错误处理；
3. Unity 接线的静态准备和可离线验证代码；
4. 只有在 Unity Hub/许可证/人类视觉决策解除后，才进入 Unity 实机、图片导入和 Windows 构建。

自动执行的硬限制：不擅自决定规则/平衡/视觉、不卡住等待不存在的工具、不新增付费服务、不读取或发送凭据、不修改其他负责人的 dirty 文件、不 push/release。每领取一个任务，必须在 mailbox 或正式报告留下“任务 ID、实际文件、测试结果、未决风险”；发现需要 PL/人类决定的任务就跳过并领取下一个 `ready` 项。

### 10.9 临时代理安全与撤回协议

本阶段的值班 PL、游戏策划和反馈检测器都是**临时代理**，不等同于正式 PL 或 DeepSeek QA。正式负责人回来前，执行以下硬规则：

1. 临时代理只读仓库、WBS、契约和测试结果；不得编辑生产代码、规则、平衡、契约、他人报告、`AI_MAILBOX.md` 或代理配置，也不得 commit/push。
2. 临时代理的 `READY` 只是候选派工，不是产品批准。Codex 只能领取 WBS 已标为 `ready` 且不涉及规则/视觉/Unity 实机/发布的任务；其余统一标为 `HUMAN_REQUIRED`。
3. Codex 每次改动前记录目标文件、HEAD、完整 dirty 清单和目标文件 hash；只允许精确 stage 目标文件。禁止 `git add .`、`git add -A`、reset、clean、rebase、merge、push 和删除性清理。
4. 一个任务一个本地 commit，commit message 必须包含 WBS ID；不把临时代理的建议写成 `CHANGELOG` 或“已完成”状态。临时 QA 只能给出 advisory PASS/FAIL，正式 QA 必须回家后复核。
5. 撤回优先使用 `git revert --no-edit <task-commit>`，不使用 history rewrite；回滚前先确认该 commit 只包含任务允许路径。未提交的临时文件不得覆盖他人 dirty，必要时放在仓库外临时目录。
6. 每次交接必须列出：任务 ID、commit、实际文件、测试命令/结果、未决风险、建议回滚命令。正式 PL/QA 可以逐项接受、返工或撤回，不需要恢复整个工作区。

因此，临时代理可以让 Codex 在你上班时持续推进低风险任务，但不能在你回家前替正式 PL 做最终规则、视觉、Unity、发布或 QA 签字。

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
