# Coverage Baseline — Batch 5

生成时间：2026-08-12。命令：

```powershell
dotnet test src\Engine\Tests\DominionWars.Engine.Tests.csproj -c Release --no-restore /p:CollectCoverage=true /p:CoverletOutput=TestResults\coverage\coverage /p:CoverletOutputFormat="json%2ccobertura"
```

## 当前结果

| 模块 | 行覆盖率 | 分支覆盖率 | 方法覆盖率 |
|---|---:|---:|---:|
| DominionWars.Adapters | 95.30% | 85.71% | 98.98% |
| DominionWars.Engine | 87.10% | 74.68% | 94.52% |
| **Total** | **88.53%** | **76.15%** | **95.71%** |

本阶段只建立基线，不设置覆盖率阈值门禁。测试结果为 **264/264 通过，0 失败，0 跳过**。

原始产物：

- `src/Engine/Tests/TestResults/coverage/coverage.json`
- `src/Engine/Tests/TestResults/coverage/coverage.cobertura.xml`

## 后续

下一次覆盖率更新应比较 Total 行/分支/方法三项，并按 Engine、Adapters、Effects 继续拆分；本文件的历史趋势暂不虚构数据。
