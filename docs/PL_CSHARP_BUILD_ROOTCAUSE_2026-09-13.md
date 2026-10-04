# C# 构建失败：根因与正确修法

**日期**：2026-09-13
**作者**：DeepSeek V4 Flash（临时 PL）
**授权**：owner 2026-09-13 授权"只修改 Program Files 的东西"来修构建；并指示"**SDK 的错误你最好不要绕过，而是搞清楚原因，用正确方式补**"。
**状态**：**根因已查清（含 file:line 证据）；② 未修复，需要一次管理员提权操作。**

---

## 0. 结论速览

| # | 问题 | 状态 |
|---|---|---|
| ① | **NuGet 漏洞审计需要联网**，本机联网被拦 → `NU1900` → 项目"警告当错误" → 还原失败 | **已解决，且是正确配置**（见 §1） |
| ② | **msbuild 的 `DefaultSdkResolver` 对两个"运行时动态 SDK"报硬错误 `MSB4276`** | **根因已确认；正确修法需要管理员**（见 §2） |

**owner 的判断是对的**：这台机器**联网被阻断**（不只是 nuget.org，`dotnet.microsoft.com`、阿里云、腾讯云镜像**全部不可达**，且 nuget.org 报的是 **SSL 握手失败**，属拦截型断网）。

---

## 1. 问题①：NuGet 漏洞审计（已正确解决）

### 证据
```
src\Data\DominionWars.Data.csproj : error NU1900: 错误形式的警告:
    获取包漏洞数据时出错: 无法加载源 https://api.nuget.org/v3/index.json 的服务索引。
```
`dotnet restore src/Data/DominionWars.Data.csproj` → **exit 1**
`dotnet restore src/Data/DominionWars.Data.csproj -p:NuGetAudit=false` → **exit 0**

### 为什么这不是"绕过"
- **`NuGetAudit` 自 .NET 8 起默认开启**，它的作用是**联网下载"包漏洞数据库"**。
- **离线环境本就应该关掉它。** 这与"没网时关掉自动更新"同类，是正确配置而非规避。
- 关掉它**不影响依赖还原正确性**：包版本、锁定、缓存全部照旧；只是不再做漏洞扫描。
- 本机所需的全部包**已在缓存里**（`%USERPROFILE%\.nuget\packages`，22 个包，
  含 `nunit 3.14.0`、`newtonsoft.json 13.0.3`、`microsoft.net.test.sdk 17.10.0`、`nunit3testadapter 4.5.0`），
  **所以离线还原是可行的** —— 前提是不去联网做审计。

### 正确落地位置
`Directory.Build.props`（仓库已有该文件，是所有项目的共同属性入口）加一行：
```xml
<NuGetAudit>false</NuGetAudit>
```
**已用 `-p:CustomBeforeMicrosoftCommonProps` 指向含该属性的临时 props 验证过：exit 0。**
⚠ **尚未写入 `Directory.Build.props`** —— 那是构建配置，属于实现范围，我按流程先报给 owner/实现方。

---

## 2. 问题②：`MSB4276`（根因已确认，未修复）

### 2.1 触发链（每一环都有证据）

**第 1 环 —— SDK 名字是 .NET SDK 自己写死的：**
`C:\Program Files\dotnet\sdk\8.0.425\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.Sdk.ImportWorkloads.props:14`
```xml
<Import Project="AutoImport.props" Sdk="Microsoft.NET.SDK.WorkloadAutoImportPropsLocator" />
```
同类还有 `Microsoft.NET.SDK.WorkloadManifestTargetsLocator`。

