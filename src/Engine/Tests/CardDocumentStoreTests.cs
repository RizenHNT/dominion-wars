using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DominionWars.Data.CardEditor;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{
    public sealed class CardDocumentStoreTests
    {
        [Test]
        public void LoadCapturesBaselineAndSaveReturnsHashesBackupAndFieldDiff()
        {
            WithTempDirectory(directory =>
            {
                var path = Path.Combine(directory, "card.json");
                var original = "[{\"name\":\"Old Name\",\"id\":\"store_card\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"text\":\"\"}]";
                File.WriteAllText(path, original, new UTF8Encoding(false));
                var store = new CardDocumentStore();
                var document = store.Load(path);
                var originalHash = Sha256(File.ReadAllBytes(path));

                document.SetString("name", "New Name");
                document.SetInteger("cost", 2);
                var result = store.Save(document);

                Assert.Multiple(() =>
                {
                    Assert.That(document.BaselineHash, Is.EqualTo(result.NewHash));
                    Assert.That(result.Path, Is.EqualTo(Path.GetFullPath(path)));
                    Assert.That(result.OldHash, Is.EqualTo(originalHash));
                    Assert.That(result.NewHash, Is.EqualTo(Sha256(File.ReadAllBytes(path))));
                    Assert.That(result.BackupPath, Is.Not.Null.And.EndsWith(".bak"));
                    Assert.That(File.ReadAllBytes(result.BackupPath!), Is.EqualTo(Encoding.UTF8.GetBytes(original)));
                    Assert.That(result.FieldChanges.Select(change => change.ToString()),
                        Is.EqualTo(new[] { "Added cost", "Changed name" }));
                    Assert.That(result.FieldDiff, Does.Contain("Added cost"));
                    Assert.That(result.FieldDiff, Does.Contain("Changed name"));
                });
            });
        }

        [Test]
        public void ConcurrentDiskChangeFailsClosedAndLeavesChangedFileUntouched()
        {
            WithTempDirectory(directory =>
            {
                var path = Path.Combine(directory, "card.json");
                File.WriteAllText(path, "[{\"id\":\"store_card\",\"name\":\"Original\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"text\":\"\"}]", new UTF8Encoding(false));
                var store = new CardDocumentStore();
                var document = store.Load(path);
                document.SetString("name", "Editor Change");
                var external = "[{\"id\":\"store_card\",\"name\":\"External Change\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"text\":\"\"}]";
                File.WriteAllText(path, external, new UTF8Encoding(false));

                var exception = Assert.Throws<CardDocumentConflictException>(() => store.Save(document));

                Assert.Multiple(() =>
                {
                    Assert.That(exception!.ExpectedHash, Is.EqualTo(document.BaselineHash));
                    Assert.That(exception.ActualHash, Is.EqualTo(Sha256(File.ReadAllBytes(path))));
                    Assert.That(File.ReadAllText(path), Is.EqualTo(external));
                    Assert.That(Directory.GetFiles(directory, "*.bak"), Is.Empty);
                });
            });
        }

        [Test]
        public void ExistingFileCannotBeOverwrittenWithoutLoadBaseline()
        {
            WithTempDirectory(directory =>
            {
                var path = Path.Combine(directory, "card.json");
                var original = "{\"id\":\"existing\",\"name\":\"Existing\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"text\":\"\"}";
                File.WriteAllText(path, original, new UTF8Encoding(false));
                var document = CardDocument.CreateNew("new_card", "New", "烈焰帝国", "SPELL");

                var exception = Assert.Throws<CardDocumentConflictException>(() => new CardDocumentStore().Save(document, path));

                Assert.Multiple(() =>
                {
                    Assert.That(exception!.ExpectedHash, Is.Null);
                    Assert.That(exception.ActualHash, Is.EqualTo(Sha256(File.ReadAllBytes(path))));
                    Assert.That(File.ReadAllText(path), Is.EqualTo(original));
                });
            });
        }

        [Test]
        public void NewDocumentCanBeSavedAndHasNoBackup()
        {
            WithTempDirectory(directory =>
            {
                var path = Path.Combine(directory, "new-card.json");
                var document = CardDocument.CreateNew("new_card", "New", "烈焰帝国", "SPELL");
                document.SetString("text", "A new card");

                var result = new CardDocumentStore().Save(document, path);
                var loaded = new CardDocumentStore().Load(path);

                Assert.Multiple(() =>
                {
                    Assert.That(result.OldHash, Is.Null);
                    Assert.That(result.BackupPath, Is.Null);
                    Assert.That(result.NewHash, Is.EqualTo(Sha256(File.ReadAllBytes(path))));
                    Assert.That(document.BaselineHash, Is.EqualTo(result.NewHash));
                    Assert.That(loaded.Id, Is.EqualTo("new_card"));
                    Assert.That(loaded.BaselineHash, Is.EqualTo(result.NewHash));
                });
            });
        }

        [Test]
        public void CanonicalOutputIsStableForDifferentPropertyOrder()
        {
            WithTempDirectory(directory =>
            {
                var first = CardDocument.LoadJson(
                    "{\"z\":1,\"id\":\"stable\",\"nested\":{\"b\":2,\"a\":1},\"tags\":[{\"z\":0,\"a\":1}],\"name\":\"Stable\"}");
                var second = CardDocument.LoadJson(
                    "{\"name\":\"Stable\",\"tags\":[{\"a\":1,\"z\":0}],\"nested\":{\"a\":1,\"b\":2},\"id\":\"stable\",\"z\":1}");

                var firstPath = Path.Combine(directory, "first.json");
                var secondPath = Path.Combine(directory, "second.json");
                new CardDocumentStore().Save(first, firstPath);
                new CardDocumentStore().Save(second, secondPath);

                Assert.That(File.ReadAllBytes(firstPath), Is.EqualTo(File.ReadAllBytes(secondPath)));
            });
        }

        [Test]
        public void MissingParentFailsBeforeTouchingExistingSource()
        {
            WithTempDirectory(directory =>
            {
                var sourcePath = Path.Combine(directory, "source.json");
                var original = "[{\"id\":\"source\",\"name\":\"Source\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"text\":\"\"}]";
                File.WriteAllText(sourcePath, original, new UTF8Encoding(false));
                var document = new CardDocumentStore().Load(sourcePath);
                document.SetString("name", "Edited");

                var missingPath = Path.Combine(directory, "missing", "target.json");
                Assert.Throws<DirectoryNotFoundException>(() => new CardDocumentStore().Save(document, missingPath));

                Assert.That(File.ReadAllText(sourcePath), Is.EqualTo(original));
                Assert.That(Directory.GetFiles(directory, "*.bak"), Is.Empty);
            });
        }

        private static void WithTempDirectory(Action<string> action)
        {
            var directory = Path.Combine(Path.GetTempPath(), "dw-card-store-" + Guid.NewGuid().ToString("N"));
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

        private static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
        }
    }
}
