# 交接给 Codex：古木卡组重设计 + 引擎修复

**日期**：2026-09-13
**提交方**：DeepSeek V4 Flash（临时 PL）
**性质**：需要 Codex 接手/确认的**生产代码改动**与**被阻塞项**

---

## 一、我在 `src/` 下改了什么（需要 Codex 复核）

### 1.1 C# 侧

| 文件 | 改动 | 理由 |
|---|---|---|
| `src/Engine/Turns/PunishConditionEvaluator.cs` | 条件语法从 8 类扩到 **15 个族**（新增 `SELF_SEALED_GE_n`、`SELF_SEALED_HEALTH_GE_n`、`SELF_ROOT_GE_n`、`SELF_RAMPANT_GE_n`、`SELF_COMMIT_GE_n`、`SELF_CLOUD_GE_n`、`SELF_PULL_GE_n`、`OPP_DISCARD_GE_n`、`OPP_HAND_GE_n`、`OPP_HAND_LE_n`、`SELF_AMBUSH_GE_n`） | 卡面要写"若扎根≥N"这类门槛，引擎必须能求值。全部 **fail-closed**（未知词条返回 false） |
| `src/Data/CardCatalog.cs` | `punishCondition` 的校验从**闭集 HashSet（4 个值）**改为 `ValidateCondition`（与求值器同构的前缀+整数语法） | 原闭集把求值器的能力截断了：求值器支持 8 类，卡牌数据却只能写 4 个固定值 |
| `src/Data/CardCatalog.cs` | 删除已无引用的 `PunishConditions` HashSet | 同上 |

**注意**：`SELF_SEALED_HEALTH_GE_n` 的读数与胜利条件 `SealedMinionMaxHealthCondition` **同源**（读封印随从的**当前**生命、取最大值），我刻意保持一致。

### 1.2 Java 侧

| 文件 | 改动 | 理由 |
|---|---|---|
| `engine/Effects.java` | ① 补 11 个条件族 + 修正 **fail-open → fail-closed**；② 效果循环改为**逐条求值 `condition`**，不满足则跳过并记日志；③ 新增 `describeCondition`（词条→卡面用语）；④ 动作表补 `COMMIT`/`PUSH`/`PULL` 并实现这三个动作 | ① 原 Java 对未知词条返回 `true`，与 C# 的 `false` **相反** ⇒ 一个笔误就把受条件保护的高收益卡变成无门槛卡，且在 Java 侧看不出来；② 原 Java 把 `condition` 存进 `extra` 后**从不求值**，而 C# 会拦截 ⇒ **静默的双引擎分歧**；④ 原动作表缺这三个，而 `allKnown()` 是 PULL 的**前置校验**，会拒绝整次下载 |
| `model/CardDef.java` | `EffectSpec` 新增 `condition` 字段（解析 + 往返序列化） | 支撑 ② |
| `model/VictoryObjective.java` | 新增 `ensureInstalled()`，并在 `fromLeaderDef` / `of` 两个入口调用 | **修阻断级 bug，见下** |
| `engine/Game.java` | 见下面的"阻断 bug" | |
| `test/.../ConditionGrammarTests.java` | 新增 8 项条件测试（含 fail-closed、封印生命读数、`OPP_DISCARD` 读对手） | 锁住新行为 |
| `test/.../TestMain.java` | 注册上述测试 | — |

### 1.3 🔴 我修掉的阻断级 bug（Codex 请重点复核）

**症状**：Java 侧 `CardLibrary.byId` 只有 **6 张卡**（只有 `neutral.json`），四个阵营的 84 张卡**一张都不存在**，且只留一行乱码警告。

**根因**：`VictoryConditionRegistry.install()` 只在 `Game.start()` 里调用，但**卡库是在 `Game` 构造之前加载的**（`CardLibrary.load → CardDef.fromMap → VictoryObjective.of`）。任何声明了 `leaderDef.victory` 的统领在解析时抛「未登记的胜利条件读数」，**整个阵营文件被静默跳过**。

**修复**：把登记变成"取用前前置条件"——`VictoryObjective.ensureInstalled()`，在两个入口先调用。幂等。

**验证**：`byId` 6 → **91**。

**这是我这轮最有价值的发现**：在此之前，**Java 引擎从未成功加载过任何阵营卡牌**。所有基于 Java 的对局测量在此之前的都是无效的。

---

## 二、被阻塞项（需要 owner 一次管理员操作）

