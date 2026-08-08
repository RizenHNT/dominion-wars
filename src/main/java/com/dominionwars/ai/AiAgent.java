package com.dominionwars.ai;

import com.dominionwars.engine.CardInstance;
import com.dominionwars.engine.Game;
import com.dominionwars.engine.PlayerAgent;
import com.dominionwars.engine.PlayerState;
import com.dominionwars.model.CardDef;
import com.dominionwars.model.CardDef.CardType;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

/** 启发式 AI：可作为人机对战对手，也用于自动化对局测试 */
public class AiAgent implements PlayerAgent {

    // ===================== 决策回调 =====================
    @Override
    public boolean askActivatePunish(Game g, int playerIdx, CardInstance card, int cost) {
        // 0费必发；连锁过深收手；空发不发
        if (g.chainDepth > 6) return false;
        PlayerState opp = g.opponentOf(playerIdx);
        if (cost > opp.deck.size()) return false;
        return true;
    }

    @Override
    public CardInstance chooseTarget(Game g, int playerIdx, List<CardInstance> options, String prompt, boolean optional) {
        if (options.isEmpty()) return null;
        boolean harmful = prompt.contains("伤害") || prompt.contains("破坏");
        List<CardInstance> sorted = new ArrayList<>(options);
        if (harmful) {
            // 伤害/破坏优先选对方最具威胁目标
            sorted.sort(Comparator.comparingInt((CardInstance c) -> -(c.attack * 2 + c.health)));
            for (CardInstance c : sorted) if (c.ownerIdx != playerIdx) return c;
            return optional ? null : sorted.get(0);
        }
        // 增益/恢复优先选自己最强随从
        sorted.sort(Comparator.comparingInt((CardInstance c) -> -(c.attack + c.maxHealth)));
        for (CardInstance c : sorted) if (c.ownerIdx == playerIdx) return c;
        return sorted.get(0);
    }


    @Override
    public Game.SingleDamageTarget chooseSingleDamageTarget(Game g, int playerIdx, List<Game.SingleDamageTarget> options, String prompt, boolean optional) {
        if (options.isEmpty()) return null;
        PlayerState opp = g.opponentOf(playerIdx);

        // 1) 单体伤害能直接斩杀敌方统领/生命时，优先终结游戏。
        for (Game.SingleDamageTarget o : options) {
            if (o.core == Game.CoreTarget.LEADER && canCoreDamageWin(g, playerIdx, o.core, damageFromPrompt(prompt))) return o;
            if (o.core == Game.CoreTarget.LIFE && canCoreDamageWin(g, playerIdx, o.core, damageFromPrompt(prompt))) return o;
        }
        // 2) 烈焰的破城胜利轴：能破王城就直接抢。
        for (Game.SingleDamageTarget o : options) {
            if (o.core == Game.CoreTarget.ROYAL_CASTLE && shouldBreakOrPressureCastle(g, playerIdx, damageFromPrompt(prompt))) return o;
        }
        // 3) 有高威胁敌随从时，优先解场，避免共享王城被对手偷最后一击。
        Game.SingleDamageTarget bestCard = null;
        int bestScore = Integer.MIN_VALUE;
        for (Game.SingleDamageTarget o : options) {
            if (!o.isCard()) continue;
            CardInstance c = o.card;
            if (c.ownerIdx == playerIdx) continue;
            int score = threatScore(c);
            if (g.castleActive() && g.royalCastleHp <= enemyCastleThreat(g, playerIdx) + 8) score += 8;
            if (score > bestScore) { bestScore = score; bestCard = o; }
        }
        if (bestCard != null && bestScore >= 8) return bestCard;
        // 4) 默认用核心伤害推进自己的主胜利轴。
        for (Game.SingleDamageTarget o : options) {
            if (o.core == chooseCoreTarget(g, playerIdx, coreOptions(options), prompt, damageFromPrompt(prompt), optional)) return o;
        }
        return options.get(0);
    }

