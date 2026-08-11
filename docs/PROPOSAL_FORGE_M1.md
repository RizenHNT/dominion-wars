# PROPOSAL_FORGE_M1 —— Dominion Wars Unity 重建提案

> **状态**：🟢 人类负责人批准（架构决策，已明确批准）
> **PL**：MiniMax（代 Claude 规划职责）
> **审阅**：Codex（程序侧 ✅ 通过，§9 修正已纳入）、DeepSeek（测试侧 ✅ 通过，§8 修正已纳入）
> **关联**：决策与审阅记录见 `docs/AI_MAILBOX.md`（2026-08-09 / 2026-08-11 各条）；顶层决策要点见本文档 §1。
> **修订**：2026-08-11 — §8 5 路径含 4 处修正（与 `docs/RULES.md` 对齐）+ 路径 5 SPEC 细化；§9 验收维度由 7 拆为 11；§5 #2 强化 JSON 唯一数据源规则；§6 eventId 补充跨 Snapshot 持久；§10 增 eventId 体系说明。
> **修订**：2026-08-11 18:02 — §13 Do 阶段准入 5/7 完成（SPEC + schema + effects.contract 入位）；§8/§9 修正逐项落 SPEC v0.1，Codex 条件"以上修正进入 SPEC 后程序侧通过"已具备，等 Codex + DeepSeek 正式 🟢 回执。

---

## 1. 目标 ✅

- Unity 6 LTS + C# 重建 Dominion Wars
- Steam Windows 单机 MVP
- Java 仓库 = **行为基准 + 黑盒对照测试**（不接受"只参考后丢弃"）

## 2. 非目标（Post-MVP）

- 完整 91 卡 / Castle / 5 阵营 / 完整数值平衡
- 联机 / 服务端 / 排行榜
- 3D 渲染 / 完整音频 / 完整无障碍
- Mac / Linux 平台

## 3. 视觉主轴 ✅

**档案卷宗**（Paper Archive / Dossier）—— 继承 `design/runtime-kit-v1.30/`

| 令牌 | 值 | 隐喻 |
|---|---|---|
| `surface.paper` | `#F3EDE0` | 古纸米黄 |
| `surface.ink` | `#11110F` | 墨黑 |
| `action.primary` | `#BC2C22` | 朱红封蜡 |
| 阵营 | 朱/青/绿/金/灰 | 印章色谱 |

字体：衬线 + CJK + 等宽数字
元语言：印章 / 戳记 / 归档 / 卷宗翻页
图标：印章 / 9-slice 文件夹 / 阶段戳

## 4. 引擎架构

- **Engine DLL/AssemblyDefinition 完全不引用 UnityEngine**（Codex 硬约束）
- 引擎为独立程序集，可无 Unity 编辑器运行
- 确定性 `IRandomSource`，禁 `UnityEngine.Random` 直接调用
- `CommandBuffer` 接口（MVP 本地，Post-MVP 复用 Netcode）

## 5. 抗负债硬约束（8 条）

1. Engine DLL 独立 ── UI 不可反向引用
2. 数据 JSON + ScriptableObject ── **JSON 是唯一数据源**；ScriptableObject 仅用于生成或缓存，运行时不可写
3. `IEffect` + `EffectDispatcher` ── 21 动作走接口
4. `CommandBuffer` 接口 ── 后期换网络
5. `IRandomSource` ── 无 `UnityEngine.Random` 直接调用
6. `Localization.Get("key")` only ── 硬编码 = 编译错
7. `contractVersion` 校验 ── 不兼容 = 启动拒绝
8. **稳定字符串 ID**（Codex 加固） ── 卡牌/事件/动作严禁 Unity Instance ID

## 6. ID 生命周期（Codex 强制）

| 类型 | 规则 |
|---|---|
| 卡牌 ID | JSON 稳定字符串，跨局复用 |
| 对局实体 ID | 单局稳定可复现的递增 ID |
| `eventId` | 单局内单调递增；跨 Snapshot 持久 |
| `parentEventId` | 直接因果父事件 |
| `actionId` | 仅在对应 Snapshot 版本内有效，携带 `snapshotRevision` |
| `contractVersion` | 进入 Snapshot / Action / Event 三类外部结构 |

## 7. MVP 7 验收

1. 4 阶段循环运行（START / AMBUSH / ACTION / DISCARD）
2. 惩罚链 20 层限制
3. 空发裁决（惩罚值 > 对手牌组数）
4. 先驱威压（仅 1 统领在战场 → 对手惩罚 +1）
5. 事件因果链输出（eventId / parentEventId）
6. **5 核心路径测试**（§8）
7. 1 卡 + 1 统领 + 1 惩罚卡，端到端可视化

