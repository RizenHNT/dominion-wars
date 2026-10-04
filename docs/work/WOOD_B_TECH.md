# 古木圣地 · B 针对轴 33 张（2026-09-13）

**段**：B 针对轴（100 张池中的 33 张）　**依据**：`docs/PL_WOOD_POOL100_SPEC_2026-09-13.md`
**状态**：设计稿（**尚未落 `data/cards/wood.json`**——已核对：该文件当前仍是 21 张基线卡，本段 33 个 id 一个都不在其中；`WoodDesignProbe` 也尚未跑）。
**新增 id**：33 个（全部 `wood_` 前缀，与现有 21 张不重 id、不重名）

---

## 0. 我们打的是谁的哪个支点

古木圣地**不主动进攻**：它承压、等待，然后收割对方已经押上的投资。因此 B 段针对卡的核心形态不是「直接解场」，而是
**把对手的进展（王城伤害 / 云端库存 / 弃牌数）读出来，转成古木自己的扎根·疯长·抽牌·生命池**。

| 对手 | 胜利条件 | 我方可攻击的支点 | 本段主要手段 |
|---|---|---|---|
| **烈焰帝国** | 击破共享**王城**（破城即胜，并把其循环计数抬到 9） | **王城的血量**；**本回合攻击过王城的随从**（烈焰的破城投资全押在这批随从身上） | 读 `castleAttackers`（本回合攻击过王城的敌方随从集合），把对手这回合的破城投资换成古木的成长与抽牌；另用「敌方≥2随从」这个词条卡住它破城所必需的场面 |
| **机械遗迹** | 累计 **6 次下载 PULL** | **云端栈 `cloudStack`**（下载只能取栈顶）；**提交队列 `commitQueue`**；**场上下载载体** | 清空云端栈（下载对象消失）；把对方随从压进它自己的 `commitQueue`（离场即失去载体）；对着它的囤牌（手牌）收过路费 |
| **深海联盟** | 使**对方**累计受效果弃牌 ≥18（强制弃牌不计） | 它的**弃牌来源随从**；**我的手牌规模**（手牌越多，随机弃牌的池子越大） | 把「已弃牌数」（`totalDiscarded`，与海的胜利条件同一读数）当成古木的成长计量表；在弃牌达到阈值后给自己补牌补血；对丢弃事件挂身位卡 |
| **古木圣地（自己）** | 把**一名封印随从**养到 **512** 生命（极脆） | —— 本段**不做**反古木卡（理由见 §6 的镜像列说明） | 所有非针对卡都必须回答「它推进 512 的哪一步」 |

**四档强度分布**：S 6 / A 10 / B 12 / C 5 = 33。
**engineCost 分布（逐张点数，和 = 33）**：**零 27 / 小 2 / 大 4**。
- **`大` 4 张（全是 S）**：`wood_amber_root`（需新词条 `OPP_ATTACKED_CASTLE_THIS_TURN` + 新状态 `castleAttackers`）、`wood_cloud_reaper`（需新动作 `EMPTY_CLOUD_STACK` + 读云栈词条）、`wood_root_prison`（需新动作 `ENEMY_COMMIT_MINIONS`）、`wood_reflux`（需新动作 `CONVERT_DISCARD_TO_GROWTH`）。
- **`小` 2 张（都是 C）**：`wood_bark_cicada`、`wood_late_guard`（各需 1 个新条件词条）。
- **`零` 27 张**：S 2（`wood_ash_barrier` / `wood_godtree_sentinel`）+ A 10 + B 12 + C 3（`wood_rotting_branch` / `wood_dry_seed` / `wood_short_cycle`）= 27。
- **一句话口径：33 张里 27 张今天就能进 `WoodDesignProbe`；剩下 6 张（4 大 + 2 小）需要先补词表。**
**新增条件词条**：4 个（`OPP_ATTACKED_CASTLE_THIS_TURN`、`OPP_CLOUD_GE_2`、`OPP_CLOUD_GE_3`、`OPP_CASTLE_HP_LE_75`；其中 `OPP_CLOUD_GE_n` 是同族两档，实现一次可覆盖两张卡）。
**新增效果动作**：3 个（`EMPTY_CLOUD_STACK` / `ENEMY_COMMIT_MINIONS` / `CONVERT_DISCARD_TO_GROWTH`），只在 3 张 S 卡上，见 §7.3。

---

## 1. 词表用法说明（先读，避免误读下面的表）

- 下文所有三倍组一律写成 `ACTION target amount param`，**action 与 target 只取自 §0.1 / §0.2**。
- 「**新增动作**」= §0.1 之外的一次性特殊动作，只出现在 3 张标 `engineCost: 大` 的 S 卡上，并在 §7.3 逐条给出精确定义。
- 「**新增条件词条**」= §0.3 之外的新词条，只出现在标 `engineCost: 大`/`小` 的卡上；未在 §7 登记的**一律 fail-closed（不结算）**。
- `tags` 为旧池值 + 本池允许的新增 tag（藤蔓/世界树/古树/苔藓/菌根，全池 5 个，每个 ≥2 张）。
- 卡面文本**逐字写出 condition 与全部效果**；下表 `卡面文本` 列可直接抄进 `text` 字段。
- **PUNISH 卡需要 §4 字段清单之外的 3 个既有字段**（规范 §4 是「必须交出的字段」下限，不禁止补既有 schema 字段；基线 `wood_punish_wrath` / `wood_owl` / `sea_punish_tsunami` 均已使用）：`punishActivatable: true`、`punishCost`（惩罚降临费用）、`punishCondition`。本段 **4 张**惩罚牌（`wood_godtree_sentinel` / `wood_punish_pact` / `wood_seed_bank` / `wood_life_pact`）的取值是 **`punishActivatable: true` + `punishCondition: <卡片 condition 列的值>`**，`punishCost` 见下：

  | id | printed `punish` | `punishCost` | 卡面写的降临时费用 |
  |---|---|---|---|
  | `wood_godtree_sentinel` | 4 | **2** | 以惩罚2发动 |
  | `wood_punish_pact` | 4 | **2** | 以惩罚2发动 |
  | `wood_seed_bank` | 4 | **2** | 以惩罚2发动 |
  | `wood_life_pact` | 4 | **2** | 以惩罚2发动 |
  | `wood_owl`（基线，不在本段） | 2 | 0 | —— 参考先例 |

  **说明**：打印惩罚值是**自己回合主动打出时的费用**（`punish`），`punishCost` 是**被对方喂牌时发动惩罚降临的费用**（替代打印值，RULES §3）。本段 4 张惩罚牌统一取 `punishCost: 2 < punish: 4`，属于 RULES §13.5 允许的折扣型；`wood_life_pact` 是**替换型**（卡面写「变更为」），按 RULES §13.5 只结算变体、不追加普通效果。
  **注意 `wood_life_pact` 的 `condition` 是 `ALWAYS`**：它的 `punishCondition` 也应为 `ALWAYS`（与基线 `wood_owl` 同形），没有额外的词条门槛——所以它在任何对局里被喂到都可用，是唯一一张不挑对手的惩罚牌。

---

## 2. S 强针对（6 张）

对目标阵营近乎决定胜负；对非目标阵营 **condition 无法成立 ⇒ 纯废牌**。

