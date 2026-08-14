# Dominion Wars 一次性架构审查

审查日期：2026-08-13  
审查性质：只读架构审查；不代表实现、QA 签字或后续开发授权  
审查结论：**Adapter Integration Gate 当前不得判定为通过。** 仓库内没有可识别、可验证的 Canonical Runtime Contract 1.31；现存 canonical 文件自报 `1.30`，当前 Java、C# Adapter 与 Unity 骨架也尚未形成一条闭合且唯一的运行时链路。

## 1. 审查范围与证据

本次静态检查覆盖：

- `AGENTS.md`、`docs/RULES.md`、`docs/DESIGN.md`、`docs/SPEC.md` 与当前 WBS/审查报告；
- `design/runtime-kit-v1.30/contracts/` 下的 runtime、snapshot、action、event、phase、component、layout、motion 等合同；
- Java 权威行为基线：`src/main/java/com/dominionwars/engine/`、`server/GameSession.java`、`server/WebServer.java`；
- C# 运行时基础：`src/Engine/`、`src/Adapters/` 及其测试；
- Unity 工程骨架：`unity/DominionWars.Unity/Packages/`、asmdef 与 EditMode smoke 文件。

本次没有运行会生成或改写构建产物的测试，也没有将已有报告中的历史通过数字当作本次验证结果。以下结论基于当前工作树的静态证据，包括尚未提交的现状。

## 2. 当前架构摘要

当前仓库同时存在两条尚未汇合的路径：

```text
现有 Java 路径
Web UI -> /api/cmd 私有命令 -> Java Game（规则行为基线）
       <- /api/state 私有 JSON <- GameSession.publish()
                              <- 从中文日志推导的临时动画事件

现有 C#/Unity 路径
Unity EditMode smoke -> C# GameState / TurnFlow / TurnActionRouter
                    -> EngineProjectionAdapter -> C# DTO
```

两条路径之间目前没有 Java Engine → canonical `GameSnapshot` / `UIEvent` / `GameAction` 的正式桥接：

- Java `GameSession.publish()` 输出 `version/current/pending/logs/events` 等私有字段，不输出 canonical `matchId/currentPlayer/legalActions/contractVersion`。
- Java `/api/cmd` 接收 `play/ambush/attack/endAmbush/endTurn/answer` 等私有命令，不接收 canonical `GameAction`。
- Java 动画事件由 `collectEvents()` 解析中文日志得到，没有 `eventId`、`parentEventId`、事件发生时的 turn/phase 或可靠重放语义。
- `src/Adapters/EngineProjectionAdapter.cs` 的输入是 **C# `GameState`**，不是 Java Engine；它是 C# 引擎投影器，不是已完成的 Java → Unity Adapter。
- Unity 工程通过 UPM manifest 声明 Engine/Data/Adapters 三个本地包，但 `packages-lock.json` 没有这三个本地包条目；Assets 下只有 EditMode smoke，没有运行时 Bootstrap、Session、Transport、Adapter 或 UI 消费链路。

因此，`IMPLEMENTATION_TODO.csv` 中“Implement snapshot/action/event adapters against current Java engine = done”与实际代码不一致，不能作为 Gate 证据。

## 3. Canonical Runtime Contract 1.31 自洽性审查

### 3.1 结论

**无法确认 1.31 自洽，因为 1.31 合同未在仓库落盘。** 当前可审查的合同是：

- 目录：`design/runtime-kit-v1.30/`
- `runtime_contract.json`：`version = "1.30"`
- `ENGINE_ADAPTER_CONTRACT.md`：标题为 v1.30
- battle、screen、component、layout、motion、localization 合同均为 `1.30`
- C# DTO/Guard 使用另一维度的整数 `contractVersion = 1`

`1.30` 与 `contractVersion = 1` 可以是“设计包版本”和“wire major version”两套不同版本，但仓库没有机器可读规则解释二者关系；更没有 1.31 的版本声明、变更清单、兼容策略或 schema ID。任何开发智能体都不得自行把现有文件重命名或口头视为 1.31。

### 3.2 当前合同内部不自洽点

