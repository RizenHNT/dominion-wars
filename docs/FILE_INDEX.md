# 项目文件一览

本目录是《统御战纪》的可维护源码根目录。旧发行包与外部 AI 工具不放入本仓库。

## 核心目录

| 路径 | 用途 | 主要负责人 |
| --- | --- | --- |
| `src/main/java/` | 游戏规则、AI、Swing UI、本地 Web 服务 | Codex |
| `src/test/java/` | 规则回归测试、模拟器、截图测试 | Codex / DeepSeek |
| `web/` | 浏览器界面及其配置 | Codex |
| `data/cards/` | 卡牌定义 | 设计确认后由 Codex 修改 |
| `data/decks/` | 预构筑卡组 | 设计确认后由 Codex 修改 |
| `data/balance.json` | 全局平衡参数 | Claude 规划，Codex 实现，DeepSeek 验证 |
| `data/art/` | 游戏插画资源 | 人工审核后纳入 |
| `docs/` | 规则、设计、平衡和交接文档 | Claude / Codex |
| `scripts/` | 构建与启动入口 | Codex |

## 关键入口

- `src/main/java/com/dominionwars/app/Main.java`：桌面程序入口。
- `src/main/java/com/dominionwars/server/WebServer.java`：网页版服务入口。
- `src/main/java/com/dominionwars/engine/Game.java`：核心规则状态机。
- `src/main/java/com/dominionwars/ai/AiAgent.java`：游戏内对手 AI。
- `src/test/java/com/dominionwars/test/TestMain.java`：规则回归测试入口。
- `src/test/java/com/dominionwars/test/SimMain.java`：批量对局模拟入口。
- `web/app.js`：网页端交互逻辑。

## 仓库外目录

- `../dominion-wars-deepseek/`：DeepSeek API 环境与测试工具，包含本地密钥，不进入本仓库。
- `../dominion-wars-legacy-2026-08-08/`：v8、v8.2、旧截图和旧开发包的只读归档。

## 不应提交

`build/`、JRE、EXE/JAR 发行包、虚拟环境、`.env`、截图和临时备份均由 `.gitignore` 排除。

