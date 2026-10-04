# 古木大卡池 100 张 · 设计规范（所有设计者共用）

**日期**：2026-09-13
**目标**：把古木圣地扩到 **100 张卡池**，涵盖 3 类轴（成长 / 针对 / 构筑分歧）× 4 档强度。
**产出**：`docs/PL_WOOD_POOL100_2026-09-13.md`（设计）+ 后续落成 `data/cards/wood.json`

---

## 0. 硬约束（违反即无法实现或两引擎不一致）

### 0.1 可用的 effect action（**只有这些**）
```
DAMAGE  HEAL  DRAW  OPP_DRAW  DISCARD_OPP_RANDOM  DISCARD_DRAWN  DESTROY
BUFF  ADD_ROOT  ADD_RAMPANT  GRANT_KEYWORD  SUMMON  SUMMON_LEADER
END_TURN  ADD_OPP_PUNISH_TURN  ADD_SELF_PUNISH_TURN  CONVERT_PUNISH_TO_DISCARD
PROTECT_TURN  NEGATE  NEGATE_ENEMY_EFFECTS_TURN  SKIP_RESHUFFLE  RESTORE_ATTACKS
GAIN_LIFE  LOSE_LIFE  DAMAGE_CASTLE  WIN_GAME  ROLLBACK  COMMIT  PUSH  PULL
```
**未在表中的动作 = 不可用**（写了会被 PULL 前置校验拒绝或静默跳过）。

### 0.2 可用的 effect target（**只有这些**）
```
ENEMY_TARGET  ENEMY_MINION  FRIENDLY_MINION  ALL_ENEMY_MINIONS  ALL_FRIENDLY_MINIONS
ALL_MINIONS  ENEMY_FACE  SELF  ANY_MINION  ENEMY_SINGLE  SINGLE_ENEMY
```
加上 `onPlayEffects` / `chantEffects` / `ambushEffects` / `punishEffects` 等挂点。

### 0.3 可用的发动条件（**只有这些**，双引擎已一致）
```
ALWAYS                SELF_LEADER_ON_FIELD      OPP_LEADER_ON_FIELD
ENEMY_MINIONS_GE_n    SELF_MINIONS_GE_n         HAND_GE_n
OPP_HAND_GE_n         OPP_HAND_LE_n             SELF_LIFE_LE_n
SELF_SEALED_GE_n      SELF_SEALED_HEALTH_GE_n   SELF_ROOT_GE_n
SELF_RAMPANT_GE_n     SELF_COMMIT_GE_n          SELF_CLOUD_GE_n
SELF_PULL_GE_n        OPP_DISCARD_GE_n          SELF_AMBUSH_GE_n
```
**未知词条 fail-closed**（不结算）。**不得发明新词条**（除非标为"需新引擎"）。

### 0.4 关键词：**只有 4 个有引擎行为**
```
✅ 嘲讽(TAUNT)  圣盾(DIVINE_SHIELD)  突袭(RUSH)  扰魔(SPELL_WARD)
❌ 有名字但无行为：降临 同归 献祭 复活 秒杀 震慑 沉默 占星 寄生 潜行 吸血
```
**用 ❌ 的关键词 = 该卡标为"需新引擎"，本次不可测。**

### 0.5 tag 规则（**不改规则**）
- **每回合每个 tag 只能用一次**；打出时消耗该卡的**全部** tag。
- `tags: []`（无 tag）⇒ **不受约束，可同回合反复打出**。
- 多 tag ⇒ 一次锁死多个种族（**限制换强度**）。
- 专属 tag（只此一卡使用）⇒ 等于无限制的**王牌**（全池 ≤3 张，见 RULES §13.5 超模容忍）。

### 0.6 成长的机制事实（古木的核心数学）
```
强化量 = (基础 + 扎根层) × 2^min(3, 疯长层)
- 疯长硬上限 3（×8）；扎根无上限
- 古木来源的 BUFF 对己方随从/自身/全体友方（growth target）自动吃增幅
- 一旦 growthApplied ⇒ 目标 sealed：攻击归0、失去全部关键词、不能攻击
- 治疗被 maxHealth 封顶，**不能靠治疗推轴**
- 阈值 512 在结束阶段检查，读「封印随从中最大当前生命」
```

---

## 1. 三段结构

