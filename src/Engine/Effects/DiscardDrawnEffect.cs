namespace DominionWars.Engine.Effects
{

public sealed class DiscardDrawnEffect : IEffect
{
    public string ActionName => EffectNames.DiscardDrawn;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.DiscardDrawn(spec, context);
}
}
