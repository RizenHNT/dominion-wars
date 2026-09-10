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
    public string? ReasonKey { get; set; }
    public IReadOnlyList<object?> TargetIds { get; set; } = Array.Empty<object?>();
    public IReadOnlyDictionary<string, object?> Data { get; set; }
        = new Dictionary<string, object?>();
}

public sealed class RuntimeEventCursor
{
    private static readonly HashSet<string> EventTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "PHASE_CHANGED", "TURN_CHANGED", "CARD_PLAYED", "AMBUSH_SET", "AMBUSH_TRIGGERED",
        "CARDS_DRAWN", "ATTACK_DECLARED", "TARGET_REJECTED", "DAMAGE_APPLIED", "HEAL_APPLIED",
        "MINION_DESTROYED", "PUNISH_ISSUED",
        "PUNISH_DRAW", "PUNISH_TRIGGERED", "CHAIN_LINK", "CHAIN_RESOLVED", "CASTLE_DAMAGED",
        "CASTLE_BROKEN", "LEADER_MANIFESTED", "LEADER_DISABLED", "VICTORY_PROGRESS", "DECK_CYCLED",
        "CARD_DISCARDED", "COMMIT_DECLARED", "CARD_COMMITTED", "CARD_PUSHED",
        "PULL_DECLARED", "CARD_PULLED", "GAME_OVER",
    };

    private static readonly HashSet<string> DataKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "amount", "cardId", "count", "reasonKey", "sourceId", "targetIds", "winnerPlayerIndex",
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
        foreach (var key in envelope.Data.Keys)
        {
            if (key is null || !DataKeys.Contains(key)) return "event.data_field_unknown";
        }

        if (envelope.Type == "GAME_OVER")
        {
            return ValidateGameOver(envelope);
        }

        return null;
    }

    private static string? ValidateGameOver(RuntimeEventEnvelope envelope)
    {
        if (envelope.Phase != "OVER") return "event.game_over_phase_invalid";
        if (!IsReasonKey(envelope.ReasonKey)) return "event.game_over_reason_missing";

        // ui_event.schema.json declares GAME_OVER.data as an exact two-field
        // object.  Do not accept a terminal event with an incomplete or
        // legacy-shaped outcome payload.
        if (envelope.Data.Count != 2
            || !envelope.Data.TryGetValue("winnerPlayerIndex", out var winner)
            || !envelope.Data.TryGetValue("reasonKey", out var dataReason))
        {
            return "event.game_over_data_invalid";
        }

        if (!TryReadPlayerIndex(winner, out _)) return "event.game_over_winner_invalid";
        if (dataReason is not string reason || !IsReasonKey(reason))
            return "event.game_over_reason_invalid";
        if (!string.Equals(envelope.ReasonKey, reason, StringComparison.Ordinal))
            return "event.game_over_reason_mismatch";

        return null;
    }

    private static bool IsReasonKey(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (!IsReasonKeyStart(value[0])) return false;
        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (!(character is >= 'a' and <= 'z')
                && !(character is >= '0' and <= '9')
                && character != '_'
                && character != '.'
                && character != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsReasonKeyStart(char character)
    {
        return (character is >= 'a' and <= 'z') || (character is >= '0' and <= '9');
    }

    private static bool TryReadPlayerIndex(object? value, out int playerIndex)
    {
        playerIndex = value switch
        {
            byte byteValue when byteValue <= 1 => byteValue,
            sbyte sbyteValue when sbyteValue is >= 0 and <= 1 => sbyteValue,
            short shortValue when shortValue is >= 0 and <= 1 => shortValue,
            ushort ushortValue when ushortValue <= 1 => ushortValue,
            int intValue when intValue is >= 0 and <= 1 => intValue,
            uint uintValue when uintValue <= 1 => (int)uintValue,
            long longValue when longValue is >= 0 and <= 1 => (int)longValue,
            ulong ulongValue when ulongValue <= 1 => (int)ulongValue,
            _ => -1,
        };

        return playerIndex is 0 or 1;
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
