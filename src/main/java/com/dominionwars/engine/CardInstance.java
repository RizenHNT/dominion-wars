package com.dominionwars.engine;

import com.dominionwars.model.CardDef;

import java.util.LinkedHashSet;
import java.util.Set;
import java.util.concurrent.atomic.AtomicInteger;

/** 运行时卡牌实例 */
public class CardInstance {
    private static final AtomicInteger SEQ = new AtomicInteger(1);

    public final int uid = SEQ.getAndIncrement();
    public final CardDef def;
    public final int ownerIdx;

    // 随从/统领状态
    public int attack, health, maxHealth;
    public int attacksUsed = 0;
    public int attacksPerTurn;
    public boolean summonedThisTurn = false;
    public boolean shield;                  // 圣盾未消耗
    public Set<String> keywords = new LinkedHashSet<>();

    // 统领
    public boolean isLeaderEntity = false;  // 该实例以统领身份在场
    public int durability = 0;              // 非随从统领耐久

    /** CONTROL 的临时控制者；所有权仍由 ownerIdx 保持不变。 */
    public Integer controlledByIdx = null;
    /** 控制者回合结束时递减；归零后回到 ownerIdx 的场上。 */
    public int controlTurnsRemaining = 0;

    // 吟唱
    public int chantRemaining = 0;

    // 惩罚牌/惩罚效果激活状态：仅当因对方惩罚抽入手牌时为 true
    public boolean punishActivated = false;

    /** 古木封印：受扎根/疯长增幅强化的单位（攻击归0、失去特殊能力，仅保留身份/归属/区域/生命） */
    public boolean sealed = false;

    /** 机械：地标层数（下载次数独立计数器，不复用全局 pullCount；由 resetRuntimeState 清零） */
    public int landmarkPullCount = 0;
    /** 机械：地标吟唱完成后要晋升的统领卡 id（null=未排队晋升） */
    public String pendingLandmarkSummonCardId = null;
    /**
     * 机械：该实例是否已提交进提交队列。
     * 仅作 UI/审计标记，不参与合法性判定（C# CardInstance 没有对应字段）：
     * 防重复提交由「卡牌是否在己方场上 / 是否已在提交队列或云端栈」判定。
     */
    public boolean committed = false;

    public CardInstance(CardDef def, int ownerIdx) {
        this.def = def;
        this.ownerIdx = ownerIdx;
        this.attack = def.attack;
        this.health = def.health;
        this.maxHealth = def.health;
        this.attacksPerTurn = def.attacksPerTurn;
        this.keywords = new LinkedHashSet<>(def.keywords);
        this.shield = keywords.contains(CardDef.KW_SHIELD);
        this.durability = def.leader ? def.leaderDef.durability : 0;
    }

    /** 当前控制者；未被操纵时就是拥有者。 */
    public int controllerIdx() { return controlledByIdx == null ? ownerIdx : controlledByIdx; }

    /** 进入卡组/手牌前重置运行时状态，避免洗回后抽到 0 血或负血随从。 */
    public void resetRuntimeState() {
        this.attack = def.attack;
        this.health = def.health;
        this.maxHealth = def.health;
        this.attacksUsed = 0;
        this.attacksPerTurn = def.attacksPerTurn;
        this.summonedThisTurn = false;
        this.keywords = new LinkedHashSet<>(def.keywords);
        this.shield = keywords.contains(CardDef.KW_SHIELD);
        this.durability = def.leader ? def.leaderDef.durability : 0;
        this.controlledByIdx = null;
        this.controlTurnsRemaining = 0;
        this.chantRemaining = 0;
        this.punishActivated = false;
        this.isLeaderEntity = false;
        this.sealed = false;
        this.landmarkPullCount = 0;
        this.pendingLandmarkSummonCardId = null;
        this.committed = false;
    }

    public boolean isMinionOnField() { return def.isMinion() || (isLeaderEntity && def.isMinion()); }

    /** 封印单位失去全部关键词（含嘲讽/圣盾/突袭等），与 C# CardInstance.HasKeyword 一致 */
    public boolean has(String kw) { return !sealed && keywords.contains(kw); }
    public boolean canAttackNow() {
        if (!def.isMinion() || attack <= 0) return false;
        if (summonedThisTurn && !has(CardDef.KW_CHARGE)) return false;
        return attacksUsed < attacksPerTurn;
    }
    public boolean alive() {
        if (def.isMinion()) return health > 0;
        if (isLeaderEntity) return durability > 0 || def.leaderDef.durability == 0;
        return true;
    }

    public String shortName() { return def.name + "#" + uid; }
    @Override public String toString() {
        if (def.isMinion()) return def.name + "(" + attack + "/" + health + ")";
        return def.name;
    }
}
