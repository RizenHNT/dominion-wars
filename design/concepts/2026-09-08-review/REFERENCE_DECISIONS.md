# 界面主体优先：参考与制作范围

更新：2026-09-08。最新状态：整体方向已认可，见 [APPROVED_DIRECTION.md](APPROVED_DIRECTION.md)。下文保留前期研究记录；与最新认可方向冲突时，以该文件为准。尚不是正式视觉系统。

## 当前授权与顺序

采用用户提供的 0–8 阶段：Design Constraints → Visual Research → Art Direction → Style Frame → Motion Study → Visual System → Vertical Slice → Production → Polish / QA。

当前继续阶段 0–3。已有局部原型不代表阶段通过。保留刚生成的四张卡图作为统一占位，不再扩展卡图生产。首先完成棋盘、卡框、战斗布局、主菜单、设置的整体关系。最终方向由用户通过；之后才向 Luna / max 交付批量资源制作规格。

阶段 0 的长文、隐藏信息、目标识别和缩放检查从样稿阶段开始；正式实机可玩性与性能结论留到阶段 6，不能由静态图片代替。

## 五条候选美术原则

1. 中国 90 年代公共空间和印刷品提供材质：水磨石、浅色墙面、绿色墙裙、米白设备、练习册与连续纸。
2. 构成主义组织画面：网格、粗黑标题、大数字、非对称色块；文字正文与主要操作保持稳定方向。
3. 战斗以卡牌和场区为重心：材质的颗粒、明暗和透视服从可读性，不把教室布景当成交互层。
4. 共用红、蓝、黄、绿、纸白和墨黑；每屏控制主次色，不让所有色彩同时争夺注意力。颜色与文字/形状共同表达状态。
5. 动态通过色块、标签和信息条的位移与遮罩建立冲击；纹理留在表面，避免全屏持续噪声、扫描线与抖动。

## 首轮样稿范围

两到三个方向先比较同一战斗局面的整体构图，区分空间和组件处理，不能只换配色。收敛后组成五张关键画面：战斗、主菜单、设置、卡牌详情、结算。卡框同时以战场尺寸、手牌尺寸、详情尺寸检验。共用棋盘、卡框、卡背、卡图、状态覆盖层、菜单组件分别定义。

样稿阶段制作的是共用资源的设计样本，不提前冻结完整资产包。去掉卡图后，主体仍应有明确的中国 90 年代识别性。

## 外部素材筛选（2026-09-08 页面核对）

以下仅核对页面，不代表已下载、检查包内文件或导入正式游戏。

| 来源 | 页面授权/内容 | 设计决策 |
|---|---|---|
| [Poly Haven Terrazzo Tiles](https://polyhaven.com/a/terrazzo_tiles) | CC0；含漫反射、法线、粗糙度、置换等 | 优先试样；统一颗粒尺度、色温和对比度后比较棋盘效果 |
| [Paper Textures](https://opengameart.org/content/paper-textures-seamless) | CC0；13 张，通常 1500×1500 | 优先试样，用于卡框底纸、设置和说明面板 |
| [PSX Paper](https://opengameart.org/content/psx-paper-textures-32x32px) | CC0；32px 纹理与 256px atlas | 局部备选；不据此把整个项目转为像素风 |
| [Base Material Pack](https://opengameart.org/content/base-material-texture-pack) | CC0；128 种材质各三种尺寸，共 384 张 | 墙面和金属备选；并非 384 种独立材质 |
| [Kenney UI Pack](https://kenney.nl/assets/ui-pack) | CC0；430 个文件 | 功能状态参考，最终形状重新按项目设计 |
| [Mechanized Magic](https://opengameart.org/content/mechanized-magic-ultimate-ui-pack) | 免费包 CC0；页面明确描述 PNG 64/256px | 次要参考；尚不确认免费包有 SVG，不把付费完整版算入可用资源 |
| [Tabler Icons](https://tabler.io/icons) | 官方图标库；具体入库时保存对应 MIT 许可 | 搜索、设置等通用图标候选；牌库/墓地/云端需验证游戏语义 |
| [Computer Terminal](https://opengameart.org/content/computer-terminal) | CC0；blend 模型及纹理 | 主菜单环境候选；先比较造型是否贴合学校机房 |
| [Fusion Pixel Font](https://github.com/TakWolf/fusion-pixel-font) | 字体 OFL 1.1，构建程序 MIT；有 zh_hans | 仅编号、短状态或局部设备显示候选；正文保持清晰中文字体 |

正式入库记录来源、作者、下载版本/日期、原许可证、文件哈希、修改内容、使用位置。字体与图标随发行保存相应许可。用户提供的参考图片用于方向研究，不自动作为可分发资源。

## 交付给后续素材代理的条件

用户通过 Style Frame 后，提供已批准画面、尺寸与安全区、分层清单、色彩与纹理样本、组件状态、命名、导出格式和验收对照。素材代理可执行制作，不自行改动布局、美术方向或规则信息。
