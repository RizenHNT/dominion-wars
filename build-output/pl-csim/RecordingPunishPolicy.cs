using System;
using System.Collections.Generic;
using System.Globalization;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;

namespace PlCsim
{
    /// <summary>
    /// Wraps the harness punish response policy, records what the chain did, and
    /// optionally enforces T1 ("one punish response per punish round").
    ///
    /// T1 is a POLICY EMULATION of a rule change, not a rules change: the engine
    /// keeps offering every response and this policy simply declines the ones
    /// past the limit. Because a punish response is voluntary, the resulting
    /// game states are identical to an engine that stopped offering them - the
    /// only difference is that the engine still emits the offer and the
    /// PUNISH_TRIGGERED event for the accepted one.
    ///
    /// A "round" is the engine's own unit: all responses hanging off one root
    /// action event (CARD_PLAYED / COMMIT_DECLARED / PULL_RESOLVED). The root of
    /// the round in progress is read from the event log exactly the way
    /// ScanDrawEvents anchors PUNISH_DRAW: the live chain's root is the parent
    /// of the most recent PUNISH_TRIGGERED event
    /// (src\Engine\Turns\PlayCardActionHandler.cs:335 appends it with
    /// rootEventId as its parent), which is re-derived on every decision
    /// because ContinueAfterPunish reuses that same root id.
    /// </summary>
    internal sealed class RecordingPunishPolicy : IPunishResponsePolicy
    {
        private readonly bool _accept;
        private readonly int _maxResponsesPerRound;
        private readonly IPunishResponsePolicy? _inner;

        public RecordingPunishPolicy(bool accept, int maxResponsesPerRound = 0)
        {
            _accept = accept;
            _maxResponsesPerRound = maxResponsesPerRound;
        }

        /// <summary>
        /// Wraps a policy supplied by the shipped registry (each playstyle owns
        /// its own response stance via PlaystyleRegistry.PunishResponsesFor), so
        /// the decision stays the shipped one and this type only records it.
        /// </summary>
        public RecordingPunishPolicy(IPunishResponsePolicy inner, int maxResponsesPerRound = 0)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _accept = true;
            _maxResponsesPerRound = maxResponsesPerRound;
        }

        public int DecideCalls { get; private set; }
        public int Accepted { get; private set; }
        /// <summary>Offers declined specifically by the T1 per-round cap.</summary>
        public int DeclinedByRoundCap { get; private set; }
        public int MaxChainDepthSeen { get; private set; }
        public readonly Dictionary<int, int> DepthHistogram = new Dictionary<int, int>();
        public readonly Dictionary<int, int> PunishAmountHistogram = new Dictionary<int, int>();

        public PunishResponseDecision Decide(GameState state, CardInstance card, int effectivePunish, int chainDepth)
        {
            DecideCalls++;
            if (chainDepth > MaxChainDepthSeen) MaxChainDepthSeen = chainDepth;
            DepthHistogram[chainDepth] = DepthHistogram.TryGetValue(chainDepth, out var depthCount)
                ? depthCount + 1
                : 1;
            PunishAmountHistogram[effectivePunish] = PunishAmountHistogram.TryGetValue(effectivePunish, out var amountCount)
                ? amountCount + 1
                : 1;
            if (_inner is not null)
            {
                var innerDecision = _inner.Decide(state, card, effectivePunish, chainDepth);
                if (innerDecision.Activate)
                {
                    Accepted++;
                }

                return innerDecision;
            }

            if (!_accept)
            {
                return PunishResponseDecision.Decline();
            }

            if (_maxResponsesPerRound > 0)
            {
                var root = CurrentRoundRoot(state);
                _responsesByRound.TryGetValue(root, out var used);
                if (used >= _maxResponsesPerRound)
                {
                    DeclinedByRoundCap++;
                    return PunishResponseDecision.Decline();
                }

                // Count the acceptance at decision time rather than waiting for
                // the PUNISH_TRIGGERED event: a response that is later rejected
                // during preparation never emits that event, and the cap must
                // still have been spent.
                _responsesByRound[root] = used + 1;
            }

            Accepted++;
            return PunishResponseDecision.Accept();
        }

        private readonly Dictionary<long, int> _responsesByRound = new Dictionary<long, int>();

        /// <summary>
        /// Root event id of the punish round currently resolving. Walks the
        /// event log backwards to the newest PUNISH_TRIGGERED event; its parent
        /// is the round's root action event.
        /// </summary>
        internal static long CurrentRoundRoot(GameState state)
        {
            var events = state.Events.Items;
            for (var index = events.Count - 1; index >= 0; index--)
            {
                if (string.Equals(events[index].EventType, "PUNISH_TRIGGERED", StringComparison.Ordinal))
                {
                    return events[index].ParentEventId ?? 0;
                }
            }

            return 0;
        }


        public static string HistogramText(Dictionary<int, int> histogram)
        {
            var parts = new List<string>();
            foreach (var pair in new SortedDictionary<int, int>(histogram))
            {
                parts.Add($"{pair.Key}:{pair.Value.ToString(CultureInfo.InvariantCulture)}");
            }

            return parts.Count == 0 ? "(none)" : string.Join(" ", parts);
        }
    }

    /// <summary>Per-faction behavioural counters, used to check policy symmetry.</summary>
    internal sealed class FactionBehavior
    {
        public long Plays;
        public long PlaysPunishActivated;
        public long Attacks;
        public long Commits;
        public long Pulls;
        public long Ambushes;
        public long PunishActivationsTaken;
        public long PunishActivationsDeclinedByBudget;
        public long BuffCardsPlayed;
        public long TurnsTaken;
        public long AttackDamageDealt;
    }
}
