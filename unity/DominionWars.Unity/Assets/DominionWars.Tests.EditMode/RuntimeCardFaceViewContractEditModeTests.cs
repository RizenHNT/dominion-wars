#if UNITY_INCLUDE_TESTS
#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using DominionWars.Adapters;
using DominionWars.Data;
using DominionWars.Engine.Model;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeCardFaceViewContractEditModeTests
{
    private string _contentRoot = string.Empty;
    private RuntimeContentResolver? _resolver;

    [SetUp]
    public void SetUp()
    {
        _contentRoot = Path.Combine(
            Path.GetTempPath(),
            "dw-card-face-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_contentRoot, "manifests"));
    }

    [TearDown]
    public void TearDown()
    {
        _resolver?.ClearTextureCache();
        _resolver = null;
        try
        {
            if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Test]
    public void FullFaceBuildsReadableSemanticLayersAndNonZeroLayout()
    {
        GameObject? root = null;
        try
        {
            root = new GameObject("CardFaceContractRoot", typeof(RectTransform));
            var face = RuntimeCardFaceView.Build(
                root.GetComponent<RectTransform>()!,
                "ReadableCard",
                RuntimeCardFaceMode.Full);
            face.Bind(Model(
                RuntimeCardZone.OwnHand,
                new RuntimeCardSnapshot
                {
                    CardId = "readable_card",
                    EntityId = 101,
                    OwnerPlayer = 0,
                }));

            Assert.That(face.TitleText.text, Is.EqualTo("Readable Card"));
            Assert.That(face.MetaText.text, Does.Contain("MINION"));
            Assert.That(face.MetaText.text, Does.Contain("机械遗迹"));
            Assert.That(face.CostBadge.gameObject.activeSelf, Is.False,
                "The legacy standalone COST badge is not a player-facing field.");
            Assert.That(face.CostLabel.text, Is.EqualTo("COST"));
            Assert.That(face.CostValue.text, Is.EqualTo("3"));
            Assert.That(face.PunishLabel.text, Is.EqualTo("PUNISH"));
            Assert.That(face.PunishValue.text, Is.EqualTo("2"));
            Assert.That(face.RulesText.text, Does.Contain("RULES"));
            Assert.That(face.RulesText.text, Does.Contain("获得强化"));
            Assert.That(face.AttackLabel.text, Is.EqualTo("ATK"));
            Assert.That(face.AttackValue.text, Is.EqualTo("4"));
            Assert.That(face.HealthLabel.text, Is.EqualTo("HP"));
            Assert.That(face.HealthValue.text, Is.EqualTo("5"));
            Assert.That(face.IdentityText.gameObject.activeSelf, Is.False,
                "Card identity is diagnostic-only on the normal face.");
            face.SetDiagnosticsVisible(true);
            Assert.That(face.CostBadge.gameObject.activeSelf, Is.True);
            Assert.That(face.IdentityText.gameObject.activeSelf, Is.True);
            Assert.That(face.IdentityText.text, Does.Contain("ID readable_card"));
            Assert.That(face.IdentityText.text, Does.Contain("Own hand"));
            Assert.That(face.ArtPanel.anchorMax.x - face.ArtPanel.anchorMin.x, Is.GreaterThan(0f));
            Assert.That(face.ArtPanel.anchorMax.y - face.ArtPanel.anchorMin.y, Is.GreaterThan(0f));

            var layout = face.GetComponent<UnityEngine.UI.LayoutElement>();
            Assert.That(layout, Is.Not.Null);
            Assert.That(layout!.preferredWidth, Is.EqualTo(RuntimeCardFaceView.FullWidth));
            Assert.That(layout.preferredHeight, Is.EqualTo(RuntimeCardFaceView.FullHeight));
            Assert.That(face.transform.Find("CardArtPanel"), Is.Not.Null);
            Assert.That(face.transform.Find("CardRulesPanel"), Is.Not.Null);
            Assert.That(face.transform.Find("CardStats/AttackBadge"), Is.Not.Null);
            Assert.That(face.transform.Find("CardStats/HealthBadge"), Is.Not.Null);
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void CompactFaceRetainsRuleSummaryAndUsesZoneAppropriateStats()
    {
        GameObject? root = null;
        try
        {
            root = new GameObject("CompactCardFaceContractRoot", typeof(RectTransform));
            var hand = RuntimeCardFaceView.Build(
                root.GetComponent<RectTransform>()!,
                "CompactHandCard",
                RuntimeCardFaceMode.Compact);
            hand.Bind(Model(
                RuntimeCardZone.OwnHand,
                new RuntimeCardSnapshot
                {
                    CardId = "readable_card",
                    EntityId = 102,
                    OwnerPlayer = 0,
                }));

            Assert.That(hand.Mode, Is.EqualTo(RuntimeCardFaceMode.Compact));
            Assert.That(hand.RulesText.gameObject.activeInHierarchy, Is.True);
            Assert.That(hand.RulesText.text, Does.Contain("获得强化"));
            Assert.That(hand.AttackValue.text, Is.EqualTo("4"));
            Assert.That(hand.HealthValue.text, Is.EqualTo("5"));
            var compactLayout = hand.GetComponent<UnityEngine.UI.LayoutElement>();
            Assert.That(compactLayout!.preferredWidth, Is.EqualTo(RuntimeCardFaceView.CompactWidth));
            Assert.That(compactLayout.preferredHeight, Is.EqualTo(RuntimeCardFaceView.CompactHeight));

            var field = RuntimeCardFaceView.Build(
                root.GetComponent<RectTransform>()!,
                "CompactFieldCard",
                RuntimeCardFaceMode.Compact);
            field.Bind(Model(
                RuntimeCardZone.OwnField,
                new RuntimeCardSnapshot
                {
                    CardId = "readable_card",
                    EntityId = 103,
                    OwnerPlayer = 0,
                    CurrentAttack = 2,
                    CurrentHealth = 3,
                }));
            Assert.That(field.AttackValue.text, Is.EqualTo("2"));
            Assert.That(field.HealthValue.text, Is.EqualTo("3"));
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ResolverUsesStableArtIdAndMissingArtKeepsVisibleFallback()
    {
        WriteProgrammaticManifest("card_art_readable");
        _resolver = RuntimeContentResolver.Load(_contentRoot);

        GameObject? root = null;
        try
        {
            root = new GameObject("CardFaceArtContractRoot", typeof(RectTransform));
            var face = RuntimeCardFaceView.Build(
                root.GetComponent<RectTransform>()!,
                "ArtCard",
                RuntimeCardFaceMode.Full);
            face.Bind(Model(
                RuntimeCardZone.OwnHand,
                new RuntimeCardSnapshot
                {
                    CardId = "readable_card",
                    EntityId = 104,
                    OwnerPlayer = 0,
                },
                "card_art_readable"),
                _resolver);

            Assert.That(face.ArtImage.texture, Is.Not.Null);
            Assert.That(face.ArtImage.texture!.name, Is.EqualTo("RuntimeContent_card_art_readable"));
            Assert.That(face.IsUsingArtFallback, Is.False);

            var missing = RuntimeCardFaceView.Build(
                root.GetComponent<RectTransform>()!,
                "MissingArtCard",
                RuntimeCardFaceMode.Full);
            missing.Bind(Model(
                RuntimeCardZone.OwnHand,
                new RuntimeCardSnapshot
                {
                    CardId = "missing_art_card",
                    EntityId = 105,
                    OwnerPlayer = 0,
                },
                "missing_art_id"),
                _resolver);

            Assert.That(missing.ArtImage.texture, Is.Not.Null,
                "Resolver must return a generated art texture when a file or ID is missing.");
            Assert.That(missing.IsUsingArtFallback, Is.True);
            Assert.That(missing.ArtFallbackText.gameObject.activeSelf, Is.False,
                "Placeholder diagnostics are hidden on the normal face.");
            missing.SetDiagnosticsVisible(true);
            Assert.That(missing.ArtFallbackText.gameObject.activeSelf, Is.True);
            Assert.That(missing.ArtFallbackText.text, Does.Contain("PLACEHOLDER"));
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void InteractionStateOnlyChangesVisualsAndChildGraphicsDoNotSwallowInput()
    {
        GameObject? root = null;
        try
        {
            root = new GameObject("CardFaceInputContractRoot", typeof(RectTransform));
            var face = RuntimeCardFaceView.Build(
                root.GetComponent<RectTransform>()!,
                "InputCard",
                RuntimeCardFaceMode.Compact);
            face.Bind(Model(
                RuntimeCardZone.OwnHand,
                new RuntimeCardSnapshot
                {
                    CardId = "readable_card",
                    EntityId = 106,
                    OwnerPlayer = 0,
                }));

            var rootImage = face.GetComponent<UnityEngine.UI.Image>();
            Assert.That(rootImage, Is.Not.Null);
            Assert.That(rootImage!.raycastTarget, Is.True);

            var graphics = face.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            foreach (var graphic in graphics)
            {
                if (graphic.gameObject == face.gameObject) continue;
                Assert.That(graphic.raycastTarget, Is.False, graphic.name + " must not block card input");
            }

            Assert.That(face.GetComponent<RuntimeBattleCardDrag>(), Is.Null);
            Assert.That(face.GetComponent<RuntimeBattleDropZone>(), Is.Null);
            face.SetInteractionState(true, false, true);
            Assert.That(face.InteractionMarker.gameObject.activeSelf, Is.True);
            Assert.That(face.InteractionMarkerText.text, Is.EqualTo("DRAG"));
            face.SetInteractionState(false, true, false);
            Assert.That(face.InteractionMarkerText.text, Is.EqualTo("TARGET"));
            Assert.That(face.DisabledVeil.gameObject.activeSelf, Is.True);
            Assert.That(face.DisabledVeil.GetComponent<UnityEngine.UI.Image>()!.raycastTarget, Is.False);
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private RuntimeCardDisplayModel Model(
        RuntimeCardZone zone,
        RuntimeCardSnapshot snapshot,
        string? artId = null)
    {
        var definition = new CardDefinition(
            "readable_card",
            "Readable Card",
            4,
            5,
            true,
            false,
            faction: "机械遗迹",
            text: "登场：获得强化。",
            cost: 3,
            artId: artId,
            keywords: new[] { "嘲讽" },
            tags: new[] { "守卫" },
            type: "MINION",
            punish: 2);
        var catalog = new CardCatalog(new Dictionary<string, CardDefinition>(StringComparer.Ordinal)
        {
            [definition.Id] = definition,
        });

        var own = new RuntimePlayerSnapshot { PlayerId = "player_0" };
        if (zone == RuntimeCardZone.OwnHand) own.Hand = new[] { snapshot };
        else if (zone == RuntimeCardZone.OwnField) own.Field = new[] { snapshot };
        else throw new ArgumentOutOfRangeException(nameof(zone));

        var envelope = new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = "card_face_contract_test",
            SnapshotRevision = 1,
            Turn = 1,
            Phase = "ACTION",
            CurrentPlayer = 0,
            ViewerPlayerId = "player_0",
            Players = new[]
            {
                own,
                new RuntimePlayerSnapshot { PlayerId = "player_1" },
            },
            Castle = new RuntimeCastleSnapshot { Enabled = true, Health = 75 },
            LegalActions = Array.Empty<RuntimeLegalAction>(),
        };
        return RuntimeCardDisplayModel.BuildVisibleCards(envelope, catalog)[0];
    }

    private void WriteProgrammaticManifest(string assetId)
    {
        var asset = "{\"id\":\"" + assetId + "\",\"kind\":\"card_art\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"approved\"}";
        var json = "{\"manifestVersion\":1,\"schemaVersion\":\"1.0.0\",\"contentVersion\":\"1.0.0\",\"assets\":[" + asset + "],\"aliases\":[]}";
        File.WriteAllText(
            Path.Combine(_contentRoot, "manifests", "content.manifest.json"),
            json);
    }
}
}
#endif
