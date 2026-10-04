using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DominionWars.Adapters;
using DominionWars.Adapters.Ai;
using DominionWars.Data;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Rules;
using DominionWars.Engine.Setup;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// Acceptance coverage for the multi-playstyle AI weight layer
/// (docs/PL_AI_PLAYSTYLE_FRAMEWORK_2026-09-11.md §6.1-§6.3, §6.5-§6.6).
///
/// The load-bearing test here is
/// <see cref="PlaystylesDecideDifferentlyOnSyntheticAdvertisements"/>: it does
/// not compare weight tables, it asserts that different playstyles make
/// DIFFERENT decisions on advertisement sets where a real choice exists. If
/// that failed, "run several AIs" would buy nothing. Everything else pins the
/// three boundary invariants (advertised actions only, no GameState, no RNG or
/// clock) and the fact that the shipped default policy still makes exactly the
/// decisions it made before this layer existed.
/// </summary>
[TestFixture]
public sealed class PlaystyleTests
{
    /// <summary>The acting player is the AI under test; player 0 is its opponent.</summary>
    private const int Ai = 1;
    private const int Enemy = 0;
    private const long Revision = 7;
    private const string MatchId = "match_playstyle_unit";

    /// <summary>Entity ids used by the synthetic advertisements.</summary>
    private const long Attacker = 401;
    private const long EnemyMinion = 501;
    private const long EnemyLeader = 502;

    // =====================================================================
    // §6.3 The premise: playstyles must make DIFFERENT decisions.
    // =====================================================================

    /// <summary>
    /// Every row is (synthetic advertisement set, playstyle, the action that
    /// playstyle must select). The expectations are not "whatever the code
    /// did": each one follows from the feature differences the weight tables
    /// price, and the driving feature is named in
    /// <see cref="DistinguishingFeature"/>. The sets are ordered so the
    /// advertised position cannot accidentally decide a case except where the
    /// shipped default is SUPPOSED to be order-sensitive (it is blind to punish
    /// and to face-versus-trade, which is exactly what the retired policy was).
    /// </summary>
    private static IEnumerable<TestCaseData> DecisionCases()
    {
        foreach (var set in DecisionSets)
        {
            foreach (var playstyle in PlaystyleRegistry.All)
            {
                var expected = ExpectedChoice(set, playstyle.Id);
                yield return new TestCaseData(set, playstyle.Id, expected)
                    .SetName(
                        "PlaystylesDecideDifferently(" + set + ", " + playstyle.Id
                        + " -> " + expected + ")");
            }
        }
    }

    [TestCaseSource(nameof(DecisionCases))]
    public void PlaystylesDecideDifferentlyOnSyntheticAdvertisements(
        string setName,
        string playstyleId,
        string expectedActionId)
    {
        var snapshot = SnapshotFor(setName);
        var chosen = Choose(PlaystyleRegistry.GetPlaystyle(playstyleId), snapshot);

        Assert.That(
            chosen.ActionId,
            Is.EqualTo(expectedActionId),
            Describe(setName, playstyleId));
    }

    /// <summary>
    /// The audit form of the same premise: each playstyle's decision signature
    /// (its choice on every synthetic set) must be unique. "The weight tables
    /// differ" is not evidence; "no two playstyles decide the same way on all
    /// of these sets" is.
    /// </summary>
    [Test]
    public void EveryPlaystyleHasAUniqueDecisionSignature()
    {
        var signatures = new Dictionary<string, string>(StringComparer.Ordinal);
        var report = new List<string>();

        foreach (var playstyle in PlaystyleRegistry.All)
        {
            var parts = new List<string>();
            foreach (var set in DecisionSets)
            {
                var chosen = Choose(playstyle, SnapshotFor(set));
                parts.Add(set + "=" + chosen.ActionId);
            }

            var signature = string.Join("|", parts);
            report.Add(playstyle.Id + ": " + signature);

            foreach (var other in signatures)
            {
                Assert.That(
                    signature,
                    Is.Not.EqualTo(other.Value),
                    "playstyles '" + playstyle.Id + "' and '" + other.Key
                    + "' make identical decisions on every synthetic set, so the matrix"
                    + " would only measure one AI twice: " + signature);
            }

            signatures[playstyle.Id] = signature;
        }

        TestContext.Out.WriteLine(
            "playstyle decision signatures ({0} playstyles x {1} sets):\n{2}",
            PlaystyleRegistry.All.Count,
            DecisionSets.Length,
            string.Join("\n", report));
    }

    /// <summary>
    /// The decisive pair the design doc names explicitly: with an attack on the
    /// enemy core and an attack that trades a board card both advertised, the
    /// damage-first playstyle must take the core and the board-first playstyle
    /// must take the trade. This is the claim that a single AI cannot make.
    /// </summary>
    [Test]
    public void AggroTakesTheCoreWhereControlTakesTheTrade()
    {
        var snapshot = SnapshotFor(FaceFirstSet);

        Assert.Multiple(() =>
        {
            Assert.That(
                Choose(PlaystyleRegistry.GetPlaystyle(PlaystyleRegistry.AggroId), snapshot).ActionId,
                Is.EqualTo("attack_face"),
                "aggro weights face (+500) over trade (+50)");
            Assert.That(
                Choose(PlaystyleRegistry.GetPlaystyle(PlaystyleRegistry.ControlId), snapshot).ActionId,
                Is.EqualTo("attack_trade"),
                "control weights trade (+600) over face (0)");
        });
    }

    /// <summary>
    /// The second decisive pair: the default (and every other playstyle in this
    /// set) plays a card, while the chain playstyle commits because a commit is
    /// the setup step of the win axis it serves.
    /// </summary>
    [Test]
    public void ComboCommitsWhereTheDefaultPlaysACard()
    {
        var snapshot = SnapshotFor(ChainSet);

        Assert.Multiple(() =>
        {
            Assert.That(
                Choose(PlaystyleRegistry.Default, snapshot).ActionId,
                Is.EqualTo("play_chain"),
                "the shipped priority ranks a card play above a commit");
            Assert.That(
                Choose(PlaystyleRegistry.GetPlaystyle(PlaystyleRegistry.ComboId), snapshot).ActionId,
                Is.EqualTo("commit_chain"),
                "combo weights the lifecycle chain above board development");
        });
    }

    /// <summary>
    /// The third decisive pair: the default is punish-blind, so with two equal
    /// plays advertised it takes the first one; the budget-conscious playstyle
    /// takes the cheap one even though it is advertised second.
    /// </summary>
    [Test]
    public void EconomyTakesTheCheapPunishWhereTheDefaultTakesTheFirstAdvertised()
    {
        var snapshot = SnapshotFor(PunishSet);

        Assert.Multiple(() =>
        {
            Assert.That(
                Choose(PlaystyleRegistry.Default, snapshot).ActionId,
                Is.EqualTo("play_expensive"),
                "the shipped default has no punish weight, so the advertised order decides");
            Assert.That(
                Choose(PlaystyleRegistry.GetPlaystyle(PlaystyleRegistry.EconomyId), snapshot).ActionId,
                Is.EqualTo("play_cheap"),
                "economy has the largest punish weight (-12 per punish point)");
        });
    }

    /// <summary>
    /// The fourth decisive pair: with a resolvable download and a card play both
    /// advertised, the default and aggro complete the download while the
    /// tempo playstyle develops the board instead - the classic "different
    /// mistake" the matrix is supposed to surface.
    /// </summary>
    [Test]
    public void TempoPlaysACardWhereTheDefaultCompletesTheDownload()
    {
        var snapshot = SnapshotFor(AxisVsBoardSet);

        Assert.Multiple(() =>
        {
            Assert.That(
                Choose(PlaystyleRegistry.Default, snapshot).ActionId,
                Is.EqualTo("pull_axis"),
                "the shipped priority ranks a resolvable download first");
            Assert.That(
                Choose(PlaystyleRegistry.GetPlaystyle(PlaystyleRegistry.TempoId), snapshot).ActionId,
                Is.EqualTo("play_axis"),
                "tempo discounts the long download axis (win_axis +100) below board development (+300)");
        });
    }

    // =====================================================================
    // §6.2 The default playstyle must reproduce the retired behaviour.
    // =====================================================================

    /// <summary>
    /// The shipped default's weight vector must order the advertised
    /// ACTION-phase actions exactly as the retired hardcoded rank did. This
    /// enumerates every ACTION-phase subset of the six advertised types, three
    /// advertised orderings, and both cloud-stack states with and without a
    /// download selection, and compares the weight order against a frozen local
    /// copy of the retired algorithm - so the equivalence does not run through
    /// the new code.
    /// </summary>
    [Test]
    public void DefaultWeightsOrderAdvertisedActionsExactlyLikeTheRetiredRank()
    {
        var policy = new AdvertisedActionPolicy();
        var cases = 0;
        var comparisons = 0;

        foreach (var snapshot in ActionCrossProduct())
        {
            cases++;
            var stable = StableActions(snapshot.LegalActions);
            var legacyOrder = LegacyUsableOrder(snapshot, stable, policy)
                .Select(action => action.ActionId)
                .ToArray();
            var weightOrder = WeightedPlaystyle.Order(PlaystyleRegistry.Default, snapshot, stable)
                .Select(action => action.ActionId)
                .ToArray();

            Assert.That(
                weightOrder,
                Is.EqualTo(legacyOrder),
                "the default weights must reproduce the retired rank order for "
                + Describe(snapshot));

            var advertisedOrder = policy.OrderAdvertisedActions(snapshot, snapshot.LegalActions)
                .Select(action => action.ActionId)
                .ToArray();
            Assert.That(
                advertisedOrder,
                Is.EqualTo(legacyOrder),
                "OrderAdvertisedActions must still publish the retired order for "
                + Describe(snapshot));

            Assert.That(
                Choose(PlaystyleRegistry.Default, snapshot).ActionId,
                Is.EqualTo(LegacyChoose(snapshot, policy).ActionId),
                "the default playstyle must submit what the retired policy submitted for "
                + Describe(snapshot));

            comparisons += 3;
        }

        TestContext.Out.WriteLine(
            "default/default-equivalence: {0} ACTION-phase advertisement sets, {1} comparisons, 0 differences",
            cases,
            comparisons);
        Assert.That(cases, Is.GreaterThan(600), "the cross product must stay exhaustive");
    }

    /// <summary>
    /// The wiring proof: a policy constructed without a playstyle is the default
    /// weight vector, and a policy constructed with it explicitly behaves the
    /// same, so nothing else (Unity's facade, the turn coordinator, the tests)
    /// silently changed behaviour.
    /// </summary>
    [Test]
    public void PolicyWithoutAPlaystyleIsTheShippedDefaultWeightVector()
    {
        var implicitDefault = new AdvertisedActionPolicy();
        var explicitDefault = new AdvertisedActionPolicy(PlaystyleRegistry.Default);

        Assert.That(implicitDefault.Playstyle.Id, Is.EqualTo(PlaystyleRegistry.DefaultId));
        Assert.That(
            implicitDefault.Playstyle.Weights,
            Is.EquivalentTo(PlaystyleRegistry.Default.Weights));

        foreach (var set in DecisionSets)
        {
            var snapshot = SnapshotFor(set);
            var expected = Choose(implicitDefault.Playstyle, snapshot).ActionId;
            Assert.Multiple(() =>
            {
                Assert.That(Choose(implicitDefault, snapshot).ActionId, Is.EqualTo(expected), set);
                Assert.That(Choose(explicitDefault, snapshot).ActionId, Is.EqualTo(expected), set);
            });
        }
    }

    // =====================================================================
    // §6.1 The invariants: advertised actions only, and no GameState.
    // =====================================================================

