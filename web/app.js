/* ================= 统御战纪 网页版客户端 P3.1 =================
 * 引擎在本地 Java 服务（唯一规则权威）；本文件只是视图与操作层。
 * 外观与节奏参数见 web/config.js。 */
"use strict";

const $ = (id) => document.getElementById(id);

/* ---------- 参数 ---------- */
const CFG = Object.assign({
  pollMs: 320, bannerMs: 1500, dmgPopMs: 1000, flipMs: 350, hitFlashMs: 500, toastMs: 2200,
  turnBannerMs: 1600, defaultLang: "zh", tutorialAutoShow: true, cssVars: {},
}, window.DW_CONFIG || {});
for (const [k, v] of Object.entries(CFG.cssVars)) document.documentElement.style.setProperty(k, v);

/* ---------- 文案（三语） ---------- */
const STR = {
  zh: { subtitle:"以惩罚为代价的对决 —— 击败对方统领者胜", yourDeck:"你的卡组", aiDeck:"AI 卡组",
    startPvE:"开始对战", watch:"AI 观战", tutorial:"新手教程", language:"界面语言",
    skipAmbush:"跳过伏击阶段", endTurn:"结束回合", log:"对战日志", skip:"放弃", again:"再来一局",
    backLobby:"返回", next:"下一步", skipTut:"跳过", turn:"回合",
    phases:{AMBUSH:"伏击",ACTION:"行动",DISCARD:"弃牌",END:"结束"},
    phase:{AMBUSH:"伏击阶段",ACTION:"行动阶段",DISCARD:"弃牌阶段",END:"结束",OVER:"对局结束"},
    yourTurn:"你的回合", enemyTurn:"对方回合",
    castle:"共享王城", castleBroken:"王城已被击破", win:"胜 利", lose:"战 败", watchOver:"对局结束",
    deck:"卡组", hand:"手牌", grave:"墓地", life:"生命", victory:"胜利计数", goal:"目标", goalTurn:"本回合",
    leaderAbsent:"统领未登场", dur:"耐久", suppressed:"被压制", ambushZone:"伏击", hiddenAmbush:"对方伏击（内容隐藏）",
    fizzleAsk:"惩罚值超过对方卡组余量，此牌将【空发】（计使用/耗词条/不结算）并强制结束回合。仍要打出吗？",
    emptyField:"战 场 为 空",
    types:{MINION:"随从",SPELL:"咒文",AMBUSH:"伏击",PUNISH:"惩罚"},
    ambushKinds:{NORMAL:"普通",FOCUS:"专注",LOCKDOWN:"封场"}, chant:"吟唱" },
  ja: { subtitle:"ペナルティを代償とする決闘 —— 敵統領を倒せ", yourDeck:"自分のデッキ", aiDeck:"AIデッキ",
    startPvE:"対戦開始", watch:"AI観戦", tutorial:"チュートリアル", language:"言語",
    skipAmbush:"伏撃をスキップ", endTurn:"ターン終了", log:"対戦ログ", skip:"パス", again:"もう一局",
    backLobby:"戻る", next:"次へ", skipTut:"スキップ", turn:"ターン",
    phases:{AMBUSH:"伏撃",ACTION:"行動",DISCARD:"捨て札",END:"終了"},
    phase:{AMBUSH:"伏撃フェイズ",ACTION:"行動フェイズ",DISCARD:"捨て札フェイズ",END:"終了",OVER:"対局終了"},
    yourTurn:"あなたのターン", enemyTurn:"相手のターン",
    castle:"共有王城", castleBroken:"王城陥落", win:"勝 利", lose:"敗 北", watchOver:"対局終了",
    deck:"デッキ", hand:"手札", grave:"墓地", life:"ライフ", victory:"勝利カウント", goal:"目標", goalTurn:"このターン",
    leaderAbsent:"統領未登場", dur:"耐久", suppressed:"抑制中", ambushZone:"伏撃", hiddenAmbush:"相手の伏撃（非公開）",
    fizzleAsk:"ペナルティが相手デッキ残量を超えています。【不発】となりターンを強制終了します。使用しますか？",
    emptyField:"戦 場 は 空",
    types:{MINION:"ミニオン",SPELL:"呪文",AMBUSH:"伏撃",PUNISH:"ペナルティ"},
    ambushKinds:{NORMAL:"通常",FOCUS:"集中",LOCKDOWN:"封鎖"}, chant:"詠唱" },
  en: { subtitle:"A duel paid by punishment — defeat the enemy leader", yourDeck:"Your deck", aiDeck:"AI deck",
    startPvE:"Start Duel", watch:"AI Demo", tutorial:"Tutorial", language:"Language",
    skipAmbush:"Skip Ambush", endTurn:"End Turn", log:"Duel Log", skip:"Pass", again:"Play Again",
    backLobby:"Back", next:"Next", skipTut:"Skip", turn:"Turn",
    phases:{AMBUSH:"Ambush",ACTION:"Action",DISCARD:"Discard",END:"End"},
    phase:{AMBUSH:"Ambush Phase",ACTION:"Action Phase",DISCARD:"Discard Phase",END:"End",OVER:"Game Over"},
    yourTurn:"YOUR TURN", enemyTurn:"ENEMY TURN",
    castle:"Royal Castle", castleBroken:"Castle Broken", win:"VICTORY", lose:"DEFEAT", watchOver:"Game Over",
    deck:"Deck", hand:"Hand", grave:"Grave", life:"Life", victory:"Victory", goal:"Goal", goalTurn:"this turn",
    leaderAbsent:"No Leader", dur:"DUR", suppressed:"Suppressed", ambushZone:"Ambush", hiddenAmbush:"Enemy ambush (hidden)",
    fizzleAsk:"Punish exceeds the enemy deck. The card will FIZZLE and end your turn. Play anyway?",
    emptyField:"E M P T Y",
    types:{MINION:"Minion",SPELL:"Spell",AMBUSH:"Ambush",PUNISH:"Punish"},
    ambushKinds:{NORMAL:"Normal",FOCUS:"Focus",LOCKDOWN:"Lockdown"}, chant:"Chant" },
};
let L = STR[localStorage.dwLang || CFG.defaultLang] || STR.zh;
function applyI18n() {
  document.querySelectorAll("[data-i18n]").forEach(el => {
    const v = L[el.dataset.i18n];
    if (typeof v === "string") el.textContent = v;
  });
}

