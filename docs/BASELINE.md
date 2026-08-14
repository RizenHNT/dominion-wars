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

> ⚠️ 2026-08-13 00:10 PL 复核：原缺口表 3 项已过时，已实测确认补齐（见下）。真缺口仅剩 **C# JSON 卡牌加载器** + **Unity 工程**。

| 缺口 | 说明 | 归属 |
|------|------|------|
| ~~CardDto 字段~~ ✅ | **已补**：Faction/Text/Flavor/Cost/Rarity/ArtId 在 Batch 3 e1b53d2 加入；Adapter EngineProjectionAdapter.cs L272-277 已映射（2026-08-13 实测） | — |
| ~~LegalActionGenerator~~ ✅ | **已补**：src/Engine/LegalActionGenerator.cs 存在（4479B，Generate 含 PLAY_CARD/ATTACK/END_TURN/ACTIVATE_PUNISH/USE_LEADER_ABILITY）+ 独立测试 LegalActionGeneratorTests.cs（5681B）（2026-08-13 实测） | — |
| ~~快照反例校验~~ ✅ | **已补**：SnapshotMapperTests.cs 有 9 个 ValidateSnapshotRejects* 测试（L215-274）+ 34 个 [Test*] 属性（2026-08-13 实测） | — |
| **C# JSON 卡牌加载器** | **真缺口**：src/ 下无任何 Loader/Json/Repository/Catalog 文件，无 System.Text.Json 引用。需按 cards.schema.json 加载 91 卡 + 4 预构筑卡组，fail-closed | Codex Batch 6A |
| Unity 6 LTS | 未安装 → 6.x/7.x/8.1 全阻塞。磁盘已释放至 113GB 空闲（2026-08-13 00:08 实测，人类卸载游戏） | 人类决策 |
| 自动接力 | 框架在但 bot 未配置 → 无 24h 接力 | Codex 接力任务 |

---

## 变更历史

| 日期 | 变更 |
|------|------|
| 2026-08-12 | 初始基线建立（264/264、38/38、91/91、88.53%、45%） |
| 2026-08-13 | 缺口表复核：CardDto/LegalActionGenerator/快照反例校验已实测补齐（Batch 3-4），真缺口= C# JSON loader + Unity；磁盘 113GB 空闲 |
