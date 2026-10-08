# 交接给 Codex：代码级审核 4 项缺陷（现象 → 根因 → 解决假设）

**日期**：2026-10-02
**来源**：临时 PL（DeepSeek V4 Flash）· 依据 `docs/PL_AUDIT_2026-10-02.md` §8
**状态**：**待 Codex 接手上线后判断**。其中 **CR-1 / CR-3 需要 owner 先裁定**（见各节「决策人」）。
**为什么写这份**：Codex 额度到期。本文把每个问题的**现象、根因、解决假设（含取舍）、验收标准、决策人**一次写全，
使 Codex 上线后**不必重新取证**即可直接判断该不该改、怎么改、改到哪一步为止。

---

## 零、优先级与阅读顺序

| ID | 缺陷 | 严重度 | 决策人 | 建议次序 |
|---|---|---|---|---|
| **CR-1** | T1 规则只有 C# 有，Java 完全没有 | 🟠 中（**影响面最大**） | **owner** | 第 1 |
| **CR-2** | `balance.json` 15 键，C# 只认 5 | 🟠 中高 | owner（范围）/ Codex（文档与守卫） | 第 2 |
| **CR-3** | 回退诊断消息不实 | 🟡 中 | **owner**（回退语义）/ Codex（措辞） | 第 3 |
| **CR-4** | 两处注释与出厂数据矛盾 | ⚪ 低 | Codex | 第 4 |

> **重要前置**：归档（`HEAD` 21.7 天未动、工作区 444 项）**优先于本文件全部内容**。
> 本文件不改变「先修 F36 → 再完整同批归档」的次序。

---

## 一、CR-1：T1（`maxPunishResponsesPerRound`）只有 C# 实现，Java 完全没有

### 1.1 现象

owner 于 2026-09-11 决定「一轮限 1 张反制响应」。该规则在 C# 已完整实现并在实机生效；
在 **Java 引擎中不存在**。

### 1.2 证据（可复现）

- `Get-ChildItem src\main\java -Recurse -Filter *.java | Select-String 'maxPunishResponsesPerRound'` → **0 命中**。
- `src\main\java\com\dominionwars\data\Balance.java` 的 `apply()` 与 `toMap()` **均无此键**（已逐行核对，共 15 个键，不含它）。
- C# 侧生效链：`data/balance.json` = `1` → `unity/.../RuntimeBootstrap.cs:230 Rules = BalanceTable.LoadRules()` → `PlayCardActionHandler.cs` 的 `PunishRound`。

### 1.3 根因（为什么没被早发现）

1. **T1 是单端决策**：9/11 只落在 C#（当时 Java 侧未同步）。
2. **9/24 的 Java parity 切片把范围写死**：`docs/DAILY_GOAL.md` 明确列出该批只做「洗牌归属 / 破城顺序 / PUSH / PULL-ROLLBACK 原子性 / 数字解析 / ENFEEBLE-BANISH-CONTROL」，**T1 不在其中**。
3. **两端各自都有测试，但没有跨端对照测试**。`docs/PL_JAVA_CSHARP_PARITY_GAP_2026-09-13.md` 自己已经指出：
   全库 grep `Java`/`parity`/双引擎 → **0 命中**。所以「Java 缺一条规则」在结构上**不可能被现有测试发现**。

### 1.4 影响（这是本条被列为最高的原因）

**所有平衡结论都是用 Java 跑的**——即在「反制不设上限」的规则下测得：

- `docs/PL_FINAL_DECK_2026-09-24.md` 自述「**数据：Java 引擎，60 局/卡组**」，结论「古木胜率 71.7%、加权平均惩罚 1.35」。
- 480 局选卡组、威胁估计器校准、M-1 机械阈值提案，同样来自 Java 实测输出。

而 T1 想治的正是「反制链过强」。**用不设上限的引擎去验证一个设上限的规则，结论方向会系统性偏。**

### 1.5 解决假设

| 方案 | 做法 | 取舍 |
|---|---|---|
| **A（PL 推荐）** | 在 Java 补齐 T1：`Balance.maxPunishResponsesPerRound`（默认 1）+ `Game`/惩罚链处应用「同一根动作事件内响应额度用尽即抑制」 | 与 C# 语义对齐；**代价**：Java 行为改变，**既有 Java 实测数字全部失效，需重跑** |
| B | 宣布 **C# 为平衡基准端**，把所有实测迁到 C# | 不必改 Java；**代价**：现有 Java 工具链与历史数据作废，迁移成本高 |
| C | 宣布 Java 为基准，即**回退 owner 的 T1 决策** | 不推荐：等于否定 9/11 决策 |

**PL 建议：A。** 理由：现有全部平衡工具链（`SimMain`、卡组实测脚本）都是 Java 的，A 的迁移成本最低，且与 C# 已有实现可直接对照。
**注意**：A 属于「把**已决定**的规则补到另一个引擎」，**不是新的规则决策**；但它改变 Java 行为并作废既有实测，**故仍需 owner 一句确认**。

### 1.6 验收标准

1. Java 与 C# 在**同一 fixture** 下对「一轮反制次数」给出相同结果（建议照 9/24 的 E1–E7 fixture 风格，加 `T1` 组）。
2. Java `TestMain` 全绿（当前 79/79），且新增 T1 定向用例。
3. 用 `maxPunishResponsesPerRound = 0` 与 `= 1` 各跑一次同一 seed 的 SimMain，证明**结果确实不同**（证明规则真的生效，而不是只编译通过）。
4. 重跑受影响的平衡实测并**明确标注**「旧数字作废」。

### 1.7 决策人
**owner**（确认是否在 Java 补齐，并接受既有平衡数字作废）。

---

## 二、CR-2：`data/balance.json` 有 15 个键，C# 只认 5 个

### 2.1 现象

编辑 `data/balance.json` 的 `royalCastleMaxHp`、`chainLimit`、`openingHand` 等键：
**Java 引擎会变，Unity 实机（C#）完全不变。**

### 2.2 证据

- C# 键映射 `src/Data/BalanceTable.cs` 的 `RuleKeyByField` **恰 5 项**：
  `handLimit`、`pioneerHandLimitBonus`、`pioneerOpponentPunishBonus`、`pioneerSelfPunishDiscount`、`maxPunishResponsesPerRound`。
- 未映射且 C# 侧**零引用**：`royalCastleMaxHp`、`royalCastleEnabled`、`royalCastleBreakVictoryCount`、`reshuffleLoseAt`、`reshuffleIncludesHand`、`chainLimit`、`deckMin`、`deckMax`、`openingHand`、`drawPerTurn`、`secondPlayerBonusDraw`。
- 取而代之是硬编码：`MatchSetup.cs:47 OpeningHandSize = 5`、`MatchSetup.cs:67 CastleHealth = 75`、`PlayCardActionHandler.cs:15 DefaultChainLimit = 20`；城堡开关/血量在 Unity 侧走 inspector 参数（`RuntimeBootstrap.cs:219-221`）。
- `docs/RULES.md` 开头承诺：「所有数值默认值均可在 `data/balance.json` 中调整」。

### 2.3 根因

**不是遗漏，是架构边界。** `BalanceTable` 的设计契约写在它自己的注释里：
「它只把 JSON 键翻译进引擎的 `MatchRules` 值对象」。而 `MatchRules` **只有 5 个字段**——
`BalanceTableTests.LoadedFieldsEqualTheMatchRulesFieldList` 还**断言**「.NET MatchRules surface is five fields today」。
**loader 在结构上不可能携带 `MatchRules` 没有的字段。**

所以真实缺陷有两层：
1. **文档层**：「F24 已闭」「C# 现在会读 balance.json」把覆盖面说大了，实际 **5/15**。
2. **架构层**：其余 knob 以硬编码常量散落在 `MatchSetup` / `PlayCardActionHandler` 等类里，**没有任何机制阻止未来继续分叉**。

