package com.dominionwars.test;

import com.dominionwars.ai.AiAgent;
import com.dominionwars.data.Balance;
import com.dominionwars.data.CardLibrary;
import com.dominionwars.engine.Game;
import com.dominionwars.model.CardDef;

import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

/**
 * AI 对 AI 批量模拟：冒烟测试 + 平衡数据采集。
 * 用法：java -cp build/classes:build/test-classes com.dominionwars.test.SimMain [局数]
 */
public class SimMain {
    public static void main(String[] args) throws Exception {
        int n = args.length > 0 ? Integer.parseInt(args[0]) : 20;
        Balance bal = Balance.load(Path.of("data/balance.json"));
        CardLibrary lib = CardLibrary.load(Path.of("data/cards"));
        List<Path> decks = CardLibrary.listDecks(Path.of("data/decks"));
        if (decks.size() < 2) { System.err.println("卡组不足"); return; }

        int[][] wins = new int[decks.size()][decks.size()];
        long totalTurns = 0; int games = 0;
        for (int a = 0; a < decks.size(); a++) {
            for (int b = 0; b < decks.size(); b++) {
                if (a == b) continue;
                for (int k = 0; k < n; k++) {
                    CardLibrary.DeckDef da = CardLibrary.DeckDef.load(decks.get(a));
                    CardLibrary.DeckDef db = CardLibrary.DeckDef.load(decks.get(b));
                    List<String> prob = new ArrayList<>();
                    List<CardDef> la = da.build(lib, bal, prob);
                    List<CardDef> lb = db.build(lib, bal, prob);
                    if (!prob.isEmpty()) { System.err.println("卡组问题: " + prob); return; }
                    AiAgent ai0 = new AiAgent(), ai1 = new AiAgent();
                    Game g = new Game(bal, la, lb, da.name, db.name, ai0, ai1,
                            (long) (a * 1000 + b * 100 + k), k % 2);
                    g.library = lib;
                    g.start();
                    int guard = 0;
                    while (!g.over() && guard++ < 400) {
                        AiAgent cur = g.currentIdx == 0 ? ai0 : ai1;
                        int before = g.turnNumber;
                        cur.playTurn(g);
                        if (!g.over() && g.turnNumber == before) g.endTurn(); // 兜底防卡死
                    }
                    games++;
                    totalTurns += g.turnNumber;
                    if (g.over()) wins[g.winner == 0 ? a : b][g.winner == 0 ? b : a]++;
                    else System.out.println("! 400回合未分胜负: " + da.name + " vs " + db.name + " seed=" + k);
                }
            }
        }
        System.out.println("== 模拟 " + games + " 局，平均回合数 " + (totalTurns / (double) games) + " ==");
        for (int i = 0; i < decks.size(); i++) {
            int w = 0, t = 0;
            for (int j = 0; j < decks.size(); j++) { w += wins[i][j]; t += wins[i][j] + wins[j][i]; }
            System.out.printf("%-28s 胜率 %.1f%% (%d/%d)%n",
                    decks.get(i).getFileName(), t == 0 ? 0 : 100.0 * w / t, w, t);
        }
    }
}
