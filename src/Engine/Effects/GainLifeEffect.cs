namespace DominionWars.Engine.Effects
{

public sealed class GainLifeEffect : IEffect
{
    public string ActionName => EffectNames.GainLife;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.GainLife(spec, context);
}
}
