#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DominionWars.Unity.PlayMode
{

[TestFixture]
public sealed class RuntimeContentContextIntegrationPlayModeTests
{
    [UnityTest]
    public IEnumerator DestroyingPanelDoesNotDisposeContextAndDestroyingBootstrapDoes()
    {
        var bootstrapObject = new GameObject("RuntimeContentContextPlayModeBootstrap");
        bootstrapObject.SetActive(false);
        var panelObject = new GameObject(
            "RuntimeContentContextPlayModePanel",
            typeof(RectTransform));
        panelObject.SetActive(false);
        RuntimeContentContext context = null!;
        try
        {
            var bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();
            SetPrivateField(bootstrap, "useSharedContentContext", true);
            Assert.That(bootstrap.TryStartSession(0, 1, out var reasonKey), Is.True,
                "The lifecycle fixture must start a data-backed session: " + reasonKey);
            context = bootstrap.SharedContentContext;

            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(bootstrap);
            Assert.That(panel.ContentContext, Is.SameAs(context));

            // A real active lifetime is required for Unity to dispatch the
            // MonoBehaviour destroy callback. Awake sees an already-created
            // adapter, so activating the host cannot create a second session.
            bootstrapObject.SetActive(true);
            panelObject.SetActive(true);

            Object.Destroy(panelObject);
            panelObject = null!;
            yield return null;
            Assert.That(context.IsDisposed, Is.False,
                "Destroying the borrowing panel must not dispose the owner context.");

            Object.Destroy(bootstrapObject);
            bootstrapObject = null!;
            yield return null;
            Assert.That(context.IsDisposed, Is.True,
                "Destroying the bootstrap owner must dispose its context exactly once.");
        }
        finally
        {
            if (panelObject != null) Object.Destroy(panelObject);
            if (bootstrapObject != null) Object.Destroy(bootstrapObject);
            if (context != null && !context.IsDisposed) context.Dispose();
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
#endif
