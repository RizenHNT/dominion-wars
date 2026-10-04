package com.dominionwars.model;

import com.dominionwars.util.Json;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * 卡牌定义（数据驱动，与 JSON 双向转换）。
 * 引擎不认识的字段统一保留在 custom 中，保证编辑器添加新字段不丢数据。
 */
public class CardDef {

    public enum CardType {
        MINION("随从"), SPELL("咒文"), AMBUSH("伏击"), PUNISH("惩罚");
        public final String cn;
        CardType(String cn) { this.cn = cn; }
        public static CardType of(String s) {
            for (CardType t : values()) if (t.name().equalsIgnoreCase(s) || t.cn.equals(s)) return t;
            return SPELL;
        }
    }

    /** 伏击三分类：普通(可累积) / 专注(触发回合压制其他伏击) / 封场(盖放期间不能再盖伏击) */
    public enum AmbushKind {
        NORMAL("普通"), FOCUS("专注"), LOCKDOWN("封场");
        public final String cn;
        AmbushKind(String cn) { this.cn = cn; }
        public static AmbushKind of(String s) {
            for (AmbushKind t : values()) if (t.name().equalsIgnoreCase(s) || t.cn.equals(s)) return t;
            return NORMAL;
        }
    }

    /** 关键词（随从机制） */
    public static final String KW_CHARGE = "突袭";   // 召唤当回合可攻击
    public static final String KW_TAUNT  = "嘲讽";   // 必须先被攻击
    public static final String KW_SHIELD = "圣盾";   // 抵消第一次伤害
    public static final String KW_WARD   = "扰魔";   // 不能成为咒文/惩罚效果的目标
    public static final String KW_ETERNAL= "永续";   // 标记型关键词(配合永续效果)

    /** 单条效果（动作 DSL） */
    public static class EffectSpec {
        public String action = "DAMAGE";     // 见 Effects 类支持的动作表
        public String target = "NONE";       // ENEMY_MINION / ALL_ENEMY_MINIONS / FRIENDLY_MINION / ALL_FRIENDLY_MINIONS / ENEMY_FACE / SELF_PLAYER / ENEMY_PLAYER / ALL_MINIONS / SELF / NONE
        public int amount = 0;
        public String param = "";            // 附加参数(关键词名/召唤卡id/胜利提示等)
        /** 该具体效果是否可以绕过统领抗性；null 表示回退到旧的卡级字段。 */
        public Boolean kingSlayer = null;
        /**
         * 每效果的发动条件（与 C# {@code EffectSpec.Condition} 对应）。
         *
         * <p>2026-09-13 补齐：此前 Java 把 {@code condition} 塞进 {@link #extra} 后
         * <b>从不求值</b>，而 C# 在 {@code EffectDispatcher} 里会拦截 —— 同一份卡牌数据
         * 两引擎行为分歧且是静默的。现在两侧都由各自的效果循环在结算前检查。
         * 空串/null = 无条件。未知词条 fail-closed（见 {@code Effects.checkCondition}）。
         */
        public String condition = "";

        /** 未识别的未来字段原样保留（往返不丢数据）。 */
        public Map<String, Object> extra = new LinkedHashMap<>();

        public static EffectSpec fromMap(Map<String, Object> m) {
            EffectSpec e = new EffectSpec();
            e.action = Json.str(m, "action", "DAMAGE");
            e.target = Json.str(m, "target", "NONE");
            e.amount = Json.integer(m, "amount", 0);
            e.param = Json.str(m, "param", "");
            if (m.containsKey("kingSlayer")) e.kingSlayer = Json.bool(m, "kingSlayer", false);
            e.condition = Json.str(m, "condition", "");
            for (Map.Entry<String, Object> en : m.entrySet())
                if (!en.getKey().matches("action|target|amount|param|kingSlayer|condition")) e.extra.put(en.getKey(), en.getValue());
            return e;
        }
        public Map<String, Object> toMap() {
            Map<String, Object> m = new LinkedHashMap<>();
            m.put("action", action);
            if (!"NONE".equals(target)) m.put("target", target);
            if (amount != 0) m.put("amount", (long) amount);
            if (!param.isEmpty()) m.put("param", param);
            if (kingSlayer != null) m.put("kingSlayer", kingSlayer);
            if (!condition.isEmpty()) m.put("condition", condition);
            m.putAll(extra);
            return m;
        }
        @Override public String toString() {
            return action + (target.equals("NONE") ? "" : "→" + target) + (amount != 0 ? " ×" + amount : "") + (param.isEmpty() ? "" : " [" + param + "]");
        }
    }

