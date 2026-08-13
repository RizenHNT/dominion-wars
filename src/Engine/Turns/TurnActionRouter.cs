using System;
using System.Collections.Generic;
using DominionWars.Engine.Model;
using DominionWars.Engine.Targeting;

namespace DominionWars.Engine.Turns
{

/// <summary>
/// Single entry for player-originated actions. Unknown or phase-incompatible
/// requests are rejected instead of allowing callers to mutate GameState.
/// </summary>
public sealed class TurnActionRouter
{
    private readonly IReadOnlyList<ITurnActionHandler> _handlers;

    public TurnActionRouter(TurnFlow flow, IEnumerable<ITurnActionHandler>? handlers = null)
    {
        Flow = flow ?? throw new ArgumentNullException(nameof(flow));
        _handlers = handlers is null
            ? Array.Empty<ITurnActionHandler>()
            : new List<ITurnActionHandler>(handlers).AsReadOnly();
        foreach (var handler in _handlers)
        {
            if (handler is null)
            {
                throw new ArgumentException("Action handlers cannot contain null.", nameof(handlers));
            }
        }
    }

    public TurnFlow Flow { get; }

    public static TurnActionRouter CreateDefault(
        TurnFlow flow,
        TargetPolicy? targetPolicy = null,
        IPunishResponsePolicy? punishResponses = null)
    {
        return new TurnActionRouter(flow, new ITurnActionHandler[]
        {
            new PlayCardActionHandler(targetPolicy, punishResponses),
            new AttackActionHandler(),
        });
    }

    public GameActionResult Execute(GameState state, GameActionRequest request)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (state.WinnerPlayerIndex.HasValue)
        {
            return GameActionResult.Reject("action.game_over");
        }

        if (request.ActorPlayerIndex != state.CurrentPlayerIndex)
        {
            return GameActionResult.Reject("action.not_current_player");
        }

        if (Flow.TryExecutePhaseAction(state, request.ActorPlayerIndex, request.ActionType))
        {
            return GameActionResult.Accept();
        }

        foreach (var handler in _handlers)
        {
            if (handler.CanHandle(state.Turn.PhaseId, request.ActionType))
            {
                return handler.Execute(state, request, Flow);
            }
        }

        return GameActionResult.Reject("action.not_legal_in_phase");
    }
}
}
