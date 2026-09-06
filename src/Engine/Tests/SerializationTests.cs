using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DominionWars.Adapters;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class SerializationTests
{
    [Test]
    public void CardDtoJsonRoundTripPreservesMetadata()
    {
        var source = new CardDto
        {
            EntityId = "entity_1",
            CardId = "card_1",
            Faction = "NORTH",
            Text = "Draw one.",
            Cost = 2,
            Tags = new[] { "SCOUT" },
        };
        var copy = RoundTrip(source);
        Assert.That(copy.Faction, Is.EqualTo("NORTH"));
        Assert.That(copy.Tags, Does.Contain("SCOUT"));
    }

    [Test]
    public void LegalActionJsonRoundTripPreservesIds()
    {
        var source = new LegalActionDto
        {
            ActionId = "attack_1_2",
            Type = "ATTACK",
            Actor = 0,
            SourceId = "entity_1",
            TargetId = "entity_2",
        };
        var copy = RoundTrip(source);
        Assert.That(copy.ActionId, Is.EqualTo("attack_1_2"));
        Assert.That(copy.TargetId, Is.EqualTo("entity_2"));
    }

    [Test]
    public void SnapshotJsonRoundTripPreservesContractFields()
    {
        var source = new SnapshotDto
        {
            MatchId = "match_1",
            Turn = 3,
            Phase = "ACTION",
            CurrentPlayer = 1,
            Players = new[] { new PlayerDto(), new PlayerDto() },
            Castle = new CastleDto { Enabled = true, Health = 60 },
        };
        var copy = RoundTrip(source);
        Assert.That(copy.MatchId, Is.EqualTo("match_1"));
        Assert.That(copy.Players, Has.Count.EqualTo(2));
        Assert.That(copy.Castle.Health, Is.EqualTo(60));
    }

    [Test]
    public void GameEventJsonRoundTripPreservesCausalData()
    {
        var source = new GameEventDto
        {
            EventId = "evt_1",
            ParentEventId = "evt_0",
            Type = "DAMAGE_APPLIED",
            Turn = 2,
            Phase = "ACTION",
            TargetIds = new[] { "entity_3" },
            Amount = 4,
        };
        var copy = RoundTrip(source);
        Assert.That(copy.ParentEventId, Is.EqualTo("evt_0"));
        Assert.That(copy.TargetIds, Does.Contain("entity_3"));
        Assert.That(copy.Amount, Is.EqualTo(4));
    }

    [Test]
    public void MissingCardFieldsUseContractDefaults()
    {
        var card = JsonSerializer.Deserialize<CardDto>("{\"CardId\":\"legacy\"}")!;
        Assert.That(card.CardId, Is.EqualTo("legacy"));
        Assert.That(card.Faction, Is.EqualTo(string.Empty));
        Assert.That(card.Tags, Is.Empty);
    }

    [Test]
    public void MissingOptionalCardArtIsAllowed()
    {
        var card = JsonSerializer.Deserialize<CardDto>("{\"CardId\":\"legacy\",\"Cost\":1}")!;
        Assert.That(card.ArtId, Is.Null);
        Assert.That(card.Flavor, Is.Null);
        Assert.That(card.Cost, Is.EqualTo(1));
    }

    [Test]
    public void MissingEventParentIsAllowed()
    {
        var gameEvent = JsonSerializer.Deserialize<GameEventDto>("{\"EventId\":\"evt_1\"}")!;
        Assert.That(gameEvent.ParentEventId, Is.Null);
        Assert.That(gameEvent.EventId, Is.EqualTo("evt_1"));
    }

    [Test]
    public void CardCostRejectsStringConversion()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CardDto>("{\"Cost\":\"two\"}"));
    }

    [Test]
    public void ActionActorRejectsStringConversion()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<LegalActionDto>("{\"Actor\":\"owner\"}"));
    }

    [Test]
    public void SnapshotTurnRejectsStringConversion()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SnapshotDto>("{\"Turn\":\"three\"}"));
    }

    [Test]
    public void EventAmountRejectsStringConversion()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<GameEventDto>("{\"Amount\":\"four\"}"));
    }

    [TestCase("PLAY_CARD")]
    [TestCase("ATTACK")]
    [TestCase("END_TURN")]
    public void LegalActionTypeValuesRemainStable(string type)
    {
        var action = JsonSerializer.Deserialize<LegalActionDto>(
            $"{{\"Type\":\"{type}\"}}")!;
        Assert.That(action.Type, Is.EqualTo(type));
    }

    [TestCase("START")]
    [TestCase("ACTION")]
    [TestCase("OVER")]
    public void SnapshotPhaseValuesRemainStable(string phase)
    {
        var snapshot = JsonSerializer.Deserialize<SnapshotDto>(
            $"{{\"Phase\":\"{phase}\"}}")!;
        Assert.That(snapshot.Phase, Is.EqualTo(phase));
    }

    [Test]
    public void PayloadDictionaryRoundTripsJsonValues()
    {
        var source = new LegalActionDto
        {
            ActionId = "punish_1",
            Payload = new Dictionary<string, object?> { ["cost"] = 2, ["visible"] = true },
        };
        var copy = RoundTrip(source);
        Assert.That(copy.Payload, Does.ContainKey("cost"));
        Assert.That(copy.Payload, Does.ContainKey("visible"));
    }

    [Test]
    public void RuntimeV131EntityIdsSerializeAsNumbers()
    {
        var source = new RuntimeSnapshotEnvelope
        {
            MatchId = "match_1",
            Players = new[]
            {
                new RuntimePlayerSnapshot
                {
                    PlayerId = "player_0",
                    Hand = new[] { new RuntimeCardSnapshot { EntityId = 7, CardId = "scout", OwnerPlayer = 0 } },
                },
                new RuntimePlayerSnapshot { PlayerId = "player_1" },
            },
        };

        var json = JsonSerializer.Serialize(source);
        var copy = JsonSerializer.Deserialize<RuntimeSnapshotEnvelope>(json)!;
        Assert.That(json, Does.Contain("\"EntityId\":7"));
        Assert.That(copy.Players[0].Hand[0].EntityId, Is.EqualTo(7L));
    }

    [Test]
    public void EngineAssemblyIsSerializerAgnostic()
    {
        var engineAssembly = typeof(CardDefinition).Assembly;

        Assert.That(engineAssembly.GetReferencedAssemblies()
                .Any(reference => string.Equals(
                    reference.Name,
                    "Newtonsoft.Json",
                    StringComparison.OrdinalIgnoreCase)),
            Is.False);
    }

    [Test]
    public void RuntimeV131NewProjectionFieldsAreOptionalAndRoundTrip()
    {
        var legacy = JsonSerializer.Deserialize<RuntimePlayerSnapshot>(
            "{\"PlayerId\":\"player_0\"}")!;
        Assert.That(legacy.LeaderZone, Is.Empty);
        Assert.That(legacy.CycleWinCount, Is.Zero);

        var source = new RuntimePlayerSnapshot
        {
            PlayerId = "player_0",
            CycleWinCount = 6,
            LeaderZone = new[]
            {
                new RuntimeCardSnapshot
                {
                    EntityId = 17,
                    CardId = "leader",
                    OwnerPlayer = 0,
                    Sealed = true,
                },
            },
        };
        var copy = RoundTrip(source);

        Assert.Multiple(() =>
        {
            Assert.That(copy.CycleWinCount, Is.EqualTo(6));
            Assert.That(copy.LeaderZone, Has.Count.EqualTo(1));
            Assert.That(copy.LeaderZone[0].EntityId, Is.EqualTo(17L));
            Assert.That(copy.LeaderZone[0].Sealed, Is.True);
        });
    }

    [Test]
    public void RuntimeV131CardStateAndMechanicalZonesAreOptionalAndRoundTrip()
    {
        var legacy = JsonSerializer.Deserialize<RuntimeSnapshotEnvelope>(
            "{\"MatchId\":\"match_legacy\",\"Players\":[{\"PlayerId\":\"player_0\"},{\"PlayerId\":\"player_1\"}]}")!;

        Assert.Multiple(() =>
        {
            Assert.That(legacy.Players[0].CommitQueue, Is.Empty);
            Assert.That(legacy.Players[0].CloudStack, Is.Empty);
            var legacyCard = JsonSerializer.Deserialize<RuntimeCardSnapshot>(
                "{\"EntityId\":1,\"CardId\":\"legacy\",\"OwnerPlayer\":0}")!;
            Assert.That(legacyCard.CurrentAttack, Is.Null);
            Assert.That(legacyCard.CurrentHealth, Is.Null);
        });

        var source = new RuntimeSnapshotEnvelope
        {
            MatchId = "match_runtime_state",
            Players = new[]
            {
                new RuntimePlayerSnapshot
                {
                    PlayerId = "player_0",
                    CommitQueue = new[]
                    {
                        new RuntimeCardSnapshot
                        {
                            EntityId = 20,
                            CardId = "queued_first",
                            OwnerPlayer = 0,
                            CurrentAttack = 2,
                            CurrentHealth = 5,
                        },
                        new RuntimeCardSnapshot
                        {
                            EntityId = 21,
                            CardId = "queued_second",
                            OwnerPlayer = 0,
                            CurrentAttack = 4,
                            CurrentHealth = 3,
                        },
                    },
                    CloudStack = new[]
                    {
                        new RuntimeCardSnapshot
                        {
                            EntityId = 30,
                            CardId = "cloud_bottom",
                            OwnerPlayer = 0,
                            CurrentAttack = 1,
                            CurrentHealth = 2,
                        },
                        new RuntimeCardSnapshot
                        {
                            EntityId = 31,
                            CardId = "cloud_top",
                            OwnerPlayer = 0,
                            CurrentAttack = 8,
                            CurrentHealth = 1,
                        },
                    },
                },
                new RuntimePlayerSnapshot { PlayerId = "player_1" },
            },
        };

        var copy = RoundTrip(source);

        Assert.Multiple(() =>
        {
            Assert.That(copy.Players[0].CommitQueue, Has.Count.EqualTo(2));
            Assert.That(copy.Players[0].CommitQueue[0].EntityId, Is.EqualTo(20L));
            Assert.That(copy.Players[0].CommitQueue[1].EntityId, Is.EqualTo(21L));
            Assert.That(copy.Players[0].CloudStack, Has.Count.EqualTo(2));
            Assert.That(copy.Players[0].CloudStack[0].EntityId, Is.EqualTo(30L));
            Assert.That(copy.Players[0].CloudStack[1].EntityId, Is.EqualTo(31L));
            Assert.That(copy.Players[0].CloudStack[1].CurrentAttack, Is.EqualTo(8));
            Assert.That(copy.Players[0].CloudStack[1].CurrentHealth, Is.EqualTo(1));
        });
    }

    [Test]
    public void RuntimeWireSerializerOmitsUnavailableCardStateAndUsesWireNames()
    {
        var legacy = RuntimeWireSerializer.Deserialize<RuntimeCardSnapshot>(
            "{\"entityId\":1,\"cardId\":\"legacy\",\"ownerPlayer\":0}");
        var legacyJson = RuntimeWireSerializer.Serialize(legacy);

        Assert.Multiple(() =>
        {
            Assert.That(legacy.CurrentAttack, Is.Null);
            Assert.That(legacy.CurrentHealth, Is.Null);
            Assert.That(legacyJson, Does.Not.Contain("currentAttack"));
            Assert.That(legacyJson, Does.Not.Contain("currentHealth"));
        });

        var source = new RuntimeCardSnapshot
        {
            EntityId = 9,
            CardId = "field_minion",
            OwnerPlayer = 1,
            CurrentAttack = 7,
            CurrentHealth = 2,
        };
        var json = RuntimeWireSerializer.Serialize(source);
        var copy = RuntimeWireSerializer.Deserialize<RuntimeCardSnapshot>(json);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"currentAttack\":7"));
            Assert.That(json, Does.Contain("\"currentHealth\":2"));
            Assert.That(json, Does.Not.Contain("\"CurrentAttack\""));
            Assert.That(copy.CurrentAttack, Is.EqualTo(7));
            Assert.That(copy.CurrentHealth, Is.EqualTo(2));
        });
    }

    [Test]
    public void NullOptionalFieldsRoundTripAsNull()
    {
        var source = new CardDto { Flavor = null, ArtId = null };
        var copy = RoundTrip(source);
        Assert.That(copy.Flavor, Is.Null);
        Assert.That(copy.ArtId, Is.Null);
    }

    private static T RoundTrip<T>(T value)
    {
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    }
}
}
