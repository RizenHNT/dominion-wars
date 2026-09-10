#nullable enable annotations

using System;
using System.Collections.Generic;
using DominionWars.Adapters;
using DominionWars.Unity.Runtime;
using NUnit.Framework;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeAiEditModeTests
{
    [Test]
    public void PolicyChoosesOnlyAdvertisedActionsAndCanForceEndTurn()
    {
        var snapshot = Snapshot(
            revision: 4,
            viewerPlayerIndex: 1,
            currentPlayer: 1,
            phase: RuntimeAiPolicy.ActionPhase,
            legalActions: new[]
            {
                Legal("end_1", RuntimeAiPolicy.EndTurnAction, 4, 1),
                Legal("play_2", "PLAY_CARD", 4, 1, sourceId: 22L),
            });
        var policy = new RuntimeAiPolicy();

        Assert.That(policy.TryChoose(snapshot, 1, false, out var action), Is.True);
        Assert.That(action.ActionId, Is.EqualTo("play_2"));
        Assert.That(policy.TryChoose(snapshot, 1, true, out var forced), Is.True);
        Assert.That(forced.ActionId, Is.EqualTo("end_1"));

        var wire = RuntimeAiPolicy.ToGameAction(snapshot, action);
        Assert.That(wire.MatchId, Is.EqualTo(snapshot.MatchId));
        Assert.That(wire.SnapshotRevision, Is.EqualTo(action.SnapshotRevision));
        Assert.That(wire.SourceId, Is.EqualTo(action.SourceId));
        Assert.That(wire.ActionId, Is.EqualTo(action.ActionId));
    }

    [Test]
    public void CpuSubmissionKeepsHumanPresentationViewerAndRedactsCpuHand()
    {
        var session = new CpuSession();
        var adapter = new RuntimeAdapter(session);
        adapter.AcceptSnapshot(session.GetSnapshot(0));
        var coordinator = new RuntimeAiTurnCoordinator(adapter, maxActionsPerTurn: 4);

        Assert.That(coordinator.Pump(), Is.True);
        Assert.That(session.LastSubmittedAction, Is.Not.Null);
        Assert.That(session.LastSubmittedAction.Actor, Is.EqualTo(1));
        Assert.That(adapter.Presentation.Snapshot.ViewerPlayerId, Is.EqualTo("player_0"));
        Assert.That(adapter.Presentation.Snapshot.SnapshotRevision, Is.EqualTo(1));
        Assert.That(adapter.Presentation.Snapshot.Players[1].Hand, Is.Empty);
        Assert.That(coordinator.Pump(), Is.False);
    }

    [Test]
    public void CoordinatorStopsWhenAiHasNoLegalActionInsteadOfLooping()
    {
        var session = new NoActionSession();
        var adapter = new RuntimeAdapter(session);
        adapter.AcceptSnapshot(session.GetSnapshot(0));
        var coordinator = new RuntimeAiTurnCoordinator(adapter, maxActionsPerTurn: 1);

        Assert.That(coordinator.Pump(), Is.False);
        Assert.That(coordinator.Halted, Is.True);
        Assert.That(coordinator.LastReasonKey, Is.EqualTo("ai.no_legal_actions"));
        Assert.That(coordinator.Pump(), Is.False);
    }

    private static RuntimeLegalAction Legal(
        string actionId,
        string type,
        long revision,
        int actor,
        object? sourceId = null)
    {
        return new RuntimeLegalAction
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            SnapshotRevision = revision,
            ActionId = actionId,
            Type = type,
            Actor = actor,
            SourceId = sourceId,
            Payload = new Dictionary<string, object?>(),
        };
    }

    private static RuntimeSnapshotEnvelope Snapshot(
        long revision,
        int viewerPlayerIndex,
        int currentPlayer,
        string phase,
        IReadOnlyList<RuntimeLegalAction> legalActions)
    {
        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = "match_ai_test",
            SnapshotRevision = revision,
            Turn = 1,
            Phase = phase,
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

    private sealed class CpuSession : IRuntimeSession
    {
        private RuntimeSnapshotEnvelope _viewer0;
        private RuntimeSnapshotEnvelope _viewer1;

        public CpuSession()
        {
            var legal = Legal("end_1", RuntimeAiPolicy.EndTurnAction, 0, 1);
            _viewer0 = Snapshot(0, 0, 1, RuntimeAiPolicy.ActionPhase, Array.Empty<RuntimeLegalAction>());
            _viewer1 = Snapshot(0, 1, 1, RuntimeAiPolicy.ActionPhase, new[] { legal });
            _viewer1.Players[1].Hand = new[]
            {
                new RuntimeCardSnapshot { EntityId = 99, CardId = "visible_to_cpu" },
            };
        }

        public RuntimeGameAction LastSubmittedAction { get; private set; }

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
        {
            return viewerPlayerIndex == 0 ? _viewer0 : _viewer1;
        }

        public RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            LastSubmittedAction = action;
            var resultSnapshot = Snapshot(1, 1, 0, "START", Array.Empty<RuntimeLegalAction>());
            var presentationSnapshot = Snapshot(1, 0, 0, "START", Array.Empty<RuntimeLegalAction>());
            _viewer1 = resultSnapshot;
            _viewer0 = presentationSnapshot;
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
                resultSnapshot);
        }

        public void Close() { }
    }

    private sealed class NoActionSession : IRuntimeSession
    {
        private readonly RuntimeSnapshotEnvelope _viewer0 =
            Snapshot(0, 0, 1, RuntimeAiPolicy.ActionPhase, Array.Empty<RuntimeLegalAction>());
        private readonly RuntimeSnapshotEnvelope _viewer1 =
            Snapshot(0, 1, 1, RuntimeAiPolicy.ActionPhase, Array.Empty<RuntimeLegalAction>());

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
        {
            return viewerPlayerIndex == 0 ? _viewer0 : _viewer1;
        }

        public RuntimeActionSubmission Submit(RuntimeGameAction action) =>
            throw new InvalidOperationException("No action should be submitted.");

        public void Close() { }
    }
}
}
