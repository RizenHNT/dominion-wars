namespace DominionWars.Engine.Effects
{

public sealed class OppDrawEffect : IEffect
{
    public string ActionName => EffectNames.OppDraw;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.OppDraw(spec, context);
}
}
