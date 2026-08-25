using System;
using System.Collections.Generic;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Effects
{

public sealed class EffectContext
{
    public EffectContext(
        int sourcePlayerIndex,
        long rootEventId,
        CardInstance? sourceCard = null,
        CardInstance? playedCard = null,
        CardInstance? attacker = null,
        IReadOnlyList<CardInstance>? drawnCards = null,
        long? selectedTargetId = null,
        CoreTarget? selectedCoreTarget = null)
        : this(
            sourcePlayerIndex,
            rootEventId,
            sourceCard,
            playedCard,
            attacker,
            drawnCards,
            selectedTargetId,
            selectedCoreTarget,
            new EffectWindowState())
    {
    }

    private EffectContext(
        int sourcePlayerIndex,
        long rootEventId,
        CardInstance? sourceCard,
        CardInstance? playedCard,
        CardInstance? attacker,
        IReadOnlyList<CardInstance>? drawnCards,
        long? selectedTargetId,
        CoreTarget? selectedCoreTarget,
        EffectWindowState window)
    {
        if (sourcePlayerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePlayerIndex));
        }

        SourcePlayerIndex = sourcePlayerIndex;
        RootEventId = rootEventId;
        if (sourceCard is not null && sourceCard.ControllerPlayerIndex != sourcePlayerIndex)
        {
            throw new ArgumentException("The effect source card must be controlled by the source player.", nameof(sourceCard));
        }

        SourceCard = sourceCard;
        PlayedCard = playedCard;
        Attacker = attacker;
        DrawnCards = drawnCards ?? Array.Empty<CardInstance>();
        SelectedTargetId = selectedTargetId;
        SelectedCoreTarget = selectedCoreTarget;
        _window = window;
    }

    public int SourcePlayerIndex { get; }
    public long RootEventId { get; }
    public CardInstance? SourceCard { get; }
    public CardInstance? PlayedCard { get; }
    public CardInstance? Attacker { get; }
    public IReadOnlyList<CardInstance> DrawnCards { get; }
    public long? SelectedTargetId { get; }
    public CoreTarget? SelectedCoreTarget { get; }
    public bool Negated
    {
        get => _window.Negated;
        set => _window.Negated = value;
    }

    internal bool NegationObserved
    {
        get => _window.NegationObserved;
        set => _window.NegationObserved = value;
    }

    internal bool DeferDeaths
    {
        get => _window.DeferDeaths;
        set => _window.DeferDeaths = value;
    }

    public EffectContext ForSource(int sourcePlayerIndex, CardInstance? sourceCard)
    {
        return new EffectContext(
            sourcePlayerIndex,
            RootEventId,
            sourceCard,
            PlayedCard,
            Attacker,
            DrawnCards,
            SelectedTargetId,
            SelectedCoreTarget,
            _window);
    }

    private readonly EffectWindowState _window;
}

internal sealed class EffectWindowState
{
    public bool Negated { get; set; }
    public bool NegationObserved { get; set; }
    public bool DeferDeaths { get; set; }
}
}
