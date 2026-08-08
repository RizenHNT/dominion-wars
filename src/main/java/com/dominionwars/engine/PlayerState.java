package com.dominionwars.engine;

import com.dominionwars.model.CardDef;

import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;

/** 单个玩家的全部运行时状态 */
public class PlayerState {
    public final int idx;
    public String name;

    public final List<CardInstance> deck = new ArrayList<>();      // 顶部 = 末尾
    public final List<CardInstance> hand = new ArrayList<>();
    public final List<CardInstance> field = new ArrayList<>();     // 随从与吟唱中的卡
    public final List<CardInstance> ambushes = new ArrayList<>();  // 盖放的伏击
    public final List<CardInstance> graveyard = new ArrayList<>();

    public CardInstance leaderOnField = null;   // 统领实体（随从在 field、咒文在统领区、伏击在 ambushes，引用同一实例）
    public Integer life = null;                 // 统领赋予的玩家生命（null=无此胜负字段）

    // 词条使用记录（玩家交替时重置）
    public final Set<String> usedTags = new LinkedHashSet<>();

    // 回合内标记
    public boolean ambushSetThisTurn = false;       // 本回合已盖放伏击
    public boolean focusAmbushLockThisTurn = false; // 专注伏击触发后压制其他伏击
    public int turnPunishDelta = 0;                 // 本回合自己卡牌惩罚值修正（机械统领+5 等）
    public boolean punishToSelfDiscardThisTurn = false; // 深海统领：本回合惩罚值转为弃自己牌
    public boolean protectedThisTurn = false;       // 古木统领：本回合己方卡牌不被破坏/反制
    public boolean effectsNegatedThisTurn = false;  // 命运之影：本回合场上卡牌效果无效
    public int punishDrawnThisTurn = 0;             // 本回合因惩罚抽到的张数（机械胜利条件）

    // 长期计数
    public int reshuffleCount = 0;                  // 自己发生过的有效牌库循环次数（记录用）
    public int cycleWinCount = 0;                   // 胜利计数：对手每次有效牌库循环时 +1
    public int skipReshuffleCredits = 0;            // 机械惩罚效果：下次洗牌不计入对手胜利计数
    public int totalDiscarded = 0;                  // 深海胜利条件
    public int noDamageTurns = 0;                   // 古木胜利条件（连续未受伤回合）
    public boolean damagedThisCycle = false;

    public PlayerState(int idx, String name) { this.idx = idx; this.name = name; }

    public boolean leaderFielded() { return leaderOnField != null; }

    /** 统领是否被「命运之影」类效果禁用（由 Game 计算后写入） */
    public boolean leaderDisabled = false;

    public boolean hasTaunt() {
        for (CardInstance c : field) if (c.def.isMinion() && c.has(CardDef.KW_TAUNT) && c.health > 0) return true;
        return false;
    }

    public List<CardInstance> minions() {
        List<CardInstance> r = new ArrayList<>();
        for (CardInstance c : field) if (c.def.isMinion() && c.health > 0) r.add(c);
        return r;
    }

    public void resetTagsOnAlternation() { usedTags.clear(); }

    public void clearTurnFlags() {
        ambushSetThisTurn = false;
        focusAmbushLockThisTurn = false;
        turnPunishDelta = 0;
        punishToSelfDiscardThisTurn = false;
        protectedThisTurn = false;
        effectsNegatedThisTurn = false;
        punishDrawnThisTurn = 0;
        for (CardInstance c : field) { c.attacksUsed = 0; c.summonedThisTurn = false; }
    }

    public boolean hasLockdownAmbush() {
        for (CardInstance a : ambushes)
            if (a.def.ambushKind == CardDef.AmbushKind.LOCKDOWN) return true;
        return false;
    }
}
