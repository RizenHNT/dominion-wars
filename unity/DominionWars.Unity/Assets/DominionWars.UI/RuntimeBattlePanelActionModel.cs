#nullable enable annotations

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DominionWars.Adapters;

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
    /// <summary>
    /// Builds presentation groups without adding or removing any advertised
    /// action. Grouping is only a convenience for choosing among exact action
    /// variants; the selected entry remains the submission payload.
    /// </summary>
    public static IReadOnlyList<RuntimeBattlePanelActionGroup> BuildActionGroups(
        RuntimeSnapshotEnvelope snapshot)
    {
        if (snapshot is null || snapshot.LegalActions is null)
            return Array.Empty<RuntimeBattlePanelActionGroup>();

        var groups = new List<RuntimeBattlePanelActionGroup>();
        var groupedEntries = new List<List<RuntimeBattlePanelActionEntry>>();
        var keys = new List<string>();
        foreach (var legal in snapshot.LegalActions)
        {
            if (legal is null) continue;
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

    /// <summary>Creates a display label from wire fields only.</summary>
    public static string Describe(
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
    /// Compares wire values without assuming whether a numeric id arrived as
    /// an integer, string, or JSON parser value.
    /// </summary>
    internal static bool WireValuesEqual(object? left, object? right)
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
