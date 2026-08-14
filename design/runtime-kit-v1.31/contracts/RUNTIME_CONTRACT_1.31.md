# Canonical Runtime Contract v1.31（Draft → 决策已回填）

## 0. 文档状态与负责人

| 项 | 值 |
|---|---|
| 版本 | 1.31 |
| 状态 | **DRAFT→决策已回填** — 人类裁决 2026-08-14 已下达，A-E 已回填；残留 1 项 HUMAN_REQUIRED（§6） |
| Gate | **解除 BLOCKED**（A-E 已答）→ 残留 1 项 HUMAN_REQUIRED 不阻塞 Step 1 schema 主体 |
| 起草 | PL（DeepSeek v4），2026-08-13 晚 |
| 批准 | 人类负责人（2026-08-14 已裁决，见 §9） |
| 依赖 | `ARCHITECTURE_REVIEW.md` §10 Step 0-8、`docs/RULES.md`、`docs/SPEC.md`、Java 权威实现 |

## 1. 目的

定义 **Unity 表现层 ↔ C# 规则引擎**之间的权威数据接口（wire contract），并作为 Adapter Integration（WBS 10.10.2+，ARCHITECTURE_REVIEW §10 Step 1-8）的锁定基线。

相对 1.30 的两件事：
1. 把 1.30 的结构化 JSON schema 升级为**严格可机器验证**的 wire schema（Step 1 由实现者细化）。
2. **书面回答** ARCHITECTURE_REVIEW §11 的 12 条待确认项；已定的直接定，需产品判断的标 `HUMAN_REQUIRED` 收敛给人类 + QA/策划，绝不自行虚构规则。

## 2. canonical-current 唯一入口

- **入口**：`design/runtime-kit-v1.31/contracts/README_FIRST.md`
- **$id grammar**：`urn:dw:runtime:contract:1.31:<name>`
- 旧 `design/runtime-kit-v1.30/` = 历史基线，不再作为 canonical-current 入口；1.30 schema 在 1.31 批准前**不改**（ARCHITECTURE_REVIEW §9"禁止猜测升级"）。

## 3. design version 与 contractVersion 的关系

| 概念 | 语义 | 当前值 |
|---|---|---|
| `contractVersion`（整数） | **wire format 兼容性信号**。相同整数 = wire 兼容；不同整数 = wire 不兼容（破坏性升级） | `1` |
| `design package version`（1.x） | 整套合同文档包的语义版本，用于人可读变更管理 | `1.31` |

**规则**：
- 每个 wire message（Snapshot / Action / Event / ActionResult）**必须携带当前 `contractVersion`**（SPEC §4.7 fail-closed）。
- 兼容性新增/澄清 → `contractVersion` 不变，`design version` 递增次版本（1.30 → 1.31）。
- 破坏性 wire 变更 → `contractVersion` 递增（1 → 2），`design version` 同步升主版本。
- 运行时/校验以**整数 `contractVersion`** 为准；文档以 `design version` 为人可读标签。

## 4. 1.30 → 1.31 兼容性判定

**结论：兼容新增 + 澄清修正，不是破坏性升级，wire break 尚未发生。**

- 新增：strict wire schema（discriminated payload）、ActionResult、明确 ID grammar、revision/idempotency 语义、redaction 规则。
- 澄清修正：把 1.30 的 `required/optional/nullable`、unknown-field、numeric width 等隐式约定书面化。
- 不改变：1.30 已定 7 条 principles、Screen/Phase/Layout/Motion/Asset/Localization/Accessibility 固定项、JSON 卡牌唯一数据源。
- 约束：**在 1.31 获得批准前不得修改任何 1.30 schema 文件**。

## 5. 12 条待确认项 — 逐条书面回答

> 裁决列：`已定`（依据见出处，直接锁定）/ `HUMAN_REQUIRED`（等人类 + QA/策划，见 §6）。

