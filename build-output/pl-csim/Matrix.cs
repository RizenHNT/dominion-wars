using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DominionWars.Adapters.Ai;
using DominionWars.Data;

namespace PlCsim
{
    /// <summary>
    /// One cell of the playstyle matrix: one playstyle over the full round robin.
    /// </summary>
    internal sealed class MatrixCell
    {
        public string PlaystyleId { get; set; } = string.Empty;
        public string PlaystyleFingerprint { get; set; } = string.Empty;

        /// <summary>
        /// The effective punish-response stance this playstyle applied, as the
        /// canonical token from <c>PunishResponseStances.IdOf</c>. Both players
        /// share it (one policy instance drives both sides), so it is quoted per
        /// side in the report rather than once for the match.
        /// </summary>
        public string PunishStance { get; set; } = "unknown";

        /// <summary>True when the playstyle id is one of the three shipped brackets.</summary>
        public bool IsBracket { get; set; }

        public string PolicyKind { get; set; } = string.Empty;
        public Aggregation Aggregation { get; set; } = new Aggregation();
        public CardUsageTable Usage { get; set; } = new CardUsageTable();

        /// <summary>How many report files were summed into this cell (1 = single-process run).</summary>
        public int Pieces { get; set; }

        /// <summary>Faction -> win rate, for the ordering verdict.</summary>
        public Dictionary<string, double> WinRates()
        {
            var result = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var faction in Aggregation.Order)
            {
                var row = Aggregation.ByFaction[faction];
                var played = row.Wins + row.Losses + row.Draws;
                result[faction] = played == 0 ? 0 : 100.0 * row.Wins / played;
            }

