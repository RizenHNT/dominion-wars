package com.dominionwars.engine;

import com.dominionwars.data.CardLibrary;
import com.dominionwars.model.CardDef;
import com.dominionwars.model.CardDef.EffectSpec;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/**
 * 效果动作 DSL 解析器。
 * 支持动作见 ACTIONS 注释；编辑器中可自由组合 action/target/amount/param。
 */
public final class Effects {
    private Effects() {}

    /**
     * 引擎已注册的动作表（对应 C# EffectDispatcher.RegisteredActions）。
     * PULL 合法动作要求被下载卡的全部下载效果都已被注册，未注册即拒绝整次下载。
     */
    public static final Set<String> ACTIONS = new HashSet<>(Arrays.asList(
            "DAMAGE", "HEAL", "DRAW", "OPP_DRAW", "DISCARD_OPP_RANDOM", "DISCARD_DRAWN",
            "DESTROY", "BUFF", "ADD_ROOT", "ADD_RAMPANT", "GRANT_KEYWORD", "SUMMON",
            "SUMMON_LEADER", "END_TURN", "ADD_OPP_PUNISH_TURN", "ADD_SELF_PUNISH_TURN",
            "CONVERT_PUNISH_TO_DISCARD", "PROTECT_TURN", "NEGATE", "NEGATE_ENEMY_EFFECTS_TURN",
            "SKIP_RESHUFFLE", "RESTORE_ATTACKS", "GAIN_LIFE", "LOSE_LIFE", "DAMAGE_CASTLE",
            "WIN_GAME", "ROLLBACK", "COMMIT", "PUSH", "PULL", "ENFEEBLE", "BANISH", "CONTROL"));

    public static boolean isKnownAction(String action) {
        return action != null && ACTIONS.contains(action);
    }

    /** 效果链中是否全部动作已注册（PULL 合法动作的前置校验）。 */
    public static boolean allKnown(List<EffectSpec> effects) {
        if (effects == null) return true;
        for (EffectSpec e : effects) if (!isKnownAction(e.action)) return false;
        return true;
    }

    /** 效果结算上下文 */
    public static class Ctx {
        public CardInstance playedCard;          // 触发反制窗口的卡
        public CardInstance attacker;            // 攻击者（OPPONENT_ATTACKS）
        public List<CardInstance> drawnCards;    // 刚抽到的牌（OPPONENT_DRAWS）
        public boolean negated = false;          // 被反制
        /** PULL 合法动作显式选择的效果目标（RULES §12.4 单目标下载效果必须明确选择）。 */
        public Integer selectedTargetId = null;
        boolean deferDeaths = false;             // 一条效果链结束前暂不清理负血随从
    }

