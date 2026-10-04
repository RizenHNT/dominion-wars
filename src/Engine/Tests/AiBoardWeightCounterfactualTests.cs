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
/// THE `board` COUNTERFACTUAL: WHAT MAKES A TURN A DECISION RATHER THAN A FORMALITY?
///
/// THE PROBLEM THIS ADDRESSES, measured in the sibling fixture: the shipped default gives `board` 500
/// to ANY card play while `punish` weighs 0, so playing everything affordable is always best and a
/// turn stops being a decision. Measured outcome: 12.89 actions per turn, and a turn was ended while
/// actions were still advertised at most twice in sixteen matches. Three deck rhythms ("mid-range",
/// "late game", "early upload then burst") cannot coexist when every turn is the same turn.
///
/// WHAT IT DOES. Rebuilds the default weight table with `board` and `punish` moved, runs the SAME
/// deck pairings at the SAME seeds under each table, and reports the shape that follows. Candidate
/// tables are constructed in the test; no production weight is touched, because the shipped default is
/// the opponent the historical measurements were taken against and changing it is the owner's call.
///
/// WHY THE EXPECTED DIRECTION IS PLAUSIBLE, so the numbers can be judged rather than just read: a card
/// play's cost is the `punish` it pays, so a negative `punish` weight makes an EXPENSIVE play score
/// lower than ending the turn. That is the only mechanism in the weight table that can make "stop
/// developing" beat "develop more", which is the decision a turn is supposed to contain.
///
/// IT ASSERTS NO TARGET. Whether the shipped default should change is a product decision. The one
/// non-vacuity requirement is that the sweep actually produced turns and actions to compare.
/// </summary>
public sealed class AiBoardWeightCounterfactualTests
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

    private sealed class Table
    {
        public string Label = string.Empty;
        public int Board;
        public int Punish;
        public WeightedPlaystyle Playstyle = null!;
    }

    private sealed class Shape
    {
        public int Matches;
        public int Finished;
        public int Turns;
        public int Actions;
        public int EndTurnChosenWithAlternatives;
        public readonly Dictionary<string, int> WinReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly List<string> UnfinishedTraces = new List<string>();
        public long AxisTotal;

        public double ActionsPerTurn => Turns == 0 ? 0 : (double)Actions / Turns;
        public double MeanTurns => Matches == 0 ? 0 : (double)Turns / Matches;
    }

    private static WeightedPlaystyle Variant(string id, int board, int punish)
    {
        var weights = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [ActionFeatures.Lifecycle] = 300,
            [ActionFeatures.WinAxis] = 700,
            [ActionFeatures.ChainReverse] = -300,
            [ActionFeatures.Board] = board,
            [ActionFeatures.Ambush] = 0,
            [ActionFeatures.SpendCard] = 0,
            [ActionFeatures.SpendField] = -60,
            [ActionFeatures.Damage] = 120,
            [ActionFeatures.Face] = 0,
            [ActionFeatures.Trade] = 0,
            [ActionFeatures.Punish] = punish,
            [ActionFeatures.DiscardCost] = 0,
            [ActionFeatures.EndTurn] = 50,
        };
        return new WeightedPlaystyle(id, PunishResponseStance.Never, weights);
    }

    private static Shape RunTable(Table table, string[] factions, int seeds)
    {
        var catalog = Catalog();
        var shape = new Shape();

        foreach (var actor in factions)
        {
            foreach (var foe in factions)
            {
                var actorDeck = Spec(Deck(actor));
                var foeDeck = Spec(Deck(foe));

                for (var seedIndex = 0; seedIndex < seeds; seedIndex++)
                {
                    var state = MatchSetup.Create(
                        actorDeck,
                        foeDeck,
                        catalog.Cards,
                        new MatchSetupOptions
                        {
                            Seed = (ulong)(9000 + seedIndex),
                            FirstPlayerIndex = 0,
                            OpeningHandSize = 5,
                            CastleEnabled = true,
                            CastleHealth = 75,
                        });

                    var flow = TurnFlow.CreateDefault();
                    var router = TurnActionRouter.CreateDefault(
                        flow, null, PunishResponseStances.PolicyFor(table.Playstyle));
                    var gateway = new RuntimeMatchGateway("match_board_cf", state, flow, router, null);
                    if (!gateway.Initialize(0).Accepted) continue;

                    shape.Matches++;
                    var policy = new AdvertisedActionPolicy(table.Playstyle);
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
                        }

                        var chosen = policy.TryChoose(snapshot, snapshot.CurrentPlayer, false, out var pick) && pick is not null
                            ? pick
                            : snapshot.LegalActions[0];

                        if (string.Equals(chosen.Type, "END_TURN", StringComparison.Ordinal)
                            && snapshot.LegalActions.Count > 1)
                        {
                            shape.EndTurnChosenWithAlternatives++;
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

                    shape.AxisTotal += state.GetPlayer(0).TotalDiscarded;

                    if (state.WinnerPlayerIndex.HasValue)
                    {
                        shape.Finished++;
                        var reason = state.WinReason ?? "(none)";
                        shape.WinReasons.TryGetValue(reason, out var seen);
                        shape.WinReasons[reason] = seen + 1;
                    }
                    else
                    {
                        // WHY IT DID NOT FINISH, and what the endgame looked like. At the lowest table
                        // the turn count jumps by an order of magnitude, so the difference between
                        // "a genuine long game" and "a loop that never resolves" has to be visible.
                        shape.UnfinishedTraces.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} vs {1} seed {2}: turn {3}, actions {4}, seat0 discarded {5}, seat0 pull {6}, field {7}/{8}",
                            actor, foe, 9000 + seedIndex, state.Turn.Number, actionsThisTurn,
                            state.GetPlayer(0).TotalDiscarded, state.GetPlayer(0).PullCount,
                            state.GetPlayer(0).Field.Count, state.GetPlayer(1).Field.Count));
                    }
                }
            }
        }

        return shape;
    }

    [Test]
    public void MeasuresHowBoardAndPunishWeightsChangeTheShapeOfATurn()
    {
        var tables = new List<Table>
        {
            new Table { Label = "shipped      board=500 punish=0" },
            new Table { Label = "punish only  board=500 punish=-2" },
            new Table { Label = "board 400    punish=-2" },
            new Table { Label = "board 250    punish=-2" },
            new Table { Label = "board 150    punish=-2" },
            new Table { Label = "board 100    punish=-2" },
            new Table { Label = "board 50     punish=-2" },
        };
        var boardValues = new[] { 500, 500, 400, 250, 150, 100, 50 };
        var punishValues = new[] { 0, -2, -2, -2, -2, -2, -2 };
        for (var index = 0; index < tables.Count; index++)
        {
            tables[index].Board = boardValues[index];
            tables[index].Punish = punishValues[index];
            tables[index].Playstyle = Variant("cf_" + boardValues[index] + "_" + punishValues[index],
                boardValues[index], punishValues[index]);
        }

        var factions = new[] { "flame", "machine", "sea", "wood" };
        const int seeds = 2;

        TestContext.Out.WriteLine(
            "BOARD/PUNISH COUNTERFACTUAL (same 16 pairings x {0} seeds per table, both seats on the table)",
            seeds);
        TestContext.Out.WriteLine(
            "  {0,-34} {1,6} {2,7} {3,7} {4,9} {5,8} {6}",
            "table", "turns/m", "act/turn", "stop", "finished", "axis(seat0)", "distinct end reasons");

        var shapes = new List<(Table Table, Shape Shape)>();
        foreach (var table in tables)
        {
            var shape = RunTable(table, factions, seeds);
            shapes.Add((table, shape));
            TestContext.Out.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0,-34} {1,6} {2,7} {3,7} {4,9} {5,8} {6}",
                table.Label,
                shape.MeanTurns.ToString("F1", CultureInfo.InvariantCulture),
                shape.ActionsPerTurn.ToString("F2", CultureInfo.InvariantCulture),
                shape.EndTurnChosenWithAlternatives,
                shape.Finished + "/" + shape.Matches,
                shape.AxisTotal,
                shape.WinReasons.Count));
        }

        TestContext.Out.WriteLine("  end-reason breakdown per table:");
        foreach (var (table, shape) in shapes)
        {
            TestContext.Out.WriteLine("    [{0}]", table.Label);
            foreach (var entry in shape.WinReasons.OrderByDescending(e => e.Value))
            {
                TestContext.Out.WriteLine("      {0,3}x  {1}", entry.Value, entry.Key);
            }

            if (shape.WinReasons.Count == 0) TestContext.Out.WriteLine("      (no match finished)");
            foreach (var trace in shape.UnfinishedTraces.Take(3))
            {
                TestContext.Out.WriteLine("      UNFINISHED: {0}", trace);
            }
        }

        // NON-VACUITY: the sweep must have produced turns and actions to compare at all.
        var totalTurns = shapes.Sum(s => s.Shape.Turns);
        var totalActions = shapes.Sum(s => s.Shape.Actions);
        Assert.That(shapes.Count, Is.EqualTo(tables.Count), "every table must be measured");
        Assert.That(totalTurns, Is.GreaterThan(0), "the sweep must produce turns");
        Assert.That(totalActions, Is.GreaterThan(0), "the sweep must produce actions");
    }
}
}
