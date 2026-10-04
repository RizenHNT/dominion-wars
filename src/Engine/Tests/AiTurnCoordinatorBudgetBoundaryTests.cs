using System;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Adapters.Ai;
using DominionWars.Engine.Events;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// Small coordinator-only regression fixtures for the action budget boundary.
/// They deliberately use advertised snapshots rather than a long match so the
/// test proves the exact TryChoose -> ToGameAction -> submit path at the phase
/// handoff that previously stalled a real match.
/// </summary>
[TestFixture]
public sealed class AiTurnCoordinatorBudgetBoundaryTests
{
    [TestCase("DISCARD", "DISCARD", true)]
    [TestCase("AMBUSH", "SKIP_AMBUSH", true)]
    // The former ACTION_PROGRESS fixture advertised PLAY_CARD plus END_TURN
    // while expecting a halt. That expectation described the old defect, not
    // the approved budget contract. Keep the no-END case explicit, and cover
    // the new close-at-budget case separately.
    [TestCase("ACTION_NO_END", "PLAY_CARD", false)]
    [TestCase("ACTION_PROGRESS_WITH_END", "END_TURN", true)]
    [TestCase("ACTION_END", "END_TURN", true)]
    public void BudgetBoundaryUsesTheAdvertisedPhaseContract(
        string boundary,
        string expectedActionType,
        bool shouldSubmitAfterBudget)
    {
        var host = new BoundaryHost(boundary);
        var coordinator = new AiTurnCoordinator(host, maxActionsPerTurn: 2);

        Assert.That(coordinator.Pump(), Is.True, boundary + " first action");
        Assert.That(coordinator.Pump(), Is.True, boundary + " second action");

        var thirdPump = coordinator.Pump();

        Assert.Multiple(() =>
        {
            Assert.That(thirdPump, Is.EqualTo(shouldSubmitAfterBudget), boundary);
            Assert.That(host.Submissions.Count, Is.EqualTo(shouldSubmitAfterBudget ? 3 : 2), boundary);
            Assert.That(host.InvalidSubmissions, Is.Empty, boundary);

            if (shouldSubmitAfterBudget)
            {
                Assert.That(coordinator.Halted, Is.False, boundary);
                Assert.That(coordinator.LastReasonKey, Is.EqualTo("ai.action_accepted"), boundary);
                Assert.That(host.Submissions[^1].Type, Is.EqualTo(expectedActionType), boundary);
            }
            else
            {
                Assert.That(coordinator.Halted, Is.True, boundary);
                Assert.That(coordinator.LastReasonKey, Is.EqualTo("ai.action_limit_reached"), boundary);
                Assert.That(host.Submissions[^1].Type, Is.EqualTo("PLAY_CARD"), boundary);
            }
        });

        TestContext.Out.WriteLine(
            "AI budget boundary: case={0} thirdPump={1} submissions={2} halted={3} reason={4} lastType={5}",
            boundary,
            thirdPump,
            host.Submissions.Count,
            coordinator.Halted,
            coordinator.LastReasonKey,
            host.Submissions[^1].Type);
    }

