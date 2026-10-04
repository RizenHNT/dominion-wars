#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Linq;
using DominionWars.Adapters;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;

namespace DominionWars.Unity.PlayMode
{

[TestFixture]
public sealed class RuntimeScreenFlowPlayModeTests
{
    private const string RuntimeBootstrapScene = "RuntimeBootstrap";

    [UnityTest]
    public IEnumerator BootstrapSceneRoutesToBattleAndReusesTheExistingBattlePanel()
    {
        yield return SceneManager.LoadSceneAsync(RuntimeBootstrapScene, LoadSceneMode.Single);
        yield return null;
        yield return null;

        var flow = UnityEngine.Object.FindFirstObjectByType<RuntimeScreenFlow>();
        Assert.That(flow, Is.Not.Null);
        Assert.That(flow!.CurrentScreen, Is.EqualTo(RuntimeScreenId.Title));
        Assert.That(flow.View, Is.Not.Null);
        Assert.That(flow.IsPresentationReady, Is.True,
            "The Player lifecycle must create and activate the TITLE shell before setup navigation.");
        Assert.That(flow.View.TitleRoot.gameObject.activeSelf, Is.True);
        Assert.That(flow.View.VisibleShellRootCount, Is.EqualTo(1));
        var bootstrap = UnityEngine.Object.FindFirstObjectByType<RuntimeBootstrap>();
        Assert.That(bootstrap, Is.Not.Null);
        Assert.That(bootstrap!.Adapter, Is.Null,
            "ScreenFlow owns the explicit StartMatch session boundary.");
        var panels = UnityEngine.Object.FindObjectsByType<RuntimeBattlePanel>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        Assert.That(panels, Has.Length.EqualTo(1),
            "The production AfterSceneLoad hook must create exactly one battle panel.");
        Assert.That(panels[0].name, Is.EqualTo("DominionWarsRuntimeBattlePanel"));
        foreach (var panel in panels)
        {
            Assert.That(
                panel.name.StartsWith("NON_PRODUCTION_", System.StringComparison.Ordinal),
                Is.False,
                "The production scene flow must not depend on a test-only panel fixture.");
        }

        flow.Navigate(RuntimeScreenId.MainMenu);
        flow.View.MainMenuMatchSetupButton.onClick.Invoke();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MatchSetup));

        flow.Refresh();
        Assert.That(flow.BattlePanel, Is.SameAs(panels[0]));

