using System;
using System.Collections.Generic;
using System.IO;
using DominionWars.Unity.EditorTools;
using NUnit.Framework;
using UnityEditor.Build.Reporting;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeDataStreamingBuildSafetyEditModeTests
{
    [TestCase("Assets/StreamingAssets/data")]
    [TestCase("Assets/StreamingAssets")]
    [TestCase("Assets/StreamingAssets/data/cards")]
    [TestCase("Assets/StreamingAssets/data/cards.meta")]
    [TestCase("Assets/StreamingAssets/data/decks")]
    [TestCase("Assets/StreamingAssets/data/decks.meta")]
    [TestCase("Assets/StreamingAssets/data/.runtime-data-generated")]
    [TestCase("Assets/StreamingAssets/data/.runtime-data-generated.meta")]
    public void AssetCleanupWhitelistAcceptsOnlyOwnedPaths(string assetPath)
    {
        Assert.That(RuntimeDataStreamingBuildSafety.IsExpectedAssetPath(assetPath), Is.True);
        Assert.That(RuntimeDataStreamingBuildSafety.IsExpectedAssetPath("Assets/StreamingAssets/data/notes.txt"), Is.False);
        Assert.That(RuntimeDataStreamingBuildSafety.IsExpectedAssetPath("Assets/StreamingAssets/data/cards/fixture.json"), Is.False);
        Assert.That(RuntimeDataStreamingBuildSafety.IsExpectedAssetPath("Assets/Other"), Is.False);
    }

    [Test]
    public void EmptyDirectoryPredicateRejectsMissingAndNonEmptyDirectories()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Assert.That(RuntimeDataStreamingBuildSafety.IsEmptyDirectory(root), Is.True);
            File.WriteAllText(Path.Combine(root, "user.asset"), "preserve");
            Assert.That(RuntimeDataStreamingBuildSafety.IsEmptyDirectory(root), Is.False);
        }
        finally
        {
            Directory.Delete(root, true);
        }

        Assert.That(RuntimeDataStreamingBuildSafety.IsEmptyDirectory(root), Is.False);
    }

    [Test]
    public void GeneratedShapeRequiresMarkerAndBothGeneratedDirectories()
    {
        var root = CreateGeneratedShape();
        try
        {
            Assert.That(RuntimeDataStreamingBuildSafety.IsOwnedGeneratedDataRoot(root), Is.True);
            File.Delete(Path.Combine(root, RuntimeDataStreamingBuildSafety.GeneratedMarkerName));
            Assert.That(RuntimeDataStreamingBuildSafety.IsOwnedGeneratedDataRoot(root), Is.True);
            File.Delete(Path.Combine(root, ".gitignore"));
            Assert.That(RuntimeDataStreamingBuildSafety.IsOwnedGeneratedDataRoot(root), Is.False);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void GeneratedShapeRejectsUnexpectedRecursiveFilesAndOrphanMeta()
    {
        var root = CreateGeneratedShape();
        try
        {
            var cards = Path.Combine(root, "cards");
            File.WriteAllText(Path.Combine(cards, "notes.txt"), "user content");
            Assert.That(RuntimeDataStreamingBuildSafety.IsOwnedGeneratedDataRoot(root), Is.False);
            File.Delete(Path.Combine(cards, "notes.txt"));

            File.WriteAllText(Path.Combine(cards, "orphan.json.meta"), "orphan");
            Assert.That(RuntimeDataStreamingBuildSafety.IsOwnedGeneratedDataRoot(root), Is.False);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void CleanedGeneratedDataRootPreservesControlledIgnoreOnly()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(root, ".gitignore"),
                RuntimeDataStreamingBuildSafety.GeneratedIgnoreContents);
            File.WriteAllText(Path.Combine(root, ".gitignore.meta"), "meta");
            Assert.That(RuntimeDataStreamingBuildSafety.IsOwnedCleanedDataRoot(root), Is.True);

            File.WriteAllText(Path.Combine(root, "notes.txt"), "user content");
            Assert.That(RuntimeDataStreamingBuildSafety.IsOwnedCleanedDataRoot(root), Is.False);
            File.Delete(Path.Combine(root, "notes.txt"));

            Directory.CreateDirectory(Path.Combine(root, "cards"));
            Assert.That(RuntimeDataStreamingBuildSafety.IsOwnedCleanedDataRoot(root), Is.False);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void UnknownPostprocessSummaryDefersUntilOutputCanProveSuccess()
    {
        Assert.That(
            RuntimeDataStreamingBuildSafety.ShouldDeferBuildCleanup(BuildResult.Unknown, 0),
            Is.True);
        Assert.That(
            RuntimeDataStreamingBuildSafety.ShouldCleanupAfterBuild(BuildResult.Unknown, 0, false),
            Is.False);
        Assert.That(
            RuntimeDataStreamingBuildSafety.ShouldCleanupAfterBuild(BuildResult.Unknown, 0, true),
            Is.True,
            "Unity 6 reports Unknown in postprocess even after a successful output was produced; " +
            "the current-build output fingerprint must allow the delayed cleanup.");
        Assert.That(
            RuntimeDataStreamingBuildSafety.ShouldDeferBuildCleanup(BuildResult.Failed, 0),
            Is.True);
        Assert.That(
            RuntimeDataStreamingBuildSafety.ShouldCleanupAfterBuild(BuildResult.Succeeded, 1, true),
            Is.False);
        Assert.That(
            RuntimeDataStreamingBuildSafety.ShouldCleanupAfterBuild(BuildResult.Unknown, 1, true),
            Is.False);
        Assert.That(
            RuntimeDataStreamingBuildSafety.ShouldCleanupAfterBuild(BuildResult.Failed, 0, true),
            Is.False);
        Assert.That(
            RuntimeDataStreamingBuildSafety.ShouldCleanupAfterBuild(BuildResult.Succeeded, 0, false),
            Is.True);
    }

    [Test]
    public void UnknownCleanupRejectsUnchangedOutputAndAcceptsChangedOutput()
    {
        var root = CreateTemporaryDirectory();
        var output = Path.Combine(root, "DominionWars.exe");
        try
        {
            File.WriteAllText(output, "old build");
            var baseline = RuntimeDataStreamingBuildSafety.CaptureBuildOutputFingerprint(output);
            Assert.That(RuntimeDataStreamingBuildSafety.HasBuildOutputChanged(output, baseline), Is.False);

            File.WriteAllText(output, "new build with changed length");
            Assert.That(RuntimeDataStreamingBuildSafety.HasBuildOutputChanged(output, baseline), Is.True);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void CleanupDecisionFailsClosedWithoutCurrentBuildEvidence()
    {
        Assert.That(
            RuntimeDataStreamingBuildSafety.ShouldCleanupAfterBuild(BuildResult.Cancelled, 0, true),
            Is.False);
        Assert.That(
            RuntimeDataStreamingBuildSafety.ShouldCleanupAfterBuild(BuildResult.Failed, 0, true),
            Is.False);
        Assert.That(
            RuntimeDataStreamingBuildSafety.ShouldCleanupAfterBuild(BuildResult.Unknown, 1, true),
            Is.False);
        Assert.That(
            RuntimeDataStreamingBuildSafety.ShouldCleanupAfterBuild(BuildResult.Unknown, 0, false),
            Is.False);
        Assert.That(
            RuntimeDataStreamingBuildSafety.HasBuildOutputChanged("missing-player.exe", "unavailable"),
            Is.False,
            "A domain reload loses the in-memory baseline and must fail closed rather than delete diagnostics.");
    }

    [Test]
    public void PendingCleanupTokenIsConsumedOnceAndNewBuildReplacesOldBuild()
    {
        object? pending = null;
        var firstBuild = new object();
        var secondBuild = new object();

        Assert.That(
            RuntimeDataStreamingBuildSafety.TryConsumeMatching(ref pending, firstBuild),
            Is.False,
            "A domain reload has no pending token and must not clean anything.");

        pending = firstBuild;
        pending = secondBuild;
        Assert.That(
            RuntimeDataStreamingBuildSafety.TryConsumeMatching(ref pending, firstBuild),
            Is.False,
            "A stale callback must not consume a newer build's pending cleanup.");
        Assert.That(
            RuntimeDataStreamingBuildSafety.TryConsumeMatching(ref pending, secondBuild),
            Is.True);
        Assert.That(pending, Is.Null);
        Assert.That(
            RuntimeDataStreamingBuildSafety.TryConsumeMatching(ref pending, secondBuild),
            Is.False,
            "A successful deferred callback may clean at most once.");

        pending = firstBuild;
        Assert.That(
            RuntimeDataStreamingBuildSafety.TryConsumeMatching(ref pending, firstBuild),
            Is.True,
            "An earlier missed callback must not permanently block the next build.");
    }

    private static string CreateGeneratedShape()
    {
        var root = CreateTemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root, "cards"));
        Directory.CreateDirectory(Path.Combine(root, "decks"));
        File.WriteAllText(
            Path.Combine(root, RuntimeDataStreamingBuildSafety.GeneratedMarkerName),
            RuntimeDataStreamingBuildSafety.GeneratedMarkerContents);
        File.WriteAllText(
            Path.Combine(root, ".gitignore"),
            RuntimeDataStreamingBuildSafety.GeneratedIgnoreContents);
        File.WriteAllText(Path.Combine(root, "cards.meta"), "meta");
        File.WriteAllText(Path.Combine(root, "decks.meta"), "meta");
        File.WriteAllText(Path.Combine(root, "cards", "fixture.json"), "{}");
        File.WriteAllText(Path.Combine(root, "cards", "fixture.json.meta"), "meta");
        File.WriteAllText(Path.Combine(root, "decks", "fixture.json"), "{}");
        File.WriteAllText(Path.Combine(root, "decks", "fixture.json.meta"), "meta");
        return root;
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw-streaming-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
}
