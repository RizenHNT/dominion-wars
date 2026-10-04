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
        var debugPhase = RuntimeBattlePanelPresentationModel.BuildDebugPhaseSummary(snapshot);
        var own = RuntimeBattlePanelPresentationModel.BuildPlayerSection(
            RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, true)!,
            true);
        var debugOwn = RuntimeBattlePanelPresentationModel.BuildDebugPlayerSection(
            RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, true)!,
            true);
        var opponent = RuntimeBattlePanelPresentationModel.BuildPlayerSection(
            RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, false)!,
            false);

        Assert.That(phase, Does.Contain("阶段 ACTION"));
        Assert.That(phase, Does.Contain("回合 3"));
        Assert.That(phase, Does.Not.Contain("当前玩家"));
        Assert.That(debugPhase, Does.Contain("当前玩家 1"));
        Assert.That(own, Does.Not.Contain("Unavailable"));
        Assert.That(own, Does.Not.Contain("统领："));
        Assert.That(own, Does.Not.Contain("除外："));
        Assert.That(own, Does.Contain("手牌：卡牌"));
        Assert.That(own, Does.Not.Contain("wood_card"));
        Assert.That(debugOwn, Does.Contain("手牌：wood_card#4"));
        Assert.That(opponent, Does.Not.Contain("Unavailable"));
        Assert.That(opponent, Does.Not.Contain("统领："));
        Assert.That(opponent, Does.Not.Contain("除外："));
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
            Assert.That(view.OpponentAmbushRoot, Is.Not.Null);
            Assert.That(view.OpponentAmbushCardsRoot, Is.Not.Null);
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
            Assert.That(view.OwnAmbushRoot, Is.Not.Null);
            Assert.That(view.OwnAmbushCardsRoot, Is.Not.Null);
            Assert.That(view.OwnLeaderRoot, Is.Not.Null);
            Assert.That(view.OwnDeckRoot, Is.Not.Null);
            Assert.That(view.OwnGraveyardRoot, Is.Not.Null);
            Assert.That(view.OwnExileRoot, Is.Not.Null);
            Assert.That(view.OwnPhaseRoot, Is.Not.Null);
            Assert.That(view.ActionsScrollRoot.GetComponent<UnityEngine.UI.ScrollRect>(), Is.Not.Null);
            Assert.That(view.EventsScrollRoot.GetComponent<UnityEngine.UI.ScrollRect>(), Is.Not.Null);

            var castleTitle = view.CastleRoot.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            Assert.That(castleTitle, Has.Some.Property("text").EqualTo("KINGDOM CORE"));
            Assert.That(view.CastleBarrierRoot.gameObject.activeSelf, Is.False);
            Assert.That(castleTitle, Has.None.Property("text").Contain("BARRIER"));
            Assert.That(castleTitle, Has.None.Property("text").Contain("Unavailable"));
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
            Assert.That(view.OwnHandScrollRoot.anchorMin.x, Is.GreaterThan(view.OwnLeaderRoot.anchorMax.x));
        }
        finally
        {
            if (rootObject != null) Object.DestroyImmediate(rootObject);
        }
    }

    [Test]
    public void OwnHandUsesClampedHorizontalScrollWithoutShrinkingReadableCards()
    {
        GameObject rootObject = null!;
        try
        {
            rootObject = new GameObject(
                "RuntimeBattleOwnHandScrollStructureTest",
                typeof(RectTransform),
                typeof(Canvas));
            var root = rootObject.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(1280f, 720f);
            var view = RuntimeBattlePanelView.Build(root);

            var scroll = view.OwnHandScrollRoot.GetComponent<UnityEngine.UI.ScrollRect>();
            Assert.That(scroll, Is.SameAs(view.OwnHandScroll));
            Assert.That(scroll, Is.Not.Null);
            Assert.That(scroll!.horizontal, Is.True);
            Assert.That(scroll.vertical, Is.False);
            Assert.That(scroll.movementType, Is.EqualTo(UnityEngine.UI.ScrollRect.MovementType.Clamped));
            Assert.That(scroll.viewport, Is.SameAs(view.OwnHandViewport));
            Assert.That(scroll.content, Is.SameAs(view.OwnHandRoot));
            Assert.That(view.OwnHandScrollbar, Is.Not.Null);
            Assert.That(view.OwnHandRoot.GetComponent<UnityEngine.UI.ContentSizeFitter>()!.horizontalFit,
                Is.EqualTo(UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize));

            CreateOwnHandCards(view.OwnHandRoot, 29, "ScrollableCard_");
            ResolveTabletopLayout(root, view);

            var layout = view.OwnHandRoot.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            Assert.That(layout, Is.Not.Null);
            Assert.That(layout!.spacing, Is.GreaterThanOrEqualTo(0f));
            Assert.That(layout.spacing, Is.EqualTo(6f).Within(0.1f));
            Assert.That(view.OwnHandRoot.rect.width, Is.GreaterThan(view.OwnHandViewport.rect.width));
            Assert.That(view.OwnHandRoot.GetChild(0).GetComponent<UnityEngine.UI.LayoutElement>()!.preferredWidth,
                Is.GreaterThanOrEqualTo(80f));

            var wideViewportWidth = view.OwnHandViewport.rect.width;
            var wideContentWidth = view.OwnHandRoot.rect.width;
            scroll.horizontalNormalizedPosition = 0.5f;
            RuntimeBattlePanelView.FitScrollableCardStrip(
                view.OwnHandScrollRoot,
                view.OwnHandViewport,
                view.OwnHandRoot);
            Assert.That(scroll.horizontalNormalizedPosition, Is.EqualTo(0.5f).Within(0.01f),
                "Snapshot fitting must preserve the user's browse position when content remains present.");

            root.sizeDelta = new Vector2(1024f, 768f);
            ResolveTabletopLayout(root, view);
            var narrowViewportWidth = view.OwnHandViewport.rect.width;
            var narrowContentWidth = view.OwnHandRoot.rect.width;
            Assert.That(narrowViewportWidth, Is.LessThan(wideViewportWidth));
            Assert.That(
                narrowContentWidth - narrowViewportWidth,
                Is.GreaterThan(wideContentWidth - wideViewportWidth),
                "A narrower viewport must expose more horizontal overflow rather than shrinking below the readable minimum.");
            Assert.That(view.OwnHandRoot.GetChild(0).GetComponent<UnityEngine.UI.LayoutElement>()!.preferredWidth,
                Is.GreaterThanOrEqualTo(80f));

            scroll.horizontalNormalizedPosition = 1f;
            Canvas.ForceUpdateCanvases();
            var viewportCorners = new Vector3[4];
            var lastCardCorners = new Vector3[4];
            view.OwnHandViewport.GetWorldCorners(viewportCorners);
            ((RectTransform)view.OwnHandRoot.GetChild(view.OwnHandRoot.childCount - 1)).GetWorldCorners(lastCardCorners);
            Assert.That(lastCardCorners[2].x, Is.LessThanOrEqualTo(viewportCorners[2].x + 0.5f),
                "The last card must be reachable at the clamped horizontal end.");
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
                RuntimeCardFaceView.Build(
                    view.OwnHandRoot,
                    "ResponsiveOwnCard_" + index,
                    RuntimeCardFaceMode.Compact);
            }
            for (var index = 0; index < 8; index++)
                RuntimeBattlePanelView.CreateCardBack(view.OpponentHandRoot, "ResponsiveBack_" + index, false);

            Canvas.ForceUpdateCanvases();
            RuntimeBattlePanelView.FitScrollableCardStrip(
                view.OwnHandScrollRoot,
                view.OwnHandViewport,
                view.OwnHandRoot);
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
            RuntimeBattlePanelView.FitScrollableCardStrip(
                view.OwnHandScrollRoot,
                view.OwnHandViewport,
                view.OwnHandRoot);
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
            RuntimeBattlePanelView.FitScrollableCardStrip(
                view.OwnHandScrollRoot,
                view.OwnHandViewport,
                view.OwnHandRoot);
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
            RuntimeBattlePanelView.FitScrollableCardStrip(
                view.OwnHandScrollRoot,
                view.OwnHandViewport,
                view.OwnHandRoot);
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

    [TestCase(5, 1280f, 720f)]
    [TestCase(5, 1440f, 900f)]
    [TestCase(10, 1280f, 720f)]
    [TestCase(10, 1440f, 900f)]
    public void OwnHandUsesReadableWidthAndKeepsEachLeadingEdgeVisible(
        int cardCount,
        float width,
        float height)
    {
        GameObject rootObject = null!;
        try
        {
            rootObject = new GameObject(
                "RuntimeBattleOwnHandReadabilityTest",
                typeof(RectTransform),
                typeof(Canvas));
            var root = rootObject.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(width, height);
            var view = RuntimeBattlePanelView.Build(root);

            Assert.That(view.OwnHandScrollRoot.anchorMin, Is.EqualTo(new Vector2(0.31f, 0.02f)));
            Assert.That(view.OwnHandScrollRoot.anchorMax, Is.EqualTo(new Vector2(0.99f, 0.76f)));
            Assert.That(view.OwnHandViewport.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(view.OwnHandViewport.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(view.OwnHandRoot.anchorMin, Is.EqualTo(new Vector2(0f, 0f)));
            Assert.That(view.OwnHandRoot.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));

            CreateOwnHandCards(view.OwnHandRoot, cardCount, "ReadabilityCard_");
            ResolveTabletopLayout(root, view);

            Assert.That(view.OwnHandRoot.childCount, Is.EqualTo(cardCount));
            var cards = new RectTransform[cardCount];
            for (var index = 0; index < cardCount; index++)
            {
                cards[index] = (RectTransform)view.OwnHandRoot.GetChild(index);
                Assert.That(cards[index].name, Is.EqualTo("ReadabilityCard_" + index));
                Assert.That(cards[index].rect.height, Is.GreaterThan(0f));
                if (index > 0)
                    Assert.That(
                        cards[index].position.y,
                        Is.EqualTo(cards[0].position.y).Within(0.1f),
                        "The hand must remain one horizontal row.");
            }

            var cardWidth = cards[0].rect.width;
            if (cardCount == 5)
            {
                Assert.That(cardWidth, Is.GreaterThanOrEqualTo(103f));
                Assert.That(cardWidth, Is.LessThanOrEqualTo(112.1f));
            }
            else
            {
                Assert.That(cardWidth, Is.EqualTo(96f).Within(0.1f));
            }

            for (var index = 1; index < cards.Length; index++)
            {
                var previousLeft = WorldLeft(cards[index - 1]);
                var currentLeft = WorldLeft(cards[index]);
                var visibleLeadingEdge = currentLeft - previousLeft;
                Assert.That(
                    visibleLeadingEdge,
                    Is.GreaterThanOrEqualTo(44f),
                    "Each card must retain at least 44 px of visible leading edge.");
                Assert.That(
                    visibleLeadingEdge,
                    Is.GreaterThanOrEqualTo(cardWidth + 5.9f),
                    "The horizontal hand viewport must keep cards separated; browsing replaces overlap.");
            }

            if (cardCount >= 10)
                Assert.That(view.OwnHandRoot.rect.width, Is.GreaterThan(view.OwnHandViewport.rect.width));
        }
        finally
        {
            if (rootObject != null) Object.DestroyImmediate(rootObject);
        }
    }

    [TestCase(1280f, 720f)]
    [TestCase(1440f, 900f)]
    public void ActionRailKeepsFirstAndLastButtonsInsideViewportAcrossScrollRange(
        float width,
        float height)
    {
        GameObject rootObject = null!;
        try
        {
            rootObject = new GameObject(
                "RuntimeBattleActionRailBoundsTest",
                typeof(RectTransform),
                typeof(Canvas));
            var root = rootObject.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(width, height);
            var view = RuntimeBattlePanelView.Build(root);
            var scroll = view.ActionsScrollRoot.GetComponent<UnityEngine.UI.ScrollRect>();
            var layout = view.ActionsRoot.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();

            CreateActionRailTestButton(view.ActionsRoot, "Action_first", "PLAY CARD");
            CreateActionRailTestButton(view.ActionsRoot, "Action_end_turn", "END TURN");
            CreateActionRailTestButton(view.ActionsRoot, "Action_pull", "PULL");
            CreateActionRailTestButton(view.ActionsRoot, "Action_attack", "ATTACK");
            CreateActionRailTestButton(view.ActionsRoot, "Action_last", "END TURN");

            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(view.ActionsScrollRoot);
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(view.ActionsRoot);
            Canvas.ForceUpdateCanvases();

            Assert.That(layout, Is.Not.Null);
            Assert.That(layout!.padding.bottom, Is.GreaterThanOrEqualTo(6));
            Assert.That(view.ActionsRoot.rect.height, Is.GreaterThan(view.ActionsViewport.rect.height));
            var maxScroll = view.ActionsRoot.rect.height - view.ActionsViewport.rect.height;
            Assert.That(maxScroll, Is.GreaterThan(0f));

            scroll!.verticalNormalizedPosition = 1f;
            Canvas.ForceUpdateCanvases();
            AssertVerticalBoundsInside(view.ActionsViewport, (RectTransform)view.ActionsRoot.GetChild(0));
            var topPosition = view.ActionsRoot.anchoredPosition.y;

            scroll.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
            AssertVerticalBoundsInside(view.ActionsViewport, (RectTransform)view.ActionsRoot.GetChild(view.ActionsRoot.childCount - 1));
            var bottomPosition = view.ActionsRoot.anchoredPosition.y;
            Assert.That(Mathf.Abs(bottomPosition - topPosition), Is.EqualTo(maxScroll).Within(0.5f));
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
            var face = RuntimeCardFaceView.Build(
                root,
                "Card",
                RuntimeCardFaceMode.Compact);
            face.Clear();
            face.SetInteractionState(true, false, true);
            var card = face.CardRoot;

            var texts = card.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            Assert.That(texts, Has.Some.Property("text").EqualTo(RuntimeCardDisplayModel.UnknownCard));
            Assert.That(face.AttackLabel.text, Is.EqualTo("ATK"));
            Assert.That(face.StatsRoot.gameObject.activeSelf, Is.False);
            Assert.That(face.AttackValue.text, Is.Empty);
            Assert.That(face.HealthLabel.text, Is.EqualTo("HP"));
            Assert.That(face.HealthValue.text, Is.Empty);
            Assert.That(texts, Has.Some.Property("text").EqualTo(RuntimeCardDisplayModel.Unavailable));
            Assert.That(card.GetComponent<UnityEngine.UI.Outline>()!.effectDistance, Is.EqualTo(new Vector2(4f, 4f)));
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
            RuntimeCardFaceView.Build(
                parent,
                namePrefix + index,
                RuntimeCardFaceMode.Compact);
        }
    }

    private static UnityEngine.UI.Button CreateActionRailTestButton(
        RectTransform parent,
        string name,
        string labelText)
    {
        var rect = RuntimeBattlePanelView.CreateRect(name, parent);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        var label = RuntimeBattlePanelView.CreateText(rect, "Label", 16, Color.white);
        label.alignment = TextAnchor.MiddleCenter;
        label.fontStyle = FontStyle.Bold;
        label.text = labelText;
        var element = rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        element.minHeight = 44f;
        element.preferredHeight = 44f;
        return button;
    }

    private static void ResolveTabletopLayout(RectTransform root, RuntimeBattlePanelView view)
    {
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(view.ContentRoot);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(view.OwnRoot);
        RuntimeBattlePanelView.FitScrollableCardStrip(
            view.OwnHandScrollRoot,
            view.OwnHandViewport,
            view.OwnHandRoot);
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

    private static float WorldLeft(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return corners[0].x;
    }

    private static void AssertVerticalBoundsInside(RectTransform viewport, RectTransform child)
    {
        var viewportCorners = new Vector3[4];
        var childCorners = new Vector3[4];
        viewport.GetWorldCorners(viewportCorners);
        child.GetWorldCorners(childCorners);
        const float tolerance = 0.5f;
        Assert.That(childCorners[0].y, Is.GreaterThanOrEqualTo(viewportCorners[0].y - tolerance), child.name + " bottom bound");
        Assert.That(childCorners[2].y, Is.LessThanOrEqualTo(viewportCorners[2].y + tolerance), child.name + " top bound");
    }
}
}
