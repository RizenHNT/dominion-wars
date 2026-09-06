#nullable enable annotations

using System;
using System.Reflection;
using System.IO;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeContentContextIntegrationEditModeTests
{
    [Test]
    public void NewlyCreatedBootstrapDefaultsToSharedContextAndStartsThroughIt()
    {
        var bootstrapObject = new GameObject("RuntimeContentContextDefaultBootstrap");
        bootstrapObject.SetActive(false);
        RuntimeBootstrap bootstrap = null!;
        RuntimeContentContext context = null!;
        try
        {
            bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();

            Assert.That(bootstrap.UseSharedContentContext, Is.True,
                "A newly authored bootstrap must select the shared context by default.");
            context = bootstrap.SharedContentContext;
            Assert.That(context, Is.Not.Null);

            bootstrap.StartSession();

            Assert.That(bootstrap.Adapter, Is.Not.Null);
            Assert.That(bootstrap.SharedContentContext, Is.SameAs(context));
            Assert.That(context.HasCardCatalog, Is.True,
                "The default path must load the card catalog through its shared context.");
            Assert.That(context.DataRoot, Is.Not.Empty);
        }
        finally
        {
            bootstrap?.StopSession();
            context?.Dispose();
            Object.DestroyImmediate(bootstrapObject);
        }
    }

    [Test]
    public void ExplicitFalseKeepsLegacyResolutionAndDoesNotConstructSharedContext()
    {
        var bootstrapObject = new GameObject("RuntimeContentContextLegacyBootstrap");
        bootstrapObject.SetActive(false);
        try
        {
            var bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();
            SetPrivateField(bootstrap, "useSharedContentContext", false);

            Assert.That(bootstrap.UseSharedContentContext, Is.False);
            Assert.That(bootstrap.SharedContentContext, Is.Null);
            Assert.That(bootstrap.ContentContext, Is.Null);

            bootstrap.StartSession();

            Assert.That(bootstrap.Adapter, Is.Not.Null,
                "Explicit false must retain the tested legacy data-root route.");
            Assert.That(bootstrap.SharedContentContext, Is.Null,
                "The compatibility route must not construct a shared context.");
        }
        finally
        {
            Object.DestroyImmediate(bootstrapObject);
        }
    }

    [Test]
    public void RuntimeBootstrapSceneSerializesTheSharedContextDefault()
    {
        var scenePath = Path.Combine(Application.dataPath, "Scenes", "RuntimeBootstrap.unity");
        Assert.That(File.Exists(scenePath), Is.True,
            "The runtime bootstrap scene must remain present for the Player entry point.");

        var serialized = File.ReadAllText(scenePath).Replace("\r\n", "\n");
        var fieldLine = "\n  useSharedContentContext: 1\n";
        StringAssert.Contains(fieldLine, "\n" + serialized + "\n",
            "The checked-in bootstrap scene must opt into the shared context explicitly.");
        Assert.That(serialized.Split(new[] { "useSharedContentContext:" }, StringSplitOptions.None).Length - 1,
            Is.EqualTo(1),
            "The rollout switch must have one unambiguous serialized value.");
    }

    [Test]
    public void PanelAwakeBeforeBootstrapSessionUsesTheBootstrapOwnedSharedContext()
    {
        var bootstrapObject = new GameObject("RuntimeContentContextSharedBootstrap");
        bootstrapObject.SetActive(false);
        var panelObject = new GameObject("RuntimeContentContextSharedPanel", typeof(RectTransform));
        panelObject.SetActive(false);
        try
        {
            var bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();
            SetPrivateField(bootstrap, "useSharedContentContext", true);

            var context = bootstrap.SharedContentContext;
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();

            // Bind before the host has started a session to model a panel
            // whose Awake/initialization precedes RuntimeBootstrap.Awake.
            panel.Bind(bootstrap);
            Assert.That(panel.ContentContext, Is.SameAs(context));
            Assert.That(panel.ContentResolverOwnership,
                Is.EqualTo(RuntimeContentResolverOwnership.None));

            bootstrap.StartSession();
            panel.Bind(bootstrap);

            Assert.That(bootstrap.SharedContentContext, Is.SameAs(context));
            Assert.That(panel.ContentContext, Is.SameAs(context));
            Assert.That(panel.CardCatalog, Is.SameAs(context.CardCatalog));
            Assert.That(panel.Adapter, Is.SameAs(bootstrap.Adapter));
        }
        finally
        {
            if (panelObject != null) Object.DestroyImmediate(panelObject);
            if (bootstrapObject != null) Object.DestroyImmediate(bootstrapObject);
        }
    }

    [Test]
    public void RestartReusesContextAndPanelDestroyDoesNotDisposeBootstrapOwnership()
    {
        var bootstrapObject = new GameObject("RuntimeContentContextLifecycleBootstrap");
        bootstrapObject.SetActive(false);
        var panelObject = new GameObject("RuntimeContentContextLifecyclePanel", typeof(RectTransform));
        panelObject.SetActive(false);
        RuntimeContentContext context = null!;
        try
        {
            var bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();
            SetPrivateField(bootstrap, "useSharedContentContext", true);
            bootstrap.StartSession();
            context = bootstrap.SharedContentContext;

            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(bootstrap);
            bootstrap.StopSession();
            bootstrap.StartSession();

            Assert.That(bootstrap.SharedContentContext, Is.SameAs(context));
            Assert.That(context.IsDisposed, Is.False);

            // EditMode's immediate destruction of test-created runtime objects
            // does not invoke MonoBehaviour.OnDestroy synchronously.  Keep the
            // ownership assertion here at the explicit binding boundary; the
            // actual Unity destroy callback is covered by the PlayMode
            // companion test, where Object.Destroy runs through a frame.
            Object.DestroyImmediate(panelObject);
            panelObject = null!;
            Assert.That(context.IsDisposed, Is.False,
                "Destroying the panel must not dispose the bootstrap-owned context.");

            bootstrap.StopSession();
            context.Dispose();
            Assert.That(context.IsDisposed, Is.True,
                "The owner remains the only component allowed to dispose the shared context.");
        }
        finally
        {
            if (panelObject != null) Object.DestroyImmediate(panelObject);
            if (bootstrapObject != null) Object.DestroyImmediate(bootstrapObject);
        }
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        var field = instance.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "Expected rollout field is missing.");
        field!.SetValue(instance, value);
    }
}
}
