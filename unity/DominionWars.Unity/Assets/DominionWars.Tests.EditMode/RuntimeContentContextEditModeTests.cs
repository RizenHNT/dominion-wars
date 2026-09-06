#nullable enable annotations

using System;
using System.IO;
using System.Text;
using DominionWars.Unity.Runtime;
using NUnit.Framework;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeContentContextEditModeTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "dw-runtime-content-context-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Test]
    public void RuntimeRootFactoryPreservesEditorCandidateOrder()
    {
        var streamingAssets = Path.Combine(_root, "StreamingAssets");
        var environment = new RuntimeContentEnvironment(
            isEditor: true,
            explicitDataRoot: "explicit-data",
            streamingAssetsRoot: streamingAssets,
            cwdDataRoot: "cwd-data",
            projectDataRoot: "project-data",
            repositoryDataRoot: "repository-data");

        var roots = RuntimeDataRootPolicy.BuildRuntimeRoots(environment);

        Assert.That(
            roots.DataCandidates,
            Is.EqualTo(new[]
            {
                "explicit-data",
                "cwd-data",
                "project-data",
                "repository-data",
                Path.Combine(streamingAssets, "data"),
            }));
        Assert.That(roots.RequireGeneratedMarker, Is.False);
    }

    [Test]
    public void RuntimeRootFactoryUsesContentSiblingOfStreamingData()
    {
        var streamingAssets = Path.Combine(_root, "StreamingAssets");
        var roots = RuntimeDataRootPolicy.BuildRuntimeRoots(
            isEditor: true,
            explicitRoot: Path.Combine(_root, "data"),
            streamingAssetsRoot: streamingAssets,
            cwd: Path.Combine(_root, "cwd-data"),
            project: Path.Combine(_root, "project-data"),
            repo: Path.Combine(_root, "repository-data"));

        Assert.That(roots.StreamingDataRoot, Is.EqualTo(Path.Combine(streamingAssets, "data")));
        Assert.That(roots.ContentRoot, Is.EqualTo(Path.Combine(streamingAssets, "content")));
        Assert.That(
            Path.GetDirectoryName(roots.StreamingDataRoot),
            Is.EqualTo(Path.GetDirectoryName(roots.ContentRoot)));
        Assert.That(
            Path.GetFileName(roots.StreamingDataRoot),
            Is.EqualTo(RuntimeDataRootPolicy.DataDirectoryName));
        Assert.That(
            Path.GetFileName(roots.ContentRoot),
            Is.EqualTo(RuntimeDataRootPolicy.ContentDirectoryName));
    }

    [Test]
    public void FailedCatalogAndResolverLoadsAreRetryableAndSuccessfulInstancesAreCached()
    {
        var environment = CreateEnvironment(isEditor: true);
        var dataRoot = environment.ExplicitDataRoot;
        CreateDataRoot(dataRoot, "not-json");
        var contentRoot = Path.Combine(environment.StreamingAssetsRoot, "content");
        Directory.CreateDirectory(Path.Combine(contentRoot, "manifests"));

        var context = new RuntimeContentContext(environment);
        try
        {
            Assert.That(context.TryGetCardCatalog(out var missingCatalog), Is.False);
            Assert.That(missingCatalog, Is.Null);
            Assert.That(context.CardCatalog, Is.Null);

            WriteCardCatalog(dataRoot);
            Assert.That(context.TryGetCardCatalog(out var firstCatalog), Is.True);
            Assert.That(context.TryGetCardCatalog(out var secondCatalog), Is.True);
            Assert.That(secondCatalog, Is.SameAs(firstCatalog));
            Assert.That(context.CardCatalog, Is.SameAs(firstCatalog));
            Assert.That(context.DataRoot, Is.EqualTo(Path.GetFullPath(dataRoot)));

            Assert.That(context.TryGetContentResolver(out var missingResolver), Is.False);
            Assert.That(missingResolver, Is.Null);
            Assert.That(context.ContentResolver, Is.Null);

            WriteManifest(contentRoot);
            Assert.That(context.TryGetContentResolver(out var firstResolver), Is.True);
            Assert.That(context.TryGetContentResolver(out var secondResolver), Is.True);
            Assert.That(secondResolver, Is.SameAs(firstResolver));
            Assert.That(context.ContentResolver, Is.SameAs(firstResolver));
        }
        finally
        {
            context.Dispose();
        }
    }

    [Test]
    public void RequireApisReturnTheSameSuccessfulInstances()
    {
        var environment = CreateEnvironment(isEditor: true);
        CreateDataRoot(environment.ExplicitDataRoot, CardJson);
        var contentRoot = Path.Combine(environment.StreamingAssetsRoot, "content");
        Directory.CreateDirectory(Path.Combine(contentRoot, "manifests"));
        WriteManifest(contentRoot);

        var context = RuntimeContentContext.Require(environment);
        try
        {
            var cardCatalog = context.RequireCardCatalog();
            var resolver = context.RequireContentResolver();

            Assert.That(context.CardCatalog, Is.SameAs(cardCatalog));
            Assert.That(context.ContentResolver, Is.SameAs(resolver));
            Assert.That(context.ResolverOwnership, Is.EqualTo(RuntimeContentResolverOwnership.Owned));
            Assert.That(context.OwnsContentResolver, Is.True);
        }
        finally
        {
            context.Dispose();
        }
    }

    [Test]
    public void BorrowedResolverDoesNotClearTheContextOwnedTextureCache()
    {
        var environment = CreateEnvironment(isEditor: true);
        var contentRoot = Path.Combine(environment.StreamingAssetsRoot, "content");
        Directory.CreateDirectory(Path.Combine(contentRoot, "manifests"));
        WriteManifest(contentRoot);

        var context = new RuntimeContentContext(environment);
        try
        {
            Assert.That(context.TryBorrowContentResolver(out var lease), Is.True);
            Assert.That(lease.Ownership, Is.EqualTo(RuntimeContentResolverOwnership.Borrowed));
            Assert.That(lease.IsBorrowed, Is.True);
            Assert.That(lease.IsOwned, Is.False);

            var resolver = lease.Resolver;
            Assert.That(resolver.GetCardArt("context_card_art"), Is.Not.Null);
            Assert.That(resolver.TextureCacheCount, Is.EqualTo(1));
            lease.Dispose();

            Assert.That(context.TryGetContentResolver(out var cached), Is.True);
            Assert.That(cached, Is.SameAs(resolver));
            Assert.That(cached.TextureCacheCount, Is.EqualTo(1));
        }
        finally
        {
            context.Dispose();
        }
    }

    [Test]
    public void PlayerFactoryAndContextIgnoreAllEditorFallbacks()
    {
        var environment = CreateEnvironment(isEditor: false);
        CreateDataRoot(environment.RepositoryDataRoot, CardJson);
        var streamingDataRoot = Path.Combine(environment.StreamingAssetsRoot, "data");
        var context = new RuntimeContentContext(environment);
        try
        {
            Assert.That(
                context.DataCandidates,
                Is.EqualTo(new[] { streamingDataRoot }));
            Assert.That(context.RequireGeneratedMarker, Is.True);
            Assert.That(context.TryGetCardCatalog(out _), Is.False);

            CreateDataRoot(streamingDataRoot, CardJson);
            Assert.That(context.TryGetCardCatalog(out _), Is.False);

            File.WriteAllText(
                Path.Combine(streamingDataRoot, RuntimeDataRootPolicy.GeneratedMarkerName),
                RuntimeDataRootPolicy.GeneratedMarkerContents,
                Encoding.UTF8);
            Assert.That(context.TryGetCardCatalog(out var catalog), Is.True);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(context.DataRoot, Is.EqualTo(Path.GetFullPath(streamingDataRoot)));
        }
        finally
        {
            context.Dispose();
        }
    }

    [Test]
    public void PlayerContentResolutionIsPackagedOnlyAndFailsClosedWhenMissing()
    {
        var environment = CreateEnvironment(isEditor: false);
        var packagedDataRoot = Path.Combine(environment.StreamingAssetsRoot, "data");
        CreateDataRoot(packagedDataRoot, CardJson);
        File.WriteAllText(
            Path.Combine(packagedDataRoot, RuntimeDataRootPolicy.GeneratedMarkerName),
            RuntimeDataRootPolicy.GeneratedMarkerContents,
            Encoding.UTF8);

        var packagedContentRoot = Path.Combine(environment.StreamingAssetsRoot, "content");
        var editorOnlyContentRoot = Path.Combine(_root, "editor-only-content");
        Directory.CreateDirectory(Path.Combine(editorOnlyContentRoot, "manifests"));
        WriteManifest(editorOnlyContentRoot);

        var context = new RuntimeContentContext(environment);
        try
        {
            Assert.That(
                context.ContentRoot,
                Is.EqualTo(packagedContentRoot),
                "Player content must be the packaged sibling of StreamingAssets/data.");
            Assert.That(context.TryGetContentResolver(out _), Is.False,
                "A missing packaged manifest must fail closed instead of using an editor fallback.");
            Assert.That(context.ContentResolver, Is.Null);

            Directory.CreateDirectory(packagedContentRoot);
            Directory.CreateDirectory(Path.Combine(packagedContentRoot, "manifests"));
            Assert.That(context.TryGetContentResolver(out _), Is.False,
                "A packaged content directory without its manifest remains unavailable.");
            Assert.That(context.ContentResolver, Is.Null);

            WriteManifest(packagedContentRoot);
            Assert.That(context.TryGetContentResolver(out var resolver), Is.True);
            Assert.That(resolver.ContentRoot, Is.EqualTo(Path.GetFullPath(packagedContentRoot)));
            Assert.That(resolver.IsAvailable, Is.True);
        }
        finally
        {
            context.Dispose();
        }
    }

    private RuntimeContentEnvironment CreateEnvironment(bool isEditor)
    {
        return new RuntimeContentEnvironment(
            isEditor,
            explicitDataRoot: Path.Combine(_root, "explicit-data"),
            streamingAssetsRoot: Path.Combine(_root, "StreamingAssets"),
            cwdDataRoot: Path.Combine(_root, "cwd-data"),
            projectDataRoot: Path.Combine(_root, "project-data"),
            repositoryDataRoot: Path.Combine(_root, "repository-data"));
    }

    private static void CreateDataRoot(string root, string cardJson)
    {
        Directory.CreateDirectory(Path.Combine(root, "cards"));
        Directory.CreateDirectory(Path.Combine(root, "decks"));
        File.WriteAllText(Path.Combine(root, "cards", "card.json"), cardJson, Encoding.UTF8);
    }

    private static void WriteCardCatalog(string root)
    {
        File.WriteAllText(Path.Combine(root, "cards", "card.json"), CardJson, Encoding.UTF8);
    }

    private static void WriteManifest(string contentRoot)
    {
        File.WriteAllText(
            Path.Combine(contentRoot, "manifests", "content.manifest.json"),
            ManifestJson,
            Encoding.UTF8);
    }

    private const string CardJson =
        "[{\"id\":\"context_card\",\"name\":\"Context Card\",\"faction\":\"无阵营\",\"type\":\"SPELL\",\"text\":\"\"}]";

    private const string ManifestJson =
        "{\"manifestVersion\":1,\"schemaVersion\":\"1.0.0\",\"contentVersion\":\"1.0.0\",\"assets\":[" +
        "{\"id\":\"context_card_art\",\"kind\":\"card_art\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"approved\"}" +
        "],\"aliases\":[]}";
}
}