**第 2 环 —— 这两个 SDK 本该由 workload 解析器在运行时处理，而不是磁盘上的目录：**
diag 日志原文：
```
SDK"Microsoft.NET.SDK.WorkloadAutoImportPropsLocator"已由
   "Microsoft.DotNet.MSBuildWorkloadSdkResolver"解析程序成功解析为位置"null"和版本"null"
SDK"Microsoft.NET.SDK.WorkloadManifestTargetsLocator"已由
   "Microsoft.DotNet.MSBuildWorkloadSdkResolver"解析程序成功解析为
   位置"C:\Program Files\dotnet\sdk-manifests\8.0.100\microsoft.net.sdk.android\34.0.43"
```
⇒ **workload 解析器成功了**（返回 "null" 意为"无 workload props 可导入"，正是无 workload 时该有的结果）。
返回 `null` 而不是目录，说明**它们是动态解析的虚拟 SDK，本来就不以文件形式存在**。

**第 3 环 —— 但 `DefaultSdkResolver` 抢先去磁盘找目录、找不到就发硬错误：**
```
错误: MSB4276: 默认 SDK 解析程序解析 SDK"Microsoft.NET.SDK.WorkloadAutoImportPropsLocator"失败，
     因为目录"...\Sdks\Microsoft.NET.SDK.WorkloadAutoImportPropsLocator\Sdk"不存在。
                                                                  ↑ 注意：它找的是 ...\Sdk（没有点）
```
**第 4 环 —— 这个错误发生在 `Restore` 的工程引用遍历里，导致整条链失败：**
```
已完成在项目"DominionWars.Adapters.csproj"中生成目标"_GetAllRestoreProjectPathItems"的操作 - 失败
已完成在项目"DominionWars.Adapters.csproj"中生成目标"_GenerateRestoreProjectPathWalk"的操作 - 失败
```

### 2.2 关键判据：`Sdks\` 目录是完整官方集合，**只缺这两个**

存在：`Microsoft.NET.Sdk`、`Microsoft.NET.Sdk.Web`、`Microsoft.NET.Sdk.Razor`、
`Microsoft.NET.Sdk.Publish`、`Microsoft.NET.Sdk.BlazorWebAssembly`、`Microsoft.NET.Sdk.WindowsDesktop`、
`Microsoft.NET.Sdk.StaticWebAssets`、`Microsoft.NET.Sdk.Worker`、
`Microsoft.NET.Sdk.Web.ProjectSystem`、`Microsoft.NET.Sdk.WebAssembly`、
`FSharp.NET.Sdk`、`Microsoft.Docker.Sdk`、`NuGet.Build.Tasks.Pack`、4 个 SourceLink、`Microsoft.Build.Tasks.Git`

**这与"安装被破坏、随机丢了两个目录"不符**；更像是**它们本就不以文件形式存在**。

### 2.3 为什么"补空目录"是错的方向（我先前判断错了，已撤回）

- 若那是**正确的官方文件**，放我手写的空壳就是**用假文件冒充官方 SDK** —— owner 明确否决。
- 而且空壳与真实行为不同：真实 locator 会**导入已安装 workload 的 props/targets**，
  空壳什么都不导入。**在有 workload 的机器上会静默丢掉 workload 支持。**
