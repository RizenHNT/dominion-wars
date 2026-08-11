namespace DominionWars.Engine.Effects
{

public sealed class EndTurnEffect : IEffect
{
    public string ActionName => EffectNames.EndTurn;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.EndTurn(spec, context);
}
}
