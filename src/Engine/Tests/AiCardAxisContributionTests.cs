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
/// THE PER-CARD YARDSTICK: WHAT DOES EACH CARD CONTRIBUTE TO ITS OWN DECK'S WIN AXIS?
///
/// WHY THIS AND NOT WIN RATE. A stat-curve sweep measured win rate as flat — moving one card by +3/+3
/// left sea-vs-flame at 100% and wood-vs-flame at 40% — because the outcome is decided by which decks
/// are paired. Win rate therefore cannot price a card. What CAN be measured is how far a deck advances
/// the counter its own leader declares, which is meaningful even in a match that never settles.
///
/// HOW IT MEASURES. For each faction, every one of its cards is ablated in turn: all copies of that
/// card are replaced by a filler body with the same name and copy count so the deck keeps its size and
/// shape, but with no stats, no effects and no punish. The matches are then replayed at the SAME seeds
/// and the three axis counters are read for the focal seat. The difference from the un-ablated baseline
/// is that card's marginal contribution to the axis.
///
/// WHY A FILLER AND NOT A SMALLER DECK: removing copies would change the deck's size, which changes
/// draw pressure, reshuffle timing and the deck-cycle counter. A same-size, same-count swap keeps every
/// other card's draw probability comparable, so the only variable is the card's own content.
///
/// KNOWN LIMITATION: this measures each card's marginal value INSIDE ITS CURRENT DECK, against the
/// shipped default opponent. A card's value elsewhere, or under another policy, would differ. It is a
/// yardstick for placing new cards, not a claim about a card's absolute strength.
///
/// IT ASSERTS NO TARGET. It prints the yardstick. The values chosen for new cards are a design
/// decision, informed by this table.
/// </summary>
public sealed class AiCardAxisContributionTests
{
    private const int SeedsPerCard = 6;

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

    private sealed class Axes
    {
        public int OwnDiscarded;
        public int OppDiscarded;
        public int Pulls;
        public int SealedHealth;
        public int Turns;
        public int Finished;
    }

    /// <summary>
    /// The axis counters for one seat, read from engine state.
    ///
    /// BOTH SIDES' DISCARD COUNTS ARE RETURNED, and that is not padding: `TotalDiscarded` counts the
    /// cards the DISCARDING player threw away, while the discard victory condition reads the
    /// OPPONENT's counter (`OPP_DISCARD_COUNT` -> `opponent.TotalDiscarded`). The first version of this
    /// harness read only the focal seat's own counter and therefore reported a discard axis of exactly
    /// zero for a sea deck built to attack that axis — a measurement bug that looked like a finding.
    /// </summary>
    private static (int OwnDiscarded, int OppDiscarded, int Pulls, int Sealed, int Turns, int Finished) ReadAxes(
        GameState state,
        int seat)
    {
        var player = state.GetPlayer(seat);
        var opponent = state.GetOpponent(seat);
        var sealedHealth = 0;
        foreach (var card in player.Field)
        {
            if (card.Sealed && card.Health > sealedHealth) sealedHealth = card.Health;
        }

        return (player.TotalDiscarded, opponent.TotalDiscarded, player.PullCount, sealedHealth,
            state.Turn.Number, state.WinnerPlayerIndex.HasValue ? 1 : 0);
    }

    /// <summary>
    /// Two ablation modes, because they answer different questions and the first version only had one.
    ///
    /// <see cref="Empty"/> swaps the card for a 0/1 body with no effects: the card's TOTAL content is
    /// removed, so the reading is "what does this card contribute at all".
    ///
    /// <see cref="Vanilla"/> swaps it for a 1/1 body with the SAME punish, no effects. Comparing the two
    /// separates "the card's effect does not push the axis" from "the AI misuses a card whose effect is
    /// fine": if a card drags the axis under Empty but is neutral under Vanilla, its stats/body are
    /// pulling weight and only its effect is questionable; if it drags under BOTH, the card is a net
    /// cost to its own deck and its slot is better spent elsewhere.
    /// </summary>
    private enum AblationMode
    {
        /// <summary>0/1, no effects: the card's whole content is gone.</summary>
        Empty,

        /// <summary>1/1 at the same punish, no effects. Kept as a CONTROL, not as the main reading.</summary>
        Vanilla,

        /// <summary>
        /// SAME type, SAME stats, SAME punish, all effects removed. This is the mode that isolates the
        /// card's EFFECT, which is what card design actually needs.
        ///
        /// Why it exists: the first version of this harness compared the full card against a 1/1 body,
        /// which changed the card's STATS and its EFFECTS in one step. `wood_druid` read +41.3 under
        /// that comparison and -5.1 against a same-size body, so the headline number was mostly the
        /// body, not the effect. An experiment that moves two variables cannot attribute either.
        /// </summary>
        EffectOnly,
    }

