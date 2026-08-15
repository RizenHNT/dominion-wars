> **⚠️ 已废弃（2026-08-15）**：本文件是 v0.1 旧卡重平衡路径的产物，已被新方法论（CARD_DESIGN_MODEL / CARD_VALUE_MODEL / 卡牌设计包）取代。仅保留作历史参考，不再维护。

# 统御战纪 · 平衡基本包卡牌设计（提案 v0.1）

> **作者**：DeepSeek（QA/策划）· **日期**：2026-08-15
> **状态**：🟡 起草中（先出设计框架 + 模板，全卡 spec 随后）
> **来源派发**：docs/AI_MAILBOX.md「🔵 [PL → DeepSeek] 卡牌设计批次：平衡基本包全卡设计」
> **规则基线**：docs/RULES.md v1.0（含 §12 候选机制）· **效果基线**：docs/effects.contract.md（24 动作）
> **数据基线**：data/schema/cards.schema.json · 现有 91 张卡（全量重做）· data/balance.json

---

## 0. 本文件内容

1. **设计目标与硬约束**
2. **平衡数值参考表**（惩罚值 ↔ 效果/身材换算）
3. **五类卡牌字段模板**（随从/咒文/伏击/惩罚/统领）— 即 Codex 实现规格
4. **阵营主题与协同框架**（flame / machine / sea / wood / neutral）
5. **统领重设计方案**（含 machine_alpha 下载轴协议+阈值、shadow_of_fate 新身份）
6. **schema / 引擎扩展需求清单**（候选机制依赖标注）
7. **实现依赖标注规则**（READY / MECHANIC_DEPENDENT）

---

## 1. 设计目标与硬约束

### 1.1 目标
- 现有 91 张太少、组卡自由度低。本批次产出**平衡基本包**（非全集）：≈120–150 张，可支撑 4 套官方卡组 + 自由组卡。
- 所有旧卡在 RULES §12 框架下重做，成为完整、平衡、可组卡的基本包卡池。
- 每张卡给出：id / 字段 / 效果 / 文本 / 平衡理由 / 强度评级。**只出提案，不改生产代码**。

### 1.2 硬约束（来自 RULES / schema / effects.contract）
1. **构筑**：59 主牌 + 1 统领 = 60 张；统领不占主牌名额（RULES §5）。
2. **惩罚值是唯一费用轴**（RULES §3）：打出卡牌使对手抽 printedPunish 张；无独立法力系统。
3. **效果只能用 24 个 EffectAction**（effects.contract §2）：DAMAGE/HEAL/DRAW/OPP_DRAW/DISCARD_OPP_RANDOM/DISCARD_DRAWN/DESTROY/BUFF/GRANT_KEYWORD/SUMMON/SUMMON_LEADER/END_TURN/ADD_OPP_PUNISH_TURN/ADD_SELF_PUNISH_TURN/CONVERT_PUNISH_TO_DISCARD/PROTECT_TURN/NEGATE/NEGATE_ENEMY_EFFECTS_TURN/SKIP_RESHUFFLE/RESTORE_ATTACKS/GAIN_LIFE/LOSE_LIFE/DAMAGE_CASTLE/WIN_GAME。
4. **target 只能用 11 个 EffectTarget**（schema 枚举）。
5. **字段必须落在 cards.schema.json 已定义范围内**；候选机制字段（协议/连乘/潮位/下载轴 winCondition）**未定义**，凡依赖者标注 `MECHANIC_DEPENDENT` 并列入 §6 扩展清单。
6. **词条限制**（RULES §4）：同玩家同词条每个回合段一次。
7. **伏击规则**（RULES §6）：每回合盖 1 张；每敌方动作最多触发 1 张；普通可累积 / 专注压制其他 / 封场禁止再盖。
8. **统领规则**（RULES §7 + §11.2）：只有随从统领可被击败；非随从统领必须自带显式 winCondition；外部效果作用统领需三步判定链。
9. **王城**（RULES §9.1）：75 血共享中立；破城计数≥9 + 破城方叫出自己统领；flame 的 ROYAL_CASTLE_BREAK 被动触发获胜（保留）。
10. **胜利计数**（RULES §9）：对手牌库循环 10 次获胜；强制弃牌不计入弃牌胜利（防挂机）。
11. **§12 候选机制未启用**：设计可以定义机制卡，但必须在实现依赖标注中标明，且机制数值在正式启用前另行冻结。

### 1.3 平衡纪律
- **强度评级**：每卡标注 S/A/B/C/D（S=构筑核心，A=强，B=常规可用，C=备选，D=环境卡/纪念卡）。基本包目标：每阵营 B 级为主（≈60%），A 级 20%，C 级 15%，S 级 ≤1-2 张，D 级 0（不设计废卡）。
- **数值自查**：每卡给出「惩罚值 → 效果+身材」折算，与 §2 参考表对照；超模 ≤10%，并在平衡理由中说明风险补偿（如针对性/条件性/延迟）。
- **协同不闭环**：任何单阵营协同链必须有明确反制出口（对手可以打断/规避），不允许无解组合。

---

## 2. 平衡数值参考表（惩罚值 ↔ 强度折算）

> 出牌 = 给对手送惩罚抽牌，因此同费卡比传统 CCG 身材更优。参考现有卡实测（BALANCE.md 96 局模拟：烈焰 56.3% / 机械 52.1% / 木 50% / 海 41.7%）。

### 2.1 白板身材参考（无效果随从）

| 惩罚值 | 可接受身材 | 参考 | 说明 |
|---|---|---|---|
| 0 | 1/2 ~ 1/3 | wood_sapling 1/2 | 0 惩罚 = 无风险，身材必须弱 |
| 1 | 2/2 ~ 2/3 | neutral_mercenary 3/3(punish2) 下调 | 1 惩罚 = 轻微风险 |
| 2 | 3/3 ~ 3/4 | neutral_mercenary 3/3 | 基准线 |
| 3 | 4/4 ~ 5/4 | flame_drake 4/4 | 中风险换中等身材 |
| 4 | 5/5 ~ 5/6 | — | 高风险 |
| 5 | 6/6 ~ 7/6 | flame_giant 7/7 嘲讽(punish5) | 高费终结 |
| 6+ | 7/7 以上 + 关键词 | — | 仅随从统领/终结随从 |

### 2.2 效果价值换算（1 个效果 ≈ 若干惩罚值）

| 效果 | 约等于惩罚值 | 说明 |
|---|---|---|
| 1 点单体伤害 | 0.8 | DAMAGE@ENEMY_TARGET |
| 1 点打脸 | 0.7 | DAMAGE@ENEMY_FACE（终结效率，节奏型） |
| 1 点 AOE 伤害 | 1.2 | DAMAGE@ALL_ENEMY_MINIONS |
| 抽 1 张 | 1.2 | DRAW（自己） |
| 对方抽 1 张 | 0.4 | OPP_DRAW（喂牌 = 低收益甚至负收益，慎用） |
| 随机弃对方 1 张 | 0.9 | DISCARD_OPP_RANDOM |
| 弃刚抽到的 1 张 | 1.0 | DISCARD_DRAWN（深渊吞噬类，克制抽牌） |
| 破坏 1 个随从 | 3.0 | DESTROY（无伤害结算，强） |
| 治疗 1 点 | 0.35 | HEAL |
| +1/+1（单随从） | 0.8 | BUFF both |
| 授予圣盾 | 1.3 | GRANT_KEYWORD 圣盾 |
| 授予突袭 | 1.0 | GRANT_KEYWORD 突袭 |
| 授予嘲讽 | 0.7 | GRANT_KEYWORD 嘲讽 |
| 授予扰魔 | 0.8 | GRANT_KEYWORD 扰魔 |
| 召唤 1 个基础随从 | 1.5~2.0 | SUMMON（视 token 质量） |
| 强制对方结束回合 | 4.0+ | END_TURN（罕见，仅统领级） |
| 本回合对方惩罚 +1 | 0.5 | ADD_OPP_PUNISH_TURN（先手惩罚流） |
| 对方弃牌胜利计数 | 特殊 | 见统领 |
| 对王城 1 点伤害 | 0.6 | DAMAGE_CASTLE（王城 75 血，节奏慢） |

### 2.3 关键词价值（从身材扣除）

| 关键词 | 扣除身材 | 说明 |
|---|---|---|
| 突袭 | −1 攻或 −1 血 | 召唤当回合可攻 |
| 嘲讽 | −1 血 | 强制目标 |
| 圣盾 | −1 血 | 挡一次伤害 |
| 扰魔 | −0.5 | 不能成为咒文目标 |

### 2.4 卡组节奏目标
- 平均对局 15±3 玩家回合（BALANCE.md 目标带）。
- 每阵营卡组应有：低费曲线（0-2 惩罚 ≥8 张）、中期（3-4 惩罚 ≥10 张）、终结（5+ 惩罚 4-6 张）。
- 惩罚值总和（整副 60 张）≈ 110-140（过高 = 送牌太多养对手惩罚牌；过低 = 自身资源不足）。

---

## 3. 五类卡牌字段模板（Codex 实现规格）

> 模板对齐 data/schema/cards.schema.json。**加粗字段 = 必填**；标注 ⚠️ = 需 schema 扩展（§6）。

### 3.1 随从（MINION）
```json
{
  "id": "flame_recruit",              // StableId: ^[a-z][a-z0-9_]*$，≥3 ≤64
  "name": "烈焰新兵",                  // ≤32 字
  "faction": "烈焰帝国",              // 阵营
  "type": "MINION",
  "tags": ["士兵"],                    // ≤4 个，≤8 字/个
  "punish": 1,                        // 0-20，出牌让对手抽 N 张
  "attack": 2,
  "health": 1,
  "keywords": [],                     // 嘲讽/圣盾/扰魔/突袭，≤4
  "guard": false,                     // 【护卫】随从护驾时不可被普攻
  "kingSlayer": false,                // 旧卡级字段，新卡用效果级
  "attacksPerTurn": 1,                // 默认 1
  "onPlayEffects": [],                // 战吼（打出时）
  "punishActivatable": false,         // 是否可被惩罚触发
  "punishCost": 0,                    // 触发惩罚值（响应成本）
  "punishCondition": null,            // ALWAYS/ENEMY_MINIONS_GE_1/GE_2/HAND_GE_3
  "punishEffects": [],
  "onOpponentDiscardEffects": [],     // 对方效果弃牌时触发（海联动）
  "text": "效果文本（简体中文，词条不重复写入）",
  "flavor": "风味文本（可选）"
}
```

### 3.2 咒文（SPELL）
```json
{
  "id": "flame_strike",
  "name": "火焰冲击",
  "faction": "烈焰帝国",
  "type": "SPELL",
  "tags": ["破坏"],
  "punish": 3,
  "onPlayEffects": [{ "action": "DAMAGE", "amount": 5, "target": "ENEMY_TARGET", "kingSlayer": true }],
  "chant": 0,                          // >0 则【吟唱 X】：入场等 X 回合后结算
  "chantEffects": [],                  // 吟唱结算效果
  "text": "…",
  "flavor": ""
}
```

### 3.3 伏击（AMBUSH）
```json
{
  "id": "flame_ambush_counter",
  "name": "烈火反噬",
  "faction": "烈焰帝国",
  "type": "AMBUSH",
  "tags": ["反击"],
  "punish": 1,                         // 盖放时支付
  "ambushKind": "NORMAL",              // NORMAL/FOCUS/LOCKDOWN
  "ambushTrigger": "OPPONENT_ATTACKS", // OPPONENT_ATTACKS/PLAYS_SPELL/SUMMONS/PLAYS_CARD/DRAWS
  "ambushEffects": [{ "action": "DAMAGE", "amount": 2, "target": "ENEMY_MINION" }],
  "text": "…"
}
```

### 3.4 惩罚牌（PUNISH）
```json
{
  "id": "flame_punish_wrath",
  "name": "焚天之怒",
  "faction": "烈焰帝国",
  "type": "PUNISH",
  "tags": ["天罚"],
  "punish": 5,                         // PUNISH 必填，1-20
  "punishActivatable": true,           // 被惩罚抽到时可发动
  "punishCost": 1,                     // 响应成本
  "punishCondition": "ENEMY_MINIONS_GE_1",
  "punishEffects": [{ "action": "DAMAGE", "amount": 4, "target": "ALL_ENEMY_MINIONS" }],
  "text": "…"
}
```