| 项 | 当前证据 | 判定 |
|---|---|---|
| 版本字段 | 三个 wire schema 都没有 `contractVersion`；C# DTO 强制默认 1；SPEC 要求 Snapshot/Action/Event 都携带版本 | P0 不一致 |
| Snapshot revision | SPEC 要求 `snapshotRevision` 且 action 仅对该 revision 有效；canonical schema 与 DTO 均无该字段 | P0 缺失 |
| Wire 命名 | schema 使用 lowerCamelCase；默认 `System.Text.Json` DTO 序列化为 PascalCase，当前测试未设置命名策略也未按 schema 校验 wire JSON | P0 不一致 |
| Snapshot 子结构 | `players`、`castle`、`legalActions` 的 items 只是无约束 `object`；未定义卡牌、区域、隐藏信息、胜利状态、目标引用 | P1 欠定义 |
| GameAction / LegalAction | runtime 输入叫 `LegalActionSet`、输出叫 `GameAction`，但只有 `game_action.schema.json`；没有独立 LegalAction schema 或二者的关联约束 | P0 欠定义 |
| Action 类型 | schema 包含 `SET_AMBUSH/SKIP_AMBUSH/CHOOSE_TARGET/DISCARD`；C# 另有 `USE_LEADER_ABILITY/ACTIVATE_PUNISH`，且 `SET_AMBUSH` 尚无默认 handler | P0 不一致 |
| Target 模型 | `sourceId/targetId/cardId/payload` 为松散字段；未定义 entity、leader、shared castle、player life、prompt option 的联合类型 | P0 欠定义 |
| Event payload | event `data` 是任意 object，不是按 `type` 区分的联合结构；所需字段、可见性和数字语义无法验证 | P1 欠定义 |
| Event 名称 | UI schema 22 类；C# map 只覆盖一部分，另有内部事件被批量投影时静默过滤 | P0 不完整 |
| Event 时间 | C# `ToEvent/ToEvents` 由调用者传入一个 turn/phase；历史事件会被盖上投影时的当前阶段，而非事件发生阶段 | P0 语义错误风险 |
| 可见性 | component contract 要求 `ownerVisibility`；snapshot schema 没有 viewer/audience 规则；C# Snapshot 对两个玩家都投影完整 Deck/Hand | P0 信息泄漏风险 |
| 本地化 | runtime 原则要求可见文本使用 localization key；C# CardDto 直接携带 `Name/Text/Flavor`，Java pending prompt 和错误为中文文本 | P1 边界泄漏 |
| Schema 严格度 | 顶层和子结构普遍未设 `additionalProperties: false`，也没有跨字段约束 | P1 无法 fail-closed |

### 3.3 当前可确认的自洽部分

- phase 主序列 `START → AMBUSH → ACTION → DISCARD → END` 与 snapshot phase 枚举一致，`OVER` 作为终局状态存在。
- canonical 层明确禁止 DOM、CSS、Unity GameObject path、Godot NodePath 等 renderer 标识泄漏。
- snapshot、legal actions、event stream 作为表现输入，GameAction 作为游戏输入的总体方向正确。
- `eventId/parentEventId`、稳定 entity ID、renderer 不推断规则等原则方向正确。

这些只是方向一致，不足以替代完整 wire contract。

## 4. Java Engine → GameSnapshot 映射审查

当前 Java 快照只能作为旧 Web prototype 的 ViewModel，不能直接声明为 canonical `GameSnapshot`。

### 4.1 可机械映射的字段

| Java 当前字段 | canonical 候选 | 备注 |
|---|---|---|
| `turn` | `turn` | 类型可对齐 |
| `phase` | `phase` | 枚举需逐值 Gate |
| `current` | `currentPlayer` | 需改名或显式投影 |
| `players[2]` | `players[2]` | 子结构必须先正式定义 |
| `castle.enabled/hp` | `castle.enabled/health` | `max/breaker` 是否进入合同待确认 |
| `pending` | `pendingPrompt` | 当前 id/choice-index/中文 prompt 不能直接定为 canonical |
| `version` | `snapshotRevision` 候选 | 当前仅 publish 递增；生命周期、溢出、重连语义未立约 |

