using System;
using System.Collections.Generic;

namespace DominionWars.Engine.Model
{

/// <summary>
/// ONE victory condition, reduced to a single job: work out how far along it is.
///
/// This is THE extension point. A new way to win is a NEW CLASS implementing this and
/// nothing else. Everything downstream — the engine's win check, the snapshot, the AI's
/// "what should I play" search, the UI — reads progress through <see cref="VictoryReading"/>
/// and therefore needs no change when a new condition appears.
///
/// The point is not "less code". It is that the DECISION algorithms stay still while the
/// RULES grow: a designer adds a win condition by writing one progress calculation, and
/// the AI keeps working because it only ever asks "how much is left".
///
/// Two consequences that the earlier, narrower design got wrong:
///
/// - A condition does NOT have to be a threshold on one counter. The castle break is
///   progress of its own kind and implements this like anything else, instead of being
///   refused as "not a threshold".
/// - Conditions COMPOSE. An "A and B" condition is a class that combines the readings of
///   its children, so a compound win condition is also just a new class.
///
/// Implementations must be deterministic and must not mutate the state they read.
/// </summary>
public interface IVictoryCondition
{
    /// <summary>
    /// A stable id for this condition, used for reporting and for the published
    /// contract. It is data, not a comparison suffix: nothing may split it to work out
    /// what it measures.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Whether SATISFYING this condition, as read here, may declare the game won.
    ///
    /// True for the ordinary threshold conditions. False for a condition whose victory is
    /// decided somewhere else — the castle break is resolved by its own simultaneous
    /// resolution path, where BOTH leaders' claims are compared at once. Reporting such a
    /// condition through this interface is still right (a consumer needs its progress),
    /// but letting it ALSO declare a winner here would add a second, weaker rule: it would
    /// hand the win to whichever seat is evaluated first whenever the real path declines
    /// to decide, which was not true before this interface existed.
    ///
    /// The distinction exists because "how much progress" and "who won" are genuinely
    /// different questions, and collapsing them silently changes the rules.
    /// </summary>
    bool DeclaresWinWhenMet { get; }

    /// <summary>
    /// How far along this condition is, for the side that owns it.
    ///
    /// Reading progress must never throw for a board it does not understand and must
    /// never return a fabricated number: a condition that cannot be measured says so
    /// through <see cref="VictoryReading.UnmeasurableReason"/>.
    /// </summary>
    VictoryReading Read(GameState state, int ownerPlayerIndex);
}

/// <summary>
/// A victory reading in the uniform shape every consumer reads.
///
/// The whole design rests on this being the ONLY thing downstream code sees. Progress is
/// a plain number and "how much is left" is derived from it, so a consumer never has to
/// know whether the number counts discards, pulls, minions or a broken castle.
/// </summary>
public sealed class VictoryReading
{
    private VictoryReading(
        bool measurable,
        int current,
        int target,
        bool met,
        int? remaining,
        string? unmeasurableReason)
    {
        IsMeasurable = measurable;
        Current = current;
        Target = target;
        IsMet = met;
        Remaining = remaining;
        UnmeasurableReason = unmeasurableReason;
    }

    /// <summary>True when this reading carries a real number.</summary>
    public bool IsMeasurable { get; }

    /// <summary>The progress value. Meaningless unless <see cref="IsMeasurable"/>.</summary>
    public int Current { get; }

    /// <summary>The value that satisfies the condition.</summary>
    public int Target { get; }

    /// <summary>Whether the condition is satisfied right now.</summary>
    public bool IsMet { get; }

    /// <summary>
    /// Signed distance to the target: positive means work still to do, zero or below
    /// means met. Null when unmeasurable — never zero, because zero reads as "about to
    /// win" and would turn a missing reading into a false alarm.
    /// </summary>
    public int? Remaining { get; }

    /// <summary>Why this reading is unmeasurable. Null when it is measurable.</summary>
    public string? UnmeasurableReason { get; }

    /// <summary>A reading that carries a value. Used by every concrete condition.</summary>
    public static VictoryReading Measured(int current, int target)
    {
        var met = current >= target;
        return new VictoryReading(
            measurable: true,
            current: current,
            target: target,
            met: met,
            remaining: met ? 0 : target - current,
            unmeasurableReason: null);
    }

    /// <summary>
    /// A reading for a condition that already holds regardless of any counter — the
    /// castle break once the castle is down, for example.
    /// </summary>
    public static VictoryReading AlreadyMet(int target = 0)
        => new VictoryReading(true, target, target, met: true, remaining: 0, unmeasurableReason: null);

    /// <summary>
    /// A reading for a condition that cannot be measured here. It names what is missing
    /// rather than reporting zero progress, so a gap is visible instead of looking like
    /// a game that has barely started.
    /// </summary>
    public static VictoryReading Unmeasurable(string reason)
        => new VictoryReading(false, 0, 0, met: false, remaining: null, unmeasurableReason: reason);
}

/// <summary>
/// A victory condition that is a threshold on one published counter.
///
/// This is the common case, not the only case — it exists so the ordinary conditions
/// stay one short class each instead of repeating the same arithmetic.
/// </summary>
public abstract class ThresholdVictoryCondition : IVictoryCondition
{
    protected ThresholdVictoryCondition(string id, int target)
    {
        Id = id;
        Target = target;
    }

