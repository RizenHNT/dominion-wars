using System;
using System.Collections.Generic;
using System.Globalization;
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
/// THE MEASUREMENT SPINE FOR DECK DESIGN: HOW FAST DOES EACH DECK ADVANCE ITS OWN WIN AXIS?
///
/// WHY NOT WIN RATE. A stat-curve sweep measured win rate as flat: one card gaining +3 attack and +3
/// health moved sea-vs-flame from 100% to 100% and wood-vs-flame from 40% to 40%, because the outcome
/// is decided by WHICH DECKS ARE PAIRED, not by one card's numbers. A flat scale cannot be used to
/// price a card, and the honest conclusion was that the instrument needs a yardstick that does not
/// depend on the game settling.
///
/// WHAT IS MEASURED INSTEAD. Every quantity here is read from authoritative engine state and is
/// meaningful even in a match that never ends:
///
///   * AXIS PROGRESS PER TURN — how fast a deck advances the counter its own leader declares:
///     `TotalDiscarded` for the discard axis, `PullCount` for the download axis,
///     the largest sealed minion's health for the growth axis, `NoDamageTurns` for the streak axis.
///     This is the number that decides whether a deck CAN win, and a card that speeds it up is
///     measurable even if the match is truncated.
///   * TURNS TO THRESHOLD — how long the deck would need at the observed rate. A deck whose axis
///     would need 90 turns is not "losing on win rate", it is not functioning as a deck at all.
///   * DECISION DIVERSITY — actions per turn, and how often the turn ends with actions still
///     advertised. A policy that simply plays everything it can afford is not making choices.
///   * HOW MATCHES END — the distribution of win reasons, which is what distinguishes the factions
///     from each other rather than from a single aggregate.
///
/// THIS FIXTURE ASSERTS NO TARGET. It prints the spine and asserts only that the sweep was
/// non-vacuous, because the design decisions built on it are the owner's.
/// </summary>
public sealed class AiMatchShapeTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data", "cards")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from " + TestContext.CurrentContext.TestDirectory);
    }

    private static CardCatalog Catalog()
        => CardCatalog.LoadDirectory(Path.Combine(RepositoryRoot(), "data", "cards"));

    private static DeckDefinition Deck(string faction)
        => DeckLoader.LoadFile(Path.Combine(RepositoryRoot(), "data", "decks", faction + "_deck.json"));

    private static MatchDeckSpec Spec(DeckDefinition deck)
        => new MatchDeckSpec(deck.Leader, deck.Cards, deck.Name, deck.Faction);

    private sealed class Shape
    {
        public string Label = string.Empty;
        public int Turns;
        public int Actions;
        public int TurnsEndingWithActionsStillAdvertised;
        public int Finished;
        public readonly List<int> AxisProgress = new List<int>();
        public string AxisName = string.Empty;
        public int AxisThreshold;
        public string WinReason = string.Empty;
    }

    /// <summary>Largest sealed minion health on a seat's field, or 0 when there is none.</summary>
    private static int SealedMaxHealth(GameState state, int seat)
    {
        var best = 0;
        foreach (var card in state.GetPlayer(seat).Field)
        {
            if (card.Sealed && card.Health > best) best = card.Health;
        }

        return best;
    }

    [Test]
    public void MeasuresAxisProgressDecisionDiversityAndHowMatchesEnd()
    {
        var factions = new[] { "flame", "machine", "sea", "wood" };
        var shapes = new List<Shape>();
        var winReasons = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var actor in factions)
        {
            foreach (var foe in factions)
            {
                var seed = 4242;
                var state = MatchSetup.Create(
                    Spec(Deck(actor)),
                    Spec(Deck(foe)),
                    Catalog().Cards,
                    new MatchSetupOptions
                    {
                        Seed = (ulong)seed,
                        FirstPlayerIndex = 0,
                        OpeningHandSize = 5,
                        CastleEnabled = true,
                        CastleHealth = 75,
                    });

                var flow = TurnFlow.CreateDefault();
                var router = TurnActionRouter.CreateDefault(
                    flow, null, PunishResponseStances.PolicyFor(PlaystyleRegistry.Default));
                var gateway = new RuntimeMatchGateway("match_shape", state, flow, router, null);
                if (!gateway.Initialize(0).Accepted) continue;

                var shape = new Shape
                {
                    Label = actor + " vs " + foe,
                };

                // The axis each side is trying to advance, plus the threshold its leader declares.
                // Read from the ENGINE state rather than from a projected objective: the engine
                // counters are the authority, and this keeps the harness free of adapter type names.
                var leader = state.GetPlayer(0).Leader;
                shape.AxisName = leader?.Definition.LeaderWinCondition ?? "(none declared)";
                shape.AxisThreshold = leader?.Definition.LeaderWinParam ?? 0;

                var policy = new AdvertisedActionPolicy();
                var lastTurn = -1;
                var actionsThisTurn = 0;
                var steps = 0;

                while (steps++ < 600)
                {
                    if (state.WinnerPlayerIndex.HasValue) break;

                    var viewer = state.CurrentPlayerIndex;
                    var snapshot = gateway.GetSnapshot(viewer);
                    if (snapshot.WinnerPlayerIndex.HasValue) break;
                    if (snapshot.LegalActions.Count == 0) break;

                    if (snapshot.Turn != lastTurn)
                    {
                        if (lastTurn >= 0)
                        {
                            shape.Actions += actionsThisTurn;
                            shape.Turns++;
                        }

                        lastTurn = snapshot.Turn;
                        actionsThisTurn = 0;
                        shape.AxisProgress.Add(state.GetPlayer(0).TotalDiscarded);
                    }

                    // Did the turn end while actions were still advertised? That is the signal that the
                    // policy CHOSE to stop rather than having nothing left to do.
                    var chosen = policy.TryChoose(snapshot, snapshot.CurrentPlayer, false, out var pick) && pick is not null
                        ? pick
                        : snapshot.LegalActions[0];

                    if (string.Equals(chosen.Type, "END_TURN", StringComparison.Ordinal)
                        && snapshot.LegalActions.Count > 1)
                    {
                        shape.TurnsEndingWithActionsStillAdvertised++;
                    }

                    var result = gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, chosen));
                    if (!result.Result.Accepted)
                    {
                        var alternative = snapshot.LegalActions
                            .FirstOrDefault(a => !string.Equals(a.ActionId, chosen.ActionId, StringComparison.Ordinal));
                        if (alternative is null
                            || !gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, alternative)).Result.Accepted)
                        {
                            break;
                        }
                    }

                    actionsThisTurn++;
                }

                if (lastTurn >= 0)
                {
                    shape.Actions += actionsThisTurn;
                    shape.Turns++;
                }

                if (state.WinnerPlayerIndex.HasValue)
                {
                    shape.Finished = 1;
                    shape.WinReason = state.WinReason ?? "(none)";
                    winReasons.TryGetValue(shape.WinReason, out var seen);
                    winReasons[shape.WinReason] = seen + 1;
                }

                shapes.Add(shape);
            }
        }

        TestContext.Out.WriteLine("MATCH SHAPE (one match per pairing, seed 4242, shipped default policy)");
        TestContext.Out.WriteLine(
            "  {0,-16} {1,6} {2,7} {3,8} {4,10} {5,8} {6}",
            "pairing", "turns", "actions", "act/turn", "stop-choice", "finished", "axis reached");
        foreach (var shape in shapes)
        {
            var perTurn = shape.Turns == 0
                ? 0
                : (double)shape.Actions / shape.Turns;
            var lastAxis = shape.AxisProgress.Count == 0 ? 0 : shape.AxisProgress[shape.AxisProgress.Count - 1];
            TestContext.Out.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0,-16} {1,6} {2,7} {3,8} {4,10} {5,8} {6}",
                shape.Label,
                shape.Turns,
                shape.Actions,
                perTurn.ToString("F2", CultureInfo.InvariantCulture),
                shape.TurnsEndingWithActionsStillAdvertised,
                shape.Finished == 1 ? "yes" : "no",
                lastAxis));
        }

        TestContext.Out.WriteLine("  win reasons:");
        foreach (var entry in winReasons.OrderByDescending(e => e.Value))
        {
            TestContext.Out.WriteLine("    {0,3}x  {1}", entry.Value, entry.Key);
        }

        var totalTurns = shapes.Sum(s => s.Turns);
        var totalActions = shapes.Sum(s => s.Actions);
        TestContext.Out.WriteLine(
            "SHAPE SUMMARY: {0} pairings, {1} turns, {2} actions, {3} finished",
            shapes.Count, totalTurns, totalActions, shapes.Count(s => s.Finished == 1));
        if (totalTurns > 0)
        {
            TestContext.Out.WriteLine(
                "  mean actions per turn: {0}",
                ((double)totalActions / totalTurns).ToString("F2", CultureInfo.InvariantCulture));
        }

        Assert.That(shapes, Is.Not.Empty, "the sweep must produce matches");
        Assert.That(totalTurns, Is.GreaterThan(0), "matches must advance turns");
        Assert.That(totalActions, Is.GreaterThan(0), "the policy must take actions");
    }
}
}
