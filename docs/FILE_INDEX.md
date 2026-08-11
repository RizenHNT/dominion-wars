# 项目文件一览

本目录是《统御战纪》的可维护源码根目录。旧发行包与外部 AI 工具不放入本仓库。

## 核心目录

| 路径 | 用途 | 主要负责人 |
| --- | --- | --- |
| `src/main/java/` | 游戏规则、AI、Swing UI、本地 Web 服务 | Codex |
| `src/test/java/` | 规则回归测试、模拟器、截图测试 | Codex / DeepSeek |
| `web/` | 浏览器界面及其配置 | 人类负责人 / GPT 网页端；Codex 支持 API 接入 |
| `data/cards/` | 卡牌定义 | 设计确认后由 Codex 修改 |
| `data/decks/` | 预构筑卡组 | 设计确认后由 Codex 修改 |
| `data/balance.json` | 全局平衡参数 | Claude 规划，Codex 实现，DeepSeek 验证 |
| `data/art/` | 游戏插画资源 | 人工审核后纳入 |
| `docs/` | 规则、设计、平衡和交接文档 | Claude / Codex |
| `design/runtime-kit-v1.30/` | UI 资产、动效样例、适配器契约和实现清单 | Claude 规划，Codex 接入，DeepSeek 验证 |
| `scripts/` | 构建与启动入口 | Codex |

## 关键入口

- `src/main/java/com/dominionwars/app/Main.java`：桌面程序入口。
- `src/main/java/com/dominionwars/server/WebServer.java`：网页版服务入口。
- `src/main/java/com/dominionwars/engine/Game.java`：核心规则状态机。
- `src/main/java/com/dominionwars/ai/AiAgent.java`：游戏内对手 AI。
- `src/test/java/com/dominionwars/test/TestMain.java`：规则回归测试入口。
- `src/test/java/com/dominionwars/test/SimMain.java`：批量对局模拟入口。
- `web/app.js`：网页端交互逻辑。
- `design/runtime-kit-v1.30/README_FIRST.md`：设计交接包入口。
- `design/runtime-kit-v1.30/contracts/`：Snapshot、Action、Event、布局、主题和动效契约。
- `design/runtime-kit-v1.30/manifests/IMPLEMENTATION_TODO.csv`：按优先级排列的接入任务。
- `docs/AI_MAILBOX.md`：AI 之间的轻量异步通知与行动项。

## 夜班自动化

- `docs/DAILY_GOAL.md`：每日由人类批准的目标记录，供《统御战纪》PL 规划步骤读取并据此限定当晚任务范围。
- `docs/NIGHTSHIFT_WORKFLOW.md`：自动化夜班工作流说明，涵盖状态机与安全门、配置和运行器用法，以及凭据设置指导。
- `scripts/nightshift/nightshift.config.json`：夜班配置文件，定义修复与任务上限、受保护分支、PL、开发和 QA 提供方设置，以及允许的测试配置。
- `scripts/nightshift/run-nightshift.ps1`：夜班入口与运行器循环；读取每日目标和配置，编排 PL 规划、Codex 实现、允许列表测试、DeepSeek QA、有限修复循环及最终报告。
- `scripts/nightshift/start-day-shift.ps1`：把人类已批准的白班目标提交到隔离分支，并立即启动同一条受审计流水线。
- `scripts/nightshift/setup-deepseek-key.ps1`：DeepSeek 凭据设置辅助脚本，以当前 Windows 用户的 DPAPI 加密密钥并保存到本机 `%LOCALAPPDATA%`，不把明文写入仓库。
- `scripts/nightshift/verify-nightshift-index.ps1`：夜班文件索引验证脚本，检查本节所需路径以及 `.nightshift/` 本地、Git 忽略状态说明。

运行时状态及原始提供方输出位于 `.nightshift/`；该目录仅保存在本机，并被 Git 忽略。

## 仓库外目录

- `../dominion-wars-deepseek/`：DeepSeek API 环境与测试工具，包含本地密钥，不进入本仓库。
- `../dominion-wars-legacy-2026-08-08/`：v8、v8.2、旧截图和旧开发包的只读归档。

## 不应提交

`build/`、JRE、EXE/JAR 发行包、虚拟环境、`.env`、截图、临时备份和已经解压的原始 ZIP 均由 `.gitignore` 排除。

## 设计包状态

Runtime Design Kit v1.30 已于 2026-08-08 解压到 `design/runtime-kit-v1.30/`。包内 365 个清单项已通过 SHA-256 校验。该目录是设计和接口基线，不表示现有 Web UI 已完成其中列出的功能。
