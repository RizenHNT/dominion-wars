using System;
using System.Collections.Generic;
using System.Text.Json;
using DominionWars.Adapters;
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
