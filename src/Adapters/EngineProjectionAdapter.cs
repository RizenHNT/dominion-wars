using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DominionWars.Engine.Events;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;

namespace DominionWars.Adapters
{

public static class EngineProjectionAdapter
{
    private static readonly IReadOnlyDictionary<string, string> EventTypeMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CARD_PLAYED"] = "CARD_PLAYED",
            ["PHASE_CHANGED"] = "PHASE_CHANGED",
            ["TURN_CHANGED"] = "TURN_CHANGED",
            ["TURN_STARTED"] = "TURN_CHANGED",
            ["CARDS_DRAWN"] = "CARDS_DRAWN",
            ["ATTACK_DECLARED"] = "ATTACK_DECLARED",
            ["AMBUSH_SET"] = "AMBUSH_SET",
            ["AMBUSH_TRIGGERED"] = "AMBUSH_TRIGGERED",
            ["PUNISH_TRIGGERED"] = "PUNISH_TRIGGERED",
            ["PUNISH_DRAW"] = "PUNISH_DRAW",
            ["DAMAGE_DEALT"] = "DAMAGE_APPLIED",
            ["HEALED"] = "HEAL_APPLIED",
            ["MINION_DESTROYED"] = "MINION_DESTROYED",
            ["CARDS_DISCARDED"] = "CARD_DISCARDED",
            ["CASTLE_DAMAGED"] = "CASTLE_DAMAGED",
            ["CASTLE_BROKEN"] = "CASTLE_BROKEN",
            ["LEADER_MANIFESTED"] = "LEADER_MANIFESTED",
            ["LEADER_REPLACED"] = "LEADER_MANIFESTED",
            ["DECK_CYCLED"] = "DECK_CYCLED",
            ["COMMIT_DECLARED"] = "COMMIT_DECLARED",
            ["CARD_COMMITTED"] = "CARD_COMMITTED",
            ["CARD_PUSHED"] = "CARD_PUSHED",
            ["PULL_DECLARED"] = "PULL_DECLARED",
            ["CARD_PULLED"] = "CARD_PULLED",
            ["GAME_WON"] = "GAME_OVER",
            ["VICTORY_PROGRESS"] = "VICTORY_PROGRESS",
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
                NoDamageTurns = player.NoDamageTurns,
                PullCount = player.PullCount,
                RootStacks = player.RootStacks,
                RampantStacks = player.RampantStacks,
                CommitQueueCount = player.CommitQueue.Count,
                CloudStackCount = player.CloudStack.Count,
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

    /// <summary>Projects the engine-owned turn metadata instead of accepting a caller-supplied phase.</summary>
    public static SnapshotDto ToSnapshot(
        GameState state,
        string matchId,
        TurnFlow turnFlow)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (turnFlow is null)
        {
            throw new ArgumentNullException(nameof(turnFlow));
        }

        return ToSnapshot(
            state,
            matchId,
            state.Turn.Number,
            state.Turn.PhaseId,
            turnFlow.GetLegalActions(state, state.CurrentPlayerIndex));
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
                TargetId = action.TargetReferenceId
                    ?? (action.TargetId.HasValue ? ToEntityId(action.TargetId.Value) : null),
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

        var data = ProjectEventData(gameEvent);
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
            SourceId = ReadId(data, "sourceId") ?? ReadId(data, "source"),
            TargetIds = ReadTargets(data),
            Amount = ReadNumber(data, "amount"),
            ReasonKey = ReadString(data, "reasonKey"),
            Data = data,
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

        var source = new List<GameEvent>(gameEvents);
        var byId = new Dictionary<long, GameEvent>();
        foreach (var gameEvent in source)
        {
            if (!byId.TryAdd(gameEvent.EventId, gameEvent))
            {
                throw new ArgumentException("Event stream contains duplicate event ids.", nameof(gameEvents));
            }
        }

        ValidateEventGraph(source, byId);

        var result = new List<GameEventDto>();
        foreach (var gameEvent in source)
        {
            if (!EventTypeMap.ContainsKey(gameEvent.EventType))
            {
                continue;
            }

            var projected = ToEvent(gameEvent, turn, phase);
            projected.ParentEventId = FindProjectableParent(gameEvent, byId);
            result.Add(projected);
        }

        return result;
    }

    private static string? FindProjectableParent(
        GameEvent gameEvent,
        IReadOnlyDictionary<long, GameEvent> byId)
    {
        var parentId = gameEvent.ParentEventId;
        var visited = new HashSet<long>();
        while (parentId.HasValue)
        {
            if (!visited.Add(parentId.Value) || !byId.TryGetValue(parentId.Value, out var parent))
            {
                throw new ArgumentException("Event stream contains a missing or cyclic parent reference.", nameof(gameEvent));
            }

            if (EventTypeMap.ContainsKey(parent.EventType))
            {
                return ToEventId(parent.EventId);
            }

            parentId = parent.ParentEventId;
        }

        return null;
    }

