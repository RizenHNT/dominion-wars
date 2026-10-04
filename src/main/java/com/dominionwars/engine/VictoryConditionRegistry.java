package com.dominionwars.engine;

import com.dominionwars.model.VictoryObjective;

/**
 * 把本引擎的读数登记进 {@link VictoryObjective} —— 与 C# 的 {@code VictoryConditions} 对齐。
 *
 * <p>登记与声明分在两处是为了避免包循环：{@code model} 只描述"要量什么"，
 * {@code engine} 提供"怎么量"。这样新增一个胜利条件只需要在这里加一条读数，
 * **AI、合法动作生成、快照层都不必改动** —— 这正是 owner 要的统一接口。
 *
 * <p>登记是幂等的：重复调用只会覆盖相同 id，不会重复累积。
 */
public final class VictoryConditionRegistry {

    private static boolean installed = false;

    private VictoryConditionRegistry() {
    }

    /** 登记全部读数。引擎构造时调用一次即可，重复调用安全。 */
    public static synchronized void install() {
        if (installed) return;
        installed = true;

        // 对方累计受效果弃牌（强制弃牌阶段不计入，见 RULES §5）。
        // 刻意读 1-seat：这是「对方」的计数，不是自己的。
        // 第一版这里错读成 players[seat]，导致海统领永远赢不了 —— 而症状只是"胜利条件不达成"，
        // 根因完全不可见。故每条读数都对着 C# VictoryCondition.cs 的 CounterOf 抄一遍。
        VictoryObjective.register("OPPONENT_DISCARD_COUNT",
                (g, seat) -> ((Game) g).players[1 - seat].totalDiscarded);

        // 己方连续未受伤回合数（C#: owner.NoDamageTurns）
        VictoryObjective.register("NO_DAMAGE_TURN_STREAK",
                (g, seat) -> ((Game) g).players[seat].noDamageTurns);

        // 对方单回合因惩罚抽牌张数（C#: opponent.PunishDrawnThisTurn）
        VictoryObjective.register("OPPONENT_PUNISH_DRAW_THIS_TURN",
                (g, seat) -> ((Game) g).players[1 - seat].punishDrawnThisTurn);

        // 己方累计下载次数（C#: owner.PullCount）
        VictoryObjective.register("PULL_COUNT",
                (g, seat) -> ((Game) g).players[seat].pullCount);

        // 己方封印随从的最大生命值（C#: owner 场上 sealed 随从的最大 Health）。
        // 没有任何封印随从时返回 null：那是"无法测量"，不是 0 —— 目标 512 时两者结论相同，
        // 但一个 DECREASE 目标下把 null 当 0 会直接误判达成，所以必须区分。
        VictoryObjective.register("SEALED_MINION_MAX_HEALTH",
                (g, seat) -> {
                    Integer best = null;
                    for (CardInstance c : ((Game) g).players[seat].field) {
                        if (c.def.isMinion() && c.sealed) {
                            best = (best == null) ? c.health : Math.max(best, c.health);
                        }
                    }
                    return best;
                });

        // 王城是否已被击破：0/1 读数，目标固定 1。royalCastleBreaker 由 breakRoyalCastle 写入。
        VictoryObjective.register("CASTLE_BREAK",
                (g, seat) -> ((Game) g).royalCastleBreaker >= 0 ? 1 : 0);

        // ── 老名字的兼容映射（迁移完成后可删）──────────────────────────
        VictoryObjective.registerLegacy("OPP_DISCARD_TOTAL_GE", "OPPONENT_DISCARD_COUNT");
        VictoryObjective.registerLegacy("NO_DAMAGE_TURNS_GE", "NO_DAMAGE_TURN_STREAK");
        VictoryObjective.registerLegacy("OPP_PUNISH_DRAW_TURN_GE", "OPPONENT_PUNISH_DRAW_THIS_TURN");
        VictoryObjective.registerLegacy("PULL_TOTAL_GE", "PULL_COUNT");
        VictoryObjective.registerLegacy("GIANT_HEALTH_GE", "SEALED_MINION_MAX_HEALTH");
        VictoryObjective.registerLegacy("ROYAL_CASTLE_BREAK", "CASTLE_BREAK");
        // AMBUSH_TRIGGER_WIN 不登记：伏击统领尚未设计，保留为"无法测量"而不是硬塞一个读数。
    }
}
