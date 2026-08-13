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
        [Test] public void MapsCardTypeAndTurnMetadata() { var cards = CardCatalog.LoadDirectory(Path.Combine(DataRoot, "cards")).Cards; Assert.Multiple(() => { Assert.That(cards["flame_imp"].Type, Is.EqualTo("MINION")); Assert.That(cards["flame_recruit"].Punish, Is.EqualTo(1)); Assert.That(cards.Values.Any(card => card.Type == "AMBUSH" && card.AmbushTrigger != null), Is.True); Assert.That(cards.Values.All(card => card.AttacksPerTurn >= 1), Is.True); }); }
        [Test] public void MapsCardAndLeaderEffectSpecsWithoutLosingTargetFlags() { var cards = CardCatalog.LoadDirectory(Path.Combine(DataRoot, "cards")).Cards; var imp = cards["flame_imp"].OnPlayEffects.Single(); var strike = cards["flame_strike"].OnPlayEffects.Single(); var leader = cards["flame_leader"].LeaderEnterEffects.Single(); Assert.Multiple(() => { Assert.That(imp.Action, Is.EqualTo("DAMAGE")); Assert.That(imp.Target, Is.EqualTo("ENEMY_FACE")); Assert.That(imp.Amount, Is.EqualTo(1)); Assert.That(strike.KingSlayer, Is.True); Assert.That(leader.Target, Is.EqualTo("ALL_ENEMY_MINIONS")); Assert.That(cards["flame_leader"].LeaderPunishEffects, Has.Count.EqualTo(2)); Assert.That(cards["sea_leader"].GrantLife, Is.EqualTo(25)); Assert.That(cards["flame_leader"].LeaderWinCondition, Is.EqualTo("ROYAL_CASTLE_BREAK")); Assert.That(cards["machine_leader"].LeaderDurability, Is.EqualTo(8)); }); }
        [Test] public void LeaderWithoutVulnerabilitiesLoads() { var directory = Path.Combine(Path.GetTempPath(), "dw-data-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); var file = Path.Combine(directory, "leader.json"); try { File.WriteAllText(file, "[{\"id\":\"abc_leader\",\"name\":\"Leader\",\"faction\":\"烈焰帝国\",\"type\":\"MINION\",\"leader\":true,\"attack\":1,\"health\":3,\"leaderDef\":{\"winCondition\":\"NONE\"},\"text\":\"\"}]"); Assert.That(CardCatalog.LoadDirectory(directory).Cards, Has.Count.EqualTo(1)); } finally { Directory.Delete(directory, true); } }
        [Test] public void RejectsBadJsonAndMissingRequiredField() { var file = Path.GetTempFileName(); try { File.WriteAllText(file, "not json"); Assert.Throws<InvalidDataException>(() => CardCatalog.LoadFile(file)); File.WriteAllText(file, "[{}]"); Assert.Throws<InvalidDataException>(() => CardCatalog.LoadFile(file)); Assert.Throws<DirectoryNotFoundException>(() => CardCatalog.LoadDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))); } finally { File.Delete(file); } }
        [Test] public void RejectsMinionWithoutCombatStats() => AssertConditionalCardRejected("{\"id\":\"abc_minion\",\"name\":\"M\",\"faction\":\"烈焰帝国\",\"type\":\"MINION\",\"text\":\"\"}");
        [Test] public void RejectsPunishWithoutPositivePunishValue() => AssertConditionalCardRejected("{\"id\":\"abc_punish\",\"name\":\"P\",\"faction\":\"烈焰帝国\",\"type\":\"PUNISH\",\"punish\":0,\"text\":\"\"}");
        [Test] public void RejectsFalseLeaderMarker() => AssertConditionalCardRejected("{\"id\":\"abc_card\",\"name\":\"C\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"leader\":false,\"text\":\"\"}");
        [Test] public void UnknownCardFieldsProduceWarningsWithoutRejecting() { var file = Path.GetTempFileName(); try { File.WriteAllText(file, "{\"id\":\"abc_card\",\"name\":\"C\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"text\":\"\",\"futureField\":true}"); var warnings = new System.Collections.Generic.List<string>(); Assert.That(CardCatalog.LoadFile(file, warnings.Add).Id, Is.EqualTo("abc_card")); Assert.That(warnings, Has.Some.Contains("unknown field futureField")); } finally { File.Delete(file); } }
        [Test] public void RejectsDeckWithInvalidFactionOrCount() { var file = Path.GetTempFileName(); try { File.WriteAllText(file, "{\"name\":\"N\",\"faction\":\"未知\",\"leader\":\"abc\",\"cards\":{\"abc\":1}}"); Assert.Throws<InvalidDataException>(() => DeckLoader.LoadFile(file)); File.WriteAllText(file, "{\"name\":\"N\",\"faction\":\"烈焰帝国\",\"leader\":\"abc\",\"cards\":{\"abc\":0}}"); Assert.Throws<InvalidDataException>(() => DeckLoader.LoadFile(file)); } finally { File.Delete(file); } }
        [Test] public void UnknownDeckFieldsProduceWarningsWithoutRejecting() { var file = Path.GetTempFileName(); try { File.WriteAllText(file, "{\"name\":\"N\",\"faction\":\"烈焰帝国\",\"leader\":\"abc\",\"cards\":{\"abc\":1},\"futureField\":true}"); var warnings = new System.Collections.Generic.List<string>(); Assert.That(DeckLoader.LoadFile(file, warnings.Add).Cards["abc"], Is.EqualTo(1)); Assert.That(warnings, Has.Some.Contains("unknown field futureField")); } finally { File.Delete(file); } }
        [Test] public void CardDirectoryRejectsDuplicateIdsAndEmptyDirectory() { var directory = Path.Combine(Path.GetTempPath(), "dw-duplicate-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); try { Assert.Throws<InvalidDataException>(() => CardCatalog.LoadDirectory(directory)); File.WriteAllText(Path.Combine(directory, "a.json"), "[{\"id\":\"abc_card\",\"name\":\"C\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"text\":\"\"}]"); File.WriteAllText(Path.Combine(directory, "b.json"), "[{\"id\":\"abc_card\",\"name\":\"C2\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"text\":\"\"}]"); Assert.Throws<InvalidDataException>(() => CardCatalog.LoadDirectory(directory)); } finally { Directory.Delete(directory, true); } }
        [Test] public void DeckLoaderRejectsBadJsonRootAndMissingFields() { var file = Path.GetTempFileName(); try { File.WriteAllText(file, "not json"); Assert.Throws<InvalidDataException>(() => DeckLoader.LoadFile(file)); File.WriteAllText(file, "[]"); Assert.Throws<InvalidDataException>(() => DeckLoader.LoadFile(file)); File.WriteAllText(file, "{\"name\":\"N\",\"faction\":\"烈焰帝国\",\"leader\":\"abc\"}"); Assert.Throws<InvalidDataException>(() => DeckLoader.LoadFile(file)); } finally { File.Delete(file); } }
        [Test] public void DeckDirectoryRejectsEmptyDirectory() { var directory = Path.Combine(Path.GetTempPath(), "dw-decks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); try { Assert.Throws<InvalidDataException>(() => DeckLoader.LoadDirectory(directory)); } finally { Directory.Delete(directory, true); } }
        private static void AssertConditionalCardRejected(string json) { var file = Path.GetTempFileName(); try { File.WriteAllText(file, "[" + json + "]"); Assert.Throws<InvalidDataException>(() => CardCatalog.LoadFile(file)); } finally { File.Delete(file); } }
    }
}
