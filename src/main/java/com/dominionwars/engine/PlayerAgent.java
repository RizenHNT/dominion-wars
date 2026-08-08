package com.dominionwars.engine;

import java.util.List;

/** 玩家决策接口：引擎在需要玩家选择时回调（人类→弹窗，AI→启发式） */
public interface PlayerAgent {

    /** 单体伤害目标：可表示真实卡牌目标，也可表示王城/统领等核心目标 */
    default Game.SingleDamageTarget chooseSingleDamageTarget(Game g, int playerIdx, java.util.List<Game.SingleDamageTarget> options, String prompt, boolean optional) {
        return options.isEmpty() ? null : options.get(0);
    }

    /** 核心目标选择：王城 / 已登场统领 / 玩家生命。供“打敌方统领/玩家”的伤害与攻击使用 */
    default Game.CoreTarget chooseCoreTarget(Game g, int playerIdx, java.util.List<Game.CoreTarget> options, String prompt, int amount, boolean optional) {
        return options.isEmpty() ? null : options.get(0);
    }

    /** 被惩罚抽到可激活的卡牌时：是否发动其【惩罚】效果 */
    boolean askActivatePunish(Game g, int playerIdx, CardInstance card, int cost);

    /** 从候选中选择一个目标；optional=true 时可返回 null 放弃 */
    CardInstance chooseTarget(Game g, int playerIdx, List<CardInstance> options, String prompt, boolean optional);

    /** 弃牌阶段：从手牌中选择一张弃置（引擎会循环调用直到达到上限） */
    CardInstance chooseDiscard(Game g, int playerIdx, List<CardInstance> hand);

    /** 多张伏击同时满足触发条件时选择其一（可返回 null 不触发） */
    CardInstance chooseAmbush(Game g, int playerIdx, List<CardInstance> candidates, String actionDesc);
}
