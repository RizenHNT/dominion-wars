using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DominionWars.Engine;
using DominionWars.Engine.Events;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;

namespace DominionWars.Adapters
{

/// <summary>
/// Contract-facing orchestration around an already-composed engine match.
/// Setup, rules and phase handlers remain engine-owned; this class only adds
/// match identity, revision checks, redaction and idempotent submission.
/// </summary>
public sealed class RuntimeMatchGateway
{
    private readonly MatchController _controller;
    private readonly GameState _state;
    private readonly TurnFlow _flow;
    private readonly IRuntimeActionResultCache _resultCache;
    private readonly Dictionary<RuntimeSubmissionKey, CachedRuntimeSubmission> _submissions =
        new Dictionary<RuntimeSubmissionKey, CachedRuntimeSubmission>();
    private bool _initializationAttempted;
    private bool _initializationAccepted;
    private string _initializationReason = "match.not_initialized";
    private IReadOnlyList<GameEvent> _initializationEvents = Array.Empty<GameEvent>();
    private long _revision;

    public RuntimeMatchGateway(
        string matchId,
        GameState state,
        TurnFlow flow,
        TurnActionRouter router,
        IRuntimeActionResultCache? resultCache = null)
    {
        if (string.IsNullOrWhiteSpace(matchId) || !matchId.StartsWith("match_", StringComparison.Ordinal))
            throw new ArgumentException("A stable match id is required.", nameof(matchId));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        _controller = new MatchController(_state, _flow, router ?? throw new ArgumentNullException(nameof(router)));
        MatchId = matchId;
        _resultCache = resultCache ?? new InMemoryRuntimeActionResultCache();
    }

    public string MatchId { get; }
    public long SnapshotRevision => _revision;

    /// <summary>
    /// Executes the engine-owned START lifecycle exactly once and enters the
    /// next registered phase. Repeated calls return the same outcome and do
    /// not append events or advance the snapshot revision.
    /// </summary>
    public RuntimeMatchInitialization Initialize(int viewerPlayerIndex)
    {
        ValidateViewer(viewerPlayerIndex);
        if (_initializationAttempted)
        {
            return BuildInitialization(viewerPlayerIndex);
        }

        _initializationAttempted = true;
        if (_state.WinnerPlayerIndex.HasValue)
        {
            _initializationAccepted = false;
            _initializationReason = "match.initialization_game_over";
            return BuildInitialization(viewerPlayerIndex);
        }

        if (!string.Equals(_state.Turn.PhaseId, TurnPhase.Start, StringComparison.Ordinal))
        {
            _initializationAccepted = false;
            _initializationReason = "match.initialization_not_start";
            return BuildInitialization(viewerPlayerIndex);
        }

        var startIndex = IndexOfRoutePhase(TurnPhase.Start);
        if (startIndex < 0 || startIndex + 1 >= _flow.Route.Count)
        {
            _initializationAccepted = false;
            _initializationReason = "match.initialization_route_invalid";
            return BuildInitialization(viewerPlayerIndex);
        }

        var eventCount = _state.Events.Count;
        try
        {
            _flow.Advance(_state, _state.CurrentPlayerIndex);
        }
        catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException)
        {
            _initializationAccepted = false;
            _initializationReason = "match.initialization_failed";
            _initializationEvents = Array.Empty<GameEvent>();
            throw new InvalidOperationException("match.initialization_failed", exception);
        }

        if (string.Equals(_state.Turn.PhaseId, TurnPhase.Start, StringComparison.Ordinal) ||
            _state.WinnerPlayerIndex.HasValue)
        {
            _initializationAccepted = false;
            _initializationReason = "match.initialization_failed";
            _initializationEvents = Array.Empty<GameEvent>();
            throw new InvalidOperationException("match.initialization_failed");
        }

        _initializationEvents = Array.AsReadOnly(
            _state.Events.Items.Skip(eventCount).ToArray());
        _initializationAccepted = true;
        _initializationReason = "match.initialized";
        checked { _revision++; }
        return BuildInitialization(viewerPlayerIndex);
    }

