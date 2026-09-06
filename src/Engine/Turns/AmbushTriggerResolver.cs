using System;
using System.Collections.Generic;
using DominionWars.Engine.Command;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

/// <summary>
/// Resolves at most one opposing ambush for a single public action window.
/// The oldest eligible card wins, keeping resolution deterministic.
/// </summary>
public sealed class AmbushTriggerResolver
{
    public AmbushTriggerResult Resolve(
        GameState state,
        int actingPlayerIndex,
        IEnumerable<string> triggerKinds,
        long rootEventId,
        CardInstance? playedCard = null,
        CardInstance? attacker = null,
        IReadOnlyList<CardInstance>? drawnCards = null,
        EffectContext? actionContext = null)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        if (triggerKinds is null) throw new ArgumentNullException(nameof(triggerKinds));
        if (!state.Events.IsRootEvent(rootEventId))
            throw new InvalidOperationException("Ambush resolution needs an existing root event.");

        var acceptedKinds = new HashSet<string>(triggerKinds, StringComparer.Ordinal);
        var owner = state.GetOpponent(actingPlayerIndex);
        if (owner.AmbushFocusTriggeredThisTurn
            || acceptedKinds.Count == 0
            || HasTriggeredInRoot(state, rootEventId))
            return AmbushTriggerResult.None;

        CardInstance? ambush = null;
        foreach (var candidate in owner.AmbushZone)
        {
            if (acceptedKinds.Contains(candidate.Definition.AmbushTrigger ?? string.Empty)
                && CardPlayRules.TagsFree(owner, candidate))
            {
                ambush = candidate;
                break;
            }
        }

        if (ambush is null) return AmbushTriggerResult.None;

        var isFocus = string.Equals(ambush.Definition.AmbushKind, "FOCUS", StringComparison.Ordinal);
        Mutate(state, _ =>
        {
            foreach (var tag in ambush.Definition.Tags) owner.UsedTags.Add(tag);
            if (isFocus) owner.AmbushFocusTriggeredThisTurn = true;
        });

        actionContext ??= new EffectContext(
            actingPlayerIndex,
            rootEventId,
            sourceCard: playedCard ?? attacker,
            playedCard: playedCard,
            attacker: attacker,
            drawnCards: drawnCards,
            selectedTargetId: (playedCard ?? attacker)?.InstanceId);
        var ambushContext = actionContext.ForSource(owner.PlayerIndex, ambush);
        state.Events.Append("AMBUSH_TRIGGERED", rootEventId, Data(
            "player", owner.PlayerIndex,
            "source", ambush.InstanceId,
            "cardId", ambush.Definition.Id,
            "kind", ambush.Definition.AmbushKind));
        EffectDispatcher.CreateDefault(new EffectRuntime(state))
            .ApplyAll(ambush.Definition.AmbushEffects, ambushContext);

        var won = state.WinnerPlayerIndex == owner.PlayerIndex;
        Mutate(state, _ =>
        {
            if (won && ambush.Definition.IsLeader) return;
            owner.AmbushZone.Remove(ambush);
            if (ambush.Definition.IsLeader)
            {
                ambush.ResetRuntimeState();
                owner.Deck.Insert(state.Random.NextInt(owner.Deck.Count + 1), ambush);
            }
            else
            {
                owner.Graveyard.Add(ambush);
            }
        });

        return new AmbushTriggerResult(true, actionContext.Negated, ambush.InstanceId);
    }

    private static bool HasTriggeredInRoot(GameState state, long rootEventId)
    {
        foreach (var item in state.Events.Items)
            if (item.ParentEventId == rootEventId
                && string.Equals(item.EventType, "AMBUSH_TRIGGERED", StringComparison.Ordinal))
                return true;
        return false;
    }

    private static void Mutate(GameState state, Action<GameState> mutation)
        => state.Commands.Execute(state, new AmbushTriggerCommand(mutation));

    private static IReadOnlyDictionary<string, object?> Data(params object?[] values)
    {
        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < values.Length; index += 2)
            data[(string)values[index]!] = values[index + 1];
        return data;
    }

    private sealed class AmbushTriggerCommand : IGameCommand
    {
        private readonly Action<GameState> _mutation;
        public AmbushTriggerCommand(Action<GameState> mutation) => _mutation = mutation;
        public void Apply(GameState state) => _mutation(state);
    }
}

public readonly struct AmbushTriggerResult
{
    public static AmbushTriggerResult None => default;
    public AmbushTriggerResult(bool triggered, bool negated, long sourceEntityId)
    {
        Triggered = triggered;
        Negated = negated;
        SourceEntityId = sourceEntityId;
    }

    public bool Triggered { get; }
    public bool Negated { get; }
    public long SourceEntityId { get; }
}
}