### 4.2 不可直接映射或缺失

- `matchId`：Java 当前缺失。
- `contractVersion`：Java snapshot/action/event 都缺失。
- `legalActions`：Java 只把 `usable/why/canAttack` 混入卡牌 ViewModel，并在 WebServer 重做一遍合法性检查；没有完整、稳定、按当前 revision 绑定的 LegalActionSet。
- 稳定 entity ID：`GameSession` 又维护一套 `IdentityHashMap<CardInstance,Integer>`，而 `CardInstance` 自身已有静态 uid；两套 ID 来源且格式不符合 canonical 稳定字符串约定。
- 隐藏信息：当前 Java 快照按 view 裁剪，但合同没有定义 viewer；C# 快照则暴露双方完整区域。必须由投影层统一执行 visibility policy，不能交给 UI 自行隐藏。
- 玩家/统领/伏击/墓地/胜利进度：Java 有部分字段，canonical schema 未规定完整结构。
- 日志：`logs` 是可见文本，不应成为规则或动画的 canonical 数据源。

### 4.3 必须采用的映射原则

1. Java Game 保持规则权威；canonical projection 只读取已结算状态，不推导合法性、胜负、目标或阶段。
2. Snapshot 必须以明确的 `viewerPlayerId`/audience 生成；不可先生成全知快照再依赖 Unity 隐藏。
3. Snapshot revision 必须由对局会话拥有，随每次可观察状态提交单调递增；match 内唯一，重开 match 重置并更换 matchId。
4. entity/action/event ID 的来源、格式和生命周期必须只有一套定义。
5. `legalActions` 必须由 Java 权威规则路径生成；Unity 只展示和回传其中一个 action，不凭卡牌字段重建动作。
6. Snapshot 必须是不可变的一致视图；不得在 HTTP 线程上读取一半旧、一半新的 Game 状态。

## 5. GameAction 输入与 UIEvent 输出边界

### 5.1 GameAction 输入

正确边界应为：Unity 提交“当前快照中由引擎发布过的一个动作”，Java 在对局线程内再次验证并原子执行，然后返回结构化 ActionResult；Unity 不直接调用 `playFromHand/attack/endTurn`。

当前缺口：

- 没有 Java canonical ActionAdapter；`/api/cmd` 是另一套命令协议。
- action 不携带 `matchId + snapshotRevision`，无法可靠拒绝陈旧点击、重复提交或跨局 action。
- C# `TurnActionRouter` 可校验 phase/actor/部分 actionId，但没有证明 request 就是当前 LegalActionSet 的成员。
- Java WebServer 在 HTTP 线程先检查 live state，再把 Runnable 排队；接受与实际执行之间存在状态变化窗口，并且先返回 `ok`、后执行。
- `answer` 的 prompt id + option index 与 canonical `CHOOSE_TARGET`/GameAction 不统一。
- `payload`、批量弃牌、核心目标、伏击、惩罚响应、统领能力没有正式 discriminated payload。

最低安全输入应包含：`contractVersion`、`matchId`、`snapshotRevision`、`actionId`、`type`、`actor`，以及按 action type 定义的 payload。服务端必须拒绝 unknown、wrong-match、stale/future revision、actor mismatch、actionId/type/payload mismatch、重复执行和不在当前 LegalActionSet 中的请求。

### 5.2 UIEvent 输出

正确边界应为：Java 规则执行时直接写结构化 domain event；EventAdapter 仅做名称/ID/可见性投影；Unity 按 eventId 去重、按 parentEventId 展示因果、按 snapshotRevision 对齐状态。

当前缺口：

- Java 从中文日志反向识别动画事件，文本变化会破坏事件语义。
- Java event 没有稳定 ID、父链、revision、turn、phase、source/target 的稳定引用。
- `recentEvents` 每次 publish 清空；轮询丢帧时事件不可恢复，也没有 cursor/gap 检测。
- C# EventLog 有单调 ID/父链基础，但事件自身不携带发生时 turn/phase/revision；Adapter 使用调用者当前值补写。
- C# `ToEvent()` 对未知事件 fail-closed，而 `ToEvents()` 对未知事件静默过滤；两条 API 的失败策略冲突。
- schema 没有规定 ActionResult/拒绝事件边界，`TARGET_REJECTED` 的归属也未明确。

