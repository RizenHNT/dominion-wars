using System;
using System.Collections.Generic;
using DominionWars.Engine.Command;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

/// <summary>
/// Registered phase route for one match. The default route is five phases, but
/// callers can supply another ordered route and handlers for future phases.
/// </summary>
public sealed class TurnFlow
{
    private readonly IReadOnlyDictionary<string, IPhaseHandler> _handlers;
    private readonly IReadOnlyList<string> _route;

    public TurnFlow(IEnumerable<IPhaseHandler> handlers, IEnumerable<string> route)
    {
        if (handlers is null)
        {
            throw new ArgumentNullException(nameof(handlers));
        }

        if (route is null)
        {
            throw new ArgumentNullException(nameof(route));
        }

        var lookup = new Dictionary<string, IPhaseHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            if (handler is null || string.IsNullOrWhiteSpace(handler.PhaseId))
            {
                throw new ArgumentException("Phase handlers need stable ids.", nameof(handlers));
            }

            if (!lookup.TryAdd(handler.PhaseId, handler))
            {
                throw new ArgumentException($"Duplicate phase handler '{handler.PhaseId}'.", nameof(handlers));
            }
        }

        var ordered = new List<string>();
        foreach (var phaseId in route)
        {
            if (string.IsNullOrWhiteSpace(phaseId) || !lookup.ContainsKey(phaseId))
            {
                throw new ArgumentException($"Route phase '{phaseId}' is not registered.", nameof(route));
            }

            if (ordered.Contains(phaseId))
            {
                throw new ArgumentException($"Route phase '{phaseId}' appears more than once.", nameof(route));
            }

            ordered.Add(phaseId);
        }

        if (ordered.Count == 0)
        {
            throw new ArgumentException("A turn route must have at least one phase.", nameof(route));
        }

