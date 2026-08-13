using System;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

internal static class CardPlayRules
{
    public static bool CanPlay(PlayerState player, CardInstance card, out string reasonKey)
    {
        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (card is null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        if (!player.Hand.Contains(card))
        {
            reasonKey = "action.card_not_in_hand";
            return false;
        }

        if (string.Equals(card.Definition.Type, "AMBUSH", StringComparison.Ordinal))
        {
            reasonKey = "action.ambush_phase_only";
            return false;
        }

        if (card.Definition.IsLeader)
        {
            reasonKey = "action.leader_auto_manifest_only";
            return false;
        }

        if (string.Equals(card.Definition.Type, "PUNISH", StringComparison.Ordinal)
            && !card.PunishActivated)
        {
            reasonKey = "action.punish_not_activated";
            return false;
        }

        if (!TagsFree(player, card))
        {
            reasonKey = "action.tag_already_used";
            return false;
        }

        reasonKey = string.Empty;
        return true;
    }

    public static bool TagsFree(PlayerState player, CardInstance card)
    {
        foreach (var tag in card.Definition.Tags)
        {
            if (player.UsedTags.Contains(tag))
            {
                return false;
            }
        }

        return true;
    }

    public static int EffectivePunish(PlayerState player, CardInstance card)
    {
        var baseValue = card.PunishActivated
            ? card.Definition.PunishCost
            : card.Definition.Punish;
        return Math.Max(0, baseValue + player.PunishDeltaThisTurn);
    }
}
}
