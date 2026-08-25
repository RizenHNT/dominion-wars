# Progress WBS — Dominion Wars 重做

> **更新时间**：2026-08-23 · **负责人**：PL (MiniMax M3 / DeepSeek v4) / Codex implementation evidence
> **更新**：10.10.2-10.10.9 attended 批已复核并 push 到 origin/main（0/0 分叉）；工作树已清理（.meta 全部入库 + 旧文档归档 + Java 冻结修复提交）— 见 10.10 节；wire 实体 ID 决策落地（80591fe）；**10.10.6 Unity EditMode 已解锁（2026-08-15 凌晨人类 Hub 打开 + Test Runner 通过）**；**8/21 Codex Unity runtime slice 实机验收通过（EditMode 24/24、PlayMode 2/2、Windows build 0 error 0 warning），证据 docs/UNITY_RUNTIME_VERIFICATION_2026-08-21.md**；**8/23 人类已答 5 项阻塞决策（①-③ 冻结、④-⑤ 委托策划），见 §10.11**
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
| 5. 测试矩阵 (Unit + Spec + Contract) | **99%** | 🟢 | .NET **435/435（2026-08-23 实测）**、Java 38/38、schema 91/91、deck 4/4、design manifest 320/320；**8/21 Unity 实机验收：EditMode 24/24 + PlayMode 2/2 + Windows build 0 error 0 warning**；剩性能/多分辨率/无障碍（10.6.3-10.6.5，依赖前端） |
| 6. 前端 / Unity 渲染 | **15%** | 🟡 | **6.0 Unity shell + RuntimeAdapter + Windows 构建已过（8/21 验收）**；6.1 牌桌布局 / 6.2 对战 UI / 6.3 目标选择 / 6.4 惩罚链渲染 / 6.5 动效 仍 🔴 pending — 这是"能玩"的最大缺口 |
| 7. 资产 / 美术 | **0%** | 🔴 | 未启动 — 91 卡专属图 + skin manifest 需人类/GPT Web/美术（10.4.4） |
| 8. 运行时 QA (Sim + A11y + Perf) | **10%** | 🟡 | 8.2 本地化已建立；8.1 仍为 P3 backlog |
| 9. 自动化 / 接力 | **20%** | 🟡 | scripts/auto-relay 框架已存在未激活 |

**项目总进度**：**~50%**（按 DASHBOARD 权重：引擎 100 / 数据 100 / 决策 100 / 适配 85 / 测试 99 / 前端 15 / 资产 0 / 其余 0-20）

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
- **4.8** Runtime 1.31 Adapter 系列（RuntimeContractV131Snapshot / RuntimeMatchGateway / RuntimeEventCursor）✅ `f58997e`（10.10.3-10.10.5，PL 已复验）

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
- **6.0** Unity 工程壳 + Windows 构建验收 🔴 **下一阶段 P0** — ✅ Editor 已装（6000.3.21f1，2026-08-13 00:27 完成，7.67GB）+ Personal 许可证已激活（UnityEntitlementLicense.xml）+ 人类 Hub 打开工程完成包解析；✅ EditMode 编译+Test Runner 通过（2026-08-15 凌晨）；⏳ 剩 Windows 构建待补跑
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
| 10.6.4 | 性能、内存、资源加载和长局稳定性 | DeepSeek + Codex | 离线长局子项：`scripts/run-java-stability.ps1`，20/100/1000 组均 exit 0、无超时；结果 `docs/CODEX_JAVA_STABILITY_2026-08-13.json`。帧率/内存/Unity 资源加载仍 pending | pending（离线子项完成） | 是（离线部分） |
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

### 10.10 C# Runtime Contract 1.31 与 Adapter Integration（2026-08-13 人类批准队列）

人类已决定：**C# Engine 是未来唯一正式运行时；Java 仅保留为临时行为对照，删除 Java 必须另开经人类批准的收尾目标。** 本节把 `ARCHITECTURE_REVIEW.md` 的审查步骤登记为可执行队列。详细上午目标见 `docs/GOAL_TERRA_CSHARP_ADAPTER_MORNING_2026-08-13.md`，自动接力入口见 `docs/DAILY_GOAL.md`。

