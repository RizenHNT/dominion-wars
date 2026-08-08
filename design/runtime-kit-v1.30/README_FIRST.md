# Dominion Wars Runtime Design Kit v1.30

本包是 **引擎无关的游戏表现层交接基线**，不是 Web UI / Unity / Godot 工程。

**固定：** Screen ID、Battle Phase、GameSnapshot / LegalAction / GameEvent、Component ID、Layout、Motion 名称、Asset ID、Localization Key。

**可替换：** 单张卡图、皮肤颜色/纹理/Seal、字体映射、音效与音乐。

**换引擎时只重写：** SnapshotAdapter、ActionAdapter、EventAdapter、LayoutAdapter、MotionAdapter、AssetAdapter、LocalizationAdapter、AccessibilityAdapter。

推荐阅读：`previews/01_game_flow.png` → `02_battle_phase_states.png` → `motion_previews/*.mp4` → `docs/DW-HANDOFF-001...docx` → `contracts/` → `manifests/FILE_INDEX.csv`。