        _handlers = lookup;
        _route = ordered.AsReadOnly();
    }

    public IReadOnlyCollection<string> RegisteredPhaseIds => new List<string>(_handlers.Keys).AsReadOnly();
    public IReadOnlyList<string> Route => _route;

    public static TurnFlow CreateDefault(LegalActionGenerator? actionGenerator = null)
    {
        var actions = actionGenerator ?? new LegalActionGenerator();
        return new TurnFlow(
            new IPhaseHandler[]
            {
                new StartPhaseHandler(),
                new DelegatePhaseHandler(TurnPhase.Ambush, CreateAmbushActions),
                new DelegatePhaseHandler(TurnPhase.Action, actions.Generate),
                new DelegatePhaseHandler(TurnPhase.Discard, EmptyActions),
                new DelegatePhaseHandler(TurnPhase.End, EmptyActions),
                new DelegatePhaseHandler(TurnPhase.Over, EmptyActions),
            },
            new[] { TurnPhase.Start, TurnPhase.Ambush, TurnPhase.Action, TurnPhase.Discard, TurnPhase.End });
    }

    public IReadOnlyList<LegalAction> GetLegalActions(GameState state, int playerIndex)
    {
        ValidateActor(state, playerIndex);
        if (state.WinnerPlayerIndex.HasValue || playerIndex != state.CurrentPlayerIndex)
        {
            return Array.Empty<LegalAction>();
        }

        return GetHandler(state.Turn.PhaseId).GetLegalActions(state, playerIndex);
    }

    /// <summary>Moves to the next registered phase, or hands the turn to the opponent after END.</summary>
    public void Advance(GameState state, int playerIndex)
    {
        ValidateActor(state, playerIndex);
        if (state.WinnerPlayerIndex.HasValue)
        {
            Transition(state, TurnPhase.Over, "game_over");
            return;
        }

        if (state.EndTurnRequested && state.Turn.PhaseId != TurnPhase.Discard && state.Turn.PhaseId != TurnPhase.End)
        {
            Transition(state, TurnPhase.Discard, "forced_end");
            return;
        }

        var currentIndex = IndexOfRoutePhase(state.Turn.PhaseId);
        if (GetHandler(state.Turn.PhaseId) is IPhaseLifecycleHandler lifecycle)
        {
            lifecycle.BeforeAdvance(state);
            if (state.WinnerPlayerIndex.HasValue)
            {
                Transition(state, TurnPhase.Over, "game_over");
                return;
            }
        }

        if (currentIndex + 1 < _route.Count)
        {
            Transition(state, _route[currentIndex + 1], "advance");
            return;
        }

        CompleteTurn(state);
    }

    /// <summary>Requests an explicit phase jump. Unknown phases fail closed.</summary>
    public void JumpTo(GameState state, int playerIndex, string phaseId, string reason = "jump")
    {
        ValidateActor(state, playerIndex);
        if (string.IsNullOrWhiteSpace(phaseId))
        {
            throw new ArgumentException("A destination phase is required.", nameof(phaseId));
        }

        GetHandler(phaseId);
        Transition(state, phaseId, reason);
    }

    /// <summary>Handles the phase-only subset of transport actions; card resolution stays separate.</summary>
    public bool TryExecutePhaseAction(GameState state, int playerIndex, string actionType)
    {
        ValidateActor(state, playerIndex);
        if (string.IsNullOrWhiteSpace(actionType))
        {
            throw new ArgumentException("An action type is required.", nameof(actionType));
        }

        if (state.Turn.PhaseId == TurnPhase.Ambush && actionType == TurnAction.SkipAmbush)
        {
            Advance(state, playerIndex);
            return true;
        }

        if (state.Turn.PhaseId == TurnPhase.Action && actionType == LegalActionGenerator.EndTurn)
        {
            Execute(state, item => item.EndTurnRequested = true);
            Advance(state, playerIndex);
            return true;
        }

        return false;
    }

    private static IReadOnlyList<LegalAction> EmptyActions(GameState _, int __)
    {
        return Array.Empty<LegalAction>();
    }

    private static IReadOnlyList<LegalAction> CreateAmbushActions(GameState _, int playerIndex)
    {
        return new LegalAction[]
        {
            new LegalAction { ActionId = $"skip_ambush_{playerIndex}", Type = TurnAction.SkipAmbush, Actor = playerIndex, ReasonKey = "action.skip_ambush" },
        };
    }

    private void CompleteTurn(GameState state)
    {
        var outgoing = state.CurrentPlayer;
        Execute(state, item =>
        {
            outgoing.PunishDeltaThisTurn = 0;
            outgoing.PunishToSelfDiscardThisTurn = false;
            outgoing.ProtectedThisTurn = false;
            outgoing.EffectsNegatedThisTurn = false;
            outgoing.PunishDrawnThisTurn = 0;
            item.EndTurnRequested = false;
            item.CurrentPlayerIndex = 1 - item.CurrentPlayerIndex;
            checked { item.Turn.Number++; }
            item.Turn.SetPhase(TurnPhase.Start);
        });
        state.Events.Append("TURN_CHANGED", null, Data("currentPlayer", state.CurrentPlayerIndex, "turn", state.Turn.Number));
        state.Events.Append("PHASE_CHANGED", null, Data("from", TurnPhase.End, "to", TurnPhase.Start, "reason", "turn_handoff"));
    }

    private void Transition(GameState state, string destination, string reason)
    {
        GetHandler(destination);
        var previous = state.Turn.PhaseId;
        if (previous == destination)
        {
            return;
        }

        Execute(state, item => item.Turn.SetPhase(destination));
        state.Events.Append("PHASE_CHANGED", null, Data("from", previous, "to", destination, "reason", reason));
    }

    private int IndexOfRoutePhase(string phaseId)
    {
        for (var index = 0; index < _route.Count; index++)
        {
            if (string.Equals(_route[index], phaseId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new InvalidOperationException($"Current phase '{phaseId}' is not in this turn route.");
    }

    private IPhaseHandler GetHandler(string phaseId)
    {
        if (!_handlers.TryGetValue(phaseId, out var handler))
        {
            throw new InvalidOperationException($"Phase '{phaseId}' is not registered.");
        }

        return handler;
    }

    private static void ValidateActor(GameState state, int playerIndex)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (playerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(playerIndex));
        }

        if (state.CurrentPlayerIndex != playerIndex)
        {
            throw new InvalidOperationException("Only the active player can advance this turn.");
        }
    }

    private static void Execute(GameState state, Action<GameState> mutation)
    {
        state.Commands.Execute(state, new TurnCommand(mutation));
    }

    private static IReadOnlyDictionary<string, object?> Data(params object?[] values)
    {
        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < values.Length; index += 2)
        {
            data[(string)values[index]!] = values[index + 1];
        }

        return data;
    }

    private sealed class TurnCommand : IGameCommand
    {
        private readonly Action<GameState> _mutation;

        public TurnCommand(Action<GameState> mutation)
        {
            _mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
        }

        public void Apply(GameState state) => _mutation(state);
    }

    private sealed class DelegatePhaseHandler : IPhaseHandler
    {
        private readonly Func<GameState, int, IReadOnlyList<LegalAction>> _actions;

        public DelegatePhaseHandler(string phaseId, Func<GameState, int, IReadOnlyList<LegalAction>> actions)
        {
            PhaseId = phaseId;
            _actions = actions;
        }

        public string PhaseId { get; }

        public IReadOnlyList<LegalAction> GetLegalActions(GameState state, int playerIndex) => _actions(state, playerIndex);
    }
}

public static class TurnAction
{
    public const string SetAmbush = "SET_AMBUSH";
    public const string SkipAmbush = "SKIP_AMBUSH";
    public const string DiscardComplete = "DISCARD";
}
}
