using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DominionWars.Data;
using DominionWars.Engine.Model;
using DominionWars.Engine;
using DominionWars.Engine.Rules;
using Newtonsoft.Json;
using PlCsim;

namespace WoodPoolProbe
{
    internal static class ProbeRunner
    {
        internal static int Run(string[] args)
        {
            try { return RunCore(args); }
            catch (Exception e) { Console.Error.WriteLine(e); return 2; }
        }

        private static int RunCore(string[] args)
        {
            if (args.Length < 2) throw new ArgumentException("WoodPoolProbe <cards-dir> <decks-dir> [games=20] [T1] [--seed N] [--playstyle default] [--both-seats] [--out dir] [--log-decisions] [--log-limit N]");
            var root = ProvenanceGuard.FindRepositoryRoot();
            var cardsDir = Path.GetFullPath(args[0]);
            var decksDir = Path.GetFullPath(args[1]);
            var i = 2;
            var games = i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal) ? int.Parse(args[i++], CultureInfo.InvariantCulture) : 20;
            var balance = BalanceTable.Load(Path.Combine(root, "data", "balance.json"));
            if (!balance.IsLoaded) throw new InvalidDataException(balance.Message);
            var t1 = i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal) ? int.Parse(args[i++], CultureInfo.InvariantCulture) : balance.Rules.MaxPunishResponsesPerRound;
            ulong seedBase = 1;
            var style = "default";
            var both = false;
            var log = false;
            var verbose = false;
            var dumpConditions = false;
            var limit = 2000;
            var output = Path.Combine(root, "build-output", "dynamic", "runs", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture));
            for (; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("missing flag value");
                switch (args[i])
                {
                    case "--seed": seedBase = ulong.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--playstyle": style = Next(); break;
                    case "--both-seats": both = true; break;
                    case "--log-decisions": log = true; break;
                    case "--verbose": verbose = true; break;
                    case "--dump-conditions": dumpConditions = true; break;
                    case "--log-limit": limit = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--out": output = Path.GetFullPath(Next()); break;
                    default: throw new ArgumentException("unknown argument: " + args[i]);
                }
            }
            if (games <= 0 || t1 < 0 || limit < 0) throw new ArgumentOutOfRangeException(nameof(args));
            Directory.CreateDirectory(output);
            if (Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("output directory must be empty: " + output);
            ProvenanceGuard.Verify();
            var catalog = CardCatalog.LoadDirectory(cardsDir);
            if (dumpConditions)
            {
                foreach (var c in catalog.Cards.Values)
                foreach (var hook in new[] { ("onPlayEffects", c.OnPlayEffects), ("punishEffects", c.PunishEffects), ("ambushEffects", c.AmbushEffects), ("chantEffects", c.ChantEffects) })
                foreach (var effect in hook.Item2)
                    if (!string.IsNullOrWhiteSpace(effect.Condition)) Console.WriteLine($"{c.Id}/{hook.Item1}: {effect.Action} condition={effect.Condition}");
                return 0;
            }
            var decks = DeckLoader.LoadDirectory(decksDir).ToList();
            var woods = decks.Where(d => d.Faction == "古木圣地").ToList();
            if (woods.Count == 0) throw new InvalidDataException("no Wood decks");
            var foes = DeckLoader.LoadDirectory(Path.Combine(root, "data", "decks")).Where(d => d.Faction != "古木圣地").ToList();
            foreach (var foe in foes)
                if (!decks.Any(d => d.Leader == foe.Leader && d.Name == foe.Name)) decks.Add(foe);
            foreach (var deck in decks)
                foreach (var id in deck.Cards.Keys.Concat(new[] { deck.Leader }))
                    if (!catalog.Cards.ContainsKey(id)) throw new InvalidDataException("missing " + id + " in " + deck.Name);
            var opts = SimOptions.Parse(new[] { "--games", games.ToString(CultureInfo.InvariantCulture), "--seed", seedBase.ToString(CultureInfo.InvariantCulture),
                "--playstyle", style, "--max-punish-responses-per-round", t1.ToString(CultureInfo.InvariantCulture) });
            var r = balance.Rules;
            opts.Rules = new MatchRules(r.HandLimit, r.PioneerHandLimitBonus, r.PioneerOpponentPunishBonus, r.PioneerSelfPunishDiscount, t1);
            using var decisions = log ? new StreamWriter(Path.Combine(output, "decisions.jsonl"), false, new UTF8Encoding(false)) : null;
            using var submissions = new StreamWriter(Path.Combine(output, "submissions.jsonl"), false, new UTF8Encoding(false));
            var sink = new Telemetry(decisions, submissions, limit);
            opts.ObserverFactory = (seed, first, a, b) => new ProbeObserver(sink, seed, first, a, b);
            File.WriteAllText(Path.Combine(output, "run.json"), JsonConvert.SerializeObject(new
            {
                utc = DateTime.UtcNow, cards_dir = cardsDir, decks_dir = decksDir, games_per_pair = games, seed_base = seedBase,
                playstyle = opts.Playstyle, both_seats = both, rules = opts.Rules,
                driver = typeof(Simulator).Assembly.Location, driver_sha256 = ProvenanceGuard.HashOf(typeof(Simulator).Assembly.Location),
                card_files = HashFiles(cardsDir), deck_files = HashFiles(decksDir), opponent_files = HashFiles(Path.Combine(root, "data", "decks")),
                balance_sha256 = ProvenanceGuard.HashOf(balance.Path), log_limit = limit,
                hand_limit_timing = "DISCARD at own turn end; transient ACTION overflow is legal",
            }, Formatting.Indented));
            Console.WriteLine($"Shared driver={typeof(Simulator).Assembly.Location}; cards={catalog.Cards.Count}; T1={t1}; handLimit={r.HandLimit}+{r.PioneerHandLimitBonus}; playstyle={opts.Playstyle}");
            Console.WriteLine("output: " + output);
            var summaries = new List<object>();
            var invalidTotal = 0;
            foreach (var wood in woods)
            {
                var results = new List<GameResult>();
                var wins = 0;
                foreach (var foe in foes)
                for (var seat = 0; seat < (both ? 2 : 1); seat++)
                for (var game = 0; game < games; game++)
                {
                    var a = seat == 0 ? wood : foe;
                    var b = seat == 0 ? foe : wood;
                    var ai = decks.FindIndex(d => d.Leader == a.Leader && d.Name == a.Name);
                    var bi = decks.FindIndex(d => d.Leader == b.Leader && d.Name == b.Name);
                    var seed = Simulator.SeedFor(ai, bi, game, seedBase);
                    var result = RunOne(catalog, a, b, seed, game % 2, opts);
                    DriverArtifacts.AppendMatch(Path.Combine(output, "matches.jsonl"), Spec(a), Spec(b), seed, game % 2, result);
                    results.Add(result);
                    if (verbose) Console.WriteLine($"{a.Name} vs {b.Name} seed={seed} first={game % 2} turns={result.Turns} valid={DriverArtifacts.IsValid(result)} reason={result.Reason}");
                    if (DriverArtifacts.IsValid(result) && result.WinnerIndex == seat) wins++;
                }
                var valid = results.Where(DriverArtifacts.IsValid).ToArray();
                var invalid = results.Count - valid.Length;
                invalidTotal += invalid;
                var rate = valid.Length == 0 ? (double?)null : 100.0 * wins / valid.Length;
                var avg = valid.Length == 0 ? (double?)null : valid.Average(x => x.Turns);
                summaries.Add(new { deck = wood.Name, main_cards = wood.Cards.Values.Sum(), attempted = results.Count, valid = valid.Length, invalid,
                    wins, win_rate = rate, average_turns = avg, reached_512 = valid.Count(x => x.MaxSealedHealth >= 512),
                    max_decision_hand = results.Max(x => x.MaxDecisionHand), max_decision_options = results.Max(x => x.MaxDecisionOptions),
                    discard_submissions = results.Sum(x => x.DiscardSubmissions), discard_accepted = results.Sum(x => x.DiscardAccepted),
                    discarded_cards = results.Sum(x => x.DiscardRequiredCards), discard_violations = results.Sum(x => x.DiscardViolations) });
                Console.WriteLine($"{wood.Name}: valid={valid.Length}/{results.Count}, invalid={invalid}, wins={wins}, winRate={rate:F1}%, avgTurns={avg:F2}");
            }
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonConvert.SerializeObject(new
            { validation_ok = invalidTotal == 0, invalid_games = invalidTotal, decision_rows = sink.Logged, decision_rows_omitted = sink.Omitted, decks = summaries }, Formatting.Indented));
            return invalidTotal == 0 ? 0 : 3;
        }

        private static object[] HashFiles(string dir) => Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.Ordinal)
            .Select(x => (object)new { file = x, sha256 = ProvenanceGuard.HashOf(x) }).ToArray();
        internal static DeckSpec Spec(DeckDefinition d) => new DeckSpec(d.Faction, d.Leader, d.Name, d.Cards);
        internal static GameResult RunOne(CardCatalog c, DeckDefinition a, DeckDefinition b, ulong seed, int first, SimOptions opts)
        {
            try { return new SingleMatch(c, Spec(a), Spec(b), seed, first, opts).Play(); }
            catch (Exception e) { return new GameResult { Exception = e.GetType().Name + ": " + e.Message, ExceptionDetail = e.ToString() }; }
        }
    }

    internal sealed class Telemetry
    {
        public readonly StreamWriter? Decisions;
        public readonly StreamWriter Submissions;
        public readonly int Limit;
        public int Logged, Omitted;
        public Telemetry(StreamWriter? decisions, StreamWriter submissions, int limit) { Decisions = decisions; Submissions = submissions; Limit = limit; }
    }

    internal sealed class ProbeObserver : DriverObserver
    {
        private readonly Telemetry _sink;
        private readonly ulong _seed;
        private readonly int _first;
        private readonly string _a, _b;
        public ProbeObserver(Telemetry sink, ulong seed, int first, DeckSpec a, DeckSpec b)
        { _sink = sink; _seed = seed; _first = first; _a = a.Name; _b = b.Name; }
        public override void Decision(GameState state, IReadOnlyList<LegalAction> actions, LegalAction chosen)
        {
            if (_sink.Decisions is null) return;
            if (_sink.Logged >= _sink.Limit) { _sink.Omitted++; return; }
            _sink.Logged++;
            var actor = state.CurrentPlayerIndex;
            var me = state.GetPlayer(actor);
            var opp = state.GetOpponent(actor);
            var ordered = new[] { chosen }.Concat(actions.Where(x => x.ActionId != chosen.ActionId)).ToArray();
            _sink.Decisions.WriteLine(JsonConvert.SerializeObject(new
            {
                seed = _seed, first_player = _first, player0 = _a, player1 = _b,
                phase = state.Turn.PhaseId, turn = state.Turn.Number, actor,
                my_life = me.Life, opp_life = opp.Life, my_hand_count = me.Hand.Count, opp_hand_count = opp.Hand.Count,
                my_deck = me.Deck.Count, opp_deck = opp.Deck.Count, my_field = Field(me), opp_field = Field(opp),
                my_hand = me.Hand.Select(x => x.Definition.Name), my_root = me.RootStacks, my_rampant = me.RampantStacks,
                my_punish_delta = me.PunishDeltaThisTurn, my_sealed = me.Field.Count(x => x.IsMinion && x.IsAlive && x.Sealed), castle_hp = state.CastleHealth,
                option_ids = ordered.Select(x => x.Type + "/" + x.ActionId), options = ordered.Select(x => Describe(x, state)),
                ai_top = chosen.Type + "/" + chosen.ActionId, candidate_order = "actual_choice_first_then_engine_order", raw_option_count = actions.Count,
            }));
        }
        public override void Submission(SubmissionTrace t) => _sink.Submissions.WriteLine(JsonConvert.SerializeObject(new
            { seed = _seed, first_player = _first, player0 = _a, player1 = _b, submission = t }));
        private static string Field(PlayerState p) => string.Join("，", p.Field.Where(c => c.IsMinion)
            .Select(c => c.Definition.Name + " " + c.Attack + "/" + c.Health + (c.Sealed ? "(封印)" : "")));
        private static string Describe(LegalAction a, GameState state)
        {
            var card = a.SourceId.HasValue ? state.FindEntity(a.SourceId.Value)?.Definition.Name : a.CardId;
            var target = a.TargetReferenceId ?? (a.TargetId.HasValue ? "#" + a.TargetId : "");
            var cost = a.Payload is not null && a.Payload.TryGetValue("punish", out var p) ? "（惩罚 " + p + "）" : "";
            return a.Type + " " + card + cost + (string.IsNullOrEmpty(target) ? "" : " → " + target);
        }
    }
}
