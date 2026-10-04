using System;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

/// <summary>
/// The single condition grammar shared by player-facing rules (punish
/// activation) and card-authored effect conditions
/// (<see cref="DominionWars.Engine.Effects.EffectSpec.Condition"/>). Unknown
/// tokens fail closed, so an unparseable condition never resolves an effect.
///
/// WHY THIS GRAMMAR IS *THE* CARD-AUTHORING VOCABULARY (2026-09-13). Card text is
/// printed from `definition.Text`, which is authored by hand and never derived
/// from the effect list — so a card whose text promises "若……则……" is only honest
/// if the engine can actually evaluate that promise. Every token here therefore
/// exists to be printed on a card face, and the set is deliberately sized so that
/// each faction gates its payoff cards on its OWN mechanic rather than sharing one
/// generic condition:
///
///   * 古木 seals its own minions, so it gates on `SELF_SEALED_*` / `SELF_ROOT_GE_*`
///   * 机械 moves cards between queue and cloud, so it gates on `SELF_COMMIT_GE_*` /
///     `SELF_CLOUD_GE_*`
///   * 深海 attacks the opponent's hand, so it gates on `OPP_DISCARD_GE_*` /
///     `OPP_HAND_LE_*` / `SELF_SEALED_GE_0` (its own board being empty)
///   * 烈焰 builds a wide board, so it gates on `SELF_MINIONS_GE_*` / `SELF_ATTACKED_GE_*`
///
/// A card may NOT gate on a mechanic its faction cannot manipulate, and identical
/// conditions are not reused across a faction's payoff cards — that is a design
/// rule recorded in docs/PL_DECK_REDESIGN_V2_2026-09-13.md, not an engine rule.
/// </summary>
public static class PunishConditionEvaluator
{
    public static bool IsSatisfied(GameState state, int playerIndex, string? condition)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (playerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(playerIndex));
        }

        if (string.IsNullOrWhiteSpace(condition) || string.Equals(condition, "ALWAYS", StringComparison.Ordinal))
        {
            return true;
        }

        var self = state.GetPlayer(playerIndex);
        var enemy = state.GetOpponent(playerIndex);
        if (string.Equals(condition, "SELF_LEADER_ON_FIELD", StringComparison.Ordinal))
        {
            return self.Leader is not null;
        }

        if (string.Equals(condition, "OPP_LEADER_ON_FIELD", StringComparison.Ordinal))
        {
            return enemy.Leader is not null;
        }

        if (TrySuffix(condition, "ENEMY_MINIONS_GE_", out var enemyMinions))
        {
            return CountLivingMinions(enemy) >= enemyMinions;
        }

        if (TrySuffix(condition, "SELF_MINIONS_GE_", out var selfMinions))
        {
            return CountLivingMinions(self) >= selfMinions;
        }

        if (TrySuffix(condition, "HAND_GE_", out var handCount))
        {
            return self.Hand.Count >= handCount;
        }

        if (TrySuffix(condition, "OPP_HAND_GE_", out var oppHandCount))
        {
            return enemy.Hand.Count >= oppHandCount;
        }

        // "at most n cards in hand" — the sea axis starves the opponent's hand, so it
        // needs the inverse of HAND_GE_ as a payoff gate.
        if (TrySuffix(condition, "OPP_HAND_LE_", out var oppHandCap))
        {
            return enemy.Hand.Count <= oppHandCap;
        }

        if (TrySuffix(condition, "SELF_LIFE_LE_", out var life))
        {
            return self.Life.HasValue && self.Life.Value <= life;
        }

        // Counts every sealed minion regardless of health: the seal identity is what
        // the 古木 mechanic is about, and a sealed unit at 1 health is still sealed.
        if (TrySuffix(condition, "SELF_SEALED_GE_", out var sealedCount))
        {
            return CountSealedMinions(self) >= sealedCount;
        }

        // The 古木 victory axis is "the largest sealed minion's health", so the payoff
        // cards gate on how far that axis has already been pushed.
        if (TrySuffix(condition, "SELF_SEALED_HEALTH_GE_", out var sealedHealth))
        {
            return BestSealedMinionHealth(self) >= sealedHealth;
        }

        if (TrySuffix(condition, "SELF_ROOT_GE_", out var rootStacks))
        {
            return self.RootStacks >= rootStacks;
        }

        if (TrySuffix(condition, "SELF_RAMPANT_GE_", out var rampantStacks))
        {
            return self.RampantStacks >= rampantStacks;
        }

        if (TrySuffix(condition, "SELF_COMMIT_GE_", out var commitQueue))
        {
            return self.CommitQueue.Count >= commitQueue;
        }

        if (TrySuffix(condition, "SELF_CLOUD_GE_", out var cloudStack))
        {
            return self.CloudStack.Count >= cloudStack;
        }

        if (TrySuffix(condition, "SELF_PULL_GE_", out var pulls))
        {
            return self.PullCount >= pulls;
        }

        // The discard victory counter reads the OPPONENT's TotalDiscarded
        // (OPP_DISCARD_COUNT), so the sea payoff cards must read the same number.
        if (TrySuffix(condition, "OPP_DISCARD_GE_", out var oppDiscarded))
        {
            return enemy.TotalDiscarded >= oppDiscarded;
        }

        if (TrySuffix(condition, "SELF_AMBUSH_GE_", out var ambushCount))
        {
            return self.AmbushZone.Count >= ambushCount;
        }

        if (TryCastleCondition(state, condition, out var castleResult))
        {
            return castleResult;
        }

        return false;
    }

    /// <summary>
    /// 共享王城的条件读数（2026-09-24 新增）。
    ///
    /// 为什么这一族不带 SELF_/OPP_ 前缀：王城是**双方共享**的一条独立败北轨
    /// （RULES §9.1），不是某一方的资源 —— 读它只有"当前血量"一个数，
    /// 加前缀反而会让人以为存在"对方的王城"。
    ///
    /// 为什么值得加：王城血量（<c>GameState.CastleHealth</c>）早就是权威状态，
    /// 烈焰阵营围绕它取胜，但**卡面从来无法表达"若王城如何"** —— 属于"能力存在、
    /// 卡牌却写不出来"。加这一族之后，其他阵营才能设计"针对破城轴"的卡，
    /// 而不是只能眼看对方砸城。
    ///
    /// 只提供 `CASTLE_HP_LE_n`：C# 的 GameState **只存当前血量、不存上限**
    /// （没有 CastleMaxHealth 属性），所以"已损失量"那族在 C# 无法实现 ——
    /// 与其两边语义不一致，不如只做双方都能精确表达的那一族。
    /// </summary>
    private static bool TryCastleCondition(GameState state, string condition, out bool result)
    {
        result = false;
        if (TrySuffix(condition, "CASTLE_HP_LE_", out var castleHp))
        {
            result = state.CastleHealth <= castleHp;
            return true;
        }

        return false;
    }

    private static int CountSealedMinions(PlayerState player)
    {
        var count = 0;
        foreach (var card in player.Field)
        {
            if (card.IsMinion && card.IsAlive && card.Sealed)
            {
                count++;
            }
        }

        return count;
    }

    private static int BestSealedMinionHealth(PlayerState player)
    {
        var best = 0;
        foreach (var card in player.Field)
        {
            // Mirrors SealedMinionMaxHealthCondition exactly (VictoryCondition.cs):
            // the victory axis reads Health, not MaxHealth, and does not require IsAlive.
            if (card.IsMinion && card.Sealed && card.Health > best)
            {
                best = card.Health;
            }
        }

        return best;
    }

    internal static bool IsSatisfied(GameState state, CardInstance card)
    {
        if (card is null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        return IsSatisfied(state, card.OwnerPlayerIndex, card.Definition.PunishCondition);
    }

    private static int CountLivingMinions(PlayerState player)
    {
        var count = 0;
        foreach (var card in player.Field)
        {
            if (card.IsMinion && card.IsAlive && !card.IsLeaderEntity)
            {
                count++;
            }
        }

        return count;
    }

    private static bool TrySuffix(string value, string prefix, out int result)
    {
        result = 0;
        return value.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(value.Substring(prefix.Length), out result);
    }
}
}
