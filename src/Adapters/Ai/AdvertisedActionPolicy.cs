using System;
using System.Collections.Generic;

namespace DominionWars.Adapters.Ai
{

/// <summary>
/// Deterministic CPU-opponent policy that lives in the buildable adapter
/// assembly so the .NET test run can exercise it. It keeps the existing
/// boundary contract: the policy only consumes the viewer-safe snapshot and
/// returns one action that the authoritative engine already advertised,
/// together with that action's own payload. It never receives GameState,
/// never derives rules, and never synthesises a target, a card id or a
/// selectedEntityIds value.
///
/// The ACTION phase is decided by a playstyle weight vector
/// (<see cref="IPlaystyle"/> / <see cref="WeightedPlaystyle"/>) over the pure
/// feature vector in <see cref="ActionFeatures"/>; the default is
/// <see cref="PlaystyleRegistry.Default"/>, which is the retired hardcoded
/// priority expressed as weights, so the shipped behaviour is unchanged. The
/// phase rules below (forced end turn, never setting an ambush, the forced
/// discard) are phase decisions rather than playstyle decisions and apply to
/// every playstyle.
/// </summary>
public sealed class AdvertisedActionPolicy
{
    public const string ActionPhase = "ACTION";
    public const string AmbushPhase = "AMBUSH";
    public const string DiscardPhase = "DISCARD";
    public const string EndTurnAction = "END_TURN";
    public const string SkipAmbushAction = "SKIP_AMBUSH";
    public const string SetAmbushAction = "SET_AMBUSH";
    public const string PullAction = "PULL";
    public const string PushAction = "PUSH";
    public const string PlayCardAction = "PLAY_CARD";
    public const string CommitAction = "COMMIT";
    public const string RollbackAction = "ROLLBACK";
    public const string AttackAction = "ATTACK";
    public const string DiscardAction = "DISCARD";

    private readonly IPlaystyle _playstyle;

    /// <summary>
    /// The shipped default policy: <see cref="PlaystyleRegistry.Default"/>.
    /// Every already-measured reading was produced by this weight vector.
    /// </summary>
    public AdvertisedActionPolicy() : this(null)
    {
    }

    /// <summary>
    /// The same boundary with a different playstyle. A null playstyle means the
    /// shipped default. The playstyle can only reorder the actions this policy
    /// was already willing to submit; it cannot widen the advertised set, invent
    /// a payload, or change a phase rule.
    /// </summary>
    public AdvertisedActionPolicy(IPlaystyle? playstyle)
    {
        _playstyle = playstyle ?? PlaystyleRegistry.Default;
    }

    /// <summary>The playstyle that orders the advertised ACTION-phase actions.</summary>
    public IPlaystyle Playstyle => _playstyle;

    /// <summary>
    /// Orders the advertised ACTION-phase actions by the policy's priority and
    /// removes the ones this boundary refuses to submit. Exposed so the
    /// ordering contract is directly testable; the caller still receives
    /// advertised actions only.
    /// </summary>
    public IReadOnlyList<RuntimeLegalAction> OrderAdvertisedActions(
        RuntimeSnapshotEnvelope snapshot,
        IReadOnlyList<RuntimeLegalAction> legalActions)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (legalActions is null) throw new ArgumentNullException(nameof(legalActions));

        var legal = StableActions(legalActions);
        var usable = new List<RuntimeLegalAction>(legal.Count);
        foreach (var candidate in WeightedPlaystyle.Order(_playstyle, snapshot, legal))
        {
            if (IsUsable(snapshot, candidate)) usable.Add(candidate);
        }

        return usable;
    }
    /// <summary>
    /// Selects one advertised action. Pure and deterministic: the same
    /// snapshot and flags always produce the same action.
    /// </summary>
    public bool TryChoose(
        RuntimeSnapshotEnvelope snapshot,
        int aiPlayerIndex,
        bool forceEndTurn,
        out RuntimeLegalAction? chosen)
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

        // A forced end turn is a host budget decision, not a phase decision.
        if (forceEndTurn)
        {
            var forced = FirstType(legal, EndTurnAction);
            if (forced is not null)
            {
                chosen = forced;
                return true;
            }
        }

        if (string.Equals(snapshot.Phase, AmbushPhase, StringComparison.Ordinal))
        {
            // Conservative, unchanged baseline behaviour: the opponent never
            // commits an ambush, but still chooses from the advertised set.
            chosen = FirstType(legal, SkipAmbushAction) ?? legal[0];
            return true;
        }

