# B2-C 平衡与牌组采纳验证（PARTIAL）

日期：2026-10-08
状态：**PARTIAL。B2-B/F 实施、fresh 回归、同输入跨端探针、post-F 机器对局及古木两臂已实测；威胁估算器校准已运行但有独立会计注记不一致；原始 10/4 B1 未恢复，D00 只能称候选，正式卡组选择校准未完成。本文不作最终平衡采纳或规则验收结论。**

## 范围与结论边界

本次先冻结并运行同一版 pre-F 程序，测量机械遗迹对烈焰、深海、古木三套正式牌组的对局。它不是四阵营全矩阵，不包含 neutral 作为独立牌组，也不能外推为所有 matchup 的胜率或平衡结论。这里的 120 局构成本批 B2-C 的 pre-F 对照；已按下文用冻结 post-F 程序复跑同一组完整键。

旧古木 34.2%/14.2%/32.5% 与旧 Java 71.7% 等结果，以及中间 pre-full B2 实验数字均作废，不作为本报告数据，也不与下文冻结结果拼接；相关原因是旧 source/data/T1 lineage 不满足本轮冻结条件。

独立的出牌源区域归属修复是这轮测量的前置条件，不属于 B2-C 平衡改动：已接受的牌在惩罚响应前先离开手牌；若响应窗口内终局，未结算的源牌只进入原拥有者墓地一次。修复同步到 C# 与 Java，未改卡值、惩罚值、AI 权重或规则。历史污染版本的数据未并入本次基线。

## 前置回归与源冻结

- .NET Release fresh build/test：**936/936 passed，0 failed、0 skipped，exit 0**。与 2026-10-07 的完整 TRX 按 `testName` 多重集比较：新增 0、删除 0。原始结果为 `build-output/checkpoint-20261008/b2c/preF/validation/trx/source-ownership-full-20261008.trx`，控制台为同目录 `dotnet-console.txt`。命令：

  ```powershell
  dotnet test DominionWars.sln --nologo --no-restore -c Release /p:MSBuildEnableWorkloadResolver=false --logger "trx;LogFileName=source-ownership-full-20261008.trx" --results-directory build-output/checkpoint-20261008/b2c/preF/validation/trx
  ```

- Java fresh `scripts\build.bat`：exit 0；fresh `com.dominionwars.test.TestMain`：**89/89，exit 0**。stdout、stderr、退出码分文件保存在 `build-output/checkpoint-20261008/b2c/preF/validation/java/`。stderr 中 5 行为 missing/corrupt/invalid balance fixture 的预期 fallback 诊断，不是失败。
- pre-F 编译清单核了 149 个生产源文件，hash mismatch 0。构建基于 HEAD `36177b4610efed726eb6958125e9ffb7edb67709`，另含下列六个当时尚未提交、之后以相同内容归档到 `b26a744e2ec1ae5c0c3bc0cdcc6932c84da9e7a9` 的源归属文件；构建与运行前后 SHA-256 均匹配：

| 文件 | SHA-256 |
|---|---|
| `src/Engine/Turns/PlayCardActionHandler.cs` | `7905d8e97960a067b28838342a18ef1a5afd9e53f3acd734d96c6b039c58011a` |
| `src/Engine/Tests/PlayCardPunishSourceOwnershipTests.cs` | `692a3c86fdf4b4ff9a61fe1819aaba276856f8651f0f6d80d228f157fd2c0e40` |
| `src/Engine/Tests/PlayCardPunishSourceOwnershipTests.cs.meta` | `e106cb25fdcc6e52cf17a37c4d1ab1764169de127e541c11c3e14f6d9a9f5db8` |
| `src/main/java/com/dominionwars/engine/Game.java` | `f6005fb0dd68a522112d8b3bd761af7899f53cd6dfe362924705803393a31736` |
| `src/test/java/com/dominionwars/test/TestMain.java` | `84fc5595c8f3e652a71a513fd59ad9f20f1fd14e78c5441b01362fb8c7eff204` |
| `src/test/java/com/dominionwars/test/PlaySourceOwnershipTests.java` | `c13c30085b9898102e00f350125bfb43e03e6206c5650c93c338ab730a0682c8` |

