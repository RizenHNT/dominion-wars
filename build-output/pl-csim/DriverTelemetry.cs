using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using DominionWars.Engine.Model;
using DominionWars.Engine;
using Newtonsoft.Json;

[assembly: InternalsVisibleTo("WoodPoolProbe")]
[assembly: InternalsVisibleTo("ProbeDriverTests")]

namespace PlCsim
{
    internal abstract class DriverObserver
    {
        public virtual void Decision(GameState state, IReadOnlyList<LegalAction> actions, LegalAction chosen) { }
        public virtual void Submission(SubmissionTrace trace) { }
    }

    internal sealed class SubmissionTrace
    {
        public string phase = string.Empty;
        public int turn;
        public int actor;
        public string action_type = string.Empty;
        public string action_id = string.Empty;
        public bool accepted;
        public string reason = string.Empty;
        public int hand_before;
        public int hand_after_resolution;
        public int effective_limit;
        public int required_count;
        public int expected_required;
        public int selected_count;
        public int discarded_event_count;
        public bool discard_protocol_valid;
    }

    internal static class DriverArtifacts
    {
        public static bool IsValid(GameResult r) => r.Decided && r.Exception is null && !r.Capped
            && r.DriverGuardHits == 0 && r.ForcedPhaseAdvances == 0 && r.DiscardViolations == 0;

        public static object MatchRecord(DeckSpec a, DeckSpec b, ulong seed, int first, GameResult r) => new
        {
            player0 = a.Name, leader0 = a.Leader, player1 = b.Name, leader1 = b.Leader,
            seed, first_player = first, valid = IsValid(r), decided = r.Decided, capped = r.Capped,
            winner = r.WinnerIndex, reason = r.Reason, turns = r.Turns,
            max_sealed_health = r.MaxSealedHealth, wood_root = r.WoodRootStacks, wood_rampant = r.WoodRampantStacks,
            decision_count = r.DecisionCount, max_decision_hand = r.MaxDecisionHand,
            max_decision_options = r.MaxDecisionOptions, discard_submissions = r.DiscardSubmissions,
            discard_accepted = r.DiscardAccepted, discard_required_cards = r.DiscardRequiredCards,
            discard_violations = r.DiscardViolations, rejections = r.Counters.Rejections,
            guard_hits = r.DriverGuardHits, forced_advances = r.ForcedPhaseAdvances,
            exception = r.Exception, exception_detail = r.ExceptionDetail,
        };

        public static void AppendMatch(string path, DeckSpec a, DeckSpec b, ulong seed, int first, GameResult r)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.AppendAllText(path, JsonConvert.SerializeObject(MatchRecord(a, b, seed, first, r)) + Environment.NewLine);
        }
    }
}
