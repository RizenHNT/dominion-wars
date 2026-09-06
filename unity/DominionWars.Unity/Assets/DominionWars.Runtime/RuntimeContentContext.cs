#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using DominionWars.Data;
using UnityEngine;

namespace DominionWars.Unity.Runtime
{

/// <summary>
/// Inputs for the runtime content root policy. The directory values ending in
/// <c>DataRoot</c> are data directories themselves; StreamingAssetsRoot is
/// their parent and therefore produces sibling data and content directories.
/// Keeping these inputs injectable lets EditMode tests model a Player without
/// changing the process working directory.
/// </summary>
public sealed class RuntimeContentEnvironment
{
    public RuntimeContentEnvironment(
        bool isEditor,
        string explicitDataRoot = "",
        string streamingAssetsRoot = "",
        string cwdDataRoot = "",
        string projectDataRoot = "",
        string repositoryDataRoot = "",
        bool production = true)
    {
        IsEditor = isEditor;
        ExplicitDataRoot = explicitDataRoot ?? string.Empty;
        StreamingAssetsRoot = streamingAssetsRoot ?? string.Empty;
        CurrentDirectoryDataRoot = cwdDataRoot ?? string.Empty;
        ProjectDataRoot = projectDataRoot ?? string.Empty;
        RepositoryDataRoot = repositoryDataRoot ?? string.Empty;
        Production = production;
    }

    public bool IsEditor { get; }
    public string ExplicitDataRoot { get; }
    public string StreamingAssetsRoot { get; }
    public string CurrentDirectoryDataRoot { get; }
    public string ProjectDataRoot { get; }
    public string RepositoryDataRoot { get; }
    public bool Production { get; }

    /// <summary>Creates an environment from the current Unity process without mutating it.</summary>
    public static RuntimeContentEnvironment FromUnity(string explicitDataRoot = "")
    {
        var projectRoot = TryGetParent(Application.dataPath);
        var repositoryRoot = TryGetParent(TryGetParent(projectRoot));
        return new RuntimeContentEnvironment(
            Application.isEditor,
            explicitDataRoot: explicitDataRoot ?? string.Empty,
            streamingAssetsRoot: Application.streamingAssetsPath,
            cwdDataRoot: CombineDataRoot(TryGetCurrentDirectory()),
            projectDataRoot: CombineDataRoot(projectRoot),
            repositoryDataRoot: CombineDataRoot(repositoryRoot));
    }

    private static string TryGetCurrentDirectory()
    {
        try { return Directory.GetCurrentDirectory(); }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string TryGetParent(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return Directory.GetParent(path)?.FullName ?? string.Empty;
        }
        catch (Exception exception) when (
            exception is ArgumentException ||
            exception is IOException ||
            exception is UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string CombineDataRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return string.Empty;
        try { return Path.Combine(root, RuntimeDataRootPolicy.DataDirectoryName); }
        catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException)
        {
            return string.Empty;
        }
    }
}

/// <summary>
/// Declares who may clear a RuntimeContentResolver's cache. A context owns its
/// successful resolver; panels and other presentation consumers receive a
/// borrowed lease and must not destroy or clear the shared resolver.
/// </summary>
public enum RuntimeContentResolverOwnership
{
    None,
    Owned,
    Borrowed,

