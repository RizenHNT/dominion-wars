using System.Collections.Generic;
using DominionWars.Adapters;
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
        var eventItem = new RuntimeEventEnvelope
        {
            EventId = "evt_000000000001", Type = "PHASE_CHANGED", Phase = "START",
            Turn = 1, SnapshotRevision = 0, TargetIds = new[] { "castle" },
            Data = new Dictionary<string, object?>(),
        };
        adapter.ApplyEvents(new[] { eventItem });

        Assert.That(() => adapter.ApplyEvents(new[] { eventItem }), Throws.InvalidOperationException);
    }

    private static RuntimeSnapshotEnvelope Snapshot(long revision)
    {
        return new RuntimeSnapshotEnvelope
        {
            MatchId = "match_unity",
            SnapshotRevision = revision,
            Turn = 1,
            Phase = "START",
            CurrentPlayer = 0,
            ViewerPlayerId = "player_0",
            Players = new[] { new RuntimePlayerSnapshot(), new RuntimePlayerSnapshot() },
            LegalActions = new RuntimeLegalAction[0],
            Castle = new RuntimeCastleSnapshot(),
        };
    }

    private sealed class FakeSession : IRuntimeSession
    {
        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex) => Snapshot(0);
        public RuntimeActionSubmission Submit(RuntimeGameAction action) => throw new System.NotSupportedException();
        public void Close() { }
    }
}
}
