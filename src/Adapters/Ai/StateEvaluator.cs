using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DominionWars.Adapters.Ai
{

/// <summary>
/// How much a single reported fact can be trusted. This exists so "I do not know"
/// is a first-class value rather than a silent zero: an evaluator that reports
/// `Unknown` for a missing fact cannot be mistaken for one that measured the fact
/// and found it neutral.
/// </summary>
public enum FactConfidence
{
    /// <summary>Read directly off a published value; no inference involved.</summary>
    Measured = 0,

    /// <summary>Inferred from public information; correct under a stated model.</summary>
    Estimated = 1,

    /// <summary>The information needed is not available in the viewer-safe snapshot.</summary>
    Unknown = 2,

    /// <summary>The fact does not apply in this state (no target exists to threaten).</summary>
    NotApplicable = 3,
}

/// <summary>
/// One fact about the state: a name, a value, how much to trust it, and — when the
/// value is missing — why.
///
/// Every entry carries its own confidence on purpose. A single score would have to
/// average away the difference between "measured" and "guessed", and that average
/// is exactly the kind of number this project has already been burned by.
/// </summary>
public sealed class StateFact
{
    public StateFact(string name, double? value, FactConfidence confidence, string note)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Value = value;
        Confidence = confidence;
        Note = note ?? string.Empty;
    }

    public string Name { get; }

    /// <summary>The value, or null when it is unknown or inapplicable.</summary>
    public double? Value { get; }

    public FactConfidence Confidence { get; }

    /// <summary>Why the value is what it is; required reading when it is null.</summary>
    public string Note { get; }

    public bool IsKnown => Value.HasValue;

    public override string ToString() =>
        Name + "=" + (Value.HasValue
            ? Value.Value.ToString("0.###", CultureInfo.InvariantCulture)
            : "-")
        + " (" + Confidence + ")"
        + (Note.Length > 0 ? " " + Note : string.Empty);
}

/// <summary>
/// A flat, named vector of facts about one snapshot from one viewer's position.
///
/// v0 IS DELIBERATELY NOT A SCORE. There is no total, no ranking and no preferred
/// action anywhere in this type: it answers "what is true", never "what is good".
/// The separation is the point — the project's earlier AI conflated the two, and a
/// weight table over an unexamined evaluation is what made nine playstyles behave
/// like nine sets of random numbers.
/// </summary>
public sealed class StateVector
{
    public StateVector(
        int turn,
        string phase,
        int viewerIndex,
        IReadOnlyList<StateFact> facts,
        ThreatReport threat)
    {
        Turn = turn;
        Phase = phase ?? string.Empty;
        ViewerIndex = viewerIndex;
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));
        Threat = threat ?? throw new ArgumentNullException(nameof(threat));
    }

    public int Turn { get; }

    public string Phase { get; }

    /// <summary>The seat this vector was computed for. The opponent sees its own.</summary>
    public int ViewerIndex { get; }

    public IReadOnlyList<StateFact> Facts { get; }

    /// <summary>The threat estimate this vector was built from, for drill-down.</summary>
    public ThreatReport Threat { get; }

    /// <summary>One fact by name, or null when the evaluator never emits it.</summary>
    public StateFact? Fact(string name)
    {
        foreach (var fact in Facts)
        {
            if (string.Equals(fact.Name, name, StringComparison.Ordinal)) return fact;
        }

        return null;
    }

    /// <summary>How many facts carry each confidence, so a caller can weight coverage.</summary>
    public int CountWith(FactConfidence confidence) => Facts.Count(f => f.Confidence == confidence);

    /// <summary>Facts that are anything other than fully measured.</summary>
    public IReadOnlyList<StateFact> Uncertain =>
        Facts.Where(f => f.Confidence != FactConfidence.Measured).ToList();

    /// <summary>
    /// A stable, human-readable rendering. Used by reports and by the tests that
    /// pin the shape of the vector, so a change in what the evaluator knows is a
    /// visible change to this text rather than a silent one.
    /// </summary>
    public string Describe()
    {
        var lines = new List<string>
        {
            "turn " + Turn.ToString(CultureInfo.InvariantCulture) + " " + Phase
            + " viewer " + ViewerIndex.ToString(CultureInfo.InvariantCulture)
            + " (" + Facts.Count.ToString(CultureInfo.InvariantCulture) + " facts, "
            + CountWith(FactConfidence.Measured).ToString(CultureInfo.InvariantCulture) + " measured, "
            + CountWith(FactConfidence.Estimated).ToString(CultureInfo.InvariantCulture) + " estimated, "
            + CountWith(FactConfidence.Unknown).ToString(CultureInfo.InvariantCulture) + " unknown, "
            + CountWith(FactConfidence.NotApplicable).ToString(CultureInfo.InvariantCulture) + " n/a)",
        };

        foreach (var fact in Facts) lines.Add("  " + fact);
        return string.Join("\n", lines);
    }
}

