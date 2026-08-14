package com.dominionwars.server;

import com.dominionwars.ai.AiAgent;
import com.dominionwars.data.Balance;
import com.dominionwars.engine.CardInstance;
import com.dominionwars.engine.Game;
import com.dominionwars.engine.PlayerState;
import com.dominionwars.model.CardDef;
import com.dominionwars.util.Json;

import java.util.ArrayList;
import java.util.IdentityHashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.atomic.AtomicLong;

/**
 * 一局游戏的运行容器：引擎跑在专属线程上（决策回调可安全阻塞），
 * UI 经 HTTP 投递指令（队列）并轮询 JSON 快照。引擎/界面彻底解耦。
 */
public class GameSession {

    public final Game game;
    public final int humanIdx;                 // -1 = AI 观战
    public final WebHumanAgent human;
    private final AiAgent[] ais = new AiAgent[2];
    private final LinkedBlockingQueue<Runnable> cmds = new LinkedBlockingQueue<>();
    private final AtomicLong version = new AtomicLong();
    private volatile String snapshot = "{\"none\":true}";
    private volatile boolean closed = false;
    private Thread loop;

    // ---- 可调参数（data/ui.json，缺省见右值）----
    private static int aiStepMs = 620, aiEndPauseMs = 250;
    static {
        try {
            Object o = Json.parse(java.nio.file.Files.readString(java.nio.file.Path.of("data/ui.json")));
            if (o instanceof Map<?, ?> m) {
                if (m.get("aiStepMs") instanceof Number n) aiStepMs = n.intValue();
                if (m.get("aiEndPauseMs") instanceof Number n) aiEndPauseMs = n.intValue();
            }
        } catch (Exception ignored) { }
    }

    // 卡牌实例 → 稳定 uid（仅对局线程访问）
    private final IdentityHashMap<CardInstance, Integer> uids = new IdentityHashMap<>();
    private int nextUid = 1;
    private int lastLogIdx = 0;
    private final List<Map<String, Object>> recentEvents = new ArrayList<>();

    public GameSession(Balance bal, List<CardDef> deckA, List<CardDef> deckB,
                       String nameA, String nameB, boolean humanA) {
        this.humanIdx = humanA ? 0 : -1;
        this.human = new WebHumanAgent(this);
        ais[0] = new AiAgent();
        ais[1] = new AiAgent();
        Object agentA = humanA ? human : ais[0];
        long seed = System.nanoTime();
        int first = new java.util.Random(seed).nextInt(2);
        this.game = new Game(bal, deckA, deckB, nameA, nameB,
                (com.dominionwars.engine.PlayerAgent) agentA, ais[1], seed, first);
    }

    public void start(com.dominionwars.data.CardLibrary lib) {
        game.library = lib;
        loop = new Thread(this::run, "dw-game-loop");
        loop.setDaemon(true);
        loop.start();
    }

    public void close() {
        closed = true;
        if (loop != null) loop.interrupt();
    }

    /** UI 指令入队（在对局线程执行）。 */
    public void submit(Runnable r) { cmds.offer(r); }

    public String snapshot() { return snapshot; }

    private void run() {
        try {
            game.start();
            publish();
            while (!closed && !game.over()) {
                if (game.currentIdx != humanIdx) {
                    // AI 回合：逐步播放
                    boolean more = ais[game.currentIdx].playOneStep(game);
                    publish();
                    if (!game.over() && more) Thread.sleep(aiStepMs);
                    else Thread.sleep(aiEndPauseMs);
                } else {
                    Runnable r = cmds.poll(300, java.util.concurrent.TimeUnit.MILLISECONDS);
                    if (r != null) {
                        try { r.run(); } catch (Exception e) { game.log("指令错误：" + e.getMessage()); }
                        publish();
                    }
                }
            }
            publish();
        } catch (InterruptedException ignored) {
        } catch (Exception e) {
            game.log("对局线程异常：" + e);
            publish();
        }
    }

    // ===================== 快照 =====================

