namespace DominionWars.Engine.Effects
{

public sealed class HealEffect : IEffect
{
    public string ActionName => EffectNames.Heal;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.Heal(spec, context);
}
}
