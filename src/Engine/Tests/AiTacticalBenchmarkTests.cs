using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// TACTICAL COMPETENCE BENCHMARK.
///
/// A balance-testing instrument does not have to play well; it has to avoid being
/// WRONG in ways that corrupt the measurement. So these tests do not assert a single
/// best move — most positions have several defensible plays. They assert that the
/// policy's choice falls inside an ACCEPTABLE SET, derived from the rules rather than
/// from what the policy happens to do.
///
/// The distinction matters: a test that asserts "the AI plays X" can only ever report
/// the AI's current habit, and would have to be rewritten every time a weight changes.
/// A test that asserts "the AI does not do Y, which the rules make clearly wrong" is a
/// claim about competence that survives tuning — which is exactly what an instrument
/// needs.
///
/// What is checked here:
///   - hard blunders: passing while a legal, useful action exists; throwing away a
///     guaranteed win; ignoring a lethal threat it can answer
///   - win-axis advance: taking an advertised victory step when one is offered
///   - punish risk: not treating a bigger own punish as free
///   - resource retention: not spending when spending achieves nothing
///   - public information: acting on what the snapshot publishes rather than guessing
/// </summary>
public sealed class AiTacticalBenchmarkTests
{
    private const int Ai = 0;
    private const int Foe = 1;

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

    // ------------------------------------------------------------------ rig

    private sealed class Rig
    {
        public GameState State { get; init; } = null!;
        public TurnFlow Flow { get; init; } = null!;
        public RuntimeMatchGateway Gateway { get; init; } = null!;
        public AdvertisedActionPolicy Policy { get; init; } = null!;
        public CardCatalog Catalog { get; init; } = null!;

        /// <summary>The viewer-safe snapshot for the side about to act.</summary>
        public RuntimeSnapshotEnvelope Snapshot() => Gateway.GetSnapshot(State.CurrentPlayerIndex);

        /// <summary>
        /// The policy's choice right now, with the reason it was reachable recorded so a
        /// failure names the candidate set instead of only the winner.
        /// </summary>
        public RuntimeLegalAction Choose()
        {
            var snapshot = Snapshot();
            if (!Policy.TryChoose(snapshot, snapshot.CurrentPlayer, false, out var chosen) || chosen is null)
            {
                Assert.Fail(
                    "the policy returned no action in phase " + snapshot.Phase
                    + " with " + snapshot.LegalActions.Count + " advertised actions; that is a hard stall");
            }

            return chosen!;
        }

        public IReadOnlyList<RuntimeLegalAction> Advertised()
            => Snapshot().LegalActions;

        public IEnumerable<string> AdvertisedSummary()
            => Advertised().Select(a => a.Type + "/" + (a.CardId ?? a.ActionId ?? ""));
    }

    /// <summary>
    /// Builds a deterministic match and advances to the first ACTION phase for
    /// <paramref name="actor"/>, using the REAL engine so every advertised action comes
    /// from the authoritative legality generator.
    /// </summary>
    private static Rig Build(
        int seed = 3,
        string actorFaction = "flame",
        string foeFaction = "machine",
        Func<Dictionary<string, CardDefinition>, Dictionary<string, CardDefinition>>? mutate = null)
    {
        var catalog = Catalog();
        var cards = new Dictionary<string, CardDefinition>(catalog.Cards, StringComparer.Ordinal);
        if (mutate is not null)
        {
            cards = mutate(cards);
        }

        var state = MatchSetup.Create(
            Spec(Deck(actorFaction)),
            Spec(Deck(foeFaction)),
            cards,
            new MatchSetupOptions
            {
                Seed = (ulong)seed,
                FirstPlayerIndex = 0,
                OpeningHandSize = 5,
                PlayerLife = 20,
                CastleEnabled = true,
                CastleHealth = 75,
            });

        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow, null, PunishResponseStances.PolicyFor(PlaystyleRegistry.Default));
        var gateway = new RuntimeMatchGateway("match_tactical_benchmark", state, flow, router, null);
        var init = gateway.Initialize(0);
        Assert.That(init.Accepted, Is.True, "match initialization failed: " + init.ReasonKey);

