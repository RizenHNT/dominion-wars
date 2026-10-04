using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Adapters.Ai;
using DominionWars.Data;
using DominionWars.Engine.Model;
using DominionWars.Engine.Setup;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// Small real-match probe for the shipped two-seat CPU path. This is deliberately
/// a fixed four-game diagnostic, not a balance simulation: both seats use the
/// production coordinator, every action crosses RuntimeMatchGateway, and the
/// output records a terminal result or the exact fail-closed boundary.
/// </summary>
[TestFixture]
public sealed class AiPlayableMatchProbeTests
{
    private const ulong ProbeSeed = 4242;
    private const int StepLimit = 4000;

    [Test]
    public void FourFactionFixedSeedMatchesReportTerminalOrExactHalt()
    {
        var factions = new[] { "flame", "machine", "sea", "wood" };
        var reports = new List<MatchReport>();

        for (var index = 0; index < factions.Length; index++)
        {
            reports.Add(Run(factions[index], factions[(index + 1) % factions.Length]));
        }

        foreach (var report in reports)
        {
            TestContext.Out.WriteLine(
                "AI_MATCH|{0}_vs_{1}|seed={2}|terminal={3}|winner={4}|reason={5}|phase={6}|turn={7}|actions={8}|rejected={9}|stepLimit={10}|halt={11}|viewerChecks={12}|hiddenLeaks={13}|lastAction={14}|lastLegal={15}|actionsThisTurn={16}",
                report.ActorFaction,
                report.FoeFaction,
                ProbeSeed,
                report.Terminal,
                report.Winner?.ToString() ?? "none",
                report.ReasonKey ?? "none",
                report.Phase,
                report.Turn,
                report.Actions,
                report.Rejected,
                report.StepLimit,
                report.HaltReason ?? "none",
                report.ViewerChecks,
                report.HiddenLeaks,
                report.LastActionId ?? "none",
                report.LastLegalActions ?? "none",
                report.ActionsTakenThisTurn);
        }

        Assert.That(reports, Has.Count.EqualTo(factions.Length));
        Assert.That(
            reports.SelectMany(report => report.Issues),
            Is.Empty,
            "the real CPU path must not reject or leak viewer-hidden information:\n  "
            + string.Join("\n  ", reports.SelectMany(report => report.Issues)));
    }

    private static MatchReport Run(string actorFaction, string foeFaction)
    {
        var root = RepositoryRoot();
        var catalog = CardCatalog.LoadDirectory(Path.Combine(root, "data", "cards"));
        var actorDeck = DeckLoader.LoadFile(
            Path.Combine(root, "data", "decks", actorFaction + "_deck.json"));
        var foeDeck = DeckLoader.LoadFile(
            Path.Combine(root, "data", "decks", foeFaction + "_deck.json"));

        var state = MatchSetup.Create(
            Spec(actorDeck),
            Spec(foeDeck),
            catalog.Cards,
            new MatchSetupOptions
            {
                Seed = ProbeSeed,
                FirstPlayerIndex = 0,
                OpeningHandSize = 5,
                PlayerLife = 20,
                CastleEnabled = true,
                CastleHealth = 75,
            });
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(
            flow,
            null,
            PunishResponseStances.PolicyFor(PlaystyleRegistry.Default));
        var gateway = new RuntimeMatchGateway(
            "match_ai_probe_" + actorFaction + "_" + foeFaction,
            state,
            flow,
            router,
            null);
        var initialization = gateway.Initialize(0);
        Assert.That(initialization.Accepted, Is.True, initialization.ReasonKey);

        var host = new ProbeHost(gateway);
        var coordinators = new[]
        {
            new AiTurnCoordinator(host, 0, 0),
            new AiTurnCoordinator(host, 1, 1),
        };
        var issues = new List<string>();
        var actions = 0;
        var viewerChecks = 0;
        var hiddenLeaks = 0;
        var haltReason = (string?)null;
        var stepLimit = false;
        var lastLegalActions = (string?)null;
        var actionsTakenThisTurn = 0;

        while (actions < StepLimit && !state.WinnerPlayerIndex.HasValue)
        {
            var actor = state.CurrentPlayerIndex;
            var snapshot = gateway.GetSnapshot(actor);
            viewerChecks++;
            CheckViewerBoundary(snapshot, state, actor, issues, ref hiddenLeaks);
            lastLegalActions = string.Join(",", snapshot.LegalActions.Select(action =>
                action.Type + "/" + action.ActionId));
            actionsTakenThisTurn = coordinators[actor].ActionsTakenThisTurn;

            host.ClearLastSubmission();
            var submitted = coordinators[actor].Pump();
            if (!submitted)
            {
                if (coordinators[actor].Halted)
                {
                    haltReason = coordinators[actor].LastReasonKey;
                    var rejected = host.LastSubmission;
                    if (rejected is not null && !rejected.Result.Accepted)
                    {
                        issues.Add(
                            actorFaction + " vs " + foeFaction
                            + ": rejected " + host.LastActionId
                            + " reason=" + rejected.Result.ReasonKey);
                    }
                }

                break;
            }

            actions++;
        }

        if (!state.WinnerPlayerIndex.HasValue && haltReason is null && actions >= StepLimit)
        {
            stepLimit = true;
        }

        var finalSnapshot = gateway.GetSnapshot(state.CurrentPlayerIndex);
        var terminal = state.WinnerPlayerIndex.HasValue
            && string.Equals(finalSnapshot.Phase, "OVER", StringComparison.Ordinal);
        if (!terminal)
        {
            issues.Add(
                actorFaction + " vs " + foeFaction
                + ": match did not reach OVER; halt=" + (haltReason ?? "none")
                + " phase=" + finalSnapshot.Phase
                + " lastLegal=" + (lastLegalActions ?? "none"));
        }

        return new MatchReport(
            actorFaction,
            foeFaction,
            terminal,
            state.WinnerPlayerIndex,
            state.WinReason,
            finalSnapshot.Phase,
            state.Turn.Number,
            actions,
            host.Rejected,
            stepLimit,
            haltReason,
            viewerChecks,
            hiddenLeaks,
            host.LastActionId,
            lastLegalActions,
            actionsTakenThisTurn,
            issues);
    }