禁止把 `logs`、异常文本或本地化文案当作 UIEvent payload 的权威字段。

## 6. Unity Adapter 职责与目录结构

### 6.1 Unity Adapter 应负责

- 建立/关闭一个 runtime session，读取 canonical Snapshot/Event，提交 canonical GameAction；
- 在进入 Unity 表现层前校验 contractVersion、matchId、revision、schema、ID 格式和事件游标；失败时 fail-closed；
- 保存“最新已接受 Snapshot + 当前 LegalActionSet + 已消费 event cursor”的只读 presentation state；
- 将后台 transport 回调切回 Unity main thread；处理取消、超时、断线、重连和事件缺口；
- 只允许 UI 提交当前 LegalActionSet 中的 action；统一点击、拖拽、键盘和无障碍输入路径；
- 将 canonical logical asset/localization/motion/layout ID 路由到各专用 adapter；
- 提供可替换的 fake session/golden fixture，供 EditMode/PlayMode 验证。

### 6.2 Unity Adapter 不应负责

- 计算 legality、目标候选、惩罚、伤害、死亡、胜利、阶段推进、伏击窗口或 AI；
- 解析中文/英文日志来决定动画或状态；
- 读取卡牌 ID 并写规则特判；
- 通过隐藏 GameObject 来补救服务端泄露的隐藏信息；
- 将 Unity Instance ID、GameObject path、MonoBehaviour 或 Sprite 引用写入 canonical DTO；
- 同时维护 Java 规则和 C# 规则两套运行时真相。

### 6.3 建议目标目录（待权威实现路线确认后执行）

```text
design/runtime-kit-v1.31/contracts/   # 唯一 canonical artifact；schema + changelog + fixtures

src/main/java/com/dominionwars/adapter/canonical/
  SnapshotProjection.java              # Java state -> viewer-scoped GameSnapshot
  LegalActionProjection.java           # Java rules -> LegalActionSet
  ActionIngress.java                    # GameAction -> queued authoritative command
  EventProjection.java                 # structured engine event -> UIEvent
  ContractVersion.java

unity/DominionWars.Unity/Assets/DominionWars.Runtime/
  Contracts/                            # wire DTO/validator；不得引用 UnityEngine
  Transport/                            # IRuntimeSession + 具体 transport
  Adapter/                              # RuntimeAdapter、revision/event cursor、Action submit
  Bootstrap/                            # Unity 生命周期和依赖组装
  Presentation/                        # 只读 state store；供 UI 消费

unity/DominionWars.Unity/Assets/DominionWars.Tests.EditMode/
  Contract/
  Adapter/
  Fixtures/
```

若最终正式批准“C# Engine 取代 Java 成为 Unity 运行时权威”，Java canonical adapter 目录可不实现，但必须先有明确的 PL/人类决策、规则 parity Gate 和迁移记录；不能让当前 C# smoke test 默默完成这一架构决策。

现有 `src/Adapters/` 可以保留为 engine-neutral projection package，但不得同时混入 Unity 生命周期、HTTP transport、资源加载或 UI 状态。建议把当前单体 `EngineProjectionAdapter` 的 Snapshot/Action/Event 职责拆开；这是后续实现建议，不是本次改动。

## 7. P0 / P1 / P2 风险

### P0 — 未解决前禁止开始真实 Unity/UI 接入

