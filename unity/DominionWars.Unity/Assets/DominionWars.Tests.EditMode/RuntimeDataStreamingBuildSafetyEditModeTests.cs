using System;
using System.Collections.Generic;
using System.IO;
using DominionWars.Unity.EditorTools;
using NUnit.Framework;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeDataStreamingBuildSafetyEditModeTests
{
    [TestCase("Assets/StreamingAssets/data")]
    [TestCase("Assets/StreamingAssets")]
    public void AssetCleanupWhitelistAcceptsOnlyOwnedPaths(string assetPath)
    {
        Assert.That(RuntimeDataStreamingBuildSafety.IsExpectedAssetPath(assetPath), Is.True);
        Assert.That(RuntimeDataStreamingBuildSafety.IsExpectedAssetPath(assetPath + "/cards"), Is.False);
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