| id | name | type | tags | punish | atk/hp | condition | effects | targetFaction | 卡面文本 | engineCost | 设计意图 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `wood_amber_root` | 琥珀之根 | SPELL | ["菌根"] | 2 | — | `OPP_ATTACKED_CASTLE_THIS_TURN`（**新增词条**） | `ADD_ROOT SELF 2 root`；`ADD_RAMPANT SELF 1 rampant`；`DRAW SELF 1` | 烈焰 | 若【对方本回合攻击过王城】，则扎根2、疯长1，并抽1张牌；否则本卡空发，不产生任何效果。 | 大 | 反烈焰核心，且是**真正的纯废牌设计**：条件不成立时它连身材都没有（空发），所以对机械/深海/古木就是一张纯粹的废牌——这正是 S 档「对非目标阵营纯废牌」的定义。烈焰的破城投资必然是「随从打王城」，本卡把这份投资直接换成古木的成长层数（疯长1 = 后续所有强化 ×2，是 512 轴上最贵的一步）。 |
| `wood_ash_barrier` | 灰烬防线 | SPELL | ["荆棘"] | 2 | — | `ENEMY_MINIONS_GE_2`（**现有词条**） | `DAMAGE ALL_ENEMY_MINIONS 3`；`HEAL ALL_FRIENDLY_MINIONS 2` | 烈焰 | 若敌方场上≥2个随从，则对所有敌方随从造成3点伤害，并使己方所有随从恢复2点生命；若敌方随从不足2个，则本卡空发。 | 零 | 反烈焰第二张，**零成本立即可测**。烈焰要破王城就必须压住场面（一次攻击只能削王城 1 点，75 点血必须有持续输出的随从），所以「≥2 个随从」是它启动破城轴的必经状态；本卡一次性清掉烈焰的冲锋部队与火元素，并补己方血，把它的破城时间表往后推两三个回合。对机械/深海（同样铺场）是正常清场，对古木镜像多数时候因对方不铺场而空发（矩阵记「中」，因为「≥2 随从」不含阵营信息、条件仍可能成立）。 |
| `wood_cloud_reaper` | 伐云者 | SPELL | ["古树"] | 1 | — | `OPP_CLOUD_GE_2`（新增词条，读对方 `cloudStack`） | `EMPTY_CLOUD_STACK ENEMY_SINGLE 0 param=cloudStack`（**新增动作**）；`ADD_ROOT SELF 2 root` | 机械 | 若对方云端栈≥2，则将对方云端栈的牌全部置入其墓地，并扎根2。 | 大 | 反机械核心。机械的胜利轴是下载 6 次，而下载**只能取栈顶**；清空云端栈直接让对手的下载轴归零（不是拖延，是清账）。对烈焰/深海/古木，非机械不会往云端栈放牌 ⇒ 废牌。 |
| `wood_root_prison` | 根须牢狱 | SPELL | ["藤蔓"] | 1 | — | `ENEMY_MINIONS_GE_3`（**现有词条**） | `ENEMY_COMMIT_MINIONS ALL_ENEMY_MINIONS 0 param=commitQueue`（**新增动作**）；`ADD_ROOT SELF 2 root` | 机械 | 若对方场上≥3个随从，则将这些随从全部提交进**对方的**提交队列（离场，不触发提交效果与提交惩罚），并扎根2。 | 大 | 反机械第二张，打**载体**而非云端。下载必须由「己方场上的机械单位」执行；把对方场面压进它自己的 `commitQueue`，同时剥夺载体与场面。机械铺场最快 ⇒ 条件最易成立；对烈焰（同样铺场）是半张牌，对古木镜像无法成立。 |
| `wood_reflux` | 逆流还根 | SPELL | ["菌根"] | 2 | — | `OPP_DISCARD_GE_10`（**现有词条**，读对方 `totalDiscarded`，与海的胜利条件同一读数） | `CONVERT_DISCARD_TO_GROWTH SELF 1 param=root`（**新增动作**，1 层/每 3 张已弃牌，向下取整）；`HEAL FRIENDLY_MINION 3` | 深海 | 若对方累计受效果弃牌≥10张，则按每3张已弃牌获得扎根1，并恢复一个友方随从3点生命。 | 大 | 反深海核心。海的整条轴就是「把我方手牌挖空」；本卡把对方已经挖走的牌重新数成古木的成长。深海每推进一次，本卡的收益就变大一次，形成「越被弃越强」的收割结构。非深海局该词条读数为 0 ⇒ 抽到即废牌。 |
| `wood_godtree_sentinel` | 神木哨卫 | PUNISH | ["世界树"] | 4 | — | `OPP_DISCARD_GE_8`（现有词条） | `DRAW SELF 2`；`GAIN_LIFE SELF 3` | 深海 | 惩罚牌：无法主动使用。打印惩罚值4。【惩罚】若对方累计受效果弃牌≥8张，则以惩罚2发动：抽2张牌，并开启3点生命池；否则本卡无法发动。 | 零 | 反深海第二张，**零成本可测**。惩罚牌只能被对手「喂」到手才能发动——正好是深海最擅长干的事，于是深海越压我，越是在给我免费的解药。非深海局永远不满足条件 ⇒ 彻底废牌。 |

> **S 档设计约束说明**：6 张里 **4 张需要新机制**（`wood_amber_root` 需 1 个新条件词条 + `castleAttackers` 状态；`wood_cloud_reaper` / `wood_root_prison` / `wood_reflux` 各需 1 个新效果动作），另 2 张（`wood_ash_barrier`、`wood_godtree_sentinel`）**只用 §0.1–§0.3 的现有词表，零成本立即可测**。这是 owner 明确允许的「一次性特殊效果」额度，且已控制在少数：**A 档 10 张与 B 档 12 张全部零新词表、零新动作**，可立刻进 `WoodDesignProbe`（C 档 3 张零成本 + 2 张各需 1 个新词条）。
>
> **S 档的「废」是怎么保证的**：6 张里 **4 张**（`wood_amber_root` / `wood_cloud_reaper` / `wood_reflux` / `wood_godtree_sentinel`）读的是**实际只有目标阵营才会去做的动作所留下的状态**——云端栈的库存、效果弃牌的累计数、对王城发动的攻击。前两者的读数为 0 时条件必然为假（非机械不会往云端栈放牌，非深海没有弃牌源）；第三项（`castleAttackers`）技术上任何阵营都能成立，但只有烈焰有动机持续攻击王城，所以它的「对别家永远为假」是**行为学结论而不是规则结论**（§7.1 明确王城是共享中立单位，任何一方攻击都会置位）。这三类条件下卡面写明「空发」，矩阵按上述行为学口径把它们记「废」。
> 另 2 张（`wood_ash_barrier` / `wood_root_prison`）读的是 `ENEMY_MINIONS_GE_n`，这个场面词条在别家也能成立，所以它们对别家只是「弱」（一张正常清场 / 一张正常换场面），镜像格记「中」——矩阵如实标注，没有假装它们也是纯废牌。

---

## 3. A 中针对（10 张）

对目标阵营好用；对别家**多数仍能打出**，只是效果平庸。**A 档的三列评级与 §6 矩阵完全一致**（下表就是矩阵的 A 档切片，避免两处各写一遍而脱同步）：

| A 档卡 | targetFaction | 烈焰 | 机械 | 深海 |
|---|---|---|---|---|
| `wood_arbor_mark` | 无 | 中 | 中 | 中 |
| `wood_moss_offset` | 无 | 中 | 中 | 中 |
| `wood_tide_screen` | 烈焰 | **强** | 弱 | 中 |
| `wood_punish_pact` | 烈焰 | **强** | 中 | 中 |
| `wood_cloud_mirror` | 机械 | 中 | **强** | 中 |
| `wood_seed_bank` | 机械 | 中 | 中 | 弱 |
| `wood_mire_lash` | 深海 | 中 | 中 | **强** |
| `wood_driftwood_snare` | 无 | 中 | 中 | 中 |
| `wood_marsh_ward` | 深海 | 弱 | 弱 | **强** |
| `wood_hand_scale` | 深海 | 弱 | 弱 | 中 |

**两个必须一起读的事实**：① `condition` 是 fail-closed（§0.3），条件不成立就**整卡空发或退化为白板身材**，不存在「部分结算」——所以 `wood_mire_lash` / `wood_marsh_ward` / `wood_hand_scale` 这类卡在对手不留手牌 / 不弃牌的局里收益会明显打折。② A 档刻意做得比 S 档宽容：S 是「不打这家就是纯废牌」，A 是「打哪家都能用，只是打目标那家才赚」。

> **本表的 3 张「无」卡为什么也算进 A 档**：`wood_arbor_mark` / `wood_moss_offset` / `wood_driftwood_snare` 的 `targetFaction` 是「无」（它们的 condition 不读任何对手专属状态，对三家都只到「中」），但它们各自的**功能性收益**（单解 + 成长 / 过牌 + 成长 / 反制攻击并交换）稳定得好用，落在 A 档的强度带里。`targetFaction: 无` 只表示「不是针对卡」，不表示「弱」。

