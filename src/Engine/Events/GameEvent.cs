using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DominionWars.Engine.Events
{

public sealed class GameEvent
{
    public GameEvent(
        long eventId,
        long? parentEventId,
        string eventType,
        IReadOnlyDictionary<string, object?>? data = null,
        int contractVersion = 1)
    {
        if (eventId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eventId));
        }

        if (parentEventId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parentEventId));
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("An event type is required.", nameof(eventType));
        }

        if (contractVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contractVersion));
        }

        EventId = eventId;
        ParentEventId = parentEventId;
        EventType = eventType;
        ContractVersion = contractVersion;
        Data = CopyData(data);
    }

    public long EventId { get; }
    public long? ParentEventId { get; }
    public string EventType { get; }
    public int ContractVersion { get; }
    public IReadOnlyDictionary<string, object?> Data { get; }

    private static IReadOnlyDictionary<string, object?> CopyData(
        IReadOnlyDictionary<string, object?>? data)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (data is not null)
        {
            foreach (var entry in data)
            {
                copy[entry.Key] = entry.Value;
            }
        }

        return new ReadOnlyDictionary<string, object?>(copy);
    }
}
}
