# 提案：胜利目标契约（Victory Objective Contract）

日期：2026-09-12 · 提出人：PL（DeepSeek V4 Flash）
状态：**owner 已批准；已按 owner 澄清的方向重做并实现** · 分支：`pl/ai-threat-estimator`

---

## 0. 实施结果（2026-09-12，先读这段）

### 0.0 我第一版做错了方向，owner 纠正后重做

**我第一版把目标理解成"少改代码"**：把度量做成闭集枚举 + 引擎 switch，
然后因为"王城破除不是阈值"就**拒绝表达它**，还写了测试钉住烈焰统领必须没有目标。

**Owner 纠正：目标是统一接口。**
加一种新胜利方式 → **只写一个新的"怎么算进度"的类**；
AI 那边"该出什么牌"的算法 → **一行都不用改**。
烈焰统领因此正是问题本身：它也该实现同一个接口、算自己的进度。
多个条件组合也是同一道理 —— 写个组合类即可。

**重做后的设计**（这才是对的形状）：

```
IVictoryCondition            ← 唯一扩展点
  Id                         "这个条件叫什么"
  DeclaresWinWhenMet         "满足它是否直接宣告胜利"
  Read(state, seat) → VictoryReading

VictoryReading               ← 下游只看这个
  IsMeasurable / Current / Target / IsMet / Remaining / UnmeasurableReason
```

**加一种新胜利方式 = 写一个实现 `IVictoryCondition` 的类 + 在注册表加一行 + 卡牌数据写 id。**
引擎判定器、快照、AI、界面**全都不改** —— 因为它们只读 `VictoryReading`，
**没有任何一处知道那个 id 是什么意思**。

### 0.1 已落地

| 层 | 改动 |
|---|---|
| 引擎模型 | 新增 `VictoryCondition.cs`：`IVictoryCondition`、`VictoryReading`、`ThresholdVictoryCondition` 基类、`AllOfVictoryCondition` / `AnyOfVictoryCondition` |
| 具体条件 | 每个条件一个**小类**：`OpponentDiscardCountCondition`、`NoDamageTurnStreakCondition`、`OpponentPunishDrawThisTurnCondition`、`PullCountCondition`、`SealedMinionMaxHealthCondition`、`CastleBreakCondition` |
| 注册表 | `VictoryConditions.Create(id, target)` —— **新条件的唯一登记处** |
| 引擎判定 | 旧 switch **已删除**。现在只有一条路径：向条件要读数，比对着读数的 `IsMet`。未迁移的卡走保留的回落分支 |
| 加载校验 | `CardCatalog` 在加载期**真的构造一次条件**；id 没有对应类就直接报错 |
| 数据 schema | `victory.metric` 由"闭集枚举"改为"指向条件类的 id"，并说明如何新增 |
| 卡牌数据 | **5 张统领全部迁移**，含此前被拒绝的 `flame_leader`（`CASTLE_BREAK`） |
| 契约快照 | 发布 `Metric / Direction / Target / Current / Remaining / Met / UnmeasurableReason` |
| AI | `VictoryObjectives.ForMatch` 逐玩家优先用发布的读数；`WinConditionCounters` 降为回落专用 |

### 0.2 验证

- `VictoryObjectiveMatchesTheLegacySwitchOnRealMatches`：
  **24 个 (统领,座位) 组合，537 条 VICTORY_PROGRESS 读数**，
  **5 张统领（含烈焰）**，新老两条路径逐条比对；**胜负与回合数必须完全一致**。
- `EveryRegisteredConditionProducesAReading`：注册表里**每一个**条件都要在真实局面上给出可用读数，
  且 `IsMet` 与 `Remaining` 必须自洽。注册了却没人验过的条件是隐患。
- `TheCastleBreakConditionReadsTheCastle`：王城未破 / 已破两种状态都验，**不涉及任何计数器**。
- `CompoundConditionsComposeThroughTheSameInterface`：`all` / `any` 各自语义正确；
  且**任一子条件不可测时整体不可测**（不许在猜测之上报进度）。
- `TheDeclaredObjectiveReachesTheAiThroughAViewerSafeSnapshot`：数据 → 引擎 → 快照（双方视角）→ AI，
  AI 的 `Current` / `Met` / `Remaining` 必须等于条件自己给出的值。

**门禁：835/835 通过，0 失败，断言 140544。**

### 0.3 一个我必须上报的规则层判断（请 owner 复核）

我加了一个接口成员 `DeclaresWinWhenMet`，理由是**"进度多少"和"谁赢了"是两个问题**：

- 普通阈值条件：满足即宣告胜利 → `true`
- **`CastleBreakCondition`：`false`** —— 它的胜利由**原本那条同时性判定路径**决定
  （比较双方统领的资格，一次定胜负）。

