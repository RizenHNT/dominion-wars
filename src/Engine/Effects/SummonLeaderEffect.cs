namespace DominionWars.Engine.Effects
{

public sealed class SummonLeaderEffect : IEffect
{
    public string ActionName => EffectNames.SummonLeader;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.SummonLeader(spec, context);
}
}
