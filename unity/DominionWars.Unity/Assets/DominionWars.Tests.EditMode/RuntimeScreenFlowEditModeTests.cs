#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using DominionWars.Adapters;
using DominionWars.Unity.Runtime;
using NUnit.Framework;
using UnityEngine;
using DominionWars.Unity.UI;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeScreenFlowEditModeTests
{
    private readonly List<GameObject> _createdObjects = new();

    [SetUp]
    public void SetUp()
    {
        DestroyGeneratedRuntimePanels();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var createdObject in _createdObjects)
        {
            if (createdObject != null) UnityEngine.Object.DestroyImmediate(createdObject);
        }

        _createdObjects.Clear();
        DestroyGeneratedRuntimePanels();
    }

    [Test]
    public void AllScreenRootsExistAndOnlyTheSelectedShellRootIsVisible()
    {
        var flow = CreateFlow();
        flow.Initialize();

        foreach (RuntimeScreenId screen in Enum.GetValues(typeof(RuntimeScreenId)))
        {
            Assert.That(flow.View.GetScreenRoot(screen), Is.Not.Null, screen + " root is missing.");
        }

        flow.Navigate(RuntimeScreenId.MainMenu);
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MainMenu));
        Assert.That(flow.View.CurrentShellRoot, Is.SameAs(flow.View.MainMenuRoot));
        Assert.That(flow.View.VisibleShellRootCount, Is.EqualTo(1));

        flow.Navigate(RuntimeScreenId.MatchSetup);
        Assert.That(flow.View.CurrentShellRoot, Is.SameAs(flow.View.MatchSetupRoot));
        Assert.That(flow.View.VisibleShellRootCount, Is.EqualTo(1));

        flow.Navigate(RuntimeScreenId.Battle);
        Assert.That(flow.View.CurrentShellRoot, Is.SameAs(flow.View.BattleRoot));
        Assert.That(flow.View.VisibleShellRootCount, Is.EqualTo(1));
    }

    [Test]
    public void ButtonsRaiseStableIntentsWithoutCreatingOrResettingAnEngineSession()
    {
        var flow = CreateFlow();
        flow.Initialize();
        var intents = new List<RuntimeScreenIntent>();
        flow.IntentRaised += intents.Add;

        flow.Navigate(RuntimeScreenId.MatchSetup);
        flow.View.MatchSetupStartButton.onClick.Invoke();
        Assert.That(intents, Does.Contain(RuntimeScreenIntent.StartMatch));
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MatchSetup));
        Assert.That(flow.View.MatchSetupErrorText.text, Is.Not.Empty);

        flow.View.MatchSetupBackButton.onClick.Invoke();
        Assert.That(intents, Does.Contain(RuntimeScreenIntent.Back));
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MainMenu));

        flow.Navigate(RuntimeScreenId.Result);
        flow.View.ResultRestartButton.onClick.Invoke();
        Assert.That(intents, Does.Contain(RuntimeScreenIntent.Restart));
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MatchSetup));
        Assert.That(flow.View.MatchSetupErrorText.text,
            Is.EqualTo("Match setup is unavailable."));

        flow.View.ResultReturnToMenuButton.onClick.Invoke();
        Assert.That(intents, Does.Contain(RuntimeScreenIntent.ReturnToMenu));
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MainMenu));

        Assert.That(flow.Bootstrap, Is.Null,
            "The screen flow must not create or reset a RuntimeBootstrap session.");
    }

    [Test]
    public void ResultIsOnlyReachedByAnExplicitFlowInput()
    {
        var flow = CreateFlow();
        flow.Initialize();
        flow.Navigate(RuntimeScreenId.Battle);
        flow.Refresh();

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.False);

        flow.ShowResult();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Result));
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.True);
        Assert.That(flow.View.VisibleShellRootCount, Is.EqualTo(1));
    }

    [Test]
    public void AuthoritativeOverSnapshotEntersResultWithoutLeakingOutcomeWireFields()
    {
        var bootstrapObject = new GameObject("RuntimeScreenFlowResultBootstrap");
        _createdObjects.Add(bootstrapObject);
        var bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();
        bootstrap.StartSession();
        var flow = CreateFlow();
        flow.Initialize();
        flow.ShowBattle();
        flow.Refresh();

        var current = bootstrap.Adapter!.Presentation.Snapshot!;
        bootstrap.Adapter.AcceptSnapshot(
            SnapshotWithOutcome(current, "OVER", 1, "victory.castle"));
        flow.Refresh();

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Result));
        Assert.That(flow.View.ResultWinnerText.text, Is.EqualTo("PLAYER 2 WINS"));
        Assert.That(flow.View.ResultReasonText.text, Is.EqualTo("MATCH COMPLETE"));
        Assert.That(flow.View.ResultReasonText.text, Does.Not.Contain("victory."));
        Assert.That(flow.View.DebugResultOutcome, Is.EqualTo("1 | victory.castle"));
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.True);
    }

    [Test]
    public void ResultRestartCreatesFreshSessionAndClearsTerminalPresentation()
    {
        var bootstrapObject = new GameObject("RuntimeScreenFlowRestartBootstrap");
        _createdObjects.Add(bootstrapObject);
        var bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();
        bootstrap.StartSession();

        var panelObject = new GameObject(
            "RuntimeScreenFlowRestartPanel",
            typeof(RectTransform));
        _createdObjects.Add(panelObject);
        var panel = panelObject.AddComponent<RuntimeBattlePanel>();
        panel.Bind(bootstrap);

        var flow = CreateFlow();
        flow.Initialize();
        flow.ShowBattle();
        flow.Refresh();

        var previousAdapter = bootstrap.Adapter;
        var previousSnapshot = previousAdapter!.Presentation.Snapshot!;
        previousAdapter.AcceptSnapshot(
            SnapshotWithOutcome(previousSnapshot, "OVER", 1, "victory.castle"));
        flow.Refresh();

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Result));
        Assert.That(flow.View.ResultReasonText.text, Is.EqualTo("MATCH COMPLETE"));
        Assert.That(flow.View.DebugResultOutcome, Is.EqualTo("1 | victory.castle"));

        flow.View.ResultRestartButton.onClick.Invoke();

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(bootstrap.Adapter, Is.Not.Null.And.Not.SameAs(previousAdapter));
        Assert.That(panel.Adapter, Is.SameAs(bootstrap.Adapter));
        Assert.That(bootstrap.Adapter!.Presentation.Snapshot, Is.Not.Null);
        Assert.That(bootstrap.Adapter.Presentation.Snapshot!.Phase, Is.EqualTo("AMBUSH"));
        Assert.That(bootstrap.Adapter.Presentation.Snapshot.SnapshotRevision, Is.EqualTo(1));
        Assert.That(bootstrap.Adapter.Presentation.Snapshot.WinnerPlayerIndex, Is.Null);
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.False);
        Assert.That(flow.View.ResultWinnerText.text, Is.Empty);
        Assert.That(flow.View.ResultReasonText.text, Is.Empty);
        Assert.That(panel.gameObject.activeSelf, Is.True);
        Assert.That(
            () => previousAdapter.RefreshSnapshot(0),
            Throws.TypeOf<ObjectDisposedException>(),
            "The terminal adapter must not remain usable after a restart.");

        // A follow-up refresh must stay in the new battle instead of replaying
        // the old terminal outcome through the observation cursor.
        flow.Refresh();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.False);

        flow.RequestReturnToMenu();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MainMenu));
    }

    [Test]
    public void ResultRestartFailureReturnsToSetupWithSafeRecoveryMessage()
    {
        var bootstrapObject = new GameObject("RuntimeScreenFlowRestartFailureBootstrap");
        _createdObjects.Add(bootstrapObject);
        var bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();
        bootstrap.StartSession();

        var flow = CreateFlow();
        flow.Initialize();
        flow.ShowResult();

        // An explicit terminal flow can still be opened without a live host;
        // destroy the host to model the unavailable-session failure path.
        UnityEngine.Object.DestroyImmediate(bootstrapObject);
        flow.Refresh();
        flow.View.ResultRestartButton.onClick.Invoke();

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MatchSetup));
        Assert.That(flow.View.MatchSetupErrorText.text,
            Is.EqualTo("Match setup is unavailable."));
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void NonTerminalOrIncompleteOutcomeStaysInBattleFailClosed()
    {
        var bootstrapObject = new GameObject("RuntimeScreenFlowIncompleteResultBootstrap");
        _createdObjects.Add(bootstrapObject);
        var bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();
        bootstrap.StartSession();
        var flow = CreateFlow();
        flow.Initialize();
        flow.ShowBattle();
        flow.Refresh();

        var current = bootstrap.Adapter!.Presentation.Snapshot!;
        bootstrap.Adapter.AcceptSnapshot(
            SnapshotWithOutcome(current, "GAME_OVER", null, null));
        flow.Refresh();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.False);

        current = bootstrap.Adapter.Presentation.Snapshot!;
        Assert.That(
            () => bootstrap.Adapter.AcceptSnapshot(
                SnapshotWithOutcome(current, "OVER", null, "victory.castle")),
            Throws.ArgumentException);
        flow.Refresh();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.False);

        current = bootstrap.Adapter.Presentation.Snapshot!;
        Assert.That(
            () => bootstrap.Adapter.AcceptSnapshot(
                SnapshotWithOutcome(current, "OVER", 1, null)),
            Throws.ArgumentException);
        flow.Refresh();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void MatchSetupListsCanonicalDecksAndStartsTheRealBootstrapSession()
    {
        var bootstrapObject = new GameObject("RuntimeScreenFlowBootstrap");
        _createdObjects.Add(bootstrapObject);
        var bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();
        bootstrap.StartSession();
        var previousAdapter = bootstrap.Adapter;
        var flow = CreateFlow();
        flow.Initialize();
        flow.Navigate(RuntimeScreenId.MatchSetup);
        flow.Refresh();

        Assert.That(flow.MatchSetupOrchestrator, Is.Not.Null);
        Assert.That(flow.View.MatchSetupPlayer0DeckButtons, Has.Count.EqualTo(4));
        Assert.That(flow.View.MatchSetupPlayer1DeckButtons, Has.Count.EqualTo(4));
        Assert.That(flow.SelectedPlayer0DeckId, Is.EqualTo("flame_deck"));
        Assert.That(flow.SelectedPlayer1DeckId, Is.EqualTo("machine_deck"));
        Assert.That(
            flow.View.MatchSetupPlayer0DeckButtons[0].GetComponentInChildren<UnityEngine.UI.Text>().text,
            Does.Contain("烈焰帝国"));
        Assert.That(
            flow.View.MatchSetupPlayer0DeckButtons[0].GetComponentInChildren<UnityEngine.UI.Text>().text,
            Does.Not.Contain("flame_leader"));
        Assert.That(
            flow.View.MatchSetupPlayer1DeckButtons[0].GetComponentInChildren<UnityEngine.UI.Text>().text,
            Does.Contain("烈焰帝国"));
        Assert.That(
            flow.View.MatchSetupPlayer1DeckButtons[0].GetComponentInChildren<UnityEngine.UI.Text>().text,
            Does.Not.Contain("flame_leader"));

        flow.View.MatchSetupPlayer0DeckButtons[2].onClick.Invoke();
        flow.View.MatchSetupPlayer1DeckButtons[3].onClick.Invoke();
        Assert.That(flow.SelectedPlayer0DeckId, Is.EqualTo("sea_deck"));
        Assert.That(flow.SelectedPlayer1DeckId, Is.EqualTo("wood_deck"));

        flow.View.MatchSetupStartButton.onClick.Invoke();

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(bootstrap.Adapter, Is.Not.Null.And.Not.SameAs(previousAdapter));
        Assert.That(bootstrap.Adapter!.Presentation.Snapshot, Is.Not.Null);
        Assert.That(bootstrap.Player0DeckId, Is.EqualTo("sea_deck"));
        Assert.That(bootstrap.Player1DeckId, Is.EqualTo("wood_deck"));
    }

    [Test]
    public void MatchSetupRendersEightDynamicDeckButtonsWithSizedWorkingListeners()
    {
        var bootstrapObject = new GameObject("RuntimeScreenFlowDeckButtonBootstrap");
        _createdObjects.Add(bootstrapObject);
        var bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();
        bootstrap.StartSession();
        var flow = CreateFlow();
        flow.Initialize();
        flow.Navigate(RuntimeScreenId.MatchSetup);
        flow.Refresh();

        Assert.That(flow.View.MatchSetupPlayer0DeckButtons, Has.Count.EqualTo(4));
        Assert.That(flow.View.MatchSetupPlayer1DeckButtons, Has.Count.EqualTo(4));
        Assert.That(flow.View.InteractiveButtons, Has.Count.EqualTo(17));

        foreach (var button in flow.View.MatchSetupPlayer0DeckButtons)
        {
            Assert.That(button.onClick, Is.Not.Null);
            var rect = button.GetComponent<RectTransform>();
            Assert.That(rect.rect.width, Is.GreaterThanOrEqualTo(66f), button.name + " logical width.");
            Assert.That(rect.rect.height, Is.GreaterThanOrEqualTo(66f), button.name + " logical height.");
        }

        foreach (var button in flow.View.MatchSetupPlayer1DeckButtons)
        {
            Assert.That(button.onClick, Is.Not.Null);
            var rect = button.GetComponent<RectTransform>();
            Assert.That(rect.rect.width, Is.GreaterThanOrEqualTo(66f), button.name + " logical width.");
            Assert.That(rect.rect.height, Is.GreaterThanOrEqualTo(66f), button.name + " logical height.");
        }

        // UnityEvent does not expose runtime listener count. Invoking every
        // generated callback proves that each button retains its own stable ID
        // rather than the final loop value.
        var expectedIds = new[] { "flame_deck", "machine_deck", "sea_deck", "wood_deck" };
        for (var index = 0; index < expectedIds.Length; index++)
        {
            flow.View.MatchSetupPlayer0DeckButtons[index].onClick.Invoke();
            Assert.That(flow.SelectedPlayer0DeckId, Is.EqualTo(expectedIds[index]));
            flow.View.MatchSetupPlayer1DeckButtons[index].onClick.Invoke();
            Assert.That(flow.SelectedPlayer1DeckId, Is.EqualTo(expectedIds[index]));
        }
    }

    [Test]
    public void FailedStartStaysOnSetupWithSafeErrorAndDoesNotReplaceSession()
    {
        var bootstrapObject = new GameObject("RuntimeScreenFlowFailedBootstrap");
        _createdObjects.Add(bootstrapObject);
        var bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();
        bootstrap.StartSession();
        var currentAdapter = bootstrap.Adapter;
        var flow = CreateFlow();
        flow.Initialize();
        flow.Navigate(RuntimeScreenId.MatchSetup);
        flow.Refresh();

        flow.View.MatchSetupPlayer1DeckButtons[0].onClick.Invoke();
        flow.View.MatchSetupStartButton.onClick.Invoke();

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MatchSetup));
        Assert.That(flow.View.MatchSetupErrorText.text, Is.EqualTo("Choose two different available decks."));
        Assert.That(bootstrap.Adapter, Is.SameAs(currentAdapter));
    }

    [Test]
    public void ReturnToMenuStopsAndUnbindsBattlePresentation()
    {
        var bootstrapObject = new GameObject("RuntimeScreenFlowReturnBootstrap");
        _createdObjects.Add(bootstrapObject);
        var bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();
        bootstrap.StartSession();
        var panelObject = new GameObject("RuntimeScreenFlowReturnPanel", typeof(RectTransform));
        _createdObjects.Add(panelObject);
        var panel = panelObject.AddComponent<RuntimeBattlePanel>();
        panel.Bind(bootstrap);
        var flow = CreateFlow();
        flow.Initialize();
        flow.Refresh();
        flow.Navigate(RuntimeScreenId.Battle);
        Assert.That(panel.Adapter, Is.SameAs(bootstrap.Adapter));

        flow.RequestReturnToMenu();

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MainMenu));
        Assert.That(bootstrap.Adapter, Is.Null);
        Assert.That(panel.Adapter, Is.Null);
        Assert.That(panel.gameObject.activeSelf, Is.False);
        Assert.That(flow.View.MainMenuRoot.gameObject.activeSelf, Is.True);
        Assert.That(flow.View.ResultWinnerText.text, Is.Empty);
        Assert.That(flow.View.ResultReasonText.text, Is.Empty);

        flow.Refresh();
        Assert.That(bootstrap.Adapter, Is.Null);
        Assert.That(panel.Adapter, Is.Null);
    }

    [Test]
    public void ReturnToMenuStopsUndiscoveredBootstrapAndDoesNotRebindNextRefresh()
    {
        var bootstrapObject = new GameObject("RuntimeScreenFlowUndiscoveredReturnBootstrap");
        _createdObjects.Add(bootstrapObject);
        var bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();
        bootstrap.StartSession();
        var panelObject = new GameObject("RuntimeScreenFlowUndiscoveredReturnPanel", typeof(RectTransform));
        _createdObjects.Add(panelObject);
        var panel = panelObject.AddComponent<RuntimeBattlePanel>();
        panel.Bind(bootstrap);

        var flow = CreateFlow();
        flow.Initialize();
        // Do not call Refresh: the host/orchestrator and panel are deliberately
        // undiscovered when ReturnToMenu is requested.
        flow.RequestReturnToMenu();

        Assert.That(bootstrap.Adapter, Is.Null);
        Assert.That(panel.Adapter, Is.Null);
        Assert.That(panel.gameObject.activeSelf, Is.False);

        flow.Refresh();
        Assert.That(bootstrap.Adapter, Is.Null);
        Assert.That(panel.Adapter, Is.Null);
        Assert.That(panel.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void RefreshDiscoversAnInactiveProductionPanelWithoutDuplicatingIt()
    {
        var panelObject = new GameObject(
            "DominionWarsRuntimeBattlePanel",
            typeof(RectTransform));
        _createdObjects.Add(panelObject);
        var panel = panelObject.AddComponent<RuntimeBattlePanel>();
        panelObject.SetActive(false);

        var flow = CreateFlow();
        flow.Initialize();
        flow.Refresh();

        Assert.That(flow.BattlePanel, Is.SameAs(panel));
        Assert.That(
            UnityEngine.Object.FindObjectsByType<RuntimeBattlePanel>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None),
            Has.Length.EqualTo(1));
    }

    [Test]
    public void BattleRecoveryButtonReturnsToMenuAndDisablesPanel()
    {
        var panelObject = new GameObject(
            "RuntimeBattleRecoveryPanel",
            typeof(RectTransform));
        _createdObjects.Add(panelObject);
        var panel = panelObject.AddComponent<RuntimeBattlePanel>();

        var flow = CreateFlow();
        flow.Initialize();
        flow.Navigate(RuntimeScreenId.Battle);
        flow.Refresh();
        panel.Refresh();

        Assert.That(flow.BattlePanel, Is.SameAs(panel));
        Assert.That(panel.RecoveryButton, Is.Not.Null);
        Assert.That(panel.RecoveryButton!.interactable, Is.True);
        panel.RecoveryButton.onClick.Invoke();

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MainMenu));
        Assert.That(panelObject.activeSelf, Is.False);
    }

    [Test]
    public void ShellControlsRemainNonZeroAtApprovedViewports()
    {
        var flow = CreateFlow();
        flow.Initialize();
        var root = flow.GetComponent<RectTransform>();
        var canvasScaler = flow.GetComponent<UnityEngine.UI.CanvasScaler>();

        Assert.That(flow.View.InteractiveButtons, Is.Not.Empty,
            "Every shell button must be registered before hit-area checks run.");
        Assert.That(canvasScaler, Is.Not.Null);

        foreach (var size in new[] { new Vector2(1280f, 720f), new Vector2(1440f, 900f) })
        {
            root.sizeDelta = size;
            Canvas.ForceUpdateCanvases();

            var scale = Mathf.Pow(
                size.x / canvasScaler!.referenceResolution.x,
                1f - canvasScaler.matchWidthOrHeight) *
                Mathf.Pow(
                    size.y / canvasScaler.referenceResolution.y,
                    canvasScaler.matchWidthOrHeight);

            foreach (var button in flow.View.InteractiveButtons)
            {
                var rect = button.GetComponent<RectTransform>();
                Assert.That(rect.rect.width, Is.GreaterThanOrEqualTo(66f), button.name + " logical width.");
                Assert.That(rect.rect.height, Is.GreaterThanOrEqualTo(66f), button.name + " logical height.");
                Assert.That(rect.rect.width * scale, Is.GreaterThanOrEqualTo(44f), button.name + " pixel width.");
                Assert.That(rect.rect.height * scale, Is.GreaterThanOrEqualTo(44f), button.name + " pixel height.");
            }
        }
    }

    [Test]
    public void VisualSmokeIsDisabledWithoutAnExplicitOutputPath()
    {
        var enabled = RuntimePlayerVisualSmoke.TryParseCommandLine(
            new[] { "-batchmode", "-nographics" },
            out var options,
            out var failureReason);

        Assert.That(enabled, Is.False);
        Assert.That(options, Is.Null);
        Assert.That(failureReason, Is.Empty);
    }

    [Test]
    public void VisualSmokeAcceptsOnlyNewAbsolutePngPathAndOptionalQuitFlag()
    {
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            "dominion-wars-visual-smoke-" + Guid.NewGuid().ToString("N") + ".png");

        var enabled = RuntimePlayerVisualSmoke.TryParseCommandLine(
            new[]
            {
                RuntimePlayerVisualSmoke.PathArgument,
                outputPath,
                RuntimePlayerVisualSmoke.QuitArgument,
                RuntimePlayerVisualSmoke.BattleArgument,
            },
            out var options,
            out var failureReason);

        Assert.That(enabled, Is.True, failureReason);
        Assert.That(options, Is.Not.Null);
        Assert.That(options!.OutputPath, Is.EqualTo(Path.GetFullPath(outputPath)));
        Assert.That(options.QuitAfterCapture, Is.True);
        Assert.That(options.CaptureBattle, Is.True);
    }

    [Test]
    public void VisualSmokeRejectsRelativeExistingAndNonPngPaths()
    {
        var existingPath = Path.Combine(
            Path.GetTempPath(),
            "dominion-wars-visual-smoke-existing-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllText(existingPath, "fixture");
        try
        {
            AssertVisualSmokePathRejected(new[]
            {
                RuntimePlayerVisualSmoke.PathArgument,
                "relative-output.png",
            });
            if (Path.DirectorySeparatorChar == '\\')
            {
                AssertVisualSmokePathRejected(new[]
                {
                    RuntimePlayerVisualSmoke.PathArgument,
                    "C:drive-relative-output.png",
                });
                AssertVisualSmokePathRejected(new[]
                {
                    RuntimePlayerVisualSmoke.PathArgument,
                    "\\root-relative-output.png",
                });
            }
            AssertVisualSmokePathRejected(new[]
            {
                RuntimePlayerVisualSmoke.PathArgument,
                Path.ChangeExtension(existingPath, ".txt"),
            });
            AssertVisualSmokePathRejected(new[]
            {
                RuntimePlayerVisualSmoke.PathArgument,
                existingPath,
            });
        }
        finally
        {
            File.Delete(existingPath);
        }
    }

    [Test]
    public void VisualSmokeValidatesPngSignatureAndPositiveIhdrDimensions()
    {
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            "dominion-wars-visual-smoke-png-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var png = CreatePngHeader(1280, 720);
            File.WriteAllBytes(outputPath, png);

            Assert.That(
                RuntimePlayerVisualSmoke.TryValidateCapturedPng(
                    outputPath,
                    out var width,
                    out var height,
                    out var failureReason),
                Is.True,
                failureReason);
            Assert.That(width, Is.EqualTo(1280));
            Assert.That(height, Is.EqualTo(720));

            png[0] = 0;
            File.WriteAllBytes(outputPath, png);
            Assert.That(
                RuntimePlayerVisualSmoke.TryValidateCapturedPng(
                    outputPath,
                    out _,
                    out _,
                    out failureReason),
                Is.False);
            Assert.That(failureReason, Does.Contain("PNG signature"));

            File.WriteAllBytes(outputPath, CreatePngHeader(0, 720));
            Assert.That(
                RuntimePlayerVisualSmoke.TryValidateCapturedPng(
                    outputPath,
                    out _,
                    out _,
                    out failureReason),
                Is.False);
            Assert.That(failureReason, Does.Contain("dimensions"));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    private static void AssertVisualSmokePathRejected(IReadOnlyList<string> arguments)
    {
        var enabled = RuntimePlayerVisualSmoke.TryParseCommandLine(
            arguments,
            out var options,
            out var failureReason);

        Assert.That(enabled, Is.False);
        Assert.That(options, Is.Null);
        Assert.That(failureReason, Is.Not.Empty);
    }

    private static byte[] CreatePngHeader(uint width, uint height)
    {
        var header = new byte[33];
        var signature = new byte[]
        {
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
        };
        Array.Copy(signature, header, signature.Length);
        WriteUInt32BigEndian(header, 8, 13);
        header[12] = (byte)'I';
        header[13] = (byte)'H';
        header[14] = (byte)'D';
        header[15] = (byte)'R';
        WriteUInt32BigEndian(header, 16, width);
        WriteUInt32BigEndian(header, 20, height);
        header[24] = 8;
        header[25] = 6;
        return header;
    }

    private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void DestroyGeneratedRuntimePanels()
    {
        foreach (var panel in UnityEngine.Object.FindObjectsByType<RuntimeBattlePanel>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (panel != null && panel.name == "DominionWarsRuntimeBattlePanel")
                UnityEngine.Object.DestroyImmediate(panel.gameObject);
        }
    }

    private RuntimeScreenFlow CreateFlow()
    {
        var gameObject = new GameObject("RuntimeScreenFlowEditMode", typeof(RectTransform));
        _createdObjects.Add(gameObject);
        return gameObject.AddComponent<RuntimeScreenFlow>();
    }

    private static RuntimeSnapshotEnvelope SnapshotWithOutcome(
        RuntimeSnapshotEnvelope source,
        string phase,
        int? winnerPlayerIndex,
        string reasonKey)
    {
        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = source.ContractVersion,
            MatchId = source.MatchId,
            SnapshotRevision = source.SnapshotRevision + 1,
            Turn = source.Turn,
            Phase = phase,
            CurrentPlayer = source.CurrentPlayer,
            ViewerPlayerId = source.ViewerPlayerId,
            Players = source.Players,
            Castle = source.Castle,
            PendingPrompt = source.PendingPrompt,
            LegalActions = source.LegalActions,
            WinnerPlayerIndex = winnerPlayerIndex,
            ReasonKey = reasonKey,
        };
    }
}
}
#endif