        return new Rig
        {
            State = state,
            Flow = flow,
            Gateway = gateway,
            Policy = new AdvertisedActionPolicy(),
            Catalog = catalog,
        };
    }

    /// <summary>Advances to the next ACTION phase where <paramref name="seat"/> is to act.</summary>
    private static Rig AdvanceToAction(Rig rig, int seat, int maxSteps = 400)
    {
        var steps = 0;
        while (steps++ < maxSteps)
        {
            var snapshot = rig.Gateway.GetSnapshot(seat);
            if (!snapshot.WinnerPlayerIndex.HasValue
                && snapshot.CurrentPlayer == seat
                && string.Equals(snapshot.Phase, "ACTION", StringComparison.Ordinal)
                && snapshot.LegalActions.Count > 0)
            {
                return rig;
            }

            if (snapshot.WinnerPlayerIndex.HasValue) break;

            var actor = snapshot.CurrentPlayer;
            var legal = rig.Gateway.GetSnapshot(actor).LegalActions;
            var pick = legal.FirstOrDefault(a => string.Equals(a.Type, "END_TURN", StringComparison.Ordinal))
                ?? legal.FirstOrDefault();
            if (pick is null) break;

            var submission = rig.Gateway.Submit(ToSubmittedAction(
                rig.Gateway.GetSnapshot(actor), pick));
            if (!submission.Result.Accepted) break;
        }

        return rig;
    }

    private static RuntimeGameAction ToSubmittedAction(
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction advertised)
    {
        var action = AdvertisedActionPolicy.ToGameAction(snapshot, advertised);
        if (!RuntimeActionSelection.TryBuildStableSelection(
                advertised,
                out var selectedEntityIds,
                out var reasonKey))
        {
            throw new AssertionException(
                advertised.ActionId + ": malformed selection advertisement: " + reasonKey);
        }

        // Self-discard PLAY_CARD keeps the legacy payload choice produced by
        // AdvertisedActionPolicy. Ordinary DISCARD uses the typed request-only
        // channel; neither path changes the engine advertisement.
        if (selectedEntityIds.Count > 0 &&
            !action.Payload.ContainsKey("selectedEntityIds"))
        {
            action.SelectedEntityIds = selectedEntityIds;
        }

        return action;
    }

    // ------------------------------------------------- hard blunder: no stalling

    /// <summary>
    /// The instrument must never simply FAIL TO ACT. A stall would silently drop a seat
    /// out of the sample, which corrupts every downstream aggregate — a deck would look
    /// weak because its pilot stopped playing.
    ///
    /// Checked across many seeds, both seats and all four matchups, because a stall that
    /// only happens in one pairing is exactly the kind of hole a single-seed run misses.
    /// </summary>
    [Test]
    public void PolicyNeverStallsAcrossMatchupsAndSeeds()
    {
        var factions = new[] { "flame", "machine", "sea", "wood" };
        var checkedPositions = 0;
        var failures = new List<string>();

        foreach (var actorFaction in factions)
        {
            foreach (var foeFaction in factions)
            {
                for (var seed = 1; seed <= 4; seed++)
                {
                    var rig = Build(seed, actorFaction, foeFaction);
                    var rigAtAction = AdvanceToAction(rig, Ai);
                    var snapshot = rigAtAction.Snapshot();

                    if (snapshot.WinnerPlayerIndex.HasValue) continue;
                    if (!string.Equals(snapshot.Phase, "ACTION", StringComparison.Ordinal)) continue;

                    checkedPositions++;
                    if (rigAtAction.Policy.TryChoose(snapshot, snapshot.CurrentPlayer, false, out var chosen)
                        && chosen is not null)
                    {
                        continue;
                    }

                    failures.Add(
                        actorFaction + " vs " + foeFaction + " seed " + seed
                        + ": no action chosen in phase " + snapshot.Phase
                        + " with legal=[" + string.Join(",", rigAtAction.AdvertisedSummary()) + "]");
                }
            }
        }

        TestContext.Out.WriteLine("ACTION positions probed: {0}", checkedPositions);
        Assert.That(checkedPositions, Is.GreaterThanOrEqualTo(40), "the sweep must be non-vacuous");
        Assert.That(
            failures,
            Is.Empty,
            "the policy stalled, which would drop a seat out of every sample (" + failures.Count + "):\n  "
            + string.Join("\n  ", failures.Take(6)));
    }

    // ------------------------------------------------- accepted action only

    /// <summary>
    /// Whatever the policy returns must be an action the ENGINE advertised, and — with the
    /// one gap named below excluded — the engine must accept it. An instrument that
    /// proposes moves the engine rejects measures a game nobody is playing.
    ///
    /// THE SELF-DISCARD GAP HAS BEEN CLOSED, and this test now proves the positive contract.
    ///
    /// WHAT WAS WRONG BEFORE (kept because the reasoning matters): when a player was under "punish
    /// converts to a self-discard", the engine advertised a PLAY_CARD carrying
    /// <c>discardRequired</c> + <c>discardCandidateIds</c>, and NO submission could satisfy it — the
    /// gateway reads a selection from <c>selectedEntityIds</c>, the boundary required the submitted
    /// payload to EQUAL the advertisement (so a submitter could not add that field), and the
    /// gateway's candidate-set bridge was restricted to the DISCARD phase's own action. The play was
    /// unsubmittable as advertised and the AI re-picked it forever, TRUNCATING matches. Measured
    /// cost: 55 of 180 games in the stat-curve sweep died at <c>action.discard_selection_required</c>,
    /// which is why no win-rate curve could be produced at all.
    ///
    /// WHAT IS NOW TRUE: the boundary permits exactly ONE extra field, <c>selectedEntityIds</c>, on
    /// exactly that action shape, and validates it on its own terms (count equals the advertised
    /// requirement, every id is an advertised candidate). The AI makes the choice and submits it.
    /// So a self-discard play must be ACCEPTED, and that is asserted directly below instead of being
    /// excluded — an exclusion that says "this shape cannot work" would otherwise silently outlive
    /// the fix that made it work.
    /// </summary>
    [Test]
    public void EveryChosenActionIsAdvertisedAndAcceptedByTheEngine()
    {
        var factions = new[] { "flame", "machine", "sea", "wood" };
        var submissions = 0;
        var selfDiscardSubmissions = 0;
        var otherRejections = new List<string>();

        foreach (var actorFaction in factions)
        {
            foreach (var foeFaction in factions)
            {
                var rig = Build(5, actorFaction, foeFaction);
                var steps = 0;

                while (steps++ < 160)
                {
                    var snapshot = rig.Gateway.GetSnapshot(rig.State.CurrentPlayerIndex);
                    if (snapshot.WinnerPlayerIndex.HasValue) break;

                    var actor = snapshot.CurrentPlayer;
                    if (!rig.Policy.TryChoose(snapshot, actor, false, out var chosen) || chosen is null) break;

                    var advertised = snapshot.LegalActions.Any(a =>
                        string.Equals(a.ActionId, chosen.ActionId, StringComparison.Ordinal));
                    if (!advertised)
                    {
                        otherRejections.Add(
                            actorFaction + " vs " + foeFaction + ": policy returned '" + chosen.Type
                            + "/" + chosen.ActionId + "' which was never advertised");
                        break;
                    }

                    var isSelfDiscard = chosen.Payload is not null
                        && chosen.Payload.ContainsKey("discardRequired")
                        && chosen.Payload.ContainsKey("discardCandidateIds");
                    if (isSelfDiscard) selfDiscardSubmissions++;

                    var submission = rig.Gateway.Submit(ToSubmittedAction(snapshot, chosen));
                    submissions++;

                    if (!submission.Result.Accepted)
                    {
                        otherRejections.Add(
                            actorFaction + " vs " + foeFaction + ": engine rejected " + chosen.Type
                            + " with " + submission.Result.ReasonKey
                            + (isSelfDiscard ? " (SELF-DISCARD: the gap is back)" : string.Empty));
                        break;
                    }
                }
            }
        }

        TestContext.Out.WriteLine("actions submitted to the engine: {0}", submissions);
        TestContext.Out.WriteLine("self-discard plays submitted (and required to be accepted): {0}", selfDiscardSubmissions);

        Assert.That(submissions, Is.GreaterThan(100), "the sweep must be non-vacuous");
        Assert.That(
            selfDiscardSubmissions,
            Is.GreaterThan(0),
            "a self-discard play must actually be exercised, or this test no longer covers the closed gap");
        Assert.That(
            otherRejections,
            Is.Empty,
            "the policy must only propose advertised actions the engine accepts ("
            + otherRejections.Count + "):\n  " + string.Join("\n  ", otherRejections.Take(6)));
    }

    /// <summary>
    /// THE CONTRACT THAT CLOSED THE GAP, pinned from the positive side.
    ///
    /// The previous version of this test asserted the gap EXISTS ("this play is unsubmittable"), and
    /// was written to fail loudly if anyone closed it. Someone did: the boundary now permits exactly
    /// one extra field on exactly this action shape. So the test now asserts what is true instead —
    /// a self-discard play is advertised WITH the selection the engine requires, and the engine
    /// accepts it.
    ///
    /// It also checks the selection is a legal one on the advertisement's own terms: the count equals
    /// the advertised requirement and every chosen id is an advertised candidate. Without that, a
    /// passing acceptance would not prove the selection was meaningful.
    /// </summary>
    [Test]
    public void ASelfDiscardPlayCarriesALegalSelectionAndIsAccepted()
    {
        var observed = new List<string>();
        var exercised = 0;
        var failed = new List<string>();

        foreach (var actorFaction in new[] { "flame", "machine", "sea", "wood" })
        {
            foreach (var foeFaction in new[] { "sea", "wood" })
            {
                var rig = Build(5, actorFaction, foeFaction);
                var steps = 0;

                while (steps++ < 160)
                {
                    var snapshot = rig.Gateway.GetSnapshot(rig.State.CurrentPlayerIndex);
                    if (snapshot.WinnerPlayerIndex.HasValue) break;

                    var actor = snapshot.CurrentPlayer;
                    if (!rig.Policy.TryChoose(snapshot, actor, false, out var chosen) || chosen is null) break;

                    var isSelfDiscard = string.Equals(chosen.Type, "PLAY_CARD", StringComparison.Ordinal)
                        && chosen.Payload is not null
                        && chosen.Payload.ContainsKey("discardRequired")
                        && chosen.Payload.ContainsKey("discardCandidateIds");

                    if (isSelfDiscard)
                    {
                        exercised++;
                        var action = ToSubmittedAction(snapshot, chosen);

                        var selectedIds = new List<long>();
                        if (action.Payload is not null
                            && action.Payload.TryGetValue("selectedEntityIds", out var rawSelection)
                            && rawSelection is System.Collections.IEnumerable selectedValues
                            && rawSelection is not string)
                        {
                            foreach (var value in selectedValues)
                            {
                                if (value is null) continue;
                                try
                                {
                                    selectedIds.Add(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
                                }
                                catch (Exception exception) when (exception is FormatException
                                    || exception is InvalidCastException
                                    || exception is OverflowException)
                                {
                                    // An unparseable entry is a violation, captured by the count/legal checks below.
                                }
                            }
                        }

                        var required = chosen.Payload?["discardRequired"];
                        var requiredCount = required is null
                            ? -1
                            : Convert.ToInt32(required, System.Globalization.CultureInfo.InvariantCulture);

                        var candidates = new HashSet<long>();
                        if (chosen.Payload is not null
                            && chosen.Payload.TryGetValue("discardCandidateIds", out var rawCandidates)
                            && rawCandidates is System.Collections.IEnumerable candidateValues
                            && rawCandidates is not string)
                        {
                            foreach (var candidate in candidateValues)
                            {
                                if (candidate is null) continue;
                                try
                                {
                                    candidates.Add(Convert.ToInt64(candidate, System.Globalization.CultureInfo.InvariantCulture));
                                }
                                catch (Exception exception) when (exception is FormatException
                                    || exception is InvalidCastException
                                    || exception is OverflowException)
                                {
                                }
                            }
                        }

                        var allChosenAreCandidates = selectedIds.Count > 0
                            && selectedIds.All(id => candidates.Contains(id));

                        var submission = rig.Gateway.Submit(action);

                        observed.Add(
                            actorFaction + " vs " + foeFaction
                            + ": accepted=" + submission.Result.Accepted
                            + " reason=" + submission.Result.ReasonKey
                            + " required=" + requiredCount
                            + " selected=" + selectedIds.Count
                            + " candidates=" + candidates.Count
                            + " allChosenAreCandidates=" + allChosenAreCandidates);

                        if (!submission.Result.Accepted
                            || selectedIds.Count != requiredCount
                            || !allChosenAreCandidates)
                        {
                            failed.Add(observed[observed.Count - 1]);
                        }

                        break;
                    }

                    var pick = rig.Gateway.Submit(ToSubmittedAction(snapshot, chosen));
                    if (!pick.Result.Accepted) break;
                }
            }
        }

        foreach (var line in observed) TestContext.Out.WriteLine("  " + line);

        Assert.That(
            exercised,
            Is.GreaterThan(0),
            "no self-discard play was advertised in these matches, so the contract was not exercised");

        Assert.That(
            failed,
            Is.Empty,
            "a self-discard play must carry a legal selection (count == advertised requirement, every id a"
            + " candidate) and be ACCEPTED by the engine (" + failed.Count + " violations):\n  "
            + string.Join("\n  ", failed));
    }

    // ------------------------------------------------- win axis

    /// <summary>
    /// When the engine advertises a step on the AI's own victory axis, the instrument
    /// must take it. For the machine leader that step is PULL, and the axis is public: the
    /// snapshot publishes the objective and its current value.
    ///
    /// This is the "does it understand what winning means" check. A policy that ignored a
    /// free victory step would understate every deck that wins that way.
    /// </summary>
    [Test]
    public void AnAdvertisedVictoryStepIsTaken()
    {
        // machine_leader wins on PULL_COUNT reaching 6; the machine deck's own landmark
        // advertises PULL, so this is the shipped win axis, not a synthetic one.
        var rig = Build(9, "machine", "wood");
        var rigAtAction = AdvanceToAction(rig, Ai);

        var snapshot = rigAtAction.Snapshot();
        Assert.That(snapshot.WinnerPlayerIndex.HasValue, Is.False, "the match must still be live");

        var pulls = snapshot.LegalActions
            .Where(a => string.Equals(a.Type, "PULL", StringComparison.Ordinal))
            .ToList();
        if (pulls.Count == 0)
        {
            Assert.Pass("no PULL advertised in this position; nothing to check here");
        }

        var chosen = rigAtAction.Choose();

        // The published objective must also be readable, since that is how a consumer
        // knows the pull matters.
        var publishedObjective = snapshot.Players[Ai].LeaderZone
            .Select(card => card.Victory)
            .FirstOrDefault(v => v is not null);

        Assert.Multiple(() =>
        {
            Assert.That(publishedObjective, Is.Not.Null,
                "the AI's own objective must be published, or it cannot know the pull advances victory");
            Assert.That(publishedObjective!.Metric, Is.EqualTo("PULL_COUNT"));
            Assert.That(chosen.Type, Is.EqualTo("PULL"),
                "with a PULL advertised and the win axis published, taking it is the only non-blunder move;"
                + " legal=[" + string.Join(",", rigAtAction.AdvertisedSummary()) + "]");
        });
    }

    // ------------------------------------------------- resource retention

    /// <summary>
    /// CHARACTERISATION, not a virtue test. This pins a measured property of the shipped
    /// weight vector so the limitation is explicit instead of being discovered later by
    /// someone reading a balance number.
    ///
    /// WHAT WAS MEASURED, with every card's effects stripped so the plays cannot change the
    /// board, and with minions excluded because summoning a body is a real effect:
    ///
    ///   inert play flame_double  punish=2 rank=2 score=500 features[board=1x500 spend_card=1x0 punish=2x0]
    ///   inert play flame_rain    punish=4 rank=4 score=500 features[board=1x500 spend_card=1x0 punish=4x0]
    ///   END_TURN                                  rank=5 score=50
    ///
    /// TWO PROPERTIES OF THE MODEL PRODUCE THIS, and they are features of the model rather
    /// than bugs in a comparison:
    ///
    /// 1. <c>board</c> is set to 1 for EVERY PLAY_CARD (ActionFeatures.Of line: `[Board] =
    ///    isPlay ? 1 : 0`). It does not mean "this play improves the board"; it means "this
    ///    action is a card play". At weight 500 against <c>end_turn</c>'s 50, any play of
    ///    any card outranks ending the turn — including a card whose effects have been
    ///    removed entirely, and including one that is pure cost.
    /// 2. <c>punish</c> is priced at ZERO in the shipped default table, which the registry
    ///    documents as deliberate ("the features the default prices at zero are the ones it
    ///    was blind to (punish, the punish-converted discard, ...)"). So handing the
    ///    opponent cards costs the policy NOTHING in its own ranking.
    ///
    /// WHAT THIS MEANS FOR BALANCE MEASUREMENT, stated plainly: the shipped instrument
    /// cannot prefer restraint. It will play into its own punish because nothing in its
    /// objective says not to. Any deck whose strength comes from living inside a punish —
    /// or any card whose weakness is "hands the opponent cards" — is therefore measured
    /// with a systematic bias toward looking GOOD, because the pilot never declines the
    /// cost. That is a limitation to declare in the report, not a number to trust.
    ///
    /// The test asserts the property HOLDS so it cannot silently regress in the other
    /// direction either: if someone prices punish or stop treating every play as board
    /// value, this fails and the finding must be updated.
    /// </summary>
    [Test]
    public void InertPlaysOutrankEndingTheTurnAndPunishIsPricedAtZero()
    {
        Dictionary<string, CardDefinition>? mutated = null;
        var rig = Build(4, "flame", "sea", cards =>
        {
            var copy = new Dictionary<string, CardDefinition>(cards, StringComparer.Ordinal);
            foreach (var pair in cards.ToList())
            {
                var definition = pair.Value;
                if (definition.IsMinion || definition.IsLeader) continue;
                if (definition.Punish <= 0) continue;
                copy[pair.Key] = CloneWithNoEffects(definition);
            }

            mutated = copy;
            return copy;
        });
        Assert.That(mutated, Is.Not.Null, "the mutation must have been applied");
        var cards = mutated!;

        var rigAtAction = AdvanceToAction(rig, Ai);
        var snapshot = rigAtAction.Snapshot();
        if (snapshot.WinnerPlayerIndex.HasValue) Assert.Pass("match ended before an ACTION phase");

        var order = rigAtAction.Policy.OrderAdvertisedActions(snapshot, snapshot.LegalActions)
            .Select(a => StableKey(a))
            .ToList();

        var inert = snapshot.LegalActions
            .Where(a => string.Equals(a.Type, "PLAY_CARD", StringComparison.Ordinal))
            .Where(a => a.Payload is not null && a.Payload.ContainsKey("punish"))
            .Where(a => Convert.ToInt32(
                a.Payload!["punish"], System.Globalization.CultureInfo.InvariantCulture) > 0)
            // MINIONS ARE EXCLUDED: summoning a body IS a board effect, so a minion is never
            // inert however empty its effect list is. An earlier version of this test forgot
            // that and "found" a blunder that was really the policy correctly valuing board
            // presence — a false alarm, which is worse than no test.
            .Where(a => !IsMinionPlay(cards, a))
            .ToList();

        if (inert.Count == 0)
        {
            Assert.Pass("no truly inert punish-bearing play was advertised in this position");
        }

        var endTurnAction = snapshot.LegalActions
            .First(a => string.Equals(a.Type, "END_TURN", StringComparison.Ordinal));
        var endTurnIndex = order.FindIndex(key => key.StartsWith("END_TURN", StringComparison.Ordinal));

        foreach (var action in inert)
        {
            var punish = Convert.ToInt32(action.Payload!["punish"], System.Globalization.CultureInfo.InvariantCulture);
            var index = order.FindIndex(key => string.Equals(key, StableKey(action), StringComparison.Ordinal));
            var score = WeightedPlaystyle.WeightedScore(rigAtAction.Policy.Playstyle, snapshot, action);
            var features = ActionFeatures.Of(snapshot, action);
            var breakdown = string.Join(" ", features
                .Where(f => f.Value != 0)
                .Select(f => f.Key + "=" + f.Value
                    + "x" + (rigAtAction.Policy.Playstyle.Weights.TryGetValue(f.Key, out var w) ? w : 0)));
            TestContext.Out.WriteLine(
                "inert play {0} punish={1} rank={2} score={3} features[{4}]",
                action.CardId, punish, index, score, breakdown);
        }

        TestContext.Out.WriteLine(
            "END_TURN score={0} rank={1}; punish weight={2}",
            WeightedPlaystyle.WeightedScore(rigAtAction.Policy.Playstyle, snapshot, endTurnAction),
            endTurnIndex,
            rigAtAction.Policy.Playstyle.Weights.TryGetValue(ActionFeatures.Punish, out var pw) ? pw : int.MinValue);

        var inertIndices = inert
            .Select(a => order.FindIndex(key => string.Equals(key, StableKey(a), StringComparison.Ordinal)))
            .Where(index => index >= 0)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(endTurnIndex, Is.GreaterThanOrEqualTo(0), "ending the turn must be advertised");

            // The measured property: every inert play outranks ending the turn.
            Assert.That(
                inertIndices.All(index => index < endTurnIndex),
                Is.True,
                "the characterisation expects inert plays to outrank END_TURN; if that changed, the"
                + " reported limitation is stale. ranks=" + string.Join(",", inertIndices)
                + " endTurn=" + endTurnIndex);

            // And the reason: punish carries no weight.
            Assert.That(
                rigAtAction.Policy.Playstyle.Weights.TryGetValue(ActionFeatures.Punish, out var weight) && weight == 0,
                Is.True,
                "the characterisation expects punish to be priced at zero in the shipped default");
        });
    }

    // ------------------------------------------------- public information

    /// <summary>
    /// The instrument must act on what the snapshot PUBLISHES and never on what it hides.
    ///
    /// The adversarial form: change the opponent's hidden hand contents so they contradict
    /// the public counts, and require the policy's choice to be identical. If a policy
    /// could see the hidden hand it would exploit it, and every measurement would be of a
    /// game with no hidden information — which is not this game.
    /// </summary>
    [Test]
    public void TheChoiceDoesNotDependOnHiddenInformation()
    {
        var rig = Build(6, "sea", "flame");
        var rigAtAction = AdvanceToAction(rig, Ai);
        var snapshot = rigAtAction.Snapshot();
        if (snapshot.WinnerPlayerIndex.HasValue) Assert.Pass("match ended before an ACTION phase");

        var baseline = rigAtAction.Choose();

        // Contradict the opponent's hidden hand. The projection empties a non-viewer's
        // hand, so this is what a leaked hand WOULD have looked like.
        var altered = rigAtAction.Gateway.GetSnapshot(Ai);
        altered.Players[Foe].Hand = new[]
        {
            new RuntimeCardSnapshot { CardId = "wood_giant", OwnerPlayer = Foe, EntityId = 9001 },
            new RuntimeCardSnapshot { CardId = "wood_giant", OwnerPlayer = Foe, EntityId = 9002 },
        };
        altered.Players[Foe].HandCount = 2;

        Assert.That(
            rigAtAction.Policy.TryChoose(altered, Ai, false, out var afterLeak),
            Is.True);
        Assert.That(afterLeak, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(afterLeak!.Type, Is.EqualTo(baseline.Type),
                "the choice must not change when hidden contents change: the policy may only read public information");
            Assert.That(afterLeak.ActionId, Is.EqualTo(baseline.ActionId));
        });
    }

    // ------------------------------------------------- metamorphic: punish cost

    /// <summary>
    /// METAMORPHIC LAW. Raising the PUNISH an action carries — the number of cards it hands
    /// the opponent — must never make that action MORE attractive, and must never make a
    /// cheaper alternative LESS attractive.
    ///
    /// Two things make this a real measurement rather than a restatement:
    ///
    /// 1. BOTH runs are driven from the SAME starting position by the SAME deterministic
    ///    driver and the SAME seed, with the punish change pre-loaded. Comparing two
    ///    independently-seeded matches would compare different positions and prove nothing.
    /// 2. Actions are matched by a STABLE identity (type + card + target), not by their
    ///    advertised action id. Those ids are positional ("play_11"), so they shift when the
    ///    candidate set changes — an earlier version of this test compared them and reported
    ///    nonsense, which is the kind of false alarm that erodes trust in an instrument.
    /// </summary>
    [Test]
    public void RaisingPunishNeverImprovesAnActionsStanding()
    {
        var checkedPairs = 0;
        var raisedDecks = 0;
        var violations = new List<string>();

        foreach (var faction in new[] { "flame", "machine", "sea", "wood" })
        {
            // The SAME position, twice: once as shipped, once with punish raised on every
            // card that has any.
            var baseline = Build(8, faction, "flame");
            var raised = Build(8, faction, "flame", cards =>
            {
                var copy = new Dictionary<string, CardDefinition>(cards, StringComparer.Ordinal);
                foreach (var pair in cards.ToList())
                {
                    if (pair.Value.Punish <= 0) continue;
                    copy[pair.Key] = CloneWithPunish(pair.Value, pair.Value.Punish + 5);
                }

                return copy;
            });

            var baseRig = AdvanceToAction(baseline, Ai);
            var raisedRig = AdvanceToAction(raised, Ai);
            if (baseRig.Snapshot().WinnerPlayerIndex.HasValue) continue;

            raisedDecks++;

            var baseOrder = baseRig.Policy
                .OrderAdvertisedActions(baseRig.Snapshot(), baseRig.Snapshot().LegalActions);
            var raisedOrder = raisedRig.Policy
                .OrderAdvertisedActions(raisedRig.Snapshot(), raisedRig.Snapshot().LegalActions);

            // Rank only among PLAY_CARD candidates, keyed by stable identity.
            var baseRanks = RanksAmongPlays(baseRig.Snapshot(), baseOrder);
            var raisedRanks = RanksAmongPlays(raisedRig.Snapshot(), raisedOrder);

            foreach (var pair in baseRanks)
            {
                if (!raisedRanks.TryGetValue(pair.Key, out var after)) continue;
                checkedPairs++;

                // Lower rank = more attractive: rank 0 is the policy's first choice. A
                // HIGHER punish must therefore never produce a LOWER rank.
                if (after >= pair.Value) continue;

                violations.Add(
                    faction + ": '" + pair.Key + "' moved from play-rank " + pair.Value
                    + " to " + after + " when its punish was raised, i.e. it became MORE attractive");
            }
        }

        TestContext.Out.WriteLine("decks compared under raised punish: {0}", raisedDecks);
        TestContext.Out.WriteLine("action/rank pairs compared: {0}", checkedPairs);
        Assert.That(checkedPairs, Is.GreaterThan(0), "the comparison must be non-vacuous");
        Assert.That(
            violations,
            Is.Empty,
            "a higher punish must never make an action more attractive (" + violations.Count + "):\n  "
            + string.Join("\n  ", violations.Take(6)));
    }

    /// <summary>
    /// Ranks PLAY_CARD candidates by a STABLE identity so two runs can be compared.
    /// The advertised action id is positional and changes with the candidate set, so it
    /// cannot be used for this.
    /// </summary>
    private static Dictionary<string, int> RanksAmongPlays(
        RuntimeSnapshotEnvelope snapshot,
        IReadOnlyList<RuntimeLegalAction> order)
    {
        var byId = new Dictionary<string, RuntimeLegalAction>(StringComparer.Ordinal);
        foreach (var action in snapshot.LegalActions) byId[action.ActionId] = action;

        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        var rank = 0;
        foreach (var action in order)
        {
            if (!string.Equals(action.Type, "PLAY_CARD", StringComparison.Ordinal)) continue;
            if (!byId.TryGetValue(action.ActionId, out var source)) continue;

            var key = StableKey(source);
            if (!ranks.ContainsKey(key)) ranks[key] = rank;
            rank++;
        }

        return ranks;
    }

    /// <summary>
    /// The identity of an action that does not depend on the candidate set: what it does,
    /// with which card, at which target.
    /// </summary>
    private static string StableKey(RuntimeLegalAction action)
    {
        var target = action.TargetId ?? string.Empty;
        return action.Type + "|" + (action.CardId ?? string.Empty) + "|" + target;
    }

    /// <summary>
    /// True when playing this card puts a MINION on the board, which is a real effect no
    /// matter how empty the card's effect list is. Read from the card definition the
    /// advertisement names, so it is the engine's own classification rather than a guess.
    /// </summary>
    private static bool IsMinionPlay(IReadOnlyDictionary<string, CardDefinition> cards, RuntimeLegalAction action)
    {
        var cardId = action.CardId;
        if (string.IsNullOrEmpty(cardId)) return false;
        return cards.TryGetValue(cardId, out var definition) && definition.IsMinion;
    }

    /// <summary>
    /// A copy of the definition with every EFFECT removed but its punish, cost and identity
    /// intact, so a play can be advertised, chosen, and still change nothing on the board.
    /// Used to build the adversarial "spending achieves nothing" position.
    /// </summary>
    private static CardDefinition CloneWithNoEffects(CardDefinition source)
    {
        return new CardDefinition(
            source.Id, source.Name, source.Attack, source.Health, source.IsMinion, source.IsLeader, source.GrantLife,
            kingSlayer: source.KingSlayer,
            faction: source.Faction,
            text: source.Text,
            flavor: source.Flavor,
            cost: source.Cost,
            artId: source.ArtId,
            keywords: source.Keywords,
            tags: source.Tags,
            punishActivatable: source.PunishActivatable,
            punishCost: source.PunishCost,
            vulnerabilities: source.Vulnerabilities,
            type: source.Type,
            punish: source.Punish,
            punishCondition: source.PunishCondition,
            onPlayEffects: Array.Empty<DominionWars.Engine.Effects.EffectSpec>(),
            punishEffects: Array.Empty<DominionWars.Engine.Effects.EffectSpec>(),
            ambushKind: null,
            ambushTrigger: null,
            ambushEffects: Array.Empty<DominionWars.Engine.Effects.EffectSpec>(),
            chant: source.Chant,
            chantEffects: Array.Empty<DominionWars.Engine.Effects.EffectSpec>(),
            attacksPerTurn: source.AttacksPerTurn,
            onOpponentDiscardEffects: Array.Empty<DominionWars.Engine.Effects.EffectSpec>(),
            guard: source.Guard,
            leaderEnterEffects: source.LeaderEnterEffects,
            leaderPunishEffects: source.LeaderPunishEffects,
            leaderWinCondition: source.LeaderWinCondition,
            leaderWinText: source.LeaderWinText,
            leaderDurability: source.LeaderDurability,
            leaderWinParam: source.LeaderWinParam,
            commitCost: source.CommitCost,
            uploadCost: source.UploadCost,
            downloadCost: source.DownloadCost,
            commitEffects: source.CommitEffects,
            pushEffects: source.PushEffects,
            pullEffects: source.PullEffects,
            isLandmark: source.IsLandmark,
            landmarkTiers: source.LandmarkTiers,
            victory: source.Victory);
    }

    /// <summary>
    /// A copy of the definition with a different punish and everything else identical, so
    /// the only variable in the metamorphic test is the punish value.
    /// </summary>
    private static CardDefinition CloneWithPunish(CardDefinition source, int punish)
    {
        return new CardDefinition(
            source.Id, source.Name, source.Attack, source.Health, source.IsMinion, source.IsLeader, source.GrantLife,
            kingSlayer: source.KingSlayer,
            faction: source.Faction,
            text: source.Text,
            flavor: source.Flavor,
            cost: source.Cost,
            artId: source.ArtId,
            keywords: source.Keywords,
            tags: source.Tags,
            punishActivatable: source.PunishActivatable,
            punishCost: source.PunishCost,
            vulnerabilities: source.Vulnerabilities,
            type: source.Type,
            punish: punish,
            punishCondition: source.PunishCondition,
            onPlayEffects: source.OnPlayEffects,
            punishEffects: source.PunishEffects,
            ambushKind: source.AmbushKind,
            ambushTrigger: source.AmbushTrigger,
            ambushEffects: source.AmbushEffects,
            chant: source.Chant,
            chantEffects: source.ChantEffects,
            attacksPerTurn: source.AttacksPerTurn,
            onOpponentDiscardEffects: source.OnOpponentDiscardEffects,
            guard: source.Guard,
            leaderEnterEffects: source.LeaderEnterEffects,
            leaderPunishEffects: source.LeaderPunishEffects,
            leaderWinCondition: source.LeaderWinCondition,
            leaderWinText: source.LeaderWinText,
            leaderDurability: source.LeaderDurability,
            leaderWinParam: source.LeaderWinParam,
            commitCost: source.CommitCost,
            uploadCost: source.UploadCost,
            downloadCost: source.DownloadCost,
            commitEffects: source.CommitEffects,
            pushEffects: source.PushEffects,
            pullEffects: source.PullEffects,
            isLandmark: source.IsLandmark,
            landmarkTiers: source.LandmarkTiers,
            victory: source.Victory);
    }
}

}