    private static CardDefinition Ablate(CardDefinition original, AblationMode mode)
    {
        switch (mode)
        {
            case AblationMode.Empty:
                return new CardDefinition(
                    id: original.Id,
                    name: original.Name,
                    attack: 0,
                    health: 1,
                    isMinion: true,
                    faction: original.Faction,
                    text: "(ablated: no content, for measurement)",
                    tags: original.Tags?.ToArray());

            case AblationMode.Vanilla:
                return new CardDefinition(
                    id: original.Id,
                    name: original.Name,
                    attack: 1,
                    health: 1,
                    isMinion: true,
                    faction: original.Faction,
                    punish: original.Punish,
                    text: "(ablated: vanilla body, same punish, for measurement)",
                    tags: original.Tags?.ToArray());

            default:
                // Everything preserved except the effects themselves.
                return new CardDefinition(
                    id: original.Id,
                    name: original.Name,
                    attack: original.Attack,
                    health: original.Health,
                    isMinion: original.IsMinion,
                    isLeader: original.IsLeader,
                    grantLife: original.GrantLife,
                    kingSlayer: original.KingSlayer,
                    keywords: original.Keywords?.ToArray(),
                    vulnerabilities: original.Vulnerabilities?.ToArray(),
                    faction: original.Faction,
                    text: "(ablated: effects removed, stats kept, for measurement)",
                    flavor: original.Flavor,
                    cost: original.Cost,
                    rarity: original.Rarity,
                    artId: original.ArtId,
                    tags: original.Tags?.ToArray(),
                    punishActivatable: original.PunishActivatable,
                    punishCost: original.PunishCost,
                    hasLeaderAbility: original.HasLeaderAbility,
                    type: original.Type,
                    punish: original.Punish,
                    punishCondition: original.PunishCondition,
                    ambushKind: original.AmbushKind,
                    ambushTrigger: original.AmbushTrigger,
                    chant: original.Chant,
                    attacksPerTurn: original.AttacksPerTurn,
                    guard: original.Guard,
                    leaderWinCondition: original.LeaderWinCondition,
                    leaderWinText: original.LeaderWinText,
                    leaderDurability: original.LeaderDurability,
                    leaderWinParam: original.LeaderWinParam,
                    commitCost: original.CommitCost,
                    uploadCost: original.UploadCost,
                    downloadCost: original.DownloadCost,
                    isLandmark: original.IsLandmark,
                    landmarkTiers: original.LandmarkTiers?.ToArray(),
                    victory: original.Victory);
        }
    }

    /// <summary>
    /// Plays one match with the focal seat's deck possibly ablated, and returns the axis readings.
    /// </summary>
    private static (int OwnDiscarded, int OppDiscarded, int Pulls, int Sealed, int Turns, int Finished) PlayOne(
        string focalFaction,
        string foeFaction,
        string? ablateCardId,
        AblationMode mode,
        ulong seed,
        int focalSeat)
    {
        var catalog = Catalog();
        var focalDeck = Deck(focalFaction);
        var foeDeck = Deck(foeFaction);

        var library = new Dictionary<string, CardDefinition>(catalog.Cards, StringComparer.Ordinal);
        if (ablateCardId is not null && catalog.Cards.TryGetValue(ablateCardId, out var original))
        {
            library[ablateCardId] = Ablate(original, mode);
        }

        var focalSpec = Spec(focalDeck);
        var foeSpec = Spec(foeDeck);

        var state = focalSeat == 0
            ? MatchSetup.Create(focalSpec, foeSpec, library, Options(seed))
            : MatchSetup.Create(foeSpec, focalSpec, library, Options(seed));

        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(
            flow, null, PunishResponseStances.PolicyFor(PlaystyleRegistry.Default));
        var gateway = new RuntimeMatchGateway("match_axis", state, flow, router, null);
        if (!gateway.Initialize(0).Accepted) return (0, 0, 0, 0, 0, 0);

        var policy = new AdvertisedActionPolicy();
        var steps = 0;
        while (steps++ < 500)
        {
            if (state.WinnerPlayerIndex.HasValue) break;

            var viewer = state.CurrentPlayerIndex;
            var snapshot = gateway.GetSnapshot(viewer);
            if (snapshot.WinnerPlayerIndex.HasValue) break;
            if (snapshot.LegalActions.Count == 0) break;

            var chosen = policy.TryChoose(snapshot, snapshot.CurrentPlayer, false, out var pick) && pick is not null
                ? pick
                : snapshot.LegalActions[0];

            var result = gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, chosen));
            if (!result.Result.Accepted)
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

