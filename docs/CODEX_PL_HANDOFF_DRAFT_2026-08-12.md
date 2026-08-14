# Codex → PL 后续计划草案（2026-08-12）

> 状态：待 MiniMax/PL 审阅；仅为技术路线草案，不改变规则、平衡、视觉方向或发布状态。
> 当前批次：Batch 1 技术收尾；本文件不代表已批准的新生产范围。

## 1. 当前可确认结果

- 91 张卡已完成字段/唯一 ID/动作引用的静态一致性复核；5 张 token 卡的缺失 `text` 字段已补齐。
- 普通效果动作固定为 24 项；当前数据实际使用 20 项，4 项保留动作未被伪造为已实现数据。
- `DISABLE_ENEMY_LEADER` 保持为独立的持久光环类型，不混入普通 `EffectSpec`，现有 `leaderDef.persistentEffects` 保持兼容。
- target aliases、效果注册表、schema 与 contract test 已对齐；Java 回归为 35/35，模拟运行完成 1200 回合。
- EventLog 根事件、父子关系和单调递增测试已写入；C# 测试尚待具备 .NET SDK 的环境执行。
- Pioneer pressure 已保留 Java `Game.effectivePunish(...)` 基线，但 C# 尚无等价回合结算 API，因此当前结论是“已记录阻塞”，不是“已通过”。

## 2. 建议的后续批次顺序

### P0：Batch 1 复验闭环

1. 在 .NET SDK 环境执行 Engine/Adapters 测试和 EventLog 测试。
2. 对 Pioneer pressure 先补“等价 API 是否属于本批”的范围判断，不先复制 Java 规则。
3. 由 DeepSeek 使用同一工作区 diff 重跑 schema、数据、回归和静态边界检查。
4. PL 根据证据矩阵决定 Batch 1 是关闭、带条件关闭，还是拆出后续任务。

### P1：B+ 控制结构设计（先提案，后实现）

建议采用独立的 `*Controls` 结构，例如 `ambushControls` / `punishControls`，不要把临时控制塞进普通效果数组。

实现前请 PL/人类负责人明确：

- 目标是否固定为 `ENEMY_LEADER`；
- 持续时间、触发窗口和来源离场行为；
- 只阻止特殊胜利，还是同时影响统领效果/登场/惩罚；
- 多来源叠加、重复施加和刷新规则；
- 是否可被 `NEGATE`、驱散或其他反制解除。

### P2：B+ 实现与双端验证

1. PL 将批准后的语义写入 proposal/contract。
2. Codex 在 Java 权威规则侧实现并添加最小场景回归。
3. Codex 再实现 C# control resolver/store 和适配器边界。
4. DeepSeek 执行 Java/C# 双端对照、schema、事件和反制路径测试。
5. 只有实现与 QA 都有证据后，才开放 schema 字段并记录完成项。

### P3：前端/视觉资产接入

- 现有 `design/runtime-kit-v1.30` 已提供档案卷宗主题、卡牌框、按钮印章、阶段戳、纸张纹理和多种状态素材；优先复用并建立导入清单，不重复生成同类资产。
- 首轮导入清单可从 `skins/default/theme.json`、`contracts/theme_contract.json`、`assets/textures/*`、`assets/raster_1x/card/*` 和 `assets/raster_1x/ui/*` 生成；这些路径只作为现有资产参考，本轮未复制或改写。
- runtime-kit 预检为 PASS（6 项），当前包内有 104 个 SVG 与 226 个 PNG；继续遵守无本地化文字、保留 gameplay component id/layout semantics 的约束。
- 先由人类负责人/GPT Web 确认档案卷宗视觉主轴、尺寸、透明背景和导入命名约定。
- 再制作少量可替换占位素材（印章、阶段戳、卡牌背面/面板装饰），每个资产附用途、尺寸、色彩和授权/来源记录。
- 资产只服务已批准的 UI/adapter 契约，不在素材阶段改变玩法、文案或规则。

## 3. 暂不应做的事

- 不删除或恢复 `.github/agents/codex.agent.md`；该文件当前由人类/代理配置审阅单独处理。
- 不把 B+ 提案提前写入生产 schema、Java 或 C# 运行时。
- 不把缺少 .NET SDK 的本地环境描述成 C# 测试通过。
- 不修改 `docs/RULES.md`、平衡值、前端最终视觉或计划任务权限。
- 不 commit、push、merge 或发布，直到人类负责人统一批复。

## 4. 请 PL 统一回答的事项

1. 是否批准 P0 的“先环境复验，再决定 Pioneer pressure 是否拆批”。
2. 是否原则批准 B+ 的独立 `*Controls` 方向。
3. B+ 的五项语义中，哪些需要升级为人类最终决策。
4. 是否在 B+ 语义批准前，保持当前 schema 的 fail-closed 限制。

## 5. 交付记录

- 详细当日报告：`docs/CODEX_DAILY_REPORT_2026-08-12.md`
- 原始 QA 报告：`docs/test/reports/QA-2026-08-12-Batch1-Followup.md`
- 当前契约：`docs/effects.contract.md`
- 当前实现说明：`docs/SPEC.md`
- 本草案只供稍后 PL 审阅，未写入 `docs/AI_MAILBOX.md`，也未提交 Git。
- 当前工作分支仍为 `main`；由于工作区已有未提交改动，本轮没有擅自切换或新建分支，待人类负责人统一批复后再处理分支整理。

## 6. 可复核命令摘要

| 检查 | 命令/依据 | 结果 |
|---|---|---|
| 构建 | `cmd /d /c scripts\\build.bat` | 通过 |
| Java 回归 | `java -cp "build\\classes;build\\test-classes" com.dominionwars.test.TestMain` | 35/35 |
| Java 模拟 | `java -cp "build\\classes;build\\test-classes" com.dominionwars.test.SimMain 100` | 1200 回合 |
| 卡牌/schema 静态审计 | Node 静态扫描 | 91 卡、91 唯一 ID、text 缺失 0 |
| target alias | schema/contract/Java/C#/tests 跨层检查 | 通过 |
| 视觉包预检 | `design/runtime-kit-v1.30/qa/preflight.json` + 资产计数 | 6/6 PASS |
| C# 测试 | `dotnet --list-sdks` / `dotnet test` | 环境阻塞：无 SDK |
| Python QA 重跑 | 本机命令探测 | 环境阻塞：无 Python |
