using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DominionWars.Data.CardEditor
{
    /// <summary>
    /// Mutable authoring view of one card JSON object.
    ///
    /// The object remains a JSON document instead of a second CardDefinition
    /// model. Gameplay validation is delegated to CardCatalog and the schema
    /// registry; this type only provides safe document editing primitives.
    /// </summary>
    public sealed class CardDocument
    {
        private readonly JObject _json;

        private CardDocument(
            JObject json,
            string? sourcePath,
            string? baselineHash,
            bool cameFromArrayEnvelope,
            CardLifecycle lifecycle,
            bool hasSerializedLifecycle,
            bool serializedLifecycleIsValid)
        {
            _json = json ?? throw new ArgumentNullException(nameof(json));
            SourcePath = sourcePath;
            BaselineHash = baselineHash;
            CameFromArrayEnvelope = cameFromArrayEnvelope;
            Lifecycle = lifecycle;
            HasSerializedLifecycle = hasSerializedLifecycle;
            SerializedLifecycleIsValid = serializedLifecycleIsValid;
        }

        public string? SourcePath { get; }

        /// <summary>
        /// SHA-256 of the bytes read by <see cref="LoadFile"/>. A null value
        /// means the document has not yet been persisted.
        /// </summary>
        public string? BaselineHash { get; internal set; }

        /// <summary>
        /// True when the source file used a one-card JSON array envelope.
        /// Existing production card files use arrays; the store preserves this
        /// convention when saving an existing document.
        /// </summary>
        public bool CameFromArrayEnvelope { get; }

        /// <summary>
        /// Authoring lifecycle. It is not emitted into the runtime card JSON.
        /// Use CardLifecycleStore when lifecycle persistence is required.
        /// </summary>
        public CardLifecycle Lifecycle { get; set; }

        /// <summary>Whether the source JSON contained an editor status field.</summary>
        public bool HasSerializedLifecycle { get; }

        /// <summary>
        /// False only when an explicit status was present but was not one of
        /// the four supported lifecycle values. Missing status is compatible
        /// and therefore defaults to Approved.
        /// </summary>
        public bool SerializedLifecycleIsValid { get; }

        public string? Id => StringValue("id");
        public string? Name => StringValue("name");
        public string? Faction => StringValue("faction");
        public string? Type => StringValue("type");
        public string? ArtId => StringValue("artId");
        public string? Rarity => StringValue("rarity");
        public string? Text => StringValue("text");

        public static CardDocument CreateNew(
            string id,
            string name,
            string faction,
            string type,
            CardLifecycle lifecycle = CardLifecycle.Draft)
        {
            if (id is null) throw new ArgumentNullException(nameof(id));
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (faction is null) throw new ArgumentNullException(nameof(faction));
            if (type is null) throw new ArgumentNullException(nameof(type));

            var json = new JObject
            {
                ["id"] = id,
                ["name"] = name,
                ["faction"] = faction,
                ["type"] = type,
                ["text"] = string.Empty
            };
            return new CardDocument(json, null, null, true, lifecycle, false, true);
        }

        public static CardDocument LoadFile(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("Card document is missing.", path);

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw Invalid(path, "card document cannot be read", exception);
            }

            return LoadJson(Encoding.UTF8.GetString(bytes), path, Sha256(bytes));
        }

        /// <summary>
        /// Loads a single card object. A one-element array is also accepted so
        /// a newly created document can use the same envelope as production
        /// card files. Multi-card files should be read with LoadCollectionFile.
        /// </summary>
        public static CardDocument LoadJson(
            string json,
            string? sourcePath = null,
            string? baselineHash = null)
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
                throw Invalid(sourcePath ?? "<memory>", "invalid JSON", exception);
            }

            var fromArray = false;
            JObject? objectValue = token as JObject;
            if (objectValue is null && token is JArray array)
            {
                if (array.Count != 1 || array[0] is not JObject)
                {
                    throw Invalid(sourcePath ?? "<memory>", "card document must contain exactly one card object");
                }

                fromArray = true;
                objectValue = (JObject)array[0];
            }

            if (objectValue is null)
            {
                throw Invalid(sourcePath ?? "<memory>", "card document root must be an object or one-card array");
            }

            var lifecycle = ReadLifecycle(objectValue, out var hasSerializedLifecycle, out var serializedLifecycleIsValid);
            return new CardDocument(
                CanonicalClone(objectValue),
                sourcePath,
                baselineHash,
                fromArray,
                lifecycle,
                hasSerializedLifecycle,
                serializedLifecycleIsValid);
        }

        /// <summary>Loads every card from a production array file.</summary>
        public static IReadOnlyList<CardDocument> LoadCollectionFile(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("Card document is missing.", path);

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw Invalid(path, "card document cannot be read", exception);
            }

            JToken token;
            try
            {
                token = JToken.Parse(Encoding.UTF8.GetString(bytes), new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
            }
            catch (JsonException exception)
            {
                throw Invalid(path, "invalid JSON", exception);
            }

            var baselineHash = Sha256(bytes);
            var objects = token is JArray array
                ? array.Children().ToArray()
                : new[] { token };
            var documents = new List<CardDocument>(objects.Length);
            foreach (var item in objects)
            {
                if (item is not JObject objectValue)
                {
                    throw Invalid(path, "card collection contains a non-object entry");
                }

                var lifecycle = ReadLifecycle(objectValue, out var hasSerializedLifecycle, out var serializedLifecycleIsValid);
                documents.Add(new CardDocument(
                    CanonicalClone(objectValue),
                    path,
                    baselineHash,
                    token is JArray,
                    lifecycle,
                    hasSerializedLifecycle,
                    serializedLifecycleIsValid));
            }

            if (documents.Count == 0)
            {
                throw Invalid(path, "card collection is empty");
            }

            return documents.AsReadOnly();
        }

        public bool HasField(string key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return _json.Property(key, StringComparison.Ordinal) is not null;
        }

        public JToken? GetField(string key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            var property = _json.Property(key, StringComparison.Ordinal);
            return property?.Value.DeepClone();
        }

        /// <summary>
        /// Sets a JSON field without interpreting gameplay semantics. The
        /// schema/validator remains responsible for allowed fields and values.
        /// </summary>
        public void SetField(string key, JToken value)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (key.Length == 0) throw new ArgumentException("A field key is required.", nameof(key));
            _json[key] = value.DeepClone();
        }

        public void RemoveField(string key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            _json.Remove(key);
        }

        public void SetString(string key, string? value) => SetField(key, value is null ? JValue.CreateNull() : new JValue(value));
        public void SetInteger(string key, int value) => SetField(key, new JValue(value));
        public void SetBoolean(string key, bool value) => SetField(key, new JValue(value));

        public void SetStringArray(string key, IEnumerable<string> values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            SetField(key, new JArray(values.Select(value => value ?? string.Empty)));
        }

        public JObject ToJsonObject()
        {
            return (JObject)_json.DeepClone();
        }

        public string ToJson(Formatting formatting = Formatting.Indented)
        {
            return _json.ToString(formatting);
        }

        /// <summary>
        /// Returns an authoring-only representation that includes lifecycle
        /// metadata. The canonical ToJson/ToCardFileJson output intentionally
        /// remains compatible with cards.schema.json, which has no status
        /// property.
        /// </summary>
        public JObject ToEditorJsonObject()
        {
            var result = ToJsonObject();
            result["status"] = CardLifecycleCodec.ToWireValue(Lifecycle);
            return result;
        }

        public string ToEditorJson(Formatting formatting = Formatting.Indented)
        {
            return ToEditorJsonObject().ToString(formatting);
        }

        public string ToCardFileJson(Formatting formatting = Formatting.Indented)
        {
            if (!CameFromArrayEnvelope)
            {
                return ToJson(formatting);
            }

            var array = new JArray(_json.DeepClone());
            return array.ToString(formatting);
        }

        public CardDocument Clone()
        {
            return new CardDocument(
                (JObject)_json.DeepClone(),
                SourcePath,
                BaselineHash,
                CameFromArrayEnvelope,
                Lifecycle,
                HasSerializedLifecycle,
                SerializedLifecycleIsValid);
        }

        private string? StringValue(string key)
        {
            var token = _json[key];
            return token?.Type == JTokenType.String ? token.ToObject<string>() : null;
        }

        private static JObject CanonicalClone(JObject objectValue)
        {
            var clone = (JObject)objectValue.DeepClone();
            // status is authoring metadata, not part of cards.schema.json.
            clone.Remove("status");
            return clone;
        }

        private static CardLifecycle ReadLifecycle(
            JObject objectValue,
            out bool hasSerializedLifecycle,
            out bool serializedLifecycleIsValid)
        {
            var statusToken = objectValue["status"];
            hasSerializedLifecycle = statusToken is not null;
            var status = statusToken?.Type == JTokenType.String ? statusToken.ToObject<string>() : null;
            serializedLifecycleIsValid = !hasSerializedLifecycle
                || CardLifecycleCodec.TryParse(status, out _);
            return CardLifecycleCodec.TryParse(status, out var lifecycle)
                ? lifecycle
                : CardLifecycle.Approved;
        }

        private static InvalidDataException Invalid(string source, string message, Exception? inner = null)
        {
            return new InvalidDataException(source + ": " + message, inner);
        }

        private static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        }
    }
}
