using System;
using System.Collections.Generic;
using DominionWars.Engine.Turns;

namespace DominionWars.Adapters.Ai
{

/// <summary>
/// The shipped playstyles and the bracket presets, by id.
///
/// A style entry is nothing but a weight table over the published feature names
/// (see <see cref="ActionFeatures"/>); the decision code is shared, so two AIs
/// can only differ in what they value. That is the point of the exercise: a
/// balance reading needs a range produced by several AIs that make different
/// mistakes, not one AI that is strong.
///
/// <see cref="DefaultId"/> is the retired hardcoded priority expressed as
/// weights and is what <see cref="AdvertisedActionPolicy"/> uses when no
/// playstyle is supplied, so the already-measured behaviour is preserved
/// exactly. The other style ids are the diverse AIs.
///
/// The <c>bracket-*</c> ids (docs/PL_AI_PLAYSTYLE_FRAMEWORK_2026-09-11.md §4.1)
/// are NOT styles. Every style here is a weight vector chosen by one author, so
/// the whole matrix could share that author's blind spot and agree on a wrong
/// answer while looking robust. The brackets bound the reading instead, and a
/// conclusion is only usable if it survives both ends:
/// <list type="bullet">
/// <item><see cref="BracketDeclineId"/>: decline every optional punish
/// arrival/response (lower bound), through the
/// <c>IPunishResponsePolicy</c> seam;</item>
/// <item><see cref="BracketAcceptId"/>: accept every optional punish
/// arrival/response (upper bound), same seam;</item>
/// <item><see cref="BracketFirstId"/>: the retired ACTION-phase priority
/// through the weight vector plus the engine's own response policy — i.e.
/// exactly today's shipped AI, which makes it the regression anchor for "we did
/// not change the existing opponent".</item>
/// </list>
///
/// A weight table is validated when it is built: an unknown feature name is a
/// construction error, never a silently ignored weight.
/// </summary>
public static class PlaystyleRegistry
{
    /// <summary>The shipped default policy's own priority, as one weight vector.</summary>
    public const string DefaultId = "default";

    /// <summary>Damage and the enemy core first; cheap punish; little interest in the board.</summary>
    public const string AggroId = "aggro";

    /// <summary>Board exchanges and survival first; punish-averse; little interest in the enemy core.</summary>
    public const string ControlId = "control";

    /// <summary>The lifecycle chain first (commit, then download); everything else is filler.</summary>
    public const string ComboId = "combo";

    /// <summary>Hand and punish budget first; refuses to feed the opponent's draws.</summary>
    public const string EconomyId = "economy";

    /// <summary>This turn's output first; deliberately discounts the long download axis.</summary>
    public const string TempoId = "tempo";

    /// <summary>Upper bracket: accept every optional punish arrival/response.</summary>
    public const string BracketAcceptId = "bracket-accept";

    /// <summary>Lower bracket: decline every optional punish arrival/response.</summary>
    public const string BracketDeclineId = "bracket-decline";

    /// <summary>Historical anchor: the retired hardcoded priority, exactly today's shipped opponent.</summary>
    public const string BracketFirstId = "bracket-first";

    /// <summary>
    /// The shipped default policy's priority as weights: a resolvable download,
    /// then a card play, then an unresolvable download, then a commit, then an
    /// attack, then ending the turn, with everything else last. Weighted score
    /// order here is exactly the retired rank order, and the features the
    /// default prices at zero are the ones it was blind to (punish, the
    /// punish-converted discard, hand spend, face versus trade, ambush).
    /// Kept as a table so the three brackets can reuse it verbatim.
    /// </summary>
    private static readonly (string Feature, int Weight)[] ShippedDefaultTable =
    {
        (ActionFeatures.Lifecycle, 300),
        (ActionFeatures.WinAxis, 700),
        (ActionFeatures.ChainReverse, -300),
        (ActionFeatures.Board, 500),
        (ActionFeatures.Ambush, 0),
        (ActionFeatures.SpendCard, 0),
        (ActionFeatures.SpendField, -60),
        (ActionFeatures.Damage, 120),
        (ActionFeatures.Face, 0),
        (ActionFeatures.Trade, 0),
        (ActionFeatures.Punish, 0),
        (ActionFeatures.DiscardCost, 0),
        (ActionFeatures.EndTurn, 50),
    };

    /// <summary>
    /// The shipped default, i.e. today's production opponent: the retired ACTION
    /// priority with the response stance production actually has. Nothing in
    /// <c>src\Adapters</c> injects a response policy, so every match built by
    /// <c>MatchFactory</c> declines every offered response, which is exactly
    /// <see cref="PunishResponseStance.Never"/> — the stance is explicit here so
    /// the reference point is written down instead of inferred from a missing
    /// injection (docs/PL_AI_PLAYSTYLE_FRAMEWORK_2026-09-11.md §3.5).
    /// </summary>
    private static readonly WeightedPlaystyle ShippedDefault = Create(
        DefaultId,
        PunishResponseStance.Never,
        ShippedDefaultTable);

