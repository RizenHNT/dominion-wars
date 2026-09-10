# 2026-09-09 PL 完整审核 + 卡牌设计落地报告

> 作者：PL（DeepSeek V4 Flash harness）· 2026-09-09
> 范围：① 代码完整审核（对抗子代理 + PL 复验）② 91 卡池逐卡审核 ③ 卡牌设计落地（owner 授权直接落 data）④ 规则优化建议（**仅说明，未改规则**）
> 依据：owner 指令"完整审核代码和设计具体卡牌，找到最合理的游戏规则优化和卡牌设计；卡牌设计可以直接落地；规则上任何改动先给我说明"
> 落地文件：`data/cards/{wood,machine,sea,flame,neutral}.json`（备份 `build-output/pl-backup-20260909/`）

---

## 1. 我做了什么（一句话）

发现正式 91 池的三个结构性问题——**古木 512 轴数学不可达、机械生命周轴同质化、20 张卡文本空白**——把它们落地修好，并用**真实 C# 引擎跑通验证**（不是纸面推演）：古木载体从 1 血长到 **625 血**并触发 `win.giant_health_ge`。

---

## 2. 卡牌设计落地明细（已写入 data/cards）

### 2.1 古木：512 胜利轴从"不可达"→"可达"（本次最大修复）

**问题**：正式池 0 张扎根卡、0 张 ADD_ROOT 引用；唯一 BUFF 卡 `wood_growth` 单次只能 +16（(2+0)×8），全 deck 只有 1 张 → 512 **数学上不可能**。

**落地**（9 张卡补扎根供给 + 1 张改造成真正的疯长卡）：

| 卡 | 变更 | 文本 |
|---|---|---|
| wood_sapling | +ADD_ROOT 2 | 登场：扎根2。 |
| wood_wisp | +ADD_ROOT 2 | 登场：扎根2，你抽1张牌。 |
| wood_guard | +ADD_ROOT 2 +BUFF 2 | 嘲讽。登场：扎根2，并强化一个友方随从+2/+2。 |
| wood_druid | +ADD_ROOT 2 +BUFF 2 | 登场：扎根2，恢复一个友方随从2点生命，并强化一个友方随从+2/+2。 |
| wood_bear | +ADD_ROOT 2 +BUFF 2 | 嘲讽。登场：扎根2，并强化一个友方随从+2/+2。 |
| wood_treant | +ADD_ROOT 2 +BUFF 2 | 嘲讽。登场：扎根2，并强化一个友方随从+2/+2。 |
| wood_stag | +ADD_ROOT 2 +BUFF 2 | 突袭。登场：扎根2，并强化一个友方随从+2/+2。 |
| wood_owl | +ADD_ROOT 2 +BUFF 2 | 【惩罚】以惩罚0登场并抽1张牌。登场：扎根2，并强化一个友方随从+2/+2。 |
| wood_warden | +ADD_ROOT 2 +BUFF 2 | 扰魔（不能成为指定型效果的目标）。登场：扎根2，并强化一个友方随从+2/+2。 |
| wood_seed | +chantEffects 扎根2 | 吟唱2：召唤一个古树行者，并扎根2。 |
| wood_growth | 改为 ADD_RAMPANT 1 + BUFF 2 | 疯长1，并强化一个友方随从+2/+2。 |

**设计依据**：RULES §12.2（扎根每层+1/+1、疯长每层×2、增幅后封印、512 阈值）；设计源 `bundle_v2.json` 古木有 63 张"扎根：+N/+N（强化）"卡，正式池一张未落地。

**引擎级验证证据**（`build-output/pl-verify/`，真实 C# 引擎，走完整出牌管线）：

```
载体 wood_guard 起始 1/1，疯长=3（满层 ×8）
回合1 wood_sapling  root=2  载体=1/1
回合1 wood_wisp     root=4
回合1 wood_druid    root=6  载体=65/65   sealed=True     ← (2+6)×8=64
回合1 wood_bear     root=8  载体=145/145  sealed=True     ← (2+8)×8=80
回合3 wood_treant   root=10 载体=241/241                  ← (2+10)×8=96
回合3 wood_stag     root=12 载体=353/353                  ← (2+12)×8=112
回合3 wood_owl      root=14 载体=481/481                  ← (2+14)×8=128
回合3 wood_warden   root=16 载体=625/625  win=0 win.giant_health_ge  ← (2+16)×8=144
结论：512 轴可达 ✓
```