        flow.View.MatchSetupStartButton.onClick.Invoke();
        yield return null;
        flow.Refresh();

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle),
            "A real runtime start must enter Battle only after its snapshot is ready.");
        Assert.That(flow.BattlePanel, Is.SameAs(panels[0]),
            "ScreenFlow must reuse the production AfterSceneLoad panel.");
        Assert.That(flow.BattlePanel!.gameObject.activeSelf, Is.True);
        Assert.That(flow.View.BattleRoot.gameObject.activeSelf, Is.False,
            "The battle screen must reuse the existing RuntimeBattlePanel root.");

        var runningSnapshot = bootstrap.Adapter!.Presentation.Snapshot!;
        bootstrap.Adapter.AcceptSnapshot(
            SnapshotWithOutcome(runningSnapshot, "OVER", 1, "victory.castle"));
        flow.Refresh();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Result));
        Assert.That(flow.View.ResultWinnerText.text, Is.EqualTo("PLAYER 2 WINS"));
        Assert.That(flow.View.ResultReasonText.text, Is.EqualTo("MATCH COMPLETE"));
        Assert.That(flow.View.ResultReasonText.text, Does.Not.Contain("victory."));
        Assert.That(flow.View.DebugResultOutcome, Is.EqualTo("1 | victory.castle"));
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.True);
        Assert.That(flow.BattlePanel.gameObject.activeSelf, Is.False);

        var previousAdapter = bootstrap.Adapter;
        flow.View.ResultRestartButton.onClick.Invoke();
        yield return null;
        flow.Refresh();

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(bootstrap.Adapter, Is.Not.Null.And.Not.SameAs(previousAdapter));
        Assert.That(flow.BattlePanel, Is.SameAs(panels[0]));
        Assert.That(flow.BattlePanel!.gameObject.activeSelf, Is.True);
        Assert.That(bootstrap.Adapter!.Presentation.Snapshot, Is.Not.Null);
        Assert.That(bootstrap.Adapter.Presentation.Snapshot!.Phase, Is.EqualTo("AMBUSH"));
        Assert.That(bootstrap.Adapter.Presentation.Snapshot.SnapshotRevision, Is.EqualTo(1));
        Assert.That(bootstrap.Adapter.Presentation.Snapshot.WinnerPlayerIndex, Is.Null);
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.False);
        Assert.That(
            () => previousAdapter!.RefreshSnapshot(0),
            Throws.TypeOf<System.ObjectDisposedException>());

        flow.RequestReturnToMenu();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MainMenu));
    }

    [UnityTest]
    public IEnumerator BattleDoesNotAutomaticallyBecomeResultWithoutExplicitInput()
    {
        yield return SceneManager.LoadSceneAsync(RuntimeBootstrapScene, LoadSceneMode.Single);
        yield return null;
        yield return null;

        var flow = UnityEngine.Object.FindFirstObjectByType<RuntimeScreenFlow>();
        Assert.That(flow, Is.Not.Null);
        flow!.ShowBattle();
        yield return null;

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.False);

        var bootstrap = flow.Bootstrap!;
        bootstrap.StartSession();
        flow.Refresh();
        var current = bootstrap.Adapter!.Presentation.Snapshot!;
        bootstrap.Adapter.AcceptSnapshot(
            SnapshotWithOutcome(current, "GAME_OVER", null, null));
        flow.Refresh();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.False);
    }

    [UnityTest]
    [Timeout(180000)]
    public IEnumerator CpuProductionMatchReachesNaturalResultThenRestartsAndReturnsToMenu()
    {
        yield return SceneManager.LoadSceneAsync(RuntimeBootstrapScene, LoadSceneMode.Single);
        yield return null;
        yield return null;

        var flow = UnityEngine.Object.FindFirstObjectByType<RuntimeScreenFlow>();
        Assert.That(flow, Is.Not.Null);
        Assert.That(flow!.CurrentScreen, Is.EqualTo(RuntimeScreenId.Title));

        Click(flow.View.TitleContinueButton.gameObject);
        Click(flow.View.MainMenuMatchSetupButton.gameObject);
        Click(FindActive("MatchSetupPlayer0Deck_machine_deck"));
        Click(FindActive("MatchSetupPlayer1Deck_sea_deck"));
        flow.View.MatchSetupCpuToggle.isOn = true;
        Assert.That(flow.SelectedCpuOpponent, Is.True);
        Click(flow.View.MatchSetupStartButton.gameObject);
        yield return null;

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        var panel = flow.BattlePanel;
        Assert.That(panel, Is.Not.Null);
        Assert.That(panel!.Adapter, Is.Not.Null);
        Assert.That(panel.ViewerPlayerIndex, Is.EqualTo(0));
        Assert.That(panel.FollowCurrentPlayer, Is.False);
        var firstAdapter = panel.Adapter;
        var submittedHumanActions = 0;
        const int maximumHumanActions = 600;

        while (flow.CurrentScreen != RuntimeScreenId.Result &&
               submittedHumanActions < maximumHumanActions)
        {
            yield return null;
            flow.Refresh();
            if (flow.CurrentScreen == RuntimeScreenId.Result)
                break;

            var snapshot = panel.Adapter!.Presentation.Snapshot;
            Assert.That(snapshot, Is.Not.Null);
            if (snapshot!.CurrentPlayer != 0)
                continue;

            var action = ChooseHumanAction(snapshot);
            Assert.That(action, Is.Not.Null,
                "The CPU journey must expose a complete human action at revision " +
                snapshot.SnapshotRevision + " in phase " + snapshot.Phase + ".");
            var beforeRevision = snapshot.SnapshotRevision;
            SubmitActionThroughUi(action!);
            submittedHumanActions++;
            yield return null;

            if (flow.CurrentScreen == RuntimeScreenId.Result)
                break;

            flow.Refresh();
            var after = panel.Adapter!.Presentation.Snapshot;
            Assert.That(after, Is.Not.Null);
            Assert.That(after!.SnapshotRevision, Is.GreaterThan(beforeRevision),
                "The human UI action must advance the authoritative revision.");
        }

        Assert.That(submittedHumanActions, Is.LessThan(maximumHumanActions),
            "The formal CPU match did not reach a natural terminal snapshot.");
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Result));
        Assert.That(panel.Adapter!.Presentation.Snapshot!.Phase, Is.EqualTo("OVER"));
        Assert.That(panel.Adapter.Presentation.Snapshot.WinnerPlayerIndex, Is.Not.Null);
        Assert.That(flow.View.ResultWinnerText.text, Is.Not.Empty);
        Assert.That(flow.View.ResultReasonText.text, Is.Not.Empty);
        Assert.That(flow.View.ResultReasonText.text, Does.Not.Contain("win."));

        Click(flow.View.ResultRestartButton.gameObject);
        yield return null;
        flow.Refresh();

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(flow.SelectedCpuOpponent, Is.True);
        Assert.That(panel.Adapter, Is.Not.Null.And.Not.SameAs(firstAdapter));
        Assert.That(panel.ViewerPlayerIndex, Is.EqualTo(0));
        Assert.That(panel.FollowCurrentPlayer, Is.False);
        Assert.That(panel.Adapter!.Presentation.Snapshot!.Phase, Is.EqualTo("AMBUSH"));
        Assert.That(panel.Adapter.Presentation.Snapshot.WinnerPlayerIndex, Is.Null);

        flow.RequestReturnToMenu();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MainMenu));
        Assert.That(panel.gameObject.activeSelf, Is.False);
    }

    private static RuntimeLegalAction ChooseHumanAction(RuntimeSnapshotEnvelope snapshot)
    {
        var actions = (snapshot.LegalActions ?? Array.Empty<RuntimeLegalAction>())
            .Where(action => action != null)
            .ToList();
        if (string.Equals(snapshot.Phase, "AMBUSH", StringComparison.Ordinal))
            return actions.FirstOrDefault(action => action.Type == "SKIP_AMBUSH") ?? actions.FirstOrDefault();
        if (string.Equals(snapshot.Phase, "DISCARD", StringComparison.Ordinal))
            return actions.FirstOrDefault(action => action.Type == "DISCARD") ?? actions.FirstOrDefault();

        return actions.FirstOrDefault(action => action.Type == "ATTACK")
            ?? actions.FirstOrDefault(action => action.Type == "PULL")
            ?? actions.FirstOrDefault(action => action.Type == "PLAY_CARD")
            ?? actions.FirstOrDefault(action => action.Type == "COMMIT")
            ?? actions.FirstOrDefault(action => action.Type == "END_TURN")
            ?? actions.FirstOrDefault();
    }

    private static void SubmitActionThroughUi(RuntimeLegalAction action)
    {
        if (string.Equals(action.Type, "DISCARD", StringComparison.OrdinalIgnoreCase) &&
            RuntimeBattlePanelActionModel.TryGetSelectionSpec(
                action,
                out var discardSelection,
                out _)
            && discardSelection != null)
        {
            SubmitDiscardThroughUi(action, discardSelection);
            return;
        }

        var suffix = SanitizeName(string.IsNullOrWhiteSpace(action.ActionId)
            ? "unknown"
            : action.ActionId);
        var direct = FindActive("Action_" + suffix);
        if (direct == null)
        {
            var moreActions = FindActive("RuntimeBattlePanelMoreActionsButton");
            if (moreActions != null)
            {
                AssertInteractableButton(moreActions);
                Click(moreActions);
                direct = FindActive("Action_" + suffix);
            }
        }

        if (direct != null)
        {
            AssertInteractableButton(direct);
            Click(direct);
            return;
        }

        var choice = FindActive("Choice_" + suffix);
        Assert.That(choice, Is.Not.Null,
            "No rendered UI control exists for " + action.Type + "/" + action.ActionId + ".");
        var toggle = choice!.GetComponent<UnityEngine.UI.Toggle>();
        Assert.That(toggle, Is.Not.Null);
        Assert.That(toggle!.interactable, Is.True);
        Click(choice);

        var group = choice.transform;
        while (group != null && !group.name.StartsWith("ActionGroup_", StringComparison.Ordinal))
            group = group.parent;
        Assert.That(group, Is.Not.Null);
        var submit = group!.Find("Submit");
        Assert.That(submit, Is.Not.Null);
        AssertInteractableButton(submit!.gameObject);
        Click(submit.gameObject);
    }

    private static void SubmitDiscardThroughUi(
        RuntimeLegalAction action,
        RuntimeActionSelectionSpec selection)
    {
        var suffix = SanitizeName(string.IsNullOrWhiteSpace(action.ActionId)
            ? "unknown"
            : action.ActionId);
        var direct = FindActive("Action_" + suffix);
        if (direct == null)
        {
            var moreActions = FindActive("RuntimeBattlePanelMoreActionsButton");
            if (moreActions != null)
            {
                AssertInteractableButton(moreActions);
                Click(moreActions);
                direct = FindActive("Action_" + suffix);
            }
        }
        Assert.That(direct, Is.Not.Null,
            "No rendered DISCARD action exists for " + action.ActionId + ".");
        AssertInteractableButton(direct!);
        Click(direct);

        for (var index = 0; index < selection.RequiredCount; index++)
        {
            var entityId = selection.CandidateIds[index];
            var candidate = FindVisibleCard(entityId);
            Assert.That(candidate, Is.Not.Null,
                "The advertised discard candidate must be visible in the player's hand: " +
                entityId);
            Click(candidate!.gameObject);
        }

        var confirm = FindActive("SelectionConfirm");
        Assert.That(confirm, Is.Not.Null,
            "DISCARD must expose the explicit selection confirm control.");
        AssertInteractableButton(confirm!);
        Click(confirm);
    }

    private static RuntimeCardInspectInteraction FindVisibleCard(long entityId)
    {
        return UnityEngine.Object.FindObjectsByType<RuntimeCardInspectInteraction>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None)
            .FirstOrDefault(interaction =>
                interaction != null &&
                interaction.gameObject.activeInHierarchy &&
                interaction.Model?.Card?.EntityId == entityId);
    }

    private static void AssertInteractableButton(GameObject target)
    {
        Assert.That(target, Is.Not.Null);
        Assert.That(target.activeInHierarchy, Is.True);
        var button = target.GetComponent<UnityEngine.UI.Button>();
        Assert.That(button, Is.Not.Null);
        Assert.That(button!.interactable, Is.True);
    }

    private static void Click(GameObject target)
    {
        Assert.That(target, Is.Not.Null);
        Assert.That(target.activeInHierarchy, Is.True);
        var eventSystem = EventSystem.current;
        Assert.That(eventSystem, Is.Not.Null);
        var pointer = new PointerEventData(eventSystem)
        {
            button = PointerEventData.InputButton.Left,
        };
        if (target.transform is RectTransform rect)
            pointer.position = RectTransformUtility.WorldToScreenPoint(
                null,
                rect.TransformPoint(rect.rect.center));
        var handled = ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
        Assert.That(handled, Is.True, target.name + " did not handle a pointer click.");
    }

    private static GameObject FindActive(string name)
    {
        var roots = UnityEngine.Object.FindObjectsByType<RectTransform>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        return roots
            .Where(root => root != null && root.name == name)
            .Select(root => root.gameObject)
            .FirstOrDefault(gameObject => gameObject.activeInHierarchy);
    }

    private static string SanitizeName(string value)
    {
        var characters = value.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            var character = characters[index];
            if (!char.IsLetterOrDigit(character) && character != '_' && character != '-')
                characters[index] = '_';
        }
        return new string(characters);
    }

    private static RuntimeSnapshotEnvelope SnapshotWithOutcome(
        RuntimeSnapshotEnvelope source,
        string phase,
        int? winnerPlayerIndex,
        string reasonKey)
    {
        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = source.ContractVersion,
            MatchId = source.MatchId,
            SnapshotRevision = source.SnapshotRevision + 1,
            Turn = source.Turn,
            Phase = phase,
            CurrentPlayer = source.CurrentPlayer,
            ViewerPlayerId = source.ViewerPlayerId,
            Players = source.Players,
            Castle = source.Castle,
            PendingPrompt = source.PendingPrompt,
            LegalActions = source.LegalActions,
            WinnerPlayerIndex = winnerPlayerIndex,
            ReasonKey = reasonKey,
        };
    }
}
}
#endif