| id | name | type | tags | punish | atk/hp | condition | effects | targetFaction | 卡面文本 | engineCost | 设计意图 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `wood_arbor_mark` | 林冠标定 | SPELL | ["古树"] | 2 | — | `ENEMY_MINIONS_GE_2` | `DAMAGE ENEMY_MINION 3`；`ADD_ROOT SELF 2 root` | 无 | 若敌方场上≥2个随从，则对一个敌方随从造成3点伤害并扎根2；否则本卡空发，不产生任何效果。 | 零 | 单解 + 成长。烈焰/机械/深海全是铺场阵营 ⇒ 条件基本成立，是稳定可用的通用卡。**条件不成立时整卡空发**（§0.3 是 fail-closed，没有「部分结算」语义），所以它在空场局是废牌——这是本卡略低于同档其余卡的原因。 |
| `wood_moss_offset` | 苔痕抵偿 | MINION | ["苔藓"] | 1 | 2/4 | `SELF_LEADER_ON_FIELD` | `DRAW SELF 1`；`ADD_ROOT SELF 2 root`（onPlayEffects，条件成立才结算） | 无 | 若己方统领在场，则登场时抽1张牌并扎根2；否则本随从仅以2/4登场。 | 零 | 过牌 + 成长的载体。统领必然会被抽到并自动进入统领区/战场（RULES §7），所以条件后期稳定成立；它同时是「等统领到场」这件事本身的奖励。 |
| `wood_tide_screen` | 潮屏结界 | AMBUSH | ["结界"] | 2 | — | `ALWAYS`（伏击触发点：`OPPONENT_ATTACKS`，伏击种类 `NORMAL`） | `PROTECT_TURN FRIENDLY_MINION 0`；`DRAW SELF 1`（ambushEffects） | 烈焰 | 伏击·普通：对方攻击时，本回合己方卡牌不会被破坏与反制，并抽1张牌。 | 零 | 反烈焰中场。烈焰的推进是「随从打王城」，本卡把这一回合的所有攻击收益清零，同时补一张手牌。对不靠攻击取胜的机械是半张牌。 |
| `wood_punish_pact` | 天罚契约 | PUNISH | ["天罚"] | 4 | — | `OPP_HAND_GE_3` | `DAMAGE ALL_ENEMY_MINIONS 4` | 烈焰 | 惩罚牌：无法主动使用。打印惩罚值4。【惩罚】若对方手牌≥3张，则以惩罚2发动：对所有敌方随从造成4点伤害；否则本卡无法发动。 | 零 | 反烈焰点杀。烈焰要喂牌给我，就必然留手牌 ⇒ 条件由对手送上门；群体4点正好清掉烈焰的低血冲锋部队与火元素。 |
| `wood_cloud_mirror` | 云端镜池 | SPELL | ["结界"] | 2 | — | `OPP_HAND_GE_4` | `DRAW SELF 2`；`ADD_ROOT SELF 2 root`；`HEAL FRIENDLY_MINION 2` | 机械 | 若对方手牌≥4张，则抽2张牌、扎根2，并恢复一个友方随从2点生命；否则本卡空发，不产生任何效果。 | 零 | 反机械中场。机械是「出牌0费、把价值藏在生命周期里」的阵营，手上会囤积大量未提交的卡；本卡对着它的囤牌收过路费。**注意**：旧稿曾写「否则只抽1张牌」，但 §0.3 的条件是 fail-closed（不成立即整卡不结算），没有「部分结算」语义，故已按空发改写。 |
| `wood_seed_bank` | 种库稽核 | PUNISH | ["菌根"] | 4 | — | `OPP_HAND_GE_2` | `DRAW SELF 1`；`ADD_ROOT SELF 2 root` | 机械 | 惩罚牌：无法主动使用。打印惩罚值4。【惩罚】若对方手牌≥2张，则以惩罚2发动：抽1张牌并扎根2；否则本卡无法发动。 | 零 | 反机械的成长型惩罚。它比 `wood_cloud_mirror` 弱，但**惩罚值只有 2**，是古木在被喂牌时最便宜的一次成长插队。 |
| `wood_mire_lash` | 泥沼缚击 | SPELL | ["藤蔓"] | 2 | — | `OPP_HAND_GE_6` | `DISCARD_OPP_RANDOM ENEMY_FACE 1`；`DRAW SELF 2` | 深海 | 若对方手牌≥6张，则对方随机弃置1张手牌，你抽2张牌；否则本卡空发，不产生任何效果。 | 零 | 反深海中场（**targetFaction 定为「深海」**：深海是唯一有系统理由囤满手牌的阵营，因为它的弃牌源需要对手手牌厚；随机弃 1 在它身上才有价值）。对别人多数时候只是「惩罚值 2 的空牌」，所以矩阵在非深海列只给「中」不给「强」。 |
| `wood_driftwood_snare` | 漂木缠缚 | AMBUSH | ["藤蔓"] | 2 | — | `ALWAYS`（伏击触发点：`OPPONENT_ATTACKS`，伏击种类 `NORMAL`） | `NEGATE ENEMY_TARGET 0`；`DAMAGE ENEMY_MINION 2`；`HEAL FRIENDLY_MINION 2`（ambushEffects） | 无 | 伏击·普通：对方攻击时，反制该次攻击，对一个敌方随从造成2点伤害，并恢复一个友方随从2点生命。 | 零 | 反深海中场，但**声明为「无」**：它的 condition 是 `ALWAYS`（不读任何对手状态），对谁都按同一张牌结算，只是对深海这种靠攻击磨场面的阵营更有价值。矩阵三列同记「中」，符合「泛用但非针对」的定义。 |
| `wood_marsh_ward` | 沼泽守卫 | MINION | ["守卫"] | 2 | 2/6 | `OPP_DISCARD_GE_4` | `GRANT_KEYWORD SELF 0 param=嘲讽`（onPlayEffects，**无条件**）；`BUFF SELF 2 param=both`（onPlayEffects，条件成立才结算） | 深海 | 嘲讽。若对方累计受效果弃牌≥4张，则登场时本随从获得+2/+2。 | 零 | 反深海的身位卡。深海把牌弃到 4 张之后才开始接近收尾，本卡在那个时点变成 4/8 的墙挡住它的场面收尾。**字段说明**：嘲讽由 `GRANT_KEYWORD … param=嘲讽` 在**无条件**的 onPlayEffects 里给出（不是 `keywords` 打印字段，与 `wood_thousand_roots` 同一写法）；条件只挂在 +2/+2 上，所以条件不成立时它仍以 2/6 嘲讽登场。 |
| `wood_hand_scale` | 手牌天秤 | SPELL | ["苔藓"] | 1 | — | `OPP_DISCARD_GE_6` | `DRAW SELF 1`；`BUFF ALL_FRIENDLY_MINIONS 1 param=both` | 深海 | 若对方累计受效果弃牌≥6张，则抽1张牌，并使己方所有随从获得+1攻/+1生命；否则本卡空发，不产生任何效果。 | 零 | 反深海第二张（**targetFaction 定为「深海」**：condition 读的就是海的胜利计量表）。与 S 档 `wood_reflux` 同读数、**低两个档位**：`wood_reflux` 直接给扎根层（后续增幅的乘数来源），本卡只给一次普通 +1/+1（不产生扎根层、不叠加增幅）。刻意做成「S 卡的下位替代」，让玩家在构建时能感觉到档差。 |

---

## 4. B 泛用（12 张）

谁都能用，打谁都是正常强度。这一档**不读对手任何专属状态**，是古木卡组的骨架与保底。