### 2.4 解决假设

| 分步 | 做法 | 取舍 |
|---|---|---|
| **B1（建议立刻做，成本极低）** | ① 把「F24 已闭」更正为「**部分闭合 5/15**」；② 在 `RULES.md` 那句话后面补一份**明确清单**：哪些键由数据驱动、哪些是硬编码；③ 新增守卫测试：**若 `data/balance.json` 新增了未被 `RuleKeyByField` 映射的键，则测试失败**（现有实现只记 warning，`RuntimeBootstrap` 走 `LoadRules()` 时 warning 通道为 null，**实机上根本看不到**） | 不改行为，只消除「静默分叉」的可能 |
| **B2（按 owner 优先级增量做）** | 逐个 knob 扩 `MatchRules` 并接线（城堡血量/enabled → 复用 Java 已有语义；chainLimit/openingHand → 引擎构造函数参数） | 每次只动一个，可回归；**代价**：`MatchRules` 构造签名变化会波及大量测试 |

**PL 建议：先 B1，再按 owner 指定的 knob 逐个做 B2。不要一次全铺开。**

### 2.5 验收标准

1. 守卫测试存在，且**故意**往 `balance.json` 加一个新键时**会失败**（证明守卫有效）。
2. 文档清单与代码事实逐项一致（可由 DeepSeek 交叉核对）。
3. B2 每完成一个 knob：改该键 → C# 与 Java **都**变化，且有定向测试。

### 2.6 决策人
**owner**（决定 B2 做哪些 knob、优先级）；**Codex**（B1 可直接做）。

---

## 三、CR-3：读盘失败时的诊断消息**不实**

### 3.1 现象

读不到 `data/balance.json` 时，C# 打印：
「使用内置默认值（**与 data/balance.json 一致**）」
但回退值对 `maxPunishResponsesPerRound` 是 `0`，磁盘上是 `1` —— **这句话不成立**。

### 3.2 证据

- 打印点：`src/Data/BalanceTable.cs:131`（路径非法）、`:195`（找不到文件）。
- 同文件 **corrupt 分支**措辞才是正确的：「已回退到内置默认值（**无法确认**与磁盘数据一致，可能改变本局规则）」。
- `BalanceTableTests.cs:161-163` **断言**「与 data/balance.json 一致」这句必须出现 → **测试把这个错误信息锁死了**。

### 3.3 根因（团队自己的注释已经写明）

`BalanceTableTests.cs:42-50` 的自述原文：

> 「2026-09-11：本测试**改过一次语义**。它原本断言"发行平衡表逐字段等于内建默认值"，那是用来证明"接上 loader 不改变行为"的。
> owner 随后**有意**把 `maxPunishResponsesPerRound` 从 0（不限制）改成 1（一轮 1 张响应），
> 于是"逐字段相等"不再成立……而 T1 字段是**发行值 1**、内建回退仍是 **0**。」

⇒ **根因确认：9/11 改 T1 时，只改了 `data/balance.json` 与断言，没有同步更新「内建默认值」与「那条宣称两者一致的消息」。**
这条消息在 9/11 之前是**真**的，之后变成**假**的，且**没人改**。

**这也是它与 Java 侧的关系**：`Balance.java` 刚被修过同类缺陷（注释原文：「内置默认值必须与 data/balance.json 一致——否则读盘失败时会静默换一套规则」），
并据此把 `royalCastleEnabled/MaxHp` 从 `false/60` 改成 `true/75`。**同样的修复没有应用到 C# 侧。**

### 3.4 解决假设（**两个方向，取决于 owner 的语义选择**）

| 方案 | 做法 | 结果 |
|---|---|---|
| **A（PL 倾向）** | 把 C# 内建回退值**对齐磁盘**（T1 回退 = `1`） | 消息「与磁盘一致」**重新变成真**；文件缺失时实机行为与出厂一致 |
| **B** | 保留回退 `0`（保守＝不限制），**只把措辞改成** corrupt 分支那句「无法确认与磁盘数据一致」 | 消息变诚实；但文件缺失时实机仍与出厂**不同规则** |

**取舍**：A 让「缺失」= 「按出厂跑」，代价是失去「保守回退」；B 保留保守语义，代价是**实机可能静默换规则**（正是 Java 侧刚判定为缺陷的那种）。
另注：`BalanceTableTests.cs:161-163` 的断言在 A/B 两种方案下**都必须同步修改**，否则测试会锁死旧行为。

### 3.5 验收标准

1. 回退路径的**消息与事实一致**（用「删掉 balance.json 后跑一次」实证，而非只读代码）。
2. 若选 A：断言 `new MatchRules().MaxPunishResponsesPerRound == 1`，并同步 `BalanceTableTests` 现有断言。
3. 若选 B：断言消息包含「无法确认与磁盘数据一致」，并**保留**一条测试记录「回退值与磁盘不同」这一已知事实。

### 3.6 决策人
**owner**（balance.json 缺失时 T1 应开还是关）；措辞修改由 Codex 执行。

---

## 四、CR-4：两处生产注释与出厂数据矛盾（最小修复）

- `src/Engine/Rules/MatchRules.cs:76`：「对应 `data/balance.json` 的 `maxPunishResponsesPerRound`（**出厂 0**）」
- `src/Engine/Turns/PlayCardActionHandler.cs:347`：「（`data/balance.json: maxPunishResponsesPerRound`，**出厂 0 = 关闭**）」

而 `data/balance.json` 实为 **1**。**根因与 CR-3 同源**：9/11 改 T1 未同步注释。
**处置**：把「出厂值」与「回退值」在注释里**分开写清**（如「磁盘出厂 = 1；内建回退 = 0」），避免后人再次混淆。
**验收**：注释与 `data/balance.json` / `MatchRules` 默认值三者一致。**决策人：Codex（可直接做）。**

---

## 五、建议执行顺序

1. **归档优先**（不受本文件影响）：修 F36 两行 → 完整同批归档提交。
2. 取得 owner 对 **CR-1（Java 是否补 T1）** 与 **CR-3（回退语义 A/B）** 的裁定。
3. **CR-4**（两行注释）+ **CR-2 B1**（文档更正 + 守卫测试）——低风险，可立即做。
4. **CR-3** 按裁定实施（含同步修改被锁死的断言）。
5. **CR-1** 按裁定实施；完成后**重跑受影响的平衡实测**并标注旧数字作废。
6. **CR-2 B2** 按 owner 指定 knob 逐个增量推进。

---

## 六、红线（不要做）

- **不要**在归档前做任何「只提交一部分」的操作（6 处「已跟踪文件引用未跟踪文件」会让半套提交编译失败）。
- **不要**为了让测试变绿而放宽或删除断言；CR-3 涉及的断言**需要同步更新而非删除**，请在提交信息里说明改的是哪条、为什么。
- **不要**一次把 10 个未映射 knob 全部接上（`MatchRules` 构造签名变化波及面大）。
- **不要**自行决定 CR-1 / CR-3 的语义方向——那两项**等 owner**。
- **不要**手工删除 `StreamingAssets/data/.runtime-data-generated` 之类的生成文件来「凑绿」。

---

## 七、验证环境提示（避免重复踩坑）

- **`dotnet test` 在受限环境下会因 `testhost` 起进程失败**；PL 侧改用**进程内 NUnit runner**：
  `build-output/pl-p0/runner/bin/Release/net8.0/PlP0Runner.dll <tests dll> --xml out.xml`，输出含 `NUNIT_RESULT result=Passed total=N ...`。
