package com.dominionwars.ui;

import com.dominionwars.engine.CardInstance;
import com.dominionwars.engine.Game;
import com.dominionwars.engine.PlayerAgent;

import java.util.List;
import java.util.Scanner;

/** 控制台人类代理：引擎需要决策时通过标准输入交互（供手机 Termux 等无图形环境使用） */
public class ConsoleHumanAgent implements PlayerAgent {

    private final Scanner in;

    public ConsoleHumanAgent(Scanner in) { this.in = in; }

    private String readLine() {
        try {
            if (in.hasNextLine()) return in.nextLine().trim();
        } catch (Exception ignored) { }
        return ""; // EOF 等异常时返回空（走默认分支）
    }

    @Override
    public boolean askActivatePunish(Game g, int playerIdx, CardInstance card, int cost) {
        System.out.println();
        System.out.println("★ 你被惩罚抽到了【" + card.def.name + "】！");
        if (!card.def.text.isEmpty()) System.out.println("  " + card.def.text);
        int oppDeck = g.opponentOf(playerIdx).deck.size();
        System.out.print("  以惩罚 " + cost + " 发动其【惩罚】效果？"
                + (cost > oppDeck ? "（警告：对方卡组仅剩 " + oppDeck + " 张，将空发！）" : "")
                + " [y/N] > ");
        String s = readLine().toLowerCase();
        return s.equals("y") || s.equals("yes") || s.equals("是");
    }

    @Override
    public CardInstance chooseTarget(Game g, int playerIdx, List<CardInstance> options, String prompt, boolean optional) {
        if (options.isEmpty()) return null;
        if (options.size() == 1 && !optional) return options.get(0);
        System.out.println();
        System.out.println("★ 选择目标 —— " + prompt + (optional ? "（0=放弃）" : ""));
        for (int i = 0; i < options.size(); i++)
            System.out.println("  " + (i + 1) + ". " + options.get(i));
        System.out.print("> ");
        int k = parse(readLine(), optional ? 0 : 1);
        if (k == 0 && optional) return null;
        if (k < 1 || k > options.size()) k = 1;
        return options.get(k - 1);
    }

    @Override
    public Game.SingleDamageTarget chooseSingleDamageTarget(Game g, int playerIdx, List<Game.SingleDamageTarget> options, String prompt, boolean optional) {
        if (options.isEmpty()) return null;
        if (options.size() == 1 && !optional) return options.get(0);
        System.out.println();
        System.out.println("★ 选择单体伤害目标 —— " + prompt + (optional ? "（0=放弃）" : ""));
        for (int i = 0; i < options.size(); i++) System.out.println("  " + (i + 1) + ". " + options.get(i));
        System.out.print("> ");
        int k = parse(readLine(), optional ? 0 : 1);
        if (k == 0 && optional) return null;
        if (k < 1 || k > options.size()) k = 1;
        return options.get(k - 1);
    }

    @Override
    public Game.CoreTarget chooseCoreTarget(Game g, int playerIdx, List<Game.CoreTarget> options, String prompt, int amount, boolean optional) {
        if (options.isEmpty()) return null;
        if (options.size() == 1 && !optional) return options.get(0);
        System.out.println();
        System.out.println("★ 选择核心伤害目标 —— " + prompt + "（" + amount + "点）" + (optional ? "（0=放弃）" : ""));
        for (int i = 0; i < options.size(); i++) System.out.println("  " + (i + 1) + ". " + options.get(i));
        System.out.print("> ");
        int k = parse(readLine(), optional ? 0 : 1);
        if (k == 0 && optional) return null;
        if (k < 1 || k > options.size()) k = 1;
        return options.get(k - 1);
    }

    @Override
    public CardInstance chooseDiscard(Game g, int playerIdx, List<CardInstance> hand) {
        if (hand.isEmpty()) return null;
        System.out.println();
        System.out.println("★ 手牌超过上限，选择一张弃置：");
        for (int i = 0; i < hand.size(); i++)
            System.out.println("  " + (i + 1) + ". " + hand.get(i));
        System.out.print("> ");
        int k = parse(readLine(), 1);
        if (k < 1 || k > hand.size()) k = 1;
        return hand.get(k - 1);
    }

    @Override
    public CardInstance chooseAmbush(Game g, int playerIdx, List<CardInstance> candidates, String actionDesc) {
        if (candidates.isEmpty()) return null;
        System.out.println();
        System.out.println("★ 伏击窗口（" + describe(actionDesc) + "），可触发：（0=不触发）");
        for (int i = 0; i < candidates.size(); i++)
            System.out.println("  " + (i + 1) + ". " + candidates.get(i).def.name + " —— " + candidates.get(i).def.text);
        System.out.print("> ");
        int k = parse(readLine(), 0);
        if (k < 1 || k > candidates.size()) return null;
        return candidates.get(k - 1);
    }

    private int parse(String s, int def) {
        try { return Integer.parseInt(s.trim()); } catch (Exception e) { return def; }
    }

    private String describe(String evt) {
        switch (evt) {
            case "OPPONENT_PLAYS_CARD": return "对方使用卡牌";
            case "OPPONENT_PLAYS_SPELL": return "对方发动咒文";
            case "OPPONENT_SUMMONS": return "对方召唤随从";
            case "OPPONENT_ATTACKS": return "对方发起攻击";
            case "OPPONENT_DRAWS": return "对方抽牌";
            default: return evt;
        }
    }
}