    @Override
    public Game.CoreTarget chooseCoreTarget(Game g, int playerIdx, List<Game.CoreTarget> options, String prompt, int amount, boolean optional) {
        if (options.isEmpty()) return null;
        PlayerState opp = g.opponentOf(playerIdx);
        boolean castleWin = hasCastleBreakWin(g, playerIdx);

        // 1) 直接斩首优先级最高，但烈焰若能直接破城则破城优先。
        if (options.contains(Game.CoreTarget.ROYAL_CASTLE) && castleWin && g.castleActive() && amount >= g.royalCastleHp) {
            return Game.CoreTarget.ROYAL_CASTLE;
        }
        if (options.contains(Game.CoreTarget.LEADER) && canCoreDamageWin(g, playerIdx, Game.CoreTarget.LEADER, amount)) {
            return Game.CoreTarget.LEADER;
        }
        if (options.contains(Game.CoreTarget.LIFE) && canCoreDamageWin(g, playerIdx, Game.CoreTarget.LIFE, amount)) {
            return Game.CoreTarget.LIFE;
        }
        if (options.contains(Game.CoreTarget.ROYAL_CASTLE) && g.castleActive() && amount >= g.royalCastleHp) {
            return Game.CoreTarget.ROYAL_CASTLE;
        }

        // 1.5) 对古木这种“连续未受伤”胜利，王城伤害不重置计数；临近胜利时必须打统领/生命断计时。
        if (enemyNoDamageThreat(g, playerIdx)) {
            if (options.contains(Game.CoreTarget.LEADER)) return Game.CoreTarget.LEADER;
            if (options.contains(Game.CoreTarget.LIFE)) return Game.CoreTarget.LIFE;
        }

        // 2) 如果这一下会把王城压进对方可偷范围，但我们破不了，就不要随手喂公共血条。
        if (options.size() > 1 && options.contains(Game.CoreTarget.ROYAL_CASTLE) && g.castleActive()) {
            int after = g.royalCastleHp - amount;
            if (after > 0 && enemyCastleThreat(g, playerIdx) >= after && ourReadyCastleDamage(g, playerIdx) < after) {
                if (options.contains(Game.CoreTarget.LEADER)) return Game.CoreTarget.LEADER;
                if (options.contains(Game.CoreTarget.LIFE)) return Game.CoreTarget.LIFE;
            }
        }

        // 3) 烈焰主轴是破城，其他卡组更偏向已登场统领/生命。
        if (castleWin && options.contains(Game.CoreTarget.ROYAL_CASTLE)) return Game.CoreTarget.ROYAL_CASTLE;
        if (options.contains(Game.CoreTarget.LEADER)) return Game.CoreTarget.LEADER;
        if (options.contains(Game.CoreTarget.LIFE)) return Game.CoreTarget.LIFE;
        return options.get(0);
    }

    @Override
    public CardInstance chooseDiscard(Game g, int playerIdx, List<CardInstance> hand) {
        // 弃掉价值最低的牌：未激活惩罚牌 > 高费 > 低身材
        CardInstance best = null;
        int worst = Integer.MAX_VALUE;
        for (CardInstance c : hand) {
            int v = value(c);
            if (c.def.type == CardType.PUNISH && !c.punishActivated) v -= 100;
            if (v < worst) { worst = v; best = c; }
        }
        return best != null ? best : (hand.isEmpty() ? null : hand.get(0));
    }

    @Override
    public CardInstance chooseAmbush(Game g, int playerIdx, List<CardInstance> candidates, String actionDesc) {
        if (candidates.isEmpty()) return null;
        // 触发最强（封场>专注>普通；同类选效果数多的）
        candidates.sort(Comparator.comparingInt((CardInstance c) -> -ambushRank(c)));
        return candidates.get(0);
    }

    private int ambushRank(CardInstance c) {
        int base = switch (c.def.ambushKind) { case LOCKDOWN -> 30; case FOCUS -> 20; case NORMAL -> 10; };
        return base + c.def.ambushEffects.size();
    }

    private int value(CardInstance c) {
        int v = c.def.punish * 2;
        if (c.def.isMinion()) v += c.attack + c.health;
        v += c.def.onPlayEffects.size() * 2 + c.def.punishEffects.size();
        return v;
    }

    // ===================== 整回合自动驾驶 =====================
    /** 执行 AI 的一个完整回合（在 beginTurn 后调用） */
    // ---- 单步执行状态（用于 UI 分步动画）----
    private int stepTurnNo = -1;
    private int stepPunishSpent = 0, stepCardsPlayed = 0;

