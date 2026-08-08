package com.dominionwars.engine;

import com.dominionwars.data.CardLibrary;
import com.dominionwars.model.CardDef;
import com.dominionwars.model.CardDef.EffectSpec;

import java.util.ArrayList;
import java.util.List;

/**
 * 效果动作 DSL 解析器。
 * 支持动作见 ACTIONS 注释；编辑器中可自由组合 action/target/amount/param。
 */
public final class Effects {
    private Effects() {}

    /** 效果结算上下文 */
    public static class Ctx {
        public CardInstance playedCard;          // 触发反制窗口的卡
        public CardInstance attacker;            // 攻击者（OPPONENT_ATTACKS）
        public List<CardInstance> drawnCards;    // 刚抽到的牌（OPPONENT_DRAWS）
        public boolean negated = false;          // 被反制
    }

    /** 额外发动条件（惩罚发动等） */
    public static boolean checkCondition(Game g, int playerIdx, String cond) {
        if (cond == null || cond.isEmpty() || cond.equals("ALWAYS")) return true;
        PlayerState p = g.players[playerIdx];
        PlayerState opp = g.opponentOf(playerIdx);
        int n = trailingNum(cond);
        if (cond.startsWith("SELF_LEADER_ON_FIELD")) return p.leaderFielded();
        if (cond.startsWith("OPP_LEADER_ON_FIELD")) return opp.leaderFielded();
        if (cond.startsWith("ENEMY_MINIONS_GE_")) return opp.minions().size() >= n;
        if (cond.startsWith("SELF_MINIONS_GE_")) return p.minions().size() >= n;
        if (cond.startsWith("HAND_GE_")) return p.hand.size() >= n;
        if (cond.startsWith("SELF_LIFE_LE_")) return p.life != null && p.life <= n;
        return true;
    }

    private static int trailingNum(String s) {
        int i = s.length();
        while (i > 0 && Character.isDigit(s.charAt(i - 1))) i--;
        if (i == s.length()) return 0;
        try { return Integer.parseInt(s.substring(i)); } catch (Exception e) { return 0; }
    }

    public static void resolve(Game g, int srcIdx, CardInstance srcCard, List<EffectSpec> effects, Ctx ctx) {
        if (g.over() || effects == null) return;
        PlayerState self = g.players[srcIdx];
        PlayerState enemy = g.opponentOf(srcIdx);
        if (self.effectsNegatedThisTurn && srcCard != null && !srcCard.def.leader) {
            g.log("【" + (srcCard.def.name) + "】的效果在本回合被无效化");
            return;
        }
        for (EffectSpec e : effects) {
            if (g.over()) return;
            apply(g, srcIdx, srcCard, e, ctx);
        }
    }

