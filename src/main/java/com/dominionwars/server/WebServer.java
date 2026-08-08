package com.dominionwars.server;

import com.dominionwars.data.Balance;
import com.dominionwars.data.CardLibrary;
import com.dominionwars.engine.CardInstance;
import com.dominionwars.engine.Game;
import com.dominionwars.model.CardDef;
import com.dominionwars.util.Json;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;

import java.io.IOException;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * 本地游戏服务（零依赖，JDK 自带 HttpServer）：
 *   GET  /             网页界面（web/ 目录，可被玩家自行修改换皮）
 *   GET  /art/<id>.png 卡图
 *   GET  /api/decks    可用卡组
 *   POST /api/new      开新对局 {deckA,deckB,mode:"pve"|"watch"}
 *   GET  /api/state    当前对局 JSON 快照（轮询）
 *   POST /api/cmd      指令 {type:play|ambush|attack|endAmbush|endTurn|answer, ...}
 */
public class WebServer {

    private final Path dataDir;
    private final Path webDir;
    private final Balance balance;
    private volatile GameSession session;
    private volatile long lastSeen = 0;

    public WebServer(Path dataDir, Path webDir) {
        this.dataDir = dataDir;
        this.webDir = webDir;
        this.balance = Balance.load(dataDir.resolve("balance.json"));
    }

    public HttpServer start(int port) throws IOException {
        HttpServer s = HttpServer.create(new InetSocketAddress("127.0.0.1", port), 0);
        s.createContext("/", this::handleStatic);
        s.createContext("/art/", this::handleArt);
        s.createContext("/api/decks", ex -> json(ex, Json.write(decks(), false)));
        s.createContext("/api/new", this::handleNew);
        s.createContext("/api/state", ex -> {
            lastSeen = System.currentTimeMillis();
            GameSession gs = session;
            json(ex, gs == null ? "{\"none\":true}" : gs.snapshot());
        });
        s.createContext("/api/cmd", this::handleCmd);
        s.setExecutor(java.util.concurrent.Executors.newFixedThreadPool(4));
        s.start();
        return s;
    }

    private List<Object> decks() {
        List<Object> out = new ArrayList<>();
        for (Path p : CardLibrary.listDecks(dataDir.resolve("decks"))) {
            try {
                CardLibrary.DeckDef d = CardLibrary.DeckDef.load(p);
                Map<String, Object> m = new LinkedHashMap<>();
                m.put("file", p.getFileName().toString());
                m.put("name", d.name);
                m.put("faction", d.faction);
                out.add(m);
            } catch (Exception ignored) { }
        }
        return out;
    }

    private void handleNew(HttpExchange ex) throws IOException {
        Map<String, Object> body = readJson(ex);
        String fa = String.valueOf(body.getOrDefault("deckA", ""));
        String fb = String.valueOf(body.getOrDefault("deckB", ""));
        String mode = String.valueOf(body.getOrDefault("mode", "pve"));
        try {
            CardLibrary lib = CardLibrary.load(dataDir.resolve("cards"));
            CardLibrary.DeckDef da = CardLibrary.DeckDef.load(dataDir.resolve("decks").resolve(fa));
            CardLibrary.DeckDef db = CardLibrary.DeckDef.load(dataDir.resolve("decks").resolve(fb));
            List<String> problems = new ArrayList<>();
            List<CardDef> la = da.build(lib, balance, problems);
            List<CardDef> lb = db.build(lib, balance, problems);
            if (!problems.isEmpty()) { json(ex, err("卡组问题：" + String.join("；", problems))); return; }
            boolean pve = !"watch".equals(mode);
            if (session != null) session.close();
            GameSession gs = new GameSession(balance, la, lb,
                    (pve ? "你（" : "甲AI（") + da.name + "）", "AI（" + db.name + "）", pve);
            gs.start(lib);
            session = gs;
            json(ex, "{\"ok\":true}");
        } catch (Exception e) {
            json(ex, err("开局失败：" + e.getMessage()));
        }
    }

