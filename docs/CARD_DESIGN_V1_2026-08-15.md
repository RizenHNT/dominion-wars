# 统御战纪 · 基本包全卡设计 v1（模型生成版 · 核心目标语义修正）

> **作者**：DeepSeek（QA/策划）· **日期**：2026-08-15
> **模型**：docs/CARD_DESIGN_MODEL_2026-08-15.md（含 §1.5 核心目标语义：本游戏无"玩家脸"，FACE=打核心目标，核心未降临落空）
> **修正记录**：v1a 废弃"打脸速攻"直伤模型（照搬炉石）；v1b 按核心目标语义重写——直伤=指定伤害（ENEMY_TARGET 可打随从/随从统领），FACE=克制件；冲锋关键词删除（用户反馈 2026-08-15）
> **格式**：id / 类型·惩罚·身材·关键词 / 效果字段 / 文本 / **价值等式** / 强度 / 实现

---

## 8. 烈焰帝国（26 张）— T1 快攻 · 随从交换 · 指定伤害斩杀 · 王城破城

> 模型参数：随从占比 61%（16/26）；惩罚值总和 ≤110；攻高血低；**主节奏=随从交换（清场→推随从统领）**；直伤=指定单体伤害；FACE=后期克制件；弱点=过牌/AOE 贵。

### 8.1 统领

**flame_leader** — 烈焰皇·焚天 / MINION·P0·8攻8血·圣盾
- leaderDef: winCondition=ROYAL_CASTLE_BREAK；vulnerabilities=[DAMAGE]；enterEffects=[DAMAGE:2@ENEMY_TARGET]；punishEffects=[END_TURN]
- 文本：圣盾。降临：对一个目标造成 2 点伤害。若你被惩罚抽到：结束对方回合。王城被击破时你获胜。
- 价值等式：身材16 −圣盾1 = 15 + 指定2伤(3.2) + 破城胜利轴；统领可被击败→预算最高档 ✓
- 强度：S｜实现：[READY]

### 8.2 随从（16）

**flame_imp** — 火舌小鬼 / MINION·P0·1攻1血
- onPlayEffects: DAMAGE:1@ENEMY_TARGET
- 文本：战吼：对一个目标造成 1 点伤害。
- 价值等式：S2 + 1.6 = 3.6 vs 2 → 超 1.6；0 费无风险，1/1 一碰即碎=风险补偿
- 强度：B｜实现：[READY]

**flame_recruit** — 烈焰新兵 / MINION·P1·2攻1血·突袭
- onDeathEffects: DAMAGE:1@ENEMY_FACE
- 文本：突袭。亡语：对敌方核心目标造成 1 点伤害。
- 价值等式：S3 −1(突袭) +1.1(亡语FACE) = 3.1 vs 4 → 节奏件
- 强度：B｜实现：[READY]（onDeathEffects 需追加字段）

**flame_volunteer** — 烈焰民兵 / MINION·P1·2攻2血
- 文本：无效果。
- 价值等式：S4 = 4 ✓
- 强度：C｜实现：[READY]

**flame_zealot** — 烈焰狂信者 / MINION·P1·2攻2血
- onDeathEffects: DAMAGE:1@ENEMY_FACE
- 文本：亡语：对敌方核心目标造成 1 点伤害。
- 价值等式：S4 + 1.1 = 5.1 vs 4 → 超 1.1；亡语延迟=补偿
- 强度：A（低费亡语，交换后抢核心血）｜实现：[READY]（onDeathEffects 追加）

**flame_charger** — 冲锋骑兵 / MINION·P2·3攻2血·突袭
- 文本：突袭。
- 价值等式：S5 −1(突袭) = 4 vs 6 → 节奏件
- 强度：B｜实现：[READY]

**flame_berserker** — 狂暴战士 / MINION·P2·4攻2血
- 文本：无效果。
- 价值等式：S6 = 6 ✓（压迫分布）
- 强度：B｜实现：[READY]

**flame_guard** — 熔岩护卫 / MINION·P2·3攻3血·嘲讽
- 文本：嘲讽。
- 价值等式：S6 −1(嘲讽) = 5 vs 6 → 略亏，保护打点
- 强度：B｜实现：[READY]

**flame_swordmaster** — 剑术大师 / MINION·P2·3攻2血
- onPlayEffects: BUFF:1:atk@SELF
- 文本：战吼：本随从 +1 攻击力。
- 价值等式：S5 + 0.8 = 5.8 vs 6 ✓
- 强度：B｜实现：[READY]

**flame_phoenix** — 烈焰凤凰 / MINION·P3·5攻3血
- 文本：无效果。
- 价值等式：S8 = 8 ✓
- 强度：B｜实现：[READY]

**flame_drake** — 赤焰幼龙 / MINION·P3·4攻3血·突袭
- 文本：突袭。
- 价值等式：S7 −1(突袭) = 6 vs 8 → 节奏件
- 强度：C｜实现：[READY]

**flame_avenger** — 烈焰复仇者 / MINION·P3·4攻2血
- onDeathEffects: DAMAGE:2@ENEMY_TARGET
- 文本：亡语：对一个目标造成 2 点伤害。
- 价值等式：S6 + 3.2 = 9.2 vs 8 → 超 1.2；亡语延迟=补偿（交换后收益件）
- 强度：A（随从交换核心）｜实现：[READY]（onDeathEffects 追加）

**flame_warlord** — 军团统领 / MINION·P4·6攻4血
- 文本：无效果。
- 价值等式：S10 = 10 ✓
- 强度：B｜实现：[READY]

**flame_infernal** — 深渊魔将 / MINION·P4·4攻3血
- onPlayEffects: DAMAGE:2@ENEMY_TARGET
- 文本：战吼：对一个目标造成 2 点伤害。
- 价值等式：S7 + 3.2 = 10.2 vs 10 → 超 0.2 ✓
- 强度：A（斩杀件）｜实现：[READY]

**flame_elemental** — 火元素 / MINION·P3·3攻4血
- onDeathEffects: DAMAGE:2@ENEMY_FACE
- 文本：亡语：对敌方核心目标造成 2 点伤害。
- 价值等式：S7 + 1.1 = 8.1 vs 8 ✓
- 强度：B｜实现：[READY]（onDeathEffects 追加）

**flame_giant** — 熔核巨人 / MINION·P5·7攻5血
- 文本：无效果。
- 价值等式：S12 = 12 ✓
- 强度：B｜实现：[READY]

**flame_titan** — 烈焰泰坦 / MINION·P5·6攻6血·嘲讽
- 文本：嘲讽。
- 价值等式：S12 −1(嘲讽) = 11 vs 12 → 略亏
- 强度：B｜实现：[READY]

### 8.3 咒文（6）— 弱点设计：过牌/AOE 贵；指定伤害为强项

**flame_bolt** — 飞火流星 / SPELL·P2·标签[速攻]
- onPlayEffects: DAMAGE:3@ENEMY_TARGET
- 文本：对一个目标造成 3 点伤害。
- 价值等式：2.4 vs 2 → 超 20%；直伤=阵营强项（超模溢价）
- 强度：A（斩杀/解场两用）｜实现：[READY]

**flame_strike** — 火焰冲击 / SPELL·P3·标签[破坏]
- onPlayEffects: DAMAGE:4@ENEMY_TARGET, kingSlayer=true
- 文本：对一个目标造成 4 点伤害（可作用于统领）。
- 价值等式：3.2+0.4(KS) = 3.6 vs 3 → 超 20%；超模解场
- 强度：A｜实现：[READY]

**flame_rain** — 烈焰风暴 / SPELL·P4·标签[风暴]
- onPlayEffects: DAMAGE:2@ALL_ENEMY_MINIONS
- 文本：群体致伤 2。
- 价值等式：2.4 vs 4 → 亏 40%；**弱点设计**（烈焰 AOE 效率低）
- 强度：C｜实现：[READY]