| ID | 未完成交付 | 负责人 | 依赖/验收证据 | 状态 | Terra 自动执行 |
|---|---|---|---|---|---|
| 10.10.0 | 技术路线锁定：C# sole runtime；Java parity-only | 人类 + PL | 本节决策；后续 PL 报告同步 | done（人类已确认） | 否 |
| 10.10.0a | 上午 relay 隔离分支同步与无付费 preflight | Codex + 人类 | `start-relay.ps1 -ValidateOnly` 已通过；隔离分支已包含 `main` 且控制路径一致；下一步在正常 Windows 用户会话运行 `run-nightshift.ps1 -SandboxPreflightOnly`，必须通过 test sandbox、developer allow、developer deny 三项且 `PaidModelAttempts=0` | pending（真实无付费探针） | 否，禁止绕过门禁 |
| 10.10.1 | PL 固化 Canonical Runtime Contract 1.31 与 1.30 迁移说明 | Claude/MiniMax PL | version、wire casing、revision、visibility、action/result、target、event matrix；未知产品语义标 `HUMAN_REQUIRED` | done（PL 2026-08-14 已回填决策包 + 解除 Gate；残留 shadow_of_fate 单点跟踪） | 否 |
| 10.10.2 | 1.31 strict schema、golden/invalid fixtures 与可重复验证脚本 | Codex/Terra | fixture 总数；valid 全过、invalid 按预期拒绝 | done（2026-08-14 attended 批；4 schema + 5 valid/4 invalid fixture；validator 5/4/0，PL 已复验） | 是，已批准字段内 |
| 10.10.3 | C# viewer-scoped GameSnapshot、稳定 match/revision 与隐藏信息裁剪 | Codex/Terra | 双 viewer redaction、稳定 ID、确定性投影测试 | done（2026-08-14 attended 批；RuntimeContractV131Snapshot viewer-safe 投影；PL 已复验） | 是，禁止 UI 隐藏补救 |
| 10.10.4 | LegalAction → GameAction → ActionResult 唯一入口与 stale/duplicate 防护 | Codex/Terra | wrong-match/stale/duplicate/not-advertised/actor/payload/game-over 无副作用测试 | done（2026-08-14 attended 批；RuntimeMatchGateway 幂等/revision/广告动作/无副作用；PL 已复验） | 是，未批准 action type 跳过 |
| 10.10.5 | UIEvent 事件时 metadata、映射策略、cursor/dedupe/gap 与因果验证 | Codex/Terra + PL | eventId/parent/revision/turn/phase；unknown 行为一致；禁止日志解析 | done（2026-08-14 attended 批；RuntimeEventCursor 22 类型 + dup/gap/order/parent 全校验；PL 已复验） | 是，语义缺口交 PL |
| 10.10.6 | Unity 非视觉 RuntimeAdapter：Contracts/Transport/Adapter/Bootstrap/Presentation | Codex/Terra | 无规则复制；main-thread/stale response/fake session EditMode 测试 | **EditMode 验证通过（2026-08-15 凌晨人类解锁）**：许可证一直激活（UnityEntitlementLicense.xml）；人类 Hub 交互打开工程 + Test Runner 通过；编译产物齐备（Library/ScriptAssemblies 含 Unity.EditMode.dll）+ packages-lock 已生成。剩余 Windows 构建归 6.0，另行验收 | 是，Unity 环境失败则静态项继续 |
| 10.10.7 | C# 最小端到端 trace：snapshot → advertised action → result → snapshot/events | Codex/Terra | 固定 fixture、revision/action/event/final-state 证据 | done（2026-08-14 attended 批；RuntimeMatchTraceTests 离线 trace；PL 已复验） | 是，不做正式 UI |
| 10.10.8 | DeepSeek 独立 Adapter Gate QA + 最多三轮 Codex 修复 | DeepSeek + Codex/Terra | 精确命令、通过/总数/失败/跳过；Step 8 十项逐条结论 | pending（本批仅 LOCAL_QA 386/386；缺独立 DeepSeek 复核，已转 DeepSeek） | relay 自动交接 |
| 10.10.9 | 上午收尾报告与工作树范围审计 | Codex/Terra | `docs/ADAPTER_INTEGRATION_MORNING_REPORT_2026-08-13.md`；总体 PASS/FAIL/BLOCKED | done（2026-08-14 attended 批；C11 段含验证矩阵/偏差/最终 Gate；PL 已复核） | 是 |
| 10.10.11 | 卡牌设计批次：平衡基本包全卡设计（QA + 策划） | DeepSeek（QA/策划） | 2026-08-15 人类指示：91 张太少，最终目标"几百张+组卡自由度"，**初版只做最平衡基本包**。范围：① 卡牌设计模板（随从/咒文/伏击/惩罚/统领五类字段+平衡参考，即 Codex 实现规格）；② 平衡基本包全卡 spec（建议 ≈120-150 张，flame/machine/sea/wood/neutral 全量重做，每卡 id/字段/效果/文本/平衡理由/强度评级）；③ machine 全量重做（Commit/Push/Rollback/Pull 主题）+ machine_alpha 下载轴胜利（具体协议+阈值）；④ shadow_of_fate 新身份+显式 winCondition（评审⑬ Q1）；⑤ 4 套官方卡组模板；⑥ 自验证平衡性/可玩性。**只出提案，不碰生产代码** | pending（mailbox 已派 DeepSeek，待人类 relay 通知） | relay 自动交接 |
| 10.10.12 | DISABLE_ENEMY_LEADER 机制删除 | Codex/Terra | 2026-08-15 人类裁决机制级删除：`data/cards/neutral.json` shadow_of_fate persistentEffects + 文案；`data/schema/cards.schema.json` PersistentEffectAction 枚举（倾向删除空枚举）；`src/Data/CardCatalog.cs` PersistentActions；Java parity `Game.java:816` + `TestMain.java:627`；C# contract tests（ContractBoundaryTests/EffectsSpecContractTests）；effects.contract.md 已由 PL 同步（4.1 + 24 动作注释） | pending（mailbox 已派 Codex，待人类 relay 通知） | relay 自动交接 |
| 10.10.10 | Java 归档/移除 | 人类 + PL + Codex + DeepSeek | C# parity、Unity、回归、可恢复归档全部通过后另行批准 | human_required（本目标禁止） | 否 |