- **`build-output/` 会被并发构建争用**（`Directory.Build.props` 把 `bin/obj` 重定向到那里）；与其他人同时跑 `dotnet` 时读数不可信。
- 构建前建议 `dotnet build-server shutdown`，否则可能出现 `ReplaceFileW EIO (Win32 1175)`，并**可能让测试跑到旧 DLL 上得到假绿**。
- Unity 官方 runner 会给 GUI 用例传 `-nographics`，导致 11 条 CardEditor 用例报「无图形设备」——这是**环境问题不是回归**（GUI Editor 下 331/331 全过）。
- 本次审核期间写入方仍在活动，故 **PL 未重跑全量门禁**；上表所有数字均来自**既有原始产物回读**（XML/TRX/JSON），非转述。

---

## 八、本文件不改变的事项

- PL **没有**修改任何 `src/`、`data/`、`scripts/`、`web/`、`unity/` 内容；本文件与 `PL_AUDIT_2026-10-02.md` 均为 `docs/` 下的规划文档。
- 平衡数值、规则语义、视觉方向的**最终决定权仍在 owner**。

---

## 九、Codex 独立复核与验证接手（2026-10-04）

### 复核边界与策划/契约结论

- 复核以 `docs/DAILY_GOAL.md` 10/1 收尾与本交接 10/2 入口为边界；没有可验证的“最后停止时”全仓快照，mtime/HEAD 只能筛候选，不能据此归因作者。mailbox 压缩脚本提示 1133 行但无可归档 settled 段，仅待处理/未知段；按协议跳过，没有压缩或删除。
- 当前 `docs/RULES.md:338` 是 59 张主牌 + 1 张统领；策划候选稿 `PL_FINAL_DECK_2026-09-24.md` 仍出现 60+1 与“定稿”字样。以 RULES 为当前玩家规则；策划文案需 PL 更正，不在本节裁定删哪张牌。
- `Balance.java` 复核到的改动仅使城堡默认值对齐当前数据并改善回退诊断；它没有实现 `maxPunishResponsesPerRound=1`。Java 模拟使用链深度上限 20，但不消费该 T1 响应窗；受 T1 影响的 Java 结果需标为基线风险，不能据此否定所有 C# 实测。owner 已授权 Java 窄实现，验收/回归结果待独立实现返回。
- “统领 Alpha 登场后免费 PULL”是历史人类指示，与当前 `RULES.md` §12.4 普通静态 PULL 成本并存且不一致；列为 `HUMAN_REQUIRED` 请 PL/owner 确认，不推断为本轮策划新改规则或现行代码缺陷。v1.31 snapshot strict schema 还未列当前 C# 输出的玩家计数器/Victory 字段，可能被严格消费者拒绝；记为既存 P1，当前交接不改契约/引擎语义，后续需兼容字段与严格 fixture 测试。

### Unity runner 与当前实证

- T1/T2/T3 runner 窄改已由静态 QA 复核：Edit/Play 测试阶段去掉 `-nographics` 但保留 `-batchmode`；构建阶段参数不变；独立 smoke 默认等待 30 秒。成功同步 build 后仍复用 `RuntimeDataStreamingBuildSafety` 的 owner-marker / generated-shape / fingerprint guard；成功后脚本只读检查精确 staging 路径下不残留 JSON payload，不声称空目录必须消失，也不删除内容。
- 官方 runner 原始结果位于 `build-output/unity-runtime-validation/20261004-102031-1b59e2c5/`：EditMode **332/332** 通过；PlayMode **32/33**，唯一失败 `RuntimeBootstrapPlayModeTests.RuntimeCardStripHasVisibleCardsOnFirstRenderedFrameAndAfterResize`，因 `WaitForEndOfFrame` 在 batchmode 不触发。QA 确认不存在保留真实首帧语义的等价 batch yield；测试未改/未跳过。runner 因此在 Windows build 前停止，不能报全门通过。
- 独立 build 的首次调用因将 log 与 exe 放入同一非空输出目录，被 `RuntimePlayerBuild` 的防覆盖 guard 正确拒绝；原始 log 保留在 `build-output/player-build-smoke-20261004-103448/windows-build.log`，没有 exe。正确布局重试前，官方 `-ValidateOnly` 检出上一 Unity 进程留下的 `Temp/UnityLockfile` 并以 `projectOpen=True` / exit 2 fail-closed；锁未删除或绕过。之后已正常 `unity open` 原项目，Editor PID 47536 保留运行；Computer Use 实际截图确认 Windows 锁屏，因此停止 UI 操作。**独立 build、30 秒 smoke、非 batch GUI PlayMode 均未完成**；须由用户解锁后继续，不能把旧 successful build 或批测的 32 项合并声称全过。

### 后续顺序与不变量

1. 解锁后在原 Editor 完成同源码、保真 `WaitForEndOfFrame` 的 GUI PlayMode 验证；正常退出后再按正式 runner 的输出布局重跑独立 Windows build 与 30 秒 bounded smoke，逐项记录 raw 结果。不得强杀 Editor、删锁、改测试等待语义或把独立阶段拼成官方 runner 全绿。
2. 收到 Java T1 实现后只按 C# 当前响应窗做跨审，核同 fixture、`TestMain` 与受影响模拟；不改 balance 值/DS 设计，不扩大 C# contract。
3. 保留 `HUMAN_REQUIRED` 的 PULL 成本问题、三语计数器术语缺口及 strict snapshot 兼容 P1；不得自行定规则、藏玩家需要的计数或放宽 schema。

---

## 十、Java T1 与机械地标载体复核（2026-10-04）

