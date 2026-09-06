#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DominionWars.Adapters;
using DominionWars.Data;
using DominionWars.Engine.Localization;
using DominionWars.Engine.Model;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeBattleActionsEditModeTests
{
    [Test]
    public void ActionGroupsExposeOnlyAdvertisedAttackSourceAndTargetPairs()
    {
        var first = Action("attack_7_8", "ATTACK", 7L, 8L);
        var second = Action("attack_7_9", "ATTACK", 7L, 9L);
        var snapshot = Snapshot(first, second);

        var groups = RuntimeBattlePanelActionModel.BuildActionGroups(snapshot);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].Actions, Has.Count.EqualTo(2));
        Assert.That(groups[0].SourceOptions, Has.Count.EqualTo(1));
        Assert.That(groups[0].TargetOptions, Has.Count.EqualTo(2));
        Assert.That(groups[0].SelectedAction.LegalAction.ActionId, Is.EqualTo(first.ActionId));

        Assert.That(groups[0].TrySelectTarget(9L), Is.True);
        Assert.That(groups[0].SelectedAction.LegalAction.ActionId, Is.EqualTo(second.ActionId));
        Assert.That(groups[0].SelectedAction.TargetId, Is.EqualTo(9L));
    }

    [Test]
    public void TargetedPlaySelectsAndSubmitsAnExistingCompleteVariant()
    {
        var face = Action(
            "play_22_core:player_1:life",
            "PLAY_CARD",
            22L,
            "player_1",
            "flame_bolt",
            new Dictionary<string, object?> { ["punish"] = 1 });
        var castle = Action(
            "play_22_core:shared_castle",
            "PLAY_CARD",
            22L,
            "castle",
            "flame_bolt",
            new Dictionary<string, object?> { ["punish"] = 1 });
        var groups = RuntimeBattlePanelActionModel.BuildActionGroups(Snapshot(face, castle));

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].TargetOptions, Has.Count.EqualTo(2));
        Assert.That(groups[0].TrySelectTarget("castle"), Is.True);

        var selected = groups[0].SelectedAction.LegalAction;
        var submission = RuntimeBattlePanelActionModel.ToGameAction(selected, "match_actions");

        Assert.That(selected, Is.SameAs(castle));
        Assert.That(submission.ActionId, Is.EqualTo(castle.ActionId));
        Assert.That(submission.SourceId, Is.EqualTo(22L));
        Assert.That(submission.TargetId, Is.EqualTo("castle"));
        Assert.That(submission.Payload, Is.SameAs(castle.Payload));
    }

    [Test]
    public void PullSourceSelectionKeepsTheAdvertisedTargetAndActionIdentity()
    {
        var first = Action("pull_10_21", "PULL", 10L, 21L, "cloud_card");
        var second = Action("pull_11_21", "PULL", 11L, 21L, "cloud_card");
        var groups = RuntimeBattlePanelActionModel.BuildActionGroups(Snapshot(first, second));

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].RequiresSourceSelection, Is.True);
        Assert.That(groups[0].RequiresTargetSelection, Is.False);
        Assert.That(groups[0].TrySelectSource(11L), Is.True);
        Assert.That(groups[0].SelectedAction.LegalAction.ActionId, Is.EqualTo(second.ActionId));
        Assert.That(groups[0].SelectedAction.TargetId, Is.EqualTo(21L));
    }

    [Test]
    public void DisabledAdvertisedActionCannotBecomeTheSelectedSubmission()
    {
        var disabled = Action(
            "play_7",
            "PLAY_CARD",
            7L,
            null,
            "card",
            new Dictionary<string, object?> { ["requiresTarget"] = true });
        var enabled = Action("end_0", "END_TURN", null, null);
        var groups = RuntimeBattlePanelActionModel.BuildActionGroups(Snapshot(disabled, enabled));

        Assert.That(groups, Has.Count.EqualTo(2));
        var play = groups[0];
        Assert.That(play.SelectedAction.State.Interactable, Is.False);
        Assert.That(play.TrySelectAction(disabled.ActionId), Is.False);
        Assert.That(play.SelectedAction.LegalAction.ActionId, Is.EqualTo(disabled.ActionId));
    }

    [Test]
    public void SelectedCardFiltersUnrelatedActionsButKeepsAdvertisedGlobalActions()
    {
        var first = Action("set_ambush_101", "SET_AMBUSH", 101L, null, "ambush_alpha");
        var second = Action("set_ambush_102", "SET_AMBUSH", 102L, null, "ambush_beta");
        var skip = Action("skip_ambush_0", "SKIP_AMBUSH", null, null);
        var snapshot = Snapshot(first, second, skip);

        var groups = RuntimeBattlePanelActionModel.BuildActionGroups(snapshot, 101L);
        var ids = groups
            .SelectMany(group => group.Actions)
            .Select(entry => entry.LegalAction.ActionId)
            .ToArray();

        Assert.That(ids, Is.EqualTo(new[] { first.ActionId, skip.ActionId }));
        Assert.That(ids, Does.Not.Contain(second.ActionId));

        var noCardAction = RuntimeBattlePanelActionModel.BuildActionGroups(snapshot, 999L);
        var noCardIds = noCardAction
            .SelectMany(group => group.Actions)
            .Select(entry => entry.LegalAction.ActionId)
            .ToArray();
        Assert.That(noCardIds, Is.EqualTo(new[] { skip.ActionId }));
    }

    [Test]
    public void CardSelectionSwitchesActionRailAndSuccessfulClickClearsSelection()
    {
        GameObject panelObject = null!;
        RuntimeAdapter? adapter = null;
        try
        {
            var first = Action("set_ambush_101", "SET_AMBUSH", 101L, null, "ambush_alpha");
            var second = Action("set_ambush_102", "SET_AMBUSH", 102L, null, "ambush_beta");
            var skip = Action("skip_ambush_0", "SKIP_AMBUSH", null, null);
            var initial = SelectionSnapshot(first, second, skip);
            var resulting = SelectionSnapshot(first, second, skip);
            resulting.SnapshotRevision = 1;
            var session = new ButtonSession(
                initial,
                Submission(initial, second, true, resulting, "action.accepted"));
            adapter = new RuntimeAdapter(session);
            adapter.AcceptSnapshot(initial);

            panelObject = CreatePanelObject();
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.SetFollowCurrentPlayer(false);
            panel.Bind(adapter);

            var firstCard = FindCardInteraction(panelObject, first.SourceId);
            var secondCard = FindCardInteraction(panelObject, second.SourceId);

            firstCard.OnPointerClick(null!);
            Assert.That(panel.SelectedCardEntityId, Is.EqualTo(101L));
            Assert.That(ActionIds(panel), Is.EqualTo(new[] { first.ActionId, skip.ActionId }));
            Assert.That(FindButton(panelObject, "Action_" + first.ActionId), Is.Not.Null);
            Assert.That(FindButtonOrNull(panelObject, "Action_" + second.ActionId), Is.Null);

            secondCard.OnPointerClick(null!);
            Assert.That(panel.SelectedCardEntityId, Is.EqualTo(102L));
            Assert.That(ActionIds(panel), Is.EqualTo(new[] { second.ActionId, skip.ActionId }));
            Assert.That(FindButton(panelObject, "Action_" + second.ActionId), Is.Not.Null);
            Assert.That(FindButtonOrNull(panelObject, "Action_" + first.ActionId), Is.Null);

            // The direct button's object/action identity and the submitted
            // wire action must remain the same after card filtering.
            FindButton(panelObject, "Action_" + second.ActionId).onClick.Invoke();
            Assert.That(session.LastAction, Is.Not.Null);
            Assert.That(session.LastAction!.ActionId, Is.EqualTo(second.ActionId));
            Assert.That(panel.SelectedCardEntityId, Is.Null);

            // A fresh click can select the first card again; clicking the same
            // pinned card a second time is the explicit cancellation path.
            var refreshedFirstCard = FindCardInteraction(panelObject, first.SourceId);
            refreshedFirstCard.OnPointerClick(null!);
            Assert.That(panel.SelectedCardEntityId, Is.EqualTo(101L));
            refreshedFirstCard.OnPointerClick(null!);
            Assert.That(panel.SelectedCardEntityId, Is.Null);
            Assert.That(ActionIds(panel), Is.EqualTo(new[]
            {
                first.ActionId, second.ActionId, skip.ActionId,
            }));
        }
        finally
        {
            adapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void PlayerActionLabelUsesReadableSemanticsWithoutWireFields()
    {
        var legal = Action("attack_7_castle", "ATTACK", 7L, "castle");
        var state = RuntimeBattlePanelActionModel.Evaluate(legal);

        var label = RuntimeBattlePanelActionModel.Describe(legal, state);

        Assert.That(label, Is.EqualTo("Attack → Castle"));
        Assert.That(label, Does.Not.Contain("source="));
        Assert.That(label, Does.Not.Contain("target="));
        Assert.That(label, Does.Not.Contain(legal.ActionId));

        var technical = RuntimeBattlePanelActionModel.DescribeTechnical(legal, state);
        Assert.That(technical, Does.Contain("source=7"));
        Assert.That(technical, Does.Contain("target=castle"));
        Assert.That(technical, Does.Contain(legal.ActionId));
    }

    [TestCase("PLAY_CARD", "action.playCard", "Localized play")]
    [TestCase("ATTACK", "action.attack", "Localized attack")]
    [TestCase("END_TURN", "action.endTurn", "Localized end turn")]
    [TestCase("SKIP_AMBUSH", "action.skipAmbush", "Localized skip ambush")]
    public void ResolverAwareDescribeUsesInjectedSemanticTextForApprovedActions(
        string actionType,
        string localizationKey,
        string expectedText)
    {
        var table = new RuntimeLocalizationTable(
            new Dictionary<string, IReadOnlyDictionary<string, string>>(System.StringComparer.Ordinal)
            {
                [localizationKey] = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
                {
                    ["en"] = expectedText,
                },
            });
        var resolver = new RuntimeLocalizationResolver(new Localization(), table);
        var legal = Action("localized_action", actionType, null, null);

        var label = RuntimeBattlePanelActionModel.Describe(
            legal,
            RuntimeBattlePanelActionModel.Evaluate(legal),
            resolver);

        Assert.That(label, Is.EqualTo(expectedText));
    }

    [Test]
    public void UnknownActionAndMissingCardMetadataUseSafeFallbackWithoutOpaqueIds()
    {
        const string unknownAction = "UNREGISTERED_ACTION_42";
        const string opaqueCardId = "opaque_card_42";
        var legal = Action(
            "opaque_action_42",
            unknownAction,
            42L,
            "opaque_target_42",
            opaqueCardId);

        var label = RuntimeBattlePanelActionModel.Describe(
            legal,
            RuntimeBattlePanelActionModel.Evaluate(legal),
            new RuntimeLocalizationResolver());

        Assert.That(label, Is.EqualTo("Unusable → Target"));
        Assert.That(label, Does.Not.Contain(unknownAction));
        Assert.That(label, Does.Not.Contain(opaqueCardId));
        Assert.That(label, Does.Not.Contain(legal.ActionId));
    }

    [TestCase("PLAY_CARD")]
    [TestCase("ATTACK")]
    public void KnownCardAndAttackRetainAuthoredCardPresentationName(string actionType)
    {
        var definition = new CardDefinition(
            "known_action_card",
            "Authored Action Card",
            faction: "烈焰帝国",
            type: "SPELL");
        var catalog = new CardCatalog(
            new Dictionary<string, CardDefinition>(System.StringComparer.Ordinal)
            {
                [definition.Id] = definition,
            });
        var legal = Action(
            "known_action_card_action",
            actionType,
            actionType == "ATTACK" ? 7L : null,
            null,
            definition.Id);

        var label = RuntimeBattlePanelActionModel.Describe(
            legal,
            RuntimeBattlePanelActionModel.Evaluate(legal),
            catalog,
            null,
            null,
            new RuntimeLocalizationResolver());

        Assert.That(label, Does.Contain("Authored Action Card"));
        Assert.That(label, Does.Not.Contain(definition.Id));
    }

    [Test]
    public void TechnicalDescriptorRetainsRawActionCardAndWireIds()
    {
        var legal = Action(
            "technical_action_42",
            "PLAY_CARD",
            42L,
            "opaque_target_42",
            "opaque_card_42");

        var technical = RuntimeBattlePanelActionModel.DescribeTechnical(
            legal,
            RuntimeBattlePanelActionModel.Evaluate(legal));

        Assert.That(technical, Does.Contain("technical_action_42"));
        Assert.That(technical, Does.Contain("opaque_card_42"));
        Assert.That(technical, Does.Contain("source=42"));
        Assert.That(technical, Does.Contain("target=opaque_target_42"));
    }

    [Test]
    public void HotSeatViewerFollowsCurrentPlayerAndRefreshesRedactedSnapshot()
    {
        GameObject panelObject = null!;
        RuntimeAdapter? adapter = null;
        try
        {
            panelObject = new GameObject("RuntimeBattleActionsHotSeatTest");
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            var session = new HotSeatSession();
            adapter = new RuntimeAdapter(session);

            panel.Bind(adapter);
            Assert.That(panel.ViewerPlayerIndex, Is.EqualTo(1));
            Assert.That(adapter.Presentation.Snapshot!.ViewerPlayerId, Is.EqualTo("player_1"));
            Assert.That(adapter.Presentation.Snapshot.Players[1].Hand.Count, Is.EqualTo(1));
            Assert.That(adapter.Presentation.Snapshot.Players[0].Hand, Is.Empty);
            Assert.That(session.RequestedViewers, Is.EqualTo(new[] { 0, 1 }));
        }
        finally
        {
            adapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void EndTurnButtonOnClickSubmitsAdvertisedActionAndRendersAcceptedResult()
    {
        GameObject panelObject = null!;
        RuntimeAdapter? adapter = null;
        try
        {
            var endTurn = Action("end_turn_0", "END_TURN", null, null);
            var initial = Snapshot(endTurn);
            var resulting = SnapshotAt(1, 1, "START");
            var session = new ButtonSession(
                initial,
                Submission(initial, endTurn, true, resulting, "action.accepted"));
            adapter = new RuntimeAdapter(session);
            adapter.AcceptSnapshot(initial);

            panelObject = CreatePanelObject();
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.SetFollowCurrentPlayer(false);
            panel.Bind(adapter);

            var button = FindButton(panelObject, "Action_end_turn_0");
            Assert.That(button.onClick.GetPersistentEventCount(), Is.EqualTo(0));
            button.onClick.Invoke();

            Assert.That(session.LastAction, Is.Not.Null);
            Assert.That(session.LastAction!.ActionId, Is.EqualTo(endTurn.ActionId));
            Assert.That(session.LastAction.Type, Is.EqualTo("END_TURN"));
            Assert.That(panel.LastActionStatus, Is.EqualTo("Action accepted"));
            Assert.That(panel.Adapter!.Presentation.Snapshot!.SnapshotRevision, Is.EqualTo(1));
            Assert.That(panel.ActionGroups, Is.Empty);
        }
        finally
        {
            adapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
        }
    }

    [TestCase("PLAY_CARD", "play_card_7", 7L, "castle")]
    [TestCase("ATTACK", "attack_7_9", 7L, "entity:9")]
    public void PlayAndAttackButtonsSubmitAdvertisedWireAction(
        string type,
        string actionId,
        long sourceId,
        string targetId)
    {
        GameObject panelObject = null!;
        RuntimeAdapter? adapter = null;
        try
        {
            var action = Action(actionId, type, sourceId, targetId, "wire_card");
            var initial = Snapshot(action);
            var resulting = SnapshotAt(1, 0, "ACTION");
            var session = new ButtonSession(
                initial,
                Submission(initial, action, true, resulting, "action.accepted"));
            adapter = new RuntimeAdapter(session);
            adapter.AcceptSnapshot(initial);

            panelObject = CreatePanelObject();
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.SetFollowCurrentPlayer(false);
            panel.Bind(adapter);

            FindButton(panelObject, "Action_" + actionId).onClick.Invoke();

            Assert.That(session.LastAction, Is.Not.Null);
            Assert.That(session.LastAction!.Type, Is.EqualTo(type));
            Assert.That(session.LastAction.ActionId, Is.EqualTo(actionId));
            Assert.That(session.LastAction.SourceId, Is.EqualTo(sourceId));
            Assert.That(session.LastAction.TargetId, Is.EqualTo(targetId));
            Assert.That(panel.LastActionStatus, Is.EqualTo("Action accepted"));
        }
        finally
        {
            adapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void TargetedPlayVariantsRenderAsVisibleDirectButtonsAndPreserveWireTarget()
    {
        GameObject panelObject = null!;
        RuntimeAdapter? adapter = null;
        try
        {
            var castle = Action(
                "play_22_core:shared_castle",
                "PLAY_CARD",
                22L,
                "castle",
                "flame_bolt",
                new Dictionary<string, object?> { ["punish"] = 1 });
            var opponentLife = Action(
                "play_22_core:player_1:life",
                "PLAY_CARD",
                22L,
                "core:player_1:life",
                "flame_bolt",
                new Dictionary<string, object?> { ["punish"] = 1 });
            var initial = Snapshot(castle, opponentLife);
            var resulting = SnapshotAt(1, 0, "ACTION");
            var session = new ButtonSession(
                initial,
                Submission(initial, castle, true, resulting, "action.accepted"));
            adapter = new RuntimeAdapter(session);
            adapter.AcceptSnapshot(initial);

            panelObject = CreatePanelObject();
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.SetFollowCurrentPlayer(false);
            panel.Bind(adapter);

            var button = FindButton(panelObject, "Action_play_22_core_shared_castle");
            Assert.That(button.interactable, Is.True);
            var buttonLabel = button.GetComponentInChildren<UnityEngine.UI.Text>(true)!.text;
            Assert.That(panel.CardCatalog, Is.Not.Null);
            Assert.That(panel.CardCatalog!.TryGetPresentationMetadata(
                castle.CardId!,
                out var cardMetadata), Is.True);
            Assert.That(buttonLabel, Does.Contain("Play"));
            Assert.That(buttonLabel, Does.Contain(cardMetadata!.Name));
            Assert.That(buttonLabel, Does.Contain("Castle"));
            Assert.That(buttonLabel, Does.Not.Contain("source="));
            Assert.That(buttonLabel, Does.Not.Contain("target="));
            Assert.That(buttonLabel, Does.Not.Contain(castle.ActionId));

            button.onClick.Invoke();

            Assert.That(session.LastAction, Is.Not.Null);
            Assert.That(session.LastAction!.ActionId, Is.EqualTo(castle.ActionId));
            Assert.That(session.LastAction.SourceId, Is.EqualTo(22L));
            Assert.That(session.LastAction.TargetId, Is.EqualTo("castle"));
            Assert.That(session.LastAction.Payload, Is.SameAs(castle.Payload));
            Assert.That(panel.LastActionStatus, Is.EqualTo("Action accepted"));
        }
        finally
        {
            adapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void PullChoiceToggleAndSubmitButtonPreserveSelectedSourceAndTarget()
    {
        GameObject panelObject = null!;
        RuntimeAdapter? adapter = null;
        try
        {
            var first = Action("pull_10_21", "PULL", 10L, 21L, "cloud_card");
            var second = Action("pull_11_21", "PULL", 11L, 21L, "cloud_card");
            var initial = Snapshot(first, second);
            var resulting = SnapshotAt(1, 0, "ACTION");
            var session = new ButtonSession(
                initial,
                Submission(initial, second, true, resulting, "action.accepted"));
            adapter = new RuntimeAdapter(session);
            adapter.AcceptSnapshot(initial);

            panelObject = CreatePanelObject();
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.SetFollowCurrentPlayer(false);
            panel.Bind(adapter);

            Assert.That(panel.ActionGroups.Count, Is.EqualTo(1));
            Assert.That(panel.ActionGroups[0].RequiresSourceSelection, Is.True);
            Assert.That(panel.ActionGroups[0].RequiresTargetSelection, Is.False);

            var choice = FindToggle(panelObject, "Choice_pull_11_21");
            choice.onValueChanged.Invoke(true);
            var submit = FindButton(panelObject, "Submit");
            submit.onClick.Invoke();

            Assert.That(session.LastAction, Is.Not.Null);
            Assert.That(session.LastAction!.ActionId, Is.EqualTo(second.ActionId));
            Assert.That(session.LastAction.SourceId, Is.EqualTo(11L));
            Assert.That(session.LastAction.TargetId, Is.EqualTo(21L));
            Assert.That(panel.LastActionStatus, Is.EqualTo("Action accepted"));
        }
        finally
        {
            adapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void RejectedButtonResultRefreshesAdvertisedActionsAndKeepsReasonVisible()
    {
        GameObject panelObject = null!;
        RuntimeAdapter? adapter = null;
        try
        {
            var rejected = Action("play_rejected", "PLAY_CARD", 7L, null, "wood_card");
            var replacement = Action("end_after_reject", "END_TURN", null, null);
            var initial = Snapshot(rejected);
            var refreshed = SnapshotAt(0, 0, "ACTION", replacement);
            replacement.SnapshotRevision = refreshed.SnapshotRevision;
            var session = new ButtonSession(
                initial,
                Submission(initial, rejected, false, refreshed, "action.not_allowed"));
            adapter = new RuntimeAdapter(session);
            adapter.AcceptSnapshot(initial);

            panelObject = CreatePanelObject();
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.SetFollowCurrentPlayer(false);
            panel.Bind(adapter);

            FindButton(panelObject, "Action_play_rejected").onClick.Invoke();

            Assert.That(session.LastAction, Is.Not.Null);
            Assert.That(session.LastAction!.ActionId, Is.EqualTo(rejected.ActionId));
            Assert.That(panel.LastActionStatus, Is.EqualTo("Action rejected"));
            Assert.That(panel.Adapter!.Presentation.Snapshot, Is.SameAs(refreshed));
            Assert.That(panel.ActionGroups.Count, Is.EqualTo(1));
            Assert.That(panel.ActionGroups[0].SelectedAction.LegalAction.ActionId,
                Is.EqualTo(replacement.ActionId));
        }
        finally
        {
            adapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void HotSeatBindRefreshesViewerAndHidesOpponentHandInRenderedText()
    {
        GameObject panelObject = null!;
        RuntimeAdapter? adapter = null;
        try
        {
            var viewer0 = SnapshotAt(0, 1, "ACTION");
            viewer0.ViewerPlayerId = "player_0";
            viewer0.Players = PlayersForViewer(0);
            var viewer1 = SnapshotAt(0, 1, "ACTION");
            viewer1.ViewerPlayerId = "player_1";
            viewer1.Players = PlayersForViewer(1);
            var session = new ViewerButtonSession(viewer0, viewer1);
            adapter = new RuntimeAdapter(session);
            adapter.AcceptSnapshot(viewer0);

            panelObject = CreatePanelObject();
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);

            Assert.That(panel.ViewerPlayerIndex, Is.EqualTo(1));
            Assert.That(session.RequestedViewers, Is.EqualTo(new[] { 0, 1 }));
            var texts = panelObject.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            var ownText = FindText(texts, "RuntimeBattlePanelOwn");
            var opponentText = FindText(texts, "RuntimeBattlePanelOpponent");
            Assert.That(ownText.text, Does.Contain("手牌：卡牌"));
            Assert.That(ownText.text, Does.Not.Contain("only_viewer_1"));
            Assert.That(opponentText.text, Does.Contain("隐藏（仅数量可见）"));
            Assert.That(opponentText.text, Does.Not.Contain("only_viewer_0"));
        }
        finally
        {
            adapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void ExplicitAdapterBindingWinsAndUnbindRestoresAutomaticBootstrapDiscovery()
    {
        GameObject panelObject = null!;
        GameObject bootstrapObject = null!;
        RuntimeAdapter? discoveredAdapter = null;
        RuntimeAdapter? explicitAdapter = null;
        try
        {
            bootstrapObject = new GameObject("RuntimeBattlePanelAutoBootstrap");
            bootstrapObject.SetActive(false);
            var bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();
            discoveredAdapter = new RuntimeAdapter(
                new SnapshotSession(SnapshotAt(0, 0, "BOOTSTRAP")));
            SetBootstrapAdapter(bootstrap, discoveredAdapter);
            bootstrapObject.SetActive(true);

            var explicitAction = Action("explicit_end_turn", "END_TURN", null, null);
            explicitAdapter = new RuntimeAdapter(
                new SnapshotSession(SnapshotAt(0, 0, "EXPLICIT", explicitAction)));

            panelObject = new GameObject("RuntimeBattlePanelExplicitAdapterTest", typeof(RectTransform));
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(explicitAdapter);

            Assert.That(panel.Adapter, Is.SameAs(explicitAdapter));
            Assert.That(panel.Bootstrap, Is.Null);

            panel.Unbind();

            Assert.That(panel.Bootstrap, Is.SameAs(bootstrap));
            Assert.That(panel.Adapter, Is.SameAs(discoveredAdapter));
        }
        finally
        {
            explicitAdapter?.Dispose();
            discoveredAdapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
            if (bootstrapObject != null) Object.DestroyImmediate(bootstrapObject);
        }
    }

    private static RuntimeSnapshotEnvelope Snapshot(params RuntimeLegalAction[] actions)
    {
        return SnapshotAt(0, 0, "ACTION", actions);
    }

    private static RuntimeSnapshotEnvelope SelectionSnapshot(
        params RuntimeLegalAction[] actions)
    {
        var snapshot = Snapshot(actions);
        snapshot.Phase = "AMBUSH";
        snapshot.Players = new[]
        {
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                Hand = new[]
                {
                    new RuntimeCardSnapshot { EntityId = 101, CardId = "ambush_alpha", OwnerPlayer = 0 },
                    new RuntimeCardSnapshot { EntityId = 102, CardId = "ambush_beta", OwnerPlayer = 0 },
                },
            },
            new RuntimePlayerSnapshot { PlayerId = "player_1" },
        };
        return snapshot;
    }

    private static string[] ActionIds(RuntimeBattlePanel panel)
    {
        return panel.ActionGroups
            .SelectMany(group => group.Actions)
            .Select(entry => entry.LegalAction.ActionId)
            .ToArray();
    }

    private static RuntimeSnapshotEnvelope SnapshotAt(
        long revision,
        int currentPlayer,
        string phase,
        params RuntimeLegalAction[] actions)
    {
        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = "match_actions",
            SnapshotRevision = revision,
            Turn = 1,
            Phase = phase,
            CurrentPlayer = currentPlayer,
            ViewerPlayerId = "player_0",
            Players = PlayersForViewer(0),
            LegalActions = actions,
        };
    }

    private static IReadOnlyList<RuntimePlayerSnapshot> PlayersForViewer(int viewerPlayerIndex)
    {
        return new[]
        {
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                Hand = viewerPlayerIndex == 0
                    ? new[] { new RuntimeCardSnapshot { EntityId = 70, CardId = "only_viewer_0" } }
                    : System.Array.Empty<RuntimeCardSnapshot>(),
            },
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_1",
                Hand = viewerPlayerIndex == 1
                    ? new[] { new RuntimeCardSnapshot { EntityId = 71, CardId = "only_viewer_1" } }
                    : System.Array.Empty<RuntimeCardSnapshot>(),
            },
        };
    }

    private static RuntimeActionSubmission Submission(
        RuntimeSnapshotEnvelope initial,
        RuntimeLegalAction action,
        bool accepted,
        RuntimeSnapshotEnvelope resulting,
        string reasonKey)
    {
        return new RuntimeActionSubmission(
            new RuntimeActionResult
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                MatchId = initial.MatchId,
                SnapshotRevision = initial.SnapshotRevision,
                ResultingSnapshotRevision = resulting.SnapshotRevision,
                ActionId = action.ActionId,
                Accepted = accepted,
                ReasonKey = reasonKey,
            },
            System.Array.Empty<DominionWars.Engine.Events.GameEvent>(),
            resulting);
    }

    private static GameObject CreatePanelObject()
    {
        return new GameObject(
            "RuntimeBattleActionButtonTest",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler),
            typeof(UnityEngine.UI.GraphicRaycaster));
    }

    private static UnityEngine.UI.Button FindButton(GameObject root, string name)
    {
        foreach (var button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
        {
            if (button.gameObject.name == name) return button;
        }
        Assert.Fail("Button not found: " + name);
        return null!;
    }

    private static UnityEngine.UI.Button? FindButtonOrNull(GameObject root, string name)
    {
        foreach (var button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
        {
            if (button.gameObject.name == name) return button;
        }
        return null;
    }

    private static RuntimeCardInspectInteraction FindCardInteraction(
        GameObject root,
        object sourceId)
    {
        var source = sourceId is long value ? value : 0L;
        foreach (var interaction in root.GetComponentsInChildren<RuntimeCardInspectInteraction>(true))
        {
            if (interaction.Model?.Card.EntityId == source) return interaction;
        }
        Assert.Fail("Card interaction not found for entity: " + source);
        return null!;
    }

    private static UnityEngine.UI.Toggle FindToggle(GameObject root, string name)
    {
        foreach (var toggle in root.GetComponentsInChildren<UnityEngine.UI.Toggle>(true))
        {
            if (toggle.gameObject.name == name) return toggle;
        }
        Assert.Fail("Toggle not found: " + name);
        return null!;
    }

    private static UnityEngine.UI.Text FindText(
        IReadOnlyList<UnityEngine.UI.Text> texts,
        string parentName)
    {
        foreach (var text in texts)
        {
            if (text.transform.parent != null &&
                text.transform.parent.name == parentName &&
                text.gameObject.name != parentName + "Title")
                return text;
        }
        Assert.Fail("Text not found under: " + parentName);
        return null!;
    }

    private static RuntimeLegalAction Action(
        string actionId,
        string type,
        object? source,
        object? target,
        string? cardId = null,
        IReadOnlyDictionary<string, object?>? payload = null)
    {
        return new RuntimeLegalAction
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            SnapshotRevision = 0,
            ActionId = actionId,
            Type = type,
            Actor = 0,
            SourceId = source,
            TargetId = target,
            CardId = cardId,
            Payload = payload ?? new Dictionary<string, object?>(),
        };
    }

    private sealed class HotSeatSession : IRuntimeSession
    {
        public List<int> RequestedViewers { get; } = new List<int>();

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
        {
            RequestedViewers.Add(viewerPlayerIndex);
            return new RuntimeSnapshotEnvelope
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                MatchId = "match_hotseat",
                SnapshotRevision = 0,
                Turn = 1,
                Phase = "ACTION",
                CurrentPlayer = 1,
                ViewerPlayerId = "player_" + viewerPlayerIndex,
                Players = new[]
                {
                    new RuntimePlayerSnapshot
                    {
                        PlayerId = "player_0",
                        Hand = viewerPlayerIndex == 0
                            ? new[] { new RuntimeCardSnapshot { EntityId = 7, CardId = "only_viewer_0" } }
                            : System.Array.Empty<RuntimeCardSnapshot>(),
                    },
                    new RuntimePlayerSnapshot
                    {
                        PlayerId = "player_1",
                        Hand = viewerPlayerIndex == 1
                            ? new[] { new RuntimeCardSnapshot { EntityId = 8, CardId = "only_viewer_1" } }
                            : System.Array.Empty<RuntimeCardSnapshot>(),
                    },
                },
                LegalActions = System.Array.Empty<RuntimeLegalAction>(),
            };
        }

        public RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            throw new System.NotSupportedException();
        }

        public void Close() { }
    }

    private sealed class ButtonSession : IRuntimeSession
    {
        private RuntimeSnapshotEnvelope _snapshot;
        private readonly RuntimeActionSubmission _submission;

        public ButtonSession(
            RuntimeSnapshotEnvelope snapshot,
            RuntimeActionSubmission submission)
        {
            _snapshot = snapshot;
            _submission = submission;
        }

        public RuntimeGameAction? LastAction { get; private set; }

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex) => _snapshot;

        public RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            LastAction = action;
            _snapshot = _submission.Snapshot;
            return _submission;
        }

        public void Close() { }
    }

    private sealed class ViewerButtonSession : IRuntimeSession
    {
        private readonly RuntimeSnapshotEnvelope[] _snapshots;

        public ViewerButtonSession(
            RuntimeSnapshotEnvelope viewer0,
            RuntimeSnapshotEnvelope viewer1)
        {
            _snapshots = new[] { viewer0, viewer1 };
        }

        public List<int> RequestedViewers { get; } = new List<int>();

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
        {
            RequestedViewers.Add(viewerPlayerIndex);
            return _snapshots[viewerPlayerIndex];
        }

        public RuntimeActionSubmission Submit(RuntimeGameAction action) =>
            throw new System.NotSupportedException();

        public void Close() { }
    }

    private sealed class SnapshotSession : IRuntimeSession
    {
        private readonly RuntimeSnapshotEnvelope _snapshot;

        public SnapshotSession(RuntimeSnapshotEnvelope snapshot)
        {
            _snapshot = snapshot;
        }

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
        {
            return _snapshot;
        }

        public RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            throw new System.NotSupportedException();
        }

        public void Close() { }
    }

    private static void SetBootstrapAdapter(RuntimeBootstrap bootstrap, RuntimeAdapter? adapter)
    {
        var field = typeof(RuntimeBootstrap).GetField(
            "<Adapter>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field!.SetValue(bootstrap, adapter);
    }
}
}
