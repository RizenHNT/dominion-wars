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
            zone.OnPointerEnter(pointer);
            Assert.That(zone.IsHighlighted, Is.True);
            zone.OnPointerExit(pointer);
            Assert.That(zone.IsHighlighted, Is.False);

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
}
}
