namespace DominionWars.Engine.Effects
{

public sealed class NegateEffect : IEffect
{
    public string ActionName => EffectNames.Negate;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.Negate(spec, context);
}
}
