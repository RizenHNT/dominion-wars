using System;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Engine.Events;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;

namespace DominionWars.Engine
{

/// <summary>
/// The single engine entry point for an already constructed match.
/// Composition (setup, seed, decks and rules) stays outside this gateway.
/// </summary>
public sealed class MatchController
{
    private readonly GameState _state;
    private readonly TurnFlow _flow;
    private readonly TurnActionRouter _router;

    public MatchController(GameState state, TurnFlow flow, TurnActionRouter router)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        if (!ReferenceEquals(_router.Flow, _flow))
        {
            throw new ArgumentException("The action router must use the controller's turn flow.", nameof(router));
        }
    }

    public IReadOnlyList<LegalAction> GetLegalActions(int actorPlayerIndex)
    {
        if (actorPlayerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(actorPlayerIndex));
        }

        if (_state.WinnerPlayerIndex.HasValue
            || actorPlayerIndex != _state.CurrentPlayerIndex)
        {
            return Array.Empty<LegalAction>();
        }

        return _flow.GetLegalActions(_state, actorPlayerIndex);
    }

    public MatchActionResult Submit(GameActionRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var eventCount = _state.Events.Count;
        var result = _router.Execute(_state, request);
        var delta = _state.Events.Items.Skip(eventCount).ToArray();
        return new MatchActionResult(result, delta);
    }
}

/// <summary>Action result plus only the events emitted by that submission.</summary>
public sealed class MatchActionResult
{
    public MatchActionResult(GameActionResult action, IReadOnlyList<GameEvent> events)
    {
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Events = events is null
            ? throw new ArgumentNullException(nameof(events))
            : Array.AsReadOnly(events.ToArray());
    }

    public GameActionResult Action { get; }
    public IReadOnlyList<GameEvent> Events { get; }
    public bool Accepted => Action.Accepted;
    public string ReasonKey => Action.ReasonKey;
}
}
