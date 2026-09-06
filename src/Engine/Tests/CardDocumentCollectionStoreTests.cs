using System;
using System.IO;
using System.Linq;
using System.Text;
using DominionWars.Data.CardEditor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{
    public sealed class CardDocumentCollectionStoreTests
    {
        [Test]
        public void SaveCollectionReplacesOnlySelectedCardAndPreservesAllEntries()
        {
            WithTempDirectory(directory =>
            {
                var path = Path.Combine(directory, "faction.json");
                var original =
                    "[{\"id\":\"first_card\",\"name\":\"First\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"text\":\"\"}," +
                    "{\"id\":\"second_card\",\"name\":\"Second\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"text\":\"\"}]";
                File.WriteAllText(path, original, new UTF8Encoding(false));

                var documents = CardDocument.LoadCollectionFile(path);
                var selected = documents.Single(document => document.Id == "second_card");
                selected.SetString("name", "Second edited");

                var result = new CardDocumentStore().SaveCollection(selected, path);
                var root = JToken.Parse(File.ReadAllText(path));

                Assert.Multiple(() =>
                {
                    Assert.That(root, Is.TypeOf<JArray>());
                    Assert.That(root.Children().Count(), Is.EqualTo(2));
                    Assert.That(root[0]!["id"]!.ToObject<string>(), Is.EqualTo("first_card"));
                    Assert.That(root[0]!["name"]!.ToObject<string>(), Is.EqualTo("First"));
                    Assert.That(root[1]!["id"]!.ToObject<string>(), Is.EqualTo("second_card"));
                    Assert.That(root[1]!["name"]!.ToObject<string>(), Is.EqualTo("Second edited"));
                    Assert.That(result.BackupPath, Is.Not.Null);
                    Assert.That(File.ReadAllText(result.BackupPath!), Is.EqualTo(original));
                    Assert.That(selected.BaselineHash, Is.EqualTo(result.NewHash));
                });
            });
        }

        private static void WithTempDirectory(Action<string> action)
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "dw-card-collection-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                action(directory);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
    }
}