    public string Id { get; }

    public int Target { get; }

    /// <summary>
    /// A threshold being reached IS the victory for every shipped condition of this kind,
    /// so the default is true. A condition whose win is decided elsewhere overrides it.
    /// </summary>
    public virtual bool DeclaresWinWhenMet => true;

    /// <summary>
    /// The counter this condition watches. Implementations read it for the OWNER, which
    /// is the side the condition belongs to; a condition that watches the opponent reads
    /// <paramref name="opponent"/> instead.
    /// </summary>
    protected abstract int CounterOf(GameState state, PlayerState owner, PlayerState opponent);

    public VictoryReading Read(GameState state, int ownerPlayerIndex)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        if (ownerPlayerIndex is < 0 or > 1)
        {
            return VictoryReading.Unmeasurable("no such player seat: " + ownerPlayerIndex);
        }

        var owner = state.GetPlayer(ownerPlayerIndex);
        var opponent = state.GetOpponent(ownerPlayerIndex);
        return VictoryReading.Measured(CounterOf(state, owner, opponent), Target);
    }
}

/// <summary>
/// The conditions the shipped cards use, one small class each.
///
/// Every one of these replaces a <c>case</c> in the engine's old win switch, and the
/// switch is gone: the engine now asks the condition, so the knowledge lives with the
/// rule instead of in the evaluator.
/// </summary>
public sealed class OpponentDiscardCountCondition : ThresholdVictoryCondition
{
    public OpponentDiscardCountCondition(int target) : base("OPPONENT_DISCARD_COUNT", target) { }

    protected override int CounterOf(GameState state, PlayerState owner, PlayerState opponent)
        => opponent.TotalDiscarded;
}

public sealed class NoDamageTurnStreakCondition : ThresholdVictoryCondition
{
    public NoDamageTurnStreakCondition(int target) : base("NO_DAMAGE_TURN_STREAK", target) { }

    protected override int CounterOf(GameState state, PlayerState owner, PlayerState opponent)
        => owner.NoDamageTurns;
}

public sealed class OpponentPunishDrawThisTurnCondition : ThresholdVictoryCondition
{
    public OpponentPunishDrawThisTurnCondition(int target) : base("OPPONENT_PUNISH_DRAW_THIS_TURN", target) { }

    protected override int CounterOf(GameState state, PlayerState owner, PlayerState opponent)
        => opponent.PunishDrawnThisTurn;
}

public sealed class PullCountCondition : ThresholdVictoryCondition
{
    public PullCountCondition(int target) : base("PULL_COUNT", target) { }

    protected override int CounterOf(GameState state, PlayerState owner, PlayerState opponent)
        => owner.PullCount;
}

public sealed class SealedMinionMaxHealthCondition : ThresholdVictoryCondition
{
    public SealedMinionMaxHealthCondition(int target) : base("SEALED_MINION_MAX_HEALTH", target) { }

    protected override int CounterOf(GameState state, PlayerState owner, PlayerState opponent)
    {
        var best = 0;
        foreach (var card in owner.Field)
        {
            if (card.IsMinion && card.Sealed && card.Health > best)
            {
                best = card.Health;
            }
        }

        return best;
    }
}

/// <summary>
/// Winning by breaking the shared castle.
///
/// This is the condition the earlier design REFUSED to express, because it is not a
/// threshold on one side's own counter. Refusing it was the wrong call: it is a victory
/// condition like any other, it just computes its own progress. Expressing it here means
/// the flame leader finally describes its win through the same interface as everyone
/// else, instead of only being understood by a special case buried in the engine.
///
/// Its progress is read from the castle itself, which both sides share, so it is
/// measured rather than estimated whenever the castle is present.
/// </summary>
public sealed class CastleBreakCondition : IVictoryCondition
{
    public CastleBreakCondition(int target = 1)
    {
        Target = target;
    }

    public string Id => "CASTLE_BREAK";

    public int Target { get; }

    /// <summary>
    /// FALSE, and this is the important part of this class.
    ///
    /// The castle break has its own resolution path that compares both leaders' claims at
    /// once and hands the win to whichever active leader holds the condition. Reporting
    /// progress here is right and useful; declaring a winner here would add a second rule
    /// that hands the win to the first-evaluated seat whenever the real path declines to
    /// decide. Before this condition existed, a castle-condition leader was not evaluated
    /// in the threshold loop at all — its winParam is absent — so declaring here would be a
    /// rule change, not a migration.
    /// </summary>
    public bool DeclaresWinWhenMet => false;

    public VictoryReading Read(GameState state, int ownerPlayerIndex)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        if (!state.CastleEnabled)
        {
            return VictoryReading.Unmeasurable(
                "the castle is not part of this match, so breaking it cannot decide anything");
        }

