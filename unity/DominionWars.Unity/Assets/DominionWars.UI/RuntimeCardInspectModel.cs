#nullable enable annotations

using System;
using System.Globalization;
using System.Text;
using DominionWars.Unity.Runtime;

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
        : this(card, diagnosticsEnabled, null, "en")
    {
    }

    private RuntimeCardInspectModel(
        RuntimeCardDisplayModel card,
        bool diagnosticsEnabled,
        RuntimeLocalizationResolver? localizationResolver,
        string language)
    {
        Card = card ?? throw new ArgumentNullException(nameof(card));
        Title = card.Name;
        IdentityLine = "ID " + Display(card.StableId) +
            " | ENTITY " + (card.EntityId > 0
                ? card.EntityId.ToString(CultureInfo.InvariantCulture)
                : RuntimeCardDisplayModel.Unavailable) +
            " | ZONE " + card.ZoneLabel;
        TypeFactionLine = BuildTypeFactionLine(card, localizationResolver, language);
        PrintedStatsLine = BuildPrintedStatsLine(card, localizationResolver, language);
        CurrentStatsLine = BuildCurrentStatsLine(card, localizationResolver, language);
        PunishAndCostLine = BuildPunishAndCostLine(card, localizationResolver, language);
        MechanicalFeesLine = BuildMechanicalFeesLine(card, localizationResolver, language);
        DiagnosticPunishAndCostLine = BuildDiagnosticPunishAndCostLine(card, localizationResolver, language);
        DiagnosticMechanicalFeesLine = BuildDiagnosticMechanicalFeesLine(card, localizationResolver, language);
        RulesText = card.RulesText;
        LeaderWinText = card.LeaderWinText;
        ChantLine = BuildChantLine(card, localizationResolver, language);
        LandmarkProgressLine = BuildLandmarkProgressLine(card, localizationResolver, language);
        KeywordsLine = BuildListLine(card.Keywords, card.DefinitionAvailable, "card.keywords", "KEYWORDS", localizationResolver, language);
        TagsLine = BuildListLine(card.Tags, card.DefinitionAvailable, "card.tags", "TAGS", localizationResolver, language);
        RulesLabel = Label(localizationResolver, "card.rules", "RULES", language);
        GoalLabel = Label(localizationResolver, "card.goal", "目标", language);
        GoalLine = BuildLeaderGoalLine(LeaderWinText, GoalLabel);
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
    public string LeaderWinText { get; }
    public string ChantLine { get; }
    public string LandmarkProgressLine { get; }
    public string KeywordsLine { get; }
    public string TagsLine { get; }
    public string RulesLabel { get; }
    public string GoalLabel { get; }
    public string GoalLine { get; }
    public string DataNotice { get; }
    public string DetailText { get; }
    public string DebugDetailText { get; }
    public bool HasMissingData => Card.HasMissingData;

    public static RuntimeCardInspectModel Build(RuntimeCardDisplayModel card)
    {
        return new RuntimeCardInspectModel(card, false);
    }

    /// <summary>
    /// Builds the same renderer-neutral inspection model with presentation
    /// labels supplied by the shared runtime resolver. Authored card name,
    /// rules, keyword values and tag values remain untouched content data.
    /// </summary>
    public static RuntimeCardInspectModel Build(
        RuntimeCardDisplayModel card,
        RuntimeLocalizationResolver localizationResolver,
        string language = "en")
    {
        if (localizationResolver == null) throw new ArgumentNullException(nameof(localizationResolver));
        return new RuntimeCardInspectModel(card, false, localizationResolver, language);
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

    public static RuntimeCardInspectModel BuildDebug(
        RuntimeCardDisplayModel card,
        RuntimeLocalizationResolver localizationResolver,
        string language = "en")
    {
        if (localizationResolver == null) throw new ArgumentNullException(nameof(localizationResolver));
        return new RuntimeCardInspectModel(card, true, localizationResolver, language);
    }

    public static RuntimeCardInspectModel FromCard(RuntimeCardDisplayModel card)
    {
        return Build(card);
    }

    public static RuntimeCardInspectModel FromCard(
        RuntimeCardDisplayModel card,
        RuntimeLocalizationResolver localizationResolver,
        string language = "en")
    {
        return Build(card, localizationResolver, language);
    }

    public static string BuildDetailText(RuntimeCardDisplayModel card)
    {
        return Build(card).DetailText;
    }

    public static string BuildDetailText(
        RuntimeCardDisplayModel card,
        RuntimeLocalizationResolver localizationResolver,
        string language = "en")
    {
        return Build(card, localizationResolver, language).DetailText;
    }

    public static string BuildDebugDetailText(RuntimeCardDisplayModel card)
    {
        return BuildDebug(card).DebugDetailText;
    }

    public static string BuildDebugDetailText(
        RuntimeCardDisplayModel card,
        RuntimeLocalizationResolver localizationResolver,
        string language = "en")
    {
        return BuildDebug(card, localizationResolver, language).DebugDetailText;
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
        AppendIfPresent(builder, model.GoalLine);
        AppendIfPresent(builder, model.ChantLine);
        AppendIfPresent(builder, model.LandmarkProgressLine);
        builder.Append(model.RulesLabel).Append(' ').Append(model.RulesText).Append('\n')
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
            .Append(model.GoalLine).Append('\n')
            .Append(model.ChantLine).Append('\n')
            .Append(model.LandmarkProgressLine).Append('\n')
            .Append(model.RulesLabel).Append(' ').Append(model.RulesText).Append('\n')
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

    private static string BuildTypeFactionLine(
        RuntimeCardDisplayModel card,
        RuntimeLocalizationResolver? localizationResolver,
        string language)
    {
        var typeLabel = Label(localizationResolver, "card.type", "TYPE", language);
        var factionLabel = Label(localizationResolver, "card.faction", "FACTION", language);
        return typeLabel + " " + Display(card.Type) + " | " + factionLabel + " " + Display(card.Faction);
    }

    private static string BuildPrintedStatsLine(
        RuntimeCardDisplayModel card,
        RuntimeLocalizationResolver? localizationResolver,
        string language)
    {
        if (!card.HasPrintedStats) return string.Empty;
        if (localizationResolver is null) return card.PrintedStatsLine;

        return Label(localizationResolver, "card.printed", "PRINTED", language) +
            " " + Label(localizationResolver, "card.attack", "ATK", language) +
            " " + DisplayNumber(card.PrintedAttack) +
            " | " + Label(localizationResolver, "card.health", "HP", language) +
            " " + DisplayNumber(card.PrintedHealth);
    }

    private static string BuildCurrentStatsLine(
        RuntimeCardDisplayModel card,
        RuntimeLocalizationResolver? localizationResolver,
        string language)
    {
        if (!card.HasCurrentStats) return string.Empty;
        if (localizationResolver is null) return card.CurrentStatsLine;

        return Label(localizationResolver, "card.current", "CURRENT", language) +
            " " + Label(localizationResolver, "card.attack", "ATK", language) +
            " " + DisplayNumber(card.CurrentAttack) +
            " | " + Label(localizationResolver, "card.health", "HP", language) +
            " " + DisplayNumber(card.CurrentHealth);
    }

    private static string BuildPunishAndCostLine(
        RuntimeCardDisplayModel card,
        RuntimeLocalizationResolver? localizationResolver,
        string language)
    {
        if (localizationResolver is null) return card.PunishAndCostLine;
        var fallback = card.DefinitionAvailable ? "—" : RuntimeCardDisplayModel.Unavailable;
        return Label(localizationResolver, "card.printedPunish", "PRINTED PUNISH", language) +
            " " + DisplayNumber(card.PrintedPunish, fallback);
    }

    private static string BuildMechanicalFeesLine(
        RuntimeCardDisplayModel card,
        RuntimeLocalizationResolver? localizationResolver,
        string language)
    {
        if (localizationResolver is null) return card.MechanicalFeesLine;

        var fees = new StringBuilder(48);
        AppendFee(fees, card.HasCommitCost, Label(localizationResolver, "card.commit", "COMMIT", language), card.CommitCost);
        AppendFee(fees, card.HasUploadCost, Label(localizationResolver, "card.upload", "UPLOAD", language), card.UploadCost);
        AppendFee(fees, card.HasDownloadCost, Label(localizationResolver, "card.download", "DOWNLOAD", language), card.DownloadCost);
        return fees.ToString();
    }

    private static string BuildDiagnosticPunishAndCostLine(
        RuntimeCardDisplayModel card,
        RuntimeLocalizationResolver? localizationResolver,
        string language)
    {
        if (localizationResolver is null) return card.LegacyPunishAndCostLine;
        var punishFallback = card.DefinitionAvailable ? "—" : RuntimeCardDisplayModel.Unavailable;
        var costFallback = card.DefinitionAvailable ? "—" : RuntimeCardDisplayModel.Unavailable;
        return Label(localizationResolver, "card.printedPunish", "PRINTED PUNISH", language) +
            " " + DisplayNumber(card.PrintedPunish, punishFallback) +
            " | COST " + DisplayNumber(card.DeclaredCost, costFallback);
    }

    private static string BuildDiagnosticMechanicalFeesLine(
        RuntimeCardDisplayModel card,
        RuntimeLocalizationResolver? localizationResolver,
        string language)
    {
        if (localizationResolver is null) return card.DiagnosticMechanicalFeesLine;
        return Label(localizationResolver, "card.commit", "COMMIT", language) +
            " " + DisplayNumber(card.CommitCost) +
            " | " + Label(localizationResolver, "card.upload", "UPLOAD", language) +
            " " + DisplayNumber(card.UploadCost) +
            " | " + Label(localizationResolver, "card.download", "DOWNLOAD", language) +
            " " + DisplayNumber(card.DownloadCost);
    }

    private static string BuildChantLine(
        RuntimeCardDisplayModel card,
        RuntimeLocalizationResolver? localizationResolver,
        string language)
    {
        if (localizationResolver is null) return card.ChantLine;
        if (card.Chant <= 0 && !card.ChantRemaining.HasValue) return string.Empty;

        var chantLabel = Label(localizationResolver, "card.chant", "吟唱", language);
        var remainingLabel = Label(localizationResolver, "card.remaining", "剩余", language);
        if (card.Chant > 0 && card.ChantRemaining.HasValue)
        {
            return chantLabel + " " + DisplayNumber(card.Chant) +
                " · " + remainingLabel + " " + DisplayNumber(card.ChantRemaining);
        }

        return card.Chant > 0
            ? chantLabel + " " + DisplayNumber(card.Chant)
            : chantLabel + remainingLabel + " " + DisplayNumber(card.ChantRemaining);
    }

    private static string BuildLandmarkProgressLine(
        RuntimeCardDisplayModel card,
        RuntimeLocalizationResolver? localizationResolver,
        string language)
    {
        if (localizationResolver is null) return card.LandmarkProgressLine;
        return card.LandmarkPullCount.HasValue
            ? Label(localizationResolver, "card.landmarkTier", "LANDMARK TIER", language) +
                " " + DisplayNumber(card.LandmarkPullCount)
            : string.Empty;
    }

    private static string BuildListLine(
        System.Collections.Generic.IReadOnlyList<string> values,
        bool dataAvailable,
        string localizationKey,
        string fallbackLabel,
        RuntimeLocalizationResolver? localizationResolver,
        string language)
    {
        if (localizationResolver is null)
            return FormatList(fallbackLabel, values, dataAvailable);

        var label = Label(localizationResolver, localizationKey, fallbackLabel, language);
        if (!dataAvailable) return label + " " + RuntimeCardDisplayModel.Unavailable;
        if (values is null) return label + " " + RuntimeCardDisplayModel.Unavailable;
        if (values.Count == 0) return label + " —";

        var builder = new StringBuilder(label.Length + 2 + values.Count * 8);
        builder.Append(label).Append(' ');
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0) builder.Append(" · ");
            builder.Append(values[index]);
        }
        return builder.ToString();
    }

    private static void AppendFee(StringBuilder builder, bool present, string label, int? value)
    {
        if (!present) return;
        if (builder.Length > 0) builder.Append(" | ");
        builder.Append(label).Append(' ').Append(DisplayNumber(value));
    }

    private static string Label(
        RuntimeLocalizationResolver? localizationResolver,
        string localizationKey,
        string fallback,
        string language)
    {
        return localizationResolver is null
            ? fallback
            : localizationResolver.Get(localizationKey, language);
    }

    private static string FormatList(
        string label,
        System.Collections.Generic.IReadOnlyList<string> values,
        bool dataAvailable)
    {
        if (!dataAvailable) return label + " " + RuntimeCardDisplayModel.Unavailable;
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

    private static string BuildLeaderGoalLine(string leaderWinText, string label = "目标")
    {
        return string.IsNullOrWhiteSpace(leaderWinText)
            ? string.Empty
            : label + " " + leaderWinText;
    }

    private static string Display(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? RuntimeCardDisplayModel.Unavailable
            : value;
    }
}
}