/* ---------- 全局状态 ---------- */
let S = null, lastVersion = -1, polling = null;
let selected = null;          // 选中的攻击者 uid
let drag = null;
let answeredId = -1;
let prevCurrent = -1, prevTurn = -1;
let gameOverShown = false;
const factionClass = f => ({"烈焰帝国":"f-flame","深海联盟":"f-sea","古木圣地":"f-wood","机械遗迹":"f-machine"}[f] || "f-neutral");

/* ---------- API ---------- */
async function api(path, body) {
  const r = await fetch(path, body ? { method: "POST", body: JSON.stringify(body) } : undefined);
  return r.json();
}
async function cmd(body) {
  const r = await api("/api/cmd", body);
  if (r && r.ok === false) toast(r.msg);
  return r;
}

/* ---------- 大厅 ---------- */
async function initLobby() {
  const decks = await api("/api/decks");
  for (const sel of [$("deckA"), $("deckB")]) {
    sel.innerHTML = "";
    decks.forEach(d => {
      const o = document.createElement("option");
      o.value = d.file; o.textContent = `${d.name}（${d.faction}）`;
      sel.appendChild(o);
    });
  }
  if (decks.length > 1) $("deckB").selectedIndex = 1;
}
async function startGame(mode) {
  const r = await api("/api/new", { deckA: $("deckA").value, deckB: $("deckB").value, mode });
  if (r.ok === false) { toast(r.msg); return; }
  lastVersion = -1; selected = null; answeredId = -1; prevCurrent = -1; prevTurn = -1; gameOverShown = false;
  $("lobby").classList.add("hidden");
  $("board").classList.remove("hidden");
  $("gameover").classList.add("hidden");
  if (CFG.tutorialAutoShow && !localStorage.dwTutorialDone && mode === "pve") setTimeout(startTutorial, 900);
}
function backLobby() {
  ["board", "gameover", "modal", "tut"].forEach(id => $(id).classList.add("hidden"));
  $("lobby").classList.remove("hidden");
}

/* ---------- 轮询 ---------- */
async function poll() {
  try {
    const s = await api("/api/state");
    if (s.none || s.version === undefined || s.version === lastVersion) return;
    const prevRects = captureRects();
    S = s; lastVersion = s.version;
    if ($("board").classList.contains("hidden")) return;   // 在大厅时不渲染对局弹窗
    render();
    playFlip(prevRects);
    playEvents(s.events || []);
    renderPending();
    announceTurn();
    if (s.over) showGameOver();
  } catch (e) { /* 服务未就绪 */ }
}
function myIdx() { return S.humanIdx >= 0 ? S.humanIdx : 0; }
function isMyTurn() { return S && !S.over && S.humanIdx >= 0 && S.current === S.humanIdx; }

/* ---------- 渲染 ---------- */
function render() {
  const me = myIdx(), opp = 1 - me;
  const P = S.players;
  $("phaseInfo").textContent =
    `${L.turn} ${S.turn} ｜ ${P[S.current].name} ｜ ${L.phase[S.phase] || S.phase}` +
    (S.over ? ` ｜ ${L.phase.OVER}` : "");

  // 王城
  const c = S.castle;
  $("castle").style.visibility = c.enabled ? "visible" : "hidden";
  if (c.enabled) {
    const pct = Math.max(0, c.hp / c.max * 100);
    const fill = $("castleFill");
    fill.style.width = pct + "%";
    fill.classList.toggle("low", pct <= 50 && pct > 25);
    fill.classList.toggle("crit", pct <= 25);
    $("castleText").textContent = c.hp > 0
      ? `${L.castle}  ${c.hp} / ${c.max}`
      : `${L.castleBroken} — ${P[c.breaker]?.name ?? ""}`;
  }

  renderPlate($("oppBar"), P[opp], opp);
  renderPlate($("myBar"), P[me], me);
  renderLeaderZone($("oppLeaderZone"), P[opp], false);
  renderLeaderZone($("myLeaderZone"), P[me], true);
  renderRow($("oppRow"), P[opp], false);
  renderRow($("myRow"), P[me], true);
  renderPiles($("oppPiles"), P[opp]);
  renderPiles($("myPiles"), P[me]);
  renderHand(P[me]);
  renderPhaseTrack();

  $("btnSkipAmbush").classList.toggle("hidden", !(isMyTurn() && S.phase === "AMBUSH"));
  $("btnEndTurn").disabled = !(isMyTurn() && (S.phase === "AMBUSH" || S.phase === "ACTION"));

  const log = $("log");
  log.innerHTML = "";
  (S.logs || []).forEach(ln => {
    const d = document.createElement("div");
    d.textContent = ln;
    if (ln.includes("王城") || ln.includes("获胜") || ln.includes("触发")) d.className = "hl";
    log.appendChild(d);
  });
  log.scrollTop = log.scrollHeight;
}

