using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DominionWars.Data.CardEditor
{
    public enum CardFieldValueKind
    {
        Unknown,
        String,
        Integer,
        Number,
        Boolean,
        Array,
        Object
    }

    /// <summary>
    /// UI-neutral metadata read directly from cards.schema.json. It describes
    /// shape and constraints; it does not contain gameplay behavior.
    /// </summary>
    public sealed class CardFieldMetadata
    {
        private readonly JToken? _defaultValue;
        private readonly IReadOnlyList<string> _allowedValues;
        private readonly IReadOnlyList<string> _itemAllowedValues;

        internal CardFieldMetadata(
            string key,
            CardFieldValueKind valueKind,
            bool required,
            string? definitionName,
            string? itemsDefinitionName,
            IEnumerable<string> allowedValues,
            IEnumerable<string> itemAllowedValues,
            int? minimum,
            int? maximum,
            int? minLength,
            int? maxLength,
            string? pattern,
            JToken? defaultValue,
            string? jsonPath = null)
        {
            Key = key;
            JsonPath = jsonPath ?? "/" + key;
            ValueKind = valueKind;
            Required = required;
            DefinitionName = definitionName;
            ItemsDefinitionName = itemsDefinitionName;
            _allowedValues = new ReadOnlyCollection<string>(
                (allowedValues ?? throw new ArgumentNullException(nameof(allowedValues))).ToList());
            _itemAllowedValues = new ReadOnlyCollection<string>(
                (itemAllowedValues ?? throw new ArgumentNullException(nameof(itemAllowedValues))).ToList());
            Minimum = minimum;
            Maximum = maximum;
            MinLength = minLength;
            MaxLength = maxLength;
            Pattern = pattern;
            _defaultValue = defaultValue?.DeepClone();
        }

        public string Key { get; }
        public string JsonPath { get; }
        public CardFieldValueKind ValueKind { get; }
        public bool Required { get; }
        public string? DefinitionName { get; }
        public string? ItemsDefinitionName { get; }
        public IReadOnlyList<string> AllowedValues => _allowedValues;
        public IReadOnlyList<string> ItemAllowedValues => _itemAllowedValues;
        public int? Minimum { get; }
        public int? Maximum { get; }
        public int? MinLength { get; }
        public int? MaxLength { get; }
        public string? Pattern { get; }
        public JToken? DefaultValue => _defaultValue?.DeepClone();
    }

    /// <summary>
    /// Conditional required fields represented by the schema's allOf/if/then
    /// clauses, such as MINION requiring attack and health.
    /// </summary>
    public sealed class CardConditionalRequirement
    {
        internal CardConditionalRequirement(
            string conditionField,
            string conditionValue,
            IEnumerable<string> requiredFields)
        {
            ConditionField = conditionField;
            ConditionValue = conditionValue;
            RequiredFields = new ReadOnlyCollection<string>(
                (requiredFields ?? throw new ArgumentNullException(nameof(requiredFields))).ToList());
        }

        public string ConditionField { get; }
        public string ConditionValue { get; }
        public IReadOnlyList<string> RequiredFields { get; }
    }

    /// <summary>
    /// Schema-driven field and enum registry for the Card Editor.
    ///
    /// No action, target, keyword, faction, or win-condition values are
    /// hard-coded here. They are exposed from the corresponding schema
    /// definitions so a schema change automatically changes editor choices.
    /// </summary>
    public sealed class CardEditorMetadataRegistry
    {
        private readonly IReadOnlyDictionary<string, CardFieldMetadata> _fields;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _enumValues;
        private readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> _definitionFields;
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, CardFieldMetadata>>
            _definitionFieldMetadata;

        private CardEditorMetadataRegistry(
            string schemaId,
            IEnumerable<CardFieldMetadata> fields,
            IEnumerable<CardConditionalRequirement> conditionalRequirements,
            IReadOnlyDictionary<string, IReadOnlyList<string>> enumValues,
            IReadOnlyDictionary<string, IReadOnlyCollection<string>> definitionFields,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, CardFieldMetadata>> definitionFieldMetadata)
        {
            SchemaId = schemaId;
            var orderedFields = fields.OrderBy(field => field.Key, StringComparer.Ordinal).ToList();
            Fields = new ReadOnlyCollection<CardFieldMetadata>(orderedFields);
            _fields = new ReadOnlyDictionary<string, CardFieldMetadata>(
                orderedFields.ToDictionary(field => field.Key, field => field, StringComparer.Ordinal));
            ConditionalRequirements = new ReadOnlyCollection<CardConditionalRequirement>(
                conditionalRequirements.ToList());
            _enumValues = new ReadOnlyDictionary<string, IReadOnlyList<string>>(
                enumValues.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            _definitionFields = new ReadOnlyDictionary<string, IReadOnlyCollection<string>>(
                definitionFields.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            _definitionFieldMetadata = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, CardFieldMetadata>>(
                definitionFieldMetadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        }

        public string SchemaId { get; }
        public IReadOnlyList<CardFieldMetadata> Fields { get; }
        public IReadOnlyList<CardFieldMetadata> CardFields => Fields;
        public IReadOnlyList<CardConditionalRequirement> ConditionalRequirements { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> EnumValues => _enumValues;

        public IReadOnlyList<string> EffectActions => GetEnumValues("EffectAction");
        public IReadOnlyList<string> EffectTargets => GetEnumValues("EffectTarget");
        public IReadOnlyList<string> Keywords => GetEnumValues("Keyword");
        public IReadOnlyList<string> WinConditions => GetEnumValues("WinCondition");
        public IReadOnlyList<string> Factions => GetEnumValues("Faction");
        public IReadOnlyList<string> CardTypes => GetEnumValues("CardType");

        public static CardEditorMetadataRegistry LoadFile(string schemaPath)
        {
            if (schemaPath is null) throw new ArgumentNullException(nameof(schemaPath));
            if (!File.Exists(schemaPath)) throw new FileNotFoundException("Card schema is missing.", schemaPath);

            string json;
            try
            {
                json = File.ReadAllText(schemaPath, Encoding.UTF8);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw Invalid(schemaPath, "card schema cannot be read", exception);
            }

            return LoadJson(json, schemaPath);
        }

        public static CardEditorMetadataRegistry LoadJson(string json, string? sourcePath = null)
        {
            if (json is null) throw new ArgumentNullException(nameof(json));
            JToken token;
            try
            {
                token = JToken.Parse(json, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
            }
            catch (JsonException exception)
            {
                throw Invalid(sourcePath ?? "<memory>", "invalid card schema JSON", exception);
            }

            if (token is not JObject root)
            {
                throw Invalid(sourcePath ?? "<memory>", "card schema root must be an object");
            }

            if (!string.Equals(root["type"]?.ToObject<string>(), "array", StringComparison.Ordinal))
            {
                throw Invalid(sourcePath ?? "<memory>", "card schema root must describe an array");
            }

            if (root["$defs"] is not JObject definitions)
            {
                throw Invalid(sourcePath ?? "<memory>", "card schema must contain $defs");
            }

            if (definitions["Card"] is not JObject cardDefinition
                || cardDefinition["properties"] is not JObject cardProperties)
            {
                throw Invalid(sourcePath ?? "<memory>", "card schema must define Card.properties");
            }

            var required = ReadStrings(cardDefinition["required"]);
            var fields = new List<CardFieldMetadata>();
            foreach (var property in cardProperties.Properties().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                fields.Add(ParseField(property.Name, property.Value, required.Contains(property.Name), definitions));
            }

            var conditionalRequirements = ParseConditionalRequirements(cardDefinition["allOf"]);
            var enumValues = ParseEnumDefinitions(definitions);
            var definitionFields = ParseDefinitionFields(definitions);
            var definitionFieldMetadata = ParseDefinitionFieldMetadata(definitions);
            var schemaId = root["$id"]?.ToObject<string>() ?? sourcePath ?? "<memory>";
            return new CardEditorMetadataRegistry(
                schemaId,
                fields,
                conditionalRequirements,
                enumValues,
                definitionFields,
                definitionFieldMetadata);
        }

        public bool TryGetField(string key, out CardFieldMetadata metadata)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return _fields.TryGetValue(key, out metadata!);
        }

        public bool IsKnownCardField(string key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return _fields.ContainsKey(key);
        }

        public IReadOnlyList<string> GetEnumValues(string definitionName)
        {
            if (definitionName is null) throw new ArgumentNullException(nameof(definitionName));
            return _enumValues.TryGetValue(definitionName, out var values)
                ? values
                : Array.Empty<string>();
        }

        public bool IsKnownDefinitionField(string definitionName, string fieldName)
        {
            if (definitionName is null) throw new ArgumentNullException(nameof(definitionName));
            if (fieldName is null) throw new ArgumentNullException(nameof(fieldName));
            return _definitionFields.TryGetValue(definitionName, out var fields)
                && fields.Contains(fieldName, StringComparer.Ordinal);
        }

        public IReadOnlyCollection<string> GetDefinitionFields(string definitionName)
        {
            if (definitionName is null) throw new ArgumentNullException(nameof(definitionName));
            return _definitionFields.TryGetValue(definitionName, out var fields)
                ? fields
                : Array.Empty<string>();
        }

        /// <summary>
        /// Returns metadata for a field inside a named schema definition.
        /// Nested editors use this same parsed schema tree rather than keeping
        /// a second list of EffectSpec fields or legal values.
        /// </summary>
        public bool TryGetDefinitionField(
            string definitionName,
            string fieldName,
            out CardFieldMetadata metadata)
        {
            if (definitionName is null) throw new ArgumentNullException(nameof(definitionName));
            if (fieldName is null) throw new ArgumentNullException(nameof(fieldName));
            if (_definitionFieldMetadata.TryGetValue(definitionName, out var fields)
                && fields.TryGetValue(fieldName, out metadata!))
            {
                return true;
            }

            metadata = null!;
            return false;
        }

        /// <summary>Returns all field metadata for one nested definition.</summary>
        public IReadOnlyDictionary<string, CardFieldMetadata> GetDefinitionFieldMetadata(
            string definitionName)
        {
            if (definitionName is null) throw new ArgumentNullException(nameof(definitionName));
            return _definitionFieldMetadata.TryGetValue(definitionName, out var fields)
                ? fields
                : new ReadOnlyDictionary<string, CardFieldMetadata>(
                    new Dictionary<string, CardFieldMetadata>(StringComparer.Ordinal));
        }

        private static CardFieldMetadata ParseField(
            string key,
            JToken raw,
            bool required,
            JObject definitions,
            string? jsonPath = null)
        {
            var definitionName = RefName(raw["$ref"]?.ToObject<string>());
            var resolved = definitionName is not null && definitions[definitionName] is JObject definition
                ? definition
                : raw;
            var kind = ParseKind(resolved["type"]?.ToObject<string>());
            if (kind == CardFieldValueKind.Unknown && definitionName is not null)
            {
                kind = CardFieldValueKind.Object;
            }

            var allowedValues = ReadStrings(raw["enum"]);
            if (allowedValues.Count == 0) allowedValues = ReadStrings(resolved["enum"]);

            string? itemsDefinitionName = null;
            var itemAllowedValues = Array.Empty<string>();
            if (kind == CardFieldValueKind.Array && raw["items"] is JObject items)
            {
                itemsDefinitionName = RefName(items["$ref"]?.ToObject<string>());
                if (items["enum"] is not null)
                {
                    itemAllowedValues = ReadStrings(items["enum"]).ToArray();
                }
                else if (itemsDefinitionName is not null && definitions[itemsDefinitionName] is JObject itemDefinition)
                {
                    itemAllowedValues = ReadStrings(itemDefinition["enum"]).ToArray();
                }
            }

            return new CardFieldMetadata(
                key,
                kind,
                required,
                definitionName,
                itemsDefinitionName,
                allowedValues,
                itemAllowedValues,
                ReadInt(raw["minimum"] ?? resolved["minimum"]),
                ReadInt(raw["maximum"] ?? resolved["maximum"]),
                ReadInt(raw["minLength"] ?? resolved["minLength"]),
                ReadInt(raw["maxLength"] ?? resolved["maxLength"]),
                raw["pattern"]?.ToObject<string>() ?? resolved["pattern"]?.ToObject<string>(),
                raw["default"] ?? resolved["default"],
                jsonPath);
        }

        private static IReadOnlyList<CardConditionalRequirement> ParseConditionalRequirements(JToken? allOf)
        {
            var result = new List<CardConditionalRequirement>();
            if (allOf is not JArray clauses) return result;

            foreach (var clause in clauses.OfType<JObject>())
            {
                if (clause["if"] is not JObject condition
                    || condition["properties"] is not JObject properties
                    || clause["then"] is not JObject then
                    || then["required"] is not JArray thenRequired)
                {
                    continue;
                }

                var conditionProperty = properties.Properties().OrderBy(property => property.Name, StringComparer.Ordinal).FirstOrDefault();
                var conditionValue = conditionProperty?.Value["const"]?.ToObject<string>();
                if (conditionProperty is null || conditionValue is null) continue;

                result.Add(new CardConditionalRequirement(
                    conditionProperty.Name,
                    conditionValue,
                    ReadStrings(thenRequired)));
            }

            return result;
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseEnumDefinitions(JObject definitions)
        {
            var values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var definition in definitions.Properties().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                var enumValues = ReadStrings(definition.Value["enum"]);
                if (enumValues.Count > 0)
                {
                    values[definition.Name] = new ReadOnlyCollection<string>(enumValues.ToList());
                }
            }

            return values;
        }

        private static IReadOnlyDictionary<string, IReadOnlyCollection<string>> ParseDefinitionFields(JObject definitions)
        {
            var values = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
            foreach (var definition in definitions.Properties().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                if (definition.Value["properties"] is not JObject properties) continue;
                values[definition.Name] = new ReadOnlyCollection<string>(
                    properties.Properties().Select(property => property.Name)
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .ToList());
            }

            return values;
        }

        private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, CardFieldMetadata>>
            ParseDefinitionFieldMetadata(JObject definitions)
        {
            var values = new Dictionary<string, IReadOnlyDictionary<string, CardFieldMetadata>>(
                StringComparer.Ordinal);
            foreach (var definition in definitions.Properties().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                if (definition.Value is not JObject definitionObject
                    || definitionObject["properties"] is not JObject properties)
                {
                    continue;
                }

                var required = ReadStrings(definitionObject["required"]);
                var fields = new Dictionary<string, CardFieldMetadata>(StringComparer.Ordinal);
                foreach (var property in properties.Properties().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    fields[property.Name] = ParseField(
                        property.Name,
                        property.Value,
                        required.Contains(property.Name),
                        definitions,
                        "/" + definition.Name + "/" + property.Name);
                }

                values[definition.Name] = new ReadOnlyDictionary<string, CardFieldMetadata>(fields);
            }

            return values;
        }

        private static CardFieldValueKind ParseKind(string? type)
        {
            switch (type)
            {
                case "string":
                    return CardFieldValueKind.String;
                case "integer":
                    return CardFieldValueKind.Integer;
                case "number":
                    return CardFieldValueKind.Number;
                case "boolean":
                    return CardFieldValueKind.Boolean;
                case "array":
                    return CardFieldValueKind.Array;
                case "object":
                    return CardFieldValueKind.Object;
                default:
                    return CardFieldValueKind.Unknown;
            }
        }

        private static string? RefName(string? reference)
        {
            const string prefix = "#/$defs/";
            return reference is not null && reference.StartsWith(prefix, StringComparison.Ordinal)
                ? reference.Substring(prefix.Length)
                : null;
        }

        private static List<string> ReadStrings(JToken? token)
        {
            if (token is not JArray array) return new List<string>();
            return array.Values<string>().Where(value => value is not null).Cast<string>().ToList();
        }

        private static int? ReadInt(JToken? token)
        {
            return token?.Type == JTokenType.Integer && token.ToObject<int?>().HasValue
                ? token.ToObject<int?>()
                : null;
        }

        private static InvalidDataException Invalid(string source, string message, Exception? inner = null)
        {
            return new InvalidDataException(source + ": " + message, inner);
        }
    }
}
