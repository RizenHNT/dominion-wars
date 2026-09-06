#if UNITY_INCLUDE_TESTS
#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Data;
using DominionWars.Engine.Model;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeCardDisplayInspectEditModeTests
{
    [Test]
    public void BuildVisibleCardsIncludesOwnHandAndPublicZonesButNeverOpponentHand()
    {
        var catalog = Catalog(
            Definition("own_hand", "Own Hand"),
            Definition("own_field", "Own Field", isMinion: true),
            Definition("own_leader", "Own Leader", isMinion: true, isLeader: true),
            Definition("own_grave", "Own Grave"),
            Definition("own_commit", "Own Commit"),
            Definition("own_cloud", "Own Cloud"),
            Definition("opponent_secret", "Opponent Secret"),
            Definition("opponent_field", "Opponent Field", isMinion: true),
            Definition("opponent_leader", "Opponent Leader", isMinion: true, isLeader: true),
            Definition("opponent_grave", "Opponent Grave"));
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                Hand = new[] { Card("own_hand", 1, 0) },
                Field = new[] { Card("own_field", 2, 0) },
                LeaderZone = new[] { Card("own_leader", 3, 0) },
                Graveyard = new[] { Card("own_grave", 4, 0) },
                CommitQueue = new[] { Card("own_commit", 8, 0) },
                CloudStack = new[] { Card("own_cloud", 9, 0) },
            },
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_1",
                // This deliberately malformed redacted list must still never
                // be traversed by the viewer-safe display factory.
                Hand = new[] { Card("opponent_secret", 5, 1) },
                Field = new[] { Card("opponent_field", 6, 1) },
                LeaderZone = new[] { Card("opponent_leader", 7, 1) },
                Graveyard = new[] { Card("opponent_grave", 8, 1) },
            });

        var cards = RuntimeCardDisplayModel.BuildVisibleCards(snapshot, catalog);

        Assert.That(cards.Select(card => card.StableId), Is.EqualTo(new[]
        {
            "own_hand", "own_field", "own_leader", "own_grave",
            "own_commit", "own_cloud",
            "opponent_field", "opponent_leader", "opponent_grave",
        }));
        Assert.That(cards.Any(card => card.StableId == "opponent_secret"), Is.False);
        Assert.That(cards.Single(card => card.StableId == "own_hand").Zone,
            Is.EqualTo(RuntimeCardZone.OwnHand));
        Assert.That(cards.Single(card => card.StableId == "opponent_field").Zone,
            Is.EqualTo(RuntimeCardZone.OpponentField));
        Assert.That(cards.Single(card => card.StableId == "own_commit").Zone,
            Is.EqualTo(RuntimeCardZone.OwnCommitQueue));
        Assert.That(cards.Single(card => card.StableId == "own_cloud").Zone,
            Is.EqualTo(RuntimeCardZone.OwnCloudStack));
    }

    [Test]
    public void MissingCatalogDefinitionUsesVisibleFallbackWithoutInventingStatsOrRules()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                Hand = new[] { Card("missing_card", 41, 0) },
            },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });

        var card = RuntimeCardDisplayModel.BuildVisibleCards(
            snapshot,
            Catalog())[0];
        var inspect = RuntimeCardInspectModel.Build(card);
        var debugInspect = RuntimeCardInspectModel.BuildDebug(card);

        Assert.That(card.DefinitionAvailable, Is.False);
        Assert.That(card.Name, Is.EqualTo(RuntimeCardDisplayModel.UnknownCard));
        Assert.That(card.StableId, Is.EqualTo("missing_card"));
        Assert.That(card.PrintedAttack, Is.Null);
        Assert.That(card.PrintedHealth, Is.Null);
        Assert.That(card.CurrentAttack, Is.Null);
        Assert.That(card.CurrentHealth, Is.Null);
        Assert.That(card.RulesText, Is.EqualTo(RuntimeCardDisplayModel.Unavailable));
        Assert.That(card.MissingDataText,
            Is.EqualTo("CARD DATA Unavailable · STABLE ID missing_card"));
        Assert.That(inspect.DetailText, Does.Not.Contain("CARD DATA"));
        Assert.That(inspect.DetailText, Does.Not.Contain("missing_card"));
        Assert.That(inspect.DetailText, Does.Not.Contain("ENTITY"));
        Assert.That(inspect.DetailText, Does.Not.Contain("ZONE"));
        Assert.That(inspect.PrintedStatsLine, Is.Empty);
        Assert.That(inspect.CurrentStatsLine, Is.Empty);
        Assert.That(inspect.DetailText, Does.Not.Contain("CURRENT ATK"));
        Assert.That(debugInspect.DetailText, Does.Contain("CARD DATA Unavailable"));
        Assert.That(debugInspect.DetailText, Does.Contain("STABLE ID missing_card"));
    }

    [Test]
    public void CatalogValuesArePrintedWhileCurrentStatsComeOnlyFromSnapshot()
    {
        var definition = Definition(
            "readable_unit",
            "Readable Unit",
            faction: "机械遗迹",
            type: "MINION",
            attack: 4,
            health: 5,
            cost: 2,
            punish: 3,
            text: "登场：你抽1张牌。",
            isMinion: true,
            keywords: new[] { "圣盾", "嘲讽" },
            tags: new[] { "守卫" },
            commitCost: 1,
            uploadCost: 2,
            downloadCost: 3);
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                Field = new[]
                {
                    Card("readable_unit", 51, 0, sealedCard: true, currentAttack: 2, currentHealth: 3),
                },
            },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });

        var card = RuntimeCardDisplayModel.BuildVisibleCards(
            snapshot,
            Catalog(definition))[0];
        var inspect = RuntimeCardInspectModel.FromCard(card);

        Assert.That(card.DefinitionAvailable, Is.True);
        Assert.That(card.Name, Is.EqualTo("Readable Unit"));
        Assert.That(card.Type, Is.EqualTo("MINION"));
        Assert.That(card.Faction, Is.EqualTo("机械遗迹"));
        Assert.That(card.PrintedAttack, Is.EqualTo(4));
        Assert.That(card.PrintedHealth, Is.EqualTo(5));
        Assert.That(card.CurrentAttack, Is.EqualTo(2));
        Assert.That(card.CurrentHealth, Is.EqualTo(3));
        Assert.That(card.PrintedPunish, Is.EqualTo(3));
        Assert.That(card.DeclaredCost, Is.EqualTo(2));
        Assert.That(card.CommitCost, Is.EqualTo(1));
        Assert.That(card.UploadCost, Is.EqualTo(2));
        Assert.That(card.DownloadCost, Is.EqualTo(3));
        Assert.That(card.IsSealed, Is.True);
        Assert.That(card.RulesText, Is.EqualTo("登场：你抽1张牌。"));
        Assert.That(card.Keywords, Is.EqualTo(new[] { "圣盾", "嘲讽" }));
        Assert.That(card.Tags, Is.EqualTo(new[] { "守卫" }));
        Assert.That(inspect.PrintedStatsLine, Is.EqualTo("PRINTED ATK 4 | HP 5"));
        Assert.That(inspect.CurrentStatsLine,
            Is.EqualTo("CURRENT ATK 2 | HP 3"));
        Assert.That(inspect.PunishAndCostLine,
            Is.EqualTo("PRINTED PUNISH 3"));
        Assert.That(inspect.DiagnosticPunishAndCostLine,
            Is.EqualTo("PRINTED PUNISH 3 | COST 2"));
        Assert.That(inspect.MechanicalFeesLine,
            Is.EqualTo("COMMIT 1 | UPLOAD 2 | DOWNLOAD 3"));
        Assert.That(inspect.RulesText, Is.EqualTo("登场：你抽1张牌。"));
        Assert.That(inspect.KeywordsLine, Is.EqualTo("KEYWORDS 圣盾 · 嘲讽"));
        Assert.That(inspect.DetailText, Does.Not.Contain("CARD DATA"));
        Assert.That(inspect.DetailText, Does.Not.Contain("readable_unit"));
        Assert.That(inspect.DetailText, Does.Not.Contain("ENTITY"));
        Assert.That(inspect.DetailText, Does.Not.Contain("ZONE"));
        Assert.That(inspect.DetailText, Does.Not.Contain("COST"));
        Assert.That(RuntimeCardInspectModel.BuildDebug(card).DetailText,
            Does.Contain("CARD DATA OK · PRINTED VALUES ONLY"));
        Assert.That(RuntimeCardInspectModel.BuildDebug(card).DetailText,
            Does.Contain("COST 2"));
    }

    [Test]
    public void CardCatalogPresentationMetadataCopiesPrintedValuesWithoutExposingDefinition()
    {
        var catalog = Catalog(Definition(
            "metadata_unit",
            "Metadata Unit",
            faction: "深海联盟",
            type: "MINION",
            attack: 6,
            health: 7,
            cost: 4,
            punish: 2,
            isMinion: true,
            keywords: new[] { "吸血" },
            tags: new[] { "测试" },
            commitCost: 1,
            uploadCost: 2,
            downloadCost: 3));

        var found = catalog.TryGetPresentationMetadata(
            "metadata_unit",
            out var metadata);

        Assert.That(found, Is.True);
        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata!.Id, Is.EqualTo("metadata_unit"));
        Assert.That(metadata.Name, Is.EqualTo("Metadata Unit"));
        Assert.That(metadata.Type, Is.EqualTo("MINION"));
        Assert.That(metadata.Faction, Is.EqualTo("深海联盟"));
        Assert.That(metadata.PrintedAttack, Is.EqualTo(6));
        Assert.That(metadata.PrintedHealth, Is.EqualTo(7));
        Assert.That(metadata.DeclaredCost, Is.EqualTo(4));
        Assert.That(metadata.PrintedPunish, Is.EqualTo(2));
        Assert.That(metadata.CommitCost, Is.EqualTo(1));
        Assert.That(metadata.UploadCost, Is.EqualTo(2));
        Assert.That(metadata.DownloadCost, Is.EqualTo(3));
        Assert.That(metadata.HasCommitCost, Is.True);
        Assert.That(metadata.HasUploadCost, Is.True);
        Assert.That(metadata.HasDownloadCost, Is.True);
        Assert.That(metadata.Keywords, Is.EqualTo(new[] { "吸血" }));
        Assert.That(metadata.Tags, Is.EqualTo(new[] { "测试" }));
    }

    [Test]
    public void MechanicalFeesRequireExplicitPresenceAndRetainExplicitZero()
    {
        var noFees = RuntimeCardDisplayModel.BuildVisibleCards(
            Snapshot(
                new RuntimePlayerSnapshot
                {
                    PlayerId = "player_0",
                    Hand = new[] { Card("mechanical_no_fees", 201, 0) },
                },
                new RuntimePlayerSnapshot { PlayerId = "player_1" }),
            Catalog(Definition("mechanical_no_fees", "No Fees", faction: "机械遗迹")))[0];
        Assert.That(noFees.HasCommitCost, Is.False);
        Assert.That(noFees.MechanicalFeesLine, Is.Empty);
        Assert.That(RuntimeCardInspectModel.Build(noFees).DetailText, Does.Not.Contain("COMMIT"));

        var explicitZero = RuntimeCardDisplayModel.BuildVisibleCards(
            Snapshot(
                new RuntimePlayerSnapshot
                {
                    PlayerId = "player_0",
                    Hand = new[] { Card("mechanical_zero", 202, 0) },
                },
                new RuntimePlayerSnapshot { PlayerId = "player_1" }),
            CatalogFromJson(
                "[{\"id\":\"mechanical_zero\",\"name\":\"Explicit Zero\",\"faction\":\"机械遗迹\",\"type\":\"SPELL\",\"commitCost\":0,\"text\":\"\"}]"))[0];
        Assert.That(explicitZero.HasCommitCost, Is.True);
        Assert.That(explicitZero.CommitCost, Is.EqualTo(0));
        Assert.That(explicitZero.MechanicalFeesLine, Is.EqualTo("COMMIT 0"));
        Assert.That(RuntimeCardInspectModel.Build(explicitZero).DetailText,
            Does.Contain("COMMIT 0"));
        Assert.That(RuntimeCardInspectModel.BuildDebug(explicitZero).DetailText,
            Does.Contain("UPLOAD —"));
    }

    [Test]
    public void NonMinionStatsAreNotProjectedEvenIfSnapshotCarriesNumbers()
    {
        var card = RuntimeCardDisplayModel.BuildVisibleCards(
            Snapshot(
                new RuntimePlayerSnapshot
                {
                    PlayerId = "player_0",
                    Field = new[]
                    {
                        Card("spell_with_fake_stats", 203, 0, currentAttack: 8, currentHealth: 9),
                    },
                },
                new RuntimePlayerSnapshot { PlayerId = "player_1" }),
            Catalog(Definition("spell_with_fake_stats", "Spell", type: "SPELL", isMinion: false)))[0];
        Assert.That(card.IsMinion, Is.False);
        Assert.That(card.PrintedAttack, Is.Null);
        Assert.That(card.PrintedHealth, Is.Null);
        Assert.That(card.CurrentAttack, Is.Null);
        Assert.That(card.CurrentHealth, Is.Null);
        Assert.That(card.PrintedStatsLine, Is.Empty);
        Assert.That(card.CurrentStatsLine, Is.Empty);
        var inspect = RuntimeCardInspectModel.Build(card);
        Assert.That(inspect.DetailText, Does.Not.Contain("ATK"));
        Assert.That(inspect.DetailText, Does.Not.Contain("HP"));
    }

    [Test]
    public void InvalidViewerIdDoesNotExposeAnyHandButRetainsPublicFieldCards()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                Hand = new[] { Card("hidden_a", 61, 0) },
                Field = new[] { Card("public_a", 62, 0) },
            },
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_1",
                Hand = new[] { Card("hidden_b", 63, 1) },
                Field = new[] { Card("public_b", 64, 1) },
            });
        snapshot.ViewerPlayerId = "malformed_viewer";

        var cards = RuntimeCardDisplayModel.BuildVisibleCards(snapshot, Catalog(
            Definition("hidden_a", "Hidden A"),
            Definition("hidden_b", "Hidden B"),
            Definition("public_a", "Public A"),
            Definition("public_b", "Public B")));

        Assert.That(cards.Select(card => card.StableId), Is.EqualTo(new[] { "public_a", "public_b" }));
    }

    [Test]
    public void DuplicateViewerIdsExposeOnlyTheFirstMatchingHand()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                Hand = new[] { Card("own_hand", 65, 0) },
            },
            new RuntimePlayerSnapshot
            {
                // A malformed duplicate identity must not turn the second
                // player's hidden hand into viewer-visible data.
                PlayerId = "player_0",
                Hand = new[] { Card("hidden_duplicate", 66, 1) },
                Field = new[] { Card("public_duplicate", 67, 1) },
            });

        var cards = RuntimeCardDisplayModel.BuildVisibleCards(
            snapshot,
            Catalog(
                Definition("own_hand", "Own Hand"),
                Definition("hidden_duplicate", "Hidden Duplicate"),
                Definition("public_duplicate", "Public Duplicate")));

        Assert.That(cards.Select(card => card.StableId), Is.EqualTo(new[]
        {
            "own_hand", "public_duplicate",
        }));
    }

    [Test]
    public void InspectInteractionEmitsHoverAndClickAndPinsOnlyOnClick()
    {
        var card = RuntimeCardDisplayModel.BuildVisibleCards(
            Snapshot(
                new RuntimePlayerSnapshot
                {
                    PlayerId = "player_0",
                    Hand = new[] { Card("unit", 71, 0) },
                },
                new RuntimePlayerSnapshot { PlayerId = "player_1" }),
            Catalog(Definition("unit", "Unit")))[0];
        var model = RuntimeCardInspectModel.Build(card);
        var root = new GameObject(
            "CardInspectTest",
            typeof(RectTransform),
            typeof(UnityEngine.UI.Image));

        try
        {
            var interaction = RuntimeCardInspectInteraction.Attach(
                root.GetComponent<RectTransform>()!,
                model);
            Assert.That(root.GetComponent<UnityEngine.UI.Image>()!.raycastTarget, Is.True);
            var triggers = new List<RuntimeCardInspectTrigger>();
            var closeCount = 0;
            interaction.InspectionRequested += (_, trigger) => triggers.Add(trigger);
            interaction.InspectionClosed += () => closeCount++;
            interaction.Bind(model);

            interaction.OnPointerEnter(null!);
            Assert.That(interaction.IsOpen, Is.True);
            Assert.That(interaction.IsPinned, Is.False);
            Assert.That(triggers, Is.EqualTo(new[] { RuntimeCardInspectTrigger.Hover }));

            interaction.OnPointerExit(null!);
            Assert.That(interaction.IsOpen, Is.False);
            Assert.That(closeCount, Is.EqualTo(1));

            interaction.OnPointerClick(null!);
            Assert.That(interaction.IsOpen, Is.True);
            Assert.That(interaction.IsPinned, Is.True);
            Assert.That(triggers, Is.EqualTo(new[]
            {
                RuntimeCardInspectTrigger.Hover,
                RuntimeCardInspectTrigger.Click,
            }));

            interaction.OnPointerExit(null!);
            Assert.That(interaction.IsOpen, Is.True);
            Assert.That(closeCount, Is.EqualTo(1));

            interaction.OnPointerClick(null!);
            Assert.That(interaction.IsPinned, Is.False);
            Assert.That(interaction.IsOpen, Is.False);
            Assert.That(closeCount, Is.EqualTo(2));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static CardCatalog Catalog(params CardDefinition[] definitions)
    {
        var cards = new Dictionary<string, CardDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions) cards.Add(definition.Id, definition);
        return new CardCatalog(cards);
    }

    private static CardDefinition Definition(
        string id,
        string name,
        string faction = "烈焰帝国",
        string type = "SPELL",
        int attack = 0,
        int health = 1,
        int cost = 0,
        int punish = 0,
        string text = "",
        bool isMinion = false,
        bool isLeader = false,
        IEnumerable<string>? keywords = null,
        IEnumerable<string>? tags = null,
        int commitCost = 0,
        int uploadCost = 0,
        int downloadCost = 0)
    {
        return new CardDefinition(
            id,
            name,
            attack,
            health,
            isMinion,
            isLeader,
            faction: faction,
            text: text,
            cost: cost,
            punish: punish,
            keywords: keywords,
            tags: tags,
            type: type,
            commitCost: commitCost,
            uploadCost: uploadCost,
            downloadCost: downloadCost);
    }

    private static CardCatalog CatalogFromJson(string json)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "dw-card-display-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "cards.json"), json);
            return CardCatalog.LoadDirectory(root);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static RuntimeCardSnapshot Card(
        string cardId,
        long entityId,
        int owner,
        bool sealedCard = false,
        int? currentAttack = null,
        int? currentHealth = null)
    {
        return new RuntimeCardSnapshot
        {
            CardId = cardId,
            EntityId = entityId,
            OwnerPlayer = owner,
            Sealed = sealedCard,
            CurrentAttack = currentAttack,
            CurrentHealth = currentHealth,
        };
    }

    private static RuntimeSnapshotEnvelope Snapshot(
        RuntimePlayerSnapshot own,
        RuntimePlayerSnapshot opponent)
    {
        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = "match_card_display_test",
            SnapshotRevision = 1,
            Turn = 1,
            Phase = "ACTION",
            CurrentPlayer = 0,
            ViewerPlayerId = "player_0",
            Players = new[] { own, opponent },
            Castle = new RuntimeCastleSnapshot { Enabled = true, Health = 75 },
            LegalActions = Array.Empty<RuntimeLegalAction>(),
        };
    }
}
}
#endif
