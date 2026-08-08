#!/usr/bin/env python3
"""DeepSeek Test Lead: 项目合理性检测脚本 v1
不修改任何生产文件，只输出检测报告。"""
import json, os, sys, glob

issues = []
warnings = []

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
        print(f'  OK: {fp}')
    except Exception as e:
        print(f'  FAIL: {fp} - {e}')
        issues.append(f'JSON 解析失败: {fp}: {e}')

# ===== 2. CHECK balance.json FIELDS =====
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
        print(f'  MISSING: {k}')
        issues.append(f'balance.json 缺少键: {k}')
    else:
        print(f'  OK: {k} = {bal[k]}')

# Extra: type/range checks
if bal.get('openingHand', 0) < 3:
    warnings.append(f'balance.json: openingHand={bal["openingHand"]} 偏小，可能影响起手体验')
if bal.get('handLimit', 0) > 12:
    warnings.append(f'balance.json: handLimit={bal["handLimit"]} 偏大')
if bal.get('chainLimit', 0) < 10:
    warnings.append(f'balance.json: chainLimit={bal["chainLimit"]} 偏低，惩罚连锁可能被过早截断')
if bal.get('deckMin', 0) >= bal.get('deckMax', 0):
    issues.append(f'balance.json: deckMin({bal["deckMin"]}) >= deckMax({bal["deckMax"]})')

# ===== 3. CHECK ALL CARD FILES =====
print()
print('=== 卡牌数据完整性 ===')
card_files = sorted(glob.glob('data/cards/*.json'))
all_cards = {}
all_card_ids = set()
card_issues = 0

for cf in card_files:
    with open(cf, 'r', encoding='utf-8') as f:
        cards = json.load(f)
    if not isinstance(cards, list):
        issues.append(f'卡牌文件不是数组: {cf}')
        continue
    print(f'  {os.path.basename(cf)}: {len(cards)} 张卡')
    for c in cards:
        cid = c.get('id', '')
        if not cid:
            print(f'    FAIL: 缺少 id 字段: {c.get("name", "?")}')
            issues.append(f'卡牌缺少 id: {cf} - {c.get("name", "?")}')
            card_issues += 1
            continue
        if cid in all_card_ids:
            print(f'    FAIL: 重复的卡牌ID: {cid}')
            issues.append(f'重复卡牌 ID: {cid}')
            card_issues += 1
        all_card_ids.add(cid)
        all_cards[cid] = c
        for req in ['id', 'name', 'faction', 'type', 'punish']:
            if req not in c:
                print(f'    WARN: {cid} 缺少字段 {req}')
                warnings.append(f'卡牌 {cid} 缺少字段 {req}')
        if c.get('leader'):
            ld = c.get('leaderDef', {})
            if not ld.get('winCondition'):
                print(f'    WARN: 统领 {cid} 缺少 winCondition')
                warnings.append(f'统领 {cid} 缺少 winCondition')
            if 'enterEffects' not in ld:
                print(f'    WARN: 统领 {cid} 缺少 enterEffects')
                warnings.append(f'统领 {cid} 缺少 enterEffects')
        if c.get('type') == 'MINION':
            for a in ['attack', 'health']:
                if a not in c:
                    print(f'    WARN: 随从 {cid} 缺少 {a}')
                    warnings.append(f'随从 {cid} 缺少 {a}')
        # Check punish value range
        if isinstance(c.get('punish'), (int, float)) and c['punish'] < 0:
            print(f'    WARN: {cid} punish={c["punish"]} 为负数')
            warnings.append(f'卡牌 {cid} punish 值为负数')

print(f'  总计: {len(all_card_ids)} 张唯一卡牌, {card_issues} 个问题')

# ===== 4. CHECK DECK REFERENCES =====
print()
print('=== 卡组引用一致性 ===')
deck_files = sorted(glob.glob('data/decks/*.json'))

for df in deck_files:
    with open(df, 'r', encoding='utf-8') as f:
        deck = json.load(f)
    deck_name = deck.get('name', os.path.basename(df))
    entries = deck.get('cards', deck.get('entries', []))
    card_ids_in_deck = []
    for e in entries:
        if isinstance(e, str):
            card_ids_in_deck.append(e)
        elif isinstance(e, dict):
            card_ids_in_deck.append(e.get('id', e.get('cardId', '')))

    missing = [cid for cid in card_ids_in_deck if cid not in all_card_ids]
    if missing:
        print(f'  FAIL: {deck_name} - 引用了不存在的卡牌: {missing}')
        issues.append(f'卡组引用不存在卡牌: {deck_name} -> {missing}')
    else:
        print(f'  OK: {deck_name} - {len(card_ids_in_deck)} 张卡, 全部有效')

    leader_count = sum(1 for cid in card_ids_in_deck
                       if cid in all_cards and all_cards[cid].get('leader'))
    if leader_count == 0:
        print(f'  WARN: {deck_name} 没有统领牌!')
        warnings.append(f'卡组缺少统领: {deck_name}')
    elif leader_count > 1:
        print(f'  WARN: {deck_name} 有 {leader_count} 张统领牌(应只有 1 张)')
        issues.append(f'卡组统领过多({leader_count}): {deck_name}')

