# Content Pipeline 与 Card Editor MVP 规格（2026-08-25）

Status: **APPROVED / READY → IMPLEMENTING**

这份文档固化已批准的 MVP 边界，供后续实现和验收使用。本次只固化文档；生产代码、卡牌规则、平衡、最终美术和 540 卡包仍未因此变更或宣称完成。

## 1. 目标和权威边界

目标是把卡牌数据、卡图、牌桌皮肤、卡背、王城、统领、阵营框、UI 图标以及未来的音乐/SFX/VFX 变成可独立替换的内容资源。非程序员只需把素材放进明确的 inbox，并在编辑器中选择字段、费用、机制和素材；程序员实现并注册新机制后，编辑器自动从注册表显示它。

权威边界：

- C:\Users\USER\Documents\dominion-wars-win64\data\cards\*.json 仍是当前 91 张生产卡牌数据。
- C:\Users\USER\Documents\dominion-wars-win64\data\schema\cards.schema.json 是当前卡牌 JSON 结构的校验权威。
- Unity 继续以 C# Engine 和 Runtime Contract 1.31 为运行时边界；Java 只作旧行为/兼容参考。
- UI 和编辑器不得复制合法性、目标、阶段、胜负或效果结算规则。
- 资产 manifest 只描述资源、角色、版本和 fallback，不描述游戏规则。
- C:\Users\USER\Documents\dominion-wars-win64\docs\卡牌设计包_2026-08-15\bundle_v2.json 是候选设计包，不是运行时输入；本规格不授权导入。

## 2. 已验证的当前基线

- 当前有 91 张卡、4 个卡组；Unity 构建预处理目前只打包 cards/decks。
- data\art\ 目前只有 4 个 PNG。卡图报告为 91 张卡、7 条统领记录、4 个 fallback、87 张缺图、0 个 artId 字段。
- CardDefinition 已有 ArtId、Cost、Rarity 属性，但当前 CardCatalog 没有完整映射；Schema 也尚未定义全部这些字段。
- Java EditorWindow 的字段、效果组和保存逻辑是手写的，直接覆盖文件，没有统一 registry、原子保存或备份。
- design\runtime-kit-v1.30\manifests\ASSET_MANIFEST.csv 的 320 项是设计资产清单，不等于运行时 content library；v1.31 是当前契约版本。

## 3. 稳定目录

新增的源内容目录建议为：

~~~text
data/content/
  inbox/
    card-art/
    boards/
    card-backs/
    castles/
    leaders/
    faction-frames/
    ui/icons/
    audio/music/
    audio/sfx/
    vfx/

  library/
    card-art/
    boards/
    card-backs/
    castles/
    leaders/
    faction-frames/
    ui/icons/
    audio/
    vfx/

  manifests/
    content.manifest.json
    skins/<skinId>.json
    themes/<themeId>.json
~~~

data\art\ 保留为 legacy 兼容目录；迁移完成前不得删除或强制搬迁。Assets\StreamingAssets\content\ 是 Unity 构建生成目录，不是人工投放目录。

## 4. ID 和 manifest 契约

### 4.1 ID

- cardId：沿用现有卡牌 ID 及 ^[a-z][a-z0-9_]*$ 规则；发布后不可重命名。
- assetId：全局稳定的逻辑 ID，同样使用 lower_snake_case，使用类别前缀避免碰撞，例如 card_art_flame_leader、board_default、card_back_default、ui_icon_draw。
- artId：卡牌引用的 assetId，其 manifest kind 必须是 card_art。它是逻辑引用，不包含路径、扩展名或 Unity 资源路径。
- 已发布资产的改名必须创建新 ID，并由 alias manifest 保留旧引用；不能静默覆盖。

### 4.2 Manifest

运行时只读取 manifest 中的相对路径：

