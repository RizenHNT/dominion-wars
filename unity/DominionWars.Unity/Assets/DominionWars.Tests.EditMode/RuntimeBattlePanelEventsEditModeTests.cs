#if UNITY_INCLUDE_TESTS
#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Engine.Localization;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeBattlePanelEventsEditModeTests
{
    [Test]
    public void EventTimelinePreservesAdapterOrderAndParentDepth()
    {
        var events = new[]
        {
            Event(1, null, "PUNISH_ISSUED"),
            Event(2, Id(1), "CHAIN_LINK"),
            Event(3, Id(2), "DAMAGE_APPLIED"),
            Event(4, null, "CASTLE_DAMAGED"),
            Event(5, Id(4), "LEADER_MANIFESTED"),
            Event(6, Id(5), "PULL_DECLARED"),
        };

        var timeline = RuntimeBattlePanelPresentationModel.BuildEventTimeline(events);
        var text = RuntimeBattlePanelPresentationModel.BuildDebugEvents(events, 10);

        Assert.That(timeline.Select(item => item.EventId), Is.EqualTo(events.Select(item => item.EventId)));
        Assert.That(timeline.Select(item => item.Depth), Is.EqualTo(new[] { 0, 1, 2, 0, 1, 2 }));
        Assert.That(text.IndexOf("PUNISH", StringComparison.Ordinal), Is.LessThan(text.IndexOf("CHAIN", StringComparison.Ordinal)));
        Assert.That(text.IndexOf("CHAIN", StringComparison.Ordinal), Is.LessThan(text.IndexOf("DAMAGE", StringComparison.Ordinal)));
        Assert.That(text, Does.Contain("CASTLE"));
        Assert.That(text, Does.Contain("LEADER"));
        Assert.That(text, Does.Contain("PULL"));
        Assert.That(text, Does.Contain("parent=" + Id(1)));
    }

    [Test]
    public void EventTimelineDoesNotReadRawEventData()
    {
        var events = new[]
        {
            Event(1, null, "PUNISH_TRIGGERED", "private-card-id"),
            Event(2, Id(1), "DAMAGE_APPLIED", "secret-player-hand"),
        };

        var text = RuntimeBattlePanelPresentationModel.BuildDebugEvents(events, 10);

        Assert.That(text, Does.Contain("PUNISH"));
        Assert.That(text, Does.Contain("DAMAGE"));
        Assert.That(text, Does.Not.Contain("private-card-id"));
        Assert.That(text, Does.Not.Contain("secret-player-hand"));
        Assert.That(text, Does.Not.Contain("rawHidden"));
    }

    [Test]
    public void UnknownEventIsStableAndSafe()
    {
        var events = new[]
        {
            Event(7, null, "UNKNOWN_SYSTEM_EVENT", "unknown-secret"),
        };

        var timeline = RuntimeBattlePanelPresentationModel.BuildEventTimeline(events);
        var first = RuntimeBattlePanelPresentationModel.BuildDebugEvents(events, 10);
        var second = RuntimeBattlePanelPresentationModel.BuildDebugEvents(events, 10);

        Assert.That(timeline[0].IsKnownType, Is.False);
        Assert.That(timeline[0].Category, Is.EqualTo("UNKNOWN"));
        Assert.That(first, Is.EqualTo(second));
        Assert.That(first, Does.Contain("UNKNOWN"));
        Assert.That(first, Does.Contain("UNKNOWN_SYSTEM_EVENT"));
        Assert.That(first, Does.Contain(Id(7)));
        Assert.That(first, Does.Not.Contain("unknown-secret"));
    }

    [Test]
    public void MalformedTypeAndIdTokensAreBoundedWithoutThrowing()
    {
        var malformed = Event(7, null, "UNKNOWN_SYSTEM_EVENT");
        malformed.EventId = "evt_" + new string('x', 80);
        malformed.Type = "UNKNOWN\n" + new string('y', 80);

        var text = RuntimeBattlePanelPresentationModel.BuildDebugEvents(
            new[] { malformed },
            10);

        Assert.That(text, Does.Contain("UNKNOWN type=UNKNOWN "));
        Assert.That(text, Does.Contain("…"));
        Assert.That(text, Does.Not.Contain("UNKNOWN\n"));
        Assert.That(text.Length, Is.LessThan(220));
    }

    [Test]
    public void MissingParentFailsSafeWithoutInventingARoot()
    {
        var events = new[]
        {
            Event(8, Id(99), "DAMAGE_APPLIED"),
            Event(9, Id(8), "CASTLE_BROKEN"),
        };

        var timeline = RuntimeBattlePanelPresentationModel.BuildEventTimeline(events);
        var text = RuntimeBattlePanelPresentationModel.BuildDebugEvents(events, 10);

        Assert.That(timeline[0].ParentIndex, Is.EqualTo(-1));
        Assert.That(timeline[0].HasMissingParent, Is.True);
        Assert.That(timeline[0].Depth, Is.Zero);
        Assert.That(timeline[1].ParentIndex, Is.EqualTo(0));
        Assert.That(timeline[1].Depth, Is.EqualTo(1));
        Assert.That(text, Does.Contain("parent missing"));
        Assert.That(text, Does.Contain("parent=" + Id(99)));
    }

    [Test]
    public void CyclicParentsFailSafeAndRemainDeterministic()
    {
        var events = new[]
        {
            Event(10, Id(11), "CHAIN_LINK"),
            Event(11, Id(10), "PUNISH_DRAW"),
            Event(12, Id(11), "DAMAGE_APPLIED"),
        };

        var timeline = RuntimeBattlePanelPresentationModel.BuildEventTimeline(events);
        var first = RuntimeBattlePanelPresentationModel.BuildDebugEvents(events, 10);
        var second = RuntimeBattlePanelPresentationModel.BuildDebugEvents(events, 10);

        Assert.That(timeline[0].IsInCycle, Is.True);
        Assert.That(timeline[1].IsInCycle, Is.True);
        Assert.That(timeline[2].HasCyclicParent, Is.True);
        Assert.That(timeline.Select(item => item.Depth), Is.EqualTo(new[] { 0, 0, 1 }));
        Assert.That(first, Is.EqualTo(second));
        Assert.That(first, Does.Contain("parent cycle"));
    }

    [Test]
    public void DuplicateEventIdIsRetainedInPlaceAndMarked()
    {
        var events = new[]
        {
            Event(13, null, "PUNISH_ISSUED"),
            Event(13, null, "PUNISH_DRAW"),
            Event(14, Id(13), "CHAIN_RESOLVED"),
        };

        var timeline = RuntimeBattlePanelPresentationModel.BuildEventTimeline(events);
        var text = RuntimeBattlePanelPresentationModel.BuildDebugEvents(events, 10);

        Assert.That(timeline, Has.Count.EqualTo(3));
        Assert.That(timeline[0].IsDuplicate, Is.False);
        Assert.That(timeline[1].IsDuplicate, Is.True);
        Assert.That(timeline[2].ParentIndex, Is.EqualTo(0));
        Assert.That(text, Does.Contain("duplicate id"));
    }

    [Test]
    public void EmptyEventListIsSafe()
    {
        var timeline = RuntimeBattlePanelPresentationModel.BuildEventTimeline(Array.Empty<RuntimeEventEnvelope>());
        var text = RuntimeBattlePanelPresentationModel.BuildEvents(Array.Empty<RuntimeEventEnvelope>());

        Assert.That(timeline, Is.Empty);
        Assert.That(text, Is.EqualTo("事件摘要：暂无事件"));
    }

    [Test]
    public void TruncationKeepsNewestSuffixInOrderAndMarksOmittedParent()
    {
        var events = new[]
        {
            Event(15, null, "PUNISH_ISSUED"),
            Event(16, Id(15), "CHAIN_LINK"),
            Event(17, Id(16), "DAMAGE_APPLIED"),
            Event(18, Id(17), "CASTLE_DAMAGED"),
            Event(19, Id(18), "LEADER_MANIFESTED"),
        };

        var text = RuntimeBattlePanelPresentationModel.BuildDebugEvents(events, 2);

        Assert.That(text, Does.Contain("已截断 3 条"));
        Assert.That(text, Does.Not.Contain("id=" + Id(17)));
        Assert.That(text, Does.Contain("parent=" + Id(17)));
        Assert.That(text, Does.Contain(Id(18)));
        Assert.That(text, Does.Contain(Id(19)));
        Assert.That(text.IndexOf(Id(18), StringComparison.Ordinal), Is.LessThan(text.IndexOf(Id(19), StringComparison.Ordinal)));
        Assert.That(text, Does.Contain("parent truncated"));
    }

    [Test]
    public void NonPositiveTruncationLimitDoesNotThrowOrRenderAnEvent()
    {
        var events = new[] { Event(20, null, "PULL_DECLARED") };

        var text = RuntimeBattlePanelPresentationModel.BuildDebugEvents(events, 0);

        Assert.That(text, Does.Contain("已截断 1 条"));
        Assert.That(text, Does.Contain("没有可显示事件"));
        Assert.That(text, Does.Not.Contain(Id(20)));
    }

    [Test]
    public void PlayerEventSurfaceHidesWireDiagnosticsButKeepsSemanticCue()
    {
        var events = new[]
        {
            Event(21, null, "PUNISH_TRIGGERED", "private-card-id"),
            Event(22, Id(21), "CARD_PLAYED", "secret-player-hand"),
            Event(23, Id(22), "UNKNOWN_SYSTEM_EVENT", "unknown-secret"),
        };

        var text = RuntimeBattlePanelPresentationModel.BuildEvents(events, 10);

        Assert.That(text, Does.Contain("Play card"));
        Assert.That(text, Does.Not.Contain("PUNISH"));
        Assert.That(text, Does.Not.Contain("EVENT"));
        Assert.That(text, Does.Not.Contain("PUNISH_TRIGGERED"));
        Assert.That(text, Does.Not.Contain("UNKNOWN_SYSTEM_EVENT"));
        Assert.That(text, Does.Not.Contain("id="));
        Assert.That(text, Does.Not.Contain("parent="));
        Assert.That(text, Does.Not.Contain("targets="));
        Assert.That(text, Does.Not.Contain("revision"));
        Assert.That(text, Does.Not.Contain("ADAPTER"));
        Assert.That(text, Does.Not.Contain("private-card-id"));
        Assert.That(text, Does.Not.Contain("secret-player-hand"));
    }

    [Test]
    public void PlayerFeedMapsOnlyApprovedEventsToExistingSemanticKeys()
    {
        var table = new RuntimeLocalizationTable(
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["action.playCard"] = Translation("Play semantic"),
                ["action.attack"] = Translation("Attack semantic"),
                ["phase.action"] = Translation("Action semantic"),
                ["phase.gameOver"] = Translation("Game-over semantic"),
            });
        var resolver = new RuntimeLocalizationResolver(new Localization(), table);
        var events = new[]
        {
            Event(24, null, "CARD_PLAYED"),
            Event(25, null, "ATTACK_DECLARED"),
            Event(26, null, "PHASE_CHANGED"),
            Event(27, null, "GAME_OVER"),
            Event(28, null, "PUNISH_TRIGGERED", "hidden-punish"),
            Event(29, null, "UNKNOWN_SYSTEM_EVENT", "hidden-unknown"),
        };

        var text = RuntimeBattlePanelPresentationModel.BuildEvents(
            events,
            10,
            resolver,
            "en");

        Assert.That(text, Does.Contain("Play semantic"));
        Assert.That(text, Does.Contain("Attack semantic"));
        Assert.That(text, Does.Contain("Action semantic"));
        Assert.That(text, Does.Contain("Game-over semantic"));
        Assert.That(text, Does.Not.Contain("PUNISH"));
        Assert.That(text, Does.Not.Contain("UNKNOWN_SYSTEM_EVENT"));
        Assert.That(text, Does.Not.Contain("EVENT"));
        Assert.That(text, Does.Not.Contain("hidden-punish"));
        Assert.That(text, Does.Not.Contain("hidden-unknown"));
    }

    [Test]
    public void PlayerFeedFiltersAndCoalescesBeforeApplyingLimit()
    {
        var events = new[]
        {
            Event(30, null, "PUNISH_DRAW"),
            Event(31, null, "CARD_PLAYED"),
            Event(32, null, "CARD_PLAYED"),
            Event(33, null, "UNKNOWN_SYSTEM_EVENT"),
            Event(34, null, "ATTACK_DECLARED"),
            Event(35, null, "CASTLE_BROKEN"),
            Event(36, null, "GAME_OVER"),
        };

        var text = RuntimeBattlePanelPresentationModel.BuildEvents(events, 2);

        // The two visible rows are selected after filtering and coalescing;
        // hidden technical events do not consume the display budget.
        Assert.That(text, Does.Contain("还有 1 条"));
        Assert.That(text, Does.Contain("Attack"));
        Assert.That(text, Does.Not.Contain("Play card"));
        Assert.That(text, Does.Not.Contain("PUNISH"));
        Assert.That(text, Does.Not.Contain("CASTLE"));
        Assert.That(text, Does.Not.Contain("UNKNOWN_SYSTEM_EVENT"));
        Assert.That(text, Does.Not.Contain("EVENT"));
        Assert.That(text, Does.Not.Contain("evt_"));
    }

    [Test]
    public void PlayerFeedUsesNeutralEmptyStateWhenNoSafeEventIsVisible()
    {
        var events = new[]
        {
            Event(36, null, "PUNISH_ISSUED", "private-card-id"),
            Event(37, null, "UNKNOWN_SYSTEM_EVENT", "private-player-id"),
        };

        var text = RuntimeBattlePanelPresentationModel.BuildEvents(events, 1);

        Assert.That(text, Is.EqualTo("事件摘要：暂无事件"));
        Assert.That(text, Does.Not.Contain("EVENT"));
        Assert.That(text, Does.Not.Contain("private-card-id"));
        Assert.That(text, Does.Not.Contain("private-player-id"));
    }

    [Test]
    public void DebugFeedRetainsUnknownProtocolTokenWhilePlayerFeedHidesIt()
    {
        var unknown = Event(38, null, "UNREGISTERED_PROTOCOL_EVENT", "secret-debug-data");

        var playerText = RuntimeBattlePanelPresentationModel.BuildEvents(new[] { unknown });
        var debugText = RuntimeBattlePanelPresentationModel.BuildDebugEvents(new[] { unknown });

        Assert.That(playerText, Is.EqualTo("事件摘要：暂无事件"));
        Assert.That(playerText, Does.Not.Contain("UNREGISTERED_PROTOCOL_EVENT"));
        Assert.That(debugText, Does.Contain("UNREGISTERED_PROTOCOL_EVENT"));
        Assert.That(debugText, Does.Contain("evt_000000000038"));
        Assert.That(debugText, Does.Not.Contain("secret-debug-data"));
    }

    private static IReadOnlyDictionary<string, string> Translation(string english)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = english,
        };
    }

    [Test]
    public void ProductionPresentationSurfaceOmitsWireTokensWhileDebugSurfaceRetainsThem()
    {
        const string matchId = "match_wire_surface_probe";
        const string cardId = "stable_card_surface_probe";
        const string eventId = "event_surface_probe";
        const string parentId = "parent_surface_probe";
        const string actionId = "action_surface_probe";
        const string sourceId = "source_surface_probe";
        const string targetId = "target_surface_probe";

        var snapshot = new RuntimeSnapshotEnvelope
        {
            MatchId = matchId,
            SnapshotRevision = 912345,
            Turn = 7,
            Phase = "ACTION",
            CurrentPlayer = 0,
            ViewerPlayerId = "player_0",
            Players = new[]
            {
                new RuntimePlayerSnapshot
                {
                    PlayerId = "player_0",
                    HandCount = 1,
                    Hand = new[]
                    {
                        new RuntimeCardSnapshot
                        {
                            CardId = cardId,
                            EntityId = 77,
                            OwnerPlayer = 0,
                        },
                    },
                },
                new RuntimePlayerSnapshot { PlayerId = "player_1" },
            },
        };
        var eventEnvelope = Event(31, parentId, "CARD_PLAYED");
        eventEnvelope.EventId = eventId;
        eventEnvelope.TargetIds = new object?[] { targetId };

        var visibleAction = new RuntimeLegalAction
        {
            ActionId = actionId,
            Type = "ATTACK",
            Actor = 0,
            SourceId = sourceId,
            TargetId = targetId,
            ReasonKey = "reason_surface_probe",
            Payload = new Dictionary<string, object?>(),
        };
        var visibleState = RuntimeBattlePanelActionModel.Evaluate(visibleAction);
        var blockedAction = new RuntimeLegalAction
        {
            ActionId = "action_blocked_surface_probe",
            Type = "ATTACK",
            Actor = 0,
            SourceId = sourceId,
            ReasonKey = "reason_surface_probe",
            Payload = new Dictionary<string, object?> { ["requiresTarget"] = true },
        };
        var blockedState = RuntimeBattlePanelActionModel.Evaluate(blockedAction);

        var productionText = string.Join(
            "\n",
            RuntimeBattlePanelPresentationModel.BuildMatchLine(snapshot),
            RuntimeBattlePanelPresentationModel.BuildPhaseSummary(snapshot),
            RuntimeBattlePanelPresentationModel.BuildPlayerSection(snapshot.Players[0], true),
            RuntimeBattlePanelPresentationModel.BuildEvents(new[] { eventEnvelope }),
            RuntimeBattlePanelActionModel.Describe(visibleAction, visibleState),
            RuntimeBattlePanelActionModel.Describe(blockedAction, blockedState));

        AssertNoProductionWireTokens(productionText);
        Assert.That(productionText, Does.Not.Contain(matchId));
        Assert.That(productionText, Does.Not.Contain(cardId));
        Assert.That(productionText, Does.Not.Contain(eventId));
        Assert.That(productionText, Does.Not.Contain(parentId));
        Assert.That(productionText, Does.Not.Contain(actionId));
        Assert.That(productionText, Does.Not.Contain(sourceId));
        Assert.That(productionText, Does.Not.Contain(targetId));
        Assert.That(productionText, Does.Not.Contain("reason_surface_probe"));

        var debugText = string.Join(
            "\n",
            RuntimeBattlePanelPresentationModel.BuildDebugMatchLine(snapshot),
            RuntimeBattlePanelPresentationModel.BuildDebugPhaseSummary(snapshot),
            RuntimeBattlePanelPresentationModel.BuildDebugPlayerSection(snapshot.Players[0], true),
            RuntimeBattlePanelPresentationModel.BuildDebugEvents(new[] { eventEnvelope }),
            RuntimeBattlePanelActionModel.DescribeTechnical(visibleAction, visibleState),
            RuntimeBattlePanelActionModel.DescribeTechnical(blockedAction, blockedState));

        Assert.That(debugText, Does.Contain(matchId));
        Assert.That(debugText, Does.Contain(cardId));
        Assert.That(debugText, Does.Contain("#77"));
        Assert.That(debugText, Does.Contain(eventId));
        Assert.That(debugText, Does.Contain(parentId));
        Assert.That(debugText, Does.Contain(actionId));
        Assert.That(debugText, Does.Contain(sourceId));
        Assert.That(debugText, Does.Contain(targetId));
        Assert.That(debugText, Does.Contain("revision"));
        Assert.That(debugText, Does.Contain("action.target_required"));
    }

    private static void AssertNoProductionWireTokens(string text)
    {
        var internalTokens = new[]
        {
            "stable",
            "entity",
            "actionid",
            "source=",
            "target=",
            "reasonkey",
            "revision",
            "eventid",
            "parent=",
            "adapter order",
            "structure placeholder",
            "left rail",
            "right rail",
        };

        foreach (var token in internalTokens)
        {
            Assert.That(
                text,
                Does.Not.Contain(token).IgnoreCase,
                "Production surface leaked token: " + token);
        }
    }

    private static RuntimeEventEnvelope Event(
        long number,
        string? parent,
        string type,
        string? hiddenValue = null)
    {
        return new RuntimeEventEnvelope
        {
            EventId = Id(number),
            ParentEventId = parent,
            Type = type,
            Turn = 1,
            Phase = "ACTION",
            SnapshotRevision = number,
            TargetIds = new object?[] { "castle" },
            Data = new Dictionary<string, object?>
            {
                ["rawHidden"] = hiddenValue,
            },
        };
    }

    private static string Id(long number)
    {
        return "evt_" + number.ToString("D12", System.Globalization.CultureInfo.InvariantCulture);
    }
}
}
#endif
