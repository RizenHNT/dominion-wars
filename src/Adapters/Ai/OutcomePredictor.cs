using System;
using System.Collections.Generic;
using System.Globalization;

namespace DominionWars.Adapters.Ai
{

/// <summary>
/// What one candidate move does to the position, as opposed to what the position
/// already is.
///
/// THE LAYERING THIS TYPE EXISTS TO KEEP:
///  - <see cref="StateEvaluator"/> reports what is TRUE now (counters, board, threat).
///  - <see cref="MoveSettlements"/> settles what ONE move COSTS (the engine's resolved
///    punish).
///  - This type combines the two into what the move DOES: how much card flow it hands
///    over, and how that flow changes the opponent's threat profile.
///
/// Collapsing these was the defect the owner caught: summing the punish across a whole
/// hand is a state-shaped number with no decision meaning, because only a CHOSEN move
/// transfers anything.
/// </summary>
public sealed class MoveOutcome
{
    public MoveOutcome(
        MoveSettlement settlement,
        int opponentHandBefore,
        int opponentHandAfter,
        IReadOnlyList<ThreatShift> threatShifts,
        int? myObjectiveRemainingBefore,
        string? myObjectiveNote)
    {
        Settlement = settlement ?? throw new ArgumentNullException(nameof(settlement));
        OpponentHandBefore = opponentHandBefore;
        OpponentHandAfter = opponentHandAfter;
        ThreatShifts = threatShifts ?? throw new ArgumentNullException(nameof(threatShifts));
        MyObjectiveRemainingBefore = myObjectiveRemainingBefore;
        MyObjectiveNote = myObjectiveNote;
    }

    public MoveSettlement Settlement { get; }

    public int OpponentHandBefore { get; }

    /// <summary>The opponent's hand size after the punish draws resolve.</summary>
    public int OpponentHandAfter { get; }

    /// <summary>How many cards the move hands the opponent.</summary>
    public int CardsHandedOver => Settlement.AdvertisedPunish ?? 0;

    /// <summary>Per-category change in the opponent's expected holdings.</summary>
    public IReadOnlyList<ThreatShift> ThreatShifts { get; }

    /// <summary>My objective distance before the move, when it is measurable.</summary>
    public int? MyObjectiveRemainingBefore { get; }

    /// <summary>
    /// Why my objective distance after the move is NOT reported. Present whenever the
    /// move's own effect on my axis cannot be resolved from the advertisement.
    /// </summary>
    public string? MyObjectiveNote { get; }

    public override string ToString()
    {
        var hand = Settlement.AdvertisedPunish.HasValue
            ? "opponent hand " + OpponentHandBefore.ToString(CultureInfo.InvariantCulture)
              + " -> " + OpponentHandAfter.ToString(CultureInfo.InvariantCulture)
            : "no card flow transferred";
        var shifts = ThreatShifts.Count == 0
            ? "no threat shift"
            : string.Join(", ", Select(ThreatShifts, s => s.ToString()));
        return Settlement.ActionType + " " + Settlement.ActionId + ": " + hand + "; " + shifts;
    }

    private static List<string> Select(IReadOnlyList<ThreatShift> shifts, Func<ThreatShift, string> project)
    {
        var result = new List<string>(shifts.Count);
        foreach (var shift in shifts) result.Add(project(shift));
        return result;
    }
}

/// <summary>
/// The change in ONE threat category's expected presence in the opponent's hand,
/// caused by a move handing them cards.
/// </summary>
public sealed class ThreatShift
{
    public ThreatShift(ThreatKind kind, double before, double after, double delta, string note)
    {
        Kind = kind;
        Before = before;
        After = after;
        Delta = delta;
        Note = note ?? string.Empty;
    }

    public ThreatKind Kind { get; }

    public double Before { get; }

    public double After { get; }

    public double Delta { get; }

    public string Note { get; }

