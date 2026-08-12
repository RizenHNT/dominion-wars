# PL Checklist — 派发 /goal 前强制检查清单

> **创建**：2026-08-12 · **负责人**：PL (MiniMax M3 / DeepSeek v4) · **目的**：根治"三次不验证基础设施"的系统性缺陷
> **触发**：每次给 Codex / DeepSeek 写 /goal 之前，逐条过一遍。不跑完不派发。
> **依据**：DeepSeek 测试岗建议（2026-08-12）· PL 自我承诺（2026-08-12）

---

## 5 条强制验证项

| # | 检查项 | 命令 / 方法 | 判定 |
|---|--------|------------|------|
| 1 | **每个声称"已存在"的文件** | `Test-Path <路径>` | 必须返回 True |
| 2 | **每个声称的测试数字** | `dotnet test --nologo`（或对应 runner） | 必须实际跑，数字以输出为准 |
| 3 | **每个声称的类/方法** | `Select-String -Path <文件> -Pattern '<符号>'` | 必须找到定义行 |
| 4 | **每个声称的对比数据** | `git show --stat <commit>` 或打开数据文件 | 必须存在且非空 |
| 5 | **工时估算** | 逐 Phase 加总 | 每 Phase ≥ 0.5h，总工时=Codex 真实工时的 1.5-2 倍 |

## 附加纪律

| # | 规则 | 说明 |
|---|------|------|
| 6 | **Baseline 不靠记忆** | 写 /goal 前先查 `docs/BASELINE.md`，引用真实数字 |
| 7 | **强制门禁审查** | 写完后交给 DeepSeek 审一遍（签字）再发 Codex |
| 8 | **诚实认错** | 第三次重复"不验证"已升级；再犯直接报告人类 owner |

---

## 历史教训（为什么要有这份清单）

| 批次 | PL 声称 | 实际 | 根因 |
|------|---------|------|------|
| Batch 3 | "SnapshotMapper.cs 已存在" | 不存在 | 没跑 Test-Path |
| Batch 5 原版 | "Localization.cs 已存在" / "Java 模拟器可对比" / "SimRunner 对局循环" | 三个都不存在 | 没跑 Test-Path / 没读文件 |
| Batch 5 修正版 | EventLogTests 有 5 个测试 | 4 个（把 [TestFixture] 当 [Test]） | 没跑 Select-String 区分属性 |

**结论**：不是技术问题，是流程缺失。本清单是强制项，不是建议项。

---

## 每次 /goal 派发前流程

```
1. 读 docs/BASELINE.md → 记录当前真实基线
2. 逐条过 PL_CHECKLIST 5 条 → 每条记录命令 + 输出
3. 写 /goal 文本 → 每个引用附 evidence（commit / 行号 / 数字）
4. 交 DeepSeek 门禁审查 → 通过后才发 Codex
5. 派发后更新 DASHBOARD 目标进度
```
