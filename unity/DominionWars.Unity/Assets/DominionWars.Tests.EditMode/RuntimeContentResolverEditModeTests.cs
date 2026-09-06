using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using DominionWars.Unity.Runtime;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeContentResolverEditModeTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "dw-runtime-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "manifests"));
        Directory.CreateDirectory(Path.Combine(_root, "authored", "card_art"));
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
    public void ResolvesDirectAliasAndFallbackIds()
    {
        var bytes = Encoding.UTF8.GetBytes("not-a-texture-but-a-verified-file");
        var relativePath = "authored/card_art/direct.bin";
        File.WriteAllBytes(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)), bytes);
        WriteManifest(
            "{\"id\":\"direct_card\",\"kind\":\"card_art\",\"sourceType\":\"file\",\"relativePath\":\"" + relativePath + "\",\"sha256\":\"" + Hash(bytes) + "\",\"fallbackId\":\"placeholder_card\",\"status\":\"approved\"}," +
            "{\"id\":\"placeholder_card\",\"kind\":\"card_art\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"approved\"}",
            "{\"fromId\":\"legacy_card\",\"toId\":\"direct_card\"}");

        var resolver = RuntimeContentResolver.Load(_root);
        var direct = resolver.Resolve("direct_card");
        var alias = resolver.Resolve("legacy_card");

        Assert.That(direct.CanonicalId, Is.EqualTo("direct_card"));
        Assert.That(direct.FallbackId, Is.EqualTo("placeholder_card"));
        Assert.That(direct.HasFallback, Is.True);
        Assert.That(alias.CanonicalId, Is.EqualTo("direct_card"));
        Assert.That(alias.UsedAlias, Is.True);
        Assert.That(alias.FallbackAsset.Id, Is.EqualTo("placeholder_card"));
    }

    [Test]
    public void CardArtUsesAndCachesProgrammaticFallbackWhenDecodeFails()
    {
        var bytes = Encoding.UTF8.GetBytes("not-a-texture");
        var relativePath = "authored/card_art/direct.bin";
        File.WriteAllBytes(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)), bytes);
        WriteManifest(
            "{\"id\":\"direct_card\",\"kind\":\"card_art\",\"sourceType\":\"file\",\"relativePath\":\"" + relativePath + "\",\"sha256\":\"" + Hash(bytes) + "\",\"fallbackId\":\"placeholder_card\",\"status\":\"approved\"}," +
            "{\"id\":\"placeholder_card\",\"kind\":\"card_art\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"approved\"}");

        var resolver = RuntimeContentResolver.Load(_root);
        var first = resolver.GetCardArt("direct_card");
        var second = resolver.GetCardArt("direct_card");

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.SameAs(first));
        Assert.That(resolver.TextureCacheCount, Is.EqualTo(1));
        Assert.That(HasDiagnostic(resolver, "texture_decode_failed"), Is.True);
    }

    [Test]
    public void MissingManifestFailsClosedButUnavailableResolverStillProvidesPlaceholder()
    {
        var loaded = RuntimeContentResolver.TryLoad(_root, out var resolver);

        Assert.That(loaded, Is.False);
        Assert.That(resolver.IsAvailable, Is.False);
        var first = resolver.GetCardArt("missing_card");
        var second = resolver.GetCardArt("missing_card");
        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.SameAs(first));
        Assert.That(HasDiagnostic(resolver, "manifest_load_failed"), Is.True);
    }

    [Test]
    public void HashMismatchFailsClosed()
    {
        var relativePath = "authored/card_art/hash.bin";
        File.WriteAllText(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)), "actual");
        WriteManifest(
            "{\"id\":\"hash_card\",\"kind\":\"card_art\",\"sourceType\":\"file\",\"relativePath\":\"" + relativePath + "\",\"sha256\":\"" + new string('0', 64) + "\",\"fallbackId\":null,\"status\":\"approved\"}");

        var loaded = RuntimeContentResolver.TryLoad(_root, out var resolver);

        Assert.That(loaded, Is.False);
        Assert.That(resolver.IsAvailable, Is.False);
        Assert.That(resolver.Diagnostics[0].Message, Does.Contain("sha256 mismatch"));
    }

    [Test]
    public void UnsafePathFailsClosed()
    {
        WriteManifest(
            "{\"id\":\"unsafe_card\",\"kind\":\"card_art\",\"sourceType\":\"file\",\"relativePath\":\"../outside.png\",\"sha256\":\"" + new string('0', 64) + "\",\"fallbackId\":null,\"status\":\"approved\"}");

        var loaded = RuntimeContentResolver.TryLoad(_root, out var resolver);

        Assert.That(loaded, Is.False);
        Assert.That(resolver.IsAvailable, Is.False);
        Assert.That(resolver.Diagnostics[0].Message, Does.Contain("unsafe relativePath"));
    }

    [Test]
    public void ProgrammaticAudioHasNoFileAndDoesNotThrow()
    {
        WriteManifest(
            "{\"id\":\"ui_click\",\"kind\":\"audio\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"approved\"}");

        var resolver = RuntimeContentResolver.Load(_root);
        var result = resolver.TryReadBytes("ui_click", out var bytes);

        Assert.That(result, Is.False);
        Assert.That(bytes, Is.Empty);
        Assert.That(resolver.IsAvailable, Is.True);
    }

    [Test]
    public void LoadsDefaultAndTestSkinsAndResolvesRoleAndCardArtworkOverrides()
    {
        WriteManifest(
            "{\"id\":\"card_art_default\",\"kind\":\"card_art\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"approved\"},"
            + "{\"id\":\"card_art_test\",\"kind\":\"card_art\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"approved\"},"
            + Asset("board_default", "board") + ","
            + Asset("board_test", "board") + ","
            + Asset("card_back_default", "card_back") + ","
            + Asset("card_back_test", "card_back") + ","
            + Asset("castle_default", "castle") + ","
            + Asset("castle_test", "castle") + ","
            + Asset("leader_frame_default", "leader") + ","
            + Asset("leader_frame_test", "leader") + ","
            + Asset("faction_frame_default", "faction_frame") + ","
            + Asset("faction_frame_test", "faction_frame") + ","
            + Asset("ui_icon_default", "ui_icon") + ","
            + Asset("ui_icon_test", "ui_icon"));
        var skinsDirectory = Path.Combine(_root, "manifests", "skins");
        Directory.CreateDirectory(skinsDirectory);
        File.WriteAllText(
            Path.Combine(skinsDirectory, "default.json"),
            "{\"skinId\":\"default\",\"version\":\"1.0.0\",\"theme\":\"theme_default\",\"assets\":{\"board\":\"board_default\",\"cardBack\":\"card_back_default\",\"castle\":\"castle_default\",\"leaderFrame\":\"leader_frame_default\",\"factionFrame\":\"faction_frame_default\",\"uiIcon\":\"ui_icon_default\"}}",
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(skinsDirectory, "test.json"),
            "{\"skinId\":\"test\",\"version\":\"1.0.0\",\"theme\":\"theme_test\",\"assets\":{\"board\":\"board_test\",\"cardBack\":\"card_back_test\",\"castle\":\"castle_test\",\"leaderFrame\":\"leader_frame_test\",\"factionFrame\":\"faction_frame_test\",\"uiIcon\":\"ui_icon_test\"},\"cardArtwork\":{\"test_card\":\"card_art_test\"}}",
            Encoding.UTF8);

        var resolver = RuntimeContentResolver.Load(_root);
        Assert.That(resolver.SkinCatalog, Is.Not.Null);
        Assert.That(resolver.SkinCatalog!.Skins.Keys, Is.EqualTo(new[] { "default", "test" }));

        Assert.That(resolver.TryResolveSkinAsset("default", "board", out var defaultBoard), Is.True);
        Assert.That(defaultBoard.CanonicalId, Is.EqualTo("board_default"));
        Assert.That(resolver.TryResolveSkinAsset("test", "board", out var testBoard), Is.True);
        Assert.That(testBoard.CanonicalId, Is.EqualTo("board_test"));
        Assert.That(resolver.TryGetSkinTexture("test", "uiIcon", out var icon), Is.True);
        Assert.That(icon, Is.Not.Null);
        Assert.That(resolver.GetCardArtForSkin("test", "test_card", "card_art_default"), Is.Not.Null);
        Assert.That(resolver.TextureCacheCount, Is.EqualTo(2));
    }

    private void WriteManifest(string assets, string aliases = "")
    {
        var aliasSection = string.IsNullOrEmpty(aliases) ? "[]" : "[" + aliases + "]";
        var json = "{\"manifestVersion\":1,\"schemaVersion\":\"1.0.0\",\"contentVersion\":\"1.0.0\",\"assets\":[" + assets + "],\"aliases\":" + aliasSection + "}";
        File.WriteAllText(Path.Combine(_root, "manifests", "content.manifest.json"), json, Encoding.UTF8);
    }

    private static string Asset(string id, string kind)
    {
        return "{\"id\":\"" + id + "\",\"kind\":\"" + kind
            + "\",\"sourceType\":\"programmatic\",\"relativePath\":null,\"sha256\":null,\"fallbackId\":null,\"status\":\"approved\"}";
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

    private static bool HasDiagnostic(RuntimeContentResolver resolver, string code)
    {
        foreach (var diagnostic in resolver.Diagnostics)
        {
            if (diagnostic.Code == code) return true;
        }

        return false;
    }
}
}
