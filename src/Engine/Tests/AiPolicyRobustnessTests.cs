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
/// POLICY ROBUSTNESS: is a reading STABLE, or is it an artefact of one starting position?
///
/// The owner's requirement is that conclusions must not depend on one policy or one seed. The
/// cross-policy half is covered in AiCrossPolicyAgreementTests; this is the OTHER half — the same
/// policy, the SAME questions, but different matches.
///
/// The most valuable measurement here is the NOISE FLOOR. A balance claim of the form "card X is
/// stronger than card Y" is only meaningful if the difference between them exceeds the variation
/// caused by nothing more than which matches happened to be sampled. So this fixture measures
/// that variation directly:
///
///   - SPLIT the seed set into disjoint halves and compute each half's card ordering,
///   - measure how often the two halves AGREE on which of a pair of cards is preferred.
///
/// That number is the instrument's reproducibility. Any claimed card difference smaller than the
/// disagreement it produces is not a finding — it is sampling noise, and the report must say so
/// rather than let a reader treat every ordering as real.
///
/// The fixture also varies the other structural knobs the owner named — seat order and opening
/// hand size — because a reading that only holds at one table setup is not robust either.
/// </summary>
public sealed class AiPolicyRobustnessTests
{
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

    private sealed class Reading
    {
        public int Advertised;
        public int Chosen;
        public double Rate => Advertised == 0 ? 0.0 : Chosen / (double)Advertised;
        public override string ToString() => Chosen + "/" + Advertised + " = " + Rate.ToString("P2");
    }

    /// <summary>
    /// Drives matches under a fixed structural setup and records per-card selection rates.
    ///
    /// The setup is parameterised by the three things the owner named: which seeds, who moves
    /// first, and the opening hand size. Everything else is fixed, so a difference between two
    /// runs is attributable to those knobs alone.
    /// </summary>
    private static Dictionary<string, Reading> Run(
        IEnumerable<int> seeds,
        int firstPlayerIndex,
        int openingHandSize,
        string playstyleId = PlaystyleRegistry.DefaultId)
    {
        var catalog = Catalog();
        var playstyle = PlaystyleRegistry.GetPlaystyle(playstyleId);
        var readings = new Dictionary<string, Reading>(StringComparer.Ordinal);

        foreach (var actorFaction in new[] { "flame", "machine", "sea", "wood" })
        {
            foreach (var foeFaction in new[] { "flame", "machine", "sea", "wood" })
            {
                foreach (var seed in seeds)
                {
                    var state = MatchSetup.Create(
                        Spec(Deck(actorFaction)),
                        Spec(Deck(foeFaction)),
                        catalog.Cards,
                        new MatchSetupOptions
                        {
                            Seed = (ulong)seed,
                            FirstPlayerIndex = firstPlayerIndex,
                            OpeningHandSize = openingHandSize,
                            PlayerLife = 20,
                            CastleEnabled = true,
                            CastleHealth = 75,
                        });

                    var flow = TurnFlow.CreateDefault();
                    var router = TurnActionRouter.CreateDefault(flow, null, PunishResponseStances.PolicyFor(playstyle));
                    var gateway = new RuntimeMatchGateway("match_robustness", state, flow, router, null);
                    if (!gateway.Initialize(firstPlayerIndex).Accepted) continue;

                    var policy = new AdvertisedActionPolicy(playstyle);
                    var steps = 0;

                    while (steps++ < 200)
                    {
                        if (state.WinnerPlayerIndex.HasValue) break;

                        var viewer = state.CurrentPlayerIndex;
                        var snapshot = gateway.GetSnapshot(viewer);
                        if (snapshot.WinnerPlayerIndex.HasValue) break;

                        var actor = snapshot.CurrentPlayer;
                        if (!policy.TryChoose(snapshot, actor, false, out var chosen) || chosen is null)
                        {
                            chosen = snapshot.LegalActions.FirstOrDefault();
                            if (chosen is null) break;
                        }

                        foreach (var action in snapshot.LegalActions)
                        {
                            if (!string.Equals(action.Type, "PLAY_CARD", StringComparison.Ordinal)) continue;
                            var cardId = action.CardId;
                            if (string.IsNullOrEmpty(cardId)) continue;

                            if (!readings.TryGetValue(cardId, out var reading))
                            {
                                reading = new Reading();
                                readings[cardId] = reading;
                            }

                            reading.Advertised++;
                            if (string.Equals(chosen.ActionId, action.ActionId, StringComparison.Ordinal))
                            {
                                reading.Chosen++;
                            }
                        }

                        var submission = gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, chosen));
                        if (!submission.Result.Accepted)
                        {
                            var alternative = snapshot.LegalActions
                                .FirstOrDefault(a => !string.Equals(a.ActionId, chosen.ActionId, StringComparison.Ordinal));
                            if (alternative is null
                                || !gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, alternative)).Result.Accepted)
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }

        return readings;
    }

