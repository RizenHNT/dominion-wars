#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DominionWars.Unity.EditorTools
{

/// <summary>
/// Stages the repository's legacy card/deck data and validated content manifest
/// into Unity's StreamingAssets only when a player build is requested. Generated
/// files are never a source of truth; repository data remains authoritative.
/// </summary>
public sealed class RuntimeDataStreamingBuildPreprocessor :
    IPreprocessBuildWithReport,
    IPostprocessBuildWithReport
{
    internal const string GeneratedRelativePath = "Assets/StreamingAssets/data";
    private const string CardsDirectoryName = "cards";
    private const string DecksDirectoryName = "decks";
    private static BuildOutputBaseline? _outputBaseline;
    private static PendingBuildCleanup? _pendingBuildCleanup;

    private sealed class BuildOutputBaseline
    {
        public BuildOutputBaseline(string outputPath, string fingerprint)
        {
            OutputPath = outputPath ?? string.Empty;
            Fingerprint = fingerprint ?? "unavailable";
        }

        public string OutputPath { get; }
        public string Fingerprint { get; }
    }

    private sealed class PendingBuildCleanup
    {
        public PendingBuildCleanup(BuildReport report, BuildOutputBaseline baseline)
        {
            Report = report;
            Baseline = baseline;
        }

        public BuildReport Report { get; }
        public BuildOutputBaseline Baseline { get; }
    }

    public int callbackOrder => 1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));

        // Unity 6 may call postprocess while the summary is still Unknown.
        // Capture the output before this build so that a transient Unknown
        // cannot clean an unchanged output from an earlier build.
        _outputBaseline = new BuildOutputBaseline(
            report.summary.outputPath,
            RuntimeDataStreamingBuildSafety.CaptureBuildOutputFingerprint(report.summary.outputPath));

        var repositoryDataRoot = ResolveRepositoryDataRoot();
        var contentRoot = Path.Combine(repositoryDataRoot, "content");
        var contentReport = ContentPipelineValidator.Validate(contentRoot, production: true);
        WriteContentBuildReport(contentReport);
        if (!contentReport.CanBuild)
        {
            throw new BuildFailedException(
                "Dominion Wars content validation blocked the build: " +
                contentReport.BlockingCount + " BLOCKING diagnostic(s). No generated content was deleted.");
        }

        var generatedContentRoot = ResolveGeneratedContentRoot();
        if (Directory.Exists(generatedContentRoot) && !ContentPipelineStaging.IsOwnedGeneratedRoot(generatedContentRoot))
        {
            throw new BuildFailedException(
                "Dominion Wars content path is not owned by the content build gate: " +
                ContentPipelineStaging.GeneratedRelativePath +
                ". Build refused to overwrite user StreamingAssets content.");
        }

        // Prepare the content outside Assets first. Validation and source copies
        // complete before either generated destination can be removed.
        var contentStagingRoot = ContentPipelineStaging.CreateStagingDirectory(contentRoot, contentReport);
        try
        {
            var sourceCards = RequireJsonDirectory(repositoryDataRoot, CardsDirectoryName);
            var sourceDecks = RequireJsonDirectory(repositoryDataRoot, DecksDirectoryName);
            var generatedRoot = ResolveGeneratedRoot();
            PrepareGeneratedRoot(generatedRoot);

            CopyDirectoryIntoGeneratedRoot(sourceCards, Path.Combine(generatedRoot, CardsDirectoryName));
            CopyDirectoryIntoGeneratedRoot(sourceDecks, Path.Combine(generatedRoot, DecksDirectoryName));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            ContentPipelineStaging.ReplaceGeneratedRoot(
                contentStagingRoot,
                generatedContentRoot,
                ContentPipelineStaging.GeneratedRelativePath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Debug.Log("Dominion Wars build data staged from " + repositoryDataRoot +
                " to " + GeneratedRelativePath + " and " +
                ContentPipelineStaging.GeneratedRelativePath +
                ". Generated content is owned and may be regenerated safely.");
        }
        finally
        {
            ContentPipelineStaging.TryDeleteDirectory(contentStagingRoot);
        }
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));
        var outputBaseline = GetOutputBaseline(report.summary.outputPath);
        Debug.Log(
            "Dominion Wars postprocess entered: result=" + report.summary.result +
            ", errors=" + report.summary.totalErrors +
            ", output=" + report.summary.outputPath + ".");

        // Unity 6 can invoke this callback before the final BuildReport result
        // is committed. A no-error non-success result is therefore deferred;
        // the editor update loop re-checks the same report after BuildPlayer
        // returns. A final non-success remains fail-closed.
        if (RuntimeDataStreamingBuildSafety.IsBuildDefinitelyFailed(
                report.summary.result,
                report.summary.totalErrors))
        {
            Debug.Log("Dominion Wars build data cleanup skipped because the build did not succeed. " +
                GeneratedRelativePath + " remains available for diagnosis.");
            ClearOutputBaseline(outputBaseline);
            return;
        }

        if (report.summary.result == BuildResult.Succeeded)
        {
            CleanupOwnedRootsAfterSuccessfulBuild();
            ClearOutputBaseline(outputBaseline);
            return;
        }

        if (RuntimeDataStreamingBuildSafety.ShouldDeferBuildCleanup(
                report.summary.result,
                report.summary.totalErrors))
        {
            SchedulePostprocessCleanup(report, outputBaseline);
            return;
        }

        Debug.Log("Dominion Wars build data cleanup skipped because the postprocess result was not conclusive. " +
            GeneratedRelativePath + " remains available for diagnosis.");
        ClearOutputBaseline(outputBaseline);
    }

    private static void SchedulePostprocessCleanup(BuildReport report, BuildOutputBaseline outputBaseline)
    {
        // Pipeline-driven builds can discard delayCall callbacks registered from
        // inside BuildPlayer. An update handler survives that boundary and runs
        // after BuildPlayer returns. Replacing the pending item is safe because
        // its baseline is unique to the current preprocess invocation.
        _pendingBuildCleanup = new PendingBuildCleanup(report, outputBaseline);
        EditorApplication.update -= ProcessPendingBuildCleanup;
        EditorApplication.update += ProcessPendingBuildCleanup;
    }

    private static void ProcessPendingBuildCleanup()
    {
        var pending = _pendingBuildCleanup;
        if (pending is null ||
            !RuntimeDataStreamingBuildSafety.TryConsumeMatching(
                ref _pendingBuildCleanup,
                pending))
        {
            EditorApplication.update -= ProcessPendingBuildCleanup;
            return;
        }

        EditorApplication.update -= ProcessPendingBuildCleanup;
        if (!ReferenceEquals(_outputBaseline, pending.Baseline))
        {
            Debug.Log("Dominion Wars build data cleanup skipped because the build output baseline belongs to another build.");
            return;
        }

        var report = pending.Report;
        Debug.Log(
            "Dominion Wars postprocess deferred check: result=" + report.summary.result +
            ", errors=" + report.summary.totalErrors +
            ", output=" + report.summary.outputPath + ".");
        if (RuntimeDataStreamingBuildSafety.IsBuildDefinitelyFailed(
                report.summary.result,
                report.summary.totalErrors))
        {
            Debug.Log("Dominion Wars build data cleanup skipped because the build did not succeed. " +
                GeneratedRelativePath + " remains available for diagnosis.");
            ClearOutputBaseline(pending.Baseline);
            return;
        }

        var outputChanged = RuntimeDataStreamingBuildSafety.HasBuildOutputChanged(
            report.summary.outputPath,
            pending.Baseline.Fingerprint);
        Debug.Log("Dominion Wars deferred build output changed from baseline: " + outputChanged + ".");
        if (!RuntimeDataStreamingBuildSafety.ShouldCleanupAfterBuild(
                report.summary.result,
                report.summary.totalErrors,
                outputChanged))
        {
            Debug.Log("Dominion Wars build data cleanup deferred report did not prove a successful output. " +
                GeneratedRelativePath + " remains available for diagnosis.");
            ClearOutputBaseline(pending.Baseline);
            return;
        }

        CleanupOwnedRootsAfterSuccessfulBuild();
        ClearOutputBaseline(pending.Baseline);
    }

    private static BuildOutputBaseline GetOutputBaseline(string outputPath)
    {
        var baseline = _outputBaseline;
        if (baseline is not null && PathsEqual(baseline.OutputPath, outputPath)) return baseline;
        return new BuildOutputBaseline(outputPath, "unavailable");
    }

    private static void ClearOutputBaseline(BuildOutputBaseline baseline)
    {
        if (ReferenceEquals(_outputBaseline, baseline)) _outputBaseline = null;
        if (_pendingBuildCleanup is not null && ReferenceEquals(_pendingBuildCleanup.Baseline, baseline))
        {
            _pendingBuildCleanup = null;
            EditorApplication.update -= ProcessPendingBuildCleanup;
        }
    }

    private static bool PathsEqual(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void CleanupOwnedRootsAfterSuccessfulBuild()
    {

        // Content is independent from the legacy card/deck staging path. Clean
        // only the exact owned root; a user-created StreamingAssets/content is
        // left untouched and reported as a safety error.
        ContentPipelineStaging.CleanupGeneratedRoot(
            ResolveGeneratedContentRoot(),
            ContentPipelineStaging.GeneratedRelativePath);

        var generatedRoot = ResolveGeneratedRoot();
        if (!Directory.Exists(generatedRoot))
        {
            Debug.Log("Dominion Wars build data cleanup found no generated directory at " +
                GeneratedRelativePath + ".");
            return;
        }

        if (!RuntimeDataStreamingBuildSafety.IsOwnedGeneratedDataRoot(generatedRoot))
        {
            if (RuntimeDataStreamingBuildSafety.IsOwnedCleanedDataRoot(generatedRoot))
            {
                Debug.Log("Dominion Wars build data cleanup found an already-clean generated root at " +
                    GeneratedRelativePath + "; controlled files were preserved.");
                return;
            }

            Debug.LogError("Dominion Wars refused to clean " + GeneratedRelativePath +
                ": ownership marker or generated-only shape is missing. " +
                "No StreamingAssets content was deleted.");
            return;
        }

        if (!CleanupGeneratedDataChildren(generatedRoot, "post-build data cleanup"))
            return;

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        Debug.Log("Dominion Wars build data generated children cleaned after successful build: " +
            GeneratedRelativePath + ". Controlled .gitignore and StreamingAssets folders were preserved.");
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

    private static string ResolveGeneratedContentRoot()
    {
        var streamingAssetsRoot = ResolveStreamingAssetsRoot();
        var generatedRoot = Path.GetFullPath(Path.Combine(streamingAssetsRoot, "content"));
        if (!ContentPipelineStaging.IsSafeDestination(generatedRoot, streamingAssetsRoot))
        {
            throw new BuildFailedException("The generated content path escaped StreamingAssets/content.");
        }

        return generatedRoot;
    }

    private static void WriteContentBuildReport(ContentValidationReport report)
    {
        try
        {
            var projectDirectory = Directory.GetParent(Application.dataPath);
            if (projectDirectory is null) return;
            ContentBuildReportWriter.Write(
                report,
                Path.Combine(projectDirectory.FullName, "Library", "ContentBuildReport.json"));
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            Debug.LogWarning("Dominion Wars could not write the content build report: " + exception.Message);
        }
    }

    private static void PrepareGeneratedRoot(string generatedRoot)
    {
        if (Directory.Exists(generatedRoot))
        {
            var ownedGeneratedRoot = RuntimeDataStreamingBuildSafety.IsOwnedGeneratedDataRoot(generatedRoot);
            var ownedCleanedRoot = RuntimeDataStreamingBuildSafety.IsOwnedCleanedDataRoot(generatedRoot);
            if (!ownedGeneratedRoot && !ownedCleanedRoot)
            {
                throw new BuildFailedException(
                    "Dominion Wars build data path is not owned by the build preprocessor: " +
                    GeneratedRelativePath + ". Build refused to overwrite user StreamingAssets content.");
            }

            if (ownedGeneratedRoot &&
                !CleanupGeneratedDataChildren(generatedRoot, "pre-build regeneration"))
            {
                throw new BuildFailedException(
                    "Dominion Wars could not remove the previous generated data children under " +
                    GeneratedRelativePath + ". Build refused to continue.");
            }

            if (ownedGeneratedRoot)
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        Directory.CreateDirectory(generatedRoot);
        WriteOwnershipFiles(generatedRoot);
    }

    private static bool CleanupGeneratedDataChildren(string generatedRoot, string operation)
    {
        var children = new[]
        {
            CardsDirectoryName,
            DecksDirectoryName,
            RuntimeDataStreamingBuildSafety.GeneratedMarkerName
        };

        foreach (var child in children)
        {
            var absolutePath = Path.Combine(generatedRoot, child);
            var assetPath = ToAssetPath(absolutePath);
            if (!DeleteAssetIfPresent(assetPath, absolutePath, operation)) return false;
        }

        return true;
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
        var metaPath = absolutePath + ".meta";
        if (!Directory.Exists(absolutePath) && !File.Exists(absolutePath) && !File.Exists(metaPath)) return true;

        if (!RuntimeDataStreamingBuildSafety.IsExpectedAssetPath(assetPath))
        {
            Debug.LogError("Dominion Wars refused " + operation +
                ": unexpected AssetDatabase path " + assetPath + ".");
            return false;
        }

        if (AssetDatabase.DeleteAsset(assetPath) &&
            !Directory.Exists(absolutePath) && !File.Exists(absolutePath) && !File.Exists(metaPath))
            return true;
        if (!Directory.Exists(absolutePath) && !File.Exists(absolutePath) && !File.Exists(metaPath)) return true;

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
            string.Equals(assetPath, "Assets/StreamingAssets", StringComparison.Ordinal) ||
            string.Equals(assetPath, "Assets/StreamingAssets/data/cards", StringComparison.Ordinal) ||
            string.Equals(assetPath, "Assets/StreamingAssets/data/cards.meta", StringComparison.Ordinal) ||
            string.Equals(assetPath, "Assets/StreamingAssets/data/decks", StringComparison.Ordinal) ||
            string.Equals(assetPath, "Assets/StreamingAssets/data/decks.meta", StringComparison.Ordinal) ||
            string.Equals(assetPath, "Assets/StreamingAssets/data/" + GeneratedMarkerName, StringComparison.Ordinal) ||
            string.Equals(assetPath, "Assets/StreamingAssets/data/" + GeneratedMarkerName + ".meta", StringComparison.Ordinal);
    }

    public static bool IsBuildDefinitelyFailed(BuildResult result, int totalErrors)
    {
        return totalErrors > 0;
    }

    public static bool ShouldCleanupAfterBuild(
        BuildResult result,
        int totalErrors,
        bool outputChanged)
    {
        // Unity 6 can leave the callback report at Unknown even after BuildPlayer
        // produced a successful output. The baseline belongs to this exact build,
        // so a changed output is sufficient only for Unknown with zero errors.
        // Failed and Cancelled always remain fail-closed.
        return totalErrors == 0 &&
            (result == BuildResult.Succeeded ||
             (result == BuildResult.Unknown && outputChanged));
    }

    public static bool ShouldDeferBuildCleanup(BuildResult result, int totalErrors)
    {
        return result != BuildResult.Succeeded && totalErrors == 0;
    }

    public static bool TryConsumeMatching<T>(ref T? pending, T expected)
        where T : class
    {
        if (!ReferenceEquals(pending, expected)) return false;
        pending = null;
        return true;
    }

    public static string CaptureBuildOutputFingerprint(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) return "unavailable";
        try
        {
            var fullPath = Path.GetFullPath(outputPath);
            if (File.Exists(fullPath))
            {
                var file = new FileInfo(fullPath);
                return "file|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks;
            }

            if (Directory.Exists(fullPath))
            {
                var builder = new StringBuilder("directory|");
                foreach (var filePath in Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var file = new FileInfo(filePath);
                    var relative = filePath.Substring(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1)
                        .Replace(Path.DirectorySeparatorChar, '/')
                        .Replace(Path.AltDirectorySeparatorChar, '/');
                    builder.Append(relative).Append('|').Append(file.Length).Append('|').Append(file.LastWriteTimeUtc.Ticks).Append(';');
                }
                return builder.ToString();
            }

            return "missing";
        }
        catch (ArgumentException)
        {
            return "unavailable";
        }
        catch (IOException)
        {
            return "unavailable";
        }
        catch (UnauthorizedAccessException)
        {
            return "unavailable";
        }
    }

    public static bool HasBuildOutputChanged(string outputPath, string baselineFingerprint)
    {
        var currentFingerprint = CaptureBuildOutputFingerprint(outputPath);
        if (string.Equals(currentFingerprint, "unavailable", StringComparison.Ordinal) ||
            string.Equals(baselineFingerprint, "unavailable", StringComparison.Ordinal))
            return false;
        return !string.Equals(currentFingerprint, baselineFingerprint, StringComparison.Ordinal);
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

    public static bool IsOwnedCleanedDataRoot(string path)
    {
        if (!Directory.Exists(path)) return false;

        try
        {
            var ignorePath = Path.Combine(path, ".gitignore");
            if (!File.Exists(ignorePath) ||
                !TextMatches(File.ReadAllText(ignorePath), GeneratedIgnoreContents))
                return false;

            foreach (var entry in Directory.GetFileSystemEntries(path))
            {
                var name = Path.GetFileName(entry);
                if (name == ".gitignore" || name == ".gitignore.meta") continue;
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
