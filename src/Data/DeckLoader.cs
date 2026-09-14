using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DominionWars.Data
{
    public sealed class DeckDefinition
    {
        public DeckDefinition(string name, string faction, string leader, IReadOnlyDictionary<string, int> cards) { Name = name; Faction = faction; Leader = leader; Cards = cards; }
        public string Name { get; }
        public string Faction { get; }
        public string Leader { get; }
        public IReadOnlyDictionary<string, int> Cards { get; }
    }

    public static class DeckLoader
    {
        private static readonly HashSet<string> Factions = new HashSet<string>(new[] { "赫萨廷", "克莱恩书院", "纳维恩诸邑", "依兰维索", "无阵营" }, StringComparer.Ordinal);
        public static IReadOnlyList<DeckDefinition> LoadDirectory(string directory, Action<string>? warning = null)
        {
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            var files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (files.Length == 0) throw new InvalidDataException("No deck JSON files were found.");
            var result = new List<DeckDefinition>();
            foreach (var file in files) result.Add(LoadFile(file, warning));
            return result;
        }

        public static DeckDefinition LoadFile(string file, Action<string>? warning = null)
        {
            if (!File.Exists(file)) throw new FileNotFoundException("Deck JSON file is missing.", file);
            try
            {
                var root = JToken.Parse(File.ReadAllText(file));
                if (root.Type != JTokenType.Object) throw Invalid(file, "root must be an object");
                var known = new HashSet<string>(new[] { "name", "faction", "leader", "cards" }, StringComparer.Ordinal);
                if (warning != null) foreach (var property in root.Children<JProperty>()) if (!known.Contains(property.Name)) warning(file + ": unknown field " + property.Name);
                var name = Required(root, "name", file);
                var faction = Required(root, "faction", file);
                var leader = Required(root, "leader", file);
                if (!Factions.Contains(faction)) throw Invalid(file, "invalid faction");
                if (!TryGetProperty(root, "cards", out var cardObject) || cardObject.Type != JTokenType.Object || cardObject.Children<JProperty>().Any(property => !TryReadInt64(property.Value, out var count) || count < 1 || count > int.MaxValue)) throw Invalid(file, "cards must map ids to positive counts");
                var cards = cardObject.Children<JProperty>().ToDictionary(property => property.Name, property => property.Value.Value<int>(), StringComparer.Ordinal);
                return new DeckDefinition(name, faction, leader, cards);
            }
            catch (JsonException exception) { throw Invalid(file, "invalid JSON", exception); }
        }

        private static bool TryGetProperty(JToken parent, string property, out JToken value) { value = parent.Type == JTokenType.Object ? parent[property]! : null!; return value != null; }
        private static bool TryReadInt64(JToken value, out long number)
        {
            number = 0;
            if (value.Type != JTokenType.Integer) return false;
            try { number = value.Value<long>(); return true; }
            catch (Exception exception) when (exception is FormatException || exception is OverflowException || exception is InvalidCastException) { return false; }
        }
        private static string Required(JToken root, string property, string file) { if (!TryGetProperty(root, property, out var value) || value.Type != JTokenType.String || string.IsNullOrWhiteSpace(value.Value<string>())) throw Invalid(file, property + " is required"); return value.Value<string>()!; }
        private static InvalidDataException Invalid(string file, string message, Exception? inner = null) { return new InvalidDataException(file + ": " + message, inner); }
    }
}