    /**
     * 地标层级（机械 B 模式，RULES §12.4）。
     * 与 C# LandmarkTierDefinition 对齐：tier 必填，effect 为展示文本，
     * effectSpecs 为该层实际结算的效果，chant / summon 为吟唱晋升字段。
     */
    public static class LandmarkTier {
        public int tier = 1;
        public String effectText = "";
        public List<EffectSpec> effectSpecs = new ArrayList<>();
        public int chant = 0;
        public String summon = "";

        public static LandmarkTier fromMap(Map<String, Object> m) {
            LandmarkTier t = new LandmarkTier();
            if (m == null) return t;
            t.tier = Json.integer(m, "tier", 1);
            t.effectText = Json.str(m, "effect", "");
            t.chant = Json.integer(m, "chant", 0);
            t.summon = Json.str(m, "summon", "");
            for (Object o : Json.list(m, "effectSpecs")) t.effectSpecs.add(EffectSpec.fromMap(castMap(o)));
            return t;
        }

        public Map<String, Object> toMap() {
            Map<String, Object> m = new LinkedHashMap<>();
            m.put("tier", (long) tier);
            if (!effectText.isEmpty()) m.put("effect", effectText);
            if (!effectSpecs.isEmpty()) m.put("effectSpecs", specs(effectSpecs));
            if (chant > 0) m.put("chant", (long) chant);
            if (!summon.isEmpty()) m.put("summon", summon);
            return m;
        }
    }

    /** 统领配置：胜负字段可叠加（自身耐久 / 赋予玩家生命 / 特殊胜利条件） */
    public static class LeaderDef {
        public int grantLife = 0;        // >0: 为已方开启「生命池」。生命池不构成胜利或败北条件（RULES §1/§7）
        public int durability = 0;       // >0: 非随从统领自身耐久，归0被击败（随从统领用攻/血）
        public String winCondition = "NONE";
        /**
         * 统领记载的胜利目标 —— 与 C# 对齐的统一接口（2026-09-12）。
         * 声明后即以它为准；未声明时回退到 {@link #winCondition}/{@link #winParam}。
         */
        public VictoryObjective victory = null;
        /** 统领允许被弑君效果命中的动作白名单；空集合表示全免疫。 */
        public Set<String> vulnerabilities = new LinkedHashSet<>();
        // 兼容字段（老卡牌）：NONE / OPP_DISCARD_TOTAL_GE / NO_DAMAGE_TURNS_GE /
        // OPP_PUNISH_DRAW_TURN_GE / PULL_TOTAL_GE / GIANT_HEALTH_GE / ROYAL_CASTLE_BREAK
        public int winParam = 0;
        public String winText = "";
        public List<EffectSpec> punishEffects = new ArrayList<>();   // 被惩罚抽到时的强力效果
        public List<EffectSpec> enterEffects = new ArrayList<>();    // 登场效果
        public List<EffectSpec> persistentEffects = new ArrayList<>(); // 永续光环(在场期间)

        // ── 机械 B 模式地标（RULES §12.4）──
        public boolean isLandmark = false;                              // 该统领以护符/地标形态在场并可接收下载
        public List<LandmarkTier> landmarkTiers = new ArrayList<>();    // 按层推进：2 层 → 吟唱1 → 摧毁地标并召唤 Alpha

        public static LeaderDef fromMap(Map<String, Object> m) {
            LeaderDef l = new LeaderDef();
            if (m == null) return l;
            l.grantLife = Json.integer(m, "grantLife", 0);
            l.durability = Json.integer(m, "durability", 0);
            l.winCondition = Json.str(m, "winCondition", "NONE");
            // 统一接口：优先读 victory；未声明时由 fromLeaderDef 回退到老字段。
            l.victory = VictoryObjective.fromLeaderDef(m);
            l.vulnerabilities = new LinkedHashSet<>(Json.strList(m, "vulnerabilities"));
            l.winParam = Json.integer(m, "winParam", 0);
            l.winText = Json.str(m, "winText", "");
            l.isLandmark = Json.bool(m, "isLandmark", false);
            for (Object o : Json.list(m, "landmarkTiers")) l.landmarkTiers.add(LandmarkTier.fromMap(castMap(o)));
            for (Object o : Json.list(m, "punishEffects")) l.punishEffects.add(EffectSpec.fromMap(castMap(o)));
            for (Object o : Json.list(m, "enterEffects")) l.enterEffects.add(EffectSpec.fromMap(castMap(o)));
            for (Object o : Json.list(m, "persistentEffects")) l.persistentEffects.add(EffectSpec.fromMap(castMap(o)));
            return l;
        }