function renderPhaseTrack() {
  const tr = $("phaseTrack");
  tr.innerHTML = "";
  ["AMBUSH", "ACTION", "DISCARD", "END"].forEach(ph => {
    const d = document.createElement("span");
    d.className = "ph" + (S.phase === ph ? " cur" : "");
    d.textContent = L.phases[ph];
    tr.appendChild(d);
  });
}

function renderPlate(el, p, idx) {
  el.classList.toggle("active", !S.over && S.current === idx);
  let goal = "";
  if (p.goal) {
    const close = p.goal.cur * 3 >= p.goal.max * 2;
    const tag = p.goal.kind === "OPP_PUNISH_DRAW_TURN_GE" ? `(${L.goalTurn})` : "";
    goal = `<span class="goal ${close ? "close" : ""}" title="${esc(p.goal.text)}">${L.goal}${tag} ${p.goal.cur}/${p.goal.max}</span>`;
  }
  let pips = "";
  for (let i = 0; i < p.victoryMax; i++) pips += `<span class="pip ${i < p.victory ? "on" : ""}"></span>`;
  el.innerHTML =
    `<span class="pname">${esc(p.name)}</span>` +
    `<span class="stat">${L.hand} <b>${p.handCount}</b></span>` +
    (p.life != null ? `<span class="stat life">${L.life} <b>${p.life}</b></span>` : "") +
    goal +
    `<span class="pips" title="${L.victory}">${pips}</span>`;
}

/* 统领区：非随从统领立牌；随从统领/未登场显示状态 */
function renderLeaderZone(zone, p, self) {
  zone.innerHTML = "";
  const box = document.createElement("div");
  const ld = p.leader;
  const entity = (p.field || []).find(cd => cd.leader && cd.atk == null);  // 非随从统领实体
  if (ld) {
    box.className = "leaderbox";
    const cardLike = entity || (p.field || []).find(cd => cd.leader);
    const art = cardLike && cardLike.art
      ? `<div class="lz-art" style="background-image:url('/art/${cardLike.id}.png')"></div>`
      : `<div class="lz-art">${cardLike ? artSvg(cardLike, true) : ""}</div>`;
    let sub = ld.minion ? `${ld.atk}/${ld.hp}` : (ld.dur ? `${L.dur} ${ld.dur}` : "");
    if (ld.disabled) sub += `（${L.suppressed}）`;
    box.innerHTML = art +
      (ld.dur && !ld.minion ? `<div class="lz-dur">${ld.dur}</div>` : "") +
      `<div class="lz-name">★ ${esc(ld.name)}</div><div class="lz-sub">${esc(sub)}</div>`;
    // 敌方非随从统领可作为攻击目标（拖线或点击，由服务端裁决合法性）
    if (!self && entity) {
      if (selected != null && isMyTurn() && S.phase === "ACTION") box.classList.add("targetable");
      box.dataset.uid = entity.uid;
      box.addEventListener("pointerup", () => {
        if (selected != null) { cmd({ type: "attack", uid: selected, target: entity.uid }); selected = null; }
      });
    }
  } else {
    box.className = "leaderbox absent";
    box.innerHTML = `<div class="lz-name">★ ${L.leaderAbsent}</div>`;
  }
  zone.appendChild(box);
}

function renderPiles(el, p) {
  el.innerHTML =
    `<div class="pile"><div class="plabel">${L.deck}</div>
       <div class="stack"></div><div class="stack"></div><div class="stack"></div>
       <div class="pcount">${p.deck}</div></div>
     <div class="pile grave"><div class="plabel">${L.grave}</div>
       <div class="stack"></div><div class="pcount">${p.grave}</div></div>`;
}

