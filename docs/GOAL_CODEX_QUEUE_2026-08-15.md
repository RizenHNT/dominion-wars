# Codex 队列 /goal — 2026-08-15（统领裁决同步 + 待办收口）

> 状态: 🟡 已起草（待人类手动粘贴给 Codex）· 2026-08-15 · PL (DeepSeek v4 Flash)
> 派发背景: 2026-08-15 人类拍板三裁决（删 DISABLE_ENEMY_LEADER / 统领全量重设计 / machine_alpha 下载轴），PL 已同步文档。本队列收口 Codex 现有待办，让 Codex 一口气跑完。
> 队列原则: 每阶段全自足、无新设计决策、有明确验收。所有任务均为「已批准规格/规则的实现与测试对齐」。
> 门禁: 任务来源 = mailbox 派发段 / RULES / 合同，无新增行为语义。卡牌设计（新卡/统领重做/machine_alpha 阈值）归 DeepSeek 出提案，本队列不设计卡牌。

---

## 全局规则（每一阶段都必须遵守）

1. **只 commit 自己本阶段的文件**：用 `git add <精确路径>`，**禁止** `git add -A` / `git add .`（主仓库有大量他人未提交项，尤其你自己的工作树未提交内容——先梳理哪些是你之前的成果、哪些是他人/PL 的）。
2. **每阶段结束**：跑一次 `dotnet test --nologo --no-restore -m:1 /nodeReuse:false`（本仓库根目录），确认**只增不减、全绿**；再追加一段 `docs/AI_MAILBOX.md` 报告（时间戳 + 做了什么 + 验收结果 + 阻塞）。
3. **不 push**（无 push unless 人类明示）。
4. **单文件 ≤500 行**（PROPOSAL §5.9 硬约束 #9）。
5. **遇到环境阻塞**：如实记录到 AI_MAILBOX，不要伪造结果、不要隐藏失败，跳到下一阶段继续。
6. 每阶段完成后，在 AI_MAILBOX 报告里明确写「✅ 完成」或「⏳ 阻塞+原因」。
7. 所有行为断言**必须对齐 RULES.md 与 Java 基线**（Java 只参考，规则权威在 RULES + Java 行为基线）。
8. **卡牌/统领/数值设计不属于本队列**——设计批次已派 DeepSeek（QA/策划），你的卡牌相关改动只限「已拍板的机制删除」，其余等设计提案过 PL + 人类拍板。

---

## P1 — wire 实体 ID C# 边界同步（mailbox L3856 派发段，收口）

**背景**：人类已拍板「对局实体 ID = 裸 JSON integer（64-bit ≥1）」，PL 已改 schema/fixture/合同并验证 5/4/0 + 386/386。**C# wire 边界仍硬编码 `entity_` 字符串，需你同步**，否则 Unity 端 payload 与 schema 不符。

**任务**（mailbox L3856 派发段 #1-#5 全量）：
1. **出站** `RuntimeContractV131Snapshot.EntityId`（L127-131）+ `TargetId`（L133-152）：实体 ID 输出**裸数字**；命名 ID（`castle`/`player_0`/`player_1`/`leader_0`/`leader_1`/`prompt_*`）保留字符串
2. **入站** `RuntimeMatchGateway.TryParseEntityId`（L180-185）+ `ToEngineTarget`（L187-200）：接受裸数字 → 引擎内部 `entity_...`；`selectedEntityIds` 已支持裸数字（Convert.ToInt64 ✅）
3. **引擎内部**可继续 string `entity_...`（非 wire，不用改）；`TargetPolicy.cs:152` / `AttackTargetPolicy` 为引擎内部，确认是否影响边界
4. **测试断言**更新：`AdapterContractTests` / `RuntimeSnapshotProjectionTests` / `SerializationTests` / `SnapshotMapperTests` / `RuntimeActionBoundaryTests` 的 `entity_0000...` 断言 → 裸数字
5. `EngineProjectionAdapter.ToEntityId`（L374）：确认是否在 1.31 wire 路径；若为 1.30 Dto 层可留旧格式或同步，需报告

**验收**：
- [ ] schema 校验 `validate-runtime-contract.ps1` 仍 5 valid/4 invalid/0 fail
- [ ] `dotnet test --nologo --no-restore -m:1 /nodeReuse:false` 全绿（基线 386，只增不减）
- [ ] 新增/更新测试验证 wire 裸数字往返（出站裸 int + 入站裸 int → 引擎 entity_）
- [ ] AI_MAILBOX 报告段（含 #3/#5 判断结论）

---

## P2 — DISABLE_ENEMY_LEADER 机制删除（mailbox L3934 派发段，收口）

**背景**：人类裁决机制级删除 DISABLE_ENEMY_LEADER，不作保留。PL 已同步文档（effects.contract / 合同 §6 / RULES §11.2 三步校验链保留）。

