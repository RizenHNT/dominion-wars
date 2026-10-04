using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DominionWars.Adapters.Ai
{

/// <summary>
/// What a threat question is asking about, expressed as a QUESTION A DECISION
/// WOULD ASK rather than as a bucket of effect ids.
///
/// The split follows the effect's TARGET as well as its action, because those are
/// different capabilities: `DAMAGE` aimed at ENEMY_MINION removes one minion,
/// `DAMAGE` aimed at ALL_ENEMY_MINIONS sweeps the board, and `DAMAGE` aimed at
/// ENEMY_FACE presses the core. Treating those as one "damage" bucket is what made
/// the previous coarse category the least accurate of all of them.
///
/// Ally-directed effects (HEAL, BUFF, GRANT_KEYWORD, RESTORE_ATTACKS) are
/// deliberately NOT threat categories: they are the opponent helping themselves,
/// which is a different kind of information from the opponent taking something
/// away. They remain visible through the raw action list if a caller wants them.
/// </summary>
public enum ThreatKind
{
    /// <summary>Can remove one of my key assets: DESTROY, BANISH, or single-target DAMAGE.</summary>
    MinionRemoval = 0,

    /// <summary>Can wipe several of my minions at once: DAMAGE or DESTROY aimed at a whole side.</summary>
    BoardSweep = 1,

    /// <summary>Can press my core: face damage or castle damage.</summary>
    FaceDamage = 2,

    /// <summary>Can make me discard.</summary>
    Discard = 3,

    /// <summary>Can negate or prevent an effect of mine: NEGATE, protection, counter keywords.</summary>
    Negation = 4,

    /// <summary>Can weaken without killing: ENFEEBLE, CONTROL.</summary>
    Disruption = 5,

    /// <summary>Can add bodies, threatening a board it does not yet have.</summary>
    Deployment = 6,

    /// <summary>
    /// An effect the split does not classify. Present so nothing is silently
    /// dropped: an unclassified action still contributes to this bucket, and a
    /// test pins which actions live here.
    /// </summary>
    Other = 7,
}

/// <summary>
/// Who an effect is aimed at. Only the target kinds that change what an effect
/// can take from the opponent are modelled; anything unrecognised maps to
/// <see cref="EffectTargetKind.Unknown"/>, which lands in
/// <see cref="ThreatKind.Other"/> rather than being assumed harmless.
/// </summary>
public enum EffectTargetKind
{
    Unknown = 0,

    /// <summary>A single enemy entity (ENEMY_TARGET, ENEMY_MINION, ENEMY_SINGLE, SINGLE_ENEMY).</summary>
    SingleEnemy = 1,

    /// <summary>A whole enemy side (ALL_ENEMY_MINIONS, ENEMY_MINIONS).</summary>
    AllEnemyMinions = 2,

    /// <summary>Both sides (ALL_MINIONS).</summary>
    AllMinions = 3,

    /// <summary>The opposing core (ENEMY_FACE).</summary>
    EnemyFace = 4,

    /// <summary>Own side (FRIENDLY_MINION, ALL_FRIENDLY_MINIONS, SELF).</summary>
    OwnSide = 5,

    /// <summary>No target field at all.</summary>
    None = 6,

    /// <summary>Any minion, either side (ANY_MINION).</summary>
    AnyMinion = 7,
}

/// <summary>
/// One threat question's answer: the probability that the opponent is holding at
/// least one card of the category, plus the arithmetic that produced it so a
/// report can be checked by hand instead of trusted.
/// </summary>
public sealed class ThreatProbability
{
    public ThreatProbability(
        ThreatKind kind,
        int unseenCopies,
        int poolSize,
        int handSize,
        double probability,
        double expectedCardsInHand,
        double expectedPlayableThisTurn,
        bool hasConditionallyPlayableCards = false,
        int scheduledCopies = 0)
    {
        Kind = kind;
        UnseenCopies = unseenCopies;
        PoolSize = poolSize;
        HandSize = handSize;
        Probability = probability;
        ExpectedCardsInHand = expectedCardsInHand;
        ExpectedPlayableThisTurn = expectedPlayableThisTurn;
        HasConditionallyPlayableCards = hasConditionallyPlayableCards;
        ScheduledCopies = scheduledCopies;
    }

    public ThreatKind Kind { get; }

    /// <summary>Copies of this category the opponent could still be holding.</summary>
    public int UnseenCopies { get; }

    /// <summary>
    /// The population this reading was sampled from: the opponent's declared
    /// copies, minus the copies public record PROVES they already hold. It is the
    /// hidden pool, not the declared total, so a caller that supplied a public
    /// record will see it smaller than the declared list.
    /// </summary>
    public int PoolSize { get; }

    /// <summary>The opponent's public hand size.</summary>
    public int HandSize { get; }

    /// <summary>
    /// P(at least one copy of this category is in the opponent's hand).
    ///
    /// Read this with care: over a 60-card pool with a 5-card hand it is close to
    /// 1 for any broad category, because the hand is a large share of the pool.
    /// It answers "could he hold one", not "is this a threat".
    /// <see cref="ExpectedCardsInHand"/> is the number that discriminates.
    /// </summary>
    public double Probability { get; }

    /// <summary>
    /// Expected copies of this category in the opponent's hand: the per-turn
    /// threat size, which is what a decision should be based on rather than the
    /// "at least one" probability.
    ///
    /// THIS IS A LOWER BOUND WHEN <see cref="HasConditionallyPlayableCards"/> IS
    /// TRUE. It is the mean of a uniform draw, and a hand is not a uniform draw:
    /// cards that cannot be played unless a condition holds wait in the hand
    /// instead of being spent, so they accumulate there. Measured over 3840 real
    /// engine samples, the share of the opponent's hand made up of Negation cards
    /// is 1.45x their share of the declared pool, while every category of
    /// unconditionally playable cards sits between 0.73x and 1.18x. The estimate
    /// understated the Negation hold count by 27% (0.90 predicted against 1.16
    /// observed) for exactly this reason, and no snapshot input can correct it:
    /// the estimator can see the hand SIZE but never the hand's contents.
    /// </summary>
    public double ExpectedCardsInHand { get; }

    /// <summary>
    /// Expected number of cards of this category the opponent can actually get
    /// onto the table this turn. Tags may each be used only once per turn
    /// (the engine's own rule), and every shipped card carries exactly one tag,
    /// so this is bounded by the number of distinct tag groups the category
    /// spans, which is far smaller than the number of copies held.
    /// </summary>
    public double ExpectedPlayableThisTurn { get; }

    /// <summary>
    /// True when at least one card in this category can only be played while a
    /// condition holds — i.e. it declares an ambush trigger or a punish condition.
    ///
    /// Such a card cannot be spent freely, so it waits in the hand, so the hand
    /// over-represents its category. A consumer that needs a bound rather than a
    /// point estimate should read <see cref="ExpectedCardsInHand"/> as a lower
    /// bound whenever this is true. Derived from the card data, not from a list of
    /// card names, so a new set that adds conditionally playable cards is covered
    /// without changing this code.
    ///
    /// THIS FLAG IS A SAFE OVER-APPROXIMATION AND IS NOT A PREDICTOR OF WHICH
    /// CATEGORY IS WRONG. 28 of the 91 shipped cards are conditionally playable, so
    /// every one of the seven shipped categories contains at least one such card and
    /// the flag is currently true throughout. It says "this reading may be low", not
    /// "this reading is low". The category where the bias is actually large was
    /// Negation (retention 1.45 against 0.73-1.18 for the others), and separating
    /// "may be low" from "is low" needs a card-level breakdown that this reading does
    /// not carry; the measured numbers live in
    /// docs/PL_THREAT_CALIBRATION_2026-09-12.md §4.17.
    /// </summary>
    public bool HasConditionallyPlayableCards { get; }

    /// <summary>
    /// Copies of this category that public record says will ENTER the opponent's hand
    /// later, and are therefore not playable yet.
    ///
    /// These are included in <see cref="ExpectedCardsInHand"/> only because their
    /// place in the hand is already reserved, which is what keeps the remaining
    /// hidden cards no worse off. A consumer deciding what can be played THIS turn
    /// should subtract this: a card that arrives in three turns is a warning to plan
    /// for, not a card that can be aimed at the viewer now. Zero unless the caller
    /// supplied <see cref="PublicHandRecord.ScheduledForHand"/>.
    /// </summary>
    public int ScheduledCopies { get; }
}

/// <summary>
/// How close each side is to its own win condition, and what stands in the way.
/// </summary>
public sealed class WinDistance
{
    public WinDistance(
        int playerIndex,
        string condition,
        int current,
        int required,
        int? remainingTurns,
        string? unmeasurableReason)
    {
        PlayerIndex = playerIndex;
        Condition = condition;
        Current = current;
        Required = required;
        RemainingTurns = remainingTurns;
        UnmeasurableReason = unmeasurableReason;
    }

    public int PlayerIndex { get; }

    /// <summary>The leader's win condition id, e.g. <c>PULL_TOTAL_GE</c>.</summary>
    public string Condition { get; }

    /// <summary>Progress toward the condition, from the engine's own reporting.</summary>
    public int Current { get; }

    /// <summary>The threshold, from the leader's data.</summary>
    public int Required { get; }

    /// <summary>
    /// Estimated turns until this player wins, or null when the estimate cannot
    /// be made from viewer-safe information (see <see cref="UnmeasurableReason"/>).
    /// </summary>
    public int? RemainingTurns { get; }

    /// <summary>Why <see cref="RemainingTurns"/> is null; null when it is set.</summary>
    public string? UnmeasurableReason { get; }

    public int Remaining => Math.Max(0, Required - Current);

    public override string ToString() =>
        Condition + " " + Current + "/" + Required
        + (RemainingTurns.HasValue
            ? " (" + RemainingTurns.Value.ToString(CultureInfo.InvariantCulture) + " turns)"
            : " (unmeasurable: " + UnmeasurableReason + ")");
}

/// <summary>
/// One leader's win condition, as DATA rather than as a declared number. Supplied
/// by the caller from the card catalog (a public artifact: the leader sitting in a
/// visible leader zone names itself, and its win condition is printed on the card).
/// </summary>
public sealed class LeaderWinCondition
{
    public LeaderWinCondition(string cardId, string condition, int required)
    {
        CardId = cardId ?? string.Empty;
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Required = required;
    }

    /// <summary>The leader card's id, e.g. <c>wood_leader</c>.</summary>
    public string CardId { get; }

    /// <summary>One of the engine's win condition ids, e.g. <c>GIANT_HEALTH_GE</c>.</summary>
    public string Condition { get; }

    /// <summary>The threshold the engine compares progress against.</summary>
    public int Required { get; }
}

/// <summary>
/// What is known about one side's progress toward its win condition, including an
/// explicit statement of what is NOT knowable.
///
/// The v1.31 viewer snapshot does not publish win conditions, and it publishes only
/// some of the counters the engine's own win evaluator reads. This type therefore
/// separates three cases that must never be conflated:
///  - the condition is known and its progress is measured;
///  - the condition is known but its progress counter is NOT published;
///  - no condition is known at all for that side.
/// A caller can then refuse to act on an unknown rather than treating it as zero.
/// </summary>
public sealed class WinProgress
{
    public WinProgress(
        int playerIndex,
        string? condition,
        int? required,
        int? current,
        string progressSource,
        int? remaining,
        int? remainingTurns,
        string? unmeasurableReason)
    {
        PlayerIndex = playerIndex;
        Condition = condition;
        Required = required;
        Current = current;
        ProgressSource = progressSource;
        Remaining = remaining;
        RemainingTurns = remainingTurns;
        UnmeasurableReason = unmeasurableReason;
    }

    public int PlayerIndex { get; }

    /// <summary>The win condition id, or null when the side's leader is unknown.</summary>
    public string? Condition { get; }

    /// <summary>The threshold, or null when the condition is unknown.</summary>
    public int? Required { get; }

    /// <summary>Measured progress, or null when the counter is not published.</summary>
    public int? Current { get; }

    /// <summary>Which snapshot field supplied <see cref="Current"/>; empty when none did.</summary>
    public string ProgressSource { get; }

    /// <summary>Required minus current, or null when either side of that is unknown.</summary>
    public int? Remaining { get; }

    /// <summary>Estimated turns to win, or null.</summary>
    public int? RemainingTurns { get; }

    /// <summary>Why the estimate is incomplete; null only when everything is known.</summary>
    public string? UnmeasurableReason { get; }

    /// <summary>True when the condition, the threshold and the progress are all known.</summary>
    public bool IsFullyMeasured => Condition is not null && Required.HasValue && Current.HasValue;

    public override string ToString()
    {
        if (Condition is null) return "player " + PlayerIndex + ": no known win condition";
        var progress = Current.HasValue
            ? Current.Value + "/" + Required
            : "?/" + Required + " (progress not published)";
        var turns = RemainingTurns.HasValue
            ? ", " + RemainingTurns.Value.ToString(CultureInfo.InvariantCulture) + " turns"
            : ", turns unknown: " + UnmeasurableReason;
        return "player " + PlayerIndex + ": " + Condition + " " + progress + turns;
    }
}

/// <summary>
/// What the opponent can do from the board alone, where no hidden information is
/// involved at all.
/// </summary>
public sealed class BoardThreat
{
    public BoardThreat(int minionCount, int attackSum, int largestAttack, int castleHealth, int? playerLife)
    {
        MinionCount = minionCount;
        AttackSum = attackSum;
        LargestAttack = largestAttack;
        CastleHealth = castleHealth;
        PlayerLife = playerLife;
    }

    public int MinionCount { get; }

    /// <summary>Total current attack across the opponent's board.</summary>
    public int AttackSum { get; }

    public int LargestAttack { get; }

    /// <summary>The shared Royal Castle's remaining health (0 when disabled).</summary>
    public int CastleHealth { get; }

    /// <summary>The viewer's own life, when the contract publishes it.</summary>
    public int? PlayerLife { get; }
}

/// <summary>
/// One leader's win condition, declared by the caller.
///
/// This is a separate input because the v1.31 viewer snapshot does NOT publish
/// leader win conditions: <c>RuntimeCardSnapshot</c> carries entity, card id,
/// owner, sealed, attack, health, chant and landmark progress, and no
/// win-condition field (only the legacy v1.30 <c>CardDto</c> has
/// <c>LeaderWinCondition</c>). Rather than guess, this estimator requires the
/// caller to state it, so a missing wiring shows up as a stated input rather
/// than as a silent zero.
/// </summary>
public sealed class WinConditionInput
{
    public WinConditionInput(int playerIndex, string condition, int required, int current, double? progressPerTurn = null)
    {
        PlayerIndex = playerIndex;
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Required = required;
        Current = current;
        ProgressPerTurn = progressPerTurn;
    }

    public int PlayerIndex { get; }

    /// <summary>One of the engine's win condition ids, e.g. <c>PULL_TOTAL_GE</c>.</summary>
    public string Condition { get; }

    public int Required { get; }

    /// <summary>Progress as the engine reported it (for example a VICTORY_PROGRESS event).</summary>
    public int Current { get; }

    /// <summary>
    /// Optional observed progress per turn for this condition. Supplied by the
    /// caller because the v1.31 viewer snapshot does not publish the counters the
    /// engine's win evaluator reads — it has no <c>TotalDiscarded</c> and no
    /// <c>PunishDrawnThisTurn</c>, and it publishes <c>pullCount</c> as a total
    /// with no turn baseline. Passing null means "no rate is known", and the
    /// estimate then declines to predict a turn count rather than inventing one.
    /// </summary>
    public double? ProgressPerTurn { get; }
}

/// <summary>
/// The whole answer for one snapshot.
/// </summary>
public sealed class ThreatReport
{
    public ThreatReport(
        int viewerIndex,
        int opponentIndex,
        int opponentHandCount,
        int opponentDeckCount,
        int unknownPoolSize,
        int accountedCopies,
        int visibleCopiesInPool,
        IReadOnlyDictionary<string, int> visibleByCardId,
        IReadOnlyList<ThreatProbability> probabilities,
        IReadOnlyList<WinDistance> winDistances,
        BoardThreat boardThreat,
        IReadOnlyList<string> notes)
    {
        ViewerIndex = viewerIndex;
        OpponentIndex = opponentIndex;
        OpponentHandCount = opponentHandCount;
        OpponentDeckCount = opponentDeckCount;
        UnknownPoolSize = unknownPoolSize;
        AccountedCopies = accountedCopies;
        VisibleCopiesInPool = visibleCopiesInPool;
        VisibleByCardId = visibleByCardId ?? throw new ArgumentNullException(nameof(visibleByCardId));
        Probabilities = probabilities;
        WinDistances = winDistances;
        BoardThreat = boardThreat;
        Notes = notes;
    }

    public int ViewerIndex { get; }

    public int OpponentIndex { get; }

    public int OpponentHandCount { get; }

    public int OpponentDeckCount { get; }

    /// <summary>Cards the opponent could still hold: hand + deck, both public counts.</summary>
    public int UnknownPoolSize { get; }

    /// <summary>Copies the card pool says exist but that are not publicly visible.</summary>
    public int AccountedCopies { get; }

    /// <summary>
    /// Copies of the opponent's declared pool the viewer can place. Published so the
    /// accounting identity is machine-checkable rather than buried in a diagnostic:
    /// <c>AccountedCopies + VisibleCopiesInPool</c> must equal the declared total the
    /// caller supplied. A mismatch is a real defect and not a reporting detail.
    /// </summary>
    public int VisibleCopiesInPool { get; }

    /// <summary>
    /// WHICH card ids the viewer could place, and how many copies of each. Published
    /// for the same reason as <see cref="VisibleCopiesInPool"/>: when an accounting
    /// surprise appears, the question is always "which card did it place", and
    /// inferring that from aggregate counts cost this project several rounds.
    /// </summary>
    public IReadOnlyDictionary<string, int> VisibleByCardId { get; }    public IReadOnlyList<ThreatProbability> Probabilities { get; }

    public IReadOnlyList<WinDistance> WinDistances { get; }

    public BoardThreat BoardThreat { get; }

    /// <summary>Stated limitations of this particular report, never silently dropped.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>The probability for one category, or null when it was not asked.</summary>
    public ThreatProbability? For(ThreatKind kind)
    {
        foreach (var probability in Probabilities)
        {
            if (probability.Kind == kind) return probability;
        }

        return null;
    }

    /// <summary>The distance entry for one player, or null when it was not supplied.</summary>
    public WinDistance? DistanceFor(int playerIndex)
    {
        foreach (var distance in WinDistances)
        {
            if (distance.PlayerIndex == playerIndex) return distance;
        }

        return null;
    }
}

/// <summary>
/// Estimates what the opponent can still be holding, and how close each side is
/// to winning, using ONLY what the viewer is allowed to know.
///
/// This type is deliberately a pure function of its inputs. It never sees a
/// <c>GameState</c>, never reads the opponent's hand or ambush zone (the v1.31
/// projection already empties both for a non-owner viewer, and this type does
/// not compensate for that by any other route), and never invents a card.
///
/// The estimate rests on two assumptions, stated so they can be falsified:
///
/// 1. The opponent's unseen cards are a uniformly random sample, so their hand is
///    a uniform sample of size H from the pool of size U that is not publicly
///    visible. The deck half of this holds because the deck is shuffled and no
///    effect reorders or inspects it (the only reshuffle-related effect is
///    SKIP_RESHUFFLE). If a scry/reorder effect is ever added, this becomes wrong
///    and must be revisited.
///
/// 2. THE HAND IS A UNIFORM SAMPLE. This one is FALSE IN GENERAL, and it is
///    measured rather than assumed away. Cards that can only be played while a
///    condition holds — ambush cards waiting for their trigger, punish cards
///    waiting for their condition — wait in the hand instead of being spent, so
///    the hand over-represents them. Over 3840 real engine samples the retention
///    ratio (a category's share of the hand divided by its share of the declared
///    pool) was 1.45 for Negation, whose cards are all ambush NEGATEs, against
///    0.73-1.18 for every category of unconditionally playable cards. The
///    consequence is that ExpectedCardsInHand understates a conditionally
///    playable category, and the direction of the error is known (low). Because
///    the snapshot publishes the opponent's hand SIZE but never its contents, no
///    input is available to correct it; the honest treatment is to declare it on
///    the reading via ThreatProbability.HasConditionallyPlayableCards and let the
///    consumer treat the number as a lower bound for those categories.
/// </summary>
public static class ThreatEstimator
{

/// <summary>
/// Derives win progress for BOTH sides from public information only: the leader
/// card is visible in the leader zone, so its identity — and therefore its printed
/// win condition — is known, and the snapshot's public counters supply whatever
/// progress can be measured.
///
/// This exists so a caller does not have to hand-supply the win condition, which
/// would let a wrong declaration go unnoticed. Anything that cannot be measured is
/// reported as unmeasurable with a reason, never as zero.
///
/// <paramref name="conditions"/> maps leader card id to its win condition data,
/// read by the caller from the public card catalog.
/// </summary>
public static IReadOnlyList<WinProgress> DeriveWinProgress(
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

    var result = new List<WinProgress>();
    for (var playerIndex = 0; playerIndex < 2; playerIndex++)
    {
        var player = snapshot.Players[playerIndex];
        var leaderId = LeaderCardId(player);
        if (leaderId is null)
        {
            result.Add(new WinProgress(
                playerIndex,
                condition: null,
                required: null,
                current: null,
                progressSource: string.Empty,
                remaining: null,
                remainingTurns: null,
                unmeasurableReason: "no leader card is visible in the leader zone"));
            continue;
        }

        if (!conditions.TryGetValue(leaderId, out var condition) || condition is null)
        {
            result.Add(new WinProgress(
                playerIndex,
                condition: null,
                required: null,
                current: null,
                progressSource: string.Empty,
                remaining: null,
                remainingTurns: null,
                unmeasurableReason: "leader '" + leaderId + "' has no win condition data supplied"));
            continue;
        }

        var (current, counterName, counterReason) = ProgressOf(snapshot, playerIndex, condition.Condition);
        int? remaining = current.HasValue ? Math.Max(0, condition.Required - current.Value) : null;
        result.Add(new WinProgress(
            playerIndex,
            condition.Condition,
            condition.Required,
            current,
            counterName,
            remaining,
            // A turn estimate needs BOTH a measured progress and a rate, and the
            // snapshot publishes no per-turn rate for any of these axes. Claiming
            // one would be invention, so it stays null with a stated reason.
            remainingTurns: null,
            unmeasurableReason: current.HasValue
                ? "the snapshot publishes no per-turn rate for " + condition.Condition + ", so a turn estimate would be a guess"
                : counterReason));
    }

    return result;
}

/// <summary>The id of the leader card visible in one side's leader zone, if any.</summary>
private static string? LeaderCardId(RuntimePlayerSnapshot player)
{
    if (player.LeaderZone is null) return null;
    foreach (var card in player.LeaderZone)
    {
        if (card is null || string.IsNullOrEmpty(card.CardId)) continue;
        return card.CardId;
    }

    return null;
}

/// <summary>
/// The progress counter for one win condition.
///
/// This deliberately does NOT switch on condition-id literals: it delegates to
/// <see cref="WinConditionCounters"/>, which parses the id into a metric family and
/// resolves it from a single documented table. A condition in an unregistered
/// family comes back unmeasured with the family NAMED, so a new series surfaces as
/// "this axis needs a counter" instead of as another silent unknown.
/// </summary>
private static (int? Current, string CounterName, string? Reason) ProgressOf(
    RuntimeSnapshotEnvelope snapshot,
    int playerIndex,
    string condition)
{
    var reading = WinConditionCounters.Resolve(snapshot, playerIndex, condition);
    return reading.IsMeasured
        ? (reading.Current, reading.Source, null)
        : (null, string.Empty, reading.Source);
}

/// <summary>
/// Every effect-bearing field on a card. A card threatens a category if any of
/// its effects can take that thing from the viewer. Punish effects count: a card
/// is activated by its owner paying its punish cost, so its punish effects are
/// something the opponent can choose to bring down.
/// </summary>
public static readonly string[] EffectFields = new[]
{
    "onPlayEffects",
    "punishEffects",
    "commitEffects",
    "pushEffects",
    "pullEffects",
    "ambushEffects",
    "chantEffects",
    "onOpponentDiscardEffects",
};

/// <summary>
/// The threat categories a card belongs to, derived from its effects. Pure: no
/// state, no ordering dependence.
///
/// A card threatens a category if ANY of its effects can take that thing from the
/// opponent. The target matters: the same DAMAGE action is a single-asset removal
/// against one enemy minion, a board sweep against a whole enemy side, and core
/// pressure against the face.
/// </summary>
public static IReadOnlyList<ThreatKind> CategoriesOf(IEnumerable<string> effectActions)
{
    if (effectActions is null) throw new ArgumentNullException(nameof(effectActions));

    var pairs = new List<EffectSpecPair>();
    foreach (var action in effectActions)
    {
        // No target information supplied: classify on the action alone, which
        // routes DAMAGE to Other rather than guessing its target.
        pairs.Add(new EffectSpecPair(action, EffectTargetKind.None));
    }

    return CategoriesOf(pairs);
}

/// <summary>
/// The threat categories a card belongs to, from its action/target pairs.
/// </summary>
public static IReadOnlyList<ThreatKind> CategoriesOf(IEnumerable<EffectSpecPair> effects)
{
    if (effects is null) throw new ArgumentNullException(nameof(effects));

    var found = new List<ThreatKind>();
    var seen = new HashSet<ThreatKind>();
    foreach (var effect in effects)
    {
        foreach (var kind in Classify(effect.Action, effect.Target))
        {
            if (seen.Add(kind)) found.Add(kind);
        }
    }

    return found;
}

/// <summary>Maps one effect action plus its target to the threats it represents.</summary>
public static IReadOnlyList<ThreatKind> Classify(string action, EffectTargetKind target)
{
    var result = new List<ThreatKind>();

    switch (action)
    {
        case "DESTROY":
        case "BANISH":
            result.Add(
                target == EffectTargetKind.AllEnemyMinions || target == EffectTargetKind.AllMinions
                    ? ThreatKind.BoardSweep
                    : ThreatKind.MinionRemoval);
            break;

        case "DAMAGE":
            switch (target)
            {
                case EffectTargetKind.SingleEnemy:
                case EffectTargetKind.AnyMinion:
                    result.Add(ThreatKind.MinionRemoval);
                    break;
                case EffectTargetKind.AllEnemyMinions:
                    // Hits the whole enemy board, which in this game's model
                    // includes the face, so it presses the core as well.
                    result.Add(ThreatKind.BoardSweep);
                    result.Add(ThreatKind.FaceDamage);
                    break;
                case EffectTargetKind.AllMinions:
                    result.Add(ThreatKind.BoardSweep);
                    break;
                case EffectTargetKind.EnemyFace:
                    result.Add(ThreatKind.FaceDamage);
                    break;
                default:
                    result.Add(ThreatKind.Other);
                    break;
            }

            break;

        case "DAMAGE_CASTLE":
            result.Add(ThreatKind.FaceDamage);
            break;

        case "DISCARD_OPP_RANDOM":
        case "DISCARD_DRAWN":
        case "CONVERT_PUNISH_TO_DISCARD":
            result.Add(ThreatKind.Discard);
            break;

        case "NEGATE":
        case "NEGATE_ENEMY_EFFECTS_TURN":
        case "PROTECT_TURN":
            result.Add(ThreatKind.Negation);
            break;

        case "ENFEEBLE":
        case "CONTROL":
            result.Add(ThreatKind.Disruption);
            break;

        case "SUMMON":
        case "SUMMON_LEADER":
            result.Add(ThreatKind.Deployment);
            break;

        default:
            // Ally-directed support (HEAL, BUFF, GRANT_KEYWORD, RESTORE_ATTACKS),
            // resource effects (DRAW, ADD_ROOT, ADD_RAMPANT), and anything not yet
            // modelled. None of these takes anything from the opponent, so none is
            // a threat — but none is silently dropped either.
            result.Add(ThreatKind.Other);
            break;
    }

    return result;
}

/// <summary>One effect's action paired with the kind of target it is aimed at.</summary>
public readonly struct EffectSpecPair
{
    public EffectSpecPair(string action, EffectTargetKind target)
    {
        Action = action ?? string.Empty;
        Target = target;
    }

    public string Action { get; }

    public EffectTargetKind Target { get; }
}

/// <summary>
/// Expected number of successes in a hand of <paramref name="draws"/> drawn
/// without replacement from a pool of <paramref name="population"/> containing
/// <paramref name="successes"/>. This is the mean of the hypergeometric
/// distribution, and it is the number that actually discriminates between
/// categories: unlike "at least one", it does not saturate near 1 when the hand
/// is a large share of the pool.
/// </summary>
public static double ExpectedCopiesInHand(int population, int successes, int draws)
{
    if (population < 0) throw new ArgumentOutOfRangeException(nameof(population));
    if (successes < 0) throw new ArgumentOutOfRangeException(nameof(successes));
    if (draws < 0) throw new ArgumentOutOfRangeException(nameof(draws));
    if (successes > population) throw new ArgumentOutOfRangeException(nameof(successes), "successes cannot exceed population.");
    if (draws > population) throw new ArgumentOutOfRangeException(nameof(draws), "draws cannot exceed population.");
    if (population == 0) return 0.0;
    return successes * (draws / (double)population);
}

/// <summary>
/// P(at least one success) for drawing <paramref name="draws"/> cards without
/// replacement from <paramref name="population"/> cards that contain
/// <paramref name="successes"/> successes.
///
/// Computed as 1 - C(pop-succ, draws) / C(pop, draws). When there are fewer
/// non-successes than draws the whole population must be drawn, so the
/// probability is 1 (the complement is empty by convention).
/// </summary>
public static double AtLeastOneProbability(int population, int successes, int draws)
{
    if (population < 0) throw new ArgumentOutOfRangeException(nameof(population));
    if (successes < 0) throw new ArgumentOutOfRangeException(nameof(successes));
    if (draws < 0) throw new ArgumentOutOfRangeException(nameof(draws));
    if (successes > population) throw new ArgumentOutOfRangeException(nameof(successes), "successes cannot exceed population.");
    if (draws > population) throw new ArgumentOutOfRangeException(nameof(draws), "draws cannot exceed population.");
    if (successes == 0 || draws == 0) return 0.0;

    var failures = population - successes;
    if (failures < draws) return 1.0;

    var noSuccess = Math.Exp(LogCombination(failures, draws) - LogCombination(population, draws));
    var result = 1.0 - noSuccess;
    if (result < 0.0) return 0.0;
    if (result > 1.0) return 1.0;
    return result;
}


/// <summary>
/// Estimate one snapshot.
///
/// <paramref name="pool"/> is the opponent's deck list (card id to copy count) —
/// public information, because every shipped deck is a fixed published list. It
/// serves two roles: the population the opponent's hand is drawn from, and the
/// multiset the accounting invariant reconciles against their public counters.
///
/// <paramref name="otherPool"/> is the VIEWER'S deck list. It cannot be used as
/// population (measured worse: see the note at the population site), so it is
/// accepted only so a caller can hand over the full public information and so a
/// future model can use it. Passing null is the measured-better default.
///
/// <paramref name="categories"/> maps card id to its threat categories. It is
/// supplied rather than read here so this stays a pure function.
///
/// <paramref name="conditionallyPlayableCardIds"/> names the card ids that can only
/// be played while a condition holds (an ambush trigger or a punish condition).
/// It exists so the report can flag the categories whose ExpectedCardsInHand is a
/// lower bound rather than a point estimate: such cards wait in the hand, so the
/// hand over-represents them. Deriving the set from card data rather than from a
/// list of names is what keeps a new set working without changing this code.
///
/// <paramref name="publicHand"/> is what public record establishes about the
/// opponent's hand — see <see cref="PublicHandRecord"/>. It is mechanism-agnostic on
/// purpose: a commit-then-rollback, a delayed add-to-hand clause, a reveal or a
/// search all report through the same shape, so a future mechanic needs no change
/// here. Supplying it can only improve the estimate, never inflate it beyond what the
/// record actually proves: certain copies are credited, scheduled copies are only
/// removed from the hidden pool. Passing null models the opponent as offering no
/// public record, which is what a caller that has read none knows.
/// </summary>
public static ThreatReport Estimate(
    RuntimeSnapshotEnvelope snapshot,
    int viewerIndex,
    IReadOnlyDictionary<string, int> pool,
    IReadOnlyDictionary<string, IReadOnlyList<ThreatKind>> categories,
    IReadOnlyList<WinConditionInput>? winConditions = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? tagsByCardId = null,
    IReadOnlyDictionary<string, int>? otherPool = null,
    IReadOnlyCollection<string>? conditionallyPlayableCardIds = null,
    PublicHandRecord? publicHand = null)
{
    if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
    if (pool is null) throw new ArgumentNullException(nameof(pool));
    if (categories is null) throw new ArgumentNullException(nameof(categories));
    if (viewerIndex is < 0 or > 1)
        throw new ArgumentOutOfRangeException(nameof(viewerIndex), "viewerIndex must be 0 or 1.");
    if (snapshot.Players is null || snapshot.Players.Count != 2)
        throw new ArgumentException("The snapshot must publish exactly two players.", nameof(snapshot));

    var opponentIndex = 1 - viewerIndex;
    var opponent = snapshot.Players[opponentIndex];
    var notes = new List<string>();

    // --- what the opponent could still be holding -------------------------
    // Three public counts, and the distinction between them matters:
    //   handCount    - cards in hand right now (the draw size for the estimate)
    //   deckCount    - cards left in the deck
    //   ambushCount  - the opponent's SET, UNFLIPPED ambushes, counted by
    //                  AmbushCount and never listed
    // A set ambush is a real card the opponent owns that is in no public zone,
    // and a resolved ambush returns to its OWNER'S HAND (see
    // EffectRuntime.Cards.cs, the ambush-return path). So an ambush card is a
    // hidden round-trip: deck -> hidden ambush zone -> hand. Any pool arithmetic
    // that omits the ambush zone under-counts the opponent's unseen cards by
    // exactly that amount.
    var handCount = opponent.HandCount;
    var deckCount = opponent.DeckCount;
    var ambushCount = opponent.AmbushCount;

    // NOTE: the hidden pool is NOT hand + deck + ambush. That expression counts
    // only cards in the OPPONENT'S OWN zones, so it cannot see a viewer-owned card
    // that moved into the opponent's hand. The pool is therefore derived from the
    // declared multiset and the viewer's own placements (see `hiddenUnseen` below,
    // computed after the accounting loop). handCount/deckCount/ambushCount are
    // still reported individually for provenance.

    // Copies the pool says exist, minus copies already publicly visible. The
    // visible zones are field, graveyard, commit queue and cloud stack of the
    // OPPONENT only. Ambush zones are excluded: an unflipped ambush is hidden.
    //
    // THE VIEWER'S OWN CARDS ARE DELIBERATELY NOT SUBTRACTED. An earlier revision
    // counted the viewer's public zones and hand as well, on the reasoning that a
    // card the viewer holds cannot be in the opponent's deck. That reasoning is
    // false for this game: each player draws from their OWN 60-card deck, so both
    // sides can hold the same card id at the same time. Measured over real engine
    // matches, the two hands hold a common card id in 10028 (sample, card) pairs —
    // the overlap is the normal case, not an edge case. Subtracting the viewer's
    // copies therefore invented certainty: in a mirror match the viewer holding 3
    // copies of an ambush deducted all 3 of the opponent's declared copies, leaving
    // the estimator convinced the opponent held none of a card it was in fact
    // holding. That produced 345 false "the opponent holds a card the estimator
    // never predicted" results, all of them in same-faction pairings.
    //
    // Cross-deck transfer was the original justification and does not support it:
    // control changes in the engine move cards only between Field zones
    // (EffectRuntime.Mechanical.cs, TurnFlow.ResolveControlExpiry), and no effect
    // adds a card to a player's hand from another player's zones. A genuine
    // transfer into the opponent's hand would surface as an accounting mismatch
    // below, which is reported rather than hidden.
    var visible = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var cardId in VisibleZones(opponent)) Add(visible, cardId, 1);

    var unseenByKind = new Dictionary<ThreatKind, double>();
    var tagsByKind = new Dictionary<ThreatKind, HashSet<string>>();
    var conditionalByKind = new HashSet<ThreatKind>();
    var accounted = 0;
    var poolTotalCopies = 0;

    foreach (var entry in pool)
    {
        if (string.IsNullOrEmpty(entry.Key)) continue;
        var totalCopies = entry.Value;
        if (totalCopies <= 0) continue;

        visible.TryGetValue(entry.Key, out var seen);
        var unseen = totalCopies - seen;
        if (unseen <= 0) continue;

        accounted += unseen;
        poolTotalCopies += totalCopies;

        if (!categories.TryGetValue(entry.Key, out var kinds) || kinds is null) continue;
        string[] tags;
        if (tagsByCardId is null || !tagsByCardId.TryGetValue(entry.Key, out var tagList) || tagList is null)
        {
            // No tag data for this card: treat it as its own group so the
            // playable bound cannot overstate what one turn can produce.
            tags = new[] { entry.Key };
        }
        else
        {
            tags = tagList.ToArray();
        }

        foreach (var kind in kinds)
        {
            unseenByKind.TryGetValue(kind, out var running);
            unseenByKind[kind] = running + unseen;

            if (conditionallyPlayableCardIds is not null
                && conditionallyPlayableCardIds.Contains(entry.Key))
            {
                conditionalByKind.Add(kind);
            }

            if (!tagsByKind.TryGetValue(kind, out var groups))
            {
                groups = new HashSet<string>(StringComparer.Ordinal);
                tagsByKind[kind] = groups;
            }

            if (tags.Length == 0)
            {
                groups.Add(entry.Key);
            }
            else
            {
                foreach (var tag in tags) groups.Add(tag);
            }
        }
    }

    // THE INVARIANT: the opponent's own declared pool, minus the copies of it the
    // viewer can see on the OPPONENT'S OWN side, must equal the hidden-card count
    // the opponent's public zone counters report. `poolTotalCopies`/`accounted`
    // come from the card list plus the opponent's public zones; `observedHidden` is
    // read independently off the snapshot. When the two disagree, a source of cards
    // is unmodelled — a card that crossed between decks being the case this check
    // exists to catch (such a card is not declared by the opponent's list, so no
    // list-based arithmetic can place it). The mismatch is reported, not hidden.
    var hiddenUnseen = poolTotalCopies - accounted;
    var observedHidden = handCount + deckCount + ambushCount;

    if (hiddenUnseen != observedHidden)
    {
        notes.Add(
            "accounting mismatch: the opponent's declared list accounts for "
            + hiddenUnseen.ToString(CultureInfo.InvariantCulture)
            + " hidden cards but their public zone counts say "
            + observedHidden.ToString(CultureInfo.InvariantCulture)
            + " (hand " + handCount.ToString(CultureInfo.InvariantCulture)
            + " + deck " + deckCount.ToString(CultureInfo.InvariantCulture)
            + " + ambush " + ambushCount.ToString(CultureInfo.InvariantCulture)
            + "); list total " + poolTotalCopies.ToString(CultureInfo.InvariantCulture)
            + " unplaced " + accounted.ToString(CultureInfo.InvariantCulture));
    }

    // Never let a category exceed the pool it is drawn from: more unseen copies
    // than cards is impossible, and would silently produce probability 1. The
    // clamp applies to the AGGREGATE per category, never to a single card type,
    // because clamping per type would invent copies for cards already located.
    //
    // The draw size stays `handCount`: only hand cards are candidates for the
    // hand. The denominator is every card the opponent could still be holding.
    // The population the opponent's hand is drawn from is the OPPONENT'S OWN
    // declared pool. Using the union of both deck lists was tried and measured
    // worse (mean |P| error 0.046 -> 0.064, with Discard degrading from 0.004 to
    // 0.087): cross-deck transfers are rare, so adding the viewer's 60 cards
    // mostly dilutes the estimate. The viewer's list is therefore used only for
    // the ACCOUNTING invariant below, not for the probability population.
    var ownDeclared = 0;
    foreach (var entry in pool) if (entry.Value > 0) ownDeclared += entry.Value;

    // NOTE: the declared copies only. The opponent's SET ambushes are hidden cards
    // too, but they are NOT extra copies of the declared list — the ambush instance
    // comes out of the same pool — so adding AmbushCount here would double-count and
    // break the accounting identity. Reverted after measuring: adding it changed no
    // observed number, because the residual mismatch turned out not to be ambush
    // related at all.
    var effectivePool = Math.Max(ownDeclared, 0);

    // --- narrow the pool by what public record establishes about the hand -------
    //
    // A copy the opponent is publicly known to be holding is no longer a candidate
    // for the HIDDEN part of the pool: it is already in the hand the caller is
    // drawing from. Removing it shrinks the population without shrinking the hand,
    // which raises the estimated in-hand count — correctly, since for that identity
    // the caller has proof rather than an estimate.
    //
    // A copy that public record says will arrive in the hand LATER also leaves the
    // hidden pool, because its destination is already fixed and it cannot be part of
    // a set of "cards that might be in hand". It is NOT credited to the hand, because
    // it is not in the hand yet and these readings are about what can be played now.
    //
    // The accounting invariant above deliberately still uses the UN-narrowed pool.
    // It reconciles the declared list against the opponent's public counters plus
    // what they can still be holding, and a proven-in-hand card is still "hidden"
    // from those counters' point of view, so subtracting it there would manufacture
    // a mismatch that is not real.
    var certainInHand = 0;
    var scheduledForHand = 0;
    var certainByKind = new Dictionary<ThreatKind, double>();
    var scheduledByKind = new Dictionary<ThreatKind, double>();

    void Record(Dictionary<ThreatKind, double> byKind, string cardId, int copies)
    {
        if (!categories.TryGetValue(cardId, out var kinds) || kinds is null) return;
        foreach (var kind in kinds)
        {
            byKind.TryGetValue(kind, out var running);
            byKind[kind] = running + copies;
        }
    }

    void Absorb(IReadOnlyDictionary<string, int>? counts, bool certain)
    {
        if (counts is null) return;
        foreach (var entry in counts)
        {
            if (string.IsNullOrEmpty(entry.Key) || entry.Value <= 0) continue;
            // Never claim more copies of an identity than the opponent's own list
            // declares: a record that says otherwise is wrong, not extra certainty.
            if (!pool.TryGetValue(entry.Key, out var declared) || declared <= 0) continue;

            var copies = Math.Min(entry.Value, declared);
            if (certain)
            {
                certainInHand += copies;
                Record(certainByKind, entry.Key, copies);
            }
            else
            {
                scheduledForHand += copies;
                Record(scheduledByKind, entry.Key, copies);
            }
        }
    }

    if (publicHand is not null)
    {
        Absorb(publicHand.CertainInHand, certain: true);
        Absorb(publicHand.ScheduledForHand, certain: false);
    }

    // Both groups leave the hidden pool, so they leave its denominator. The hand that
    // the hidden cards are drawn into is what is LEFT of the pool, so the draw size
    // grows by every copy whose destination is that hand — including the scheduled
    // ones, whose place is already reserved even though they have not arrived.
    // Shrinking the denominator without growing the numerator would understate the
    // estimate, which is the same class of error this input exists to remove.
    var hiddenPool = Math.Max(effectivePool - certainInHand - scheduledForHand, 0);
    var draw = Math.Min(handCount + certainInHand + scheduledForHand, hiddenPool);
    var probabilities = new List<ThreatProbability>();
    foreach (ThreatKind kind in Enum.GetValues(typeof(ThreatKind)))
    {
        if (!unseenByKind.TryGetValue(kind, out var weightedCopies) || weightedCopies <= 0) continue;

        // The removed copies are taken out of this category's hidden count too, so the
        // successes and the population shrink together and the hypergeometric mean
        // stays the mean of the distribution actually being sampled.
        var removed = 0.0;
        if (certainByKind.TryGetValue(kind, out var certain)) removed += certain;
        if (scheduledByKind.TryGetValue(kind, out var scheduled)) removed += scheduled;
        weightedCopies = Math.Max(0.0, weightedCopies - removed);

        if (weightedCopies <= 0) continue;

        var clamped = Math.Min(weightedCopies, hiddenPool);
        var expectedInHand = ExpectedCopiesInHand(hiddenPool, Math.Min((int)Math.Round(clamped), hiddenPool), draw);

        // The playable bound: one card per tag per turn. Each shipped card
        // carries exactly one tag, so a category spanning N distinct tag groups
        // cannot put more than N of its cards on the table in one turn.
        var tagGroups = tagsByKind.TryGetValue(kind, out var groups) ? groups.Count : 0;
        var playable = Math.Min(expectedInHand, tagGroups);

        probabilities.Add(new ThreatProbability(
            kind,
            (int)Math.Ceiling(clamped),
            hiddenPool,
            handCount,
            hiddenPool == 0 ? 0.0 : AtLeastOneProbability(hiddenPool, (int)Math.Ceiling(clamped), draw),
            expectedInHand,
            Math.Max(0.0, playable),
            conditionalByKind.Contains(kind),
            scheduledByKind.TryGetValue(kind, out var scheduledCopies)
                ? (int)Math.Round(scheduledCopies)
                : 0));
    }

    if (effectivePool == 0)
    {
        notes.Add("the opponent holds no unseen cards, so no category can be in hand");
    }

    if (certainInHand > 0 || scheduledForHand > 0)
    {
        notes.Add(
            "public record: " + certainInHand.ToString(CultureInfo.InvariantCulture)
            + " card(s) certain in the opponent's hand, "
            + scheduledForHand.ToString(CultureInfo.InvariantCulture)
            + " scheduled to enter it later; the hidden pool is reduced from "
            + effectivePool.ToString(CultureInfo.InvariantCulture) + " to "
            + hiddenPool.ToString(CultureInfo.InvariantCulture)
            + ". The certain copies are facts, the scheduled ones are not in hand yet");
    }

    // --- how close each side is ------------------------------------------
    var distances = new List<WinDistance>();
    var conditions = winConditions ?? Array.Empty<WinConditionInput>();
    foreach (var condition in conditions)
    {
        if (condition.PlayerIndex is < 0 or > 1) continue;
        var rate = condition.ProgressPerTurn;
        int? turns = null;
        string? reason = null;
        var remaining = Math.Max(0, condition.Required - condition.Current);
        if (remaining == 0)
        {
            turns = 0;
        }
        else if (rate.HasValue && rate.Value > 0)
        {
            turns = (int)Math.Ceiling(remaining / rate.Value);
        }
        else
        {
            reason = rate.HasValue
                ? "progress per turn is zero, so this condition never arrives at the observed rate"
                : "no progress-per-turn rate was supplied for " + condition.Condition;
        }

        distances.Add(new WinDistance(
            condition.PlayerIndex,
            condition.Condition,
            condition.Current,
            condition.Required,
            turns,
            reason));
    }

    if (conditions.Count == 0)
    {
        notes.Add("no win conditions were supplied; the v1.31 snapshot does not publish them");
    }

    // --- what the board alone threatens -----------------------------------
    var board = BoardThreatOf(snapshot, opponentIndex, viewerIndex);

    return new ThreatReport(
        viewerIndex,
        opponentIndex,
        handCount,
        deckCount,
        effectivePool,
        accounted,
        VisibleInPool(visible, pool),
        VisibleInPoolByCard(visible, pool),
        probabilities,
        distances,
        board,
        notes);
}

/// <summary>
/// The public zones of one side: field, graveyard, commit queue and cloud stack.
///
/// The LEADER ZONE IS DELIBERATELY EXCLUDED. A leader is not a drawable card:
/// MatchSetup builds the deck from the declared card list and then adds the
/// leader as its own entity, so a leader instance is never in the pool and
/// counting it would make the accounting off by one per side. Ambush zones are
/// excluded too, because an unflipped ambush is identity the game hides.
/// </summary>
private static IEnumerable<string> VisibleZones(RuntimePlayerSnapshot player)
{
    foreach (var zone in new[]
    {
        player.Field,
        player.Graveyard,
        player.CommitQueue,
        player.CloudStack,
    })
    {
        if (zone is null) continue;
        foreach (var card in zone)
        {
            if (card is null) continue;
            if (!string.IsNullOrEmpty(card.CardId)) yield return card.CardId;
        }
    }
}

private static void Add(Dictionary<string, int> counts, string key, int amount)
{
    counts.TryGetValue(key, out var running);
    counts[key] = running + amount;
}

/// <summary>
/// How many copies of the supplied pool the viewer can place. Counted only for card
/// ids the pool actually declares, because a viewer card from a different deck was
/// never a candidate for the opponent to hold and so cannot reduce their pool.
/// Clamped to the declared count per id, so a visible copy surplus (which cannot
/// happen with well-formed data) cannot make the accounting negative.
/// </summary>
private static int VisibleInPool(
    Dictionary<string, int> visible,
    IReadOnlyDictionary<string, int> pool)
{
    var total = 0;
    foreach (var entry in VisibleInPoolByCard(visible, pool)) total += entry.Value;
    return total;
}

/// <summary>
/// The visible-copy count per card id, restricted to ids the supplied pool declares
/// and clamped to the declared count.
/// </summary>
private static IReadOnlyDictionary<string, int> VisibleInPoolByCard(
    Dictionary<string, int> visible,
    IReadOnlyDictionary<string, int> pool)
{
    var result = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var entry in pool)
    {
        if (entry.Value <= 0) continue;
        if (!visible.TryGetValue(entry.Key, out var seen) || seen <= 0) continue;
        result[entry.Key] = Math.Min(seen, entry.Value);
    }

    return result;
}

