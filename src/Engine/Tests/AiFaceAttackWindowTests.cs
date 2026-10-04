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
/// IS A FACE ATTACK EVER AVAILABLE — AND DOES THE POLICY USE IT WHEN IT IS?
///
/// WHY THIS EXISTS. A previous fixture (`AnIncomingLethalIsNotIgnored`) was deleted for being
/// built on an illegal position: it assumed the player has a life pool, assumed life-loss loses
/// the game, and rigged a threat that could not reach the life total anyway. But deleting it
/// also risked burying a REAL observation the reviewer refused to let me drop: in that position
/// the policy was handed attacks and did not use them on the face.
///
/// The narrow, legal form of that question is measured here:
///
///   `AttackTargetPolicy` offers a player's LIFE as an attack target only while that player's
///   leader is UNMANIFESTED **and** their life pool exists. A leader starts in its owner's deck
///   (docs/RULES.md §7), so there is a real opening window with no manifested leader. The
///   question is whether a face attack is advertised in that window, and what the policy does.
///
/// THIS FIXTURE ASSERTS NO DIRECTION. It records counts and prints the raw advertisement, so a
/// reviewer judges whether passing over face attacks is a defect or a defensible ordering. The
/// correction lesson is exactly this: do not turn an unmeasured preference into a claimed defect.
/// </summary>
public sealed class AiFaceAttackWindowTests
{
    private sealed class Sweep
    {
        public int Matches;
        public int DecisionsInWindow;
        public int DecisionsWithAnyAttack;
        public int DecisionsWithFaceAttack;
        public int FaceTaken;
        public int LifePresent;
        public string FirstAttack = string.Empty;
        public readonly List<string> Examples = new List<string>();
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

    [Test]
    public void MeasuresWhetherAFaceAttackIsEverAdvertisedOrTaken()
    {
        // (1) THE SHIPPED CONFIGURATION: no life pool unless a leader grants one.
        var shipped = RunSweep(playerLife: null);

        // (2) THE CONTROL: force a life pool to exist from the start. If the face target is
        // advertised ONLY here, then the shipped rules are what makes it unavailable — which is
        // a statement about the rules, not about the policy. Without this control, "0 face
        // attacks" could equally mean this fixture fails to recognise one.
        var withPool = RunSweep(playerLife: 20);

        TestContext.Out.WriteLine("R7 FACE-ATTACK WINDOW");
        Report("shipped (no life pool by default)", shipped);
        Report("control (life pool forced to 20)", withPool);
        TestContext.Out.WriteLine(
            "R7 READING: a face attack requires the opponent to have a life pool AND no manifested"
            + " leader (AttackTargetPolicy.cs:87). The only thing that creates a life pool is a"
            + " leader declaring grantLife — and that same event manifests the leader. So under the"
            + " shipped rules the two conditions are mutually exclusive and the face target is never"
            + " advertised; the control shows it IS advertised once a pool exists.");

        Assert.That(shipped.Matches, Is.GreaterThan(0), "the unmanifested-leader window must occur");
        Assert.That(
            shipped.DecisionsWithAnyAttack,
            Is.GreaterThan(0),
            "attacks must really be offered in the shipped sweep, or this measures nothing");
        Assert.That(
            withPool.DecisionsWithFaceAttack,
            Is.GreaterThan(0),
            "CONTROL FAILED: with a life pool present the face target must be advertised, otherwise"
            + " this fixture cannot recognise a face attack and the shipped count is meaningless");
    }

    private static void Report(string label, Sweep sweep)
    {
        TestContext.Out.WriteLine("  [{0}]", label);
        TestContext.Out.WriteLine("    matches where a leader was unmanifested: {0}", sweep.Matches);
        TestContext.Out.WriteLine("    ACTION decisions in that window: {0}", sweep.DecisionsInWindow);
        TestContext.Out.WriteLine("    decisions where ANY attack was advertised: {0}", sweep.DecisionsWithAnyAttack);
        TestContext.Out.WriteLine("    decisions where a FACE attack was advertised: {0}", sweep.DecisionsWithFaceAttack);
        TestContext.Out.WriteLine("    decisions where the policy took a face attack: {0}", sweep.FaceTaken);
        TestContext.Out.WriteLine("    decisions where the opponent HAD a life pool: {0}", sweep.LifePresent);
        if (sweep.FirstAttack.Length > 0)
        {
            TestContext.Out.WriteLine("    first advertised ATTACK, verbatim: {0}", sweep.FirstAttack);
        }

        foreach (var line in sweep.Examples) TestContext.Out.WriteLine("    " + line);
    }

    private static Sweep RunSweep(int? playerLife)
    {
        var catalog = Catalog();
        var sweep = new Sweep();

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
                    catalog.Cards,
                    new MatchSetupOptions
                    {
                        Seed = (ulong)seed,
                        FirstPlayerIndex = 0,
                        OpeningHandSize = 5,
                        PlayerLife = playerLife,
                        CastleEnabled = true,
                        CastleHealth = 75,
                    });

                var flow = TurnFlow.CreateDefault();
                var router = TurnActionRouter.CreateDefault(
                    flow, null, PunishResponseStances.PolicyFor(PlaystyleRegistry.Default));
                var gateway = new RuntimeMatchGateway("match_face_window", state, flow, router, null);
                if (!gateway.Initialize(0).Accepted) continue;

