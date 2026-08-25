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
    private readonly List<RuntimeLegalAction> _actions = new List<RuntimeLegalAction>();
    private Action<RuntimeLegalAction>? _submit;
    private Canvas? _canvas;
    private Transform? _originalParent;
    private int _originalSiblingIndex;
    private Vector2 _originalAnchoredPosition;
    private Vector3 _originalScale = Vector3.one;
    private CanvasGroup? _canvasGroup;
    private bool _dragging;
    private bool _dropAccepted;
    private bool _retired;

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
        for (var index = 0; index < _actions.Count; index++)
        {
            if (RuntimeBattlePanelActionModel.Evaluate(_actions[index]).Interactable) return true;
        }
        return false;
    }

    public bool CanAccept(object? targetId)
    {
        for (var index = 0; index < _actions.Count; index++)
        {
            var action = _actions[index];
            if (!RuntimeBattlePanelActionModel.Evaluate(action).Interactable) continue;
            if (RuntimeBattlePanelActionModel.WireValuesEqual(action.TargetId, targetId)) return true;
        }
        return false;
    }

    public bool TrySubmitForTarget(object? targetId)
    {
        for (var index = 0; index < _actions.Count; index++)
        {
            var action = _actions[index];
            if (!RuntimeBattlePanelActionModel.Evaluate(action).Interactable) continue;
            if (!RuntimeBattlePanelActionModel.WireValuesEqual(action.TargetId, targetId)) continue;
            SubmitAndRetire(action);
            return true;
        }
        return false;
    }

    public bool TryClickFallback()
    {
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
        if (!HasInteractableAction()) return;
        _dragging = true;
        _dropAccepted = false;
        _originalParent = transform.parent;
        _originalSiblingIndex = transform.GetSiblingIndex();
        var rectTransform = transform as RectTransform;
        _originalAnchoredPosition = rectTransform == null ? Vector2.zero : rectTransform.anchoredPosition;
        _originalScale = transform.localScale;
        if (_canvas != null) transform.SetParent(_canvas.transform, true);
        if (_canvasGroup != null) _canvasGroup.blocksRaycasts = false;
        transform.SetAsLastSibling();
        transform.localScale = _originalScale * 1.04f;
        RuntimeBattleDropZone.NotifyDragStarted(this);
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging) return;
        transform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_dragging) return;
        _dragging = false;
        RuntimeBattleDropZone.NotifyDragEnded(this);
        if (!_dropAccepted)
        {
            if (_originalParent != null) transform.SetParent(_originalParent, false);
            var maxIndex = _originalParent == null ? 0 : _originalParent.childCount - 1;
            transform.SetSiblingIndex(Mathf.Clamp(_originalSiblingIndex, 0, Mathf.Max(0, maxIndex)));
            var rectTransform = transform as RectTransform;
            if (rectTransform != null) rectTransform.anchoredPosition = _originalAnchoredPosition;
        }
        transform.localScale = _originalScale;
        if (_canvasGroup != null) _canvasGroup.blocksRaycasts = true;
    }

    internal void RejectDrop()
    {
        _dropAccepted = false;
    }

    private void SubmitAndRetire(RuntimeLegalAction action)
    {
        _dropAccepted = true;
        if (_dragging) RuntimeBattleDropZone.NotifyDragEnded(this);
        _dragging = false;
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
    private object? _targetId;
    private Outline? _outline;
    private Color _normalColor;
    private bool _highlighted;

    public object? TargetId => _targetId;
    public bool IsHighlighted => _highlighted;

    public void Configure(object? targetId)
    {
        _targetId = targetId;
        _outline = GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
        _normalColor = _outline.effectColor;
        _outline.effectDistance = new Vector2(1f, 1f);
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
        var drag = eventData.pointerDrag == null ? null : eventData.pointerDrag.GetComponent<RuntimeBattleCardDrag>();
        SetHighlighted(drag != null && drag.CanAccept(_targetId));
    }

    public void OnPointerExit(PointerEventData eventData) { SetHighlighted(false); }

    public void OnDrop(PointerEventData eventData)
    {
        var drag = eventData.pointerDrag == null ? null : eventData.pointerDrag.GetComponent<RuntimeBattleCardDrag>();
        if (drag == null || !drag.TrySubmitForTarget(_targetId))
            drag?.RejectDrop();
        SetHighlighted(false);
    }

    internal static void NotifyDragStarted(RuntimeBattleCardDrag drag)
    {
        for (var index = 0; index < ActiveZones.Count; index++)
            ActiveZones[index].SetHighlighted(ActiveZones[index].IsTargetFor(drag));
    }

    internal static void NotifyDragEnded(RuntimeBattleCardDrag drag)
    {
        for (var index = 0; index < ActiveZones.Count; index++)
            ActiveZones[index].SetHighlighted(false);
    }

    private bool IsTargetFor(RuntimeBattleCardDrag drag) { return drag != null && drag.CanAccept(_targetId); }

    private void SetHighlighted(bool highlighted)
    {
        _highlighted = highlighted;
        if (_outline == null) _outline = GetComponent<Outline>();
        if (_outline == null) return;
        _outline.effectColor = highlighted ? new Color(0.34f, 0.93f, 0.95f, 1f) : _normalColor;
        _outline.effectDistance = highlighted ? new Vector2(4f, 4f) : new Vector2(1f, 1f);
    }
}
}
