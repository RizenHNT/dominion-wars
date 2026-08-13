using System.Collections.Generic;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

/// <summary>Supplies actions for one registered phase; it never advances the match directly.</summary>
public interface IPhaseHandler
{
    string PhaseId { get; }

    IReadOnlyList<LegalAction> GetLegalActions(GameState state, int playerIndex);
}
}