/// <summary>
/// The board-only threat: no hidden information is used, so these numbers are
/// exact rather than probabilistic.
/// </summary>
private static BoardThreat BoardThreatOf(RuntimeSnapshotEnvelope snapshot, int opponentIndex, int viewerIndex)
{
    var opponent = snapshot.Players[opponentIndex];
    var viewer = snapshot.Players[viewerIndex];

    var count = 0;
    var sum = 0;
    var largest = 0;
    if (opponent.Field is not null)
    {
        foreach (var card in opponent.Field)
        {
            if (card is null) continue;
            count++;
            var attack = card.CurrentAttack ?? 0;
            sum += attack;
            if (attack > largest) largest = attack;
        }
    }

    return new BoardThreat(
        count,
        sum,
        largest,
        snapshot.Castle is null ? 0 : snapshot.Castle.Health,
        viewer.Life);
}

/// <summary>
/// ln C(n, k) via log-gamma, so no intermediate factorial overflows.
/// </summary>
private static double LogCombination(int n, int k)
{
    if (k < 0 || k > n) return double.NegativeInfinity;
    if (k == 0 || k == n) return 0.0;
    return LogGamma(n + 1) - LogGamma(k + 1) - LogGamma(n - k + 1);
}

/// <summary>Lanczos approximation of ln Γ(x) for x &gt; 0.</summary>
private static double LogGamma(double x)
{
    double[] coefficients =
    {
        676.5203681218851,
        -1259.1392167224028,
        771.32342877765313,
        -176.61502916214059,
        12.507343278686905,
        -0.13857109526572012,
        9.9843695780195716e-6,
        1.5056327351493116e-7,
    };

    if (x < 0.5)
    {
        return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1.0 - x);
    }

    var z = x - 1.0;
    var a = 0.99999999999980993;
    var t = z + 7.5;
    for (var i = 0; i < coefficients.Length; i++)
    {
        a += coefficients[i] / (z + i + 1.0);
    }

    return 0.5 * Math.Log(2.0 * Math.PI) + (z + 0.5) * Math.Log(t) - t + Math.Log(a);
}

}


