#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DominionWars.Engine.Localization;

namespace DominionWars.Unity.Runtime
{

/// <summary>
/// The presentation semantic families understood by the runtime boundary.
/// Protocol values are mapped here; they are never rendered as user-facing
/// text directly.
/// </summary>
public enum RuntimeSemanticKind
{
    Phase,
    Action,
    Target,
    Event,
    Zone,
    Status,
}

/// <summary>
/// A localized semantic result. The result deliberately does not retain the
/// input protocol token, so an unknown token or entity identifier cannot leak
/// into a fallback label.
/// </summary>
public sealed class RuntimeLocalizedText
{
    internal RuntimeLocalizedText(
        RuntimeSemanticKind kind,
        string localizationKey,
        string text,
        bool knownSemantic,
        bool usedFallback)
    {
        Kind = kind;
        LocalizationKey = localizationKey;
        Text = text;
        IsKnownSemantic = knownSemantic;
        UsedFallback = usedFallback;
    }

    public RuntimeSemanticKind Kind { get; }
    public string LocalizationKey { get; }
    public string Text { get; }
    public bool IsKnownSemantic { get; }
    public bool UsedFallback { get; }
}

/// <summary>
/// Injectable presentation-only localization source. Implementations receive
/// canonical language ids (en, zh, or jp) from RuntimeLocalizationResolver.
/// </summary>
public interface IRuntimeLocalizationTable
{
    bool TryGet(string localizationKey, string language, out string value);
}

/// <summary>
/// Small immutable table for Runtime localization overrides and test fixtures.
/// It contains only presentation strings; card/deck authored content remains
/// in the content data path and is not exposed through this type.
/// </summary>
public sealed class RuntimeLocalizationTable : IRuntimeLocalizationTable
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _entries;

    public RuntimeLocalizationTable(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> entries)
    {
        if (entries is null) throw new ArgumentNullException(nameof(entries));

        var copy = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
                throw new ArgumentException("Localization keys cannot be empty.", nameof(entries));
            if (entry.Value is null)
                throw new ArgumentException("Localization language maps cannot be null.", nameof(entries));

            var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var translation in entry.Value)
            {
                var language = RuntimeLocalizationResolver.NormalizeLanguage(translation.Key);
                if (string.IsNullOrWhiteSpace(translation.Value))
                    throw new ArgumentException("Localization values cannot be empty.", nameof(entries));
                translations[language] = translation.Value;
            }

            copy[entry.Key] = new ReadOnlyDictionary<string, string>(translations);
        }

        _entries = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(copy);
    }

    /// <summary>
    /// The approved UI terms currently present in the runtime kit and the
    /// existing UI terms document. Keys without an approved term remain
    /// injectable and resolve to the safe generic fallback.
    /// </summary>
    public static RuntimeLocalizationTable Default { get; } = CreateDefault();

    public bool TryGet(string localizationKey, string language, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(localizationKey)) return false;
        if (!_entries.TryGetValue(localizationKey, out var translations)) return false;

        var canonicalLanguage = RuntimeLocalizationResolver.NormalizeLanguage(language);
        if (translations.TryGetValue(canonicalLanguage, out value)) return true;
        return translations.TryGetValue("en", out value);
    }

    private static RuntimeLocalizationTable CreateDefault()
    {
        var entries = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        // These are the fixed v1.30 UI keys and directly documented UI terms.
        // Do not add card names/rules, deck names, or speculative gameplay
        // wording.
        Add(entries, "app.title", "统御战纪", "統御戦記", "Dominion Wars");
        Add(entries, "menu.duel", "对战", "対戦", "Duel");
        Add(entries, "menu.deckBuilder", "卡牌编辑器", "カードエディター", "Card Editor");
        Add(entries, "menu.rules", "规则书", "ルールブック", "Rulebook");
        Add(entries, "menu.settings", "设置", "設定", "Settings");

        Add(entries, "phase.start", "开始阶段", "開始フェイズ", "Start Phase");
        Add(entries, "phase.ambush", "伏击阶段", "伏撃フェイズ", "Ambush Phase");
        Add(entries, "phase.action", "行动阶段", "行動フェイズ", "Action Phase");
        Add(entries, "phase.discard", "弃牌阶段", "捨て札フェイズ", "Discard Phase");
        Add(entries, "phase.end", "结束阶段", "終了フェイズ", "End Phase");
        Add(entries, "phase.gameOver", "对局结束", "対戦終了", "Game Over");

        Add(entries, "action.skipAmbush", "跳过伏击阶段", "伏撃フェイズをスキップ", "Skip Ambush Phase");
        Add(entries, "action.endTurn", "结束回合", "ターン終了", "End Turn");

        Add(entries, "status.usable", "可用", "使用可", "Usable");
        Add(entries, "status.unusable", "不可用", "使用不可", "Unusable");
        Add(entries, "status.targetable", "可选目标", "対象可", "Targetable");
        Add(entries, "status.kingSlayer", "弑君", "王殺し", "King Slayer");
        Add(entries, "status.castle", "王城", "王城", "Royal Castle");
        Add(entries, "status.victoryCount", "胜利计数", "勝利カウント", "Victory Count");

        // Zone labels are documented UI terms and are useful to semantic
        // consumers. They do not grant the resolver ownership of card data.
        Add(entries, "zone.field", "战场", "戦場", "Field");
        Add(entries, "zone.hand", "手牌", "手札", "Hand");
        Add(entries, "zone.deck", "卡组", "デッキ", "Deck");
        Add(entries, "zone.graveyard", "墓地", "墓地", "Graveyard");
        Add(entries, "zone.ambushZone", "伏击区", "伏撃ゾーン", "Ambush Zone");
        Add(entries, "zone.leader", "统领", "統領", "Leader");
        Add(entries, "zone.log", "对局日志", "対戦ログ", "Duel Log");

        return new RuntimeLocalizationTable(entries);
    }

    private static void Add(
        IDictionary<string, IReadOnlyDictionary<string, string>> entries,
        string key,
        string zh,
        string jp,
        string en)
    {
        entries.Add(
            key,
            new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["zh"] = zh,
                    ["jp"] = jp,
                    ["en"] = en,
                }));
    }
}

