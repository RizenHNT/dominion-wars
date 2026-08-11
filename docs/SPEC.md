# SPEC.md — Dominion Wars Unity MVP 实施规格

> **目的**：把 [`docs/PROPOSAL_FORGE_M1.md`](PROPOSAL_FORGE_M1.md) 的"做什么"翻译成"怎么做"——落到仓库布局、API 表面、测试钩子和构建步骤。
> **Owner**：MiniMax（PL 起草）/ Codex 实现 / DeepSeek 校验。
> **状态**：🟡 v0.1 骨架（2026-08-11 起草）；2026-08-11 18:30 同步 PROPOSAL §5.9 新增硬约束 #9（模块拆分 + 单文件 ≤500 行）。等 Codex + DeepSeek 反馈后细化。
> **冲突解决优先级**（从高到低）：`docs/RULES.md` > 本 SPEC > `docs/PROPOSAL_FORGE_M1.md` > UI/视觉稿。任何冲突都要回到 PL 协商。

---

## 1. 概览

第一切片目标是 **1 卡 + 1 统领 + 1 惩罚卡，端到端可视化**。

- 引擎核心是一份独立的 .NET 8 / netstandard2.1 程序集（`src/Engine/`），**完全不引用 `UnityEngine.dll`**——可以脱离 Unity 编辑器运行。
- 适配器层（`src/Adapters/`）负责把引擎事件翻译成 `design/runtime-kit-v1.30/contracts/` 定义的三种结构：Snapshot / Action / Event。
- 测试覆盖"5 路径 + 11 验收"，跑两档：纯 .NET（EditTime）和 Unity EditMode。
- 数据只从 `data/cards/*.json` 读取；契约版本号不匹配直接启动失败，不放行。

---

## 2. 范围与非目标

**范围（in）**

- MVP 一回合（START → AMBUSH → ACTION → DISCARD → END）
- 1 卡 + 1 统领 + 1 惩罚卡的最小对局可视化
- 21 个动作（IEffect）通过 `EffectDispatcher` 单一入口派发
- 21 动作之外的实现可以留 stub（占位实现 + 注释 + 单测覆盖），但不能"未实现 + 通过"
- 引擎可纯 .NET 跑测试；Unity EditMode 跑同一组用例做对照

**非目标（out）**

- 完整 91 卡 / 5 阵营 / Castle 完整平衡
- 联机 / 服务端 / 排行榜
- 3D 渲染 / 完整音频 / 完整无障碍
- Mac / Linux 平台
- 数据迁移工具（暂时手工）

---

## 3. 仓库布局（首切片落地后的目标结构）

```
dominion-wars/
├── src/
│   ├── Engine/                          # 引擎核心（纯 .NET，无 UnityEngine 引用）
│   │   ├── Effects/                     # IEffect + EffectDispatcher + 21 动作
│   │   ├── Model/                       # Game, Player, Card, Phase, IdAllocator
│   │   ├── Command/                     # ICommandBuffer + 本地实现
│   │   ├── Id/                          # IRandomSource / SnapshotRevision / EventLog
│   │   ├── Localization/                # Localization.Get("key")
│   │   └── Tests/                       # 引擎单元测试（xUnit 或 NUnit 选一）
│   ├── Adapters/                        # 适配器层（引 UnityEngine 也引 Engine）
│   │   ├── SnapshotAdapter.cs           # 引擎 Snapshot → runtime kit GameSnapshot
│   │   ├── ActionAdapter.cs             # IGameAction → 引擎可读 action
│   │   └── EventAdapter.cs              # 引擎事件 → IGameEvent
│   ├── Tests.EditMode/                  # Unity Test Framework EditMode
│   └── Tests.PlayMode/                  # 后期可加，MVP 不需要
├── design/runtime-kit-v1.30/            # 设计包已平移；详见 §10
├── data/
│   ├── cards/*.json                     # 91 张卡（**唯一数据源**）
│   ├── decks/*.json                     # 4 卡组 JSON
│   └── schema/
│       ├── cards.schema.json            # 91 卡反推
│       ├── game_snapshot.schema.json    # 补齐 §10.2 子结构
│       └── ui_event.schema.json         # 补齐 §10.3 类型分支
├── scripts/
│   ├── schema/                          # Schema 推导 + 校验脚本
│   ├── build/                           # 纯 .NET 构建脚本
│   └── nightshift/                      # 已有；不动
└── docs/
    ├── RULES.md                         # 玩家规则权威
    ├── DESIGN.md                        # 架构权威（实现后回填）
    ├── PROPOSAL_FORGE_M1.md             # 提案
    ├── SPEC.md                          # 本文档
    └── PL_REPORT_*.md / NIGHT_REPORT.md # 报告
```

