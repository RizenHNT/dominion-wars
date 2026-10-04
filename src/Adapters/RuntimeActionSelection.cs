using System;
using System.Collections;
using System.Collections.Generic;

namespace DominionWars.Adapters
{

/// <summary>
/// Reads and validates the explicit card-selection channel that accompanies a
/// complete advertised action. The selected ids deliberately live outside the
/// advertised payload: the payload remains an immutable, field-for-field copy
/// of the engine advertisement while the player/AI supplies only the choice.
/// </summary>
public static class RuntimeActionSelection
{
    public const string SelectionRequiredReason = "action.selection_required";
    public const string SelectionCountMismatchReason = "action.selection_count_mismatch";
    public const string SelectionDuplicateReason = "action.selection_duplicate";
    public const string SelectionNotCandidateReason = "action.selection_not_candidate";
    public const string SelectionInvalidReason = "action.selection_invalid";
    public const string SelectionNotAllowedReason = "action.selection_not_allowed";
    public const string SelectionSpecInvalidReason = "action.selection_spec_invalid";

    /// <summary>
    /// Gets the selection contract advertised by an action. A null spec means
    /// that the action has no explicit selected-id channel. A malformed or
    /// mixed selection payload is rejected rather than interpreted loosely.
    /// </summary>
    public static bool TryGetSpec(
        RuntimeLegalAction advertised,
        out RuntimeActionSelectionSpec? spec,
        out string reasonKey)
    {
        spec = null;
        reasonKey = string.Empty;
        if (advertised is null)
        {
            reasonKey = SelectionSpecInvalidReason;
            return false;
        }

        var payload = advertised.Payload;
        if (payload is null)
        {
            reasonKey = SelectionSpecInvalidReason;
            return false;
        }

        var hasDiscardRequired = payload.ContainsKey("discardRequired");
        var hasDiscardCandidates = payload.ContainsKey("discardCandidateIds");
        var hasRequiredCount = payload.ContainsKey("requiredCount");
        var hasCandidates = payload.ContainsKey("candidateIds");
        var hasAny = hasDiscardRequired || hasDiscardCandidates || hasRequiredCount || hasCandidates;
        if (!hasAny) return true;

        // `discardCandidateIds` belongs to the self-discard shape when it is
        // paired with `discardRequired`; a few legacy DISCARD fixtures pair
        // the same candidate-key spelling with `requiredCount`. Distinguish
        // those cases before classifying the shape so the compatibility alias
        // remains reachable without accepting a mixed/ambiguous payload.
        if (hasDiscardRequired && hasRequiredCount)
        {
            reasonKey = SelectionSpecInvalidReason;
            return false;
        }

        string requiredKey;
        string candidateKey;
        if (hasDiscardRequired || (hasDiscardCandidates && !hasRequiredCount && !hasCandidates))
        {
            if (!hasDiscardRequired || !hasDiscardCandidates ||
                hasCandidates ||
                !IsSelfDiscardAction(advertised.Type))
            {
                reasonKey = SelectionSpecInvalidReason;
                return false;
            }

            requiredKey = "discardRequired";
            candidateKey = "discardCandidateIds";
        }
        else
        {
            // `discardCandidateIds` was used by a few pre-1.31 presentation
            // fixtures alongside `requiredCount`; accept that legacy spelling
            // only for the DISCARD action and never mix both candidate keys.
            if (!hasRequiredCount || !IsDiscardAction(advertised.Type) ||
                (hasCandidates == hasDiscardCandidates))
            {
                reasonKey = SelectionSpecInvalidReason;
                return false;
            }

            requiredKey = "requiredCount";
            candidateKey = hasCandidates ? "candidateIds" : "discardCandidateIds";
        }

        if (!RuntimeWireValue.TryGetInt64(payload[requiredKey]!, out var required) ||
            required < 0 || required > int.MaxValue ||
            !TryReadCandidateIds(payload[candidateKey], out var candidateIds))
        {
            reasonKey = SelectionSpecInvalidReason;
            return false;
        }

        if (required > candidateIds.Count)
        {
            reasonKey = SelectionSpecInvalidReason;
            return false;
        }

        spec = new RuntimeActionSelectionSpec((int)required, candidateIds);
        return true;
    }

