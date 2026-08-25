#if UNITY_INCLUDE_TESTS
using System.Linq;
using DominionWars.Engine.Effects;
using NUnit.Framework;

namespace DominionWars.Unity.EditMode
{
    public sealed class EngineBoundaryEditModeTests
    {
        [Test]
        public void EnginePackageExposesContractActions()
        {
            Assert.That(EffectNames.All, Is.Not.Empty);
            Assert.That(EffectNames.All, Is.Unique);
            Assert.That(EffectNames.All, Is.EquivalentTo(EffectNames.DeclaredActions));
            Assert.That(EffectNames.All.Count, Is.EqualTo(EffectNames.All.Distinct().Count()));
        }
    }
}
#endif