~~~json
{
  "manifestVersion": 1,
  "packId": "base",
  "assets": [
    {
      "assetId": "card_art_flame_leader",
      "kind": "card_art",
      "path": "library/card-art/flame_leader.png",
      "sha256": "...",
      "optional": true,
      "fallbackAssetId": "card_art_faction_flame_default"
    }
  ]
}
~~~

正式实现还应记录 format、尺寸、透明度、文件大小、来源/授权状态和可选 variant。路径必须是 manifest 根目录下的相对路径，拒绝绝对路径、..、重复 ID、重复路径、未知 kind 和 hash 不一致。

卡图解析顺序：

~~~text
card.artId
→ 当前 skin 的 cardArtwork 覆盖
→ 默认 skin 的角色资源
→ 阵营/类型默认资源
→ 程序化或通用占位资源
~~~

普通可选图片缺失时显示 fallback 和可见警告；manifest 损坏、必需公共资源缺失或安全校验失败时必须 fail-closed。

## 5. Skin、Theme 和未来媒体

Skin 只组合表现资源，不改变组件 ID、布局、合法行动或规则。一个 skin 可以指定：

- themeId、牌桌/board、cardBackId、王城和统领框；
- 阵营框、UI 图标、卡图覆盖；
- 音频 cue 和 VFX cue 的逻辑映射。

Theme 负责颜色、字体、间距、动效语义和无障碍 token；不直接携带游戏规则。

MVP 只建立 audio/vfx 的 typed manifest 入口和 no-op/fallback 约定，不实现音乐播放、SFX 混音、粒子系统、Addressables 或远程内容包。未来接口使用 audio_sfx_attack、audio_music_battle、vfx_damage 一类逻辑 ID，不能把 Unity 路径写进卡牌或运行时契约。

## 6. 字段和机制注册表

编辑器不得再维护自己的字段和效果枚举。建立统一的生成注册表，至少包含：

~~~text
key
kind: field | effect | keyword | trigger | win_condition
displayKey / localizationKey
status: implemented | pending | deprecated
allowedTargets
parameterType
cardTypes / effectGroups
handlerKey
iconAssetId
~~~

注册和生成规则：

1. C# 引擎实现效果处理器，并在同一次实现中注册 descriptor。
2. EffectDispatcher 暴露已注册 handler 和 descriptor。
3. Schema 生成器从 Schema 结构和 engine descriptors 生成字段/枚举注册表。
4. 编辑器只读取生成后的注册表；条件字段来自 Schema 的 allOf/if/then。
5. CI 必须拒绝 Schema action、runtime handler、descriptor 三者不一致。
6. CardCatalog 里的手写 HashSet 和旧 Java 列表只能作为过渡兼容检查，不能继续作为新机制来源。

新机制只有在引擎实现、注册和测试都通过后，才会自动出现在编辑器；编辑器显示机制不等于机制已经实现。未注册或状态为 pending 的机制不能保存为正式运行时卡牌。

## 7. Card Editor 工作流

~~~text
新建/复制卡牌
→ Schema 生成基础字段
→ 选择已注册关键词、效果、触发器和费用字段
→ 选择或导入 artId
→ 预览
→ Schema + 语义 + 资产引用校验
→ 原子保存
→ 更新 manifest
→ 生成变更报告
~~~

编辑器必须使用逻辑 ID 选资源，不显示或保存绝对路径。删除、重命名和替换前要扫描卡组、卡牌和 skin 引用，并默认保留可恢复备份。

## 8. 原子保存、备份和 fail-closed

卡牌保存：

1. 记录原文件字节和 hash。
2. 在内存中编辑副本。
3. 校验 Schema、ID 唯一性、机制注册、条件字段和资产引用。
4. 同目录写入临时文件并 flush/close。
5. 创建时间戳 .bak，再原子替换正式文件。
6. 独立原子更新 manifest；失败时保留卡牌文件并标记 manifest stale。
7. 输出变更 ID、文件、hash 和验证结果。

素材导入：

- 从 inbox 检查格式、尺寸、透明度和重复 ID；
- 不得静默覆盖已有 library 文件；
- 复制到 library，计算 SHA-256，原子更新 manifest；
- 导入失败不得破坏旧 library 或旧 manifest。

