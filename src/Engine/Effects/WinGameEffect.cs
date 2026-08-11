namespace DominionWars.Engine.Effects
{

public sealed class WinGameEffect : IEffect
{
    public string ActionName => EffectNames.WinGame;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.WinGame(spec, context);
}
}
