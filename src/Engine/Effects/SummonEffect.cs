namespace DominionWars.Engine.Effects
{

public sealed class SummonEffect : IEffect
{
    public string ActionName => EffectNames.Summon;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.Summon(spec, context);
}
}
