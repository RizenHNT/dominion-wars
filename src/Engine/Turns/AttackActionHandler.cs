using System;
using System.Collections.Generic;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

public sealed class AttackActionHandler : ITurnActionHandler
{
    private readonly AttackTargetPolicy _targets;

    public AttackActionHandler(AttackTargetPolicy? targets = null)
    {
        _targets = targets ?? new AttackTargetPolicy();
    }

    public bool CanHandle(string phaseId, string actionType)
    {
        return phaseId == TurnPhase.Action && actionType == LegalActionGenerator.Attack;
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

        var attacker = state.FindEntity(request.SourceEntityId.Value);
        if (attacker is null
            || attacker.ControllerPlayerIndex != request.ActorPlayerIndex
            || !state.GetPlayer(request.ActorPlayerIndex).Field.Contains(attacker))
        {
            return GameActionResult.Reject("action.invalid_attacker");
        }

        if (!_targets.TryResolve(state, attacker, request.TargetId!, out var target) || target is null)
        {
            return GameActionResult.Reject("action.invalid_target");
        }

        var expectedId = $"attack_{attacker.InstanceId}_{target.Id}";
        if (request.ActionId is not null
            && !string.Equals(request.ActionId, expectedId, StringComparison.Ordinal))
        {
            return GameActionResult.Reject("action.id_mismatch");
        }

        var root = state.Events.Append("ATTACK_DECLARED", null, Data(
            "player", request.ActorPlayerIndex,
            "source", attacker.InstanceId,
            "target", target.Id));
        new EffectRuntime(state).ResolveAttack(
            attacker,
            target.EntityId,
            target.CoreTarget,
            root.EventId);
        if (state.EndTurnRequested && !state.WinnerPlayerIndex.HasValue)
        {
            flow.Advance(state, request.ActorPlayerIndex);
        }

        return GameActionResult.Accept();
    }

    private static IReadOnlyDictionary<string, object?> Data(params object?[] values)
    {
        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < values.Length; index += 2)
        {
            data[(string)values[index]!] = values[index + 1];
        }

        return data;
    }
}
}
