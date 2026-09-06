#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DominionWars.Adapters;
using DominionWars.Data;

namespace DominionWars.Unity.UI
{

/// <summary>
/// A snapshot-visible card paired with its immutable catalog definition.
///
/// This model deliberately distinguishes catalog/printed values from runtime
/// values. CurrentAttack/CurrentHealth come only from the optional runtime
/// snapshot projection; they are never inferred from printed catalog values.
/// </summary>
public sealed class RuntimeCardDisplayModel
{
    public const string Unavailable = "Unavailable";
    public const string UnknownCard = "Unknown card";

    private RuntimeCardDisplayModel(
        RuntimeCardSnapshot snapshot,
        RuntimeCardZone zone,
        CardPresentationMetadata? definition)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Zone = zone;
        DefinitionAvailable = definition is not null;

        // Preserve the wire value exactly. Display fallbacks are applied only
        // by the human-readable fields, never by the stable-id property.
        StableId = snapshot.CardId ?? string.Empty;
        EntityId = snapshot.EntityId;
        OwnerPlayer = snapshot.OwnerPlayer;
        IsSealed = snapshot.Sealed;

        if (definition is null)
        {
            Name = UnknownCard;
            Type = Unavailable;
            Faction = Unavailable;
            PrintedAttack = null;
            PrintedHealth = null;
            PrintedPunish = null;
            DeclaredCost = null;
            PunishCost = null;
            CommitCost = null;
            UploadCost = null;
            DownloadCost = null;
            HasCommitCost = false;
            HasUploadCost = false;
            HasDownloadCost = false;
            CurrentAttack = null;
            CurrentHealth = null;
            RulesText = Unavailable;
            FlavorText = Unavailable;
            Keywords = Array.Empty<string>();
            Tags = Array.Empty<string>();
            ArtId = null;
            IsMinion = false;
            IsLeader = false;
            PunishActivatable = false;
            MissingDataText = BuildMissingDataText(StableId);
            return;
        }

        Name = Safe(definition.Name, UnknownCard);
        Type = Safe(definition.Type);
        Faction = Safe(definition.Faction);
        // These are printed/base values from CardCatalog. CurrentAttack and
        // CurrentHealth above remain sourced exclusively from the snapshot.
        PrintedAttack = definition.PrintedAttack;
        PrintedHealth = definition.PrintedHealth;
        PrintedPunish = definition.PrintedPunish;
        DeclaredCost = definition.DeclaredCost;
        PunishCost = definition.PunishCost;
        HasCommitCost = definition.HasCommitCost;
        HasUploadCost = definition.HasUploadCost;
        HasDownloadCost = definition.HasDownloadCost;
        CommitCost = HasCommitCost ? definition.CommitCost : (int?)null;
        UploadCost = HasUploadCost ? definition.UploadCost : (int?)null;
        DownloadCost = HasDownloadCost ? definition.DownloadCost : (int?)null;
        RulesText = string.IsNullOrWhiteSpace(definition.Text) ? "—" : definition.Text;
        FlavorText = string.IsNullOrWhiteSpace(definition.Flavor) ? "—" : definition.Flavor!;
        Keywords = CopyStrings(definition.Keywords);
        Tags = CopyStrings(definition.Tags);
        ArtId = string.IsNullOrWhiteSpace(definition.ArtId) ? null : definition.ArtId;
        IsMinion = definition.IsMinion;
        IsLeader = definition.IsLeader;
        // Current values are meaningful only for an applicable minion. They
        // remain sourced exclusively from the snapshot; a printed value is
        // never promoted to a runtime value when the snapshot omits it.
        CurrentAttack = IsMinion ? snapshot.CurrentAttack : (int?)null;
        CurrentHealth = IsMinion ? snapshot.CurrentHealth : (int?)null;
        PunishActivatable = definition.PunishActivatable;
        MissingDataText = string.Empty;
    }

    public RuntimeCardSnapshot Snapshot { get; }
    public RuntimeCardZone Zone { get; }
    public string StableId { get; }
    public long EntityId { get; }
    public int OwnerPlayer { get; }
    public bool IsSealed { get; }

    public bool DefinitionAvailable { get; }
    public bool HasMissingData => !DefinitionAvailable;
    public string MissingDataText { get; }

