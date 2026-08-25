package com.dominionwars.engine;

import com.dominionwars.data.Balance;
import com.dominionwars.data.CardLibrary;
import com.dominionwars.model.CardDef;
import com.dominionwars.model.CardDef.AmbushKind;
import com.dominionwars.model.CardDef.CardType;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Random;
import java.util.function.Consumer;

/**
 * 《统御战纪》核心规则引擎。
 * 回合阶段：开始(抽牌/洗牌) → 伏击 → 行动 → 弃牌 → 结束
 */
public class Game {

    public enum Phase { START, AMBUSH, ACTION, DISCARD, END, OVER }

    /** 王城/统领等核心目标。王城是公共目标，LEADER/LIFE 属于目标玩家。 */
    public enum CoreTarget {
        ROYAL_CASTLE("共享王城"),
        LEADER("敌方统领"),
        LIFE("敌方生命");
        public final String cn;
        CoreTarget(String cn) { this.cn = cn; }
        @Override public String toString() { return cn; }
    }

    /** 单体伤害目标：未来卡牌可用 target=ENEMY_TARGET 在随从/王城/统领之间选择。 */
    public static class SingleDamageTarget {
        public final CardInstance card;
        public final CoreTarget core;
        public SingleDamageTarget(CardInstance card) { this.card = card; this.core = null; }
        public SingleDamageTarget(CoreTarget core) { this.card = null; this.core = core; }
        public boolean isCard() { return card != null; }
        @Override public String toString() { return card != null ? card.toString() : core.toString(); }
    }

    public final Balance balance;
    public CardLibrary library = null;   // SUMMON 等效果所需（可选注入）
    public final PlayerState[] players = new PlayerState[2];
    public final PlayerAgent[] agents = new PlayerAgent[2];
    public final Random rng;

    public int currentIdx;
    public Phase phase = Phase.START;
    public int turnNumber = 0;          // 第几个玩家回合（从1开始）
    public int winner = -1;             // -1 进行中
    public String winReason = "";

    public int chainDepth = 0;          // 惩罚连锁深度
    private boolean pendingEndTurn = false;
    private boolean reshuffling = false; // 洗牌阶段禁用一切效果

    // 共享王城：双方争夺同一条公共血量。谁打破，谁迫使对手首领出场并进入洗牌倒计时。
    public int royalCastleHp = 0;
    public int royalCastleMaxHp = 0;
    public int royalCastleBreaker = -1;

    public final List<String> logs = new ArrayList<>();
    public Consumer<String> logListener = null;

    public Game(Balance balance, List<CardDef> deckA, List<CardDef> deckB,
                String nameA, String nameB, PlayerAgent agentA, PlayerAgent agentB,
                long seed, int firstPlayer) {
        this.balance = balance;
        this.royalCastleMaxHp = Math.max(1, balance.royalCastleMaxHp);
        this.royalCastleHp = balance.royalCastleEnabled ? this.royalCastleMaxHp : 0;
        this.rng = new Random(seed);
        players[0] = new PlayerState(0, nameA);
        players[1] = new PlayerState(1, nameB);
        agents[0] = agentA;
        agents[1] = agentB;
        this.currentIdx = firstPlayer;
        for (CardDef d : deckA) players[0].deck.add(new CardInstance(d, 0));
        for (CardDef d : deckB) players[1].deck.add(new CardInstance(d, 1));
        Collections.shuffle(players[0].deck, rng);
        Collections.shuffle(players[1].deck, rng);
    }

    // ===================== 基本访问 =====================
    public PlayerState current() { return players[currentIdx]; }
    public PlayerState opponent() { return players[1 - currentIdx]; }
    public PlayerState opponentOf(int idx) { return players[1 - idx]; }
    public PlayerAgent agentOf(int idx) { return agents[idx]; }
    public boolean over() { return winner >= 0; }
    public boolean castleActive() { return balance.royalCastleEnabled && royalCastleHp > 0; }

    /** 统领效果抗性：默认统领不成为敌方卡牌效果对象，拥有【弑君】的来源可以绕过。 */
    public boolean canAffectLeader(CardInstance source, CardInstance leader) {
        if (leader == null) return false;
        return source != null && source.def.kingSlayer;
    }

    /** 效果级弑君优先；未声明时回退到旧的卡级字段。 */
    public boolean canAffectLeader(CardInstance source, CardDef.EffectSpec effect, CardInstance leader) {
        if (leader == null) return false;
        if (effect == null) return canAffectLeader(source, leader);
        boolean slayer = effect != null && effect.kingSlayer != null
                ? effect.kingSlayer
                : source != null && source.def.kingSlayer;
        return slayer
                && leader.def.leaderDef != null
                && leader.def.leaderDef.vulnerabilities.contains(effect.action);
    }

    /** 护卫：随从型统领有其他非统领随从护驾时，不能被普通攻击指定；弑君攻击者可绕过。 */
    public boolean isLeaderGuarded(CardInstance leader, CardInstance attacker) {
        if (leader == null || !leader.isLeaderEntity || !leader.def.guard) return false;
        if (attacker != null && attacker.def.kingSlayer) return false;
        return hasOtherNonLeaderMinion(leader.ownerIdx);
    }

    public boolean hasOtherNonLeaderMinion(int playerIdx) {
        for (CardInstance c : players[playerIdx].field) {
            if (c.def.isMinion() && !c.isLeaderEntity && c.health > 0) return true;
        }
        return false;
    }

    /** 当前可被指定/攻击的敌方核心目标。默认不把统领作为卡牌效果目标，除非来源具备【弑君】。 */
    public List<CoreTarget> legalEnemyCoreTargets(int srcIdx) {
        return legalEnemyCoreTargets(srcIdx, null, true);
    }

    public List<CoreTarget> legalEnemyCoreTargets(int srcIdx, CardInstance source, boolean includeMinionLeader) {
        return legalEnemyCoreTargets(srcIdx, source, null, includeMinionLeader);
    }

