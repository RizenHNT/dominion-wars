#if UNITY_INCLUDE_TESTS
using System.Collections;
using DominionWars.Adapters;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;

namespace DominionWars.Unity.PlayMode
{

[TestFixture]
public sealed class RuntimeScreenFlowPlayModeTests
{
    private const string RuntimeBootstrapScene = "RuntimeBootstrap";

    [UnityTest]
    public IEnumerator BootstrapSceneRoutesToBattleAndReusesTheExistingBattlePanel()
    {
        yield return SceneManager.LoadSceneAsync(RuntimeBootstrapScene, LoadSceneMode.Single);
        yield return null;
        yield return null;

        var flow = Object.FindFirstObjectByType<RuntimeScreenFlow>();
        Assert.That(flow, Is.Not.Null);
        Assert.That(flow!.CurrentScreen, Is.EqualTo(RuntimeScreenId.Title));
        Assert.That(flow.View, Is.Not.Null);
        Assert.That(flow.IsPresentationReady, Is.True,
            "The Player lifecycle must create and activate the TITLE shell before setup navigation.");
        Assert.That(flow.View.TitleRoot.gameObject.activeSelf, Is.True);
        Assert.That(flow.View.VisibleShellRootCount, Is.EqualTo(1));
        var bootstrap = Object.FindFirstObjectByType<RuntimeBootstrap>();
        Assert.That(bootstrap, Is.Not.Null);
        Assert.That(bootstrap!.Adapter, Is.Null,
            "ScreenFlow owns the explicit StartMatch session boundary.");
        var panels = Object.FindObjectsByType<RuntimeBattlePanel>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        Assert.That(panels, Has.Length.EqualTo(1),
            "The production AfterSceneLoad hook must create exactly one battle panel.");
        Assert.That(panels[0].name, Is.EqualTo("DominionWarsRuntimeBattlePanel"));
        foreach (var panel in panels)
        {
            Assert.That(
                panel.name.StartsWith("NON_PRODUCTION_", System.StringComparison.Ordinal),
                Is.False,
                "The production scene flow must not depend on a test-only panel fixture.");
        }

        flow.Navigate(RuntimeScreenId.MainMenu);
        flow.View.MainMenuMatchSetupButton.onClick.Invoke();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MatchSetup));

        flow.Refresh();
        Assert.That(flow.BattlePanel, Is.SameAs(panels[0]));

        flow.View.MatchSetupStartButton.onClick.Invoke();
        yield return null;
        flow.Refresh();

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle),
            "A real runtime start must enter Battle only after its snapshot is ready.");
        Assert.That(flow.BattlePanel, Is.SameAs(panels[0]),
            "ScreenFlow must reuse the production AfterSceneLoad panel.");
        Assert.That(flow.BattlePanel!.gameObject.activeSelf, Is.True);
        Assert.That(flow.View.BattleRoot.gameObject.activeSelf, Is.False,
            "The battle screen must reuse the existing RuntimeBattlePanel root.");

        var runningSnapshot = bootstrap.Adapter!.Presentation.Snapshot!;
        bootstrap.Adapter.AcceptSnapshot(
            SnapshotWithOutcome(runningSnapshot, "OVER", 1, "victory.castle"));
        flow.Refresh();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Result));
        Assert.That(flow.View.ResultWinnerText.text, Is.EqualTo("PLAYER 2 WINS"));
        Assert.That(flow.View.ResultReasonText.text, Is.EqualTo("MATCH COMPLETE"));
        Assert.That(flow.View.ResultReasonText.text, Does.Not.Contain("victory."));
        Assert.That(flow.View.DebugResultOutcome, Is.EqualTo("1 | victory.castle"));
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.True);
        Assert.That(flow.BattlePanel.gameObject.activeSelf, Is.False);

        var previousAdapter = bootstrap.Adapter;
        flow.View.ResultRestartButton.onClick.Invoke();
        yield return null;
        flow.Refresh();

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(bootstrap.Adapter, Is.Not.Null.And.Not.SameAs(previousAdapter));
        Assert.That(flow.BattlePanel, Is.SameAs(panels[0]));
        Assert.That(flow.BattlePanel!.gameObject.activeSelf, Is.True);
        Assert.That(bootstrap.Adapter!.Presentation.Snapshot, Is.Not.Null);
        Assert.That(bootstrap.Adapter.Presentation.Snapshot!.Phase, Is.EqualTo("AMBUSH"));
        Assert.That(bootstrap.Adapter.Presentation.Snapshot.SnapshotRevision, Is.EqualTo(1));
        Assert.That(bootstrap.Adapter.Presentation.Snapshot.WinnerPlayerIndex, Is.Null);
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.False);
        Assert.That(
            () => previousAdapter!.RefreshSnapshot(0),
            Throws.TypeOf<System.ObjectDisposedException>());

        flow.RequestReturnToMenu();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.MainMenu));
    }

    [UnityTest]
    public IEnumerator BattleDoesNotAutomaticallyBecomeResultWithoutExplicitInput()
    {
        yield return SceneManager.LoadSceneAsync(RuntimeBootstrapScene, LoadSceneMode.Single);
        yield return null;
        yield return null;

        var flow = Object.FindFirstObjectByType<RuntimeScreenFlow>();
        Assert.That(flow, Is.Not.Null);
        flow!.ShowBattle();
        yield return null;

        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.False);

        var bootstrap = flow.Bootstrap!;
        bootstrap.StartSession();
        flow.Refresh();
        var current = bootstrap.Adapter!.Presentation.Snapshot!;
        bootstrap.Adapter.AcceptSnapshot(
            SnapshotWithOutcome(current, "GAME_OVER", null, null));
        flow.Refresh();
        Assert.That(flow.CurrentScreen, Is.EqualTo(RuntimeScreenId.Battle));
        Assert.That(flow.View.ResultRoot.gameObject.activeSelf, Is.False);
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
