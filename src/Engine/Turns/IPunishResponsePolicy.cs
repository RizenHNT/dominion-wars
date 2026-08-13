using System;
using System.Collections.Generic;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

/// <summary>
/// Supplies the player's optional response during a punish chain. A headless
/// AI can decide synchronously; a UI adapter may replace this with a queued
/// command bridge without changing card resolution rules.
/// </summary>
public interface IPunishResponsePolicy
{
    PunishResponseDecision Decide(GameState state, CardInstance card, int effectivePunish, int chainDepth);
}

public readonly struct PunishResponseDecision
{
    private PunishResponseDecision(
        bool activate,
        string? targetId,
        IReadOnlyList<long>? selectedDiscardIds)
    {
        Activate = activate;
        TargetId = targetId;
        SelectedDiscardIds = selectedDiscardIds ?? Array.Empty<long>();
    }

    public bool Activate { get; }
    public string? TargetId { get; }
    public IReadOnlyList<long> SelectedDiscardIds { get; }

    public static PunishResponseDecision Decline() => new PunishResponseDecision(false, null, null);
    public static PunishResponseDecision Accept(
        string? targetId = null,
        IReadOnlyList<long>? selectedDiscardIds = null)
        => new PunishResponseDecision(true, targetId, selectedDiscardIds);
}

public sealed class DeclinePunishResponsePolicy : IPunishResponsePolicy
{
    public PunishResponseDecision Decide(GameState state, CardInstance card, int effectivePunish, int chainDepth)
    {
        return PunishResponseDecision.Decline();
    }
}
}
