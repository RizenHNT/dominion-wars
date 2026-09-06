#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DominionWars.Unity.PlayMode
{

[TestFixture]
public sealed class RuntimeFullMatchUserJourneyPlayModeTests
{
    private const string RuntimeBootstrapScene = "RuntimeBootstrap";
    private const int MaximumUiActions = 1200;

    [UnityTest]
    [Timeout(180000)]
    public IEnumerator FullMatchCompletesThroughThePlayerFacingUi()
    {
        yield return SceneManager.LoadSceneAsync(RuntimeBootstrapScene, LoadSceneMode.Single);
        yield return null;
        yield return null;

        var flow = UnityEngine.Object.FindFirstObjectByType<RuntimeScreenFlow>();
        Assert.That(flow, Is.Not.Null);
        Assert.That(flow!.CurrentScreen, Is.EqualTo(RuntimeScreenId.Title));
        Assert.That(
            UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None),
            Has.Length.EqualTo(1),
            "A human-facing uGUI journey requires exactly one EventSystem.");

        Click(flow.View.TitleContinueButton.gameObject);
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MainMenu));
        Click(flow.View.MainMenuMatchSetupButton.gameObject);
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MatchSetup));
        Assert.That(flow.View.MatchSetupPlayer0DeckButtons, Has.Count.EqualTo(4));
        Assert.That(flow.View.MatchSetupPlayer1DeckButtons, Has.Count.EqualTo(4));

        // The same deterministic Sea-versus-Wood route used by the live UX
        // run. Both selections are made through their rendered uGUI buttons.
        Click(flow.View.MatchSetupPlayer0DeckButtons[1].gameObject);
        Click(flow.View.MatchSetupPlayer1DeckButtons[2].gameObject);
        Click(flow.View.MatchSetupStartButton.gameObject);
        yield return null;

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(flow.View.MatchSetupErrorText.text, Is.Empty);
        var panel = flow.BattlePanel;
        Assert.That(panel, Is.Not.Null);
        Assert.That(panel!.Adapter, Is.Not.Null);
        Assert.That(panel.Adapter.Presentation.Snapshot, Is.Not.Null);

        var coverage = new HashSet<string>(StringComparer.Ordinal);
        var ambushSetByPlayer = new HashSet<int>();

        var initialSnapshot = panel.Adapter.Presentation.Snapshot!;
        var initialAmbush = initialSnapshot.LegalActions.FirstOrDefault(
            action => action != null && action.Type == "SET_AMBUSH");
        Assert.That(initialAmbush, Is.Not.Null,
            "The selected production deck must expose the ambush interaction used by this journey.");

        var ambushDrag = FindDrag(initialAmbush!.ActionId);
        Assert.That(ambushDrag, Is.Not.Null);
        VerifyCardInspection(ambushDrag!.gameObject);
        VerifyInvalidDragReturnsWithoutAdvancing(panel, ambushDrag);

        var beforeAmbushRevision = panel.Adapter.Presentation.Snapshot!.SnapshotRevision;
        SubmitDragToAdvertisedDropZone(ambushDrag, initialAmbush);
        yield return null;
        Assert.That(
            panel.Adapter.Presentation.Snapshot!.SnapshotRevision,
            Is.GreaterThan(beforeAmbushRevision),
            "A legal ambush drop must advance the authoritative revision.");
        coverage.Add("SET_AMBUSH");
        ambushSetByPlayer.Add(initialSnapshot.CurrentPlayer);

        var submittedActions = 1;
        while (flow.CurrentScreen != RuntimeScreenId.Result && submittedActions < MaximumUiActions)
        {
            var snapshot = panel.Adapter.Presentation.Snapshot;
            Assert.That(snapshot, Is.Not.Null);
            var action = ChooseNextAction(snapshot!, ambushSetByPlayer);
            Assert.That(action, Is.Not.Null,
                $"No usable UI action at revision {snapshot!.SnapshotRevision}, phase {snapshot.Phase}.");

            var beforeRevision = snapshot!.SnapshotRevision;
            SubmitActionThroughUi(action!);
            coverage.Add(action!.Type);
            submittedActions++;
            yield return null;

            if (flow.CurrentScreen == RuntimeScreenId.Result) break;
            var after = panel.Adapter.Presentation.Snapshot;
            Assert.That(after, Is.Not.Null);
            Assert.That(after!.SnapshotRevision, Is.GreaterThan(beforeRevision),
                $"UI action {action.Type}/{action.ActionId} did not advance the authoritative revision.");
        }

        Assert.That(submittedActions, Is.LessThan(MaximumUiActions),
            "The UI journey hit its action limit before reaching a natural result.");
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Result));
        Assert.That(flow.View.ResultWinnerText.text, Is.Not.Empty);
        Assert.That(flow.View.ResultReasonText.text, Is.EqualTo("MATCH COMPLETE"));
        Assert.That(flow.View.ResultReasonText.text, Does.Not.Contain("win."));
        Assert.That(flow.View.DebugResultOutcome, Does.Contain("win.enemy_leader_defeated"));
        Assert.That(panel.Adapter.Presentation.Snapshot!.WinnerPlayerIndex, Is.Not.Null);

        foreach (var required in new[]
                 {
                     "SET_AMBUSH", "SKIP_AMBUSH", "PLAY_CARD", "COMMIT",
                     "END_TURN", "DISCARD", "ATTACK", "PULL",
                 })
        {
            Assert.That(coverage, Does.Contain(required),
                $"The complete UI journey never exercised {required}.");
        }

        Click(flow.View.ResultReturnToMenuButton.gameObject);
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MainMenu));
    }

    private static RuntimeLegalAction ChooseNextAction(
        RuntimeSnapshotEnvelope snapshot,
        ISet<int> ambushSetByPlayer)
    {
        var actions = snapshot.LegalActions
            .Where(action =>
                action != null &&
                RuntimeBattlePanelActionModel.Evaluate(action, snapshot.PendingPrompt).Interactable)
            .ToList();

        RuntimeLegalAction chosen = null;
        if (string.Equals(snapshot.Phase, "AMBUSH", StringComparison.Ordinal))
        {
            if (!ambushSetByPlayer.Contains(snapshot.CurrentPlayer))
            {
                chosen = actions.FirstOrDefault(action => action.Type == "SET_AMBUSH");
                if (chosen != null) ambushSetByPlayer.Add(snapshot.CurrentPlayer);
            }
            return chosen ?? actions.FirstOrDefault(action => action.Type == "SKIP_AMBUSH");
        }

        if (string.Equals(snapshot.Phase, "DISCARD", StringComparison.Ordinal))
            return actions.FirstOrDefault(action => action.Type == "DISCARD");

        return actions.FirstOrDefault(action => action.Type == "ATTACK")
            ?? actions.FirstOrDefault(action => action.Type == "PULL")
            ?? actions.FirstOrDefault(action => action.Type == "PLAY_CARD")
            ?? actions.FirstOrDefault(action => action.Type == "COMMIT")
            ?? actions.FirstOrDefault(action => action.Type == "END_TURN")
            ?? actions.FirstOrDefault();
    }

    private static void VerifyCardInspection(GameObject card)
    {
        var interaction = card.GetComponent<RuntimeCardInspectInteraction>();
        Assert.That(interaction, Is.Not.Null);
        var inspectRoot = FindActive("RuntimeCardInspect", includeInactive: true);
        Assert.That(inspectRoot, Is.Not.Null);

        var pointer = NewPointerData(card);
        ExecuteEvents.Execute(card, pointer, ExecuteEvents.pointerEnterHandler);
        Assert.That(interaction!.IsOpen, Is.True);
        Assert.That(inspectRoot!.activeSelf, Is.True,
            "Hovering a readable card must open the large card reader.");

        ExecuteEvents.Execute(card, pointer, ExecuteEvents.pointerExitHandler);
        Assert.That(interaction.IsOpen, Is.False);
        Assert.That(inspectRoot.activeSelf, Is.False);
    }

    private static void VerifyInvalidDragReturnsWithoutAdvancing(
        RuntimeBattlePanel panel,
        RuntimeBattleCardDrag drag)
    {
        var root = (RectTransform)drag.transform;
        var originalParent = root.parent;
        var originalSiblingIndex = root.GetSiblingIndex();
        var originalRevision = panel.Adapter.Presentation.Snapshot!.SnapshotRevision;
        var pointer = NewPointerData(drag.gameObject);
        pointer.pointerDrag = drag.gameObject;

        ExecuteEvents.Execute(drag.gameObject, pointer, ExecuteEvents.beginDragHandler);
        pointer.position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        ExecuteEvents.Execute(drag.gameObject, pointer, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(drag.gameObject, pointer, ExecuteEvents.endDragHandler);

        Assert.That(panel.Adapter.Presentation.Snapshot!.SnapshotRevision, Is.EqualTo(originalRevision));
        Assert.That(root.parent, Is.SameAs(originalParent));
        Assert.That(root.GetSiblingIndex(), Is.EqualTo(originalSiblingIndex));
        Assert.That(drag.IsDragging, Is.False);
        Assert.That(drag.IsRetired, Is.False);
    }

    private static void SubmitDragToAdvertisedDropZone(
        RuntimeBattleCardDrag drag,
        RuntimeLegalAction action)
    {
        var zone = UnityEngine.Object
            .FindObjectsByType<RuntimeBattleDropZone>(FindObjectsSortMode.None)
            .FirstOrDefault(candidate =>
                candidate.gameObject.activeInHierarchy &&
                string.Equals(candidate.ActionId, action.ActionId, StringComparison.Ordinal) &&
                string.Equals(candidate.ActionType, action.Type, StringComparison.Ordinal));
        Assert.That(zone, Is.Not.Null,
            $"No rendered drop zone exists for {action.Type}/{action.ActionId}.");

        var pointer = NewPointerData(drag.gameObject);
        pointer.pointerDrag = drag.gameObject;
        ExecuteEvents.Execute(drag.gameObject, pointer, ExecuteEvents.beginDragHandler);
        var zoneRoot = (RectTransform)zone!.transform;
        pointer.position = RectTransformUtility.WorldToScreenPoint(
            null,
            zoneRoot.TransformPoint(zoneRoot.rect.center));
        ExecuteEvents.Execute(drag.gameObject, pointer, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(zone.gameObject, pointer, ExecuteEvents.dropHandler);
        ExecuteEvents.Execute(drag.gameObject, pointer, ExecuteEvents.endDragHandler);
    }

    private static RuntimeBattleCardDrag FindDrag(string actionId)
    {
        return UnityEngine.Object
            .FindObjectsByType<RuntimeBattleCardDrag>(FindObjectsSortMode.None)
            .FirstOrDefault(candidate =>
                candidate.gameObject.activeInHierarchy &&
                candidate.Actions.Any(action =>
                    string.Equals(action.ActionId, actionId, StringComparison.Ordinal)));
    }

    private static void SubmitActionThroughUi(RuntimeLegalAction action)
    {
        var suffix = SanitizeName(string.IsNullOrWhiteSpace(action.ActionId)
            ? "unknown"
            : action.ActionId);
        var direct = FindActive("Action_" + suffix);
        if (direct != null)
        {
            AssertInteractableButton(direct);
            Click(direct);
            return;
        }

        var choice = FindActive("Choice_" + suffix);
        Assert.That(choice, Is.Not.Null,
            $"No rendered UI control exists for {action.Type}/{action.ActionId}.");
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

    private static void AssertInteractableButton(GameObject target)
    {
        Assert.That(target.activeInHierarchy, Is.True);
        var button = target.GetComponent<UnityEngine.UI.Button>();
        Assert.That(button, Is.Not.Null);
        Assert.That(button!.interactable, Is.True);
    }

    private static void Click(GameObject target)
    {
        Assert.That(target, Is.Not.Null);
        Assert.That(target.activeInHierarchy, Is.True);
        var pointer = NewPointerData(target);
        var handled = ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
        Assert.That(handled, Is.True, $"{target.name} did not handle a pointer click.");
    }

    private static PointerEventData NewPointerData(GameObject target)
    {
        var eventSystem = EventSystem.current;
        Assert.That(eventSystem, Is.Not.Null);
        var pointer = new PointerEventData(eventSystem)
        {
            button = PointerEventData.InputButton.Left,
        };
        if (target != null && target.transform is RectTransform root)
        {
            pointer.position = RectTransformUtility.WorldToScreenPoint(
                null,
                root.TransformPoint(root.rect.center));
        }
        return pointer;
    }

    private static GameObject FindActive(string name, bool includeInactive = false)
    {
        var roots = UnityEngine.Object.FindObjectsByType<RectTransform>(
            includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        return roots
            .Where(root => root != null && root.name == name)
            .Select(root => root.gameObject)
            .FirstOrDefault(gameObject => includeInactive || gameObject.activeInHierarchy);
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
}
}
#endif