自动执行规则：按 10.10.1 → 10.10.8 的依赖顺序领取；单项遇到 `HUMAN_REQUIRED` 或环境阻塞时，记录后继续所有独立项；时间不足时完成当前可验证批次，不留下未经测试的半编辑；10.10.9 永远必做。不得因本队列进入 Renderer/UI、Windows 发布或 Java 删除。

### 10.11 2026-08-23 人类 5 项决策冻结与派工（Added 2026-08-23 PL）

人类已逐条答复 5 项阻塞级决策，①-③ 冻结可派工，④-⑤ 委托策划确认。完整记录见 `docs/PL_REPORT_2026-08-23.md` §6。

| ID | 决策/任务 | 负责人 | 状态 | 依赖/验收证据 |
|---|---|---|---|---|
| 10.11.1 | 统领形态：离开卡组即直接出场（不入手牌）；主动出场按自出结算、被惩罚按惩罚结算；后续同普通出牌；卡特殊规则优先 | Codex（实施）+ DeepSeek（验收测试） | 🟢 已实现并通过 DeepSeek 验收 | Codex 已实现三区直接出场路径 + ForceLeaderOut 统一路径 + fail-closed（CODEX_IMPLEMENTATION_REPORT_2026-08-23.md）；基线 456/456 已由 PL 独立复测；DeepSeek 独立验收 TC1-1~1-6 全过（QA_REPORT_2026-08-23_ROUND1.md），TC1-2/1-5/1-6 建议补直接断言（非缺陷） |
| 10.11.2 | 手动下载：云端发光 → 点顶端卡拉箭头选目标 → 支付惩罚 → 结算顶端下载效果 | Codex（实施）+ DeepSeek（验收测试） | 🟢 已实现（引擎部分）并通过 DeepSeek 验收 | 引擎 PULL 法律动作 + fail-closed 处理器 + PULL_DECLARED/CARD_PULLED 事件已落地；正价下载费未臆造资源（cost_system_unavailable）；实机拖拽 UI 仍归 6.3；DeepSeek 独立验收 TC2-1/2-4/2-5 全过（QA_REPORT_2026-08-23_ROUND1.md），TC2-2 实机拖拽依赖 6.3 跳过、TC2-3 建议补支付惩罚路径断言；**人类需定惩罚模型**（当前 22 张机械卡 downloadCost 全 0，不阻塞现卡） |
| 10.11.3 | 破城：抽牌计数扣到剩 1 → 对方首领普通抽牌方式强制出场 → 我方获增益 | Codex（实施）+ DeepSeek（验收测试） | 🟢 已实现并通过 DeepSeek 验收（"我方抽牌"语义仍待人类最终确认） | 破城单次结算 + CycleWinCount=max(existing,9) + 对立首领强制出场 + minion 双统领即时胜 win.castle_break_minion + ROYAL_CASTLE_BREAK 被动双查（CODEX_IMPLEMENTATION_REPORT_2026-08-23.md）；DeepSeek 独立验收 TC3-1~3-5 全过（QA_REPORT_2026-08-23_ROUND1.md，破城 6/6）；**增益归属（记在破城方）语义人类未最终确认，后续内容包会踩坑** |
| 10.11.4 | 古木 512 数值 → 数值策划决定 | DeepSeek（策划/QA）→ Codex 实施 | 🟢 已确认 + 🟢 已实现已验收 | **确认 512 + 疯长仅统领结算**（×8 疯长 10 步达 521；384 仅差 1 步且破坏 2^9 主题，不采纳）。Codex 已实现：ADD_RAMPANT/ADD_ROOT action + wood_leader 迁 GIANT_HEALTH_GE/512（enter SUMMON2+ADD_RAMPANT1，punish PROTECT_TURN+ADD_RAMPANT2）+ 移除普通木卡疯长 tag（保留 Root）；rampant 上限 3 + 非法输入 fail-closed。DeepSeek 独立验收木方 3/3 通过（QA_REPORT_2026-08-23_ROUND1.md）。依赖 10.11.7 BUFF SELF→FRIENDLY 已随批完成。详见 CODEX_IMPLEMENTATION_REPORT_2026-08-23.md |
| 10.11.5 | 深海潮位 → 游戏策划决定 | DeepSeek（策划/QA）→ Codex 实施 | 🟢 已确认 + 🟢 已实现已验收 | **确认本包不做潮位，留扩展包**（0 张为预期；弃牌+潮蚀轴已完整；上限/来源/衰减/交换全未冻结）。Codex 已清理 sea_leader bundle 文本残留"对方获得 1 潮位"；未引入任何未冻结潮位 action。DeepSeek 独立验收确认（QA_REPORT_2026-08-23_ROUND1.md）。详见 CODEX_IMPLEMENTATION_REPORT_2026-08-23.md |
| 10.11.6 | winParam 跨栈断裂（schema 双字段） | PL 建议 + Codex 确认 | 🟡 待人类/Codex 确认迁移决策 | Java 只读 winParam（CardDef L103）+ C# 双字段兼容；Codex 报告：loader/schema 当前同时接受 winAmount 与 winParam（向后兼容），**bundle winAmount→winParam 显式迁移决策未做**（HUMAN_REQUIRED）；PL 建议 schema 统一为 winParam、移除 winAmount（详见 PL_REPORT §3） |
| 10.11.7 | 全局修复：幽灵 P'145 / BUFF:SELF 47 / 防御回补 | Codex（实施）+ DeepSeek（验证） | 🟢 机械部分已实现并通过验收，剩余 2 项 HUMAN_REQUIRED | 机械部分完成：AMBUSH punishCost:0（54 张检查，23 张修正）+ BUFF SELF→FRIENDLY_MINION 54 处/47 卡 + CardCatalog 保留显式 punishCost/punishActivatable（含旧 fallback）；DeepSeek 独立验收全过（QA_REPORT_2026-08-23_ROUND1.md）。**遗留**：① 122 张非 AMBUSH 幽灵 P'（punishCost>0 无惩罚效果）需设计/平衡决策（清成本 vs 补效果）；② 防御回补卡数/数值/破城 heal-buff 语义需设计决策 |
| 10.11.8 | 牌库循环胜负方向反转（人类 2026-08-23 14:17 裁决"按我的意思来"） | PL（转写/文档）+ Codex（实施）+ **游戏策划/DeepSeek（QA 审核）** | 🟡 派工中（Codex 实施中） | 新规则：被抽空方自己胜利计数+1，满 10 被抽空方获胜（=磨空对方者输）；破城方自己=9 不变；自己抽空自己同样计数。RULES.md L18/§9 已由 PL 同步（PL_REPORT §12）。Codex 改 EffectRuntime.Cards.cs Reshuffle 方向 + 测试预期；**已登记需 QA 审核列表（docs/QA_REVIEW_LIST.md），审核归属游戏策划** |

