using System;
using System.IO;
using System.Linq;
using System.Text;
using DominionWars.Data;
using DominionWars.Data.CardEditor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{
    public sealed class CardEffectSpecEditorTests
    {
        [Test]
        public void EffectSpecFieldMetadataComesFromSchema()
        {
            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);

            Assert.Multiple(() =>
            {
                Assert.That(registry.TryGetDefinitionField("EffectSpec", "action", out var action), Is.True);
                Assert.That(action!.DefinitionName, Is.EqualTo("EffectAction"));
                Assert.That(action.AllowedValues, Does.Contain("DAMAGE"));
                Assert.That(registry.TryGetDefinitionField("EffectSpec", "target", out var target), Is.True);
                Assert.That(target!.DefinitionName, Is.EqualTo("EffectTarget"));
                Assert.That(registry.TryGetDefinitionField("EffectSpec", "amount", out var amount), Is.True);
                Assert.That(amount!.Minimum, Is.EqualTo(-99));
                Assert.That(amount.Maximum, Is.EqualTo(99));
                Assert.That(registry.TryGetDefinitionField("EffectSpec", "kingSlayer", out var kingSlayer), Is.True);
                Assert.That(kingSlayer!.ValueKind, Is.EqualTo(CardFieldValueKind.Boolean));
            });
        }

        [Test]
        public void NewEffectAcceptsOnlySchemaActionAndTargetValues()
        {
            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);
            var effect = CardEffectSpecEditor.CreateNew(registry);

            Assert.That(effect.IsSupported(registry, out var reason), Is.True, reason);
            Assert.That(effect.TrySetString(registry, "target", "ENEMY_FACE", out reason), Is.True, reason);
            Assert.That(effect.TrySetInteger(registry, "amount", 4, out reason), Is.True, reason);
            Assert.That(effect.TrySetString(registry, "param", "both", out reason), Is.True, reason);
            Assert.That(effect.TrySetString(registry, "condition", "ALWAYS", out reason), Is.True, reason);
            Assert.That(effect.TrySetBoolean(registry, "kingSlayer", true, out reason), Is.True, reason);

            Assert.Multiple(() =>
            {
                Assert.That(effect.Action, Is.EqualTo(registry.EffectActions[0]));
                Assert.That(effect.Target, Is.EqualTo("ENEMY_FACE"));
                Assert.That(effect.Amount, Is.EqualTo(4));
                Assert.That(effect.Param, Is.EqualTo("both"));
                Assert.That(effect.Condition, Is.EqualTo("ALWAYS"));
                Assert.That(effect.KingSlayer, Is.True);
                Assert.That(effect.IsSupported(registry, out var supportedReason), Is.True, supportedReason);
            });

            Assert.That(effect.TrySetString(registry, "action", "NOT_A_SCHEMA_ACTION", out _), Is.False);
            Assert.That(effect.TrySetString(registry, "target", "NOT_A_SCHEMA_TARGET", out _), Is.False);
        }

        [Test]
        public void UnknownPropertiesAndUnsupportedEntriesRoundTripWithoutLoss()
        {
            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);
            var raw = JToken.Parse(
                "{\"action\":\"DAMAGE\",\"target\":\"ENEMY_FACE\",\"futureField\":{\"nested\":true}}" );
            var effect = CardEffectSpecEditor.FromJsonToken(raw);

            Assert.Multiple(() =>
            {
                Assert.That(effect.GetUnsupportedProperties(registry), Is.EqualTo(new[] { "futureField" }));
                Assert.That(effect.IsSupported(registry, out var reason), Is.False);
                Assert.That(reason, Does.Contain("futureField"));
            });

            Assert.That(effect.TrySetInteger(registry, "amount", 2, out _), Is.True);
            var updated = effect.ToJsonToken() as JObject;
            Assert.That(updated!["futureField"]!["nested"]!.Value<bool>(), Is.True);
            Assert.That(updated["amount"]!.Value<int>(), Is.EqualTo(2));

            var scalar = CardEffectSpecEditor.FromJsonToken(new JValue("future-effect"));
            Assert.That(scalar.IsSupported(registry, out _), Is.False);
            Assert.That(scalar.ToJsonToken().ToString(Formatting.None), Is.EqualTo("\"future-effect\""));
        }

        [Test]
        public void AmbushUnknownPropertiesAndUnsupportedEntriesRoundTripWithoutLoss()
        {
            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);
            var document = CardDocument.LoadJson(
                "{\"id\":\"ambush_roundtrip\",\"name\":\"Ambush Roundtrip\",\"faction\":\"烈焰帝国\",\"type\":\"AMBUSH\",\"text\":\"\",\"ambushEffects\":[{\"action\":\"DAMAGE\",\"target\":\"ENEMY_MINION\",\"amount\":2,\"futureField\":{\"nested\":true}},\"future-effect\",{\"action\":\"HEAL\",\"target\":\"FRIENDLY_MINION\",\"amount\":1}]}" );

            var effects = CardEffectSpecEditor.Read(document, CardEffectSpecEditor.AmbushEffectsField).ToList();
            Assert.That(effects, Has.Count.EqualTo(3));
            Assert.Multiple(() =>
            {
                Assert.That(effects[0].GetUnsupportedProperties(registry), Is.EqualTo(new[] { "futureField" }));
                Assert.That(effects[0].IsSupported(registry, out _), Is.False);
                Assert.That(effects[1].IsObject, Is.False);
                Assert.That(effects[1].IsSupported(registry, out _), Is.False);
                Assert.That(effects[2].Action, Is.EqualTo("HEAL"));
                Assert.That(effects[2].Target, Is.EqualTo("FRIENDLY_MINION"));
            });

            Assert.That(effects[0].TrySetInteger(registry, CardEffectSpecEditor.AmountField, 7, out _), Is.True);
            CardEffectSpecEditor.SetArray(document, effects, CardEffectSpecEditor.AmbushEffectsField);

            var reordered = CardEffectSpecEditor.Reorder(
                (JArray)document.GetField(CardEffectSpecEditor.AmbushEffectsField)!,
                0,
                1);
            document.SetField(CardEffectSpecEditor.AmbushEffectsField, reordered);

            var expected = (JArray)document.GetField(CardEffectSpecEditor.AmbushEffectsField)!;
            var reloaded = CardDocument.LoadJson(document.ToCardFileJson());
            var actual = (JArray)reloaded.GetField(CardEffectSpecEditor.AmbushEffectsField)!;
            Assert.That(JToken.DeepEquals(actual, expected), Is.True,
                "Ambush unknown properties and unsupported entries must survive edit, reorder, and JSON round-trip.");
            Assert.That(actual[0]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(actual[0]!.Value<string>(), Is.EqualTo("future-effect"));
            Assert.That(actual[1]!["futureField"]!["nested"]!.Value<bool>(), Is.True);
            Assert.That(actual[1]!["amount"]!.Value<int>(), Is.EqualTo(7));
        }

        [Test]
        public void PunishUnknownPropertiesAndUnsupportedEntriesRoundTripWithoutLoss()
        {
            var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);
            var document = CardDocument.LoadJson(
                "{\"id\":\"punish_roundtrip\",\"name\":\"Punish Roundtrip\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"text\":\"\",\"punish\":5,\"punishEffects\":[{\"action\":\"DAMAGE\",\"target\":\"ENEMY_FACE\",\"amount\":2,\"futureField\":{\"nested\":true}},\"future-punish\",{\"action\":\"DRAW\",\"amount\":1}]}" );

            var effects = CardEffectSpecEditor.Read(document, CardEffectSpecEditor.PunishEffectsField).ToList();
            Assert.That(effects, Has.Count.EqualTo(3));
            Assert.Multiple(() =>
            {
                Assert.That(effects[0].GetUnsupportedProperties(registry), Is.EqualTo(new[] { "futureField" }));
                Assert.That(effects[0].IsSupported(registry, out _), Is.False);
                Assert.That(effects[1].IsObject, Is.False);
                Assert.That(effects[1].IsSupported(registry, out _), Is.False);
                Assert.That(effects[2].Action, Is.EqualTo("DRAW"));
            });

            Assert.That(effects[0].TrySetInteger(registry, CardEffectSpecEditor.AmountField, 7, out _), Is.True);
            CardEffectSpecEditor.SetArray(document, effects, CardEffectSpecEditor.PunishEffectsField);
            var reordered = CardEffectSpecEditor.Reorder(
                (JArray)document.GetField(CardEffectSpecEditor.PunishEffectsField)!, 0, 1);
            document.SetField(CardEffectSpecEditor.PunishEffectsField, reordered);

            var expected = (JArray)document.GetField(CardEffectSpecEditor.PunishEffectsField)!;
            var reloaded = CardDocument.LoadJson(document.ToCardFileJson());
            var actual = (JArray)reloaded.GetField(CardEffectSpecEditor.PunishEffectsField)!;
            Assert.Multiple(() =>
            {
                Assert.That(JToken.DeepEquals(actual, expected), Is.True,
                    "Punish unknown properties and unsupported entries must survive edit, reorder, and JSON round-trip.");
                Assert.That(actual[0]!.Type, Is.EqualTo(JTokenType.String));
                Assert.That(actual[0]!.Value<string>(), Is.EqualTo("future-punish"));
                Assert.That(actual[1]!["futureField"]!["nested"]!.Value<bool>(), Is.True);
                Assert.That(actual[1]!["amount"]!.Value<int>(), Is.EqualTo(7));
                Assert.That(reloaded.GetField("punish")!.Value<int>(), Is.EqualTo(5),
                    "Editing punishEffects must not change printed punish.");
            });
        }

        [Test]
        public void EffectArrayOperationsPreserveOrderAndSaveRoundTrip()
        {
            WithTempDirectory(directory =>
            {
                var registry = CardEditorMetadataRegistry.LoadFile(SchemaPath);
                var document = CardDocument.LoadJson(
                    "{\"id\":\"effect_editor_card\",\"name\":\"Effects\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"text\":\"\"}");
                var first = CardEffectSpecEditor.CreateNew(registry);
                first.TrySetString(registry, "target", "ENEMY_FACE", out _);
                var second = CardEffectSpecEditor.CreateNew(registry);
                second.TrySetInteger(registry, "amount", 3, out _);
                CardEffectSpecEditor.SetArray(document, new[] { first, second });

                var reversed = CardEffectSpecEditor.Reorder(
                    (JArray)document.GetField(CardEffectSpecEditor.OnPlayEffectsField)!, 0, 1);
                document.SetField(CardEffectSpecEditor.OnPlayEffectsField, reversed);
                var path = Path.Combine(directory, "effect_editor_card.json");
                var saved = new CardDocumentStore().Save(document, path);
                var reloaded = CardDocument.LoadJson(File.ReadAllText(path, Encoding.UTF8), path, saved.NewHash);
                var effects = CardEffectSpecEditor.Read(reloaded);

                Assert.Multiple(() =>
                {
                    Assert.That(effects, Has.Count.EqualTo(2));
                    Assert.That(effects[0].Amount, Is.EqualTo(3));
                    Assert.That(effects[1].Target, Is.EqualTo("ENEMY_FACE"));
                    Assert.That(saved.FieldChanges.Any(change => change.Field == "onPlayEffects"), Is.True);
                });
            });
        }

        private static string SchemaPath => Path.Combine(RepositoryRoot, "data", "schema", "cards.schema.json");

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

        private static void WithTempDirectory(Action<string> action)
        {
            var directory = Path.Combine(Path.GetTempPath(), "dw-card-effect-editor-" + Guid.NewGuid().ToString("N"));
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