    /// <summary>
    /// 速攻: pressure the enemy core now. Damage and face attacks dominate,
    /// punish barely matters (it will pay to close the game), board development
    /// and board exchanges are cheap, and it ends the turn only when nothing
    /// else is advertised. Response stance <see cref="PunishResponseStance.FirstInRound"/>:
    /// it will spend one response for tempo, but not feed a chain.
    /// </summary>
    private static readonly WeightedPlaystyle Aggro = Create(
        AggroId,
        PunishResponseStance.FirstInRound,
        (ActionFeatures.Lifecycle, 100),
        (ActionFeatures.WinAxis, 300),
        (ActionFeatures.ChainReverse, -300),
        (ActionFeatures.Board, 100),
        (ActionFeatures.Ambush, 0),
        (ActionFeatures.SpendCard, 0),
        (ActionFeatures.SpendField, -60),
        (ActionFeatures.Damage, 400),
        (ActionFeatures.Face, 500),
        (ActionFeatures.Trade, 50),
        (ActionFeatures.Punish, -1),
        (ActionFeatures.DiscardCost, -20),
        (ActionFeatures.EndTurn, 20));

    /// <summary>
    /// 控制: win the board first and keep the carriers alive. Board exchanges
    /// and development dominate, face damage is worthless, and the punish and
    /// commit costs are priced highly because giving the opponent cards is how
    /// control loses. It almost never ends the turn while it still has a cheap
    /// action left. Response stance <see cref="PunishResponseStance.Never"/>:
    /// activating a response hands the opponent the punish draw it was paid for,
    /// which is exactly what a slow board-first plan cannot afford.
    /// </summary>
    private static readonly WeightedPlaystyle Control = Create(
        ControlId,
        PunishResponseStance.Never,
        (ActionFeatures.Lifecycle, 150),
        (ActionFeatures.WinAxis, 500),
        (ActionFeatures.ChainReverse, -300),
        (ActionFeatures.Board, 400),
        (ActionFeatures.Ambush, 150),
        (ActionFeatures.SpendCard, 0),
        (ActionFeatures.SpendField, -250),
        (ActionFeatures.Damage, 200),
        (ActionFeatures.Face, 0),
        (ActionFeatures.Trade, 600),
        (ActionFeatures.Punish, -6),
        (ActionFeatures.DiscardCost, -40),
        (ActionFeatures.EndTurn, -100));

    /// <summary>
    /// 组合: serve one win axis. The download axis and the lifecycle chain
    /// dominate, a commit is a welcome setup cost, the board and damage are
    /// filler, and a rollback is heavily negative because it reverses the chain
    /// this playstyle exists to build.
    ///
    /// Response stance <see cref="PunishResponseStance.Always"/> — my judgement,
    /// with the evidence and the cost stated. The mechanical leader's axis is
    /// throughput (commit, upload, download), and a punish response is the only
    /// optional card flow in the game: accepting it resolves the responding
    /// card's effects immediately. Measured on the shipped machine-vs-wood pair
    /// at seed 11 with both sides on the retired ACTION priority, accept-everything
    /// reached the six-download win in 5 turns / 36 submissions against 7 turns /
    /// 72 submissions for decline-everything, so the axis this playstyle serves
    /// is reached sooner under Always. The cost is real and accepted: every
    /// activated response also lengthens the punish chain and hands the opponent
    /// cards, which is precisely why control and economy take Never instead.
    /// </summary>
    private static readonly WeightedPlaystyle Combo = Create(
        ComboId,
        PunishResponseStance.Always,
        (ActionFeatures.Lifecycle, 500),
        (ActionFeatures.WinAxis, 1000),
        (ActionFeatures.ChainReverse, -800),
        (ActionFeatures.Board, 100),
        (ActionFeatures.Ambush, 0),
        (ActionFeatures.SpendCard, 0),
        (ActionFeatures.SpendField, -10),
        (ActionFeatures.Damage, 50),
        (ActionFeatures.Face, 0),
        (ActionFeatures.Trade, 0),
        (ActionFeatures.Punish, -5),
        (ActionFeatures.DiscardCost, -10),
        (ActionFeatures.EndTurn, -50));

