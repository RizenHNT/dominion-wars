namespace DominionWars.Engine.Effects
{

public interface IEffectDispatcher
{
    EffectRuntime Runtime { get; }

    void Apply(EffectSpec spec, EffectContext context);
}
}
