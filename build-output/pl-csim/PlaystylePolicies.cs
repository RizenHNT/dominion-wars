using System;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Adapters.Ai;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using EngineLegalAction = DominionWars.Engine.LegalAction;

namespace PlCsim
{
    /// <summary>
    /// Which AI decides an action. Two implementations exist so a balance claim
    /// can be tested against both the production policy and the legacy harness
    /// heuristic:
    ///
    /// <list type="bullet">
    /// <item><see cref="MatrixPolicy"/> drives the SHIPPED playstyle boundary
    /// (<see cref="AdvertisedActionPolicy"/> over
    /// <see cref="RuntimeSnapshotProjection"/>), so a playstyle id selects the
    /// ordering and nothing is reimplemented here.</item>
    /// <item><see cref="HarnessPolicy"/> drives the legacy CsimPolicy heuristic
    /// used for every earlier measurement in this scratch area.</item>
    /// </list>
    /// </summary>
    internal interface IMatchPolicy
    {
        string Describe { get; }

        /// <summary>Playstyle id when this policy is driven by the registry, else null.</summary>
        string? PlaystyleId { get; }

        /// <summary>FNV-1a fingerprint of the playstyle weight vector, else null.</summary>
        string? PlaystyleFingerprint { get; }

        EngineLegalAction? ChooseAmbush(GameState state, IReadOnlyList<EngineLegalAction> actions);

        EngineLegalAction? ChooseAction(GameState state, IReadOnlyList<EngineLegalAction> actions);

        IReadOnlyList<long> ChooseDiscards(GameState state, int actor, IReadOnlyList<long> candidates, int required);

        void NoteSubmission(EngineLegalAction action, bool accepted);

        /// <summary>
        /// Set by the driver before each choice. The projection must stamp the
        /// SAME revision the gateway will validate against, otherwise every
        /// submission is rejected as action.stale_snapshot
        /// (RuntimeContractV131ActionBoundary.cs:152).
        /// </summary>
        long SnapshotRevision { get; set; }
    }

    /// <summary>
    /// The legacy harness heuristic, unchanged. Kept so the playstyle matrix can
    /// be read against the numbers already measured tonight.
    /// </summary>
    internal sealed class HarnessPolicy : IMatchPolicy
    {
        private readonly CsimPolicy _policy;

        public HarnessPolicy(bool aggressive, string? noBuffFaction)
        {
            _policy = new CsimPolicy { Aggressive = aggressive, NoBuffPlayForFaction = noBuffFaction };
        }

        public string Describe => "harness heuristic (CsimPolicy)";
        public string? PlaystyleId => null;
        public string? PlaystyleFingerprint => null;

        public EngineLegalAction? ChooseAmbush(GameState state, IReadOnlyList<EngineLegalAction> actions) =>
            _policy.ChooseAmbush(state, actions);

        public EngineLegalAction? ChooseAction(GameState state, IReadOnlyList<EngineLegalAction> actions) =>
            _policy.ChooseAction(state, actions);

        public IReadOnlyList<long> ChooseDiscards(GameState state, int actor, IReadOnlyList<long> candidates, int required) =>
            _policy.ChooseDiscards(state, actor, candidates, required);

        public long SnapshotRevision { get; set; }

        public void NoteSubmission(EngineLegalAction action, bool accepted)
        {
            if (!accepted) return;
            // NoteSubmission takes the engine action result in the legacy path;
            // the gateway path only knows the boolean, so replay the two
            // bookkeeping effects CsimPolicy performs on acceptance.
            _policy.NoteAccepted(action);
        }
    }

    /// <summary>
    /// Decides through the shipped playstyle boundary.
    ///
    /// It is given the viewer-safe snapshot the adapter projected from the same
    /// GameState the harness holds, so it sees exactly what a CPU opponent sees
    /// in production. The chosen <see cref="RuntimeLegalAction"/> is resolved
    /// back to the engine's own advertised <see cref="LegalAction"/> by action
    /// id, so the request that reaches the router is the engine's own object and
    /// no payload is ever synthesised here.
    /// </summary>
    internal sealed class MatrixPolicy : IMatchPolicy
    {
        private readonly AdvertisedActionPolicy _policy;
        private readonly IPlaystyle _playstyle;

        public MatrixPolicy(IPlaystyle playstyle)
        {
            _playstyle = playstyle ?? throw new ArgumentNullException(nameof(playstyle));
            _policy = new AdvertisedActionPolicy(playstyle);
        }

        public string Describe => "shipped AdvertisedActionPolicy + PlaystyleRegistry id '" + _playstyle.Id + "'";

        /// <summary>
        /// Times an advertised action id was duplicated and the second copy was
        /// dropped by the engine-defect workaround in ChooseThroughBoundary.
        /// </summary>
        public static long DuplicateAdvertisementDrops { get; set; }

