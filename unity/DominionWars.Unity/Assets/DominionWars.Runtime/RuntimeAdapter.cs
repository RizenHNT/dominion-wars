using System;
using System.Collections.Generic;
using DominionWars.Adapters;
using DominionWars.Engine.Events;

namespace DominionWars.Unity.Runtime
{

/// <summary>
/// Renderer-neutral Unity-side state holder. It consumes snapshots/actions/
/// events and never reproduces engine rules or phase transitions.
/// </summary>
public sealed class RuntimeAdapter : IDisposable
{
    private readonly IRuntimeSession _session;
    private readonly List<RuntimeEventEnvelope> _acceptedTransportEvents = new List<RuntimeEventEnvelope>();
    private bool _closed;

    public RuntimeAdapter(IRuntimeSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Presentation = new RuntimePresentationState();
    }

    public RuntimePresentationState Presentation { get; }

    /// <summary>
    /// Reprojects the current authoritative match for one viewer. This is a
    /// transport/presentation operation only: it does not advance the match
    /// or derive legal actions in Unity.
    /// </summary>
    public RuntimeSnapshotEnvelope RefreshSnapshot(int viewerPlayerIndex)
    {
        EnsureOpen();
        if (viewerPlayerIndex is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(viewerPlayerIndex));

        var snapshot = _session.GetSnapshot(viewerPlayerIndex);
        var expectedViewerPlayerId = "player_" + viewerPlayerIndex;
        if (snapshot is null ||
            !string.Equals(snapshot.ViewerPlayerId, expectedViewerPlayerId, StringComparison.Ordinal))
        {
            // Validate the session response before AcceptSnapshot can clear a
            // delta or replace the current presentation state.
            throw new InvalidOperationException(
                "Snapshot viewer identity does not match the requested player.");
        }
        AcceptSnapshot(snapshot);
        return snapshot;
    }

    public void AcceptSnapshot(RuntimeSnapshotEnvelope snapshot)
    {
        EnsureOpen();
        RuntimeSnapshotProjection.Validate(snapshot);
        if (Presentation.Snapshot is not null &&
            !string.Equals(Presentation.Snapshot.MatchId, snapshot.MatchId, StringComparison.Ordinal))
            throw new InvalidOperationException("Snapshot match identity changed.");
        if (Presentation.Snapshot is not null && snapshot.SnapshotRevision < Presentation.Snapshot.SnapshotRevision)
            throw new InvalidOperationException("A stale snapshot cannot replace the current presentation.");
        // A snapshot refresh is not an event delta. A viewer change must also
        // discard viewer-scoped history, regardless of whether this entry
        // point was RefreshSnapshot or a direct AcceptSnapshot call.
        if (Presentation.Snapshot is not null &&
            !string.Equals(
                Presentation.Snapshot.ViewerPlayerId,
                snapshot.ViewerPlayerId,
                StringComparison.Ordinal))
        {
            Presentation.ClearEvents();
        }
        else
        {
            Presentation.ClearEventDelta();
        }
        Presentation.Snapshot = snapshot;
    }

    public RuntimeActionSubmission Submit(RuntimeGameAction action)
    {
        EnsureOpen();
        if (Presentation.Snapshot is null) throw new InvalidOperationException("A snapshot is required before submitting actions.");
        var validation = RuntimeActionBoundary.Validate(action, Presentation.Snapshot);
        if (!validation.Accepted) throw new InvalidOperationException(validation.ReasonKey);
        var submission = _session.Submit(action);
        ValidateSubmission(action, submission);
        var eventDelta = RuntimeEventProjection.ToDelta(
            submission.Events,
            submission.Snapshot.Turn,
            submission.Snapshot.Phase,
            submission.Result.ResultingSnapshotRevision);
        // Project the event delta before publishing the resulting snapshot so
        // a malformed session response cannot partially update presentation.
        AcceptSnapshot(submission.Snapshot);
        Presentation.AddEvents(eventDelta);
        return submission;
    }

