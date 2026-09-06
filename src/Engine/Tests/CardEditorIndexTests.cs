using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Data.CardEditor;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{
    public sealed class CardEditorIndexTests
    {
        [Test]
        public void LoadsProductionCardDirectoryWithStableCardIdOrder()
        {
            var root = RepositoryRoot;
            var index = CardDocumentIndex.LoadDirectory(Path.Combine(root, "data", "cards"));

            Assert.Multiple(() =>
            {
                Assert.That(index.Documents, Is.Not.Empty);
                Assert.That(index.Documents.Select(document => document.Id),
                    Is.EqualTo(index.Documents.Select(document => document.Id).OrderBy(id => id, StringComparer.Ordinal)));
                Assert.That(index.TryGet("flame_leader", out var leader), Is.True);
                Assert.That(leader!.Name, Is.EqualTo("烈焰皇·焚天"));
            });
        }

        [Test]
        public void DuplicateCardIdsFailClosed()
        {
            WithTempDirectory(directory =>
            {
                WriteCardFile(directory, "a.json", "duplicate_card", "First");
                WriteCardFile(directory, "b.json", "duplicate_card", "Second");

                var exception = Assert.Throws<InvalidDataException>(
                    () => CardDocumentIndex.LoadDirectory(directory));
                Assert.That(exception!.Message, Does.Contain("duplicate card id"));
                Assert.That(exception.Message, Does.Contain("duplicate_card"));
            });
        }

        [Test]
        public void SearchesCardIdNameFactionTypeStatusAndArtId()
        {
            WithTempDirectory(directory =>
            {
                WriteCardFile(directory, "cards.json", "alpha_card", "Ash Runner", "烈焰帝国", "MINION", "card_art_flame", "ready");
                AppendCardFile(directory, "cards.json", "beta_spell", "Ocean Pulse", "深海联盟", "SPELL", "card_art_sea", "approved");
                AppendCardFile(directory, "cards.json", "gamma_old", "Retired Gear", "机械遗迹", "MINION", "card_art_machine", "deprecated");

                var index = CardDocumentIndex.LoadDirectory(directory);

                Assert.Multiple(() =>
                {
                    Assert.That(index.Search(new CardDocumentSearchQuery(cardId: "beta"))
                        .Select(document => document.Id), Is.EqualTo(new[] { "beta_spell" }));
                    Assert.That(index.Search(new CardDocumentSearchQuery(text: "Ash"))
                        .Select(document => document.Id), Is.EqualTo(new[] { "alpha_card" }));
                    Assert.That(index.Search(new CardDocumentSearchQuery(faction: "机械"))
                        .Select(document => document.Id), Is.EqualTo(new[] { "gamma_old" }));
                    Assert.That(index.Search(new CardDocumentSearchQuery(type: "MINION"))
                        .Select(document => document.Id), Is.EqualTo(new[] { "alpha_card", "gamma_old" }));
                    Assert.That(index.Search(new CardDocumentSearchQuery(lifecycle: CardLifecycle.Deprecated))
                        .Select(document => document.Id), Is.EqualTo(new[] { "gamma_old" }));
                    Assert.That(index.Search(new CardDocumentSearchQuery(artId: "sea"))
                        .Select(document => document.Id), Is.EqualTo(new[] { "beta_spell" }));
                });
            });
        }

        [Test]
        public void SearchResultsAreOrdinalCardIdOrderedRegardlessOfFileOrder()
        {
            WithTempDirectory(directory =>
            {
                WriteCardFile(directory, "z-file.json", "z_card", "Z");
                AppendCardFile(directory, "z-file.json", "a_card", "A", "烈焰帝国", "SPELL", null, "approved");
                WriteCardFile(directory, "a-file.json", "m_card", "M");

                var index = CardDocumentIndex.LoadDirectory(directory);
                var ids = index.Search().Select(document => document.Id).ToArray();

                Assert.That(ids, Is.EqualTo(new[] { "a_card", "m_card", "z_card" }));
            });
        }

        [Test]
        public void EmptyDirectoryAndMissingCardIdFailClosed()
        {
            WithTempDirectory(directory =>
            {
                Assert.Throws<InvalidDataException>(() => CardDocumentIndex.LoadDirectory(directory));
                WriteCardFile(directory, "missing.json", null, "Missing ID");
                var exception = Assert.Throws<InvalidDataException>(
                    () => CardDocumentIndex.LoadDirectory(directory));
                Assert.That(exception!.Message, Does.Contain("non-empty card id"));
            });
        }

        private static string RepositoryRoot
        {
            get
            {
                var directory = TestContext.CurrentContext.TestDirectory;
                while (!string.IsNullOrEmpty(directory) && !Directory.Exists(Path.Combine(directory, "data", "cards")))
                {
                    directory = Directory.GetParent(directory)?.FullName;
                }

                return directory!;
            }
        }

        private static void WithTempDirectory(Action<string> action)
        {
            var directory = Path.Combine(Path.GetTempPath(), "dw-card-index-" + Guid.NewGuid().ToString("N"));
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

        private static void WriteCardFile(
            string directory,
            string fileName,
            string? id,
            string name,
            string faction = "烈焰帝国",
            string type = "SPELL",
            string? artId = null,
            string? status = null)
        {
            var json = CardJson(id, name, faction, type, artId, status);
            File.WriteAllText(Path.Combine(directory, fileName), "[" + json + "]");
        }

        private static void AppendCardFile(
            string directory,
            string fileName,
            string id,
            string name,
            string faction,
            string type,
            string? artId,
            string status)
        {
            var path = Path.Combine(directory, fileName);
            var existing = File.ReadAllText(path);
            File.WriteAllText(path, existing.TrimEnd(' ', '\r', '\n', ']') + "," + CardJson(id, name, faction, type, artId, status) + "]");
        }

        private static string CardJson(
            string? id,
            string name,
            string faction,
            string type,
            string? artId,
            string? status)
        {
            var values = new List<string>();
            if (id is not null) values.Add("\"id\":\"" + id + "\"");
            values.Add("\"name\":\"" + name + "\"");
            values.Add("\"faction\":\"" + faction + "\"");
            values.Add("\"type\":\"" + type + "\"");
            values.Add("\"text\":\"\"");
            if (artId is not null) values.Add("\"artId\":\"" + artId + "\"");
            if (status is not null) values.Add("\"status\":\"" + status + "\"");
            return "{" + string.Join(",", values) + "}";
        }
    }
}