function renderRow(row, p, self) {
  row.innerHTML = "";
  // 非随从统领实体放在统领区，不在战场行重复显示
  const cards = (p.field || []).filter(cd => !(cd.leader && cd.atk == null));
  if (!cards.length) {
    const e = document.createElement("span"); e.className = "empty"; e.textContent = L.emptyField;
    row.appendChild(e);
  }
  cards.forEach(cd => row.appendChild(makeCard(cd, { zone: self ? "myField" : "oppField" })));
  const ambushes = p.ambushes, count = p.ambushCount ?? (ambushes ? ambushes.length : 0);
  if (count > 0) {
    const sep = document.createElement("span"); sep.className = "ambush-sep"; sep.textContent = L.ambushZone;
    row.appendChild(sep);
    if (ambushes) ambushes.forEach(cd => row.appendChild(makeCard(cd, { zone: "ambush", small: true })));
    else for (let i = 0; i < count; i++) {
      const b = document.createElement("div"); b.className = "card small back"; b.title = L.hiddenAmbush;
      row.appendChild(b);
    }
  }
}

function renderHand(p) {
  const hand = $("hand");
  hand.innerHTML = "";
  (p.hand || []).forEach(cd => hand.appendChild(makeCard(cd, { zone: "hand" })));
}

function makeCard(cd, opt) {
  const el = document.createElement("div");
  el.className = `card ${factionClass(cd.faction)} ${opt.small ? "small" : ""}`;
  el.dataset.uid = cd.uid;
  if (cd.leader) el.classList.add("leader");
  const my = isMyTurn();
  if (opt.zone === "hand") {
    if (my && cd.usable) el.classList.add("usable");
    else if (my && S.phase === "AMBUSH" && cd.type !== "AMBUSH") el.classList.add("idle"); // 伏击阶段非伏击牌：仅暗示
    else el.classList.add("dim");
    if (cd.fizzle) el.classList.add("fizzle");
    el.title = cd.usable ? (cd.text || "") : (cd.why || "");
  }
  if (opt.zone === "myField") {
    if (my && S.phase === "ACTION" && cd.canAttack) el.classList.add("canatk");
    if (selected === cd.uid) el.classList.add("selected");
  }
  if (opt.zone === "oppField" && selected != null && my && S.phase === "ACTION") {
    el.classList.add("targetable");
  }
  const crown = cd.leader ? `<span class="crown">★ </span>` : "";
  const artStyle = cd.art ? ` style="background-image:url('/art/${cd.id}.png')"` : "";
  const typeName = (L.types[cd.type] || cd.type) + (cd.ambushKind ? "·" + (L.ambushKinds[cd.ambushKind] || cd.ambushKind) : "");
  let gems = "";
  if (cd.atk != null) {
    const atkCls = cd.atk > cd.baseAtk ? "buffed" : "";
    const hpCls = cd.hp < cd.baseHp ? "hurt" : cd.hp > cd.baseHp ? "buffed" : "";
    gems = `<div class="gem atk"><span class="${atkCls}">${cd.atk}</span></div>` +
           `<div class="gem hp"><span class="${hpCls}">${cd.hp}</span></div>`;
  } else if (cd.dur != null) {
    gems = `<div class="gem dur">${cd.dur}</div>`;
  }
  el.innerHTML =
    `<div class="art"${artStyle}>${cd.art ? "" : artSvg(cd)}</div>` +
    `<div class="cname">${crown}${esc(cd.name)}</div>` +
    `<div class="punish">${cd.punish}</div>` +
    `<div class="typebar t-${cd.type}">${esc(typeName)}</div>` +
    (cd.chant ? `<div class="chant">${L.chant}${cd.chant}</div>` : "") +
    (cd.kw && cd.kw.length ? `<div class="kws">${cd.kw.map(k => `<span class="kw">${esc(k)}</span>`).join("")}</div>` : "") +
    `<div class="ctext">${esc(cd.text || "")}</div>` +
    gems +
    (cd.shield ? `<div class="shield"></div>` : "");
  bindCardInput(el, cd, opt);
  return el;
}