    /**
     * 额外发动条件（卡级 punishCondition，以及每效果 {@link EffectSpec#condition}）。
     *
     * <p>与 C# {@code PunishConditionEvaluator.IsSatisfied} 逐条对齐（2026-09-13 补齐）。
     * 本方法与 C# 侧同为"条件语法"的唯一真源；卡面文案里写的"若……则……"必须能在这里求值，
     * 否则卡面就是在说谎。
     *
     * <p><b>失败方向：未知词条一律返回 false（fail-closed）。</b>
     * 2026-09-13 修正：本方法原先对未知词条返回 true，而那与 C# 的 false 相反 ——
     * 同一个拼错的词条在 C# 里让效果永不结算、在 Java 里让效果永不拦截，
     * 等于一个笔误就把受条件保护的高收益卡变成无门槛卡，且在 Java 侧看不出来。
     * 现在两侧一致 fail-closed：写错词条 = 效果不结算，立刻暴露。
     *
     * <p>与 C# 的两处已知差异（保持显式，不隐藏）：
     * <ul>
     *   <li>C# 的 {@code SELF_LIFE_LE_n} 读 {@code PlayerState.Life}（非空且 ≤ n）；
     *       Java 的 {@code life} 默认 null，故 {@code SELF_LIFE_LE_n} 仅在 life 非空时成立。</li>
     *   <li>C# 的 {@code SELF_SEALED_HEALTH_GE_n} 读封印随从的 {@code Health}（与
     *       {@code SealedMinionMaxHealthCondition} 一致，即当前生命而非生命上限），
     *       Java 同样读 {@code health}。</li>
     * </ul>
     */
    public static boolean checkCondition(Game g, int playerIdx, String cond) {
        if (cond == null || cond.isEmpty() || cond.equals("ALWAYS")) return true;
        PlayerState p = g.players[playerIdx];
        PlayerState opp = g.opponentOf(playerIdx);
        // ── 精确词条 ──
        if (cond.equals("SELF_LEADER_ON_FIELD")) return p.leaderFielded();
        if (cond.equals("OPP_LEADER_ON_FIELD")) return opp.leaderFielded();

        // ── 场面 ──
        Integer n = threshold(cond, "ENEMY_MINIONS_GE_");
        if (n != null) return livingMinions(opp) >= n;
        n = threshold(cond, "SELF_MINIONS_GE_");
        if (n != null) return livingMinions(p) >= n;

        // ── 手牌 ──
        n = threshold(cond, "HAND_GE_");
        if (n != null) return p.hand.size() >= n;
        n = threshold(cond, "OPP_HAND_GE_");
        if (n != null) return opp.hand.size() >= n;
        n = threshold(cond, "OPP_HAND_LE_");
        if (n != null) return opp.hand.size() <= n;

        // ── 生命池（默认未启用，见 RULES §0/§12.5）──
        n = threshold(cond, "SELF_LIFE_LE_");
        if (n != null) return p.life != null && p.life <= n;

        // ── 古木：封印 / 扎根 / 疯长 ──
        n = threshold(cond, "SELF_SEALED_GE_");
        if (n != null) return sealedMinions(p) >= n;
        n = threshold(cond, "SELF_SEALED_HEALTH_GE_");
        if (n != null) return bestSealedHealth(p) >= n;
        n = threshold(cond, "SELF_ROOT_GE_");
        if (n != null) return p.rootStacks >= n;
        n = threshold(cond, "SELF_RAMPANT_GE_");
        if (n != null) return p.rampantStacks >= n;

        // ── 机械：提交队列 / 云端栈 / 下载 ──
        n = threshold(cond, "SELF_COMMIT_GE_");
        if (n != null) return p.commitQueue.size() >= n;
        n = threshold(cond, "SELF_CLOUD_GE_");
        if (n != null) return p.cloudStack.size() >= n;
        n = threshold(cond, "SELF_PULL_GE_");
        if (n != null) return p.pullCount >= n;

        // ── 深海：对手弃牌（胜利轴读的是对手的 totalDiscarded）──
        n = threshold(cond, "OPP_DISCARD_GE_");
        if (n != null) return opp.totalDiscarded >= n;

        // ── 伏击 ──
        n = threshold(cond, "SELF_AMBUSH_GE_");
        if (n != null) return p.ambushes.size() >= n;

        // ── 共享王城（2026-09-24 新增）──
        // 不带 SELF_/OPP_ 前缀：王城是双方共享的一条独立败北轨（RULES §9.1），
        // 读它只有"当前血量"一个数，加前缀会让人误以为存在"对方的王城"。
        // 只做 `CASTLE_HP_LE_n`：C# 的 GameState 只存当前血量、不存上限，
        // 所以"已损失量"那族在 C# 无法精确表达 —— 与其两边语义不一致，
        // 不如只做双方都能精确表达的那一族。
        n = threshold(cond, "CASTLE_HP_LE_");
        if (n != null) return g.royalCastleHp <= n;

        return false;
    }

    /** 存活的非统领随从数（与 C# CountLivingMinions 一致）。 */
    private static int livingMinions(PlayerState p) {
        int c = 0;
        for (CardInstance x : p.field)
            if (x.def.isMinion() && x.health > 0 && !x.isLeaderEntity) c++;
        return c;
    }

    /** 封印随从数（不论当前生命，封印身份本身就是古木机制的核心）。 */
    private static int sealedMinions(PlayerState p) {
        int c = 0;
        for (CardInstance x : p.field)
            if (x.def.isMinion() && x.health > 0 && x.sealed) c++;
        return c;
    }

