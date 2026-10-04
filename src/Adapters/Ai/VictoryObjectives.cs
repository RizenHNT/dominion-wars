using System;
using DominionWars.Engine.Model;
using System.Collections.Generic;
using System.Globalization;

namespace DominionWars.Adapters.Ai
{

/// <summary>How a victory objective's progress is meant to move.</summary>
public enum VictoryDirection
{
    /// <summary>Progress must reach the target by climbing (accumulate N pulls).</summary>
    Increase = 0,

    /// <summary>Progress must reach the target by falling (break the castle down).</summary>
    Decrease = 1,

    /// <summary>Progress must be HELD at or above the target for the objective to stay met.</summary>
    Maintain = 2,

    /// <summary>The direction is not known, so no "how far" claim can be made.</summary>
    Unknown = 3,
}

/// <summary>
/// ONE side's victory objective, in a form that is language- and card-agnostic.
///
/// THIS TYPE IS THE POINT OF THE REFACTOR. A rational AI needs exactly one thing
/// from a victory condition: how far it is from being met, and which way "far"
/// points. It does NOT need to know whether the number counts discards, downloads,
/// held zones or a tree's health. So the AI reads
///
///     objective.Metric / Direction / Target / Progress / Completed / Remaining
///
/// and never a condition name. When the engine starts publishing its objectives
/// itself, <see cref="VictoryObjectives.FromEngine"/> becomes the producer and
/// nothing above it changes.
/// </summary>
public sealed class VictoryObjective
{
    public VictoryObjective(
        int playerIndex,
        string metric,
        VictoryDirection direction,
        int? target,
        int? progress,
        bool completed,
        object? objectiveId,
        string source,
        string? unmeasurableReason)
    {
        PlayerIndex = playerIndex;
        Metric = metric ?? string.Empty;
        Direction = direction;
        Target = target;
        Progress = progress;
        Completed = completed;
        ObjectiveId = objectiveId;
        Source = source ?? string.Empty;
        UnmeasurableReason = unmeasurableReason;
    }

    public int PlayerIndex { get; }

    /// <summary>
    /// The metric family, e.g. <c>PULL</c>. Kept for provenance and reporting only:
    /// no decision should branch on it.
    /// </summary>
    public string Metric { get; }

    public VictoryDirection Direction { get; }

    public int? Target { get; }

    public int? Progress { get; }

    /// <summary>True when this objective is already satisfied.</summary>
    public bool Completed { get; }

    /// <summary>
    /// Whatever identifies the objective in its own system. Deliberately untyped:
    /// the adapter must not depend on the shape of the engine's identifiers.
    /// </summary>
    public object? ObjectiveId { get; }

    /// <summary>Where this objective came from, for provenance in a report.</summary>
    public string Source { get; }

    /// <summary>Why the objective is incomplete information; null when it is fully known.</summary>
    public string? UnmeasurableReason { get; }

    /// <summary>True when target and progress are both known, so Remaining is meaningful.</summary>
    public bool IsMeasurable => Target.HasValue && Progress.HasValue;

    /// <summary>
    /// How far this objective still has to travel, ALREADY SIGNED BY DIRECTION:
    /// for an Increase objective that has 4 of 6, this is 2; for a Decrease
    /// objective whose counter sits at 41 against a target of 0, this is 41.
    ///
    /// This is the single number a rational AI needs, and computing it is the only
    /// arithmetic it performs on a victory condition.
    /// </summary>
    public int? Remaining
    {
        get
        {
            if (!IsMeasurable) return null;
            switch (Direction)
            {
                case VictoryDirection.Increase:
                    return Math.Max(0, Target!.Value - Progress!.Value);
                case VictoryDirection.Decrease:
                    return Math.Max(0, Progress!.Value - Target!.Value);
                case VictoryDirection.Maintain:
                    return Math.Max(0, Target!.Value - Progress!.Value);
                default:
                    return null;
            }
        }
    }

    public override string ToString()
    {
        if (!IsMeasurable)
        {
            return "player " + PlayerIndex + ": " + (Metric.Length > 0 ? Metric : "<unknown metric>")
                + " unmeasurable (" + (UnmeasurableReason ?? "no reason given") + ")";
        }

        return "player " + PlayerIndex + ": " + Metric + " " + Direction + " "
            + Progress!.Value.ToString(CultureInfo.InvariantCulture) + " -> "
            + Target!.Value.ToString(CultureInfo.InvariantCulture)
            + " (remaining " + Remaining!.Value.ToString(CultureInfo.InvariantCulture)
            + (Completed ? ", already met" : string.Empty) + ")";
    }
}

/// <summary>
/// The victory objectives for one position, one per side.
/// </summary>
public sealed class VictoryObjectiveSet
{
    public VictoryObjectiveSet(int viewerIndex, IReadOnlyList<VictoryObjective> objectives)
    {
        ViewerIndex = viewerIndex;
        Objectives = objectives ?? throw new ArgumentNullException(nameof(objectives));
    }

