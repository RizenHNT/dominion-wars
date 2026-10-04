using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DominionWars.Adapters.Ai;
using DominionWars.Data;
using DominionWars.Engine;
using DominionWars.Engine.Model;
using DominionWars.Engine.Setup;
using DominionWars.Engine.Turns;

namespace PlCsim
{
    /// <summary>
    /// C#-authoritative full-match simulation harness.
    ///
    /// It drives the real engine composition (MatchSetup + TurnFlow +
    /// TurnActionRouter + LegalActionGenerator) with an internal heuristic
    /// policy that only ever picks an action the engine already advertised.
    /// No file outside build-output/pl-csim is written.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                // Provenance hardening: record which physical file each engine
                // assembly is actually loaded from. A revision mismatch that is
                // invisible in a hash of the app directory (probing order, the
                // NuGet cache, a stray copy earlier on the probing path) shows up
                // here instead of as a bare MissingMethodException.
                AppDomain.CurrentDomain.AssemblyLoad += (_, eventArgs) =>
                {
                    var name = eventArgs.LoadedAssembly.GetName().Name;
                    if (name is null || !name.StartsWith("DominionWars", StringComparison.Ordinal)) return;
                    var location = eventArgs.LoadedAssembly.Location;
                    if (string.IsNullOrEmpty(location)) return;
                    Console.WriteLine($"loaded {name} <- {location} " +
                                      $"({ProvenanceGuard.HashOf(location).Substring(0, 12)})");
                };