**任务**（mailbox L3934 派发段全量，数据 + 引擎 + parity + 测试）：
1. `data/cards/neutral.json` shadow_of_fate：移除 `persistentEffects:[{action:"DISABLE_ENEMY_LEADER"}]` + 更新卡面 text（去掉"对方统领效果与特殊胜利条件被禁用"表述）
2. `data/schema/cards.schema.json` L88-91 `PersistentEffectAction` 枚举（现仅含 DISABLE_ENEMY_LEADER）：机制删除后无合法值——删除该枚举 + `PersistentEffectSpec` 定义，或保留结构待重设计批次启用（**倾向删除，避免空枚举**；shadow 重设计如需新 persistent 光环随批次重建）
3. `src/Data/CardCatalog.cs`：PersistentActions 移除 DISABLE_ENEMY_LEADER
4. Java parity：`Game.java:816`（disable 处理）+ `TestMain.java:627`（shadow persistent 测试）
5. C# 合同测试：ContractBoundaryTests / EffectsSpecContractTests 断言更新（DISABLE 不再存在于 schema persistent actions）

**注意**：RULES §11.2 三步校验链（kingSlayer→vulnerabilities→disable_resistance）是通用统领交互模型，**保留**；仅删 DISABLE_ENEMY_LEADER 这个具体 persistent 动作。shadow 卡最终形态待重设计批次（DeepSeek 出）。
**验收**：
- [ ] `dotnet test --nologo --no-restore -m:1 /nodeReuse:false` 全绿
- [ ] schema 校验 0 fail；Java `java -cp "build/classes;build/test-classes" com.dominionwars.test.TestMain` 38/38
- [ ] AI_MAILBOX 报告改动清单

---

## P3 — 10.10.2 合同 fixture/ schema 补强（QA 深审 4/6 项，Codex 责任内）

**背景**：DeepSeek 深审 QA 报告（2026-08-14）发现 6 条 schema 问题，其中 4/6 明确属 Codex 的 10.10.2 补强项。1/2/3 属 wire 语义待 PL/人类定稿，不在本队列。

**任务**：
1. **fixture 覆盖补强（QA #4，低）**：每类目前仅 1 valid + 1 invalid。补：pendingPrompt 非空、带 payload 的 PLAY_CARD/ATTACK、rejected result、`parentEventId` 非空等。
2. **payload 内 `targetId` pattern 对齐（QA #6，低）**：顶层 `targetId` 有 pattern，payload 内是裸 string——同一概念两套约束，统一（裸数字为主，命名 ID 给 pattern）。

**验收**：
- [ ] fixture 新增 ≥3 个（valid/invalid 各补），`validate-runtime-contract.ps1` 全过（数量更新后记录 valid/invalid/fail 新数）
- [ ] payload `targetId` 约束与顶层一致
- [ ] `dotnet test --nologo --no-restore -m:1 /nodeReuse:false` 全绿
- [ ] AI_MAILBOX 报告段

---

## P4 — Unity Windows 构建补跑（6.0 验收）

**背景**：10.10.6 Unity EditMode 已解锁（2026-08-15 凌晨人类 Hub 交互打开工程 + Test Runner 通过；许可证一直激活 `UnityEntitlementLicense.xml`；`Library/ScriptAssemblies/` 编译产物齐备）。剩 **Windows 构建** 归 6.0 验收，现可排。

**任务**：
1. 用 Unity CLI 构建 Windows 64 player：`"C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Unity.exe" -batchmode -quit -projectPath <unity工程路径> -buildWindows64Player <产物输出路径> -logFile <日志路径>`
2. 记录产物路径 + 构建日志关键行；不 commit 构建产物（产物路径记到 AI_MAILBOX）。
3. 若无头 CLI 又卡 Licensing（无 GUI 会话已知行为），如实记录阻塞，不伪造成功；由人类 Hub 再开一次后补跑。

**验收**：
- [ ] Windows 构建产物存在（记录路径）
- [ ] AI_MAILBOX 报告段（含构建日志关键行 / 阻塞说明）

---

## 收尾（全天结束前必做）

1. 最终全量回归：`dotnet test --nologo --no-restore -m:1 /nodeReuse:false` 全绿 + Java 38/38。
2. AI_MAILBOX 写「全天总结」段：P1-P4 各 ✅/⏳ + 各自 commit SHA + 阻塞清单 + 明日建议。
3. 不 push。所有 commit 留在本地，等人类验收。

## 人类检查清单

| 阶段 | 检查点 |
|---|---|
| P1 | wire 裸数字往返测试存在；schema 5/4/0；dotnet 386+ 全绿 |
| P2 | DISABLE 从 schema 枚举/数据/引擎/parity 全移除；RULES §11.2 三步链保留 |
| P3 | fixture 补强；payload targetId 对齐 |
| P4 | Windows 构建产物存在（或记录真实阻塞） |
| 收尾 | dotnet 全绿 + Java 38/38 + AI_MAILBOX 全天总结 |

## 本队列之外（明确不在此做）

- **卡牌/统领/数值设计**：归 DeepSeek（QA/策划）出提案 → PL 审 → 人类拍板 → 再派实现。
- **10.10.10 Java 归档**：human_required，本目标禁止。
- **Renderer/UI / Windows 发布**：不得进入。
- 若 DeepSeek 卡牌设计批次先产出模板，可另派实现批次（不在本队列内）。