    private void handleCmd(HttpExchange ex) throws IOException {
        GameSession gs = session;
        if (gs == null) { json(ex, err("尚未开局")); return; }
        Map<String, Object> body = readJson(ex);
        String type = String.valueOf(body.getOrDefault("type", ""));
        Game g = gs.game;
        try {
            switch (type) {
                case "answer": {
                    long id = ((Number) body.getOrDefault("id", -1L)).longValue();
                    int choice = ((Number) body.getOrDefault("choice", -1)).intValue();
                    boolean ok = gs.human.answer(id, choice);
                    json(ex, ok ? "{\"ok\":true}" : err("决策已失效"));
                    return;
                }
                case "play": case "ambush": {
                    int uid = ((Number) body.getOrDefault("uid", -1)).intValue();
                    CardInstance c = gs.byUid(uid);
                    if (c == null) { json(ex, err("卡牌不存在")); return; }
                    if (g.currentIdx != gs.humanIdx || g.over()) { json(ex, err("当前不可操作")); return; }
                    String why = "play".equals(type) ? g.whyCannotPlay(c) : g.whyCannotSetAmbush(c);
                    if (why != null) { json(ex, err(why)); return; }
                    if ("play".equals(type)) gs.submit(() -> g.playFromHand(c));
                    else gs.submit(() -> g.setAmbush(c));
                    json(ex, "{\"ok\":true}");
                    return;
                }
                case "attack": {
                    int uid = ((Number) body.getOrDefault("uid", -1)).intValue();
                    boolean face = Boolean.TRUE.equals(body.get("face"));
                    int tuid = ((Number) body.getOrDefault("target", -1)).intValue();
                    CardInstance att = gs.byUid(uid);
                    if (att == null) { json(ex, err("攻击者不存在")); return; }
                    if (g.currentIdx != gs.humanIdx || g.phase != Game.Phase.ACTION) { json(ex, err("当前不可攻击")); return; }
                    if (!att.canAttackNow()) { json(ex, err("该单位当前无法攻击")); return; }
                    if (face) {
                        if (!g.canAttackFace(att)) { json(ex, err("无法直接攻击核心（嘲讽或不可行动）")); return; }
                        gs.submit(() -> g.attack(att, null));
                    } else {
                        CardInstance t = gs.byUid(tuid);
                        if (t == null || !g.legalAttackTargets(att).contains(t)) { json(ex, err("非法攻击目标")); return; }
                        gs.submit(() -> g.attack(att, t));
                    }
                    json(ex, "{\"ok\":true}");
                    return;
                }
                case "endAmbush": {
                    if (g.currentIdx != gs.humanIdx || g.phase != Game.Phase.AMBUSH) { json(ex, err("不在伏击阶段")); return; }
                    gs.submit(g::endAmbushPhase);
                    json(ex, "{\"ok\":true}");
                    return;
                }
                case "endTurn": {
                    if (g.currentIdx != gs.humanIdx || g.over()) { json(ex, err("当前不可操作")); return; }
                    gs.submit(() -> {
                        if (g.phase == Game.Phase.AMBUSH) g.endAmbushPhase();
                        if (!g.over() && g.currentIdx == gs.humanIdx && g.phase == Game.Phase.ACTION) g.endTurn();
                    });
                    json(ex, "{\"ok\":true}");
                    return;
                }
                default:
                    json(ex, err("未知指令：" + type));
            }
        } catch (Exception e) {
            json(ex, err("指令异常：" + e.getMessage()));
        }
    }

    // ===================== 工具 =====================

    private static String err(String msg) {
        return Json.write(Map.of("ok", false, "msg", msg), false);
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> readJson(HttpExchange ex) throws IOException {
        String body = new String(ex.getRequestBody().readAllBytes(), StandardCharsets.UTF_8);
        if (body.isBlank()) return new LinkedHashMap<>();
        Object o = Json.parse(body);
        return o instanceof Map ? (Map<String, Object>) o : new LinkedHashMap<>();
    }

    private static void json(HttpExchange ex, String s) throws IOException {
        byte[] b = s.getBytes(StandardCharsets.UTF_8);
        ex.getResponseHeaders().set("Content-Type", "application/json; charset=utf-8");
        ex.getResponseHeaders().set("Cache-Control", "no-store");
        ex.sendResponseHeaders(200, b.length);
        try (OutputStream os = ex.getResponseBody()) { os.write(b); }
    }

    private void handleStatic(HttpExchange ex) throws IOException {
        String p = ex.getRequestURI().getPath();
        if (p.equals("/")) p = "/index.html";
        Path f = webDir.resolve(p.substring(1)).normalize();
        if (!f.startsWith(webDir) || !Files.isRegularFile(f)) {
            ex.sendResponseHeaders(404, -1);
            return;
        }
        String ct = p.endsWith(".html") ? "text/html; charset=utf-8"
                : p.endsWith(".js") ? "application/javascript; charset=utf-8"
                : p.endsWith(".css") ? "text/css; charset=utf-8"
                : p.endsWith(".png") ? "image/png" : "application/octet-stream";
        byte[] b = Files.readAllBytes(f);
        ex.getResponseHeaders().set("Content-Type", ct);
        ex.sendResponseHeaders(200, b.length);
        try (OutputStream os = ex.getResponseBody()) { os.write(b); }
    }

    private void handleArt(HttpExchange ex) throws IOException {
        String name = ex.getRequestURI().getPath().substring("/art/".length());
        if (name.contains("..") || name.contains("/")) { ex.sendResponseHeaders(404, -1); return; }
        Path f = dataDir.resolve("art").resolve(name);
        if (!Files.isRegularFile(f)) { ex.sendResponseHeaders(404, -1); return; }
        byte[] b = Files.readAllBytes(f);
        ex.getResponseHeaders().set("Content-Type", "image/png");
        ex.sendResponseHeaders(200, b.length);
        try (OutputStream os = ex.getResponseBody()) { os.write(b); }
    }

    /** 入口：启动服务并打开浏览器（端口被占则顺延），浏览器关闭 2 分钟后自动退出 */
    public static void launch(Path dataDir, Path webDir, int port) throws IOException {
        WebServer ws = new WebServer(dataDir, webDir);
        IOException last = null;
        int bound = -1;
        for (int p = port; p < port + 10; p++) {
            try { ws.start(p); bound = p; break; } catch (IOException e) { last = e; }
        }
        if (bound < 0) throw last;
        port = bound;
        // 守望线程：页面曾连接但已 2 分钟无轮询 → 退出进程（避免后台残留）
        Thread watchdog = new Thread(() -> {
            while (true) {
                try { Thread.sleep(15000); } catch (InterruptedException e) { return; }
                long seen = ws.lastSeen;
                if (seen > 0 && System.currentTimeMillis() - seen > 120_000) System.exit(0);
            }
        }, "dw-watchdog");
        watchdog.setDaemon(true);
        watchdog.start();
        String url = "http://127.0.0.1:" + port + "/";
        System.out.println("统御战纪 网页版已启动: " + url);
        try {
            if (java.awt.Desktop.isDesktopSupported()
                    && java.awt.Desktop.getDesktop().isSupported(java.awt.Desktop.Action.BROWSE)) {
                java.awt.Desktop.getDesktop().browse(java.net.URI.create(url));
            }
        } catch (Exception ignored) { }
    }
}