    /// <summary>
    /// Returns the cached result of the one-time initialization without
    /// executing any lifecycle or mutating the match. The factory uses
    /// Initialize once; composition roots can then retrieve the same
    /// snapshot/event delta for another viewer.
    /// </summary>
    public RuntimeMatchInitialization GetInitialization(int viewerPlayerIndex)
    {
        ValidateViewer(viewerPlayerIndex);
        if (!_initializationAttempted)
            throw new InvalidOperationException("match.not_initialized");
        return BuildInitialization(viewerPlayerIndex);
    }

    public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
    {
        return RuntimeSnapshotProjection.ToSnapshot(
            _state, MatchId, _revision, viewerPlayerIndex, _flow);
    }

    public RuntimeActionSubmission Submit(RuntimeGameAction action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (string.IsNullOrWhiteSpace(action.ActionId))
            return Reject(action, "action.id_required");
        var submissionKey = new RuntimeSubmissionKey(action.MatchId, action.SnapshotRevision, action.ActionId);
        if (_submissions.TryGetValue(submissionKey, out var prior))
        {
            if (ActionsEqual(prior.Action, action))
                return prior.Submission;
            return Reject(action, "action.duplicate");
        }
        if (action.Actor is < 0 or > 1)
            return Reject(action, "action.actor_invalid");

        var snapshot = GetSnapshot(action.Actor);
        var validation = RuntimeActionBoundary.Validate(action, snapshot);
        if (!validation.Accepted)
            return Reject(action, validation.ReasonKey, snapshot);

        if (!TryBuildRequest(action, out var request, out var mappingReason))
            return Reject(action, mappingReason, snapshot);

        var result = _controller.Submit(request);
        var beforeRevision = _revision;
        if (result.Accepted) _revision++;
        var transportResult = new RuntimeActionResult
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = MatchId,
            SnapshotRevision = beforeRevision,
            ResultingSnapshotRevision = _revision,
            ActionId = action.ActionId,
            Accepted = result.Accepted,
            ReasonKey = result.ReasonKey,
        };
        _resultCache.TryStore(MatchId, beforeRevision, action.ActionId, transportResult);
        var submission = new RuntimeActionSubmission(
            transportResult,
            result.Events,
            GetSnapshot(action.Actor));
        _submissions.Add(submissionKey, new CachedRuntimeSubmission(CopyAction(action), submission));
        return submission;
    }

    private static bool ActionsEqual(RuntimeGameAction left, RuntimeGameAction right)
    {
        return left.ContractVersion == right.ContractVersion &&
            string.Equals(left.MatchId, right.MatchId, StringComparison.Ordinal) &&
            left.SnapshotRevision == right.SnapshotRevision &&
            string.Equals(left.ActionId, right.ActionId, StringComparison.Ordinal) &&
            string.Equals(left.Type, right.Type, StringComparison.Ordinal) &&
            left.Actor == right.Actor &&
            RuntimeActionBoundary.ValuesEqual(left.SourceId, right.SourceId) &&
            RuntimeActionBoundary.ValuesEqual(left.TargetId, right.TargetId) &&
            string.Equals(left.CardId, right.CardId, StringComparison.Ordinal) &&
            RuntimeActionBoundary.ValuesEqual(left.Payload, right.Payload);
    }

    private static RuntimeGameAction CopyAction(RuntimeGameAction action)
    {
        return new RuntimeGameAction
        {
            ContractVersion = action.ContractVersion,
            MatchId = action.MatchId,
            SnapshotRevision = action.SnapshotRevision,
            ActionId = action.ActionId,
            Type = action.Type,
            Actor = action.Actor,
            SourceId = CopyWireValue(action.SourceId),
            TargetId = CopyWireValue(action.TargetId),
            CardId = action.CardId,
            Payload = CopyPayload(action.Payload),
        };
    }

    private static IReadOnlyDictionary<string, object?> CopyPayload(
        IReadOnlyDictionary<string, object?> payload)
    {
        var copy = new Dictionary<string, object?>(payload.Count, StringComparer.Ordinal);
        foreach (var entry in payload)
            copy.Add(entry.Key, CopyWireValue(entry.Value));
        return copy;
    }

    private static object? CopyWireValue(object? value)
    {
        if (value is null) return null;
        if (value is IReadOnlyDictionary<string, object?> map)
            return CopyPayload(map);
        if (value is IEnumerable items && value is not string)
        {
            var copy = new List<object?>();
            foreach (var item in items)
                copy.Add(CopyWireValue(item));
            return copy.AsReadOnly();
        }
        return RuntimeWireValue.Normalize(value);
    }

    private readonly struct RuntimeSubmissionKey : IEquatable<RuntimeSubmissionKey>
    {
        public RuntimeSubmissionKey(string matchId, long snapshotRevision, string actionId)
        {
            MatchId = matchId;
            SnapshotRevision = snapshotRevision;
            ActionId = actionId;
        }

        public string MatchId { get; }
        public long SnapshotRevision { get; }
        public string ActionId { get; }

        public bool Equals(RuntimeSubmissionKey other)
        {
            return string.Equals(MatchId, other.MatchId, StringComparison.Ordinal) &&
                SnapshotRevision == other.SnapshotRevision &&
                string.Equals(ActionId, other.ActionId, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is RuntimeSubmissionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(MatchId);
                hash = (hash * 397) ^ SnapshotRevision.GetHashCode();
                return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(ActionId);
            }
        }
    }

    private sealed class CachedRuntimeSubmission
    {
        public CachedRuntimeSubmission(RuntimeGameAction action, RuntimeActionSubmission submission)
        {
            Action = action;
            Submission = submission;
        }

        public RuntimeGameAction Action { get; }
        public RuntimeActionSubmission Submission { get; }
    }

    private RuntimeActionSubmission Reject(
        RuntimeGameAction action,
        string reason,
        RuntimeSnapshotEnvelope? snapshot = null)
    {
        var revision = snapshot?.SnapshotRevision ?? Math.Max(0, action.SnapshotRevision);
        var result = new RuntimeActionResult
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = MatchId,
            SnapshotRevision = revision,
            ResultingSnapshotRevision = _revision,
            ActionId = action.ActionId ?? string.Empty,
            Accepted = false,
            ReasonKey = reason,
        };
        return new RuntimeActionSubmission(
            result,
            Array.Empty<GameEvent>(),
            snapshot ?? GetSnapshot(0));
    }

    private RuntimeMatchInitialization BuildInitialization(int viewerPlayerIndex)
    {
        return new RuntimeMatchInitialization(
            _initializationAccepted,
            _initializationReason,
            _initializationEvents,
            GetSnapshot(viewerPlayerIndex));
    }

    private int IndexOfRoutePhase(string phaseId)
    {
        for (var index = 0; index < _flow.Route.Count; index++)
        {
            if (string.Equals(_flow.Route[index], phaseId, StringComparison.Ordinal))
                return index;
        }

        return -1;
    }

    private static void ValidateViewer(int viewerPlayerIndex)
    {
        if (viewerPlayerIndex is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(viewerPlayerIndex));
    }

    private bool TryBuildRequest(
        RuntimeGameAction action,
        out GameActionRequest request,
        out string reason)
    {
        request = null!;
        reason = "action.mapping_invalid";
        long? source = null;
        if (action.SourceId is not null)
        {
            if (!TryParseEntityId(action.SourceId, out var parsed))
            {
                reason = "action.source_id_invalid";
                return false;
            }
            source = parsed;
        }

        var target = ToEngineTarget(action.TargetId);
        if (action.TargetId is not null && target is null)
        {
            reason = "action.target_id_invalid";
            return false;
        }

        var isDiscard = string.Equals(action.Type, TurnAction.DiscardComplete, StringComparison.Ordinal);
        if (!TryReadSelectedIds(action.Payload, isDiscard, out var selected))
        {
            reason = "action.selected_ids_invalid";
            return false;
        }

        request = new GameActionRequest(
            action.Actor,
            action.Type,
            action.ActionId,
            source,
            target,
            selected);
        return true;
    }

    private static bool TryReadSelectedIds(
        IReadOnlyDictionary<string, object?> payload,
        bool allowDiscardAdvertisement,
        out IReadOnlyList<long> selected)
    {
        selected = Array.Empty<long>();
        if (payload.TryGetValue("selectedEntityIds", out var raw))
        {
            if (raw is null) return true;
            return TryReadIdList(raw, out selected, requirePositiveUnique: false);
        }

        // The discard-phase legal action advertises the complete candidate
        // set and the engine-owned required count. The current presentation
        // action is intentionally submitted unchanged, so bridge that legacy
        // advertisement shape into the engine request here. The handler still
        // validates the actual requirement, hand membership, and uniqueness;
        // malformed counts/candidate lists therefore remain fail-closed.
        if (!allowDiscardAdvertisement)
        {
            return true;
        }

        var hasRequiredCount = payload.ContainsKey("requiredCount");
        var hasCandidateIds = payload.ContainsKey("candidateIds");
        if (!hasRequiredCount && !hasCandidateIds)
        {
            return true;
        }

        if (!payload.TryGetValue("requiredCount", out var rawRequired) ||
            !RuntimeWireValue.TryGetInt64(rawRequired!, out var required) ||
            required < 0 ||
            !payload.TryGetValue("candidateIds", out var rawCandidates) ||
            rawCandidates is null ||
            !TryReadIdList(rawCandidates, out var candidates, requirePositiveUnique: true))
        {
            return false;
        }

        if (required > candidates.Count || required > int.MaxValue)
        {
            selected = Array.Empty<long>();
            return false;
        }

        selected = candidates.Take((int)required).ToArray();
        return true;
    }

    private static bool TryReadIdList(
        object raw,
        out IReadOnlyList<long> selected,
        bool requirePositiveUnique)
    {
        selected = Array.Empty<long>();
        if (raw is string || !RuntimeWireValue.TryEnumerate(raw, out var values)) return false;

        var result = new List<long>();
        var seen = requirePositiveUnique ? new HashSet<long>() : null;
        foreach (var value in values)
        {
            if (!RuntimeWireValue.TryGetInt64(value, out var id) ||
                (requirePositiveUnique && (id <= 0 || !seen!.Add(id))))
                return false;
            result.Add(id);
        }

        selected = result.AsReadOnly();
        return true;
    }

    private static bool TryParseEntityId(object value, out long id)
    {
        id = 0;
        if (RuntimeWireValue.TryGetString(value, out var text))
        {
            if (text!.StartsWith("entity_", StringComparison.Ordinal))
                return long.TryParse(text.Substring("entity_".Length), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
            return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
        }
        return RuntimeWireValue.TryGetInt64(value, out id) && id > 0;
    }

    private string? ToEngineTarget(object? rawTarget)
    {
        if (rawTarget is null) return null;
        RuntimeWireValue.TryGetString(rawTarget, out var target);
        if (target is null && TryParseEntityId(rawTarget, out var numericEntity))
            return "entity_" + numericEntity.ToString("D12", CultureInfo.InvariantCulture);
        if (target is null) return null;
        if (target == "castle") return "core:shared_castle";
        if (target.StartsWith("entity_", StringComparison.Ordinal)) return target;
        if (target == "player_0" || target == "player_1") return "core:" + target + ":life";
        if (target == "leader_0" || target == "leader_1")
        {
            var owner = target[target.Length - 1] - '0';
            var leader = _state.GetPlayer(owner).Leader;
            return leader is null ? null : "entity_" + leader.InstanceId.ToString("D12", CultureInfo.InvariantCulture);
        }
        return target.StartsWith("prompt_", StringComparison.Ordinal) ? target : null;
    }
}

