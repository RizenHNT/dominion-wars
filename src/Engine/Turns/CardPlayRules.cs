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

    /// <summary>
    /// 先驱威压 (RULES §3.2) as a single shared punish modifier: +N while only
    /// the opponent holds a fielded leader, −N while only this player does.
    /// Both leaders or neither leader on the field is not a pioneer state.
    /// </summary>
    public static int PioneerPunishModifier(GameState state, PlayerState player)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        var modifier = 0;
        if (IsSoloLeader(state, 1 - player.PlayerIndex))
        {
            modifier += state.Rules.PioneerOpponentPunishBonus;
        }

        if (IsSoloLeader(state, player.PlayerIndex))
        {
            modifier -= state.Rules.PioneerSelfPunishDiscount;
        }

        return modifier;
    }

    /// <summary>
    /// Resolves the effective punish for a card play under the shared match
    /// rules, including 先驱威压 (pioneer pressure). Card definitions keep their
    /// printed punish; only this computed value changes.
    /// </summary>
    public static int EffectivePunish(GameState state, PlayerState player, CardInstance card)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (card is null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        // 先驱威压：仅对方统领在场 → 本方惩罚值 +N；仅本方统领在场 → 本方惩罚值 −N。
        return EffectivePunish(player, card, PioneerPunishModifier(state, player));
    }

    public static int EffectivePunish(PlayerState player, CardInstance card)
        => EffectivePunish(player, card, 0);

    public static int EffectivePunish(PlayerState player, CardInstance card, int modifier)
    {
        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (card is null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        var baseValue = card.PunishActivated
            ? card.Definition.PunishCost
            : card.Definition.Punish;
        return Math.Max(0, baseValue + modifier + player.PunishDeltaThisTurn);
    }

    /// <summary>
    /// 先驱威压：是否恰好只有 <paramref name="playerIndex"/> 一方的统领在场。
    /// Both leaders or neither leader on the field is not a pioneer state.
    /// </summary>
    public static bool IsSoloLeader(GameState state, int playerIndex)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (playerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(playerIndex));
        }

        return state.GetPlayer(playerIndex).Leader is not null
            && state.GetOpponent(playerIndex).Leader is null;
    }
}
}
