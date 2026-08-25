#if UNITY_INCLUDE_TESTS
#nullable enable annotations

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DominionWars.Adapters;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace DominionWars.Unity.EditMode
{

/// <summary>
/// Proves the U-03 mechanical lifecycle with a clearly non-authoritative
/// Unity-only data fixture. The production 91-card data intentionally has no
/// COMMIT/PUSH/PULL cards yet, so this test injects a tiny fixture through the
/// existing RuntimeBootstrap dataRoot field and still submits only actions
/// advertised by the real gateway snapshot.
/// </summary>
[TestFixture]
public sealed class RuntimePullLifecycleFixtureEditModeTests
{
    private const string CarrierCardId = "u03_fixture_machine_carrier";
    private const string CommitCardId = "u03_fixture_machine_commit";

    [Test]
    public void NonAuthoritativeFixtureCompletesCommitPushPullAndGraveyardMove()
    {
        GameObject? gameObject = null;
        RuntimeAdapter? adapter = null;
        try
        {
            gameObject = new GameObject("u03-non-authoritative-pull-fixture");
            // Awake starts the real bootstrap session. Configure the private
            // serialized fields while inactive so no production session starts.
            gameObject.SetActive(false);
            var bootstrap = gameObject.AddComponent<RuntimeBootstrap>();
            SetPrivateField(bootstrap, "dataRoot", FixtureRoot());
            SetPrivateField(bootstrap, "player0DeckIndex", 0);
            SetPrivateField(bootstrap, "player1DeckIndex", 1);
            SetPrivateField(bootstrap, "firstPlayerIndex", 0);
            SetPrivateField(bootstrap, "openingHandSize", 3);
            SetPrivateField(bootstrap, "seed", 1);
            // EditMode does not guarantee that an Awake scheduled by
            // SetActive(true) has run before the next statement. Start the
            // configured public composition root explicitly so the test reads
            // the real gateway snapshot rather than a not-yet-started adapter.
            bootstrap.StartSession();

            adapter = bootstrap.Adapter;
            Assert.That(adapter, Is.Not.Null);
            var snapshot = adapter!.Presentation.Snapshot;
            Assert.That(snapshot, Is.Not.Null);
            WriteSnapshot("initialized", snapshot!);
            Assert.That(snapshot!.ViewerPlayerId, Is.EqualTo("player_0"));
            Assert.That(snapshot.Phase, Is.EqualTo("AMBUSH"));

            Submit(adapter, FindAction(snapshot, "SKIP_AMBUSH"));
            snapshot = adapter.Presentation.Snapshot!;

            var carrierPlay = FindAction(
                snapshot,
                "PLAY_CARD",
                action => action.CardId == CarrierCardId);
            Submit(adapter, carrierPlay);
            snapshot = adapter.Presentation.Snapshot!;

            var commitPlay = FindAction(
                snapshot,
                "PLAY_CARD",
                action => action.CardId == CommitCardId);
            var commitSubmission = Submit(adapter, commitPlay);
            Assert.That(
                commitSubmission.Events.Select(item => item.EventType),
                Does.Contain("CARD_COMMITTED"));
            snapshot = adapter.Presentation.Snapshot!;

            var player0AfterCommit = Player(snapshot, 0);
            Assert.That(player0AfterCommit.CommitQueueCount, Is.EqualTo(1));
            Assert.That(player0AfterCommit.CloudStackCount, Is.Zero);

            // EndTurn -> DISCARD -> END is the normal engine route. The
            // discard action has requiredCount=0 for this fixture, and its
            // existing handler advances START into the next AMBUSH phase.
            Submit(adapter, FindAction(snapshot, "END_TURN"));
            snapshot = adapter.Presentation.Snapshot!;
            var firstDiscard = FindAction(snapshot, "DISCARD");
            var pushSubmission = Submit(adapter, firstDiscard);
            Assert.That(
                pushSubmission.Events.Select(item => item.EventType),
                Does.Contain("CARD_PUSHED"));
            snapshot = adapter.Presentation.Snapshot!;
            Assert.That(snapshot.CurrentPlayer, Is.EqualTo(1));
            Assert.That(Player(snapshot, 0).CommitQueueCount, Is.Zero);
            Assert.That(Player(snapshot, 0).CloudStackCount, Is.EqualTo(1));

            // Hand off the opponent through the same advertised phase actions.
            adapter.RefreshSnapshot(1);
            snapshot = adapter.Presentation.Snapshot!;
            Submit(adapter, FindAction(snapshot, "SKIP_AMBUSH"));
            snapshot = adapter.Presentation.Snapshot!;
            Submit(adapter, FindAction(snapshot, "END_TURN"));
            snapshot = adapter.Presentation.Snapshot!;
            Submit(adapter, FindAction(snapshot, "DISCARD"));

            // The incoming START work is completed by the existing discard
            // handler; refresh the viewer only, without advancing the match.
            adapter.RefreshSnapshot(0);
            snapshot = adapter.Presentation.Snapshot!;
            WriteSnapshot("after-opponent-discard-refresh-player0", snapshot);
            Assert.That(snapshot.CurrentPlayer, Is.EqualTo(0));
            Assert.That(snapshot.Phase, Is.EqualTo("AMBUSH"));
            Submit(adapter, FindAction(snapshot, "SKIP_AMBUSH"));
            snapshot = adapter.Presentation.Snapshot!;

            var pull = FindAction(snapshot, "PULL");
            Assert.That(pull.SourceId, Is.Not.Null);
            Assert.That(pull.TargetId, Is.Not.Null);
            var pullSubmission = Submit(adapter, pull);

            Assert.That(
                pullSubmission.Events.Select(item => item.EventType),
                Is.EqualTo(new[] { "PULL_DECLARED", "CARD_PULLED" }));
            var finalPlayer0 = Player(pullSubmission.Snapshot, 0);
            Assert.That(finalPlayer0.CloudStackCount, Is.Zero);
            Assert.That(
                finalPlayer0.Graveyard.Any(card => card.CardId == CommitCardId),
                Is.True,
                "The pulled card must be in the player's graveyard.");
            Assert.That(finalPlayer0.PullCount, Is.EqualTo(1));
        }
        finally
        {
            adapter?.Dispose();
            if (gameObject is not null)
                UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    private static RuntimeActionSubmission Submit(
        RuntimeAdapter adapter,
        RuntimeLegalAction legal)
    {
        var snapshot = adapter.Presentation.Snapshot;
        Assert.That(snapshot, Is.Not.Null);
        WriteSnapshot("before-submit-" + legal.Type + "-" + legal.ActionId, snapshot!);
        var submission = adapter.Submit(
            RuntimeBattlePanelActionModel.ToGameAction(legal, snapshot!.MatchId));
        Diagnostic(
            "[U-03 PULL lifecycle] submit " + legal.Type + "#" + legal.ActionId +
            " accepted=" + submission.Result.Accepted +
            " reason=" + submission.Result.ReasonKey +
            " events=[" + string.Join(", ", submission.Events.Select(item => item.EventType)) + "]");
        WriteSnapshot("after-submit-" + legal.Type + "-" + legal.ActionId, submission.Snapshot);
        Assert.That(
            submission.Result.Accepted,
            Is.True,
            "Submit " + legal.Type + "#" + legal.ActionId + " rejected: " +
            submission.Result.ReasonKey + ". Before: " + DescribeSnapshot(snapshot!) +
            ". After: " + DescribeSnapshot(submission.Snapshot));
        return submission;
    }

    private static RuntimeLegalAction FindAction(
        RuntimeSnapshotEnvelope snapshot,
        string type,
        Func<RuntimeLegalAction, bool>? predicate = null)
    {
        var result = snapshot.LegalActions.FirstOrDefault(action =>
            action is not null
            && string.Equals(action.Type, type, StringComparison.Ordinal)
            && (predicate is null || predicate(action)));
        if (result is null)
        {
            var details = DescribeSnapshot(snapshot);
            Diagnostic(
                "[U-03 PULL lifecycle] Missing advertised action " + type + ". " + details);
            Assert.Fail("Missing advertised action " + type + ". " + details);
        }
        return result!;
    }

    private static void WriteSnapshot(
        string checkpoint,
        RuntimeSnapshotEnvelope snapshot)
    {
        Diagnostic(
            "[U-03 PULL lifecycle] " + checkpoint + ": " + DescribeSnapshot(snapshot));
    }

    private static void Diagnostic(string message)
    {
        TestContext.WriteLine(message);
        Debug.Log(message);
    }

    private static string DescribeSnapshot(RuntimeSnapshotEnvelope snapshot)
    {
        var actions = string.Join(", ", snapshot.LegalActions.Select(action =>
            action is null
                ? "<null>"
                : action.Type + "#" + action.ActionId +
                  (action.CardId is null ? string.Empty : " card=" + action.CardId) +
                  (action.SourceId is null ? string.Empty : " source=" + action.SourceId) +
                  (action.TargetId is null ? string.Empty : " target=" + action.TargetId)));
        var players = string.Join(" | ", snapshot.Players.Select(player =>
            player.PlayerId +
            " hand=" + player.HandCount +
            " field=" + player.FieldCount +
            " queue=" + player.CommitQueueCount +
            " cloud=" + player.CloudStackCount +
            " grave=" + player.GraveyardCount +
            " pull=" + player.PullCount));
        return "rev=" + snapshot.SnapshotRevision +
            " turn=" + snapshot.Turn +
            " phase=" + snapshot.Phase +
            " current=" + snapshot.CurrentPlayer +
            " viewer=" + snapshot.ViewerPlayerId +
            " actions=[" + actions + "]" +
            " players=[" + players + "]";
    }

    private static RuntimePlayerSnapshot Player(
        RuntimeSnapshotEnvelope snapshot,
        int playerIndex)
    {
        var player = snapshot.Players.Single(item => item.PlayerId == "player_" + playerIndex);
        Assert.That(player, Is.Not.Null);
        return player!;
    }

    private static string FixtureRoot()
    {
        return Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "DominionWars.Tests.EditMode",
            "Fixtures",
            "PullLifecycleData"));
    }

    private static void SetPrivateField<T>(
        RuntimeBootstrap bootstrap,
        string name,
        T value)
    {
        var field = typeof(RuntimeBootstrap).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "RuntimeBootstrap field not found: " + name);
        field!.SetValue(bootstrap, value);
    }
}
}
#endif