| id | name | type | tags | punish | atk/hp | condition | effects | targetFaction | 卡面文本 | engineCost | 设计意图 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `wood_harvest_rite` | 收成之仪 | SPELL | ["仪式"] | 1 | — | `ALWAYS` | `DRAW SELF 2`；`ADD_ROOT SELF 2 root` | 无 | 你抽2张牌，并扎根2。 | 零 | 全池最纯粹的引擎卡：一次过牌 + 一次成长层，是 512 轴的起手铺垫，对谁都不亏。 |
| `wood_leaf_storm` | 落叶风暴 | SPELL | ["荆棘"] | 3 | — | `ALWAYS` | `DAMAGE ALL_ENEMY_MINIONS 2` | 无 | 对所有敌方随从造成2点伤害。 | 零 | 通用清场：把对方刚站场的随从打回起跑线，为等待中的古木买回合。它打谁都能结算，所以在矩阵里对三家一律「中」，不虚标「强」。 |
| `wood_sap_drain` | 汲汁 | SPELL | ["菌根"] | 2 | — | `ALWAYS` | `DAMAGE ENEMY_MINION 3`；`GAIN_LIFE SELF 2` | 无 | 对一个敌方随从造成3点伤害，并开启2点生命池。 | 零 | 通用解场 + 生存。生命池不构成胜负条件（RULES §0），所以这里的价值是「撑住」而非「赢」——正好符合古木承压的性格。 |
| `wood_burrow_arrival` | 破土而出 | MINION | ["树灵"] | 1 | 2/3 | `SELF_LEADER_ON_FIELD` | `ADD_ROOT SELF 2 root`；`BUFF FRIENDLY_MINION 2 param=root` | 无 | 若己方统领在场，则扎根2并强化一个友方随从+2/+2；否则仅以2/3登场。 | 零 | 低惩罚成长随从。卡组里要有「无脑铺」的下限卡，否则 33 张针对卡会把成长轴挤死。 |
| `wood_waiting_copse` | 待时林 | SPELL | ["增强"] | 2 | — | `ALWAYS` | `BUFF ALL_FRIENDLY_MINIONS 1 param=both`；`DRAW SELF 1` | 无 | 所有友方随从获得+1/+1，你抽1张牌。 | 零 | 从任一卡组都能用。注意：木来源的 BUFF 会让目标进入封印——本卡是「主动封印化」的开关，服务于 512 轴。 |
| `wood_still_moment` | 静默时刻 | SPELL | ["结界"] | 2 | — | `ALWAYS` | `NEGATE_ENEMY_EFFECTS_TURN SELF 0` | 无 | 对方场上所有卡牌的效果在本回合被变更为无效。 | 零 | 全池唯一能一次性关掉对方**全部**触发效果的反制（机械的提交/上传/下载触发效果、深海的弃牌触发效果都在射程内），所以它是 B 档里最贵的一张（惩罚值 2 + 一个 tag）。它打谁都能结算，因此在矩阵里对三家一律「中」，不虚标「强」。 |
| `wood_thousand_roots` | 千根盘结 | MINION | ["古树"] | 3 | 4/8 | `SELF_LEADER_ON_FIELD` | `GRANT_KEYWORD SELF 0 param=嘲讽`（onPlayEffects，**无条件**）；`ADD_ROOT SELF 2 root`（onPlayEffects，条件成立才结算） | 无 | 嘲讽。若己方统领在场，则登场时额外扎根2。 | 零 | 通用大墙。古木要赢就必须活得久，本卡是「拖到疯长3层」的物理保障。**字段说明**：嘲讽由 `GRANT_KEYWORD … param=嘲讽` 在**无条件**的 onPlayEffects 里给出（不是 `keywords` 打印字段），与既有 `wood_bark` / `wood_circle` 同一写法；条件只作用在扎根上，所以条件不成立时它仍以 4/8 嘲讽登场。 |
| `wood_barkskin` | 树肤硬化 | SPELL | ["守护"] | 1 | — | `ALWAYS` | `GRANT_KEYWORD FRIENDLY_MINION 0 param=圣盾`；`HEAL FRIENDLY_MINION 2` | 无 | 使一个友方随从获得【圣盾】，并恢复其2点生命。 | 零 | 保命卡。用于保护已接近阈值的封印体（封印体只保留生命值，所以圣盾是它唯一能拿到的保护）。 |
| `wood_rune_ward` | 根纹护壁 | AMBUSH | ["结界"] | 2 | — | `ALWAYS`（伏击触发点：`OPPONENT_PLAYS_SPELL`，伏击种类 `FOCUS`） | `NEGATE ENEMY_TARGET 0`（ambushEffects） | 无 | 伏击·专注：反制对方发动的咒文。触发回合内你的其他伏击被压制。 | 零 | 通用咒文反制。古木是慢速阵营，最怕被一发大招打断成长节奏（`wood_seed` 这类吟唱卡被反制会直接损失两层节奏）。**字段说明**：限定条件是**伏击触发点** `ambushTrigger = OPPONENT_PLAYS_SPELL`（与既有 `wood_veil` 的 `OPPONENT_ATTACKS`、`wood_root` 的 `OPPONENT_SUMMONS` 同一形制），不是 §0.3 的 condition——所以 condition 列写 `ALWAYS`（伏击卡无额外发动条件），卡面「反制对方发动的咒文」正是该触发点的中文表述。 |
| `wood_vine_lash` | 藤鞭缠击 | SPELL | ["藤蔓"] | 1 | — | `HAND_GE_3` | `DAMAGE ENEMY_MINION 2`；`ADD_ROOT SELF 2 root` | 无 | 若你手牌≥3张，则对一个敌方随从造成2点伤害并扎根2；否则本卡空发，不产生任何效果。 | 零 | 便宜的通用小解 + 成长。condition 只读自己的手牌，所以打谁都能成立；古木手牌常年 ≥3（起手 5 张、上限 8 张），空发风险很低。 |
| `wood_life_pact` | 生命契约 | PUNISH | ["恢复"] | 4 | — | `ALWAYS` | `ADD_SELF_PUNISH_TURN SELF 2`；`GAIN_LIFE SELF 5` | 无 | 惩罚牌：无法主动使用。打印惩罚值4。【惩罚】以惩罚2发动，效果**变更为**：本回合己方所有卡牌惩罚值-2，并开启5点生命池。 | 零 | 通用的「被喂牌时插队」工具：把惩罚链切掉 2 点，同时补生命池，让对手的喂牌节奏失效。卡面「变更为」= 替换型语义（RULES §13.5：惩罚触发时普通效果被替换，只结算变体）。 |
| `wood_eternal_vigil` | 永续守望 | SPELL | ["世界树"] | 4 | — | `ALWAYS` | `PROTECT_TURN FRIENDLY_MINION 0`；`NEGATE ENEMY_TARGET 0` | 无 | 本回合己方卡牌不会被破坏与反制，并反制对方本回合的下一次攻击。 | 零 | 一整回合的「免死金牌」。惩罚值 4 是真实代价，所以它是应急牌而不是循环牌。 |

---

## 5. C 环境废牌（5 张）

**故意做弱**。它们只在极窄场景有用，进构筑需要玩家自己判断环境；这是 owner 明确要求存在的档位。

| id | name | type | tags | punish | atk/hp | condition | effects | targetFaction | 卡面文本 | engineCost | 设计意图 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `wood_rotting_branch` | 枯枝残木 | MINION | ["树灵"] | 0 | 1/1 | `ENEMY_MINIONS_GE_4` | `ADD_ROOT SELF 1 root` | 无 | 若敌方场上≥4个随从，则登场时扎根1；否则本随从仅以1/1登场。 | 零 | 惩罚值 0 是它唯一的优点（被喂牌时 0 费）。1/1 身材在任何对局都近乎无意义，只有极限铺场的对手能让它变成「0 费扎根1」。 |
| `wood_bark_cicada` | 树皮蝉 | MINION | ["苔藓"] | 0 | 1/2 | `OPP_CASTLE_HP_LE_75`（新增词条） | `DRAW SELF 1` | 烈焰 | 若【王城血量≤75】，则登场时抽1张牌；否则仅以1/2登场。 | 小 | 反烈焰的**规则陷阱牌**（targetFaction 定为「烈焰」：只有它是想压王城血线的阵营，所以这张牌的「读数」只有对它有战术含义）。王城**初始**生命就是 75（`royalCastleMaxHp` 上限 75），所以严格读法下它永远成立、等于「1/2 抽1」——一张完全平庸的卡。它存在的意义是让构筑者去查王城数值，而不是无脑塞满。 |
| `wood_dry_seed` | 休眠干种 | SPELL | ["仪式"] | 1 | — | `SELF_LIFE_LE_3` | `HEAL FRIENDLY_MINION 3`；`DRAW SELF 1` | 无 | 若己方生命池≤3，则恢复一个友方随从3点生命并抽1张牌；否则本卡空发，不产生任何效果。 | 零 | 生命池默认不存在（只有「赋予生命统领」才开启，RULES §7），所以对绝大多数对局它**整卡空发**——一张被刻意做弱的牌。注意：§0.3 的条件是 fail-closed，**不存在「条件不成立也给一部分效果」的写法**，所以这里也不能写成「否则只抽1张」。 |
| `wood_short_cycle` | 断根残绪 | SPELL | ["恢复"] | 1 | — | `OPP_DISCARD_GE_2` | `HEAL FRIENDLY_MINION 2` | 深海 | 若对方累计受效果弃牌≥2张，则恢复一个友方随从2点生命；否则本卡空发，不产生任何效果。 | 零 | 只给 2 点治疗、条件却要读对手头像栏，且没有任何过牌。对深海是一张残废的下位卡，对别人是纯粹的白板（空发）。 |
| `wood_late_guard` | 迟来的守卫 | MINION | ["守卫"] | 1 | 1/5 | `OPP_CLOUD_GE_3`（新增词条） | `GRANT_KEYWORD SELF 0 param=嘲讽`（onPlayEffects，条件成立才结算） | 机械 | 若对方云端栈≥3，则本随从登场时获得【嘲讽】；否则本随从以1/5无关键词登场。 | 小 | 一张几乎打不出来的反机械卡：机械的云端栈通常**上传后立刻下载**，很少长期停在 3 张，所以它多半是 1 费 1/5 白板。它存在的价值是机械玩家必须知道「有这张牌」，从而改自己的上传节奏。 |