**C# 无法重新编译**：本机 .NET SDK 8.0.425 缺两个 workload locator 目录
（`Microsoft.NET.SDK.WorkloadAutoImportPropsLocator`、`Microsoft.NET.SDK.WorkloadManifestTargetsLocator`），
msbuild 的 `DefaultSdkResolver` 报**硬错误** `MSB4276`，而 `DominionWars.Engine.csproj` 设了
`TreatWarningsAsErrors=true` ⇒ 所有构建失败。

**已排除的伪因**：
- 不是 `TreatWarningsAsErrors` 的锅——我实测过 `-p:MSBuildTreatWarningsAsErrors=false` 等 **5 种**命令行抑制，**全部无效**（MSB4276 由 SDK 解析器直接发出，不走普通警告机制）
- 也不是我最初判断的"缺目录所以补空壳"——日志显示 **workload 解析器已成功解析**这两个 SDK（返回 `null` = 无 workload props，正是无 workload 时的正确结果），**它们本就不以文件形式存在**

**正确修法**（安装包已缓存在本机，可离线）：
```powershell
& "C:\ProgramData\Package Cache\{ab39b1d0-4847-4c0d-a1c1-b2be697f2b76}\dotnet-sdk-8.0.425-win-x64.exe" /repair
```

**另有一条独立的离线问题**：本机联网被拦（nuget.org SSL 握手失败，`dotnet.microsoft.com`/阿里云/腾讯云镜像均不可达），
而 **`NuGetAudit` 自 .NET 8 起默认开启、需联网下载漏洞库** ⇒ 报 `NU1900` ⇒ 因"警告当错误"而还原失败。
**这是离线环境该有的正确配置**，建议在 `Directory.Build.props` 加 `<NuGetAudit>false</NuGetAudit>`（我已实测可让还原 exit 0）。

---

## 三、需要 Codex 处理的一件事：一条测试断言现在会失败

```
FAILED: DominionWars.Engine.Tests.DataLoaderTests.LoadsAllNinetyOneCards
    Expected: 91 cards   But was: 152
```

**原因是我造成的**：我把实验卡牌灌进了 `data/cards/wood.json`（21 → 82），卡池变 152。
**我已回滚**：`data/cards/wood.json` 恢复为 21 张，实验卡移到 `build-output/wl/wood.json`（不进生产）。

⇒ **回滚后这条断言应恢复通过**，但我**没有擅自修改测试断言**（测试属于 Codex/QA 范围）。
**如果卡池最终确定要扩到 100 张，这条断言需要按最终卡数更新——请 Codex 定。**

**我保留的 `data/` 改动只有一处**：`data/cards/sea.json` 的 `sea_leader.text`
（原文写「赋予你25点生命（**归零落败**）」，但"生命归零判负"这条规则 **2026-09-12 已按 owner 裁定删除**，
卡面在陈述一条不存在的规则）。已改为「开启25点生命池（生命池不构成胜负条件，见 RULES §0）」。

---

## 四、按 owner 要求做的**规则合规自检**

owner 提醒："检查有没有之前要求修正的结果你还是按照旧的规则写的内容"。结果：

| 检查项 | 结果 |
|---|---|
| 旧规则「生命归零判负 / 归零落败」残留 | ✅ **无残留**。`sea.json` 已修；其余命中全在勘误文档里**引用旧规则原文**（`PL_RULES_ERRATA_2026-09-12.md`、`PL_AI_VALIDATION_ACCEPTANCE_2026-09-12.md`），属应有内容 |
| 无行为关键词（秒杀/沉默/寄生/占星/震慑…）被当有效字段用 | ✅ 未使用。我的卡表里只用了有行为的 4 个（嘲讽/圣盾/突袭/扰魔） |
| 用 `guard` 当"随从保镖" | ✅ 已纠正。`guard` **只对非随从统领生效**；随从保镖走 `keywords:["嘲讽"]` |
| 专属 tag 预算 | ⚠️ **我先前的结论作废**。`RULES §13.5` 说的是「**每阵营 1-2 张超模卡**」，**不是 tag 预算**。"专属 tag ≤3" 是**我自己**写进规范的，不是 RULES 规定。按我规范的真实要求（新 tag ≤5 个、每个 ≥2 张），实际 **5 个新 tag、最少 4 张/个 ⇒ 合规** |
| 两引擎一致性 | ❌ **未验证，且仓库里没有这道测试**。详见 `docs/PL_JAVA_CSHARP_PARITY_GAP_2026-09-13.md` |

---

## 五、产物清单

