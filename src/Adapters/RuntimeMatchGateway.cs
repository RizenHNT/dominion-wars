using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
    private readonly Dictionary<string, RuntimeActionSubmission> _submissions =
        new Dictionary<string, RuntimeActionSubmission>(StringComparer.Ordinal);
    private readonly HashSet<string> _seenActionIds = new HashSet<string>(StringComparer.Ordinal);
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
        if (_submissions.TryGetValue(action.ActionId, out var prior))
        {
            if (prior.Result.SnapshotRevision == action.SnapshotRevision &&
                string.Equals(action.MatchId, MatchId, StringComparison.Ordinal))
                return prior;
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
        _seenActionIds.Add(action.ActionId);
        _resultCache.TryStore(MatchId, beforeRevision, action.ActionId, transportResult);
        var submission = new RuntimeActionSubmission(
            transportResult,
            result.Events,
            GetSnapshot(action.Actor));
        _submissions.Add(action.ActionId, submission);
        return submission;
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

    private bool TryBuildRequest(
        RuntimeGameAction action,
        out GameActionRequest request,
        out string reason)
    {
        request = null!;
        reason = "action.mapping_invalid";
        long? source = null;
        if (!string.IsNullOrWhiteSpace(action.SourceId))
        {
            if (!TryParseEntityId(action.SourceId!, out var parsed))
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

        if (!TryReadSelectedIds(action.Payload, out var selected))
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
        out IReadOnlyList<long> selected)
    {
        selected = Array.Empty<long>();
        if (!payload.TryGetValue("selectedEntityIds", out var raw) || raw is null) return true;
        if (raw is string || raw is not IEnumerable values) return false;
        var result = new List<long>();
        foreach (var value in values)
        {
            try { result.Add(Convert.ToInt64(value, CultureInfo.InvariantCulture)); }
            catch { return false; }
        }
        selected = result.AsReadOnly();
        return true;
    }

    private static bool TryParseEntityId(string value, out long id)
    {
        id = 0;
        return value.StartsWith("entity_", StringComparison.Ordinal) &&
            long.TryParse(value.Substring("entity_".Length), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }

    private string? ToEngineTarget(string? target)
    {
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
}
