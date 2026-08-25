# Unity U-00 → U-03 Daily Report — 2026-08-24

Status: **PASS — U-00 → U-03 CYCLE COMPLETE**

## Outcome

U-00～U-03 的占位 UI 已通过实机与自动门禁终验，**本周期从 20% 到 100%**。这里的 100% 只代表获批的 U-00～U-03 占位可玩周期完成，**不代表整个游戏、最终美术或发布完成**。权威实机证据为 [`tabletop-v2` live report](evidence/unity-u00-u03-2026-08-24/tabletop-v2/UNITY_LIVE_FLOW_REPORT_2026-08-24.md)：生产数据完成 `PLAY_CARD` / `END_TURN` / `DISCARD`；明确标记为 **NON_AUTHORITATIVE**、仅存在于 Play Mode 且从未保存的 fixture 完成生产卡池当前未广告的 `ATTACK` / `PULL` 交互证据。fixture 随后被销毁，面板恢复绑定生产 `RuntimeBootstrap`，Editor 回到 `ready`、Play Mode 停止。

## 开工前三问

### 1. 实际读取哪套卡牌数据

- 生产/runtime 权威输入是 `data/cards/*.json` 中的 **91 张**；`CardCatalog.LoadDirectory` 读取该目录，Unity 构建/运行准备流程把相同数据放入 `Assets/StreamingAssets/data`。
- 540 张 `docs/卡牌设计包_2026-08-15/bundle_v2.json` 是设计卡池/候选迁移源，当前 runtime 不读取它。
- 540 张导入**没有排进 U-00～U-03 周期，也没有已批准的交付周期**。它涉及数据映射、schema/兼容、构筑与回归，应作为本周期之后单独批准的数据迁移周期处理；本周底层冻结期间不导入。

### 2. Runtime Contract 的 “6 invalid”

这六项是验证器必须拒绝的负向 fixture，不是六个失败：

1. `ui_event/invalid/bad_phase.json` — 非法阶段值；
2. `ui_event/invalid/pull_raw_internal_data.json` — UIEvent 泄露不允许的内部 Pull 原始字段；
3. `game_snapshot/invalid/missing_version.json` — 缺少契约版本；
4. `game_action/invalid/unknown.json` — 含 schema 不允许的未知字段；
5. `game_action/invalid/payload_target_entity_string.json` — target entity 使用不合约的字符串表示；
6. `action_result/invalid/missing_revision.json` — 缺少 revision。

验证结果是 **11 valid / 6 expected-invalid / 0 failures**，所以要紧程度为“测试覆盖正常”，不是 runtime 缺陷。此前几份报告多只汇总 Contract gate 的 PASS/FAIL，或早于这批负向 fixture；“invalid”被当作预期拒绝用例压缩进 PASS，没有单列解释。2026-08-23 的实现报告已记录 11/6/0 明细。

### 3. 牌库循环胜负方向

已同步。`docs/RULES.md` §9 现在明确：某玩家自己的牌库发生有效循环时，该玩家自己的计数 +1；该玩家达到默认 10 次时自己获胜。因此，把对方牌库反复打空会促使对方获胜，出牌造成这一结果的一方输。文档、实现与最终 **466/466** 回归方向一致。

## U-00～U-03 最终验收

- **U-00 牌桌 — PASS：**`RuntimeBootstrap.unity` 实机显示占位桌面；1280×720 与 1440×900 各保留完整证据组。fixture 移除后的新鲜生产启动只有一个 scene bootstrap、零 fixture object，面板绑定 `DominionWarsRuntimeBootstrap`。
- **U-01 阶段 — PASS：**生产实机由 revision 1 的 AMBUSH 进入 ACTION、DISCARD、下一玩家 AMBUSH/ACTION，再回到玩家 0；UI 显示的 match、turn、phase、current player 与 revision 均来自权威 snapshot。
- **U-02 合法行动 — PASS：**真实 `Button.onClick` 与卡牌拖拽只消费广告的 `LegalAction`。非法拖放 `play_21` 保持 revision **2 → 2**、不提交并回到手牌；合法城堡落点 `play_22_core:shared_castle` **2 → 3** 且 accepted。
- **U-03 对局操作 — PASS：**生产链路接受 `PLAY_CARD`、`END_TURN`、`DISCARD`，revision **1 → 10**；NON_AUTHORITATIVE fixture 接受 `ATTACK` 与 `PULL`，revision **10 → 12**，并验证城堡生命、Cloud、Grave 与 PullCount 的状态变化。最终 QA 接受生产链路与 fixture 边界的组合证据作为本占位周期门禁。
- 未手改 scene/prefab YAML；本轮文档收尾未修改 engine、rules 或 Adapter。

`RuntimeBattleActionsEditModeTests` 当前新增的七类真实 UI 交互覆盖为：

1. ATTACK 只暴露引擎广告的 source/target 组合；
2. PULL 的 source 选择、toggle 与 submit 保留广告的 target/action identity；
3. disabled advertised action 不能成为提交项；
4. 行动标签保留 wire source/target，不推导规则；
5. hot-seat viewer 跟随 current player 刷新，并保持对手手牌隐藏；
6. `Button.onClick` 提交广告的 `END_TURN` 并渲染 accepted result；
7. rejected result 后刷新 advertised actions，同时保留 reason 可见性。

新增的 `PullLifecycleData` 不替代 91 张 production 卡池，也不会进入运行时正式数据。对应 EditMode 测试使用该 Unity-only fixture 走真实链路：`PLAY_CARD` 放置载体与提交牌 → on-play `COMMIT` → `END_TURN` 触发 `PUSH` → 回合返回后广告并提交 `PULL` → 卡进入 Graveyard；同时断言 `CARD_COMMITTED`、`CARD_PUSHED`、`PULL_DECLARED`、`CARD_PULLED` 事件与 `PullCount = 1`。该 fixture 还在实机 Play Mode 中临时绑定进行 ATTACK/PULL 交互取证；它始终是 **NON_AUTHORITATIVE**，未保存进 scene，取证后已销毁并恢复生产绑定。