以下情况拒绝保存或启动：JSON/manifest 损坏、重复 cardId/assetId、未知必需机制、非法路径、hash 不匹配、必需 fallback 缺失、manifest 与 library 不一致。

## 9. Unity 解析和构建

新增 ContentCatalog/ContentResolver，职责仅包括：

- 加载并校验 content manifest、skin、theme；
- 通过 assetId/角色解析已打包资源；
- 实施 fallback 和可见诊断；
- 向 UI 提供卡图、牌桌、卡背、王城、统领框、阵营框和图标引用。

RuntimeDataStreamingBuildPreprocessor 在保持现有 cards/decks 行为的基础上，把已验证的 content manifest 和引用资源复制到自有的 Assets/StreamingAssets/content。必须使用独立 ownership marker，只清理自己生成的目录；不得删除用户手工资源。

运行时禁止通过 cardId + .png 猜路径。过渡期允许 resolver 读取旧 data/art alias，但该 alias 必须由兼容 manifest 管理。

## 10. 兼容迁移

- 91 张当前卡牌继续可加载；初期不批量给所有卡 JSON 写入 artId。
- 缺少 artId 时，先查兼容 alias，再查旧 data/art/<cardId>.*，最后使用 fallback。
- machine_leader -> machine_alpha.png 等旧映射迁入 alias manifest，不再新增代码硬编码。
- 过渡期同时打包 data/cards、data/decks 和新 content 目录。
- 旧 v1.30 skin manifest 通过适配器读取；新 manifest 按 v1.31 版本化。
- 540 卡包必须另开数据迁移目标，经过 ID/字段/规则转换、人工确认和完整 QA；不得自动合并。

## 11. MVP 范围和非目标

### MVP

- 当前 91 张卡的 Schema 驱动 Card Editor；
- 已实现机制的自动字段/下拉注册；
- inbox → library → manifest 素材导入；
- 卡图、牌桌、卡背、王城、统领、阵营框和 UI 图标的解析；
- artId/assetId、hash、fallback、备份和原子保存；
- 默认 skin 与第二个测试 skin；
- Unity 构建打包和运行时解析；
- Schema、registry、manifest、路径安全和资产引用测试；
- 非程序员一页操作说明。

### 非目标

- 不改游戏规则、平衡、胜负或效果结算；
- 不导入 540 卡包；
- 不实现最终美术、完整 deck builder 或内容商店；
- 不实现 Mod、上传、下载或脚本执行；
- 不实现音乐/SFX/VFX 的实际播放或特效系统；
- 不把 Java 旧编辑器继续当作新的运行时权威。

## 12. MVP 验收

- 编辑器能创建、复制、校验、保存并重新加载一张合法新卡。
- 字段和机制来自 Schema/registry，不存在编辑器独有的动作列表。
- 注册并测试的新机制自动出现在编辑器；未注册机制不能保存。
- 自定义 artId 能正确解析，缺图时有 fallback、警告且不崩溃。
- 两个 skin 能切换牌桌、卡背、王城、框和图标。
- Unity Windows 构建使用打包 content，不依赖工作目录。
- 中断保存不会破坏原卡牌；重复 ID、非法路径、hash 错误和 manifest 损坏会拒绝启动。
- 现有 91 张卡和旧 data/art 兼容路径仍可回归加载。
- 音频/VFX 只验证 manifest 接口和 fallback，不宣称实际播放。

### 12.1 2026-08-31 实现检查点

本检查点只收口已批准的 C-00～C-06 内容边界，不改变规则、数值、生产卡牌、牌桌视觉、图片、Packages 或 ProjectSettings：

