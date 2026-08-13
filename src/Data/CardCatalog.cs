using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;

namespace DominionWars.Data
{
    public sealed class CardCatalog
    {
        private static readonly HashSet<string> Factions = new HashSet<string>(new[] { "烈焰帝国", "机械遗迹", "深海联盟", "古木圣地", "无阵营" }, StringComparer.Ordinal);
        private static readonly HashSet<string> CardTypes = new HashSet<string>(new[] { "MINION", "SPELL", "AMBUSH", "PUNISH" }, StringComparer.Ordinal);
        private static readonly HashSet<string> Keywords = new HashSet<string>(new[] { "嘲讽", "圣盾", "扰魔", "突袭" }, StringComparer.Ordinal);
        private static readonly HashSet<string> WinConditions = new HashSet<string>(new[] { "NONE", "ROYAL_CASTLE_BREAK", "AMBUSH_TRIGGER_WIN", "OPP_DISCARD_TOTAL_GE", "OPP_PUNISH_DRAW_TURN_GE", "NO_DAMAGE_TURNS_GE" }, StringComparer.Ordinal);
        private static readonly HashSet<string> EffectTargets = new HashSet<string>(new[] { "ENEMY_TARGET", "ENEMY_MINION", "FRIENDLY_MINION", "ALL_ENEMY_MINIONS", "ALL_FRIENDLY_MINIONS", "ALL_MINIONS", "ENEMY_FACE", "SELF", "ANY_MINION", "ENEMY_SINGLE", "SINGLE_ENEMY" }, StringComparer.Ordinal);
        private static readonly HashSet<string> EffectActions = new HashSet<string>(new[] { "DAMAGE", "HEAL", "DRAW", "OPP_DRAW", "DISCARD_OPP_RANDOM", "DISCARD_DRAWN", "DESTROY", "BUFF", "GRANT_KEYWORD", "SUMMON", "SUMMON_LEADER", "END_TURN", "ADD_OPP_PUNISH_TURN", "ADD_SELF_PUNISH_TURN", "CONVERT_PUNISH_TO_DISCARD", "PROTECT_TURN", "NEGATE", "NEGATE_ENEMY_EFFECTS_TURN", "SKIP_RESHUFFLE", "RESTORE_ATTACKS", "GAIN_LIFE", "LOSE_LIFE", "DAMAGE_CASTLE", "WIN_GAME" }, StringComparer.Ordinal);
        private static readonly HashSet<string> PersistentActions = new HashSet<string>(new[] { "DISABLE_ENEMY_LEADER" }, StringComparer.Ordinal);
        private static readonly HashSet<string> PunishConditions = new HashSet<string>(new[] { "ALWAYS", "ENEMY_MINIONS_GE_1", "ENEMY_MINIONS_GE_2", "HAND_GE_3" }, StringComparer.Ordinal);
        private static readonly HashSet<string> AmbushKinds = new HashSet<string>(new[] { "NORMAL", "FOCUS", "LOCKDOWN" }, StringComparer.Ordinal);
        private static readonly HashSet<string> AmbushTriggers = new HashSet<string>(new[] { "OPPONENT_ATTACKS", "OPPONENT_PLAYS_SPELL", "OPPONENT_SUMMONS", "OPPONENT_PLAYS_CARD", "OPPONENT_DRAWS" }, StringComparer.Ordinal);
        private static readonly HashSet<string> KnownCardFields = new HashSet<string>(new[] { "id", "name", "faction", "type", "tags", "punish", "attack", "health", "keywords", "leader", "leaderDef", "punishActivatable", "punishCost", "punishCondition", "punishEffects", "ambushKind", "ambushTrigger", "ambushEffects", "chant", "chantEffects", "attacksPerTurn", "onOpponentDiscardEffects", "onPlayEffects", "text", "flavor", "guard", "kingSlayer", "summonedThisTurn", "cost", "rarity" }, StringComparer.Ordinal);
        private static readonly HashSet<string> KnownLeaderFields = new HashSet<string>(new[] { "winCondition", "vulnerabilities", "winText", "winAmount", "winParam", "durability", "grantLife", "persistentEffects", "enterEffects", "punishEffects" }, StringComparer.Ordinal);

