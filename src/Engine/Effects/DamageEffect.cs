namespace DominionWars.Engine.Effects
{

public sealed class DamageEffect : IEffect
{
    public string ActionName => EffectNames.Damage;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.Damage(spec, context);
}
}
