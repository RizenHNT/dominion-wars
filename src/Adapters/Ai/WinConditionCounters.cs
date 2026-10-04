using System;
using System.Collections.Generic;
using System.Globalization;

namespace DominionWars.Adapters.Ai
{

/// <summary>
/// ⚠️ COMPATIBILITY LAYER — NOT THE TARGET ARCHITECTURE. ⚠️
///
/// This type resolves win-condition progress from the condition's NAME against a
/// table compiled into the adapter. It is an improvement over a switch of literal
/// ids, but it is STILL HARDCODING: it only relocates the literals into one place.
///
/// It does NOT satisfy the requirement that the RULES describe their own victory
/// objective to the AI. A future series adding, say,
/// <c>CONTROL_ZONE_TURNS_GE</c> would still need a hand-written entry here, and
/// until someone writes it the AI is blind to that axis.
///
/// THE TARGET is for the leader definition to publish a structured objective —
/// something like `{ progress, target, direction, completed }` plus optional
/// prerequisites — so that the rational AI reads a uniform structure and never
/// recognises a condition name at all. That requires an ENGINE change and is
/// proposed separately; see the victory-objective contract proposal in docs/.
///
/// Until then this table is the honest bridge, and it is deliberately written so
/// that an UNKNOWN family reports itself by name rather than degrading to a silent
/// "unknown".
///
/// WHAT IT DOES NOT DUPLICATE: it never recomputes a cost or a legality rule. For
/// the resolved cost of a move it reads what the engine advertised (see
/// <see cref="MoveSettlements"/>); for progress it only reads counters the engine
/// already publishes.
/// </summary>
public static class WinConditionCounters
{

/// <summary>
/// A parsed condition id, e.g. <c>OPP_DISCARD_TOTAL_GE</c> becomes
/// Family=<c>DISCARD</c>, Subject=<c>OPP</c>, Predicate=<c>TOTAL_GE</c>.
/// </summary>
public readonly struct ConditionId
{
    public ConditionId(string raw, string? subject, string family, string predicate)
    {
        Raw = raw ?? string.Empty;
        Subject = subject;
        Family = family;
        Predicate = predicate;
    }

    /// <summary>The id exactly as published by the engine.</summary>
    public string Raw { get; }

    /// <summary><c>SELF</c> or <c>OPP</c>; null when the id names no subject.</summary>
    public string? Subject { get; }

    /// <summary>The metric family, e.g. <c>PULL_TOTAL</c> or <c>DISCARD</c>.</summary>
    public string Family { get; }

    /// <summary>The comparison suffix, e.g. <c>GE</c>.</summary>
    public string Predicate { get; }

    public override string ToString() => Raw;
}

/// <summary>Resolved progress for one condition, with the source named.</summary>
public sealed class ProgressReading
{
    public ProgressReading(string axis, string subject, int? current, string source)
    {
        Axis = axis ?? throw new ArgumentNullException(nameof(axis));
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        Current = current;
        Source = source ?? string.Empty;
    }

    /// <summary>The resolved metric family, e.g. <c>DISCARD</c>.</summary>
    public string Axis { get; }

    /// <summary>Whose counter supplies it: <c>SELF</c> or <c>OPP</c>.</summary>
    public string Subject { get; }

    /// <summary>The measured progress, or null when no counter could be read.</summary>
    public int? Current { get; }

    /// <summary>The snapshot field that supplied the value, or why none could.</summary>
    public string Source { get; }

