using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

/// <summary>Resolves the player-originated manual pull of the cloud-stack top.</summary>
public sealed class PullActionHandler : ITurnActionHandler
{
    private readonly PlayCardActionHandler _punishResolver;

    public PullActionHandler(IPunishResponsePolicy? punishResponses = null)
    {
        _punishResolver = new PlayCardActionHandler(punishResponses: punishResponses);
    }

    internal static string CreateActionId(
        long carrierId,
        long topCardId,
        long? selectedTargetId = null)
    {
        return selectedTargetId.HasValue
            ? $"pull_{carrierId}_{topCardId}_{selectedTargetId.Value}"
            : $"pull_{carrierId}_{topCardId}";
    }

    internal static bool RequiresTargetSelection(IReadOnlyList<EffectSpec> effects)
    {
        if (effects is null)
        {
            throw new ArgumentNullException(nameof(effects));
        }

        foreach (var effect in effects)
        {
            if (effect is not null && effect.Target == "FRIENDLY_MINION")
            {
                return true;
            }
        }

        return false;
    }

    internal static IReadOnlyList<CardInstance> GetLegalTargets(
        GameState state,
        int playerIndex,
        CardInstance card)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (playerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(playerIndex));
        }

        if (card is null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        if (!RequiresTargetSelection(card.Definition.PullEffects))
        {
            return Array.Empty<CardInstance>();
        }

        var result = new List<CardInstance>();
        foreach (var candidate in state.GetPlayer(playerIndex).Field)
        {
            if (CardTargetValidator.IsOrdinaryAliveMinion(candidate))
            {
                result.Add(candidate);
            }
        }

        return result.AsReadOnly();
    }

    internal static bool HasSupportedPullEffects(GameState state, CardInstance card)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (card is null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        var dispatcher = EffectDispatcher.CreateDefault(new EffectRuntime(state));
        return HasSupportedPullEffects(dispatcher, card.Definition.PullEffects);
    }

    private static bool HasSupportedPullEffects(
        EffectDispatcher dispatcher,
        IReadOnlyList<EffectSpec> effects)
    {
        foreach (var effect in effects)
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
            && string.Equals(actionType, LegalActionGenerator.Pull, StringComparison.Ordinal);
    }

    public GameActionResult Execute(GameState state, GameActionRequest request, TurnFlow flow)
    {
        if (!request.SourceEntityId.HasValue)
        {
            return GameActionResult.Reject("action.source_required");
        }

        if (string.IsNullOrWhiteSpace(request.TargetId))
        {
            return GameActionResult.Reject("action.target_required");
        }

        var owner = state.GetPlayer(request.ActorPlayerIndex);
        var carrier = state.FindEntity(request.SourceEntityId.Value);
        if (carrier is null
            || carrier.ControllerPlayerIndex != owner.PlayerIndex
            || !EffectRuntime.IsDownloadCarrier(owner, carrier))
        {
            return GameActionResult.Reject("action.invalid_pull_carrier");
        }

        if (!TryParseEntityReference(request.TargetId!, out var targetId))
        {
            return GameActionResult.Reject("action.invalid_pull_target");
        }

        if (owner.CloudStack.Count == 0
            || owner.CloudStack[owner.CloudStack.Count - 1].InstanceId != targetId)
        {
            return GameActionResult.Reject("action.invalid_pull_target");
        }

        var top = owner.CloudStack[owner.CloudStack.Count - 1];
        var requiresTarget = RequiresTargetSelection(top.Definition.PullEffects);
        long? selectedTargetId = null;
        if (requiresTarget)
        {
            if (request.SelectedEntityIds.Count != 1)
            {
                return GameActionResult.Reject("action.target_required");
            }

            selectedTargetId = request.SelectedEntityIds[0];
            if (!GetLegalTargets(state, owner.PlayerIndex, top)
                .Any(candidate => candidate.InstanceId == selectedTargetId.Value))
            {
                return GameActionResult.Reject("action.invalid_pull_effect_target");
            }
        }
        else if (request.SelectedEntityIds.Count != 0)
        {
            return GameActionResult.Reject("action.invalid_pull_effect_target");
        }

        var expectedActionId = CreateActionId(carrier.InstanceId, top.InstanceId, selectedTargetId);
        if (request.ActionId is not null
            && !string.Equals(request.ActionId, expectedActionId, StringComparison.Ordinal))
        {
            return GameActionResult.Reject("action.id_mismatch");
        }

        var runtime = new EffectRuntime(state);
        var dispatcher = EffectDispatcher.CreateDefault(runtime);
        if (!HasSupportedPullEffects(dispatcher, top.Definition.PullEffects))
        {
            return GameActionResult.Reject("action.unknown_effect");
        }

        var root = state.Events.Append("PULL_DECLARED", null, Data(
            "player", owner.PlayerIndex,
            "source", carrier.InstanceId,
            "target", top.InstanceId,
            "punish", top.Definition.DownloadCost));
        if (!_punishResolver.ResolveLifecyclePunish(
                state,
                owner.PlayerIndex,
                top.Definition.DownloadCost,
                root.EventId))
        {
            return GameActionResult.Accept();
        }

        dispatcher.Apply(
            new EffectSpec(EffectNames.Pull),
            new EffectContext(
                owner.PlayerIndex,
                root.EventId,
                sourceCard: carrier,
                selectedTargetId: selectedTargetId ?? carrier.InstanceId));
        return GameActionResult.Accept();
    }

    private static bool TryParseEntityReference(string value, out long id)
    {
        id = 0;
        if (value.StartsWith("entity_", StringComparison.Ordinal))
        {
            return long.TryParse(
                value.Substring("entity_".Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out id)
                && id > 0;
        }

        if (value.StartsWith("entity:", StringComparison.Ordinal))
        {
            return long.TryParse(
                value.Substring("entity:".Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out id)
                && id > 0;
        }

        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id)
            && id > 0;
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