        /** 该层是否声明了吟唱/召唤晋升（对应 C# Chant>0 || SummonCardId 非空）。 */
        public LandmarkTier tierAt(int pullCount) {
            for (LandmarkTier t : landmarkTiers) if (t.tier == pullCount) return t;
            return null;
        }

        public Map<String, Object> toMap() {
            Map<String, Object> m = new LinkedHashMap<>();
            if (grantLife > 0) m.put("grantLife", (long) grantLife);
            if (durability > 0) m.put("durability", (long) durability);
            m.put("winCondition", winCondition);
            if (victory != null) m.put("victory", victory.toMap());
            if (!vulnerabilities.isEmpty()) m.put("vulnerabilities", new ArrayList<Object>(vulnerabilities));
            if (winParam != 0) m.put("winParam", (long) winParam);
            if (!winText.isEmpty()) m.put("winText", winText);
            if (isLandmark) m.put("isLandmark", true);
            if (!landmarkTiers.isEmpty()) {
                List<Object> arr = new ArrayList<>();
                for (LandmarkTier t : landmarkTiers) arr.add(t.toMap());
                m.put("landmarkTiers", arr);
            }
            m.put("punishEffects", specs(punishEffects));
            if (!enterEffects.isEmpty()) m.put("enterEffects", specs(enterEffects));
            if (!persistentEffects.isEmpty()) m.put("persistentEffects", specs(persistentEffects));
            return m;
        }
    }

    // ===================== 基本字段 =====================
    public String id = "";
    public String name = "";
    public String faction = "无阵营";
    public CardType type = CardType.SPELL;
    public List<String> tags = new ArrayList<>();   // 词条：破坏/弃牌/恢复/增强/统领/...
    public int punish = 1;                          // 基础惩罚值（自己回合主动打出）
    public String text = "";
    public String flavor = "";

    // 随从
    public int attack = 0;
    public int health = 0;
    public int attacksPerTurn = 1;
    public Set<String> keywords = new LinkedHashSet<>();

    // 吟唱（延迟 X 回合后触发 chantEffects）
    public int chant = 0;
    public List<EffectSpec> chantEffects = new ArrayList<>();

    // 出牌效果（咒文/随从战吼等）
    public List<EffectSpec> onPlayEffects = new ArrayList<>();

    // 对方因效果/惩罚转化弃牌时触发（本卡在己方场上时生效）——深海联动核心
    public List<EffectSpec> onOpponentDiscardEffects = new ArrayList<>();

    // 伏击
    public AmbushKind ambushKind = AmbushKind.NORMAL;
    public String ambushTrigger = "OPPONENT_PLAYS_CARD";
    // OPPONENT_PLAYS_CARD / OPPONENT_PLAYS_SPELL / OPPONENT_SUMMONS / OPPONENT_ATTACKS / OPPONENT_DRAWS
    public List<EffectSpec> ambushEffects = new ArrayList<>();

    // 惩罚发动（对方回合被惩罚抽到时）
    public boolean punishActivatable = false;       // 是否具有【惩罚】效果
    public int punishCost = 0;                      // 惩罚发动时的（降低后）惩罚值
    public String punishCondition = "ALWAYS";       // 额外发动条件: ALWAYS / SELF_LEADER_ON_FIELD / ENEMY_MINIONS_GE_n / HAND_GE_n / SELF_FIELD_GE_n
    public List<EffectSpec> punishEffects = new ArrayList<>();

    // ── 机械生命周期（RULES §12.4，字段与 C# CardDefinition 一致）──
    /** 提交（COMMIT）时点造成的惩罚抽牌数量；不是第二套法力费用。 */
    public int commitCost = 0;
    /** 上传（PUSH）时点额外造成的惩罚抽牌数量；普通机械默认 0，避免 COMMIT/PUSH 双算。 */
    public int uploadCost = 0;
    /** 下载（PULL）时点造成的惩罚抽牌数量。 */
    public int downloadCost = 0;
    public boolean hasCommitCost = false;           // 数据中是否显式声明（区别于显式 0）
    public boolean hasUploadCost = false;
    public boolean hasDownloadCost = false;
    public boolean hasPullEffects = false;
    public List<EffectSpec> commitEffects = new ArrayList<>();   // 提交瞬间结算
    public List<EffectSpec> pushEffects = new ArrayList<>();     // 进入云端栈一瞬结算
    public List<EffectSpec> pullEffects = new ArrayList<>();     // 下载结算