**如果我不加这个区分**：王城被破时，如果原路径没有决出胜者（例如 `leader_gate` 拦下），
那么新条件会在阈值循环里让**座位 0 先被求值而获胜** ——
而迁移前烈焰统领**根本不进这个循环**（它没有 `winParam`），所以那是**新增了一条规则**，不是迁移。

**所以：`CASTLE_BREAK` 会汇报进度，但不通过这条路径宣告胜利。**
它现在只是把原本"只有引擎内部知道"的进度**翻译成统一读数**给下游，
这正是 owner 要的"翻译成一个进度让下一个地方去调用"。

**这个判断属于规则层，我照实报上来。** 如果 owner 认为王城破除**也应该**由这个接口定胜负，
那需要改的是"谁先被求值/如何比较双方"，而不是我这里偷偷决定。

### 0.4 关于 `AMBUSH_TRIGGER_WIN`（按 owner 指示未动）

查清了：`gate_of_fate`（命运之门）**根本不靠胜利条件获胜** ——
它的 `ambushEffects` 里直接是 `WIN_GAME`。所以它声明的 `AMBUSH_TRIGGER_WIN`
**是一段不会被求值的惰性元数据**，不是"引擎缺一个分支"。

按 owner 说明（留给未来的伏击统领），**本轮不改**，仅记录。
它也没有对应的条件类，所以如果哪天有卡把它写进 `victory`，**加载期就会直接报错**。

### 0.5 仍未完成 / 边界（不掩盖）

- **新增一个条件仍需在注册表加一行。** 要连这一行都免掉，得让数据能描述"怎么数"
  （读数表达式）。**现在不做**：卡池里还没有第二个非平凡度量，没有样本可依。
- `OutcomePredictor` 对进度值的读取路径我**没有逐条复核**；它走 `StateEvaluator` → `ForMatch`，
  所以拿到的是发布读数，但它自身还有没有别处按名字解析，未逐一确认。

---

## 1. 为什么需要这个（问题陈述）

---

## 1. 为什么需要这个（问题陈述）

owner 指出的要求是：

> 新胜利条件应该**由规则本身描述给 AI**。AI 读的是统一结构，不需要知道那 14 代表
> 弃牌、下载、占格还是养树。

**现状不满足这条，而且根在引擎，不在 AI。**

### 1.1 引擎自己也是写死的

`src/Engine/Effects/EffectRuntime.EndPhase.cs:156-183`：

```csharp
switch (leader.Definition.LeaderWinCondition)
{
    case "OPP_DISCARD_TOTAL_GE":  current = opponent.TotalDiscarded; break;
    case "NO_DAMAGE_TURNS_GE":    current = player.NoDamageTurns;    break;
    case "OPP_PUNISH_DRAW_TURN_GE": current = opponent.PunishDrawnThisTurn; break;
    case "PULL_TOTAL_GE":         current = player.PullCount;        break;
    case "GIANT_HEALTH_GE":       /* 找封印随从最大生命 */ break;
    default: continue;
}
```

外加 `EffectRuntime.State.cs:236-243` 单独处理 `ROYAL_CASTLE_BREAK`。

也就是说：**"这个条件的进度从哪读"这条知识，只存在于引擎的 C# 代码里**，
数据层（`data/cards/*.json` 的 `leaderDef`）只有 `winCondition` 名字和 `winParam` 阈值。

### 1.2 后果

- 任何消费者（AI、UI、回放、平衡工具）都必须**再写一份同样的 switch**，否则就看不见进度。
  我这一轮在适配层就写了第六份（`WinConditionCounters.cs`），**已经自己声明那是兼容层**。
- **新系列加一个条件 → 所有消费者都要改代码**，而不是改数据。
- 名字拼写成了接口：`winCondition: "PULL_TOTAL_GE"` 里 `_TOTAL_GE` 是**比较后缀**，
  `PULL` 才是度量。消费者必须"猜"怎么切分（我这轮就猜错过两次：`TURNS_GE` 复数、
  `ROYAL_CASTLE_BREAK` 没有后缀）。

---

## 2. 提案：让 `leaderDef` 自己描述目标

### 2.1 最小结构

在 `data/cards/*.json` 的 `leaderDef` 增加一个可选字段：

```json
"leaderDef": {
  "winCondition": "PULL_TOTAL_GE",
  "winParam": 6,
  "victory": {
    "metric": "PULL_COUNT",
    "direction": "INCREASE",
    "target": 6
  }
}
```

引擎在判定时读 `victory.metric`，**不再读 `winCondition` 字符串**。
`winCondition` 可保留为展示/兼容字段，或逐步废弃。

### 2.2 AI 看到的统一结构

每个玩家一份，语言无关的语义：