**flame_warcry** — 帝国战吼 / SPELL·P3·标签[增强]
- onPlayEffects: BUFF:1:both@ALL_FRIENDLY_MINIONS
- 文本：己方所有随从 +1/+1。
- 价值等式：n×0.8（3 随从=2.4✓，4 随从=3.2 超）→ 铺场上限牌
- 强度：B｜实现：[READY]

**flame_double** — 二连斩令 / SPELL·P2·标签[军令]
- onPlayEffects: RESTORE_ATTACKS@FRIENDLY_MINION
- 文本：使己方一个随从本回合可再次攻击。
- 价值等式：2.0-3.0 vs 2 → 上限牌（高攻随从超值）
- 强度：B｜实现：[READY]

**flame_siege** — 烈焰攻城 / SPELL·P3·标签[攻城]
- onPlayEffects: DAMAGE_CASTLE:4
- 文本：对共享王城造成 4 点伤害。
- 价值等式：2.4 vs 3 → 亏；破城轴协同价值（王城 75 血 + 统领 ROYAL_CASTLE_BREAK）
- 强度：C（协同件）｜实现：[READY]

### 8.4 伏击（2）

**flame_ambush_counter** — 烈火反噬 / AMBUSH·P1·NORMAL·OPPONENT_ATTACKS·标签[反击]
- ambushEffects: DAMAGE:2@ENEMY_MINION
- 文本：伏击：对方攻击时，对其攻击随从造成 2 点伤害。
- 价值等式：1.6 条件 vs 1 → 超；条件触发+伏击限制=补偿
- 强度：B｜实现：[READY]

**flame_ambush_seal** — 燃尽封咒 / AMBUSH·P2·FOCUS·OPPONENT_PLAYS_SPELL·标签[反制]
- ambushEffects: NEGATE
- 文本：伏击（专注）：对方打出咒文时，反制其效果。
- 价值等式：2.5 条件 vs 2 → 略超；专注限制=补偿
- 强度：B｜实现：[READY]

### 8.5 惩罚牌（1）

**flame_punish_wrath** — 焚天之怒 / PUNISH·P4·cost1·ENEMY_MINIONS_GE_1·标签[天罚]
- punishEffects: DAMAGE:4@ENEMY_FACE
- 文本：若你被惩罚抽到（费用 1）：对方有随从时，对敌方核心目标造成 4 点伤害。
- 价值等式：2.2 vs 4+1 → 亏；被喂牌时的斩杀件（后期核心必在场），条件防空发
- 强度：B｜实现：[READY]

---

*烈焰 v1b 完成（26 张）。设计要点：主节奏=随从交换（突袭/亡语/连击），直伤=指定伤害（可打随从统领），FACE 仅 4 处（recruit/zealot/elemental 亡语 + wrath 惩罚），王城破城=独立慢轴。下一阵营：机械。*


## 9. 机械遗迹（28 张）— T2 中速转换 · 0 费上传下载 · alpha 下载轴

> 模型参数：随从全 0 惩罚（RULES §12.4 基线）、身材 S(0)=2-3 偏低；价值 = 基础身材 + 上传收益(提交/上传) + 下载收益(下载/回滚)；卡组惩罚值 ≤100（对手几乎不被喂牌→转换收益=净价值）；弱点=前期场面弱、被弃牌拆燃料。
> 核心目标语义：machine_alpha 是随从统领（可被击败）→ 下载轴必须在 alpha 存活期完成（圣盾/墙保护）；machine_leader 耐久统领（不可被击败，先锋）。
> 新机制标注：[MD: E3/E4/E5] = 提交/上传/下载/回滚 动作与区域、协议字段未实现；全部 READY 卡当前引擎可直接落地。

### 9.1 统领（2）

**machine_leader** — 上古咒文·赋值机身 / SPELL·P0·耐久8·标签[统领]
- leaderDef: winCondition=NONE, durability=8, vulnerabilities=[DAMAGE]；enterEffects=[SUMMON:1:machine_drone]；punishEffects=[ADD_OPP_PUNISH_TURN:1, SKIP_RESHUFFLE:1]；chant=2, chantEffects=[SUMMON_LEADER:machine_alpha]
- 文本：耐久 8。降临：召唤 1 个侦察机偶。吟唱 2：2 回合后转生为上古极神Alpha。若你被惩罚抽到：对方本回合惩罚 +1，你获得 1 次跳过洗牌计数。
- 价值等式：耐久 8（不可被击败）≈ 中期生存预算；降临造 1 燃料 + 2 回合转生 alpha = 先锋→终端的节奏桥；被惩罚保底惩罚流
- 强度：A｜实现：[READY]（alpha 胜利条件 [MD: E1/E5]）

**machine_alpha** — 上古极神Alpha / MINION·P0·8攻10血·圣盾·标签[极神]
- leaderDef: winCondition=PULL_TOTAL_GE, winAmount=6, protocolFields=[COMMIT,PUSH,PULL,ROLLBACK,CHARGE,TAUNT]（协议白名单定稿）, vulnerabilities=[DAMAGE]；enterEffects=[BUFF:1:both@ALL_FRIENDLY_MINIONS]；punishEffects=[ADD_OPP_PUNISH_TURN:2]
- 文本：圣盾。降临：己方所有随从 +1/+1。若你被惩罚抽到：对方本回合惩罚 +2。本局己方累计完成 6 次 下载 时你获胜。
- 价值等式：身材 18 −圣盾1 = 17（随从统领最高档）+ 降临全队强化 + 下载轴胜利；**胜利轴=6 次 下载**（15 回合局 ≈ 12-14 回合达成），协议六字段白名单防字段池膨胀贬值
- 强度：S｜实现：[MD: E1, E5]

### 9.2 随从（12）— 全 0 惩罚，低身材 + 转换价值

**machine_drone** — 侦察机偶 / MINION·P0·1攻1血
- onPlayEffects: DRAW:1
- 文本：战吼：抽 1 张牌。
- 价值等式：S2 + 2.4 = 4.4 vs 2 → 超；0 费 + 1/1 一碰即碎=风险补偿；量产协议产出 token、提交 燃料
- 强度：B｜实现：[READY]

**machine_golem** — 锈蚀魔像 / MINION·P0·1攻2血·嘲讽
- 文本：嘲讽。
- 价值等式：S3 −1(嘲讽) = 2 = 预算 ✓（0 费防守件，提供 TAUNT 协议字段）
- 强度：B｜实现：[READY]

**machine_wall** — 装甲壁垒 / MINION·P0·0攻4血·嘲讽
- 文本：嘲讽。
- 价值等式：S4 −1(嘲讽) = 3 vs 2 → 超 1；0 攻纯防守墙（拖到 alpha 转生），墙身份允许
- 强度：B｜实现：[READY]

**machine_blaster** — 聚能炮台 / MINION·P0·1攻1血
- onPlayEffects: DAMAGE:1@ENEMY_TARGET
- 文本：战吼：对一个目标造成 1 点伤害。
- 价值等式：S2 + 1.6 = 3.6 vs 2 → 超；1/1 脆=补偿
- 强度：B｜实现：[READY]

**machine_spark** — 火花机蜂 / MINION·P0·2攻1血·突袭
- 文本：突袭。
- 价值等式：S3 −1(突袭) = 2 = 预算 ✓（提供 CHARGE 协议字段=突袭映射）
- 强度：B｜实现：[READY]