## pre-F 程序与输入指纹

构建输出位于 `build-output/checkpoint-20261008/b2c/preF/`。程序程序集均在该独立输出目录内新鲜构建；没有从 10/5 的旧输出复制 DLL。`driver/SOURCE_REVISION.json` SHA-256 为 `9af6e5dbbba4eef07b26c0236d673b973d55e7ae2a7ecf08955998c43ed67aa0`（149 源文件、0 不匹配）；`driver/PINNED_REVISION.json` SHA-256 为 `880204cc4a8a9bb8404db3981f2be684eb77375d2a071a72724706fb8ae2958f`。完整路径、版本、输入及派生核验另见 `freeze-manifest.json` 与 `validation/pref-machine-120-integrity.json`。

| DLL | SHA-256 |
|---|---|
| `driver/DominionWars.Engine.dll` | `36de52f633fa5c7769d18c1553e2fed7062fa699c6804836ff5ca1bfec3aaddd` |
| `driver/DominionWars.Data.dll` | `d7bf8f8c0b8889ebdf86fdd441c5d6e2507b60044299dc38624f17a175f66911` |
| `driver/DominionWars.Adapters.dll` | `160df8fc35c188d01052de0fc72d5a72c7027007d7c8997fe76240d2df610ccf` |
| `driver/PlCsim.dll` | `11c34f177dd4be6e5830b65d757f6dd260168344469e0583dd75af163d334929` |
| `driver/Newtonsoft.Json.dll` | `22c649f75fce5be7c7ccda8880473b634ef69ecf33f5d1ab8ad892caf47d5a07` |
| `growth/WoodPoolProbe.dll` | `343fd562efeae22c7af20d5e5bd41d2f50aeeb2e389b6fded1202383d8a44908` |

独立 growth probe 已新鲜构建并冻结，但没有参与这 120 局机器对局。

十个原始 JSON（五张卡池、四套牌组、balance）逐一复制到 `preF/inputs/`，副本 SHA-256 与构建冻结记录 **10/10 相同**；`docs/RULES.md` 副本也匹配，hash 为 `ceff332c2eac26c2b38f93f746c6c6e730b9f67b943c707a59486b3d070d2902`。`freeze-manifest.json` 保存各文件逐项 hash。三套 pair fixture 从这些副本再复制六份牌组 JSON，fixture hash **6/6 匹配**。

| frozen JSON | SHA-256 |
|---|---|
| `data/cards/flame.json` | `c5c1f4faf61283dc7e52aad9b5c844f4d25bea5c8df1184fac704b4341470325` |
| `data/cards/machine.json` | `501e10347467c65f099c34b4bc88516cf6e06e0c62d56dfe1074773425854023` |
| `data/cards/neutral.json` | `cae381377fbd1acd542e5dfdd672cccb15025bc69fb9391a95af75e310f64eed` |
| `data/cards/sea.json` | `4c6d41a8abe6156be9dc0a7c8e0eab47a8c4b65c5b6dacf1b9414ad894a59172` |
| `data/cards/wood.json` | `1560d64fc7d6f120137a246394860e2ca6c7ff38e5c69eef169d910cde3c8253` |
| `data/decks/flame_deck.json` | `d8251553a0817056158a4d229d8c545aebed4d560b4f5266e226fe9243dfc087` |
| `data/decks/machine_deck.json` | `158c05da4c57ba164e05b2f55f5503a0cd3ec486db375082aefe24ffb19c1151` |
| `data/decks/sea_deck.json` | `e2f9d4b42b419c371ec902400348ee78c5735cb43e32bbb155e003fb1d28fafe` |
| `data/decks/wood_deck.json` | `a8902b9f431955de5078d1d3906195dd8f15363844909bbdf61324e0785c95a1` |
| `data/balance.json` | `b51043f6e41dfc0e72413f21b3ba9d9fd416b1b8bddb843e5ee39e842799b359` |

注意：被冻结的 `balance.json` 中 `maxPunishResponsesPerRound=1`，但 Csim 报告明确标记 `balanceJsonRead=false`。所以本轮通过 CLI `--max-punish-responses-per-round 1` 显式传入相同的引擎规则 cap，报告 `t1Mechanism=engine-rule`、`engineCapApplied=1`；本报告不声称模拟器从 balance 文件加载了 T1。