| # | 问题 | 裁决 | 类型 | 依据 |
|---|---|---|---|---|
| 1 | 1.31 实际文件、负责人、变更清单、批准记录在哪？ | 本目录 = `design/runtime-kit-v1.31/contracts/`；主文档 = 本文件；入口 = README_FIRST.md；负责人 = PL 起草 + 人类批准；变更清单 = §8；批准记录 = §9 | 已定 | ARCHITECTURE_REVIEW §10 Step 0 |
| 2 | Unity 最终运行时 = Java 还是 C#？取代则谁批准、parity 范围、迁移 Gate？ | **C# Engine = sole runtime；Java = parity-only**（WBS 10.10.0 已确认）。批准记录位置 = PROGRESS_WBS.md 10.10.0。parity 范围 = Java Effects.java 24 case 行为对齐（DeepSeek Batch1 QA 已确认 0 divergence）。迁移 Gate = parity tests 全绿 + Step 8 Adapter Integration Gate | 已定 | WBS 10.10.0 / SPEC §4 |
| 3 | 1.30/1.31 与整数 contractVersion=1 的关系？ | 见 §3：整数 = wire 兼容信号；design version = 文档语义版本。1.30/1.31 同为 contractVersion=1 | 已定 | SPEC §4.7 |
| 4 | serializer / lowerCamelCase / null·omitted / unknown-field / numeric width？ | 见下"§5.4 wire 策略" | 已定 | SPEC §4 / 1.30 schema 观察 |
| 5 | LegalAction 与 GameAction 同一 schema 两方向，还是两类型？ | **同一基础 GameAction 结构的两个方向**：LegalAction = 引擎→UI 的"已发布合法动作广告"（含 actionId/type/payload/snapshotRevision）；GameAction = UI→引擎的"提交动作"（须与当前 LegalActionSet 完全匹配）。schema 共享 definitions 块 + 两个受约束变体 | 已定 | ARCHITECTURE_REVIEW §10 Step 1/3 |
| 6 | snapshotRevision 是否强制；stale/duplicate/idempotency？ | 强制。Snapshot 单调递增 revision（SPEC §5）。action 携带提交时所见 revision：`< 当前` → reject（STALE_SNAPSHOT，无副作用）；与当前 LegalActionSet 不匹配 → reject（NOT_ADVERTISED）；重复 actionId → 幂等返回缓存 ActionResult，不重复结算。actionId = matchId + 单调序号（或 GUID），引擎按 actionId dedupe | 已定 | SPEC §5.1 / ARCHITECTURE_REVIEW §10 Step 3 |
| 7 | action 集是否含 CHOOSE_TARGET / ACTIVATE_PUNISH / USE_LEADER_ABILITY？prompt answer 统一为 GameAction？ | prompt answer **统一为 GameAction**（UI 只有唯一提交入口）。**已定（人类裁决+A-E）**：CHOOSE_TARGET 是出牌/攻击的子步骤非独立动作（砍）；ACTIVATE_PUNISH 保持纯被动无主动激活（砍）；USE_LEADER_ABILITY 不进 MVP（91 卡无 activated_ability 字段）。玩家主动动作集 = 盖放伏击/出牌/攻击/结束回合 | 已定 | 人类裁决 mailbox L3495；RULES_QUESTIONS B |
| 8 | entity/leader/castle/player life/prompt option 的 target union 与稳定 ID grammar？ | ID grammar → 已定（基于 SPEC §5）：卡牌 ID = JSON 字符串常量；对局实体 ID = 引擎 64-bit counter（单局稳定，跨局不复用）；eventId = 单调 int64；actionId = 提交时分配；player = `player_0/player_1`；leader = `leader_0/leader_1`；castle = `castle`。**target union 已定（A-E）**：8 值 ENEMY_FACE/ENEMY_TARGET/ENEMY_MINION/ALL_ENEMY_MINIONS/ALL_FRIENDLY_MINIONS/FRIENDLY_MINION/ALL_MINIONS/SELF；王城用 DAMAGE_CASTLE action 非独立 target；正交维度 = 侧×对象×范围×约束（嘲讽/扰魔/护卫） | 已定 | SPEC §5；RULES_QUESTIONS C |
| 9 | viewer/audience、隐藏手牌、伏击、牌库顺序、spectator redaction？ | **已定（A-E+人类裁决）**：双 viewer 投影，每 viewer 只收自己可见字段；对手手牌**数量可见/内容隐藏**；盖放伏击**只显数量+档位（普通/专注/封场）**；牌库顺序秘密，任何到达 Unity = Gate 失败 | 已定 | 人类裁决 mailbox L3495；RULES_QUESTIONS D |
| 10 | UIEvent 完整枚举、每类 payload、内部过滤、ancestry compression、ActionResult/拒绝事件分工？ | 枚举 = 1.30 已有 22 个（≥ SPEC ≥21）。payload = 每 type 一个 discriminated union（oneOf data schema，Step 1 细化）。ancestry = 默认只带 parentEventId 单引用，根事件 parent=null，不发送全祖先数组。ActionResult = 提交 action 的权威同步响应（accepted/rejected + reason + 新 revision 或错误码）；拒绝可同时发对应 UI 事件（如 TARGET_REJECTED），职责分离。内部事件过滤清单 → 待 Step 4 Java 投影时细化，未知类型一律 fail-closed，不静默丢弃 | 大部分已定 | SPEC §9.3；ARCHITECTURE_REVIEW §10 Step 4 |
| 11 | Castle 在首个 Unity MVP 是否启用？禁用如何表达？ | **已定（人类裁决）**：MVP **启用**，双方共用、中立、无攻击力、推荐初始生命 75；破城后破城方胜利计数≥9。禁用表达保留技术方案：`snapshot.castle = null`（或 `{enabled:false}`）；CASTLE_* 事件不发 | 已定 | 人类裁决 mailbox L3495；RULES_QUESTIONS A |
| 12 | Java 作为 Unity runtime 的打包/进程/IPC/JRE 边界？ | 在 C# sole runtime（#2）决策下**不适用**：Unity runtime = C# Engine assembly（DLL），无 Java/JRE、无 IPC 边界。若未来改回 Java runtime 需重新评估，不在本合同范围 | 已定 | WBS 10.10.0 |

