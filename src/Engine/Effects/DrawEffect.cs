namespace DominionWars.Engine.Effects
{

public sealed class DrawEffect : IEffect
{
    public string ActionName => EffectNames.Draw;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.Draw(spec, context);
}
}