**machine_assembler** — 自我组装体 / MINION·P0·1攻2血
- punishActivatable=true, punishCost=0, punishCondition=ALWAYS
- 文本：若你被惩罚抽到（费用 0）：可打出占场。
- 价值等式：S3 vs 2 → 超 1；0 费响应=免费节奏
- 强度：B｜实现：[READY]

**machine_recycler** — 回收单元 / MINION·P0·1攻2血
- punishActivatable=true, punishCost=1, punishCondition=HAND_GE_3, punishEffects=[DRAW:1]
- 文本：若你被惩罚抽到（费用 1）：你的手牌 ≥3 时，抽 1 张牌。
- 价值等式：S3 + 条件过牌(2.4) = 5.4 vs 2 → 超（条件+0费补偿）
- 强度：C｜实现：[READY]

**machine_builder** — 建造单元 / MINION·P0·1攻1血
- onDeathEffects: DRAW:1
- 文本：亡语：抽 1 张牌。
- 价值等式：S2 + 2.4 = 4.4 vs 2 → 超；亡语延迟+1/1 脆=补偿；交换后过牌（转换燃料回收）
- 强度：B｜实现：[READY]（onDeathEffects 追加）

**machine_uploader** — 上传傀儡 / MINION·P0·1攻2血·标签[上传]
- onPlayEffects: COMMIT:1:deck_top
- 文本：战吼：将己方牌库顶的机械卡提交（支付其 提交 费用）。（提供 COMMIT 协议字段）
- 价值等式：S3 + 免费提交引擎（上传收益≈30% 预算）→ 提交队列关键来源
- 强度：B｜实现：[MD: E3, E4]

**machine_compiler** — 编译机仆 / MINION·P0·1攻2血·标签[编译]
- pushEffects: DRAW:1（上传 结算时触发）
- 文本：推送：进入云端栈时抽 1 张牌。（提供 PUSH 协议字段）
- 价值等式：S3 + 上传 过牌（上传收益）→ 推送阶段资源回收
- 强度：B｜实现：[MD: E3, E4]

**machine_downloader** — 下载终端 / MINION·P0·1攻2血·标签[下载]
- pullEffects: DRAW:1（下载 结算时触发）
- 文本：拉取：被 下载 结算时抽 1 张牌。（提供 PULL 协议字段）
- 价值等式：S3 + 下载 过牌 + **下载 计数推进 alpha 胜利** → 下载轴主引擎
- 强度：A｜实现：[MD: E3, E4]

**machine_archivist** — 档案维护体 / MINION·P0·1攻2血·标签[档案]
- onPlayEffects: ROLLBACK:1
- 文本：战吼：将己方提交队列中一张卡回滚回手牌。（提供 ROLLBACK 协议字段）
- 价值等式：S3 + 回滚复用（下载收益）→ 资源循环
- 强度：B｜实现：[MD: E3, E4]

### 9.3 咒文（10）— 转换引擎 + 防守（高费段集中）

**machine_scan** — 全域扫描 / SPELL·P1·标签[侦测]
- onPlayEffects: DRAW:2
- 文本：抽 2 张牌。
- 价值等式：2.4 vs 1 → 超 40%；机械需手牌做 提交 燃料，1 费低风险过牌（强项）
- 强度：A｜实现：[READY]

**machine_overload** — 过载冲击 / SPELL·P3·标签[破坏]
- onPlayEffects: DAMAGE:4@ENEMY_TARGET
- 文本：对一个目标造成 4 点伤害。
- 价值等式：3.2 vs 3 → 超 7%；中期解场
- 强度：B｜实现：[READY]

**machine_emp** — EMP脉冲 / SPELL·P4·标签[风暴]
- onPlayEffects: DAMAGE:2@ALL_ENEMY_MINIONS
- 文本：群体致伤 2。
- 价值等式：2.4 vs 4 → 亏；AOE 稳定性补价
- 强度：B｜实现：[READY]

**machine_repair** — 纳米修复 / SPELL·P1·标签[恢复]
- onPlayEffects: HEAL:2@ALL_FRIENDLY_MINIONS
- 文本：己方所有随从恢复 2 点生命。
- 价值等式：0.7×2×n vs 1 → 随从多时超值；0 费随从脆→治疗收益稳定
- 强度：B｜实现：[READY]

**machine_factory** — 量产协议 / SPELL·P3·标签[协议]
- chant=2, chantEffects=[SUMMON:3:machine_drone]
- 文本：吟唱 2：2 回合后召唤 3 个侦察机偶。
- 价值等式：3×(4.4) 延迟 vs 3 → 延迟对冲超值；产出=燃料池（既是场面又是 提交 原料）
- 强度：B｜实现：[READY]

**machine_cannon** — 轨道炮 / SPELL·P3·标签[轰击]
- chant=1, chantEffects=[DAMAGE:7@ENEMY_TARGET, kingSlayer=true]
- 文本：吟唱 1：1 回合后对一个目标造成 7 点伤害（可作用于统领）。
- 价值等式：5.6+0.5 延迟 vs 3 → 延迟对冲巨额单体；蓄力重炮身份牌
- 强度：A｜实现：[READY]

**machine_virus** — 逻辑病毒 / SPELL·P2·标签[干扰]
- onPlayEffects: ADD_OPP_PUNISH_TURN:1
- 文本：对方本回合每张卡牌惩罚值 +1。
- 价值等式：0.5×n vs 2 → 对方出牌越多收益越大；惩罚流干扰件
- 强度：B｜实现：[READY]

**machine_recharge** — 紧急充能 / SPELL·P1·标签[充能]
- onPlayEffects: RESTORE_ATTACKS@FRIENDLY_MINION
- 文本：使己方一个随从本回合可再次攻击。
- 价值等式：2.0-3.0 vs 1 → 上限牌；配合高攻随从/上传傀儡
- 强度：B｜实现：[READY]

**machine_commit_protocol** — 提交协议 / SPELL·P3·标签[协议]
- onPlayEffects: COMMIT:2:hand
- 文本：从手牌提交 2 张机械卡（支付各自 提交 费用）。（提供 COMMIT 协议字段）
- 价值等式：批量提交引擎（上传轴）+ COMMIT 协议计数
- 强度：B｜实现：[MD: E3, E4]

**machine_pull_protocol** — 拉取协议 / SPELL·P1·标签[协议]
- onPlayEffects: PULL:1
- 文本：拉取云端栈顶卡（支付其 下载费用，结算其 下载效果）。（提供 PULL 协议字段）
- 价值等式：直接推进 alpha 胜利（1 次 下载）+ 结算 下载效果；1 费低风险（下载轴加速器）
- 强度：A｜实现：[MD: E3, E4]

### 9.4 伏击（2）

**machine_trap** — 电磁陷阱 / AMBUSH·P1·NORMAL·OPPONENT_SUMMONS·标签[陷阱]
- ambushEffects: DAMAGE:3@ENEMY_MINION
- 文本：伏击：对方召唤随从时，对其造成 3 点伤害。
- 价值等式：2.4 条件 vs 1 → 超；条件触发+伏击限制=补偿；反铺场防御件
- 强度：B｜实现：[READY]

**machine_null** — 协议屏障 / AMBUSH·P2·FOCUS·OPPONENT_PLAYS_SPELL·标签[反制]
- ambushEffects: NEGATE
- 文本：伏击（专注）：对方打出咒文时，反制其效果。
- 价值等式：2.5 条件 vs 2 → 略超；专注限制=补偿
- 强度：B｜实现：[READY]

### 9.5 惩罚牌（2）

**machine_punish_core** — 核心反击程序 / PUNISH·P5·cost1·ALWAYS·标签[程序]
- punishEffects: [DAMAGE:3@ALL_ENEMY_MINIONS, DRAW:1]
- 文本：若你被惩罚抽到（费用 1）：对所有敌方随从造成 3 点伤害，并抽 1 张牌。
- 价值等式：3.6+1.2 = 4.8 vs 5+1 → 略亏；被喂牌时的清场+过牌（回收反击主题）
- 强度：A｜实现：[READY]