    // 统领
    public boolean leader = false;
    /** 护卫：本卡作为随从型统领时，若己方有其他非统领随从，敌方不能攻击本统领。 */
    public boolean guard = false;
    /** 旧版兼容字段：整张卡的弑君标记。新卡应在具体 EffectSpec 上声明。 */
    public boolean kingSlayer = false;
    public LeaderDef leaderDef = new LeaderDef();

    // 编辑器自定义扩展字段（引擎忽略但完整保留）
    public Map<String, Object> custom = new LinkedHashMap<>();

    private static final Set<String> KNOWN = Set.of(
            "id","name","faction","type","tags","punish","text","flavor",
            "attack","health","attacksPerTurn","keywords",
            "chant","chantEffects","onPlayEffects","onOpponentDiscardEffects",
            "ambushKind","ambushTrigger","ambushEffects",
            "punishActivatable","punishCost","punishCondition","punishEffects",
            "leader","guard","kingSlayer","leaderDef",
            "commitCost","uploadCost","downloadCost",
            "commitEffects","pushEffects","pullEffects");

    @SuppressWarnings("unchecked")
    static Map<String, Object> castMap(Object o) {
        return o instanceof Map ? (Map<String, Object>) o : new LinkedHashMap<>();
    }
    static List<Object> specs(List<EffectSpec> l) {
        List<Object> r = new ArrayList<>();
        for (EffectSpec e : l) r.add(e.toMap());
        return r;
    }

    public static CardDef fromMap(Map<String, Object> m) {
        CardDef c = new CardDef();
        c.id = Json.str(m, "id", "");
        c.name = Json.str(m, "name", c.id);
        c.faction = Json.str(m, "faction", "无阵营");
        c.type = CardType.of(Json.str(m, "type", "SPELL"));
        c.tags = Json.strList(m, "tags");
        c.punish = Json.integer(m, "punish", 1);
        c.text = Json.str(m, "text", "");
        c.flavor = Json.str(m, "flavor", "");
        c.attack = Json.integer(m, "attack", 0);
        c.health = Json.integer(m, "health", 0);
        c.attacksPerTurn = Json.integer(m, "attacksPerTurn", 1);
        c.keywords = new LinkedHashSet<>(Json.strList(m, "keywords"));
        c.chant = Json.integer(m, "chant", 0);
        for (Object o : Json.list(m, "chantEffects")) c.chantEffects.add(EffectSpec.fromMap(castMap(o)));
        for (Object o : Json.list(m, "onPlayEffects")) c.onPlayEffects.add(EffectSpec.fromMap(castMap(o)));
        for (Object o : Json.list(m, "onOpponentDiscardEffects")) c.onOpponentDiscardEffects.add(EffectSpec.fromMap(castMap(o)));
        c.ambushKind = AmbushKind.of(Json.str(m, "ambushKind", "NORMAL"));
        c.ambushTrigger = Json.str(m, "ambushTrigger", "OPPONENT_PLAYS_CARD");
        for (Object o : Json.list(m, "ambushEffects")) c.ambushEffects.add(EffectSpec.fromMap(castMap(o)));
        c.punishActivatable = Json.bool(m, "punishActivatable", c.type == CardType.PUNISH);
        c.punishCost = Json.integer(m, "punishCost", 0);
        c.punishCondition = Json.str(m, "punishCondition", "ALWAYS");
        for (Object o : Json.list(m, "punishEffects")) c.punishEffects.add(EffectSpec.fromMap(castMap(o)));
        c.hasCommitCost = m.containsKey("commitCost");
        c.hasUploadCost = m.containsKey("uploadCost");
        c.hasDownloadCost = m.containsKey("downloadCost");
        c.hasPullEffects = m.containsKey("pullEffects");
        c.commitCost = Json.integer(m, "commitCost", 0);
        c.uploadCost = Json.integer(m, "uploadCost", 0);
        c.downloadCost = Json.integer(m, "downloadCost", 0);
        for (Object o : Json.list(m, "commitEffects")) c.commitEffects.add(EffectSpec.fromMap(castMap(o)));
        for (Object o : Json.list(m, "pushEffects")) c.pushEffects.add(EffectSpec.fromMap(castMap(o)));
        for (Object o : Json.list(m, "pullEffects")) c.pullEffects.add(EffectSpec.fromMap(castMap(o)));
        c.leader = Json.bool(m, "leader", false);
        c.guard = Json.bool(m, "guard", false);
        c.kingSlayer = Json.bool(m, "kingSlayer", false);
        c.leaderDef = LeaderDef.fromMap(Json.map(m, "leaderDef"));
        for (Map.Entry<String, Object> e : m.entrySet())
            if (!KNOWN.contains(e.getKey())) c.custom.put(e.getKey(), e.getValue());
        c.applyMechanicalLifecycleDefaults();
        return c;
    }