    private static void ValidateEventGraph(
        IReadOnlyList<GameEvent> source,
        IReadOnlyDictionary<long, GameEvent> byId)
    {
        var complete = new HashSet<long>();
        foreach (var gameEvent in source)
        {
            if (complete.Contains(gameEvent.EventId))
            {
                continue;
            }

            var path = new HashSet<long>();
            var current = gameEvent;
            while (true)
            {
                if (!path.Add(current.EventId))
                {
                    throw new ArgumentException("Event stream contains a cyclic parent reference.", nameof(source));
                }

                if (!current.ParentEventId.HasValue)
                {
                    break;
                }

                if (!byId.TryGetValue(current.ParentEventId.Value, out var parent))
                {
                    throw new ArgumentException("Event stream contains a missing parent reference.", nameof(source));
                }

                if (complete.Contains(parent.EventId))
                {
                    break;
                }

                current = parent;
            }

            foreach (var id in path)
            {
                complete.Add(id);
            }
        }
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
            Type = card.Definition.Type,
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
            DefinitionDurability = card.Definition.LeaderDurability,
            Durability = card.Durability,
            LeaderWinCondition = card.Definition.LeaderWinCondition,
            LeaderWinParam = card.Definition.LeaderWinParam,
            Victory = card.Definition.Victory is null
                ? null
                : new VictoryObjectiveDto
                {
                    Metric = card.Definition.Victory.Metric,
                    Direction = card.Definition.Victory.Direction,
                    Target = card.Definition.Victory.Target,
                },
            KingSlayer = card.Definition.KingSlayer,
            Vulnerabilities = new List<string>(card.Definition.Vulnerabilities),
            Attack = card.Attack,
            Health = card.Health,
            MaxHealth = card.MaxHealth,
            Shield = card.Shield,
            AttacksUsed = card.AttacksUsed,
            SummonedThisTurn = card.SummonedThisTurn,
            Sealed = card.Sealed,
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
        if (data.TryGetValue("targetIds", out var rawTargets)
            && rawTargets is IEnumerable targets
            && rawTargets is not string)
        {
            var result = new List<string>();
            foreach (var rawTarget in targets)
            {
                var id = ReadIdValue(rawTarget);
                if (id is not null)
                {
                    result.Add(id);
                }
            }

            return result;
        }

        var canonicalTarget = ReadId(data, "target");
        return canonicalTarget is null ? Array.Empty<string>() : new[] { canonicalTarget };
    }

    private static string? ReadId(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return ReadIdValue(value);
    }