- 本节更新第九节“Java T1 实现/验收待返回”的旧状态。T1 已在 Java `Balance.java`/`Game.java` 落地：出厂/缺字段回退 `maxPunishResponsesPerRound=0`（不限），正式 `data/balance.json` 显式值仍为 `1`；每次顶层出牌或 COMMIT/PULL 惩罚事件各开一个响应窗，递归响应共享窗口，独立事件重置；仅成功接受的响应计数，被抑制牌保留手牌与激活状态。此语义与 C# 当前 `PunishRound` 一致。Java 83/83 T1 原始结果仍在 `build-output/java-t1-20261004/full-java-tests-final.txt`（SHA256 `4990D334C4D6FBA0394109D24108A9C5DF402CC8B51ED1ACB7DEC6720065D999`）；该值属于地标修复前基线，不与新总数拼接。此前 71.7%/30pp 等 Java 模拟数值不再是修复后证据，本轮未重跑模拟。
- T1 文件冻结 hash：`Balance.java` `37B3760B5DE3E53FB0360832EE8C10F9252026B45C91651B792AFC4ED1D05907`；`Game.java`（含下述修复）`8F69E82B26CAE4D9C1E16505E450F4F9C4CB9EA97F9D92B66567102B28DCF2F2`；`TestMain.java` `18F537FFA3711581EB9D9BF960277757AB8611C974D0F9DFDF3D89D419D9FE47`；`T1PunishResponseWindowTests.java` `C160AFBBB624391F23BC1E83DA8A3DE710F30AB9338E11AD9BE88C0723DBB1B7`。
- 跨审发现并修复既存 Java P1：生产 `machine_leader` 是 SPELL 地标，由 `Game.enterLeader` 保持为活动统领而不进入普通 `field`；旧 `isDownloadCarrier`/枚举只看 `field`，因此按真实路径无法用地标发起 PULL。旧 `realLeader` 测试夹具把所有统领伪造进 `field`，曾掩盖该问题。现在载体查询识别唯一活动的机械地标、避免 Alpha 在 field 与活动统领指针重复计数，并按当前控制者而非原始拥有者判定随从载体（对齐 C# `ControllerPlayerIndex`）。`TestMain` 新用例经真实抽牌→`enterLeader` 路径覆盖：只有地标时 PULL、云端 LIFO 顶端移出/墓地顺序、结束阶段晋升 Alpha、载体接替/去重及 CONTROL 后载体归属；`realLeader` 普通fixture也改为不把非随从统领放进 field。
- 修后完整 Java `scripts/build.bat` exit 0，`TestMain` exit 0 **84/84**；raw：`build-output/java-machine-landmark-20261004/java-build.txt`（SHA256 `8398039BBF85F1E1DC563C943952F3E327037CB9695EB2D9F26EED1472865BC2`）与 `build-output/java-machine-landmark-20261004/test-main-cmd-retry.txt`（SHA256 `F88CD0AD236CB6C9255C2F79A44E8E718E4A6494C0DED8E1822B3ABF294733EB`）。另有第一次 PowerShell 参数拆分造成的 `ClassNotFoundException` 原始文件 `test-main.txt`；这是启动命令失败，不是测试失败，成功的整行 cmd 运行已单独保存。
- 指定规则轴窄审：牌库循环计数归循环玩家且阈值 10、木 root/rampant 与封印 512、海胜利读取对手效果弃牌且阈值 18（强制手牌上限弃牌不计）、统领声明阈值、王城默认 75/破城方计数至少 9/被动条件与双方随从主动破城优先、机械 COMMIT/PUSH/PULL/ROLLBACK 时序，未见 Java/C# 与当前 RULES 的额外语义差异；海潮位仍是候选，不据策划候选改实现。发现 Java `PlayerState`/`Balance` 的牌库循环注释仍写成“对手循环计数”，与 RULES/实际实现相反，是注释级残项。
- 未结项按既有边界保留：四套预构筑仍是 60 张主牌 + 1 统领，RULES 已注明需迁移至 59+1，删卡选择待 PL/平衡测试；“Alpha 登场后免费 PULL”的历史人类指示与当前 RULES §12.4 普通 PULL 惩罚值规则并存，`HUMAN_REQUIRED`；T1 字段命名含 `PerRound` 而 C# 实际窗口是单个根动作事件，§12.4 仅描述沿用响应链，若需改玩家文案待 PL 确认。snapshot strict schema 兼容 P1 仍在另一边界，本批未改协议。其余机制/适配器/AI/联机/发布全栈未作全面一致性审计，标记 **not audited**。
- Java 修复的独立 QA 已返回：冻结源码指纹一致，实际生产统领入场、地标载体、Alpha 去重、CONTROL 后控制者归属和 PULL 顺序均确认，未发现 P0/P1；完整 Java 为 84/84。尚无专门组合 T1=1 与 PULL 生命周期惩罚的单独用例，仅记录为非阻断覆盖待办，不削弱现有测试。本结果不改变第九节 Unity gate：官方 batch Edit 332/332、Play 32/33（仅首帧/resize 用例的 `WaitForEndOfFrame` 不被 batchmode 触发），独立 Windows build / 30 秒 smoke / GUI PlayMode 仍因锁屏未完成。不得将旧 GUI 331/331 或中间 Java 83/83 与新结果混合成全门通过。

## 十一、当前非 Unity 交付门禁与最终交接（2026-10-04）

- 本轮 .NET Release 构建及测试 exit 0，TRX 为 Completed：918/918 通过，0 失败、0 跳过。原始 TRX：`build-output/dotnet-nonunity-20261004/dotnet-release-20261004.trx`；构建/测试日志：同目录 `dotnet-release-output.txt`。220 个相关 C# 源文件运行前后指纹未变；Data、Adapters、测试程序集本轮重新构建，Engine 增量复用的程序集仍晚于其最新源文件，未用旧测试报告替代当前结果。
- 与 10/1 的 909/909 TRX 比较，仅增加 9 个测试名、无旧名删除：1 个 Castle condition 用例及 8 个 effect-property 参数用例。这是测试集合差异，不代表本轮新增了 9 项游戏功能，不能据此归因作者。
- 同目录现有数据门禁：卡牌 schema 91/91（5 个文件）、牌组 4/4、设计资产清单 320/320 与透明度记录 109 项通过；sanity exit 0。逐项退出码、原始日志和产物指纹见 `fingerprint-and-gates-summary.json`、`project-freshness.json`、`dotnet-trx-summary.json`。
- 告警口径保留：schema 工具有 NU1900 漏洞信息获取警告，验证本身通过；sanity 汇总为 0 errors/0 warnings/0 info，但正文仍有 machine_alpha/shadow_of_fate 启发式 WARN，不能声称完全无提示。卡图映射脚本 exit 0 仅为诊断：91 张卡中 87 张未配置该脚本检查的 fallback、artId 字段为 0；素材清单通过不等于每张卡已配正式插图。
- 原实现代理因额度限制停止后，由根代理仅补齐本节项目交接记录；没有接手生产代码、切换付费服务、commit、push、删除锁或清理未知脏文件。所有实现及独立 QA 已由现有 Lunar 子代理完成。Unity 与第十节列出的规则/契约/内容迁移未结项仍未完成；解锁后的第一步是原项目非 batch GUI PlayMode，再正常关闭 Editor 后运行正确输出布局的 Windows build 和 30 秒启动检查。

## 十二、人类批准的底层与页面分工及 PL 交接（2026-10-04）

### 职责边界

- owner 本次明确将页面与素材交给另一个对话；本窗口专注游戏底层。此分工覆盖此前本窗口的页面布局任务，但不改变既有视觉方向、规则或公开接口。
- 本窗口负责 C# Engine/Data/Adapters、Java 对照实现、AI、数据验证及相关测试/构建支持；不继续修改页面布局、皮肤、美术素材、显示文案或视觉组件。C# 是当前 Unity 运行主线，不能以旧 Java 行为覆盖已批准的新规则。
- 页面对话沿用已批准布局及素材解耦约束，通过 snapshot、legal actions、events、稳定 ID 和现有 resolver 接线，不在页面复制行动合法性、目标筛选、阶段或胜负规则。玩法层不知道素材文件位置。
- Runtime 启动、Unity assembly 配置、共享契约、构建配置及跨层入口属于协作交界：改动前先列精确文件、接口影响与验证需求，由单一实现方修改；本窗口不同时修改页面对话正在编辑的文件。契约变更须保持兼容或明确迁移，不能悄悄换字段。
- 未提交内容仍是有效工作区，不代表已经远端归档。页面方应读当前工作区及本报告，不使用旧 GitHub 版本当新接口基线。双方保留未知改动；本次不批量 stage、commit、push、merge、reset 或删除所谓垃圾文件。

### 提交 PL 复核的现状与队列

- 最新实证：.NET Release 918/918（0 失败/0 跳过），Java 84/84；schema 91/91、牌组 4/4、资产清单 320/320。具体 raw、指纹及警告口径见第十、十一节，不将测试数等同于功能完成率。
- 当前 Unity batch EditMode 332/332，PlayMode 32/33；唯一失败是首帧/resize 用例的 WaitForEndOfFrame 在 batchmode 不触发，尚需 GUI 复验，不能删断言或标成全绿。新 Windows build、30 秒启动检查及原生鼠标完整对局仍未闭合；旧 GUI 全绿不能替代本轮证据。
- PL 待复核：Java T1 与机械地标修复；旧 Java 平衡读数需重跑；59+1 预构筑迁移的卡牌选择、Alpha 免费 PULL 与当前文字冲突、T1 响应窗术语、strict snapshot schema 兼容字段迁移。候选规则不视为已批准实现，不自行拍板。
- 发送状态：已整理仓库内正式报告及短留言，尚无真实 PL 接收/阅读凭证。本次执行 audited relay 的 ValidateOnly 失败：docs/DAILY_GOAL.md exceeds the 20,000-character relay limit。未进入 PlanOnly/执行模式，未发生付费 PL 调用，未改门限或绕过入口。需要在保留历史计划的前提下修复交接入口再进行实时交付；仓库通知不冒充实时送达。

