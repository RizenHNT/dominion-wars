using System;

namespace DominionWars.Engine.Model
{

/// <summary>
/// Mutable match-turn metadata owned by the engine. Phase ids stay strings so
/// a future rules package can register phases without changing this model.
/// </summary>
public sealed class TurnState
{
    public int Number { get; internal set; } = 1;

    public string PhaseId { get; internal set; } = "START";

    internal void SetPhase(string phaseId)
    {
        if (string.IsNullOrWhiteSpace(phaseId))
        {
            throw new ArgumentException("A phase id is required.", nameof(phaseId));
        }

        PhaseId = phaseId;
    }
}
}
