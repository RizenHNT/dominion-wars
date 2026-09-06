using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DominionWars.Data;
using DominionWars.Data.CardEditor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{
    public sealed class CardEditorWorkflowIntegrationTests
    {
        [Test]
        public void ScanCloneValidateSaveAndReloadUsesTheSchemaAndContentRegistry()
        {
            WithTempDirectory(directory =>
            {
                var path = Path.Combine(directory, "workflow.json");
                File.WriteAllText(path, "[" + CardJson("workflow_card", "Before") + "]", new UTF8Encoding(false));

                var index = CardDocumentIndex.LoadDirectory(directory);
                Assert.That(index.TryGet("workflow_card", out var selected), Is.True);
                var edited = selected!.Clone();
                edited.SetString("name", "After");
                edited.SetInteger("punish", 2);

                var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);
                var validator = new CardEditorValidator(registry);
                var catalog = ContentCatalog.LoadFile(ContentManifestPath);
                var validation = validator.ValidateForProduction(edited, catalog, index);
                Assert.That(validation.IsProductionReady, Is.True,
                    string.Join("; ", validation.Issues.Select(issue => issue.Message)));

                var result = new CardDocumentStore().Save(edited);
                var reloaded = new CardDocumentStore().Load(path);

                Assert.Multiple(() =>
                {
                    Assert.That(result.OldHash, Is.Not.Null);
                    Assert.That(result.BackupPath, Is.Not.Null.And.EndsWith(".bak"));
                    Assert.That(reloaded.Id, Is.EqualTo("workflow_card"));
                    Assert.That(reloaded.Name, Is.EqualTo("After"));
                    Assert.That(reloaded.GetField("punish")!.Value<int>(), Is.EqualTo(2));
                    Assert.That(reloaded.BaselineHash, Is.EqualTo(result.NewHash));
                });
            });
        }

        [Test]
        public void ProductionValidationRejectsDraftLifecycleBeforeSave()
        {
            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);
            var validator = new CardEditorValidator(registry);
            var draft = CardDocument.LoadJson(CardJson("draft_workflow", "Draft", status: "draft"));

            var result = validator.ValidateForProduction(draft);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsValid, Is.False);
                Assert.That(result.Issues.Any(issue => issue.Code == "card.lifecycle.production"), Is.True);
            });
        }

        [Test]
        public void UnknownOrMalformedArtIdFailsClosedAgainstContentCatalog()
        {
            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);
            var validator = new CardEditorValidator(registry);
            var catalog = ContentCatalog.LoadFile(ContentManifestPath);

            var unknown = CardDocument.LoadJson(CardJson("unknown_art_workflow", "Unknown", artId: "missing_art"));
            var malformed = CardDocument.LoadJson(
                "{\"id\":\"malformed_art_workflow\",\"name\":\"Malformed\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"artId\":7,\"text\":\"\"}");

            var unknownResult = validator.ValidateForProduction(unknown, catalog);
            var malformedResult = validator.ValidateForProduction(malformed, catalog);

            Assert.Multiple(() =>
            {
                Assert.That(unknownResult.IsValid, Is.False);
                Assert.That(unknownResult.Issues.Any(issue => issue.Code == "card.art_id.unknown"), Is.True);
                Assert.That(malformedResult.IsValid, Is.False);
                Assert.That(malformedResult.Issues.Any(issue => issue.Code == "card.art_id.invalid"), Is.True);
            });
        }

        [Test]
        public void DuplicateIdIsRejectedByDirectoryScanAndEditorValidation()
        {
            WithTempDirectory(directory =>
            {
                File.WriteAllText(Path.Combine(directory, "a.json"), "[" + CardJson("same_workflow", "First") + "]", new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(directory, "b.json"), "[" + CardJson("same_workflow", "Second") + "]", new UTF8Encoding(false));

                var exception = Assert.Throws<InvalidDataException>(() => CardDocumentIndex.LoadDirectory(directory));

                Assert.That(exception!.Message, Does.Contain("duplicate card id"));
            });

            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);
            var validator = new CardEditorValidator(registry);
            var existing = new CardDocumentIndex(new[]
            {
                CardDocument.CreateNew("same_workflow", "Existing", "烈焰帝国", "SPELL")
            });
            var duplicate = CardDocument.CreateNew("same_workflow", "Duplicate", "烈焰帝国", "SPELL");

            var result = validator.Validate(duplicate, existingCards: existing);

            Assert.That(result.Issues.Any(issue => issue.Code == "card.id.duplicate"), Is.True);
        }

        [Test]
        public void ConcurrentModificationLeavesCurrentFileAndEarlierBackupRecoverable()
        {
            WithTempDirectory(directory =>
            {
                var path = Path.Combine(directory, "recoverable.json");
                var original = "[" + CardJson("recoverable_card", "Original") + "]";
                File.WriteAllText(path, original, new UTF8Encoding(false));
                var store = new CardDocumentStore();

                var firstEdit = store.Load(path);
                firstEdit.SetString("name", "First edit");
                var firstSave = store.Save(firstEdit);
                var backupBytes = File.ReadAllBytes(firstSave.BackupPath!);

                var secondEdit = store.Load(path);
                secondEdit.SetString("name", "Second edit");
                var external = "[" + CardJson("recoverable_card", "External edit") + "]";
                File.WriteAllText(path, external, new UTF8Encoding(false));
                Assert.Throws<CardDocumentConflictException>(() => store.Save(secondEdit));

                Assert.That(File.ReadAllText(path), Is.EqualTo(external));
                Assert.That(backupBytes, Is.EqualTo(Encoding.UTF8.GetBytes(original)));

                // The caller can explicitly restore the durable recovery copy;
                // the failed optimistic save never changed it or the target.
                File.Copy(firstSave.BackupPath!, path, true);
                var recovered = store.Load(path);
                Assert.That(recovered.Name, Is.EqualTo("Original"));
            });
        }

        [Test]
        public void RepeatedSaveIsByteDeterministicAndDoesNotSerializeEditorStatus()
        {
            WithTempDirectory(directory =>
            {
                var path = Path.Combine(directory, "repeat.json");
                var document = CardDocument.LoadJson(
                    CardJson("repeat_card", "Repeat", status: "approved"));
                var store = new CardDocumentStore();

                var first = store.Save(document, path);
                var firstBytes = File.ReadAllBytes(path);
                var second = store.Save(document, path);
                var secondBytes = File.ReadAllBytes(path);
                var savedJson = JToken.Parse(Encoding.UTF8.GetString(secondBytes));

                Assert.Multiple(() =>
                {
                    Assert.That(first.NewHash, Is.EqualTo(second.NewHash));
                    Assert.That(firstBytes, Is.EqualTo(secondBytes));
                    Assert.That(second.FieldChanges, Is.Empty);
                    Assert.That(second.FieldDiff, Is.EqualTo("no field changes"));
                    Assert.That(savedJson.SelectTokens("$..status"), Is.Empty,
                        "editor lifecycle must not become a gameplay card field");
                });
            });
        }

        private static string SchemaPath => Path.Combine(RepositoryRoot, "data", "schema", "cards.schema.json");
        private static string ContentManifestPath => Path.Combine(RepositoryRoot, "data", "content", "manifests", "content.manifest.json");

        private static string RepositoryRoot
        {
            get
            {
                var directory = TestContext.CurrentContext.TestDirectory;
                while (!string.IsNullOrEmpty(directory)
                    && !File.Exists(Path.Combine(directory, "data", "schema", "cards.schema.json")))
                {
                    directory = Directory.GetParent(directory)?.FullName;
                }

                return directory!;
            }
        }

        private static string CardJson(
            string id,
            string name,
            string? artId = "card_art_flame_leader",
            string? status = null)
        {
            var fields = new List<string>
            {
                "\"id\":\"" + id + "\"",
                "\"name\":\"" + name + "\"",
                "\"faction\":\"烈焰帝国\"",
                "\"type\":\"SPELL\"",
                "\"text\":\"\""
            };
            if (artId is not null) fields.Add("\"artId\":\"" + artId + "\"");
            if (status is not null) fields.Add("\"status\":\"" + status + "\"");
            return "{" + string.Join(",", fields) + "}";
        }

        private static void WithTempDirectory(Action<string> action)
        {
            var directory = Path.Combine(Path.GetTempPath(), "dw-card-workflow-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                action(directory);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
