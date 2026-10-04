using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PlCsim
{
    /// <summary>
    /// Pins the (Engine, Adapters) revision pair a run measured, and refuses to
    /// let a report be produced from a pair whose provenance is unknown.
    ///
    /// Why this exists: <c>DominionWars.Adapters.dll</c> is a separate assembly
    /// from <c>DominionWars.Engine.dll</c>, staged next to the harness by hand.
    /// Nothing in the toolchain tied the two together, so a harness could load
    /// the current Adapters together with a frozen pre-P0 Engine and stamp only
    /// "adapters sha256 ..." with no way to tell which revision that was or
    /// whether the pair was ever meant to sit together. One recorded matrix run
    /// did exactly that: it measured an Adapters revision that had already been
    /// superseded by a newer build under <c>build-output\DominionWars.Adapters</c>.
    ///
    /// The contract here mirrors <see cref="MatchRulesFactory"/>: if the
    /// requested pair cannot be verified, throw <see cref="NotSupportedException"/>
    /// and fail the run rather than silently measuring something unattributable.
    ///
    /// Two drift checks are applied, and they are deliberately different:
    ///   * <b>pinned-pair drift</b> — the DLLs beside the exe must hash to the
    ///     values recorded in <c>PINNED_REVISION.json</c>. Any other hash is a
    ///     hard error, because the numbers would not be attributable to the
    ///     revision the sidecar describes.
    ///   * <b>source-output drift</b> — the Adapters currently built under
    ///     <c>build-output\DominionWars.Adapters</c> must hash to the same value
    ///     as the pinned copy. If a newer build exists, that is reported as a
    ///     warning naming both hashes, because it means the source has moved on
    ///     and the recorded numbers describe the pinned revision only.
    ///
    /// This file lives in the harness only. Nothing under src\ is read or written.
    /// </summary>
    internal static class ProvenanceGuard
    {
        public const string SidecarName = "PINNED_REVISION.json";

        /// <summary>Files the sidecar pins, in report order.</summary>
        private static readonly string[] PinnedFiles =
        {
            "DominionWars.Engine.dll",
            "DominionWars.Adapters.dll",
            "DominionWars.Data.dll",
        };

        public static string AdaptersSourcePath { get; private set; } = "unavailable";
        public static string AdaptersSourceSha256 { get; private set; } = "unavailable";
        public static string SidecarPath { get; private set; } = "absent";
        public static bool PinnedPairVerified { get; private set; }
        public static string DriftNotes { get; private set; } = string.Empty;

        private static readonly List<string> Notes = new List<string>();

        /// <summary>
        /// Checks the pair against the sidecar WITHOUT throwing, so a caller can
        /// report the truth even when it is not enforcing the contract. Returns
        /// null when the pair matches the pin, otherwise the reason it does not.
        ///
        /// This exists because a bypass must never be able to launder an
        /// unverified pair into a report: if the hashes happen to match the pin,
        /// the run IS verified and must say so, rather than reporting
        /// <c>false</c> merely because enforcement was switched off.
        /// </summary>
        public static string? CheckWithoutThrowing(string? sidecarDirectory = null)
        {
            try
            {
                Verify(sidecarDirectory);
                return null;
            }
            catch (NotSupportedException exception)
            {
                return exception.Message;
            }
        }

        /// <summary>
        /// Verifies the pair beside the exe against the sidecar. Throws when the
        /// pair cannot be attributed; returns normally when it can.
        /// </summary>
        /// <param name="sidecarDirectory">
        /// Directory holding <see cref="SidecarName"/>. Defaults to the staged
        /// flavour directory the harness was linked against.
        /// </param>
        public static void Verify(string? sidecarDirectory = null)
        {
            Notes.Clear();
            var baseDir = AppContext.BaseDirectory;
            var dir = sidecarDirectory ?? FindStagedFlavourDirectory();

            // An Adapters assembly that is simply not there is the loudest
            // failure: the harness cannot run the playstyle seam at all, and a
            // report claiming playstyle provenance would be fabricated.
            var adaptersBesideExe = Path.Combine(baseDir, "DominionWars.Adapters.dll");
            if (!File.Exists(adaptersBesideExe))
            {
                throw new NotSupportedException(
                    "DominionWars.Adapters.dll is not present beside the harness at " + baseDir + ". " +
                    "The playstyle seam and the per-side punish-response stance cannot be exercised or " +
                    "attributed, so no report may be produced from this build.");
            }

            SidecarPath = Path.Combine(dir, SidecarName);
            if (!File.Exists(SidecarPath))
            {
                throw new NotSupportedException(
                    "No pinned-revision sidecar at " + SidecarPath + ", so the (Engine, Adapters) pair " +
                    "beside the harness cannot be attributed to a revision. Stage a sidecar before measuring; " +
                    "an unstamped pair must not produce a report.");
            }

            var pinned = ReadSidecar(SidecarPath);
            foreach (var file in PinnedFiles)
            {
                var actual = HashOf(Path.Combine(baseDir, file));
                if (!pinned.TryGetValue(file, out var expected))
                {
                    throw new NotSupportedException(
                        "The pinned-revision sidecar " + SidecarName + " does not pin " + file +
                        ", so the pair is only partially attributed.");
                }

                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException(
                        "Revision drift: " + file + " beside the harness hashes to " + Short(actual) +
                        " but the pinned pair requires " + Short(expected) + ". The numbered results would " +
                        "describe neither revision. Re-stage the pinned pair, or pin the new pair deliberately " +
                        "and say which one the numbers belong to.");
                }
            }

            PinnedPairVerified = true;

            // Which Adapters build produced the pinned copy, and has source moved on?
            var sourceBuild = Path.Combine(FindRepositoryRoot(), "build-output", "DominionWars.Adapters",
                "bin", "Release", "netstandard2.1", "DominionWars.Adapters.dll");
            if (File.Exists(sourceBuild))
            {
                AdaptersSourcePath = sourceBuild;
                AdaptersSourceSha256 = HashOf(sourceBuild);
                if (!string.Equals(AdaptersSourceSha256, pinned["DominionWars.Adapters.dll"], StringComparison.OrdinalIgnoreCase))
                {
                    Notes.Add("Adapters source-output drift: the pinned Adapters is " +
                              Short(pinned["DominionWars.Adapters.dll"]) + " but the current build under " +
                              "build-output\\DominionWars.Adapters is " + Short(AdaptersSourceSha256) +
                              ". The recorded numbers describe the PINNED revision only.");
                }
            }
            else
            {
                Notes.Add("Adapters source build not found at " + sourceBuild +
                          "; the pinned revision is described by its hash only.");
            }

            DriftNotes = Notes.Count == 0 ? "none" : string.Join(" | ", Notes);
        }

        /// <summary>
        /// The staged flavour directory the harness was linked against, derived
        /// from the sidecar sitting next to the harness's own references. Falls
        /// back to the build-output Adapters tree when no staged copy is found.
        /// </summary>
        private static string FindStagedFlavourDirectory()
        {
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, SidecarName))) return AppContext.BaseDirectory;
            var root = FindRepositoryRoot();
            var candidate = Path.Combine(root, "build-output", "pl-csim", "engine-matrix");
            return Directory.Exists(candidate) ? candidate : AppContext.BaseDirectory;
        }

        internal static string FindRepositoryRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "data", "cards")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "src")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return Directory.GetCurrentDirectory();
        }

        private static Dictionary<string, string> ReadSidecar(string path)
        {
            // Deliberately hand-parsed: the harness must not take a JSON
            // dependency just to read four hashes, and a malformed sidecar has
            // to fail loudly rather than degrade to "unpinned".
            var text = File.ReadAllText(path);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var i = 0;
            while (true)
            {
                var fileAt = text.IndexOf("\"file\"", i, StringComparison.Ordinal);
                if (fileAt < 0) break;
                var file = ReadJsonString(text, fileAt);
                var shaAt = text.IndexOf("\"sha256\"", fileAt, StringComparison.Ordinal);
                if (file is null || shaAt < 0)
                {
                    throw new NotSupportedException("Malformed pinned-revision sidecar at " + path + ".");
                }

                var sha = ReadJsonString(text, shaAt);
                if (sha is null)
                {
                    throw new NotSupportedException("Malformed pinned-revision sidecar at " + path + ".");
                }

                result[file] = sha;
                i = shaAt + 8;
            }

            if (result.Count == 0)
            {
                throw new NotSupportedException("Pinned-revision sidecar at " + path + " pins no files.");
            }

            return result;
        }

        private static string? ReadJsonString(string text, int keyAt)
        {
            var colon = text.IndexOf(':', keyAt);
            if (colon < 0) return null;
            var open = text.IndexOf('"', colon + 1);
            if (open < 0) return null;
            var close = text.IndexOf('"', open + 1);
            return close < 0 ? null : text.Substring(open + 1, close - open - 1);
        }

        internal static string HashOf(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static string Short(string sha) => sha.Length >= 12 ? sha.Substring(0, 12) : sha;

        /// <summary>
        /// Human-readable provenance lines for console output and reports. Reads
        /// the hashes live from the DLLs beside the harness, so it stays correct
        /// even when called before <see cref="EngineStamp"/> has been populated.
        /// </summary>
        public static string Describe()
        {
            var builder = new StringBuilder();
            foreach (var file in PinnedFiles)
            {
                var path = Path.Combine(AppContext.BaseDirectory, file);
                var sha = File.Exists(path) ? Short(HashOf(path)) : "MISSING";
                builder.Append("  ").Append(file.PadRight(26)).Append(' ').Append(sha).Append('\n');
            }

            builder.Append("  pinned pair verified      ").Append(PinnedPairVerified ? "yes" : "NO").Append('\n');
            builder.Append("  pinned sidecar            ").Append(SidecarPath).Append('\n');
            builder.Append("  adapters source build     ").Append(AdaptersSourcePath)
                .Append(" (").Append(Short(AdaptersSourceSha256)).Append(")\n");
            return builder.ToString();
        }
    }
}