    // Descriptive aliases for hosts that name the context explicitly.
    ContextOwned = Owned,
    Shared = Borrowed,
}

/// <summary>
/// A non-owning reference to a context resolver. Disposing a borrow is
/// intentionally a no-op; RuntimeContentContext controls resolver lifetime and
/// clears its texture cache once when the context is disposed.
/// </summary>
public sealed class RuntimeContentResolverLease : IDisposable
{
    internal RuntimeContentResolverLease(RuntimeContentResolver resolver)
    {
        Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public RuntimeContentResolver Resolver { get; }
    public RuntimeContentResolver ContentResolver => Resolver;
    public RuntimeContentResolverOwnership Ownership => RuntimeContentResolverOwnership.Borrowed;
    public bool IsBorrowed => true;
    public bool IsOwned => false;

    public void Dispose()
    {
        // The context, not a borrowing panel, owns the resolver and its cache.
    }
}

/// <summary>
/// Shared lazy content boundary for a runtime session. Card data and visual
/// content deliberately have separate roots: data may use Editor fallbacks,
/// while content is always the sibling StreamingAssets/content directory.
/// Only successful loads are cached, so a failed lookup can be retried after a
/// build/staging operation makes the source available.
/// </summary>
public sealed class RuntimeContentContext : IDisposable
{
    private readonly RuntimeDataRootPolicy.RuntimeContentRootSet _roots;
    private CardCatalog? _cardCatalog;
    private RuntimeContentResolver? _contentResolver;
    private string _dataRoot = string.Empty;
    private bool _disposed;

    public RuntimeContentContext()
        : this(RuntimeContentEnvironment.FromUnity())
    {
    }

    public RuntimeContentContext(RuntimeContentEnvironment environment)
    {
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _roots = RuntimeDataRootPolicy.BuildRuntimeRoots(environment);
    }

    public RuntimeContentEnvironment Environment { get; }
    public RuntimeDataRootPolicy.RuntimeContentRootSet Roots => _roots;
    public bool IsEditor => _roots.IsEditor;
    public bool RequireGeneratedMarker => _roots.RequireGeneratedMarker;
    public IReadOnlyList<string> DataCandidates => _roots.DataCandidates;
    public string StreamingAssetsRoot => _roots.StreamingAssetsRoot;
    public string StreamingDataRoot => _roots.StreamingDataRoot;
    public string ContentRoot => _roots.ContentRoot;

    /// <summary>
    /// The selected data root after a successful card-catalog load. It remains
    /// empty until TryGetCardCatalog/RequireCardCatalog succeeds.
    /// </summary>
    public string DataRoot => _dataRoot;
    public string ResolvedDataRoot => _dataRoot;
    public CardCatalog? CardCatalog => _cardCatalog;
    public RuntimeContentResolver? ContentResolver => _contentResolver;
    public bool HasCardCatalog => _cardCatalog is not null;
    public bool HasContentResolver => _contentResolver is not null;
    public bool IsAvailable => HasCardCatalog && HasContentResolver;
    public RuntimeContentResolverOwnership ResolverOwnership =>
        _contentResolver is null
            ? RuntimeContentResolverOwnership.None
            : RuntimeContentResolverOwnership.Owned;
    public bool OwnsContentResolver => _contentResolver is not null;
    public bool IsDisposed => _disposed;

    /// <summary>Creates a context around the current Unity runtime paths.</summary>
    public static RuntimeContentContext Create()
    {
        return new RuntimeContentContext();
    }

    public static RuntimeContentContext Create(RuntimeContentEnvironment environment)
    {
        return new RuntimeContentContext(environment);
    }

    public static bool TryCreate(out RuntimeContentContext context)
    {
        return TryCreate(RuntimeContentEnvironment.FromUnity(), out context);
    }

