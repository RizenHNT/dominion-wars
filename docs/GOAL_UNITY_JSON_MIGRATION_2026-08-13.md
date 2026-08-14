# Codex goal — Unity 编译收敛：Data 层 System.Text.Json → Newtonsoft.Json

> 状态: 🔴 待派 · 2026-08-13 19:55 · PL (DeepSeek v4)
> 背景: 人类实测 Unity Console 从 663 警告/105 错误 → **169 警告/27 错误**。nunit 冲突已由 PL 临时修复（Directory.Build.props），剩 27 错误 = `src/Data` 用了 `System.Text.Json`（Unity netstandard2.1 profile 不含该库）。
> 门禁: **人类已批准方案 B**（Data 层改用 Newtonsoft.Json，推翻 GOAL_6A 决策 3 的 STJ 选择，理由 = 新事实：Unity 无 STJ）。改动为生产代码，归 Codex 执行。

---

## 全局规则

1. **只 commit 自己本阶段的文件**：用 `git add <精确路径>`，**禁止** `git add -A` / `git add .`（主仓库有大量他人未提交项）。
2. **每阶段结束**：跑一次 `dotnet test --nologo --no-restore`（仓库根目录），确认**只增不减、全绿**（基线当前 344/344）。
3. **不 push**（无 push unless 人类明示）。
4. **单文件 ≤500 行**（PROPOSAL §5.9 硬约束 #9）。
5. **Unity Console 实机验证需人工打开工程**——你无法无头跑（Licensing 挂起）。只能改代码 + 跑 .NET 回归 + 在 AI_MAILBOX 报告待人工复验。
6. **不改 `src/Engine/Tests/` 里那 4 个用 STJ 的测试**（ContractBoundary/Serialization/Stress/EffectsSpecContract）——它们是 .NET 专用、不在 Unity 编译范围，继续用 STJ 无碍。

---

## P0 — 追认 nunit 冲突修复（已生效，~15min）

**背景**：PL 已临时完成以下修复（人类实测 105→27 错误生效），需你追认并提交：
- 新建根 [Directory.Build.props](/C:/Users/USER/Documents/dominion-wars-win64/Directory.Build.props)：重定向全部 .NET 构建输出到 `build-output\$(MSBuildProjectName)\`，使 `src/` 下永不再出现 bin/obj（Unity 会把包目录内所有 .dll 当预编译程序集，Tests 的 nunit.framework.dll 3.14 与内置 ext.nunit 3.5 冲突即源于此）。
- [.gitignore](/C:/Users/USER/Documents/dominion-wars-win64/.gitignore) L12 加 `build-output/`。
- 已删除 `src/` 下全部 bin/obj/TestResults（10 目录）+ 9 个孤儿 .meta。

**任务**：
1. 独立复核 `Directory.Build.props` 生效：`dotnet build` 后确认 `build-output/` 下产出，`src/` 下无新增 bin/obj。
2. 提交这两个文件（`Directory.Build.props` + `.gitignore`），commit message 如 `build: redirect .NET output to build-output/ to fix Unity nunit conflict`。
3. 确认 `src/` 下 0 个 .dll、0 个 bin/obj（排除 `src/main/java/`）。

**验收**：
- [ ] `dotnet test --nologo --no-restore` 344/344 全绿
- [ ] `git log -1` 显示 P0 commit，只含上述 2 个文件
- [ ] `Get-ChildItem src -Recurse -Filter *.dll`（排除 `src/main/java`）为空

---

## P1 — Data 层迁移 Newtonsoft.Json（核心交付，2-3h）

**背景**：`src/Data/` 生产代码（仅 2 文件）用 `System.Text.Json`，.NET 侧有 NuGet 包可编译，但 **Unity netstandard2.1 profile 不含 System.Text.Json**（已查 `Editor\Data\NetStandard\ref\2.1.0\` 确认无此 DLL）。Unity 工程只有 EditMode 测试引用 Data（`Assets/DominionWars.Tests.EditMode.asmdef`），运行时代码（Adapters）不碰 Data。

**任务**：
1. [src/Data/DominionWars.Data.csproj](/C:/Users/USER/Documents/dominion-wars-win64/src/Data/DominionWars.Data.csproj)：`System.Text.Json` 8.0.5 → `Newtonsoft.Json` 13.0.3。
2. [src/Data/CardCatalog.cs](/C:/Users/USER/Documents/dominion-wars-win64/src/Data/CardCatalog.cs)：26 处 STJ → Newtonsoft（`JsonDocument.Parse`→`JObject.Parse`、`JsonElement`→`JToken`、`JsonValueKind`→`JTokenType`、`TryGetProperty`→`SelectToken`/索引、`GetString/GetInt32/GetBoolean`→`Value<T>()`/显式转换、`JsonException`→`JsonReaderException`）。**公开 API 签名、fail-closed 语义、报错消息格式必须完全保持**（DataLoaderTests 依赖）。
3. [src/Data/DeckLoader.cs](/C:/Users/USER/Documents/dominion-wars-win64/src/Data/DeckLoader.cs)：同上迁移（5 处）。
4. [unity/DominionWars.Unity/Packages/manifest.json](/C:/Users/USER/Documents/dominion-wars-win64/unity/DominionWars.Unity/Packages/manifest.json)：加 `"com.unity.nuget.newtonsoft-json": "3.2.1"`。
5. 确认 `src/Data/DominionWars.Data.asmdef` 无需改动（newtonsoft-json 包 autoReferenced，Data asmdef 应自动可见；若实测需显式引用则补 `overrideReferences`/`precompiledReferences` 并说明）。
6. 迁移后跑 `dotnet test`：确认 DataLoaderTests 全绿（它们走公开 API，库切换对它们透明）。

**验收**：
- [ ] `grep -r "System.Text.Json" src/Data/` 无命中
- [ ] `dotnet test --nologo --no-restore` 344/344 全绿（不增不减基线）
- [ ] CardCatalog.cs / DeckLoader.cs 各 ≤500 行，公开 API 未变
- [ ] manifest.json 含 `com.unity.nuget.newtonsoft-json`
- [ ] AI_MAILBOX 报告：迁移 commit SHA + 回归结果 + **明确"待人类重开 Unity 验证 27 红标清零"**

---

## P2 — 交接收尾（~15min）

**任务**：
1. AI_MAILBOX 追加 P0+P1 完整报告（时间戳 + 做了什么 + 验收结果 + 阻塞）。
2. 报告里明确剩余唯一待人工项：**人类重开 Unity 工程**，确认 Console 27 红标清零（预期全绿）。

**验收**：
- [ ] AI_MAILBOX 有 `[Codex→PL] Unity JSON 迁移完成` 报告段
- [ ] 无未 commit 的 src/Data、Directory.Build.props、.gitignore、manifest.json 变更