    public List<CoreTarget> legalEnemyCoreTargets(int srcIdx, CardInstance source, CardDef.EffectSpec effect, boolean includeMinionLeader) {
        List<CoreTarget> r = new ArrayList<>();
        PlayerState enemy = opponentOf(srcIdx);
        if (castleActive()) r.add(CoreTarget.ROYAL_CASTLE);
        if (enemy.leaderOnField != null) {
            boolean minionLeader = enemy.leaderOnField.def.isMinion();
            if ((!minionLeader || includeMinionLeader) && canAffectLeader(source, effect, enemy.leaderOnField)) r.add(CoreTarget.LEADER);
        } else if (enemy.life != null) {
            r.add(CoreTarget.LIFE);
        }
        return r;
    }

    /** 可选目标的单体敌方伤害：可选择敌方随从，也可选择王城/统领等核心目标。 */
    public void resolveSingleEnemyDamage(int srcIdx, int amt, String reason, boolean includeMinions, boolean includeCore) {
        resolveSingleEnemyDamage(srcIdx, null, amt, reason, includeMinions, includeCore);
    }

    public void resolveSingleEnemyDamage(int srcIdx, CardInstance source, int amt, String reason, boolean includeMinions, boolean includeCore) {
        resolveSingleEnemyDamage(srcIdx, source, null, amt, reason, includeMinions, includeCore);
    }

    public void resolveSingleEnemyDamage(int srcIdx, CardInstance source, CardDef.EffectSpec effect, int amt, String reason, boolean includeMinions, boolean includeCore) {
        if (amt <= 0 || over()) return;
        PlayerState enemy = opponentOf(srcIdx);
        List<SingleDamageTarget> opts = new ArrayList<>();
        if (includeMinions) {
            for (CardInstance m : enemy.minions()) {
                if (m.has(CardDef.KW_WARD)) continue;
                if (m.isLeaderEntity && !canAffectLeader(source, effect, m)) continue;
                opts.add(new SingleDamageTarget(m));
            }
        }
        if (includeCore) {
            for (CoreTarget c : legalEnemyCoreTargets(srcIdx, source, effect, true)) opts.add(new SingleDamageTarget(c));
        }
        if (opts.isEmpty()) {
            log(enemy.name + " 没有可选择的单体伤害目标");
            return;
        }
        SingleDamageTarget chosen = opts.size() == 1 ? opts.get(0) : agents[srcIdx].chooseSingleDamageTarget(this, srcIdx, opts, reason, false);
        if (chosen == null || !opts.contains(chosen)) chosen = opts.get(0);
        if (chosen.isCard()) {
            log("单体伤害选择【" + chosen.card.def.name + "】");
            dealDamage(chosen.card, amt);
            cleanupDeaths();
        } else {
            damageCoreTarget(srcIdx, chosen.core, amt, reason);
        }
    }

    /** 对敌方核心造成伤害；choose=true 时在王城/统领/生命中选择，避免脸伤被王城无脑吸收。 */
    public void damageEnemyCore(int srcIdx, int amt, String reason, boolean choose) {
        damageEnemyCore(srcIdx, null, amt, reason, choose, true);
    }

    public void damageEnemyCore(int srcIdx, CardInstance source, int amt, String reason, boolean choose) {
        damageEnemyCore(srcIdx, source, amt, reason, choose, true);
    }

    public void damageEnemyCore(int srcIdx, CardInstance source, CardDef.EffectSpec effect, int amt, String reason, boolean choose) {
        damageEnemyCore(srcIdx, source, effect, amt, reason, choose, true);
    }

    public void damageEnemyCore(int srcIdx, CardInstance source, int amt, String reason, boolean choose, boolean includeMinionLeader) {
        damageEnemyCore(srcIdx, source, null, amt, reason, choose, includeMinionLeader);
    }

    public void damageEnemyCore(int srcIdx, CardInstance source, CardDef.EffectSpec effect, int amt, String reason, boolean choose, boolean includeMinionLeader) {
        if (amt <= 0 || over()) return;
        List<CoreTarget> opts = legalEnemyCoreTargets(srcIdx, source, effect, includeMinionLeader);
        if (opts.isEmpty()) {
            log(opponentOf(srcIdx).name + " 的统领尚未登场，伤害落空");
            return;
        }
        CoreTarget t = opts.get(0);
        if (choose && opts.size() > 1) {
            CoreTarget picked = agents[srcIdx].chooseCoreTarget(this, srcIdx, opts, reason, amt, false);
            if (picked != null && opts.contains(picked)) t = picked;
        }
        damageCoreTarget(srcIdx, t, amt, reason);
    }

    private void damageCoreTarget(int srcIdx, CoreTarget t, int amt, String reason) {
        PlayerState enemy = opponentOf(srcIdx);
        switch (t) {
            case ROYAL_CASTLE -> damageRoyalCastle(srcIdx, amt, reason);
            case LEADER -> {
                if (enemy.leaderOnField != null) damageLeaderEntity(enemy, enemy.leaderOnField, amt);
                else log(enemy.name + " 的统领尚未登场，伤害落空");
            }
            case LIFE -> damagePlayerLife(enemy, amt);
        }
    }

    public void log(String s) {
        logs.add(s);
        if (logListener != null) logListener.accept(s);
    }

    public boolean bothLeadersFielded() {
        return players[0].leaderFielded() && players[1].leaderFielded();
    }

    /** 先驱威压：是否仅 idx 一方统领在场 */
    public boolean soloLeader(int idx) {
        return players[idx].leaderFielded() && !players[1 - idx].leaderFielded();
    }

