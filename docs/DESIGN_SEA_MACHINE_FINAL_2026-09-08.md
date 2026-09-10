# 深海联盟 + 机械遗迹 · 设计定稿提案（QA/策划）

- 日期：2026-09-08（深夜追加，owner 指令"把深海和机械设计完吧"）
- 作者：DeepSeek（测试负责人，兼任策划；08-23 同流程产出 [QA_PROPOSAL_AND_ACCEPTANCE_2026-08-23](./QA_PROPOSAL_AND_ACCEPTANCE_2026-08-23.md)）
- 性质：**策划定稿提案（spec），不改生产 data**。落地交 Codex，平衡/规则终审经 PL 汇总 owner。
- 决策分级：
  - 【L0 已冻结】RULES §12.3/§12.4 人类冻结条款，本档不触碰。
  - 【L1 本次定稿】可直接实施项（待 PL 认可）。
  - 【L2 决策点】需 owner/PL 冻结（HUMAN_REQUIRED）。

---

## 1. 触发与范围

owner 深夜指令：把深海（sea）与机械（machine）两个阵营"设计完"。
两阵营正是今日 closeout 标记为未完成的两处：
- [RULES §12.5](./RULES.md) L296：*"正式 91 卡的 COMMIT/PUSH/PULL 时点效果仍须逐卡批准后接入"*。
- [CURRENT_STATE_2026-09-06](./CURRENT_STATE_2026-09-06.md) L99-100：普通机械随从**尚无已批准的 COMMIT/PUSH/PULL 时点描述**；深海"改成印记"继续 `HUMAN_REQUIRED`（无出处）。

范围收敛结论（基于证据）：
- **机械待完成 = 正式池 8 张机械随从**的 0 费/时点文本逐卡收口；咒文/伏击/惩罚/统领已落地完整（本档逐条核对，见 §4）。
- **深海待完成 = "印记"语义冻结 + 阈值文档不一致清理 + 卡面措辞主题化**；20 张卡数值已落地。
- **不扩池**：设计源 `machine_uploader/compiler/downloader/archivist` 语义并入现有 8 张正式随从，deck 构成保持不变（deck 冻结见 RULES L292；扩池/替换为 L2 可选，本档不默认做）。

---

## 2. 证据与基线

### 2.1 模拟基线（2026-09-08 23:40，`java -cp build/classes;build/test-classes com.dominionwars.test.SimMain 300` → 3600 局）

| 阵营 | 当前胜率 |
|---|---|
| 烈焰 flame | 73.9% |
| 深海 sea | 66.2% |
| 古木 wood | 43.6% |
| **机械 machine** | **16.3%** |

解读（谨慎）：
- 模拟是 AI vs AI。机械轴依赖 AI **主动执行 Commit→Push→Pull** 且地标层级推进；16.3% 既反映机械卡池时点未接、也反映 AI 对下载轴利用不足。此数字是"机械未设计完"的直接数值信号，但**不能**单独当绝对强度结论。
- 深海 66.2% 偏高；弃牌轴 AI 好执行。阈值调整需真人测试佐证（见 §5.1）。

### 2.2 现状核对表（vs 设计源 CARD_DESIGN_BASIC_SET_2026-08-15 §9/§11 + RULES §12）