---

## 4. 抗负债硬约束（PROPOSAL §5 展开）

> 9 条硬约束从 PROPOSAL §5 镜像；本节仅记录与 Codex 落地的映射。

每条都列**怎么实现 + 怎么验证**。

### 4.1 引擎 DLL 独立于 UnityEngine

- **实现**：`src/Engine/DominionWars.Engine.csproj` 设 `<TargetFramework>netstandard2.1</TargetFramework>`；不写 `<Reference Include="UnityEngine" />`。
- **验证**：`dotnet build src/Engine` 在没有 Unity 安装的环境跑通。Codex 加 CI 校验脚本扫"`UnityEngine`"引用 = 0。

### 4.2 JSON 是唯一数据源

- **实现**：卡牌/卡组/平衡数值只在 `data/` 下；代码用 `dataLoader.LoadCards()` 读，禁止硬编码 ID / 数值。
- **验证**：Codex 加 grep 脚本 `scripts/no_hardcoded_ids.py`，命中 = 编译错。

### 4.3 IEffect + EffectDispatcher

- **实现**：每个 21 动作 = 一个 `class XxxEffect : IEffect`；`EffectDispatcher` 用 lookup table `<string actionName, IEffect>` 派发。
- **验证**：新增动作如果绕过 dispatcher（直接 `new XxxEffect()`） = 测试失败。

### 4.4 CommandBuffer 接口

- **实现**：所有 `GameState.Mutate(...)` 都走 `ICommandBuffer.Apply(GameState)`；MVP 内本地实现 `LocalCommandBuffer`。
- **验证**：grep `GameState\.Mutate` 命中位置必须都在 `LocalCommandBuffer` 内。

### 4.5 IRandomSource

- **实现**：`SeededRandomSource : IRandomSource`（基于 xoshiro256\*\*）；MVP 内提供；禁 `UnityEngine.Random`。
- **验证**：测试 fixture 用固定种子，回归脚本重跑结果字节级一致。

### 4.6 Localization.Get only

- **实现**：所有 player-facing 字符串走 `Localization.Get("card.foo.name")`；缺失 key → 启动拒绝（fail-closed）。
- **验证**：grep 玩家可见字符串字面量 = 0；Codex 写测试故意少一个 key 看能否启动失败。

### 4.7 contractVersion 校验（fail-closed）

- **实现**：Snapshot / Action / Event 三类结构各带 `contractVersion: 1`；启动时读 + 比对，缺失或不一致 → `StartupReject("contract version mismatch: expected 1, got " + v)`。
- **验证**：见 §10 #9 #10。

### 4.8 稳定字符串 ID（严禁 Unity Instance ID）

- **实现**：卡 / 事件 / 动作 ID 都是 JSON 稳定字符串或引擎 64-bit counter；`actionId.snapshotRevision` 必填。
- **验证**：grep `GetInstanceID()` 在跨局数据路径 = 0 命中。

---

## 5. ID 生命周期（PROPOSAL §6 展开）

