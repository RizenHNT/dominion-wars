using System;
using System.Collections.Generic;
using DominionWars.Engine.Command;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

/// <summary>Resolves the currently approved START rules before entering AMBUSH.</summary>
public sealed class StartPhaseHandler : IPhaseHandler, IPhaseLifecycleHandler
{
    public string PhaseId => TurnPhase.Start;

    public IReadOnlyList<LegalAction> GetLegalActions(GameState state, int playerIndex)
    {
        return Array.Empty<LegalAction>();
    }

    public void BeforeAdvance(GameState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var player = state.CurrentPlayer;
        state.Commands.Execute(state, new StartPhaseCommand(player));
        var root = state.Events.Append("TURN_STARTED", null, new Dictionary<string, object?>
        {
            ["player"] = player.PlayerIndex,
            ["turn"] = state.Turn.Number,
        });

        var drawCount = player.PlayerIndex == 1 && state.Turn.Number == 2 ? 2 : 1;
        new EffectRuntime(state).DrawForTurn(player.PlayerIndex, drawCount, root.EventId);
    }

    private sealed class StartPhaseCommand : IGameCommand
    {
        private readonly PlayerState _player;

        public StartPhaseCommand(PlayerState player)
        {
            _player = player;
        }

        public void Apply(GameState state)
        {
            foreach (var card in _player.Field)
            {
                card.AttacksUsed = 0;
                card.SummonedThisTurn = false;
            }
        }
    }
}
}
