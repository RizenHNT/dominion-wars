namespace DominionWars.Engine.Effects
{

public sealed class BuffEffect : IEffect
{
    public string ActionName => EffectNames.Buff;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.Buff(spec, context);
}
}
