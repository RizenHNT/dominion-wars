using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DominionWars.Data.CardEditor
{
    /// <summary>
    /// Transactional persistence for one <see cref="CardDocument"/>.
    ///
    /// The store deliberately does not perform card/schema validation. The
    /// editor owns that step; this type is the final persistence boundary and
    /// only writes a deterministic document after an optimistic-concurrency
    /// check succeeds.
    /// </summary>
    public sealed class CardDocumentStore
    {
        private const int BufferSize = 4096;

        /// <summary>Loads a card document and captures its source byte hash.</summary>
        public CardDocument Load(string path)
        {
            return CardDocument.LoadFile(path);
        }

        /// <summary>
        /// Saves to the document's source path. A document loaded from disk is
        /// required so an existing file can never be overwritten without a
        /// baseline hash.
        /// </summary>
        public CardDocumentSaveResult Save(CardDocument document)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            if (string.IsNullOrWhiteSpace(document.SourcePath))
            {
                throw new InvalidOperationException(
                    "A new card document requires an explicit save path.");
            }

            return Save(document, document.SourcePath!);
        }

        /// <summary>
        /// Saves to <paramref name="path"/> using a same-directory temporary
        /// file, a durable backup, and an atomic replacement. Existing files
        /// must have the same byte hash that was captured at load time.
        /// </summary>
        public CardDocumentSaveResult Save(CardDocument document, string path)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (path.Length == 0) throw new ArgumentException("A save path is required.", nameof(path));

            var targetPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("A save path must have a parent directory.");
            }

            if (Directory.Exists(targetPath))
            {
                throw new IOException("Card document save target is a directory: " + targetPath);
            }

            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(directory);
            }

            var exists = File.Exists(targetPath);
            var oldBytes = exists ? ReadBytes(targetPath) : null;
            var oldHash = oldBytes is null ? null : Sha256(oldBytes);
            EnsureBaselineMatches(document, targetPath, exists, oldHash);

            var oldJson = oldBytes is null
                ? null
                : ParseCardFile(oldBytes, targetPath);
            var newBytes = SerializeDeterministically(document);
            var newHash = Sha256(newBytes);
            var newJson = ParseCardFile(newBytes, targetPath);
            var changes = ComputeFieldChanges(oldJson, newJson);

            string? temporaryPath = null;
            string? backupPath = null;
            try
            {
                temporaryPath = WriteTemporaryFile(directory, Path.GetFileName(targetPath), newBytes);
                if (exists)
                {
                    backupPath = WriteBackupFile(directory, Path.GetFileName(targetPath), oldBytes!);
                }

                AtomicReplace(temporaryPath, targetPath, exists);
                temporaryPath = null;

                // A future save must compare against the bytes just committed.
                document.BaselineHash = newHash;
                return new CardDocumentSaveResult(
                    targetPath,
                    oldHash,
                    newHash,
                    backupPath,
                    changes);
            }
            catch
            {
                // Never remove a backup: it is the caller's recovery copy.
                // The temporary file contains only the new candidate and can
                // be removed when replacement did not consume it.
                if (temporaryPath is not null)
                {
                    TryDelete(temporaryPath);
                }

                throw;
            }
        }

        /// <summary>
        /// Replaces one card inside a multi-card JSON array while preserving
        /// every other card. CardDocumentIndex loads production faction files
        /// this way, so saving a selected card must never serialize only the
        /// selected entry and silently discard its neighbours.
        /// </summary>
        public CardDocumentSaveResult SaveCollection(CardDocument document, string path)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (path.Length == 0) throw new ArgumentException("A save path is required.", nameof(path));

            var targetPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("A save path must have a parent directory.");
            if (Directory.Exists(targetPath))
                throw new IOException("Card document save target is a directory: " + targetPath);
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException(directory);

            if (!File.Exists(targetPath))
                return Save(document, targetPath);

            var oldBytes = ReadBytes(targetPath);
            var oldHash = Sha256(oldBytes);
            EnsureBaselineMatches(document, targetPath, exists: true, oldHash);

            JToken oldRoot;
            try
            {
                oldRoot = JToken.Parse(
                    Encoding.UTF8.GetString(oldBytes),
                    new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(targetPath + ": card collection JSON is invalid", exception);
            }

            if (oldRoot is not JArray oldArray || oldArray.Count == 0)
                return Save(document, targetPath);

            var selectedIndex = -1;
            var selectedId = document.Id;
            if (string.IsNullOrWhiteSpace(selectedId))
                throw new InvalidDataException("A collection card must have a non-empty id.");

            for (var index = 0; index < oldArray.Count; index++)
            {
                if (oldArray[index] is not JObject entry)
                    throw new InvalidDataException(
                        targetPath + ": card collection contains a non-object entry at index " + index + ".");

                var entryId = entry["id"]?.Type == JTokenType.String
                    ? entry["id"]!.ToObject<string>()
                    : null;
                if (!string.Equals(entryId, selectedId, StringComparison.Ordinal)) continue;
                if (selectedIndex >= 0)
                    throw new InvalidDataException(
                        targetPath + ": duplicate card id in collection: " + selectedId + ".");
                selectedIndex = index;
            }

            if (selectedIndex < 0)
                throw new InvalidDataException(
                    targetPath + ": selected card id was not found in collection: " + selectedId + ".");

            var oldSelected = (JObject)oldArray[selectedIndex]!;
            var replacement = document.ToJsonObject();
            var newArray = (JArray)oldArray.DeepClone();
            newArray[selectedIndex] = replacement;
            var newRoot = Canonicalize(newArray);
            var newJsonBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                .GetBytes(newRoot.ToString(Formatting.Indented));
            var newHash = Sha256(newJsonBytes);
            var changes = ComputeFieldChanges(oldSelected, replacement);

            string? temporaryPath = null;
            string? backupPath = null;
            try
            {
                temporaryPath = WriteTemporaryFile(directory, Path.GetFileName(targetPath), newJsonBytes);
                backupPath = WriteBackupFile(directory, Path.GetFileName(targetPath), oldBytes);
                AtomicReplace(temporaryPath, targetPath, targetExists: true);
                temporaryPath = null;
                document.BaselineHash = newHash;
                return new CardDocumentSaveResult(
                    targetPath,
                    oldHash,
                    newHash,
                    backupPath,
                    changes);
            }
            catch
            {
                if (temporaryPath is not null) TryDelete(temporaryPath);
                throw;
            }
        }

        private static void EnsureBaselineMatches(
            CardDocument document,
            string targetPath,
            bool exists,
            string? currentHash)
        {
            var expectedHash = document.BaselineHash;
            if (!exists && expectedHash is null)
            {
                return;
            }

            if (expectedHash is null)
            {
                throw new CardDocumentConflictException(
                    targetPath,
                    null,
                    currentHash,
                    "An existing card document requires a load baseline before it can be overwritten.");
            }

            if (!exists || !StringComparer.OrdinalIgnoreCase.Equals(expectedHash, currentHash))
            {
                throw new CardDocumentConflictException(
                    targetPath,
                    expectedHash,
                    currentHash,
                    "The card document changed on disk after it was loaded.");
            }
        }

        private static byte[] ReadBytes(string path)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new IOException("Card document cannot be read: " + path, exception);
            }
        }

        private static string WriteTemporaryFile(string directory, string fileName, byte[] bytes)
        {
            var temporaryPath = Path.Combine(
                directory,
                "." + fileName + "." + Path.GetRandomFileName() + ".tmp");
            try
            {
                using var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.WriteThrough);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
                return temporaryPath;
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        private static string WriteBackupFile(string directory, string fileName, byte[] bytes)
        {
            var stamp = DateTime.UtcNow.ToString(
                "yyyyMMddHHmmssfff",
                System.Globalization.CultureInfo.InvariantCulture);
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var suffix = attempt == 0 ? string.Empty : "-" + attempt;
                var backupPath = Path.Combine(directory, fileName + "." + stamp + suffix + ".bak");
                try
                {
                    using var stream = new FileStream(
                        backupPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.Read,
                        BufferSize,
                        FileOptions.WriteThrough);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                    return backupPath;
                }
                catch (IOException) when (File.Exists(backupPath))
                {
                    // A same-millisecond save already used the name; try the
                    // next suffix without overwriting its recovery copy.
                }
            }

            throw new IOException("Unable to create a unique card document backup.");
        }

        private static void AtomicReplace(string temporaryPath, string targetPath, bool targetExists)
        {
            if (targetExists)
            {
                // The backup was created separately so this replacement does
                // not delegate backup naming or durability to the platform.
                File.Replace(temporaryPath, targetPath, null, true);
            }
            else
            {
                // Both paths are in one directory, so the move is atomic on
                // the supported filesystems and cannot expose a partial file.
                File.Move(temporaryPath, targetPath);
            }
        }

        private static byte[] SerializeDeterministically(CardDocument document)
        {
            var objectValue = Canonicalize(document.ToJsonObject());
            JToken output = document.CameFromArrayEnvelope
                ? new JArray(objectValue)
                : objectValue;
            var json = output.ToString(Formatting.Indented);
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
        }

        private static JToken ParseCardFile(byte[] bytes, string path)
        {
            JToken token;
            try
            {
                token = JToken.Parse(
                    Encoding.UTF8.GetString(bytes),
                    new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(path + ": saved card JSON is invalid", exception);
            }

            if (token is JObject)
            {
                return token;
            }

            if (token is JArray array && array.Count == 1 && array[0] is JObject)
            {
                return array;
            }

            throw new InvalidDataException(
                path + ": card document must be an object or a one-card array");
        }

        private static IReadOnlyList<CardFieldChange> ComputeFieldChanges(
            JToken? oldDocument,
            JToken newDocument)
        {
            var oldObject = UnwrapObject(oldDocument);
            var newObject = UnwrapObject(newDocument)!;
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (oldObject is not null)
            {
                foreach (var property in oldObject.Properties()) names.Add(property.Name);
            }

            foreach (var property in newObject.Properties()) names.Add(property.Name);

            var changes = new List<CardFieldChange>();
            foreach (var name in names.OrderBy(value => value, StringComparer.Ordinal))
            {
                var oldValue = oldObject?.Property(name, StringComparison.Ordinal)?.Value;
                var newValue = newObject.Property(name, StringComparison.Ordinal)?.Value;
                if (oldValue is null && newValue is not null)
                {
                    changes.Add(new CardFieldChange(name, CardFieldChangeKind.Added, null, Compact(newValue)));
                }
                else if (oldValue is not null && newValue is null)
                {
                    changes.Add(new CardFieldChange(name, CardFieldChangeKind.Removed, Compact(oldValue), null));
                }
                else if (oldValue is not null && !JToken.DeepEquals(oldValue, newValue))
                {
                    changes.Add(new CardFieldChange(
                        name,
                        CardFieldChangeKind.Changed,
                        Compact(oldValue),
                        Compact(newValue!)));
                }
            }

            return changes.AsReadOnly();
        }

        private static JObject? UnwrapObject(JToken? document)
        {
            if (document is JObject objectValue) return objectValue;
            if (document is JArray array && array.Count == 1) return array[0] as JObject;
            return null;
        }

        private static string Compact(JToken value)
        {
            var text = value.ToString(Formatting.None);
            return text.Length <= 160 ? text : text.Substring(0, 157) + "...";
        }

        private static JToken Canonicalize(JToken token)
        {
            if (token is JObject objectValue)
            {
                var result = new JObject();
                foreach (var property in objectValue.Properties().OrderBy(value => value.Name, StringComparer.Ordinal))
                {
                    result.Add(property.Name, Canonicalize(property.Value));
                }

                return result;
            }

            if (token is JArray array)
            {
                var result = new JArray();
                foreach (var item in array)
                {
                    result.Add(Canonicalize(item));
                }

                return result;
            }

            return token.DeepClone();
        }

        private static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // Preserve the original failure; the temporary name is
                // intentionally recoverable if the OS still has it open.
            }
            catch (UnauthorizedAccessException)
            {
                // Preserve the original failure for the same reason.
            }
        }
    }

    public enum CardFieldChangeKind
    {
        Added,
        Removed,
        Changed
    }

    /// <summary>One top-level card field changed by a save.</summary>
    public sealed class CardFieldChange
    {
        public CardFieldChange(
            string field,
            CardFieldChangeKind kind,
            string? oldValue,
            string? newValue)
        {
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Kind = kind;
            OldValue = oldValue;
            NewValue = newValue;
        }

        public string Field { get; }
        public CardFieldChangeKind Kind { get; }
        public string? OldValue { get; }
        public string? NewValue { get; }

        public override string ToString()
        {
            return Kind + " " + Field;
        }
    }

    /// <summary>Result and recovery information for one successful save.</summary>
    public sealed class CardDocumentSaveResult
    {
        internal CardDocumentSaveResult(
            string path,
            string? oldHash,
            string newHash,
            string? backupPath,
            IReadOnlyList<CardFieldChange> changes)
        {
            Path = path;
            OldHash = oldHash;
            NewHash = newHash;
            BackupPath = backupPath;
            FieldChanges = changes;
            FieldDiff = changes.Count == 0
                ? "no field changes"
                : string.Join(", ", changes.Select(change => change.ToString()));
        }

        public string Path { get; }
        public string? OldHash { get; }
        public string NewHash { get; }
        public string? BackupPath { get; }
        public IReadOnlyList<CardFieldChange> FieldChanges { get; }
        public string FieldDiff { get; }
    }

    /// <summary>
    /// Indicates that the disk file no longer matches the document's load
    /// baseline. The store refuses to merge or overwrite in this case.
    /// </summary>
    public sealed class CardDocumentConflictException : IOException
    {
        public CardDocumentConflictException(
            string path,
            string? expectedHash,
            string? actualHash,
            string reason)
            : base(path + ": " + reason + " Expected=" + (expectedHash ?? "<none>") + "; Actual=" + (actualHash ?? "<missing>"))
        {
            Path = path;
            ExpectedHash = expectedHash;
            ActualHash = actualHash;
        }

        public string Path { get; }
        public string? ExpectedHash { get; }
        public string? ActualHash { get; }
    }
}
