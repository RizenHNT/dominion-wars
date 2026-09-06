#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DominionWars.Adapters;
using DominionWars.Data;
using DominionWars.Unity.Runtime;

namespace DominionWars.Unity.UI
{

/// <summary>
/// A renderer-neutral row in the adapter event timeline. The row keeps the
/// event's input index and parent relationship so a view can render causal
/// indentation without sorting, filtering, or interpreting engine rules.
/// </summary>
public sealed class RuntimeBattleEventTimelineEntry
{
    internal RuntimeBattleEventTimelineEntry(
        RuntimeEventEnvelope? @event,
        int sourceIndex,
        string category,
        bool knownType)
    {
        Event = @event;
        SourceIndex = sourceIndex;
        Category = category;
        IsKnownType = knownType;
    }

    public RuntimeEventEnvelope? Event { get; }
    public int SourceIndex { get; }
    public int ParentIndex { get; internal set; } = -1;
    public int Depth { get; internal set; }
    public string Category { get; }
    public bool IsKnownType { get; }
    public bool IsDuplicate { get; internal set; }
    public bool HasMissingParent { get; internal set; }
    public bool IsInCycle { get; internal set; }
    public bool HasCyclicParent { get; internal set; }

    public string EventId => Event?.EventId ?? string.Empty;
    public string? ParentEventId => Event?.ParentEventId;
    public string Type => Event?.Type ?? string.Empty;
}

/// <summary>
/// Pure presentation formatting for the neutral runtime battle skeleton.
/// This class only reads contract DTOs; it does not derive legality, costs,
/// targets, phase transitions, or victory state.
/// </summary>
public static class RuntimeBattlePanelPresentationModel
{
    public const string Unavailable = "Unavailable";
    public const int MaxEventTimelineEntries = 6;

    private const int MaxEventTimelineDepth = 4;
    private const int MaxStableTokenLength = 48;

    private static readonly RuntimeLocalizationResolver DefaultLocalizationResolver =
        new RuntimeLocalizationResolver();

    private static readonly HashSet<string> KnownEventTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "PHASE_CHANGED", "TURN_CHANGED", "CARD_PLAYED", "AMBUSH_SET", "AMBUSH_TRIGGERED",
        "ATTACK_DECLARED", "TARGET_REJECTED", "DAMAGE_APPLIED", "HEAL_APPLIED", "PUNISH_ISSUED",
        "PUNISH_DRAW", "PUNISH_TRIGGERED", "CHAIN_LINK", "CHAIN_RESOLVED", "CASTLE_DAMAGED",
        "CASTLE_BROKEN", "LEADER_MANIFESTED", "LEADER_DISABLED", "VICTORY_PROGRESS", "DECK_CYCLED",
        "CARD_DISCARDED", "COMMIT_DECLARED", "CARD_COMMITTED", "CARD_PUSHED",
        "PULL_DECLARED", "CARD_PULLED", "GAME_OVER",
    };

    // The board is intentionally a bounded summary. The full wire snapshot
    // remains authoritative; the view only limits how many visible card IDs
    // are printed so the placeholder panel remains readable at 1280x720.
    private const int MaxCardsPerSummary = 8;