    /// <summary>
    /// 经济: protect the hand and the punish budget. The punish amount has its
    /// largest negative weight here, spending a hand card or the punish
    /// discount's cards is a real cost, and it will end the turn rather than
    /// pay a punish it cannot afford. Response stance
    /// <see cref="PunishResponseStance.Never"/> for the same reason its punish
    /// weight is the largest: it is the playstyle that refuses to feed the
    /// flood.
    /// </summary>
    private static readonly WeightedPlaystyle Economy = Create(
        EconomyId,
        PunishResponseStance.Never,
        (ActionFeatures.Lifecycle, 150),
        (ActionFeatures.WinAxis, 400),
        (ActionFeatures.ChainReverse, -300),
        (ActionFeatures.Board, 200),
        (ActionFeatures.Ambush, 100),
        (ActionFeatures.SpendCard, -100),
        (ActionFeatures.SpendField, -200),
        (ActionFeatures.Damage, 150),
        (ActionFeatures.Face, 0),
        (ActionFeatures.Trade, 100),
        (ActionFeatures.Punish, -12),
        (ActionFeatures.DiscardCost, -100),
        (ActionFeatures.EndTurn, 40));

    /// <summary>
    /// 节奏: maximise what happens this turn. Board development and damage come
    /// first, the long download axis is deliberately discounted (this is the
    /// playstyle that plays a card instead of completing a download it could
    /// have completed), and the turn is never ended early. Response stance
    /// <see cref="PunishResponseStance.FirstInRound"/>: one reaction for tempo,
    /// not an open-ended chain.
    /// </summary>
    private static readonly WeightedPlaystyle Tempo = Create(
        TempoId,
        PunishResponseStance.FirstInRound,
        (ActionFeatures.Lifecycle, 100),
        (ActionFeatures.WinAxis, 100),
        (ActionFeatures.ChainReverse, -200),
        (ActionFeatures.Board, 300),
        (ActionFeatures.Ambush, 0),
        (ActionFeatures.SpendCard, 0),
        (ActionFeatures.SpendField, -30),
        (ActionFeatures.Damage, 180),
        (ActionFeatures.Face, 100),
        (ActionFeatures.Trade, 0),
        (ActionFeatures.Punish, -3),
        (ActionFeatures.DiscardCost, -20),
        (ActionFeatures.EndTurn, -50));

    private static readonly IReadOnlyList<IPlaystyle> AllList = Array.AsReadOnly(new IPlaystyle[]
    {
        ShippedDefault,
        Aggro,
        Control,
        Combo,
        Economy,
        Tempo,
    });

    private static readonly IReadOnlyList<string> IdList = Array.AsReadOnly(new[]
    {
        DefaultId,
        AggroId,
        ControlId,
        ComboId,
        EconomyId,
        TempoId,
    });

    /// <summary>
    /// The bracket ids, in the order a matrix should report them: the upper
    /// bound, the lower bound, then the historical anchor.
    /// </summary>
    private static readonly IReadOnlyList<string> BracketIdList = Array.AsReadOnly(new[]
    {
        BracketAcceptId,
        BracketDeclineId,
        BracketFirstId,
    });

    /// <summary>
    /// The three brackets as playstyles. Each carries the shipped default ACTION
    /// priority verbatim — the bracket is about the response seam and about being
    /// a boundary, not about a different way of playing — and differs only in its
    /// response stance and its id:
    /// <list type="bullet">
    /// <item><c>bracket-accept</c>: <see cref="PunishResponseStance.Always"/>, the
    /// upper bound;</item>
    /// <item><c>bracket-decline</c>: <see cref="PunishResponseStance.Never"/>, the
    /// lower bound;</item>
    /// <item><c>bracket-first</c>: <see cref="PunishResponseStance.Never"/> plus the
    /// retired priority, which is exactly today's shipped opponent (nothing is
    /// injected in production, which declines), so it doubles as the regression
    /// anchor for "the existing AI was not changed". Because production declines,
    /// this bracket and <c>bracket-decline</c> produce identical numbers today;
    /// they are different labels for different questions and diverge only if the
    /// shipped runtime ever starts accepting responses.</item>
    /// </list>
    /// </summary>
    private static readonly WeightedPlaystyle BracketAccept = Create(
        BracketAcceptId,
        PunishResponseStance.Always,
        ShippedDefaultTable);

    private static readonly WeightedPlaystyle BracketDecline = Create(
        BracketDeclineId,
        PunishResponseStance.Never,
        ShippedDefaultTable);

    private static readonly WeightedPlaystyle BracketFirst = Create(
        BracketFirstId,
        PunishResponseStance.Never,
        ShippedDefaultTable);

    private static readonly IReadOnlyList<IPlaystyle> BracketList = Array.AsReadOnly(new IPlaystyle[]
    {
        BracketAccept,
        BracketDecline,
        BracketFirst,
    });

    private static readonly IReadOnlyList<IPlaystyle> SelectableList = Array.AsReadOnly(new IPlaystyle[]
    {
        ShippedDefault,
        Aggro,
        Control,
        Combo,
        Economy,
        Tempo,
        BracketAccept,
        BracketDecline,
        BracketFirst,
    });

