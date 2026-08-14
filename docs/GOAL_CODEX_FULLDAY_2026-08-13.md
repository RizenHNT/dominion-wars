# Codex 全天队列 /goal — 2026-08-13（无人值守版）

> 状态: 🟢 已派（人类手动粘贴）· 2026-08-13 02:00 · PL (DeepSeek v4)
> 派发背景: 人类负责人 2026-08-13 01:44 指示 PL 排出全天安排，人类整天外出无法联系 PL/QA。
> 队列原则: **每阶段全自足、无新设计决策、有明确验收**。所有任务均为「已批准规格/规则的实现与测试对齐」。
> 门禁: 全部任务来源 = RULES.md / SPEC.md / 契约，无新增行为语义；DeepSeek 门禁补审标记为可选（见 AI_MAILBOX）。

---

## 全局规则（每一阶段都必须遵守）

1. **只 commit 自己本阶段的文件**：用 `git add <精确路径>`，**禁止** `git add -A` / `git add .`（主仓库有大量他人未提交项）。
2. **每阶段结束**：跑一次 `dotnet test --nologo --no-restore`（本仓库根目录），确认**只增不减、全绿**；再追加一段 `docs/AI_MAILBOX.md` 报告（时间戳 + 做了什么 + 验收结果 + 阻塞）。
3. **不 push**（无 push unless 人类明示）。
4. **单文件 ≤500 行**（PROPOSAL §5.9 硬约束 #9）。
5. **遇到环境阻塞**：如实记录到 AI_MAILBOX，不要伪造结果、不要隐藏失败，跳到下一阶段继续。
6. 每阶段完成后，在 AI_MAILBOX 报告里明确写「✅ 完成」或「⏳ 阻塞+原因」。
7. 所有行为断言**必须对齐 RULES.md 与 Java 基线**（Java 只参考，规则权威在 RULES + Java 行为基线）。

---

## P1 — 6A-1 收尾：commit + 全量回归（0.5-1h）

**背景**：你已实质完成 Batch 6A-1（C# JSON 卡牌/卡组加载器，269/269 通过，01:47 已报告）。当前**未 commit**。

**任务**：
1. 精确提交以下路径（只这些，不要带其他 dirty 文件）：
   - `src/Data/DominionWars.Data.csproj`
   - `src/Data/DominionWars.Data.asmdef`
   - `src/Data/CardCatalog.cs`
   - `src/Data/DeckLoader.cs`
   - `src/Engine/Tests/DataLoaderTests.cs`
   - `DominionWars.sln`
2. commit message 格式参照仓库历史（如 `feat: add JSON card/deck loader (Batch 6A-1)`）。
3. 全量回归：`dotnet test --nologo --no-restore` → **269/269 全绿**。
4. AI_MAILBOX 追加报告：commit SHA + 回归结果。

**验收**：
- [ ] `git log -1` 显示 6A-1 commit，且只含上述 6 个文件
- [ ] `dotnet test` 269/269 全绿
- [ ] AI_MAILBOX 有 `[Codex→PL] 6A-1 committed` 报告段

---

## P2 — 6A-2：SPEC §8 测试补缺 + Buff 负值闭环（2-3h）

**背景**：SPEC §8「首切片 11 验收」当前覆盖状态（已核实）：
- #1 纯 .NET 测试 ✅ 已有
- #3 固定种子 ✅ 已有（SeededRandomSource(42)）
- #4 因果链 ✅ 已有（AdapterContractTests）
- #6 ID 校验 ✅ 已有
- #7 过期 action/snapshotRevision ✅ 已有
- #8 根标记 + eventId 单调 ✅ **已有**（EventLogTests.cs，跳过）
- #9 contractVersion 启动拒绝 → 若已有则跳过，没有则补
- #10 JSON 数据 + 契约版本校验拒绝 → 6A-1 已覆盖坏 JSON fail-closed，可跳过
- **#5 惩罚/空发/威压单元测试** → 缺（`src/Engine/Tests/Effects/` 目录不存在）
- **#11 与 Java 对照 run_dual.py** → 缺（`scripts/java_compare/` 不存在）

### 2a. SPEC §8 #5：Effects 单元测试补齐（核心交付）

