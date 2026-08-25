using System.IO;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Data;
using DominionWars.Engine.Setup;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class MatchFactoryTests
{
    [Test]
    public void FactoryComposesLoadedDecksIntoRuntimeGateway()
    {
        var root = FindRepositoryRoot();
        var catalog = CardCatalog.LoadDirectory(Path.Combine(root, "data", "cards"));
        var decks = DeckLoader.LoadDirectory(Path.Combine(root, "data", "decks"));
        var gateway = MatchFactory.CreateFromDecks(
            "match_prebuilt",
            catalog,
            decks[0],
            decks[1],
            new MatchSetupOptions { Seed = 77 });

        var snapshot = gateway.GetSnapshot(0);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.MatchId, Is.EqualTo("match_prebuilt"));
            Assert.That(snapshot.SnapshotRevision, Is.EqualTo(0));
            Assert.That(snapshot.Phase, Is.EqualTo("START"));
            Assert.That(snapshot.Castle.Enabled, Is.True);
            Assert.That(snapshot.Castle.Health, Is.EqualTo(75));
            Assert.That(snapshot.Players, Has.Count.EqualTo(2));
            Assert.That(snapshot.Players[0].Hand.Count + snapshot.Players[0].Field.Count, Is.EqualTo(5));
            Assert.That(snapshot.Players.SelectMany(player => player.Hand).Any(), Is.True);
        });
    }

    [Test]
    public void InitializedFactoryEntersFirstPlayablePhase()
    {
        var root = FindRepositoryRoot();
        var catalog = CardCatalog.LoadDirectory(Path.Combine(root, "data", "cards"));
        var decks = DeckLoader.LoadDirectory(Path.Combine(root, "data", "decks"));
        var gateway = MatchFactory.CreateInitializedFromDecks(
            "match_prebuilt_initialized",
            catalog,
            decks[0],
            decks[1],
            new MatchSetupOptions { Seed = 77 });

        var snapshot = gateway.GetSnapshot(0);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.SnapshotRevision, Is.EqualTo(1));
            Assert.That(snapshot.Phase, Is.EqualTo("AMBUSH"));
            Assert.That(snapshot.LegalActions, Has.Count.EqualTo(1));
            Assert.That(snapshot.LegalActions[0].Type, Is.EqualTo("SKIP_AMBUSH"));
        });
    }

    [Test]
    public void DataBackedGatewayCompletesAFullTurnAndAdvertisesTheNextPlayer()
    {
        var root = FindRepositoryRoot();
        var catalog = CardCatalog.LoadDirectory(Path.Combine(root, "data", "cards"));
        var decks = DeckLoader.LoadDirectory(Path.Combine(root, "data", "decks"));
        var gateway = MatchFactory.CreateInitializedFromDecks(
            "match_full_turn",
            catalog,
            decks[0],
            decks[1],
            new MatchSetupOptions { Seed = 1138, FirstPlayerIndex = 0 });

        var ambush = gateway.GetSnapshot(0);
        var skipped = Submit(gateway, ambush, ambush.LegalActions.Single(
            action => action.Type == "SKIP_AMBUSH"));
        var ended = Submit(gateway, skipped.Snapshot, skipped.Snapshot.LegalActions.Single(
            action => action.Type == "END_TURN"));
        var discarded = Submit(gateway, ended.Snapshot, ended.Snapshot.LegalActions.Single(
            action => action.Type == "DISCARD"));
        var incoming = gateway.GetSnapshot(1);

        Assert.Multiple(() =>
        {
            Assert.That(skipped.Result.Accepted, Is.True);
            Assert.That(ended.Result.Accepted, Is.True);
            Assert.That(discarded.Result.Accepted, Is.True);
            Assert.That(discarded.Snapshot.CurrentPlayer, Is.EqualTo(1));
            Assert.That(discarded.Snapshot.Phase, Is.EqualTo("AMBUSH"));
            Assert.That(discarded.Snapshot.SnapshotRevision, Is.EqualTo(4));
            Assert.That(discarded.Snapshot.LegalActions, Is.Empty,
                "The submission response remains scoped to the outgoing viewer.");
            Assert.That(incoming.LegalActions.Any(
                action => action.Type == "SKIP_AMBUSH" && action.Actor == 1), Is.True);
            Assert.That(incoming.Players[1].Hand.Count + incoming.Players[1].Field.Count,
                Is.EqualTo(7), "The second player's first START draw is resolved before actions are advertised.");
            Assert.That(discarded.Events.Select(item => item.EventType), Does.Contain("TURN_STARTED"));
        });
    }

    private static RuntimeActionSubmission Submit(
        RuntimeMatchGateway gateway,
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction action)
    {
        return gateway.Submit(new RuntimeGameAction
        {
            MatchId = snapshot.MatchId,
            SnapshotRevision = snapshot.SnapshotRevision,
            ActionId = action.ActionId,
            Type = action.Type,
            Actor = action.Actor,
            SourceId = action.SourceId,
            TargetId = action.TargetId,
            CardId = action.CardId,
            Payload = action.Payload,
        });
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