    private static void apply(Game g, int srcIdx, CardInstance srcCard, EffectSpec e, Ctx ctx) {
        PlayerState self = g.players[srcIdx];
        PlayerState enemy = g.opponentOf(srcIdx);
        boolean leaderSource = srcCard != null && srcCard.def.leader;
        switch (e.action) {

            case "DAMAGE": {
                if (isSingleEnemyTarget(e.target)) {
                    g.resolveSingleEnemyDamage(srcIdx, srcCard, e.amount, "造成 " + e.amount + " 点单体伤害", true, true);
                } else {
                    for (CardInstance t : pickTargets(g, srcIdx, srcCard, e, "造成 " + e.amount + " 点伤害")) g.dealDamage(t, e.amount);
                    if (isFaceTarget(e.target)) hitFace(g, srcIdx, srcCard, enemy, e.amount);
                    if ("ENEMY_PLAYER".equals(e.target)) g.damagePlayerLife(enemy, e.amount);
                    if ("SELF_PLAYER".equals(e.target)) g.damagePlayerLife(self, e.amount);
                    g.cleanupDeaths();
                }
                break;
            }
            case "HEAL": {
                for (CardInstance t : pickTargets(g, srcIdx, srcCard, e, "恢复 " + e.amount + " 点生命")) {
                    if (t.def.isMinion()) {
                        t.health = Math.min(t.maxHealth, t.health + e.amount);
                        g.log("【" + t.def.name + "】恢复至 " + t.health);
                    }
                }
                if ("SELF_PLAYER".equals(e.target) && self.life != null) {
                    self.life += e.amount;
                    g.log(self.name + " 生命 +" + e.amount + " (" + self.life + ")");
                }
                break;
            }
            case "DRAW": g.drawCards(self, Math.max(1, e.amount), false); break;
            case "OPP_DRAW": g.drawCards(enemy, Math.max(1, e.amount), false); break;

            case "DISCARD_OPP_RANDOM": {
                for (int i = 0; i < e.amount && !enemy.hand.isEmpty(); i++) {
                    CardInstance c = enemy.hand.get(g.rng.nextInt(enemy.hand.size()));
                    g.discardFromHand(enemy, c, "效果弃牌");
                }
                break;
            }
            case "DISCARD_DRAWN": { // 深渊吞噬：弃掉刚抽到的所有牌
                if (ctx != null && ctx.drawnCards != null) {
                    for (CardInstance c : new ArrayList<>(ctx.drawnCards)) {
                        PlayerState owner = g.players[c.ownerIdx];
                        if (owner.hand.contains(c)) g.discardFromHand(owner, c, "深渊吞噬");
                    }
                }
                break;
            }
            case "DESTROY": {
                for (CardInstance t : pickTargets(g, srcIdx, srcCard, e, "破坏目标")) {
                    if (t.isLeaderEntity) { g.log("统领【" + t.def.name + "】免疫离场效果！"); continue; }
                    PlayerState owner = g.players[t.ownerIdx];
                    if (owner.protectedThisTurn) { g.log("【" + t.def.name + "】受庇护，无法被破坏"); continue; }
                    owner.field.remove(t);
                    owner.graveyard.add(t);
                    g.log("【" + t.def.name + "】被破坏（效果立即失效）");
                }
                break;
            }
            case "BUFF": {
                for (CardInstance t : pickTargets(g, srcIdx, srcCard, e, "强化")) {
                    String mode = e.param.isEmpty() ? "both" : e.param;
                    if (mode.contains("atk") || mode.equals("both")) t.attack += e.amount;
                    if (mode.contains("hp") || mode.equals("both")) { t.health += e.amount; t.maxHealth += e.amount; }
                    g.log("【" + t.def.name + "】获得强化 → " + t.attack + "/" + t.health);
                }
                break;
            }
            case "GRANT_KEYWORD": {
                for (CardInstance t : pickTargets(g, srcIdx, srcCard, e, "赋予【" + e.param + "】")) {
                    t.keywords.add(e.param);
                    if (CardDef.KW_SHIELD.equals(e.param)) t.shield = true;
                    g.log("【" + t.def.name + "】获得【" + e.param + "】");
                }
                break;
            }
            case "SUMMON": {
                CardDef d = g.library != null ? g.library.get(e.param) : null;
                if (d == null) { g.log("召唤失败：卡库中无 " + e.param); break; }
                int n = Math.max(1, e.amount);
                for (int i = 0; i < n; i++) {
                    CardInstance tok = new CardInstance(d, srcIdx);
                    g.summonToField(self, tok);
                    g.log(self.name + " 召唤了【" + d.name + "】(" + tok.attack + "/" + tok.health + ")");
                }
                break;
            }
            case "SUMMON_LEADER": { // 机械遗迹：吟唱完成后召唤随从代替本卡成为统领
                CardDef d = g.library != null ? g.library.get(e.param) : null;
                if (d == null) { g.log("统领替换失败：卡库中无 " + e.param); break; }
                CardInstance old = self.leaderOnField;
                if (old != null) {
                    self.field.remove(old);
                    self.graveyard.add(old);
                }
                CardInstance neo = new CardInstance(d, srcIdx);
                neo.isLeaderEntity = true;
                self.leaderOnField = neo;
                g.summonToField(self, neo);
                if (d.leaderDef != null && d.leaderDef.grantLife > 0) self.life = d.leaderDef.grantLife;
                g.log("☆ " + self.name + " 的新统领【" + d.name + "】(" + neo.attack + "/" + neo.health + ") 降临，取代旧躯！");
                g.computeAuras();
                break;
            }
            case "END_TURN": { // 烈焰统领：立即结束对方（当前行动方）回合
                g.log("⚡ 当前回合被强制终结！");
                g.requestEndTurn();
                break;
            }
            case "ADD_OPP_PUNISH_TURN": { // 机械统领惩罚效果：对方当回合所有卡牌惩罚值+N
                enemy.turnPunishDelta += e.amount;
                g.log(enemy.name + " 本回合所有卡牌惩罚值 +" + e.amount);
                break;
            }
            case "ADD_SELF_PUNISH_TURN": {
                self.turnPunishDelta += e.amount;
                g.log(self.name + " 本回合所有卡牌惩罚值 " + (e.amount >= 0 ? "+" : "") + e.amount);
                break;
            }
            case "CONVERT_PUNISH_TO_DISCARD": { // 深海统领：对方当回合惩罚值转为弃自己牌
                enemy.punishToSelfDiscardThisTurn = true;
                g.log(enemy.name + " 本回合的惩罚值被深渊扭曲：转化为弃置自己的手牌");
                break;
            }
            case "PROTECT_TURN": { // 古木统领：当回合己方卡牌不被破坏/反制
                self.protectedThisTurn = true;
                g.log(self.name + " 的卡牌在本回合受到古木庇护（不被破坏与反制）");
                break;
            }
            case "NEGATE": { // 反制：使触发窗口中的卡/攻击无效
                if (ctx != null) ctx.negated = true;
                break;
            }
            case "NEGATE_ENEMY_EFFECTS_TURN": { // 命运之影惩罚：对方场上卡牌效果当回合无效
                enemy.effectsNegatedThisTurn = true;
                g.computeAuras();
                g.log(enemy.name + " 场上所有卡牌的效果在本回合被变更为无效");
                break;
            }
            case "SKIP_RESHUFFLE": { // 机械统领惩罚效果：跳过(不计数)下次洗牌
                self.skipReshuffleCredits += Math.max(1, e.amount);
                g.log(self.name + " 获得 " + Math.max(1, e.amount) + " 次洗牌免计数");
                break;
            }
            case "RESTORE_ATTACKS": {
                for (CardInstance t : pickTargets(g, srcIdx, srcCard, e, "恢复攻击机会")) {
                    t.attacksUsed = 0;
                    t.summonedThisTurn = false;
                    g.log("【" + t.def.name + "】的攻击机会恢复了");
                }
                break;
            }
            case "GAIN_LIFE": {
                if (self.life != null) { self.life += e.amount; g.log(self.name + " 生命 +" + e.amount + " (" + self.life + ")"); }
                break;
            }
            case "LOSE_LIFE": {
                g.damagePlayerLife(enemy, e.amount);
                break;
            }
            case "DAMAGE_CASTLE": {
                // 对共享王城造成伤害（王城规则关闭时落空）
                if (g.castleActive()) g.damageRoyalCastle(srcIdx, e.amount, srcCard != null ? srcCard.def.name : "效果");
                else g.log("王城规则未启用，攻城效果落空");
                break;
            }
            case "WIN_GAME": {
                g.declareWin(srcIdx, e.param.isEmpty() ? (srcCard != null ? srcCard.def.name : "特殊胜利") : e.param);
                break;
            }
            default:
                g.log("（未知效果动作 " + e.action + "，已跳过——可在编辑器中自定义后由引擎扩展）");
        }
        g.checkAll();
    }

