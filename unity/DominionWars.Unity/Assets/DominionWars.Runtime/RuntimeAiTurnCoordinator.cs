using System;
using DominionWars.Adapters;

namespace DominionWars.Unity.Runtime
{

/// <summary>
/// Bounded one-action-per-pump CPU turn driver. A host calls Pump once per
/// frame; the coordinator obtains an AI-view snapshot, submits one complete
/// advertised action, and waits for the next pump before continuing.
/// </summary>
public sealed class RuntimeAiTurnCoordinator
{
    public const int DefaultMaxActionsPerTurn = 32;

    private readonly RuntimeAdapter _adapter;
    private readonly RuntimeAiPolicy _policy;
    private readonly int _aiPlayerIndex;
    private readonly int _presentationViewerIndex;
    private readonly int _maxActionsPerTurn;
    private int _turn = -1;
    private int _actionPhaseActions;
    private long _lastSubmittedRevision = -1;
    private string _lastSubmittedActionId = string.Empty;

    public RuntimeAiTurnCoordinator(
        RuntimeAdapter adapter,
        int aiPlayerIndex = 1,
        int presentationViewerIndex = 0,
        RuntimeAiPolicy policy = null,
        int maxActionsPerTurn = DefaultMaxActionsPerTurn)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        if (aiPlayerIndex is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(aiPlayerIndex));
        if (presentationViewerIndex is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(presentationViewerIndex));
        if (maxActionsPerTurn <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxActionsPerTurn));

        _aiPlayerIndex = aiPlayerIndex;
        _presentationViewerIndex = presentationViewerIndex;
        _policy = policy ?? new RuntimeAiPolicy();
        _maxActionsPerTurn = maxActionsPerTurn;
    }

    public int ActionsTakenThisTurn { get; private set; }
    public bool Halted { get; private set; }
    public string LastReasonKey { get; private set; } = string.Empty;

    /// <summary>
    /// Performs at most one AI action. False means no submission was made for
    /// this pump; a stopped/terminal/non-AI turn is intentionally fail-closed.
    /// </summary>
    public bool Pump()
    {
        if (Halted) return false;

        var snapshot = _adapter.GetSnapshotForViewer(_aiPlayerIndex);
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
            _actionPhaseActions = 0;
        }

        if (!string.Equals(snapshot.Phase, RuntimeAiPolicy.ActionPhase, StringComparison.Ordinal))
            _actionPhaseActions = 0;

        if (ActionsTakenThisTurn >= _maxActionsPerTurn)
        {
            Halted = true;
            LastReasonKey = "ai.action_limit_reached";
            return false;
        }

        var forceEndTurn = string.Equals(snapshot.Phase, RuntimeAiPolicy.ActionPhase, StringComparison.Ordinal) &&
            _actionPhaseActions > 0;
        if (!_policy.TryChoose(snapshot, _aiPlayerIndex, forceEndTurn, out var legal))
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

        var action = RuntimeAiPolicy.ToGameAction(snapshot, legal);
        var submission = _adapter.SubmitForViewer(action, _presentationViewerIndex);
        _lastSubmittedRevision = action.SnapshotRevision;
        _lastSubmittedActionId = action.ActionId ?? string.Empty;
        if (submission is null || submission.Result is null || !submission.Result.Accepted)
        {
            Halted = true;
            LastReasonKey = submission?.Result?.ReasonKey ?? "ai.action_rejected";
            return false;
        }

        ActionsTakenThisTurn++;
        if (string.Equals(snapshot.Phase, RuntimeAiPolicy.ActionPhase, StringComparison.Ordinal) &&
            !string.Equals(legal.Type, RuntimeAiPolicy.EndTurnAction, StringComparison.Ordinal))
        {
            _actionPhaseActions++;
        }

        LastReasonKey = "ai.action_accepted";
        return true;
    }

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

    private void ResetTurnState()
    {
        _turn = -1;
        ActionsTakenThisTurn = 0;
        _actionPhaseActions = 0;
    }
}
}