                return MainInner(args);
            }
            catch (Exception exception)
            {
                // A silent crash loses the only diagnostic there is; write it
                // where a redirected console cannot swallow it.
                try
                {
                    File.WriteAllText(
                        Path.Combine(Directory.GetCurrentDirectory(), "build-output", "pl-csim", "matrix-crash.txt"),
                        exception.ToString());
                }
                catch (IOException)
                {
                }

                Console.Error.WriteLine(exception);
                return 3;
            }
        }

        private static int MainInner(string[] args)
        {
            var options = SimOptions.Parse(args);
            var root = FindRepositoryRoot();
            var shippedCatalog = CardCatalog.LoadDirectory(options.CardsDirectory ?? Path.Combine(root, "data", "cards"));
            var decks = DeckLoader.LoadDirectory(options.DecksDirectory ?? Path.Combine(root, "data", "decks"));
            var catalog = CatalogVariant.Build(shippedCatalog, options.Variant);

            if (options.MergeMatrix)
            {
                var outDir0 = Path.Combine(Directory.GetCurrentDirectory(), "build-output", "pl-csim");
                StampProvenance(options);
                return MatrixMerge.Run(options, outDir0);
            }

            if (options.Matrix)
            {
                return RunMatrix(options, catalog, decks);
            }

            // Resolve the MatchRules the harness will hand the engine. The engine
            // cap (T1 as a rule) is NOT the same mechanism as the policy-level
            // emulation; both are reported so a run can never be mis-attributed.
            var rules = MatchRulesFactory.Create(options);
            options.Rules = rules;
            // The legacy non-playstyle path does not use the Adapters playstyle
            // seam, so an unverifiable pair is recorded rather than fatal here.
            // The matrix path, which does use that seam, stays strict.
            StampProvenance(options,
                options.Playstyle is null
                    ? "legacy non-playstyle run: Adapters pair not verified"
                    : null);
            Console.WriteLine("=== C# engine (DominionWars.Engine.dll) full-match simulation ===");
            Console.WriteLine($"repo root          : {root}");
            Console.WriteLine($"balance.json       : NOT read by this harness (MatchRules supplied in-process)");
            Console.WriteLine($"MatchRules         : {MatchRulesFactory.Describe}");
            Console.WriteLine($"T1 mechanism       : ENGINE RULE maxPunishResponsesPerRound={options.MaxPunishResponsesPerRound}" +
                              (options.MaxPunishResponsesPerRound == 0 ? " (rule off)" : "") +
                              $" | POLICY flag maxResponsesPerRound={options.MaxResponsesPerRound}" +
                              (options.MaxResponsesPerRound == 0 ? " (off)" : " (ACTIVE)"));
            Console.WriteLine($"cards loaded       : {catalog.Cards.Count}");
            Console.WriteLine($"ablated variant    : {options.Variant}");
            var clearedKinds = CatalogVariant.ClearedKindsFor(options.Variant);
            if (clearedKinds.Length > 0)
            {
                Console.WriteLine($"  punishActivatable cleared on kinds: {clearedKinds}");
            }

            if (options.MaxResponsesPerRound > 0)
            {
                Console.WriteLine($"  NOTE: policy-level T1 emulation is ACTIVE (run is not engine-rule pure)");
            }
            if (options.NoBuffFaction is not null)
            {
                Console.WriteLine($"policy ablation    : {options.NoBuffFaction} never plays BUFF-bearing cards");
            }
            Console.WriteLine($"decks loaded       : {string.Join(", ", decks.Select(d => d.Leader + "(" + d.Cards.Count + " kinds)"))}");
            Console.WriteLine($"games per pairing  : {options.GamesPerPairing} (seeds 1..{options.GamesPerPairing}, alternating first player)");
            Console.WriteLine($"turn cap / game    : {options.TurnCap}");
            Console.WriteLine($"punish responses   : {(options.DeclinePunish ? "DECLINE (no punish draw)" : "ACCEPT (full punish chain)")}");
            Console.WriteLine($"castle enabled     : {options.CastleEnabled} (health {options.CastleHealth}), player life {options.PlayerLife}, opening hand {options.OpeningHandSize}");
            Console.WriteLine();

            // Card-usage telemetry is collected in-process, so a chunked cell
            // writes one usage CSV per piece and the merge concatenates them.
            CardUsageTable? usageTable = options.UsageCsv.Length > 0 ? new CardUsageTable() : null;
            var agg = Simulator.Run(catalog, decks, options, usageTable);
            if (usageTable is not null)
            {
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "build-output", "pl-csim", options.UsageCsv),
                    CardUsageCsv.Write(usageTable.Rows, string.Empty));
                Console.WriteLine($"usage csv written  : {options.UsageCsv} ({usageTable.Rows.Count()} rows)");
            }

            Console.WriteLine("--- per-faction results (full matches, real C# engine) ---");
            Console.WriteLine($"{"faction",-14}{"wins",6}{"losses",8}{"draws",7}{"games",7}{"winRate",10}{"avgTurns",10}");
            foreach (var faction in agg.Order)
            {
                var row = agg.ByFaction[faction];
                var played = row.Wins + row.Losses + row.Draws;
                Console.WriteLine($"{faction,-14}{row.Wins,6}{row.Losses,8}{row.Draws,7}{played,7}" +
                                  $"{100.0 * row.Wins / Math.Max(1, played),9:F1}%{row.TurnSum / (double)Math.Max(1, played),10:F1}");
            }

            Console.WriteLine();
            Console.WriteLine("--- win-reason breakdown (all decided games) ---");
            foreach (var pair in agg.ReasonCounts.OrderByDescending(p => p.Value))
            {
                Console.WriteLine($"{pair.Key,-40}{pair.Value,7}");
            }

            Console.WriteLine();
            Console.WriteLine("--- win-reason per WINNING faction (which axis closes whose games) ---");
            foreach (var pair in agg.WinReasonsByFaction.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"{pair.Key,-40}{pair.Value,7}");
            }

            Console.WriteLine();
            Console.WriteLine("--- per-matchup matrix (row faction win rate vs column) ---");
            var header = "row \\ col".PadRight(14) + string.Concat(agg.Order.Select(f => f.PadLeft(14)));
            Console.WriteLine(header);
            foreach (var a in agg.Order)
            {
                var line = a.PadRight(14);
                foreach (var b in agg.Order)
                {
                    if (a == b) { line += "-".PadLeft(14); continue; }
                    var cell = agg.Matchups[(a, b)];
                    var played = cell.Wins + cell.Losses + cell.Draws;
                    line += played == 0 ? "n/a".PadLeft(14)
                        : $"{100.0 * cell.Wins / played:F1}% ({cell.Wins}/{played})".PadLeft(14);
                }

                Console.WriteLine(line);
            }

            Console.WriteLine();
            Console.WriteLine("--- first-player effect (faction win rate as P0 / as P1) ---");
            foreach (var faction in agg.Order)
            {
                var row = agg.ByFaction[faction];
                Console.WriteLine($"{faction,-14} as P0 {Pct(row.WinsAsP0, row.GamesAsP0),6:F1}%  " +
                                  $"as P1 {Pct(row.WinsAsP1, row.GamesAsP1),6:F1}%");
            }

            Console.WriteLine();
            Console.WriteLine("--- machine 提交/上传/下载 (commit/push/pull) observations ---");
            Console.WriteLine($"machine PullCount at game end: avg {agg.MachinePullAvg:F2}, max {agg.MachinePullMax}, " +
                              $"decided games ending at >=6 pulls {agg.MachinePullGe6AtWin}, of {agg.MachineGames} machine games");
            Console.WriteLine($"machine PullCount observed at turn 5: avg {agg.MachinePullAtTurn5Avg:F2} " +
                              $"({agg.MachinePullAtTurn5Count} games); at turn 10: avg {agg.MachinePullAtTurn10Avg:F2} " +
                              $"({agg.MachinePullAtTurn10Count} games)");

            Console.WriteLine();
            Console.WriteLine("--- wood growth observations (RootStacks) ---");
            Console.WriteLine($"wood games {agg.WoodGames}, avg RootStacks at end {agg.WoodRootAvg:F2} (max {agg.WoodRootMax})");
            Console.WriteLine($"wood RootStacks observed at turn 5: avg {agg.WoodRootAtTurn5Avg:F2} " +
                              $"({agg.WoodRootAtTurn5Count} games); at turn 10: avg {agg.WoodRootAtTurn10Avg:F2} " +
                              $"({agg.WoodRootAtTurn10Count} games)");

            Console.WriteLine();
            Console.WriteLine("--- ambush / punish / lifecycle counters ---");
            Console.WriteLine($"SET_AMBUSH submitted   : {agg.AmbushSets}");
            Console.WriteLine($"COMMIT submitted       : {agg.Commits}");
            Console.WriteLine($"PULL submitted         : {agg.Pulls}");
            Console.WriteLine($"ATTACK submitted       : {agg.Attacks}");
            Console.WriteLine($"PLAY_CARD submitted    : {agg.Plays}");
            Console.WriteLine($"actions rejected       : {agg.Rejections}");
            Console.WriteLine($"driver guard hits      : {agg.DriverGuardHits}");
            foreach (var sample in agg.DriverGuardSamples)
            {
                Console.WriteLine($"    {sample}");
            }
            if (agg.RejectReasons.Count > 0)
            {
                foreach (var pair in agg.RejectReasons.OrderByDescending(p => p.Value))
                {
                    Console.WriteLine($"    {pair.Key,-44}{pair.Value,8}");
                }
            }

            if (agg.RejectSamples.Count > 0)
            {
                Console.WriteLine("    rejection samples:");
                foreach (var sample in agg.RejectSamples)
                {
                    Console.WriteLine($"      {sample}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("--- board / hand size observations ---");
            Console.WriteLine($"largest minion health seen in any game        : {agg.MaxMinionHealthMax} (wood win needs a sealed 512)");
            Console.WriteLine($"largest sealed minion health seen in any game : {agg.MaxSealedHealthMax}");
            Console.WriteLine($"avg combined hand size at game end            : {agg.EndHandSum / (double)Math.Max(1, agg.EndedGames):F1}");
            Console.WriteLine($"avg combined deck size at game end            : {agg.EndDeckSum / (double)Math.Max(1, agg.EndedGames):F1}");
            Console.WriteLine($"largest single-player hand seen in any game   : {agg.MaxHandSeen}");
            Console.WriteLine($"cards drawn by punish chains (PUNISH_DRAW)    : {agg.PunishDrawCards} over {agg.PunishDrawEvents} events");
            Console.WriteLine($"cards drawn by ordinary draw (CARDS_DRAWN)    : {agg.OrdinaryDrawCards}");

            Console.WriteLine();
            Console.WriteLine("--- punish-chain decomposition (full matches) ---");
            Console.WriteLine($"PUNISH_TRIGGERED events (answered responses)  : {agg.PunishTriggered} " +
                              $"over {agg.PunishChains} chains (one chain per originating play/commit/pull)");
            Console.WriteLine($"PUNISH_DRAW cards from the INITIAL trigger    : {agg.PunishDrawInitial}");
            Console.WriteLine($"PUNISH_DRAW cards from RESPONSE re-entry      : {agg.PunishDrawResponse}");
            Console.WriteLine($"draws per chain (initial)                     : " +
                              $"{agg.PunishDrawInitial / (double)Math.Max(1, agg.PunishChains):F2}");
            Console.WriteLine($"draws per chain (response)                    : " +
                              $"{agg.PunishDrawResponse / (double)Math.Max(1, agg.PunishChains):F2}");
            Console.WriteLine($"chains per decided game                       : " +
                              $"{agg.PunishChains / (double)Math.Max(1, agg.Decided):F2}");
            Console.WriteLine($"max chain depth observed (events / policy)    : {agg.MaxPunishChainDepth} / {agg.MaxChainDepthFromPolicy} (engine limit 20)");
            Console.WriteLine($"offers declined by the T1 round cap           : {agg.DeclinedByRoundCap}" +
                              (options.MaxResponsesPerRound > 0 ? $" (cap = {options.MaxResponsesPerRound} per round)" : " (cap disabled)"));
            Console.WriteLine($"inactive PUNISH cards expired from hand       : {agg.ExpiredPunish} (P0 {agg.ExpiredPunish0} / P1 {agg.ExpiredPunish1})");
            Console.WriteLine($"activatable copies remaining in sea deck      : {CatalogVariant.ActivatableCopiesRemaining(options.Variant)} of 36");
            Console.WriteLine($"chain-depth histogram (depth:decides)         : {RecordingPunishPolicy.HistogramText(agg.ChainDepthHistogram)}");
            Console.WriteLine($"punish draws per GAME-TURN histogram          : {RecordingPunishPolicy.HistogramText(agg.PunishDrawsPerTurnHistogram)}");

            Console.WriteLine();
            Console.WriteLine("--- per-faction policy behaviour (symmetry check) ---");
            Console.WriteLine($"{"faction",-14}{"turns",7}{"plays/turn",12}{"punishPlays/turn",18}{"attacks/turn",14}" +
                              $"{"boardAtk/turn",15}{"punishTaken",13}{"punishDeclined",16}");
            foreach (var faction in agg.Order)
            {
                var behavior = agg.BehaviorByFaction[faction];
                var turns = Math.Max(1, behavior.TurnsTaken);
                Console.WriteLine($"{faction,-14}{behavior.TurnsTaken,7}{behavior.Plays / (double)turns,12:F2}" +
                                  $"{behavior.PlaysPunishActivated / (double)turns,18:F2}" +
                                  $"{behavior.Attacks / (double)turns,14:F2}" +
                                  $"{behavior.AttackDamageDealt / (double)turns,15:F2}" +
                                  $"{behavior.PunishActivationsTaken,13}{behavior.PunishActivationsDeclinedByBudget,16}");
            }

            Console.WriteLine();
            Console.WriteLine("--- wood / machine axis detail per faction ---");
            foreach (var faction in agg.Order)
            {
                var behavior = agg.BehaviorByFaction[faction];
                Console.WriteLine($"{faction,-14} buffCardsPlayed={behavior.BuffCardsPlayed} " +
                                  $"commits={behavior.Commits} pulls={behavior.Pulls} ambushes={behavior.Ambushes}");
            }

            Console.WriteLine();
            Console.WriteLine("--- termination health ---");
            Console.WriteLine($"games decided          : {agg.Decided}");
            Console.WriteLine($"games hitting turn cap : {agg.Capped}");
            Console.WriteLine($"games ending with no winner but no cap : {agg.NoWinnerNoCap}");
            Console.WriteLine($"exceptions thrown      : {agg.Exceptions}");
            foreach (var pair in agg.ExceptionSamples.Take(5))
            {
                Console.WriteLine($"    {pair}");
            }

            var report = Simulator.ToJson(agg, options, decks);
            var reportPath = Path.Combine(Directory.GetCurrentDirectory(), "build-output", "pl-csim",
                options.ReportName);
            File.WriteAllText(reportPath, report);
            Console.WriteLine();
            Console.WriteLine($"raw result json: {reportPath}");
            return agg.Exceptions == 0 ? 0 : 2;
        }

        /// <summary>
        /// --matrix: every shipped playstyle over the shipped decks, with the
        /// robustness verdict, the card-usage CSV and the provenance stamps.
        /// </summary>
        private static int RunMatrix(SimOptions options, CardCatalog catalog, IReadOnlyList<DeckDefinition> decks)
        {
            var rules = MatchRulesFactory.Create(options);
            options.Rules = rules;
            var outDir = Path.Combine(Directory.GetCurrentDirectory(), "build-output", "pl-csim");
            StampProvenance(options);

            var available = MatrixRunner.AvailablePlaystyleIds();
            Console.WriteLine("=== playstyle matrix (engine-rule T1, shipped decks) ===");
            Console.WriteLine($"engine sha256      : {EngineStamp.EngineSha256}");
            Console.WriteLine($"adapters sha256    : {EngineStamp.AdaptersSha256}");
            Console.WriteLine(ProvenanceGuard.Describe());
            Console.WriteLine($"MatchRules         : {MatchRulesFactory.Describe}");
            Console.WriteLine($"T1 mechanism       : ENGINE RULE maxPunishResponsesPerRound={options.MaxPunishResponsesPerRound}" +
                              $" | POLICY flag maxResponsesPerRound={options.MaxResponsesPerRound}");
            Console.WriteLine($"balance.json       : NOT read by this harness (MatchRules supplied in-process)");
            Console.WriteLine($"playstyles available ({available.Count}): {string.Join(", ", available)}");
            var brackets = available.Where(id => id.StartsWith("bracket-", StringComparison.Ordinal)).ToArray();
            Console.WriteLine(brackets.Length == 0
                ? "bracket playstyles : ABSENT from the referenced DominionWars.Adapters.dll (mechanism will pick them up when published)"
                : "bracket playstyles : " + string.Join(", ", brackets));
            Console.WriteLine($"games per playstyle: {options.GamesPerPairing * 12}");
            Console.WriteLine();

            var cells = MatrixRunner.Run(catalog, decks, options);
            if (cells.Count == 0)
            {
                Console.WriteLine("no playstyles available; nothing to measure");
                return 2;
            }

            var (verdict, disagreeing, orders) = MatrixRunner.Verdict(cells);
            Console.WriteLine();
            Console.WriteLine($"ROBUSTNESS VERDICT : {verdict}");
            if (disagreeing.Count > 0)
            {
                foreach (var line in disagreeing) Console.WriteLine($"    disagreements: {line}");
            }

            Console.WriteLine();
            Console.WriteLine("--- per faction: min / median / max / spread across playstyles ---");
            foreach (var faction in cells[0].Aggregation.Order)
            {
                var values = cells.Select(c => c.WinRates()[faction]).OrderBy(v => v).ToArray();
                var median = values.Length % 2 == 1
                    ? values[values.Length / 2]
                    : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2.0;
                Console.WriteLine($"{faction,-10} min {values.Min(),6:F2}%  median {median,6:F2}%  " +
                                  $"max {values.Max(),6:F2}%  spread {values.Max() - values.Min(),5:F2} pts");
            }

            var usageRows = cells.SelectMany(c => c.Usage.Rows).ToList();
            File.WriteAllText(Path.Combine(outDir, options.MatrixName + ".json"),
                MatrixRunner.ToJson(cells, EngineStamp.EngineSha256, options));
            File.WriteAllText(Path.Combine(outDir, options.MatrixName + ".md"),
                MatrixRunner.ToMarkdown(cells, EngineStamp.EngineSha256, options));
            File.WriteAllText(Path.Combine(outDir, options.MatrixName + "-usage.csv"),
                CardUsageCsv.Write(usageRows, string.Empty));

            // Dead / narrow, computed from the telemetry only.
            var byCard = usageRows.GroupBy(r => r.CardId, StringComparer.Ordinal)
                .Select(g => new { CardId = g.Key, Using = g.Count(r => r.TotalUses > 0), Uses = g.Sum(r => r.TotalUses) })
                .OrderBy(r => r.CardId, StringComparer.Ordinal).ToList();
            var dead = byCard.Where(r => r.Using == 0).Select(r => r.CardId).ToArray();
            var narrow = byCard.Where(r => r.Using == 1).Select(r => $"{r.CardId}({r.Uses})").ToArray();
            Console.WriteLine();
            Console.WriteLine($"DEAD cards (used by no playstyle): {dead.Length}" +
                              (dead.Length == 0 ? " (none)" : " -> " + string.Join(", ", dead)));
            Console.WriteLine($"NARROW cards (used by exactly one playstyle): {narrow.Length}" +
                              (narrow.Length == 0 ? " (none)" : " -> " + string.Join(", ", narrow)));
            Console.WriteLine();
            Console.WriteLine($"artifacts: {options.MatrixName}.json / .md / -usage.csv in build-output\\pl-csim");
            var failures = cells.Sum(c => c.Aggregation.Exceptions);
            return failures == 0 ? 0 : 2;
        }

        private static string Sha256(string path)
        {
            if (!File.Exists(path)) return "missing:" + Path.GetFileName(path);
            using var stream = File.OpenRead(path);
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        /// <summary>
        /// Stamps the measured revision pair and enforces the provenance
        /// contract: the Engine and Adapters revisions beside the harness must be
        /// the exact pair named by the pinned-revision sidecar. <see
        /// cref="ProvenanceGuard.Verify"/> throws when they are not, which is the
        /// same fail-loudly contract <see cref="MatchRulesFactory"/> applies to
        /// the engine rule, so a report can never carry a provenance it cannot
        /// prove.
        ///
        /// <c>--allow-unpinned-pair</c> exists only for exploratory runs whose
        /// numbers are never published; it records the pair as unverified rather
        /// than suppressing the check silently.
        /// </summary>
        private static void StampProvenance(SimOptions options, string? bypassNotes = null)
        {
            EngineStamp.EngineSha256 = Sha256(Path.Combine(AppContext.BaseDirectory, "DominionWars.Engine.dll"));
            EngineStamp.AdaptersSha256 = Sha256(Path.Combine(AppContext.BaseDirectory, "DominionWars.Adapters.dll"));

            if (options.AllowUnpinnedPair)
            {
                // The bypass suppresses ENFORCEMENT, never the measurement. If
                // the pair actually matches the pin, the run is verified and the
                // report must say so; if it does not, the mismatch is recorded
                // verbatim rather than being replaced by a generic "not
                // verified" that hides which revision was measured.
                var mismatch = ProvenanceGuard.CheckWithoutThrowing();
                EngineStamp.PinnedPairVerified = mismatch is null;
                EngineStamp.PinnedSidecarPath = ProvenanceGuard.SidecarPath;
                EngineStamp.AdaptersSourcePath = ProvenanceGuard.AdaptersSourcePath;
                EngineStamp.AdaptersSourceSha256 = ProvenanceGuard.AdaptersSourceSha256;
                EngineStamp.ProvenanceNotes = mismatch is null
                    ? "Enforcement bypassed by --allow-unpinned-pair, but the pair WAS verified against the " +
                      "sidecar (" + ProvenanceGuard.DriftNotes + ")."
                    : "PROVENANCE NOT VERIFIED: --allow-unpinned-pair was passed and the pair does NOT match " +
                      "the pin. Numbers from this run must not be published. Reason: " + mismatch;
                return;
            }

            try
            {
                ProvenanceGuard.Verify();
            }
            catch (NotSupportedException exception) when (bypassNotes is not null)
            {
                // Only the legacy non-playstyle path may continue unverified: it
                // does not exercise the Adapters playstyle seam at all. The
                // reason is recorded in the report instead of being swallowed.
                EngineStamp.PinnedPairVerified = false;
                EngineStamp.PinnedSidecarPath = "not verified";
                EngineStamp.ProvenanceNotes = bypassNotes + " (" + exception.Message + ")";
                return;
            }

            EngineStamp.PinnedPairVerified = ProvenanceGuard.PinnedPairVerified;
            EngineStamp.PinnedSidecarPath = ProvenanceGuard.SidecarPath;
            EngineStamp.AdaptersSourcePath = ProvenanceGuard.AdaptersSourcePath;
            EngineStamp.AdaptersSourceSha256 = ProvenanceGuard.AdaptersSourceSha256;
            EngineStamp.ProvenanceNotes = ProvenanceGuard.DriftNotes;
        }

        private static double Pct(int wins, int games) => games == 0 ? 0.0 : 100.0 * wins / games;

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docs", "RULES.md")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
        }
    }

    internal sealed class SimOptions
    {
        public string? CardsDirectory { get; private set; }
        public string? DecksDirectory { get; private set; }
        public string? MatchLogPath { get; private set; }
        public ulong SeedBase { get; private set; } = 1;
        public Func<ulong, int, DeckSpec, DeckSpec, DriverObserver>? ObserverFactory { get; set; }
        public int GamesPerPairing { get; private set; } = 25;
        public int TurnCap { get; private set; } = 200;
        public bool DeclinePunish { get; private set; }
        public bool CastleEnabled { get; private set; } = true;
        public int CastleHealth { get; private set; } = 75;
        public int PlayerLife { get; private set; } = 20;
        public int OpeningHandSize { get; private set; } = 5;
        public bool Verbose { get; private set; }
        public bool Aggressive { get; private set; }
        public bool Trace { get; private set; }
        public ulong TraceSeed { get; private set; }
        public string PolicyName { get; private set; } = "heuristic-v1";
        public string ReportName { get; private set; } = "pl-csim-result.json";
        public string Variant { get; private set; } = CatalogVariant.Shipped;
        public string? NoBuffFaction { get; private set; }
        /// <summary>T1 emulation: 0 = unlimited responses per punish round (shipped behaviour).</summary>
        public int MaxResponsesPerRound { get; private set; }

        /// <summary>
        /// T1 as an ENGINE rule: the value handed to
        /// MatchRules.MaxPunishResponsesPerRound. 0 = rule off (engine default).
        /// Distinct from <see cref="MaxResponsesPerRound"/>, which only makes the
        /// policy decline further offers.
        /// </summary>
        public int MaxPunishResponsesPerRound { get; private set; }

        /// <summary>The MatchRules instance handed to every match (built by MatchRulesFactory).</summary>
        public DominionWars.Engine.Rules.MatchRules? Rules { get; set; }

        /// <summary>
        /// Playstyle id selecting through PlaystyleRegistry. Null = the legacy
        /// harness heuristic (every measurement taken before the playstyle
        /// boundary landed).
        /// </summary>
        public string? Playstyle { get; private set; }

        /// <summary>Run playstyles x decks and emit the robustness verdict.</summary>
        public bool Matrix { get; private set; }

        /// <summary>
        /// Exploratory escape hatch for the provenance contract. Never set this
        /// on a run whose numbers are published.
        /// </summary>
        public bool AllowUnpinnedPair { get; private set; }

        /// <summary>Merge per-playstyle reports into the matrix artifacts.</summary>
        public bool MergeMatrix { get; private set; }

        /// <summary>Number of seeds to run in this process (0 = all GamesPerPairing).</summary>
        public int SeedChunkSize { get; private set; }

        /// <summary>Index of the seed chunk this process runs.</summary>
        public int SeedChunkIndex { get; private set; }

        /// <summary>Base name for the matrix artifacts.</summary>
        public string MatrixName { get; private set; } = "matrix";

        /// <summary>Card-usage CSV path (empty = do not write one).</summary>
        public string UsageCsv { get; private set; } = string.Empty;

        /// <summary>A copy of these options with the playstyle changed, for matrix cells.</summary>
        public SimOptions CloneForPlaystyle(string playstyleId) => new SimOptions
        {
            GamesPerPairing = GamesPerPairing,
            CardsDirectory = CardsDirectory,
            DecksDirectory = DecksDirectory,
            MatchLogPath = MatchLogPath,
            SeedBase = SeedBase,
            ObserverFactory = ObserverFactory,
            TurnCap = TurnCap,
            DeclinePunish = DeclinePunish,
            CastleEnabled = CastleEnabled,
            CastleHealth = CastleHealth,
            PlayerLife = PlayerLife,
            OpeningHandSize = OpeningHandSize,
            Verbose = false,
            Aggressive = Aggressive,
            Trace = false,
            TraceSeed = TraceSeed,
            PolicyName = "playstyle-" + playstyleId,
            ReportName = ReportName,
            Variant = Variant,
            NoBuffFaction = NoBuffFaction,
            MaxResponsesPerRound = MaxResponsesPerRound,
            MaxPunishResponsesPerRound = MaxPunishResponsesPerRound,
            MatrixName = MatrixName,
            UsageCsv = UsageCsv,
            Matrix = false,
            Playstyle = playstyleId,
            Rules = Rules,
            SeedChunkSize = SeedChunkSize,
            SeedChunkIndex = SeedChunkIndex,
        };

        public static SimOptions Parse(string[] args)
        {
            var options = new SimOptions();
            for (var index = 0; index < args.Length; index++)
            {
                var arg = args[index];
                string Next() => index + 1 < args.Length ? args[++index] : throw new ArgumentException("missing value for " + arg);
                switch (arg)
                {
                    case "--cards-dir": options.CardsDirectory = Path.GetFullPath(Next()); break;
                    case "--decks-dir": options.DecksDirectory = Path.GetFullPath(Next()); break;
                    case "--match-log": options.MatchLogPath = Path.GetFullPath(Next()); break;
                    case "--seed": options.SeedBase = ulong.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--games": options.GamesPerPairing = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--turn-cap": options.TurnCap = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--decline-punish": options.DeclinePunish = true; break;
                    case "--no-castle": options.CastleEnabled = false; break;
                    case "--castle-health": options.CastleHealth = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--life": options.PlayerLife = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--hand": options.OpeningHandSize = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--verbose": options.Verbose = true; break;
                    case "--aggressive": options.Aggressive = true; options.PolicyName = "heuristic-v1-aggressive"; break;
                    case "--report": options.ReportName = Next(); break;
                    case "--trace": options.Trace = true; break;
                    case "--trace-seed": options.TraceSeed = ulong.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--policy": options.PolicyName = Next(); break;
                    case "--playstyle": options.Playstyle = PlaystyleRegistry.GetPlaystyle(Next()).Id; break;
                    case "--matrix": options.Matrix = true; break;
                    case "--merge-matrix": options.MergeMatrix = true; break;
                    case "--seed-chunk-size": options.SeedChunkSize = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--seed-chunk": options.SeedChunkIndex = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--matrix-name": options.MatrixName = Next(); break;
                    case "--allow-unpinned-pair": options.AllowUnpinnedPair = true; break;
                    case "--usage-csv": options.UsageCsv = Next(); break;
                    case "--variant": options.Variant = Next(); break;
                    case "--max-responses-per-round": options.MaxResponsesPerRound = int.Parse(Next(), CultureInfo.InvariantCulture); options.PolicyName = "heuristic-v1-T1-policy-" + options.MaxResponsesPerRound; break;
                    case "--max-punish-responses-per-round": options.MaxPunishResponsesPerRound = int.Parse(Next(), CultureInfo.InvariantCulture); options.PolicyName = "heuristic-v1-T1-engine-" + options.MaxPunishResponsesPerRound; break;
                    case "--no-buff-faction": options.NoBuffFaction = Next(); options.PolicyName = "heuristic-v1-nobuff-" + options.NoBuffFaction; break;
                    default: throw new ArgumentException("unknown argument " + arg);
                }
            }

            return options;
        }
    }
}