    private static boolean isFaceTarget(String t) { return "ENEMY_FACE".equals(t); }
    private static boolean isSingleEnemyTarget(String t) {
        return "ENEMY_TARGET".equals(t) || "ENEMY_SINGLE".equals(t) || "SINGLE_ENEMY".equals(t);
    }

    private static void hitFace(Game g, int srcIdx, CardInstance srcCard, PlayerState enemy, int amt) {
        // 单体核心伤害：王城存在时不再无脑吸收，由玩家/AI 在王城与已登场核心之间选择；统领抗性由【弑君】绕过。
        g.damageEnemyCore(srcIdx, srcCard, amt, "对统领/玩家伤害", true);
    }

    /** 目标解析。扰魔(WARD)随从不能成为指定型效果的目标；ALL_* 群体效果无视扰魔，但默认不伤害统领。 */
    private static List<CardInstance> pickTargets(Game g, int srcIdx, CardInstance srcCard, EffectSpec e, String prompt) {
        PlayerState self = g.players[srcIdx];
        PlayerState enemy = g.opponentOf(srcIdx);
        List<CardInstance> r = new ArrayList<>();
        switch (e.target) {
            case "SELF": {
                // 效果来源卡自身（须为己方场上存活随从）
                if (srcCard != null && self.field.contains(srcCard)
                        && srcCard.def.isMinion() && srcCard.health > 0) r.add(srcCard);
                break;
            }
            case "ENEMY_MINION": {
                List<CardInstance> opts = new ArrayList<>();
                for (CardInstance m : enemy.minions()) {
                    if (m.has(CardDef.KW_WARD)) continue;
                    if (m.isLeaderEntity && !g.canAffectLeader(srcCard, m)) continue;
                    opts.add(m);
                }
                CardInstance t = choose(g, srcIdx, opts, prompt);
                if (t != null) r.add(t);
                break;
            }
            case "FRIENDLY_MINION": {
                CardInstance t = choose(g, srcIdx, self.minions(), prompt);
                if (t != null) r.add(t);
                break;
            }
            case "ANY_MINION": {
                List<CardInstance> opts = new ArrayList<>(self.minions());
                for (CardInstance m : enemy.minions()) {
                    if (m.has(CardDef.KW_WARD)) continue;
                    if (m.isLeaderEntity && !g.canAffectLeader(srcCard, m)) continue;
                    opts.add(m);
                }
                CardInstance t = choose(g, srcIdx, opts, prompt);
                if (t != null) r.add(t);
                break;
            }
            case "ALL_ENEMY_MINIONS":
                for (CardInstance m : enemy.minions()) if (!m.isLeaderEntity || g.canAffectLeader(srcCard, m)) r.add(m);
                break;
            case "ALL_FRIENDLY_MINIONS": r.addAll(self.minions()); break;
            case "ALL_MINIONS":
                r.addAll(self.minions());
                for (CardInstance m : enemy.minions()) if (!m.isLeaderEntity || g.canAffectLeader(srcCard, m)) r.add(m);
                break;
            default: break;
        }
        return r;
    }

    private static CardInstance choose(Game g, int srcIdx, List<CardInstance> opts, String prompt) {
        if (opts.isEmpty()) return null;
        if (opts.size() == 1) return opts.get(0);
        return g.agents[srcIdx].chooseTarget(g, srcIdx, opts, prompt, false);
    }
}
