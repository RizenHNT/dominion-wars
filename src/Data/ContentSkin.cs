using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DominionWars.Data
{
    /// <summary>
    /// A presentation-only skin manifest. It maps stable role names to asset
    /// IDs and never carries layout, legality, balance, or effect semantics.
    /// </summary>
    public sealed class ContentSkinManifest
    {
        public ContentSkinManifest(
            string skinId,
            string version,
            string themeId,
            IReadOnlyDictionary<string, string> assets,
            IReadOnlyDictionary<string, string>? cardArtwork = null,
            IReadOnlyDictionary<string, string>? audioCues = null,
            IReadOnlyDictionary<string, string>? vfxCues = null)
        {
            SkinId = skinId ?? throw new ArgumentNullException(nameof(skinId));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            ThemeId = themeId ?? throw new ArgumentNullException(nameof(themeId));
            Assets = CopyMap(assets, nameof(assets));
            CardArtwork = CopyMap(cardArtwork, nameof(cardArtwork));
            AudioCues = CopyMap(audioCues, nameof(audioCues));
            VfxCues = CopyMap(vfxCues, nameof(vfxCues));
        }

        public string SkinId { get; }
        public string Version { get; }
        public string ThemeId { get; }
        public IReadOnlyDictionary<string, string> Assets { get; }
        public IReadOnlyDictionary<string, string> CardArtwork { get; }
        public IReadOnlyDictionary<string, string> AudioCues { get; }
        public IReadOnlyDictionary<string, string> VfxCues { get; }

        private static IReadOnlyDictionary<string, string> CopyMap(
            IReadOnlyDictionary<string, string>? values,
            string parameterName)
        {
            if (values is null)
                return new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(StringComparer.Ordinal));

            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in values)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    throw new ArgumentException("Skin map keys must be non-empty.", parameterName);
                if (string.IsNullOrWhiteSpace(pair.Value))
                    throw new ArgumentException("Skin map values must be non-empty asset IDs.", parameterName);
                if (!copy.TryAdd(pair.Key, pair.Value))
                    throw new ArgumentException(
                        "Skin map contains a duplicate key: " + pair.Key, parameterName);
            }

            return new ReadOnlyDictionary<string, string>(
                copy.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// Loads and validates one or more skin manifests against the shared
    /// ContentCatalog. Every mapped value must resolve to a registered asset;
    /// role-kind checks prevent a board or icon role from silently pointing at
    /// card data.
    /// </summary>
    public sealed class ContentSkinCatalog
    {
        public const string SkinDirectoryRelativePath = "manifests/skins";

        private static readonly HashSet<string> RootFields = new HashSet<string>(
            new[]
            {
                "skinId", "version", "theme", "assets", "cardArtwork",
                "audioCues", "vfxCues"
            },
            StringComparer.Ordinal);

        private readonly IReadOnlyDictionary<string, ContentSkinManifest> _skins;

        private ContentSkinCatalog(
            ContentCatalog contentCatalog,
            IEnumerable<ContentSkinManifest> skins)
        {
            ContentCatalog = contentCatalog ?? throw new ArgumentNullException(nameof(contentCatalog));
            var values = new Dictionary<string, ContentSkinManifest>(StringComparer.Ordinal);
            foreach (var skin in skins ?? throw new ArgumentNullException(nameof(skins)))
            {
                if (skin is null) throw Invalid("skin list contains a null entry");
                if (!values.TryAdd(skin.SkinId, skin))
                    throw Invalid("duplicate skin id: " + skin.SkinId);
            }

            _skins = new ReadOnlyDictionary<string, ContentSkinManifest>(
                values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        }

        public ContentCatalog ContentCatalog { get; }
        public IReadOnlyDictionary<string, ContentSkinManifest> Skins => _skins;

        public static ContentSkinCatalog LoadDirectory(
            string directory,
            ContentCatalog contentCatalog,
            bool production = true)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));
            if (contentCatalog is null) throw new ArgumentNullException(nameof(contentCatalog));
            var fullDirectory = Path.GetFullPath(directory);
            if (!Directory.Exists(fullDirectory))
                throw Invalid("skin directory is missing: " + fullDirectory);

            var paths = Directory.GetFiles(fullDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (paths.Length == 0) throw Invalid("skin directory contains no JSON manifests");

            var skins = new List<ContentSkinManifest>();
            foreach (var path in paths)
            {
                string json;
                try
                {
                    json = File.ReadAllText(path, Encoding.UTF8);
                }
                catch (Exception exception) when (
                    exception is IOException || exception is UnauthorizedAccessException)
                {
                    throw Invalid("skin manifest cannot be read: " + path, exception);
                }

                skins.Add(ParseJson(json, path, contentCatalog, production));
            }

            return new ContentSkinCatalog(contentCatalog, skins);
        }

        public static ContentSkinManifest LoadFile(
            string path,
            ContentCatalog contentCatalog,
            bool production = true)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (contentCatalog is null) throw new ArgumentNullException(nameof(contentCatalog));
            if (!File.Exists(path)) throw Invalid("skin manifest is missing: " + path);
            try
            {
                return ParseJson(
                    File.ReadAllText(path, Encoding.UTF8),
                    path,
                    contentCatalog,
                    production);
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException)
            {
                throw Invalid("skin manifest cannot be read: " + path, exception);
            }
        }

        public static ContentSkinManifest LoadJson(
            string json,
            ContentCatalog contentCatalog,
            bool production = true,
            string? sourcePath = null)
        {
            if (json is null) throw new ArgumentNullException(nameof(json));
            if (contentCatalog is null) throw new ArgumentNullException(nameof(contentCatalog));
            return ParseJson(json, sourcePath ?? "<memory>", contentCatalog, production);
        }

        public bool TryGetSkin(string skinId, out ContentSkinManifest skin)
        {
            return _skins.TryGetValue(skinId ?? string.Empty, out skin!);
        }

        public ContentSkinManifest GetSkin(string skinId)
        {
            if (!TryGetSkin(skinId, out var skin))
                throw Invalid("skin id is not registered: " + skinId);
            return skin;
        }

        /// <summary>Resolves a role override through aliases to a canonical asset ID.</summary>
        public string ResolveAssetId(string skinId, string role)
        {
            var skin = GetSkin(skinId);
            if (string.IsNullOrWhiteSpace(role)) throw Invalid("skin role is empty");
            if (!skin.Assets.TryGetValue(role, out var requestedId))
                throw Invalid("skin role is not configured: " + skinId + "/" + role);
            return ContentCatalog.ResolveAlias(requestedId);
        }

        /// <summary>
        /// Resolves a card-art override by card ID first, then requested art ID.
        /// An absent override returns the supplied art ID unchanged so callers
        /// can continue the normal catalog fallback chain.
        /// </summary>
        public string ResolveCardArtwork(
            string skinId,
            string cardId,
            string? requestedArtId)
        {
            var skin = GetSkin(skinId);
            if (!string.IsNullOrWhiteSpace(cardId)
                && skin.CardArtwork.TryGetValue(cardId, out var cardOverride))
            {
                return ContentCatalog.ResolveAlias(cardOverride);
            }

            if (!string.IsNullOrWhiteSpace(requestedArtId)
                && skin.CardArtwork.TryGetValue(requestedArtId, out var artOverride))
            {
                return ContentCatalog.ResolveAlias(artOverride);
            }

            return requestedArtId ?? string.Empty;
        }

        private static ContentSkinManifest ParseJson(
            string json,
            string sourcePath,
            ContentCatalog contentCatalog,
            bool production)
        {
            try
            {
                var settings = new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                };
                var root = JToken.Parse(json, settings);
                if (root.Type != JTokenType.Object)
                    throw Invalid(sourcePath + ": root must be an object");

                var objectValue = (JObject)root;
                EnsureKnownFields(objectValue, sourcePath);
                var skinId = RequiredString(objectValue, "skinId", sourcePath);
                var version = RequiredString(objectValue, "version", sourcePath);
                var themeId = RequiredString(objectValue, "theme", sourcePath);
                if (!ContentManifest.IsStableId(skinId))
                    throw Invalid(sourcePath + ": invalid skinId: " + skinId);
                if (!ContentManifest.IsStableId(themeId))
                    throw Invalid(sourcePath + ": invalid theme ID: " + themeId);

                var assets = ParseMap(objectValue, "assets", sourcePath, required: true);
                var cardArtwork = ParseMap(objectValue, "cardArtwork", sourcePath, required: false);
                var audioCues = ParseMap(objectValue, "audioCues", sourcePath, required: false);
                var vfxCues = ParseMap(objectValue, "vfxCues", sourcePath, required: false);
                ValidateMap(
                    assets,
                    "role",
                    sourcePath,
                    contentCatalog,
                    production,
                    ExpectedRoleKind);
                ValidateMap(
                    cardArtwork,
                    "card artwork",
                    sourcePath,
                    contentCatalog,
                    production,
                    _ => "card_art");
                ValidateMap(
                    audioCues,
                    "audio cue",
                    sourcePath,
                    contentCatalog,
                    production,
                    _ => "audio");
                ValidateMap(
                    vfxCues,
                    "vfx cue",
                    sourcePath,
                    contentCatalog,
                    production,
                    _ => "vfx");

                return new ContentSkinManifest(
                    skinId,
                    version,
                    themeId,
                    assets,
                    cardArtwork,
                    audioCues,
                    vfxCues);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (JsonException exception)
            {
                throw Invalid(sourcePath + ": invalid skin JSON", exception);
            }
        }

        private static Dictionary<string, string> ParseMap(
            JObject root,
            string property,
            string sourcePath,
            bool required)
        {
            if (!root.TryGetValue(property, StringComparison.Ordinal, out var token))
            {
                if (required)
                    throw Invalid(sourcePath + ": required skin map is missing: " + property);
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            if (token.Type != JTokenType.Object)
                throw Invalid(sourcePath + ": skin map must be an object: " + property);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in ((JObject)token).Properties()
                .OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(item.Name))
                    throw Invalid(sourcePath + ": skin role key is empty");
                if (item.Value.Type != JTokenType.String
                    || string.IsNullOrWhiteSpace(item.Value.Value<string>()))
                {
                    throw Invalid(
                        sourcePath + ": skin map value must be a non-empty string: "
                        + property + "/" + item.Name);
                }

                result.Add(item.Name, item.Value.Value<string>()!);
            }

            return result;
        }

        private static void ValidateMap(
            IReadOnlyDictionary<string, string> map,
            string mapName,
            string sourcePath,
            ContentCatalog contentCatalog,
            bool production,
            Func<string, string?> expectedKind)
        {
            foreach (var pair in map.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                string canonicalId;
                try
                {
                    canonicalId = contentCatalog.ResolveAlias(pair.Value);
                }
                catch (InvalidDataException exception)
                {
                    throw Invalid(
                        sourcePath + ": " + mapName + " target does not resolve: "
                        + pair.Key + " -> " + pair.Value,
                        exception);
                }

                var asset = contentCatalog.Assets[canonicalId];
                if (production && string.Equals(asset.Status, "draft", StringComparison.Ordinal))
                    throw Invalid(
                        sourcePath + ": draft asset is not allowed in production: " + pair.Value);

                var requiredKind = expectedKind(pair.Key);
                if (requiredKind is not null
                    && !string.Equals(asset.Kind, requiredKind, StringComparison.Ordinal))
                {
                    throw Invalid(
                        sourcePath + ": " + mapName + " role has kind " + asset.Kind
                        + ", expected " + requiredKind + ": " + pair.Key);
                }
            }
        }

        private static string? ExpectedRoleKind(string role)
        {
            if (string.Equals(role, "board", StringComparison.Ordinal)) return "board";
            if (string.Equals(role, "cardBack", StringComparison.Ordinal)
                || string.Equals(role, "card_back", StringComparison.Ordinal))
                return "card_back";
            if (string.Equals(role, "castle", StringComparison.Ordinal)) return "castle";
            if (string.Equals(role, "leader", StringComparison.Ordinal)
                || string.Equals(role, "leaderFrame", StringComparison.Ordinal)
                || string.Equals(role, "leader_frame", StringComparison.Ordinal))
                return "leader";
            if (string.Equals(role, "factionFrame", StringComparison.Ordinal)
                || string.Equals(role, "faction_frame", StringComparison.Ordinal)
                || role.StartsWith("factionFrame.", StringComparison.Ordinal)
                || role.StartsWith("faction_frame.", StringComparison.Ordinal))
                return "faction_frame";
            if (string.Equals(role, "uiIcon", StringComparison.Ordinal)
                || string.Equals(role, "ui_icon", StringComparison.Ordinal)
                || role.StartsWith("uiIcon.", StringComparison.Ordinal)
                || role.StartsWith("ui_icon.", StringComparison.Ordinal))
                return "ui_icon";
            return null;
        }

        private static void EnsureKnownFields(JObject root, string sourcePath)
        {
            foreach (var property in root.Properties())
            {
                if (!RootFields.Contains(property.Name))
                    throw Invalid(sourcePath + ": unknown skin field: " + property.Name);
            }
        }

        private static string RequiredString(
            JObject root,
            string property,
            string sourcePath)
        {
            var token = root[property];
            if (token?.Type != JTokenType.String
                || string.IsNullOrWhiteSpace(token.Value<string>()))
            {
                throw Invalid(
                    sourcePath + ": required skin field is missing or empty: " + property);
            }

            return token.Value<string>()!;
        }

        private static InvalidDataException Invalid(
            string message,
            Exception? inner = null)
        {
            return new InvalidDataException("content skin manifest: " + message, inner);
        }
    }
}