        // The castle is shared, so its state is the same reading for either side; what
        // differs between the two sides is only whether their OWN leader wins from it,
        // which is why this reading does not depend on the owner beyond validation.
        var health = state.CastleHealth;
        var broken = health <= 0;
        return broken
            ? VictoryReading.AlreadyMet(Target)
            : VictoryReading.Measured(0, Target);
    }
}

/// <summary>
/// A condition met only when EVERY child is met — "win by doing A and B".
///
/// Included in this first version rather than deferred, because composition is the case
/// a uniform interface most needs to survive: if a compound condition needed its own
/// engine branch, the interface would not actually be an interface. Adding it is ONE
/// class, which is the claim being tested.
///
/// Progress reports the child that is furthest from done, so "how much is left" has a
/// single meaning: the hardest remaining requirement.
/// </summary>
public sealed class AllOfVictoryCondition : IVictoryCondition
{
    private readonly IReadOnlyList<IVictoryCondition> _parts;

    public AllOfVictoryCondition(string id, IReadOnlyList<IVictoryCondition> parts)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An id is required.", nameof(id));
        if (parts is null) throw new ArgumentNullException(nameof(parts));
        if (parts.Count == 0) throw new ArgumentException("At least one part is required.", nameof(parts));
        Id = id;
        _parts = parts;
    }

    public string Id { get; }

    /// <summary>
    /// True only when every part declares its own win. If one part defers to a path
    /// elsewhere, the compound cannot decide either: a compound is exactly as authoritative
    /// as its least authoritative part.
    /// </summary>
    public bool DeclaresWinWhenMet
    {
        get
        {
            foreach (var part in _parts)
            {
                if (!part.DeclaresWinWhenMet) return false;
            }

            return true;
        }
    }

    public VictoryReading Read(GameState state, int ownerPlayerIndex)
    {
        var readings = new List<VictoryReading>(_parts.Count);
        foreach (var part in _parts)
        {
            var reading = part.Read(state, ownerPlayerIndex);
            if (!reading.IsMeasurable)
            {
                // A compound condition is only as knowable as its weakest part. Guessing
                // at the missing part would let "A and B" report confident progress while
                // one of its halves is unreadable.
                return VictoryReading.Unmeasurable(
                    "part '" + part.Id + "' is unmeasurable: " + reading.UnmeasurableReason);
            }

            readings.Add(reading);
        }

        var worst = readings[0];
        foreach (var reading in readings)
        {
            if ((reading.Remaining ?? 0) > (worst.Remaining ?? 0)) worst = reading;
        }

        return worst.IsMet
            ? VictoryReading.Measured(worst.Target, worst.Target)
            : VictoryReading.Measured(worst.Current, worst.Target);
    }
}

/// <summary>
/// A condition met when ANY child is met — "win by doing A or B".
///
/// The counterpart to <see cref="AllOfVictoryCondition"/>, and for the same reason: the
/// interface has to cover both join shapes or a compound rule would fall back to needing
/// engine changes.
///
/// Progress reports the CLOSEST child, because with an "any" rule the nearest satisfied
/// requirement is the one that wins.
/// </summary>
public sealed class AnyOfVictoryCondition : IVictoryCondition
{
    private readonly IReadOnlyList<IVictoryCondition> _parts;

    public AnyOfVictoryCondition(string id, IReadOnlyList<IVictoryCondition> parts)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An id is required.", nameof(id));
        if (parts is null) throw new ArgumentNullException(nameof(parts));
        if (parts.Count == 0) throw new ArgumentException("At least one part is required.", nameof(parts));
        Id = id;
        _parts = parts;
    }

    public string Id { get; }

    /// <summary>
    /// True only when every part declares its own win. See the note on the "all" form: a
    /// compound cannot be more authoritative than its parts.
    /// </summary>
    public bool DeclaresWinWhenMet
    {
        get
        {
            foreach (var part in _parts)
            {
                if (!part.DeclaresWinWhenMet) return false;
            }

            return true;
        }
    }

    public VictoryReading Read(GameState state, int ownerPlayerIndex)
    {
        VictoryReading? best = null;
        var unmeasurable = new List<string>();

        foreach (var part in _parts)
        {
            var reading = part.Read(state, ownerPlayerIndex);
            if (!reading.IsMeasurable)
            {
                unmeasurable.Add(part.Id + ": " + reading.UnmeasurableReason);
                continue;
            }

            if (reading.IsMet)
            {
                return VictoryReading.Measured(reading.Target, reading.Target);
            }

            if (best is null || (reading.Remaining ?? int.MaxValue) < (best.Remaining ?? int.MaxValue))
            {
                best = reading;
            }
        }

        if (best is not null) return best;

        // Every part unreadable: an "any" rule with nothing readable is not "no progress",
        // it is unknown, and saying so keeps the difference visible.
        return VictoryReading.Unmeasurable("no part is measurable — " + string.Join("; ", unmeasurable));
    }
}

}