        public CardCatalog(IReadOnlyDictionary<string, CardDefinition> cards)
        {
            Cards = cards ?? throw new ArgumentNullException(nameof(cards));
        }

        public IReadOnlyDictionary<string, CardDefinition> Cards { get; }

        public static CardCatalog LoadDirectory(string directory, Action<string>? warning = null)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            var files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (files.Length == 0) throw new InvalidDataException("No card JSON files were found.");
            var cards = new Dictionary<string, CardDefinition>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                using (var document = ParseFile(file))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Array) throw Invalid(file, "root must be an array");
                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        var card = MapCard(element, file, warning);
                        if (!cards.TryAdd(card.Id, card)) throw Invalid(file, "duplicate card id: " + card.Id);
                    }
                }
            }
            return new CardCatalog(cards);
        }

        public static CardDefinition LoadFile(string file, Action<string>? warning = null)
        {
            if (file is null) throw new ArgumentNullException(nameof(file));
            using (var document = ParseFile(file)) return MapCard(document.RootElement, file, warning);
        }

        private static JsonDocument ParseFile(string file)
        {
            if (!File.Exists(file)) throw new FileNotFoundException("Card JSON file is missing.", file);
            try { return JsonDocument.Parse(File.ReadAllText(file)); }
            catch (JsonException exception) { throw Invalid(file, "invalid JSON", exception); }
        }

        private static CardDefinition MapCard(JsonElement element, string source, Action<string>? warning)
        {
            if (element.ValueKind != JsonValueKind.Object) throw Invalid(source, "card must be an object");
            WarnUnknown(element, KnownCardFields, source, warning);
            var id = RequiredString(element, "id", source, 3, 64);
            var name = RequiredString(element, "name", source, 1, 32);
            var faction = RequiredString(element, "faction", source, 1, 32);
            if (!Factions.Contains(faction)) throw Invalid(source, "invalid faction: " + faction);
            var type = RequiredString(element, "type", source, 1, 16);
            if (!CardTypes.Contains(type)) throw Invalid(source, "invalid card type: " + type);
            var text = RequiredString(element, "text", source, 0, 256);
            var isMinion = type == "MINION";
            if (isMinion && (!element.TryGetProperty("attack", out _) || !element.TryGetProperty("health", out _))) throw Invalid(source, "MINION cards require attack and health");
            if (type == "PUNISH" && (!element.TryGetProperty("punish", out var punishValue) || !punishValue.TryGetInt32(out var punishAmount) || punishAmount < 1)) throw Invalid(source, "PUNISH cards require punish >= 1");
            if (element.TryGetProperty("leader", out var leaderValue) && (leaderValue.ValueKind != JsonValueKind.True)) throw Invalid(source, "leader must be true when present");
            var isLeader = OptionalBool(element, "leader", false, source);
            var attack = OptionalInt(element, "attack", 0, 0, 99, source);
            var health = OptionalInt(element, "health", isMinion ? 1 : 1, 1, 99, source);
            var cost = OptionalInt(element, "cost", 0, 0, 99, source);
            var punish = OptionalInt(element, "punish", 0, 0, 20, source);
            var punishActivatable = OptionalBool(element, "punishActivatable", false, source);
            var punishCost = OptionalInt(element, "punishCost", 0, 0, 20, source);
            if (punish > 0) { punishActivatable = true; punishCost = punish; }
            var punishCondition = OptionalString(element, "punishCondition", source, 64);
            ValidateArrayStrings(element, "tags", source, 4, 1, 8, null);
            var keywords = ValidateArrayStrings(element, "keywords", source, 4, 1, 16, Keywords);
            ValidateEnum(element, "punishCondition", PunishConditions, source);
            ValidateEnum(element, "ambushKind", AmbushKinds, source);
            ValidateEnum(element, "ambushTrigger", AmbushTriggers, source);
            var punishEffects = MapEffects(element, "punishEffects", source);
            var ambushEffects = MapEffects(element, "ambushEffects", source);
            var chantEffects = MapEffects(element, "chantEffects", source);
            var onOpponentDiscardEffects = MapEffects(element, "onOpponentDiscardEffects", source);
            var onPlayEffects = MapEffects(element, "onPlayEffects", source);
            var leaderEnterEffects = new List<EffectSpec>();
            var leaderPunishEffects = new List<EffectSpec>();
            var vulnerabilities = new List<string>();
            if (element.TryGetProperty("leaderDef", out var leaderDef))
            {
                if (leaderDef.ValueKind != JsonValueKind.Object) throw Invalid(source, "leaderDef must be an object");
                WarnUnknown(leaderDef, KnownLeaderFields, source + ": leaderDef", warning);
                if (!leaderDef.TryGetProperty("winCondition", out var winCondition)) throw Invalid(source, "leaderDef.winCondition is required");
                var win = ReadString(winCondition, source, "leaderDef.winCondition");
                if (!WinConditions.Contains(win)) throw Invalid(source, "invalid win condition: " + win);
                if (leaderDef.TryGetProperty("vulnerabilities", out var vulnerabilityArray))
                {
                    if (vulnerabilityArray.ValueKind != JsonValueKind.Array) throw Invalid(source, "leaderDef.vulnerabilities must be an array");
                    foreach (var item in vulnerabilityArray.EnumerateArray())
                    {
                        var value = ReadString(item, source, "leaderDef.vulnerabilities");
                        if (!EffectActions.Contains(value)) throw Invalid(source, "invalid vulnerability action: " + value);
                        vulnerabilities.Add(value);
                    }
                }
                ValidateLeaderDef(leaderDef, source);
                leaderEnterEffects = MapEffects(leaderDef, "enterEffects", source);
                leaderPunishEffects = MapEffects(leaderDef, "punishEffects", source);
            }
            else if (isLeader) throw Invalid(source, "leader cards require leaderDef");
            return new CardDefinition(
                id, name, attack, health, isMinion, isLeader,
                kingSlayer: OptionalBool(element, "kingSlayer", false, source),
                faction: faction,
                text: text,
                flavor: OptionalString(element, "flavor", source, 256),
                cost: cost,
                keywords: keywords,
                tags: ValidateArrayStrings(element, "tags", source, 4, 1, 8, null),
                punishActivatable: punishActivatable,
                punishCost: punishCost,
                vulnerabilities: vulnerabilities,
                type: type,
                punish: punish,
                punishCondition: punishCondition,
                onPlayEffects: onPlayEffects,
                punishEffects: punishEffects,
                ambushKind: OptionalString(element, "ambushKind", source, 32),
                ambushTrigger: OptionalString(element, "ambushTrigger", source, 64),
                ambushEffects: ambushEffects,
                chant: OptionalInt(element, "chant", 0, 0, 99, source),
                chantEffects: chantEffects,
                attacksPerTurn: OptionalInt(element, "attacksPerTurn", 1, 1, 99, source),
                onOpponentDiscardEffects: onOpponentDiscardEffects,
                guard: OptionalBool(element, "guard", false, source),
                leaderEnterEffects: leaderEnterEffects,
                leaderPunishEffects: leaderPunishEffects);
        }

        private static void ValidateLeaderDef(JsonElement value, string source)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name == "vulnerabilities" || property.Name == "winCondition" || property.Name == "winText") continue;
                if (property.Name == "winAmount" || property.Name == "winParam" || property.Name == "durability" || property.Name == "grantLife") OptionalInt(value, property.Name, 0, 0, 999, source);
                else if (property.Name == "persistentEffects") ValidateEffects(value, property.Name, source, PersistentActions);
                else if (property.Name == "enterEffects" || property.Name == "punishEffects") ValidateEffects(value, property.Name, source);
            }
            if (value.TryGetProperty("winText", out var winText) && (winText.ValueKind != JsonValueKind.String || winText.GetString()!.Length > 64)) throw Invalid(source, "leaderDef.winText is invalid");
        }

        private static void ValidateEffects(JsonElement parent, string property, string source, HashSet<string>? actionSet = null)
        {
            if (!parent.TryGetProperty(property, out var array)) return;
            if (array.ValueKind != JsonValueKind.Array) throw Invalid(source, property + " must be an array");
            foreach (var effect in array.EnumerateArray())
            {
                if (effect.ValueKind != JsonValueKind.Object || !effect.TryGetProperty("action", out var action)) throw Invalid(source, property + " entries require action");
                var actionValue = ReadString(action, source, property + ".action");
                if (!(actionSet ?? EffectActions).Contains(actionValue)) throw Invalid(source, "invalid effect action: " + actionValue);
                if (effect.TryGetProperty("target", out var target) && !EffectTargets.Contains(ReadString(target, source, property + ".target"))) throw Invalid(source, "invalid effect target");
                if (effect.TryGetProperty("amount", out var amount)) ValidateInt(amount, source, property + ".amount", -99, 99);
                if (effect.TryGetProperty("param", out var param) && (param.ValueKind != JsonValueKind.String || param.GetString()!.Length > 64)) throw Invalid(source, "invalid effect param");
            }
        }

        private static List<EffectSpec> MapEffects(JsonElement parent, string property, string source)
        {
            ValidateEffects(parent, property, source);
            var result = new List<EffectSpec>();
            if (!parent.TryGetProperty(property, out var array)) return result;
            foreach (var effect in array.EnumerateArray())
            {
                result.Add(new EffectSpec(
                    RequiredString(effect, "action", source, 1, 64),
                    OptionalString(effect, "target", source, 64),
                    OptionalInt(effect, "amount", 0, -99, 99, source),
                    OptionalString(effect, "param", source, 64),
                    effect.TryGetProperty("kingSlayer", out _) ? OptionalBool(effect, "kingSlayer", false, source) : (bool?)null,
                    OptionalString(effect, "condition", source, 64)));
            }
            return result;
        }

        private static List<string> ValidateArrayStrings(JsonElement parent, string property, string source, int maxItems, int minLength, int maxLength, HashSet<string>? allowed)
        {
            var values = new List<string>();
            if (!parent.TryGetProperty(property, out var array)) return values;
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > maxItems) throw Invalid(source, property + " is invalid");
            foreach (var item in array.EnumerateArray()) { var value = ReadString(item, source, property); if (value.Length < minLength || value.Length > maxLength || (allowed != null && !allowed.Contains(value))) throw Invalid(source, "invalid " + property + " value"); values.Add(value); }
            return values;
        }

        private static void WarnUnknown(JsonElement element, HashSet<string> known, string source, Action<string>? warning) { if (warning == null) return; foreach (var property in element.EnumerateObject()) if (!known.Contains(property.Name)) warning(source + ": unknown field " + property.Name); }
        private static void ValidateEnum(JsonElement parent, string property, HashSet<string> allowed, string source) { if (parent.TryGetProperty(property, out var value) && !allowed.Contains(ReadString(value, source, property))) throw Invalid(source, "invalid " + property); }
        private static string RequiredString(JsonElement parent, string property, string source, int min, int max) { if (!parent.TryGetProperty(property, out var value)) throw Invalid(source, property + " is required"); var result = ReadString(value, source, property); if (result.Length < min || result.Length > max) throw Invalid(source, property + " length is invalid"); return result; }
        private static string? OptionalString(JsonElement parent, string property, string source, int max) { if (!parent.TryGetProperty(property, out var value)) return null; var result = ReadString(value, source, property); if (result.Length > max) throw Invalid(source, property + " is too long"); return result; }
        private static bool OptionalBool(JsonElement parent, string property, bool fallback, string source) { if (!parent.TryGetProperty(property, out var value)) return fallback; if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False) throw Invalid(source, property + " must be boolean"); return value.GetBoolean(); }
        private static int OptionalInt(JsonElement parent, string property, int fallback, int min, int max, string source) { if (!parent.TryGetProperty(property, out var value)) return fallback; ValidateInt(value, source, property, min, max); return value.GetInt32(); }
        private static void ValidateInt(JsonElement value, string source, string property, int min, int max) { if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < min || number > max) throw Invalid(source, property + " is outside its allowed range"); }
        private static string ReadString(JsonElement value, string source, string property) { if (value.ValueKind != JsonValueKind.String || value.GetString() == null) throw Invalid(source, property + " must be a string"); return value.GetString()!; }
        private static InvalidDataException Invalid(string source, string message, Exception? inner = null) { return new InvalidDataException(source + ": " + message, inner); }
    }
}
