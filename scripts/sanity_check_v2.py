#!/usr/bin/env python3
"""DeepSeek Test Lead: 项目合理性检测脚本 v2 (修正卡组格式)
不修改任何生产文件，只输出检测报告。"""
import json, os, sys, glob

# Keep diagnostics UTF-8 on Windows consoles (including cp932) and pipes.
for _stream in (sys.stdout, sys.stderr):
    _reconfigure = getattr(_stream, 'reconfigure', None)
    if _reconfigure is not None:
        try:
            _reconfigure(encoding='utf-8', errors='replace')
        except (OSError, TypeError, ValueError):
            pass

issues = []
warnings = []
info = []

# ===== 1. CHECK ALL JSON FILES PARSE CORRECTLY =====
json_files = []
for root, dirs, files in os.walk('data'):
    for f in files:
        if f.endswith('.json'):
            json_files.append(os.path.join(root, f))

print(f'=== JSON 解析检查 ({len(json_files)} 个文件) ===')
for fp in json_files:
    try:
        with open(fp, 'r', encoding='utf-8') as fh:
            json.load(fh)
    except Exception as e:
        print(f'  FAIL: {fp} - {e}')
        issues.append(f'JSON 解析失败: {fp}: {e}')

# ===== 2. CHECK balance.json =====
print()
print('=== balance.json 完整性 ===')
with open('data/balance.json', 'r', encoding='utf-8') as f:
    bal = json.load(f)
required_balance = [
    'openingHand', 'drawPerTurn', 'handLimit', 'reshuffleLoseAt',
    'chainLimit', 'deckMin', 'deckMax', 'royalCastleEnabled',
    'royalCastleMaxHp', 'royalCastleBreakVictoryCount'
]
for k in required_balance:
    if k not in bal:
        issues.append(f'balance.json 缺少键: {k}')

if bal.get('deckMin', 0) >= bal.get('deckMax', 999):
    issues.append(f'deckMin >= deckMax')
print(f'  openingHand={bal.get("openingHand")} drawPerTurn={bal.get("drawPerTurn")}')
print(f'  handLimit={bal.get("handLimit")}  chainLimit={bal.get("chainLimit")}')
print(f'  deckMin={bal.get("deckMin")} deckMax={bal.get("deckMax")}')
print(f'  reshuffleLoseAt={bal.get("reshuffleLoseAt")}')
print(f'  royalCastleEnabled={bal.get("royalCastleEnabled")}  royalCastleMaxHp={bal.get("royalCastleMaxHp")}  breakVictoryCount={bal.get("royalCastleBreakVictoryCount")}')

# ===== 3. CHECK ALL CARD FILES =====
print()
print('=== 卡牌数据完整性 ===')
card_files = sorted(glob.glob('data/cards/*.json'))
all_cards = {}
all_card_ids = set()

for cf in card_files:
    with open(cf, 'r', encoding='utf-8') as f:
        cards = json.load(f)
    if not isinstance(cards, list):
        issues.append(f'卡牌文件不是数组: {cf}')
        continue
    for c in cards:
        cid = c.get('id', '')
        if not cid:
            issues.append(f'卡牌缺少 id: {cf}')
            continue
        if cid in all_card_ids:
            issues.append(f'重复卡牌 ID: {cid}')
        all_card_ids.add(cid)
        all_cards[cid] = c
        for req in ['id', 'name', 'faction', 'type', 'punish']:
            if req not in c:
                warnings.append(f'卡牌 {cid} 缺少字段 {req}')
        if c.get('type') == 'MINION':
            if 'attack' not in c or 'health' not in c:
                warnings.append(f'随从 {cid} 缺少 attack/health')
        if isinstance(c.get('punish'), (int, float)) and c['punish'] < 0:
            warnings.append(f'卡牌 {cid} punish={c["punish"]} 为负数')