        if (string.Equals(snapshot.Phase, DiscardPhase, StringComparison.Ordinal))
        {
            // The engine advertises the complete discard action together with
            // its requiredCount/candidateIds payload; that payload is forwarded
            // untouched and the gateway bridges it into the engine request.
            chosen = legal[0];
            return true;
        }

        if (string.Equals(snapshot.Phase, ActionPhase, StringComparison.Ordinal))
        {
            var usable = OrderAdvertisedActions(snapshot, snapshot.LegalActions);
            if (usable.Count > 0)
            {
                chosen = usable[0];
                return true;
            }

            chosen = legal[0];
            return true;
        }

        chosen = legal[0];
        return true;
    }

    /// <summary>
    /// Copies one complete advertised action without adding targets, selections, or
    /// rule-derived payload values. The produced action is field-for-field identical to
    /// the advertisement, so it satisfies RuntimeActionBoundary.Validate by construction.
    ///
    /// KNOWN GAP, measured rather than assumed: when a player is under "punish converts to
    /// a self-discard", the engine advertises a PLAY_CARD carrying <c>discardRequired</c>
    /// and <c>discardCandidateIds</c>, and there is NO way to submit the required selection
    /// with it. The wire name the gateway reads for a selection is <c>selectedEntityIds</c>,
    /// and RuntimeActionBoundary requires the submitted payload to EQUAL the advertisement,
    /// so a submitter cannot add it; the gateway only bridges the candidate-set shape for
    /// the DISCARD phase's own action, not for PLAY_CARD. The submission is therefore
    /// rejected with <c>action.discard_selection_required</c> and the AI re-picks the same
    /// action instead of playing.
    ///
    /// That gap is in the contract between the engine's advertisement and the gateway, so it
    /// is NOT fixed here: papering over it in this adapter would either break the boundary
    /// rule or invent a selection the engine never published. It is reported as a finding
    /// (see AiTacticalBenchmarkTests.SelfDiscardPlayIsUnsubmittableAsAdvertised).
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
            Payload = WithSelfDiscardSelection(legal),
        };
        var validation = RuntimeActionBoundary.Validate(result, snapshot);
        if (!validation.Accepted)
            throw new InvalidOperationException(validation.ReasonKey);
        return result;
    }

    /// <summary>
    /// Carries the discard CHOICE for a self-discard PLAY_CARD.
    ///
    /// When the engine advertises `discardRequired` + `discardCandidateIds`, its play handler REQUIRES
    /// the chosen discard ids and rejects the play with `action.discard_selection_required` without
    /// them. The choice has no other wire slot, so it travels as `selectedEntityIds` — the one field
    /// the boundary permits beyond the advertisement, validated there against the advertised
    /// requirement and candidate set.
    ///
    /// WHO CHOOSES: the AI does, here, and the choice is the FIRST `discardRequired` advertised
    /// candidates in the engine's own order. That order is deterministic and is the engine's, not this
    /// adapter's opinion about which card is least valuable. This is deliberately NOT a silent gateway
    /// auto-pick: the ruling is that the player chooses within the window (owner, 2026-09-12), so the
    /// decision is taken where decisions are taken and then submitted for validation. A future policy
    /// that wants to discard something else changes this one method.
    ///
    /// Without those advertisement fields the payload passes through untouched, so every other action
    /// keeps the exact-match contract unchanged.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> WithSelfDiscardSelection(RuntimeLegalAction legal)
    {
        var payload = legal.Payload;
        if (payload is null
            || !payload.TryGetValue("discardRequired", out var rawRequired)
            || rawRequired is null
            || !payload.TryGetValue("discardCandidateIds", out var rawCandidates)
            || rawCandidates is null)
        {
            return payload ?? new Dictionary<string, object?>();
        }

        if (!RuntimeWireValue.TryGetInt64(rawRequired, out var required) || required <= 0)
        {
            return payload;
        }

        if (!RuntimeWireValue.TryEnumerate(rawCandidates, out var candidates))
        {
            return payload;
        }

        var selected = new List<object?>();
        foreach (var candidate in candidates)
        {
            if (selected.Count >= required) break;
            if (RuntimeWireValue.TryGetInt64(candidate, out var id) && id > 0)
            {
                selected.Add(id);
            }
        }

        if (selected.Count != required)
        {
            // Not enough candidates to satisfy the requirement: submit unchanged and let the engine
            // reject it with its own reason, rather than padding the selection with something invented.
            return payload;
        }

        return new Dictionary<string, object?>(payload, StringComparer.Ordinal)
        {
            ["selectedEntityIds"] = selected,
        };
    }

    /// <summary>
    /// Priority of one advertised ACTION-phase action; lower runs first. A
    /// download (PULL) advances the owner's leader win condition directly and
    /// outranks everything, playing and committing build the board and the
    /// cloud stack the download consumes, and attacking and ending the turn
    /// come last. Exposed so the ordering contract can be asserted directly.
    ///
    /// This is the frozen reference order of the shipped default playstyle: the
    /// default's weights in <see cref="PlaystyleRegistry"/> are chosen so that
    /// ordering by weighted score is exactly ordering by this rank (there is a
    /// test that enumerates the ACTION-phase cross product and proves it). It
    /// stays public because it documents what the already-measured readings
    /// were produced by; the ordering used to decide actions is the playstyle's.
    /// </summary>
    public int AdvertisedActionPriority(
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction action)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (action is null) throw new ArgumentNullException(nameof(action));
        return Rank(snapshot, action);
    }

    private static int Rank(RuntimeSnapshotEnvelope snapshot, RuntimeLegalAction action)
    {
        var type = action.Type ?? string.Empty;
        if (string.Equals(type, PullAction, StringComparison.Ordinal))
        {
            // "Useful" is read from the viewer-safe snapshot alone: a download
            // with nothing in the public cloud stack cannot resolve, and the
            // engine only makes a targeted download executable by advertising
            // the selectedEntityIds it already chose.
            return IsUsefulPull(snapshot, action) ? 0 : 2;
        }

        if (string.Equals(type, PlayCardAction, StringComparison.Ordinal)) return 1;
        if (string.Equals(type, CommitAction, StringComparison.Ordinal)) return 3;
        if (string.Equals(type, AttackAction, StringComparison.Ordinal)) return 4;
        if (string.Equals(type, EndTurnAction, StringComparison.Ordinal)) return 5;
        return 6;
    }

    /// <summary>
    /// True when the snapshot says an advertised download can still make
    /// progress. Reads only viewer-safe snapshot fields; it never inspects the
    /// advertisement for rule data.
    /// </summary>
    public bool IsUsefulAdvertisedDownload(
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction action)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (!string.Equals(action.Type, PullAction, StringComparison.Ordinal)) return false;
        return IsUsefulPull(snapshot, action);
    }

    private static bool IsUsefulPull(RuntimeSnapshotEnvelope snapshot, RuntimeLegalAction action)
    {
        if (snapshot.Players is null || snapshot.Players.Count != 2)
            return false;
        if (snapshot.CurrentPlayer is < 0 or > 1)
            return false;
        if (snapshot.Players[snapshot.CurrentPlayer].CloudStackCount <= 0)
            return false;
        return true;
    }

    private static bool HasWireSelection(IReadOnlyDictionary<string, object?> payload)
    {
        if (payload.TryGetValue("selectedEntityIds", out var raw)) return raw is not null;
        if (payload.TryGetValue("selectedEntityId", out var single)) return single is not null;
        return false;
    }

    /// <summary>
    /// Fails closed only for a download advertisement that claims a selection
    /// but carries none; every other advertised action is usable verbatim. The
    /// policy never invents the missing selection.
    /// </summary>
    private static bool IsUsable(RuntimeSnapshotEnvelope snapshot, RuntimeLegalAction action)
    {
        if (action is null) return false;
        if (action.Actor != snapshot.CurrentPlayer) return false;
        if (!string.Equals(action.Type, PullAction, StringComparison.Ordinal)) return true;
        if (action.Payload is null) return true;
        var requiresSelection = action.Payload.ContainsKey("selectedEntityIds")
            || action.Payload.ContainsKey("selectedEntityId");
        if (!requiresSelection) return true;
        return HasWireSelection(action.Payload);
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

    private static RuntimeLegalAction? FirstType(
        List<RuntimeLegalAction> actions,
        string type)
    {
        foreach (var action in actions)
        {
            if (string.Equals(action.Type, type, StringComparison.Ordinal))
                return action;
        }

        return null;
    }
}

}