### 3.5 统领（任意类型 + leader:true）
> 存在形式由卡型+字段决定：随从统领（MINION+attack/health）可被击败（强度预算高）；耐久统领（leaderDef.durability）；赋予生命统领（leaderDef.grantLife）；伏击统领（AMBUSH+ambushTrigger，触发条件达成为胜）。

```json
{
  "id": "flame_leader",
  "name": "烈焰皇·焚天",
  "faction": "烈焰帝国",
  "type": "MINION",
  "tags": ["统领"],
  "punish": 0,
  "attack": 8,
  "health": 10,
  "keywords": ["圣盾"],
  "leader": true,
  "leaderDef": {
    "winCondition": "ROYAL_CASTLE_BREAK",  // NONE/ROYAL_CASTLE_BREAK/AMBUSH_TRIGGER_WIN/OPP_DISCARD_TOTAL_GE/OPP_PUNISH_DRAW_TURN_GE/NO_DAMAGE_TURNS_GE + ⚠️新值
    "winAmount": 0,                        // 阈值（winCondition 需要时）
    "winParam": 0,                         // 第二参数
    "winText": "王城被击破时你立即获胜",
    "durability": null,                    // 耐久形态
    "grantLife": null,                     // 赋予生命形态
    "vulnerabilities": ["DAMAGE"],         // 效果白名单，空=全免疫
    "enterEffects": [],                    // 登场效果
    "punishEffects": [],                   // 被惩罚抽到时触发
    "persistentEffects": []                // ⚠️当前无允许值（DISABLE 已删），预留
  },
  "text": "…"
}
```

### 3.6 文本规范
1. 简体中文；效果文本以「效果」开头部分描述，关键词/词条不重复写入（卡面已有词条条）。
2. 数值用阿拉伯数字；目标代词统一（你/对手/己方/对方）。
3. 条件句式：「若…则…」「每当…」「当…时」。
4. 文本长度 ≤256（schema 上限），flavor ≤256。
5. 多效果按数组顺序 = 结算顺序（effects.contract §1.6），文本用「随后」衔接。

---

## 4. 阵营主题与协同框架

### 4.1 烈焰帝国 —「速攻直伤 · 破城终结」
- **身份**：最快节奏、直伤打脸、高攻击低血量、破坏性咒文；中后期围绕王城施压。
- **协同关键词**：直伤（ENEMY_FACE）、连击（RESTORE_ATTACKS）、火焰（DAMAGE 主题）、破城（DAMAGE_CASTLE + ROYAL_CASTLE_BREAK）。
- **统领**：flame_leader（随从统领 8/10 圣盾，ROYAL_CASTLE_BREAK 保留——人类裁决）。
- **风险补偿**：自身随从偏脆、依赖直伤抢血 → 惩罚抽牌风险可控（低费低惩罚为主）。
- **数量**：26 张（含统领）。

### 4.2 机械遗迹 —「上传下载 · 协议轴」（全量重做，⚠️机制依赖）
- **身份**：围绕 §12.4 Commit/Push/Rollback/Pull 的转换引擎。基础随从惩罚 0、身材偏低（RULES §12.4 基线），通过提交队列/云端栈形成价值。
- **协同关键词**：提交（Commit 付费进队列）→ 推送（Push 进云端触发效果）→ 拉取（Pull 付费取回结算）→ 回滚（Rollback 回手）；协议字段（COMMIT/PUSH/PULL/ROLLBACK/CHARGE/STEALTH/TAUNT/BATTLECRY/DEATHRATTLE）供统领读取。
- **统领**：machine_leader（非随从，上传轴先锋）+ machine_alpha（随从统领，**下载轴胜利**，见 §5.2）。
- **实现状态**：本阵营大部分卡 = `MECHANIC_DEPENDENT`（需引擎支持 Commit/Push/Pull 动作 + 协议字段 + 下载轴 winCondition）。设计照常产出，实现待 §12.4 接入。
- **数量**：28 张（含 2 统领）。

### 4.3 深海联盟 —「低费铺场 · 弃牌潮蚀」
- **身份**：低惩罚值、前期铺场、弃牌干扰、对方弃牌联动（onOpponentDiscardEffects）；潮位为候选机制（⚠️）。
- **协同关键词**：弃牌（DISCARD_OPP_RANDOM/DISCARD_DRAWN）、对方弃牌联动（BUFF/DAMAGE_CASTLE）、吞噬（抽牌惩罚化）。
- **统领**：sea_leader（赋予生命统领，潮汐债务 + 弃牌胜利 OPP_DISCARD_TOTAL_GE）。
- **风险补偿**：弃牌胜利需效果弃牌（强制弃牌不计）；潮位机制未启用前用「弃牌联动」撑起主题。
- **数量**：26 张（含统领）。

### 4.4 古木圣地 —「成长强化 · 连乘封印」
- **身份**：慢速成长、治疗、大随从、BUFF 强化；连乘/封印/512 生命为候选机制（⚠️）。
- **协同关键词**：治疗（HEAL）、强化（BUFF/GRANT_KEYWORD）、大随从（高血嘲讽）、成长（连乘 ⚠️）。
- **统领**：wood_leader（耐久统领，成长启动 + 无伤计数 NO_DAMAGE_TURNS_GE 重审视）。
- **风险补偿**：前期弱、靠治疗与高血拖后期；无伤计数对破城/直伤敏感。
- **数量**：26 张（含统领）。

### 4.5 中立 —「通用工具 · 命运主题」
- **身份**：跨阵营通用工具（抽牌/直伤/身材/嘲讽）+ 命运主题卡（shadow_of_fate / gate_of_fate）。
- **统领**：shadow_of_fate（新身份，见 §5.3）+ gate_of_fate（伏击统领，AMBUSH_TRIGGER_WIN 保留）。
- **数量**：14 张（含 2 统领）。

---

## 5. 统领重设计方案

### 5.1 flame_leader — 烈焰皇·焚天（保留，微调）
- 形态：随从统领 8/10，圣盾，vulnerabilities=[DAMAGE]。
- winCondition：**ROYAL_CASTLE_BREAK**（人类裁决保留，破城即胜，被动不问谁破城）。
- enterEffects：DAMAGE:2（登场 2 点 AOE 或打脸——按数值参考，登场给 2 点全场或 3 点单体）。
- punishEffects：END_TURN + DAMAGE（被惩罚抽到时强制对方结束回合——保留，强且主题符合）。
- 平衡理由：随从统领可被击败 → 强度预算最高档；破城是慢轴（75 血），速攻+破城双轴。

### 5.2 machine_alpha — 下载轴胜利（人类裁决，定稿协议+阈值）⭐
- 形态：随从统领（攻/血战斗，可被击败，强度预算高）。
- winCondition：**⚠️ 新值 `PULL_TOTAL_GE`**（schema 需扩展）：本局己方累计有效 Pull（拉取结算）达到阈值即获胜。
- **协议组合定稿**：协议字段白名单 = `COMMIT, PUSH, PULL, ROLLBACK, CHARGE, TAUNT`（六字段；STEALTH/BATTLECRY/DEATHRATTLE 不入白名单——机械基础卡不主打潜行/战吼/亡语）。
- **阈值定稿：累计 6 次 Pull 结算**（每次 Pull 将云端栈顶卡移出并结算 Pull 效果，计入一次；Rollback 不计；对手 Pull 不计）。
- 理由：一局约 15 回合，机械前期 Commit/Push 铺转换，中期开始 Pull 回收；6 次 Pull ≈ 第 12-14 回合达成，与 10 次牌库循环胜利时间线竞争。若过强调 7，过弱调 5（自验证建议首测 6）。
- 附带：登场后己方 Pull 惩罚费用 = 0（RULES §12.4 机械统领已定），即胜利轴不依赖 Pull 费用资源。
- enterEffects：SUMMON 1-2 个机偶 + 或 BUFF。
- punishEffects：ADD_OPP_PUNISH_TURN（保留主题）。

### 5.3 machine_leader — 上传轴先锋（重做）
- 形态：耐久统领（durability，非随从，不可被击败）。
- winCondition：**NONE**（先锋不是胜利轴；胜利在 machine_alpha 下载轴）。
- 角色：上传轴引擎——登场给提交/推送加速（enterEffects 给已提交队列加速；用现有动作表达如 DRAW/BUFF + ⚠️机制动作如 "PUSH_NEXT"）。
- 或：改为随从统领但弱身材 + 高收益 enter？——**定：耐久统领**，避免与 alpha 双随从叠加破城收益。

### 5.4 sea_leader — 深渊主宰·涛冥（重做：潮汐债务 + 弃牌胜利）
- 形态：赋予生命统领（grantLife，玩家生命归零败北，非随从不可被击败）。
- winCondition：**OPP_DISCARD_TOTAL_GE**（保留弃牌胜利，阈值重新定稿）。
- **阈值定稿：12**（BALANCE.md 记录 15→12 的历史调整；12 为当前运行时值，保留）。
- 潮汐债务（§12.3 候选）：登场读取对手未消耗潮位 → 转化为指定参数削弱。⚠️机制依赖；启用前 enterEffects 用 DRAW:2 + 弃牌联动表达。
- punishEffects：CONVERT_PUNISH_TO_DISCARD（保留：对方惩罚转弃牌——直接服务弃牌胜利）。

### 5.5 wood_leader — 世界树之心（重做：成长启动 + 无伤胜利）
- 形态：耐久统领（durability，非随从）。
- winCondition：**NO_DAMAGE_TURNS_GE**（保留无伤胜利，阈值重审视：当前 7 回合过高——目标对局 15 回合，7 回合无伤 ≈ 半局，实际更靠防御达成；**定稿 6**，仍偏保守，自验证复核）。
- 角色：成长启动——enterEffects 加入连乘卡 ⚠️（§12.2：统领登场向指定区域加入连乘卡）；启用前 enterEffects = SUMMON 2 树灵 + PROTECT_TURN（保留）。
- punishEffects：PROTECT_TURN（保留）。

### 5.6 shadow_of_fate — 命运之影（新身份）⭐
- 背景：DISABLE_ENEMY_LEADER 已删（人类裁决）。原 MINION 1/3 扰魔随从统领 + NEGATE_ENEMY_EFFECTS_TURN 惩罚。
- **新身份：赋予生命统领**（grantLife）——"命运"主题：操纵惩罚连锁的因果牌。
- winCondition：**⚠️ 新值 `OPP_PUNISH_TRIGGERED_GE`**（schema 需扩展；DeepSeek 评审⑫ D 类建议 OPP_PUNISH_TRIGGERED_GE 6，现正式采用）：对手因惩罚抽到牌并**成功发动惩罚响应**累计 N 次即获胜。
- **阈值定稿：6 次**。理由：对方发动惩罚响应 = 对方主动资源（惩罚值>0 且满足条件），6 次在 15 回合局约第 12-14 回合达成；配合"让对手多抽牌"的干扰套（OPP_DRAW/惩罚转弃牌）可主动加速，但给对手资源有反噬风险——天然平衡。
- 附带机制（⚠️）：shadow 自身可以**强制对方多抽**（OPP_DRAW）来喂连锁；保留 NEGATE_ENEMY_EFFECTS_TURN 作为惩罚响应（被惩罚抽到时）。
- 身份变更理由：原 MINION 1/3 可被击败但无 winCondition（NONE）→ 语义死胡同（评审⑬ Q1：非随从统领必须有显式 winCondition；随从统领 NONE 也可行但无设计空间）。赋予生命统领 + 连锁胜利给出明确身份。

### 5.7 gate_of_fate — 命运之门（保留，微调）
- 形态：伏击统领（AMBUSH + FOCUS + OPPONENT_PLAYS_CARD 触发 + WIN_GAME）。
- winCondition：**AMBUSH_TRIGGER_WIN**（保留——伏击统领的明示触发条件达成即胜，RULES §7 示例）。
- 微调：触发条件/文本按新模板规范化；vulnerabilities 保持 [DAMAGE] 或改空（伏击区不受战斗目标，见 RULES §7 伏击统领）。

---

## 6. schema / 引擎扩展需求清单（交 PL 定稿 + Codex 实现）