## pre-F 机器对局：120 局

运行方法是三次隔离的双牌组 Csim，而不是把四牌组全矩阵聚合后错误地除以 120。每组 20 局/有序牌组对、两种牌组顺序各 20 局，共 40 局；只涉及 `machine` 与 `flame`、`sea`、`wood` 正式牌组。Csim 使用同一冻结 `driver/PlCsim.dll`，`--cards-dir` 明确指向 `preF/inputs/data/cards`，`--decks-dir` 指向 `preF/fixtures/<pair>`。CLI：

```text
--games 20 --seed 1 --turn-cap 200 --playstyle default --max-punish-responses-per-round 1
```

种子按驱动公式核验：`SeedFor(a,b,gameIndex,1) = 1 + gameIndex*7919 + a*104729 + b*1299709`；每组中 deck 文件名排序决定 `a,b` 为 `0/1`，`first_player=gameIndex%2`。逐局记录以 `(seed, first_player, player0, leader0, player1, leader1)` 核验：**120 个键唯一，重复 0**；六个有序牌组方向各 20 局、每向先后手各 10 局，种子序号及先手奇偶错误均为 0。逐键原始明细：`validation/pref-machine-120-game-keys.json`，汇总与逐项输入核验：`validation/pref-machine-120-integrity.json`。

| 对局 | 机械胜 | 对手胜 | 平均回合 | 全场胜因 | 惩罚初始抽牌 / 响应抽牌 |
|---|---:|---:|---:|---|---:|
| 机械 vs 烈焰 | 18/40 (45.0%) | 22/40 | 6.6500 | `win.pull_total_ge` 18；`win.enemy_leader_defeated` 21；`win.royal_castle_break` 1 | 4,967 / 0 |
| 机械 vs 深海 | 17/40 (42.5%) | 23/40 | 5.3250 | `win.pull_total_ge` 17；`win.opp_discard_total_ge` 16；`win.enemy_leader_defeated` 7 | 4,075 / 0 |
| 机械 vs 古木 | 8/40 (20.0%) | 32/40 | 5.5500 | `win.pull_total_ge` 8；`win.giant_health_ge` 31；`win.enemy_leader_defeated` 1 | 4,365 / 0 |
| **合计** | **43/120 (35.8%)** | **77/120** | **5.8417** | 三组均已按全场获胜方计数 | **13,407 / 0** |

120 局均为 valid 且 decided；invalid、封顶、异常、拒绝、弃牌违规均为 0。默认策略报告的惩罚响应立场是 `never`，故 `punishDrawResponse=0`。`punishDrawInitial=13,407` 是该驱动对**双方合计**的初始惩罚抽牌统计；111.725 是其除以 120 次尝试所得均值。它不是机械方自身收到的抽牌，也不能解释为机械出牌单独引起的抽牌量或每张牌效果归因。

各组单独保存 `console.txt`、`stderr.txt`、`exit.txt`、`matches.jsonl`、JSON 汇总和 usage CSV 于 `build-output/checkpoint-20261008/b2c/preF/runs/<pair>/`。三组 exit 均为 0，stderr 为空。报告确认 `pinnedPairVerified=true`，运行使用上表的隔离 Engine/Adapters hash。运行记录还提示仓库另一个 `build-output/DominionWars.Adapters` DLL hash 与 pinned Adapters 不同；本组数字只对应 pinned revision，未以那个漂移 DLL 替代冻结副本。

## Post-F 源版本、回归与跨端探针

根协调者确认以下三个提交已在当前分支远端：B2-A loader 修复 `36177b4610efed726eb6958125e9ffb7edb67709`；出牌源归属修复 `b26a744e2ec1ae5c0c3bc0cdcc6932c84da9e7a9`；B2-B/F `31c8f46c51d2875dabaff294011c65c3f8567850`。出牌源归属是 pre-F 与 post-F 测量的共同前置修复，不是本次卡值或平衡调整。

