using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Adapters.Ai;
using DominionWars.Data;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// End-to-end acceptance coverage for P0-5: the advertised-action CPU policy
/// must drive a real machine-deck match through the production adapter
/// pipeline (MatchFactory -> RuntimeMatchGateway -> MatchController) far
/// enough to satisfy the machine leader's own win condition.
///
/// The driver deliberately does not rate limit the ACTION phase. One action
/// per turn is a Unity presenter budget (RuntimeAiTurnCoordinator.
/// DefaultMaxActionsPerTurn), not an engine rule, and MatchController accepts
/// every advertised action until the player submits END_TURN.
/// </summary>
[TestFixture]
public sealed class AiLifecyclePolicyTests
{
    /// <summary>
    /// Fixed seed so the acceptance run is reproducible. With the shipped
    /// decks the machine reaches six downloads on several seeds; this one is
    /// the shortest confirmed run.
    /// </summary>
    private const ulong AcceptanceSeed = 11;
    private const string ControlledWoodDrawCardId = "test_controlled_wood_draw";

    [Test]
    public void MachineDeckReachesSixDownloadsAndWinsThroughTheAdvertisedActionPipeline()
    {
        var production = LoadProduction();
        var machineIndex = 0;
        var run = RunMatch(
            production,
            machineIndex,
            AcceptanceSeed,
            useAdvertisedPolicy: true,
            scriptWoodOpponent: true);

        Assert.Multiple(() =>
        {
            Assert.That(run.Halted, Is.Empty,
                "the shipped policy must never stall the match: " + run.Trace());
            Assert.That(run.RejectedSubmissions, Is.Empty,
                "no advertised action may be rejected: " + string.Join(" | ", run.RejectedSubmissions));
            Assert.That(run.ActionCounts.TryGetValue(AdvertisedActionPolicy.PullAction, out var pulls), Is.True,
                "the policy must actually select PULL: " + run.Trace());
            Assert.That(pulls, Is.GreaterThanOrEqualTo(6),
                "at least six downloads must be submitted: " + run.Trace());
            Assert.That(run.Final.Phase, Is.EqualTo("OVER"), run.Trace());
            Assert.That(run.Final.WinnerPlayerIndex, Is.EqualTo(machineIndex),
                "the machine must win with its own leader condition: " + run.Trace());
            Assert.That(run.Final.ReasonKey, Does.Contain("pull_total_ge"), run.Trace());
            Assert.That(run.BothLeadersManifestedBeforeSixthPull, Is.True,
                "both real deck leaders must have manifested before the sixth advertised PULL: " + run.Trace());
            Assert.That(run.Final.Players[machineIndex].PullCount, Is.EqualTo(6),
                "the machine leader needs six completed downloads: " + run.Trace());
        });

        TestContext.Out.WriteLine(
            "P0-5 acceptance: WinnerPlayerIndex={0} Phase={1} ReasonKey={2} PullCount={3} Turn={4} "
            + "PULL submissions={5} CloudStackCount={6} rejected={7}",
            run.Final.WinnerPlayerIndex,
            run.Final.Phase,
            run.Final.ReasonKey,
            run.Final.Players[machineIndex].PullCount,
            run.Final.Turn,
            run.ActionCounts.TryGetValue(AdvertisedActionPolicy.PullAction, out var pullSubmissions) ? pullSubmissions : 0,
            run.Final.Players[machineIndex].CloudStackCount,
            run.RejectedSubmissions.Count);
    }