### 2.2 机械：落地 M1 差异化（原为 8 张全同质）

**问题**：8 张普通随从全部 `commit=1/upload=0/download=1` + 一律"下载后+1/+1"，无 commitEffects/pushEffects；4 张文本空白。

**落地**（按 9/8 提案 §3.3 M1，owner 指令交付物）：

| 卡 | commit | download | 新增时点效果 |
|---|---|---|---|
| machine_drone | 1 | 1 | —（轴燃料，上传免费） |
| machine_golem | **2** | 1 | —（留场主力，提交代价高） |
| machine_wall | **2** | 1 | — |
| machine_blaster | **3** | 1 | —（站场打手） |
| machine_titan | **3** | **2** | —（大墙） |
| machine_spark | 1 | 1 | **提交：抽1张** |
| machine_assembler | 1 | 1 | **上传：抽1张** |
| machine_recycler | 1 | 1 | **提交：回滚队列1张** |

**规则边界说明**：M1 原案把 `uploadCost` 抬到 1/2，但 RULES §12.4 L256 已冻结"PUSH 普通默认 0，避免 COMMIT/PUSH 双算"。**我保留了 upload=0**（不擅自改规则行为），只落地 commit 差异化与时点效果。若你要 M1 原案的 upload=1/2，需要你确认（那会让 PUSH 时点产生惩罚抽牌）。

### 2.3 文本补全（20 张空白 → 0 张）

- 古木 6 张（sapling/guard/bear/treant/warden/stag）随本次设计一并补全
- 机械 4 张（golem/wall/titan/spark）+ assembler/recycler 陈旧文本
- 深海 4 张（crab/tentacle/eel/leviathan_young）
- 烈焰 4 张（recruit/drake/giant/elemental）+ sacrifice 名实修正
- 中立 2 张（mercenary/watcher）

### 2.4 数值修正（2 张，均为明显缺陷）

| 卡 | 变更 | 依据 |
|---|---|---|
| `sea_leviathan_young` | 5/6 → **4/6** | 审核实测净值 **+4.26**（全池最超模） |
| `sea_devour` | punish 4 → **2** | 审核实测净值 **−6.71**（全池最亏）；同类反制/干扰伏击定价为 P1-P2（sea_ink P1 / machine_null P2 / flame_ambush_seal P2），P4 明显错误 |

> 深海"收口"说明：弃牌轴数值/文本已完整（siren/tide/depths/abyss_call/warden 全部落地 + 文本），**"印记"语义与阈值冲突属规则层，未改动**（见 §4 R2/R3）。

---

## 3. 校验结果（我实测，最终态）

| 校验 | 结果 |
|---|---|
| cards schema | **91/91 pass** |
| decks | **4/4 pass（91 cards）** |
| Java 回归 | **38/38 pass** |
| .NET 测试项目构建 | 成功（0 warning / 0 error） |
| **引擎级验证（双场景）** | **古木 512 可达 ✓ + 机械 6 次下载可达 ✓**（见 §2.5） |
| .NET 全量测试 | ⚠️ 本会话沙箱禁止 testhost 启动，**需 Codex/QA 在可运行环境重跑**（数据改动可能让写死旧值的测试失效） |

### 2.5 引擎级验证证据（真实 C# 引擎，走完整动作管线；程序 `build-output/pl-verify/`）

**场景 1｜古木 512 轴**：
```
载体 wood_guard 起始 1/1，疯长=3（×8）
回合1 sapling root=2 | wisp root=4 | seed root=4 | druid 载体=65   ← (2+6)×8=64
回合1 bear    载体=145                              ← (2+8)×8=80
回合3 treant  载体=241 | stag 353 | owl 481 | warden 625  win.giant_health_ge
→ 9 张卡 / 3 回合达标 ✓
```

**场景 2｜机械 6 次下载轴**（验证 M1 落地生效）：
```
提交 drone(1) golem(2) wall(2) blaster(3) titan(3) spark(1) assembler(1) recycler(1)  ← commitCost 差异化生效
下载 assembler(1) spark(1) titan(2) blaster(1) wall(1) golem(1)                        ← downloadCost 生效
载体 3/4 → 9/10（每次 +1/+1）→ PullCount=6 → win.pull_total_ge ✓
```
> 附带印证：recycler 提交后提交队列 7→7（先入队再被 ROLLBACK 回滚 1 张）→ **M1 的 commitEffects 时点效果在真实引擎中生效**。

