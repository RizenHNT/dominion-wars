using System;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Effects
{

public sealed partial class EffectRuntime
{
    public void EndTurn(EffectSpec spec, EffectContext context)
    {
        Commit(state => state.EndTurnRequested = true);
        Emit("TURN_FORCE_ENDED", context);
    }

    public void AddOpponentPunishTurn(EffectSpec spec, EffectContext context)
    {
        ApplyPunishDelta(State.GetOpponent(context.SourcePlayerIndex), spec.Amount, context);
    }

    public void AddSelfPunishTurn(EffectSpec spec, EffectContext context)
    {
        ApplyPunishDelta(State.GetPlayer(context.SourcePlayerIndex), spec.Amount, context);
    }

    public void ConvertPunishToDiscard(EffectSpec spec, EffectContext context)
    {
        var enemy = State.GetOpponent(context.SourcePlayerIndex);
        Commit(_ => enemy.PunishToSelfDiscardThisTurn = true);
        Emit("PUNISH_FLIP_APPLIED", context, Data("player", enemy.PlayerIndex));
    }

    public void ProtectTurn(EffectSpec spec, EffectContext context)
    {
        var self = State.GetPlayer(context.SourcePlayerIndex);
        Commit(_ => self.ProtectedThisTurn = true);
        Emit("PROTECTION_APPLIED", context, Data("player", self.PlayerIndex));
    }

    public void Negate(EffectSpec spec, EffectContext context)
    {
        var protectedPlayerIndex = (context.PlayedCard ?? context.Attacker ?? context.SourceCard)?.OwnerPlayerIndex
            ?? context.SourcePlayerIndex;
        if (State.GetPlayer(protectedPlayerIndex).ProtectedThisTurn)
        {
            EmitSkipped(context, spec.Action, "target.protected");
            return;
        }

        context.Negated = true;
        context.NegationObserved = true;
        EmitNegated(context);
    }

    public void NegateEnemyEffectsTurn(EffectSpec spec, EffectContext context)
    {
        var enemy = State.GetOpponent(context.SourcePlayerIndex);
        Commit(_ => enemy.EffectsNegatedThisTurn = true);
        Emit("EFFECTS_NEGATED_TURN", context, Data("player", enemy.PlayerIndex));
    }

    public void SkipReshuffle(EffectSpec spec, EffectContext context)
    {
        var self = State.GetPlayer(context.SourcePlayerIndex);
        var amount = Math.Max(1, spec.Amount);
        Commit(_ => self.SkipReshuffleCredits += amount);
        Emit("RESHUFFLE_CREDIT_GRANTED", context, Data(
            "player", self.PlayerIndex,
            "amount", amount));
    }

    public void GainLife(EffectSpec spec, EffectContext context)
    {
        ChangeLife(State.GetPlayer(context.SourcePlayerIndex), spec, context, true);
    }

    public void LoseLife(EffectSpec spec, EffectContext context)
    {
        ChangeLife(State.GetOpponent(context.SourcePlayerIndex), spec, context, false);
    }

    public void DamageCastle(EffectSpec spec, EffectContext context)
    {
        if (!TryPositiveAmount(spec, context, out var amount))
        {
            return;
        }

        if (!State.CastleEnabled || State.CastleHealth <= 0)
        {
            EmitSkipped(context, spec.Action, "castle.disabled");
            return;
        }

        ApplyCastleDamage(amount, context);
    }

    public void WinGame(EffectSpec spec, EffectContext context)
    {
        var reasonKey = string.IsNullOrWhiteSpace(spec.Param) ? "win.special" : spec.Param!;
        TryDeclareWinner(context.SourcePlayerIndex, reasonKey, context);
    }

    private void ApplyPunishDelta(PlayerState player, int amount, EffectContext context)
    {
        Commit(_ => player.PunishDeltaThisTurn += amount);
        Emit("PUNISH_DELTA_APPLIED", context, Data(
            "player", player.PlayerIndex,
            "amount", amount));
    }

    private void ChangeLife(
        PlayerState player,
        EffectSpec spec,
        EffectContext context,
        bool gain)
    {
        if (!TryPositiveAmount(spec, context, out var amount))
        {
            return;
        }

        if (!player.Life.HasValue)
        {
            EmitSkipped(context, spec.Action, "target.no_life_pool");
            return;
        }

        Commit(_ => player.Life = gain
            ? player.Life.Value + amount
            : Math.Max(0, player.Life.Value - amount));
        Emit(gain ? "LIFE_GAINED" : "LIFE_LOST", context, Data(
            "player", player.PlayerIndex,
            "amount", amount));
    }

    private void ApplyCastleDamage(int amount, EffectContext context)
    {
        var before = State.CastleHealth;
        Commit(state =>
        {
            state.CastleHealth = Math.Max(0, state.CastleHealth - amount);
            state.GetOpponent(context.SourcePlayerIndex).DamagedThisCycle = true;
        });
        Emit("CASTLE_DAMAGED", context, Data("amount", amount, "health", State.CastleHealth));
        if (before > 0 && State.CastleHealth == 0)
        {
            Emit("CASTLE_BROKEN", context, Data("breaker", context.SourcePlayerIndex));
            var breaker = State.GetPlayer(context.SourcePlayerIndex);
            Commit(_ => breaker.CycleWinCount = Math.Max(
                breaker.CycleWinCount,
                State.CastleBreakVictoryCount));
            ForceLeaderOut(State.GetOpponent(context.SourcePlayerIndex), context);
            var breakerLeader = FindLeaderAnywhere(breaker);
            if (breakerLeader is not null
                && string.Equals(
                    breakerLeader.Definition.LeaderWinCondition,
                    "ROYAL_CASTLE_BREAK",
                    StringComparison.Ordinal))
            {
                DeclareWinner(context.SourcePlayerIndex, "win.royal_castle_break", context);
            }
        }
    }

    private void ForceLeaderOut(PlayerState player, EffectContext context)
    {
        if (player.Leader is not null)
        {
            return;
        }

        CardInstance? leader = FindLeader(player.Hand)
            ?? FindLeader(player.Deck)
            ?? FindLeader(player.Graveyard);
        if (leader is null)
        {
            EmitSkipped(context, EffectNames.DamageCastle, "leader.not_found");
            return;
        }

        Commit(_ =>
        {
            player.Hand.Remove(leader);
            player.Deck.Remove(leader);
            player.Graveyard.Remove(leader);
            leader.ResetRuntimeState();
            leader.IsLeaderEntity = true;
            leader.SummonedThisTurn = leader.Definition.IsMinion;
            leader.ChantRemaining = leader.Definition.Chant;
            player.Field.Add(leader);
            if (leader.Definition.GrantLife > 0)
            {
                player.Life = leader.Definition.GrantLife;
            }
        });
        Emit("LEADER_MANIFESTED", context, Data(
            "player", player.PlayerIndex,
            "target", leader.InstanceId));
        if (leader.Definition.LeaderEnterEffects.Count > 0)
        {
            EffectDispatcher.CreateDefault(this).ApplyAll(
                leader.Definition.LeaderEnterEffects,
                new EffectContext(
                    player.PlayerIndex,
                    context.RootEventId,
                    sourceCard: leader));
        }
    }

    private static CardInstance? FindLeader(System.Collections.Generic.IEnumerable<CardInstance> cards)
    {
        foreach (var card in cards)
        {
            if (card.Definition.IsLeader)
            {
                return card;
            }
        }

        return null;
    }

    private static CardInstance? FindLeaderAnywhere(PlayerState player)
    {
        return player.Leader
            ?? FindLeader(player.Hand)
            ?? FindLeader(player.Deck)
            ?? FindLeader(player.Graveyard);
    }
}
}
