# Runtime Contract v1.31 — canonical-current 唯一入口

> **本目录是 Canonical Runtime Contract 的 canonical-current 唯一入口。**
> 版本：**1.31** · 合同状态：**DRAFT→决策已回填 / Gate 已解除 BLOCKED**（残留 1 项 HUMAN_REQUIRED：shadow_of_fate，见主文档 §6）
> 负责人：PL（DeepSeek v4）起草 · 人类负责人 2026-08-14 已裁决 · 依赖 ARCHITECTURE_REVIEW.md §10 Step 0-8

## 阅读顺序

1. [RUNTIME_CONTRACT_1.31.md](./RUNTIME_CONTRACT_1.31.md) — 主文档（目的 / 版本规则 / 12 条待确认项书面回答 / 变更清单 / 批准记录）
2. [RULES_QUESTIONS_FOR_PLANNER.md](./RULES_QUESTIONS_FOR_PLANNER.md) — 需 QA/策划拍板的规则语义清单（HUMAN_REQUIRED，Gate 阻塞来源）

## 版本与入口约定

- **canonical-current** = 本目录。旧 `design/runtime-kit-v1.30/` 是历史基线，不再作为 canonical-current 入口。
- **$id grammar**：`urn:dw:runtime:contract:1.31:<name>`
- **contractVersion（整数）**：`1`（wire format 兼容性信号；不变则兼容）
- **design package version**：`1.31`（文档包语义版本）

> 本目录 2026-08-14 从 `design/runtime-contract-v1.31/` 迁移而来，对齐 v1.30 的 `runtime-kit-vX.Y/contracts` 命名体系。

> 详细版本关系与 1.30 → 1.31 兼容性判定见主文档 §3、§4。
> **Gate 状态**：A-E 已回填（人类裁决 2026-08-14 + DeepSeek A-E 答案）→ **Gate 已解除 BLOCKED**，实现者可进入 Step 1（strict wire schema）。残留 1 项 HUMAN_REQUIRED（shadow_of_fate 可达胜利条件，主文档 §6）不阻塞 schema 主体。
