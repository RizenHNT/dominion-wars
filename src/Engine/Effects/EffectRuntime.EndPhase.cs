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
        if (IsGameOver)
        {
            return;
        }

        // All other END effects settle first; only then does the queue push
        // FIFO into the public cloud stack.
        PushQueue(player, context);
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
        foreach (var card in player.LeaderZone)
        {
            if (card.ChantRemaining > 0)
            {
                chanting.Add(card);
            }
        }

        foreach (var card in chanting)
        {
            if (IsGameOver
                || (!player.Field.Contains(card) && !player.LeaderZone.Contains(card)))
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
            if (!string.IsNullOrWhiteSpace(card.PendingLandmarkSummonCardId)
                && player.LeaderZone.Contains(card)
                && !IsGameOver)
            {
                PromoteLandmark(context, card);
            }

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

    /// <summary>
    /// Evaluates every seated leader's victory condition and declares a winner.
    ///
    /// ONE code path for every condition. The engine asks the condition how far along it
    /// is and compares nothing itself, so a new way to win — including a compound one —
    /// needs no change here. The old switch over condition names is gone: it was the
    /// place where the knowledge of "which number decides this" lived, and it is now in
    /// the condition classes where a designer writing a new win condition will find it.
    ///
    /// A leader whose card declares no objective takes the legacy path below, so a card
    /// can migrate individually. That is the only remaining name-based branch, and it is
    /// reachable only for a card that has not migrated.
    /// </summary>
    private void EvaluateLeaderWinConditions(EffectContext context)
    {
        foreach (var player in State.Players)
        {
            var leader = player.Leader;

            // The legacy path needs a positive threshold to mean anything. A leader that
            // DESCRIBES its objective does not: the castle break carries no winParam at all
            // (flame_leader declares none), so requiring one here would skip the migrated
            // leader completely and it would never report progress.
            var hasObjective = leader?.Definition.Victory is not null;
            if (leader is null || (!hasObjective && leader.Definition.LeaderWinParam <= 0))
            {
                continue;
            }

            // The card data DESCRIBES its own objective: the condition is resolved from
            // it and asked for a reading. Nothing here knows what the id measures.
            if (hasObjective)
            {
                var objective = leader.Definition.Victory!;
                var condition = objective.CreateCondition();
                var reading = condition.Read(State, player.PlayerIndex);

                Emit("VICTORY_PROGRESS", context, Data(
                    "player", player.PlayerIndex,
                    "condition", objective.Metric,
                    "current", reading.Current,
                    "required", reading.Target,
                    "direction", objective.Direction,
                    "measurable", reading.IsMeasurable,
                    "met", reading.IsMet));

                // An unmeasurable reading never wins. Treating it as progress would let a
                // condition the engine cannot read decide the game.
                if (!reading.IsMeasurable)
                {
                    continue;
                }

                // A condition whose victory is decided ELSEWHERE reports progress but does
                // not declare here. The castle break is the case: its own path compares both
                // leaders at once, and declaring from this loop as well would hand the win to
                // whichever seat is evaluated first whenever that path declines to decide.
                if (!condition.DeclaresWinWhenMet)
                {
                    continue;
                }

                // The WIN REASON KEY still comes from the legacy condition name. That
                // string is a consumer contract, not an internal label: the Unity
                // presentation model switches on "win.pull_total_ge" to choose display
                // copy, and engine and PlayMode tests compare it exactly. Renaming it is a
                // separate, consumer-visible decision left to the owner.
                if (reading.IsMet
                    && TryDeclareWinner(
                        player.PlayerIndex,
                        "win." + (leader.Definition.LeaderWinCondition ?? objective.Metric).ToLowerInvariant(),
                        context))
                {
                    return;
                }

                continue;
            }

            // Legacy path: a card that has not migrated. Kept so migration can be
            // per-card and an unmigrated card cannot change behaviour.
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
                case "PULL_TOTAL_GE":
                    current = player.PullCount;
                    break;
                case "GIANT_HEALTH_GE":
                    foreach (var card in player.Field)
                    {
                        if (card.IsMinion && card.Sealed && card.Health >= leader.Definition.LeaderWinParam)
                        {
                            current = leader.Definition.LeaderWinParam;
                            break;
                        }
                    }

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
