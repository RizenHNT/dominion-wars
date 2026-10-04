using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Adapters.Ai;
using DominionWars.Data;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// P0-5 release-blocker coverage for the shipped CPU turn pacing.
///
/// The advertised-action policy is only reachable in the shipped game through
/// DominionWars.Adapters.Ai.AiTurnCoordinator (Unity's
/// RuntimeAiTurnCoordinator is a thin facade over it). These tests therefore
/// drive a real machine-deck match through MatchFactory / RuntimeMatchGateway
/// using that coordinator for the machine side, so the acceptance claim covers
/// the same per-turn pacing the player actually experiences.
/// </summary>
[TestFixture]
public sealed class AiTurnCoordinatorTests
{
    /// <summary>Same fixed seed as AiLifecyclePolicyTests, so both suites describe one match.</summary>
    private const ulong AcceptanceSeed = 11;
    private const string ControlledWoodDrawCardId = "test_controlled_wood_draw";

    /// <summary>
    /// The release blocker itself: with the shipped pacing the machine must
    /// still reach its own leader win condition. The old coordinator forced
    /// END_TURN after a single ACTION-phase action, so the machine burned its
    /// one action per turn on commits and never reached six downloads.
    /// </summary>
    [Test]
    public void MachineDeckReachesSixDownloadsAndWinsWhilePacedByTheShippedCoordinator()
    {
        var production = LoadProduction();
        var run = RunMatch(production, scriptWoodOpponent: true);

        Assert.Multiple(() =>
        {
            Assert.That(run.Rejected, Is.Empty,
                "no advertised action may be rejected: " + string.Join(" | ", run.Rejected));
            Assert.That(run.MachineHalted, Is.False,
                "the machine coordinator must never halt before the match ends: " + run.Trace());
            Assert.That(run.Final.Phase, Is.EqualTo("OVER"), run.Trace());
            Assert.That(run.Final.WinnerPlayerIndex, Is.EqualTo(0),
                "the machine (player 0) must win with its own leader condition: " + run.Trace());
            Assert.That(run.Final.ReasonKey, Is.EqualTo("win.pull_total_ge"), run.Trace());
            Assert.That(run.BothLeadersManifestedBeforeSixthPull, Is.True,
                "both real deck leaders must have manifested before the sixth advertised PULL: " + run.Trace());
            Assert.That(run.Final.Players[0].PullCount, Is.EqualTo(6),
                "the machine leader needs six completed downloads: " + run.Trace());
            Assert.That(run.MachinePullSubmissions, Is.EqualTo(6),
                "exactly six downloads complete the machine leader condition: " + run.Trace());
            Assert.That(run.MaxActionsInOneActionPhase, Is.GreaterThan(1),
                "the pacing fix must let one ACTION phase hold more than one action: " + run.Trace());
        });

        TestContext.Out.WriteLine(
            "P0-5 coordinator acceptance: Winner={0} ReasonKey={1} PullCount={2} Turn={3} "
            + "machinePumps={4} machinePullSubmissions={5} maxActionsInOneActionPhase={6} "
            + "machineHalted={7} machineLastReasonKey={8} rejected={9} totalPumps={10}",
            run.Final.WinnerPlayerIndex,
            run.Final.ReasonKey,
            run.Final.Players[0].PullCount,
            run.Final.Turn,
            run.MachinePumps,
            run.MachinePullSubmissions,
            run.MaxActionsInOneActionPhase,
            run.MachineHalted,
            run.MachineLastReasonKey ?? "none",
            run.Rejected.Count,
            run.TotalPumps);

        foreach (var probe in run.ActionProbes)
        {
            TestContext.Out.WriteLine("P0-5 machine ACTION advertisement: " + probe);
        }
    }