    private static void CheckViewerBoundary(
        RuntimeSnapshotEnvelope snapshot,
        GameState state,
        int viewer,
        List<string> issues,
        ref int hiddenLeaks)
    {
        if (snapshot.Players.Count != 2)
        {
            issues.Add("viewer snapshot did not contain two players");
            return;
        }

        if (!string.Equals(snapshot.ViewerPlayerId, "player_" + viewer, StringComparison.Ordinal))
        {
            issues.Add("viewer identity mismatch: expected player_" + viewer
                + " actual=" + snapshot.ViewerPlayerId);
        }

        var own = snapshot.Players[viewer];
        var opponent = snapshot.Players[1 - viewer];
        if (own.Hand.Count != state.GetPlayer(viewer).Hand.Count
            || own.Ambush.Count != state.GetPlayer(viewer).AmbushZone.Count)
        {
            issues.Add("viewer " + viewer + " own hidden counts mismatch");
        }

        if (opponent.Hand.Count != 0 || opponent.Ambush.Count != 0)
        {
            hiddenLeaks++;
            issues.Add(
                "viewer " + viewer + " exposed opponent hidden cards: hand="
                + opponent.Hand.Count + " ambush=" + opponent.Ambush.Count);
        }
    }

    private static MatchDeckSpec Spec(DeckDefinition deck)
        => new MatchDeckSpec(deck.Leader, deck.Cards, deck.Name, deck.Faction);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data", "cards")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class ProbeHost : IAiTurnHost
    {
        private readonly RuntimeMatchGateway _gateway;

        public ProbeHost(RuntimeMatchGateway gateway)
        {
            _gateway = gateway;
        }

        public RuntimeActionSubmission? LastSubmission { get; private set; }
        public string? LastActionId { get; private set; }
        public int Rejected { get; private set; }

        public RuntimeSnapshotEnvelope GetSnapshotForViewer(int viewerPlayerIndex)
            => _gateway.GetSnapshot(viewerPlayerIndex);

        public RuntimeActionSubmission SubmitForViewer(
            RuntimeGameAction action,
            int presentationViewerIndex)
        {
            LastActionId = action.ActionId;
            LastSubmission = _gateway.Submit(action);
            if (!LastSubmission.Result.Accepted) Rejected++;
            return LastSubmission;
        }

        public void ClearLastSubmission()
        {
            LastSubmission = null;
            LastActionId = null;
        }
    }

    private sealed class MatchReport
    {
        public MatchReport(
            string actorFaction,
            string foeFaction,
            bool terminal,
            int? winner,
            string? reasonKey,
            string phase,
            int turn,
            int actions,
            int rejected,
            bool stepLimit,
            string? haltReason,
            int viewerChecks,
            int hiddenLeaks,
            string? lastActionId,
            string? lastLegalActions,
            int actionsTakenThisTurn,
            IReadOnlyList<string> issues)
        {
            ActorFaction = actorFaction;
            FoeFaction = foeFaction;
            Terminal = terminal;
            Winner = winner;
            ReasonKey = reasonKey;
            Phase = phase;
            Turn = turn;
            Actions = actions;
            Rejected = rejected;
            StepLimit = stepLimit;
            HaltReason = haltReason;
            ViewerChecks = viewerChecks;
            HiddenLeaks = hiddenLeaks;
            LastActionId = lastActionId;
            LastLegalActions = lastLegalActions;
            ActionsTakenThisTurn = actionsTakenThisTurn;
            Issues = issues;
        }

        public string ActorFaction { get; }
        public string FoeFaction { get; }
        public bool Terminal { get; }
        public int? Winner { get; }
        public string? ReasonKey { get; }
        public string Phase { get; }
        public int Turn { get; }
        public int Actions { get; }
        public int Rejected { get; }
        public bool StepLimit { get; }
        public string? HaltReason { get; }
        public int ViewerChecks { get; }
        public int HiddenLeaks { get; }
        public string? LastActionId { get; }
        public string? LastLegalActions { get; }
        public int ActionsTakenThisTurn { get; }
        public IReadOnlyList<string> Issues { get; }
    }
}

}
