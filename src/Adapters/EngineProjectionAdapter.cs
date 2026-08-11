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
            });
        }

        return new SnapshotDto
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

    public static string ToEventId(long eventId) => $"evt_{eventId:D12}";

    public static string ToEntityId(long entityId) => $"entity_{entityId:D12}";

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
