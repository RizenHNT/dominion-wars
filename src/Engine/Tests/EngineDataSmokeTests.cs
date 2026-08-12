using System;
using System.IO;
using System.Linq;
using DominionWars.Data;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class EngineDataSmokeTests
{
    [Test]
    public void EveryPrebuiltDeckResolvesAgainstTheCardCatalog()
    {
        var root = FindRepositoryRoot();
        var catalog = CardCatalog.LoadDirectory(Path.Combine(root, "data", "cards"));
        var decks = DeckLoader.LoadDirectory(Path.Combine(root, "data", "decks"));

        Assert.That(decks, Has.Count.EqualTo(4));
        foreach (var deck in decks)
        {
            Assert.That(catalog.Cards.ContainsKey(deck.Leader), Is.True, deck.Name);
            foreach (var cardId in deck.Cards.Keys)
            {
                Assert.That(catalog.Cards.ContainsKey(cardId), Is.True, $"{deck.Name}: {cardId}");
            }
        }
    }

    [Test]
    public void PrebuiltDecksCanPopulateEngineZonesWithoutRuleDuplication()
    {
        var root = FindRepositoryRoot();
        var catalog = CardCatalog.LoadDirectory(Path.Combine(root, "data", "cards"));
        var decks = DeckLoader.LoadDirectory(Path.Combine(root, "data", "decks"));
        var state = new GameState(
            new PlayerState(0, 20),
            new PlayerState(1, 20),
            cardLibrary: catalog.Cards.Values);
        var nextId = 1000L;

        foreach (var deck in decks.Take(2))
        {
            var player = state.GetPlayer(deck == decks[0] ? 0 : 1);
            foreach (var entry in deck.Cards)
            {
                for (var count = 0; count < entry.Value; count++)
                {
                    player.Deck.Add(new CardInstance(nextId++, player.PlayerIndex, catalog.Cards[entry.Key]));
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(state.Players[0].Deck, Is.Not.Empty);
            Assert.That(state.Players[1].Deck, Is.Not.Empty);
            Assert.That(state.Players.SelectMany(player => player.Deck).Count(), Is.GreaterThan(0));
        });
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
}
}