    // ===================== 开局 =====================
    public void start() {
        log("== 对局开始：" + players[0].name + " VS " + players[1].name + " ==");
        if (castleActive()) log("◇ 共享王城出现：" + royalCastleHp + "/" + royalCastleMaxHp + "。王城与已登场核心可由单体伤害选择；破城者迫使对手首领出场。");
        log(players[currentIdx].name + " 先手");
        for (int p = 0; p < 2; p++) {
            log(players[p].name + " 的统领【" + findLeaderName(p) + "】正面朝上置入卡组");
            drawCards(players[p], balance.openingHand, false);
        }
        beginTurn(false);
    }

    private String findLeaderName(int p) {
        for (CardInstance c : players[p].deck) if (c.def.leader) return c.def.name;
        return players[p].leaderFielded() ? players[p].leaderOnField.def.name : "?";
    }

    // ===================== 回合流程 =====================
    void beginTurn(boolean alternated) {
        if (over()) return;
        turnNumber++;
        phase = Phase.START;
        PlayerState p = current();
        log("―― 第 " + turnNumber + " 回合：" + p.name + " ――");
        int n = balance.drawPerTurn;
        if (turnNumber == 2) n += balance.secondPlayerBonusDraw;
        drawCards(p, n, false);
        if (over()) return;
        phase = Phase.AMBUSH;
    }

    public void endAmbushPhase() {
        if (phase == Phase.AMBUSH) phase = Phase.ACTION;
    }

    /** 主动结束回合（或被强制）：弃牌阶段 → 结束阶段 → 交替 */
    public void endTurn() {
        if (over()) return;
        PlayerState p = current();
        // ---- 弃牌阶段 ----
        phase = Phase.DISCARD;
        int limit = balance.handLimit + (soloLeader(currentIdx) ? balance.pioneerHandLimitBonus : 0);
        while (p.hand.size() > limit && !over()) {
            CardInstance pick = agents[currentIdx].chooseDiscard(this, currentIdx, new ArrayList<>(p.hand));
            if (pick == null || !p.hand.contains(pick)) pick = p.hand.get(p.hand.size() - 1);
            discardFromHand(p, pick, "弃牌阶段", false);
        }
        // ---- 结束阶段 ----
        phase = Phase.END;
        // 吟唱推进
        List<CardInstance> chanting = new ArrayList<>();
        for (CardInstance c : p.field) if (c.chantRemaining > 0) chanting.add(c);
        if (p.leaderOnField != null && p.leaderOnField.chantRemaining > 0) chanting.add(p.leaderOnField);
        for (CardInstance c : chanting) {
            c.chantRemaining--;
            log(p.name + " 的【" + c.def.name + "】吟唱剩余 " + c.chantRemaining);
            if (c.chantRemaining == 0) {
                log("【" + c.def.name + "】吟唱完成！");
                Effects.resolve(this, p.idx, c, c.def.chantEffects, new Effects.Ctx());
                if (!c.def.leader && p.field.remove(c)) p.graveyard.add(c);
            }
        }
        // 非激活惩罚牌销毁
        List<CardInstance> doomed = new ArrayList<>();
        for (CardInstance c : p.hand)
            if (c.def.type == CardType.PUNISH && !c.punishActivated) doomed.add(c);
        for (CardInstance c : doomed) {
            p.hand.remove(c);
            p.graveyard.add(c);
            log(p.name + " 的惩罚牌【" + c.def.name + "】未满足惩罚条件，自动销毁");
        }
        // 古木胜利计数：本回合周期未受伤
        if (p.leaderFielded()) {
            if (!p.damagedThisCycle) p.noDamageTurns++;
            else p.noDamageTurns = 0;
        }
        p.damagedThisCycle = false;
        checkSpecialWins();
        if (over()) return;
        p.clearTurnFlags();
        // ---- 玩家交替 ----
        players[0].resetTagsOnAlternation();
        players[1].resetTagsOnAlternation();
        currentIdx = 1 - currentIdx;
        beginTurn(true);
    }

    // ===================== 抽牌与洗牌 =====================
    /** 抽牌（byPunish=true 为惩罚抽牌）。返回实际抽到的牌（统领除外） */
    public List<CardInstance> drawCards(PlayerState p, int n, boolean byPunish) {
        List<CardInstance> drawn = new ArrayList<>();
        for (int i = 0; i < n && !over(); i++) {
            if (p.deck.isEmpty()) {
                reshuffle(p);
                if (over()) return drawn;
                if (p.deck.isEmpty()) { log(p.name + " 已无任何可抽卡牌"); break; }
            }
            CardInstance c = p.deck.remove(p.deck.size() - 1);
            if (byPunish) p.punishDrawnThisTurn++;
            if (c.def.leader) {
                log(p.name + " 抽到了统领【" + c.def.name + "】！");
                enterLeader(p, c, byPunish);
            } else {
                c.punishActivated = byPunish && (c.def.punishActivatable || c.def.type == CardType.PUNISH);
                p.hand.add(c);
                drawn.add(c);
            }
        }
        if (!drawn.isEmpty() && !reshuffling) {
            log(p.name + " 抽了 " + drawn.size() + " 张牌" + (byPunish ? "（惩罚）" : ""));
            // 伏击触发：对方抽牌时
            Effects.Ctx ctx = new Effects.Ctx();
            ctx.drawnCards = drawn;
            tryAmbushWindow(1 - p.idx, "OPPONENT_DRAWS", null, ctx);
        }
        // 惩罚连锁：被惩罚抽到且满足发动条件的卡牌，询问是否发动
        if (byPunish && !over()) {
            for (CardInstance c : new ArrayList<>(drawn)) {
                if (over()) break;
                if (!p.hand.contains(c) || !c.punishActivated) continue;       // 可能已被伏击弃掉
                if (chainDepth >= balance.chainLimit) { log("惩罚连锁达到上限 " + balance.chainLimit + "，不再响应"); break; }
                if (!Effects.checkCondition(this, p.idx, c.def.punishCondition)) continue;
                if (!tagsFree(p, c.def)) continue;
                int cost = effectivePunish(p.idx, c, true);
                if (agents[p.idx].askActivatePunish(this, p.idx, c, cost)) {
                    chainDepth++;
                    playCard(p, c, true, false);
                    chainDepth--;
                }
            }
        }
        checkSpecialWins();
        return drawn;
    }

