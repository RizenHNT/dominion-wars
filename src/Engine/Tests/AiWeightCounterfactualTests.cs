using System;
using System.Collections.Generic;
using System.Globalization;
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
/// WEIGHT COUNTERFACTUALS, MEASURED BEFORE ANY PRODUCTION WEIGHT IS CHANGED.
///
/// WHY THIS EXISTS INSTEAD OF AN EDIT. The shipped default weights give `face` 0 and `punish` 0, so
/// a face attack scores `damage` 120 + `face` 0 = 120 while ANY card play scores `board` 500. A face
/// attack can therefore never be chosen while a play is advertised. Raised `face` fixes that — but
/// `PlaystyleTests.DefaultWeightsOrderAdvertisedActionsExactlyLikeTheRetiredRank` asserts, over 600+
/// advertised sets and three separate comparisons, that the default weights reproduce the RETIRED
/// hand-written order, and in that order a PLAY ranks above an ATTACK. So raising `face` through
/// `board` BREAKS a currently-green invariant.
///
/// THAT INVARIANT IS NOT SOMETHING TO QUIETLY REWRITE. It is the migration proof that the weight
/// table reproduces the policy the historical measurements were taken with. Rewriting it to fit a
/// new preference would be changing the test to agree with the change — the exact failure mode this
/// project has already suffered from. So the trade is MEASURED here first, on real positions, with
/// candidate weight tables built in the test and no production weight touched.
///
/// Every candidate is compared against the shipped default on:
///   * how often an advertised FACE attack is actually taken (the thing `face` 0 makes impossible),
///   * how often it is taken when the attack is LETHAL (the case that decides games),
///   * how often the chosen action DIFFERS from the default's choice (the blast radius),
///   * the total `punish` the chosen actions carry (the thing `punish` 0 makes invisible).
///
/// THIS FIXTURE ASSERTS NO DIRECTION. It reports numbers; the acceptance decision belongs to the
/// owner, because raising `face` above `board` changes what the shipped opponent DOES.
/// </summary>
public sealed class AiWeightCounterfactualTests
{
    private sealed class Candidate
    {
        public string Label = string.Empty;
        public int Face;
        public int Punish;
        public WeightedPlaystyle Playstyle = null!;
    }