新建 `src/Engine/Tests/Effects/` 目录，按 RULES.md 与 EffectRuntime 现有行为写单元测试，覆盖：

1. **威压（Punish pressure）**：`AddOppPunishTurn` / `AddSelfPunishTurn` 正确更新 `PunishDeltaThisTurn`；负数 delta（自惩罚减免）按 RULES 语义处理。
2. **惩罚结算**：`PUNISH` 效果（如 flame_berserker 的 PunishActivatable/PunishCost 数据加载）→ 引擎结算路径。
3. **空发（Skipped）**：目标为空 / amount 非法（0 或负，按效果类型）→ emit `EFFECT_SKIPPED`，不 crash，不产生副作用。

对齐依据：
- RULES.md §11（数值结算）、§7.4（威压）
- `src/Engine/Effects/EffectRuntime*.cs` 现有实现
- 现有测试 `EffectRuntimeTests.cs` / `CounterWindowTests.cs` 的风格（NUnit，`game.Apply(...)` + Assert）

### 2b. Buff 负值专项测试（闭环 DeepSeek 🟡 遗留）

DeepSeek QA 报告（AI_MAILBOX L269）遗留问题：「Buff 是否允许负值」。**答案已在 RULES.md §11.1，无需新决策**：
- Attack / 持续时间 / Cost / 其他通用 stat：负值 **clamp 到 0**
- HP / 生命值：保持负数参与计算，结算后若 <0 判定死亡
- `amount = 0`：视为无意义 buff，报错

写测试验证现有 `EffectRuntime.Buff` 已按此实现（Attack clamp、HP 保持、amount=0 抛/跳过），若实现有偏差**报告给 PL 不要自行改规则语义**（如与 RULES 冲突，记录为阻塞）。

### 2c. SPEC §8 #11：run_dual.py（脚本交付）

新建 `scripts/java_compare/run_dual.py`：同 JSON 同 seed 跑 Java + C#，比对最终 snapshot。
- 参考 SPEC.md L385 附近用法说明。
- **环境风险**：你的执行环境无 python 可执行文件（01:47 已记录）。**交付物 = 脚本本身 + 运行说明**；验证部分若环境不允许，如实标注「待 PL/人类侧验证」，不要伪造输出。
- 若你环境其实有 python，就实际跑通并记录结果。

**验收**：
- [ ] `src/Engine/Tests/Effects/` 新增测试文件（≤500 行/文件），覆盖威压/惩罚/空发
- [ ] Buff 负值专项测试：Attack clamp / HP 保持 / amount=0 处理，全绿
- [ ] `scripts/java_compare/run_dual.py` 存在 + 运行说明（或明确环境阻塞）
- [ ] `dotnet test` 只增不减全绿（基线 269 → 新增 ≥10）
- [ ] AI_MAILBOX 报告段（含 #5/#11/Buff 各自结果）

---

## P3 — 6B-1：Unity 工程壳（3-4h）

**背景**：Unity Personal 许可证已激活（`C:\Users\USER\AppData\Local\Unity\licenses\UnityEntitlementLicense.xml`，2026-08-13 00:02 起）；Unity 6000.3.21f1 Editor 已装。**6B 阻塞已解除，可排。**

**目标**：新建 Unity 工程壳，接入现有 .NET Engine/Adapters/Data，EditMode 测试跑通，Windows 构建成功。**本阶段无任何视觉/UI 设计**，纯工程接线。

**任务**：
1. 用 Unity CLI 命令行创建新工程（例如 `unity/DominionWars.Unity/`，路径以仓库结构为准）：
   `"C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Unity.exe" -batchmode -createProject <path> -quit`
2. 建立 asmdef 接线：
   - 引用 `src/Engine/`、`src/Adapters/`、`src/Data/`（这三个已各自有 .asmdef 或需补）
   - 规则权威在 Engine，**renderer/UI 不复制规则**
3. EditMode 测试：
   - 用 `src/Tests.EditMode/`（或 Unity 内 Tests.EditMode asmdef）引用 Engine 测试
   - 跑通后记录结果（Unity CLI `-runTests -testPlatform EditMode`）
