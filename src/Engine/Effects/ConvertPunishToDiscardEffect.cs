namespace DominionWars.Engine.Effects
{

public sealed class ConvertPunishToDiscardEffect : IEffect
{
    public string ActionName => EffectNames.ConvertPunishToDiscard;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.ConvertPunishToDiscard(spec, context);
}
}
