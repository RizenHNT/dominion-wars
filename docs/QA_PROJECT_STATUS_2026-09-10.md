# QA 项目整体状态审计 — 2026-09-10

> 作者：DeepSeek（测试负责人）· 初始审计 2026-09-10 23:08–23:30；复核追加 2026-09-10 23:45；预言机与在飞复验追加 2026-09-11 00:06；跨预言机交叉验证追加 2026-09-11 00:12；进程取证更正 + 消融独立复核追加 2026-09-11 00:19；破城胜利五场景分歧 + `SimMain` 采样缺陷 + 规范路径基线追加 2026-09-11 00:26；**定稿复核（F17 撤回）+ `BALANCE.md` 陈旧基线（F21）追加 2026-09-11 00:34**；**PL 代码审核清单逐条复核（含 F24–F27）追加 2026-09-11 01:0x**
> 触发：owner 报“Codex 到本周额度限制”，要求检查项目整体状态
> 性质：只读审计。本报告不改任何生产文件。（唯一例外：§13.2 两处 test-only 断言修复已获 owner 授权）

---

## 0. 结论摘要

> ⚠️ **2026-09-11 00:34 定稿复核：读本表第 18 行与 §14 的 F17 行之前，请先读 §13.17。**
> **F17 作为"引擎缺陷"已整体撤回。** owner 2026-09-11 的表态（「不建议内置写死 分开吧 别的随从首领有别的获胜方式呢」「alpha 现在已经在改了……**所以不需要破城**」「**当然 A，不然大家都不打王城了**」）确认了"**主动破城必须得到奖励**"这一优先级。据此重放五场景：「双方均随从、仅防守方持有」的**应然结果就是破城方胜**，C# `EffectRuntime.State.cs:200-207` 的**结果正确**；`EffectRuntimeTests.cs:604-642` 那条用例**编码的正是定稿规则，应保留而非改写**；§13.14-⑤ 的**方案 (a)"收窄 `:138`"作废**。
> ⇒ 本项的剩余工作由"**改引擎行为（等定稿）**"变为"**改 `RULES.md:105/:137/:138` 的文本（PL/owner）+ 修 F18（Codex）+ 补一格覆盖用例（Codex）**"。**F18 维持、F19 转为文本修正、F20 维持**；新增 **F21**（`docs/BALANCE.md` 的平衡基线与复现命令早已失效，见 §13.18）。

| # | 严重度 | 结论 |
|---|---|---|
| 1 | ~~P1~~ **已修复** | 当前工作树 **测试为红**：`.NET Debug 601/603`（2 失败），全部由卡牌设计线改写 `data/cards/*.json` 后未同步测试断言导致。**⇒ 已于 §13.2 修复，§13.1 全门禁转绿（603/603）。** |
| 2 | ~~P1~~ **已处理** | **4 天无提交**。`HEAD` 仍是 `880250c`（2026-09-06），Codex 09-08/09-09/09-10 全天成果 + PL 卡牌落地全部停留在未提交脏工作树，**无 commit 快照可复现**。**⇒ 已建 checkpoint 提交 `8bc0515`（§13.5）。** |
| 3 | **P1** | Unity 侧存在**同源未验证风险**：Codex 的 `EditMode 306/306` / `PlayMode 26/26` 是**卡牌落地前**的读数，落地后**尚未复测**。（PL 点名担心的“Unity 机械 fixture commit 值”已在 §11 扫描确认为内联合成数据，**不含连带失败**；残余风险＝“从未复跑”本身。） |
| 4 | **P2** | CPU 对手 `Halted` / `LastReasonKey` 从不外露、不写日志：AI 一旦 halted，玩家只看到“CPU 不动了”，无任何提示或诊断。 |
| 5 | **P2** | QA-only 的 Unity 静态编译桩缺 2 个模块引用（`ImageConversion` / `ScreenCapture`），产生 2 个假 error，永久污染静态检查信号。 |
| 6 | ⚪ | `RuntimeAiTurnCoordinator.Reset()` 为死代码（UI 从不调用，改用置空重建）。 |
| 7 | **P0** | **（§13.10 追加）** C# 发布路径的 AI（`RuntimeAiPolicy.cs`）**完全没有生命周期策略** ⇒ 机械的胜利条件在 Unity 里不可能达成（600 局 0 胜）。**改数值无效。** §13.12.2-1 以第二套预言机证实"差异 = 有没有人替机械按下下载键"。 |
| 8 | **P1** | **（§13.10 追加）** 生产 AI 下烈焰 **91.0%**，其中 **273/600 局由 `win.royal_castle_break` 直接裁决**；关闭王城后降到 59.3%（唯一变量实验）。代码与 `docs/RULES.md:105` 一致 ⇒ 是**平衡/语义问题**，不是实现缺陷。 |
| 9 | **P1** | **（§13.10 追加）** 下载轴一旦被追求，机械 **95.7%**（`winParam = 6` 过廉价）；生产 AI 600 局只选 `PULL` 91 次 ⇒ "机械很弱"的方向是反的。 |
| 10 | **P1** | **（§13.11 追加）** **两引擎规则分歧**：`ROYAL_CASTLE_BREAK` 在 C# 是"持有者胜，不问谁破城"（合 `RULES.md:105/137`），在 Java `Game.java:593-604` 只判破城方 ⇒ 同一枚举语义相反。 |
| 11 | **P1** | **（§13.11 追加）** 跨引擎一致的唯一平衡结论：**深海偏强**（Java 80.3% / C# 74.0%，均超 40–60% 目标带）。§13.12.1 由 PL 独立预言机二次确认（84.4%）。 |
| 12 | 正向 | **（§13.11 追加）** 在飞的 Java 移植使 Java 平均回合由 25.0 回到 **14.79**（落在 10–20 目标带），测试数 38 → **59**，编译干净。**归因已更正为 harness 会话而非 Codex，见 §13.13。** |
| 13 | **P1（机制）** | **（§13.12 追加）** 古木封印机制自相矛盾：`EffectRuntime.Combat.cs:163-173` 的 `woodSource` **阵营**开关令每一张古木 buff 都成为增幅 ⇒ `:192-197` 封印并清零攻击，而 `AttackTargetPolicy.cs:17` 规定封印单位不能攻击。叠加 `wood_leader` 登场即 `ADD_RAMPANT 1`，**古木身边不存在"普通 buff"** ⇒ 512 轴实质不可用。**是机制问题，调数值无效。** |
| 14 | **P2** | **（§13.12 追加）** `SET_AMBUSH` 在 `手牌−1 < punish` 时被广告但原子上不可满足（"广告 ⇒ 可解"契约违规）；有 `SKIP_AMBUSH` 恢复路径，故非死锁。精确位置 `TurnFlow.cs:204-213` vs `AmbushActionHandler.cs:126-136`。 |
| 15 | 工具 | **（§13.12 追加）** 现有**两套**独立 C# 权威预言机（我的仓库外 `dw-cs-sim` + PL 的 `build-output/pl-csim`，后者已 gitignore）。两套交叉验证后的**唯一共同结论是"深海偏强"**；其余差异均由 harness 策略差异解释。 |
| 16 | 澄清 | **（§13.13 追加）** **归因更正：仓库的实时写入者是 `dsh`（DeepSeek Harness）进程，不是 Codex。** `codex.exe`（PID 29668）是桌面应用的常驻 `app-server`，实测 20 秒 CPU 增量 **0.00s**（完全空闲），子进程里没有任何 `codex exec` 回合进程。§13.4/§13.9/§13.11 中"Codex 在飞"的措辞系**未经证实的推测**，现已由进程取证推翻。 |
| 17 | **P1** | **（§13.13 追加）** 取证方法已用对照实验校验：`.文件.PID.guid.tmpdir\文件.tmp` 这一原子写模式是 **harness 独有**（我自己的 `create`/`edit` 写入只落最终文件、不产生 `.tmpdir`）；嵌入的 PID **就是写入进程**。100 秒窗口内 PID **21148** 同时写入 `src\main\java\...\Game.java` 与 **`build-output\pl-csim\SUMMARY.md`**（PL 自己的预言机目录）⇒ 写入者是**harness 上正在跑 PL 线的那条 DeepSeek 会话**。 |
| 18 | ~~P1（规范冲突）~~ **⇒ 已撤回** | **（§13.14 追加；⚠️ 结论已由 §13.17 整体撤回——见该节）** **破城胜利条件在"双随从 + 仅防守方持有"时归属冲突**：C# `EffectRuntime.State.cs:200-207` 的"双方统领都是随从 ⇒ 破城方胜"预判**不要求任何一方持有** `ROYAL_CASTLE_BREAK`，在"仅防守方持有"时会**抢走防守方的被动胜利**。**⚠️ 定性：C# 忠实实现了 `RULES.md:138` 的*字面*文本，根因是 `:138`（字面）与 `:105/:137`（不问谁破城）互相矛盾 ⇒ 规范文本缺陷（= F19），不是 C# 写错代码。定稿前不要动 C#。** 该分支**已实证可达**（变体 B：`win.castle_break_minion`×9，前提是机械方晋升 `machine_alpha`），但 3 000 局内未造成可观测偏差。 |
| 19 | **P2（潜伏）** | **（§13.14 追加；⚠️ §13.17 确认维持有效，并明确 `:200-207` 不得改动）** 同一函数**反方向的过窄**：`:214-217` 在"双方均持有"时 `return`（无人获胜），与 `RULES.md:138`「破城方胜」相反。**⚠️ 这一处与定稿无关、可无条件判定为缺陷**（`:138` 在"双方均持有"上没有歧义，Java 亦正确）⇒ **可立刻修**。今天不可达（仅 `flame_leader` 持有），**下一个持有该条件的统领落地即暴露**。⇒ C# 与 Java 在第 4/5 行场景上**恰好互换**。 |
| 20 | **P1（规范）** | **（§13.14 追加；⚠️ §13.17 更新其性质：owner 意图已明确 ⇒ 本条由"需裁决"转为"需文本改写"）** `RULES.md:138` **文本自相矛盾**：触发条件写"双方统领均为随从型"，理由写"避免**双方条件同时满足**时产生平局"。既有测试 `EffectRuntimeTests.cs:604-642`（双方**都不持有**却断言破城方胜）固化了比理由更宽的行为。**需 PL/owner 一句话定稿**：双随从但只有一方持有 ⇒ 持有者胜，还是破城方胜？ |
| 21 | **P2（测试台）** | **（§13.15 追加）** **`SimMain` 读数强烈依赖 `N`**：同一确定性构建下机械 N=20 **19.2%** vs N=300 **13.2%**；因 `seed = a*1000+b*100+k` 使样本**嵌套**，低 N 是**偏置早期分块**而非随机子样本（平均回合则对 N 不敏感，14.76–15.13）。另因 `k` 步长上限 100，**N>100 时种子跨对局重叠**（N=300：1 200/2 200 被共享）⇒ 采样非 i.i.d.。**勿引用 `SimMain < 200` 的阵营胜率。** |
| 22 | 正向 | **（§13.16 追加；⚠️ 读数性质已由 §13.19 限定）** 写入方停止后，**规范路径**（`scripts\build.bat` → `build\classes`）基线全绿：`build` exit 0、`TestMain` **59/59**、`SimMain 300`（3 600 局）平均回合 **14.79**（带内）。与仓外编译**逐位一致** ⇒ `build\classes` 未被写坏，**合并前基线"Java 侧"一项完成**；**F8 关闭**。**⚠️ 但这三组数字描述的是\*工作树\*（含 1 402 行未提交改动）；提交态 `HEAD` 独立重建实测为 `build` exit 0、`TestMain` **38/38**、平均回合 **22.61**（**带外**）——见 §13.19 / F22。** |
| 23 | ~~P1~~ **⇒ 已撤回** | **（§13.17 追加）** **F17（破城胜利在"双随从 + 仅防守方持有"时归属冲突）作为引擎缺陷整体撤回**：owner 2026-09-11 的表态（「不然大家都不打王城了」+「alpha……所以不需要破城」）确认"主动破城必须得到奖励" ⇒ 该场景的**应然结果就是破城方胜**，C# `EffectRuntime.State.cs:200-207` **结果正确**，既有测试 `EffectRuntimeTests.cs:604-642` **编码的正是定稿规则、应保留**；§13.14-⑤ 的**方案 (a) 作废**。本项转为：**改 `RULES.md:105/:137/:138` 文本（PL/owner）+ 修 F18 + 补一格覆盖用例（Codex）**。 |
| 24 | **P2（文档）** | **（§13.18 追加）** **`docs/BALANCE.md` 的平衡基线与复现命令早已失效（F21）**：`:3-10` 标"最新模拟数据"却**复现不出**——用 `:18` 自己给的 `SimMain 8` 实测机械 **18.8%**、深海 **79.2%**，与表中 52.1% / 41.7% 相差 33.3 / 37.5 pts；`:13`「所有阵营胜率落在 40%–60% 带内」**部分失效**（机械 18.8% 与深海 79.2% 越界，烈焰 45.8% 与古木 56.3% 仍在带内）；`:18` 用 `SimMain 8` 而 `:46` 要求「`SimMain 30` 以上」⇒ **同文档自相矛盾且两者都不够**；`:21` 把系统性偏置误称为"方差"；`:73` 的「35/35」现为 **38/38（提交态）/ 59/59（工作树）**——**两个修订版都不是 35/35**。同一失效命令复制到 `docs/DESIGN.md:91`。 |
| 25 | **P1（流程 / 数据丢失）** | **（§13.19 追加）** **`HEAD` 与工作树是两个不同的引擎（F22）**：工作树里 `src/main/java/**` 9 个文件 + `TestMain.java` 共 **1 402 行插入未提交**，且**没有任何 ref / 分支 / stash 包含它们**。用 `git archive HEAD` 在仓外独立重建的**提交态**实测：`TestMain` **38/38**、`SimMain 300` 平均回合 **22.61**（**10–20 带外**）、深海 **90.9%**、古木 **35.7%**（均在 40–60 带外）；而工作树是 59/59、14.79、80.3%、55.3%（三项回到带附近）。⇒ **按 `HEAD` 评估合并会得到更差的基线**；同时任何 `checkout`/`reset --hard`/`clean -fd` 会静默销毁这 1 402 行。 |
| 26 | 正向 + **P2/P3（报表）** | **（§13.20 追加）** **PL 的 C# 权威实测已被我独立复现**：从 **HEAD 源码重建 dll** 后覆盖 harness 副本，重跑全部 **14 组配置 / 10 080 局**，得**逐字段 0 差异**（config A 39 字段、其余各 50 字段），种子 **720/720 全唯一**（对照 F20 无采样偏置）、关键计数逐项对上 ⇒ **PL 的 C# 数字有修订版锚点、可复现**（§13.19 的"无锚点"只属于我先前的 Java 读数）。同时发现 **F23**：报告 §3 的链深直方图是**两件仪器逐档相加**（108 730 = 事件 48 060 + 决策 60 670 = 链数 7 541 的 **14.4 倍**）、§5 表 10.61 应为 **10.56**、§4"平均回合"列实为**烈焰的 `avgTurns`**、§4 清深海行 23.11 与产物 23.06 不符——**四处都不改变报告结论方向**。 |

| 27 | **P0×2 / P1×4（复核裁决）** | **（§13.21 追加）** **逐条复核 PL 的代码审核清单**（`docs/PL_CODE_AUDIT_2026-09-09.md`，110 行）：基线 **603/603 全绿**（`dotnet test … -c Release`，当前修订版）。**仍成立**：P0-1（先驱惩罚缺失，且 C# **只做了手牌上限那一半**）、P0-2（`AMBUSH_TRIGGER_WIN` **在 schema enum 里、引擎永不执行**）、P1-1/2/3、P2-2/3/8/9/10、§六"另注"（**升格 P0**：Unity 侧**没有任何"发动惩罚"的合法动作**，`LegalActionGenerator` 中 `punish` 零命中，而 Java `WebHumanAgent.askActivatePunish` 有完整人工路径）。**已过期/误读（建议删除）**：**P0-4 主体**（`EffectRuntime.Cards.cs:514` 确实读取阈值 ⇒ 破城方**只差 1 次循环即胜**，不是死局）、**D-1**（与 `RULES.md:125-126` 方向一致，无歧义）、**D-2**（已由 `:128` 定稿）。**需改表述**：P0-3（已修复 + 已有 `EffectChainTests.cs:38-61`，仅缺两段 AOE 用例）、P2-1（降 P3，两端玩法都有 `Math.max(0,…)`）、P2-4、P2-7、**P2-11**（死分支存在但清单举错了条件名）。**新增 F24–F27**：**F24 C# 完全不读 `data/balance.json`**（全参数硬编码 ⇒ 改平衡文件对 Unity 侧无效，P0-1 只是其一个实例；**已用四臂对照实测证明**：改该文件 C# 输出 SHA 不变、改一张卡即变，见 §13.22）；**F25 快照 wire format 无契约测试**（canonical `game_snapshot.schema.json` **从未被任何 C# 测试引用**，legacy `SnapshotDto` 结构上不可能通过却仍在仓库）；**F26 v1.31 事件契约有校验器却无生产者**（引擎发 **46** 种事件类型 / **51** 种载荷键，契约只收 **29**/**7**，`RuntimeEventEnvelope` 只在测试中构造）；**F27** 澄清（`RuntimeEventCursor` 的校验质量高于清单假设） |
| 28 | **P1（契约 / 新发现）** | **（§13.23 追加）** **canonical v1.31 投影会发布违反自身 schema 的负 `currentHealth`（F28）**：`EffectRuntime.Combat.cs:333`（`DamageCard`）**不夹零**，而唯一 canonical 生产者 `RuntimeContractV131Snapshot.cs:160`（`CurrentHealth = card.Health`）**原样搬运** ⇒ 任意**溢出击杀**（伤害 > 剩余血量）使墓场卡 `currentHealth` 为负，违反 `schemas/game_snapshot.schema.json:57` 的 `minimum: 0`，而契约 `:74` 规定无效消息 **fail-closed**；同时契约 `:77` 又要求投影**必须直投**权威血量 ⇒ 二者不可同时满足。**治疗组 vs 对照组实证**：S6 施加 `DAMAGE ALL_ENEMY_MINIONS 5` 后 schema **2 errors**（−2 / −4），S7 同工作台不施加效果 **0 errors**；JSON 由仓库自身 `RuntimeWireSerializer` 生成。**只在 v1.31 成立**（v1.30 的 schema 无 health 约束）。**与 F25 硬耦合**：F25 的"用 schema 校验生产快照"这条测试**今天实现就会红** |
| 29 | **正向 + P3（报表/复现）** | **（§13.25 追加）** **owner 四条破城/下载轴决定在 C# 侧逐层落地**（① 持有者防守方被动胜、② 无 blanket 硬编码、③ `PULL_TOTAL_GE` 在数据/schema/C#/Java/投影五层齐、④ 镜像破城方胜），由 **63/63** 定向用例守护（`EffectRuntimeTests` 5 条破城 + `ProductionFactionIntegrationTests.cs:226`）。**F7 可达性上修**（`flame_leader`×`machine_alpha` 镜像即可触发，Java 缺两格）；**F18 括注收紧**（"Java 亦正确"只在该场景成立）；**决策台账 4a 过时更正**（"叫出破城方自己首领"已被 2026-08-23 裁决取代为"叫出对方首领"）；**§0 数据基线漂移定位**：`sea.json` 记录的 `4C86A410D0BD` 不可复现，实际为 `0FA7E4E33425`（`8bc0515` 引入），根因是哈希快照取在 harness 逐文件改写的中间（`23:18:00`–`23:37:45`），**非笔误** |
| 30 | **正向 + P2（工程卫生）** | **（§13.26 / §13.27 追加）** **工作树合并候选的独立再验证**：Java `TestMain` **59/59**、`SimMain 300` 平均回合 **14.793611**、烈焰 51.2 / 机械 13.2 / 深海 80.3 / 古木 55.3 %，且**同命令两次运行输出 SHA256 完全相同**（`D950F3B6…`，单次约 7 s）⇒ Java 测试台**完全确定性**、可作廉价门禁；C# 侧 `dotnet test -c Release` **603/603**。**同时新发现 F29**：`src\Adapters\Ai\AdvertisedActionPolicy.cs`（10 408 B / 242 行，mtime `2026-09-11 01:49:06`）是**未跟踪**文件，却**已被两个构建系统编译**（SDK 默认通配 + Unity 本地包 `com.dominionwars.adapters` 与 `autoReferenced` 的 asmdef）、**零引用、零覆盖**，且内容是 **F3 的未交付修复** + 同一 AI 策略的**第三份**实现（`PULL/COMMIT/CloudStack` 关键字 12 : 0）⇒ 需 Codex 认领（采纳单源化 + 补用例，或移除）；**F30**（P3）：`.gitignore` 未覆盖 `Assets\QA\` 与 `InitTestScene*.unity` 生成物 |

- 分支：`codex/p0-complete-match-loop-2026-09-06`（`main` 停 `6c9ef65`，自身**领先 `origin/main` 44 个提交**；全部未 push；F28 实证提交为 `ce04ccc`，**本报告最新提交号以文末签署行为准**）
- .NET SDK：8.0.425
- 数据快照：审计时刻 `machine.json` SHA256 前缀 `A7AD0E6F997E`、`wood.json` `240C42D6DAA1`、`sea.json` **`0FA7E4E33425`**（**2026-09-11 01:5x 更正**：原记 `4C86A410D0BD` 属于写入方改写过程中的中间态、**不可复现**；现值为 `HEAD` blob = 工作树，`git status/diff -- data/` 均为空，根因见 §13.25.6）、`flame.json` `2740725CA701`、`neutral.json` `CAE381377FBD`（其余四项经 `git cat-file blob HEAD:…` 重算 **= `HEAD` = 工作树**，仍有效）

> ⚠️ 并发写入：本仓库当前**有另一个写入者在活动**（owner 说明为 harness 上的 DeepSeek 卡牌设计线）。审计期间观测到 `data/cards/*.json` 于 23:10:40–23:11:15 与 23:18:00 被写入。审计结论绑定上述哈希。

---

## 2. 数字门禁实测

### 2.1 通过项（本轮实测）

| 门禁 | 命令 | 结果 |
|---|---|---|
| 卡牌 schema | `.\scripts\validate-cards.ps1` | `SCHEMA_VALIDATION pass=91 fail=0 files=5` |
| 牌组 | `.\scripts\validate-decks.ps1` | `DECK_VALIDATION pass=4 fail=0 files=4 cards=91` |
| 空白差异 | `git diff --check` | PASS |
| 冲突标记 | 全仓库扫描 | 无 |

### 2.2 **失败项（本轮实测，P1）**

命令：

```
dotnet test DominionWars.sln -c Debug -p:MSBuildEnableWorkloadResolver=false --nologo
```

结果：

```
失败!  - 失败: 2，通过: 601，已跳过: 0，总计: 603
```

失败用例：

1. `FormalOrdinaryMachineMinionsHaveTheApprovedLifecycleDefaults`
   （`src/Engine/Tests/DataLoaderTests.cs:59-86`）
2. `ProductionMachineDeckCompletesCommitPushPullLandmarkAndAlphaVictory`
   （`src/Engine/Tests/ProductionFactionIntegrationTests.cs:377-400`，断言点 `:390`）

### 2.3 本轮**未能执行**（环境阻塞，非失败）

本会话的 PowerShell 宿主在 23:19 之后持续返回 `shell context is being reconfigured`，跨 20+ 次直接调用（含 `list_powershell` 本身、不同 `shellId`）与 1 个独立子代理均零输出。**根因判定**：这是**宿主层**整体不可用（连 `list_powershell` 都失败），发生在 owner 于本会话中开启**终端沙盒化 / Terminal Sandboxing** 之后 → 工具宿主在重配置 shell 上下文，未恢复。**恢复方式**：关闭（或保持）该设置后**重载 VS Code 窗口 / 重启本会话**。这与仓库代码无关。以下门禁**本轮无法复跑**：

- `.\scripts\validate-design-manifest.ps1`（本轮更早时段为 `pass=1 rows=320 files=320`）
- `java -cp "build/classes;build/test-classes" com.dominionwars.test.TestMain`（本轮更早时段为 **38/38**）
- `com.dominionwars.test.SimMain 300`（阵营胜率基线，**本轮未取得数字**）
- Unity EditMode / PlayMode（另受 connected Editor 项目锁限制）

---

## 3. 两个失败的根因（已定位到具体数据字段）

两处失败**均非引擎缺陷**，而是测试断言停留在“落地前的统一默认值”。

### 3.1 `FormalOrdinaryMachineMinionsHaveTheApprovedLifecycleDefaults`

该测试对 8 张普通机械随从硬断言 `CommitCost==1 && UploadCost==0 && DownloadCost==1`。

落地后 `data/cards/machine.json` 的实际值：

| 卡 | commitCost | uploadCost | downloadCost |
|---|---|---|---|
| machine_drone | 1 | 0 | 1 |
| machine_golem | **2** | 0 | 1 |
| machine_wall | **2** | 0 | 1 |
| machine_blaster | **3** | 0 | 1 |
| machine_titan | **3** | 0 | **2** |
| machine_spark | 1 | 0 | 1 |
| machine_assembler | 1 | 0 | 1 |
| machine_recycler | 1 | 0 | 1 |

→ 5 张与断言冲突。`PullEffects`（`BUFF/FRIENDLY_MINION/1/both`）仍全部一致，未冲突。

### 3.2 `ProductionMachineDeckCompletesCommitPushPullLandmarkAndAlphaVictory`

测试在 `:328` 把 `machine_blaster / machine_titan / machine_recycler / machine_assembler` 压入云栈并全部 PULL，随后在 `:390` 断言：

```csharp
Assert.That(pullDeclarations.All(item => item.Data["punish"]!.Equals(1)), Is.True);
```

PULL 的 `punish` 取自被下载卡的 `downloadCost`（`src/Engine/Turns/PullActionHandler.cs:195`）。`machine_titan` 的 `downloadCost` 由 `1` 改为 `2` → 该 PULL 的 `punish=2`，`:390` 断言失败。

> **更正（复核源码后）**：同处 `:391` 的 `PUNISH_DRAW == 8` **并未失配，仍然通过**。`EffectRuntime.Cards.cs:301-347` 的 `DrawCards` 无论 `amount` 多大，只在循环结束后 `Emit` **一次**（`:344`），幅度只写入 `count` 字段；因此 `PUNISH_DRAW` 事件数 = 惩罚结算次数 = 2 次 COMMIT + 6 次 PULL = **8**。`machine_titan` 的 `1→2` 只改变该事件的 `count` 载荷，不改变事件数量。
>
> 结论：该测试**只有 `:390` 一条断言失效**。初版报告把 `:391` 一并列为失配是不准确的，已按源码更正。

### 3.3 该失败**已被预测**

PL 在 `docs/AI_MAILBOX.md:440` 明确写下：

> ⚠️ 需 Codex 处理：`.NET 全量测试需在可运行环境重跑`（本会话 testhost 被沙箱拦）；可能受影响的断言见报告 §3（**ProductionFactionIntegrationTests / Unity 机械 fixture commit 值**）

Codex 在额度耗尽前未处理这 3 条断言；`docs/CODEX_AI_CLOSEOUT_REPORT_2026-09-08.md §21` 的 `603/603` 是**落地前**的数字。

### 3.4 规则一致性判断（重要）

`docs/RULES.md:268` 与 `:271` 均写明：

> 对正式 91 卡中**未单独声明** COMMIT/PUSH/PULL 数值或下载效果的普通机械随从，当前默认是 COMMIT=1、PUSH=0、PULL=1……**任何已明确的专属字段优先于该默认。后续新卡可例外。**

当前 8 张卡**都有显式专属字段**，因此**数据侧不违反规则书**，`1/0/1` 只是“未声明时的兜底默认”。→ 正确修复方向是**更新测试断言以反映显式值**，而不是回退数据。

> 附带澄清：PL 报告 D7 把“非 0 惩罚”列为偏离 `RULES.md:271` 的“全家 0 费”。实测 `punish`（出牌惩罚）在落地前后**完全一致**（golem/wall/blaster 均为 2、titan 5、spark 1、drone 0），**落地未引入该偏离**；这是更早批次遗留的规则书与数据口径差，不应记在本次落地账上。

---

## 4. 仓库洁净度

- `HEAD` = `880250c`（2026-09-06），此后 **0 次提交**。
- 脏条目数量级 ~110（Codex 自述 09-09 捕获为 107），含 80 个已跟踪文件改动与约 30 个未跟踪新增文件。
- 未跟踪新增文档：`PL_REPORT_2026-09-09.md`、`PL_CARD_DESIGN_LANDING_2026-09-09.md`、`CODEX_AI_CLOSEOUT_REPORT_2026-09-08.md`、`DESIGN_SEA_MACHINE_FINAL_2026-09-08.md`、`VISUAL_PRODUCTION_GOAL_2026-09-08.md`。
- 未跟踪新增代码：`RuntimeAiPolicy.cs`、`RuntimeAiTurnCoordinator.cs`、`RuntimeAttackDragArrow.cs`、`RuntimeAi*Tests.cs`、`Assets/QA/`、`InitTestScene*.unity`。
- `docs/CHANGELOG_CASTLE.md`（“completed changes only”）**未记录** 09-08 之后任何批次。

**风险**：Codex 休息 + PL 仍在写 data 的情况下，整个 4 天成果没有任何可 checkout 的快照。任何误操作（reset/clean/覆盖）都会不可恢复。这是当前**最高优先级的工程风险**，且已在 `PL_REPORT_2026-09-09.md` 中被 PL 独立指出。

---

## 5. Unity 侧未验证风险（P1）

- `docs/CODEX_AI_CLOSEOUT_REPORT_2026-09-08.md §21` 的 `EditMode 306/306`、`PlayMode 26/26`、`.NET Debug 603/603` 全部是**卡牌落地之前**的读数。
- PL 明确点名“Unity 机械 fixture commit 值”可能受影响（`AI_MAILBOX.md:440`）。
- 落地后 Unity 侧**没有任何一次复测证据**（新未跟踪测试 `RuntimeAiEditModeTests` / `RuntimeAiPlayModeTests` / `RuntimeAiIntegrationPlayModeTests` 同样未经落地后复测）。
- **部分解除（见 §11）**：`EditMode` 中所有涉及机械费用的 fixture（`RuntimeCardDisplayInspectEditModeTests.cs`、`Fixtures/PullLifecycleData/cards/fixture_cards.json`）用的是**内联/合成卡数据且显式写 0**，不读 `data/cards/*.json` → **不存在已知的连带失败**。因此 PL 点名的“Unity 机械 fixture commit 值”**确认安全**。
- 但 Unity 侧其余断言（卡面文本、卡数 91、木质效果）未纳入本轮穷举，且整套 EditMode/PlayMode 落地后**从未运行** → 该风险仍为**未验证**，不因 §11 而消除。
- 本轮**无法代跑**：connected Editor 持有项目锁，且本会话 PowerShell 宿主不可用。

→ 在恢复环境前，**不得**把 `306/306` 当作当前有效基线。

---

## 6. 新增 Unity AI 代码审查（只读）

文件：`RuntimeAiPolicy.cs`（154 行）、`RuntimeAiTurnCoordinator.cs`（157 行）、`RuntimeScreenFlow.cs`（642 行）。

**正面确认（符合 DAILY_GOAL 验收项）**：

- AI 只消费 `GetSnapshotForViewer(1)` 与快照自带 `LegalActions`；`ToGameAction` 逐字段复制广告动作，不追加目标/选择/规则推导值。
- 人类呈现固定 `_presentationViewerIndex = 0`。
- fail-closed 完整：终局（`ai.match_over`）、非我方回合（`ai.waiting_for_turn`）、无合法动作（`ai.no_legal_actions`）、重复动作（`ai.duplicate_action`）、拒绝（`ai.action_rejected`）、32 动作上限（`ai.action_limit_reached`）均停止且不自旋。
- 暂停门禁存在（`RuntimeScreenFlow.cs:487` `IsPauseMenuOpen` 时不再推进 CPU），异常路径 `Halt("ai.coordinator_failed")`。

