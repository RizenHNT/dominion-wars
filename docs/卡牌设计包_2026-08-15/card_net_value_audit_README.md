# card_net_value_audit 说明（2026-08-16 v13 修正版 · 生成器对账口径）

> 审计脚本：build-output/audit-net-value.js（可复验）· 数据：bundle_v2.json（540 张）
> **口径修正（2026-08-16 14:30）**：审计与生成器预算统一，解决"超模 178"虚高问题。

## 对账口径（与 gen-v11 生成器完全一致）

```
target = Budget_A(P) − 1.5×P' − 主题权重w − 任务价值T + 等级加成(0/1/2)
MINION 净值 = S + K + Σ主动效果 − target
```

- **Budget_A(P) = 4.55 + 0.25P + 0.16P²**（唯一总预算线，SPEC §3.4）
- **P' 扣减**：1.5×P'（生成器预算含此，审计不再加回 2×max(0,P−P')——旧口径虚高的根源）
- **等级加成**：v1=0 / v2=1 / v3=2（生成器 LEVEL_BONUS）

## 分布（对账口径，2026-08-16 修正后）

| 类型 | 张数 | 超模 | 亏模 | 正常 |
|---|---|---|---|---|
| **MINION** | 288 | 26 (9%) | 4 | **258 (90%)** ✅ 平衡 |
| SPELL | 144 | 57 | 63 | 24 |
| AMBUSH | 54 | 0 | 53 | 1 |
| PUNISH | 54 | 0 | 54 | 0 |

## 公式适用性

- **MINION = 公式适用**（对账口径，净值可信）
- **SPELL/AMBUSH/PUNISH = 公式不适用**：无身材，Budget_A 含身材底价（4.55）导致 SPELL 虚高、PUNISH 虚亏。这三类需平衡批次单独定预算线（如效果价值 ≤ Budget_A − 2）。

## 签名卡（10 张，5 阵营，全部净值 [-1.5,+1.5]）

flame_imp_v3(-0.55) / flame_titan_v3(-1.11) / machine_drone_v3(-0.05) / machine_relay_v3(-0.05) / sea_leviathan_v3(0.95) / sea_octopus_v3(0.39) / wood_seedling_v3(-0.05) / wood_bramble_v3(1.39) / neutral_mercenary_v3(-0.55) / neutral_cleric_v3(1.31)

## CSV 列

id, faction, name, type, P, Pp, S, K, actEv, T, budget, target, net, verdict, formulaNote
