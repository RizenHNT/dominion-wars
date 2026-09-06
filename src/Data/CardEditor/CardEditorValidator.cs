using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using DominionWars.Engine.Model;

namespace DominionWars.Data.CardEditor
{
    public enum CardValidationSeverity
    {
        Info,
        Warning,
        Error,
        Blocking
    }

    public sealed class CardValidationIssue
    {
        public CardValidationIssue(
            string code,
            CardValidationSeverity severity,
            string message,
            string? path = null)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Path = path;
            Severity = severity;
        }

        public string Code { get; }
        public CardValidationSeverity Severity { get; }
        public string Message { get; }
        public string? Path { get; }
        public bool IsError => Severity == CardValidationSeverity.Error || Severity == CardValidationSeverity.Blocking;
    }

    public sealed class CardValidationResult
    {
        internal CardValidationResult(IEnumerable<CardValidationIssue> issues)
        {
            Issues = new ReadOnlyCollection<CardValidationIssue>(
                (issues ?? throw new ArgumentNullException(nameof(issues))).ToList());
        }

        public IReadOnlyList<CardValidationIssue> Issues { get; }
        public bool IsValid => Issues.All(issue => !issue.IsError);
        public bool IsSaveable => IsValid;
        public bool IsProductionReady => IsValid
            && Issues.All(issue => issue.Severity != CardValidationSeverity.Warning);
        public IReadOnlyList<CardValidationIssue> Errors =>
            new ReadOnlyCollection<CardValidationIssue>(Issues.Where(issue => issue.IsError).ToList());
        public IReadOnlyList<CardValidationIssue> Warnings =>
            new ReadOnlyCollection<CardValidationIssue>(Issues
                .Where(issue => issue.Severity == CardValidationSeverity.Warning)
                .ToList());
    }

    /// <summary>
    /// Validates an authoring CardDocument without reimplementing gameplay.
    /// CardCatalog remains the semantic card validator; this class adds the
    /// schema-boundary, lifecycle, duplicate-ID, and content asset checks that
    /// the editor needs before handing a document to a writer.
    /// </summary>
    public sealed class CardEditorValidator
    {
        private readonly CardEditorMetadataRegistry _metadata;

        public CardEditorValidator(CardEditorMetadataRegistry metadata)
        {
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        public CardValidationResult Validate(
            CardDocument document,
            ContentCatalog? contentCatalog = null,
            CardDocumentIndex? existingCards = null,
            bool production = false)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));

            var issues = new List<CardValidationIssue>();
            var json = document.ToJsonObject();
            ValidateKnownFields(json, issues);
            ValidateRequiredFields(document, issues);
            ValidateLifecycle(document, issues, production);
            ValidateDuplicateId(document, existingCards, issues);
            ValidateArtReference(document, contentCatalog, issues, production);
            ValidateEffectList(document, _metadata, issues, CardEffectSpecEditor.OnPlayEffectsField);
            ValidateEffectList(document, _metadata, issues, CardEffectSpecEditor.AmbushEffectsField);
            ValidateEffectList(document, _metadata, issues, CardEffectSpecEditor.PunishEffectsField);
            ValidateWithCardCatalog(document, issues);
            return new CardValidationResult(issues);
        }

        public CardValidationResult ValidateForProduction(
            CardDocument document,
            ContentCatalog? contentCatalog = null,
            CardDocumentIndex? existingCards = null)
        {
            return Validate(document, contentCatalog, existingCards, production: true);
        }

        private void ValidateKnownFields(
            Newtonsoft.Json.Linq.JObject json,
            ICollection<CardValidationIssue> issues)
        {
            foreach (var property in json.Properties())
            {
                if (!_metadata.IsKnownCardField(property.Name))
                {
                    issues.Add(new CardValidationIssue(
                        "card.field.unknown",
                        CardValidationSeverity.Error,
                        "Unknown card field: " + property.Name + ".",
                        "/" + property.Name));
                }
            }
        }

        private void ValidateRequiredFields(
            CardDocument document,
            ICollection<CardValidationIssue> issues)
        {
            foreach (var field in _metadata.Fields.Where(field => field.Required))
            {
                var value = document.GetField(field.Key);
                if (value is null || value.Type == Newtonsoft.Json.Linq.JTokenType.Null)
                {
                    issues.Add(new CardValidationIssue(
                        "card.field.required",
                        CardValidationSeverity.Error,
                        "Required card field is missing: " + field.Key + ".",
                        field.JsonPath));
                }
            }
        }

        private static void ValidateLifecycle(
            CardDocument document,
            ICollection<CardValidationIssue> issues,
            bool production)
        {
            if (document.HasSerializedLifecycle && !document.SerializedLifecycleIsValid)
            {
                issues.Add(new CardValidationIssue(
                    "card.lifecycle.invalid",
                    CardValidationSeverity.Error,
                    "Card lifecycle status must be draft, ready, approved, or deprecated.",
                    "/status"));
            }

            if (production && document.Lifecycle != CardLifecycle.Approved)
            {
                issues.Add(new CardValidationIssue(
                    "card.lifecycle.production",
                    CardValidationSeverity.Error,
                    "Only approved cards may enter production content.",
                    "/status"));
            }
        }

        private static void ValidateDuplicateId(
            CardDocument document,
            CardDocumentIndex? existingCards,
            ICollection<CardValidationIssue> issues)
        {
            if (existingCards is null || string.IsNullOrWhiteSpace(document.Id)) return;
            if (!existingCards.TryGet(document.Id, out var existing)) return;

            // Editing an existing file is allowed; a new document or a
            // document from another source path must not shadow its ID.
            var isSamePersistedDocument = existing.SourcePath is not null
                && document.SourcePath is not null
                && string.Equals(existing.SourcePath, document.SourcePath, StringComparison.OrdinalIgnoreCase);
            if (!isSamePersistedDocument)
            {
                issues.Add(new CardValidationIssue(
                    "card.id.duplicate",
                    CardValidationSeverity.Error,
                    "Card id is already used by another document: " + document.Id + ".",
                    "/id"));
            }
        }

        private static void ValidateArtReference(
            CardDocument document,
            ContentCatalog? contentCatalog,
            ICollection<CardValidationIssue> issues,
            bool production)
        {
            var token = document.GetField("artId");
            if (token is null || token.Type == Newtonsoft.Json.Linq.JTokenType.Null) return;
            if (token.Type != Newtonsoft.Json.Linq.JTokenType.String
                || string.IsNullOrWhiteSpace(token.ToObject<string>()))
            {
                issues.Add(new CardValidationIssue(
                    "card.art_id.invalid",
                    CardValidationSeverity.Error,
                    "artId must be a non-empty semantic asset ID.",
                    "/artId"));
                return;
            }

            if (contentCatalog is null)
            {
                issues.Add(new CardValidationIssue(
                    "card.art_catalog.unavailable",
                    CardValidationSeverity.Warning,
                    "artId cannot be checked without a ContentCatalog.",
                    "/artId"));
                return;
            }

            var requestedId = token.ToObject<string>()!;
            string resolvedId;
            try
            {
                resolvedId = contentCatalog.ResolveAlias(requestedId);
            }
            catch (InvalidDataException)
            {
                issues.Add(new CardValidationIssue(
                    "card.art_id.unknown",
                    CardValidationSeverity.Error,
                    "artId does not resolve to a registered content asset: " + requestedId + ".",
                    "/artId"));
                return;
            }

            if (!contentCatalog.Assets.TryGetValue(resolvedId, out var asset)
                || !string.Equals(asset.Kind, "card_art", StringComparison.Ordinal))
            {
                issues.Add(new CardValidationIssue(
                    "card.art_id.kind",
                    CardValidationSeverity.Error,
                    "artId must resolve to a card_art asset: " + requestedId + ".",
                    "/artId"));
                return;
            }

            if (string.Equals(asset.Status, "draft", StringComparison.Ordinal))
            {
                issues.Add(new CardValidationIssue(
                    "card.art_id.draft",
                    production ? CardValidationSeverity.Error : CardValidationSeverity.Warning,
                    production
                        ? "Draft art assets cannot be referenced by production cards: " + requestedId + "."
                        : "artId references a draft asset: " + requestedId + ".",
                    "/artId"));
            }
        }

        private static void ValidateWithCardCatalog(
            CardDocument document,
            ICollection<CardValidationIssue> issues)
        {
            var temporaryPath = Path.Combine(
                Path.GetTempPath(),
                "dw-card-editor-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(temporaryPath, document.ToJson(), new UTF8Encoding(false));
                CardCatalog.LoadFile(temporaryPath);
            }
            catch (InvalidDataException exception)
            {
                issues.Add(new CardValidationIssue(
                    "card.catalog.invalid",
                    CardValidationSeverity.Error,
                    "CardCatalog rejected the document: " + exception.Message + "."));
            }
            catch (IOException exception)
            {
                issues.Add(new CardValidationIssue(
                    "card.catalog.io",
                    CardValidationSeverity.Error,
                    "CardCatalog validation could not read the temporary document: " + exception.Message + "."));
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // A validation scratch file is not part of project data;
                    // cleanup failure is intentionally not allowed to mask the
                    // authoritative validation result.
                }
                catch (UnauthorizedAccessException)
                {
                    // See the IOException note above.
                }
            }
        }

        private static void ValidateEffectList(
            CardDocument document,
            CardEditorMetadataRegistry metadata,
            ICollection<CardValidationIssue> issues,
            string fieldName)
        {
            var token = document.GetField(fieldName);
            if (token is null) return;
            if (token is not Newtonsoft.Json.Linq.JArray array)
            {
                // CardCatalog reports the authoritative shape failure. Keep
                // this editor-side pass focused on the nested schema entries.
                return;
            }

            for (var index = 0; index < array.Count; index++)
            {
                var entry = CardEffectSpecEditor.FromJsonToken(array[index]!);
                if (entry.IsSupported(metadata, out var reason)) continue;
                issues.Add(new CardValidationIssue(
                    "card.effect.unsupported",
                    CardValidationSeverity.Error,
                    fieldName + "[" + index + "] is not supported by the current EffectSpec schema: " + reason,
                    "/" + fieldName + "/" + index));
            }
        }
    }
}