    /** 洗牌阶段：除战场外的卡牌洗回卡组（默认仅墓地，可配置含手牌）。期间禁用一切效果 */
    void reshuffle(PlayerState p) {
        reshuffling = true;
        log("―【洗牌阶段】" + p.name + " 无卡可抽，将场外卡牌洗回卡组―");
        for (CardInstance c : p.graveyard) c.resetRuntimeState();
        p.deck.addAll(p.graveyard);
        p.graveyard.clear();
        if (balance.reshuffleIncludesHand) {
            for (CardInstance c : p.hand) c.resetRuntimeState();
            p.deck.addAll(p.hand);
            p.hand.clear();
        }
        Collections.shuffle(p.deck, rng);
        if (p.skipReshuffleCredits > 0) {
            p.skipReshuffleCredits--;
            log(p.name + " 的牌库循环不计入对手胜利计数（机械遗迹效果）");
        } else {
            p.reshuffleCount++;
            PlayerState beneficiary = opponentOf(p.idx);
            beneficiary.cycleWinCount++;
            log(p.name + " 完成牌库循环；" + beneficiary.name + " 的胜利计数 +1（"
                    + beneficiary.cycleWinCount + "/" + balance.reshuffleLoseAt + "）");
            if (beneficiary.cycleWinCount >= balance.reshuffleLoseAt) {
                winner = beneficiary.idx;
                winReason = beneficiary.name + " 的胜利计数达到 " + balance.reshuffleLoseAt
                        + "（对手牌库循环过多），" + beneficiary.name + " 获胜！";
                phase = Phase.OVER;
                log("== " + winReason + " ==");
            }
        }
        reshuffling = false;
    }

    // ===================== 惩罚值计算 =====================
    public int effectivePunish(int playerIdx, CardInstance card, boolean asPunishActivation) {
        int v = asPunishActivation ? card.def.punishCost : card.def.punish;
        if (soloLeader(1 - playerIdx)) v += balance.pioneerOpponentPunishBonus; // 对方威压
        if (soloLeader(playerIdx)) v -= balance.pioneerSelfPunishDiscount;      // 备选：己方折扣
        v += players[playerIdx].turnPunishDelta;                                // 机械统领 +5 等
        return Math.max(0, v);
    }

    // ===================== 出牌 =====================
    public boolean tagsFree(PlayerState p, CardDef def) {
        for (String t : def.tags) if (p.usedTags.contains(t)) return false;
        return true;
    }
    void consumeTags(PlayerState p, CardDef def) { p.usedTags.addAll(def.tags); }

    /** 行动阶段是否可主动打出 */
    public String whyCannotPlay(CardInstance c) {
        PlayerState p = current();
        if (over()) return "对局已结束";
        if (phase != Phase.ACTION) return "不在行动阶段";
        if (!p.hand.contains(c)) return "卡牌不在手中";
        if (c.def.type == CardType.AMBUSH) return "伏击牌只能在伏击阶段盖放";
        if (c.def.type == CardType.PUNISH && !c.punishActivated) return "惩罚牌只能依靠惩罚抽取发动";
        if (!tagsFree(p, c.def)) return "词条本回合已使用：" + c.def.tags;
        return null;
    }

    /** 主动打出手牌（行动阶段）。已激活的惩罚牌/惩罚效果按降低费用结算 */
    public boolean playFromHand(CardInstance card) {
        String why = whyCannotPlay(card);
        if (why != null) { log("无法打出【" + card.def.name + "】：" + why); return false; }
        boolean asPunish = card.punishActivated;
        boolean ok = playCard(current(), card, asPunish, true);
        flushPendingEndTurn();
        return ok;
    }

