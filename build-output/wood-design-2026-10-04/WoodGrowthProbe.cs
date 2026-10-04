/*
 * WoodGrowthProbe.cs -- 古木圣地 (wood) growth-axis timeline probe.
 *
 * WHAT THIS IS
 *   A thin observer-only wrapper around the SHARED driver entry point
 *   `PlCsim.SingleMatch` (build-output/pl-csim/Simulator.cs, line ~728). It does NOT
 *   contain a phase loop of its own: every match is driven by SingleMatch.Play(), the
 *   same code the production WoodPoolProbe/PlCsim CLIs use.
 *
 *   The only thing it adds is a DriverObserver that, at every decision point and every
 *   action-resolution point, snapshots for the wood seat:
 *       root stacks / rampant stacks / largest sealed minion health / sealed count /
 *       hand / deck / life / field listing
 *   plus a drain of the engine event log (ROOT_STACKS_ADDED, RAMPANT_STACKS_ADDED,
 *   BUFF_APPLIED, VICTORY_PROGRESS, TURN_CHANGED) so every amplified BUFF amount the
 *   engine actually computed is recorded verbatim.
 *
 *   Sampling caveat (stated in the report): samples are taken at driver callbacks, so an
 *   END-phase effect (e.g. wood_seed's chant ADD_ROOT) is first visible in the NEXT turn's
 *   first sample. Rows are keyed by the turn number at sample time; contents are the
 *   state AFTER the resolution that produced the callback.
 *
 * BUILD + RUN (from the repository root):
 *   powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `
 *       build-output\wood-design-2026-10-04\build-growth-probe.ps1
 *   dotnet build-output\wood-design-2026-10-04\probe\WoodPoolProbe.dll `
 *       data\cards data\decks 10 1 --seed 1 --out build-output\wood-design-2026-10-04\runs\timeline-01
 *
 *   The output assembly is deliberately named WoodPoolProbe.dll because
 *   build-output/pl-csim/DriverTelemetry.cs grants internals access to that assembly name
 *   (`[assembly: InternalsVisibleTo("WoodPoolProbe")]`); SingleMatch/SimOptions/
 *   DriverObserver are internal to PlCsim.dll.
 *
 * OUTPUT (inside --out):
 *   games.jsonl  -- one row per match
 *   turns.jsonl  -- one row per match x turn
 *   events.jsonl -- growth-relevant engine events, in emission order
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DominionWars.Data;
using DominionWars.Engine;
using DominionWars.Engine.Model;
using DominionWars.Engine.Rules;
using Newtonsoft.Json;
using PlCsim;

namespace WoodGrowthProbe
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try { return Run(args); }
            catch (Exception e) { Console.Error.WriteLine(e); return 2; }
        }

        private static int Run(string[] args)
        {
            if (args.Length < 2)
                throw new ArgumentException(
                    "WoodPoolProbe <cards-dir> <decks-dir> [games=20] [T1] [--seed N] [--playstyle default] [--both-seats] [--out dir]");

            var cardsDir = Path.GetFullPath(args[0]);
            var decksDir = Path.GetFullPath(args[1]);
            var i = 2;
            var games = i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal)
                ? int.Parse(args[i++], CultureInfo.InvariantCulture) : 20;

            var root = FindRepositoryRoot();
            var balance = BalanceTable.Load(Path.Combine(root, "data", "balance.json"));
            if (!balance.IsLoaded) throw new InvalidDataException(balance.Message);
            var t1 = i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal)
                ? int.Parse(args[i++], CultureInfo.InvariantCulture) : balance.Rules.MaxPunishResponsesPerRound;

            ulong seedBase = 1;
            var style = "default";
            var both = false;
            var output = Path.Combine(root, "build-output", "wood-design-2026-10-04", "runs",
                "timeline-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            for (; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("missing flag value");
                switch (args[i])
                {
                    case "--seed": seedBase = ulong.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--playstyle": style = Next(); break;
                    case "--both-seats": both = true; break;
                    case "--out": output = Path.GetFullPath(Next()); break;
                    default: throw new ArgumentException("unknown argument: " + args[i]);
                }
            }

            Directory.CreateDirectory(output);
            if (Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("output directory must be empty: " + output);

            var catalog = CardCatalog.LoadDirectory(cardsDir);
            var decks = DeckLoader.LoadDirectory(decksDir).ToList();
            var woods = decks.Where(d => d.Faction == "古木圣地").ToList();
            if (woods.Count == 0) throw new InvalidDataException("no Wood decks in " + decksDir);
            var foes = DeckLoader.LoadDirectory(Path.Combine(root, "data", "decks"))
                .Where(d => d.Faction != "古木圣地").ToList();

            var opts = SimOptions.Parse(new[]
            {
                "--games", games.ToString(CultureInfo.InvariantCulture),
                "--seed", seedBase.ToString(CultureInfo.InvariantCulture),
                "--playstyle", style,
                "--max-punish-responses-per-round", t1.ToString(CultureInfo.InvariantCulture),
            });
            var r = balance.Rules;
            opts.Rules = new MatchRules(r.HandLimit, r.PioneerHandLimitBonus, r.PioneerOpponentPunishBonus,
                r.PioneerSelfPunishDiscount, t1);
            opts.ObserverFactory = (seed, first, a, b) => new GrowthObserver(seed, first, a, b);

            Console.WriteLine($"driver={typeof(Simulator).Assembly.Location}");
            Console.WriteLine($"driver_sha256={Sha256(typeof(Simulator).Assembly.Location)}");
            Console.WriteLine($"engine_sha256={Sha256(Path.Combine(AppContext.BaseDirectory, "DominionWars.Engine.dll"))}");
            Console.WriteLine($"cards={catalog.Cards.Count} T1={t1} handLimit={r.HandLimit}+{r.PioneerHandLimitBonus} playstyle={opts.Playstyle}");
            Console.WriteLine("output: " + output);

            using var gamesFile = new StreamWriter(Path.Combine(output, "games.jsonl"), false, new UTF8Encoding(false));
            using var turnsFile = new StreamWriter(Path.Combine(output, "turns.jsonl"), false, new UTF8Encoding(false));
            using var eventsFile = new StreamWriter(Path.Combine(output, "events.jsonl"), false, new UTF8Encoding(false));
            using var submissionsFile = new StreamWriter(Path.Combine(output, "submissions.jsonl"), false, new UTF8Encoding(false));
            using var playsFile = new StreamWriter(Path.Combine(output, "plays.jsonl"), false, new UTF8Encoding(false));

            var attempted = 0;
            var observed = 0;
            var validCount = 0;
            var invalidCount = 0;
            var observerMissing = 0;
            var validWoodWins = 0;
            var validTurnsTotal = 0;
            var validReached512Observed = 0;
            var reached512 = 0;
            var giantWins = 0;
            var validReasonCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var invalidReasonCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var wood in woods)
            {
                foreach (var foe in foes)
                for (var seat = 0; seat < (both ? 2 : 1); seat++)
                for (var game = 0; game < games; game++)
                {
                    attempted++;
                    var a = seat == 0 ? wood : foe;
                    var b = seat == 0 ? foe : wood;
                    var ai = decks.FindIndex(d => d.Leader == a.Leader && d.Name == a.Name);
                    var bi = decks.FindIndex(d => d.Leader == b.Leader && d.Name == b.Name);
                    if (ai < 0) ai = 0;
                    if (bi < 0) bi = 0;
                    var seed = Simulator.SeedFor(ai, bi, game, seedBase);
                    var first = game % 2;

                    GrowthObserver? observer = null;
                    opts.ObserverFactory = (s, f, x, y) => observer = new GrowthObserver(s, f, x, y);
                    GameResult result;
                    try
                    {
                        result = new SingleMatch(catalog, Spec(a), Spec(b), seed, first, opts).Play();
                    }
                    catch (Exception e)
                    {
                        result = new GameResult { Exception = e.GetType().Name + ": " + e.Message, ExceptionDetail = e.ToString() };
                    }

                    var valid = DriverArtifacts.IsValid(result);
                    var invalidReason = valid ? null : InvalidReason(result);
                    var expectedWoodSeat = a.Faction == "古木圣地" ? 0 : 1;
                    if (valid)
                    {
                        validCount++;
                        if (result.WinnerIndex == expectedWoodSeat) validWoodWins++;
                        validTurnsTotal += result.Turns;
                        Increment(validReasonCounts, result.Reason ?? "<null>");
                    }
                    else
                    {
                        invalidCount++;
                        Increment(invalidReasonCounts, invalidReason ?? "invalid.unknown");
                    }

                    if (observer is null)
                    {
                        observerMissing++;
                        gamesFile.WriteLine(JsonConvert.SerializeObject(new
                        {
                            seed, first, player0 = a.Name, leader0 = a.Leader, player1 = b.Name, leader1 = b.Leader,
                            wood_seat = expectedWoodSeat, wood_name = wood.Name,
                            turns = result.Turns, winner = result.WinnerIndex, reason = result.Reason,
                            decided = result.Decided, capped = result.Capped, exception = result.Exception,
                            valid, invalid_reason = invalidReason, observer_missing = true,
                            wood_won = result.WinnerIndex == expectedWoodSeat,
                            driver_guard_hits = result.DriverGuardHits, driver_guard_reason = result.DriverGuardReason,
                            forced_phase_advances = result.ForcedPhaseAdvances,
                            discard_violations = result.DiscardViolations,
                        }));
                        continue;
                    }

                    var obs = (GrowthObserver)observer!;
                    obs.Flush();
                    observed++;

                    var rows = obs.Rows;
                    var bestEver = 0;
                    var firstTurnAt512 = 0;
                    var bestName = string.Empty;
                    foreach (var row in rows)
                    {
                        if (row.maxSealed > bestEver)
                        {
                            bestEver = row.maxSealed;
                            bestName = row.maxSealedName;
                        }
                        if (firstTurnAt512 == 0 && bestEver >= 512) firstTurnAt512 = row.turn;
                    }

                    var woodSeat = obs.WoodSeat;
                    var giantWin = (result.Reason ?? string.Empty) == "win.giant_health_ge";
                    if (bestEver >= 512) reached512++;
                    if (giantWin) giantWins++;
                    if (valid && bestEver >= 512) validReached512Observed++;

                    gamesFile.WriteLine(JsonConvert.SerializeObject(new
                    {
                        seed, first, player0 = a.Name, leader0 = a.Leader, player1 = b.Name, leader1 = b.Leader,
                        wood_seat = woodSeat, wood_name = wood.Name,
                        turns = result.Turns, winner = result.WinnerIndex, reason = result.Reason,
                        decided = result.Decided, capped = result.Capped, exception = result.Exception,
                        valid, invalid_reason = invalidReason, observer_missing = false,
                        driver_guard_hits = result.DriverGuardHits, driver_guard_reason = result.DriverGuardReason,
                        forced_phase_advances = result.ForcedPhaseAdvances,
                        discard_violations = result.DiscardViolations,
                        wood_won = result.WinnerIndex == woodSeat,
                        giant_health_win = giantWin,
                        reached_512_observed = bestEver >= 512,
                        max_sealed_health_observed = bestEver,
                        max_sealed_minion = bestName,
                        first_turn_at_or_above_512 = firstTurnAt512,
                        result_max_sealed_health = result.MaxSealedHealth,
                        final_root = result.WoodRootStacks, final_rampant = result.WoodRampantStacks,
                        wood_root_at_turn5 = result.WoodRootAtTurn5, wood_root_at_turn10 = result.WoodRootAtTurn10,
                    }));

                    foreach (var row in rows)
                    {
                        turnsFile.WriteLine(JsonConvert.SerializeObject(new
                        {
                            seed, first, player0 = a.Name, player1 = b.Name, wood_seat = woodSeat,
                            turn = row.turn, samples = row.samples,
                            root = row.root, rampant = row.rampant,
                            opp_root = row.oppRoot, opp_rampant = row.oppRampant,
                            max_sealed_health = row.maxSealed, max_sealed_minion = row.maxSealedName,
                            last_sealed_health = row.lastSealed, sealed_count = row.sealedCount,
                            minion_count = row.minionCount,
                            end_action_root = row.endActionRoot, end_action_rampant = row.endActionRampant,
                            end_action_sealed_health = row.endActionSealed,
                            end_action_field = row.endActionField,
                            wood_hand = row.hand, wood_deck = row.deck, opp_deck = row.oppDeck,
                            wood_life = row.life, opp_life = row.oppLife,
                            wood_plays = row.plays, wood_buff_events = row.buffs,
                        }));
                    }

                    foreach (var e in obs.Events) eventsFile.WriteLine(e);
                    foreach (var s in obs.Submissions) submissionsFile.WriteLine(s);
                    foreach (var p in obs.Plays) playsFile.WriteLine(JsonConvert.SerializeObject(p));
                    Console.WriteLine($"{a.Name} vs {b.Name} seed={seed} turns={result.Turns} reason={result.Reason} " +
                                      $"maxSealed={bestEver} root={result.WoodRootStacks} rampant={result.WoodRampantStacks} " +
                                      $"reached512={bestEver >= 512}");
                }
            }

            File.WriteAllText(Path.Combine(output, "summary.json"), JsonConvert.SerializeObject(new
            {
                utc = DateTime.UtcNow,
                matches = attempted,
                attempted_matches = attempted,
                observed_matches = observed,
                valid_matches = validCount,
                invalid_matches = invalidCount,
                observer_missing_matches = observerMissing,
                valid_wood_wins = validWoodWins,
                valid_wood_win_rate = validCount == 0 ? (double?)null : validWoodWins / (double)validCount,
                average_turns_valid = validCount == 0 ? (double?)null : validTurnsTotal / (double)validCount,
                valid_reason_counts = validReasonCounts,
                invalid_reason_counts = invalidReasonCounts,
                valid_reached_512_observed_matches = validReached512Observed,
                reached_512_observed = reached512,
                giant_health_ge_wins = giantWins,
                driver = typeof(Simulator).Assembly.Location,
                driver_sha256 = Sha256(typeof(Simulator).Assembly.Location),
                engine_sha256 = Sha256(Path.Combine(AppContext.BaseDirectory, "DominionWars.Engine.dll")),
                playstyle = opts.Playstyle,
                t1,
                games_per_pair = games,
                seed_base = seedBase,
                both_seats = both,
                sampling = "samples at driver Decision/Submission callbacks; END-phase effects first visible in the next turn's first sample",
            }, Formatting.Indented));

            return 0;
        }

        private static void Increment(Dictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out var current);
            counts[key] = current + 1;
        }

        private static string InvalidReason(GameResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.Exception)) return "exception: " + result.Exception;
            if (!result.Decided) return "undecided: " + (result.Reason ?? "<null>");
            if (result.Capped) return "capped";
            if (result.DriverGuardHits > 0)
                return "driver_guard: " + (string.IsNullOrWhiteSpace(result.DriverGuardReason) ? "unknown" : result.DriverGuardReason);
            if (result.ForcedPhaseAdvances > 0) return "forced_phase_advances";
            if (result.DiscardViolations > 0) return "discard_violations";
            return "invalid.unknown";
        }

        internal static DeckSpec Spec(DeckDefinition d) => new DeckSpec(d.Faction, d.Leader, d.Name, d.Cards);

        internal static string FindRepositoryRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "data", "cards"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("repository root not found");
        }

        internal static string Sha256(string path)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
    }

    internal sealed class Row
    {
        public int turn;
        public int samples;
        public int root = -1;
        public int rampant = -1;
        public int oppRoot = -1;
        public int oppRampant = -1;
        public int maxSealed;
        public string maxSealedName = string.Empty;
        public int lastSealed;
        public int sealedCount;
        public int minionCount;
        public int hand;
        public int deck;
        public int oppDeck;
        public int? life;
        public int? oppLife;
        public int endActionRoot = -1;
        public int endActionRampant = -1;
        public int endActionSealed;
        public string endActionField = string.Empty;
        public readonly List<string> plays = new List<string>();
        public readonly List<string> buffs = new List<string>();
    }

    internal sealed class PlayRow
    {
        public ulong seed;
        public string player0 = string.Empty;
        public string player1 = string.Empty;
        public int turn;
        public string action_id = string.Empty;
        public string card_id = string.Empty;
        public string name = string.Empty;
        public string type = string.Empty;
        public int punish;
        public int punish_cost;
        public bool punish_activatable;
        public bool punish_activated_instance;
        public int chant;
        public int on_play_effects;
        public int punish_effects;
        public bool tags_free;
        public bool accepted;
        public string reason = string.Empty;
        public int handBefore;
        public int handAfter;
        public string events = string.Empty;
    }

    internal sealed class GrowthObserver : DriverObserver
    {
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<string> _events = new List<string>();
        private readonly List<string> _submissions = new List<string>();
        private readonly List<PlayRow> _plays = new List<PlayRow>();
        private PlayRow? _pendingPlay;
        private readonly ulong _seed;
        private readonly int _first;
        private readonly string _a;
        private readonly string _b;
        private int _cursor;
        private GameState? _state;

        public GrowthObserver(ulong seed, int first, DeckSpec a, DeckSpec b)
        {
            _seed = seed;
            _first = first;
            _a = a.Name;
            _b = b.Name;
            WoodSeat = a.Faction == "古木圣地" ? 0 : 1;
        }

        public int WoodSeat { get; }
        public List<Row> Rows => _rows;
        public List<string> Events => _events;
        public List<string> Submissions => _submissions;
        public List<PlayRow> Plays => _plays;

        public override void Decision(GameState state, IReadOnlyList<LegalAction> actions, LegalAction chosen)
        {
            _state = state;
            Drain(state);
            var actor = state.CurrentPlayerIndex;
            Sample(state, actor, endOfAction: actor == WoodSeat && chosen.Type == LegalActionGenerator.EndTurn);
            if (actor == WoodSeat)
            {
                var row = RowFor(state.Turn.Number);
                if (chosen.Type == LegalActionGenerator.PlayCard)
                {
                    row.plays.Add(Describe(state, chosen));
                    var card = chosen.SourceId.HasValue ? state.FindEntity((int)chosen.SourceId.Value) : null;
                    if (card is not null)
                    {
                        _pendingPlay = new PlayRow
                        {
                            seed = _seed,
                            player0 = _a,
                            player1 = _b,
                            turn = state.Turn.Number,
                            action_id = chosen.ActionId,
                            card_id = card.Definition.Id,
                            name = card.Definition.Name,
                            type = card.Definition.Type,
                            punish = card.Definition.Punish,
                            punish_cost = card.Definition.PunishCost,
                            punish_activatable = card.Definition.PunishActivatable,
                            punish_activated_instance = card.PunishActivated,
                            chant = card.Definition.Chant,
                            on_play_effects = card.Definition.OnPlayEffects.Count,
                            punish_effects = card.Definition.PunishEffects.Count,
                            tags_free = !cards_used_contains(state, card),
                        };
                        _plays.Add(_pendingPlay);
                    }
                }
                else if (chosen.Type == LegalActionGenerator.Attack)
                {
                    row.plays.Add("ATTACK " + Name(state, chosen.SourceId) + " -> " + Target(state, chosen));
                }
            }
        }

        private static bool cards_used_contains(GameState state, CardInstance card)
        {
            var used = state.GetPlayer(card.OwnerPlayerIndex).UsedTags;
            foreach (var tag in card.Definition.Tags)
                if (used.Contains(tag)) return true;
            return false;
        }

        public override void Submission(SubmissionTrace trace)
        {
            var state = _state;
            if (state is null) return;
            var emitted = Drain(state);
            RecordSubmission(trace, state, emitted);
            Sample(state, trace.actor, endOfAction: false);
        }

        private void RecordSubmission(SubmissionTrace trace, GameState state, List<string> emitted)
        {
            if (_pendingPlay is not null && trace.actor == WoodSeat && trace.action_type == "PLAY_CARD")
            {
                _pendingPlay.accepted = trace.accepted;
                _pendingPlay.reason = trace.reason;
                _pendingPlay.handBefore = trace.hand_before;
                _pendingPlay.handAfter = trace.hand_after_resolution;
                _pendingPlay.events = string.Join(",", emitted);
                _pendingPlay = null;
            }

            _submissions.Add(JsonConvert.SerializeObject(new
            {
                seed = _seed,
                player0 = _a,
                player1 = _b,
                wood_seat = WoodSeat,
                turn = state.Turn.Number,
                phase = state.Turn.PhaseId,
                actor = trace.actor,
                action_type = trace.action_type,
                action_id = trace.action_id,
                accepted = trace.accepted,
                reason = trace.reason,
                hand_before = trace.hand_before,
                hand_after_resolution = trace.hand_after_resolution,
                events = emitted,
            }));
        }

        /// <summary>Drain whatever the engine logged after the last callback.</summary>
        public void Flush()
        {
            var state = _state;
            if (state is not null) Drain(state);
        }

        private void RowSample(int turn)
        {
            RowFor(turn);
        }

        private Row RowFor(int turn)
        {
            while (_rows.Count < turn) _rows.Add(new Row { turn = _rows.Count + 1 });
            return _rows[turn - 1];
        }

        private void Sample(GameState state, int actor, bool endOfAction)
        {
            var turn = state.Turn.Number;
            RowSample(turn);
            var row = RowFor(turn);
            row.samples++;

            var w = state.GetPlayer(WoodSeat);
            var o = state.GetOpponent(WoodSeat);
            row.root = w.RootStacks;
            row.rampant = w.RampantStacks;
            row.oppRoot = o.RootStacks;
            row.oppRampant = o.RampantStacks;
            row.hand = w.Hand.Count;
            row.deck = w.Deck.Count;
            row.oppDeck = o.Deck.Count;
            row.life = w.Life;
            row.oppLife = o.Life;
            var sealedCount = 0;
            var minions = 0;
            var best = 0;
            var bestName = string.Empty;
            var field = new List<string>();
            foreach (var card in w.Field)
            {
                if (!card.IsMinion) continue;
                minions++;
                field.Add(card.Definition.Name + " " + card.Attack + "/" + card.Health + (card.Sealed ? "[sealed]" : string.Empty));
                if (!card.IsAlive || !card.Sealed) continue;
                sealedCount++;
                if (card.Health > best)
                {
                    best = card.Health;
                    bestName = card.Definition.Name;
                }
            }

            row.sealedCount = sealedCount;
            row.minionCount = minions;
            row.lastSealed = best;
            if (best > row.maxSealed)
            {
                row.maxSealed = best;
                row.maxSealedName = bestName;
            }

            if (endOfAction)
            {
                row.endActionRoot = row.root;
                row.endActionRampant = row.rampant;
                row.endActionSealed = best;
                row.endActionField = string.Join("，", field);
            }
        }

        private List<string> Drain(GameState state)
        {
            var emitted = new List<string>();
            var items = state.Events.Items;
            if (_cursor >= items.Count) return emitted;
            for (var index = _cursor; index < items.Count; index++)
            {
                var e = items[index];
                var emittedType = e.EventType;
                if (e.EventType == "EFFECT_SKIPPED"
                    && e.Data.TryGetValue("action", out var skippedAction)
                    && string.Equals(Convert.ToString(skippedAction, CultureInfo.InvariantCulture), "CARD_EFFECTS", StringComparison.Ordinal)
                    && e.Data.TryGetValue("reasonKey", out var skippedReason)
                    && string.Equals(Convert.ToString(skippedReason, CultureInfo.InvariantCulture), "effect.no_effects", StringComparison.Ordinal))
                {
                    emittedType += ":effect.no_effects";
                }
                emitted.Add(emittedType);
                switch (e.EventType)
                {
                    case "ROOT_STACKS_ADDED":
                    case "RAMPANT_STACKS_ADDED":
                    case "BUFF_APPLIED":
                    case "VICTORY_PROGRESS":
                    case "TURN_CHANGED":
                    case "LEADER_MANIFESTED":
                    case "CARD_PLAYED":
                    case "EFFECT_SKIPPED":
                    case "AMBUSH_TRIGGERED":
                        break;
                    default:
                        continue;
                }

                var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["seed"] = _seed,                    ["player0"] = _a,
                    ["player1"] = _b,
                    ["event_id"] = e.EventId,
                    ["type"] = e.EventType,
                    ["turn"] = state.Turn.Number,
                    ["phase"] = state.Turn.PhaseId,
                    ["actor"] = state.CurrentPlayerIndex,
                };
                foreach (var entry in e.Data) payload["d_" + entry.Key] = entry.Value;
                if (e.EventType == "BUFF_APPLIED")
                {
                    var targetId = Convert.ToInt32(e.Data["target"], CultureInfo.InvariantCulture);
                    var target = state.FindEntity(targetId);
                    payload["target_name"] = target?.Definition.Name;
                    payload["target_owner"] = target?.OwnerPlayerIndex;
                }

                _events.Add(JsonConvert.SerializeObject(payload));
                if (e.EventType == "BUFF_APPLIED")
                {
                    var turn = state.Turn.Number;
                    RowFor(turn).buffs.Add(
                        "base=" + Convert.ToString(e.Data["baseAmount"], CultureInfo.InvariantCulture) +
                        " amount=" + Convert.ToString(e.Data["amount"], CultureInfo.InvariantCulture) +
                        " sealed=" + Convert.ToString(e.Data["sealed"], CultureInfo.InvariantCulture) +
                        " target=" + Convert.ToString(payload["target_name"], CultureInfo.InvariantCulture));
                }
            }

            _cursor = items.Count;
            return emitted;
        }

        private static string Describe(GameState state, LegalAction action)
        {
            return "PLAY " + Name(state, action.SourceId) + " -> " + Target(state, action);
        }

        private static string Name(GameState state, long? id)
        {
            if (!id.HasValue) return "?";
            var card = state.FindEntity((int)id.Value);
            return card?.Definition.Name ?? ("#" + id.Value.ToString(CultureInfo.InvariantCulture));
        }

        private static string Target(GameState state, LegalAction action)
        {
            if (action.TargetId.HasValue) return Name(state, action.TargetId);
            return action.TargetReferenceId ?? string.Empty;
        }
    }
}
