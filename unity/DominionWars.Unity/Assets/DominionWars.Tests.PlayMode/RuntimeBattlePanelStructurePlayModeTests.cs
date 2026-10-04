#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using DominionWars.Adapters;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;

namespace DominionWars.Unity.PlayMode
{

[TestFixture]
public sealed class RuntimeBattlePanelStructurePlayModeTests
{
    [UnityTest]
    public IEnumerator FeedbackQueueShowsAuthoritativeRevisionOrderAndFastMode()
    {
        var feedback = new RuntimeBattlePanelActionFeedback();
        var draw = new RuntimeEventEnvelope
        {
            EventId = "evt_000000000201",
            Type = "CARDS_DRAWN",
            Turn = 2,
            Phase = "ACTION",
            SnapshotRevision = 1,
            TargetIds = new object[0],
            Data = new Dictionary<string, object?> { ["count"] = 1 },
        };
        var damage = new RuntimeEventEnvelope
        {
            EventId = "evt_000000000202",
            Type = "DAMAGE_APPLIED",
            Turn = 2,
            Phase = "ACTION",
            SnapshotRevision = 2,
            TargetIds = new object[] { "castle" },
            Data = new Dictionary<string, object?> { ["amount"] = 2 },
        };

        feedback.Consume(new[] { damage, draw });
        Assert.That(feedback.CurrentCue.Kind, Is.EqualTo(RuntimeBattlePanelFeedbackKind.CardsDrawn));
        Assert.That(feedback.PendingCueCount, Is.EqualTo(1));
        Assert.That(feedback.SkipCurrentCue(), Is.True);
        Assert.That(feedback.CurrentCue.Kind, Is.EqualTo(RuntimeBattlePanelFeedbackKind.DamageApplied));

        feedback.SetReducedMotion(true);
        feedback.Consume(new[]
        {
            new RuntimeEventEnvelope
            {
                EventId = "evt_000000000203",
                Type = "GAME_OVER",
                Turn = 2,
                Phase = "OVER",
                SnapshotRevision = 3,
                TargetIds = new object[0],
                Data = new Dictionary<string, object?>(),
            },
        });
        Assert.That(feedback.IsAnimating, Is.False);
        Assert.That(feedback.PendingCueCount, Is.Zero);
        Assert.That(feedback.CurrentCue.Kind, Is.EqualTo(RuntimeBattlePanelFeedbackKind.GameOver));

        yield return null;
    }

    [UnityTest]
    public IEnumerator PlayerEventRailPersistsPublicEventsAfterPanelRender()
    {
        GameObject panelObject = null!;
        RuntimeAdapter adapter = null!;
        try
        {
            var snapshot = new RuntimeSnapshotEnvelope
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                MatchId = "match_public_event_rail",
                SnapshotRevision = 1,
                Turn = 1,
                Phase = "ACTION",
                CurrentPlayer = 0,
                ViewerPlayerId = "player_0",
                Players = new[]
                {
                    new RuntimePlayerSnapshot { PlayerId = "player_0" },
                    new RuntimePlayerSnapshot { PlayerId = "player_1" },
                },
                Castle = new RuntimeCastleSnapshot { Enabled = true, Health = 75 },
                LegalActions = System.Array.Empty<RuntimeLegalAction>(),
            };

            adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
            adapter.AcceptSnapshot(snapshot);

            panelObject = new GameObject(
                "RuntimeBattlePanelPublicEventRail",
                typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);
            panelObject.SetActive(true);

            adapter.ApplyEvents(new[]
            {
                new RuntimeEventEnvelope
                {
                    EventId = "evt_000000000001",
                    Type = "CARDS_DRAWN",
                    Turn = 1,
                    Phase = "ACTION",
                    SnapshotRevision = 2,
                    TargetIds = System.Array.Empty<object?>(),
                    Data = new Dictionary<string, object?> { ["count"] = 2 },
                },
                new RuntimeEventEnvelope
                {
                    EventId = "evt_000000000002",
                    Type = "DAMAGE_APPLIED",
                    Turn = 1,
                    Phase = "ACTION",
                    SnapshotRevision = 2,
                    TargetIds = new object?[] { "castle" },
                    Data = new Dictionary<string, object?> { ["amount"] = 3 },
                },
            });

            yield return null;

            Assert.That(panel.View.EventsText.text, Does.Contain("CARDS DRAWN 2"));
            Assert.That(panel.View.EventsText.text, Does.Contain("DAMAGE 3"));
            Assert.That(panel.View.EventsText.text, Does.Not.Contain("evt_000000000001"));
            Assert.That(panel.View.EventsText.text, Does.Not.Contain("evt_000000000002"));
        }
        finally
        {
            if (adapter != null) adapter.Dispose();
            if (panelObject != null) Object.Destroy(panelObject);
        }
    }

