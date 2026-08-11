using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DominionWars.Engine.Events
{

public sealed class EventLog
{
    private readonly List<GameEvent> _events = new();
    private readonly ReadOnlyCollection<GameEvent> _readOnlyEvents;
    private long _nextEventId = 1;

    public EventLog()
    {
        _readOnlyEvents = _events.AsReadOnly();
    }

    public IReadOnlyList<GameEvent> Items => _readOnlyEvents;
    public int Count => _events.Count;
    public long LastEventId => _nextEventId - 1;

    public bool IsRootEvent(long eventId)
    {
        return eventId > 0
            && eventId <= _events.Count
            && _events[(int)eventId - 1].ParentEventId is null;
    }

    public GameEvent Append(
        string eventType,
        long? parentEventId = null,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        if (parentEventId.HasValue && parentEventId.Value >= _nextEventId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parentEventId),
                "A parent event must already exist in this log.");
        }

        var gameEvent = new GameEvent(_nextEventId, parentEventId, eventType, data);
        checked
        {
            _nextEventId++;
        }

        _events.Add(gameEvent);
        return gameEvent;
    }
}
}