    public static bool TryCreate(
        RuntimeContentEnvironment environment,
        out RuntimeContentContext context)
    {
        context = null!;
        try
        {
            context = new RuntimeContentContext(environment);
            return true;
        }
        catch (Exception exception) when (IsExpectedContextFailure(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// Requires construction of the root context. Resource files remain lazy;
    /// callers that require a loaded catalog should use the corresponding
    /// RequireCardCatalog/RequireContentResolver methods.
    /// </summary>
    public static RuntimeContentContext Require()
    {
        return new RuntimeContentContext();
    }

    public static RuntimeContentContext Require(RuntimeContentEnvironment environment)
    {
        return new RuntimeContentContext(environment);
    }

    public bool TryGetCardCatalog(out CardCatalog catalog)
    {
        EnsureNotDisposed();
        if (_cardCatalog is not null)
        {
            catalog = _cardCatalog;
            return true;
        }

        foreach (var candidate in _roots.DataCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;

            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(candidate);
            }
            catch (Exception exception) when (IsExpectedContextFailure(exception))
            {
                continue;
            }

            if (!RuntimeDataRootPolicy.IsUsableRoot(fullRoot, _roots.RequireGeneratedMarker))
                continue;

            try
            {
                var loaded = CardCatalog.LoadDirectory(
                    Path.Combine(fullRoot, "cards"));
                // Assign only after the complete catalog has loaded. A failed
                // call leaves both cache fields untouched and retryable.
                _cardCatalog = loaded;
                _dataRoot = fullRoot;
                catalog = loaded;
                return true;
            }
            catch (Exception exception) when (IsExpectedContextFailure(exception))
            {
                // A malformed higher-priority Editor candidate must not poison
                // the context or prevent a later fallback candidate.
            }
        }

        catalog = null!;
        return false;
    }

    public CardCatalog RequireCardCatalog()
    {
        if (TryGetCardCatalog(out var catalog)) return catalog;
        throw new InvalidDataException(
            "RuntimeContentContext could not load a CardCatalog from the configured data roots.");
    }

    public bool TryGetDataRoot(out string dataRoot)
    {
        dataRoot = string.Empty;
        if (!TryGetCardCatalog(out _)) return false;
        dataRoot = _dataRoot;
        return !string.IsNullOrWhiteSpace(dataRoot);
    }

    public string RequireDataRoot()
    {
        if (TryGetDataRoot(out var dataRoot)) return dataRoot;
        throw new InvalidDataException(
            "RuntimeContentContext could not resolve a usable data root.");
    }

    public bool TryGetContentResolver(out RuntimeContentResolver resolver)
    {
        EnsureNotDisposed();
        if (_contentResolver is not null)
        {
            resolver = _contentResolver;
            return true;
        }

        resolver = null!;
        if (string.IsNullOrWhiteSpace(_roots.ContentRoot)) return false;

        try
        {
            if (!RuntimeContentResolver.TryLoad(
                _roots.ContentRoot,
                out var loaded,
                Environment.Production))
            {
                // RuntimeContentResolver.TryLoad returns an unavailable
                // diagnostic instance on failure. Do not cache that failure.
                return false;
            }

            _contentResolver = loaded;
            resolver = loaded;
            return true;
        }
        catch (Exception exception) when (IsExpectedContextFailure(exception))
        {
            return false;
        }
    }

    public RuntimeContentResolver RequireContentResolver()
    {
        if (TryGetContentResolver(out var resolver)) return resolver;
        throw new InvalidDataException(
            "RuntimeContentContext could not load the content manifest from " + ContentRoot + ".");
    }

    /// <summary>
    /// Returns a non-owning resolver reference for a presentation consumer.
    /// The lease never clears the shared texture cache when disposed.
    /// </summary>
    public bool TryBorrowContentResolver(out RuntimeContentResolverLease lease)
    {
        lease = null!;
        if (!TryGetContentResolver(out var resolver)) return false;
        lease = new RuntimeContentResolverLease(resolver);
        return true;
    }

    public RuntimeContentResolverLease BorrowContentResolver()
    {
        return new RuntimeContentResolverLease(RequireContentResolver());
    }

    public bool TryBorrowResolver(out RuntimeContentResolverLease lease)
    {
        return TryBorrowContentResolver(out lease);
    }

    public RuntimeContentResolverLease BorrowResolver()
    {
        return BorrowContentResolver();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var resolver = _contentResolver;
        _contentResolver = null;
        _cardCatalog = null;
        _dataRoot = string.Empty;
        resolver?.ClearTextureCache();
    }

    private void EnsureNotDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RuntimeContentContext));
    }

    private static bool IsExpectedContextFailure(Exception exception)
    {
        return exception is ArgumentException ||
            exception is DirectoryNotFoundException ||
            exception is FileNotFoundException ||
            exception is InvalidDataException ||
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is NotSupportedException;
    }
}
}