/// <summary>
/// Turns a viewer-safe snapshot into a <see cref="StateVector"/>.
///
/// The evaluator is a pure function of its inputs. It reads no <c>GameState</c>,
/// never consults the opponent's hand contents or an unflipped ambush, holds no
/// state between calls, and uses no randomness or clock — so the same snapshot
/// always yields the same vector.
///
/// It reports FACTS ONLY. It does not score, rank, or recommend, and there is no
/// playstyle input anywhere in this type: the whole point of v0 is to establish
/// what is true about a position before anything is allowed to have an opinion
/// about it.
/// </summary>
public static class StateEvaluator
{

// ------------------------------------------------------------------ fact names
// Named constants so a consumer never matches on a string literal it typed itself.

/// <summary>Largest sealed-minion health on my side (the GIANT_HEALTH_GE axis).</summary>
public const string MySealedHealth = "my.sealed_health";

/// <summary>Downloads completed by the opponent (the PULL_TOTAL_GE axis).</summary>
public const string OpponentPullCount = "opponent.pull_count";

/// <summary>Remaining health of the shared Royal Castle.</summary>
public const string CastleHealth = "castle.health";

/// <summary>My hand size.</summary>
public const string MyHandCount = "my.hand_count";

/// <summary>My deck size.</summary>
public const string MyDeckCount = "my.deck_count";

/// <summary>The opponent's hand size — public, contents hidden.</summary>
public const string OpponentHandCount = "opponent.hand_count";

/// <summary>The opponent's deck size.</summary>
public const string OpponentDeckCount = "opponent.deck_count";

/// <summary>Total current attack across the opponent's board (exact, no hidden info).</summary>
public const string OpponentBoardAttack = "opponent.board_attack";

/// <summary>Number of minions on the opponent's board.</summary>
public const string OpponentBoardMinions = "opponent.board_minions";

/// <summary>Largest single attack on the opponent's board.</summary>
public const string OpponentLargestAttack = "opponent.largest_attack";

/// <summary>Expected copies of the named threat category in the opponent's hand.</summary>
public const string ThreatPrefix = "threat.";

/// <summary>
/// Cards I have drawn this turn because I was punished. This is a CURRENT-STATE
/// counter: it records what already happened, not what a move would cause.
/// </summary>
public const string MyPunishDrawnThisTurn = "my.punish_drawn_this_turn";

/// <summary>
/// This turn's punish modifier applied to my cards. This is a modifier, NOT a
/// resource: it must not be read as "the opponent gained this much". The gain from
/// a specific move is settled by <see cref="MoveSettlement"/>, which reads the
/// engine's own resolved cost off the advertisement.
/// </summary>
public const string MyPunishDeltaThisTurn = "my.punish_delta_this_turn";

/// <summary>Cards the opponent has discarded cumulatively.</summary>
public const string OpponentTotalDiscarded = "opponent.total_discarded";

/// <summary>Cards I have discarded cumulatively.</summary>
public const string MyTotalDiscarded = "my.total_discarded";

/// <summary>Consecutive no-damage turns for the opponent.</summary>
public const string OpponentNoDamageTurns = "opponent.no_damage_turns";

/// <summary>Facts about my own progress toward my win condition.</summary>
public const string MyWinProgress = "my.win_progress";

/// <summary>
/// My victory objective as a uniform structure (metric/direction/progress/target).
/// This is the card-agnostic form: a consumer reads Remaining and never learns what
/// the metric counts. Prefer this over the condition-string facts.
/// </summary>
public const string MyObjective = "my.objective";

/// <summary>The opponent's victory objective, in the same uniform form.</summary>
public const string OpponentObjective = "opponent.objective";

/// <summary>
/// Which side is closer to its objective: -1 mine, 1 theirs, 0 tied. A fact about
/// the race, not a recommendation about what to do.
/// </summary>
public const string WhoIsCloser = "victory.who_is_closer";

/// <summary>The win condition id for my seat, when the leader is visible.</summary>
public const string MyWinCondition = "my.win_condition";

/// <summary>The condition id for the opponent's seat.</summary>
public const string OpponentWinCondition = "opponent.win_condition";

/// <summary>
/// Evaluates one snapshot for one seat.
///
/// <paramref name="conditions"/> and <paramref name="viewerIndex"/> follow
/// <see cref="ThreatEstimator.DeriveWinProgress"/>; the same
/// <paramref name="pool"/>/<paramref name="categories"/>/<paramref name="tagsByCardId"/>
/// are forwarded to the threat estimate, so a caller supplies each public artifact
/// once.
/// </summary>
public static StateVector Evaluate(
    RuntimeSnapshotEnvelope snapshot,
    int viewerIndex,
    IReadOnlyDictionary<string, LeaderWinCondition> conditions,
    IReadOnlyDictionary<string, int> pool,
    IReadOnlyDictionary<string, IReadOnlyList<ThreatKind>> categories,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? tagsByCardId = null)
{
    if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
    if (conditions is null) throw new ArgumentNullException(nameof(conditions));
    if (pool is null) throw new ArgumentNullException(nameof(pool));
    if (categories is null) throw new ArgumentNullException(nameof(categories));
    if (viewerIndex is < 0 or > 1)
        throw new ArgumentOutOfRangeException(nameof(viewerIndex), "viewerIndex must be 0 or 1.");
    if (snapshot.Players is null || snapshot.Players.Count != 2)
        throw new ArgumentException("The snapshot must publish exactly two players.", nameof(snapshot));

    var opponentIndex = 1 - viewerIndex;
    var me = snapshot.Players[viewerIndex];
    var them = snapshot.Players[opponentIndex];

    var threat = ThreatEstimator.Estimate(
        snapshot,
        viewerIndex,
        pool,
        categories,
        winConditions: null,
        tagsByCardId: tagsByCardId);

    var progress = ThreatEstimator.DeriveWinProgress(snapshot, viewerIndex, conditions);
    var mine = progress.FirstOrDefault(p => p.PlayerIndex == viewerIndex);
    var theirs = progress.FirstOrDefault(p => p.PlayerIndex == opponentIndex);

    // The uniform, card-agnostic objective form. `ForMatch` prefers the objective the
    // ENGINE published (metric + target + current value, all data-driven) and falls back
    // to the name-parsing compatibility producer only for a leader that declares no
    // objective. Each objective's Source records which path produced it, so a mixed
    // match cannot silently pass a reconstructed reading off as a published one.
    var objectives = VictoryObjectives.ForMatch(snapshot, viewerIndex, conditions);

    var facts = new List<StateFact>
    {
        // --- resources: all published counts, therefore Measured ---------------
        Resource(MyHandCount, me.HandCount, "my hand size"),
        Resource(MyDeckCount, me.DeckCount, "my deck size"),
        Resource(OpponentHandCount, them.HandCount, "opponent hand size (contents hidden)"),
        Resource(OpponentDeckCount, them.DeckCount, "opponent deck size"),

        // --- my own win axis ---------------------------------------------------
        WinConditionFact(MyWinCondition, mine, "my"),
        WinProgressFact(MyWinProgress, mine),

        // --- the opponent's win axis -------------------------------------------
        WinConditionFact(OpponentWinCondition, theirs, "opponent"),

        // --- both sides as uniform objectives (the card-agnostic form) ---------
        ObjectiveFact(MyObjective, objectives.For(viewerIndex), "my"),
        ObjectiveFact(OpponentObjective, objectives.For(opponentIndex), "the opponent's"),
        CloserFact(objectives, viewerIndex),

        // --- board pressure: exact, because the board is fully public ----------
        Board(OpponentBoardMinions, threat.BoardThreat.MinionCount, "opponent minions on board"),
        Board(OpponentBoardAttack, threat.BoardThreat.AttackSum, "total current attack on the opponent's board"),
        Board(OpponentLargestAttack, threat.BoardThreat.LargestAttack, "largest single attack on the opponent's board"),
        Castle(snapshot),
    };

    // --- punish counters: CURRENT STATE ONLY -------------------------------------
    //
    // These are counters of what has already happened plus this turn's modifier.
    // The evaluator deliberately does NOT sum the punish across my hand: that total
    // has no decision meaning, because what matters is what a CHOSEN move costs and
    // what the opponent does with what it hands them. That is an action-dependent
    // question and it belongs to the move settlement below, not here.
    facts.Add(new StateFact(
        MyPunishDrawnThisTurn, me.PunishDrawnThisTurn, FactConfidence.Measured,
        "cards I drew this turn as punishment for the opponent's plays — their cost, my resource"));
    facts.Add(new StateFact(
        MyPunishDeltaThisTurn, me.PunishDeltaThisTurn, FactConfidence.Measured,
        "this turn's punish MODIFIER on my cards; it is not itself a resource transfer — "
        + "the transfer is settled per move by MoveSettlement"));
    facts.Add(new StateFact(
        OpponentTotalDiscarded, them.TotalDiscarded, FactConfidence.Measured,
        "the opponent's cumulative discards, excluding the forced discard phase"));
    facts.Add(new StateFact(
        MyTotalDiscarded, me.TotalDiscarded, FactConfidence.Measured,
        "my cumulative discards, excluding the forced discard phase"));
    facts.Add(new StateFact(
        OpponentNoDamageTurns, them.NoDamageTurns, FactConfidence.Measured,
        "the opponent's consecutive no-damage turns"));

    // --- hidden-hand threat: an estimate, flagged as one -----------------------
    foreach (var probability in threat.Probabilities.OrderBy(p => p.Kind))
    {
        facts.Add(new StateFact(
            ThreatPrefix + probability.Kind.ToString().ToLowerInvariant() + ".expected_in_hand",
            probability.ExpectedCardsInHand,
            FactConfidence.Estimated,
            "expected copies of " + probability.Kind + " in the opponent's hand, from "
            + probability.UnseenCopies.ToString(CultureInfo.InvariantCulture) + " unplaced copies of "
            + probability.PoolSize.ToString(CultureInfo.InvariantCulture)
            + "; playable this turn ~" + probability.ExpectedPlayableThisTurn.ToString("0.##", CultureInfo.InvariantCulture)));
    }

    if (threat.Probabilities.Count == 0)
    {
        facts.Add(new StateFact(
            ThreatPrefix + "none",
            null,
            FactConfidence.Unknown,
            "no threat category could be estimated from the supplied pool"));
    }

    return new StateVector(snapshot.Turn, snapshot.Phase, viewerIndex, facts, threat);
}

private static StateFact Resource(string name, int value, string note)
    => new StateFact(name, value, FactConfidence.Measured, note);

private static StateFact Board(string name, int value, string note)
    => new StateFact(name, value, FactConfidence.Measured, note);

private static StateFact Castle(RuntimeSnapshotEnvelope snapshot)
    => snapshot.Castle is null
        ? new StateFact(CastleHealth, null, FactConfidence.Unknown, "the snapshot publishes no castle state")
        : new StateFact(
            CastleHealth,
            snapshot.Castle.Enabled ? snapshot.Castle.Health : 0,
            FactConfidence.Measured,
            snapshot.Castle.Enabled ? "shared Royal Castle hit points remaining" : "the castle is disabled in this match");

/// <summary>
/// A victory objective as a fact: its remaining distance, with the direction and
/// target explained in the note. The VALUE is the remaining distance because that
/// is the only number a decision needs; the metric name is provenance only.
/// </summary>
private static StateFact ObjectiveFact(string name, VictoryObjective? objective, string who)
{
    if (objective is null)
    {
        return new StateFact(name, null, FactConfidence.Unknown, "no objective is known for " + who);
    }

    var progress = objective.Progress;
    var target = objective.Target;
    var remaining = objective.Remaining;
    if (!progress.HasValue || !target.HasValue || !remaining.HasValue)
    {
        return new StateFact(
            name, null, FactConfidence.Unknown,
            who + " objective " + (objective.Metric.Length > 0 ? objective.Metric : "<unknown metric>")
            + " is unmeasurable: " + (objective.UnmeasurableReason ?? "no reason given"));
    }

    return new StateFact(
        name,
        remaining.Value,
        FactConfidence.Measured,
        who + " objective " + objective.Metric + " " + objective.Direction
        + ": progress " + progress.Value.ToString(CultureInfo.InvariantCulture)
        + " against target " + target.Value.ToString(CultureInfo.InvariantCulture)
        + ", so " + remaining.Value.ToString(CultureInfo.InvariantCulture) + " to go"
        + (objective.Completed ? " (already met)" : string.Empty));
}

/// <summary>
/// Reports which side is closer to its objective. This is a comparison of two
/// facts, not a judgement: it says nothing about what to do with the information.
/// </summary>
private static StateFact CloserFact(VictoryObjectiveSet objectives, int viewerIndex)
{
    var closer = objectives.WhoIsCloser(viewerIndex);
    if (closer is null)
    {
        return new StateFact(
            WhoIsCloser, null, FactConfidence.Unknown,
            "at least one side's objective is unmeasurable, so the race cannot be compared");
    }

    var meaning = closer.Value < 0
        ? "I am closer to my objective than the opponent is to theirs"
        : closer.Value > 0
            ? "the opponent is closer to their objective than I am to mine"
            : "both sides are equally far from their objectives";

    return new StateFact(WhoIsCloser, closer.Value, FactConfidence.Measured, meaning);
}

/// <summary>
/// A win-condition fact is an IDENTIFIER, not a quantity, so it has no numeric
/// value by design. Its confidence records whether the identifier itself was
/// resolvable: a visible leader resolves it (Measured), an unknown or unsupplied
/// leader does not (Unknown). Reporting a null value as Measured would be
/// self-contradictory, so the two are kept distinct.
/// </summary>
private static StateFact WinConditionFact(string name, WinProgress? progress, string who)
    => progress?.Condition is null
        ? new StateFact(name, null, FactConfidence.Unknown,
            progress?.UnmeasurableReason ?? ("no win condition is known for " + who))
        : new StateFact(name, null, FactConfidence.NotApplicable,
            progress.Condition + " requires " + progress.Required
            + " (an identifier, so it has no numeric value)");

private static StateFact WinProgressFact(string name, WinProgress? progress)
{
    if (progress is null || progress.Condition is null)
    {
        return new StateFact(name, null, FactConfidence.Unknown,
            progress?.UnmeasurableReason ?? "the win condition is unknown");
    }

    if (!progress.IsFullyMeasured)
    {
        // The condition is known but its counter is not published. Reporting a
        // number here would invent progress, so the value stays null.
        return new StateFact(name, null, FactConfidence.Unknown,
            "condition " + progress.Condition + " requires " + progress.Required
            + " but progress is unmeasurable: " + progress.UnmeasurableReason);
    }

    return new StateFact(
        name,
        progress.Current!.Value,
        FactConfidence.Measured,
        progress.Condition + " progress " + progress.Current.Value.ToString(CultureInfo.InvariantCulture)
        + "/" + progress.Required!.Value.ToString(CultureInfo.InvariantCulture)
        + " via " + progress.ProgressSource
        + (progress.RemainingTurns.HasValue
            ? "; ~" + progress.RemainingTurns.Value.ToString(CultureInfo.InvariantCulture) + " turns"
            : "; turns unknown"));
}

}

}
