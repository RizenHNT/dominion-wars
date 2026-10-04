using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DominionWars.Adapters.Ai;
using DominionWars.Data;
using DominionWars.Engine;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Events;
using DominionWars.Engine.Model;
using DominionWars.Engine.Setup;
using DominionWars.Engine.Turns;
using AdapterRuntime = DominionWars.Adapters;

namespace PlCsim
{
    internal sealed class DeckSpec
    {
        public DeckSpec(string faction, string leader, string name, IReadOnlyDictionary<string, int> cards)
        {
            Faction = faction;
            Leader = leader;
            Name = name;
            Cards = cards;
        }

        public string Faction { get; }
        public string Leader { get; }
        public string Name { get; }
        public IReadOnlyDictionary<string, int> Cards { get; }

        public MatchDeckSpec ToMatchDeck() => new MatchDeckSpec(Leader, Cards, Name, Faction);
    }

    internal sealed class Counters
    {
        public long AmbushSets;
        public long Plays;
        public long Attacks;
        public long Commits;
        public long Pulls;
        public long Rejections;
        public readonly Dictionary<string, int> RejectReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly List<string> RejectSamples = new List<string>();
    }

    internal sealed class GameResult
    {
        public bool Decided { get; set; }
        public bool Capped { get; set; }
        public int WinnerIndex { get; set; } = -1;
        public string Reason { get; set; } = "unfinished";
        public int Turns { get; set; }
        public int MachinePullCount { get; set; }
        public int? MachinePullAtTurn5 { get; set; }
        public int? MachinePullAtTurn10 { get; set; }
        public int WoodRootStacks { get; set; }
        public int WoodRampantStacks { get; set; }
        public int? WoodRootAtTurn5 { get; set; }
        public int? WoodRootAtTurn10 { get; set; }
        public string? Exception { get; set; }

        /// <summary>Full stack trace for an exception, so a binding failure names its own origin.</summary>
        public string? ExceptionDetail { get; set; }
        public int DriverGuardHits { get; set; }
        public string DriverGuardReason { get; set; } = string.Empty;
        public int ForcedPhaseAdvances;
        public int MaxMinionHealth;
        public int MaxSealedHealth;
        public int EndHand0;
        public int EndHand1;
        public int EndDeck0;
        public int EndDeck1;
        public int MaxHand0;
        public int MaxHand1;
        public int DecisionCount;
        public int MaxDecisionHand;
        public int MaxDecisionOptions;
        public int DiscardSubmissions;
        public int DiscardAccepted;
        public int DiscardRequiredCards;
        public int DiscardViolations;
        public int PunishDrawEvents0;
        public int PunishDrawEvents1;
        public int PunishDrawCards0;
        public int PunishDrawCards1;
        public int DrawEvents0;
        public int DrawEvents1;
        public int DrawCards0;
        public int DrawCards1;
        public int PunishDrawInitial0;
        public int PunishDrawInitial1;
        public int PunishDrawResponse0;
        public int PunishDrawResponse1;
        public int PunishChains;
        public int ResponseDrawEvents;
        public int ExpiredPunish0;
        public int ExpiredPunish1;
        /// <summary>"winnerIndex:reasonKey" copied from the engine's terminal GAME_WON event.</summary>
        public string WinReasonByPlayer = string.Empty;
        public int PunishEventCount0;
        public int PunishEventCount1;
        public int MaxPunishChainDepth;
        public readonly Dictionary<int, int> ChainDepthHistogram = new Dictionary<int, int>();
        public readonly Dictionary<int, int> PunishDrawsPerTurnHistogram = new Dictionary<int, int>();
        public readonly Dictionary<long, int> ChainDrawTotals = new Dictionary<long, int>();
        public readonly FactionBehavior Behavior0 = new FactionBehavior();
        public readonly FactionBehavior Behavior1 = new FactionBehavior();
        public long BoardAttackSum0;
        public long BoardAttackSum1;
        public int RecordedPunishDecides;
        public int RecordedPunishAccepts;
        public int RecordedMaxChainDepth;
        public int DeclinedByRoundCap;
        public readonly Dictionary<int, int> RecordedDepthHistogram = new Dictionary<int, int>();
        public Counters Counters { get; } = new Counters();

        /// <summary>Live state handed to the telemetry scanner after the match (null on the legacy path).</summary>
        public GameState? UsageState { get; set; }
    }

    internal static class Simulator
    {
        public static ulong SeedFor(int a, int b, int gameIndex, ulong seedBase = 1)
            => checked(seedBase + (ulong)gameIndex * 7919 + (ulong)a * 104729 + (ulong)b * 1299709);
        public static Aggregation Run(
            CardCatalog catalog,
            IReadOnlyList<DeckDefinition> decks,
            SimOptions options,
            CardUsageTable? usage = null)
        {
            var specs = decks.Select(d => new DeckSpec(d.Faction, d.Leader, d.Name, d.Cards)).ToList();
            var agg = new Aggregation();
            foreach (var spec in specs) agg.Order.Add(spec.Faction);

            RunInto(catalog, decks, options, agg, usage);
            return agg;
        }

        /// <summary>
        /// Runs the full round robin into a caller-supplied aggregation, and
        /// optionally into a card-usage table. Used by --matrix so every
        /// playstyle cell reuses the identical driver.
        /// </summary>
        public static void RunInto(
            CardCatalog catalog,
            IReadOnlyList<DeckDefinition> decks,
            SimOptions options,
            Aggregation agg,
            CardUsageTable? usage)
        {
            if (usage is not null) usage.Playstyle = options.Playstyle ?? "(harness-heuristic)";
            var specs = decks.Select(d => new DeckSpec(d.Faction, d.Leader, d.Name, d.Cards)).ToList();
            if (agg.Order.Count == 0)
            {
                foreach (var spec in specs) agg.Order.Add(spec.Faction);
            }

            for (var a = 0; a < specs.Count; a++)
            {
                for (var b = 0; b < specs.Count; b++)
                {
                    if (a == b) continue;
                    var player0 = specs[a];
                    var player1 = specs[b];
                    // Seed chunking: this environment terminates a harness
                    // process after roughly 90 seconds of sustained work, so a
                    // 720-game cell is run as several processes over disjoint
                    // seed ranges. Seeds and first-player alternation are
                    // unchanged, so the merged result is the same cell.
                    var chunkStart = options.SeedChunkSize > 0 ? options.SeedChunkIndex * options.SeedChunkSize : 0;
                    var chunkEnd = options.SeedChunkSize > 0
                        ? Math.Min(options.GamesPerPairing, chunkStart + options.SeedChunkSize)
                        : options.GamesPerPairing;
                    for (var gameIndex = chunkStart; gameIndex < chunkEnd; gameIndex++)
                    {
                        var firstPlayer = gameIndex % 2;
                        var seed = SeedFor(a, b, gameIndex, options.SeedBase);
                        GameResult result;
                        try
                        {
                            result = new SingleMatch(catalog, player0, player1, seed, firstPlayer, options).Play();
                        }
                        catch (Exception exception)
                        {
                            result = new GameResult
                            {
                                // The message alone hides WHERE a reflection or
                                // binding failure came from, which is exactly the
                                // information needed to tell an engine-revision
                                // mismatch from a harness bug.
                                Exception = exception.GetType().Name + ": " + exception.Message,
                                ExceptionDetail = exception.ToString(),
                            };
                            agg.Exceptions++;
                            if (agg.ExceptionSamples.Count < 5)
                            {
                                agg.ExceptionSamples.Add(
                                    $"{player0.Faction} vs {player1.Faction} seed {seed}: {result.Exception}" +
                                    (result.ExceptionDetail is null
                                        ? string.Empty
                                        : Environment.NewLine + "        " +
                                          string.Join(Environment.NewLine + "        ",
                                              result.ExceptionDetail
                                                  .Split('\n')
                                                  .Select(line => line.TrimEnd('\r'))
                                                  .Where(line => line.TrimStart().StartsWith("at ", StringComparison.Ordinal))
                                                  .Take(3))));
                            }
                        }

                        agg.Record(player0, player1, firstPlayer, result);
                        if (options.MatchLogPath is not null) DriverArtifacts.AppendMatch(options.MatchLogPath, player0, player1, seed, firstPlayer, result);
                        if (usage is not null && result.UsageState is not null)
                        {
                            usage.Scan(result.UsageState, player0, player1);
                        }

                        if (options.Verbose)
                        {
                            Console.WriteLine($"  {player0.Faction} vs {player1.Faction} seed={seed,7} p0={firstPlayer} " +
                                              $"turns={result.Turns,3} decided={result.Decided} reason={result.Reason} " +
                                              $"pull={result.MachinePullCount} root={result.WoodRootStacks}");
                        }
                    }
                }
            }
        }

        private static string PlaystyleFingerprint(SimOptions options)
        {
            if (options.Playstyle is null) return "none";
            return PlaystyleRegistry.TryGetPlaystyle(options.Playstyle, out var playstyle)
                ? WeightedPlaystyle.WeightFingerprint(playstyle)
                : "unknown";
        }

        /// <summary>
        /// The effective punish-response stance this run applied, as the shipped
        /// canonical token. Both players share one policy instance, so the same
        /// token is authoritative for both sides; it is recorded per side so a
        /// report never has to infer it from the absence of an injection.
        /// </summary>
        private static string PunishStance(SimOptions options)
        {
            if (options.Playstyle is null)
            {
                return options.DeclinePunish ? "never (legacy --decline-punish)" : "always (legacy greedy-accept)";
            }

            return PlaystyleRegistry.TryGetPlaystyle(options.Playstyle, out var playstyle)
                ? PunishResponseStances.IdOf(playstyle.ResponseStance)
                : "unknown";
        }