    public bool IsMeasured => Current.HasValue;
}

/// <summary>
/// Splits a condition id into its parts.
///
/// The engine's ids are <c>[SUBJECT_]FAMILY_PREDICATE</c>, for example
/// <c>OPP_DISCARD_TOTAL_GE</c>. The trailing predicate is recognised by a known
/// suffix list rather than by splitting on the last underscore, so a family that
/// itself contains underscores still parses.
/// </summary>
public static ConditionId Parse(string condition)
{
    if (condition is null) throw new ArgumentNullException(nameof(condition));
    var raw = condition.Trim();
    if (raw.Length == 0) return new ConditionId(raw, null, string.Empty, string.Empty);

    // Longest comparison suffix first, so `_TURN_GE` wins over `_GE`.
    string[] predicates = { "TOTAL_GE", "TURNS_GE", "TURN_GE", "COUNT_GE", "GE", "GT", "LE", "LT", "EQ" };
    var predicate = string.Empty;
    var head = raw;
    foreach (var candidate in predicates)
    {
        var suffix = "_" + candidate;
        if (!raw.EndsWith(suffix, StringComparison.Ordinal)) continue;
        predicate = candidate;
        head = raw.Substring(0, raw.Length - suffix.Length);
        break;
    }

    // The family is the id with the comparison suffix removed, so
    // `GIANT_HEALTH_GE` measures GIANT_HEALTH and `PULL_TOTAL_GE` measures
    // PULL_TOTAL. No further trimming is applied: collapsing a generic word would
    // rename families by spelling rather than by metric.
    string? subject = null;
    if (head.StartsWith("OPP_", StringComparison.Ordinal))
    {
        subject = "OPP";
        head = head.Substring(4);
    }
    else if (head.StartsWith("SELF_", StringComparison.Ordinal))
    {
        subject = "SELF";
        head = head.Substring(5);
    }

    return new ConditionId(raw, subject, head, predicate);
}

/// <summary>
/// Resolves the progress counter for a condition from the snapshot.
///
/// Subject semantics follow the engine's own evaluator: a condition stated from a
/// player's point of view counts that player by default, and an <c>OPP_</c>
/// condition counts the OTHER player. Getting this backwards would silently invert
/// the estimate, so each mapping states whose counter it reads in
/// <see cref="ProgressReading.Subject"/> and the tests pin it.
/// </summary>
public static ProgressReading Resolve(
    RuntimeSnapshotEnvelope snapshot,
    int playerIndex,
    string condition)
{
    if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
    if (snapshot.Players is null || snapshot.Players.Count != 2)
        throw new ArgumentException("The snapshot must publish exactly two players.", nameof(snapshot));
    if (playerIndex is < 0 or > 1)
        throw new ArgumentOutOfRangeException(nameof(playerIndex), "playerIndex must be 0 or 1.");

    var id = Parse(condition);
    var self = snapshot.Players[playerIndex];
    var other = snapshot.Players[1 - playerIndex];
    var readsOpponent = string.Equals(id.Subject, "OPP", StringComparison.Ordinal);

    switch (id.Family)
    {
        // `PULL_TOTAL_GE` parses as family PULL with predicate TOTAL_GE, because the
        // trailing `_TOTAL_GE` is a comparison suffix rather than part of the metric.
        // The long-suffix-first rule in Parse is what makes that split correct.
        case "PULL":
        case "PULL_TOTAL":
            return new ProgressReading(
                id.Family,
                readsOpponent ? "OPP" : "SELF",
                self.PullCount,
                "PullCount on " + (readsOpponent ? "the opponent" : "this player"));
        case "DISCARD":
            // The engine reads the OPPONENT's discards for this axis, because the
            // axis is "make the other side discard".
            return new ProgressReading(
                id.Family,
                "OPP",
                other.TotalDiscarded,
                "TotalDiscarded on the opponent");

        case "PUNISH_DRAW":
            return new ProgressReading(
                id.Family,
                "OPP",
                other.PunishDrawnThisTurn,
                "PunishDrawnThisTurn on the opponent");

        case "NO_DAMAGE":
        case "DAMAGE":
            // NO_DAMAGE_TURNS_GE reads the player's OWN no-damage streak.
            return new ProgressReading(
                id.Family,
                "SELF",
                self.NoDamageTurns,
                "NoDamageTurns on this player");

        case "AMBUSH_TRIGGER_WIN":
            // The axis is "trigger an ambush", and a face-down ambush IS the
            // progress: AmbushCount is published for both sides while the identities
            // stay hidden, so this is measurable without leaking anything.
            return new ProgressReading(
                id.Family,
                "SELF",
                self.AmbushCount,
                "AmbushCount on this player (identities remain hidden)");

        case "GIANT_HEALTH":
            return ReadSealedHealth(self);

        case "CASTLE":
        // ROYAL_CASTLE_BREAK carries no predicate suffix, so it parses as the whole
        // family name. It is listed explicitly rather than relying on the `_GE`
        // split, because the id has no suffix to split on.
        case "ROYAL_CASTLE_BREAK":
            // The castle is shared, so its remaining health is the same reading
            // for both seats.
            return snapshot.Castle is null
                ? new ProgressReading(id.Family, "SHARED", null, "the snapshot publishes no castle state")
                : new ProgressReading(id.Family, "SHARED", snapshot.Castle.Health, "Castle.Health (shared)");

        case "CYCLE":
            return new ProgressReading(id.Family, "SELF", self.CycleWinCount, "CycleWinCount on this player");

        default:
            return new ProgressReading(
                id.Family,
                readsOpponent ? "OPP" : "SELF",
                null,
                "no counter is registered for the '" + id.Family + "' family (condition '" + id.Raw
                + "'); add a mapping in WinConditionCounters when the engine gains that axis");
    }
}

/// <summary>
/// The GIANT_HEALTH axis is satisfied by a SEALED minion reaching a health
/// threshold, so the reading is the largest health among this side's sealed
/// minions. Zero is a real measurement here (no sealed minion yet), not a gap.
/// </summary>
private static ProgressReading ReadSealedHealth(RuntimePlayerSnapshot player)
{
    var best = 0;
    var sealedSeen = false;
    if (player.Field is not null)
    {
        foreach (var card in player.Field)
        {
            if (card is null || !card.Sealed) continue;
            sealedSeen = true;
            var health = card.CurrentHealth ?? 0;
            if (health > best) best = health;
        }
    }

    return new ProgressReading(
        "HEALTH",
        "SELF",
        best,
        sealedSeen
            ? "max CurrentHealth among sealed minions on Field"
            : "no sealed minion on Field, so progress is a genuine 0");
}

/// <summary>
/// The families this adapter can read, for reporting. A condition outside this set
/// is not silently unknown: <see cref="Resolve"/> names the missing family.
/// </summary>
public static IReadOnlyList<string> KnownFamilies { get; } =
    Array.AsReadOnly(new[]
    {
        "AMBUSH_TRIGGER_WIN", "PULL_TOTAL", "DISCARD", "PUNISH_DRAW", "NO_DAMAGE",
        "GIANT_HEALTH", "ROYAL_CASTLE_BREAK", "CYCLE",
    });

/// <summary>Human-readable description of one reading, for a report line.</summary>
public static string Describe(ProgressReading reading)
{
    if (reading is null) throw new ArgumentNullException(nameof(reading));
    return reading.IsMeasured
        ? reading.Axis + " (" + reading.Subject + ") = "
          + reading.Current!.Value.ToString(CultureInfo.InvariantCulture) + " via " + reading.Source
        : reading.Axis + " (" + reading.Subject + ") unknown: " + reading.Source;
}

}

}
