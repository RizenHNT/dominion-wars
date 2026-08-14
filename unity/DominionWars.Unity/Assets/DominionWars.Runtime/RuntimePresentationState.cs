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

    internal void AddEvents(IEnumerable<RuntimeEventEnvelope> events)
    {
        foreach (var item in events) _events.Add(item);
    }
}
}