## 8. 5 核心路径（DeepSeek 定义；2026-08-11 复审修正）

| # | 路径 | 断言骨架 |
|---|---|---|
| 1 | 5 阶段循环 | START → AMBUSH → ACTION → DISCARD → **END** → 下回合；每阶段事件含 `phase` 字段 |
| 2 | 惩罚链 | add punish + 源卡 → 达到 20 层后**不再响应**（不结算） |
| 3 | 空发裁决 | 惩罚值 > 对手牌组数 → **计为使用 / 消耗词条 / 效果不结算 / 对方不抽牌 / 主动回合进弃牌结束回合** |
| 4 | 先驱威压 | 唯一统领在战场 → 对手惩罚 +1、**统领方弃牌上限 +2**（`pioneerHandLimitBonus`）；事件含 `source: pioneerPressure` |
| 5 | 事件因果 | A 触发 → A.eventId → B.parentEventId = A.eventId；可遍历 root；根事件 `parentEventId=null` |

## 9. 首切片验收（Codex 7 条 → 2026-08-11 复审拆为 11 条）

1. 纯 .NET（无 Unity 编辑器）C# 引擎测试 ── EditTime 路径
2. Unity EditMode 测试 ── 编辑器内运行（如适用）
3. 固定随机种子 → 结果可复现
4. Snapshot → LegalAction → Command → Event 完整因果链
5. 惩罚链 / 空发裁决 / 先驱威压 三项规则测试
6. 所有实例 ID、事件 ID、父事件关系可校验（含根事件 `parentEventId=null`）
7. 过期 action / `snapshotRevision` 不匹配 → 拒绝执行
8. 根事件标记 + 单局内 eventId 单调递增 → 可校验
9. 缺失或不兼容 `contractVersion` → 启动拒绝
10. JSON 数据 + 契约版本校验失败明确拒绝加载
11. 与现有 Java 引擎跑同一组最小场景并比较结果（路径 1-4 对照；路径 5 因 Java 无 eventId 系统，C# 自检）

## 10. Schema 已知缺口（SPEC 阶段补齐）

- `game_snapshot.schema.json`：`players[] / castle / legalActions[]` 子结构为空，需补
- `ui_event.schema.json`：`data` 自由 object，需约束各事件类型字段
- `data/cards/*.json`：91 卡无 Schema 校验，需反向推导 `cards.schema.json`
- `eventId` 体系：Java 引擎无事件 ID 系统（仅 textual logs），C# 引擎为唯一权威实现；黑盒对照仅适用路径 1-4；路径 5 由 C# 自检（eventId 单调递增 + parentEventId 关系 + 跨 Snapshot 持久）

## 11. 风险

| 风险 | 等级 | 缓解 |
|---|---|---|
| 引擎切换成本 > 继续 Java 接入 | 高 | Java 作黑盒对照测试基线 |
| 视觉系统完全平移成功率 | 中 | `theme.json` + `motion_primitives.json` 全量平移 |
| 91 卡 JSON Schema 重构 | 中 | SPEC 阶段必做，DeepSeek 协助 |
| Unity 6 LTS C# 与 Java 行为差异 | 中 | Code 项 7 强制对照测试 |

## 12. 角色与流程

- **PL**：MiniMax（代 Claude）—— 规划 / 决策 / 验收
- **实现**：Codex ── 引擎 + 适配器 + 构建
- **测试**：DeepSeek ── Schema / 5 路径 / 回归
- **前端**：人类 + GPT Web ── 结构 / 视觉 / 文案
- **决策**：人类负责人

## 13. Do 阶段准入

- [x] 本提案（PROPOSAL_FORGE_M1.md）
- [x] 人类负责人明确批准引擎切换
- [x] `docs/SPEC.md` 骨架（含 8 硬约束 + 7 验收 + 5 路径）
- [x] `data/cards.schema.json`（DeepSeek 协助）
- [x] `docs/effects.contract.md`（21 动作 + 目标枚举）
- [ ] 平移 `design/runtime-kit-v1.30/` 到 Unity 包
- [ ] `AI_MAILBOX.md` 通知 Codex 进 Do

## 14. 协作约束

- **PL 决策前先广播**至 `docs/AI_MAILBOX.md`，等 Codex/DeepSeek 回执（Codex 阻断）
- **架构决策不可沉默批准**（Codex 阻断）── 必须人类明确批准
- 留言 ≤5 行，详见 `docs/AI_WORKFLOW.md`
