using System;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

internal static class PunishConditionEvaluator
{
    public static bool IsSatisfied(GameState state, CardInstance card)
    {
        var condition = card.Definition.PunishCondition;
        if (string.IsNullOrWhiteSpace(condition) || condition == "ALWAYS")
        {
            return true;
        }

        var self = state.GetPlayer(card.OwnerPlayerIndex);
        var enemy = state.GetOpponent(card.OwnerPlayerIndex);
        if (condition == "SELF_LEADER_ON_FIELD")
        {
            return self.Leader is not null;
        }

        if (condition == "OPP_LEADER_ON_FIELD")
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

        if (TrySuffix(condition, "SELF_LIFE_LE_", out var life))
        {
            return self.Life.HasValue && self.Life.Value <= life;
        }

        return false;
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