# Leader type analysis
print(f'  总计: {len(all_card_ids)} 张唯一卡牌')
leaders = {cid: c for cid, c in all_cards.items() if c.get('leader')}
print(f'  其中统领: {len(leaders)} 张')
for cid, c in leaders.items():
    ld = c.get('leaderDef', {})
    wc = ld.get('winCondition', 'MISSING')
    ctype = c.get('type', '?')
    has_enter = 'enterEffects' in ld
    has_punish = 'punishEffects' in ld
    has_chant = 'chantEffects' in c
    has_persist = 'persistentEffects' in ld
    has_ambush = 'ambushEffects' in c
    other_effects = []
    if has_chant: other_effects.append('chantEffects')
    if has_persist: other_effects.append('persistentEffects')
    if has_ambush: other_effects.append('ambushEffects')
    
    status = 'OK'
    if not has_enter and ctype == 'MINION' and not other_effects:
        status = 'WARN (随从型统领无 enterEffects)'
    elif not has_enter and ctype != 'MINION':
        status = f'OK (非随从型, 有替代效果: {other_effects or "无"})'
    elif has_enter:
        status = 'OK'
    
    print(f'  {cid}: type={ctype}, winCondition={wc}, enterEffects={has_enter}, punishEffects={has_punish} [{status}]')

# ===== 4. CHECK DECK FORMAT & REFERENCES =====
print()
print('=== 卡组引用一致性 (leader + cards{id:count}) ===')
deck_files = sorted(glob.glob('data/decks/*.json'))

for df in deck_files:
    with open(df, 'r', encoding='utf-8') as f:
        deck = json.load(f)
    deck_name = deck.get('name', os.path.basename(df))
    
    # Check leader field
    leader_id = deck.get('leader', '')
    if not leader_id:
        issues.append(f'卡组 {deck_name} 缺少 leader 字段')
    elif leader_id not in all_card_ids:
        issues.append(f'卡组 {deck_name}: leader "{leader_id}" 不存在于卡牌库')
    elif not all_cards.get(leader_id, {}).get('leader'):
        issues.append(f'卡组 {deck_name}: leader "{leader_id}" 不是统领卡')
    
    # Check cards map
    cards_map = deck.get('cards', {})
    if not isinstance(cards_map, dict):
        issues.append(f'卡组 {deck_name}: cards 不是 dict 格式')
        continue
    
    missing = []
    total_cards = 0
    for cid, count in cards_map.items():
        if cid not in all_card_ids:
            missing.append(cid)
        if not isinstance(count, int) or count <= 0:
            warnings.append(f'卡组 {deck_name}: {cid} 数量异常 ({count})')
        total_cards += count
    
    if missing:
        issues.append(f'卡组 {deck_name}: 引用不存在的卡牌 {missing}')
    
    # Deck size include leader = total_cards + 1
    deck_total = total_cards + 1  # +1 for leader
    dmin, dmax = bal.get('deckMin', 60), bal.get('deckMax', 80)
    
    size_ok = dmin <= deck_total <= dmax
    print(f'  {deck_name}: leader={leader_id}, {len(cards_map)} 种卡 × 数量 = {total_cards} 张 + 1 统领 = {deck_total} 总 ({dmin}-{dmax}) {"OK" if size_ok else "FAIL"}')
    if not size_ok:
        issues.append(f'卡组 {deck_name}: 总张数 {deck_total} 超出范围 [{dmin}, {dmax}]')

# ===== 5. CHECK neutral / other faction references =====
print()
print('=== 阵营卡牌分析 ===')
faction_cards = {}
for cid, c in all_cards.items():
    f = c.get('faction', '未知')
    faction_cards.setdefault(f, []).append(cid)
for f, ids in sorted(faction_cards.items()):
    leader_ids = [i for i in ids if all_cards[i].get('leader')]
    print(f'  {f}: {len(ids)} 张卡 (统领: {leader_ids})')