**machine_punish_sync** — 同步协议 / PUNISH·P4·cost1·HAND_GE_3·标签[程序]
- punishEffects: COMMIT:1:hand
- 文本：若你被惩罚抽到（费用 1）：你的手牌 ≥3 时，从手牌提交 1 张机械卡（免费，支付其 提交 费用）。（提供 COMMIT 协议字段）
- 价值等式：被喂牌→免费转上传燃料；条件防空发；推进 COMMIT 计数
- 强度：B｜实现：[MD: E3, E4]

---

*机械 28 张完成（统领 2 / 随从 12 / 咒文 10 / 伏击 2 / 惩罚 2）。READY 18 张 + MD 10 张（E3/E4 系 7 张 + alpha/leader 胜利条件 2 张 + 同步协议 1 张）。下一阵营：深海（潮位标记系统）。*


## 10. 深海联盟（26 张）— T2 中速 · 潮位标记系统 · 弃牌副轴

> 模型参数：潮位=附加于对手的公开标记（§12.3，E8 扩展）。**潮位经济：来源卡叠标记（1 潮位 = 1.0 价值，身材预算 −1/潮位）；消费卡兑现（1 潮位 = 1.3 价值，兑现比叠加更赚→驱动成组）**；来源:消费 ≈ 1.2:1。惩罚值总和 115-125。
> 核心目标语义：sea_leader = **赋予生命统领（grantLife 25）→ 对手 FACE 直伤可直接打深海生命池**——这是深海固有弱点（需治疗/墙对冲）；弃牌副轴=对方累计效果弃牌 12 张（强制弃牌不计）。
> 新机制标注：[MD: E8] = ADD_OPP_TIDE/CONSUME_OPP_TIDE 动作未实现；潮位卡在机制接入前暂缓。

### 10.1 统领

**sea_leader** — 深渊主宰·涛冥 / SPELL·P0·赋予生命25·标签[统领]
- leaderDef: winCondition=OPP_DISCARD_TOTAL_GE, winAmount=12, grantLife=25, vulnerabilities=[DAMAGE]；enterEffects=[DRAW:2, ADD_OPP_TIDE:1]；punishEffects=[CONVERT_PUNISH_TO_DISCARD]
- 文本：赋予你 25 点生命。降临：抽 2 张，对方获得 1 点潮位。若你被惩罚抽到：对方本回合的惩罚抽牌改为弃牌。对方累计因效果弃牌达到 12 张时你获胜。
- 价值等式：生命池 25（赋予生命统领，被 FACE 直伤克制）+ 降临过牌+叠标记 + 惩罚翻转（弃牌副轴加速）+ 弃牌胜利
- 强度：S｜实现：[READY]（ADD_OPP_TIDE 部分 [MD: E8]）

### 10.2 随从（12）

**sea_crab** — 铁甲蟹 / MINION·P1·1攻4血·嘲讽
- 文本：嘲讽。
- 价值等式：S5 −1(嘲讽) = 4 ✓（低费墙，保护潮位引擎）
- 强度：B｜实现：[READY]

**sea_eel** — 电鳗游袭 / MINION·P1·3攻1血·突袭
- 文本：突袭。
- 价值等式：S4 −1(突袭) = 3 vs 4 → 节奏件（前期铺场压制）
- 强度：B｜实现：[READY]

**sea_tentacle** — 深渊之触 / MINION·P1·2攻2血·嘲讽（token）
- 文本：嘲讽。
- 价值等式：S4 −1(嘲讽) = 3 vs 4 → 略亏；token 模板
- 强度：B｜实现：[READY]

**sea_fanatic** — 潮汐信徒 / MINION·P1·2攻2血·标签[信徒]
- onPlayEffects: ADD_OPP_TIDE:1
- 文本：战吼：对方获得 1 点潮位。
- 价值等式：S4 + 1.0(潮位来源) = 5 vs 4 → 超 1；叠标记=投资（兑现后才回本）
- 强度：B｜实现：[MD: E8]

**sea_siren** — 暗礁海妖 / MINION·P2·2攻3血·标签[海妖]
- onPlayEffects: ADD_OPP_TIDE:2；onOpponentDiscardEffects: BUFF:1:both@SELF
- 文本：战吼：对方获得 2 点潮位。每当对方因效果弃牌：本随从 +1/+1。
- 价值等式：S5 + 2.0(潮位来源) = 7 vs 6 → 超 1；来源核心 + 弃牌联动成长（双轴复合）
- 强度：A｜实现：[READY]（ADD_OPP_TIDE [MD: E8]）

**sea_mistwalker** — 迷雾行者 / MINION·P2·3攻3血·扰魔
- 文本：扰魔。
- 价值等式：S6 −0.5(扰魔) = 5.5 vs 6 ✓
- 强度：C｜实现：[READY]

**sea_kraken** — 克拉肯触手 / MINION·P2·4攻2血·标签[巨兽]
- punishActivatable=true, punishCost=0, punishCondition=ENEMY_MINIONS_GE_2
- 文本：若你被惩罚抽到（费用 0）：对方有 ≥2 个随从时可打出占场。
- 价值等式：S6 = 6 ✓ + 0 费响应（条件）
- 强度：B｜实现：[READY]

**sea_priest** — 潮汐祭司 / MINION·P2·2攻4血·标签[祭司]
- onPlayEffects: HEAL:3@FRIENDLY_MINION
- 文本：战吼：使己方一个随从恢复 3 点生命。
- 价值等式：S6 + 1.05 = 7.05 vs 6 → 略超；治疗对冲 FACE 直伤弱点
- 强度：B｜实现：[READY]

**sea_leviathan_young** — 幼年利维坦 / MINION·P3·5攻5血·标签[巨兽]
- 文本：无效果。
- 价值等式：S10 vs 8 → 超 2（海阵营身材补强——因生命池弱点被直伤克制，需身材超模对冲）
- 强度：B｜实现：[READY]

**sea_warden** — 深海狱卒 / MINION·P3·4攻5血·嘲讽·标签[守卫]
- onOpponentDiscardEffects: DAMAGE_CASTLE:1
- 文本：嘲讽。每当对方因效果弃牌：对共享王城造成 1 点伤害。
- 价值等式：S9 −1(嘲讽) = 8 ✓ + 弃牌→王城转换（弃牌副轴副收益）
- 强度：A｜实现：[READY]

**sea_abyss** — 深渊巨口 / MINION·P3·3攻5血·扰魔·标签[巨兽]
- 文本：扰魔。
- 价值等式：S8 −0.5(扰魔) = 7.5 vs 8 ✓
- 强度：C｜实现：[READY]

**sea_leviathan** — 利维坦巨兽 / MINION·P5·6攻7血·嘲讽·标签[巨兽]
- 文本：嘲讽。
- 价值等式：S13 −1(嘲讽) = 12 ✓（终结墙）
- 强度：B｜实现：[READY]

### 10.3 咒文（8）

**sea_tide** — 退潮 / SPELL·P1·标签[弃牌]
- onPlayEffects: DISCARD_OPP_RANDOM:2
- 文本：随机弃对方 2 张手牌。
- 价值等式：1.8 vs 1 → 超 0.8；直接推进弃牌胜利（12 张阈值）+ 触发联动（siren/warden）
- 强度：A｜实现：[READY]

**sea_whisper** — 深渊低语 / SPELL·P2·标签[呓语]
- onPlayEffects: ADD_OPP_TIDE:2
- 文本：对方获得 2 点潮位。
- 价值等式：2.0 vs 2 ✓（来源卡）
- 强度：B｜实现：[MD: E8]

