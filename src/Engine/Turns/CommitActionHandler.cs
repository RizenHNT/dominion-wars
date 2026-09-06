using System;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

/// <summary>
/// Resolves the player-originated manual commit of one mechanical field card.
/// Positive fees remain fail-closed until the approved cost model defines a
/// payment source; zero-fee cards can exercise the frozen lifecycle today.
/// </summary>
public sealed class CommitActionHandler : ITurnActionHandler
{
    internal static bool HasSupportedCommitEffects(GameState state, CardInstance card)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        if (card is null) throw new ArgumentNullException(nameof(card));

        var dispatcher = EffectDispatcher.CreateDefault(new EffectRuntime(state));
        foreach (var effect in card.Definition.CommitEffects)
        {
            if (effect is null || !dispatcher.RegisteredActions.Contains(effect.Action))
            {
                return false;
            }
        }

        return true;
    }

    public bool CanHandle(string phaseId, string actionType)
    {
        return string.Equals(phaseId, TurnPhase.Action, StringComparison.Ordinal)
            && string.Equals(actionType, LegalActionGenerator.Commit, StringComparison.Ordinal);
    }

    public GameActionResult Execute(GameState state, GameActionRequest request, TurnFlow flow)
    {
        if (!request.SourceEntityId.HasValue)
        {
            return GameActionResult.Reject("action.source_required");
        }

        var owner = state.GetPlayer(request.ActorPlayerIndex);
        CardInstance? card = null;
        foreach (var candidate in owner.Field)
        {
            if (candidate.InstanceId == request.SourceEntityId.Value)
            {
                card = candidate;
                break;
            }
        }

        if (card is null
            || card.ControllerPlayerIndex != owner.PlayerIndex
            || !EffectRuntime.IsMechanicalCard(card)
            || card.IsLeaderEntity
            || card.Definition.IsLeader)
        {
            return GameActionResult.Reject("action.invalid_commit_source");
        }

        var expectedActionId = $"commit_{card.InstanceId}";
        if (request.ActionId is not null
            && !string.Equals(request.ActionId, expectedActionId, StringComparison.Ordinal))
        {
            return GameActionResult.Reject("action.id_mismatch");
        }

        if (card.Definition.CommitCost > 0)
        {
            return GameActionResult.Reject("action.cost_system_unavailable");
        }

        if (!HasSupportedCommitEffects(state, card))
        {
            return GameActionResult.Reject("action.unknown_effect");
        }

        var root = state.Events.Append("COMMIT_DECLARED", null, Data(
            "player", owner.PlayerIndex,
            "source", card.InstanceId));
        var runtime = new EffectRuntime(state);
        runtime.CommitCard(
            new EffectSpec(EffectNames.Commit, "SELF"),
            new EffectContext(
                owner.PlayerIndex,
                root.EventId,
                sourceCard: card,
                playedCard: card,
                selectedTargetId: card.InstanceId));
        return GameActionResult.Accept();
    }

    private static IReadOnlyDictionary<string, object?> Data(params object?[] values)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < values.Length; index += 2)
        {
            result[(string)values[index]!] = values[index + 1];
        }
        return result;
    }
}
}
