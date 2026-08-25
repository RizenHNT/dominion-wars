#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DominionWars.Adapters;

namespace DominionWars.Unity.UI
{

/// <summary>
/// Pure presentation formatting for the neutral runtime battle skeleton.
/// This class only reads contract DTOs; it does not derive legality, costs,
/// targets, phase transitions, or victory state.
/// </summary>
public static class RuntimeBattlePanelPresentationModel
{
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

    public static string BuildMatchLine(RuntimeSnapshotEnvelope snapshot)
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
            return "共享王城：数据不可用";
        if (!castle.Enabled)
            return "共享王城：未启用";

        return "共享王城：生命 " + castle.Health.ToString(CultureInfo.InvariantCulture);
    }

    public static string BuildPhaseSummary(RuntimeSnapshotEnvelope snapshot)
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
            .Append("统领：当前快照未单列，UI 不从场面推导")
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

    public static string BuildEvents(IReadOnlyList<RuntimeEventEnvelope> events)
    {
        if (events is null || events.Count == 0)
            return "事件摘要：暂无适配器事件";

        var start = Math.Max(0, events.Count - 6);
        var builder = new StringBuilder("事件摘要（最新）：\n");
        for (var index = start; index < events.Count; index++)
        {
            var item = events[index];
            if (item is null)
            {
                builder.Append("未知事件\n");
                continue;
            }

            builder.Append(Display(item.Type, "未知事件"))
                .Append(" @ T")
                .Append(item.Turn.ToString(CultureInfo.InvariantCulture))
                .Append("/")
                .Append(Display(item.Phase, "未提供"))
                .Append(" rev ")
                .Append(item.SnapshotRevision.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }
        return builder.ToString().TrimEnd();
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

    private static string Display(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
}