**发现（P2）**：

1. `Halted` 与 `LastReasonKey` 在 `RuntimeScreenFlow.cs` 中**零引用**——既不上屏也不写 `Debug.Log`。AI 一旦 halted，玩家侧表现是“CPU 不动了”，无任何可读原因。
2. `RuntimeAiTurnCoordinator.Reset()`（`:133`）是**死代码**：UI 用置空 + 重建（`:269/:462/:535`）代替调用。
3. `Halted` 置位后**不会自动清除**：若 AI 在自身回合因 `ai.no_legal_actions` 停止，必须由 host 重建 coordinator 才能恢复。当前只能靠 `StopRuntimeSession`（回主菜单）重建 → 表现为该局 CPU 永久停摆。

---

## 7. QA 工具缺口（P2）

`build-output/unity-static-compile/DominionWars.Unity.StaticCompile.csproj`（被 gitignore，仅用于静态编译 `Assets/DominionWars.Runtime/*.cs` 与 `Assets/DominionWars.UI/*.cs`）只引用 5 个 Unity 模块，缺：

- `UnityEngine.ImageConversionModule.dll`（`RuntimeContentResolver.cs:463 Texture2D.LoadImage`）
- `UnityEngine.ScreenCaptureModule.dll`（`RuntimePlayerVisualSmoke.cs:395 ScreenCapture`）

两者在 `C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Data\Managed\UnityEngine\` 下确实存在。→ **非生产缺陷**，是静态检查永远报 2 个假 error 的工具缺口，会让真实回归被噪声掩盖。

---

## 8. 建议路由

| 项 | 建议 |
|---|---|
| §3 两处失效断言 | **test-only 修复**（更新为显式值 / 计数改为按数据推导）。属测试代码改动，需 owner 或 Codex 显式授权 test-only scope。 |
| §5 Unity 落地后复测 | 恢复 connected Unity 后串行复跑 EditMode/PlayMode，并把机器 fixture 与 `machine.json` 对齐。 |
| §4 无提交快照 | 请 owner 明确授权一次**本地 checkpoint commit**（仅纳入指定实现路径，不含 `Assets/QA/`、包锁、ProjectSettings 等代理脏项）；不做 push。 |
| §6 CPU halted 无提示 | 建议记为 P2 backlog：至少落一条 `Debug.LogWarning(LastReasonKey)`，上屏提示可留待 UI 批次。 |
| §7 静态编译桩 | 补 2 个模块引用（工具文件，非生产）。 |

---

## 9. 复现步骤

```powershell
cd C:\Users\USER\Documents\dominion-wars-win64
git rev-parse --short HEAD                     # 期望 880250c
dotnet test DominionWars.sln -c Debug -p:MSBuildEnableWorkloadResolver=false --nologo
# 期望（当前的坏状态）：失败 2 / 通过 601 / 总计 603
# 失败：FormalOrdinaryMachineMinionsHaveTheApprovedLifecycleDefaults
#       ProductionMachineDeckCompletesCommitPushPullLandmarkAndAlphaVictory
.\scripts\validate-cards.ps1                   # pass=91 fail=0
.\scripts\validate-decks.ps1                   # pass=4 fail=0 cards=91
```

---

## 10. 未完成项（下次接续）

- `.NET` 全绿复验（待 §3 修复后）。
- `SimMain 300` 阵营胜率基线（本轮未取得；旧基线 machine 16.3% 无存档佐证）。
- design manifest 320/320 与 Java 38/38 的落地后复跑。
- Unity EditMode/PlayMode 落地后复跑。
- `RULES.md:292`/`:296` 的“59 张主牌”与 `data/decks/*.json` 实际 60 张的差异：**经核查这是规则书自身记录的待迁移项**（`:292` 原文“……四套现有预构筑**仍须完成数据迁移**”），属已声明的 ⚪ 缺口，**不是新发现的缺陷**；测得的实际值为四套牌组各 20 种 ×3 = 60 张主牌。
- 古木 `ADD_ROOT` 落地后 512 轴是否在**真实对局可达性**（非手工 fixture）上成立。
- 落地新增时点效果（`machine_spark` commit DRAW 1、`machine_assembler` push DRAW 1、`machine_recycler` commit ROLLBACK 1）**当前零测试覆盖**（见 §11.2）。

---

## 11. 失效面全仓库扫描（2026-09-10 补充，只读）

为把待修复范围钉死，对全仓库做了硬编码旧机械 `1/0/1`、`punish`、以及 `machine_*` fixture 的扫描。

### 11.1 会因本次卡牌落地而失败（WILL BREAK）——精确 3 条断言

| 文件 | 测试 | 行 | 断言 | 实际冲突 |
|---|---|---|---|---|
| `src/Engine/Tests/DataLoaderTests.cs` | `FormalOrdinaryMachineMinionsHaveTheApprovedLifecycleDefaults` | `:71` | `CommitCost == 1` | golem/wall=2，blaster/titan=3 |
| 同上 | 同上 | `:73` | `DownloadCost == 1` | `machine_titan`=2 |
| `src/Engine/Tests/ProductionFactionIntegrationTests.cs` | `ProductionMachineDeckCompletesCommitPushPullLandmarkAndAlphaVictory` | `:390` | 所有 PULL `punish == 1` | titan PULL `punish=2` |

已确认**不受影响**的相邻断言：`DataLoaderTests.cs:66`（数量 8）、`:72`（upload==0）、`:74-77`（PULL 效果 1/FRIENDLY_MINION/1/both）、`:80-84`（alpha / leader 费用）；`ProductionFactionIntegrationTests.cs:274-277`、`:387`（首 PUSH `punish==0`）、`:391`（`PUNISH_DRAW==8`，见 §3.2 更正）。

### 11.2 安全（SAFE）——使用内联 / 合成卡数据，不读 `data/cards/*.json`

- 引擎侧：`DataLoaderTests.cs:30,42-44`（内联 JSON 练习默认值逻辑）、`CommitActionHandlerTests.cs:39,53,70,103,127,140`、`ContentCatalogTests.cs:186-224`、`PullActionHandlerTests.cs:265,317`、`AdvancedEffectTests.cs:327`、`AttackActionHandlerTests.cs:89`。
- Unity 侧：`RuntimeCardDisplayInspectEditModeTests.cs:160-162,189-191,284-286,303-308,339-341`、`Fixtures/PullLifecycleData/cards/fixture_cards.json:37-39,49-51`（显式写 0，不触发默认值）、`RuntimePullLifecycleFixtureEditModeTests.cs`（仅断言事件名/数量，不含费用）、`Tests.PlayMode/**`（无匹配）。
- `data/decks/*.json` 只引用卡 id，不含费用字段；`build-output/` 下无 `machine*.json` fixture。
- **口径提醒**：落地新增的三个时点效果**不被任何测试断言** → 既不会导致失败，也意味着**新机制处于零覆盖**。⚪ 建议 PL 决定是否立项补断言。
- **覆盖率说明**：本轮针对“费用 / punish / 机械 fixture / Unity 连带失败”做了穷举；Unity 侧其余（卡面文本、卡数 91、木质效果）不在本次扫描结论范围内，仍属 §5 的未验证区。

### 11.4 互补扫描（文本 / 卡数 / 海数值 / 木效果 / golden 文件）——同样 **0 处新增失效**

对落地批次其余改动做了第二遍穷举（**只读静态核查，非执行**）：

| 类别 | 结论 |
|---|---|
| 真实卡面文本（空/非空、精确串） | **无失败**。全仓库唯一断言真实文本的是 `DataLoaderTests.cs:154`（`machine_factory` = “召唤三个侦察机偶。”，`data/cards/machine.json:240` 一致）与 `RuntimeBootstrapEditModeTests.cs:62,65`（机械/古木 `winText`，一致）。**没有任何测试断言真实卡 `text` 为空或非空** → 本次补 20 张文本不会打破任何断言。 |
| 卡数 91 / 阵营卡数 | **无失败**。`DataLoaderTests.cs:24`（91）满足（实测 wood 21 + sea 21 + machine 22 + flame 21 + neutral 6 = 91）；`scripts/report-card-art-map.ps1:41`、`scripts/validate-decks.ps1:37`（4 套）亦满足。 |
| 海系数值 | **无失败**。`sea_leviathan_young` 与 `sea_devour` **全仓库无任何断言** → 5/6→4/6、punish 4→2 属**未被覆盖的改动**。`OpponentDiscardEffectsTests.cs:49-50`（`sea_siren` 5/6）满足。 |
| 古木 512 轴 | **无失败**。`DataLoaderTests.cs:29`（`wood_leader` 效果序列、`LeaderWinParam 512`）与 `ProductionFactionIntegrationTests.cs:77,134-135`（`RampantStacks==3`、血量 512）均满足；`wood_growth` 现值为 `ADD_RAMPANT 1` + `BUFF 2 param=rampant`，经 `EffectRuntime.Combat.cs:217-225` 的 ×8 叠乘可达 512。 |
| `ADD_ROOT` 是否被真实卡引用 | **已被真实卡引用**：`data/cards/wood.json` 命中行 `58,80,123,157,302,326,372,400,423,493`，确证落地生效；但**无测试断言真实卡上的 `ADD_ROOT`**（`WoodCounterEffectTests` 用合成状态）→ 又是**零覆盖**。 |
| golden / 快照 / 向量文件 | **不存在**。`data/**/*.json` 仅卡牌、牌组、`balance.json`、`ui.json`、schema；`data/balance.json` 不含任何卡 id/攻防/punish；`build-output/` 无 JSON；Unity 仅 3 个合成 fixture。唯一相关的是 `docs/test/sanity_v2_2026-08-12_1956.txt:11`（“总计: 91 张唯一卡牌”，仍成立，且不被 CI 执行）。 |

### 11.5 ⚪ 文档漂移（新发现，非阻塞）

`docs/effects.contract.md:27` 仍写着 `ADD_ROOT` **“尚未被 91 张运行时卡数据引用”** —— 本次落地后该句**已过时**（`wood.json` 现有 9 处引用）。建议由 PL/文档批次一并修正。


### 11.3 结论

修复面**精确为 2 个测试文件、3 条断言**，不存在更广的机械 fixture 雪崩，也**不存在 Unity 侧连带红**。§5 中 PL 担心的“Unity 机械 fixture commit 值”经实测为**内联合成数据，确认安全**；Unity 侧残余不确定性来自落地后**从未复跑**本身，而非已知 fixture 冲突。


---

## 12. 建议补丁（**提案，未实施**；待 owner / Codex 授权 test-only scope）

> 依据 §11：全仓库**只有 3 条断言**失效，且**判定为测试陈旧而非数据错误**（`RULES.md:268/:296`“已明确的逐卡值优先”）。以下为最小改动提案，供 Codex 直接采纳。

### 12.1 `src/Engine/Tests/DataLoaderTests.cs:66-78`

把“统一 1/0/1”断言改为“落地批准的逐卡显式值”。落在 `Assert.Multiple` 内的 4 行为例：

```csharp
// PL_CARD_DESIGN_LANDING_2026-09-09 §2.2（机械 M1）：逐卡生命周期费用已是显式值，
// 不再套用 RULES.md:268 的 1/0/1 兜底默认。
var approvedCommitCost = new Dictionary<string, int>
{
    ["machine_drone"] = 1, ["machine_golem"] = 2, ["machine_wall"] = 2,
    ["machine_blaster"] = 3, ["machine_titan"] = 3, ["machine_spark"] = 1,
    ["machine_assembler"] = 1, ["machine_recycler"] = 1,
};
var approvedDownloadCost = approvedCommitCost.ToDictionary(
    pair => pair.Key,
    pair => pair.Key == "machine_titan" ? 2 : 1);

Assert.Multiple(() =>
{
    foreach (var card in ordinaryMachineMinions)
    {
        Assert.That(card.CommitCost, Is.EqualTo(approvedCommitCost[card.Id]), card.Id);
        Assert.That(card.UploadCost, Is.Zero, card.Id);
        Assert.That(card.DownloadCost, Is.EqualTo(approvedDownloadCost[card.Id]), card.Id);
        // :74-77 的 PULL 效果断言保持不变（实测仍成立）
    }
    // :80-84 的 machine_alpha / machine_leader 断言保持不变
});
```

`DataLoaderTests.cs:66`（`Has.Length.EqualTo(8)`）与 `:72`（`UploadCost Is.Zero`）**无需改动**（已实测仍成立）。默认值逻辑本身已被 `ContentCatalogTests.cs:186-224` 的内联 JSON 用例覆盖，故此处不必保留“兜底默认”语义。

### 12.2 `src/Engine/Tests/ProductionFactionIntegrationTests.cs:390`

不要硬编码 `1`，改为**从被 PULL 的卡推导期望惩罚**（PULL 的 `punish` 定义就是该卡 `downloadCost`）。`ExecutePull` 循环（`:335-350`）里 `expectedTop` 就是该 `CardInstance`，顺手收集即可：

```csharp
var expectedPullPunish = new List<int>();
foreach (var expectedTop in machine.CloudStack.Reverse().ToArray())
{
    var pull = ExecutePull(state, flow, router, alpha, expectedTop, target);
    Assert.That(pull.Accepted, Is.True);
    expectedPullPunish.Add(expectedTop.Definition.DownloadCost);
    ...
}
// …并在 :390 处替换为：
Assert.That(
    pullDeclarations.Select(item => (int)item.Data["punish"]!).ToArray(),
    Is.EqualTo(new[] { 1, 1 }.Concat(expectedPullPunish).ToArray()));
