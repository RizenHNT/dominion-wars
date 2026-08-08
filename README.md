# 统御战纪（Dominion Wars）

## 王城正式版（2026-06-12）

本版以 GPT 王城原型 v7 为设计基线正式整合（详见 docs/CHANGELOG_CASTLE.md 与 docs/BALANCE.md）：

- **共享王城（75 血）**：双方共同争夺的公共目标，破城方迫使对手统领出场并获得胜利计数 ≥9 的终局优势；烈焰皇破城即胜。
- **统领体系**：统领默认效果抗性、【护卫】（随从护驾时不可被普攻）、【弑君】（绕过统领抗性）、单体/群体伤害目标分离。
- **胜利计数**：对手每完成一次牌库循环，己方胜利计数 +1，达 10 获胜。
- **深海弃牌联动**：新增「对方弃牌时」触发（onOpponentDiscardEffects），编辑器已支持。
- **全新图形卡牌界面**：暗色牌桌、全自绘卡牌（阵营纹章插画/惩罚徽章/攻血宝石/状态光效）、王城血条 HUD、玩家信息板、三语界面与显示密度设置。
- **Windows EXE 便携版**：双击即玩，内置 Java 运行时，无需安装任何环境。

### P3：引擎与界面解耦（网页版正式成为主界面）
- **架构**：Java 引擎内置零依赖本地 HTTP 服务（JDK 自带 HttpServer），JSON 协议暴露状态与指令；界面是纯 HTML/JS（web/ 目录），规则权威完全在引擎，35 项测试与模拟器不受 UI 影响。
- **操作**：拖动手牌到牌桌出牌/盖伏击；按住己方随从拖出红色攻击箭头到敌方单位、王城或对方信息板；点击仍然可用。
- **动画**：卡牌移动（FLIP）、入场弹出、受击抖动+伤害飘字、王城血条震动、行动播报横幅、AI 分步出招。
- **新手教程**：首次对局自动弹出 6 步引导遮罩（手牌/惩罚值/王城/攻击/阶段/日志），右上角 ? 可随时重看，三语文案。
- **参数化**：外观与节奏集中在 web/config.js 与 data/ui.json，改参数即可调整（见 web/修改指南.md）。
- **编辑器**：右侧实时卡面预览（与对局观感一致）、「选择卡图…」一键导入 data/art/、打开卡图文件夹。
- **启动方式**：统御战纪.exe = 网页版（自动开浏览器）；经典界面.bat = 原 Swing 界面；浏览器关闭约 2 分钟后服务自动退出。

### 体验更新（玩家反馈第一轮）
- **行动播放**：AI 回合逐步执行（约 0.65 秒/动作），中央横幅播报每个动作，受击单位红光闪烁——再也不会"一瞬间就打完了"。
- **胜利条件进度**：双方信息板实时显示统领特殊胜利进度（如 Goal 3/12），临近达成变红提醒。
- **卡面优化**：攻/血宝石放大，受伤变红、增益变绿（炉石规则）；卡牌文本去冗余（词条不再重复写进描述、吟唱回合并入类型条）。
- **自定义卡图**：将 PNG 以「卡牌ID.png」放入 `data/art/` 即自动用作插画（已附四张统领示例图），缺图时回退程序化纹章。



一款以**惩罚值**为核心机制的原创卡牌对战游戏的 Java 单机实现：你打出的每张牌都会让对手抽牌，而对手抽到的牌可能立刻反咬你一口。唯一的胜利方式是击败对方的**统领**。

- Java 17 + Swing，**零外部依赖**（JSON 解析与测试框架均为自实现）
- 卡牌 / 卡组 / 全部数值由 `data/` 下的 JSON 驱动，内置可视化卡牌编辑器
- 支持：人机对战、双人同屏、AI 演示观战
- 4 个阵营 91 张卡、4 套 61 张构筑卡组、30 项规则回归测试、AI 平衡模拟器

完整规则见 [docs/RULES.md](docs/RULES.md)，架构与扩展指南见 [docs/DESIGN.md](docs/DESIGN.md)，平衡数据见 [docs/BALANCE.md](docs/BALANCE.md)。

## 运行

需要 JDK 17 或更高版本。**请在项目根目录运行**（程序从工作目录读取 `data/`）。

### 方式一：Gradle

```bash
gradle run          # 启动游戏
gradle rulesTest    # 运行 30 项规则测试
gradle simulate -Pgames=8   # AI 对 AI 平衡模拟
```

### 方式二：纯 JDK（无需 Gradle）

```bash
# Linux / macOS
scripts/build.sh
scripts/run.sh

# Windows
scripts\build.bat
scripts\run.bat
```

### 方式三：控制台对战模式（无图形环境 / 手机）

与图形版共用同一套引擎与 AI，规则零分叉：

```bash
java -Dstdout.encoding=UTF-8 -cp build/classes com.dominionwars.app.ConsoleMain
# 可选参数：玩家卡组序号 AI卡组序号，如 ConsoleMain 1 3
```

## 在手机上测试

**方案 A：Termux 原生运行（安卓，推荐）**

1. 安装 [Termux](https://f-droid.org/packages/com.termux/)（建议 F-Droid 版）；
2. 把工程 zip 放进手机后执行：

```bash
pkg install openjdk-21 unzip -y    # 若无 21 可用 openjdk-17
termux-setup-storage               # 授权访问下载目录
cd ~ && unzip /sdcard/Download/dominion-wars-v1.0.zip
cd dominion-wars && scripts/build.sh
java -cp build/classes com.dominionwars.app.ConsoleMain        # 人机对战
java -cp build/classes:build/test-classes com.dominionwars.test.SimMain 8   # 平衡模拟
```

**方案 B：远程桌面玩图形版**——家中电脑保持开机运行 `scripts/run.sh`，手机装向日葵 / ToDesk / Chrome Remote Desktop 连回去操作，测试的是完整 Swing 界面。

（iOS 无法本地运行 JVM，请使用方案 B，或 SSH 连接家中电脑跑控制台模式。）

测试与模拟（任一方式构建后）：

```bash
java -Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -cp build/classes:build/test-classes com.dominionwars.test.TestMain
java -Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -cp build/classes:build/test-classes com.dominionwars.test.SimMain 8
```

（Windows 下 classpath 分隔符用 `;`。）

## 目录速览

```
data/balance.json     全部可调数值（先驱威压、手牌上限、洗牌判负等）
data/cards/*.json     烈焰帝国 / 深海联盟 / 古木圣地 / 机械遗迹 / 中立
data/decks/*.json     四套预构筑卡组
docs/                 规则书 / 架构文档 / 平衡说明
src/main/java/        引擎 + AI + 界面
src/test/java/        规则测试 + 模拟器
scripts/              无 Gradle 环境的构建脚本
```

## 上传到 Git

```bash
cd dominion-wars
git init
git add .
git commit -m "统御战纪 v1.0：引擎 + UI + 编辑器 + 4 阵营卡池"
git remote add origin <你的仓库地址>
git push -u origin main
```

`.gitignore` 已配置忽略 `build/` 等产物目录。

## 路线图

- [x] 规则引擎（惩罚连锁 / 空发裁决 / 词条回合段 / 伏击三分类 / 统领体系 / 先驱威压）
- [x] 启发式 AI 与平衡模拟
- [x] 对战界面 / 卡牌编辑器 / 主菜单
- [ ] 联机对战（待数值平衡测试充分后开发）