机械（machine.json，22 entries）：
- 统领 machine_leader 地标 B 模式 + machine_alpha PULL_TOTAL_GE/6 已落地（Codex 09-08 完成），**与 design 源/RULES 一致**。⚠️ alpha 现为 10/12 圣盾扰魔（design 源写 8/10 圣盾）——差异记录，未单独裁决，随重设计批次处理。
- 8 随从（drone/golem/wall/blaster/titan/spark/assembler/recycler）：现为**早期草案态**——全部 `commitCost/uploadCost/downloadCost=1`、`pullEffects=BUFF+1/+1`、**无 commitEffects/pushEffects**、多数 `text=""` 空白、punish 未 0 费化（drone0/spark1/其余2/titan5）。→ 本次 L1 逐卡收口对象。
- 咒文 9（overload/recharge/factory/virus/shield_gen/scan/emp/repair/cannon）+ 伏击 2（trap/null）+ 惩罚 1（punish_core）：**文本/数值已落地完整**，与 design 源一致（仅 deck 用 shield_gen 替换了 design 源的 commit_protocol/pull_protocol 两张协议咒文——已冻结 deck 构成）。咒文保留正常惩罚费，**不适用** 0 费（RULES L271 0 费只针对机械**随从**）。→ 本次不改。
- ⚠️ blaster：data 为 `attacksPerTurn=2`（双重攻击），design 源为 onPlay DAMAGE:1 —— data 草案偏离 design 源；按 data 现有形态收口（双重攻击 = 站场打手身份，本档保留）。

深海（sea.json，21 entries）：
- sea_leader = 赋予生命 25 + `OPP_DISCARD_TOTAL_GE/winParam=18` + DRAW2 + CONVERT_PUNISH_TO_DISCARD —— 数值已落地。
- 20 张卡数值/文本与 design 源 §9 一致（弃牌联动 siren BUFF、warden DAMAGE_CASTLE 已在 data）。
- **阈值文档不一致**（见 §5.1）：data=18、BALANCE.md L25 记 "15→12"、design 源 §5.4/§9.1 写 12。三者冲突，须 owner 一次性冻结。
- **"印记"无出处**：全仓库（docs/design/data）仅 mailbox/CURRENT_STATE/RULES 将其标 HUMAN_REQUIRED，无任何原始定义。判定：这是 PL/Codex 交接时对"海轴胜利/计数可见化方向"的占位转译，**非已冻结机制**。

### 2.3 schema 支持确认（data/schema/cards.schema.json）
- 已支持 `commitCost/uploadCost/downloadCost`（L221-223）与 `commitEffects/pushEffects/pullEffects`（L224-237）。
- EffectAction 枚举含 DRAW/ROLLBACK/BUFF/SUMMON/COMMIT/PUSH/PULL 等（L83-96），EffectTarget 含 SELF/FRIENDLY_MINION 等——本档推荐效果全部可映射。
- WinCondition 枚举**无**"印记"类条件、无潮位资源（L40-49）→ 印证：印记若引入**独立资源/胜利**需 schema+引擎扩展（成本高）；推荐起步为"可见化别名"（§4.2），不扩 schema。
- ⚠️ 实现依赖待 Codex 确认：COMMIT→队列→PUSH FIFO→云栈→PULL 顺序已实现（CURRENT_STATE L98），但 **commitEffects/pushEffects 的结算触发**是否已接通需核实；schema 有字段 ≠ 引擎已结算。

---

## 3. 机械定稿（L1：逐卡时点收口，M1 保留身材/惩罚，M2 另议）

### 3.1 框架（引用 RULES §12.4，不改）
- 出牌让机械随从先进场（普通随从，可站场可攻击）；**提交 = 后续主动动作**（付 commitCost、卡离场入提交队列、触发 commitEffects）；结束阶段自动上传（付 uploadCost、入云栈、触发 pushEffects）；下载 = 用己方机械单位/地标做载体付 downloadCost 拉云栈顶（触发 pullEffects、强化载体、PullCount+1）。
- 下载效果冻结为"写在卡自身、强化目标=下载载体"（本档所有 pullEffects 保持 BUFF+1/+1）。
- 胜利 = PULL_TOTAL_GE 6；地标 tier1 免首 pull 下载费。

