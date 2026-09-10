#nullable enable annotations

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DominionWars.Unity.UI
{

/// <summary>
/// Presentation-only arrow for a targeted ATTACK drag.
///
/// The component never chooses or submits an action. RuntimeBattleCardDrag
/// supplies the pointer position and RuntimeBattleDropZone supplies the
/// advertised-action legality result; the engine remains the only authority
/// for what can actually be submitted.
/// </summary>
[DisallowMultipleComponent]
public sealed class RuntimeAttackDragArrow : MonoBehaviour
{
    private static readonly Color LegalColor = new Color(0.28f, 0.96f, 0.90f, 0.96f);
    private static readonly Color InvalidColor = new Color(1f, 0.42f, 0.30f, 0.94f);
    private static Sprite? _whiteSprite;

    private const float ShaftThickness = 5f;
    private const float HeadLength = 18f;
    private const float HeadThickness = 5f;

    private RuntimeBattleCardDrag? _drag;
    private Canvas? _canvas;
    private RectTransform? _visualRoot;
    private Image? _shaft;
    private Image? _headLeft;
    private Image? _headRight;
    private CanvasGroup? _visualGroup;
    private Vector2 _startScreenPosition;
    private Vector2 _endScreenPosition;
    private bool _visible;
    private bool _pointingAtLegalTarget;

    public bool IsVisible => _visible;
    public bool IsPointingAtLegalTarget => _pointingAtLegalTarget;
    public Vector2 StartScreenPosition => _startScreenPosition;
    public Vector2 EndScreenPosition => _endScreenPosition;
    public Color CurrentColor => _pointingAtLegalTarget ? LegalColor : InvalidColor;

    internal void Bind(RuntimeBattleCardDrag drag)
    {
        _drag = drag;
    }

    internal void Configure(Canvas? canvas)
    {
        _canvas = canvas;
        End();
    }

    internal void Begin(PointerEventData? eventData)
    {
        if (_drag == null || !_drag.HasTargetedAttackAction())
        {
            End();
            return;
        }

        _startScreenPosition = SourceScreenPosition(eventData);
        _endScreenPosition = eventData == null
            ? _startScreenPosition
            : eventData.position;
        _pointingAtLegalTarget = false;
        _visible = true;
        EnsureVisualRoot();
        RefreshVisual();
    }

    internal void UpdatePointer(PointerEventData? eventData, bool pointingAtLegalTarget)
    {
        if (!_visible) return;
        if (eventData != null) _endScreenPosition = eventData.position;
        _pointingAtLegalTarget = pointingAtLegalTarget;
        RefreshVisual();
    }

    internal void End()
    {
        _visible = false;
        _pointingAtLegalTarget = false;
        SetVisualEnabled(false);
    }

    private void Awake()
    {
        // RuntimeBattleCardDrag binds the component after AddComponent. This
        // is intentionally empty so an inspector-added component is also safe.
    }

    private void OnDestroy()
    {
        DestroyVisualRoot();
    }

    private Vector2 SourceScreenPosition(PointerEventData? eventData)
    {
        var sourceRect = transform as RectTransform;
        var worldPosition = sourceRect == null
            ? transform.position
            : sourceRect.TransformPoint(sourceRect.rect.center);
        var eventCamera = eventData == null
            ? (_canvas == null ? null : _canvas.worldCamera)
            : eventData.pressEventCamera ?? eventData.enterEventCamera ??
              (_canvas == null ? null : _canvas.worldCamera);
        return RectTransformUtility.WorldToScreenPoint(eventCamera, worldPosition);
    }

    private void EnsureVisualRoot()
    {
        if (_canvas == null) return;
        if (_visualRoot != null && _visualRoot.gameObject != null &&
            _visualRoot.GetComponentInParent<Canvas>() == _canvas)
            return;

        DestroyVisualRoot();
        var rootObject = new GameObject(
            name + "_AttackDragArrow",
            typeof(RectTransform),
            typeof(CanvasGroup));
        _visualRoot = rootObject.GetComponent<RectTransform>();
        _visualRoot.SetParent(_canvas.transform, false);
        _visualRoot.anchorMin = Vector2.zero;
        _visualRoot.anchorMax = Vector2.one;
        _visualRoot.offsetMin = Vector2.zero;
        _visualRoot.offsetMax = Vector2.zero;
        _visualRoot.pivot = new Vector2(0.5f, 0.5f);
        _visualRoot.SetAsLastSibling();

        _visualGroup = rootObject.GetComponent<CanvasGroup>();
        _visualGroup.interactable = false;
        _visualGroup.blocksRaycasts = false;
        _visualGroup.ignoreParentGroups = true;

        _shaft = CreateStroke(_visualRoot, "Shaft");
        _headLeft = CreateStroke(_visualRoot, "HeadLeft");
        _headRight = CreateStroke(_visualRoot, "HeadRight");
    }

    private static Image CreateStroke(RectTransform parent, string strokeName)
    {
        var strokeObject = new GameObject(strokeName, typeof(RectTransform), typeof(Image));
        strokeObject.transform.SetParent(parent, false);
        var image = strokeObject.GetComponent<Image>();
        image.sprite = WhiteSprite();
        image.raycastTarget = false;
        var rect = strokeObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        return image;
    }

    private static Sprite WhiteSprite()
    {
        // Texture2D.whiteTexture is built into Unity, so the presentation
        // does not add an asset or a project dependency for this cue.
        if (_whiteSprite != null) return _whiteSprite;
        _whiteSprite = Sprite.Create(
            Texture2D.whiteTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        _whiteSprite.hideFlags = HideFlags.HideAndDontSave;
        return _whiteSprite;
    }

    private void RefreshVisual()
    {
        if (!_visible) return;
        EnsureVisualRoot();
        if (_visualRoot == null || _shaft == null || _headLeft == null || _headRight == null)
            return;

        var start = CanvasLocalPoint(_startScreenPosition);
        var end = CanvasLocalPoint(_endScreenPosition);
        var direction = end - start;
        var length = direction.magnitude;
        if (length <= Mathf.Epsilon)
        {
            SetVisualEnabled(false);
            return;
        }

        var unit = direction / length;
        var perpendicular = new Vector2(-unit.y, unit.x);
        var headTip = end - unit * HeadLength;
        var color = CurrentColor;
        SetSegment(_shaft, start, end, ShaftThickness, color);
        SetSegment(_headLeft, end, headTip + perpendicular * (HeadLength * 0.52f), HeadThickness, color);
        SetSegment(_headRight, end, headTip - perpendicular * (HeadLength * 0.52f), HeadThickness, color);
        SetVisualEnabled(true);
    }

    private Vector2 CanvasLocalPoint(Vector2 screenPosition)
    {
        if (_canvas == null) return screenPosition;
        var canvasRect = _canvas.transform as RectTransform;
        if (canvasRect != null)
        {
            var eventCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : _canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPosition,
                eventCamera,
                out var localPoint))
                return localPoint;
        }

        var scaleFactor = _canvas.scaleFactor > Mathf.Epsilon ? _canvas.scaleFactor : 1f;
        return screenPosition / scaleFactor;
    }

    private static void SetSegment(
        Image image,
        Vector2 start,
        Vector2 end,
        float thickness,
        Color color)
    {
        var rect = image.rectTransform;
        var delta = end - start;
        var length = delta.magnitude;
        image.color = color;
        image.raycastTarget = false;
        image.gameObject.SetActive(length > Mathf.Epsilon);
        if (length <= Mathf.Epsilon) return;
        rect.anchoredPosition = start;
        rect.sizeDelta = new Vector2(length, thickness);
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    private void SetVisualEnabled(bool enabled)
    {
        if (_visualGroup != null) _visualGroup.alpha = enabled ? 1f : 0f;
        if (_shaft != null) _shaft.gameObject.SetActive(enabled);
        if (_headLeft != null) _headLeft.gameObject.SetActive(enabled);
        if (_headRight != null) _headRight.gameObject.SetActive(enabled);
    }

    private void DestroyVisualRoot()
    {
        if (_visualRoot == null) return;
        var rootObject = _visualRoot.gameObject;
        _visualRoot = null;
        _shaft = null;
        _headLeft = null;
        _headRight = null;
        _visualGroup = null;
        if (Application.isPlaying) Destroy(rootObject);
        else DestroyImmediate(rootObject);
    }
}
}