| ID 类型 | 来源 | 生命周期 | 跨局 |
|---|---|---|---|
| 卡牌 ID | `data/cards/*.json` 的 `id` 字段 | 字符串常量 | 跨局复用 |
| 卡组 ID | `data/decks/*.json` | 字符串常量 | 跨局复用 |
| 对局实体 ID | `Engine.IdAllocator.Allocate()` | 单局 64-bit 递增 | 仅当局 |
| `eventId` | `Engine.EventLog.Append(...)` | 单局单调递增 | **跨 Snapshot 持久** |
| `parentEventId` | 引擎派发时填 | 直接因果父事件 | 同 `eventId` |
| `actionId` | Adapter 输入时分配 | 仅对应 Snapshot 有效 | 仅该 Snapshot |
| `snapshotRevision` | 引擎 emit 后 `++` | 单调递增 | 跨 Snapshot |
| `contractVersion` | 编译期常量 `1` | 全局不变 | 不变 |

### 5.1 关键规则

- **根事件**：根事件 `parentEventId = null`。
- **跨 Snapshot 持久**：`eventId` 在重新 emit 同一事件时不变（用于回放/调试）。
- **过期 action 拒绝**：`action.snapshotRevision < engine.CurrentRevision()` 时拒收。

---

## 6. MVP 7 验收实施细节（PROPOSAL §7）

### 6.1 5 阶段循环

- 引擎 `Phase = { START, AMBUSH, ACTION, DISCARD, END }`
- 阶段切换 emit `phase_change { from, to }` 事件

### 6.2 惩罚链 20 层

- 事件：`punish_change { target, source_card, amount, chain_depth }`
- 达到 20 后 `add_punish` no-op，可选 emit `punish_capped { target, final_chain: 20 }`

### 6.3 空发裁决（5 子条款必须全部 emit）

- 计为使用：`used_counted { source_card }`
- 消耗词条：`keyword_consumed { source_card, keyword }`
- 效果不结算：`not_resolved { source_card }`
- 对方不抽牌：`opponent_not_draw { opponent }`
- 主动回合进弃牌结束：`active_turn_discard_end { active_player }`

### 6.4 先驱威压

- 事件：`pioneer_pressure { source: "leader_in_play", opponent_punish_delta: 1, leader_discard_limit_bonus: 2 }`

### 6.5 事件因果链

- 每个事件 emit 持久化到 `Engine.EventLog`；`Snapshot` 可携带最近 N 条事件给 UI
- 测试要验证：可达根 / 单调递增 / 跨 Snapshot 持久

### 6.6 5 路径（详见 §7）

### 6.7 端到端可视化

- 最小 demo：1 卡 + 1 统领 + 1 惩罚卡；1 玩家 vs 1 AI；能跑完一整回合
- 视觉主轴（`docs/PROPOSAL_FORGE_M1.md` §3）：配色 `#F3EDE0 / #11110F / #BC2C22`，印章 / 戳记 / 卷宗翻页

---

## 7. 5 核心路径测试骨架（PROPOSAL §8）

每条 1+ 单元测试。

### 7.1 5 阶段循环

```
[GIVEN] 新对局，phase = START
[WHEN]  推进
[THEN]  phase ∈ {AMBUSH, ACTION, DISCARD, END} 顺序；
       每步 emit phase_change 事件含 phase 字段
```

### 7.2 惩罚链

```
[GIVEN] player_x 累计 19 点惩罚
[WHEN]  apply +2 punish（来源 foo）
[THEN]  chain_depth = 19；引擎不响应后续 add_punish；
       最近的 punish_change 事件 source_card = foo
```

### 7.3 空发裁决

```
[GIVEN] player_x 在 ACTION，punish=30，对手手牌=10
[WHEN]  player_x 出卡 foo（惩罚卡）
[THEN]  emit 5 子事件全列；phase 进 DISCARD → END
```

### 7.4 先驱威压