    /**
     * 执行一个最小 AI 动作（盖一张伏击 / 进入行动 / 出一张牌 / 一次攻击 / 结束回合）。
     * 返回 true = 本回合还有后续动作；false = 回合已结束（已调用 endTurn）。
     * UI 用定时器逐步调用本方法，让玩家看清 AI 的每一步。
     */
    public boolean playOneStep(Game g) {
        if (g.over()) return false;
        int me = g.currentIdx;
        PlayerState p = g.players[me];
        PlayerState opp = g.opponentOf(me);
        if (stepTurnNo != g.turnNumber) {
            stepTurnNo = g.turnNumber;
            stepPunishSpent = 0;
            stepCardsPlayed = 0;
        }

        if (g.phase == Game.Phase.AMBUSH) {
            CardInstance best = null;
            for (CardInstance c : new ArrayList<>(p.hand)) {
                if (c.def.type != CardType.AMBUSH) continue;
                if (g.whyCannotSetAmbush(c) != null) continue;
                int cost = g.effectivePunish(me, c, false);
                if (cost > opp.deck.size()) continue;
                if (best == null || ambushRank(c) > ambushRank(best)) best = c;
            }
            if (best != null) {
                g.setAmbush(best);
                if (g.over() || g.currentIdx != me) return false;
                return true;
            }
            g.endAmbushPhase();
            return !g.over() && g.currentIdx == me;
        }

        if (g.phase != Game.Phase.ACTION) { g.endTurn(); return false; }

        int punishBudget = 4 + g.turnNumber / 4;
        if (opp.hand.size() >= 7) punishBudget -= 2;
        if (g.soloLeader(me)) punishBudget += 2;

        // 1) 出一张牌
        List<CardInstance> playable = new ArrayList<>();
        for (CardInstance c : p.hand) {
            if (g.whyCannotPlay(c) != null) continue;
            int cost = g.effectivePunish(me, c, c.punishActivated);
            if (cost > opp.deck.size()) continue;
            if (stepCardsPlayed >= 3 && cost > 0) continue;
            if (stepPunishSpent + cost > Math.max(0, punishBudget)) continue;
            playable.add(c);
        }
        playable.sort(Comparator.comparingInt(c -> playPriority(g, me, c)));
        if (!playable.isEmpty()) {
            CardInstance pick = playable.get(0);
            int cost = g.effectivePunish(me, pick, pick.punishActivated);
            if (g.playFromHand(pick)) {
                stepPunishSpent += cost;
                stepCardsPlayed++;
            }
            return !g.over() && g.currentIdx == me;
        }

        // 2) 一次攻击
        for (CardInstance m : new ArrayList<>(p.field)) {
            if (!m.canAttackNow()) continue;
            CardInstance target = pickAttackTarget(g, m);
            List<CardInstance> legal = g.legalAttackTargets(m);
            if (target != null && !legal.contains(target)) target = null;
            if (target != null || g.canAttackFace(m)) {
                g.attack(m, target);
                return !g.over() && g.currentIdx == me;
            }
        }

        // 3) 没有可做的：结束回合
        g.endTurn();
        return false;
    }