    [TestCase("ACTION_WRONG_ACTOR")]
    [TestCase("ACTION_STALE_END")]
    public void BudgetDoesNotUseInvalidAdvertisedEndTurn(string boundary)
    {
        var host = new BoundaryHost(boundary);
        var coordinator = new AiTurnCoordinator(host, maxActionsPerTurn: 2);

        Assert.That(coordinator.Pump(), Is.True, boundary + " first action");
        Assert.That(coordinator.Pump(), Is.True, boundary + " second action");

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.Pump(), Is.False, boundary);
            Assert.That(host.Submissions.Count, Is.EqualTo(2), boundary);
            Assert.That(host.InvalidSubmissions, Is.Empty, boundary);
            Assert.That(coordinator.Halted, Is.True, boundary);
            Assert.That(coordinator.LastReasonKey, Is.EqualTo("ai.action_limit_reached"), boundary);
        });

        TestContext.Out.WriteLine(
            "AI budget invalid END_TURN guard: case={0} submissions={1} halted={2} reason={3}",
            boundary,
            host.Submissions.Count,
            coordinator.Halted,
            coordinator.LastReasonKey);
    }

    [Test]
    public void TerminalSnapshotWinsOverBudgetEscape()
    {
        var host = new BoundaryHost("ACTION_TERMINAL");
        var coordinator = new AiTurnCoordinator(host, maxActionsPerTurn: 2);

        Assert.That(coordinator.Pump(), Is.True);
        Assert.That(coordinator.Pump(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.Pump(), Is.False);
            Assert.That(host.Submissions.Count, Is.EqualTo(2));
            Assert.That(coordinator.Halted, Is.True);
            Assert.That(coordinator.LastReasonKey, Is.EqualTo("ai.match_over"));
        });
    }

    private sealed class BoundaryHost : IAiTurnHost
    {
        private readonly IReadOnlyList<RuntimeSnapshotEnvelope> _snapshots;
        private int _index;

        public BoundaryHost(string boundary)
        {
            _snapshots = BuildSnapshots(boundary);
        }

        public List<RuntimeGameAction> Submissions { get; } = new List<RuntimeGameAction>();

        public List<string> InvalidSubmissions { get; } = new List<string>();

        public RuntimeSnapshotEnvelope GetSnapshotForViewer(int viewerPlayerIndex)
        {
            var snapshot = _snapshots[Math.Min(_index, _snapshots.Count - 1)];
            snapshot.ViewerPlayerId = "player_" + viewerPlayerIndex;
            return snapshot;
        }

        public RuntimeActionSubmission SubmitForViewer(
            RuntimeGameAction action,
            int presentationViewerIndex)
        {
            var snapshot = _snapshots[Math.Min(_index, _snapshots.Count - 1)];
            var advertised = snapshot.LegalActions.FirstOrDefault(candidate =>
                string.Equals(candidate.ActionId, action.ActionId, StringComparison.Ordinal));
            var validation = RuntimeActionBoundary.Validate(action, snapshot);
            if (advertised is null || !validation.Accepted)
            {
                InvalidSubmissions.Add(
                    action.ActionId + ": " + (advertised is null
                        ? "not_advertised"
                        : validation.ReasonKey));
            }

            Submissions.Add(action);
            _index++;
            var next = _snapshots[Math.Min(_index, _snapshots.Count - 1)];
            return new RuntimeActionSubmission(
                new RuntimeActionResult
                {
                    ContractVersion = ContractVersionGuard.ExpectedVersion,
                    MatchId = action.MatchId,
                    SnapshotRevision = action.SnapshotRevision,
                    ResultingSnapshotRevision = next.SnapshotRevision,
                    ActionId = action.ActionId,
                    Accepted = true,
                    ReasonKey = "action.accepted",
                },
                Array.Empty<GameEvent>(),
                next);
        }

        private static IReadOnlyList<RuntimeSnapshotEnvelope> BuildSnapshots(string boundary)
        {
            var thirdPhase = boundary switch
            {
                "DISCARD" => "DISCARD",
                "AMBUSH" => "AMBUSH",
                "ACTION_TERMINAL" => "OVER",
                _ => AdvertisedActionPolicy.ActionPhase,
            };
            var thirdActionType = boundary switch
            {
                "DISCARD" => "DISCARD",
                "AMBUSH" => "SKIP_AMBUSH",
                "ACTION_END" => AdvertisedActionPolicy.EndTurnAction,
                "ACTION_TERMINAL" => AdvertisedActionPolicy.EndTurnAction,
                _ => AdvertisedActionPolicy.PlayCardAction,
            };
            var thirdActions = boundary switch
            {
                "ACTION_PROGRESS_WITH_END" => new[]
                {
                    Action(3, "play_3", AdvertisedActionPolicy.PlayCardAction),
                    Action(3, "end_3", AdvertisedActionPolicy.EndTurnAction),
                },
                "ACTION_WRONG_ACTOR" => new[]
                {
                    Action(3, "play_3", AdvertisedActionPolicy.PlayCardAction),
                    Action(3, "end_wrong_actor_3", AdvertisedActionPolicy.EndTurnAction, actor: 0),
                },
                "ACTION_STALE_END" => new[]
                {
                    Action(3, "play_3", AdvertisedActionPolicy.PlayCardAction),
                    Action(2, "end_stale_3", AdvertisedActionPolicy.EndTurnAction),
                },
                "ACTION_TERMINAL" => Array.Empty<RuntimeLegalAction>(),
                _ => new[] { Action(3, ActionId(thirdActionType, 3), thirdActionType) },
            };

            return new[]
            {
                Snapshot(1, "play_1", AdvertisedActionPolicy.PlayCardAction),
                Snapshot(2, "play_2", AdvertisedActionPolicy.PlayCardAction),
                Snapshot(3, thirdPhase, thirdActions, boundary == "ACTION_TERMINAL" ? 0 : null),
                Handoff(4),
            };
        }

        private static RuntimeSnapshotEnvelope Snapshot(
            long revision,
            string actionId,
            string actionType)
        {
            return Snapshot(revision, AdvertisedActionPolicy.ActionPhase,
                new[] { Action(revision, actionId, actionType) });
        }

        private static RuntimeSnapshotEnvelope Snapshot(
            long revision,
            string phase,
            IReadOnlyList<RuntimeLegalAction> actions,
            int? winnerPlayerIndex = null)
        {
            return new RuntimeSnapshotEnvelope
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                MatchId = "match_ai_budget_boundary",
                SnapshotRevision = revision,
                Turn = 4,
                Phase = phase,
                CurrentPlayer = 1,
                ViewerPlayerId = "player_1",
                Players = Players(),
                Castle = new RuntimeCastleSnapshot(),
                LegalActions = actions,
                WinnerPlayerIndex = winnerPlayerIndex,
            };
        }

        private static RuntimeSnapshotEnvelope Handoff(long revision)
        {
            return new RuntimeSnapshotEnvelope
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                MatchId = "match_ai_budget_boundary",
                SnapshotRevision = revision,
                Turn = 5,
                Phase = "START",
                CurrentPlayer = 0,
                ViewerPlayerId = "player_0",
                Players = Players(),
                Castle = new RuntimeCastleSnapshot(),
                LegalActions = Array.Empty<RuntimeLegalAction>(),
            };
        }

        private static RuntimeLegalAction Action(
            long revision,
            string actionId,
            string type,
            int actor = 1)
        {
            return new RuntimeLegalAction
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                SnapshotRevision = revision,
                ActionId = actionId,
                Type = type,
                Actor = actor,
                ReasonKey = "test." + type,
                Payload = new Dictionary<string, object?>(StringComparer.Ordinal),
            };
        }

        private static RuntimePlayerSnapshot[] Players()
        {
            return new[]
            {
                new RuntimePlayerSnapshot { PlayerId = "player_0" },
                new RuntimePlayerSnapshot { PlayerId = "player_1" },
            };
        }

        private static string ActionId(string type, long revision)
            => type.ToLowerInvariant() + "_" + revision;
    }
}

}
