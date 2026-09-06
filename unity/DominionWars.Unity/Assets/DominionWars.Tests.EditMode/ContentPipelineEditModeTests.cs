#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DominionWars.Unity.EditorTools;
using NUnit.Framework;
using UnityEditor.Build;

namespace DominionWars.Unity.EditMode
{
    [TestFixture]
    public sealed class ContentPipelineEditModeTests
    {
        [Test]
        public void ValidationReportOrderingIsDeterministic()
        {
            var root = CreateContentRoot();
            try
            {
                File.WriteAllText(Path.Combine(root, "authored", "unlisted.txt"), "inbox-like authored source");
                WriteManifest(root, "{\n  \"manifestVersion\": 1,\n  \"schemaVersion\": \"1.0.0\",\n  \"contentVersion\": \"1.0.0\",\n  \"assets\": [{\"id\":\"placeholder_card_art_neutral\",\"kind\":\"card_art\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"approved\"}],\n  \"aliases\": []\n}");

                var first = ContentPipelineValidator.Validate(root);
                var second = ContentPipelineValidator.Validate(root);
                var firstText = string.Join("\n", first.Diagnostics.Select(ToDiagnosticText));
                var secondText = string.Join("\n", second.Diagnostics.Select(ToDiagnosticText));
                Assert.That(firstText, Is.EqualTo(secondText));
                Assert.That(first.ReferencedFiles, Is.Empty);
                Assert.That(first.Diagnostics.Any(diagnostic => diagnostic.Code == "asset.orphan"), Is.True);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void DuplicateIdsAndPathsAreBlocking()
        {
            var root = CreateContentRoot();
            try
            {
                var assetPath = Path.Combine(root, "authored", "card_art", "one.bin");
                File.WriteAllText(assetPath, "same bytes");
                var hash = Hash(assetPath);
                WriteManifest(root, "{\n  \"manifestVersion\": 1,\n  \"schemaVersion\": \"1.0.0\",\n  \"contentVersion\": \"1.0.0\",\n  \"assets\": [\n    {\"id\":\"card_art_one\",\"kind\":\"card_art\",\"sourceType\":\"file\",\"relativePath\":\"authored/card_art/one.bin\",\"sha256\":\"" + hash + "\",\"fallbackId\":null,\"status\":\"approved\"},\n    {\"id\":\"card_art_one\",\"kind\":\"card_art\",\"sourceType\":\"file\",\"relativePath\":\"authored/card_art/one.bin\",\"sha256\":\"" + hash + "\",\"fallbackId\":null,\"status\":\"approved\"}\n  ],\n  \"aliases\": []\n}");

                var report = ContentPipelineValidator.Validate(root);
                Assert.That(report.BlockingCount, Is.GreaterThanOrEqualTo(2));
                Assert.That(report.Diagnostics.Any(diagnostic => diagnostic.Code == "manifest.asset.id.duplicate"), Is.True);
                Assert.That(report.Diagnostics.Any(diagnostic => diagnostic.Code == "manifest.asset.path.duplicate"), Is.True);
                Assert.That(report.CanBuild, Is.False);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void ProductionDraftIsBlocking()
        {
            var root = CreateContentRoot();
            try
            {
                WriteManifest(root, "{\"manifestVersion\":1,\"schemaVersion\":\"1\",\"contentVersion\":\"1\",\"assets\":[{\"id\":\"placeholder_card_art_neutral\",\"kind\":\"card_art\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"draft\"}],\"aliases\":[]}");
                var report = ContentPipelineValidator.Validate(root, production: true);
                Assert.That(report.Diagnostics.Any(diagnostic => diagnostic.Code == "manifest.asset.draft.production"), Is.True);
                Assert.That(report.CanBuild, Is.False);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void StagingCopiesOnlyManifestAndReferencedAuthoredFiles()
        {
            var root = CreateContentRoot();
            string? staging = null;
            try
            {
                var assetPath = Path.Combine(root, "authored", "card_art", "one.bin");
                File.WriteAllText(assetPath, "authored bytes");
                File.WriteAllText(Path.Combine(root, "inbox", "waiting.bin"), "do not stage");
                var hash = Hash(assetPath);
                WriteManifest(root, "{\"manifestVersion\":1,\"schemaVersion\":\"1\",\"contentVersion\":\"1\",\"assets\":[{\"id\":\"card_art_one\",\"kind\":\"card_art\",\"sourceType\":\"file\",\"relativePath\":\"authored/card_art/one.bin\",\"sha256\":\"" + hash + "\",\"fallbackId\":null,\"status\":\"approved\"}],\"aliases\":[]}");
                var report = ContentPipelineValidator.Validate(root);
                Assert.That(report.CanBuild, Is.True);
                staging = ContentPipelineStaging.CreateStagingDirectory(root, report);
                Assert.That(File.Exists(Path.Combine(staging, "manifests", "content.manifest.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(staging, "authored", "card_art", "one.bin")), Is.True);
                Assert.That(File.Exists(Path.Combine(staging, "inbox", "waiting.bin")), Is.False);
                Assert.That(ContentPipelineStaging.ValidateStagingDirectory(staging).CanBuild, Is.True);
                File.WriteAllText(Path.Combine(staging, "authored", "card_art", "one.bin"), "tampered staged bytes");
                Assert.That(ContentPipelineStaging.ValidateStagingDirectory(staging).CanBuild, Is.False);
            }
            finally
            {
                if (staging is not null) ContentPipelineStaging.TryDeleteDirectory(staging);
                DeleteDirectory(root);
            }
        }

        [Test]
        public void StagingCopiesValidatedSkinManifestsAlongsideContentManifest()
        {
            var root = CreateContentRoot();
            string? staging = null;
            try
            {
                WriteManifest(root,
                    "{\"manifestVersion\":1,\"schemaVersion\":\"1\",\"contentVersion\":\"1\",\"assets\":["
                    + ProgrammaticAsset("board_default", "board") + ","
                    + ProgrammaticAsset("board_test", "board") + ","
                    + ProgrammaticAsset("card_back_default", "card_back") + ","
                    + ProgrammaticAsset("card_back_test", "card_back") + ","
                    + ProgrammaticAsset("castle_default", "castle") + ","
                    + ProgrammaticAsset("castle_test", "castle") + ","
                    + ProgrammaticAsset("leader_frame_default", "leader") + ","
                    + ProgrammaticAsset("leader_frame_test", "leader") + ","
                    + ProgrammaticAsset("faction_frame_default", "faction_frame") + ","
                    + ProgrammaticAsset("faction_frame_test", "faction_frame") + ","
                    + ProgrammaticAsset("ui_icon_default", "ui_icon") + ","
                    + ProgrammaticAsset("ui_icon_test", "ui_icon")
                    + "],\"aliases\":[]}");
                var skinsDirectory = Path.Combine(root, "manifests", "skins");
                Directory.CreateDirectory(skinsDirectory);
                File.WriteAllText(
                    Path.Combine(skinsDirectory, "default.json"),
                    "{\"skinId\":\"default\",\"version\":\"1.0.0\",\"theme\":\"theme_default\",\"assets\":{\"board\":\"board_default\",\"cardBack\":\"card_back_default\",\"castle\":\"castle_default\",\"leaderFrame\":\"leader_frame_default\",\"factionFrame\":\"faction_frame_default\",\"uiIcon\":\"ui_icon_default\"}}",
                    Encoding.UTF8);
                File.WriteAllText(
                    Path.Combine(skinsDirectory, "test.json"),
                    "{\"skinId\":\"test\",\"version\":\"1.0.0\",\"theme\":\"theme_test\",\"assets\":{\"board\":\"board_test\",\"cardBack\":\"card_back_test\",\"castle\":\"castle_test\",\"leaderFrame\":\"leader_frame_test\",\"factionFrame\":\"faction_frame_test\",\"uiIcon\":\"ui_icon_test\"}}",
                    Encoding.UTF8);

                var report = ContentPipelineValidator.Validate(root);
                Assert.That(report.CanBuild, Is.True);
                Assert.That(report.ReferencedFiles, Does.Contain("manifests/skins/default.json"));
                Assert.That(report.ReferencedFiles, Does.Contain("manifests/skins/test.json"));
                staging = ContentPipelineStaging.CreateStagingDirectory(root, report);

                Assert.That(File.Exists(Path.Combine(staging, "manifests", "content.manifest.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(staging, "manifests", "skins", "default.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(staging, "manifests", "skins", "test.json")), Is.True);
                Assert.That(ContentPipelineStaging.ValidateStagingDirectory(staging).CanBuild, Is.True);
            }
            finally
            {
                if (staging is not null) ContentPipelineStaging.TryDeleteDirectory(staging);
                DeleteDirectory(root);
            }
        }

        [Test]
        public void UnsafeDestinationAndFailedValidationPreserveExistingContent()
        {
            var root = CreateContentRoot();
            var destination = Path.Combine(root, "existing-generated-content");
            try
            {
                Directory.CreateDirectory(destination);
                var sentinel = Path.Combine(destination, "sentinel.txt");
                File.WriteAllText(sentinel, "preserve");
                Assert.That(ContentPipelineStaging.IsSafeDestination(destination, Path.Combine(root, "other-streaming-assets")), Is.False);

                WriteManifest(root, "{\"manifestVersion\":1,\"schemaVersion\":\"1\",\"contentVersion\":\"1\",\"assets\":[{\"id\":\"Bad\",\"kind\":\"card_art\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"approved\"}],\"aliases\":[]}");
                var report = ContentPipelineValidator.Validate(root);
                Assert.That(report.CanBuild, Is.False);
                Assert.Throws<BuildFailedException>(() => ContentPipelineStaging.CreateStagingDirectory(root, report));
                Assert.That(File.ReadAllText(sentinel), Is.EqualTo("preserve"));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void UnityGeneratedMetaForKnownEntriesRemainsOwned()
        {
            var root = CreateGeneratedContentShape();
            try
            {
                File.WriteAllText(Path.Combine(root, "manifests.meta"), "meta");
                File.WriteAllText(Path.Combine(root, "authored.meta"), "meta");
                File.WriteAllText(Path.Combine(root, ".content-generated.meta"), "meta");
                File.WriteAllText(Path.Combine(root, ".gitignore.meta"), "meta");
                File.WriteAllText(Path.Combine(root, "manifests", "content.manifest.json.meta"), "meta");
                File.WriteAllText(Path.Combine(root, "authored", "card_art.meta"), "meta");
                File.WriteAllText(Path.Combine(root, "authored", "card_art", "one.bin.meta"), "meta");

                Assert.That(ContentPipelineStaging.IsOwnedGeneratedRoot(root), Is.True);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void GeneratedContentShapeRejectsUnexpectedFilesAndMeta()
        {
            var root = CreateGeneratedContentShape();
            try
            {
                File.WriteAllText(Path.Combine(root, "manifests", "unexpected.json"), "{}");
                Assert.That(ContentPipelineStaging.IsOwnedGeneratedRoot(root), Is.False);
                File.Delete(Path.Combine(root, "manifests", "unexpected.json"));

                File.WriteAllText(Path.Combine(root, "authored", "card_art", "unexpected.bin.meta"), "meta");
                Assert.That(ContentPipelineStaging.IsOwnedGeneratedRoot(root), Is.False);
                File.Delete(Path.Combine(root, "authored", "card_art", "unexpected.bin.meta"));

                File.WriteAllText(Path.Combine(root, "unexpected.meta"), "meta");
                Assert.That(ContentPipelineStaging.IsOwnedGeneratedRoot(root), Is.False);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void FailedGeneratedContentExchangeRestoresPreviousRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "dw-content-exchange-" + Guid.NewGuid().ToString("N"));
            var destination = Path.Combine(root, "content");
            var staging = Path.Combine(root, "stage");
            var backup = Path.Combine(root, "backup");
            Directory.CreateDirectory(destination);
            Directory.CreateDirectory(staging);
            var sentinel = Path.Combine(destination, "previous.txt");
            File.WriteAllText(sentinel, "previous content");
            File.WriteAllText(Path.Combine(staging, "new.txt"), "new content");
            try
            {
                var failInstall = true;
                var success = ContentPipelineStaging.TryExchangeGeneratedRoots(
                    staging,
                    destination,
                    backup,
                    (source, target) =>
                    {
                        if (failInstall && string.Equals(source, staging, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(target, destination, StringComparison.OrdinalIgnoreCase))
                        {
                            failInstall = false;
                            return "simulated install failure";
                        }
                        Directory.Move(source, target);
                        return string.Empty;
                    },
                    out var error);

                Assert.That(success, Is.False);
                Assert.That(error, Does.Contain("install"));
                Assert.That(File.ReadAllText(sentinel), Is.EqualTo("previous content"));
                Assert.That(Directory.Exists(destination), Is.True);
                Assert.That(Directory.Exists(backup), Is.False);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        private static string CreateContentRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "dw-content-editor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "manifests"));
            Directory.CreateDirectory(Path.Combine(root, "authored", "card_art"));
            Directory.CreateDirectory(Path.Combine(root, "inbox"));
            return root;
        }

        private static void WriteManifest(string root, string json)
        {
            File.WriteAllText(Path.Combine(root, "manifests", "content.manifest.json"), json, new UTF8Encoding(false));
        }

        private static string CreateGeneratedContentShape()
        {
            var root = Path.Combine(Path.GetTempPath(), "dw-generated-content-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "manifests"));
            Directory.CreateDirectory(Path.Combine(root, "authored", "card_art"));
            var assetPath = Path.Combine(root, "authored", "card_art", "one.bin");
            File.WriteAllText(assetPath, "generated authored bytes");
            WriteManifest(root, "{\"manifestVersion\":1,\"schemaVersion\":\"1\",\"contentVersion\":\"1\",\"assets\":[{\"id\":\"card_art_one\",\"kind\":\"card_art\",\"sourceType\":\"file\",\"relativePath\":\"authored/card_art/one.bin\",\"sha256\":\"" + Hash(assetPath) + "\",\"fallbackId\":null,\"status\":\"approved\"}],\"aliases\":[]}");
            File.WriteAllText(Path.Combine(root, ContentPipelineStaging.GeneratedMarkerName), ContentPipelineStaging.GeneratedMarkerContents, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, ".gitignore"), ContentPipelineStaging.GeneratedIgnoreContents, new UTF8Encoding(false));
            return root;
        }

        private static string ProgrammaticAsset(string id, string kind)
        {
            return "{\"id\":\"" + id + "\",\"kind\":\"" + kind
                + "\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"approved\"}";
        }

        private static string Hash(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
        }

        private static string ToDiagnosticText(ContentDiagnostic diagnostic)
        {
            return diagnostic.Severity + "|" + diagnostic.Code + "|" + diagnostic.Path + "|" + diagnostic.Message;
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }
}
#endif