    [UnityTest]
    public IEnumerator TabletopStructureIsVisibleAtBothApprovedViewports()
    {
        GameObject canvasObject = null!;
        try
        {
            canvasObject = new GameObject(
                "RuntimeBattlePanelStructurePlayModeCanvas",
                typeof(RectTransform),
                typeof(Canvas));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var root = canvasObject.GetComponent<RectTransform>();
            var view = RuntimeBattlePanelView.Build(root);

            foreach (var size in new[] { new Vector2(1280f, 720f), new Vector2(1440f, 900f) })
            {
                root.sizeDelta = size;
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(view.ContentRoot);
                yield return null;

                Assert.That(view.OpponentLeaderRoot.rect.width, Is.GreaterThan(0f));
                Assert.That(view.OpponentLeaderRoot.rect.height, Is.GreaterThan(0f));
                Assert.That(view.OwnLeaderRoot.rect.width, Is.GreaterThan(0f));
                Assert.That(view.OwnLeaderRoot.rect.height, Is.GreaterThan(0f));
                Assert.That(view.CastleRoot.rect.width, Is.GreaterThan(0f));
                Assert.That(view.CastleRoot.rect.height, Is.GreaterThan(0f));
                Assert.That(view.ActionsArea.rect.width, Is.GreaterThan(0f));
                Assert.That(view.ActionsArea.rect.height, Is.GreaterThan(0f));
                Assert.That(view.EventsArea.rect.width, Is.GreaterThan(0f));
                Assert.That(view.EventsArea.rect.height, Is.GreaterThan(0f));
                Assert.That(view.MainBattleRoot.Find("LegalTargetSurfaces"), Is.Not.Null);
            }
        }
        finally
        {
            if (canvasObject != null) Object.Destroy(canvasObject);
        }
    }

    [UnityTest]
    public IEnumerator ScrollableOwnHandKeepsEveryVisibleCardRaycastable()
    {
        GameObject canvasObject = null!;
        GameObject eventSystemObject = null!;
        try
        {
            canvasObject = new GameObject(
                "RuntimeBattleOwnHandEventSystemReadabilityCanvas",
                typeof(RectTransform),
                typeof(Canvas));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var root = canvasObject.GetComponent<RectTransform>();
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                eventSystemObject = new GameObject(
                    "RuntimeBattleOwnHandEventSystemReadabilityEventSystem",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule));
                eventSystem = eventSystemObject.GetComponent<EventSystem>();
            }

            var view = RuntimeBattlePanelView.Build(root);
            for (var index = 0; index < 10; index++)
            {
                RuntimeCardFaceView.Build(
                    view.OwnHandRoot,
                    "EventSystemReadabilityCard_" + index,
                    RuntimeCardFaceMode.Compact);
            }