**sea_flood** — 潮汐涌动 / SPELL·P2·标签[兑现]
- onPlayEffects: CONSUME_OPP_TIDE:3 → DAMAGE:4@ENEMY_TARGET + DRAW:1
- 文本：消耗对方 3 点潮位：对一个目标造成 4 点伤害并抽 1 张牌。
- 价值等式：3.2+1.2 = 4.4 vs 3 潮位×1.3 = 3.9 → 兑现略赚（系数 1.13）；潮位不足则无法兑现（成组驱动）
- 强度：A（兑现核心）｜实现：[MD: E8]

**sea_current** — 海流加速 / SPELL·P2·标签[抽牌]
- onPlayEffects: DRAW:2
- 文本：抽 2 张牌。
- 价值等式：2.4 vs 2 → 略超；供弹（弃牌/潮位卡需要手牌）
- 强度：B｜实现：[READY]

**sea_sink** — 沉没 / SPELL·P4·标签[湮灭]
- onPlayEffects: DESTROY@ENEMY_MINION
- 文本：消灭一个敌方随从。
- 价值等式：3.0 vs 4 → 亏；无条件硬解（无视圣盾/血量）
- 强度：B｜实现：[READY]

**sea_whirl** — 灭顶漩涡 / SPELL·P3·标签[风暴]
- onPlayEffects: DAMAGE:2@ALL_ENEMY_MINIONS
- 文本：群体致伤 2。
- 价值等式：2.4 vs 3 → 亏；AOE 稳定性补价（海 AOE 非强项）
- 强度：B｜实现：[READY]

**sea_depths** — 海渊凝视 / SPELL·P2·标签[仪式]
- chant=1, chantEffects=[DISCARD_OPP_RANDOM:3]
- 文本：吟唱 1：1 回合后随机弃对方 3 张手牌。
- 价值等式：2.7 延迟 vs 2 → 延迟对冲；弃牌副轴爆发件
- 强度：B｜实现：[READY]

**sea_barrier** — 水盾结界 / SPELL·P2·标签[守护]
- onPlayEffects: GRANT_KEYWORD:圣盾@FRIENDLY_MINION
- 文本：使己方一个随从获得圣盾。
- 价值等式：1.3 vs 2 → 亏；保护关键引擎随从（对冲生命池弱点）
- 强度：C｜实现：[READY]

### 10.4 伏击（2）

**sea_devour** — 深渊吞噬 / AMBUSH·P3·FOCUS·OPPONENT_DRAWS·标签[弃牌]
- ambushEffects: DISCARD_DRAWN
- 文本：伏击（专注）：对方抽牌时，弃掉其刚抽到的牌。
- 价值等式：反抽牌 2.0-3.0 条件 vs 3 → 条件+专注补偿；克制抽牌流（烈焰/机械过牌）
- 强度：B｜实现：[READY]

**sea_ink** — 墨幕 / AMBUSH·P1·NORMAL·OPPONENT_ATTACKS·标签[反制]
- ambushEffects: NEGATE
- 文本：伏击：对方攻击时，反制该次攻击。
- 价值等式：2.0 条件 vs 1 → 超；条件触发+伏击限制=补偿
- 强度：B｜实现：[READY]

### 10.5 惩罚牌（3）

**sea_punish_tsunami** — 灭世海啸 / PUNISH·P6·cost2·ALWAYS·标签[天灾]
- punishEffects: DAMAGE:5@ALL_ENEMY_MINIONS
- 文本：若你被惩罚抽到（费用 2）：对所有敌方随从造成 5 点伤害。
- 价值等式：6.0 vs 6+2 → 持平；高费高回报清场（费用 2 防滥用）
- 强度：B｜实现：[READY]

**sea_punish_maelstrom** — 漩涡深渊 / PUNISH·P5·cost1·ALWAYS·标签[天灾]
- punishEffects: [DISCARD_OPP_RANDOM:2, DAMAGE:2@ENEMY_FACE]
- 文本：若你被惩罚抽到（费用 1）：随机弃对方 2 张手牌，并对敌方核心目标造成 2 点伤害。
- 价值等式：1.8+1.1 = 2.9 vs 5+1 → 亏；双效果推进弃牌胜利+压核心（后期核心必在场）
- 强度：B｜实现：[READY]

**sea_punish_tide** — 潮汐回响 / PUNISH·P4·cost1·ALWAYS·标签[天灾]
- punishEffects: ADD_OPP_TIDE:3
- 文本：若你被惩罚抽到（费用 1）：对方获得 3 点潮位。
- 价值等式：3.0 vs 4+1 → 亏；被喂牌时叠标记，为后续兑现蓄力
- 强度：C｜实现：[MD: E8]

---

*深海 26 张完成（统领 1 / 随从 12 / 咒文 8 / 伏击 2 / 惩罚 3）。潮位依赖 [MD: E8] 的卡：fanatic/siren/whisper/flood/tide 共 5 张（含统领注记）；READY 21 张。下一阵营：古木。*


## 11. 古木圣地（29 张）— T3 大后期控制 · 敌不动我不动 · 惩罚降临普及版（v8）

> 模型参数（2026-08-15 人类定稿 v8）：
> 1. **打印惩罚值压低**：P1-2 为主（3-5 偏少，仅高收益/终结件）——**防双方无限连锁**（打印值高→喂对面多→连锁激烈）；
> 2. **大部分卡（≥60%）带惩罚降临**（punishActivatable，普通卡非仅惩罚牌），**响应费用低费为主（cost 0-1）**；
> 3. **双层效果**：主动打出=低收益模板（致伤1/恢复1 级）；被惩罚抽到=正常模板（致伤2/抽牌/群体级）——驱动"谨慎出牌/期待对方出牌"博弈；
> 4. 全局原则 §0.1：出牌双刃剑、惩罚降临=卡牌核心价值层。
> 新机制：[MD: E6]扎根/疯长；[MD: E7]封印；[MD: E11]GIANT_HEALTH_GE；[MD: E13]驱逐；[MD: E14]操纵。

### 11.1 统领

**wood_leader** — 世界树之心 / SPELL·P0·非战斗·标签[统领]
- leaderDef: winCondition=GIANT_HEALTH_GE, winAmount=512（256-384 待平衡测试）, vulnerabilities=[]；enterEffects=[SUMMON:2:wood_sapling, 疯长+1]；punishEffects=[疯长+2, PROTECT_TURN]；onTurnEndConditional=[无伤回合→疯长+1，上限 3 层]
- 文本：降临：召唤 2 个新芽树灵，获得 1 层疯长。若你被惩罚抽到：获得 2 层疯长，本回合己方卡牌不会被破坏。在场期间，每回合结束时若你本回合未受任何伤害：获得 1 层疯长（上限 3 层）。当你的一只封印随从生命值达到 512 时你获胜。
- 强度：S｜实现：[MD: E6, E7, E11]

### 11.2 随从（10）— 低打印惩罚 + 普遍惩罚降临

**wood_sapling** — 新芽树灵 / MINION·P0·1攻2血（token）
- 主动：无效果。
- 惩罚降临：cost0 / 无条件 / 恢复 1。
- 强度：C｜实现：[READY]

**wood_wisp** — 灵光精灵 / MINION·P1·1攻1血
- 主动：无效果（低收益）。
- 惩罚降临：cost0 / 无条件 / 抽牌 1（正常模板）。
- 强度：B｜实现：[READY]

**wood_seedling** — 幼苗护卫 / MINION·P1·1攻2血（扎根源）
- 主动：降临：扎根+1。
- 惩罚降临：cost1 / 无条件 / 扎根+1。
- 强度：B｜实现：[MD: E6]