### §5.4 wire 策略（对应 #4）

- **serializer**：引擎无关；schema 为权威。C# 侧已用 Newtonsoft.Json（Codex 迁移完成），合同不锁定具体库。
- **naming**：`lowerCamelCase`（与 1.30 schema 一致）。
- **null/omitted 三态**：`required`（必须有非 null）/ `optional`（可省略，省略视同 null）/ `nullable`（显式 null 允许）。schema 中逐字段表达，省略与 null 等价策略按字段标注。
- **unknown-field**：**fail-closed** —— 与已知 schema 不匹配的未知字段使该 message 无效（不得静默降级，ARCHITECTURE_REVIEW §9）。
- **numeric width**：integer = 64-bit（JSON 无小数）；float = 64-bit double；无 NaN/Infinity（序列化即失败）。

## 6. HUMAN_REQUIRED 决策清单（已回填 + 残留）

**A-E 已于 2026-08-14 回填**（人类裁决 mailbox L3495 + DeepSeek A-E 答案 mailbox L2793-2798），全部转入主文档 §5 表格：

- A. Castle：MVP 启用，双方共用中立无攻击、推荐初始生命 75、破城计数≥9 ✅
- B. 动作集：CHOOSE_TARGET=子步骤、ACTIVATE_PUNISH=纯被动、USE_LEADER_ABILITY 不进 MVP ✅
- C. 目标枚举：8 值 + DAMAGE_CASTLE action（非独立 target）✅
- D. 隐藏信息：手牌数量可见/内容隐藏、伏击只显数量+档位、牌库顺序保密 ✅
- E. Buff 负值：RULES §11.1 确认；amount=0 fail-closed 拒绝启动（PL 裁定）✅

**残留 HUMAN_REQUIRED（不阻塞 Step 1 schema 主体，阻塞完整冻结）**：
- **shadow_of_fate 可达胜利条件**：评审⑬ Q1 已定"只有随从型首领可被击败"，非随从首领只能靠自身 winCondition 获胜。shadow_of_fate（`winCondition=NONE`）作为非随从首领，不可被击败，因此必须有一个可达的显式 winCondition 才能获胜。老板仍在考虑（Q5 标注 shadow NONE 暂缓）；DeepSeek 曾建议 OPP_PUNISH_TRIGGERED_GE 6（评审⑫ D 类）仍未落盘。**待人类 + PL 拍板 shadow 的可达胜利条件**，不虚构。
- **machine_alpha 胜利条件定稿**：评审⑬ Q5 已定改为**上传/下载相关轴**（替换 `OPP_PUNISH_DRAW_TURN_GE`），待老板定稿后回填 data/cards + RULES §12.4 + 本合同；定稿前保持当前值。