    /** 核心出牌结算。asPunish=按惩罚发动；topLevel=连锁深度0的入口 */
    boolean playCard(PlayerState p, CardInstance card, boolean asPunish, boolean topLevel) {
        if (over()) return false;
        PlayerState opp = opponentOf(p.idx);
        if (!tagsFree(p, card.def)) { log(p.name + " 词条受限，无法使用【" + card.def.name + "】"); return false; }
        int cost = effectivePunish(p.idx, card, asPunish);

        // ---- 支付惩罚 ----
        if (p.punishToSelfDiscardThisTurn && cost > 0) {
            // 深海统领：惩罚值转化为弃自己牌
            log(p.name + " 的惩罚值被转化：弃置自己 " + cost + " 张牌");
            for (int i = 0; i < cost && !p.hand.isEmpty(); i++) {
                CardInstance d = agents[p.idx].chooseDiscard(this, p.idx, new ArrayList<>(p.hand));
                if (d == null || !p.hand.contains(d)) d = p.hand.get(p.hand.size() - 1);
                if (d == card) { // 不能弃正在打出的牌
                    if (p.hand.size() == 1) break;
                    for (CardInstance alt : p.hand) if (alt != card) { d = alt; break; }
                }
                discardFromHand(p, d, "惩罚转化");
            }
        } else if (cost > opp.deck.size()) {
            consumeTags(p, card.def);
            moveToGrave(p, card);
            if (isCurrent(p)) {
                // 裁决③：超出对方卡组余量——空发并强制结束回合
                log(p.name + " 打出【" + card.def.name + "】(惩罚" + cost + ") —— 对方卡组仅剩 "
                        + opp.deck.size() + " 张，卡牌空发！回合强制结束");
                pendingEndTurn = true;
            } else {
                // 对方回合的惩罚发动空发：卡牌作废但不结束回合
                log(p.name + " 的惩罚发动【" + card.def.name + "】因对方卡组不足而空发");
            }
            return true; // 算作使用
        } else if (cost > 0) {
            log(p.name + " 使用【" + card.def.name + "】，" + opp.name + " 因惩罚抽 " + cost + " 张牌");
            chainDepth++;
            drawCards(opp, cost, true);
            chainDepth--;
            if (over()) return true;
        }

        // ---- 伏击反制窗口（统领效果免疫非统领反制）----
        Effects.Ctx ctx = new Effects.Ctx();
        ctx.playedCard = card;
        String evt = card.def.type == CardType.SPELL ? "OPPONENT_PLAYS_SPELL"
                : card.def.isMinion() ? "OPPONENT_SUMMONS" : "OPPONENT_PLAYS_CARD";
        boolean negated = false;
        if (!card.def.leader) {
            tryAmbushWindow(opp.idx, evt, card, ctx);
            tryAmbushWindow(opp.idx, "OPPONENT_PLAYS_CARD", card, ctx);
            negated = ctx.negated;
        }
        consumeTags(p, card.def);
        p.hand.remove(card);

        if (negated) {
            if (p.protectedThisTurn) {
                log("【" + card.def.name + "】受古木庇护，反制无效！");
            } else {
                p.graveyard.add(card);
                log("【" + card.def.name + "】被反制，效果未结算");
                checkAll();
                return true;
            }
        }

        // ---- 结算 ----
        if (asPunish) {
            log(p.name + " 惩罚发动【" + card.def.name + "】(惩罚" + cost + ")");
            if (card.def.isMinion()) summonToField(p, card);
            Effects.resolve(this, p.idx, card, card.def.punishEffects, ctx);
            if (!card.def.isMinion()) p.graveyard.add(card);
        } else {
            switch (card.def.type) {
                case MINION:
                    log(p.name + " 召唤【" + card.def.name + "】(" + card.attack + "/" + card.health + ")");
                    summonToField(p, card);
                    Effects.resolve(this, p.idx, card, card.def.onPlayEffects, ctx);
                    break;
                case SPELL:
                    if (card.def.chant > 0) {
                        card.chantRemaining = card.def.chant;
                        p.field.add(card);
                        log(p.name + " 开始吟唱【" + card.def.name + "】(" + card.def.chant + "回合)");
                    } else {
                        log(p.name + " 发动咒文【" + card.def.name + "】");
                        Effects.resolve(this, p.idx, card, card.def.onPlayEffects, ctx);
                        p.graveyard.add(card);
                    }
                    break;
                default:
                    p.graveyard.add(card);
            }
        }
        checkAll();
        return true;
    }

    private boolean isCurrent(PlayerState p) { return p.idx == currentIdx; }

    void flushPendingEndTurn() {
        if (pendingEndTurn && !over() && chainDepth == 0) {
            pendingEndTurn = false;
            endTurn();
        }
    }

    /** 供效果调用：立即结束当前回合（烈焰统领惩罚效果） */
    public void requestEndTurn() { pendingEndTurn = true; }
    public boolean isEndTurnPending() { return pendingEndTurn; }

    public void damageRoyalCastle(int srcIdx, int amt, String reason) {
        if (!castleActive() || amt <= 0 || over()) return;
        royalCastleHp -= amt;
        // 王城是公共防线：被 srcIdx 方破坏时，视为另一方的领土遭受伤害，打断其「无伤」计数
        opponentOf(srcIdx).damagedThisCycle = true;
        log("◇ 王城受到 " + amt + " 点伤害（" + reason + "）(" + Math.max(0, royalCastleHp) + "/" + royalCastleMaxHp + ")");
        if (royalCastleHp <= 0) breakRoyalCastle(srcIdx);
    }

    private void breakRoyalCastle(int breakerIdx) {
        if (royalCastleBreaker >= 0 || over()) return;
        royalCastleBreaker = breakerIdx;
        royalCastleHp = 0;
        PlayerState victim = opponentOf(breakerIdx);
        log("◇ 王城被 " + players[breakerIdx].name + " 击破！" + victim.name + " 的首领被迫出场，破城方胜利计数进入末日倒计时。");
        players[breakerIdx].cycleWinCount = Math.max(players[breakerIdx].cycleWinCount, balance.royalCastleBreakVictoryCount);
        log(players[breakerIdx].name + " 的胜利计数被设为至少 " + balance.royalCastleBreakVictoryCount
                + "（当前 " + players[breakerIdx].cycleWinCount + "/" + balance.reshuffleLoseAt + "）");
        forceLeaderOut(victim);
        checkRoyalCastleWin(breakerIdx);
        checkAll();
    }

    /** 破城胜利：用于烈焰等以王城为主目标的统领。该胜利不受“双统领在场”门限保护。 */
    private void checkRoyalCastleWin(int breakerIdx) {
        if (over()) return;
        CardInstance leader = findLeaderAnywhere(players[breakerIdx]);
        if (leader == null || leader.def.leaderDef == null) return;
        if ("ROYAL_CASTLE_BREAK".equals(leader.def.leaderDef.winCondition)) {
            winner = breakerIdx;
            winReason = players[breakerIdx].name + " 获胜：" +
                    (leader.def.leaderDef.winText.isEmpty() ? "击破王城" : leader.def.leaderDef.winText);
            phase = Phase.OVER;
            log("== " + winReason + " ==");
        }
    }

    public CardInstance findLeaderAnywhere(PlayerState p) {
        if (p.leaderOnField != null) return p.leaderOnField;
        for (CardInstance c : p.hand) if (c.def.leader) return c;
        for (CardInstance c : p.deck) if (c.def.leader) return c;
        for (CardInstance c : p.graveyard) if (c.def.leader) return c;
        for (CardInstance c : p.field) if (c.def.leader) return c;
        for (CardInstance c : p.ambushes) if (c.def.leader) return c;
        return null;
    }