/* 程序化纹章（无卡图时；深色饱和版） */
function artSvg(cd, big) {
  const f = cd.faction, h = hashCode(cd.id);
  const r = (i) => { const v = Math.sin(h * 9301 + i * 49297) * 233280; return v - Math.floor(v); };
  let inner = "", bg1, bg2;
  if (f === "烈焰帝国") {
    bg1 = "#3a0e06"; bg2 = "#120402";
    for (let i = 0; i < 5; i++) {
      const x = 10 + 92 * r(i), w = 7 + 12 * r(i + 9), hh = 24 + 30 * r(i + 5);
      inner += `<path d="M${x - w} 60 Q${x - w * 1.5} ${60 - hh * .5} ${x} ${60 - hh} Q${x + w * 1.5} ${60 - hh * .5} ${x + w} 60 Z" fill="${i % 2 ? "#ff9a3c" : "#d8431b"}" opacity=".95"/>`;
    }
    inner += `<circle cx="56" cy="44" r="8" fill="#ffd98a"/>`;
  } else if (f === "深海联盟") {
    bg1 = "#06243f"; bg2 = "#020c18";
    for (let i = 0; i < 3; i++) {
      const y = 20 + i * 13, a = 5 + 4 * r(i);
      inner += `<path d="M0 ${y} Q 14 ${y - a} 28 ${y} T 56 ${y} T 84 ${y} T 112 ${y} V64 H0 Z" fill="${i % 2 ? "#46c2e0" : "#1568a0"}" opacity="${.5 + i * .2}"/>`;
    }
    for (let i = 0; i < 4; i++)
      inner += `<circle cx="${8 + 96 * r(i + 20)}" cy="${6 + 30 * r(i + 30)}" r="${1.5 + 2 * r(i + 40)}" fill="none" stroke="#bfeaf5" stroke-width="1" opacity=".8"/>`;
  } else if (f === "古木圣地") {
    bg1 = "#0c2410"; bg2 = "#030c05";
    inner = `<path d="M53 64 V30 M53 44 L38 30 M53 38 L70 24" stroke="#4a3018" stroke-width="6" stroke-linecap="round" fill="none"/>`;
    for (let i = 0; i < 5; i++) {
      const x = 22 + 68 * r(i), y = 8 + 22 * r(i + 3), rr = 9 + 9 * r(i + 7);
      inner += `<circle cx="${x}" cy="${y}" r="${rr}" fill="${i % 2 ? "#8fce58" : "#3f7d37"}" opacity=".95"/>`;
    }
  } else if (f === "机械遗迹") {
    bg1 = "#1a1f29"; bg2 = "#0a0d13";
    for (let i = 0; i < 2; i++) {
      const cx = 34 + 46 * i + 8 * r(i), cy = 30 + 8 * r(i + 4), rr = 14 + 7 * r(i + 2);
      let spokes = "";
      for (let t = 0; t < 8; t++) {
        const a = t * Math.PI / 4 + r(i + 8);
        spokes += `<line x1="${cx}" y1="${cy}" x2="${cx + Math.cos(a) * rr}" y2="${cy + Math.sin(a) * rr}" stroke="${i ? "#aecbe0" : "#5f7287"}" stroke-width="4.5"/>`;
      }
      inner += spokes + `<circle cx="${cx}" cy="${cy}" r="${rr * .62}" fill="none" stroke="${i ? "#aecbe0" : "#5f7287"}" stroke-width="3"/><circle cx="${cx}" cy="${cy}" r="${rr * .24}" fill="#0d1016"/>`;
    }
    inner += `<line x1="0" y1="10" x2="112" y2="10" stroke="#46e0d4" stroke-width="1" opacity=".5"/>`;
  } else {
    bg1 = "#241537"; bg2 = "#0c0614";
    inner = `<circle cx="56" cy="32" r="18" fill="none" stroke="#b794e8" stroke-width="2" opacity=".8"/>` +
            `<circle cx="56" cy="32" r="10" fill="#b794e8"/>`;
  }
  const vb = big ? "0 0 112 80" : "0 0 112 64";
  return `<svg viewBox="${vb}" width="100%" height="100%" preserveAspectRatio="xMidYMid slice">
    <defs><radialGradient id="g${h}" cx="50%" cy="40%"><stop offset="0%" stop-color="${bg1}"/><stop offset="100%" stop-color="${bg2}"/></radialGradient></defs>
    <rect width="112" height="80" fill="url(#g${h})"/>${inner}</svg>`;
}

/* ---------- 输入：统一指针处理（修复 click 被吞 bug） ----------
 * 不在 pointerdown 调 preventDefault（那会吞掉 click）；
 * 移动 < 8px 视为点击，>= 8px 进入拖拽（手牌=拖出牌 / 随从=拉攻击线）。 */
function bindCardInput(el, cd, opt) {
  if (!S || S.humanIdx < 0) return;   // 观战不可操作
  if (opt.zone === "hand") {
    el.addEventListener("pointerdown", e => { if (e.button === 0) beginDrag(e, { kind: "hand", cd, el }); });
  } else if (opt.zone === "myField") {
    el.addEventListener("pointerdown", e => { if (e.button === 0) beginDrag(e, { kind: "unit", cd, el }); });
  } else if (opt.zone === "oppField") {
    el.addEventListener("pointerup", () => {
      if (selected == null || !isMyTurn() || S.phase !== "ACTION") return;
      cmd({ type: "attack", uid: selected, target: cd.uid });
      selected = null;
    });
  }
}

async function tryUseHand(cd) {
  if (!isMyTurn()) return;
  if (S.phase === "AMBUSH") {
    if (cd.usable) cmd({ type: "ambush", uid: cd.uid });
    else if (cd.why) toast(cd.why);
  } else if (S.phase === "ACTION") {
    if (!cd.usable) { if (cd.why) toast(cd.why); return; }
    if (cd.fizzle && !confirm(L.fizzleAsk)) return;
    cmd({ type: "play", uid: cd.uid });
  }
}

function clickUnit(cd) {
  if (!isMyTurn() || S.phase !== "ACTION" || !cd.canAttack) return;
  selected = selected === cd.uid ? null : cd.uid;
  render();
}

function beginDrag(e, info) {
  if (!isMyTurn()) return;
  drag = { ...info, moved: false, x0: e.clientX, y0: e.clientY };
  window.addEventListener("pointermove", onDragMove);
  window.addEventListener("pointerup", onDragEnd, { once: true });
}

