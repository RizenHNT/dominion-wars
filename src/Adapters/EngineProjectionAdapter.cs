using System;
using System.Collections.Generic;
using System.Globalization;
using DominionWars.Engine.Events;
using DominionWars.Engine.Model;

namespace DominionWars.Adapters
{

public static class EngineProjectionAdapter
{
    private static readonly IReadOnlyDictionary<string, string> EventTypeMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CARD_PLAYED"] = "CARD_PLAYED",
            ["ATTACK_DECLARED"] = "ATTACK_DECLARED",
            ["AMBUSH_TRIGGERED"] = "AMBUSH_TRIGGERED",
            ["PUNISH_TRIGGERED"] = "PUNISH_TRIGGERED",
            ["DAMAGE_DEALT"] = "DAMAGE_APPLIED",
            ["HEALED"] = "HEAL_APPLIED",
            ["CARDS_DISCARDED"] = "CARD_DISCARDED",
            ["CASTLE_DAMAGED"] = "CASTLE_DAMAGED",
            ["CASTLE_BROKEN"] = "CASTLE_BROKEN",
            ["LEADER_MANIFESTED"] = "LEADER_MANIFESTED",
            ["LEADER_REPLACED"] = "LEADER_MANIFESTED",
            ["DECK_CYCLED"] = "DECK_CYCLED",
            ["GAME_WON"] = "GAME_OVER",
        };

    public static SnapshotDto ToSnapshot(
        GameState state,
        string matchId,
        int turn,
        string phase,
        IReadOnlyList<LegalActionDto>? legalActions = null)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (string.IsNullOrWhiteSpace(matchId))
        {
            throw new ArgumentException("A stable match id is required.", nameof(matchId));
        }

        var players = new List<PlayerDto>(state.Players.Count);
        foreach (var player in state.Players)
        {
            var fieldIds = new List<string>(player.Field.Count);
            var deck = ToCards(player.Deck);
            var hand = ToCards(player.Hand);
            var field = ToCards(player.Field);
            var graveyard = ToCards(player.Graveyard);
            foreach (var card in player.Field)
            {
                fieldIds.Add(ToEntityId(card.InstanceId));
            }

            players.Add(new PlayerDto
            {
                Id = $"player_{player.PlayerIndex}",
                Life = player.Life,
                DeckCount = player.Deck.Count,
                HandCount = player.Hand.Count,
                FieldEntityIds = fieldIds,
                Deck = deck,
                Hand = hand,
                Field = field,
                Graveyard = graveyard,
                PunishDeltaThisTurn = player.PunishDeltaThisTurn,
                PunishToSelfDiscardThisTurn = player.PunishToSelfDiscardThisTurn,
                ProtectedThisTurn = player.ProtectedThisTurn,
                EffectsNegatedThisTurn = player.EffectsNegatedThisTurn,
                SkipReshuffleCredits = player.SkipReshuffleCredits,
                ReshuffleCount = player.ReshuffleCount,
                CycleWinCount = player.CycleWinCount,
                TotalDiscarded = player.TotalDiscarded,
                PunishDrawnThisTurn = player.PunishDrawnThisTurn,
                DamagedThisCycle = player.DamagedThisCycle,
            });
        }

        var snapshot = new SnapshotDto
        {
            ContractVersion = 1,
            MatchId = matchId,
            Turn = turn,
            Phase = phase ?? string.Empty,
            CurrentPlayer = state.CurrentPlayerIndex,
            Players = players,
            Castle = new CastleDto
            {
                Enabled = state.CastleEnabled,
                Health = state.CastleHealth,
            },
            LegalActions = legalActions ?? Array.Empty<LegalActionDto>(),
        };