**胜利体系决策链（评审⑬，2026-08-14）已闭合项**：
- Q1 只有随从首领可被击败（随从首领获更强效果补偿）；非随从首领只能靠 winCondition 获胜
- Q2 随从首领靠击破王城获胜（收益导向）；开局即展示双方首领与胜利效果
- Q3 非随从首领也能攻王城，破城不立即获胜，只吃通用软效果（胜利计数≥9 + 叫出自己首领）
- Q4 拆两层：通用层（破城→胜利计数≥9 + 叫出自己首领）+ 首领胜利条件层（王城被破坏即触发持有该条件的首领获胜，被动）；Q4a 破城方叫出自己首领；Q4b 双方均随从首领局主动破城方直接获胜
- Q5 per-leader 显式：flame=ROYAL_CASTLE_BREAK（保留，无改动）；machine_alpha=上传/下载轴（待定稿）；shadow=NONE（老板考虑中）。**勿把"破城即胜"写成全局内置规则**

## 7. Step 0 验收自查（对照 ARCHITECTURE_REVIEW §10 Step 0）

| 验收 | 状态 |
|---|---|
| 仓库只有一个 canonical-current 入口；schema $id/版本可机器读取 | ✅ README_FIRST.md + $id grammar 已定义（schema 文件本体在 Step 1） |
| 明确 design package version 与 contractVersion 关系 | ✅ §3 |
| 明确 1.30→1.31 是兼容新增/破坏性/尚未发生 | ✅ §4（兼容新增+澄清，contractVersion=1 不变） |
| 待确认项均有书面答案 | ✅ A-E 已回填（人类裁决 L3495 + DeepSeek A-E L2793）；残留 1 项 HUMAN_REQUIRED（shadow_of_fate，§6）不阻塞 Step 1 主体 |

## 8. 变更清单（Changelog）

| 版本 | 日期 | 变更 |
|---|---|---|
| 1.31-draft | 2026-08-13 | 创建 canonical-current 入口 + 主文档；书面回答 12 条待确认项；明确版本规则与 1.30→1.31 兼容性；A-E 规则问题待策划 |
| 1.31-decision | 2026-08-14 | 回填人类裁决 + DeepSeek A-E 答案（§5 #7/#8/#9/#11 转已定）；目录迁移 `runtime-contract-v1.31/` → `runtime-kit-v1.31/contracts/`（与 v1.30 命名体系对齐）；Gate 解除 BLOCKED；残留 1 项 HUMAN_REQUIRED（shadow_of_fate） |
| 1.31-victory | 2026-08-14 | 回填胜利体系决策链（评审⑬ Q1–Q5）：只有随从首领可被击败；王城被破坏被动触发持有 ROYAL_CASTLE_BREAK 的首领获胜；破城方叫出自己首领；双方随从首领局主动破城方直接获胜；per-leader 显式（flame 保留 / alpha 上传下载轴待定稿 / shadow 考虑中）。RULES §7/§9.1/§12.4 同步；勿写全局内置"破城即胜" |

## 9. 批准记录

- [x] 人类负责人裁决 2026-08-14（mailbox L3495）：批准按 Runtime Contract 1.31 推进，C# Engine = Unity 运行时权威，Java 仅 parity；不新增独立费用系统但保留扩展接口；王城共用中立无攻击 75；牌库循环胜利阈值 10、破城计数≥9；动作/目标/可见性按 A-E 收敛。
- [x] QA/策划 A-E 答案 2026-08-13/14 已回填（mailbox L2793-2798）。
- [ ] 残留：shadow_of_fate 可达胜利条件（§6）待人类 + PL 拍板（老板考虑中）。
- [ ] machine_alpha 上传/下载胜利条件定稿（评审⑬ Q5，§6 跟踪）。
- 胜利体系决策链评审⑬（2026-08-14）Q1–Q5 已确认并回填 RULES §7/§9.1/§12.4 + 本合同 §6（见 changelog 1.31-victory）。
- 批准后：Gate 已解除 BLOCKED → 实现者可进入 Step 1（strict wire schema）；shadow 语义作为独立 HUMAN_REQUIRED 单点跟踪。
