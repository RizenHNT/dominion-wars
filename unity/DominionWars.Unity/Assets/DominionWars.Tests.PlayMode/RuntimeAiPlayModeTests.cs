#nullable enable annotations

using System;
using System.Collections;
using System.Collections.Generic;
using DominionWars.Adapters;
using DominionWars.Unity.Runtime;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace DominionWars.Unity.PlayMode
{

[TestFixture]
public sealed class RuntimeAiPlayModeTests
{
    [UnityTest]
    public IEnumerator CoordinatorSubmitsAtMostOneActionPerFrame()
    {
        var session = new OneActionSession();
        var adapter = new RuntimeAdapter(session);
        adapter.AcceptSnapshot(session.GetSnapshot(0));
        var coordinator = new RuntimeAiTurnCoordinator(adapter, maxActionsPerTurn: 2);

        Assert.That(coordinator.Pump(), Is.True);
        Assert.That(session.SubmissionCount, Is.EqualTo(1));
        yield return null;

        Assert.That(session.SubmissionCount, Is.EqualTo(1));
        Assert.That(coordinator.Pump(), Is.False);
        Assert.That(session.SubmissionCount, Is.EqualTo(1));
        adapter.Dispose();
    }

    private sealed class OneActionSession : IRuntimeSession
    {
        private RuntimeSnapshotEnvelope _viewer0 = Snapshot(0, 0, 1, Array.Empty<RuntimeLegalAction>());
        private RuntimeSnapshotEnvelope _viewer1 = Snapshot(
            0,
            1,
            1,
            new[] { Legal("end_1", RuntimeAiPolicy.EndTurnAction, 0, 1) });

        public int SubmissionCount { get; private set; }

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
        {
            return viewerPlayerIndex == 0 ? _viewer0 : _viewer1;
        }

        public RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            SubmissionCount++;
            var actorSnapshot = Snapshot(1, 1, 0, Array.Empty<RuntimeLegalAction>());
            _viewer1 = actorSnapshot;
            _viewer0 = Snapshot(1, 0, 0, Array.Empty<RuntimeLegalAction>());
            return new RuntimeActionSubmission(
                new RuntimeActionResult
                {
                    MatchId = action.MatchId,
                    SnapshotRevision = action.SnapshotRevision,
                    ResultingSnapshotRevision = 1,
                    ActionId = action.ActionId,
                    Accepted = true,
                    ReasonKey = "action.accepted",
                },
                Array.Empty<DominionWars.Engine.Events.GameEvent>(),
                actorSnapshot);
        }

        public void Close() { }
    }

    private static RuntimeLegalAction Legal(string actionId, string type, long revision, int actor)
    {
        return new RuntimeLegalAction
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            SnapshotRevision = revision,
            ActionId = actionId,
            Type = type,
            Actor = actor,
            Payload = new Dictionary<string, object?>(),
        };
    }

    private static RuntimeSnapshotEnvelope Snapshot(
        long revision,
        int viewerPlayerIndex,
        int currentPlayer,
        IReadOnlyList<RuntimeLegalAction> legalActions)
    {
        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = "match_ai_playmode_test",
            SnapshotRevision = revision,
            Turn = 1,
            Phase = RuntimeAiPolicy.ActionPhase,
            CurrentPlayer = currentPlayer,
            ViewerPlayerId = "player_" + viewerPlayerIndex,
            Players = new[]
            {
                new RuntimePlayerSnapshot { PlayerId = "player_0" },
                new RuntimePlayerSnapshot { PlayerId = "player_1" },
            },
            Castle = new RuntimeCastleSnapshot(),
            LegalActions = legalActions,
        };
    }
}
}