    /// <summary>
    /// Whatever a playstyle returns must be an element of the advertised set,
    /// unchanged field for field, and it must survive the boundary that the
    /// engine validates submissions with. This mirrors
    /// <c>AiLifecyclePolicyTests.EverySubmittedActionMatchesItsAdvertisementFieldForField</c>.
    /// </summary>
    [Test]
    public void EveryPlaystyleReturnsAnAdvertisedActionUnchangedFieldForField()
    {
        var playstyles = PlaystyleRegistry.All;
        var compared = 0;

        foreach (var snapshot in ActionCrossProduct().Concat(DecisionSnapshots()))
        {
            foreach (var playstyle in playstyles)
            {
                var chosen = Choose(playstyle, snapshot);
                var advertised = snapshot.LegalActions
                    .FirstOrDefault(action => ReferenceEquals(action, chosen));
                Assert.That(
                    advertised,
                    Is.Not.Null,
                    playstyle.Id + " must return the advertised instance itself, not a copy: " + Describe(snapshot));

                var wire = AdvertisedActionPolicy.ToGameAction(snapshot, chosen);
                Assert.Multiple(() =>
                {
                    Assert.That(wire.ActionId, Is.EqualTo(advertised!.ActionId));
                    Assert.That(wire.Type, Is.EqualTo(advertised.Type));
                    Assert.That(wire.Actor, Is.EqualTo(advertised.Actor));
                    Assert.That(wire.CardId, Is.EqualTo(advertised.CardId));
                    Assert.That(
                        RuntimeActionBoundary.Validate(wire, snapshot).Accepted,
                        Is.True,
                        playstyle.Id + ": the boundary must accept the action exactly as advertised");
                    Assert.That(ValuesEqual(wire.SourceId, advertised.SourceId), Is.True,
                        advertised.ActionId + ": source id must be forwarded unchanged");
                    Assert.That(ValuesEqual(wire.TargetId, advertised.TargetId), Is.True,
                        advertised.ActionId + ": target id must be forwarded unchanged");
                    Assert.That(ValuesEqual(wire.Payload, advertised.Payload), Is.True,
                        advertised.ActionId + ": payload must be forwarded unchanged");
                });

                compared++;
            }
        }

        TestContext.Out.WriteLine(
            "invariant: {0} playstyle/action-set pairs submitted an action that is an advertised element, field for field",
            compared);
    }

    /// <summary>
    /// The phase rules (a forced end turn, never setting an ambush, the forced
    /// discard) are phase decisions, not playstyle decisions, so no weight
    /// table can change them. That matters because a playstyle can never supply
    /// a payload for those phases.
    /// </summary>
    [Test]
    public void PhaseRulesAreIdenticalForEveryPlaystyle()
    {
        foreach (var playstyle in PlaystyleRegistry.All)
        {
            var policy = new AdvertisedActionPolicy(playstyle);

            var action = Snapshot(new[] { Play("play_1", "wood_probe", 1), EndTurn() });
            Assert.That(policy.TryChoose(action, Ai, true, out var chosen), Is.True);
            Assert.That(chosen!.Type, Is.EqualTo(AdvertisedActionPolicy.EndTurnAction),
                playstyle.Id + ": a forced end turn is a host budget decision");

            var ambush = AmbushSnapshot();
            Assert.That(policy.TryChoose(ambush, Ai, false, out var skipped), Is.True);
            Assert.That(skipped!.Type, Is.EqualTo(AdvertisedActionPolicy.SkipAmbushAction),
                playstyle.Id + ": the shipped baseline never commits an ambush, whatever it values");

            var discard = DiscardSnapshot();
            Assert.That(policy.TryChoose(discard, Ai, false, out var discarded), Is.True);
            Assert.That(discarded!.ActionId, Is.EqualTo("discard_2"),
                playstyle.Id + ": the forced discard is forwarded verbatim");
        }
    }

    /// <summary>
    /// <see cref="ActionFeatures"/> must be a pure function of the advertised
    /// action plus the viewer-safe snapshot: identical output when called twice,
    /// no shared mutable state, no effect on its inputs, and every published
    /// feature present exactly once with a non-negative value.
    /// </summary>
    [Test]
    public void ActionFeaturesAreAPureFunctionOfTheAdvertisementAndTheSnapshot()
    {
        var probes = FeatureProbes();
        var calls = 0;

        foreach (var probe in probes)
        {
            var advertisedBefore = probe.Snapshot.LegalActions.Count;
            var firstReference = probe.Snapshot.LegalActions[0];

            var first = ActionFeatures.Of(probe.Snapshot, probe.Action);
            var second = ActionFeatures.Of(probe.Snapshot, probe.Action);

            Assert.Multiple(() =>
            {
                Assert.That(second.Count, Is.EqualTo(ActionFeatures.Names.Count));
                Assert.That(second.Keys, Is.EquivalentTo(ActionFeatures.Names));
                foreach (var feature in ActionFeatures.Names)
                {
                    Assert.That(
                        second[feature],
                        Is.EqualTo(first[feature]),
                        probe.Name + ": feature '" + feature + "' must not vary between calls");
                    Assert.That(second[feature], Is.GreaterThanOrEqualTo(0),
                        probe.Name + ": feature '" + feature + "' must be a small non-negative integer");
                }

                Assert.That(
                    ActionFeatures.Value(probe.Snapshot, probe.Action, ActionFeatures.Damage),
                    Is.EqualTo(second[ActionFeatures.Damage]),
                    probe.Name + ": Value must agree with the vector");
                Assert.That(
                    ActionFeatures.Value(probe.Snapshot, probe.Action, "not_a_feature"),
                    Is.EqualTo(0),
                    probe.Name + ": an unpublished name reads as 0");
            });

            // Mutating the returned vector must not leak into the next call.
            if (first is IDictionary<string, int> mutable) mutable[ActionFeatures.Face] = 999;
            var third = ActionFeatures.Of(probe.Snapshot, probe.Action);
            Assert.That(
                third[ActionFeatures.Face],
                Is.EqualTo(second[ActionFeatures.Face]),
                probe.Name + ": each call must return a fresh vector");

            Assert.Multiple(() =>
            {
                Assert.That(probe.Snapshot.LegalActions.Count, Is.EqualTo(advertisedBefore));
                Assert.That(ReferenceEquals(probe.Snapshot.LegalActions[0], firstReference), Is.True,
                    probe.Name + ": feature extraction must not touch the advertised set");
            });

            calls++;
        }

        TestContext.Out.WriteLine(
            "purity: {0} probes x 3 calls, identical vectors, fresh instances, inputs untouched",
            calls);
    }

    /// <summary>
    /// The features must depend only on the snapshot fields the weight tables
    /// document: changing unrelated snapshot data (turn number, hand and deck
    /// counts, castle health, the opponent's board) must not move the vector,
    /// while the one documented field (the actor's cloud stack) must.
    /// </summary>
    [Test]
    public void FeatureVectorOnlyReadsTheDocumentedSnapshotFields()
    {
        var action = Pull("pull_probe", 2);
        var baseline = Snapshot(new[] { action }, cloudStackCount: 3);
        var unrelated = Snapshot(new[] { action }, cloudStackCount: 3, turn: 99);
        unrelated.Players[Enemy].HandCount = 99;
        unrelated.Players[Enemy].DeckCount = 99;
        unrelated.Players[Ai].HandCount = 0;
        unrelated.Castle.Health = 1;
        unrelated.Players[Enemy].FieldCount = 7;

        var emptied = Snapshot(new[] { action }, cloudStackCount: 0);

        Assert.Multiple(() =>
        {
            Assert.That(
                ActionFeatures.Of(unrelated, action),
                Is.EquivalentTo(ActionFeatures.Of(baseline, action)),
                "unrelated snapshot data must not reach the feature vector");
            Assert.That(ActionFeatures.Of(baseline, action)[ActionFeatures.WinAxis], Is.EqualTo(1));
            Assert.That(ActionFeatures.Of(emptied, action)[ActionFeatures.WinAxis], Is.EqualTo(0),
                "an empty cloud stack means the advertised download cannot advance the win axis");
        });
    }

    /// <summary>
    /// The strong form of "never touches GameState": this type is checked at the
    /// member-signature level (no parameter, return, property, or field type may
    /// come from the engine assembly that declares GameState, and the public
    /// input types must be exactly the advertisement and the snapshot) and at
    /// the IL level (no member reference inside its method bodies may resolve
    /// into that assembly). The scanner reports how much IL it actually
    /// inspected, and proves itself non-vacuous in the same test by finding the
    /// engine reference this test file itself makes.
    /// </summary>
    [Test]
    public void ActionFeaturesCannotReachGameStateByTypeOrByBodyReference()
    {
        var engine = typeof(GameState).Assembly;
        Assert.That(engine.GetName().Name, Is.EqualTo("DominionWars.Engine"),
            "premise: GameState lives in the engine assembly");

        var signatureTypes = SignatureTypes(typeof(ActionFeatures)).ToArray();
        var engineSignatureTypes = signatureTypes.Where(type => type.Assembly == engine).ToArray();
        var features = ScanBodyReferences(typeof(ActionFeatures), engine);
        var self = ScanBodyReferences(typeof(PlaystyleTests), engine);

        var allowedInputs = new[] { typeof(RuntimeSnapshotEnvelope), typeof(RuntimeLegalAction), typeof(string) };
        foreach (var method in typeof(ActionFeatures)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            foreach (var parameter in method.GetParameters())
            {
                Assert.That(
                    allowedInputs,
                    Does.Contain(parameter.ParameterType),
                    method.Name + " may only take the advertisement, the snapshot, or a literal name");
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                engineSignatureTypes,
                Is.Empty,
                "no signature of ActionFeatures may mention a type from " + engine.FullName);
            Assert.That(
                features.ForeignReferences,
                Is.Empty,
                "no method body of ActionFeatures may reference a member of " + engine.FullName);
            Assert.That(
                features.BodiesScanned,
                Is.GreaterThan(0),
                "the scanner must actually walk ActionFeatures' method bodies");
            Assert.That(
                features.MemberTokensInspected,
                Is.GreaterThan(0),
                "the IL walk must stay in step long enough to resolve member references;"
                + " 0 would mean the scan proved nothing");
            Assert.That(
                features.DesynchronisedBodies,
                Is.Zero,
                "every ActionFeatures body must be walked to the end");
            Assert.That(
                self.ForeignReferences,
                Has.Some.Contains("GameState"),
                "the IL scanner must not be vacuous: it has to find this test's own GameState reference");
        });