    /// <summary>
    /// Pairwise ordering agreement between two readings, over the cards both observed.
    ///
    /// Ties are skipped rather than counted as agreement: counting them would inflate the number
    /// and make the instrument look more reproducible than it is.
    /// </summary>
    private static (int Agree, int Total) OrderAgreement(
        Dictionary<string, Reading> left,
        Dictionary<string, Reading> right)
    {
        var shared = left.Keys
            .Intersect(right.Keys, StringComparer.Ordinal)
            .Where(id => left[id].Advertised > 0 && right[id].Advertised > 0)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var agree = 0;
        var total = 0;
        for (var a = 0; a < shared.Count; a++)
        {
            for (var b = a + 1; b < shared.Count; b++)
            {
                var leftDelta = left[shared[a]].Rate - left[shared[b]].Rate;
                var rightDelta = right[shared[a]].Rate - right[shared[b]].Rate;
                if (Math.Abs(leftDelta) < 1e-12 || Math.Abs(rightDelta) < 1e-12) continue;

                total++;
                if (Math.Sign(leftDelta) == Math.Sign(rightDelta)) agree++;
            }
        }

        return (agree, total);
    }

    /// <summary>
    /// ⑥ THE NOISE FLOOR. Two disjoint seed halves of the SAME setup are compared. Their
    /// agreement on card ordering is how reproducible the instrument is when nothing has changed.
    ///
    /// This is the number that decides whether a balance claim is usable: a claimed difference
    /// between two cards is only evidence if it survives re-sampling.
    /// </summary>
    [Test]
    public void ReSamplingTheSameSetupReproducesTheCardOrdering()
    {
        var firstHalf = Run(new[] { 1, 2, 3, 4 }, firstPlayerIndex: 0, openingHandSize: 5);
        var secondHalf = Run(new[] { 5, 6, 7, 8 }, firstPlayerIndex: 0, openingHandSize: 5);

        var (agree, total) = OrderAgreement(firstHalf, secondHalf);
        var rate = total == 0 ? 0.0 : agree / (double)total;

        TestContext.Out.WriteLine("cards observed by both halves: {0}",
            firstHalf.Keys.Intersect(secondHalf.Keys, StringComparer.Ordinal).Count());
        TestContext.Out.WriteLine("seed-half ordering agreement: {0}/{1} = {2:P1}", agree, total, rate);
        TestContext.Out.WriteLine(
            "  => the instrument's REPRODUCIBILITY at this sample size. A claimed card difference"
            + " smaller than the disagreement this implies is sampling noise, not a finding.");

        Assert.That(total, Is.GreaterThan(0), "the comparison must be non-vacuous");
        Assert.That(
            rate,
            Is.GreaterThan(0.5),
            "two halves of the same setup must agree on ordering more often than chance, or no"
            + " reading from this instrument is reproducible (measured " + rate.ToString("P1") + ")");
    }