## 十三、底层程序修改目标草案（2026-10-04，只读复核，待 PL 审核）

本节是待实施目标，不是完成记录。本次不改生产代码、不重复运行整套测试、不 stage/commit/push；不把 918/918 或 Java 84/84 扩大成无缺陷结论。页面布局不在范围内。

| 顺序 | 证据与影响 | 最小修改目标 | 验收 |
| --- | --- | --- | --- |
| 1：F36 结算时序（P1） | `EffectRuntime.Cards.cs:418` 统领子上下文、`EffectRuntime.Mechanical.cs:193` PULL 子上下文仍由公开构造新建独立窗口；未继承父 `DeferDeaths`。`EffectDispatcher.cs:174–175` 恢复子窗口的 false 后调用 CheckAll，可能提前清理父批次的死亡单位。现有 NestedHookChain 用例覆盖普通弃牌钩子，不是这两条路径。 | 先补两条嵌套行为复现，再让子批继承死亡延迟状态；不顺便共享其它状态或重写结算系统。 | 父批中途经过统领/PULL 子效果后，后续伤害/治疗仍按批准批次语义命中，最后只清理一次；独立致死效果仍正常清理。保留复现失败与修后通过证据。 |
| 2：独立 Player 平衡配置（P1，静态缺口确认，部署影响待实测） | `RuntimeBootstrap.cs:230` 使用无参数 `BalanceTable.LoadRules()`，没有使用同函数已解析的 data root；`BalanceTable.TryResolveShippedPath` 只向 AppContext/CWD 祖先搜索 data/balance.json。构建预处理只复制 cards/decks，不复制 balance；generated-root 归属检查也未包含 balance。离开仓库运行可能找不到配置，T1 从正式文件 1 回退为内建 0。 | 正式配置与已验证的数据根一致，纳入精确打包清单；同步维护 staging 归属/指纹/清理保护。不扩大删除范围，不改缺文件时的玩法默认值。 | 将 Player 放在无仓库祖先的独立目录运行，证明实际加载正式 balance 且 T1=1；删失/损坏配置用隔离 fixture 验证诊断。人工 StreamingAssets/未知文件不被清理。 |
| 3：strict snapshot 契约（P1） | 当前 DTO/projection 输出 8 个玩家状态字段及 card.victory，而 v1.31 schema 相应对象 additionalProperties=false 且未列这些字段；严格消费者会拒绝。 | 经 PL 核对后补明确的兼容字段及闭合 victory 定义，不把 additionalProperties 改成 true、不删除 UI 需要的状态。 | 真实 gateway 输出→正式序列化→严格 schema 验证，覆盖两种观看者、统领进度、隐藏信息和旧 fixture；错误字段仍被拒绝。 |
| 4：配置覆盖与诚实诊断（P2，防止后续调参分叉） | balance 实际 16 个键，C# 映射 5 个；余 11 个键未生效。LoadRules 不提供 warning sink，调用者拿不到 load.Message；missing/invalid/unresolved 分支还宣称与磁盘一致，但 T1 内建0/正式1。 | 先实现明确的已映射/有意未映射清单与新键守卫，公开启动诊断，修正错误措辞和测试；保留回退0语义。其它 knob 是否接线由 PL 逐项定，不一次启用11项。 | 新增未声明键在 fixture 中触发守卫；16键归属清楚；缺文件诊断不声称与磁盘一致；当前游戏数值不变。 |
| 5：低风险说明修正（P3） | C# 两处注释仍写T1“出厂0”；Java PlayerState/Balance 的循环计数注释仍写对手计数；MatchFactory 注释仍把正式文件与内建值说成一致。 | 仅同步注释至当前行为，不改任何判胜/费用/上限。 | 窄 diff 确认没有运行行为改动。 |

### 不在本轮擅自实施

- 59+1 已批准，但四套 deck 实际均为 60 张主牌+1统领；每套删哪张重复牌待 PL 选择，不随机删卡来凑合规。
- Alpha 免费 PULL 与当前文本、海印记未冻结语义、T1 对玩家的响应窗表述继续作为设计待决项；不恢复旧潮位、不调 AI 权重或卡牌数值。
- Java 修复后旧平衡数字需重跑；这与程序修复及安全存档分开，不以旧数字阻塞所有主线。
- Unity GUI 首帧用例、新 Windows build/30秒启动/原生完整对局仍为待验证，不以删测试或 batch 替代方式关闭。

### checkpoint 建议（待审核，不等于本次提交授权）

- 本次逐文件 status 快照：HEAD ce0d6b6；102 项已跟踪修改、416 个未跟踪文件，其中208个是根目录 InternalTrace 日志，另外208个仍需按源码/内容/文档/新页面素材/测试产物分类，不能统称垃圾。这与默认折叠目录的 status 数量不是同一口径。
- 核实的同批依赖仍在：HiddenInformationRedaction、RuntimeActionSelection、BalanceTable、VictoryCondition、VictoryObjectiveDefinition、RollbackActionHandler 与整套 Adapters/Ai 尚未跟踪，但已有已跟踪源码引用。只提交调用者会产生不完整版本；相关 Unity .meta 也需成对纳入。
- PL 审核后先确定一份精确归档清单及短暂停写窗口，排除日志、缓存、测试截图与凭据，按依赖闭包同批存档。使用精确路径，不使用 git add -A；提交前检查 staged diff 并运行相应验证。另一个对话正在生成的未完成页面内容要明确是否纳入，不能不经核对连带提交。
- 不必等全部功能/已知缺陷清零才 checkpoint：可以保存注明已知缺陷与验证边界的可恢复基线。优先快速修复已复现的 F36；若其复验受阻，不让存档无限推迟。当前不合并远端、不改阵营名、不 push；这些另外审批。

## 十四、owner 批准的修复与整批归档（2026-10-04）

本节更新第十三节的“待授权”状态：owner 已明确批准单次整批归档并首次推送当前 `pl/ai-threat-estimator` 到 `origin`，随后确认“先归档”。修复由既有 Lunar 子代理实施，Codex 根代理负责复核、证据整理和 Git 操作；不启动 relay、不调用 DeepSeek，不进入新的规则或平衡修改批次。

### 本批实现与复现证据