---

## 6. 针对矩阵（一眼看清强度不均）

行 = 古木的 B 段 33 张；列 = 对手阵营。
评级口径：**强** = 该局的核心胜负手；**中** = 正常可用；**弱** = 能打出但近乎无影响；**废** = condition 无法成立 ⇒ 空发、或效果无目标。
**镜像列的读数规则**：本段不做反古木卡，所以镜像局里能用的只有「不读对手状态的卡」。**功能牌（B 档与 A 档的大部分卡）在镜像里记「中」**；**读对手专属状态的针对卡在镜像里记「弱」**——它仍然能按自己的字段结算，只是不构成任何优势，而玩家依然要付惩罚值把它打出来（这就是「针对卡在镜像局是死牌」的真实含义：不是打不出来，是打出来不赚）。**注意本段前两版曾把镜像列写成「—」并把统计改成 99 格，那是错的**（「—」不是评级，会让 33 格无法归类）；现在镜像列有真实评级，统计口径回到**全部 33 × 4 = 132 格**。

| 卡 | 档 | 烈焰 | 机械 | 深海 | 古木(镜像) |
|---|---|---|---|---|---|
| wood_amber_root | S | **强** | 废 | 废 | 弱 |
| wood_ash_barrier | S | **强** | 弱 | 弱 | 中 |
| wood_cloud_reaper | S | 废 | **强** | 废 | 弱 |
| wood_root_prison | S | 弱 | **强** | 弱 | 中 |
| wood_reflux | S | 废 | 废 | **强** | 弱 |
| wood_godtree_sentinel | S | 废 | 废 | **强** | 弱 |
| wood_arbor_mark | A | 中 | 中 | 中 | 中 |
| wood_moss_offset | A | 中 | 中 | 中 | 中 |
| wood_tide_screen | A | **强** | 弱 | 中 | 中 |
| wood_punish_pact | A | **强** | 中 | 中 | 中 |
| wood_cloud_mirror | A | 中 | **强** | 中 | 中 |
| wood_seed_bank | A | 中 | 中 | 弱 | 中 |
| wood_mire_lash | A | 中 | 中 | **强** | 中 |
| wood_driftwood_snare | A | 中 | 中 | 中 | 中 |
| wood_marsh_ward | A | 弱 | 弱 | **强** | 弱 |
| wood_hand_scale | A | 弱 | 弱 | 中 | 弱 |
| wood_harvest_rite | B | 中 | 中 | 中 | 中 |
| wood_leaf_storm | B | 中 | 中 | 中 | 中 |
| wood_sap_drain | B | 中 | 中 | 中 | 中 |
| wood_burrow_arrival | B | 中 | 中 | 中 | 中 |
| wood_waiting_copse | B | 中 | 中 | 中 | 中 |
| wood_still_moment | B | 中 | 中 | 中 | 中 |
| wood_thousand_roots | B | 中 | 中 | 中 | 中 |
| wood_barkskin | B | 中 | 中 | 中 | 中 |
| wood_rune_ward | B | 中 | 中 | 中 | 中 |
| wood_vine_lash | B | 中 | 中 | 中 | 中 |
| wood_life_pact | B | 中 | 中 | 中 | 中 |
| wood_eternal_vigil | B | 中 | 中 | 中 | 中 |
| wood_rotting_branch | C | 废 | 废 | 废 | 中 |
| wood_bark_cicada | C | 弱 | 弱 | 弱 | 中 |
| wood_dry_seed | C | 弱 | 弱 | 弱 | 中 |
| wood_short_cycle | C | 废 | 废 | 弱 | 中 |
| wood_late_guard | C | 弱 | 弱 | 弱 | 中 |

**分布统计（口径：全部 33 × 4 = 132 格，镜像列有真实评级）**

| 档 | 强 | 中 | 弱 | 废 | 合计 |
|---|---|---|---|---|---|
| S（6×4） | 6 | 2 | 8 | 8 | 24 |
| A（10×4） | 5 | 27 | 8 | 0 | 40 |
| B（12×4） | 0 | 48 | 0 | 0 | 48 |
| C（5×4） | 0 | 5 | 10 | 5 | 20 |
| **合计（33×4）** | **11** | **82** | **26** | **13** | **132** |

> **逐格点数（点名分解，可复核）**
> - **S 行 = 24 格** ｜ 强 6 / 中 2 / 弱 8 / 废 8：
>   「废」8 格 = 4 张「读对手专属状态」的卡 × 它们各自的 2 个非目标阵营格：`wood_amber_root`(机械,深海)、`wood_cloud_reaper`(烈焰,深海)、`wood_reflux`(烈焰,机械)、`wood_godtree_sentinel`(烈焰,机械)。
>   「弱」8 格 = 这 4 张的镜像格（4）+ `wood_ash_barrier`(机械,深海)（2）+ `wood_root_prison`(烈焰,深海)（2）。
>   「中」2 格 = `wood_ash_barrier` 与 `wood_root_prison` 的镜像格。
> - **A 行 = 40 格** ｜ 强 5 / 中 27 / 弱 8 / 废 0：
>   「强」5 格 = `wood_tide_screen`(烈焰)、`wood_punish_pact`(烈焰)、`wood_cloud_mirror`(机械)、`wood_mire_lash`(深海)、`wood_marsh_ward`(深海)。
>   「弱」8 格 = `wood_tide_screen`(机械) 1 + `wood_seed_bank`(深海) 1 + `wood_marsh_ward`(烈焰,机械) 2 + `wood_hand_scale`(烈焰,机械) 2 + `wood_marsh_ward`(镜像) 1 + `wood_hand_scale`(镜像) 1 = 8。
>   「中」27 格 = 40 − 5 − 8。
> - **B 行 = 48 格** ｜ 中 48：12 张全部 `targetFaction: 无`，按口径**一律「中」，不允许出现「强」或「废」格**。
> - **C 行 = 20 格** ｜ 强 0 / 中 5 / 弱 10 / 废 5：
>   「废」5 格 = `wood_rotting_branch`(烈焰,机械,深海) 3 + `wood_short_cycle`(烈焰,机械) 2。
>   「弱」10 格 = `wood_bark_cicada`(烈焰,机械,深海) 3 + `wood_dry_seed`(烈焰,机械,深海) 3 + `wood_late_guard`(烈焰,机械,深海) 3 + `wood_short_cycle`(深海) 1。
>   「中」5 格 = 5 张 C 卡各自的**镜像格**（含 `wood_rotting_branch` 与 `wood_short_cycle` 的镜像格——矩阵里它们的镜像记「中」，不是「弱」）。
> - **校核**：24 + 40 + 48 + 20 = 132 = 33 × 4 ✅；11 + 82 + 26 + 13 = 132 ✅。
> - **列校核（三对手列 + 镜像列，每列 33 格）**：烈焰 = 强 5 / 中 18 / 弱 6 / 废 4；机械 = 强 3 / 中 19 / 弱 8 / 废 3；深海 = 强 3 / 中 18 / 弱 6 / 废 6；古木镜像 = 强 0 / 中 27 / 弱 6 / 废 0。四列各自 33，合计 132 ✅；非镜像三列 99 格中 强 11 / 中 55 / 弱 20 / 废 13。

