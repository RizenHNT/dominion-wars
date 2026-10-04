# 实验卡池模拟驱动修复与验收 — 2026-10-04

Codex 接手 `HANDOFF_TO_CODEX_PROBE_DRIVER_DEFECT_2026-10-04.md` 和条件系统交接后完成。范围为模拟脚手架、条件加载校验及相关验证；卡牌数值、费用规则和生产卡池未改变。

## 结果

**已采用 A：两个入口使用同一个当前源码编译的 `PlCsim.SingleMatch`。独立探针不再维护阶段循环。**

最终版本的探针与 `pl-csim` 在同卡池、卡组、策略、规则、种子、先手下，**120 局匹配对局的所有逐局记录完全一致**。胜率差为 0 个百分点，平均回合差为 0。参考入口完整运行 240 局，全部正常结束；探针运行其中涉及古木的 120 局，全部有效。

全量 .NET 回归 **918/918 通过，0 失败、0 跳过**。生产古木仍为 21 张，SHA-256：

`240C42D6DAA10B89653966A8C080573FB4F5A3B3AFDAB0D4EC237FD3C8206F05`

审查前备份所覆盖的 `data/` JSON 均未发生变化。既有工作区改动保持原状，本报告不推断它们属于哪位代理。

## 必须纠正的交接判断

交接准确指出了独立循环、跳过伏兵、策略与统计分叉的问题，但下面三项不能按原文作为根因或验收：

1. **当前路由会拒绝 DISCARD 中的 PLAY_CARD。**针对性测试得到 `action.not_legal_in_phase`，且不改变手牌。不能将“弃牌阶段继续出牌”描述为当前引擎已证实的行为。
2. **手牌上限在回合末 DISCARD 执行，行动阶段不是实时截断到 8/10。**抽牌、惩罚响应后暂时溢出符合当前流程。正确验收是按广告 `requiredCount` 选择 `candidateIds`，作为 `selectedEntityIds` 提交，被接受，并兑现对应数量的 `CARDS_DISCARDED` 事件。
3. **广告动作数不能固定限定为手牌数 ×3。**一个来源可有多个合法目标，攻击、机械动作等也会增加选项。应检查选项来自当前引擎广告、阶段正确、提交结果可追踪，不能删掉合法动作来满足任意数量上限。

最终匹配验证中，行动决策点最多 **60 张手牌、614 个选项**，仍与参考入口逐局完全一致。共有 **1,034 次有效弃牌提交，实际弃掉 6,797 张牌，协议违规 0 次**。例如 12 张手牌时要求弃 4 张，提交成功后剩 8 张。因此 55 张手牌或 517 个选项本身不足以证明手牌上限未执行。

旧探针有独立策略、跳过伏兵、阶段兜底、未完成局计数及版本归属问题，旧胜率不能直接作为新驱动的平衡结论；但也不能仅凭溢出手牌把所有历史数据一概判无效。历史 `pl-csim` 的 5.8–6.4 回合并非不随策略、T1、版本变化的固定标准。本次以当前源码、同条件、逐局精确比较取代历史平均值比较。

## 改动

- `build-output/wood-probe/Program.cs` 只保留入口，`ProbeRunner.cs` 负责加载卡池、卡组、统计及落盘，所有对局调用共享 `SingleMatch`。
- `build-output/pl-csim/Program.cs` 增加 `--cards-dir`、`--decks-dir`、`--seed`、`--match-log`；保留原有默认目录和策略选项。
- `Simulator.cs` 统一种子生成，记录决策、提交、弃牌数量；修复参考驱动重复初始化后跳过首个 AMBUSH 的问题。弃牌缺广告或被拒时停止并记为未完成；无进展保护停止该局，避免绕过未完成弃牌。
- 新增 `DriverTelemetry.cs`，两个入口共用逐局格式。探针单独记录 attempted / valid / invalid，封顶、异常、保护触发或弃牌违规不计为有效败局；出现无效局返回非零状态。
- `build-probe.ps1` 转入 `build-shared-driver.ps1`，从当前 Engine/Data/Adapters/驱动源码编译，输出本地 `PINNED_REVISION.json` 与 `SOURCE_REVISION.json`。运行时核对程序集哈希，不继续复用旧 DLL 混搭。
- 普通 `WoodPoolProbe.csproj` 引用同一套固定程序集，优先按显式 HintPath 解析，避免旧 `wood-probe/run/` 副本抢先。普通项目构建已通过，0 警告、0 错误。
- 每次测量使用独立输出目录；若目录已有内容则拒绝覆盖。保存 `run.json`、`matches.jsonl`、`submissions.jsonl`、`summary.json`，可选 `decisions.jsonl`。
- 决策日志包含实际选择的 `ai_top`；候选顺序明确标为“实际选择在前，其余为引擎顺序”，不冒充完整权重排序。日志上限及遗漏数量显式记录。只记己方手牌身份、对方手牌数量及公共场面。
- 保留 `--verbose`、`--dump-conditions`；Jev 离线脚本增加 `-DecisionFile`，有阶段字段时只读取 ACTION 记录。