    public string Name { get; }
    public string Type { get; }
    public string Faction { get; }
    public bool IsMinion { get; }
    public bool IsLeader { get; }

    /// <summary>Printed/base attack from CardCatalog; null means not applicable or missing.</summary>
    public int? PrintedAttack { get; }

    /// <summary>Printed/base health from CardCatalog; null means not applicable or missing.</summary>
    public int? PrintedHealth { get; }

    /// <summary>
    /// Runtime current attack from the authoritative snapshot, when present.
    /// Null means the snapshot did not project a current value.
    /// </summary>
    public int? CurrentAttack { get; }

    /// <summary>
    /// Runtime current health from the authoritative snapshot, when present.
    /// Null means the snapshot did not project a current value.
    /// </summary>
    public int? CurrentHealth { get; }

    /// <summary>Printed/base punishment value from CardCatalog.</summary>
    public int? PrintedPunish { get; }

    /// <summary>Declared legacy/data cost; no legality or effective cost is inferred.</summary>
    public int? DeclaredCost { get; }

    /// <summary>Declared punishment-response cost, when supplied by card data.</summary>
    public int? PunishCost { get; }

    /// <summary>Declared mechanical commit fee, when supplied by card data.</summary>
    public int? CommitCost { get; }

    /// <summary>Whether commitCost was explicitly present in the source data.</summary>
    public bool HasCommitCost { get; }

    /// <summary>Declared mechanical upload fee, when supplied by card data.</summary>
    public int? UploadCost { get; }

    /// <summary>Whether uploadCost was explicitly present in the source data.</summary>
    public bool HasUploadCost { get; }

    /// <summary>Declared mechanical download fee, when supplied by card data.</summary>
    public int? DownloadCost { get; }

    /// <summary>Whether downloadCost was explicitly present in the source data.</summary>
    public bool HasDownloadCost { get; }

    public bool PunishActivatable { get; }
    public string RulesText { get; }
    public string FlavorText { get; }
    public IReadOnlyList<string> Keywords { get; }
    public IReadOnlyList<string> Tags { get; }
    public string? ArtId { get; }

    public string ZoneLabel
    {
        get
        {
            switch (Zone)
            {
                case RuntimeCardZone.OwnHand: return "Own hand";
                case RuntimeCardZone.OwnAmbush: return "Own ambush";
                case RuntimeCardZone.OwnField: return "Own field";
                case RuntimeCardZone.OpponentField: return "Opponent field";
                case RuntimeCardZone.OwnLeader: return "Own leader";
                case RuntimeCardZone.OpponentLeader: return "Opponent leader";
                case RuntimeCardZone.OwnGraveyard: return "Own graveyard";
                case RuntimeCardZone.OpponentGraveyard: return "Opponent graveyard";
                case RuntimeCardZone.OwnCommitQueue: return "Own commit queue";
                case RuntimeCardZone.OwnCloudStack: return "Own cloud stack";
                default: return Unavailable;
            }
        }
    }

    /// <summary>
    /// Creates one item for a card that the caller has already obtained from
    /// BuildVisibleCards. The method name is intentionally explicit: a raw
    /// RuntimeCardSnapshot alone does not grant visibility to hidden zones.
    /// </summary>
    internal static RuntimeCardDisplayModel CreateVisible(
        RuntimeCardSnapshot snapshot,
        RuntimeCardZone zone,
        CardCatalog? cardCatalog)
    {
        if (!IsVisibleZone(zone))
            throw new ArgumentOutOfRangeException(nameof(zone), "The requested zone is not a snapshot-visible card zone.");

        return new RuntimeCardDisplayModel(snapshot, zone, FindDefinition(snapshot, cardCatalog));
    }

