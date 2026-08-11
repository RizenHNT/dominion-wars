namespace DominionWars.Engine.Effects
{

public sealed class GrantKeywordEffect : IEffect
{
    public string ActionName => EffectNames.GrantKeyword;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.GrantKeyword(spec, context);
}
}