    /// <summary>
    /// Negative control. The retired Unity baseline decided the whole ACTION
    /// phase with FirstNonType(legal, END_TURN) ?? legal[0] over a list sorted
    /// lexicographically by ActionId, so it always submitted the first
    /// play_*/commit_* action and never selected an advertised PULL at all.
    /// </summary>
    [Test]
    public void RetiredFirstAdvertisedActionPolicyNeverReachesSixDownloads()
    {
        var production = LoadProduction();
        var machineIndex = 0;
        var run = RunMatch(production, machineIndex, AcceptanceSeed, useAdvertisedPolicy: false);

        Assert.Multiple(() =>
        {
            Assert.That(run.Halted, Is.Empty,
                "the driver must run the baseline to a real terminal state: " + run.Trace());
            Assert.That(run.ActionCounts.ContainsKey(AdvertisedActionPolicy.PullAction), Is.False,
                "the retired policy never reaches the PULL action ids: " + run.Trace());
            Assert.That(run.Final.Players[machineIndex].PullCount, Is.LessThan(6),
                "the retired policy must miss the six-download win condition: " + run.Trace());
            Assert.That(run.Final.ReasonKey, Is.Not.Null.And.Not.Contain("pull_total_ge"),
                "the retired policy must not produce the pull victory: " + run.Trace());
        });

        TestContext.Out.WriteLine(
            "P0-5 negative control: WinnerPlayerIndex={0} Phase={1} ReasonKey={2} PullCount={3} Turn={4} "
            + "PULL submissions={5} actions={6}",
            run.Final.WinnerPlayerIndex,
            run.Final.Phase,
            run.Final.ReasonKey,
            run.Final.Players[machineIndex].PullCount,
            run.Final.Turn,
            run.ActionCounts.TryGetValue(AdvertisedActionPolicy.PullAction, out var pulls) ? pulls : 0,
            run.Trace());
    }

    /// <summary>
    /// Every submission the driver made must be structurally identical to the
    /// advertisement it came from, which is the machine-checkable form of "the
    /// policy never fabricates a payload".
    /// </summary>
    [Test]
    public void EverySubmittedActionMatchesItsAdvertisementFieldForField()
    {
        var production = LoadProduction();
        var machineIndex = 0;
        var run = RunMatch(
            production,
            machineIndex,
            AcceptanceSeed,
            useAdvertisedPolicy: true,
            scriptWoodOpponent: true);

        Assert.That(run.Submissions, Is.Not.Empty, run.Trace());
        foreach (var submission in run.Submissions)
        {
            Assert.That(submission.Submitted.ActionId, Is.EqualTo(submission.Advertised.ActionId));
            Assert.That(submission.Submitted.Type, Is.EqualTo(submission.Advertised.Type));
            Assert.That(submission.Submitted.Actor, Is.EqualTo(submission.Advertised.Actor));
            Assert.That(submission.Submitted.CardId, Is.EqualTo(submission.Advertised.CardId));
            Assert.That(
                RuntimeActionBoundary.Validate(submission.Submitted, submission.SnapshotForAdvertisement).Accepted,
                Is.True,
                submission.Advertised.ActionId
                + ": the boundary must accept the action exactly as advertised");
            Assert.That(ValuesEqual(submission.Submitted.SourceId, submission.Advertised.SourceId), Is.True,
                submission.Advertised.ActionId + ": source id must be forwarded unchanged");
            Assert.That(ValuesEqual(submission.Submitted.TargetId, submission.Advertised.TargetId), Is.True,
                submission.Advertised.ActionId + ": target id must be forwarded unchanged");
            Assert.That(ValuesEqual(submission.Submitted.Payload, submission.Advertised.Payload), Is.True,
                submission.Advertised.ActionId + ": payload must be forwarded unchanged");
        }

        var pulls = run.Submissions
            .Where(item => item.Advertised.Type == AdvertisedActionPolicy.PullAction)
            .ToArray();
        Assert.That(pulls, Is.Not.Empty, "the acceptance run must contain PULL submissions: " + run.Trace());
        Assert.That(
            pulls.Count(item => item.Advertised.Payload.ContainsKey("selectedEntityIds")),
            Is.GreaterThanOrEqualTo(1),
            "targeted downloads must carry the engine-advertised selectedEntityIds: "
            + string.Join(", ", pulls.Select(item => item.Advertised.ActionId)));
        TestContext.Out.WriteLine(
            "P0-5 payload provenance: {0} submissions compared field-for-field, {1} PULL submissions ({2} with an engine-advertised selectedEntityIds)",
            run.Submissions.Count,
            pulls.Length,
            pulls.Count(item => item.Advertised.Payload.ContainsKey("selectedEntityIds")));
    }