- 本机确实**装了 workload 清单**（`sdk-manifests\8.0.100\` 下有 android/ios/maui/macos/aspire/emscripten/mono-toolchain），
  所以这个差异是**真实风险**，不是理论风险。

### 2.4 正确修法（两条路，都需要管理员或网络）

**路 A（可离线，需管理员）—— 用官方安装包修复安装：**
官方安装器**已缓存在本机**：
```
C:\ProgramData\Package Cache\{ab39b1d0-4847-4c0d-a1c1-b2be697f2b76}\dotnet-sdk-8.0.425-win-x64.exe
```
另有其内部 MSI：`{B95C0F96-...}v32.15.21213\dotnet-sdk-internal-8.0.425-win-x64.msi`

以**管理员**运行：
```
"C:\ProgramData\Package Cache\{ab39b1d0-4847-4c0d-a1c1-b2be697f2b76}\dotnet-sdk-8.0.425-win-x64.exe" /repair
```
**这条命令同时回答一个决定性问题**：
- 若修复后那两个目录**出现** → 说明原安装确实损坏，问题解决；
- 若修复后**仍不存在** → **证明**它们是动态虚拟 SDK，官方安装本就没有，
  那么 `MSB4276` 属于"这个 SDK 版本的已知噪声"，真正修法是**升级到后续 SDK 补丁版**（需要联网）。

**路 B（需网络）—— 安装更新的 .NET 8 SDK 补丁版。**
本机只有 `8.0.425` 一个 SDK；`dotnet.microsoft.com`、VS 下载域、阿里云/腾讯云镜像**全部不可达**，
所以这条路要等网络恢复。

### 2.5 为什么不能用 msbuild 属性把 MSB4276 降级（已逐一实测失败）

| 尝试 | 结果 |
|---|---|
| `-p:MSBuildTreatWarningsAsErrors=false` | exit 1 |
| `-p:TreatWarningsAsErrors=false` | exit 1 |
| `-p:MSBuildWarningsAsMessages=MSB4276` | exit 1 |
| `-p:MSBuildWarningsAsErrors=` | exit 1 |
| `MSBuildEnableWorkloadResolver=false`（环境变量） | exit 1 |

⇒ **`MSB4276` 由 SDK 解析器直接发出、不走普通警告机制，命令行无法降级。**

### 2.6 沙箱与权限的实情（我为什么停在这里）

- DSH 沙箱**已按 owner 授权放开**（`danger-full-access`），但 **Windows 本身拒绝写入**：
  ```
  Access to the path 'C:\Program Files\dotnet\sdk\8.0.425\Sdks\Microsoft.NET.SDK...' is denied.
  ```
- 当前进程**非管理员**（`IsInRole(Administrator)` = **False**，用户 `LAPTOP-T1VGKEJN\USER`）。
- `Sdks` 目录 ACL 的 Owner 是 **`NT AUTHORITY\SYSTEM`**，可写者仅
  `TrustedInstaller` / `SYSTEM` / `BUILTIN\Administrators` —— **普通用户无权写入**。
- 尝试 `Start-Process -Verb RunAs` 提权 → **失败（无 UAC 应答）**。

---

## 3. 当前可用的验证能力（不影响推进卡表设计）

| 引擎 | 状态 | 说明 |
|---|---|---|
| **Java** | ✅ **绿 59/59** | 与 C# **读同一批 `data/cards/*.json`**（`ConsoleMain.java:42` → `CardLibrary.load(Path.of("data/cards"))`）⇒ **卡牌数据改动由 Java 侧完整覆盖** |
| **C#** | ⚠ **无法重新构建** | 既有测试 DLL 仍是 09/12 23:46 的版本；`build-output/pl-p0/runner` 可运行，但全量套件超过超时（>10 分钟） |

**因此**：卡表数据、schema 校验、双引擎解析一致性里的**数据侧**可推进；
**新引擎字段（如 `SEALED_MINION` 目标、新条件词条）的 C# 侧验证必须等 ② 修好。**
我不会把"Java 绿"当作"C# 已验证"。

---

## 4. 需要 owner 的一个动作

**以管理员身份**（开始菜单搜 PowerShell → 右键"以管理员身份运行"）执行：

```powershell
& "C:\ProgramData\Package Cache\{ab39b1d0-4847-4c0d-a1c1-b2be697f2b76}\dotnet-sdk-8.0.425-win-x64.exe" /repair
```

`/repair` 是**官方安装器自带的修复开关**，只重装 SDK 自己的文件，**不会碰你的项目、数据或 Git**。

跑完后告诉我，我会：
1. 立刻检查那两个目录是否出现（**这条命令同时给出决定性证据**）；
2. 把 `NuGetAudit=false` 落到 `Directory.Build.props`，验证 `dotnet build` 全绿；
3. 跑 885 项 C# 回归 + 新卡表的平衡测试。

**如果 `/repair` 后目录仍不存在**，我会明确报告"这是该 SDK 版本的噪声，正解是升级 SDK 补丁版"，
而**不会**再提议放空壳。