    /// <summary>
    /// Regression guard for the pacing defect: while useful advertised actions
    /// remain, one ACTION phase must accept more than one action, and the AI
    /// only ends the phase once nothing better than END_TURN is advertised.
    /// Each script is one engine state: revisions advance exactly as the
    /// gateway advances them after an accepted action.
    /// </summary>
    [Test]
    public void ActionPhaseAcceptsMoreThanOneActionBeforeEndingThePhase()
    {
        var host = new ScriptedAiHost(
            Actions(1, ("play_1", AdvertisedActionPolicy.PlayCardAction), ("commit_1", AdvertisedActionPolicy.CommitAction)),
            Actions(2, ("commit_1", AdvertisedActionPolicy.CommitAction), ("end_1", AdvertisedActionPolicy.EndTurnAction)),
            Actions(3, ("end_1", AdvertisedActionPolicy.EndTurnAction)));
        var coordinator = new AiTurnCoordinator(host);

        Assert.That(coordinator.Pump(), Is.True);
        Assert.That(coordinator.Pump(), Is.True);
        Assert.That(coordinator.Pump(), Is.True);
        // Capture the phase totals before the closing pump: leaving the AI's
        // turn resets the per-turn counter, exactly as the shipped coordinator
        // did, so the logged evidence must be read here.
        var actionsInOneActionPhase = coordinator.ActionsTakenThisTurn;
        var submittedInOrder = host.SubmittedTypes.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(submittedInOrder, Is.EqualTo(new[]
            {
                AdvertisedActionPolicy.PlayCardAction,
                AdvertisedActionPolicy.CommitAction,
                AdvertisedActionPolicy.EndTurnAction,
            }), "one ACTION phase must run several actions, then leave through END_TURN");
            Assert.That(actionsInOneActionPhase, Is.EqualTo(3));
            Assert.That(coordinator.Halted, Is.False, coordinator.LastReasonKey);
            Assert.That(coordinator.Pump(), Is.False);
            Assert.That(coordinator.Halted, Is.False);
            Assert.That(coordinator.LastReasonKey, Is.EqualTo("ai.waiting_for_turn"));
        });

        TestContext.Out.WriteLine(
            "P0-5 pacing guard: submitted=[{0}] actionsInOneActionPhase={1} pumps={2}",
            string.Join(",", submittedInOrder),
            actionsInOneActionPhase,
            host.PumpCount);
    }

    /// <summary>
    /// The per-turn budget still bounds the ACTION phase: the coordinator stops
    /// with ai.action_limit_reached instead of looping forever, and it does not
    /// force a phase transition to keep going.
    /// </summary>
    [Test]
    public void PerTurnBudgetStillBoundsTheActionPhase()
    {
        var host = new ScriptedAiHost(
            Actions(1,
                ("play_1", AdvertisedActionPolicy.PlayCardAction),
                ("commit_1", AdvertisedActionPolicy.CommitAction),
                ("attack_1", AdvertisedActionPolicy.AttackAction)),
            Actions(2,
                ("commit_1", AdvertisedActionPolicy.CommitAction),
                ("attack_1", AdvertisedActionPolicy.AttackAction)),
            Actions(3, ("attack_1", AdvertisedActionPolicy.AttackAction)));
        var coordinator = new AiTurnCoordinator(host, maxActionsPerTurn: 2);

        Assert.That(coordinator.Pump(), Is.True);
        Assert.That(coordinator.Pump(), Is.True);
        var pumpsBeforeLimit = host.SubmissionCount;

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.ActionsTakenThisTurn, Is.EqualTo(2));
            Assert.That(coordinator.Pump(), Is.False);
            Assert.That(coordinator.Halted, Is.True);
            Assert.That(coordinator.LastReasonKey, Is.EqualTo("ai.action_limit_reached"));
            Assert.That(host.SubmissionCount, Is.EqualTo(pumpsBeforeLimit),
                "a halted coordinator must not keep submitting");
            Assert.That(coordinator.Pump(), Is.False);
            Assert.That(host.SubmissionCount, Is.EqualTo(pumpsBeforeLimit));
        });

        TestContext.Out.WriteLine(
            "P0-5 budget: maxActionsPerTurn=2 actionsTaken={0} halted={1} reason={2} submissions={3}",
            coordinator.ActionsTakenThisTurn,
            coordinator.Halted,
            coordinator.LastReasonKey,
            host.SubmissionCount);
    }

    /// <summary>
    /// Uses the production RuntimeMatchGateway rather than a scripted host to
    /// prove the budget escape is a real engine-advertised action. The small
    /// maxActionsPerTurn value makes the first useful ACTION phase reach the
    /// boundary quickly; the coordinator must submit that phase's exact
    /// END_TURN, not a synthetic action or a 2nd optional action.
    /// </summary>
    [Test]
    public void ProductionGatewayClosesAtBudgetWithAdvertisedEndTurn()
    {
        var production = LoadProduction();
        var options = new DominionWars.Engine.Setup.MatchSetupOptions
        {
            Seed = AcceptanceSeed,
            FirstPlayerIndex = 0,
            OpeningHandSize = 5,
            PlayerLife = 20,
            CastleEnabled = false,
            CastleHealth = 75,
        };
        var gateway = MatchFactory.CreateFromDecks(
            "match_ai_budget_gateway",
            production.Catalog,
            production.MachineDeck,
            production.WoodDeck,
            options);
        var initialization = gateway.Initialize(0);
        Assert.That(initialization.Accepted, Is.True, initialization.ReasonKey);

        var host = new GatewayAiHost(gateway);
        var coordinator = new AiTurnCoordinator(
            host,
            aiPlayerIndex: 0,
            presentationViewerIndex: 1,
            maxActionsPerTurn: 1);

        for (var step = 0; step < 200; step++)
        {
            var snapshot = gateway.GetSnapshot(0);
            Assert.That(snapshot.WinnerPlayerIndex.HasValue, Is.False,
                "the bounded probe must find its budget boundary before terminal state");

            var actionPhaseSubmissions = host.Submissions.Count(item =>
                string.Equals(item.Snapshot.Phase, AdvertisedActionPolicy.ActionPhase, StringComparison.Ordinal));
            var advertisedEnd = snapshot.LegalActions.FirstOrDefault(action =>
                string.Equals(action.Type, AdvertisedActionPolicy.EndTurnAction, StringComparison.Ordinal) &&
                action.Actor == snapshot.CurrentPlayer &&
                action.SnapshotRevision == snapshot.SnapshotRevision);
            var hasOptionalAction = snapshot.LegalActions.Any(action =>
                !string.Equals(action.Type, AdvertisedActionPolicy.EndTurnAction, StringComparison.Ordinal));

            if (string.Equals(snapshot.Phase, AdvertisedActionPolicy.ActionPhase, StringComparison.Ordinal) &&
                actionPhaseSubmissions > 0 &&
                advertisedEnd is not null &&
                hasOptionalAction)
            {
                var submissionsBefore = host.Submissions.Count;
                Assert.That(coordinator.Pump(), Is.True,
                    "the budget boundary must close through the advertised END_TURN");
                Assert.That(host.Submissions.Count, Is.EqualTo(submissionsBefore + 1));
                var submitted = host.Submissions[^1];
                Assert.Multiple(() =>
                {
                    Assert.That(submitted.Action.Type, Is.EqualTo(AdvertisedActionPolicy.EndTurnAction));
                    Assert.That(submitted.Action.ActionId, Is.EqualTo(advertisedEnd.ActionId));
                    Assert.That(submitted.Action.SnapshotRevision, Is.EqualTo(snapshot.SnapshotRevision));
                    Assert.That(submitted.Advertised, Is.Not.Null);
                    Assert.That(coordinator.Halted, Is.False);
                    Assert.That(host.Rejected, Is.Empty);
                });

                TestContext.Out.WriteLine(
                    "AI production gateway budget close: seed={0} step={1} revision={2} "
                    + "actionPhaseSubmissionsBefore={3} actionId={4} submissions={5} rejected={6}",
                    AcceptanceSeed,
                    step,
                    snapshot.SnapshotRevision,
                    actionPhaseSubmissions,
                    submitted.Action.ActionId,
                    host.Submissions.Count,
                    host.Rejected.Count);
                return;
            }

            Assert.That(coordinator.Pump(), Is.True,
                "the production gateway should advance to a budget boundary before halting: "
                + "step=" + step + " phase=" + snapshot.Phase + " turn=" + snapshot.Turn);
        }

        Assert.Fail("The production gateway did not expose an ACTION snapshot with END_TURN and an optional action within 200 pumps.");
    }

    /// <summary>
    /// Unity's default CPU is player 1 with the machine deck and the default
    /// advertised-action policy. This production gateway run uses that seat
    /// and the shipped balance rules, while retaining the older player-0
    /// machine run above as a separate fixture. It is a configuration
    /// consistency probe, not a claim about a live Unity mouse session.
    /// </summary>
    [Test]
    public void UnityDefaultCpuSeatUsesTheSamePolicyAndShippedRules()
    {
        var production = LoadProduction();
        var options = new DominionWars.Engine.Setup.MatchSetupOptions
        {
            Seed = AcceptanceSeed,
            FirstPlayerIndex = 0,
            OpeningHandSize = 5,
            PlayerLife = 20,
            CastleEnabled = false,
            CastleHealth = 75,
        };
        var gateway = MatchFactory.CreateWithShippedRulesFromDecks(
            "match_ai_unity_default_config",
            production.Catalog,
            production.WoodDeck,
            production.MachineDeck,
            options);
        var initialization = gateway.Initialize(0);
        Assert.That(initialization.Accepted, Is.True, initialization.ReasonKey);

        const int machineIndex = 1;
        var machineHost = new GatewayAiHost(gateway);
        var otherHost = new GatewayAiHost(gateway);
        var machine = new AiTurnCoordinator(
            machineHost,
            aiPlayerIndex: machineIndex,
            presentationViewerIndex: 0);
        var other = new AiTurnCoordinator(
            otherHost,
            aiPlayerIndex: 0,
            presentationViewerIndex: machineIndex);

        var totalPumps = 0;
        while (totalPumps++ < 8000)
        {
            var probe = gateway.GetSnapshot(machineIndex);
            if (probe.WinnerPlayerIndex.HasValue) break;

            var step = other.Pump() ? other : (machine.Pump() ? machine : null);
            if (step is null) break;
        }

        var final = gateway.GetSnapshot(machineIndex);
        var counts = machineHost.Submissions
            .GroupBy(item => item.Action.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Func<string, int> count = type => counts.TryGetValue(type, out var value) ? value : 0;

        Assert.Multiple(() =>
        {
            Assert.That(machineHost.Rejected, Is.Empty,
                "Unity-default-seat gateway submissions must remain engine-accepted: "
                + string.Join(" | ", machineHost.Rejected));
            Assert.That(machineHost.Submissions, Is.Not.Empty);
            Assert.That(count(AdvertisedActionPolicy.PullAction), Is.GreaterThan(0));
            Assert.That(count(AdvertisedActionPolicy.CommitAction), Is.GreaterThan(0));
            Assert.That(count(AdvertisedActionPolicy.EndTurnAction), Is.GreaterThan(0));
            Assert.That(machine.Halted, Is.False,
                "the default CPU seat must not halt before this bounded production probe ends");
        });

        TestContext.Out.WriteLine(
            "AI Unity-default gateway probe: seed={0} machineSeat={1} rules=maxPunishResponsesPerRound:{2} "
            + "finalTurn={3} finalPhase={4} winner={5} pumps={6} submissions={7}",
            AcceptanceSeed,
            machineIndex,
            options.Rules?.MaxPunishResponsesPerRound.ToString() ?? "null",
            final.Turn,
            final.Phase,
            final.WinnerPlayerIndex?.ToString() ?? "none",
            totalPumps,
            string.Join(",", counts.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => item.Key + ":" + item.Value)));
    }

    /// <summary>
    /// The force-end-turn path still ends the phase when the advertised set
    /// offers nothing else. This is the exact production shape the coordinator
    /// meets once the machine has played out its hand.
    /// </summary>
    [Test]
    public void PhaseEndsWhenNothingBetterThanEndTurnIsAdvertised()
    {
        var host = new ScriptedAiHost(
            Actions(1, ("end_1", AdvertisedActionPolicy.EndTurnAction)));
        var coordinator = new AiTurnCoordinator(host);

        Assert.That(coordinator.Pump(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(host.SubmittedTypes.ToArray(), Is.EqualTo(new[] { AdvertisedActionPolicy.EndTurnAction }),
                "the only advertised action must be submitted verbatim");
            Assert.That(coordinator.ActionsTakenThisTurn, Is.EqualTo(1));
            Assert.That(coordinator.Pump(), Is.False);
            Assert.That(coordinator.Halted, Is.False, coordinator.LastReasonKey);
            Assert.That(coordinator.LastReasonKey, Is.EqualTo("ai.waiting_for_turn"));
        });

        TestContext.Out.WriteLine(
            "P0-5 end-turn path: submitted=[{0}] reason={1}",
            string.Join(",", host.SubmittedTypes),
            coordinator.LastReasonKey);
    }

    /// <summary>
    /// An advertised download this policy refuses to submit (it claims a
    /// selection and carries none) must not keep the ACTION phase alive: the
    /// coordinator ends the phase through the advertised END_TURN instead of
    /// submitting an action the boundary has already failed closed on.
    /// </summary>
    [Test]
    public void UnusableAdvertisedDownloadDoesNotKeepTheActionPhaseAlive()
    {
        var host = new ScriptedAiHost(
            ActionsWithPayload(1,
                ("pull_1", AdvertisedActionPolicy.PullAction, UnresolvablePullPayload()),
                ("end_1", AdvertisedActionPolicy.EndTurnAction, null)));
        var coordinator = new AiTurnCoordinator(host);

        Assert.That(coordinator.Pump(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(host.SubmittedTypes.ToArray(), Is.EqualTo(new[] { AdvertisedActionPolicy.EndTurnAction }),
                "an unusable download must not be submitted");
            Assert.That(coordinator.Pump(), Is.False);
            Assert.That(coordinator.LastReasonKey, Is.EqualTo("ai.waiting_for_turn"));
        });
    }

    /// <summary>
    /// A rejected submission still fails closed with the engine's own reason
    /// key, submits nothing further, and never retries the same action.
    /// </summary>
    [Test]
    public void RejectedSubmissionHaltsWithTheEngineReasonKey()
    {
        var host = new ScriptedAiHost(
            Actions(1,
                ("play_1", AdvertisedActionPolicy.PlayCardAction),
                ("commit_1", AdvertisedActionPolicy.CommitAction)))
        {
            RejectionReasonKey = "action.phase_mismatch",
        };
        var coordinator = new AiTurnCoordinator(host);

        Assert.That(coordinator.Pump(), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(host.SubmissionCount, Is.EqualTo(1), "no retry after a rejection");
            Assert.That(coordinator.Halted, Is.True);
            Assert.That(coordinator.LastReasonKey, Is.EqualTo("action.phase_mismatch"));
            Assert.That(coordinator.ActionsTakenThisTurn, Is.EqualTo(0));
            Assert.That(coordinator.Pump(), Is.False);
            Assert.That(host.SubmissionCount, Is.EqualTo(1));
        });

        TestContext.Out.WriteLine(
            "P0-5 rejected path: submissions={0} halted={1} lastReasonKey={2}",
            host.SubmissionCount,
            coordinator.Halted,
            coordinator.LastReasonKey);
    }

    /// <summary>No advertised action at all still halts instead of spinning.</summary>
    [Test]
    public void NoAdvertisedActionHaltsFailClosed()
    {
        var host = new ScriptedAiHost(Actions(1));
        var coordinator = new AiTurnCoordinator(host);

        Assert.That(coordinator.Pump(), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(host.SubmissionCount, Is.EqualTo(0));
            Assert.That(coordinator.Halted, Is.True);
            Assert.That(coordinator.LastReasonKey, Is.EqualTo("ai.no_legal_actions"));
            Assert.That(coordinator.Pump(), Is.False);
        });

        TestContext.Out.WriteLine(
            "P0-5 no-action path: halted={0} lastReasonKey={1}",
            coordinator.Halted,
            coordinator.LastReasonKey);
    }

    /// <summary>
    /// The terminal snapshot halts the driver with ai.match_over, which is what
    /// the shipped screen flow relies on to stop pumping after a victory.
    /// </summary>
    [Test]
    public void TerminalSnapshotHaltsWithMatchOver()
    {
        var terminal = new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = "match_ai_coordinator_unit",
            SnapshotRevision = 999,
            Turn = 4,
            Phase = "OVER",
            CurrentPlayer = 0,
            ViewerPlayerId = "player_1",
            WinnerPlayerIndex = 0,
            ReasonKey = "win.pull_total_ge",
            Players = new[]
            {
                new RuntimePlayerSnapshot { PlayerId = "player_0", PullCount = 6, CloudStackCount = 0 },
                new RuntimePlayerSnapshot { PlayerId = "player_1", CloudStackCount = 1 },
            },
            Castle = new RuntimeCastleSnapshot(),
            LegalActions = Array.Empty<RuntimeLegalAction>(),
        };
        var host = new ScriptedAiHost(
            terminal,
            Actions(1, ("end_1", AdvertisedActionPolicy.EndTurnAction)));
        var coordinator = new AiTurnCoordinator(host);

        Assert.That(coordinator.Pump(), Is.True);
        Assert.That(coordinator.Pump(), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.Halted, Is.True);
            Assert.That(coordinator.LastReasonKey, Is.EqualTo("ai.match_over"));
            Assert.That(host.SubmissionCount, Is.EqualTo(1));
            Assert.That(coordinator.Pump(), Is.False);
            Assert.That(host.SubmissionCount, Is.EqualTo(1));
        });

        TestContext.Out.WriteLine(
            "P0-5 terminal path: halted={0} lastReasonKey={1} submissions={2}",
            coordinator.Halted,
            coordinator.LastReasonKey,
            host.SubmissionCount);
    }

    /// <summary>
    /// Legality/payload invariant: every action the coordinator put on the wire
    /// was advertised verbatim by the engine snapshot it chose from, including
    /// the engine-supplied selectedEntityIds payload of a targeted download.
    /// The coordinator never widens the advertised set to keep acting.
    /// </summary>
    [Test]
    public void EveryPacedSubmissionIsAnAdvertisedActionForwardedUnchanged()
    {
        var production = LoadProduction();
        var run = RunMatch(production, scriptWoodOpponent: true);

        Assert.That(run.Submissions, Is.Not.Empty, run.Trace());
        foreach (var submission in run.Submissions)
        {
            Assert.That(submission.Advertised, Is.Not.Null,
                submission.Action.ActionId + ": the submitted action must be in the advertised set: "
                + submission.DescribeComparison());
            Assert.That(submission.Advertised!.Actor, Is.EqualTo(submission.Snapshot.CurrentPlayer),
                submission.Action.ActionId + ": the advertisement actor must be the current player: "
                + submission.DescribeComparison());
            Assert.That(RuntimeActionBoundary.Validate(submission.Action, submission.Snapshot).Accepted, Is.True,
                submission.Action.ActionId + ": the boundary must accept the action exactly as advertised: "
                + submission.DescribeComparison());
            var comparison = submission.DescribeComparison();
            Assert.That(ValuesEqual(submission.Action.Payload, submission.Advertised.Payload), Is.True,
                submission.Action.ActionId + ": the payload must be the advertised payload: " + comparison);
            Assert.That(ValuesEqual(submission.Action.SourceId, submission.Advertised.SourceId), Is.True,
                submission.Action.ActionId + ": source id must be forwarded unchanged: " + comparison);
            Assert.That(ValuesEqual(submission.Action.TargetId, submission.Advertised.TargetId), Is.True,
                submission.Action.ActionId + ": target id must be forwarded unchanged: " + comparison);
            Assert.That(submission.Action.CardId, Is.EqualTo(submission.Advertised.CardId),
                submission.Action.ActionId + ": card id must be forwarded unchanged: " + comparison);
        }

        var pulls = run.Submissions
            .Where(item => item.Action.Type == AdvertisedActionPolicy.PullAction)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(pulls, Is.Not.Empty, "the paced run must contain downloads: " + run.Trace());
            Assert.That(
                pulls.Count(item => item.Action.Payload.ContainsKey("selectedEntityIds")),
                Is.GreaterThanOrEqualTo(1),
                "targeted downloads must carry the engine-advertised selectedEntityIds: "
                + string.Join(", ", pulls.Select(item => item.Action.ActionId)));
            Assert.That(
                pulls.Any(item => ValuesEqual(item.Advertised!.Payload, item.Action.Payload)),
                Is.True,
                "every download must carry its advertised payload: "
                + string.Join(" | ", pulls.Select(item => item.DescribeComparison())));
        });

        TestContext.Out.WriteLine(
            "P0-5 paced payload provenance: {0} machine submissions compared against their advertisements, "
            + "{1} downloads ({2} with an engine-advertised selectedEntityIds)",
            run.Submissions.Count,
            pulls.Length,
            pulls.Count(item => item.Action.Payload.ContainsKey("selectedEntityIds")));
    }

    /// <summary>
    /// Drives a full match through the production gateway with the ported
    /// coordinator pumping the machine side, exactly as Unity's screen flow
    /// pumps it. The wood-deck side is driven by its own coordinator so the
    /// match uses the shipped coordinator for the production machine deck. The
    /// acceptance variant can script only the wood opponent's already-advertised
    /// safe inputs so the test measures the machine's download path, not whether
    /// it beats a legitimate Wood 512-health win before reaching six downloads.
    /// </summary>
    private static PacedMatchRun RunMatch(Production production, bool scriptWoodOpponent = false)
    {
        var runProduction = scriptWoodOpponent
            ? ControlledDrawWoodProduction(production)
            : production;
        var woodDeck = runProduction.WoodDeck;
        var options = new DominionWars.Engine.Setup.MatchSetupOptions
        {
            Seed = AcceptanceSeed,
            FirstPlayerIndex = 0,
            OpeningHandSize = 5,
            PlayerLife = 20,
            CastleEnabled = false,
            CastleHealth = 75,
        };

        var gateway = MatchFactory.CreateFromDecks(
            "match_ai_coordinator",
            runProduction.Catalog,
            runProduction.MachineDeck,
            woodDeck,
            options);

        var initialization = gateway.Initialize(0);
        Assert.That(initialization.Accepted, Is.True, initialization.ReasonKey);

        var machineIndex = 0;
        var machineHost = new GatewayAiHost(gateway);
        var humanHost = new GatewayAiHost(gateway);
        var machine = new AiTurnCoordinator(
            machineHost,
            aiPlayerIndex: machineIndex,
            presentationViewerIndex: 1);
        var human = new AiTurnCoordinator(
            humanHost,
            aiPlayerIndex: 1,
            presentationViewerIndex: 0);

        var actionsPerActionPhase = new Dictionary<int, int>();
        var lastMachineActionPhaseKey = -1;
        var totalPumps = 0;
        var machinePumps = 0;
        var scriptedOpponentRejected = new List<string>();
        var scriptedOpponentFailure = string.Empty;
        var bothLeadersManifestedBeforeSixthPull = false;
        var controlledDrawCardPlayed = false;

        while (totalPumps++ < 8000)
        {
            var probe = gateway.GetSnapshot(0);
            if (probe.WinnerPlayerIndex.HasValue) break;

            AiTurnCoordinator? step = null;
            var machineSubmissionCountBefore = machineHost.Submissions.Count;
            if (machine.Pump())
            {
                step = machine;
                if (probe.Players[0].PullCount == 5
                    && machineHost.Submissions.Skip(machineSubmissionCountBefore)
                        .Any(item => string.Equals(
                            item.Action.Type,
                            AdvertisedActionPolicy.PullAction,
                            StringComparison.Ordinal)))
                {
                    bothLeadersManifestedBeforeSixthPull = BothLeadersManifested(
                        probe,
                        runProduction.MachineDeck.Leader,
                        woodDeck.Leader);
                }
            }
            else if (scriptWoodOpponent)
            {
                var opponentSnapshot = gateway.GetSnapshot(1);
                var opponentAction = ChooseSafeWoodAction(
                    opponentSnapshot,
                    runProduction.MachineDeck.Leader,
                    woodDeck.Leader,
                    controlledDrawCardPlayed);
                if (opponentAction is null)
                {
                    scriptedOpponentFailure = "no advertised SKIP_AMBUSH, END_TURN, or DISCARD action at turn="
                        + opponentSnapshot.Turn + " phase=" + opponentSnapshot.Phase;
                    break;
                }

                var submittedAction = ToSubmittedGameAction(opponentSnapshot, opponentAction);
                var submission = gateway.Submit(submittedAction);
                if (!submission.Result.Accepted)
                {
                    var rejection = "turn=" + opponentSnapshot.Turn
                        + " phase=" + opponentSnapshot.Phase
                        + " action=" + submittedAction.ActionId
                        + " reason=" + submission.Result.ReasonKey;
                    scriptedOpponentRejected.Add(rejection);
                    scriptedOpponentFailure = "scripted wood action rejected: " + rejection;
                    break;
                }

                if (string.Equals(submittedAction.CardId, ControlledWoodDrawCardId, StringComparison.Ordinal))
                    controlledDrawCardPlayed = true;

                continue;
            }
            else if (human.Pump())
            {
                step = human;
            }

            if (step is null)
            {
                break;
            }

            if (ReferenceEquals(step, machine))
            {
                machinePumps++;
                var actorSnapshot = gateway.GetSnapshot(machineIndex);
                if (string.Equals(actorSnapshot.Phase, AdvertisedActionPolicy.ActionPhase, StringComparison.Ordinal))
                {
                    var key = actorSnapshot.Turn;
                    if (lastMachineActionPhaseKey != key)
                    {
                        lastMachineActionPhaseKey = key;
                        actionsPerActionPhase[key] = 0;
                    }

                    actionsPerActionPhase[key] = actionsPerActionPhase[key] + 1;
                }
            }
        }

        var final = gateway.GetSnapshot(machineIndex);
        var submissions = machineHost.Submissions
            .Select(item => new RecordedSubmission(item.Snapshot, item.Advertised, item.Action))
            .ToArray();

        return new PacedMatchRun(
            final,
            submissions,
            machineHost.Rejected.Concat(scriptedOpponentRejected).ToArray(),
            machine.Halted || scriptedOpponentFailure.Length > 0,
            scriptedOpponentFailure.Length == 0
                ? machine.LastReasonKey
                : "scripted_wood_opponent: " + scriptedOpponentFailure,
            machinePumps,
            totalPumps,
            actionsPerActionPhase.Count == 0 ? 0 : actionsPerActionPhase.Values.Max(),
            machineHost.ActionProbes,
            bothLeadersManifestedBeforeSixthPull);
    }

    private static RuntimeLegalAction? ChooseSafeWoodAction(
        RuntimeSnapshotEnvelope snapshot,
        string machineLeaderId,
        string woodLeaderId,
        bool controlledDrawCardPlayed)
    {
        if (!controlledDrawCardPlayed
            && string.Equals(snapshot.Phase, AdvertisedActionPolicy.ActionPhase, StringComparison.Ordinal)
            && (!HasManifestedLeader(snapshot.Players[0], machineLeaderId)
                || !HasManifestedLeader(snapshot.Players[1], woodLeaderId)))
        {
            var drawCard = snapshot.LegalActions
                .Where(candidate => string.Equals(candidate.Type, AdvertisedActionPolicy.PlayCardAction, StringComparison.Ordinal)
                    && string.Equals(candidate.CardId, ControlledWoodDrawCardId, StringComparison.Ordinal))
                .OrderBy(candidate => candidate.ActionId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (drawCard is not null) return drawCard;
        }

        var safeTypes = new[]
        {
            AdvertisedActionPolicy.SkipAmbushAction,
            AdvertisedActionPolicy.EndTurnAction,
            AdvertisedActionPolicy.DiscardAction,
        };

        foreach (var type in safeTypes)
        {
            var action = snapshot.LegalActions
                .Where(candidate => string.Equals(candidate.Type, type, StringComparison.Ordinal))
                .OrderBy(candidate => candidate.ActionId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (action is not null) return action;
        }

        return null;
    }

    private static Production ControlledDrawWoodProduction(Production production)
    {
        // A normal catalog plus one test-only card with only the existing
        // DRAW/OPP_DRAW actions. One advertised play exposes each real leader
        // from its owner's own deck; production Machine data/policy remain
        // untouched.
        var cards = production.Catalog.Cards.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal);
        cards.Add(
            ControlledWoodDrawCardId,
            new DominionWars.Engine.Model.CardDefinition(
                ControlledWoodDrawCardId,
                "Test controlled DRAW 60",
                type: "SPELL",
                faction: production.WoodDeck.Faction,
                text: "Test-only draw fixture.",
                onPlayEffects: new[]
                {
                    new DominionWars.Engine.Effects.EffectSpec("DRAW", amount: 60),
                    new DominionWars.Engine.Effects.EffectSpec("OPP_DRAW", amount: 60),
                }));
        var catalog = new CardCatalog(cards);
        var deck = new DeckDefinition(
            "test_controlled_wood_draw_punish",
            production.WoodDeck.Faction,
            production.WoodDeck.Leader,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [ControlledWoodDrawCardId] = 58,
                ["wood_punish_wrath"] = 1,
            });
        return new Production(catalog, deck, production.MachineDeck);
    }

    private static bool BothLeadersManifested(
        RuntimeSnapshotEnvelope snapshot,
        string machineLeaderId,
        string woodLeaderId)
    {
        return HasManifestedLeader(snapshot.Players[0], machineLeaderId)
            && HasManifestedLeader(snapshot.Players[1], woodLeaderId);
    }

    private static bool HasManifestedLeader(RuntimePlayerSnapshot player, string leaderId)
    {
        return player.LeaderZone.Concat(player.Field).Concat(player.Ambush)
            .Any(card => string.Equals(card.CardId, leaderId, StringComparison.Ordinal));
    }

    private static RuntimeGameAction ToSubmittedGameAction(
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction chosen)
    {
        var action = AdvertisedActionPolicy.ToGameAction(snapshot, chosen);
        if (!RuntimeActionSelection.TryBuildStableSelection(
                chosen,
                out var selectedEntityIds,
                out var reasonKey))
        {
            throw new AssertionException(
                chosen.ActionId + ": malformed selection advertisement: " + reasonKey);
        }

        if (selectedEntityIds.Count > 0 && !action.Payload.ContainsKey("selectedEntityIds"))
        {
            action.SelectedEntityIds = selectedEntityIds;
        }

        return action;
    }

    /// <summary>
    /// Wraps the production gateway as the coordinator's transport seam. It
    /// only forwards GetSnapshot/Submit, and it records the advertisement each
    /// accepted action was copied from so provenance can be asserted.
    /// </summary>
    private sealed class GatewayAiHost : IAiTurnHost
    {
        private readonly RuntimeMatchGateway _gateway;
        private readonly List<HostSubmission> _submissions = new List<HostSubmission>();
        private readonly List<string> _rejected = new List<string>();

        public GatewayAiHost(RuntimeMatchGateway gateway)
        {
            _gateway = gateway;
        }

        public IReadOnlyList<HostSubmission> Submissions => _submissions;

        public IReadOnlyList<string> Rejected => _rejected;

        public RuntimeSnapshotEnvelope GetSnapshotForViewer(int viewerPlayerIndex)
        {
            var snapshot = _gateway.GetSnapshot(viewerPlayerIndex);
            if (!string.Equals(
                snapshot.ViewerPlayerId,
                "player_" + viewerPlayerIndex,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Snapshot viewer identity does not match the requested player.");
            }

            if (viewerPlayerIndex == 0 &&
                string.Equals(snapshot.Phase, AdvertisedActionPolicy.ActionPhase, StringComparison.Ordinal) &&
                _actionProbes.Count < 3)
            {
                _actionProbes.Add("turn=" + snapshot.Turn
                    + " rev=" + snapshot.SnapshotRevision
                    + " current=" + snapshot.CurrentPlayer
                    + " cloud=" + snapshot.Players[0].CloudStackCount
                    + " legal=[" + string.Join(",", snapshot.LegalActions.Select(
                        action => action.Type + "/" + action.ActionId)) + "]");
            }

            return snapshot;
        }

        private readonly List<string> _actionProbes = new List<string>();

        public IReadOnlyList<string> ActionProbes => _actionProbes;

        public RuntimeActionSubmission SubmitForViewer(
            RuntimeGameAction action,
            int presentationViewerIndex)
        {
            var snapshot = _gateway.GetSnapshot(action.Actor);
            RuntimeLegalAction? advertised = null;
            foreach (var candidate in snapshot.LegalActions)
            {
                if (string.Equals(candidate.ActionId, action.ActionId, StringComparison.Ordinal))
                {
                    advertised = candidate;
                    break;
                }
            }

            var submission = _gateway.Submit(action);
            if (submission.Result.Accepted)
            {
                _submissions.Add(new HostSubmission(snapshot, advertised, action));
            }
            else
            {
                _rejected.Add("turn=" + snapshot.Turn
                    + " phase=" + snapshot.Phase
                    + " action=" + action.ActionId
                    + " reason=" + submission.Result.ReasonKey);
            }

            return submission;
        }
    }

    private sealed class HostSubmission
    {
        public HostSubmission(
            RuntimeSnapshotEnvelope snapshot,
            RuntimeLegalAction? advertised,
            RuntimeGameAction action)
        {
            Snapshot = snapshot;
            Advertised = advertised;
            Action = action;
        }

        public RuntimeSnapshotEnvelope Snapshot { get; }
        public RuntimeLegalAction? Advertised { get; }
        public RuntimeGameAction Action { get; }
    }

    /// <summary>
    /// A revision-faithful host for the pacing tests. Each script is the
    /// advertisement for one successful pump; an accepted submission advances
    /// the revision and moves to the next script, exactly as the gateway does,
    /// so the coordinator can never resubmit a stale action. Once the scripts
    /// run out the host serves <see cref="Handoff"/>, which by default is a
    /// snapshot where it is no longer the AI's turn.
    /// </summary>
    private sealed class ScriptedAiHost : IAiTurnHost
    {
        private const int AiPlayerIndex = 1;

        private readonly List<ScriptedStep> _scripts;
        private readonly RuntimeSnapshotEnvelope _handoff;
        private readonly List<string> _submittedTypes = new List<string>();
        private int _index;
        private int _pumpCount;
        private long _revision;

        public ScriptedAiHost(params ScriptedStep[] scripts)
            : this(HandoffSnapshot(), scripts)
        {
        }

        public ScriptedAiHost(RuntimeSnapshotEnvelope handoff, params ScriptedStep[] scripts)
        {
            _scripts = scripts is null || scripts.Length == 0
                ? new List<ScriptedStep> { new ScriptedStep(1, Array.Empty<RuntimeLegalAction>()) }
                : scripts.ToList();
            _handoff = handoff ?? HandoffSnapshot();
            _revision = _scripts[0].Snapshot.SnapshotRevision;
        }

        /// <summary>Reason key every submission is rejected with; null accepts everything.</summary>
        public string? RejectionReasonKey { get; set; }

        public int SubmissionCount { get; private set; }

        public int PumpCount => _pumpCount;

        public IReadOnlyList<string> SubmittedTypes => _submittedTypes;

        public RuntimeSnapshotEnvelope GetSnapshotForViewer(int viewerPlayerIndex)
        {
            _pumpCount++;
            // Each script is one engine state and pins the revision the engine
            // would report there. The handoff is the state after the AI's turn
            // ends, reported at the revision the last accepted action produced.
            var script = CurrentScript;
            var snapshot = script is null ? _handoff : script.Snapshot;
            if (script is null) snapshot.SnapshotRevision = _revision;
            snapshot.ViewerPlayerId = "player_" + viewerPlayerIndex;
            return snapshot;
        }

        public RuntimeActionSubmission SubmitForViewer(
            RuntimeGameAction action,
            int presentationViewerIndex)
        {
            SubmissionCount++;
            _submittedTypes.Add(action.Type);
            var accepted = RejectionReasonKey is null;
            if (accepted) _index++;
            if (accepted) _revision++;

            // Mirrors the gateway: an accepted action advances the revision to
            // the next advertised state, and the result snapshot carries that
            // state. A rejected action leaves the match where it was.
            var resultSnapshot = new RuntimeSnapshotEnvelope
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                MatchId = action.MatchId,
                SnapshotRevision = accepted ? _revision : action.SnapshotRevision,
                Turn = 4,
                Phase = AdvertisedActionPolicy.ActionPhase,
                CurrentPlayer = AiPlayerIndex,
                ViewerPlayerId = "player_" + AiPlayerIndex,
                Players = Players(),
                Castle = new RuntimeCastleSnapshot(),
                LegalActions = CurrentScript?.Actions ?? Array.Empty<RuntimeLegalAction>(),
            };
            return new RuntimeActionSubmission(
                new RuntimeActionResult
                {
                    ContractVersion = ContractVersionGuard.ExpectedVersion,
                    MatchId = action.MatchId,
                    SnapshotRevision = action.SnapshotRevision,
                    ResultingSnapshotRevision = resultSnapshot.SnapshotRevision,
                    ActionId = action.ActionId,
                    Accepted = accepted,
                    ReasonKey = accepted ? "action.accepted" : RejectionReasonKey!,
                },
                Array.Empty<DominionWars.Engine.Events.GameEvent>(),
                resultSnapshot);
        }

        private ScriptedStep? CurrentScript => _index < _scripts.Count ? _scripts[_index] : null;

        private static RuntimeSnapshotEnvelope HandoffSnapshot(int? winner = null)
        {
            return new RuntimeSnapshotEnvelope
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                MatchId = "match_ai_coordinator_unit",
                SnapshotRevision = 999,
                Turn = 4,
                Phase = "START",
                CurrentPlayer = 0,
                ViewerPlayerId = "player_0",
                WinnerPlayerIndex = winner,
                Players = Players(),
                Castle = new RuntimeCastleSnapshot(),
                LegalActions = Array.Empty<RuntimeLegalAction>(),
            };
        }

        private static RuntimePlayerSnapshot[] Players()
        {
            return new[]
            {
                new RuntimePlayerSnapshot { PlayerId = "player_0", CloudStackCount = 1 },
                new RuntimePlayerSnapshot { PlayerId = "player_1", CloudStackCount = 1 },
            };
        }
    }

    /// <summary>
    /// One advertised step: the snapshot revision the engine would report and
    /// the complete set of legal actions it would advertise at that revision.
    /// </summary>
    private sealed class ScriptedStep
    {
        public ScriptedStep(long revision, IReadOnlyList<RuntimeLegalAction> actions)
        {
            Snapshot = new RuntimeSnapshotEnvelope
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                MatchId = "match_ai_coordinator_unit",
                SnapshotRevision = revision,
                Turn = 4,
                Phase = AdvertisedActionPolicy.ActionPhase,
                CurrentPlayer = 1,
                ViewerPlayerId = "player_1",
                Players = new[]
                {
                    new RuntimePlayerSnapshot { PlayerId = "player_0", CloudStackCount = 1 },
                    new RuntimePlayerSnapshot { PlayerId = "player_1", CloudStackCount = 1 },
                },
                Castle = new RuntimeCastleSnapshot(),
                LegalActions = actions,
            };
        }

        public RuntimeSnapshotEnvelope Snapshot { get; }

        public IReadOnlyList<RuntimeLegalAction> Actions => Snapshot.LegalActions;
    }

    private static ScriptedStep Actions(
        long revision,
        params (string ActionId, string Type)[] actions)
    {
        var withPayload = new (string ActionId, string Type, IReadOnlyDictionary<string, object?>? Payload)[actions.Length];
        for (var index = 0; index < actions.Length; index++)
        {
            withPayload[index] = (actions[index].ActionId, actions[index].Type, null);
        }

        return ActionsWithPayload(revision, withPayload);
    }

    private static ScriptedStep ActionsWithPayload(
        long revision,
        params (string ActionId, string Type, IReadOnlyDictionary<string, object?>? Payload)[] actions)
    {
        var legal = new List<RuntimeLegalAction>();
        foreach (var action in actions)
        {
            legal.Add(new RuntimeLegalAction
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                SnapshotRevision = revision,
                ActionId = action.ActionId,
                Type = action.Type,
                Actor = 1,
                ReasonKey = "test." + action.Type,
                Payload = action.Payload ?? new Dictionary<string, object?>(StringComparer.Ordinal),
            });
        }

        return new ScriptedStep(revision, legal);
    }

    private static IReadOnlyDictionary<string, object?> PullPayload()
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["punish"] = 1,
            ["selectedEntityIds"] = new[] { 33L },
        };
    }

    /// <summary>
    /// A download advertisement that claims a selection but carries none. The
    /// engine never ships this shape for the machine deck; it is the negative
    /// case AdvertisedActionPolicy.IsUsable refuses, which makes the phase
    /// worth ending.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> UnresolvablePullPayload()
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["punish"] = 1,
            ["selectedEntityIds"] = null,
        };
    }

    private sealed class RecordedSubmission
    {
        public RecordedSubmission(
            RuntimeSnapshotEnvelope snapshot,
            RuntimeLegalAction? advertised,
            RuntimeGameAction action)
        {
            Snapshot = snapshot;
            Advertised = advertised;
            Action = action;
        }

        public RuntimeSnapshotEnvelope Snapshot { get; }
        public RuntimeLegalAction? Advertised { get; }
        public RuntimeGameAction Action { get; }

        public string DescribeComparison()
        {
            if (Advertised is null) return "not advertised";
            return "submitted(payload=" + Describe(Action.Payload)
                + " source=" + Describe(Action.SourceId)
                + " target=" + Describe(Action.TargetId)
                + " card=" + (Action.CardId ?? "null") + ")"
                + " advertised(payload=" + Describe(Advertised.Payload)
                + " source=" + Describe(Advertised.SourceId)
                + " target=" + Describe(Advertised.TargetId)
                + " card=" + (Advertised.CardId ?? "null") + ")";
        }

        private static string Describe(object? value)
        {
            if (value is null) return "null";
            if (value is string text) return "'" + text + "'";
            if (value is System.Collections.IEnumerable items)
            {
                var parts = new List<string>();
                foreach (var item in items) parts.Add(Describe(item));
                return "[" + string.Join(",", parts) + "]";
            }

            if (value is IReadOnlyDictionary<string, object?> map)
            {
                var parts = new List<string>();
                foreach (var entry in map) parts.Add(entry.Key + "=" + Describe(entry.Value));
                return "{" + string.Join(",", parts) + "}";
            }

            return value.ToString() ?? "null";
        }
    }

    private sealed class PacedMatchRun
    {
        public PacedMatchRun(
            RuntimeSnapshotEnvelope final,
            IReadOnlyList<RecordedSubmission> submissions,
            IReadOnlyList<string> rejected,
            bool machineHalted,
            string machineLastReasonKey,
            int machinePumps,
            int totalPumps,
            int maxActionsInOneActionPhase,
            IReadOnlyList<string> actionProbes,
            bool bothLeadersManifestedBeforeSixthPull)
        {
            Final = final;
            Submissions = submissions;
            Rejected = rejected;
            MachineHalted = machineHalted;
            MachineLastReasonKey = machineLastReasonKey;
            MachinePumps = machinePumps;
            TotalPumps = totalPumps;
            MaxActionsInOneActionPhase = maxActionsInOneActionPhase;
            ActionProbes = actionProbes;
            BothLeadersManifestedBeforeSixthPull = bothLeadersManifestedBeforeSixthPull;
        }

        public RuntimeSnapshotEnvelope Final { get; }
        public IReadOnlyList<RecordedSubmission> Submissions { get; }
        public IReadOnlyList<string> Rejected { get; }
        public bool MachineHalted { get; }
        public string MachineLastReasonKey { get; }
        public int MachinePumps { get; }
        public int TotalPumps { get; }
        public int MaxActionsInOneActionPhase { get; }
        public IReadOnlyList<string> ActionProbes { get; }
        public bool BothLeadersManifestedBeforeSixthPull { get; }

        public int MachinePullSubmissions =>
            Submissions.Count(item => item.Action.Type == AdvertisedActionPolicy.PullAction);

        public string Trace()
        {
            return "turn=" + Final.Turn
                + " phase=" + Final.Phase
                + " winner=" + (Final.WinnerPlayerIndex.HasValue
                    ? Final.WinnerPlayerIndex.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "none")
                + " reason=" + (Final.ReasonKey ?? "none")
                + " pullCount=" + Final.Players[0].PullCount
                + " machinePumps=" + MachinePumps
                + " totalPumps=" + TotalPumps
                + " maxActionsInOneActionPhase=" + MaxActionsInOneActionPhase
                + " bothLeadersBeforeSixthPull=" + BothLeadersManifestedBeforeSixthPull
                + " machineHalted=" + MachineHalted
                + " machineLastReasonKey=" + (MachineLastReasonKey ?? "none")
                + " submissions=" + string.Join(",", Submissions
                    .GroupBy(item => item.Action.Type, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => group.Key + ":" + group.Count()));
        }
    }

    private sealed class Production
    {
        public Production(CardCatalog catalog, DeckDefinition woodDeck, DeckDefinition machineDeck)
        {
            Catalog = catalog;
            WoodDeck = woodDeck;
            MachineDeck = machineDeck;
        }

        public CardCatalog Catalog { get; }
        public DeckDefinition WoodDeck { get; }
        public DeckDefinition MachineDeck { get; }
    }

    private static Production LoadProduction()
    {
        var root = FindRepositoryRoot();
        var catalog = CardCatalog.LoadDirectory(Path.Combine(root, "data", "cards"));
        var decks = DeckLoader.LoadDirectory(Path.Combine(root, "data", "decks"));
        return new Production(
            catalog,
            decks.Single(deck => deck.Leader == "wood_leader"),
            decks.Single(deck => deck.Leader == "machine_leader"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docs", "RULES.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new AssertionException("Repository root was not found.");
    }

    /// <summary>Structural equality for wire values, mirroring the adapter boundary.</summary>
    private static bool ValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;

        if (left is string || right is string)
        {
            return string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal);
        }

        if (left is IReadOnlyDictionary<string, object?> leftMap &&
            right is IReadOnlyDictionary<string, object?> rightMap)
        {
            if (leftMap.Count != rightMap.Count) return false;
            foreach (var entry in leftMap)
            {
                if (!rightMap.TryGetValue(entry.Key, out var value)) return false;
                if (!ValuesEqual(entry.Value, value)) return false;
            }

            return true;
        }

        if (left is System.Collections.IEnumerable leftItems && right is System.Collections.IEnumerable rightItems)
        {
            var a = new List<object?>();
            var b = new List<object?>();
            foreach (var item in leftItems) a.Add(item);
            foreach (var item in rightItems) b.Add(item);
            if (a.Count != b.Count) return false;
            for (var index = 0; index < a.Count; index++)
            {
                if (!ValuesEqual(a[index], b[index])) return false;
            }

            return true;
        }

        return left.Equals(right);
    }
}
}
