using System;
using System.Collections.Generic;
using System.Globalization;
using DominionWars.Engine;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;

namespace DominionWars.Adapters
{

/// <summary>
/// Projects engine state into the 1.31 viewer-safe snapshot. This boundary is
/// deliberately separate from the legacy 1.30 SnapshotDto adapter.
/// </summary>
public static class RuntimeSnapshotProjection
{
    public static RuntimeSnapshotEnvelope ToSnapshot(
        GameState state,
        string matchId,
        long snapshotRevision,
        int viewerPlayerIndex,
        TurnFlow flow)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        if (flow is null) throw new ArgumentNullException(nameof(flow));
        ValidateIdentity(matchId, snapshotRevision, viewerPlayerIndex);
        var isTerminal = string.Equals(state.Turn.PhaseId, TurnPhase.Over, StringComparison.Ordinal);

        var players = new List<RuntimePlayerSnapshot>(2);
        foreach (var player in state.Players)
        {
            var isViewer = player.PlayerIndex == viewerPlayerIndex;
            players.Add(new RuntimePlayerSnapshot
            {
                PlayerId = PlayerId(player.PlayerIndex),
                Life = player.Life,
                DeckCount = player.Deck.Count,
                HandCount = player.Hand.Count,
                FieldCount = player.Field.Count,
                GraveyardCount = player.Graveyard.Count,
                AmbushCount = player.AmbushZone.Count,
                CycleWinCount = player.CycleWinCount,
                RootStacks = player.RootStacks,
                RampantStacks = player.RampantStacks,
                PullCount = player.PullCount,
                CommitQueueCount = player.CommitQueue.Count,
                CloudStackCount = player.CloudStack.Count,
                Hand = isViewer ? Cards(player.Hand) : Array.Empty<RuntimeCardSnapshot>(),
                Ambush = isViewer ? Cards(player.AmbushZone) : Array.Empty<RuntimeCardSnapshot>(),
                Field = Cards(player.Field),
                LeaderZone = Cards(player.LeaderZone),
                Graveyard = Cards(player.Graveyard),
                CommitQueue = Cards(player.CommitQueue),
                CloudStack = Cards(player.CloudStack),
            });
        }

