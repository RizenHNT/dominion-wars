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

    // 吟唱
    public int chantRemaining = 0;

    // 惩罚牌/惩罚效果激活状态：仅当因对方惩罚抽入手牌时为 true
    public boolean punishActivated = false;

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
        this.chantRemaining = 0;
        this.punishActivated = false;
        this.isLeaderEntity = false;
    }

    public boolean isMinionOnField() { return def.isMinion() || (isLeaderEntity && def.isMinion()); }
    public boolean has(String kw) { return keywords.contains(kw); }
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
