#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using DominionWars.Adapters;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using DominionWars.Unity.UI;

namespace DominionWars.Unity.PlayMode
{

[TestFixture]
public sealed class RuntimeBattlePanelStructurePlayModeTests
{
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
    public IEnumerator OverlappedOwnHandKeepsEveryVisibleLeadingEdgeRaycastable()
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
                LayoutRebuilder.ForceRebuildLayoutImmediate(view.OwnHandRoot);
                yield return null;

                var pointerEvent = new PointerEventData(eventSystem!);
                for (var index = 0; index < view.OwnHandRoot.childCount; index++)
                {
                    var card = (RectTransform)view.OwnHandRoot.GetChild(index);
                    var cardCorners = new Vector3[4];
                    card.GetWorldCorners(cardCorners);
                    var sampleX = cardCorners[0].x;
                    if (index + 1 < view.OwnHandRoot.childCount)
                    {
                        var nextCorners = new Vector3[4];
                        view.OwnHandRoot.GetChild(index + 1).GetComponent<RectTransform>()!.GetWorldCorners(nextCorners);
                        sampleX = (sampleX + nextCorners[0].x) * 0.5f;
                    }
                    else
                    {
                        sampleX = (sampleX + cardCorners[2].x) * 0.5f;
                    }

                    pointerEvent.position = new Vector2(sampleX, (cardCorners[0].y + cardCorners[2].y) * 0.5f);
                    var raycasts = new List<RaycastResult>();
                    eventSystem.RaycastAll(pointerEvent, raycasts);
                    Assert.That(
                        raycasts,
                        Has.Some.Property("gameObject").SameAs(card.gameObject),
                        "Card " + index + " must remain reachable at " + size.x + "x" + size.y + ".");
                }
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
            Assert.That(RectanglesOverlap(view.CardInspectRoot, view.OwnAmbushRoot), Is.False,
                "The detail reader must stay inside the side rail and away from the ambush lane.");
            Assert.That(RectanglesOverlap(view.CardInspectRoot, view.OwnHandRoot), Is.False,
                "The detail reader must not cover the hand's physical drag start area.");

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
            ExecuteEvents.Execute(cardObject, pointer, ExecuteEvents.endDragHandler);
            Assert.That(drag.IsDragging, Is.False);
            Assert.That(drag.IsRetired, Is.False);
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
}
}
#endif
