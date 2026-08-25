#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DominionWars.Unity.EditorTools
{

/// <summary>Non-development Windows build entry used by the local validation script.</summary>
public static class RuntimePlayerBuild
{
    private const string OutputArgument = "-buildOutput";

    public static void BuildWindows64FromCommandLine()
    {
        var outputPath = RequireOutputPath(Environment.GetCommandLineArgs());
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new BuildFailedException("Dominion Wars Windows build output directory is invalid.");
        if (File.Exists(outputPath) ||
            (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any()))
        {
            throw new BuildFailedException(
                "Dominion Wars refused to overwrite an existing Windows build output: " + outputDirectory);
        }

        Directory.CreateDirectory(outputDirectory);
        var scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled && AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path) != null)
            .Select(scene => scene.path)
            .ToArray();
        if (scenes.Length == 0)
            throw new BuildFailedException("Dominion Wars requires at least one enabled scene before building.");

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new BuildFailedException(
                "Dominion Wars Windows build failed: " + report.summary.result +
                ", errors=" + report.summary.totalErrors +
                ", warnings=" + report.summary.totalWarnings + ".");
        }

        Debug.Log("Dominion Wars non-development Windows build succeeded: " + outputPath);
    }

    private static string RequireOutputPath(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], OutputArgument, StringComparison.Ordinal)) continue;
            var value = args[index + 1];
            if (string.IsNullOrWhiteSpace(value)) break;
            var fullPath = Path.GetFullPath(value);
            if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("Dominion Wars Windows build output must be an .exe path.");
            return fullPath;
        }

        throw new BuildFailedException("Dominion Wars Windows build requires -buildOutput <path.exe>.");
    }
}
}
#endif
