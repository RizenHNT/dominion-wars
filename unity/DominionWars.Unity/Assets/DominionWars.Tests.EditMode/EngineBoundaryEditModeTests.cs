#if UNITY_INCLUDE_TESTS
using DominionWars.Engine.Effects;
using NUnit.Framework;

namespace DominionWars.Unity.EditMode
{
    public sealed class EngineBoundaryEditModeTests
    {
        [Test]
        public void EnginePackageExposesContractActions()
        {
            Assert.That(EffectNames.All, Has.Count.EqualTo(24));
        }
    }
}
#endif
