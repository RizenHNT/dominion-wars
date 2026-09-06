using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Data;
using DominionWars.Engine.Setup;
using UnityEngine;

namespace DominionWars.Unity.Runtime
{

/// <summary>
/// Immutable deck metadata for the match-setup shell. The ID is a technical
/// identifier derived from the canonical deck filename; card contents and all
/// deck legality remain owned by the engine/data loaders.
/// </summary>
public sealed class RuntimeDeckOption
{
    public RuntimeDeckOption(
        string id,
        string displayName,
        string faction,
        string leader,
        int index)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A deck id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A deck display name is required.", nameof(displayName));
        Id = id;
        DisplayName = displayName;
        Faction = faction ?? string.Empty;
        Leader = leader ?? string.Empty;
        Index = index;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Faction { get; }
    public string Leader { get; }
    public int Index { get; }
}

/// <summary>
/// Non-visual composition root for the first local match slice. It loads the
/// existing data files, creates the engine gateway, and exposes RuntimeAdapter
/// to future presentation code; it does not implement legality or rules.
/// </summary>
public sealed class RuntimeBootstrap : MonoBehaviour
{
    public const string ReadyLogMessage = "Dominion Wars runtime bootstrap ready.";
    public const bool DefaultUseSharedContentContext = true;

    [SerializeField] private string matchId = "match_local";
    [SerializeField] private string dataRoot = string.Empty;
    [SerializeField] private int player0DeckIndex;
    [SerializeField] private int player1DeckIndex = 1;
    [SerializeField] private int firstPlayerIndex;
    [SerializeField] private int openingHandSize = 5;
    [SerializeField] private int playerLife = 20;
    [SerializeField] private bool castleEnabled = true;
    [SerializeField] private int castleHealth = 75;
    [SerializeField] private int seed = 1;

    // The shared context is the normal path for newly authored/runtime hosts.
    // Setting this serialized switch to false is the deliberate compatibility
    // escape hatch for an older host that still needs the legacy resolution
    // path. Keep the switch serialized so a scene can pin that choice.
    [Header("Content resolution rollout")]
    [SerializeField] private bool useSharedContentContext = DefaultUseSharedContentContext;

    private IReadOnlyList<DeckEntry> _deckEntries;
    private IReadOnlyList<RuntimeDeckOption> _deckOptions;
    private RuntimeContentContext _contentContext;

    public RuntimeAdapter Adapter { get; private set; }
    public bool UseSharedContentContext => useSharedContentContext;

    /// <summary>
    /// The bootstrap owns this context for the lifetime of the host. A panel
    /// may borrow its resolver, but must never dispose the context or clear
    /// its texture cache.
    /// </summary>
    public RuntimeContentContext SharedContentContext =>
        useSharedContentContext ? EnsureSharedContentContext() : null;

    // Compatibility vocabulary for hosts that call the seam simply
    // ContentContext. Normal hosts now receive the shared context here; an
    // explicitly serialized false keeps this property null for legacy hosts.
    public RuntimeContentContext ContentContext => SharedContentContext;
    public IReadOnlyList<RuntimeDeckOption> DeckOptions
    {
        get
        {
            EnsureDeckCatalog();
            return _deckOptions;
        }
    }

    public int Player0DeckIndex => player0DeckIndex;
    public int Player1DeckIndex => player1DeckIndex;
    public string Player0DeckId => DeckIdAt(player0DeckIndex);
    public string Player1DeckId => DeckIdAt(player1DeckIndex);

    /// <summary>
    /// Unity calls Reset when a component is first added in the Editor. Keep
    /// this explicit alongside the field initializer so a newly authored
    /// bootstrap cannot inherit an old serialized default from an inspector
    /// template. Existing scenes retain their serialized true/false choice.
    /// </summary>
    private void Reset()
    {
        useSharedContentContext = DefaultUseSharedContentContext;
    }

    private void Awake()
    {
        // RuntimeBootstrap is also used by the legacy scene-only smoke path.
        // When the presentation flow is present, let its explicit StartMatch
        // intent own session creation instead of opening a hidden session in
        // Awake. The runtime assembly cannot reference the UI assembly, so
        // this boundary uses the stable component type name.
        if (HasRuntimeScreenFlow()) return;
        StartSession();
    }

