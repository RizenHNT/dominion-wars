# 回滚（ROLLBACK）玩家动作 · 契约变更提案（PL · 2026-09-11）

> **状态：已获 owner 批准并于 2026-09-11 落地**（契约条目 `1.31-player-rollback`，见 `docs/effects.contract.md` 的 ROLLBACK 行）。
> ~~状态：待 owner 批准。提案不落地。~~（此状态行写于提案提交时，批准后未及时更新；2026-09-12 审核时更正。）
> 本文件是 `PL_NIGHT_SHIFT_2026-09-11.md §7.7` 的展开，保留作为批准前的提案记录。
> **性质：规则 + 契约变更**（新增第 9 个玩家动作类型），故按 owner 的「规则上任何改动先给我说明」先出提案。

---

## 1. 问题

`ROLLBACK`（回滚）目前**只有卡牌效果能触发**：

- `src\Engine\Effects\RollbackEffect.cs` 已注册，`EffectRuntime` 已实现"把提交队列中的一张卡移回手牌"；
- **但玩家自己用不了**：`LegalActionGenerator` 从不广告 `ROLLBACK`，`TurnActionRouter` 也没有对应 handler。
- 目前唯一能回滚的路径是 `machine_recycler` 的 `commitEffects: [{"action":"ROLLBACK","amount":1}]`。

已由测试钉住现状：`P1P2CorrectnessTests.RollbackIsAnEffectOnlyActionAndIsNotAdvertisedToPlayers`（断言 `ROLLBACK` **不在** 1.31 玩家动作枚举、**在**卡牌效果枚举；五阶段扫描在不空/空的提交队列下都不广告它），以及 `RollbackEffectStillMovesTheChosenQueueCardBackToHand`（效果路径正常工作）。

## 2. 为什么它是"契约变更"而不是普通实现

| 契约文件 | 现状 | 影响 |
|---|---|---|
| `design/runtime-kit-v1.31/contracts/schemas/game_action.schema.json:15` | 枚举**恰好 8 个**：`PLAY_CARD / SET_AMBUSH / SKIP_AMBUSH / ATTACK / COMMIT / PULL / DISCARD / END_TURN` | 加第 9 个 = 改冻结契约 |
| `design/runtime-kit-v1.31/contracts/schemas/game_snapshot.schema.json:106` | 镜像同一枚举 | 同上 |
| `src\Engine\Tests\ContractBoundaryTests.cs:56-77` | `PlayerActionConstantsMatchRuntimeContract131` 钉住这 8 个 | 改枚举会红，须同步更新 |
| `data/schema/cards.schema.json` `$defs.EffectAction` | `ROLLBACK` **已经**在这一侧 | 说明这是"效果动作 vs 玩家动作"两套词表的分裂 |

⇒ 按 `AGENTS.md`，改 `design/runtime-kit-v1.31/contracts/` 属契约变更，需要**明确的迁移说明**，且不在工程侧单方权限内。

## 3. 规则依据（支持做成玩家动作）

`docs/RULES.md`：

- **:261（§12.4）**：「选择己方提交队列中的一张合法卡牌，将其移回手牌；**不触发上传或下载效果，也不能回溯已经发生的惩罚抽牌、伤害、随机结果或事件记录**」。
- **:262**：明确标注 上传 / PUSH **不是**玩家主动按钮（「上传不是玩家主动按钮」）。
- **:344（术语表）**：`回滚 | ROLLBACK`。

⇒ 规则书把"回滚"和"上传"区别对待：上传被显式排除在玩家按钮之外，**回滚没有被这样排除**，且被写进了动作条款。这是它应当成为玩家动作的主要依据。

## 4. 提案（形状已备好，批准后可一步落地）

| 项 | 值 |
|---|---|
| 动作类型 | `ROLLBACK`（第 9 个） |
| 广告条件 | `player.CommitQueue.Count > 0`，**每张**合法队列卡广告一条 |
| ActionId | `rollback_{instanceId}` |
| SourceId | 该队列卡 |
| reasonKey | `action.rollback` |
| **惩罚值** | **0** |
| 惩罚值的依据 | `RULES.md:261`「不能回溯已经发生的惩罚抽牌」+ 术语表「费用不返还」⇒ 之前那次 COMMIT 已付的 `commitCost` **既不重收也不退还**，故回滚路径**不产生惩罚抽牌、也不开惩罚响应窗口** |
| 效果 | 复用既有 `EffectRuntime.Rollback`（队列 → 手牌），不触发上传/下载效果 |
| 目标选择 | 由 ActionId 携带（`rollback_{instanceId}`），不需要额外 `selectedEntityIds` |

**为什么惩罚值是 0 而不是 `commitCost`**：若回滚要再付一次惩罚，玩家就会因为"提交→反悔"而二次受罚，而 `RULES` 明说"费用不返还"——它的语义是这次回滚**不结算费用**，既不是退款也不是重收。若改成"付 `commitCost` 才能回滚"，那是另一条规则，需要另立条款。

## 5. 需要同步改动的清单（批准后）

1. `design/runtime-kit-v1.31/contracts/schemas/game_action.schema.json` + `game_snapshot.schema.json`：枚举加 `ROLLBACK`，**并写明迁移说明**（旧客户端如何忽略未知动作）。
2. `src\Engine\Tests\ContractBoundaryTests.cs:56-77` 的期望集合。
3. `src\Engine\LegalActionGenerator.cs` 广告逻辑 + `src\Engine\Turns\TurnActionRouter.cs` 新增 handler。
4. 更新 `P1P2CorrectnessTests` 里那条"钉住现状"的测试（它必须跟着契约一起改，**这正是它的用途**）。
5. `docs/RULES.md` 与 `docs/effects.contract.md` 的交叉引用；`data/schema/cards.schema.json` 侧不动（效果动作词表本就含它）。
6. 客户端/前端需要能渲染这个动作（属前端路由）。

## 6. 我的建议

**建议批准**，理由：规则书已经写了这个动作且没有像 PUSH 那样排除它；机械阵营的"提交→上传→下载"链条目前**没有任何反悔机制**，而回滚能为机械提供一点操作弹性（机械在 P0-1 之后已被实测确认偏弱）。但**这不解决机械的核心问题**（见 `PL_BALANCE_MEASUREMENT_2026-09-11.md §10`：该修的是胜利条件 6→5），别把回滚当成机械的救命稻草。

**风险提示**：新增玩家动作会增加前端契约面（枚举、快照、合法动作渲染、回放），收益是机制完整性与机械的操作弹性。若想缩小改动面，也可以选择**只把 `ROLLBACK` 从效果词表"提升"为可选动作并在 v1.32 集中处理**，而不是在 1.31 上打补丁——**这条我建议 owner 与前端一起定**。

— PL（DeepSeek V4 Flash harness）· 2026-09-11
