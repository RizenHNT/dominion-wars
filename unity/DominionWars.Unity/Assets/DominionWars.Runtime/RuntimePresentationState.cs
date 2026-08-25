#nullable enable annotations

using System;
using System.Collections.Generic;
using DominionWars.Adapters;

namespace DominionWars.Unity.Runtime
{

public sealed class RuntimePresentationState
{
    private readonly List<RuntimeEventEnvelope> _events = new List<RuntimeEventEnvelope>();

    public RuntimeSnapshotEnvelope? Snapshot { get; internal set; }
    public IReadOnlyList<RuntimeEventEnvelope> Events => _events.AsReadOnly();
    public IReadOnlyList<RuntimeEventEnvelope> EventDelta { get; private set; } = Array.Empty<RuntimeEventEnvelope>();

    internal void AddEvents(IEnumerable<RuntimeEventEnvelope> events)
    {
        if (events is null) throw new ArgumentNullException(nameof(events));
        var delta = new List<RuntimeEventEnvelope>();
        foreach (var item in events)
        {
            if (item is null) throw new ArgumentException("Event entries cannot be null.", nameof(events));
            var alreadyPresent = false;
            foreach (var existing in _events)
            {
                if (existing.EventId != item.EventId) continue;
                alreadyPresent = true;
                break;
            }

            if (alreadyPresent) continue;
            _events.Add(item);
            delta.Add(item);
        }

        EventDelta = delta.AsReadOnly();
    }

    internal void ClearEvents()
    {
        _events.Clear();
        EventDelta = Array.Empty<RuntimeEventEnvelope>();
    }

    internal void ClearEventDelta()
    {
        EventDelta = Array.Empty<RuntimeEventEnvelope>();
    }
}
}