### 3.2 设计意图（把 design 源"四轴"语义并入现有 8 卡，不扩池）
上传下载轴要转起来，需要四类角色，现 8 卡全部承担：
1. **站场/载体组**（留场吃下载 BUFF，尽量不提交）：golem、wall、blaster、titan。
2. **提交换牌引擎**（提交瞬间赚牌，鼓励把残血/无价值卡送进队列）：spark ← compiler/uploader 语义。
3. **上传引擎**（进云栈时抽牌，压缩牌库喂云端）：assembler ← compiler 语义。
4. **回滚引擎**（提交时回收队列卡回手，防被针对/复用强卡）：recycler ← archivist 语义。
5. **轴燃料**（0 费+免费上传，廉价 Pull 计数）：drone ← uploader/factory token 语义。

### 3.3 逐卡定稿表（M1；⭐=本次新增/修改；未标=保留现状）

| 卡 id | 身材 | punish(保留) | commit | upload | download | ⭐commitEffects | ⭐pushEffects | pullEffects | 卡面文本（定稿） |
|---|---|---|---|---|---|---|---|---|---|
| machine_drone | 1/1 机偶 | 0 | 1 | **0** | 1 | — | — | BUFF+1/+1 | 登场：抽1。上传免费。下载后选己方机械随从 +1/+1。 |
| machine_golem | 3/4 嘲讽 | 2 | **2** | **1** | **1** | — | — | BUFF+1/+1 | 嘲讽。优先作为下载载体留场。 |
| machine_wall | 0/8 嘲讽 | 2 | **2** | **1** | **1** | — | — | BUFF+1/+1 | 嘲讽。防守墙；可作为下载载体。 |
| machine_blaster | 2/3 双重攻击 | 2 | **3** | **2** | **1** | — | — | BUFF+1/+1 | 每回合可攻击2次。站场打手。 |
| machine_titan | 6/6 嘲讽 | 5 | **3** | **2** | **2** | — | — | BUFF+1/+1 | 嘲讽。大墙；转换代价高，通常留场。 |
| machine_spark | 2/1 突袭 | 1 | 1 | 1 | 1 | ⭐DRAW1 | — | BUFF+1/+1 | 突袭。提交火花机蜂：抽1张。下载后 +1/+1。 |
| machine_assembler | 3/3 | 2 | 1 | 1 | 1 | — | ⭐DRAW1 | BUFF+1/+1 | 组装体上传进云栈：抽1张。下载后 +1/+1。 |
| machine_recycler | 2/5 | 2 | 1 | 1 | 1 | ⭐ROLLBACK1 | — | BUFF+1/+1 | 提交回收单元：将提交队列中一张机械卡回滚回手。 |

费用差异逻辑：
- **留场价值高**（golem/wall/blaster/titan）→ 动作费用抬升（阻碍玩家把它送走、维持惩罚经济：出牌已喂对手，转换再付费）。
- **残血/免费/弱站场**（spark/assembler/recycler/drone）→ 动作费用低（鼓励转上传下载轴）。
- **drone upload=0**：唯一 0 费卡，免费抽1、免费上传、廉价下载 = 轴燃料与 Pull 计数垫，符合 design 源 drone"转换燃料"定位。

### 3.4 协议字段贡献（供 alpha/地标 protocolFields 白名单一致性）
- COMMIT：所有随从（可手动提交）；PUSH：全部被上传卡；PULL：全部被下载卡；ROLLBACK：recycler；CHARGE：spark（突袭）；TAUNT：golem/wall/titan。均落在白名单六字段内（STEALTH/BATTLECRY/DEATHRATTLE 不入，与 RULES §12.4 一致）。

### 3.5 实现依赖（转 Codex 确认）
1. commitEffects/pushEffects 结算触发路径是否已接通（schema 已支持字段）。
2. ROLLBACK 作为 commitEffects 动作的合法目标选择（队列中自选 1 张）。
3. 落地后跑 `SimMain` 验证机械胜率回归（目标 40–60% 带内，AI 下载轴策略可能仍需增强——若 AI 不会执行 Commit/Pull 则此指标失真，需人工/规则用例兜底）。

