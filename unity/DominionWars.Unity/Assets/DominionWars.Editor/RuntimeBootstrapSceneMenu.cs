#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using DominionWars.Unity.Runtime;

namespace DominionWars.Unity.EditorTools
{

/// <summary>Creates the minimal runtime bootstrap scene after Unity has given
/// the user a chance to save or cancel changes in the current scene.
/// RuntimeBattlePanel is created automatically after scene load, so this menu
/// does not serialize UI objects into YAML.</summary>
public static class RuntimeBootstrapSceneMenu
{
    private const string ScenePath = "Assets/Scenes/RuntimeBootstrap.unity";

    [MenuItem("Dominion Wars/Create Runtime Bootstrap Scene")]
    public static void CreateRuntimeBootstrapScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning("Dominion Wars runtime bootstrap scene creation was cancelled.");
            return;
        }

        EnsureDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        CreatePresentationCamera(scene);
        var bootstrap = new GameObject("DominionWarsRuntimeBootstrap");
        var bootstrapComponent = bootstrap.AddComponent<RuntimeBootstrap>();
        ApplyRuntimeBootstrapDefaults(bootstrapComponent);
        SceneManager.MoveGameObjectToScene(bootstrap, scene);
        SceneManager.SetActiveScene(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        var settings = new EditorBuildSettingsScene(ScenePath, true);
        var scenes = EditorBuildSettings.scenes;
        var found = false;
        for (var index = 0; index < scenes.Length; index++)
        {
            if (scenes[index].path != ScenePath) continue;
            scenes[index] = settings;
            found = true;
            break;
        }

        if (!found)
        {
            var expanded = new EditorBuildSettingsScene[scenes.Length + 1];
            scenes.CopyTo(expanded, 0);
            expanded[expanded.Length - 1] = settings;
            scenes = expanded;
        }

        EditorBuildSettings.scenes = scenes;
        Selection.activeGameObject = bootstrap;
        Debug.Log("Dominion Wars runtime bootstrap scene created and added to Build Settings: " + ScenePath +
            ". RuntimeBattlePanel will auto-create at runtime; card data is staged only during Build preprocessing.");
    }

    private static void ApplyRuntimeBootstrapDefaults(RuntimeBootstrap bootstrap)
    {
        // Do not rely on the C# field initializer alone: Unity serializes the
        // scene value, and a newly created scene must explicitly opt into the
        // shared data/content context while keeping the inspector escape hatch
        // available for hosts that set it back to false.
        var serializedBootstrap = new SerializedObject(bootstrap);
        var sharedContext = serializedBootstrap.FindProperty("useSharedContentContext");
        if (sharedContext == null)
        {
            Debug.LogError("RuntimeBootstrap is missing useSharedContentContext; the new scene was not configured.");
            return;
        }

        sharedContext.boolValue = RuntimeBootstrap.DefaultUseSharedContentContext;
        serializedBootstrap.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreatePresentationCamera(Scene scene)
    {
        var cameraObject = new GameObject(
            "Main Camera",
            typeof(Camera),
            typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.018f, 0.024f, 0.038f, 1f);
        camera.cullingMask = 0;
        camera.depth = -100f;
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
    }

    private static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
    }
}
}
#endif
