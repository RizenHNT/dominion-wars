using DominionWars.Unity.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeBootstrapEditModeTests
{
    [Test]
    public void BootstrapCreatesSnapshotFromPrebuiltData()
    {
        var gameObject = new GameObject("runtime-bootstrap-test");
        try
        {
            var bootstrap = gameObject.AddComponent<RuntimeBootstrap>();
            bootstrap.StartSession();
            Assert.That(bootstrap.Adapter, Is.Not.Null);
            Assert.That(bootstrap.Adapter!.Presentation.Snapshot, Is.Not.Null);
            Assert.That(bootstrap.Adapter.Presentation.Snapshot!.Phase, Is.EqualTo("AMBUSH"));
            Assert.That(bootstrap.Adapter.Presentation.Snapshot.SnapshotRevision, Is.EqualTo(1));
            Assert.That(bootstrap.Adapter.Presentation.Snapshot.Castle.Enabled, Is.True);
            Assert.That(bootstrap.Adapter.Presentation.EventDelta, Is.Not.Empty);
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }
}
}