- F 后 fresh .NET Release 回归：**938/938 通过，0 failed、0 skipped，exit 0**。原始 TRX：`build-output/checkpoint-20261008/b2b/postF/dotnet/postf-full-dotnet-r2.trx`；控制台和退出码：同目录 `full-dotnet-console-r2.txt`、`full-dotnet-exit-r2.txt`。
- Java fresh compile + `TestMain`：**93/93，exit 0**。原始 stdout/退出码：`build-output/checkpoint-20261008/b2b/postF/java/test-main.txt`、`test-main-exit.txt`；编译退出码分别在 `javac-main-exit.txt`、`javac-test-exit.txt`。
- 一次 .NET 启动尝试误带 MSBuild 不支持的 `--no-incremental`，原始控制台报 `MSB1001: 未知开关`，VSTest 未启动且没有 TRX；它是 0 测试的命令行错误，不计为源码失败。其后按 fresh 命令重跑得到上述 938/938。
- Fresh post-F pinned assembly：Engine `81D441186B3CEB56F9A9A1E003987B4D296286554B3C4EBF4915B56BCE9AE171`，Data `D7BF8F8C0B8889EBDF86FDD441C5D6E2507B60044299DC38624F17A175F66911`，Adapters `160DF8FC35C188D01052DE0FC72D5A72C7027007D7C8997FE76240D2DF610CCF`。冻结清单及其余 DLL/hash 位于 `build-output/b2b-20261008/lunar/freeze-manifest.txt`。
- 独立 C#/Java same-input 探针 raw：`build-output/b2b-20261008/lunar/crossfixture/csharp/trace.json` 与 `java-trace.json`。同一 cost-0 惩罚响应牌夹具在 not-Alpha → Alpha active → Alpha removed 三态中得到 fee、惩罚抽牌、响应回调 `[1,0,1]`，每态实际 PULL `+1`，**被下载的云端栈顶牌进入墓地，机械载体仍留场**；两端均到达相同输出。此 scratch fixture 是手动切换 Alpha 场上状态，**不冒充实际统领晋升**；实际晋升另由正式 `ProductionFactionIntegrationTests` 回归覆盖。正式测试及 Java Web 正费用响应路径回归通过，但随机对局并未导出 Alpha-active PULL 遥测。

## Post-F 机械遗迹：同一 120 个键

post-F 与 pre-F 使用相同的 120 个完整逐局键，键集合完全相同，唯一 120、重复 0。每对手 40 局：机械占 player0/player1 各 20 局，先后手各 20 局；每对手原始 match 行实算 distinct seed 为 **40**，不是按每臂种子数误报 20。post-F 汇总：`build-output/checkpoint-20261008/b2b/postF-sim/validation/postf-machine-120-summary.json`；三组原始 match JSONL 在同目录 `runs/postf-r2-machine-{flame,sea,wood}/matches.jsonl`。每组 exit 0，120/120 valid、decided；invalid、cap、异常、拒绝均为 0。

| 对手 | 机械胜 | 平均回合 | 全场胜因（不只机械方） | 双方合计初始惩罚抽牌 |
|---|---:|---:|---|---:|
| 烈焰 | 18/40 | 6.650 | PULL 18；敌方统领被击败 21；王城破坏 1 | 4,967 |
| 深海 | 17/40 | 5.325 | PULL 17；对手弃牌总数 16；敌方统领被击败 7 | 4,075 |
| 古木 | 8/40 | 5.550 | PULL 8；巨人生命 31；敌方统领被击败 1 | 4,365 |
| **合计** | **43/120 (35.8%)** | **5.842** | PULL 43；弃牌 16；巨人生命 31；统领 29；王城 1 | **13,407** |

默认策略为 `never`，响应惩罚抽牌为 0。13,407/120 = 111.725 是每局**双方合计**初始惩罚抽牌，既不是机械方单独收到，也不是机械主动动作归因。机器 pre/post 的胜率、回合和原因同值；这说明此默认样本未暴露 F 的效果，不证明免费 PULL 规则无效：该随机对局输出没有 Alpha-active 时点和 PULL 费用/尝试数。F 的规则路径由上述同输入跨端探针及正式集成回归验证，模拟样本不能量化受益幅度。

## Post-F 古木生产版 vs 可追溯 D00 候选

