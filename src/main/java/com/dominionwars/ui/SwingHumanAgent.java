package com.dominionwars.ui;

import com.dominionwars.engine.CardInstance;
import com.dominionwars.engine.Game;
import com.dominionwars.engine.PlayerAgent;

import javax.swing.JOptionPane;
import java.awt.Component;
import java.util.List;

/** 人类玩家代理：引擎需要决策时弹出对话框 */
public class SwingHumanAgent implements PlayerAgent {
    private final Component parent;

    public SwingHumanAgent(Component parent) { this.parent = parent; }

    private String pname(Game g, int idx) { return g.players[idx].name; }

    @Override
    public boolean askActivatePunish(Game g, int playerIdx, CardInstance card, int cost) {
        String msg = pname(g, playerIdx) + "：你被惩罚抽到了【" + card.def.name + "】！\n\n"
                + card.def.text + "\n\n是否立即以惩罚 " + cost + " 发动其【惩罚】效果？"
                + (cost > g.opponentOf(playerIdx).deck.size() ? "\n（警告：对方卡组仅剩 "
                + g.opponentOf(playerIdx).deck.size() + " 张，发动将会空发！）" : "");
        return JOptionPane.showConfirmDialog(parent, msg, "惩罚发动机会",
                JOptionPane.YES_NO_OPTION, JOptionPane.QUESTION_MESSAGE) == JOptionPane.YES_OPTION;
    }

    @Override
    public CardInstance chooseTarget(Game g, int playerIdx, List<CardInstance> options, String prompt, boolean optional) {
        if (options.isEmpty()) return null;
        Object[] arr = options.toArray();
        Object sel = JOptionPane.showInputDialog(parent,
                pname(g, playerIdx) + "：请选择目标 —— " + prompt,
                "选择目标", JOptionPane.QUESTION_MESSAGE, null, arr, arr[0]);
        if (sel == null) return optional ? null : options.get(0);
        return (CardInstance) sel;
    }

    @Override
    public Game.SingleDamageTarget chooseSingleDamageTarget(Game g, int playerIdx, List<Game.SingleDamageTarget> options, String prompt, boolean optional) {
        if (options.isEmpty()) return null;
        Object[] arr = options.toArray();
        Object sel = JOptionPane.showInputDialog(parent,
                pname(g, playerIdx) + "：请选择单体伤害目标 —— " + prompt,
                "选择单体伤害目标", JOptionPane.QUESTION_MESSAGE, null, arr, arr[0]);
        if (sel == null) return optional ? null : options.get(0);
        return (Game.SingleDamageTarget) sel;
    }

    @Override
    public Game.CoreTarget chooseCoreTarget(Game g, int playerIdx, List<Game.CoreTarget> options, String prompt, int amount, boolean optional) {
        if (options.isEmpty()) return null;
        Object[] arr = options.toArray();
        Object sel = JOptionPane.showInputDialog(parent,
                pname(g, playerIdx) + "：请选择核心伤害目标 —— " + prompt + "（" + amount + "点）",
                "选择核心伤害目标", JOptionPane.QUESTION_MESSAGE, null, arr, arr[0]);
        if (sel == null) return optional ? null : options.get(0);
        return (Game.CoreTarget) sel;
    }

    @Override
    public CardInstance chooseDiscard(Game g, int playerIdx, List<CardInstance> hand) {
        if (hand.isEmpty()) return null;
        Object[] arr = hand.toArray();
        Object sel = JOptionPane.showInputDialog(parent,
                pname(g, playerIdx) + "：请选择一张要弃置的手牌",
                "弃牌", JOptionPane.QUESTION_MESSAGE, null, arr, arr[0]);
        return sel == null ? hand.get(0) : (CardInstance) sel;
    }

    @Override
    public CardInstance chooseAmbush(Game g, int playerIdx, List<CardInstance> candidates, String actionDesc) {
        if (candidates.isEmpty()) return null;
        Object[] arr = new Object[candidates.size() + 1];
        arr[0] = "（不触发）";
        for (int i = 0; i < candidates.size(); i++) arr[i + 1] = candidates.get(i);
        Object sel = JOptionPane.showInputDialog(parent,
                pname(g, playerIdx) + "：对方动作触发了你的伏击窗口（" + describe(actionDesc) + "），是否触发？",
                "伏击响应", JOptionPane.QUESTION_MESSAGE, null, arr, arr[0]);
        return (sel instanceof CardInstance) ? (CardInstance) sel : null;
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
