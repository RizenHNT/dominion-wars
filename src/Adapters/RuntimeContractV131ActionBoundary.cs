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

    /// <summary>
    /// This turn's punish-value modifier for the player's own cards. It is what
    /// makes a card cost more or less to play than its printed value, so it is the
    /// live form of "playing cards is paid for in the opponent's card flow".
    /// Purely a per-turn number: it carries no card identity, so publishing it
    /// leaks nothing that the printed card and the public board do not already
    /// show.
    /// </summary>
    public int PunishDeltaThisTurn { get; set; }

    /// <summary>
    /// Cards this player has drawn THIS TURN as a result of being punished. It is
    /// the direct measure of the punish mechanism's benefit to this player, and it
    /// is a public event count — every punish draw is visible to both sides — so it
    /// is not hidden information.
    /// </summary>
    public int PunishDrawnThisTurn { get; set; }

    /// <summary>
    /// Cards this player has discarded cumulatively, excluding the forced discard
    /// phase (the engine's own rule). A public event count.
    /// </summary>
    public int TotalDiscarded { get; set; }

    /// <summary>Consecutive turns this player has ended without taking damage.</summary>
    public int NoDamageTurns { get; set; }

    /// <summary>Whether this player has taken damage during the current cycle.</summary>
    public bool DamagedThisCycle { get; set; }

    /// <summary>True when this player's punish is converted to self-discard this turn.</summary>
    public bool PunishToSelfDiscardThisTurn { get; set; }

    /// <summary>True when this player's effects are protected from negation this turn.</summary>
    public bool ProtectedThisTurn { get; set; }

    /// <summary>True when this player's effects are negated this turn.</summary>
    public bool EffectsNegatedThisTurn { get; set; }
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

    /// <summary>
    /// Remaining end phases for an active chant. It is optional so old
    /// snapshots and cards without an active chant remain wire-compatible;
    /// null means that no active countdown was projected, not zero.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int? ChantRemaining { get; set; }

    /// <summary>
    /// Authoritative public pull progress for a landmark. It is optional so
    /// ordinary cards do not acquire a meaningless progress field.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int? LandmarkPullCount { get; set; }

    /// <summary>
    /// The victory objective this card DECLARES, when it declares one.
    ///
    /// Public information by design: a leader's win condition is printed on the card,
    /// so publishing it leaks nothing a player cannot already read. It is optional, and
    /// null means the card does not describe its objective in data — which is the
    /// honest answer for the 烈焰 leader, whose castle win is resolved by a separate
    /// path and has no threshold.
    ///
    /// Publishing it is what removes the consumer's need to re-parse a condition name.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public RuntimeVictoryObjectiveSnapshot? Victory { get; set; }
}

/// <summary>
/// A victory objective in the published contract: a closed-set metric, a direction, a
/// threshold, and the metric's CURRENT value. No condition name appears, so a consumer
/// reads the rule instead of guessing at it from an id.
/// </summary>
public sealed class RuntimeVictoryObjectiveSnapshot
{
    /// <summary>One of the engine's closed metric set.</summary>
    public string Metric { get; set; } = string.Empty;

    /// <summary>INCREASE or DECREASE.</summary>
    public string Direction { get; set; } = string.Empty;

    /// <summary>The threshold the metric is compared against.</summary>
    public int Target { get; set; }

    /// <summary>
    /// The metric's value right now, read by the engine's OWN metric reader.
    ///
    /// It is optional so a snapshot produced by an older build stays readable, and null
    /// means "not published", never zero — a zero would read as "no progress yet" and a
    /// consumer could not tell the difference between that and a missing reading.
    ///
    /// Publishing it is what lets a consumer compute "how much further" from the
    /// contract alone. Without it the consumer has to work the number out for itself,
    /// which is exactly the duplication the contract exists to end.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int? Current { get; set; }

    /// <summary>
    /// How much further this objective has to go, signed by the direction: zero when it
    /// is already met. Precomputed by the publisher so the arithmetic exists once rather
    /// than once per consumer.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int? Remaining { get; set; }

    /// <summary>
    /// Whether the condition is satisfied right now, as the ENGINE judges it. Null when
    /// the objective could not be measured.
    ///
    /// Published rather than left to the consumer to infer from Current vs Target,
    /// because a condition is not always a simple comparison — the castle break is met or
    /// it is not — and a consumer that guessed would be re-implementing the rule.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public bool? Met { get; set; }

    /// <summary>
    /// Why this objective could not be measured, when it could not be. Null when it was.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? UnmeasurableReason { get; set; }
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