**派工顺序（2026-08-23 更新）**：Codex 已完成 10.11.1/10.11.2/10.11.3 引擎部分 + 10.11.4/10.11.5 实现 + 10.11.7 机械部分（证据 CODEX_IMPLEMENTATION_REPORT_2026-08-23.md，基线 456/456 已由 PL 复测确认）；**DeepSeek 已用 TC1-1~1-6 / TC2-1~2-5 / TC3-1~3-5 完成独立验收，全部通过、无必须修的缺陷**（证据 QA_REPORT_2026-08-23_ROUND1.md）。人类需拍板：③"我方抽牌/增益归属"语义最终确认 + ② 下载惩罚模型 + winParam（10.11.6）迁移决策 + 10.11.7 遗留 ① 幽灵 P'122 与 ② 防御回补设计决策 + 前端视觉方向（解锁 6.1-6.5）。

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
| Unity 实机/构建环境 | 6.1-6.5, 7.2-7.3, 8.1 全部 P0/P1 | ✅ 8/21 已解锁（EditMode 24/24 + PlayMode 2/2 + Windows build PASS）；**但 6.1-6.5 需要人类视觉方向（10.3.4 human_required）** |
| 前端视觉方向 | 6.1-6.5, 10.3.4, 10.5.2, 10.5.4 | 人类 + GPT Web 出视觉/文案方向 |
| 91 卡专属美术 | 7.1, 10.4.4, 10.3.4 | 人类 + GPT Web/美术 产图 + 版权确认 |
| 10.10.8 缺独立 DeepSeek 复核 | 10.10.8 门禁 | DeepSeek 复核 10.10.2-10.10.5 离线 PASS（已转 DeepSeek） |
| 8/23 决策④⑤ | 古木 512 数值 / 深海潮位 | 数值策划（DeepSeek）+ 游戏策划（DeepSeek）确认 PL 草案后回填（见 §10.11） |

---

## 进度更新规则

1. **完成一个 WBS 节点** → 把对应 backlog 行 status 改 `done` + 加 evidence（commit / 报告 / 文件路径）
2. **开始一个新节点** → status 改 `in_progress` + owner
3. **被阻塞** → status 改 `blocked` + 阻塞原因
4. **新增节点** → 加新行 + 注明加在哪天（"Added 2026-08-12 WBS v1"）
5. **每周** PL 写一次"完成度变化 %"到 PROGRESS_DASHBOARD.md

PL 负责维护本文件 + CSV；Codex / DeepSeek 在完成 milestone 时同步更新。
