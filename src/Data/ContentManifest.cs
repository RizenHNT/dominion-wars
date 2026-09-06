using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DominionWars.Data
{
    /// <summary>
    /// Immutable, JSON-independent representation of the content manifest contract.
    /// </summary>
    public sealed class ContentManifest
    {
        public const int CurrentManifestVersion = 1;
        public const string StableIdExpression = "^[a-z][a-z0-9_]*$";

        private static readonly IReadOnlyCollection<string> CanonicalKinds =
            Array.AsReadOnly(new[]
            {
                "board",
                "card_art",
                "card_back",
                "leader",
                "castle",
                "faction_frame",
                "faction_icon",
                "resource_icon",
                "status_icon",
                "ui_icon",
                "font",
                "audio",
                "vfx",
                "animation_profile",
                "theme"
            });

        public ContentManifest(
            int manifestVersion,
            string schemaVersion,
            string contentVersion,
            IEnumerable<ContentAsset> assets,
            IEnumerable<ContentAlias>? aliases = null)
        {
            if (schemaVersion is null) throw new ArgumentNullException(nameof(schemaVersion));
            if (contentVersion is null) throw new ArgumentNullException(nameof(contentVersion));
            if (assets is null) throw new ArgumentNullException(nameof(assets));

            ManifestVersion = manifestVersion;
            SchemaVersion = schemaVersion;
            ContentVersion = contentVersion;
            Assets = new ReadOnlyCollection<ContentAsset>(new List<ContentAsset>(assets));
            Aliases = new ReadOnlyCollection<ContentAlias>(
                aliases is null ? new List<ContentAlias>() : new List<ContentAlias>(aliases));
        }

        public int ManifestVersion { get; }
        public string SchemaVersion { get; }
        public string ContentVersion { get; }
        public IReadOnlyList<ContentAsset> Assets { get; }
        public IReadOnlyList<ContentAlias> Aliases { get; }

        public static IReadOnlyCollection<string> Kinds => CanonicalKinds;

        public static bool IsStableId(string value)
        {
            if (value is null || value.Length == 0) return false;
            if (value[0] < 'a' || value[0] > 'z') return false;
            for (var index = 1; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= 'a' && character <= 'z')
                    || (character >= '0' && character <= '9')
                    || character == '_'))
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool IsCanonicalKind(string value)
        {
            foreach (var kind in CanonicalKinds)
            {
                if (string.Equals(kind, value, StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }

    /// <summary>One stable logical asset entry in a content manifest.</summary>
    public sealed class ContentAsset
    {
        public ContentAsset(
            string id,
            string kind,
            string sourceType,
            string? relativePath,
            string? sha256,
            string? fallbackId,
            string status)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            SourceType = sourceType ?? throw new ArgumentNullException(nameof(sourceType));
            RelativePath = relativePath;
            Sha256 = sha256;
            FallbackId = fallbackId;
            Status = status ?? throw new ArgumentNullException(nameof(status));
        }

        public string Id { get; }
        public string AssetId => Id;
        public string Kind { get; }
        public string SourceType { get; }
        public string? RelativePath { get; }
        public string? Path => RelativePath;
        public string? Sha256 { get; }
        public string? FallbackId { get; }
        public string? FallbackAssetId => FallbackId;
        public string Status { get; }
    }

    /// <summary>One stable alias from a legacy/content reference to an asset ID.</summary>
    public sealed class ContentAlias
    {
        public ContentAlias(string fromId, string toId)
        {
            FromId = fromId ?? throw new ArgumentNullException(nameof(fromId));
            ToId = toId ?? throw new ArgumentNullException(nameof(toId));
        }

        public string FromId { get; }
        public string ToId { get; }
    }
}
