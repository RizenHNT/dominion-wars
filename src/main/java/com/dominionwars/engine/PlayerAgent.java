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

    // ===================== 机械 B 模式（RULES §12.4）=====================
    // 全部为 default 方法：人类代理（控制台/Swing/Web）无需实现即可继续编译，
    // 默认一律保守拒绝，只有 AiAgent 覆盖为实际策略。

    /** 提交（COMMIT）：从己方场上合法机械卡中选择一张提交；返回 null 表示本回合不提交 */
    default CardInstance chooseCommit(Game g, int playerIdx, List<CardInstance> candidates) {
        return null;
    }

    /**
     * 兼容旧代理的 PUSH 回调；上传本身是结束阶段的自动步骤，当前引擎不会再用该返回值否决上传。
     * 保留接口以免控制台/Swing/Web 代理因 API 变更而无法编译。
     */
    default boolean askPush(Game g, int playerIdx, List<CardInstance> queued) {
        return true;
    }

    /** 下载（PULL）：是否对云端栈顶发起下载；返回 false 表示放弃 */
    default boolean askPull(Game g, int playerIdx, CardInstance cloudTop, CardInstance carrier, int downloadCost) {
        return false;
    }

    /** 回滚（ROLLBACK）：从提交队列中选择一张回到手牌；多张候选返回 null 表示拒绝本次回滚 */
    default CardInstance chooseRollbackTarget(Game g, int playerIdx, List<CardInstance> queued) {
        return null;
    }
}
