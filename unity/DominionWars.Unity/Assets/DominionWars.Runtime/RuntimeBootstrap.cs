using System;
using System.IO;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Data;
using DominionWars.Engine.Setup;
using UnityEngine;

namespace DominionWars.Unity.Runtime
{

/// <summary>
/// Non-visual composition root for the first local match slice. It loads the
/// existing data files, creates the engine gateway, and exposes RuntimeAdapter
/// to future presentation code; it does not implement legality or rules.
/// </summary>
public sealed class RuntimeBootstrap : MonoBehaviour
{
    public const string ReadyLogMessage = "Dominion Wars runtime bootstrap ready.";

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

    public RuntimeAdapter Adapter { get; private set; }

    private void Awake()
    {
        StartSession();
    }

    public void StartSession()
    {
        if (Adapter is not null)
        {
            return;
        }

        var root = ResolveDataRoot();
        var catalog = CardCatalog.LoadDirectory(Path.Combine(root, "cards"));
        var decks = DeckLoader.LoadDirectory(Path.Combine(root, "decks"));
        if (player0DeckIndex < 0 || player0DeckIndex >= decks.Count ||
            player1DeckIndex < 0 || player1DeckIndex >= decks.Count ||
            player0DeckIndex == player1DeckIndex)
        {
            throw new InvalidDataException("RuntimeBootstrap requires two distinct prebuilt deck indexes.");
        }

        var gateway = MatchFactory.CreateInitializedFromDecks(
            matchId,
            catalog,
            decks[player0DeckIndex],
            decks[player1DeckIndex],
            new MatchSetupOptions
            {
                Seed = unchecked((ulong)(uint)seed),
                FirstPlayerIndex = firstPlayerIndex,
                OpeningHandSize = openingHandSize,
                PlayerLife = playerLife,
                CastleEnabled = castleEnabled,
                CastleHealth = castleHealth,
            });

        var session = new RuntimeGatewaySession(gateway);
        var initialization = gateway.GetInitialization(0);
        if (!initialization.Accepted)
            throw new InvalidOperationException(initialization.ReasonKey);

        var adapter = new RuntimeAdapter(session);
        adapter.AcceptSnapshot(initialization.Snapshot);
        adapter.AcceptEventDelta(initialization.Events, initialization.Snapshot);
        Adapter = adapter;
        Debug.Log(ReadyLogMessage);
    }

    private string ResolveDataRoot()
    {
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        var repositoryRoot = projectRoot is null
            ? null
            : Directory.GetParent(projectRoot)?.Parent?.FullName;
        var candidates = string.IsNullOrWhiteSpace(dataRoot)
            ? BuildDefaultDataCandidates(projectRoot, repositoryRoot)
            : new[] { dataRoot };

        foreach (var candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var fullPath = Path.GetFullPath(candidate);
            if (Directory.Exists(Path.Combine(fullPath, "cards")) &&
                Directory.Exists(Path.Combine(fullPath, "decks")))
            {
                return fullPath;
            }
        }

        throw new DirectoryNotFoundException(
            "RuntimeBootstrap could not find a data root containing cards and decks. " +
            "Set dataRoot to that directory.");
    }

    private static string[] BuildDefaultDataCandidates(string projectRoot, string repositoryRoot)
    {
        var streamingData = Path.Combine(Application.streamingAssetsPath, "data");
        var workingDirectoryData = Path.Combine(Directory.GetCurrentDirectory(), "data");
        var projectData = projectRoot is null ? string.Empty : Path.Combine(projectRoot, "data");
        var repositoryData = repositoryRoot is null ? string.Empty : Path.Combine(repositoryRoot, "data");

        // Player builds contain the authoritative staged copy under
        // StreamingAssets. Keep editor fallbacks for local development, but
        // never let a player's working directory shadow its packaged data.
        return Application.isEditor
            ? new[] { workingDirectoryData, projectData, repositoryData, streamingData }
            : new[] { streamingData, workingDirectoryData, projectData, repositoryData };
    }

    private void OnDestroy()
    {
        Adapter?.Dispose();
        Adapter = null;
    }
}
}
