#nullable enable annotations

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DominionWars.Adapters;
using DominionWars.Data;
using DominionWars.Unity.Runtime;

namespace DominionWars.Unity.UI
{

/// <summary>
/// Neutral runtime presentation defaults. The Unity 6 legacy built-in font is
/// deliberately named here so the temporary uGUI placeholder cannot silently
/// regress to the unavailable Arial resource.
/// </summary>
public static class RuntimeBattlePanelDefaults
{
    public const string LegacyBuiltinFontResource = "LegacyRuntime.ttf";
}

/// <summary>
/// Presentation-only action state. The panel may disable an action when the
/// wire payload explicitly says that a target/selection is required but the
/// corresponding value is absent. It never derives legality from card rules.
/// </summary>
public sealed class RuntimeBattlePanelActionState
{
    internal RuntimeBattlePanelActionState(bool interactable, string reasonKey)
    {
        Interactable = interactable;
        ReasonKey = reasonKey ?? string.Empty;
    }

    public bool Interactable { get; }
    public string ReasonKey { get; }
}

/// <summary>
/// One source/target value advertised by one complete LegalAction. The UI may
/// use this value to offer a choice, but it must keep the associated action
/// intact when submitting. It never manufactures a new action from a value.
/// </summary>
public sealed class RuntimeBattlePanelActionOption
{
    internal RuntimeBattlePanelActionOption(
        RuntimeLegalAction action,
        object? value,
        bool source)
    {
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Value = value;
        IsSource = source;
    }

    public RuntimeLegalAction Action { get; }
    public object? Value { get; }
    public bool IsSource { get; }
    public string ActionId => Action.ActionId;
    public string Label => RuntimeBattlePanelActionModel.FormatWireValue(Value);
}

/// <summary>
/// Presentation entry for one advertised action. A View can use this as the
/// complete button/selection state without looking at engine rules.
/// </summary>
public sealed class RuntimeBattlePanelActionEntry
{
    internal RuntimeBattlePanelActionEntry(
        RuntimeLegalAction legalAction,
        RuntimeBattlePanelActionState state)
    {
        LegalAction = legalAction ?? throw new ArgumentNullException(nameof(legalAction));
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public RuntimeLegalAction LegalAction { get; }
    public RuntimeBattlePanelActionState State { get; }
    public object? SourceId => LegalAction.SourceId;
    public object? TargetId => LegalAction.TargetId;
    public bool IsSelected { get; internal set; }
    public string Label => RuntimeBattlePanelActionModel.Describe(LegalAction, State);
}

/// <summary>
/// A UI-only choice set. Every member is an action advertised in the same
/// snapshot; selecting an option only selects that existing action identity.
/// </summary>
public sealed class RuntimeBattlePanelActionGroup
{
    private readonly List<RuntimeBattlePanelActionEntry> _actions;
    private readonly List<RuntimeBattlePanelActionOption> _sourceOptions;
    private readonly List<RuntimeBattlePanelActionOption> _targetOptions;
    private RuntimeBattlePanelActionEntry _selectedAction;

    internal RuntimeBattlePanelActionGroup(
        string groupKey,
        List<RuntimeBattlePanelActionEntry> actions)
    {
        if (string.IsNullOrWhiteSpace(groupKey))
            throw new ArgumentException("An action group key is required.", nameof(groupKey));
        if (actions is null || actions.Count == 0)
            throw new ArgumentException("An action group must contain an action.", nameof(actions));

        GroupKey = groupKey;
        _actions = actions;
        _sourceOptions = BuildOptions(actions, true);
        _targetOptions = BuildOptions(actions, false);
        _selectedAction = FirstSelectable(actions) ?? actions[0];
        MarkSelected(_selectedAction);
    }

