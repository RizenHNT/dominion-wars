using System;
using System.Collections.Generic;
using DominionWars.Adapters;

namespace DominionWars.Unity.Runtime
{

/// <summary>
/// Deterministic baseline policy for a local CPU opponent. The policy only
/// consumes the viewer-safe snapshot and chooses an action already advertised
/// by the authoritative engine. It never receives GameState or derives rules.
/// </summary>
public sealed class RuntimeAiPolicy
{
    public const string ActionPhase = "ACTION";
    public const string AmbushPhase = "AMBUSH";
    public const string DiscardPhase = "DISCARD";
    public const string EndTurnAction = "END_TURN";
    public const string SkipAmbushAction = "SKIP_AMBUSH";

    public bool TryChoose(
        RuntimeSnapshotEnvelope snapshot,
        int aiPlayerIndex,
        bool forceEndTurn,
        out RuntimeLegalAction chosen)
    {
        chosen = null;
        if (snapshot is null || snapshot.LegalActions is null)
            return false;
        if (aiPlayerIndex is < 0 or > 1 || snapshot.CurrentPlayer != aiPlayerIndex)
            return false;
        if (string.Equals(snapshot.Phase, "OVER", StringComparison.Ordinal) ||
            snapshot.WinnerPlayerIndex.HasValue)
            return false;

        var legal = StableActions(snapshot.LegalActions);
        if (legal.Count == 0)
            return false;

        if (forceEndTurn && string.Equals(snapshot.Phase, ActionPhase, StringComparison.Ordinal))
        {
            chosen = FirstType(legal, EndTurnAction);
            if (chosen is not null)
                return true;
        }

        if (string.Equals(snapshot.Phase, AmbushPhase, StringComparison.Ordinal))
        {
            // The baseline opponent keeps ambush choice conservative. The
            // action is still selected from the exact advertised set.
            chosen = FirstType(legal, SkipAmbushAction) ?? legal[0];
            return true;
        }

        if (string.Equals(snapshot.Phase, DiscardPhase, StringComparison.Ordinal))
        {
            // The engine advertises the complete discard action and resolves
            // its required candidate selection at the same boundary.
            chosen = legal[0];
            return true;
        }

        if (string.Equals(snapshot.Phase, ActionPhase, StringComparison.Ordinal))
        {
            chosen = FirstNonType(legal, EndTurnAction) ?? legal[0];
            return true;
        }

        chosen = legal[0];
        return true;
    }

    /// <summary>
    /// Copies one complete advertised action without adding targets,
    /// selections, or rule-derived payload values.
    /// </summary>
    public static RuntimeGameAction ToGameAction(
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction legal)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (legal is null) throw new ArgumentNullException(nameof(legal));
        if (string.IsNullOrWhiteSpace(legal.ActionId))
            throw new InvalidOperationException("The AI action identity is invalid.");
        if (legal.SnapshotRevision != snapshot.SnapshotRevision)
            throw new InvalidOperationException("The AI action is not from the current snapshot.");
        if (legal.Actor != snapshot.CurrentPlayer)
            throw new InvalidOperationException("The AI action actor is not the current player.");

        var result = new RuntimeGameAction
        {
            ContractVersion = legal.ContractVersion,
            MatchId = snapshot.MatchId,
            SnapshotRevision = legal.SnapshotRevision,
            ActionId = legal.ActionId,
            Type = legal.Type,
            Actor = legal.Actor,
            SourceId = legal.SourceId,
            TargetId = legal.TargetId,
            CardId = legal.CardId,
            Payload = legal.Payload,
        };
        var validation = RuntimeActionBoundary.Validate(result, snapshot);
        if (!validation.Accepted)
            throw new InvalidOperationException(validation.ReasonKey);
        return result;
    }

    private static List<RuntimeLegalAction> StableActions(
        IReadOnlyList<RuntimeLegalAction> actions)
    {
        var result = new List<RuntimeLegalAction>();
        foreach (var action in actions)
        {
            if (action is not null) result.Add(action);
        }

        result.Sort((left, right) =>
        {
            var id = string.Compare(left.ActionId, right.ActionId, StringComparison.Ordinal);
            if (id != 0) return id;
            return string.Compare(left.Type, right.Type, StringComparison.Ordinal);
        });
        return result;
    }

    private static RuntimeLegalAction FirstType(
        IReadOnlyList<RuntimeLegalAction> actions,
        string type)
    {
        foreach (var action in actions)
        {
            if (string.Equals(action.Type, type, StringComparison.Ordinal))
                return action;
        }

        return null;
    }

    private static RuntimeLegalAction FirstNonType(
        IReadOnlyList<RuntimeLegalAction> actions,
        string type)
    {
        foreach (var action in actions)
        {
            if (!string.Equals(action.Type, type, StringComparison.Ordinal))
                return action;
        }

        return null;
    }
}
}