```
[GIVEN] player_x 的统领在战场；对手手牌上限=7
[WHEN]  轮到 player_x
[THEN]  emit pioneer_pressure { opponent_punish_delta: 1,
                                leader_discard_limit_bonus: 2 }
```

### 7.5 事件因果

```
[GIVEN] engine.dispatch(action_a)
[WHEN]  action_a 触发 chain events
[THEN]  event_a.eventId 是某值；后续事件 parentEventId = event_a.eventId；
       可达 root (parentEventId=null)；
       eventId 在该局内单调递增
```

---

## 8. 首切片 11 验收（PROPOSAL §9 → 测试位置）

| # | 验收项 | 落地位置 |
|---|---|---|
| 1 | 纯 .NET 测试 | `src/Engine/Tests/`，CI `dotnet test` |
| 2 | Unity EditMode 测试 | `src/Tests.EditMode/`，Unity CLI runner |
| 3 | 固定种子可复现 | 测试 fixture 用 `SeededRandomSource(42)`；同种子 = 字节一致 |
| 4 | Snapshot → Action → Command → Event 因果链 | Integration test |
| 5 | 惩罚 / 空发 / 威压单元测试 | `src/Engine/Tests/Effects/` |
| 6 | ID 校验（含根 `parentEventId=null`） | `IdValidator` 测试 |
| 7 | 过期 action / `snapshotRevision` 不匹配拒绝 | Adapter 输入路径测试 |
| 8 | 根标记 + eventId 单调可校验 | `EventLogValidator` 测试 |
| 9 | `contractVersion` 缺失/不兼容 → 启动拒绝 | `EngineStartup` 测试 |
| 10 | JSON 数据 + 契约版本校验拒绝 | 数据加载路径测试 |
| 11 | 与 Java 对照（路径 1-4） | `scripts/java_compare/run_dual.py`——同 JSON 同 seed 跑 Java + C#，比对最终 snapshot |

---

## 9. 数据契约（PROPOSAL §10 展开）

### 9.1 `cards.schema.json`

- 逆向推导自 `data/cards/*.json`
- 关键字段：`id, name, type, cost, text, attack?, health?, effects?, leader?, deck?`
- Code：DeepSeek 写 `scripts/schema/gen_cards_schema.py`；Codex 不参与生成（只接产物校验）

### 9.2 `game_snapshot.schema.json`（补子结构）

```jsonc
{
  "snapshotRevision": "int",
  "phase": "START|AMBUSH|ACTION|DISCARD|END",
  "players": [{
    "id": "string",
    "hand":        [{ "card_id": "string", "instance_id": "int" }],
    "deck":        [{ "card_id": "string", "instance_id": "int" }],
    "discard":     [{ "card_id": "string", "instance_id": "int" }],
    "battlefield": [{ "card_id": "string", "instance_id": "int" }],
    "leader_instance_id": "int",
    "punish": {
      "value": "int",
      "chain": [{
        "source_card": "string",
        "amount": "int",
        "depth": "int"
      }],
      "capped": "bool"
    }
  }],
  "castle":    { "phase_progress": "int", "threshold": "int" },
  "legalActions": [{
    "action_id": "string",
    "type": "string",
    "params": "object",
    "snapshotRevision": "int"
  }],
  "contractVersion": 1
}
```

### 9.3 `ui_event.schema.json`（discriminated union by `type`）

```jsonc
{
  "eventId": "int64",
  "parentEventId": "int64|null",
  "type": "phase_change|punish_change|punish_capped|used_counted|keyword_consumed|not_resolved|opponent_not_draw|active_turn_discard_end|pioneer_pressure|...",
  "snapshotRevision": "int",
  "contractVersion": 1,
  "data": { /* 按 type 分支子结构；与 §6/§7 对应 */ }
}
```

完整事件类型清单 ≥ 21；具体哪些由 Codex 实现最终定。

---

## 10. 适配器 API（runtime-kit-v1.30 → C# 草案）