# ===== 5. CHECK DECK SIZE =====
print()
print('=== 卡组大小检查 ===')
for df in deck_files:
    with open(df, 'r', encoding='utf-8') as f:
        deck = json.load(f)
    deck_name = deck.get('name', os.path.basename(df))
    entries = deck.get('cards', deck.get('entries', []))
    sz = len(entries)
    dmin, dmax = bal.get('deckMin', 60), bal.get('deckMax', 80)
    if sz < dmin:
        print(f'  FAIL: {deck_name} 只有 {sz} 张, 至少需要 {dmin}')
        issues.append(f'卡组过小: {deck_name} ({sz} < {dmin})')
    elif sz > dmax:
        print(f'  FAIL: {deck_name} 有 {sz} 张, 最多允许 {dmax}')
        issues.append(f'卡组过大: {deck_name} ({sz} > {dmax})')
    else:
        print(f'  OK: {deck_name} - {sz} 张 (范围 {dmin}-{dmax})')

# ===== 6. CHECK duplications across factions =====
print()
print('=== 跨阵营 ID 前缀检查 ===')
prefix_map = {}
for cid in all_card_ids:
    parts = cid.split('_', 1)
    prefix = parts[0] if len(parts) > 1 else cid
    prefix_map.setdefault(prefix, []).append(cid)

# Check neutral cards don't appear in faction decks' expected prefixes
faction_prefixes = {'flame', 'machine', 'sea', 'wood', 'neutral'}
for cid in all_card_ids:
    parts = cid.split('_', 1)
    pref = parts[0] if len(parts) > 1 else ''
    if pref not in faction_prefixes and pref:
        print(f'  NOTE: 非标准前缀: {cid}')
print(f'  各前缀卡牌数: { {k: len(v) for k, v in sorted(prefix_map.items())} }')

# ===== 7. CHECK ui.json =====
print()
print('=== ui.json 检查 ===')
if os.path.exists('data/ui.json'):
    with open('data/ui.json', 'r', encoding='utf-8') as f:
        ui = json.load(f)
    print(f'  OK: ui.json 包含 {len(ui) if isinstance(ui, dict) else "N/A"} 个顶层键')
else:
    print('  WARN: data/ui.json 不存在')
    warnings.append('data/ui.json 不存在')

# ===== 8. CHECK data/art/ directory =====
print()
print('=== 卡图资源检查 ===')
art_dir = 'data/art'
cardart_dir = 'data/cardart'
for d in [art_dir, cardart_dir]:
    if os.path.exists(d):
        files = [f for f in os.listdir(d) if not f.endswith('.txt') and not f.startswith('.')]
        print(f'  {d}: {len(files)} 个文件')
    else:
        print(f'  NOTE: {d} 目录不存在或为空')

# ===== 9. CHECK docs exist and have content =====
print()
print('=== 文档完整性 ===')
doc_files = ['docs/RULES.md', 'docs/DESIGN.md', 'docs/BALANCE.md',
             'docs/CHANGELOG_CASTLE.md', 'docs/AI_WORKFLOW.md', 'docs/FILE_INDEX.md']
for df in doc_files:
    if os.path.exists(df):
        sz = os.path.getsize(df)
        print(f'  OK: {df} ({sz} bytes)')
        if sz < 100:
            warnings.append(f'文档过短: {df} ({sz} bytes)')
    else:
        print(f'  MISSING: {df}')
        issues.append(f'缺失文档: {df}')

# ===== 10. CHECK IMPLEMENTATION_TODO =====
print()
print('=== Runtime Design Kit 实现清单 ===')
todo_path = 'design/runtime-kit-v1.30/manifests/IMPLEMENTATION_TODO.csv'
if os.path.exists(todo_path):
    with open(todo_path, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    print(f'  OK: {len(lines)-1} 行任务 (不计表头)')
    for line in lines[1:]:
        parts = line.strip().split(',')
        if len(parts) >= 3:
            print(f'    [{parts[0]}] {parts[1]}: {parts[2]}')
else:
    print(f'  MISSING: {todo_path}')
    issues.append('IMPLEMENTATION_TODO.csv 缺失')

# ===== SUMMARY =====
print()
print('=' * 60)
print(f'检测完成')
print(f'  ERROR 级问题: {len(issues)}')
print(f'  WARN 级问题:  {len(warnings)}')
print('=' * 60)

if issues:
    print()
    print('### ERROR 级别 (需修复) ###')
    for i, iss in enumerate(issues, 1):
        print(f'  [{i}] {iss}')

if warnings:
    print()
    print('### WARNING 级别 (建议关注) ###')
    for i, w in enumerate(warnings, 1):
        print(f'  [{i}] {w}')

if not issues and not warnings:
    print()
    print('  未发现任何问题，项目数据质量良好！')

# Return code = number of errors
sys.exit(min(len(issues), 127))
