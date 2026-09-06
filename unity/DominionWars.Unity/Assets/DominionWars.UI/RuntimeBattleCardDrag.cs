#nullable enable annotations

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DominionWars.Adapters;

namespace DominionWars.Unity.UI
{

/// <summary>
/// Drag source for a face-up card. It carries complete advertised actions,
/// never manufactures a target, and only submits after a drop zone confirms
/// an exact wire target match. A Button remains the click fallback.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public sealed class RuntimeBattleCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private const float DragLift = 24f;
    private const float DragScaleMultiplier = 1.10f;

    private readonly List<RuntimeLegalAction> _actions = new List<RuntimeLegalAction>();
    private readonly List<UnityEngine.UI.Button> _dragButtons = new List<UnityEngine.UI.Button>();
    private readonly List<bool> _dragButtonInteractableStates = new List<bool>();
    private Action<RuntimeLegalAction>? _submit;
    private Canvas? _canvas;
    private Transform? _originalParent;
    private int _originalSiblingIndex;
    private Vector2 _originalAnchoredPosition;
    private Vector3 _originalLocalPosition;
    private Quaternion _originalLocalRotation;
    private Vector3 _originalScale = Vector3.one;
    private Vector2 _pointerOffset;
    private Vector2 _screenPointerOffset;
    private bool _useCanvasCoordinates;
    private int _suppressClickUntilFrame = -1;
    private CanvasGroup? _canvasGroup;
    private bool _dragging;
    private bool _dropAccepted;
    private bool _retired;
    private bool _diagnosticDragLogged;

    // Opt-in diagnostics for foreground input investigations. The flag is
    // false by default so ordinary editor sessions and development players do
    // not receive pointer noise; an Editor eval can enable it for one probe.
    internal static bool DiagnosticsEnabled { get; private set; }

    public static void SetDiagnosticsEnabled(bool enabled)
    {
        DiagnosticsEnabled = enabled;
    }

