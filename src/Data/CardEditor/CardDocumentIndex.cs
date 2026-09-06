using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace DominionWars.Data.CardEditor
{
    /// <summary>
    /// Deterministic read-only index of card documents. The index owns clones
    /// of its inputs so an editor mutation cannot silently change search state.
    /// </summary>
    public sealed class CardDocumentIndex
    {
        private readonly IReadOnlyDictionary<string, CardDocument> _cards;
        private readonly IReadOnlyList<CardDocument> _documents;

        public CardDocumentIndex(IEnumerable<CardDocument> documents)
        {
            if (documents is null) throw new ArgumentNullException(nameof(documents));

            var cards = new Dictionary<string, CardDocument>(StringComparer.Ordinal);
            foreach (var document in documents)
            {
                if (document is null)
                {
                    throw new InvalidDataException("card document index cannot contain a null document");
                }

                if (string.IsNullOrWhiteSpace(document.Id))
                {
                    throw new InvalidDataException(
                        "card document index requires a non-empty card id: "
                        + (document.SourcePath ?? "<memory>"));
                }

                if (!cards.TryAdd(document.Id, document.Clone()))
                {
                    throw new InvalidDataException("duplicate card id in card document index: " + document.Id);
                }
            }

            var ordered = cards
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value)
                .ToList();
            _documents = new ReadOnlyCollection<CardDocument>(ordered);
            _cards = new ReadOnlyDictionary<string, CardDocument>(
                ordered.ToDictionary(document => document.Id!, document => document, StringComparer.Ordinal));
        }

        public IReadOnlyList<CardDocument> Documents => _documents;
        public IReadOnlyDictionary<string, CardDocument> Cards => _cards;

        public static CardDocumentIndex LoadDirectory(string directory)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);

            var files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length == 0)
            {
                throw new InvalidDataException("No card JSON files were found.");
            }

            var documents = new List<CardDocument>();
            foreach (var file in files)
            {
                documents.AddRange(CardDocument.LoadCollectionFile(file));
            }

            return new CardDocumentIndex(documents);
        }

        public bool TryGet(string cardId, out CardDocument document)
        {
            if (cardId is null) throw new ArgumentNullException(nameof(cardId));
            return _cards.TryGetValue(cardId, out document!);
        }

        /// <summary>
        /// Searches stable card metadata using case-insensitive ordinal
        /// matching. Results are always ordered by cardId using ordinal order.
        /// </summary>
        public IReadOnlyList<CardDocument> Search(CardDocumentSearchQuery? query = null)
        {
            query ??= CardDocumentSearchQuery.Empty;
            var results = _documents.Where(document => query.Matches(document)).ToList();
            return new ReadOnlyCollection<CardDocument>(results);
        }
    }

    /// <summary>Optional filters for CardDocumentIndex.Search.</summary>
    public sealed class CardDocumentSearchQuery
    {
        public static CardDocumentSearchQuery Empty { get; } = new CardDocumentSearchQuery();

        public CardDocumentSearchQuery(
            string? text = null,
            string? cardId = null,
            string? faction = null,
            string? type = null,
            CardLifecycle? lifecycle = null,
            string? artId = null)
        {
            Text = Normalize(text);
            CardId = Normalize(cardId);
            Faction = Normalize(faction);
            Type = Normalize(type);
            Lifecycle = lifecycle;
            ArtId = Normalize(artId);
        }

        public string? Text { get; }
        public string? CardId { get; }
        public string? Faction { get; }
        public string? Type { get; }
        public CardLifecycle? Lifecycle { get; }
        public CardLifecycle? Status => Lifecycle;
        public string? ArtId { get; }

        internal bool Matches(CardDocument document)
        {
            if (!Contains(document.Id, CardId)
                || !Contains(document.Faction, Faction)
                || !Contains(document.Type, Type)
                || !Contains(document.ArtId, ArtId)
                || (Lifecycle.HasValue && document.Lifecycle != Lifecycle.Value))
            {
                return false;
            }

            if (Text is null) return true;
            return Contains(document.Id, Text)
                || Contains(document.Name, Text)
                || Contains(document.Faction, Text)
                || Contains(document.Type, Text)
                || Contains(document.ArtId, Text)
                || Contains(CardLifecycleCodec.ToWireValue(document.Lifecycle), Text);
        }

        private static string? Normalize(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool Contains(string? value, string? query)
        {
            return query is null
                || (value is not null && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