- F36：PULL 与统领降临的子 `EffectContext` 各补一条 `DeferDeaths` 继承赋值，公开接口不变。生产探针位于 `src/Engine/Tests/F36DeathDeferralTests.cs`，分别验证父批 PULL 载荷 AOE + AOE、父批 DRAW 触发统领进入 AOE + AOE；两个敌方 3 生命单位必须合计产生 4 条 DAMAGE_DEALT、0 条跳过 DAMAGE，后者同时断言 LEADER_MANIFESTED。修前两条均失败（实际伤害事件 2 而非 4），修后两条通过；独立修前原始 TRX 为 `build-output/checkpoint-20261004/f36-independent-targeted-before-fix.trx`。
- 古木 P0-1：C# 加载默认值对齐 RULES §2 的明确声明原则及 Java：只有 PUNISH 类型默认启用惩罚降临，默认 punishCost 为 0。精确给指定 16 张卡补 `punishActivatable:false`，未改其他属性；wood_owl、wood_punish_wrath 保持原数据。测试覆盖 16 张的 false/0、真实惩罚抽牌后不自动响应及随后普通打出正常执行 onPlayEffects；保留显式能力（包括 flame_berserker）的原断言。
- 古木 P0-2：成功的非吟唱、非反制、尚未判胜且效果列表为空的打出，追加既有内部 EFFECT_SKIPPED（action=CARD_EFFECTS，reasonKey=effect.no_effects），不改变结算状态。定向用例同时防止对吟唱、反制和先行判胜误报。这是内部事件审计，不在现有 UI 事件映射中，**不声称页面已显示空效果提示**。
- 精确定向回归：39/39 通过，0 失败、0 跳过（DataLoader 33、F36 2、惩罚激活/空效果 4）；原始 TRX `build-output/p0-targeted-20261004-r3/p0-targeted-r3.trx`，构建及测试输出同目录 `console.txt`。首次新用例因测试自身用 TargetReferenceId 而非实体 TargetId 筛动作失败，已只修测试定位，未据此修改生产目标协议。
- 首次全量回归实际为 920/925 通过、5 失败、0 跳过，exit 1，**因此未提交**。原始 TRX `build-output/checkpoint-20261004/dotnet-full/full-dotnet-release-20261004.trx`；与此前 918 基线相比新增 7 个测试名、无旧测试名删除，差集保存于同目录 `trx-diff-summary.json`。其中两条机械胜利流程被恢复正常能力的古木在第 4 回合合法 512 胜利截断；其余失败仍须逐项核实，不将它们未经证据全部归因测试前提，也不删除或放宽断言以通过门禁。
- 本批重新运行数据门禁：card schema 91/91、0 失败，牌组 4/4、0 失败，均 exit 0；原始记录 `build-output/checkpoint-20261004/cards-schema-validation.txt` 与 `decks-validation.txt`。schema 的 NU1900 漏洞信息源获取告警不等于 schema 失败。牌组检查通过不代表 59+1 迁移已完成：现有四套仍是 60 张主牌+1统领。
- Java 本批重新构建 exit 0，TestMain 84/84 通过、0 失败；原始记录 `build-output/checkpoint-20261004/java-build.txt`、`java-testmain.txt` 和分别对应的 exit-code 文件。没有重跑尚待后续批次的 Java 大范围平衡测试。

### 修后 C# 古木生产/B1 真配对（不作为最终平衡裁定）

使用本轮重新编译的相同引擎和共享驱动，T1=1、default 策略。每个烈焰/机械/深海对手各 40 局、先后手各 20 局；两臂各 120 个唯一对手/seed/first 键，全部逐局相等、无重复。非 wood 的四份卡牌定义逐字节相同，三套对手 deck 均由同一仓库路径加载；输入指纹见 `build-output/checkpoint-20261004/matched-wood/input-manifest.json`。B1 是既有实验 B1，不是 B1′；实验卡未加入生产。

| 指标 | 生产古木 | 实验 B1 |
| --- | ---: | ---: |
| 尝试 / 有效 / 无效 | 120 / 120 / 0 | 120 / 120 / 0 |
| 古木获胜 / 胜率 | 86 / 71.67% | 65 / 54.17% |
| 有效局平均回合 | 5.9583 | 8.0500 |
| 达到 512 的有效局 / 比例 | 86 / 71.67% | 59 / 49.17% |
| 全场胜因：GIANT_HEALTH_GE | 86 | 59 |
| 全场胜因：enemy_leader_defeated | 5 | 25 |
| 全场胜因：OPP_DISCARD_TOTAL_GE | 15 | 13 |
| 全场胜因：PULL_TOTAL_GE | 14 | 23 |
| 古木出牌“无非诊断事件”代理 / 接受的古木出牌 | 283 / 2346（12.06%） | 119 / 2757（4.32%） |
| 古木出牌关联的 effect.no_effects 空列表审计数 | 0 | 0 |
| 增幅 BUFF / 实际古木回合 | 963 / 374（2.5749） | 769 / 487（1.5791） |

增幅事件使用 `BUFF_APPLIED`、`d_growthApplied=true` 且 amount≠baseAmount；本样本相应事件目标均属于木侧。原始事件没有 EffectContext 来源字段，不能将 CurrentPlayer actor 冒充严格效果来源，也不能推断未来中立目标不存在。木回合按含对手的完整对局键和真实 first/turn 计算，不用只按 seed 混组的旧分母。零效果代理排除普通 EFFECT_SKIPPED 和空列表审计，是粗粒度事件指标，受吟唱、召唤主体和嵌套惩罚响应影响，**不是坏卡率或单卡因果归因**。

原始对局/事件/出牌/回合分别保留在 `build-output/checkpoint-20261004/matched-wood/{prod-run,b1-run}/`，两臂 summary.json、console 与 analysis 也在 checkpoint 目录。首次默认驱动输出因 DLL 被别的进程占用而构建失败，保留 `dynamic-driver-build.txt`；随后按脚本已有参数使用全新 `shared-driver` / `wood-probe-fresh` 隔离目录重新编译成功，没有杀进程、覆盖锁或使用旧 DLL。驱动编译仍有既有 CS8602/CS1701/CS1717 提示，不宣称零警告。

该结果仅说明本修后基线下 B1 比生产少 21 胜（-17.5 个百分点），不替 owner 决定卡值、实验入库或是否采用 B1。生产 71.67% 与旧 Java 71.7% 数值近似是两组不同证据，不得混用。尚缺 P0-3、后续 Alpha 免费 PULL 与回退配置裁定的实现，因此不能据本表称完整设计路线已验收。

### 归档卫生与边界

- 使用精确路径纳入全部真实源码、内容、契约、测试、技术/PL 文档及已结束的页面预览，包含依赖它们的未跟踪类和 Unity .meta；不使用 git add -A，不做半套提交。现有 DAILY_GOAL/RULES 的历史修改属于待归档内容，本窗口未编辑这两份文件或 AGENTS。
- 根目录 InternalTrace 数字日志、精确列出的 Unity 测试截图/临时测试场景、自动 staging 标记已加入 ignore，保留磁盘原件，不删除真实资产或未知文件。208 条既有日志不得随批进入 Git；测试执行可能继续生成日志，不将新增数量当作真实源码。
- 共有 22 个仍位于 ignored build-output 下的必要驱动/探针源文件按精确源码白名单例外归档（pl-csim、wood-probe、dynamic 构建入口、PoolContractValidator、古木测量源码/脚本），不纳入 DLL、运行缓存、模拟 JSONL 或实验卡池。沿用现有目录，不在本次移动工具或改构建架构。部分探针入口仍依赖 ignored 输出下已有程序集，**不宣称这些工具已完成干净克隆环境的自举**。
- `design/menu-kit-2026-10-04/`、`docs/work/`、`design/runtime-kit-v1.31/` 均保留。页面方已确认当前 spatial-preview 小批结束并暂停写入；其 Three.js MIT、思源黑体 SIL OFL 资源附许可证/来源/哈希，仅归档已存在的独立简模，不接入或改动正式页面。17.6 MB 字库是许可随附的真实预览资源，不是构建垃圾。
- 提交前按候选文件名及文本秘密模式扫描未见匹配；这不构成所有历史内容的安全保证。没有删除、reset/clean、强推、合并保护分支或发布游戏。

### 明确未完成与后续批次

- P0-3 统领降临逐段选目标尚未实现：owner 同意先归档，再由底层提供可暂停/继续的目标选择协议、页面方接输入；不能自动选第一只单位来替代玩家选择。其影响仍存在，不标成修复完成。
- PL 在本批执行期间更新了 RULES 与 mailbox：木 512 改为与当前实现一致的结算后立即检查；CR-3 回退 T1=1、CR-1 重跑 Java 平衡及 Alpha 在场免费 PULL 记录为新的 owner 裁定。该并发文档更新予以保留，不回退；本批没有顺便实施这些后续任务。Alpha 条文已有“尚未实现”标记，免费 PULL 必须后续独立规则提交，不能用本批门禁冒充其验收。
- 没有调卡值/平衡参数，没有启用新目标类型、woodSource 阵营开关或改变 512，没有将 10 张实验卡移入生产。RULES 中 growth/owl 的例子不一致留 PL 更正；本窗口不改规则文本。
- 修前古木 34.2%、14.2%、32.5% 与旧 Java 71.7% 作为平衡证据**作废**，不得与本批修后数字拼接。新对照也仅属于已修 P0-1/P0-2 的当前程序基线，不能代替尚缺 P0-3 的设计路线验收。
- Unity GUI 首帧/resize、当前 Windows build/30 秒启动及原生鼠标完整对局仍未复验；不把此前 batch Edit 332/332、Play 32/33 或浏览器空间预览测试扩大为正式游戏全门通过。页面、素材、正式 UI 文案仍由另一个对话负责。

