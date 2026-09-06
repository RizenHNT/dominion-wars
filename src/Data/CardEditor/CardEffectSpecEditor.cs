using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DominionWars.Data.CardEditor
{
    /// <summary>
    /// Schema-backed authoring view of one EffectSpec entry.
    ///
    /// This type deliberately stores the original JSON token. An effect that
    /// uses a future action, target, property, or shape is therefore still
    /// round-trippable even though the current editor cannot modify it.
    /// </summary>
    public sealed class CardEffectSpecEditor
    {
        public const string DefinitionName = "EffectSpec";
        public const string OnPlayEffectsField = "onPlayEffects";
        public const string AmbushEffectsField = "ambushEffects";
        public const string PunishEffectsField = "punishEffects";
        public const string ActionField = "action";
        public const string TargetField = "target";
        public const string AmountField = "amount";
        public const string ParamField = "param";
        public const string ConditionField = "condition";
        public const string KingSlayerField = "kingSlayer";

        private readonly JToken _raw;
        private readonly JObject? _object;

        private CardEffectSpecEditor(JToken raw)
        {
            _raw = raw?.DeepClone() ?? throw new ArgumentNullException(nameof(raw));
            _object = _raw as JObject;
        }

        public bool IsObject => _object is not null;
        public JToken Raw => _raw.DeepClone();
        public string? Action => StringValue(ActionField);
        public string? Target => StringValue(TargetField);
        public int? Amount => IntegerValue(AmountField);
        public string? Param => StringValue(ParamField);
        public string? Condition => StringValue(ConditionField);
        public bool? KingSlayer => BooleanValue(KingSlayerField);

        /// <summary>Returns a defensive copy of the source JSON token.</summary>
        public JToken ToJsonToken() => Raw;

        /// <summary>
        /// Lists properties not declared by the schema's EffectSpec
        /// definition. Unknown properties are never removed by this editor.
        /// </summary>
        public IReadOnlyList<string> GetUnsupportedProperties(CardEditorMetadataRegistry metadata)
        {
            if (metadata is null) throw new ArgumentNullException(nameof(metadata));
            if (_object is null) return Array.Empty<string>();

            var result = _object.Properties()
                .Select(property => property.Name)
                .Where(name => !metadata.IsKnownDefinitionField(DefinitionName, name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            return new ReadOnlyCollection<string>(result);
        }

        /// <summary>
        /// Determines whether the complete entry is supported by the current
        /// schema metadata. A false result is a UI/read-only boundary, not a
        /// request to discard or normalize the source token.
        /// </summary>
        public bool IsSupported(CardEditorMetadataRegistry metadata, out string reason)
        {
            if (metadata is null) throw new ArgumentNullException(nameof(metadata));
            if (_object is null)
            {
                reason = "Effect entry must be a JSON object.";
                return false;
            }

            foreach (var property in _object.Properties())
            {
                if (!metadata.TryGetDefinitionField(DefinitionName, property.Name, out var field))
                {
                    reason = "Unsupported EffectSpec field is retained: " + property.Name + ".";
                    return false;
                }

                if (!MatchesField(field, property.Value))
                {
                    reason = "EffectSpec field has an unsupported value: " + property.Name + ".";
                    return false;
                }
            }

            if (!metadata.TryGetDefinitionField(DefinitionName, ActionField, out var actionField)
                || !MatchesField(actionField, _object[ActionField]))
            {
                reason = "EffectSpec action is required and must be a registered schema value.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Updates one schema-declared property after checking its metadata.
        /// Passing null removes an optional property; required properties are
        /// never removed. Unknown properties cannot be edited here, but remain
        /// untouched in the original token.
        /// </summary>
        public bool TrySetField(
            CardEditorMetadataRegistry metadata,
            string fieldName,
            JToken? value,
            out string error)
        {
            if (metadata is null) throw new ArgumentNullException(nameof(metadata));
            if (fieldName is null) throw new ArgumentNullException(nameof(fieldName));
            if (_object is null)
            {
                error = "Unsupported effect entry is read-only.";
                return false;
            }

            if (!metadata.TryGetDefinitionField(DefinitionName, fieldName, out var field))
            {
                error = "Unsupported EffectSpec field is read-only: " + fieldName + ".";
                return false;
            }

            if (value is null)
            {
                if (field.Required)
                {
                    error = "Required EffectSpec field cannot be removed: " + fieldName + ".";
                    return false;
                }

                _object.Remove(fieldName);
                error = string.Empty;
                return true;
            }

            if (!MatchesField(field, value))
            {
                error = "Value is not valid for schema field " + fieldName + ".";
                return false;
            }

            _object[fieldName] = value.DeepClone();
            error = string.Empty;
            return true;
        }

        public bool TrySetString(
            CardEditorMetadataRegistry metadata,
            string fieldName,
            string? value,
            out string error)
        {
            return TrySetField(
                metadata,
                fieldName,
                value is null ? null : new JValue(value),
                out error);
        }

        public bool TrySetInteger(
            CardEditorMetadataRegistry metadata,
            string fieldName,
            int value,
            out string error)
        {
            return TrySetField(metadata, fieldName, new JValue(value), out error);
        }

        public bool TrySetBoolean(
            CardEditorMetadataRegistry metadata,
            string fieldName,
            bool? value,
            out string error)
        {
            return TrySetField(
                metadata,
                fieldName,
                value.HasValue ? new JValue(value.Value) : null,
                out error);
        }

        public static CardEffectSpecEditor CreateNew(CardEditorMetadataRegistry metadata)
        {
            if (metadata is null) throw new ArgumentNullException(nameof(metadata));
            if (metadata.EffectActions.Count == 0)
            {
                throw new InvalidDataException(
                    "Card schema does not declare an EffectAction value; effect creation is unavailable.");
            }

            return new CardEffectSpecEditor(new JObject
            {
                [ActionField] = metadata.EffectActions[0]
            });
        }

        public static CardEffectSpecEditor FromJsonToken(JToken token)
        {
            if (token is null) throw new ArgumentNullException(nameof(token));
            return new CardEffectSpecEditor(token);
        }

        /// <summary>
        /// Reads an EffectSpec array without changing any source token. The
        /// same schema-backed editor is used by on-play, ambush, and punish
        /// response effects.
        /// </summary>
        public static IReadOnlyList<CardEffectSpecEditor> Read(
            CardDocument document,
            string fieldName = OnPlayEffectsField)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            if (fieldName is null) throw new ArgumentNullException(nameof(fieldName));

            var token = document.GetField(fieldName);
            if (token is null) return Array.Empty<CardEffectSpecEditor>();
            if (token is not JArray array)
            {
                throw new InvalidDataException(fieldName + " must be a JSON array.");
            }

            return new ReadOnlyCollection<CardEffectSpecEditor>(
                array.Select(FromJsonToken).ToList());
        }

        /// <summary>
        /// Builds a new array from defensive token copies. Order and unknown
        /// fields are preserved exactly apart from standard JSON formatting.
        /// </summary>
        public static JArray BuildArray(IEnumerable<CardEffectSpecEditor> entries)
        {
            if (entries is null) throw new ArgumentNullException(nameof(entries));
            var result = new JArray();
            foreach (var entry in entries)
            {
                if (entry is null) throw new ArgumentException("Effect entries cannot contain null.", nameof(entries));
                result.Add(entry.Raw);
            }

            return result;
        }

        public static void SetArray(
            CardDocument document,
            IEnumerable<CardEffectSpecEditor> entries,
            string fieldName = OnPlayEffectsField)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            if (fieldName is null) throw new ArgumentNullException(nameof(fieldName));
            document.SetField(fieldName, BuildArray(entries));
        }

        public static JArray Reorder(JArray source, int oldIndex, int newIndex)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (oldIndex < 0 || oldIndex >= source.Count) throw new ArgumentOutOfRangeException(nameof(oldIndex));
            if (newIndex < 0 || newIndex >= source.Count) throw new ArgumentOutOfRangeException(nameof(newIndex));

            var result = (JArray)source.DeepClone();
            var moved = result[oldIndex]!.DeepClone();
            result.RemoveAt(oldIndex);
            result.Insert(newIndex, moved);
            return result;
        }

        private string? StringValue(string fieldName)
        {
            return _object?[fieldName]?.Type == JTokenType.String
                ? _object[fieldName]!.ToObject<string>()
                : null;
        }

        private int? IntegerValue(string fieldName)
        {
            return _object?[fieldName]?.Type == JTokenType.Integer
                ? _object[fieldName]!.ToObject<int>()
                : null;
        }

        private bool? BooleanValue(string fieldName)
        {
            return _object?[fieldName]?.Type == JTokenType.Boolean
                ? _object[fieldName]!.ToObject<bool>()
                : (bool?)null;
        }

        private static bool MatchesField(CardFieldMetadata field, JToken? value)
        {
            if (value is null || value.Type == JTokenType.Null) return false;

            var typeMatches = field.ValueKind switch
            {
                CardFieldValueKind.String => value.Type == JTokenType.String,
                CardFieldValueKind.Integer => value.Type == JTokenType.Integer,
                CardFieldValueKind.Number => value.Type == JTokenType.Integer || value.Type == JTokenType.Float,
                CardFieldValueKind.Boolean => value.Type == JTokenType.Boolean,
                CardFieldValueKind.Array => value.Type == JTokenType.Array,
                CardFieldValueKind.Object => value.Type == JTokenType.Object,
                _ => false
            };
            if (!typeMatches) return false;

            if (field.AllowedValues.Count > 0
                && (value.Type != JTokenType.String
                    || !field.AllowedValues.Contains(value.ToObject<string>()!, StringComparer.Ordinal)))
            {
                return false;
            }

            if (field.ValueKind == CardFieldValueKind.Integer)
            {
                long integer;
                try
                {
                    integer = value.ToObject<long>();
                }
                catch (Exception exception) when (
                    exception is FormatException
                    || exception is OverflowException
                    || exception is InvalidCastException)
                {
                    return false;
                }

                if (field.Minimum.HasValue && integer < field.Minimum.Value) return false;
                if (field.Maximum.HasValue && integer > field.Maximum.Value) return false;
            }

            if (field.ValueKind == CardFieldValueKind.String)
            {
                var text = value.ToObject<string>() ?? string.Empty;
                if (field.MinLength.HasValue && text.Length < field.MinLength.Value) return false;
                if (field.MaxLength.HasValue && text.Length > field.MaxLength.Value) return false;
            }

            return true;
        }
    }
}