### 3.6 M2（L2 决策点，本档不动）
RULES L271 "机械随从出牌惩罚值=0（全家 0 费）+ 基础身材偏低"是已冻结的**重设计基线**，但现 8 随从身材（0/8、6/6 等）**非"偏低"**，若字面执行 0 费会破坏惩罚经济（6/6 免费出+不喂对手）。正确终态 = 0 费 + 大幅削身材 + 强度全部进动作轴，属**卡池级重平衡**，需平衡模拟与 owner 定夺。**建议以 M1（保留现身材/惩罚、只补时点）作为可立即执行的中间态**，待真人数据后再议 M2。

---

## 4. 深海定稿

### 4.1 现状
sea_leader 弃牌胜利 `OPP_DISCARD_TOTAL_GE/18` + 赋予生命 25 + CONVERT_PUNISH_TO_DISCARD；20 卡弃牌轴已落地（tide/whirl/depths/abyss_call 弃牌、siren 联动 BUFF、warden 弃牌→王城伤、devour 反抽、ink 反制等）。主胜利轴明确为弃牌（RULES L245），潮位未启用（§12.3 候选）。

### 4.2 "印记"语义推荐定稿（L1 推荐 + L2 请冻结）
出处不明 + schema 无独立资源 ⇒ **推荐把"印记"冻结为弃牌胜利计数的可见化/主题化表现层，不引入第二资源**：

> **海之印记（Sea Mark）**：每当对方因深海卡牌或深海统领效果（含 CONVERT_PUNISH_TO_DISCARD 的惩罚转化弃牌）弃置 1 张手牌，对方获得 1 枚【海之印记】——一枚公开计数器，作为深海统领 `OPP_DISCARD_TOTAL_GE` 的判定别名。印记只增不减、无独立结算、不触发卡牌联动（海妖 BUFF 等仍以"弃置事件"为触发，不以印记数触发，防同一事件重复推导，遵守 RULES L241）；对方强制手牌上限弃牌**不**产生印记（RULES L246/§9 防挂机）。当印记 ≥ 阈值（当前 18）时深海获胜。
>
> 落地范围：① RULES §12.3 增一句措辞（弃牌胜利计数在规则/UI 中命名为"海之印记"）；② 海卡面与 leader winText 统一用"施加印记/印记×N"表述；③ UI/渲染把统领弃牌计数显示为「海之印记 N/18」（runtime 只读已有 winParam）。**不新增 schema 字段、不新增引擎资源、不改任何数值**。

理由：无出处 + 零引擎成本 + 不破坏既有 20 卡落地 + 立即可见化；若 owner 想要的"印记"是**可消耗的独立资源/第二胜利轴**（更强玩法），属 L2 新机制，需另行 schema+引擎设计，本档给候选但不默认实施。

候选（若 owner 要"印记≠弃牌计数"）：
- 印记 = 敌方玩家身上的可消耗诅咒层：海卡可明示"消耗 N 印记"触发加强效果（如 pressure 升级为弑君、ink 额外反制）——新增玩家级资源，schema/引擎需扩展。
- 印记 = 海统领降临时读取的债务（即 RULES L244 潮汐债务的别名）——潮汐债务本身仍 [候选]。

### 4.3 阈值核对报告（L2 请 owner 一次性冻结）

| 出处 | 值 | 备注 |
|---|---|---|
| data/cards/sea.json（运行时权威） | **18** | QA/当前引擎实际值 |
| BALANCE.md L25 | "15→12" | 2026-06 旧平衡记录，未反映后续回调 |
| CARD_DESIGN_BASIC_SET §5.4/§9.1 | 12 | 2026-08-15 文本，已过期 |

建议：**冻结 18 为当前权威**。理由：当前模拟深海 66.2% 已偏高（虽受 AI 弃牌流影响），调低阈值会进一步强化；18 亦与 RULES L246"弃牌胜利阈值未冻结"形成对照，一次性锁定避免文档分叉。→ 需同步修正 BALANCE.md L25 与 design 源 §5.4/§9.1 的过期值。

