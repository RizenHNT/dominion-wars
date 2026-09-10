#if UNITY_INCLUDE_TESTS
#nullable enable annotations

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Engine.Events;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DominionWars.Unity.PlayMode
{

[TestFixture]
public sealed class RuntimeAiIntegrationPlayModeTests
{
    private const string RuntimeBootstrapScene = "RuntimeBootstrap";

    [UnityTest]
    public IEnumerator CpuSetupKeepsViewer0AndRunsAnAdvertisedAiTurn()
    {
        yield return LoadRuntimeBootstrapScene();

        var flow = UnityEngine.Object.FindFirstObjectByType<RuntimeScreenFlow>();
        var bootstrap = UnityEngine.Object.FindFirstObjectByType<RuntimeBootstrap>();
        var panel = UnityEngine.Object.FindObjectsByType<RuntimeBattlePanel>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None).FirstOrDefault();
        Assert.That(flow, Is.Not.Null);
        Assert.That(bootstrap, Is.Not.Null);
        Assert.That(panel, Is.Not.Null);
        Assert.That(flow!.View.MatchSetupCpuToggle, Is.Not.Null);

        flow.Navigate(RuntimeScreenId.MainMenu);
        flow.View.MainMenuMatchSetupButton.onClick.Invoke();
        flow.View.MatchSetupCpuToggle.isOn = true;
        Assert.That(flow.SelectedCpuOpponent, Is.True);

        flow.View.MatchSetupStartButton.onClick.Invoke();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(bootstrap!.CpuOpponent, Is.True);
        Assert.That(panel!.FollowCurrentPlayer, Is.False);
        Assert.That(panel.ViewerPlayerIndex, Is.EqualTo(0));
        Assert.That(bootstrap.Adapter, Is.Not.Null);
        Assert.That(bootstrap.Adapter!.Presentation.Snapshot!.CurrentPlayer, Is.EqualTo(0),
            "The real setup path must start the human on player 0 before the CPU handoff.");

        // Complete player 0's turn using only complete actions advertised by
        // the authoritative snapshot. No phase/card rule is reproduced here.
        var humanRevision = CompleteAdvertisedHumanTurn(bootstrap.Adapter!);
        var humanSnapshot = bootstrap.Adapter.Presentation.Snapshot!;
        Assert.That(humanSnapshot.CurrentPlayer, Is.EqualTo(1));
        Assert.That(humanSnapshot.ViewerPlayerId, Is.EqualTo("player_0"));

        var sawAiProgress = false;
        var returnedToHumanOrTerminal = false;
        for (var frame = 0; frame < 16; frame++)
        {
            yield return null;
            flow.Refresh();

            var snapshot = bootstrap.Adapter.Presentation.Snapshot!;
            Assert.That(snapshot.ViewerPlayerId, Is.EqualTo("player_0"));
            Assert.That(panel.ViewerPlayerIndex, Is.EqualTo(0));
            Assert.That(panel.FollowCurrentPlayer, Is.False);
            if (snapshot.SnapshotRevision > humanRevision)
                sawAiProgress = true;

            if (sawAiProgress &&
                (snapshot.CurrentPlayer == 0 || string.Equals(snapshot.Phase, "OVER", StringComparison.Ordinal)))
            {
                returnedToHumanOrTerminal = true;
                break;
            }
        }

        Assert.That(sawAiProgress, Is.True,
            "The real screen-flow CPU path must submit at least one AI action.");
        Assert.That(returnedToHumanOrTerminal, Is.True,
            "The bounded CPU path must hand back to player 0 or stop at a terminal snapshot.");
    }

    [UnityTest]
    public IEnumerator TerminalSubmissionStopsCoordinator()
    {
        var session = new TerminalSession();
        var adapter = new RuntimeAdapter(session);
        adapter.AcceptSnapshot(session.GetSnapshot(0));
        var coordinator = new RuntimeAiTurnCoordinator(adapter, maxActionsPerTurn: 32);

        Assert.That(coordinator.Pump(), Is.True);
        Assert.That(session.SubmissionCount, Is.EqualTo(1));
        Assert.That(adapter.Presentation.Snapshot!.Phase, Is.EqualTo("OVER"));
        Assert.That(coordinator.Pump(), Is.False);
        Assert.That(coordinator.Halted, Is.True);
        Assert.That(coordinator.LastReasonKey, Is.EqualTo("ai.match_over"));
        yield return null;
    }

    [UnityTest]
    public IEnumerator RejectedSubmissionStopsCoordinatorWithoutRetryLoop()
    {
        var session = new RejectedSession();
        var adapter = new RuntimeAdapter(session);
        adapter.AcceptSnapshot(session.GetSnapshot(0));
        var coordinator = new RuntimeAiTurnCoordinator(adapter, maxActionsPerTurn: 32);

        Assert.That(coordinator.Pump(), Is.False);
        Assert.That(session.SubmissionCount, Is.EqualTo(1));
        Assert.That(coordinator.Halted, Is.True);
        Assert.That(coordinator.LastReasonKey, Is.EqualTo("action.rejected"));
        Assert.That(coordinator.Pump(), Is.False);
        Assert.That(session.SubmissionCount, Is.EqualTo(1));
        yield return null;
    }

    [UnityTest]
    public IEnumerator CoordinatorStopsAtConfiguredThirtyTwoActionLimit()
    {
        var session = new EndlessAmbushSession();
        var adapter = new RuntimeAdapter(session);
        adapter.AcceptSnapshot(session.GetSnapshot(0));
        var coordinator = new RuntimeAiTurnCoordinator(adapter, maxActionsPerTurn: 32);

        for (var action = 0; action < 32; action++)
        {
            Assert.That(coordinator.Pump(), Is.True, "CPU action " + action + " should be accepted.");
            yield return null;
        }

        Assert.That(session.SubmissionCount, Is.EqualTo(32));
        Assert.That(coordinator.ActionsTakenThisTurn, Is.EqualTo(32));
        Assert.That(coordinator.Pump(), Is.False);
        Assert.That(coordinator.Halted, Is.True);
        Assert.That(coordinator.LastReasonKey, Is.EqualTo("ai.action_limit_reached"));
        Assert.That(session.SubmissionCount, Is.EqualTo(32));
    }

    private static long CompleteAdvertisedHumanTurn(RuntimeAdapter adapter)
    {
        for (var step = 0; step < 8; step++)
        {
            var snapshot = adapter.Presentation.Snapshot!;
            if (snapshot.CurrentPlayer != 0)
                return snapshot.SnapshotRevision;
            Assert.That(snapshot.LegalActions, Is.Not.Null.And.Not.Empty,
                "The active human viewer must receive an advertised action before handoff.");

            var legal = ChooseHandoffAction(snapshot.LegalActions);
            adapter.Submit(ToGameAction(legal, snapshot.MatchId));
        }

        Assert.Fail("The advertised player-0 actions did not hand off within the bounded test loop.");
        return -1;
    }

    private static RuntimeLegalAction ChooseHandoffAction(
        IReadOnlyList<RuntimeLegalAction> legalActions)
    {
        // Prefer neutral phase-completion actions when they are advertised;
        // otherwise preserve the first complete action supplied by the engine.
        return legalActions.FirstOrDefault(action =>
                   string.Equals(action.Type, "SKIP_AMBUSH", StringComparison.Ordinal) ||
                   string.Equals(action.Type, "END_TURN", StringComparison.Ordinal)) ??
               legalActions[0];
    }

    private static RuntimeGameAction ToGameAction(RuntimeLegalAction legal, string matchId)
    {
        return new RuntimeGameAction
        {
            ContractVersion = legal.ContractVersion,
            MatchId = matchId,
            SnapshotRevision = legal.SnapshotRevision,
            ActionId = legal.ActionId,
            Type = legal.Type,
            Actor = legal.Actor,
            SourceId = legal.SourceId,
            TargetId = legal.TargetId,
            CardId = legal.CardId,
            Payload = legal.Payload,
        };
    }

    private static IEnumerator LoadRuntimeBootstrapScene()
    {
        var load = SceneManager.LoadSceneAsync(RuntimeBootstrapScene, LoadSceneMode.Single);
        Assert.That(load, Is.Not.Null);
        yield return load;
        yield return null;
    }

    private static RuntimeLegalAction EndAction(long revision)
    {
        return new RuntimeLegalAction
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            SnapshotRevision = revision,
            ActionId = "end_1_" + revision,
            Type = RuntimeAiPolicy.EndTurnAction,
            Actor = 1,
            Payload = new Dictionary<string, object?>(),
        };
    }

    private static RuntimeLegalAction SkipAction(long revision)
    {
        return new RuntimeLegalAction
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            SnapshotRevision = revision,
            ActionId = "skip_ambush_1_" + revision,
            Type = RuntimeAiPolicy.SkipAmbushAction,
            Actor = 1,
            Payload = new Dictionary<string, object?>(),
        };
    }

    private static RuntimeSnapshotEnvelope Snapshot(
        string matchId,
        long revision,
        int viewerPlayerIndex,
        int currentPlayer,
        string phase,
        IReadOnlyList<RuntimeLegalAction> legalActions,
        int? winnerPlayerIndex = null,
        string? reasonKey = null)
    {
        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = matchId,
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
            WinnerPlayerIndex = winnerPlayerIndex,
            ReasonKey = reasonKey,
        };
    }

    private abstract class FakeSessionBase : IRuntimeSession
    {
        protected RuntimeSnapshotEnvelope Viewer0 = null!;
        protected RuntimeSnapshotEnvelope Viewer1 = null!;
        public int SubmissionCount { get; protected set; }

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
        {
            return viewerPlayerIndex == 0 ? Viewer0 : Viewer1;
        }

        public abstract RuntimeActionSubmission Submit(RuntimeGameAction action);
        public void Close() { }
    }

    private sealed class TerminalSession : FakeSessionBase
    {
        public TerminalSession()
        {
            Viewer0 = Snapshot("match_terminal_test", 0, 0, 1, "ACTION", Array.Empty<RuntimeLegalAction>());
            Viewer1 = Snapshot("match_terminal_test", 0, 1, 1, "ACTION", new[] { EndAction(0) });
        }

        public override RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            SubmissionCount++;
            var actor = Snapshot("match_terminal_test", 1, 1, 1, "OVER", Array.Empty<RuntimeLegalAction>(), 0, "victory.castle");
            var presentation = Snapshot("match_terminal_test", 1, 0, 1, "OVER", Array.Empty<RuntimeLegalAction>(), 0, "victory.castle");
            Viewer1 = actor;
            Viewer0 = presentation;
            return Accepted(action, actor);
        }
    }

    private sealed class RejectedSession : FakeSessionBase
    {
        public RejectedSession()
        {
            Viewer0 = Snapshot("match_rejected_test", 0, 0, 1, "ACTION", Array.Empty<RuntimeLegalAction>());
            Viewer1 = Snapshot("match_rejected_test", 0, 1, 1, "ACTION", new[] { EndAction(0) });
        }

        public override RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            SubmissionCount++;
            return new RuntimeActionSubmission(
                new RuntimeActionResult
                {
                    MatchId = action.MatchId,
                    SnapshotRevision = 0,
                    ResultingSnapshotRevision = 0,
                    ActionId = action.ActionId,
                    Accepted = false,
                    ReasonKey = "action.rejected",
                },
                Array.Empty<GameEvent>(),
                Viewer1);
        }
    }

    private sealed class EndlessAmbushSession : FakeSessionBase
    {
        public EndlessAmbushSession()
        {
            Viewer0 = Snapshot("match_limit_test", 0, 0, 1, "AMBUSH", Array.Empty<RuntimeLegalAction>());
            Viewer1 = Snapshot("match_limit_test", 0, 1, 1, "AMBUSH", new[] { SkipAction(0) });
        }

        public override RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            SubmissionCount++;
            var revision = SubmissionCount;
            var actor = Snapshot("match_limit_test", revision, 1, 1, "AMBUSH", new[] { SkipAction(revision) });
            var presentation = Snapshot("match_limit_test", revision, 0, 1, "AMBUSH", Array.Empty<RuntimeLegalAction>());
            Viewer1 = actor;
            Viewer0 = presentation;
            return Accepted(action, actor);
        }
    }

    private static RuntimeActionSubmission Accepted(
        RuntimeGameAction action,
        RuntimeSnapshotEnvelope actorSnapshot)
    {
        return new RuntimeActionSubmission(
            new RuntimeActionResult
            {
                MatchId = action.MatchId,
                SnapshotRevision = action.SnapshotRevision,
                ResultingSnapshotRevision = actorSnapshot.SnapshotRevision,
                ActionId = action.ActionId,
                Accepted = true,
                ReasonKey = "action.accepted",
            },
            Array.Empty<GameEvent>(),
            actorSnapshot);
    }
}
}
#endif
