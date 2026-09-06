#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DominionWars.Data;
using UnityEngine;

namespace DominionWars.Unity.Runtime
{

/// <summary>Severity for a runtime content diagnostic.</summary>
public enum RuntimeContentDiagnosticSeverity
{
    Info,
    Warning,
    Error,
    Blocking,
}

/// <summary>
/// A non-fatal runtime content observation. Content failures are kept here so
/// the presentation layer can report them without taking down gameplay.
/// </summary>
public sealed class RuntimeContentDiagnostic
{
    public RuntimeContentDiagnostic(
        RuntimeContentDiagnosticSeverity severity,
        string code,
        string message,
        string? assetId = null)
    {
        Severity = severity;
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        AssetId = assetId;
    }

    public RuntimeContentDiagnosticSeverity Severity { get; }
    public string Code { get; }
    public string Message { get; }
    public string? AssetId { get; }

    public override string ToString()
    {
        return AssetId is null
            ? Severity + " " + Code + ": " + Message
            : Severity + " " + Code + " [" + AssetId + "]: " + Message;
    }
}

/// <summary>
/// The result of resolving a requested stable ID. The asset itself is never a
/// file path in gameplay code; the path remains metadata owned by the catalog.
/// </summary>
public sealed class RuntimeResolvedAsset
{
    internal RuntimeResolvedAsset(
        string requestedId,
        string canonicalId,
        ContentAsset asset,
        string fallbackId,
        ContentAsset fallbackAsset)
    {
        RequestedId = requestedId;
        CanonicalId = canonicalId;
        Asset = asset;
        FallbackId = fallbackId;
        FallbackAsset = fallbackAsset;
    }

    public string RequestedId { get; }
    public string CanonicalId { get; }
    public ContentAsset Asset { get; }
    public string FallbackId { get; }
    public ContentAsset FallbackAsset { get; }
    public bool UsedAlias => !string.Equals(RequestedId, CanonicalId, StringComparison.Ordinal);
    public bool HasFallback => !string.Equals(CanonicalId, FallbackId, StringComparison.Ordinal);
}

/// <summary>
/// Runtime-only resolver for the Phase A content pipeline.
///
/// The resolver has one source root: Application.streamingAssetsPath/content.
/// It never searches the working directory, uses AssetDatabase, or creates
/// gameplay rules. The Data-layer catalog performs fail-closed manifest, path,
/// duplicate-ID, alias-cycle, fallback-cycle and SHA-256 validation. Runtime
/// reads are checked again so a file changed after startup cannot silently
/// replace the approved content.
/// </summary>
public sealed class RuntimeContentResolver
{
    public const string ContentDirectoryName = "content";
    public const string ManifestRelativePath = "manifests/content.manifest.json";

    private readonly ContentCatalog? _catalog;
    private readonly ContentSkinCatalog? _skinCatalog;
    private readonly Dictionary<string, Texture2D> _textureCache =
        new Dictionary<string, Texture2D>(StringComparer.Ordinal);
    private readonly List<RuntimeContentDiagnostic> _diagnostics =
        new List<RuntimeContentDiagnostic>();

    private RuntimeContentResolver(
        string contentRoot,
        ContentCatalog? catalog,
        ContentSkinCatalog? skinCatalog,
        RuntimeContentDiagnostic? loadDiagnostic)
    {
        ContentRoot = contentRoot;
        _catalog = catalog;
        _skinCatalog = skinCatalog;
        if (loadDiagnostic is not null) _diagnostics.Add(loadDiagnostic);
    }

    public string ContentRoot { get; }
    public bool IsAvailable => _catalog is not null;
    public ContentCatalog? Catalog => _catalog;
    public ContentSkinCatalog? SkinCatalog => _skinCatalog;
    public IReadOnlyList<RuntimeContentDiagnostic> Diagnostics => _diagnostics.AsReadOnly();
    public int TextureCacheCount => _textureCache.Count;

    /// <summary>
    /// Strict load from the packaged content root. Invalid or missing content
    /// throws, which is useful for build/test gates.
    /// </summary>
    public static RuntimeContentResolver LoadFromStreamingAssets(bool production = true)
    {
        return Load(
            Path.Combine(Application.streamingAssetsPath, ContentDirectoryName),
            production);
    }

