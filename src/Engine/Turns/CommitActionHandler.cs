using System;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

/// <summary>
/// Resolves the player-originated manual commit of one mechanical field card.
/// The card's declared commit value is a punishment amount: it makes the
/// opponent draw through the existing punish/response chain before the card
/// enters the public commit queue. It is not a separate payment resource.
/// </summary>
public sealed class CommitActionHandler : ITurnActionHandler
{
    private readonly PlayCardActionHandler _punishResolver;

    public CommitActionHandler(IPunishResponsePolicy? punishResponses = null)
    {
        _punishResolver = new PlayCardActionHandler(punishResponses: punishResponses);
    }

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

        if (!HasSupportedCommitEffects(state, card))
        {
            return GameActionResult.Reject("action.unknown_effect");
        }

        var root = state.Events.Append("COMMIT_DECLARED", null, Data(
            "player", owner.PlayerIndex,
            "source", card.InstanceId,
            "punish", card.Definition.CommitCost));
        if (!_punishResolver.ResolveLifecyclePunish(
                state,
                owner.PlayerIndex,
                card.Definition.CommitCost,
                root.EventId))
        {
            return GameActionResult.Accept();
        }

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
