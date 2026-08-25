# Known Issues — Content Pipeline 与 Card Editor（2026-08-25）

Status: **OPEN — 新 MVP 周期基线**

本清单记录内容管线、卡图解耦和编辑器的已验证问题。它不改变游戏规则、卡牌数值或视觉最终方向；未完成项不能写成已实现功能。

## 摘要

- P0：0（本审计未发现会破坏当前 Unity U-00～U-03 已完成门禁的内容管线故障）。
- P1：当前卡图接入、Unity 内容打包、编辑器保存安全和机制注册表均未达到 MVP。
- P2/P3：设计资产、旧 Java/Web 路径和未来音频/VFX接口需要后续迁移。
- 540 卡包仍是候选设计源，未导入，不属于本问题清单的修复输入。

## KI-CONTENT-001 — 卡图覆盖不足

- 严重度：P1。
- 事实：data/art/ 目前只有 4 个 PNG；卡图报告显示 91 张卡、7 条统领记录、4 个 fallback、87 张缺图、0 个 artId 字段。
- 影响：当前只能依赖程序化/阵营占位图，不能由非程序员独立管理每张卡的图像引用。
- 位置：data/art/、scripts/report-card-art-map.ps1、Java/Web 直接路径读取。
- 计划：建立 data/content/inbox/card-art、library、manifest 和 artId 兼容解析。

## KI-CONTENT-002 — Unity 构建不打包内容资产

- 严重度：P1。
- 事实：RuntimeDataStreamingBuildPreprocessor.cs 当前只复制 data/cards 和 data/decks，不复制卡图、牌桌、卡背、王城、skin、音频或 VFX。
- 影响：工作目录里存在的图片不能证明 Windows Player 中可用；运行时没有统一 asset resolver。
- 计划：新增独立的 Assets/StreamingAssets/content 生成目录、manifest 校验、引用资产复制和 ownership marker。

## KI-CONTENT-003 — artId、Schema、CardCatalog 和 DTO 不一致

- 严重度：P1。
- 事实：C# CardDefinition 和 ContractDtos.CardDto 已有 ArtId，但生产卡 JSON 没有 artId；cards.schema.json 尚未定义它；CardCatalog 没有完整映射。
- 影响：编辑器无法安全生成卡图引用，UI 只能依赖 cardId 或占位逻辑。
- 计划：在获批的兼容 Schema 迁移中增加 artId，保留缺省值和旧 alias fallback；不在本周期批量改写 91 张卡。

## KI-CONTENT-004 — 旧 Card Editor 直接覆盖文件

- 严重度：P1。
- 事实：src/main/java/com/dominionwars/ui/editor/EditorWindow.java 的保存流程直接写文件；素材选择会复制到旧 art 目录；没有事务、备份、统一 Schema 校验或 manifest 更新。
- 影响：保存中断、重复 ID 或未知字段可能造成部分写入；素材和卡牌引用不能可靠回滚。
- 计划：引入共享的 CardDocumentStore，执行临时文件、flush、备份、原子替换和 manifest 二阶段更新。

## KI-CONTENT-005 — 机制和字段注册表重复

- 严重度：P1。
- 事实：动作/目标/关键词/胜利条件分别在 Schema、CardCatalog、EffectNames、EffectDispatcher、目标校验器、Java 模型和旧编辑器中维护。
- 影响：新机制需要多处手写复制，容易出现“编辑器可选但引擎不支持”或“引擎支持但编辑器不可选”。
- 计划：由 C# engine descriptor + Schema 生成 registry；CI 交叉校验 handler、descriptor 和 Schema；编辑器只读取生成结果。

## KI-CONTENT-006 — 运行时仍有直接路径拼接

- 严重度：P1。
- 事实：CardArt.java、CardPanel.java、GameSession.java 和 Web 层仍有 data/art/<cardId> 形式的读取。
- 影响：即使引入新 manifest，也可能出现不同前端解析结果不一致。
- 计划：统一 ContentResolver/manifest alias；旧目录只作为迁移 fallback，不再新增直接路径逻辑。

## KI-CONTENT-007 — 320 项设计资产未接入运行时

- 严重度：P2。
- 事实：design/runtime-kit-v1.30/manifests/ASSET_MANIFEST.csv 有 320 项资产和逻辑 ID，但当前 Unity 构建和运行时没有读取它。
- 影响：设计 kit 的牌桌、框、图标不能作为可替换 content pack 使用。
- 计划：从 v1.30 清单提取兼容输入，生成 v1.31 typed content manifest；不把历史 manifest 直接当作运行时权威。

## KI-CONTENT-008 — 540 卡包不能直接迁移

- 严重度：P1（流程风险）。
- 事实：docs/卡牌设计包_2026-08-15/bundle_v2.json 包含 540 张卡和 4 个统领，与当前 91 张生产卡只有部分 ID 重叠，字段和机制也存在候选设计差异。
- 影响：直接复制会覆盖或混入生产数据，并可能引入尚未实现的机制。
- 计划：另开数据迁移目标，先做 ID/字段/规则映射、人工确认和完整回归；本 MVP 明确禁止导入。

## KI-CONTENT-009 — Skin 只有设计契约，没有 Unity 解析链

- 严重度：P2。
- 事实：已有 v1.30 skin/theme 设计契约，但 Unity RuntimeBootstrap 没有加载 content manifest、skin 或 theme。
- 影响：当前牌桌、卡背、王城和 UI 图标仍是占位或代码内表现，不能通过 skinId 组合替换。
- 计划：MVP 实现默认 skin + 第二个测试 skin，并验证 fallback 和构建打包。

## KI-CONTENT-010 — 音频和 VFX 尚未实现

- 严重度：P3（已知非目标）。
- 事实：当前没有统一 audio cue/VFX asset resolver，也没有实际播放/粒子系统接入。
- 影响：本周期不能声称音乐、SFX 或 VFX 已解耦可用。
- 计划：MVP 只保留 typed manifest、逻辑 ID 和 no-op/fallback 接口；实际运行时接入另开周期。

## 本周期的处理边界

- 只实现已批准的内容管线和 Card Editor MVP，不修改规则、平衡、卡牌效果结算或最终美术方向。
- 不删除 data/art、旧编辑器或历史设计资产；迁移必须可恢复。
- 所有实现必须补充 Schema、manifest、编辑器事务、Unity EditMode/PlayMode/构建和回归证据。
- 在对应证据完成前，状态保持 OPEN，不得把设计规格写成实现完成。

