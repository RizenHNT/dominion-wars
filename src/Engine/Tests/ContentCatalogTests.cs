using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DominionWars.Data;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{
    public sealed class ContentCatalogTests
    {
        private static string RepositoryRoot
        {
            get
            {
                var directory = TestContext.CurrentContext.TestDirectory;
                while (!string.IsNullOrEmpty(directory) && !Directory.Exists(Path.Combine(directory, "data", "content")))
                {
                    directory = Directory.GetParent(directory)?.FullName;
                }

                return directory!;
            }
        }

        [Test]
        public void ProductionManifestLoadsAndResolvesCardArt()
        {
            var root = RepositoryRoot;
            var manifest = Path.Combine(root, "data", "content", "manifests", "content.manifest.json");
            var catalog = ContentCatalog.LoadFile(manifest);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Manifest.ManifestVersion, Is.EqualTo(1));
                Assert.That(catalog.Assets, Has.Count.EqualTo(21));
                Assert.That(catalog.Aliases, Has.Count.EqualTo(4));
                Assert.That(catalog.ResolveAlias("flame_leader"), Is.EqualTo("card_art_flame_leader"));
                Assert.That(catalog.ResolveAlias("machine_leader"), Is.EqualTo("card_art_machine_alpha"));
                Assert.That(catalog.ResolveFallback("sea_leader"), Is.EqualTo("placeholder_card_art_sea"));
                Assert.That(catalog.ResolveFallback("placeholder_card_art_neutral"), Is.EqualTo("placeholder_card_art_neutral"));
                Assert.That(catalog.Assets.Keys, Is.EqualTo(catalog.Assets.Keys.OrderBy(value => value, StringComparer.Ordinal)));
            });

            Assert.Throws<InvalidDataException>(() => catalog.ResolveAlias("gate_of_fate"));
        }

        [Test]
        public void ResolutionIsDeterministicAcrossLoads()
        {
            var root = RepositoryRoot;
            var manifest = Path.Combine(root, "data", "content", "manifests", "content.manifest.json");
            var first = ContentCatalog.LoadFile(manifest);
            var second = ContentCatalog.LoadFile(manifest);

            Assert.That(first.Assets.Keys, Is.EqualTo(second.Assets.Keys));
            Assert.That(first.Aliases, Is.EqualTo(second.Aliases));
            foreach (var id in first.Assets.Keys)
            {
                Assert.That(first.ResolveFallback(id), Is.EqualTo(second.ResolveFallback(id)));
            }
        }

        [Test]
        public void DuplicateAssetIdIsRejected()
        {
            AssertManifestRejected(
                Manifest(Programmatic("duplicate_asset"), Programmatic("duplicate_asset")),
                "duplicate asset id");
        }

        [Test]
        public void DuplicateAssetPathIsRejected()
        {
            WithTempRoot(root =>
            {
                var path = Path.Combine(root, "shared.bin");
                File.WriteAllText(path, "shared", Encoding.UTF8);
                var hash = Sha256(path);
                var json = Manifest(FileAsset("asset_one", "shared.bin", hash), FileAsset("asset_two", "shared.bin", hash));
                AssertManifestRejected(root, json, "duplicate asset path");
            });
        }

        [Test]
        public void UnsafeRelativePathIsRejected()
        {
            AssertManifestRejected(
                Manifest(FileAsset("unsafe_asset", "../outside.bin", new string('a', 64))),
                "unsafe relativePath");
            AssertManifestRejected(
                Manifest(FileAsset("unsafe_asset", "nested\\outside.bin", new string('a', 64))),
                "unsafe relativePath");
        }

        [Test]
        public void HashMismatchIsRejected()
        {
            WithTempRoot(root =>
            {
                var path = Path.Combine(root, "asset.bin");
                File.WriteAllText(path, "actual", Encoding.UTF8);
                var json = Manifest(FileAsset("hashed_asset", "asset.bin", new string('0', 64)));
                AssertManifestRejected(root, json, "sha256 mismatch");
            });
        }

        [Test]
        public void MissingFallbackIsRejected()
        {
            AssertManifestRejected(
                Manifest(Programmatic("asset_without_fallback", "missing_asset")),
                "fallback asset target not found");
        }

        [Test]
        public void CyclicFallbackIsRejected()
        {
            AssertManifestRejected(
                Manifest(Programmatic("asset_a", "asset_b"), Programmatic("asset_b", "asset_a")),
                "fallback cycle");
        }

        [Test]
        public void AliasCycleIsRejected()
        {
            AssertManifestRejected(
                Manifest(new[] { Programmatic("asset_a") }, aliases: "[{\"fromId\":\"legacy_a\",\"toId\":\"legacy_b\"},{\"fromId\":\"legacy_b\",\"toId\":\"legacy_a\"}]"),
                "alias cycle");
        }

        [Test]
        public void DraftIsRejectedForProductionButAcceptedForAuthoring()
        {
            var json = Manifest(Programmatic("draft_asset", status: "draft"));
            AssertManifestRejected(json, "draft asset is not allowed in production");
            WithTempRoot(root =>
            {
                var catalog = ContentCatalog.LoadJson(json, root, production: false);
                Assert.That(catalog.Assets["draft_asset"].Status, Is.EqualTo("draft"));
            });
        }

        [Test]
        public void CardCatalogPreservesMissingArtIdCompatibilityAndMapsOptionalArtId()
        {
            var file = Path.GetTempFileName();
            try
            {
            File.WriteAllText(file, "{\"id\":\"compat_card\",\"name\":\"Compat\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"text\":\"\"}");
                var compatibilityCard = CardCatalog.LoadFile(file);
                Assert.That(compatibilityCard.ArtId, Is.Null);
                File.WriteAllText(file, "{\"id\":\"art_card\",\"name\":\"Art\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"artId\":\"card_art_flame_leader\",\"text\":\"\"}");
                var artCard = CardCatalog.LoadFile(file);
                Assert.That(artCard.ArtId, Is.EqualTo("card_art_flame_leader"));
                var cardCatalog = new CardCatalog(new Dictionary<string, CardDefinition>
                {
                    [compatibilityCard.Id] = compatibilityCard,
                    [artCard.Id] = artCard,
                });
                Assert.That(cardCatalog.TryGetArtId("art_card", out var resolvedArtId), Is.True);
                Assert.That(resolvedArtId, Is.EqualTo("card_art_flame_leader"));
                Assert.That(cardCatalog.TryGetArtId("compat_card", out _), Is.False);
                Assert.That(cardCatalog.TryGetArtId("missing_card", out _), Is.False);
                File.WriteAllText(file, "{\"id\":\"bad_art_card\",\"name\":\"Bad\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"artId\":true,\"text\":\"\"}");
                Assert.Throws<InvalidDataException>(() => CardCatalog.LoadFile(file));
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Test]
        public void CardCatalogDistinguishesMissingAndExplicitZeroMechanicalFees()
        {
            var file = Path.GetTempFileName();
            try
            {
                File.WriteAllText(file, "{\"id\":\"missing_fees\",\"name\":\"Missing Fees\",\"faction\":\"机械遗迹\",\"type\":\"SPELL\",\"text\":\"\"}");
                var missing = CardCatalog.LoadFile(file);
                File.WriteAllText(file, "{\"id\":\"explicit_zero_fees\",\"name\":\"Explicit Zero Fees\",\"faction\":\"机械遗迹\",\"type\":\"SPELL\",\"commitCost\":0,\"uploadCost\":0,\"downloadCost\":0,\"text\":\"\"}");
                var explicitZero = CardCatalog.LoadFile(file);

                Assert.Multiple(() =>
                {
                    Assert.That(missing.CommitCost, Is.Zero);
                    Assert.That(missing.UploadCost, Is.Zero);
                    Assert.That(missing.DownloadCost, Is.Zero);
                    Assert.That(explicitZero.CommitCost, Is.Zero);
                    Assert.That(explicitZero.UploadCost, Is.Zero);
                    Assert.That(explicitZero.DownloadCost, Is.Zero);
                });

                var catalogRoot = Path.Combine(
                    Path.GetTempPath(),
                    "dw-card-presence-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(catalogRoot);
                try
                {
                    File.WriteAllText(
                        Path.Combine(catalogRoot, "cards.json"),
                        "[{\"id\":\"missing_fees\",\"name\":\"Missing Fees\",\"faction\":\"机械遗迹\",\"type\":\"SPELL\",\"text\":\"\"},{\"id\":\"explicit_zero_fees\",\"name\":\"Explicit Zero Fees\",\"faction\":\"机械遗迹\",\"type\":\"SPELL\",\"commitCost\":0,\"uploadCost\":0,\"downloadCost\":0,\"text\":\"\"}]");
                    var catalog = CardCatalog.LoadDirectory(catalogRoot);

                    Assert.That(catalog.TryGetPresentationMetadata(
                        "missing_fees",
                        out var missingMetadata), Is.True);
                    Assert.That(catalog.TryGetPresentationMetadata(
                        "explicit_zero_fees",
                        out var explicitZeroMetadata), Is.True);
                    Assert.Multiple(() =>
                    {
                        Assert.That(missingMetadata!.HasCommitCost, Is.False);
                        Assert.That(missingMetadata.HasUploadCost, Is.False);
                        Assert.That(missingMetadata.HasDownloadCost, Is.False);
                        Assert.That(explicitZeroMetadata!.HasCommitCost, Is.True);
                        Assert.That(explicitZeroMetadata.HasUploadCost, Is.True);
                        Assert.That(explicitZeroMetadata.HasDownloadCost, Is.True);
                        Assert.That(explicitZeroMetadata.CommitCost, Is.EqualTo(0));
                        Assert.That(JsonSerializer.Serialize(catalog.Cards["explicit_zero_fees"]),
                            Does.Not.Contain("HasExplicit"));
                    });
                }
                finally
                {
                    Directory.Delete(catalogRoot, true);
                }
            }
            finally
            {
                File.Delete(file);
            }
        }

        private static void AssertManifestRejected(string json, string message)
        {
            WithTempRoot(root => AssertManifestRejected(root, json, message));
        }

        private static void AssertManifestRejected(string root, string json, string message)
        {
            var exception = Assert.Throws<InvalidDataException>(() => ContentCatalog.LoadJson(json, root));
            Assert.That(exception!.Message, Does.Contain(message));
        }

        private static void WithTempRoot(Action<string> action)
        {
            var root = Path.Combine(Path.GetTempPath(), "dw-content-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                action(root);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string Manifest(params string[] assets)
        {
            return Manifest(assets, "[]");
        }

        private static string Manifest(string[] assets, string aliases)
        {
            return "{\"manifestVersion\":1,\"schemaVersion\":\"1.0.0\",\"contentVersion\":\"1.0.0\",\"assets\":["
                + string.Join(",", assets) + "],\"aliases\":" + aliases + "}";
        }

        private static string Programmatic(string id, string? fallbackId = null, string status = "approved")
        {
            return "{\"id\":\"" + id + "\",\"kind\":\"card_art\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":"
                + (fallbackId is null ? "null" : "\"" + fallbackId + "\"")
                + ",\"status\":\"" + status + "\"}";
        }

        private static string FileAsset(string id, string path, string hash)
        {
            return "{\"id\":\"" + id + "\",\"kind\":\"card_art\",\"sourceType\":\"file\",\"relativePath\":\"" + path.Replace("\\", "\\\\")
                + "\",\"sha256\":\"" + hash + "\",\"fallbackId\":null,\"status\":\"approved\"}";
        }

        private static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
        }
    }
}
