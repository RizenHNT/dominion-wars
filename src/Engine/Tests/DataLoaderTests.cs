using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Data;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
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
        [Test] public void MapsTypePunishAndVulnerabilities() { var catalog = CardCatalog.LoadDirectory(Path.Combine(DataRoot, "cards")); var card = catalog.Cards["flame_berserker"]; Assert.That(card.IsMinion, Is.True); Assert.That(card.PunishActivatable, Is.True); Assert.That(card.PunishCost, Is.EqualTo(1)); Assert.That(catalog.Cards["flame_leader"].Vulnerabilities, Does.Contain("DAMAGE")); }
        [Test] public void MapsPunishAndExplicitPunishCostIndependently() { var file = Path.GetTempFileName(); try { File.WriteAllText(file, "{\"id\":\"explicit\",\"name\":\"Explicit\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"punish\":5,\"punishCost\":1,\"punishActivatable\":false,\"text\":\"\"}"); var explicitCard = CardCatalog.LoadFile(file); File.WriteAllText(file, "{\"id\":\"legacy\",\"name\":\"Legacy\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"punish\":3,\"text\":\"\"}"); var legacyCard = CardCatalog.LoadFile(file); Assert.Multiple(() => { Assert.That(explicitCard.Punish, Is.EqualTo(5)); Assert.That(explicitCard.PunishCost, Is.EqualTo(1)); Assert.That(explicitCard.PunishActivatable, Is.False); Assert.That(legacyCard.Punish, Is.EqualTo(3)); Assert.That(legacyCard.PunishCost, Is.EqualTo(3)); Assert.That(legacyCard.PunishActivatable, Is.True); }); } finally { File.Delete(file); } }
        [Test] public void MapsCardTypeAndTurnMetadata() { var cards = CardCatalog.LoadDirectory(Path.Combine(DataRoot, "cards")).Cards; Assert.Multiple(() => { Assert.That(cards["flame_imp"].Type, Is.EqualTo("MINION")); Assert.That(cards["flame_recruit"].Punish, Is.EqualTo(1)); Assert.That(cards.Values.Any(card => card.Type == "AMBUSH" && card.AmbushTrigger != null), Is.True); Assert.That(cards.Values.All(card => card.AttacksPerTurn >= 1), Is.True); }); }
        [Test] public void MapsCardAndLeaderEffectSpecsWithoutLosingTargetFlags() { var cards = CardCatalog.LoadDirectory(Path.Combine(DataRoot, "cards")).Cards; var imp = cards["flame_imp"].OnPlayEffects.Single(); var strike = cards["flame_strike"].OnPlayEffects.Single(); var leader = cards["flame_leader"].LeaderEnterEffects.Single(); Assert.Multiple(() => { Assert.That(imp.Action, Is.EqualTo("DAMAGE")); Assert.That(imp.Target, Is.EqualTo("ENEMY_FACE")); Assert.That(imp.Amount, Is.EqualTo(1)); Assert.That(strike.KingSlayer, Is.True); Assert.That(leader.Target, Is.EqualTo("ALL_ENEMY_MINIONS")); Assert.That(cards["flame_leader"].LeaderPunishEffects, Has.Count.EqualTo(2)); Assert.That(cards["sea_leader"].GrantLife, Is.EqualTo(25)); Assert.That(cards["flame_leader"].LeaderWinCondition, Is.EqualTo("ROYAL_CASTLE_BREAK")); Assert.That(cards["sea_leader"].LeaderWinParam, Is.EqualTo(18)); Assert.That(cards["wood_leader"].LeaderWinCondition, Is.EqualTo("GIANT_HEALTH_GE")); Assert.That(cards["wood_leader"].LeaderWinParam, Is.EqualTo(512)); Assert.That(cards["wood_leader"].LeaderEnterEffects.Select(effect => effect.Action), Is.EqualTo(new[] { "SUMMON", "ADD_RAMPANT" })); Assert.That(cards["wood_leader"].LeaderPunishEffects.Select(effect => effect.Action), Is.EqualTo(new[] { "PROTECT_TURN", "ADD_RAMPANT" })); Assert.That(cards["machine_alpha"].LeaderWinCondition, Is.EqualTo("PULL_TOTAL_GE")); Assert.That(cards["machine_alpha"].LeaderWinParam, Is.EqualTo(6)); Assert.That(cards["machine_leader"].IsLandmark, Is.True); Assert.That(cards["machine_leader"].LandmarkTiers.Select(tier => tier.Tier), Is.EqualTo(new[] { 1, 2 })); Assert.That(cards["machine_leader"].LandmarkTiers[1].Chant, Is.EqualTo(1)); Assert.That(cards["machine_leader"].LandmarkTiers[1].SummonCardId, Is.EqualTo("machine_alpha")); Assert.That(cards["machine_leader"].LeaderDurability, Is.EqualTo(8)); }); }
        [Test] public void MapsMechanicalCostsAndLifecycleEffects() { var file = Path.GetTempFileName(); try { File.WriteAllText(file, "{\"id\":\"mechanical_card\",\"name\":\"Mechanical\",\"faction\":\"机械遗迹\",\"type\":\"MINION\",\"attack\":1,\"health\":2,\"text\":\"\",\"commitCost\":2,\"uploadCost\":3,\"downloadCost\":1,\"commitEffects\":[{\"action\":\"DRAW\",\"amount\":1}],\"pushEffects\":[{\"action\":\"BUFF\",\"target\":\"SELF\",\"amount\":1,\"param\":\"hp\"}],\"pullEffects\":[{\"action\":\"HEAL\",\"amount\":1}]}" ); var card = CardCatalog.LoadFile(file); Assert.Multiple(() => { Assert.That(card.CommitCost, Is.EqualTo(2)); Assert.That(card.UploadCost, Is.EqualTo(3)); Assert.That(card.DownloadCost, Is.EqualTo(1)); Assert.That(card.CommitEffects.Single().Action, Is.EqualTo("DRAW")); Assert.That(card.PushEffects.Single().Param, Is.EqualTo("hp")); Assert.That(card.PullEffects.Single().Action, Is.EqualTo("HEAL")); }); } finally { File.Delete(file); } }
        [Test]
        public void AppliesMechanicalLifecycleDefaultsWhenOrdinaryMinionOmitsThem()
        {
            var file = Path.GetTempFileName();
            try
            {
                File.WriteAllText(file, "{\"id\":\"mechanical_default\",\"name\":\"Mechanical Default\",\"faction\":\"机械遗迹\",\"type\":\"MINION\",\"attack\":1,\"health\":2,\"text\":\"\"}");
                var card = CardCatalog.LoadFile(file);

                Assert.Multiple(() =>
                {
                    Assert.That(card.CommitCost, Is.EqualTo(1));
                    Assert.That(card.UploadCost, Is.Zero);
                    Assert.That(card.DownloadCost, Is.EqualTo(1));
                    Assert.That(card.PullEffects, Has.Count.EqualTo(1));
                    Assert.That(card.PullEffects.Single().Action, Is.EqualTo(EffectNames.Buff));
                    Assert.That(card.PullEffects.Single().Target, Is.EqualTo("FRIENDLY_MINION"));
                    Assert.That(card.PullEffects.Single().Amount, Is.EqualTo(1));
                    Assert.That(card.PullEffects.Single().Param, Is.EqualTo("both"));
                });
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Test]
        public void FormalOrdinaryMachineMinionsHaveTheApprovedLifecycleDefaults()
        {
            var cards = CardCatalog.LoadDirectory(Path.Combine(DataRoot, "cards")).Cards;
            var ordinaryMachineMinions = cards.Values
                .Where(card => card.Faction == "机械遗迹" && card.IsMinion && !card.IsLeader)
                .ToArray();

            Assert.That(ordinaryMachineMinions, Has.Length.EqualTo(8));

            // 每张普通机械随从都带显式生命周期字段（RULES §12.4：显式专属字段优先于 1/0/1 默认）。
            // M1 差异化把"留场价值高 → 提交/下载代价高"写进数据，因此逐卡断言显式值。
            var expectedLifecycle = new Dictionary<string, (int Commit, int Upload, int Download)>
            {
                ["machine_drone"] = (1, 0, 1),
                ["machine_golem"] = (2, 0, 1),
                ["machine_wall"] = (2, 0, 1),
                ["machine_blaster"] = (3, 0, 1),
                ["machine_titan"] = (3, 0, 2),
                ["machine_spark"] = (1, 0, 1),
                ["machine_assembler"] = (1, 0, 1),
                ["machine_recycler"] = (1, 0, 1),
            };

            Assert.Multiple(() =>
            {
                foreach (var card in ordinaryMachineMinions)
                {
                    Assert.That(expectedLifecycle.ContainsKey(card.Id), Is.True, card.Id);
                    var expected = expectedLifecycle[card.Id];
                    Assert.That(card.CommitCost, Is.EqualTo(expected.Commit), card.Id);
                    Assert.That(card.UploadCost, Is.EqualTo(expected.Upload), card.Id);
                    Assert.That(card.DownloadCost, Is.EqualTo(expected.Download), card.Id);
                    Assert.That(card.PullEffects, Has.Count.EqualTo(1), card.Id);
                    Assert.That(card.PullEffects.Single().Target, Is.EqualTo("FRIENDLY_MINION"), card.Id);
                    Assert.That(card.PullEffects.Single().Amount, Is.EqualTo(1), card.Id);
                    Assert.That(card.PullEffects.Single().Param, Is.EqualTo("both"), card.Id);
                }

                Assert.That(cards["machine_alpha"].CommitCost, Is.Zero);
                Assert.That(cards["machine_alpha"].PullEffects, Is.Empty);
                Assert.That(cards["machine_leader"].CommitCost, Is.Zero);
                Assert.That(cards["machine_leader"].UploadCost, Is.Zero);
                Assert.That(cards["machine_leader"].DownloadCost, Is.Zero);
            });
        }
        [Test] public void MapsLandmarkMarkerAndTierMetadata() { var file = Path.GetTempFileName(); try { File.WriteAllText(file, "{\"id\":\"machine_landmark\",\"name\":\"Landmark\",\"faction\":\"机械遗迹\",\"type\":\"SPELL\",\"leader\":true,\"text\":\"\",\"leaderDef\":{\"winCondition\":\"PULL_TOTAL_GE\",\"isLandmark\":true,\"landmarkTiers\":[{\"tier\":1,\"effect\":\"free pull\"},{\"tier\":2,\"chant\":1,\"summon\":\"machine_alpha\"}]}}" ); var card = CardCatalog.LoadFile(file); Assert.Multiple(() => { Assert.That(card.IsLandmark, Is.True); Assert.That(card.LandmarkTiers, Has.Count.EqualTo(2)); Assert.That(card.LandmarkTiers[0].EffectText, Is.EqualTo("free pull")); Assert.That(card.LandmarkTiers[1].Chant, Is.EqualTo(1)); Assert.That(card.LandmarkTiers[1].SummonCardId, Is.EqualTo("machine_alpha")); }); } finally { File.Delete(file); } }
        [Test]
        public void FormalMachineLandmarkDataRunsTieredPullPromotion()
        {
            var catalog = CardCatalog.LoadDirectory(Path.Combine(DataRoot, "cards"));
            var state = new GameState(
                new PlayerState(0, 20),
                new PlayerState(1, 20),
                cardLibrary: catalog.Cards.Values);
            var flow = TurnFlow.CreateDefault();
            var router = TurnActionRouter.CreateDefault(flow);
            flow.Advance(state, 0);
            Assert.That(router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted, Is.True);

            var landmark = new CardInstance(600, 0, catalog.Cards["machine_leader"])
            {
                IsLeaderEntity = true,
            };
            state.GetPlayer(0).LeaderZone.Add(landmark);
            state.GetPlayer(0).CloudStack.Add(new CardInstance(
                601,
                0,
                new CardDefinition("formal_pull_one", "Formal Pull One")));
            state.GetPlayer(0).CloudStack.Add(new CardInstance(
                602,
                0,
                new CardDefinition("formal_pull_two", "Formal Pull Two")));

            foreach (var expectedTarget in new[] { 602L, 601L })
            {
                var action = flow.GetLegalActions(state, 0)
                    .Single(candidate => candidate.Type == LegalActionGenerator.Pull);
                Assert.That(action.TargetId, Is.EqualTo(expectedTarget));
                Assert.That(router.Execute(state, new GameActionRequest(
                    0,
                    LegalActionGenerator.Pull,
                    action.ActionId,
                    action.SourceId,
                    action.TargetId?.ToString())).Accepted, Is.True);
            }

            flow.JumpTo(state, 0, TurnPhase.End);
            flow.Advance(state, 0);

            Assert.Multiple(() =>
            {
                Assert.That(state.GetPlayer(0).PullCount, Is.EqualTo(2));
                Assert.That(state.GetPlayer(0).Leader, Is.Not.Null);
                Assert.That(state.GetPlayer(0).Leader!.Definition.Id, Is.EqualTo("machine_alpha"));
                Assert.That(state.GetPlayer(0).Graveyard, Does.Contain(landmark));
            });
        }

        [Test]
        public void FormalMachineFactoryRetainsChantAndThreeDroneProduction()
        {
            var catalog = CardCatalog.LoadDirectory(Path.Combine(DataRoot, "cards"));
            var factory = catalog.Cards["machine_factory"];

            Assert.Multiple(() =>
            {
                Assert.That(factory.Type, Is.EqualTo("SPELL"));
                Assert.That(factory.Chant, Is.EqualTo(2));
                Assert.That(factory.ChantEffects, Has.Count.EqualTo(1));
                Assert.That(factory.ChantEffects.Single().Action, Is.EqualTo(EffectNames.Summon));
                Assert.That(factory.ChantEffects.Single().Amount, Is.EqualTo(3));
                Assert.That(factory.ChantEffects.Single().Param, Is.EqualTo("machine_drone"));
                Assert.That(factory.Text, Is.EqualTo("召唤三个侦察机偶。"));
            });
        }
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
