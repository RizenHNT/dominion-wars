using System;

namespace DominionWars.Engine.Rules
{

/// <summary>Runtime-tunable match rules that are independent from card data.</summary>
public sealed class MatchRules
{
    public MatchRules(int handLimit = 8, int pioneerHandLimitBonus = 2)
    {
        if (handLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(handLimit));
        }

        if (pioneerHandLimitBonus < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pioneerHandLimitBonus));
        }

        HandLimit = handLimit;
        PioneerHandLimitBonus = pioneerHandLimitBonus;
    }

    public int HandLimit { get; }
    public int PioneerHandLimitBonus { get; }
}
}