                var policy = new AdvertisedActionPolicy();
                var steps = 0;
                var sawWindow = false;

                while (steps++ < 400)
                {
                    if (state.WinnerPlayerIndex.HasValue) break;

                    var viewer = state.CurrentPlayerIndex;
                    var snapshot = gateway.GetSnapshot(viewer);
                    if (snapshot.WinnerPlayerIndex.HasValue) break;

                    var actor = snapshot.CurrentPlayer;
                    var foe = state.GetOpponent(actor);

                    if (foe.Leader is null && string.Equals(snapshot.Phase, "ACTION", StringComparison.Ordinal))
                    {
                        sawWindow = true;
                        sweep.DecisionsInWindow++;
                        if (foe.Life.HasValue) sweep.LifePresent++;

                        var attacks = snapshot.LegalActions
                            .Where(a => string.Equals(a.Type, "ATTACK", StringComparison.Ordinal))
                            .ToList();
                        if (attacks.Count > 0)
                        {
                            sweep.DecisionsWithAnyAttack++;
                            if (sweep.FirstAttack.Length == 0) sweep.FirstAttack = Describe(attacks[0]);
                        }

                        var faceAttacks = attacks.Where(IsFaceAttack).ToList();
                        var chosen = policy.TryChoose(snapshot, actor, false, out var pick) && pick is not null
                            ? pick
                            : snapshot.LegalActions.FirstOrDefault();

                        if (chosen is not null && faceAttacks.Count > 0)
                        {
                            sweep.DecisionsWithFaceAttack++;
                            if (IsFaceAttack(chosen))
                            {
                                sweep.FaceTaken++;
                            }
                            else if (sweep.Examples.Count < 5)
                            {
                                sweep.Examples.Add(
                                    "declined " + pair[0] + " vs " + pair[1] + " seed " + seed
                                    + " turn " + snapshot.Turn + ": " + faceAttacks.Count
                                    + " face attack(s) advertised ["
                                    + string.Join(", ", faceAttacks.Take(3).Select(a => Describe(a)))
                                    + "], chose " + chosen.Type + "/" + chosen.CardId
                                    + "; my board attack " + BoardAttack(state, actor)
                                    + " vs their life "
                                    + (foe.Life?.ToString(CultureInfo.InvariantCulture) ?? "none"));
                            }
                        }
                    }

                    var submit = policy.TryChoose(snapshot, actor, false, out var choice) && choice is not null
                        ? choice
                        : snapshot.LegalActions.FirstOrDefault();
                    if (submit is null) break;

                    var result = gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, submit));
                    if (!result.Result.Accepted)
                    {
                        var alternative = snapshot.LegalActions
                            .FirstOrDefault(a => !string.Equals(a.ActionId, submit.ActionId, StringComparison.Ordinal));
                        if (alternative is null
                            || !gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, alternative)).Result.Accepted)
                        {
                            break;
                        }
                    }
                }

                if (sawWindow) sweep.Matches++;
            }
        }

        return sweep;
    }

    /// <summary>
    /// Whether an advertised ATTACK aims at the opponent's LIFE rather than at an entity.
    ///
    /// THE WIRE FORM, read from the projector rather than guessed
    /// (`RuntimeContractV131Snapshot.TargetId`): the adapter maps the engine's
    /// `core:player_N:life` reference to the literal string <c>player_N</c>, maps
    /// `core:shared_castle` to <c>castle</c>, and maps an entity target to a POSITIVE NUMERIC id.
    /// An earlier version of this predicate looked for <c>":life"</c> and therefore recognised
    /// nothing — which is exactly the failure a control condition exists to catch, and it did.
    /// </summary>
    private static bool IsFaceAttack(RuntimeLegalAction action)
    {
        if (action is null || !string.Equals(action.Type, "ATTACK", StringComparison.Ordinal)) return false;
        if (action.TargetId is null) return false;

        var target = action.TargetId.ToString();
        if (string.IsNullOrEmpty(target)) return false;

        // A numeric target is an entity (a minion or a manifested leader).
        if (long.TryParse(target, NumberStyles.None, CultureInfo.InvariantCulture, out _)) return false;

        // `castle` is the shared royal castle; anything else non-numeric is a player core.
        if (string.Equals(target, "castle", StringComparison.Ordinal)) return false;
        return target.StartsWith("player_", StringComparison.Ordinal);
    }

    /// <summary>The advertisement's own shape, so the target encoding is recorded not assumed.</summary>
    private static string Describe(RuntimeLegalAction action)
    {
        var payload = action.Payload is null
            ? "{}"
            : "{" + string.Join(", ", action.Payload.Select(
                e => e.Key + "=" + (e.Value?.ToString() ?? "null"))) + "}";
        return "type=" + action.Type
            + " source=" + (action.SourceId?.ToString() ?? "null")
            + " target=" + (action.TargetId?.ToString() ?? "null")
            + " card=" + (action.CardId ?? "null")
            + " payload=" + payload;
    }

    private static int BoardAttack(GameState state, int playerIndex)
    {
        var total = 0;
        foreach (var card in state.GetPlayer(playerIndex).Field) total += card.Attack;
        return total;
    }
}
}