两臂均为 120/120 attempted、observed、valid，invalid 0、缺观测 0；同一完整键归一化（只把木牌组版本标签规范化）后两臂键集合完全相同，每臂唯一 120、重复 0。**配对矩阵为 20 seed indices × 2 个木席位 × 3 个正式对手（烈焰/机械/深海）= 每臂 120 局；每一“对手 × 木席位”单元 first-player parity 10/10。**每个对手 40 局，木席位各 20，木先/后各 20。neutral 是公共牌池，不作为对手牌组。

| 木卡池/牌组 | 木胜 | 平均回合 | 达到 512 的观测 / 巨人胜因 | 无非诊断事件代理 | 增幅 BUFF / 实际木方回合 |
|---|---:|---:|---:|---:|---:|
| 当前生产版 | 90/120 (75.0%) | 5.725 | 512：89；全场胜因：巨人生命 89、统领 6、PULL 14、弃牌 11 | 228/2,266 = 10.06%（非吟唱代理 111/2,266 = 4.90%） | 949/360 = 2.636 |
| 冻结 D00 B1 候选 | 68/120 (56.7%) | 8.283 | 512：59；全场胜因：巨人生命 59、统领 27、PULL 25、弃牌 9 | 138/2,829 = 4.88% | 749/504 = 1.486 |

表中胜因是 120 局的**全场胜局原因**（胜方可能是任一方），不是仅统计木方获胜；四类原因分别合计 120。

“无非诊断事件”仅是粗略的出牌无效代理，不等于逐卡效果归因；空效果审计事件两臂均为 0。记录到仍付满惩罚的代理牌分别为 151 和 53 次。增幅 BUFF 以 `d_growthApplied=true && amount!=baseAmount` 计数；事件没有来源字段，包含玩家牌、统领/被动等成长触发，不能写成手动出牌 BUFF 数。目标拥有者统计为木侧 949/749、对手 0、中立/未归属 0。

D00 候选不是原 10/4 B1：JSON 中 `historicalOriginalB1_20261004Recovered=false`，原始 8d 指纹仍未恢复。本轮可追溯 D00 卡池/牌组 hash 分别为 `D00CD12AF232CC0E9224DE13080B8D518D5255F64DFCD6A2C1827467E71F1C08` 与 `D14712337AFA96D90F11EE78F817B13A1834D38D5B28583072DAA6E1C7C41DAC`；它来自早前 post-P0-1 冻结候选，报告只称候选，不追称原始 B1。当前生产木牌组为 60 张主牌、21 个木定义；D00 候选是 50 张主牌、31 个木定义。两者都不满足 59+统领的标准输入，因此虽然逐局匹配，结果仍有构筑大小/牌池差异，**不构成公平 B1 采纳结论**。

分析脚本最初按 seed 单独分组，把相同 seed 的不同对手/席位混在一起；旧输出原样保留但不采用。仅分析工具修正为按事件可用的唯一 `(seed, player0, player1)` 游戏键聚合，再以游戏账本完整键验证唯一性。正式采用的日志为 `validation/wood-prod-postf-120-analysis-fullkey-r2.txt` 与 `wood-b1d00-postf-120-analysis-fullkey-r2.txt`，二者 exit 0，BUFF 算术/封印检查 mismatch 0；旧 seed-only 日志在同目录 `wood-{prod,b1d00}-postf-120-analysis.txt` 明确保留为旧过程。合成测试用同 seed、不同对手/席位两局证明分组隔离，exit 0：`validation/analyzer-fullkey-synthetic.txt`。修正及两臂逐局汇总：`validation/wood-postf-paired-120-summary.json`（SHA-256 `83C58D9BAC263DFB77CCE01F59DCBA9DBAAC6F843C0649B7FD8BEDEA2075CF97`）。汇总中 `plCsimDriverSha256=11C34F…` 标识对局驱动；`woodGrowthProbeSha256=343FD5…` 才是观测器，不再混称一个 probe hash。

路径卫生补充：`build-output/wood-design-2026-10-04/analyze-timeline.ps1` 是第一批 checkpoint 已精确归档的手写分析源文件，虽然位于通常忽略的目录内，实际仍受 Git 跟踪。本轮保留并归档这份窄统计修复；生成的 DLL、JSONL、日志和 scratch 项目仍被忽略，不随文档提交。未修改游戏生产代码、卡值或原始对局数据。

