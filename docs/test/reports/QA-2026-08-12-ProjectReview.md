# QA 项目全面审查报告

> DeepSeek QA · 2026-08-12 · 范围：项目结构、游戏规则、资源合理性、有效性

## 概要

对 91 张卡牌、4 套牌组、schema、规则书、美术资源做了一次非回归的全量审计。发现 2 个高优先级问题、4 个中等、3 个低优先级。

---

## 🔴 P1：EffectTarget — Schema 与 DESIGN.md 不一致

**发现**：
| 对比 | 差异 |
|---|---|
| Schema 独有 | `ENEMY_TARGET`, `ENEMY_SINGLE`, `SINGLE_ENEMY` |
| DESIGN.md 独有 | `ENEMY_PLAYER`, `SELF_PLAYER`, `NONE` |

**影响**：
- `ENEMY_TARGET` 被 4 张卡使用（flame_strike, machine_cannon, wood_moon, sea_pressure），但 DESIGN.md 未列出
- `ENEMY_SINGLE` / `SINGLE_ENEMY` 没有任何卡使用，疑似死代码
- `ENEMY_PLAYER`, `SELF_PLAYER`, `NONE` 在 DESIGN.md 列出但 schema 不允许

**建议**：Codex 同步 schema 与 DESIGN.md；删除未使用的 `ENEMY_SINGLE`/`SINGLE_ENEMY`；添加缺失的 `ENEMY_PLAYER`/`SELF_PLAYER`/`NONE`

**复现**：
```bash
python scripts/align_check.py  # 目前不检查 targets，建议扩展
```

---

## 🔴 P2：RULES.md 缺失 Castle v4 核心规则（统领默认抗性 + guard + kingSlayer）

**引擎实际行为**（Game.java L94–111）：
- **统领默认抗性**：敌方普通卡牌效果（伤害/指定破坏/群伤）不能影响对方统领。这是 Castle v4 引入的重大规则变更
- **guard（护卫）**：随从型统领有护卫时，只要己方场上存在其他非统领随从，敌方不能攻击该统领
- **kingSlayer（弑君）**：拥有弑君的卡可以绕过统领抗性，也可以绕过护卫保护

**现状**：
- `CHANGELOG_CASTLE.md` §4 有完整文档 ✅
- `RULES.md` §7 未同步 —— 仍然只说"统领效果免疫非统领来源的反制"，完全没有提及"普通伤害/指定也对统领无效"
- `DESIGN.md` 也未提及

**影响**：新玩家/新开发者只读 RULES.md 会以为任何伤害都能打统领，实际引擎会空发。这是本次审查发现的最严重的文档漂移。

**建议**：PL 将 Castle v4 统领抗性/护卫/弑君三条规则合入 RULES.md §7

---

## 🟡 P3–P6：中等优先级

### P3：4 张统领图 RGB 无透明通道
- `flame_leader.png`, `machine_alpha.png`, `sea_leader.png`, `wood_leader.png`
- 全部 mode=RGB, 360×150
- UI 叠在卡框上时会有矩形白/黑底边
- **建议**：转 RGBA，透明底

### P4：cardart/ 目录为空
- 91 张卡全部无插图
- 试玩前需排期

### P5：Tags 跨阵营共享锁（PL 已标记）
- "守卫""破坏""风暴""反制"等 10+ 个 tag 跨阵营共享
- 按 §4 规则，同 tag 每回合段只能用一次
- 跨阵营对战时形成隐式互锁——需规则澄清

---

## ⚪ P6–P8：低优先级

### P6：summonedThisTurn 出现在 Schema
- 明显的运行时标记，不应存在于卡牌数据 schema

### P7：winAmount 定义但未使用
- 所有统领都用 winParam，winAmount 冗余

### P8：condition 字段定义但未使用
- EffectSpec.condition 在 schema 中但没有任何卡使用

---

## ✅ 通过项

- 91 卡全部通过 JSON schema 校验
- SUMMON/SUMMON_LEADER 引用全部有效（无悬空 id）
- 4 套牌组 = 各 61 张（20 种 × 3 + 统领），在 [60, 80] 范围内
- 无 punish=0 + punishActivatable 的矛盾卡
- DAMAGE_CASTLE 实际使用与 schema 一致
- EffectAction 24 项全部 DESIGN.md ↔ schema 对齐
- Java 回归 35/35、模拟 1200 回合均通过
- Python sanity 0 ERROR、align 无悬空

---

## 环境
- OS: Windows NT, `dominion-wars-win64`
- Java 35 项回归 + 1200 回合模拟
- Python 3 (sanity_check_v2, align_check, 自定义审计脚本)
- PIL/Pillow (PNG transparency check)
