# Known Issues — Unity U-00 → U-03 Cycle (2026-08-24)

本清单同时记录本周期环境阻塞与不属于硬规则 a/b/c 的延期问题。标为 a/b/c 的项目只允许做解除对应 UI 阻塞所必需的最小修复；其他项目不在本周期现场修复，统一留到周期收尾窗口或下周期开始处理。

## Cycle closeout — 2026-08-24

- **P0 = 0**；没有会阻止 U-00～U-03 占位周期关闭的生产缺陷。
- KI-001、KI-003、KI-005 是 45%/52%/56% 历史检查点的环境观察，现已由最终实机证据取代：交互式 Editor PID 33236 已通过 Pipeline port 7800 连接，tabletop-v2 已完成取证，最终状态为 `ready`、`playMode=stopped`、`compiling=false`、`domainReloadInProgress=false`。Software Terms 从未被可见窗口证实，不能写成最终或唯一阻塞。
- KI-002 与 KI-004 保留为 P3 工具环境说明；它们不是游戏缺陷，也不影响已完成的实机门禁。
- 保留 **P1 视觉跟进**：证据画面水印、卡牌文字层级/可读性、最终美术与整体 polish。这些属于后续视觉生产，不是本周期使用占位素材验收的未完成项。
- 最终门禁：U-00/U-01/U-02/U-03 全 PASS；EditMode **65/65**、PlayMode **3/3**、.NET **466/466**、Unity compile **0 errors**、Console 新边界 **0 warnings / 0 errors**。权威实机报告：`docs/evidence/unity-u00-u03-2026-08-24/tabletop-v2/UNITY_LIVE_FLOW_REPORT_2026-08-24.md`。

## KI-001 — 当前没有可连接的 Unity Editor

Closeout status: **RESOLVED / HISTORICAL CHECKPOINT**；以下保留发现当时的原始记录。

- 严重度：BLOCKER；硬规则分类：**a（Unity 无法进入可连接状态）**。
- 发现：Pipeline 已通过官方 CLI 安装为 `com.unity.pipeline` `0.5.0-exp.1`；当前发现 **4 个指向同一项目的 Editor 进程**，但 Computer Use 没有找到可交互的同项目 Editor 窗口，`unity status --project-path C:\Users\USER\Documents\dominion-wars-win64\unity\DominionWars.Unity --format json` 仍返回 `STATUS_NO_INSTANCES`。最新启动尝试受 Licensing Client mutex 阻塞。
- 影响：无法通过 Pipeline 驱动已打开的 Editor，也无法完成 U-00～U-03 的实时交互验证。
- 位置：环境/Editor 状态；不涉及卡牌或生产代码。
- 处理：需要人类确认四个进程各自对应的可见窗口并只保留一个实例，观察真实窗口状态，解除 Licensing Client mutex 竞争后等待 Pipeline 变为 `ready`；代理不自动结束用户的 Editor 进程，不改引擎、规则、Adapter。

## KI-002 — 新终端的 PATH 尚未包含 Unity CLI

- 严重度：P3（工具可用性问题）
- 发现：当前会话 `Get-Command unity`/`where unity` 找不到命令；实际可执行文件为 `C:\Users\USER\AppData\Local\Unity\bin\unity.exe`，版本 `1.0.0-beta.6`。
- 影响：新终端直接输入 `unity` 可能失败，但使用上述绝对路径可继续执行 CLI。
- 位置：用户环境 PATH；不涉及卡牌或生产代码。
- 处理：由用户在需要时刷新终端 PATH；本周期不修改系统环境或项目代码。

## KI-003 — Software Terms 仅为日志推断，尚未由可见窗口证实

Closeout status: **SUPERSEDED / HISTORICAL CHECKPOINT**；Terms 未经可见窗口证实，最终也不是周期阻塞。

- 严重度：BLOCKER；硬规则分类：**a（Unity 无法打开/连接）**。
- 发现：真实用户上下文已经登录且许可证有效，**不需要重新登录或激活**。CLI 日志曾提示 Unity Editor Software Terms，但 Computer Use 没有找到对应窗口；因此不能再断言四个 Editor 都停在条款页，也不能把 Terms 当作唯一已证实阻塞。
- 影响：Unity Test Runner 无法实际运行，EditMode/PlayMode 与 U-00～U-03 实机验收不能开始。
- 位置：Unity 用户条款、账号与许可证环境；不涉及项目生产代码。
- 处理：人类在保留的一个官方 Editor 中按真实可见内容处理；只有实际出现条款窗口时才接受条款，无须重登录。文档代理不代替用户接受条款。

## KI-005 — Licensing Client mutex 与无可连接窗口

Closeout status: **RESOLVED / HISTORICAL CHECKPOINT**；最终已取得单一可连接 Editor 与 Pipeline `ready` 证据。

- 严重度：BLOCKER；硬规则分类：**a（Unity 无法进入可验证状态）**。
- 发现：最新 Editor 启动尝试报告 Licensing Client mutex 竞争；同时没有可由 Computer Use 或 Pipeline 连接的 Editor 窗口。
- 影响：Unity Test Runner/EditMode 无法启动，新增 UI 交互与 Pull 生命周期测试只能保留静态证据。
- 位置：Unity Licensing Client/Editor 进程环境；不涉及 production 卡池、引擎规则或 Adapter。
- 处理：由人类确认并收敛四个同项目 Editor 实例，避免代理盲目杀进程；得到一个可见且 Pipeline `ready` 的 Editor 后再执行测试。

## KI-004 — 沙箱工具身份只读访问导致 SQLite Error 14

- 严重度：P3（工具环境限制；不是用户许可证或数据库损坏证据）。
- 发现：CLI 在沙箱工具身份下对 Unity 本地状态库只有只读/受限访问，因此出现 `SQLite Error 14` 和误导性的未登录/license status 不可用结果；真实用户上下文许可证有效。
- 影响：该沙箱身份不能作为用户登录或许可证状态的权威验证来源，但不阻止用户在官方 Editor 中接受条款并继续。
- 位置：沙箱工具身份与 Unity 用户状态库的权限边界；不涉及仓库内引擎、规则或 Adapter。
- 处理：不修改、复制或重建用户数据库；后续许可证判断以真实用户上下文和官方 Editor 为准。
