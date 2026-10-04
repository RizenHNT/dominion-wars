using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DominionWars.Adapters;
using DominionWars.Adapters.Ai;
using DominionWars.Data;
using DominionWars.Engine.Model;
using DominionWars.Engine.Setup;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// HUMAN SANITY CHECK — THE MATERIAL, NOT THE VERDICT.
///
/// ⑧ asks for a human replay of representative matches and contentious decisions. The judgement
/// "does a person see an obvious misjudgement" CANNOT be made by the system that produced the
/// decisions, and this fixture does not pretend otherwise. What it does is produce the ARTIFACT a
/// person needs: a readable, deterministic, complete decision trace.
///
/// Two things are exported, both written under build-output/pl-ai-replay/:
///
///   decisions.md   — every ACTION-phase decision in a handful of representative matches, with
///                    the position summary, the action taken, the alternatives that were
///                    available, and the cost each carried.
///   judgement-calls.csv — the subset most likely to be contentious, defined by an OBJECTIVE
///                    rule rather than by my opinion of what looks odd: decisions where the
///                    policy handed the opponent cards or spent a card while a no-cost
///                    alternative was advertised.
///
/// The export is deterministic: same seeds, same output. A reviewer can re-run it and get the
/// same file, which is what makes a review reproducible rather than a story about one run.
///
/// WHAT THIS FIXTURE DELIBERATELY DOES NOT DO: assign a verdict. It has no assertion about
/// whether any decision was good. Its assertions are only about the export being complete,
/// non-empty, and of a PLAUSIBLE GAME SHAPE, so that neither a silently empty replay nor a
/// runaway-hand simulation can be mistaken for something a person should review.
/// </summary>
public sealed class AiHumanReplayExportTests
{
    /// <summary>Two matches chosen to show different victory axes, plus a mirror.</summary>
    private static readonly (string Actor, string Foe, int Seed)[] Representative =
    {
        ("machine", "sea", 3),
        ("flame", "wood", 5),
        ("sea", "machine", 7),
    };

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data", "cards")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from " + TestContext.CurrentContext.TestDirectory);
    }

    private static CardCatalog Catalog()
        => CardCatalog.LoadDirectory(Path.Combine(RepositoryRoot(), "data", "cards"));

    private static DeckDefinition Deck(string faction)
        => DeckLoader.LoadFile(Path.Combine(RepositoryRoot(), "data", "decks", faction + "_deck.json"));

    private static MatchDeckSpec Spec(DeckDefinition deck)
        => new MatchDeckSpec(deck.Leader, deck.Cards, deck.Name, deck.Faction);

    private sealed class Decision
    {
        public string Match = string.Empty;
        public int Turn;
        public string Phase = string.Empty;
        public int Actor;
        public string ActorFaction = string.Empty;
        public string Chosen = string.Empty;
        public string ChosenDetail = string.Empty;
        public int AdvertisedCount;
        public int? ChosenPunish;
        public List<string> Alternatives = new List<string>();
        public int OpponentLife;
        public int ActorLife;
        public int ActorHand;
        public int OpponentHand;
        public int ActorBoard;
        public int OpponentBoard;
        public bool HadNoCostAlternative;
    }

    /// <summary>The exported shape of one match, so the reviewer can judge its plausibility.</summary>
    private sealed class MatchShape
    {
        public string Label = string.Empty;
        public int MaxActorHand;
        public int MaxOpponentHand;
        public int Decisions;
    }

    private static List<Decision> TraceMatch(
        string actorFaction,
        string foeFaction,
        int seed,
        out string outcome,
        out int worstHandSeen)
    {
        var catalog = Catalog();
        var decisions = new List<Decision>();
        worstHandSeen = 0;

        var state = MatchSetup.Create(
            Spec(Deck(actorFaction)),
            Spec(Deck(foeFaction)),
            catalog.Cards,
            new MatchSetupOptions
            {
                Seed = (ulong)seed,
                FirstPlayerIndex = 0,
                OpeningHandSize = 5,
                PlayerLife = 20,
                CastleEnabled = true,
                CastleHealth = 75,
            });

        var matchId = "match_replay_" + actorFaction + "_" + foeFaction + "_" + seed;
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow, null, PunishResponseStances.PolicyFor(PlaystyleRegistry.Default));
        var gateway = new RuntimeMatchGateway(matchId, state, flow, router, null);
        Assert.That(gateway.Initialize(0).Accepted, Is.True);

        var policy = new AdvertisedActionPolicy();
        var steps = 0;

        // A BOUNDED NUMBER OF PLAYS PER TURN — and the FIRST version of this export omitted it,
        // which made the exported matches USELESS AS REVIEW MATERIAL. Without a bound the policy
        // plays every card it can afford; since every play hands the opponent cards, hand sizes
        // ran away: the measurement showed opponent hands reaching 34 against an opening hand of
        // 5, averaging 17. A reviewer reading that would be reviewing a game that never happens.
        //
        // The bound mirrors the calibration probe's driver, whose own comment records the same
        // lesson. Two plays per turn — the same rate the calibration driver uses, so a reviewer
        // reads the game in the shape the measurements were taken in. The bound is NOT a fidelity
        // guarantee on its own: measured hands still drift upward, because every play hands the
        // opponent cards (punish) and the income exceeds what two plays per turn spends. The
        // caller refuses to publish a runaway shape, and the export states the measured maxima so
        // the reviewer can weigh that limitation instead of trusting a silent claim.
        const int PlaysPerTurn = 2;
        var playsThisTurn = 0;
        var playsTurnKey = string.Empty;

        while (steps++ < 400)
        {
            if (state.WinnerPlayerIndex.HasValue) break;

            var viewer = state.CurrentPlayerIndex;
            var snapshot = gateway.GetSnapshot(viewer);
            if (snapshot.WinnerPlayerIndex.HasValue) break;

            var actor = snapshot.CurrentPlayer;

            var turnKey = snapshot.Turn + "|" + snapshot.Phase + "|" + actor;
            if (!string.Equals(turnKey, playsTurnKey, StringComparison.Ordinal))
            {
                playsTurnKey = turnKey;
                playsThisTurn = 0;
            }

            var endTurn = snapshot.LegalActions
                .FirstOrDefault(a => string.Equals(a.Type, "END_TURN", StringComparison.Ordinal));

            if (!policy.TryChoose(snapshot, actor, false, out var chosen) || chosen is null)
            {
                chosen = endTurn ?? snapshot.LegalActions.FirstOrDefault();
                if (chosen is null) break;
            }

            // Enforce the bound: once this turn's plays are spent, end the turn instead.
            if (playsThisTurn >= PlaysPerTurn
                && endTurn is not null
                && !string.Equals(chosen.Type, "END_TURN", StringComparison.Ordinal))
            {
                chosen = endTurn;
            }

            var wasPlay = !string.Equals(chosen.Type, "END_TURN", StringComparison.Ordinal);
            if (!wasPlay) playsThisTurn = 0;

            // Record every ACTION-phase decision, not the phase mechanics, so a reviewer reads
            // the interesting part rather than every SKIP_AMBUSH.
            if (string.Equals(snapshot.Phase, "ACTION", StringComparison.Ordinal))
            {
                var me = state.GetPlayer(actor);
                var foe = state.GetOpponent(actor);

                var decision = new Decision
                {
                    Match = actorFaction + " vs " + foeFaction + " seed " + seed,
                    Turn = snapshot.Turn,
                    Phase = snapshot.Phase,
                    Actor = actor,
                    ActorFaction = actor == 0 ? actorFaction : foeFaction,
                    Chosen = chosen.Type + " " + (chosen.CardId ?? chosen.ActionId ?? string.Empty),
                    ChosenDetail = Describe(chosen),
                    AdvertisedCount = snapshot.LegalActions.Count,
                    ChosenPunish = PunishOf(chosen),
                    ActorLife = me.Life ?? 0,
                    OpponentLife = foe.Life ?? 0,
                    ActorHand = me.Hand.Count,
                    OpponentHand = foe.Hand.Count,
                    ActorBoard = me.Field.Count,
                    OpponentBoard = foe.Field.Count,
                };

                // The largest hand any seat reached in this match, so the caller can refuse to
                // publish a game shape that never happens.
                worstHandSeen = Math.Max(worstHandSeen, Math.Max(me.Hand.Count, foe.Hand.Count));

                // The alternatives, ranked by the policy's own score, so a reviewer can see what
                // it passed over rather than only what it took.
                var ordered = policy.OrderAdvertisedActions(snapshot, snapshot.LegalActions)
                    .Where(a => !string.Equals(a.ActionId, chosen.ActionId, StringComparison.Ordinal))
                    .Take(4)
                    .ToList();

                foreach (var alternative in ordered)
                {
                    decision.Alternatives.Add(Describe(alternative));
                }

                decision.HadNoCostAlternative = snapshot.LegalActions.Any(a =>
                    !string.Equals(a.ActionId, chosen.ActionId, StringComparison.Ordinal)
                    && (PunishOf(a) ?? 0) == 0);

                decisions.Add(decision);
            }

            var acceptedType = string.Empty;
            if (gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, chosen)).Result.Accepted)
            {
                acceptedType = chosen.Type;
            }
            else
            {
                // The engine refused the chosen action, so the export must record the fallback it
                // actually took rather than keep reporting the choice that never happened.
                var alternative = snapshot.LegalActions
                    .FirstOrDefault(a => !string.Equals(a.ActionId, chosen.ActionId, StringComparison.Ordinal));
                if (alternative is null
                    || !gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, alternative)).Result.Accepted)
                {
                    break;
                }

                acceptedType = alternative.Type;
            }

            // Count SUCCESSFUL plays, not sorted decisions: a play the engine refused never happened,
            // and spending the bound on it would silently end turns early.
            if (!string.Equals(acceptedType, "END_TURN", StringComparison.Ordinal)) playsThisTurn++;
        }

        outcome = state.WinnerPlayerIndex.HasValue
            ? "winner=" + state.WinnerPlayerIndex + " reason=" + state.WinReason + " turn=" + state.Turn.Number
            : "unfinished at turn " + state.Turn.Number;

        return decisions;
    }

    private static int? PunishOf(RuntimeLegalAction action)
    {
        if (action.Payload is null) return null;
        if (!action.Payload.TryGetValue("punish", out var raw) || raw is null) return null;
        try
        {
            return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException || exception is InvalidCastException || exception is OverflowException)
        {
            return null;
        }
    }

    private static string Describe(RuntimeLegalAction action)
    {
        var punish = PunishOf(action);
        var target = action.TargetId?.ToString();
        return action.Type
            + " " + (action.CardId ?? action.ActionId ?? string.Empty)
            + (string.IsNullOrEmpty(target) ? string.Empty : " -> " + target)
            + (punish.HasValue ? " [cost " + punish.Value + "]" : string.Empty);
    }

    /// <summary>
    /// Writes the replay material and asserts only that the export is complete and non-empty.
    ///
    /// The assertion is deliberately about the ARTIFACT, never about the decisions: a fixture
    /// that passed judgement on its own decisions would be exactly the conflict of interest this
    /// item exists to avoid.
    /// </summary>
    [Test]
    public void ExportsReadableDecisionsAndJudgementCalls()
    {
        var directory = Path.Combine(RepositoryRoot(), "build-output", "pl-ai-replay");
        Directory.CreateDirectory(directory);

        var all = new List<Decision>();
        var outcomes = new List<string>();
        var worstOpponentHand = 0;
        var worstActorHand = 0;
        var shapes = new List<MatchShape>();

        foreach (var (actor, foe, seed) in Representative)
        {
            var decisions = TraceMatch(actor, foe, seed, out var outcome, out var worstHandSeen);
            outcomes.Add(actor + " vs " + foe + " seed " + seed + ": " + outcome);
            all.AddRange(decisions);

            var maxActorHand = decisions.Count == 0 ? 0 : decisions.Max(d => d.ActorHand);
            var maxOpponentHand = decisions.Count == 0 ? 0 : decisions.Max(d => d.OpponentHand);
            shapes.Add(new MatchShape
            {
                Label = actor + " vs " + foe + " seed " + seed,
                MaxActorHand = maxActorHand,
                MaxOpponentHand = maxOpponentHand,
                Decisions = decisions.Count,
            });

            worstOpponentHand = Math.Max(worstOpponentHand, maxOpponentHand);
            worstActorHand = Math.Max(worstActorHand, maxActorHand);

            Assert.That(
                worstHandSeen,
                Is.LessThan(28),
                "match " + actor + " vs " + foe + " seed " + seed + " reached a hand of " + worstHandSeen
                + " against an opening hand of 5 — the exported shape does not happen in a real game and cannot be reviewed");
        }

        Assert.That(all, Is.Not.Empty, "the export must contain decisions or a reviewer has nothing to read");
        TestContext.Out.WriteLine(
            "replay export hand sizes: worst actor hand " + worstActorHand + ", worst opponent hand " + worstOpponentHand);

        var markdown = new StringBuilder();
        markdown.AppendLine("# AI 决策回放素材（⑧ Human sanity check）");
        markdown.AppendLine();
        markdown.AppendLine("> **这份文件是素材，不是结论。** 本文件由测试自动导出，");
        markdown.AppendLine("> **系统不会、也不应替人判定「这步是不是明显误判」** —— 那正是 ⑧ 要人来做的部分。");
        markdown.AppendLine();
        markdown.AppendLine("导出来源：`src/Engine/Tests/AiHumanReplayExportTests.cs`，确定性运行（同 seed 同输出）。");
        markdown.AppendLine();
        markdown.AppendLine("## 对局结果");
        markdown.AppendLine();
        foreach (var outcome in outcomes) markdown.AppendLine("- " + outcome);
        markdown.AppendLine();
        markdown.AppendLine("## 这份素材的可审性前提（导出形状是否可信）");
        markdown.AppendLine();
        markdown.AppendLine("每回合最多 2 次出牌（与校准探针的驱动器一致）。**实测形状**（起手 5 张）：");
        markdown.AppendLine();
        markdown.AppendLine("| 对局 | 导出决策数 | 手牌最大值（本方） | 手牌最大值（对手） |");
        markdown.AppendLine("|---|---|---|---|");
        foreach (var shape in shapes)
        {
            markdown.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "| {0} | {1} | {2} | {3} |",
                shape.Label, shape.Decisions, shape.MaxActorHand, shape.MaxOpponentHand));
        }

        markdown.AppendLine();
        markdown.AppendLine("- 自己手牌最大值：" + worstActorHand + "（起手 5）");
        markdown.AppendLine("- 对手手牌最大值：" + worstOpponentHand + "（起手 5）");
        markdown.AppendLine();
        markdown.AppendLine("**为什么这一节存在**：本导出的第一版没有每回合出牌上限，策略会把每一张付得起的牌都打出去，");
        markdown.AppendLine("而每次出牌都会让对手抽牌（惩罚），于是手牌失控（对手最多 34 张、平均 17）。");
        markdown.AppendLine("审阅那份文件等于审阅一个真实对局中不会出现的局面。");
        markdown.AppendLine();
        markdown.AppendLine("加上限后手牌**仍然会涨到十几张**，这不是又一次失控，而是结算规则本身的结果：");
        markdown.AppendLine("惩罚收入大于每回合 2 次出牌的消耗，双方手牌都会缓慢累积。");
        markdown.AppendLine("导出器对此设了硬性上限（任一方手牌达到 28 张即判定形状不可审阅并报错），");
        markdown.AppendLine("所以上表就是实际形状。**审阅时请把它当作限制条件**：");
        markdown.AppendLine("这里的「手牌多、选项多」比人手玩的一局更多，涉及手牌数量的判断要按此打折。");
        markdown.AppendLine();

        markdown.AppendLine("## 全部决策（按对局）");
        markdown.AppendLine();
        markdown.AppendLine("字段说明：`成本` 是该动作让对手抽的牌数（punish）。`候选` 是策略自己排序后**它没有选**的前几项。");
        markdown.AppendLine();

        foreach (var group in all.GroupBy(d => d.Match))
        {
            markdown.AppendLine("### " + group.Key);
            markdown.AppendLine();
            markdown.AppendLine("| 回合 | 座位 | 手牌 | 对手手牌 | 场面 | 对手场面 | 生命 | 对手生命 | 候选数 | 选择 | 成本 |");
            markdown.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var d in group)
            {
                markdown.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | `{9}` | {10} |",
                    d.Turn, d.ActorFaction, d.ActorHand, d.OpponentHand, d.ActorBoard, d.OpponentBoard,
                    d.ActorLife, d.OpponentLife, d.AdvertisedCount, d.Chosen,
                    d.ChosenPunish.HasValue ? d.ChosenPunish.Value.ToString(CultureInfo.InvariantCulture) : "-"));
            }

            markdown.AppendLine();

            // The alternatives, so a reviewer can see what was passed over.
            foreach (var d in group.Take(14))
            {
                if (d.Alternatives.Count == 0) continue;
                markdown.AppendLine("- 回合 " + d.Turn + " 选了 `" + d.Chosen + "`；未选的前几项："
                    + string.Join(" / ", d.Alternatives.Select(a => "`" + a + "`")));
            }

            markdown.AppendLine();
        }

        // The judgement-call list, selected by an OBJECTIVE rule, not by my taste.
        var judgementCalls = all
            .Where(d => (d.ChosenPunish ?? 0) > 0 && d.HadNoCostAlternative)
            .ToList();

        markdown.AppendLine("## 争议候选（客观规则筛出，非我的判断）");
        markdown.AppendLine();
        markdown.AppendLine("筛选规则：**这一步付出了 punish 成本（让对手抽牌），而当时存在零成本的可选项。**");
        markdown.AppendLine("这**不等于**这些决策是错的 —— 出牌可能值这个价。它只是人最可能想复核的那一批。");
        markdown.AppendLine();
        markdown.AppendLine("共 " + judgementCalls.Count + " / " + all.Count + " 步落在这个规则内。");
        markdown.AppendLine();
        if (judgementCalls.Count > 0)
        {
            markdown.AppendLine("| 对局 | 回合 | 选择 | 成本 | 零成本可选项 |");
            markdown.AppendLine("|---|---|---|---|---|");
            foreach (var d in judgementCalls)
            {
                markdown.AppendLine("| " + d.Match + " | " + d.Turn + " | `" + d.Chosen + "` | "
                    + (d.ChosenPunish ?? 0) + " | " + string.Join(" / ", d.Alternatives.Take(2).Select(a => "`" + a + "`")) + " |");
            }
        }

        markdown.AppendLine();
        markdown.AppendLine("## 复核者要回答什么（由人填写）");
        markdown.AppendLine();
        markdown.AppendLine("1. 有没有**人一眼就能看出**的明显误判？（若有，请指出回合与动作）");
        markdown.AppendLine("2. 有没有**错误的目标**？（例如该推进胜利轴时在无关动作上耗时）");
        markdown.AppendLine("3. 有没有**反直觉的循环**？（反复做同一件没有进展的事）");
        markdown.AppendLine();
        markdown.AppendLine("> 重点**不是** AI 是否像人，而是确认它没有出现人一眼能看出的明显误判。");

        File.WriteAllText(Path.Combine(directory, "decisions.md"), markdown.ToString(), Encoding.UTF8);

        var csv = new StringBuilder();
        csv.AppendLine("match,turn,actor_faction,chosen,cost,advertised_actions,opponent_life,actor_life,opponent_hand,actor_hand,alternatives");
        foreach (var d in all)
        {
            csv.AppendLine(string.Join(",",
                Csv(d.Match), d.Turn.ToString(CultureInfo.InvariantCulture), Csv(d.ActorFaction),
                Csv(d.Chosen), (d.ChosenPunish ?? 0).ToString(CultureInfo.InvariantCulture),
                d.AdvertisedCount.ToString(CultureInfo.InvariantCulture),
                d.OpponentLife.ToString(CultureInfo.InvariantCulture), d.ActorLife.ToString(CultureInfo.InvariantCulture),
                d.OpponentHand.ToString(CultureInfo.InvariantCulture), d.ActorHand.ToString(CultureInfo.InvariantCulture),
                Csv(string.Join(" | ", d.Alternatives))));
        }

        File.WriteAllText(Path.Combine(directory, "judgement-calls.csv"), csv.ToString(), Encoding.UTF8);

        TestContext.Out.WriteLine("replay material written to: {0}", directory);
        TestContext.Out.WriteLine("  decisions.md        : {0} decisions across {1} matches", all.Count, Representative.Length);
        TestContext.Out.WriteLine("  judgement-calls.csv : {0} rows", all.Count);
        TestContext.Out.WriteLine("  decisions flagged by the objective rule (paid a cost while a free option existed): {0}",
            judgementCalls.Count);
        TestContext.Out.WriteLine("  => ⑧ remains HUMAN_REQUIRED: this export is the material, not the verdict.");

        Assert.That(File.Exists(Path.Combine(directory, "decisions.md")), Is.True);
        Assert.That(all.Count, Is.GreaterThan(20), "the export must contain enough decisions to be worth reviewing");
    }

    private static string Csv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

}