    public synchronized void publish() {
        collectEvents();
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("version", version.incrementAndGet());
        m.put("turn", (long) game.turnNumber);
        m.put("phase", game.phase.name());
        m.put("current", (long) game.currentIdx);
        m.put("humanIdx", (long) humanIdx);
        m.put("over", game.over());
        if (game.over() && game.winner >= 0) {
            m.put("winner", (long) game.winner);
            m.put("winReason", game.winReason);
        }
        Map<String, Object> castle = new LinkedHashMap<>();
        castle.put("enabled", game.balance.royalCastleEnabled);
        castle.put("hp", (long) Math.max(0, game.royalCastleHp));
        castle.put("max", (long) game.royalCastleMaxHp);
        castle.put("breaker", (long) game.royalCastleBreaker);
        m.put("castle", castle);

        List<Object> ps = new ArrayList<>();
        int view = humanIdx >= 0 ? humanIdx : 0;
        for (int i = 0; i < 2; i++) ps.add(playerJson(game.players[i], i, view));
        m.put("players", ps);

        // 待决策
        WebHumanAgent.Pending p = human.pending();
        if (p != null) {
            Map<String, Object> pj = new LinkedHashMap<>();
            pj.put("id", p.id);
            pj.put("kind", p.kind);
            pj.put("prompt", p.prompt);
            pj.put("optional", p.optional);
            List<Object> opts = new ArrayList<>();
            for (int i = 0; i < p.raw.size(); i++) {
                Object o = p.raw.get(i);
                Map<String, Object> oj = new LinkedHashMap<>();
                oj.put("i", (long) i);
                if (o instanceof CardInstance ci) {
                    oj.put("card", cardJson(ci, true));
                    oj.put("label", ci.def.name);
                } else if (o instanceof Game.SingleDamageTarget t) {
                    if (t.isCard()) { oj.put("card", cardJson(t.card, true)); oj.put("label", t.card.def.name); }
                    else oj.put("label", t.core.toString());
                } else {
                    oj.put("label", String.valueOf(o));
                }
                opts.add(oj);
            }
            pj.put("options", opts);
            m.put("pending", pj);
        }

        // 日志（尾部 80 条）与动画事件
        List<String> logs = game.logs;
        int from = Math.max(0, logs.size() - 80);
        m.put("logStart", (long) from);
        m.put("logs", new ArrayList<>(logs.subList(from, logs.size())));
        m.put("events", new ArrayList<>(recentEvents));

        snapshot = Json.write(m, false);
    }

    /** 从新增日志提取动画事件（伤害/出牌/王城/触发） */
    private void collectEvents() {
        recentEvents.clear();
        List<String> logs = game.logs;
        for (int i = lastLogIdx; i < logs.size(); i++) {
            String ln = logs.get(i);
            Map<String, Object> ev = null;
            int a = ln.indexOf('【'), b = ln.indexOf('】');
            if (ln.contains("王城受到") && ln.contains("点伤害")) {
                ev = Map.of("t", "castleHit");
            } else if (a >= 0 && b > a && ln.substring(b).contains("受到") && ln.contains("点伤害")) {
                String amt = ln.replaceAll(".*受到 (\\d+) 点伤害.*", "$1");
                ev = Map.of("t", "hit", "name", ln.substring(a + 1, b), "n", amt);
            } else if (ln.contains("使用【") || ln.contains("发动咒文【") || ln.contains("召唤【") || ln.contains("惩罚发动【")) {
                ev = Map.of("t", "play", "s", ln);
            } else if (ln.contains("触发") && ln.contains("伏击")) {
                ev = Map.of("t", "ambush", "s", ln);
            } else if (ln.contains("被破坏")) {
                if (a >= 0 && b > a) ev = Map.of("t", "die", "name", ln.substring(a + 1, b));
            }
            if (ev != null) recentEvents.add(new LinkedHashMap<>(ev));
        }
        lastLogIdx = logs.size();
    }

    private int uidOf(CardInstance c) {
        return uids.computeIfAbsent(c, k -> nextUid++);
    }

    /** uid 反查（指令用） */
    public CardInstance byUid(int uid) {
        for (Map.Entry<CardInstance, Integer> e : uids.entrySet())
            if (e.getValue() == uid) return e.getKey();
        return null;
    }

