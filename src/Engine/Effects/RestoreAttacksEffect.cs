namespace DominionWars.Engine.Effects
{

public sealed class RestoreAttacksEffect : IEffect
{
    public string ActionName => EffectNames.RestoreAttacks;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.RestoreAttacks(spec, context);
}
}
