#nullable enable annotations

using DominionWars.Adapters;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeBattleBoardEditModeTests
{
    [Test]
    public void BoardBuildCreatesAnchoredSlotsAndResponsiveCanvas()
    {
        GameObject rootObject = null!;
        try
        {
            rootObject = new GameObject(
                "RuntimeBattleBoardLayoutTest",
                typeof(RectTransform),
                typeof(Canvas));
            var root = rootObject.GetComponent<RectTransform>();

            var view = RuntimeBattlePanelView.Build(root);
            var scaler = rootObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            var raycaster = rootObject.GetComponent<UnityEngine.UI.GraphicRaycaster>();

            Assert.That(scaler, Is.Not.Null);
            Assert.That(scaler!.uiScaleMode, Is.EqualTo(UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize));
            Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1280f, 720f)));
            Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(0.5f));
            Assert.That(raycaster, Is.Not.Null);

            Assert.That(view.ContentRoot.anchorMin, Is.EqualTo(new Vector2(0.03f, 0.03f)));
            Assert.That(view.ContentRoot.anchorMax, Is.EqualTo(new Vector2(0.97f, 0.97f)));
            Assert.That(view.BoardRoot.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>(), Is.Not.Null);
            Assert.That(view.FooterRoot.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>(), Is.Not.Null);
            Assert.That(view.OpponentRoot.GetComponent<UnityEngine.UI.LayoutElement>()!.minHeight, Is.GreaterThan(0f));
            Assert.That(view.CenterRoot.GetComponent<UnityEngine.UI.LayoutElement>()!.minWidth, Is.GreaterThan(0f));
            Assert.That(view.OwnRoot.GetComponent<UnityEngine.UI.LayoutElement>()!.minHeight, Is.GreaterThan(0f));
            Assert.That(view.ActionsScrollRoot.GetComponent<UnityEngine.UI.LayoutElement>()!.minHeight, Is.GreaterThan(0f));
            var scrollRect = view.ActionsScrollRoot.GetComponent<UnityEngine.UI.ScrollRect>();
            Assert.That(scrollRect, Is.Not.Null);
            Assert.That(scrollRect!.viewport, Is.SameAs(view.ActionsViewport));
            Assert.That(scrollRect.content, Is.SameAs(view.ActionsRoot));
            Assert.That(scrollRect.horizontal, Is.False);
            Assert.That(scrollRect.vertical, Is.True);
            Assert.That(view.ActionsViewport.GetComponent<UnityEngine.UI.Image>(), Is.Not.Null);
            Assert.That(view.ActionsViewport.GetComponent<UnityEngine.UI.Mask>(), Is.Not.Null);
            var contentFitter = view.ActionsRoot.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            Assert.That(contentFitter, Is.Not.Null);
            Assert.That(contentFitter!.verticalFit, Is.EqualTo(UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize));
            Assert.That(view.EventsText.rectTransform.GetComponent<UnityEngine.UI.LayoutElement>()!.minHeight, Is.GreaterThan(0f));
        }
        finally
        {
            if (rootObject != null) Object.DestroyImmediate(rootObject);
        }
    }

    [Test]
    public void BoardSnapshotFormattingShowsPhaseTurnAndPlayerAreasWithoutRuleInference()
    {
        var snapshot = new RuntimeSnapshotEnvelope
        {
            MatchId = "board_fixture",
            SnapshotRevision = 7,
            Turn = 3,
            Phase = "ACTION",
            CurrentPlayer = 1,
            ViewerPlayerId = "player_0",
            Players = new[]
            {
                new RuntimePlayerSnapshot
                {
                    PlayerId = "player_0",
                    HandCount = 2,
                    FieldCount = 1,
                    Hand = new[] { new RuntimeCardSnapshot { CardId = "wood_card", EntityId = 4 } },
                },
                new RuntimePlayerSnapshot { PlayerId = "player_1", HandCount = 4 },
            },
            Castle = new RuntimeCastleSnapshot { Enabled = true, Health = 61 },
        };

        var phase = RuntimeBattlePanelPresentationModel.BuildPhaseSummary(snapshot);
        var own = RuntimeBattlePanelPresentationModel.BuildPlayerSection(
            RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, true)!,
            true);
        var opponent = RuntimeBattlePanelPresentationModel.BuildPlayerSection(
            RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, false)!,
            false);

        Assert.That(phase, Does.Contain("阶段 ACTION"));
        Assert.That(phase, Does.Contain("回合 3"));
        Assert.That(phase, Does.Contain("当前玩家 1"));
        Assert.That(own, Does.Contain("统领：当前快照未单列"));
        Assert.That(own, Does.Contain("手牌：wood_card#4"));
        Assert.That(opponent, Does.Contain("统领：当前快照未单列"));
        Assert.That(opponent, Does.Contain("隐藏（仅数量可见）"));
        Assert.That(opponent, Does.Not.Contain("wood_card"));
    }

    [Test]
    public void TabletopBuildContainsCardLanesMirroredPilesAndScrollableSecondaryRail()
    {
        GameObject rootObject = null!;
        try
        {
            rootObject = new GameObject(
                "RuntimeBattleTabletopStructureTest",
                typeof(RectTransform),
                typeof(Canvas));
            var view = RuntimeBattlePanelView.Build(rootObject.transform);

            Assert.That(view.OpponentHandRoot, Is.Not.Null);
            Assert.That(view.OpponentHandRoot.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>(), Is.Not.Null);
            Assert.That(view.OpponentLeaderRoot, Is.Not.Null);
            Assert.That(view.OpponentDeckRoot, Is.Not.Null);
            Assert.That(view.OpponentGraveyardRoot, Is.Not.Null);
            Assert.That(view.OpponentExileRoot, Is.Not.Null);
            Assert.That(view.OpponentPhaseRoot, Is.Not.Null);
            Assert.That(view.CastleRoot, Is.Not.Null);
            Assert.That(view.CastleBarrierRoot, Is.Not.Null);
            Assert.That(view.MechanicalRoot, Is.Not.Null);
            Assert.That(view.OwnFieldRoot, Is.Not.Null);
            Assert.That(view.OwnHandRoot, Is.Not.Null);
            Assert.That(view.OwnLeaderRoot, Is.Not.Null);
            Assert.That(view.OwnDeckRoot, Is.Not.Null);
            Assert.That(view.OwnGraveyardRoot, Is.Not.Null);
            Assert.That(view.OwnExileRoot, Is.Not.Null);
            Assert.That(view.OwnPhaseRoot, Is.Not.Null);
            Assert.That(view.ActionsScrollRoot.GetComponent<UnityEngine.UI.ScrollRect>(), Is.Not.Null);
            Assert.That(view.EventsScrollRoot.GetComponent<UnityEngine.UI.ScrollRect>(), Is.Not.Null);

            var castleTitle = view.CastleRoot.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            Assert.That(castleTitle, Has.Some.Property("text").EqualTo("KINGDOM CORE"));
            Assert.That(castleTitle, Has.Some.Property("text").EqualTo("BARRIER  —"));
        }
        finally
        {
            if (rootObject != null) Object.DestroyImmediate(rootObject);
        }
    }

    [Test]
    public void TabletopAnchorsMirroredSideRailsAroundCenteredBattleAndKeepsPrimaryActionsRight()
    {
        GameObject rootObject = null!;
        try
        {
            rootObject = new GameObject("RuntimeBattleMirroredRailTest", typeof(RectTransform));
            var view = RuntimeBattlePanelView.Build(rootObject.transform);

            Assert.That(view.LeftRailRoot.anchorMin.x, Is.EqualTo(0f));
            Assert.That(view.LeftRailRoot.anchorMax.x, Is.LessThan(view.MainBattleRoot.anchorMin.x));
            Assert.That(view.RightRailRoot.anchorMin.x, Is.GreaterThan(view.MainBattleRoot.anchorMax.x));
            Assert.That(1f - view.RightRailRoot.anchorMin.x, Is.EqualTo(view.LeftRailRoot.anchorMax.x).Within(0.001f));
            Assert.That(view.MainBattleRoot.anchorMin.x, Is.EqualTo(1f - view.MainBattleRoot.anchorMax.x).Within(0.001f));
            Assert.That(view.ActionsArea.anchorMin.x, Is.GreaterThan(view.EventsArea.anchorMax.x));
            Assert.That(view.OpponentHandRoot.anchorMin.x, Is.GreaterThan(view.OpponentLeaderRoot.anchorMax.x));
            Assert.That(view.OwnHandRoot.anchorMin.x, Is.GreaterThan(view.OwnLeaderRoot.anchorMax.x));
        }
        finally
        {
            if (rootObject != null) Object.DestroyImmediate(rootObject);
        }
    }

    [TestCase(1280f, 720f)]
    [TestCase(1440f, 900f)]
    public void TabletopCardsAndPilesStayInsideTheirResponsiveBounds(float width, float height)
    {
        GameObject rootObject = null!;
        try
        {
            rootObject = new GameObject("RuntimeBattleBoundsTest", typeof(RectTransform));
            var root = rootObject.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(width, height);
            var view = RuntimeBattlePanelView.Build(root);

            for (var index = 0; index < 16; index++)
            {
                RuntimeBattlePanelView.CreateCardFace(
                    view.OwnHandRoot,
                    "ResponsiveOwnCard_" + index,
                    "card_" + index,
                    "#" + index,
                    "—",
                    "—",
                    "—",
                    Color.cyan,
                    false,
                    false,
                    false);
            }
            for (var index = 0; index < 8; index++)
                RuntimeBattlePanelView.CreateCardBack(view.OpponentHandRoot, "ResponsiveBack_" + index, false);

            Canvas.ForceUpdateCanvases();
            RuntimeBattlePanelView.FitCardStrip(view.OwnHandRoot);
            RuntimeBattlePanelView.FitCardStrip(view.OpponentHandRoot);
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(view.LeftRailRoot);
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(view.RightRailRoot);
            Canvas.ForceUpdateCanvases();

            AssertChildrenInside(view.OwnHandRoot);
            AssertChildrenInside(view.OpponentHandRoot);
            AssertInside(view.LeftRailRoot, view.OpponentDeckRoot);
            AssertInside(view.LeftRailRoot, view.OpponentGraveyardRoot);
            AssertInside(view.LeftRailRoot, view.OpponentExileRoot);
            AssertInside(view.LeftRailRoot, view.OpponentPhaseRoot);
            AssertInside(view.LeftRailRoot, view.CenterPilesRoot);
            AssertInside(view.LeftRailRoot, view.OwnPhaseRoot);
            AssertInside(view.RightRailRoot, view.MechanicalRoot);
            AssertInside(view.RightRailRoot, view.OwnExileRoot);
            AssertInside(view.RightRailRoot, view.OwnGraveyardRoot);
            AssertInside(view.RightRailRoot, view.OwnDeckRoot);
        }
        finally
        {
            if (rootObject != null) Object.DestroyImmediate(rootObject);
        }
    }

    [TestCase(1280f, 720f)]
    [TestCase(1440f, 900f)]
    public void FirstLayoutPassDoesNotCollapseCardsBuiltBeforeCanvasHasSize(float width, float height)
    {
        GameObject rootObject = null!;
        try
        {
            rootObject = new GameObject(
                "RuntimeBattleFirstLayoutTest",
                typeof(RectTransform),
                typeof(Canvas));
            var root = rootObject.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = Vector2.zero;
            var view = RuntimeBattlePanelView.Build(root);
            CreateOwnHandCards(view.OwnHandRoot, 6, "FirstLayoutCard_");

            // This is the production ordering that previously persisted 1x1
            // preferred sizes before the Canvas had resolved its dimensions.
            RuntimeBattlePanelView.FitCardStrip(view.OwnHandRoot);
            Assert.That(
                view.OwnHandRoot.GetChild(0).GetComponent<UnityEngine.UI.LayoutElement>()!.preferredWidth,
                Is.GreaterThan(1f));

            root.sizeDelta = new Vector2(width, height);
            ResolveTabletopLayout(root, view);

            AssertCardsMeetVisibleMinimum(view.OwnHandRoot);
            AssertChildrenInside(view.OwnHandRoot);
        }
        finally
        {
            if (rootObject != null) Object.DestroyImmediate(rootObject);
        }
    }

    [Test]
    public void CardStripReflowsAfterResizeAndDynamicHandRefresh()
    {
        GameObject rootObject = null!;
        try
        {
            rootObject = new GameObject(
                "RuntimeBattleResizeLayoutTest",
                typeof(RectTransform),
                typeof(Canvas));
            var root = rootObject.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(1440f, 900f);
            var view = RuntimeBattlePanelView.Build(root);
            CreateOwnHandCards(view.OwnHandRoot, 6, "ResizeCard_");
            RuntimeBattlePanelView.FitCardStrip(view.OwnHandRoot);
            ResolveTabletopLayout(root, view);
            var wideCardWidth = ((RectTransform)view.OwnHandRoot.GetChild(0)).rect.width;

            root.sizeDelta = new Vector2(1280f, 720f);
            ResolveTabletopLayout(root, view);
            var narrowCardWidth = ((RectTransform)view.OwnHandRoot.GetChild(0)).rect.width;

            Assert.That(narrowCardWidth, Is.GreaterThanOrEqualTo(40f));
            Assert.That(narrowCardWidth, Is.LessThan(wideCardWidth - 0.1f));
            AssertCardsMeetVisibleMinimum(view.OwnHandRoot);
            AssertChildrenInside(view.OwnHandRoot);

            for (var index = view.OwnHandRoot.childCount - 1; index >= 0; index--)
                Object.DestroyImmediate(view.OwnHandRoot.GetChild(index).gameObject);
            CreateOwnHandCards(view.OwnHandRoot, 8, "RefreshedCard_");
            RuntimeBattlePanelView.FitCardStrip(view.OwnHandRoot);
            ResolveTabletopLayout(root, view);

            Assert.That(view.OwnHandRoot.childCount, Is.EqualTo(8));
            AssertCardsMeetVisibleMinimum(view.OwnHandRoot);
            AssertChildrenInside(view.OwnHandRoot);
        }
        finally
        {
            if (rootObject != null) Object.DestroyImmediate(rootObject);
        }
    }

    [Test]
    public void CardFaceUsesPlaceholdersWhenStatsAreNotInSnapshotContract()
    {
        GameObject rootObject = null!;
        try
        {
            rootObject = new GameObject("RuntimeBattleCardFaceTest", typeof(RectTransform));
            var root = rootObject.GetComponent<RectTransform>();
            var card = RuntimeBattlePanelView.CreateCardFace(
                root,
                "Card",
                "visible_card",
                "#12",
                "—",
                "—",
                "—",
                Color.cyan,
                true,
                false,
                true);

            var texts = card.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            Assert.That(texts, Has.Some.Property("text").EqualTo("visible_card"));
            Assert.That(texts, Has.Some.Property("text").EqualTo("ATK —   HP —"));
            Assert.That(texts, Has.Some.Property("text").EqualTo("—"));
            Assert.That(card.GetComponent<UnityEngine.UI.Outline>()!.effectDistance, Is.EqualTo(new Vector2(3f, 3f)));
        }
        finally
        {
            if (rootObject != null) Object.DestroyImmediate(rootObject);
        }
    }

    private static void AssertChildrenInside(RectTransform parent)
    {
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index) as RectTransform;
            if (child != null && child.gameObject.activeSelf) AssertInside(parent, child);
        }
    }

    private static void CreateOwnHandCards(RectTransform parent, int count, string namePrefix)
    {
        for (var index = 0; index < count; index++)
        {
            RuntimeBattlePanelView.CreateCardFace(
                parent,
                namePrefix + index,
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
    }

    private static void ResolveTabletopLayout(RectTransform root, RuntimeBattlePanelView view)
    {
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(view.ContentRoot);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(view.OwnRoot);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(view.OwnHandRoot);
        Canvas.ForceUpdateCanvases();
    }

    private static void AssertCardsMeetVisibleMinimum(RectTransform parent)
    {
        for (var index = 0; index < parent.childCount; index++)
        {
            var card = (RectTransform)parent.GetChild(index);
            Assert.That(card.rect.width, Is.GreaterThanOrEqualTo(40f), card.name + " width");
            Assert.That(card.rect.height, Is.GreaterThanOrEqualTo(44f), card.name + " height");
        }
    }

    private static void AssertInside(RectTransform parent, RectTransform child)
    {
        var parentCorners = new Vector3[4];
        var childCorners = new Vector3[4];
        parent.GetWorldCorners(parentCorners);
        child.GetWorldCorners(childCorners);
        const float tolerance = 0.5f;
        for (var index = 0; index < childCorners.Length; index++)
        {
            Assert.That(childCorners[index].x, Is.GreaterThanOrEqualTo(parentCorners[0].x - tolerance), child.name + " left bound");
            Assert.That(childCorners[index].x, Is.LessThanOrEqualTo(parentCorners[2].x + tolerance), child.name + " right bound");
            Assert.That(childCorners[index].y, Is.GreaterThanOrEqualTo(parentCorners[0].y - tolerance), child.name + " bottom bound");
            Assert.That(childCorners[index].y, Is.LessThanOrEqualTo(parentCorners[2].y + tolerance), child.name + " top bound");
        }
    }
}
}