    public void playTurn(Game g) {
        if (g.over() || g.current().idx != indexOf(g)) {}
        int me = g.currentIdx;
        PlayerState p = g.players[me];
        PlayerState opp = g.opponentOf(me);

        // ---- 伏击阶段：盖一张最值得的伏击 ----
        if (g.phase == Game.Phase.AMBUSH) {
            CardInstance best = null;
            for (CardInstance c : new ArrayList<>(p.hand)) {
                if (c.def.type != CardType.AMBUSH) continue;
                if (g.whyCannotSetAmbush(c) != null) continue;
                int cost = g.effectivePunish(me, c, false);
                if (cost > opp.deck.size()) continue;          // 避免空发断送回合
                if (best == null || ambushRank(c) > ambushRank(best)) best = c;
            }
            if (best != null) g.setAmbush(best);
            if (g.over() || g.currentIdx != me) return;
            g.endAmbushPhase();
        }

        // ---- 行动阶段 ----
        // 节制策略：惩罚值是把卡牌喂给对方的代价——每回合设置预算，避免无脑倾泻
        int punishBudget = 4 + g.turnNumber / 4;          // 随对局推进略微放宽
        if (opp.hand.size() >= 7) punishBudget -= 2;       // 对方手牌已满，别再喂
        if (g.soloLeader(me)) punishBudget += 2;           // 己方威压期：压制节奏
        int punishSpent = 0, cardsPlayed = 0;
        int guard = 0;
        while (!g.over() && g.currentIdx == me && g.phase == Game.Phase.ACTION && guard++ < 50) {
            boolean acted = false;
            // 1) 出牌：按价值从高到低，避免空发，遵守惩罚预算
            List<CardInstance> playable = new ArrayList<>();
            for (CardInstance c : p.hand) {
                if (g.whyCannotPlay(c) != null) continue;
                int cost = g.effectivePunish(me, c, c.punishActivated);
                if (cost > opp.deck.size()) continue;
                if (cardsPlayed >= 3 && cost > 0) continue;
                if (punishSpent + cost > Math.max(0, punishBudget)) continue;
                playable.add(c);
            }
            playable.sort(Comparator.comparingInt(c -> playPriority(g, me, c)));
            if (!playable.isEmpty()) {
                CardInstance pick = playable.get(0);
                int cost = g.effectivePunish(me, pick, pick.punishActivated);
                acted = g.playFromHand(pick);
                if (acted) { punishSpent += cost; cardsPlayed++; }
            }
            if (g.over() || g.currentIdx != me || g.phase != Game.Phase.ACTION) return;
            // 2) 攻击：优先有利交换，其次打脸/统领
            for (CardInstance m : new ArrayList<>(p.field)) {
                if (g.over() || g.currentIdx != me) return;
                if (!m.canAttackNow()) continue;
                CardInstance target = pickAttackTarget(g, m);
                List<CardInstance> legal = g.legalAttackTargets(m);
                if (target != null && !legal.contains(target)) target = null;
                if (target != null || g.canAttackFace(m)) {
                    g.attack(m, target);
                    acted = true;
                }
            }
            if (!acted) break;
        }
        if (!g.over() && g.currentIdx == me) g.endTurn();
    }

    private int indexOf(Game g) { return g.currentIdx; }

    /** 越小越优先。加入王城后，AI 要知道什么时候抢城、什么时候先解场。 */
    private int playPriority(Game g, int me, CardInstance c) {
        int pr = 0;
        if (c.def.isMinion()) pr -= (c.attack + c.health);
        pr -= c.def.onPlayEffects.size() * 3;
        if (c.punishActivated) pr -= 5; // 已激活的低费机会优先用掉
        pr += c.def.punish;             // 高惩罚喂牌多，稍后再用

        int face = faceDamage(c);
        int aoe = aoeDamage(c);
        int minionDmg = targetedMinionDamage(c);
        int enemyThreat = enemyCastleThreat(g, me);
        if (g.castleActive()) {
            if (face >= g.royalCastleHp && hasCastleBreakWin(g, me)) pr -= 100; // 烈焰破城即胜
            else if (face >= g.royalCastleHp) pr -= 40;                         // 非烈焰也能抢末日倒计时
            else if (face > 0 && g.royalCastleHp - face <= enemyThreat && !hasCastleBreakWin(g, me)) pr += 12; // 别把王城送进对方斩杀线
        }
        if (aoe > 0 && g.opponentOf(me).minions().size() >= 2) pr -= 18 + aoe;
        if (minionDmg > 0 && dangerousEnemyMinionExists(g, me, minionDmg)) pr -= 14 + minionDmg;
        return pr;
    }


    private boolean hasCastleBreakWin(Game g, int playerIdx) {
        CardInstance leader = g.findLeaderAnywhere(g.players[playerIdx]);
        return leader != null && leader.def.leaderDef != null && "ROYAL_CASTLE_BREAK".equals(leader.def.leaderDef.winCondition);
    }

    private boolean enemyNoDamageThreat(Game g, int playerIdx) {
        PlayerState opp = g.opponentOf(playerIdx);
        if (opp.leaderOnField == null || opp.leaderDisabled) return false;
        CardDef.LeaderDef ld = opp.leaderOnField.def.leaderDef;
        return "NO_DAMAGE_TURNS_GE".equals(ld.winCondition) && ld.winParam > 0 && opp.noDamageTurns >= Math.max(1, ld.winParam - 2);
    }