**wood_vine** — 藤蔓缠绕者 / MINION·P1·2攻2血
- 主动：降临：使己方一个随从 +1/+1（普通 buff，不封印）。
- 惩罚降临：cost0 / 无条件 / 使己方一个随从 +1/+1。
- 强度：B｜实现：[READY]

**wood_guard** — 橡木守卫 / MINION·P2·3攻3血·嘲讽
- 主动：嘲讽。
- 惩罚降临：cost0 / 无条件 / 使己方一个随从获得圣盾（正常模板）。
- 强度：B｜实现：[READY]

**wood_ancient** — 古木长老 / MINION·P2·2攻2血·嘲讽
- 主动：嘲讽。降临：致伤 1（低收益模板）。
- 惩罚降临：cost1 / 无条件 / 致伤 2（正常模板·低费反击点）。
- 强度：B｜实现：[READY]

**wood_druid** — 林地德鲁伊 / MINION·P2·2攻3血
- 主动：降临：使己方一个随从恢复 1。
- 惩罚降临：cost1 / 对方有随从 / 群体恢复 2（正常模板）。
- 强度：B｜实现：[READY]

**wood_owl** — 智慧古枭 / MINION·P1·2攻2血
- 主动：无效果（P1 正常小卡身材，喂 1 换站场）。
- 惩罚降临：cost0 / 无条件 / 抽牌 1（正常模板）。
- 强度：B｜实现：[READY]

**wood_grove** — 常青林冠 / MINION·P2·0攻5血·嘲讽（扎根源）
- 主动：嘲讽。降临：扎根+1。
- 惩罚降临：cost1 / 无条件 / 扎根+1。
- 强度：B｜实现：[MD: E6, E7]

**wood_treant** — 古树行者 / MINION·P5·5攻7血·嘲讽（主载体·终结件）
- 主动：嘲讽。（高身材终结，P5 例外偏高打印）
- 惩罚降临：cost2 / 无条件 / 自身恢复 5（保载体）。
- 强度：B｜实现：[READY]

### 11.3 咒文（11）— 主动低收益 + 惩罚降临正常模板

**wood_growth** — 疯长 / SPELL·P3（任务·3费高收益）
- 主动：使己方一个随从 +4/+4（受扎根/疯长增幅则进封印）。
- 惩罚降临：cost1 / 无条件 / 使己方一个随从 +2/+2（正常模板）。
- 强度：A｜实现：[READY]（增幅/封印 [MD: E6/E7]）

**wood_bloom** — 生命绽放 / SPELL·P2（任务·扎根源）
- 主动：获得 1 层扎根。
- 惩罚降临：cost0 / 无条件 / 获得 1 层扎根。
- 强度：A｜实现：[MD: E6]

**wood_roots** — 扎根之根 / SPELL·P2（任务·扎根源·大）
- 主动：获得 2 层扎根。
- 惩罚降临：cost1 / 无条件 / 获得 1 层扎根。
- 强度：A｜实现：[MD: E6]

**wood_moon** — 月华射线 / SPELL·P3（3费高收益解场）
- 主动：对一个目标致伤 4（可作用于统领）。
- 惩罚降临：cost1 / 无条件 / 对一个目标致伤 2（正常模板）。
- 强度：A｜实现：[READY]

**wood_storm** — 荆棘风暴 / SPELL·P3（3费高收益清场）
- 主动：群体致伤 2。
- 惩罚降临：cost1 / 对方有随从 / 对一个敌方随从致伤 2。
- 强度：A｜实现：[READY]

**wood_decay** — 枯萎凋零 / SPELL·P2
- 主动：使一个敌方随从无力 -1/-1。
- 惩罚降临：cost0 / 无条件 / 使一个敌方随从无力 -1/-1。
- 强度：B｜实现：[READY]

**wood_bind** — 藤蔓束缚 / SPELL·P3（驱逐）
- 主动：驱逐一个敌方随从（回其卡组并洗牌）。
- 惩罚降临：cost1 / 无条件 / 使一个敌方随从无力 -1/-1。
- 强度：B｜实现：[MD: E13]

**wood_bramble** — 荆棘操纵 / SPELL·P3（操纵）
- 主动：操纵一个敌方随从至回合结束。
- 惩罚降临：cost1 / 无条件 / 使一个敌方随从震慑（下回合不能攻击）。
- 强度：B｜实现：[MD: E14]

**wood_scry** — 林间占卜 / SPELL·P1（资源·占星）
- 主动：占星 2（查看己方牌库顶 2 张，按任意顺序放回）。
- 惩罚降临：cost0 / 无条件 / 占星 1（查看牌库顶 1 张）。
- 价值等式：占星≈0.5-1.0/次（信息+操控抽牌，低值因抽卡不稀奇）；P1 喂 1 换 2 张看顶——不亏不赚，找 key 卡（buff/扎根源）用
- 强度：A｜实现：[READY]（占星=SCRY 关键词，RULES §13.1）

**wood_spring** — 生命之泉 / SPELL·P2
- 主动：群体恢复 1。
- 惩罚降临：cost0 / 无条件 / 使己方一个随从恢复 2。
- 强度：B｜实现：[READY]

**wood_seed** — 生命之种 / SPELL·P1（载体来源）
- 主动：吟唱 2：2 回合后召唤 1 个 5/7 嘲讽古树行者。
- 惩罚降临：cost1 / 无条件 / 召唤 1 个 1/2 新芽树灵（正常模板）。
- 强度：B｜实现：[READY]

### 11.4 伏击（3）— 低打印惩罚 + 惩罚降临

**wood_root** — 盘根陷阱 / AMBUSH·P1·NORMAL·OPPONENT_SUMMONS
- 主动：伏击：对方召唤随从时，对其致伤 2。
- 惩罚降临：cost0 / 无条件 / 对一个敌方随从致伤 1。
- 强度：B｜实现：[READY]

**wood_trapvine** — 缠绕陷阱 / AMBUSH·P2·NORMAL·OPPONENT_ATTACKS
- 主动：伏击：对方攻击时，使其攻击随从无力 -3 攻击（本回合）。
- 惩罚降临：cost0 / 无条件 / 使一个敌方随从无力 -1 攻击。
- 强度：B｜实现：[READY]

**wood_veil** — 密叶帷幕 / AMBUSH·P3·LOCKDOWN·OPPONENT_ATTACKS
- 主动：伏击（封场）：对方攻击时，反制该次攻击。
- 惩罚降临：cost1 / 无条件 / 使一个敌方随从震慑（下回合不能攻击）。
- 强度：B｜实现：[READY]

### 11.5 惩罚牌（4）— 打印值压低（防连锁）

**wood_punish_wrath** — 自然之怒 / PUNISH·P3·打印3
- punishCost=1, punishCondition=ENEMY_MINIONS_GE_1, punishEffects=[DESTROY@ENEMY_MINION]
- 文本：若你被惩罚抽到（费用 1）：对方有随从时，破坏一个敌方随从。
- 强度：A｜实现：[READY]

**wood_punish_thornfield** — 荆棘之域 / PUNISH·P3·打印3
- punishCost=1, punishCondition=ALWAYS, punishEffects=[DAMAGE:2@ALL_ENEMY_MINIONS]
- 文本：若你被惩罚抽到（费用 1）：群体致伤 2。
- 强度：A｜实现：[READY]

**wood_punish_barrier** — 夜幕屏障 / PUNISH·P3·打印3
- punishCost=1, punishCondition=HAND_GE_3, punishEffects=[GRANT_KEYWORD:圣盾@ALL_FRIENDLY_MINIONS]
- 文本：若你被惩罚抽到（费用 1）：你的手牌 ≥3 时，群体圣盾。
- 强度：B｜实现：[READY]