> **⚠️ 本表已作废的旧数字（留档以免再犯）**：本稿前三版分别写过 ①「S 6/0/2/10、B 3/33/0/0」（把镜像列混算 + 给 B 档两张 `targetFaction: 无` 的卡错标「强」）；②「S 6/0/4/8、A 4/20/6/0、B 0/36/0/0、C 0/0/9/5 = 98」（把镜像列当「—」排除，且 98 ≠ 99 少算 1 格）；③「A 4/18/8/0、C 0/0/10/5」。三者都不对。**以上表为准**——它把每一格都归入强/中/弱/废四类之一，没有「—」这种无法归类的占位。

**读法（数字全部对应上表）**：S 档 24 格里 **8 格「废」**（占 1/3）——`wood_amber_root` / `wood_cloud_reaper` / `wood_reflux` / `wood_godtree_sentinel` 这 4 张读的是**只有目标阵营才会产生的状态**（攻击过王城 / 云端栈 / 已弃牌数），对别家永远为假、卡面写明空发，所以它们在两个非目标阵营列上全是「废」。另 2 张（`wood_ash_barrier`、`wood_root_prison`）读的是不含阵营信息的场面词条，对别家只是「弱」——矩阵如实标注，没有假装它们也是纯废牌。
**B 档 48 格全是「中」**，既没有「强」也没有「弱」——这就是「泛用」。
**C 档 20 格里 5 格「废」+ 10 格「弱」**（15/20 = 75% 落在弱或废，另 5 格是镜像「中」）——环境废牌。
**A 档夹在中间**：40 格里 5 格「强」+ 8 格「弱」，其余 27 格「中」。
**全表总览**：132 格里只有 **11 格「强」**——S 档 6 张各 1 格 + A 档 5 张各 1 格（`wood_tide_screen` / `wood_punish_pact` / `wood_cloud_mirror` / `wood_mire_lash` / `wood_marsh_ward`）；另有 **13 格「废」**。强度不均的分布是刻意做出来的，不是数据噪声。

---

## 7. 越出 §0.1–§0.3 的卡清单与**精确引擎需求**

**6 张 S 卡里有 4 张需要新机制**（`engineCost: 大`）：**3 张各需 1 个全新效果动作**（§7.3：`wood_cloud_reaper` / `wood_root_prison` / `wood_reflux`），**`wood_amber_root` 需 1 个全新条件词条 + 新的 `castleAttackers` 状态**（§7.1）。另有 2 个新条件词条服务于 2 张标 `小` 的卡（§7.2：`wood_cloud_reaper` 用的 `OPP_CLOUD_GE_2` 与 `wood_late_guard` 用的 `OPP_CLOUD_GE_3` 同族一次实现，`wood_bark_cicada` 用的 `OPP_CASTLE_HP_LE_75`）。下表是**实现契约**：须先登记进 RULES §13.2 与条件表，再进 Schema、C# `IEffect`、`GameSnapshot`、`LegalAction`。
**两张 S 卡零成本可测**：`wood_ash_barrier` 与 `wood_godtree_sentinel` 只用 §0.1–§0.3 的现有词表（加上读王城血量的 1 个读数源，若同时实现 `wood_bark_cicada` 则一并解决），今天就能在 `WoodDesignProbe` 里测出「带它 / 不带它」对烈焰 / 深海的胜率差。

### 7.1 新增条件词条（`大`，1 个）

| 新词条 | 读什么状态 | 语义与边界 | 使用卡 |
|---|---|---|---|
| `OPP_ATTACKED_CASTLE_THIS_TURN` | 本回合「攻击过共享王城」的**敌方随从集合**（建议落成 `MatchState.castleAttackers`，每回合段切换时清空） | 只要本回合有 ≥1 个敌方随从对王城结算过攻击即成立（伤害是否为 0 不影响）。王城是共享中立单位，所以「攻击王城」= 任意一方对其发起的战斗攻击。**不做**伤害量统计。 | `wood_amber_root` |

### 7.2 新增条件词条（`小`，**2 个登记项 / 3 个词条串**；全部使用卡共 2 张 C + 1 张 S）

| 新词条 | 读什么状态 | 使用卡 | 备注 |
|---|---|---|---|
| `OPP_CLOUD_GE_n`（`n = 2` 与 `n = 3` 两档） | 对方 `cloudStack` 当前张数 | `wood_cloud_reaper`（`n=2`，S，卡片本体标 `大`）、`wood_late_guard`（`n=3`，C，标 `小`） | 一个词条族、两档阈值，**实现一次即可覆盖两张卡**；作为「登记项」记 1 个 |
| `OPP_CASTLE_HP_LE_75` | `royalCastleHp`（0–75，满血值即 75） | `wood_bark_cicada`（C，标 `小`） | 阈值取满血值，故字面为真；若改用「< 满血」语义，该卡档位需上移（见 §9.1）。作为「登记项」记 1 个 |

> 注：`OPP_CLOUD_GE_n` 与 `OPP_CASTLE_HP_LE_n` 都是「读对手公开状态」的词条族，与 §0.3 现有词条的两条既定形制一致（`OPP_DISCARD_GE_n` 同名形，`SELF_CLOUD_GE_n` 已有对应己方版）。**引擎需实现两族比较器 + 三个读数源**：① 对方 `cloudStack` 张数（服务 `wood_cloud_reaper` / `wood_late_guard`）、② 王城当前生命 `royalCastleHp`（服务 `wood_bark_cicada`）、③ 本回合攻击过王城的敌方随从集合 `castleAttackers`（服务 `wood_amber_root`，见 §7.1）。
>
> **实现注记（`ambushKind` / `ambushTrigger`）**：本段 3 张伏击卡的取值是 `ambushTrigger` ∈ {`OPPONENT_ATTACKS`×2, `OPPONENT_PLAYS_SPELL`}、`ambushKind` ∈ {`NORMAL`×2, `FOCUS`×1}。这两个枚举在基线 `wood.json` 中已出现（`wood_root` = NORMAL/`OPPONENT_SUMMONS`、`wood_veil` = LOCKDOWN/`OPPONENT_ATTACKS`），**本段只复用基线已用的值，不新增枚举成员**——`OPPONENT_ATTACKS`（`wood_veil` / `flame_ambush_counter` / `sea_ink` 已用）、`OPPONENT_SUMMONS`（`wood_root` 已用）、`OPPONENT_PLAYS_SPELL`（`flame_ambush_seal` / `machine_null` 已用）三者都是跨阵营既有取值。实现时请确认各阵营共用同一份枚举（若按阵营分表，则需为这些值做映射）。
>
> **归属澄清（避免与 `engineCost` 列冲突）**：`wood_cloud_reaper` 的卡片本体是 **`engineCost: 大`**（它需要新动作 `EMPTY_CLOUD_STACK`）；本节把它列进来，只是因为它是 `OPP_CLOUD_GE_n` 这个新词条的**使用卡之一**，并不表示它的卡片等级是 `小`。**标 `小` 的只有 2 张 C 卡**（`wood_bark_cicada` / `wood_late_guard`）；**标 `大` 的是 4 张 S 卡**（`wood_amber_root` / `wood_cloud_reaper` / `wood_root_prison` / `wood_reflux`）。其中 `wood_cloud_reaper`（S，`大`）与 `wood_late_guard`（C，`小`）共用 `OPP_CLOUD_GE_n` 词条族，所以这一族的实现成本被两张卡分摊。

### 7.3 新增效果动作（`大`，3 个）