### 2026-10-05 续办：门禁与后续批次隔离

- owner 已明确要求“等全量测试绿了再提交推送”，因此仍未 stage、commit 或 push。第一批之后按 B2-A（T1 缺失/不可读回退 1）→B2-B（在场 Alpha 免费 PULL）→B2-C（最终同种子实测）推进；B2-A 与 B2-B 分开提交，不能夹入第一批归档。规则书与 CHANGELOG 仍由 PL 更新。
- 机械流程测试的两次受控对手夹具尝试尚未成立：被动完整牌库定向 15/17，双方统领门槛开启太晚而累计 21 次 PULL；短 5 张牌库定向 13/17，对手在第 22 回合牌库循环获胜而 PULL 为 0。原始记录分别为 `build-output/p0-fixture-targeted-20261004/machine-fixture-targeted.trx` 与 `build-output/p0-short-wood-targeted-20261004/machine-short-wood-targeted.trx`。它们不是新的全量结果，也不作为生产规则缺陷的证据；修正仅限测试夹具，不修改胜利门槛或放宽原断言。
- 中间古木配对汇总现已独立完成：`build-output/checkpoint-20261004/matched-wood/metrics-summary.json`。完整对局键与 turn 校验无重复/缺失，生产 715 条回合记录、B1 966 条，分别与 games 的回合总数一致；实际木回合分母为 374/487。修正后的分析器也通过同 seed、三个对手与不同目标归属的合成校验。这份汇总不替代 B2 全落地后的最终重跑。
- 页面对话获 owner 新授权制作 `design/menu-kit-2026-10-05-3d/`，该进行中的新资产包暂不纳入本次既有工作区 checkpoint；不删除、不干预其独立制作。此前完成的 `design/menu-kit-2026-10-04/` 仍按精确路径归档，共享旧入口保持单一修改方。
- 机械夹具最终定向 17/17 通过、0 失败/0 跳过，raw 为 `build-output/p0-machine-draw-oppdraw-targeted-20261005/machine-draw-oppdraw-targeted-20261005.trx`。测试专用卡经一个真实广告的 PLAY_CARD 执行既有 DRAW60 与 OPP_DRAW60，分别从双方牌库触发真实统领显形；仍使用生产机械牌组、原 seed、原策略/逐步 coordinator 和原胜利规则。两条机械接受测试均在第 15 回合、第 6 次 PULL 精确终局，第六次之前双方统领门槛已满足、拒绝 0；两条广告载荷保真用例也实际经过有目标选择的 PULL。未手造统领/赢家、未截断到第六次、未放宽原断言。两文件已冻结，等待响应策略单点与全量复验。
- 页面对话随后确认 10/5 新3D资产小批已完成且停止扩展；仍按约定留待另批归档。其浏览器模型验证不代替 Unity 导入、PBR/性能或正式菜单实装验收。
- 响应策略单点改为固定两次合法响应机会，经真实 gateway 执行和 DAMAGE 终局；T1 显式 0 只为隔离策略本身。原有 terminal、零拒绝、Never/默认行为一致及 First 的触发/机会断言均保留，另加相同机会、ADD_ROOT 实效 2/1/0/0 与同一胜负结果断言。单点 1/1、0 失败/0 跳过，raw `build-output/checkpoint-20261005/playstyle-fixed-response-r3/playstyle-fixed-response-r3.trx`；首版夹具因终局卡缺 KingSlayer 未广告统领目标而失败，已只补夹具能力，不标成引擎错误。
- 第一批最终 fresh .NET 全量 **925/925 通过、0 失败/0 跳过**，raw `build-output/checkpoint-20261005/full-dotnet/full-dotnet-20261005.trx`；相对 918 基线新增 7 条定向用例，未删除旧测试。此前 920/925 和夹具失败记录保留为过程证据，不再冒充当前门禁。F36 修前 918/918、修后含新增用例 925/925；Java 当前批 84/84，数据校验口径仍如上。
- B2-A 预审纠正前置信息：Java `Balance.load` 实际缺文件/读取失败/缺 T1 字段仍通常从默认 0 出发，`Game` 没有隐藏的 1 兜底；磁盘显式 1 路径正常。后续须按 owner 已裁定的 CR-3 对齐 loader 回退，两端直接构造规则对象的默认保持不动；若解析途中失败，不返回部分修改对象。第一批没有混入此修复或 Alpha 免费 PULL。
- 最终暂存清单共 336 个精确路径（314 个正常候选 + 22 个源码白名单），无清单外文件，冻结到暂存的 SHA256 未变。完整 staged diff-check 仍报告 9 个既有文件的空白格式问题：spatial-preview 的 index/menu/verify 末尾空行、官方 vendor/three.cjs 缩进、4 份历史 PL 报告末尾空行、Unity Ai.meta 空字段尾空格。保留第三方/素材指纹及历史文档，不做行为或批量格式改写；仅排除这 9 个已列明路径的检查 exit 0，**不声称完整 staged diff-check 无告警**。常见私钥/token 格式扫描无匹配，不等同全历史安全保证。
- 归档提交：`81b62559c8e4cfe456d1628018541fb900d8430e`，信息为“checkpoint: 底层引擎 + 契约 + 文档同批归档”；提交包含 Java T1、机械地标、探针/共享驱动、F36 与新增 7 条回归的说明。首次 push exit 0，新远端分支 `origin/pl/ai-threat-estimator` 成功建立并设置 upstream，`ls-remote` 返回完全相同 SHA。未强推、改写历史、合并保护分支、删除远端或发布游戏。该 SHA 回执与提交后 mailbox 通知是提交后的文档改动，尚未另做 closeout commit，不声称它们已包含在自身提交中。

## 十五、第二批配置与 Alpha 行为分离实施（2026-10-05）

### B2-A：CR-3 已完成、独立提交

- 提交 `36177b4610efed726eb6958125e9ffb7edb67709`：`fix(data): use T1=1 when balance configuration cannot be loaded`，仅含 `src/Data/BalanceTable.cs`、`src/Engine/Tests/BalanceTableTests.cs`、`src/main/java/com/dominionwars/data/Balance.java`、`src/test/java/com/dominionwars/test/T1PunishResponseWindowTests.java`。不含 F、卡值或规则文档改动。目前远端已核实的首次 checkpoint 仍为 `81b6255`；本项单独提交后尚未另推，不冒充远端已包含。
- 两端 loader 在文件缺失/不可读/解析失败时 T1 回退 1；合法文件缺 T1 字段也取 1。其它规则默认不变，直接 `new MatchRules()` / `new Balance()` / Java `apply({})` 的默认 0 保留，文件明确配置的 0 或 1 均保留。Java 丢弃解析失败的候选对象，返回独立完整回退对象，避免部分 apply 状态。诊断明确只保证 T1 基准，不保证所有磁盘字段相等。
- 定向最终 C# 15/15、0 失败/0 跳过，raw `build-output/b2a-cr3-csharp-final-r3-20261005/b2a-balance-table-final-r3-20261005.trx`。QA 独立 fresh 全量 .NET **927/927、0 失败/0 跳过、exit 0**，raw `build-output/b2a-full-20261005-r3/trx/dotnet-full-r3.trx`；四文件运行前后 SHA256 与冻结清单一致。
- QA 独立 Java 重建 exit 0、TestMain **85/85、exit 0**，实际 stdout 为 `build-output/b2a-full-20261005-r3/java/testmain-stdout.txt`，stderr 同目录保留（配置失败探针的预期诊断，不等于用例失败）。首个实现方 transcript 只记录退出码，数量证据以独立 stdout 为准。
- 对比第一批 925 个结果净新增 2 个用例（invalid path、missing T1 key）；一个非对象 JSON 旧用例仅改名为 `CorruptFileThatIsNotAnObjectUsesLoaderFallback`，原夹具及断言仍执行，不计作新增或删除行为。其它缺失/损坏用例原名保留，原断言按 owner 的 CR-3 同步加强，不删除或放宽。

