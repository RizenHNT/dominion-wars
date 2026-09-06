using System;
using System.Collections.Generic;
using System.IO;
using DominionWars.Data;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{
    [TestFixture]
    public sealed class ContentSkinCatalogTests
    {
        private static string RepositoryRoot
        {
            get
            {
                var directory = TestContext.CurrentContext.TestDirectory;
                while (!string.IsNullOrEmpty(directory)
                    && !Directory.Exists(Path.Combine(directory, "data", "content")))
                {
                    directory = Directory.GetParent(directory)?.FullName;
                }

                return directory!;
            }
        }

        [Test]
        public void ProductionDefaultAndTestSkinsExposeStableRoleOverrides()
        {
            var root = RepositoryRoot;
            var contentRoot = Path.Combine(root, "data", "content");
            var manifest = Path.Combine(contentRoot, "manifests", "content.manifest.json");
            var catalog = ContentCatalog.LoadFile(manifest, contentRoot);
            var skins = ContentSkinCatalog.LoadDirectory(
                Path.Combine(contentRoot, "manifests", "skins"),
                catalog);

            Assert.Multiple(() =>
            {
                Assert.That(skins.Skins.Keys, Is.EqualTo(new[] { "default", "test" }));
                Assert.That(skins.ResolveAssetId("default", "board"), Is.EqualTo("board_default"));
                Assert.That(skins.ResolveAssetId("default", "cardBack"), Is.EqualTo("card_back_default"));
                Assert.That(skins.ResolveAssetId("default", "castle"), Is.EqualTo("castle_default"));
                Assert.That(skins.ResolveAssetId("default", "leaderFrame"), Is.EqualTo("leader_frame_default"));
                Assert.That(skins.ResolveAssetId("default", "factionFrame"), Is.EqualTo("faction_frame_default"));
                Assert.That(skins.ResolveAssetId("default", "uiIcon"), Is.EqualTo("ui_icon_default"));
                Assert.That(skins.ResolveAssetId("test", "board"), Is.EqualTo("board_test"));
                Assert.That(skins.ResolveAssetId("test", "cardBack"), Is.EqualTo("card_back_test"));
                Assert.That(skins.ResolveAssetId("test", "castle"), Is.EqualTo("castle_test"));
                Assert.That(skins.ResolveAssetId("test", "leaderFrame"), Is.EqualTo("leader_frame_test"));
                Assert.That(skins.ResolveAssetId("test", "factionFrame"), Is.EqualTo("faction_frame_test"));
                Assert.That(skins.ResolveAssetId("test", "uiIcon"), Is.EqualTo("ui_icon_test"));
                Assert.That(
                    skins.ResolveCardArtwork("test", "flame_leader", "card_art_flame_leader"),
                    Is.EqualTo("card_art_machine_alpha"));
                Assert.That(
                    skins.ResolveCardArtwork("default", "flame_leader", "card_art_flame_leader"),
                    Is.EqualTo("card_art_flame_leader"));
            });
        }

        [Test]
        public void MissingOptionalMapsRemainSafeAndUnknownRolePreservesForwardCompatibility()
        {
            var root = Path.Combine(Path.GetTempPath(), "dw-content-skin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var catalog = ContentCatalog.LoadJson(
                    "{\"manifestVersion\":1,\"schemaVersion\":\"1.0.0\",\"contentVersion\":\"1.0.0\",\"assets\":["
                    + Asset("board_default", "board")
                    + "," + Asset("card_art_default", "card_art")
                    + "],\"aliases\":[]}",
                    root);

                var skin = ContentSkinCatalog.LoadJson(
                    "{\"skinId\":\"minimal\",\"version\":\"1.0.0\",\"theme\":\"theme_minimal\",\"assets\":{\"board\":\"board_default\",\"futureRole\":\"board_default\"}}",
                    catalog);

                Assert.That(skin.AudioCues, Is.Empty);
                Assert.That(skin.VfxCues, Is.Empty);
                Assert.That(skin.CardArtwork, Is.Empty);
                Assert.That(skin.Assets["futureRole"], Is.EqualTo("board_default"));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void RoleOverrideCannotPointAtAnUnrelatedAssetKind()
        {
            var root = Path.Combine(Path.GetTempPath(), "dw-content-skin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var catalog = ContentCatalog.LoadJson(
                    "{\"manifestVersion\":1,\"schemaVersion\":\"1.0.0\",\"contentVersion\":\"1.0.0\",\"assets\":["
                    + Asset("card_art_default", "card_art")
                    + "],\"aliases\":[]}",
                    root);

                var exception = Assert.Throws<InvalidDataException>(() => ContentSkinCatalog.LoadJson(
                    "{\"skinId\":\"invalid\",\"version\":\"1.0.0\",\"theme\":\"theme_invalid\",\"assets\":{\"board\":\"card_art_default\"}}",
                    catalog));

                Assert.That(exception!.Message, Does.Contain("expected board"));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        private static string Asset(string id, string kind)
        {
            return "{\"id\":\"" + id + "\",\"kind\":\"" + kind
                + "\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"approved\"}";
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }
}