    internal static void DragDiagnostic(string message)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (DiagnosticsEnabled) Debug.Log("[RuntimeDragDiag] " + message);
#endif
    }

    public bool IsDragging => _dragging;
    public bool IsRetired => _retired;
    public IReadOnlyList<RuntimeLegalAction> Actions => _actions.AsReadOnly();

    private void Awake()
    {
        EnsureCanvasGroup();
    }

    public void Configure(
        IReadOnlyList<RuntimeLegalAction> actions,
        Canvas canvas,
        Action<RuntimeLegalAction> submit)
    {
        if (_dragging) CancelDragVisual();
        _actions.Clear();
        if (actions != null)
        {
            for (var index = 0; index < actions.Count; index++)
            {
                if (actions[index] != null) _actions.Add(actions[index]);
            }
        }
        _canvas = canvas;
        _submit = submit;
        var canvasGroup = EnsureCanvasGroup();
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
    }

    public bool HasInteractableAction()
    {
        if (_retired) return false;
        for (var index = 0; index < _actions.Count; index++)
        {
            if (RuntimeBattlePanelActionModel.Evaluate(_actions[index]).Interactable) return true;
        }
        return false;
    }

    public bool CanAccept(object? targetId)
    {
        return CanAccept(targetId, null, null);
    }

    /// <summary>
    /// Checks one exact drop surface against the advertised action identity.
    /// The optional action id/type constraints let a semantic surface submit
    /// a null-target action (for example PLAY_CARD) without choosing another
    /// null-target action from the same card. The one-argument overload remains
    /// the compatibility path for ordinary wire-target zones.
    /// </summary>
    public bool CanAccept(object? targetId, string? actionId, string? actionType)
    {
        if (_retired) return false;
        if (targetId == null && string.IsNullOrWhiteSpace(actionId) && string.IsNullOrWhiteSpace(actionType))
            return false;
        var matchCount = 0;
        for (var index = 0; index < _actions.Count; index++)
        {
            var action = _actions[index];
            if (!RuntimeBattlePanelActionModel.Evaluate(action).Interactable) continue;
            if (!MatchesDrop(action, targetId, actionId, actionType)) continue;
            matchCount++;
            // An explicitly identified semantic surface is unambiguous even
            // when another action on the same card shares its wire target.
            if (!string.IsNullOrWhiteSpace(actionId) || !string.IsNullOrWhiteSpace(actionType))
                return true;
        }
        // A non-empty target without an action identity is a visual target,
        // not a license to pick the first matching action. Reject ambiguity;
        // the action rail exposes each complete advertised action instead.
        return matchCount == 1;
    }

    public bool TrySubmitForTarget(object? targetId)
    {
        return TrySubmitForTarget(targetId, null, null);
    }

    /// <summary>Submits one exact advertised action at a drop surface.</summary>
    public bool TrySubmitForTarget(object? targetId, string? actionId, string? actionType)
    {
        if (_retired) return false;
        if (targetId == null && string.IsNullOrWhiteSpace(actionId) && string.IsNullOrWhiteSpace(actionType))
            return false;
        RuntimeLegalAction? matchingAction = null;
        for (var index = 0; index < _actions.Count; index++)
        {
            var action = _actions[index];
            if (!RuntimeBattlePanelActionModel.Evaluate(action).Interactable) continue;
            if (!MatchesDrop(action, targetId, actionId, actionType)) continue;
            if (matchingAction != null &&
                string.IsNullOrWhiteSpace(actionId) &&
                string.IsNullOrWhiteSpace(actionType))
                return false;
            matchingAction = action;
            if (!string.IsNullOrWhiteSpace(actionId) || !string.IsNullOrWhiteSpace(actionType))
                break;
        }
        if (matchingAction == null) return false;
        SubmitAndRetire(matchingAction);
        return true;
    }

    public bool TryClickFallback()
    {
        if (_retired || _dragging) return false;
        if (_suppressClickUntilFrame == Time.frameCount) return false;
        RuntimeLegalAction? fallback = null;
        for (var index = 0; index < _actions.Count; index++)
        {
            var action = _actions[index];
            if (!RuntimeBattlePanelActionModel.Evaluate(action).Interactable) continue;
            if (fallback != null) return false; // ambiguous source: use the action rail.
            fallback = action;
        }
        if (fallback == null) return false;
        SubmitAndRetire(fallback);
        return true;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        RuntimeBattleCardDrag.DragDiagnostic(
            "BeginDrag invoked object=" + gameObject.name +
            " pointer=" + (eventData == null ? "<null>" : eventData.position.ToString()) +
            " press=" + (eventData == null ? "<null>" : eventData.pressPosition.ToString()) +
            " pointerDrag=" + (eventData == null || eventData.pointerDrag == null
                ? "<null>"
                : eventData.pointerDrag.name) +
            " retired=" + _retired +
            " dragging=" + _dragging +
            " hasAction=" + HasInteractableAction());
        if (_retired || _dragging || !HasInteractableAction())
        {
            DragDiagnostic("BeginDrag rejected object=" + gameObject.name);
            return;
        }
        _diagnosticDragLogged = false;
        // The large hover reader must disappear before the card is lifted;
        // otherwise it can sit above a legal drop surface and steal the drop.
        GetComponent<RuntimeCardInspectInteraction>()?.Close();
        _suppressClickUntilFrame = -1;
        _dropAccepted = false;
        _originalParent = transform.parent;
        _originalSiblingIndex = transform.GetSiblingIndex();
        var rectTransform = transform as RectTransform;
        _originalAnchoredPosition = rectTransform == null ? Vector2.zero : rectTransform.anchoredPosition;
        _originalLocalPosition = transform.localPosition;
        _originalLocalRotation = transform.localRotation;
        _originalScale = transform.localScale;
        if (_canvas != null) transform.SetParent(_canvas.transform, true);

        _useCanvasCoordinates = TryGetCanvasPointerLocal(eventData, out var pointerLocal);
        if (_useCanvasCoordinates)
        {
            var currentLocal = GetCanvasLocalPosition();
            _pointerOffset = currentLocal - pointerLocal;
        }
        else
        {
            _screenPointerOffset = new Vector2(
                transform.position.x - eventData.position.x,
                transform.position.y - eventData.position.y);
        }

        _dragging = true;
        DisableButtonsForDrag();
        if (_canvasGroup != null) _canvasGroup.blocksRaycasts = false;
        transform.SetAsLastSibling();
        transform.localScale = _originalScale * DragScaleMultiplier;
        RuntimeBattleDropZone.NotifyDragStarted(this);
        RuntimeBattleCardDrag.DragDiagnostic(
            "BeginDrag accepted object=" + gameObject.name +
            " canvas=" + (_canvas == null ? "<null>" : _canvas.name) +
            " renderMode=" + (_canvas == null ? "<null>" : _canvas.renderMode.ToString()) +
            " useCanvasCoordinates=" + _useCanvasCoordinates +
            " pointerLocal=" + pointerLocal);
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging || _retired) return;

        if (!_diagnosticDragLogged)
        {
            _diagnosticDragLogged = true;
            DragDiagnostic(
                "Drag update object=" + gameObject.name +
                " pointer=" + (eventData == null ? "<null>" : eventData.position.ToString()) +
                " world=" + transform.position);
        }

        if (_useCanvasCoordinates && TryGetCanvasPointerLocal(eventData, out var pointerLocal))
        {
            var localPosition = pointerLocal + _pointerOffset + Vector2.up * DragLift;
            var currentLocal = transform.localPosition;
            transform.localPosition = new Vector3(localPosition.x, localPosition.y, currentLocal.z);
            return;
        }

        var screenPosition = eventData.position + _screenPointerOffset + Vector2.up * DragLift;
        transform.position = new Vector3(screenPosition.x, screenPosition.y, transform.position.z);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_dragging) return;

        RuntimeBattleCardDrag.DragDiagnostic(
            "EndDrag object=" + gameObject.name +
            " pointer=" + (eventData == null ? "<null>" : eventData.position.ToString()) +
            " dropAccepted=" + _dropAccepted +
            " retired=" + _retired);

        // The pointer module normally dispatches IDropHandler.OnDrop before
        // IEndDragHandler.OnEndDrag. Some real pointer paths can miss that
        // dispatch (for example when the dragged card is reparented while its
        // raycast state changes), so recover from the actual release position
        // before rolling the card back. The first pass follows EventSystem's
        // normal raycast; the exact active-zone geometry fallback covers a
        // missed UI raycast without inventing a target. The zone still
        // validates the exact advertised target/action identity. A successful
        // OnDrop already retires the source and clears _dragging, so this path
        // cannot submit twice.
        if (!_dropAccepted && !_retired)
            TrySubmitForReleasePosition(eventData);

        if (!_dragging) return;
        CancelDragVisual();
    }

    private void TrySubmitForReleasePosition(PointerEventData eventData)
    {
        if (eventData == null) return;

        // A normal player release has an active EventSystem, but edit-mode
        // fixtures and a few host hand-offs can reach EndDrag with only the
        // PointerEventData instance available. Keep the raycast pass optional;
        // the registered-zone geometry pass below is the authoritative
        // recovery path and must not be skipped merely because the global
        // EventSystem.current reference is temporarily null.
        if (EventSystem.current != null)
        {
            var raycastResults = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, raycastResults);
            RuntimeBattleCardDrag.DragDiagnostic(
                "Release raycast object=" + gameObject.name +
                " pointer=" + eventData.position +
                " results=" + raycastResults.Count);
            for (var resultIndex = 0; resultIndex < raycastResults.Count; resultIndex++)
            {
                var resultObject = raycastResults[resultIndex].gameObject;
                if (resultObject == null) continue;

                DragDiagnostic("Release hit=" + resultObject.name);

                var zones = resultObject.GetComponentsInParent<RuntimeBattleDropZone>(true);
                for (var zoneIndex = 0; zoneIndex < zones.Length; zoneIndex++)
                {
                    var zone = zones[zoneIndex];
                    if (zone == null || !zone.CanReceiveRelease(this)) continue;
                    DragDiagnostic(
                        "Release zone=" + zone.gameObject.name +
                        " target=" + RuntimeBattlePanelActionModel.FormatWireValue(zone.TargetId) +
                        " action=" + zone.ActionId +
                        " type=" + zone.ActionType);
                    if (TrySubmitForTarget(zone.TargetId, zone.ActionId, zone.ActionType)) return;
                }
            }
        }

        // A dragged card can be reparented to the top-level Canvas and have
        // its CanvasGroup raycast state changed between the pointer module's
        // release and EndDrag callback. In that frame Unity's GraphicRaycaster
        // may return no result even though the pointer is inside a visible
        // semantic target. Check only registered, active drop zones and keep
        // the same exact wire identity checks used by OnDrop.
        RuntimeBattleDropZone.TrySubmitAtScreenPosition(this, eventData);
        DragDiagnostic(
            "Release geometry fallback completed object=" + gameObject.name +
            " pointer=" + eventData.position +
            " dragging=" + _dragging +
            " retired=" + _retired);
    }

    internal void RejectDrop()
    {
        if (_retired) return;
        _dropAccepted = false;
    }

    private void SubmitAndRetire(RuntimeLegalAction action)
    {
        if (_retired) return;
        DragDiagnostic(
            "Submit action=" + action.ActionId +
            " type=" + action.Type +
            " source=" + action.SourceId +
            " target=" + RuntimeBattlePanelActionModel.FormatWireValue(action.TargetId));
        _dropAccepted = true;
        if (_dragging) RuntimeBattleDropZone.NotifyDragEnded(this);
        _dragging = false;
        RestoreButtonsAfterDrag();
        _retired = true;
        var canvasGroup = EnsureCanvasGroup();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        gameObject.SetActive(false);

        // Submission synchronously refreshes and renders the authoritative
        // snapshot. This old visual is never reused as the post-action card.
        _submit?.Invoke(action);
        if (Application.isPlaying) Destroy(gameObject);
    }

    private void CancelDragVisual()
    {
        if (!_dragging) return;
        _dragging = false;
        // Button.OnPointerUp can run after EndDrag for the same pointer event.
        // Keep the fallback suppressed for this frame so releasing a drag
        // cannot also click the card's ordinary action.
        _suppressClickUntilFrame = Time.frameCount;
        RuntimeBattleDropZone.NotifyDragEnded(this);
        if (!_dropAccepted) RestoreOriginalTransform();
        RestoreButtonsAfterDrag();
        if (_canvasGroup != null) _canvasGroup.blocksRaycasts = true;
    }

    private void RestoreOriginalTransform()
    {
        if (transform.parent != _originalParent)
            transform.SetParent(_originalParent, false);

        var parent = transform.parent;
        var maxIndex = parent == null ? 0 : parent.childCount - 1;
        transform.SetSiblingIndex(Mathf.Clamp(_originalSiblingIndex, 0, Mathf.Max(0, maxIndex)));
        var rectTransform = transform as RectTransform;
        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = _originalAnchoredPosition;
            rectTransform.localPosition = _originalLocalPosition;
        }
        else
        {
            transform.localPosition = _originalLocalPosition;
        }
        transform.localRotation = _originalLocalRotation;
        transform.localScale = _originalScale;
    }

    private void DisableButtonsForDrag()
    {
        _dragButtons.Clear();
        _dragButtonInteractableStates.Clear();
        var buttons = GetComponentsInChildren<UnityEngine.UI.Button>(true);
        for (var index = 0; index < buttons.Length; index++)
        {
            var button = buttons[index];
            if (button == null) continue;
            _dragButtons.Add(button);
            _dragButtonInteractableStates.Add(button.interactable);
            button.interactable = false;
        }
    }

    private void RestoreButtonsAfterDrag()
    {
        var count = Mathf.Min(_dragButtons.Count, _dragButtonInteractableStates.Count);
        for (var index = 0; index < count; index++)
        {
            var button = _dragButtons[index];
            if (button != null) button.interactable = _dragButtonInteractableStates[index];
        }
        _dragButtons.Clear();
        _dragButtonInteractableStates.Clear();
    }

    private bool TryGetCanvasPointerLocal(PointerEventData eventData, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;
        if (_canvas == null || eventData == null) return false;

        var canvasRect = _canvas.transform as RectTransform;
        var eventCamera = eventData.pressEventCamera ?? eventData.enterEventCamera ?? _canvas.worldCamera;
        // Overlay canvases, and editor fixtures without a usable world camera,
        // express pointer positions in screen pixels. Convert those pixels to
        // Canvas units before applying the grab offset.
        if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            if (canvasRect != null &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    eventData.position,
                    null,
                    out localPoint))
                return true;

            // Keep the fallback in the same centered Canvas coordinate space as
            // GetCanvasLocalPosition. Returning raw screen pixels here makes a
            // reparented card visibly drift away from the pointer on an
            // overlay canvas (especially in a scaled Game view).
            var pointerScale = _canvas.scaleFactor > Mathf.Epsilon ? _canvas.scaleFactor : 1f;
            var pixelSize = _canvas.pixelRect.size / pointerScale;
            localPoint = eventData.position / pointerScale - pixelSize * 0.5f;
            return true;
        }
        // A world-space Canvas without an event/world camera has no
        // meaningful screen-to-plane projection. The deterministic fallback
        // keeps drag deltas in Canvas units instead of letting Unity's
        // camera-less projection invent an origin for the first pointer.
        if (eventCamera == null && _canvas.renderMode == RenderMode.WorldSpace)
        {
            var pointerScale = _canvas.scaleFactor > Mathf.Epsilon ? _canvas.scaleFactor : 1f;
            localPoint = eventData.position / pointerScale;
            return true;
        }
        if (canvasRect != null && canvasRect.rect.width > 0f && canvasRect.rect.height > 0f &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                eventData.position,
                eventCamera,
                out localPoint))
            return true;

        // A freshly-created Canvas may not have a resolved RectTransform yet.
        // Keep the fallback in Canvas units so CanvasScaler scale is still
        // respected instead of treating screen pixels as reference pixels.
        var scaleFactor = _canvas.scaleFactor > Mathf.Epsilon ? _canvas.scaleFactor : 1f;
        localPoint = eventData.position / scaleFactor;
        return true;
    }

    internal bool IsOnCanvas(Canvas candidate)
    {
        return _canvas == null ? candidate == null : candidate == _canvas;
    }

    private Vector2 GetCanvasLocalPosition()
    {
        if (_canvas != null)
        {
            var canvasRect = _canvas.transform as RectTransform;
            if (canvasRect != null)
            {
                var local = canvasRect.InverseTransformPoint(transform.position);
                return new Vector2(local.x, local.y);
            }
        }
        return new Vector2(transform.localPosition.x, transform.localPosition.y);
    }

    private void OnDisable()
    {
        // A panel refresh can disable a card before Unity sends EndDrag. Do
        // the same deterministic cancellation so target highlights and button
        // state cannot leak into the next authoritative snapshot.
        if (_dragging) CancelDragVisual();
    }

    private static bool MatchesDrop(
        RuntimeLegalAction action,
        object? targetId,
        string? actionId,
        string? actionType)
    {
        if (action == null) return false;
        if (!string.IsNullOrWhiteSpace(actionId) &&
            !string.Equals(action.ActionId, actionId, StringComparison.Ordinal))
            return false;
        if (!string.IsNullOrWhiteSpace(actionType) &&
            !string.Equals(action.Type, actionType, StringComparison.Ordinal))
            return false;
        return RuntimeBattlePanelActionModel.WireValuesEqual(action.TargetId, targetId);
    }

    private CanvasGroup EnsureCanvasGroup()
    {
        if (_canvasGroup != null) return _canvasGroup;
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        return _canvasGroup;
    }
}