## Action / revision evidence

### Production — authoritative 91-card runtime data

| UI path | Action | Revision | Result |
|---|---|---:|---|
| Button | `skip_ambush_0` | 1 → 2 | accepted；进入 ACTION |
| Invalid drag/drop | `play_21` | 2 → 2 | 未提交；卡返回手牌 |
| Drag → castle drop zone | `play_22_core:shared_castle` | 2 → 3 | `PLAY_CARD` accepted |
| Button | `play_21` / `play_28` | 3 → 4 → 5 | 两次 `PLAY_CARD` accepted |
| Button | `end_turn_0` | 5 → 6 | accepted；进入 DISCARD |
| Button | `discard_0_0` | 6 → 7 | accepted；`requiredCount=0` |
| Button | `skip_ambush_1` / `end_turn_1` | 7 → 8 → 9 | accepted；进入玩家 1 DISCARD |
| Button | `discard_1_6` | 9 → 10 | accepted；`requiredCount=6`，hand 14 → 7，grave 0 → 7 |

### Fixture — NON_AUTHORITATIVE, Play-Mode-only

fixture 设置阶段的真实按钮提交全部 accepted，并从 revision **1 → 10** 建立 `PLAY → COMMIT → END → PUSH` 状态。随后：

| UI path | Action | Revision | Result |
|---|---|---:|---|
| Drag → castle drop zone | `attack_3_core:shared_castle` | 10 → 11 | `ATTACK` accepted；castle life 74 |
| Drag → MechanicalCloudCommit | `pull_3_2` | 11 → 12 | `PULL` accepted；Cloud 1 → 0、Grave 0 → 1、PullCount 0 → 1 |

fixture 仅用于补足 production 91-card 数据当前不广告的 ATTACK/PULL 生命周期证据；它不证明 540-card 设计包已经导入，也不改变生产规则或卡池。

## Final verification

- QA 最终结论：**PASS；U-00 / U-01 / U-02 / U-03 全 PASS；P0 = 0**。
- Unity EditMode：**65/65 passed**。
- Unity PlayMode：**3/3 passed**。
- .NET Release regression：**466/466 passed**。
- Unity compilation：**0 errors**。
- 新鲜生产 Console 边界：**0 warnings / 0 errors**（cursor 409 → 410 只有正常 bootstrap ready 日志，`dropped=false`）。
- `PullLifecycleData` fixture JSON、静态编译与文档 diff check：**PASS**。
- Runtime Contract：**11 valid / 6 expected-invalid / 0 failures**。
- Unity CLI：`C:\Users\USER\AppData\Local\Unity\bin\unity.exe`，版本 **1.0.0-beta.6**。
- Unity Pipeline：官方 CLI 已把 `com.unity.pipeline` **0.5.0-exp.1** 加入项目 manifest。
- 最终 Editor / Pipeline：Unity 6000.3.21f1，PID 33236，port 7800；`ready`、`playMode=stopped`、`compiling=false`、`domainReloadInProgress=false`。

## Historical checkpoints corrected

45%、52%、56% 是同日的保守中间检查点，不是最终状态。56% 检查点时，Software Terms 只是 CLI 日志推断，Licensing Client mutex、四个同项目进程与 `STATUS_NO_INSTANCES` 是当时观察到的环境阻塞；真实用户许可证一直有效，沙箱 `SQLite Error 14` 也只是工具身份的只读限制。它们均不得继续写作已证实的当前唯一阻塞。

最终状态已由可连接的交互式 Editor、Pipeline `ready`、实机 tabletop-v2 交互和 Test Runner 结果取代上述中间判断；因此 U-03 与本周期可以关闭。旧状态保留仅为审计历史。

## Final cycle accounting

验收权重仍按最终可玩结果分配；实机与自动门禁已补齐此前保留的 44%：

| Slice | Weight | Final credit | Evidence |
|---|---:|---:|---|
| U-00 可见牌桌 | 20% | 20% | 双分辨率实机截图、fresh production bootstrap、零 fixture object |
| U-01 阶段/回合 | 15% | 15% | production revision 1 → 10 的阶段/玩家流转 |
| U-02 合法行动 | 25% | 25% | 非法拖放不提交；合法按钮/拖放均使用 advertised action 并 accepted |
| U-03 对局操作 | 40% | 40% | production PLAY/END/DISCARD + NON_AUTHORITATIVE fixture ATTACK/PULL；EditMode 65/65、PlayMode 3/3 |
| **Total** | **100%** | **100%** | **仅 U-00～U-03 占位周期，不是整个游戏完成度** |

## Severity and follow-up

- **P0 = 0**。
- 保留的 P1 是视觉收尾：证据画面水印处理、卡牌文字层级/可读性、最终美术与整体 polish。
- 这些 P1 不影响“占位素材从 U-00 到 U-03 可玩”的本周期验收；未来美术工作应进入后续视觉周期，不能反向把本周期标成未完成。

一句话日报：**Unity 前端 U-00～U-03 周期 20% → 100%；今天完成 U-00 牌桌、U-01 阶段、U-02 合法行动与 U-03 PLAY/ATTACK/END TURN/PULL 的实机终验，最终门禁 EditMode 65/65、PlayMode 3/3、.NET 466/466、Unity 编译 0 错、Console 新边界 0 warning/0 error；这是本周期完成度，不是整个游戏完成度。**
