namespace DominionWars.Engine.Effects
{

/// <summary>Moves a selected card back into its owner's deck without a death.</summary>
public sealed class BanishEffect : IEffect
{
    public string ActionName => EffectNames.Banish;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.Banish(spec, context);
}
}
