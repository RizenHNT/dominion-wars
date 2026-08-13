using System;
using System.Collections.Generic;

namespace DominionWars.Engine.Turns
{

/// <summary>
/// Transport-neutral request received from a player, UI, or AI. It carries
/// stable ids only; handlers resolve them against the current game state.
/// </summary>
public sealed class GameActionRequest
{
    public GameActionRequest(
        int actorPlayerIndex,
        string actionType,
        string? actionId = null,
        long? sourceEntityId = null,
        string? targetId = null,
        IReadOnlyList<long>? selectedEntityIds = null)
    {
        if (actorPlayerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(actorPlayerIndex));
        }

        if (string.IsNullOrWhiteSpace(actionType))
        {
            throw new ArgumentException("An action type is required.", nameof(actionType));
        }

        ActorPlayerIndex = actorPlayerIndex;
        ActionType = actionType;
        ActionId = actionId;
        SourceEntityId = sourceEntityId;
        TargetId = targetId;
        SelectedEntityIds = selectedEntityIds ?? Array.Empty<long>();
    }

    public int ActorPlayerIndex { get; }
    public string ActionType { get; }
    public string? ActionId { get; }
    public long? SourceEntityId { get; }
    public string? TargetId { get; }
    public IReadOnlyList<long> SelectedEntityIds { get; }
}

public sealed class GameActionResult
{
    private GameActionResult(bool accepted, string reasonKey)
    {
        Accepted = accepted;
        ReasonKey = reasonKey;
    }

    public bool Accepted { get; }
    public string ReasonKey { get; }

    public static GameActionResult Accept() => new GameActionResult(true, "action.accepted");

    public static GameActionResult Reject(string reasonKey)
    {
        if (string.IsNullOrWhiteSpace(reasonKey))
        {
            throw new ArgumentException("A rejection reason is required.", nameof(reasonKey));
        }

        return new GameActionResult(false, reasonKey);
    }
}
}