### 后续边界

B2-B 免费 PULL 在本节 B2-A 提交时尚未实施；先隔离构建并冻结修前程序集、数据与策略指纹，再单独实现/提交 F。B2-C 的最终同种子实测另出报告，不能借用旧 DLL、混合中间结果或自行调整卡值/512/阈值。P0-3 逐段选择协议、当前 Unity 原生验证和新3D包仍不在本项完成声明内。

### 10/8 接续：出牌源归属修复独立归档与修前冻结

- 保留并复核另一对话的出牌源归属修复：通过合法性/目标校验的出牌源在惩罚响应前离开手牌，避免被响应弃掉后又进入战场或墓地；支付期间终局则只进入拥有者墓地一次。没有修改卡值、胜利条件、费用、AI 权重或规则文档。来源对话已确认停止共享源码写入。
- 独立 fresh Release 回归 .NET **936/936、0 失败/0 跳过**，raw `build-output/checkpoint-20261008/b2c/preF/validation/trx/source-ownership-full-20261008.trx`；Java fresh build / TestMain 均 exit 0，**89/89**，raw `.../validation/java/testmain-stdout.txt`。C# 用例名称 multiset 与来源批次一致；六个归档文件冻结哈希无漂移。
- 精确六路径独立提交 `b26a744e2ec1ae5c0c3bc0cdcc6932c84da9e7a9`：两端出牌实现、新增 C# 9 例及 Java 4 例、Java 注册和必要 Unity 测试 .meta。暂存空白检查通过，没有混入 F 或页面素材，没有 push。
- 修前 shared-driver / growth 已在 `build-output/checkpoint-20261008/b2c/preF/` fresh 构建，两个 exit 0，149 个被编译 C# 源文件哈希核对无漂移；卡池、卡组、balance 与 RULES 的原字节副本及指纹用于隔离模拟，后续不读取 live data。已启动三组双牌组、每组 40 局的机械修前样本，最终统计仍待结果。
- 已向实现子代理发出 F 编辑 GO；免费 PULL 尚未验收，必须独立提交。P0-3 逐段选择协议仍待跨层实施。页面/卡图/卡组候选均独立，不纳入本次底层提交，也不把插画生成或候选筛查等同于生产采用。

## 十六、B2-B/F：活动机械 Alpha 令 PULL 免费（2026-10-08 收尾）

- 规则边界：只有拥有者的真实 `machine_alpha` 随从统领实例仍在其统领/场上位置时，PULL 生命周期惩罚为 0；未登场或离场立即恢复栈顶牌印刷 `downloadCost`。不加sealed/血量等额外门槛。C# 广告、`PULL_DECLARED`、响应支付及 `CARD_PULLED` 共用一次有效费用快照；快照绑定来源与栈顶且不继承给嵌套 PULL。Java `Game.pullWith` 与 AI `askPull`/惩罚额度读数复用同一有效费用解析。
- 精确 11 路径本机提交 `31c8f46c51d2875dabaff294011c65c3f8567850`：C# `src/Engine/Effects/EffectContext.cs`、`src/Engine/Effects/EffectRuntime.Mechanical.cs`、`src/Engine/LegalActionGenerator.cs`、`src/Engine/Turns/PullActionHandler.cs`，C# 测试 `src/Engine/Tests/ProductionFactionIntegrationTests.cs`、`src/Engine/Tests/PullAlphaFreeTests.cs` 及其 `.meta`，Java `src/main/java/com/dominionwars/engine/Game.java`、`src/main/java/com/dominionwars/ai/AiAgent.java`、`src/test/java/com/dominionwars/test/PullAlphaFreeTests.java`、`src/test/java/com/dominionwars/test/TestMain.java`。提交未 push。
- 正式验证：fresh Release .NET **938/938，0 failed、0 skipped**；Java `TestMain` **93/93，exit 0**。原始 .NET TRX `build-output/checkpoint-20261008/b2b/postF/dotnet/postf-full-dotnet-r2.trx`，Java stdout `build-output/checkpoint-20261008/b2b/postF/java/test-main.txt`。目标 .NET 过滤 5/5 raw `build-output/b2b-20261008/lunar/targeted-dotnet-r4.txt`；Java 93/93 原始 stdout `build-output/b2b-20261008/lunar/java/test-main-r3.txt`。定向测试涵盖实际 landmark→Alpha 晋升后六次 PULL 收费 `[1,1,0,0,0,0]`、1→0→1 的源/顶牌收费快照、高费 Titan 免费、敌方/同名非统领拒绝、Java AI 费用输入及 Web 正费 pending/decline 对照与零费真实 PULL 无 pending。
- 独立同输入 scratch trace（非正式测试源）以相同生产 `machine_golem`、`machine_drone`（成本 1）、4 张 ALWAYS PUNISH 响应牌及 decline policy，逐状态 notAlpha / activeAlpha / removed 比较：两端费用、实际惩罚抽牌、响应回调均 `[1,0,1]`，每步 pullCount +1 且栈顶入墓。两端 probe 源码均定义 `response_0..3` / `Response 0..3`，并使用相同 type、激活、punishCost=0 与 ALWAYS 条件；trace 本身不单独输出响应牌 ID。JSON raw：`build-output/b2b-20261008/lunar/crossfixture/csharp/trace.json`、`build-output/b2b-20261008/lunar/crossfixture/java-trace.json`；均 exit 0，hash 与 11 源路径清单记录在 ignored `build-output/b2b-20261008/lunar/freeze-manifest.txt`。此窄 probe 手动切换真实 Alpha 实例在场状态，**不冒充** landmark 晋升；生产晋升另由正式集成用例覆盖。
- 不变项：仅 PULL 生命周期费用；未修改 COMMIT/PUSH、PULL 次数胜利门槛、目标选择/顺序、卡值、AI 权重或 RULES/UI。P0-3 逐段统领目标选择仍延期到跨层协议与 UI 可选目标工作；本提交不声称修复该项，也不声称完成 Unity/native 鼠标验收。
- B2-C 的 post-F 机械/古木配对数值和设计解释由 QA 维护既有 `docs/CODEX_B2_FINAL_BALANCE_VERIFICATION_2026-10-08.md`；本节不重述胜率、不作采纳/平衡结论。历史修前 936/936 与 pre-F 模拟仍是不同基线，不替代本节 post-F 门禁。

### 10/8 远端同步回执

- 按 owner 已授权的归档/推送执行普通 `git push origin HEAD:refs/heads/pl/ai-threat-estimator`，exit 0；远端由 `81b6255` 前进至 `31c8f46c51d2875dabaff294011c65c3f8567850`，随后 `git ls-remote` 核对相同 SHA。因此 B2-A `36177b4`、出牌源归属 `b26a744`、B2-B/F `31c8f46` 已分别归档且远端包含。
- 没有强推、改写历史、合并保护分支、删除远程分支或发布游戏。本节以前的“未 push”是各提交当时的状态，现以本回执为准。页面/美术另一对话的未跟踪产出仍留在本地，没有混入本次生产提交。
