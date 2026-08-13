namespace DominionWars.Engine.Effects
{

public sealed class EffectSpec
{
    public EffectSpec(
        string action,
        string? target = null,
        int amount = 0,
        string? param = null,
        bool? kingSlayer = null,
        string? condition = null)
    {
        Action = action;
        Target = target;
        Amount = amount;
        Param = param;
        KingSlayer = kingSlayer;
        Condition = condition;
    }

    public string Action { get; }
    public string? Target { get; }
    public int Amount { get; }
    public string? Param { get; }
    /// <summary>Effect-level leader-resistance bypass. Null falls back to the legacy card-level flag.</summary>
    public bool? KingSlayer { get; }
    public string? Condition { get; }
}
}
