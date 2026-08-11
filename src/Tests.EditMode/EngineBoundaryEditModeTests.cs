#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using DominionWars.Engine.Effects;

namespace DominionWars.Tests.EditMode
{

public sealed class EngineBoundaryEditModeTests
{
    [Test]
    public void EngineContractExposesAllTwentyFourEffects()
    {
        Assert.That(EffectNames.All, Has.Count.EqualTo(24));
    }
}
}
#endif
