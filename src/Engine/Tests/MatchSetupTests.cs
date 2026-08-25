using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Data;
using DominionWars.Engine.Events;
using DominionWars.Engine.Model;
using DominionWars.Engine.Setup;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class MatchSetupTests
{
    [Test]
    public void PrebuiltDecksCreateDeterministicOpeningState()
    {
        var root = FindRepositoryRoot();
        var catalog = CardCatalog.LoadDirectory(Path.Combine(root, "data", "cards"));
        var decks = DeckLoader.LoadDirectory(Path.Combine(root, "data", "decks"));
        var first = ToSpec(decks[0]);
        var second = ToSpec(decks[1]);
        var options = new MatchSetupOptions { Seed = 90210, FirstPlayerIndex = 1 };

        var left = MatchSetup.Create(first, second, catalog.Cards, options);
        var right = MatchSetup.Create(first, second, catalog.Cards, new MatchSetupOptions
        {
            Seed = 90210,
            FirstPlayerIndex = 1,
        });

        Assert.Multiple(() =>
        {
            Assert.That(left.CurrentPlayerIndex, Is.EqualTo(1));
            Assert.That(left.Turn.PhaseId, Is.EqualTo(TurnPhase.Start));
            Assert.That(left.CastleEnabled, Is.True);
            Assert.That(left.CastleHealth, Is.EqualTo(75));
            Assert.That(ZoneSignature(left), Is.EqualTo(ZoneSignature(right)));
            Assert.That(left.Events.Items.Any(item => item.EventType == "MATCH_STARTED"), Is.True);
        });
    }

    [Test]
    public void OpeningHandAndLeaderArePopulatedWithoutDuplicateInstances()
    {
        var root = FindRepositoryRoot();
        var catalog = CardCatalog.LoadDirectory(Path.Combine(root, "data", "cards"));
        var decks = DeckLoader.LoadDirectory(Path.Combine(root, "data", "decks"));
        var state = MatchSetup.Create(ToSpec(decks[0]), ToSpec(decks[1]), catalog.Cards);

        var all = state.Players.SelectMany(player => player.Deck
            .Concat(player.Hand).Concat(player.Field).Concat(player.Graveyard)).ToArray();
        var leaders = all.Where(card => card.Definition.IsLeader).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Length.EqualTo(122));
            Assert.That(all.Select(card => card.InstanceId).Distinct().Count(), Is.EqualTo(all.Length));
            Assert.That(leaders, Has.Length.EqualTo(2));
            Assert.That(state.Players[0].Hand.Count + state.Players[0].Field.Count, Is.EqualTo(5));
            Assert.That(state.Players[1].Hand.Count + state.Players[1].Field.Count, Is.EqualTo(5));
            Assert.That(state.Events.Items.Count, Is.GreaterThanOrEqualTo(3));
        });
    }

    [Test]
    public void UnknownCardAndLeaderFailClosed()
    {
        var definition = new CardDefinition("soldier", "Soldier", attack: 1, health: 1, isMinion: true);
        var leader = new CardDefinition("leader", "Leader", attack: 1, health: 3, isMinion: true, isLeader: true);
        var cards = new Dictionary<string, CardDefinition>
        {
            [definition.Id] = definition,
            [leader.Id] = leader,
        };
        var valid = new MatchDeckSpec("leader", new Dictionary<string, int> { ["soldier"] = 1 });
        var invalid = new MatchDeckSpec("missing_leader", new Dictionary<string, int>());
        Assert.Throws<InvalidDataException>(() => MatchSetup.Create(valid, invalid, cards));
        Assert.Throws<InvalidDataException>(() => MatchSetup.Create(
            valid,
            new MatchDeckSpec("leader", new Dictionary<string, int> { ["missing"] = 1 }),
            cards));
    }

    private static MatchDeckSpec ToSpec(DeckDefinition deck)
    {
        return new MatchDeckSpec(deck.Leader, deck.Cards, deck.Name, deck.Faction);
    }

    private static string ZoneSignature(GameState state)
    {
        return string.Join("|", state.Players.SelectMany(player => new[]
        {
            string.Join(",", player.Deck.Select(card => card.Definition.Id + ":" + card.InstanceId)),
            string.Join(",", player.Hand.Select(card => card.Definition.Id + ":" + card.InstanceId)),
            string.Join(",", player.Field.Select(card => card.Definition.Id + ":" + card.InstanceId)),
        }));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docs", "RULES.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new AssertionException("Repository root was not found.");
    }
}
}
