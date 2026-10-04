using System.Collections.Generic;

namespace DominionWars.Adapters.Ai
{

/// <summary>
/// One CPU-opponent playstyle: an identity, the integer weight vector that turns
/// the pure feature vector of an advertised action (<see cref="ActionFeatures"/>)
/// into a score, and the stance it takes on the optional punish
/// arrival/response the engine offers while a punish draw resolves.
///
/// A playstyle is deliberately data only — a name, a set of numbers, and one
/// enum. Adding an AI that makes different mistakes therefore means adding a
/// weight table and a stance, never a new decision path: the ACTION-phase side
/// can only reorder the actions the engine already advertised, and the response
/// side can only accept or decline an offer the engine already made. The
/// boundary invariants (advertised actions only, no rule derivation, no RNG, no
/// clock) therefore hold for every playstyle by construction.
///
/// Weights are keyed by the feature names published in
/// <see cref="ActionFeatures.Names"/>. A weight may be negative (the feature is a
/// cost for this playstyle) or zero (this playstyle is blind to the feature,
/// which is how the shipped default reproduces the retired hardcoded priority).
///
/// The response stance is a separate seam on purpose: a punish response is never
/// an advertised action, so it cannot be expressed as a weight. Use
/// <see cref="PunishResponseStances.PolicyFor(IPlaystyle)"/> to get the engine
/// policy that implements this playstyle's stance and hand it to
/// <c>TurnActionRouter.CreateDefault(flow, punishResponses: …)</c>.
/// </summary>
public interface IPlaystyle
{
    /// <summary>Stable registry id, for example <c>aggro</c>; used for provenance.</summary>
    string Id { get; }

    /// <summary>
    /// Feature name to weight. Higher score wins, so a negative weight means the
    /// feature is a cost and a positive weight means it is a benefit.
    /// </summary>
    IReadOnlyDictionary<string, int> Weights { get; }

    /// <summary>
    /// How this playstyle treats the optional punish arrival/response the engine
    /// offers. Applied through <c>IPunishResponsePolicy</c>, not through the
    /// weights.
    /// </summary>
    PunishResponseStance ResponseStance { get; }
}

}