    /// <summary>
    /// Builds only cards that the viewer snapshot exposes. The opponent hand
    /// is never traversed, even if a malformed caller-provided snapshot puts
    /// card objects in that redacted list. Public field, leader and graveyard
    /// lists are consumed exactly as provided by the snapshot boundary.
    /// </summary>
    public static IReadOnlyList<RuntimeCardDisplayModel> BuildVisibleCards(
        RuntimeSnapshotEnvelope snapshot,
        CardCatalog? cardCatalog)
    {
        if (snapshot is null || snapshot.Players is null)
            return Array.Empty<RuntimeCardDisplayModel>();

        var result = new List<RuntimeCardDisplayModel>();
        var viewerId = snapshot.ViewerPlayerId;
        var viewerFound = false;
        for (var playerIndex = 0; playerIndex < snapshot.Players.Count; playerIndex++)
        {
            var player = snapshot.Players[playerIndex];
            if (player is null) continue;

            var isViewer = !viewerFound &&
                !string.IsNullOrWhiteSpace(viewerId) &&
                string.Equals(player.PlayerId, viewerId, StringComparison.Ordinal);
            if (isViewer) viewerFound = true;
            if (isViewer)
            {
                Append(result, player.Hand, RuntimeCardZone.OwnHand, cardCatalog);
                Append(result, player.Ambush, RuntimeCardZone.OwnAmbush, cardCatalog);
                Append(result, player.Field, RuntimeCardZone.OwnField, cardCatalog);
                Append(result, player.LeaderZone, RuntimeCardZone.OwnLeader, cardCatalog);
                Append(result, player.Graveyard, RuntimeCardZone.OwnGraveyard, cardCatalog);
                Append(result, player.CommitQueue, RuntimeCardZone.OwnCommitQueue, cardCatalog);
                Append(result, player.CloudStack, RuntimeCardZone.OwnCloudStack, cardCatalog);
            }
            else
            {
                // The snapshot contract makes these zones public. Do not add
                // player.Hand here: that list is the hidden opponent hand.
                Append(result, player.Field, RuntimeCardZone.OpponentField, cardCatalog);
                Append(result, player.LeaderZone, RuntimeCardZone.OpponentLeader, cardCatalog);
                Append(result, player.Graveyard, RuntimeCardZone.OpponentGraveyard, cardCatalog);
            }
        }

        return result.AsReadOnly();
    }

    /// <summary>Alias kept concise for renderers that already use "cards" terminology.</summary>
    public static IReadOnlyList<RuntimeCardDisplayModel> Build(
        RuntimeSnapshotEnvelope snapshot,
        CardCatalog? cardCatalog)
    {
        return BuildVisibleCards(snapshot, cardCatalog);
    }

    public static bool TryFindVisible(
        RuntimeSnapshotEnvelope snapshot,
        long entityId,
        CardCatalog? cardCatalog,
        out RuntimeCardDisplayModel? card)
    {
        card = null;
        if (entityId <= 0) return false;

        var visible = BuildVisibleCards(snapshot, cardCatalog);
        for (var index = 0; index < visible.Count; index++)
        {
            if (visible[index].EntityId != entityId) continue;
            card = visible[index];
            return true;
        }

        return false;
    }

    public string PrintedStatsLine
    {
        get
        {
            if (!HasPrintedStats) return string.Empty;
            return "PRINTED ATK " + DisplayNumber(PrintedAttack) +
                " | HP " + DisplayNumber(PrintedHealth);
        }
    }

    /// <summary>True when both applicable printed minion stats are real values.</summary>
    public bool HasPrintedStats => IsMinion && PrintedAttack.HasValue && PrintedHealth.HasValue;

    public string CurrentStatsLine => HasCurrentStats
        ? "CURRENT ATK " + DisplayNumber(CurrentAttack) + " | HP " + DisplayNumber(CurrentHealth)
        : string.Empty;

    /// <summary>True when both applicable current minion stats are projected.</summary>
    public bool HasCurrentStats => IsMinion && CurrentAttack.HasValue && CurrentHealth.HasValue;

    public string PunishAndCostLine
    {
        get
        {
            if (!DefinitionAvailable) return "PRINTED PUNISH " + Unavailable;

            return "PRINTED PUNISH " + DisplayNumber(PrintedPunish);
        }
    }

    /// <summary>
    /// Legacy diagnostic line retained for explicitly enabled technical
    /// surfaces. Player-facing renderers must use PunishAndCostLine, which
    /// intentionally omits the obsolete standalone COST field.
    /// </summary>
    public string LegacyPunishAndCostLine
    {
        get
        {
            if (!DefinitionAvailable)
                return "PRINTED PUNISH " + Unavailable + " | COST " + Unavailable;

            return "PRINTED PUNISH " + DisplayNumber(PrintedPunish) +
                " | COST " + DisplayNumber(DeclaredCost);
        }
    }

