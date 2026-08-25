using System.Collections.Generic;
using DominionWars.Adapters;
using DominionWars.Engine.Events;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class SnapshotMapperTests
{
    [TestCase("ContractVersion", "1")]
    [TestCase("MatchId", "match_cases")]
    [TestCase("Turn", "4")]
    [TestCase("Phase", "ACTION")]
    [TestCase("CurrentPlayer", "0")]
    [TestCase("Players", "2")]
    [TestCase("Castle.Enabled", "True")]
    [TestCase("Castle.Health", "70")]
    [TestCase("LegalActions", "0")]
    [TestCase("Player.Id", "player_0")]
    [TestCase("Player.Life", "13")]
    [TestCase("Player.DeckCount", "1")]
    [TestCase("Player.HandCount", "1")]
    [TestCase("Player.FieldEntityIds", "1")]
    [TestCase("Player.Deck", "1")]
    [TestCase("Player.Hand", "1")]
    [TestCase("Player.Field", "1")]
    [TestCase("Player.Graveyard", "0")]
    [TestCase("Player.PunishDeltaThisTurn", "2")]
    [TestCase("Player.DamagedThisCycle", "True")]
    public void SnapshotFieldMappingCaseIsStable(string field, string expected)
    {
        var player0 = new PlayerState(0, 13) { PunishDeltaThisTurn = 2, DamagedThisCycle = true };
        var player1 = new PlayerState(1, 20);
        var definition = new CardDefinition("case_card", "Case Card", 1, 2, isMinion: true);
        player0.Deck.Add(new CardInstance(20, 0, definition));
        player0.Hand.Add(new CardInstance(21, 0, definition));
        player0.Field.Add(new CardInstance(22, 0, definition));
        var state = new GameState(player0, player1) { CastleEnabled = true, CastleHealth = 70 };
        var snapshot = EngineProjectionAdapter.ToSnapshot(state, "match_cases", 4, "ACTION");
        var mappedPlayer = snapshot.Players[0];
        var actual = field switch
        {
            "ContractVersion" => snapshot.ContractVersion.ToString(),
            "MatchId" => snapshot.MatchId,
            "Turn" => snapshot.Turn.ToString(),
            "Phase" => snapshot.Phase,
            "CurrentPlayer" => snapshot.CurrentPlayer.ToString(),
            "Players" => snapshot.Players.Count.ToString(),
            "Castle.Enabled" => snapshot.Castle.Enabled.ToString(),
            "Castle.Health" => snapshot.Castle.Health.ToString(),
            "LegalActions" => snapshot.LegalActions.Count.ToString(),
            "Player.Id" => mappedPlayer.Id,
            "Player.Life" => mappedPlayer.Life!.Value.ToString(),
            "Player.DeckCount" => mappedPlayer.DeckCount.ToString(),
            "Player.HandCount" => mappedPlayer.HandCount.ToString(),
            "Player.FieldEntityIds" => mappedPlayer.FieldEntityIds.Count.ToString(),
            "Player.Deck" => mappedPlayer.Deck.Count.ToString(),
            "Player.Hand" => mappedPlayer.Hand.Count.ToString(),
            "Player.Field" => mappedPlayer.Field.Count.ToString(),
            "Player.Graveyard" => mappedPlayer.Graveyard.Count.ToString(),
            "Player.PunishDeltaThisTurn" => mappedPlayer.PunishDeltaThisTurn.ToString(),
            "Player.DamagedThisCycle" => mappedPlayer.DamagedThisCycle.ToString(),
            _ => throw new AssertionException($"Unknown mapping case '{field}'."),
        };

        Assert.That(actual, Is.EqualTo(expected), field);
    }

    [Test]
    public void SnapshotMapsAllZonesAndPlayerRuntimeState()
    {
        var player0 = new PlayerState(0, 17)
        {
            PunishDeltaThisTurn = 2,
            PunishToSelfDiscardThisTurn = true,
            ProtectedThisTurn = true,
            EffectsNegatedThisTurn = true,
            SkipReshuffleCredits = 3,
            ReshuffleCount = 4,
            CycleWinCount = 5,
            TotalDiscarded = 6,
            PunishDrawnThisTurn = 7,
            DamagedThisCycle = true,
            PullCount = 3,
            RootStacks = 2,
            RampantStacks = 1,
        };
        var player1 = new PlayerState(1, 20);
        var definition = new CardDefinition(
            "mapped_card", "Mapped Card", 6, 9, isMinion: true,
            isLeader: true, keywords: new[] { "圣盾", "嘲讽" });
        var card = new CardInstance(7, 0, definition)
        {
            Attack = 8,
            Health = 4,
            MaxHealth = 10,
            AttacksUsed = 2,
            SummonedThisTurn = true,
            IsLeaderEntity = true,
            Sealed = true,
        };
        card.Keywords.Add("额外标记");
        player0.Deck.Add(card);
        player0.Hand.Add(new CardInstance(8, 0, definition));
        player0.Field.Add(new CardInstance(9, 0, definition));
        player0.Graveyard.Add(new CardInstance(10, 0, definition));
        player0.CommitQueue.Add(new CardInstance(11, 0, definition));
        player0.CloudStack.Add(new CardInstance(12, 0, definition));
        var state = new GameState(player0, player1) { CastleEnabled = true, CastleHealth = 61 };

        var snapshot = EngineProjectionAdapter.ToSnapshot(state, "match_mapper", 12, "ACTION");
        var mapped = snapshot.Players[0];
        var mappedCard = mapped.Deck[0];

        Assert.Multiple(() =>
        {
            Assert.That(mapped.Deck, Has.Count.EqualTo(1));
            Assert.That(mapped.Hand, Has.Count.EqualTo(1));
            Assert.That(mapped.Field, Has.Count.EqualTo(1));
            Assert.That(mapped.Graveyard, Has.Count.EqualTo(1));
            Assert.That(mapped.FieldEntityIds, Does.Contain("entity_000000000009"));
            Assert.That(mapped.Life, Is.EqualTo(17));
            Assert.That(mapped.PunishDeltaThisTurn, Is.EqualTo(2));
            Assert.That(mapped.PunishToSelfDiscardThisTurn, Is.True);
            Assert.That(mapped.ProtectedThisTurn, Is.True);
            Assert.That(mapped.EffectsNegatedThisTurn, Is.True);
            Assert.That(mapped.SkipReshuffleCredits, Is.EqualTo(3));
            Assert.That(mapped.ReshuffleCount, Is.EqualTo(4));
            Assert.That(mapped.CycleWinCount, Is.EqualTo(5));
            Assert.That(mapped.TotalDiscarded, Is.EqualTo(6));
            Assert.That(mapped.PunishDrawnThisTurn, Is.EqualTo(7));
            Assert.That(mapped.DamagedThisCycle, Is.True);
            Assert.That(mapped.PullCount, Is.EqualTo(3));
            Assert.That(mapped.RootStacks, Is.EqualTo(2));
            Assert.That(mapped.RampantStacks, Is.EqualTo(1));
            Assert.That(mapped.CommitQueueCount, Is.EqualTo(1));
            Assert.That(mapped.CloudStackCount, Is.EqualTo(1));
            Assert.That(mappedCard.EntityId, Is.EqualTo("entity_000000000007"));
            Assert.That(mappedCard.CardId, Is.EqualTo("mapped_card"));
            Assert.That(mappedCard.Name, Is.EqualTo("Mapped Card"));
            Assert.That(mappedCard.Attack, Is.EqualTo(8));
            Assert.That(mappedCard.Health, Is.EqualTo(4));
            Assert.That(mappedCard.MaxHealth, Is.EqualTo(10));
            Assert.That(mappedCard.Shield, Is.True);
            Assert.That(mappedCard.AttacksUsed, Is.EqualTo(2));
            Assert.That(mappedCard.SummonedThisTurn, Is.True);
            Assert.That(mappedCard.Sealed, Is.True);
            Assert.That(mappedCard.IsLeaderEntity, Is.True);
            Assert.That(mappedCard.DefinitionAttack, Is.EqualTo(6));
            Assert.That(mappedCard.DefinitionHealth, Is.EqualTo(9));
            Assert.That(mappedCard.GrantLife, Is.EqualTo(0));
            Assert.That(mappedCard.KingSlayer, Is.False);
            Assert.That(mappedCard.Vulnerabilities, Is.Empty);
            Assert.That(mappedCard.Keywords, Does.Contain("额外标记"));
            Assert.That(snapshot.Castle.Enabled, Is.True);
            Assert.That(snapshot.Castle.Health, Is.EqualTo(61));
        });
    }

    [TestCase("PLAY_CARD", 4L, 5L, "card_x", "card.invalid")]
    [TestCase("ATTACK", 7L, 8L, null, null)]
    [TestCase("END_TURN", null, null, null, "phase.end")]
    public void LegalActionArrayMapsStableIdsAndReason(
        string type,
        long? source,
        long? target,
        string? cardId,
        string? reason)
    {
        var actions = EngineProjectionAdapter.ToLegalActionDtos(new[]
        {
            new LegalAction
            {
                ActionId = "action_1",
                Type = type,
                Actor = 0,
                SourceId = source,
                TargetId = target,
                CardId = cardId,
                ReasonKey = reason,
            },
        });

        var mapped = actions[0];
        Assert.Multiple(() =>
        {
            Assert.That(mapped.ActionId, Is.EqualTo("action_1"));
            Assert.That(mapped.Type, Is.EqualTo(type));
            Assert.That(mapped.Actor, Is.Zero);
            Assert.That(mapped.SourceId, Is.EqualTo(source.HasValue ? EngineProjectionAdapter.ToEntityId(source.Value) : null));
            Assert.That(mapped.TargetId, Is.EqualTo(target.HasValue ? EngineProjectionAdapter.ToEntityId(target.Value) : null));
            Assert.That(mapped.CardId, Is.EqualTo(cardId));
            Assert.That(mapped.ReasonKey, Is.EqualTo(reason));
            Assert.That(mapped.ContractVersion, Is.EqualTo(1));
        });
    }

    [Test]
    public void EventArrayPreservesCausalIdsAndPayload()
    {
        var events = new[]
        {
            new GameEvent(1, null, "CARD_PLAYED", new Dictionary<string, object?> { ["source"] = 7L }),
            new GameEvent(2, 1, "DAMAGE_DEALT", new Dictionary<string, object?>
            {
                ["source"] = 7L, ["target"] = 8L, ["amount"] = 3, ["reasonKey"] = "effect.damage",
            }),
        };

        var mapped = EngineProjectionAdapter.ToEvents(events, 9, "ACTION");
        Assert.Multiple(() =>
        {
            Assert.That(mapped, Has.Count.EqualTo(2));
            Assert.That(mapped[0].ParentEventId, Is.Null);
            Assert.That(mapped[1].ParentEventId, Is.EqualTo("evt_000000000001"));
            Assert.That(mapped[1].TargetIds, Does.Contain("entity_000000000008"));
            Assert.That(mapped[1].Amount, Is.EqualTo(3));
            Assert.That(mapped[1].ReasonKey, Is.EqualTo("effect.damage"));
            Assert.That(mapped[1].Data["source"], Is.EqualTo(7L));
        });
    }

    [Test]
    public void ValidateSnapshotRejectsUnsupportedContractVersion()
    {
        var snapshot = CreateValidSnapshot();
        snapshot.ContractVersion = 2;
        Assert.Throws<System.ArgumentException>(() => EngineProjectionAdapter.ValidateSnapshot(snapshot));
    }

    [Test]
    public void ValidateSnapshotRejectsNegativeTurn()
    {
        var snapshot = CreateValidSnapshot();
        snapshot.Turn = -1;
        Assert.Throws<System.ArgumentException>(() => EngineProjectionAdapter.ValidateSnapshot(snapshot));
    }

    [Test]
    public void ValidateSnapshotRejectsUnknownPhase()
    {
        var snapshot = CreateValidSnapshot();
        snapshot.Phase = "UNKNOWN";
        Assert.Throws<System.ArgumentException>(() => EngineProjectionAdapter.ValidateSnapshot(snapshot));
    }

    [Test]
    public void ValidateSnapshotRejectsInvalidCurrentPlayer()
    {
        var snapshot = CreateValidSnapshot();
        snapshot.CurrentPlayer = 2;
        Assert.Throws<System.ArgumentException>(() => EngineProjectionAdapter.ValidateSnapshot(snapshot));
    }

    [Test]
    public void ValidateSnapshotRejectsWrongPlayerCount()
    {
        var snapshot = CreateValidSnapshot();
        snapshot.Players = System.Array.Empty<PlayerDto>();
        Assert.Throws<System.ArgumentException>(() => EngineProjectionAdapter.ValidateSnapshot(snapshot));
    }

    [Test]
    public void ValidateSnapshotRejectsNegativeCastleHealth()
    {
        var snapshot = CreateValidSnapshot();
        snapshot.Castle.Health = -1;
        Assert.Throws<System.ArgumentException>(() => EngineProjectionAdapter.ValidateSnapshot(snapshot));
    }

    [Test]
    public void ValidateSnapshotRejectsNullLegalActions()
    {
        var snapshot = CreateValidSnapshot();
        snapshot.LegalActions = null!;
        Assert.Throws<System.ArgumentException>(() => EngineProjectionAdapter.ValidateSnapshot(snapshot));
    }

    [Test]
    public void ToSnapshotRejectsNegativeTurnAtEntry()
    {
        Assert.Throws<System.ArgumentException>(() =>
            EngineProjectionAdapter.ToSnapshot(new GameState(), "match_invalid", -1, "ACTION"));
    }

    private static SnapshotDto CreateValidSnapshot()
    {
        return EngineProjectionAdapter.ToSnapshot(new GameState(), "match_valid", 0, "ACTION");
    }
}
}
