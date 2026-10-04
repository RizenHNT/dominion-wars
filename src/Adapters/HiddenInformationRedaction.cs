using System;
using System.Collections.Generic;
using DominionWars.Engine.Events;

namespace DominionWars.Adapters
{

/// <summary>
/// Redacts hidden-information fields from engine events on their way to a
/// viewer. The engine keeps the full truth internally (its own state, timers
/// and resolution reads the untouched event log); only the event copies handed
/// to the transport boundary are filtered.
///
/// A face-down ambush is exactly the information the game hides from the
/// opponent, so a viewer who is not the ambush owner must never receive the
/// ambush card id, the entity id, or the ambush kind, in either the snapshot or
/// the event stream. The owner keeps the full shape because the owner already
/// sees the card in the viewer-scoped snapshot.
/// </summary>
public static class HiddenInformationRedaction
{
    /// <summary>Engine event types whose data identifies a hidden ambush card.</summary>
    private static readonly HashSet<string> AmbushIdentityEventTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "AMBUSH_SET",
            "AMBUSH_TRIGGERED",
        };

    /// <summary>Data keys that identify the hidden ambush card itself.</summary>
    private static readonly HashSet<string> AmbushIdentityDataKeys =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "source",
            "cardId",
            "kind",
        };

    public static IReadOnlyList<GameEvent> ToViewer(
        IReadOnlyList<GameEvent> events,
        int viewerPlayerIndex)
    {
        if (events is null) throw new ArgumentNullException(nameof(events));
        if (viewerPlayerIndex is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(viewerPlayerIndex));
        if (events.Count == 0) return Array.Empty<GameEvent>();

        var result = new List<GameEvent>(events.Count);
        foreach (var gameEvent in events)
        {
            if (gameEvent is null) throw new ArgumentException("Events cannot contain null.", nameof(events));
            result.Add(ShouldHideAmbushIdentity(gameEvent, viewerPlayerIndex)
                ? WithoutAmbushIdentity(gameEvent)
                : gameEvent);
        }

        return result.AsReadOnly();
    }

    private static bool ShouldHideAmbushIdentity(GameEvent gameEvent, int viewerPlayerIndex)
    {
        if (!AmbushIdentityEventTypes.Contains(gameEvent.EventType)) return false;
        if (!AmbushOwner(gameEvent, out var owner)) return true;
        return owner != viewerPlayerIndex;
    }

    /// <summary>
    /// Reads the ambush owner from the event's own <c>player</c> field. An
    /// event that cannot state its owner is treated as hidden from every
    /// viewer rather than leaking the identity by default.
    /// </summary>
    private static bool AmbushOwner(GameEvent gameEvent, out int owner)
    {
        owner = -1;
        if (!gameEvent.Data.TryGetValue("player", out var raw) || raw is null) return false;
        if (raw is int intValue)
        {
            owner = intValue;
            return true;
        }

        if (raw is long longValue && longValue is >= int.MinValue and <= int.MaxValue)
        {
            owner = (int)longValue;
            return true;
        }

        return false;
    }

    private static GameEvent WithoutAmbushIdentity(GameEvent gameEvent)
    {
        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var entry in gameEvent.Data)
        {
            if (AmbushIdentityDataKeys.Contains(entry.Key)) continue;
            data[entry.Key] = entry.Value;
        }

        return new GameEvent(
            gameEvent.EventId,
            gameEvent.ParentEventId,
            gameEvent.EventType,
            data,
            gameEvent.ContractVersion);
    }
}
}
