using System;
using System.IO;
using System.Linq;
using DominionWars.Data;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{
    public sealed class DataLoaderTests
    {
        private static string DataRoot
        {
            get
            {
                var directory = TestContext.CurrentContext.TestDirectory;
                while (!string.IsNullOrEmpty(directory) && !Directory.Exists(Path.Combine(directory, "data", "cards"))) directory = Directory.GetParent(directory)?.FullName;
                return Path.Combine(directory!, "data");
            }
        }

        [Test] public void LoadsAllNinetyOneCards() { var catalog = CardCatalog.LoadDirectory(Path.Combine(DataRoot, "cards")); Assert.That(catalog.Cards, Has.Count.EqualTo(91)); }
        [Test] public void LoadsFourDecks() { Assert.That(DeckLoader.LoadDirectory(Path.Combine(DataRoot, "decks")), Has.Count.EqualTo(4)); }
        [Test] public void MapsTypePunishAndVulnerabilities() { var catalog = CardCatalog.LoadDirectory(Path.Combine(DataRoot, "cards")); var card = catalog.Cards["flame_berserker"]; Assert.That(card.IsMinion, Is.True); Assert.That(card.PunishActivatable, Is.True); Assert.That(card.PunishCost, Is.EqualTo(3)); Assert.That(catalog.Cards["flame_leader"].Vulnerabilities, Does.Contain("DAMAGE")); }
        [Test] public void LeaderWithoutVulnerabilitiesLoads() { var directory = Path.Combine(Path.GetTempPath(), "dw-data-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); var file = Path.Combine(directory, "leader.json"); try { File.WriteAllText(file, "[{\"id\":\"abc_leader\",\"name\":\"Leader\",\"faction\":\"烈焰帝国\",\"type\":\"MINION\",\"leader\":true,\"attack\":1,\"health\":3,\"leaderDef\":{\"winCondition\":\"NONE\"},\"text\":\"\"}]"); Assert.That(CardCatalog.LoadDirectory(directory).Cards, Has.Count.EqualTo(1)); } finally { Directory.Delete(directory, true); } }
        [Test] public void RejectsBadJsonAndMissingRequiredField() { var file = Path.GetTempFileName(); try { File.WriteAllText(file, "not json"); Assert.Throws<InvalidDataException>(() => CardCatalog.LoadFile(file)); File.WriteAllText(file, "[{}]"); Assert.Throws<InvalidDataException>(() => CardCatalog.LoadFile(file)); Assert.Throws<DirectoryNotFoundException>(() => CardCatalog.LoadDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))); } finally { File.Delete(file); } }
        [Test] public void RejectsMinionWithoutCombatStats() => AssertConditionalCardRejected("{\"id\":\"abc_minion\",\"name\":\"M\",\"faction\":\"烈焰帝国\",\"type\":\"MINION\",\"text\":\"\"}");
        [Test] public void RejectsPunishWithoutPositivePunishValue() => AssertConditionalCardRejected("{\"id\":\"abc_punish\",\"name\":\"P\",\"faction\":\"烈焰帝国\",\"type\":\"PUNISH\",\"punish\":0,\"text\":\"\"}");
        [Test] public void RejectsFalseLeaderMarker() => AssertConditionalCardRejected("{\"id\":\"abc_card\",\"name\":\"C\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"leader\":false,\"text\":\"\"}");
        private static void AssertConditionalCardRejected(string json) { var file = Path.GetTempFileName(); try { File.WriteAllText(file, "[" + json + "]"); Assert.Throws<InvalidDataException>(() => CardCatalog.LoadFile(file)); } finally { File.Delete(file); } }
    }
}
