namespace DominionWars.Engine.Effects
{

public sealed class DestroyEffect : IEffect
{
    public string ActionName => EffectNames.Destroy;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.Destroy(spec, context);
}
}
