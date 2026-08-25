namespace DominionWars.Engine.Effects
{

public sealed class ControlEffect : IEffect
{
    public string ActionName => EffectNames.Control;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher)
    {
        dispatcher.Runtime.Control(spec, context);
    }
}
}