### 4.4 海卡本体（L1 微调，数值不动）
20 张卡数值已落地且符合 RULES §12.3（低惩罚铺场 + 弃牌干扰 + 弃牌联动）。建议仅做**措辞主题化**（可选、表现层，Codex 排期宽松时做）：
- 效果弃牌类（tide/depths/abyss_call/siren/maelstrom 未入池）文本统一为"……，对其施加 1/2/3 枚海之印记（弃置其手牌）"。
- leader winText 改"对方海之印记 ≥ 18 时你获胜"。
数值、target、触发一律不变。

---

## 5. 决策点清单（交 owner/PL）

| # | 议题 | 建议 | 等级 |
|---|---|---|---|
| D1 | 机械 8 随从按 M1 补时点（§3.3 表）是否批准实施 | 批准（可立即执行，不动 deck/数值结构） | L1→需 PL 认 |
| D2 | "印记"语义 = 弃牌胜利可见化别名（§4.2 推荐）是否冻结 | 冻结推荐案 | L2 |
| D3 | 海弃牌阈值权威值 | 冻结 18 并修正 BALANCE/design 源 | L2 |
| D4 | 机械 0 费全卡迁移（M2）是否列入后续重平衡 | 列 backlog，不立即做 | L2 |
| D5 | alpha 身材 10/12 vs design 源 8/10 差异 | 随统领重设计批次统一裁决 | L2 |
| D6 | machine_alpha 现 0 费随从转换代价/α 召唤节奏是否符合设计预期 | 落地 M1 后模拟验证 | L1 |

---

## 6. 验收用例（落地后 DeepSeek QA 执行）

机械（映射现有回归/模拟）：
1. M1-SPARK-1：场上 spark 突袭后手动提交，支付 commitCost=1 → 触发 DRAW1，spark 离场入提交队列。
2. M1-ASM-1：提交 assembler 后结束阶段自动上传，支付 uploadCost=1 → 进入云栈瞬间触发 DRAW1。
3. M1-RECY-1：提交 recycler 时队列另有 ≥1 机械卡 → ROLLBACK 将该卡回手。
4. M1-DL-1：云栈顶卡 downloadCost 生效（付费才拉取）；地标 tier1 首次 PULL 免该次费用。
5. M1-WIN-1：累计 6 次有效 Pull（云端栈顶成功拉取结算）→ alpha 或地标触发 PULL_TOTAL_GE=6 胜利。
6. M1-SIM：落地后 `SimMain 300` 机械胜率相对 16.3% 明显回升（AI 可用下载轴前提下目标 40–60% 带内）。
7. M1-SCHEMA：machine.json 全卡通过 data/schema/cards.schema.json（91/91）与 deck 校验（4/4）。

深海：
8. SEA-MARK-1（文本/规则层）：RULES §12.3 含"海之印记"措辞；leader winText 同步。
9. SEA-MARK-2（表现层可选）：UI 弃牌计数以「海之印记 N/18」呈现。
10. SEA-THR-1：阈值冻结值写入 BALANCE.md/design 源与 data 一致（18），无文档分叉。
11. SEA-REG：现有 21 卡 schema/回归不受措辞改动影响（数值未动，应全绿）。

---

## 7. 交接

- **Codex**：实施 §3.3 M1 逐卡（8 张机械随从补 commit/upload/download 差异 + spark/assembler/recycler 时点效果 + 全卡文本）；确认 §3.5 实现依赖；落地后自跑 schema/deck/regression。
- **PL**：审 D1–D6，汇总 owner 冻结 D2/D3/D4/D5。
- **QA（DeepSeek 本人）**：Codex 落地后执行 §6 验收；同步把模拟/文档证据归档 mailbox。
- **本档未改任何生产 data / RULES 数值**；RULES §12.3/§12.4 冻结条款原样保留。
