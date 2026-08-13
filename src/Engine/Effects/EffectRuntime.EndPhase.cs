using System;
using System.Collections.Generic;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Effects
{

public sealed partial class EffectRuntime
{
    internal void ResolveEndPhase(int playerIndex, long rootEventId)
    {
        if (playerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(playerIndex));
        }

        if (!State.Events.IsRootEvent(rootEventId))
        {
            throw new InvalidOperationException("End phase needs an existing root event.");
        }

        var player = State.GetPlayer(playerIndex);
        var context = new EffectContext(playerIndex, rootEventId);
        ResolveChants(player, context);
        if (IsGameOver)
        {
            return;
        }

        ExpireInactivePunish(player, context);
        TrackNoDamageTurn(player, context);
        EvaluateLeaderWinConditions(context);
    }

    private void ResolveChants(PlayerState player, EffectContext rootContext)
    {
        var chanting = new List<CardInstance>();
        foreach (var card in player.Field)
        {
            if (card.ChantRemaining > 0)
            {
                chanting.Add(card);
            }
        }

        foreach (var card in chanting)
        {
            if (IsGameOver || !player.Field.Contains(card))
            {
                break;
            }

            Commit(_ => card.ChantRemaining--);
            Emit("VICTORY_PROGRESS", rootContext.ForSource(player.PlayerIndex, card), Data(
                "source", card.InstanceId,
                "condition", "CHANT",
                "remaining", card.ChantRemaining));
            if (card.ChantRemaining != 0)
            {
                continue;
            }

            var context = rootContext.ForSource(player.PlayerIndex, card);
            EffectDispatcher.CreateDefault(this).ApplyAll(card.Definition.ChantEffects, context);
            if (!card.Definition.IsLeader && player.Field.Contains(card))
            {
                Commit(_ =>
                {
                    player.Field.Remove(card);
                    player.Graveyard.Add(card);
                });
            }
        }
    }

    private void ExpireInactivePunish(PlayerState player, EffectContext context)
    {
        var expired = new List<CardInstance>();
        foreach (var card in player.Hand)
        {
            if (string.Equals(card.Definition.Type, "PUNISH", StringComparison.Ordinal)
                && !card.PunishActivated)
            {
                expired.Add(card);
            }
        }

        Commit(_ =>
        {
            foreach (var card in expired)
            {
                player.Hand.Remove(card);
                player.Graveyard.Add(card);
            }
        });
        if (expired.Count > 0)
        {
            Emit("CARDS_DISCARDED", context, Data(
                "player", player.PlayerIndex,
                "count", expired.Count,
                "reasonKey", "rule.inactive_punish_expired"));
        }
    }

    private void TrackNoDamageTurn(PlayerState player, EffectContext context)
    {
        if (player.Leader is not null)
        {
            Commit(_ => player.NoDamageTurns = player.DamagedThisCycle
                ? 0
                : checked(player.NoDamageTurns + 1));
            Emit("VICTORY_PROGRESS", context, Data(
                "player", player.PlayerIndex,
                "condition", "NO_DAMAGE_TURNS_GE",
                "current", player.NoDamageTurns));
        }

        Commit(_ => player.DamagedThisCycle = false);
    }

    private void EvaluateLeaderWinConditions(EffectContext context)
    {
        foreach (var player in State.Players)
        {
            var leader = player.Leader;
            if (leader is null || leader.Definition.LeaderWinParam <= 0)
            {
                continue;
            }

            var opponent = State.GetOpponent(player.PlayerIndex);
            var current = 0;
            switch (leader.Definition.LeaderWinCondition)
            {
                case "OPP_DISCARD_TOTAL_GE":
                    current = opponent.TotalDiscarded;
                    break;
                case "NO_DAMAGE_TURNS_GE":
                    current = player.NoDamageTurns;
                    break;
                case "OPP_PUNISH_DRAW_TURN_GE":
                    current = opponent.PunishDrawnThisTurn;
                    break;
                default:
                    continue;
            }

            Emit("VICTORY_PROGRESS", context, Data(
                "player", player.PlayerIndex,
                "condition", leader.Definition.LeaderWinCondition,
                "current", current,
                "required", leader.Definition.LeaderWinParam));
            if (current >= leader.Definition.LeaderWinParam
                && TryDeclareWinner(player.PlayerIndex, "win." + leader.Definition.LeaderWinCondition!.ToLowerInvariant(), context))
            {
                return;
            }
        }
    }
}
}
