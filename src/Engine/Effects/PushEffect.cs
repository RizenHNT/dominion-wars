namespace DominionWars.Engine.Effects
{

public sealed class PushEffect : IEffect
{
    public string ActionName => EffectNames.Push;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher)
    {
        dispatcher.Runtime.Push(spec, context);
    }
}
}
