using DominionWars.Adapters;
using DominionWars.Adapters.Ai;

namespace DominionWars.Unity.Runtime
{

/// <summary>
/// Unity-side facade for the deterministic CPU opponent policy.
///
/// This file is compiled by Unity only. It is not part of DominionWars.sln and
/// is therefore NOT covered by the .NET test run; the behaviour it delegates to
/// lives in DominionWars.Adapters.Ai.AdvertisedActionPolicy, which the .NET
/// acceptance tests in src/Engine/Tests/AiLifecyclePolicyTests.cs do exercise.
///
/// The policy only consumes the viewer-safe snapshot and chooses an action
/// already advertised by the authoritative engine. It never receives GameState
/// or derives rules.
/// </summary>
public sealed class RuntimeAiPolicy
{
    public const string ActionPhase = AdvertisedActionPolicy.ActionPhase;
    public const string AmbushPhase = AdvertisedActionPolicy.AmbushPhase;
    public const string DiscardPhase = AdvertisedActionPolicy.DiscardPhase;
    public const string EndTurnAction = AdvertisedActionPolicy.EndTurnAction;
    public const string SkipAmbushAction = AdvertisedActionPolicy.SkipAmbushAction;

    private readonly AdvertisedActionPolicy _policy = new AdvertisedActionPolicy();

    /// <summary>
    /// The shared adapter-level policy this facade delegates to. Unity-only
    /// callers (RuntimeAiTurnCoordinator) forward it straight into the ported
    /// coordinator, so no decision logic is duplicated on the Unity side.
    /// </summary>
    internal AdvertisedActionPolicy InnerPolicy => _policy;

    /// <summary>
    /// Selects one advertised action through the shared adapter-level policy.
    /// </summary>
    public bool TryChoose(
        RuntimeSnapshotEnvelope snapshot,
        int aiPlayerIndex,
        bool forceEndTurn,
        out RuntimeLegalAction chosen)
    {
        var selected = _policy.TryChoose(snapshot, aiPlayerIndex, forceEndTurn, out var chosenAction);
        chosen = chosenAction!;
        return selected;
    }

    /// <summary>
    /// Copies one complete advertised action without adding targets,
    /// selections, or rule-derived payload values.
    /// </summary>
    public static RuntimeGameAction ToGameAction(
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction legal)
    {
        return AdvertisedActionPolicy.ToGameAction(snapshot, legal);
    }
}
}
