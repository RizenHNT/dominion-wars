#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DominionWars.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace DominionWars.Unity.EditorTools
{
    /// <summary>Severity used by the deterministic content validation report.</summary>
    public enum ContentDiagnosticSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2,
        Blocking = 3
    }

    /// <summary>A single stable, machine-readable content validation diagnostic.</summary>
    public sealed class ContentDiagnostic
    {
        public ContentDiagnostic(
            ContentDiagnosticSeverity severity,
            string code,
            string message,
            string path = "")
        {
            Severity = severity;
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Path = path ?? string.Empty;
        }

        public ContentDiagnosticSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public string Path { get; }
    }

    /// <summary>
    /// Immutable validation result. The order of diagnostics and referenced files
    /// is stable so the report is suitable for CI and source comparison.
    /// </summary>
    public sealed class ContentValidationReport
    {
        public ContentValidationReport(
            string contentRoot,
            string manifestPath,
            IEnumerable<ContentDiagnostic> diagnostics,
            IEnumerable<string> referencedFiles)
        {
            ContentRoot = contentRoot ?? throw new ArgumentNullException(nameof(contentRoot));
            ManifestPath = manifestPath ?? throw new ArgumentNullException(nameof(manifestPath));
            Diagnostics = diagnostics
                .OrderBy(diagnostic => SeverityOrder(diagnostic.Severity))
                .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
                .ToArray();
            ReferencedFiles = referencedFiles
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        public string ContentRoot { get; }
        public string ManifestPath { get; }
        public IReadOnlyList<ContentDiagnostic> Diagnostics { get; }
        public IReadOnlyList<string> ReferencedFiles { get; }
        public int InfoCount => Count(ContentDiagnosticSeverity.Info);
        public int WarningCount => Count(ContentDiagnosticSeverity.Warning);
        public int ErrorCount => Count(ContentDiagnosticSeverity.Error);
        public int BlockingCount => Count(ContentDiagnosticSeverity.Blocking);
        public bool CanBuild => BlockingCount == 0;

        private int Count(ContentDiagnosticSeverity severity)
        {
            return Diagnostics.Count(diagnostic => diagnostic.Severity == severity);
        }

        private static int SeverityOrder(ContentDiagnosticSeverity severity)
        {
            // Blocking diagnostics are reported first because they are the build gate.
            return 3 - (int)severity;
        }
    }

    /// <summary>
    /// Content manifest scanner. It is Editor-only and deliberately does not write
    /// authored data. Runtime uses the Data-layer ContentCatalog for the same
    /// fail-closed semantic contract.
    /// </summary>
    public static class ContentPipelineValidator
    {
        public const string ManifestRelativePath = "manifests/content.manifest.json";
        public const string SkinDirectoryRelativePath = "manifests/skins";
        public const string AuthoredRelativePath = "authored";
        public const string InboxRelativePath = "inbox";

        private static readonly HashSet<string> SourceTypes =
            new HashSet<string>(new[] { "file", "programmatic" }, StringComparer.Ordinal);
        private static readonly HashSet<string> Statuses =
            new HashSet<string>(new[] { "draft", "ready", "approved", "deprecated" }, StringComparer.Ordinal);

        public static ContentValidationReport Validate(string contentRoot, bool production = true)
        {
            if (contentRoot is null) throw new ArgumentNullException(nameof(contentRoot));
            var fullRoot = Path.GetFullPath(contentRoot);
            return Validate(fullRoot, Path.Combine(fullRoot, ManifestRelativePath), production);
        }

        public static ContentValidationReport Validate(
            string contentRoot,
            string manifestPath,
            bool production = true)
        {
            if (contentRoot is null) throw new ArgumentNullException(nameof(contentRoot));
            if (manifestPath is null) throw new ArgumentNullException(nameof(manifestPath));

            var fullRoot = Path.GetFullPath(contentRoot);
            var fullManifest = Path.GetFullPath(manifestPath);
            var diagnostics = new List<ContentDiagnostic>();
            var referencedFiles = new List<string>();
            JObject? root = null;

            if (!Directory.Exists(fullRoot))
            {
                diagnostics.Add(Block("content.root.missing", "Content root does not exist.", fullRoot));
                return new ContentValidationReport(fullRoot, fullManifest, diagnostics, referencedFiles);
            }

            if (!IsWithinRoot(fullManifest, fullRoot))
            {
                diagnostics.Add(Block("manifest.path.unsafe", "Manifest path is outside the content root.", fullManifest));
                return new ContentValidationReport(fullRoot, fullManifest, diagnostics, referencedFiles);
            }

            if (!File.Exists(fullManifest))
            {
                diagnostics.Add(Block("manifest.missing", "Content manifest is missing.", ToRelative(fullManifest, fullRoot)));
                AddOrphanDiagnostics(fullRoot, referencedFiles, diagnostics);
                return new ContentValidationReport(fullRoot, fullManifest, diagnostics, referencedFiles);
            }

            try
            {
                var settings = new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                };
                var token = JToken.Parse(File.ReadAllText(fullManifest, Encoding.UTF8), settings);
                if (token.Type != JTokenType.Object)
                {
                    diagnostics.Add(Block("manifest.root.invalid", "Manifest root must be an object.", ManifestRelativePath));
                }
                else
                {
                    root = (JObject)token;
                    ScanManifestShape(root, fullRoot, production, referencedFiles, diagnostics);
                }
            }
            catch (JsonException exception)
            {
                diagnostics.Add(Block("manifest.json.invalid", "Manifest JSON is invalid: " + exception.Message, ManifestRelativePath));
            }
            catch (IOException exception)
            {
                diagnostics.Add(Block("manifest.read.failed", "Manifest could not be read: " + exception.Message, ManifestRelativePath));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(Block("manifest.read.denied", "Manifest could not be read: " + exception.Message, ManifestRelativePath));
            }

            if (root is not null)
            {
                var catalog = AddCatalogValidation(fullRoot, fullManifest, production, diagnostics);
                if (catalog is not null)
                    AddSkinValidation(fullRoot, catalog, production, referencedFiles, diagnostics);
                AddUnusedDiagnostics(root, fullRoot, diagnostics);
            }

            AddOrphanDiagnostics(fullRoot, referencedFiles, diagnostics);
            AddInboxDiagnostics(fullRoot, diagnostics);
            if (diagnostics.Count == 0)
            {
                diagnostics.Add(new ContentDiagnostic(
                    ContentDiagnosticSeverity.Info,
                    "content.valid",
                    "Content manifest and referenced authored assets are valid.",
                    ManifestRelativePath));
            }

            return new ContentValidationReport(fullRoot, fullManifest, diagnostics, referencedFiles);
        }

        private static void ScanManifestShape(
            JObject root,
            string contentRoot,
            bool production,
            ICollection<string> referencedFiles,
            ICollection<ContentDiagnostic> diagnostics)
        {
            var assetsToken = root["assets"];
            if (assetsToken is null || assetsToken.Type != JTokenType.Array)
            {
                diagnostics.Add(Block("manifest.assets.invalid", "Manifest assets must be an array.", ManifestRelativePath));
                return;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assetIndex = 0;
            foreach (var item in assetsToken.Children())
            {
                var sourcePath = ManifestRelativePath + ":assets[" + assetIndex + "]";
                if (item.Type != JTokenType.Object)
                {
                    diagnostics.Add(Block("manifest.asset.invalid", "Asset entry must be an object.", sourcePath));
                    assetIndex++;
                    continue;
                }

                var asset = (JObject)item;
                var id = RequiredString(asset, "id", sourcePath, diagnostics);
                if (!string.IsNullOrEmpty(id) && !ContentManifest.IsStableId(id))
                {
                    diagnostics.Add(Block("manifest.asset.id.invalid", "Asset ID must match " + ContentManifest.StableIdExpression + ".", sourcePath));
                }
                else if (!string.IsNullOrEmpty(id) && !ids.Add(id))
                {
                    diagnostics.Add(Block("manifest.asset.id.duplicate", "Duplicate asset ID: " + id + ".", sourcePath));
                }

                var sourceType = RequiredString(asset, "sourceType", sourcePath, diagnostics);
                var status = RequiredString(asset, "status", sourcePath, diagnostics);
                if (!string.IsNullOrEmpty(sourceType) && !SourceTypes.Contains(sourceType))
                {
                    diagnostics.Add(Block("manifest.asset.source_type.invalid", "Unknown sourceType: " + sourceType + ".", sourcePath));
                }
                if (!string.IsNullOrEmpty(status) && !Statuses.Contains(status))
                {
                    diagnostics.Add(Block("manifest.asset.status.invalid", "Unknown status: " + status + ".", sourcePath));
                }
                if (production && string.Equals(status, "draft", StringComparison.Ordinal))
                {
                    diagnostics.Add(Block("manifest.asset.draft.production", "Draft asset is not allowed in a production build: " + id + ".", sourcePath));
                }

                var relativePath = NullableString(asset, "relativePath", sourcePath, diagnostics);
                var sha256 = NullableString(asset, "sha256", sourcePath, diagnostics);
                if (string.Equals(sourceType, "file", StringComparison.Ordinal))
                {
                    if (string.IsNullOrEmpty(relativePath))
                    {
                        diagnostics.Add(Block("manifest.asset.path.missing", "File asset requires relativePath.", sourcePath));
                    }
                    else if (!TryNormalizeRelativePath(relativePath, contentRoot, out var normalized, out var pathError))
                    {
                        diagnostics.Add(Block("manifest.asset.path.unsafe", pathError, sourcePath));
                    }
                    else
                    {
                        if (!paths.Add(normalized))
                        {
                            diagnostics.Add(Block("manifest.asset.path.duplicate", "Duplicate asset path: " + normalized + ".", sourcePath));
                        }
                        referencedFiles.Add(normalized);
                        if (!IsAuthoredPath(normalized))
                        {
                            var code = IsInboxPath(normalized)
                                ? "manifest.asset.inbox.forbidden"
                                : "manifest.asset.source_path.forbidden";
                            diagnostics.Add(Block(code, "Production content assets must be under authored/; inbox and other source paths are not staged.", sourcePath));
                        }
                    }

                    if (string.IsNullOrEmpty(sha256))
                    {
                        diagnostics.Add(Block("manifest.asset.hash.missing", "File asset requires sha256.", sourcePath));
                    }
                    else if (!IsSha256(sha256))
                    {
                        diagnostics.Add(Block("manifest.asset.hash.invalid", "sha256 must contain exactly 64 hexadecimal characters.", sourcePath));
                    }
                }
                else if (string.Equals(sourceType, "programmatic", StringComparison.Ordinal) &&
                    (!string.IsNullOrEmpty(relativePath) || !string.IsNullOrEmpty(sha256)))
                {
                    diagnostics.Add(Block("manifest.asset.programmatic_path.invalid", "Programmatic assets cannot declare a path or hash.", sourcePath));
                }

                assetIndex++;
            }
        }

        private static ContentCatalog? AddCatalogValidation(
            string contentRoot,
            string manifestPath,
            bool production,
            ICollection<ContentDiagnostic> diagnostics)
        {
            try
            {
                // Keep semantic validation in the Data layer; this prevents the
                // Editor scanner from silently becoming a second content truth.
                return ContentCatalog.LoadFile(manifestPath, contentRoot, production);
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(Block("manifest.catalog.invalid", exception.Message, ManifestRelativePath));
            }
            catch (IOException exception)
            {
                diagnostics.Add(Block("manifest.catalog.read_failed", exception.Message, ManifestRelativePath));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(Block("manifest.catalog.read_denied", exception.Message, ManifestRelativePath));
            }

            return null;
        }

        private static void AddSkinValidation(
            string contentRoot,
            ContentCatalog contentCatalog,
            bool production,
            ICollection<string> referencedFiles,
            ICollection<ContentDiagnostic> diagnostics)
        {
            var skinDirectory = Path.Combine(
                contentRoot,
                SkinDirectoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(skinDirectory)) return;

            var paths = Directory.GetFiles(skinDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (paths.Length == 0)
            {
                diagnostics.Add(Block(
                    "skin.directory.empty",
                    "Skin directory contains no JSON manifests.",
                    SkinDirectoryRelativePath));
                return;
            }

            foreach (var path in paths)
                referencedFiles.Add(ToRelative(path, contentRoot).Replace('\\', '/'));

            try
            {
                _ = ContentSkinCatalog.LoadDirectory(skinDirectory, contentCatalog, production);
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(Block("skin.catalog.invalid", exception.Message, SkinDirectoryRelativePath));
            }
            catch (IOException exception)
            {
                diagnostics.Add(Block("skin.catalog.read_failed", exception.Message, SkinDirectoryRelativePath));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(Block("skin.catalog.read_denied", exception.Message, SkinDirectoryRelativePath));
            }
        }

        private static void AddOrphanDiagnostics(
            string contentRoot,
            IReadOnlyCollection<string> referencedFiles,
            ICollection<ContentDiagnostic> diagnostics)
        {
            var authoredRoot = Path.Combine(contentRoot, AuthoredRelativePath);
            if (!Directory.Exists(authoredRoot))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticSeverity.Warning, "content.authored.missing", "Authored content directory is missing.", AuthoredRelativePath));
                return;
            }

            var referenced = new HashSet<string>(referencedFiles, StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(authoredRoot, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var relative = ToRelative(file, contentRoot).Replace('\\', '/');
                if (!referenced.Contains(relative))
                {
                    diagnostics.Add(new ContentDiagnostic(ContentDiagnosticSeverity.Warning, "asset.orphan", "Authored file is not referenced by the manifest; it will not be staged.", relative));
                }
            }
        }

        private static void AddInboxDiagnostics(string contentRoot, ICollection<ContentDiagnostic> diagnostics)
        {
            var inboxRoot = Path.Combine(contentRoot, InboxRelativePath);
            if (!Directory.Exists(inboxRoot)) return;
            foreach (var file in Directory.GetFiles(inboxRoot, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticSeverity.Info, "asset.inbox.ignored", "Inbox asset is intentionally not staged until it is authored and added to the manifest.", ToRelative(file, contentRoot).Replace('\\', '/')));
            }
        }

        private static void AddUnusedDiagnostics(JObject root, string contentRoot, ICollection<ContentDiagnostic> diagnostics)
        {
            var assetsToken = root["assets"] as JArray;
            if (assetsToken is null) return;

            var references = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asset in assetsToken.OfType<JObject>())
            {
                AddReference(asset, "fallbackId", references);
            }
            if (root["aliases"] is JArray aliases)
            {
                foreach (var alias in aliases.OfType<JObject>()) AddReference(alias, "toId", references);
            }

            AddSkinReferences(contentRoot, references);

            var cardsRoot = Directory.GetParent(contentRoot)?.FullName is string dataRoot
                ? Path.Combine(dataRoot, "cards")
                : string.Empty;
            if (Directory.Exists(cardsRoot))
            {
                foreach (var file in Directory.GetFiles(cardsRoot, "*.json", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var token = JToken.Parse(File.ReadAllText(file, Encoding.UTF8));
                        foreach (var artId in token.SelectTokens("$..artId"))
                        {
                            if (artId.Type == JTokenType.String && !string.IsNullOrEmpty(artId.Value<string>())) references.Add(artId.Value<string>()!);
                        }
                    }
                    catch (JsonException)
                    {
                        // Card validation owns malformed card JSON. The content
                        // pipeline must not invent a second card-data error.
                    }
                    catch (IOException)
                    {
                        // Same boundary: card data has its own build gate.
                    }
                }
            }

            foreach (var asset in assetsToken.OfType<JObject>().OrderBy(value => value.Value<string>("id") ?? string.Empty, StringComparer.Ordinal))
            {
                var id = asset.Value<string>("id");
                if (!string.IsNullOrEmpty(id) && !references.Contains(id))
                {
                    diagnostics.Add(new ContentDiagnostic(ContentDiagnosticSeverity.Warning, "asset.unused", "Manifest asset has no card, alias, or fallback reference.", id));
                }
            }
        }

        private static void AddSkinReferences(string contentRoot, ISet<string> references)
        {
            var skinDirectory = Path.Combine(
                contentRoot,
                SkinDirectoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(skinDirectory)) return;

            foreach (var path in Directory.GetFiles(skinDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var skin = JToken.Parse(File.ReadAllText(path, Encoding.UTF8)) as JObject;
                    if (skin is null) continue;
                    foreach (var property in new[] { "assets", "cardArtwork", "audioCues", "vfxCues" })
                    {
                        if (skin[property] is not JObject map) continue;
                        foreach (var value in map.Properties())
                        {
                            if (value.Value.Type == JTokenType.String && !string.IsNullOrEmpty(value.Value.Value<string>()))
                                references.Add(value.Value.Value<string>()!);
                        }
                    }
                }
                catch (JsonException)
                {
                    // Skin semantic validation reports malformed JSON. This
                    // diagnostic pass must not create a second error.
                }
                catch (IOException)
                {
                    // The skin validation pass owns read failures.
                }
                catch (UnauthorizedAccessException)
                {
                    // The skin validation pass owns access failures.
                }
            }
        }

        private static void AddReference(JObject value, string property, ISet<string> references)
        {
            var token = value[property];
            if (token?.Type == JTokenType.String && !string.IsNullOrEmpty(token.Value<string>()))
            {
                references.Add(token.Value<string>()!);
            }
        }

        private static string RequiredString(
            JObject value,
            string property,
            string sourcePath,
            ICollection<ContentDiagnostic> diagnostics)
        {
            var token = value[property];
            if (token?.Type != JTokenType.String || string.IsNullOrEmpty(token.Value<string>()))
            {
                diagnostics.Add(Block("manifest.field.missing", "Required field is missing or empty: " + property + ".", sourcePath));
                return string.Empty;
            }
            return token.Value<string>()!;
        }

        private static string? NullableString(
            JObject value,
            string property,
            string sourcePath,
            ICollection<ContentDiagnostic> diagnostics)
        {
            var token = value[property];
            if (token is null || token.Type == JTokenType.Null) return null;
            if (token.Type != JTokenType.String)
            {
                diagnostics.Add(Block("manifest.field.type.invalid", "Field must be a string or null: " + property + ".", sourcePath));
                return null;
            }
            return token.Value<string>();
        }

        private static bool TryNormalizeRelativePath(
            string value,
            string root,
            out string normalized,
            out string error)
        {
            normalized = value.Replace('\\', '/');
            error = "Unsafe relative path: " + value + ".";
            if (value.Length == 0 || value.IndexOf('\\') >= 0 || value.StartsWith("/", StringComparison.Ordinal) ||
                value.StartsWith("//", StringComparison.Ordinal) || Path.IsPathRooted(value) ||
                (value.Length >= 2 && value[1] == ':')) return false;
            var segments = normalized.Split('/');
            if (segments.Any(segment => segment.Length == 0 || segment == "." || segment == ".." || ContainsInvalidPathCharacter(segment))) return false;
            try
            {
                var full = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsWithinRoot(full, root)) return false;
                normalized = ToRelative(full, root).Replace('\\', '/');
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is NotSupportedException)
            {
                error = "Unsafe relative path: " + value + " (" + exception.Message + ").";
                return false;
            }
        }

        private static bool IsAuthoredPath(string path)
        {
            return path.Equals(AuthoredRelativePath, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(AuthoredRelativePath + "/", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsStageableReferencedPath(string path)
        {
            return IsAuthoredPath(path) ||
                path.StartsWith(SkinDirectoryRelativePath + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInboxPath(string path)
        {
            return path.Equals(InboxRelativePath, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(InboxRelativePath + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsInvalidPathCharacter(string value)
        {
            return value.Any(character => character < 32 || character == ':' || character == '*' || character == '?' || character == '"' || character == '<' || character == '>' || character == '|');
        }

        private static bool IsSha256(string value)
        {
            return value.Length == 64 && value.All(character => (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') || (character >= 'A' && character <= 'F'));
        }

        private static bool IsWithinRoot(string path, string root)
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToRelative(string path, string root)
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(fullRoot.Length)
                : fullPath;
        }

        private static ContentDiagnostic Block(string code, string message, string path)
        {
            return new ContentDiagnostic(ContentDiagnosticSeverity.Blocking, code, message, path);
        }
    }

    /// <summary>Deterministic JSON report writer used by the build gate and CI.</summary>
    public static class ContentBuildReportWriter
    {
        public static void Write(ContentValidationReport report, string outputPath)
        {
            if (report is null) throw new ArgumentNullException(nameof(report));
            if (outputPath is null) throw new ArgumentNullException(nameof(outputPath));
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var diagnostics = new JArray();
            foreach (var diagnostic in report.Diagnostics)
            {
                diagnostics.Add(new JObject
                {
                    ["severity"] = diagnostic.Severity.ToString().ToUpperInvariant(),
                    ["code"] = diagnostic.Code,
                    ["path"] = diagnostic.Path,
                    ["message"] = diagnostic.Message
                });
            }
            var document = new JObject
            {
                ["reportVersion"] = 1,
                ["manifestPath"] = report.ManifestPath,
                ["canBuild"] = report.CanBuild,
                ["infoCount"] = report.InfoCount,
                ["warningCount"] = report.WarningCount,
                ["errorCount"] = report.ErrorCount,
                ["blockingCount"] = report.BlockingCount,
                ["referencedFiles"] = new JArray(report.ReferencedFiles),
                ["diagnostics"] = diagnostics
            };
            File.WriteAllText(outputPath, document.ToString(Formatting.Indented) + Environment.NewLine, new UTF8Encoding(false));
        }
    }

    /// <summary>
    /// Owns only Assets/StreamingAssets/content. It never deletes a destination
    /// unless the exact ownership marker and generated-only shape are present.
    /// </summary>
    public static class ContentPipelineStaging
    {
        public const string GeneratedRelativePath = "Assets/StreamingAssets/content";
        public const string GeneratedMarkerName = ".content-generated";
        public const string GeneratedMarkerContents = "Generated by ContentPipelineStaging.cs.\n";
        public const string GeneratedIgnoreContents = "# Generated by ContentPipelineStaging.cs during player builds.\n*\n!.gitignore\n!" + GeneratedMarkerName + "\n";

        public static bool IsExpectedAssetPath(string assetPath)
        {
            return string.Equals(assetPath, GeneratedRelativePath, StringComparison.Ordinal) || string.Equals(assetPath, "Assets/StreamingAssets", StringComparison.Ordinal);
        }

        public static bool IsOwnedGeneratedRoot(string path)
        {
            if (!Directory.Exists(path)) return false;
            try
            {
                if (IsReparsePoint(path)) return false;
                var marker = Path.Combine(path, GeneratedMarkerName);
                if (!File.Exists(marker) || !TextMatches(File.ReadAllText(marker), GeneratedMarkerContents)) return false;

                // The manifest is the source of truth for the authored files that
                // this generated root is allowed to contain. Unity may add .meta
                // files beside those known files and directories, but an
                // arbitrary .meta (or any other extra file) must fail closed.
                var report = ContentPipelineValidator.Validate(path, production: true);
                if (!report.CanBuild) return false;

                var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    GeneratedMarkerName,
                    ".gitignore",
                    ContentPipelineValidator.ManifestRelativePath
                };
                var expectedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                AddExpectedDirectoryParents(ContentPipelineValidator.ManifestRelativePath, expectedDirectories);
                foreach (var relativePath in report.ReferencedFiles)
                {
                    if (!ContentPipelineValidator.IsStageableReferencedPath(relativePath))
                        return false;
                    expectedFiles.Add(relativePath);
                    AddExpectedDirectoryParents(relativePath, expectedDirectories);
                }

                var fullRoot = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                foreach (var entry in Directory.GetFileSystemEntries(fullRoot, "*", SearchOption.AllDirectories))
                {
                    if (IsReparsePoint(entry)) return false;
                    var relative = ToGeneratedRelative(entry, fullRoot);
                    var isMeta = relative.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
                    var comparisonPath = isMeta
                        ? relative.Substring(0, relative.Length - ".meta".Length)
                        : relative;
                    if (Directory.Exists(entry))
                    {
                        if (isMeta || !expectedDirectories.Contains(comparisonPath)) return false;
                        continue;
                    }

                    if (isMeta)
                    {
                        if (!expectedFiles.Contains(comparisonPath) && !expectedDirectories.Contains(comparisonPath)) return false;
                    }
                    else if (!expectedFiles.Contains(comparisonPath))
                    {
                        return false;
                    }
                }

                foreach (var relativePath in expectedFiles)
                    if (!File.Exists(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))) return false;
                foreach (var relativePath in expectedDirectories)
                    if (!Directory.Exists(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))) return false;
                return true;
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (Exception) { return false; }
        }

        public static bool IsSafeDestination(string destinationRoot, string streamingAssetsRoot)
        {
            var destination = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var streaming = Path.GetFullPath(streamingAssetsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return destination.Equals(Path.Combine(streaming, "content"), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFileName(streaming), "StreamingAssets", StringComparison.Ordinal);
        }

        public static string CreateStagingDirectory(string contentRoot, ContentValidationReport report)
        {
            if (contentRoot is null) throw new ArgumentNullException(nameof(contentRoot));
            if (report is null) throw new ArgumentNullException(nameof(report));
            if (!report.CanBuild) throw new BuildFailedException("Content validation has BLOCKING diagnostics; no generated content was changed.");

            var staging = Path.Combine(Path.GetTempPath(), "dominion-wars-content-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                var manifestDestination = Path.Combine(staging, ContentPipelineValidator.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(manifestDestination)!);
                File.Copy(report.ManifestPath, manifestDestination, true);
                foreach (var relativePath in report.ReferencedFiles)
                {
                    if (!ContentPipelineValidator.IsStageableReferencedPath(relativePath))
                        throw new BuildFailedException("Content staging refused non-stageable path: " + relativePath);
                    var source = Path.Combine(contentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    var destination = Path.Combine(staging, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(source)) throw new BuildFailedException("Content staging source is missing: " + relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination, true);
                }
                File.WriteAllText(Path.Combine(staging, GeneratedMarkerName), GeneratedMarkerContents, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(staging, ".gitignore"), GeneratedIgnoreContents, new UTF8Encoding(false));
                return staging;
            }
            catch
            {
                TryDeleteDirectory(staging);
                throw;
            }
        }

        public static void ReplaceGeneratedRoot(string stagingRoot, string destinationRoot, string destinationAssetPath)
        {
            if (stagingRoot is null) throw new ArgumentNullException(nameof(stagingRoot));
            if (destinationRoot is null) throw new ArgumentNullException(nameof(destinationRoot));
            if (!Directory.Exists(stagingRoot)) throw new BuildFailedException("Content staging directory is missing.");
            if (!string.Equals(destinationAssetPath, GeneratedRelativePath, StringComparison.Ordinal) ||
                !IsSafeDestination(destinationRoot, Path.GetDirectoryName(destinationRoot)!))
                throw new BuildFailedException("Content staging destination escaped StreamingAssets/content.");
            if (Directory.Exists(destinationRoot) && !IsOwnedGeneratedRoot(destinationRoot)) throw new BuildFailedException("Content destination is not owned; user StreamingAssets content was preserved.");

            var streamingAssetsRoot = Path.GetDirectoryName(Path.GetFullPath(destinationRoot));
            if (string.IsNullOrEmpty(streamingAssetsRoot))
                throw new BuildFailedException("Content staging destination has no StreamingAssets parent.");
            Directory.CreateDirectory(streamingAssetsRoot);

            var siblingStageRoot = CreateUniqueSiblingPath(streamingAssetsRoot, "DominionWars.ContentStage.");
            var siblingBackupRoot = CreateUniqueSiblingPath(streamingAssetsRoot, "DominionWars.ContentBackup.");
            var siblingStageAssetPath = ToAssetPath(siblingStageRoot);
            var siblingBackupAssetPath = ToAssetPath(siblingBackupRoot);
            var oldRootMoved = false;
            var newRootMoved = false;
            try
            {
                // Copy into a complete sibling under StreamingAssets. This
                // keeps the old destination intact until the copy and its
                // second validation have both succeeded.
                Directory.CreateDirectory(siblingStageRoot);
                CopyDirectory(stagingRoot, siblingStageRoot);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var stagedReport = ValidateStagingDirectory(siblingStageRoot);
                if (!stagedReport.CanBuild || !IsOwnedGeneratedRoot(siblingStageRoot))
                    throw new BuildFailedException(
                        "Generated content staging failed re-validation with " +
                        stagedReport.BlockingCount + " BLOCKING diagnostic(s); the previous content was preserved.");

                var destinationExisted = Directory.Exists(destinationRoot);
                if (!TryExchangeGeneratedRoots(
                        siblingStageRoot,
                        destinationRoot,
                        siblingBackupRoot,
                        (source, destination) => AssetDatabase.MoveAsset(ToAssetPath(source), ToAssetPath(destination)),
                        out var exchangeError))
                    throw new BuildFailedException(exchangeError);
                oldRootMoved = destinationExisted;
                newRootMoved = true;
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                if (oldRootMoved && Directory.Exists(siblingBackupRoot))
                {
                    // The backup is known-owned because the old destination
                    // was checked before the exchange. If Unity cannot remove
                    // it, retain it for recovery rather than deleting blindly.
                    if (!AssetDatabase.DeleteAsset(siblingBackupAssetPath) && Directory.Exists(siblingBackupRoot))
                    {
                        Debug.LogWarning("Dominion Wars installed generated content but retained its owned backup at " + siblingBackupAssetPath + ".");
                    }
                }
            }
            catch (Exception exception)
            {
                var rollbackError = TryRollbackGeneratedRoot(
                    destinationRoot,
                    destinationAssetPath,
                    siblingStageRoot,
                    siblingBackupRoot,
                    siblingBackupAssetPath,
                    oldRootMoved,
                    newRootMoved);
                var message = "Could not replace generated content asset: " + destinationAssetPath + ".";
                if (!string.IsNullOrEmpty(rollbackError)) message += " Rollback warning: " + rollbackError;
                message += " " + exception.Message;
                throw new BuildFailedException(message);
            }
            finally
            {
                TryDeleteOwnedSibling(siblingStageRoot, siblingStageAssetPath);
            }
        }

        public static ContentValidationReport ValidateStagingDirectory(string stagingRoot)
        {
            if (stagingRoot is null) throw new ArgumentNullException(nameof(stagingRoot));
            return ContentPipelineValidator.Validate(stagingRoot, production: true);
        }

        /// <summary>
        /// Exchanges a complete generated sibling into the destination and
        /// restores the previous destination if the install move fails. The
        /// move delegate keeps this transaction independently testable while
        /// production uses AssetDatabase.MoveAsset.
        /// </summary>
        public static bool TryExchangeGeneratedRoots(
            string stagingRoot,
            string destinationRoot,
            string backupRoot,
            Func<string, string, string> move,
            out string error)
        {
            if (stagingRoot is null) throw new ArgumentNullException(nameof(stagingRoot));
            if (destinationRoot is null) throw new ArgumentNullException(nameof(destinationRoot));
            if (backupRoot is null) throw new ArgumentNullException(nameof(backupRoot));
            if (move is null) throw new ArgumentNullException(nameof(move));

            var previousMoved = false;
            error = string.Empty;
            try
            {
                if (Directory.Exists(destinationRoot))
                {
                    var backupError = move(destinationRoot, backupRoot);
                    if (!string.IsNullOrEmpty(backupError))
                    {
                        error = "Could not back up generated content: " + backupError;
                        if (!Directory.Exists(destinationRoot) && Directory.Exists(backupRoot))
                        {
                            var restoreError = move(backupRoot, destinationRoot);
                            if (!string.IsNullOrEmpty(restoreError))
                                error += " Previous content restore failed: " + restoreError;
                        }
                        return false;
                    }
                    previousMoved = true;
                }

                var installError = move(stagingRoot, destinationRoot);
                if (!string.IsNullOrEmpty(installError))
                {
                    error = "Could not install generated content: " + installError;
                    if (previousMoved && !Directory.Exists(destinationRoot) && Directory.Exists(backupRoot))
                    {
                        var restoreError = move(backupRoot, destinationRoot);
                        if (!string.IsNullOrEmpty(restoreError))
                            error += " Previous content restore failed: " + restoreError;
                    }
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "Generated content exchange failed: " + exception.Message;
                if ((!previousMoved && Directory.Exists(backupRoot)) || previousMoved)
                {
                    if (!Directory.Exists(destinationRoot) && Directory.Exists(backupRoot))
                    {
                        try
                        {
                            var restoreError = move(backupRoot, destinationRoot);
                            if (!string.IsNullOrEmpty(restoreError))
                                error += " Previous content restore failed: " + restoreError;
                        }
                        catch (Exception restoreException)
                        {
                            error += " Previous content restore failed: " + restoreException.Message;
                        }
                    }
                }
                return false;
            }
        }

        public static void CleanupGeneratedRoot(string destinationRoot, string destinationAssetPath)
        {
            if (!Directory.Exists(destinationRoot)) return;
            if (!IsOwnedGeneratedRoot(destinationRoot))
            {
                Debug.LogError("Dominion Wars refused to clean " + destinationAssetPath + ": ownership marker or generated-only shape is missing. No content was deleted.");
                return;
            }
            var deleted = AssetDatabase.DeleteAsset(destinationAssetPath);
            Debug.Log(
                "Dominion Wars generated content cleanup requested: asset=" + destinationAssetPath +
                ", assetDatabaseDeleted=" + deleted +
                ", directoryRemains=" + Directory.Exists(destinationRoot) + ".");
            if (deleted) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        public static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void CopyDirectory(string source, string destination)
        {
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (var directory in Directory.GetDirectories(source))
            {
                var child = Path.Combine(destination, Path.GetFileName(directory));
                Directory.CreateDirectory(child);
                CopyDirectory(directory, child);
            }
        }

        private static string CreateUniqueSiblingPath(string streamingAssetsRoot, string prefix)
        {
            var fullStreamingRoot = Path.GetFullPath(streamingAssetsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var candidate = Path.Combine(fullStreamingRoot, prefix + Guid.NewGuid().ToString("N"));
                if (!IsSafeSiblingPath(candidate, fullStreamingRoot))
                    throw new BuildFailedException("Generated content sibling path escaped StreamingAssets.");
                if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
            }
            throw new BuildFailedException("Could not allocate a unique generated content sibling path.");
        }

        private static bool IsSafeSiblingPath(string candidate, string streamingAssetsRoot)
        {
            var fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullStreamingRoot = Path.GetFullPath(streamingAssetsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(Path.GetDirectoryName(fullCandidate), fullStreamingRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToAssetPath(string absolutePath)
        {
            var assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(absolutePath);
            if (!fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("Generated content sibling escaped the Unity Assets directory.");
            return "Assets" + fullPath.Substring(assetsRoot.Length).Replace('\\', '/');
        }

        private static string? TryRollbackGeneratedRoot(
            string destinationRoot,
            string destinationAssetPath,
            string siblingStageRoot,
            string siblingBackupRoot,
            string siblingBackupAssetPath,
            bool oldRootMoved,
            bool newRootMoved)
        {
            var errors = new List<string>();
            try
            {
                if (newRootMoved && Directory.Exists(destinationRoot))
                {
                    var failedRoot = CreateUniqueSiblingPath(Path.GetDirectoryName(destinationRoot)!, "DominionWars.ContentFailed.");
                    var failedAssetPath = ToAssetPath(failedRoot);
                    var moveError = AssetDatabase.MoveAsset(destinationAssetPath, failedAssetPath);
                    if (!string.IsNullOrEmpty(moveError))
                    {
                        errors.Add("could not move the new root aside: " + moveError);
                    }
                    else
                    {
                        if (Directory.Exists(failedRoot) && IsOwnedGeneratedRoot(failedRoot) &&
                            !AssetDatabase.DeleteAsset(failedAssetPath) && Directory.Exists(failedRoot))
                            errors.Add("could not remove the failed new root");
                    }
                }

                if (oldRootMoved && !Directory.Exists(destinationRoot) && Directory.Exists(siblingBackupRoot))
                {
                    var moveError = AssetDatabase.MoveAsset(siblingBackupAssetPath, destinationAssetPath);
                    if (!string.IsNullOrEmpty(moveError)) errors.Add("could not restore the previous root: " + moveError);
                }
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
            }

            TryDeleteOwnedSibling(siblingStageRoot, ToAssetPath(siblingStageRoot));
            if (Directory.Exists(siblingBackupRoot) && !oldRootMoved)
                TryDeleteOwnedSibling(siblingBackupRoot, siblingBackupAssetPath);
            return errors.Count == 0 ? null : string.Join("; ", errors);
        }

        private static void TryDeleteOwnedSibling(string siblingRoot, string siblingAssetPath)
        {
            if (!Directory.Exists(siblingRoot) && !File.Exists(siblingRoot)) return;
            var name = Path.GetFileName(siblingRoot);
            if (!name.StartsWith("DominionWars.ContentStage.", StringComparison.Ordinal) &&
                !name.StartsWith("DominionWars.ContentBackup.", StringComparison.Ordinal) &&
                !name.StartsWith("DominionWars.ContentFailed.", StringComparison.Ordinal)) return;
            var parent = Path.GetDirectoryName(Path.GetFullPath(siblingRoot));
            if (string.IsNullOrEmpty(parent) ||
                !string.Equals(Path.GetFileName(parent), "StreamingAssets", StringComparison.OrdinalIgnoreCase)) return;
            if (AssetDatabase.DeleteAsset(siblingAssetPath)) return;
            TryDeleteDirectory(siblingRoot);
            var metaPath = siblingRoot + ".meta";
            if (File.Exists(metaPath))
            {
                try { File.Delete(metaPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void AddExpectedDirectoryParents(string relativePath, ISet<string> expectedDirectories)
        {
            var separator = relativePath.LastIndexOf('/');
            while (separator > 0)
            {
                expectedDirectories.Add(relativePath.Substring(0, separator));
                separator = relativePath.LastIndexOf('/', separator - 1);
            }
        }

        private static string ToGeneratedRelative(string path, string fullRoot)
        {
            var fullPath = Path.GetFullPath(path);
            return fullPath.Substring(fullRoot.Length + 1).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        private static bool TextMatches(string actual, string expected)
        {
            return string.Equals(actual.Replace("\r\n", "\n").Trim(), expected.Replace("\r\n", "\n").Trim(), StringComparison.Ordinal);
        }
    }
}
#endif
