using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

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
        private static readonly HashSet<string> Factions = new HashSet<string>(new[] { "烈焰帝国", "机械遗迹", "深海联盟", "古木圣地", "无阵营" }, StringComparer.Ordinal);
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
                using (var document = JsonDocument.Parse(File.ReadAllText(file)))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) throw Invalid(file, "root must be an object");
                    var known = new HashSet<string>(new[] { "name", "faction", "leader", "cards" }, StringComparer.Ordinal);
                    if (warning != null) foreach (var property in root.EnumerateObject()) if (!known.Contains(property.Name)) warning(file + ": unknown field " + property.Name);
                    var name = Required(root, "name", file);
                    var faction = Required(root, "faction", file);
                    var leader = Required(root, "leader", file);
                    if (!Factions.Contains(faction)) throw Invalid(file, "invalid faction");
                    if (!root.TryGetProperty("cards", out var cardObject) || cardObject.ValueKind != JsonValueKind.Object || cardObject.EnumerateObject().Any(property => !property.Value.TryGetInt32(out var count) || count < 1)) throw Invalid(file, "cards must map ids to positive counts");
                    var cards = cardObject.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetInt32(), StringComparer.Ordinal);
                    return new DeckDefinition(name, faction, leader, cards);
                }
            }
            catch (JsonException exception) { throw Invalid(file, "invalid JSON", exception); }
        }

        private static string Required(JsonElement root, string property, string file) { if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw Invalid(file, property + " is required"); return value.GetString()!; }
        private static InvalidDataException Invalid(string file, string message, Exception? inner = null) { return new InvalidDataException(file + ": " + message, inner); }
    }
}
