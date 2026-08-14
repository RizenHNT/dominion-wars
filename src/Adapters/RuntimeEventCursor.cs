using System;
using System.Collections.Generic;
using System.Globalization;

namespace DominionWars.Adapters
{

/// <summary>
/// Validates the ordered 1.31 event transport. Engine events remain the
/// source of truth; callers must provide explicit turn/phase/revision metadata.
/// </summary>
public sealed class RuntimeEventEnvelope
{
    public int ContractVersion { get; set; } = ContractVersionGuard.ExpectedVersion;
    public string EventId { get; set; } = string.Empty;
    public string? ParentEventId { get; set; }
    public string Type { get; set; } = string.Empty;
    public int Turn { get; set; }
    public string Phase { get; set; } = string.Empty;
    public long SnapshotRevision { get; set; }
    public IReadOnlyList<string> TargetIds { get; set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, object?> Data { get; set; }
        = new Dictionary<string, object?>();
}

public sealed class RuntimeEventCursor
{
    private static readonly HashSet<string> EventTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "PHASE_CHANGED", "TURN_CHANGED", "CARD_PLAYED", "AMBUSH_SET", "AMBUSH_TRIGGERED",
        "ATTACK_DECLARED", "TARGET_REJECTED", "DAMAGE_APPLIED", "HEAL_APPLIED", "PUNISH_ISSUED",
        "PUNISH_DRAW", "PUNISH_TRIGGERED", "CHAIN_LINK", "CHAIN_RESOLVED", "CASTLE_DAMAGED",
        "CASTLE_BROKEN", "LEADER_MANIFESTED", "LEADER_DISABLED", "VICTORY_PROGRESS", "DECK_CYCLED",
        "CARD_DISCARDED", "GAME_OVER",
    };

    private readonly HashSet<string> _known = new HashSet<string>(StringComparer.Ordinal);
    private long? _lastNumber;

    public string? LastEventId { get; private set; }

    public RuntimeEventCursorResult Accept(RuntimeEventEnvelope envelope)
    {
        if (envelope is null) throw new ArgumentNullException(nameof(envelope));
        var reason = Validate(envelope);
        if (reason is not null) return RuntimeEventCursorResult.Reject(reason);

        var number = ParseNumber(envelope.EventId);
        if (_known.Contains(envelope.EventId)) return RuntimeEventCursorResult.Reject("event.duplicate");
        if (_lastNumber.HasValue && number != _lastNumber.Value + 1)
            return RuntimeEventCursorResult.Reject(number <= _lastNumber.Value ? "event.out_of_order" : "event.gap");
        if (envelope.ParentEventId is not null && !_known.Contains(envelope.ParentEventId))
            return RuntimeEventCursorResult.Reject("event.parent_missing");

        _known.Add(envelope.EventId);
        _lastNumber = number;
        LastEventId = envelope.EventId;
        return RuntimeEventCursorResult.Accept();
    }

    public RuntimeEventCursorResult Accept(IEnumerable<RuntimeEventEnvelope> envelopes)
    {
        if (envelopes is null) throw new ArgumentNullException(nameof(envelopes));
        foreach (var envelope in envelopes)
        {
            var result = Accept(envelope);
            if (!result.Accepted) return result;
        }
        return RuntimeEventCursorResult.Accept();
    }

    private static string? Validate(RuntimeEventEnvelope envelope)
    {
        if (envelope.ContractVersion != ContractVersionGuard.ExpectedVersion) return "event.version_mismatch";
        if (!IsEventId(envelope.EventId)) return "event.id_invalid";
        if (envelope.ParentEventId is not null && !IsEventId(envelope.ParentEventId)) return "event.parent_invalid";
        if (!EventTypes.Contains(envelope.Type)) return "event.type_unknown";
        if (envelope.Turn < 0) return "event.turn_invalid";
        if (envelope.SnapshotRevision < 0) return "event.revision_invalid";
        if (!IsPhase(envelope.Phase)) return "event.phase_invalid";
        if (envelope.TargetIds is null || envelope.Data is null) return "event.payload_missing";
        return null;
    }

    private static bool IsEventId(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.StartsWith("evt_", StringComparison.Ordinal) &&
            value.Length == 16 && long.TryParse(value.Substring(4), NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private static long ParseNumber(string value) => long.Parse(value.Substring(4), CultureInfo.InvariantCulture);

    private static bool IsPhase(string value)
    {
        return value == "START" || value == "AMBUSH" || value == "ACTION" ||
            value == "DISCARD" || value == "END" || value == "OVER";
    }
}

public sealed class RuntimeEventCursorResult
{
    private RuntimeEventCursorResult(bool accepted, string reasonKey)
    {
        Accepted = accepted;
        ReasonKey = reasonKey;
    }

    public bool Accepted { get; }
    public string ReasonKey { get; }
    public static RuntimeEventCursorResult Accept() => new RuntimeEventCursorResult(true, "event.accepted");
    public static RuntimeEventCursorResult Reject(string reason) => new RuntimeEventCursorResult(false, reason);
}
}