`SubmissionTrace.effective_limit` 在超限时等于实际上限；未超限时等于当前手牌数。它由“提交前手牌 − 引擎计算的 requiredCount”得到。`hand_after_resolution` 是整个提交结算后的手牌，可能包含后续阶段产生的合法变化；弃牌证据同时核对广告、选择数和实际弃牌事件，避免把后续抽牌误判成弃牌失效。

## 条件系统修复

- `src/Data/CardCatalog.cs` 登记现有 `CASTLE_HP_LE_` 条件族，并在统一 `ValidateEffects` 中调用条件校验；错误条件在加载时失败，不再等到效果执行时才跳过。
- `build-output/PoolContractValidator.java` 同步登记该条件族。
- 新增加载和求值测试，覆盖卡级/效果级王城生命边界，以及 8 个效果入口中的缺数字、负数、溢出和未知 token。

没有改变这些条件的运行时意义。`ADD_SELF_PUNISH_TURN` 从 0 加负值仍钳在 0，只能移除已有正附加惩罚，不能实现通用降费。`ADD_OPP_PUNISH_TURN +N` 的已有实现持续至对方自己的回合结束；本次没有替 owner 判定或改写其设计意图。

## 验证与实验结果

| 验证 | 结果 |
|---|---|
| 当前源码共享驱动编译 | 成功；记录已有源码/引用兼容警告 |
| 普通探针项目构建 | 成功，0 警告、0 错误 |
| 条件等针对性 .NET 检查 | 32/32 通过 |
| 全量 .NET 回归 | 918/918 通过 |
| 驱动专用复现 | legacy/default 均保留首个 AMBUSH；可设置伏兵；封顶局排除；DISCARD 拒绝出牌并执行准确选择 |
| 最终同种子比较 | 120 匹配局逐局完全一致；参考全场 240/240 完成 |
| 动态 v2 / 静态对照 | 各 150/150 完成，2,544 次弃牌提交全部有效 |
| Java 校验器 | v2 8 张 PASS；王城条件合法样本退出 0，错误效果条件样本退出 1 |
| Jev 脚本兼容 | 参数和语法检查通过；本次未进行模型评分 |
| 生产数据/编译源码漂移 | 生产 JSON 未变；最终编译清单源码无漂移 |

动态/静态每组各 30 局（3 对手 ×10；古木 seat 0，交替先手）：

| 密度组 | 动态胜率 | 静态胜率 | 动态平均回合 | 静态平均回合 |
|---|---:|---:|---:|---:|
| v2-0 | 30.0% | 30.0% | 10.57 | 10.57 |
| v2-2 | 20.0% | 20.0% | 10.10 | 10.10 |
| v2-4 | 20.0% | 20.0% | 8.60 | 8.60 |
| v2-6 | 20.0% | 20.0% | 9.93 | 9.93 |
| v2-8 | 3.3% | 3.3% | 8.07 | 8.00 |

总胜率相同不代表逐局完全相同：v2-8 对机械有 2 局胜者/回合差异，聚合胜率恰好抵消。已有正附加惩罚时负修正仍可产生作用。每池有 5 局峰值封印生命达 512；它是进度指标，不能不看 `reason` 就当成 5 次成长胜利。

