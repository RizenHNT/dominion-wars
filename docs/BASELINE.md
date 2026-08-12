# Baseline — 事实基线（单一起源）

> **建立**：2026-08-12 23:50 · **负责人**：PL · **目的**：每次写 /goal 前查这里，不靠记忆/猜测
> **更新规则**：每完成一个 batch 或发现数字变化 → 更新对应行 + 日期；旧值留注

---

## 测试基线（2026-08-12 实测）

| 测试 | 通过/总数 | 命令 | 备注 |
|------|-----------|------|------|
| C# dotnet test | **264/264** | `dotnet test --nologo` | 0 failed / 0 skipped / 372ms |
| Java TestMain | **38/38** | `javac -encoding UTF-8 ... && java -cp ... com.dominionwars.test.TestMain` | 需先编译 27 main + 3 test |
| SchemaValidator | **91/91** | `pwsh scripts\validate-cards.ps1` | pass=91 fail=0 files=5 |
| sanity_check_v2.py | **0 ERROR / 0 WARN / 0 INFO** | `python scripts\sanity_check_v2.py` | 需 `PYTHONIOENCODING=utf-8` |
| align_check.py | **OK（无悬空引用）** | `python scripts\align_check.py` | 需 `PYTHONIOENCODING=utf-8` |

## 覆盖率（2026-08-12，cobertura）

| 指标 | 数值 | 来源 |
|------|------|------|
| Line coverage | **88.53%** | `src\Engine\Tests\TestResults\coverage\coverage.cobertura.xml` |
| Branch coverage | **76.15%** | 同上 |

## 代码库事实

| 项 | 数值 | 验证 |
|----|------|------|
| Engine 生产文件 | `src/Engine/*.cs` | dotnet 工程 DominionWars.Engine |
| Adapter 生产文件 | `src/Adapters/*.cs` | DominionWars.Adapters |
| Java main 源文件 | 27 个（`src/main/java`） | 实际编译 |
| Java test 源文件 | 3 个（`src/test/java`） | 实际编译 |
| EngineProjectionAdapter | 305 行 / 7 public 方法 / 20 snapshot 字段 | 实测（Codex 曾报 348，虚报 +14%） |
| LegalActionGenerator | 125 行 | 实测（Codex 曾报 139，虚报 +11%） |

## 已知缺口（未完成）

| 缺口 | 说明 | 归属 |
|------|------|------|
| CardDto 字段 | 缺 Faction / Text / Flavor / Cost / Rarity / ArtId | Codex 下一轮 |
| LegalActionGenerator | 仅 DTO + 格式转换，生成逻辑未写 | Codex 下一轮 |
| 快照反例校验 | 只测正向映射，未测非法字段 reject | Codex 下一轮 |
| Unity 6 LTS | 未安装 → 6.x/7.x/8.1 全阻塞 | 人类决策 |
| 自动接力 | 框架在但 bot 未配置 → 无 24h 接力 | Codex 接力任务 |

---

## 变更历史

| 日期 | 变更 |
|------|------|
| 2026-08-12 | 初始基线建立（264/264、38/38、91/91、88.53%、45%） |