    private static readonly IReadOnlyList<string> SelectableIdList = BuildSelectableIds();

    private static readonly Dictionary<string, IPlaystyle> SelectableIndex = BuildSelectableIndex();

    /// <summary>The shipped default playstyle: the retired hardcoded priority plus production's response stance.</summary>
    public static IPlaystyle Default => ShippedDefault;

    /// <summary>Every shipped style playstyle, default first.</summary>
    public static IReadOnlyList<IPlaystyle> All => AllList;

    /// <summary>Every shipped style playstyle id, in <see cref="All"/> order.</summary>
    public static IReadOnlyList<string> Ids => IdList;

    /// <summary>The three bracket playstyles, in <see cref="BracketIds"/> order.</summary>
    public static IReadOnlyList<IPlaystyle> Brackets => BracketList;

    /// <summary>
    /// Every selectable playstyle: the six style playstyles followed by the three
    /// brackets. A style entry prices features and declares a stance; a bracket
    /// entry reuses the shipped default weights and pins the response stance,
    /// which is the whole point of a bracket.
    /// </summary>
    public static IReadOnlyList<IPlaystyle> Selectable => SelectableList;

    /// <summary>
    /// Every selectable playstyle id (styles and brackets). This is what a harness
    /// <c>--playstyle &lt;id&gt;</c> flag should accept, and what a report must
    /// stamp.
    /// </summary>
    public static IReadOnlyList<string> SelectableIds => SelectableIdList;

    /// <summary>The three bracket ids: <c>bracket-accept</c>, <c>bracket-decline</c>, <c>bracket-first</c>.</summary>
    public static IReadOnlyList<string> BracketIds => BracketIdList;

    /// <summary>True when the id names a bracket rather than a style playstyle.</summary>
    public static bool IsBracket(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        foreach (var bracket in BracketList)
        {
            if (string.Equals(bracket.Id, id, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>
    /// Looks up any selectable playstyle by id, brackets included, and throws on
    /// an unknown id, so a mistyped id in a report or a harness flag cannot
    /// silently fall back to a style playstyle and pollute a matrix.
    /// </summary>
    public static IPlaystyle GetPlaystyle(string id)
    {
        if (id is null) throw new ArgumentNullException(nameof(id));
        if (!SelectableIndex.TryGetValue(id, out var found))
        {
            throw new ArgumentException(
                "Unknown playstyle id '" + id + "'; shipped ids are: " + string.Join(", ", SelectableIdList) + ".",
                nameof(id));
        }

        return found;
    }

    /// <summary>Non-throwing form of <see cref="GetPlaystyle"/>.</summary>
    public static bool TryGetPlaystyle(string? id, out IPlaystyle playstyle)
    {
        playstyle = Default;
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (!SelectableIndex.TryGetValue(id, out var found)) return false;
        playstyle = found;
        return true;
    }

    /// <summary>
    /// The engine policy that implements a playstyle's response stance, ready to
    /// hand to <c>TurnActionRouter.CreateDefault(flow, punishResponses: …)</c>.
    /// This is the adapter-side half of the wiring; the production half (which
    /// call site injects it) is deliberately not changed here
    /// (docs/PL_AI_PLAYSTYLE_FRAMEWORK_2026-09-11.md §3.5).
    /// </summary>
    public static IPunishResponsePolicy PunishResponsesFor(string id)
        => PunishResponseStances.PolicyFor(GetPlaystyle(id));

    private static WeightedPlaystyle Create(
        string id,
        PunishResponseStance responseStance,
        params (string Feature, int Weight)[] weights)
    {
        var table = new Dictionary<string, int>(weights.Length, StringComparer.Ordinal);
        foreach (var weight in weights)
        {
            if (table.ContainsKey(weight.Feature))
            {
                throw new InvalidOperationException(
                    "Playstyle '" + id + "' declares the feature '" + weight.Feature + "' twice.");
            }

            table[weight.Feature] = weight.Weight;
        }

        return new WeightedPlaystyle(id, responseStance, table);
    }

    private static IReadOnlyList<string> BuildSelectableIds()
    {
        var ids = new string[SelectableList.Count];
        for (var position = 0; position < SelectableList.Count; position++)
        {
            ids[position] = SelectableList[position].Id;
        }

        return Array.AsReadOnly(ids);
    }

    private static Dictionary<string, IPlaystyle> BuildSelectableIndex()
    {
        var index = new Dictionary<string, IPlaystyle>(SelectableList.Count, StringComparer.Ordinal);
        foreach (var playstyle in SelectableList)
        {
            index[playstyle.Id] = playstyle;
        }

        return index;
    }
}

}
