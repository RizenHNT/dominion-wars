namespace DominionWars.Engine.Effects
{

public sealed class ProtectTurnEffect : IEffect
{
    public string ActionName => EffectNames.ProtectTurn;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.ProtectTurn(spec, context);
}
}