    public void AcceptEventDelta(
        IEnumerable<GameEvent> events,
        RuntimeSnapshotEnvelope snapshot)
    {
        EnsureOpen();
        if (events is null) throw new ArgumentNullException(nameof(events));
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (Presentation.Snapshot is null)
            throw new InvalidOperationException("A snapshot is required before accepting events.");
        if (!string.Equals(Presentation.Snapshot.MatchId, snapshot.MatchId, StringComparison.Ordinal))
            throw new InvalidOperationException("Event delta match identity changed.");
        if (Presentation.Snapshot.SnapshotRevision != snapshot.SnapshotRevision)
            throw new InvalidOperationException("Event delta revision does not match the accepted snapshot.");

        var eventDelta = RuntimeEventProjection.ToDelta(
            events,
            snapshot.Turn,
            snapshot.Phase,
            snapshot.SnapshotRevision);
        Presentation.AddEvents(eventDelta);
    }

    public void ApplyEvents(IEnumerable<RuntimeEventEnvelope> events)
    {
        EnsureOpen();
        if (events is null) throw new ArgumentNullException(nameof(events));
        if (Presentation.Snapshot is null)
            throw new InvalidOperationException("A snapshot is required before accepting events.");

        var materialized = new List<RuntimeEventEnvelope>();
        foreach (var item in events)
        {
            if (item is null) throw new ArgumentException("Event entries cannot be null.", nameof(events));
            materialized.Add(item);
        }

        // Validate the complete batch against a temporary cursor first. The
        // live cursor and presentation state remain untouched if any suffix
        // is invalid, so a caller can safely retry the batch.
        var candidate = new RuntimeEventCursor();
        foreach (var accepted in _acceptedTransportEvents)
        {
            var historyResult = candidate.Accept(accepted);
            if (!historyResult.Accepted)
                throw new InvalidOperationException("Event cursor history is invalid: " + historyResult.ReasonKey);
        }
        foreach (var item in materialized)
        {
            var result = candidate.Accept(item);
            if (!result.Accepted) throw new InvalidOperationException(result.ReasonKey);
        }

        _acceptedTransportEvents.AddRange(materialized);
        Presentation.AddEvents(materialized);
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        _session.Close();
    }

    private void EnsureOpen()
    {
        if (_closed) throw new ObjectDisposedException(nameof(RuntimeAdapter));
    }

    private static void ValidateSubmission(
        RuntimeGameAction action,
        RuntimeActionSubmission submission)
    {
        if (submission is null)
            throw new InvalidOperationException("The runtime session returned no action submission.");
        if (submission.Result is null || submission.Snapshot is null || submission.Events is null)
            throw new InvalidOperationException("The runtime session returned an incomplete action submission.");

        var result = submission.Result;
        var snapshot = submission.Snapshot;
        if (result.ContractVersion != ContractVersionGuard.ExpectedVersion ||
            snapshot.ContractVersion != ContractVersionGuard.ExpectedVersion)
            throw new InvalidOperationException("The runtime session returned an unsupported action contract.");
        if (!string.Equals(result.MatchId, action.MatchId, StringComparison.Ordinal) ||
            !string.Equals(snapshot.MatchId, action.MatchId, StringComparison.Ordinal))
            throw new InvalidOperationException("The runtime session returned a different match identity.");
        if (result.SnapshotRevision != action.SnapshotRevision)
            throw new InvalidOperationException("The runtime session returned a different input snapshot revision.");
        if (result.ResultingSnapshotRevision != snapshot.SnapshotRevision)
            throw new InvalidOperationException("The runtime session result revision does not match its snapshot.");
        if (result.ResultingSnapshotRevision < result.SnapshotRevision)
            throw new InvalidOperationException("The runtime session returned a stale resulting snapshot.");
        if (!string.Equals(result.ActionId, action.ActionId, StringComparison.Ordinal))
            throw new InvalidOperationException("The runtime session returned a different action identity.");
    }
}
}