    public void StartSession()
    {
        if (Adapter is not null)
        {
            return;
        }

        if (!TryStartSession(player0DeckIndex, player1DeckIndex, out var reasonKey))
            throw new InvalidOperationException(reasonKey);
    }

    /// <summary>
    /// Starts a fresh data-backed match using the selected canonical deck IDs.
    /// A candidate adapter is fully initialized before the current session is
    /// replaced, so a failed selection or load leaves the current adapter
    /// untouched.
    /// </summary>
    public bool TryStartSession(
        string player0DeckId,
        string player1DeckId,
        out string reasonKey)
    {
        reasonKey = "match.deck_selection_invalid";
        if (string.IsNullOrWhiteSpace(player0DeckId) || string.IsNullOrWhiteSpace(player1DeckId))
            return false;

        try
        {
            EnsureDeckCatalog();
            var player0 = FindDeckEntry(player0DeckId);
            var player1 = FindDeckEntry(player1DeckId);
            if (player0 == null || player1 == null)
                return false;
            return TryStartSession(player0.Option.Index, player1.Option.Index, out reasonKey);
        }
        catch (Exception exception)
        {
            reasonKey = SafeStartFailureReason(exception);
            return false;
        }
    }

    /// <summary>
    /// Index-based composition entry point used by the default bootstrap
    /// selection and by the ID-based setup orchestrator. It does not mutate
    /// serialized selection fields until the candidate session is ready.
    /// </summary>
    public bool TryStartSession(
        int player0Index,
        int player1Index,
        out string reasonKey)
    {
        reasonKey = "match.start_failed";
        RuntimeAdapter candidate = null;
        try
        {
            EnsureDeckCatalog();
            if (player0Index < 0 || player0Index >= _deckEntries.Count ||
                player1Index < 0 || player1Index >= _deckEntries.Count ||
                player0Index == player1Index)
            {
                reasonKey = "match.deck_selection_invalid";
                return false;
            }

            var root = ResolveDataRoot();
            var catalog = LoadCardCatalog(root);
            var gateway = MatchFactory.CreateInitializedFromDecks(
                matchId,
                catalog,
                _deckEntries[player0Index].Definition,
                _deckEntries[player1Index].Definition,
                new MatchSetupOptions
                {
                    Seed = unchecked((ulong)(uint)seed),
                    FirstPlayerIndex = firstPlayerIndex,
                    OpeningHandSize = openingHandSize,
                    PlayerLife = playerLife,
                    CastleEnabled = castleEnabled,
                    CastleHealth = castleHealth,
                });

            var initialization = gateway.GetInitialization(0);
            if (!initialization.Accepted)
            {
                reasonKey = "match.start_failed";
                return false;
            }

            candidate = new RuntimeAdapter(new RuntimeGatewaySession(gateway));
            candidate.AcceptSnapshot(initialization.Snapshot);
            candidate.AcceptEventDelta(initialization.Events, initialization.Snapshot);

            if (candidate.Presentation.Snapshot == null)
            {
                reasonKey = "match.snapshot_unavailable";
                return false;
            }

            // Commit only after every candidate boundary has succeeded.
            var previous = Adapter;
            Adapter = candidate;
            candidate = null;
            player0DeckIndex = player0Index;
            player1DeckIndex = player1Index;
            previous?.Dispose();
            reasonKey = "match.started";
            Debug.Log(ReadyLogMessage);
            return true;
        }
        catch (Exception exception)
        {
            reasonKey = SafeStartFailureReason(exception);
            return false;
        }
        finally
        {
            candidate?.Dispose();
        }
    }

    /// <summary>
    /// Safely detaches the current runtime adapter. No new session is inferred
    /// or created; a future setup flow must explicitly start one again.
    /// </summary>
    public void StopSession()
    {
        var previous = Adapter;
        Adapter = null;
        previous?.Dispose();
    }

    private void EnsureDeckCatalog()
    {
        if (_deckEntries is not null) return;

        var root = ResolveDataRoot();
        var deckDirectory = Path.Combine(root, "decks");
        var files = Directory.GetFiles(deckDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
            throw new InvalidDataException("No deck JSON files were found.");

        var entries = new List<DeckEntry>(files.Length);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < files.Length; index++)
        {
            var file = files[index];
            var id = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                throw new InvalidDataException("Deck filenames must provide unique stable IDs.");
            var definition = DeckLoader.LoadFile(file);
            entries.Add(new DeckEntry(
                definition,
                new RuntimeDeckOption(id, definition.Name, definition.Faction, definition.Leader, index)));
        }

        _deckEntries = entries.AsReadOnly();
        _deckOptions = entries.Select(entry => entry.Option).ToArray();
    }

