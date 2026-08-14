using System.Collections.Generic;
using System;
using DominionWars.Adapters;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class RuntimeActionBoundaryTests
{
    [Test]
    public void MatchingAdvertisedActionIsAccepted()
    {
        var result = RuntimeActionBoundary.Validate(Action(), Snapshot());

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.ReasonKey, Is.EqualTo("action.accepted"));
    }

    [Test]
    public void WrongMatchIsRejectedBeforeActionLookup()
    {
        var action = Action();
        action.MatchId = "other_match";

        Assert.That(RuntimeActionBoundary.Validate(action, Snapshot()).ReasonKey,
            Is.EqualTo("action.wrong_match"));
    }

    [Test]
    public void MissingSnapshotMatchIdIsRejected()
    {
        var snapshot = Snapshot();
        snapshot.MatchId = string.Empty;

        Assert.That(RuntimeActionBoundary.Validate(Action(), snapshot).ReasonKey,
            Is.EqualTo("snapshot.match_id_missing"));
    }

    [Test]
    public void InvalidActorIsRejected()
    {
        var action = Action();
        action.Actor = 2;

        Assert.That(RuntimeActionBoundary.Validate(action, Snapshot()).ReasonKey,
            Is.EqualTo("action.actor_invalid"));
    }

    [Test]
    public void NegativeSnapshotRevisionIsRejected()
    {
        var snapshot = Snapshot();
        snapshot.SnapshotRevision = -1;

        Assert.That(RuntimeActionBoundary.Validate(Action(), snapshot).ReasonKey,
            Is.EqualTo("snapshot.revision_invalid"));
    }

    [Test]
    public void NegativeActionRevisionIsRejected()
    {
        var action = Action();
        action.SnapshotRevision = -1;

        Assert.That(RuntimeActionBoundary.Validate(action, Snapshot()).ReasonKey,
            Is.EqualTo("action.revision_invalid"));
    }

    [Test]
    public void EmptyActionTypeIsRejected()
    {
        var action = Action();
        action.Type = string.Empty;

        Assert.That(RuntimeActionBoundary.Validate(action, Snapshot()).ReasonKey,
            Is.EqualTo("action.type_required"));
    }

    [Test]
    public void StaleAndFutureRevisionsAreRejected()
    {
        var stale = Action();
        stale.SnapshotRevision = 3;
        var future = Action();
        future.SnapshotRevision = 5;
        var snapshot = Snapshot(4);

        Assert.Multiple(() =>
        {
            Assert.That(RuntimeActionBoundary.Validate(stale, snapshot).ReasonKey,
                Is.EqualTo("action.stale_snapshot"));
            Assert.That(RuntimeActionBoundary.Validate(future, snapshot).ReasonKey,
                Is.EqualTo("action.future_snapshot"));
        });
    }

    [Test]
    public void UnadvertisedActionIsRejected()
    {
        var action = Action();
        action.ActionId = "not_advertised";

        Assert.That(RuntimeActionBoundary.Validate(action, Snapshot()).ReasonKey,
            Is.EqualTo("action.not_advertised"));
    }

    [Test]
    public void PayloadMismatchIsRejected()
    {
        var action = Action();
        action.Payload = new Dictionary<string, object?> { ["count"] = 2 };

        Assert.That(RuntimeActionBoundary.Validate(action, Snapshot()).ReasonKey,
            Is.EqualTo("action.advertisement_mismatch"));
    }

    [Test]
    public void SameActionIdWithDifferentTypeIsRejected()
    {
        var action = Action();
        action.Type = "ATTACK";

        Assert.That(RuntimeActionBoundary.Validate(action, Snapshot()).ReasonKey,
            Is.EqualTo("action.advertisement_mismatch"));
    }

    [Test]
    public void SourceTargetAndCardMismatchesAreRejectedIndependently()
    {
        var sourceMismatch = Action();
        sourceMismatch.SourceId = "entity_2";
        var targetMismatch = Action();
        targetMismatch.TargetId = "entity_2";
        var cardMismatch = Action();
        cardMismatch.CardId = "card_2";

        Assert.Multiple(() =>
        {
            Assert.That(RuntimeActionBoundary.Validate(sourceMismatch, Snapshot()).ReasonKey,
                Is.EqualTo("action.advertisement_mismatch"));
            Assert.That(RuntimeActionBoundary.Validate(targetMismatch, Snapshot()).ReasonKey,
                Is.EqualTo("action.advertisement_mismatch"));
            Assert.That(RuntimeActionBoundary.Validate(cardMismatch, Snapshot()).ReasonKey,
                Is.EqualTo("action.advertisement_mismatch"));
        });
    }

    [Test]
    public void RejectedValidationDoesNotMutateActionOrSnapshotInputs()
    {
        var snapshot = Snapshot();
        var advertised = snapshot.LegalActions[0];
        var rejected = new[]
        {
            WrongMatchAction(),
            StaleAction(),
            UnadvertisedAction(),
            InvalidActorAction(),
            PayloadMismatchAction(),
        };

        foreach (var action in rejected)
        {
            var beforeMatchId = action.MatchId;
            var beforeRevision = action.SnapshotRevision;
            var beforeActionId = action.ActionId;
            var beforeType = action.Type;
            var beforeActor = action.Actor;
            var beforePayload = action.Payload;

            Assert.That(RuntimeActionBoundary.Validate(action, snapshot).Accepted, Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(action.MatchId, Is.EqualTo(beforeMatchId));
                Assert.That(action.SnapshotRevision, Is.EqualTo(beforeRevision));
                Assert.That(action.ActionId, Is.EqualTo(beforeActionId));
                Assert.That(action.Type, Is.EqualTo(beforeType));
                Assert.That(action.Actor, Is.EqualTo(beforeActor));
                Assert.That(action.Payload, Is.SameAs(beforePayload));
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.MatchId, Is.EqualTo("match_1"));
            Assert.That(snapshot.SnapshotRevision, Is.EqualTo(4));
            Assert.That(snapshot.LegalActions, Has.Count.EqualTo(1));
            Assert.That(advertised.ActionId, Is.EqualTo("play_1"));
            Assert.That(advertised.Type, Is.EqualTo("PLAY_CARD"));
            Assert.That(advertised.Actor, Is.Zero);
            Assert.That(advertised.Payload["count"], Is.EqualTo(1));
        });
    }

    [Test]
    public void NestedPayloadMismatchIsRejected()
    {
        var action = Action();
        action.Payload = new Dictionary<string, object?>
        {
            ["meta"] = new Dictionary<string, object?>
            {
                ["ids"] = new[] { 1, 2 },
            },
        };
        var snapshot = Snapshot();
        snapshot.LegalActions[0].Payload = new Dictionary<string, object?>
        {
            ["meta"] = new Dictionary<string, object?>
            {
                ["ids"] = new[] { 1, 3 },
            },
        };

        Assert.That(RuntimeActionBoundary.Validate(action, snapshot).ReasonKey,
            Is.EqualTo("action.advertisement_mismatch"));
    }

    [Test]
    public void MissingLegalActionsRejectsClosed()
    {
        var snapshot = Snapshot();
        snapshot.LegalActions = null!;

        Assert.That(RuntimeActionBoundary.Validate(Action(), snapshot).ReasonKey,
            Is.EqualTo("snapshot.legal_actions_missing"));
    }

    [Test]
    public void DuplicateAdvertisedActionIdsRejectClosed()
    {
        var snapshot = Snapshot();
        snapshot.LegalActions = new[]
        {
            snapshot.LegalActions[0],
            new RuntimeLegalAction
            {
                SnapshotRevision = snapshot.SnapshotRevision,
                ActionId = "play_1",
                Type = "PLAY_CARD",
                Actor = 0,
            },
        };

        Assert.That(RuntimeActionBoundary.Validate(Action(), snapshot).ReasonKey,
            Is.EqualTo("snapshot.duplicate_action_id"));
    }

    [Test]
    public void DuplicateUnrequestedAdvertisedActionIdsAlsoRejectClosed()
    {
        var snapshot = Snapshot();
        snapshot.LegalActions = new[]
        {
            snapshot.LegalActions[0],
            new RuntimeLegalAction
            {
                SnapshotRevision = snapshot.SnapshotRevision,
                ActionId = "other",
                Type = "END_TURN",
                Actor = 0,
            },
            new RuntimeLegalAction
            {
                SnapshotRevision = snapshot.SnapshotRevision,
                ActionId = "other",
                Type = "END_TURN",
                Actor = 0,
            },
        };

        Assert.That(RuntimeActionBoundary.Validate(Action(), snapshot).ReasonKey,
            Is.EqualTo("snapshot.duplicate_action_id"));
    }

    [Test]
    public void InvalidAdvertisedActorIsRejected()
    {
        var snapshot = Snapshot();
        snapshot.LegalActions[0].Actor = 2;

        Assert.That(RuntimeActionBoundary.Validate(Action(), snapshot).ReasonKey,
            Is.EqualTo("snapshot.actor_invalid"));
    }

    [Test]
    public void AdvertisedVersionAndRevisionMustMatchSnapshot()
    {
        var versionMismatch = Snapshot();
        versionMismatch.LegalActions[0].ContractVersion = 2;
        var revisionMismatch = Snapshot();
        revisionMismatch.LegalActions[0].SnapshotRevision = 5;

        Assert.Multiple(() =>
        {
            Assert.That(RuntimeActionBoundary.Validate(Action(), versionMismatch).ReasonKey,
                Is.EqualTo("snapshot.legal_action_version_mismatch"));
            Assert.That(RuntimeActionBoundary.Validate(Action(), revisionMismatch).ReasonKey,
                Is.EqualTo("snapshot.legal_action_revision_mismatch"));
        });
    }

    [Test]
    public void EmptyAdvertisedActionTypeIsRejected()
    {
        var snapshot = Snapshot();
        snapshot.LegalActions[0].Type = string.Empty;

        Assert.That(RuntimeActionBoundary.Validate(Action(), snapshot).ReasonKey,
            Is.EqualTo("snapshot.action_type_missing"));
    }

    [Test]
    public void ActionResultCacheDoesNotOverwriteDuplicateAction()
    {
        var cache = new InMemoryRuntimeActionResultCache();
        var first = new RuntimeActionResult
        {
            MatchId = "match_1",
            SnapshotRevision = 4,
            ActionId = "play_1",
            Accepted = true,
            ReasonKey = "action.accepted",
        };
        var second = new RuntimeActionResult
        {
            MatchId = "match_1",
            SnapshotRevision = 4,
            ActionId = "play_1",
            Accepted = false,
            ReasonKey = "action.rejected",
        };

        Assert.Multiple(() =>
        {
            Assert.That(cache.TryStore("match_1", 4, "play_1", first), Is.True);
            Assert.That(cache.TryStore("match_1", 4, "play_1", second), Is.False);
            Assert.That(cache.TryGet("match_1", 4, "play_1", out var result), Is.True);
            Assert.That(result, Is.SameAs(first));
        });
    }

    [Test]
    public void ActionResultCacheSeparatesMatchRevisionAndActionIdentity()
    {
        var cache = new InMemoryRuntimeActionResultCache();
        var result = new RuntimeActionResult
        {
            MatchId = "match_1",
            SnapshotRevision = 4,
            ActionId = "play_1",
            Accepted = true,
            ReasonKey = "action.accepted",
        };
        cache.TryStore("match_1", 4, "play_1", result);

        Assert.Multiple(() =>
        {
            Assert.That(cache.TryGet("match_2", 4, "play_1", out _), Is.False);
            Assert.That(cache.TryGet("match_1", 5, "play_1", out _), Is.False);
            Assert.That(cache.TryGet("match_1", 4, "attack_1", out _), Is.False);
        });
    }

    [Test]
    public void ActionResultCacheRejectsInvalidIdentity()
    {
        var cache = new InMemoryRuntimeActionResultCache();
        var result = new RuntimeActionResult
        {
            MatchId = "match_1",
            SnapshotRevision = 4,
            ActionId = "play_1",
        };

        Assert.Multiple(() =>
        {
            Assert.That(cache.TryGet(string.Empty, 4, "play_1", out _), Is.False);
            Assert.That(cache.TryGet("match_1", -1, "play_1", out _), Is.False);
            Assert.Throws<System.ArgumentException>(() =>
                cache.TryStore("match_1", 4, string.Empty, result));
            Assert.Throws<System.ArgumentException>(() =>
                cache.TryStore("match_1", 5, "play_1", result));
        });
    }

    [Test]
    public void ActionResultCacheCanExpireWithInjectedRetentionPolicy()
    {
        var now = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);
        var cache = new InMemoryRuntimeActionResultCache(
            new TimeToLiveRuntimeActionResultPolicy(TimeSpan.FromMinutes(5)),
            () => now);
        var result = new RuntimeActionResult
        {
            MatchId = "match_1",
            SnapshotRevision = 4,
            ActionId = "play_1",
        };

        cache.TryStore("match_1", 4, "play_1", result);
        Assert.That(cache.TryGet("match_1", 4, "play_1", out _), Is.True);
        now = now.AddMinutes(5).AddTicks(1);

        Assert.That(cache.TryGet("match_1", 4, "play_1", out _), Is.False);
    }

    [Test]
    public void ContractMismatchIsRejected()
    {
        var action = Action();
        action.ContractVersion = 2;

        Assert.That(RuntimeActionBoundary.Validate(action, Snapshot()).ReasonKey,
            Is.EqualTo("contract.version_mismatch"));
    }

    private static RuntimeSnapshotEnvelope Snapshot(long revision = 4)
    {
        return new RuntimeSnapshotEnvelope
        {
            MatchId = "match_1",
            SnapshotRevision = revision,
            LegalActions = new[]
            {
                new RuntimeLegalAction
                {
                    SnapshotRevision = revision,
                    ActionId = "play_1",
                    Type = "PLAY_CARD",
                    Actor = 0,
                    SourceId = "entity_1",
                    Payload = new Dictionary<string, object?> { ["count"] = 1 },
                },
            },
        };
    }

    private static RuntimeGameAction Action()
    {
        return new RuntimeGameAction
        {
            MatchId = "match_1",
            SnapshotRevision = 4,
            ActionId = "play_1",
            Type = "PLAY_CARD",
            Actor = 0,
            SourceId = "entity_1",
            Payload = new Dictionary<string, object?> { ["count"] = 1 },
        };
    }

    private static RuntimeGameAction WrongMatchAction()
    {
        var action = Action();
        action.MatchId = "other_match";
        return action;
    }

    private static RuntimeGameAction StaleAction()
    {
        var action = Action();
        action.SnapshotRevision = 3;
        return action;
    }

    private static RuntimeGameAction UnadvertisedAction()
    {
        var action = Action();
        action.ActionId = "not_advertised";
        return action;
    }

    private static RuntimeGameAction InvalidActorAction()
    {
        var action = Action();
        action.Actor = 2;
        return action;
    }

    private static RuntimeGameAction PayloadMismatchAction()
    {
        var action = Action();
        action.Payload = new Dictionary<string, object?> { ["count"] = 2 };
        return action;
    }
}
}