public sealed class RuntimeActionSubmission
{
    public RuntimeActionSubmission(
        RuntimeActionResult result,
        IReadOnlyList<GameEvent> events,
        RuntimeSnapshotEnvelope snapshot)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
        Events = events is null ? throw new ArgumentNullException(nameof(events)) : Array.AsReadOnly(new List<GameEvent>(events).ToArray());
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public RuntimeActionResult Result { get; }
    public IReadOnlyList<GameEvent> Events { get; }
    public RuntimeSnapshotEnvelope Snapshot { get; }
}

public sealed class RuntimeMatchInitialization
{
    public RuntimeMatchInitialization(
        bool accepted,
        string reasonKey,
        IReadOnlyList<GameEvent> events,
        RuntimeSnapshotEnvelope snapshot)
    {
        if (string.IsNullOrWhiteSpace(reasonKey))
            throw new ArgumentException("An initialization reason is required.", nameof(reasonKey));
        Accepted = accepted;
        ReasonKey = reasonKey;
        Events = events is null
            ? throw new ArgumentNullException(nameof(events))
            : Array.AsReadOnly(new List<GameEvent>(events).ToArray());
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public bool Accepted { get; }
    public string ReasonKey { get; }
    public IReadOnlyList<GameEvent> Events { get; }
    public RuntimeSnapshotEnvelope Snapshot { get; }
}
}