        var legal = new List<RuntimeLegalAction>();
        if (!state.WinnerPlayerIndex.HasValue && state.CurrentPlayerIndex == viewerPlayerIndex)
        {
            foreach (var action in flow.GetLegalActions(state, viewerPlayerIndex))
            {
                legal.Add(ToLegalAction(action, snapshotRevision));
            }
        }

        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = matchId,
            SnapshotRevision = snapshotRevision,
            Turn = state.Turn.Number,
            Phase = state.Turn.PhaseId,
            CurrentPlayer = state.CurrentPlayerIndex,
            ViewerPlayerId = PlayerId(viewerPlayerIndex),
            Players = players.AsReadOnly(),
            Castle = new RuntimeCastleSnapshot { Enabled = state.CastleEnabled, Health = state.CastleHealth },
            LegalActions = legal.AsReadOnly(),
            PendingPrompt = null,
            WinnerPlayerIndex = isTerminal ? state.WinnerPlayerIndex : null,
            ReasonKey = isTerminal ? state.WinReason : null,
        };
    }

    public static void Validate(RuntimeSnapshotEnvelope snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        ValidateIdentity(snapshot.MatchId, snapshot.SnapshotRevision, ParsePlayer(snapshot.ViewerPlayerId));
        if (snapshot.ContractVersion != ContractVersionGuard.ExpectedVersion)
            throw new ArgumentException("Unsupported contract version.", nameof(snapshot));
        if (snapshot.Players is null || snapshot.Players.Count != 2)
            throw new ArgumentException("A snapshot must contain both players.", nameof(snapshot));
        if (snapshot.CurrentPlayer is < 0 or > 1)
            throw new ArgumentException("The current player is invalid.", nameof(snapshot));
        if (string.IsNullOrWhiteSpace(snapshot.Phase))
            throw new ArgumentException("The snapshot phase is required.", nameof(snapshot));
        if (string.Equals(snapshot.Phase, TurnPhase.Over, StringComparison.Ordinal))
        {
            if (!snapshot.WinnerPlayerIndex.HasValue
                || snapshot.WinnerPlayerIndex.Value is < 0 or > 1)
            {
                throw new ArgumentException(
                    "A terminal snapshot must identify the winner.",
                    nameof(snapshot));
            }

            if (!WinReasonKey.IsContractReasonKey(snapshot.ReasonKey))
            {
                throw new ArgumentException(
                    "A terminal snapshot must contain a valid reason key.",
                    nameof(snapshot));
            }
        }
        else if (snapshot.WinnerPlayerIndex.HasValue || snapshot.ReasonKey is not null)
        {
            throw new ArgumentException(
                "Non-terminal snapshots cannot contain an outcome.",
                nameof(snapshot));
        }
    }

    private static RuntimeLegalAction ToLegalAction(DominionWars.Engine.LegalAction action, long revision)
    {
        if (action is null) throw new ArgumentException("Legal actions cannot contain null.", nameof(action));
        return new RuntimeLegalAction
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            SnapshotRevision = revision,
            ActionId = action.ActionId,
            Type = action.Type,
            Actor = action.Actor,
            SourceId = EntityId(action.SourceId),
            TargetId = TargetId(action.TargetReferenceId, action.TargetId),
            CardId = action.CardId,
            ReasonKey = action.ReasonKey,
            Payload = CopyPayload(action.Payload),
        };
    }

    private static IReadOnlyDictionary<string, object?> CopyPayload(IReadOnlyDictionary<string, object?> payload)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var entry in payload) copy[entry.Key] = entry.Value;
        return copy;
    }

    private static IReadOnlyList<RuntimeCardSnapshot> Cards(IList<CardInstance> cards)
    {
        var result = new List<RuntimeCardSnapshot>(cards.Count);
        foreach (var card in cards)
        {
            result.Add(new RuntimeCardSnapshot
            {
                EntityId = EntityId(card.InstanceId),
                CardId = card.Definition.Id,
                OwnerPlayer = card.OwnerPlayerIndex,
                Sealed = card.Sealed,
                CurrentAttack = card.Attack,
                CurrentHealth = card.Health,
            });
        }
        return result.AsReadOnly();
    }

    private static long? EntityId(long? id) => id.HasValue ? EntityId(id.Value) : null;

    private static long EntityId(long id)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        return id;
    }

    private static object? TargetId(string? reference, long? numericId)
    {
        if (!string.IsNullOrWhiteSpace(reference))
        {
            // Engine TargetReference ids are legacy/internal strings. The
            // v1.31 wire target for an entity is the same positive numeric id
            // used by RuntimeCardSnapshot.EntityId. Prefer the explicit
            // numeric field and only parse the fixed legacy forms when it is
            // absent; malformed references remain unchanged rather than being
            // guessed into a different target.
            if (TryParseEntityReference(reference, out var referencedEntityId))
                return EntityId(numericId ?? referencedEntityId);
            if (string.Equals(reference, "core:shared_castle", StringComparison.Ordinal)) return "castle";
            if (reference.StartsWith("core:player_", StringComparison.Ordinal))
            {
                var parts = reference.Split(':');
                if (parts.Length == 3 && parts[1].StartsWith("player_", StringComparison.Ordinal))
                {
                    if (parts[2] == "life") return parts[1];
                    if (parts[2] == "leader") return "leader_" + parts[1].Substring("player_".Length);
                }
            }
            return reference;
        }
        return EntityId(numericId);
    }

    private static bool TryParseEntityReference(string reference, out long entityId)
    {
        const string legacyPrefix = "entity_";
        const string colonPrefix = "entity:";
        string digits;
        if (reference.StartsWith(legacyPrefix, StringComparison.Ordinal))
        {
            digits = reference.Substring(legacyPrefix.Length);
            if (digits.Length == 0)
            {
                entityId = 0;
                return false;
            }
        }
        else if (reference.StartsWith(colonPrefix, StringComparison.Ordinal))
        {
            digits = reference.Substring(colonPrefix.Length);
        }
        else
        {
            entityId = 0;
            return false;
        }

        return long.TryParse(
            digits,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out entityId) && entityId > 0;
    }

    private static string PlayerId(int index) => "player_" + index;

    private static int ParsePlayer(string value)
    {
        if (value == "player_0") return 0;
        if (value == "player_1") return 1;
        throw new ArgumentException("Viewer player id is invalid.", nameof(value));
    }

    private static void ValidateIdentity(string matchId, long revision, int viewer)
    {
        if (string.IsNullOrWhiteSpace(matchId) || !matchId.StartsWith("match_", StringComparison.Ordinal))
            throw new ArgumentException("A stable match id is required.", nameof(matchId));
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (viewer is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(viewer));
    }
}
}
