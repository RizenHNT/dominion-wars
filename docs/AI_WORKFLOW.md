# AI 协作流程

## 分工

1. Claude 担任长期 PL；Claude 不可用期间由 MiniMax 临时代管，负责产品规划、规则提案、UX 方案、验收标准和实施清单。涉及表现层时，以 `design/runtime-kit-v1.30/contracts/` 为设计基线。
2. Codex 担任程序负责人，负责读取规划、修改 Java 引擎与 API、实现设计包适配器、维护构建脚本并完成本地验证。游戏规则必须保留在 Java 引擎中。
3. DeepSeek 担任测试负责人，负责根据验收标准、JSON Schema 和 Manifest 设计补充测试，执行回归并报告风险；测试职责下不直接修改生产源码。
4. 人类负责人和 GPT 网页端共同负责浏览器前端的结构、视觉、交互和文案；前端只消费引擎给出的状态、合法行动和事件，不自行实现规则。
5. 人类负责人批准规则、视觉、平衡和发布决策。

## 每日汇报节奏

- 当值 PL 每个工作日汇总一次规划与决策，只有出现会阻断全部剩余工作的关键选择或即时安全风险时才临时追加沟通。
- Codex 日报包含实际修改、测试结果、与计划偏差、风险和次日建议。
- DeepSeek 日报包含测试环境、执行命令、通过/失败项、复现步骤和风险等级。
- 前端日报包含完成的画面、使用的契约或模拟数据、待确认交互以及与真实 API 的联调状态。
- 每日报告使用同一日期，并明确写出“需要 Claude 决策”和“需要人类确认”；没有事项时写“无”，避免隐性阻塞。
- `docs/AI_MAILBOX.md` 只用于五行以内的异步提醒和行动项；正式规划、实现交付与测试报告仍按本文格式提交。

## 单次改动的交接格式

Claude 的规划至少写明：目标、非目标、涉及文件、规则变化、验收标准和风险。

Codex 的交付至少写明：实际修改、与规划的偏差、执行过的测试和仍未解决的问题。

DeepSeek 的报告至少写明：测试环境、执行命令、通过/失败项、复现步骤和风险等级。测试失败时不得直接改生产源码，应先提交报告，由 Codex 修复。

## 自动接力

- VS Code 自定义智能体只是角色配置，不是常驻进程；仅在 Markdown 中写 `@MiniMax`、`@Codex` 或 `@DeepSeek` 不算唤醒成功。
- 白天目标经人类批准并写入 `docs/DAILY_GOAL.md` 后，先由 `scripts/auto-relay/start-relay.ps1 -PlanOnly -ApprovedByHuman` 调用 DeepSeek V4 Flash PL，返回计划供人类审阅；只有人类明确批准该计划后，才允许用 `-ApprovedPlan -ApprovedByHuman` 启动 Codex、本地测试、DeepSeek V4 Pro QA、修复循环和 PL 终审。
- 已批准目标内的完成汇报、QA 交接、缺陷回传和 PL 复核属于常规 AI 协作，不再逐次向人类申请；但自动运输必须能验证实际接收者，不能让一个模型冒充另一个角色。
- `code chat` 只面向普通聊天视图，不能作为“已送达 VS Code 智能体窗口 MiniMax PL”的证据。`scripts/auto-relay/notify-vscode-pl.ps1` 当前强制拒绝执行，直到存在可选择并验证目标智能体窗口的正式接口。无人值守协作继续使用已审计的后台接力控制器。
- 夜间继续使用既有 `DominionWars-NightShift` 计划任务；自动接力层不替换夜间调度器。
- GitHub 评论、未知 bot、webhook 或普通 mailbox 文本不得作为执行授权。模型不可用、权限越界、目标不明确或最多三轮修复仍失败时，状态必须落为 `HUMAN_REQUIRED`，其余独立工作继续。
- `scripts/auto-relay/disable-relay.ps1` 是关闭开关；它不强杀正在写文件的进程，但已启动的白天接力会在下一次付费模型调用或测试阶段前协作式停止并落为 `HUMAN_REQUIRED`。

## Runtime Design Kit 接入顺序

1. Claude 从 `manifests/IMPLEMENTATION_TODO.csv` 选择一个明确范围，补充目标、非目标和验收标准。
2. Codex 实现 Snapshot、Action、Event 等必要适配器，并把逻辑资产 ID 映射到资源文件。
3. DeepSeek 校验 Schema、合法行动、因果事件链、分辨率、Reduced Motion、本地化和回归结果。
4. 人类负责人确认视觉效果与游戏体验后，才更新完成状态和 Changelog。

## 文档原则

- `docs/RULES.md` 是玩家规则的权威来源。
- `docs/DESIGN.md` 是技术架构的权威来源。
- `docs/BALANCE.md` 记录平衡目标与结果。
- `docs/CHANGELOG_CASTLE.md` 记录已落地变化。
- 尚未实施的方案应单独写成提案，不得直接混入“已完成”文档。