    /// <summary>
    /// ⑥ SEAT ORDER. Whoever moves first is a real structural advantage, so a reading that only
    /// holds with seat 0 first is not robust. The two seat orders must not invert the ordering.
    /// </summary>
    [Test]
    public void ChangingWhichSeatMovesFirstDoesNotInvertTheOrdering()
    {
        var seatZero = Run(new[] { 1, 2, 3, 4, 5, 6 }, firstPlayerIndex: 0, openingHandSize: 5);
        var seatOne = Run(new[] { 1, 2, 3, 4, 5, 6 }, firstPlayerIndex: 1, openingHandSize: 5);

        var (agree, total) = OrderAgreement(seatZero, seatOne);
        var rate = total == 0 ? 0.0 : agree / (double)total;

        TestContext.Out.WriteLine("seat 0 first vs seat 1 first ordering agreement: {0}/{1} = {2:P1}",
            agree, total, rate);

        Assert.That(total, Is.GreaterThan(0), "the comparison must be non-vacuous");
        Assert.That(
            rate,
            Is.GreaterThan(0.5),
            "which seat moves first must not reverse the instrument's card ordering, or the reading"
            + " is a seat artefact (measured " + rate.ToString("P1") + ")");
    }

    /// <summary>
    /// ⑥ OPENING HAND SIZE. This changes how many cards are in play from turn one, so it is a
    /// different game shape rather than a different sample of the same one. Weaker claim: the
    /// ordering must stay better than chance, not identical.
    /// </summary>
    [Test]
    public void ChangingTheOpeningHandSizeDoesNotReverseTheOrdering()
    {
        var small = Run(new[] { 1, 2, 3, 4, 5, 6 }, firstPlayerIndex: 0, openingHandSize: 4);
        var large = Run(new[] { 1, 2, 3, 4, 5, 6 }, firstPlayerIndex: 0, openingHandSize: 6);

        var (agree, total) = OrderAgreement(small, large);
        var rate = total == 0 ? 0.0 : agree / (double)total;

        TestContext.Out.WriteLine("opening hand 4 vs 6 ordering agreement: {0}/{1} = {2:P1}",
            agree, total, rate);

        Assert.That(total, Is.GreaterThan(0), "the comparison must be non-vacuous");
        Assert.That(
            rate,
            Is.GreaterThan(0.5),
            "a different opening hand size must not reverse the card ordering (measured "
            + rate.ToString("P1") + ")");
    }

    /// <summary>
    /// ⑥ THE NON-VACUITY GUARD. The reproducibility tests above are only meaningful if the
    /// readings they compare actually vary. If every card were read at the same rate by every
    /// run, ordering agreement would be trivially perfect and would prove nothing.
    ///
    /// This measures how much spread there is to reproduce in the first place.
    /// </summary>
    [Test]
    public void ThereIsOrderingToReproduceInTheFirstPlace()
    {
        var readings = Run(new[] { 1, 2, 3, 4, 5, 6 }, firstPlayerIndex: 0, openingHandSize: 5);

        var observed = readings.Where(pair => pair.Value.Advertised > 0).ToList();
        Assert.That(observed.Count, Is.GreaterThan(4), "too few cards observed to order them");

        var rates = observed.Select(pair => pair.Value.Rate).OrderBy(r => r).ToList();
        var spread = rates[^1] - rates[0];
        var distinct = rates.Distinct().Count();

        TestContext.Out.WriteLine("cards observed: {0}", observed.Count);
        TestContext.Out.WriteLine("  selection-rate range: {0:P2} .. {1:P2} (spread {2:P2})",
            rates[0], rates[^1], spread);
        TestContext.Out.WriteLine("  distinct rates: {0}", distinct);

        Assert.That(spread, Is.GreaterThan(0.0),
            "if every card were read at the same rate there would be no ordering to reproduce, and"
            + " the agreement tests above would be vacuous");
        Assert.That(distinct, Is.GreaterThan(1), "at least two cards must differ");
    }
}

}
