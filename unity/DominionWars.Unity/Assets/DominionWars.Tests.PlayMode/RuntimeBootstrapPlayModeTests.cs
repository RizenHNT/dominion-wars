using System.Collections;
using System.Linq;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DominionWars.Unity.PlayMode
{

[TestFixture]
public sealed class RuntimeBootstrapPlayModeTests
{
    private const string RuntimeBootstrapScene = "RuntimeBootstrap";

    [UnityTest]
    public IEnumerator RuntimeBootstrapScenePublishesSnapshot()
    {
        yield return LoadRuntimeBootstrapScene();

        var bootstrap = Object.FindFirstObjectByType<RuntimeBootstrap>();
        Assert.That(bootstrap, Is.Not.Null);
        Assert.That(bootstrap!.UseSharedContentContext, Is.True,
            "The checked-in bootstrap scene must use the shared content context by default.");
        Assert.That(bootstrap.SharedContentContext, Is.Not.Null);
        var flow = Object.FindFirstObjectByType<RuntimeScreenFlow>();
        Assert.That(flow, Is.Not.Null);
        Assert.That(flow!.CurrentScreen, Is.EqualTo(RuntimeScreenId.Title));
        Assert.That(bootstrap!.Adapter, Is.Null,
            "The scene bootstrap must defer session creation to MatchSetup.");

        StartMatchThroughScreenFlow(flow);
        yield return null;
        flow.Refresh();

        var snapshot = bootstrap.Adapter!.Presentation.Snapshot;
        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.Phase, Is.EqualTo("AMBUSH"));
        Assert.That(snapshot.SnapshotRevision, Is.EqualTo(1));
        Assert.That(snapshot.LegalActions, Is.Not.Null.And.Not.Empty);
        Assert.That(snapshot.LegalActions.Any(action => action.Type == "SKIP_AMBUSH"), Is.True);
        Assert.That(bootstrap.Adapter.Presentation.Events, Is.Not.Empty,
            "The production panel may refresh the viewer snapshot and clear EventDelta; cumulative Events remain authoritative evidence.");
    }

    [UnityTest]
    public IEnumerator RuntimeBattlePanelBindsToSceneBootstrapAdapter()
    {
        yield return LoadRuntimeBootstrapScene();

        var bootstrap = Object.FindFirstObjectByType<RuntimeBootstrap>();
        Assert.That(bootstrap, Is.Not.Null);
        var flow = Object.FindFirstObjectByType<RuntimeScreenFlow>();
        Assert.That(flow, Is.Not.Null);
        Assert.That(flow!.CurrentScreen, Is.EqualTo(RuntimeScreenId.Title));
        Assert.That(bootstrap!.Adapter, Is.Null,
            "The panel test must also use the deferred MatchSetup boundary.");

        var panels = Object.FindObjectsByType<RuntimeBattlePanel>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        Assert.That(panels, Has.Length.EqualTo(1),
            "The production bootstrap flow must create exactly one battle panel.");
        var panel = panels[0];
        Assert.That(panel.name, Is.EqualTo("DominionWarsRuntimeBattlePanel"));
        Assert.That(flow.BattlePanel, Is.SameAs(panel));
        Assert.That(panel.gameObject.activeSelf, Is.False,
            "The production panel stays hidden until the Battle screen is entered.");

        flow.Navigate(RuntimeScreenId.MainMenu);
        flow.View.MainMenuMatchSetupButton.onClick.Invoke();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MatchSetup));

        flow.View.MatchSetupStartButton.onClick.Invoke();
        yield return null;
        flow.Refresh();

        Assert.That(panel, Is.Not.Null);
        Assert.That(panel!.Bootstrap, Is.SameAs(bootstrap));
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(bootstrap.Adapter, Is.Not.Null);
        Assert.That(panel.Adapter, Is.SameAs(bootstrap.Adapter));
        Assert.That(panel.gameObject.activeSelf, Is.True);
        Assert.That(panel.Adapter!.Presentation.Snapshot, Is.Not.Null);
        Assert.That(flow.View.BattleRoot.gameObject.activeSelf, Is.False,
            "The shell must hand Battle presentation to the production panel.");
    }

    [UnityTest]
    public IEnumerator FormalBootstrapSceneKeepsOneDeferredHostAcrossRefresh()
    {
        yield return LoadRuntimeBootstrapScene();

        var flow = Object.FindFirstObjectByType<RuntimeScreenFlow>();
        Assert.That(flow, Is.Not.Null);
        var hosts = Object.FindObjectsByType<RuntimeBootstrap>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        Assert.That(hosts, Has.Length.EqualTo(1),
            "The production RuntimeBootstrap scene must not receive an auto-created duplicate host.");

        var bootstrap = hosts[0];
        Assert.That(bootstrap.Adapter, Is.Null,
            "The formal scene host must defer session creation until START MATCH.");
        Assert.That(bootstrap.gameObject.scene.name, Is.EqualTo(SceneManager.GetActiveScene().name),
            "The formal host must remain a scene object so a later scene/Play exit can clean it up.");

        flow!.Refresh();
        Assert.That(flow.Bootstrap, Is.SameAs(bootstrap));
        Assert.That(flow.MatchSetupOrchestrator, Is.Not.Null);
        Assert.That(flow.View.MatchSetupPlayer0DeckButtons, Has.Count.EqualTo(4));
        Assert.That(flow.View.MatchSetupPlayer1DeckButtons, Has.Count.EqualTo(4));
        Assert.That(Object.FindObjectsByType<RuntimeBootstrap>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None), Has.Length.EqualTo(1));
        Assert.That(bootstrap.Adapter, Is.Null,
            "Refreshing the production path must not start a hidden session.");
    }

    [UnityTest]
    public IEnumerator MissingSceneBootstrapStillSupportsDeckSelectionAndStart()
    {
        yield return LoadRuntimeBootstrapScene();

        var originalBootstrap = Object.FindFirstObjectByType<RuntimeBootstrap>();
        Assert.That(originalBootstrap, Is.Not.Null);
        Object.Destroy(originalBootstrap!.gameObject);
        yield return null;

        var flow = Object.FindFirstObjectByType<RuntimeScreenFlow>();
        Assert.That(flow, Is.Not.Null);
        flow!.Refresh();
        Assert.That(flow.MatchSetupOrchestrator, Is.Not.Null);
        Assert.That(flow.View.MatchSetupPlayer0DeckButtons, Has.Count.EqualTo(4));
        Assert.That(flow.View.MatchSetupPlayer1DeckButtons, Has.Count.EqualTo(4));
        var autoBootstrap = flow.Bootstrap;
        Assert.That(autoBootstrap, Is.Not.Null);
        Assert.That(autoBootstrap!.Adapter, Is.Null,
            "The fallback host must remain deferred until the explicit START MATCH click.");
        Assert.That(autoBootstrap.gameObject.scene.name, Is.EqualTo(SceneManager.GetActiveScene().name),
            "The fallback host must stay in the active scene and be cleaned with that scene/Play session.");
        Assert.That(Object.FindObjectsByType<RuntimeBootstrap>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None), Has.Length.EqualTo(1));

        flow.Navigate(RuntimeScreenId.MainMenu);
        Click(flow.View.MainMenuMatchSetupButton);
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MatchSetup));

        Click(flow.View.MatchSetupPlayer0DeckButtons[2]);
        Click(flow.View.MatchSetupPlayer1DeckButtons[3]);
        Assert.That(flow.SelectedPlayer0DeckId, Is.EqualTo("sea_deck"));
        Assert.That(flow.SelectedPlayer1DeckId, Is.EqualTo("wood_deck"));

        Click(flow.View.MatchSetupCpuToggle);
        Assert.That(flow.View.MatchSetupCpuToggle.isOn, Is.True);
        Assert.That(flow.SelectedCpuOpponent, Is.True);
        Assert.That(autoBootstrap.CpuOpponent, Is.True);
        Assert.That(flow.View.MatchSetupPlayer0DeckButtons, Has.Count.EqualTo(4),
            "CPU toggle must not remove or disable the player 0 deck rows.");
        Assert.That(flow.View.MatchSetupPlayer1DeckButtons, Has.Count.EqualTo(4),
            "CPU toggle must not remove or disable the player 1 deck rows.");
        Assert.That(flow.SelectedPlayer0DeckId, Is.EqualTo("sea_deck"));
        Assert.That(flow.SelectedPlayer1DeckId, Is.EqualTo("wood_deck"));

        Click(flow.View.MatchSetupStartButton);
        yield return null;
        flow.Refresh();

        var bootstrap = flow.Bootstrap;
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(bootstrap, Is.Not.Null);
        Assert.That(bootstrap!.Adapter, Is.Not.Null);
        Assert.That(bootstrap.Player0DeckId, Is.EqualTo("sea_deck"));
        Assert.That(bootstrap.Player1DeckId, Is.EqualTo("wood_deck"));
        Assert.That(bootstrap.CpuOpponent, Is.True);
        Assert.That(bootstrap.Adapter!.Presentation.Snapshot, Is.Not.Null);
        Assert.That(bootstrap.Adapter.Presentation.Snapshot!.Phase, Is.EqualTo("AMBUSH"));
        Assert.That(Object.FindObjectsByType<RuntimeBootstrap>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None), Has.Length.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator EmptySceneCreatesOneDeferredHostAndFormalSceneReclaimsIt()
    {
        yield return LoadRuntimeBootstrapScene();

        var flow = Object.FindFirstObjectByType<RuntimeScreenFlow>();
        Assert.That(flow, Is.Not.Null);
        var formalScene = SceneManager.GetActiveScene();
        var emptyScene = SceneManager.CreateScene("RuntimeEmptyEntryAudit");
        Assert.That(emptyScene.IsValid, Is.True);
        SceneManager.SetActiveScene(emptyScene);
        var unload = SceneManager.UnloadSceneAsync(formalScene);
        Assert.That(unload, Is.Not.Null);
        yield return unload;
        yield return null;

        flow!.Refresh();
        var hosts = Object.FindObjectsByType<RuntimeBootstrap>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        Assert.That(hosts, Has.Length.EqualTo(1),
            "An empty scene must create one fallback host, not a host per refresh.");
        var emptyBootstrap = hosts[0];
        Assert.That(emptyBootstrap.gameObject.scene.name, Is.EqualTo(emptyScene.name));
        Assert.That(emptyBootstrap.Adapter, Is.Null,
            "An empty-scene fallback must not open a session before START MATCH.");
        Assert.That(flow.MatchSetupOrchestrator, Is.Not.Null);
        Assert.That(flow.View.MatchSetupPlayer0DeckButtons, Has.Count.EqualTo(4));
        Assert.That(flow.View.MatchSetupPlayer1DeckButtons, Has.Count.EqualTo(4));

        yield return LoadRuntimeBootstrapScene();
        Assert.That(emptyBootstrap == null, Is.True,
            "The empty-scene fallback must be reclaimed when the scene is replaced.");
        var formalHosts = Object.FindObjectsByType<RuntimeBootstrap>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        Assert.That(formalHosts, Has.Length.EqualTo(1));
        Assert.That(formalHosts[0].Adapter, Is.Null,
            "Re-entering the formal scene must return to deferred setup, not reuse an old session.");
    }

    [UnityTest]
    public IEnumerator RuntimeCardStripHasVisibleCardsOnFirstRenderedFrameAndAfterResize()
    {
        var canvasObject = new GameObject(
            "PlayModeResponsiveTabletop",
            typeof(RectTransform),
            typeof(Canvas));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var boardObject = new GameObject("ResponsiveBoard", typeof(RectTransform));
        boardObject.transform.SetParent(canvasObject.transform, false);
        var root = boardObject.GetComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = Vector2.zero;
        var view = RuntimeBattlePanelView.Build(root);

        for (var index = 0; index < 6; index++)
        {
            RuntimeCardFaceView.Build(
                view.OwnHandRoot,
                "FirstFrameCard_" + index,
                RuntimeCardFaceMode.Compact);
        }

        RuntimeBattlePanelView.FitCardStrip(view.OwnHandRoot);
        root.sizeDelta = new Vector2(1440f, 900f);

        // Production uses the normal Canvas layout lifecycle; there is no
        // frame-count retry or sleep in the card strip implementation.
        yield return new WaitForEndOfFrame();

        var firstCard = (RectTransform)view.OwnHandRoot.GetChild(0);
        Assert.That(firstCard.rect.width, Is.GreaterThanOrEqualTo(40f));
        Assert.That(firstCard.rect.height, Is.GreaterThanOrEqualTo(44f));
        var wideWidth = firstCard.rect.width;

        root.sizeDelta = new Vector2(1280f, 720f);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(view.ContentRoot);
        LayoutRebuilder.ForceRebuildLayoutImmediate(view.OwnRoot);
        LayoutRebuilder.ForceRebuildLayoutImmediate(view.OwnHandRoot);

        Assert.That(firstCard.rect.width, Is.GreaterThanOrEqualTo(40f));
        Assert.That(firstCard.rect.height, Is.GreaterThanOrEqualTo(44f));
        Assert.That(firstCard.rect.width, Is.LessThan(wideWidth - 0.1f));

        Object.Destroy(canvasObject);
        yield return null;
    }

    private static IEnumerator LoadRuntimeBootstrapScene()
    {
        var load = SceneManager.LoadSceneAsync(RuntimeBootstrapScene, LoadSceneMode.Single);
        Assert.That(load, Is.Not.Null);
        yield return load;
        // ScreenFlow owns the explicit StartMatch boundary; the extra frame
        // gives its title shell and scene hooks a stable observation point.
        yield return null;
    }

    private static void StartMatchThroughScreenFlow(RuntimeScreenFlow flow)
    {
        flow.Navigate(RuntimeScreenId.MainMenu);
        flow.View.MainMenuMatchSetupButton.onClick.Invoke();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MatchSetup));
        Assert.That(flow.View.MatchSetupPlayer0DeckButtons, Has.Count.EqualTo(4));
        Assert.That(flow.View.MatchSetupPlayer1DeckButtons, Has.Count.EqualTo(4));

        // The bootstrap's serialized defaults are the first and second
        // canonical options; no test-only deck or engine call is injected.
        flow.View.MatchSetupStartButton.onClick.Invoke();
    }

    private static void Click(Button button)
    {
        Assert.That(button, Is.Not.Null);
        Assert.That(button.interactable, Is.True, button.name + " must be interactable.");
        Click(button.gameObject, button.name);
    }

    private static void Click(Toggle toggle)
    {
        Assert.That(toggle, Is.Not.Null);
        Assert.That(toggle.interactable, Is.True, toggle.name + " must be interactable.");
        Click(toggle.gameObject, toggle.name);
    }

    private static void Click(GameObject target, string targetName)
    {
        Assert.That(target, Is.Not.Null, targetName + " must exist.");
        var eventSystem = EventSystem.current;
        Assert.That(eventSystem, Is.Not.Null);
        var pointer = new PointerEventData(eventSystem);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
    }
}
}