```csharp
namespace DominionWars.Engine;

public interface IRandomSource {
    int Next();                        // [0, int.MaxValue)
    int NextRange(int minInclusive, int maxExclusive);
}

public interface IEffect {
    string ActionName { get; }         // 稳定字符串
    void Execute(GameState s);
}

public interface ICommandBuffer {
    void Apply(GameState s, ICommand c);
}

public interface IGameAction {         // 输入（来自 UI）
    string  ActionId     { get; }
    int     SnapshotRevision { get; } // 必填
    string  Type         { get; }     // 稳定字符串
    JsonElement Params   { get; }
}

public interface IGameEvent {          // 输出（到 UI）
    long    EventId        { get; }
    long?   ParentEventId  { get; }
    string  Type           { get; }
    int     SnapshotRevision { get; }
    int     ContractVersion { get; }  // 当前 1
    JsonElement Data       { get; }   // discriminated union by type
}

public sealed class EngineStartup {
    public Game Load(string dataRoot, int contractVersionExpected);
    // 失败 throw StartupReject("schema mismatch: ...") 或 ("contract version ...")
}
```

`design/runtime-kit-v1.30/contracts/game_action.schema.json` / `ui_event.schema.json` 是权威；C# interface 字段对齐上面清单。

---

## 11. 构建与运行

### 11.1 纯 .NET 跑引擎测试

```
dotnet test src/Engine/Tests/DominionWars.Engine.Tests.csproj
```

### 11.2 Unity EditMode 跑测试

```
"Unity" -batchmode -projectPath . -runTests -testPlatform EditMode
```

### 11.3 启动检查（fail-closed 顺序）

1. 读 `data/cards/*.json`
2. 用 `cards.schema.json` 校验 → 不通过 `StartupReject`
3. `contractVersion` 校验 → 不通过 `StartupReject`
4. 加载对局循环

### 11.4 与 Java 对照

```
python scripts/java_compare/run_dual.py \
  --java-cmd "java -cp build/classes com.dominionwars.test.TestMain" \
  --cs-cmd   "dotnet run --project src/Engine" \
  --seed 42 --scenarios path1.json path2.json ...
```

脚本输出两张 snapshot diff；路径 5 因 Java 无 eventId，跳过。

---

## 12. 迁移与回滚

### 12.1 MVP 阶段

- Java 仓库 = **行为基准 + 黑盒对照**（PROPOSAL §1）
- 每条引擎 5 路径测试都先跑 Java 跑一遍做基线，C# 跑一遍比对

### 12.2 MVP 完成 / 稳态

- Java 仓库降级为 `archive/java-baseline/`（只读）
- 保留 `TestMain.java` + `scripts/java_compare/` 供后期黑盒对照

### 12.3 回滚

- MVP 没正式上线；回退决策由人类负责人拍
- 短期回滚成本低：清退工作只占工程量的约 1/4（设计包 + 数据 + 报告可保留）

---

## 13. 风险与未决

| 等级 | 风险 | 缓解 |
|---|---|---|
| 高 | 引擎切换成本 > 继续 Java 接入 | Java 跑同 seed 做黑盒对照（§11.4） |
| 中 | 视觉系统完整平移 | `theme.json` + `motion_primitives.json` 全量平移（Codex 任务） |
| 中 | 91 卡 Schema 反向推导 | SPEC 阶段必做；DeepSeek 协助 |
| 中 | C# vs Java 行为差异 | 路径 1-4 强制对照；路径 5 C# 自检 |
| 中 | `eventId` 体系是新加的设计而非迁来 | 用户已批；C# 实现从 day 1 |
| 低 | Unity 6 LTS / .NET 8 netstandard2.1 兼容 | Codex 验证 |

未决（等 Codex/DeepSeek 反馈）：

