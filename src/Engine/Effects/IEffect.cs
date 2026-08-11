namespace DominionWars.Engine.Effects
{

public interface IEffect
{
    string ActionName { get; }

    void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher);
}
}
