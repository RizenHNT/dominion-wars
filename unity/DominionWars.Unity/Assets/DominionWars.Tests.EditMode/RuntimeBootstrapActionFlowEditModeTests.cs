#if UNITY_INCLUDE_TESTS
#nullable enable annotations

using System;
using System.Collections.Generic;
using DominionWars.Adapters;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeBootstrapActionFlowEditModeTests
{
    private const int MaxSteps = 64;

    [Test]
    public void RealBootstrapSmokeFollowsAdvertisedActionsWithoutViewerLeak()
    {
        GameObject? gameObject = null;
        RuntimeAdapter? adapter = null;
        try
        {
            gameObject = new GameObject("runtime-bootstrap-action-flow-test");
            var bootstrap = gameObject.AddComponent<RuntimeBootstrap>();
            bootstrap.StartSession();
            adapter = bootstrap.Adapter;

            Assert.That(adapter, Is.Not.Null);
            Assert.That(adapter!.Presentation.Snapshot, Is.Not.Null);

            var seenTypes = new HashSet<string>(StringComparer.Ordinal);
            var lastRevision = adapter.Presentation.Snapshot!.SnapshotRevision;
            var acceptedActions = 0;
            var invalidSubmissionChecked = false;
            var acceptedTargetedFlameBolt = false;
            var acceptedDiscardActions = 0;
            var viewerPlayerIndex = ParseViewer(adapter.Presentation.Snapshot);
            var reachedOverPhase = false;

            for (var step = 0; step < MaxSteps; step++)
            {
                var snapshot = adapter.Presentation.Snapshot;
                Assert.That(snapshot, Is.Not.Null);
                AssertViewerSnapshot(snapshot!, viewerPlayerIndex);

                if (snapshot!.CurrentPlayer != viewerPlayerIndex)
                {
                    viewerPlayerIndex = snapshot.CurrentPlayer;
                    adapter.RefreshSnapshot(viewerPlayerIndex);
                    snapshot = adapter.Presentation.Snapshot;
                    Assert.That(snapshot, Is.Not.Null);
                    AssertViewerSnapshot(snapshot!, viewerPlayerIndex);
                }

                if (snapshot!.Phase == "OVER")
                {
                    reachedOverPhase = true;
                    break;
                }

                if (snapshot.LegalActions is null || snapshot.LegalActions.Count == 0)
                {
                    // A terminal or otherwise action-less projection is an
                    // honest stopping point for this bounded smoke test. It
                    // must not be turned into a fabricated action.
                    TestContext.WriteLine(
                        "Action flow stopped without a legal action at phase " +
                        snapshot.Phase + ".");
                    break;
                }

                if (!invalidSubmissionChecked)
                {
                    AssertRejectedSubmissionDoesNotMutate(adapter, snapshot);
                    invalidSubmissionChecked = true;
                }

                var legal = ChooseAction(snapshot.LegalActions);
                Assert.That(legal, Is.Not.Null);
                Assert.That(legal!.Actor, Is.EqualTo(snapshot.CurrentPlayer));
                Assert.That(legal.SnapshotRevision, Is.EqualTo(snapshot.SnapshotRevision));

                var beforeRevision = snapshot.SnapshotRevision;
                var isTargetedFlameBolt = string.Equals(
                    legal.CardId,
                    "flame_bolt",
                    StringComparison.Ordinal);
                if (isTargetedFlameBolt)
                {
                    Assert.That(legal.SourceId, Is.EqualTo(22L).Or.EqualTo(23L),
                        "flame_bolt must preserve an engine-advertised entity id.");
                    Assert.That(legal.TargetId, Is.Not.Null,
                        "flame_bolt must carry an engine-advertised target.");
                }
                var submission = adapter.Submit(
                    RuntimeBattlePanelActionModel.ToGameAction(legal, snapshot.MatchId));

                Assert.That(submission.Result.Accepted, Is.True,
                    "An action advertised by the real RuntimeBootstrap must be accepted.");
                Assert.That(submission.Result.SnapshotRevision, Is.EqualTo(beforeRevision));
                Assert.That(submission.Result.ResultingSnapshotRevision,
                    Is.GreaterThan(beforeRevision));
                Assert.That(adapter.Presentation.Snapshot, Is.Not.Null);
                Assert.That(adapter.Presentation.Snapshot!.SnapshotRevision,
                    Is.EqualTo(submission.Result.ResultingSnapshotRevision));
                Assert.That(adapter.Presentation.Snapshot.SnapshotRevision,
                    Is.GreaterThan(lastRevision));

                lastRevision = adapter.Presentation.Snapshot.SnapshotRevision;
                acceptedActions++;
                if (string.Equals(legal.Type, "DISCARD", StringComparison.Ordinal))
                    acceptedDiscardActions++;
                acceptedTargetedFlameBolt |= isTargetedFlameBolt;
                seenTypes.Add(legal.Type);
                reachedOverPhase = adapter.Presentation.Snapshot.Phase == "OVER";
                if (reachedOverPhase) break;
            }

            Assert.That(acceptedActions, Is.GreaterThan(0));
            Assert.That(invalidSubmissionChecked, Is.True);
            Assert.That(seenTypes, Does.Contain("END_TURN"));
            Assert.That(seenTypes, Does.Contain("PLAY_CARD"));
            Assert.That(acceptedDiscardActions, Is.GreaterThan(0),
                "The real RuntimeBootstrap must accept an unchanged advertised DISCARD action.");
            Assert.That(acceptedTargetedFlameBolt, Is.True,
                "The fixed real-data action flow must accept flame_bolt with its advertised target.");

            TestContext.WriteLine("Accepted action types: " + string.Join(", ", seenTypes));
            TestContext.WriteLine("Accepted DISCARD actions: " + acceptedDiscardActions);
            TestContext.WriteLine("Last snapshot revision: " + lastRevision);
            TestContext.WriteLine("Reached OVER phase: " + reachedOverPhase);
            if (!seenTypes.Contains("PULL"))
            {
                TestContext.WriteLine(
                    "PULL was not advertised by the current real data-backed flow " +
                    "within the bounded smoke window; no PULL action was fabricated.");
            }
        }
        finally
        {
            adapter?.Dispose();
            if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    private static RuntimeLegalAction ChooseAction(
        IReadOnlyList<RuntimeLegalAction> legalActions)
    {
        var priority = new[]
        {
            "PULL",
            "PLAY_CARD",
            "ATTACK",
            "END_TURN",
            "SKIP_AMBUSH",
            "DISCARD",
        };

        foreach (var type in priority)
        {
            foreach (var action in legalActions)
            {
                if (action != null && string.Equals(action.Type, type, StringComparison.Ordinal))
                    return action;
            }
        }

        return legalActions[0];
    }

    private static void AssertRejectedSubmissionDoesNotMutate(
        RuntimeAdapter adapter,
        RuntimeSnapshotEnvelope snapshot)
    {
        var beforeSnapshot = adapter.Presentation.Snapshot;
        var beforeEventCount = adapter.Presentation.Events.Count;
        var invalid = new RuntimeGameAction
        {
            ContractVersion = snapshot.ContractVersion,
            MatchId = snapshot.MatchId,
            SnapshotRevision = snapshot.SnapshotRevision,
            ActionId = "u03_not_advertised_probe",
            Type = snapshot.LegalActions[0].Type,
            Actor = snapshot.CurrentPlayer,
            SourceId = snapshot.LegalActions[0].SourceId,
            TargetId = snapshot.LegalActions[0].TargetId,
            CardId = snapshot.LegalActions[0].CardId,
            Payload = snapshot.LegalActions[0].Payload,
        };

        Assert.That(
            () => adapter.Submit(invalid),
            Throws.InvalidOperationException.With.Message.EqualTo("action.not_advertised"));
        Assert.That(adapter.Presentation.Snapshot, Is.SameAs(beforeSnapshot));
        Assert.That(adapter.Presentation.Snapshot!.SnapshotRevision,
            Is.EqualTo(snapshot.SnapshotRevision));
        Assert.That(adapter.Presentation.Events, Has.Count.EqualTo(beforeEventCount));
        Assert.That(adapter.Presentation.EventDelta, Has.Count.EqualTo(beforeEventCount));
    }

    private static int ParseViewer(RuntimeSnapshotEnvelope snapshot)
    {
        Assert.That(snapshot.ViewerPlayerId, Does.Match("^player_[01]$"));
        return snapshot.ViewerPlayerId[snapshot.ViewerPlayerId.Length - 1] - '0';
    }

    private static void AssertViewerSnapshot(
        RuntimeSnapshotEnvelope snapshot,
        int viewerPlayerIndex)
    {
        Assert.That(snapshot.ViewerPlayerId,
            Is.EqualTo("player_" + viewerPlayerIndex));
        Assert.That(snapshot.CurrentPlayer, Is.InRange(0, 1));
        Assert.That(snapshot.Phase, Is.Not.Null.And.Not.Empty);
        Assert.That(snapshot.Players, Has.Count.EqualTo(2));

        foreach (var player in snapshot.Players)
        {
            Assert.That(player, Is.Not.Null);
            if (player.PlayerId == "player_" + viewerPlayerIndex)
            {
                foreach (var card in player.Hand)
                    Assert.That(card.OwnerPlayer, Is.EqualTo(viewerPlayerIndex));
            }
            else
            {
                Assert.That(player.Hand, Is.Empty,
                    "The opponent hand must remain redacted in the viewer snapshot.");
            }
        }
    }
}
}
#endif