    private boolean canCoreDamageWin(Game g, int playerIdx, Game.CoreTarget target, int amount) {
        PlayerState opp = g.opponentOf(playerIdx);
        if (target == Game.CoreTarget.ROYAL_CASTLE) return g.castleActive() && amount >= g.royalCastleHp;
        if (target == Game.CoreTarget.LEADER && opp.leaderOnField != null) {
            CardInstance l = opp.leaderOnField;
            if (l.def.isMinion()) return amount >= l.health && g.bothLeadersFielded();
            if (l.def.leaderDef.durability > 0) return amount >= l.durability && g.bothLeadersFielded();
            if (opp.life != null) return amount >= opp.life && g.bothLeadersFielded();
        }
        if (target == Game.CoreTarget.LIFE && opp.life != null) return amount >= opp.life && g.bothLeadersFielded();
        return false;
    }

    private int threatScore(CardInstance c) {
        int score = c.attack * 2 + Math.max(0, c.health);
        if (c.has(CardDef.KW_TAUNT)) score += 4;
        if (c.has(CardDef.KW_WARD)) score += 3;
        if (c.attacksPerTurn > 1) score += 4 * c.attacksPerTurn;
        if (c.isLeaderEntity) score += 20;
        return score;
    }

    private int enemyCastleThreat(Game g, int me) {
        return readyCastleDamage(g.opponentOf(me));
    }

    private int ourReadyCastleDamage(Game g, int me) {
        return readyCastleDamage(g.players[me]);
    }

    private int readyCastleDamage(PlayerState p) {
        int dmg = 0;
        for (CardInstance m : p.field) if (m.canAttackNow()) dmg += Math.max(0, m.attack);
        return dmg;
    }

    private List<Game.CoreTarget> coreOptions(List<Game.SingleDamageTarget> options) {
        List<Game.CoreTarget> r = new ArrayList<>();
        for (Game.SingleDamageTarget o : options) if (o.core != null && !r.contains(o.core)) r.add(o.core);
        return r;
    }

    private int damageFromPrompt(String prompt) {
        int n = 0;
        for (int i = 0; i < prompt.length(); i++) {
            char ch = prompt.charAt(i);
            if (Character.isDigit(ch)) n = n * 10 + (ch - '0');
            else if (n > 0) return n;
        }
        return n;
    }

    private boolean shouldBreakOrPressureCastle(Game g, int playerIdx, int amount) {
        if (!g.castleActive()) return false;
        if (amount >= g.royalCastleHp) return true;
        if (hasCastleBreakWin(g, playerIdx)) return true;
        int after = g.royalCastleHp - amount;
        return after > 0 && enemyCastleThreat(g, playerIdx) < after;
    }

    private boolean dangerousEnemyMinionExists(Game g, int me, int damage) {
        for (CardInstance m : g.opponentOf(me).minions()) {
            if (m.health <= damage && threatScore(m) >= 9) return true;
        }
        return false;
    }

    private int faceDamage(CardInstance c) {
        int sum = 0;
        for (CardDef.EffectSpec e : allImmediateEffects(c)) {
            if ("DAMAGE".equals(e.action) && "ENEMY_FACE".equals(e.target)) sum += Math.max(0, e.amount);
        }
        return sum;
    }

    private int targetedMinionDamage(CardInstance c) {
        int sum = 0;
        for (CardDef.EffectSpec e : allImmediateEffects(c)) {
            if ("DAMAGE".equals(e.action) && ("ENEMY_MINION".equals(e.target) || "ENEMY_TARGET".equals(e.target) || "ENEMY_SINGLE".equals(e.target) || "SINGLE_ENEMY".equals(e.target))) sum += Math.max(0, e.amount);
        }
        return sum;
    }

    private int aoeDamage(CardInstance c) {
        int sum = 0;
        for (CardDef.EffectSpec e : allImmediateEffects(c)) {
            if ("DAMAGE".equals(e.action) && e.target != null && e.target.startsWith("ALL_")) sum += Math.max(0, e.amount);
        }
        return sum;
    }

    private List<CardDef.EffectSpec> allImmediateEffects(CardInstance c) {
        List<CardDef.EffectSpec> fx = new ArrayList<>();
        if (c.punishActivated && !c.def.punishEffects.isEmpty()) fx.addAll(c.def.punishEffects);
        else fx.addAll(c.def.onPlayEffects);
        return fx;
    }