    public static bool IsViewerSnapshot(RuntimeSnapshotEnvelope snapshot, int viewerPlayerIndex)
    {
        if (snapshot is null || viewerPlayerIndex is < 0 or > 1)
            return false;

        return string.Equals(
            snapshot.ViewerPlayerId,
            "player_" + viewerPlayerIndex.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    public static RuntimePlayerSnapshot? FindPlayer(
        RuntimeSnapshotEnvelope snapshot,
        bool viewer)
    {
        if (snapshot is null || snapshot.Players is null)
            return null;

        if (string.IsNullOrWhiteSpace(snapshot.ViewerPlayerId))
            return null;

        RuntimePlayerSnapshot? viewerPlayer = null;
        for (var index = 0; index < snapshot.Players.Count; index++)
        {
            var player = snapshot.Players[index];
            if (player is null) continue;

            if (string.Equals(player.PlayerId, snapshot.ViewerPlayerId, StringComparison.Ordinal))
            {
                viewerPlayer = player;
                break;
            }
        }

        // A malformed or incomplete viewer id must not cause the UI to guess
        // which player is local. The caller will render a safe placeholder.
        if (viewerPlayer is null)
            return null;
        if (viewer)
            return viewerPlayer;

        for (var index = 0; index < snapshot.Players.Count; index++)
        {
            var player = snapshot.Players[index];
            if (player is not null && !ReferenceEquals(player, viewerPlayer))
                return player;
        }
        return null;
    }

    /// <summary>
    /// Builds the player-facing match header. Match identity, numeric viewer
    /// identity and adapter revision are diagnostics, not game information,
    /// so the normal surface only shows the turn and phase.
    /// </summary>
    public static string BuildMatchLine(RuntimeSnapshotEnvelope snapshot)
    {
        if (snapshot is null)
            return "比赛状态：不可用";

        return "回合 " + snapshot.Turn.ToString(CultureInfo.InvariantCulture) +
            " | 阶段 " + Display(snapshot.Phase, "未提供");
    }

    /// <summary>Returns the complete header for an explicitly enabled debug surface.</summary>
    public static string BuildDebugMatchLine(RuntimeSnapshotEnvelope snapshot)
    {
        if (snapshot is null)
            return "比赛状态：不可用";

        var match = Display(snapshot.MatchId, "未提供");
        var phase = Display(snapshot.Phase, "未提供");
        return "对局 " + match +
            " | 回合 " + snapshot.Turn.ToString(CultureInfo.InvariantCulture) +
            " | 阶段 " + phase +
            " | 当前玩家 " + snapshot.CurrentPlayer.ToString(CultureInfo.InvariantCulture) +
            " | revision " + snapshot.SnapshotRevision.ToString(CultureInfo.InvariantCulture);
    }

    public static string BuildCastleSummary(RuntimeCastleSnapshot castle)
    {
        if (castle is null)
            return "共享王城：Unavailable";
        if (!castle.Enabled)
            return "共享王城：未启用";

        return "共享王城：生命 " + castle.Health.ToString(CultureInfo.InvariantCulture);
    }

    public static string BuildCastleSummary(
        RuntimeCastleSnapshot castle,
        RuntimePlayerSnapshot own,
        RuntimePlayerSnapshot opponent)
    {
        if (castle is null)
            return "共享王城：Unavailable | 洗牌胜利计数 己方 " + CycleWinCount(own) +
                " / 对手 " + CycleWinCount(opponent);
        if (!castle.Enabled)
            return "共享王城：未启用 | 洗牌胜利计数 己方 " + CycleWinCount(own) +
                " / 对手 " + CycleWinCount(opponent);

        return "共享王城：生命 " + castle.Health.ToString(CultureInfo.InvariantCulture) +
            " | 洗牌胜利计数 己方 " + CycleWinCount(own) +
            " / 对手 " + CycleWinCount(opponent);
    }

    public static string BuildLeaderSummary(RuntimePlayerSnapshot player)
    {
        var parts = new List<string>(3);
        AddAvailableLeaderPart(parts, "名称", BuildLeaderName(player));
        AddAvailableLeaderPart(parts, "生命", BuildLeaderLife(player));
        AddAvailableLeaderPart(parts, "状态", BuildLeaderStatus(player));
        return string.Join(" | ", parts);
    }

    /// <summary>Returns the complete leader summary for the debug surface.</summary>
    public static string BuildDebugLeaderSummary(RuntimePlayerSnapshot player)
    {
        return "名称 " + BuildDebugLeaderName(player) +
            " | 生命 " + BuildDebugLeaderLife(player) +
            " | 状态 " + BuildDebugLeaderStatus(player);
    }

    public static string BuildLeaderName(RuntimePlayerSnapshot player)
    {
        var leaders = player?.LeaderZone;
        if (leaders is null || leaders.Count == 0)
            return string.Empty;

        var count = 0;
        for (var index = 0; index < leaders.Count; index++)
        {
            if (leaders[index] is not null) count++;
        }

        if (count == 0) return string.Empty;
        return count == 1
            ? "统领"
            : "统领 ×" + count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Returns the stable card/entity form for the debug surface only.</summary>
    public static string BuildDebugLeaderName(RuntimePlayerSnapshot player)
    {
        var leaders = player?.LeaderZone;
        if (leaders is null || leaders.Count == 0)
            return Unavailable + "（LeaderZone 为空）";

        var builder = new StringBuilder();
        for (var index = 0; index < leaders.Count; index++)
        {
            var card = leaders[index];
            if (card is null) continue;
            if (builder.Length > 0) builder.Append(", ");
            builder.Append(Display(card.CardId, "未知统领"));
            if (card.EntityId > 0)
                builder.Append("#").Append(card.EntityId.ToString(CultureInfo.InvariantCulture));
        }
        return builder.Length == 0 ? Unavailable + "（LeaderZone 内容不可用）" : builder.ToString();
    }

    public static string BuildLeaderLife(RuntimePlayerSnapshot player)
    {
        // The canonical LeaderZone card DTO has identity/sealed only; it does
        // not carry the runtime health value. Player.Life is not leader life.
        return string.Empty;
    }

    /// <summary>Returns the missing leader-life detail for the debug surface only.</summary>
    public static string BuildDebugLeaderLife(RuntimePlayerSnapshot player)
    {
        return Unavailable;
    }

    public static string BuildLeaderStatus(RuntimePlayerSnapshot player)
    {
        var leaders = player?.LeaderZone;
        if (leaders is null || leaders.Count == 0) return string.Empty;

        var hasCard = false;
        var sealedLeader = false;
        var unsealedLeader = false;
        for (var index = 0; index < leaders.Count; index++)
        {
            var card = leaders[index];
            if (card is null) continue;
            hasCard = true;
            if (card.Sealed) sealedLeader = true;
            else unsealedLeader = true;
        }
        if (!hasCard) return string.Empty;
        if (sealedLeader && unsealedLeader) return "mixed (sealed/present)";
        return sealedLeader ? "sealed" : "present";
    }

    /// <summary>Returns the leader status detail for the debug surface only.</summary>
    public static string BuildDebugLeaderStatus(RuntimePlayerSnapshot player)
    {
        var status = BuildLeaderStatus(player);
        return string.IsNullOrWhiteSpace(status) ? Unavailable : status;
    }

    public static string BuildExileSummary(RuntimePlayerSnapshot player)
    {
        // Exile is not part of the current canonical player snapshot. Graveyard
        // cards and counts remain separate and must not be relabelled as exile.
        // Keep the method as a future semantic hook without placing an absence
        // sentinel on the normal player-facing surface.
        return string.Empty;
    }

    /// <summary>Returns the schema detail for the debug surface only.</summary>
    public static string BuildDebugExileSummary(RuntimePlayerSnapshot player)
    {
        if (player is null)
            return Unavailable + "（snapshot 未提供除外区数量或内容）";

        return Unavailable + "（snapshot 未提供除外区数量或内容）";
    }

    private static string CycleWinCount(RuntimePlayerSnapshot player)
    {
        return player is null
            ? Unavailable
            : player.CycleWinCount.ToString(CultureInfo.InvariantCulture);
    }

    public static string BuildPhaseSummary(RuntimeSnapshotEnvelope snapshot)
    {
        if (snapshot is null)
            return "阶段：不可用";

        return "阶段 " + Display(snapshot.Phase, "未提供") +
            " | 回合 " + snapshot.Turn.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Returns the complete phase line for the debug surface.</summary>
    public static string BuildDebugPhaseSummary(RuntimeSnapshotEnvelope snapshot)
    {
        if (snapshot is null)
            return "阶段：不可用";

        return "阶段 " + Display(snapshot.Phase, "未提供") +
            " | 回合 " + snapshot.Turn.ToString(CultureInfo.InvariantCulture) +
            " | 当前玩家 " + snapshot.CurrentPlayer.ToString(CultureInfo.InvariantCulture) +
            " | 合法行动由引擎提供";
    }

    public static string BuildPlayerSection(RuntimePlayerSnapshot player, bool viewer)
    {
        return BuildPlayerSection(player, viewer, null);
    }

    /// <summary>
    /// Builds a player-facing player section. The optional catalog is used to
    /// retain human-readable card names without exposing stable IDs.
    /// </summary>
    public static string BuildPlayerSection(
        RuntimePlayerSnapshot player,
        bool viewer,
        CardCatalog cardCatalog)
    {
        if (player is null)
            return viewer ? "己方状态：不可用" : "对手状态：不可用";

        var role = viewer ? "己方" : "对手";
        var builder = new StringBuilder();
        builder.Append(role)
            .Append('\n')
            .Append("生命 ")
            .Append(player.Life.HasValue
                ? player.Life.Value.ToString(CultureInfo.InvariantCulture)
                : "隐藏/不可用")
            .Append(" | 牌库 ")
            .Append(player.DeckCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | 手牌 ")
            .Append(player.HandCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | 场面 ")
            .Append(player.FieldCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | 墓地 ")
            .Append(player.GraveyardCount.ToString(CultureInfo.InvariantCulture));

        var leaderSummary = BuildLeaderSummary(player);
        if (!string.IsNullOrWhiteSpace(leaderSummary))
            builder.Append('\n').Append("统领：").Append(leaderSummary);

        var exileSummary = BuildExileSummary(player);
        if (!string.IsNullOrWhiteSpace(exileSummary))
            builder.Append('\n').Append("除外：").Append(exileSummary);

        builder.Append('\n')
            .Append("伏击 ")
            .Append(player.AmbushCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | root ")
            .Append(player.RootStacks.ToString(CultureInfo.InvariantCulture))
            .Append(" | rampant ")
            .Append(player.RampantStacks.ToString(CultureInfo.InvariantCulture))
            .Append(" | commit ")
            .Append(player.CommitQueueCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | cloud ")
            .Append(player.CloudStackCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | pull ")
            .Append(player.PullCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | 洗牌胜利计数 ")
            .Append(player.CycleWinCount.ToString(CultureInfo.InvariantCulture))
            .Append('\n')
            .Append("手牌：")
            .Append(viewer
                ? FormatVisibleCards(player.Hand, "空", cardCatalog)
                : "隐藏（仅数量可见）")
            .Append('\n')
            .Append("场面：")
            .Append(FormatVisibleCards(player.Field, "空", cardCatalog));
        return builder.ToString();
    }

    /// <summary>Returns the complete player section for the debug surface only.</summary>
    public static string BuildDebugPlayerSection(RuntimePlayerSnapshot player, bool viewer)
    {
        if (player is null)
            return viewer ? "己方状态：不可用" : "对手状态：不可用";

        var role = viewer ? "己方" : "对手";
        var builder = new StringBuilder();
        builder.Append(role)
            .Append(" ")
            .Append(Display(player.PlayerId, "未提供"))
            .Append('\n')
            .Append("生命 ")
            .Append(player.Life.HasValue
                ? player.Life.Value.ToString(CultureInfo.InvariantCulture)
                : "隐藏/不可用")
            .Append(" | 牌库 ")
            .Append(player.DeckCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | 手牌 ")
            .Append(player.HandCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | 场面 ")
            .Append(player.FieldCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | 墓地 ")
            .Append(player.GraveyardCount.ToString(CultureInfo.InvariantCulture))
            .Append('\n')
            .Append("统领：")
            .Append(BuildDebugLeaderSummary(player))
            .Append('\n')
            .Append("除外：")
            .Append(BuildDebugExileSummary(player))
            .Append('\n')
            .Append("伏击 ")
            .Append(player.AmbushCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | root ")
            .Append(player.RootStacks.ToString(CultureInfo.InvariantCulture))
            .Append(" | rampant ")
            .Append(player.RampantStacks.ToString(CultureInfo.InvariantCulture))
            .Append(" | commit ")
            .Append(player.CommitQueueCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | cloud ")
            .Append(player.CloudStackCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | pull ")
            .Append(player.PullCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | 洗牌胜利计数 ")
            .Append(player.CycleWinCount.ToString(CultureInfo.InvariantCulture))
            .Append('\n')
            .Append("手牌：")
            .Append(viewer
                ? FormatCards(player.Hand, "空")
                : "隐藏（仅数量可见）")
            .Append('\n')
            .Append("场面：")
            .Append(FormatCards(player.Field, "空"));
        return builder.ToString();
    }

    /// <summary>
    /// Builds a causal view of the adapter event stream while preserving the
    /// exact input order. Parent links only affect indentation and safety
    /// markers; they never cause events to be sorted or synthesized.
    /// </summary>
    public static IReadOnlyList<RuntimeBattleEventTimelineEntry> BuildEventTimeline(
        IReadOnlyList<RuntimeEventEnvelope> events)
    {
        if (events is null || events.Count == 0)
            return Array.Empty<RuntimeBattleEventTimelineEntry>();

        var entries = new List<RuntimeBattleEventTimelineEntry>(events.Count);
        var firstById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < events.Count; index++)
        {
            var item = events[index];
            var type = item?.Type ?? string.Empty;
            var knownType = KnownEventTypes.Contains(type);
            var entry = new RuntimeBattleEventTimelineEntry(
                item,
                index,
                knownType ? EventCategory(type) : "UNKNOWN",
                knownType);
            entries.Add(entry);

            // A duplicate is retained in-place so the adapter's order is not
            // silently changed. Parent lookup uses the first occurrence,
            // which is deterministic even for malformed input.
            var eventId = item?.EventId;
            if (string.IsNullOrWhiteSpace(eventId)) continue;
            if (firstById.ContainsKey(eventId))
            {
                entry.IsDuplicate = true;
                continue;
            }
            firstById.Add(eventId, index);
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var item = entries[index].Event;
            if (item is null || item.ParentEventId is null) continue;
            if (firstById.TryGetValue(item.ParentEventId, out var parentIndex))
            {
                entries[index].ParentIndex = parentIndex;
            }
            else
            {
                entries[index].HasMissingParent = true;
            }
        }

        var states = new int[entries.Count];
        var path = new List<int>();
        for (var index = 0; index < entries.Count; index++)
            ResolveTimelineDepth(index, entries, states, path);

        return entries.AsReadOnly();
    }

    /// <summary>
    /// Builds the player-facing event rail. It keeps the event order and
    /// bounded suffix, but only exposes established semantic cues; wire IDs,
    /// parent links, target counts, revisions and adapter ordering stay in the
    /// explicit debug formatter below.
    /// </summary>
    public static string BuildEvents(IReadOnlyList<RuntimeEventEnvelope> events)
    {
        return BuildEvents(
            events,
            MaxEventTimelineEntries,
            DefaultLocalizationResolver,
            "en");
    }

    /// <summary>
    /// Formats the bounded event rail. The newest suffix is retained in its
    /// original order. If a retained child points to an omitted ancestor, the
    /// row says "parent truncated" instead of inventing a root or reordering
    /// the stream. A non-positive limit is a safe, deterministic empty view.
    /// </summary>
    public static string BuildEvents(
        IReadOnlyList<RuntimeEventEnvelope> events,
        int maxEntries)
    {
        return BuildEvents(
            events,
            maxEntries,
            DefaultLocalizationResolver,
            "en");
    }

    /// <summary>
    /// Builds the player-facing event rail with an explicit presentation
    /// localization source. Only the approved event-to-semantic mappings are
    /// eligible for this surface; all other protocol events are omitted.
    /// </summary>
    public static string BuildEvents(
        IReadOnlyList<RuntimeEventEnvelope> events,
        RuntimeLocalizationResolver localizationResolver,
        string language = "en")
    {
        return BuildEvents(
            events,
            MaxEventTimelineEntries,
            localizationResolver,
            language);
    }

    /// <summary>
    /// Filters and coalesces safe semantic rows before applying the display
    /// limit. This prevents technical/unknown events from crowding out a
    /// player-visible cue and keeps the omitted count meaningful. The source
    /// envelope is never formatted on this surface.
    /// </summary>
    public static string BuildEvents(
        IReadOnlyList<RuntimeEventEnvelope> events,
        int maxEntries,
        RuntimeLocalizationResolver localizationResolver,
        string language = "en")
    {
        if (localizationResolver is null)
            throw new ArgumentNullException(nameof(localizationResolver));

        var visibleEvents = BuildPlayerEventRows(events, localizationResolver, language);
        if (visibleEvents.Count == 0)
            return "事件摘要：暂无事件";

        var visibleCount = Math.Max(0, maxEntries);
        var start = Math.Max(0, visibleEvents.Count - visibleCount);
        var builder = new StringBuilder("事件摘要");
        if (start > 0)
        {
            builder.Append(" · 还有 ")
                .Append(start.ToString(CultureInfo.InvariantCulture))
                .Append(" 条");
        }
        builder.Append("：");

        if (start >= visibleEvents.Count)
            return builder.Append("暂无事件").ToString();

        for (var index = start; index < visibleEvents.Count; index++)
        {
            var line = visibleEvents[index];
            if (index > start) builder.Append('\n');
            builder.Append("• ").Append(line);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Formats the complete bounded event rail for an explicitly enabled
    /// diagnostic surface. Keep this separate from BuildEvents so a normal
    /// view cannot accidentally inherit wire diagnostics.
    /// </summary>
    public static string BuildDebugEvents(
        IReadOnlyList<RuntimeEventEnvelope> events,
        int maxEntries)
    {
        var timeline = BuildEventTimeline(events);
        if (timeline.Count == 0)
            return "事件摘要（事件时间线）：暂无适配器事件";

        var visibleCount = Math.Max(0, maxEntries);
        var start = Math.Max(0, timeline.Count - visibleCount);
        var truncated = start;
        var builder = new StringBuilder("事件摘要（事件时间线 · Adapter 顺序");
        if (truncated > 0)
        {
            builder.Append(" · 已截断 ")
                .Append(truncated.ToString(CultureInfo.InvariantCulture))
                .Append(" 条");
        }
        builder.Append("）：\n");

        if (start >= timeline.Count)
        {
            builder.Append("（当前截断上限为 0，没有可显示事件）");
            return builder.ToString();
        }

        for (var index = start; index < timeline.Count; index++)
        {
            AppendTimelineEntry(builder, timeline, index, start);
            if (index + 1 < timeline.Count) builder.Append('\n');
        }
        return builder.ToString();
    }

    public static string BuildDebugEvents(IReadOnlyList<RuntimeEventEnvelope> events)
    {
        return BuildDebugEvents(events, MaxEventTimelineEntries);
    }

    private static IReadOnlyList<string> BuildPlayerEventRows(
        IReadOnlyList<RuntimeEventEnvelope> events,
        RuntimeLocalizationResolver localizationResolver,
        string language)
    {
        if (events is null || events.Count == 0)
            return Array.Empty<string>();

        var rows = new List<string>();
        string previousSemanticKey = null;
        for (var index = 0; index < events.Count; index++)
        {
            if (!TryResolvePlayerEvent(
                    events[index],
                    localizationResolver,
                    language,
                    out var semanticKey,
                    out var text))
            {
                continue;
            }

            // Adjacent identical semantic cues are one player-facing update.
            // Do not merge non-adjacent actions: two separate cards/attacks
            // remain visible in their original relative order.
            if (string.Equals(previousSemanticKey, semanticKey, StringComparison.Ordinal))
                continue;

            rows.Add(text);
            previousSemanticKey = semanticKey;
        }

        return rows.AsReadOnly();
    }

    private static bool TryResolvePlayerEvent(
        RuntimeEventEnvelope eventEnvelope,
        RuntimeLocalizationResolver localizationResolver,
        string language,
        out string semanticKey,
        out string text)
    {
        semanticKey = string.Empty;
        text = string.Empty;
        if (eventEnvelope is null)
            return false;

        var eventType = NormalizeEventToken(eventEnvelope.Type);
        RuntimeLocalizedText localized;
        switch (eventType)
        {
            case "CARD_PLAYED":
                localized = localizationResolver.ResolveSemantic(
                    RuntimeSemanticKind.Action,
                    "PLAY_CARD",
                    language);
                break;
            case "ATTACK_DECLARED":
                localized = localizationResolver.ResolveSemantic(
                    RuntimeSemanticKind.Action,
                    "ATTACK",
                    language);
                break;
            case "PHASE_CHANGED":
                localized = localizationResolver.ResolveSemantic(
                    RuntimeSemanticKind.Phase,
                    eventEnvelope.Phase,
                    language);
                break;
            case "GAME_OVER":
                // GAME_OVER has one approved neutral semantic. Do not inspect
                // outcome/winner fields or infer a result from the snapshot.
                localized = localizationResolver.ResolveSemantic(
                    RuntimeSemanticKind.Phase,
                    "OVER",
                    language);
                break;
            default:
                return false;
        }

        if (!localized.IsKnownSemantic || string.IsNullOrWhiteSpace(localized.Text))
            return false;

        semanticKey = localized.LocalizationKey;
        text = localized.Text;
        return true;
    }

    private static string NormalizeEventToken(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant().Replace('-', '_');
    }

    private static void AppendTimelineEntry(
        StringBuilder builder,
        IReadOnlyList<RuntimeBattleEventTimelineEntry> timeline,
        int index,
        int visibleStart)
    {
        var entry = timeline[index];
        var displayDepth = Math.Min(MaxEventTimelineDepth, Math.Max(0, entry.Depth));
        if (displayDepth > 0)
            builder.Append(new string(' ', displayDepth * 2)).Append("└─ ");
        else
            builder.Append("• ");

        if (!entry.IsKnownType || entry.Event is null)
        {
            builder.Append("UNKNOWN type=")
                .Append(SafeToken(entry.Type, "(missing)"))
                .Append(" id=")
                .Append(SafeToken(entry.EventId, "(missing)"));
        }
        else
        {
            var item = entry.Event;
            builder.Append(entry.Category)
                .Append(' ')
                .Append(SafeToken(item.Type, "(missing)"))
                .Append(" id=")
                .Append(SafeToken(item.EventId, "(missing)"))
                .Append(" T")
                .Append(item.Turn.ToString(CultureInfo.InvariantCulture))
                .Append('/')
                .Append(SafeToken(item.Phase, "?"))
                .Append(" r")
                .Append(item.SnapshotRevision.ToString(CultureInfo.InvariantCulture));

            if (item.TargetIds is not null && item.TargetIds.Count > 0)
            {
                builder.Append(" targets=")
                    .Append(item.TargetIds.Count.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (entry.ParentEventId is not null)
        {
            builder.Append(" parent=")
                .Append(SafeToken(entry.ParentEventId, "(missing)"));
        }
        if (entry.HasMissingParent)
            builder.Append(" [parent missing]");
        else if (entry.ParentIndex >= 0 && entry.ParentIndex < visibleStart)
            builder.Append(" [parent truncated]");
        if (entry.IsInCycle || entry.HasCyclicParent)
            builder.Append(" [parent cycle]");
        if (entry.IsDuplicate)
            builder.Append(" [duplicate id]");
        if (entry.Depth > MaxEventTimelineDepth)
            builder.Append(" [depth clipped]");
    }

    private static void ResolveTimelineDepth(
        int index,
        IReadOnlyList<RuntimeBattleEventTimelineEntry> entries,
        int[] states,
        List<int> path)
    {
        if (index < 0 || index >= entries.Count || states[index] == 2)
            return;
        if (states[index] == 1)
        {
            var cycleStart = path.LastIndexOf(index);
            if (cycleStart >= 0)
            {
                for (var pathIndex = cycleStart; pathIndex < path.Count; pathIndex++)
                {
                    var cycleEntry = entries[path[pathIndex]];
                    cycleEntry.IsInCycle = true;
                    cycleEntry.Depth = 0;
                }
            }
            return;
        }

        states[index] = 1;
        path.Add(index);
        var entry = entries[index];
        if (entry.ParentIndex < 0)
        {
            entry.Depth = 0;
        }
        else
        {
            ResolveTimelineDepth(entry.ParentIndex, entries, states, path);
            var parent = entries[entry.ParentIndex];
            if (entry.IsInCycle)
            {
                entry.Depth = 0;
            }
            else if (parent.IsInCycle || parent.HasCyclicParent)
            {
                entry.Depth = 1;
                entry.HasCyclicParent = true;
            }
            else
            {
                entry.Depth = parent.Depth >= int.MaxValue - 1
                    ? int.MaxValue
                    : parent.Depth + 1;
                entry.HasCyclicParent = parent.HasCyclicParent;
            }
        }

        path.RemoveAt(path.Count - 1);
        states[index] = 2;
    }

    private static string EventCategory(string type)
    {
        if (type.StartsWith("PUNISH_", StringComparison.Ordinal)) return "PUNISH";
        if (type.StartsWith("CHAIN_", StringComparison.Ordinal)) return "CHAIN";
        if (type == "DAMAGE_APPLIED") return "DAMAGE";
        if (type.StartsWith("CASTLE_", StringComparison.Ordinal)) return "CASTLE";
        if (type.StartsWith("LEADER_", StringComparison.Ordinal)) return "LEADER";
        if (type.StartsWith("PULL_", StringComparison.Ordinal) || type == "CARD_PULLED") return "PULL";
        return "EVENT";
    }

    private static string SafeToken(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        var builder = new StringBuilder(Math.Min(value.Length, MaxStableTokenLength));
        for (var index = 0; index < value.Length && builder.Length < MaxStableTokenLength; index++)
        {
            var character = value[index];
            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        var token = builder.ToString().Trim();
        if (token.Length == 0) return fallback;
        if (value.Length > MaxStableTokenLength)
        {
            // Control/whitespace characters may have been removed by Trim,
            // so the sanitized token can be shorter than the source cap.
            var clippedLength = Math.Min(MaxStableTokenLength - 1, token.Length);
            return token.Substring(0, clippedLength) + "…";
        }
        return token;
    }

    private static string FormatCards(IReadOnlyList<RuntimeCardSnapshot> cards, string emptyText)
    {
        if (cards is null)
            return "不可用";
        if (cards.Count == 0)
            return emptyText;

        var builder = new StringBuilder();
        var shown = 0;
        for (var index = 0; index < cards.Count && shown < MaxCardsPerSummary; index++)
        {
            var card = cards[index];
            if (card is null) continue;

            if (shown > 0) builder.Append(", ");
            builder.Append(Display(card.CardId, "未知卡牌"));
            if (card.EntityId > 0)
            {
                builder.Append("#")
                    .Append(card.EntityId.ToString(CultureInfo.InvariantCulture));
            }
            if (card.Sealed) builder.Append(" [sealed]");
            shown++;
        }

        if (shown == 0)
            return "不可用";
        if (cards.Count > shown)
        {
            builder.Append(" …（还有 ")
                .Append((cards.Count - shown).ToString(CultureInfo.InvariantCulture))
                .Append(" 张）");
        }
        return builder.ToString();
    }

    private static string FormatVisibleCards(
        IReadOnlyList<RuntimeCardSnapshot> cards,
        string emptyText,
        CardCatalog cardCatalog)
    {
        if (cards is null)
            return "不可用";
        if (cards.Count == 0)
            return emptyText;

        var builder = new StringBuilder();
        var shown = 0;
        for (var index = 0; index < cards.Count && shown < MaxCardsPerSummary; index++)
        {
            var card = cards[index];
            if (card is null) continue;

            var name = string.Empty;
            if (cardCatalog != null &&
                cardCatalog.TryGetPresentationMetadata(card.CardId, out var metadata) &&
                metadata != null)
            {
                name = metadata.Name;
            }

            if (string.IsNullOrWhiteSpace(name)) name = "卡牌";
            if (shown > 0) builder.Append(", ");
            builder.Append(name);
            shown++;
        }

        if (shown == 0)
            return "不可用";
        if (cards.Count > shown)
        {
            builder.Append(" …（还有 ")
                .Append((cards.Count - shown).ToString(CultureInfo.InvariantCulture))
                .Append(" 张）");
        }
        return builder.ToString();
    }

    private static string Display(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static void AddAvailableLeaderPart(
        List<string> parts,
        string label,
        string value)
    {
        if (parts is null || string.IsNullOrWhiteSpace(label) ||
            string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, Unavailable, StringComparison.Ordinal))
            return;

        parts.Add(label + " " + value);
    }
}
}
