#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DominionWars.Unity.EditorTools
{

/// <summary>
/// Stages the repository's data/cards and data/decks into Unity's
/// StreamingAssets only when a player build is requested. The generated JSON
/// is ignored by the generated-folder .gitignore and is never a source of
/// truth; the repository data remains authoritative.
/// </summary>
public sealed class RuntimeDataStreamingBuildPreprocessor :
    IPreprocessBuildWithReport,
    IPostprocessBuildWithReport
{
    internal const string GeneratedRelativePath = "Assets/StreamingAssets/data";
    private const string StreamingAssetsRelativePath = "Assets/StreamingAssets";
    private const string CardsDirectoryName = "cards";
    private const string DecksDirectoryName = "decks";

    public int callbackOrder => 1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));

        var repositoryDataRoot = ResolveRepositoryDataRoot();
        var sourceCards = RequireJsonDirectory(repositoryDataRoot, CardsDirectoryName);
        var sourceDecks = RequireJsonDirectory(repositoryDataRoot, DecksDirectoryName);
        var generatedRoot = ResolveGeneratedRoot();
        PrepareGeneratedRoot(generatedRoot);

        CopyDirectoryIntoGeneratedRoot(sourceCards, Path.Combine(generatedRoot, CardsDirectoryName));
        CopyDirectoryIntoGeneratedRoot(sourceDecks, Path.Combine(generatedRoot, DecksDirectoryName));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        Debug.Log("Dominion Wars build data staged from " + repositoryDataRoot +
            " to " + GeneratedRelativePath + ". Generated JSON is ignored and may be regenerated safely.");
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));

        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.Log("Dominion Wars build data cleanup skipped because the build did not succeed. " +
                GeneratedRelativePath + " remains available for diagnosis.");
            return;
        }

        var generatedRoot = ResolveGeneratedRoot();
        if (!Directory.Exists(generatedRoot))
        {
            Debug.Log("Dominion Wars build data cleanup found no generated directory at " +
                GeneratedRelativePath + ".");
            return;
        }

        if (!RuntimeDataStreamingBuildSafety.IsOwnedGeneratedDataRoot(generatedRoot))
        {
            Debug.LogError("Dominion Wars refused to clean " + GeneratedRelativePath +
                ": ownership marker or generated-only shape is missing. " +
                "No StreamingAssets content was deleted.");
            return;
        }

        if (!DeleteAssetIfPresent(GeneratedRelativePath, generatedRoot, "post-build data cleanup"))
            return;

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var streamingAssetsRoot = ResolveStreamingAssetsRoot();
        if (Directory.Exists(streamingAssetsRoot) &&
            RuntimeDataStreamingBuildSafety.IsEmptyDirectory(streamingAssetsRoot))
        {
            if (!DeleteAssetIfPresent(
                    StreamingAssetsRelativePath,
                    streamingAssetsRoot,
                    "empty StreamingAssets cleanup"))
                return;
        }

        Debug.Log("Dominion Wars build data cleaned after successful build: " +
            GeneratedRelativePath + ". Other StreamingAssets content was preserved.");
    }

    private static string ResolveRepositoryDataRoot()
    {
        var assetsDirectory = new DirectoryInfo(Application.dataPath);
        var projectDirectory = assetsDirectory.Parent;
        var repositoryDirectory = projectDirectory?.Parent?.Parent;
        var dataDirectory = repositoryDirectory is null
            ? null
            : Path.Combine(repositoryDirectory.FullName, "data");

        if (string.IsNullOrWhiteSpace(dataDirectory) || !Directory.Exists(dataDirectory))
        {
            throw new BuildFailedException(
                "Dominion Wars build data was not found. Expected repository data directory at " +
                (dataDirectory ?? "<unresolved>") + ". Build refused; no generated data was staged.");
        }

        return dataDirectory;
    }

    private static string RequireJsonDirectory(string dataRoot, string childName)
    {
        var directory = Path.Combine(dataRoot, childName);
        if (!Directory.Exists(directory) || Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).Length == 0)
        {
            throw new BuildFailedException(
                "Dominion Wars build data is incomplete. Expected JSON files under " + directory + ".");
        }

        return directory;
    }

    private static string ResolveGeneratedRoot()
    {
        var generatedRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "data"));
        var expectedPrefix = Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets")) +
            Path.DirectorySeparatorChar;
        if (!generatedRoot.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new BuildFailedException("The generated StreamingAssets path escaped the Unity project.");
        }

        return generatedRoot;
    }

    private static string ResolveStreamingAssetsRoot()
    {
        var streamingAssetsRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets"));
        var assetsRoot = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
        if (!streamingAssetsRoot.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            throw new BuildFailedException("The StreamingAssets path escaped the Unity project.");
        return streamingAssetsRoot;
    }

    private static void PrepareGeneratedRoot(string generatedRoot)
    {
        if (Directory.Exists(generatedRoot))
        {
            if (!RuntimeDataStreamingBuildSafety.IsOwnedGeneratedDataRoot(generatedRoot))
            {
                throw new BuildFailedException(
                    "Dominion Wars build data path is not owned by the build preprocessor: " +
                    GeneratedRelativePath + ". Build refused to overwrite user StreamingAssets content.");
            }

            if (!DeleteAssetIfPresent(GeneratedRelativePath, generatedRoot, "pre-build regeneration"))
            {
                throw new BuildFailedException(
                    "Dominion Wars could not remove the previous generated data asset: " +
                    GeneratedRelativePath + ". Build refused to continue.");
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        Directory.CreateDirectory(generatedRoot);
        WriteOwnershipFiles(generatedRoot);
    }

    private static void CopyDirectoryIntoGeneratedRoot(string source, string destination)
    {
        var generatedRoot = ResolveGeneratedRoot();
        var fullDestination = Path.GetFullPath(destination);
        var expectedPrefix = generatedRoot + Path.DirectorySeparatorChar;
        if (!fullDestination.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new BuildFailedException("The generated data copy target is outside StreamingAssets/data.");
        }

        if (Directory.Exists(fullDestination))
        {
            var destinationAssetPath = ToAssetPath(fullDestination);
            if (!AssetDatabase.DeleteAsset(destinationAssetPath))
            {
                throw new BuildFailedException(
                    "Dominion Wars could not replace generated asset " + destinationAssetPath + ".");
            }
        }

        Directory.CreateDirectory(fullDestination);
        CopyDirectoryRecursive(new DirectoryInfo(source), new DirectoryInfo(fullDestination));
    }

    private static void WriteOwnershipFiles(string generatedRoot)
    {
        File.WriteAllText(
            Path.Combine(generatedRoot, RuntimeDataStreamingBuildSafety.GeneratedMarkerName),
            RuntimeDataStreamingBuildSafety.GeneratedMarkerContents);
        File.WriteAllText(
            Path.Combine(generatedRoot, ".gitignore"),
            RuntimeDataStreamingBuildSafety.GeneratedIgnoreContents);
    }

    private static bool DeleteAssetIfPresent(string assetPath, string absolutePath, string operation)
    {
        if (!Directory.Exists(absolutePath) && !File.Exists(absolutePath)) return true;

        if (!RuntimeDataStreamingBuildSafety.IsExpectedAssetPath(assetPath))
        {
            Debug.LogError("Dominion Wars refused " + operation +
                ": unexpected AssetDatabase path " + assetPath + ".");
            return false;
        }

        if (AssetDatabase.DeleteAsset(assetPath)) return true;
        if (!Directory.Exists(absolutePath) && !File.Exists(absolutePath)) return true;

        Debug.LogError("Dominion Wars could not delete " + operation + " asset " +
            assetPath + ". No fallback filesystem deletion was attempted.");
        return false;
    }

    private static string ToAssetPath(string absolutePath)
    {
        var assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(absolutePath);
        if (!fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new BuildFailedException("Generated asset path escaped the Unity Assets directory.");
        return "Assets" + fullPath.Substring(assetsRoot.Length).Replace('\\', '/');
    }

    private static void CopyDirectoryRecursive(DirectoryInfo source, DirectoryInfo destination)
    {
        // Only card/deck JSON is runtime content. Do not copy README files,
        // editor notes, or other source artifacts into a player build.
        foreach (var file in source.GetFiles("*.json", SearchOption.TopDirectoryOnly))
        {
            file.CopyTo(Path.Combine(destination.FullName, file.Name), true);
        }

        foreach (var child in source.GetDirectories())
        {
            if (child.GetFiles("*.json", SearchOption.AllDirectories).Length == 0) continue;
            var destinationChild = destination.CreateSubdirectory(child.Name);
            CopyDirectoryRecursive(child, destinationChild);
        }
    }
}

/// <summary>
/// Pure safety predicates kept separate from Unity state so path and empty
/// directory decisions can be covered without invoking a player build.
/// </summary>
public static class RuntimeDataStreamingBuildSafety
{
    public const string GeneratedMarkerName = ".runtime-data-generated";
    public const string GeneratedMarkerContents =
        "Generated by RuntimeDataStreamingBuildPreprocessor.cs.\n";
    public const string GeneratedIgnoreContents =
        "# Generated by RuntimeDataStreamingBuildPreprocessor.cs during player builds.\n" +
        "# Repository data/cards and data/decks remain authoritative; these files are\n" +
        "# intentionally ignored and may be regenerated or removed at any time.\n" +
        "*\n" +
        "!.gitignore\n";

    public static bool IsExpectedAssetPath(string assetPath)
    {
        return string.Equals(assetPath, "Assets/StreamingAssets/data", StringComparison.Ordinal) ||
            string.Equals(assetPath, "Assets/StreamingAssets", StringComparison.Ordinal);
    }

    public static bool IsEmptyDirectory(string path)
    {
        if (!Directory.Exists(path)) return false;
        try
        {
            using (var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator())
                return !entries.MoveNext();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsOwnedGeneratedDataRoot(string path)
    {
        if (!Directory.Exists(path)) return false;

        try
        {
            var markerPath = Path.Combine(path, GeneratedMarkerName);
            var legacyIgnorePath = Path.Combine(path, ".gitignore");
            var markerMatches = File.Exists(markerPath) &&
                TextMatches(File.ReadAllText(markerPath), GeneratedMarkerContents);
            var legacyMarkerMatches = File.Exists(legacyIgnorePath) &&
                TextMatches(File.ReadAllText(legacyIgnorePath), GeneratedIgnoreContents);
            if (!markerMatches && !legacyMarkerMatches) return false;

            var cardsPath = Path.Combine(path, "cards");
            var decksPath = Path.Combine(path, "decks");
            if (!Directory.Exists(cardsPath) || !Directory.Exists(decksPath)) return false;
            if (!IsGeneratedContentDirectory(cardsPath) || !IsGeneratedContentDirectory(decksPath)) return false;

            foreach (var entry in Directory.GetFileSystemEntries(path))
            {
                var name = Path.GetFileName(entry);
                if (name == "cards" || name == "decks" ||
                    name == "cards.meta" || name == "decks.meta" ||
                    name == ".gitignore" || name == ".gitignore.meta" ||
                    name == GeneratedMarkerName || name == GeneratedMarkerName + ".meta")
                    continue;
                return false;
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsGeneratedContentDirectory(string path)
    {
        foreach (var entry in Directory.GetFileSystemEntries(path))
        {
            if (Directory.Exists(entry))
            {
                if (!IsGeneratedContentDirectory(entry)) return false;
                continue;
            }

            var name = Path.GetFileName(entry);
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;

            var sourcePath = entry.Substring(0, entry.Length - ".meta".Length);
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath)) return false;
        }

        return true;
    }

    private static bool TextMatches(string actual, string expected)
    {
        return string.Equals(
            actual.Replace("\r\n", "\n").Trim(),
            expected.Replace("\r\n", "\n").Trim(),
            StringComparison.Ordinal);
    }
}
}
#endif
