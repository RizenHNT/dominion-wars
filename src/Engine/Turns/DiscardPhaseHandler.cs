using System;
using System.Collections.Generic;
using DominionWars.Engine.Command;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

/// <summary>Requires an explicit, validated hand-limit discard selection.</summary>
public sealed class DiscardPhaseHandler : IPhaseHandler, ITurnActionHandler
{
    public string PhaseId => TurnPhase.Discard;

    public IReadOnlyList<LegalAction> GetLegalActions(GameState state, int playerIndex)
    {
        var player = state.GetPlayer(playerIndex);
        var required = RequiredDiscardCount(state, player);
        var candidates = new long[player.Hand.Count];
        for (var index = 0; index < player.Hand.Count; index++)
        {
            candidates[index] = player.Hand[index].InstanceId;
        }

        return new[]
        {
            new LegalAction
            {
                ActionId = "discard_" + playerIndex + "_" + required,
                Type = TurnAction.DiscardComplete,
                Actor = playerIndex,
                ReasonKey = "action.discard_to_limit",
                Payload = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["requiredCount"] = required,
                    ["candidateIds"] = candidates,
                },
            },
        };
    }

    public bool CanHandle(string phaseId, string actionType)
    {
        return phaseId == TurnPhase.Discard && actionType == TurnAction.DiscardComplete;
    }

    public GameActionResult Execute(GameState state, GameActionRequest request, TurnFlow flow)
    {
        var player = state.GetPlayer(request.ActorPlayerIndex);
        var required = RequiredDiscardCount(state, player);
        if (request.SelectedEntityIds.Count != required)
        {
            return GameActionResult.Reject("action.discard_count_mismatch");
        }

        var selected = new List<CardInstance>(required);
        var unique = new HashSet<long>();
        foreach (var id in request.SelectedEntityIds)
        {
            if (!unique.Add(id))
            {
                return GameActionResult.Reject("action.discard_duplicate");
            }

            var card = FindInHand(player, id);
            if (card is null)
            {
                return GameActionResult.Reject("action.discard_not_in_hand");
            }

            selected.Add(card);
        }

        if (request.ActionId is not null
            && !string.Equals(request.ActionId, "discard_" + player.PlayerIndex + "_" + required, StringComparison.Ordinal))
        {
            return GameActionResult.Reject("action.id_mismatch");
        }

        state.Commands.Execute(state, new DiscardCommand(player, selected));
        if (selected.Count > 0)
        {
            state.Events.Append("CARDS_DISCARDED", null, new Dictionary<string, object?>
            {
                ["player"] = player.PlayerIndex,
                ["count"] = selected.Count,
                ["reasonKey"] = "rule.hand_limit",
            });
        }

        flow.Advance(state, request.ActorPlayerIndex);
        if (!state.WinnerPlayerIndex.HasValue && state.Turn.PhaseId == TurnPhase.End)
        {
            flow.Advance(state, request.ActorPlayerIndex);
        }

        // CompleteTurn deliberately lands on START so the phase lifecycle is
        // observable. A player command must nevertheless finish the automatic
        // START work for the incoming player; otherwise the public action
        // gateway would expose an empty action list and the match would stall
        // after its first handoff.
        if (!state.WinnerPlayerIndex.HasValue && state.Turn.PhaseId == TurnPhase.Start)
        {
            flow.Advance(state, state.CurrentPlayerIndex);
        }

        return GameActionResult.Accept();
    }

    public static int RequiredDiscardCount(GameState state, PlayerState player)
    {
        var pioneer = player.Leader is not null && state.GetOpponent(player.PlayerIndex).Leader is null;
        var limit = state.Rules.HandLimit + (pioneer ? state.Rules.PioneerHandLimitBonus : 0);
        return Math.Max(0, player.Hand.Count - limit);
    }

    private static CardInstance? FindInHand(PlayerState player, long id)
    {
        foreach (var card in player.Hand)
        {
            if (card.InstanceId == id)
            {
                return card;
            }
        }

        return null;
    }

    private sealed class DiscardCommand : IGameCommand
    {
        private readonly PlayerState _player;
        private readonly IReadOnlyList<CardInstance> _cards;

        public DiscardCommand(PlayerState player, IReadOnlyList<CardInstance> cards)
        {
            _player = player;
            _cards = cards;
        }

        public void Apply(GameState state)
        {
            foreach (var card in _cards)
            {
                _player.Hand.Remove(card);
                _player.Graveyard.Add(card);
            }
        }
    }
}
}
