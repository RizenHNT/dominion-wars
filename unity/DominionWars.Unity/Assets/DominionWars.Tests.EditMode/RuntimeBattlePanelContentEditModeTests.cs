#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DominionWars.Adapters;
using DominionWars.Data;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeBattlePanelContentEditModeTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "dw-panel-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "manifests"));
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Test]
    public void KnownArtIdAndAliasReplaceCardPlaceholderWithoutChangingCardFrame()
    {
        WriteFileManifestWithKnownArt("legacy_card", "card_art_known");
        var resolver = RuntimeContentResolver.Load(_root);
        var cardCatalog = WriteCardCatalog("known_card", "legacy_card");
        var snapshot = PanelSnapshot("known_card", "secret_card");
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject("RuntimeBattlePanelContentTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);
            panel.BindContentResolver(resolver, cardCatalog);

            var ownCard = panelObject.transform.Find(
                "RuntimeBattlePanelContent/RuntimeBattlePanelBoard/RuntimeBattlePanelMainBattle/RuntimeBattlePanelOwn/OwnHandFaceUp/OwnCard_0");
            Assert.That(ownCard, Is.Not.Null);
            Assert.That(ownCard!.GetComponent<UnityEngine.UI.Image>(), Is.Not.Null);
            var cardArt = ownCard.Find("CardArtPanel/CardArt")?.GetComponent<UnityEngine.UI.RawImage>();
            Assert.That(cardArt, Is.Not.Null);
            Assert.That(cardArt!.texture, Is.Not.Null);
            Assert.That(cardArt.texture.name, Is.EqualTo("RuntimeContent_card_art_known"));
            Assert.That(cardArt.transform.parent, Is.SameAs(ownCard.Find("CardArtPanel")));
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void UnknownCardUsesVisibleFallbackAndHiddenOpponentHandHasNoArt()
    {
        WriteProgrammaticManifest("placeholder_card", string.Empty, string.Empty);
        var resolver = RuntimeContentResolver.Load(_root);
        var snapshot = PanelSnapshot("unknown_card", "secret_card");
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject("RuntimeBattlePanelFallbackTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);
            panel.BindContentResolver(resolver);

            var ownCard = panelObject.transform.Find(
                "RuntimeBattlePanelContent/RuntimeBattlePanelBoard/RuntimeBattlePanelMainBattle/RuntimeBattlePanelOwn/OwnHandFaceUp/OwnCard_0");
            Assert.That(ownCard, Is.Not.Null);
            var cardArt = ownCard!.Find("CardArtPanel/CardArt")?.GetComponent<UnityEngine.UI.RawImage>();
            Assert.That(cardArt, Is.Not.Null);
            Assert.That(cardArt!.texture, Is.Not.Null);
            Assert.That(cardArt.transform.parent.name, Is.EqualTo("CardArtPanel"));
            Assert.That(panelObject.GetComponentsInChildren<UnityEngine.UI.Image>(true), Has.Some.Property("gameObject").Property("name").EqualTo("OpponentHandBack_0"));
            foreach (var label in panelObject.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                Assert.That(label.text, Does.Not.Contain("secret_card"));
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void UnavailableResolverDoesNotPreventPanelFromRendering()
    {
        var loaded = RuntimeContentResolver.TryLoad(
            Path.Combine(_root, "missing"),
            out var resolver);
        Assert.That(loaded, Is.False);

        var snapshot = PanelSnapshot("unknown_card", "secret_card");
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject("RuntimeBattlePanelUnavailableContentTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            Assert.DoesNotThrow(() => panel.Bind(adapter));
            Assert.DoesNotThrow(() => panel.BindContentResolver(resolver));
            Assert.That(panel.ContentResolver, Is.SameAs(resolver));
            var ownCard = panelObject.transform.Find(
                "RuntimeBattlePanelContent/RuntimeBattlePanelBoard/RuntimeBattlePanelMainBattle/RuntimeBattlePanelOwn/OwnHandFaceUp/OwnCard_0");
            Assert.That(ownCard, Is.Not.Null);
            var cardArt = ownCard!.Find("CardArtPanel/CardArt")?.GetComponent<UnityEngine.UI.RawImage>();
            Assert.That(cardArt, Is.Not.Null);
            Assert.That(cardArt!.texture, Is.Not.Null);
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    private CardCatalog WriteCardCatalog(string cardId, string artId)
    {
        var cardsRoot = Path.Combine(_root, "cards");
        Directory.CreateDirectory(cardsRoot);
        var json = "[{\"id\":\"" + cardId + "\",\"name\":\"Known\",\"faction\":\"烈焰帝国\",\"type\":\"SPELL\",\"artId\":\"" + artId + "\",\"text\":\"\"}]";
        var file = Path.Combine(cardsRoot, "cards.json");
        File.WriteAllText(file, json, Encoding.UTF8);
        return CardCatalog.LoadDirectory(cardsRoot);
    }

    private void WriteProgrammaticManifest(string assetId, string aliasFrom, string aliasTo)
    {
        var aliases = string.IsNullOrEmpty(aliasFrom)
            ? "[]"
            : "[{\"fromId\":\"" + aliasFrom + "\",\"toId\":\"" + aliasTo + "\"}]";
        var asset = "{\"id\":\"" + assetId + "\",\"kind\":\"card_art\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"approved\"}";
        var json = "{\"manifestVersion\":1,\"schemaVersion\":\"1.0.0\",\"contentVersion\":\"1.0.0\",\"assets\":[" + asset + "],\"aliases\":" + aliases + "}";
        File.WriteAllText(Path.Combine(_root, "manifests", "content.manifest.json"), json, Encoding.UTF8);
    }

    private void WriteFileManifestWithKnownArt(string aliasFrom, string assetId)
    {
        var relativePath = "authored/card_art/known.png";
        var filePath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var bytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        File.WriteAllBytes(filePath, bytes);

        var asset = "{\"id\":\"" + assetId + "\",\"kind\":\"card_art\",\"sourceType\":\"file\",\"relativePath\":\"" + relativePath + "\",\"sha256\":\"" + Hash(bytes) + "\",\"fallbackId\":\"placeholder_card_art_known\",\"status\":\"approved\"}";
        var placeholder = "{\"id\":\"placeholder_card_art_known\",\"kind\":\"card_art\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"approved\"}";
        var aliases = "[{\"fromId\":\"" + aliasFrom + "\",\"toId\":\"" + assetId + "\"}]";
        var json = "{\"manifestVersion\":1,\"schemaVersion\":\"1.0.0\",\"contentVersion\":\"1.0.0\",\"assets\":[" + asset + "," + placeholder + "],\"aliases\":" + aliases + "}";
        File.WriteAllText(Path.Combine(_root, "manifests", "content.manifest.json"), json, Encoding.UTF8);
    }

    private static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create())
        {
            var digest = sha.ComputeHash(bytes);
            var builder = new StringBuilder(digest.Length * 2);
            foreach (var value in digest) builder.Append(value.ToString("x2"));
            return builder.ToString();
        }
    }

    private static RuntimeSnapshotEnvelope PanelSnapshot(string ownCardId, string opponentCardId)
    {
        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = "match_content_test",
            SnapshotRevision = 1,
            Turn = 1,
            Phase = "ACTION",
            CurrentPlayer = 0,
            ViewerPlayerId = "player_0",
            Players = new[]
            {
                new RuntimePlayerSnapshot
                {
                    PlayerId = "player_0",
                    HandCount = 1,
                    Hand = new[] { new RuntimeCardSnapshot { CardId = ownCardId, EntityId = 1, OwnerPlayer = 0 } },
                },
                new RuntimePlayerSnapshot
                {
                    PlayerId = "player_1",
                    HandCount = 1,
                    Hand = new[] { new RuntimeCardSnapshot { CardId = opponentCardId, EntityId = 2, OwnerPlayer = 1 } },
                },
            },
            Castle = new RuntimeCastleSnapshot { Enabled = true, Health = 75 },
            LegalActions = Array.Empty<RuntimeLegalAction>(),
        };
    }

    private sealed class SnapshotSession : IRuntimeSession
    {
        private readonly RuntimeSnapshotEnvelope _snapshot;

        public SnapshotSession(RuntimeSnapshotEnvelope snapshot)
        {
            _snapshot = snapshot;
        }

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
        {
            return _snapshot;
        }

        public RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            throw new NotSupportedException();
        }

        public void Close() { }
    }
}
}
