namespace DominionWars.Engine.Effects
{

public sealed class DamageCastleEffect : IEffect
{
    public string ActionName => EffectNames.DamageCastle;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.DamageCastle(spec, context);
}
}
