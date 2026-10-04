using System;
using System.Collections.Generic;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

/// <summary>
/// Resolves the player-originated manual rollback of one card the acting
/// player has already committed. It reuses the existing
/// <see cref="EffectRuntime.Rollback"/> movement (commit queue -> hand) that
/// the <c>machine_recycler</c> commit effect already uses; it does not
/// re-implement the zone change and it does not trigger upload or download
/// effects.
///
/// Punish value is 0: per RULES.md §12.4 「不能回溯已经发生的惩罚抽牌」and the
/// glossary's 「费用不返还」, the earlier COMMIT's commitCost is neither
/// re-charged nor refunded here, so this action produces no punish draw and
/// opens no punish response window.
/// </summary>
public sealed class RollbackActionHandler : ITurnActionHandler
{
    internal static string CreateActionId(long queueCardId)
    {
        return $"rollback_{queueCardId}";
    }

    public bool CanHandle(string phaseId, string actionType)
    {
        return string.Equals(phaseId, TurnPhase.Action, StringComparison.Ordinal)
            && string.Equals(actionType, LegalActionGenerator.Rollback, StringComparison.Ordinal);
    }

    public GameActionResult Execute(GameState state, GameActionRequest request, TurnFlow flow)
    {
        if (!request.SourceEntityId.HasValue)
        {
            return GameActionResult.Reject("action.source_required");
        }

        var owner = state.GetPlayer(request.ActorPlayerIndex);
        CardInstance? card = null;
        foreach (var candidate in owner.CommitQueue)
        {
            if (candidate.InstanceId == request.SourceEntityId.Value)
            {
                card = candidate;
                break;
            }
        }

        if (card is null)
        {
            // A card that is not in the acting player's commit queue is
            // rejected instead of falling back to another queue entry.
            return GameActionResult.Reject("action.invalid_rollback_source");
        }

        var expectedActionId = CreateActionId(card.InstanceId);
        if (request.ActionId is not null
            && !string.Equals(request.ActionId, expectedActionId, StringComparison.Ordinal))
        {
            return GameActionResult.Reject("action.id_mismatch");
        }

        if (request.SelectedEntityIds.Count != 0)
        {
            return GameActionResult.Reject("action.invalid_rollback_payload");
        }

        var root = state.Events.Append("ROLLBACK_DECLARED", null, Data(
            "player", owner.PlayerIndex,
            "source", card.InstanceId,
            "punish", 0));

        // No punish resolution here on purpose: see the class comment.
        new EffectRuntime(state).Rollback(
            new EffectSpec(EffectNames.Rollback),
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