    public override string ToString()
        => Kind + " " + Before.ToString("0.##", CultureInfo.InvariantCulture) + "->"
           + After.ToString("0.##", CultureInfo.InvariantCulture) + " (+"
           + Delta.ToString("0.##", CultureInfo.InvariantCulture) + ")";
}

/// <summary>
/// Predicts what a candidate move does to the position.
///
/// WHAT IT CAN PREDICT HONESTLY:
///  - The card flow: the engine resolved the punish, so the number of cards the
///    opponent draws is known exactly, and so is their hand size afterwards.
///  - The consequent threat shift: the cards come from the unseen pool whose
///    composition is public, so each category's expected holding grows by
///    (draws x category density). That is arithmetic on published data, not a guess.
///
/// WHAT IT DELIBERATELY DOES NOT PREDICT (yet):
///  - How the move advances MY OWN victory axis. That depends on the move's effects
///    resolving in the engine, which the advertisement does not describe. It is
///    reported as unknown WITH A REASON rather than estimated, because a fabricated
///    self-advance number would be the most misleading possible field in this type.
///  - What the opponent will do in reply. That is a search question, not a state
///    question, and it belongs above this layer.
/// </summary>
public static class OutcomePredictor
{

/// <summary>
/// Predicts one move's consequences. <paramref name="threat"/> is the CURRENT
/// estimate (usually from the state evaluator); the prediction shifts it by the card
/// flow rather than recomputing it.
/// </summary>
public static MoveOutcome Predict(
    ThreatReport threat,
    RuntimeLegalAction action,
    VictoryObjective? myObjective = null)
{
    if (threat is null) throw new ArgumentNullException(nameof(threat));

    var settlement = MoveSettlements.Settle(action);
    var handedOver = settlement.AdvertisedPunish ?? 0;
    var handBefore = threat.OpponentHandCount;
    var handAfter = handBefore + handedOver;

    return new MoveOutcome(
        settlement,
        handBefore,
        handAfter,
        Shift(threat, handedOver),
        myObjective?.Remaining,
        myObjective is null
            ? "no objective was supplied, so the move's effect on my own axis is unknown"
            : "the advertisement does not describe how this move's effects advance my axis, "
              + "so the post-move distance is unknown rather than estimated");
}

/// <summary>Predicts every advertised move, in the order supplied.</summary>
public static IReadOnlyList<MoveOutcome> PredictAll(
    ThreatReport threat,
    IReadOnlyList<RuntimeLegalAction> actions,
    VictoryObjective? myObjective = null)
{
    if (actions is null) throw new ArgumentNullException(nameof(actions));

    var result = new List<MoveOutcome>(actions.Count);
    foreach (var action in actions)
    {
        if (action is null) continue;
        result.Add(Predict(threat, action, myObjective));
    }

    return result;
}

/// <summary>
/// How each threat category's expected holdings move when the opponent draws
/// <paramref name="draws"/> cards from the unseen pool.
///
/// The draw is a uniform sample of the pool, so a category holding S of the pool's U
/// copies gains draws x (S/U) in expectation. This is exactly the hypergeometric mean
/// applied incrementally, which is why it can be computed from the existing estimate
/// instead of re-deriving it.
/// </summary>
private static IReadOnlyList<ThreatShift> Shift(ThreatReport threat, int draws)
{
    var shifts = new List<ThreatShift>();
    if (draws <= 0) return shifts;

    var pool = threat.UnknownPoolSize;
    if (pool <= 0) return shifts;

    foreach (var probability in threat.Probabilities)
    {
        var unseen = probability.UnseenCopies;
        var density = unseen / (double)pool;
        var before = probability.ExpectedCardsInHand;
        var delta = draws * density;
        shifts.Add(new ThreatShift(
            probability.Kind,
            before,
            before + delta,
            delta,
            "the opponent gains " + delta.ToString("0.##", CultureInfo.InvariantCulture)
            + " more " + probability.Kind + " cards in expectation ("
            + draws.ToString(CultureInfo.InvariantCulture) + " draws at density "
            + density.ToString("0.###", CultureInfo.InvariantCulture) + ")"));
    }

    return shifts;
}

/// <summary>
/// The predicted threat shift for one category, or null when the move hands over
/// nothing or the category was not estimated.
/// </summary>
public static ThreatShift? ShiftFor(MoveOutcome outcome, ThreatKind kind)
{
    if (outcome is null) throw new ArgumentNullException(nameof(outcome));
    foreach (var shift in outcome.ThreatShifts)
    {
        if (shift.Kind == kind) return shift;
    }

    return null;
}

}

}
