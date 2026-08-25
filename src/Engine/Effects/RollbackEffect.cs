namespace DominionWars.Engine.Effects
{

public sealed class RollbackEffect : IEffect
{
    public string ActionName => EffectNames.Rollback;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher)
    {
        dispatcher.Runtime.Rollback(spec, context);
    }
}
}