    /// <summary>
    /// Focused unit pin for the release blocker: an advertised targeted
    /// download must be selected and forwarded with the selection the engine
    /// supplied. The policy never supplies selectedEntityIds itself.
    /// </summary>
    [Test]
    public void AdvertisedTargetedDownloadIsChosenAndReachesTheWireUnchanged()
    {
        foreach (var cloudStackCount in new[] { 0, 1 })
        {
            var snapshot = ActionSnapshot(new[] { "END_TURN", "PLAY_CARD", "COMMIT", "ATTACK", "PULL" },
                cloudStackCount: cloudStackCount);
            var policy = new AdvertisedActionPolicy();

            var selected = policy.TryChoose(snapshot, 1, false, out var chosen);

            Assert.That(selected, Is.True);
            Assert.That(chosen, Is.Not.Null);
            if (cloudStackCount == 0)
            {
                // With nothing in the public cloud stack the download cannot
                // resolve, so the policy keeps building instead of stalling.
                Assert.That(chosen!.Type, Is.Not.EqualTo(AdvertisedActionPolicy.PullAction));
                continue;
            }

            Assert.That(chosen!.Type, Is.EqualTo(AdvertisedActionPolicy.PullAction));
            Assert.That(chosen.ActionId, Is.EqualTo("pull_11_22_33"));
            Assert.That(chosen.Payload.ContainsKey("selectedEntityIds"), Is.True,
                "the engine-advertised selection must be part of the advertisement");

            var wire = AdvertisedActionPolicy.ToGameAction(snapshot, chosen);
            Assert.That(RuntimeActionBoundary.Validate(wire, snapshot).Accepted, Is.True);
            Assert.That(wire.ActionId, Is.EqualTo(chosen.ActionId));
            Assert.That(ValuesEqual(wire.Payload, chosen.Payload), Is.True,
                "the submitted payload must be the advertised payload, not a synthesised one");
            Assert.That(
                ((System.Collections.IEnumerable)wire.Payload["selectedEntityIds"]!)
                    .Cast<object>()
                    .Select(Convert.ToInt64)
                    .ToArray(),
                Is.EqualTo(new[] { 33L }));
        }
    }

    /// <summary>
    /// A download the snapshot says cannot resolve drops below play and commit,
    /// and the published ordering is exactly the priority function the policy
    /// uses. The policy never invents a selection for an advertisement.
    /// </summary>
    [Test]
    public void UnusableAdvertisedDownloadIsRankedBelowSubmittableActions()
    {
        var policy = new AdvertisedActionPolicy();

        var emptyCloud = ActionSnapshot(
            new[] { "PULL", "PLAY_CARD", "COMMIT", "ATTACK", "END_TURN" },
            cloudStackCount: 0);
        var probePull = emptyCloud.LegalActions.First(action => action.Type == AdvertisedActionPolicy.PullAction);
        Assert.That(policy.IsUsefulAdvertisedDownload(emptyCloud, probePull), Is.False,
            "an empty cloud stack makes the advertised download useless");

        var expectedOrder = emptyCloud.LegalActions
            .OrderBy(action => policy.AdvertisedActionPriority(emptyCloud, action))
            .Select(action => action.ActionId)
            .ToArray();
        var actualOrder = policy.OrderAdvertisedActions(emptyCloud, emptyCloud.LegalActions)
            .Select(action => action.ActionId)
            .ToArray();
        TestContext.Out.WriteLine(
            "P0-5 ordering: ranks=[{0}] order=[{1}]",
            string.Join(",", emptyCloud.LegalActions.Select(
                action => action.Type + ":" + policy.AdvertisedActionPriority(emptyCloud, action))),
            string.Join(",", actualOrder));
        Assert.That(actualOrder, Is.EqualTo(expectedOrder),
            "the ordered list must follow the published priorities");
        Assert.That(
            policy.OrderAdvertisedActions(emptyCloud, emptyCloud.LegalActions)
                .Select(action => action.Type)
                .ToArray(),
            Is.EqualTo(new[]
            {
                AdvertisedActionPolicy.PlayCardAction,
                AdvertisedActionPolicy.PullAction,
                AdvertisedActionPolicy.CommitAction,
                AdvertisedActionPolicy.AttackAction,
                AdvertisedActionPolicy.EndTurnAction,
            }),
            "an unresolvable download drops below play/commit");

        var tornSelection = ActionSnapshot(
            new[] { "PULL", "END_TURN" },
            includePullSelection: false,
            cloudStackCount: 3);
        // The advertisement carries no selection key at all, which this
        // boundary cannot distinguish from an untargeted download, so the
        // policy does not filter it; it also never invents the selection. The
        // engine stays the authority and rejects it, and the shipped machine
        // deck always advertises selectedEntityIds with its downloads.
        Assert.That(policy.TryChoose(tornSelection, 1, false, out var chosen), Is.True);
        Assert.That(tornSelection.LegalActions.Any(action => action.ActionId == chosen!.ActionId), Is.True,
            "the chosen action is still verbatim from the advertised list");
        Assert.That(chosen!.Payload.ContainsKey("selectedEntityIds"), Is.False,
            "the policy must not synthesise a selection");
    }