**已知可能受影响的测试**（建议 Codex 优先跑）：
- `ProductionFactionIntegrationTests`（用 3 张 wood_growth 走 512；我核算 growth 新参数下仍为 (2+0)×8×3=48 → 464+48=512 ✓ 应通过）
- `DataLoaderTests`（硬断言 sea winParam=18 —— 我未改该值 ✓；但 `sea_devour` punish 4→2 若被断言需同步）
- Unity 侧机械 fixture 测试（machine commit 值变化：golem/wall 1→2、blaster/titan 1→3）

---

## 4. 规则优化建议（**未落地，等你拍板**）

### 4.1 我在审核中发现、但**没有擅自改**的规则问题

| # | 问题 | 证据 | 我的建议 |
|---|---|---|---|
| R1 | **同 tag 卡每回合互斥**：`CardPlayRules.TagsFree` 让带同一 tag 的卡每回合只能出一张（回合结束 `TurnFlow.CompleteTurn` 清空 UsedTags） | CardPlayRules.cs:58-67 | 观察：古木 6 组 tag 重复（树灵×2/野兽×2/精灵×2/恢复×2/荆棘×2/结界×2）→ 同回合无法连出。若这是有意设计（防同族堆叠）则保留；若想允许连出，需改引擎（规则改动） |
| R2 | **深海阈值三源冲突**：data=18 / BALANCE.md L25"15→12" / 设计源=12；`DataLoaderTests` 硬断言 18 | 三处文件 | 建议冻结一个值（我倾向 18=data 现值，因海模拟胜率已偏高），同步 BALANCE/设计源/测试 |
| R3 | **"印记"语义未定义**：RULES 无定义，提案建议作"弃牌胜利计数可见化别名"，但计数口径矛盾（海源弃牌 vs 全部受效果弃牌） | RULES §12.3 / 引擎 TotalDiscarded | 需你定口径；若采纳"全部受效果弃牌 + 别名"，零引擎成本 |
| R4 | **machine_leader 地标 tier1 零效果**：tier1 只有文本"首次PULL推进地标层数"，无 effectSpecs，对局中不产生实际效果（tier2 的 chant→alpha 正常） | EffectRuntime.Mechanical tier 处理 | 可补 tier1 一个小效果（如"首次下载后抽1"）或删该层 |
| R5 | **Java 引擎不支持古木机制**：`src/main/java` 全部无 扎根/疯长/512 逻辑 → Java 模拟（SimMain）不反映真实古木强度，其胜率数字对古木无效 | grep 零命中 | Java 仅作历史库存 ✓ 当前状态合理；但**平衡模拟需以 C# 为准**，建议增加 C# 侧模拟入口 |
| R6 | 全池 PUNISH 仅 4 张（4.4%），设计文档甜点区建议 20-30% | 逐卡统计 | 属于内容扩充决策，需你定是否扩池 |

### 4.2 平衡现状（子代理实测 480 局，Java 引擎，不含木机制）

| 卡组 | 胜率 |
|---|---|
| 烈焰 | 76.3% |
| 深海 | 63.8% |
| 古木 | 41.3%（**不含扎根机制**，我本次落地后需重测） |
| 机械 | 18.8% |

极差 57.5pp，超出 BALANCE 自述的 40-60% 带。我本次的机械/古木改动方向是提升两者，但**需要重跑平衡模拟确认**（C# 侧更准）。

---

## 5. 剩余待办（建议路由）

1. **Codex**：在可运行环境重跑 .NET 全量测试；同步因数据变更失效的测试断言（清单见 §3）；如需 M1 原案 upload 值请先取得 owner 确认
2. **QA（DeepSeek）**：按 9/8 提案 §6 验收清单复验机械 M1；重跑平衡模拟（古木/机械）
3. **owner**：拍板 §4 的 R1-R6 规则议题

---

## 6. 附：本次落地用到的引擎机制（供后续设计参考，避免踩坑）