- `data/content/manifests/content.manifest.json` 已登记 board、card-back、castle、leader、faction-frame 和 UI-icon 的 default/test 稳定资源；资源均为 programmatic placeholder，不新增图片。
- `data/content/manifests/skins/default.json` 与 `test.json` 已提供稳定 skinId、themeId、角色覆盖和一条 cardArtwork 覆盖；`ContentSkinCatalog` 负责严格字段、kind、alias、draft 和重复 ID 校验。
- Unity 内容 build gate 会校验并把已验证的 `manifests/skins/*.json` 与主 manifest 一起复制到自有生成目录；运行时 resolver 可读取 skin 角色和 card-art 覆盖，audio/VFX 仍只保留 typed no-op 入口。
- Card Editor 的实际 art picker→文件预览→programmatic fallback 预览路径已有直接 EditMode 覆盖。
- 当前证据是 .NET 全量 **521/521**、runtime contract **13 valid/11 expected-invalid/0 fail**，connected Unity Editor 全量 EditMode **132/132**、PlayMode **6/6**、recompile `failed=false`，内容专项 **17/17**（ContentPipeline 9/9、RuntimeContentResolver 7/7、Card Editor picker/preview 1/1），卡 **91/91**、牌组 **4/4**、设计素材清单 **320/320**。这仍不是 Windows Player final smoke 或最终视觉验收的通过声明。
- Windows Player build/package 的独立证据仍待主代理补齐；在此之前保持 OPEN，不把编辑器门禁结果扩写为独立包运行结果。

### 12.2 本轮仍需人工/PL 决策与未宣称项

- **Restart 语义：** 尚未冻结“重启”是否创建新 session，以及 seed、match-id 是否重新生成/如何显示；当前仅保留导航意图，不宣称已经实现新对局语义。
- **权威运行时字段：** 引擎快照尚未提供 Exile、城堡屏障、统领生命的权威字段；UI 对这些值保持 `Unavailable`，不能从其他区域或视觉状态推断。
- **LeaderZone 聚合范围：** 尚未冻结 LeaderZone 是否只显示 canonical `LeaderZone`，还是聚合 Field/AmbushZone 中的活动统领；当前不扩大聚合范围。
- **独立包门禁：** Windows Player final smoke、打包内容实际读取和最终视觉验收尚未由本轮文档宣称通过。

## 13. 非程序员一页：文件放哪里

### 目前可用的旧目录

~~~text
卡牌数据：data/cards/
旧卡图：data/art/<卡牌ID>.png
~~~

不要把素材放进 Assets/StreamingAssets；它是构建生成目录。不要把 540 卡包复制进 data/cards。

### 新管线完成后

~~~text
卡图：data/content/inbox/card-art/
牌桌：data/content/inbox/boards/
卡背：data/content/inbox/card-backs/
王城：data/content/inbox/castles/
统领：data/content/inbox/leaders/
阵营框：data/content/inbox/faction-frames/
UI 图标：data/content/inbox/ui/icons/
音乐/SFX：data/content/inbox/audio/
VFX：data/content/inbox/vfx/
~~~

在编辑器中执行“导入素材”，再新建卡牌、填写 ID/名称/阵营/类型/费用、选择已有机制和 artId、预览并保存。不要填写路径，不要改 ID，不要覆盖已有 assetId。

程序员实现并注册新机制且测试通过后，该机制会自动出现在编辑器下拉框；在此之前不要用“自定义字段”绕过注册表。

## 14. 参考对齐的资产与布局约束（2026-09-29）

**状态：设计接入规范 / 不是新的视觉定稿。** 本节把已存在的参考、运行时契约和当前 uGUI 测量值整理成可维护的资产交接边界。它不把样稿中的卡名、数值、额外区域、字体、颜色、动画时长或精确构图提升为规则，也不宣称已经有可安装的 Mod/皮肤包系统。

### 14.1 证据层级：参考、当前实现和硬门禁必须分开

