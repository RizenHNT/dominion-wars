#!/usr/bin/env python3
"""DeepSeek Test Lead: 文档与实现对齐检查"""
import json, os

all_cards = {}
for f in ['data/cards/flame.json','data/cards/machine.json','data/cards/sea.json','data/cards/wood.json','data/cards/neutral.json']:
    cards = json.load(open(f, 'r', encoding='utf-8'))
    for c in cards:
        all_cards[c['id']] = c

print('=== 卡牌关键词统计 ===')
all_keywords = set()
for cid, c in all_cards.items():
    for kw in c.get('keywords', []):
        all_keywords.add(kw)
print(f'  所有关键词: {sorted(all_keywords)}')

print()
print('=== 卡牌效果动作统计 ===')
all_actions = set()
def scan_effects(effects):
    if not effects: return
    for e in effects:
        if isinstance(e, dict) and 'action' in e:
            all_actions.add(e['action'])

for cid, c in all_cards.items():
    for key in ['onPlayEffects','onDeathEffects','onAttackEffects','chantEffects','ambushEffects',
                'onTurnStartEffects','onTurnEndEffects','persistentEffects','onDamageEffects',
                'onOpponentDiscardEffects']:
        scan_effects(c.get(key, []))
    ld = c.get('leaderDef', {})
    for key in ['enterEffects','punishEffects','persistentEffects']:
        scan_effects(ld.get(key, []))

actions_doc = {
    'DAMAGE','HEAL','DRAW','OPP_DRAW','DISCARD_OPP_RANDOM','DISCARD_DRAWN',
    'DESTROY','BUFF','GRANT_KEYWORD','SUMMON','SUMMON_LEADER','END_TURN',
    'ADD_OPP_PUNISH_TURN','ADD_SELF_PUNISH_TURN','CONVERT_PUNISH_TO_DISCARD',
    'PROTECT_TURN','NEGATE','NEGATE_ENEMY_EFFECTS_TURN','SKIP_RESHUFFLE',
    'RESTORE_ATTACKS','GAIN_LIFE','LOSE_LIFE','WIN_GAME',
    'DAMAGE_CASTLE','DISABLE_ENEMY_LEADER','DRAW_CASTLE_BREAK','RESTORE_CASTLE',
    'NO_DAMAGE_CHECKPOINT','GIVE_OPP_PUNISH','DEAL_DAMAGE_CASTLE'
}
undocumented = all_actions - actions_doc
unused_in_cards = actions_doc - all_actions
print(f'  卡牌实际使用: {sorted(all_actions)}')
if undocumented:
    print(f'  DESIGN.md 未文档化的动作: {sorted(undocumented)}')
if unused_in_cards:
    print(f'  DESIGN.md 有但卡牌未使用的动作: {sorted(unused_in_cards)}')

print()
print('=== 卡牌类型分布 ===')
type_dist = {}
for cid, c in all_cards.items():
    t = c.get('type','?')
    type_dist[t] = type_dist.get(t, 0) + 1
for t, count in sorted(type_dist.items()):
    print(f'  {t}: {count} 张')

print()
print('=== 关键词/Tag ===')
tags_dist = {}
for cid, c in all_cards.items():
    for t in c.get('tags', []):
        tags_dist[t] = tags_dist.get(t, 0) + 1
for t, count in sorted(tags_dist.items()):
    print(f'  {t}: {count}')
guard_count = sum(1 for c in all_cards.values() if c.get('guard'))
ks_count = sum(1 for c in all_cards.values() if c.get('kingSlayer'))
print(f'  guard(护卫): {guard_count} 张, kingSlayer(弑君): {ks_count} 张')

print()
print('=== 统领胜利条件 ===')
for cid, c in sorted(all_cards.items()):
    if c.get('leader'):
        ld = c.get('leaderDef', {})
        print(f'  {cid}: {ld.get("winCondition", "MISSING")}')

print()
print('=== 跨卡引用检查 (SUMMON param) ===')
all_refs = set()
for cid, c in all_cards.items():
    def find_summons(effects):
        if not effects: return
        for e in effects:
            if isinstance(e, dict) and e.get('action') in ('SUMMON', 'SUMMON_LEADER'):
                ref = e.get('param', '')
                all_refs.add(ref)
                if ref not in all_cards:
                    print(f'  MISSING: {cid} -> {ref} (不存在)')
    for key in ['onPlayEffects','onDeathEffects','leaderDef']:
        effects = c.get(key, [])
        if isinstance(effects, dict):
            effects = effects.get('enterEffects', effects.get('punishEffects', effects.get('persistentEffects', [])))
        find_summons(effects)
print(f'  所有SUMMON引用: {sorted(all_refs & set(all_cards.keys()))}')
if not (all_refs - set(all_cards.keys())):
    print('  无悬空引用')

print()
print('对齐检查完成')