## 威胁估算器实测与未完成的牌组校准

沿用现有 `build-output/pl-threat-calib` 入口，将 scratch 项目单独绑定到本次 post-F Engine/Data/Adapters 与 `env-prod` 冻结数据副本；没有运行旧目录中的校准 DLL。命令为：

```powershell
dotnet build build-output/checkpoint-20261008/b2b/postF-sim/env-prod/runner/threat-calib/PlThreatCalib.Scratch.csproj -c Release --no-restore -m:1 /p:MSBuildEnableWorkloadResolver=false
dotnet build-output/checkpoint-20261008/b2b/postF-sim/env-prod/runner/threat-calib/bin/Release/net8.0/PlThreatCalib.dll --seeds 8 --turn-cap 40
```

构建和实际运行均 exit 0；runner SHA `2CFBB51C8E4CEDC4DA7310B6160257BD95A11911D547A653E09F7FE36CA7D9EB`，scratch csproj SHA `6500CDF51170CBF02E791444F2555B9061B98EA680C23E2CCAB65B87D00B7A3F`，估算器源 SHA `B556AFFF5063BA2C7E8EF946D8117C2E017D506A6500020B151DEFEBB8C8C7CC`，入口源 SHA `8A50FD34730332AFD8B15B7FDB5F708CC6257D2B1DD8B38A495A4CD5AB377F98`。编译输出有 120 条 CS0436 duplicate-type 警告、0 error；scratch 项目显式编译当前 `ThreatEstimator.cs` 并引用冻结 Adapters 中其余类型，因此编译器选本地源定义。运行 raw 和 exit 分别是 `validation/postf-threat-calib-seeds8-turncap40.txt`、`.exit.txt`；数据为四套正式牌组、每有序对手组合 8 seeds、turn cap 40，共 5,120 samples、matches threw 0。

跨阵营 3,840 samples：ECE 0.0265，MCE 0.0871，Brier 0.1141，平均绝对概率误差 0.0474，整体 bias −0.0259（低估持有概率）；最差类别 Negation，预测 0.611、观测 0.791、绝对误差 0.1807。覆盖缺失 0。需要保留一个工具输出限制：同一报告还写 `estimator's own accounting notes are clean: 0/5120`，而它单独的 `AccountedCopies + VisibleCopiesInPool == declared pool` 公共计数校验写 `inconsistent: 0/5120`。这两个计数采用不同判据，不能合并成“会计全部通过”；校准实测有用但记为 **PARTIAL**，会计注记差异需后续独立解释。本次不改估算器。

该测量不是卡组选择校准。旧 `DeckCompare.java` 依赖历史 10-vs-9 牌组集合，不等于当前已授权且冻结的 59+统领候选输入；本轮没有合适的批准候选集，因此**牌组选择校准未运行/未通过验收**。旧模拟结果不复用。

## 最终边界与后续项

- B2-B/F 代码与完整 .NET/Java 回归、同输入三态 C#/Java 探针已验证；正式统领晋升测试与 Java Web 正费用 pending/响应正对照由测试覆盖。没有 Unity、原生鼠标或完整产品 UI 验收证据，本报告不声称通过这些层。
- post-F 机械 120 局与其 pre-F 完全同键，但随机样本没有 Alpha-active PULL 观测字段；故不量化免费 PULL 的实际利用率或胜率因果影响。
- 古木 prod vs D00 配对结果只可读为这两个冻结输入在此矩阵下的观察差异。原 10/4 B1 仍阻塞，D00 输入大小不标准，当前生产输入也为 60 主牌；不能据此决定采纳。
- 此处三对手矩阵不是四阵营全矩阵；威胁估算器实测不是卡组选择实测。卡组选择校准仍待明确、可追溯的正式候选输入。
- F24 映射 5/16 与 P0-3 接线是另外的待办，本批未修复或验收。最终规则/平衡/卡组采纳需 PL 与 owner 审核；本报告不把任何数值观察升级为新规则或卡值决策。
