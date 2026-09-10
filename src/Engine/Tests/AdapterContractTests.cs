using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DominionWars.Adapters;
using DominionWars.Engine.Events;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class AdapterContractTests
{
    private static readonly string[] MappedInternalEventTypes =
    {
        "CARD_PLAYED", "PHASE_CHANGED", "TURN_CHANGED", "TURN_STARTED",
        "CARDS_DRAWN", "ATTACK_DECLARED", "AMBUSH_SET", "AMBUSH_TRIGGERED",
        "PUNISH_TRIGGERED", "PUNISH_DRAW", "DAMAGE_DEALT", "HEALED",
        "MINION_DESTROYED", "CARDS_DISCARDED", "CASTLE_DAMAGED", "CASTLE_BROKEN",
        "LEADER_MANIFESTED", "LEADER_REPLACED", "DECK_CYCLED", "COMMIT_DECLARED",
        "CARD_COMMITTED", "CARD_PUSHED", "PULL_DECLARED", "CARD_PULLED",
        "GAME_WON", "VICTORY_PROGRESS",
    };

    [Test]
    public void SnapshotProjectionPopulatesRequiredRuntimeKitFields()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var snapshot = EngineProjectionAdapter.ToSnapshot(state, "match_test", 3, "ACTION");
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ContractVersion, Is.EqualTo(1));
            Assert.That(snapshot.MatchId, Is.EqualTo("match_test"));
            Assert.That(snapshot.Turn, Is.EqualTo(3));
            Assert.That(snapshot.Phase, Is.EqualTo("ACTION"));
            Assert.That(snapshot.Players, Has.Count.EqualTo(2));
            Assert.That(snapshot.Castle, Is.Not.Null);
            Assert.That(snapshot.LegalActions, Is.Not.Null);
        });
    }

    [Test]
    public void DamageEventMapsToApprovedUiEventNameAndStableIds()
    {
        var data = new System.Collections.Generic.Dictionary<string, object?>
        {
            ["source"] = 2L,
            ["target"] = 3L,
            ["amount"] = 4,
        };
        var gameEvent = new GameEvent(2, 1, "DAMAGE_DEALT", data);
        var projected = EngineProjectionAdapter.ToEvent(gameEvent, 4, "ACTION");
        Assert.Multiple(() =>
        {
            Assert.That(projected.EventId, Is.EqualTo("evt_000000000002"));
            Assert.That(projected.ParentEventId, Is.EqualTo("evt_000000000001"));
            Assert.That(projected.Type, Is.EqualTo("DAMAGE_APPLIED"));
            Assert.That(projected.SourceId, Is.EqualTo("entity_000000000002"));
            Assert.That(projected.TargetIds, Does.Contain("entity_000000000003"));
            Assert.That(projected.Amount, Is.EqualTo(4));
        });
    }

    [Test]
    public void CardPulledProjectsPerEventCountInsteadOfCumulativePullCount()
    {
        var first = EngineProjectionAdapter.ToEvent(
            new GameEvent(
                1,
                null,
                "CARD_PULLED",
                new Dictionary<string, object?>
                {
                    ["carrier"] = 10L,
                    ["target"] = 11L,
                    ["cost"] = 1,
                    ["pullCount"] = 1,
                }),
            4,
            "ACTION");
        var second = EngineProjectionAdapter.ToEvent(
            new GameEvent(
                2,
                1,
                "CARD_PULLED",
                new Dictionary<string, object?>
                {
                    ["carrier"] = 10L,
                    ["target"] = 12L,
                    ["cost"] = 1,
                    ["pullCount"] = 2,
                }),
            4,
            "ACTION");

        Assert.Multiple(() =>
        {
            Assert.That(first.Data["count"], Is.EqualTo(1));
            Assert.That(second.Data["count"], Is.EqualTo(1));
            Assert.That(second.Data["count"], Is.Not.EqualTo(2));
        });
    }

    [Test]
    public void DrawAndDeathEventsMapToViewerSafeUiEvents()
    {
        var draw = new GameEvent(
            7,
            null,
            "CARDS_DRAWN",
            new System.Collections.Generic.Dictionary<string, object?>
            {
                ["player"] = 1,
                ["count"] = 2,
                ["byPunish"] = false,
            });
        var death = new GameEvent(
            8,
            7,
            "MINION_DESTROYED",
            new System.Collections.Generic.Dictionary<string, object?>
            {
                ["target"] = 77L,
                ["reasonKey"] = "effect.lethal_damage",
            });

        var projectedDraw = EngineProjectionAdapter.ToEvent(draw, 2, "START");
        var projectedDeath = EngineProjectionAdapter.ToEvent(death, 2, "START");

        Assert.Multiple(() =>
        {
            Assert.That(projectedDraw.Type, Is.EqualTo("CARDS_DRAWN"));
            Assert.That(projectedDraw.Data.Keys, Is.EquivalentTo(new[] { "targetIds", "count" }));
            Assert.That(projectedDraw.Data["count"], Is.EqualTo(2));
            Assert.That(projectedDeath.Type, Is.EqualTo("MINION_DESTROYED"));
            Assert.That(projectedDeath.ParentEventId, Is.EqualTo(projectedDraw.EventId));
            Assert.That(projectedDeath.TargetIds, Does.Contain("entity_000000000077"));
            Assert.That(projectedDeath.Data.Keys, Is.EquivalentTo(new[] { "targetIds", "reasonKey" }));
            Assert.That(projectedDeath.Data.Keys, Does.Not.Contain("target"));
        });
    }

    [TestCaseSource(nameof(MappedInternalEventTypes))]
    public void EveryMappedUiEventUsesOnlySchemaDataKeys(string eventType)
    {
        var projected = EngineProjectionAdapter.ToEvent(
            new GameEvent(
                42,
                null,
                eventType,
                new Dictionary<string, object?>
                {
                    ["player"] = 1,
                    ["currentPlayer"] = 1,
                    ["source"] = 2L,
                    ["target"] = 3L,
                    ["carrier"] = 2L,
                    ["newLeader"] = 4L,
                    ["cardId"] = "fixture.card",
                    ["punish"] = 2,
                    ["cost"] = 2,
                    ["count"] = 2,
                    ["pullCount"] = 2,
                    ["amount"] = 2,
                    ["current"] = 2,
                    ["remaining"] = 2,
                    ["condition"] = "TEST_CONDITION",
                    ["reasonKey"] = "fixture.reason",
                    ["winnerPlayerIndex"] = 1,
                }),
            1,
            "ACTION");
        var allowed = ReadUiEventDataKeys();

        Assert.Multiple(() =>
        {
            Assert.That(projected.Data.Keys, Is.SubsetOf(allowed));
            Assert.That(projected.Data.Keys, Does.Not.Contain("source"));
            Assert.That(projected.Data.Keys, Does.Not.Contain("target"));
            Assert.That(projected.Data.Keys, Does.Not.Contain("player"));
            Assert.That(projected.Data.Keys, Does.Not.Contain("punish"));
            Assert.That(projected.Data.Keys, Does.Not.Contain("cost"));
        });
    }

    [Test]
    public void ProjectedChildCanTraverseToProjectedRoot()
    {
        var game = new EffectTestFixture();
        game.Apply(
            DominionWars.Engine.Effects.EffectNames.Damage,
            "ENEMY_MINION",
            2,
            selectedTargetId: game.Enemy.InstanceId);
        var root = game.State.Events.Items[0];
        var child = game.State.Events.Items[1];
        var projectedRoot = EngineProjectionAdapter.ToEvent(root, 4, "ACTION");
        var projectedChild = EngineProjectionAdapter.ToEvent(child, 4, "ACTION");
        Assert.Multiple(() =>
        {
            Assert.That(projectedRoot.ParentEventId, Is.Null);
            Assert.That(projectedRoot.Type, Is.EqualTo("CARD_PLAYED"));
            Assert.That(projectedChild.ParentEventId, Is.EqualTo(projectedRoot.EventId));
        });
    }

    [Test]
    public void UnmappedInternalEventFailsClosed()
    {
        var gameEvent = new GameEvent(1, null, "BUFF_APPLIED");
        Assert.Throws<NotSupportedException>(() =>
            EngineProjectionAdapter.ToEvent(gameEvent, 1, "ACTION"));
    }

    [Test]
    public void EventBatchFiltersInternalEventsAndPreservesProjectableAncestry()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var played = new CardInstance(700, 0, new CardDefinition(
            "played", "Played", 1, 2, isMinion: true));
        state.GetPlayer(0).Deck.Add(played);
        state.GetPlayer(0).Field.Add(new CardInstance(701, 0, new CardDefinition(
            "attacker", "Attacker", 2, 3, isMinion: true)));
        state.GetPlayer(1).Field.Add(new CardInstance(702, 1, new CardDefinition(
            "defender", "Defender", 1, 2, isMinion: true)));
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);

        flow.Advance(state, 0);
        router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush));
        Assert.That(router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: played.InstanceId)).Accepted, Is.True);
        Assert.That(router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Attack,
            sourceEntityId: 701,
            targetId: "entity_000000000702")).Accepted, Is.True);
        Assert.That(router.Execute(state, new GameActionRequest(0, LegalActionGenerator.EndTurn)).Accepted, Is.True);
        Assert.That(router.Execute(state, new GameActionRequest(
            0, TurnAction.DiscardComplete, selectedEntityIds: Array.Empty<long>())).Accepted, Is.True);

        var projected = EngineProjectionAdapter.ToEvents(
            state.Events.Items,
            state.Turn.Number,
            state.Turn.PhaseId);
        var ids = new System.Collections.Generic.HashSet<string>(
            projected.Select(item => item.EventId),
            StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(projected, Is.Not.Empty);
            Assert.That(projected.Select(item => item.Type), Does.Contain("TURN_CHANGED"));
            Assert.That(projected.Select(item => item.Type), Does.Contain("CARD_PLAYED"));
            Assert.That(projected.Select(item => item.Type), Does.Contain("ATTACK_DECLARED"));
            Assert.That(projected.Select(item => item.Type), Does.Contain("DAMAGE_APPLIED"));
            Assert.That(projected.All(item => item.ParentEventId is null || ids.Contains(item.ParentEventId)), Is.True);
            Assert.That(projected.Any(item => item.Type == "MINION_SUMMONED"), Is.False);
        });
    }

    [Test]
    public void EventBatchRejectsDuplicateMissingAndCyclicGraphs()
    {
        var duplicate = new[]
        {
            new GameEvent(1, null, "CARD_PLAYED"),
            new GameEvent(1, null, "ATTACK_DECLARED"),
        };
        var missing = new[] { new GameEvent(2, 1, "DAMAGE_DEALT") };
        var selfCycle = new[] { new GameEvent(1, 1, "CARD_PLAYED") };
        var twoNodeCycle = new[]
        {
            new GameEvent(1, 2, "CARD_PLAYED"),
            new GameEvent(2, 1, "ATTACK_DECLARED"),
        };

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => EngineProjectionAdapter.ToEvents(duplicate, 1, "ACTION"));
            Assert.Throws<ArgumentException>(() => EngineProjectionAdapter.ToEvents(missing, 1, "ACTION"));
            Assert.Throws<ArgumentException>(() => EngineProjectionAdapter.ToEvents(selfCycle, 1, "ACTION"));
            Assert.Throws<ArgumentException>(() => EngineProjectionAdapter.ToEvents(twoNodeCycle, 1, "ACTION"));
        });
    }

    [TestCase(null)]
    [TestCase(0)]
    [TestCase(2)]
    public void MissingOrIncompatibleContractVersionRejectsStartup(int? version)
    {
        Assert.Throws<StartupRejectException>(() => ContractVersionGuard.Validate(version));
    }

    [Test]
    public void ExpectedContractVersionIsAccepted()
    {
        Assert.DoesNotThrow(() => ContractVersionGuard.Validate(1));
    }

    [Test]
    public void PublicCardProjectionMapsAStableEntityId()
    {
        var definition = new CardDefinition("public_card", "Public Card", 1, 2);
        var mapped = EngineProjectionAdapter.ToCardDto(new CardInstance(19, 0, definition));
        Assert.That(mapped.EntityId, Is.EqualTo("entity_000000000019"));
    }

    [Test]
    public void PublicSnapshotValidationAcceptsAdapterOutput()
    {
        var snapshot = EngineProjectionAdapter.ToSnapshot(new GameState(), "public_match", 0, "START");
        Assert.DoesNotThrow(() => EngineProjectionAdapter.ValidateSnapshot(snapshot));
    }

    [Test]
    public void PublicLegalActionBatchProjectionMapsEngineActions()
    {
        var mapped = EngineProjectionAdapter.ToLegalActions(new[]
        {
            new DominionWars.Engine.LegalAction
            {
                ActionId = "public_action",
                Type = "END_TURN",
                Actor = 0,
            },
        });
        Assert.That(mapped[0].ActionId, Is.EqualTo("public_action"));
    }

    [Test]
    public void NullLegalActionEntryFailsClosed()
    {
        Assert.Throws<System.ArgumentException>(() => EngineProjectionAdapter.ToLegalActionDtos(
            new DominionWars.Engine.LegalAction[] { null! }));
    }

    [TestCase(-1)]
    [TestCase(2)]
    public void SnapshotWithInvalidCurrentPlayerIsRejected(int player)
    {
        var snapshot = EngineProjectionAdapter.ToSnapshot(new GameState(), "invalid_player", 0, "START");
        snapshot.CurrentPlayer = player;
        Assert.Throws<System.ArgumentException>(() => EngineProjectionAdapter.ValidateSnapshot(snapshot));
    }

    [Test]
    public void SnapshotWithUnknownPhaseIsRejected()
    {
        var snapshot = EngineProjectionAdapter.ToSnapshot(new GameState(), "invalid_phase", 0, "START");
        snapshot.Phase = "UNKNOWN";
        Assert.Throws<System.ArgumentException>(() => EngineProjectionAdapter.ValidateSnapshot(snapshot));
    }

    [Test]
    public void AdapterNullInputsFailClosed()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<System.ArgumentNullException>(() => EngineProjectionAdapter.ToLegalActionDtos(null!));
            Assert.Throws<System.ArgumentNullException>(() => EngineProjectionAdapter.ToEvents(null!, 0, "START"));
            Assert.Throws<System.ArgumentNullException>(() => EngineProjectionAdapter.ToEvent(null!, 0, "START"));
            Assert.Throws<System.ArgumentNullException>(() => EngineProjectionAdapter.ToSnapshot(null!, "m", 0, "START"));
            Assert.Throws<System.ArgumentException>(() => EngineProjectionAdapter.ToSnapshot(new GameState(), "", 0, "START"));
        });
    }

    private static IReadOnlyCollection<string> ReadUiEventDataKeys()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            var path = Path.Combine(
                current.FullName,
                "design",
                "runtime-kit-v1.31",
                "contracts",
                "schemas",
                "ui_event.schema.json");
            if (File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var data = document.RootElement
                    .GetProperty("properties")
                    .GetProperty("data");
                Assert.That(data.GetProperty("additionalProperties").GetBoolean(), Is.False);
                return data.GetProperty("properties")
                    .EnumerateObject()
                    .Select(property => property.Name)
                    .ToHashSet(StringComparer.Ordinal);
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate ui_event.schema.json.");
    }
}
}
