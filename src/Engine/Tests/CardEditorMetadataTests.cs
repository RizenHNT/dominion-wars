using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Data;
using DominionWars.Data.CardEditor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{
    public sealed class CardEditorMetadataTests
    {
        [Test]
        public void RegistryReadsTopLevelFieldMetadataFromSchema()
        {
            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);

            Assert.Multiple(() =>
            {
                Assert.That(registry.TryGetField("id", out var id), Is.True);
                Assert.That(id!.ValueKind, Is.EqualTo(CardFieldValueKind.String));
                Assert.That(id.Required, Is.True);
                Assert.That(id.Pattern, Is.EqualTo("^[a-z][a-z0-9_]*$"));

                Assert.That(registry.TryGetField("commitCost", out var commitCost), Is.True);
                Assert.That(commitCost!.ValueKind, Is.EqualTo(CardFieldValueKind.Integer));
                Assert.That(commitCost.Minimum, Is.EqualTo(0));
                Assert.That(commitCost.Maximum, Is.EqualTo(99));

                Assert.That(registry.TryGetField("punish", out var punish), Is.True);
                Assert.That(punish!.DefaultValue!.ToObject<int>(), Is.EqualTo(0));
                Assert.That(registry.TryGetField("leaderDef", out var leaderDef), Is.True);
                Assert.That(leaderDef!.ValueKind, Is.EqualTo(CardFieldValueKind.Object));
                Assert.That(registry.TryGetField("keywords", out var keywords), Is.True);
                Assert.That(keywords!.ValueKind, Is.EqualTo(CardFieldValueKind.Array));
                Assert.That(keywords.ItemAllowedValues, Does.Contain("震慑"));
                Assert.That(registry.CardFields.Select(field => field.Key),
                    Is.EqualTo(registry.CardFields.Select(field => field.Key).OrderBy(key => key, StringComparer.Ordinal)));
            });
        }

        [Test]
        public void RegistryExposesSchemaEnumsWithoutAnEditorOwnedList()
        {
            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);

            Assert.Multiple(() =>
            {
                Assert.That(registry.EffectActions, Does.Contain("DAMAGE"));
                Assert.That(registry.EffectActions, Does.Contain("COMMIT"));
                Assert.That(registry.EffectTargets, Does.Contain("ENEMY_FACE"));
                Assert.That(registry.Keywords, Does.Contain("潜行"));
                Assert.That(registry.WinConditions, Does.Contain("PULL_TOTAL_GE"));
                Assert.That(registry.Factions, Does.Contain("机械遗迹"));
                Assert.That(registry.CardTypes, Is.EquivalentTo(new[] { "MINION", "SPELL", "AMBUSH", "PUNISH" }));
                Assert.That(registry.GetDefinitionFields("LeaderDef"), Does.Contain("winCondition"));
                Assert.That(registry.IsKnownDefinitionField("EffectSpec", "action"), Is.True);
            });
        }

        [Test]
        public void RegistryReadsConditionalRequirementsFromSchema()
        {
            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);

            var minion = registry.ConditionalRequirements.Single(requirement =>
                requirement.ConditionField == "type" && requirement.ConditionValue == "MINION");
            Assert.That(minion.RequiredFields, Is.EquivalentTo(new[] { "attack", "health" }));
        }

        [Test]
        public void MissingLifecycleDefaultsToApprovedAndCanonicalJsonOmitsEditorStatus()
        {
            var document = CardDocument.LoadJson(CardJson("legacy_card", "Legacy"));

            Assert.Multiple(() =>
            {
                Assert.That(document.Lifecycle, Is.EqualTo(CardLifecycle.Approved));
                Assert.That(document.HasSerializedLifecycle, Is.False);
                Assert.That(document.SerializedLifecycleIsValid, Is.True);
                Assert.That(document.HasField("status"), Is.False);
                Assert.That(document.ToEditorJsonObject()["status"]!.ToObject<string>(), Is.EqualTo("approved"));
            });
        }

        [Test]
        public void ValidatorDelegatesCardSemanticsAndReportsBasicDocumentErrors()
        {
            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);
            var validator = new CardEditorValidator(registry);

            var valid = CardDocument.LoadJson(CardJson("valid_card", "Valid"));
            var validResult = validator.Validate(valid);
            Assert.That(validResult.IsValid, Is.True);

            var unknown = CardDocument.LoadJson(CardJson("unknown_card", "Unknown")
                .Replace("\"text\":\"\"", "\"text\":\"\",\"futureField\":true", StringComparison.Ordinal));
            var unknownResult = validator.Validate(unknown);
            Assert.That(unknownResult.Issues.Any(issue => issue.Code == "card.field.unknown"), Is.True);

            var invalidMinion = CardDocument.LoadJson(
                "{\"id\":\"invalid_minion\",\"name\":\"Minion\",\"faction\":\"烈焰帝国\",\"type\":\"MINION\",\"text\":\"\"}");
            var invalidResult = validator.Validate(invalidMinion);
            Assert.That(invalidResult.Issues.Any(issue => issue.Code == "card.catalog.invalid"), Is.True);
        }

        [Test]
        public void ValidatorChecksPunishEffectsAgainstEffectSpecMetadata()
        {
            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);
            var validator = new CardEditorValidator(registry);
            var document = CardDocument.LoadJson(
                "{\"id\":\"invalid_punish_effect\",\"name\":\"Invalid punish effect\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"text\":\"\",\"punish\":2,\"punishEffects\":[{\"action\":\"DAMAGE\",\"futureField\":true}]}" );

            var result = validator.Validate(document);

            Assert.That(result.Issues.Any(issue =>
                issue.Code == "card.effect.unsupported"
                && issue.Path == "/punishEffects/0"), Is.True,
                "punishEffects entries must be checked by the same schema-backed validator as onPlayEffects and ambushEffects.");
        }

        [Test]
        public void ValidatorChecksLifecycleAndDuplicateIdWithoutChangingGameplayRules()
        {
            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);
            var validator = new CardEditorValidator(registry);
            var draft = CardDocument.LoadJson(CardJson("draft_card", "Draft", status: "draft"));
            var draftResult = validator.Validate(draft);
            var productionResult = validator.ValidateForProduction(draft);

            var existing = new CardDocumentIndex(new[] { CardDocument.CreateNew("existing_card", "Existing", "烈焰帝国", "SPELL") });
            var duplicate = CardDocument.CreateNew("existing_card", "Duplicate", "烈焰帝国", "SPELL");
            var duplicateResult = validator.Validate(duplicate, existingCards: existing);

            Assert.Multiple(() =>
            {
                Assert.That(draftResult.IsValid, Is.True);
                Assert.That(productionResult.Issues.Any(issue => issue.Code == "card.lifecycle.production"), Is.True);
                Assert.That(duplicateResult.Issues.Any(issue => issue.Code == "card.id.duplicate"), Is.True);
            });
        }

        [Test]
        public void ValidatorChecksArtIdAgainstContentCatalogSemanticKindAndAliases()
        {
            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);
            var validator = new CardEditorValidator(registry);
            var contentCatalog = ContentCatalog.LoadFile(ContentManifestPath);

            var aliasDocument = CardDocument.LoadJson(CardJson("alias_card", "Alias", artId: "flame_leader"));
            var aliasResult = validator.ValidateForProduction(aliasDocument, contentCatalog);
            Assert.That(aliasResult.IsValid, Is.True);

            var unknownDocument = CardDocument.LoadJson(CardJson("unknown_art", "Unknown", artId: "missing_art"));
            var unknownResult = validator.Validate(unknownDocument, contentCatalog);
            Assert.That(unknownResult.Issues.Any(issue => issue.Code == "card.art_id.unknown"), Is.True);

            var wrongKindCatalog = ContentCatalog.LoadJson(
                "{\"manifestVersion\":1,\"schemaVersion\":\"1\",\"contentVersion\":\"1\",\"assets\":[{\"id\":\"board_default\",\"kind\":\"board\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"approved\"}],\"aliases\":[]}",
                Path.GetTempPath());
            var wrongKindDocument = CardDocument.LoadJson(CardJson("wrong_kind", "Wrong Kind", artId: "board_default"));
            var wrongKindResult = validator.Validate(wrongKindDocument, wrongKindCatalog);
            Assert.That(wrongKindResult.Issues.Any(issue => issue.Code == "card.art_id.kind"), Is.True);
        }

        [Test]
        public void ExplicitUnknownLifecycleStatusFailsClosedButMissingStatusRemainsCompatible()
        {
            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);
            var validator = new CardEditorValidator(registry);
            var document = CardDocument.LoadJson(CardJson("bad_status", "Bad Status", status: "in_review"));

            Assert.That(document.Lifecycle, Is.EqualTo(CardLifecycle.Approved));
            Assert.That(document.SerializedLifecycleIsValid, Is.False);
            Assert.That(validator.Validate(document).Issues.Any(issue => issue.Code == "card.lifecycle.invalid"), Is.True);
        }

        private static string SchemaPath => Path.Combine(RepositoryRoot, "data", "schema", "cards.schema.json");
        private static string ContentManifestPath => Path.Combine(RepositoryRoot, "data", "content", "manifests", "content.manifest.json");

        private static string RepositoryRoot
        {
            get
            {
                var directory = TestContext.CurrentContext.TestDirectory;
                while (!string.IsNullOrEmpty(directory) && !File.Exists(Path.Combine(directory, "data", "schema", "cards.schema.json")))
                {
                    directory = Directory.GetParent(directory)?.FullName;
                }

                return directory!;
            }
        }

        private static string CardJson(string id, string name, string faction = "烈焰帝国", string type = "SPELL", string? artId = null, string? status = null)
        {
            var fields = new List<string>
            {
                "\"id\":\"" + id + "\"",
                "\"name\":\"" + name + "\"",
                "\"faction\":\"" + faction + "\"",
                "\"type\":\"" + type + "\"",
                "\"text\":\"\""
            };
            if (artId is not null) fields.Add("\"artId\":\"" + artId + "\"");
            if (status is not null) fields.Add("\"status\":\"" + status + "\"");
            return "{" + string.Join(",", fields) + "}";
        }
    }
}
