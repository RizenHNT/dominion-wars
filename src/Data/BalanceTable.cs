using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DominionWars.Engine.Rules;

namespace DominionWars.Data
{
    /// <summary>
    /// Outcome of one <c>data/balance.json</c> read.
    /// </summary>
    public enum BalanceStatus
    {
        /// <summary>The file existed, parsed, and every rule key was applied.</summary>
        Loaded = 0,

        /// <summary>The file did not exist. The loader fallback uses T1=1 and built-in defaults for other MatchRules fields.</summary>
        MissingFile = 1,

        /// <summary>The file existed but could not be read, parsed, or validated. The fallback may differ from disk.</summary>
        Corrupt = 2,
    }

    /// <summary>
    /// One balance-table read. <see cref="Rules"/> is never null: a failed read
    /// falls back to the loader's configured fallback (T1=1; other MatchRules
    /// fields use their constructor defaults). Direct <c>new MatchRules()</c>
    /// remains T1=0.
    /// </summary>
    public sealed class BalanceLoad
    {
        internal BalanceLoad(string path, BalanceStatus status, MatchRules rules, string message, Exception? exception)
        {
            Path = path;
            Status = status;
            Rules = rules;
            Message = message;
            Exception = exception;
        }

        /// <summary>The path that was read, or the empty string when no path was requested.</summary>
        public string Path { get; }

        public BalanceStatus Status { get; }

        /// <summary>The loaded rules, or the BalanceTable loader fallback when <see cref="Status"/> is not <see cref="BalanceStatus.Loaded"/>.</summary>
        public MatchRules Rules { get; }

        /// <summary>Human-readable diagnosis. Empty when <see cref="Status"/> is <see cref="BalanceStatus.Loaded"/>.</summary>
        public string Message { get; }

        /// <summary>The underlying failure, when the read threw.</summary>
        public Exception? Exception { get; }

        public bool IsLoaded => Status == BalanceStatus.Loaded;

        /// <summary>
        /// True when <see cref="Rules"/> is only a fallback. T1=1 matches the
        /// published baseline; other fields use engine defaults, which are not
        /// guaranteed to match every field in a missing or unreadable file.
        /// </summary>
        public bool UsedFallback => Status != BalanceStatus.Loaded;