/// <summary>
/// Runtime-owned localization adapter. Engine resources remain the first
/// compatibility source for existing keys; the small Runtime table supplies
/// the contract terms the older Engine resource bundle does not yet contain.
/// </summary>
public sealed class RuntimeLocalizationResolver
{
    public const string FallbackLocalizationKey = "status.unusable";

    private static readonly IReadOnlyDictionary<string, string> EngineKeyAliases =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["action.playCard"] = "action.play_card",
                ["action.attack"] = "action.attack",
                ["action.endTurn"] = "action.end_turn",
                ["result.victory"] = "ui.you_win",
                ["result.defeat"] = "ui.you_lose",
            });

    private static readonly IReadOnlyDictionary<RuntimeSemanticKind, IReadOnlyDictionary<string, string>> SemanticKeys =
        CreateSemanticKeys();

    private readonly Localization _engineLocalization;
    private readonly IRuntimeLocalizationTable _table;
    private readonly IRuntimeLocalizationTable _fallbackTable;

    public RuntimeLocalizationResolver()
        : this(new Localization(), RuntimeLocalizationTable.Default)
    {
    }

    public RuntimeLocalizationResolver(Localization engineLocalization)
        : this(engineLocalization, RuntimeLocalizationTable.Default)
    {
    }

    public RuntimeLocalizationResolver(
        Localization engineLocalization,
        IRuntimeLocalizationTable table)
    {
        _engineLocalization = engineLocalization ?? throw new ArgumentNullException(nameof(engineLocalization));
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _fallbackTable = RuntimeLocalizationTable.Default;
    }

    /// <summary>Normalizes supported UI language aliases to en, zh, or jp.</summary>
    public static string NormalizeLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language)) return "en";

        var normalized = language.Trim().Replace('_', '-').ToLowerInvariant();
        if (normalized == "zh" || normalized.StartsWith("zh-", StringComparison.Ordinal)) return "zh";
        if (normalized == "ja" || normalized == "jp" || normalized.StartsWith("ja-", StringComparison.Ordinal) ||
            normalized.StartsWith("jp-", StringComparison.Ordinal)) return "jp";
        if (normalized == "en" || normalized.StartsWith("en-", StringComparison.Ordinal)) return "en";
        return "en";
    }

    /// <summary>
    /// Resolves a localization key without ever returning the key as a user
    /// facing fallback. Callers that need to distinguish a missing key should
    /// use TryGet.
    /// </summary>
    public bool TryGet(string localizationKey, string language, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(localizationKey)) return false;

        var canonicalLanguage = NormalizeLanguage(language);
        if (TryGetFromTable(_table, localizationKey, canonicalLanguage, out value)) return true;
        if (TryGetFromEngine(localizationKey, canonicalLanguage, out value)) return true;
        if (EngineKeyAliases.TryGetValue(localizationKey, out var engineKey) &&
            TryGetFromEngine(engineKey, canonicalLanguage, out value)) return true;
        return false;
    }

    public string Get(string localizationKey, string language = "en")
    {
        if (TryGet(localizationKey, language, out var value)) return value;
        return GetFallbackText(language);
    }

    /// <summary>
    /// Maps a known protocol token to a stable localization key and text.
    /// Unknown values, including stable/entity IDs, return only the generic
    /// status.unusable semantic and never echo the supplied token.
    /// </summary>
    public RuntimeLocalizedText ResolveSemantic(
        RuntimeSemanticKind kind,
        string protocolToken,
        string language = "en")
    {
        var canonicalLanguage = NormalizeLanguage(language);
        var known = TryResolveSemanticKey(kind, protocolToken, out var key);
        var textAvailable = TryGet(key, canonicalLanguage, out var text);
        var usedFallback = false;

        if (!textAvailable)
        {
            usedFallback = true;
            key = known ? key : FallbackLocalizationKey;
            text = GetFallbackText(canonicalLanguage);
        }

        return new RuntimeLocalizedText(kind, key, text, known, usedFallback || !known);
    }

    /// <summary>
    /// Returns false for an unknown protocol token but still provides a safe
    /// fallback key in <paramref name="localizationKey"/>.
    /// </summary>
    public bool TryResolveSemanticKey(
        RuntimeSemanticKind kind,
        string protocolToken,
        out string localizationKey)
    {
        localizationKey = FallbackLocalizationKey;
        if (!SemanticKeys.TryGetValue(kind, out var mappings)) return false;

        var normalizedToken = NormalizeProtocolToken(protocolToken);
        if (normalizedToken.Length == 0 || !mappings.TryGetValue(normalizedToken, out var key)) return false;

        localizationKey = key;
        return true;
    }

    public bool TryResolveSemantic(
        RuntimeSemanticKind kind,
        string protocolToken,
        string language,
        out RuntimeLocalizedText localized)
    {
        localized = ResolveSemantic(kind, protocolToken, language);
        return localized.IsKnownSemantic;
    }

    private string GetFallbackText(string language)
    {
        var canonicalLanguage = NormalizeLanguage(language);
        if (TryGetFromTable(_fallbackTable, FallbackLocalizationKey, canonicalLanguage, out var value)) return value;
        return canonicalLanguage == "zh" ? "不可用" : canonicalLanguage == "jp" ? "使用不可" : "Unusable";
    }

    private bool TryGetFromEngine(string localizationKey, string language, out string value)
    {
        value = string.Empty;
        try
        {
            value = _engineLocalization.GetOrThrow(localizationKey, language);
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryGetFromTable(
        IRuntimeLocalizationTable table,
        string localizationKey,
        string language,
        out string value)
    {
        value = string.Empty;
        try
        {
            return table.TryGet(localizationKey, language, out value) && !string.IsNullOrWhiteSpace(value);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private static string NormalizeProtocolToken(string protocolToken)
    {
        if (string.IsNullOrWhiteSpace(protocolToken)) return string.Empty;
        return protocolToken.Trim().ToUpperInvariant().Replace('-', '_');
    }

    private static IReadOnlyDictionary<RuntimeSemanticKind, IReadOnlyDictionary<string, string>> CreateSemanticKeys()
    {
        var result = new Dictionary<RuntimeSemanticKind, IReadOnlyDictionary<string, string>>();
        result[RuntimeSemanticKind.Phase] = Map(
            Pair("START", "phase.start"),
            Pair("AMBUSH", "phase.ambush"),
            Pair("ACTION", "phase.action"),
            Pair("DISCARD", "phase.discard"),
            Pair("END", "phase.end"),
            Pair("phase.start", "phase.start"),
            Pair("phase.ambush", "phase.ambush"),
            Pair("phase.action", "phase.action"),
            Pair("phase.discard", "phase.discard"),
            Pair("phase.end", "phase.end"),
            Pair("OVER", "phase.gameOver"),
            Pair("phase.gameOver", "phase.gameOver"));

        result[RuntimeSemanticKind.Action] = Map(
            Pair("PLAY_CARD", "action.playCard"),
            Pair("SET_AMBUSH", "action.setAmbush"),
            Pair("SKIP_AMBUSH", "action.skipAmbush"),
            Pair("ATTACK", "action.attack"),
            Pair("CHOOSE_TARGET", "action.chooseTarget"),
            Pair("DISCARD", "action.confirmDiscard"),
            Pair("END_TURN", "action.endTurn"),
            Pair("action.playCard", "action.playCard"),
            Pair("action.setAmbush", "action.setAmbush"),
            Pair("action.skipAmbush", "action.skipAmbush"),
            Pair("action.attack", "action.attack"),
            Pair("action.chooseTarget", "action.chooseTarget"),
            Pair("action.confirmDiscard", "action.confirmDiscard"),
            Pair("action.endTurn", "action.endTurn"));

        result[RuntimeSemanticKind.Target] = Map(
            Pair("ENEMY_FACE", "target.enemyFace"),
            Pair("ENEMY_TARGET", "target.enemyTarget"),
            Pair("ENEMY_MINION", "target.enemyMinion"),
            Pair("ALL_ENEMY_MINIONS", "target.allEnemyMinions"),
            Pair("ALL_FRIENDLY_MINIONS", "target.allFriendlyMinions"),
            Pair("FRIENDLY_MINION", "target.friendlyMinion"),
            Pair("ALL_MINIONS", "target.allMinions"),
            Pair("SELF", "target.self"),
            Pair("CASTLE", "status.castle"),
            Pair("ROYAL_CASTLE", "status.castle"),
            Pair("CORE_SHARED_CASTLE", "status.castle"),
            Pair("core:shared_castle", "status.castle"));

        result[RuntimeSemanticKind.Event] = Map(
            Pair("PHASE_CHANGED", "event.phaseChanged"),
            Pair("TURN_CHANGED", "event.turnChanged"),
            Pair("CARD_PLAYED", "event.cardPlayed"),
            Pair("AMBUSH_SET", "event.ambushSet"),
            Pair("AMBUSH_TRIGGERED", "event.ambushTriggered"),
            Pair("ATTACK_DECLARED", "event.attackDeclared"),
            Pair("TARGET_REJECTED", "event.targetRejected"),
            Pair("DAMAGE_APPLIED", "event.damageApplied"),
            Pair("HEAL_APPLIED", "event.healApplied"),
            Pair("PUNISH_ISSUED", "event.punishIssued"),
            Pair("PUNISH_DRAW", "event.punishDraw"),
            Pair("PUNISH_TRIGGERED", "event.punishTriggered"),
            Pair("CHAIN_LINK", "event.chainLink"),
            Pair("CHAIN_RESOLVED", "event.chainResolved"),
            Pair("CASTLE_DAMAGED", "event.castleDamaged"),
            Pair("CASTLE_BROKEN", "event.castleBroken"),
            Pair("LEADER_MANIFESTED", "event.leaderManifested"),
            Pair("LEADER_DISABLED", "event.leaderDisabled"),
            Pair("VICTORY_PROGRESS", "event.victoryProgress"),
            Pair("DECK_CYCLED", "event.deckCycled"),
            Pair("CARD_DISCARDED", "event.cardDiscarded"),
            Pair("GAME_OVER", "event.gameOver"));

        result[RuntimeSemanticKind.Zone] = Map(
            Pair("FIELD", "zone.field"),
            Pair("HAND", "zone.hand"),
            Pair("DECK", "zone.deck"),
            Pair("GRAVEYARD", "zone.graveyard"),
            Pair("AMBUSH_ZONE", "zone.ambushZone"),
            Pair("LEADER_ZONE", "zone.leader"),
            Pair("LEADER", "zone.leader"),
            Pair("LOG", "zone.log"),
            Pair("CASTLE", "status.castle"),
            Pair("ROYAL_CASTLE", "status.castle"),
            Pair("CORE_SHARED_CASTLE", "status.castle"),
            Pair("core:shared_castle", "status.castle"));

        result[RuntimeSemanticKind.Status] = Map(
            Pair("USABLE", "status.usable"),
            Pair("UNUSABLE", "status.unusable"),
            Pair("TARGETABLE", "status.targetable"),
            Pair("GUARD_BLOCKED", "status.guardBlocked"),
            Pair("KING_SLAYER", "status.kingSlayer"),
            Pair("CASTLE", "status.castle"),
            Pair("VICTORY_COUNT", "status.victoryCount"),
            Pair("status.usable", "status.usable"),
            Pair("status.unusable", "status.unusable"),
            Pair("status.targetable", "status.targetable"),
            Pair("status.guardBlocked", "status.guardBlocked"),
            Pair("status.kingSlayer", "status.kingSlayer"),
            Pair("status.castle", "status.castle"),
            Pair("status.victoryCount", "status.victoryCount"));

        return new ReadOnlyDictionary<RuntimeSemanticKind, IReadOnlyDictionary<string, string>>(result);
    }

    private static IReadOnlyDictionary<string, string> Map(params KeyValuePair<string, string>[] pairs)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            result[NormalizeProtocolToken(pair.Key)] = pair.Value;
        }

        return new ReadOnlyDictionary<string, string>(result);
    }

    private static KeyValuePair<string, string> Pair(string token, string key)
    {
        return new KeyValuePair<string, string>(token, key);
    }
}
}