1. **P0-01：Canonical 1.31 不存在或不可定位。** 无法锁定权威 schema、版本差异与兼容策略。
2. **P0-02：运行时权威冲突。** 项目规则要求 Java 权威，但 Unity 当前只接 C# Engine；没有正式迁移决策，也没有 Java → canonical bridge。
3. **P0-03：wire contract 与 DTO 不一致。** contractVersion、snapshotRevision、大小写、LegalAction/GameAction 关系不一致，现有 round-trip 测试不等于 schema conformance。
4. **P0-04：没有防陈旧/重复 Action 的闭环。** action 未绑定 match/revision，也未强制属于当前 LegalActionSet。
5. **P0-05：Java UIEvent 来源不可靠。** 从中文日志推导，缺稳定 ID、父链、事件时 metadata 和恢复机制。
6. **P0-06：事件时序可能被篡改。** C# Adapter 用投影时的 turn/phase 标记全部历史事件；批量 API 还会静默丢弃未知事件。
7. **P0-07：隐藏信息合同缺失。** C# Snapshot 会投影双方完整 Deck/Hand；canonical 没有 viewer/audience 与 redaction 规则。
8. **P0-08：Action/Target/Prompt taxonomy 未闭合。** Ambush、discard、choose target、punish response、leader ability、castle/life core 之间没有一套获批准的联合模型。

### P1 — Gate 前必须有明确处理或可验证隔离

1. **P1-01：schema 过于宽松。** 关键子结构为任意 object，无法拒绝缺字段、错 payload 或 renderer-only 数据。
2. **P1-02：Java command 存在 TOCTOU 与提前 ACK。** HTTP 线程检查后排队，返回成功不代表权威线程已执行。
3. **P1-03：事件轮询可丢失。** `recentEvents` 为一次 publish 的临时集合，无 cursor、ack、replay 或 gap recovery。
4. **P1-04：本地化边界泄漏。** Card/Prompt/Error 直接携带可见文本，和 localization-key 原则冲突。
5. **P1-05：Unity 序列化技术未锁定。** DTO 使用 `IReadOnlyDictionary<string, object?>`；Unity `JsonUtility` 无法直接承载，`System.Text.Json` 在实际 Unity 编译路径仍需证明。
6. **P1-06：Unity 包解析证据不完整。** manifest 有三个 file package，但 lock 没有对应条目；尚无 Runtime assembly/Bootstrap/Session。
7. **P1-07：合同与状态文档漂移。** TODO 把 Java adapter 标为 done，其他 WBS/报告又承认 action/event/Unity 缺口，后续智能体可能错误开工。
8. **P1-08：ActionResult 未立约。** accepted/rejected、reasonKey、执行后 revision、幂等结果和 fatal/retryable 分类不明确。

### P2 — 可在 P0/P1 闭环后治理

1. **P2-01：Adapter 命名和职责过宽。** 一个静态类同时处理 snapshot/card/legal action/event/validation/ID。
2. **P2-02：ID 表达不统一。** Java numeric uid、C# `entity_000...`、core `core:*`、event `evt_*` 并存，缺正式 grammar。
3. **P2-03：合同缺 golden fixtures 与 migration fixtures。** 无法跨 Java、C#、Unity 做字节级或语义级对照。
4. **P2-04：错误类型不统一。** Java 中文 err、C# reasonKey/exception、Unity 启动失败策略尚未汇合。
5. **P2-05：EventAdapter 的 ancestry compression 未立约。** 过滤内部父事件后把子事件挂到最近可展示祖先，可能有用，但需写进合同并测试。

## 8. 已确认的架构决策

以下只记录当前仓库多个权威来源一致支持的决策；不把 TODO 或 smoke 当作完成证明：

1. Java Engine 当前仍是规则行为权威/基线；renderer 不得复制规则。
2. 表现层只消费引擎给出的 Snapshot、LegalActionSet 与 Event stream，并只提交 GameAction。
3. UI 不得计算 legality、targeting、phase progression、victory 或 punish resolution。
4. canonical 合同不得包含 DOM/CSS/JavaScript callback、Unity GameObject path、Godot NodePath 等引擎标识。
5. card ID 来自 JSON；match entity/action/event 使用稳定 ID，不使用 Unity `GetInstanceID()`。
6. `eventId/parentEventId` 表达可追踪因果；根事件 parent 为 null。
7. 合同版本不兼容时 fail-closed；不能以默认值掩盖 wire 中缺失的版本。
8. JSON 卡牌/卡组数据是内容唯一来源；ScriptableObject/Unity 资源只能做派生缓存或表现映射。
9. Layout、Motion、Asset、Localization、Accessibility 是独立 adapter，不进入规则引擎。
10. 任何 Java → C# 权威迁移都必须显式批准并以 parity/迁移证据完成，不能由目录或引用关系隐式发生。

