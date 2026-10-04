using System;
using DominionWars.Engine.Events;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;

namespace DominionWars.Adapters.Ai
{

/// <summary>
/// Lower bracket: decline every optional punish arrival/response. Semantically
/// identical to the engine's own <see cref="DeclinePunishResponsePolicy"/>
/// fallback, and therefore identical to what every match built by
/// <c>MatchFactory</c> does today — naming it lets a report say which stance
/// produced a reading instead of relying on the absence of an injection.
/// </summary>
public sealed class NeverPunishResponses : IPunishResponsePolicy
{
    public PunishResponseDecision Decide(
        GameState state,
        CardInstance card,
        int effectivePunish,
        int chainDepth)
    {
        return PunishResponseDecision.Decline();
    }
}

/// <summary>
/// Upper bracket: accept every optional punish arrival/response.
///
/// Unconditional by design — the bracket exists to remove the decision, so it
/// reads neither the state, the card, the amount, nor the chain depth — and it
/// reproduces the harness's existing greedy-accept instrument exactly
/// (<c>build-output/pl-csim/RecordingPunishPolicy.cs</c> with <c>accept: true</c>):
/// a bare <see cref="PunishResponseDecision.Accept"/> with no target and no
/// punish-converted discard selection. Two consequences are known and honest:
/// <list type="bullet">
/// <item>a response whose punish effects require a target (or a discard
/// selection, because the punish was converted to self-discard) is rejected
/// during preparation and therefore does not resolve, so this is the existing
/// instrument's upper bracket rather than the theoretical maximum;</item>
/// <item>it applies no response budget of its own. Budgets are a RULE
/// (<c>MatchRules.MaxPunishResponsesPerRound</c>) and the engine suppresses
/// over-budget offers before it asks, which is the correct layering: rules stay
/// in the engine, the decision stays in the policy.</item>
/// </list>
/// </summary>
public sealed class AlwaysPunishResponses : IPunishResponsePolicy
{
    public PunishResponseDecision Decide(
        GameState state,
        CardInstance card,
        int effectivePunish,
        int chainDepth)
    {
        return PunishResponseDecision.Accept();
    }
}

/// <summary>
/// "Accept the first response of each punish round, decline the rest."
///
/// A punish round is the engine's own unit: every response hanging off one root
/// action event (the log's <c>CARD_PLAYED</c> / <c>COMMIT_DECLARED</c> /
/// <c>PULL_DECLARED</c>, all appended with no parent). The policy derives both
/// the round and what it has already spent from the event log — the newest root
/// event (<see cref="EventLog.IsRootEvent"/>'s notion) and the
/// <c>PUNISH_TRIGGERED</c> events appended under it. That is the same
/// accounting the engine's own T1 rule uses (<c>PunishRound.AcceptedCount</c> is
/// incremented only after a response actually resolves), so:
/// <list type="bullet">
/// <item>it counts RESOLVED responses, not accepted offers, which is stricter
/// and more honest than the harness's policy-side cap emulation (that one spends
/// the cap at decision time, so an offer rejected during preparation still
/// consumes the round's allowance);</item>
/// <item>it holds no state between calls, so one shared instance cannot leak a
/// round between two matches or between the two players;</item>
/// <item>when the engine rule <c>MaxPunishResponsesPerRound</c> is enabled the
/// engine suppresses over-budget offers first, so this stance becomes a
/// redundant backstop rather than a second, competing budget.</item>
/// </list>
/// </summary>
public sealed class FirstInRoundPunishResponses : IPunishResponsePolicy
{
    public PunishResponseDecision Decide(
        GameState state,
        CardInstance card,
        int effectivePunish,
        int chainDepth)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        return ResolvedResponsesInCurrentRound(state) == 0
            ? PunishResponseDecision.Accept()
            : PunishResponseDecision.Decline();
    }

    /// <summary>
    /// How many responses have already resolved in the punish round that is
    /// resolving now. 0 means this offer is the first of its round.
    /// </summary>
    public static int ResolvedResponsesInCurrentRound(GameState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        var events = state.Events.Items;
        var root = CurrentRoundRoot(state);
        var resolved = 0;
        for (var index = 0; index < events.Count; index++)
        {
            if (events[index].ParentEventId != root) continue;
            if (string.Equals(events[index].EventType, "PUNISH_TRIGGERED", StringComparison.Ordinal))
                resolved++;
        }

        return resolved;
    }

    /// <summary>
    /// The root event id of the punish round currently resolving: the newest
    /// event in the log that has no parent. During a chain that is the root
    /// action event of the round, because the engine appends it before it draws
    /// or offers anything and turn/phase events are only appended between
    /// actions. Returns 0 for an empty log.
    /// </summary>
    public static long CurrentRoundRoot(GameState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        var events = state.Events.Items;
        for (var index = events.Count - 1; index >= 0; index--)
        {
            if (events[index].ParentEventId is null) return events[index].EventId;
        }

        return 0;
    }
}

}
