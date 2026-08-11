namespace DominionWars.Engine.Effects
{

public sealed class DiscardOppRandomEffect : IEffect
{
    public string ActionName => EffectNames.DiscardOppRandom;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.DiscardOpponentRandom(spec, context);
}
}