/// <summary>
/// A visible drop surface. During a drag it highlights only when the source
/// card carries an engine-advertised action for this exact target. An invalid
/// drop is ignored and the source's normal end-drag path restores its slot.
/// </summary>
public sealed class RuntimeBattleDropZone : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    private static readonly List<RuntimeBattleDropZone> ActiveZones = new List<RuntimeBattleDropZone>();
    private static RuntimeBattleCardDrag? ActiveDrag;
    private object? _targetId;
    private string? _actionId;
    private string? _actionType;
    private Outline? _outline;
    private Image? _image;
    private Color _normalColor;
    private Color _normalFillColor;
    private bool _highlighted;

    public object? TargetId => _targetId;
    public string? ActionId => _actionId;
    public string? ActionType => _actionType;
    public bool IsHighlighted => _highlighted;

    public void Configure(object? targetId)
    {
        Configure(targetId, null, null);
    }

    /// <summary>
    /// Configures the wire target and, optionally, the exact advertised action
    /// identity. A null target with an action id/type is intentional: it is a
    /// semantic surface for a null-target action, not a manufactured target.
    /// </summary>
    public void Configure(object? targetId, string? actionId, string? actionType)
    {
        // AddComponent/OnEnable ordering is not guaranteed by edit-mode test
        // fixtures. Registration here also makes a freshly configured live
        // surface available to a drag immediately in the same frame.
        ActiveZones.RemoveAll(zone => zone == null);
        if (!ActiveZones.Contains(this)) ActiveZones.Add(this);
        _targetId = targetId;
        _actionId = actionId;
        _actionType = actionType;
        _outline = GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
        _image = GetComponent<Image>();
        _normalColor = _outline.effectColor;
        _normalFillColor = _image == null ? Color.clear : _image.color;
        _outline.effectDistance = new Vector2(1f, 1f);
        _outline.useGraphicAlpha = false;
        SetHighlighted(ActiveDrag != null && IsTargetFor(ActiveDrag));
    }

    private void OnEnable()
    {
        if (!ActiveZones.Contains(this)) ActiveZones.Add(this);
    }

    private void OnDisable()
    {
        ActiveZones.Remove(this);
        SetHighlighted(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        var drag = eventData.pointerDrag == null ? null : eventData.pointerDrag.GetComponentInParent<RuntimeBattleCardDrag>();
        RuntimeBattleCardDrag.DragDiagnostic(
            "Zone enter object=" + gameObject.name +
            " pointerDrag=" + (eventData.pointerDrag == null ? "<null>" : eventData.pointerDrag.name) +
            " drag=" + (drag == null ? "<null>" : drag.gameObject.name) +
            " accepts=" + (drag != null && IsTargetFor(drag)) +
            " target=" + RuntimeBattlePanelActionModel.FormatWireValue(_targetId) +
            " action=" + _actionId);
        SetHighlighted(drag != null && IsTargetFor(drag));
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // Valid targets remain visibly available for the whole drag. Pointer
        // exit only removes a hover when this zone is not an advertised target
        // for the active source.
        SetHighlighted(ActiveDrag != null && IsTargetFor(ActiveDrag));
    }

    public void OnDrop(PointerEventData eventData)
    {
        var drag = eventData.pointerDrag == null ? null : eventData.pointerDrag.GetComponentInParent<RuntimeBattleCardDrag>();
        RuntimeBattleCardDrag.DragDiagnostic(
            "Drop invoked object=" + gameObject.name +
            " pointer=" + eventData.position +
            " pointerDrag=" + (eventData.pointerDrag == null ? "<null>" : eventData.pointerDrag.name) +
            " drag=" + (drag == null ? "<null>" : drag.gameObject.name) +
            " canReceive=" + (drag != null && CanReceiveRelease(drag)) +
            " target=" + RuntimeBattlePanelActionModel.FormatWireValue(_targetId) +
            " action=" + _actionId);
        if (drag == null || !CanReceiveRelease(drag) || !drag.IsDragging ||
            !drag.TrySubmitForTarget(_targetId, _actionId, _actionType))
            drag?.RejectDrop();
        SetHighlighted(false);
    }

    internal static void NotifyDragStarted(RuntimeBattleCardDrag drag)
    {
        ActiveDrag = drag;
        for (var index = ActiveZones.Count - 1; index >= 0; index--)
        {
            var zone = ActiveZones[index];
            if (zone == null)
            {
                ActiveZones.RemoveAt(index);
                continue;
            }
            zone.SetHighlighted(zone.IsTargetFor(drag));
        }
    }

    internal static void NotifyDragEnded(RuntimeBattleCardDrag drag)
    {
        if (ReferenceEquals(ActiveDrag, drag)) ActiveDrag = null;
        for (var index = ActiveZones.Count - 1; index >= 0; index--)
        {
            var zone = ActiveZones[index];
            if (zone == null)
            {
                ActiveZones.RemoveAt(index);
                continue;
            }
            zone.SetHighlighted(false);
        }
    }

    internal static bool TrySubmitAtScreenPosition(
        RuntimeBattleCardDrag drag,
        PointerEventData eventData)
    {
        if (drag == null || eventData == null) return false;
        var eventCamera = eventData.pressEventCamera ?? eventData.enterEventCamera;
        // The registry contains only configured, enabled drop surfaces. Do not
        // scan the whole scene here: a release must not be affected by an
        // unrelated or stale surface in another panel/scene.
        for (var index = ActiveZones.Count - 1; index >= 0; index--)
        {
            var zone = ActiveZones[index];
            if (zone == null)
            {
                ActiveZones.RemoveAt(index);
                continue;
            }
            if (!zone.CanReceiveRelease(drag)) continue;
            var rectTransform = zone.transform as RectTransform;
            if (rectTransform == null ||
                !RectTransformUtility.RectangleContainsScreenPoint(
                    rectTransform,
                    eventData.position,
                    eventCamera))
                continue;
            RuntimeBattleCardDrag.DragDiagnostic(
                "Geometry zone hit object=" + zone.gameObject.name +
                " pointer=" + eventData.position +
                " target=" + RuntimeBattlePanelActionModel.FormatWireValue(zone.TargetId) +
                " action=" + zone.ActionId);
            if (drag.TrySubmitForTarget(zone.TargetId, zone.ActionId, zone.ActionType))
                return true;
        }
        return false;
    }

    internal bool CanReceiveRelease(RuntimeBattleCardDrag drag)
    {
        if (drag == null || !isActiveAndEnabled) return false;
        // A release fallback is only allowed to cross the same Canvas the
        // drag source was configured for. Without this guard a hidden
        // overlay or a second UI surface in a different display/camera
        // could steal a release merely because its rectangle overlaps the
        // pointer position.
        if (!drag.IsOnCanvas(GetComponentInParent<Canvas>())) return false;

        // CanvasGroup is the uGUI visibility/raycast contract for an entire
        // panel. Respect every ancestor so a stale zone under a hidden panel
        // cannot receive a release merely because its RectTransform overlaps.
        var canvasGroups = GetComponentsInParent<CanvasGroup>(true);
        for (var groupIndex = 0; groupIndex < canvasGroups.Length; groupIndex++)
        {
            var group = canvasGroups[groupIndex];
            if (group == null || !group.isActiveAndEnabled ||
                group.alpha <= Mathf.Epsilon || !group.blocksRaycasts ||
                !group.interactable)
                return false;
        }
        return true;
    }

    private bool IsTargetFor(RuntimeBattleCardDrag drag)
    {
        return drag != null && drag.IsDragging && drag.CanAccept(_targetId, _actionId, _actionType);
    }

    private void SetHighlighted(bool highlighted)
    {
        _highlighted = highlighted;
        if (_outline == null) _outline = GetComponent<Outline>();
        if (_outline == null) return;
        // A semantic surface can host one drop component per advertised
        // source. Preserve the highlight while any sibling accepts this drag;
        // a later non-matching component must not erase it.
        var anyHighlighted = false;
        var siblings = GetComponents<RuntimeBattleDropZone>();
        for (var index = 0; index < siblings.Length; index++)
        {
            if (!siblings[index]._highlighted) continue;
            anyHighlighted = true;
            break;
        }

        _outline.useGraphicAlpha = false;
        _outline.effectColor = anyHighlighted ? new Color(0.34f, 0.93f, 0.95f, 1f) : _normalColor;
        _outline.effectDistance = anyHighlighted ? new Vector2(4f, 4f) : new Vector2(1f, 1f);
        if (_image == null) _image = GetComponent<Image>();
        if (_image != null)
        {
            _image.color = anyHighlighted
                ? new Color(0.18f, 0.78f, 0.82f, Mathf.Max(0.18f, _normalFillColor.a))
                : _normalFillColor;
        }
    }
}
}
