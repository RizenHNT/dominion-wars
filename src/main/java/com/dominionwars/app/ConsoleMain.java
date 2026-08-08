package com.dominionwars.app;

import com.dominionwars.ai.AiAgent;
import com.dominionwars.data.Balance;
import com.dominionwars.data.CardLibrary;
import com.dominionwars.engine.CardInstance;
import com.dominionwars.engine.Game;
import com.dominionwars.engine.PlayerState;
import com.dominionwars.model.CardDef;
import com.dominionwars.ui.ConsoleHumanAgent;

import java.io.PrintStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Scanner;

/**
 * 控制台对战模式（人机）：与图形版共用同一套引擎与 AI，规则零分叉。
 * 适用于手机 Termux、SSH 等无图形环境。
 *
 * 运行：java -cp build/classes com.dominionwars.app.ConsoleMain [玩家卡组序号] [AI卡组序号]
 */
public class ConsoleMain {

    private final Scanner in = new Scanner(System.in, StandardCharsets.UTF_8);
    private Game g;

    public static void main(String[] args) throws Exception {
        // 强制 UTF-8 输出（Termux/旧 JDK 环境兜底）
        System.setOut(new PrintStream(System.out, true, StandardCharsets.UTF_8));
        System.setErr(new PrintStream(System.err, true, StandardCharsets.UTF_8));
        new ConsoleMain().run(args);
    }

    private void run(String[] args) throws Exception {
        println("==============================");
        println("   统 御 战 纪 · 控制台对战");
        println("==============================");
        Balance bal = Balance.load(Path.of("data/balance.json"));
        CardLibrary lib = CardLibrary.load(Path.of("data/cards"));
        List<Path> deckPaths = CardLibrary.listDecks(Path.of("data/decks"));
        if (deckPaths.isEmpty()) { println("未找到卡组（请在项目根目录运行）"); return; }

        List<CardLibrary.DeckDef> defs = new ArrayList<>();
        for (Path p : deckPaths) defs.add(CardLibrary.DeckDef.load(p));
        for (int i = 0; i < defs.size(); i++)
            println("  " + (i + 1) + ". " + defs.get(i).name + "（" + defs.get(i).faction + "）");

        int my = pickIndex(args.length > 0 ? args[0] : null, "选择你的卡组", defs.size(), 1);
        int ai = pickIndex(args.length > 1 ? args[1] : null, "选择 AI 卡组", defs.size(),
                my == 1 ? 2 : 1);

        List<String> problems = new ArrayList<>();
        List<CardDef> da = defs.get(my - 1).build(lib, bal, problems);
        List<CardDef> db = defs.get(ai - 1).build(lib, bal, problems);
        if (!problems.isEmpty()) { println("卡组校验失败：" + problems); return; }

        ConsoleHumanAgent me = new ConsoleHumanAgent(in);
        AiAgent bot = new AiAgent();
        long seed = System.nanoTime();
        g = new Game(bal, da, db, "你（" + defs.get(my - 1).name + "）",
                "AI（" + defs.get(ai - 1).name + "）", me, bot, seed, (int) (seed & 1));
        g.library = lib;
        g.logListener = s -> println("· " + s);

        g.start();
        while (!g.over()) {
            if (g.currentIdx == 1) { bot.playTurn(g); continue; }
            if (g.phase == Game.Phase.AMBUSH) ambushPhase();
            if (g.over() || g.currentIdx != 0) continue;
            if (g.phase == Game.Phase.ACTION) actionPhase();
        }
        println("");
        println("====== 对局结束 ======");
        println("胜者：" + g.players[g.winner].name);
        println("原因：" + g.winReason);
    }

    private int pickIndex(String arg, String prompt, int max, int def) {
        if (arg != null) { try { int v = Integer.parseInt(arg); if (v >= 1 && v <= max) return v; } catch (Exception ignored) { } }
        print(prompt + " [1-" + max + "，默认" + def + "] > ");
        try { int v = Integer.parseInt(in.nextLine().trim()); if (v >= 1 && v <= max) return v; } catch (Exception ignored) { }
        return def;
    }

