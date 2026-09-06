using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DominionWars.Adapters
{

public sealed class RuntimeSnapshotEnvelope
{
    public int ContractVersion { get; set; } = ContractVersionGuard.ExpectedVersion;
    public string MatchId { get; set; } = string.Empty;
    public long SnapshotRevision { get; set; }
    public int Turn { get; set; }
    public string Phase { get; set; } = string.Empty;
    public int CurrentPlayer { get; set; }
    public string ViewerPlayerId { get; set; } = string.Empty;
    public IReadOnlyList<RuntimePlayerSnapshot> Players { get; set; } = Array.Empty<RuntimePlayerSnapshot>();
    public RuntimeCastleSnapshot Castle { get; set; } = new RuntimeCastleSnapshot();
    public object? PendingPrompt { get; set; }
    public IReadOnlyList<RuntimeLegalAction> LegalActions { get; set; } = Array.Empty<RuntimeLegalAction>();
    /// <summary>
    /// Optional terminal outcome. It is populated only when the engine has
    /// already moved the turn to OVER; absent/null is the safe default for
    /// legacy and in-progress snapshots.
    /// </summary>
    public int? WinnerPlayerIndex { get; set; }
    public string? ReasonKey { get; set; }
}

public sealed class RuntimePlayerSnapshot
{
    public string PlayerId { get; set; } = string.Empty;
    public int? Life { get; set; }
    public int DeckCount { get; set; }
    public int HandCount { get; set; }
    public int FieldCount { get; set; }
    public int GraveyardCount { get; set; }
    public int AmbushCount { get; set; }
    public int CycleWinCount { get; set; }
    public int RootStacks { get; set; }
    public int RampantStacks { get; set; }
    public int PullCount { get; set; }
    public int CommitQueueCount { get; set; }
    public int CloudStackCount { get; set; }
    public IReadOnlyList<RuntimeCardSnapshot> Hand { get; set; } = Array.Empty<RuntimeCardSnapshot>();
    /// <summary>Viewer-owned set ambushes. Opponent ambush identities remain redacted.</summary>
    public IReadOnlyList<RuntimeCardSnapshot> Ambush { get; set; } = Array.Empty<RuntimeCardSnapshot>();
    public IReadOnlyList<RuntimeCardSnapshot> Field { get; set; } = Array.Empty<RuntimeCardSnapshot>();
    public IReadOnlyList<RuntimeCardSnapshot> LeaderZone { get; set; } = Array.Empty<RuntimeCardSnapshot>();
    public IReadOnlyList<RuntimeCardSnapshot> Graveyard { get; set; } = Array.Empty<RuntimeCardSnapshot>();
    public IReadOnlyList<RuntimeCardSnapshot> CommitQueue { get; set; } = Array.Empty<RuntimeCardSnapshot>();
    public IReadOnlyList<RuntimeCardSnapshot> CloudStack { get; set; } = Array.Empty<RuntimeCardSnapshot>();
}

public sealed class RuntimeCardSnapshot
{
    public long EntityId { get; set; }
    public string CardId { get; set; } = string.Empty;
    public int OwnerPlayer { get; set; }
    public bool Sealed { get; set; }

    /// <summary>
    /// Current attack from the authoritative CardInstance. It is optional so
    /// legacy snapshots remain readable; null means unavailable, not zero.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int? CurrentAttack { get; set; }

    /// <summary>
    /// Current health from the authoritative CardInstance. It is optional so
    /// legacy snapshots remain readable; null means unavailable, not zero.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int? CurrentHealth { get; set; }
}

public sealed class RuntimeCastleSnapshot
{
    public bool Enabled { get; set; }
    public int Health { get; set; }
}

public sealed class RuntimeLegalAction
{
    public int ContractVersion { get; set; } = ContractVersionGuard.ExpectedVersion;
    public long SnapshotRevision { get; set; }
    public string ActionId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Actor { get; set; }
    public object? SourceId { get; set; }
    public object? TargetId { get; set; }
    public string? CardId { get; set; }
    public string? ReasonKey { get; set; }
    public IReadOnlyDictionary<string, object?> Payload { get; set; } = new Dictionary<string, object?>();
}

public sealed class RuntimeGameAction
{
    public int ContractVersion { get; set; } = ContractVersionGuard.ExpectedVersion;
    public string MatchId { get; set; } = string.Empty;
    public long SnapshotRevision { get; set; }
    public string ActionId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Actor { get; set; }
    public object? SourceId { get; set; }
    public object? TargetId { get; set; }
    public string? CardId { get; set; }
    public IReadOnlyDictionary<string, object?> Payload { get; set; } = new Dictionary<string, object?>();
}

public sealed class RuntimeActionResult
{
    public int ContractVersion { get; set; } = ContractVersionGuard.ExpectedVersion;
    public string MatchId { get; set; } = string.Empty;
    public long SnapshotRevision { get; set; }
    public long ResultingSnapshotRevision { get; set; }
    public string ActionId { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public string ReasonKey { get; set; } = string.Empty;
}

public static class RuntimeActionBoundary
{
    public static RuntimeActionValidation Validate(RuntimeGameAction action, RuntimeSnapshotEnvelope snapshot)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (action.ContractVersion != ContractVersionGuard.ExpectedVersion || snapshot.ContractVersion != ContractVersionGuard.ExpectedVersion)
            return RuntimeActionValidation.Reject("contract.version_mismatch");
        if (string.IsNullOrWhiteSpace(snapshot.MatchId)) return RuntimeActionValidation.Reject("snapshot.match_id_missing");
        if (snapshot.SnapshotRevision < 0) return RuntimeActionValidation.Reject("snapshot.revision_invalid");
        if (action.SnapshotRevision < 0) return RuntimeActionValidation.Reject("action.revision_invalid");
        if (string.IsNullOrWhiteSpace(action.MatchId) || action.MatchId != snapshot.MatchId)
            return RuntimeActionValidation.Reject("action.wrong_match");
        if (action.Actor is < 0 or > 1) return RuntimeActionValidation.Reject("action.actor_invalid");
        if (action.SnapshotRevision < snapshot.SnapshotRevision) return RuntimeActionValidation.Reject("action.stale_snapshot");
        if (action.SnapshotRevision > snapshot.SnapshotRevision) return RuntimeActionValidation.Reject("action.future_snapshot");
        if (snapshot.LegalActions is null) return RuntimeActionValidation.Reject("snapshot.legal_actions_missing");
        if (string.IsNullOrWhiteSpace(action.ActionId)) return RuntimeActionValidation.Reject("action.id_required");
        if (string.IsNullOrWhiteSpace(action.Type)) return RuntimeActionValidation.Reject("action.type_required");

        RuntimeLegalAction? advertised = null;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in snapshot.LegalActions)
        {
            if (candidate is null) return RuntimeActionValidation.Reject("snapshot.legal_action_missing");
            if (string.IsNullOrWhiteSpace(candidate.ActionId)) return RuntimeActionValidation.Reject("snapshot.action_id_missing");
            if (candidate.ContractVersion != snapshot.ContractVersion) return RuntimeActionValidation.Reject("snapshot.legal_action_version_mismatch");
            if (candidate.SnapshotRevision != snapshot.SnapshotRevision) return RuntimeActionValidation.Reject("snapshot.legal_action_revision_mismatch");
            if (string.IsNullOrWhiteSpace(candidate.Type)) return RuntimeActionValidation.Reject("snapshot.action_type_missing");
            if (candidate.Actor is < 0 or > 1) return RuntimeActionValidation.Reject("snapshot.actor_invalid");
            if (!ids.Add(candidate.ActionId)) return RuntimeActionValidation.Reject("snapshot.duplicate_action_id");
            if (candidate.ActionId == action.ActionId) advertised = candidate;
        }
        if (advertised is null) return RuntimeActionValidation.Reject("action.not_advertised");
        if (advertised.Type != action.Type || advertised.Actor != action.Actor || advertised.ContractVersion != action.ContractVersion ||
            advertised.SnapshotRevision != action.SnapshotRevision || !ValuesEqual(advertised.SourceId, action.SourceId) ||
            !ValuesEqual(advertised.TargetId, action.TargetId) || advertised.CardId != action.CardId || !ValuesEqual(advertised.Payload, action.Payload))
            return RuntimeActionValidation.Reject("action.advertisement_mismatch");
        return RuntimeActionValidation.Accept();
    }

    internal static bool ValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (Equals(RuntimeWireValue.Normalize(left), RuntimeWireValue.Normalize(right))) return true;
        if (left is IReadOnlyDictionary<string, object?> leftMap && right is IReadOnlyDictionary<string, object?> rightMap)
        {
            if (leftMap.Count != rightMap.Count) return false;
            foreach (var entry in leftMap)
                if (!rightMap.TryGetValue(entry.Key, out var value) || !ValuesEqual(entry.Value, value)) return false;
            return true;
        }
        if (left is IEnumerable leftItems && right is IEnumerable rightItems && left is not string && right is not string)
        {
            var a = leftItems.GetEnumerator(); var b = rightItems.GetEnumerator();
            while (true)
            {
                var hasA = a.MoveNext(); var hasB = b.MoveNext();
                if (hasA != hasB) return false;
                if (!hasA) return true;
                if (!ValuesEqual(a.Current, b.Current)) return false;
            }
        }
        return Equals(left, right);
    }

}

public sealed class RuntimeActionValidation
{
    private RuntimeActionValidation(bool accepted, string reasonKey) { Accepted = accepted; ReasonKey = reasonKey; }
    public bool Accepted { get; }
    public string ReasonKey { get; }
    public static RuntimeActionValidation Accept() => new RuntimeActionValidation(true, "action.accepted");
    public static RuntimeActionValidation Reject(string reasonKey)
    {
        if (string.IsNullOrWhiteSpace(reasonKey)) throw new ArgumentException("A rejection reason is required.", nameof(reasonKey));
        return new RuntimeActionValidation(false, reasonKey);
    }
}
}
