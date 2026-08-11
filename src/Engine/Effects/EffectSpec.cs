namespace DominionWars.Engine.Effects
{

public sealed class EffectSpec
{
    public EffectSpec(string action, string? target = null, int amount = 0, string? param = null)
    {
        Action = action;
        Target = target;
        Amount = amount;
        Param = param;
    }

    public string Action { get; }
    public string? Target { get; }
    public int Amount { get; }
    public string? Param { get; }
}
}
