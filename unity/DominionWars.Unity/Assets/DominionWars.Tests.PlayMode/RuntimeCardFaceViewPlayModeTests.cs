#if UNITY_INCLUDE_TESTS
#nullable enable annotations

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DominionWars.Unity.UI;

namespace DominionWars.Unity.PlayMode
{

[TestFixture]
public sealed class RuntimeCardFaceViewPlayModeTests
{
    [UnityTest]
    public IEnumerator CompactFaceKeepsPositiveCanvasLayoutAndSingleRaycastSurface()
    {
        GameObject? canvasObject = null;
        try
        {
            canvasObject = new GameObject(
                "CardFacePlayModeCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas!.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler!.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            var parentObject = new GameObject("CardFacePlayModeParent", typeof(RectTransform));
            parentObject.transform.SetParent(canvasObject.transform, false);
            var parent = parentObject.GetComponent<RectTransform>();
            parent!.sizeDelta = new Vector2(1280f, 720f);

            var face = RuntimeCardFaceView.Build(
                parent,
                "CompactPlayModeFace",
                RuntimeCardFaceMode.Compact);
            face.SetInteractionState(true, false, true);

            yield return null;
            Canvas.ForceUpdateCanvases();

            var layout = face.GetComponent<UnityEngine.UI.LayoutElement>();
            Assert.That(layout, Is.Not.Null);
            Assert.That(layout!.preferredWidth, Is.EqualTo(RuntimeCardFaceView.CompactWidth));
            Assert.That(layout.preferredHeight, Is.EqualTo(RuntimeCardFaceView.CompactHeight));
            Assert.That(face.ArtPanel.anchorMax.x - face.ArtPanel.anchorMin.x, Is.GreaterThan(0f));
            Assert.That(face.RulesText.gameObject.activeInHierarchy, Is.True);
            Assert.That(face.ArtFallbackText.gameObject.activeInHierarchy, Is.False,
                "Placeholder diagnostics are hidden on the normal face.");
            face.SetDiagnosticsVisible(true);
            Assert.That(face.ArtFallbackText.gameObject.activeInHierarchy, Is.True);
            Assert.That(face.InteractionMarker.gameObject.activeInHierarchy, Is.True);

            var rootImage = face.GetComponent<UnityEngine.UI.Image>();
            Assert.That(rootImage, Is.Not.Null);
            Assert.That(rootImage!.raycastTarget, Is.True);
            var graphics = face.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            foreach (var graphic in graphics)
            {
                if (graphic.gameObject == face.gameObject) continue;
                Assert.That(graphic.raycastTarget, Is.False, graphic.name + " must not intercept the card root");
            }
        }
        finally
        {
            if (canvasObject != null) Object.Destroy(canvasObject);
        }
    }
}
}
#endif
