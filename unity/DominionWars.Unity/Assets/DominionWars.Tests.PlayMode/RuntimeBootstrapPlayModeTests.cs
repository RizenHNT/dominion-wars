using System.Collections;
using System.Linq;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;
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
}
}