        /// <summary>
        /// The <c>data/balance.json</c> key that feeds each <see cref="MatchRules"/>
        /// field. Any key outside this map is documented but not read by the .NET
        /// engine, and any unread key is reported through
        /// <see cref="BalanceLoad.Message"/> instead of being applied.
        /// </summary>
        public static IReadOnlyDictionary<string, string> RuleKeyByField { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "handLimit", "handLimit" },
                { "pioneerHandLimitBonus", "pioneerHandLimitBonus" },
                { "pioneerOpponentPunishBonus", "pioneerOpponentPunishBonus" },
                { "pioneerSelfPunishDiscount", "pioneerSelfPunishDiscount" },
                { "maxPunishResponsesPerRound", "maxPunishResponsesPerRound" },
            };
    }

    /// <summary>
    /// Loader for the documented balance source of truth, <c>data/balance.json</c>.
    /// <para>
    /// This type owns no rules: it only translates the documented JSON keys into
    /// the engine's <see cref="MatchRules"/> value object, which stays the single
    /// authority for what a rules value means. Field ranges here are a guard
    /// against a typo silently changing the game, so every bound is either the
    /// built-in default or wider than it.
    /// </para>
    /// <para>
    /// A read never throws for missing or malformed input. Missing/invalid paths
    /// and malformed files are reported distinctly and loudly. The loader
    /// fallback sets maxPunishResponsesPerRound to the shipped T1 value (1) and
    /// leaves the other MatchRules fields at constructor defaults; direct
    /// <c>new MatchRules()</c> still defaults that field to 0. A corrupt file
    /// cannot be confirmed against disk, and the message says so.
    /// </para>
    /// </summary>
    public static class BalanceTable
    {
        /// <summary>File name inside the data root.</summary>
        public const string FileName = "balance.json";

        /// <summary>Repository-relative location of the balance table.</summary>
        public static readonly string RelativePath = "data" + Path.DirectorySeparatorChar + FileName;

        private static readonly char[] InvalidPathChars =
        {
            '"', '<', '>', '|', '\0', '\u0001', '\u0002', '\u0003', '\u0004', '\u0005',
            '\u0006', '\u0007', '\b', '\t', '\n', '\v', '\f', '\r', '\u0010', '\u0011',
            '\u0012', '\u0013', '\u0014', '\u0015', '\u0016', '\u0017', '\u0018',
            '\u0019', '\u001a', '\u001b', '\u001c', '\u001d', '\u001e', '\u001f',
        };

        private static readonly object ShippedLock = new object();
        private static BalanceLoad? _shipped;

        /// <summary>
        /// Reads the balance table from an explicit file path. Never throws for
        /// missing or malformed input; inspect <see cref="BalanceLoad.Status"/>.
        /// </summary>
        /// <param name="file">Full or relative path to the balance JSON file.</param>
        /// <param name="warning">
        /// Sink for unknown keys. Null means no unknown-key report; the
        /// missing/corrupt diagnostics always go to <paramref name="diagnostic"/>.
        /// </param>
        /// <param name="diagnostic">Sink for the loud missing/corrupt report. Null means stderr.</param>
        public static BalanceLoad Load(
            string file,
            Action<string>? warning = null,
            Action<string>? diagnostic = null)
        {
            if (file is null) throw new ArgumentNullException(nameof(file));
            var sink = diagnostic ?? DefaultDiagnostic;
            if (string.IsNullOrWhiteSpace(file) || file.IndexOfAny(InvalidPathChars) >= 0)
            {
                var invalid = "invalid balance table path '" + file + "'";
                sink("[Balance] " + invalid + "：使用配置加载器回退值（maxPunishResponsesPerRound=1，与已发布基准一致；其他字段沿用引擎默认值，不保证与磁盘全部字段一致）。");
                return new BalanceLoad(file, BalanceStatus.MissingFile, CreateLoaderFallbackRules(), invalid, null);
            }

            try
            {
                if (!File.Exists(file))
                {
                    var missing = "未找到平衡表 " + file + "，使用配置加载器回退值（maxPunishResponsesPerRound=1，与已发布基准一致；其他字段沿用引擎默认值，不保证与磁盘全部字段一致）。";
                    sink("[Balance] " + missing);
                    return new BalanceLoad(file, BalanceStatus.MissingFile, CreateLoaderFallbackRules(), missing, null);
                }

                var text = ReadText(file);
                var root = JToken.Parse(text);
                if (root.Type != JTokenType.Object)
                {
                    throw new InvalidDataException("root must be a JSON object");
                }

                var notes = new List<string>();
                var rules = MapRules(root, file, warning, notes);
                return new BalanceLoad(file, BalanceStatus.Loaded, rules, string.Join("; ", notes), null);
            }
            catch (Exception exception) when (
                exception is JsonException
                || exception is IOException
                || exception is InvalidDataException
                || exception is UnauthorizedAccessException
                || exception is NotSupportedException
                || exception is FormatException
                || exception is OverflowException
                || exception is ArgumentException)
            {
                var corrupt = "平衡表 " + file + " 读取/解析失败：" + exception.Message;
                sink("[Balance] " + corrupt);
                sink("[Balance] 已回退到配置加载器回退值（maxPunishResponsesPerRound=1，与已发布基准一致；其他字段沿用引擎默认值；无法确认与磁盘全部字段一致，可能改变本局规则）。");
                return new BalanceLoad(file, BalanceStatus.Corrupt, CreateLoaderFallbackRules(), corrupt, exception);
            }
        }

        /// <summary>
        /// Reads the shipped balance table from <paramref name="repositoryRoot"/>
        /// when given, otherwise by searching upward from the running assembly
        /// and the current directory for <c>data/balance.json</c>.
        /// </summary>
        public static BalanceLoad LoadShipped(
            string? repositoryRoot = null,
            Action<string>? warning = null,
            Action<string>? diagnostic = null)
        {
            var path = repositoryRoot is null
                ? TryResolveShippedPath()
                : Path.Combine(repositoryRoot, RelativePath);
            if (path is null)
            {
                var sink = diagnostic ?? DefaultDiagnostic;
                var unresolved = "未能在运行目录之上找到 " + RelativePath + "，使用配置加载器回退值（maxPunishResponsesPerRound=1，与已发布基准一致；其他字段沿用引擎默认值，不保证与磁盘全部字段一致）。";
                sink("[Balance] " + unresolved);
                return new BalanceLoad(string.Empty, BalanceStatus.MissingFile, CreateLoaderFallbackRules(), unresolved, null);
            }

            return Load(path, warning, diagnostic);
        }

        /// <summary>
        /// Convenience wrapper returning the rules alone. A failed read yields
        /// the BalanceTable loader fallback (T1=1; other fields use constructor
        /// defaults), never an exception.
        /// </summary>
        public static MatchRules LoadRules(
            string? file = null,
            Action<string>? warning = null,
            Action<string>? diagnostic = null)
        {
            return file is null
                ? LoadShipped(null, warning, diagnostic).Rules
                : Load(file, warning, diagnostic).Rules;
        }

        /// <summary>
        /// Process-wide cached read of the shipped table. Production entry points
        /// start many matches against one immutable file; pass the returned
        /// instance to <c>MatchFactory</c> to avoid re-reading per match.
        /// </summary>
        public static BalanceLoad Shipped(
            Action<string>? diagnostic = null)
        {
            lock (ShippedLock)
            {
                if (_shipped is null)
                {
                    _shipped = LoadShipped(null, null, diagnostic);
                }
                else if (diagnostic is not null && _shipped.UsedFallback)
                {
                    diagnostic("[Balance] " + _shipped.Message);
                }

                return _shipped;
            }
        }

        /// <summary>
        /// Locates <c>data/balance.json</c> by walking up from the running
        /// assembly directory and the current directory. Returns null when no
        /// candidate exists.
        /// </summary>
        public static string? TryResolveShippedPath()
        {
            var fromAssembly = SearchUpwards(AppContext.BaseDirectory);
            if (fromAssembly is not null)
            {
                return fromAssembly;
            }

            return SearchUpwards(Directory.GetCurrentDirectory());
        }

        /// <summary>
        /// Filesystem-safe entry point for tests and callers that need to
        /// classify a candidate path without ever throwing. Thin wrapper over
        /// <see cref="Load"/> that documents the contract.
        /// </summary>
        public static BalanceLoad LoadOrFallback(string file, Action<string>? diagnostic = null)
        {
            return Load(file, null, diagnostic);
        }

        private static MatchRules CreateLoaderFallbackRules()
        {
            return new MatchRules(maxPunishResponsesPerRound: 1);
        }

        private static MatchRules MapRules(
            JToken root,
            string source,
            Action<string>? warning,
            List<string> notes)
        {
            var handLimit = ReadInt(root, "handLimit", 8, 0, 99, source, notes);
            var pioneerHandLimitBonus = ReadInt(root, "pioneerHandLimitBonus", 2, 0, 99, source, notes);
            var pioneerOpponentPunishBonus = ReadInt(root, "pioneerOpponentPunishBonus", 1, 0, 99, source, notes);
            var pioneerSelfPunishDiscount = ReadInt(root, "pioneerSelfPunishDiscount", 0, 0, 99, source, notes);
            var maxPunishResponsesPerRound = ReadInt(
                root,
                "maxPunishResponsesPerRound",
                1,
                0,
                99,
                source,
                notes,
                "BalanceTable loader fallback");

            var unread = new List<string>();
            var unknown = new List<string>();
            foreach (var property in root.Children<JProperty>())
            {
                if (BalanceLoad.RuleKeyByField.ContainsKey(property.Name))
                {
                    continue;
                }

                unread.Add(property.Name);
                unknown.Add(property.Name);
            }

            if (unread.Count > 0)
            {
                unread.Sort(StringComparer.Ordinal);
                notes.Add(
                    "ignored " + unread.Count + " key(s) with no MatchRules counterpart in the .NET engine: "
                    + string.Join(", ", unread));
            }

            if (warning is not null)
            {
                foreach (var name in unknown)
                {
                    warning(source + ": " + name + " has no MatchRules counterpart and was not read");
                }
            }

            return new MatchRules(
                handLimit,
                pioneerHandLimitBonus,
                pioneerOpponentPunishBonus,
                pioneerSelfPunishDiscount,
                maxPunishResponsesPerRound);
        }

        private static int ReadInt(
            JToken root,
            string property,
            int fallback,
            int min,
            int max,
            string source,
            List<string> notes,
            string fallbackDescription = "built-in default")
        {
            if (root[property] is not JToken value)
            {
                notes.Add(property + " absent, using " + fallbackDescription + " " + fallback);
                return fallback;
            }

            if (!TryReadInt64(value, out var number))
            {
                throw new InvalidDataException(source + ": " + property + " must be a whole number");
            }

            if (number < min || number > max)
            {
                throw new InvalidDataException(
                    source + ": " + property + " is outside its allowed range " + min + ".." + max + " (read " + number + ")");
            }

            return (int)number;
        }

        private static bool TryReadInt64(JToken value, out long number)
        {
            number = 0;
            if (value.Type == JTokenType.Integer)
            {
                try
                {
                    number = value.Value<long>();
                    return true;
                }
                catch (Exception exception) when (exception is FormatException || exception is OverflowException || exception is InvalidCastException)
                {
                    return false;
                }
            }

            if (value.Type != JTokenType.Float)
            {
                return false;
            }

            try
            {
                var asDouble = value.Value<double>();
                if (double.IsNaN(asDouble) || double.IsInfinity(asDouble) || asDouble != Math.Floor(asDouble))
                {
                    return false;
                }

                number = (long)asDouble;
                return true;
            }
            catch (Exception exception) when (exception is FormatException || exception is OverflowException || exception is InvalidCastException)
            {
                return false;
            }
        }

        private static string ReadText(string file)
        {
            using (var stream = File.OpenRead(file))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return reader.ReadToEnd().TrimStart('\uFEFF', '\u200B');
            }
        }

        private static string? SearchUpwards(string? start)
        {
            if (string.IsNullOrEmpty(start))
            {
                return null;
            }

            DirectoryInfo? directory;
            try
            {
                directory = new DirectoryInfo(start);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is NotSupportedException)
            {
                return null;
            }

            for (var depth = 0; directory is not null && depth < 12; depth++, directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, RelativePath);
                try
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException)
                {
                    // An unreadable intermediate directory is not a fatal
                    // condition: keep walking and let the caller fall back.
                }
            }

            return null;
        }

        private static void DefaultDiagnostic(string message)
        {
            Console.Error.WriteLine(message);
        }
    }
}