    // ================= 阶段交互 =================

    private void ambushPhase() {
        while (g.phase == Game.Phase.AMBUSH && !g.over() && g.currentIdx == 0) {
            List<CardInstance> ambushable = new ArrayList<>();
            for (CardInstance c : g.players[0].hand)
                if (g.whyCannotSetAmbush(c) == null) ambushable.add(c);
            if (ambushable.isEmpty()) { g.endAmbushPhase(); return; }
            println("");
            println("―【伏击阶段】可盖放：（0=跳过进入行动阶段）");
            for (int i = 0; i < ambushable.size(); i++) {
                CardInstance c = ambushable.get(i);
                println("  " + (i + 1) + ". " + c.def.name + "（" + c.def.ambushKind.cn
                        + "，惩罚" + g.effectivePunish(0, c, false) + "）—— " + c.def.text);
            }
            print("> ");
            int k = readInt(0);
            if (k < 1 || k > ambushable.size()) { g.endAmbushPhase(); return; }
            g.setAmbush(ambushable.get(k - 1));
        }
    }

    private void actionPhase() {
        while (g.phase == Game.Phase.ACTION && !g.over() && g.currentIdx == 0) {
            printState();
            println("指令: [数字]=打出第N张手牌  a=攻击  s=查看双方战场  d=查看手牌详情  e=结束回合");
            print("> ");
            String s = readLine().toLowerCase();
            if (s.isEmpty()) continue;
            switch (s) {
                case "e": g.endTurn(); return;
                case "s": printFields(); break;
                case "d": printHandDetail(); break;
                case "a": doAttack(); break;
                case "q": confirmQuit(); break;
                default: tryPlay(s);
            }
        }
    }

    private void tryPlay(String s) {
        int k;
        try { k = Integer.parseInt(s); } catch (Exception e) { println("无法识别的指令：" + s); return; }
        PlayerState p = g.players[0];
        if (k < 1 || k > p.hand.size()) { println("没有第 " + k + " 张手牌"); return; }
        CardInstance c = p.hand.get(k - 1);
        String why = g.whyCannotPlay(c);
        if (why != null) { println("无法打出：" + why); return; }
        int ep = g.effectivePunish(0, c, c.punishActivated);
        int oppDeck = g.players[1].deck.size();
        if (ep > oppDeck) {
            print("惩罚值 " + ep + " 超过对方卡组余量 " + oppDeck
                    + "，此牌将【空发】并强制结束你的回合。确定？[y/N] > ");
            if (!readLine().toLowerCase().startsWith("y")) return;
        }
        g.playFromHand(c);
    }

    private void doAttack() {
        PlayerState p = g.players[0];
        List<CardInstance> attackers = new ArrayList<>();
        for (CardInstance c : p.field) if (c.canAttackNow()) attackers.add(c);
        if (attackers.isEmpty()) { println("没有可行动的攻击者（召唤回合/次数耗尽）"); return; }
        println("选择攻击者：（0=取消）");
        for (int i = 0; i < attackers.size(); i++)
            println("  " + (i + 1) + ". " + attackers.get(i));
        print("> ");
        int k = readInt(0);
        if (k < 1 || k > attackers.size()) return;
        CardInstance atk = attackers.get(k - 1);

        List<CardInstance> targets = g.legalAttackTargets(atk);
        boolean face = g.canAttackFace(atk);
        println("选择攻击目标：（0=取消" + (face ? "，f=直接攻击对方玩家生命" : "") + "）");
        for (int i = 0; i < targets.size(); i++)
            println("  " + (i + 1) + ". " + targets.get(i)
                    + (targets.get(i).isLeaderEntity ? " ★统领" : ""));
        print("> ");
        String s = readLine().toLowerCase();
        if (face && s.equals("f")) { g.attack(atk, null); return; }
        int t;
        try { t = Integer.parseInt(s); } catch (Exception e) { return; }
        if (t >= 1 && t <= targets.size()) g.attack(atk, targets.get(t - 1));
    }

