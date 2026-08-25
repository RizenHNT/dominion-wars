#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Globalization;
using DominionWars.Adapters;
using DominionWars.Engine.Events;

namespace DominionWars.Unity.Runtime
{

/// <summary>
/// Projects the engine event delta returned by a local session into the
/// renderer-neutral 1.31 event envelope. The approved event mapping remains in
/// EngineProjectionAdapter; Unity does not interpret event meaning.
/// </summary>
public static class RuntimeEventProjection
{
    public static IReadOnlyList<RuntimeEventEnvelope> ToDelta(
        IEnumerable<GameEvent> events,
        int turn,
        string phase,
        long snapshotRevision)
    {
        if (events is null) throw new ArgumentNullException(nameof(events));
        if (turn < 0) throw new ArgumentOutOfRangeException(nameof(turn));
        if (string.IsNullOrWhiteSpace(phase)) throw new ArgumentException("An event phase is required.", nameof(phase));
        if (snapshotRevision < 0) throw new ArgumentOutOfRangeException(nameof(snapshotRevision));

        var result = new List<RuntimeEventEnvelope>();
        foreach (var gameEvent in events)
        {
            if (gameEvent is null) throw new ArgumentException("Event entries cannot be null.", nameof(events));

            // EngineProjectionAdapter is the single approved internal-to-wire
            // event map. Unsupported internal bookkeeping events are not UI
            // events and therefore do not cross the Unity presentation edge.
            GameEventDto projected;
            try
            {
                projected = EngineProjectionAdapter.ToEvent(gameEvent, turn, phase);
            }
            catch (NotSupportedException)
            {
                continue;
            }

            var targets = new List<object?>(projected.TargetIds.Count);
            foreach (var target in projected.TargetIds)
            {
                if (TryParseEntityId(target, out var entityId))
                {
                    targets.Add(entityId);
                }
                else if (string.Equals(target, "core:shared_castle", StringComparison.Ordinal))
                {
                    targets.Add("castle");
                }
                else
                {
                    targets.Add(target);
                }
            }

            result.Add(new RuntimeEventEnvelope
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                EventId = projected.EventId,
                ParentEventId = projected.ParentEventId,
                Type = projected.Type,
                Turn = projected.Turn,
                Phase = projected.Phase,
                SnapshotRevision = snapshotRevision,
                TargetIds = targets.AsReadOnly(),
                Data = projected.Data,
            });
        }

        return result.AsReadOnly();
    }

    private static bool TryParseEntityId(string value, out long entityId)
    {
        entityId = 0;
        return value.StartsWith("entity_", StringComparison.Ordinal)
            && long.TryParse(
                value.Substring("entity_".Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out entityId)
            && entityId > 0;
    }
}
}