function onDragMove(e) {
  if (!drag) return;
  if (!drag.moved && Math.hypot(e.clientX - drag.x0, e.clientY - drag.y0) < 8) return;
  if (!drag.moved) {
    drag.moved = true;
    if (drag.kind === "hand") {
      const g = $("dragGhost");
      g.innerHTML = "";
      const clone = drag.el.cloneNode(true);
      clone.classList.remove("usable", "idle", "dim");
      g.appendChild(clone);
      g.classList.remove("hidden");
      drag.el.style.opacity = .25;
    }
  }
  if (drag.kind === "hand") {
    const g = $("dragGhost");
    g.style.left = e.clientX + "px"; g.style.top = e.clientY + "px";
  } else {
    const r = drag.el.getBoundingClientRect();
    drawArrow(r.left + r.width / 2, r.top + r.height / 2, e.clientX, e.clientY);
  }
}

function onDragEnd(e) {
  window.removeEventListener("pointermove", onDragMove);
  const d = drag; drag = null;
  $("dragGhost").classList.add("hidden");
  $("arrowLayer").innerHTML = "";
  if (!d) return;
  if (d.el) d.el.style.opacity = "";
  if (!d.moved) {                                  // === 点击 ===
    if (d.kind === "hand") tryUseHand(d.cd);
    else clickUnit(d.cd);
    return;
  }
  const target = document.elementFromPoint(e.clientX, e.clientY);  // === 拖拽落点 ===
  if (d.kind === "hand") {
    if (target && target.closest("#battle")) tryUseHand(d.cd);
  } else {
    const tcard = target && target.closest(".card[data-uid]");
    const lz = target && target.closest(".leaderbox[data-uid]");
    if (tcard && tcard.closest("#oppRow")) {
      cmd({ type: "attack", uid: d.cd.uid, target: parseInt(tcard.dataset.uid) });
    } else if (lz) {
      cmd({ type: "attack", uid: d.cd.uid, target: parseInt(lz.dataset.uid) });
    } else if (target && (target.closest("#castle") || target.closest("#oppBar") || target.closest("#oppLeaderZone"))) {
      cmd({ type: "attack", uid: d.cd.uid, face: true });
    }
    selected = null;
  }
}

function drawArrow(x1, y1, x2, y2) {
  $("arrowLayer").innerHTML =
    `<defs><marker id="ah" markerWidth="12" markerHeight="12" refX="7" refY="4" orient="auto">
       <path d="M0,0 L8,4 L0,8 Z" fill="var(--arrow)"/></marker></defs>
     <line x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}" stroke="var(--arrow)" stroke-width="4.5"
       stroke-dasharray="11 7" marker-end="url(#ah)" opacity=".95"/>`;
}

/* ---------- 动画 ---------- */
function captureRects() {
  const m = new Map();
  document.querySelectorAll("#board .card[data-uid]").forEach(el => m.set(el.dataset.uid, el.getBoundingClientRect()));
  return m;
}
function playFlip(prev) {
  document.querySelectorAll("#board .card[data-uid]").forEach(el => {
    const uid = el.dataset.uid, was = prev.get(uid);
    const now = el.getBoundingClientRect();
    if (!was) { el.classList.add("entering"); setTimeout(() => el.classList.remove("entering"), 400); return; }
    const dx = was.left - now.left, dy = was.top - now.top;
    if (Math.abs(dx) + Math.abs(dy) < 4) return;
    el.style.transition = "none";
    el.style.transform = `translate(${dx}px, ${dy}px)`;
    requestAnimationFrame(() => {
      el.style.transition = `transform ${CFG.flipMs}ms ease`;
      el.style.transform = "";
      setTimeout(() => { el.style.transition = ""; }, CFG.flipMs + 60);
    });
  });
}
function playEvents(events) {
  let bannerShown = false;
  events.forEach(ev => {
    if (ev.t === "hit" || ev.t === "die") {
      document.querySelectorAll("#board .card .cname").forEach(nm => {
        if (nm.textContent.replace("★ ", "") === ev.name) {
          const card = nm.parentElement;
          card.classList.add("hitflash");
          setTimeout(() => card.classList.remove("hitflash"), CFG.hitFlashMs);
          if (ev.n) dmgPop(card, "-" + ev.n);
        }
      });
    } else if (ev.t === "castleHit") {
      $("castleBar").animate(
        [{ transform: "translateX(0)" }, { transform: "translateX(-5px)" }, { transform: "translateX(5px)" }, { transform: "translateX(0)" }],
        { duration: 320 });
    } else if ((ev.t === "play" || ev.t === "ambush") && !bannerShown) {
      banner(ev.s); bannerShown = true;
    }
  });
}
function announceTurn() {
  if (S.over || S.humanIdx < 0) { prevCurrent = S.current; prevTurn = S.turn; return; }
  if (S.turn !== prevTurn || S.current !== prevCurrent) {
    if (prevTurn !== -1) {
      const tb = $("turnBanner");
      tb.textContent = S.current === S.humanIdx ? L.yourTurn : L.enemyTurn;
      tb.classList.remove("hidden");
      tb.style.animation = "none"; void tb.offsetWidth;   // 重置动画
      tb.style.animation = `turnin ${CFG.turnBannerMs}ms ease forwards`;
      setTimeout(() => tb.classList.add("hidden"), CFG.turnBannerMs);
    }
    prevCurrent = S.current; prevTurn = S.turn;
  }
}
function dmgPop(card, text) {
  const r = card.getBoundingClientRect();
  const d = document.createElement("div");
  d.className = "dmgpop"; d.textContent = text;
  d.style.left = (r.left + r.width / 2 - 14) + "px";
  d.style.top = (r.top + 6) + "px";
  document.body.appendChild(d);
  setTimeout(() => d.remove(), CFG.dmgPopMs);
}
let bannerTimer = null;
function banner(text) {
  const b = $("banner");
  b.textContent = text;
  b.classList.remove("hidden");
  clearTimeout(bannerTimer);
  bannerTimer = setTimeout(() => b.classList.add("hidden"), CFG.bannerMs);
}
let toastTimer = null;
function toast(msg) {
  const t = $("toast");
  t.textContent = msg;
  t.classList.remove("hidden");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => t.classList.add("hidden"), CFG.toastMs);
}