        public static string ToJson(Aggregation agg, SimOptions options, IReadOnlyList<DeckDefinition> decks)
        {            var invariant = CultureInfo.InvariantCulture;
            var builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"engine\": \"DominionWars.Engine.dll (build-output/pl-verify/bin/Release/net8.0)\",\n");
            builder.Append($"  \"policy\": \"{options.PolicyName}\",\n");
            builder.Append($"  \"playstyleId\": {(options.Playstyle is null ? "null" : "\"" + options.Playstyle + "\"")},\n");
            builder.Append($"  \"playstyleFingerprint\": \"{PlaystyleFingerprint(options)}\",\n");
            builder.Append($"  \"punishResponseStance\": \"{PunishStance(options)}\",\n");
            builder.Append("  \"punishResponseStancePerSide\": {\"sideA\": \"" + PunishStance(options) +
                           "\", \"sideB\": \"" + PunishStance(options) + "\"},\n");
            builder.Append($"  \"punishResponseStanceAppliesTo\": \"{EngineStamp.PunishStanceAppliesTo}\",\n");
            builder.Append(EngineStamp.ToJsonFields());
            builder.Append("  \"factionTurnSums\": {" +
                           string.Join(", ", agg.Order.Select(f => $"\"{f}\":{agg.ByFaction[f].TurnSum}")) +
                           "},\n");
            builder.Append($"  \"duplicateAdvertisementDrops\": {MatrixPolicy.DuplicateAdvertisementDrops},\n");
            builder.Append("  \"factionGames\": {" +
                           string.Join(", ", agg.Order.Select(f =>
                           {
                               var r = agg.ByFaction[f];
                               return $"\"{f}\":{r.Wins + r.Losses + r.Draws}";
                           })) + "},\n");
            builder.Append($"  \"variant\": \"{options.Variant}\",\n");
            builder.Append($"  \"maxResponsesPerRound\": {options.MaxResponsesPerRound},\n");
            builder.Append($"  \"maxPunishResponsesPerRound\": {options.MaxPunishResponsesPerRound},\n");
            builder.Append($"  \"engineCapApplied\": {(MatchRulesFactory.EffectiveEngineCap.HasValue ? MatchRulesFactory.EffectiveEngineCap.Value.ToString(CultureInfo.InvariantCulture) : "null")},\n");
            builder.Append($"  \"engineSupportsCap\": {(MatchRulesFactory.SupportsEngineCap ? "true" : "false")},\n");
            builder.Append($"  \"engineRulesDescription\": \"{MatchRulesFactory.Describe.Replace("\\", "/")}\",\n");
            builder.Append("  \"balanceJsonRead\": false,\n");
            builder.Append("  \"t1Mechanism\": \"" +
                           (options.MaxPunishResponsesPerRound > 0 ? "engine-rule" : "off") +
                           (options.MaxResponsesPerRound > 0 ? "+policy-emulation" : "") + "\",\n");
            builder.Append($"  \"declinedByRoundCap\": {agg.DeclinedByRoundCap},\n");
            builder.Append($"  \"noBuffFaction\": {(options.NoBuffFaction is null ? "null" : "\"" + options.NoBuffFaction + "\"")},\n");
            builder.Append($"  \"punishDrawInitial\": {agg.PunishDrawInitial},\n");
            builder.Append($"  \"punishDrawResponse\": {agg.PunishDrawResponse},\n");
            builder.Append($"  \"punishDrawInitial0\": {agg.PunishDrawInitial0},\n");
            builder.Append($"  \"punishDrawInitial1\": {agg.PunishDrawInitial1},\n");
            builder.Append($"  \"punishDrawResponse0\": {agg.PunishDrawResponse0},\n");
            builder.Append($"  \"punishDrawResponse1\": {agg.PunishDrawResponse1},\n");
            builder.Append($"  \"expiredInactivePunishCards\": {agg.ExpiredPunish},\n");
            builder.Append($"  \"activatableCopiesRemaining\": {CatalogVariant.ActivatableCopiesRemaining(options.Variant)},\n");
            builder.Append($"  \"clearedKinds\": \"{CatalogVariant.ClearedKindsFor(options.Variant)}\",\n");
            builder.Append($"  \"punishChains\": {agg.PunishChains},\n");
            builder.Append($"  \"punishTriggered\": {agg.PunishTriggered},\n");
            builder.Append($"  \"maxChainDepth\": {agg.MaxPunishChainDepth},\n");
            builder.Append($"  \"maxChainDepthFromPolicy\": {agg.MaxChainDepthFromPolicy},\n");
            builder.Append("  \"chainDepthHistogram\": {" +
                           string.Join(", ", agg.ChainDepthHistogram.OrderBy(p => p.Key).Select(p => $"\"{p.Key}\":{p.Value}")) +
                           "},\n");
            builder.Append("  \"punishDrawsPerTurnHistogram\": {" +
                           string.Join(", ", agg.PunishDrawsPerTurnHistogram.OrderBy(p => p.Key).Select(p => $"\"{p.Key}\":{p.Value}")) +
                           "},\n");
            builder.Append("  \"behavior\": [\n" + string.Join(",\n", agg.Order.Select(f =>
            {
                var b = agg.BehaviorByFaction[f];
                var turns = Math.Max(1, b.TurnsTaken);
                return "    {" + $"\"faction\":\"{f}\",\"turns\":{b.TurnsTaken}," +
                       $"\"playsPerTurn\":{(b.Plays / (double)turns).ToString("F3", invariant)}," +
                       $"\"punishPlaysPerTurn\":{(b.PlaysPunishActivated / (double)turns).ToString("F3", invariant)}," +
                       $"\"attacksPerTurn\":{(b.Attacks / (double)turns).ToString("F3", invariant)}," +
                       $"\"attackPowerPerTurn\":{(b.AttackDamageDealt / (double)turns).ToString("F3", invariant)}," +
                       $"\"punishActivationsTaken\":{b.PunishActivationsTaken}," +
                       $"\"punishActivationsDeclinedByBudget\":{b.PunishActivationsDeclinedByBudget}," +
                       $"\"buffCardsPlayed\":{b.BuffCardsPlayed},\"commits\":{b.Commits},\"pulls\":{b.Pulls}" + "}";
            })) + "\n  ],\n");
            builder.Append($"  \"gamesPerPairing\": {options.GamesPerPairing},\n");
            builder.Append($"  \"turnCap\": {options.TurnCap},\n");
            builder.Append($"  \"declinePunish\": {(options.DeclinePunish ? "true" : "false")},\n");
            builder.Append($"  \"castleEnabled\": {(options.CastleEnabled ? "true" : "false")},\n");
            builder.Append($"  \"castleHealth\": {options.CastleHealth},\n");
            builder.Append($"  \"playerLife\": {options.PlayerLife},\n");
            builder.Append($"  \"openingHandSize\": {options.OpeningHandSize},\n");
            builder.Append("  \"decks\": [" + string.Join(", ", decks.Select(d => "\"" + d.Faction + "/" + d.Leader + "\"")) + "],\n");
            builder.Append("  \"factions\": [\n");
            builder.Append(string.Join(",\n", agg.Order.Select(f =>
            {
                var row = agg.ByFaction[f];
                var played = row.Wins + row.Losses + row.Draws;
                return "    {" + $"\"faction\":\"{f}\",\"wins\":{row.Wins},\"losses\":{row.Losses},\"draws\":{row.Draws}," +
                       $"\"games\":{played},\"winRate\":{(played == 0 ? 0 : 100.0 * row.Wins / played).ToString("F2", invariant)}," +
                       $"\"avgTurns\":{(played == 0 ? 0 : row.TurnSum / (double)played).ToString("F2", invariant)}," +
                       $"\"winsAsP0\":{row.WinsAsP0},\"gamesAsP0\":{row.GamesAsP0}," +
                       $"\"winsAsP1\":{row.WinsAsP1},\"gamesAsP1\":{row.GamesAsP1}" + "}";
            })));
            builder.Append("\n  ],\n");
            builder.Append("  \"winReasons\": {" + string.Join(", ", agg.ReasonCounts.Select(p => $"\"{p.Key}\":{p.Value}")) + "},\n");
            builder.Append("  \"winReasonsByFaction\": {" +
                           string.Join(", ", agg.WinReasonsByFaction.OrderBy(p => p.Key, StringComparer.Ordinal)
                               .Select(p => $"\"{p.Key}\":{p.Value}")) + "},\n");
            builder.Append($"  \"decided\": {agg.Decided},\n");
            builder.Append($"  \"capped\": {agg.Capped},\n");
            builder.Append($"  \"noWinnerNoCap\": {agg.NoWinnerNoCap},\n");
            builder.Append($"  \"exceptions\": {agg.Exceptions},\n");
            builder.Append($"  \"rejections\": {agg.Rejections},\n");
            builder.Append("  \"rejectReasons\": {" + string.Join(", ", agg.RejectReasons.Select(p => $"\"{p.Key}\":{p.Value}")) + "},\n");
            builder.Append($"  \"machinePullAvg\": {agg.MachinePullAvg.ToString("F3", invariant)},\n");
            builder.Append($"  \"machinePullMax\": {agg.MachinePullMax},\n");
            builder.Append($"  \"machinePullGe6AtWin\": {agg.MachinePullGe6AtWin},\n");
            builder.Append($"  \"machinePullAtTurn5Avg\": {agg.MachinePullAtTurn5Avg.ToString("F2", invariant)},\n");
            builder.Append($"  \"machinePullAtTurn10Avg\": {agg.MachinePullAtTurn10Avg.ToString("F2", invariant)},\n");
            builder.Append($"  \"machineGames\": {agg.MachineGames},\n");
            builder.Append($"  \"woodRootAvg\": {agg.WoodRootAvg.ToString("F2", invariant)},\n");
            builder.Append($"  \"woodRootMax\": {agg.WoodRootMax},\n");
            builder.Append($"  \"woodRootAtTurn5Avg\": {agg.WoodRootAtTurn5Avg.ToString("F2", invariant)},\n");
            builder.Append($"  \"woodRootAtTurn10Avg\": {agg.WoodRootAtTurn10Avg.ToString("F2", invariant)},\n");
            builder.Append($"  \"woodGames\": {agg.WoodGames},\n");
            builder.Append($"  \"ambushSets\": {agg.AmbushSets},\n");
            builder.Append($"  \"commits\": {agg.Commits},\n");
            builder.Append($"  \"pulls\": {agg.Pulls},\n");
            builder.Append($"  \"attacks\": {agg.Attacks},\n");
            builder.Append($"  \"plays\": {agg.Plays},\n");
            builder.Append($"  \"driverGuardHits\": {agg.DriverGuardHits},\n");
            builder.Append($"  \"maxMinionHealthSeen\": {agg.MaxMinionHealthMax},\n");
            builder.Append($"  \"maxSealedHealthSeen\": {agg.MaxSealedHealthMax},\n");
            builder.Append($"  \"avgCombinedHandAtEnd\": {(agg.EndedGames == 0 ? 0 : agg.EndHandSum / (double)agg.EndedGames).ToString("F2", invariant)},\n");
            builder.Append($"  \"avgCombinedDeckAtEnd\": {(agg.EndedGames == 0 ? 0 : agg.EndDeckSum / (double)agg.EndedGames).ToString("F2", invariant)}\n");
            builder.Append("}\n");
            return builder.ToString();
        }
    }

    internal sealed class FactionRow
    {
        public int Wins;
        public int Losses;
        public int Draws;
        public long TurnSum;
        public int WinsAsP0;
        public int GamesAsP0;
        public int WinsAsP1;
        public int GamesAsP1;
    }

    internal sealed class Aggregation
    {
        public readonly List<string> Order = new List<string>();
        public readonly Dictionary<string, FactionRow> ByFaction = new Dictionary<string, FactionRow>(StringComparer.Ordinal);
        public readonly Dictionary<(string, string), FactionRow> Matchups = new Dictionary<(string, string), FactionRow>();
        public readonly Dictionary<string, int> ReasonCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> RejectReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly List<string> ExceptionSamples = new List<string>();
        public readonly List<string> RejectSamples = new List<string>();
        public readonly List<string> DriverGuardSamples = new List<string>();

        public int Decided;
        public int Capped;
        public int NoWinnerNoCap;
        public int Exceptions;
        public long MachinePullSum;
        public int MachinePullMax;
        public int MachinePullGe6AtWin;
        public int MachineGames;
        public long MachinePullAtTurn5Sum;
        public int MachinePullAtTurn5Count;
        public long MachinePullAtTurn10Sum;
        public int MachinePullAtTurn10Count;
        public long WoodRootSum;
        public int WoodRootMax;
        public int WoodGames;
        public long WoodRootAtTurn5Sum;
        public int WoodRootAtTurn5Count;
        public long WoodRootAtTurn10Sum;
        public int WoodRootAtTurn10Count;
        public long AmbushSets;
        public long Commits;
        public long Pulls;
        public long Attacks;
        public long Plays;
        public long Rejections;
        public long DriverGuardHits;
        public int MaxMinionHealthMax;
        public int MaxSealedHealthMax;
        public long EndHandSum;
        public long EndDeckSum;
        public int EndedGames;
        public int MaxHandSeen;
        public long PunishDrawCards;
        public long PunishDrawEvents;
        public long OrdinaryDrawCards;
        public long PunishDrawInitial;
        public long PunishDrawResponse;
        public long PunishDrawInitial0;
        public long PunishDrawInitial1;
        public long PunishDrawResponse0;
        public long PunishDrawResponse1;
        public long ExpiredPunish;
        public long ExpiredPunish0;
        public long ExpiredPunish1;
        public readonly Dictionary<string, int> WinReasonsByFaction = new Dictionary<string, int>(StringComparer.Ordinal);
        public long PunishChains;
        public long PunishTriggered;
        public int MaxPunishChainDepth;
        public int MaxChainDepthFromPolicy;
        public long RecordedPunishDecides;
        public long RecordedPunishAccepts;
        public long DeclinedByRoundCap;
        public readonly Dictionary<int, int> ChainDepthHistogram = new Dictionary<int, int>();
        public readonly Dictionary<int, int> PunishDrawsPerTurnHistogram = new Dictionary<int, int>();
        public readonly Dictionary<string, FactionBehavior> BehaviorByFaction = new Dictionary<string, FactionBehavior>(StringComparer.Ordinal);

        public double MachinePullAvg => MachineGames == 0 ? 0 : MachinePullSum / (double)MachineGames;
        public double MachinePullAtTurn5Avg => MachinePullAtTurn5Count == 0 ? 0 : MachinePullAtTurn5Sum / (double)MachinePullAtTurn5Count;
        public double MachinePullAtTurn10Avg => MachinePullAtTurn10Count == 0 ? 0 : MachinePullAtTurn10Sum / (double)MachinePullAtTurn10Count;
        public double WoodRootAvg => WoodGames == 0 ? 0 : WoodRootSum / (double)WoodGames;
        public double WoodRootAtTurn5Avg => WoodRootAtTurn5Count == 0 ? 0 : WoodRootAtTurn5Sum / (double)WoodRootAtTurn5Count;
        public double WoodRootAtTurn10Avg => WoodRootAtTurn10Count == 0 ? 0 : WoodRootAtTurn10Sum / (double)WoodRootAtTurn10Count;

        public void Record(DeckSpec player0, DeckSpec player1, int firstPlayer, GameResult result)
        {
            var factions = new[] { player0.Faction, player1.Faction };
            foreach (var faction in factions)
            {
                if (!ByFaction.TryGetValue(faction, out var row))
                {
                    row = new FactionRow();
                    ByFaction[faction] = row;
                }

                row.TurnSum += result.Turns;
            }

            // Faction totals follow the matchup matrix exactly.
            if (result.Decided && result.WinnerIndex == 0)
            {
                ByFaction[player0.Faction].Wins += 1;
                ByFaction[player1.Faction].Losses += 1;
            }
            else if (result.Decided && result.WinnerIndex == 1)
            {
                ByFaction[player1.Faction].Wins += 1;
                ByFaction[player0.Faction].Losses += 1;
            }
            else
            {
                ByFaction[player0.Faction].Draws += 1;
                ByFaction[player1.Faction].Draws += 1;
            }

            for (var index = 0; index < 2; index++)
            {
                var isFirst = index == firstPlayer;
                var row = ByFaction[factions[index]];
                if (isFirst) row.GamesAsP0 += 1;
                else row.GamesAsP1 += 1;
                if (result.Decided && result.WinnerIndex == index)
                {
                    if (isFirst) row.WinsAsP0 += 1;
                    else row.WinsAsP1 += 1;
                }
            }

            var key = (player0.Faction, player1.Faction);
            if (!Matchups.TryGetValue(key, out var matchup))
            {
                matchup = new FactionRow();
                Matchups[key] = matchup;
            }

            // Punish-chain decomposition and per-faction behaviour, attributed
            // by faction (never by player index) so cross-faction comparisons
            // survive the alternating first player.
            PunishDrawInitial += result.PunishDrawInitial0 + result.PunishDrawInitial1;
            PunishDrawResponse += result.PunishDrawResponse0 + result.PunishDrawResponse1;
            PunishDrawInitial0 += result.PunishDrawInitial0;
            PunishDrawInitial1 += result.PunishDrawInitial1;
            PunishDrawResponse0 += result.PunishDrawResponse0;
            PunishDrawResponse1 += result.PunishDrawResponse1;
            ExpiredPunish += result.ExpiredPunish0 + result.ExpiredPunish1;
            ExpiredPunish0 += result.ExpiredPunish0;
            ExpiredPunish1 += result.ExpiredPunish1;

            // Per-faction win-reason attribution: which victory axis actually
            // closes each faction's games.
            if (result.WinReasonByPlayer.Length > 0)
            {
                var split = result.WinReasonByPlayer.Split(':');
                if (split.Length == 2
                    && int.TryParse(split[0], out var winnerIndex)
                    && winnerIndex is 0 or 1)
                {
                    var winnerFaction = winnerIndex == 0 ? player0.Faction : player1.Faction;
                    var reasonKey = winnerFaction + "|" + split[1];
                    WinReasonsByFaction[reasonKey] = WinReasonsByFaction.TryGetValue(reasonKey, out var seen) ? seen + 1 : 1;
                }
            }
            PunishChains += result.PunishChains;
            PunishTriggered += result.PunishEventCount0 + result.PunishEventCount1;
            if (result.MaxPunishChainDepth > MaxPunishChainDepth) MaxPunishChainDepth = result.MaxPunishChainDepth;
            if (result.RecordedMaxChainDepth > MaxChainDepthFromPolicy) MaxChainDepthFromPolicy = result.RecordedMaxChainDepth;
            RecordedPunishDecides += result.RecordedPunishDecides;
            RecordedPunishAccepts += result.RecordedPunishAccepts;
            DeclinedByRoundCap += result.DeclinedByRoundCap;
            MergeHistogram(ChainDepthHistogram, result.ChainDepthHistogram);
            MergeHistogram(ChainDepthHistogram, result.RecordedDepthHistogram);
            MergeHistogram(PunishDrawsPerTurnHistogram, result.PunishDrawsPerTurnHistogram);

            var index0 = player0.Faction;
            var index1 = player1.Faction;
            var behavior0 = FactionBehaviorFor(index0);
            var behavior1 = FactionBehaviorFor(index1);
            MergeBehavior(behavior0, result.Behavior0);
            MergeBehavior(behavior1, result.Behavior1);

            matchup.TurnSum += result.Turns;
            if (!Matchups.TryGetValue((player1.Faction, player0.Faction), out var reverse))
            {
                reverse = new FactionRow();
                Matchups[(player1.Faction, player0.Faction)] = reverse;
            }

            // Matchup matrix counts the row faction's wins against the column.
            var rowFaction = player0.Faction;
            var columnFaction = player1.Faction;
            if (result.Decided && result.WinnerIndex == 0)
            {
                Matchups[(rowFaction, columnFaction)].Wins += 1;
                Matchups[(columnFaction, rowFaction)].Losses += 1;
            }
            else if (result.Decided && result.WinnerIndex == 1)
            {
                Matchups[(columnFaction, rowFaction)].Wins += 1;
                Matchups[(rowFaction, columnFaction)].Losses += 1;
            }
            else
            {
                Matchups[(rowFaction, columnFaction)].Draws += 1;
                Matchups[(columnFaction, rowFaction)].Draws += 1;
            }

            if (result.Exception is not null)
            {
                ReasonCounts["exception"] = ReasonCounts.TryGetValue("exception", out var exceptions) ? exceptions + 1 : 1;
                return;
            }

            if (result.DriverGuardReason.Length > 0 && DriverGuardSamples.Count < 5)
            {
                DriverGuardSamples.Add(result.DriverGuardReason);
            }

            ReasonCounts[result.Reason] = ReasonCounts.TryGetValue(result.Reason, out var reasonCount) ? reasonCount + 1 : 1;
            if (result.Decided)
            {
                Decided++;
            }
            else if (result.Capped)
            {
                Capped++;
            }
            else
            {
                NoWinnerNoCap++;
            }

            if (player0.Faction == "机械遗迹") AccumulateMachine(result);
            if (player1.Faction == "机械遗迹") AccumulateMachine(result);
            if (player0.Faction == "古木圣地") AccumulateWood(result);
            if (player1.Faction == "古木圣地") AccumulateWood(result);

            if (result.MaxMinionHealth > MaxMinionHealthMax) MaxMinionHealthMax = result.MaxMinionHealth;
            if (result.MaxSealedHealth > MaxSealedHealthMax) MaxSealedHealthMax = result.MaxSealedHealth;
            EndHandSum += result.EndHand0 + result.EndHand1;
            EndDeckSum += result.EndDeck0 + result.EndDeck1;
            if (result.MaxHand0 > MaxHandSeen) MaxHandSeen = result.MaxHand0;
            if (result.MaxHand1 > MaxHandSeen) MaxHandSeen = result.MaxHand1;
            PunishDrawCards += result.PunishDrawCards0 + result.PunishDrawCards1;
            PunishDrawEvents += result.PunishDrawEvents0 + result.PunishDrawEvents1;
            OrdinaryDrawCards += result.DrawCards0 + result.DrawCards1;
            EndedGames++;

            var counters = result.Counters;
            AmbushSets += counters.AmbushSets;
            Plays += counters.Plays;
            Attacks += counters.Attacks;
            Commits += counters.Commits;
            Pulls += counters.Pulls;
            Rejections += counters.Rejections;
            DriverGuardHits += result.DriverGuardHits;
            foreach (var pair in counters.RejectReasons)
            {
                RejectReasons[pair.Key] = RejectReasons.TryGetValue(pair.Key, out var existing) ? existing + pair.Value : pair.Value;
            }

            foreach (var sample in counters.RejectSamples)
            {
                if (RejectSamples.Count < 12) RejectSamples.Add(sample);
            }
        }

        private static void MergeHistogram(Dictionary<int, int> target, Dictionary<int, int> source)
        {
            foreach (var pair in source)
            {
                target[pair.Key] = target.TryGetValue(pair.Key, out var existing) ? existing + pair.Value : pair.Value;
            }
        }

        private static void MergeBehavior(FactionBehavior target, FactionBehavior source)
        {
            target.Plays += source.Plays;
            target.PlaysPunishActivated += source.PlaysPunishActivated;
            target.Attacks += source.Attacks;
            target.Commits += source.Commits;
            target.Pulls += source.Pulls;
            target.Ambushes += source.Ambushes;
            target.PunishActivationsTaken += source.PunishActivationsTaken;
            target.PunishActivationsDeclinedByBudget += source.PunishActivationsDeclinedByBudget;
            target.BuffCardsPlayed += source.BuffCardsPlayed;
            target.TurnsTaken += source.TurnsTaken;
            target.AttackDamageDealt += source.AttackDamageDealt;
        }

        private static int DepthCount(GameResult result, int depth)
        {
            var total = 0;
            if (result.ChainDepthHistogram.TryGetValue(depth, out var fromEvents)) total += fromEvents;
            if (result.RecordedDepthHistogram.TryGetValue(depth, out var fromPolicy)) total += fromPolicy;
            return total;
        }

        private FactionBehavior FactionBehaviorFor(string faction)
        {
            if (!BehaviorByFaction.TryGetValue(faction, out var behavior))
            {
                behavior = new FactionBehavior();
                BehaviorByFaction[faction] = behavior;
            }

            return behavior;
        }

        private void AccumulateMachine(GameResult result)        {
            MachineGames++;
            MachinePullSum += result.MachinePullCount;
            if (result.MachinePullCount > MachinePullMax) MachinePullMax = result.MachinePullCount;
            if (result.Decided && result.MachinePullCount >= 6) MachinePullGe6AtWin++;
            if (result.MachinePullAtTurn5.HasValue)
            {
                MachinePullAtTurn5Sum += result.MachinePullAtTurn5.Value;
                MachinePullAtTurn5Count++;
            }

            if (result.MachinePullAtTurn10.HasValue)
            {
                MachinePullAtTurn10Sum += result.MachinePullAtTurn10.Value;
                MachinePullAtTurn10Count++;
            }
        }

        private void AccumulateWood(GameResult result)
        {
            WoodGames++;
            WoodRootSum += result.WoodRootStacks;
            if (result.WoodRootStacks > WoodRootMax) WoodRootMax = result.WoodRootStacks;
            if (result.WoodRootAtTurn5.HasValue)
            {
                WoodRootAtTurn5Sum += result.WoodRootAtTurn5.Value;
                WoodRootAtTurn5Count++;
            }

            if (result.WoodRootAtTurn10.HasValue)
            {
                WoodRootAtTurn10Sum += result.WoodRootAtTurn10.Value;
                WoodRootAtTurn10Count++;
            }
        }
    }

    /// <summary>One real C# match: MatchSetup + TurnFlow + TurnActionRouter + controller.</summary>
    internal sealed class SingleMatch
    {
        private readonly CardCatalog _catalog;
        private readonly DeckSpec _player0;
        private readonly DeckSpec _player1;
        private readonly ulong _seed;
        private readonly int _firstPlayer;
        private readonly SimOptions _options;
        private readonly CsimPolicy _policy;
        private readonly IMatchPolicy _matchPolicy;
        private readonly DriverObserver? _observer;
        private GameState? _state;
        private GameResult? _activeResult;
        private RecordingPunishPolicy? _punishRecorder;

        /// <summary>Set when this match is driven through the shipped adapter gateway.</summary>
        private AdapterRuntime.RuntimeMatchGateway? _gateway;
        private string? _matchId;

        public SingleMatch(
            CardCatalog catalog,
            DeckSpec player0,
            DeckSpec player1,
            ulong seed,
            int firstPlayer,
            SimOptions options)
        {
            _catalog = catalog;
            _player0 = player0;
            _player1 = player1;
            _seed = seed;
            _firstPlayer = firstPlayer;
            _options = options;
            _observer = options.ObserverFactory?.Invoke(seed, firstPlayer, player0, player1);
            _policy = new CsimPolicy
            {
                Aggressive = options.Aggressive,
                NoBuffPlayForFaction = options.NoBuffFaction,
            };
            _matchPolicy = options.Playstyle is null
                ? (IMatchPolicy)new HarnessPolicy(options.Aggressive, options.NoBuffFaction)
                : new MatrixPolicy(PlaystyleRegistry.GetPlaystyle(options.Playstyle));
        }

        public GameResult Play()
        {
            var state = MatchSetup.Create(
                _player0.ToMatchDeck(),
                _player1.ToMatchDeck(),
                _catalog.Cards,
                new MatchSetupOptions
                {
                    Seed = _seed,
                    FirstPlayerIndex = _firstPlayer,
                    OpeningHandSize = _options.OpeningHandSize,
                    PlayerLife = _options.PlayerLife,
                    CastleEnabled = _options.CastleEnabled,
                    CastleHealth = _options.CastleHealth,
                    // The harness supplies MatchRules itself; nothing here reads
                    // data/balance.json. MaxPunishResponsesPerRound is the ENGINE
                    // rule (T1), distinct from the policy-level emulation.
                    //
                    // The fallback goes through MatchRulesFactory rather than a
                    // bare `new MatchRules()`: the parameterless ctor binds to
                    // whichever revision the ASSEMBLY was compiled against, so a
                    // caller that forgot to set Rules got a silent engine-revision
                    // mismatch instead of a diagnosable error. Building through
                    // the factory uses the widest public ctor by reflection, which
                    // is the same path the working matrix mode uses.
                    Rules = _options.Rules ?? MatchRulesFactory.Create(_options),
                });

            var flow = TurnFlow.CreateDefault();
            _state = state;
            // Each shipped playstyle owns its own punish-response stance
            // (PlaystyleRegistry.PunishResponsesFor). Playstyle runs therefore use
            // the REGISTRY policy, wrapped only to record what it decided; the
            // legacy harness path keeps its own accept/decline switch.
            _punishRecorder = _options.Playstyle is not null
                ? new RecordingPunishPolicy(
                    PlaystyleRegistry.PunishResponsesFor(_options.Playstyle),
                    _options.MaxResponsesPerRound)
                : new RecordingPunishPolicy(
                    accept: !_options.DeclinePunish,
                    maxResponsesPerRound: _options.MaxResponsesPerRound);
            var router = TurnActionRouter.CreateDefault(
                flow,
                targetPolicy: null,
                punishResponses: _punishRecorder);
            var controller = new MatchController(state, flow, router);
            _controller = controller;
            if (_options.Playstyle is not null)
            {
                // Drive the SHIPPED adapter boundary: the gateway builds the
                // viewer-safe snapshot, validates the submitted action against
                // it, and converts it to the engine request.
                _matchId = "match_plcsim";
                _gateway = new AdapterRuntime.RuntimeMatchGateway(_matchId, state, flow, router);
                var initialization = _gateway.Initialize(state.CurrentPlayerIndex);
                if (!initialization.Accepted)
                {
                    throw new InvalidOperationException("gateway_initialize_failed:" + initialization.ReasonKey);
                }
            }
            else
            {
                // Legacy path: one-time START lifecycle, as before.
                flow.Advance(state, state.CurrentPlayerIndex);
            }


            var result = new GameResult();
            _activeResult = result;
            var actionGuard = 0;
            var turns = 0;
            var traceBudget = _options.Trace ? 140 : 0;
            var traceSeed = _options.TraceSeed;
            var _actionPhaseCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var _actionPhaseTotal = 0;
            var lastActionTurn = -1;
            var _lastProgressKey = string.Empty;
            var _sameProgressRepeats = 0;
            var lastRequestedActionId = string.Empty;
            var lastRejectReason = string.Empty;

            // Initialize/legacy START above already advanced once. Advancing again
            // would skip the first player's AMBUSH phase.

            // Behaviour attribution: every submitted action is counted against
            // the acting player's faction, so cross-faction comparisons can be
            // checked for policy asymmetry.
            _policy.OnPunishActivation = declined =>
            {
                var behavior = state.CurrentPlayerIndex == 0 ? result.Behavior0 : result.Behavior1;
                if (declined) behavior.PunishActivationsDeclinedByBudget++;
                else behavior.PunishActivationsTaken++;
            };

            while (!state.WinnerPlayerIndex.HasValue && turns < _options.TurnCap)
            {
                turns = state.Turn.Number;
                if (lastActionTurn != state.Turn.Number)
                {
                    lastActionTurn = state.Turn.Number;
                    _actionPhaseCounts.Clear();
                    _actionPhaseTotal = 0;
                }

                result.MachinePullAtTurn5 ??= PullSnapshot(state, 5);
                result.MachinePullAtTurn10 ??= PullSnapshot(state, 10);
                result.WoodRootAtTurn5 ??= RootSnapshot(state, 5);
                result.WoodRootAtTurn10 ??= RootSnapshot(state, 10);

                // The gateway owns the snapshot revision the boundary validates
                // against; hand it to the policy before every choice.
                if (_gateway is not null) _matchPolicy.SnapshotRevision = _gateway.SnapshotRevision;

                var actor = state.CurrentPlayerIndex;
                if (actor == 0) result.Behavior0.TurnsTaken++;
                else result.Behavior1.TurnsTaken++;
                if (actor == 0) result.BoardAttackSum0 += BoardAttack(state.GetPlayer(0));
                else result.BoardAttackSum1 += BoardAttack(state.GetPlayer(1));
                var handled = false;
                var trace = traceBudget > 0 && traceSeed == _seed;
                void Trace(string what)
                {
                    if (!trace || traceBudget <= 0) return;
                    traceBudget--;
                    var machine = _player0.Faction == "机械遗迹" ? state.GetPlayer(0)
                        : _player1.Faction == "机械遗迹" ? state.GetPlayer(1) : null;
                    Console.WriteLine($"      [T{state.Turn.Number} {state.Turn.PhaseId} p{actor}] {what,-46} " +
                                      $"machinePull={(machine?.PullCount.ToString(CultureInfo.InvariantCulture) ?? "-")} " +
                                      $"cloud={machine?.CloudStack.Count.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
                                      $"queue={machine?.CommitQueue.Count.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
                                      $"hand={state.GetPlayer(actor).Hand.Count} deck={state.GetPlayer(actor).Deck.Count}");
                }
                switch (state.Turn.PhaseId)
                {
                    case TurnPhase.Start:
                        flow.Advance(state, actor);
                        Trace("advance START -> " + state.Turn.PhaseId);
                        handled = true;
                        break;

                    case TurnPhase.Ambush:
                    {
                        var actions = controller.GetLegalActions(actor);
                        if (actions.Count == 0)
                        {
                            flow.Advance(state, actor);
                            handled = true;
                            break;
                        }

                        var action = _matchPolicy.ChooseAmbush(state, actions);
                        if (action is null)
                        {
                            flow.Advance(state, actor);
                            handled = true;
                            break;
                        }

                        ObserveDecision(state, actions, action);
                        actionGuard++; lastRequestedActionId = action.ActionId;
                        var submission = Submit(actor, action);
                        if (submission.Accepted)
                        {
                            if (action.Type == TurnAction.SetAmbush) result.Counters.AmbushSets++;
                        }
                        else
                        {
                            RecordReject(result, submission.ReasonKey, state, action); lastRejectReason = submission.ReasonKey;
                            var skip = actions.FirstOrDefault(a => a.Type == TurnAction.SkipAmbush);
                            if (skip is not null)
                            {
                                actionGuard++; lastRequestedActionId = action.ActionId;
                                Submit(actor, skip);
                            }
                        }

                        handled = true;
                        break;
                    }

                    case TurnPhase.Action:
                    {
                        var actions = controller.GetLegalActions(actor);
                        if (actions.Count == 0)
                        {
                            // No advertised action in ACTION: the engine expects
                            // the caller to end the turn.
                            var endOnly = flow.GetLegalActions(state, actor);
                            var endAction = endOnly.FirstOrDefault(a => a.Type == LegalActionGenerator.EndTurn);
                            if (endAction is null)
                            {
                                result.Reason = "driver_stall_action_no_actions";
                                goto done;
                            }

                            actionGuard++; lastRequestedActionId = endAction.ActionId;
                            Submit(actor, endAction);
                            handled = true;
                            break;
                        }

                        var action = _matchPolicy.ChooseAction(state, actions);
                        if (action is null)
                        {
                            result.Reason = "driver_stall_policy_no_action";
                            goto done;
                        }

                        ObserveDecision(state, actions, action);
                        // Defensive driver bound: a policy that keeps choosing
                        // the same advertised action inside one turn would
                        // otherwise loop forever. This is a harness guard, not
                        // an engine rule.
                        if (action.Type != LegalActionGenerator.EndTurn)
                        {
                            _actionPhaseCounts.TryGetValue(action.ActionId, out var repeats);
                            _actionPhaseCounts[action.ActionId] = repeats + 1;
                            _actionPhaseTotal++;
                            if (repeats + 1 > 3 || _actionPhaseTotal > 64)
                            {
                                result.DriverGuardHits++;
                                if (result.DriverGuardReason.Length == 0)
                                {
                                    result.DriverGuardReason =
                                        $"action-phase stall: action '{action.ActionId}' type={action.Type} " +
                                        $"card={action.CardId} picked {repeats + 1}x in one ACTION phase " +
                                        $"(phase actions={_actionPhaseTotal}) at turn={state.Turn.Number} actor={actor}";
                                }

                                result.Capped = true;
                                var forcedEnd = actions.FirstOrDefault(a => a.Type == LegalActionGenerator.EndTurn);
                                if (forcedEnd is not null)
                                {
                                    actionGuard++; lastRequestedActionId = action.ActionId;
                                    Submit(actor, forcedEnd);
                                    handled = true;
                                    break;
                                }

                                result.Capped = true;
                                result.Reason = "driver_action_guard";
                                goto done;
                            }
                        }

                        actionGuard++; lastRequestedActionId = action.ActionId;
                        var submission = Submit(actor, action);
                        Trace($"{action.Type} card={action.CardId} ok={submission.Accepted}");
                        if (submission.Accepted)
                        {
                            switch (action.Type)
                            {
                                case LegalActionGenerator.PlayCard: result.Counters.Plays++; break;
                                case LegalActionGenerator.Attack: result.Counters.Attacks++; break;
                                case LegalActionGenerator.Commit: result.Counters.Commits++; break;
                                case LegalActionGenerator.Pull: result.Counters.Pulls++; break;
                            }

                            RecordBehavior(state, result, actor, action);
                            _matchPolicy.NoteSubmission(action, submission.Accepted);
                        }
                        else
                        {
                            RecordReject(result, submission.ReasonKey, state, action); lastRejectReason = submission.ReasonKey;
                            // Defensive driver path: if an advertised action is
                            // rejected, do not retry it forever. Submit
                            // END_TURN only when the engine advertised it for
                            // this phase; otherwise force the phase forward,
                            // which the engine accepts (TurnFlow.Advance).
                            var endAction = actions.FirstOrDefault(a => a.Type == LegalActionGenerator.EndTurn);
                            if (endAction is not null && state.Turn.PhaseId == TurnPhase.Action)
                            {
                                actionGuard++; lastRequestedActionId = action.ActionId;
                                var endSubmission = Submit(actor, endAction);
                                if (!endSubmission.Accepted) RecordReject(result, endSubmission.ReasonKey, state, endAction);
                            }
                            else
                            {
                                result.ForcedPhaseAdvances++;
                                if (!state.WinnerPlayerIndex.HasValue)
                                {
                                    flow.Advance(state, state.CurrentPlayerIndex);
                                }
                            }
                        }

                        handled = true;
                        break;
                    }

                    case TurnPhase.Discard:
                    {
                        var actions = controller.GetLegalActions(actor);
                        var action = actions.FirstOrDefault(a => a.Type == TurnAction.DiscardComplete);
                        if (action is null)
                        {
                            result.Reason = "driver_missing_discard_advertisement";
                            goto done;
                        }

                        actionGuard++; lastRequestedActionId = action.ActionId;
                        var required = 0;
                        var candidates = Array.Empty<long>();
                        if (action.Payload is not null)
                        {
                            if (action.Payload.TryGetValue("requiredCount", out var raw) && raw is not null)
                            {
                                required = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                            }

                            if (action.Payload.TryGetValue("candidateIds", out var ids) && ids is IEnumerable<long> enumerable)
                            {
                                candidates = enumerable.ToArray();
                            }
                        }

                        lastRequestedActionId = action.ActionId;

                        var selected = _matchPolicy.ChooseDiscards(state, actor, candidates, required);
                        var submission = Submit(actor, action, selected);
                        if (!submission.Accepted)
                        {
                            RecordReject(result, submission.ReasonKey, state, action);
                            result.Reason = "driver_discard_rejected:" + submission.ReasonKey;
                            goto done;
                        }
                        lastRejectReason = submission.ReasonKey;
                        handled = true;
                        break;
                    }

                    case TurnPhase.End:
                        flow.Advance(state, actor);
                        handled = true;
                        break;

                    default:
                        result.Reason = "driver_stall_unknown_phase_" + state.Turn.PhaseId;
                        goto done;
                }

                if (!handled)
                {
                    result.Reason = "driver_stall_phase_" + state.Turn.PhaseId;
                    break;
                }

                ScanPeakSizes(state, result);

                // Universal stall breaker. If an iteration produced no visible
                // progress (same turn, same phase, same action, no winner), the
                // driver is spinning on an action the engine will keep rejecting.
                // This is a driver defect. Stop and mark the match unfinished;
                // never bypass an outstanding hand-limit discard.
                // (Found by S1-30pct seed 3131774: a DISCARD-phase stall that
                // produced 199,985 rejected submissions before this guard.)
                var progressKey = state.Turn.Number + "|" + state.Turn.PhaseId + "|" + state.CurrentPlayerIndex + "|" + lastRequestedActionId;
                if (progressKey == _lastProgressKey)
                {
                    _sameProgressRepeats++;
                }
                else
                {
                    _lastProgressKey = progressKey;
                    _sameProgressRepeats = 0;
                }

                if (_sameProgressRepeats >= 25 && !state.WinnerPlayerIndex.HasValue)
                {
                    result.DriverGuardHits++;
                    if (result.DriverGuardReason.Length == 0)
                    {
                        result.DriverGuardReason =
                            $"no-progress stall: repeated '{lastRequestedActionId}' in {state.Turn.PhaseId} " +
                            $"turn={state.Turn.Number} actor={state.CurrentPlayerIndex} " +
                            $"reason={lastRejectReason}";
                    }

                    result.Reason = "driver_no_progress";
                    result.Capped = true;
                    goto done;
                }

                if (actionGuard > 200000)
                {
                    result.Reason = "driver_action_guard";
                    result.Capped = true;
                    break;
                }
            }

            done:
            result.UsageState = state;
            ScanDrawEvents(state, result);
            foreach (var item in state.Events.Items)
            {
                if (!string.Equals(item.EventType, "GAME_WON", StringComparison.Ordinal)) continue;
                var winner = ReadInt(item, "player");
                var reason = item.Data is not null && item.Data.TryGetValue("reasonKey", out var reasonRaw)
                    ? reasonRaw as string
                    : null;
                result.WinReasonByPlayer = winner + ":" + (reason ?? "win.unknown");
            }
            if (_punishRecorder is not null)
            {
                result.RecordedPunishDecides = _punishRecorder.DecideCalls;
                result.RecordedPunishAccepts = _punishRecorder.Accepted;
                result.RecordedMaxChainDepth = _punishRecorder.MaxChainDepthSeen;
                result.DeclinedByRoundCap = _punishRecorder.DeclinedByRoundCap;
                foreach (var pair in _punishRecorder.DepthHistogram)
                {
                    result.RecordedDepthHistogram[pair.Key] = pair.Value;
                }
            }

            result.Turns = state.Turn.Number;
            result.EndHand0 = state.GetPlayer(0).Hand.Count;
            result.EndHand1 = state.GetPlayer(1).Hand.Count;
            result.EndDeck0 = state.GetPlayer(0).Deck.Count;
            result.EndDeck1 = state.GetPlayer(1).Deck.Count;
            result.Decided = state.WinnerPlayerIndex.HasValue;
            if (result.Decided)
            {
                result.Reason = string.IsNullOrWhiteSpace(state.WinReason) ? "win.unknown" : state.WinReason!;
                result.WinnerIndex = state.WinnerPlayerIndex!.Value;
            }
            else if (state.Turn.Number >= _options.TurnCap)
            {
                result.Reason = "turn_cap_reached";
                result.Capped = true;
            }

            for (var index = 0; index < state.Players.Count; index++)
            {
                var player = state.Players[index];
                foreach (var card in player.Field)
                {
                    if (card.IsMinion && card.Health > result.MaxMinionHealth) result.MaxMinionHealth = card.Health;
                    if (card.Sealed && card.Health > result.MaxSealedHealth) result.MaxSealedHealth = card.Health;
                }

                if (player.PlayerIndex == 0 && _player0.Faction == "机械遗迹") result.MachinePullCount = player.PullCount;
                if (player.PlayerIndex == 1 && _player1.Faction == "机械遗迹") result.MachinePullCount = player.PullCount;
                if (player.PlayerIndex == 0 && _player0.Faction == "古木圣地")
                {
                    result.WoodRootStacks = player.RootStacks;
                    result.WoodRampantStacks = player.RampantStacks;
                }

                if (player.PlayerIndex == 1 && _player1.Faction == "古木圣地")
                {
                    result.WoodRootStacks = player.RootStacks;
                    result.WoodRampantStacks = player.RampantStacks;
                }
            }

            return result;
        }

        /// <summary>Behaviour counters for the acting player's faction.</summary>
        private void RecordBehavior(GameState state, GameResult result, int actor, LegalAction action)
        {
            var behavior = actor == 0 ? result.Behavior0 : result.Behavior1;
            switch (action.Type)
            {
                case LegalActionGenerator.PlayCard:
                {
                    behavior.Plays++;
                    var card = action.SourceId.HasValue ? state.FindEntity(action.SourceId.Value) : null;
                    if (card is null) break;
                    if (card.PunishActivated) behavior.PlaysPunishActivated++;
                    var effects = card.PunishActivated ? card.Definition.PunishEffects : card.Definition.OnPlayEffects;
                    foreach (var effect in effects)
                    {
                        if (effect is not null && string.Equals(effect.Action, EffectNames.Buff, StringComparison.Ordinal))
                        {
                            behavior.BuffCardsPlayed++;
                            break;
                        }
                    }

                    break;
                }

                case LegalActionGenerator.Attack:
                {
                    behavior.Attacks++;
                    if (action.SourceId.HasValue)
                    {
                        var attacker = state.FindEntity(action.SourceId.Value);
                        if (attacker is not null) behavior.AttackDamageDealt += Math.Max(0, attacker.Attack);
                    }

                    break;
                }

                case LegalActionGenerator.Commit: behavior.Commits++; break;
                case LegalActionGenerator.Pull: behavior.Pulls++; break;
                case TurnAction.SetAmbush: behavior.Ambushes++; break;
            }
        }

        /// <summary>Attack power currently standing on a player's board (sealed minions keep Attack 0).</summary>
        private static int BoardAttack(PlayerState player)
        {
            var total = 0;
            foreach (var card in player.Field)
            {
                if (card.IsMinion && card.IsAlive) total += Math.Max(0, card.Attack);
            }

            return total;
        }

        private static void ScanPeakSizes(GameState state, GameResult result)
        {
            foreach (var player in state.Players)
            {
                if (player.PlayerIndex == 0 && player.Hand.Count > result.MaxHand0) result.MaxHand0 = player.Hand.Count;
                if (player.PlayerIndex == 1 && player.Hand.Count > result.MaxHand1) result.MaxHand1 = player.Hand.Count;
                foreach (var card in player.Field)
                {
                    if (!card.IsMinion) continue;
                    if (card.Health > result.MaxMinionHealth) result.MaxMinionHealth = card.Health;
                    if (card.Sealed && card.Health > result.MaxSealedHealth) result.MaxSealedHealth = card.Health;
                }
            }
        }

        /// <summary>
        /// Punish-chain decomposition from the engine event log.
        ///
        /// Every punish chain hangs off one root action event (CARD_PLAYED,
        /// COMMIT_DECLARED or PULL_RESOLVED - PUSH in the end phase is silent
        /// when UploadCost is 0). Within one root, the FIRST PUNISH_DRAW is the
        /// initial punish draw bought by that play; every later PUNISH_DRAW
        /// under the same root is a response re-entry, because the only way a
        /// punish draw can happen again is an activated punish card resolving
        /// (src\Engine\Turns\PlayCardActionHandler.cs:300-345 -> :235) or a
        /// mechanical lifecycle punish (CommitActionHandler.cs:91, PullActionHandler).
        /// Classification therefore anchors on the root event id rather than on
        /// a PUNISH_TRIGGERED ancestor, which nested effect-runtime draws do not
        /// carry.
        /// </summary>
        private static void ScanDrawEvents(GameState state, GameResult result)
        {
            var events = state.Events.Items;
            var byId = new Dictionary<long, GameEvent>();
            foreach (var item in events) byId[item.EventId] = item;

            var currentTurn = 0;
            var roots = new HashSet<long>();
            var punishDrawSeenByRoot = new Dictionary<long, int>();
            var punishDrawsPerTurn = new Dictionary<int, int>();

            foreach (var item in events)
            {
                if (item.EventType == "TURN_CHANGED")
                {
                    currentTurn++;
                    continue;
                }

                if (item.EventType == "PUNISH_TRIGGERED")
                {
                    roots.Add(RootOf(byId, item) ?? 0);
                    var depth = ReadInt(item, "chainDepth");
                    if (depth > result.MaxPunishChainDepth) result.MaxPunishChainDepth = depth;
                    result.ChainDepthHistogram[depth] =
                        result.ChainDepthHistogram.TryGetValue(depth, out var seen) ? seen + 1 : 1;
                    var owner = ReadInt(item, "player");
                    if (owner == 0) result.PunishEventCount0++;
                    else result.PunishEventCount1++;
                    continue;
                }

                if (item.EventType == "CARDS_DISCARDED")
                {
                    // Inactive PUNISH cards expire from hand at the end phase
                    // (EffectRuntime.EndPhase.cs:99-126). Counting them shows
                    // whether an ablation changed the CHAIN or only the tempo.
                    var reason = item.Data is not null
                                 && item.Data.TryGetValue("reasonKey", out var reasonRaw)
                        ? reasonRaw as string
                        : null;
                    if (string.Equals(reason, "rule.inactive_punish_expired", StringComparison.Ordinal))
                    {
                        var owner = ReadInt(item, "player");
                        var expired = ReadInt(item, "count");
                        if (owner == 0) result.ExpiredPunish0 += expired;
                        else result.ExpiredPunish1 += expired;
                    }

                    continue;
                }

                if (item.EventType != "PUNISH_DRAW" && item.EventType != "CARDS_DRAWN") continue;
                var player = ReadInt(item, "player");
                var count = ReadInt(item, "count");
                if (item.EventType == "PUNISH_DRAW")
                {
                    if (player == 0) { result.PunishDrawEvents0++; result.PunishDrawCards0 += count; }
                    else { result.PunishDrawEvents1++; result.PunishDrawCards1 += count; }
                    var root = RootOf(byId, item) ?? 0;
                    punishDrawSeenByRoot.TryGetValue(root, out var seenSoFar);
                    punishDrawSeenByRoot[root] = seenSoFar + 1;
                    if (seenSoFar == 0)
                    {
                        if (player == 0) result.PunishDrawInitial0 += count;
                        else result.PunishDrawInitial1 += count;
                    }
                    else
                    {
                        if (player == 0) result.PunishDrawResponse0 += count;
                        else result.PunishDrawResponse1 += count;
                        result.ResponseDrawEvents++;
                    }

                    punishDrawsPerTurn[currentTurn] = punishDrawsPerTurn.TryGetValue(currentTurn, out var turnCount)
                        ? turnCount + count
                        : count;
                }
                else if (player == 0)
                {
                    result.DrawEvents0++;
                    result.DrawCards0 += count;
                }
                else
                {
                    result.DrawEvents1++;
                    result.DrawCards1 += count;
                }
            }

            foreach (var root in punishDrawSeenByRoot.Keys) roots.Add(root);
            result.PunishChains = roots.Count;
            foreach (var pair in punishDrawsPerTurn)
            {
                result.PunishDrawsPerTurnHistogram[pair.Key] = pair.Value;
            }
        }

        private static int ReadInt(GameEvent item, string key)
        {
            if (item.Data is null) return 0;
            if (!item.Data.TryGetValue(key, out var raw) || raw is null) return 0;
            return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        }

        private static bool HasAncestorOfType(
            Dictionary<long, GameEvent> byId,
            GameEvent item,
            string eventType)
        {
            var cursor = item.ParentEventId;
            var hops = 0;
            while (cursor.HasValue && hops++ < 64)
            {
                if (!byId.TryGetValue(cursor.Value, out var parent)) return false;
                if (string.Equals(parent.EventType, eventType, StringComparison.Ordinal)) return true;
                cursor = parent.ParentEventId;
            }

            return false;
        }

        private static long? RootOf(Dictionary<long, GameEvent> byId, GameEvent item)
        {
            var cursor = item;
            var hops = 0;
            while (cursor.ParentEventId.HasValue && hops++ < 64)
            {
                if (!byId.TryGetValue(cursor.ParentEventId.Value, out var parent)) return cursor.EventId;
                cursor = parent;
            }

            return cursor.EventId;
        }

        private int? PullSnapshot(GameState state, int turn)
        {
            if (state.Turn.Number < turn) return null;
            var player = MachinePlayer(state);
            return player?.PullCount;
        }

        private int? RootSnapshot(GameState state, int turn)
        {
            if (state.Turn.Number < turn) return null;
            var player = WoodPlayer(state);
            return player?.RootStacks;
        }

        private PlayerState? MachinePlayer(GameState state)
        {
            if (_player0.Faction == "机械遗迹") return state.GetPlayer(0);
            if (_player1.Faction == "机械遗迹") return state.GetPlayer(1);
            return null;
        }

        private PlayerState? WoodPlayer(GameState state)
        {
            if (_player0.Faction == "古木圣地") return state.GetPlayer(0);
            if (_player1.Faction == "古木圣地") return state.GetPlayer(1);
            return null;
        }

        private static void RecordReject(GameResult result, string reasonKey, GameState? state = null, LegalAction? action = null)
        {
            result.Counters.Rejections++;
            result.Counters.RejectReasons[reasonKey] =
                result.Counters.RejectReasons.TryGetValue(reasonKey, out var count) ? count + 1 : 1;
            if (state is not null && action is not null && result.Counters.RejectSamples.Count < 8)
            {
                var actor = state.CurrentPlayerIndex;
                var player = state.GetPlayer(actor);
                result.Counters.RejectSamples.Add(
                    $"{reasonKey} turn={state.Turn.Number} phase={state.Turn.PhaseId} actor={actor} " +
                    $"type={action.Type} card={action.CardId} source={action.SourceId} target={action.TargetId} " +
                    $"hand={player.Hand.Count} deck={player.Deck.Count} " +
                    $"required={PayloadInt(action, "discardRequired")} candidates={PayloadCount(action, "discardCandidateIds")}");
            }
        }

        private static int PayloadInt(LegalAction action, string key)
        {
            if (action.Payload is not null && action.Payload.TryGetValue(key, out var raw) && raw is not null)
            {
                return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            }

            return -1;
        }

        private static int PayloadCount(LegalAction action, string key)
        {
            if (action.Payload is not null
                && action.Payload.TryGetValue(key, out var raw)
                && raw is IEnumerable<long> enumerable)
            {
                return enumerable.Count();
            }

            return -1;
        }

        /// <summary>
        /// Submits an engine-advertised action through whichever boundary this
        /// match uses: the shipped adapter gateway (playstyle runs) or the raw
        /// controller (legacy harness runs). The action object is always the
        /// engine's own advertisement; nothing is synthesised.
        /// </summary>
        private (bool Accepted, string ReasonKey) Submit(int actor, LegalAction action, IReadOnlyList<long>? selected = null)
        {
            var state = _state!;
            var trace = new SubmissionTrace
            {
                phase = state.Turn.PhaseId, turn = state.Turn.Number, actor = actor,
                action_type = action.Type, action_id = action.ActionId,
                hand_before = state.GetPlayer(actor).Hand.Count,
                selected_count = selected?.Count ?? 0,
            };
            var eventCount = state.Events.Items.Count;
            if (action.Type == TurnAction.DiscardComplete)
            {
                if (action.Payload is null || !action.Payload.TryGetValue("requiredCount", out var required))
                    throw new InvalidOperationException("discard advertisement missing requiredCount");
                trace.required_count = Convert.ToInt32(required, CultureInfo.InvariantCulture);
                trace.expected_required = DiscardPhaseHandler.RequiredDiscardCount(state, state.GetPlayer(actor));
                // The required count encodes the actual cap when hand exceeds it.
                trace.effective_limit = trace.hand_before - trace.expected_required;
            }
            var result = SubmitCore(actor, action, selected);
            trace.accepted = result.Accepted;
            trace.reason = result.ReasonKey;
            trace.hand_after_resolution = state.GetPlayer(actor).Hand.Count;
            if (action.Type == TurnAction.DiscardComplete)
            {
                foreach (var e in state.Events.Items.Skip(eventCount))
                {
                    if (e.EventType == "CARDS_DISCARDED" && e.Data is not null
                        && e.Data.TryGetValue("reasonKey", out var reason) && Equals(reason, "rule.hand_limit")
                        && e.Data.TryGetValue("player", out var player) && Convert.ToInt32(player, CultureInfo.InvariantCulture) == actor
                        && e.Data.TryGetValue("count", out var count))
                        trace.discarded_event_count += Convert.ToInt32(count, CultureInfo.InvariantCulture);
                }
                trace.discard_protocol_valid = trace.phase == TurnPhase.Discard && result.Accepted
                    && trace.required_count == trace.expected_required && trace.selected_count == trace.required_count
                    && trace.discarded_event_count == trace.required_count
                    && trace.hand_before - trace.selected_count <= trace.effective_limit;
                _activeResult!.DiscardSubmissions++;
                _activeResult.DiscardRequiredCards += trace.required_count;
                if (result.Accepted) _activeResult.DiscardAccepted++;
                if (!trace.discard_protocol_valid) _activeResult.DiscardViolations++;
            }
            _observer?.Submission(trace);
            return result;
        }

        private void ObserveDecision(GameState state, IReadOnlyList<LegalAction> actions, LegalAction chosen)
        {
            _activeResult!.DecisionCount++;
            _activeResult.MaxDecisionHand = Math.Max(_activeResult.MaxDecisionHand, state.GetPlayer(state.CurrentPlayerIndex).Hand.Count);
            _activeResult.MaxDecisionOptions = Math.Max(_activeResult.MaxDecisionOptions, actions.Count);
            _observer?.Decision(state, actions, chosen);
        }

        private (bool Accepted, string ReasonKey) SubmitCore(int actor, LegalAction action, IReadOnlyList<long>? selected = null)
        {
            if (_gateway is null)
            {
                var direct = _controller!.Submit(selected is null
                    ? ToRequest(actor, action)
                    : new GameActionRequest(actor, action.Type, action.ActionId, action.SourceId, TargetReference(action), selected));
                return (direct.Accepted, direct.ReasonKey);
            }

            return SubmitThroughGateway(actor, action, selected);
        }

        /// <summary>
        /// Submits through the shipped adapter gateway. When the action came from
        /// the shipped playstyle policy, the advertisement itself is submitted
        /// (via AdvertisedActionPolicy.ToGameAction) at the revision it was
        /// advertised at, because the boundary compares field-for-field and
        /// rejects any rebuilt copy as action.advertisement_mismatch.
        /// </summary>
        private (bool Accepted, string ReasonKey) SubmitThroughGateway(
            int actor,
            LegalAction action,
            IReadOnlyList<long>? selected)
        {
            if (_matchPolicy is MatrixPolicy matrix
                && matrix.LastChosenAdvertisement is not null
                && string.Equals(matrix.LastChosenAdvertisement.ActionId, action.ActionId, StringComparison.Ordinal)
                && selected is null)
            {
                var verbatim = AdvertisedActionPolicy.ToGameAction(
                    matrix.LastChosenSnapshot!,
                    matrix.LastChosenAdvertisement);
                var verbatimResult = _gateway!.Submit(verbatim);
                return (verbatimResult.Result.Accepted, verbatimResult.Result.ReasonKey);
            }

            // HARNESS LIMITATION, stated rather than hidden: the adapter boundary
            // compares the submission with the advertisement field-for-field
            // (RuntimeContractV131ActionBoundary.cs:172-175), and the engine's
            // DISCARD advertisement carries only requiredCount/candidateIds - the
            // per-match selection a player must make is not part of it. A discard
            // selection submitted through the gateway is therefore always
            // rejected as action.advertisement_mismatch. The forced hand-limit
            // discard is not a playstyle decision (AdvertisedActionPolicy itself
            // forwards that advertisement and leaves resolution to the host), so
            // this one phase is submitted directly to the controller. Every ACTION
            // and AMBUSH decision still goes through the gateway.
            if (selected is not null)
            {
                var directDiscard = _controller!.Submit(new GameActionRequest(
                    actor,
                    action.Type,
                    action.ActionId,
                    action.SourceId,
                    TargetReference(action),
                    selected));
                return (directDiscard.Accepted, directDiscard.ReasonKey);
            }

            var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (action.Payload is not null)
            {
                foreach (var entry in action.Payload) payload[entry.Key] = entry.Value;
            }

            var wire = new AdapterRuntime.RuntimeGameAction
            {
                ContractVersion = AdapterRuntime.ContractVersionGuard.ExpectedVersion,
                MatchId = _matchId!,
                SnapshotRevision = _gateway!.SnapshotRevision,
                ActionId = action.ActionId,
                Type = action.Type,
                Actor = actor,
                SourceId = action.SourceId.HasValue ? EntityRef(action.SourceId.Value) : null,
                TargetId = TargetReference(action),
                CardId = action.CardId,
                Payload = payload,
            };
            var submission = _gateway.Submit(wire);
            return (submission.Result.Accepted, submission.Result.ReasonKey);
        }

        /// <summary>
        /// The engine request that reaches the router, built exactly as the
        /// legacy path always built it. Used only when no gateway is attached.
        /// </summary>
        private static GameActionRequest ToRequest(int actor, LegalAction action) => ToRequestCore(actor, action);

        private MatchController? _controller;

        private static string? TargetReference(LegalAction action)
        {
            switch (action.Type)
            {
                case LegalActionGenerator.PlayCard:
                case LegalActionGenerator.Attack:
                    return action.TargetId.HasValue ? EntityRef(action.TargetId.Value) : action.TargetReferenceId;
                case LegalActionGenerator.Pull:
                    return action.TargetId.HasValue ? EntityRef(action.TargetId.Value) : null;
                default:
                    return null;
            }
        }

        internal static GameActionRequest ToRequestCore(int actor, LegalAction action)
        {
            string? targetId = null;
            IReadOnlyList<long>? selected = null;
            switch (action.Type)
            {
                case LegalActionGenerator.PlayCard:
                    targetId = action.TargetId.HasValue
                        ? EntityRef(action.TargetId.Value)
                        : action.TargetReferenceId;
                    // Punish-to-self-discard cards advertise the required
                    // discard selection in the action payload; without it the
                    // engine correctly rejects with
                    // action.discard_selection_required.
                    if (action.Payload is not null
                        && action.Payload.TryGetValue("discardRequired", out var requiredRaw)
                        && requiredRaw is not null
                        && Convert.ToInt32(requiredRaw, CultureInfo.InvariantCulture) > 0
                        && action.Payload.TryGetValue("discardCandidateIds", out var candidatesRaw)
                        && candidatesRaw is IEnumerable<long> candidateEnumerable)
                    {
                        var required = Convert.ToInt32(requiredRaw, CultureInfo.InvariantCulture);
                        selected = candidateEnumerable.Take(required).ToArray();
                    }

                    break;
                case LegalActionGenerator.Pull:
                    targetId = action.TargetId.HasValue ? EntityRef(action.TargetId.Value) : null;
                    selected = action.Payload is not null
                               && action.Payload.TryGetValue("selectedEntityIds", out var ids)
                               && ids is IEnumerable<long> enumerable
                        ? enumerable.ToArray()
                        : Array.Empty<long>();
                    break;
                case LegalActionGenerator.Attack:
                    targetId = action.TargetId.HasValue ? EntityRef(action.TargetId.Value) : action.TargetReferenceId;
                    break;
                case TurnAction.SetAmbush:
                    // Ambush setting converts the punish cost into a self-discard
                    // while the sea leader's conversion effect is active, and the
                    // engine advertises the required selection in the payload.
                    if (action.Payload is not null
                        && action.Payload.TryGetValue("discardRequired", out var ambushRequiredRaw)
                        && ambushRequiredRaw is not null
                        && Convert.ToInt32(ambushRequiredRaw, CultureInfo.InvariantCulture) > 0
                        && action.Payload.TryGetValue("discardCandidateIds", out var ambushCandidatesRaw)
                        && ambushCandidatesRaw is IEnumerable<long> ambushCandidates)
                    {
                        var ambushRequired = Convert.ToInt32(ambushRequiredRaw, CultureInfo.InvariantCulture);
                        selected = ambushCandidates.Take(ambushRequired).ToArray();
                    }

                    break;
            }

            return new GameActionRequest(actor, action.Type, action.ActionId, action.SourceId, targetId, selected);
        }

        internal static string EntityRef(long instanceId) =>
            "entity_" + instanceId.ToString("D12", CultureInfo.InvariantCulture);
    }

    internal static class Unused_GameResultExtensions
    {
    }

    internal sealed class AcceptAllPolicy : IPunishResponsePolicy
    {
        public PunishResponseDecision Decide(GameState state, CardInstance card, int effectivePunish, int chainDepth)
            => PunishResponseDecision.Accept();
    }

    internal sealed class DeclineAllPolicy : IPunishResponsePolicy
    {
        public PunishResponseDecision Decide(GameState state, CardInstance card, int effectivePunish, int chainDepth)
            => PunishResponseDecision.Decline();
    }
}
