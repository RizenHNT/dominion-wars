#nullable enable annotations

using System;
using System.Globalization;
using System.Text;

namespace DominionWars.Unity.UI
{

/// <summary>
/// Renderer-neutral card detail content for click/hover inspection. The
/// interaction host can bind DetailText to a panel, tooltip or accessible
/// live-region without needing to know engine or catalog types.
/// </summary>
public sealed class RuntimeCardInspectModel
{
    private RuntimeCardInspectModel(RuntimeCardDisplayModel card, bool diagnosticsEnabled)
    {
        Card = card ?? throw new ArgumentNullException(nameof(card));
        Title = card.Name;
        IdentityLine = "ID " + Display(card.StableId) +
            " | ENTITY " + (card.EntityId > 0
                ? card.EntityId.ToString(CultureInfo.InvariantCulture)
                : RuntimeCardDisplayModel.Unavailable) +
            " | ZONE " + card.ZoneLabel;
        TypeFactionLine = "TYPE " + Display(card.Type) + " | FACTION " + Display(card.Faction);
        PrintedStatsLine = card.PrintedStatsLine;
        CurrentStatsLine = card.CurrentStatsLine;
        PunishAndCostLine = card.PunishAndCostLine;
        MechanicalFeesLine = card.MechanicalFeesLine;
        DiagnosticPunishAndCostLine = card.LegacyPunishAndCostLine;
        DiagnosticMechanicalFeesLine = card.DiagnosticMechanicalFeesLine;
        RulesText = card.RulesText;
        KeywordsLine = card.KeywordsLine;
        TagsLine = card.TagsLine;
        DataNotice = card.HasMissingData
            ? card.MissingDataText
            : "CARD DATA OK · PRINTED VALUES ONLY";
        DetailText = BuildDetailText(this);
        DebugDetailText = BuildDebugDetailText(this);
        if (diagnosticsEnabled)
            DetailText = DebugDetailText;
    }

    public RuntimeCardDisplayModel Card { get; }
    public string Title { get; }
    public string IdentityLine { get; }
    public string TypeFactionLine { get; }
    public string PrintedStatsLine { get; }
    public string CurrentStatsLine { get; }
    public string PunishAndCostLine { get; }
    public string MechanicalFeesLine { get; }
    public string DiagnosticPunishAndCostLine { get; }
    public string DiagnosticMechanicalFeesLine { get; }
    public string RulesText { get; }
    public string KeywordsLine { get; }
    public string TagsLine { get; }
    public string DataNotice { get; }
    public string DetailText { get; }
    public string DebugDetailText { get; }
    public bool HasMissingData => Card.HasMissingData;

    public static RuntimeCardInspectModel Build(RuntimeCardDisplayModel card)
    {
        return new RuntimeCardInspectModel(card, false);
    }

    /// <summary>
    /// Builds the complete card inspection text for an explicitly enabled
    /// diagnostic surface. Normal inspection must use Build so stable IDs,
    /// entity IDs, zones and data-pipeline notices stay out of the player UI.
    /// </summary>
    public static RuntimeCardInspectModel BuildDebug(RuntimeCardDisplayModel card)
    {
        return new RuntimeCardInspectModel(card, true);
    }

    public static RuntimeCardInspectModel FromCard(RuntimeCardDisplayModel card)
    {
        return Build(card);
    }

    public static string BuildDetailText(RuntimeCardDisplayModel card)
    {
        return Build(card).DetailText;
    }

    public static string BuildDebugDetailText(RuntimeCardDisplayModel card)
    {
        return BuildDebug(card).DebugDetailText;
    }

    private static string BuildDetailText(RuntimeCardInspectModel model)
    {
        var builder = new StringBuilder(256);
        builder.Append(model.Title).Append('\n')
            .Append(model.TypeFactionLine).Append('\n')
            .Append(model.PunishAndCostLine).Append('\n');
        AppendIfPresent(builder, model.PrintedStatsLine);
        AppendIfPresent(builder, model.CurrentStatsLine);
        AppendIfPresent(builder, model.MechanicalFeesLine);
        builder.Append("RULES ").Append(model.RulesText).Append('\n')
            .Append(model.KeywordsLine).Append('\n')
            .Append(model.TagsLine);
        return builder.ToString();
    }

    private static string BuildDebugDetailText(RuntimeCardInspectModel model)
    {
        var builder = new StringBuilder(256);
        builder.Append(model.Title).Append('\n')
            .Append(model.IdentityLine).Append('\n')
            .Append(model.TypeFactionLine).Append('\n')
            .Append(model.DiagnosticPunishAndCostLine).Append('\n');
        AppendIfPresent(builder, model.PrintedStatsLine);
        AppendIfPresent(builder, model.CurrentStatsLine);
        builder.Append(model.DiagnosticMechanicalFeesLine).Append('\n')
            .Append("RULES ").Append(model.RulesText).Append('\n')
            .Append(model.KeywordsLine).Append('\n')
            .Append(model.TagsLine).Append('\n')
            .Append(model.DataNotice);
        return builder.ToString();
    }

    private static void AppendIfPresent(StringBuilder builder, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (builder.Length > 0 && builder[builder.Length - 1] != '\n') builder.Append('\n');
        builder.Append(value).Append('\n');
    }

    private static string Display(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? RuntimeCardDisplayModel.Unavailable
            : value;
    }
}
}