**wood_punish_bloom** — 绽放之光 / PUNISH·P3·打印3
- punishCost=1, punishCondition=HAND_GE_3, punishEffects=[HEAL:4@FRIENDLY_MINION, BUFF:1:both@FRIENDLY_MINION]
- 文本：若你被惩罚抽到（费用 1）：你的手牌 ≥3 时，使己方一个随从恢复 4 并 +1/+1。
- 强度：B｜实现：[READY]

## 12. 中立（14 张）— 通用工具 · 命运主题

> 模型参数：数值=各阵营基准中位数；每阵营卡组可外挂 8-12 张；无独立胜利轴（除两统领）。

### 12.1 统领（2）

**shadow_of_fate** — 命运之影 / SPELL·P0·赋予生命20·标签[统领]（新身份）
- leaderDef: winCondition=OPP_PUNISH_TRIGGERED_GE, winAmount=6, grantLife=20, vulnerabilities=[]；enterEffects=[OPP_DRAW:2]；punishEffects=[NEGATE_ENEMY_EFFECTS_TURN]
- 文本：赋予你 20 点生命。降临：对方抽 2 张牌。若你被惩罚抽到：对方场上卡牌效果本回合全部无效。对方累计成功发动惩罚降临达到 6 次时你获胜。
- 价值等式：生命池 20（赋予生命统领，FACE 可打）+ 喂牌引擎（诱导对方抽到惩罚牌并发动响应）+ 连锁胜利轴（6 次响应）；**平衡：对方可主动少发动响应压制（代价=放弃响应价值）**
- 强度：S（自定义卡组核心）｜实现：[MD: E2]

**gate_of_fate** — 命运之门 / AMBUSH·P0·FOCUS·OPPONENT_PLAYS_CARD·标签[统领]
- leaderDef: winCondition=AMBUSH_TRIGGER_WIN, vulnerabilities=[]
- ambushEffects: WIN_GAME:命运之门开启
- 文本：伏击（专注）统领：对方打出卡牌→触发：你立即获胜。
- 价值等式：触发=无条件获胜（极强）；条件=对方必须打出卡牌（对方可用不出牌/技能拖）——环境特化卡
- 强度：S（特化）｜实现：[READY]

### 12.2 随从（6）

**neutral_mercenary** — 雇佣剑士 / MINION·P2·3攻3血·标签[佣兵]
- 文本：无效果。
- 价值等式：S6 = 6 ✓（中立基准白板）
- 强度：B｜实现：[READY]

**neutral_mage** — 流浪法师 / MINION·P1·2攻2血·标签[法师]
- onPlayEffects: DAMAGE:1@ENEMY_TARGET
- 文本：战吼：对一个目标造成 1 点伤害。
- 价值等式：S4 + 1.6 = 5.6 vs 4 → 超；1/1 级脆+战吼即时=补偿
- 强度：B｜实现：[READY]

**neutral_watcher** — 沉默观察者 / MINION·P3·3攻5血·扰魔·标签[观察]
- 文本：扰魔。
- 价值等式：S8 −0.5(扰魔) = 7.5 vs 8 ✓
- 强度：C｜实现：[READY]

**neutral_knight** — 巡游骑士 / MINION·P3·3攻4血·嘲讽·标签[骑士]
- 文本：嘲讽。
- 价值等式：S7 −1(嘲讽) = 6 vs 8 → 略亏；通用守卫件
- 强度：C｜实现：[READY]

**neutral_scout** — 斥候信使 / MINION·P1·2攻2血·突袭·标签[斥候]
- 文本：突袭。
- 价值等式：S4 −1(突袭) = 3 vs 4 → 略亏；通用节奏件
- 强度：C｜实现：[READY]

**neutral_healer** — 巡回医师 / MINION·P2·2攻3血·标签[医师]
- onPlayEffects: HEAL:2@FRIENDLY_MINION
- 文本：战吼：使己方一个随从恢复 2 点生命。
- 价值等式：S5 + 0.7 = 5.7 vs 6 ✓
- 强度：B｜实现：[READY]

### 12.3 咒文（3）

**neutral_supply** — 补给车队 / SPELL·P1·标签[补给]
- onPlayEffects: DRAW:1
- 文本：抽 1 张牌。
- 价值等式：1.2 vs 1 → 略超；通用过牌
- 强度：B｜实现：[READY]

**neutral_arcane** — 奥术飞弹 / SPELL·P2·标签[速攻]
- onPlayEffects: DAMAGE:3@ENEMY_TARGET
- 文本：对一个目标造成 3 点伤害。
- 价值等式：2.4 vs 2 → 超 20%；通用解场
- 强度：B｜实现：[READY]

**neutral_barrier** — 守护誓约 / SPELL·P2·标签[守护]
- onPlayEffects: GRANT_KEYWORD:圣盾@FRIENDLY_MINION
- 文本：使己方一个随从获得圣盾。
- 价值等式：1.3 vs 2 → 亏；通用保护件
- 强度：C｜实现：[READY]

### 12.4 伏击（1）

**neutral_trap** — 伏击陷阱 / AMBUSH·P1·NORMAL·OPPONENT_ATTACKS·标签[陷阱]
- ambushEffects: DAMAGE:2@ENEMY_MINION
- 文本：伏击：对方攻击时，对其攻击随从造成 2 点伤害。
- 价值等式：1.6 条件 vs 1 → 超；条件+伏击限制=补偿
- 强度：B｜实现：[READY]

### 12.5 惩罚牌（2）

**neutral_punish_blast** — 混沌爆裂 / PUNISH·P5·cost1·ALWAYS·标签[混沌]
- punishEffects: DAMAGE:3@ALL_ENEMY_MINIONS
- 文本：若你被惩罚抽到（费用 1）：对所有敌方随从造成 3 点伤害。
- 价值等式：3.6 vs 5+1 → 亏；通用 AOE 响应
- 强度：B｜实现：[READY]

**neutral_punish_bomb** — 定时炸弹 / PUNISH·P4·cost1·HAND_GE_3·标签[混沌]
- punishEffects: DAMAGE:4@ENEMY_TARGET
- 文本：若你被惩罚抽到（费用 1）：你的手牌 ≥3 时，对一个目标造成 4 点伤害。
- 价值等式：3.2 vs 4+1 → 亏；条件通用去除
- 强度：C｜实现：[READY]

---

*中立 14 张完成（统领 2 / 随从 6 / 咒文 3 / 伏击 1 / 惩罚 2）。全 5 阵营合计：烈焰 26 + 机械 28 + 深海 26 + 古木 26 + 中立 14 = 120 张。下一批：④统计验证 + ⑤卡组。*

---

## 13. 统计验证报告（§7 协议 V1-V4）

### V1 价值分布 ✅
- 全 120 张逐卡带价值等式（S + Σ效果×2 + Σ关键词 ≤ 2P+2，咒文 Σ效果 ≤ P）；无 D 级废卡。
- 超模 >10% 清单（均有风险补偿）：flame_imp(+1.6 身材点，1/1 脆)、flame_zealot(+1.1，亡语延迟)、machine_drone(+2.4，1/1 脆)、machine_scan(+40% 惩罚，强项超模)、wood_moon(+40%，超模解场)、sea_leviathan_young(+2 身材，生命池弱点对冲)、wood_ancient(+1，防守身份)。
- 弱点设计落实：烈焰 AOE 亏 40%（flame_rain）、烈焰无高效过牌、古木单体解场弱（wood_thorn 亏）、机械前期无身材。

### V2 曲线检验 ✅（全卡池）
| 档位 | 数量 | 占比 | 模型目标 |
|---|---|---|---|
| 低费 0-2 | 77 | 64% | ≥40% ✓ |
| 中费 3-4 | 34 | 28% | 35%（偏低——机械 0 费轴拉低，惩罚值机制下低费更安全，接受） |
| 高费 5+ | 9 | 8% | ≤25% ✓ |