/// <summary>
/// What public record establishes about the OPPONENT'S HAND, assembled by the
/// caller from whatever the engine publishes.
///
/// This exists so the estimator never has to know WHICH mechanic produced the
/// information. Any current or future mechanic that moves a card into a hand
/// through a public window — a commit then rollback, a delayed "add to hand in N
/// turns" clause, a reveal, a search — is reported here in the same shape. A new
/// mechanic therefore requires no change to the estimator: the caller reads the
/// engine's public record and fills this in.
///
/// The estimator itself cannot derive either field, because the snapshot publishes
/// the opponent's hand SIZE but never its contents, and it carries no event log.
/// That is a deliberate hidden-information boundary, so the public record has to
/// come in from outside.
/// </summary>
public sealed class PublicHandRecord
{
    public static PublicHandRecord Empty { get; } = new PublicHandRecord(null, null);

    public PublicHandRecord(
        IReadOnlyDictionary<string, int>? certainInHand = null,
        IReadOnlyDictionary<string, int>? scheduledForHand = null)
    {
        CertainInHand = certainInHand ?? EmptyCounts;
        ScheduledForHand = scheduledForHand ?? EmptyCounts;
    }

    private static readonly IReadOnlyDictionary<string, int> EmptyCounts =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Copies PROVEN to be in the opponent's hand right now: the engine showed the
    /// card publicly and then put it in that hand. These are facts, not estimates,
    /// so they are removed from the hidden pool and credited to the hand.
    /// </summary>
    public IReadOnlyDictionary<string, int> CertainInHand { get; }

    /// <summary>
    /// Copies public record says will enter the opponent's hand LATER, so they are
    /// not in it yet — a delayed "add to hand in N turns" clause is the intended
    /// case. They are removed from the hidden pool, because their destination is
    /// already fixed and they are not candidates for the hidden part of it, but they
    /// are NOT credited to the hand: crediting them would claim the opponent holds a
    /// card it does not hold yet, and the readings are about what can be played now.
    /// </summary>
    public IReadOnlyDictionary<string, int> ScheduledForHand { get; }
}
}