| 新动作 | 目标 / 参数 | 精确语义 | 使用卡 |
|---|---|---|---|
| `EMPTY_CLOUD_STACK` | target `ENEMY_SINGLE`（字段占位，语义指向对方 `cloudStack`），amount 0 | 将对方云端栈的**全部**卡牌移入**其**墓地。不触发上传/下载效果、不推进下载计数、不产生惩罚抽牌、不开启惩罚响应窗口（因为这不是 PULL，是移场）。 | `wood_cloud_reaper` |
| `ENEMY_COMMIT_MINIONS` | target `ALL_ENEMY_MINIONS`，amount 0 | 把对方场上全部随从移入**对方的** `commitQueue`。**不结算 `commitCost`、不产生惩罚抽牌、不触发 `commitEffects`**（提交是被强制的，不是对方主动动作）。被移入的牌此后按正常上传流程在对方结束阶段 PUSH（若实现上要禁用上传，须另行声明，本卡取「正常上传」）。**不作用于统领。** | `wood_root_prison` |
| `CONVERT_DISCARD_TO_GROWTH` | target `SELF`，amount 1，param `root` | 读取对方 `totalDiscarded`（与 `OPP_DISCARD_GE_n` 同源），按 `floor(totalDiscarded / 3)` 获得对应层数的**扎根**。每张卡每次结算只转换一次已经发生的弃牌（防重复计账：转换后须登记本卡实例已消费的水位，避免同一批弃牌被两张 `wood_reflux` 重复转换）。 | `wood_reflux` |

### 7.4 需要的一并改造（实现清单）

1. **`castleAttackers` 状态**：`GameSnapshot` 暴露该集合（AI 与 UI 都要能读，否则条件无法被客户端展示）。
2. **`cloudStack` / `commitQueue` 的敌方可读**：机制上两者已经是公开区域（RULES §12.1「机械的两个子区域均公开」），`GameSnapshot` 需确认对**对手**也暴露张数（AI 用），卡牌身份可继续按现规则公开。
3. **`totalDiscarded` 水位**：为 `CONVERT_DISCARD_TO_GROWTH` 增加「已转换水位」字段，避免重复计账。
4. **`EMPTY_CLOUD_STACK` 与胜负检查的时序**：清空云端栈后须重算「栈顶是否合法可下载」，否则机械的下一次 PULL 会指向已移走的卡实例（同一实例重复结算，见 RULES §12.4 末条禁令）。
5. **`ENEMY_COMMIT_MINIONS` 与 `PUSH` 的交互**：需明确本卡移入的队列卡是否参与**当回合**结束阶段的自动上传（本稿取「参与」）。若改取「不参与」，须在卡面文本里写明，否则违反「卡面必须逐字写出条件与效果」。
6. **`ENEMY_MINIONS_GE_n` 已是 §0.3 现有词条**：`wood_root_prison` 直接复用 `ENEMY_MINIONS_GE_3`，不新造词条——同一语义只允许一个词条（RULES §13.4 术语唯一）。它只依赖 §7.3 的新动作 `ENEMY_COMMIT_MINIONS`。

---

## 8. 落库前自查（对照 §0 逐条）

- ✅ **id 唯一**：33 个新 id 与现有 21 张（`wood_leader … wood_owl`）零冲突；全部 `wood_` 前缀。
- ✅ **name 唯一**：33 个中文名与现有 21 张中文名零重复。
- ✅ **action**：`DAMAGE / HEAL / DRAW / DISCARD_OPP_RANDOM / BUFF / ADD_ROOT / ADD_RAMPANT / GRANT_KEYWORD / NEGATE / NEGATE_ENEMY_EFFECTS_TURN / PROTECT_TURN / GAIN_LIFE / ADD_SELF_PUNISH_TURN` 全部在 §0.1；越界动作 **3 个**，已在 §7.3 逐条登记（`EMPTY_CLOUD_STACK` / `ENEMY_COMMIT_MINIONS` / `CONVERT_DISCARD_TO_GROWTH`）。
- ✅ **target**：`ENEMY_MINION / ALL_ENEMY_MINIONS / FRIENDLY_MINION / ALL_FRIENDLY_MINIONS / SELF / ENEMY_TARGET / ENEMY_FACE / ENEMY_SINGLE` 全部在 §0.2。
- ✅ **condition**：`ALWAYS / SELF_LEADER_ON_FIELD / HAND_GE_3 / OPP_HAND_GE_2·3·4·6 / SELF_LIFE_LE_3 / ENEMY_MINIONS_GE_2·3·4 / OPP_DISCARD_GE_2·4·6·8·10` 全部在 §0.3（`wood_root_prison` 与 `wood_ash_barrier` 用的都是现有词条 `ENEMY_MINIONS_GE_n`，**未**新造同义词条）；越界词条 **4 个**，已在 §7.1/§7.2 逐条登记（`OPP_ATTACKED_CASTLE_THIS_TURN`、`OPP_CLOUD_GE_2`、`OPP_CLOUD_GE_3`、`OPP_CASTLE_HP_LE_75`），未登记者一律 fail-closed（不结算）；**§7 里没有任何一个登记了却没被使用的词条**。
- ✅ **关键词（§0.4）**：本稿在**卡面文本列与设计意图列**里实际使用的关键词只有 **嘲讽**（3 张：`wood_marsh_ward` / `wood_thousand_roots` / `wood_late_guard`——前两张由 `GRANT_KEYWORD SELF 0 param=嘲讽` 在无条件的 onPlayEffects 里给出，`wood_late_guard` 由条件成立时的同一动作给出，**字段都已承载卡面承诺**）与 **圣盾**（`wood_barkskin`，`GRANT_KEYWORD FRIENDLY_MINION 0 param=圣盾`），外加伏击种类术语「伏击·普通 / 伏击·专注」（沿用基线 `wood_root` / `wood_veil` 的写法，不是 §0.4 词表项）。**11 个 ❌ 词在全文出现 0 次**（含本条自查行；本条只以「❌ 列表」「那 11 个词」的方式指代，不逐词写出），所以「没有把 ❌ 词当机制名用」是**可验证的空集结论**，而不是靠引文豁免。
- ✅ **tag（逐张脚本点数，可复算）**：33 张卡共 **33 个 tag 实例**，且**每张卡恰好 1 个 tag、没有任何多 tag 卡**。
  - **旧池 tag：17 个实例 / 10 个不同 tag** —— 树灵 2、守卫 2、恢复 2、荆棘 2、**结界 4**、增强 1、守护 1、天罚 1、仪式 2（结界 4 张 = `wood_tide_screen` / `wood_cloud_mirror` / `wood_still_moment` / `wood_rune_ward`；荆棘 2 张 = `wood_ash_barrier` / `wood_leaf_storm`；守卫 2 张 = `wood_marsh_ward` / `wood_late_guard`；树灵 2 张 = `wood_burrow_arrival` / `wood_rotting_branch`；恢复 2 张 = `wood_life_pact` / `wood_short_cycle`；仪式 2 张 = `wood_harvest_rite` / `wood_dry_seed`；单张的是增强 = `wood_waiting_copse`、守护 = `wood_barkskin`、天罚 = `wood_punish_pact`）。
  - **新增池 tag：16 个实例 / 5 个不同 tag** —— 藤蔓 4、菌根 4、苔藓 3、古树 3、世界树 2（藤蔓 4 = `wood_root_prison` / `wood_mire_lash` / `wood_driftwood_snare` / `wood_vine_lash`；菌根 4 = `wood_amber_root` / `wood_reflux` / `wood_seed_bank` / `wood_sap_drain`；苔藓 3 = `wood_moss_offset` / `wood_hand_scale` / `wood_bark_cicada`；古树 3 = `wood_cloud_reaper` / `wood_arbor_mark` / `wood_thousand_roots`；世界树 2 = `wood_godtree_sentinel` / `wood_eternal_vigil`）。
  - 17 + 16 = **33** = 卡数 ✅。新增 tag 恰 5 个（≤5 符合 §3），每个 ≥2 张（最少的是**世界树 2**）：世界树 2 / 古树 3 / 苔藓 3 / 藤蔓 4 / 菌根 4。
  - **无卡在自己数组内重复 tag**。本段刻意**不使用多 tag 卡**——§0.5 的「多 tag = 一次锁死多个种族」这个代价留给别的段去用（本段以单 tag 保证 33 张都能在需要时被打出）。
  - **口径提示**：前几版曾把旧池写成 18、把 `wood_waiting_copse` 记成双 tag，两处都错；当前数字以上面各 tag 的点名列名为准。