        TestContext.Out.WriteLine(
            "purity check on ActionFeatures: {0} signature types ({1} declared in Adapters),"
            + " {2} method bodies walked, {3} member tokens resolved ({4} unresolved),"
            + " {5} references from DominionWars.Engine, {6} bodies not walked to the end;"
            + " scanner self-test on this test class: {7} bodies, {8} member tokens, engine references {9}",
            signatureTypes.Length,
            signatureTypes.Count(type => type.Assembly == typeof(ActionFeatures).Assembly),
            features.BodiesScanned,
            features.MemberTokensInspected,
            features.UnresolvedTokens,
            features.ForeignReferences.Count,
            features.DesynchronisedBodies,
            self.BodiesScanned,
            self.MemberTokensInspected,
            string.Join(",", self.ForeignReferences.Take(3)));
    }

    /// <summary>
    /// Face versus trade must be decidable from the advertisement plus the
    /// snapshot alone, including the three core shapes the v1.31 projection can
    /// emit and the honest limitation that a numeric target outside the visible
    /// board is not a trade the snapshot can see.
    /// </summary>
    [Test]
    public void AttackFeaturesSeparateCoreTargetsFromBoardTrades()
    {
        var snapshot = Snapshot(
            new[] { EndTurn() },
            enemyField: new[] { BoardCard(EnemyMinion, "wood_minion"), BoardCard(EnemyLeader, "wood_leader", leader: true) },
            enemyLeaderZone: new[] { BoardCard(700, "wood_landmark") });

        var cases = new (string Name, object? Target, int Face, int Trade, int Damage)[]
        {
            ("enemy life", "player_0", 1, 0, 1),
            ("shared royal castle", "castle", 1, 0, 1),
            ("enemy leader core", "leader_0", 1, 0, 1),
            ("legacy core reference", "core:player_0:life", 1, 0, 1),
            ("visible enemy minion", EnemyMinion, 0, 1, 1),
            ("visible enemy leader entity", EnemyLeader, 0, 1, 1),
            ("visible enemy leader zone entity", 700L, 0, 1, 1),
            ("target the snapshot cannot see", 4242L, 1, 0, 1),
            ("no target at all", null, 0, 0, 1),
        };

        foreach (var item in cases)
        {
            var action = Attack("attack_" + item.Name, item.Target);
            var features = ActionFeatures.Of(snapshot, action);
            Assert.Multiple(() =>
            {
                Assert.That(features[ActionFeatures.Face], Is.EqualTo(item.Face), item.Name + ": face");
                Assert.That(features[ActionFeatures.Trade], Is.EqualTo(item.Trade), item.Name + ": trade");
                Assert.That(features[ActionFeatures.Damage], Is.EqualTo(item.Damage), item.Name + ": damage");
            });
        }

        var pull = ActionFeatures.Of(Snapshot(new[] { Pull("pull_probe", 2) }, cloudStackCount: 2), Pull("pull_probe", 2));
        Assert.Multiple(() =>
        {
            Assert.That(pull[ActionFeatures.Face], Is.EqualTo(0), "only an attack can be a face attack");
            Assert.That(pull[ActionFeatures.Trade], Is.EqualTo(0), "only an attack can be a trade");
            Assert.That(pull[ActionFeatures.Lifecycle], Is.EqualTo(1));
            Assert.That(pull[ActionFeatures.WinAxis], Is.EqualTo(1));
        });

        TestContext.Out.WriteLine(
            "attack classification: {0} target shapes pinned, plus the non-attack control",
            cases.Length);
    }

    /// <summary>
    /// The advertised amounts the budget-conscious playstyles price must come
    /// from the advertisement itself, and a malformed or negative amount must
    /// read as 0 rather than turn a declared cost into a benefit.
    /// </summary>
    [Test]
    public void AdvertisedAmountFeaturesComeFromTheAdvertisementOnly()
    {
        var snapshot = Snapshot(new[] { EndTurn() });

        var rich = Play("play_rich", "wood_flood", 12, discardRequired: 3);
        var richFeatures = ActionFeatures.Of(snapshot, rich);
        var stringAmounts = Play("play_strings", "wood_flood", 4);
        stringAmounts.Payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["punish"] = "4",
            ["discardRequired"] = 2L,
        };
        var stringFeatures = ActionFeatures.Of(snapshot, stringAmounts);
        var broken = Play("play_broken", "wood_flood", 0);
        broken.Payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["punish"] = -5,
            ["discardRequired"] = "not a number",
        };
        var brokenFeatures = ActionFeatures.Of(snapshot, broken);

        Assert.Multiple(() =>
        {
            Assert.That(richFeatures[ActionFeatures.Punish], Is.EqualTo(12));
            Assert.That(richFeatures[ActionFeatures.DiscardCost], Is.EqualTo(3));
            Assert.That(stringFeatures[ActionFeatures.Punish], Is.EqualTo(4),
                "a wire amount may arrive as a string after serialization");
            Assert.That(stringFeatures[ActionFeatures.DiscardCost], Is.EqualTo(2));
            Assert.That(brokenFeatures[ActionFeatures.Punish], Is.EqualTo(0),
                "a negative advertised punish must not become a bonus");
            Assert.That(brokenFeatures[ActionFeatures.DiscardCost], Is.EqualTo(0));
            Assert.That(ActionFeatures.Of(snapshot, EndTurn())[ActionFeatures.Punish], Is.EqualTo(0),
                "an advertisement without a punish payload has no punish cost");
        });
    }

    // =====================================================================
    // §6.5 Determinism, and the scoring contract itself.
    // =====================================================================

    /// <summary>
    /// Same snapshot plus same advertised set must produce the same choice every
    /// time, for every playstyle, with no state carried between calls.
    /// </summary>
    [Test]
    public void ChoicesAreDeterministicAcrossRepeatedRuns()
    {
        const int repetitions = 25;
        var checks = 0;

        foreach (var set in DecisionSets)
        {
            foreach (var playstyle in PlaystyleRegistry.All)
            {
                var snapshot = SnapshotFor(set);
                var first = Choose(playstyle, snapshot).ActionId;
                var firstOrder = WeightedPlaystyle.Order(playstyle, snapshot, snapshot.LegalActions)
                    .Select(action => action.ActionId)
                    .ToArray();

                var fresh = PlaystyleRegistry.GetPlaystyle(playstyle.Id);
                for (var run = 0; run < repetitions; run++)
                {
                    Assert.That(Choose(playstyle, snapshot).ActionId, Is.EqualTo(first),
                        playstyle.Id + " on " + set + " must not depend on call count");
                    Assert.That(Choose(fresh, snapshot).ActionId, Is.EqualTo(first),
                        playstyle.Id + " on " + set + " must not depend on instance identity");
                    checks += 2;
                }

                Assert.That(
                    WeightedPlaystyle.Order(playstyle, snapshot, snapshot.LegalActions)
                        .Select(action => action.ActionId)
                        .ToArray(),
                    Is.EqualTo(firstOrder),
                    playstyle.Id + " on " + set + ": the whole order must be stable");
            }
        }

        TestContext.Out.WriteLine(
            "determinism: {0} repeated choices across {1} playstyles x {2} sets, 0 differences",
            checks,
            PlaystyleRegistry.All.Count,
            DecisionSets.Length);
    }

    /// <summary>
    /// A score must be exactly the weighted sum of the published features, and
    /// the selected action must be the argmax of that score - so a report can
    /// always explain a decision by showing its feature contributions.
    /// </summary>
    [Test]
    public void ScoresAreTheWeightedSumOfPublishedFeaturesAndTheChoiceIsItsArgmax()
    {
        var checks = 0;

        foreach (var snapshot in DecisionSnapshots())
        {
            foreach (var playstyle in PlaystyleRegistry.All)
            {
                var scored = snapshot.LegalActions
                    .Select(action => new
                    {
                        Action = action,
                        Score = ManualScore(playstyle, snapshot, action),
                    })
                    .ToArray();

                foreach (var entry in scored)
                {
                    Assert.That(
                        WeightedPlaystyle.WeightedScore(playstyle, snapshot, entry.Action),
                        Is.EqualTo(entry.Score),
                        playstyle.Id + " / " + entry.Action.ActionId + ": score must be the weighted feature sum");
                    checks++;
                }

                var best = scored.Max(entry => entry.Score);
                var chosen = Choose(playstyle, snapshot);
                Assert.That(
                    ManualScore(playstyle, snapshot, chosen),
                    Is.EqualTo(best),
                    playstyle.Id + ": the submitted action must be an argmax");
                checks++;
            }
        }

        TestContext.Out.WriteLine("scoring contract: {0} score/argmax checks against the published feature vector", checks);
    }

    /// <summary>
    /// The documented tie-break, made observable: with equal scores the action
    /// the engine advertised first wins, and when neither action is in the
    /// advertised list the order falls through to ActionId and then Type - a
    /// total order, so the result never depends on sort stability.
    /// </summary>
    [Test]
    public void TiesFollowTheDocumentedTotalOrder()
    {
        var cheapFirst = Snapshot(new[]
        {
            Play("play_cheap", "wood_probe", 1),
            Play("play_expensive", "wood_flood", 6),
        });
        var expensiveFirst = Snapshot(new[]
        {
            Play("play_expensive", "wood_flood", 6),
            Play("play_cheap", "wood_probe", 1),
        });

        Assert.Multiple(() =>
        {
            Assert.That(Choose(PlaystyleRegistry.Default, cheapFirst).ActionId, Is.EqualTo("play_cheap"),
                "with equal scores the default takes the first advertised action");
            Assert.That(Choose(PlaystyleRegistry.Default, expensiveFirst).ActionId, Is.EqualTo("play_expensive"),
                "and it follows the advertisement when the order flips");
            Assert.That(
                WeightedPlaystyle.Order(PlaystyleRegistry.Default, expensiveFirst, expensiveFirst.LegalActions)
                    .Select(action => action.ActionId)
                    .ToArray(),
                Is.EqualTo(new[] { "play_expensive", "play_cheap" }),
                "equal scores keep the advertised order");
        });

        // Neither action is advertised: the advertised index is unavailable, so
        // ActionId then Type decide.
        var stranger = Snapshot(new[] { EndTurn() });
        var unadvertised = new[]
        {
            Play("play_zeta", "wood_probe", 1),
            Play("play_alpha", "wood_probe", 1),
        };
        var order = WeightedPlaystyle.Order(PlaystyleRegistry.Default, stranger, unadvertised)
            .Select(action => action.ActionId)
            .ToArray();
        Assert.That(order, Is.EqualTo(new[] { "play_alpha", "play_zeta" }),
            "when nothing is advertised the ActionId ordinal decides");
    }

    // =====================================================================
    // The registry itself.
    // =====================================================================

    /// <summary>
    /// The shipped playstyles must be discoverable by id, and every weight table
    /// must be complete over the published feature names - a misspelled or
    /// missing weight is a loud failure, not a silent no-op.
    /// </summary>
    [Test]
    public void RegistryPublishesCompleteAndValidatedWeightTables()
    {
        var expectedIds = new[]
        {
            PlaystyleRegistry.DefaultId,
            PlaystyleRegistry.AggroId,
            PlaystyleRegistry.ControlId,
            PlaystyleRegistry.ComboId,
            PlaystyleRegistry.EconomyId,
            PlaystyleRegistry.TempoId,
        };

        Assert.Multiple(() =>
        {
            Assert.That(PlaystyleRegistry.Ids, Is.EqualTo(expectedIds));
            Assert.That(PlaystyleRegistry.Ids.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(expectedIds.Length));
            Assert.That(PlaystyleRegistry.All.Select(playstyle => playstyle.Id), Is.EqualTo(expectedIds));
            Assert.That(PlaystyleRegistry.Default.Id, Is.EqualTo(PlaystyleRegistry.DefaultId));
            Assert.That(PlaystyleRegistry.TryGetPlaystyle("aggro", out var aggro), Is.True);
            Assert.That(aggro.Id, Is.EqualTo(PlaystyleRegistry.AggroId));
            Assert.That(PlaystyleRegistry.TryGetPlaystyle("nope", out _), Is.False);
            Assert.That(PlaystyleRegistry.TryGetPlaystyle(null, out _), Is.False);
            Assert.That(() => PlaystyleRegistry.GetPlaystyle("nope"), Throws.ArgumentException);
            Assert.That(
                () => new WeightedPlaystyle(
                    "typo",
                    PunishResponseStance.Never,
                    new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["damagee"] = 1,
                    }),
                Throws.ArgumentException,
                "an unknown feature name must fail at construction");
        });

        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var playstyle in PlaystyleRegistry.All)
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    playstyle.Weights.Keys,
                    Is.EquivalentTo(ActionFeatures.Names),
                    playstyle.Id + " must price every published feature (explicitly, zeros included)");
                Assert.That(
                    playstyle.Weights.Count,
                    Is.EqualTo(ActionFeatures.Names.Count),
                    playstyle.Id + ": a missing or extra weight must be visible");
            });

            var fingerprint = WeightedPlaystyle.WeightFingerprint(playstyle);
            Assert.That(fingerprints.ContainsKey(fingerprint), Is.False,
                playstyle.Id + " must have its own weight fingerprint for provenance");
            fingerprints[fingerprint] = playstyle.Id;
            Assert.That(
                WeightedPlaystyle.WeightFingerprint(playstyle),
                Is.EqualTo(fingerprint),
                playstyle.Id + ": the provenance fingerprint must be stable");
        }

        TestContext.Out.WriteLine(
            "registry: {0} playstyles, weight fingerprints {1}",
            PlaystyleRegistry.All.Count,
            string.Join(
                ", ",
                fingerprints.Select(entry => entry.Value + "=" + entry.Key).OrderBy(text => text, StringComparer.Ordinal)));
    }

    /// <summary>
    /// A guard for the later harness step: no playstyle may prefer a ROLLBACK
    /// over ending the turn. A rollback returns a queued card to hand, so an AI
    /// that ranks it above END_TURN can commit and roll back forever, exhaust
    /// <c>AiTurnCoordinator.DefaultMaxActionsPerTurn</c>, and halt the driver
    /// with <c>ai.action_limit_reached</c> instead of producing a match.
    /// </summary>
    [Test]
    public void NoPlaystylePrefersRollbackOverEndingTheTurn()
    {
        var snapshot = Snapshot(new[]
        {
            Rollback("rollback_1", "machine_queued"),
            EndTurn(),
        });

        foreach (var playstyle in PlaystyleRegistry.All)
        {
            Assert.That(
                Choose(playstyle, snapshot).Type,
                Is.EqualTo(AdvertisedActionPolicy.EndTurnAction),
                playstyle.Id + " must not roll the chain back while ending the turn is available");
        }
    }

    // =====================================================================
    // §4.1 brackets + §3.5 response stance.
    //
    // The style playstyles are all weights one author chose, so a matrix built
    // only from them can agree on a wrong answer while looking robust. The
    // brackets bound the reading instead, and they use a DIFFERENT seam:
    // accept/decline is not an advertised action, so it is decided through
    // IPunishResponsePolicy (the optional punish arrival/response the engine
    // offers), never through the weight vector. §3.5 moved that stance onto the
    // playstyle, so every playstyle now declares one.
    // =====================================================================

    /// <summary>
    /// `bracket-first` is the historical anchor: the retired hardcoded priority
    /// plus the response stance production actually has (`Never`, because
    /// nothing injects a policy). It must reproduce the retired decisions
    /// exactly, which is what makes it the regression anchor for "the existing
    /// AI was not changed", and its injected policy must be indistinguishable
    /// from injecting nothing.
    /// </summary>
    [Test]
    public void BracketFirstReproducesTheRetiredDefaultDecisions()
    {
        var anchor = PlaystyleRegistry.GetPlaystyle(PlaystyleRegistry.BracketFirstId);
        var shipped = new AdvertisedActionPolicy();

        Assert.Multiple(() =>
        {
            Assert.That(PlaystyleRegistry.IsBracket(anchor.Id), Is.True);
            Assert.That(anchor.Id, Is.EqualTo(PlaystyleRegistry.BracketFirstId));
            Assert.That(anchor.ResponseStance, Is.EqualTo(PunishResponseStance.Never),
                "today's shipped opponent declines every punish response (nothing is injected)");
            Assert.That(anchor.ResponseStance, Is.EqualTo(PlaystyleRegistry.Default.ResponseStance),
                "bracket-first must not change the response stance of the shipped default");
            Assert.That(anchor.Weights, Is.EquivalentTo(PlaystyleRegistry.Default.Weights),
                "bracket-first must carry the retired priority verbatim");
            Assert.That(
                PunishResponseStances.PolicyFor(anchor),
                Is.SameAs(PunishResponseStances.NeverPolicy),
                "bracket-first's stance must resolve to the decline policy");
        });

        // Behavioural identity, which is what "identical to the shipped default"
        // means now that every id is its own playstyle instance: the retired
        // algorithm AND the policy the runtime actually builds must both agree
        // with bracket-first on every ACTION-phase advertisement.
        var cases = 0;
        var bracketPolicy = new AdvertisedActionPolicy(anchor);
        foreach (var snapshot in ActionCrossProduct())
        {
            cases++;
            var expected = LegacyChoose(snapshot, shipped).ActionId;
            Assert.Multiple(() =>
            {
                Assert.That(
                    Choose(anchor, snapshot).ActionId,
                    Is.EqualTo(expected),
                    "bracket-first must submit what the retired default submitted for " + Describe(snapshot));
                Assert.That(
                    Choose(bracketPolicy, snapshot).ActionId,
                    Is.EqualTo(expected),
                    "a policy built from bracket-first must submit what the shipped default submits for "
                    + Describe(snapshot));
                Assert.That(
                    Choose(new AdvertisedActionPolicy(), snapshot).ActionId,
                    Is.EqualTo(expected),
                    "the shipped default policy itself must still submit what the retired algorithm submitted for "
                    + Describe(snapshot));
            });
        }

        Assert.That(cases, Is.GreaterThan(600), "the cross product must stay immutable");
        TestContext.Out.WriteLine(
            "bracket-first: {0} ACTION-phase advertisement sets, decisions compared against the frozen retired algorithm, 0 differences; stance {1}",
            cases,
            PunishResponseStances.IdOf(anchor.ResponseStance));
    }

    /// <summary>
    /// The stance seam itself, at the unit level. `Never` and `Always` are
    /// unconditional and invent nothing (no target, no discard selection - that
    /// matches the existing harness instrument's greedy-accept reading rather
    /// than inventing a new one); the state and card arguments are deliberately
    /// null for those two, which proves they do not even look at them.
    ///
    /// `FirstInRound` is the stance that needs the engine's own round unit, so it
    /// gets a real game state whose event log is arranged the way the engine
    /// arranges it: a root action event with no parent, then the
    /// PUNISH_TRIGGERED events the round has already resolved.
    /// </summary>
    [Test]
    public void ResponseStancesDecideTheSeamAndNothingElse()
    {
        var accept = PlaystyleRegistry.GetPlaystyle(PlaystyleRegistry.BracketAcceptId);
        var decline = PlaystyleRegistry.GetPlaystyle(PlaystyleRegistry.BracketDeclineId);
        var first = PlaystyleRegistry.GetPlaystyle(PlaystyleRegistry.AggroId);

        Assert.Multiple(() =>
        {
            Assert.That(accept.ResponseStance, Is.EqualTo(PunishResponseStance.Always));
            Assert.That(decline.ResponseStance, Is.EqualTo(PunishResponseStance.Never));
            Assert.That(first.ResponseStance, Is.EqualTo(PunishResponseStance.FirstInRound));
            Assert.That(PunishResponseStances.IdOf(accept.ResponseStance), Is.EqualTo("always"));
            Assert.That(PunishResponseStances.IdOf(decline.ResponseStance), Is.EqualTo("never"));
            Assert.That(PunishResponseStances.IdOf(first.ResponseStance), Is.EqualTo("first-in-round"));
            Assert.That(
                PunishResponseStances.PolicyFor(PunishResponseStance.Never),
                Is.SameAs(PunishResponseStances.NeverPolicy));
            Assert.That(
                PunishResponseStances.PolicyFor(PunishResponseStance.FirstInRound),
                Is.SameAs(PunishResponseStances.FirstInRoundPolicy));
            Assert.That(
                PunishResponseStances.PolicyFor(PunishResponseStance.Always),
                Is.SameAs(PunishResponseStances.AlwaysPolicy));
        });

        foreach (var depth in new[] { 0, 1, 2, 7 })
        {
            foreach (var amount in new[] { 0, 1, 5 })
            {
                var accepted = PunishResponseStances.AlwaysPolicy.Decide(null!, null!, amount, depth);
                var declined = PunishResponseStances.NeverPolicy.Decide(null!, null!, amount, depth);

                Assert.Multiple(() =>
                {
                    Assert.That(accepted.Activate, Is.True,
                        "the upper bracket must accept every offer (depth=" + depth + ", amount=" + amount + ")");
                    Assert.That(accepted.TargetId, Is.Null, "the bracket must not invent a target");
                    Assert.That(accepted.SelectedDiscardIds, Is.Empty, "the bracket must not invent a discard selection");
                    Assert.That(declined.Activate, Is.False,
                        "the lower bracket must decline every offer (depth=" + depth + ", amount=" + amount + ")");
                    Assert.That(declined.TargetId, Is.Null);
                    Assert.That(declined.SelectedDiscardIds, Is.Empty);
                });
            }
        }

        // FirstInRound: one response per punish round, derived from the engine's
        // own accounting (a root action event, and the responses resolved under
        // it), with no state kept between calls.
        var state = CreateBracketState();
        var root = state.Events.Append("COMMIT_DECLARED", null, BracketEventData());
        Assert.Multiple(() =>
        {
            Assert.That(FirstInRoundPunishResponses.CurrentRoundRoot(state), Is.EqualTo(root.EventId));
            Assert.That(FirstInRoundPunishResponses.ResolvedResponsesInCurrentRound(state), Is.Zero);
            Assert.That(PunishResponseStances.FirstInRoundPolicy.Decide(state, null!, 1, 1).Activate, Is.True,
                "the first offer of a round is taken");
        });

        state.Events.Append("PUNISH_TRIGGERED", root.EventId, BracketEventData());
        Assert.Multiple(() =>
        {
            Assert.That(FirstInRoundPunishResponses.ResolvedResponsesInCurrentRound(state), Is.EqualTo(1));
            Assert.That(PunishResponseStances.FirstInRoundPolicy.Decide(state, null!, 1, 1).Activate, Is.False,
                "a second offer in the same round is declined");
        });

        // A new root action opens a new round, so the stance is available again.
        var nextRoot = state.Events.Append("CARD_PLAYED", null, BracketEventData());
        Assert.Multiple(() =>
        {
            Assert.That(FirstInRoundPunishResponses.CurrentRoundRoot(state), Is.EqualTo(nextRoot.EventId));
            Assert.That(PunishResponseStances.FirstInRoundPolicy.Decide(state, null!, 1, 1).Activate, Is.True,
                "a new round offers the stance again");
            Assert.That(PunishResponseStances.FirstInRoundPolicy.Decide(state, null!, 1, 1).Activate, Is.True,
                "the stance keeps no state between calls, so the same state answers the same way");
            Assert.That(
                PunishResponseStances.FirstInRoundPolicy.Decide(
                    state,
                    null!,
                    1,
                    1).TargetId,
                Is.Null,
                "the first-in-round stance must not invent a target either");
        });
    }

    /// <summary>
    /// THE WIRING PROOF (§3.5). A chosen playstyle must reach a real match
    /// through its own IPunishResponsePolicy and change observable punish
    /// behaviour there — the failure mode this guards against is "the code was
    /// written but nothing injects it", which this project has already been
    /// bitten by once.
    ///
    /// Each run starts from the same small engine fixture and submits through the
    /// real RuntimeMatchGateway. One root play draws exactly two eligible response
    /// cards; each response only adds one root, so accepting it cannot create more
    /// opportunities. The finishing play kills the same opposing leader in every
    /// run. The injected stance is therefore the only variable, and the production
    /// default remains the router's uninjected decline policy.
    /// </summary>
    [Test]
    public void PlaystyleResponseStanceReachesTheMatchThroughItsPolicy()
    {
        var alwaysCounter = new CountingPunishPolicy(PunishResponseStances.AlwaysPolicy);
        var firstCounter = new CountingPunishPolicy(PunishResponseStances.FirstInRoundPolicy);
        var neverCounter = new CountingPunishPolicy(PunishResponseStances.NeverPolicy);

        var always = RunFixedResponseMatch(alwaysCounter);
        var first = RunFixedResponseMatch(firstCounter);
        var never = RunFixedResponseMatch(neverCounter);
        var production = RunFixedResponseMatch();

        TestContext.Out.WriteLine(
            "stance readings, fixed two-response engine fixture:\n{0}\n{1}\n{2}\n{3}",
            describe("always        ", always),
            describe("first-in-round", first),
            describe("never         ", never),
            describe("production    ", production));

        Assert.Multiple(() =>
        {
            foreach (var run in new[] { always, first, never, production })
            {
                Assert.That(run.Halted, Is.Empty, run.Describe());
                Assert.That(run.Rejections, Is.Zero, run.Describe());
                Assert.That(run.Terminal, Is.True, "every stance must drive a real match to a terminal state: " + run.Describe());
            }

            // The three instrumented runs prove the engine really does offer
            // optional responses in this match, which is the premise the whole
            // stance comparison rests on. The production run carries no counter
            // (nothing is injected), so its offer count is structurally 0 and its
            // evidence is the behavioural identity with the never run below.
            foreach (var run in new[] { always, first, never })
            {
                Assert.That(run.Offers, Is.GreaterThan(0),
                    "the engine must offer optional responses, otherwise this test would prove nothing: " + run.Describe());
                Assert.That(run.Offers, Is.EqualTo(2),
                    "the fixed root play must offer exactly its two eligible response cards: " + run.Describe());
            }
            Assert.That(production.Offers, Is.Zero,
                "the production run injects nothing, so no counter can observe it: " + production.Describe());

            Assert.That(never.PunishTriggered, Is.Zero,
                "the never stance declines every offered response: " + never.Describe());
            Assert.That(production.PunishTriggered, Is.Zero,
                "production declines every offered response, which is why the anchor is a Never stance: " + production.Describe());
            Assert.That(production.Behaviour(), Is.EqualTo(never.Behaviour()),
                "injecting Never must be indistinguishable from today's production wiring (nothing injected): production="
                + production.Describe() + " never=" + never.Describe());
            Assert.That(never.Behaviour(), Is.EqualTo(production.Behaviour()));

            Assert.That(always.PunishTriggered, Is.GreaterThan(0),
                "the always stance must actually activate responses: " + always.Describe());
            Assert.That(first.PunishTriggered, Is.GreaterThan(0),
                "the first-in-round stance must activate the first response of each round: " + first.Describe());
            Assert.That(first.PunishTriggered, Is.LessThan(always.PunishTriggered),
                "first-in-round must bound the chain below always, or the stance would be decorative: first="
                + first.Describe() + " always=" + always.Describe());
            Assert.That(first.PunishTriggered, Is.GreaterThanOrEqualTo(never.PunishTriggered));
            Assert.That(first.Offers, Is.EqualTo(always.Offers),
                "all instrumented stances see the same fixed response opportunities: first="
                + first.Describe() + " always=" + always.Describe());
            Assert.That(first.Offers, Is.LessThanOrEqualTo(always.Offers),
                "first-in-round changes the match, so it must not offer more than always does: first="
                + first.Describe() + " always=" + always.Describe());

            Assert.That(always.ResponseRootStacks, Is.EqualTo(2),
                "both always-accepted response abilities must resolve their explicit ADD_ROOT effect");
            Assert.That(first.ResponseRootStacks, Is.EqualTo(1),
                "the first-in-round response ability must resolve its explicit ADD_ROOT effect");
            Assert.That(never.ResponseRootStacks, Is.Zero);
            Assert.That(production.ResponseRootStacks, Is.Zero);
            Assert.That(new[] { always, first, never, production }.Select(run => run.Outcome()).Distinct().Count(),
                Is.EqualTo(1), "every response stance must reach the same engine-resolved terminal outcome");

            Assert.That(always.Accepts - always.PunishTriggered, Is.GreaterThanOrEqualTo(0),
                "an activation cannot exceed the offers a policy accepted: " + always.Describe());
            Assert.That(first.Accepts - first.PunishTriggered, Is.GreaterThanOrEqualTo(0));
        });

        TestContext.Out.WriteLine(
            "stance spread: responses always={0} first-in-round={1} never={2} production={3}; offers {4}/{5}/{6}/{7}; turns {8}/{9}/{10}/{11}; outcome {12} / {13}",
            always.PunishTriggered,
            first.PunishTriggered,
            never.PunishTriggered,
            production.PunishTriggered,
            always.Offers,
            first.Offers,
            never.Offers,
            production.Offers,
            always.Turns,
            first.Turns,
            never.Turns,
            production.Turns,
            always.Outcome(),
            never.Outcome());
        TestContext.Out.WriteLine(
            "upper bracket resolution: {0} offers, {1} accepted, {2} activated, {3} accepted but not resolvable without a target/discard selection",
            always.Offers,
            always.Accepts,
            always.PunishTriggered,
            always.Accepts - always.PunishTriggered);
    }

    /// <summary>
    /// The registry contract for the style playstyles and the brackets: nine
    /// selectable playstyles, the three brackets flagged as brackets, every
    /// playstyle declaring a response stance whose policy is reachable, and every
    /// playstyle publishing a weight fingerprint for report provenance.
    /// </summary>
    [Test]
    public void RegistryPublishesPlaystylesAndBracketsWithTheirResponseStance()
    {
        var expectedIds = PlaystyleRegistry.Ids.Concat(PlaystyleRegistry.BracketIds).ToArray();
        var expectedStances = new Dictionary<string, PunishResponseStance>(StringComparer.Ordinal)
        {
            [PlaystyleRegistry.DefaultId] = PunishResponseStance.Never,
            [PlaystyleRegistry.AggroId] = PunishResponseStance.FirstInRound,
            [PlaystyleRegistry.ControlId] = PunishResponseStance.Never,
            [PlaystyleRegistry.ComboId] = PunishResponseStance.Always,
            [PlaystyleRegistry.EconomyId] = PunishResponseStance.Never,
            [PlaystyleRegistry.TempoId] = PunishResponseStance.FirstInRound,
            [PlaystyleRegistry.BracketAcceptId] = PunishResponseStance.Always,
            [PlaystyleRegistry.BracketDeclineId] = PunishResponseStance.Never,
            [PlaystyleRegistry.BracketFirstId] = PunishResponseStance.Never,
        };

        Assert.Multiple(() =>
        {
            Assert.That(PlaystyleRegistry.SelectableIds, Is.EqualTo(expectedIds),
                "every selectable id must be a style or a bracket id");
            Assert.That(PlaystyleRegistry.SelectableIds.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(expectedIds.Length), "playstyle ids must be unique");
            Assert.That(PlaystyleRegistry.Selectable.Select(playstyle => playstyle.Id), Is.EqualTo(expectedIds));
            Assert.That(PlaystyleRegistry.Brackets.Select(playstyle => playstyle.Id), Is.EqualTo(PlaystyleRegistry.BracketIds));
            Assert.That(PlaystyleRegistry.BracketIds, Is.EqualTo(new[]
            {
                PlaystyleRegistry.BracketAcceptId,
                PlaystyleRegistry.BracketDeclineId,
                PlaystyleRegistry.BracketFirstId,
            }));
            Assert.That(PlaystyleRegistry.BracketIds.Count, Is.EqualTo(3));
            Assert.That(PlaystyleRegistry.Ids.Count, Is.EqualTo(6));
            Assert.That(PlaystyleRegistry.IsBracket(PlaystyleRegistry.BracketAcceptId), Is.True);
            Assert.That(PlaystyleRegistry.IsBracket(PlaystyleRegistry.AggroId), Is.False);
            Assert.That(PlaystyleRegistry.IsBracket("nope"), Is.False);
            Assert.That(PlaystyleRegistry.IsBracket(null), Is.False);
            Assert.That(PlaystyleRegistry.TryGetPlaystyle("aggro", out _), Is.True);
            Assert.That(PlaystyleRegistry.TryGetPlaystyle("nope", out _), Is.False);
            Assert.That(PlaystyleRegistry.TryGetPlaystyle(null, out _), Is.False);
            Assert.That(() => PlaystyleRegistry.GetPlaystyle("nope"), Throws.ArgumentException);
            Assert.That(() => PlaystyleRegistry.PunishResponsesFor("nope"), Throws.ArgumentException);
        });

        foreach (var playstyle in PlaystyleRegistry.Selectable)
        {
            Assert.Multiple(() =>
            {
                Assert.That(playstyle.ResponseStance, Is.EqualTo(expectedStances[playstyle.Id]),
                    playstyle.Id + " must carry the response stance the design assigns it");
                Assert.That(
                    PunishResponseStances.PolicyFor(playstyle),
                    Is.SameAs(PunishResponseStances.PolicyFor(playstyle.ResponseStance)),
                    playstyle.Id + ": the playstyle must resolve to its stance's policy");
                Assert.That(PlaystyleRegistry.PunishResponsesFor(playstyle.Id),
                    Is.SameAs(PunishResponseStances.PolicyFor(playstyle.ResponseStance)),
                    playstyle.Id + ": the id must resolve to the same policy");
                Assert.That(
                    WeightedPlaystyle.WeightFingerprint(playstyle),
                    Is.Not.Empty,
                    playstyle.Id + " must publish a weight fingerprint");
                Assert.That(new AdvertisedActionPolicy(playstyle).Playstyle, Is.SameAs(playstyle),
                    playstyle.Id + " must be usable directly as the ACTION-phase playstyle");
            });
        }

        // The three brackets reuse the shipped default weights verbatim: they are
        // boundaries, not different ways of playing.
        foreach (var bracket in PlaystyleRegistry.Brackets)
        {
            Assert.That(bracket.Weights, Is.EquivalentTo(PlaystyleRegistry.Default.Weights),
                bracket.Id + " is a bracket: it must carry the shipped default priority verbatim");
        }

        Assert.That(PlaystyleRegistry.Ids.Count + PlaystyleRegistry.BracketIds.Count,
            Is.EqualTo(PlaystyleRegistry.SelectableIds.Count));

        TestContext.Out.WriteLine(
            "playstyles: {0} selectable ids ({1} styles + {2} brackets); stance per id: {3}",
            PlaystyleRegistry.SelectableIds.Count,
            PlaystyleRegistry.Ids.Count,
            PlaystyleRegistry.BracketIds.Count,
            string.Join(
                ", ",
                PlaystyleRegistry.Selectable.Select(playstyle =>
                    playstyle.Id + "=" + PunishResponseStances.IdOf(playstyle.ResponseStance)
                    + "/" + WeightedPlaystyle.WeightFingerprint(playstyle))));
    }

    private static string describe(string label, BracketRun run) => label + ": " + run.Describe();

    // =====================================================================
    // Match-wide bracket driver (mirrors how the harness builds a match).
    // =====================================================================

    /// <summary>
    /// Seed used by the bracket match-wide readings. It is the same seed the
    /// shipped-policy acceptance run uses, so the lower bracket's numbers can be
    /// read against an already measured match.
    /// </summary>
    private const ulong BracketSeed = 11;

    private sealed class BracketRun
    {
        public int Steps { get; set; }
        public int Turns { get; set; }
        public bool Terminal { get; set; }
        public int? Winner { get; set; }
        public string Reason { get; set; } = string.Empty;
        public int PunishTriggered { get; set; }
        public int ResponseRootStacks { get; set; }
        public int Rejections { get; set; }
        public int Offers { get; set; }
        public int Accepts { get; set; }
        public string Halted { get; set; } = string.Empty;

        public string Outcome()
            => (Winner.HasValue ? "winner=" + Winner.Value : "winner=none")
                + " reason=" + (Reason.Length == 0 ? "none" : Reason);

        /// <summary>
        /// Everything the match actually did, without the test-only counter
        /// fields, so two runs can be compared for behavioural identity.
        /// </summary>
        public string Behaviour()
            => "steps=" + Steps
                + " turns=" + Turns
                + " terminal=" + Terminal
                + " " + Outcome()
                + " punishTriggered=" + PunishTriggered
                + " responseRootStacks=" + ResponseRootStacks
                + " rejections=" + Rejections
                + (Halted.Length == 0 ? string.Empty : " halted=" + Halted);

        public string Describe()
            => Behaviour()
                + " punishOffers=" + Offers
                + " punishAccepts=" + Accepts;
    }

    /// <summary>
    /// Test-only wrapper that counts what the engine offered and what the bracketed
    /// policy accepted, so "accept everything" can be checked against what actually
    /// resolved instead of being taken on trust.
    /// </summary>
    private sealed class CountingPunishPolicy : IPunishResponsePolicy
    {
        private readonly IPunishResponsePolicy _inner;

        public CountingPunishPolicy(IPunishResponsePolicy inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public int Offers { get; private set; }

        public int Accepts { get; private set; }

        public PunishResponseDecision Decide(
            GameState state,
            CardInstance card,
            int effectivePunish,
            int chainDepth)
        {
            Offers++;
            var decision = _inner.Decide(state, card, effectivePunish, chainDepth);
            if (decision.Activate) Accepts++;
            return decision;
        }
    }

    /// <summary>
    /// Runs the same compact real-engine match for one response policy. A fixed
    /// root PLAY_CARD draws two response spells with a targetless ADD_ROOT ability,
    /// then damages the opposing leader for the shared terminal result. The zero
    /// T1 response budget keeps both offered reactions available in every run.
    /// </summary>
    private static BracketRun RunFixedResponseMatch(CountingPunishPolicy? counter = null)
    {
        const int responseOpportunities = 2;
        var playerLeaderDefinition = new CardDefinition(
            "stance_fixture_player_leader", "Stance Fixture Player Leader",
            health: 10, isMinion: true, isLeader: true,
            vulnerabilities: new[] { EffectNames.Damage });
        var enemyLeaderDefinition = new CardDefinition(
            "stance_fixture_enemy_leader", "Stance Fixture Enemy Leader",
            health: 1, isMinion: true, isLeader: true,
            vulnerabilities: new[] { EffectNames.Damage });
        var finishingCardDefinition = new CardDefinition(
            "stance_fixture_finish", "Stance Fixture Finisher",
            kingSlayer: true,
            punish: responseOpportunities,
            onPlayEffects: new[] { new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 1) });
        var responseDefinition = new CardDefinition(
            "stance_fixture_response", "Stance Fixture Response",
            isMinion: true,
            punishActivatable: true,
            punishCondition: "ALWAYS",
            punishEffects: new[] { new EffectSpec(EffectNames.AddRoot, "SELF_PLAYER", 1) });
        var openingDrawDefinition = new CardDefinition(
            "stance_fixture_opening_draw", "Stance Fixture Opening Draw");
        var definitions = new[]
        {
            playerLeaderDefinition,
            enemyLeaderDefinition,
            finishingCardDefinition,
            responseDefinition,
            openingDrawDefinition,
        };
        var state = new GameState(
            new PlayerState(0),
            new PlayerState(1),
            cardLibrary: definitions,
            rules: new MatchRules(maxPunishResponsesPerRound: 0));
        var player = state.GetPlayer(0);
        var opponent = state.GetPlayer(1);
        player.Field.Add(new CardInstance(state.AllocateEntityId(), 0, playerLeaderDefinition)
        {
            IsLeaderEntity = true,
        });
        opponent.Field.Add(new CardInstance(state.AllocateEntityId(), 1, enemyLeaderDefinition)
        {
            IsLeaderEntity = true,
        });
        player.Hand.Add(new CardInstance(state.AllocateEntityId(), 0, finishingCardDefinition));
        player.Deck.Add(new CardInstance(state.AllocateEntityId(), 0, openingDrawDefinition));
        opponent.Deck.Add(new CardInstance(state.AllocateEntityId(), 1, responseDefinition));
        opponent.Deck.Add(new CardInstance(state.AllocateEntityId(), 1, responseDefinition));

        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow, null, counter);
        var gateway = new RuntimeMatchGateway("match_playstyle_stance_fixture", state, flow, router);

        var initialization = gateway.Initialize(0);
        Assert.That(initialization.Accepted, Is.True, initialization.ReasonKey);
        var snapshot = initialization.Snapshot;
        var run = new BracketRun();
        void CaptureResult(RuntimeSnapshotEnvelope finalSnapshot)
        {
            run.Turns = finalSnapshot.Turn;
            run.Winner = finalSnapshot.WinnerPlayerIndex;
            run.Reason = finalSnapshot.ReasonKey ?? string.Empty;
            run.Terminal = finalSnapshot.WinnerPlayerIndex.HasValue;
            run.PunishTriggered = state.Events.Items.Count(
                item => string.Equals(item.EventType, "PUNISH_TRIGGERED", StringComparison.Ordinal));
            run.ResponseRootStacks = opponent.RootStacks;
            if (counter is not null)
            {
                run.Offers = counter.Offers;
                run.Accepts = counter.Accepts;
            }
        }

        var skipAmbush = snapshot.LegalActions.Single(action => action.Type == TurnAction.SkipAmbush);
        var skipSubmission = gateway.Submit(ToSubmittedGameAction(snapshot, skipAmbush));
        run.Steps++;
        if (!skipSubmission.Result.Accepted)
        {
            run.Rejections++;
            run.Halted = skipSubmission.Result.ReasonKey;
            CaptureResult(skipSubmission.Snapshot);
            return run;
        }

        snapshot = gateway.GetSnapshot(0);
        var finishingAction = snapshot.LegalActions.Single(action =>
            action.Type == LegalActionGenerator.PlayCard
            && action.CardId == finishingCardDefinition.Id);
        var finishingSubmission = gateway.Submit(ToSubmittedGameAction(snapshot, finishingAction));
        run.Steps++;
        if (!finishingSubmission.Result.Accepted)
        {
            run.Rejections++;
            run.Halted = finishingSubmission.Result.ReasonKey;
        }
        CaptureResult(finishingSubmission.Snapshot);
        return run;
    }

    private static RuntimeGameAction ToSubmittedGameAction(
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

        // Self-discard PLAY_CARD keeps the legacy payload selection produced by
        // AdvertisedActionPolicy. Ordinary DISCARD supplies an explicit typed
        // choice from the advertised candidates; the gateway never auto-picks.
        if (selectedEntityIds.Count > 0 &&
            !action.Payload.ContainsKey("selectedEntityIds"))
        {
            action.SelectedEntityIds = selectedEntityIds;
        }

        return action;
    }

    private static MatchDeckSpec ToDeckSpec(DeckDefinition deck)
        => new MatchDeckSpec(deck.Leader, deck.Cards, deck.Name, deck.Faction);

    /// <summary>
    /// A real game state for the FirstInRound unit test, built the same way the
    /// match driver builds one so the event log behaves like a live match's.
    /// </summary>
    private static GameState CreateBracketState()
    {
        var production = LoadProduction();
        return MatchSetup.Create(
            ToDeckSpec(production.MachineDeck),
            ToDeckSpec(production.WoodDeck),
            production.Catalog.Cards,
            new MatchSetupOptions { Seed = BracketSeed, CastleEnabled = false });
    }

    private static IReadOnlyDictionary<string, object?> BracketEventData()
        => new Dictionary<string, object?>(StringComparer.Ordinal) { ["player"] = 1 };

    private sealed class Production
    {
        public Production(CardCatalog catalog, DeckDefinition woodDeck, DeckDefinition machineDeck)
        {
            Catalog = catalog;
            WoodDeck = woodDeck;
            MachineDeck = machineDeck;
        }

        public CardCatalog Catalog { get; }
        public DeckDefinition WoodDeck { get; }
        public DeckDefinition MachineDeck { get; }
    }

    private static Production LoadProduction()
    {
        var root = FindRepositoryRoot();
        var catalog = CardCatalog.LoadDirectory(Path.Combine(root, "data", "cards"));
        var decks = DeckLoader.LoadDirectory(Path.Combine(root, "data", "decks"));
        return new Production(
            catalog,
            decks.Single(deck => deck.Leader == "wood_leader"),
            decks.Single(deck => deck.Leader == "machine_leader"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docs", "RULES.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new AssertionException("Repository root was not found.");
    }

    // =====================================================================
    // Synthetic advertisement sets.
    // =====================================================================

    private const string FaceFirstSet = "attack_face_then_trade";
    private const string TradeFirstSet = "attack_trade_then_face";
    private const string PunishSet = "expensive_punish_advertised_first";
    private const string AxisVsBoardSet = "resolvable_download_vs_card_play";
    private const string ChainSet = "commit_vs_card_play";
    private const string FloodSet = "high_punish_play_vs_end_turn";

    private static readonly string[] DecisionSets =
    {
        FaceFirstSet,
        TradeFirstSet,
        PunishSet,
        AxisVsBoardSet,
        ChainSet,
        FloodSet,
    };

    /// <summary>
    /// The decision each playstyle must make on each set, and the feature
    /// difference that drives it. Every expectation is derivable from the
    /// weight tables alone; the comment names the driving feature.
    /// </summary>
    private static string ExpectedChoice(string setName, string playstyleId)
    {
        switch (setName + "|" + playstyleId)
        {
            // An attack on the enemy leader core versus an attack that trades a
            // visible minion. face: aggro +500, tempo +100, others 0.
            // trade: control +600, aggro +50, economy +100, others 0.
            case "attack_face_then_trade|default": return "attack_face";
            case "attack_face_then_trade|aggro": return "attack_face";
            case "attack_face_then_trade|control": return "attack_trade";
            case "attack_face_then_trade|combo": return "attack_face";
            case "attack_face_then_trade|economy": return "attack_trade";
            case "attack_face_then_trade|tempo": return "attack_face";

            // Same two attacks, advertised the other way round: the shipped
            // default has equal scores and follows the advertisement (it was
            // face/trade-blind), while aggro, control, and tempo are decided by
            // their weights and do not move.
            case "attack_trade_then_face|default": return "attack_trade";
            case "attack_trade_then_face|aggro": return "attack_face";
            case "attack_trade_then_face|control": return "attack_trade";
            case "attack_trade_then_face|combo": return "attack_trade";
            case "attack_trade_then_face|economy": return "attack_trade";
            case "attack_trade_then_face|tempo": return "attack_face";

            // A punish-6 play advertised before a punish-1 play. punish:
            // default 0 (blind, so the advertisement decides), aggro -1,
            // tempo -3, combo -5, control -6, economy -12.
            case "expensive_punish_advertised_first|default": return "play_expensive";
            case "expensive_punish_advertised_first|aggro": return "play_cheap";
            case "expensive_punish_advertised_first|control": return "play_cheap";
            case "expensive_punish_advertised_first|combo": return "play_cheap";
            case "expensive_punish_advertised_first|economy": return "play_cheap";
            case "expensive_punish_advertised_first|tempo": return "play_cheap";

            // A resolvable download (win_axis + lifecycle) versus a card play
            // (board). tempo is the only playstyle that discounts the download
            // axis below board development.
            case "resolvable_download_vs_card_play|default": return "pull_axis";
            case "resolvable_download_vs_card_play|aggro": return "pull_axis";
            case "resolvable_download_vs_card_play|control": return "pull_axis";
            case "resolvable_download_vs_card_play|combo": return "pull_axis";
            case "resolvable_download_vs_card_play|economy": return "pull_axis";
            case "resolvable_download_vs_card_play|tempo": return "play_axis";

            // A commit (lifecycle + spend_field) versus a card play (board).
            // combo is the only playstyle that pays for the chain setup step.
            case "commit_vs_card_play|default": return "play_chain";
            case "commit_vs_card_play|aggro": return "play_chain";
            case "commit_vs_card_play|control": return "play_chain";
            case "commit_vs_card_play|combo": return "commit_chain";
            case "commit_vs_card_play|economy": return "play_chain";
            case "commit_vs_card_play|tempo": return "play_chain";

            // A punish-12 play versus ending the turn. Only the playstyle whose
            // whole point is refusing the flood declines the card.
            case "high_punish_play_vs_end_turn|default": return "play_expensive";
            case "high_punish_play_vs_end_turn|aggro": return "play_expensive";
            case "high_punish_play_vs_end_turn|control": return "play_expensive";
            case "high_punish_play_vs_end_turn|combo": return "play_expensive";
            case "high_punish_play_vs_end_turn|economy": return "end_turn_1";
            case "high_punish_play_vs_end_turn|tempo": return "play_expensive";

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(setName),
                    "no expectation is declared for " + setName + " / " + playstyleId);
        }
    }

    private static RuntimeSnapshotEnvelope SnapshotFor(string setName)
    {
        switch (setName)
        {
            case FaceFirstSet:
                return Snapshot(
                    new[] { Attack("attack_face", "leader_0"), Attack("attack_trade", EnemyMinion) },
                    enemyField: new[] { BoardCard(EnemyMinion, "wood_minion") });
            case TradeFirstSet:
                return Snapshot(
                    new[] { Attack("attack_trade", EnemyMinion), Attack("attack_face", "leader_0") },
                    enemyField: new[] { BoardCard(EnemyMinion, "wood_minion") });
            case PunishSet:
                return Snapshot(new[]
                {
                    Play("play_expensive", "wood_flood", 6),
                    Play("play_cheap", "wood_probe", 1),
                });
            case AxisVsBoardSet:
                return Snapshot(
                    new[] { Pull("pull_axis", 2), Play("play_axis", "machine_filler", 2) },
                    cloudStackCount: 3);
            case ChainSet:
                return Snapshot(new[]
                {
                    Commit("commit_chain", "machine_carrier_card", 1),
                    Play("play_chain", "machine_filler", 1),
                });
            case FloodSet:
                return Snapshot(new[]
                {
                    Play("play_expensive", "wood_flood", 12),
                    EndTurn(),
                });
            default:
                throw new ArgumentOutOfRangeException(nameof(setName), "unknown synthetic set " + setName);
        }
    }

    private static IEnumerable<RuntimeSnapshotEnvelope> DecisionSnapshots()
    {
        foreach (var set in DecisionSets) yield return SnapshotFor(set);
    }

    /// <summary>
    /// Every non-empty ACTION-phase advertisement over the six advertised types,
    /// three advertised orderings, and both cloud-stack states with and without
    /// a download selection: the input space the default weight vector must
    /// reproduce the retired rank on.
    /// </summary>
    private static IEnumerable<RuntimeSnapshotEnvelope> ActionCrossProduct()
    {
        var types = new[]
        {
            AdvertisedActionPolicy.PlayCardAction,
            AdvertisedActionPolicy.PullAction,
            AdvertisedActionPolicy.CommitAction,
            AdvertisedActionPolicy.AttackAction,
            AdvertisedActionPolicy.EndTurnAction,
            AdvertisedActionPolicy.RollbackAction,
        };

        for (var mask = 1; mask < 1 << types.Length; mask++)
        {
            var selected = new List<string>();
            for (var bit = 0; bit < types.Length; bit++)
            {
                if ((mask & (1 << bit)) != 0) selected.Add(types[bit]);
            }

            if (selected.Count < 2) continue;

            foreach (var ordering in Orderings(selected))
            {
                foreach (var cloudStackCount in new[] { 0, 2 })
                {
                    foreach (var includeSelection in new[] { true, false })
                    {
                        yield return CrossProductSnapshot(ordering, cloudStackCount, includeSelection);
                    }
                }
            }
        }
    }

    private static IEnumerable<string[]> Orderings(IReadOnlyList<string> types)
    {
        yield return types.ToArray();
        yield return types.Reverse().ToArray();
        var rotated = types.Skip(1).Concat(types.Take(1)).ToArray();
        yield return rotated;
    }

    private static RuntimeSnapshotEnvelope CrossProductSnapshot(
        IReadOnlyList<string> orderedTypes,
        int cloudStackCount,
        bool includeSelection)
    {
        var advertised = new List<RuntimeLegalAction>(orderedTypes.Count);
        for (var index = 0; index < orderedTypes.Count; index++)
        {
            var suffix = index.ToString(CultureInfo.InvariantCulture);
            switch (orderedTypes[index])
            {
                case AdvertisedActionPolicy.PlayCardAction:
                    advertised.Add(Play("play_" + suffix, "wood_probe", index));
                    break;
                case AdvertisedActionPolicy.PullAction:
                    advertised.Add(Pull("pull_" + suffix, index, includeSelection));
                    break;
                case AdvertisedActionPolicy.CommitAction:
                    advertised.Add(Commit("commit_" + suffix, "machine_card_" + suffix, index));
                    break;
                case AdvertisedActionPolicy.AttackAction:
                    advertised.Add(Attack("attack_" + suffix, EnemyMinion));
                    break;
                case AdvertisedActionPolicy.EndTurnAction:
                    advertised.Add(EndTurn());
                    break;
                case AdvertisedActionPolicy.RollbackAction:
                    advertised.Add(Rollback("rollback_" + suffix, "machine_card_" + suffix));
                    break;
                default:
                    throw new InvalidOperationException("unexpected advertised type " + orderedTypes[index]);
            }
        }

        return Snapshot(
            advertised,
            cloudStackCount: cloudStackCount,
            enemyField: new[] { BoardCard(EnemyMinion, "wood_minion") });
    }

    private static RuntimeSnapshotEnvelope AmbushSnapshot()
    {
        var snapshot = Snapshot(new[] { SetAmbush("set_ambush_1", "wood_veil", 1), SkipAmbush() });
        snapshot.Phase = AdvertisedActionPolicy.AmbushPhase;
        return snapshot;
    }

    private static RuntimeSnapshotEnvelope DiscardSnapshot()
    {
        var snapshot = Snapshot(new[] { Discard("discard_2", 2) });
        snapshot.Phase = AdvertisedActionPolicy.DiscardPhase;
        return snapshot;
    }

    // =====================================================================
    // Advertisement, snapshot, and policy helpers.
    // =====================================================================

    private static RuntimeSnapshotEnvelope Snapshot(
        IReadOnlyList<RuntimeLegalAction> advertised,
        int cloudStackCount = 0,
        int turn = 3,
        IReadOnlyList<RuntimeCardSnapshot>? enemyField = null,
        IReadOnlyList<RuntimeCardSnapshot>? enemyLeaderZone = null)
    {
        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = MatchId,
            SnapshotRevision = Revision,
            Turn = turn,
            Phase = AdvertisedActionPolicy.ActionPhase,
            CurrentPlayer = Ai,
            ViewerPlayerId = "player_1",
            Players = new[]
            {
                new RuntimePlayerSnapshot
                {
                    PlayerId = "player_0",
                    Life = 20,
                    HandCount = 4,
                    DeckCount = 20,
                    FieldCount = enemyField?.Count ?? 0,
                    Field = enemyField ?? Array.Empty<RuntimeCardSnapshot>(),
                    LeaderZone = enemyLeaderZone ?? Array.Empty<RuntimeCardSnapshot>(),
                },
                new RuntimePlayerSnapshot
                {
                    PlayerId = "player_1",
                    Life = 20,
                    HandCount = 5,
                    DeckCount = 20,
                    FieldCount = 1,
                    CloudStackCount = cloudStackCount,
                    Field = new[] { BoardCard(Attacker, "machine_carrier") },
                },
            },
            Castle = new RuntimeCastleSnapshot { Enabled = true, Health = 30 },
            LegalActions = advertised,
        };
    }

    private static RuntimeCardSnapshot BoardCard(
        long entityId,
        string cardId,
        bool leader = false,
        int attack = 2,
        int health = 3)
    {
        return new RuntimeCardSnapshot
        {
            EntityId = entityId,
            CardId = cardId,
            OwnerPlayer = Enemy,
            CurrentAttack = attack,
            CurrentHealth = leader ? 8 : health,
        };
    }

    private static RuntimeLegalAction Attack(string actionId, object? targetId)
    {
        var action = Advertised(
            actionId,
            AdvertisedActionPolicy.AttackAction,
            new Dictionary<string, object?>(StringComparer.Ordinal));
        action.SourceId = Attacker;
        action.TargetId = targetId;
        return action;
    }

    private static RuntimeLegalAction Play(string actionId, string cardId, int punish, int discardRequired = 0)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["punish"] = punish };
        if (discardRequired > 0) payload["discardRequired"] = discardRequired;
        var action = Advertised(actionId, AdvertisedActionPolicy.PlayCardAction, payload);
        action.SourceId = 900L;
        action.CardId = cardId;
        return action;
    }

    private static RuntimeLegalAction Pull(string actionId, int punish, bool includeSelection = true)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["punish"] = punish };
        if (includeSelection) payload["selectedEntityIds"] = new[] { 33L };
        var action = Advertised(actionId, AdvertisedActionPolicy.PullAction, payload);
        action.SourceId = 411L;
        action.TargetId = 421L;
        action.CardId = "machine_top";
        return action;
    }

    private static RuntimeLegalAction Commit(string actionId, string cardId, int commitCost)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["commitCost"] = commitCost,
            ["punish"] = commitCost,
        };
        var action = Advertised(actionId, AdvertisedActionPolicy.CommitAction, payload);
        action.SourceId = Attacker;
        action.CardId = cardId;
        return action;
    }

    private static RuntimeLegalAction Rollback(string actionId, string cardId)
    {
        var action = Advertised(actionId, AdvertisedActionPolicy.RollbackAction, Empty());
        action.SourceId = 402L;
        action.CardId = cardId;
        return action;
    }

    private static RuntimeLegalAction SetAmbush(string actionId, string cardId, int punish)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["punish"] = punish };
        var action = Advertised(actionId, AdvertisedActionPolicy.SetAmbushAction, payload);
        action.SourceId = 903L;
        action.CardId = cardId;
        return action;
    }

    private static RuntimeLegalAction SkipAmbush()
        => Advertised("skip_ambush_1", AdvertisedActionPolicy.SkipAmbushAction, Empty());

    private static RuntimeLegalAction Discard(string actionId, int requiredCount)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["requiredCount"] = requiredCount,
            ["discardCandidateIds"] = new[] { 1L, 2L, 3L },
        };
        return Advertised(actionId, AdvertisedActionPolicy.DiscardAction, payload);
    }

    private static RuntimeLegalAction EndTurn()
        => Advertised("end_turn_1", AdvertisedActionPolicy.EndTurnAction, Empty());

    private static RuntimeLegalAction Advertised(
        string actionId,
        string type,
        IReadOnlyDictionary<string, object?> payload)
    {
        return new RuntimeLegalAction
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            SnapshotRevision = Revision,
            ActionId = actionId,
            Type = type,
            Actor = Ai,
            ReasonKey = "action." + type.ToLowerInvariant(),
            Payload = payload,
        };
    }

    private static Dictionary<string, object?> Empty()
        => new Dictionary<string, object?>(StringComparer.Ordinal);

    private static RuntimeLegalAction Choose(IPlaystyle playstyle, RuntimeSnapshotEnvelope snapshot)
        => Choose(new AdvertisedActionPolicy(playstyle), snapshot);

    private static RuntimeLegalAction Choose(AdvertisedActionPolicy policy, RuntimeSnapshotEnvelope snapshot)
    {
        var selected = policy.TryChoose(snapshot, Ai, false, out var chosen);
        Assert.That(selected, Is.True, "the policy must select an advertised action: " + Describe(snapshot));
        Assert.That(chosen, Is.Not.Null);
        return chosen!;
    }

    private static int ManualScore(
        IPlaystyle playstyle,
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction action)
    {
        var features = ActionFeatures.Of(snapshot, action);
        var total = 0;
        foreach (var feature in ActionFeatures.Names)
        {
            total += playstyle.Weights[feature] * features[feature];
        }

        return total;
    }

    private static string Describe(RuntimeSnapshotEnvelope snapshot)
    {
        var types = string.Join(
            ",",
            snapshot.LegalActions.Select(action => action.Type + ":" + action.ActionId));
        return "cloudStack=" + snapshot.Players[Ai].CloudStackCount + " advertised=[" + types + "]";
    }

    private static string Describe(string setName, string playstyleId)
        => "set=" + setName + " playstyle=" + playstyleId;

    // ---- frozen copies of the retired default behaviour -------------------

    /// <summary>
    /// Frozen copy of the retired <c>AdvertisedActionPolicy.Ordered</c>: rank
    /// ascending, then advertised position, then ActionId and Type. It is the
    /// independent reference the default weights are compared against, so the
    /// equivalence proof does not run through the new weight code.
    /// </summary>
    private static List<RuntimeLegalAction> LegacyUsableOrder(
        RuntimeSnapshotEnvelope snapshot,
        List<RuntimeLegalAction> actions,
        AdvertisedActionPolicy policy)
    {
        var pending = new List<RuntimeLegalAction>(actions);
        var sorted = new List<RuntimeLegalAction>(actions.Count);
        while (pending.Count > 0)
        {
            var best = 0;
            for (var index = 1; index < pending.Count; index++)
            {
                if (LegacyPrecedes(snapshot, policy, pending[index], pending[best])) best = index;
            }

            sorted.Add(pending[best]);
            pending.RemoveAt(best);
        }

        return sorted;
    }

    private static bool LegacyPrecedes(
        RuntimeSnapshotEnvelope snapshot,
        AdvertisedActionPolicy policy,
        RuntimeLegalAction left,
        RuntimeLegalAction right)
    {
        var leftRank = policy.AdvertisedActionPriority(snapshot, left);
        var rightRank = policy.AdvertisedActionPriority(snapshot, right);
        if (leftRank != rightRank) return leftRank < rightRank;

        var advertised = LegacyAdvertisedIndex(snapshot, left).CompareTo(LegacyAdvertisedIndex(snapshot, right));
        if (advertised != 0) return advertised < 0;

        var id = string.Compare(left.ActionId, right.ActionId, StringComparison.Ordinal);
        if (id != 0) return id < 0;
        return string.Compare(left.Type, right.Type, StringComparison.Ordinal) < 0;
    }

    private static int LegacyAdvertisedIndex(RuntimeSnapshotEnvelope snapshot, RuntimeLegalAction action)
    {
        for (var index = 0; index < snapshot.LegalActions.Count; index++)
        {
            if (ReferenceEquals(snapshot.LegalActions[index], action)) return index;
        }

        return int.MaxValue;
    }

    /// <summary>Frozen copy of the retired ACTION-phase decision end to end.</summary>
    private static RuntimeLegalAction LegacyChoose(
        RuntimeSnapshotEnvelope snapshot,
        AdvertisedActionPolicy policy)
    {
        var stable = StableActions(snapshot.LegalActions);
        foreach (var candidate in LegacyUsableOrder(snapshot, stable, policy))
        {
            if (LegacyUsable(snapshot, candidate)) return candidate;
        }

        return stable[0];
    }

    /// <summary>Frozen copy of the retired <c>IsUsable</c> filter.</summary>
    private static bool LegacyUsable(RuntimeSnapshotEnvelope snapshot, RuntimeLegalAction action)
    {
        if (action.Actor != snapshot.CurrentPlayer) return false;
        if (!string.Equals(action.Type, AdvertisedActionPolicy.PullAction, StringComparison.Ordinal)) return true;
        if (action.Payload is null) return true;
        var requiresSelection = action.Payload.ContainsKey("selectedEntityIds")
            || action.Payload.ContainsKey("selectedEntityId");
        if (!requiresSelection) return true;
        if (action.Payload.TryGetValue("selectedEntityIds", out var many)) return many is not null;
        return action.Payload.TryGetValue("selectedEntityId", out var single) && single is not null;
    }

    private static List<RuntimeLegalAction> StableActions(IReadOnlyList<RuntimeLegalAction> actions)
    {
        var result = new List<RuntimeLegalAction>();
        foreach (var action in actions)
        {
            if (action is not null) result.Add(action);
        }

        result.Sort((left, right) =>
        {
            var id = string.Compare(left.ActionId, right.ActionId, StringComparison.Ordinal);
            if (id != 0) return id;
            return string.Compare(left.Type, right.Type, StringComparison.Ordinal);
        });
        return result;
    }

    /// <summary>Structural equality for wire values, mirroring the adapter boundary.</summary>
    private static bool ValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;

        if (left is string || right is string)
        {
            return string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal);
        }

        if (left is IReadOnlyDictionary<string, object?> leftMap &&
            right is IReadOnlyDictionary<string, object?> rightMap)
        {
            if (leftMap.Count != rightMap.Count) return false;
            foreach (var entry in leftMap)
            {
                if (!rightMap.TryGetValue(entry.Key, out var value)) return false;
                if (!ValuesEqual(entry.Value, value)) return false;
            }

            return true;
        }

        if (left is System.Collections.IEnumerable leftItems && right is System.Collections.IEnumerable rightItems)
        {
            var a = new List<object?>();
            var b = new List<object?>();
            foreach (var item in leftItems) a.Add(item);
            foreach (var item in rightItems) b.Add(item);
            if (a.Count != b.Count) return false;
            for (var index = 0; index < a.Count; index++)
            {
                if (!ValuesEqual(a[index], b[index])) return false;
            }

            return true;
        }

        return left.Equals(right);
    }

    // =====================================================================
    // ActionFeatures dependency checks (signature level and IL level).
    // =====================================================================

    private sealed class FeatureProbe
    {
        public FeatureProbe(string name, RuntimeSnapshotEnvelope snapshot, RuntimeLegalAction action)
        {
            Name = name;
            Snapshot = snapshot;
            Action = action;
        }

        public string Name { get; }
        public RuntimeSnapshotEnvelope Snapshot { get; }
        public RuntimeLegalAction Action { get; }
    }

    private static IEnumerable<FeatureProbe> FeatureProbes()
    {
        yield return new FeatureProbe("resolvable download", Snapshot(new[] { Pull("pull_1", 2) }, cloudStackCount: 2), Pull("pull_1", 2));
        yield return new FeatureProbe("unresolvable download", Snapshot(new[] { Pull("pull_1", 2) }, cloudStackCount: 0), Pull("pull_1", 2));
        yield return new FeatureProbe("card play", Snapshot(new[] { Play("play_1", "wood_probe", 3, 1) }), Play("play_1", "wood_probe", 3, 1));
        yield return new FeatureProbe("commit", Snapshot(new[] { Commit("commit_1", "machine_card", 2) }), Commit("commit_1", "machine_card", 2));
        yield return new FeatureProbe("rollback", Snapshot(new[] { Rollback("rollback_1", "machine_card") }), Rollback("rollback_1", "machine_card"));
        yield return new FeatureProbe("attack on a trade", Snapshot(new[] { Attack("attack_1", EnemyMinion) }, enemyField: new[] { BoardCard(EnemyMinion, "wood_minion") }), Attack("attack_1", EnemyMinion));
        yield return new FeatureProbe("attack on the core", Snapshot(new[] { Attack("attack_1", "player_0") }), Attack("attack_1", "player_0"));
        yield return new FeatureProbe("end turn", Snapshot(new[] { EndTurn() }), EndTurn());
        yield return new FeatureProbe("set ambush", Snapshot(new[] { SetAmbush("set_ambush_1", "wood_veil", 1) }), SetAmbush("set_ambush_1", "wood_veil", 1));
        yield return new FeatureProbe("forced discard", Snapshot(new[] { Discard("discard_1", 2) }), Discard("discard_1", 2));
        yield return new FeatureProbe("skip ambush", Snapshot(new[] { SkipAmbush() }), SkipAmbush());
    }

    private static IEnumerable<Type> SignatureTypes(Type type)
    {
        foreach (var method in type.GetMethods(Flags))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters()) yield return parameter.ParameterType;
        }

        foreach (var constructor in type.GetConstructors(Flags))
        {
            foreach (var parameter in constructor.GetParameters()) yield return parameter.ParameterType;
        }

        foreach (var property in type.GetProperties(Flags)) yield return property.PropertyType;
        foreach (var field in type.GetFields(Flags)) yield return field.FieldType;

        foreach (var nested in type.GetNestedTypes(Flags))
        {
            yield return nested;
            foreach (var inner in SignatureTypes(nested)) yield return inner;
        }
    }

    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    /// <summary>
    /// What one IL scan of a type found.
    /// </summary>
    private sealed class BodyScan
    {
        public int BodiesScanned { get; set; }
        public int MemberTokensInspected { get; set; }
        public int UnresolvedTokens { get; set; }
        public int DesynchronisedBodies { get; set; }
        public List<string> ForeignReferences { get; } = new List<string>();
    }

    /// <summary>
    /// Members referenced by a type's method bodies that are declared in
    /// <paramref name="foreign"/>'s assembly, plus the counters that show how
    /// much IL was actually walked. The IL is decoded with the real operand
    /// sizes taken from <see cref="System.Reflection.Emit.OpCodes"/>, so the
    /// scan stays in step with the instruction stream.
    /// </summary>
    private static BodyScan ScanBodyReferences(Type type, Assembly foreign)
    {
        var scan = new BodyScan();

        foreach (var method in type.GetMethods(Flags))
        {
            var body = method.GetMethodBody();
            var il = body?.GetILAsByteArray();
            if (il is null) continue;
            scan.BodiesScanned++;

            var position = 0;
            while (position < il.Length)
            {
                int key;
                if (il[position] == 0xFE)
                {
                    if (position + 1 >= il.Length)
                    {
                        scan.DesynchronisedBodies++;
                        break;
                    }

                    key = 0xFE00 | il[position + 1];
                    position += 2;
                }
                else
                {
                    key = il[position];
                    position += 1;
                }

                if (!OpCodeTable.TryGetValue(key, out var op))
                {
                    scan.DesynchronisedBodies++;
                    break;
                }

                if (op.OperandType == OperandType.InlineSwitch)
                {
                    if (position + 4 > il.Length)
                    {
                        scan.DesynchronisedBodies++;
                        break;
                    }

                    var count = BitConverter.ToInt32(il, position);
                    position += 4 + (4 * count);
                    continue;
                }

                var size = OperandSize(op.OperandType);
                if (size < 0)
                {
                    scan.DesynchronisedBodies++;
                    break;
                }

                if (IsMemberToken(op.OperandType) && position + 4 <= il.Length)
                {
                    scan.MemberTokensInspected++;
                    var token = BitConverter.ToInt32(il, position);
                    var referenced = ResolveMember(method.Module, token);
                    if (referenced is null)
                    {
                        scan.UnresolvedTokens++;
                    }
                    else if (referenced.Module.Assembly == foreign)
                    {
                        scan.ForeignReferences.Add(method.Name + " -> " + referenced.Name);
                    }
                }

                position += size;
            }
        }

        return scan;
    }

    private static MemberInfo? ResolveMember(Module module, int token)
    {
        try
        {
            return module.ResolveMember(token);
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (BadImageFormatException)
        {
        }

        try
        {
            return module.ResolveMethod(token);
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (BadImageFormatException)
        {
        }

        try
        {
            return module.ResolveField(token);
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (BadImageFormatException)
        {
        }

        try
        {
            return module.ResolveType(token);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static bool IsMemberToken(OperandType type)
    {
        return type == OperandType.InlineField
            || type == OperandType.InlineMethod
            || type == OperandType.InlineTok
            || type == OperandType.InlineType;
    }

    private static int OperandSize(OperandType type)
    {
        switch (type)
        {
            case OperandType.InlineNone: return 0;
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
            case OperandType.ShortInlineBrTarget: return 1;
            case OperandType.InlineVar: return 2;
            case OperandType.InlineI:
            case OperandType.InlineBrTarget:
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType:
            case OperandType.ShortInlineR: return 4;
            case OperandType.InlineI8:
            case OperandType.InlineR: return 8;
            case OperandType.InlineSwitch: return -1;
            default: return -1;
        }
    }

    private static readonly Dictionary<int, OpCode> OpCodeTable = BuildOpCodeTable();

    private static Dictionary<int, OpCode> BuildOpCodeTable()
    {
        var table = new Dictionary<int, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(OpCode)) continue;
            if (field.GetValue(null) is not OpCode op) continue;
            table[(int)(ushort)op.Value] = op;
        }

        return table;
    }
}

}