/* ---------- 决策弹窗 ---------- */
function renderPending() {
  const p = S.pending;
  const modal = $("modal");
  if (!p || S.humanIdx < 0) { modal.classList.add("hidden"); return; }
  if (p.id === answeredId) return;
  $("modalPrompt").textContent = p.prompt;
  const box = $("modalOptions");
  box.innerHTML = "";
  p.options.forEach(o => {
    const wrap = document.createElement("div");
    wrap.className = "opt";
    if (o.card) {
      const c = makeCard(o.card, { zone: "static" });
      c.classList.remove("dim", "usable", "idle");
      wrap.appendChild(c);
    } else {
      const b = document.createElement("div"); b.className = "corebtn"; b.textContent = o.label;
      wrap.appendChild(b);
    }
    wrap.addEventListener("click", () => answer(p.id, o.i));
    box.appendChild(wrap);
  });
  $("modalSkip").style.display = p.optional ? "" : "none";
  $("modalSkip").onclick = () => answer(p.id, -1);
  modal.classList.remove("hidden");
}
async function answer(id, choice) {
  answeredId = id;
  $("modal").classList.add("hidden");
  await cmd({ type: "answer", id, choice });
}

/* ---------- 结算 ---------- */
function showGameOver() {
  if (gameOverShown) return;
  gameOverShown = true;
  const me = S.humanIdx;
  $("gameoverTitle").textContent = me < 0 ? L.watchOver : (S.winner === me ? L.win : L.lose);
  $("gameoverReason").textContent = (S.players[S.winner]?.name ?? "") + " — " + (S.winReason || "");
  $("gameover").classList.remove("hidden");
}