1. **层数供给走动作，不走 tag**：`ADD_ROOT`/`ADD_RAMPANT` 是效果动作（可多张重复），而 tag 有每回合唯一限制
2. **增幅公式**：`effective = (BUFF amount + RootStacks) × 2^min(3, RampantStacks)`，古木来源的 BUFF 自动吃增幅（`woodSource` 判定）
3. **增幅即封印**：只要增幅发生（effective ≠ 原值）→ 目标 `Sealed=true, Attack=0, Shield=false`，失去全部关键词（含嘲讽/圣盾）
4. **吟唱卡**：`onPlayEffects` 不结算，必须写进 `chantEffects`（wood_seed 踩过）
5. **关键词必须写在 `keywords` 字段**（判定用 `HasKeyword`），写在 `tags` 里无效
6. **P=0 卡要克制**：免费卡若附带大幅强化（吃 ×8 增幅）会超模，本次 P=0 的 sapling/wisp 只给层数不给强化

---

## 7. 对抗审核的纠正记录（重要）

卡池对抗子代理的**头号建议**是"给 6 张随从加 `"tags": ["扎根"]`，引擎即刻生效、零代码改动"。

**PL 实测证明该建议会导致设计失效**：`CardPlayRules.TagsFree`（CardPlayRules.cs:58-67）对 `player.UsedTags` 做检查，带同一 tag 的卡**每回合只能出一张**；给 10 张卡都打 `扎根` tag 后，只有第一张能打出，其余全部被拒（`action.tag_already_used`）。PL 首版按此落地 → 引擎验证失败（仅 1 张生效）。

**修正**：改用 `ADD_ROOT` 效果动作（不受 tag 唯一性限制）→ 引擎验证通过（625 血 + `win.giant_health_ge`）。

> 教训：读代码得出的机制结论必须经引擎实测确认。tag 是"每回合唯一的能力标识"，不是"卡牌类别"。

---

## 8. 剩余平衡问题清单（子代理逐卡审计，我仅改了 1 张，其余**待你决定**）

> 数据源：91 张逐卡净值重算（Budget_A(P)=4.55+0.25P+0.16P²，容忍带 [−1.5,+1.5]）。**未落地**，属平衡基调调整，等你拍板。

### 8.1 超模（> +2.8，建议下调）

| 卡 | 净值 | 建议 |
|---|---|---|
| sea_leviathan_young | +4.26 | **已落地** 5/6→4/6 |
| flame_berserker | +3.64 | 惩罚降临 2 伤→1 伤 |
| flame_strike | +3.26 | 5 伤→4 伤 |
| flame_giant | +3.20 | 7/7→6/7 或去嘲讽 |
| machine_recycler | +2.88 | 去 pullEffects 或惩罚降临改 0 费 |

### 8.2 中度超模（+1.6~+2.3）
sea_priest +2.86 / wood_moon +2.31 / wood_guard +2.31 / wood_druid +2.31 / wood_bear +1.89 / sea_kraken +1.71 / machine_assembler +1.71 / wood_owl +1.59

### 8.3 亏模（< −3.1，建议上调或降 P）
sea_devour **−6.71**（全池最亏）/ wood_punish_wrath −4.90 / sea_punish_tsunami −4.46 / machine_factory −4.24 / wood_seed −4.13 / flame_forge −4.02 / machine_virus −3.69 / flame_sacrifice −3.66 / machine_punish_core −3.40 / flame_punish_wrath −3.36 / wood_veil −3.24 / wood_spring −3.19 / sea_barrier −3.14 / machine_shield_gen −3.14 / wood_circle −3.11

### 8.4 结构性问题
- **4 张 PUNISH 系统性亏模**（−3.36~−4.90）：打印惩罚值 5/5/5/6 过高，建议降到 3/4/3/4
- **PUNISH 仅 4 张（4.4%）**：设计文档甜点区建议 20-30%
- **中立 6 张中 4 张从不被编入任何 deck**（mage/mercenary/supply/watcher）→ 形同虚设
- **类型冗余**：machine_scan ≡ sea_current（P1 抽2）；machine_recharge ≡ flame_double（恢复攻击）；flame_ambush_seal ≡ machine_null（反制咒文）
- **烈焰 deck 惩罚值 138** 超文档上限 110；**0 张 DAMAGE_CASTLE 卡**（破城胜利轴无卡支撑）
- **深海阵营身份丢失**：设计源里 crab/sink/tide/eel/kraken 都是"潮蚀/潮位"机制，正式池改成通用随从

### 8.5 平衡实测（480 局，Java 引擎，**不含木机制**）
flame 76.3% / sea 63.8% / wood 41.3% / machine 18.8%；平均 24.9 回合。极差 57.5pp 远超 40-60% 目标带；`docs/BALANCE.md` 记载（56.3/52.1/50.0/41.7、15.4 回合）已失效。

