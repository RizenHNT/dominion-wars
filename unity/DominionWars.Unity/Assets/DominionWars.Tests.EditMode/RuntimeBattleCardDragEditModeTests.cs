#nullable enable annotations

using System.Collections.Generic;
using DominionWars.Adapters;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeBattleCardDragEditModeTests
{
    [Test]
    public void DragComponentOwnsOneCanvasGroupAcrossLifecycleAndRepeatedConfigure()
    {
        GameObject sourceObject = null!;
        try
        {
            sourceObject = new GameObject("LifecycleSource", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var drag = sourceObject.AddComponent<RuntimeBattleCardDrag>();
            var legal = new RuntimeLegalAction
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                SnapshotRevision = 0,
                ActionId = "play_lifecycle",
                Type = "PLAY_CARD",
                Actor = 0,
                SourceId = 1L,
                TargetId = "shared_castle",
                Payload = new Dictionary<string, object?>(),
            };

            Assert.That(sourceObject.GetComponents<CanvasGroup>(), Has.Length.EqualTo(1));

            drag.Configure(new[] { legal }, null!, _ => { });
            drag.Configure(new[] { legal }, null!, _ => { });
            var canvasGroup = sourceObject.GetComponent<CanvasGroup>();
            Assert.That(sourceObject.GetComponents<CanvasGroup>(), Has.Length.EqualTo(1));
            Assert.That(canvasGroup, Is.Not.Null);
            Assert.That(canvasGroup!.blocksRaycasts, Is.True);

            var pointer = new PointerEventData(null) { position = Vector2.one };
            drag.OnBeginDrag(pointer);
            Assert.That(drag.IsDragging, Is.True);
            Assert.That(canvasGroup.blocksRaycasts, Is.False);

            drag.OnEndDrag(pointer);
            Assert.That(drag.IsDragging, Is.False);
            Assert.That(canvasGroup.blocksRaycasts, Is.True);
            Assert.That(sourceObject.GetComponents<CanvasGroup>(), Has.Length.EqualTo(1));
        }
        finally
        {
            if (sourceObject != null) Object.DestroyImmediate(sourceObject);
        }
    }

    [Test]
    public void DragSourceSubmitsOnlyAnAdvertisedTargetAndRetainsClickFallback()
    {
        GameObject root = null!;
        GameObject canvasObject = null!;
        RuntimeLegalAction submitted = null!;
        try
        {
            canvasObject = new GameObject("DragCanvas", typeof(Canvas));
            var canvas = canvasObject.GetComponent<Canvas>();
            root = new GameObject("DragCard", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            root.transform.SetParent(canvasObject.transform, false);
            var legal = new RuntimeLegalAction
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                SnapshotRevision = 0,
                ActionId = "play_7_castle",
                Type = "PLAY_CARD",
                Actor = 0,
                SourceId = 7L,
                TargetId = "shared_castle",
                CardId = "visible_card",
                Payload = new Dictionary<string, object?>(),
            };
            var drag = root.AddComponent<RuntimeBattleCardDrag>();
            drag.Configure(new[] { legal }, canvas, action => submitted = action);

            Assert.That(drag.HasInteractableAction(), Is.True);
            Assert.That(drag.CanAccept("wrong_target"), Is.False);
            Assert.That(drag.CanAccept("shared_castle"), Is.True);
            Assert.That(drag.TrySubmitForTarget("wrong_target"), Is.False);
            Assert.That(submitted, Is.Null);
            Assert.That(drag.TryClickFallback(), Is.True);
            Assert.That(submitted, Is.SameAs(legal));
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            if (canvasObject != null) Object.DestroyImmediate(canvasObject);
        }
    }

    [Test]
    public void DropZoneHighlightsLegalTargetAndRejectsIllegalDrop()
    {
        GameObject sourceObject = null!;
        GameObject targetObject = null!;
        RuntimeLegalAction submitted = null!;
        try
        {
            var legal = new RuntimeLegalAction
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                SnapshotRevision = 0,
                ActionId = "attack_3_9",
                Type = "ATTACK",
                Actor = 0,
                SourceId = 3L,
                TargetId = 9L,
                Payload = new Dictionary<string, object?>(),
            };
            sourceObject = new GameObject("Source", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            targetObject = new GameObject("Target", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var drag = sourceObject.AddComponent<RuntimeBattleCardDrag>();
            drag.Configure(new[] { legal }, null!, action => submitted = action);
            var zone = targetObject.AddComponent<RuntimeBattleDropZone>();
            zone.Configure(9L);

            Assert.That(drag.CanAccept(9L), Is.True);
            Assert.That(drag.CanAccept(10L), Is.False);
            var pointer = new PointerEventData(null) { pointerDrag = sourceObject };

            drag.OnBeginDrag(pointer);
            zone.OnPointerEnter(pointer);
            Assert.That(zone.IsHighlighted, Is.True);
            zone.OnPointerExit(pointer);
            Assert.That(zone.IsHighlighted, Is.True);

            pointer.pointerDrag = sourceObject;
            zone.OnDrop(pointer);
            Assert.That(submitted, Is.SameAs(legal));

            var invalidTargetObject = new GameObject("InvalidTarget", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            try
            {
                var invalidZone = invalidTargetObject.AddComponent<RuntimeBattleDropZone>();
                invalidZone.Configure(10L);
                submitted = null!;
                invalidZone.OnDrop(pointer);
                Assert.That(submitted, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(invalidTargetObject);
            }
        }
        finally
        {
            if (sourceObject != null) Object.DestroyImmediate(sourceObject);
            if (targetObject != null) Object.DestroyImmediate(targetObject);
        }
    }

    [Test]
    public void DragRaisesScalesAndFollowsCanvasUnitsWhileProtectingButton()
    {
        GameObject canvasObject = null!;
        GameObject cardObject = null!;
        GameObject laterSibling = null!;
        try
        {
            canvasObject = new GameObject("ScaledDragCanvas", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.scaleFactor = 2f;

            cardObject = new GameObject(
                "ScaledDragCard",
                typeof(RectTransform),
                typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.Button));
            cardObject.transform.SetParent(canvasObject.transform, false);
            var cardRect = cardObject.GetComponent<RectTransform>();
            cardRect.localPosition = new Vector3(50f, 50f, 0f);
            laterSibling = new GameObject("LaterSibling", typeof(RectTransform));
            laterSibling.transform.SetParent(canvasObject.transform, false);

            var button = cardObject.GetComponent<UnityEngine.UI.Button>();
            button.interactable = true;
            var originalPosition = cardRect.localPosition;
            var originalScale = cardObject.transform.localScale;
            var drag = cardObject.AddComponent<RuntimeBattleCardDrag>();
            drag.Configure(new[] { LegalAction("scaled_drag", "PLAY_CARD", 3L, "castle") }, canvas, _ => { });

            var pointer = new PointerEventData(null)
            {
                pointerDrag = cardObject,
                position = new Vector2(100f, 100f),
            };
            drag.OnBeginDrag(pointer);

            Assert.That(drag.IsDragging, Is.True);
            Assert.That(cardObject.transform.parent, Is.SameAs(canvasObject.transform));
            Assert.That(cardObject.transform.GetSiblingIndex(), Is.EqualTo(canvasObject.transform.childCount - 1));
            Assert.That(cardObject.transform.localScale.x, Is.GreaterThan(originalScale.x));
            Assert.That(cardRect.localPosition.y, Is.EqualTo(74f).Within(0.01f));
            Assert.That(button.interactable, Is.False);
            Assert.That(cardObject.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);

            pointer.position = new Vector2(200f, 200f);
            drag.OnDrag(pointer);
            Assert.That(cardRect.localPosition.x, Is.EqualTo(100f).Within(0.01f));
            Assert.That(cardRect.localPosition.y, Is.EqualTo(124f).Within(0.01f));

            drag.OnEndDrag(pointer);
            Assert.That(drag.IsDragging, Is.False);
            Assert.That(cardObject.transform.localPosition, Is.EqualTo(originalPosition));
            Assert.That(cardObject.transform.GetSiblingIndex(), Is.Zero);
            Assert.That(cardObject.transform.localScale, Is.EqualTo(originalScale));
            Assert.That(button.interactable, Is.True);
            Assert.That(cardObject.GetComponent<CanvasGroup>().blocksRaycasts, Is.True);
        }
        finally
        {
            if (cardObject != null) Object.DestroyImmediate(cardObject);
            if (laterSibling != null) Object.DestroyImmediate(laterSibling);
            if (canvasObject != null) Object.DestroyImmediate(canvasObject);
        }
    }

    [Test]
    public void DropZonesHighlightOnlyDuringActiveDragAndKeepAdvertisedTargetsVisible()
    {
        GameObject sourceObject = null!;
        GameObject validObject = null!;
        GameObject invalidObject = null!;
        try
        {
            sourceObject = new GameObject("HighlightSource", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            validObject = new GameObject("ValidTarget", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            invalidObject = new GameObject("InvalidTarget", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var drag = sourceObject.AddComponent<RuntimeBattleCardDrag>();
            drag.Configure(new[] { LegalAction("highlight_attack", "ATTACK", 3L, 9L) }, null!, _ => { });
            var valid = validObject.AddComponent<RuntimeBattleDropZone>();
            valid.Configure(9L);
            var invalid = invalidObject.AddComponent<RuntimeBattleDropZone>();
            invalid.Configure(10L);
            var pointer = new PointerEventData(null) { pointerDrag = sourceObject, position = Vector2.one };

            valid.OnPointerEnter(pointer);
            Assert.That(valid.IsHighlighted, Is.False);
            drag.OnBeginDrag(pointer);
            Assert.That(valid.IsHighlighted, Is.True);
            Assert.That(invalid.IsHighlighted, Is.False);

            valid.OnPointerExit(pointer);
            Assert.That(valid.IsHighlighted, Is.True);
            invalid.OnPointerEnter(pointer);
            Assert.That(invalid.IsHighlighted, Is.False);

            drag.OnEndDrag(pointer);
            Assert.That(valid.IsHighlighted, Is.False);
            Assert.That(invalid.IsHighlighted, Is.False);
        }
        finally
        {
            if (sourceObject != null) Object.DestroyImmediate(sourceObject);
            if (validObject != null) Object.DestroyImmediate(validObject);
            if (invalidObject != null) Object.DestroyImmediate(invalidObject);
        }
    }

    [Test]
    public void EndDragRaycastFallbackSubmitsMatchingZoneWhenOnDropWasNotDispatched()
    {
        GameObject eventSystemObject = null!;
        GameObject canvasObject = null!;
        GameObject handObject = null!;
        GameObject sourceObject = null!;
        GameObject targetObject = null!;
        RuntimeLegalAction submitted = null!;
        try
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                eventSystemObject = new GameObject("ReleaseFallbackEventSystem", typeof(EventSystem));
                eventSystem = eventSystemObject.GetComponent<EventSystem>();
            }

            canvasObject = new GameObject(
                "ReleaseFallbackCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(800f, 600f);

            handObject = new GameObject("ReleaseFallbackHand", typeof(RectTransform));
            handObject.transform.SetParent(canvasObject.transform, false);
            sourceObject = new GameObject(
                "ReleaseFallbackCard",
                typeof(RectTransform),
                typeof(UnityEngine.UI.Image));
            sourceObject.transform.SetParent(handObject.transform, false);
            var sourceRect = sourceObject.GetComponent<RectTransform>();
            sourceRect.sizeDelta = new Vector2(80f, 80f);
            sourceRect.anchoredPosition = new Vector2(-240f, 0f);

            targetObject = new GameObject(
                "ReleaseFallbackTarget",
                typeof(RectTransform),
                typeof(UnityEngine.UI.Image));
            targetObject.transform.SetParent(canvasObject.transform, false);
            var targetRect = targetObject.GetComponent<RectTransform>();
            targetRect.sizeDelta = new Vector2(220f, 180f);
            targetRect.anchorMin = new Vector2(0.5f, 0.5f);
            targetRect.anchorMax = new Vector2(0.5f, 0.5f);
            targetRect.anchoredPosition = Vector2.zero;
            targetObject.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            var zone = targetObject.AddComponent<RuntimeBattleDropZone>();
            zone.Configure(9L);

            var legal = LegalAction("release_fallback", "ATTACK", 3L, 9L);
            var drag = sourceObject.AddComponent<RuntimeBattleCardDrag>();
            drag.Configure(new[] { legal }, canvas, action => submitted = action);
            Canvas.ForceUpdateCanvases();
            var releasePosition = canvas.pixelRect.center;
            var pointer = new PointerEventData(eventSystem)
            {
                pointerDrag = sourceObject,
                position = releasePosition,
            };

            var targetContainsRelease = RectTransformUtility.RectangleContainsScreenPoint(
                targetRect,
                releasePosition,
                null);
            // EditMode does not guarantee a completed GraphicRaycaster depth
            // pass. This test is about the production geometry fallback, so
            // assert the release is inside the exact registered target rather
            // than treating an optional EditMode raycast result as a gate.
            Assert.That(
                targetContainsRelease,
                Is.True,
                "Release position is outside the target. position=" + releasePosition +
                " target=" + targetRect.position + " rect=" + targetRect.rect +
                " canvasEnabled=" + canvas.enabled + " raycasterEnabled=" +
                canvasObject.GetComponent<UnityEngine.UI.GraphicRaycaster>().enabled +
                " imageEnabled=" + targetObject.GetComponent<UnityEngine.UI.Image>().enabled +
                " raycastTarget=" + targetObject.GetComponent<UnityEngine.UI.Image>().raycastTarget +
                " imageCanvas=" + targetObject.GetComponent<UnityEngine.UI.Image>().canvas +
                " imageDepth=" + targetObject.GetComponent<UnityEngine.UI.Image>().depth +
                " imageCulled=" + targetObject.GetComponent<UnityEngine.UI.Image>().canvasRenderer.cull +
                " pixelRect=" + canvas.pixelRect +
                " contains=" + targetContainsRelease);

            drag.OnBeginDrag(pointer);
            Assert.That(drag.IsDragging, Is.True);
            Assert.That(zone.isActiveAndEnabled, Is.True);
            Assert.That(drag.CanAccept(zone.TargetId, zone.ActionId, zone.ActionType), Is.True);
            Assert.That(RectTransformUtility.RectangleContainsScreenPoint(
                targetRect,
                pointer.position,
                pointer.pressEventCamera ?? pointer.enterEventCamera), Is.True);
            drag.OnEndDrag(pointer);

            Assert.That(submitted, Is.SameAs(legal));
            Assert.That(drag.IsRetired, Is.True);
            Assert.That(drag.IsDragging, Is.False);
            Assert.That(sourceObject.activeSelf, Is.False);
        }
        finally
        {
            if (sourceObject != null) Object.DestroyImmediate(sourceObject);
            if (targetObject != null) Object.DestroyImmediate(targetObject);
            if (handObject != null) Object.DestroyImmediate(handObject);
            if (canvasObject != null) Object.DestroyImmediate(canvasObject);
            if (eventSystemObject != null) Object.DestroyImmediate(eventSystemObject);
        }
    }

    [Test]
    public void EndDragRaycastFallbackStillRestoresCardForAnInvalidRelease()
    {
        GameObject eventSystemObject = null!;
        GameObject canvasObject = null!;
        GameObject handObject = null!;
        GameObject sourceObject = null!;
        GameObject targetObject = null!;
        RuntimeLegalAction submitted = null!;
        try
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                eventSystemObject = new GameObject("InvalidReleaseEventSystem", typeof(EventSystem));
                eventSystem = eventSystemObject.GetComponent<EventSystem>();
            }

            canvasObject = new GameObject(
                "InvalidReleaseCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(UnityEngine.UI.GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(800f, 600f);

            handObject = new GameObject("InvalidReleaseHand", typeof(RectTransform));
            handObject.transform.SetParent(canvasObject.transform, false);
            sourceObject = new GameObject(
                "InvalidReleaseCard",
                typeof(RectTransform),
                typeof(UnityEngine.UI.Image));
            sourceObject.transform.SetParent(handObject.transform, false);
            var sourceRect = sourceObject.GetComponent<RectTransform>();
            sourceRect.sizeDelta = new Vector2(80f, 80f);
            sourceRect.anchoredPosition = new Vector2(-240f, 0f);
            var originalPosition = sourceRect.anchoredPosition;

            targetObject = new GameObject(
                "InvalidReleaseTarget",
                typeof(RectTransform),
                typeof(UnityEngine.UI.Image));
            targetObject.transform.SetParent(canvasObject.transform, false);
            var targetRect = targetObject.GetComponent<RectTransform>();
            targetRect.sizeDelta = new Vector2(220f, 180f);
            targetRect.anchorMin = new Vector2(0.5f, 0.5f);
            targetRect.anchorMax = new Vector2(0.5f, 0.5f);
            targetRect.anchoredPosition = Vector2.zero;
            targetObject.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            var invalidZone = targetObject.AddComponent<RuntimeBattleDropZone>();
            invalidZone.Configure(10L);

            var legal = LegalAction("invalid_release", "ATTACK", 3L, 9L);
            var drag = sourceObject.AddComponent<RuntimeBattleCardDrag>();
            drag.Configure(new[] { legal }, canvas, action => submitted = action);
            Canvas.ForceUpdateCanvases();
            var releasePosition = canvas.pixelRect.center;
            var pointer = new PointerEventData(eventSystem)
            {
                pointerDrag = sourceObject,
                position = releasePosition,
            };

            drag.OnBeginDrag(pointer);
            drag.OnEndDrag(pointer);

            Assert.That(submitted, Is.Null);
            Assert.That(drag.IsRetired, Is.False);
            Assert.That(drag.IsDragging, Is.False);
            Assert.That(sourceObject.activeSelf, Is.True);
            Assert.That(sourceObject.transform.parent, Is.SameAs(handObject.transform));
            Assert.That(sourceRect.anchoredPosition, Is.EqualTo(originalPosition));
        }
        finally
        {
            if (sourceObject != null) Object.DestroyImmediate(sourceObject);
            if (targetObject != null) Object.DestroyImmediate(targetObject);
            if (handObject != null) Object.DestroyImmediate(handObject);
            if (canvasObject != null) Object.DestroyImmediate(canvasObject);
            if (eventSystemObject != null) Object.DestroyImmediate(eventSystemObject);
        }
    }

    [Test]
    public void SharedSemanticSurfaceKeepsHighlightWhenOnlyOneSiblingActionMatches()
    {
        GameObject sourceObject = null!;
        GameObject targetObject = null!;
        try
        {
            sourceObject = new GameObject("SharedSurfaceSource", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            targetObject = new GameObject("SharedSurface", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var drag = sourceObject.AddComponent<RuntimeBattleCardDrag>();
            var matching = LegalAction("play_matching", "PLAY_CARD", 3L, null!);
            drag.Configure(new[] { matching }, null!, _ => { });
            var first = targetObject.AddComponent<RuntimeBattleDropZone>();
            first.Configure(null, matching.ActionId, matching.Type);
            var laterNonMatch = targetObject.AddComponent<RuntimeBattleDropZone>();
            laterNonMatch.Configure(null, "play_other", "PLAY_CARD");
            var pointer = new PointerEventData(null) { pointerDrag = sourceObject, position = Vector2.one };

            drag.OnBeginDrag(pointer);

            Assert.That(first.IsHighlighted, Is.True);
            Assert.That(laterNonMatch.IsHighlighted, Is.False);
            Assert.That(targetObject.GetComponent<UnityEngine.UI.Outline>().effectDistance.x,
                Is.EqualTo(4f));
            Assert.That(targetObject.GetComponent<UnityEngine.UI.Image>().color.a,
                Is.GreaterThanOrEqualTo(0.18f));
        }
        finally
        {
            if (sourceObject != null) Object.DestroyImmediate(sourceObject);
            if (targetObject != null) Object.DestroyImmediate(targetObject);
        }
    }

    [Test]
    public void NullTargetDropSurfaceUsesExactAdvertisedActionIdentity()
    {
        GameObject sourceObject = null!;
        GameObject firstTargetObject = null!;
        GameObject secondTargetObject = null!;
        RuntimeLegalAction submitted = null!;
        try
        {
            sourceObject = new GameObject("NullTargetSource", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            firstTargetObject = new GameObject("FirstNullTarget", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            secondTargetObject = new GameObject("SecondNullTarget", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var first = LegalAction("play_first", "PLAY_CARD", 3L, null!);
            var second = LegalAction("play_second", "PLAY_CARD", 3L, null!);
            var drag = sourceObject.AddComponent<RuntimeBattleCardDrag>();
            drag.Configure(new[] { first, second }, null!, action => submitted = action);
            var firstZone = firstTargetObject.AddComponent<RuntimeBattleDropZone>();
            firstZone.Configure(null, first.ActionId, first.Type);
            var secondZone = secondTargetObject.AddComponent<RuntimeBattleDropZone>();
            secondZone.Configure(null, second.ActionId, second.Type);
            var pointer = new PointerEventData(null) { pointerDrag = sourceObject, position = Vector2.one };

            drag.OnBeginDrag(pointer);
            Assert.That(firstZone.IsHighlighted, Is.True);
            Assert.That(secondZone.IsHighlighted, Is.True);
            secondZone.OnDrop(pointer);

            Assert.That(submitted, Is.SameAs(second));
            Assert.That(drag.IsRetired, Is.True);
            Assert.That(drag.TrySubmitForTarget(null, first.ActionId, first.Type), Is.False);
        }
        finally
        {
            if (sourceObject != null) Object.DestroyImmediate(sourceObject);
            if (firstTargetObject != null) Object.DestroyImmediate(firstTargetObject);
            if (secondTargetObject != null) Object.DestroyImmediate(secondTargetObject);
        }
    }

    [Test]
    public void SharedNonEmptyTargetRejectsAmbiguousActionWithoutIdentity()
    {
        GameObject sourceObject = null!;
        RuntimeLegalAction submitted = null!;
        try
        {
            sourceObject = new GameObject(
                "AmbiguousTargetSource",
                typeof(RectTransform),
                typeof(UnityEngine.UI.Image));
            var first = LegalAction("attack_castle", "ATTACK", 3L, "castle");
            var second = LegalAction("play_castle", "PLAY_CARD", 3L, "castle");
            var drag = sourceObject.AddComponent<RuntimeBattleCardDrag>();
            drag.Configure(new[] { first, second }, null!, action => submitted = action);

            // A semantic target alone must not silently choose whichever
            // matching action happens to occur first in the source list.
            Assert.That(drag.CanAccept("castle"), Is.False);
            Assert.That(drag.TrySubmitForTarget("castle"), Is.False);
            Assert.That(submitted, Is.Null);
            Assert.That(drag.IsRetired, Is.False);

            // The action rail/drop surface can still select one complete,
            // engine-advertised identity explicitly.
            Assert.That(drag.CanAccept("castle", first.ActionId, first.Type), Is.True);
            Assert.That(drag.TrySubmitForTarget("castle", first.ActionId, first.Type), Is.True);
            Assert.That(submitted, Is.SameAs(first));
            Assert.That(drag.IsRetired, Is.True);
        }
        finally
        {
            if (sourceObject != null) Object.DestroyImmediate(sourceObject);
        }
    }

    [Test]
    public void ReleaseFallbackRejectsOtherCanvasAndHiddenPanelZones()
    {
        GameObject canvasObject = null!;
        GameObject otherCanvasObject = null!;
        GameObject hiddenParent = null!;
        GameObject sourceObject = null!;
        GameObject otherTargetObject = null!;
        GameObject hiddenTargetObject = null!;
        RuntimeLegalAction submitted = null!;
        try
        {
            canvasObject = new GameObject("ReleaseCanvas", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(800f, 600f);

            otherCanvasObject = new GameObject(
                "OtherReleaseCanvas",
                typeof(RectTransform),
                typeof(Canvas));
            var otherCanvas = otherCanvasObject.GetComponent<Canvas>();
            otherCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            otherCanvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(800f, 600f);

            sourceObject = new GameObject(
                "ReleaseSource",
                typeof(RectTransform),
                typeof(UnityEngine.UI.Image));
            sourceObject.transform.SetParent(canvasObject.transform, false);
            var legal = LegalAction("release_castle", "ATTACK", 3L, "castle");
            var drag = sourceObject.AddComponent<RuntimeBattleCardDrag>();
            drag.Configure(new[] { legal }, canvas, action => submitted = action);

            otherTargetObject = new GameObject(
                "OtherCanvasTarget",
                typeof(RectTransform),
                typeof(UnityEngine.UI.Image));
            otherTargetObject.transform.SetParent(otherCanvasObject.transform, false);
            ConfigureCenteredSurface(otherTargetObject);
            var otherZone = otherTargetObject.AddComponent<RuntimeBattleDropZone>();
            otherZone.Configure("castle");

            hiddenParent = new GameObject(
                "HiddenPanel",
                typeof(RectTransform),
                typeof(CanvasGroup));
            hiddenParent.transform.SetParent(canvasObject.transform, false);
            var hiddenGroup = hiddenParent.GetComponent<CanvasGroup>();
            hiddenGroup.alpha = 0f;
            hiddenTargetObject = new GameObject(
                "HiddenTarget",
                typeof(RectTransform),
                typeof(UnityEngine.UI.Image));
            hiddenTargetObject.transform.SetParent(hiddenParent.transform, false);
            ConfigureCenteredSurface(hiddenTargetObject);
            var hiddenZone = hiddenTargetObject.AddComponent<RuntimeBattleDropZone>();
            hiddenZone.Configure("castle");

            Canvas.ForceUpdateCanvases();
            var pointer = new PointerEventData(null)
            {
                pointerDrag = sourceObject,
                position = canvas.pixelRect.center,
            };
            drag.OnBeginDrag(pointer);
            drag.OnEndDrag(pointer);

            Assert.That(submitted, Is.Null);
            Assert.That(drag.IsRetired, Is.False);
            Assert.That(sourceObject.activeSelf, Is.True);
        }
        finally
        {
            if (sourceObject != null) Object.DestroyImmediate(sourceObject);
            if (hiddenTargetObject != null) Object.DestroyImmediate(hiddenTargetObject);
            if (hiddenParent != null) Object.DestroyImmediate(hiddenParent);
            if (otherTargetObject != null) Object.DestroyImmediate(otherTargetObject);
            if (otherCanvasObject != null) Object.DestroyImmediate(otherCanvasObject);
            if (canvasObject != null) Object.DestroyImmediate(canvasObject);
        }
    }

    [Test]
    public void NoInteractableActionCannotStartDrag()
    {
        GameObject sourceObject = null!;
        try
        {
            sourceObject = new GameObject("UnavailableDragSource", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var action = LegalAction("missing_target", "PLAY_CARD", 3L, null!);
            action.Payload = new Dictionary<string, object?> { ["requiresTarget"] = true };
            var drag = sourceObject.AddComponent<RuntimeBattleCardDrag>();
            drag.Configure(new[] { action }, null!, _ => { });
            var pointer = new PointerEventData(null) { pointerDrag = sourceObject, position = Vector2.one };

            Assert.That(drag.HasInteractableAction(), Is.False);
            drag.OnBeginDrag(pointer);
            Assert.That(drag.IsDragging, Is.False);
            Assert.That(sourceObject.GetComponent<CanvasGroup>().blocksRaycasts, Is.True);
        }
        finally
        {
            if (sourceObject != null) Object.DestroyImmediate(sourceObject);
        }
    }

    [Test]
    public void SuccessfulDropImmediatelyHidesGhostAndLeavesAuthoritativeRebuildVisible()
    {
        GameObject canvasObject = null!;
        GameObject handObject = null!;
        GameObject sourceObject = null!;
        GameObject targetObject = null!;
        GameObject rebuiltObject = null!;
        RuntimeLegalAction submitted = null!;
        try
        {
            canvasObject = new GameObject("SuccessCanvas", typeof(RectTransform), typeof(Canvas));
            handObject = new GameObject("AuthoritativeHand", typeof(RectTransform));
            handObject.transform.SetParent(canvasObject.transform, false);
            sourceObject = new GameObject("CloudDragGhost", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            sourceObject.transform.SetParent(handObject.transform, false);
            targetObject = new GameObject("PullTarget", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            targetObject.transform.SetParent(canvasObject.transform, false);
            var legal = LegalAction("pull_cloud", "PULL", 3L, 9L);
            var drag = sourceObject.AddComponent<RuntimeBattleCardDrag>();
            drag.Configure(new[] { legal }, canvasObject.GetComponent<Canvas>(), action =>
            {
                submitted = action;
                rebuiltObject = new GameObject("AuthoritativeSnapshotCard", typeof(RectTransform));
                rebuiltObject.transform.SetParent(handObject.transform, false);
            });
            var zone = targetObject.AddComponent<RuntimeBattleDropZone>();
            zone.Configure(9L);
            var pointer = new PointerEventData(null) { pointerDrag = sourceObject, position = new Vector2(200f, 120f) };

            drag.OnBeginDrag(pointer);
            zone.OnDrop(pointer);

            Assert.That(submitted, Is.SameAs(legal));
            Assert.That(drag.IsRetired, Is.True);
            Assert.That(drag.IsDragging, Is.False);
            Assert.That(sourceObject.activeSelf, Is.False);
            Assert.That(sourceObject.GetComponent<CanvasGroup>().alpha, Is.Zero);
            Assert.That(rebuiltObject, Is.Not.Null);
            Assert.That(rebuiltObject.activeSelf, Is.True);
            Assert.That(rebuiltObject.transform.parent, Is.SameAs(handObject.transform));
            Assert.That(handObject.transform.Find("CloudDragGhost"), Is.Null, "the accepted drag visual must not remain in the authoritative zone");
        }
        finally
        {
            if (sourceObject != null) Object.DestroyImmediate(sourceObject);
            if (targetObject != null) Object.DestroyImmediate(targetObject);
            if (rebuiltObject != null) Object.DestroyImmediate(rebuiltObject);
            if (handObject != null) Object.DestroyImmediate(handObject);
            if (canvasObject != null) Object.DestroyImmediate(canvasObject);
        }
    }

    [Test]
    public void IllegalDropRestoresOriginalParentSiblingAnchoredPositionAndScale()
    {
        GameObject canvasObject = null!;
        GameObject handObject = null!;
        GameObject sourceObject = null!;
        GameObject targetObject = null!;
        try
        {
            canvasObject = new GameObject("RollbackCanvas", typeof(RectTransform), typeof(Canvas));
            handObject = new GameObject("RollbackHand", typeof(RectTransform));
            handObject.transform.SetParent(canvasObject.transform, false);
            new GameObject("EarlierSibling", typeof(RectTransform)).transform.SetParent(handObject.transform, false);
            sourceObject = new GameObject("RollbackCard", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            sourceObject.transform.SetParent(handObject.transform, false);
            targetObject = new GameObject("IllegalTarget", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            targetObject.transform.SetParent(canvasObject.transform, false);
            var rect = sourceObject.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(31f, -17f);
            sourceObject.transform.localScale = new Vector3(0.86f, 0.91f, 1f);
            var originalPosition = rect.anchoredPosition;
            var originalScale = sourceObject.transform.localScale;
            var originalSibling = sourceObject.transform.GetSiblingIndex();
            RuntimeLegalAction submitted = null!;
            var drag = sourceObject.AddComponent<RuntimeBattleCardDrag>();
            drag.Configure(new[] { LegalAction("attack_rollback", "ATTACK", 3L, 9L) }, canvasObject.GetComponent<Canvas>(), action => submitted = action);
            var zone = targetObject.AddComponent<RuntimeBattleDropZone>();
            zone.Configure(10L);
            var pointer = new PointerEventData(null) { pointerDrag = sourceObject, position = new Vector2(420f, 260f) };

            drag.OnBeginDrag(pointer);
            zone.OnDrop(pointer);
            drag.OnEndDrag(pointer);

            Assert.That(submitted, Is.Null);
            Assert.That(drag.IsRetired, Is.False);
            Assert.That(sourceObject.activeSelf, Is.True);
            Assert.That(sourceObject.transform.parent, Is.SameAs(handObject.transform));
            Assert.That(sourceObject.transform.GetSiblingIndex(), Is.EqualTo(originalSibling));
            Assert.That(rect.anchoredPosition, Is.EqualTo(originalPosition));
            Assert.That(sourceObject.transform.localScale, Is.EqualTo(originalScale));
            Assert.That(sourceObject.GetComponent<CanvasGroup>().blocksRaycasts, Is.True);
        }
        finally
        {
            if (sourceObject != null) Object.DestroyImmediate(sourceObject);
            if (targetObject != null) Object.DestroyImmediate(targetObject);
            if (handObject != null) Object.DestroyImmediate(handObject);
            if (canvasObject != null) Object.DestroyImmediate(canvasObject);
        }
    }

    private static RuntimeLegalAction LegalAction(string actionId, string type, long sourceId, object targetId)
    {
        return new RuntimeLegalAction
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            SnapshotRevision = 0,
            ActionId = actionId,
            Type = type,
            Actor = 0,
            SourceId = sourceId,
            TargetId = targetId,
            Payload = new Dictionary<string, object?>(),
        };
    }

    private static void ConfigureCenteredSurface(GameObject targetObject)
    {
        var rect = targetObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(220f, 180f);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        targetObject.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
    }
}
}