- `IRandomSource` 默认实现选 `xoshiro256**` 还是 `SplitMix64`？待 Codex 反馈
- `xUnit` vs `NUnit` 选哪个测试框架？待 Codex + DeepSeek 协商
- C# 9 / .NET 8 的 nullable reference types 是否启用？建议启用，与 §4.6 fail-closed 对齐
- Unity EditMode 测试是否进 CI；抑或只在本地跑？待人类拍板（影响 §8 #2 的可执行性）

---

## 14. 工作分配

### Codex

- §3 仓库布局实现
- §4 八硬约束全部实现 + 各验证脚本
- §5 ID 生命周期实现
- §6 MVP 7 验收 #1-#5 + §7 5 路径引擎实现
- §8 11 验收里引擎可独立完成的 #1 #3 #4 #5 #6 #7 #8 #9 #10
- §9 Schema 校验加载（消费，不生成）
- §10 适配器 API 实现
- §11.4 Java 对照脚本

### DeepSeek

- §9.1 / §9.2 / §9.3 Schema 反向推导 + 校验工具
- §7 5 路径的单元测试 + §8 #1 #2 测试编写
- §8 #11 黑盒对照执行（路径 1-4）
- SPEC.md 审阅意见

### Frontend（人类 + GPT Web）

- §6.7 可视化（1 卡 + 1 统领 + 1 惩罚卡）
- §9.3 `ui_event` 消费
- 视觉/交互：配色 + 印章/戳记/卷宗翻页；具体视觉稿由前端负责人定

### 人类负责人

- §13 未决事项最终拍板
- §12 回滚决策（仅当触发）
- SPEC.md 通过签字后才能通知 Codex 进 Do

---

## 附录 A：可追溯矩阵（待 Codex 实现同步填充）

| PROPOSAL 条目 | SPEC 节 | 验证位置 |
|---|---|---|
| §5 #1 引擎独立 | §4.1 | `scripts/build/verify_no_unityengine.py` |
| §5 #2 JSON 唯一 | §4.2 | `scripts/no_hardcoded_ids.py` |
| §5 #3 IEffect dispatcher | §4.3 | 单元测试 |
| §5 #4 CommandBuffer | §4.4 | grep 守卫 |
| §5 #5 IRandomSource | §4.5 | 字节级回归 |
| §5 #6 Localization | §4.6 | 启动失败测试 |
| §5 #7 contractVersion | §4.7 | §8 #9 #10 |
| §5 #8 稳定 ID | §4.8 / §5 | §8 #6 #7 #8 |
| §6 5 阶段 | §6.1 / §7.1 | 集成测试 |
| §6 惩罚链 20 | §6.2 / §7.2 | 单元测试 |
| §6 空发裁决 | §6.3 / §7.3 | 单元测试 |
| §6 先驱威压 | §6.4 / §7.4 | 单元测试 |
| §6 事件因果 | §6.5 / §7.5 | §8 #4 #6 #8 |
| §7 MVP | §6 | §8 各 |
| §8 5 路径 | §7 | §8 各 |
| §9 11 验收 | §8 | §8 各自 |

---

## 附录 B：本骨架可见的缺口（等 Codex/DeepSeek 反馈补全）

- [ ] runtime-kit-v1.30 contract → C# type mapping 完整对应表（§10 草案需逐字段对齐）
- [ ] Java 仓库最小对照场景的具体列表（DeepSeek 提供）
- [ ] §6.3 空发裁决 5 子事件哪些归"事件流"哪些归"event.data"哪个键（待 Codex 实现定）
- [ ] Unity EditMode 测试是否进 CI（人类拍板）
- [ ] .NET 工程文件 + `global.json` 版本钉死方案
- [ ] §10 适配器 API 与 `design/runtime-kit-v1.30/contracts/` 完整字段对齐表

---

> **首版说明**：本文是 v0.1 骨架。细节等 Codex 实现 + DeepSeek 测试反馈后细化。任何与 `docs/RULES.md` 或 `docs/PROPOSAL_FORGE_M1.md` 冲突的地方，按§0 优先级表解决。