4. Windows 构建：`-buildWindows64Player` 出一个可执行产物（产物路径记录到 AI_MAILBOX，不 commit 构建产物）
5. 完成后 AI_MAILBOX 报告：工程路径、asmdef 接线图（文字）、EditMode 测试数、构建产物路径。

**关键约束**：
- **不创建任何视觉元素/场景美术**（UI 属人类 frontend lead 领域，视觉方向待人类拍板）。
- 不确定的 Unity 命令参数先 `-help` 或查官方文档，不要猜。
- 若命令行创建工程遇到许可证/序列化问题，如实记录阻塞。

**⚠️ 已知坑（PL 08:25 兜底补充，务必先读）**：
- Unity 6000 无头 CLI（`-batchmode -nographics -quit`）在**无 GUI 会话**下会卡在 `[Licensing::Module] Licensing is not yet initialized`，长时间不返回成功码。**许可证文件其实在**（`C:\Users\USER\AppData\Local\Unity\licenses\UnityEntitlementLicense.xml` 存在），这是 Unity 无头模式的已知行为，不是许可证没激活。
- **不要再无限重试无头 CLI**（02:31/02:52/08:18 已试三次，均卡同一点）。判定标准：
  - 若 `-logFile <path>` 下 Unity 进程超过 ~5 分钟仍不退出、且日志停在 Licensing 行 → **判定为"运行时验证待人工"**，终止该进程。
  - P3 的**静态验收项（工程目录 + asmdef 引用 + EditMode 测试文件 + manifest 本地包引用 + ProjectVersion）已完成即视为 P3 配置达标**。
  - 剩余"EditMode 跑通 + Windows 构建"两项**留给人类 Hub 交互打开一次后再补跑**，Codex 不要伪造结果。
- 若 P3 判定为配置达标，则 P4 的 EditMode 引擎对局冒烟**代码可以照写**（离线可验证），但"实际跑通"同样待人类 Hub 打开后验证。

**验收**：
- [ ] Unity 工程目录存在，asmdef 正确引用 Engine/Adapters/Data
- [ ] EditMode 测试跑通（记录测试数）
- [ ] Windows 构建产物存在（记录路径）
- [ ] AI_MAILBOX 报告段

---

## P4 — 6B-2 前置：引擎驱动对局验证（可选，2-3h，若 P3 提前完成且仍有精力）

**背景**：6B-2 垂直切片（一阵营一统领约十卡可玩）涉及视觉方向，**不在此无人值守队列内**（待人类拍板视觉方向）。但可以在 Unity 壳内做「引擎驱动对局验证」——纯逻辑、无 UI。

**任务**（全部不做任何 UI）：
1. 在 Unity EditMode 测试里跑一局完整引擎对局（用 `data/decks/` 4 套卡组之一 vs 另一），验证 Engine 经 Adapters 在 Unity 环境可正常推进：手牌→出牌→战斗→结束回合→胜利判定。
2. 记录：对局能否完整跑通、事件链是否单调、胜利判定是否触发。
3. 不实现任何场景/渲染/交互。

**验收**：
- [ ] EditMode 内引擎对局冒烟测试通过（记录对局步骤数）
- [ ] 无任何 UI 代码
- [ ] AI_MAILBOX 报告段

---

## 收尾（全天结束前必做）

1. 最终全量回归：`dotnet test --nologo --no-restore` 全绿。
2. AI_MAILBOX 写「全天总结」段：P1-P4 各 ✅/⏳ + 各自 commit SHA + 阻塞清单 + 明日建议。
3. 不 push。所有 commit 留在本地，等人类晚上回来验收。

## 人类晚上检查清单（粘贴给 Codex 前请知悉）

| 阶段 | 检查点 |
|---|---|
| P1 | 6A-1 commit 存在，只含 6 文件，269/269 |
| P2 | Effects/ 测试、Buff clamp 测试、run_dual.py |
| P3 | Unity 工程 + EditMode 测试文件 + Windows 构建产物。**注意**：无头 CLI 验证卡在 Licensing 已知行为，配置已达标；晚上先 Hub 打开工程一次（完成包解析+许可证初始化），再让 Codex 补跑 EditMode/构建 |
| P4 | 引擎对局冒烟（可选） |
| 收尾 | dotnet test 全绿 + AI_MAILBOX 全天总结 |
