# Dominion Wars 当前状态存档 — 2026-09-06

这是一份本地 checkpoint 的状态记录，不是发布声明，也不替代 Unity 实机复验。

## 存档位置

- 起点：本地 `main` 的 `6c9ef6506914ecc9deb6b8ee5f15580df4ac175f`。
- 分支：`codex/checkpoint-2026-09-06-playable-demo`。
- 远端只读核对：`origin/main` 当前为 `0f9868a1d43e85bc984607835b96aea6c9da837d`；本次没有 push。
- 本次 checkpoint 提交（按依赖顺序）：
  - `fade1180c979b4821882ae9f8c0419cde2969b2e` — 内容管线 / Card Editor。
  - `bfa921c25234cbef92d52f95c88995a3ba562ade` — Engine 伏击与机械生命周期。
  - `ae3df0ba19f06dcfb40b775d9530227610544ab8` — runtime contract / outcome wire。
  - `a5642d8523c0d79e2552de03c63949d3311611b9` — Unity runtime / UI / tests。

这些提交是可恢复的本地历史；它们不等于所有相关门禁已经通过。

## 当前可记录的验证证据

本次低额度存档只记录已有的、明确允许引用的基线：

- .NET：`548/548`。
- 离线卡牌：`91/91`。
- 离线牌组：`4/4`。
- 离线素材清单：`320/320`。
- Java：`38/38`。
- Unity 当前工作区版本：全量 EditMode、PlayMode、Windows Player 和前台视觉 smoke **尚未完成本轮复验**，不在本文件中宣称通过。

历史日报中的更早 Unity 数字仅属于当时的工作区证据，不能自动转移为本 checkpoint 的当前 PASS。

## 主线与优先级

P0 主线仍是玩家端完整一局：

`启动 → 选择/进入牌组 → 开局 → 读牌与鼠标操作 → 出牌/攻击/回合推进/PULL（由引擎广告时） → 终局 outcome → 恢复或再开一局`

只有会导致启动/崩溃、死局、核心规则明显错误、不可逆数据损坏，或后续必然推翻架构的问题可以插队。视觉 polish、CardEditor 极端便利功能、额外覆盖率、泛化重构和非主路径边界留作 P1/P2 backlog。

当前不能把游戏描述为“完成”：Unity 当前版本的完整闭环仍需重新运行并记录证据，且 Restart/session/seed/match-id 等待定语义仍不能由 UI 自行猜测。

## 本次卫生与安全审计

- 未把 `build-output/`、Unity `Library/Temp/Logs/Builds`、`bin/`、`obj/`、`target/` 或 `.codex-remote-attachments/` 纳入提交。
- 未把 `unity/**/Assets/QA/**` 截图或其 `.meta` 纳入提交。
- 未把空目录/来源不明的孤立 `.meta` 纳入提交。
- 候选文本和已暂存文件未发现高置信度密钥/私钥字面量；暂存文件没有超过 10 MiB 的文件。
- 本次未删除、清理或 reset 文件；未 push。

## 有意保留的工作区脏项

以下内容没有被强行处理，待后续由 owner/PL 重基线或单独决定：

- `docs/AI_MAILBOX.md`、`docs/DAILY_GOAL.md`、内容规格和 known-issues 的历史增量：其中包含旧 Unity 证据，当前版本未复验，暂不作为当前事实提交。
- `scripts/sanity_check.py` 的删除：本次遵守不删除文件，保持未提交删除状态。
- `unity/DominionWars.Unity/Packages/manifest.json`、`packages-lock.json` 与 `ProjectSettings/*` 的 AI Assistant / 编辑器环境变更：未证明是游戏运行所需依赖，暂不纳入游戏 checkpoint。
- `src/Data/DeckEditor.meta`、`unity/DominionWars.Unity/Assets/DominionWars.Editor/ContentStudio.meta`、`unity/DominionWars.Unity/Assets/Resources.meta`：对应空目录或来源无法确认，暂不提交。
- `unity/DominionWars.Unity/Assets/QA/` 及其截图、`.meta`：明确属于人工 QA 产物，暂不提交。

下一次开发应从本分支恢复，先完成 Unity 当前版本的真实复验，再回到上述 P0 玩家闭环；除非获得明确授权，不要把这些脏项自动 add 或 push。