## 9. 禁止破坏的边界

- 不在 Unity/UI/Renderer 中复制或补写任何规则判断。
- 不让 UI 读取 Engine mutable state、调用 `Game` 方法或绕过 GameAction 入口。
- 不把日志文本解析成规则状态或 UIEvent。
- 不把未按 viewer 裁剪的隐藏信息送到 presentation boundary。
- 不允许 action 缺少 match/revision 绑定后仍可执行。
- 不允许未知 contract version、action type、event type 或 payload 静默降级。
- 不允许 canonical schema 依赖 Unity、HTTP、JSON 库或具体 renderer。
- 不用 Unity Instance ID、数组下标或本地化名称充当持久 entity/action/event ID。
- 不把 AssetAdapter、LocalizationAdapter、MotionAdapter 合并进规则/ActionAdapter。
- 不在没有批准的 1.31 artifact 前修改 1.30 schema 来“猜测升级”。
- 不以 `.NET tests pass`、Unity package manifest 存在或一个 smoke test 代替端到端 Adapter Gate。
- 不因本审查创建开发目标、启动 Terra、改 WBS 状态或推进 UI。

## 10. Adapter 实施顺序与逐步验收标准

以下顺序是给后续实现者的依赖顺序，不是本次开发授权。任何一步未通过，不得越级让 UI 依赖临时形状。

### Step 0 — 锁定权威与 1.31 artifact

工作：由 PL/人类确认运行时权威路线，并提供唯一 1.31 目录、版本规则、changelog、schema 与 fixtures。

验收：

- 仓库中只有一个被声明为 canonical-current 的入口；所有 schema 的 `$id`/版本可机器读取。
- 明确 `design package version` 与 `contractVersion` 的关系。
- 明确 1.30 → 1.31 是兼容新增、破坏性升级还是尚未发生。
- 下面“待确认项”均有书面答案；没有答案则保持 Gate BLOCKED。

### Step 1 — 完成 strict wire schema

工作：定义 GameSnapshot、Player/Card/Zone/Castle/Prompt、LegalActionSet、GameAction、ActionResult、UIEvent discriminated payload、ID grammar、visibility 与 revision 规则。

验收：

- valid/invalid golden JSON 均由标准 JSON Schema validator 得到预期结果。
- 三类 wire message 显式要求 `contractVersion`；Snapshot/Action/Event/ActionResult 的 revision 关联明确。
- lowerCamelCase、null/omitted、number width、enum、unknown field 策略明确。
- schema 不含 Java/C#/Unity 类型名或 renderer path。

### Step 2 — 建立 Java canonical Snapshot/LegalAction projection

工作：在 Java 对局线程生成 viewer-scoped、不可变 Snapshot 与 LegalActionSet，不复用旧 Web ViewModel 的 `usable/why` 推断。

验收：

- 同一已结算状态的重复投影语义相同；revision 只按合同规定变化。
- 两个 viewer 的 fixture 证明对手手牌/伏击/牌库顺序没有泄漏。
- 每个 LegalAction 的 actor/source/target/payload 都能追溯到 Java 权威规则路径。
- matchId、entity ID、actionId 在规定生命周期内稳定且无碰撞。

### Step 3 — 建立 Java GameAction ingress

工作：统一旧 `play/attack/answer/...` 为 canonical GameAction，在权威对局线程原子验证与执行。

验收：

- 对每种批准 action type 至少有 accepted、unknown、wrong actor、wrong match、stale revision、payload mismatch、not-advertised、duplicate case。
- HTTP/transport 收到的成功只表示权威线程完成并产出 ActionResult，不是仅排队成功。
- actionId/type/payload 必须与当前 LegalActionSet 对应项完全匹配。
- 重复提交不会重复结算；幂等或明确拒绝行为已立约并测试。

### Step 4 — 建立结构化 Java Event stream

工作：规则执行时直接 emit 结构化事件，移除日志解析作为动画依据。

验收：