    /** 封印随从的最大当前生命（与 C# SealedMinionMaxHealthCondition 一致）。 */
    private static int bestSealedHealth(PlayerState p) {
        int best = 0;
        for (CardInstance x : p.field)
            if (x.def.isMinion() && x.sealed && x.health > best) best = x.health;
        return best;
    }

    /** 解析一个闭合条件族的非负十进制阈值；坏后缀、负数和溢出均拒绝。 */
    private static Integer threshold(String value, String prefix) {
        if (value == null || prefix == null || !value.startsWith(prefix)) return null;
        String suffix = value.substring(prefix.length());
        if (suffix.isEmpty()) return null;
        for (int i = 0; i < suffix.length(); i++) {
            char c = suffix.charAt(i);
            if (c < '0' || c > '9') return null;
        }
        try {
            return Integer.valueOf(suffix);
        } catch (NumberFormatException e) {
            return null;
        }
    }

    public static void resolve(Game g, int srcIdx, CardInstance srcCard, List<EffectSpec> effects, Ctx ctx) {
        resolve(g, srcIdx, srcCard, effects, ctx, null);
    }

    /**
     * @param selectedTargetId 已由合法动作/调用方预先选定的效果目标实例 uid；
     *                         null 时由 pickTargets + PlayerAgent 决策（默认路径）。
     */
    public static void resolve(Game g, int srcIdx, CardInstance srcCard, List<EffectSpec> effects,
                               Ctx ctx, Integer selectedTargetId) {
        if (g.over() || effects == null) return;
        // 与 C# EffectContext 的来源控制权约束对齐：被 CONTROL 的卡只能
        // 以当前控制者身份结算，不能用原拥有者或任意调用方冒充来源。
        if (srcCard != null && srcCard.controllerIdx() != srcIdx) {
            g.log("效果来源控制权不匹配，效果已跳过");
            return;
        }
        PlayerState self = g.players[srcIdx];
        if (self.effectsNegatedThisTurn && srcCard != null && !srcCard.def.leader) {
            g.log("【" + (srcCard.def.name) + "】的效果在本回合被无效化");
            return;
        }
        ctx.deferDeaths = true;
        try {
            for (EffectSpec e : effects) {
                if (g.over()) return;
                // 卡面写明的发动条件在效果结算前求值（与 C# EffectDispatcher 的
                // "effect.condition_not_met" 分支对齐）。不满足或词条未知 → 跳过该效果，
                // 并留下可审计的日志，而不是静默地无条件结算。
                if (!checkCondition(g, srcIdx, e.condition)) {
                    g.log("【" + (srcCard != null ? srcCard.def.name : "效果") + "】的「"
                            + describeCondition(e.condition) + "」未满足，该效果不结算");
                    continue;
                }
                apply(g, srcIdx, srcCard, e, ctx, false, selectedTargetId);
            }
        } finally {
            ctx.deferDeaths = false;
            g.checkAll();
        }
    }

    /**
     * 把条件词条翻成卡面用语，供日志与 UI 复用。
     * 词条是机器语法（SELF_SEALED_GE_2），卡面是人话（己方封印随从≥2），
     * 两者必须能互相解释，否则卡面就是在说谎。
     */
    public static String describeCondition(String cond) {
        if (cond == null || cond.isEmpty() || cond.equals("ALWAYS")) return "无条件";
        String[] families = {
                "ENEMY_MINIONS_GE_|对方存活随从≥", "SELF_MINIONS_GE_|己方存活随从≥",
                "HAND_GE_|己方手牌≥", "OPP_HAND_GE_|对方手牌≥", "OPP_HAND_LE_|对方手牌≤",
                "SELF_LIFE_LE_|己方生命≤", "SELF_SEALED_GE_|己方封印随从≥",
                "SELF_SEALED_HEALTH_GE_|己方封印随从生命≥", "SELF_ROOT_GE_|己方扎根≥",
                "SELF_RAMPANT_GE_|己方疯长≥", "SELF_COMMIT_GE_|己方提交队列≥",
                "SELF_CLOUD_GE_|己方云端栈≥", "SELF_PULL_GE_|己方累计下载≥",
                "OPP_DISCARD_GE_|对方累计弃牌≥", "SELF_AMBUSH_GE_|己方盖放伏击≥"
        };
        for (String f : families) {
            int bar = f.indexOf('|');
            String prefix = f.substring(0, bar);
                Integer n = threshold(cond, prefix);
                if (n != null) return f.substring(bar + 1) + n;
        }
        if (cond.equals("SELF_LEADER_ON_FIELD")) return "己方统领在场";
        if (cond.equals("OPP_LEADER_ON_FIELD")) return "对方统领在场";
        return cond;
    }