    private void forceLeaderOut(PlayerState p) {
        if (p.leaderFielded()) {
            log(p.name + " 的首领已经在场，破城只施加洗牌倒计时压力");
            return;
        }
        CardInstance leader = removeLeaderFromZone(p.hand);
        if (leader == null) leader = removeLeaderFromZone(p.deck);
        if (leader == null) leader = removeLeaderFromZone(p.graveyard);
        if (leader == null) leader = removeLeaderFromZone(p.field);
        if (leader == null) leader = removeLeaderFromZone(p.ambushes);
        if (leader == null) {
            log(p.name + " 未找到可强制出场的首领");
            return;
        }
        leader.resetRuntimeState();
        log("◇ 破城强制召唤：" + p.name + " 的首领【" + leader.def.name + "】出场（不视为抽到，不触发惩罚效果）");
        enterLeader(p, leader, false);
    }

    private CardInstance removeLeaderFromZone(List<CardInstance> zone) {
        for (CardInstance c : new ArrayList<>(zone)) {
            if (c.def.leader) {
                zone.remove(c);
                return c;
            }
        }
        return null;
    }

    void summonToField(PlayerState p, CardInstance card) {
        card.summonedThisTurn = true;
        card.attacksUsed = 0;
        p.field.add(card);
    }

    void moveToGrave(PlayerState p, CardInstance card) {
        p.hand.remove(card);
        p.graveyard.add(card);
    }

    public void discardFromHand(PlayerState p, CardInstance c, String reason) {
        discardFromHand(p, c, reason, true);
    }

    /** countsForWin=false 时不计入「对方累计弃牌」类胜利条件（弃牌阶段的强制弃牌） */
    public void discardFromHand(PlayerState p, CardInstance c, String reason, boolean countsForWin) {
        if (!p.hand.remove(c)) return;
        p.graveyard.add(c);
        if (countsForWin) p.totalDiscarded++;
        log(p.name + " 弃置【" + c.def.name + "】(" + reason + ")");
        if (countsForWin) triggerOpponentDiscardHooks(p);
        checkSpecialWins();
    }

    private int discardHookDepth = 0;

    /** p 因效果/惩罚转化弃牌后，其对手场上带「对方弃牌时」效果的卡牌依次触发 */
    private void triggerOpponentDiscardHooks(PlayerState discarder) {
        if (over() || discardHookDepth >= 4) return;   // 防联动套娃
        PlayerState owner = opponentOf(discarder.idx);
        if (owner.effectsNegatedThisTurn) return;
        discardHookDepth++;
        try {
            for (CardInstance c : new ArrayList<>(owner.field)) {
                if (over()) break;
                if (c.def.onOpponentDiscardEffects.isEmpty()) continue;
                if (!owner.field.contains(c) || (c.def.isMinion() && c.health <= 0)) continue;
                log("≈【" + c.def.name + "】因对方弃牌而触发");
                Effects.resolve(this, owner.idx, c, c.def.onOpponentDiscardEffects, new Effects.Ctx());
            }
            cleanupDeaths();
        } finally {
            discardHookDepth--;
        }
    }

    // ===================== 伏击 =====================
    public String whyCannotSetAmbush(CardInstance c) {
        PlayerState p = current();
        if (over()) return "对局已结束";
        if (phase != Phase.AMBUSH) return "只能在伏击阶段盖放";
        if (c.def.type != CardType.AMBUSH) return "不是伏击牌";
        if (p.ambushSetThisTurn) return "本回合已盖放过伏击";
        if (p.hasLockdownAmbush()) return "封场伏击在场，无法盖放其他伏击";
        return null;
    }

    /** 盖放伏击：惩罚值在盖放时支付（引擎裁定），词条在触发时消耗 */
    public boolean setAmbush(CardInstance card) {
        String why = whyCannotSetAmbush(card);
        if (why != null) { log("无法盖放：" + why); return false; }
        PlayerState p = current();
        PlayerState opp = opponent();
        int cost = effectivePunish(p.idx, card, false);
        if (p.punishToSelfDiscardThisTurn && cost > 0) {
            for (int i = 0; i < cost && p.hand.size() > 1; i++) {
                CardInstance d = agents[p.idx].chooseDiscard(this, p.idx, new ArrayList<>(p.hand));
                if (d == null || d == card || !p.hand.contains(d)) {
                    for (CardInstance alt : p.hand) if (alt != card) { d = alt; break; }
                }
                discardFromHand(p, d, "惩罚转化");
            }
        } else if (cost > opp.deck.size()) {
            consumeTags(p, card.def);
            moveToGrave(p, card);
            log(p.name + " 盖放伏击(惩罚" + cost + ")超出对方卡组余量，空发！回合强制结束");
            endTurn();
            return true;
        } else if (cost > 0) {
            log(p.name + " 盖放伏击，" + opp.name + " 因惩罚抽 " + cost + " 张牌");
            chainDepth++;
            drawCards(opp, cost, true);
            chainDepth--;
            flushPendingEndTurn();
            if (over() || phase != Phase.AMBUSH) return true;
        }
        p.hand.remove(card);
        p.ambushes.add(card);
        p.ambushSetThisTurn = true;
        log(p.name + " 盖放了一张伏击牌" + (card.def.ambushKind != AmbushKind.NORMAL ? "（" + card.def.ambushKind.cn + "）" : ""));
        return true;
    }

