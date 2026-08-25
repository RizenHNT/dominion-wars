namespace DominionWars.Engine.Effects
{

/// <summary>Applies a negative stat adjustment to the selected target.</summary>
public sealed class EnfeebleEffect : IEffect
{
    public string ActionName => EffectNames.Enfeeble;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.Enfeeble(spec, context);
}
}
