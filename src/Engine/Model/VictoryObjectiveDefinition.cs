using System;
using System.Collections.Generic;

namespace DominionWars.Engine.Model
{

/// <summary>
/// A card's victory objective: which condition decides it, and what satisfies it.
///
/// Its job is to BUILD the <see cref="IVictoryCondition"/> that knows how to measure
/// itself. The objective carries only the identity and the threshold; the arithmetic
/// lives in the condition class, which is the piece a designer adds when they add a way
/// to win.
///
/// This shape is what makes the interface actually uniform. An earlier version made the
/// metric a closed enum and the engine a switch over it, which meant (a) every new
/// condition needed an engine branch anyway, and (b) conditions that are not a threshold
/// on one counter — the castle break — had to be refused. Both problems come from the
/// same mistake: putting the measurement in the evaluator instead of in the condition.
/// </summary>
public sealed class VictoryObjectiveDefinition
{
    public VictoryObjectiveDefinition(string metric, string direction, int target)
    {
        if (string.IsNullOrWhiteSpace(metric))
        {
            throw new ArgumentException("A victory metric id is required.", nameof(metric));
        }

        // The direction is still explicit rather than implied, because "reach 6" and
        // "get down to 0" print differently on a card and mean opposite things. It is
        // descriptive, though: the condition decides how to compare, since a condition
        // like the castle break is neither a floor nor a ceiling.
        if (!VictoryObjectiveDirection.IsKnown(direction))
        {
            throw new ArgumentException(
                "Unknown victory direction '" + direction + "'. It must be "
                + VictoryObjectiveDirection.Increase + " or " + VictoryObjectiveDirection.Decrease + ".",
                nameof(direction));
        }

        if (target < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        Metric = metric;
        Direction = direction;
        Target = target;
    }

    /// <summary>
    /// The condition id. Data, not a comparison suffix: nothing may split it to work out
    /// what it measures. <see cref="VictoryConditions.Create"/> resolves it to the class
    /// that does the measuring.
    /// </summary>
    public string Metric { get; }

    /// <summary>
    /// One of <see cref="VictoryObjectiveDirection"/>. Read by consumers that need to
    /// present the objective; the CONDITION is what decides satisfaction.
    /// </summary>
    public string Direction { get; }

    /// <summary>The threshold that satisfies the condition.</summary>
    public int Target { get; }

    /// <summary>
    /// Builds the condition that measures this objective.
    ///
    /// Throws when the id resolves to nothing, which is the behaviour the closed set used
    /// to provide: a card that names a condition nobody implemented must fail loudly at
    /// load time rather than sit in the deck never firing, which is exactly how
    /// AMBUSH_TRIGGER_WIN is unreachable today.
    /// </summary>
    public IVictoryCondition CreateCondition()
        => VictoryConditions.Create(Metric, Target);

    /// <summary>
    /// How much further this objective has to go for a given current value.
    ///
    /// Signed by the direction, so it reads correctly on a card that counts down. Kept on
    /// the definition because a consumer holding only the published contract has the
    /// current value and needs the distance; the CONDITION remains the authority on
    /// whether the condition is actually met.
    /// </summary>
    public int Remaining(int current)
    {
        return string.Equals(Direction, VictoryObjectiveDirection.Decrease, StringComparison.Ordinal)
            ? current - Target
            : Target - current;
    }
}

/// <summary>Which way progress on an objective runs, for presentation.</summary>
public static class VictoryObjectiveDirection
{
    /// <summary>The metric must reach or exceed the target (the usual case).</summary>
    public const string Increase = "INCREASE";

    /// <summary>The metric must fall to or below the target.</summary>
    public const string Decrease = "DECREASE";

    public static bool IsKnown(string? direction)
    {
        return string.Equals(direction, Increase, StringComparison.Ordinal)
            || string.Equals(direction, Decrease, StringComparison.Ordinal);
    }
}

/// <summary>
/// Maps a condition id to the class that measures it — THE registration point for a new
/// way to win.
///
/// Adding a condition is: write a class implementing <see cref="IVictoryCondition"/>, add
/// one line here, and put the id in card data. Nothing in the engine's evaluator, the
/// snapshot, the AI or the UI changes, because all of them read
/// <see cref="VictoryReading"/> and none of them knows what the id means.
///
/// This is deliberately a registry rather than a switch inside the evaluator. A switch
/// there would mean the AI's view of progress and the engine's decision could be built
/// from different code, and a consumer that disagrees with the engine about who is about
/// to win is the one bug this whole contract exists to prevent.
/// </summary>
public static class VictoryConditions
{
    /// <summary>Every condition id the engine can measure.</summary>
    public static readonly string[] Known =
    {
        "OPPONENT_DISCARD_COUNT",
        "NO_DAMAGE_TURN_STREAK",
        "OPPONENT_PUNISH_DRAW_THIS_TURN",
        "PULL_COUNT",
        "SEALED_MINION_MAX_HEALTH",
        "CASTLE_BREAK",
    };

    /// <summary>
    /// Builds the condition for an id, or throws naming the id and the known set.
    ///
    /// <paramref name="target"/> is passed through; a condition that ignores it — the
    /// castle break is met or not, with no count in between — simply does not use it.
    /// </summary>
    public static IVictoryCondition Create(string metric, int target)
    {
        switch (metric)
        {
            case "OPPONENT_DISCARD_COUNT":
                return new OpponentDiscardCountCondition(target);
            case "NO_DAMAGE_TURN_STREAK":
                return new NoDamageTurnStreakCondition(target);
            case "OPPONENT_PUNISH_DRAW_THIS_TURN":
                return new OpponentPunishDrawThisTurnCondition(target);
            case "PULL_COUNT":
                return new PullCountCondition(target);
            case "SEALED_MINION_MAX_HEALTH":
                return new SealedMinionMaxHealthCondition(target);
            case "CASTLE_BREAK":
                return new CastleBreakCondition(target > 0 ? target : 1);
            default:
                throw new ArgumentException(
                    "Unknown victory condition '" + metric + "'. Add a class implementing IVictoryCondition "
                    + "and register it in VictoryConditions.Create. Known ids: "
                    + string.Join(", ", Known),
                    nameof(metric));
        }
    }

    public static bool IsKnown(string? metric)
    {
        if (string.IsNullOrEmpty(metric)) return false;
        foreach (var known in Known)
        {
            if (string.Equals(known, metric, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}

}
