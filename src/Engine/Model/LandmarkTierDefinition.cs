using System;
using System.Collections.Generic;
using DominionWars.Engine.Effects;

namespace DominionWars.Engine.Model
{

/// <summary>
/// Declarative metadata for a landmark tier. Runtime tier advancement remains
/// a separate rules decision; loading this structure must not activate it.
/// </summary>
public sealed class LandmarkTierDefinition
{
    public LandmarkTierDefinition(
        int tier,
        string? effectText = null,
        int chant = 0,
        string? summonCardId = null,
        IEnumerable<EffectSpec>? effectSpecs = null)
    {
        if (tier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tier));
        }

        if (chant < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chant));
        }

        Tier = tier;
        EffectText = effectText;
        Chant = chant;
        SummonCardId = summonCardId;

        var copy = new List<EffectSpec>();
        if (effectSpecs is not null)
        {
            foreach (var effect in effectSpecs)
            {
                copy.Add(effect ?? throw new ArgumentException(
                    "Landmark tier effect lists cannot contain null.",
                    nameof(effectSpecs)));
            }
        }

        EffectSpecs = copy.AsReadOnly();
    }

    public int Tier { get; }
    public string? EffectText { get; }
    public int Chant { get; }
    public string? SummonCardId { get; }
    public IReadOnlyList<EffectSpec> EffectSpecs { get; }
}
}