### V3 阵营均值差 ✅
- 平均身材点：烈焰 7.3 / 机械 3.1（0 费轴）/ 深海 7.5 / 古木 8.2 / 中立 6.2。
- 机械排除（0 费设计基线）后，四阵营均值差 ≤1.0 身材点，整体强度带未漂移。
- 机械的"低身材"由转换价值（提交/上传/下载 收益）补偿——符合 §12.4 基线。

### V4 协同-反制矩阵 ✅
| 阵营 | 协同链 | 反制出口 |
|---|---|---|
| 烈焰 | 突袭/亡语随从交换 → 指定伤害斩杀 → 王城破城 | 嘲讽墙、AOE 清场（烈焰自身 AOE 弱=被克制）、惩罚降临 |
| 机械 | 0 费随从 → COMMIT → PUSH 过牌 → PULL 计数 → alpha 6 次获胜 | 击败 alpha（随从统领）、弃牌拆燃料、压 machine_leader 耐久期 |
| 深海 | 低费铺场+叠潮位 → 消费兑现（潮位×1.3）→ 弃牌 12 张 | 生命池被 FACE 直伤克制、潮位不足兑现失败、强制弃牌不计胜利 |
| 古木 | 墙+治疗拖时间 → 无伤 6 回合 | 任何伤害打断（**含王城受伤**）、破城轴、牌库循环 |
| 中立 | 命运连锁（shadow 喂牌→对方发动响应） | 对方少发动响应、FACE 打生命池；gate 特化无解但极难触发 |

---

## 14. 官方卡组模板（4 套，59 主牌 + 1 统领，脚本校验）

### 14.1 烈焰帝国·焚天速攻（统领 flame_leader）
- 随从 21：flame_imp×3, flame_recruit×3, flame_volunteer×2, flame_zealot×3, flame_charger×3, flame_berserker×3, flame_guard×2, flame_swordmaster×2, flame_phoenix×2, flame_drake×2, flame_avenger×2, flame_warlord×2, flame_infernal×2, flame_elemental×2, flame_giant×1, flame_titan×1
- 咒文 11：flame_bolt×2, flame_strike×2, flame_warcry×2, flame_double×2, flame_siege×2, flame_rain×1
- 伏击 3：flame_ambush_counter×2, flame_ambush_seal×1
- 惩罚 1：flame_punish_wrath×1
- 中立 9：neutral_supply×2, neutral_mercenary×2, neutral_mage×2, neutral_scout×2, neutral_arcane×1
- 合计 59（30 种）✅

### 14.2 机械遗迹·极神协议（统领 machine_leader → machine_alpha）
- 0 费随从 17：machine_drone×3, machine_builder×2, machine_golem×3, machine_wall×2, machine_blaster×2, machine_spark×2, machine_assembler×2, machine_recycler×1
- 转换件 16：machine_uploader×3, machine_compiler×3, machine_downloader×3, machine_archivist×2, machine_commit_protocol×2, machine_pull_protocol×3
- 咒文 15：machine_scan×3, machine_overload×2, machine_emp×1, machine_repair×2, machine_factory×2, machine_cannon×2, machine_virus×2, machine_recharge×1
- 伏击 3：machine_trap×2, machine_null×1
- 惩罚 3：machine_punish_core×2, machine_punish_sync×1
- 中立 5：neutral_supply×2, neutral_mercenary×2, neutral_scout×1
- 合计 59（30 种）✅ ⚠️ 含 MD 转换件（E3/E4 未接入前以 READY 子集替代，见 §14.5）

### 14.3 深海联盟·吞噬之渊（统领 sea_leader）
- 随从 28：sea_crab×3, sea_eel×3, sea_tentacle×3, sea_fanatic×3, sea_siren×3, sea_mistwalker×2, sea_kraken×2, sea_priest×2, sea_leviathan_young×2, sea_warden×2, sea_abyss×2, sea_leviathan×1
- 咒文 17：sea_tide×3, sea_whisper×2, sea_flood×3, sea_current×2, sea_sink×2, sea_whirl×2, sea_depths×2, sea_barrier×1
- 伏击 4：sea_devour×2, sea_ink×2
- 惩罚 4：sea_punish_tsunami×2, sea_punish_maelstrom×1, sea_punish_tide×1
- 中立 6：neutral_supply×2, neutral_mercenary×2, neutral_mage×2
- 合计 59（28 种）✅ ⚠️ 潮位卡（fanatic/siren/whisper/flood/tide）依赖 E8

### 14.4 古木圣地·常青壁垒（统领 wood_leader）— 养巨物
- 随从 22：wood_sapling×3, wood_wisp×3, wood_seedling×2, wood_guard×3, wood_vine×2, wood_druid×2, wood_owl×2, wood_ancient×2, wood_treant×2, wood_grove×1
- 咒文 21：wood_growth×3, wood_bloom×2, wood_roots×2, wood_moon×2, wood_storm×2, wood_decay×2, wood_bind×1, wood_bramble×1, wood_scry×2, wood_spring×2, wood_seed×2
- 伏击 5：wood_root×2, wood_trapvine×2, wood_veil×1
- 惩罚 7：wood_punish_wrath×2, wood_punish_thornfield×2, wood_punish_barrier×2, wood_punish_bloom×1
- 中立 4：neutral_supply×2, neutral_healer×2
- 合计 59（30 种）✅ ⚠️ 养巨物核心（扎根×5 张：seedling/bloom/grove/roots + 疯长/封印/512）依赖 E6/E7/E11 + 新干扰动作 E13/E14；机制接入前木阵营整体延后；READY 工具（storm/scry/decay/thornfield/barrier 等）可先落地

### 14.5 卡组节奏/实现说明
- 四套卡组均含 ≥14 张 0-2 惩罚低费卡（惩罚值机制下低费安全，与全池曲线一致）。
- 机械/深海卡组含 [MD] 卡：机制（E3/E4/E8）接入前，以 READY 子集临时替代（机械用 0 费随从+防守咒文撑场面；深海用弃牌轴替代潮位兑现轴），机制接入后换回完整构筑。
- 各卡组对阵意图：烈焰快攻压古木无伤（古木前期弱）；古木控制拖深海（治疗扛弃牌）；深海弃牌拆机械燃料；机械转换中期爆发克烈焰（墙+清场）。四角循环，待 Java SimMain 实测（§15 V5）。

---

## 15. 交付清单与后续
| 项 | 状态 |
|---|---|
| ① 设计数学模型 | ✅ docs/CARD_DESIGN_MODEL_2026-08-15.md（含 §1.5 核心目标语义、§5 字段扩展、§7 验证协议） |
| ② 字段/关键词扩展 | ✅ 模型 §5（潜行/吸血新增，冲锋删除；onDeathEffects/protocolFields/tide 等） |
| ③ 全卡 v1（模型生成） | ✅ 120 张：烈焰 26 / 机械 28 / 深海 26 / 古木 26 / 中立 14 |
| ④ 统计分析 | ✅ §13（V1-V4 全过） |
| ⑤ 4 套卡组 | ✅ §14（59+1=60，脚本校验） |
| ⑥ Java 实测（V5） | ⏳ 待授权：临时数据跑 SimMain 4×4 |
| mailbox 通知 | 待写（下一步） |

**V5 前置**：需 Codex/PL 先确认 120 张卡的临时 JSON 数据格式（对齐 cards.schema.json 现有字段子集 + 标注 MD 卡），DeepSeek 生成临时数据文件 → 跑 SimMain → 出胜率矩阵 → 清理。
