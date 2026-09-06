using System;
using System.Collections.Generic;
using DominionWars.Engine.Command;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

/// <summary>Sets one advertised ambush during the AMBUSH phase.</summary>
public sealed class AmbushActionHandler : ITurnActionHandler
{
    public static string ActionId(long instanceId) => $"set_ambush_{instanceId}";

    public bool CanHandle(string phaseId, string actionType)
    {
        return string.Equals(phaseId, TurnPhase.Ambush, StringComparison.Ordinal)
            && string.Equals(actionType, TurnAction.SetAmbush, StringComparison.Ordinal);
    }

    public GameActionResult Execute(GameState state, GameActionRequest request, TurnFlow flow)
    {
        if (!request.SourceEntityId.HasValue)
            return GameActionResult.Reject("action.source_required");

        var player = state.GetPlayer(request.ActorPlayerIndex);
        var card = FindInHand(player, request.SourceEntityId.Value);
        if (card is null)
            return GameActionResult.Reject("action.card_not_in_hand");
        if (!string.Equals(card.Definition.Type, "AMBUSH", StringComparison.Ordinal))
            return GameActionResult.Reject("action.ambush_card_required");
        if (card.Definition.IsLeader)
            return GameActionResult.Reject("action.leader_auto_manifest_only");
        if (request.ActionId is not null &&
            !string.Equals(request.ActionId, ActionId(card.InstanceId), StringComparison.Ordinal))
            return GameActionResult.Reject("action.id_mismatch");
        if (player.AmbushSetThisTurn)
            return GameActionResult.Reject("action.ambush_already_set_this_turn");
        if (HasLockdown(player))
            return GameActionResult.Reject("action.ambush_lockdown");

        var cost = CardPlayRules.EffectivePunish(player, card);
        var discards = ResolveConvertedCost(player, card, cost, request.SelectedEntityIds);
        if (discards is null)
            return GameActionResult.Reject("action.discard_selection_invalid");

        var opponent = state.GetOpponent(player.PlayerIndex);
        var fizzle = !player.PunishToSelfDiscardThisTurn && cost > opponent.Deck.Count;
        var root = state.Events.Append("AMBUSH_SET", null, Data(
            "player", player.PlayerIndex,
            "source", card.InstanceId,
            "cardId", card.Definition.Id,
            "punish", cost,
            "fizzle", fizzle));

        if (player.PunishToSelfDiscardThisTurn && cost > 0)
        {
            Mutate(state, _ =>
            {
                foreach (var discard in discards)
                {
                    player.Hand.Remove(discard);
                    player.Graveyard.Add(discard);
                    player.TotalDiscarded++;
                }
            });
            state.Events.Append("CARDS_DISCARDED", root.EventId, Data(
                "player", player.PlayerIndex,
                "count", discards.Count,
                "reasonKey", "rule.punish_converted"));
        }
        else if (fizzle)
        {
            Mutate(state, item =>
            {
                player.Hand.Remove(card);
                player.Graveyard.Add(card);
                player.AmbushSetThisTurn = true;
                item.EndTurnRequested = true;
            });
            flow.Advance(state, request.ActorPlayerIndex);
            return GameActionResult.Accept();
        }
        else if (cost > 0)
        {
            new EffectRuntime(state).DrawForPunish(opponent.PlayerIndex, cost, root.EventId);
        }

        Mutate(state, _ =>
        {
            player.Hand.Remove(card);
            player.AmbushZone.Add(card);
            player.AmbushSetThisTurn = true;
        });
        return GameActionResult.Accept();
    }

    private static CardInstance? FindInHand(PlayerState player, long instanceId)
    {
        foreach (var card in player.Hand)
            if (card.InstanceId == instanceId) return card;
        return null;
    }

    private static bool HasLockdown(PlayerState player)
    {
        foreach (var card in player.AmbushZone)
            if (string.Equals(card.Definition.AmbushKind, "LOCKDOWN", StringComparison.Ordinal)) return true;
        return false;
    }

    private static IReadOnlyList<CardInstance>? ResolveConvertedCost(
        PlayerState player,
        CardInstance source,
        int cost,
        IReadOnlyList<long> selectedIds)
    {
        if (!player.PunishToSelfDiscardThisTurn || cost == 0)
            return selectedIds.Count == 0 ? Array.Empty<CardInstance>() : null;
        if (selectedIds.Count != cost) return null;

        var selected = new List<CardInstance>(cost);
        var seen = new HashSet<long>();
        foreach (var id in selectedIds)
        {
            if (!seen.Add(id)) return null;
            var card = FindInHand(player, id);
            if (card is null || ReferenceEquals(card, source)) return null;
            selected.Add(card);
        }
        return selected.AsReadOnly();
    }

    private static void Mutate(GameState state, Action<GameState> mutation)
    {
        state.Commands.Execute(state, new AmbushCommand(mutation));
    }

    private static IReadOnlyDictionary<string, object?> Data(params object?[] values)
    {
        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < values.Length; index += 2)
            data[(string)values[index]!] = values[index + 1];
        return data;
    }

    private sealed class AmbushCommand : IGameCommand
    {
        private readonly Action<GameState> _mutation;
        public AmbushCommand(Action<GameState> mutation) => _mutation = mutation;
        public void Apply(GameState state) => _mutation(state);
    }
}
}
