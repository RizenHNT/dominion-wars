using System;
using System.Collections.Generic;
using DominionWars.Engine.Command;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Effects
{

public sealed partial class EffectRuntime
{
    private readonly EffectTargetResolver _targets;

    public EffectRuntime(GameState state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        _targets = new EffectTargetResolver(state);
    }

    public GameState State { get; }

    internal bool IsGameOver => State.WinnerPlayerIndex.HasValue;

    internal bool IsSourceEffectNegated(EffectContext context)
    {
        var source = context.SourceCard ?? context.Attacker;
        return source is not null
            && !source.Definition.IsLeader
            && State.GetPlayer(source.OwnerPlayerIndex).EffectsNegatedThisTurn;
    }

    internal void CheckAll(EffectContext context)
    {
        CleanupNonLeaderDeaths(context);
        if (IsGameOver)
        {
            return;
        }

        var bothLeadersFielded = State.Players[0].Leader is not null
            && State.Players[1].Leader is not null;
        foreach (var player in State.Players)
        {
            var leader = player.Leader;
            var leaderDefeated = leader is not null && leader.IsMinion && leader.Health <= 0;
            var durabilityDefeated = leader is not null
                && !leader.IsMinion
                && leader.Definition.LeaderDurability > 0
                && leader.Durability <= 0;
            var lifeDefeated = player.Life.HasValue && player.Life.Value <= 0;
            if (!leaderDefeated && !durabilityDefeated && !lifeDefeated)
            {
                continue;
            }

            if (!bothLeadersFielded)
            {
                Commit(_ =>
                {
                    if (leaderDefeated)
                    {
                        leader!.Health = 1;
                    }

                    if (lifeDefeated)
                    {
                        player.Life = 1;
                    }

                    if (durabilityDefeated)
                    {
                        leader!.Durability = 1;
                    }
                });
                Emit("DEFEAT_PREVENTED", context, Data(
                    "player", player.PlayerIndex,
                    "reasonKey", "rule.leader_gate"));
                continue;
            }

            DeclareWinner(
                1 - player.PlayerIndex,
                leaderDefeated || durabilityDefeated
                    ? "win.enemy_leader_defeated"
                    : "win.enemy_life_zero",
                context);
            break;
        }
    }

    internal void EmitNegated(EffectContext context)
    {
        Emit("EFFECT_NEGATED", context, Data("source", context.SourceCard?.InstanceId));
    }

    internal void EmitSkipped(
        EffectContext context,
        string action,
        string reasonKey,
        long? target = null)
    {
        Emit("EFFECT_SKIPPED", context, Data(
            "action", action,
            "reasonKey", reasonKey,
            "target", target));
    }

    private void DeclareWinner(int playerIndex, string reasonKey, EffectContext context)
    {
        if (IsGameOver)
        {
            return;
        }

        Commit(state =>
        {
            state.WinnerPlayerIndex = playerIndex;
            state.WinReason = reasonKey;
            state.Turn.SetPhase("OVER");
        });
        Emit("GAME_WON", context, Data("player", playerIndex, "reasonKey", reasonKey));
    }

    private bool TryDeclareWinner(int playerIndex, string reasonKey, EffectContext context)
    {
        if (State.Players[0].Leader is null || State.Players[1].Leader is null)
        {
            EmitSkipped(context, EffectNames.WinGame, "rule.leader_gate");
            return false;
        }

        DeclareWinner(playerIndex, reasonKey, context);
        return true;
    }

    private void CleanupNonLeaderDeaths(EffectContext context)
    {
        foreach (var player in State.Players)
        {
            var dead = new List<CardInstance>();
            foreach (var card in player.Field)
            {
                if (card.IsMinion && !card.IsLeaderEntity && !card.IsAlive)
                {
                    dead.Add(card);
                }
            }

            foreach (var card in dead)
            {
                Commit(_ =>
                {
                    player.Field.Remove(card);
                    player.Graveyard.Add(card);
                });
                Emit("MINION_DESTROYED", context, Data(
                    "target", card.InstanceId,
                    "reasonKey", "effect.lethal_damage"));
            }
        }
    }

    private bool TryPositiveAmount(EffectSpec spec, EffectContext context, out int amount)
    {
        amount = spec.Amount;
        if (amount > 0)
        {
            return true;
        }

        EmitSkipped(context, spec.Action, "effect.invalid_amount");
        return false;
    }

    private CardDefinition? FindDefinition(string? id)
    {
        return string.IsNullOrWhiteSpace(id) ? null : State.FindCardDefinition(id);
    }

    private void Commit(Action<GameState> mutation)
    {
        State.Commands.Execute(State, new DelegateGameCommand(mutation));
    }

    private void Emit(
        string eventType,
        EffectContext context,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        State.Events.Append(eventType, context.RootEventId, data);
    }

    private static IReadOnlyDictionary<string, object?> Data(params object?[] pairs)
    {
        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < pairs.Length; index += 2)
        {
            data[(string)pairs[index]!] = pairs[index + 1];
        }

        return data;
    }
}
}
