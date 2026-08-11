namespace DominionWars.Engine.Effects
{

public sealed class AddOppPunishTurnEffect : IEffect
{
    public string ActionName => EffectNames.AddOppPunishTurn;

    public void Apply(EffectSpec spec, EffectContext context, IEffectDispatcher dispatcher) =>
        dispatcher.Runtime.AddOpponentPunishTurn(spec, context);
}
}
