# 构建与测量纪律（2026-09-11 夜班实录）

> **本文不是设计文档，是"今晚真实踩过的坑"清单。** 每条都附带当晚的实际事故。
> 适用对象：任何在本仓跑构建、跑测试、跑模拟的人（Codex、QA、PL、子代理）。

---

## 1. ⚠️ 沙箱环境的两条硬性要求

| 要求 | 原因（实测） |
|---|---|
| **`dotnet build` 必须加 `-m:1 --no-restore`** | 不加 `-m:1` 时，MSBuild 并行节点在本沙箱**无法使用命名管道**，于是它以 **"0 个警告 0 个错误"** 的形式**失败**（`Build FAILED` 但错误计数为 0）。这是最容易被误读成成功的一种失败。 |
| **不要运行 `dotnet restore`**（必要时加 `/p:NuGetAudit=false`） | 裸调用因 `NU1900`（网络审计不可达）失败；离线场景下加 `NuGetAudit=false` 可成功。 |
| **`dotnet test` 在本沙箱被阻断** | testhost 抛 `System.ComponentModel.Win32Exception (5)` @ `ProcessManager.OpenProcess`（进程访问被拒）。**这是策略拒绝、不是代码缺陷**；切勿据此判断"测试写错了"。替代路径：进程内 NUnit runner（见 §4）。 |

## 2. ⚠️ 头号坑：**过期产物冒充新结果**（今晚发生 **3 次**）

**症状**：测试跑出"绿"或"红"，但那个结果属于**上一版源码**。

**三次实录**：
1. 某代理用 `Copy-Item` 覆盖文件时**保留了源文件时间戳** ⇒ MSBuild 认为无需重编译 ⇒ 跑的是旧 DLL，产出**作废的**红/绿。（该代理自己发现并标注 `superseded-INVALID-stale-binary-*.xml`，做法正确。）
2. 另一代理把**当前版本**的 `DominionWars.Adapters.dll` 与**冻结版**的 `DominionWars.Engine.dll` 混配引用 ⇒ 报告写着"冻结引擎"，实际 AI 来自新版本。
3. PL 复跑时看到 1 条失败（753/754），**在报出去之前对了时间戳**，发现测试 DLL（22:58:58）比源文件（22:59:18）**旧** ⇒ 重新编译后 754/754 全绿。**差一点冤枉了一个其实已经修好的代理。**

**规则（请照做）**：
- **取数之前**，先比 **`源文件 mtime` vs `产物 mtime`**；源更新则**必须先重编**。
- 每轮开工先 `dotnet build-server shutdown`。遗留的 build server 会让构建以
  **`ReplaceFileW EIO (Win32 1175)`** 失败，而中文日志里它很容易被读成成功。
- 看构建输出时**显式搜 `CS####`**，不要只看"0 个错误"这行。
- 报告里**盖产物哈希**（引擎/适配层各一个），而不是只写"用的是新版本"。

## 3. ⚠️ 测量纪律：**混口径**是本项目最大的假结论来源

今晚有 **5 条**结论在事后被自己的数据推翻，全部因为口径不清或口径不对：

| 假结论 | 真因 |
|---|---|
| 「机械 −31.2pp 回归」 | 读数来自 **Java 引擎**，而 Java 当时**不认识机械的提交/上传/下载与古木增幅** |
| 「S1：可降临密度 ≤30% 能修深海」 | 只有 **−7.8pts** 且非线性；真实机制是"四阵营共同产出洪流 + 深海负责转换" |
| 「古木弱是因为增幅即封印」 | 消融：去掉封印只从 10.00% → **11.11%**，不是主因 |
| 「T1 打开后对局变成 15.35 回合」 | 那是**策略层仿真**；引擎规则实测是 **12.60**，仿真把效果**高估 29%** |
| 「发行版正被惩罚洪流压成 6 回合」 | 那是 harness 注入"接受一切"的口径；**发行路径默认放弃全部响应** |

**规则**：
- 每份报告必须能回答 **"这份数字是哪个引擎、哪个策略、哪个规则配置、哪份卡数据跑出来的"**。缺一项即视为无效报告。
- **单套策略 = 单点，不是结论。** 本项目已实测：只改 AI 行为就能让机械从 **0.0% 摆到 95.7%**。跨策略只能引用**区间**，同策略内才能引用**差值**。
- **"策略拒绝"不能用来仿真"规则不提供"** —— 会系统性高估（实测 −29%）。

## 4. 唯一可用的测试路径（本沙箱）

```
dotnet build-server shutdown
dotnet build DominionWars.sln -c Release --no-restore /p:MSBuildEnableWorkloadResolver=false -m:1
cd build-output\pl-p0\runner\bin\Release\net8.0
dotnet PlP0Runner.dll "<repo>\build-output\DominionWars.Engine.Tests\bin\Release\net8.0\DominionWars.Engine.Tests.dll" --xml "<out.xml>"
# 单类： --filter '<filter><class>DominionWars.Engine.Tests.XxxTests</class></filter>'
```
它用**真实的 NUnit 引擎 + 真实编译产物**，但**不是 vstest**：`[TestCaseSource]` 之类行为与官方 runner 可能不完全一致（曾出现"537 vs 603"的缺口，即由忽略 `TestCaseSource` 造成）。**因此它不能替代官方 `dotnet test`** —— 请在有完整权限的机器上补跑一次官方门禁。

## 5. 其它环境事实

- `git` 命令在本会话默认失败（`error: missing config key GIT_CONFIG_KEY_0`）。清掉 `GIT_CONFIG_COUNT` / `GIT_CONFIG_VALUE_0` / `GIT_CONFIG_VALUE_1` 即可正常使用。
- NUnit runner 会在**仓库根**留下 `InternalTrace.*.log`（未被 git 跟踪）。PL 已清过一次（21 个）；建议加进 `.gitignore`。
- Unity 程序集在本环境**无许可证、编译不了** ⇒ 任何 Unity 侧改动只有**人工核对**，需在装有 Unity 的机器上跑 EditMode/PlayMode 才算验收。

— PL（DeepSeek V4 Flash harness）· 2026-09-11