    private static string? ReadIdValue(object? value)
    {
        if (value is null)
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

    private static IReadOnlyDictionary<string, object?> ProjectEventData(GameEvent gameEvent)
    {
        // Engine events intentionally keep their historical, internal data
        // (for example source/target/player/punish).  None of that dictionary
        // may cross the 1.31 presentation boundary verbatim: ui_event.schema
        // is fail-closed and only permits the seven canonical data keys below.
        // Each mapped event therefore gets an explicit public projection.  A
        // field which has no safe schema representation is dropped rather than
        // leaking an internal identifier or making the wire payload invalid.
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        switch (gameEvent.EventType)
        {
            case "CARD_PLAYED":
                AddCanonicalId(result, "sourceId", gameEvent.Data, "source");
                AddString(result, "cardId", gameEvent.Data, "cardId");
                AddNumber(result, "amount", gameEvent.Data, "punish");
                break;

            case "PHASE_CHANGED":
                // The current phase is already carried by the envelope.  The
                // engine's from/to/reason fields are not 1.31 data fields.
                break;

            case "TURN_CHANGED":
                AddPlayerTarget(result, gameEvent.Data, "currentPlayer");
                break;

            case "TURN_STARTED":
                AddPlayerTarget(result, gameEvent.Data, "player");
                break;

            case "CARDS_DRAWN":
            case "PUNISH_DRAW":
                AddPlayerTarget(result, gameEvent.Data, "player");
                AddNumber(result, "count", gameEvent.Data, "count");
                break;

            case "ATTACK_DECLARED":
                AddCanonicalId(result, "sourceId", gameEvent.Data, "source");
                AddCanonicalTargets(result, gameEvent.Data, "target");
                break;

            case "AMBUSH_SET":
                // Ambush identity remains hidden until the rules make it
                // public.  The punishment amount is safe presentation data;
                // source/cardId are intentionally omitted.
                AddNumber(result, "amount", gameEvent.Data, "punish");
                break;

            case "AMBUSH_TRIGGERED":
                // Do not expose the hidden ambush card or its entity id.
                break;

            case "PUNISH_TRIGGERED":
                AddCanonicalId(result, "sourceId", gameEvent.Data, "source");
                break;

            case "DAMAGE_DEALT":
            case "HEALED":
                AddCanonicalId(result, "sourceId", gameEvent.Data, "source");
                AddCanonicalTargets(result, gameEvent.Data, "target");
                AddNumber(result, "amount", gameEvent.Data, "amount");
                AddReasonKey(result, gameEvent.Data, "reasonKey");
                break;

            case "MINION_DESTROYED":
                AddCanonicalTargets(result, gameEvent.Data, "target");
                AddReasonKey(result, gameEvent.Data, "reasonKey");
                break;

            case "CARDS_DISCARDED":
                AddPlayerTarget(result, gameEvent.Data, "player");
                AddNumber(result, "count", gameEvent.Data, "count");
                AddReasonKey(result, gameEvent.Data, "reasonKey");
                break;

            case "CASTLE_DAMAGED":
                AddLiteralTarget(result, "castle");
                AddNumber(result, "amount", gameEvent.Data, "amount");
                break;

            case "CASTLE_BROKEN":
                AddLiteralTarget(result, "castle");
                break;

            case "LEADER_MANIFESTED":
                AddCanonicalTargets(result, gameEvent.Data, "target");
                break;

            case "LEADER_REPLACED":
                AddCanonicalTargets(result, gameEvent.Data, "newLeader");
                AddString(result, "cardId", gameEvent.Data, "cardId");
                break;

            case "DECK_CYCLED":
                AddPlayerTarget(result, gameEvent.Data, "player");
                break;

            case "COMMIT_DECLARED":
                AddCanonicalId(result, "sourceId", gameEvent.Data, "source");
                AddNumber(result, "amount", gameEvent.Data, "punish");
                break;

            case "CARD_COMMITTED":
            case "CARD_PUSHED":
                AddCanonicalTargets(result, gameEvent.Data, "target");
                AddNumber(result, "amount", gameEvent.Data, "cost");
                break;

            case "PULL_DECLARED":
                AddCanonicalId(result, "sourceId", gameEvent.Data, "source");
                AddCanonicalTargets(result, gameEvent.Data, "target");
                break;

            case "CARD_PULLED":
                AddCanonicalId(result, "sourceId", gameEvent.Data, "carrier");
                AddCanonicalTargets(result, gameEvent.Data, "target");
                AddNumber(result, "amount", gameEvent.Data, "cost");
                // A CARD_PULLED event represents one successful pull.  The
                // engine's pullCount is an owner-wide cumulative victory
                // counter and must never be exposed as this event's count.
                AddNumber(result, "count", gameEvent.Data, "count");
                if (!result.ContainsKey("count"))
                {
                    result["count"] = 1;
                }
                break;

            case "GAME_WON":
                var winnerPlayerIndex = ReadPlayerIndex(
                    ValueOrNull(gameEvent.Data, "winnerPlayerIndex")
                    ?? ValueOrNull(gameEvent.Data, "player"));
                if (winnerPlayerIndex is not null)
                {
                    result["winnerPlayerIndex"] = winnerPlayerIndex.Value;
                }
                AddReasonKey(result, gameEvent.Data, "reasonKey");
                break;

            case "VICTORY_PROGRESS":
                AddCanonicalId(result, "sourceId", gameEvent.Data, "source");
                AddPlayerTarget(result, gameEvent.Data, "player");
                AddNumber(result, "amount", gameEvent.Data, "current");
                if (!result.ContainsKey("amount"))
                {
                    AddNumber(result, "amount", gameEvent.Data, "remaining");
                }

                AddVictoryCondition(result, gameEvent.Data, "condition");
                break;
        }

        return result;
    }

    private static object? ValueOrNull(
        IReadOnlyDictionary<string, object?> data,
        string key)
    {
        return data.TryGetValue(key, out var value) ? value : null;
    }

    private static IReadOnlyList<object?> SingleTargetList(
        IReadOnlyDictionary<string, object?> data,
        string key)
    {
        var value = ReadCanonicalWireId(ValueOrNull(data, key));
        return value is null ? Array.Empty<object?>() : new[] { value };
    }

    private static void AddCanonicalId(
        IDictionary<string, object?> result,
        string outputKey,
        IReadOnlyDictionary<string, object?> data,
        string inputKey)
    {
        var value = ReadCanonicalWireId(ValueOrNull(data, inputKey));
        if (value is not null)
        {
            result[outputKey] = value;
        }
    }

    private static void AddCanonicalTargets(
        IDictionary<string, object?> result,
        IReadOnlyDictionary<string, object?> data,
        string inputKey)
    {
        var targets = SingleTargetList(data, inputKey);
        if (targets.Count > 0)
        {
            result["targetIds"] = targets;
        }
    }

    private static void AddLiteralTarget(IDictionary<string, object?> result, string target)
    {
        result["targetIds"] = new[] { target };
    }

    private static void AddPlayerTarget(
        IDictionary<string, object?> result,
        IReadOnlyDictionary<string, object?> data,
        string inputKey)
    {
        var playerIndex = ReadPlayerIndex(ValueOrNull(data, inputKey));
        if (playerIndex is not null)
        {
            result["targetIds"] = new[] { "player_" + playerIndex.Value.ToString(CultureInfo.InvariantCulture) };
        }
    }

    private static void AddNumber(
        IDictionary<string, object?> result,
        string outputKey,
        IReadOnlyDictionary<string, object?> data,
        string inputKey)
    {
        var value = ValueOrNull(data, inputKey);
        if (value is null)
        {
            return;
        }

        if (value is byte || value is sbyte || value is short || value is ushort
            || value is int || value is uint || value is long || value is ulong
            || value is float || value is double || value is decimal)
        {
            result[outputKey] = value;
        }
    }

    private static void AddString(
        IDictionary<string, object?> result,
        string outputKey,
        IReadOnlyDictionary<string, object?> data,
        string inputKey)
    {
        var value = ValueOrNull(data, inputKey);
        if (value is string text && !string.IsNullOrWhiteSpace(text))
        {
            result[outputKey] = text;
        }
    }

    private static void AddReasonKey(
        IDictionary<string, object?> result,
        IReadOnlyDictionary<string, object?> data,
        string inputKey)
    {
        AddString(result, "reasonKey", data, inputKey);
    }

    private static void AddVictoryCondition(
        IDictionary<string, object?> result,
        IReadOnlyDictionary<string, object?> data,
        string inputKey)
    {
        var value = ValueOrNull(data, inputKey);
        if (value is string condition && !string.IsNullOrWhiteSpace(condition))
        {
            result["reasonKey"] = "victory." + condition.ToLowerInvariant();
        }
    }

    private static int? ReadPlayerIndex(object? value)
    {
        if (value is byte byteValue && byteValue <= 1) return byteValue;
        if (value is sbyte sbyteValue && sbyteValue is >= 0 and <= 1) return sbyteValue;
        if (value is short shortValue && shortValue is >= 0 and <= 1) return shortValue;
        if (value is ushort ushortValue && ushortValue <= 1) return ushortValue;
        if (value is int intValue && intValue is >= 0 and <= 1) return intValue;
        if (value is uint uintValue && uintValue <= 1) return (int)uintValue;
        if (value is long longValue && longValue is >= 0 and <= 1) return (int)longValue;
        if (value is ulong ulongValue && ulongValue <= 1) return (int)ulongValue;

        if (value is string text)
        {
            if (text.StartsWith("player:", StringComparison.Ordinal))
            {
                text = text.Substring("player:".Length);
            }
            else if (text.StartsWith("player_", StringComparison.Ordinal))
            {
                text = text.Substring("player_".Length);
            }

            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                && parsed is >= 0 and <= 1)
            {
                return parsed;
            }
        }

        return null;
    }

