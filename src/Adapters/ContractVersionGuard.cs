using System;

namespace DominionWars.Adapters
{

public static class ContractVersionGuard
{
    public const int ExpectedVersion = 1;

    public static void Validate(int? actualVersion)
    {
        if (actualVersion != ExpectedVersion)
        {
            var actual = actualVersion.HasValue ? actualVersion.Value.ToString() : "missing";
            throw new StartupRejectException(
                $"contract version mismatch: expected {ExpectedVersion}, got {actual}");
        }
    }
}

public sealed class StartupRejectException : InvalidOperationException
{
    public StartupRejectException(string message)
        : base(message)
    {
    }
}
}