    /// <summary>
    /// Three different advertised orderings must all resolve to the same
    /// download, which is the determinism the retired lexical ordering lacked.
    /// </summary>
    [Test]
    public void DownloadPreferenceIsIndependentOfAdvertisedOrdering()
    {
        var orderings = new[]
        {
            new[] { "END_TURN", "PLAY_CARD", "COMMIT", "ATTACK", "PULL" },
            new[] { "PULL", "ATTACK", "COMMIT", "PLAY_CARD", "END_TURN" },
            new[] { "COMMIT", "PULL", "END_TURN", "ATTACK", "PLAY_CARD" },
        };

        foreach (var ordering in orderings)
        {
            var snapshot = ActionSnapshot(ordering, cloudStackCount: 2);
            var policy = new AdvertisedActionPolicy();

            Assert.That(policy.TryChoose(snapshot, 1, false, out var chosen), Is.True,
                string.Join(",", ordering));
            Assert.That(chosen!.ActionId, Is.EqualTo("pull_11_22_33"), string.Join(",", ordering));
        }
    }

    /// <summary>
    /// Builds a synthetic viewer-safe ACTION snapshot from an ordered list of
    /// action types, so the ordering contract can be pinned without a match.
    /// </summary>
    private static RuntimeSnapshotEnvelope ActionSnapshot(
        IReadOnlyList<string> orderedTypes,
        bool includePullSelection = true,
        int cloudStackCount = 1)
    {
        const long revision = 7;
        var legal = new List<RuntimeLegalAction>();
        foreach (var type in orderedTypes)
        {
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
            var actionId = "test_" + type;
            if (type == AdvertisedActionPolicy.PullAction)
            {
                actionId = "pull_11_22_33";
                payload["punish"] = 1;
                if (includePullSelection) payload["selectedEntityIds"] = new[] { 33L };
            }

            legal.Add(new RuntimeLegalAction
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                SnapshotRevision = revision,
                ActionId = actionId,
                Type = type,
                Actor = 1,
                SourceId = type == AdvertisedActionPolicy.PullAction ? 11L : (object?)null,
                TargetId = type == AdvertisedActionPolicy.PullAction ? 22L : (object?)null,
                CardId = type == AdvertisedActionPolicy.PullAction ? "machine_drone" : null,
                ReasonKey = "test." + type,
                Payload = payload,
            });
        }

        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = "match_ai_policy_unit",
            SnapshotRevision = revision,
            Turn = 3,
            Phase = AdvertisedActionPolicy.ActionPhase,
            CurrentPlayer = 1,
            ViewerPlayerId = "player_1",
            Players = new[]
            {
                new RuntimePlayerSnapshot { PlayerId = "player_0" },
                new RuntimePlayerSnapshot { PlayerId = "player_1", CloudStackCount = cloudStackCount },
            },
            Castle = new RuntimeCastleSnapshot(),
            LegalActions = legal,
        };
    }

    private static MatchRun RunMatch(
        Production production,
        int machineIndex,
        ulong seed,
        bool useAdvertisedPolicy,
        bool scriptWoodOpponent = false)
    {
        var runProduction = scriptWoodOpponent
            ? ControlledDrawWoodProduction(production)
            : production;
        var woodDeck = runProduction.WoodDeck;
        var player0 = machineIndex == 0 ? runProduction.MachineDeck : woodDeck;
        var player1 = machineIndex == 0 ? woodDeck : runProduction.MachineDeck;
        var options = new DominionWars.Engine.Setup.MatchSetupOptions
        {
            Seed = seed,
            FirstPlayerIndex = 0,
            OpeningHandSize = 5,
            PlayerLife = 20,
            CastleEnabled = false,
            CastleHealth = 75,
        };

        var gateway = MatchFactory.CreateFromDecks(
            "match_ai_lifecycle",
            runProduction.Catalog,
            player0,
            player1,
            options);

        // MatchSetup.Create leaves the first turn in START; production performs
        // the one-time flow.Advance in RuntimeMatchGateway.Initialize.
        var initialization = gateway.Initialize(0);
        Assert.That(initialization.Accepted, Is.True, initialization.ReasonKey);

        var policy = new AdvertisedActionPolicy();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var submissions = new List<SubmittedAction>();
        var rejected = new List<string>();
        var snapshot = initialization.Snapshot;
        var halted = string.Empty;
        var steps = 0;
        var pullRejections = 0;
        var bothLeadersManifestedBeforeSixthPull = false;
        var controlledDrawCardPlayed = false;

        while (steps++ < 4000)
        {
            if (snapshot.WinnerPlayerIndex.HasValue) break;

            // The v1.31 snapshot only advertises legal actions to the viewer
            // whose turn it is, so the driver reads a snapshot for the current
            // player rather than reusing the submitting player's projection.
            snapshot = gateway.GetSnapshot(snapshot.CurrentPlayer);
            var actor = snapshot.CurrentPlayer;

            RuntimeLegalAction? chosen;
            bool selected;
            if (scriptWoodOpponent && actor != machineIndex)
            {
                chosen = ChooseSafeOpponentAction(
                    snapshot,
                    runProduction.MachineDeck.Leader,
                    woodDeck.Leader,
                    controlledDrawCardPlayed);
                selected = chosen is not null;
            }
            else
            {
                selected = useAdvertisedPolicy
                    ? policy.TryChoose(snapshot, actor, false, out chosen)
                    : RetiredFirstAdvertisedActionPolicy.TryChoose(snapshot, actor, out chosen);
            }
            if (!selected || chosen is null)
            {
                halted = "no-action phase=" + snapshot.Phase
                    + " legal=" + snapshot.LegalActions.Count
                    + " turn=" + snapshot.Turn;
                break;
            }

            if (actor == machineIndex
                && chosen.Type == AdvertisedActionPolicy.PullAction
                && snapshot.Players[machineIndex].PullCount == 5)
            {
                bothLeadersManifestedBeforeSixthPull = BothLeadersManifested(
                    snapshot,
                    runProduction.MachineDeck.Leader,
                    woodDeck.Leader);
            }

            var submissionAction = ToSubmittedGameAction(snapshot, chosen);
            submissions.Add(new SubmittedAction(snapshot, chosen, submissionAction));
            counts[submissionAction.Type] =
                counts.TryGetValue(submissionAction.Type, out var current) ? current + 1 : 1;

            var submission = gateway.Submit(submissionAction);
            if (submission.Result.Accepted
                && string.Equals(submissionAction.CardId, ControlledWoodDrawCardId, StringComparison.Ordinal))
            {
                controlledDrawCardPlayed = true;
            }

            if (!submission.Result.Accepted)
            {
                var isPull = string.Equals(
                    submissionAction.Type,
                    AdvertisedActionPolicy.PullAction,
                    StringComparison.Ordinal);
                if (isPull) pullRejections++;
                rejected.Add("turn=" + snapshot.Turn
                    + " phase=" + snapshot.Phase
                    + " action=" + submissionAction.ActionId
                    + " reason=" + submission.Result.ReasonKey);

                // RuntimeAiTurnCoordinator halts the CPU driver on a rejected
                // submission. The retired policy advertises an unusable PULL
                // (no selectedEntityIds), so that halt is the shipped failure.
                if (isPull) halted = "rejected:" + submission.Result.ReasonKey;
                if (rejected.Count > 20) halted = "too-many-rejections";
                if (!string.IsNullOrEmpty(halted)) break;
            }

            snapshot = gateway.GetSnapshot(submission.Snapshot.CurrentPlayer);
        }

        return new MatchRun(
            machineIndex,
            snapshot,
            counts,
            submissions,
            rejected,
            halted,
            steps,
            pullRejections,
            bothLeadersManifestedBeforeSixthPull);
    }

    private static RuntimeGameAction ToSubmittedGameAction(
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction chosen)
    {
        var action = AdvertisedActionPolicy.ToGameAction(snapshot, chosen);
        if (!RuntimeActionSelection.TryBuildStableSelection(
                chosen,
                out var selectedEntityIds,
                out var reasonKey))
        {
            throw new AssertionException(
                chosen.ActionId + ": malformed selection advertisement: " + reasonKey);
        }

        // Self-discard PLAY_CARD keeps its legacy payload selection for older
        // AI callers. For phase DISCARD, the choice travels in the new typed
        // request channel; never put it into the immutable advertisement.
        if (selectedEntityIds.Count > 0 &&
            !action.Payload.ContainsKey("selectedEntityIds"))
        {
            action.SelectedEntityIds = selectedEntityIds;
        }

        return action;
    }

    private static RuntimeLegalAction? ChooseSafeOpponentAction(
        RuntimeSnapshotEnvelope snapshot,
        string machineLeaderId,
        string woodLeaderId,
        bool controlledDrawCardPlayed)
    {
        if (!controlledDrawCardPlayed
            && string.Equals(snapshot.Phase, AdvertisedActionPolicy.ActionPhase, StringComparison.Ordinal)
            && (!HasManifestedLeader(snapshot.Players[0], machineLeaderId)
                || !HasManifestedLeader(snapshot.Players[1], woodLeaderId)))
        {
            var drawCard = snapshot.LegalActions
                .Where(candidate => string.Equals(candidate.Type, AdvertisedActionPolicy.PlayCardAction, StringComparison.Ordinal)
                    && string.Equals(candidate.CardId, ControlledWoodDrawCardId, StringComparison.Ordinal))
                .OrderBy(candidate => candidate.ActionId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (drawCard is not null) return drawCard;
        }

        var safeTypes = new[]
        {
            AdvertisedActionPolicy.SkipAmbushAction,
            AdvertisedActionPolicy.EndTurnAction,
            AdvertisedActionPolicy.DiscardAction,
        };

        foreach (var type in safeTypes)
        {
            var action = snapshot.LegalActions
                .Where(candidate => string.Equals(candidate.Type, type, StringComparison.Ordinal))
                .OrderBy(candidate => candidate.ActionId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (action is not null) return action;
        }

        return null;
    }

    private static Production ControlledDrawWoodProduction(Production production)
    {
        // The controlled opponent uses a test-only card with only the existing
        // DRAW/OPP_DRAW effects. A single advertised play exposes each player's
        // real leader from that player's deck; production machine cards/deck,
        // advertised policy, and the win rule remain untouched.
        var cards = production.Catalog.Cards.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal);
        cards.Add(
            ControlledWoodDrawCardId,
            new DominionWars.Engine.Model.CardDefinition(
                ControlledWoodDrawCardId,
                "Test controlled DRAW 60",
                type: "SPELL",
                faction: production.WoodDeck.Faction,
                text: "Test-only draw fixture.",
                onPlayEffects: new[]
                {
                    new DominionWars.Engine.Effects.EffectSpec("DRAW", amount: 60),
                    new DominionWars.Engine.Effects.EffectSpec("OPP_DRAW", amount: 60),
                }));
        var catalog = new CardCatalog(cards);
        var deck = new DeckDefinition(
            "test_controlled_wood_draw_punish",
            production.WoodDeck.Faction,
            production.WoodDeck.Leader,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [ControlledWoodDrawCardId] = 58,
                ["wood_punish_wrath"] = 1,
            });
        return new Production(catalog, deck, production.MachineDeck);
    }

    private static bool BothLeadersManifested(
        RuntimeSnapshotEnvelope snapshot,
        string machineLeaderId,
        string woodLeaderId)
    {
        return HasManifestedLeader(snapshot.Players[0], machineLeaderId)
            && HasManifestedLeader(snapshot.Players[1], woodLeaderId);
    }

    private static bool HasManifestedLeader(RuntimePlayerSnapshot player, string leaderId)
    {
        return player.LeaderZone.Concat(player.Field).Concat(player.Ambush)
            .Any(card => string.Equals(card.CardId, leaderId, StringComparison.Ordinal));
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

    private sealed class SubmittedAction
    {
        public SubmittedAction(
            RuntimeSnapshotEnvelope snapshotForAdvertisement,
            RuntimeLegalAction advertised,
            RuntimeGameAction submitted)
        {
            SnapshotForAdvertisement = snapshotForAdvertisement;
            Advertised = advertised;
            Submitted = submitted;
        }

        public RuntimeSnapshotEnvelope SnapshotForAdvertisement { get; }
        public RuntimeLegalAction Advertised { get; }
        public RuntimeGameAction Submitted { get; }
    }

    /// <summary>Structural equality for wire values, mirroring the adapter boundary.</summary>
    private static bool ValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;

        if (left is string leftText || right is string)
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

    private sealed class MatchRun
    {
        public MatchRun(
            int machineIndex,
            RuntimeSnapshotEnvelope final,
            Dictionary<string, int> actionCounts,
            List<SubmittedAction> submissions,
            List<string> rejectedSubmissions,
            string halted,
            int steps,
            int pullRejections,
            bool bothLeadersManifestedBeforeSixthPull)
        {
            MachineIndex = machineIndex;
            Final = final;
            ActionCounts = actionCounts;
            Submissions = submissions;
            RejectedSubmissions = rejectedSubmissions;
            Halted = halted;
            Steps = steps;
            PullRejections = pullRejections;
            BothLeadersManifestedBeforeSixthPull = bothLeadersManifestedBeforeSixthPull;
        }

        public int MachineIndex { get; }
        public RuntimeSnapshotEnvelope Final { get; }
        public Dictionary<string, int> ActionCounts { get; }
        public List<SubmittedAction> Submissions { get; }
        public List<string> RejectedSubmissions { get; }
        public string Halted { get; }
        public int Steps { get; }
        public int PullRejections { get; }
        public bool BothLeadersManifestedBeforeSixthPull { get; }

        public string Trace()
        {
            return "steps=" + Steps
                + " turn=" + Final.Turn
                + " phase=" + Final.Phase
                + " winner=" + (Final.WinnerPlayerIndex.HasValue
                    ? Final.WinnerPlayerIndex.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "none")
                + " reason=" + (Final.ReasonKey ?? "none")
                + " pullCount=" + Final.Players[MachineIndex].PullCount
                + " cloudStack=" + Final.Players[MachineIndex].CloudStackCount
                + " pullRejections=" + PullRejections
                + " bothLeadersBeforeSixthPull=" + BothLeadersManifestedBeforeSixthPull
                + " actions=" + string.Join(",", ActionCounts
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => entry.Key + ":" + entry.Value));
        }
    }

    /// <summary>
    /// Frozen copy of the retired Unity RuntimeAiPolicy ACTION-phase decision:
    /// sort every advertised action lexicographically by ActionId and take the
    /// first action that is not END_TURN. It exists only as the negative
    /// control for the advertised-action policy.
    /// </summary>
    private static class RetiredFirstAdvertisedActionPolicy
    {
        public static bool TryChoose(
            RuntimeSnapshotEnvelope snapshot,
            int playerIndex,
            out RuntimeLegalAction? chosen)
        {
            chosen = null;
            if (snapshot is null || snapshot.LegalActions is null) return false;
            if (playerIndex is < 0 or > 1 || snapshot.CurrentPlayer != playerIndex) return false;
            if (string.Equals(snapshot.Phase, "OVER", StringComparison.Ordinal) ||
                snapshot.WinnerPlayerIndex.HasValue)
                return false;

            var legal = snapshot.LegalActions
                .Where(action => action is not null)
                .OrderBy(action => action.ActionId, StringComparer.Ordinal)
                .ThenBy(action => action.Type, StringComparer.Ordinal)
                .ToList();
            if (legal.Count == 0) return false;

            if (string.Equals(snapshot.Phase, AdvertisedActionPolicy.AmbushPhase, StringComparison.Ordinal))
            {
                chosen = legal.FirstOrDefault(action => action.Type == AdvertisedActionPolicy.SkipAmbushAction)
                    ?? legal[0];
                return true;
            }

            if (string.Equals(snapshot.Phase, AdvertisedActionPolicy.DiscardPhase, StringComparison.Ordinal))
            {
                chosen = legal[0];
                return true;
            }

            if (string.Equals(snapshot.Phase, AdvertisedActionPolicy.ActionPhase, StringComparison.Ordinal))
            {
                chosen = legal.FirstOrDefault(action => action.Type != AdvertisedActionPolicy.EndTurnAction)
                    ?? legal[0];
                return true;
            }

            chosen = legal[0];
            return true;
        }
    }
}

}