            foreach (var size in new[] { new Vector2(1280f, 720f), new Vector2(1440f, 900f) })
            {
                root.sizeDelta = size;
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(view.ContentRoot);
                LayoutRebuilder.ForceRebuildLayoutImmediate(view.OwnRoot);
                RuntimeBattlePanelView.FitScrollableCardStrip(
                    view.OwnHandScrollRoot,
                    view.OwnHandViewport,
                    view.OwnHandRoot);
                LayoutRebuilder.ForceRebuildLayoutImmediate(view.OwnHandRoot);
                yield return null;

                var scroll = view.OwnHandScroll!;
                Assert.That(scroll.horizontal, Is.True);
                Assert.That(scroll.vertical, Is.False);
                Assert.That(view.OwnHandRoot.rect.width, Is.GreaterThan(view.OwnHandViewport.rect.width),
                    "Ten cards must overflow the viewport so the user can browse without shrinking them.");

                foreach (var normalizedPosition in new[] { 0f, 0.5f, 1f })
                {
                    scroll.horizontalNormalizedPosition = normalizedPosition;
                    Canvas.ForceUpdateCanvases();
                    yield return null;

                    var viewportCorners = new Vector3[4];
                    view.OwnHandViewport.GetWorldCorners(viewportCorners);
                    var visibleCards = 0;
                    var pointerEvent = new PointerEventData(eventSystem!);
                    for (var index = 0; index < view.OwnHandRoot.childCount; index++)
                    {
                        var card = (RectTransform)view.OwnHandRoot.GetChild(index);
                        var cardCorners = new Vector3[4];
                        card.GetWorldCorners(cardCorners);
                        var center = (cardCorners[0] + cardCorners[2]) * 0.5f;
                        if (center.x < viewportCorners[0].x || center.x > viewportCorners[2].x)
                            continue;
                        visibleCards++;

                        Assert.That(card.rect.width, Is.GreaterThanOrEqualTo(96f),
                            "Hand cards must keep the readable minimum width while scrolling.");

                        pointerEvent.position = center;
                        var raycasts = new List<RaycastResult>();
                        eventSystem.RaycastAll(pointerEvent, raycasts);
                        Assert.That(
                            raycasts,
                            Has.Some.Property("gameObject").SameAs(card.gameObject),
                            "Visible card " + index + " must remain reachable at " + size.x + "x" + size.y +
                            " scroll=" + normalizedPosition + ".");
                    }

                    Assert.That(visibleCards, Is.GreaterThan(0),
                        "Each scroll position must expose at least one hand card at " + size.x + "x" + size.y + ".");
                }

                var scrollPositionBeforeResize = scroll.horizontalNormalizedPosition;
                Assert.That(scrollPositionBeforeResize, Is.EqualTo(1f).Within(0.01f));
                var viewportWidth = view.OwnHandViewport.rect.width;
                view.OwnHandViewport.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Horizontal,
                    viewportWidth + 16f);
                Canvas.ForceUpdateCanvases();
                RuntimeBattlePanelView.FitScrollableCardStrip(
                    view.OwnHandScrollRoot,
                    view.OwnHandViewport,
                    view.OwnHandRoot);
                Canvas.ForceUpdateCanvases();
                yield return null;
                Assert.That(
                    scroll.horizontalNormalizedPosition,
                    Is.EqualTo(scrollPositionBeforeResize).Within(0.01f),
                    "A narrowed overflow strip must preserve its horizontal position after refit.");