- eventId 在 match 内唯一、单调；parent 必须已存在或为 null；无环。
- 每个事件携带发生时的 turn/phase/snapshotRevision，而非投影时补写。
- UI schema 每个 event type 至少一个 golden fixture；每个 Java 可展示事件均有批准映射。
- cursor 断档可被检测并通过 replay/resnapshot 恢复；轮询跳过一次不会无声丢动画语义。
- 本地化日志变更不改变 event fixture。

### Step 5 — 建立 transport-neutral session protocol

工作：定义 create/load/close、snapshot/event cursor、submit action、ActionResult、fatal/retryable error 与取消语义。

验收：

- transport 替身与真实实现通过同一 contract test suite。
- 断线、超时、重复响应、乱序响应、旧 match 响应均 fail-closed 或按合同恢复。
- transport payload 逐条通过 1.31 schema；不暴露 Java mutable object。

### Step 6 — 实现 Unity Contracts/RuntimeAdapter

工作：只做 deserialize/validate/state-store/event-cursor/main-thread/action-submit；不实现规则。

验收：

- Runtime assembly 不引用 Java/C# 规则实现类型；若正式选择 C# 权威路线，则按获批依赖图验证。
- 真实 Unity Editor 完成 UPM lock，三个所需包解析明确，Console 无编译错误。
- EditMode 通过 golden snapshot/action/event、version mismatch、schema failure、stale response、event dedupe/gap、main-thread dispatch 测试。
- 任一非法 payload 不进入 presentation store，且产生结构化 fatal/recoverable 状态。

### Step 7 — 接入最小 presentation consumer

工作：只用测试 consumer/无视觉 smoke 证明 Snapshot → state、LegalAction → submit、UIEvent → ordered delivery；不重写 UI。

验收：

- consumer 只依赖只读 presentation model 和 adapter interfaces。
- 点击/拖拽/键盘测试最终提交同一个 GameAction，不存在旁路。
- phase/target/victory 改变只来自新 Snapshot/Event，删除 consumer 推断后行为不变。

### Step 8 — Adapter Integration Gate

只有以下条件全部有当次可复现证据，才能标记 PASS：

1. Canonical 1.31 artifact、changelog、版本兼容规则与 schema hash 已锁定。
2. Java authority（或正式批准的替代 authority）到 wire 的 Snapshot/LegalAction/Action/Event 映射矩阵 100% 有测试。
3. 所有 golden wire payload 通过 schema；所有故意错误 payload 被拒绝。
4. visibility/redaction 双 viewer 测试通过，未泄漏隐藏卡、伏击或牌库顺序。
5. stale/wrong-match/duplicate/not-advertised Action 全部拒绝且无状态副作用。
6. event ID/parent/revision/turn/phase/cursor/recovery 测试通过；没有日志解析依赖。
7. Unity 实际编译通过，UPM lock 含实际本地依赖，EditMode Adapter suite 当次全绿。
8. 最小端到端 trace 在固定 seed 下完成：create match → snapshot → choose advertised action → authoritative ActionResult → updated snapshot → ordered events；Java 与 wire 状态断言一致。
9. 代码扫描确认 Unity presentation/runtime adapter 中没有 legality、targeting、phase progression、victory、punish 等规则实现。
10. Gate 报告记录精确 commit、命令、测试总数、失败/跳过数与未验证层；任何 BLOCKED/SKIPPED 的必需项都使总 Gate 为 BLOCKED，而非 PASS。

Windows `.exe`、视觉验收和完整 UI 不属于 Adapter Gate 本身，不应阻塞纯 Adapter PASS；但也不能用 Adapter PASS 宣称游戏或 UI 已完成。

## 11. 待确认项（不得自行虚构）

