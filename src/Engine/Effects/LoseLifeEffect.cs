namespace DominionWars.Engine.Effects
{

public sealed class LoseLifeEffect : IEffect
{
    public string ActionName => EffectNames.LoseLife;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.LoseLife(spec, context);
}
}