    private void confirmQuit() {
        print("确认退出对局？[y/N] > ");
        if (readLine().toLowerCase().startsWith("y")) System.exit(0);
    }

    // ================= 显示 =================

    private void printState() {
        PlayerState me = g.players[0], op = g.players[1];
        println("");
        println("―――― 第 " + g.turnNumber + " 回合 · 你的行动阶段 ――――");
        println("对方: " + summary(op));
        println("己方: " + summary(me));
        StringBuilder hand = new StringBuilder("手牌: ");
        for (int i = 0; i < me.hand.size(); i++) {
            CardInstance c = me.hand.get(i);
            hand.append("[").append(i + 1).append("]").append(c.def.name)
                .append("(惩").append(g.effectivePunish(0, c, c.punishActivated)).append(") ");
        }
        println(me.hand.isEmpty() ? "手牌: （空）" : hand.toString());
    }

    private String summary(PlayerState p) {
        StringBuilder sb = new StringBuilder();
        sb.append("卡组").append(p.deck.size())
          .append(" 手牌").append(p.hand.size())
          .append(" 墓地").append(p.graveyard.size())
          .append(" 伏击").append(p.ambushes.size());
        if (p.life != null) sb.append(" 生命").append(p.life);
        CardInstance l = p.leaderOnField;
        if (l != null) {
            sb.append(" ★").append(l.def.name);
            if (l.def.isMinion()) sb.append(l.attack).append("/").append(l.health);
            else if (l.durability > 0) sb.append("耐久").append(l.durability);
            if (p.leaderDisabled) sb.append("(被压制)");
        } else sb.append(" 统领未登场");
        sb.append(" 胜利计数").append(p.cycleWinCount).append("/").append(g.balance.reshuffleLoseAt)
          .append(" 循环").append(p.reshuffleCount);
        return sb.toString();
    }

    private void printFields() {
        for (int idx = 1; idx >= 0; idx--) {
            PlayerState p = g.players[idx];
            println((idx == 0 ? "己方" : "对方") + "战场:");
            if (p.field.isEmpty()) println("  （空）");
            for (CardInstance c : p.field) {
                StringBuilder sb = new StringBuilder("  ");
                if (c.isLeaderEntity) sb.append("★");
                sb.append(c.def.name);
                if (c.def.isMinion()) {
                    sb.append(" ").append(c.attack).append("/").append(c.health);
                    if (c.shield) sb.append(" ◈圣盾");
                    if (!c.keywords.isEmpty()) sb.append(" ").append(String.join(" ", c.keywords));
                    sb.append(c.canAttackNow() ? "（可攻击）" : "");
                } else if (c.chantRemaining > 0) sb.append(" 吟唱").append(c.chantRemaining);
                println(sb.toString());
            }
        }
    }

    private void printHandDetail() {
        PlayerState me = g.players[0];
        if (me.hand.isEmpty()) { println("（无手牌）"); return; }
        for (int i = 0; i < me.hand.size(); i++) {
            CardInstance c = me.hand.get(i);
            CardDef d = c.def;
            StringBuilder sb = new StringBuilder("  [" + (i + 1) + "] " + d.name + " · " + d.type.cn
                    + " · 惩罚" + g.effectivePunish(0, c, c.punishActivated));
            if (d.isMinion()) sb.append(" · ").append(d.attack).append("/").append(d.health);
            if (!d.tags.isEmpty()) sb.append(" · 词条:").append(String.join("、", d.tags));
            if (c.punishActivated) sb.append(" ·【已激活惩罚】");
            println(sb.toString());
            if (!d.text.isEmpty()) println("       " + d.text);
        }
    }

    private int readInt(int def) {
        try { return Integer.parseInt(readLine().trim()); } catch (Exception e) { return def; }
    }
    private String readLine() {
        try { if (in.hasNextLine()) return in.nextLine().trim(); } catch (Exception ignored) { }
        return "e"; // EOF 兜底：结束回合，避免死循环
    }
    private void println(String s) { System.out.println(s); }
    private void print(String s) { System.out.print(s); }
}