        /// <summary>Chooses from a de-duplicated advertisement list under this playstyle's weights.</summary>
        private void TryChooseFromUnique(
            IReadOnlyList<RuntimeLegalAction> unique,
            RuntimeSnapshotEnvelope snapshot,
            int actor,
            out RuntimeLegalAction? chosen)
        {
            chosen = null;
            foreach (var candidate in WeightedPlaystyle.Order(_playstyle, snapshot, unique))
            {
                if (candidate.Actor != actor) continue;
                chosen = candidate;
                return;
            }
        }

        public string? PlaystyleId => _playstyle.Id;
        public string? PlaystyleFingerprint => WeightedPlaystyle.WeightFingerprint(_playstyle);

        /// <summary>
        /// The snapshot revision the chosen action was advertised at. The adapter
        /// boundary requires a submission's SnapshotRevision to equal the
        /// advertised one exactly (RuntimeContractV131ActionBoundary.cs:152-153
        /// and :172-175), so the driver submits against this revision rather than
        /// the gateway's current one.
        /// </summary>
        public long LastChosenSnapshotRevision { get; private set; }

        public long SnapshotRevision { get; set; }

        /// <summary>
        /// The exact advertisement the shipped policy chose, so the driver can
        /// submit it verbatim (payload included) instead of rebuilding it.
        /// </summary>
        public RuntimeLegalAction? LastChosenAdvertisement { get; private set; }

        /// <summary>The snapshot the chosen advertisement came from.</summary>
        public RuntimeSnapshotEnvelope? LastChosenSnapshot { get; private set; }

        public EngineLegalAction? ChooseAmbush(GameState state, IReadOnlyList<EngineLegalAction> actions)
            => ChooseThroughBoundary(state, actions, "AMBUSH");

        public EngineLegalAction? ChooseAction(GameState state, IReadOnlyList<EngineLegalAction> actions)
            => ChooseThroughBoundary(state, actions, "ACTION");

        public IReadOnlyList<long> ChooseDiscards(GameState state, int actor, IReadOnlyList<long> candidates, int required)
        {
            if (required <= 0 || candidates.Count == 0) return Array.Empty<long>();
            // The boundary forwards the engine's own advertised discard payload
            // untouched; the required candidate list is the engine's, so taking
            // the first `required` entries is exactly what the shipped policy
            // does with the same advertisement.
            return candidates.Take(required).ToArray();
        }

        public void NoteSubmission(EngineLegalAction action, bool accepted)
        {
        }

        /// <summary>
        /// Projects the live state with the SHIPPED projection, asks the
        /// SHIPPED policy, and maps the chosen advertisement back to the
        /// engine's own advertised action object.
        /// </summary>
        private EngineLegalAction? ChooseThroughBoundary(
            GameState state,
            IReadOnlyList<EngineLegalAction> actions,
            string phase)
        {
            if (actions.Count == 0) return null;
            var actor = state.CurrentPlayerIndex;
            var snapshot = RuntimeSnapshotProjection.ToSnapshot(
                state,
                "match_plcsim",
                SnapshotRevision,
                actor,
                TurnFlow.CreateDefault());

            var advertised = snapshot.LegalActions;
            if (advertised is null || advertised.Count == 0) return null;

            // DEFECT WORKAROUND (harness side): PullActionHandler.CreateActionId
            // (src\Engine\Turns\PullActionHandler.cs:21-29) keys a download by
            // carrier+topCard+selectedTarget, so two carriers pulling the same
            // cloud-stack top with different selected targets collide on one
            // action id; RuntimeSnapshotProjection.ToSnapshot
            // (RuntimeContractV131Snapshot.cs:59-66) emits both, and the boundary
            // then rejects with snapshot.duplicate_action_id. The engine owns the
            // ids, so this harness cannot repair them; it drops the duplicates it
            // can detect and counts how often that happens, and the reports say so.
            var duplicateIds = new HashSet<string>(StringComparer.Ordinal);
            var unique = new List<RuntimeLegalAction>(advertised.Count);
            foreach (var candidate in advertised)
            {
                if (duplicateIds.Add(candidate.ActionId)) unique.Add(candidate);
                else DuplicateAdvertisementDrops++;
            }

            RuntimeLegalAction? chosen;
            _policy.TryChoose(snapshot, actor, forceEndTurn: false, out chosen);
            if (chosen is not null && !unique.Contains(chosen))
            {
                TryChooseFromUnique(unique, snapshot, actor, out chosen);
            }

            if (chosen is null) return null;

            LastChosenAdvertisement = chosen;
            LastChosenSnapshot = snapshot;
            LastChosenSnapshotRevision = chosen.SnapshotRevision;

            foreach (var candidate in actions)
            {
                if (string.Equals(candidate.ActionId, chosen.ActionId, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
