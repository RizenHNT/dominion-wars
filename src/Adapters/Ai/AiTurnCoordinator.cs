using System;

namespace DominionWars.Adapters.Ai
{

/// <summary>
/// The two transport operations the CPU turn driver needs from the host
/// adapter: read one viewer-safe snapshot, and submit one action on behalf of
/// its actor while keeping presentation bound to a separate viewer.
///
/// The shipped Unity host implements this on RuntimeAdapter (which wraps
/// RuntimeGatewaySession / RuntimeMatchGateway); the .NET test run implements
/// it directly over RuntimeMatchGateway. The coordinator itself is therefore
/// renderer-neutral and stays inside the buildable adapter assembly.
/// </summary>
public interface IAiTurnHost
{
    RuntimeSnapshotEnvelope GetSnapshotForViewer(int viewerPlayerIndex);

    RuntimeActionSubmission SubmitForViewer(RuntimeGameAction action, int presentationViewerIndex);
}

/// <summary>
/// Bounded CPU turn driver for the shipped game.
///
/// Pacing contract (the P0-5 fix). A host calls <see cref="Pump"/> once per
/// frame; each pump performs at most one submission and this class never loops
/// internally, so a single pump is still a single action on the wire.
///
/// The AI does NOT end its ACTION phase just because it already acted this
/// turn. It keeps choosing advertised ACTION-phase actions while the engine
/// still advertises one this policy could actually submit and that would
/// continue the phase - any advertised action other than END_TURN, except a
/// download the viewer-safe snapshot says cannot resolve - and it ends the
/// phase (by selecting the advertised END_TURN) only when (a) no such action
/// is advertised (nothing but END_TURN, an empty advertisement, or downloads
/// that cannot progress) or (b) the per-turn budget
/// (<see cref="DefaultMaxActionsPerTurn"/> or the constructor argument) is
/// exhausted. At the budget boundary it may submit only a current-snapshot,
/// current-actor END_TURN that the engine already advertised; otherwise it
/// halts with <c>ai.action_limit_reached</c>. It never submits a 33rd
/// optional ACTION action or synthesises an END_TURN.
///
/// Legality is unchanged and stays entirely engine-owned: the policy may only
/// return an action the engine already advertised, every submitted action is a
/// field-for-field copy of its advertisement, and this coordinator never
/// inspects GameState, never synthesises a target, card id, or
/// advertised candidate set; for an action that explicitly requires a card
/// choice it copies a deterministic selection into the separate request field
/// without changing the advertised payload, and never widens the advertised set to keep acting. The
/// budget is a presentation budget, not a rule: at its boundary it may only
/// forward an already-advertised END_TURN; otherwise it stops the driver.
///
/// Fail-closed cases, unchanged from the shipped Unity coordinator: match over
/// (<c>ai.match_over</c>), not the AI's turn (<c>ai.waiting_for_turn</c>, not
/// halted), no legal actions (<c>ai.no_legal_actions</c>), an advertised set
/// the policy cannot select from (<c>ai.no_action_selected</c>), a resubmission
/// of the same action against the same revision (<c>ai.duplicate_action</c>),
/// and a rejected submission (the engine's own reason key, or
/// <c>ai.action_rejected</c>). Every one of those halts instead of retrying.
/// </summary>
public sealed class AiTurnCoordinator
{
    public const int DefaultMaxActionsPerTurn = 32;

    private readonly IAiTurnHost _host;
    private readonly AdvertisedActionPolicy _policy;
    private readonly int _aiPlayerIndex;
    private readonly int _presentationViewerIndex;
    private readonly int _maxActionsPerTurn;
    private int _turn = -1;
    private string _phase = string.Empty;
    private int _actionsTakenInActionPhase;
    private long _lastSubmittedRevision = -1;
    private string _lastSubmittedActionId = string.Empty;

    public AiTurnCoordinator(
        IAiTurnHost host,
        int aiPlayerIndex = 1,
        int presentationViewerIndex = 0,
        AdvertisedActionPolicy? policy = null,
        int maxActionsPerTurn = DefaultMaxActionsPerTurn)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        if (aiPlayerIndex is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(aiPlayerIndex));
        if (presentationViewerIndex is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(presentationViewerIndex));
        if (maxActionsPerTurn <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxActionsPerTurn));

