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

    /// <summary>
    /// A top-level manual PULL's fee is fixed before lifecycle punishment can
    /// change the board. It is intentionally not copied by <see cref="ForSource"/>
    /// so nested PULL effects resolve against their own source and current state.
    /// </summary>
    internal PullCostSnapshot? PullCostSnapshot { get; set; }

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

internal readonly struct PullCostSnapshot
{
    public PullCostSnapshot(long sourceInstanceId, long targetInstanceId, int amount)
    {
        SourceInstanceId = sourceInstanceId;
        TargetInstanceId = targetInstanceId;
        Amount = amount;
    }

    public long SourceInstanceId { get; }
    public long TargetInstanceId { get; }
    public int Amount { get; }

    public bool Matches(CardInstance source, CardInstance target)
    {
        return SourceInstanceId == source.InstanceId
            && TargetInstanceId == target.InstanceId;
    }
}
}