    /**
     * 伏击触发窗口：ownerIdx 的伏击响应对方动作。
     * 规则：每个动作最多1张伏击响应；专注伏击触发后压制本回合其他伏击；
     * 词条受限的伏击无法触发；效果无效化期间不可触发。
     */
    void tryAmbushWindow(int ownerIdx, String event, CardInstance targetCard, Effects.Ctx ctx) {
        if (over() || reshuffling || ctx.negated) return;
        PlayerState owner = players[ownerIdx];
        if (owner.effectsNegatedThisTurn) return;   // 命运之影：效果无效化期间伏击不可触发
        if (owner.focusAmbushLockThisTurn) return;  // 专注伏击触发后压制本回合其他伏击
        List<CardInstance> candidates = new ArrayList<>();
        for (CardInstance a : owner.ambushes) {
            if (!a.def.ambushTrigger.equals(event)) continue;
            if (!tagsFree(owner, a.def)) continue;
            candidates.add(a);
        }
        if (candidates.isEmpty()) return;
        CardInstance chosen = agents[ownerIdx].chooseAmbush(this, ownerIdx, candidates, event);
        if (chosen == null || !owner.ambushes.contains(chosen)) return;
        owner.ambushes.remove(chosen);
        consumeTags(owner, chosen.def);
        log(owner.name + " 的伏击【" + chosen.def.name + "】触发！");
        if (chosen.def.ambushKind == AmbushKind.FOCUS) owner.focusAmbushLockThisTurn = true;
        Effects.resolve(this, ownerIdx, chosen, chosen.def.ambushEffects, ctx);
        if (chosen.isLeaderEntity) {
            handleLeaderAmbushTriggered(owner, chosen);
        } else {
            owner.graveyard.add(chosen);
        }
        checkAll();
    }

    /** 伏击统领触发后的处理：胜利门未开则洗回卡组（其自身效果允许离场） */
    void handleLeaderAmbushTriggered(PlayerState owner, CardInstance leaderAmbush) {
        if (over()) return;
        owner.leaderOnField = null;
        leaderAmbush.punishActivated = false;
        owner.deck.add(leaderAmbush);
        Collections.shuffle(owner.deck, rng);
        log("统领伏击【" + leaderAmbush.def.name + "】以自身之力归还卡组，等待下一次降临…");
    }

    // ===================== 统领 =====================
    void enterLeader(PlayerState p, CardInstance card, boolean byPunish) {
        card.isLeaderEntity = true;
        p.leaderOnField = card;
        switch (card.def.type) {
            case MINION:
                summonToField(p, card);
                log("☆ " + p.name + " 的统领【" + card.def.name + "】(" + card.attack + "/" + card.health + ") 降临战场！");
                break;
            case AMBUSH:
                p.ambushes.add(card);
                log("☆ " + p.name + " 的统领【" + card.def.name + "】化作伏击潜伏于阴影中！");
                break;
            default:
                if (card.def.chant > 0) card.chantRemaining = card.def.chant;
                log("☆ " + p.name + " 的统领【" + card.def.name + "】进入统领区！"
                        + (card.chantRemaining > 0 ? "（吟唱 " + card.chantRemaining + " 回合）" : ""));
        }
        if (card.def.leaderDef.grantLife > 0) {
            p.life = card.def.leaderDef.grantLife;
            log("【" + card.def.name + "】赋予 " + p.name + " " + p.life + " 点生命，生命归零将落败");
        }
        computeAuras();
        Effects.Ctx ctx = new Effects.Ctx();
        if (!card.def.leaderDef.enterEffects.isEmpty())
            Effects.resolve(this, p.idx, card, card.def.leaderDef.enterEffects, ctx);
        if (byPunish) {
            log("⚡ 被惩罚抽到——统领【" + card.def.name + "】的强力惩罚效果发动！");
            Effects.resolve(this, p.idx, card, card.def.leaderDef.punishEffects, ctx);
        }
        checkAll();
    }

    /** 兼容旧调用点；已删除的统领压制光环不再产生任何状态。 */
    public void computeAuras() {
        players[0].leaderDisabled = false;
        players[1].leaderDisabled = false;
    }

    // ===================== 战斗 =====================
    /** 可被 attacker 攻击的目标（含敌方随从、随从统领、统领区耐久统领、伏击统领、玩家生命）。FACE 用 null 表示 */
    public List<CardInstance> legalAttackTargets(CardInstance attacker) {
        List<CardInstance> r = new ArrayList<>();
        PlayerState opp = opponentOf(attacker.ownerIdx);
        boolean taunt = opp.hasTaunt();
        for (CardInstance m : opp.minions()) {
            if (m.isLeaderEntity) continue;
            if (taunt && !m.has(CardDef.KW_TAUNT)) continue;
            r.add(m);
        }
        if (!taunt && opp.leaderOnField != null) {
            CardInstance l = opp.leaderOnField;
            if (!isLeaderGuarded(l, attacker)) r.add(l);
        }
        return r;
    }

    public boolean canAttackFace(CardInstance attacker) {
        PlayerState opp = opponentOf(attacker.ownerIdx);
        return !opp.hasTaunt() && (castleActive() || opp.life != null);
    }

    /** 攻击。target=null 表示攻击对方玩家生命(FACE) */
    public boolean attack(CardInstance attacker, CardInstance target) {
        if (over() || phase != Phase.ACTION) return false;
        PlayerState p = current();
        PlayerState opp = opponent();
        if (attacker.ownerIdx != p.idx || !p.field.contains(attacker) || !attacker.canAttackNow()) {
            log("【" + attacker.def.name + "】现在无法攻击");
            return false;
        }
        if (target == null && !canAttackFace(attacker)) { log("无法直接攻击对方"); return false; }
        if (target != null && !legalAttackTargets(attacker).contains(target)) {
            log("非法的攻击目标（嘲讽随从优先）");
            return false;
        }
        attacker.attacksUsed++;
        // 伏击窗口：对方攻击时
        Effects.Ctx ctx = new Effects.Ctx();
        ctx.attacker = attacker;
        tryAmbushWindow(opp.idx, "OPPONENT_ATTACKS", null, ctx);
        if (over()) return true;
        if (ctx.negated) { log("攻击被反制！"); checkAll(); return true; }
        if (attacker.health <= 0 || !p.field.contains(attacker)) { checkAll(); return true; }

        if (target == null) {
            log("【" + attacker.def.name + "】攻击敌方核心目标！");
            damageEnemyCore(p.idx, attacker, attacker.attack, attacker.def.name + "攻击", true, false);
        } else if (target.def.isMinion()) {
            log("【" + attacker.def.name + "】攻击【" + target.def.name + "】");
            dealDamage(target, attacker.attack);
            if (target.attack > 0) dealDamage(attacker, target.attack);
        } else {
            log("【" + attacker.def.name + "】攻击统领【" + target.def.name + "】");
            damageLeaderEntity(opponentOf(attacker.ownerIdx), target, attacker.attack);
        }
        cleanupDeaths();
        checkAll();
        flushPendingEndTurn();
        return true;
    }

