namespace DominionWars.Engine.Effects
{

public sealed class AddSelfPunishTurnEffect : IEffect
{
    public string ActionName => EffectNames.AddSelfPunishTurn;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.AddSelfPunishTurn(spec, context);
}
}