| 段 | 数量 | 作用 | 强度 |
|---|---|---|---|
| **A 成长轴** | **34** | 保证卡组能赢、推进 512 | 稳定，不超模 |
| **B 针对轴** | **33** | 应对特定对手 | **四档**（见 §2） |
| **C 构筑分歧** | **33** | 同一轴的不同走法 | 各有取舍 |
| 合计 | **100** | | |

现有 21 张**全部保留**（不删除，落进对应段）。

---

## 2. 强度分档（B 段尤其要"有强有弱、对部分是废卡"）

| 档 | 定义 | 对非目标阵营 | 例子 |
|---|---|---|---|
| **S 强针对** | 对目标阵营近乎决定胜负 | **纯废牌**（打不出来/无目标） | 王城<10 才生效的卡 |
| **A 中针对** | 对目标好用，对别家仍有基本作用 | 可用但平庸 | 按对方云栈数量破坏 |
| **B 泛用** | 谁都能用 | 正常 | 致伤、抽牌、沉默 |
| **C 环境废牌** | **故意做得弱**，只有极窄场景有用 | 近似白板 | 只在特定条件生效的小效果 |

**每档数量建议**：S 6 / A 10 / B 12 / C 5。

---

## 3. 编号与命名规范（防止 100 张互相冲突）

- **id 前缀一律 `wood_`**，全小写下划线。
- **已被占用的 id（21 张现有卡 + 本规范前已定稿的）不得复用**：
  ```
  现有：wood_leader wood_sapling wood_guard wood_spring wood_druid wood_bear
        wood_thorn wood_growth wood_bark wood_root wood_veil wood_punish_wrath
        wood_treant wood_wisp wood_moon wood_warden wood_seed wood_stag
        wood_circle wood_renew wood_owl
  ```
- **tag 只能取自现有 tag 池 + 明确新增**：
  ```
  现有：统领 树灵 守卫 恢复 德鲁伊 野兽 荆棘 增强 守护 结界 天罚 精灵 月光 巡守 仪式
  新增（本池允许）：藤蔓 世界树 古树 苔藓 菌根
  ```
  **新增 tag 全池 ≤5 个**，且每个新 tag 至少有 2 张卡使用（否则应并入现有 tag）。

---

## 4. 每张卡必须交出的字段

```
id            稳定 id（wood_ 前缀，唯一）
name          中文卡名（不与其他卡重名）
section       A 成长 / B 针对 / C 构筑分歧
tier          （B 段必填）S 强针对 / A 中针对 / B 泛用 / C 环境废牌
targetFaction 针对哪个阵营（B 段必填）：烈焰 / 机械 / 深海 / 无
type          MINION / SPELL / AMBUSH / PUNISH
tags          []  = 无 tag
punish        惩罚值（代价轴，本作唯一代价）
attack/health （MINION 必填）
condition     §0.3 表中的词条，或留空
effects       用 §0.1 的 action + §0.2 的 target
text          卡面文案（**必须逐字写出 condition 与效果**，玩家不能靠猜）
engineCost    「零」（纯现有字段，立刻可测） / 「小」（加条件词条） / 「大」（需新机制）
```

---

## 5. 设计原则（owner 的批评都要对上）

1. **不要只有"强化/叠层/功能/防御"四类**。要有多样：条件性、复合、时序（吟唱）、跨阵营互动、改变规则性效果。
2. **允许非字段的一次性特殊效果**（如 owner 范例：若王城<10，破坏对方本回合攻击过王城的随从）。
   这类标为 `engineCost: 大`，**卡面必须写清**。
3. **针对卡的强度要故意不均**，并且**允许环境废牌存在**。
4. **卡面与效果必须一致** —— 卡面不写条件而字段里有条件，视为缺陷。
5. **每张卡要能回答"它推进 512 的哪一步"或"它针对谁的哪个支点"**。

---

## 6. 验收（设计完成后）

1. **100 张不重 id、不重名**。
2. **每张卡都用 §0.1/§0.2/§0.3 允许的值**（我把 100 张逐张对照校验）。
3. **`engineCost: 零` 的卡立刻可测**；统计三类成本各多少张。
4. **落数据 → 跑 `WoodDesignProbe` + 配对矩阵**：
   - 成长轴：中位封印生命 ≥512
   - 针对轴：**每张针对卡的"带它/不带它"胜率差**要算出（验证"对谁有效、对谁是废牌"）
   - 构筑分歧：不同构筑的胜率分布**必须真的不同**（相同则分歧是假的）