    private static void apply(Game g, int srcIdx, CardInstance srcCard, EffectSpec e, Ctx ctx, boolean checkAll) {
        apply(g, srcIdx, srcCard, e, ctx, checkAll, null);
    }

    private static void apply(Game g, int srcIdx, CardInstance srcCard, EffectSpec e, Ctx ctx,
                              boolean checkAll, Integer selectedTargetId) {
        PlayerState self = g.players[srcIdx];
        PlayerState enemy = g.opponentOf(srcIdx);
        boolean leaderSource = srcCard != null && srcCard.def.leader;
        switch (e.action) {

            case "DAMAGE": {
                if (isSingleEnemyTarget(e.target)) {
                    g.resolveSingleEnemyDamage(srcIdx, srcCard, e, e.amount, "造成 " + e.amount + " 点单体伤害", true, true);
                } else {
                    for (CardInstance t : pickTargets(g, srcIdx, srcCard, e, ctx, "造成 " + e.amount + " 点伤害", selectedTargetId)) g.dealDamage(t, e.amount);
                    if (isFaceTarget(e.target)) hitFace(g, srcIdx, srcCard, enemy, e.amount, e);
                    if ("ENEMY_PLAYER".equals(e.target)) g.damagePlayerLife(enemy, e.amount);
                    if ("SELF_PLAYER".equals(e.target)) g.damagePlayerLife(self, e.amount);
                    if (!ctx.deferDeaths) g.cleanupDeaths();
                }
                break;
            }
            case "HEAL": {
                for (CardInstance t : pickTargets(g, srcIdx, srcCard, e, ctx, "恢复 " + e.amount + " 点生命", selectedTargetId)) {
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
                for (CardInstance t : pickTargets(g, srcIdx, srcCard, e, ctx, "破坏目标", selectedTargetId)) {
                    if (t.isLeaderEntity) { g.log("统领【" + t.def.name + "】免疫离场效果！"); continue; }
                    PlayerState controller = g.players[t.controllerIdx()];
                    PlayerState owner = g.players[t.ownerIdx];
                    if (owner.protectedThisTurn) { g.log("【" + t.def.name + "】受庇护，无法被破坏"); continue; }
                    controller.field.remove(t);
                    owner.graveyard.add(t);
                    g.log("【" + t.def.name + "】被破坏（效果立即失效）");
                }
                break;
            }
            case "ENFEEBLE": {
                if (e.amount >= 0) {
                    g.log("无力效果 amount=" + e.amount + " 非负，已跳过");
                    break;
                }
                String mode = e.param == null || e.param.trim().isEmpty()
                        ? "both" : e.param.trim().toLowerCase();
                if (!mode.equals("atk") && !mode.equals("hp") && !mode.equals("both")) {
                    g.log("无力效果 param=" + e.param + " 非法，已跳过");
                    break;
                }
                for (CardInstance t : pickTargets(g, srcIdx, srcCard, e, ctx, "无力", selectedTargetId)) {
                    if (mode.equals("atk") || mode.equals("both"))
                        t.attack = Math.max(0, saturatingAdd(t.attack, e.amount));
                    if (mode.equals("hp") || mode.equals("both")) {
                        t.health = saturatingAdd(t.health, e.amount);
                        t.maxHealth = Math.max(0, saturatingAdd(t.maxHealth, e.amount));
                    }
                    g.log("【" + t.def.name + "】受到无力 → " + t.attack + "/" + t.health);
                }
                break;
            }
            case "BANISH": {
                for (CardInstance t : pickTargets(g, srcIdx, srcCard, e, ctx, "驱逐目标", selectedTargetId)) {
                    if (t.isLeaderEntity) { g.log("统领【" + t.def.name + "】免疫驱逐效果！"); continue; }
                    PlayerState owner = g.players[t.ownerIdx];
                    PlayerState controller = g.players[t.controllerIdx()];
                    // 驱逐不是破坏：从当前控制者场上取出，清理旧区域后重置，
                    // 再放回原拥有者牌库并洗牌；不进入任一方墓地。
                    controller.field.remove(t);
                    owner.hand.remove(t);
                    owner.graveyard.remove(t);
                    owner.deck.remove(t);
                    t.resetRuntimeState();
                    owner.deck.add(t);
                    Collections.shuffle(owner.deck, g.rng);
                    g.log("【" + t.def.name + "】被驱逐回" + owner.name + "的牌库");
                }
                break;
            }
            case "CONTROL": {
                if (e.amount < 1) {
                    g.log("操纵效果 amount=" + e.amount + " 非法，已跳过");
                    break;
                }
                PlayerState controller = g.players[srcIdx];
                for (CardInstance t : pickTargets(g, srcIdx, srcCard, e, ctx, "操纵目标", selectedTargetId)) {
                    if (t.isLeaderEntity) { g.log("统领【" + t.def.name + "】免疫操纵效果！"); continue; }
                    int previousControllerIdx = t.controllerIdx();
                    if (previousControllerIdx == srcIdx) {
                        g.log("【" + t.def.name + "】已经由" + controller.name + "控制，操纵落空");
                        continue;
                    }
                    g.players[previousControllerIdx].field.remove(t);
                    if (!controller.field.contains(t)) controller.field.add(t);
                    t.controlledByIdx = srcIdx;
                    t.controlTurnsRemaining = e.amount;
                    g.log("【" + t.def.name + "】暂时由" + controller.name + "控制 " + e.amount + " 回合");
                }
                break;
            }
            case "BUFF": {
                if (e.amount == 0) { g.log("强化效果 amount=0 无效"); break; }
                PlayerState caster = g.players[srcIdx];
                // 古木增幅（对照 C# EffectRuntime.Combat.Buff，RULES §12.2）：
                //   param=root/rampant 或来源为古木阵营 → 吃扎根/疯长层数
                //   effective = (amount + rootStacks) × 2^min(3, rampantStacks)
                //   发生增幅时目标封印（攻击归 0、失去关键词与护盾）
                String rawMode = e.param.isEmpty() ? "both" : e.param.toLowerCase();
                boolean growthMode = rawMode.equals("root") || rawMode.equals("rampant");
                String mode = growthMode ? "both" : rawMode;
                if (!mode.equals("atk") && !mode.equals("hp") && !mode.equals("both")) {
                    g.log("强化效果 param=" + e.param + " 非法，已跳过（合法值：both/atk/hp/root/rampant）");
                    break;
                }
                boolean woodSource = srcCard != null && "古木圣地".equals(srcCard.def.faction);
                boolean growthTarget = isGrowthTarget(e.target);
                int rootLayers = growthTarget && (rawMode.equals("root") || woodSource) ? caster.rootStacks : 0;
                int rampantLayers = growthTarget && (rawMode.equals("rampant") || woodSource) ? caster.rampantStacks : 0;
                int effective = applyGrowth(e.amount, rootLayers, rampantLayers);
                boolean growthApplied = effective != e.amount;
                for (CardInstance t : pickTargets(g, srcIdx, srcCard, e, ctx, "强化", selectedTargetId)) {
                    if (mode.equals("atk") || mode.equals("both"))
                        t.attack = Math.max(0, saturatingAdd(t.attack, effective));
                    if (mode.equals("hp") || mode.equals("both")) {
                        // 顺序对齐 C# EffectRuntime.Combat.Buff：先加当前生命，再由新的当前生命推最大生命
                        t.health = saturatingAdd(t.health, effective);
                        t.maxHealth = Math.max(0, saturatingAdd(t.maxHealth, effective));
                    }
                    if (growthApplied) { t.sealed = true; t.attack = 0; t.shield = false; }
                    g.log("【" + t.def.name + "】获得强化 → " + t.attack + "/" + t.health
                            + (growthApplied ? "（增幅 " + effective + "，封印）" : ""));
                }
                break;
            }
            case "ADD_ROOT": {
                PlayerState rt = g.players[srcIdx];
                // C# AddRoot 只接受正数；amount<=0 视为无效并跳过
                if (e.amount <= 0) { g.log("扎根效果 amount=" + e.amount + " 无效"); break; }
                rt.rootStacks = Math.max(0, saturatingAdd(rt.rootStacks, e.amount));
                g.log("扎根层数 → " + rt.rootStacks + "（每层 +1/+1）");
                break;
            }
            case "ADD_RAMPANT": {
                PlayerState rp = g.players[srcIdx];
                if (e.amount <= 0) { g.log("疯长效果 amount=" + e.amount + " 无效"); break; }
                rp.rampantStacks = (int) Math.min(3L, Math.max(0, (long) rp.rampantStacks + e.amount));
                g.log("疯长层数 → " + rp.rampantStacks + "（每层 ×2，上限 3）");
                break;
            }
            case "GRANT_KEYWORD": {
                for (CardInstance t : pickTargets(g, srcIdx, srcCard, e, ctx, "赋予【" + e.param + "】", selectedTargetId)) {
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
                for (CardInstance t : pickTargets(g, srcIdx, srcCard, e, ctx, "恢复攻击机会", selectedTargetId)) {
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
            case "ROLLBACK": { // 机械 B 模式：把提交队列中的一张卡回滚回手（RULES §12.4）
                PlayerState owner = g.players[srcIdx];
                if (owner.commitQueue.isEmpty()) { g.log("提交队列为空，回滚落空"); break; }
                CardInstance chosen = owner.commitQueue.size() == 1
                        ? owner.commitQueue.get(0)
                        : g.agentOf(srcIdx).chooseRollbackTarget(g, srcIdx, new ArrayList<>(owner.commitQueue));
                if (chosen == null || !owner.commitQueue.contains(chosen)) {
                    g.log("回滚拒绝：有多个提交队列候选但未选择合法目标");
                    break;
                }
                owner.commitQueue.remove(chosen);
                chosen.resetRuntimeState();
                owner.hand.add(chosen);
                g.log("【" + chosen.def.name + "】从提交队列回滚回手");
                break;
            }
            case "COMMIT": { // 机械：把一张己方机械卡提交进提交队列（effect 动作，非玩家主动 COMMIT）
                // 目标语义与 C# EffectRuntime.Mechanical.CommitCard 一致：
                // target=SELF 表示"效果来源卡自身"；否则需显式选中己方场上的一张机械卡。
                CardInstance commitTarget = null;
                if ("SELF".equals(e.target)) {
                    commitTarget = srcCard != null && self.field.contains(srcCard) ? srcCard : null;
                } else if (selectedTargetId != null) {
                    for (CardInstance c : self.field) {
                        if (c.uid == selectedTargetId) { commitTarget = c; break; }
                    }
                }
                if (commitTarget == null) { g.log("提交落空：没有合法的己方机械卡"); break; }
                // 用引擎自己的完整提交校验（阶段/场上/机械/minion/重复提交/效果已注册），
                // 而不是在这里重写一遍规则 —— 规则只能有一个真源。
                String why = g.whyCannotCommit(commitTarget);
                if (why != null) { g.log("提交被拒：" + why); break; }
                g.commitCard(commitTarget);
                break;
            }
            case "PUSH": { // 机械：上传 —— 正常是结束阶段的自动步骤；卡牌效果可显式触发（RULES §12.4）
                g.pushQueue(self);
                break;
            }
            case "PULL": { // 机械：下载云端栈顶（= Pull），推进胜利计数
                int times = Math.max(1, e.amount);
                for (int i = 0; i < times; i++) {
                    if (g.over()) break;
                    if (!g.pull()) break;   // 无载体/栈空/效果未注册时 pull() 自身会返回 false 并记日志
                }
                break;
            }
            default:
                g.log("（未知效果动作 " + e.action + "，已跳过——可在编辑器中自定义后由引擎扩展）");
        }
        if (checkAll) g.checkAll();
    }

    /** 古木增幅公式（与 C# EffectRuntime.Combat.ApplyGrowth 一致）：
     *  effective = (base + rootStacks) × 2^min(3, rampantStacks)，溢出饱和到 Integer.MAX_VALUE。 */
    private static int applyGrowth(int base, int root, int rampant) {
        if (base <= 0 || (root == 0 && rampant == 0)) return base;
        long additive = (long) base + root;
        long multiplier = 1L << Math.min(3, Math.max(0, rampant));
        long result = additive * multiplier;
        return result > Integer.MAX_VALUE ? Integer.MAX_VALUE : (int) result;
    }

    /** 与 C# EffectRuntime.SaturatingAdd 一致：long 相加后按 int 饱和。 */
    private static int saturatingAdd(int value, int amount) {
        long result = (long) value + amount;
        if (result > Integer.MAX_VALUE) return Integer.MAX_VALUE;
        if (result < Integer.MIN_VALUE) return Integer.MIN_VALUE;
        return (int) result;
    }

    /** 与 C# EffectRuntime.Combat.IsGrowthTarget 一致：空/未声明、SELF、己方单体、己方全体才算增幅目标。 */
    private static boolean isGrowthTarget(String target) {
        if (target == null || target.isEmpty()) return true;
        return "SELF".equals(target)
                || "FRIENDLY_MINION".equals(target)
                || "ALL_FRIENDLY_MINIONS".equals(target);
    }

    private static boolean isFaceTarget(String t) { return "ENEMY_FACE".equals(t); }

    private static boolean isSingleEnemyTarget(String t) {
        return "ENEMY_TARGET".equals(t) || "ENEMY_SINGLE".equals(t) || "SINGLE_ENEMY".equals(t);
    }

    private static void hitFace(Game g, int srcIdx, CardInstance srcCard, PlayerState enemy, int amt, EffectSpec effect) {
        // 单体核心伤害：王城存在时不再无脑吸收，由玩家/AI 在王城与已登场核心之间选择；统领抗性由【弑君】绕过。
        g.damageEnemyCore(srcIdx, srcCard, effect, amt, "对统领/玩家伤害", true);
    }

    /** 目标解析。扰魔(WARD)随从不能成为指定型效果的目标；ALL_* 群体效果无视扰魔，但默认不伤害统领。 */
    private static List<CardInstance> pickTargets(Game g, int srcIdx, CardInstance srcCard, EffectSpec e, Ctx ctx,
                                                String prompt) {
        return pickTargets(g, srcIdx, srcCard, e, ctx, prompt, null);
    }

    private static List<CardInstance> pickTargets(Game g, int srcIdx, CardInstance srcCard, EffectSpec e, Ctx ctx,
                                                 String prompt, Integer selectedTargetId) {
        PlayerState self = g.players[srcIdx];
        PlayerState enemy = g.opponentOf(srcIdx);
        List<CardInstance> r = new ArrayList<>();
        // 合法动作预选的目标优先：单目标下载效果必须在动作中明确选择（RULES §12.4）
        Integer hint = selectedTargetId != null ? selectedTargetId : (ctx != null ? ctx.selectedTargetId : null);
        if (hint != null) {
            CardInstance pinned = findInFieldTargets(g, self, enemy, srcCard, e, ctx, hint);
            if (pinned != null) { r.add(pinned); return r; }
            // 合法动作携带了显式选择却选择了不存在/不合法的实体时，
            // 不得回退到代理默认的第一张候选，避免失效选择产生隐式副作用。
            g.log("效果目标选择无效，效果落空");
            return r;
        }
        switch (e.target) {
            case "SELF": {
                // 效果来源卡自身（须为己方场上存活随从）
                if (srcCard != null && self.field.contains(srcCard)
                        && srcCard.def.isMinion() && (srcCard.health > 0 || ctx.deferDeaths)) r.add(srcCard);
                break;
            }
            case "ENEMY_MINION": {
                List<CardInstance> opts = new ArrayList<>();
                for (CardInstance m : fieldMinions(enemy, ctx)) {
                    if (m.has(CardDef.KW_WARD)) continue;
                    if (m.isLeaderEntity && !g.canAffectLeader(srcCard, e, m)) continue;
                    opts.add(m);
                }
                CardInstance t = choose(g, srcIdx, opts, prompt);
                if (t != null) r.add(t);
                break;
            }
            case "FRIENDLY_MINION": {
                CardInstance t = choose(g, srcIdx, fieldMinions(self, ctx), prompt);
                if (t != null) r.add(t);
                break;
            }
            case "ANY_MINION": {
                List<CardInstance> opts = new ArrayList<>(fieldMinions(self, ctx));
                for (CardInstance m : fieldMinions(enemy, ctx)) {
                    if (m.has(CardDef.KW_WARD)) continue;
                    if (m.isLeaderEntity && !g.canAffectLeader(srcCard, e, m)) continue;
                    opts.add(m);
                }
                CardInstance t = choose(g, srcIdx, opts, prompt);
                if (t != null) r.add(t);
                break;
            }
            case "ALL_ENEMY_MINIONS":
                for (CardInstance m : fieldMinions(enemy, ctx)) if (!m.isLeaderEntity || g.canAffectLeader(srcCard, e, m)) r.add(m);
                break;
            case "ALL_FRIENDLY_MINIONS": r.addAll(fieldMinions(self, ctx)); break;
            case "ALL_MINIONS":
                r.addAll(fieldMinions(self, ctx));
                for (CardInstance m : fieldMinions(enemy, ctx)) if (!m.isLeaderEntity || g.canAffectLeader(srcCard, e, m)) r.add(m);
                break;
            default: break;
        }
        return r;
    }

    private static List<CardInstance> fieldMinions(PlayerState player, Ctx ctx) {
        List<CardInstance> result = new ArrayList<>();
        for (CardInstance card : player.field) {
            if (card.def.isMinion() && (card.health > 0 || ctx.deferDeaths)) result.add(card);
        }
        return result;
    }

    /**
     * 预先选定的目标实例：只有落在该 target 类型的合法集合里才被采用。
     * 与 C# EffectTargetResolver 一致——引擎的合法动作在动作生成时已做过同样的筛选，
     * 这里只做防御性校验，非法时不静默随机，而是记录并落空。
     */
    private static CardInstance findInFieldTargets(Game g, PlayerState self, PlayerState enemy,
                                                   CardInstance srcCard, EffectSpec e, Ctx ctx, int uid) {
        if (e.target == null) return null;
        switch (e.target) {
            case "FRIENDLY_MINION":
                for (CardInstance c : fieldMinions(self, ctx)) if (c.uid == uid) return c;
                return null;
            case "ALL_FRIENDLY_MINIONS":
                for (CardInstance c : fieldMinions(self, ctx)) if (c.uid == uid) return c;
                return null;
            case "ENEMY_MINION":
                for (CardInstance c : fieldMinions(enemy, ctx)) {
                    if (c.has(CardDef.KW_WARD)) continue;
                    if (c.isLeaderEntity && !g.canAffectLeader(srcCard, e, c)) continue;
                    if (c.uid == uid) return c;
                }
                return null;
            case "ANY_MINION":
                    for (CardInstance c : fieldMinions(self, ctx)) if (c.uid == uid) return c;
                for (CardInstance c : fieldMinions(enemy, ctx)) {
                    if (c.has(CardDef.KW_WARD)) continue;
                    if (c.isLeaderEntity && !g.canAffectLeader(srcCard, e, c)) continue;
                    if (c.uid == uid) return c;
                }
                return null;
            case "SELF":
                if (srcCard != null && srcCard.uid == uid
                        && self.field.contains(srcCard) && srcCard.def.isMinion()
                        && (srcCard.health > 0 || ctx.deferDeaths)) return srcCard;
                return null;
            default: return null;   // ALL_ENEMY_MINIONS / 核心目标等不接受单体预选
        }
    }

    private static CardInstance choose(Game g, int srcIdx, List<CardInstance> opts, String prompt) {
        if (opts.isEmpty()) return null;
        if (opts.size() == 1) return opts.get(0);
        return g.agents[srcIdx].chooseTarget(g, srcIdx, opts, prompt, false);
    }
}