        ValidateSnapshot(snapshot);
        return snapshot;
    }

    public static SnapshotDto ToSnapshot(
        GameState state,
        string matchId,
        int turn,
        string phase,
        IReadOnlyList<DominionWars.Engine.LegalAction> legalActions)
    {
        return ToSnapshot(state, matchId, turn, phase, ToLegalActionDtos(legalActions));
    }

    public static IReadOnlyList<LegalActionDto> ToLegalActionDtos(
        IEnumerable<DominionWars.Engine.LegalAction> actions)
    {
        if (actions is null)
        {
            throw new ArgumentNullException(nameof(actions));
        }

        var result = new List<LegalActionDto>();
        foreach (var action in actions)
        {
            if (action is null)
            {
                throw new ArgumentException("Legal action entries cannot be null.", nameof(actions));
            }

            result.Add(new LegalActionDto
            {
                ContractVersion = 1,
                ActionId = action.ActionId,
                Type = action.Type,
                Actor = action.Actor,
                SourceId = action.SourceId.HasValue ? ToEntityId(action.SourceId.Value) : null,
                TargetId = action.TargetId.HasValue ? ToEntityId(action.TargetId.Value) : null,
                CardId = action.CardId,
                ReasonKey = action.ReasonKey,
                Payload = action.Payload,
            });
        }

        return result;
    }

    public static IReadOnlyList<LegalActionDto> ToLegalActions(
        IEnumerable<DominionWars.Engine.LegalAction> actions)
    {
        return ToLegalActionDtos(actions);
    }

    public static void ValidateSnapshot(SnapshotDto snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (snapshot.ContractVersion != 1)
        {
            throw new ArgumentException("Unsupported snapshot contract version.", nameof(snapshot));
        }

        if (string.IsNullOrWhiteSpace(snapshot.MatchId))
        {
            throw new ArgumentException("A stable match id is required.", nameof(snapshot));
        }

        if (snapshot.Turn < 0)
        {
            throw new ArgumentException("Snapshot turn cannot be negative.", nameof(snapshot));
        }

        if (snapshot.Phase is not ("START" or "AMBUSH" or "ACTION" or "DISCARD" or "END" or "OVER"))
        {
            throw new ArgumentException("Snapshot phase is not recognized.", nameof(snapshot));
        }

        if (snapshot.CurrentPlayer is < 0 or > 1)
        {
            throw new ArgumentException("Snapshot current player must be 0 or 1.", nameof(snapshot));
        }

        if (snapshot.Players is null || snapshot.Players.Count != 2)
        {
            throw new ArgumentException("A snapshot must contain exactly two players.", nameof(snapshot));
        }

        if (snapshot.Castle is null || snapshot.Castle.Health < 0)
        {
            throw new ArgumentException("Snapshot castle health cannot be negative.", nameof(snapshot));
        }

        if (snapshot.LegalActions is null)
        {
            throw new ArgumentException("Snapshot legal actions cannot be null.", nameof(snapshot));
        }
    }

    public static GameEventDto ToEvent(GameEvent gameEvent, int turn, string phase)
    {
        if (gameEvent is null)
        {
            throw new ArgumentNullException(nameof(gameEvent));
        }

        if (!EventTypeMap.TryGetValue(gameEvent.EventType, out var externalType))
        {
            throw new NotSupportedException(
                $"Internal event '{gameEvent.EventType}' has no approved UI event mapping.");
        }

        return new GameEventDto
        {
            ContractVersion = gameEvent.ContractVersion,
            EventId = ToEventId(gameEvent.EventId),
            ParentEventId = gameEvent.ParentEventId.HasValue
                ? ToEventId(gameEvent.ParentEventId.Value)
                : null,
            Type = externalType,
            Turn = turn,
            Phase = phase ?? string.Empty,
            SourceId = ReadId(gameEvent.Data, "source"),
            TargetIds = ReadTargets(gameEvent.Data),
            Amount = ReadNumber(gameEvent.Data, "amount"),
            ReasonKey = ReadString(gameEvent.Data, "reasonKey"),
            Data = gameEvent.Data,
        };
    }

    public static IReadOnlyList<GameEventDto> ToEvents(
        IEnumerable<GameEvent> gameEvents,
        int turn,
        string phase)
    {
        if (gameEvents is null)
        {
            throw new ArgumentNullException(nameof(gameEvents));
        }

        var result = new List<GameEventDto>();
        foreach (var gameEvent in gameEvents)
        {
            result.Add(ToEvent(gameEvent, turn, phase));
        }

        return result;
    }

    public static string ToEventId(long eventId) => $"evt_{eventId:D12}";

    public static string ToEntityId(long entityId) => $"entity_{entityId:D12}";

    public static CardDto ToCardDto(CardInstance card)
    {
        if (card is null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        return new CardDto
        {
            EntityId = ToEntityId(card.InstanceId),
            CardId = card.Definition.Id,
            Name = card.Definition.Name,
            Type = card.Definition.IsMinion ? "MINION" : "CARD",
            IsMinion = card.IsMinion,
            IsLeader = card.IsLeader,
            IsLeaderEntity = card.IsLeaderEntity,
            OwnerPlayer = card.OwnerPlayerIndex,
            Faction = card.Definition.Faction,
            Text = card.Definition.Text,
            Flavor = card.Definition.Flavor,
            Cost = card.Definition.Cost,
            Rarity = card.Definition.Rarity,
            ArtId = card.Definition.ArtId,
            DefinitionAttack = card.Definition.Attack,
            DefinitionHealth = card.Definition.Health,
            GrantLife = card.Definition.GrantLife,
            KingSlayer = card.Definition.KingSlayer,
            Vulnerabilities = new List<string>(card.Definition.Vulnerabilities),
            Attack = card.Attack,
            Health = card.Health,
            MaxHealth = card.MaxHealth,
            Shield = card.Shield,
            AttacksUsed = card.AttacksUsed,
            SummonedThisTurn = card.SummonedThisTurn,
            Keywords = new List<string>(card.Keywords),
            Tags = new List<string>(card.Definition.Tags),
        };
    }

    private static IReadOnlyList<CardDto> ToCards(IEnumerable<CardInstance> cards)
    {
        var result = new List<CardDto>();
        foreach (var card in cards)
        {
            result.Add(ToCardDto(card));
        }

        return result;
    }

    private static IReadOnlyList<string> ReadTargets(IReadOnlyDictionary<string, object?> data)
    {
        var target = ReadId(data, "target");
        return target is null ? Array.Empty<string>() : new[] { target };
    }

    private static string? ReadId(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is long longValue)
        {
            return ToEntityId(longValue);
        }

        if (value is int intValue)
        {
            return ToEntityId(intValue);
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> data, string key)
    {
        return data.TryGetValue(key, out var value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;
    }

    private static double? ReadNumber(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }
}
}
