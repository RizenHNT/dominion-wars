namespace DominionWars.Engine.Effects
{

public sealed class SkipReshuffleEffect : IEffect
{
    public string ActionName => EffectNames.SkipReshuffle;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.SkipReshuffle(spec, context);
}
}
