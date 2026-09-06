using System;
using System.Collections.Generic;

namespace DominionWars.Data
{
    /// <summary>
    /// Immutable, presentation-facing metadata copied from a loaded card
    /// definition. It intentionally contains only printed or declared card
    /// values; it does not expose gameplay effects or runtime state.
    /// </summary>
    public sealed class CardPresentationMetadata
    {
        public CardPresentationMetadata(
            string id,
            string name,
            string type,
            string faction,
            bool isMinion,
            bool isLeader,
            int? printedAttack,
            int? printedHealth,
            int printedPunish,
            int declaredCost,
            int punishCost,
            bool punishActivatable,
            int commitCost,
            int uploadCost,
            int downloadCost,
            string text,
            string? flavor,
            IEnumerable<string>? keywords,
            IEnumerable<string>? tags,
            string? artId,
            bool? hasCommitCost = null,
            bool? hasUploadCost = null,
            bool? hasDownloadCost = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A card id is required.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A card name is required.", nameof(name));
            }

            Id = id;
            Name = name;
            Type = type ?? string.Empty;
            Faction = faction ?? string.Empty;
            IsMinion = isMinion;
            IsLeader = isLeader;
            PrintedAttack = printedAttack;
            PrintedHealth = printedHealth;
            PrintedPunish = printedPunish;
            DeclaredCost = declaredCost;
            PunishCost = punishCost;
            PunishActivatable = punishActivatable;
            CommitCost = commitCost;
            UploadCost = uploadCost;
            DownloadCost = downloadCost;
            HasCommitCost = hasCommitCost ?? commitCost != 0;
            HasUploadCost = hasUploadCost ?? uploadCost != 0;
            HasDownloadCost = hasDownloadCost ?? downloadCost != 0;
            Text = text ?? string.Empty;
            Flavor = flavor;
            Keywords = CopyStrings(keywords);
            Tags = CopyStrings(tags);
            ArtId = artId;
        }

        public string Id { get; }
        public string Name { get; }
        public string Type { get; }
        public string Faction { get; }
        public bool IsMinion { get; }
        public bool IsLeader { get; }

        /// <summary>Printed/base attack. Null means the card is not a minion.</summary>
        public int? PrintedAttack { get; }

        /// <summary>Printed/base health. Null means the card is not a minion.</summary>
        public int? PrintedHealth { get; }

        public int PrintedPunish { get; }
        public int DeclaredCost { get; }
        public int PunishCost { get; }
        public bool PunishActivatable { get; }
        public int CommitCost { get; }
        public int UploadCost { get; }
        public int DownloadCost { get; }
        /// <summary>True only when the source card explicitly declared commitCost.</summary>
        public bool HasCommitCost { get; }
        /// <summary>True only when the source card explicitly declared uploadCost.</summary>
        public bool HasUploadCost { get; }
        /// <summary>True only when the source card explicitly declared downloadCost.</summary>
        public bool HasDownloadCost { get; }
        public string Text { get; }
        public string? Flavor { get; }
        public IReadOnlyCollection<string> Keywords { get; }
        public IReadOnlyCollection<string> Tags { get; }
        public string? ArtId { get; }

        private static IReadOnlyCollection<string> CopyStrings(IEnumerable<string>? values)
        {
            var copy = new List<string>();
            if (values is not null)
            {
                foreach (var value in values)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        copy.Add(value);
                    }
                }
            }

            return copy.AsReadOnly();
        }
    }
}
