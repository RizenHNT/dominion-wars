using System;
using DominionWars.Adapters;
using DominionWars.Adapters.Ai;

namespace DominionWars.Unity.Runtime
{

/// <summary>
/// Unity-side facade for the bounded CPU turn driver.
///
/// This file is compiled by Unity only. It is not part of DominionWars.sln and
/// is therefore NOT covered by the .NET test run (dotnet test / the in-process
/// NUnit runner); the behaviour it delegates to lives in
/// DominionWars.Adapters.Ai.AiTurnCoordinator, which the .NET acceptance tests
/// in src/Engine/Tests/AiTurnCoordinatorTests.cs do exercise against a real
/// machine-deck match. Keep the two in step: this class must stay a pure
/// delegation with no pacing, ordering, or legality logic of its own.
///
/// The paced behaviour is unchanged from the host's point of view: a host calls
/// Pump once per frame, each pump performs at most one submission, and the
/// coordinator obtains an AI-view snapshot, submits one complete advertised
/// action, and waits for the next pump before continuing.
/// </summary>
public sealed class RuntimeAiTurnCoordinator
{
    public const int DefaultMaxActionsPerTurn = AiTurnCoordinator.DefaultMaxActionsPerTurn;

    public const string ActionPhase = AdvertisedActionPolicy.ActionPhase;
    public const string EndTurnAction = AdvertisedActionPolicy.EndTurnAction;

    private readonly AiTurnCoordinator _coordinator;

    public RuntimeAiTurnCoordinator(
        RuntimeAdapter adapter,
        int aiPlayerIndex = 1,
        int presentationViewerIndex = 0,
        RuntimeAiPolicy policy = null,
        int maxActionsPerTurn = DefaultMaxActionsPerTurn)
    {
        if (adapter is null) throw new ArgumentNullException(nameof(adapter));
        _coordinator = new AiTurnCoordinator(
            new RuntimeAdapterAiTurnHost(adapter),
            aiPlayerIndex,
            presentationViewerIndex,
            policy?.InnerPolicy,
            maxActionsPerTurn);
    }

    public int ActionsTakenThisTurn => _coordinator.ActionsTakenThisTurn;

    public bool Halted => _coordinator.Halted;

    public string LastReasonKey => _coordinator.LastReasonKey;

    /// <summary>
    /// Performs at most one AI action. False means no submission was made for
    /// this pump; a stopped/terminal/non-AI turn is intentionally fail-closed.
    /// </summary>
    public bool Pump() => _coordinator.Pump();

    public void Reset() => _coordinator.Reset();

    public void Halt(string reasonKey) => _coordinator.Halt(reasonKey);

    /// <summary>
    /// Adapts the Unity presentation adapter (RuntimeAdapter, which wraps
    /// IRuntimeSession) to the renderer-neutral IAiTurnHost seam the ported
    /// coordinator consumes. No rules live here: it forwards the two transport
    /// calls only.
    /// </summary>
    private sealed class RuntimeAdapterAiTurnHost : IAiTurnHost
    {
        private readonly RuntimeAdapter _adapter;

        public RuntimeAdapterAiTurnHost(RuntimeAdapter adapter)
        {
            _adapter = adapter;
        }

        public RuntimeSnapshotEnvelope GetSnapshotForViewer(int viewerPlayerIndex)
            => _adapter.GetSnapshotForViewer(viewerPlayerIndex);

        public RuntimeActionSubmission SubmitForViewer(
            RuntimeGameAction action,
            int presentationViewerIndex)
            => _adapter.SubmitForViewer(action, presentationViewerIndex);
    }
}
}