    public int ViewerIndex { get; }

    public IReadOnlyList<VictoryObjective> Objectives { get; }

    /// <summary>The objective belonging to one seat, or null when it is unknown.</summary>
    public VictoryObjective? For(int playerIndex)
    {
        foreach (var objective in Objectives)
        {
            if (objective.PlayerIndex == playerIndex) return objective;
        }

        return null;
    }

    /// <summary>
    /// Which side is closer to winning, as a FACT rather than a preference: -1 when
    /// the viewer is closer, 1 when the opponent is, 0 when they are equally close or
    /// the comparison cannot be made. It deliberately does not say what to do about
    /// it.
    /// </summary>
    public int? WhoIsCloser(int viewerIndex)
    {
        var mine = For(viewerIndex);
        var theirs = For(1 - viewerIndex);
        if (mine?.Remaining is null || theirs?.Remaining is null) return null;
        if (mine.Remaining.Value == theirs.Remaining.Value) return 0;
        return mine.Remaining.Value < theirs.Remaining.Value ? -1 : 1;
    }
}

/// <summary>
/// Builds victory objectives.
///
/// <see cref="FromEngine"/> is the target path: the engine describes its own
/// objective. It is NOT implemented, because the engine does not publish one yet —
/// see docs/PL_VICTORY_OBJECTIVE_CONTRACT_PROPOSAL_2026-09-12.md.
///
/// <see cref="FromLeaderCondition"/> is the COMPATIBILITY PRODUCER. It reproduces
/// today's behaviour by reading the leader's condition id through the counter
/// registry, and it is explicitly marked as such: it still consults a condition
/// NAME, which is exactly what the target architecture removes. It exists so the
/// consumers above can be written against the uniform structure NOW, so that
/// switching to the engine-published objective later changes no consumer.
/// </summary>
public static class VictoryObjectives
{

/// <summary>
/// THE PRODUCER A CALLER SHOULD USE. Prefers the engine's published objective and
/// falls back to the name-based compatibility producer only where the engine published
/// none — a leader that declares no objective, or a snapshot from an older build.
///
/// The fallback is PER PLAYER, not global, and that is deliberate: the shipped game
/// mixes migrated and unmigrated leaders, so a whole-set fallback would drag every
/// migrated leader back onto the name-parsing path the moment one unmigrated leader sat
/// across the table.
///
/// Each objective records which path produced it in <see cref="VictoryObjective.Source"/>,
/// so a consumer — and a test — can tell a published reading from a reconstructed one
/// instead of having to assume.
/// </summary>
public static VictoryObjectiveSet ForMatch(
    RuntimeSnapshotEnvelope snapshot,
    int viewerIndex,
    IReadOnlyDictionary<string, LeaderWinCondition>? conditions)
{
    var published = FromEngine(snapshot, viewerIndex);
    if (conditions is null || conditions.Count == 0)
    {
        return published;
    }

    var fallback = FromLeaderCondition(snapshot, viewerIndex, conditions);
    var blended = new List<VictoryObjective>(2);
    for (var playerIndex = 0; playerIndex < 2; playerIndex++)
    {
        var fromEngine = published.For(playerIndex);
        var needsFallback = fromEngine is null
            || fromEngine.Direction == VictoryDirection.Unknown
            || !fromEngine.Progress.HasValue;

        blended.Add(needsFallback ? fallback.For(playerIndex)! : fromEngine!);
    }

    return new VictoryObjectiveSet(viewerIndex, blended);
}

/// <summary>
/// THE TARGET PRODUCER. Builds objectives from what the engine publishes about its
/// own victory conditions, so no condition NAME is consulted anywhere.
///
/// This is the point of the victory-objective contract: the card data says which
/// metric decides its victory, the engine evaluates the same field, and the snapshot
/// republishes it — so the AI reads a metric and a threshold instead of parsing an id
/// into a family and a comparison suffix.
///
/// FALLBACK, and why it is explicit: a leader whose card has not migrated publishes no
/// objective. There is no honest way to synthesise one, so this returns an objective
/// that says what is missing rather than guessing. The 烈焰 leader is the shipped case
/// — its castle win is resolved by a separate simultaneous path and carries no
/// threshold at all — and reporting "no objective declared" for it is correct, not a
/// gap to paper over.
/// </summary>
public static VictoryObjectiveSet FromEngine(RuntimeSnapshotEnvelope snapshot, int viewerIndex)
{
    if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
    if (viewerIndex is < 0 or > 1)
        throw new ArgumentOutOfRangeException(nameof(viewerIndex), "viewerIndex must be 0 or 1.");
    if (snapshot.Players is null || snapshot.Players.Count != 2)
        throw new ArgumentException("The snapshot must publish exactly two players.", nameof(snapshot));

    var result = new List<VictoryObjective>(2);
    for (var playerIndex = 0; playerIndex < 2; playerIndex++)
    {
        result.Add(OneFromEngine(snapshot, playerIndex));
    }

    return new VictoryObjectiveSet(viewerIndex, result);
}

private static VictoryObjective OneFromEngine(RuntimeSnapshotEnvelope snapshot, int playerIndex)
{
    var leaderCard = LeaderCard(snapshot.Players[playerIndex]);
    var leaderId = leaderCard?.CardId;
    if (leaderCard is null || string.IsNullOrEmpty(leaderId))
    {
        return new VictoryObjective(
            playerIndex, string.Empty, VictoryDirection.Unknown, null, null, false, null,
            "engine-published objective",
            "no leader card is visible in the leader zone, so no objective is known");
    }

    var published = leaderCard.Victory;
    if (published is null || string.IsNullOrEmpty(published.Metric))
    {
        return new VictoryObjective(
            playerIndex, string.Empty, VictoryDirection.Unknown, null, null, false, leaderId,
            "engine-published objective",
            "leader '" + leaderId + "' does not declare a victory objective, so its win condition is "
            + "resolved outside the threshold path and no progress can be read from one");
    }

    // The direction comes from the DATA now, not from a name lookup. Only the two
    // directions the engine evaluates map to a reading; anything else is reported as
    // unknown rather than silently treated as an increase.
    var direction = DirectionOfPublished(published.Direction);
    if (direction == VictoryDirection.Unknown)
    {
        return new VictoryObjective(
            playerIndex, published.Metric, VictoryDirection.Unknown, published.Target, null, false, leaderId,
            "engine-published objective",
            "the published direction '" + published.Direction + "' is not one the engine evaluates");
    }

    // Progress is read from what the ENGINE published, not measured here. The point of
    // the contract is that ONE implementation turns a condition into a number, and that
    // implementation belongs to the condition itself. A consumer that worked the value
    // out its own way could disagree with the engine about who is about to win, and
    // nothing would surface that until it mattered.
    //
    // `Met` is taken as published rather than inferred from Current vs Target, because a
    // condition need not be a simple comparison: the castle break is met or it is not.
    // The compatibility counter registry is not consulted on this path at all.
    var progress = published.Current;
    var completed = published.Met ?? false;

    return new VictoryObjective(
        playerIndex,
        published.Metric,
        direction,
        published.Target,
        progress,
        completed,
        leaderId,
        "engine-published objective",
        published.UnmeasurableReason
            ?? (progress.HasValue
                ? null
                : "the objective is declared but the snapshot carries no current value for it; "
                  + "it is reported as unmeasurable rather than measured here, so that only the "
                  + "condition itself decides progress"));
}

/// <summary>
/// COMPATIBILITY PRODUCER. Derives an objective from the leader's condition id.
///
/// ⚠️ This still recognises condition NAMES, via
/// <see cref="WinConditionCounters"/>. That is the very thing the target
/// architecture removes, so this is a bridge and not the destination. Its purpose
/// is to let every consumer above be written against <see cref="VictoryObjective"/>,
/// so the switch to engine-published objectives is invisible to them.
/// </summary>
public static VictoryObjectiveSet FromLeaderCondition(
    RuntimeSnapshotEnvelope snapshot,
    int viewerIndex,
    IReadOnlyDictionary<string, LeaderWinCondition> conditions)
{
    if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
    if (conditions is null) throw new ArgumentNullException(nameof(conditions));
    if (viewerIndex is < 0 or > 1)
        throw new ArgumentOutOfRangeException(nameof(viewerIndex), "viewerIndex must be 0 or 1.");
    if (snapshot.Players is null || snapshot.Players.Count != 2)
        throw new ArgumentException("The snapshot must publish exactly two players.", nameof(snapshot));

    var result = new List<VictoryObjective>(2);
    for (var playerIndex = 0; playerIndex < 2; playerIndex++)
    {
        result.Add(One(snapshot, playerIndex, conditions));
    }

    return new VictoryObjectiveSet(viewerIndex, result);
}

private static VictoryObjective One(
    RuntimeSnapshotEnvelope snapshot,
    int playerIndex,
    IReadOnlyDictionary<string, LeaderWinCondition> conditions)
{
    var leaderId = LeaderCardId(snapshot.Players[playerIndex]);
    if (leaderId is null)
    {
        return new VictoryObjective(
            playerIndex, string.Empty, VictoryDirection.Unknown, null, null, false, null,
            "leader-condition compatibility producer",
            "no leader card is visible in the leader zone, so no objective is known");
    }

    if (!conditions.TryGetValue(leaderId, out var condition) || condition is null)
    {
        return new VictoryObjective(
            playerIndex, string.Empty, VictoryDirection.Unknown, null, null, false, null,
            "leader-condition compatibility producer",
            "leader '" + leaderId + "' has no objective data supplied");
    }

    var reading = WinConditionCounters.Resolve(snapshot, playerIndex, condition.Condition);
    var direction = DirectionOf(reading.Axis);
    var target = condition.Required;
    var progress = reading.Current;
    var completed = progress.HasValue && IsMet(direction, progress.Value, target);

    // The target's MEANING depends on the direction, which is why both travel
    // together: for a Decrease axis the printed threshold is the floor to reach,
    // for an Increase axis it is the ceiling.
    return new VictoryObjective(
        playerIndex,
        reading.Axis,
        direction,
        target,
        progress,
        completed,
        condition.CardId,
        "leader-condition compatibility producer via WinConditionCounters",
        reading.IsMeasured
            ? null
            : "progress is not measurable: " + reading.Source);
}

/// <summary>
/// The direction each metric moves. Required, because it decides what "how far"
/// means: a castle axis counts DOWN toward zero, everything else counts UP.
///
/// This mapping is part of the compatibility layer for the same reason the counter
/// table is: the engine does not state its objectives' directions yet.
///
/// NOTE: both the parsed family name (`GIANT_HEALTH`, from the condition id) and the
/// name the counter reader reports (`HEALTH`) are accepted, because a single axis can
/// reach here under either spelling. An unknown direction silently makes the distance
/// uncomputable, so a test pins that every counter the registry can return also has a
/// direction here.
/// </summary>
private static VictoryDirection DirectionOf(string axis)
{
    switch (axis)
    {
        case "ROYAL_CASTLE_BREAK":
        case "CASTLE":
            return VictoryDirection.Decrease;
        case "HEALTH":
        case "GIANT_HEALTH":
        case "PULL":
        case "PULL_TOTAL":
        case "DISCARD":
        case "PUNISH_DRAW":
        case "NO_DAMAGE":
        case "DAMAGE":
        case "AMBUSH_TRIGGER_WIN":
            return VictoryDirection.Increase;
        default:
            return VictoryDirection.Unknown;
    }
}

private static bool IsMet(VictoryDirection direction, int progress, int target)
{
    switch (direction)
    {
        case VictoryDirection.Increase: return progress >= target;
        case VictoryDirection.Decrease: return progress <= target;
        case VictoryDirection.Maintain: return progress >= target;
        default: return false;
    }
}

/// <summary>The id of the leader card visible in one side's leader zone, if any.</summary>
private static string? LeaderCardId(RuntimePlayerSnapshot player) => LeaderCard(player)?.CardId;

/// <summary>The leader card visible in one side's leader zone, if any.</summary>
private static RuntimeCardSnapshot? LeaderCard(RuntimePlayerSnapshot player)
{
    if (player.LeaderZone is null) return null;
    foreach (var card in player.LeaderZone)
    {
        if (card is null || string.IsNullOrEmpty(card.CardId)) continue;
        return card;
    }

    return null;
}

/// <summary>
/// Maps the direction the CARD DATA declares onto the AI's own enum. Reading the
/// declared field is the whole point: the alternative is inferring the direction from
/// the condition name, which is how "ROYAL_CASTLE_BREAK" — a name with no comparison
/// suffix at all — had to be special-cased by hand.
/// </summary>
private static VictoryDirection DirectionOfPublished(string? direction)
{
    if (string.Equals(direction, VictoryObjectiveDirection.Increase, StringComparison.Ordinal))
    {
        return VictoryDirection.Increase;
    }

    if (string.Equals(direction, VictoryObjectiveDirection.Decrease, StringComparison.Ordinal))
    {
        return VictoryDirection.Decrease;
    }

    return VictoryDirection.Unknown;
}
}

}