    private CardInstance pickAttackTarget(Game g, CardInstance attacker) {
        int me = attacker.ownerIdx;
        List<CardInstance> targets = g.legalAttackTargets(attacker);
        boolean canFace = g.canAttackFace(attacker);

        // 嘲讽墙存在时必须先处理。
        if (g.opponentOf(me).hasTaunt()) {
            CardInstance bestTaunt = null;
            int best = Integer.MIN_VALUE;
            for (CardInstance t : targets) {
                if (!t.has(CardDef.KW_TAUNT)) continue;
                int score = threatScore(t);
                if (attacker.attack >= t.health) score += 8;
                if (t.attack >= attacker.health && !attacker.isLeaderEntity) score -= 4;
                if (score > best) { best = score; bestTaunt = t; }
            }
            return bestTaunt != null ? bestTaunt : (targets.isEmpty() ? null : targets.get(0));
        }

        // 能直接通过攻击核心结束比赛/破城时，优先打核心。
        if (canFace) {
            if (g.castleActive() && attacker.attack >= g.royalCastleHp) return null;
            PlayerState opp = g.opponentOf(me);
            if (opp.leaderOnField != null && g.bothLeadersFielded()) {
                CardInstance l = opp.leaderOnField;
                if (l.def.isMinion() && attacker.attack >= l.health) return l;
                if (!l.def.isMinion() && l.def.leaderDef.durability > 0 && attacker.attack >= l.durability) return null;
                if (opp.life != null && attacker.attack >= opp.life) return null;
            }
        }

        // 共享王城低血线时，如果这一击破不了城，优先解对方可偷城的随从。
        if (g.castleActive() && canFace) {
            int after = g.royalCastleHp - attacker.attack;
            if (after > 0 && enemyCastleThreat(g, me) >= after && ourReadyCastleDamage(g, me) < after) {
                CardInstance thief = highestThreatKillable(g, attacker, targets);
                if (thief != null) return thief;
            }
        }

        // 有利交换：能击杀且自己不死。统领不要随便亲征撞死。
        for (CardInstance t : sortedByThreat(targets)) {
            if (t.def.isMinion() && attacker.attack >= t.health && (attacker.attack == 0 || t.attack < attacker.health || attacker.shield) && !t.shield) return t;
        }
        // 必须清场的高威胁。
        for (CardInstance t : sortedByThreat(targets)) {
            if (t.def.isMinion() && threatScore(t) >= 14 && attacker.attack >= t.health) return t;
        }
        // 古木等“无伤倒计时”临近时，攻击核心交给 chooseCoreTarget 去打统领断计时。
        if (canFace && enemyNoDamageThreat(g, me)) return null;

        // 烈焰等破城轴：在不会把王城送进对手补刀范围时，主动压王城。
        if (canFace && g.castleActive()) {
            int after = g.royalCastleHp - attacker.attack;
            if (hasCastleBreakWin(g, me) && (after <= 0 || enemyCastleThreat(g, me) < after)) return null;
        }
        // 非破城轴若有敌方统领/生命，优先打统领；否则再打王城。
        if (canFace && !g.castleActive()) return null;
        for (CardInstance t : targets) if (!t.def.isMinion()) return t;
        for (CardInstance t : targets) if (t.isLeaderEntity && (!attacker.isLeaderEntity || t.attack < attacker.health || attacker.shield)) return t;
        if (canFace) return null;
        return targets.isEmpty() ? null : sortedByThreat(targets).get(0);
    }

    private List<CardInstance> sortedByThreat(List<CardInstance> targets) {
        List<CardInstance> sorted = new ArrayList<>(targets);
        sorted.sort(Comparator.comparingInt((CardInstance c) -> -threatScore(c)));
        return sorted;
    }

    private CardInstance highestThreatKillable(Game g, CardInstance attacker, List<CardInstance> targets) {
        CardInstance best = null;
        int score = Integer.MIN_VALUE;
        for (CardInstance t : targets) {
            if (!t.def.isMinion()) continue;
            if (attacker.attack < t.health || t.shield) continue;
            int s = threatScore(t);
            if (t.attack >= attacker.health && !attacker.shield && attacker.isLeaderEntity) s -= 20;
            if (s > score) { score = s; best = t; }
        }
        return best;
    }
}
