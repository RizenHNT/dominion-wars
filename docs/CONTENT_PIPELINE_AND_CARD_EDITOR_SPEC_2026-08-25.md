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