        return ReadAxes(state, focalSeat);
    }

    private static MatchSetupOptions Options(ulong seed)
        => new MatchSetupOptions
        {
            Seed = seed,
            FirstPlayerIndex = 0,
            OpeningHandSize = 5,
            CastleEnabled = true,
            CastleHealth = 75,
        };

    private static Axes Sweep(string focalFaction, string foeFaction, string? ablateCardId, AblationMode mode)
    {
        var axes = new Axes();
        for (var seedIndex = 0; seedIndex < SeedsPerCard; seedIndex++)
        {
            var seed = (ulong)(7100 + seedIndex);
            var first = PlayOne(focalFaction, foeFaction, ablateCardId, mode, seed, focalSeat: 0);
            var second = PlayOne(focalFaction, foeFaction, ablateCardId, mode, seed, focalSeat: 1);

            axes.OwnDiscarded += first.OwnDiscarded + second.OwnDiscarded;
            axes.OppDiscarded += first.OppDiscarded + second.OppDiscarded;
            axes.Pulls += first.Pulls + second.Pulls;
            axes.SealedHealth += first.Sealed + second.Sealed;
            axes.Turns += first.Turns + second.Turns;
            axes.Finished += first.Finished + second.Finished;
        }

        return axes;
    }

    [Test]
    public void MeasuresEachCardsMarginalContributionToItsDecksAxis()
    {
        // The opponent is held fixed per faction so the ablation is the only variable.
        var matchups = new (string Focal, string Foe, string Axis)[]
        {
            ("sea", "flame", "discard (18)"),
            ("wood", "machine", "sealed health (512)"),
            ("machine", "wood", "pulls (6)"),
        };

        var lines = new List<string>();
        var totalAblations = 0;

        foreach (var (focal, foe, axis) in matchups)
        {
            var deck = Deck(focal);
            var cards = deck.Cards.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
            var baseline = Sweep(focal, foe, null, AblationMode.Empty);
            var matches = SeedsPerCard * 2;

            // Per-match rates, because raw totals depend on how long each match ran and a card that
            // shortens the game would otherwise look like a card that hurts the axis.
            double BaselinePerMatch(int total) => (double)total / matches;
            var baseOppDiscard = BaselinePerMatch(baseline.OppDiscarded);
            var baseSealed = BaselinePerMatch(baseline.SealedHealth);
            var basePull = BaselinePerMatch(baseline.Pulls);

            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "[{0} vs {1}] axis = {2}; baseline over {3} matches: oppDiscarded {4} ({5:F1}/match, threshold {6})",
                focal, foe, axis, matches, baseline.OppDiscarded, baseOppDiscard, axis));
            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "  other baselines: sealed {0} ({1:F1}/match, threshold 512), pulls {2} ({3:F1}/match, threshold 6), turns {4} ({5:F1}/match)",
                baseline.SealedHealth, baseSealed, baseline.Pulls, basePull, baseline.Turns,
                BaselinePerMatch(baseline.Turns)));
            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "  {0,-22} {1,14} {2,13} {3,11} {4,11} {5,10}",
                "ablated card", "d-oppDiscard/m", "d-sealed/m", "d-pull/m", "d-turns/m", "d-finish"));
            lines.Add("  ablation = EFFECT-ONLY: same type, same stats, same punish, effects removed");

            foreach (var cardId in cards)
            {
                totalAblations++;
                var ablated = Sweep(focal, foe, cardId, AblationMode.EffectOnly);
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0,-22} {1,14:F2} {2,13:F1} {3,11:F2} {4,11:F1} {5,10}",
                    cardId,
                    BaselinePerMatch(ablated.OppDiscarded) - baseOppDiscard,
                    BaselinePerMatch(ablated.SealedHealth) - baseSealed,
                    BaselinePerMatch(ablated.Pulls) - basePull,
                    BaselinePerMatch(ablated.Turns) - BaselinePerMatch(baseline.Turns),
                    ablated.Finished - baseline.Finished));
            }

            lines.Add(string.Empty);
        }

        foreach (var line in lines) TestContext.Out.WriteLine(line);

        Assert.That(totalAblations, Is.GreaterThan(0), "the sweep must ablate at least one card");
        Assert.That(
            totalAblations,
            Is.GreaterThanOrEqualTo(60),
            "every card in every measured deck must be ablated (" + totalAblations + ")");
    }
}
}