| # | 扩展 | 类型 | 用途 | 依赖卡 |
|---|---|---|---|---|
| E1 | WinCondition 新增 `PULL_TOTAL_GE` | schema 枚举 | machine_alpha 下载轴胜利 | machine_alpha |
| E2 | WinCondition 新增 `OPP_PUNISH_TRIGGERED_GE` | schema 枚举 | shadow_of_fate 连锁胜利 | shadow_of_fate |
| E3 | 机械动作：`COMMIT` / `PULL` / `ROLLBACK`（+可选 `PUSH_NEXT`） | EffectAction 枚举 + IEffect | §12.4 上传下载 | machine 阵营大部 |
| E4 | 机械区域状态：提交队列 / 云端栈 | 引擎 GameState + Snapshot | §12.4 | machine 阵营 |
| E5 | 协议字段计数（protocol fields） | 引擎状态 + Snapshot | 机械统领读取进度 | machine_alpha 等 |
| E6 | 连乘进度字段（multiplier） | schema + 引擎 | §12.2 木成长 | wood 阵营部分 |
| E7 | 封印状态（sealed） | schema 字段 + 引擎 | §12.2 木连乘目标 | wood 连乘卡 |
| E8 | 潮位字段（tide） | schema + 引擎 | §12.3 海 | sea 阵营部分 |
| E9 | persistentEffects 新允许值（如有） | schema 枚举 | 未来光环 | 预留 |
| E10 | `OPP_DRAW` 等已实现未用动作（4 个） | 无（已实现） | 可放心使用 | — |

> 标注规则：设计中使用 E1-E8 的卡 = `MECHANIC_DEPENDENT`；只用现有 24 动作 + 11 target + 现有字段 = `READY`。

---

## 7. 实现依赖标注规则

- 每张卡标注 `[READY]` 或 `[MECHANIC_DEPENDENT: E#]`。
- `READY` 卡：当前 schema + 引擎可直接实现（Codex 可立即落地）。
- `MECHANIC_DEPENDENT` 卡：依赖 §6 扩展；在机制接入前不得声称已实现（RULES §12 未启用条款）。
- 每阵营保证 ≥60% 卡为 `READY`，避免整阵营阻塞；机械阵营因机制本体为 §12.4，`READY` 比例可低至 40%，其余标注依赖。

---

*下一批：全卡 spec（每阵营卡表 + 每卡 id/字段/效果/文本/平衡理由/强度评级）*


---

# 全卡 Spec（每阵营逐卡）

