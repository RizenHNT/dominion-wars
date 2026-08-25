using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DominionWars.Data
{
    /// <summary>
    /// Fail-closed loader and resolver for the immutable content manifest.
    /// </summary>
    public sealed class ContentCatalog
    {
        private static readonly HashSet<string> SourceTypes =
            new HashSet<string>(new[] { "file", "programmatic" }, StringComparer.Ordinal);
        private static readonly HashSet<string> Statuses =
            new HashSet<string>(new[] { "draft", "ready", "approved", "deprecated" }, StringComparer.Ordinal);
        private static readonly HashSet<string> RootFields =
            new HashSet<string>(new[] { "manifestVersion", "schemaVersion", "contentVersion", "assets", "aliases" }, StringComparer.Ordinal);
        private static readonly HashSet<string> AssetFields =
            new HashSet<string>(new[] { "id", "kind", "sourceType", "relativePath", "sha256", "fallbackId", "status" }, StringComparer.Ordinal);
        private static readonly HashSet<string> AliasFields =
            new HashSet<string>(new[] { "fromId", "toId" }, StringComparer.Ordinal);

        private readonly IReadOnlyDictionary<string, ContentAsset> _assets;
        private readonly IReadOnlyDictionary<string, string> _aliases;
        private readonly IReadOnlyDictionary<string, string> _fallbacks;

        public ContentCatalog(ContentManifest manifest, string contentRoot, bool production = true)
        {
            if (manifest is null) throw new ArgumentNullException(nameof(manifest));
            if (contentRoot is null) throw new ArgumentNullException(nameof(contentRoot));

            Manifest = manifest;
            Production = production;
            ContentRoot = GetFullRoot(contentRoot);
            if (!Directory.Exists(ContentRoot))
            {
                throw Invalid("content root does not exist");
            }

            ValidateManifestVersion(manifest);
            ValidateVersionString(manifest.SchemaVersion, "schemaVersion");
            ValidateVersionString(manifest.ContentVersion, "contentVersion");

            var assets = new Dictionary<string, ContentAsset>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in manifest.Assets)
            {
                if (asset is null) throw Invalid("assets contains a null entry");
                ValidateAsset(asset);
                if (!assets.TryAdd(asset.Id, asset))
                {
                    throw Invalid("duplicate asset id: " + asset.Id);
                }

                if (asset.SourceType == "file")
                {
                    var normalizedPath = ValidateRelativePath(asset.RelativePath!, asset.Id);
                    if (!paths.Add(normalizedPath))
                    {
                        throw Invalid("duplicate asset path: " + normalizedPath);
                    }

                    VerifyFile(asset, normalizedPath);
                }
            }

            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var alias in manifest.Aliases)
            {
                if (alias is null) throw Invalid("aliases contains a null entry");
                ValidateAlias(alias);
                if (assets.ContainsKey(alias.FromId))
                {
                    throw Invalid("alias conflicts with asset id: " + alias.FromId);
                }

                if (!aliases.TryAdd(alias.FromId, alias.ToId))
                {
                    throw Invalid("duplicate alias id: " + alias.FromId);
                }
            }

            ValidateAliases(assets, aliases);
            var sortedAssets = assets.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var sortedAliases = aliases.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            _assets = new ReadOnlyDictionary<string, ContentAsset>(sortedAssets);
            _aliases = new ReadOnlyDictionary<string, string>(sortedAliases);

            var fallbackMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var asset in sortedAssets.Values)
            {
                fallbackMap.Add(asset.Id, ResolveFallbackCore(asset.Id, sortedAssets, sortedAliases));
            }

            _fallbacks = new ReadOnlyDictionary<string, string>(fallbackMap);
        }

        public ContentManifest Manifest { get; }
        public string ContentRoot { get; }
        public bool Production { get; }
        public IReadOnlyDictionary<string, ContentAsset> Assets => _assets;
        public IReadOnlyDictionary<string, ContentAsset> AssetsById => _assets;
        public IReadOnlyDictionary<string, string> Aliases => _aliases;
        public IReadOnlyDictionary<string, string> AliasesById => _aliases;
        public IReadOnlyDictionary<string, string> Fallbacks => _fallbacks;
        public IReadOnlyDictionary<string, string> FallbacksById => _fallbacks;

        public static ContentCatalog Load(string manifestPath, bool production = true)
        {
            return LoadFile(manifestPath, InferContentRoot(manifestPath), production);
        }

        public static ContentCatalog Load(string manifestPath, string contentRoot, bool production = true)
        {
            return LoadFile(manifestPath, contentRoot, production);
        }

        public static ContentCatalog LoadFile(string manifestPath, bool production = true)
        {
            return Load(manifestPath, production);
        }

        public static ContentCatalog LoadFile(string manifestPath, string contentRoot, bool production = true)
        {
            if (manifestPath is null) throw new ArgumentNullException(nameof(manifestPath));
            if (contentRoot is null) throw new ArgumentNullException(nameof(contentRoot));
            if (!File.Exists(manifestPath)) throw Invalid("manifest file is missing");

            string json;
            try
            {
                json = File.ReadAllText(manifestPath, Encoding.UTF8);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw Invalid("manifest file cannot be read", exception);
            }

            return LoadJson(json, contentRoot, production);
        }

        public static ContentCatalog LoadJson(string json, string contentRoot, bool production = true)
        {
            if (json is null) throw new ArgumentNullException(nameof(json));
            if (contentRoot is null) throw new ArgumentNullException(nameof(contentRoot));

            ContentManifest manifest;
            try
            {
                var settings = new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                };
                var root = JToken.Parse(json, settings);
                if (root.Type != JTokenType.Object) throw Invalid("manifest root must be an object");
                manifest = ParseManifest((JObject)root);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (JsonException exception)
            {
                throw Invalid("invalid manifest JSON", exception);
            }

            return new ContentCatalog(manifest, contentRoot, production);
        }

        public string ResolveAlias(string id)
        {
            return ResolveAliasCore(id, _assets, _aliases, "asset id");
        }

        public string ResolveFallback(string id)
        {
            var canonicalId = ResolveAlias(id);
            return _fallbacks[canonicalId];
        }

        private static ContentManifest ParseManifest(JObject root)
        {
            EnsureKnownFields(root, RootFields, "manifest");
            var manifestVersion = RequiredInt(root, "manifestVersion");
            var schemaVersion = RequiredString(root, "schemaVersion");
            var contentVersion = RequiredString(root, "contentVersion");
            var assetsToken = RequiredToken(root, "assets");
            if (assetsToken.Type != JTokenType.Array) throw Invalid("assets must be an array");

            var assets = new List<ContentAsset>();
            var assetIndex = 0;
            foreach (var token in assetsToken.Children())
            {
                if (token.Type != JTokenType.Object) throw Invalid("assets[" + assetIndex + "] must be an object");
                var assetObject = (JObject)token;
                EnsureKnownFields(assetObject, AssetFields, "assets[" + assetIndex + "]");
                var id = RequiredString(assetObject, "id");
                var kind = RequiredString(assetObject, "kind");
                var sourceType = RequiredString(assetObject, "sourceType");
                var relativePath = NullableString(assetObject, "relativePath");
                var sha256 = NullableString(assetObject, "sha256");
                var fallbackId = NullableString(assetObject, "fallbackId");
                var status = RequiredString(assetObject, "status");
                assets.Add(new ContentAsset(id, kind, sourceType, relativePath, sha256, fallbackId, status));
                assetIndex++;
            }

            var aliases = new List<ContentAlias>();
            if (root.TryGetValue("aliases", StringComparison.Ordinal, out var aliasesToken))
            {
                if (aliasesToken.Type != JTokenType.Array) throw Invalid("aliases must be an array");
                var aliasIndex = 0;
                foreach (var token in aliasesToken.Children())
                {
                    if (token.Type != JTokenType.Object) throw Invalid("aliases[" + aliasIndex + "] must be an object");
                    var aliasObject = (JObject)token;
                    EnsureKnownFields(aliasObject, AliasFields, "aliases[" + aliasIndex + "]");
                    aliases.Add(new ContentAlias(
                        RequiredString(aliasObject, "fromId"),
                        RequiredString(aliasObject, "toId")));
                    aliasIndex++;
                }
            }

            return new ContentManifest(manifestVersion, schemaVersion, contentVersion, assets, aliases);
        }

        private void ValidateAsset(ContentAsset asset)
        {
            if (!ContentManifest.IsStableId(asset.Id)) throw Invalid("invalid asset id: " + asset.Id);
            if (!ContentManifest.IsCanonicalKind(asset.Kind)) throw Invalid("invalid asset kind: " + asset.Kind);
            if (!SourceTypes.Contains(asset.SourceType)) throw Invalid("invalid sourceType for " + asset.Id + ": " + asset.SourceType);
            if (!Statuses.Contains(asset.Status)) throw Invalid("invalid status for " + asset.Id + ": " + asset.Status);
            if (Production && asset.Status == "draft")
            {
                throw Invalid("draft asset is not allowed in production: " + asset.Id);
            }

            if (asset.FallbackId is not null && !ContentManifest.IsStableId(asset.FallbackId))
            {
                throw Invalid("invalid fallbackId for " + asset.Id + ": " + asset.FallbackId);
            }

            if (asset.SourceType == "file")
            {
                if (string.IsNullOrEmpty(asset.RelativePath)) throw Invalid("file asset requires relativePath: " + asset.Id);
                if (string.IsNullOrEmpty(asset.Sha256)) throw Invalid("file asset requires sha256: " + asset.Id);
                if (!IsSha256(asset.Sha256)) throw Invalid("invalid sha256 for " + asset.Id);
            }
            else if (asset.RelativePath is not null || asset.Sha256 is not null)
            {
                throw Invalid("programmatic asset cannot declare relativePath or sha256: " + asset.Id);
            }
        }

        private void VerifyFile(ContentAsset asset, string normalizedPath)
        {
            var fullPath = GetSafeFilePath(normalizedPath, asset.Id);
            if (!File.Exists(fullPath))
            {
                throw Invalid("asset file is missing: " + asset.Id + " (" + normalizedPath + ")");
            }

            string actualHash;
            try
            {
                using var sha = SHA256.Create();
                using var stream = File.OpenRead(fullPath);
                actualHash = ToLowerHex(sha.ComputeHash(stream));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw Invalid("asset file cannot be read: " + asset.Id, exception);
            }

            if (!string.Equals(actualHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid("sha256 mismatch for " + asset.Id);
            }
        }

        private string ValidateRelativePath(string relativePath, string assetId)
        {
            if (string.IsNullOrEmpty(relativePath)) throw Invalid("file asset requires relativePath: " + assetId);
            if (relativePath.IndexOf('\\') >= 0
                || relativePath[0] == '/'
                || relativePath.StartsWith("//", StringComparison.Ordinal)
                || (relativePath.Length >= 2 && relativePath[1] == ':'))
            {
                throw Invalid("unsafe relativePath for " + assetId + ": " + relativePath);
            }

            var segments = relativePath.Split('/');
            foreach (var segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == ".." || ContainsInvalidPathCharacter(segment))
                {
                    throw Invalid("unsafe relativePath for " + assetId + ": " + relativePath);
                }
            }

            var fullPath = GetSafeFilePath(relativePath, assetId);
            var rootWithSeparator = ContentRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? ContentRoot
                : ContentRoot + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid("unsafe relativePath for " + assetId + ": " + relativePath);
            }

            return relativePath;
        }

        private string GetSafeFilePath(string relativePath, string assetId)
        {
            try
            {
                var platformPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
                return Path.GetFullPath(Path.Combine(ContentRoot, platformPath));
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is NotSupportedException)
            {
                throw Invalid("unsafe relativePath for " + assetId + ": " + relativePath, exception);
            }
        }

        private static bool ContainsInvalidPathCharacter(string value)
        {
            foreach (var character in value)
            {
                if (character < 32 || character == ':' || character == '*' || character == '?' || character == '"'
                    || character == '<' || character == '>' || character == '|')
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateAliases(
            IReadOnlyDictionary<string, ContentAsset> assets,
            IReadOnlyDictionary<string, string> aliases)
        {
            foreach (var alias in aliases.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                ResolveAliasCore(alias.Key, assets, aliases, "alias");
            }
        }

        private static string ResolveFallbackCore(
            string assetId,
            IReadOnlyDictionary<string, ContentAsset> assets,
            IReadOnlyDictionary<string, string> aliases)
        {
            var chain = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = assetId;
            while (true)
            {
                if (!visited.Add(current))
                {
                    chain.Add(current);
                    throw Invalid("fallback cycle: " + string.Join(" -> ", chain));
                }

                if (!assets.TryGetValue(current, out var asset))
                {
                    throw Invalid("fallback asset not found: " + current);
                }

                chain.Add(current);
                if (asset.FallbackId is null) return current;
                current = ResolveAliasCore(asset.FallbackId, assets, aliases, "fallback");
            }
        }

        private static string ResolveAliasCore(
            string id,
            IReadOnlyDictionary<string, ContentAsset> assets,
            IReadOnlyDictionary<string, string> aliases,
            string referenceKind)
        {
            if (string.IsNullOrEmpty(id)) throw Invalid(referenceKind + " id is empty");

            var current = id;
            var chain = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (aliases.TryGetValue(current, out var next))
            {
                if (!visited.Add(current))
                {
                    chain.Add(current);
                    throw Invalid("alias cycle: " + string.Join(" -> ", chain));
                }

                chain.Add(current);
                current = next;
            }

            if (!assets.ContainsKey(current))
            {
                var message = referenceKind == "fallback"
                    ? "fallback asset target not found: " + current
                    : referenceKind + " target not found: " + current;
                throw Invalid(message);
            }

            return current;
        }

        private static void ValidateAlias(ContentAlias alias)
        {
            if (!ContentManifest.IsStableId(alias.FromId)) throw Invalid("invalid alias fromId: " + alias.FromId);
            if (!ContentManifest.IsStableId(alias.ToId)) throw Invalid("invalid alias toId: " + alias.ToId);
        }

        private static void ValidateManifestVersion(ContentManifest manifest)
        {
            if (manifest.ManifestVersion != ContentManifest.CurrentManifestVersion)
            {
                throw Invalid("unsupported manifestVersion: " + manifest.ManifestVersion);
            }
        }

        private static void ValidateVersionString(string value, string property)
        {
            if (string.IsNullOrWhiteSpace(value)) throw Invalid(property + " must be a non-empty string");
        }

        private static string GetFullRoot(string contentRoot)
        {
            try
            {
                var fullRoot = Path.GetFullPath(contentRoot);
                var root = Path.GetPathRoot(fullRoot);
                if (!string.IsNullOrEmpty(root) && string.Equals(fullRoot, root, StringComparison.OrdinalIgnoreCase))
                {
                    return root;
                }

                return fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is NotSupportedException)
            {
                throw Invalid("content root is invalid", exception);
            }
        }

        private static string InferContentRoot(string manifestPath)
        {
            if (manifestPath is null) throw new ArgumentNullException(nameof(manifestPath));
            var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
            if (string.IsNullOrEmpty(manifestDirectory)) throw Invalid("manifest directory is invalid");
            if (string.Equals(Path.GetFileName(manifestDirectory), "manifests", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Directory.GetParent(manifestDirectory);
                if (parent is not null) return parent.FullName;
            }

            return manifestDirectory;
        }

        private static bool IsSha256(string value)
        {
            if (value.Length != 64) return false;
            foreach (var character in value)
            {
                if (!((character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes) builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static void EnsureKnownFields(JObject objectValue, HashSet<string> knownFields, string source)
        {
            foreach (var property in objectValue.Properties())
            {
                if (!knownFields.Contains(property.Name)) throw Invalid(source + " contains unknown field: " + property.Name);
            }
        }

        private static JToken RequiredToken(JObject objectValue, string property)
        {
            var token = objectValue[property];
            if (token is null) throw Invalid(property + " is required");
            return token;
        }

        private static int RequiredInt(JObject objectValue, string property)
        {
            var token = RequiredToken(objectValue, property);
            if (token.Type != JTokenType.Integer || !int.TryParse(token.ToString(), out var value))
            {
                throw Invalid(property + " must be an integer");
            }

            return value;
        }

        private static string RequiredString(JObject objectValue, string property)
        {
            var token = RequiredToken(objectValue, property);
            if (token.Type != JTokenType.String || string.IsNullOrEmpty(token.Value<string>()))
            {
                throw Invalid(property + " must be a non-empty string");
            }

            return token.Value<string>()!;
        }

        private static string? NullableString(JObject objectValue, string property)
        {
            if (!objectValue.TryGetValue(property, StringComparison.Ordinal, out var token) || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type != JTokenType.String) throw Invalid(property + " must be a string or null");
            return token.Value<string>();
        }

        private static InvalidDataException Invalid(string message, Exception? inner = null)
        {
            return new InvalidDataException("content manifest: " + message, inner);
        }
    }
}