    public void dealDamage(CardInstance target, int amt) {
        if (amt <= 0) return;
        if (target.shield) {
            target.shield = false;
            log("【" + target.def.name + "】的圣盾抵消了伤害");
            return;
        }
        if (target.def.isMinion()) {
            target.health -= amt;
            log("【" + target.def.name + "】受到 " + amt + " 点伤害 (" + Math.max(0, target.health) + ")");
            if (target.isLeaderEntity) players[target.ownerIdx].damagedThisCycle = true;
        } else if (target.isLeaderEntity) {
            damageLeaderEntity(players[target.ownerIdx], target, amt);
        }
    }

    void damageLeaderEntity(PlayerState owner, CardInstance leader, int amt) {
        if (amt <= 0) return;
        owner.damagedThisCycle = true;
        if (leader.def.isMinion()) {
            dealDamage(leader, amt);
        } else if (leader.def.leaderDef.durability > 0) {
            leader.durability -= amt;
            log("统领【" + leader.def.name + "】耐久 -" + amt + " (" + Math.max(0, leader.durability) + ")");
        } else if (owner.life != null) {
            damagePlayerLife(owner, amt);
        } else {
            log("统领【" + leader.def.name + "】无可损耗的胜负字段，攻击未产生效果");
        }
    }

    public void damagePlayerLife(PlayerState p, int amt) {
        if (p.life == null || amt <= 0) return;
        p.life -= amt;
        p.damagedThisCycle = true;
        log(p.name + " 生命 -" + amt + " (" + Math.max(0, p.life) + ")");
    }

    public void cleanupDeaths() {
        for (PlayerState p : players) {
            List<CardInstance> dead = new ArrayList<>();
            for (CardInstance c : p.field)
                if (c.def.isMinion() && c.health <= 0 && !c.isLeaderEntity) dead.add(c);
            for (CardInstance c : dead) {
                p.field.remove(c);
                p.graveyard.add(c);
                log("【" + c.def.name + "】被破坏（效果立即失效）");
            }
        }
    }

    // ===================== 胜负判定 =====================
    void checkAll() {
        cleanupDeaths();
        checkLeaderDefeat();
        checkSpecialWins();
    }

    /** 统领败北判定（受「双方统领未齐则游戏无法结束」门限保护） */
    void checkLeaderDefeat() {
        if (over()) return;
        for (PlayerState p : players) {
            if (!p.leaderFielded()) continue;
            CardInstance l = p.leaderOnField;
            boolean defeated = false;
            String how = "";
            if (l.def.isMinion() && l.health <= 0) { defeated = true; how = "统领【" + l.def.name + "】被击败"; }
            else if (!l.def.isMinion() && l.def.leaderDef.durability > 0 && l.durability <= 0) {
                defeated = true; how = "统领【" + l.def.name + "】耐久归零";
            }
            if (!defeated && p.life != null && p.life <= 0) { defeated = true; how = p.name + " 生命归零"; }
            if (defeated) {
                if (bothLeadersFielded()) {
                    winner = 1 - p.idx;
                    winReason = how + "，" + players[winner].name + " 获胜！";
                    phase = Phase.OVER;
                    log("== " + winReason + " ==");
                } else {
                    // 门限保护：统领未齐，无法终结——回复至 1
                    if (l.def.isMinion() && l.health <= 0) l.health = 1;
                    if (!l.def.isMinion() && l.durability <= 0 && l.def.leaderDef.durability > 0) l.durability = 1;
                    if (p.life != null && p.life <= 0) p.life = 1;
                    log("双方统领未齐，命运拒绝终结——" + how + " 被强行止于一线之间");
                }
            }
        }
    }

    /** 各阵营特殊胜利条件 */
    void checkSpecialWins() {
        if (over()) return;
        for (PlayerState p : players) {
            if (!p.leaderFielded()) continue;
            CardDef.LeaderDef ld = p.leaderOnField.def.leaderDef;
            PlayerState opp = opponentOf(p.idx);
            boolean met = false;
            switch (ld.winCondition) {
                case "OPP_DISCARD_TOTAL_GE": met = opp.totalDiscarded >= ld.winParam; break;
                case "NO_DAMAGE_TURNS_GE": met = p.noDamageTurns >= ld.winParam; break;
                case "OPP_PUNISH_DRAW_TURN_GE": met = opp.punishDrawnThisTurn >= ld.winParam; break;
                default: break;
            }
            if (met) declareWin(p.idx, "特殊胜利条件达成：" + (ld.winText.isEmpty() ? ld.winCondition : ld.winText));
        }
    }

    /** 宣告胜利（受门限保护） */
    public boolean declareWin(int idx, String reason) {
        if (over()) return false;
        if (!bothLeadersFielded() && balance.royalCastleEnabled) {
            log("胜利条件达成，王城规则强制未登场首领现身以结算终局");
            for (PlayerState p : players) if (!p.leaderFielded()) forceLeaderOut(p);
        }
        if (!bothLeadersFielded()) {
            log("胜利条件达成，但双方统领未齐——游戏尚不能结束");
            return false;
        }
        winner = idx;
        winReason = players[idx].name + " 获胜：" + reason;
        phase = Phase.OVER;
        log("== " + winReason + " ==");
        return true;
    }
}