    public string GroupKey { get; }
    public IReadOnlyList<RuntimeBattlePanelActionEntry> Actions => _actions.AsReadOnly();
    public IReadOnlyList<RuntimeBattlePanelActionOption> SourceOptions => _sourceOptions.AsReadOnly();
    public IReadOnlyList<RuntimeBattlePanelActionOption> TargetOptions => _targetOptions.AsReadOnly();
    public RuntimeBattlePanelActionEntry SelectedAction => _selectedAction;
    public bool RequiresSelection => _actions.Count > 1;
    public bool RequiresSourceSelection => _sourceOptions.Count > 1;
    public bool RequiresTargetSelection => _targetOptions.Count > 1;

    /// <summary>
    /// Selects an already advertised action by its stable action id.
    /// Disabled entries cannot become submit candidates.
    /// </summary>
    public bool TrySelectAction(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return false;
        foreach (var action in _actions)
        {
            if (!string.Equals(action.LegalAction.ActionId, actionId, StringComparison.Ordinal))
                continue;
            if (!action.State.Interactable) return false;
            MarkSelected(action);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Selects the advertised action carrying the requested source. If more
    /// than one action has that source, the current target is preferred.
    /// </summary>
    public bool TrySelectSource(object? sourceId)
    {
        return TrySelectValue(sourceId, true);
    }

    /// <summary>See <see cref="TrySelectSource"/> for the safety boundary.</summary>
    public bool TrySelectTarget(object? targetId)
    {
        return TrySelectValue(targetId, false);
    }

    private bool TrySelectValue(object? value, bool source)
    {
        RuntimeBattlePanelActionEntry? fallback = null;
        foreach (var action in _actions)
        {
            var candidate = source ? action.SourceId : action.TargetId;
            if (!RuntimeBattlePanelActionModel.WireValuesEqual(candidate, value)) continue;
            if (!action.State.Interactable) continue;
            if (fallback is null) fallback = action;
            if (RuntimeBattlePanelActionModel.WireValuesEqual(
                source ? action.TargetId : action.SourceId,
                source ? _selectedAction.TargetId : _selectedAction.SourceId))
            {
                MarkSelected(action);
                return true;
            }
        }

        if (fallback is null) return false;
        MarkSelected(fallback);
        return true;
    }

    private void MarkSelected(RuntimeBattlePanelActionEntry selected)
    {
        _selectedAction = selected;
        foreach (var action in _actions)
            action.IsSelected = ReferenceEquals(action, selected);
    }

    private static RuntimeBattlePanelActionEntry? FirstSelectable(
        IReadOnlyList<RuntimeBattlePanelActionEntry> actions)
    {
        foreach (var action in actions)
        {
            if (action.State.Interactable) return action;
        }
        return null;
    }

    private static List<RuntimeBattlePanelActionOption> BuildOptions(
        IReadOnlyList<RuntimeBattlePanelActionEntry> actions,
        bool source)
    {
        var options = new List<RuntimeBattlePanelActionOption>();
        foreach (var action in actions)
        {
            var value = source ? action.SourceId : action.TargetId;
            if (value is null) continue;

            var duplicate = false;
            foreach (var existing in options)
            {
                if (RuntimeBattlePanelActionModel.WireValuesEqual(existing.Value, value))
                {
                    duplicate = true;
                    break;
                }
            }
            if (!duplicate) options.Add(new RuntimeBattlePanelActionOption(action.LegalAction, value, source));
        }
        return options;
    }
}

public static class RuntimeBattlePanelActionModel
{
    private static readonly RuntimeLocalizationResolver DefaultLocalizationResolver =
        new RuntimeLocalizationResolver();

    /// <summary>
    /// Builds presentation groups without adding or removing any advertised
    /// action. Grouping is only a convenience for choosing among exact action
    /// variants; the selected entry remains the submission payload.
    /// </summary>
    public static IReadOnlyList<RuntimeBattlePanelActionGroup> BuildActionGroups(
        RuntimeSnapshotEnvelope snapshot)
    {
        return BuildActionGroups(snapshot, null);
    }

    /// <summary>
    /// Builds presentation groups for the current card selection. A selected
    /// card may expose only the complete legal actions whose advertised
    /// <see cref="RuntimeLegalAction.SourceId"/> is that card's entity id.
    /// Actions without a source or card identity are phase-level actions and
    /// remain visible (for example SKIP_AMBUSH and END_TURN). This is a
    /// presentation filter only: it never creates, rewrites, or re-evaluates
    /// engine legality.
    /// </summary>
    public static IReadOnlyList<RuntimeBattlePanelActionGroup> BuildActionGroups(
        RuntimeSnapshotEnvelope snapshot,
        long? selectedCardEntityId)
    {
        if (snapshot is null || snapshot.LegalActions is null)
            return Array.Empty<RuntimeBattlePanelActionGroup>();

        var groups = new List<RuntimeBattlePanelActionGroup>();
        var groupedEntries = new List<List<RuntimeBattlePanelActionEntry>>();
        var keys = new List<string>();
        foreach (var legal in snapshot.LegalActions)
        {
            if (legal is null) continue;
            if (selectedCardEntityId.HasValue &&
                !IsVisibleForSelectedCard(legal, selectedCardEntityId.Value))
                continue;
            var entry = new RuntimeBattlePanelActionEntry(
                legal,
                Evaluate(legal, snapshot.PendingPrompt));
            var key = GroupKey(legal);
            var groupIndex = keys.IndexOf(key);
            if (groupIndex < 0)
            {
                keys.Add(key);
                groupedEntries.Add(new List<RuntimeBattlePanelActionEntry> { entry });
            }
            else
            {
                groupedEntries[groupIndex].Add(entry);
            }
        }

        for (var index = 0; index < groupedEntries.Count; index++)
        {
            groups.Add(new RuntimeBattlePanelActionGroup(keys[index], groupedEntries[index]));
        }
        return groups.AsReadOnly();
    }

    /// <summary>
    /// Returns true when an advertised action belongs to the selected card or
    /// is explicitly a phase/global action. A non-null CardId without a
    /// SourceId is not treated as global: it cannot be safely tied to one
    /// entity, so it is hidden while a card is selected rather than guessed.
    /// </summary>
    public static bool IsVisibleForSelectedCard(
        RuntimeLegalAction legal,
        long selectedCardEntityId)
    {
        if (legal is null) return false;
        if (legal.SourceId is null)
            return string.IsNullOrWhiteSpace(legal.CardId);

        return WireValuesEqual(legal.SourceId, selectedCardEntityId);
    }

    public static RuntimeBattlePanelActionState Evaluate(
        RuntimeLegalAction action,
        object? pendingPrompt = null)
    {
        if (action is null)
            return new RuntimeBattlePanelActionState(false, "action.missing");

        // LegalAction.ReasonKey is an engine-owned localization/description
        // key (for example action.skip_ambush), not an enabled flag. Only
        // explicit target/selection requirements below may disable a
        // correctly advertised action at this presentation boundary.
        var payload = action.Payload;
        if (payload is null)
            return new RuntimeBattlePanelActionState(false, "action.payload_missing");

        if (IsExplicitlyRequired(payload, "requiresTarget") ||
            IsExplicitlyRequired(payload, "targetRequired"))
        {
            if (action.TargetId is null)
                return new RuntimeBattlePanelActionState(false, "action.target_required");
        }

        if (IsExplicitlyRequired(payload, "requiresSelection") ||
            IsExplicitlyRequired(payload, "selectionRequired"))
        {
            if (!HasNonEmptySelection(payload))
                return new RuntimeBattlePanelActionState(false, "action.selection_required");
        }

        // A prompt alone is not enough to infer which legal action needs the
        // choice. The engine/contract must mark the action explicitly.
        _ = pendingPrompt;
        return new RuntimeBattlePanelActionState(true, string.Empty);
    }

    /// <summary>
    /// Creates the player-facing label for one already-advertised action.
    ///
    /// This method deliberately does not expose action ids, entity ids, or wire
    /// field names. Those values remain in <see cref="DescribeTechnical"/>,
    /// which is intended for an opt-in diagnostic surface only. The optional
    /// catalog/snapshot arguments enrich the same label with an authored card
    /// name and a visible target name; they never create or alter an action.
    /// </summary>
    public static string Describe(
        RuntimeLegalAction legal,
        RuntimeBattlePanelActionState state)
    {
        return Describe(
            legal,
            state,
            null,
            null,
            null,
            DefaultLocalizationResolver,
            "en");
    }

    public static string Describe(
        RuntimeLegalAction legal,
        RuntimeBattlePanelActionState state,
        CardCatalog? cardCatalog,
        RuntimeSnapshotEnvelope? snapshot,
        int? viewerPlayerIndex)
    {
        return Describe(
            legal,
            state,
            cardCatalog,
            snapshot,
            viewerPlayerIndex,
            DefaultLocalizationResolver,
            "en");
    }

    /// <summary>
    /// Creates a player-facing action label with an explicit localization
    /// source. The resolver is presentation-only; it receives only the
    /// advertised action token and cannot create or alter the action.
    /// </summary>
    public static string Describe(
        RuntimeLegalAction legal,
        RuntimeBattlePanelActionState state,
        RuntimeLocalizationResolver localizationResolver,
        string language = "en")
    {
        if (localizationResolver is null)
            throw new ArgumentNullException(nameof(localizationResolver));

        return Describe(
            legal,
            state,
            null,
            null,
            null,
            localizationResolver,
            language);
    }

    /// <summary>
    /// Creates a player-facing action label with card/visibility context and
    /// an explicit localization source. The existing five-argument overload
    /// remains available for callers that use the default resolver and
    /// English language.
    /// </summary>
    public static string Describe(
        RuntimeLegalAction legal,
        RuntimeBattlePanelActionState state,
        CardCatalog? cardCatalog,
        RuntimeSnapshotEnvelope? snapshot,
        int? viewerPlayerIndex,
        RuntimeLocalizationResolver? localizationResolver)
    {
        return Describe(
            legal,
            state,
            cardCatalog,
            snapshot,
            viewerPlayerIndex,
            localizationResolver ?? DefaultLocalizationResolver,
            "en");
    }

    public static string Describe(
        RuntimeLegalAction legal,
        RuntimeBattlePanelActionState state,
        CardCatalog? cardCatalog,
        RuntimeSnapshotEnvelope? snapshot,
        int? viewerPlayerIndex,
        RuntimeLocalizationResolver localizationResolver,
        string language)
    {
        if (legal is null) return "Unavailable action";
        if (state is null) throw new ArgumentNullException(nameof(state));
        if (localizationResolver is null)
            throw new ArgumentNullException(nameof(localizationResolver));

        var actionType = FriendlyActionType(legal.Type, localizationResolver, language);
        var cardName = ResolveCardName(legal.CardId, cardCatalog);
        if (string.IsNullOrWhiteSpace(cardName) && IsCardAction(legal.Type))
            cardName = ResolveVisibleEntityName(legal.SourceId, snapshot, cardCatalog);

        var builder = new StringBuilder(actionType);
        if (!string.IsNullOrWhiteSpace(cardName)) builder.Append(' ').Append(cardName);

        var targetName = DescribeTarget(
            legal.TargetId,
            snapshot,
            cardCatalog,
            viewerPlayerIndex);
        if (!string.IsNullOrWhiteSpace(targetName))
            builder.Append(" → ").Append(targetName);

        if (!state.Interactable) builder.Append(" — Unavailable");
        return builder.ToString();
    }

    /// <summary>
    /// Returns the diagnostic form of an action label. This is kept separate
    /// from <see cref="Describe"/> so a normal button cannot accidentally leak
    /// protocol identifiers while an opt-in debug overlay can still inspect
    /// the exact advertised payload.
    /// </summary>
    public static string DescribeTechnical(
        RuntimeLegalAction legal,
        RuntimeBattlePanelActionState state)
    {
        if (legal is null) return "Unavailable action";
        if (state is null) throw new ArgumentNullException(nameof(state));

        var builder = new StringBuilder(
            string.IsNullOrWhiteSpace(legal.Type) ? "UNKNOWN" : legal.Type);
        if (!string.IsNullOrWhiteSpace(legal.CardId)) builder.Append(" ").Append(legal.CardId);
        if (!string.IsNullOrWhiteSpace(legal.ActionId)) builder.Append(" [").Append(legal.ActionId).Append("]");
        if (legal.SourceId is not null) builder.Append(" source=").Append(FormatWireValue(legal.SourceId));
        if (legal.TargetId is not null) builder.Append(" target=").Append(FormatWireValue(legal.TargetId));
        if (!state.Interactable) builder.Append(" — ").Append(state.ReasonKey);
        return builder.ToString();
    }

    /// <summary>
    /// Converts an advertised target into a short player-facing semantic. A
    /// missing/unknown target is intentionally rendered as a generic word; its
    /// stable wire value remains available through <see cref="DescribeTechnical"/>.
    /// </summary>
    public static string? DescribeTarget(
        object? targetId,
        RuntimeSnapshotEnvelope? snapshot = null,
        CardCatalog? cardCatalog = null,
        int? viewerPlayerIndex = null)
    {
        if (targetId is null) return null;

        var visibleCardName = ResolveVisibleEntityName(targetId, snapshot, cardCatalog);
        if (!string.IsNullOrWhiteSpace(visibleCardName)) return visibleCardName;

        var wire = FormatWireValue(targetId);
        if (string.IsNullOrWhiteSpace(wire) || wire == "—") return null;
        if (IsCastleTarget(wire)) return "Castle";

        if (TryExtractPlayerTarget(wire, out var playerId, out var scope))
        {
            var viewerId = snapshot?.ViewerPlayerId;
            if (string.IsNullOrWhiteSpace(viewerId) && viewerPlayerIndex.HasValue)
                viewerId = "player_" + viewerPlayerIndex.Value.ToString(CultureInfo.InvariantCulture);
            var own = !string.IsNullOrWhiteSpace(viewerId) &&
                string.Equals(viewerId, playerId, StringComparison.Ordinal);
            var side = own ? "Your" : "Opponent";
            if (scope == PlayerTargetScope.Life) return side + " Life";
            if (scope == PlayerTargetScope.Leader) return side + " Leader";
            return side + " Side";
        }

        var lower = wire.ToLowerInvariant();
        if (lower.Contains("cloud")) return "Cloud";
        if (lower.Contains("commit") || lower.Contains("queue")) return "Commit Queue";
        if (lower.Contains("grave")) return "Graveyard";
        if (lower.Contains("exile")) return "Exile";
        if (lower.Contains("hand")) return "Hand";
        if (lower.Contains("field")) return "Field";
        if (lower.Contains("enemy") || lower.Contains("opponent")) return "Opponent";
        if (lower.Contains("friendly") || lower.Contains("ally")) return "Your Side";
        if (lower.Contains("face")) return "Opponent";
        return "Target";
    }

    private enum PlayerTargetScope
    {
        Side,
        Life,
        Leader,
    }

    private static string FriendlyActionType(
        string? type,
        RuntimeLocalizationResolver localizationResolver,
        string language)
    {
        var normalized = type?.Trim().ToUpperInvariant() ?? string.Empty;
        switch (normalized)
        {
            // These are the only action tokens in this slice with approved
            // semantic keys in RuntimeLocalizationResolver.
            case "PLAY_CARD":
            case "ATTACK":
            case "END_TURN":
            case "SKIP_AMBUSH":
                return localizationResolver.ResolveSemantic(
                    RuntimeSemanticKind.Action,
                    normalized,
                    language).Text;

            // Keep the existing authored wording for action types whose
            // semantic keys are not part of this localization slice.
            case "SET_AMBUSH": return "Set Ambush";
            case "COMMIT": return "Commit";
            case "PUSH": return "Push";
            case "PULL": return "Pull";
            case "ROLLBACK": return "Rollback";
            case "DISCARD": return "Discard";
            case "CHOOSE_TARGET": return "Choose Target";
            default:
                // Unknown protocol values use the resolver's generic safe
                // fallback. Never turn an opaque token into display text.
                return localizationResolver.ResolveSemantic(
                    RuntimeSemanticKind.Action,
                    type ?? string.Empty,
                    language).Text;
        }
    }

    private static bool IsCardAction(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return false;
        switch (type.Trim().ToUpperInvariant())
        {
            case "PLAY_CARD":
            case "ATTACK":
            case "SET_AMBUSH":
            case "COMMIT":
            case "PUSH":
            case "PULL":
            case "ROLLBACK":
                return true;
            default:
                return false;
        }
    }

    private static string? ResolveCardName(string? cardId, CardCatalog? cardCatalog)
    {
        if (string.IsNullOrWhiteSpace(cardId)) return null;
        if (cardCatalog == null ||
            !cardCatalog.TryGetPresentationMetadata(cardId, out var metadata) ||
            metadata == null ||
            string.IsNullOrWhiteSpace(metadata.Name))
        {
            // A card id without presentation metadata is not player-facing
            // content. Keep the label generic instead of exposing or
            // humanizing the opaque id.
            return null;
        }

        return metadata.Name;
    }

    private static string? ResolveVisibleEntityName(
        object? entityId,
        RuntimeSnapshotEnvelope? snapshot,
        CardCatalog? cardCatalog)
    {
        if (snapshot?.Players == null || !TryGetLongId(entityId, out var numericId)) return null;
        for (var playerIndex = 0; playerIndex < snapshot.Players.Count; playerIndex++)
        {
            var player = snapshot.Players[playerIndex];
            if (player == null) continue;
            var name = FindCardName(player.Hand, numericId, cardCatalog) ??
                FindCardName(player.Ambush, numericId, cardCatalog) ??
                FindCardName(player.Field, numericId, cardCatalog) ??
                FindCardName(player.LeaderZone, numericId, cardCatalog) ??
                FindCardName(player.CommitQueue, numericId, cardCatalog) ??
                FindCardName(player.CloudStack, numericId, cardCatalog) ??
                FindCardName(player.Graveyard, numericId, cardCatalog);
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        return null;
    }

    private static string? FindCardName(
        IReadOnlyList<RuntimeCardSnapshot>? cards,
        long entityId,
        CardCatalog? cardCatalog)
    {
        if (cards == null) return null;
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            if (card == null || card.EntityId != entityId) continue;
            return ResolveCardName(card.CardId, cardCatalog) ?? "Card";
        }
        return null;
    }

    private static bool TryGetLongId(object? value, out long numericId)
    {
        numericId = 0;
        if (value is null || value is bool) return false;
        if (value is string text)
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out numericId);
        if (value is IConvertible convertible)
        {
            try
            {
                numericId = convertible.ToInt64(CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception exception) when (
                exception is FormatException ||
                exception is InvalidCastException ||
                exception is OverflowException)
            {
                // Non-numeric wire values are semantic strings, not entity ids.
            }
        }
        return false;
    }

    private static bool IsCastleTarget(string wire)
    {
        return string.Equals(wire, "castle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(wire, "shared_castle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(wire, "core:shared_castle", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractPlayerTarget(
        string wire,
        out string playerId,
        out PlayerTargetScope scope)
    {
        playerId = string.Empty;
        scope = PlayerTargetScope.Side;
        if (wire.StartsWith("player_", StringComparison.OrdinalIgnoreCase))
        {
            playerId = wire;
            return true;
        }

        const string corePrefix = "core:player_";
        if (!wire.StartsWith(corePrefix, StringComparison.OrdinalIgnoreCase)) return false;
        var remainder = wire.Substring("core:".Length);
        if (remainder.EndsWith(":life", StringComparison.OrdinalIgnoreCase))
        {
            playerId = remainder.Substring(0, remainder.Length - ":life".Length);
            scope = PlayerTargetScope.Life;
            return playerId.Length > 0;
        }
        if (remainder.EndsWith(":leader", StringComparison.OrdinalIgnoreCase))
        {
            playerId = remainder.Substring(0, remainder.Length - ":leader".Length);
            scope = PlayerTargetScope.Leader;
            return playerId.Length > 0;
        }
        playerId = remainder;
        return playerId.Length > 0;
    }

    /// <summary>
    /// Compares wire values without assuming whether a numeric id arrived as
    /// an integer, string, or JSON parser value.
    /// </summary>
    public static bool WireValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (Equals(NormalizeWireValue(left), NormalizeWireValue(right))) return true;

        if (left is IReadOnlyDictionary<string, object?> leftMap &&
            right is IReadOnlyDictionary<string, object?> rightMap)
        {
            if (leftMap.Count != rightMap.Count) return false;
            foreach (var entry in leftMap)
            {
                if (!rightMap.TryGetValue(entry.Key, out var value) ||
                    !WireValuesEqual(entry.Value, value)) return false;
            }
            return true;
        }

        if (left is IEnumerable leftItems && right is IEnumerable rightItems &&
            left is not string && right is not string)
        {
            var a = leftItems.GetEnumerator();
            var b = rightItems.GetEnumerator();
            try
            {
                while (true)
                {
                    var hasA = a.MoveNext();
                    var hasB = b.MoveNext();
                    if (hasA != hasB) return false;
                    if (!hasA) return true;
                    if (!WireValuesEqual(a.Current, b.Current)) return false;
                }
            }
            finally
            {
                (a as IDisposable)?.Dispose();
                (b as IDisposable)?.Dispose();
            }
        }
        return false;
    }

    internal static string FormatWireValue(object? value)
    {
        if (value is null) return "—";
        if (value is string text) return text;
        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        return value.ToString() ?? string.Empty;
    }

    private static object? NormalizeWireValue(object value)
    {
        if (value is string text && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            return number;
        if (value is IConvertible convertible)
        {
            try { return convertible.ToInt64(CultureInfo.InvariantCulture); }
            catch { }
        }
        return value;
    }

    private static string GroupKey(RuntimeLegalAction legal)
    {
        return (legal.Type ?? string.Empty) + "\u001f" +
            legal.Actor.ToString(CultureInfo.InvariantCulture) + "\u001f" +
            (legal.CardId ?? string.Empty);
    }

    public static RuntimeGameAction ToGameAction(RuntimeLegalAction legal, string matchId)
    {
        if (legal is null) throw new ArgumentNullException(nameof(legal));
        if (string.IsNullOrWhiteSpace(matchId)) throw new ArgumentException("A match id is required.", nameof(matchId));

        return new RuntimeGameAction
        {
            ContractVersion = legal.ContractVersion,
            MatchId = matchId,
            SnapshotRevision = legal.SnapshotRevision,
            ActionId = legal.ActionId,
            Type = legal.Type,
            Actor = legal.Actor,
            SourceId = legal.SourceId,
            TargetId = legal.TargetId,
            CardId = legal.CardId,
            Payload = legal.Payload,
        };
    }

    private static bool IsExplicitlyRequired(
        IReadOnlyDictionary<string, object?> payload,
        string key)
    {
        return payload.TryGetValue(key, out var value) && value is bool required && required;
    }

    private static bool HasNonEmptySelection(IReadOnlyDictionary<string, object?> payload)
    {
        if (!payload.TryGetValue("selectedEntityIds", out var raw) || raw is null || raw is string)
            return false;
        if (raw is ICollection collection) return collection.Count > 0;
        if (raw is IEnumerable values)
        {
            var enumerator = values.GetEnumerator();
            try { return enumerator.MoveNext(); }
            finally { (enumerator as IDisposable)?.Dispose(); }
        }
        return false;
    }
}
}
