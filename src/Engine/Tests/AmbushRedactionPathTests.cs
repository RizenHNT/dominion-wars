using System;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Engine.Events;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// 2026-09-11 (P1-1 follow-up). The P1-1 fix routes both transports through the
/// single viewer-scoped projection <see cref="RuntimeMatchGateway.GetViewerScopedEvents"/>
/// — the action-delta path (<c>Submit</c>) and the one-time initialization path
/// (<c>BuildInitialization</c>). The action-delta path was already covered
/// end-to-end, but the initialization path cannot be observed through a real
/// match because the START lifecycle never emits an ambush event, so it was
/// covered by code reading only. These tests exercise the projection function
/// itself with synthesized events, which closes that gap without needing a
/// match that cannot exist.
/// </summary>
[TestFixture]
public sealed class AmbushRedactionPathTests
{
    private const string AmbushCardId = "hidden_ambush";

    private static GameEvent AmbushEvent(int? owner, long eventId = 5, long? parentEventId = null)
    {
        var data = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["source"] = 41L,
            ["cardId"] = AmbushCardId,
            ["kind"] = "NORMAL",
            ["punish"] = 0L,
            ["fizzle"] = false,
        };
        if (owner.HasValue)
        {
            data["player"] = owner.Value;
        }

        return new GameEvent(eventId, parentEventId, "AMBUSH_SET", data);
    }

    [Test]
    public void InitializationPathNeverGivesANonOwnerTheAmbushIdentity()
    {
        var events = new[] { AmbushEvent(owner: 0) };

        // This is the exact call BuildInitialization makes for its viewer.
        var asOpponent = RuntimeMatchGateway.GetViewerScopedEvents(events, 1);
        var asOwner = RuntimeMatchGateway.GetViewerScopedEvents(events, 0);

        Assert.Multiple(() =>
        {
            Assert.That(asOpponent, Has.Count.EqualTo(1));
            Assert.That(asOpponent[0].Data.Keys, Does.Not.Contain("cardId"));
            Assert.That(asOpponent[0].Data.Keys, Does.Not.Contain("source"));
            Assert.That(asOpponent[0].Data.Keys, Does.Not.Contain("kind"));
            Assert.That(
                asOpponent[0].Data.Values.Select(value => value?.ToString()),
                Does.Not.Contain(AmbushCardId),
                "the ambush card id must not survive anywhere in the non-owner's copy");

            // Identity of the event itself is preserved so the transport cursor
            // still works, and the owner keeps the full shape.
            Assert.That(asOpponent[0].EventId, Is.EqualTo(5L));
            Assert.That(asOpponent[0].EventType, Is.EqualTo("AMBUSH_SET"));
            Assert.That(asOpponent[0].ContractVersion, Is.EqualTo(1));
            Assert.That(asOwner[0].Data["cardId"], Is.EqualTo(AmbushCardId));
            Assert.That(asOwner[0].Data["source"], Is.EqualTo(41L));
        });
    }

    [Test]
    public void AnAmbushEventThatCannotStateItsOwnerIsHiddenFromEveryViewer()
    {
        var events = new[] { AmbushEvent(owner: null) };

        Assert.Multiple(() =>
        {
            foreach (var viewer in new[] { 0, 1 })
            {
                var scoped = RuntimeMatchGateway.GetViewerScopedEvents(events, viewer);
                Assert.That(
                    scoped[0].Data.Keys,
                    Does.Not.Contain("cardId"),
                    $"viewer {viewer} must fail closed when the owner is unknown");
            }
        });
    }

    [Test]
    public void AmbushTriggeredEventsAreRedactedTheSameWayAsAmbushSet()
    {
        var triggered = new GameEvent(
            7,
            6,
            "AMBUSH_TRIGGERED",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["player"] = 0,
                ["source"] = 41L,
                ["cardId"] = AmbushCardId,
                ["kind"] = "FOCUS",
            });

        var scoped = RuntimeMatchGateway.GetViewerScopedEvents(new[] { triggered }, 1);

        Assert.Multiple(() =>
        {
            Assert.That(scoped[0].Data.Keys, Does.Not.Contain("cardId"));
            Assert.That(scoped[0].Data.Keys, Does.Not.Contain("source"));
            Assert.That(scoped[0].Data.Keys, Does.Not.Contain("kind"));
            Assert.That(scoped[0].ParentEventId, Is.EqualTo(6L));
        });
    }

    [Test]
    public void NonAmbushEventsAreForwardedUnchanged()
    {
        var draw = new GameEvent(
            9,
            8,
            "PUNISH_DRAW",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["player"] = 1,
                ["cardId"] = "sea_crab",
                ["count"] = 2L,
            });

        var scoped = RuntimeMatchGateway.GetViewerScopedEvents(new[] { draw }, 0);

        Assert.Multiple(() =>
        {
            // The redaction must not be over-broad: a public draw keeps its card
            // id, otherwise the transport would lose legitimate information.
            Assert.That(scoped[0].Data["cardId"], Is.EqualTo("sea_crab"));
            Assert.That(scoped[0].Data["count"], Is.EqualTo(2L));
        });
    }

    [Test]
    public void RedactionCopiesTheEventSoTheEngineKeepsTheFullTruth()
    {
        var original = AmbushEvent(owner: 0);

        var scoped = RuntimeMatchGateway.GetViewerScopedEvents(new[] { original }, 1);

        Assert.Multiple(() =>
        {
            Assert.That(scoped[0], Is.Not.SameAs(original));
            Assert.That(
                original.Data["cardId"],
                Is.EqualTo(AmbushCardId),
                "the engine's own event log must keep the ambush identity");
            Assert.That(scoped[0].Data.Keys, Does.Not.Contain("cardId"));
        });
    }

    [Test]
    public void ViewerScopedProjectionRejectsAnOutOfRangeViewer()
    {
        var events = new[] { AmbushEvent(owner: 0) };

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => RuntimeMatchGateway.GetViewerScopedEvents(events, -1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => RuntimeMatchGateway.GetViewerScopedEvents(events, 2));
        });
    }
}
}
