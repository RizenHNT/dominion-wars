#nullable enable annotations

using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DominionWars.Unity.UI
{

public enum RuntimeCardInspectTrigger
{
    Hover,
    Click,
}

/// <summary>
/// Small uGUI bridge for card inspection. It emits model events only; the
/// owning panel decides where DetailText is rendered. A click pins the detail
/// until the next click or an explicit Close call, while hover opens a
/// transient detail that closes on pointer exit.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class RuntimeCardInspectInteraction : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    private RuntimeCardInspectModel? _model;
    private bool _pointerOver;
    private bool _pinned;
    private bool _open;

    public RuntimeCardInspectModel? Model => _model;
    public bool IsPointerOver => _pointerOver;
    public bool IsPinned => _pinned;
    public bool IsOpen => _open;

    public event Action<RuntimeCardInspectModel, RuntimeCardInspectTrigger>? InspectionRequested;
    public event Action? InspectionClosed;

    /// <summary>
    /// Attaches the interaction bridge to an existing card root and makes its
    /// first UI Graphic raycastable. Card roots created by the current panel
    /// use an Image as their graphic; no card/action or gameplay state is
    /// changed by this helper.
    /// </summary>
    public static RuntimeCardInspectInteraction Attach(
        RectTransform cardRoot,
        RuntimeCardInspectModel model)
    {
        if (cardRoot == null) throw new ArgumentNullException(nameof(cardRoot));
        if (model == null) throw new ArgumentNullException(nameof(model));

        var graphic = cardRoot.GetComponent<UnityEngine.UI.Graphic>();
        if (graphic != null) graphic.raycastTarget = true;
        var interaction = cardRoot.GetComponent<RuntimeCardInspectInteraction>();
        if (interaction == null)
            interaction = cardRoot.gameObject.AddComponent<RuntimeCardInspectInteraction>();
        interaction.Bind(model);
        return interaction;
    }

    public void Bind(RuntimeCardInspectModel model)
    {
        Close();
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _pointerOver = false;
        _pinned = false;
        _open = false;
    }

    public void Clear()
    {
        Close();
        _model = null;
        _pointerOver = false;
        _pinned = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        RuntimeBattleCardDrag.DragDiagnostic(
            "Inspect enter object=" + gameObject.name +
            " pointer=" + (eventData == null ? "<null>" : eventData.position.ToString()));
        _pointerOver = true;
        Request(RuntimeCardInspectTrigger.Hover);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        RuntimeBattleCardDrag.DragDiagnostic(
            "Inspect exit object=" + gameObject.name +
            " pointer=" + (eventData == null ? "<null>" : eventData.position.ToString()));
        _pointerOver = false;
        if (!_pinned) Close();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        RuntimeBattleCardDrag.DragDiagnostic(
            "Inspect click object=" + gameObject.name +
            " pointer=" + (eventData == null ? "<null>" : eventData.position.ToString()));
        if (_model is null) return;

        _pinned = !_pinned;
        if (_pinned)
        {
            Request(RuntimeCardInspectTrigger.Click);
        }
        else
        {
            Close();
        }
    }

    public void Close()
    {
        _pinned = false;
        if (!_open) return;
        _open = false;
        InspectionClosed?.Invoke();
    }

    private void Request(RuntimeCardInspectTrigger trigger)
    {
        if (_model is null) return;
        _open = true;
        InspectionRequested?.Invoke(_model, trigger);
    }

    private void OnDisable()
    {
        _pointerOver = false;
        Close();
    }
}
}