    public string MechanicalFeesLine
    {
        get
        {
            var fees = new List<string>(3);
            if (HasCommitCost) fees.Add("COMMIT " + DisplayNumber(CommitCost));
            if (HasUploadCost) fees.Add("UPLOAD " + DisplayNumber(UploadCost));
            if (HasDownloadCost) fees.Add("DOWNLOAD " + DisplayNumber(DownloadCost));
            return string.Join(" | ", fees);
        }
    }

    /// <summary>
    /// Complete technical fee diagnostics. Missing fields remain distinguishable
    /// from explicit zero on this debug-only surface.
    /// </summary>
    public string DiagnosticMechanicalFeesLine
    {
        get
        {
            return "COMMIT " + DisplayNumber(CommitCost) +
                " | UPLOAD " + DisplayNumber(UploadCost) +
                " | DOWNLOAD " + DisplayNumber(DownloadCost);
        }
    }

    public string KeywordsLine => FormatList("KEYWORDS", Keywords, DefinitionAvailable);
    public string TagsLine => FormatList("TAGS", Tags, DefinitionAvailable);

    private static void Append(
        List<RuntimeCardDisplayModel> destination,
        IReadOnlyList<RuntimeCardSnapshot> cards,
        RuntimeCardZone zone,
        CardCatalog? cardCatalog)
    {
        if (cards is null) return;
        for (var index = 0; index < cards.Count; index++)
        {
            var snapshot = cards[index];
            if (snapshot is null) continue;
            destination.Add(CreateVisible(snapshot, zone, cardCatalog));
        }
    }

    private static CardPresentationMetadata? FindDefinition(
        RuntimeCardSnapshot snapshot,
        CardCatalog? cardCatalog)
    {
        if (snapshot is null || cardCatalog is null ||
            string.IsNullOrWhiteSpace(snapshot.CardId))
            return null;

        return cardCatalog.TryGetPresentationMetadata(snapshot.CardId, out var metadata)
            ? metadata
            : null;
    }

    private static bool IsVisibleZone(RuntimeCardZone zone)
    {
        return zone == RuntimeCardZone.OwnHand ||
            zone == RuntimeCardZone.OwnAmbush ||
            zone == RuntimeCardZone.OwnField ||
            zone == RuntimeCardZone.OpponentField ||
            zone == RuntimeCardZone.OwnLeader ||
            zone == RuntimeCardZone.OpponentLeader ||
            zone == RuntimeCardZone.OwnGraveyard ||
            zone == RuntimeCardZone.OpponentGraveyard ||
            zone == RuntimeCardZone.OwnCommitQueue ||
            zone == RuntimeCardZone.OwnCloudStack;
    }

    private static string FormatList(
        string label,
        IReadOnlyList<string> values,
        bool dataAvailable)
    {
        if (!dataAvailable) return label + " " + Unavailable;
        if (values is null || values.Count == 0) return label + " —";

        var builder = new StringBuilder(label.Length + 2 + values.Count * 8);
        builder.Append(label).Append(' ');
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0) builder.Append(" · ");
            builder.Append(values[index]);
        }
        return builder.ToString();
    }

    private static string DisplayNumber(int? value, string fallback = "—")
    {
        return value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : fallback;
    }

    private static string BuildMissingDataText(string stableId)
    {
        return "CARD DATA " + Unavailable + " · STABLE ID " +
            (string.IsNullOrWhiteSpace(stableId) ? Unavailable : stableId);
    }

    private static string Safe(string? value, string fallback = Unavailable)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value!;
    }

    private static IReadOnlyList<string> CopyStrings(IReadOnlyCollection<string> values)
    {
        if (values is null || values.Count == 0) return Array.Empty<string>();
        var copy = new List<string>(values.Count);
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) copy.Add(value);
        }
        return copy.AsReadOnly();
    }

}

/// <summary>Snapshot-visible zones accepted by RuntimeCardDisplayModel.</summary>
public enum RuntimeCardZone
{
    OwnHand,
    OwnAmbush,
    OwnField,
    OpponentField,
    OwnLeader,
    OpponentLeader,
    OwnGraveyard,
    OpponentGraveyard,
    OwnCommitQueue,
    OwnCloudStack,
}
}