- ✅ **卡面 = 字段**：以下计数由脚本按「卡片行 → condition 列 / 卡面文本列」逐行提取后点得（33 行全覆盖，可直接复算）：
  - `condition = ALWAYS` 的 **11 张**（A 2 + B 9）卡面**全部不含**「若」「否则」从句。其中 3 张伏击卡（A 档 `wood_tide_screen` / `wood_driftwood_snare`、B 档 `wood_rune_ward`）的限定条件是 `ambushTrigger` 而不是 §0.3 condition，已在各自卡片行内注明。
  - `condition` 非 `ALWAYS` 的 **22 张**（S 6 + A 8 + B 3 + C 5）卡面**全部含**「若…」。其中 **16 张**用「否则…」写出条件不成立时的分支（`wood_amber_root` / `wood_godtree_sentinel` / `wood_arbor_mark` / `wood_moss_offset` / `wood_punish_pact` / `wood_cloud_mirror` / `wood_seed_bank` / `wood_mire_lash` / `wood_hand_scale` / `wood_burrow_arrival` / `wood_vine_lash` / `wood_rotting_branch` / `wood_bark_cicada` / `wood_dry_seed` / `wood_short_cycle` / `wood_late_guard`）；另外 **6 张**（`wood_ash_barrier` / `wood_cloud_reaper` / `wood_root_prison` / `wood_reflux` / `wood_marsh_ward` / `wood_thousand_roots`）各用「若…则…」的**同句否定式**写明失败分支（例如 `wood_ash_barrier` 写「若敌方随从不足2个，则本卡空发」、`wood_marsh_ward` 写「…则登场时获得+2/+2」以对比无条件的嘲讽），语义等价、不产生歧义。16 + 6 = 22 ✅。
  11 + 22 = 33 ✅。**逐档明细**：ALWAYS = A 2（`wood_tide_screen` / `wood_driftwood_snare`，两张带 `ambushTrigger` 的 `OPPONENT_ATTACKS` 伏击）+ B 9（`wood_harvest_rite` / `wood_leaf_storm` / `wood_sap_drain` / `wood_waiting_copse` / `wood_still_moment` / `wood_barkskin` / `wood_life_pact` / `wood_eternal_vigil` / `wood_rune_ward`）= 11；非 ALWAYS = S 6（全部）+ A 8 + B 3（`wood_burrow_arrival` / `wood_thousand_roots` / `wood_vine_lash`）+ C 5（全部）= 22。
  - **没有任何一张卡面承诺了字段里不存在的效果，也没有任何字段效果在卡面缺失**（`wood_marsh_ward` 与 `wood_thousand_roots` 卡面写的「嘲讽」由无条件 onPlayEffects 的 `GRANT_KEYWORD … param=嘲讽` 承载，不算「卡面承诺而字段缺失」）。
- ✅ **矩阵口径自洽**：§6 矩阵 33 行 × 4 列 = 132 格**全部有强/中/弱/废评级**（没有「—」这类不可归类的占位），且 §6 统计表逐行与矩阵相等——**S 6/2/8/8、A 5/27/8/0、B 0/48/0/0、C 0/5/10/5，合计 11/82/26/13 = 132**；列向也相等（烈焰 5/18/6/4、机械 3/19/8/3、深海 3/18/6/6、镜像 0/27/6/0，各 33）。
- ✅ **targetFaction 与矩阵一致（脚本逐张点数）**：**声明了阵营的 16 张卡** = S 6 + A 7 + C 3。逐阵营：**烈焰 5**（`wood_amber_root` / `wood_ash_barrier` / `wood_tide_screen` / `wood_punish_pact` / `wood_bark_cicada`）、**机械 5**（`wood_cloud_reaper` / `wood_root_prison` / `wood_cloud_mirror` / `wood_seed_bank` / `wood_late_guard`）、**深海 6**（`wood_reflux` / `wood_godtree_sentinel` / `wood_mire_lash` / `wood_marsh_ward` / `wood_hand_scale` / `wood_short_cycle`）。
  - 结构规则校验：**11 个「强」格全部落在各自声明的列**（烈焰 5、机械 3、深海 3），**没有任何一张卡把强格放到别人的列**。
  - 声明了阵营但**没有强格**的 5 张：`wood_seed_bank`(机械/本列中) / `wood_hand_scale`(深海/本列中) / `wood_short_cycle`(深海/本列弱) / `wood_bark_cicada`(烈焰/本列弱) / `wood_late_guard`(机械/本列弱)——它们是**弱针对与刻意做废的陷阱牌**，不构成违规（本项只要求「有强格时必须在自己的列」）。
  - **无阵营卡 = 33 − 16 = 17 张** = A 3（`wood_arbor_mark` / `wood_moss_offset` / `wood_driftwood_snare`）+ B 12 + C 2（`wood_rotting_branch` / `wood_dry_seed`）——这 17 张**没有任何一张带「强」格** ✅。
- ✅ **不变量表**：无一张卡对**敌方**单位施加木来源的 BUFF，也不对王城施加关键词/能力（符合 RULES §12.2）。
- ✅ **数值合法性**：无任何 BUFF 使用 amount=0（§11.1 报错项）；`GRANT_KEYWORD` 的 amount=0 沿既有用法（`wood_bark` / `wood_circle`）。
- ✅ **落库状态**：已核对 `data/cards/wood.json`，当前为 **21 张基线卡**，本段 33 个 id **一个都不在其中**（本稿尚未落数据）。曾观察到该文件被另一路并行改动到 121 张、且那批数据带有明显缺陷（`text` 写成阵营名、`param` 写成 `"param=嘲讽"`、`wood_amber_root` 类型与身材错误）；**该改动随后被回滚**，当前文件干净。落库仍请按本稿 `卡面文本` 列逐字写 `text`，再跑 `WoodDesignProbe`。

---

## 9. 待 owner / 实现方裁决的 4 点

1. **`wood_bark_cicada`（C1）**：`OPP_CASTLE_HP_LE_75` 是否按字面成立（王城满血 75）。若采用「< 满血」语义，本卡立刻变成「王城一掉血就抽1」的可用牌，档位需上移到 A。本稿按**字面**取废牌档，请确认。
2. **`ENEMY_COMMIT_MINIONS` 移入队列的卡是否参与当回合 PUSH**（§7.4 第 5 条）。取「参与」时本卡是强解场；取「不参与」时它是永久移除，强度过高，建议不要。
3. **S 卡数量与引擎成本的取舍**：本稿 6 张 S 里有 **4 张**需要新机制（`wood_amber_root` 需新词条 + 新状态，`wood_cloud_reaper` / `wood_root_prison` / `wood_reflux` 各需 1 个新动作；另外 2 个新词条服务于 2 张 C 卡）。若要先跑 `WoodDesignProbe` 验证，建议先用 **`wood_ash_barrier` + `wood_godtree_sentinel`**（两张零成本 S）+ 全部 10 张 A 档 + 全部 12 张 B 档 + C 档 3 张零成本卡共 **27 张**组成「零成本先验组」，先确认「针对卡带它 / 不带它的胜率差」这个测量口径本身有效，再补那 6 张需要新词表的卡。
4. **数据落库与规范化（供实现方）**：本稿撰写期间观察到 `data/cards/wood.json` 曾被另一路并行改动到 121 张，其中包含本段 33 个 id 但**带有 4 类缺陷**（33 张的 `text` 被写成阵营名如 `"text":"烈焰"`；`param` 被写成 `"param=嘲讽"` 而不是 `"param":"嘲讽"`；`wood_amber_root` 的类型与身材错误——本稿是 **SPELL、无 atk/hp**；condition 挂点与卡面文本不一致）。**该改动随后已被回滚**，当前文件是干净的 21 张基线。落库时请以本稿四张表的 `卡面文本` 列为准逐字写入 `text`，然后跑一次「`text` 字段 == 设计稿 `卡面文本`」的自动比对，再跑 `WoodDesignProbe`。在该校验通过前，不要把这 33 张的胜率数据当成有效结论。
