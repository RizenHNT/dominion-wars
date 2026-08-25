namespace DominionWars.Engine.Effects
{

public sealed class PullEffect : IEffect
{
    public string ActionName => EffectNames.Pull;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher)
    {
        dispatcher.Runtime.Pull(spec, context);
    }
}
}