    /// <summary>
    /// Explicit card choices supplied alongside an immutable advertised
    /// payload. This is intentionally a top-level request field: adding the
    /// choice to <see cref="Payload"/> would make a valid advertisement fail
    /// the field-for-field boundary comparison.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<long>? SelectedEntityIds { get; set; }
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
        if (action.Payload is null) return RuntimeActionValidation.Reject("action.payload_missing");
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
            !ValuesEqual(advertised.TargetId, action.TargetId) || advertised.CardId != action.CardId)
            return RuntimeActionValidation.Reject("action.advertisement_mismatch");

        var hasLegacyPayloadSelection = action.Payload is not null &&
            action.Payload.ContainsKey("selectedEntityIds");
        var hasTypedSelection = action.SelectedEntityIds is not null;
        if (hasLegacyPayloadSelection && hasTypedSelection)
            return RuntimeActionValidation.Reject("action.selection_channel_conflict");

        // The typed selection channel is deliberately separate from the
        // advertised Payload. A missing selection is left for the submission
        // gateway to reject with a precise required/count reason, so legacy
        // action-copy helpers can still materialize immutable advertised
        // fields first. The existing self-discard payload exception remains a
        // compatibility path for older AI clients; new callers should use the
        // typed field above.
        if (!PayloadMatches(advertised, action))
            return RuntimeActionValidation.Reject("action.advertisement_mismatch");

        if (action.SelectedEntityIds is not null && action.SelectedEntityIds.Count > 0 &&
            !RuntimeActionSelection.TryValidate(
                advertised,
                action.SelectedEntityIds,
                requireSelection: false,
                out var selectionReason))
            return RuntimeActionValidation.Reject(selectionReason);

        return RuntimeActionValidation.Accept();
    }

    /// <summary>
    /// ONE CONTROLLED EXCEPTION to "the submitted payload must equal the advertisement", and only one.
    ///
    /// A SELF-DISCARD PLAY_CARD cannot be submitted otherwise. When a player is under "punish converts
    /// to a self-discard", the engine advertises a PLAY_CARD carrying <c>discardRequired</c> and
    /// <c>discardCandidateIds</c>, and `CardPlayActionHandler.Prepare` then REQUIRES the chosen discard
    /// ids — rejecting with <c>action.discard_selection_required</c> when they are absent. But the
    /// selection has to travel on the wire, and the only wire name the gateway reads for a selection is
    /// <c>selectedEntityIds</c>, which strict equality forbade adding. The result was a loop: the policy
    /// re-picked the same action, every submission was rejected, and matches were TRUNCATED. Measured:
    /// 55 of 180 games in the stat-curve sweep died exactly here, which is why no win-rate curve could
    /// be produced at all.
    ///
    /// SO THE RULE IS: for a self-discard PLAY_CARD, <c>selectedEntityIds</c> MAY be present, and it is
    /// validated on its own terms in <see cref="ValidateSelfDiscardSelection"/> — the count must equal
    /// the advertised requirement and every id must be one of the advertised candidates. Everything
    /// else must still match EXACTLY, and for every other action type the payload must still match
    /// exactly, so this does not loosen the boundary in general: it admits one field, for one action
    /// shape, under its own validation.
    ///
    /// It deliberately does NOT auto-select a discard. Choosing which card to throw away is a player
    /// decision (owner ruling 2026-09-12: the player picks within the window and the system discards on
    /// timeout); the adapter's job is to carry that choice, not to make it.
    /// </summary>
    private static bool PayloadMatches(RuntimeLegalAction advertised, RuntimeGameAction action)
    {
        var advertisedPayload = advertised.Payload ?? new Dictionary<string, object?>();
        var submittedPayload = action.Payload ?? new Dictionary<string, object?>();

        var isSelfDiscard = string.Equals(advertised.Type, "PLAY_CARD", StringComparison.Ordinal)
            && advertisedPayload.ContainsKey("discardRequired")
            && advertisedPayload.ContainsKey("discardCandidateIds");

        if (!isSelfDiscard)
        {
            return ValuesEqual(advertisedPayload, submittedPayload);
        }

        // Count the submitted payload minus the one permitted extra key.
        var extraKeys = 0;
        foreach (var entry in submittedPayload)
        {
            if (string.Equals(entry.Key, "selectedEntityIds", StringComparison.Ordinal))
            {
                extraKeys++;
                continue;
            }

            if (!advertisedPayload.TryGetValue(entry.Key, out var expected)
                || !ValuesEqual(expected, entry.Value))
            {
                return false;
            }
        }

        if (extraKeys > 1) return false;

        // Everything the advertisement declared must still be present and equal.
        foreach (var entry in advertisedPayload)
        {
            if (!submittedPayload.TryGetValue(entry.Key, out var actual)
                || !ValuesEqual(entry.Value, actual))
            {
                return false;
            }
        }

        if (!submittedPayload.TryGetValue("selectedEntityIds", out var rawSelection))
        {
            // Absent selection: the engine will reject it with its own reason key, which is the
            // honest outcome — this adapter must not invent a discard to fill the gap.
            return true;
        }

        return ValidateSelfDiscardSelection(advertisedPayload, rawSelection);
    }

    /// <summary>
    /// The submitted discard selection must be legal on the advertisement's own terms: exactly the
    /// required number of ids, each one an advertised candidate. Fail-closed on anything else.
    /// </summary>
    private static bool ValidateSelfDiscardSelection(
        IReadOnlyDictionary<string, object?> advertisedPayload,
        object? rawSelection)
    {
        if (rawSelection is null) return false;
        if (!advertisedPayload.TryGetValue("discardRequired", out var rawRequired)
            || rawRequired is null
            || !RuntimeWireValue.TryGetInt64(rawRequired, out var required)
            || required < 0)
        {
            return false;
        }

        if (!RuntimeWireValue.TryEnumerate(rawSelection, out var values)) return false;

        var candidates = new HashSet<long>();
        if (advertisedPayload.TryGetValue("discardCandidateIds", out var rawCandidates)
            && rawCandidates is not null
            && RuntimeWireValue.TryEnumerate(rawCandidates, out var candidateValues))
        {
            foreach (var value in candidateValues)
            {
                if (RuntimeWireValue.TryGetInt64(value, out var candidateId)) candidates.Add(candidateId);
            }
        }

        var seen = new HashSet<long>();
        var count = 0;
        foreach (var value in values)
        {
            if (!RuntimeWireValue.TryGetInt64(value, out var id)) return false;
            if (id <= 0) return false;
            if (!seen.Add(id)) return false;
            if (!candidates.Contains(id)) return false;
            count++;
        }

        return count == required;
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