    /// <summary>
    /// Validates the separate selection channel. When <paramref name="requireSelection"/>
    /// is false (the action-boundary preflight), an absent selection remains
    /// acceptable so legacy action-copy helpers can construct the immutable
    /// advertised portion first. The submission gateway calls this with true,
    /// which makes a required selection fail closed.
    /// </summary>
    public static bool TryValidate(
        RuntimeLegalAction advertised,
        IReadOnlyList<long>? selectedEntityIds,
        bool requireSelection,
        out string reasonKey)
    {
        reasonKey = string.Empty;
        if (!TryGetSpec(advertised, out var spec, out reasonKey))
            return false;

        var selected = selectedEntityIds ?? Array.Empty<long>();
        if (spec is null)
        {
            if (selected.Count > 0)
            {
                reasonKey = SelectionNotAllowedReason;
                return false;
            }

            return true;
        }

        if (selected.Count == 0 && spec.RequiredCount > 0 && !requireSelection)
            return true;
        if (selected.Count != spec.RequiredCount)
        {
            reasonKey = selected.Count == 0 && spec.RequiredCount > 0
                ? SelectionRequiredReason
                : SelectionCountMismatchReason;
            return false;
        }

        var seen = new HashSet<long>();
        foreach (var id in selected)
        {
            if (id <= 0)
            {
                reasonKey = SelectionInvalidReason;
                return false;
            }

            if (!seen.Add(id))
            {
                reasonKey = SelectionDuplicateReason;
                return false;
            }

            // An EXPLICIT loop rather than `Contains`: with `using System` in scope, a
            // membership call can bind to `MemoryExtensions.Contains` through an implicit
            // span conversion, which does not compile on netstandard2.1 (CS7036), and
            // `IReadOnlyList<long>` has no `IndexOf`. This also keeps the comparison on
            // `long` rather than on any string form of the entity id.
            var isCandidate = false;
            for (var index = 0; index < spec.CandidateIds.Count; index++)
            {
                if (spec.CandidateIds[index] == id)
                {
                    isCandidate = true;
                    break;
                }
            }

            if (!isCandidate)
            {
                reasonKey = SelectionNotCandidateReason;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds the deterministic CPU choice from the engine's own candidate
    /// order. This is an explicit AI selection, not a gateway fallback: the
    /// gateway never calls this method for a human or incomplete action.
    /// </summary>
    public static bool TryBuildStableSelection(
        RuntimeLegalAction advertised,
        out IReadOnlyList<long> selectedEntityIds,
        out string reasonKey)
    {
        selectedEntityIds = Array.Empty<long>();
        if (!TryGetSpec(advertised, out var spec, out reasonKey))
            return false;
        if (spec is null) return true;

        var selected = new List<long>(spec.RequiredCount);
        for (var index = 0; index < spec.RequiredCount; index++)
            selected.Add(spec.CandidateIds[index]);

        selectedEntityIds = selected.AsReadOnly();
        return TryValidate(advertised, selectedEntityIds, true, out reasonKey);
    }

    private static bool TryReadCandidateIds(
        object? raw,
        out IReadOnlyList<long> candidateIds)
    {
        candidateIds = Array.Empty<long>();
        if (raw is null || raw is string || !RuntimeWireValue.TryEnumerate(raw, out var values))
            return false;

        var result = new List<long>();
        var seen = new HashSet<long>();
        foreach (var value in values)
        {
            if (!RuntimeWireValue.TryGetInt64(value, out var id) ||
                id <= 0 || !seen.Add(id))
                return false;
            result.Add(id);
        }

        candidateIds = result.AsReadOnly();
        return true;
    }

    private static bool IsSelfDiscardAction(string? actionType)
    {
        return string.Equals(actionType, "PLAY_CARD", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actionType, "SET_AMBUSH", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDiscardAction(string? actionType)
    {
        return string.Equals(actionType, "DISCARD", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class RuntimeActionSelectionSpec
{
    internal RuntimeActionSelectionSpec(
        int requiredCount,
        IReadOnlyList<long> candidateIds)
    {
        RequiredCount = requiredCount;
        CandidateIds = candidateIds ?? throw new ArgumentNullException(nameof(candidateIds));
    }

    public int RequiredCount { get; }
    public IReadOnlyList<long> CandidateIds { get; }
}
}