    /// <summary>
    /// Strict load from an explicit content root. The caller must provide the
    /// content directory itself, not a repository or Unity project root.
    /// </summary>
    public static RuntimeContentResolver Load(string contentRoot, bool production = true)
    {
        var fullRoot = NormalizeRoot(contentRoot);
        var manifestPath = Path.Combine(fullRoot, ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var catalog = ContentCatalog.LoadFile(manifestPath, fullRoot, production);
        var skinDirectory = Path.Combine(
            fullRoot,
            ContentSkinCatalog.SkinDirectoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var skinCatalog = Directory.Exists(skinDirectory)
            ? ContentSkinCatalog.LoadDirectory(skinDirectory, catalog, production)
            : null;
        return new RuntimeContentResolver(fullRoot, catalog, skinCatalog, null);
    }

    /// <summary>
    /// Safe runtime entry point. It returns an unavailable resolver instead of
    /// throwing, allowing a presentation shell to show a diagnostic and use a
    /// generated card-art placeholder while gameplay remains alive.
    /// </summary>
    public static bool TryLoadFromStreamingAssets(
        out RuntimeContentResolver resolver,
        bool production = true)
    {
        return TryLoad(
            Path.Combine(Application.streamingAssetsPath, ContentDirectoryName),
            out resolver,
            production);
    }

    public static bool TryLoad(
        string contentRoot,
        out RuntimeContentResolver resolver,
        bool production = true)
    {
        string normalizedRoot;
        try
        {
            normalizedRoot = NormalizeRoot(contentRoot);
            resolver = Load(normalizedRoot, production);
            return true;
        }
        catch (Exception exception) when (IsExpectedLoadFailure(exception))
        {
            normalizedRoot = TryNormalizeRoot(contentRoot);
            resolver = new RuntimeContentResolver(
                normalizedRoot,
                null,
                null,
                new RuntimeContentDiagnostic(
                    RuntimeContentDiagnosticSeverity.Blocking,
                    "manifest_load_failed",
                    exception.Message));
            return false;
        }
    }

    /// <summary>Resolve a stable ID or an approved alias without loading bytes.</summary>
    public bool TryResolve(string requestedId, out RuntimeResolvedAsset resolved)
    {
        resolved = null!;
        if (_catalog is null)
        {
            AddDiagnostic(
                RuntimeContentDiagnosticSeverity.Error,
                "catalog_unavailable",
                "The runtime content catalog is unavailable.",
                requestedId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(requestedId))
        {
            AddDiagnostic(
                RuntimeContentDiagnosticSeverity.Warning,
                "asset_id_missing",
                "An asset ID was empty.");
            return false;
        }

        try
        {
            var canonicalId = _catalog.ResolveAlias(requestedId);
            var asset = _catalog.Assets[canonicalId];
            var fallbackId = _catalog.ResolveFallback(canonicalId);
            var fallbackAsset = _catalog.Assets[fallbackId];
            resolved = new RuntimeResolvedAsset(
                requestedId,
                canonicalId,
                asset,
                fallbackId,
                fallbackAsset);
            return true;
        }
        catch (InvalidDataException exception)
        {
            AddDiagnostic(
                RuntimeContentDiagnosticSeverity.Warning,
                "asset_resolve_failed",
                exception.Message,
                requestedId);
            return false;
        }
        catch (KeyNotFoundException exception)
        {
            AddDiagnostic(
                RuntimeContentDiagnosticSeverity.Warning,
                "asset_resolve_failed",
                exception.Message,
                requestedId);
            return false;
        }
    }

    public RuntimeResolvedAsset Resolve(string requestedId)
    {
        if (TryResolve(requestedId, out var resolved)) return resolved;
        throw new InvalidDataException("Runtime content asset could not be resolved: " + requestedId);
    }

    /// <summary>Resolves one presentation role from an optional skin catalog.</summary>
    public bool TryResolveSkinAsset(
        string skinId,
        string role,
        out RuntimeResolvedAsset resolved)
    {
        resolved = null!;
        if (_skinCatalog is null)
        {
            AddDiagnostic(
                RuntimeContentDiagnosticSeverity.Warning,
                "skin_unavailable",
                "The runtime content skin catalog is unavailable.",
                skinId);
            return false;
        }

        try
        {
            var assetId = _skinCatalog.ResolveAssetId(skinId, role);
            return TryResolve(assetId, out resolved);
        }
        catch (InvalidDataException exception)
        {
            AddDiagnostic(
                RuntimeContentDiagnosticSeverity.Warning,
                "skin_asset_resolve_failed",
                exception.Message,
                skinId + "/" + role);
            return false;
        }
    }

    /// <summary>
    /// Resolves a skin role and loads it when the role is a texture-compatible
    /// asset. Programmatic roles use the same deterministic placeholder path as
    /// card art; audio/VFX remain typed manifest references with no playback.
    /// </summary>
    public bool TryGetSkinTexture(
        string skinId,
        string role,
        out Texture2D texture)
    {
        texture = null!;
        if (!TryResolveSkinAsset(skinId, role, out var resolved)) return false;
        return TryGetTextureForAsset(resolved.Asset, out texture);
    }

    /// <summary>Applies a skin's optional card-art override before normal fallback.</summary>
    public Texture2D GetCardArtForSkin(
        string skinId,
        string cardId,
        string? requestedArtId)
    {
        if (_skinCatalog is null)
        {
            AddDiagnostic(
                RuntimeContentDiagnosticSeverity.Warning,
                "skin_unavailable",
                "The runtime content skin catalog is unavailable.",
                skinId);
            return GetCardArt(requestedArtId ?? cardId);
        }

        try
        {
            var resolvedArtId = _skinCatalog.ResolveCardArtwork(
                skinId,
                cardId,
                requestedArtId);
            return GetCardArt(
                string.IsNullOrWhiteSpace(resolvedArtId)
                    ? "card_art_" + cardId
                    : resolvedArtId);
        }
        catch (InvalidDataException exception)
        {
            AddDiagnostic(
                RuntimeContentDiagnosticSeverity.Warning,
                "skin_card_art_resolve_failed",
                exception.Message,
                skinId + "/" + cardId);
            return GetCardArt(requestedArtId ?? cardId);
        }
    }

    /// <summary>
    /// Reads a file asset after checking its path and SHA-256. Programmatic
    /// assets and missing files return false rather than throwing.
    /// </summary>
    public bool TryReadBytes(string requestedId, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (!TryResolve(requestedId, out var resolved)) return false;
        if (resolved.Asset.SourceType != "file")
        {
            AddDiagnostic(
                RuntimeContentDiagnosticSeverity.Info,
                "asset_is_programmatic",
                "The requested asset has no file bytes.",
                resolved.CanonicalId);
            return false;
        }

        return TryReadFile(resolved.Asset, out bytes);
    }

    /// <summary>
    /// Loads a file-backed texture directly. It intentionally does not apply
    /// the fallback; callers wanting card-art resilience should use
    /// GetCardArt, which returns a generated placeholder when needed.
    /// </summary>
    public bool TryGetTexture(string requestedId, out Texture2D texture)
    {
        texture = null!;
        if (!TryResolve(requestedId, out var resolved)) return false;
        if (!IsTextureKind(resolved.Asset.Kind))
        {
            AddDiagnostic(
                RuntimeContentDiagnosticSeverity.Warning,
                "texture_kind_mismatch",
                "The asset kind is not texture-compatible.",
                resolved.CanonicalId);
            return false;
        }

        return TryGetTextureForAsset(resolved.Asset, out texture);
    }

    /// <summary>
    /// Gets card art with the approved asset/fallback chain. Missing, invalid,
    /// programmatic, or malformed card art becomes a deterministic generated
    /// placeholder and never propagates an exception into gameplay.
    /// </summary>
    public Texture2D GetCardArt(string requestedId)
    {
        if (TryResolve(requestedId, out var resolved))
        {
            if (TryGetTextureForAsset(resolved.Asset, out var texture)) return texture;

            if (resolved.HasFallback &&
                TryGetTextureForAsset(resolved.FallbackAsset, out var fallbackTexture))
            {
                AddDiagnostic(
                    RuntimeContentDiagnosticSeverity.Warning,
                    "asset_fallback_used",
                    "The primary card art was unavailable; its fallback was used.",
                    resolved.CanonicalId);
                return fallbackTexture;
            }

            AddDiagnostic(
                RuntimeContentDiagnosticSeverity.Warning,
                "asset_fallback_missing",
                "The primary card art and its fallback were unavailable.",
                resolved.CanonicalId);
            return GetOrCreatePlaceholder(resolved.FallbackId);
        }

        return GetOrCreatePlaceholder(requestedId);
    }

    /// <summary>Clears and destroys runtime-created texture objects.</summary>
    public void ClearTextureCache()
    {
        foreach (var texture in _textureCache.Values)
        {
            if (texture == null) continue;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }

        _textureCache.Clear();
    }

    private bool TryGetTextureForAsset(ContentAsset asset, out Texture2D texture)
    {
        texture = null!;
        if (!IsTextureKind(asset.Kind)) return false;
        if (_textureCache.TryGetValue(asset.Id, out var cached) && cached != null)
        {
            texture = cached;
            return true;
        }

        if (asset.SourceType == "programmatic")
        {
            // A registered programmatic asset is an available presentation
            // asset (normally a neutral generated surface), not a missing
            // asset fallback. Keep its stable ID in the generated texture
            // name so consumers can distinguish it from an unresolved ID.
            texture = GetOrCreateGeneratedTexture(asset.Id, false);
            return true;
        }

        if (asset.SourceType != "file" || !TryReadFile(asset, out var bytes)) return false;

        Texture2D loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
        {
            name = "RuntimeContent_" + asset.Id,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        try
        {
            if (!loaded.LoadImage(bytes, false))
            {
                DestroyTexture(loaded);
                AddDiagnostic(
                    RuntimeContentDiagnosticSeverity.Warning,
                    "texture_decode_failed",
                    "The file was not a supported texture.",
                    asset.Id);
                return false;
            }

            _textureCache[asset.Id] = loaded;
            texture = loaded;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException)
        {
            DestroyTexture(loaded);
            AddDiagnostic(
                RuntimeContentDiagnosticSeverity.Warning,
                "texture_decode_failed",
                exception.Message,
                asset.Id);
            return false;
        }
    }

    private static bool IsTextureKind(string kind)
    {
        return kind == "card_art"
            || kind == "board"
            || kind == "card_back"
            || kind == "castle"
            || kind == "leader"
            || kind == "faction_frame"
            || kind == "faction_icon"
            || kind == "resource_icon"
            || kind == "status_icon"
            || kind == "ui_icon";
    }

    private Texture2D GetOrCreatePlaceholder(string requestedId)
    {
        return GetOrCreateGeneratedTexture(requestedId, true);
    }

    private Texture2D GetOrCreateGeneratedTexture(string requestedId, bool placeholder)
    {
        var normalizedId = string.IsNullOrWhiteSpace(requestedId) ? "missing" : requestedId;
        var cacheId = placeholder ? "placeholder_" + normalizedId : normalizedId;
        if (_textureCache.TryGetValue(cacheId, out var cached) && cached != null) return cached;

        byte[] digest;
        using (var sha = SHA256.Create())
        {
            digest = sha.ComputeHash(Encoding.UTF8.GetBytes(cacheId));
        }
        var primary = new Color32(
            (byte)(48 + digest[0] % 120),
            (byte)(72 + digest[1] % 120),
            (byte)(92 + digest[2] % 120),
            255);
        var secondary = new Color32(
            (byte)(primary.r / 2),
            (byte)(primary.g / 2),
            (byte)(primary.b / 2),
            255);
        var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false, false)
        {
            name = "RuntimeContent_" + cacheId,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point,
        };
        var pixels = new Color32[64];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                pixels[y * 8 + x] = ((x + y) % 2 == 0) ? primary : secondary;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        _textureCache[cacheId] = texture;
        return texture;
    }

    private bool TryReadFile(ContentAsset asset, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (!TryGetSafeFilePath(asset, out var filePath)) return false;
        try
        {
            if (!File.Exists(filePath))
            {
                AddDiagnostic(
                    RuntimeContentDiagnosticSeverity.Warning,
                    "asset_file_missing",
                    "The referenced asset file is missing.",
                    asset.Id);
                return false;
            }

            bytes = File.ReadAllBytes(filePath);
            if (!string.IsNullOrWhiteSpace(asset.Sha256) &&
                !string.Equals(ComputeSha256(bytes), asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                bytes = Array.Empty<byte>();
                AddDiagnostic(
                    RuntimeContentDiagnosticSeverity.Warning,
                    "asset_hash_mismatch",
                    "The asset file changed after manifest validation.",
                    asset.Id);
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            AddDiagnostic(
                RuntimeContentDiagnosticSeverity.Warning,
                "asset_file_read_failed",
                exception.Message,
                asset.Id);
            return false;
        }
    }

    private bool TryGetSafeFilePath(ContentAsset asset, out string filePath)
    {
        filePath = string.Empty;
        var relativePath = asset.RelativePath;
        if (string.IsNullOrWhiteSpace(relativePath) || !IsSafeRelativePath(relativePath))
        {
            AddDiagnostic(
                RuntimeContentDiagnosticSeverity.Error,
                "unsafe_asset_path",
                "The asset path is not a safe content-relative path.",
                asset.Id);
            return false;
        }

        try
        {
            var platformPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            filePath = Path.GetFullPath(Path.Combine(ContentRoot, platformPath));
            var rootPrefix = ContentRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? ContentRoot
                : ContentRoot + Path.DirectorySeparatorChar;
            if (!filePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                filePath = string.Empty;
                AddDiagnostic(
                    RuntimeContentDiagnosticSeverity.Error,
                    "unsafe_asset_path",
                    "The asset path escaped the content root.",
                    asset.Id);
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is NotSupportedException)
        {
            AddDiagnostic(
                RuntimeContentDiagnosticSeverity.Error,
                "unsafe_asset_path",
                exception.Message,
                asset.Id);
            return false;
        }
    }

    private static bool IsSafeRelativePath(string relativePath)
    {
        if (relativePath.Length == 0 || relativePath.IndexOf('\\') >= 0 || relativePath[0] == '/' ||
            relativePath.StartsWith("//", StringComparison.Ordinal) ||
            (relativePath.Length >= 2 && relativePath[1] == ':')) return false;

        foreach (var segment in relativePath.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..") return false;
            foreach (var character in segment)
            {
                if (character < 32 || character == ':' || character == '*' || character == '?' ||
                    character == '"' || character == '<' || character == '>' || character == '|') return false;
            }
        }

        return true;
    }

    private void AddDiagnostic(
        RuntimeContentDiagnosticSeverity severity,
        string code,
        string message,
        string? assetId = null)
    {
        _diagnostics.Add(new RuntimeContentDiagnostic(severity, code, message, assetId));
    }

    private static string NormalizeRoot(string contentRoot)
    {
        if (contentRoot is null) throw new ArgumentNullException(nameof(contentRoot));
        var fullRoot = Path.GetFullPath(contentRoot);
        var pathRoot = Path.GetPathRoot(fullRoot);
        if (!string.IsNullOrEmpty(pathRoot) &&
            string.Equals(fullRoot, pathRoot, StringComparison.OrdinalIgnoreCase))
        {
            return pathRoot;
        }

        return fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string TryNormalizeRoot(string contentRoot)
    {
        try { return NormalizeRoot(contentRoot); }
        catch (Exception) { return string.Empty; }
    }

    private static bool IsExpectedLoadFailure(Exception exception)
    {
        return exception is ArgumentException ||
            exception is DirectoryNotFoundException ||
            exception is FileNotFoundException ||
            exception is InvalidDataException ||
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is NotSupportedException;
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using (var sha = SHA256.Create())
        {
            var digest = sha.ComputeHash(bytes);
            var builder = new StringBuilder(digest.Length * 2);
            foreach (var value in digest) builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }

    private static void DestroyTexture(Texture2D texture)
    {
        if (texture == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
        else UnityEngine.Object.DestroyImmediate(texture);
    }
}
}
