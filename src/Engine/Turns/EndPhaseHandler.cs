using System;
using System.Collections.Generic;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Events;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

/// <summary>Resolves deterministic end-of-turn rules before handoff.</summary>
public sealed class EndPhaseHandler : IPhaseHandler, IPhaseLifecycleHandler
{
    public string PhaseId => TurnPhase.End;

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

        GameEvent? root = null;
        for (var index = state.Events.Items.Count - 1; index >= 0; index--)
        {
            var candidate = state.Events.Items[index];
            if (candidate.ParentEventId is null
                && string.Equals(candidate.EventType, "PHASE_CHANGED", StringComparison.Ordinal))
            {
                root = candidate;
                break;
            }
        }

        if (root is null)
        {
            throw new InvalidOperationException("END must be entered through TurnFlow before resolution.");
        }

        new EffectRuntime(state).ResolveEndPhase(state.CurrentPlayerIndex, root.EventId);
    }
}
}