- `design/concepts/2026-09-08-review/APPROVED_DIRECTION.md` 与其中的 `screens/approved-motion-language-01.png` 只批准“叠片、套色、破框、水磨石竞赛台和构成主义对比”的方向。图中四格实际展示了非对称大色块、硬边阴影、卡框/卡图越界和按需展开的信息承载；该文件明确说它不是完整视觉系统、精确布局、正式素材或实机动效验收。`card-art-study*.png` 也只是四类阵营插画研究，不是可直接分发的卡包素材。
- `design/runtime-kit-v1.30/contracts/layout_contract.json` 是当前布局比例锚点：参考坐标为 `1440x900`、单位为 normalized；`1440`、`1280` 和 `960` 是参考、紧凑和观战/缩放断点。`component_registry.json` 固定组件 ID、输入和信息边界；`theme_contract.json` 固定语义 token、字体角色和“矢量资产不得内嵌本地化文字”。这些契约比样稿中的具体文案和图形更高优先级。
- 当前 Unity uGUI 仍在 `RuntimeScreenFlow`/`RuntimeBattlePanel` 运行时创建 CanvasScaler（`ScaleWithScreenSize`，当前 reference resolution 为 `1920x1080`），再把 normalized 牌桌区域映射到 Canvas；这是当前实现测量值，不是要求所有素材按 1920×1080 绘制。
- 当前 `RuntimeCardFaceView` 的可复用卡框测量值为 Full `176x248`、Compact `112x158`，外轮廓约 `0.71` 宽高比。卡框内部已有稳定的 Header/Title/Meta、Cost、Punish、Art、Rules、Stats、InteractionMarker、DisabledVeil 结构；卡图子面当前使用 `AspectRatioFitter.FitInParent` 的 `1:1` 显示槽。以上是现有实现的可复用锚点，不能被误写成已经完成的最终美术验收。
- 当前 `data/content/manifests/content.manifest.json` 中的 board、card-back、castle、leader、faction-frame、UI icon 仍主要是 programmatic placeholder；仓库现有的 9-slice、SVG、纹理和屏幕预览位于 `design/runtime-kit-v1.30/`，清单本身不是运行时内容库，不能仅凭设计资产文件存在就声称 Unity 已加载正式美术。

### 14.2 结构和比例锚点

下表的 normalized 值来自 `layout_contract.json`；“当前测量”来自现有 Unity 代码；“建议目标”只约束可维护性和可读性，仍需人类对 Style Frame/实机画面确认。