```

期望序列（当前数据）＝`[1, 1, 1, 1, 2, 1]`（前两个是被提交的 pair，其后按 LIFO 顺序 assembler/recycler/titan/blaster）。
**Codex 落码前请复核两点**：① `Data["punish"]` 的装箱类型确实是 `int`（`PullActionHandler.cs:195` 传入 `int`，但事件经 `Data(...)` 后可能是 `long`，用 `Convert.ToInt32` 更稳）；② `expectedTop.Definition` 在被 PULL 后是否仍可读（同一实例引用，应可读）。
`:391` 的 `PUNISH_DRAW == 8` **无需改动**（见 §3.2 更正）。

### 12.3 验收判据

采纳后应得到：`.NET Debug` **603/603**（或在新增覆盖后 ≥ 603 且 0 失败）；`DataLoaderTests.cs:24`（91）与 §11.4 全部断言保持绿色。

### 12.4 建议一并考虑的**覆盖缺口**（⚪，非本批阻塞）

以下落地内容目前**零测试覆盖**，建议由 PL 决定是否立项补断言：

- `machine_spark` commitEffects `DRAW 1`、`machine_assembler` pushEffects `DRAW 1`、`machine_recycler` commitEffects `ROLLBACK 1`；
- 真实卡上的 `ADD_ROOT`（古木 512 轴）；
- `sea_leviathan_young` 4/6、`sea_devour` punish 2；
- 古木 512 轴的**真实对局可达性**（现有 512 断言是手工铺场 fixture，不代表对局中可达）。

## 13. 复核补充与 checkpoint 封版（2026-09-10 23:45 追加）

### 13.1 门禁全绿（本树复跑实测）

| 门禁 | 命令 | 结果 |
|---|---|---|
| 卡牌 schema | `.\scripts\validate-cards.ps1` | `pass=91 fail=0 files=5` ✅ |
| 牌组 | `.\scripts\validate-decks.ps1` | `pass=4 fail=0 cards=91`（四套各 60+1） ✅ |
| design manifest | `.\scripts\validate-design-manifest.ps1` | `pass=1 rows=320 files=320` ✅ |
| 对齐 | `python scripts/align_check.py` | 完成，无悬空 SUMMON ✅ |
| 数据完整性 | `python scripts/sanity_check_v2.py` | `0 ERROR / 0 WARN / 0 INFO` ✅ |
| .NET | `dotnet test DominionWars.sln -c Debug -p:MSBuildEnableWorkloadResolver=false --nologo` | **603/603 通过，0 失败** ✅ |
| Java 回归 | `java "-Dfile.encoding=UTF-8" -cp "build/classes;build/test-classes" com.dominionwars.test.TestMain` | **38/38** ✅ |

§3 记录的两处失败（601/603）已全部消除。§11.1 判定的"3 条失效断言"经落地后复核，实际需要改动的是 **2 条 + 1 处编译缺失**：见 §13.2。

### 13.2 本轮修复的测试代码缺陷（3 处，均已验证）

| # | 位置 | 缺陷 | 处置 |
|---|---|---|---|
| 1 | `src/Engine/Tests/DataLoaderTests.cs:2` | 新加的逐卡费用表使用 `Dictionary<,>` 但缺 `using System.Collections.Generic;` → `error CS0246`，**整个解决方案无法编译**（不只是该测试失败） | 补 using。修复后 603/603 |
| 2 | `src/Engine/Tests/DataLoaderTests.cs:70-80` | 原为"普通机械随从一律 1/0/1"的全局断言，与 M1 差异化（golem/wall=2、blaster/titan=3、titan download=2）冲突 | 已被改为逐卡显式值表（8 张齐全）。实测通过，与 `data/cards/machine.json` 一致 |
| 3 | `src/Engine/Tests/ProductionFactionIntegrationTests.cs:398` | 断言 `PUNISH_DRAW` 事件数 == **9**（"2 次 COMMIT + **7** 次 PULL"）——把"惩罚张数之和"当成了"事件条数" | 改为 **8**（2 次 COMMIT + 6 次 PULL），并加注释说明语义 |

**Class 3 的判定依据（实测 + 源码双证）**：`Expected: 9, But was: 8`。`EffectRuntime.Cards.cs:293-347` 的 `DrawCards` 无论 `amount` 多大，只在循环结束后 `Emit` **一次**（`:344`），幅度只写入 `count` 载荷；`src/Adapters/EngineProjectionAdapter.cs:530-534` 也确实是"一条事件 + `count` 字段"的投影契约。因此 `machine_titan` 的 `downloadCost 1→2` 只改变该条事件的 `count`（使 `pullPunishes.Sum()==7`），**不产生第二条事件**。`Is.EqualTo(9)` 是按"每抽一张发一条"的误读写的，应改测试而非改引擎。同处 `pullPunishes.All(v => v is 1 or 2)`、`Sum()==7`、`pullDeclarations.Length==6` 三条断言均正确且通过。

### 13.3 平衡 A/B 与机械 −30pp 的**确切成因**（已修正：含 7 变体隔离实验）

> ⚠️ **本节已依据 2026-09-10 23:58 的隔离实验重写。初版把机械崩塌归因于"提交/下载代价被抬高"，该归因错误，请勿引用旧版数字与旧结论。**

**方法**：`SimMain` 的所有数据路径都相对 cwd（`SimMain.java:21-23` 用 `Path.of("data/balance.json")` 等），因此在 `%TEMP%` 下复制 `data/` 即可做**仓库外**的数据变体实验（**未改动任何生产文件**）。`build/classes`、`build/test-classes`、`data/decks/*`、`data/balance.json` 全程固定，只切换 `data/cards/*.json`。每变体 `SimMain 300` = 3600 局。

**变体读数**（同一份编译产物、同一时刻、同一牌组）：

| 变体 | `data/cards/*` 来源 | flame | machine | sea | wood | 平均回合 |
|---|---|---|---|---|---|---|
| `old` | 全用 `880250c`（落地前） | 63.6% | **46.4%** | 55.3% | 34.7% | 20.42 |
| `cur` | 全用本树（＝提交 `8bc0515` 内容） | 59.1% | **14.3%** | **90.9%** | 35.7% | 22.61 |
| `B` | 本树，仅 `machine.json` 用 `880250c` | 48.7% | **35.9%** | 90.0% | 25.3% | 18.77 |
| `C` | `880250c`，仅 `machine.json` 用本树 | 73.9% | **16.3%** | 66.2% | 43.6% | 24.66 |
| `D1` | `cur` ＋ 仅还原 `machine_leader.chant=2`、`chantEffects=[SUMMON_LEADER machine_alpha]` | 48.7% | **35.9%** | 90.0% | 25.3% | 18.77 |
| `D2` | `cur` ＋ 仅还原 `machine_alpha.leaderDef.winCondition=OPP_PUNISH_DRAW_TURN_GE`、`winParam=15` | 59.1% | **14.3%** | 90.9% | 35.7% | 22.61 |
| `D3` | `cur` ＋ 同时还原 D1 与 D2 | 48.7% | **35.9%** | 90.0% | 25.3% | 18.77 |

**结论（逐条可复现）**

1. **机械 −32.1pp（46.4→14.3）几乎全部出自一处：新 `machine_leader` 去掉了 `chant: 2` + `chantEffects: [{SUMMON_LEADER, machine_alpha}]`。**
   `D1` 只把这两个字段加回去，机械立刻 **14.3% → 35.9%（+21.6pp，647/1800）**，其余三方数据不动。
2. **`winCondition` 的改动在这份 Java 引擎里完全无效（no-op）**：`D2`（只还原 alpha 的 `OPP_PUNISH_DRAW_TURN_GE 15`）与 `cur` **逐位完全相同**（59.1/14.3/90.9/35.7，22.6105555556）。原因见 §13.8：**提交 `8bc0515`** 的 `Game.java:970-975` 的 `switch` 只认 `OPP_DISCARD_TOTAL_GE` / `NO_DAMAGE_TURNS_GE` / `OPP_PUNISH_DRAW_TURN_GE`，`PULL_TOTAL_GE` 落进 `default: break;`（`:975`），且 `PlayerState` 里**不存在** `pullCount`/`sealed`/`commitQueue`/`cloudStack` ⇒ **新 `PULL_TOTAL_GE 6`（机械）与 `GIANT_HEALTH_GE 512`（古木）在该版本里永远不可能达成**。`D3 == D1` 再次证明 alpha 的胜利条件从未触发过（"单回合 15 次惩罚抽牌"在实际对局中不发生）。
   ⚠️ **该缺口此刻正在被修复**：工作树里 Codex 已于 23:47 之后向 4 个 Java 文件加入 `pullCount`/`commitQueue`/`cloudStack`/`sealed`/`landmarkPullCount` 与两个 `case`，见 §13.9。**A/B 必须在这次 Java 移植落地并重新编译后重采。**
3. **剩余 −10.5pp（46.4→35.9）来自其他三方的新数据**：把机械换回旧数据后机械仍只有 35.9%（`B`）。主要贡献者是**深海**——只要用新 `sea.json`，sea 就稳定在 **90.0–90.9%**（`cur`/`B`/`D1`/`D3`），用旧 `sea.json` 则只有 55.3%（`old`）/66.2%（`C`）。⇒ **新深海数据是当前最大的越带项（+35pp，远超 40–60% 带），量级高于机械。**
4. **实验本身是确定性的，非方差**：`SimMain.java:38` 用 `(a*1000+b*100+k)` 作固定 seed，因此 `D1` 与 `B`、`D3` 与 `B` 的四项胜率与平均回合**逐位相同**（877/647/1620/456，18.77 回合）。变体之间可直接比对。
5. ⚠️ **但这些数字不能当作平衡判决**——这份 Java 引擎不实现当前规则集的核心机制（见 §13.8），这是"**残缺规则下的读数**"：它既会**放大**（sea 的降临／惩罚激活 Java 认，而 Unity 运行时目前无法发动），也会**缩小**（机械新设计的下载轴与地标 Java 全不认）。**初版"机械代价抬高导致崩塌"的说法据此撤回**：`commitCost`／`uploadCost`／`downloadCost`／`commitEffects`／`pushEffects`／`pullEffects`／`isLandmark`／`landmarkTiers` 这 8 类字段在 **Java 的 `com/dominionwars/model/CardDef.java`** 里根本不存在（§13.8），对 `SimMain` 完全不可见——**但在 C# 权威引擎 `src/Engine` 里已完整实现**（同见 §13.8）。
6. ❌ **旧版数字已作废**：初版记载的"本树 flame 72.7 / machine 15.2 / sea 68.3 / wood 43.9，25.01 回合"是在 `data/cards/sea.json` 被并发写入者重写的**中途**采样（sea.json 于 23:37:45 再次改变，哈希 `D0B280B65084`→`0FA7E4E33425`）。冻结在提交内容上的权威读数以 `cur` 行为准。
7. ⚠️ 仍成立的前提：`data/decks/*.json` 的 mtime 为 **2026-06-12**，四套预构筑**从未**随 91 卡重设计与 59 张迁移更新（`docs/RULES.md:292` 自述"仍须完成数据迁移"）。全部读数都是"9 月卡牌 × 6 月牌组"。

**建议路由（不自改数值）**：`balance.json`、卡牌数值、以及"是否保留 `machine_leader` 的 chant 升级路径"属 **owner/PL 权限**。请注意 `D1` 的含义：恢复 `chant 2 → 召唤 Alpha` 在 Java 里等价于 **+21.6pp**，但新设计已把该机制**搬进地标第 2 层（`landmarkTiers`）**——**机制没丢，只是 Java 认不出地标**。因此真正的决策不是"回调数值"，而是"**先让权威引擎具备地标／下载语义，再重采平衡数据**"。

### 13.4 并发写入者（对"能否合并/何时提交"有直接影响）

本轮观测到**在本会话之外还有实时写入者**，证据：

| 时间 | 事件 |
|---|---|
| 23:11:02–23:20:24 | `data/cards/{machine,flame,neutral,wood,sea}.json` 被逐份改写（另一条卡牌设计线） |
| 23:29:58 | **Codex 桌面应用启动**（`ChatGPT.exe` → `codex.exe app-server`，当前仍在运行但**空闲**；进程存活 ≠ 在跑回合，见 §13.13） |
| 23:34:28 / 23:34:36 | `DataLoaderTests.cs` / `ProductionFactionIntegrationTests.cs` 被改写（引入 §13.2 的 #1/#3） |
| 23:37:45 | `data/cards/sea.json` 再次被改写（哈希 `D0B280B65084` → `0FA7E4E33425`） |
| 23:39:20 | `docs/DESIGN_SEA_PUNISH_MATH_2026-09-09.md` 被改写 |
| 23:44 之后 | `docs/DESIGN_SEA_PUNISH_MATH_2026-09-09.md` 在我暂存之后**又**被改写一次 |

即：用户所依据的"Codex 熄火"前提在 23:29 之后**已经不成立**——但**结论要反过来读**：23:29:58 启动的是 Codex **桌面应用**，它并未在编辑仓库；真正继续写入的是 **`dsh`（DeepSeek Harness）会话**（§13.13 已取证）。因此本次 checkpoint 采用了**不干扰并发写入者**的做法：只在当前分支建提交、**不 push、不切分支、不改动异常文件树以外的任何内容**。

### 13.5 checkpoint 与分支

| 项 | 值 |
|---|---|
| 提交 | **`8bc0515`** `feat(engine): checkpoint card landing, AI wiring and QA green (2026-09-10)` |
| 内容 | 151 files changed, 11617 insertions(+), 969 deletions(-) |
| 父提交 | `880250c`（09-06，此前 4 天 0 提交） |
| 当前分支 | `codex/p0-complete-match-loop-2026-09-06`（未切换） |
| 新建分支 | **`qa/verify-2026-09-10`**（指向 `8bc0515`，未 checkout） |
| 有意排除 | Unity Test Runner 产物：`Assets/QA/`、`Assets/QA.meta`、`Assets/InitTestScene*.unity(.meta)` —— 建议后续加入 `.gitignore`（现有 `.gitignore` 已有 `# Generated QA artifacts` 段，但未覆盖这两项） |
| 提交后仍未提交 | 并发写入者在我暂存之后又改动了 `docs/DESIGN_SEA_PUNISH_MATH_2026-09-09.md`；本报告 §13 本身也是提交后才追加 |

**未 push**。合并与否请在 Codex 停止写入后再决定（见 §13.4）。

### 13.6 过度防御审计结论（子代理全仓库扫描，只读）

总体判断：**存在系统性过度防御，但没有"规则双份实现"的架构性违规**。`LegalActionGenerator` 先 advertise、`PlayCardActionHandler` 再 authorize 属于**必要的**重复（广告 ≠ 授权），不应删。

按严重度排序的**真实**过度防御：

| # | 位置 | 问题 | 级别 |
|---|---|---|---|
| 1 | `RuntimeScreenFlow.cs:490-499`（读 `RuntimeAiTurnCoordinator.cs:48-49` 的 `Halted`/`LastReasonKey`） | AI 协调器一旦 halt 就**永久静默**：返回值被丢弃、`Halted`/`LastReasonKey` 除两个测试外**全仓库无人读取**，玩家永远不知道对手 AI 已死 | **HIGH** |
| 2 | `web/app.js:117-129` | `catch (e) { /* 服务未就绪 */ }` 包住了整个轮询体（含 `render()`/`playEvents()`），任何真实 JS 异常都会变成"永久静默不刷新"，注释却把责任推给网络 | **HIGH** |
| 3 | `Balance.java:37-39` | `catch (Exception) { return; }` 静默回落硬编码默认值，且默认值与 `data/balance.json` **不一致**：`royalCastleMaxHp` 60 vs 75、`royalCastleEnabled` false vs true → 一次加载失败就**改变规则** | **HIGH** |
| 4 | `Game.java:363` (`chainDepth >= limit`) vs `PlayCardActionHandler.cs:306` (`chainDepth > _chainLimit`) | 同一 `chainLimit=20` 语义**差一**：Java 拒绝第 20 链节，C# 接受 → Unity 合法的连锁在 Java 权威下非法 | **HIGH** |
| 5 | `DeckLoader.cs:45` / `MatchSetup.ValidateDeck` | **未校验** `deckMin/deckMax` 60–80，而 Java `CardLibrary.java:92-93` 校验 → Unity 接受 Java 会拒的牌组 | **HIGH** |
| 6 | `RuntimeActionBoundary.Validate` 对**同一个 snapshot 对象**被调用 2–3 次（`RuntimeAiPolicy.cs:103`、`RuntimeAdapter.cs:113`、`RuntimeMatchGateway.cs:154`；人类点击再叠 `RuntimeBattlePanel.cs:2171`） | 纯冗余（同层重复，期间不重读 snapshot，第 2/3 次不可能新拒），不构成层边界 | MEDIUM |
| 7 | `RuntimeBootstrap.cs:227-241`、`RuntimeScreenFlow.cs:159-164`、`RuntimeAdapter.cs:220-222`、`RuntimeAiTurnCoordinator.cs:114-115` | 死防御分支：上游已 throw/已保证非 null，这些分支**永不触发**；其中 `RuntimeScreenFlow.cs:159-164` 还**违反** `RuntimeMatchSetupOrchestrator.cs:29-32` 明文写的"此处不要再做第二次 post-commit 就绪检查" | MEDIUM |
| 8 | `src/` 约 26 处 `catch (Exception)` / `catch {}`、Unity 侧约 16 处 | 宽泛吞异常；反例是 `RuntimeContentContext.cs`（7 处全用 `when` 过滤器）——建议以其为模板 | MEDIUM |

另有 12 项 MEDIUM/LOW（`Json.java:216` 数字解析静默回落、`GameSession.java:97` 把 NPE 记成"指令错误"、`RuntimeBattlePanelActionFeedback.cs:429` 不可达 `catch (OverflowException)` 等）。完整清单见会话产物。

### 13.7 主线完成度结论（子代理逐条读码核验）

**判定：PARTIALLY complete。** 核心环（setup → START → AMBUSH/ACTION → END → victory → result/restart）**确实已实现**，且自动化 PlayMode 端到端通过，Windows 播放器能构建；但按项目自己的门禁**未算完成**：

1. **Unity 从不接线惩罚响应策略** → 惩罚降临/响应降临在发布运行时**恒被自动放弃**：`MatchFactory.cs:62-64` 传 `punishResponses = null` → `TurnActionRouter.cs:34-48` 回落到 `PlayCardActionHandler.cs:45` 的 `DeclinePunishResponsePolicy`；且 `ACTIVATE_PUNISH` **根本不在线上契约里**（`ContractBoundaryTests.cs:74`），也未进入合法动作表（`LegalActionGeneratorTests.cs:184`）。**Java/Web 引擎有，Unity 没有。**
2. **`OPP_PUNISH_TRIGGERED_GE n` 胜利条件未实现**（`EffectRuntime.EndPhase.cs:156-191` 无此 case），这正是 `shadow_of_fate` 停在 `NONE` 且"设计上不可赢"的原因。
3. **原生 OS 鼠标输入从未被验收**（`ENV_BLOCKED` / `INPUT_NOT_ACCEPTED`）：只有 `EventSystem` 自动化覆盖，`.exe` 对真实玩家**未验证**。
4. **深海印记/潮位语义未冻结也未实现**（全仓库无潮位计数器），`RULES.md:237-246,295` 自己标注未冻结 + `HUMAN_REQUIRED`。
5. `battle_state_machine.json` **零代码引用**（契约文档，UI 实际由 snapshot 驱动）；`docs/DESIGN.md` 严重过时（只描述 Java/Swing，无 Unity、无 v1.31 契约）。
6. `PULL`/`VICTORY_PROGRESS` 只有通用提示，尚无因果化的进度轨。
7. `docs/CODEX_AI_CLOSEOUT_REPORT_2026-09-08.md` 自身状态仍是 **DRAFT / PENDING_EXTERNAL_PL_QA**，其 §21 的 `603/603`/`306/306`/`26/26` 均为卡牌落地**前**读数。

14 条 P0 manifest 行的逐条判定：Implemented 9 / Partially 2（L3 Unity 端到端仅 PlayMode 证明；L11 惩罚因果链已铺数据但激活路径不可达）/ doc-claimed 1（L19）/ 其余为已实现。

### 13.8 权威引擎 vs 测试台：能力覆盖差（**本次最高优先级发现**）

问题：仓库里唯一的批量模拟器 `SimMain`（Java）跑的是**旧 Java 引擎**，而当前 91 卡规则集依赖的机制大多**只存在于 C# 权威引擎 `src/Engine`**。逐项核查（`git grep` 全仓库取证）：

| 能力 | C# `src/Engine`（Unity/Web 走它，**权威**） | Java `src/main/java`（`TestMain`/`SimMain` 走它） |
|---|---|---|
| 提交/上传/下载 生命周期（COMMIT/UPLOAD/PULL、提交队列、云端栈） | ✅ `CardDefinition.CommitCost`/`UploadCost`/`DownloadCost`/`CommitEffects`/`PushEffects`/`PullEffects`；`src/Engine/Turns/CommitActionHandler.cs`、`PullActionHandler.cs`、`Effects/EffectRuntime.Mechanical.cs` | ❌ **提交 `8bc0515` 里零实现**：`CardDef.java` 无这些字段；`src/main/java` 全域对 `PULL\|pull\|upload\|download\|commit` **零命中** |
| 地标与地标层（`isLandmark`/`landmarkTiers`） | ✅ `CardDefinition.IsLandmark`/`LandmarkTiers`、`CardInstance.LandmarkPullCount`、`EffectRuntime.Mechanical.cs:209-299`（`AdvanceLandmark`/`PromoteLandmark`）、`RuntimeContractV131Snapshot.cs:162` | ❌ 同上，字段被**静默忽略** |
| `PULL_TOTAL_GE` 胜利 | ✅ `Effects/EffectRuntime.EndPhase.cs:152`（→ `PullCount`） | ❌ 提交 `8bc0515` 的 `Game.java:970-975` 无此 `case`（`PlayerState` 当时也没有 `pullCount`） |
| `GIANT_HEALTH_GE` 胜利（古木 512） | ✅ `EffectRuntime.EndPhase.cs:155` | ❌ 提交 `8bc0515` 无此 `case`（当时 `CardInstance` 没有 `sealed`） |
| `punishActivatable`/`punishCost`/`punishCondition`/`punishEffects` | ✅ | ✅ `Game.java:346,364,413`、`CardDef.java:158-160,213-215`、`CardCatalog.cs` 对应 | 
| `ACTIVATE_PUNISH` 作为**线上动作** | ❌ **不在 wire contract**：`Tests/ContractBoundaryTests.cs:74` 断言 `Does.Not.Contain("ACTIVATE_PUNISH")`、`Tests/LegalActionGeneratorTests.cs:184` 断言该类型**不存在**于合法动作表；`src/Engine/Localization/Resources.cs:26` 只有本地化键 `action.activate_punish`。引擎虽定义了 `IPunishResponsePolicy`，但 Unity `MatchFactory.cs:62-64` 传 `punishResponses = null` ⇒ 恒回落 `PlayCardActionHandler.cs:45` 的 `DeclinePunishResponsePolicy` | ✅ Java 走自己的 UI/动作枚举，无 wire-contract 约束 |
| **无头批量模拟器** | ❌ **不存在**：`src/` 下 4 个 csproj（Adapters/Data/Engine/Engine.Tests）**全部无 `static void Main`**，`src/Engine/Tests` 只有单元测试 | ✅ `src/test/java/.../SimMain.java` |

**结论（直接影响"能否用模拟数据判定平衡"）**

1. **仓库当前没有能评估现行规则集的平衡预言机。** `SimMain` 测的是一份它不认识的规则集；C# 侧那台**根本不存在**（603 个单元测试不能当胜率基线）。因此 §13.3 的胜率、尤其是"机械 14.3%"**不能作为设计回归结论**，正确表述是"**旧测试台对新数据的可见性效应**"。
2. **机制没丢，测试台看不见。** 旧数据里机械靠 `chant 2 → SUMMON_LEADER machine_alpha` 升级（Java 认得，所以 `D1` 能回到 35.9%），新设计把同一件事搬进**地标第 2 层**（`landmarkTiers`，Java 认不得）⇒ 差别在**引擎覆盖**，不在数值。
3. **深海 90.9% 是唯一"Java 看得见"的越带项**（降临/惩罚激活两个引擎都实现）⇒ 最需要跨引擎保真度核对的一项，量级 +35pp。但也必须计入 §13.7.1 的反向因素：**Unity 运行时目前永远走自动放弃**，降临在玩家实际对局里根本不会发动——两侧语义并不等价，两个读数都不能直接采信。
4. **建议（属实现/规划权限，我只上报）**：
   - 近期：为 **C# `src/Engine`** 建一个最小无头对局跑批入口（复用 `MatchController` + `RuntimeAiPolicy`），否则每一次数据落地都只能靠人工对局主观判断；
   - 或者：把 `SimMain` 依赖的 Java 引擎补齐到与 `src/Engine` 同等能力后，再恢复"数据落地 → 重采胜率"的例行门禁；
   - 无论走哪条，**在预言机可用之前不要依据 §13.3 的百分比改动数值**。
5. ✅ **需要肯定的一点**：`data/schema/cards.schema.json` 已含 `LandmarkTier`（`:116`）、`isLandmark`（`:147`）、`landmarkTiers`（`:148`）、`WinCondition` 枚举含 `PULL_TOTAL_GE`（`:48`），`src/Data/CardCatalog.cs:27-28,247-279,442-480` **fail-closed 地解析**这些字段——**数据侧与 C# 侧的契约是通的**，缺口只在 Java 测试台。

### 13.9 提交后的在飞写入（**归因已更正：写入者是 harness 会话，不是 Codex**，见 §13.13）

`8bc0515` 提交完成后，仓库**继续被写入**。**最初我把写入者记为 `codex` 进程 PID 29668，这是未经证实的推测；§13.13 的取证推翻了它**——PID 29668 是 Codex 桌面应用的常驻 `app-server`，实测空闲。`git status` 现为：

**2026-09-11 00:00 快照**：

```
M docs/AI_MAILBOX.md
M docs/DESIGN_SEA_PUNISH_MATH_2026-09-09.md
M docs/QA_PROJECT_STATUS_2026-09-10.md
M docs/effects.contract.md
M src/main/java/com/dominionwars/ai/AiAgent.java                  (+163)  mtime 23:59:54
M src/main/java/com/dominionwars/engine/CardInstance.java         (+18)
M src/main/java/com/dominionwars/engine/Effects.java              (+196)
M src/main/java/com/dominionwars/engine/Game.java                 (+233)
M src/main/java/com/dominionwars/engine/PlayerAgent.java          (+24)
M src/main/java/com/dominionwars/engine/PlayerState.java          (+9)
M src/main/java/com/dominionwars/model/CardDef.java
M src/test/java/com/dominionwars/test/TestMain.java
?? docs/PL_CODE_AUDIT_2026-09-09.md
?? unity/.../InitTestScene<guid>.unity(.meta)   ← Unity Test Runner 产物（有意未提交）
?? unity/.../Assets/QA/ + QA.meta               ← 同上
```

**`src/main/java/com/dominionwars/engine/**` 的改动正是 §13.8 缺口的补齐**：新增 `PlayerState.pullCount`/`commitQueue`/`cloudStack`、`CardInstance.sealed`/`landmarkPullCount`/`pendingLandmarkSummonCardId`/`committed`，`has()` 改为封印时失效，`checkSpecialWins()` 补 `GIANT_HEALTH_GE` 与 `PULL_TOTAL_GE` 两个 `case`。⇒ **§13.3 的 A/B 必须在这批移植落地并 `javac` 重编后重采**；§13.3 与 §13.10 的所有胜率数字的**有效期截至该批写入之前**。

**⚠️ 新增（23:53–00:00，与 §13.10 的 F3 直接相关）**：同一写入者同时在 `src/main/java/com/dominionwars/ai/AiAgent.java` 补了**Java 测试台的生命周期 AI**——新增 `chooseCommit` / `askPush` / `askPull` / `chooseRollbackTarget` 四个覆写 + `lifecycleBudget()` 预算（`3 + turnNumber/4`）+ 地标 `landmarkPullCount < 2` 优先下载。**注意作用域差别**：

- 修的是 **Java 侧 `AiAgent`（测试台选手）**；
- §13.10 的 F3 指的是 **C# 侧 `RuntimeAiPolicy.cs`（Unity 发布路径的 AI）**，该文件**至今没有任何生命周期分支**（全文仅 `FirstNonType(legal, "END_TURN")`，`Select-String` 对 `Pull|Commit|Push|Rollback` 零命中）。
- 因此**即使 Java 移植全部落地，发布运行时机械的胜利条件仍然是死的**；同一套生命周期策略需要**同样在 C# 侧落地**（`RuntimeAiPolicy` 或其上游动作选择器）。这是 §13.9 之外的**第 4 条暂不合并理由**，已并入 §14 的 F3。

**合并建议：暂不合并到主线。** 四条理由：

1. **测试台与规则集尚未对齐**：§13.8 的缺口正在被补（在飞），补齐后需重采基线；此时合并等于把"未经任何有效预言机验证的数值"带入主线。
2. **Unity 侧惩罚激活链仍未接线**（§13.7.1）：`ACTIVATE_PUNISH` 不在 wire contract，降临在发布运行时恒被放弃——这是**行为级缺口**，不是数值问题。
3. **`data/decks/*.json` 仍是 2026-06-12 的旧构筑**（§13.3 第 7 条）：牌组未随 91 卡迁移，任何胜率都建立在"9 月卡牌 × 6 月牌组"上。
4. **发布路径的 AI 仍不会打生命周期胜利条件（§13.10 F3）**：在飞补的是 **Java 测试台的 `AiAgent`**，而 C# `RuntimeAiPolicy.cs` 至今零生命周期分支 ⇒ 即使 Java 移植全部落地，Unity 里的机械依然是 0% 胜率。合并前必须在 C# 侧补同样的策略，否则"机械的胜利条件"在发布玩法中不存在。

建议的合并路线：① 完成 Java 移植与 Unity 惩罚链接线 → ② **在 C# `RuntimeAiPolicy` 侧补生命周期策略（F3）** → ③ 重采基线（`SimMain` 与 §13.10 的 `DwSim` 双方）并建立可重复门禁 → ④ 完成 4 套预构筑迁移（`docs/RULES.md:292`）→ ⑤ 再由 owner/PL 决定数值 → ⑥ 最后合并。分支 `qa/verify-2026-09-10`（指向 `8bc0515`）就是这条路线上的取证基线，**未经上述步骤不要直接 merge**。

---

### 13.10 新建 **C# 权威引擎平衡预言机** 并首次实测（本次交付，直接补 §13.8 的最大缺口）

§13.8 指出"仓库当前没有能评估现行规则集的平衡预言机"。本节把这个缺口**在仓库外**补上并跑出第一份权威引擎读数。

**工具**：`%TEMP%\dw-cs-sim\`（`DwSim`，net8.0 控制台程序，只 `Reference` `build-output\DominionWars.Engine\bin\Release\netstandard2.1\DominionWars.Engine.dll`、`DominionWars.Data.dll` 与 `Newtonsoft.Json.dll`，**不修改仓库内任何文件**）。

> **源与原始读数存档**：`Program.cs`（17.2 KB）与 `dw-sim.csproj`（1.2 KB），以及 5 个变体的原始 stdout（`variant-{A..E}-*-600.txt`），已复存到**仓库外**的会话产物目录 `<session>\files\dw-cs-sim\`。它是**一次性 QA 工具**，未纳入 git（我不改 `.gitignore`，也未获授权向 `scripts/` 写文件）；如需转正为常设门禁，请 owner/Codex 决定落点。

```powershell
# 构建
cd $env:TEMP\dw-cs-sim
dotnet build -c Release -p:MSBuildEnableWorkloadResolver=false --nologo
# 每对阵 100 局、生产默认王城配置、惩罚响应 = 自动放弃（Unity 现状）
.\bin\Release\net8.0\DwSim.exe 'C:\Users\USER\Documents\dominion-wars-win64' 100 decline 60 quiet
# 可选模式标记：pullfirst（AI 优先下载） / reverse（排序反转） / nocastle（CastleEnabled=false）
```

**方法**：`MatchSetup.Create` + `TurnFlow.CreateDefault` + `TurnActionRouter.CreateDefault`，生产默认 `MatchSetupOptions`（`OpeningHandSize=5`、`PlayerLife=20`、`CastleEnabled=true`、`CastleHealth=75`）；4 副 `data/decks/*.json` 全对局无序对（6 对），每对 100 局，先手按局号交替，`Seed = 20260910 + k`；动作选择镜像 `RuntimeAiPolicy`（`(ActionId, Type)` 稳定排序后取首个非 `END_TURN`；DISCARD 取 `DISCARD`）。**每变体 600 局，单次运行约 3–5 秒。**

#### 结果（每变体 600 局）

| 变体 | 动作选择 / 惩罚响应 / 王城 | 烈焰 | 机械 | 深海 | 古木 | 平均回合 | 无胜者 |
|---|---|---|---|---|---|---|---|
| **A 生产基线** | 排序取首个非 END_TURN / 放弃 / 开 | **91.0%** (273/300) | **0.0%** (0/300) | 74.0% (222/300) | 35.0% (105/300) | 10.17 | 0 |
| **B 惩罚接受** | 同 A / 接受 / 开 | 84.3% (253/300) | 1.7% (5/300) | 82.0% (246/300) | 31.7% (95/300) | 6.21 | 1 |
| **C 下载优先** | PULL>COMMIT>PLAY>ATTACK / 放弃 / 开 | 58.3% (175/300) | **95.7%** (287/300) | 43.7% (131/300) | 2.3% (7/300) | 8.37 | 0 |
| **D 排序反转** | 反转 / 放弃 / 开 | 63.0% (189/300) | 52.3% (157/300) | 65.3% (196/300) | 19.3% (58/300) | 7.82 | 0 |
| **E 无王城** | 同 A / 放弃 / **关** | 59.3% (178/300) | 0.0% (0/300) | 94.3% (283/300) | 46.3% (139/300) | 8.26 | 0 |

终局原因（裁决该局的数量，每变体 600 局）：

| 变体 | `royal_castle_break` | `pull_total_ge` | `enemy_leader_defeated` | `opp_discard_total_ge` | `deck_cycles` | `giant_health_ge` | 其它 |
|---|---|---|---|---|---|---|---|
| A | **273** | 0 | 167 | 96 | 58 | 6 | 0 |
| B | **244** | 0 | 124 | 167 | 55 | 0 | `castle_break_minion` 9、`turn_limit` 1 |
| C | 175 | **287** | 26 | 76 | 29 | 7 | 0 |
| D | 0 | 157 | **352** | 27 | 0 | 10 | `enemy_life_zero` 54 |
| E | 0 | 0 | **489** | 71 | 0 | 25 | `enemy_life_zero` 15 |

生命周期动作被 AI 实际选择的次数（每 600 局）：`PULL` = A 91 / B 93 / C 1808 / D 1357 / E 16；`machine_leader` 全程 `maxPull` = A 3 / B 4 / C 10 / D 11 / E 1（胜利门槛 `winParam = 6`）。

#### 结论

1. **F1（P1，方向稳健）王城轴把烈焰推到 84–91%。** `src/Engine/Effects/EffectRuntime.State.cs:209-224` 的判定是：当前活跃统领中**恰好一方**持有 `ROYAL_CASTLE_BREAK` 时，把胜利判给**持有者**——**无论王城是谁破的**；双方都是随从统领时则由主动破城方获胜（`win.castle_break_minion`，`:200-207`）。四套牌组里只有 `flame_leader` 持有该条件（`data/cards/flame.json:18`，`type = MINION`），所以**凡出现破城且有烈焰在场，烈焰必胜**，包括"对手破掉烈焰的王城"这一分支。唯一变量实验（A 91.0% ↔ E 59.3%，只差 `CastleEnabled`）与 A 变体 273/600 = **45.5% 的对局由 `win.royal_castle_break` 直接裁决**共同证明这条轴就是烈焰的胜率来源。
   ✅ **代码与规范一致**：`docs/RULES.md:105` "王城被破坏即触发持有该条件的首领获胜（被动，**不问谁破城**）"、`:137` 同义、`:138` "双方统领均为随从型统领的对局中，主动破城方直接获胜" —— C# 引擎**正确实现了文档规则**，F1 是**平衡问题而不是实现缺陷**（规则本身把"破城"变成烈焰的地雷）。因此归属是 **PL/owner 定数值或改语义**，不是 Codex。
   ⚠️ 唯一文本瑕疵：`flame_leader.leaderDef.winText = "击破王城即获胜"` 只描述了"我破城我胜"这一半，与被动条件"**不问谁破城**"不符 ⇒ 建议改为与 `RULES.md:105` 同义（属 `data/`，PL/owner 权限）。
2. **F2（P1）下载轴一旦被追求就是压倒性的：`winParam = 6` 过强。** C 变体与 A 的唯一区别是 AI 优先下载，机械立刻从 0.0% 变成 **95.7%（287/300），且 287 个胜局全部是 `win.pull_total_ge`**；同变体烈焰掉到 58.3%、古木 2.3%。结合 §13.8：这条轴在旧 Java 测试台上**完全不可见**，所以"机械只有 14.3% 所以很弱"的方向是**反的**——真实风险是它太强，只是没人打。
3. **F3（P0，主线完成度）生产 AI 不会打自己的胜利条件，机械在发布路径上不可胜。** 每 600 局里 AI 只选 `PULL` 91 次（A），而 `COMMIT` 5069 次；`machine_leader` 的 `maxPull` 全程停在 3–4，远低于门槛 6。机制：`RuntimeAiPolicy` 的 ACTION 分支是"稳定排序后取第一个非 `END_TURN`"，对提交/下载生命周期**没有任何策略**，`PULL` 恰好稳定排在后位 ⇒ **提交出去就再不下载**。这不是数值问题，**改数值无效**；路由 Codex（`RuntimeAiPolicy.cs`）。
4. **F4（P1）对局长度低于 `docs/BALANCE.md` 目标带。** 实测平均回合 A 10.17 / B 6.21 / C 8.37 / D 7.82 / E 8.26（中位 6–10），而 `docs/BALANCE.md:12` 要求 **10–20 回合**、`:13` 要求胜率落在 **40–60%**。除 A 勉强贴到下沿外全部偏低，B（惩罚接受）短到 6.21。**B 正是"降临会被接受"的语义，也就是 §13.7.1 之后 Unity 应该变成的形态 ⇒ 一旦把惩罚响应接线，对局长度会再掉近一半**，这是合并前必须先解决的风险。
5. **F5（P1）古木 512 轴几乎不发生。** `win.giant_health_ge` 在 600 局里只裁决 6（A）/ 7（C）/ 10（D）/ 25（E）局（1–4%）；`wood_leader` 在所有变体里都最弱或次弱（35.0 / 31.7 / 2.3 / 19.3 / 46.3%）。由于 §13.8 已证明该轴**引擎可达**，问题是达成成本而非引擎缺口。
6. **F6（工具）** 这就是 §13.8 建议的"为 C# `src/Engine` 建一个最小无头跑批入口"的可用版本，5 秒/600 局，可作为后续每次数据落地的例行门禁。**保真度限制必须与数字一起引用**：① 双方都由**无策略**选点驱动，绝对值对选点敏感（D 变体即证据），**只有方向性结论稳健**；② "弃 N 张手牌为额外费用"的 `PLAY_CARD` 不在合法动作表里广告选择，harness 做了有界重试：A 变体共 630 次 `action.discard_selection_required`，其中 535 次重试成功，未恢复 95 次（占 45690 次 `PLAY_CARD` 的 0.2%）；③ 牌组仍取自 `data/decks/*.json`（**2026-06-12 旧构筑**，与 §13.9 第 3 条同一保留）；④ A 变体单次运行的可复现读数：600 局、平均 10.17 回合、中位 10、最长 23、无胜者 0。
7. **F7（P1，规则单源被破坏 —— 两引擎对同一枚举给出不同语义）** 见 §13.11：Java `Game.java:593-604` 的 `checkRoyalCastleWin(breakerIdx)` **只检查破城方**是否持有 `ROYAL_CASTLE_BREAK`，与 `docs/RULES.md:105/137` 的"**不问谁破城**"不符，而 C# 引擎按文档实现。两引擎在同一规则上结论相反，这解释了 Java 实测烈焰 51.2% 与 C# 实测 91.0% 的巨大落差。**路由 Codex**（Java 是必须与文档对齐的一侧）。

---

### 13.11 对在飞 Java 移植的快照复验（2026-09-11 00:03–00:06，只读）

> **✅ 00:23 更新（见 §13.16）**：写入方确已停止，改用**仓库规范路径**（`scripts\build.bat` → `build\classes`）复跑，`build` exit 0 / `TestMain` **59/59** / `SimMain 300` 平均回合 **14.79**，与本节仓外编译读数**逐位一致**。本节"此后未复跑"的保留项**已就地关闭**。

写入方仍在写（该轮由 harness 会话执行，见 §13.13；当时我误记为 Codex）。我在**不触碰仓库 `build/classes`** 的前提下，把工作树源码编到 `%TEMP%` 独立目录后复跑，得到以下快照：

**复现命令**
```powershell
cd <repo>
$main = "$env:TEMP\dw-javac-out"; $tst = "$env:TEMP\dw-javac-test2"
New-Item -ItemType Directory -Force -Path $main,$tst | Out-Null
# ① 编译主源码（27 文件）
Get-ChildItem src\main\java -Recurse -Filter *.java | % FullName | Out-File "$env:TEMP\l1.txt" -Encoding utf8
javac -encoding UTF-8 -nowarn -d $main "@$env:TEMP\l1.txt"          # exit 0
# ② 编译测试源码（3 文件）
Get-ChildItem src\test\java -Recurse -Filter *.java | % FullName | Out-File "$env:TEMP\l2.txt" -Encoding utf8
javac -encoding UTF-8 -nowarn -cp $main -d $tst "@$env:TEMP\l2.txt" # exit 0
# ③ 回归
java -Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -cp "$main;$tst" com.dominionwars.test.TestMain
# ④ 模拟
java -Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -cp "$main;$tst" com.dominionwars.test.SimMain 300
```

**结果 1：编译通过**（主 27 文件、测试 3 文件，`javac` 两次 exit 0）。该移植处于可编译的一致状态。

**结果 2：Java 回归 `TestMain` = 51 / 52（exit 1）**。测试总数由 `8bc0515` 时的 38 增至 **52**（新增 14 条生命周期/古木测试）。唯一失败：

```
✗ 机械：PULL 下载栈顶 → 惩罚/效果/墓地/计数/地标层数
  —— 下载效果 BUFF 1/1 落在唯一合法目标（载体地标）上：8+1：期望 9，实际 0
```

**注意**：首次快照（00:04）为 **48/52**，含 3 条古木用例以 `Cannot read field "attack" because "<parameter1>" is null` 失败；**两分钟内写入方已自行修掉** ⇒ 这是**在飞状态**，不是稳定读数，本节不作为缺陷上报，仅作为"移植正在收敛"的证据。

> **✅ 00:20 追加复验（仍在飞，但已收敛）**：同一路径（编到 `%TEMP%\dw-javac-out3`，不碰仓库 `build\classes`）复跑，`javac` 主/测两次 **exit 0**，**`TestMain` = 59 / 59 全绿**（测试数 52 → 59，此前唯一失败的"下载 BUFF 落在地标上 8+1 期望 9"已通过；新增用例含"惩罚连锁深度正好等于 `chainLimit` 时仍要响应（对照 C# 的 `>` 而非 `>=`）"⇒ **§13.6 第 4 条的 `chainLimit` 差一已被写入方修掉**）。`SimMain 300`（3600 局）读数：flame 51.2% / machine 13.2% / **sea 80.3%** / wood 55.3%，平均回合 **14.79** —— **与 00:06 快照完全一致**，说明本轮 `Balance.java`（默认值对齐）与 `chainLimit` 修正未影响该模拟下的平衡读数。**F8 可关闭。**

**结果 3：Java 侧新平衡读数**（`SimMain 300`，3600 局，确定性种子）：

| | flame | machine | sea | wood | 平均回合 |
|---|---|---|---|---|---|
| Java（00:05 在飞移植 + 新 `AiAgent` 生命周期策略） | 51.2% | 13.2% | **80.3%** | 55.3% | **14.79** |
| C# 权威引擎（§13.10 A 变体） | **91.0%** | 0.0% | 74.0% | 35.0% | 10.17 |

三点解读：① **平均回合 14.79 落回 `docs/BALANCE.md` 的 10–20 目标带**——`8bc0515` 时 Java 侧的 25.0 回合异常随移植落地而消失，这是本次移植的**正向证据**；② 两引擎都指向 **深海偏强（74–80%，超出 40–60% 带）**，这条**跨引擎一致**，可信度最高；③ 烈焰 51.2% ↔ 91.0% 的巨大落差**不是噪声，而是规则分歧**（下条 F7）。

**结果 4：F7 规则分歧（P1，两引擎语义相反）**

| | 破城方持有条件 | **防守方持有条件**（对手破掉 flame 王城） |
|---|---|---|
| **C#** `EffectRuntime.State.cs:212-224` | 破城方胜（`win.royal_castle_break`） | **防守方胜**（`win.royal_castle_break`） |
| **Java** `Game.java:593-604` | 破城方胜（`winReason = winText`） | **无人获胜**，仅破城方 `cycleWinCount = max(…, 9)` |
| **文档** `docs/RULES.md:105` / `:137` | 持有者胜 | "被动，**不问谁破城**" ⇒ **持有者胜** |

⇒ **C# 符合文档，Java `checkRoyalCastleWin(breakerIdx)` 缺少防守方分支**，是**规则单源被破坏**（同一 `ROYAL_CASTLE_BREAK` 枚举在两引擎里语义相反），必须由 Codex 对齐。另附：`RULES.md:138` 的"双方统领均为随从型 ⇒ 主动破城方获胜"在 C# 由 `:196-207` 的 `win.castle_break_minion` 实现，Java 侧未检索到对应分支，请 Codex 一并核对。

---

### 13.12 两套 C# 预言机的交叉验证与差异归因（2026-09-11 00:10 追加）

**背景**：PL 在我提交 §13.10 之后，**独立**建成了第二套 C# 权威预言机（`build-output/pl-csim/`，`build-output/` 已由 `.gitignore:15` 覆盖，未污染仓库），并产出 `docs/PL_BALANCE_MEASUREMENT_2026-09-11.md`（2160 局）。两套工具**引擎相同、harness 不同**，因此这是天然的双向交叉验证。下表逐条归因。

**13.12.1 读数对照**

| 阵营 | 我的预言机 A 变体（600 局，生产策略） | PL 预言机 A 配置（720 局） | PL B 配置（720 局） | 判定 |
|---|---|---|---|---|
| 深海 | 74.0% | **84.4%** | 67.5% | ✅ **两套独立工具一致偏强** |
| 烈焰 | **91.0%** | 71.1% | 58.3% | ⚠️ 见 13.12.2 第 2 条 |
| 机械 | 0.0% | 34.4% | 31.1% | ⚠️ 见 13.12.2 第 1 条 |
| 古木 | 35.0% | 10.0% | 43.1% | ⚠️ 见 13.12.2 第 3 条 |
| 平均回合 | 10.17 | 5.8–6.4 | 15.7–16.7 | 同向 |

**13.12.2 差异归因（逐条已用源码核实）**

**1. 机械 0.0% ↔ 34.4% —— 不是矛盾，恰恰是 F3 的第二重证据。**
我的 A 变体刻意驱动"**发布路径策略**"（等同 `RuntimeAiPolicy.cs:65` 的 `FirstNonType(END_TURN) ?? legal[0]`），该策略下 600 局共只选中 **PULL 91 次**、`machine_leader` 单局最多下载 **3** 次，达不到 `pull_total_ge = 6` ⇒ 0 胜。PL 的 harness **自带生命周期策略**（`--games 10` 复核：COMMIT 328 / PULL 149，`actions rejected 0`）⇒ 34.4%。**两者之差 = "有没有人替机械按下下载键"**，与 F3 完全同源：**引擎有这条胜利轴，发布路径的 AI 不会去走。**

**2. 烈焰 91.0% ↔ 71.1% —— F7（规则分歧）＋策略共同作用。**
我的 A 变体 600 局终局原因中 `win.royal_castle_break` 占 **273** 次（含"对手破掉烈焰王城 ⇒ 烈焰胜"的防守方分支）；PL 的报告亦独立记录"破城轴 A 配置仅 3/720、B 配置 69 次"，且其 §7 明言"破城轴未证明不可达"。⇒ **两套工具都看到破城轴在 C# 侧生效**，差异主要来自策略对**是否主动破城**的取舍，F7 的结论不受影响（分歧在 Java 侧）。

**3. 古木 10.0% ↔ 35.0% —— PL 给出了机制级根因，我采纳并已源码复核。**
PL §4 指出古木的封印机制自相矛盾，我逐行核验**全部成立**：

| 环节 | 证据（已读源码） |
|---|---|
| 增幅即封印，且攻击清零 | `src/Engine/Effects/EffectRuntime.Combat.cs:192-197`：`growthApplied` ⇒ `Sealed = true; Attack = 0; Shield = false;` |
| 增幅开关含**阵营**条件 | 同上 `:163-173`：`woodSource = 来源卡阵营 == "古木圣地"` ⇒ `rootLayers`/`rampantLayers` 对**任意** mode 都计入 |
| 封印单位不能攻击 | `src/Engine/Turns/AttackTargetPolicy.cs:17`：`&& !attacker.Sealed` |
| 古木统领登场即为疯长供能 | `data/cards/wood.json` `wood_leader.leaderDef.enterEffects = [SUMMON ×2, **ADD_RAMPANT 1**]` |

⇒ **古木统领一出场 `RampantStacks ≥ 1`，此后每一张古木来源的强化卡都被判为增幅 ⇒ 目标被封印且攻击力归 0。** 设计意图（`CARD_DESIGN_MODEL:174`"普通 buff 不封印，保留木的场面能力"）被**统领自身的疯长供给击穿**：古木身边不存在"普通 buff"。我的 A 变体里 `giant_health_ge` 仅裁决 6/600、`wood` 35.0%，与 PL 的 10.0% / A 配置 3 次转化**同向同构**，只是 harness 对手强度不同。**这是机制矛盾，不是数值问题；PL 的 W1（封印只认显式 `param:"root"/"rampant"`，取消阵营开关）与我 F5 的"先查机制再动数值"结论一致。**

> **⚠️ 2026-09-11 00:15 追加（PL 自证伪，我未独立复跑）**：PL 的消融实测**推翻了"封印是古木弱的原因"**——移除 `wood_leader` 的 `ADD_RAMPANT` 后古木在 A 配置 **10.00% → 11.11%**、B 配置 **43.06% → 33.61%**（`build-output\pl-csim\SUMMARY.md §2`），且古木每回合可测输出攻击力几乎不变（0.595 → 0.619）。**因此第 3 条的定位需下调**：封印机制**客观矛盾且应修**（源码级事实，仍成立），但它**不是古木胜率低的成因**，修它也不应期待胜率回升。真正被 PL 测出的成因是第 5 条（`punishActivatable` 占比）。**告诫：不要拿"修封印"当作提升古木的手段。**

**4. `PUNISH_DRAW` 洪流 —— 两套工具同向，PL 的量化更锐利。**
PL 测得 **138 张惩罚抽牌 / 局 vs 10.2 次出牌 = 13:1**。我的 A 变体终局原因中 `opp_discard_total_ge` 96/600 次、`action.discard_selection_required` 630 次，同向。**PL 的 13:1 是本轮最有解释力的单个数字**，建议 PL 报告 §3 的 T1 结论直接进入 owner 决策清单。

**13.12.3 PL 报告新增缺陷：`SET_AMBUSH` 广告 ≠ 可解（我独立复现并给出精确位置）**

PL §6 的判断**成立**，但位置需更正（其引用 `LegalActionGenerator.cs:88-89` 是 `PLAY_CARD` 的 fizzle 分支，与埋伏无关）。真正的广告点与判定点：

| 角色 | 位置 | 逻辑 |
|---|---|---|
| **广告** | `src/Engine/Turns/TurnFlow.cs:204-211` | `PunishToSelfDiscardThisTurn && punish > 0` ⇒ 写入 `payload["discardRequired"] = punish`、`discardCandidateIds = 手牌中除来源卡外的全部`，**但仍无条件 `actions.Add(...)`（`:213`）** |
| **判定** | `src/Engine/Turns/AmbushActionHandler.cs:126-128` | `selectedIds.Count != cost` ⇒ 返回 `null` ⇒ 拒收 |
| **候选池** | 同上 `:136` | `ReferenceEquals(card, source)` ⇒ 来源卡本身被排除 |

⇒ 当 `手牌数 − 1 < punish` 时，广告出的 `SET_AMBUSH` **在原子上不可满足**（PL 实测 2160 局中触发 1 次：`required=3 candidates=1`）。**恢复路径存在**（`TurnFlow.cs:227-233` 始终广告 `SKIP_AMBUSH`），故定级 **P2**（"广告 ⇒ 可解"契约违规，非软锁死）。**建议 Codex 的最小修复**：`:204` 的条件改为 `&& candidates.Count >= punish`，并补一条回归测试。

**13.12.4 一个被我排除的假设（记录以免 Codex 追空）**

我一度怀疑"`selectedEntityIds` 在适配层不可达 ⇒ 下载永远不可能成功"。**已证伪**：`src/Adapters/RuntimeMatchGateway.cs:382` 会从 payload 读取 `selectedEntityIds`、`:366-372` 传入 `GameActionRequest`；`LegalActionGenerator.cs:229-232` 也确实广告了该字段。⇒ **传输通道完整**，`RuntimeAiPolicy.ToGameAction:101` 原样转发 `Payload` 即足以执行带目标选择的下载（我的预言机 91 次成功 PULL 即为实证）。**F3 是策略缺口，不是管道缺口。**

**13.12.5 PL 第二轮消融的独立复核（2026-09-11 00:16–00:18，只读）**

PL 在 `build-output\pl-csim\SUMMARY.md`（00:15:15 更新）公布了新一轮控制变量消融。我做了两项独立验证：

**（a）牌组数据事实——完全吻合。** 用 `data\decks\*.json` 的 `{cardId: count}`（每副 20 种 ×3 = **60 张**）加权，直接读 `data\cards\*.json`：

| 阵营 | 牌组张数 | `punishActivatable` | 占比 | PL 报告值 | 判定 |
|---|---|---|---|---|---|
| 深海 | 60 | **36** | **60%** | 60% | ✅ |
| 烈焰 | 60 | 9 | 15% | 15% | ✅ |
| 机械 | 60 | 9 | 15% | 15% | ✅ |
| 古木 | 60 | 6 | 10% | 10% | ✅ |

⇒ PL 的"深海 60% vs 古木 10%，差 6 倍"**是真实数据事实**，不是测量伪影。**（b）PL 的 BUFF 列有误**：PL 报 `BUFF-bearing cards` 为 sea 6 / flame 3 / machine **0** / wood 24；实测（递归匹配 `action == "BUFF"`）为 sea **9** / flame 3 / machine **24** / wood 24。**machine 0 应为 24、sea 6 应为 9**（flame、wood 正确）。这是**报表缺陷（P3）**，不影响 `punishActivatable` 这条载荷结论，但 PL 的 `--no-buff-faction` 控制组的解释若引用了 machine 的 0，需要更正。

**（c）我未独立复跑的部分（诚实边界）**：PL §1 的惩罚链分解（99,516 次 `PUNISH_DRAW` 中 **88% 是响应再入**、11.59 张/链、链深触到 20 上限）、§4 的 `non-activatable` 消融（深海 −61.4 点、古木 +53.6 点等）、§6 的 14 组配置 10,080 局零异常——**这些是 PL 工具的输出，我未复跑**（其 harness 在 `build-output\pl-csim\`，仓库外）。我只确认了 §7 的命令可读、§6 的自检字段存在、以及 §4 依赖的牌组事实（上表）。**在合并决策中应把 (c) 类数字标记为"单一工具来源"。**

---

### 13.13 进程取证：仓库的实时写入者是 `dsh`（DeepSeek Harness），不是 Codex（2026-09-11 00:13–00:19）

**触发**：owner 问"Codex 额度已尽，为什么它还会诈尸？你确定看到的是 Codex 吗？" **结论：owner 的怀疑是对的，我此前的归因是错的。**

**取证方法（已用对照实验校验）**：harness 的文件写入采用原子写——先建 `.<文件名>.<PID>.<guid>.tmpdir\`、写入 `<文件名>.tmp`，再落到目标文件。**嵌入的第二个字段就是写入进程的 PID。**

| 校验 | 观测 |
|---|---|
| 我自己用 `create` 工具写 `%TEMP%\dwprobe-control.txt` | 只出现**最终文件**，**不产生** `.tmpdir`（watcher 75 秒）⇒ 该模式**不是**我的工具产生的 |
| 观测窗口内 `Game.java` / `SUMMARY.md` 的写入事件 | 全部形如 `.Game.java.**21148**.536c4bdd-….tmpdir\Game.java.tmp` ⇒ 写入进程 PID = **21148** |

**进程身份（`Get-CimInstance Win32_Process`）**：

```
21148  node.exe  node --import tsx/esm apps/cli/src/bin.ts "web" --patch web-browse-picker.overlay.yml
   ↑ 监听  127.0.0.1:3080
   └─ 28576  cmd.exe  /d /s /c node --import tsx/esm apps/cli/src/bin.ts "web" …
        └─ 15728  node.exe  pnpm.mjs  dsh web --patch web-browse-picker.overlay.yml     ← **dsh = DeepSeek Harness**
             └─ 10824  cmd.exe  /c pnpm dsh web --patch web-browse-picker.overlay.yml
```

**CPU 对照（20 秒采样，00:15:12–00:15:32）**：

| PID | 进程 | 20 秒 CPU 增量 | 判定 |
|---|---|---|---|
| 29668 | `codex.exe app-server`（父 `ChatGPT.exe`） | **0.00 s** | **完全空闲** |
| 21148 | `dsh` node（`dsh web`） | **+10.22 s**（≈单核 51%） | **满负荷作业** |
| 28916 | `java.exe` | +0.03 s | 空闲 |

**观测窗口（100 秒）内 PID 21148 的写入对象**：

```
src\main\java\com\dominionwars\engine\Game.java          （3 次暂存 + 落盘；mtime 00:14:17、00:17:18 仍在变）
build-output\pl-csim\SUMMARY.md                          （3 次暂存 + 落盘；00:15:15）
```

`build-output\pl-csim\` 是 **PL 自己的预言机目录**（§13.12，已 gitignore），且 `docs\AI_MAILBOX.md:606-615` 的 PL 条目自称"**本会话已落地的改动（`src/main/java`）**"、"`scripts\build.bat` 编译通过"。**两条独立线索同向**：写入者就是 **harness 上正在跑 PL 线的那条 DeepSeek 会话**。

**因此更正以下三处归因（均为我未经证实的推测）：**

1. §13.4 表格"23:29:58 codex 进程启动 ⇒ Codex 已恢复并作业"——**错**。启动的是 Codex **桌面应用**（12 个 `ChatGPT.exe` 子进程，一次应用启动），它**没有**在编辑仓库。
2. §13.9 的标题与正文把 Java 引擎/`AiAgent` 改动记为 Codex——**错**，应为 harness 上的 PL 会话。
3. §13.11 的"Codex 在飞"——**错**，同上。

**同时判定的两件事**：

- `~/.codex` 下的 `.codex-global-state.json`（00:14:35）、`models_cache.json`（00:15:03）持续更新 ⇒ **Codex 桌面应用的 UI/状态在刷新**，但这**不等于在执行 agent 回合**（PID 29668 的 CPU 为 0、子进程里没有任何 `codex exec` 回合进程、也没有 java/javac）。**"进程活着"与"有人在跑回合"是两件事——这正是 owner 看到的"诈尸"幻象的来源。**
- **本报告此前所有"Codex 在飞"的时段结论仍然成立**（Java 移植、`AiAgent` 生命周期策略、48/52→51/52 的收敛、`Game.java` 的持续改写），只是**作者身份要改**。缺陷归属路由（F3/F7/F11 发给 Codex）**不变**，因为那些是"谁该修"，不是"谁在写"。

**⚠️ 对后续操作的直接影响**：写入方**仍在活动**（`Game.java` mtime 00:17:18 在我观测之后）。因此**不要在此时执行 `scripts\build.bat`**（会与写入方争抢 `build\classes`）；Java 侧的合并前基线复跑必须等写入方停止。

> **✅ 00:23 更新**：PL 在 `docs/AI_MAILBOX.md:790-792` 声明"现在写入已停止（两个子代理均已收尾）"。我独立复核：全进程（`codex`/`java`/`node`/`dotnet`）15 秒 CPU 增量 ≈ **0**（最大 0.125 s），`build\classes` 冻结在 **00:22:04**，`Game.java` 冻结在 00:17:18。**判定成立，禁令解除。** 规范路径基线已复跑，见 §13.16。

---

### 13.14 王城破城胜利条件：五场景跨引擎分歧，且既有测试固化了过宽行为（F17 / F18）

> ⚠️ **本节是过程记录；其中"C# 过宽"的结论与修复建议已被 §13.17 取代。**
> **已作废**：② 的"两种读法"对照表结论、③ 的"测试固化了过宽行为"、⑤ 的**方案 (a) 收窄 `:138`**、⑥ 的"建议 (a)"。
> **仍有效**：**F18 判断**、**③-b 覆盖矩阵恰好缺一格**、**④ 可达性实证**（§13.17 全部确认），以及 ①②⑥ 的**事实性观察**（规则原文、代码结构、数据普查）。

owner 在 2026-09-11 决定：**随从型首领自带一条胜利条件「王城被破坏」**，且**不内置写死**（由 `leaderDef.winCondition` 显式声明——owner 选的 A 方案，因为别的随从首领有别的获胜方式）。落点检查：

**① 数据侧已就位 ✅**

| 文件 | 统领 | `type` | `leaderDef.winCondition` | 参数 |
|---|---|---|---|---|
| `data/cards/flame.json` | `flame_leader` | **MINION** | **`ROYAL_CASTLE_BREAK`** | — |
| `data/cards/machine.json` | `machine_leader` | SPELL（地标，耐久 8） | `PULL_TOTAL_GE` | 6 |
| `data/cards/machine.json` | `machine_alpha` | **MINION** | `PULL_TOTAL_GE` | 6 |
| `data/cards/neutral.json` | `shadow_of_fate` | **MINION** | `NONE` | — |
| `data/cards/neutral.json` | `gate_of_fate` | AMBUSH | `AMBUSH_TRIGGER_WIN` | — |
| `data/cards/sea.json` | `sea_leader` | SPELL | `OPP_DISCARD_TOTAL_GE` | 18 |
| `data/cards/wood.json` | `wood_leader` | SPELL（耐久 20） | `GIANT_HEALTH_GE` | 512 |

⇒ 全仓**恰好只有 `flame_leader` 一家持有** `ROYAL_CASTLE_BREAK`，且它是 MINION；`machine_alpha`（MINION）已按 owner 决定改到上传/下载轴、**不带**破城条件。**与 owner 的决定一致，无数据缺陷。**

**② 但实现侧在"谁赢"上出现五场景三方分歧 ❌**

判定链：C# `src/Engine/Effects/EffectRuntime.State.cs:196-224`；Java `src/main/java/com/dominionwars/engine/Game.java:597-609`；规范 `docs/RULES.md:105/137/138`。

| # | 场景 | `RULES.md` | **C#** | **Java** |
|---|---|---|---|---|
| 1 | 破城方持有、防守方不持有（烈焰破） | 持有者胜 | 破城方胜 ✅ | 破城方胜 ✅ |
| 2 | 防守方持有、破城方不持有（对手破烈焰） | **持有者胜（:137「不问谁破城」）** | **防守方胜 ✅** | **无人获胜 ❌（F7）** |
| 3 | 双方均随从、**双方均不持有** | 未规定；`:138` 字面 ⇒ 破城方胜 | 破城方胜（`win.castle_break_minion`） | **无人获胜 ❌** |
| 4 | 双方均随从、**仅防守方持有**（`machine_alpha` 破 → 烈焰守） | **持有者胜（烈焰）** | **破城方胜（机械）❌（F17）** | 无人获胜 ❌ |
| 5 | 双方**均持有** | 破城方胜（`:138`「避免平局」） | **无人获胜 ❌（F18）** | 破城方胜 ✅ |

**F17（P1，规则冲突）** —— `EffectRuntime.State.cs:200-207`：

```csharp
if (breakerLeader.IsMinion && defenderLeader.IsMinion)
{
    DeclareWinner(context.SourcePlayerIndex, "win.castle_break_minion", context);
    return;   // ← 提前返回，防守方的 ROYAL_CASTLE_BREAK 被动条件永不评估
}
```

该分支的触发条件只是"**双方统领都是随从**"，**不要求任何一方持有** `ROYAL_CASTLE_BREAK`。⇒ 当**只有防守方持有**时（第 4 行场景），它会**抢走防守方的招牌胜利**，与 `RULES.md:137`「被动，不问谁破城」直接冲突。**这正是 owner 在 2026-09-11 明确担心的那种情形**（"对方是随从型首领……你击破王城就会满足对方胜利条件"）——在 `machine_alpha` 身上反了过来。

**⚠️ 定性必须精确（重要，避免误判责任人）**：C# 这段代码是 `RULES.md:138` **字面文本的忠实实现** —— `:138` 原文就是"双方统领均为随从型统领的对局中，主动破城方直接获胜"，**没有**附加"持有条件"这一前提。因此：

| 读法 | 谁合规 |
|---|---|
| **字面读**（触发 = 双随从） | **C# 合规**；Java 缺这一分支 ⇒ Java 偏差 |
| **理由读**（触发 = 双方均持有，即 `:138` 自述的"避免双方条件同时满足"） | **C# 过宽**；Java 在第 5 行反而正确 |

⇒ **F17 的本质是两个引擎各自忠实实现了 `:138` 的两种互不相容的读法**，根因是**规范文本自相矛盾**（= F19），**不是 C# 写错了代码**。在没有 owner/PL 定稿前**不要动 C#**（否则可能把"合规"改成"违规"）。我把 F17 定为 P1 的理由是：**该冲突会实际改变胜负归属**，而 `:137` 的"不问谁破城"是 owner 2026-09-11 口头确认过的语义方向（"随从型首领自带一条胜利条件：王城被破坏"——一个首领**自带**的胜利条件被同型对手无条件覆盖，与该表态的意图不符）。

**F18（P2，潜伏）** —— 同一函数 `:214-217`：`HasCastleBreakWinCondition` 两边相等即 `return`（不判任何一方胜）。当**双方均持有**时这正是 `RULES.md:138` 要解决的那种平局，代码却选择"无人获胜"。今天不可达（无镜像对局、只有一家持有），但**下一个持有 `ROYAL_CASTLE_BREAK` 的统领一落地就会暴露**。**F18 可以无条件判定为缺陷**（`:138` 在此场景**没有**歧义：双方均持有时破城方胜，Java 亦如此），与 F17 不同，它不需要等定稿。

⇒ **F17 与 F18 的方向相反**：C# 在第 4 行过宽、在第 5 行过窄；Java 恰好相反。三者互不一致 ⇒ `ROYAL_CASTLE_BREAK` 仍不是单源语义（F7 的同一根因，未被覆盖完全）。

**③ 既有测试把过宽行为固化成了"期望"**

`src/Engine/Tests/EffectRuntimeTests.cs:604-642` 的 `BreakingCastleWithActiveMinionLeadersGivesBreakerPriority`：双方统领都是 MINION，**且双方都没有 `leaderWinCondition`**，断言 `WinReason == "win.castle_break_minion"`、`WinnerPlayerIndex == 0`。

⇒ 这条测试证明的不是 `RULES.md:138` 那句理由（"双方条件同时满足"），而是"**双方都是随从 ⇒ 破城方无条件获胜**"。**`RULES.md:138` 自身也自相矛盾**：触发条件写的是"双方统领均为随从型"，理由写的却是"避免双方条件同时满足时产生平局"——触发条件比理由宽。这是**规范文本**内部的不一致，需 PL/owner 定稿（见下）。

**③-b 覆盖矩阵恰好缺一格，而 F17 就住在那一格里（本次核对的决定性细节）**

逐字读了 4 条破城用例的**构造**（不含断言）：

| 用例 | 破城方 `isMinion` | 防守方 `isMinion` | 破城方持有条件 | 防守方持有条件 | 期望 |
|---|---|---|---|---|---|
| `:551` `…AppliesCountdownForcesLeaderAndChecksCastleVictory` | —（未设） | **true** | **✅ 有**（`:560`） | 无 | 破城方胜 `royal_castle_break` |
| `:605` `…WithActiveMinionLeadersGivesBreakerPriority` | **true**（`:615`） | **true**（`:625`） | **无** | **无** | 破城方胜 `castle_break_minion` |
| `:645` `…RoyalConditionCanAwardActiveDefender` | 否（`:653` 仅 `isLeader`） | **否**（`:661` 只设 `leaderWinCondition`） | 无 | **✅ 有** | **防守方**胜 `royal_castle_break` |
| `:678` `…IgnoresHiddenRoyalLeader` | — | — | — | 有（隐藏） | 无人胜 |
| **缺** | **true** | **true** | **无** | **✅ 有** | ← **F17 场景，无任何测试覆盖** |

⇒ 三条既有用例各自都**正确**、与 `RULES.md:105/137` 一致；它们的并集**唯一漏掉**的组合正是"**双方均随从 且 仅防守方持有**"。这不是"实现写错了某个用例"，而是**实现里多了一条比任何用例都更宽的预判**（`:200-207`），恰好只在那个空格里生效。**⇒ 修复时不需要推翻任何既有断言，只需补上这一格的新用例 + 删预判。** 这让 F17 的修复成本与风险都很低。

**③-c 实测：5 条用例全绿 ⇒ 实现确实按"双随从即胜"在跑**

```
dotnet test src\Engine\Tests\DominionWars.Engine.Tests.csproj -c Debug --filter "FullyQualifiedName~BreakingCastle"
   → 已通过! 失败: 0，通过: 5，已跳过: 0，总计: 5（59 ms）
```

⇒ `:605` 那条（**双方均随从、双方都不持有**）**当前是通过的**，即引擎此刻确实会对该组合判破城方胜。由于 `:200-207` 的 `return` 位于 `HasCastleBreakWinCondition` **之前**，且分支条件**只**看两个 `IsMinion`，**给防守方补上 `ROYAL_CASTLE_BREAK` 不会改变这条路径的任何一步** ⇒ F17 由"通过的测试 + 早退结构"即可**演绎确定**，无需新用例即成立（新用例只用于防回归）。

**④ 该分支不是死代码 —— 实测它在真实对局中被走到**

我的 C# 权威预言机（`%TEMP%\dw-cs-sim`，600 局/变体）获胜理由分桶：

| 变体 | `win.royal_castle_break` | `win.castle_break_minion` | 归属（按初始 deck 标签） |
|---|---|---|---|
| A decline | 273 | — | `flame_leader`×273 |
| **B accept** | 244 | **9** | `flame_leader`：`royal_castle_break`×244、**`castle_break_minion`×9** |
| C pullfirst | 175 | — | `flame_leader`×175 |
| D reverse / E nocastle | — | — | — |

那 9 局的双随从前提只能由**机械方统领已晋升为 `machine_alpha`（MINION）**满足——烈焰与深海/古木（SPELL）永远不构成"双随从"，且 `ForceLeaderOut` 只会召出 `IsLeader` 卡。`data/cards/machine.json` 的 `machine_leader.leaderDef.landmarkTiers[2] = {chant: 1, summon: "machine_alpha"}` 提供了这条路径。

⇒ **三条结论同时成立**：(a) 地标二层晋升 `machine_alpha` 在真实对局中**确实发生**；(b) `win.castle_break_minion` 分支**确实可达**；(c) 这 9 局的胜负**与** `:137` 的被动判定方向一致（烈焰既持有又是破城方），所以**过宽尚未造成可观测量偏差**——F17 是**规则正确性**缺陷，不是统计偏差（实测频率 0/3000，因为机械在本策略下几乎不破城）。

**⑤ 两种读法各自的修复（发给 Codex —— 但必须先拿到定稿）**

**先做、与定稿无关的一步（修 F18，两边都要做）**：`:214-217` 的"两边相等即 `return`"只对"双方**都不**持有"正确；对"双方**都**持有"错误（`:138` 明确要求破城方胜，Java 也如此）。改为：

```csharp
if (!breakerHolds && !defenderHolds) return;                  // 无人持有 → 不立即裁决，只走倒计时
DeclareWinner(breakerHolds ? breaker : defender.PlayerIndex,  // 持有者胜；双方均持有 → 破城方（:138）
              "win.royal_castle_break", context);
```

**再按定稿二选一**：

| 定稿选择 | 对 `:200-207` 的处理 |
|---|---|
| **(a) 收窄 `:138`** 为"双方**均持有**该条件 ⇒ 破城方胜"（保住 `:137`「不问谁破城」） | **删除整个 `IsMinion && IsMinion` 预判**。此后双随从但无人持有 ⇒ 不立即裁决（与今天不同，今天会判破城方胜 ⇒ **这是行为变更**）；双随从但仅防守方持有 ⇒ **防守方胜**（今天的错误被修掉）。 |
| **(b) 保留 `:138` 字面**"双方均为随从 ⇒ 破城方胜"（即**承认它是 `:137` 的例外**） | **保留预判 + 给 `:137` 补写例外条款**。C# 现状基本正确，仅需上面那一步修 F18。**行为变更最小。** |

两案都必须**改 `EffectRuntimeTests.cs:604-642` 与新增那一格用例**（**测试代码属 Codex**，我不提交）；**Java** 需补上第 3/4/5 行整条被动链（F7），否则两引擎仍不一致。

**⑥ 需 owner 一句话定稿的点**：第 3/4 行场景下，"双方均随从但只有一方持有"应算**持有者胜**（保住 `:137`）还是**破城方胜**（`:138` 字面）？

**我的建议是 (a) 收窄**，理由是：`RULES.md:105` 把 `ROYAL_CASTLE_BREAK` 描述为一个首领**自带**的胜利条件，而 owner 2026-09-11 的表态（"随从型首领自带一条胜利条件：王城被破坏"）意在让它成为一个**真正的威胁**；若同型对手可以无条件覆盖它（(b)），那么"随从 vs 随从"对局里先破城反而变成纯收益，"防备对方破城"的设计意图落空。但**这是最终规则裁决，我只上报，不修改 `RULES.md`。**

---

### 13.15 `SimMain` 的读数强烈依赖 `N`：小样本数字不可与 N=300 混用（H1 / H2）

**背景**：PL 在 `docs/AI_MAILBOX.md:766-768` 用 `SimMain 20` 读数（240 局）作为"Java 已与 C# 同机制"的证据。我复跑时先得到 N=300 的 13.2%（机械），与 PL 的 19.2% 不符 ⇒ 追查到测试台本身。

**① 先排除"代码不同"**：`SimMain` **是确定性的**。我复跑 `SimMain 20` **两次**，输出与 PL 逐位一致：

```
== 模拟 240 局，平均回合数 15.133333333333333 ==
flame 51.7% (62/120) / machine 19.2% (23/120) / sea 76.7% (92/120) / wood 52.5% (63/120)
```

⇒ 同一份构建、同一命令 ⇒ **差异来自采样，不来自代码**。

**② 实测 N 敏感度（同一构建，全部确定性，可复现）**

| `N` | 局数 | 平均回合 | 烈焰 | **机械** | 深海 | 古木 |
|---|---|---|---|---|---|---|
| **20** | 240 | 15.13 | 51.7% | **19.2%** | 76.7% | 52.5% |
| 40 | 480 | 15.06 | 50.0% | 15.0% | 78.8% | 56.3% |
| 60 | 720 | 14.99 | 49.7% | 14.4% | 80.0% | 55.8% |
| 100 | 1 200 | 14.85 | 50.5% | 12.5% | 81.2% | 55.8% |
| 200 | 2 400 | 14.76 | 50.9% | 13.2% | 80.2% | 55.8% |
| **300** | 3 600 | **14.79** | 51.2% | **13.2%** | 80.3% | 55.3% |
| 600 | 7 200 | 14.83 | 51.2% | 13.4% | 80.1% | 55.3% |

机械横跨 **12.5% – 19.2%（6.7 pts）**，`N=20` 恰是偏离最大的点。**平均回合则稳定在 14.76–15.13 ⇒ 结论落在 10–20 目标带一事对 N 不敏感；阵营胜率对 N 敏感。**

**③ 这不是"随机抽样噪声"，而是种子分块偏置**

`src/test/java/com/dominionwars/test/SimMain.java:39`：`seed = a*1000 + b*100 + k`，`firstPlayer = k % 2`，`k ∈ [0, N)`。⇒ **`N=20` 的样本是 `N=300` 样本的真子集**（对同一 `(a,b)` 对，前者用 `k=0..19`）。既然样本是**嵌套**的，读数随 N 单调补齐却从 19.2% 漂到 13.2%，只能说明**前 20 个种子恰好对机械有利**，而不是"小样本有误差、大样本更准"——**低 N 读数是偏置的早期分块，不是随机子样本。**（可诊断信号：若某指标随 N 单调漂移，先怀疑分块偏置，不要先怀疑平衡改动。）

**④ 附带的种子碰撞（H1，P2）**

`seed = a*1000 + b*100 + k` 中 `k` 的步长上限是 **100**（因为 `b` 的权是 100）。因此：

| `N` | 被 ≥2 个对局共享的 seed 数 | 唯一 seed 数 |
|---|---|---|
| ≤ 100 | **0** | 12×`N` |
| 300 | **1 200 / 2 200** | 2 200 |

例：`seed = 200` 同时属于 `(a=0,b=1,k=100)` 与 `(a=0,b=2,k=0)`。⇒ "每个对局独占一段互不相交的种子"这一设计意图在 `N > 100` 时**静默失效**。建议改为 `seed = (long)(a * decks.size() + b) * N + k`（互不相交且保持嵌套）。

**⑤ 建议动作**

1. **PL/owner**：不要在任何报告里引用 `SimMain < 200` 的阵营胜率；`SimMain 20` 的机械 19.2% 比 N=300 的 13.2% **高 6 pts**。
2. **Codex**：修 `SimMain.java:39` 的种子公式（H1），并考虑在摘要里**同时打印 N**，避免读数脱离样本量被引用。
3. **我**：`docs/BALANCE.md` 的基线表与所有历史 `SimMain` 引用都应补上 N（下轮补齐）。

---

### 13.16 合并前规范路径基线（2026-09-11 00:23，写入方停止后）

写入方停止已复核（见 §13.13 末条）：全进程 15 秒 CPU 增量 ≈ 0，`build\classes` 冻结于 00:22:04。⇒ 禁令解除，改用**仓库自己的规范路径**（`scripts\build.bat` → `build\classes`）复跑，替换 00:06 的旧快照。

```
cmd /d /c "cd /d <repo> && call scripts\build.bat"                              → Build complete, exit 0
java -cp "build\classes;build\test-classes" com.dominionwars.test.TestMain      → 通过 59 / 59
java -cp "build\classes;build\test-classes" com.dominionwars.test.SimMain 300   → 3 600 局，平均回合 14.7936…
   flame 51.2% (922/1800) / machine 13.2% (238/1800) / sea 80.3% (1445/1800) / wood 55.3% (995/1800)
```

**三点结论**：

1. **规范路径与我的仓外编译（`%TEMP%\dw-javac-out3`）读数逐位一致** ⇒ 两套编译产物等价，`build\classes` 未被写入方写坏。
2. **Java 侧已收敛且稳定**：`TestMain` **59/59**（§13.11 的 51/52 已关闭）；平均回合 **14.79** 落在 `docs/BALANCE.md` 的 10–20 带内；**98 处新增测试** 覆盖了生命周期（COMMIT/PUSH/PULL/ROLLBACK）、地标层、`chainLimit` 边界。
3. **唯一超带项仍是深海 80.3%**（与 C# 侧 74.0% 同向）⇒ 强化 §13.12 的 F10：这是**跨引擎、跨测试台、跨策略**都成立的结论，且 **N 不敏感**（N=20→600 全在 76.7–81.2%）⇒ **可以据此定结论，不必再等更多样本。** 相对地，机械 12.5–19.2% 的读数**不可**用于定结论（§13.15）。

⇒ **合并前基线的"Java 侧"一项已完成**，`java-reread-after-codex` 可关闭。

---

### 13.17 ⚠️ 定稿复核：**F17 作为引擎缺陷整体撤回**；本项从"改代码"变为"改规范文本 + 修 F18"（2026-09-11 00:34）

> **本节取代 §13.14-②/⑤/⑥ 中"C# 过宽、建议收窄 `:138`（方案 a）"的结论与建议。** §13.14 保留为过程记录，**执行请以本节为准**。

**复核所依据的 owner 表态（2026-09-11，逐字）**

1. 「随从型首领自带一条胜利条件：王城被破坏」……「这并不冲突」
2. 「不建议内置写死 分开吧 别的随从首领有别的获胜方式呢」……「alpha 现在已经在改了 正在改成上传下载相关的胜利条件 **所以不需要破城**」
3. 「**当然 A，不然大家都不打王城了**」

**由 (2)：`machine_alpha` 不持有破城被动胜 ⇒ 第 4 行场景确实可达**

(2) 明确 `machine_alpha`（`type = MINION`）改持 `PULL_TOTAL_GE 6` 后「**不需要破城**」，且 (2) 反对"内置写死"。⇒「随从型」只是**默认**携带破城条件，可被该首领**自己声明的新条件替换**（**不是叠加**）。这**确认**了 §13.14-④ 的结论：`machine_alpha` 破城、`flame_leader` 守城这一组合（"双方均随从、仅防守方持有"）**是可达状态，不是空洞场景**。

**由 (3)：主动破城必须受奖励 ⇒ 第 4 行的应然结果就是破城方胜**

(3) 的**理由**——"不然大家都不打王城了"——要求"主动破城"必须被奖励。据此重放 §13.14-② 的五场景：

| # | 场景 | 按 (1)(2)(3) 的**应然**结果 | C# 现状 |
|---|---|---|---|
| 1 | 破城方持有、防守方不持有 | 破城方胜 | 破城方胜 ✅ |
| 2 | 防守方持有、破城方不持有（非双随从） | 持有者（防守方）胜 | 防守方胜 ✅ |
| 3 | 双方均随从、**均不持有** | 破城方胜（`:138`） | 破城方胜 ✅ |
| 4 | 双方均随从、**仅防守方持有** | **破城方胜**（`:138` 优先级；(3) 明确要求） | 破城方胜 ✅ |
| 5 | 双方**均持有** | 破城方胜（`:138`） | **无人获胜 ❌ F18** |

⇒ **C# `:200-207` 在每一个可达场景上的结果都与 owner 定稿一致。因此 F17 作为"引擎缺陷"撤回；代码不需要改。**

**⚠️ 一点诚实的不确定：「当然 A」的标签**

原始问答里的 A/B 选项编号没有留在可检索记录中（该轮消息在会话压缩时被省略）。但**同一句的理由**「不然大家都不打王城了」与我 §13.14-⑥ 给出的 (a)/(b) 表**方向相反**：(a)「持有者胜」会让"仅防守方持有"时的主动破城者**受罚**，正是这句理由要排除的情形。故我按**理由**而非标签执行，并把这里标为"**理由明确、标签待确认**"。**若 owner 原意确实是方案 (a)，则 §13.14-⑤ 的方案 (a) 重新生效**——请 PL 在定稿时连同下面第 1 项一并确认。

**明确撤回的东西（请勿再引用）**

1. ❌ **§13.14-⑤ 方案 (a)「收窄 `:138` 为'双方均持有'」** —— 与 (3) 直接冲突。
2. ❌ **§13.14-③「既有测试 `:604-642` 把*过宽*行为固化成了期望」** —— 该用例（双方均随从、双方都不持有 ⇒ 破城方胜）正是 (3) 确认的规则，**应保留**。
3. ❌ **§13.14-② 表中"C# 在第 4 行过宽、Java 在第 5 行过窄、两者恰好互换"的对称表述** —— 第 4 行的应然结果就是破城方胜。
4. ❌ **§0 第 18 行、§14 F17 行、"给 Codex 的 🔴 ①：先别改 C# 等定稿"** —— 后者结论（**不要改 `:200-207`**）恰好**仍然正确**，但理由变了：不是"怕改错"，而是**它本来就对**。

**保留、且本次予以确认的东西**

1. ✅ **F18 仍是真缺陷**：`EffectRuntime.State.cs:214-217` 在"双方均持有"时 `return`（无人获胜），而 (3) 的"打破平局"意图与 `:138` 都要求**破城方胜**。§13.14-⑤ 给出的 F18 修补代码**依然适用**，**可立刻做，不必等定稿**。今天不可达（仅 `flame_leader` 持有该条件），且在第 3/4 行场景被 `:200-207` 抢先 `return` 掩盖；只在"双方均持有 **且** 至少一方非随从"时暴露 ⇒ **今天不可达**。
2. ✅ **③-b 覆盖矩阵仍缺一格**（双方均随从 + 仅防守方持有）⇒ **仍需补那条用例**，但**目的不是改行为**，而是把 (3) 的定稿**钉进回归**：今天**没有任何用例**保护第 3/4 行（`:604-642` 只覆盖"都不持有"）。
3. ✅ **F20（`SimMain` 小 N）** 完全不变。
4. ✅ **F7（Java 分歧）不变，且要求更明确了**：Java 需补 **(i)** 双随从优先级分支（全仓 `git grep castle_break_minion -- src/main/java` 仍**零命中**）与 **(ii)** 第 2/3/4 行的被动链（`Game.java:598` 的 `checkRoyalCastleWin(breakerIdx)` 形参只有破城方 ⇒ 结构上无法表达"不问谁破城"）。

**⇒ 责任划分的变化（本节最重要的一条）**

| 原来（§13.14） | 现在（§13.17） |
|---|---|
| 等 owner 定稿 → Codex **改引擎行为**（删预判） | **不需要改引擎行为**。owner 意图已明确：C# 保持现状 |
| owner 只需裁决"谁赢" | **PL/owner 改文本**：① `:138` 的**理由**要与**触发条件**对齐（现理由"避免双方条件同时满足"窄于触发条件"双方均随从"，这是 F19 的本体）② 给 `:137`「被动，不问谁破城」补一条**例外指针**（双方均随从的对局 ⇒ 破城方优先）③ `:105`「当前仅 flame 持有」应改为"`flame_leader` 持有；其他随从型统领若声明了自己的胜利条件则以自己声明的为准"——否则与 `:109`「候选机制不得通过隐藏计数器自动增加新的胜利条件」并读会显得冲突 |
| — | **Codex 只做**：F18 + Java 对齐（F7）+ 补那一格用例 |

### 13.18 `docs/BALANCE.md` 的平衡基线与复现命令早已失效（F21，2026-09-11 00:34）

**方法**：按 §13.15 的结论，逐字复核仓库内所有 `SimMain` 引用，并用**同一份确定性构建**在各 N 下实测。

**本次实测（同一构建，`build\classes`，2026-09-11 00:31）**

| N | 局数 | 平均回合 | 烈焰 | 机械 | 深海 | 古木 |
|---|---|---|---|---|---|---|
| **8** | 96 | 14.98 | **45.8%** | **18.8%** | **79.2%** | 56.3% |
| 12 | 144 | 15.28 | 48.6% | 19.4% | 76.4% | 55.6% |
| 20 | 240 | 15.13 | 51.7% | 19.2% | 76.7% | 52.5% |
| 300 | 3 600 | 14.79 | 51.2% | 13.2% | 80.3% | 55.3% |

**`docs/BALANCE.md` 的四处失效**

| 行 | 原文 | 问题 |
|---|---|---|
| `:3` + `:7-10` | "## 1. **最新**模拟数据（AI 对 AI，每对阵 **8 局** × 12 对阵 = 96 局）"，表为 烈焰 56.3 / 机械 52.1 / 古木 50.0 / 深海 41.7 | 标题声称 96 局的"最新"数据，但**用 `:18` 给出的那条命令复现不出它**：今天 N=8 实测为 烈焰 45.8 / 机械 **18.8** / 深海 **79.2** / 古木 56.3 ⇒ **机械差 33.3 pts、深海差 37.5 pts、古木差 6.3 pts、烈焰差 10.5 pts**。⇒ 表**不是该命令的产物**，且是历史快照被标成"最新" |
| `:13` | "所有阵营胜率落在 40%–60% 的可接受带内" | **不再成立**：按 `:18` 的命令复现，机械 **18.8%**、深海 **79.2%** ⇒ 今天**机械 18.8% 与深海 79.2% 都落在带外**（古木 56.3%、烈焰 45.8% 仍在带内） |
| `:18` vs `:46` | `:18` 复现命令用 **`SimMain 8`**；`:46` 却要求"用 `SimMain 30` **以上**的样本量验证任何数值改动" | **同一文档内自相矛盾**，而且**两者都不够**：按 F20，`SimMain 30` 的 `k` 仍从 0 起算 ⇒ **仍是嵌套偏置样本**（机械 N=20 读 19.2%、N=300 读 13.2%）。**阵营胜率应取 `N ≥ 200`** |
| `:21` | "（参数为每个对阵的局数，**可加大以降低方差**）" | 量级判断错误：低 N 的问题**不是方差而是系统性偏置**（嵌套早期分块）⇒ 加大 N 不是"降方差"，而是**去掉一个 +6 pts 的系统偏差** |
| `:73` | "规则测试 **35/35** 通过" | 陈旧。当前为 **59/59**（§13.16） |

**同一缺陷复制到了架构文档**：`docs/DESIGN.md:91` 的复现命令**同样是 `SimMain 8`**。另：`docs/BALANCE.md:54` 的"基线：Castle Prototype v7（机械 70.8% / 古木 60.4% / 烈焰 39.6% / 深海 29.2%）"**无日期、无引擎标识、无 N**，不可复现。

**为什么定 P2 而不是 P3（影响）**：`docs/BALANCE.md` 是 `docs/AI_WORKFLOW.md` 指定的平衡权威文档之一。任何人（未来的我、Codex、或外部 Claude）照 `:18` 复现，都会得到**与文档冲突**的数字，而最可能的错误反应是"**去调机械的数值**"——从而掩盖真正的两个原因：**(i) `SimMain` 小 N 偏置（F20）；(ii) 这条命令测的引擎与当前 91 卡规则集的差距（§13.8：Java 侧不支持提交/上传/下载、地标层、扎根/疯长/512）**。§13.15 已量化：拿 `SimMain` 的机械读数去调数值，方向会是错的。

**建议修法（QA 只提案；`docs/BALANCE.md` 属 PL 权限，我不改）**
1. `§1` 的表改为**带 N、带日期、带构建标识**的读数，并把 `:18` 的复现命令统一为 `SimMain 300`。
2. `§4` 第 2 条改为："**阵营胜率取 `N ≥ 200`**；**平均回合**在 `N ≥ 20` 即可稳定（对 N 不敏感）。"
3. `§1` 的"最新"与 `:73` 的"35/35"加历史标注；`§5`/`DESIGN.md:91` 的 `SimMain 8` 一并更正。
4. `:54` 的基线补日期与 N。

**发布数字前的复现校验（00:36，同一冻结构建）**：在 `build\classes`（`Game.class` 冻结于 **00:23:42**，晚于最新 Java 源 `Balance.java` 00:19:57 / `Game.java` 00:17:18，即该构建已包含全部当前源）上把本节与 §13.16 引用的读数**逐位重跑确认**：

| 命令 | 局数 | 平均回合 | 烈焰 | 机械 | 深海 | 古木 | 与已发布读数 |
|---|---|---|---|---|---|---|---|
| `SimMain 8` | 96 | 14.979 | 45.8% | **18.8%** | **79.2%** | 56.3% | 一致 ✅ |
| `SimMain 300` | 3 600 | 14.794 | 51.2% | 13.2% | 80.3% | 55.3% | 一致 ✅ |

⇒ 上面 `:13` 的结论按**重跑后的原值**写：`BALANCE.md:13` 的"所有阵营胜率落在 40%–60% 带内"今天**只有机械（18.8%）与深海（79.2%）越界**，烈焰 45.8% 与古木 56.3% 仍在带内——**是"部分失效"而非"全表失效"**，`§1` 表格本身仅 2 项不可复现（机械差 33.3 pts、深海差 37.5 pts；烈焰差 10.5、古木差 6.3）。这个更正不影响 F21 的定性与修法。

---

### 13.19 `HEAD` 与工作树是两个不同的引擎（F22）

**起因**：在给 §13.18 / §13.16 的读数做发布前复现校验时，我意识到一件事——那些读数是在**当前工作树**上跑出来的，而工作树里有**未提交**的 Java 改动。于是我**不动工作树**，把**提交态**导出到仓外独立重建，直接量了一次差。

**方法（可复现）**

```powershell
$dst = "$env:TEMP\dw-head-audit"
Remove-Item -Recurse -Force $dst -ErrorAction SilentlyContinue; New-Item -ItemType Directory $dst | Out-Null
git archive --format=tar HEAD | tar -x -C $dst
& $env:ComSpec /d /c "cd /d $dst && call scripts\build.bat"        # exit 0（干净重建）
java -Dfile.encoding=UTF-8 -cp "$dst\build\classes;$dst\build\test-classes" com.dominionwars.test.TestMain
java -Dfile.encoding=UTF-8 -cp "$dst\build\classes;$dst\build\test-classes" com.dominionwars.test.SimMain 300
```

（`tar` 对非 ASCII 路径报 `Invalid empty pathname` ⇒ `docs/卡牌设计包_2026-08-15/`、`web/修改指南.md` 等未导出，但 **Java 源完整**（27 个 main / 3 个 test）且 `data/` 下 json 数与工作树一致 ⇒ **不影响本结论**。）

**实测对比（同一台机器、同一 JDK 23，同一 `SimMain.java`）**

| 指标 | **提交态 `HEAD`**（＝可合并的东西） | **工作树**（＝我此前引用的东西） | `docs/BALANCE.md` 目标 |
|---|---|---|---|
| `scripts\build.bat` | exit 0 | exit 0 | — |
| `TestMain` | **38 / 38** | **59 / 59** | `:73` 写 35/35（两个都不对） |
| `SimMain 300` 平均回合 | **22.61**（**带外**） | **14.79**（带内） | 10–20 |
| 烈焰 | 59.1%（带内） | 51.2%（带内） | 40–60% |
| 机械 | 14.3%（带外） | 13.2%（带外） | 40–60% |
| 深海 | **90.9%**（带外） | 80.3%（带外） | 40–60% |
| 古木 | **35.7%**（**带外**） | 55.3%（带内） | 40–60% |
| `SimMain 8` 平均回合 | 21.08 | 14.98 | — |

**未提交的到底是什么**（`git diff --stat -- src`）：

| 文件 | 插入 |
|---|---|
| `src/main/java/.../engine/Game.java` | +240 |
| `src/main/java/.../engine/Effects.java` | +197 |
| `src/main/java/.../ai/AiAgent.java` | +143 |
| `src/main/java/.../model/CardDef.java` | +114 |
| `src/main/java/.../engine/CardInstance.java` | +22 |
| `src/main/java/.../data/Balance.java` | +20 |
| `src/main/java/.../engine/PlayerAgent.java` | +24 |
| `src/main/java/.../engine/PlayerState.java` | +12 |
| `src/test/java/.../test/TestMain.java` | +658 |
| **合计** | **1 402 行插入 / 30 行删除** |

`git branch -avv` 的全部落点（含 `qa/verify-2026-09-10`、`main`、各 `agents/*`、`archive/*`）与 `git stash list`（仅一条无关的 `codex-cycle-flip-wip-pre-existing`）**都不包含**这批改动 ⇒ **它们只存在于这一个工作树里**。

**结论（4 条）**

1. **我此前公布的"59/59、平均回合 14.79、深海 80.3%"描述的是工作树，不是任何 ref 指向的修订版。** 报告与交接文档中所有"合并前基线 Java 侧完成（F8 关闭）"的措辞都要按这一条收窄。
2. **按当前 `HEAD` 评估合并，得到的是明显更差的基线**：平均回合 **22.61** 已超出 `BALANCE.md` 的 **10–20** 带；深海 **90.9%**、古木 **35.7%** 都在 40–60 带外。反而那 1 402 行未提交改动让平均回合与古木回到带附近。
3. **差异 100% 来自那批未提交文件本身**（8 个 `src/main/java` 文件 + `TestMain.java`；`SimMain.java` 两边一致，`data/` 一致）⇒ 这批 WIP **不是噪声，是有效工作**，但它**尚未进入版本历史**。
4. **数据丢失风险**：`git checkout` / `reset --hard` / `clean -fd`（或 IDE 的"放弃更改"）会**静默销毁 1 402 行**。这批工作**不在任何分支/ref/stash 里**；QA 已在**仓库外**留了一份可复原的快照作保险（见下），但快照**不能替代版本历史**。

**归属与建议（不是引擎缺陷，是流程缺陷 ⇒ 归 Codex/harness，我不替写入方提交他们的在飞工作）**

- **优先**：把这批 WIP 落到一个**明确命名的提交或分支**（哪怕信息写 `WIP`），让它进入版本历史、可被引用、可被回退。
- 或者由 **owner** 明确决定归档/丢弃这两条路里的哪一条（丢弃是**不可恢复**操作，需要明确同意）。
- **在这之前**：① 不要用 `HEAD` 评估合并；② 不要把工作树读数当成"已交付成果"；③ 任何需要"干净基线"的验证都必须先声明**测的是哪个修订版**。
- 顺带印证 **F21**：`BALANCE.md:73` 的「35/35」对**两个**修订版都不对（提交态 38、工作树 59）。

**QA 的取证保险（仓外，不进入版本库）**：为避免这批在飞工作在等待落地期间被意外销毁，我把**当时的工作树状态**存到了 QA 会话目录
`…\.copilot\session-state\cb0c2a5e-…\files\wip-snapshot-20260911\`：
`src-wip.patch`（`git diff --binary -- src` 原样输出，108 564 字节、无 BOM、
**SHA256 `7EA152290EED5177E03A167A57EAA77B9E540B2253FA8710AD0FE619E5607BA5`**，
快照当时 `git apply --check --reverse` **exit 0**）、
9 个 `src/**` 文件的**逐字节副本**（SHA256 与工作树比对 **9/9 一致**）、4 份写入方未提交的 `docs/*.md`，以及一份说明恢复步骤的 `README.md`。

⚠️ **三条边界必须同时说清**：① 这是**仓库外的临时保险**，不是交付物，不改变"正解是落地为具名提交/分支"这一条；
② 它冻结在 **00:5x**，此后写入方若继续改，快照即过期（恢复前先比对 9 个文件）；
③ QA **没有**用它提交、也没有动 Git 指针——`git branch -avv` / `stash list` 现状不变。

---

### 13.20 PL 的 C# 权威实测已被独立复现（10 080 局逐字段一致）＋ F23 报表问题五处（2026-09-11 00:49）

**复核对象**：`docs/PL_BALANCE_MEASUREMENT_2026-09-11.md`（PL 所有，未跟踪，我只读）——PL 用自建 harness（`build-output/pl-csim/`，已 gitignore）在 **14 组配置 × 720 局 = 10 080 局**上给出：A 配置 **深海 84.4% / 烈焰 71.1% / 机械 34.4% / 古木 10.0%**。

**为什么必须复现**：§13.19 已证 `HEAD` 与工作树**是两个不同的引擎**。PL 的 C# 数字若同样绑定"当刻磁盘状态"，就会和我的 Java 读数一样**无法锚定到某个修订版** ⇒ "按哪一版评估合并"将失去唯一可复现的依据。这是本轮**唯一能决定合并方向**的外部输入，故必须独立重跑而不是采信文字。

**第一步：先确定"测的是哪一版"（时间戳取证）**

- 实际被加载的引擎 dll `build-output/pl-verify/bin/Release/net8.0/DominionWars.Engine.dll` mtime **09-10 02:11:06**，**晚于**所有非测试 C# 引擎源的最新 mtime（02:10:40）⇒ 测量用的 dll 已含当前全部 C# 引擎源码；
- `8bc0515`（09-10 23:41）在 `src/Engine/` 下**只改 `Tests/**`**，而 csproj 有 `Compile Remove="Tests\**"` ⇒ 对 dll 无影响；
- `data/` 最新 mtime **09-10 23:37** < 测量时刻 **09-11 00:10** ⇒ 测量确实读到了当前卡牌数据。

⇒ PL 的读数是**有修订版锚点的**；§13.19 的"无锚点"问题**只属于我先前的 Java 读数**，不适用于 C# 侧。

**第二步：仓外独立重建 + 逐字段比对（可复现）**

1. 从 **HEAD 源码**重建：`dotnet build src\Engine\DominionWars.Engine.csproj -c Release -p:MSBuildEnableWorkloadResolver=false`（+ `src\Data\…Data.csproj`）；
2. `Copy-Item -Recurse build-output\pl-csim $env:TEMP\qa-repro`，用步骤 1 的新 dll **覆盖** `$env:TEMP\qa-repro\bin\Release\net8.0\` 内副本（**不动 PL 的 bin**）；
3. 从仓库根逐组运行：`…\PlCsim.exe --games 60 --turn-cap 200 [--decline-punish] [--variant X] [--no-buff-faction 古木圣地] --report <out.json>`（每组 ≈5 s，14 组 ≈80 s）。

**结果：14/14 配置、10 080 局、逐字段 0 差异**（config A 为 39 字段、其余各 50 字段，含 `factions` / `winReasons` / `behavior` / `punishDrawInitial` / `punishDrawResponse` / `chainDepthHistogram` 等全部输出）。

> ⚠️ **方法论**：**dll 哈希不同不能作为判据**（编译路径/MVID 会变，哈希必然不同），必须**实际重跑**才能证明可复现。

**第三步：种子独立性（对照 F20）**：`Simulator.cs:119` `seed = 1 + gameIndex*7919 + a*104729 + b*1299709` ⇒ **720/720 个种子全唯一**；每阵营 180 局作 P0 + 180 局作 P1（先后手完全平衡）⇒ **PL harness 没有 F20 那类跨对阵种子共享偏置**，其读数**不可与 `SimMain < 200` 混为一谈**。

**第四步：关键计数逐项对上**（`abl-A-shipped.json`）：`punishDrawInitial 12 126`（报告 12 126 ✓）、`punishDrawResponse 87 390`（✓）、`punishChains 7 541`（✓）、`punishTriggered 48 060`（✓）、响应占比 **87.82%**（报告"88%"✓）、`maxChainDepth 20`、`capped 0 / exceptions 0 / driverGuardHits 0`。§2 的 A/B 两列胜率（4×2 项）与 §5 的四项古木读数（10.00 / 11.11 / 43.06 / 19.17）**也逐值对上**；§5 的"古木攻击力/回合 0.595 / 0.619"与产物 `boardAtk/turn` **0.60 / 0.62** 一致（1 位小数内）。

**F23：五处可验证的取值/表述问题（⚠️ 报告结论方向均不受影响）**

| # | 位置 | 报告写法 | 产物实测 | 判定 |
|---|---|---|---|---|
| ① | §3 链深直方图 | 当作"深度 d 的链数/事件数"引用 | 该直方图是**两件仪器逐档相加**：引擎 `PUNISH_TRIGGERED` 事件（`Simulator.cs:1015-1021`）＋策略 `RecordingPunishPolicy.DepthHistogram`（按 `Decide` 调用计数，`RecordingPunishPolicy.cs:33`），由 `Simulator.cs:375-376` 合并、`:165-166` / `Program.cs:158` 输出。合并合计 **108 730** = 事件 **48 060** ＋ 决策 **60 670**，是链数 **7 541** 的 **14.4 倍**；仅 depth-1 桶（**9 855**）就已 > 链数 ⇒ **没有任何一档可读作链数**（harness 自己打印的标签 `chain-depth histogram (depth:decides)` 与数值也不符，因为它含引擎事件） | **P2（度量表述）**："链被 20 档上限截断"**仍成立，但依据不应是这张图**——直接证据是 `data/balance.json:8 chainLimit = 20`（= `PlayCardActionHandler.DefaultChainLimit`，`:306` 以 `chainDepth > _chainLimit` 截断），而 **8 组 A 配置的实测 `maxChainDepth` 全部恰好 = 20**（每组 7 410–14 806 条链）。建议：**两个直方图分开输出**。另 `Simulator.cs:490-496` 的 `DepthCount` 是死代码 |
| ② | §5 表格 | 策略层面完全不打出 BUFF 卡 ⇒ 古木 **10.61%** | `abl-A-woodNoBuffPlay.json` = **10.56%**（且 §5 正文自己写的也是 **10.56%**）⇒ **同文档自相矛盾，产物支持 10.56** | P3（报表）：10.61 → 10.56 |
| ③ | §4 "平均回合"列 | 5.79 / 14.14 / 6.92 / 8.41 / 7.49 | 与产物 **`factions[烈焰帝国].avgTurns` 逐值完全相等（5/5，diff = 0）** ⇒ 该列实为"**烈焰所参与对局**的平均回合"，**不是全部对局的平均**（原样配置四阵营 avgTurns 5.79 / 5.79 / 6.04 / 6.43 ⇒ 全局均值 ≈ **6.01**）；且报告给 2 位小数而产物只到 1 位 | P3（报表）：列名须加注，否则会被当成全局节奏 |
| ④ | §4 "清掉深海"行 | 90.60 / 48.60 / **23.11** / 37.80 | `abl-A-sea-non-activatable.json` = 90.56 / 48.61 / **23.06** / 37.78；同组 `abl-A-seaNonActivatable.txt` = 90.6 / 48.6 / 23.1 / 37.8（**1 位小数下全部一致**） | P3（报表）：−61.4 pts 结论不变（84.44 − 23.06 = **61.38**）；另**同一产物的 json 与 txt 文件名不一致**（`-non-activatable` vs `seaNonActivatable`），建议统一 |
| ⑤ | §3 的"87.8% 洪流"/A→B 差 | 隐含 A 与 B 分母相同 | `punishDrawInitial`：**A = 12 126** vs **B = 45 248**；roots：**A = 7 541** vs **B = 24 237** ⇒ **两模式分母相差 3.2 倍**。另 B 配置 `maxChainDepth = 0` 是**未填充字段**（该模式无 `PUNISH_TRIGGERED`，因事件只在"响应被接受"时发出），不是"测得深度 0" | P3（表述）：88% 在 **A 内部**成立；但 **A→B 的差（深海 84.4% → 67.5%）不是单变量消融**。建议按模式分别给分母（每局惩罚抽牌数：A **138.2** / B **62.8**），B 模式 `maxChainDepth` 输出 `null` |

**⑥（向 PL 转达，非缺陷，但请顺手修）**：**5 组 A 配置各有 1 次非法动作被引擎拒绝**（`action.discard_selection_invalid`；示例见 `abl-A-flame-non-activatable.txt:58`：`wood_veil` 的 `SET_AMBUSH`，`required=3 candidates=1`，`turn=2 phase=AMBUSH actor=0`），B 配置 0 次。**引擎正确地拒绝了它**（合法性成立、"广告 ⇒ 可解"契约未破），统计影响可忽略（万级动作中 1 次），但说明 harness 的木系策略在 A 配置下会**试图掷出一个不可满足的伏击设置**，建议修。

**⑦（QA 追加证据，正面强化 PL 的 S1 结论）**：PL 的 14 组里 B 模式只有 **flame / sea** 两组消融（缺 machine / wood）。其中 **`abl-B-sea-non-activatable`：深海 67.5% → 31.11%（−36.4 pts）**，同时烈焰 58.33 → **70.83**、机械 31.11 → **40.56**、古木 43.06 → **57.5** ⇒ **"可降临密度是决定性货币"在关闭响应回环（B 模式）后依然成立**，这比报告 §4 只有 config A 的消融**更强**：S1（密度上限）在两种模式下都被支持，而且**深海去掉可降临后会掉到 40–60 带以下**（31.11%）。

**必须同时声明的边界（否则会过度外推）**

1. 我是**同代码、同种子重跑**——证明的是**确定性与"无 HEAD 源码漂移"**，**不**证明该 harness 的通用策略（`heuristic-v1`）能代表游戏 AI，也**不**证明 84.4% 不是策略假象；
2. 与 Java `SimMain` 的跨引擎对比同时混入了"**引擎语义差异**"与"**策略差异**"（`heuristic-v1` vs `AiAgent`）⇒ PL 报告 §1「先前的 Java 读数方向是错的」**应限定为"对 `HEAD` 的 Java 读数成立"**——工作树 Java 已含机械字段 / 古木门控修复（F22），**WIP 落地后必须重测**；
3. 唯一跨引擎、跨工具、跨修订版都成立的结论仍是 **F10 深海偏强**（Java 80.3% / C# 74.0%，PL harness 84.4%）。

### 13.21 复核 PL 的代码审核清单（`docs/PL_CODE_AUDIT_2026-09-09.md`）——逐条裁决

> 追加 2026-09-11 01:0x。**只读复核**：未改动任何生产/测试代码，未动 Git 指针，未启动任何 relay。
> 目的：清单写于 09-09，此后 C# 侧有多次修订（`8bc0515` 之后 `HEAD` 未再出现 `src/` 变更 ⇒ 本节全部行号对 `b4657e8` = **当前修订版**有效）。本节的产出是给 Codex 排期的**"仍成立 / 已修复 / 已过期 / 需改表述"裁决表**，避免照单全做时白干。**落盘提交 `13f6164`（仅 docs，未 push）。**

**复核对象**：`docs/PL_CODE_AUDIT_2026-09-09.md`（PL 所有，**未跟踪**，**110 行 / 9 516 B**，mtime 2026-09-10 23:43 —— 比首版 84 行长，PL 仍在编辑）。结构：一、P0×4（+1 误报撤销）；二、P1×3；三、P2×11；四、设计待裁决 D-1..D-4；五、测试盲点 12 条；六、建议修复顺序 + 页脚"另注"。

**基线读数（当前修订版）**

```
dotnet test src\Engine\Tests\DominionWars.Engine.Tests.csproj -c Release -p:MSBuildEnableWorkloadResolver=false
已通过! - 失败: 0，通过: 603，已跳过: 0，总计: 603，持续时间: 3 s
```

⇒ **603/603 全绿**。所以下表所有「**仍成立**」**都不是测试失败，而是"测试套件看不到"**：这些缺口在 603 绿灯下照样存在。

#### 13.21.1 P0 与 P1

| 清单条目 | 清单结论 | 裁决 | 取证（当前修订版） |
|---|---|---|---|
| **P0-1** 先驱惩罚修正缺失 | 仍成立 | ✅ **仍成立，且应升级为 F24 的一个实例 + 跨引擎玩法分歧** | C# `src/Engine/Turns/CardPlayRules.cs:69-75` 的 `EffectivePunish` **只有** `Math.Max(0, base + player.PunishDeltaThisTurn)`，**无对手视野**；Java `Game.java:426-431` 有 `soloLeader(...)` 双向修正 ⇒ **这是玩法层差异，不是展示层**。C# **已实现** pioneer 的手牌上限那一半（`DiscardPhaseHandler.cs:111-114` `pioneerHandLimitBonus`）⇒ 现状是**半实现伪装成已实现**，比"完全没做"更难查。且 pioneer 的两个惩罚参数在 `data/balance.json` 里（Java 读）在 C# 侧**无任何消费者**（见 F24） |
| **P0-2** `AMBUSH_TRIGGER_WIN` 无实现路径 | 仍成立 | ✅ **仍成立，且属契约级违规** | **数据/契约允许、引擎永不执行**：`data/schema/cards.schema.json` 的 `WinCondition.enum` **显式包含** `AMBUSH_TRIGGER_WIN`，而 `src/Engine/Effects/EffectRuntime.EndPhase.cs:156-183` 的 `switch` 5 个 case 中**没有它**，`default: continue`（丢弃）。当前唯一载体 `gate_of_fate`（`data/cards/neutral.json:34-44`）实际靠 `ambushEffects: WIN_GAME` 取胜 ⇒ **声明的胜利条件是死的，"广告 ⇒ 可解"契约在数据侧被破** |
| **P0-3** `DeferDeaths` 半成品 | 仍成立 | ⚠️ **形态已改，判定为"已修复但缺专项覆盖"** | `src/Engine/Effects/EffectDispatcher.cs:133-157`：`ApplyAll` 置 `DeferDeaths = true` → 批内 `ApplyInternal(checkAll:false)` → `finally` 复位 → **批后一次** `CheckAll`；`CheckAll(` 的非测试调用点仅 `EffectDispatcher.cs:129/155` + `EffectRuntime.Attack.cs:39/51`；`EffectTargetResolver.cs:64/100/118` 允许在批内选中"已死未清理"目标。**且已有回归测试**：`src/Engine/Tests/EffectChainTests.cs:38-61`（`IntermediateDeathCanBeReversedBeforeBatchCheck` / `IntermediateDeathMovesCardAfterBatchCompletes`）⇒ 清单描述的"半成品"形态已不成立。**残留**：**AOE 的两段效果**在"清理被延后"下**结构上必然**第二段仍能命中批内已被标记死亡的单位（我方**结构推断，未跑探针**）⇒ 建议 Codex 补一条"两段 AOE 作用于同一 target 集合"的显式用例 |
| **P0-4** 破城后永久死局 | 仍成立 | ❌ **不成立（已过期）——反证见下** | `EffectRuntime.State.cs:177-226 ApplyCastleDamage`：破城 → `CycleWinCount = max(…, 9)`（`:190-192`）→ `ForceLeaderOut(defender)`（`:194`）→ 双方首领皆随从 ⇒ `DeclareWinner(breaker, "win.castle_break_minion")`（`:200-207`）。**"幻影阈值"说法错误**：阈值确实被读——`src/Engine/Effects/EffectRuntime.Cards.cs:514` `if (counted && player.CycleWinCount >= State.ReshuffleLossThreshold) DeclareWinner(player.PlayerIndex, "win.deck_cycles")` ⇒ **破城方只差 1 次循环即胜**，与 `docs/RULES.md:128` 的"威慑"定稿一致；已有测试 `EffectRuntimeTests.cs:551-675`（含 `win.castle_break_minion` 与"破城方优先"）。**窄残留（= F18 同一点）**：`breakerHas == defenderHas` 时 `:214-217` `return`（无人获胜） |
| **P1-2** 空发/被反制仍消耗攻击 | 仍成立 | ✅ **仍成立** | `src/Engine/Effects/EffectRuntime.Attack.cs:36` 的 `Commit(_ => attacker.AttacksUsed++)` 位于 `:37-41` 的 `if (context.Negated) { CheckAll; return; }` **之前** ⇒ 被反制的攻击照样扣攻击次数 |
| **P1-3** `EffectSpec.Condition` 是死字段 | 仍成立 | ✅ **仍成立** | `EffectSpec.cs:12/19/28` 声明了属性；`src/Engine` 非测试代码**零消费点** ⇒ 带 `Condition` 的效果被**无条件结算**（比"忽略"更危险：静默放宽约束）|
| **P1-1** 伏击 `cardId` 增量泄露 | 仍成立 | ⚠️ **在 canonical 路径上已由 viewer 过滤取代；残留只在 legacy 适配器** | `src/Adapters/RuntimeContractV131Snapshot.cs:49-50`：`Hand = isViewer ? … : Array.Empty`、`Ambush = isViewer ? … : empty`；`:60-66` 非当前行动方**不产出 legalActions** ⇒ 生产路径不泄露。泄露者为 legacy `EngineProjectionAdapter`（见 P2-5）|

#### 13.21.2 P2 表

| 清单条目 | 裁决 | 取证 / 建议 |
|---|---|---|
| **P2-1** `ApplyPunishDelta` 无下限 | ⚠️ **成立但降级为展示层（P3）** | `EffectRuntime.State.cs:144-150` 确实只做 `+=`；但**唯一玩法消费点**（`CardPlayRules.cs:74` 与 Java `Game.java:431`）**两端都有 `Math.max(0,…)` 夹零** ⇒ 玩法无负惩罚；只有快照字段 `PlayerState.cs:50` 可能显示负值，且 `TurnFlow.cs:243` 每回合清零。建议：显示层夹零或契约显式允许负值 |
| **P2-2** 15 关键词中 11 个无实现 | ✅ **仍成立（与 P0-2 同族：schema 过宽）** | `EffectRuntime.Combat.cs:10-14` 是关键词白名单；那 11 个词在非测试引擎代码中**各仅 1 处命中（即白名单自身）** ⇒ **数据可以写、引擎不收** |
| **P2-3** Enfeeble 可致负 HP | ✅ **仍成立，且是 canonical 契约违规 ⇒ 建议升 P1** | `EffectRuntime.Advanced.cs:38/43`：Attack 与 MaxHealth 有 `Math.Max(0,…)`，**HP 无下限**（`target.Health += spec.Amount`）；`design/runtime-kit-v1.31/contracts/schemas/game_snapshot.schema.json` 的 `card.currentHealth` 是 `{"minimum": 0}` 且 `additionalProperties:false` ⇒ **负 HP 快照无法通过 canonical 校验**，即会产生**非法 wire 数据** |
| **P2-4** 纯玩家无法 ROLLBACK | ⚠️ **成立，但需改表述** | `ROLLBACK` 在 C# 侧**只是卡牌效果动作**（`EffectNames.cs:41` 白名单 + `RollbackEffect.cs`）；`LegalActionGenerator` 只产 `Commit`/`Pull` ⇒ 「无回合级回滚动作」成立，但「RollbackEffect 已注册」指的是**效果动作**，两件事不要混为一条 |
| **P2-5** 隐藏信息泄露 | ❌ **生产路径已被 v1.31 架构取代；残留是 legacy 适配器 + 名不副实的测试** | 生产路径 `RuntimeMatchGateway.cs:134` → `RuntimeContractV131Snapshot.ToSnapshot`（按 viewer 过滤，见 P1-1）。泄露者为 `src/Adapters/EngineProjectionAdapter.cs:62-103`（文件注释自称 legacy 1.30）——它填 `Deck = ToCards(player.Deck)` **有序牌库** + 双方手牌，**调用者全是测试**。更值得修的是：`src/Engine/Tests/RuntimeSnapshotProjectionTests.cs:453 ProjectionIsStableAndDoesNotExposeDeckOrder` **只断言 `DeckCount` 与 Hand/Field/Graveyard 为空，从未断言 `Deck` 列表本身** ⇒ 测试名与断言不符（P2），且**快照 wire format 至今没有契约测试**（见 F25）|
| **P2-6** `LeaderWinParam > 0` 前置跳过 | ✅ **成立（= P0-2 的代码侧根因）** | `EffectRuntime.EndPhase.cs:149` 对所有参数化胜利条件做门控 ⇒ 非参数化的 `AMBUSH_TRIGGER_WIN` / `ROYAL_CASTLE_BREAK` 永不在此求值。**但两者性质不同**：`ROYAL_CASTLE_BREAK` 的缺席是**有意的**（在 `ApplyCastleDamage` 内实现），`AMBUSH_TRIGGER_WIN` 的缺席是**无意的**（无任何实现）⇒ 建议加注释写明前者，并修后者（或在 schema 删该枚举）|
| **P2-7** `DamagedThisCycle` 语义未入规则书 | ⚠️ **部分成立（降 P3 文本）** | 字段名在 `docs/RULES.md` **零命中**，但**规则语义有**：`RULES.md:102`「`NO_DAMAGE_TURNS_GE n`：己方连续 n 个回合未受任何伤害」，且 `:228` 提到"无伤回合"（疯长 +1）。真正缺的是**判定口径**（0 伤害 / 被治疗 / AOE 未命中是否算"受伤害"）⇒ 属文本补注 |
| **P2-8** `DEFEAT_PREVENTED` 不在事件白名单 | ✅ **成立（见 F27 的普遍化）** | 引擎确实发出它（`src/Engine/Effects/EffectRuntime.cs:80 Emit("DEFEAT_PREVENTED", …)`，`EffectSafetyTests.cs:169` 有断言），但它**不在** canonical `ui_event.schema.json` 的 `type.enum`，也**不在** legacy `EventTypeMap` ⇒ 在**批量**投影中被**静默丢弃**（`EngineProjectionAdapter.cs:304-307 continue`），在**单条**投影中直接抛（`:254-258 NotSupportedException`）|
| **P2-9** 先驱判定用单一 `Leader` | ✅ **仍成立，且是 P0-1 的现成修复模板** | `DiscardPhaseHandler.cs:111` `pioneer = player.Leader is not null && opponent.Leader is null` ⇒ C# **已经有**内联 solo-leader 判据（用于手牌上限）。建议把该判据抽成 `MatchRules`/`GameState` 级 helper，供 `EffectivePunish` 复用，**避免两处对"先驱"的定义漂移** |
| **P2-10** `TurnFlow.CompleteTurn` 直写 `Events.Append` | ✅ **仍成立** | `src/Engine/Turns/TurnFlow.cs:258/259`（`TURN_CHANGED` / `PHASE_CHANGED` 回合交接）与 `:307`（相位推进）**绕过命令缓冲**直写事件流；对照 `EffectRuntime.cs:208` 走 `State.Events.Append` 也属同类 ⇒ 事件的"命令可回滚性"在回合/相位边界上不成立 |
| **P2-11** 四个条件分支不可达 | ⚠️ **方向成立、举例过期，需改写后再给 Codex** | `data/schema/cards.schema.json` 的 `PunishCondition.enum` **只允许** `ALWAYS` / `ENEMY_MINIONS_GE_1` / `ENEMY_MINIONS_GE_2` / `HAND_GE_3`；`PunishConditionEvaluator` 另实现的 `SELF_LEADER_ON_FIELD` / `OPP_LEADER_ON_FIELD` / `SELF_MINIONS_GE_*` / `SELF_LIFE_LE_*` **在受校验数据中不可表达** ⇒ **确为死分支**。但清单举的"已无使用者"是**胜利条件**（`OPP_PUNISH_DRAW_TURN_GE` 等），它们在 `EndPhase.cs:164` 等**有实现**、属**过期描述**；而 `ENEMY_MINIONS_GE_1/2`、`HAND_GE_3` **确实被数据使用**（`data/cards/` 中 flame:124/275/330、machine:530、sea:276/353、wood:276）|

#### 13.21.3 设计待裁决 D 项

| 条目 | 裁决 | 取证 |
|---|---|---|
| **D-1** 循环胜利方向歧义 | ❌ **误读，建议从清单删除** | `docs/RULES.md:125-126`（自己计数 +1；**让对方循环 10 次即对方获胜**）与 `EffectRuntime.Cards.cs:508/516`（**循环者自己**计数 +1 且**循环者自己**获胜）**方向完全一致** |
| **D-2** 破城后 `CycleWinCount = 9` | ✅ **已由 `RULES.md:128` 定稿为"威慑"（只差 1 次循环即胜）** | 这正是 P0-4 不成立的原因；不需要再裁决 |
| **D-3 / D-4** | ⚪ **本轮未取证**（不构成"已修复"声明） | 见 13.21.6 边界声明 |

#### 13.21.4 §六 页脚"另注"：惩罚响应恒被放弃 ⇒ ✅ **成立，且比清单更严重：这是跨引擎能力差距**

| 引擎 | 实证 |
|---|---|
| **C#（Unity/发布路径）** | `punishResponses` 在 `PlayCardActionHandler` / `CommitActionHandler` / `PullActionHandler` / `TurnActionRouter` 中**全是可选参数**，回退 `DeclinePunishResponsePolicy`；`src/Adapters/MatchFactory.cs:63` 用 `TurnActionRouter.CreateDefault(flow)` **不注入策略** ⇒ **发布运行时永远自动放弃**。更硬的证据：**`src/Engine/Turns/LegalActionGenerator.cs` 中 `punish` 零命中**，`ACTIVATE_PUNISH` 全仓只出现在 `src/Engine/Tests/ContractBoundaryTests.cs` 与 `LegalActionGeneratorTests.cs` ⇒ **Unity 侧玩家没有任何"发动惩罚"的合法动作** |
| **Java（Web/桌面）** | **完整的人工路径都在**：`src/main/java/com/dominionwars/server/WebHumanAgent.java:67-71 askActivatePunish`（Web 动作 `activatePunish`，`:20/:53` 还专门把它标为非可放弃）、`ConsoleHumanAgent.java:25`、`SwingHumanAgent.java:20`、`AiAgent.java:19`，由 `Game.java:381` 调用 |

**两条后果（比原清单重要）**：

1. **Unity 侧玩家被砍掉了一整条决策轴**（惩罚响应），而这条轴正是 PL 的 T1/惩罚链预算与我的 F15（`punishActivatable` 是最强单一解释变量）所依赖的货币；
2. **PL 的 A 模式（`--decline-punish`）恰恰等于 C# 发布默认行为，B 模式才接近 Java/Web 现实** ⇒ 这给 F23-⑤ 的"A→B 分母差 3.2 倍"提供了**规则级解释**（不是采样巧合），也再次说明**两引擎的平衡读数不可直接比较**。

#### 13.21.5 本轮新增发现（F24–F27）

| # | 发现 | 严重度 | 归属 / 建议动作 |
|---|---|---|---|
| **F24** | **C# 引擎完全不读 `data/balance.json`（跨引擎参数单一来源缺失）**：`src/` 内 `balance.json` **零命中**（只有 Java `Balance.load`）。C# 把 `chainLimit = 20`（`PlayCardActionHandler` 的 `DefaultChainLimit`）、`castleHealth = 75`（`MatchSetup` / `GameState`）、`reshuffleLossThreshold = 10` / `castleBreakVictoryCount = 9`（`GameState`）、`handLimit = 8` / `pioneerHandLimitBonus = 2`（`MatchRules`）**全部硬编码**；Java 侧 `Balance.java` 还有"读盘失败回退并写 stderr"的保护。⇒ **改 `data/balance.json` 对 Unity 侧完全无效**，而 PL/owner 的平衡讨论正是围绕该文件进行的。**P0-1 只是该缺失的一个实例** | **P0（主线正确性）** | **Codex**：让 C# 从 `data/balance.json` 装载（或明确声明 C# 侧唯一来源到底是谁），并补一致性测试；**PL/owner**：需知晓"这份文件只管 Java 侧"这一现状。**行为级实证（四臂对照 + 阳性对照）见 §13.22**：改该文件 5 个键 ⇒ C# 输出逐字节不变；只改一张卡 ⇒ 输出立刻变 |
| **F25** | **快照 wire format 没有契约测试**：canonical `game_snapshot.schema.json`（`additionalProperties:false`、required 含 `snapshotRevision`/`viewerPlayerId`/`pendingPrompt`、player 要求 `fieldCount`/`graveyardCount`/`ambushCount`、**无 `deck` 字段**、`card.currentHealth minimum 0`）**从未被任何 C# 测试引用**（`ContractBoundaryTests.cs:151` 只引 `game_action.schema.json`），且 legacy `SnapshotDto`/`PlayerDto`（`src/Adapters/ContractDtos.cs:7-49`，PascalCase `Id`/`FieldEntityIds`/`Deck`/一堆 `*ThisTurn`）**结构上不可能通过 v1.31 校验**却仍留在仓库被测试固化 ⇒ 这是 **P2-3 负 HP** 与 **P2-5 误导性测试名**能长期存活的共同根因。建议：加 `game_snapshot.schema.json` 校验测试 + 给 legacy DTO/适配器打 `[Obsolete]` 或删除 | **P1（契约）** | **Codex** |
| **F26** | **v1.31 的"事件边界"有契约、有校验器，却没有生产者**：引擎实际发出 **46** 种事件类型、**51** 种 `Data()` 载荷键；canonical `ui_event.schema.json` 的 `type.enum` 只有 **29** 个，`RuntimeEventCursor`（`src/Adapters/RuntimeEventCursor.cs:29-38`）**逐个对齐这 29 个**，并把载荷键限制为 **7 个**（`:40-43` `amount/cardId/count/reasonKey/sourceId/targetIds/winnerPlayerIndex`，`:92` 未知键直接 `event.data_field_unknown`）。**但 `RuntimeEventEnvelope` / `RuntimeEventCursor` 在 `src/` 中只被 `src/Engine/Tests/RuntimeEventCursorTests.cs` 构造 ⇒ 没有生产适配器**；`RuntimeMatchGateway` 对外返回的是**原始引擎 `GameEvent`**（`:110-111/:177`）。仓库里唯一的类型改名表是 legacy 1.30 的 `EventTypeMap`（`EngineProjectionAdapter.cs:14-43`，26 键，只覆盖 22 种引擎类型；批量为**静默丢弃**、单条为**抛异常**）⇒ **28 个引擎事件类型在 canonical 契约中不可表达，11 个契约类型当前无生产者**。任何想按契约把事件扇出到 Web 的宿主都必须自己再写一遍映射 | **P1（集成缺口）** | **Codex**：补 `GameEvent → RuntimeEventEnvelope` 的生产映射（含改名表 + 载荷裁剪 + `snapshotRevision` 打戳），并覆盖全部 46 种类型（或与 PL 一起把 enum 收窄）；**PL/owner**：确认 `ui_event` 的 29 项 enum 是否即为最终意图 |
| **F27** | **（正面记录）`RuntimeEventCursor` 的质量高于清单假设**：它已实现乱序/缺号/重复/父缺失/相位/版本/终局载荷（`GAME_OVER` 必须恰好两个字段且 `reasonKey` 齐备，`:95-115`）等拒绝原因 ⇒ 清单把 P2-8 描述为"白名单缺失"**低估了已有实现**，真正的缺口是 F26 的"没有生产者" | 澄清 | 记录在案，供 Codex 复用而非重写 |

#### 13.21.6 建议排期与"不要照单全做"的部分

**建议 Codex 立即做（按序）**：**F26 的事件生产映射** → **F24 参数的单一来源**（含 P0-1 的惩罚修正，可直接复用 `DiscardPhaseHandler.cs:111` 的判据）→ **F25 快照契约测试 + legacy 清理** → **P2-3 HP 夹零（契约违规）** → **P0-2 的 `AMBUSH_TRIGGER_WIN`**（实现或删 enum）→ **P1-2 攻击消耗 / P1-3 `Condition` 死字段** → 三个补覆盖用例（P0-3 的两段 AOE、P0-4 的"双无首领"、F18 的双持有）。

**建议从清单删除或改写（避免白干）**：

| 清单条目 | 处置 |
|---|---|
| **P0-4 主体** | **删除主结论**（阈值确被读、破城方只差 1 次循环，不是死局）；只保留 `:214-217` 的窄分支（= F18，已在 §14 跟踪）|
| **P2-11 的条件名** | 保留"有死分支"的结论，**换掉举例**（死的是 `SELF_*` / `OPP_LEADER_ON_FIELD` 四个**惩罚条件**，不是胜利条件）|
| **D-1** | **删除**（与 `RULES.md` 方向一致，无歧义）|
| **D-2** | **删除**（已由 `RULES.md:128` 定稿）|
| **P2-5 主体** | 改写为"legacy 1.30 适配器 + 名不副实的测试 + 缺快照契约测试"三件具体事（见 F25）|
| **P2-1** | 降级 P3（显示层）|
| **P2-7** | 改为"补判定口径文本"（P3）|
| **P0-3** | 改写为"已修复，补一条两段 AOE 用例"|
| **§六 另注** | **升格为 P0 排期**（Unity 侧缺失惩罚响应轴 + 它解释了两引擎读数为何不可比）|

#### 13.21.7 边界声明（不得过度外推）

1. 本轮为**只读**复核：所有裁决都基于**源码/数据/契约取证**，唯一执行的命令是 C# 测试套件（603/603）；
2. **P0-3 的两段 AOE** 与 **P0-4 的双持有窄分支**是**结构推断**，**未跑探针实证**；
3. **P1-1 的增量泄露路径、P2-7 的字段判定口径、D-3/D-4、清单第五节"测试盲点 12 条"本轮未逐条复检** ⇒ 下表/上文**不构成"已修复"**，只是"未复检"；
4. F26 的"46 种事件类型 / 51 种载荷键"是按**字面量扫描**（`Emit(`/`Events.Append(`）得到，**由变量拼接产生的事件名不会被计入** ⇒ 该数字是**下界**；同理 legacy `EventTypeMap` 中 `CARDS_DRAWN` / `PUNISH_DRAW` 无字面量生产者，可能为 Java 语义残留。

### 13.22 F24 的行为级实证：`data/balance.json` 对 C# 引擎零影响、而改卡有效（四臂对照实测）

> 追加 2026-09-11 01:2x。§13.21 里 F24 的依据只是"`src/` 内 `balance.json` 零命中"（**静态**）。本节把它升级为**实跑证据**，并**用阳性对照排除"探针根本没读数据"这一替代解释**。
> **边界**：全部数据改动都只发生在**仓库外的 `%TEMP%` 副本**上。仓库内数据/生产文件**一字未改**（`git status --porcelain -- src/Engine data/` 为空），Git 指针未动，未启动任何 relay、未产生任何付费调用。

**被测对象：同一份可执行体，唯一变量 = 引擎实际读到的那份数据目录**

| 引擎 | 可执行体 | 数据是怎么被读到的 |
|---|---|---|
| Java（测试台） | `scripts\build.bat` 产物 `build\classes`（工作树当前源码，59/59 绿） | **CWD 相对路径**：`SimMain.java:20` `Path.of("data/balance.json")`；`data/cards`、`data/decks` 同（`ConsoleMain.java:41`、`WebServer.java:43` 亦同）|
| C#（Unity 权威引擎） | 我的仓外预言机 `DwSim.dll`（`%TEMP%\dw-cs-sim`），引用 `build-output\...\DominionWars.Engine.dll`（`FBEBCABD…`，2026-09-11 00:59:52） | **命令行参数 `repoRoot`** 指向数据目录；`CardCatalog.LoadDirectory` 读 `data/cards/*.json` |

**改的是 5 个全局键**（副本 `balance.json` 相对仓库原文）：`openingHand 5→3`、`handLimit 8→4`、`reshuffleLoseAt 10→3`、`chainLimit 20→5`、`royalCastleMaxHp 75→15`（`README.md:99` 恰好把这份文件宣传为"全部可调数值"）。

**四臂结果**

| 臂 | 引擎 | 数据来源 | 命令 | 关键读数 | 输出 SHA256 |
|---|---|---|---|---|---|
| **A** 基线 | Java | 仓库 `data/` | `SimMain 300`（12 对阵 ×300 = **3 600 局**） | 平均回合 **14.7936**；烈焰 **51.2%**、机械 **13.2%**、深海 **80.3%**、古木 **55.3%** | `234ADB28BCA7DA14…` |
| **B** 极值 balance | Java | `%TEMP%` 副本 | `SimMain 300`（3 600 局） | 平均回合 **9.3111**；烈焰 **79.6%**（+28.4）、机械 **8.4%**（−4.8）、深海 **73.2%**（−7.1）、古木 **38.8%**（−16.5） | `BA7B14262E5F0174…` |
| **A′** 基线 | C# | 仓库 `data/` | `DwSim <repoRoot> 250 decline 60`（6 对阵 ×250 = **1 500 局**） | 平均回合 **10.16**、中位 10、最长 23；烈焰 89.1%、机械 **0.0%**、深海 75.2%、古木 35.7% | `9F969FF74047679C…` |
| **B′** 极值 balance | C# | `%TEMP%` 副本 | 同上（1 500 局） | **与 A′ 逐字节相同**（43 行输出 diff = 0） | **`9F969FF74047679C…`（同 A′）** |
| **C** 阳性对照① | C# | 副本，`balance.json` 复原、**只改卡**（`machine_leader.winParam` 6→2） | 同上（1 500 局） | 机械 **2.3%**（17/750）、`maxPull` **3→2**、`win.pull_total_ge` **0→17**、`win.castle_break_minion` **2→0**、平均回合 10.16→**10.12**，事件计数全变 | `169AB81B349BBAFD…` |
| **D** 阳性对照② | C# | 同 C，**另把 `machine_alpha.winParam` 也改成 2** | 同上（1 500 局） | **与 C 逐字节相同** | **`169AB81B349BBAFD…`（同 C）** |

**读法（三点，缺任一点都不足以定案）**

1. **Java 对这份文件高度敏感**：只换 CWD 指向的数据目录（编译产物、卡牌、牌组全同），平均回合 −5.48 回合、四个阵营胜率整体位移 4.8–28.4 pts ⇒ 该文件在 Java 侧**确实是规则来源**；
2. **C# 对其零敏感**：改同样的 5 个键后输出**逐字节一致**（SHA 相同）⇒ 不是"读了但影响小"，而是**根本没进引擎**。与 `src/` 全目录 `balance.json` 零命中、`data/balance.json` 的**唯一读取者是 Java 与 `scripts/sanity_check_v2.py`（后者只校验键是否齐全）**一致；
3. **阳性对照通过**：同一套 `repoRoot` 机制下**只改一张卡的数值**（C 臂）⇒ 输出立刻改变、SHA 从 `9F96…` 变 `169A…`，且机甲方胜率 0.0%→2.3%、`maxPull` 3→2、`win.pull_total_ge` 0→17、`win.castle_break_minion` 2→0 **同时移动**。这排除了"探针压根没读 `repoRoot`"的替代解释 ⇒ **C# 读卡（`data/cards`）不读全局参数（`data/balance.json`）**。D 臂与 C 臂逐字节相同，说明该差异可**完全归因到 `machine_leader` 一张卡**（`machine_alpha` 的 `winParam` 在"首领对首领"模式里不参与）。

**逐参数归因（源码侧，配合上表才完整）**

| `balance.json` 键 | C# 里的硬编码对应物（今天数值恰好相同 ⇒ 缺陷**不可见**） |
|---|---|
| `openingHand: 5` | `MatchSetup.cs:47` `OpeningHandSize { get; set; } = 5` |
| `handLimit: 8` / `pioneerHandLimitBonus: 2` | `MatchRules.cs:9` `MatchRules(int handLimit = 8, int pioneerHandLimitBonus = 2)` |
| `reshuffleLoseAt: 10` | `GameState.cs:20` `_reshuffleLossThreshold = 10` |
| `chainLimit: 20` | `PlayCardActionHandler.cs:15` `DefaultChainLimit = 20` |
| `royalCastleMaxHp: 75` | `GameState.cs:85` `_castleHealth = 75`；`MatchSetup.cs:50` `CastleHealth { get; set; } = 75` |
| `royalCastleBreakVictoryCount: 9` | `GameState.cs:21` `_castleBreakVictoryCount = 9` |

**为什么这是 P0（而不是"文档问题"）**：本实验等价于一次真实事故的预演 —— **owner/PL 按 `README.md:99` 的承诺调 `data/balance.json`，Java/Web 侧规则随之改变，Unity 侧一动不动**；而因为两套默认值**当前恰好相等**，这种分叉在门禁上**完全静默**（`sanity_check_v2.py` 照样通过、C# 测试 603/603 照样通过）。F24 因此不只是"重复定义"，而是**"唯一被文档承诺的跨引擎调参入口对主线引擎无效"**。
**附带产出（供 PL 参考，非缺陷）**：C/D 臂顺带给出 `machine_leader.winParam` 的语义读数 —— 该值变小则机甲方胜率与 `maxPull` 读数同向移动，与 §14 的 F2（"`winParam=6` 一旦被追求即过强"）方向一致；本条为**观测**，不是结论。

**本节不可外推的部分**：① 5 个键是**成组**修改 ⇒ 行为实验证明的是"对该文件**整体**零敏感"，**逐键**归因靠上表的源码行号（二者合起来才构成完整证明）；② C# 侧跑的是我的**仓外预言机**而非 Unity 构建产物，但它引用的就是 `build-output` 的 `DominionWars.Engine.dll`，且 `src/Engine` 相对 `HEAD` 无改动 ⇒ 代表主线引擎代码；③ **诚实说明**：我的预言机自身也没有 balance 装载代码（它只把 `data/cards`、`data/decks` 交给引擎）⇒ 行为实验证明的是"**这条 host+engine 路径对该文件零敏感**"；**产品级**的"没有任何宿主读它"这一条，依据是**全仓库排除 `docs/`/`design/` 后的零命中取证**（唯一读取者 = Java 侧 + `scripts/sanity_check_v2.py` 的键齐全校验，含 `unity/Assets` 全部脚本）——两条证据合起来才等于 F24 的完整结论。**复现命令与原始输出**见 `docs/QA_HANDOFF_2026-09-11.md` §5。

### 13.23 P0-3 残留形态探针实证（机制成立、**当前不可达**）+ 由同一探针升级出的新缺陷 **F28（P1）**：canonical v1.31 生产者在溢出击杀后发布**负 `currentHealth`**（2026-09-11 01:2x）

**本节边界**：全部实验在**仓库外** `%TEMP%\qa-p03p04\` 进行，只调用 `src/` 的**公开 API**（`GameState`、`EffectDispatcher`、`RuntimeSnapshotProjection`、`RuntimeWireSerializer` 均为 public；`src/` 内无 `InternalsVisibleTo`）⇒ **未修改任何生产/测试文件、未动 Git 指针、未启动 relay**。探针从**合成摆盘**出发（手工放置 3 个敌方随从 + 1 个我方随从 + 两套预组牌库），不是真实对局回放 ⇒ 结论限于"**机制与投影行为**"；"当前卡池能否触发"另由**全卡扫描**单独证明（13.23.2）。

#### 13.23.1 复现（三条命令，全部在仓库外执行）

| 序 | 命令 | 预期 |
|---|---|---|
| 1 | `dotnet build %TEMP%\qa-p03p04\qa-probe.csproj -c Release -p:MSBuildEnableWorkloadResolver=false` | **0 警告 0 错误** |
| 2 | `dotnet %TEMP%\qa-p03p04\bin\Release\net8.0\QaProbe.dll <仓库根路径>` | 打印 S1–S7；落盘 `canonical-snapshot-1.json`（含负血）与 `canonical-snapshot-2.json`（对照） |
| 3 | `python %TEMP%\qa-p03p04\validate_snapshot.py design\runtime-kit-v1.31\contracts\schemas\game_snapshot.schema.json <上述两个 JSON>` | 前者 **2 errors**、后者 **0 errors**，末行 `RESULT: FAIL` |

工件 SHA256（前 16 位，便于比对）：`Program.cs` `E017A15E87C1A2DB`、`qa-probe.csproj` `FFBFFC7A55B97C93`、`canonical-snapshot-1.json` `BC0F2BB202B1A9EB`、`canonical-snapshot-2.json` `C9015DAA38E364CF`。
**第 3 步的脚本必须先 `schema.pop("$id", None)`**：该 schema 的 `$id` 是 `urn:dw:runtime:contract:1.31:game-snapshot`，`jsonschema` 4.17.3 会以 `RefResolutionError: unknown url type: ''` 拒绝加载；删 `$id` 是安全的，因为 schema 内 `$ref` 全是普通 `#/$defs/...`。

#### 13.23.2 卡数据可达性：**当前不可达**（91 张卡全扫描）

- **无任何卡**同时含"同一批内的两段伤害 AOE"（= 清单 P0-3 所假设的形态）；
- **无任何卡**含"同批伤害 + 治疗"（= S3 那种"打死后在同一批被治疗救回"）；
- `ROYAL_CASTLE_BREAK` 全仓库只有 `data/cards/flame.json:18` 一个持有者 ⇒ `EffectRuntime.State.cs:214-217` 的"双方均持有"分支**不可达**（`:200-207` 的"双方随从"分支可达）。

⇒ **对清单 P0-3 的裁决**：**主形态（单段 AOE 溢出）确已修复**且已有回归测试（`EffectChainTests.cs:38-61`），**残留形态（同批多段）机制成立但当前不可达**。建议 **Codex 补一条两段 AOE 用例**把语义钉住（究竟"允许第二次命中尸体"还是"应跳过"），**不必改引擎**。

#### 13.23.3 批内行为实测（S1–S5）

摆盘（每个场景前重建）：敌方 `flame_guard#123(5/6)`、`flame_recruit#124(3/3)`、`flame_charger#125(1/2)`；我方 `wood_treant#126(3/8)`。

| 场景 | 施加效果 | 123 / 124 / 125 终值 | 墓场 P1 | `DAMAGE_DEALT` | `MINION_DESTROYED` | 读数 |
|---|---|---|---|---|---|---|
| S1 | 单段 `DAMAGE ALL_ENEMY_MINIONS 5`（非延迟） | 0 / **−2** / **−4** | 3 | 3 | 3 | 负血**不是**批内独有 |
| S2 | 同批 `DAMAGE 5` + `DAMAGE 2`（DeferDeaths） | **−2** / **−4** / **−6** | 3 | **6** | 3 | 尸体被二次命中；**无**双重死亡、**无**重复入墓 |
| S3 | 同批 `DAMAGE 5` + `HEAL ALL_ENEMY_MINIONS 2` | **2** / 0 / −2 | **2** | 3 | 2 | 5 血单位 **0 → 2 复活并留在场**（必死变存活） |
| S4 | 两次**独立** `Apply`（各自 CheckAll） | 0 / −2 / −4 | 3 | 3 | 3 | 第二批结算前尸体已清 ⇒ 唯一事件是 `EFFECT_SKIPPED`（`target.none`） |
| S5 | 同批 `DAMAGE 5` + `HEAL 5`（足量治疗） | 5 / 3 / 1 | 0 | 3 | 0 | 有符号池**完全可逆** ⇒ 批内延迟死亡是**设计**而非 bug |

**读法**：S1/S4 证明"溢出伤害被写成负数"**单段即存在**（不是批内独有）；S2 证明"批内不清理尸体"让同一目标多挨一次（多出 3 条 `DAMAGE_DEALT`）并使负值更深；S3/S5 证明"批内治疗能救回本该死"与"足量治疗完全复原"。⇒ 若改成"伤害时夹零"，S3 的 3 血单位会**从死变活**，因此**不是无害重构**（见 13.23.5 选项 ③）。

#### 13.23.4 ⭐ F28（P1，新）：canonical v1.31 投影会发布**违反自身 schema** 的负 `currentHealth`

**根因链（全在 C# 主线）**

1. **伤害不夹零**：`src/Engine/Effects/EffectRuntime.Combat.cs:333`（`DamageCard`）直接执行 `target.Health -= amount`；对照同一文件 `:112`（`DamageNonMinionLeader`）与 `src/Engine/Effects/EffectRuntime.State.cs:182`（王城）都写了 `Math.Max(0, …)` ⇒ **三条伤害路径两条夹零、一条不夹**。
2. **投影原样搬运**：`src/Adapters/RuntimeContractV131Snapshot.cs:53`（`Graveyard = Cards(player.Graveyard)`）→ `:160`（`CurrentHealth = card.Health`，**无夹零、无省略**）。该投影是 canonical 的**唯一生产者**（入口 `src/Adapters/RuntimeMatchGateway.cs:132-136`），Unity 侧由 `unity/DominionWars.Unity/Assets/DominionWars.Runtime/RuntimeAdapter.cs:60/67` 消费。
3. **契约自相矛盾**：`design/runtime-kit-v1.31/contracts/RUNTIME_CONTRACT_1.31.md:77` 要求 `currentHealth` **必须直接来自权威 `CardInstance.Health`**；`contracts/schemas/game_snapshot.schema.json:57` 要求 `minimum: 0`（`:49` `additionalProperties: false`）；契约 `:74` 规定无法解析/无效的消息**fail-closed**（整条消息作废）。⇒ 只要发生**一次溢出击杀**，二者**不可能同时满足**。

**证据（S6 治疗组 / S7 对照组，JSON 由仓库自己的 `RuntimeWireSerializer` 生成，viewer = 受害方 `player_1`）**

| 臂 | 施加 | 墓场 `currentHealth` | 负值计数 | schema 校验结果 |
|---|---|---|---|---|
| S6 | `DAMAGE ALL_ENEMY_MINIONS 5` | `[123=0, 124=−2, 125=−4]` | **2** | **2 errors**：`players/1/graveyard/1/currentHealth` = −2、`players/1/graveyard/2/currentHealth` = −4（均 `is less than the minimum of 0`） |
| S7 | 无（同一工作台，不施加效果） | `[123=5, 124=3, 125=1]`（未死亡） | 0 | **0 errors** |

**违规被隔离到负血量这一个值**：其余载荷（`contractVersion`、`players`、`field`、`hand`、`castle`、计数类字段）全部合规 —— S6 与 S7 的 `jsonBytes` 只差 2（1874 vs 1872），正好是 `-2` / `-4` 与 `0` / `0` 的字节差。

**影响面**

- 按契约 `:74`，合规客户端**必须拒绝一份合法对局快照** ⇒ 任意一次溢出击杀后画面/状态同步断裂；
- Unity 卡牌检视视图会枚举墓场卡并走 `RuntimeCardDisplayModel.cs:106`（`CurrentHealth = IsMinion ? snapshot.CurrentHealth : null`）⇒ **UI 会显示 −2**（主战面板的墓场只显示数量，`RuntimeBattlePanel.cs:876/921` `SetPileCount`，所以问题只在检视/未来的卡面渲染）；
- **只在 v1.31 成立**：v1.30 的 `game_snapshot` schema 极松（`players.items` 仅 `{type: object}`，**无** health 约束）⇒ 负血量在 v1.30 **不构成违规**（这也是它长期不被发现的原因）。

#### 13.23.5 修复选项（**需 PL 定稿契约文本；我不改生产代码**）

| 选项 | 改动位置 | 影响 | 评价 |
|---|---|---|---|
| ① **投影层夹零** | `RuntimeContractV131Snapshot.cs:160` 改 `Math.Max(0, card.Health)` | 与 Java 的显示约定一致（`src/main/java/com/dominionwars/engine/Game.java:1113` 打印 `Math.max(0, target.health)`）；**零规则影响** | **推荐**；但需在契约 `:77` 加一句豁免（"权威状态不夹零；投影/日志展示值夹零"） |
| ② `Health ≤ 0` 时**省略** `currentHealth` | 同处条件赋值 | schema `:50` 已允许缺省、`:77` 标注 optional ⇒ **零 schema 改动** | 次优；代价是墓场卡面缺一个数值 |
| ③ **引擎层** `DamageCard` 夹零 | `EffectRuntime.Combat.cs:333` | 按 S3：3 血单位受 5 伤先夹到 0，再受同批 `HEAL 2` → **2，从必死变为存活** ⇒ **规则语义变更** | **不是无害重构**，需 owner 批准；且该场景**当前无卡可达** |
| ④ 放宽 schema（删 `minimum: 0`） | 契约变更 | 让"投影 = 权威"这条约束**永久失守** | 最差 |

**与 F25 的硬耦合**：`codex-f25-snapshot-contract-test`（用 `game_snapshot.schema.json` 校验生产快照）**一旦实现，今天就会红** —— 任意溢出击杀即触发 F28 ⇒ **必须先定 F28，再做 F25**。

#### 13.23.6 本节不可外推的部分

① 探针是**合成摆盘**，不覆盖"真实对局驱动的完整快照"（但 F28 只需**一次**溢出击杀，与摆盘方式无关，且 S6 的 JSON 由**仓库自身**的 wire 序列化器产出）；② 我**未**跑 Unity 侧渲染，"UI 显示 −2"的依据是**源码路径**（`RuntimeCardDisplayModel.cs:106` + 检视视图枚举墓场），**未**做屏幕级验证；③ 卡池可达性扫描基于当前 `data/cards/*.json` 快照，**新增一张卡即可改变结论**（这正是要把它写成门禁用例的理由）；④ S1–S7 的原始转录见 `%TEMP%\qa-p03p04\probe-output.txt`（96 行）。

### 13.24 写入者归因复核：今晚的仓库写入不是 Codex（2026-09-11 01:35 前后）

owner 提问：「Codex 本周额度已到，为何看起来还活着/还在写？」以下取证全部在**仓库外只读**进行（进程快照 + `~/.codex` 投影表 + 文件 mtime），**未**修改任何被审文件，也**未**读取 owner 的私有会话内容。

| 证据 | 观测值 | 含义 |
|---|---|---|
| 仓库写入窗口 | 首次 `23:41:39`（`docs/DESIGN_SEA_PUNISH_MATH_2026-09-09.md`）；`src/main/java/**` 落在 `00:05:43`–`00:19:57`；末次 `00:23:06`（`docs/PL_BALANCE_MEASUREMENT_2026-09-11.md`） | 今晚的写入集中在 **23:41–00:23** 这 42 分钟内 |
| Codex 桌面端线程活动 | `~/.codex/state_5.sqlite` → `threads` 表 `updated_at_ms` **最大值 = `1789041121266`**（= **2026-09-10 20:52:01**）；`~/.codex/sessions/**` 最新 rollout 文件 mtime 同为 **20:52:01**（共 362 条线程，无一条在此之后更新） | **23:41–00:23 窗口内 Codex 无任何线程/会话活动** —— 两路独立信号互证 |
| 存活的 Codex 进程 | `ChatGPT.exe`（PID 28088，`WindowsApps\OpenAI.Codex_26.903.8094.0`，启动 **23:29:51**）→ `codex.exe … app-server -c features.code_mode_host=true`（PID 29668，**23:29:58**）→ `cmd /c call ./scripts/launch_codex_app_tools_mcp.cmd ./server.mjs`（PID 9064 / node 31300，**23:30:12**） | 这是桌面端的**常驻宿主**（app-server + MCP 工具后端），进程活着但**没有 running thread** ⇒ 观感上"Codex 还在"，实际未产出任何写入 |
| 实际写入者 | `node`（PID 15728）`pnpm dsh web --patch web-browse-picker.overlay.yml`（父 `cmd` PID 10824，启动 **22:57:06**），scratch `%TEMP%\dsh-CT5ODw`（23:00） | 即 owner 所述的 **harness 侧 DeepSeek 线**；产物形态（`docs/PL_*`、`build-output/pl-csim/*`——后者被 `.gitignore:15` 忽略）与之相符 |

**结论**：今晚写入**归因于 harness（`dsh web`）**，**不是** Codex CLI / relay（当前无 `codex exec` 进程存在）。⇒ F22 的"把这 1 402 行落地为具名提交"仍应发给 **Codex**，但**不要**等待写入方自己提交：写入方是 harness 侧，它不会替 Codex 提交，也不在 Codex 的权限/额度路径上。

**WIP 冻结复核（同一次取证）**：仓外快照的 13 个文件副本与工作树**逐字节一致**，且实时 `git diff --binary -- src` 与快照 `src-wip.patch` 的 **SHA256 完全相同**（`7EA152290EED5177…`，108 564 字节）⇒ 自 `00:23` 起无人再改，该 WIP 已**冻结**，老板本与工作树同时可落、也可安全重建。

**本节不可外推**：① `threads.updated_at_ms` 是桌面端投影表，理论上可能滞后；判据是"5 小时静默 + rollout 文件 mtime 一致"两路互证，而非单一字段；② 我**未**读取 harness 会话内容，因此"写入者是 harness"是**排除法 + 产物形态**推断，**不是**逐条工具调用级证据；③ 进程快照是 01:4x 的单点观测，只能说明"此刻"谁在运行。

### 13.25 owner 破城/下载轴四条规则的落地核查 + §0 数据基线漂移定位（2026-09-11 01:5x）

owner 本轮给出的四条决定：**① 随从型首领自带"王城被破坏"胜利条件；② 不内置写死，各首领各写各的；③ `alpha` 已改为上传/下载轴 ⇒ 不需要破城；④ 镜像局「当然 A」＝主动破城方胜**（"不然大家都不打王城了"）。以下逐条只读核查，**未修改任何被审文件**，用例与哈希全部实测。

#### 13.25.1 逐条落地（`data/` → C# → Java → schema/契约 → 用例）

| owner 决定 | 现状证据 | 判定 |
|---|---|---|
| ① 随从型首领自带"王城被破坏"条件 | `data/cards/flame.json:18`（**全卡池唯一**持有 `ROYAL_CASTLE_BREAK` 者）；C# `EffectRuntime.State.cs:212-224`（持有者一键被动胜，`:209-211` 注释明确"隐藏首领不得从卡组/手牌/墓地胜"）；用例 `EffectRuntimeTests.cs:645`（防守方持有 ⇒ **防守方**胜）、`:678`（隐藏持有者被忽略） | ✅ C# 落地 |
| ② 不内置写死、各写各的 | 卡池中仅烈焰声明破城条件、`machine_alpha` 声明下载轴、`shadow` 为 `NONE`（owner："命运之影先不管"）⇒ 引擎内**无** blanket 硬编码（`HasCastleBreakWinCondition` 是读卡判定，不是常量开关） | ✅ 与实现一致；**规范文本**待 PL 定稿（Q1） |
| ③ `alpha` 走上传/下载轴 | 数据 `machine.json:14`（地标 `machine_leader`，`isLandmark:true`，第 2 层召唤 `machine_alpha`）与 `:65`（`machine_alpha`：MINION、atk 10 / hp 12、圣盾+扰魔）**均**为 `PULL_TOTAL_GE` / `winParam 6` / "己方累计完成6次下载"；schema `data/schema/cards.schema.json:48`（enum 收 `PULL_TOTAL_GE`）；`src/Data/CardCatalog.cs:19` 白名单；C# `EffectRuntime.EndPhase.cs:167-168` + 计数 `EffectRuntime.Mechanical.cs:207`；Java `Game.java:1211` + `PlayerState.java:54`；投影 `Adapters/ContractDtos.cs:44`、`EngineProjectionAdapter.cs:97`；规范 `RULES.md:274`（注明 2026-08-15 人类裁决替换 `OPP_PUNISH_DRAW_TURN_GE`）；用例 `ProductionFactionIntegrationTests.cs:226`（断言 `:321` 卡值 / `:380` `win.pull_total_ge` / `:407` 快照 `PullCount=6`） | ✅ **五层齐**，与"不需要破城"一致 |
| ④ 镜像局破城方胜 | 规范 `RULES.md:138`；C# `EffectRuntime.State.cs:200-207`（`IsMinion && IsMinion` ⇒ `win.castle_break_minion`，且**早于** `:212-224` 的持有者被动胜）；用例 `EffectRuntimeTests.cs:605` | ✅ **C# 落地**；Java **缺这一格**（13.25.3） |

防误读补充：`IsMinion` = `type == "MINION"`（`src/Data/CardCatalog.cs:182`）⇒ `flame_leader`、`machine_alpha` **是**随从型；`machine_leader`（SPELL + `isLandmark`）是**耐久型**，`RULES.md:96` 已规则化"耐久归零则败北"⇒ 与决定①（只有随从型首领会有"被击败"）不冲突。

#### 13.25.2 定向实证（C#，2 分钟内可重跑）

```
dotnet test src\Engine\Tests\DominionWars.Engine.Tests.csproj -c Release -p:MSBuildEnableWorkloadResolver=false --filter "FullyQualifiedName~EffectRuntimeTests|FullyQualifiedName~ProductionFactionIntegrationTests|FullyQualifiedName~PullActionHandlerTests" --nologo
```

结果：**63 passed / 0 failed**（约 1 s）。守护上述四条的用例：`BreakingCastleAppliesCountdownForcesLeaderAndChecksCastleVictory`(:551)、`BreakingCastleWithActiveMinionLeadersGivesBreakerPriority`(:605)、`BreakingCastleRoyalConditionCanAwardActiveDefender`(:645)、`BreakingCastleIgnoresHiddenRoyalLeader`(:678)、`BreakingCastleDoesNotLowerCountOrRepeatBreakResolution`(:718)、`ProductionMachineDeckCompletesCommitPushPullLandmarkAndAlphaVictory`(:226)。⇒ 决定 ①③④ 在 C# 侧**都有回归网**；这也收紧了 F18 的性质：那一格是**唯一没有用例覆盖**的破城分支。

#### 13.25.3 F7 可达性上修（Java 缺两格，不再只是潜伏）

Java `Game.java:583-609`（`breakRoyalCastle`）**只判破城方**，且用 `findLeaderAnywhere`（隐藏首领也能替其所有者胜）；`:1200-1213` 的 `checkSpecialWins` `switch` **没有 `ROYAL_CASTLE_BREAK` 分支**（`default: break`）⇒ Java 同时缺 **① 的防守方被动胜** 与 **④ 的镜像优先级**两格。

**可达性上修**：此前认为镜像场景需要"两张 `flame_leader`"，实际只需**双方统领都是随从** —— `flame_leader`（MINION）× `machine_alpha`（MINION，可由 `machine_leader` 地标第 2 层召唤入场，见 `ProductionFactionIntegrationTests.cs:226`）。该摆盘下 **C# 判破城方胜（`win.castle_break_minion`）、Java 判无人获胜** ⇒ F7 从"语义相反但难触发"变为**跨阵营正常对局即可出现的引擎分歧**，优先级不变（P1）但**不该再被当作潜伏问题延后**。

#### 13.25.4 F18 括注收紧（防"Java 是参考实现"的误读）

F18 场景（双方均持有 ⇒ 破城方胜）Java **结果恰好一致**，原因是它只判破城方 —— **不可**据此认为 Java 正确或可作为参考实现：Java 在镜像摆盘（13.25.3）与防守方被动胜（决定①）上**都缺格**。本报告 F18 行原括注"（Java 亦正确）"已按此收紧。

#### 13.25.5 决策台账更正（⚠️ 过时陷阱）

本会话 SQL `owner_decisions` 的 **4a 行**（记于 2026-08-14："破城时叫出**破城方自己**的首领"）**已被 2026-08-23 的人类裁决取代**，现行为"**叫出对方首领**"。证据：`docs/PL_REPORT_2026-08-23.md:266`、`docs/AI_MAILBOX_ARCHIVE.md:1220`，以及 `git show 2508140` 对 `RULES.md:136` 的改写（`- 并叫出破城方自己的统领` → `+ 并叫出对方首领（经普通抽牌方式强制入场…）`）。现状三方一致：`RULES.md:136` + C# `EffectRuntime.State.cs:194`（`ForceLeaderOut(defender)`）+ Java `Game.java:592`（`forceLeaderOut(victim)`）⇒ **不是缺陷，不要"照台账修错"**；台账已就地标注。

#### 13.25.6 §0 数据基线漂移定位（`sea.json`）——不是笔误，是与写入方并发

| 项 | 值 |
|---|---|
| 当前磁盘 = `HEAD` blob = `8bc0515` 版本 | `sea.json` **`0FA7E4E33425`**（9 446 B） |
| `8bc0515~1`（改写前） | `D324A02DDCBF`（7 156 B） |
| 本报告 §0 记录的 | `4C86A410D0BD` —— **两者都不是** ⇒ 不可复现 |

根因链（三条证据）：
① 引入该字符串的提交就是 `8bc0515`（`git log -S` 命中；`--diff-filter=A` 双证该提交**创建**了本报告的前 348 行）⇒ 哈希快照由 harness 会话写入；
② 五个卡文件 mtime 依次为 `machine 23:11:02`、`flame/neutral 23:11:15`、`wood 23:18:00`、**`sea 23:37:45`** ⇒ 快照取在 **`23:18:00`–`23:37:45` 之间**（逐文件改写过程中），此时四个文件已等于最终内容、`sea.json` 仍在中间态；
③ `sea.json` 于 `23:37:45` 再写一次（`8bc0515` 对该文件 `+119/−18`）并在 `23:41:38` 提交 ⇒ **快照值属于一个从未提交、现已不可恢复的中间状态**（与 §0:50 的并发写入警告同源）。

更正与影响：§0:48 的 `sea.json` 项已改为实测可复现值并加注；其余四项（`machine`/`wood`/`flame`/`neutral`）**已逐一复核 = `HEAD` = 工作树**，仍有效。`8bc0515` 同时改写了全部五个卡文件（`machine +143/−25`、`sea +119/−18`、`wood +115/−13`、`neutral +8/−4`、`flame +7/−5`）⇒ **任何在 `23:37:45` 之前记录的深海（及机械/古木）读数都绑定旧数据**；本报告的阵营读数（F10/F13/F15/F21/F22）时间戳都在其后，绑定当前快照。反之，harness 线在 `23:37` 前落盘的 `docs/PL_*` 数值须按此界线核对。

#### 13.25.7 本节不可外推的部分

① 我**未**在 Java 侧构造 `flame_leader`×`machine_alpha` 镜像摆盘实跑，F7 的上修依据是**源码路径 + C# 用例**，不是跨引擎对局；② `IsMinion` / `isLandmark` 判定基于 `CardCatalog.cs:182` 与卡面字段，**未**在 Unity 运行时验证；③ `machine_alpha` 的"地标第 2 层召唤"入场依据是 `ProductionFactionIntegrationTests.cs:226` 的断言，未做逐帧观测；④ 决定②的**规范文本**（`RULES.md:105/:137/:138`）尚未改写 ⇒ 本节只证明"实现层没有 blanket 硬编码"，**不**证明"规范已表述正确"。

**Q1（交 PL，一句话）**：决定②落地后 `RULES.md:105/:137` 的字面文本（"破城即由某方获胜"）应改写为"**由各随从型统领自己声明的胜利条件决定**；未声明者按 `:105` 通用规则"，并在 `:138` 保留镜像破城方优先。请 PL 给出定稿句式，我再据文本改写后的版本复验一次。

**投递记录（2026-09-11 01:5x）**：F7 可达性上修（🟠 QA → Codex）与 Q1 句式提案（🟡 QA → PL）已作为 `docs/AI_MAILBOX.md` 两条新条目发出，单独提交 **`1024a5f`**（+22/−0，未 push）。**投递不改变本节任何结论**：F7 仍等 Codex 补 Java 两格后由其提交重跑 Java 基线复验，F28 仍阻塞 F25，均待对方落地后再记。

#### 13.26 合并候选（工作树）的独立再验证 —— Java 侧：确定性成立

前提：`build\classes` 与工作树源码同步（最新源 `Balance.java` `00:19:57` < 最新 class `TestMain.class` `00:23:50`）⇒ 无需重建即可对当前 WIP 取值。

| 命令 | 结果 |
|---|---|
| `java -cp "build/classes;build/test-classes" com.dominionwars.test.TestMain` | **59 / 59** 通过 |
| `java "-Dfile.encoding=UTF-8" -cp "build/classes;build/test-classes" com.dominionwars.test.SimMain 300` | 3 600 局，平均回合 **14.793611111111112**；烈焰 **51.2 %**（922/1800）、机械 **13.2 %**（238/1800）、深海 **80.3 %**（1445/1800）、古木 **55.3 %**（995/1800） |

**确定性（本轮新增事实）**：同一 `SimMain 300` 命令独立跑两次，两份输出的 SHA256 **完全相同**（`D950F3B66595115869A9BD648AEFB92AF1316144832CC3B0AA5CDFDCE84D7C9D`，单次约 **7 s**）⇒ Java 测试台**无采样噪声**：`SimMain` 的数值随 N 变（见 F20），但**同一 N 完全可复现**，可作为廉价门禁逐次比对。

**判定**：§0 第 22/25 行记录的工作树读数（`TestMain` 59/59、平均回合 14.79）**第二次独立复现成立** ⇒ 该 1 402 行 WIP **在其自身口径上自洽、可复现**。但**这不等于"可合并"**：合并是 owner/Codex 的决策，我只能提供证据；而且这批 WIP **完全不覆盖 C# 生产路径**——F3 / F7 / F24 / F28 均仍在 C# 侧未变（§13.27 的 `dotnet test` 603/603 只证明既有行为未被破坏，不证明新策略可用）。

#### 13.27 工作树清单复核：**编译路径上有一个未跟踪的 C# AI 策略**（新发现 F29）

事实（全部可复现）：

1. `git status --porcelain` 共 **17** 项，其中唯一的未跟踪**源码**项是 `src\Adapters\Ai\`——内含单个文件 `AdvertisedActionPolicy.cs`（**10 408 B / 242 行**，mtime **2026-09-11 01:49:06**，SHA256 `5CBA5EFF149777…`；`01:56` 复核大小与哈希未再变动）。
2. **它已经被两个构建系统编译**：`src\Adapters\DominionWars.Adapters.csproj` 使用 SDK 默认通配（**无** `EnableDefaultCompileItems=false`、**无** `<Compile Remove>`）；同时 `src\Adapters` 本身就是 Unity 本地包（`unity\DominionWars.Unity\Packages\manifest.json` → `"com.dominionwars.adapters": "file:../../../src/Adapters"`）且带 `DominionWars.Adapters.asmdef`（`autoReferenced: true`）⇒ 该文件进入 `DominionWars.Adapters.dll` **和** Unity 的 `DominionWars.Adapters` 程序集。该目录**没有 `.meta`**，Unity 下次导入会再生成 `Ai.meta` 与 `AdvertisedActionPolicy.cs.meta` 两个未跟踪文件。
3. **零引用**：对全部 `.cs`（排除 `build-output` / `Library` / `obj` / `bin` / `.nightshift`）扫描 `AdvertisedActionPolicy`，只有它自己的声明命中；`unity\…\Assets` 内**没有** `Activator.CreateInstance` / `GetTypes(` / `Assembly.Load` / `GetType("` ⇒ 它今天**惰性、不改变行为**。反过来说：**603/603 全绿不构成对它 242 行的任何覆盖**。
4. **内容 = F3 的"未交付修复" + 同一策略的第三份实现**：与已跟踪的 `unity\…\DominionWars.Runtime\RuntimeAiPolicy.cs`（136 行，`git` 干净，`8bc0515` 之后未再改动）**同形**——`TryChoose` / `ToGameAction` / `StableActions` / `FirstType` 逐段对应——并另外拥有 `Rank` / `IsUsefulPull` / `IsUsable` / `HasWireSelection` / `Ordered`。关键字扫描 `PULL|COMMIT|PUSH|ROLLBACK|CloudStack` 计数为 **12 : 0**（新文件 : Unity 旧文件）⇒ 它正是 F3 缺的下载/提交轴。于是同一策略现在有 **3 份**（Unity `RuntimeAiPolicy`、本文件、Java `AiAgent`）。
5. **来源**：写成时刻 `01:49:06` 晚于 Codex 桌面端的停止时刻（`20:52`，§13.24）、处于 harness（`dsh web`）窗口之内；`Get-Process` 中**没有任何 `codex` 进程**；`scripts\auto-relay\get-relay-status.ps1` 显示 `disabled=false` 但 **`latestRun` 为空**、`goalLedger` 最后一条是 `2026-09-09 20:40 FAILED/SANDBOX_PREFLIGHT`，且 relay 的隔离工作树是 `.nightshift\rehearsal`（**不是本仓**）⇒ **没有任何自动化 run 在写本仓**。该文件**无提交、无邮箱条目**声明它。

风险（全部与"能否合并"直接相关）：

- **半成品会在两个系统上同时短路**：`TreatWarningsAsErrors=true` + Unity 包程序集 ⇒ 这个文件一旦写出警告/错误，`dotnet test` 与 Unity 编译**同时**变红，而它与 F3 修复无关的第三方改动都会被牵连。
- **两个方向的静默动作都很常见**：`git add -A` / `git add src` 会把这 10 KB 未审阅 public API 静默入库；`git clean -fd src` / `reset --hard` 会静默销毁它。
- **F3 可能"修了却仍没修"**：生产路径用的是 `RuntimeAiTurnCoordinator.cs:94` 里的 `_policy.TryChoose(...)`，而 `_policy` 是 Unity 侧的 `RuntimeAiPolicy`；若改动只落在 Adapters 副本上，F3 依旧为零，C# 却多出一个会与旧策略漂移的第二实现。

建议动作：

- **Codex（认领归属，二选一并写明）**：(a) **采纳**——把该逻辑并入**唯一**一份生产策略（改造 Unity `RuntimeAiPolicy` 或改由 Adapters 版接管并更新 `RuntimeAiTurnCoordinator`），并补一条**真正驱动 `PULL` / `COMMIT` 分支**的用例（这才闭合 F3）；或 (b) **移除**，维持现状。两种情况都要在提交信息里写明，避免下一位维护者再猜。
- **PL/owner**：这是一次**未声明的代码写入**（无提交、无邮箱条目），与 §13.24 的写入者归因同源；建议在 relay / 夜班的前置检查里加一条硬规则——**未跟踪的 `.cs` 不得存在于 `src\` 或 Unity 包路径**。

#### 13.28 工作树 17 项清点（合并前决策用）

| 类别 | 工作树项 | 处置建议 |
|---|---|---|
| Java WIP（可合并候选） | `src\main\java\**` 8 文件 + `TestMain.java`，共 **1 402 行插入** | **已由 §13.26 验证自洽**（59/59 + 确定性基准）；提交或丢弃由 **Codex** 决定；**任何情况下不可 `clean`** |
| 未跟踪 C# 源码（**F29**） | `?? src\Adapters\Ai\AdvertisedActionPolicy.cs` | 见 §13.27：**Codex 认领**（采纳并单源化 + 补用例，或移除） |
| 未跟踪 PL 产物 | `?? docs\PL_BALANCE_MEASUREMENT_2026-09-11.md`、`PL_CODE_AUDIT_2026-09-09.md`、`PL_NIGHT_SHIFT_2026-09-11.md` | **PL** 自行提交（我只读，不代传） |
| Unity 编辑器产物（**F30**） | `?? Assets\InitTestScene…unity` + `.meta`、`?? Assets\QA\`（09-05 两张截图 + `.meta`） | **P3**：`.gitignore:39-42` 的意图是忽略"生成的 QA 工件"，但规则是 `/artifacts/`、`codex-ui-*.png`，**不匹配** `Assets\QA\`；`InitTestScene*` 亦无规则覆盖 ⇒ 长期噪声。建议 **Codex** 补两条忽略规则（`/unity/DominionWars.Unity/Assets/InitTestScene*.unity*`、`/unity/DominionWars.Unity/Assets/QA/`） |
| 其余 | Unity `Library/`、`build/`、`build-output/` 等 | 均在 `.gitignore` 内，**未**出现在工作树清单里，无需动作 |

---

## 14. 本轮新增发现汇总与建议动作

> ⚠️ **F17 行已作废，以 §13.17 为准**（F17 撤回为"非引擎缺陷"；剩余工作是改 `RULES.md` 文本 + 修 F18 + 补一格覆盖用例）。**F18 / F19 / F20 行维持有效**，其中 F19 的性质由"需定稿裁决"变为"**需文本改写（owner 意图已明确）**"。新增 **F21**（`docs/BALANCE.md` 陈旧基线，见下与 §13.18）与 **F22**（**`HEAD` ≠ 工作树**：1 402 行未提交代码是唯一的"合并候选"风险，见 §13.19）。**本段再追加 F23**（PL 报告的 §3/§4/§5 **五处取值与表述问题**，**均不改变其结论方向**；并附 QA 独立追加的 **B 模式消融证据**，见 §13.20）。**再追加 F24–F27**（PL 代码审核清单的逐条复核裁决与四项新发现，见 §13.21）。**最后追加 F28**：P0-3 残留形态探针（`%TEMP%\qa-p03p04\`，仓库外只读）证明批内延迟死亡机制**成立但当前不可达**，并由同一探针端到端证实 **canonical v1.31 生产者在溢出击杀后发布负 `currentHealth`**（违反自身 schema、fail-closed 契约）——见 §13.23；**F28 与 F25 硬耦合（先定 F28 再做 F25）**。**另追加 §13.24**：写入者归因复核（今晚的仓库写入**不是 Codex**——Codex 桌面端自 `20:52` 起零线程活动，实际写入方是 harness 的 `dsh web`；并复核 1 402 行 WIP 已冻结、快照 SHA256 未变）。**再追加 §13.25**：owner 四条破城/下载轴决定的逐层落地核查（C# 侧 **63/63** 定向用例守护）、**F7 可达性上修**（镜像摆盘 `flame_leader`×`machine_alpha` 即触发）、F18 括注收紧、决策台账 4a 过时更正、**§0 数据基线漂移定位**（`sea.json` 哈希不可复现的根因 = 与 harness 写入并发，非笔误）。**最后追加 §13.26–§13.28**：**工作树合并候选的独立再验证**（Java `TestMain` 59/59、`SimMain 300` 平均回合 14.793611 且**两次运行输出 SHA256 完全一致** ⇒ 该测试台完全确定性，可作廉价门禁；C# `dotnet test` 603/603）、**新发现 F29**（一个**未跟踪**的 C# 策略 `src\Adapters\Ai\AdvertisedActionPolicy.cs` 已经落在 `dotnet` 与 Unity **两个**构建系统的编译路径上，零引用、零覆盖，内容却是 F3 的未交付修复）、**F30**（`.gitignore` 漏覆盖 Unity 生成物）与工作树 17 项清点。
| # | 发现 | 严重度 | 归属 / 建议动作 |
|---|---|---|---|
| F3 | 生产 AI 无生命周期策略 ⇒ 机械（及任何生命周期轴统领）在发布路径上胜率恒 0，改数值无效 | **P0**（主线完成度） | **Codex**：**C# `RuntimeAiPolicy.cs`** 至今零 `Pull/Commit/Push/Rollback` 分支（Java `AiAgent` 已在 23:53–00:00 补上，但那是测试台）⇒ 需在 C# 侧补提交/下载/地标策略，或在合法动作表层面给出可用选择 |
| F7 | **规则分歧**：`ROYAL_CASTLE_BREAK` 在 C#（合规）与 Java（缺防守方分支）语义相反；Java 亦未见 `castle_break_minion` 分支。**⚠️ 可达性已上修（§13.25.3）**：镜像摆盘**不需要两张 `flame_leader`** —— 只要双方统领都是随从（`flame_leader` × `machine_alpha`，后者可由 `machine_leader` 地标第 2 层召唤入场）即触发 ⇒ 这是**正常对局可达**的引擎分歧，不宜再按潜伏问题延后 | **P1** | **Codex**：按 `docs/RULES.md:105/137/138` 对齐 Java `Game.java:593-604`（补防守方被动胜 + 镜像优先级两格） |
| F2 | `machine_leader.winParam = 6` 过强：一旦被追求，胜率 95.7%、8.4 回合结束 | **P1** | **PL/owner**：调 `winParam` 与提交/下载成本；**先修 F3 再调**，否则读数仍不可用 |
| F1 | 规则本身使破城 = 烈焰获胜（不问谁破城）⇒ 烈焰 84–91%；`winText` 只描述一半 | **P1** | **PL/owner**：F1 是**平衡/语义**问题（C# 实现与 `RULES.md` 一致，不是缺陷）；如需"分开"，由 PL 定义第二个枚举值 |
| F4 | 平均回合 C# 侧 6.2–10.2 低于目标带（Java 侧移植后已回到 14.79） | **P1** | **PL/owner**：目标带 10–20 是否随新规则集调整；C# 侧节奏需在生产 AI 补全后重测 |
| F5 | 古木 512 轴 C# 侧 600 局只发生 1–4%（Java 侧古木 55.3% 反而正常） | **P1** | **PL/owner**：先查 C# 侧古木 AI 是否也缺策略（与 F3 同源），再决定是否动数值 |
| F10 | **深海跨引擎一致偏强**（Java 80.3% / C# 74.0%，均超 40–60% 带） | **P1** | **PL/owner**：这是本次可信度最高的平衡结论（两引擎独立复现） |
| F6 | 权威引擎平衡预言机已可用（仓库外） | 工具 | **Codex**：如认可，可将其纳入 `scripts/` 作为例行门禁（需 owner 授权写入 `scripts/`） |
| F8 | ~~Java 回归在飞状态 51/52~~ **已关闭（00:20 复验 59/59 全绿）** | ~~P2~~ 已解决 | 写入方已自行修掉；`chainLimit` 差一（§13.6 第 4 条）亦已修（新用例显式对照 C# 的 `>` 而非 `>=`）（详见 §13.11） |
| F9 | Java 测试数 38 → 52，编译干净，平均回合 25.0 → 14.79 | 正向 | 记录在案作为移植有效性的证据 |
| F11 | `SET_AMBUSH` 被广告但本引擎在 `手牌−1 < punish` 时无法满足（"广告 ⇒ 可解"契约违规） | **P2** | **Codex**：`TurnFlow.cs:204` 加 `&& candidates.Count >= punish`，补回归测试（详见 §13.12.3） |
| F12 | **古木封印机制自相矛盾**：`woodSource` 阵营开关令每一张古木 buff 都成为增幅 ⇒ 目标被封印且攻击归 0，而封印单位不能攻击（`AttackTargetPolicy.cs:17`） | **P1（机制，非数值）** | **PL/owner**：PL 的 W1 方案（封印只认显式 `param`）与我 F5 同向；**应在 W1 落地后重测 F4/F5**（详见 §13.12.2-3） |
| F13 | PL 独立建成第二套 C# 预言机（`build-output/pl-csim/`，已 gitignore）⇒ 两套工具交叉验证：**深海偏强**为唯一两引擎两工具共同确认的结论 | 正向 | 记录在案；`PUNISH_DRAW` 138:10.2 的洪流比是本轮最有解释力的单量（详见 §13.12） |
| F14 | **归因更正**：仓库实时写入者是 `dsh`（DeepSeek Harness）会话，**不是 Codex**。`codex.exe` PID 29668 是桌面应用常驻 `app-server`，20 秒 CPU 增量 0.00s、无回合子进程 | 澄清 | 记录在案；**不改变任何缺陷的修复归属**（F3/F7/F11 仍发 Codex）（详见 §13.13） |
| F15 | **`punishActivatable` 占比是当前最强的单一解释变量**：深海 60% vs 古木 10%（我独立按 `data/decks` 加权复核，与 PL 完全一致）；PL 消融显示清掉该标志可移动 8–62 点 | **P1** | **PL/owner**：这是"惩罚响应经济"的核心货币，应在设计层决策（PL 的 T1/惩罚链预算）；**先不要用数值微调去抵消它** |
| F16 | PL 报告 §4 的 `BUFF-bearing cards` 列有 2 处数据错误：机械 **0 应为 24**、深海 **6 应为 9**（烈焰 3、古木 24 正确） | P3（报表） | **PL**：更正 `docs/PL_BALANCE_MEASUREMENT_2026-09-11.md`；不影响 `punishActivatable` 载荷结论（详见 §13.12.5-(b)） |
| ~~F17（原文，已撤回）~~ | 见**本表末尾**同号修订行与 §13.17 —— **破城胜利在"双随从 + 仅防守方持有"时归属冲突**：`EffectRuntime.State.cs:200-207` 的 `IsMinion && IsMinion` 预判**只要求"双方统领都是随从"、不要求任何一方持有 `ROYAL_CASTLE_BREAK`**，因此在"仅防守方持有"时**抢走防守方（烈焰）的被动胜利**。**⚠️ 定性：C# 忠实实现了 `:138` 的*字面*文本 —— 根因是 `:138`（字面）与 `:105/:137`（不问谁破城）矛盾（= F19），不是 C# 代码错。定稿前不要动 C#。** 已实证可达（变体 B：`win.castle_break_minion`×9，前提是机械方晋升 `machine_alpha`），3 000 局内未造成可观测偏差；`BreakingCastle` 5 条用例当前 **5/5 全绿** | **P1（规范冲突）** | **PL/owner 定稿 → 再由 Codex 落地**<br>见 §13.14-⑤：**两种读法各有一个修复方案**；(a) 收窄 ⇒ 删预判（含行为变更），(b) 保留字面 ⇒ C# 基本正确、仅需修 F18 + 给 `:137` 补例外条款。**两案都必须改 `EffectRuntimeTests.cs:604-642` 并新增那一格用例**（测试代码属 Codex） |
| ~~F18（原文）~~ | 见**本表末尾**同号修订行与 §13.17 —— **同一函数的相反方向的过窄**：`EffectRuntime.State.cs:214-217` 在"双方均持有 `ROYAL_CASTLE_BREAK`"时 `return`（无人获胜），而 `RULES.md:138` **在此场景没有歧义**地要求**破城方胜**（Java 在**本场景**结果恰好一致——它只判破城方；**不可**据此认为 Java 是参考实现，见 §13.25.3/§13.25.4）⇒ **可无条件判定为缺陷、可立刻修，不需要等定稿**。今天不可达（仅 `flame_leader` 持有），但**下一个持有该条件的统领一落地即暴露** | **P2（潜伏，可立刻修）** | **Codex**<br>改为 `if (!breakerHolds && !defenderHolds) return;` 再 `DeclareWinner(breakerHolds ? breaker : defender, "win.royal_castle_break")`。与 F17 方向相反 ⇒ C#/Java 在第 4/5 行场景上**恰好互换** |
| F19 | **`RULES.md:138` 规范文本自相矛盾**：触发条件写作"双方统领均为随从型"，理由写作"以避免**双方条件同时满足**时产生平局"。**owner 意图已由 §13.17 定稿**（主动破城须受奖 ⇒ 第 3/4 行破城方胜），故 F19 不再是"待裁决"而是**纯文本改写**：触发条件宽于其自述理由，且与 `:105/:137`「被动，不问谁破城」矛盾 | **P1（规范）** | **PL/owner**：改 `:105/:137/:138` 文本（理由与触发条件对齐 + `:137` 补例外指针 + `:105` 改为"其他随从型统领若声明了自己的胜利条件则以自己声明的为准"）；**不再需要裁决第 3/4 行**（详见 §13.17 / §13.18 / 邮箱 🟢 条目） |
| F20 | **`SimMain` 读数强烈依赖 `N`，低 N 不可与 N=300 混用**：同一确定性构建下机械在 N=20 读 **19.2%**、N=300 读 **13.2%**（+6 pts）；因 `seed = a*1000+b*100+k` 使样本**嵌套**，低 N 是**偏置的早期分块**而非随机子样本。另：因 `k` 步长上限 100，**N>100 时种子范围跨对局重叠**（N=300 时 1 200/2 200 个 seed 被 ≥2 个对局共享） | **P2（测试台）** | **Codex**：`SimMain.java:39` 改 `seed = (a*decks.size()+b)*N + k`；摘要打印 N。**PL/owner**：勿引用 `SimMain < 200` 的阵营胜率（平均回合对 N 不敏感，可继续引用）（详见 §13.15） |
| F21 | **`docs/BALANCE.md` 的平衡基线与复现命令早已失效**：① `:3-10` 标"**最新**模拟数据"却与 `:18` 给出的命令（`SimMain **8**`，96 局）**逐项不符**（表里机械 52.1% / 深海 41.7%，今天 N=8 实测 **18.8% / 79.2%**，差 33.3 / 37.5 pts）⇒ 表不是该命令的产物；② `:13`「所有阵营胜率落在 40%–60% 带内」**不再成立**（今天机械 18.8% 与深海 79.2% 都在带外，古木 56.3% 与烈焰 45.8% 仍在带内）；③ `:18` 用 `SimMain 8` 而 `:46` 要求「`SimMain 30` 以上」⇒ **同文档自相矛盾，且两者都不够**（按 F20，`N=30` 仍是嵌套偏置样本）；④ `:21` 把**系统性偏置**说成"可加大以降低方差"；⑤ `:73`「规则测试 **35/35**」现为 **38/38（提交态）/ 59/59（工作树）**。同一失效命令复制到 `docs/DESIGN.md:91`；`:54` 的基线无日期/引擎/N | **P2（文档权威性）** | **PL/owner**：改 `BALANCE.md §1`（带 N/日期/构建）与 `§4` 第 2 条（"阵营胜率 `N ≥ 200`"），更正 `DESIGN.md:91`；**Codex**：与 F20 的种子修复一并做（详见 §13.18） |
| **F22** | **`HEAD` 与工作树是两个不同的引擎（⚠️ 本轮唯一的 P1）**：工作树里 `src/main/java/**` **9 个文件 + `TestMain.java` 共 1 402 行插入未提交**（`Game +240`、`Effects +197`、`AiAgent +143`、`CardDef +114`、`CardInstance +22`、`PlayerAgent +24`、`Balance +20`、`PlayerState +12`、`TestMain +658`），且 **`git branch -avv` 的全部落点与 `git stash list` 都不包含它们**。用 `git archive HEAD` 在仓外**独立重建提交态**：`build.bat` exit 0、`TestMain` **38/38**、`SimMain 300` 平均回合 **22.61**（**超出 10–20 带**）、烈焰 59.1% / 机械 14.3% / 深海 **90.9%** / 古木 **35.7%**（两项在 40–60 带外）；工作树则为 59/59、14.79、51.2/13.2/80.3/55.3 | **P1（流程 / 数据丢失）** | **Codex / harness**：把这批 WIP 落到明确命名的提交或分支（哪怕标 `WIP`）；**owner**：或明确决定归档/丢弃（丢弃不可恢复，需明确同意）。**在此之前：不要用 `HEAD` 评估合并、不要把工作树读数当"已交付"、任何"干净基线"验证必须先声明测的是哪个修订版**（详见 §13.19） |
| **F23** | **PL 的 C# 实测：结论可复现、但有五处取值/表述问题**（⚠️ 全部为**表述层**，**结论方向不变**）**① §3 链深直方图是"两件仪器逐档相加"**：`Simulator.cs:375-376` 把引擎 `PUNISH_TRIGGERED` 事件与策略 `RecordingPunishPolicy.DepthHistogram`（按 `Decide` 调用计数）**合并进同一个字典**再输出（`:165-166` / `Program.cs:158`），合计 **108 730 = 事件 48 060 + 决策 60 670**，是链数 **7 541** 的 **14.4 倍**，仅 depth-1 桶（**9 855**）已 > 链数 ⇒ **没有任何一档可读作链数**（harness 自打的标签 `(depth:decides)` 亦与数值不符）；**"链被 20 档上限截断"仍成立，但依据应是** `data/balance.json:8 chainLimit = 20`（= `PlayCardActionHandler.DefaultChainLimit`，`:306` `chainDepth > _chainLimit` 截断）**且 8 组 A 配置的实测 `maxChainDepth` 全部恰好 = 20**；**② §5 表** "策略层面完全不打出 BUFF 卡"古木 **10.61%** 应为 **10.56%**（产物 `abl-A-woodNoBuffPlay.json` = 10.56，§5 正文自己写的也是 10.56 ⇒ 同文档自相矛盾）；**③ §4「平均回合」列**（5.79/14.14/6.92/8.41/7.49）与产物 `factions[烈焰帝国].avgTurns` **逐值完全相等（5/5，diff = 0）** ⇒ 实为"**烈焰所参与对局**的平均回合"，非全局（全局均值 ≈ **6.01**）；**④ §4「清掉深海」行** 90.60/48.60/**23.11**/37.80 与产物 90.56/48.61/**23.06**/37.78 不符（1 位小数下一致；−61.4 pts 不变，84.44 − 23.06 = **61.38**），同组 json/txt **文件名不一致**（`-non-activatable` vs `seaNonActivatable`）；**⑤ §3 的 88% 与 A→B 差**存在**分母不同**问题（`punishDrawInitial` A **12 126** vs B **45 248**；roots A **7 541** vs B **24 237**）⇒ 88% 只在 **A 内部**成立，**A→B 不是单变量消融**；B 模式 `maxChainDepth = 0` 是**未填充字段**。另：**5 组** A 配置各有 **1 次**非法动作被拒（`action.discard_selection_invalid`，**引擎正确拒绝**） | **P2（度量表述）+ P3（报表）** | **PL**：更正上述文字；harness 侧建议：**两个直方图分开输出**、统一产物文件名、B 模式 `maxChainDepth` 输出 `null`。**QA 追加证据（正面强化 S1）**：`abl-B-sea-non-activatable` ⇒ 深海 67.5% → **31.11%**（−36.4 pts）、烈焰 58.33 → 70.83、机械 31.11 → 40.56、古木 43.06 → 57.5 ⇒ **"可降临密度是决定性货币"在关闭响应回环后依然成立**（PL 的 B 模式只跑了 flame/sea 两组消融）。**QA 不改 PL 的文件，只提案**（详见 §13.20） |
| **F24** | **C# 引擎完全不读 `data/balance.json`（跨引擎参数单一来源缺失）**：`src/` 内 `balance.json` **零命中**（只有 Java `Balance.load`；Java 侧还有"读盘失败回退 + 写 stderr"保护）。C# 把 `chainLimit=20`（`PlayCardActionHandler.DefaultChainLimit`）、`castleHealth=75`（`MatchSetup`/`GameState`）、`reshuffleLossThreshold=10` 与 `castleBreakVictoryCount=9`（`GameState`）、`handLimit=8` 与 `pioneerHandLimitBonus=2`（`MatchRules`）**全部硬编码** ⇒ **改这份文件对 Unity 侧完全无效**，而 PL/owner 的平衡讨论正围绕它进行。**清单 P0-1（先驱惩罚缺失）只是该缺失的一个实例**；C# 已实现 pioneer 的手牌上限那一半（`DiscardPhaseHandler.cs:111`），缺的正是读参数的惩罚那一半 | **P0（主线正确性）** | **Codex**：让 C# 从 `data/balance.json` 装载（或明确声明唯一来源），并补一致性测试；修复 P0-1 时可复用 `DiscardPhaseHandler.cs:111` 的 solo-leader 判据。**PL/owner**：需知晓"这份平衡文件目前只作用于 Java 侧"。**行为级实证（§13.22 四臂对照）**：改 `data/balance.json` 的 5 个键 ⇒ C# 1 500 局输出**逐字节不变**（SHA `9F96…`）；只改 `machine_leader.winParam` 一张卡 ⇒ 输出立刻变（SHA `169A…`）⇒ 该分叉在门禁上**完全静默**（当前两套默认值恰好相等） |
| **F25** | **快照 wire format 从来没有契约测试**：canonical `design/runtime-kit-v1.31/contracts/schemas/game_snapshot.schema.json`（`additionalProperties:false`、required 含 `snapshotRevision`/`viewerPlayerId`/`pendingPrompt`、player 要 `fieldCount`/`graveyardCount`/`ambushCount`、**无 `deck` 字段**、`card.currentHealth minimum 0`）**从未被任何 C# 测试引用**（`ContractBoundaryTests.cs:151` 只引 `game_action.schema.json`，v1.31 目录虽有 valid/invalid fixtures）。legacy `SnapshotDto`/`PlayerDto`（`src/Adapters/ContractDtos.cs:7-49`：PascalCase `Id`/`FieldEntityIds`/`Deck`/一堆 `*ThisTurn`）**结构上不可能通过 v1.31 校验**却仍在仓库里被测试固化（`EngineProjectionAdapter.cs:62-103` 还投影**有序牌库**与双方手牌）。这正是 **P2-3 负 HP** 与 **P2-5 名不副实测试**能长期存活的共同根因 | **P1（契约）** | **Codex**：① 加用 `game_snapshot.schema.json` 校验生产快照的测试；② legacy 1.30 DTO/适配器打 `[Obsolete]` 或删除；③ 修 `RuntimeSnapshotProjectionTests.cs:453` 的断言（它只验 `DeckCount`，从未验 `Deck` 列表）。**详见 §13.21.5** |
| **F26** | **v1.31 的"事件边界"有契约、有校验器，却没有生产者**：引擎实际发出 **46** 种事件类型、**51** 种 `Data()` 载荷键（字面量扫描，**下界**）；canonical `ui_event.schema.json` 的 `type.enum` 只有 **29** 个，`RuntimeEventCursor`（`:29-38`）**逐个对齐这 29 个**并把载荷键限制为 **7** 个（`:40-43`，`:92` 未知键即 `event.data_field_unknown`）。**但 `RuntimeEventEnvelope`/`RuntimeEventCursor` 在 `src/` 中只被 `src/Engine/Tests/RuntimeEventCursorTests.cs` 构造**；`RuntimeMatchGateway` 对外返回**原始引擎 `GameEvent`**（`:110-111/:177`）。唯一的类型改名表是 legacy 1.30 的 `EventTypeMap`（`EngineProjectionAdapter.cs:14-43`，26 键、仅覆盖 22 种引擎类型；批量为**静默丢弃**、单条为**抛异常**）⇒ **28 个引擎事件类型在 canonical 契约中不可表达、11 个契约类型无生产者**。任何要把事件按契约扇出到 Web 的宿主都得再写一遍映射 | **P1（集成缺口）** | **Codex**：补 `GameEvent → RuntimeEventEnvelope` 的生产映射（改名表 + 载荷裁剪 + `snapshotRevision` 打戳），覆盖全部 46 种（或与 PL 一起收窄 enum）；**PL/owner**：确认那 29 项 enum 是否为最终意图。**清单 P2-8（`DEFEAT_PREVENTED`）是本条的一个实例**（引擎发出但两端白名单都没有 ⇒ 批量静默丢弃） |
| **F27** | **（澄清，防重写）** `RuntimeEventCursor` 的校验质量高于清单假设：已实现乱序 / 缺号 / 重复 / 父缺失 / 相位 / 版本 / 终局载荷（`GAME_OVER` 必须恰好两字段且 `reasonKey` 齐备，`:95-115`）等拒绝原因 ⇒ 清单把 P2-8 描述为"白名单缺失"**低估了已有实现**，真正的缺口是 F26 的"没有生产者" | 澄清 | 记录在案，供 Codex **复用而非重写**；**详见 §13.21.5** |
| **F28** | **canonical v1.31 的投影会发布违反自身 schema 的负 `currentHealth`（溢出击杀 ⇒ 投影与契约不可能同时满足）**：`EffectRuntime.Combat.cs:333`（`DamageCard`）**不夹零**（对照：`:112` 与 `Runtime.State.cs:182` 都夹零；Java `Game.java:1112` 状态不夹零但 `:1113` 日志夹零），而 canonical 的**唯一生产者** `Adapters/RuntimeContractV131Snapshot.cs:160`（`CurrentHealth = card.Health`）**原样搬运** ⇒ 任意伤害超过剩余血量时，墓场卡 `currentHealth` 为负，违反 `contracts/schemas/game_snapshot.schema.json:57` 的 `minimum: 0`，而契约 `:74` 要求无效消息 **fail-closed**；同时契约 `:77` 又要求投影**必须直投权威 `CardInstance.Health`** ⇒ **同时满足不可能**。**实证**（JSON 由仓库自身 `RuntimeWireSerializer` 生成）：S6 施加 `DAMAGE ALL_ENEMY_MINIONS 5` ⇒ `graveyard = [0, −2, −4]`、schema **2 errors**（`players/1/graveyard/1`、`/2` 的 `currentHealth`）；S7 同工作台不施加效果 ⇒ **0 errors**；两臂 `jsonBytes` 只差 2 ⇒ **违规被隔离到负血量这一个值**。**影响**：合规客户端必须拒绝一份合法对局快照；Unity 检视视图经 `RuntimeCardDisplayModel.cs:106` 会显示 −2（主战面板墓场只显示数量）。**只在 v1.31 成立**（v1.30 的 schema 无 health 约束） | **P1（契约）** | **PL/owner 定稿契约文本 → 再由 Codex 落地**。**PL 裁决四选一**：① 投影层夹零 `Math.Max(0, …)`（**零规则影响**，与 Java 日志约定一致，需在 `:77` 加一句"权威不夹零、展示夹零"豁免）＝**推荐**；② `Health ≤ 0` 时省略 `currentHealth`（schema 已允许，**零 schema 改动**）；③ 引擎层 `DamageCard` 夹零 —— **不是无害重构**：按 S3，3 血单位受 5 伤夹零 → 0，再受同批 `HEAL 2` → **2 存活**，**改的是规则结果**，需 owner 批准（该场景当前无卡可达）；④ 放宽 schema＝最差。**Codex**：**必须先定 F28 再做 `codex-f25-snapshot-contract-test`** —— 该测试今天实现即红（任意溢出击杀）。**探针/复现**：见 §13.23.1（三条命令，仓库外，只读仓库） |
| **F29** | **未跟踪的 C# AI 策略落在编译路径上（工程卫生 / 归属）**：`src\Adapters\Ai\AdvertisedActionPolicy.cs`（10 408 B / 242 行，mtime `2026-09-11 01:49:06`，SHA256 `5CBA5EFF1497…`）**不在任何提交里**，却**同时被 `dotnet` 与 Unity 编译**（`DominionWars.Adapters.csproj` 用 SDK 默认通配；`manifest.json` → `"com.dominionwars.adapters": "file:../../../src/Adapters"` + `autoReferenced: true` 的 asmdef），且**零引用**（全仓 `.cs` 扫描只有它自己的声明；`Assets` 内无 `Activator.CreateInstance`/`GetTypes(`/`Assembly.Load`）⇒ 今天惰性、行为不变，但 **603/603 不为它 242 行提供任何覆盖**。内容与已跟踪的 Unity `RuntimeAiPolicy.cs`（136 行、`git` 干净）**同形**，并额外有 `Rank` / `IsUsefulPull` / `IsUsable` / `HasWireSelection` / `Ordered`；`PULL\|COMMIT\|PUSH\|ROLLBACK\|CloudStack` 关键字 **12 : 0** ⇒ **这是 F3 的未交付修复 + 同一 AI 策略的第 3 份实现**（Unity ⊕ 它 ⊕ Java `AiAgent`）。写入时刻晚于 Codex 停止（`20:52`），`Get-Process` 无 `codex`，relay `latestRun` 为空、`goalLedger` 末条 `2026-09-09 FAILED`、隔离工作树在 `.nightshift\rehearsal` ⇒ **没有自动化 run 在写本仓**，属**未声明写入**（无提交、无邮箱条目） | **P2** | **Codex**：认领归属并二选一 —— (a) **采纳**：并入**唯一**生产策略（改造 Unity `RuntimeAiPolicy`，或改由 Adapters 版接管并更新 `RuntimeAiTurnCoordinator.cs:94`），并补一条**真正驱动 `PULL`/`COMMIT`** 的用例（这才闭合 F3）；(b) **移除**。两种都要写进提交信息。⚠️ 合并冲突期**不要** `git add -A`（会把这 10 KB 未审阅 public API 静默入库），也**不要** `git clean -fd src`（会静默销毁它）。**PL/owner**：建议给 relay / 夜班加硬规则"未跟踪 `.cs` 不得存在于 `src\` 或 Unity 包路径"（详见 §13.27） |
| **F30** | **`.gitignore` 漏覆盖 Unity 生成物**（噪声）：`Assets\QA\`（09-05 两张截图 + `.meta`）与 `Assets\InitTestScene….unity(.meta)` 长期以未跟踪状态存在；`.gitignore:39-42` 的规则是 `/artifacts/` 与 `codex-ui-*.png`，**不匹配**这两处 ⇒ 每次 `git status` 多 4 项噪声，也让"工作树是否干净"的判断更易出错 | **P3** | **Codex**：补 `/unity/DominionWars.Unity/Assets/InitTestScene*.unity*` 与 `/unity/DominionWars.Unity/Assets/QA/`（详见 §13.28） |
| F17 | ~~"双随从 + 仅防守方持有 ⇒ C# 抢走防守方被动胜"~~ **⚠️ 本节结论已由 §13.17 整体撤回**：按 owner 2026-09-11 的定稿（「不然大家都不打王城了」+「alpha……所以不需要破城」），第 4 行的**应然结果就是破城方胜**，C# `:200-207` **结果正确**、`EffectRuntimeTests.cs:604-642` **编码的正是定稿规则应保留**；§13.14-⑤ 的**方案 (a) 作废**。剩余工作＝改 `RULES.md:105/:137/:138` 文本（PL/owner）+ 修 F18 + 补一格覆盖用例（Codex） | **~~P1~~ 撤回** | **PL/owner**（文本）+ **Codex**（F18 + Java 对齐 + 补用例）。**详见 §13.17** |
| F18 | **F17 撤回后，本地唯一的真缺陷**：`EffectRuntime.State.cs:214-217` 在"双方均持有 `ROYAL_CASTLE_BREAK`"时 `return`（无人获胜），而 `RULES.md:138` 与 owner 的"打破平局"意图都要求**破城方胜**（Java 在本场景结果恰好一致——它只判破城方；**不可**据此认为 Java 是参考实现，见 §13.25.3/§13.25.4）⇒ **可无条件判定为缺陷、可立刻修，不需要等定稿**。今天不可达（仅 `flame_leader` 持有），且在第 3/4 行被 `:200-207` 抢先 `return` 掩盖；只在"双方均持有 **且** 至少一方非随从"时暴露 | **P2（潜伏，可立刻修）** | **Codex**：改为 `if (!breakerHolds && !defenderHolds) return;` 再 `DeclareWinner(breakerHolds ? breaker : defender, "win.royal_castle_break")`。**C# 的 `:200-207` 不要动**（见 §13.17） |

**关于 `docs/AI_MAILBOX.md` 第 471-485 行旧条目的更正**：该条目第 3 条把机械 −31.2pp 归因为"数据改动"，**已作废**；正确归因见 §13.3（7 变体隔离实验：唯一 Java 可见成因是删除 `chant`+`chantEffects`）与 §13.8（能力覆盖差）。结论方向也需改写为 §13.10 的 F2/F3：**机械不是变弱了，而是在旧测试台上不可见、在生产 AI 下不可胜、在被正确驾驶时过强。**

---

— DeepSeek（测试负责人）· 2026-09-10 / 复验追加 2026-09-11 00:06 / 交叉验证追加 2026-09-11 00:12 / 进程取证与消融复核追加 2026-09-11 00:19 / 破城胜利分歧与采样缺陷追加 2026-09-11 00:26 / **定稿复核：F17 撤回、F18 维持（§13.17）、`BALANCE.md` 陈旧基线 F21（§13.18）追加 2026-09-11 00:34** / **读数复现校验 + F21 范围更正（"部分失效"）+ F19 转纯文本改写 2026-09-11 00:36** / **提交态 vs 工作树独立重建对比、新增 F22（1 402 行未提交代码）2026-09-11 00:44** / **独立复现 PL 的 10 080 局 C# 权威实测（14/14 配置、逐字段 0 差异）+ 新增 F23（§13.20）2026-09-11 00:49** / **复核 PL 代码审核清单：逐条裁决 + 新增 F24（C# 不读 `data/balance.json`）/F25（快照无契约测试）/F26（事件契约无生产者）/F27（§13.21）2026-09-11 01:0x** / **F24 行为级实证：改该文件对 C# 输出零影响、改一张卡即变（四臂对照含阳性对照，§13.22）2026-09-11 01:2x** / **P0-3 残留探针（仓库外 S1–S7：批内延迟死亡机制成立但当前不可达）+ 新发现 F28（canonical v1.31 投影发布负 `currentHealth`、违反自身 schema）§13.23 2026-09-11 01:2x** / **写入者归因复核：本次写入归因 harness（`dsh web`）而非 Codex；并列证 1 402 行 WIP 已冻结（快照 SHA256 未变，§13.24）2026-09-11 01:3x** / **owner 四条破城/下载轴决定落地核查（C# 侧 63/63 定向用例守护、F7 可达性上修、F18 括注收紧、决策台账 4a 过时更正）+ §0 `sea.json` 基线漂移定位（§13.25）2026-09-11 01:5x** / **F7 与 `RULES.md` 文本提案投递邮箱（提交 `1024a5f`）2026-09-11 01:5x** / **合并候选独立再验证（Java 59/59 + `SimMain 300` 两次运行 SHA256 一致 = 完全确定性；C# 603/603）+ 新增 F29（编译路径上的未跟踪 C# AI 策略）/F30（§13.26–§13.28）2026-09-11 01:5x**