1. Canonical Runtime Contract 1.31 的实际文件、负责人、变更清单和批准记录在哪里？
2. Unity 最终运行时是调用 Java Engine，还是由 C# Engine 正式取代 Java？若取代，谁批准、parity 范围和迁移 Gate 是什么？
3. 1.30/1.31 与整数 `contractVersion = 1` 的关系是什么？
4. canonical wire serializer、lowerCamelCase、null/omitted、unknown-field 和 numeric width 策略是什么？
5. LegalAction 与 GameAction 是同一 schema 的两个方向，还是两个不同类型？
6. `snapshotRevision` 是否强制；action 的 stale、duplicate 与 idempotency 语义是什么？
7. 正式 action 集是否包含 `CHOOSE_TARGET`、`ACTIVATE_PUNISH`、`USE_LEADER_ABILITY`；prompt answer 是否统一为 GameAction？
8. entity/leader/shared castle/player life/prompt option 的 target union 与稳定 ID grammar 是什么？
9. viewer/audience、隐藏手牌、伏击、牌库顺序和 spectator 的 redaction 规则是什么？
10. UIEvent 完整枚举、每类 payload、内部事件过滤、ancestry compression 与 ActionResult/拒绝事件的分工是什么？
11. Castle 在首个 Unity MVP 是否启用；若禁用，Snapshot/Event/布局如何表达 feature disabled？
12. Java 作为 Unity runtime 时的打包/进程/IPC 方案与 JRE 分发边界是什么？

## 12. 给后续 Terra 开发智能体的完整交接指令

> 你接手的是 **Adapter Integration**，不是 Renderer/UI 重写，也不是规则设计。开始前完整阅读本文件、根 `AGENTS.md`、获批准的 Canonical Runtime Contract 1.31、`docs/RULES.md`、`docs/DESIGN.md` 与 Java 权威实现。不要依据文件名或旧 TODO 假定 Adapter 已完成。

1. 首先验证 Step 0。若仓库仍没有获批准的 1.31 artifact，或 Java/C# 运行时权威仍冲突，立即将任务标为 `HUMAN_REQUIRED/BLOCKED`；只报告证据，不创建猜测合同，不修改 1.30 schema。
2. 工作顺序必须严格按 Step 1 → Step 8。每一步只提交该步最小文件；不得越过未通过 Gate 的前置项。
3. Java 规则是当前权威。Adapter 只能投影已结算状态、发布 Java 生成的合法动作、接收并回送动作、投影结构化事件；不得在 C#、Unity 或 transport 中重写规则。
4. 若收到正式批准将 C# Engine 升为运行时权威，先保存批准记录与迁移范围，再建立 Java parity tests；在 parity Gate 通过前不得删除、旁路或宣称替代 Java。
5. 不复用 `GameSession.publish()` 的旧 Web JSON 作为 canonical 成品；它只能作为迁移输入证据。禁止继续用 `collectEvents()` 的中文日志解析产生 UIEvent。
6. Snapshot 必须 viewer-scoped；任何对手隐藏卡牌身份、伏击身份或牌库顺序到达 Unity 都是 Gate 失败。
7. 所有 Unity 用户输入必须先解析为当前 LegalActionSet 中的一个动作，再提交包含 match/revision 的 GameAction；不允许 UI 直接调用规则方法或构造未发布动作。
8. Event 必须由权威执行路径结构化生成，并保留事件发生时 metadata、稳定 eventId/parentEventId 与可恢复 cursor。未知类型按获批策略 fail-closed，禁止静默丢弃。
9. Unity RuntimeAdapter 只负责 contracts、transport、validation、state store、event cursor、main-thread dispatch 与 action submit。Asset、Localization、Layout、Motion、Accessibility 使用独立接口；不要把它们塞进规则 adapter。
10. 保留仓库中所有无关 dirty 文件；禁止 reset/clean、历史重写、push、merge、release。不要改 Renderer/UI、规则、平衡、Schema 或资源，除非后续用户给出新的明确授权和已批准合同范围。
11. 每一步报告：目标、实际文件、commit、命令、通过/总数/跳过数、golden fixture、未验证层与回滚方式。没有实际 Unity 运行证据就写 BLOCKED，不得写 PASS。
12. 最终只在 Step 8 十项全部满足时声明 `ADAPTER_INTEGRATION_GATE=PASS`。否则声明 `BLOCKED` 或 `FAIL`，列出最小未决项，并停止；不要自行进入 UI、Windows build 或下一阶段开发。

---

本文件完成后，本次架构审查即结束；没有授权任何后续实现。