    private static object? ReadCanonicalWireId(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is byte byteValue && byteValue > 0) return (long)byteValue;
        if (value is sbyte sbyteValue && sbyteValue > 0) return (long)sbyteValue;
        if (value is short shortValue && shortValue > 0) return (long)shortValue;
        if (value is ushort ushortValue && ushortValue > 0) return (long)ushortValue;
        if (value is int intValue && intValue > 0) return (long)intValue;
        if (value is uint uintValue && uintValue > 0) return (long)uintValue;
        if (value is long longValue && longValue > 0) return longValue;
        if (value is ulong ulongValue && ulongValue > 0 && ulongValue <= long.MaxValue)
            return (long)ulongValue;

        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (text.StartsWith("entity_", StringComparison.Ordinal)
            && long.TryParse(text.Substring("entity_".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var entityId)
            && entityId > 0)
        {
            return entityId;
        }

        if (text.StartsWith("player:", StringComparison.Ordinal))
        {
            var playerIndex = ReadPlayerIndex(text);
            return playerIndex is null
                ? null
                : "player_" + playerIndex.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (text.Equals("core:shared_castle", StringComparison.Ordinal))
        {
            return "castle";
        }

        if (text.Equals("castle", StringComparison.Ordinal)
            || text.StartsWith("player_", StringComparison.Ordinal)
            || text.StartsWith("leader_", StringComparison.Ordinal)
            || text.StartsWith("prompt_", StringComparison.Ordinal))
        {
            return text;
        }

        if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out entityId)
            && entityId > 0)
        {
            return entityId;
        }

        return null;
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