            return result;
        }
    }

    /// <summary>
    /// The canonical punish-response stance token for a playstyle id, read from
    /// the shipped registry so the harness never keeps a second copy of the
    /// mapping. Namespace level so the live-matrix and merge paths share it.
    /// </summary>
    internal static class PlaystyleProvenance
    {
        public static string StanceOf(string id)
        {
            try
            {
                return PunishResponseStances.IdOf(PlaystyleRegistry.GetPlaystyle(id).ResponseStance);
            }
            catch (Exception exception)
            {
                // An id the registry cannot resolve must not silently read as a
                // stance; the token says so instead.
                return "unresolved (" + exception.GetType().Name + ")";
            }
        }

        public static bool IsBracket(string id) => PlaystyleRegistry.IsBracket(id);
    }

    /// <summary>
    /// The playstyles x decks matrix: runs every available playstyle over the
    /// shipped decks, then applies a HARD-CODED robustness rule so the verdict
    /// is computed rather than judged.
    /// </summary>
    internal static class MatrixRunner
    {
        /// <summary>
        /// The shipped playstyles to run: every SELECTABLE registry id (the six
        /// style playstyles followed by the three brackets), in registry order.
        /// Bracket ids are picked up automatically, because the list is read from
        /// the registry rather than hard-coded.
        ///
        /// <see cref="PlaystyleRegistry.SelectableIds"/>, not
        /// <see cref="PlaystyleRegistry.Ids"/>: the latter is the style table only,
        /// and using it silently drops the three boundary brackets, which is the
        /// range the verdict is supposed to be computed over.
        /// </summary>
        public static IReadOnlyList<string> AvailablePlaystyleIds() =>
            PlaystyleRegistry.SelectableIds.ToArray();

        public static List<MatrixCell> Run(
            CardCatalog catalog,
            IReadOnlyList<DeckDefinition> decks,
            SimOptions template)
        {
            var cells = new List<MatrixCell>();
            foreach (var id in AvailablePlaystyleIds())
            {
                var playstyle = PlaystyleRegistry.GetPlaystyle(id);
                var options = template.CloneForPlaystyle(id);
                Console.WriteLine($"--- playstyle '{id}' (weights {WeightedPlaystyle.WeightFingerprint(playstyle)}, " +
                                  $"punish stance {PlaystyleProvenance.StanceOf(id)}) ---");
                var cell = new MatrixCell
                {
                    PlaystyleId = id,
                    PlaystyleFingerprint = WeightedPlaystyle.WeightFingerprint(playstyle),
                    PunishStance = PlaystyleProvenance.StanceOf(id),
                    IsBracket = PlaystyleRegistry.IsBracket(id),
                    PolicyKind = "shipped-playstyle",
                };
                Simulator.RunInto(catalog, decks, options, cell.Aggregation, cell.Usage);
                cells.Add(cell);
                foreach (var faction in cell.Aggregation.Order)
                {
                    var row = cell.Aggregation.ByFaction[faction];
                    var played = row.Wins + row.Losses + row.Draws;
                    Console.WriteLine($"      {faction,-10} {100.0 * row.Wins / Math.Max(1, played),6:F2}%  " +
                                      $"({row.Wins}/{played}) turns {row.TurnSum / (double)Math.Max(1, played):F2}");
                }
            }

            return cells;
        }

        /// <summary>
        /// THE ROBUSTNESS RULE, hard-coded and deterministic:
        ///
        /// 1. Rank the factions by win rate for each playstyle, descending;
        ///    ties broken by faction name ordinal so the ranking is total.
        /// 2. The matrix is ORDER-STABLE if every playstyle produces the same
        ///    ordering. Otherwise it is ORDER-FLIPS, and every playstyle whose
        ///    ordering differs from the first playstyle's is named together with
        ///    the factions it moved.
        ///
        /// No judgement is applied; the verdict is a comparison of rank vectors.
        /// </summary>
        public static (string Verdict, List<string> Disagreeing, Dictionary<string, string> OrderByPlaystyle) Verdict(
            IReadOnlyList<MatrixCell> cells)
        {
            var orders = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var cell in cells)
            {
                var rates = cell.WinRates();
                var ranked = rates
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Key)
                    .ToArray();
                orders[cell.PlaystyleId] = string.Join(" > ", ranked);
            }

            if (orders.Count == 0) return ("NO-DATA", new List<string>(), orders);

            var referenceId = cells[0].PlaystyleId;
            var reference = orders[referenceId];
            var disagreeing = new List<string>();
            foreach (var pair in orders)
            {
                if (string.Equals(pair.Value, reference, StringComparison.Ordinal)) continue;
                var moved = DescribeMoves(reference.Split(new[] { " > " }, StringSplitOptions.None),
                    pair.Value.Split(new[] { " > " }, StringSplitOptions.None));
                disagreeing.Add($"{pair.Key}: {pair.Value} ({moved})");
            }

            return (disagreeing.Count == 0 ? "ORDER-STABLE" : "ORDER-FLIPS", disagreeing, orders);
        }

        private static string DescribeMoves(string[] reference, string[] candidate)
        {
            var moves = new List<string>();
            var indexOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < reference.Length; i++) indexOf[reference[i]] = i;
            for (var i = 0; i < candidate.Length; i++)
            {
                if (!indexOf.TryGetValue(candidate[i], out var was)) continue;
                if (was != i) moves.Add($"{candidate[i]} {was + 1}->{i + 1}");
            }

            return moves.Count == 0 ? "same ranks, different tie order" : string.Join(", ", moves);
        }

        public static string ToJson(IReadOnlyList<MatrixCell> cells, string engineSha, SimOptions options)
        {
            var invariant = CultureInfo.InvariantCulture;
            var (verdict, disagreeing, orders) = Verdict(cells);
            var builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append(EngineStamp.ToJsonFields());
            builder.Append($"  \"t1Mechanism\": \"engine-rule\",\n");
            builder.Append($"  \"maxPunishResponsesPerRound\": {options.MaxPunishResponsesPerRound},\n");
            builder.Append($"  \"maxResponsesPerRound\": {options.MaxResponsesPerRound},\n");
            builder.Append($"  \"balanceJsonRead\": false,\n");
            builder.Append($"  \"gamesPerPairing\": {options.GamesPerPairing},\n");
            builder.Append($"  \"playstylesAvailable\": [{string.Join(", ", AvailablePlaystyleIds().Select(id => "\"" + id + "\""))}],\n");
            builder.Append($"  \"robustnessVerdict\": \"{verdict}\",\n");
            builder.Append("  \"orders\": {" + string.Join(", ", orders.Select(p => $"\"{p.Key}\":\"{p.Value}\"")) + "},\n");
            builder.Append("  \"disagreeingPlaystyles\": [" +
                           string.Join(", ", disagreeing.Select(d => "\"" + d.Replace("\"", "'") + "\"")) + "],\n");
            builder.Append("  \"playstyles\": [\n");
            builder.Append(string.Join(",\n", cells.Select(cell =>
            {
                var rates = cell.WinRates();
                var values = rates.Values.ToArray();
                return "    {" +
                       $"\"playstyleId\":\"{cell.PlaystyleId}\"," +
                       $"\"weightFingerprint\":\"{cell.PlaystyleFingerprint}\"," +
                       $"\"isBracket\":{(cell.IsBracket ? "true" : "false")}," +
                       $"\"punishResponseStance\":\"{cell.PunishStance}\"," +
                       $"\"punishResponseStancePerSide\":{{\"sideA\":\"{cell.PunishStance}\"," +
                       $"\"sideB\":\"{cell.PunishStance}\"}}," +
                       $"\"min\":{values.Min().ToString("F2", invariant)}," +
                       $"\"median\":{Median(values).ToString("F2", invariant)}," +
                       $"\"max\":{values.Max().ToString("F2", invariant)}," +
                       $"\"spread\":{(values.Max() - values.Min()).ToString("F2", invariant)}," +
                       "\"factions\": {" +
                       string.Join(", ", rates.OrderBy(p => p.Key, StringComparer.Ordinal)
                           .Select(p => $"\"{p.Key}\":{p.Value.ToString("F2", invariant)}")) +
                       "}}";
            })));
            builder.Append("\n  ],\n");
            builder.Append("  \"perFaction\": [\n");
            builder.Append(string.Join(",\n", cells[0].Aggregation.Order.Select(faction =>
            {
                var values = cells.Select(c => c.WinRates()[faction]).ToArray();
                return "    {" +
                       $"\"faction\":\"{faction}\"," +
                       $"\"min\":{values.Min().ToString("F2", invariant)}," +
                       $"\"median\":{Median(values).ToString("F2", invariant)}," +
                       $"\"max\":{values.Max().ToString("F2", invariant)}," +
                       $"\"spread\":{(values.Max() - values.Min()).ToString("F2", invariant)}," +
                       "\"byPlaystyle\": {" +
                       string.Join(", ", cells.Select(c =>
                           $"\"{c.PlaystyleId}\":{c.WinRates()[faction].ToString("F2", invariant)}")) +
                       "}}";
            })));
            builder.Append("\n  ],\n");
            builder.Append("  \"health\": {\n");
            builder.Append(string.Join(",\n", cells.Select(c =>
                "    " + $"\"{c.PlaystyleId}\": {{\"decided\":{c.Aggregation.Decided}," +
                $"\"capped\":{c.Aggregation.Capped},\"exceptions\":{c.Aggregation.Exceptions}," +
                $"\"driverGuardHits\":{c.Aggregation.DriverGuardHits}}}")));
            builder.Append("\n  }\n}");
            return builder.ToString();
        }

        public static string ToMarkdown(IReadOnlyList<MatrixCell> cells, string engineSha, SimOptions options)
        {
            var (verdict, disagreeing, orders) = Verdict(cells);
            var builder = new StringBuilder();
            builder.AppendLine("# Playstyle matrix (shipped decks, engine-rule T1)");
            builder.AppendLine();
            builder.AppendLine($"* T1 mechanism **engine-rule** (`maxPunishResponsesPerRound={options.MaxPunishResponsesPerRound}`, policy flag {options.MaxResponsesPerRound})");
            builder.Append(EngineStamp.ToMarkdownLines());
            builder.AppendLine($"* `balance.json` read by harness: **false** (MatchRules supplied in-process)");
            builder.AppendLine($"* {options.GamesPerPairing} seeds x 12 ordered pairings = {options.GamesPerPairing * 12} games per playstyle");
            builder.AppendLine();
            builder.AppendLine($"## Robustness verdict: **{verdict}**");
            builder.AppendLine();
            if (disagreeing.Count > 0)
            {
                builder.AppendLine("Playstyles whose faction ordering differs from the first playstyle:");
                builder.AppendLine();
                foreach (var line in disagreeing) builder.AppendLine($"* {line}");
                builder.AppendLine();
            }
            else
            {
                builder.AppendLine("Every playstyle produced an identical faction strength ordering.");
                builder.AppendLine();
            }

            builder.AppendLine("## Per faction");
            builder.AppendLine();
            builder.AppendLine("| faction | min | median | max | spread | ordering-stable? |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var faction in cells[0].Aggregation.Order)
            {
                var values = cells.Select(c => c.WinRates()[faction]).ToArray();
                builder.AppendLine($"| {faction} | {values.Min():F2}% | {Median(values):F2}% | {values.Max():F2}% | " +
                                   $"{values.Max() - values.Min():F2} pts | {yesno(disagreeing.Count == 0)} |");
            }

            builder.AppendLine();
            builder.AppendLine("## Per playstyle x faction win rate");
            builder.AppendLine();
            builder.Append("| playstyle | fingerprint | bracket? | punish stance (both sides) |");
            foreach (var faction in cells[0].Aggregation.Order) builder.Append($" {faction} |");
            builder.AppendLine();
            builder.Append("| --- | --- | --- | --- |");
            foreach (var _ in cells[0].Aggregation.Order) builder.Append(" --- |");
            builder.AppendLine();
            foreach (var cell in cells)
            {
                var rates = cell.WinRates();
                builder.Append($"| {cell.PlaystyleId} | `{cell.PlaystyleFingerprint}` | " +
                               $"{(cell.IsBracket ? "yes" : "no")} | {cell.PunishStance} |");
                foreach (var faction in cell.Aggregation.Order) builder.Append($" {rates[faction]:F2}% |");
                builder.AppendLine();
            }

            builder.AppendLine();
            builder.AppendLine("## Faction ordering per playstyle");
            builder.AppendLine();
            foreach (var pair in orders) builder.AppendLine($"* `{pair.Key}`: {pair.Value}");

            builder.AppendLine();
            builder.AppendLine("## Health");
            builder.AppendLine();
            builder.AppendLine("| playstyle | decided | capped | exceptions | driver guard hits |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var cell in cells)
            {
                builder.AppendLine($"| {cell.PlaystyleId} | {cell.Aggregation.Decided} | {cell.Aggregation.Capped} | " +
                                   $"{cell.Aggregation.Exceptions} | {cell.Aggregation.DriverGuardHits} |");
            }

            var usage = cells.SelectMany(c => c.Usage.Rows).ToList();
            builder.AppendLine();
            builder.AppendLine("## Card usage: dead and narrow");
            builder.AppendLine();
            var byCard = usage.GroupBy(r => r.CardId, StringComparer.Ordinal)
                .Select(g => new
                {
                    CardId = g.Key,
                    Faction = g.First().Faction,
                    Plays = g.Sum(r => r.Played),
                    Uses = g.Sum(r => r.TotalUses),
                    PlaystylesUsing = g.Count(r => r.TotalUses > 0),
                })
                .OrderBy(r => r.PlaystylesUsing).ThenBy(r => r.CardId, StringComparer.Ordinal)
                .ToList();
            var dead = byCard.Where(r => r.PlaystylesUsing == 0).ToList();
            var narrow = byCard.Where(r => r.PlaystylesUsing == 1).ToList();
            builder.AppendLine();
            builder.AppendLine($"DEAD (used by no playstyle, {dead.Count} card ids): " +
                               (dead.Count == 0 ? "(none)" : string.Join(", ", dead.Select(d => d.CardId))));
            builder.AppendLine();
            builder.AppendLine($"NARROW (used by exactly one playstyle, {narrow.Count} card ids): " +
                               (narrow.Count == 0 ? "(none)" : string.Join(", ", narrow.Select(d => d.CardId))));
            builder.AppendLine();
            return builder.ToString();
        }

        private static string yesno(bool value) => value ? "yes" : "no";

        private static double Median(double[] values)
        {
            if (values.Length == 0) return 0;
            var sorted = values.OrderBy(v => v).ToArray();
            var mid = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }
    }

    /// <summary>
    /// Merge step. The full matrix is nine playstyles x 720 games, and this
    /// environment terminates a single harness process after roughly 90 seconds
    /// of sustained work (observed repeatedly: a 720-game matrix dies at ~89 s
    /// while the same games run fine as separate processes). Each playstyle is
    /// therefore executed by its own process writing style-ID.json, and this
    /// step re-reads those reports and applies the same hard-coded rule, so the
    /// verdict is produced by the same code path.
    /// </summary>
    /// <summary>
    /// Merge step. The full matrix is nine playstyles x 720 games, and this
    /// environment terminates a harness process after roughly 90 seconds of
    /// sustained work, so each playstyle is executed as one or more separate
    /// processes over DISJOINT seed ranges (--seed-chunk-size / --seed-chunk),
    /// each writing style-&lt;id&gt;.chunk&lt;N&gt;.json. This step re-reads those
    /// pieces, sums them, and applies the same hard-coded robustness rule, so the
    /// verdict comes from one code path regardless of how the games were split.
    /// </summary>
    internal static class MatrixMerge
    {
        /// <summary>Total duplicate-id advertisement drops summed over all pieces.</summary>
        public static long DuplicateDrops { get; private set; }

        /// <summary>Per-piece card-usage CSVs read back during the merge.</summary>
        public static readonly List<string> UsageFiles = new List<string>();

        private static int ChunkIndexOf(string piecePath)
        {
            var name = Path.GetFileNameWithoutExtension(piecePath);
            var at = name.LastIndexOf(".chunk", StringComparison.Ordinal);
            if (at < 0) return -1;
            return int.TryParse(name.Substring(at + ".chunk".Length), out var chunk) ? chunk : -1;
        }

        public static int Run(SimOptions options, string outDir)
        {
            var available = MatrixRunner.AvailablePlaystyleIds();
            var cells = new List<MatrixCell>();
            var missing = new List<string>();

            foreach (var id in available)
            {
                var pieces = new List<string>();
                var single = Path.Combine(outDir, "style-" + id + ".json");
                if (File.Exists(single)) pieces.Add(single);
                for (var chunk = 0; chunk < 64; chunk++)
                {
                    var piece = Path.Combine(outDir, "style-" + id + ".chunk" + chunk + ".json");
                    if (File.Exists(piece)) pieces.Add(piece);
                }

                if (pieces.Count == 0)
                {
                    missing.Add(id);
                    continue;
                }

                var agg = new Aggregation();
                foreach (var faction in new[] { "烈焰帝国", "机械遗迹", "深海联盟", "古木圣地" })
                {
                    agg.Order.Add(faction);
                    agg.ByFaction[faction] = new FactionRow();
                }

                var fingerprint = string.Empty;
                foreach (var piece in pieces)
                {
                    var json = File.ReadAllText(piece);
                    if (fingerprint.Length == 0) fingerprint = JsonText(json, "playstyleFingerprint");

                    foreach (var faction in agg.Order)
                    {
                        var row = agg.ByFaction[faction];
                        row.Wins += (int)JsonNumberIn(json, "\"faction\":\"" + faction + "\"", "wins");
                        row.Losses += (int)JsonNumberIn(json, "\"faction\":\"" + faction + "\"", "losses");
                        row.Draws += (int)JsonNumberIn(json, "\"faction\":\"" + faction + "\"", "draws");
                        row.TurnSum += (long)JsonNumberIn(json, "\"faction\":\"" + faction + "\"", "turnSum");
                    }

                    agg.Decided += (int)JsonNumber(json, "decided");
                    agg.Capped += (int)JsonNumber(json, "capped");
                    agg.Exceptions += (int)JsonNumber(json, "exceptions");
                    agg.DriverGuardHits += (long)JsonNumber(json, "driverGuardHits");
                    DuplicateDrops += (long)JsonNumber(json, "duplicateAdvertisementDrops");

                    // Card-usage telemetry is written per piece as a CSV by the
                    // running process (usage is collected in-process only), so it
                    // is read back here rather than re-derived.
                    var usagePath = Path.Combine(outDir, "style-" + id + ".chunk" + ChunkIndexOf(piece) + "-usage.csv");
                    if (File.Exists(usagePath)) UsageFiles.Add(usagePath);
                    var singleUsage = Path.Combine(outDir, "style-" + id + "-usage.csv");
                    if (File.Exists(singleUsage)) UsageFiles.Add(singleUsage);
                }

                cells.Add(new MatrixCell
                {
                    PlaystyleId = id,
                    PlaystyleFingerprint = fingerprint,
                    // The stance is a property of the shipped playstyle, not of
                    // the merged numbers, so it is read from the registry rather
                    // than left blank in the merge path.
                    PunishStance = PlaystyleProvenance.StanceOf(id),
                    IsBracket = PlaystyleRegistry.IsBracket(id),
                    PolicyKind = "shipped-playstyle",
                    Aggregation = agg,
                    Pieces = pieces.Count,
                });
            }

            Console.WriteLine("=== playstyle matrix merge (per-playstyle report pieces) ===");
            Console.WriteLine($"playstyles available : {string.Join(", ", available)}");
            Console.WriteLine($"duplicate-id drops   : {DuplicateDrops} (engine-defect workaround)");
            for (var index = 0; index < cells.Count; index++)
            {
                Console.WriteLine($"  {cells[index].PlaystyleId,-16} pieces={cells[index].Pieces}");
            }

            if (missing.Count > 0) Console.WriteLine($"MISSING reports (excluded): {string.Join(", ", missing)}");
            if (cells.Count == 0)
            {
                Console.WriteLine("nothing to merge; run --playstyle <id> --seed-chunk-size N --seed-chunk K --report style-<id>.chunk<K>.json first");
                return 2;
            }

            var (verdict, disagreeing, orders) = MatrixRunner.Verdict(cells);
            Console.WriteLine($"playstyles merged    : {cells.Count}");
            Console.WriteLine($"ROBUSTNESS VERDICT   : {verdict}");
            foreach (var line in disagreeing) Console.WriteLine($"    disagreement: {line}");
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

            if (UsageFiles.Count > 0)
            {
                var mergedUsage = new System.Text.StringBuilder();
                mergedUsage.AppendLine(CardUsageCsv.Header);
                foreach (var file in UsageFiles)
                {
                    foreach (var line in File.ReadAllLines(file))
                    {
                        if (line.Length == 0) continue;
                        if (line.StartsWith("playstyle,card_id", StringComparison.Ordinal)) continue;
                        mergedUsage.AppendLine(line);
                    }
                }

                File.WriteAllText(Path.Combine(outDir, options.MatrixName + "-usage.csv"), mergedUsage.ToString());
                Console.WriteLine($"usage pieces merged  : {UsageFiles.Count} CSV(s)");
            }

            File.WriteAllText(Path.Combine(outDir, options.MatrixName + ".json"),
                MatrixRunner.ToJson(cells, EngineStamp.EngineSha256, options));
            File.WriteAllText(Path.Combine(outDir, options.MatrixName + ".md"),
                MatrixRunner.ToMarkdown(cells, EngineStamp.EngineSha256, options));
            Console.WriteLine();
            Console.WriteLine($"artifacts: {options.MatrixName}.json / .md in build-output\\pl-csim");
            return 0;
        }

        /// <summary>
        /// Reads a number belonging to a faction entry of the report's
        /// "factions" array. The search is scoped to that array first, because
        /// "faction" also appears in the later "behavior" array whose objects
        /// carry no win/loss/turnSum fields.
        /// </summary>
        private static double JsonNumberIn(string json, string factionMarker, string key)
        {
            var scope = JsonArraySection(json, "\"factions\"");
            var at = scope.IndexOf(factionMarker, StringComparison.Ordinal);
            if (at < 0)
            {
                scope = json;
                at = scope.IndexOf(factionMarker, StringComparison.Ordinal);
            }

            if (at < 0) return 0;
            var close = scope.IndexOf('}', at);
            var slice = close < 0 ? scope.Substring(at) : scope.Substring(at, close - at);
            return JsonNumber(slice, key);
        }

        /// <summary>Extracts a top-level JSON array's text by its key, honouring nesting.</summary>
        private static string JsonArraySection(string json, string key)
        {
            var at = json.IndexOf(key, StringComparison.Ordinal);
            if (at < 0) return string.Empty;
            var open = json.IndexOf('[', at);
            if (open < 0) return string.Empty;
            var depth = 0;
            for (var index = open; index < json.Length; index++)
            {
                if (json[index] == '[') depth++;
                else if (json[index] == ']')
                {
                    depth--;
                    if (depth == 0) return json.Substring(open, index - open + 1);
                }
            }

            return string.Empty;
        }

        private static double JsonNumber(string json, string key)
        {
            var at = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (at < 0) return 0;
            var colon = json.IndexOf(':', at);
            if (colon < 0) return 0;
            var start = colon + 1;
            // The report writer emits "key": value with a space after the colon,
            // so whitespace must be skipped before the digits.
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            var end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-' || json[end] == '.' || json[end] == '+' || json[end] == 'e' || json[end] == 'E'))
            {
                end++;
            }

            return end <= start
                ? 0
                : double.TryParse(json.Substring(start, end - start),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        private static string JsonText(string json, string key)
        {
            var at = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (at < 0) return string.Empty;
            var colon = json.IndexOf(':', at);
            var open = json.IndexOf('"', colon + 1);
            var close = json.IndexOf('"', open + 1);
            return open < 0 || close < 0 ? string.Empty : json.Substring(open + 1, close - open - 1);
        }
    }

    internal static class EngineStamp
    {
        public static string EngineSha256 { get; set; } = "unavailable";
        public static string AdaptersSha256 { get; set; } = "unavailable";

        /// <summary>Where the pinned Adapters build lives outside the staged pair.</summary>
        public static string AdaptersSourcePath { get; set; } = "unavailable";

        /// <summary>Hash of that build, so pinned-vs-source drift is visible in the artifact.</summary>
        public static string AdaptersSourceSha256 { get; set; } = "unavailable";

        /// <summary>Result of the pinned-pair drift check.</summary>
        public static bool PinnedPairVerified { get; set; }

        public static string PinnedSidecarPath { get; set; } = "absent";

        /// <summary>Non-fatal provenance notes (source-output drift, missing source build).</summary>
        public static string ProvenanceNotes { get; set; } = "none";

        /// <summary>Both players in a harness match share one response policy; say so rather than implying two.</summary>
        public const string PunishStanceAppliesTo = "both-sides (one shared IPunishResponsePolicy instance)";

        /// <summary>JSON fragment for the provenance block, shared by the matrix report writers.</summary>
        public static string ToJsonFields()
        {
            var builder = new StringBuilder();
            builder.Append($"  \"engineSha256\": \"{EngineSha256}\",\n");
            builder.Append($"  \"adaptersSha256\": \"{AdaptersSha256}\",\n");
            builder.Append($"  \"adaptersSourcePath\": \"{Escape(AdaptersSourcePath)}\",\n");
            builder.Append($"  \"adaptersSourceSha256\": \"{AdaptersSourceSha256}\",\n");
            builder.Append($"  \"pinnedRevisionSidecar\": \"{Escape(PinnedSidecarPath)}\",\n");
            builder.Append($"  \"pinnedPairVerified\": {(PinnedPairVerified ? "true" : "false")},\n");
            builder.Append($"  \"provenanceNotes\": \"{Escape(ProvenanceNotes)}\",\n");
            builder.Append($"  \"punishStanceAppliesTo\": \"{Escape(PunishStanceAppliesTo)}\",\n");
            return builder.ToString();
        }

        /// <summary>Markdown lines for the provenance block.</summary>
        public static string ToMarkdownLines()
        {
            var builder = new StringBuilder();
            builder.AppendLine($"* engine `DominionWars.Engine.dll` sha256 `{EngineSha256}`");
            builder.AppendLine($"* adapters `DominionWars.Adapters.dll` sha256 `{AdaptersSha256}`");
            builder.AppendLine($"* adapters source build `{AdaptersSourcePath}` sha256 `{AdaptersSourceSha256}`");
            builder.AppendLine($"* pinned (Engine, Adapters) pair verified against `{PinnedSidecarPath}`: " +
                               $"**{(PinnedPairVerified ? "yes" : "NO")}**");
            builder.AppendLine($"* provenance notes: {ProvenanceNotes}");
            return builder.ToString();
        }

        private static string Escape(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "'");
    }
}