| 元素 | 稳定结构/槽位 | 当前测量或契约比例 | 建议目标与不可破坏边界 |
|---|---|---|---|
| 根画布 | Canvas → CanvasScaler → GraphicRaycaster → `RuntimeScreenShell` | Canvas 当前 1920×1080 reference；设计契约 1440×900 normalized | 素材不写死屏幕像素；所有屏幕在 1280×720、1024×768 至少保持可见、可达和不横向滚动。 |
| 敌方牌库区 | `opponentArchive` | x=.03, y=.02, w=.78, h=.10 | 只显示公开计数/牌背；不把牌库背景当交互目标，不泄漏牌序或暗牌身份。 |
| 敌方伏击区 | `opponentAmbush` | x=.10, y=.12, w=.68, h=.08 | 只显示契约允许的封存计数/牌背；图案可以换，语义和隐藏边界不能换。 |
| 敌方场区 | `opponentFront` | x=.08, y=.20, w=.72, h=.19 | 卡槽顺序与公开卡身份由 snapshot 提供；皮肤不能替换为规则推断或隐藏卡正面。 |
| 公共王城 | `royalCastle` | x=.25, y=.395, w=.40, h=.105 | 保持中央共享目标与可读 HP/损伤状态；城堡图、损伤纹理可换，胜负语义不可换。 |
| 我方场区 | `playerFront` | x=.08, y=.51, w=.72, h=.18 | 保留稳定 drop/target socket；目标 socket 至少为契约的 44 reference-px，且不能只用颜色表达合法性。 |
| 我方伏击区 | `playerAmbush` | x=.10, y=.695, w=.68, h=.075 | 公开部分可展示卡面，封存部分仍按 snapshot 的可见性渲染；不因皮肤覆盖扩大信息。 |
| 手牌 | `hand` | x=.04, y=.77, w=.76, h=.21 | 可采用 fan/overlap/zoom，但手牌永远可达；压缩布局不能把拖拽命中区变成仅一条不可用的视觉缝。 |
| 战报抽屉 | `battleReportDrawer` | x=.81, y=.10, w=.18, h=.76，`overlay-drawer` | 可折叠，不是理解合法行动的前置条件；打开时不得遮住必须操作的目标，关闭后释放 raycast。 |
| 阶段指示 | `phaseIndicator` | x=.82, y=.02, w=.16, h=.075 | 文案由 localization 渲染，背景/图形不内嵌语言；状态仍需文字或形状双重表达。 |
| 主操作 | `primaryAction` | x=.83, y=.88, w=.14, h=.08 | 一次只显示当前 snapshot 暴露的上下文操作；皮肤只换外观，不新增跳阶段或绕过引擎的按钮。 |
| 卡框 | `CardView` | Full 176×248；Compact 112×158；约 0.71 比例 | 保持稳定 anatomy 和状态（default/hover/selected/disabled/silenced/destroyed）；卡图、框、阵营色可替换，CardView ID、数值槽和信息顺序不可删除。 |
| 卡框内部 | Header/Title/Meta → Cost/Punish → Art → Rules → Stats → interaction/disabled overlay | 当前 Art 子面为 1:1 FitInParent；文字和数值槽由运行时填充 | 插画主体留在安全中心区；不要把可本地化标题、数值或规则文字烘进图；详细长文放现有 CardDetail/reader，不强塞进 Compact 卡面。 |
| 卡牌详情 | `CardDetail` folio overlay → `CardInspectScrollRect` → Viewport/Content | 当前读卡面可扩展到手牌前缘，reader 本身是 presentation-only | 详情可以覆盖次要信息，但不能覆盖拖拽起点；图片和背景 `Raycast Target=false`，滚动内容按 uGUI ScrollRect 结构维护。 |

### 14.3 皮肤/素材的硬门禁

这些是资产进入运行时前可自动检查的边界，不是要求每张图保持同一画风：

1. **稳定身份。** 运行时身份使用 `cardId`/`assetId`，遵守 `^[a-z][a-z0-9_]*$`，建议按类别加前缀（例如 `card_art_`、`board_`、`ui_icon_`）。Unity `.meta` GUID 是项目/导入层标识，不是跨包运行时身份；不得把 GUID、绝对路径或 `Assets/...` 写入卡牌 JSON。已发布资产改名必须新建 ID 并保留 alias，不能静默覆盖。
2. **资源与文字分离。** `theme_contract.json` 的 display/body/mono 字体角色和 localization key 负责文字；通用矢量/纹理/9-slice 图不得嵌入中文、日文或英文。卡图可有艺术构图，但不能把会随语言变化的卡名、费用、攻击、生命、关键词和规则文案画死。
3. **最小可读与命中。** 继续遵守 layout contract 的“无横向滚动、手牌可达、目标 socket ≥44 reference-px”；交互状态不能只靠颜色，至少有形状、边框、标签或位置变化。素材本身应在 1280×720 和 1024×768 的紧凑模式仍保留识别焦点；无法证明时标为待实机验收，不通过静态文件存在推断。
4. **图层和射线。** 建议/当前顺序为：背景与桌面 → 目标/drop 语义面 → 牌区和卡牌 → 反馈/事件/操作栏 → pause 或详情 reader。非交互的背景、卡图、反馈图层必须关闭 raycast；交互根、Button、ScrollRect viewport 和 target socket 才可拦截输入。详情打开时可临时覆盖次要信息，但不得遮住手牌拖拽起点；对手暗牌永远只能显示允许的牌背/计数。
5. **九宫格与比例。** 设计 kit 中已有 `button_stamp_9slice`、`tooltip_9slice`、`archive_panel_9slice` 等 source/raster 参考，但当前 v1.30 content manifest 没有 border/padding/slice 元数据，Unity runtime 也没有已证实的九宫格资产绑定链。当前只能把它们视为设计/导出参考；若未来接入 `Image.Type.Sliced`，每个资产必须随 manifest 版本记录四边 cap、内容 padding、最小尺寸、可拉伸轴和 1x/2x 来源，并在目标分辨率验证不变形，不能靠 renderer 猜边距。
6. **字体与授权。** `theme.json` 已有字体角色名，但当前 Unity CardFace 使用内置 `LegacyRuntime.ttf`，不能宣称任意 skin 字体已能运行时替换。未来字体资源必须记录来源、版本、许可证、CJK/日文覆盖和发行允许范围；缺字、字体加载失败或许可证不明时回退到默认字体并给诊断，不把未授权字体打入包。