# ===== 6. CHECK data/art/ =====
print()
print('=== 卡图资源 ===')
art_dir = 'data/art'
if os.path.exists(art_dir):
    art_files = [f for f in os.listdir(art_dir) if f.endswith('.png')]
    print(f'  data/art/: {len(art_files)} 张 PNG 卡图')
    # Check if art files match card IDs
    art_ids = {f.replace('.png', '') for f in art_files}
    matched = art_ids & all_card_ids
    unmatched_art = art_ids - all_card_ids
    print(f'  匹配卡牌ID的: {len(matched)} 张')
    if unmatched_art:
        info.append(f'卡图无对应卡牌: {unmatched_art}')
else:
    warnings.append('data/art/ 目录不存在')

# ===== 7. CHECK docs =====
print()
print('=== 文档检查 ===')
for df in ['docs/RULES.md', 'docs/DESIGN.md', 'docs/BALANCE.md', 'docs/AI_WORKFLOW.md']:
    if os.path.exists(df):
        sz = os.path.getsize(df)
        print(f'  {df}: {sz} bytes')
        if sz < 500:
            warnings.append(f'文档过短: {df}')
    else:
        issues.append(f'缺失文档: {df}')

# ===== 8. CHECK Runtime Design Kit =====
print()
print('=== Runtime Design Kit ===')
todo_path = 'design/runtime-kit-v1.30/manifests/IMPLEMENTATION_TODO.csv'
if os.path.exists(todo_path):
    with open(todo_path, 'r', encoding='utf-8') as f:
        lines = [l.strip() for l in f.readlines() if l.strip()]
    p0_count = sum(1 for l in lines if l.startswith('P0'))
    p1_count = sum(1 for l in lines if l.startswith('P1'))
    p2_count = sum(1 for l in lines if l.startswith('P2'))
    p3_count = sum(1 for l in lines if l.startswith('P3'))
    print(f'  待实现: P0={p0_count}, P1={p1_count}, P2={p2_count}, P3={p3_count}')
    print(f'  注: IMPLEMENTATION_TODO.csv 是整个 Design Kit 的待办，不等于当前项目缺失')
else:
    issues.append('IMPLEMENTATION_TODO.csv 缺失')

# Check contracts exist
contracts_dir = 'design/runtime-kit-v1.30/contracts'
if os.path.exists(contracts_dir):
    contract_files = os.listdir(contracts_dir)
    print(f'  contracts/: {len(contract_files)} 个文件')

# ===== 9. Web frontend check =====
print()
print('=== Web 前端检查 ===')
for wf in ['web/index.html', 'web/app.js', 'web/config.js', 'web/style.css']:
    if os.path.exists(wf):
        print(f'  OK: {wf} ({os.path.getsize(wf)} bytes)')
    else:
        issues.append(f'Web 文件缺失: {wf}')

# Check skin dir
if os.path.exists('web/skin'):
    skin_files = os.listdir('web/skin')
    print(f'  web/skin/: {len(skin_files)} 个文件')
else:
    info.append('web/skin/ 目录不存在')

# ===== SUMMARY =====
print()
print('=' * 60)
print(f'检测完成: {len(issues)} 个 ERROR, {len(warnings)} 个 WARN, {len(info)} 个 INFO')
print('=' * 60)

if issues:
    print()
    print('### ERROR (需修复) ###')
    for i, iss in enumerate(issues, 1):
        print(f'  [{i}] {iss}')

if warnings:
    print()
    print('### WARNING (建议关注) ###')
    for i, w in enumerate(warnings, 1):
        print(f'  [{i}] {w}')

if info:
    print()
    print('### INFO (供参考) ###')
    for i, inf in enumerate(info, 1):
        print(f'  [{i}] {inf}')

if not issues and not warnings:
    print()
    print('  未发现任何问题，项目数据质量良好！')
else:
    print()

sys.exit(min(len(issues), 127))
