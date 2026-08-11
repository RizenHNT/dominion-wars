namespace DominionWars.Engine.Effects
{

public sealed class NegateEnemyEffectsTurnEffect : IEffect
{
    public string ActionName => EffectNames.NegateEnemyEffectsTurn;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.NegateEnemyEffectsTurn(spec, context);
}
}