### 14.4 安装、卸载、回退与包冲突：当前边界和未来最小约定

**当前已证实的能力仅限构建/运行时内容边界：** `ContentPipelineValidator` 校验 `data/content`，`RuntimeDataStreamingBuildPreprocessor` 把通过校验的 manifest 和引用文件原子交换到自有的 `Assets/StreamingAssets/content`；`ContentPipelineStaging` 使用 `.content-generated` ownership marker、旁路 stage/backup 和失败回滚，拒绝覆盖非本工具拥有的 StreamingAssets。`RuntimeContentResolver` 只从 `Application.streamingAssetsPath/content` 读 manifest、alias、hash 和 fallback；`RuntimeContentContext` 只缓存成功 resolver，失败可重试。素材导入前停留在 inbox，不能因文件存在就被打包。

**当前没有证据、不可对 Mod 作者承诺的能力：** 独立的 zip/package 发现与安装、游戏内启用/禁用、卸载按钮、优先级合并、依赖解析、跨包冲突解决、运行中热重载、下载/上传或脚本执行。成功加载的 context/resolver 没有公开的热重载入口；改包后需要新的构建/运行时上下文，不能说“刷新即可生效”。

在未来确实批准 Mod 包时，先复用本节现有目录和 manifest 内部格式，不引入大 CMS，并至少遵守以下最小规则：

- 每包使用自己的稳定 ID 命名空间；同一 `assetId`/`cardId` 的冲突默认 fail-closed，只有显式、版本化的 skin override 才能覆盖表现资源，不能覆盖组件 ID、布局语义、合法行动或规则。
- 包安装先在隔离目录校验 manifest、相对路径、hash、fallback、字体许可和所有引用，再整体交换；任何失败保留上一份可用包，不删除用户文件。卸载只允许删除该包拥有的文件，并回退到 base/default skin 或其已验证 fallback。
- `packId`、`packVersion`、依赖/冲突/优先级字段目前不在 `ContentManifest`/`ContentSkinManifest` 的运行时合同中。加入这些字段必须先版本化 schema、更新 resolver/build gate 并增加冲突/回退测试；在此之前不要让作者手写这些字段并假定生效。
- 任何未来包都必须能只靠 `assetId`、角色槽和 skin map 接入；卡牌 JSON 不得依赖包内物理路径、Unity GUID 或某个编辑器工程的本地文件名。

### 14.5 仍需人类确认的视觉选择

- 参考图的最终卡框宽高、卡图安全区、标题/正文最小字号、详情 reader 的遮挡边界和各屏幕精确比例尚未作为 Style Frame/实机结果冻结；本节的当前测量值是维护锚点，不替代 1280×720、1024×768 和 Windows Player 视觉验收。
- 是否允许正式 skin 替换字体、是否采用 9-slice 元数据、未来 Mod 包的优先级/依赖/冲突语义，都是合同扩展决策；在得到批准并有实现与测试前，保持未实现/未宣称。
- 设计 kit 的 320 项资产、参考图片和外部素材链接不自动取得发行授权。正式入库仍需来源、作者、版本/日期、许可证、文件 hash、修改记录和使用位置。