**文档**
- `docs/PL_WOOD_POOL100_SPEC_2026-09-13.md` —— 100 张卡池设计规范（硬约束：合法动作/target/条件、tag 规则、成长数学）
- `docs/work/WOOD_A_GROWTH.md`（34 张）/ `WOOD_B_TECH.md`（33 张，含 132 格针对矩阵）/ `WOOD_C_DIVERSITY.md`（33 张）
- `docs/work/cards_A.json`（35 张）/ `cards_B.json`（32 张）—— 设计稿 JSON
- `docs/PL_JAVA_CSHARP_PARITY_GAP_2026-09-13.md` —— 双引擎一致性缺口（诚实记录）
- `docs/PL_CSHARP_BUILD_ROOTCAUSE_2026-09-13.md` —— 构建根因与两种修法
- `docs/PL_DECK_REDESIGN_V2_2026-09-13.md` / `PL_WOOD_REDESIGN_V4_2026-09-13.md` / `PL_WOOD_SPECIAL_EFFECTS_V6_2026-09-13.md`
- `docs/PL_ENGINE_DIVERGENCE_2026-09-13.md` —— 四处双引擎缺陷 + 修法

**工具（`build-output/`）**
- **`PoolContractValidator.java`** —— 入库闸门，见 §6
- `WoodDesignProbe.java` —— 轴轨迹 + 用卡统计（回答"设计成不成立"）
- `SwapProbe.java` / `TechValueProbe.java` —— 逐卡互换 / 针对卡价值矩阵
- `WatchOneGame.java` —— 逐回合打印一局，用于人眼验证
- `convert-cards3.ps1` / `remap-keys.ps1` —— 文档→JSON 转换（**有坑，见 §6**）
- `build-output/wl/` —— 实验卡池（82 张：21 基线 + 61 新，**不进生产**）

---

## 六、⚠️ 给 Codex 的警告：这条流水线上出现过 **6 个静默数据错误**

全部是"文档说 A、实际是 B"且**不报错**的类型。我把它写成了校验器规则：

| # | 错误 | 校验器规则 |
|---|---|---|
| 1 | 正则吞效果文本，`amount` 丢失变 0 | 需要 amount 的动作 amount==0 ⇒ **错误** |
| 2 | 卡面 `text` 被写成阵营名（`"烈焰"`） | text ∈ 阵营名 ⇒ **错误** |
| 3 | `param` 被写成 `"param=嘲讽"` | param 含 `=` 或以破折号开头 ⇒ **错误** |
| 4 | 读表格列索引错位，把 `targetFaction` 当卡面文本 | 卡面含「若」但效果无 condition ⇒ 警告 |
| **5** | **数据用 `effects` 键，引擎读 `onPlayEffects`** | **`type != MINION` 却零效果 ⇒ 错误**（若数据用了错误键名，整个效果列表会被当未知字段丢弃，卡"能打出但什么都不做"） |
| 6 | `ConvertTo-Json` 把缺 target 序列化成 `"NONE"` | 引擎的 `EffectSpec` 默认就是 `NONE`，那是**哨兵值不是非法值**（我第一版校验器误报 37 处） |

**`PoolContractValidator` 自己也出过 2 次误判**（把哨兵值当非法、把基线批次当 id 冲突）——
**它必须被质疑和测试，不能盲信**。

**强烈建议**：卡牌数据**直接手写 JSON**，不要经过 markdown→JSON 转换。设计文档适合人读，
但作为机器输入不可靠（单元格里有 `<br>`、行会跨行、列数不齐）。

---

## 七、我未完成、需要接手的部分

1. **新卡的实测数字一个都没有**（前几轮结论因错误 1 全部撤回）
2. **C 段两条线需重做**：设计者把 `woodSource` 与 `rawMode` 读成**串联**（"必须同时满足"），
   而源码是**并联**（`Combat.cs:168-172`：`(rawMode == "root" || woodSource)`）⇒ 古木来源的
   `param:"hp"` **一样会吃增幅并封印**，该段"先铺血再封印"的核心路线前提不成立
3. **B 段 6 张需引擎的卡**：3 个动作（`EMPTY_CLOUD_STACK`/`ENEMY_COMMIT_MINIONS`/`CONVERT_DISCARD_TO_GROWTH`）
   + 3 个条件（`OPP_ATTACKED_CASTLE_THIS_TURN`/`OPP_CLOUD_GE_n`/`OPP_CASTLE_HP_LE_n`）。
   其中 `OPP_CLOUD_GE_n`、`OPP_CASTLE_HP_LE_n` 的读数**都已存在**（`cloudStack`、`royalCastleHp`），成本低
4. **双引擎对照测试**（被 C# 构建阻塞）
5. **卡池最终规模与那条测试断言**（`LoadsAllNinetyOneCards`）
