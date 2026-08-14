using System;
using System.Collections.Generic;
using DominionWars.Adapters;

namespace DominionWars.Unity.Runtime
{

/// <summary>
/// Renderer-neutral Unity-side state holder. It consumes snapshots/actions/
/// events and never reproduces engine rules or phase transitions.
/// </summary>
public sealed class RuntimeAdapter : IDisposable
{
    private readonly IRuntimeSession _session;
    private readonly RuntimeEventCursor _eventCursor = new RuntimeEventCursor();
    private bool _closed;

    public RuntimeAdapter(IRuntimeSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Presentation = new RuntimePresentationState();
    }

    public RuntimePresentationState Presentation { get; }

    public void AcceptSnapshot(RuntimeSnapshotEnvelope snapshot)
    {
        EnsureOpen();
        RuntimeSnapshotProjection.Validate(snapshot);
        if (Presentation.Snapshot is not null &&
            !string.Equals(Presentation.Snapshot.MatchId, snapshot.MatchId, StringComparison.Ordinal))
            throw new InvalidOperationException("Snapshot match identity changed.");
        if (Presentation.Snapshot is not null && snapshot.SnapshotRevision < Presentation.Snapshot.SnapshotRevision)
            throw new InvalidOperationException("A stale snapshot cannot replace the current presentation.");
        Presentation.Snapshot = snapshot;
    }

    public RuntimeActionSubmission Submit(RuntimeGameAction action)
    {
        EnsureOpen();
        if (Presentation.Snapshot is null) throw new InvalidOperationException("A snapshot is required before submitting actions.");
        var validation = RuntimeActionBoundary.Validate(action, Presentation.Snapshot);
        if (!validation.Accepted) throw new InvalidOperationException(validation.ReasonKey);
        var submission = _session.Submit(action);
        AcceptSnapshot(submission.Snapshot);
        return submission;
    }

    public void ApplyEvents(IEnumerable<RuntimeEventEnvelope> events)
    {
        EnsureOpen();
        if (events is null) throw new ArgumentNullException(nameof(events));
        foreach (var item in events)
        {
            var result = _eventCursor.Accept(item);
            if (!result.Accepted) throw new InvalidOperationException(result.ReasonKey);
            Presentation.AddEvents(new[] { item });
        }
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
}
}