    private DeckEntry FindDeckEntry(string id)
    {
        for (var index = 0; index < _deckEntries.Count; index++)
        {
            if (string.Equals(_deckEntries[index].Option.Id, id, StringComparison.OrdinalIgnoreCase))
                return _deckEntries[index];
        }

        return null;
    }

    private string DeckIdAt(int index)
    {
        try
        {
            EnsureDeckCatalog();
            return index >= 0 && index < _deckEntries.Count
                ? _deckEntries[index].Option.Id
                : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string SafeStartFailureReason(Exception exception)
    {
        if (exception is DirectoryNotFoundException || exception is FileNotFoundException)
            return "match.data_unavailable";
        if (exception is InvalidDataException)
            return "match.data_invalid";
        return "match.start_failed";
    }

    private static bool HasRuntimeScreenFlow()
    {
        const string screenFlowTypeName = "DominionWars.Unity.UI.RuntimeScreenFlow";
        var components = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (var component in components)
        {
            if (component != null &&
                string.Equals(component.GetType().FullName, screenFlowTypeName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private string ResolveDataRoot()
    {
        if (useSharedContentContext)
        {
            var sharedContext = EnsureSharedContentContext();
            if (sharedContext.TryGetDataRoot(out var sharedRoot))
                return sharedRoot;

            throw new DirectoryNotFoundException(
                "RuntimeBootstrap could not find a data root containing cards and decks. " +
                "Set dataRoot to that directory.");
        }

        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        var repositoryRoot = projectRoot is null
            ? null
            : Directory.GetParent(projectRoot)?.Parent?.FullName;

        var candidates = BuildDataCandidates(
            Application.isEditor,
            dataRoot,
            Path.Combine(Application.streamingAssetsPath, "data"),
            Path.Combine(Directory.GetCurrentDirectory(), "data"),
            projectRoot is null ? string.Empty : Path.Combine(projectRoot, "data"),
            repositoryRoot is null ? string.Empty : Path.Combine(repositoryRoot, "data"));
        var resolved = RuntimeDataRootPolicy.FindUsableRoot(
            candidates,
            requireGeneratedMarker: !Application.isEditor);
        if (resolved is not null)
            return resolved;

        throw new DirectoryNotFoundException(
            "RuntimeBootstrap could not find a data root containing cards and decks. " +
            "Set dataRoot to that directory.");
    }

    private CardCatalog LoadCardCatalog(string root)
    {
        if (!useSharedContentContext)
            return CardCatalog.LoadDirectory(Path.Combine(root, "cards"));

        var sharedContext = EnsureSharedContentContext();
        if (sharedContext.TryGetCardCatalog(out var catalog))
            return catalog;

        // Keep the existing start-session failure mapping. The context is
        // retryable; no failed load is cached and no new public error format
        // is exposed by the rollout.
        throw new InvalidDataException(
            "RuntimeBootstrap could not load the card catalog from the shared content context.");
    }

    private RuntimeContentContext EnsureSharedContentContext()
    {
        if (_contentContext != null && !_contentContext.IsDisposed)
            return _contentContext;

        _contentContext = new RuntimeContentContext(
            RuntimeContentEnvironment.FromUnity(dataRoot));
        return _contentContext;
    }

    internal static string[] BuildDataCandidates(
        bool isEditor,
        string explicitRoot,
        string streamingData,
        string cwdData,
        string projectData,
        string repoData)
    {
        if (!isEditor)
        {
            return NonEmptyDistinct(streamingData);
        }

        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return NonEmptyDistinct(explicitRoot);
        }

        return NonEmptyDistinct(cwdData, projectData, repoData, streamingData);
    }

    private static string[] NonEmptyDistinct(params string[] values)
    {
        return values
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void OnDestroy()
    {
        StopSession();
        var contentContext = _contentContext;
        _contentContext = null;
        contentContext?.Dispose();
    }

    private sealed class DeckEntry
    {
        public DeckEntry(DeckDefinition definition, RuntimeDeckOption option)
        {
            Definition = definition;
            Option = option;
        }

        public DeckDefinition Definition { get; }
        public RuntimeDeckOption Option { get; }
    }
}
}