此表是驱动可运行的小批次证据，不能冻结平衡。当前预构筑与实验构筑仍采用 60 主牌 +统领，尚未迁移到正式 59+1；密度实验还包含较多同名廉价牌副本，且只用默认策略。Jev 不会自动补足这些实验设计限制。旧的“1 点惩罚≈30 个百分点”未获得本次验证支持。

## DS 可直接执行的命令

在仓库根目录先重新编译，再给每次运行一个新输出目录：

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File build-output\dynamic\build-probe.ps1

dotnet build-output\dynamic\probe\WoodPoolProbe.dll build-output\dynamic\pool_v2_dyn build-output\dynamic\decks_v2 60 1 --seed 1 --log-decisions --log-limit 100000 --out build-output\dynamic\runs\qa-v2-dynamic-01

dotnet build-output\dynamic\probe\WoodPoolProbe.dll build-output\dynamic\pool_v2_static build-output\dynamic\decks_v2_static 60 1 --seed 1 --log-decisions --log-limit 100000 --out build-output\dynamic\runs\qa-v2-static-01
```

要验证两座位，加 `--both-seats`。先检查进程退出码和 `summary.json` 的 `validation_ok` / `invalid_games`，再读胜率；同时检查 `matches.jsonl` 的胜利原因。日志超过上限时 `decision_rows_omitted` 必须如实保留，不能声称完整决策采集。

Jev 离线脚本现在可以传入：

```powershell
powershell.exe -NoProfile -File build-output\dynamic\jev-score-decisions.ps1 -DecisionFile build-output\dynamic\runs\qa-v2-dynamic-01\decisions.jsonl -Take 12 -Actor 0
```

同卡池协议对比的两个入口命令（两边需采用同样的 `--playstyle`、T1 与 `--seed`）：

```powershell
dotnet build-output\dynamic\probe\WoodPoolProbe.dll data\cards data\decks 20 1 --seed 1 --both-seats --out build-output\dynamic\runs\qa-parity-probe-01

dotnet build-output\dynamic\probe\PlCsim.dll --cards-dir data\cards --decks-dir data\decks --games 20 --seed 1 --playstyle default --max-punish-responses-per-round 1 --match-log build-output\dynamic\runs\qa-parity-reference-01\matches.jsonl --report C:\Users\USER\Documents\dominion-wars-win64\build-output\dynamic\runs\qa-parity-reference-01\report.json
```

按 `(leader0, leader1, seed, first_player)` 匹配涉及古木的记录，不能把不同对阵集合的全场平均值直接相减。参考入口的 `--match-log` 为追加式，复核时也应使用新路径。

## 证据位置与边界

全部证据在 `build-output/codex-probe-repair-20261004/`：

- `before/`、`before-manifest.json`、`before-binaries/`：接手前源码和二进制备份。
- `final-parity-probe/`、`final-parity-reference/`、`final-verification.json`：最终 120/240 局对照及数据漂移检查。
- `parity-probe/decisions.jsonl`、`submissions.jsonl`：全量匹配决策和弃牌证据；最终核准版本对局结果与该批完全一致。
- `v2-dynamic/`、`v2-static/`：各 150 局小批次；记录自己的驱动哈希。其后仅修改了驱动说明注释和未使用的兼容 CLI 参数，最终匹配复核通过。
- `test-results/probe-repair-full.trx`、`driver-tests-console.txt`、`project-build-console.txt`：回归和驱动专项证据。
- `validator-valid.txt`、`validator-invalid.txt`、`validator-v2.txt`：Java 门禁正反例。

修复的是 C# 实验运行路径；没有验证 Java/C# 全量规则一致性、真人体验或最终卡牌平衡。没有改费用钳制、卡牌数值、规则文案，没有提交/推送，也没有声称 DS 已经独立执行验收。脚手架源码位于 Git 忽略的 `build-output/` 中，当前已保存在本机和备份中；需另行纳入版本管理时应只收录经审核的源文件，不把日志、二进制或全部脏工作区一起提交。