```
VictoryObjective {
  metric      : "PULL_COUNT"      // 一个闭集枚举，不是自由字符串
  direction   : INCREASE | DECREASE | MAINTAIN
  target      : 6
  progress    : 4                 // 引擎已算好的当前值
  completed   : false
}
```

**AI 从此不认识任何具体条件名。** 它只做：

```
remaining = abs(target - progress)
```

——"还差多少"。至于 4 是弃牌还是下载，**与 AI 无关**，这正是 owner 要的。

### 2.3 复合目标（例如"先决条件 A 且 B"）

不建议第一版就做。建议先定义**可扩展形状**留位：

```json
"victory": {
  "all": [ { "metric": "...", "target": 6 } ],
  "any": []
}
```

单目标是 `all` 长度为 1 的特例。这样以后加复合条件**不改形状**，只改内容。

---

## 3. 需要的 `metric` 闭集（按现有卡池）

从引擎现有 switch 反推，共 6 个度量：

| metric | 现条件名 | 引擎读取来源 | 本分支是否已公开给 AI |
|---|---|---|---|
| `OPPONENT_DISCARD_COUNT` | `OPP_DISCARD_TOTAL_GE` | 对手 `TotalDiscarded` | ✅ 已补 |
| `NO_DAMAGE_TURN_STREAK` | `NO_DAMAGE_TURNS_GE` | 自己 `NoDamageTurns` | ✅ 已补 |
| `OPPONENT_PUNISH_DRAW_THIS_TURN` | `OPP_PUNISH_DRAW_TURN_GE` | 对手 `PunishDrawnThisTurn` | ✅ 已补 |
| `PULL_COUNT` | `PULL_TOTAL_GE` | 自己 `PullCount` | ✅ 已有 |
| `SEALED_MINION_MAX_HEALTH` | `GIANT_HEALTH_GE` | 自己场上封印随从最大生命 | ✅ 已有 |
| `CASTLE_BREAK` | `ROYAL_CASTLE_BREAK` | 共享王城血量 | ✅ 已有 |
| `AMBUSH_TRIGGER_COUNT` | `AMBUSH_TRIGGER_WIN` | 自己 `AmbushCount`（身份仍隐藏） | ✅ 已有 |

**注意**：`AMBUSH_TRIGGER_WIN` 目前在引擎 switch 里**没有 case**（走到 `default: continue`），
所以它在引擎里其实是**不可达的胜利条件**。这一点我在本分支无法修（属引擎），
**仅作记录**：如果它有卡在用（`gate_of_fate`），那是一个真实的引擎缺口。

---

## 4. 兼容与迁移

1. `victory` 为**可选**：缺省时引擎回落到现有 `winCondition` switch，**行为完全不变**。
2. 逐卡迁移：先给一张卡加 `victory`，跑全量门禁比对判定结果一致，再推广。
3. schema：`data/schema/cards.schema.json` 的 `LeaderDef` 增加 `victory` 定义（可选）。
4. 适配层：`WinConditionCounters` 从"兼容层"降级为"回落路径"，
   有 `victory` 时直接读 `progress`，**不再猜名字**。

---

## 5. 影响面

| 文件 | 改动 |
|---|---|
| `data/schema/cards.schema.json` | 加 `VictoryObjective` 定义（可选） |
| `src/Data/CardCatalog.cs` | 读 `victory`，校验 `metric` 在闭集内 |
| `src/Engine/Model/CardDefinition.cs` | 增加 victory 字段 |
| `src/Engine/Effects/EffectRuntime.EndPhase.cs` | 优先读 `victory.metric`，回落旧 switch |
| `src/Adapters/RuntimeContractV131*` | 快照增加 `victory`（metric/direction/target/progress/completed） |
| `src/Adapters/Ai/WinConditionCounters.cs` | 变为回落路径 |
| `data/cards/*.json` | 逐卡加 `victory`（可分批） |

**风险**：这是**规则层改动**（改变"引擎如何判定胜利"的代码路径），
即使加 `victory` 且数值等价，也**必须逐卡验证判定结果不变**。
建议先只改 1–2 张卡 + 全量门禁 + 一局真实对局对照。

---

## 6. 我不做的事（边界）

- **我没有改引擎**（本提案只是文档）。
- 我**没有**把 `WinConditionCounters` 说成最终架构——它在代码里已明确标注为**兼容层**。
- 我**没有**改 `data/` 任何卡牌数据。

---

## 7. 需要 owner 决定

1. **是否批准这个方向**（让 `leaderDef` 描述目标，AI 读统一结构）？
2. **是否批准动 `data/schema` + 引擎判定路径**？这属于规则层，需你明确批准。
3. 若批准，**建议分批**：先 1 张卡 + 门禁对照，再推广。
4. `AMBUSH_TRIGGER_WIN` 在引擎里**没有 case**，是否要一并修？