    /**
     * 机械生命周期默认值（与 C# CardCatalog.ApplyMechanicalLifecycleDefaults 一致）。
     * 仅对「机械遗迹」阵营的非统领随从补齐 COMMIT=1 / PUSH=0 / PULL=1 与
     * 「PULL 后选择一个己方随从 +1/+1」；数据中显式声明的字段优先。
     */
    public void applyMechanicalLifecycleDefaults() {
        if (!isMinion() || leader || !"机械遗迹".equals(faction)) return;
        if (!hasCommitCost) { commitCost = 1; hasCommitCost = true; }
        if (!hasUploadCost) { uploadCost = 0; hasUploadCost = true; }
        if (!hasDownloadCost) { downloadCost = 1; hasDownloadCost = true; }
        if (!hasPullEffects) {
            EffectSpec buff = new EffectSpec();
            buff.action = "BUFF";
            buff.target = "FRIENDLY_MINION";
            buff.amount = 1;
            buff.param = "both";
            pullEffects.add(buff);
            hasPullEffects = true;
        }
    }

    public Map<String, Object> toMap() {
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("id", id);
        m.put("name", name);
        m.put("faction", faction);
        m.put("type", type.name());
        m.put("tags", new ArrayList<Object>(tags));
        m.put("punish", (long) punish);
        if (!text.isEmpty()) m.put("text", text);
        if (!flavor.isEmpty()) m.put("flavor", flavor);
        if (type == CardType.MINION || (leader && health > 0)) {
            m.put("attack", (long) attack);
            m.put("health", (long) health);
            if (attacksPerTurn != 1) m.put("attacksPerTurn", (long) attacksPerTurn);
        }
        if (!keywords.isEmpty()) m.put("keywords", new ArrayList<Object>(keywords));
        if (chant > 0) { m.put("chant", (long) chant); m.put("chantEffects", specs(chantEffects)); }
        if (!onPlayEffects.isEmpty()) m.put("onPlayEffects", specs(onPlayEffects));
        if (!onOpponentDiscardEffects.isEmpty()) m.put("onOpponentDiscardEffects", specs(onOpponentDiscardEffects));
        if (type == CardType.AMBUSH) {
            m.put("ambushKind", ambushKind.name());
            m.put("ambushTrigger", ambushTrigger);
            m.put("ambushEffects", specs(ambushEffects));
        }
        if (punishActivatable) {
            m.put("punishActivatable", true);
            m.put("punishCost", (long) punishCost);
            m.put("punishCondition", punishCondition);
            m.put("punishEffects", specs(punishEffects));
        }
        if (leader) m.put("leader", true);
        if (guard) m.put("guard", true);
        if (kingSlayer) m.put("kingSlayer", true);
        if (leader) m.put("leaderDef", leaderDef.toMap());
        // 机械生命周期字段：显式 0 也要写出，否则重载后会吃到默认 1/0/1
        if (hasCommitCost) m.put("commitCost", (long) commitCost);
        if (hasUploadCost) m.put("uploadCost", (long) uploadCost);
        if (hasDownloadCost) m.put("downloadCost", (long) downloadCost);
        if (!commitEffects.isEmpty()) m.put("commitEffects", specs(commitEffects));
        if (!pushEffects.isEmpty()) m.put("pushEffects", specs(pushEffects));
        if (hasPullEffects && !pullEffects.isEmpty()) m.put("pullEffects", specs(pullEffects));
        m.putAll(custom);
        return m;
    }

    public boolean isMinion() { return type == CardType.MINION; }

    /** UI/审计用：卡级兼容标记或任一具体效果带有弑君标记。 */
    public boolean hasKingSlayer() {
        if (kingSlayer) return true;
        return hasKingSlayer(onPlayEffects)
                || hasKingSlayer(chantEffects)
                || hasKingSlayer(ambushEffects)
                || hasKingSlayer(punishEffects)
                || hasKingSlayer(onOpponentDiscardEffects)
                || hasKingSlayer(commitEffects)
                || hasKingSlayer(pushEffects)
                || hasKingSlayer(pullEffects)
                || leaderDef != null && (hasKingSlayer(leaderDef.enterEffects)
                    || hasKingSlayer(leaderDef.punishEffects));
    }

    private static boolean hasKingSlayer(List<EffectSpec> effects) {
        for (EffectSpec effect : effects) if (Boolean.TRUE.equals(effect.kingSlayer)) return true;
        return false;
    }

    @Override public String toString() { return name + "(" + faction + "·" + type.cn + " 惩罚" + punish + ")"; }
}
