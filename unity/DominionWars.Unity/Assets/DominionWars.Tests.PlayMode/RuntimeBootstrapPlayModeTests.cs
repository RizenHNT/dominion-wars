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
        Assert.That(bootstrap!.Adapter, Is.Not.Null);

        var snapshot = bootstrap.Adapter!.Presentation.Snapshot;
        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.Phase, Is.EqualTo("AMBUSH"));
        Assert.That(snapshot.SnapshotRevision, Is.EqualTo(1));
        Assert.That(snapshot.LegalActions, Is.Not.Null.And.Not.Empty);
        Assert.That(snapshot.LegalActions.Any(action => action.Type == "SKIP_AMBUSH"), Is.True);
        Assert.That(bootstrap.Adapter.Presentation.EventDelta, Is.Not.Empty);
    }

    [UnityTest]
    public IEnumerator RuntimeBattlePanelBindsToSceneBootstrapAdapter()
    {
        yield return LoadRuntimeBootstrapScene();

        var bootstrap = Object.FindFirstObjectByType<RuntimeBootstrap>();
        Assert.That(bootstrap, Is.Not.Null);
        Assert.That(bootstrap!.Adapter, Is.Not.Null);

        // Keep the smoke independent of whether the disposable panel's
        // AfterSceneLoad auto-creator ran before the test scene was loaded.
        // This exercises the same public binding path used by the runtime UI.
        var panelObject = new GameObject(
            "PlayModeRuntimeBattlePanel",
            typeof(RectTransform));
        var panel = panelObject.AddComponent<RuntimeBattlePanel>();
        panel.Bind(bootstrap);
        yield return null;

        Assert.That(panel, Is.Not.Null);
        Assert.That(panel!.Bootstrap, Is.SameAs(bootstrap));
        Assert.That(panel.Adapter, Is.SameAs(bootstrap.Adapter));

        Object.Destroy(panelObject);
        yield return null;
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
            RuntimeBattlePanelView.CreateCardFace(
                view.OwnHandRoot,
                "FirstFrameCard_" + index,
                "card_" + index,
                "#" + index,
                "—",
                "—",
                "—",
                Color.cyan,
                false,
                false,
                true);
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
        // RuntimeBootstrap runs from scene Awake; the extra frame gives the
        // scene and any runtime presentation hooks a stable observation point.
        yield return null;
    }
}
}
