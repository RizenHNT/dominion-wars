namespace DominionWars.Engine.Turns
{

/// <summary>Built-in ids only; <see cref="TurnFlow"/> also accepts registered extension ids.</summary>
public static class TurnPhase
{
    public const string Start = "START";
    public const string Ambush = "AMBUSH";
    public const string Action = "ACTION";
    public const string Discard = "DISCARD";
    public const string End = "END";
    public const string Over = "OVER";
}
}
