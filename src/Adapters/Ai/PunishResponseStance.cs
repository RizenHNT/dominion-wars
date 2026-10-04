using System;
using DominionWars.Engine.Turns;

namespace DominionWars.Adapters.Ai
{

/// <summary>
/// How a playstyle treats the optional punish arrival/response the engine offers
/// while a punish draw resolves (docs/PL_AI_PLAYSTYLE_FRAMEWORK_2026-09-11.md §3.5).
///
/// This is a second, separate decision point next to the ACTION-phase weights: a
/// response is never an advertised action, so no weight can accept or decline
/// one. It is carried on the playstyle because it is part of how that playstyle
/// plays, and because the shipped runtime currently injects no response policy
/// at all, which means every match built by <c>MatchFactory</c> declines every
/// response (the T1 rule is inert there until a caller injects a stance).
/// </summary>
public enum PunishResponseStance
{
    /// <summary>Decline every offered response: the lower bound, and today's shipped behaviour.</summary>
    Never = 0,

    /// <summary>
    /// Accept at most one response per punish round (the engine's own unit: all
    /// responses hanging off one root action event). Semantically the same rule
    /// as <c>MatchRules.MaxPunishResponsesPerRound = 1</c>, applied by the
    /// policy instead of by the engine, so it also holds when that rule is off.
    /// </summary>
    FirstInRound = 1,

    /// <summary>Accept every offered response: the upper bound.</summary>
    Always = 2,
}

/// <summary>
/// The three stances as ready-to-inject engine policies.
///
/// A caller wires a playstyle's stance into a match with the engine's own seam,
/// which <c>MatchFactory</c> does not (yet) do:
/// <code>
/// var playstyle = PlaystyleRegistry.GetPlaystyle("aggro");
/// var router = TurnActionRouter.CreateDefault(flow, punishResponses: PunishResponseStances.PolicyFor(playstyle));
/// </code>
/// </summary>
public static class PunishResponseStances
{
    /// <summary>Shared lower-bracket policy: declines every offer.</summary>
    public static IPunishResponsePolicy NeverPolicy { get; } = new NeverPunishResponses();

    /// <summary>Shared "one per punish round" policy.</summary>
    public static IPunishResponsePolicy FirstInRoundPolicy { get; } = new FirstInRoundPunishResponses();

    /// <summary>Shared upper-bracket policy: accepts every offer.</summary>
    public static IPunishResponsePolicy AlwaysPolicy { get; } = new AlwaysPunishResponses();

    /// <summary>The policy that implements one stance.</summary>
    public static IPunishResponsePolicy PolicyFor(PunishResponseStance stance)
    {
        switch (stance)
        {
            case PunishResponseStance.Never: return NeverPolicy;
            case PunishResponseStance.FirstInRound: return FirstInRoundPolicy;
            case PunishResponseStance.Always: return AlwaysPolicy;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(stance),
                    "Unhandled punish-response stance " + stance + ".");
        }
    }

    /// <summary>
    /// The policy a playstyle asks for. This is the single place a caller needs
    /// in order to hand a playstyle's response stance to
    /// <c>TurnActionRouter.CreateDefault(flow, punishResponses: …)</c>.
    /// </summary>
    public static IPunishResponsePolicy PolicyFor(IPlaystyle playstyle)
    {
        if (playstyle is null) throw new ArgumentNullException(nameof(playstyle));
        return PolicyFor(playstyle.ResponseStance);
    }

    /// <summary>
    /// Stable provenance token for a stance, so a report can state which stance
    /// produced a number without printing the policy type name.
    /// </summary>
    public static string IdOf(PunishResponseStance stance)
    {
        switch (stance)
        {
            case PunishResponseStance.Never: return "never";
            case PunishResponseStance.FirstInRound: return "first-in-round";
            case PunishResponseStance.Always: return "always";
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(stance),
                    "Unhandled punish-response stance " + stance + ".");
        }
    }
}

}
