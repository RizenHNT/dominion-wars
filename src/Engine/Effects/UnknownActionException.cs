using System;

namespace DominionWars.Engine.Effects
{

public sealed class UnknownActionException : InvalidOperationException
{
    public UnknownActionException(string action)
        : base($"Unknown effect action '{action}'.")
    {
        ActionName = action;
    }

    public string ActionName { get; }
}
}