/* ---------- 新手教程 ---------- */
const TUT = window.DW_TUTORIAL || {
  zh: [
    { sel: "#hand", text: "这是你的手牌。绿光的牌当前可以使用：点一下，或拖到战场上打出。左上角紫色六边形是【惩罚值】——打出这张牌，对方就会抽这么多张牌作为补偿。" },
    { sel: "#oppBar", text: "对方信息条：手牌数、胜利条件进度（Goal）、胜利计数。Goal 变红 = 对方快达成特殊胜利了，要小心！" },
    { sel: "#castleBar", text: "共享王城：双方都能攻击的公共目标。打到 0 的一方获得巨大终局优势（烈焰皇甚至直接获胜）。" },
    { sel: "#myRow", text: "你的战场。绿光随从可以攻击：按住拖出红色箭头，指向敌方单位、敌方统领立牌或王城血条。" },
    { sel: "#phaseTrack", text: "回合流程：伏击（可盖 1 张伏击牌）→ 行动（出牌与攻击）→ 弃牌 → 结束。右边的金色大按钮就是结束回合。" },
    { sel: "#logPanel", text: "对战日志记录每一步；AI 行动会逐步播放并弹出播报。祝你旗开得胜！" },
  ],
  ja: [
    { sel: "#hand", text: "あなたの手札。緑に光るカードは使用可能：クリック、または戦場へドラッグ。左上の六角形は【ペナルティ】——使用すると相手がその枚数ドローします。" },
    { sel: "#oppBar", text: "相手の情報バー：手札数、特殊勝利の進捗（目標）、勝利カウント。目標が赤くなったら要警戒！" },
    { sel: "#castleBar", text: "共有王城：双方が攻撃できる公共目標。0 にした側が大きな終盤アドバンテージを得ます。" },
    { sel: "#myRow", text: "あなたの戦場。緑に光るミニオンは攻撃可能：ドラッグで赤い矢印を敵ユニット・敵統領・王城へ。" },
    { sel: "#phaseTrack", text: "ターンの流れ：伏撃（1枚セット可）→ 行動 → 捨て札 → 終了。右の金色ボタンがターン終了です。" },
    { sel: "#logPanel", text: "対戦ログにすべて記録されます。AI の行動はステップ再生されます。健闘を！" },
  ],
  en: [
    { sel: "#hand", text: "Your hand. Green-glowing cards are playable: click, or drag onto the battlefield. The purple hexagon (top-left) is PUNISH — your opponent draws that many cards." },
    { sel: "#oppBar", text: "Enemy info bar: hand count, special-win progress (Goal), victory pips. A red Goal means danger!" },
    { sel: "#castleBar", text: "The shared Royal Castle — both sides can attack it. Breaking it grants a huge endgame edge." },
    { sel: "#myRow", text: "Your field. Glowing minions can attack: drag a red arrow to an enemy unit, enemy leader plate, or the castle bar." },
    { sel: "#phaseTrack", text: "Turn flow: Ambush (set one) → Action (play & attack) → Discard → End. The gold button ends your turn." },
    { sel: "#logPanel", text: "The duel log records everything; AI actions play out step by step. Good luck!" },
  ],
};
let tutStep = 0;
function startTutorial() {
  tutStep = 0;
  $("tut").classList.remove("hidden");
  showTutStep();
}
function showTutStep() {
  const steps = TUT[localStorage.dwLang || CFG.defaultLang] || TUT.zh;
  if (tutStep >= steps.length) { endTutorial(); return; }
  const st = steps[tutStep];
  const el = document.querySelector(st.sel);
  const r = el ? el.getBoundingClientRect()
              : { left: innerWidth / 2 - 100, top: innerHeight / 2 - 60, width: 200, height: 120, bottom: innerHeight / 2 + 60 };
  const hole = $("tutHole");
  hole.style.left = (r.left - 8) + "px"; hole.style.top = (r.top - 8) + "px";
  hole.style.width = (r.width + 16) + "px"; hole.style.height = (r.height + 16) + "px";
  const box = $("tutBox");
  $("tutText").textContent = st.text;
  $("tutStep").textContent = `${tutStep + 1} / ${steps.length}`;
  // 先放到可见处量出尺寸，再决定最终位置（并强制夹回视口，确保按钮永远可点）
  box.style.left = "0px"; box.style.top = "0px"; box.style.bottom = "auto";
  const bw = box.offsetWidth || 390, bh = box.offsetHeight || 140;
  const pad = 14;
  let left, top;
  const tallTarget = r.height > innerHeight * 0.55;
  if (tallTarget) {
    // 高目标（如整列日志栏）：贴在目标左/右侧，垂直居中
    left = r.left > innerWidth / 2 ? r.left - bw - 18 : r.left + r.width + 18;
    top = innerHeight / 2 - bh / 2;
  } else if (r.top + r.height + 18 + bh <= innerHeight - pad) {
    left = r.left; top = r.bottom + 18;            // 下方
  } else if (r.top - 18 - bh >= pad) {
    left = r.left; top = r.top - 18 - bh;          // 上方
  } else {
    left = innerWidth / 2 - bw / 2; top = innerHeight / 2 - bh / 2;  // 居中兜底
  }
  box.style.left = Math.min(Math.max(left, pad), innerWidth - bw - pad) + "px";
  box.style.top = Math.min(Math.max(top, pad), innerHeight - bh - pad) + "px";
}
function endTutorial() {
  $("tut").classList.add("hidden");
  localStorage.dwTutorialDone = "1";
}

/* ---------- 工具 ---------- */
function esc(s) { return String(s ?? "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;"); }
function hashCode(s) { let h = 0; for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) | 0; return Math.abs(h); }

/* ---------- 绑定 ---------- */
window.addEventListener("DOMContentLoaded", () => {
  $("lang").value = localStorage.dwLang || CFG.defaultLang;
  $("lang").addEventListener("change", () => {
    localStorage.dwLang = $("lang").value; L = STR[$("lang").value]; applyI18n();
  });
  applyI18n();
  initLobby().then(() => {
    // URL 直达：#pve / #watch 自动开局（可收藏为快捷方式）
    if (location.hash === "#watch") startGame("watch");
    else if (location.hash === "#pve") startGame("pve");
  });
  polling = setInterval(poll, CFG.pollMs);
  $("btnStart").addEventListener("click", () => startGame("pve"));
  $("btnWatch").addEventListener("click", () => startGame("watch"));
  $("btnTutorial").addEventListener("click", () => { startGame("pve").then(() => setTimeout(startTutorial, 1000)); });
  $("btnSkipAmbush").addEventListener("click", () => cmd({ type: "endAmbush" }));
  $("btnEndTurn").addEventListener("click", () => { selected = null; cmd({ type: "endTurn" }); });
  $("btnQuit").addEventListener("click", backLobby);
  $("btnHelp").addEventListener("click", startTutorial);
  $("btnAgain").addEventListener("click", () => { startGame(S && S.humanIdx < 0 ? "watch" : "pve"); });
  $("btnLobby").addEventListener("click", backLobby);
  $("tutNext").addEventListener("click", () => { tutStep++; showTutStep(); });
  $("tutSkip").addEventListener("click", endTutorial);
  // 防卡死：点遮罩任意空白处=下一步；Esc=退出教程
  $("tut").addEventListener("click", e => { if (e.target.id === "tut" || e.target.id === "tutHole") { tutStep++; showTutStep(); } });
  window.addEventListener("keydown", e => { if (e.key === "Escape" && !$("tut").classList.contains("hidden")) endTutorial(); });
});