    private sealed class Tally
    {
        public int Decisions;
        public int FaceAdvertised;
        public int FaceTaken;
        public int LethalFaceAdvertised;
        public int LethalFaceTaken;
        public int DifferFromDefault;
        public long PunishOnChosen;
        public int PunishOnChosenSamples;
    }

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

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from " + TestContext.CurrentContext.TestDirectory);
    }

    private static CardCatalog Catalog()
        => CardCatalog.LoadDirectory(Path.Combine(RepositoryRoot(), "data", "cards"));

    private static DeckDefinition Deck(string faction)
        => DeckLoader.LoadFile(Path.Combine(RepositoryRoot(), "data", "decks", faction + "_deck.json"));

    private static MatchDeckSpec Spec(DeckDefinition deck)
        => new MatchDeckSpec(deck.Leader, deck.Cards, deck.Name, deck.Faction);

    /// <summary>The shipped default table with exactly one feature overridden.</summary>
    private static WeightedPlaystyle Variant(string id, int face, int punish)
    {
        var weights = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [ActionFeatures.Lifecycle] = 300,
            [ActionFeatures.WinAxis] = 700,
            [ActionFeatures.ChainReverse] = -300,
            [ActionFeatures.Board] = 500,
            [ActionFeatures.Ambush] = 0,
            [ActionFeatures.SpendCard] = 0,
            [ActionFeatures.SpendField] = -60,
            [ActionFeatures.Damage] = 120,
            [ActionFeatures.Face] = face,
            [ActionFeatures.Trade] = 0,
            [ActionFeatures.Punish] = punish,
            [ActionFeatures.DiscardCost] = 0,
            [ActionFeatures.EndTurn] = 50,
        };
        return new WeightedPlaystyle(id, PunishResponseStance.Never, weights);
    }

    [Test]
    public void MeasuresCandidateWeightTablesAgainstTheShippedDefault()
    {
        var candidates = new List<Candidate>
        {
            new Candidate { Label = "baseline  face=0   punish=0  (shipped)", Face = 0, Punish = 0 },
            new Candidate { Label = "sub-board face=300 punish=-2 (keeps plays above face)", Face = 300, Punish = -2 },
            new Candidate { Label = "above-board face=400 punish=-2 (breaks the retired order)", Face = 400, Punish = -2 },
            new Candidate { Label = "punish-only face=0   punish=-2 (no ordering change)", Face = 0, Punish = -2 },
        };
        foreach (var candidate in candidates)
        {
            candidate.Playstyle = Variant("cand_" + candidate.Face + "_" + candidate.Punish, candidate.Face, candidate.Punish);
        }

        var tallies = candidates.ToDictionary(c => c.Label, _ => new Tally(), StringComparer.Ordinal);
        var orderingDivergences = candidates.ToDictionary(c => c.Label, _ => 0, StringComparer.Ordinal);
        var orderingComparisons = 0;
        var defaultPolicy = new AdvertisedActionPolicy();

        foreach (var pair in new[]
                 {
                     new[] { "flame", "machine" },
                     new[] { "machine", "sea" },
                     new[] { "sea", "flame" },
                     new[] { "wood", "machine" },
                 })
        {
            for (var seed = 1; seed <= 4; seed++)
            {
                var state = MatchSetup.Create(
                    Spec(Deck(pair[0])),
                    Spec(Deck(pair[1])),
                    Catalog().Cards,
                    new MatchSetupOptions
                    {
                        Seed = (ulong)seed,
                        FirstPlayerIndex = 0,
                        OpeningHandSize = 5,
                        PlayerLife = 20,   // a life pool, so the FACE target is advertised at all
                        CastleEnabled = true,
                        CastleHealth = 75,
                    });

                var flow = TurnFlow.CreateDefault();
                var router = TurnActionRouter.CreateDefault(
                    flow, null, PunishResponseStances.PolicyFor(PlaystyleRegistry.Default));
                var gateway = new RuntimeMatchGateway("match_weights", state, flow, router, null);
                if (!gateway.Initialize(0).Accepted) continue;

                var steps = 0;
                while (steps++ < 300)
                {
                    if (state.WinnerPlayerIndex.HasValue) break;

                    var viewer = state.CurrentPlayerIndex;
                    var snapshot = gateway.GetSnapshot(viewer);
                    if (snapshot.WinnerPlayerIndex.HasValue) break;

                    var actor = snapshot.CurrentPlayer;
                    if (!string.Equals(snapshot.Phase, "ACTION", StringComparison.Ordinal)
                        || snapshot.LegalActions.Count == 0)
                    {
                        var any = snapshot.LegalActions.FirstOrDefault();
                        if (any is null) break;
                        if (!gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, any)).Result.Accepted) break;
                        continue;
                    }

                    var faceAttacks = snapshot.LegalActions.Where(IsFaceAttack).ToList();
                    var foeLife = state.GetOpponent(actor).Life;
                    var myAttack = state.GetPlayer(actor).Field.Sum(c => c.Attack);
                    var lethalAvailable = faceAttacks.Count > 0 && foeLife.HasValue && myAttack >= foeLife.Value;

                    // The shipped default's own choice, used as the comparison point.
                    var defaultChoice = defaultPolicy.TryChoose(snapshot, actor, false, out var dflt) && dflt is not null
                        ? dflt
                        : snapshot.LegalActions[0];

                    foreach (var candidate in candidates)
                    {
                        var tally = tallies[candidate.Label];
                        tally.Decisions++;
                        if (faceAttacks.Count > 0)
                        {
                            tally.FaceAdvertised++;
                            if (lethalAvailable) tally.LethalFaceAdvertised++;
                        }

                        var policy = new AdvertisedActionPolicy(candidate.Playstyle);
                        var chosen = policy.TryChoose(snapshot, actor, false, out var pick) && pick is not null
                            ? pick
                            : snapshot.LegalActions[0];

                        if (IsFaceAttack(chosen))
                        {
                            tally.FaceTaken++;
                            if (lethalAvailable) tally.LethalFaceTaken++;
                        }

                        if (!string.Equals(chosen.ActionId, defaultChoice.ActionId, StringComparison.Ordinal))
                        {
                            tally.DifferFromDefault++;
                        }

                        if (chosen.Payload is not null
                            && chosen.Payload.TryGetValue("punish", out var raw)
                            && raw is not null
                            && int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
                        {
                            tally.PunishOnChosen += p;
                            tally.PunishOnChosenSamples++;
                        }

                        // Ordering divergence versus the shipped default, counted per decision.
                        orderingComparisons++;
                        var candidateOrder = WeightedPlaystyle.Order(candidate.Playstyle, snapshot, snapshot.LegalActions)
                            .Select(a => a.ActionId).ToArray();
                        var defaultOrder = defaultPolicy.OrderAdvertisedActions(snapshot, snapshot.LegalActions)
                            .Select(a => a.ActionId).ToArray();
                        if (!candidateOrder.SequenceEqual(defaultOrder)) orderingDivergences[candidate.Label]++;
                    }

                    var submitChoice = defaultPolicy.TryChoose(snapshot, actor, false, out var submit) && submit is not null
                        ? submit
                        : snapshot.LegalActions[0];
                    var result = gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, submitChoice));
                    if (!result.Result.Accepted)
                    {
                        var alternative = snapshot.LegalActions
                            .FirstOrDefault(a => !string.Equals(a.ActionId, submitChoice.ActionId, StringComparison.Ordinal));
                        if (alternative is null
                            || !gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, alternative)).Result.Accepted)
                        {
                            break;
                        }
                    }
                }
            }
        }

        TestContext.Out.WriteLine("WEIGHT COUNTERFACTUALS (same positions for every candidate)");
        foreach (var candidate in candidates)
        {
            var t = tallies[candidate.Label];
            TestContext.Out.WriteLine("  [{0}]", candidate.Label);
            TestContext.Out.WriteLine("    ACTION decisions considered        : {0}", t.Decisions);
            TestContext.Out.WriteLine("    decisions with a FACE attack offered: {0}", t.FaceAdvertised);
            TestContext.Out.WriteLine("    ...of which the attack was LETHAL  : {0}", t.LethalFaceAdvertised);
            TestContext.Out.WriteLine("    face attacks actually TAKEN        : {0}", t.FaceTaken);
            TestContext.Out.WriteLine("    LETHAL face attacks taken          : {0}", t.LethalFaceTaken);
            TestContext.Out.WriteLine("    choices differing from baseline    : {0} ({1}%)",
                t.DifferFromDefault,
                t.Decisions == 0 ? "0" : (100.0 * t.DifferFromDefault / t.Decisions).ToString("F1", CultureInfo.InvariantCulture));
            TestContext.Out.WriteLine("    mean punish on the chosen action   : {0}",
                t.PunishOnChosenSamples == 0
                    ? "n/a"
                    : ((double)t.PunishOnChosen / t.PunishOnChosenSamples).ToString("F2", CultureInfo.InvariantCulture));
            TestContext.Out.WriteLine("    orderings differing from baseline  : {0}/{1}",
                orderingDivergences[candidate.Label], t.Decisions);
        }

        // NON-VACUITY ONLY: the sweep must have produced positions where a face attack was on offer.
        // Without that, every count above is a statement about the fixtures rather than the weights.
        var baseline = tallies[candidates[0].Label];
        Assert.That(baseline.Decisions, Is.GreaterThan(0), "the sweep must reach ACTION decisions");
        foreach (var candidate in candidates)
        {
            Assert.That(
                tallies[candidate.Label].FaceAdvertised,
                Is.GreaterThan(0),
                "candidate '" + candidate.Label + "' never saw a face attack offered, so its face counts mean nothing");
        }
    }

    /// <summary>
    /// A FACE attack, using the wire form read from the projector rather than guessed:
    /// `RuntimeContractV131Snapshot.TargetId` maps the engine's `core:player_N:life` to the
    /// literal string <c>player_N</c>, maps the shared castle to <c>castle</c>, and maps an entity
    /// target to a positive numeric id. An earlier predicate looked for ":life" and recognised
    /// nothing — which is why the control in the sibling fixture exists.
    /// </summary>
    private static bool IsFaceAttack(RuntimeLegalAction action)
    {
        if (action is null || !string.Equals(action.Type, "ATTACK", StringComparison.Ordinal)) return false;
        if (action.TargetId is null) return false;

        var target = action.TargetId.ToString();
        if (string.IsNullOrEmpty(target)) return false;
        if (long.TryParse(target, NumberStyles.None, CultureInfo.InvariantCulture, out _)) return false;
        if (string.Equals(target, "castle", StringComparison.Ordinal)) return false;
        return target.StartsWith("player_", StringComparison.Ordinal);
    }
}
}