        _aiPlayerIndex = aiPlayerIndex;
        _presentationViewerIndex = presentationViewerIndex;
        _policy = policy ?? new AdvertisedActionPolicy();
        _maxActionsPerTurn = maxActionsPerTurn;
    }

    public int AiPlayerIndex => _aiPlayerIndex;

    public int MaxActionsPerTurn => _maxActionsPerTurn;

    /// <summary>
    /// Every accepted submission since the AI's current turn began, including
    /// the END_TURN that leaves the ACTION phase.
    /// </summary>
    public int ActionsTakenThisTurn { get; private set; }

    public bool Halted { get; private set; }

    public string LastReasonKey { get; private set; } = string.Empty;

    /// <summary>
    /// Performs at most one AI action. False means no submission was made for
    /// this pump; a stopped/terminal/non-AI turn is intentionally fail-closed.
    /// A single ACTION phase may span many successful pumps.
    /// </summary>
    public bool Pump()
    {
        if (Halted) return false;

        var snapshot = _host.GetSnapshotForViewer(_aiPlayerIndex);
        if (snapshot is null || snapshot.WinnerPlayerIndex.HasValue ||
            string.Equals(snapshot.Phase, "OVER", StringComparison.Ordinal))
        {
            Halted = true;
            LastReasonKey = "ai.match_over";
            return false;
        }

        if (snapshot.CurrentPlayer != _aiPlayerIndex)
        {
            ResetTurnState();
            LastReasonKey = "ai.waiting_for_turn";
            return false;
        }

        if (_turn != snapshot.Turn)
        {
            _turn = snapshot.Turn;
            ActionsTakenThisTurn = 0;
            _actionsTakenInActionPhase = 0;
            _phase = string.Empty;
        }

        if (!string.Equals(_phase, snapshot.Phase, StringComparison.Ordinal))
        {
            _phase = snapshot.Phase;
            if (string.Equals(_phase, AdvertisedActionPolicy.ActionPhase, StringComparison.Ordinal))
                _actionsTakenInActionPhase = 0;
        }

        // The safety budget bounds optional ACTION-phase work. Mandatory
        // AMBUSH/DISCARD phases must still be able to resolve after a long
        // action phase, otherwise fail-closed turns become unrecoverable
        // matches. At the boundary, accept only the exact END_TURN the
        // current snapshot advertises for this actor. A stale or wrong-actor
        // END_TURN is not a legal escape hatch, and no optional action may be
        // submitted as a 33rd action.
        RuntimeLegalAction? budgetEndTurn = null;
        var actionBudgetReached =
            string.Equals(snapshot.Phase, AdvertisedActionPolicy.ActionPhase, StringComparison.Ordinal) &&
            _actionsTakenInActionPhase >= _maxActionsPerTurn;
        if (actionBudgetReached)
        {
            budgetEndTurn = FindAdvertisedEndTurn(snapshot);
            if (budgetEndTurn is null)
            {
                Halted = true;
                LastReasonKey = "ai.action_limit_reached";
                return false;
            }
        }

        var forceEndTurn = actionBudgetReached || !ActionPhaseCanMakeProgress(snapshot);
        RuntimeLegalAction? legal;
        if (budgetEndTurn is not null)
        {
            legal = budgetEndTurn;
        }
        else if (!_policy.TryChoose(snapshot, _aiPlayerIndex, forceEndTurn, out legal) || legal is null)
        {
            Halted = true;
            LastReasonKey = snapshot.LegalActions is null || snapshot.LegalActions.Count == 0
                ? "ai.no_legal_actions"
                : "ai.no_action_selected";
            return false;
        }

        if (_lastSubmittedRevision == snapshot.SnapshotRevision &&
            string.Equals(_lastSubmittedActionId, legal.ActionId, StringComparison.Ordinal))
        {
            Halted = true;
            LastReasonKey = "ai.duplicate_action";
            return false;
        }

        var action = AdvertisedActionPolicy.ToGameAction(snapshot, legal);
        if (!RuntimeActionSelection.TryBuildStableSelection(
            legal,
            out var selectedEntityIds,
            out var selectionReason))
        {
            Halted = true;
            LastReasonKey = string.IsNullOrWhiteSpace(selectionReason)
                ? "ai.selection_invalid"
                : selectionReason;
            return false;
        }

        // The CPU makes an explicit, deterministic choice from the engine's
        // advertised candidate order. The legal-action payload remains the
        // exact advertisement; only the separate request selection is filled.
        // Keep the already-shipped self-discard payload choice compatible: the
        // two channels are mutually exclusive at the boundary, so an older
        // policy-produced payload selection must not be duplicated as typed
        // selection on the same request.
        if (selectedEntityIds.Count > 0 &&
            !action.Payload.ContainsKey("selectedEntityIds"))
            action.SelectedEntityIds = selectedEntityIds;

        var submission = _host.SubmitForViewer(action, _presentationViewerIndex);
        _lastSubmittedRevision = action.SnapshotRevision;
        _lastSubmittedActionId = action.ActionId ?? string.Empty;
        if (submission is null || submission.Result is null || !submission.Result.Accepted)
        {
            Halted = true;
            LastReasonKey = submission?.Result?.ReasonKey ?? "ai.action_rejected";
            return false;
        }

        ActionsTakenThisTurn++;
        if (string.Equals(snapshot.Phase, AdvertisedActionPolicy.ActionPhase, StringComparison.Ordinal))
            _actionsTakenInActionPhase++;
        LastReasonKey = "ai.action_accepted";
        return true;
    }

    private static RuntimeLegalAction? FindAdvertisedEndTurn(RuntimeSnapshotEnvelope snapshot)
    {
        foreach (var candidate in snapshot.LegalActions)
        {
            if (candidate is null ||
                !string.Equals(candidate.Type, AdvertisedActionPolicy.EndTurnAction, StringComparison.Ordinal) ||
                candidate.Actor != snapshot.CurrentPlayer ||
                candidate.SnapshotRevision != snapshot.SnapshotRevision ||
                string.IsNullOrWhiteSpace(candidate.ActionId))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    /// <summary>
    /// Clears the halt and the submission history. The next pump starts a
    /// fresh attempt against whatever the engine currently advertises.
    /// </summary>
    public void Reset()
    {
        Halted = false;
        LastReasonKey = string.Empty;
        ResetTurnState();
        _lastSubmittedRevision = -1;
        _lastSubmittedActionId = string.Empty;
    }

    public void Halt(string reasonKey)
    {
        Halted = true;
        LastReasonKey = string.IsNullOrWhiteSpace(reasonKey)
            ? "ai.halted"
            : reasonKey;
    }

    /// <summary>
    /// True only when an advertised ACTION-phase action could still fairly be
    /// submitted and would continue the phase, i.e. the phase may continue. A
    /// candidate counts when it is not END_TURN and this policy could submit it:
    /// any advertised action is acceptable except a download, which counts only
    /// when the viewer-safe snapshot says it can resolve (an unresolvable
    /// download is exactly the advertisement AdvertisedActionPolicy.IsUsable
    /// refuses to submit). The coordinator selects or rewrites nothing here -
    /// it only decides whether the phase is worth continuing - so the advertised
    /// set and the engine's legality stay the sole authority.
    /// </summary>
    private bool ActionPhaseCanMakeProgress(RuntimeSnapshotEnvelope snapshot)
    {
        if (!string.Equals(snapshot.Phase, AdvertisedActionPolicy.ActionPhase, StringComparison.Ordinal))
            return false;

        foreach (var candidate in snapshot.LegalActions)
        {
            if (candidate is null) continue;
            if (string.Equals(candidate.Type, AdvertisedActionPolicy.EndTurnAction, StringComparison.Ordinal))
                continue;
            if (!string.Equals(candidate.Type, AdvertisedActionPolicy.PullAction, StringComparison.Ordinal))
                return true;
            if (_policy.IsUsefulAdvertisedDownload(snapshot, candidate))
                return true;
        }

        return false;
    }

    private void ResetTurnState()
    {
        _turn = -1;
        _phase = string.Empty;
        _actionsTakenInActionPhase = 0;
        ActionsTakenThisTurn = 0;
    }
}

}