                view.OwnHandViewport.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Horizontal,
                    viewportWidth);
                Canvas.ForceUpdateCanvases();
                RuntimeBattlePanelView.FitScrollableCardStrip(
                    view.OwnHandScrollRoot,
                    view.OwnHandViewport,
                    view.OwnHandRoot);
                Canvas.ForceUpdateCanvases();
                yield return null;
                Assert.That(
                    scroll.horizontalNormalizedPosition,
                    Is.EqualTo(scrollPositionBeforeResize).Within(0.01f),
                    "Returning to the original width must not drift a nonzero hand position.");
            }
        }
        finally
        {
            if (canvasObject != null) Object.Destroy(canvasObject);
            if (eventSystemObject != null) Object.Destroy(eventSystemObject);
        }
    }

    [UnityTest]
    public IEnumerator CardInspectScrollRectDoesNotStealHandDragInput()
    {
        GameObject canvasObject = null!;
        GameObject eventSystemObject = null!;
        GameObject cardObject = null!;
        try
        {
            canvasObject = new GameObject(
                "RuntimeBattlePanelDragScrollCoexistenceCanvas",
                typeof(RectTransform),
                typeof(Canvas));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var root = canvasObject.GetComponent<RectTransform>();
            root.sizeDelta = new Vector2(1280f, 720f);

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                eventSystemObject = new GameObject(
                    "RuntimeBattlePanelDragScrollCoexistenceEventSystem",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule));
                eventSystem = eventSystemObject.GetComponent<EventSystem>();
            }

            var view = RuntimeBattlePanelView.Build(root);
            view.CardInspectRoot.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(view.ContentRoot);
            yield return null;

            var inspectScroll = view.CardInspectRoot.GetComponentInChildren<ScrollRect>(true);
            Assert.That(inspectScroll, Is.Not.Null);
            Assert.That(inspectScroll!.enabled, Is.True);
            var readerSurface = view.CardInspectRoot.Find("CardInspectSurface") as RectTransform;
            Assert.That(readerSurface, Is.Not.Null,
                "The card reader must use the bounded expanded presentation surface.");
            Assert.That(readerSurface!.anchorMax.x, Is.GreaterThan(1f),
                "The reader surface must expand beyond the original narrow rail.");
            var viewportImage = inspectScroll.viewport!.GetComponent<Image>();
            Assert.That(viewportImage, Is.Not.Null);
            Assert.That(viewportImage!.raycastTarget, Is.True,
                "The full visible detail viewport must own scroll input across its width.");
            Assert.That(RectanglesOverlap(view.CardInspectRoot, view.OwnAmbushRoot), Is.False,
                "The original reader interaction boundary must stay inside the side rail and away from the ambush lane.");
            Assert.That(RectanglesOverlap(view.CardInspectRoot, view.OwnHandRoot), Is.False,
                "The original reader interaction boundary must not cover the hand's physical drag start area.");

            cardObject = new GameObject(
                "DragScrollCoexistenceCard",
                typeof(RectTransform),
                typeof(Image));
            cardObject.transform.SetParent(view.OwnHandRoot, false);
            var cardRect = cardObject.GetComponent<RectTransform>();
            cardRect.sizeDelta = new Vector2(86f, 110f);
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = Vector2.zero;
            cardObject.GetComponent<Image>().raycastTarget = true;
            var drag = cardObject.AddComponent<RuntimeBattleCardDrag>();
            drag.Configure(
                new[]
                {
                    new RuntimeLegalAction
                    {
                        ContractVersion = ContractVersionGuard.ExpectedVersion,
                        SnapshotRevision = 0,
                        ActionId = "scroll_coexistence_drag",
                        Type = "SET_AMBUSH",
                        Actor = 0,
                        SourceId = 1L,
                        Payload = new Dictionary<string, object?>(),
                    },
                },
                canvas,
                _ => { });
            Canvas.ForceUpdateCanvases();
            yield return null;

            var pointerPosition = RectTransformUtility.WorldToScreenPoint(
                null,
                cardRect.TransformPoint(cardRect.rect.center));
            var pointer = new PointerEventData(eventSystem)
            {
                position = pointerPosition,
                pressPosition = pointerPosition,
                button = PointerEventData.InputButton.Left,
            };
            var raycasts = new List<RaycastResult>();
            eventSystem.RaycastAll(pointer, raycasts);
            Assert.That(raycasts, Is.Not.Empty,
                "A card at the hand center must be reachable by the EventSystem.");
            Assert.That(raycasts, Has.Some.Property("gameObject").SameAs(cardObject));
            foreach (var hit in raycasts)
            {
                Assert.That(hit.gameObject.transform.IsChildOf(view.CardInspectRoot), Is.False,
                    "The detail ScrollRect must not own a hand-card pointer hit.");
            }

            pointer.pointerDrag = cardObject;
            Assert.That(ExecuteEvents.Execute(
                cardObject,
                pointer,
                ExecuteEvents.beginDragHandler), Is.True);
            Assert.That(drag.IsDragging, Is.True,
                "The hand card must remain the drag source while the reader is open.");
            Assert.That(view.OwnHandScroll!.enabled, Is.False,
                "A hand drag must temporarily release the horizontal ScrollRect so it cannot steal the gesture.");
            ExecuteEvents.Execute(cardObject, pointer, ExecuteEvents.endDragHandler);
            Assert.That(drag.IsDragging, Is.False);
            Assert.That(drag.IsRetired, Is.False);
            Assert.That(view.OwnHandScroll.enabled, Is.True,
                "Cancelling an invalid hand drag must restore browsing immediately.");
        }
        finally
        {
            if (cardObject != null) Object.Destroy(cardObject);
            if (canvasObject != null) Object.Destroy(canvasObject);
            if (eventSystemObject != null) Object.Destroy(eventSystemObject);
        }
    }

    [UnityTest]
    public IEnumerator LastActionButtonRemainsRaycastableAndClickableAtClampedScrollEnd()
    {
        GameObject canvasObject = null!;
        GameObject eventSystemObject = null!;
        try
        {
            canvasObject = new GameObject(
                "RuntimeBattleActionRailPointerCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var root = canvasObject.GetComponent<RectTransform>();
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                eventSystemObject = new GameObject(
                    "RuntimeBattleActionRailPointerEventSystem",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule));
                eventSystem = eventSystemObject.GetComponent<EventSystem>();
            }

            var view = RuntimeBattlePanelView.Build(root);
            var clicked = false;
            Button last = null!;
            for (var index = 0; index < 5; index++)
            {
                var rect = RuntimeBattlePanelView.CreateRect("ActionPointer_" + index, view.ActionsRoot);
                var image = rect.gameObject.AddComponent<Image>();
                image.raycastTarget = true;
                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                var label = RuntimeBattlePanelView.CreateText(rect, "Label", 16, Color.white);
                label.alignment = TextAnchor.MiddleCenter;
                label.text = index == 4 ? "END TURN" : "ACTION " + index;
                var element = rect.gameObject.AddComponent<LayoutElement>();
                element.minHeight = 44f;
                element.preferredHeight = 44f;
                button.onClick.AddListener(() => clicked = true);
                if (index == 4) last = button;
            }

            foreach (var size in new[] { new Vector2(1280f, 720f), new Vector2(1440f, 900f) })
            {
                root.sizeDelta = size;
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(view.ActionsScrollRoot);
                LayoutRebuilder.ForceRebuildLayoutImmediate(view.ActionsRoot);
                view.ActionsScrollRoot.GetComponent<ScrollRect>()!.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
                yield return null;

                var lastRect = last.GetComponent<RectTransform>();
                var corners = new Vector3[4];
                lastRect!.GetWorldCorners(corners);
                var pointer = new PointerEventData(eventSystem!)
                {
                    position = (corners[0] + corners[2]) * 0.5f,
                    pressPosition = (corners[0] + corners[2]) * 0.5f,
                    button = PointerEventData.InputButton.Left,
                };
                var raycasts = new List<RaycastResult>();
                eventSystem!.RaycastAll(pointer, raycasts);
                Assert.That(raycasts, Has.Some.Property("gameObject").SameAs(last.gameObject),
                    "END TURN must remain reachable at " + size.x + "x" + size.y + ".");
                Assert.That(ExecuteEvents.Execute(last.gameObject, pointer, ExecuteEvents.pointerClickHandler), Is.True);
                Assert.That(clicked, Is.True);
                clicked = false;
            }
        }
        finally
        {
            if (canvasObject != null) Object.Destroy(canvasObject);
            if (eventSystemObject != null) Object.Destroy(eventSystemObject);
        }
    }

    private static bool RectanglesOverlap(RectTransform left, RectTransform right)
    {
        var leftCorners = new Vector3[4];
        var rightCorners = new Vector3[4];
        left.GetWorldCorners(leftCorners);
        right.GetWorldCorners(rightCorners);
        return leftCorners[0].x < rightCorners[2].x &&
            leftCorners[2].x > rightCorners[0].x &&
            leftCorners[0].y < rightCorners[2].y &&
            leftCorners[2].y > rightCorners[0].y;
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
}
}
#endif
