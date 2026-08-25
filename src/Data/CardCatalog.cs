using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;

namespace DominionWars.Data
{
    public sealed class CardCatalog
    {
        private static readonly HashSet<string> Factions = new HashSet<string>(new[] { "烈焰帝国", "机械遗迹", "深海联盟", "古木圣地", "无阵营" }, StringComparer.Ordinal);
        private static readonly HashSet<string> CardTypes = new HashSet<string>(new[] { "MINION", "SPELL", "AMBUSH", "PUNISH" }, StringComparer.Ordinal);
        private static readonly HashSet<string> Keywords = new HashSet<string>(new[] { "嘲讽", "圣盾", "扰魔", "突袭", "降临", "同归", "献祭", "复活", "秒杀", "震慑", "沉默", "占星", "寄生", "潜行", "吸血" }, StringComparer.Ordinal);
        private static readonly HashSet<string> WinConditions = new HashSet<string>(new[] { "NONE", "ROYAL_CASTLE_BREAK", "AMBUSH_TRIGGER_WIN", "OPP_DISCARD_TOTAL_GE", "OPP_PUNISH_DRAW_TURN_GE", "NO_DAMAGE_TURNS_GE", "GIANT_HEALTH_GE", "PULL_TOTAL_GE" }, StringComparer.Ordinal);
        private static readonly HashSet<string> EffectTargets = new HashSet<string>(new[] { "ENEMY_TARGET", "ENEMY_MINION", "FRIENDLY_MINION", "ALL_ENEMY_MINIONS", "ALL_FRIENDLY_MINIONS", "ALL_MINIONS", "ENEMY_FACE", "SELF", "ANY_MINION", "ENEMY_SINGLE", "SINGLE_ENEMY" }, StringComparer.Ordinal);
        private static readonly HashSet<string> EffectActions = new HashSet<string>(new[] { "DAMAGE", "HEAL", "DRAW", "OPP_DRAW", "DISCARD_OPP_RANDOM", "DISCARD_DRAWN", "DESTROY", "ENFEEBLE", "BANISH", "CONTROL", "BUFF", "GRANT_KEYWORD", "SUMMON", "SUMMON_LEADER", "END_TURN", "ADD_OPP_PUNISH_TURN", "ADD_SELF_PUNISH_TURN", "CONVERT_PUNISH_TO_DISCARD", "PROTECT_TURN", "NEGATE", "NEGATE_ENEMY_EFFECTS_TURN", "SKIP_RESHUFFLE", "RESTORE_ATTACKS", "GAIN_LIFE", "LOSE_LIFE", "DAMAGE_CASTLE", "WIN_GAME", "ADD_ROOT", "ADD_RAMPANT", "COMMIT", "PUSH", "PULL", "ROLLBACK" }, StringComparer.Ordinal);
        private static readonly HashSet<string> PunishConditions = new HashSet<string>(new[] { "ALWAYS", "ENEMY_MINIONS_GE_1", "ENEMY_MINIONS_GE_2", "HAND_GE_3" }, StringComparer.Ordinal);
        private static readonly HashSet<string> AmbushKinds = new HashSet<string>(new[] { "NORMAL", "FOCUS", "LOCKDOWN" }, StringComparer.Ordinal);
        private static readonly HashSet<string> AmbushTriggers = new HashSet<string>(new[] { "OPPONENT_ATTACKS", "OPPONENT_PLAYS_SPELL", "OPPONENT_SUMMONS", "OPPONENT_PLAYS_CARD", "OPPONENT_DRAWS" }, StringComparer.Ordinal);
        private static readonly HashSet<string> KnownCardFields = new HashSet<string>(new[] { "id", "name", "faction", "type", "tags", "punish", "attack", "health", "keywords", "leader", "leaderDef", "punishActivatable", "punishCost", "punishCondition", "punishEffects", "ambushKind", "ambushTrigger", "ambushEffects", "chant", "chantEffects", "attacksPerTurn", "onOpponentDiscardEffects", "onPlayEffects", "commitCost", "uploadCost", "downloadCost", "commitEffects", "pushEffects", "pullEffects", "text", "flavor", "guard", "kingSlayer", "summonedThisTurn", "cost", "rarity" }, StringComparer.Ordinal);
        private static readonly HashSet<string> KnownLeaderFields = new HashSet<string>(new[] { "winCondition", "vulnerabilities", "winText", "winAmount", "winParam", "durability", "grantLife", "enterEffects", "punishEffects", "isLandmark", "landmarkTiers" }, StringComparer.Ordinal);
        private static readonly HashSet<string> KnownLandmarkTierFields = new HashSet<string>(new[] { "tier", "effect", "effectSpecs", "chant", "summon" }, StringComparer.Ordinal);

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
                var document = ParseFile(file);
                if (document.Type != JTokenType.Array) throw Invalid(file, "root must be an array");
                foreach (var element in document.Children())
                {
                    var card = MapCard(element, file, warning);
                    if (!cards.TryAdd(card.Id, card)) throw Invalid(file, "duplicate card id: " + card.Id);
                }
            }
            return new CardCatalog(cards);
        }

        public static CardDefinition LoadFile(string file, Action<string>? warning = null)
        {
            if (file is null) throw new ArgumentNullException(nameof(file));
            return MapCard(ParseFile(file), file, warning);
        }

        private static JToken ParseFile(string file)
        {
            if (!File.Exists(file)) throw new FileNotFoundException("Card JSON file is missing.", file);
            try { return JToken.Parse(File.ReadAllText(file)); }
            catch (JsonException exception) { throw Invalid(file, "invalid JSON", exception); }
        }

        private static CardDefinition MapCard(JToken element, string source, Action<string>? warning)
        {
            if (element.Type != JTokenType.Object) throw Invalid(source, "card must be an object");
            WarnUnknown(element, KnownCardFields, source, warning);
            var id = RequiredString(element, "id", source, 3, 64);
            var name = RequiredString(element, "name", source, 1, 32);
            var faction = RequiredString(element, "faction", source, 1, 32);
            if (!Factions.Contains(faction)) throw Invalid(source, "invalid faction: " + faction);
            var type = RequiredString(element, "type", source, 1, 16);
            if (!CardTypes.Contains(type)) throw Invalid(source, "invalid card type: " + type);
            var text = RequiredString(element, "text", source, 0, 256);
            var isMinion = type == "MINION";
            if (isMinion && (!TryGetProperty(element, "attack", out _) || !TryGetProperty(element, "health", out _))) throw Invalid(source, "MINION cards require attack and health");
            if (type == "PUNISH" && (!TryGetProperty(element, "punish", out var punishValue) || !TryReadInt64(punishValue, out var punishAmount) || punishAmount < 1)) throw Invalid(source, "PUNISH cards require punish >= 1");
            if (TryGetProperty(element, "leader", out var leaderValue) &&
                (leaderValue.Type != JTokenType.Boolean || !leaderValue.Value<bool>())) throw Invalid(source, "leader must be true when present");
            var isLeader = OptionalBool(element, "leader", false, source);
            var attack = OptionalInt(element, "attack", 0, 0, 99, source);
            var health = OptionalInt(element, "health", isMinion ? 1 : 1, 1, 99, source);
            var cost = OptionalInt(element, "cost", 0, 0, 99, source);
            var punish = OptionalInt(element, "punish", 0, 0, 20, source);
            var hasExplicitPunishActivatable = TryGetProperty(element, "punishActivatable", out _);
            var hasExplicitPunishCost = TryGetProperty(element, "punishCost", out _);
            var punishActivatable = OptionalBool(element, "punishActivatable", false, source);
            var punishCost = OptionalInt(element, "punishCost", 0, 0, 20, source);
            if (punish > 0 && !hasExplicitPunishActivatable && !hasExplicitPunishCost)
            {
                punishActivatable = true;
                punishCost = punish;
            }
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
            var commitEffects = MapEffects(element, "commitEffects", source);
            var pushEffects = MapEffects(element, "pushEffects", source);
            var pullEffects = MapEffects(element, "pullEffects", source);
            var commitCost = OptionalInt(element, "commitCost", 0, 0, 99, source);
            var uploadCost = OptionalInt(element, "uploadCost", 0, 0, 99, source);
            var downloadCost = OptionalInt(element, "downloadCost", 0, 0, 99, source);
            var leaderEnterEffects = new List<EffectSpec>();
            var leaderPunishEffects = new List<EffectSpec>();
            var vulnerabilities = new List<string>();
            var grantLife = 0;
            string? leaderWinCondition = null;
            string? leaderWinText = null;
            var leaderDurability = 0;
            var leaderWinParam = 0;
            var isLandmark = false;
            var landmarkTiers = new List<LandmarkTierDefinition>();
            if (TryGetProperty(element, "leaderDef", out var leaderDef))
            {
                if (leaderDef.Type != JTokenType.Object) throw Invalid(source, "leaderDef must be an object");
                WarnUnknown(leaderDef, KnownLeaderFields, source + ": leaderDef", warning);
                if (!TryGetProperty(leaderDef, "winCondition", out var winCondition)) throw Invalid(source, "leaderDef.winCondition is required");
                var win = ReadString(winCondition, source, "leaderDef.winCondition");
                if (!WinConditions.Contains(win)) throw Invalid(source, "invalid win condition: " + win);
                leaderWinCondition = win;
                leaderWinText = OptionalString(leaderDef, "winText", source, 64);
                if (TryGetProperty(leaderDef, "vulnerabilities", out var vulnerabilityArray))
                {
                    if (vulnerabilityArray.Type != JTokenType.Array) throw Invalid(source, "leaderDef.vulnerabilities must be an array");
                    foreach (var item in vulnerabilityArray.Children())
                    {
                        var value = ReadString(item, source, "leaderDef.vulnerabilities");
                        if (!EffectActions.Contains(value)) throw Invalid(source, "invalid vulnerability action: " + value);
                        vulnerabilities.Add(value);
                    }
                }
                ValidateLeaderDef(leaderDef, source);
                grantLife = OptionalInt(leaderDef, "grantLife", 0, 1, 99, source);
                leaderDurability = OptionalInt(leaderDef, "durability", 0, 1, 999, source);
                leaderWinParam = OptionalInt(
                    leaderDef,
                    TryGetProperty(leaderDef, "winParam", out _) ? "winParam" : "winAmount",
                    0,
                    0,
                    999,
                    source);
                isLandmark = OptionalBool(leaderDef, "isLandmark", false, source);
                landmarkTiers = MapLandmarkTiers(leaderDef, source, warning);
                leaderEnterEffects = MapEffects(leaderDef, "enterEffects", source);
                leaderPunishEffects = MapEffects(leaderDef, "punishEffects", source);
            }
            else if (isLeader) throw Invalid(source, "leader cards require leaderDef");
            return new CardDefinition(
                id, name, attack, health, isMinion, isLeader, grantLife,
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
                leaderPunishEffects: leaderPunishEffects,
                leaderWinCondition: leaderWinCondition,
                leaderWinText: leaderWinText,
                leaderDurability: leaderDurability,
                leaderWinParam: leaderWinParam,
                commitCost: commitCost,
                uploadCost: uploadCost,
                downloadCost: downloadCost,
                commitEffects: commitEffects,
                pushEffects: pushEffects,
                pullEffects: pullEffects,
                isLandmark: isLandmark,
                landmarkTiers: landmarkTiers);
        }

        private static void ValidateLeaderDef(JToken value, string source)
        {
            foreach (var property in value.Children<JProperty>())
            {
                if (property.Name == "vulnerabilities" || property.Name == "winCondition" || property.Name == "winText") continue;
                if (property.Name == "winAmount" || property.Name == "winParam" || property.Name == "durability" || property.Name == "grantLife") OptionalInt(value, property.Name, 0, 0, 999, source);
                else if (property.Name == "enterEffects" || property.Name == "punishEffects") ValidateEffects(value, property.Name, source);
            }
            if (TryGetProperty(value, "winText", out var winText) && (winText.Type != JTokenType.String || winText.Value<string>()!.Length > 64)) throw Invalid(source, "leaderDef.winText is invalid");
        }

        private static void ValidateEffects(JToken parent, string property, string source, HashSet<string>? actionSet = null)
        {
            if (!TryGetProperty(parent, property, out var array)) return;
            if (array.Type != JTokenType.Array) throw Invalid(source, property + " must be an array");
            foreach (var effect in array.Children())
            {
                if (effect.Type != JTokenType.Object || !TryGetProperty(effect, "action", out var action)) throw Invalid(source, property + " entries require action");
                var actionValue = ReadString(action, source, property + ".action");
                if (!(actionSet ?? EffectActions).Contains(actionValue)) throw Invalid(source, "invalid effect action: " + actionValue);
                if (TryGetProperty(effect, "target", out var target) && !EffectTargets.Contains(ReadString(target, source, property + ".target"))) throw Invalid(source, "invalid effect target");
                if (TryGetProperty(effect, "amount", out var amount)) ValidateInt(amount, source, property + ".amount", -99, 99);
                if (TryGetProperty(effect, "param", out var param) && (param.Type != JTokenType.String || param.Value<string>()!.Length > 64)) throw Invalid(source, "invalid effect param");
            }
        }

        private static List<EffectSpec> MapEffects(JToken parent, string property, string source)
        {
            ValidateEffects(parent, property, source);
            var result = new List<EffectSpec>();
            if (!TryGetProperty(parent, property, out var array)) return result;
            foreach (var effect in array.Children())
            {
                result.Add(new EffectSpec(
                    RequiredString(effect, "action", source, 1, 64),
                    OptionalString(effect, "target", source, 64),
                    OptionalInt(effect, "amount", 0, -99, 99, source),
                    OptionalString(effect, "param", source, 64),
                    TryGetProperty(effect, "kingSlayer", out _) ? OptionalBool(effect, "kingSlayer", false, source) : (bool?)null,
                    OptionalString(effect, "condition", source, 64)));
            }
            return result;
        }

        private static List<LandmarkTierDefinition> MapLandmarkTiers(
            JToken leaderDef,
            string source,
            Action<string>? warning)
        {
            var result = new List<LandmarkTierDefinition>();
            if (!TryGetProperty(leaderDef, "landmarkTiers", out var value))
            {
                return result;
            }

            if (value.Type != JTokenType.Array)
            {
                throw Invalid(source, "leaderDef.landmarkTiers must be an array");
            }

            var seen = new HashSet<int>();
            var index = 0;
            foreach (var item in value.Children())
            {
                var tierSource = source + ": leaderDef.landmarkTiers[" + index + "]";
                if (item.Type != JTokenType.Object)
                {
                    throw Invalid(tierSource, "tier must be an object");
                }

                WarnUnknown(item, KnownLandmarkTierFields, tierSource, warning);
                if (!TryGetProperty(item, "tier", out _))
                {
                    throw Invalid(tierSource, "tier is required");
                }

                var tier = OptionalInt(item, "tier", 0, 1, 99, tierSource);
                if (!seen.Add(tier))
                {
                    throw Invalid(tierSource, "duplicate tier: " + tier);
                }

                result.Add(new LandmarkTierDefinition(
                    tier,
                    OptionalString(item, "effect", tierSource, 256),
                    OptionalInt(item, "chant", 0, 0, 99, tierSource),
                    OptionalString(item, "summon", tierSource, 64),
                    MapEffects(item, "effectSpecs", tierSource)));
                index++;
            }

            return result;
        }

        private static List<string> ValidateArrayStrings(JToken parent, string property, string source, int maxItems, int minLength, int maxLength, HashSet<string>? allowed)
        {
            var values = new List<string>();
            if (!TryGetProperty(parent, property, out var array)) return values;
            if (array.Type != JTokenType.Array || array.Count() > maxItems) throw Invalid(source, property + " is invalid");
            foreach (var item in array.Children()) { var value = ReadString(item, source, property); if (value.Length < minLength || value.Length > maxLength || (allowed != null && !allowed.Contains(value))) throw Invalid(source, "invalid " + property + " value"); values.Add(value); }
            return values;
        }

        private static void WarnUnknown(JToken element, HashSet<string> known, string source, Action<string>? warning) { if (warning == null) return; foreach (var property in element.Children<JProperty>()) if (!known.Contains(property.Name)) warning(source + ": unknown field " + property.Name); }
        private static bool TryGetProperty(JToken parent, string property, out JToken value) { value = parent.Type == JTokenType.Object ? parent[property]! : null!; return value != null; }
        private static void ValidateEnum(JToken parent, string property, HashSet<string> allowed, string source) { if (TryGetProperty(parent, property, out var value) && !allowed.Contains(ReadString(value, source, property))) throw Invalid(source, "invalid " + property); }
        private static string RequiredString(JToken parent, string property, string source, int min, int max) { if (!TryGetProperty(parent, property, out var value)) throw Invalid(source, property + " is required"); var result = ReadString(value, source, property); if (result.Length < min || result.Length > max) throw Invalid(source, property + " length is invalid"); return result; }
        private static string? OptionalString(JToken parent, string property, string source, int max) { if (!TryGetProperty(parent, property, out var value)) return null; var result = ReadString(value, source, property); if (result.Length > max) throw Invalid(source, property + " is too long"); return result; }
        private static bool OptionalBool(JToken parent, string property, bool fallback, string source) { if (!TryGetProperty(parent, property, out var value)) return fallback; if (value.Type != JTokenType.Boolean) throw Invalid(source, property + " must be boolean"); return value.Value<bool>(); }
        private static int OptionalInt(JToken parent, string property, int fallback, int min, int max, string source) { if (!TryGetProperty(parent, property, out var value)) return fallback; ValidateInt(value, source, property, min, max); return value.Value<int>(); }
        private static void ValidateInt(JToken value, string source, string property, int min, int max) { if (!TryReadInt64(value, out var number) || number < min || number > max) throw Invalid(source, property + " is outside its allowed range"); }
        private static bool TryReadInt64(JToken value, out long number)
        {
            number = 0;
            if (value.Type != JTokenType.Integer) return false;
            try { number = value.Value<long>(); return true; }
            catch (Exception exception) when (exception is FormatException || exception is OverflowException || exception is InvalidCastException) { return false; }
        }
        private static string ReadString(JToken value, string source, string property) { if (value.Type != JTokenType.String || value.Value<string>() == null) throw Invalid(source, property + " must be a string"); return value.Value<string>()!; }
        private static InvalidDataException Invalid(string source, string message, Exception? inner = null) { return new InvalidDataException(source + ": " + message, inner); }
    }
}
