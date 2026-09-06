using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DominionWars.Unity.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeBootstrapEditModeTests
{
    [Test]
    public void BootstrapCreatesSnapshotFromPrebuiltData()
    {
        var gameObject = new GameObject("runtime-bootstrap-test");
        try
        {
            var bootstrap = gameObject.AddComponent<RuntimeBootstrap>();
            bootstrap.StartSession();
            Assert.That(bootstrap.Adapter, Is.Not.Null);
            Assert.That(bootstrap.Adapter!.Presentation.Snapshot, Is.Not.Null);
            Assert.That(bootstrap.Adapter.Presentation.Snapshot!.Phase, Is.EqualTo("AMBUSH"));
            Assert.That(bootstrap.Adapter.Presentation.Snapshot.SnapshotRevision, Is.EqualTo(1));
            Assert.That(bootstrap.Adapter.Presentation.Snapshot.Castle.Enabled, Is.True);
            Assert.That(bootstrap.Adapter.Presentation.EventDelta, Is.Not.Empty);
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void BootstrapExposesCanonicalDeckOptionsAndCurrentDefaults()
    {
        var gameObject = new GameObject("runtime-bootstrap-deck-options-test");
        try
        {
            var bootstrap = gameObject.AddComponent<RuntimeBootstrap>();
            bootstrap.StartSession();
            Assert.That(bootstrap.DeckOptions.Count, Is.EqualTo(4));
            Assert.That(bootstrap.DeckOptions.Select(option => option.Id), Is.EqualTo(new[]
            {
                "flame_deck",
                "machine_deck",
                "sea_deck",
                "wood_deck",
            }));
            Assert.That(bootstrap.DeckOptions.Select(option => option.DisplayName), Is.EqualTo(new[]
            {
                "烈焰帝国·焚天速攻",
                "机械遗迹·极神协议",
                "深海联盟·吞噬之渊",
                "古木圣地·常青壁垒",
            }));
            Assert.That(bootstrap.Player0DeckId, Is.EqualTo("flame_deck"));
            Assert.That(bootstrap.Player1DeckId, Is.EqualTo("machine_deck"));
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void TryStartSessionAtomicallyReplacesReadyAdapterAndPreservesItOnFailure()
    {
        var gameObject = new GameObject("runtime-bootstrap-session-replace-test");
        try
        {
            var bootstrap = gameObject.AddComponent<RuntimeBootstrap>();
            bootstrap.StartSession();
            var previous = bootstrap.Adapter;
            var previousSnapshot = previous!.Presentation.Snapshot;

            Assert.That(
                bootstrap.TryStartSession("sea_deck", "wood_deck", out var startedReason),
                Is.True);
            Assert.That(startedReason, Is.EqualTo("match.started"));
            Assert.That(bootstrap.Adapter, Is.Not.SameAs(previous));
            Assert.That(bootstrap.Adapter!.Presentation.Snapshot, Is.Not.Null);
            Assert.That(bootstrap.Player0DeckId, Is.EqualTo("sea_deck"));
            Assert.That(bootstrap.Player1DeckId, Is.EqualTo("wood_deck"));

            var current = bootstrap.Adapter;
            var currentSnapshot = current.Presentation.Snapshot;
            Assert.That(
                bootstrap.TryStartSession("sea_deck", "sea_deck", out var failedReason),
                Is.False);
            Assert.That(failedReason, Is.EqualTo("match.deck_selection_invalid"));
            Assert.That(bootstrap.Adapter, Is.SameAs(current));
            Assert.That(bootstrap.Adapter!.Presentation.Snapshot, Is.SameAs(currentSnapshot));
            Assert.That(previousSnapshot, Is.Not.Null);
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void StopSessionClearsAndClosesCurrentAdapter()
    {
        var gameObject = new GameObject("runtime-bootstrap-stop-session-test");
        try
        {
            var bootstrap = gameObject.AddComponent<RuntimeBootstrap>();
            bootstrap.StartSession();
            var adapter = bootstrap.Adapter;
            Assert.That(adapter, Is.Not.Null);

            bootstrap.StopSession();

            Assert.That(bootstrap.Adapter, Is.Null);
            Assert.That(() => adapter!.RefreshSnapshot(0),
                Throws.TypeOf<System.ObjectDisposedException>());
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void MatchSetupOrchestratorTreatsBootstrapSuccessAsReadyContract()
    {
        var gameObject = new GameObject("runtime-match-setup-orchestrator-test");
        try
        {
            var bootstrap = gameObject.AddComponent<RuntimeBootstrap>();
            var orchestrator = new RuntimeMatchSetupOrchestrator(bootstrap);

            Assert.That(
                orchestrator.TryStartMatch("sea_deck", "wood_deck", out var reasonKey),
                Is.True);
            Assert.That(reasonKey, Is.EqualTo("match.started"));
            Assert.That(bootstrap.Adapter, Is.Not.Null);
            Assert.That(bootstrap.Adapter!.Presentation.Snapshot, Is.Not.Null);
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void MatchSetupOrchestratorPreservesExistingAdapterWhenStartFails()
    {
        var gameObject = new GameObject("runtime-match-setup-orchestrator-preserve-test");
        try
        {
            var bootstrap = gameObject.AddComponent<RuntimeBootstrap>();
            bootstrap.StartSession();
            var previous = bootstrap.Adapter;
            var previousSnapshot = previous!.Presentation.Snapshot;
            var orchestrator = new RuntimeMatchSetupOrchestrator(bootstrap);

            Assert.That(
                orchestrator.TryStartMatch("missing_deck", "wood_deck", out var reasonKey),
                Is.False);
            Assert.That(reasonKey, Is.EqualTo("match.deck_selection_invalid"));
            Assert.That(bootstrap.Adapter, Is.SameAs(previous));
            Assert.That(bootstrap.Adapter!.Presentation.Snapshot, Is.SameAs(previousSnapshot));
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void ResolveDataCandidatesInPlayerModeOnlyReturnsPackagedStreamingData()
    {
        var candidates = RuntimeDataRootPolicy.ResolveDataCandidates(
            isEditor: false,
            explicitRoot: "explicit-data",
            streaming: "streaming-data",
            cwd: "working-data",
            project: "project-data",
            repo: "repository-data");

        Assert.That(candidates, Is.EqualTo(new[] { "streaming-data" }));
    }

    [Test]
    public void ResolveDataCandidatesInEditorModeKeepsExplicitRootAndDevelopmentFallbacks()
    {
        var candidates = RuntimeDataRootPolicy.ResolveDataCandidates(
            isEditor: true,
            explicitRoot: "explicit-data",
            streaming: "streaming-data",
            cwd: "working-data",
            project: "project-data",
            repo: "repository-data");

        Assert.That(candidates[0], Is.EqualTo("explicit-data"));
        Assert.That(candidates, Does.Contain("working-data"));
        Assert.That(candidates, Does.Contain("project-data"));
        Assert.That(candidates, Does.Contain("repository-data"));
        Assert.That(candidates, Does.Contain("streaming-data"));
    }

    [Test]
    public void PlayerDataRootPolicyUsesOnlyStreamingDataAndRequiresGeneratedMarker()
    {
        var root = CreateTemporaryDataRoot();
        var streamingRoot = Path.Combine(root, "streaming-data");
        var repositoryRoot = Path.Combine(root, "repository-data");
        CreateDataRoot(repositoryRoot);
        try
        {
            var playerCandidates = RuntimeDataRootPolicy.BuildCandidates(
                repositoryRoot,
                streamingRoot,
                repositoryRoot,
                repositoryRoot,
                repositoryRoot,
                isEditor: false);

            Assert.That(playerCandidates, Is.EqualTo(new[] { streamingRoot }));
            Assert.That(
                RuntimeDataRootPolicy.FindUsableRoot(playerCandidates, requireGeneratedMarker: true),
                Is.Null,
                "A Player must not fall back to repository data when packaged StreamingAssets/data is missing.");

            CreateDataRoot(streamingRoot);
            Assert.That(
                RuntimeDataRootPolicy.FindUsableRoot(playerCandidates, requireGeneratedMarker: true),
                Is.Null,
                "Cards and decks without the owned staging marker are not packaged runtime data.");

            File.WriteAllText(
                Path.Combine(streamingRoot, RuntimeDataRootPolicy.GeneratedMarkerName),
                RuntimeDataRootPolicy.GeneratedMarkerContents);
            Assert.That(
                RuntimeDataRootPolicy.FindUsableRoot(playerCandidates, requireGeneratedMarker: true),
                Is.EqualTo(Path.GetFullPath(streamingRoot)));

            Directory.Delete(Path.Combine(streamingRoot, "decks"), recursive: true);
            Assert.That(RuntimeDataRootPolicy.IsUsableRoot(streamingRoot, requireGeneratedMarker: true), Is.False);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void EditorDataRootPolicyRetainsExplicitAndRepositoryFallback()
    {
        var root = CreateTemporaryDataRoot();
        var explicitRoot = Path.Combine(root, "explicit-missing");
        var repositoryRoot = Path.Combine(root, "repository-data");
        CreateDataRoot(repositoryRoot);
        try
        {
            var editorCandidates = RuntimeDataRootPolicy.BuildCandidates(
                explicitRoot,
                Path.Combine(root, "streaming-data"),
                Path.Combine(root, "working-data"),
                Path.Combine(root, "project-data"),
                repositoryRoot,
                isEditor: true);

            Assert.That(editorCandidates[0], Is.EqualTo(explicitRoot));
            Assert.That(editorCandidates, Does.Contain(repositoryRoot));
            Assert.That(
                RuntimeDataRootPolicy.FindUsableRoot(editorCandidates, requireGeneratedMarker: false),
                Is.EqualTo(Path.GetFullPath(repositoryRoot)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void BootstrapPlayerCandidatesNeverIncludeExplicitOrRepositoryFallbacks()
    {
        var candidates = BuildDataCandidates(
            isEditor: false,
            explicitRoot: "explicit-data",
            streamingData: "missing-packaged-data",
            cwdData: "working-data",
            projectData: "project-data",
            repoData: "repository-data");

        Assert.That(candidates, Is.EqualTo(new[] { "missing-packaged-data" }));
    }

    [Test]
    public void BootstrapEditorExplicitRootIsExclusiveWhenProvided()
    {
        var candidates = BuildDataCandidates(
            isEditor: true,
            explicitRoot: "explicit-data",
            streamingData: "streaming-data",
            cwdData: "working-data",
            projectData: "project-data",
            repoData: "repository-data");

        Assert.That(candidates, Is.EqualTo(new[] { "explicit-data" }));
    }

    private static string[] BuildDataCandidates(
        bool isEditor,
        string explicitRoot,
        string streamingData,
        string cwdData,
        string projectData,
        string repoData)
    {
        var method = typeof(RuntimeBootstrap).GetMethod(
            "BuildDataCandidates",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "RuntimeBootstrap candidate selector is missing.");
        return (string[])method!.Invoke(
            null,
            new object[] { isEditor, explicitRoot, streamingData, cwdData, projectData, repoData })!;
    }

    private static string CreateTemporaryDataRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw-runtime-data-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CreateDataRoot(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "cards"));
        Directory.CreateDirectory(Path.Combine(root, "decks"));
        File.WriteAllText(Path.Combine(root, "cards", "card.json"), "{}");
        File.WriteAllText(Path.Combine(root, "decks", "deck.json"), "{}");
    }
}
}
