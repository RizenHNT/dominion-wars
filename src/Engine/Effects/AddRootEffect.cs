namespace DominionWars.Engine.Effects
{

public sealed class AddRootEffect : IEffect
{
    public string ActionName => EffectNames.AddRoot;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.AddRoot(spec, context);
}
}
