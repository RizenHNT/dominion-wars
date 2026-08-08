# 《统御战纪》架构与扩展指南

## 1. 技术栈

- Java 17（无任何外部依赖；JSON 解析与测试框架均为自实现）
- Swing 界面；Gradle 构建（也提供纯 javac 脚本兜底）
- 卡牌、卡组、数值全部 JSON 数据驱动，从工作目录 `data/` 加载

## 2. 目录结构

```
src/main/java/com/dominionwars/
  util/Json.java          自实现 JSON 解析/序列化（保序）
  model/CardDef.java      卡牌定义（含 EffectSpec / LeaderDef，双向序列化）
  data/Balance.java       全局数值（balance.json）
  data/CardLibrary.java   卡库加载/保存 + DeckDef 卡组构建校验
  engine/CardInstance.java  对局中的卡牌实体
  engine/PlayerState.java   玩家区域与回合标记
  engine/PlayerAgent.java   决策回调接口（AI 与人类共用）
  engine/Effects.java       效果动作 DSL 解释器
  engine/Game.java          核心规则引擎（回合/惩罚/伏击/统领/胜负）
  ai/AiAgent.java           启发式 AI（含惩罚预算克制）
  ui/SwingHumanAgent.java   人类决策弹窗代理
  ui/MainMenu.java          启动器
  ui/game/GameWindow.java   对战窗口
  ui/editor/EditorWindow.java 卡牌编辑器
  app/Main.java             入口
src/test/java/com/dominionwars/test/
  TestMain.java             30 项规则回归测试
  SimMain.java              AI 对 AI 批量平衡模拟
data/
  balance.json  cards/*.json  decks/*.json
```

## 3. 效果 DSL

卡牌效果是 `EffectSpec` 列表：`{action, target, amount, param}`。

### 3.1 动作表（Effects.java）

| action | 说明 |
|---|---|
| DAMAGE / HEAL | 造成伤害 / 治疗（target+amount） |
| DRAW / OPP_DRAW | 己方 / 对方抽 amount 张 |
| DISCARD_OPP_RANDOM | 对方随机弃 amount 张（计入弃牌胜利条件） |
| DISCARD_DRAWN | 弃掉本次抽到的牌 |
| DESTROY | 摧毁目标（对统领空发） |
| BUFF | 攻/血修正（amount=攻，param=血，如 "2"） |
| GRANT_KEYWORD | 赋予关键词（param=关键词名） |
| SUMMON / SUMMON_LEADER | 召唤 param 指定 id 的卡（后者以统领身份） |
| END_TURN | 立即结束当前回合 |
| ADD_OPP_PUNISH_TURN / ADD_SELF_PUNISH_TURN | 本回合对方/己方卡牌惩罚值 +amount |
| CONVERT_PUNISH_TO_DISCARD | 本回合自己受到的惩罚抽牌改为弃自己牌 |
| PROTECT_TURN | 本回合己方卡牌不被破坏/反制 |
| NEGATE | 反制目标卡（统领免疫） |
| NEGATE_ENEMY_EFFECTS_TURN | 本回合敌方场上卡牌效果无效 |
| SKIP_RESHUFFLE | 下一次洗牌不计入判负计数 |
| RESTORE_ATTACKS | 重置攻击次数 |
| GAIN_LIFE / LOSE_LIFE | 玩家生命增减 |
| WIN_GAME | 直接达成胜利（命运之门） |

### 3.2 目标表

`ENEMY_MINION / FRIENDLY_MINION / ANY_MINION / ALL_ENEMY_MINIONS / ALL_FRIENDLY_MINIONS / ALL_MINIONS / ENEMY_FACE / ENEMY_PLAYER / SELF_PLAYER / SELF / NONE`

单体目标在人类回合会经 `PlayerAgent.chooseTarget` 弹窗选择，AI 走启发式。

### 3.3 条件表（punishCondition 等）

`ALWAYS / SELF_LEADER_ON_FIELD / OPP_LEADER_ON_FIELD / ENEMY_MINIONS_GE_n / SELF_MINIONS_GE_n / HAND_GE_n / SELF_LIFE_LE_n`（n 写在条件串中，如 `ENEMY_MINIONS_GE_2`）。

## 4. 如何扩展新机制

1. **纯数值新卡**：直接用卡牌编辑器（或手改 `data/cards/*.json`）组合现有动作即可，无需改代码。
2. **新动作**：在 `Effects.java` 的动作 switch 中加一个分支；编辑器效果页直接填写新动作名即可使用。
3. **新关键词**：在 `CardDef` 加常量，在 `Game.attack/dealDamage` 等位置加判定。
4. **新统领胜利条件**：在 `Game.checkSpecialWins()` 增加分支，并在 `PlayerState` 中补充所需计数器。
5. **预埋数据**：编辑器"自定义字段"页写入的键值会原样保存在 JSON 的 `custom` 中，引擎读档不丢失，方便先做数据后做功能。

## 5. AI 说明

`AiAgent.playTurn(Game)` 完成一个完整回合：盖伏击 → 行动 → 攻击 → 结束。
关键克制：**惩罚预算**（每回合 `4 + turn/4`，对方手牌≥7 时 −2，己方威压期 +2，且单回合惩罚出牌数 ≤3），防止无脑灌牌喂对方手牌。

## 6. 测试与模拟

```bash
# 30 项规则测试
java -Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -cp build/classes:build/test-classes com.dominionwars.test.TestMain
# 平衡模拟（每个对阵 8 局）
java -Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -cp build/classes:build/test-classes com.dominionwars.test.SimMain 8
```

新增机制时请同步在 `TestMain` 中补充用例。