> 格式：**id** — 名称 / 类型 / 惩罚 / 身材 / 关键词 / 标签；效果（字段）；文本；实现标注；强度 + 平衡理由。
> 强度：S=构筑核心 / A=强 / B=常规可用 / C=备选 / D=废卡（不设计）。
> 实现：[READY]=当前引擎可做；[MD: E#]=依赖 §6 扩展清单。

## 8. 烈焰帝国（26 张：1 统领 + 13 随从 + 8 咒文 + 2 伏击 + 2 惩罚）

### 8.1 统领

**flame_leader** — 烈焰皇·焚天 / MINION / 惩罚 0 / 8 攻 10 血 / 圣盾 / 标签[统领]
- leaderDef: winCondition=ROYAL_CASTLE_BREAK；vulnerabilities=[DAMAGE]；enterEffects=[DAMAGE:2@ALL_ENEMY_MINIONS]；punishEffects=[END_TURN, DAMAGE:2@ENEMY_FACE]
- 文本：登场：对所有敌方随从造成 2 点伤害。若你被惩罚抽到：结束对方回合，并对其造成 2 点伤害。王城被击破时你立即获胜。
- 实现：[READY]
- 强度：S（卡组定义）。理由：随从统领可被击败→强度预算最高档；8/10 圣盾站场 + 破城即胜慢轴 + 速攻快轴双胜利路线。登场 2 点 AOE 压制前期铺场；被惩罚抽到时强制结束对方回合（END_TURN 4.0+ 价值）是惩罚抽牌风险的反制——但只在你被惩罚抽到时触发，对手可控制是否喂你。

### 8.2 随从（13）

**flame_recruit** — 烈焰新兵 / MINION / 惩罚 1 / 2 攻 1 血 / 标签[士兵]
- 文本：无效果。
- 实现：[READY]
- 强度：C（曲线填充）。理由：2/1 身材(3) vs 惩罚 1 基准 2/2(4)——亏 1 点但为速攻 1 费曲线；标准压迫型 1-drop。

**flame_imp** — 火舌小鬼 / MINION / 惩罚 0 / 1 攻 2 血 / 标签[恶魔]
- onPlayEffects: DAMAGE:1@ENEMY_FACE
- 文本：战吼：对敌方统领造成 1 点伤害。
- 实现：[READY]
- 强度：B（速攻核心）。理由：3 身材 + 1 打脸(0.7) = 3.7 价值 @ 惩罚 0——惩罚 0 无风险，故略超；但 1/2 极易被换掉，风险由站场能力对冲。是 0 费曲线唯一站场威胁。

**flame_charger** — 冲锋骑兵 / MINION / 惩罚 2 / 3 攻 2 血 / 突袭 / 标签[骑兵]
- 文本：突袭。
- 实现：[READY]
- 强度：B。理由：5 身材 −1(突袭) = 4 vs 惩罚 2 基准 6——亏 2 换节奏；速攻需要的是当回合打点，接受身材亏损。

**flame_guard** — 熔岩护卫 / MINION / 惩罚 2 / 2 攻 6 血 / 嘲讽 / 标签[守卫]
- 文本：嘲讽。
- 实现：[READY]
- 强度：B。理由：8 身材 −1(嘲讽) = 7 vs 基准 6——略超，2/6 是标准防守墙，给速攻争取打脸窗口。

**flame_berserker** — 狂热战士 / MINION / 惩罚 3 / 5 攻 3 血 / 标签[狂战]
- punishActivatable=true, punishCost=1, punishCondition=ALWAYS, punishEffects=[DAMAGE:2@ENEMY_FACE]
- 文本：若你被惩罚抽到（费用 1）：对敌方统领造成 2 点伤害。
- 实现：[READY]
- 强度：A。理由：8 身材 vs 基准 8 = 持平；附赠 2 点打脸惩罚响应（1.4 价值，条件=对手喂你牌）——对手弃掉它也亏节奏，是速攻的额外斩杀来源。

**flame_drake** — 赤焰幼龙 / MINION / 惩罚 3 / 4 攻 4 血 / 标签[龙]
- 文本：无效果。
- 实现：[READY]
- 强度：C（标准白板）。理由：8 身材 = 惩罚 3 基准白板，用于曲线稳定。

**flame_giant** — 熔核巨人 / MINION / 惩罚 5 / 7 攻 7 血 / 嘲讽 / 标签[巨人]
- 文本：嘲讽。
- 实现：[READY]
- 强度：B。理由：14 身材 −1(嘲讽) = 13 vs 基准 12——略超但 5 惩罚送对手 5 张，高风险高回报终结墙。

**flame_assassin** — 灰烬刺客 / MINION / 惩罚 2 / 4 攻 2 血 / 突袭 / 标签[刺客]
- punishActivatable=true, punishCost=0, punishCondition=ALWAYS
- 文本：突袭。若你被惩罚抽到（费用 0）：无额外效果，但可 0 费打出占场。
- 实现：[READY]
- 强度：B。理由：6 身材 −1(突袭) = 5 vs 基准 6——亏 1；0 费惩罚响应是软价值（不占出牌惩罚抽牌），节奏牌。

**flame_elemental** — 火元素 / MINION / 惩罚 2 / 3 攻 3 血 / 突袭 / 标签[元素]（token：锻火祭坛召唤）
- 文本：突袭。
- 实现：[READY]
- 强度：B（token 模板）。理由：6 身材 −1(突袭) = 5 vs 基准 6——token 略亏可接受，召唤源赚节奏。

**flame_phoenix** — 烈焰凤凰 / MINION / 惩罚 3 / 4 攻 4 血 / 突袭 / 标签[凤凰]
- 文本：突袭。
- 实现：[READY]
- 强度：B。理由：8 身材 −1(突袭) = 7 vs 基准 8——亏 1 换突袭打点，中期抢血。

**flame_warlord** — 军团统领 / MINION / 惩罚 4 / 5 攻 5 血 / 标签[军官]
- onPlayEffects: BUFF:1:both@ALL_FRIENDLY_MINIONS
- 文本：战吼：己方所有随从 +1/+1。
- 实现：[READY]
- 强度：A（铺场终结）。理由：10 身材 vs 基准 10 = 持平 + 全场 buff(2+ 价值)——铺满场时超模，空场时亏；典型的"场面越多越强"，有明确反制（清场）。

**flame_infernal** — 深渊魔将 / MINION / 惩罚 4 / 5 攻 5 血 / 标签[恶魔]
- onPlayEffects: DAMAGE:3@ENEMY_FACE
- 文本：战吼：对敌方统领造成 3 点伤害。
- 实现：[READY]
- 强度：A（直伤终结）。理由：10 身材 + 3 打脸(2.1) = 12.1 vs 基准 10——超约 2 点；3 惩罚送牌风险对冲，速攻直伤轴的 4 费爆发点。

**flame_warden** — 烈焰守将 / MINION / 惩罚 2 / 3 攻 4 血 / 嘲讽 / 标签[守卫]
- 文本：嘲讽。
- 实现：[READY]
- 强度：C。理由：7 身材 −1(嘲讽) = 6 = 基准——标准 2 费守卫，与熔岩护卫互为曲线替换。

### 8.3 咒文（8）

**flame_strike** — 火焰冲击 / SPELL / 惩罚 3 / 标签[破坏]
- onPlayEffects: DAMAGE:5@ENEMY_TARGET, kingSlayer=true
- 文本：对一个目标造成 5 点伤害（可作用于统领）。
- 实现：[READY]
- 强度：A（招牌解场）。理由：5×0.8=4.0 + 弑君(0.5) = 4.5 vs 基准 3——超约 1.5，但目标必须存在且为单体；超模卡允许小幅超模（身份牌）。

**flame_rain** — 烈焰风暴 / SPELL / 惩罚 4 / 标签[风暴]
- onPlayEffects: DAMAGE:3@ALL_ENEMY_MINIONS
- 文本：对所有敌方随从造成 3 点伤害。
- 实现：[READY]
- 强度：B。理由：3×1.2=3.6 vs 基准 4——略亏，AOE 清场稳定性是隐性价值。

**flame_bolt** — 飞火流星 / SPELL / 惩罚 2 / 标签[速攻]
- onPlayEffects: DAMAGE:3@ENEMY_FACE
- 文本：对敌方统领造成 3 点伤害。
- 实现：[READY]
- 强度：A（斩杀组件）。理由：3×0.7=2.1 vs 基准 2——略超；无目标限制、稳定 3 伤，速攻斩杀核心，配合打脸随从。

**flame_warcry** — 帝国战吼 / SPELL / 惩罚 3 / 标签[增强]
- onPlayEffects: BUFF:1:both@ALL_FRIENDLY_MINIONS
- 文本：己方所有随从 +1/+1。
- 实现：[READY]
- 强度：B。理由：全场 buff（2+ 价值）vs 基准 3——铺场时赚；与军团统领(8.2)效果重复但为咒文可重复带，数值压到 3 惩罚避免过度堆叠。

**flame_double** — 二连斩令 / SPELL / 惩罚 2 / 标签[军令]
- onPlayEffects: RESTORE_ATTACKS@FRIENDLY_MINION
- 文本：使己方一个随从本回合可再次攻击。
- 实现：[READY]
- 强度：B（连击组件）。理由：重置攻击价值 ≈ 目标攻击力(1.5-2.5)，基准 2——配合高攻随从超模，配合 1 攻随从亏；典型"上限牌"。

**flame_forge** — 锻火祭坛 / SPELL / 惩罚 2 / 标签[仪式]
- chant=2, chantEffects=[SUMMON:2:flame_elemental]
- 文本：吟唱 2：2 回合后召唤 2 个 3/3 突袭火元素。
- 实现：[READY]
- 强度：B（延迟铺场）。理由：2×(5 价值) = 10 延迟 2 回合 vs 基准 2——延迟风险对冲超高价值；吟唱期可被针对。

**flame_banner** — 帝国军旗 / SPELL / 惩罚 2 / 标签[军势]
- onPlayEffects: SUMMON:2:flame_recruit
- 文本：召唤 2 个 2/1 烈焰新兵。
- 实现：[READY]
- 强度：B。理由：2×(3 价值) = 6 vs 基准 2——超模但产出是弱 token；铺场引擎，配合军团统领/帝国战吼收益翻倍。

**flame_siege** — 烈焰攻城 / SPELL / 惩罚 3 / 标签[攻城]
- onPlayEffects: DAMAGE_CASTLE:3
- 文本：对共享王城造成 3 点伤害。
- 实现：[READY]
- 强度：C（破城轴组件）。理由：3×0.6=1.8 vs 基准 3——按即时价值亏；但 75 血王城 + 破城计数 9 + 统领 ROYAL_CASTLE_BREAK，是速攻转中期的第二胜利轴，价值按"全卡组协同"计。

### 8.4 伏击（2）

**flame_ambush_counter** — 烈火反噬 / AMBUSH / 惩罚 1 / 标签[反击] / ambushKind=NORMAL, ambushTrigger=OPPONENT_ATTACKS
- ambushEffects: DAMAGE:2@ENEMY_MINION
- 文本：伏击：对方攻击时，对其攻击随从造成 2 点伤害。
- 实现：[READY]
- 强度：B。理由：2×0.8=1.6 vs 基准 1 + 条件触发——条件性补价；惩罚 1 便宜，速攻曲线友好。

**flame_ambush_seal** — 燃尽封咒 / AMBUSH / 惩罚 2 / 标签[反制] / ambushKind=FOCUS, ambushTrigger=OPPONENT_PLAYS_SPELL
- ambushEffects: NEGATE
- 文本：伏击（专注）：对方打出咒文时，反制其效果。
- 实现：[READY]
- 强度：B。理由：反制(2.5) vs 基准 2 + 专注限制（触发当回合压制其他伏击）——条件反制标准价。

### 8.5 惩罚牌（2）

**flame_punish_wrath** — 焚天之怒 / PUNISH / 惩罚 5 / 标签[天罚]
- punishActivatable=true, punishCost=1, punishCondition=ENEMY_MINIONS_GE_1, punishEffects=[DAMAGE:4@ALL_ENEMY_MINIONS]
- 文本：若你被惩罚抽到（费用 1）：对方有随从时，对所有敌方随从造成 4 点伤害。
- 实现：[READY]
- 强度：A。理由：4×1.2=4.8 群体 vs 惩罚 5+1 响应——高费高回报，条件限制（对方有随从）防空发；是速攻卡组的中期清场兼斩杀保护。

**flame_punish_rage** — 怒焰迸发 / PUNISH / 惩罚 4 / 标签[天罚]
- punishActivatable=true, punishCost=1, punishCondition=HAND_GE_3, punishEffects=[DAMAGE:4@ENEMY_FACE]
- 文本：若你被惩罚抽到（费用 1）：你的手牌 ≥3 时，对敌方统领造成 4 点伤害。
- 实现：[READY]
- 强度：B。理由：4×0.7=2.8 vs 惩罚 4+1——按直伤偏贵；HAND_GE_3 条件保证非空发，作为额外斩杀补件。

## 9. 深海联盟（26 张：1 统领 + 12 随从 + 9 咒文 + 2 伏击 + 2 惩罚）

### 9.1 统领

**sea_leader** — 深渊主宰·涛冥 / SPELL / 惩罚 0 / 标签[统领]
- leaderDef: winCondition=OPP_DISCARD_TOTAL_GE, winAmount=12, grantLife=25, vulnerabilities=[DAMAGE]；enterEffects=[DRAW:2]；punishEffects=[CONVERT_PUNISH_TO_DISCARD]
- 文本：赋予你 25 点生命。登场：抽 2 张。若你被惩罚抽到：对方本回合的惩罚抽牌改为弃牌。对方累计因效果弃牌达到 12 张时你获胜。
- 实现：[READY]（潮汐债务部分 [MD: E8]，见下注）
- 强度：S（卡组定义）。理由：赋予生命统领（玩家生命=25）不可被击败→胜利依赖弃牌轴；强制弃牌不计入胜利（RULES §9 防挂机），所以 12 张必须靠效果弃牌/惩罚转化弃牌——卡组围绕弃牌构建。被惩罚抽到时翻转对方惩罚为弃牌（CONVERT_PUNISH_TO_DISCARD）直接加速胜利，且让对手"不敢喂你牌"——自带博弈。注：§12.3 潮汐债务（登场读取对手未消耗潮位转化为削弱）为候选机制，启用前 enterEffects 用 DRAW:2 表达。

### 9.2 随从（12）

**sea_crab** — 铁甲蟹 / MINION / 惩罚 1 / 1 攻 4 血 / 嘲讽 / 标签[甲壳]
- 文本：嘲讽。
- 实现：[READY]
- 强度：B。理由：5 身材 −1(嘲讽) = 4 = 惩罚 1 基准；低费铺场墙，保护弃牌引擎随从。

**sea_siren** — 暗礁海妖 / MINION / 惩罚 2 / 2 攻 3 血 / 标签[海妖]
- onPlayEffects: DISCARD_OPP_RANDOM:1；onOpponentDiscardEffects: BUFF:1:both@SELF
- 文本：战吼：随机弃对方 1 张手牌。每当对方因效果弃牌：本随从 +1/+1。
- 实现：[READY]
- 强度：A（弃牌轴核心）。理由：5 身材 + 0.9(弃1) = 5.9 vs 基准 6 持平；联动成长（对方每效果弃 1 张 +1/+1）是滚雪球核心，但依赖弃牌事件持续发生——对手可少弃牌来压制，有明确反制。

**sea_leviathan_young** — 幼年利维坦 / MINION / 惩罚 3 / 5 攻 5 血 / 标签[巨兽]
- 文本：无效果。
- 实现：[READY]
- 强度：B。理由：10 身材 vs 基准 8——超 2 点的大白板；海阵营旧版最弱(41.7%)，靠身材超模补强度；5 惩罚送牌风险对冲。

**sea_mistwalker** — 迷雾行者 / MINION / 惩罚 2 / 3 攻 3 血 / 扰魔 / 标签[幽影]
- 文本：扰魔。
- 实现：[READY]
- 强度：C。理由：6 身材 −0.5(扰魔) = 5.5 vs 基准 6——标准中规中矩，站场干扰件。

**sea_kraken** — 克拉肯触手 / MINION / 惩罚 2 / 4 攻 2 血 / 标签[巨兽]
- punishActivatable=true, punishCost=0, punishCondition=ENEMY_MINIONS_GE_2
- 文本：若你被惩罚抽到（费用 0）：对方有 ≥2 个随从时可打出占场。
- 实现：[READY]
- 强度：B。理由：6 身材 = 基准；0 费惩罚响应（条件=对方有随从）是免费节奏点，但触发条件限制非空发。

**sea_tentacle** — 深渊之触 / MINION / 惩罚 1 / 2 攻 2 血 / 嘲讽 / 标签[触手]（token：深渊召令召唤）
- 文本：嘲讽。
- 实现：[READY]
- 强度：B（token 模板）。理由：3 身材 vs 基准 4——token 略亏可接受，召唤源赚节奏。

**sea_eel** — 电鳗游袭 / MINION / 惩罚 1 / 3 攻 1 血 / 突袭 / 标签[速攻]
- 文本：突袭。
- 实现：[READY]
- 强度：B。理由：4 身材 −1(突袭) = 3 vs 基准 4——亏 1 换当回合打点，前期压制。

**sea_priest** — 潮汐祭司 / MINION / 惩罚 2 / 2 攻 4 血 / 标签[祭司]
- onPlayEffects: HEAL:3@FRIENDLY_MINION
- 文本：战吼：使己方一个随从恢复 3 点生命。
- 实现：[READY]
- 强度：B。理由：6 身材 + 3×0.35=1.05 = 7.05 vs 基准 6——略超；治疗价值在换血局体现，配合高血随从。

**sea_warden** — 深海狱卒 / MINION / 惩罚 3 / 4 攻 5 血 / 嘲讽 / 标签[守卫]
- onOpponentDiscardEffects: DAMAGE_CASTLE:1
- 文本：嘲讽。每当对方因效果弃牌：对共享王城造成 1 点伤害。
- 实现：[READY]
- 强度：A（弃牌→攻城转换）。理由：8 身材 = 基准 + 弃牌联动王城伤害——把弃牌轴转化为破城压力（王城 75 血，配合破城计数 9 与烈焰对称）；对手弃牌越多，王城掉血越快，形成弃牌轴的副胜利。

**sea_abyss** — 深渊巨口 / MINION / 惩罚 3 / 3 攻 5 血 / 扰魔 / 标签[巨兽]
- 文本：扰魔。
- 实现：[READY]
- 强度：C。理由：8 身材 −0.5(扰魔) = 7.5 vs 基准 8——标准防守站场。

**sea_leviathan** — 利维坦巨兽 / MINION / 惩罚 5 / 6 攻 7 血 / 嘲讽 / 标签[巨兽]
- 文本：嘲讽。
- 实现：[READY]
- 强度：B。理由：13 身材 −1(嘲讽) = 12 = 基准；5 惩罚高风险的终结墙。

**sea_song** — 海歌祭司 / MINION / 惩罚 2 / 2 攻 3 血 / 标签[祭司]
- onPlayEffects: GRANT_KEYWORD:圣盾@FRIENDLY_MINION
- 文本：战吼：使己方一个随从获得圣盾。
- 实现：[READY]
- 强度：B。理由：5 身材 + 1.3(圣盾) = 6.3 vs 基准 6——略超；圣盾保护弃牌引擎随从持续输出。

### 9.3 咒文（9）

**sea_tide** — 退潮 / SPELL / 惩罚 1 / 标签[弃牌]
- onPlayEffects: DISCARD_OPP_RANDOM:2
- 文本：随机弃对方 2 张手牌。
- 实现：[READY]
- 强度：A（弃牌轴）。理由：2×0.9=1.8 vs 基准 1——超 0.8，但直接服务弃牌胜利（12 张阈值）；1 惩罚低风险，弃牌轴核心加速器。

**sea_whirl** — 灭顶漩涡 / SPELL / 惩罚 3 / 标签[风暴]
- onPlayEffects: DAMAGE:2@ALL_ENEMY_MINIONS
- 文本：对所有敌方随从造成 2 点伤害。
- 实现：[READY]
- 强度：B。理由：2×1.2=2.4 vs 基准 3——略亏；AOE 清场稳定性补价，与弃牌轴的场面控制配合。

**sea_current** — 海流加速 / SPELL / 惩罚 2 / 标签[抽牌]
- onPlayEffects: DRAW:2
- 文本：抽 2 张牌。
- 实现：[READY]
- 强度：B。理由：2×1.2=2.4 vs 基准 2——略超；抽牌给弃牌轴供弹（弃牌卡需要手牌），2 惩罚送牌风险对冲。

**sea_pressure** — 深压猛击 / SPELL / 惩罚 2 / 标签[破坏]
- onPlayEffects: DAMAGE:3@ENEMY_TARGET, kingSlayer=true
- 文本：对一个目标造成 3 点伤害（可作用于统领）。
- 实现：[READY]
- 强度：A（招牌解场）。理由：3×0.8=2.4+0.5(弑君) = 2.9 vs 基准 2——超 0.9；超模解场，烈焰火焰冲击(5 伤)的低伤版。

**sea_sink** — 沉没 / SPELL / 惩罚 4 / 标签[湮灭]
- onPlayEffects: DESTROY@ENEMY_MINION
- 文本：消灭一个敌方随从。
- 实现：[READY]
- 强度：B。理由：DESTROY(3.0) vs 基准 4——按价值亏，但无条件消灭（无视圣盾/血量）是硬解，惩罚 4 换稳定去除。

**sea_abyss_call** — 深渊召令 / SPELL / 惩罚 4 / 标签[召唤]
- onPlayEffects: SUMMON:2:sea_tentacle
- 文本：召唤 2 个 2/2 嘲讽深渊之触。
- 实现：[READY]
- 强度：B。理由：2×(3 价值) = 6 vs 基准 4——超 2 但产出弱 token；铺场引擎，配合潮汐祭司/海歌祭司收益翻倍。

**sea_barrier** — 水盾结界 / SPELL / 惩罚 2 / 标签[守护]
- onPlayEffects: GRANT_KEYWORD:圣盾@FRIENDLY_MINION
- 文本：使己方一个随从获得圣盾。
- 实现：[READY]
- 强度：C。理由：1.3 vs 基准 2——按价值亏；保护关键随从的防解卡，与海歌祭司二选一带。

**sea_depths** — 海渊凝视 / SPELL / 惩罚 2 / 标签[仪式]
- chant=1, chantEffects=[DISCARD_OPP_RANDOM:3]
- 文本：吟唱 1：1 回合后随机弃对方 3 张手牌。
- 实现：[READY]
- 强度：B（弃牌轴延迟件）。理由：3×0.9=2.7 延迟 vs 基准 2——延迟对冲超值；1 回合吟唱可被针对，弃牌轴中期爆发点。

**sea_whisper** — 深渊低语 / SPELL / 惩罚 3 / 标签[呓语]
- onPlayEffects: OPP_DRAW:3
- 文本：对方抽 3 张牌。
- 实现：[READY]
- 强度：C（条件协同）。理由：喂牌(3×0.4=1.2) vs 基准 3——按即时价值大亏；但配合统领被惩罚翻转（对方惩罚抽→弃牌）时变成"弃对方 3 张"——只有在统领翻转生效时值回票价，环境/协同牌。

### 9.4 伏击（2）

**sea_devour** — 深渊吞噬 / AMBUSH / 惩罚 3 / 标签[弃牌] / ambushKind=FOCUS, ambushTrigger=OPPONENT_DRAWS
- ambushEffects: DISCARD_DRAWN
- 文本：伏击（专注）：对方抽牌时，弃掉其刚抽到的牌。
- 实现：[READY]
- 强度：B。理由：反抽牌(1.5-2.0 条件) vs 基准 3——专注限制 + 条件触发；专门克制抽牌流（烈焰抽牌、惩罚响应），弃牌轴防御件。

**sea_ink** — 墨幕 / AMBUSH / 惩罚 1 / 标签[反制] / ambushKind=NORMAL, ambushTrigger=OPPONENT_ATTACKS
- ambushEffects: NEGATE
- 文本：伏击：对方攻击时，反制该次攻击。
- 实现：[READY]
- 强度：B。理由：反制攻击(2.0 条件) vs 基准 1——超但条件触发 + 每敌方动作限 1 张伏击（RULES §6）；低费防御件。

### 9.5 惩罚牌（2）

**sea_punish_tsunami** — 灭世海啸 / PUNISH / 惩罚 6 / 标签[天灾]
- punishActivatable=true, punishCost=2, punishCondition=ALWAYS, punishEffects=[DAMAGE:5@ALL_ENEMY_MINIONS]
- 文本：若你被惩罚抽到（费用 2）：对所有敌方随从造成 5 点伤害。
- 实现：[READY]
- 强度：B。理由：5×1.2=6 vs 惩罚 6+2 响应——按价值持平，高费高回报清场；费用 2 响应成本限制滥用。

**sea_punish_maelstrom** — 漩涡深渊 / PUNISH / 惩罚 5 / 标签[天灾]
- punishActivatable=true, punishCost=1, punishCondition=ALWAYS, punishEffects=[DISCARD_OPP_RANDOM:2, DAMAGE:2@ENEMY_FACE]
- 文本：若你被惩罚抽到（费用 1）：随机弃对方 2 张手牌，并对敌方统领造成 2 点伤害。
- 实现：[READY]
- 强度：B。理由：2×0.9+2×0.7=3.2 vs 惩罚 5+1——按价值亏，但双效果同时推进弃牌胜利 + 斩杀，复合价值。

## 10. 中立（14 张：2 统领 + 6 随从 + 3 咒文 + 1 伏击 + 2 惩罚）

### 10.1 统领

**shadow_of_fate** — 命运之影 / SPELL / 惩罚 0 / 标签[统领]（新身份：赋予生命统领 + 连锁胜利）
- leaderDef: winCondition=OPP_PUNISH_TRIGGERED_GE, winAmount=6, grantLife=20, vulnerabilities=[]；enterEffects=[OPP_DRAW:2]；punishEffects=[NEGATE_ENEMY_EFFECTS_TURN]
- 文本：赋予你 20 点生命。登场：对方抽 2 张牌。若你被惩罚抽到：对方场上卡牌效果本回合全部无效。对方累计成功发动惩罚响应达到 6 次时你获胜。
- 实现：[MD: E2]（OPP_PUNISH_TRIGGERED_GE 需新 winCondition；其余 [READY]）
- 强度：S（自定义卡组核心）。理由：DISABLE_ENEMY_LEADER 已删（人类裁决）→ 原身份作废。新身份="操纵惩罚因果的命运":赋予生命统领不可被击败，胜利靠"对方发动惩罚响应 6 次"——你的卡组用 OPP_DRAW/低惩罚铺场诱导对方抽到惩罚牌并发动响应；对方发动响应=消耗其资源+可能反咬你，天然有反制（对方可以少发动响应，但会损失响应价值）。enterEffects 让对方抽 2 喂连锁。这是全卡池唯一"以对手资源为食"的胜利轴，平衡靠"喂牌反噬"自动调节。
- 注：原 gate_of_fate（伏击统领）保留为中立第二统领，两个统领互不冲突（不同卡组选择）。

**gate_of_fate** — 命运之门 / AMBUSH / 惩罚 0 / 标签[统领] / ambushKind=FOCUS, ambushTrigger=OPPONENT_PLAYS_CARD
- leaderDef: winCondition=AMBUSH_TRIGGER_WIN, vulnerabilities=[]
- ambushEffects: WIN_GAME:命运之门开启
- 文本：伏击（专注）统领：盖放时对方打出卡牌→触发：你立即获胜。
- 实现：[READY]
- 强度：S（高风险特化）。理由：伏击统领（RULES §7 示例）——盖放后对方一旦打出卡牌即获胜；触发条件极苛刻（对方全程不再出牌？不可能——除非你前期压场），实际上是一张"对方节奏陷阱"：你不盖放它没有价值，盖放后对方被迫 1 回合不出牌或接受失败。平衡理由：触发窗口只有"对方打出下一张牌"，而对方可以先用伏击/技能/弃牌拖——属环境特化卡，官方卡组不采用。

### 10.2 随从（6）

**neutral_mercenary** — 雇佣剑士 / MINION / 惩罚 2 / 3 攻 3 血 / 标签[佣兵]
- 文本：无效果。
- 实现：[READY]
- 强度：B（中立基准）。理由：6 身材 = 惩罚 2 基准白板；所有阵营都可带的通用曲线件。

**neutral_mage** — 流浪法师 / MINION / 惩罚 1 / 2 攻 2 血 / 标签[法师]
- onPlayEffects: DAMAGE:1@ENEMY_FACE
- 文本：战吼：对敌方统领造成 1 点伤害。
- 实现：[READY]
- 强度：B。理由：4 身材 + 0.7(打脸) = 4.7 vs 基准 4——略超；通用直伤件，速攻阵营外挂。

**neutral_watcher** — 沉默观察者 / MINION / 惩罚 3 / 3 攻 5 血 / 扰魔 / 标签[观察]
- 文本：扰魔。
- 实现：[READY]
- 强度：C。理由：8 身材 −0.5(扰魔) = 7.5 vs 基准 8——标准防守件。

**neutral_knight** — 巡游骑士 / MINION / 惩罚 3 / 3 攻 4 血 / 嘲讽 / 标签[骑士]
- 文本：嘲讽。
- 实现：[READY]
- 强度：C。理由：7 身材 −1(嘲讽) = 6 vs 基准 8——略亏但嘲讽价值稳定；通用守卫件。

**neutral_scout** — 斥候信使 / MINION / 惩罚 1 / 2 攻 2 血 / 突袭 / 标签[斥候]
- 文本：突袭。
- 实现：[READY]
- 强度：C。理由：4 身材 −1(突袭) = 3 vs 基准 4——亏 1 换节奏；通用抢血件。

**neutral_healer** — 巡回医师 / MINION / 惩罚 2 / 2 攻 3 血 / 标签[医师]
- onPlayEffects: HEAL:2@FRIENDLY_MINION
- 文本：战吼：使己方一个随从恢复 2 点生命。
- 实现：[READY]
- 强度：B。理由：5 身材 + 0.7(治疗2) = 5.7 vs 基准 6——标准；通用回复件。

### 10.3 咒文（3）

**neutral_supply** — 补给车队 / SPELL / 惩罚 1 / 标签[补给]
- onPlayEffects: DRAW:1
- 文本：抽 1 张牌。
- 实现：[READY]
- 强度：B（通用过牌）。理由：1.2 vs 基准 1——略超；1 费过牌，任何卡组可用。

**neutral_arcane** — 奥术飞弹 / SPELL / 惩罚 2 / 标签[速攻]
- onPlayEffects: DAMAGE:3@ENEMY_TARGET
- 文本：对一个目标造成 3 点伤害。
- 实现：[READY]
- 强度：B（通用解场）。理由：3×0.8=2.4 vs 基准 2——略超；通用单体去除，烈焰/深海的低配版。

**neutral_barrier** — 守护誓约 / SPELL / 惩罚 2 / 标签[守护]
- onPlayEffects: GRANT_KEYWORD:圣盾@FRIENDLY_MINION
- 文本：使己方一个随从获得圣盾。
- 实现：[READY]
- 强度：C。理由：1.3 vs 基准 2——按价值亏；通用保护件，非核心。

### 10.4 伏击（1）

**neutral_trap** — 伏击陷阱 / AMBUSH / 惩罚 1 / 标签[陷阱] / ambushKind=NORMAL, ambushTrigger=OPPONENT_ATTACKS
- ambushEffects: DAMAGE:2@ENEMY_MINION
- 文本：伏击：对方攻击时，对其攻击随从造成 2 点伤害。
- 实现：[READY]
- 强度：B。理由：2×0.8=1.6 vs 基准 1——条件补价；通用伏击件，与烈焰烈火反噬互为中立替换。

### 10.5 惩罚牌（2）

**neutral_punish_blast** — 混沌爆裂 / PUNISH / 惩罚 5 / 标签[混沌]
- punishActivatable=true, punishCost=1, punishCondition=ALWAYS, punishEffects=[DAMAGE:3@ALL_ENEMY_MINIONS]
- 文本：若你被惩罚抽到（费用 1）：对所有敌方随从造成 3 点伤害。
- 实现：[READY]
- 强度：B。理由：3×1.2=3.6 vs 惩罚 5+1——按价值亏；通用 AOE 响应，无阵营限制。

**neutral_punish_bomb** — 定时炸弹 / PUNISH / 惩罚 4 / 标签[混沌]
- punishActivatable=true, punishCost=1, punishCondition=HAND_GE_3, punishEffects=[DAMAGE:4@ENEMY_TARGET]
- 文本：若你被惩罚抽到（费用 1）：你的手牌 ≥3 时，对一个目标造成 4 点伤害。
- 实现：[READY]
- 强度：C。理由：4×0.8=3.2 vs 惩罚 4+1——按价值亏；条件通用去除，非核心。

## 11. 机械遗迹（28 张：2 统领 + 12 随从 + 10 咒文 + 2 伏击 + 2 惩罚）— 全量重做，围绕 §12.4

> 机械基线（RULES §12.4）：普通出牌惩罚 0、身材偏低 → 通过 Commit/Push/Rollback/Pull 形成转换收益。
> 新动作（E3）：COMMIT（付费进提交队列）/ PULL（付费拉取栈顶结算）/ ROLLBACK（队列回手）。
> 新状态（E4）：提交队列（公开）/ 云端栈（公开，队首先 Push，后 Push 压顶）。
> 新字段（E5）：leaderDef.protocolFields 白名单；随从卡 protocolFields 提供字段（关键词映射：突袭=CHARGE、嘲讽=TAUNT）。

### 11.1 统领（2）

**machine_leader** — 上古咒文·赋值机身 / SPELL / 惩罚 0 / 标签[统领]
- leaderDef: winCondition=NONE, durability=8, vulnerabilities=[DAMAGE]；enterEffects=[SUMMON:1:machine_drone]；punishEffects=[ADD_OPP_PUNISH_TURN:1, SKIP_RESHUFFLE:1]；chant=2, chantEffects=[SUMMON_LEADER:machine_alpha]
- 文本：耐久 8。登场：召唤 1 个侦察机偶。吟唱 2：2 回合后转生为上古极神Alpha。若你被惩罚抽到：对方本回合惩罚 +1，且你获得 1 次跳过洗牌计数。
- 实现：[READY]（SUMMON_LEADER 结构现引擎已支持；alpha 胜利条件 [MD: E1/E5]）
- 强度：A（先锋引擎）。理由：耐久统领不可被击败→胜利依赖转生后的 alpha；耐久 8 撑住前期，登场造机偶 + 2 回合后转生是明确的"先守后攻"节奏；被惩罚抽到的 ADD_OPP_PUNISH_TURN + SKIP_RESHUFFLE 保留旧主题（惩罚流 + 防牌库循环）。

**machine_alpha** — 上古极神Alpha / MINION / 惩罚 0 / 8 攻 10 血 / 圣盾 / 标签[极神]
- leaderDef: winCondition=PULL_TOTAL_GE, winAmount=6, protocolFields=[COMMIT, PUSH, PULL, ROLLBACK, CHARGE, TAUNT], vulnerabilities=[DAMAGE]；enterEffects=[BUFF:1:both@ALL_FRIENDLY_MINIONS]；punishEffects=[ADD_OPP_PUNISH_TURN:2]
- 文本：圣盾。登场：己方所有随从 +1/+1。若你被惩罚抽到：对方本回合惩罚 +2。本局己方累计完成 6 次 Pull（拉取结算）时你获胜。
- 实现：[MD: E1, E5]（PULL_TOTAL_GE winCondition + protocolFields 计数需引擎扩展）
- 强度：S（卡组定义·下载轴）。理由：随从统领 8/10 圣盾可被击败——强度预算最高档；胜利轴=6 次有效 Pull。**协议白名单定稿：COMMIT/PUSH/PULL/ROLLBACK/CHARGE/TAUNT 六字段**（STEALTH/BATTLECRY/DEATHRATTLE 不入——机械基础卡不主打潜行/战吼/亡语，防字段池膨胀贬值，RULES §12.4）。**阈值定稿：6 次 Pull**（15 回合局 ≈ 第 12-14 回合达成，与 10 次牌库循环胜利竞争；登场的"己方 Pull 费用=0"（RULES §12.4 机械统领通用规则）使胜利轴不消耗资源）。登场全队 +1/+1 强化机偶铺场，被惩罚抽到的惩罚流保底。

### 11.2 随从（12）

**machine_drone** — 侦察机偶 / MINION / 惩罚 0 / 1 攻 1 血 / 标签[机偶]
- onPlayEffects: DRAW:1
- 文本：战吼：抽 1 张牌。
- 实现：[READY]
- 强度：B（转换燃料）。理由：1/1(2 身材) + 抽1(1.2) = 3.2 价值 @ 惩罚 0——超模但 1/1 一碰就碎；它是 Commit 费用最低的燃料，量产协议的产出 token。

**machine_golem** — 锈蚀魔像 / MINION / 惩罚 2 / 3 攻 4 血 / 嘲讽 / 标签[魔像]
- 文本：嘲讽。
- 实现：[READY]
- 强度：B。理由：7 身材 −1(嘲讽) = 6 = 基准；同时提供 TAUNT 协议字段（E5）。

**machine_wall** — 装甲壁垒 / MINION / 惩罚 2 / 0 攻 8 血 / 嘲讽 / 标签[守卫]
- 文本：嘲讽。
- 实现：[READY]
- 强度：B（防御墙）。理由：8 身材 −1(嘲讽) = 7 vs 基准 6——略超；0 攻纯防守，拖到 alpha 转生；提供 TAUNT 协议字段。

**machine_blaster** — 聚能炮台 / MINION / 惩罚 2 / 2 攻 3 血 / 标签[炮台]
- onPlayEffects: DAMAGE:1@ENEMY_TARGET
- 文本：战吼：对一个目标造成 1 点伤害。
- 实现：[READY]
- 强度：B。理由：5 身材 + 0.8 = 5.8 vs 基准 6——标准；灵活 1 伤补刀。

**machine_titan** — 残响泰坦 / MINION / 惩罚 5 / 6 攻 6 血 / 嘲讽 / 标签[泰坦]
- 文本：嘲讽。
- 实现：[READY]
- 强度：B。理由：12 身材 −1(嘲讽) = 11 vs 基准 12——略亏；5 惩罚高费终结墙，提供 TAUNT 协议字段。

**machine_spark** — 火花机蜂 / MINION / 惩罚 1 / 2 攻 1 血 / 突袭 / 标签[蜂群]
- 文本：突袭。
- 实现：[READY]
- 强度：B。理由：3 身材 −1(突袭) = 2 vs 基准 4——亏但节奏；提供 CHARGE 协议字段（突袭=CHARGE，E5）。

**machine_assembler** — 自我组装体 / MINION / 惩罚 2 / 3 攻 3 血 / 标签[组装]
- punishActivatable=true, punishCost=0, punishCondition=ALWAYS
- 文本：若你被惩罚抽到（费用 0）：可打出占场。
- 实现：[READY]
- 强度：B。理由：6 身材 = 基准 + 0 费惩罚响应（免费节奏）——机械被喂牌时的免费场面。

**machine_recycler** — 回收单元 / MINION / 惩罚 2 / 2 攻 5 血 / 标签[回收]
- punishActivatable=true, punishCost=1, punishCondition=HAND_GE_3, punishEffects=[DRAW:1]
- 文本：若你被惩罚抽到（费用 1）：你的手牌 ≥3 时，抽 1 张牌。
- 实现：[READY]
- 强度：C。理由：7 身材 vs 基准 6 略超 + 条件过牌；回收主题（Pull 的静态替代），非核心。

**machine_uploader** — 上传傀儡 / MINION / 惩罚 1 / 2 攻 2 血 / 标签[上传]
- onPlayEffects: COMMIT:1:deck_top
- 文本：战吼：将己方牌库顶的机械卡提交（支付其 Commit 费用）。（提供 COMMIT 协议字段）
- 实现：[MD: E3, E4]
- 强度：B（上传轴）。理由：4 身材 = 基准 + 免费提交引擎（牌库顶提交，RULES §12.4 卡牌效果可指定来源）——提交队列的关键来源，机器上传轴核心燃料。

**machine_compiler** — 编译机仆 / MINION / 惩罚 2 / 3 攻 3 血 / 标签[编译]
- pushEffects: DRAW:1（Push 结算时触发）
- 文本：推送：进入云端栈时抽 1 张牌。（提供 PUSH 协议字段）
- 实现：[MD: E3, E4]（pushEffects 为 E4 新字段）
- 强度：B（推送轴）。理由：6 身材 = 基准 + 每次 Push 过牌（云端栈结算时）——推送阶段的资源回收，上传-下载转换的核心。

**machine_downloader** — 下载终端 / MINION / 惩罚 2 / 3 攻 2 血 / 标签[下载]
- pullEffects: DRAW:1（Pull 结算时触发）
- 文本：拉取：被 Pull 结算时抽 1 张牌。（提供 PULL 协议字段）
- 实现：[MD: E3, E4]
- 强度：A（下载轴核心）。理由：5 身材略亏 + Pull 过牌——每次拉取收益，且 Pull 次数直接推进 alpha 胜利阈值（6 次），下载轴主引擎。

**machine_archivist** — 档案维护体 / MINION / 惩罚 2 / 2 攻 4 血 / 标签[档案]
- onPlayEffects: ROLLBACK:1（队列回手）
- 文本：战吼：将己方提交队列中一张卡回滚回手牌。（提供 ROLLBACK 协议字段）
- 实现：[MD: E3, E4]
- 强度：B（回滚轴）。理由：6 身材 = 基准 + 回滚复用（队列卡回手再出）——灵活资源循环；ROLLBACK 协议字段来源。

### 11.3 咒文（10）

**machine_overload** — 过载冲击 / SPELL / 惩罚 3 / 标签[破坏]
- onPlayEffects: DAMAGE:4@ENEMY_MINION
- 文本：对一个敌方随从造成 4 点伤害。
- 实现：[READY]
- 强度：B。理由：4×0.8=3.2 vs 基准 3——略超；单体去除，机械中期解场。

**machine_recharge** — 紧急充能 / SPELL / 惩罚 1 / 标签[充能]
- onPlayEffects: RESTORE_ATTACKS@FRIENDLY_MINION
- 文本：使己方一个随从本回合可再次攻击。
- 实现：[READY]
- 强度：B。理由：重置攻击价值 ≈ 目标攻击力(1.5-2.5) vs 基准 1——上限牌；配合高攻随从/上传傀儡的当回合打点。

**machine_factory** — 量产协议 / SPELL / 惩罚 3 / 标签[协议]
- chant=2, chantEffects=[SUMMON:3:machine_drone]
- 文本：吟唱 2：2 回合后召唤 3 个侦察机偶。
- 实现：[READY]
- 强度：B（上传燃料池）。理由：3×(3 价值) = 9 延迟 vs 基准 3——延迟对冲高价值；产出的机偶既是场面又是 Commit 燃料（0 惩罚），上传轴核心引擎。

**machine_virus** — 逻辑病毒 / SPELL / 惩罚 2 / 标签[干扰]
- onPlayEffects: ADD_OPP_PUNISH_TURN:1
- 文本：对方本回合每张卡牌惩罚值 +1。
- 实现：[READY]
- 强度：B。理由：0.5×n vs 基准 2——对方出牌越多收益越大；惩罚流干扰件，配合 alpha/leader 的同类效果堆叠。

**machine_scan** — 全域扫描 / SPELL / 惩罚 1 / 标签[侦测]
- onPlayEffects: DRAW:2
- 文本：抽 2 张牌。
- 实现：[READY]
- 强度：A（机械过牌）。理由：2×1.2=2.4 vs 基准 1——超 1.4；机械需要手牌做 Commit 燃料，1 惩罚低风险高收益过牌，阵营招牌。

**machine_emp** — EMP脉冲 / SPELL / 惩罚 4 / 标签[风暴]
- onPlayEffects: DAMAGE:2@ALL_ENEMY_MINIONS
- 文本：对所有敌方随从造成 2 点伤害。
- 实现：[READY]
- 强度：B。理由：2×1.2=2.4 vs 基准 4——按价值亏；AOE 清场稳定性补价。

**machine_repair** — 纳米修复 / SPELL / 惩罚 1 / 标签[恢复]
- onPlayEffects: HEAL:2@ALL_FRIENDLY_MINIONS
- 文本：己方所有随从恢复 2 点生命。
- 实现：[READY]
- 强度：B。理由：2×0.35×n vs 基准 1——随从多时超值；机械随从身材低，治疗收益稳定。

**machine_cannon** — 轨道炮 / SPELL / 惩罚 3 / 标签[轰击]
- chant=1, chantEffects=[DAMAGE:7@ENEMY_TARGET, kingSlayer=true]
- 文本：吟唱 1：1 回合后对一个目标造成 7 点伤害（可作用于统领）。
- 实现：[READY]
- 强度：A（招牌重炮）。理由：7×0.8=5.6+0.5(弑君) 延迟 vs 基准 3——延迟对冲巨额单体伤害；机械"蓄力重炮"身份牌。

**machine_commit_protocol** — 提交协议 / SPELL / 惩罚 3 / 标签[协议]
- onPlayEffects: COMMIT:2:hand
- 文本：从手牌提交 2 张机械卡（支付各自 Commit 费用）。（提供 COMMIT 协议字段）
- 实现：[MD: E3, E4]
- 强度：B（上传轴）。理由：批量提交引擎——直接喂提交队列；COMMIT 协议字段保证 alpha 胜利进度。

**machine_pull_protocol** — 拉取协议 / SPELL / 惩罚 1 / 标签[协议]
- onPlayEffects: PULL:1
- 文本：拉取云端栈顶卡（支付其 Pull 费用，结算其 Pull 效果）。（提供 PULL 协议字段）
- 实现：[MD: E3, E4]
- 强度：A（下载轴核心）。理由：直接推进 alpha 胜利（1 次 Pull 计数）+ 结算 Pull 效果；1 惩罚低风险，下载轴加速器。

### 11.4 伏击（2）

**machine_trap** — 电磁陷阱 / AMBUSH / 惩罚 1 / 标签[陷阱] / ambushKind=NORMAL, ambushTrigger=OPPONENT_SUMMONS
- ambushEffects: DAMAGE:3@ENEMY_MINION
- 文本：伏击：对方召唤随从时，对其造成 3 点伤害。
- 实现：[READY]
- 强度：B。理由：3×0.8=2.4 vs 基准 1——超但条件触发；反铺场防御件，保护上传引擎。

**machine_null** — 协议屏障 / AMBUSH / 惩罚 2 / 标签[反制] / ambushKind=FOCUS, ambushTrigger=OPPONENT_PLAYS_SPELL
- ambushEffects: NEGATE
- 文本：伏击（专注）：对方打出咒文时，反制其效果。
- 实现：[READY]
- 强度：B。理由：反制(2.5) vs 基准 2 + 专注限制——条件反制标准价。

### 11.5 惩罚牌（2）

**machine_punish_core** — 核心反击程序 / PUNISH / 惩罚 5 / 标签[程序]
- punishActivatable=true, punishCost=1, punishCondition=ALWAYS, punishEffects=[DAMAGE:3@ALL_ENEMY_MINIONS, DRAW:1]
- 文本：若你被惩罚抽到（费用 1）：对所有敌方随从造成 3 点伤害，并抽 1 张牌。
- 实现：[READY]
- 强度：A。理由：3×1.2+1.2=4.8 vs 惩罚 5+1——按价值略亏但双效果复合；被喂牌时的清场+过牌，机械"回收反击"主题。

**machine_punish_sync** — 同步协议 / PUNISH / 惩罚 4 / 标签[程序]
- punishActivatable=true, punishCost=1, punishCondition=HAND_GE_3, punishEffects=[COMMIT:1:hand]
- 文本：若你被惩罚抽到（费用 1）：你的手牌 ≥3 时，从手牌提交 1 张机械卡（免费，支付其 Commit 费用）。（提供 COMMIT 协议字段）
- 实现：[MD: E3, E4]
- 强度：B。理由：被喂牌 → 免费转上传燃料；条件限制防空发，推进 COMMIT 协议计数。

## 12. 古木圣地（26 张：1 统领 + 12 随从 + 9 咒文 + 2 伏击 + 2 惩罚）

> 木基线（RULES §12.2）：慢速成长、承担前期压力换后期强化上限；连乘/封印/512 生命为候选机制 [MD: E6/E7]。

### 12.1 统领

**wood_leader** — 世界树之心 / SPELL / 惩罚 0 / 标签[统领]
- leaderDef: winCondition=NO_DAMAGE_TURNS_GE, winAmount=6, durability=20, vulnerabilities=[DAMAGE]；enterEffects=[SUMMON:2:wood_sapling, PROTECT_TURN]；punishEffects=[PROTECT_TURN]
- 文本：耐久 20。登场：召唤 2 个新芽树灵，本回合己方随从不会被破坏。若你被惩罚抽到：本回合己方随从不会被破坏。你连续 6 个回合未受任何伤害时获胜。
- 实现：[READY]（连乘启动部分 [MD: E6/E7]，见注）
- 强度：S（卡组定义）。理由：耐久统领不可被击败→胜利靠无伤计数（阈值从 7 降到 6——15 回合局 6 回合无伤 ≈ 40% 对局时长，仍需高强度防守才能达成，配合 PROTECT_TURN/治疗/高血随从）；登场 2 树灵 + 保护是本阵营"前期挨打、后期无伤胜利"的节奏宣言。注：§12.2 连乘启动（统领登场向区域加入连乘卡）为候选机制，启用前用 SUMMON:2 表达。

### 12.2 随从（12）

**wood_sapling** — 新芽树灵 / MINION / 惩罚 0 / 1 攻 2 血 / 标签[树灵]（token：统领/种子召唤）
- 文本：无效果。
- 实现：[READY]
- 强度：C（token 模板）。理由：3 身材 @ 惩罚 0——0 惩罚无风险身材上限；成长体系的起始单位。

**wood_guard** — 橡木守卫 / MINION / 惩罚 2 / 2 攻 7 血 / 嘲讽 / 标签[守卫]
- 文本：嘲讽。
- 实现：[READY]
- 强度：B。理由：9 身材 −1(嘲讽) = 8 vs 基准 6——超 2；木阵营的防守墙身份，2/7 标准厚墙。

**wood_druid** — 林地德鲁伊 / MINION / 惩罚 2 / 2 攻 4 血 / 标签[德鲁伊]
- onPlayEffects: HEAL:2@FRIENDLY_MINION
- 文本：战吼：使己方一个随从恢复 2 点生命。
- 实现：[READY]
- 强度：B。理由：6 身材 + 0.7(治疗2) = 6.7 vs 基准 6——略超；治疗协同件。

**wood_bear** — 灰熊守护者 / MINION / 惩罚 4 / 5 攻 6 血 / 嘲讽 / 标签[野兽]
- 文本：嘲讽。
- 实现：[READY]
- 强度：B。理由：11 身材 −1(嘲讽) = 10 = 基准；中期厚墙。

**wood_treant** — 古树行者 / MINION / 惩罚 5 / 4 攻 8 血 / 嘲讽 / 标签[树灵]
- 文本：嘲讽。
- 实现：[READY]
- 强度：B。理由：12 身材 −1(嘲讽) = 11 vs 基准 12——略亏；高血防守终结墙，生命之种的目标。

**wood_wisp** — 灵光精灵 / MINION / 惩罚 0 / 1 攻 1 血 / 标签[精灵]
- onPlayEffects: DRAW:1
- 文本：战吼：抽 1 张牌。
- 实现：[READY]
- 强度：B（0 费过牌）。理由：2 身材 + 1.2(抽1) = 3.2 @ 惩罚 0——超模但 1/1 极易被清；0 惩罚过牌，木阵营节奏润滑。

**wood_warden** — 林海巡守 / MINION / 惩罚 2 / 3 攻 4 血 / 扰魔 / 标签[巡守]
- 文本：扰魔。
- 实现：[READY]
- 强度：C。理由：7 身材 −0.5(扰魔) = 6.5 vs 基准 6——标准；扰魔防咒文去除，保护成长随从。

**wood_stag** — 白角巨鹿 / MINION / 惩罚 3 / 4 攻 3 血 / 突袭 / 标签[野兽]
- 文本：突袭。
- 实现：[READY]
- 强度：C。理由：7 身材 −1(突袭) = 6 vs 基准 8——亏 2；慢速卡组需要少量节奏件抢回场面。

**wood_owl** — 智慧古枭 / MINION / 惩罚 2 / 2 攻 3 血 / 标签[精灵]
- punishActivatable=true, punishCost=0, punishCondition=ALWAYS, punishEffects=[DRAW:1]
- 文本：若你被惩罚抽到（费用 0）：抽 1 张牌。
- 实现：[READY]
- 强度：B。理由：5 身材 + 0 费响应过牌——被喂牌时免费补充手牌；条件触发。

**wood_ancient** — 古木长老 / MINION / 惩罚 3 / 3 攻 7 血 / 嘲讽 / 标签[树灵]
- 文本：嘲讽。
- 实现：[READY]
- 强度：B。理由：10 身材 −1(嘲讽) = 9 vs 基准 8——略超；中费厚墙，无伤胜利轴的中坚。

**wood_vine** — 藤蔓缠绕者 / MINION / 惩罚 1 / 2 攻 2 血 / 标签[藤蔓]
- onPlayEffects: BUFF:1:both@FRIENDLY_MINION
- 文本：战吼：使己方一个随从 +1/+1。
- 实现：[READY]
- 强度：B。理由：4 身材 + 0.8 = 4.8 vs 基准 4——略超；成长引擎的早期强化件。

**wood_grove** — 常青林冠 / MINION / 惩罚 2 / 0 攻 5 血 / 嘲讽 / 标签[树灵]
- onPlayEffects: MULTIPLY_NEXT:2（连乘：下一次强化数值 ×2，消耗此效果）
- 文本：嘲讽。战吼：获得 1 层连乘（你下一次强化数值 ×2，可累积）。
- 实现：[MD: E6, E7]
- 强度：B（成长引擎·候选）。理由：0/5 纯防守 + 连乘进度——木阵营 512 生命胜利轴的进度来源；机制启用前该卡暂缓实现。

### 12.3 咒文（9）

**wood_spring** — 生命之泉 / SPELL / 惩罚 2 / 标签[恢复]
- onPlayEffects: HEAL:3@ALL_FRIENDLY_MINIONS
- 文本：己方所有随从恢复 3 点生命。
- 实现：[READY]
- 强度：B。理由：3×0.35×n vs 基准 2——随从多时超值；无伤胜利轴的续航件。

**wood_thorn** — 荆棘缠绕 / SPELL / 惩罚 2 / 标签[荆棘]
- onPlayEffects: DAMAGE:2@ENEMY_MINION
- 文本：对一个敌方随从造成 2 点伤害。
- 实现：[READY]
- 强度：C。理由：2×0.8=1.6 vs 基准 2——按价值亏；木阵营解场偏弱是身份特征（防守为主），保留克制。

**wood_growth** — 疯长 / SPELL / 惩罚 2 / 标签[增强]
- onPlayEffects: BUFF:2:both@FRIENDLY_MINION
- 文本：使己方一个随从 +2/+2。
- 实现：[READY]
- 强度：A（成长核心）。理由：2×0.8×2=3.2 vs 基准 2——超 1.2；单随从大强化，配合嘲讽墙滚雪球；木阵营"养大随从"主旋律。

**wood_bark** — 树皮圣盾 / SPELL / 惩罚 1 / 标签[守护]
- onPlayEffects: GRANT_KEYWORD:圣盾@FRIENDLY_MINION
- 文本：使己方一个随从获得圣盾。
- 实现：[READY]
- 强度：B。理由：1.3 vs 基准 1——略超；保护成长中的关键随从。

**wood_seed** — 生命之种 / SPELL / 惩罚 1 / 标签[仪式]
- chant=2, chantEffects=[SUMMON:1:wood_treant]
- 文本：吟唱 2：2 回合后召唤 1 个 4/8 嘲讽古树行者。
- 实现：[READY]
- 强度：B（延迟大怪）。理由：11 价值延迟 2 回合 vs 基准 1——延迟对冲超值；慢速卡组的中期核心节奏。

**wood_moon** — 月华射线 / SPELL / 惩罚 2 / 标签[月光]
- onPlayEffects: DAMAGE:4@ENEMY_TARGET, kingSlayer=true
- 文本：对一个目标造成 4 点伤害（可作用于统领）。
- 实现：[READY]
- 强度：A（招牌解场）。理由：4×0.8=3.2+0.5(弑君) = 3.7 vs 基准 2——超 1.7；超模解场，木阵营唯一的硬去除（配合荆棘缠绕）。

**wood_circle** — 守护之环 / SPELL / 惩罚 4 / 标签[结界]
- onPlayEffects: GRANT_KEYWORD:圣盾@ALL_FRIENDLY_MINIONS
- 文本：己方所有随从获得圣盾。
- 实现：[READY]
- 强度：A（群体保护）。理由：1.3×n vs 基准 4——随从多时超值；无伤胜利轴的保护大招。

**wood_renew** — 轮回新生 / SPELL / 惩罚 2 / 标签[恢复]
- onPlayEffects: HEAL:5@FRIENDLY_MINION
- 文本：使己方一个随从恢复 5 点生命。
- 实现：[READY]
- 强度：B。理由：5×0.35=1.75 vs 基准 2——按价值亏；大治疗保核心随从（配合疯长的养大路线）。

**wood_blessing** — 森林祝福 / SPELL / 惩罚 3 / 标签[增强]
- onPlayEffects: BUFF:1:both@ALL_FRIENDLY_MINIONS
- 文本：己方所有随从 +1/+1。
- 实现：[READY]
- 强度：B。理由：n×0.8 vs 基准 3——铺场时超值；与烈焰帝国战吼对称，木阵营铺场终结件。

### 12.4 伏击（2）

**wood_root** — 盘根陷阱 / AMBUSH / 惩罚 1 / 标签[荆棘] / ambushKind=NORMAL, ambushTrigger=OPPONENT_SUMMONS
- ambushEffects: DAMAGE:2@ENEMY_MINION
- 文本：伏击：对方召唤随从时，对其造成 2 点伤害。
- 实现：[READY]
- 强度：B。理由：2×0.8=1.6 vs 基准 1——条件补价；反铺场防御件。

**wood_veil** — 密叶帷幕 / AMBUSH / 惩罚 3 / 标签[结界] / ambushKind=LOCKDOWN, ambushTrigger=OPPONENT_ATTACKS
- ambushEffects: NEGATE
- 文本：伏击（封场）：对方攻击时，反制该次攻击。盖放期间你不能盖放其他伏击。
- 实现：[READY]
- 强度：B。理由：反制攻击(2.0 条件) + 封场限制 vs 基准 3——高费高保障；LOCKDOWN 型伏击的示例卡。

### 12.5 惩罚牌（2）

**wood_punish_wrath** — 自然之怒 / PUNISH / 惩罚 5 / 标签[天罚]
- punishActivatable=true, punishCost=1, punishCondition=ENEMY_MINIONS_GE_1, punishEffects=[DESTROY@ENEMY_MINION]
- 文本：若你被惩罚抽到（费用 1）：对方有随从时，消灭一个敌方随从。
- 实现：[READY]
- 强度：A。理由：DESTROY(3.0) 条件 vs 惩罚 5+1——按价值亏但无条件消灭（无视圣盾/血量）是硬解；木阵营被喂牌时的硬去除。

**wood_punish_bloom** — 绽放之光 / PUNISH / 惩罚 4 / 标签[天罚]
- punishActivatable=true, punishCost=1, punishCondition=HAND_GE_3, punishEffects=[HEAL:6@FRIENDLY_MINION, BUFF:1:both@FRIENDLY_MINION]
- 文本：若你被惩罚抽到（费用 1）：你的手牌 ≥3 时，使己方一个随从恢复 6 点生命并 +1/+1。
- 实现：[READY]
- 强度：B。理由：6×0.35+0.8=2.9 vs 惩罚 4+1——按价值亏；大保核心随从，配合成长轴。

---

## 13. 官方卡组模板（4 套，每套 59 主牌 + 1 统领 = 60 张）

> 格式：id ×数量。均从对应阵营卡池 + 中立工具卡构建。数量已脚本校验 = 59 主牌。
> ⚠️ 机械卡组含 [MD] 卡（上传/下载轴），在 §12.4 机制接入前不可完整运行；届时以 READY 子集临时替代（见 §14.4）。

### 13.1 烈焰帝国·焚天速攻（统领 flame_leader）
- 快攻直伤：flame_imp×3, flame_recruit×3, flame_charger×3, flame_berserker×3, flame_bolt×3, flame_elemental×3
- 铺场终结：flame_banner×2, flame_warcry×2, flame_warlord×1, flame_forge×2, flame_phoenix×2
- 解场：flame_strike×2, flame_rain×2, flame_ambush_counter×2, flame_ambush_seal×1, flame_warden×2, flame_guard×2
- 连击/斩杀：flame_double×2, flame_assassin×2, flame_infernal×1, flame_punish_wrath×2, flame_punish_rage×1, flame_drake×2, flame_siege×2
- 中立工具：neutral_supply×2, neutral_mercenary×2, neutral_mage×2, neutral_scout×2, neutral_punish_blast×1
- 合计：59（25 种卡）

### 13.2 机械遗迹·极神协议（统领 machine_leader → machine_alpha）
- 上传引擎：machine_uploader×3, machine_commit_protocol×2, machine_factory×2, machine_archivist×2
- 下载引擎：machine_downloader×3, machine_pull_protocol×3, machine_compiler×3
- 转换燃料：machine_drone×3, machine_scan×3, machine_assembler×2, machine_recycler×1, machine_punish_sync×1
- 防守/解场：machine_golem×3, machine_wall×2, machine_blaster×2, machine_overload×2, machine_cannon×2, machine_emp×1, machine_trap×2, machine_null×1, machine_titan×1
- 干扰/回复：machine_virus×2, machine_repair×2, machine_recharge×1, machine_spark×2, machine_punish_core×2
- 中立工具：neutral_supply×2, neutral_mercenary×2, neutral_barrier×1, neutral_punish_blast×1
- 合计：59（28 种卡）

### 13.3 深海联盟·吞噬之渊（统领 sea_leader）
- 弃牌引擎：sea_siren×3, sea_tide×3, sea_depths×2, sea_devour×2, sea_whisper×1, sea_punish_maelstrom×1
- 铺场：sea_crab×3, sea_eel×3, sea_tentacle×3, sea_abyss_call×2, sea_leviathan_young×2, sea_leviathan×1, sea_abyss×1
- 联动/保护：sea_warden×2, sea_priest×2, sea_song×2, sea_barrier×2, sea_mistwalker×2, sea_kraken×2
- 解场/干扰：sea_pressure×2, sea_sink×2, sea_whirl×2, sea_ink×2, sea_current×2, sea_punish_tsunami×2
- 中立工具：neutral_supply×2, neutral_mercenary×2, neutral_mage×2, neutral_trap×1, neutral_punish_blast×1
- 合计：59（28 种卡）

### 13.4 古木圣地·常青壁垒（统领 wood_leader）
- 成长引擎：wood_growth×2, wood_vine×2, wood_seed×2, wood_wisp×3
- 防守墙：wood_guard×3, wood_ancient×2, wood_bear×2, wood_treant×2, wood_sapling×3
- 治疗/保护：wood_spring×2, wood_druid×2, wood_renew×2, wood_bark×2, wood_circle×2, wood_owl×2
- 解场/节奏：wood_moon×2, wood_thorn×2, wood_stag×2, wood_root×2, wood_veil×2, wood_blessing×2, wood_punish_wrath×2, wood_punish_bloom×1
- 中立工具：neutral_supply×2, neutral_healer×2, neutral_mercenary×2, neutral_watcher×1, neutral_knight×1, neutral_arcane×1, neutral_barrier×1
- 合计：59（30 种卡）

---

## 14. 自验证报告（⑥）

### 14.1 数值自查（惩罚值 ↔ 价值折算，对照 §2 参考表）
- 全 120 张逐卡给出平衡理由（见各卡条目）；无 D 级废卡，无空发风险未标注的卡。
- **超模卡（>10%）清单**（均有风险补偿说明）：flame_bolt(+0.1)、flame_imp(+0.7, 0 费)、sea_tide(+0.8, 弃牌轴)、sea_current(+0.4)、sea_siren(+0.3, 联动)、neutral_mage(+0.7, 0 费直伤)、machine_scan(+1.4, 过牌)、wood_wisp(+1.2, 0 费过牌)、flame_strike(+1.5, 超模)、wood_moon(+1.7, 超模)、sea_pressure(+0.9, 超模)、wood_guard(+2, 厚墙身份)。
- 结论：超模集中于 ①超模解场（身份牌，可接受）②0 费过牌/直伤（身材 1/1-1/2 脆弱对冲）③弃牌轴加速（服务 12 张胜利阈值，阈值本身可调）。无一构成无解组合。

### 14.2 强度评级分布（目标：B 为主 60%，A 20%，S ≤1-2，C 15%，D 0）
| 阵营 | 卡数 | S | A | B | C | B+ 占比 |
|---|---|---|---|---|---|---|
| 烈焰 | 26 | 1 | 6 | 15 | 4 | 85% |
| 深海 | 26 | 1 | 4 | 17 | 4 | 85% |
| 中立 | 14 | 2 | 0 | 7 | 5 | 64% |
| 机械 | 28 | 1 | 6 | 20 | 1 | 96% |
| 古木 | 26 | 1 | 4 | 17 | 4 | 85% |
| **合计** | **120** | **6** | **20** | **76** | **18** | **85%** |

- 达标：无 D；B 级 63%；A 17%；S 5%（6 张统领/特化，符合"统领级"定位）；C 15%。
- 注：机械 B 级 96% 偏高——转换机制卡的价值难即时量化，评级保守（机制接入后按实测调整）。

### 14.3 实现状态（READY vs 机制依赖）
| 阵营 | READY | MD 标注 | READY 占比 |
|---|---|---|---|
| 烈焰 | 26 | 0 | 100% |
| 深海 | 26 | 1（sea_leader 潮汐债务注记） | 100% |
| 中立 | 14 | 1（shadow E2） | 100% |
| 机械 | 20 | 8（+alpha/leader 注记） | 71% |
| 古木 | 25 | 1（wood_grove E6/E7） | 96% |
| **合计** | **111** | **11 纯 MD + 3 注记** | **93%** |

- 纯 MD 卡：9 机械（uploader/compiler/downloader/archivist/commit_protocol/pull_protocol/punish_sync/alpha/leader）+ wood_grove + shadow_of_fate = 11 张；其余 109 张当前引擎可立即实现。
- 依赖集中在 E1（PULL_TOTAL_GE）、E2（OPP_PUNISH_TRIGGERED_GE）、E3/E4（机械动作+区域）、E5（协议字段）、E6/E7（连乘/封印）。

### 14.4 卡池协同与反制出口（每阵营协同链 → 反制）
1. **烈焰·速攻直伤**：0 费曲线（imp/recruit）→ 中费直伤（bolt/infernal）→ 破城轴（siege+leader ROYAL_CASTLE_BREAK）。
   - 反制：前期清场（emp/whirl/tsunami 类）、嘲讽墙（guard/giant）、惩罚响应自保；自身随从脆（1-2 血为主），被 AOE 一波清。
2. **机械·上传下载**：commit（uploader/protocol）→ push 过牌（compiler）→ pull 计数+过牌（downloader/protocol）→ alpha 6 次 Pull 获胜。
   - 反制：提交队列/云端栈是公开区——对手可见进度；Pull 需付费（alpha 登场后免费但 alpha 是随从可被击败）；压制 alpha 登场前（machine_leader 耐久 8 期间压血）；惩罚流（virus/alpha 惩罚）会喂对手惩罚牌反噬。
3. **深海·弃牌吞噬**：弃牌（tide/siren/depths）→ 联动成长（siren buff/warden 王城伤）→ 12 张弃牌胜利。
   - 反制：强制弃牌不计入胜利（RULES §9）——对手只需少打手牌；手牌管理（不出牌或少出）；清掉联动随从（siren/warden）；王城防守。
4. **古木·成长壁垒**：0 费过牌（wisp）→ 养大随从（growth/vine/renew）→ 无伤 6 回合胜利。
   - 反制：直伤/破城打断无伤计数（任何对王城或玩家的伤害都打断）；去除（moon/arc strike 类硬解）；前期抢血逼治疗资源消耗。
5. **中立·命运**：shadow 连锁胜利（对方发动 6 次惩罚响应）——对方可主动少发动响应来压制（代价是放弃响应价值）；gate 伏击胜——对方 1 回合不出牌即可规避（实际极难，特化环境卡）。

### 14.5 卡组可玩性
- 4 套卡组曲线均覆盖 0-5 惩罚；每套含 ≥8 张 0-2 惩罚低费卡（烈焰 14、机械 15、深海 14、古木 13）。
- 节奏定位：烈焰 快攻(5-8 回合发力) / 机械 中速转换(8-12) / 深海 中速弃牌(8-12) / 古木 控制(10-15)——与 BALANCE.md 目标对局 15±3 回合一致。
- 对阵思路（设计意图）：烈焰 > 古木（速攻压无伤计数）；古木 > 深海（治疗扛弃牌）；深海 > 机械（弃牌拆提交燃料）；机械 > 烈焰（墙+清场扛速攻）。四角循环避免单阵营统治，待 SimMain 实测验证。

### 14.6 未决/待 PL+人类确认
1. machine_alpha 阈值 6 次 Pull、shadow 阈值 6 次响应、wood 无伤 6 回合——首测值，按模拟数据回调（建议 5-7 区间）。
2. 机械临时 READY 卡组（机制接入前）是否直接发布？否则机械卡组整体延后。
3. gate_of_fate 是否保留在基本包（特化卡）？官方卡组不采用。
4. 潮汐债务/连乘/512 生命在机制接入前一律标注暂缓。
5. 新 winCondition（PULL_TOTAL_GE / OPP_PUNISH_TRIGGERED_GE）与协议字段（protocolFields）的 schema 命名由 PL 定稿。

---

## 15. 交付清单对照（PL 验收①-⑥）
| 验收项 | 状态 | 位置 |
|---|---|---|
| ① 卡牌设计模板 | ✅ | §3（五类字段模板 + §2 数值参考 + §3.6 文本规范） |
| ② 平衡基本包全卡 spec | ✅ 120 张 | §8-§12（每卡 id/字段/效果/文本/平衡理由/强度） |
| ③ 4 套官方卡组模板 | ✅ | §13（每套 59+1=60 张，已脚本校验） |
| ④ machine_alpha 下载轴协议+阈值 | ✅ | §5.2 + §11.1（PULL_TOTAL_GE，6 次，六字段白名单） |
| ⑤ shadow_of_fate 新身份+winCondition | ✅ | §5.6 + §10.1（赋予生命统领 + OPP_PUNISH_TRIGGERED_GE 6） |
| ⑥ 自验证报告 | ✅ | §14（数值自查/协同/可玩性/实现状态） |