    private Map<String, Object> playerJson(PlayerState p, int idx, int view) {
        boolean self = idx == view;
        boolean revealAll = humanIdx < 0;     // 观战模式全明
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("name", p.name);
        m.put("deck", (long) p.deck.size());
        m.put("handCount", (long) p.hand.size());
        m.put("grave", (long) p.graveyard.size());
        m.put("victory", (long) p.cycleWinCount);
        m.put("victoryMax", (long) game.balance.reshuffleLoseAt);
        if (p.life != null) m.put("life", (long) p.life);
        // 统领
        CardInstance ld = p.leaderOnField;
        if (ld != null) {
            Map<String, Object> lj = new LinkedHashMap<>();
            lj.put("name", ld.def.name);
            lj.put("minion", ld.def.isMinion());
            if (ld.def.isMinion()) { lj.put("atk", (long) ld.attack); lj.put("hp", (long) ld.health); }
            if (ld.durability > 0) lj.put("dur", (long) ld.durability);
            lj.put("disabled", p.leaderDisabled);
            m.put("leader", lj);
        }
        // 胜利条件进度
        Map<String, Object> goal = goalJson(p);
        if (goal != null) m.put("goal", goal);
        // 战场
        List<Object> field = new ArrayList<>();
        if (ld != null && !ld.def.isMinion() && !p.ambushes.contains(ld)) field.add(cardJson(ld, true));
        for (CardInstance c : p.field) field.add(cardJson(c, true));
        m.put("field", field);
        // 伏击
        if (self || revealAll) {
            List<Object> am = new ArrayList<>();
            for (CardInstance c : p.ambushes) am.add(cardJson(c, true));
            m.put("ambushes", am);
        } else {
            m.put("ambushCount", (long) p.ambushes.size());
        }
        // 手牌
        if (self || revealAll) {
            List<Object> hand = new ArrayList<>();
            for (CardInstance c : p.hand) hand.add(handCardJson(c, p));
            m.put("hand", hand);
        }
        return m;
    }

    private Map<String, Object> goalJson(PlayerState p) {
        CardInstance leader = game.findLeaderAnywhere(p);
        if (leader == null || leader.def.leaderDef == null) return null;
        var ld = leader.def.leaderDef;
        PlayerState opp = game.players[1 - p.idx];
        long cur, max;
        String kind = ld.winCondition == null ? "" : ld.winCondition;
        switch (kind) {
            case "OPP_DISCARD_TOTAL_GE": cur = opp.totalDiscarded; max = ld.winParam; break;
            case "NO_DAMAGE_TURNS_GE": cur = p.noDamageTurns; max = ld.winParam; break;
            case "OPP_PUNISH_DRAW_TURN_GE": cur = opp.punishDrawnThisTurn; max = ld.winParam; break;
            case "ROYAL_CASTLE_BREAK":
                if (!game.balance.royalCastleEnabled) return null;
                cur = game.royalCastleMaxHp - Math.max(0, game.royalCastleHp); max = game.royalCastleMaxHp; break;
            default: return null;
        }
        Map<String, Object> g = new LinkedHashMap<>();
        g.put("kind", kind);
        g.put("text", ld.winText);
        g.put("cur", cur);
        g.put("max", max);
        return g;
    }

    private Map<String, Object> cardJson(CardInstance c, boolean onBoard) {
        CardDef d = c.def;
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("uid", (long) uidOf(c));
        m.put("id", d.id);
        m.put("name", d.name);
        m.put("faction", d.faction);
        m.put("type", d.type.name());
        m.put("text", d.text);
        m.put("punish", (long) game.effectivePunish(c.ownerIdx, c, c.punishActivated));
        if (d.isMinion()) {
            m.put("atk", (long) c.attack); m.put("hp", (long) c.health);
            m.put("baseAtk", (long) d.attack); m.put("baseHp", (long) d.health);
        }
        if (c.isLeaderEntity && c.durability > 0) m.put("dur", (long) c.durability);
        if (d.chant > 0) m.put("chant", (long) (c.chantRemaining > 0 ? c.chantRemaining : d.chant));
        List<String> kw = new ArrayList<>(c.keywords.isEmpty() ? d.keywords : c.keywords);
        if (d.guard) kw.add("护卫");
        if (d.hasKingSlayer()) kw.add("弑君");
        m.put("kw", kw);
        if (c.shield) m.put("shield", true);
        if (d.leader || c.isLeaderEntity) m.put("leader", true);
        if (d.type == CardDef.CardType.AMBUSH) m.put("ambushKind", d.ambushKind.name());
        if (onBoard && d.isMinion()) m.put("canAttack", c.canAttackNow());
        if (new java.io.File("data/art/" + d.id + ".png").isFile()) m.put("art", true);
        return m;
    }

    private Map<String, Object> handCardJson(CardInstance c, PlayerState p) {
        Map<String, Object> m = cardJson(c, false);
        boolean my = game.currentIdx == p.idx && !game.over();
        String why = null;
        if (my && game.phase == Game.Phase.AMBUSH) why = game.whyCannotSetAmbush(c);
        else if (my && game.phase == Game.Phase.ACTION) why = game.whyCannotPlay(c);
        else why = "非操作阶段";
        m.put("usable", why == null);
        if (why != null) m.put("why", why);
        // 空发预警：有效惩罚 > 对方卡组余量
        if (why == null && game.phase == Game.Phase.ACTION) {
            int ep = game.effectivePunish(p.idx, c, false);
            if (ep > game.players[1 - p.idx].deck.size()) m.put("fizzle", true);
        }
        return m;
    }
}
