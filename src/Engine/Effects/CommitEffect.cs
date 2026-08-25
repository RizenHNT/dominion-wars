namespace DominionWars.Engine.Effects
{

public sealed class CommitEffect : IEffect
{
    public string ActionName => EffectNames.Commit;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher)
    {
        dispatcher.Runtime.CommitCard(spec, context);
    }
}
}
