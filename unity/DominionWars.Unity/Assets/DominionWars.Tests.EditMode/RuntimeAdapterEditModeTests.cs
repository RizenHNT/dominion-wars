#nullable enable annotations

using System.Collections.Generic;
using DominionWars.Adapters;
using DominionWars.Engine.Events;
using DominionWars.Unity.Runtime;
using NUnit.Framework;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeAdapterEditModeTests
{
    [Test]
    public void SnapshotReplacementRejectsStaleRevision()
    {
        var session = new FakeSession();
        var adapter = new RuntimeAdapter(session);
        adapter.AcceptSnapshot(Snapshot(2));

        Assert.That(() => adapter.AcceptSnapshot(Snapshot(1)), Throws.InvalidOperationException);
    }

    [Test]
    public void EventCursorRejectsDuplicateTransportEvent()
    {
        var adapter = new RuntimeAdapter(new FakeSession());
        adapter.AcceptSnapshot(Snapshot(0));
        var eventItem = TransportEvent(1);
        adapter.ApplyEvents(new[] { eventItem });

        Assert.That(() => adapter.ApplyEvents(new[] { eventItem }), Throws.InvalidOperationException);
    }

    [Test]
    public void ApplyEventsRequiresSnapshotBeforeAcceptingTransportData()
    {
        var adapter = new RuntimeAdapter(new FakeSession());

        Assert.That(() => adapter.ApplyEvents(new[] { TransportEvent(1) }),
            Throws.InvalidOperationException.With.Message.EqualTo("A snapshot is required before accepting events."));
        Assert.That(adapter.Presentation.Events, Is.Empty);
        Assert.That(adapter.Presentation.EventDelta, Is.Empty);
    }

    [Test]
    public void ApplyEventsBatchFailureDoesNotAdvanceCursorOrPresentation()
    {
        var adapter = new RuntimeAdapter(new FakeSession());
        adapter.AcceptSnapshot(Snapshot(0));

        Assert.That(
            () => adapter.ApplyEvents(new[] { TransportEvent(1), TransportEvent(3) }),
            Throws.InvalidOperationException.With.Message.EqualTo("event.gap"));
        Assert.That(adapter.Presentation.Events, Is.Empty);
        Assert.That(adapter.Presentation.EventDelta, Is.Empty);

        // The valid prefix can be retried because the failed batch never
        // mutated the live cursor.
        adapter.ApplyEvents(new[] { TransportEvent(1) });
        Assert.That(adapter.Presentation.Events, Has.Count.EqualTo(1));
        Assert.That(adapter.Presentation.EventDelta, Has.Count.EqualTo(1));
    }

    [Test]
    public void RefreshSnapshotRequestsAndPublishesTheSelectedViewer()
    {
        var viewer0 = Snapshot(3, 0);
        var viewer1 = Snapshot(3, 1);
        var session = new ViewerSession(viewer0, viewer1);
        var adapter = new RuntimeAdapter(session);
        adapter.AcceptSnapshot(viewer0);
        adapter.ApplyEvents(new[]
        {
            new RuntimeEventEnvelope
            {
                EventId = "evt_000000000001",
                Type = "PHASE_CHANGED",
                Turn = 1,
                Phase = "AMBUSH",
                SnapshotRevision = 3,
                TargetIds = System.Array.Empty<object?>(),
                Data = new Dictionary<string, object?>(),
            },
        });

        var refreshed = adapter.RefreshSnapshot(1);

        Assert.That(session.LastRequestedViewerPlayerIndex, Is.EqualTo(1));
        Assert.That(refreshed, Is.SameAs(viewer1));
        Assert.That(adapter.Presentation.Snapshot, Is.SameAs(viewer1));
        Assert.That(adapter.Presentation.Snapshot!.ViewerPlayerId, Is.EqualTo("player_1"));
        Assert.That(adapter.Presentation.Events, Is.Empty);
        Assert.That(adapter.Presentation.EventDelta, Is.Empty);
    }

    [Test]
    public void RefreshSnapshotRejectsWrongViewerWithoutPollutingPresentation()
    {
        var initial = Snapshot(0, 0);
        var wrongViewer = Snapshot(1, 0);
        var adapter = new RuntimeAdapter(new ViewerSession(initial, wrongViewer));
        adapter.AcceptSnapshot(initial);
        adapter.ApplyEvents(new[] { TransportEvent(1) });
        var priorSnapshot = adapter.Presentation.Snapshot;
        var priorEvent = adapter.Presentation.Events[0];

        Assert.That(() => adapter.RefreshSnapshot(1),
            Throws.InvalidOperationException.With.Message.EqualTo(
                "Snapshot viewer identity does not match the requested player."));
        Assert.That(adapter.Presentation.Snapshot, Is.SameAs(priorSnapshot));
        Assert.That(adapter.Presentation.Events, Has.Count.EqualTo(1));
        Assert.That(adapter.Presentation.Events[0], Is.SameAs(priorEvent));
        Assert.That(adapter.Presentation.EventDelta, Has.Count.EqualTo(1));
        Assert.That(adapter.Presentation.EventDelta[0], Is.SameAs(priorEvent));
    }

    [Test]
    public void RefreshSnapshotRejectsInvalidViewerBeforeCallingSession()
    {
        var session = new ViewerSession(Snapshot(0, 0), Snapshot(0, 1));
        var adapter = new RuntimeAdapter(session);

        Assert.That(() => adapter.RefreshSnapshot(2), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        Assert.That(session.LastRequestedViewerPlayerIndex, Is.EqualTo(-1));
    }

    [Test]
    public void SubmitPublishesSnapshotAndEventDeltaToPresentation()
    {
        var initial = Snapshot(0);
        initial.LegalActions = new[]
        {
            new RuntimeLegalAction
            {
                ActionId = "action_skip_ambush",
                Type = "SKIP_AMBUSH",
                Actor = 0,
                SnapshotRevision = 0,
            },
        };
        var resulting = Snapshot(1);
        var actionResult = new RuntimeActionResult
        {
            MatchId = initial.MatchId,
            SnapshotRevision = 0,
            ResultingSnapshotRevision = 1,
            ActionId = "action_skip_ambush",
            Accepted = true,
            ReasonKey = "action.accepted",
        };
        var eventDelta = new[]
        {
            new GameEvent(1, null, "PHASE_CHANGED"),
            new GameEvent(2, 1, "INTERNAL_BOOKKEEPING"),
        };
        var fake = new FakeSession(
            initial,
            new RuntimeActionSubmission(actionResult, eventDelta, resulting));
        var adapter = new RuntimeAdapter(fake);
        adapter.AcceptSnapshot(initial);

        var submission = adapter.Submit(new RuntimeGameAction
        {
            MatchId = initial.MatchId,
            SnapshotRevision = 0,
            ActionId = "action_skip_ambush",
            Type = "SKIP_AMBUSH",
            Actor = 0,
        });

        Assert.That(submission.Result.Accepted, Is.True);
        Assert.That(adapter.Presentation.Snapshot, Is.SameAs(resulting));
        Assert.That(adapter.Presentation.EventDelta, Has.Count.EqualTo(1));
        Assert.That(adapter.Presentation.EventDelta[0].Type, Is.EqualTo("PHASE_CHANGED"));
        Assert.That(adapter.Presentation.EventDelta[0].SnapshotRevision, Is.EqualTo(1));
        Assert.That(adapter.Presentation.Events, Has.Count.EqualTo(1));
    }

    [Test]
    public void DirectSnapshotRefreshClearsPreviousEventDeltaButKeepsHistory()
    {
        var initial = Snapshot(0);
        initial.LegalActions = new[]
        {
            new RuntimeLegalAction
            {
                ActionId = "action_skip_ambush",
                Type = "SKIP_AMBUSH",
                Actor = 0,
                SnapshotRevision = 0,
            },
        };
        var resulting = Snapshot(1);
        var submission = new RuntimeActionSubmission(
            new RuntimeActionResult
            {
                MatchId = initial.MatchId,
                SnapshotRevision = 0,
                ResultingSnapshotRevision = 1,
                ActionId = "action_skip_ambush",
                Accepted = true,
                ReasonKey = "action.accepted",
            },
            new[] { new GameEvent(1, null, "PHASE_CHANGED") },
            resulting);
        var adapter = new RuntimeAdapter(new FakeSession(initial, submission));
        adapter.AcceptSnapshot(initial);
        adapter.Submit(new RuntimeGameAction
        {
            MatchId = initial.MatchId,
            SnapshotRevision = 0,
            ActionId = "action_skip_ambush",
            Type = "SKIP_AMBUSH",
            Actor = 0,
        });

        adapter.AcceptSnapshot(Snapshot(2));

        Assert.That(adapter.Presentation.EventDelta, Is.Empty);
        Assert.That(adapter.Presentation.Events, Has.Count.EqualTo(1));
    }

    [Test]
    public void DirectSnapshotViewerChangeClearsHistoryAndDelta()
    {
        var viewer0 = Snapshot(3, 0);
        var viewer1 = Snapshot(3, 1);
        var adapter = new RuntimeAdapter(new ViewerSession(viewer0, viewer1));
        adapter.AcceptSnapshot(viewer0);
        adapter.ApplyEvents(new[] { TransportEvent(1) });

        adapter.AcceptSnapshot(viewer1);

        Assert.That(adapter.Presentation.Snapshot, Is.SameAs(viewer1));
        Assert.That(adapter.Presentation.Events, Is.Empty);
        Assert.That(adapter.Presentation.EventDelta, Is.Empty);
    }

    [Test]
    public void MalformedSubmissionCannotPollutePresentation()
    {
        var initial = Snapshot(0);
        var action = AdvertisedSkipAction(initial);
        initial.LegalActions = new[] { action };
        var resulting = Snapshot(1);
        var malformedResult = new RuntimeActionResult
        {
            MatchId = "match_other",
            SnapshotRevision = initial.SnapshotRevision,
            ResultingSnapshotRevision = resulting.SnapshotRevision,
            ActionId = action.ActionId,
            Accepted = true,
            ReasonKey = "action.accepted",
        };
        var adapter = new RuntimeAdapter(new FakeSession(
            initial,
            new RuntimeActionSubmission(
                malformedResult,
                new[] { new GameEvent(1, null, "PHASE_CHANGED") },
                resulting)));
        adapter.AcceptSnapshot(initial);

        Assert.That(() => adapter.Submit(ToAction(action, initial.MatchId)),
            Throws.InvalidOperationException.With.Message.EqualTo("The runtime session returned a different match identity."));
        Assert.That(adapter.Presentation.Snapshot, Is.SameAs(initial));
        Assert.That(adapter.Presentation.Events, Is.Empty);
        Assert.That(adapter.Presentation.EventDelta, Is.Empty);
    }

    [Test]
    public void SubmissionResultRevisionAndActionIdentityMustMatchInput()
    {
        var initial = Snapshot(0);
        var action = AdvertisedSkipAction(initial);
        initial.LegalActions = new[] { action };
        var resulting = Snapshot(1);

        var badRevision = new RuntimeActionResult
        {
            MatchId = initial.MatchId,
            SnapshotRevision = initial.SnapshotRevision + 1,
            ResultingSnapshotRevision = resulting.SnapshotRevision,
            ActionId = action.ActionId,
            Accepted = true,
            ReasonKey = "action.accepted",
        };
        var revisionAdapter = new RuntimeAdapter(new FakeSession(
            initial,
            new RuntimeActionSubmission(badRevision, System.Array.Empty<GameEvent>(), resulting)));
        revisionAdapter.AcceptSnapshot(initial);
        Assert.That(() => revisionAdapter.Submit(ToAction(action, initial.MatchId)),
            Throws.InvalidOperationException.With.Message.EqualTo("The runtime session returned a different input snapshot revision."));
        Assert.That(revisionAdapter.Presentation.Snapshot, Is.SameAs(initial));

        var badActionId = new RuntimeActionResult
        {
            MatchId = initial.MatchId,
            SnapshotRevision = initial.SnapshotRevision,
            ResultingSnapshotRevision = resulting.SnapshotRevision,
            ActionId = "different_action",
            Accepted = true,
            ReasonKey = "action.accepted",
        };
        var actionAdapter = new RuntimeAdapter(new FakeSession(
            initial,
            new RuntimeActionSubmission(badActionId, System.Array.Empty<GameEvent>(), resulting)));
        actionAdapter.AcceptSnapshot(initial);
        Assert.That(() => actionAdapter.Submit(ToAction(action, initial.MatchId)),
            Throws.InvalidOperationException.With.Message.EqualTo("The runtime session returned a different action identity."));
        Assert.That(actionAdapter.Presentation.Snapshot, Is.SameAs(initial));
    }

    private static RuntimeSnapshotEnvelope Snapshot(long revision, int viewerPlayerIndex = 0)
    {
        return new RuntimeSnapshotEnvelope
        {
            MatchId = "match_unity",
            SnapshotRevision = revision,
            Turn = 1,
            Phase = "START",
            CurrentPlayer = 0,
            ViewerPlayerId = "player_" + viewerPlayerIndex,
            Players = new[] { new RuntimePlayerSnapshot(), new RuntimePlayerSnapshot() },
            LegalActions = new RuntimeLegalAction[0],
            Castle = new RuntimeCastleSnapshot(),
        };
    }

    private static RuntimeEventEnvelope TransportEvent(long number)
    {
        return new RuntimeEventEnvelope
        {
            EventId = "evt_" + number.ToString("D12"),
            Type = "PHASE_CHANGED",
            Phase = "START",
            Turn = 1,
            SnapshotRevision = 0,
            TargetIds = System.Array.Empty<object?>(),
            Data = new Dictionary<string, object?>(),
        };
    }

    private static RuntimeLegalAction AdvertisedSkipAction(RuntimeSnapshotEnvelope snapshot)
    {
        return new RuntimeLegalAction
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            SnapshotRevision = snapshot.SnapshotRevision,
            ActionId = "action_skip_ambush",
            Type = "SKIP_AMBUSH",
            Actor = 0,
            Payload = new Dictionary<string, object?>(),
        };
    }

    private static RuntimeGameAction ToAction(RuntimeLegalAction legal, string matchId)
    {
        return new RuntimeGameAction
        {
            ContractVersion = legal.ContractVersion,
            MatchId = matchId,
            SnapshotRevision = legal.SnapshotRevision,
            ActionId = legal.ActionId,
            Type = legal.Type,
            Actor = legal.Actor,
            Payload = legal.Payload,
        };
    }

    private sealed class ViewerSession : IRuntimeSession
    {
        private readonly RuntimeSnapshotEnvelope[] _snapshots;

        public ViewerSession(RuntimeSnapshotEnvelope viewer0, RuntimeSnapshotEnvelope viewer1)
        {
            _snapshots = new[] { viewer0, viewer1 };
        }

        public int LastRequestedViewerPlayerIndex { get; private set; } = -1;

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
        {
            LastRequestedViewerPlayerIndex = viewerPlayerIndex;
            return _snapshots[viewerPlayerIndex];
        }

        public RuntimeActionSubmission Submit(RuntimeGameAction action) =>
            throw new System.NotSupportedException();

        public void Close() { }
    }

    private sealed class FakeSession : IRuntimeSession
    {
        private readonly RuntimeSnapshotEnvelope _snapshot;
        private readonly RuntimeActionSubmission? _submission;

        public FakeSession(RuntimeSnapshotEnvelope? snapshot = null, RuntimeActionSubmission? submission = null)
        {
            _snapshot = snapshot ?? Snapshot(0);
            _submission = submission;
        }

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex) => _snapshot;
        public RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            return _submission ?? throw new System.NotSupportedException();
        }
        public void Close() { }
    }
}
}
