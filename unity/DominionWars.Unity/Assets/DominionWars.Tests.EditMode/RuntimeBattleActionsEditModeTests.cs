#nullable enable annotations

using System.Collections.Generic;
using System.Reflection;
using DominionWars.Adapters;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeBattleActionsEditModeTests
{
    [Test]
    public void ActionGroupsExposeOnlyAdvertisedAttackSourceAndTargetPairs()
    {
        var first = Action("attack_7_8", "ATTACK", 7L, 8L);
        var second = Action("attack_7_9", "ATTACK", 7L, 9L);
        var snapshot = Snapshot(first, second);

        var groups = RuntimeBattlePanelActionModel.BuildActionGroups(snapshot);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].Actions, Has.Count.EqualTo(2));
        Assert.That(groups[0].SourceOptions, Has.Count.EqualTo(1));
        Assert.That(groups[0].TargetOptions, Has.Count.EqualTo(2));
        Assert.That(groups[0].SelectedAction.LegalAction.ActionId, Is.EqualTo(first.ActionId));

        Assert.That(groups[0].TrySelectTarget(9L), Is.True);
        Assert.That(groups[0].SelectedAction.LegalAction.ActionId, Is.EqualTo(second.ActionId));
        Assert.That(groups[0].SelectedAction.TargetId, Is.EqualTo(9L));
    }

    [Test]
    public void TargetedPlaySelectsAndSubmitsAnExistingCompleteVariant()
    {
        var face = Action(
            "play_22_core:player_1:life",
            "PLAY_CARD",
            22L,
            "player_1",
            "flame_bolt",
            new Dictionary<string, object?> { ["punish"] = 1 });
        var castle = Action(
            "play_22_core:shared_castle",
            "PLAY_CARD",
            22L,
            "castle",
            "flame_bolt",
            new Dictionary<string, object?> { ["punish"] = 1 });
        var groups = RuntimeBattlePanelActionModel.BuildActionGroups(Snapshot(face, castle));

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].TargetOptions, Has.Count.EqualTo(2));
        Assert.That(groups[0].TrySelectTarget("castle"), Is.True);

        var selected = groups[0].SelectedAction.LegalAction;
        var submission = RuntimeBattlePanelActionModel.ToGameAction(selected, "match_actions");

        Assert.That(selected, Is.SameAs(castle));
        Assert.That(submission.ActionId, Is.EqualTo(castle.ActionId));
        Assert.That(submission.SourceId, Is.EqualTo(22L));
        Assert.That(submission.TargetId, Is.EqualTo("castle"));
        Assert.That(submission.Payload, Is.SameAs(castle.Payload));
    }

    [Test]
    public void PullSourceSelectionKeepsTheAdvertisedTargetAndActionIdentity()
    {
        var first = Action("pull_10_21", "PULL", 10L, 21L, "cloud_card");
        var second = Action("pull_11_21", "PULL", 11L, 21L, "cloud_card");
        var groups = RuntimeBattlePanelActionModel.BuildActionGroups(Snapshot(first, second));

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].RequiresSourceSelection, Is.True);
        Assert.That(groups[0].RequiresTargetSelection, Is.False);
        Assert.That(groups[0].TrySelectSource(11L), Is.True);
        Assert.That(groups[0].SelectedAction.LegalAction.ActionId, Is.EqualTo(second.ActionId));
        Assert.That(groups[0].SelectedAction.TargetId, Is.EqualTo(21L));
    }

    [Test]
    public void DisabledAdvertisedActionCannotBecomeTheSelectedSubmission()
    {
        var disabled = Action(
            "play_7",
            "PLAY_CARD",
            7L,
            null,
            "card",
            new Dictionary<string, object?> { ["requiresTarget"] = true });
        var enabled = Action("end_0", "END_TURN", null, null);
        var groups = RuntimeBattlePanelActionModel.BuildActionGroups(Snapshot(disabled, enabled));

        Assert.That(groups, Has.Count.EqualTo(2));
        var play = groups[0];
        Assert.That(play.SelectedAction.State.Interactable, Is.False);
        Assert.That(play.TrySelectAction(disabled.ActionId), Is.False);
        Assert.That(play.SelectedAction.LegalAction.ActionId, Is.EqualTo(disabled.ActionId));
    }

    [Test]
    public void ActionLabelIncludesWireSourceAndTargetWithoutDerivingRules()
    {
        var legal = Action("attack_7_castle", "ATTACK", 7L, "castle");
        var state = RuntimeBattlePanelActionModel.Evaluate(legal);

        var label = RuntimeBattlePanelActionModel.Describe(legal, state);

        Assert.That(label, Does.Contain("source=7"));
        Assert.That(label, Does.Contain("target=castle"));
        Assert.That(label, Does.Contain("attack_7_castle"));
    }

    [Test]
    public void HotSeatViewerFollowsCurrentPlayerAndRefreshesRedactedSnapshot()
    {
        GameObject panelObject = null!;
        RuntimeAdapter? adapter = null;
        try
        {
            panelObject = new GameObject("RuntimeBattleActionsHotSeatTest");
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            var session = new HotSeatSession();
            adapter = new RuntimeAdapter(session);

            panel.Bind(adapter);
            Assert.That(panel.ViewerPlayerIndex, Is.EqualTo(1));
            Assert.That(adapter.Presentation.Snapshot!.ViewerPlayerId, Is.EqualTo("player_1"));
            Assert.That(adapter.Presentation.Snapshot.Players[1].Hand.Count, Is.EqualTo(1));
            Assert.That(adapter.Presentation.Snapshot.Players[0].Hand, Is.Empty);
            Assert.That(session.RequestedViewers, Is.EqualTo(new[] { 0, 1 }));
        }
        finally
        {
            adapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void EndTurnButtonOnClickSubmitsAdvertisedActionAndRendersAcceptedResult()
    {
        GameObject panelObject = null!;
        RuntimeAdapter? adapter = null;
        try
        {
            var endTurn = Action("end_turn_0", "END_TURN", null, null);
            var initial = Snapshot(endTurn);
            var resulting = SnapshotAt(1, 1, "START");
            var session = new ButtonSession(
                initial,
                Submission(initial, endTurn, true, resulting, "action.accepted"));
            adapter = new RuntimeAdapter(session);
            adapter.AcceptSnapshot(initial);

            panelObject = CreatePanelObject();
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.SetFollowCurrentPlayer(false);
            panel.Bind(adapter);

            var button = FindButton(panelObject, "Action_end_turn_0");
            Assert.That(button.onClick.GetPersistentEventCount(), Is.EqualTo(0));
            button.onClick.Invoke();

            Assert.That(session.LastAction, Is.Not.Null);
            Assert.That(session.LastAction!.ActionId, Is.EqualTo(endTurn.ActionId));
            Assert.That(session.LastAction.Type, Is.EqualTo("END_TURN"));
            Assert.That(panel.LastActionStatus, Does.Contain("Action accepted: END_TURN"));
            Assert.That(panel.Adapter!.Presentation.Snapshot!.SnapshotRevision, Is.EqualTo(1));
            Assert.That(panel.ActionGroups, Is.Empty);
        }
        finally
        {
            adapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
        }
    }

    [TestCase("PLAY_CARD", "play_card_7", 7L, "castle")]
    [TestCase("ATTACK", "attack_7_9", 7L, "entity:9")]
    public void PlayAndAttackButtonsSubmitAdvertisedWireAction(
        string type,
        string actionId,
        long sourceId,
        string targetId)
    {
        GameObject panelObject = null!;
        RuntimeAdapter? adapter = null;
        try
        {
            var action = Action(actionId, type, sourceId, targetId, "wire_card");
            var initial = Snapshot(action);
            var resulting = SnapshotAt(1, 0, "ACTION");
            var session = new ButtonSession(
                initial,
                Submission(initial, action, true, resulting, "action.accepted"));
            adapter = new RuntimeAdapter(session);
            adapter.AcceptSnapshot(initial);

            panelObject = CreatePanelObject();
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.SetFollowCurrentPlayer(false);
            panel.Bind(adapter);

            FindButton(panelObject, "Action_" + actionId).onClick.Invoke();

            Assert.That(session.LastAction, Is.Not.Null);
            Assert.That(session.LastAction!.Type, Is.EqualTo(type));
            Assert.That(session.LastAction.ActionId, Is.EqualTo(actionId));
            Assert.That(session.LastAction.SourceId, Is.EqualTo(sourceId));
            Assert.That(session.LastAction.TargetId, Is.EqualTo(targetId));
            Assert.That(panel.LastActionStatus, Does.Contain("Action accepted: " + type));
        }
        finally
        {
            adapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void PullChoiceToggleAndSubmitButtonPreserveSelectedSourceAndTarget()
    {
        GameObject panelObject = null!;
        RuntimeAdapter? adapter = null;
        try
        {
            var first = Action("pull_10_21", "PULL", 10L, 21L, "cloud_card");
            var second = Action("pull_11_21", "PULL", 11L, 21L, "cloud_card");
            var initial = Snapshot(first, second);
            var resulting = SnapshotAt(1, 0, "ACTION");
            var session = new ButtonSession(
                initial,
                Submission(initial, second, true, resulting, "action.accepted"));
            adapter = new RuntimeAdapter(session);
            adapter.AcceptSnapshot(initial);

            panelObject = CreatePanelObject();
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.SetFollowCurrentPlayer(false);
            panel.Bind(adapter);

            Assert.That(panel.ActionGroups.Count, Is.EqualTo(1));
            Assert.That(panel.ActionGroups[0].RequiresSourceSelection, Is.True);
            Assert.That(panel.ActionGroups[0].RequiresTargetSelection, Is.False);

            var choice = FindToggle(panelObject, "Choice_pull_11_21");
            choice.onValueChanged.Invoke(true);
            var submit = FindButton(panelObject, "Submit");
            submit.onClick.Invoke();

            Assert.That(session.LastAction, Is.Not.Null);
            Assert.That(session.LastAction!.ActionId, Is.EqualTo(second.ActionId));
            Assert.That(session.LastAction.SourceId, Is.EqualTo(11L));
            Assert.That(session.LastAction.TargetId, Is.EqualTo(21L));
            Assert.That(panel.LastActionStatus, Does.Contain("Action accepted: PULL"));
        }
        finally
        {
            adapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void RejectedButtonResultRefreshesAdvertisedActionsAndKeepsReasonVisible()
    {
        GameObject panelObject = null!;
        RuntimeAdapter? adapter = null;
        try
        {
            var rejected = Action("play_rejected", "PLAY_CARD", 7L, null, "wood_card");
            var replacement = Action("end_after_reject", "END_TURN", null, null);
            var initial = Snapshot(rejected);
            var refreshed = SnapshotAt(0, 0, "ACTION", replacement);
            replacement.SnapshotRevision = refreshed.SnapshotRevision;
            var session = new ButtonSession(
                initial,
                Submission(initial, rejected, false, refreshed, "action.not_allowed"));
            adapter = new RuntimeAdapter(session);
            adapter.AcceptSnapshot(initial);

            panelObject = CreatePanelObject();
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.SetFollowCurrentPlayer(false);
            panel.Bind(adapter);

            FindButton(panelObject, "Action_play_rejected").onClick.Invoke();

            Assert.That(session.LastAction, Is.Not.Null);
            Assert.That(session.LastAction!.ActionId, Is.EqualTo(rejected.ActionId));
            Assert.That(panel.LastActionStatus, Does.Contain("Action rejected: action.not_allowed"));
            Assert.That(panel.Adapter!.Presentation.Snapshot, Is.SameAs(refreshed));
            Assert.That(panel.ActionGroups.Count, Is.EqualTo(1));
            Assert.That(panel.ActionGroups[0].SelectedAction.LegalAction.ActionId,
                Is.EqualTo(replacement.ActionId));
        }
        finally
        {
            adapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void HotSeatBindRefreshesViewerAndHidesOpponentHandInRenderedText()
    {
        GameObject panelObject = null!;
        RuntimeAdapter? adapter = null;
        try
        {
            var viewer0 = SnapshotAt(0, 1, "ACTION");
            viewer0.ViewerPlayerId = "player_0";
            viewer0.Players = PlayersForViewer(0);
            var viewer1 = SnapshotAt(0, 1, "ACTION");
            viewer1.ViewerPlayerId = "player_1";
            viewer1.Players = PlayersForViewer(1);
            var session = new ViewerButtonSession(viewer0, viewer1);
            adapter = new RuntimeAdapter(session);
            adapter.AcceptSnapshot(viewer0);

            panelObject = CreatePanelObject();
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);

            Assert.That(panel.ViewerPlayerIndex, Is.EqualTo(1));
            Assert.That(session.RequestedViewers, Is.EqualTo(new[] { 0, 1 }));
            var texts = panelObject.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            var ownText = FindText(texts, "RuntimeBattlePanelOwn");
            var opponentText = FindText(texts, "RuntimeBattlePanelOpponent");
            Assert.That(ownText.text, Does.Contain("only_viewer_1"));
            Assert.That(opponentText.text, Does.Contain("隐藏（仅数量可见）"));
            Assert.That(opponentText.text, Does.Not.Contain("only_viewer_0"));
        }
        finally
        {
            adapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void ExplicitAdapterBindingWinsAndUnbindRestoresAutomaticBootstrapDiscovery()
    {
        GameObject panelObject = null!;
        GameObject bootstrapObject = null!;
        RuntimeAdapter? discoveredAdapter = null;
        RuntimeAdapter? explicitAdapter = null;
        try
        {
            bootstrapObject = new GameObject("RuntimeBattlePanelAutoBootstrap");
            bootstrapObject.SetActive(false);
            var bootstrap = bootstrapObject.AddComponent<RuntimeBootstrap>();
            discoveredAdapter = new RuntimeAdapter(
                new SnapshotSession(SnapshotAt(0, 0, "BOOTSTRAP")));
            SetBootstrapAdapter(bootstrap, discoveredAdapter);
            bootstrapObject.SetActive(true);

            var explicitAction = Action("explicit_end_turn", "END_TURN", null, null);
            explicitAdapter = new RuntimeAdapter(
                new SnapshotSession(SnapshotAt(0, 0, "EXPLICIT", explicitAction)));

            panelObject = new GameObject("RuntimeBattlePanelExplicitAdapterTest", typeof(RectTransform));
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(explicitAdapter);

            Assert.That(panel.Adapter, Is.SameAs(explicitAdapter));
            Assert.That(panel.Bootstrap, Is.Null);

            panel.Unbind();

            Assert.That(panel.Bootstrap, Is.SameAs(bootstrap));
            Assert.That(panel.Adapter, Is.SameAs(discoveredAdapter));
        }
        finally
        {
            explicitAdapter?.Dispose();
            discoveredAdapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
            if (bootstrapObject != null) Object.DestroyImmediate(bootstrapObject);
        }
    }

    private static RuntimeSnapshotEnvelope Snapshot(params RuntimeLegalAction[] actions)
    {
        return SnapshotAt(0, 0, "ACTION", actions);
    }

    private static RuntimeSnapshotEnvelope SnapshotAt(
        long revision,
        int currentPlayer,
        string phase,
        params RuntimeLegalAction[] actions)
    {
        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = "match_actions",
            SnapshotRevision = revision,
            Turn = 1,
            Phase = phase,
            CurrentPlayer = currentPlayer,
            ViewerPlayerId = "player_0",
            Players = PlayersForViewer(0),
            LegalActions = actions,
        };
    }

    private static IReadOnlyList<RuntimePlayerSnapshot> PlayersForViewer(int viewerPlayerIndex)
    {
        return new[]
        {
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                Hand = viewerPlayerIndex == 0
                    ? new[] { new RuntimeCardSnapshot { EntityId = 70, CardId = "only_viewer_0" } }
                    : System.Array.Empty<RuntimeCardSnapshot>(),
            },
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_1",
                Hand = viewerPlayerIndex == 1
                    ? new[] { new RuntimeCardSnapshot { EntityId = 71, CardId = "only_viewer_1" } }
                    : System.Array.Empty<RuntimeCardSnapshot>(),
            },
        };
    }

    private static RuntimeActionSubmission Submission(
        RuntimeSnapshotEnvelope initial,
        RuntimeLegalAction action,
        bool accepted,
        RuntimeSnapshotEnvelope resulting,
        string reasonKey)
    {
        return new RuntimeActionSubmission(
            new RuntimeActionResult
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                MatchId = initial.MatchId,
                SnapshotRevision = initial.SnapshotRevision,
                ResultingSnapshotRevision = resulting.SnapshotRevision,
                ActionId = action.ActionId,
                Accepted = accepted,
                ReasonKey = reasonKey,
            },
            System.Array.Empty<DominionWars.Engine.Events.GameEvent>(),
            resulting);
    }

    private static GameObject CreatePanelObject()
    {
        return new GameObject(
            "RuntimeBattleActionButtonTest",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler),
            typeof(UnityEngine.UI.GraphicRaycaster));
    }

    private static UnityEngine.UI.Button FindButton(GameObject root, string name)
    {
        foreach (var button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
        {
            if (button.gameObject.name == name) return button;
        }
        Assert.Fail("Button not found: " + name);
        return null!;
    }

    private static UnityEngine.UI.Toggle FindToggle(GameObject root, string name)
    {
        foreach (var toggle in root.GetComponentsInChildren<UnityEngine.UI.Toggle>(true))
        {
            if (toggle.gameObject.name == name) return toggle;
        }
        Assert.Fail("Toggle not found: " + name);
        return null!;
    }

    private static UnityEngine.UI.Text FindText(
        IReadOnlyList<UnityEngine.UI.Text> texts,
        string parentName)
    {
        foreach (var text in texts)
        {
            if (text.transform.parent != null &&
                text.transform.parent.name == parentName &&
                text.gameObject.name != parentName + "Title")
                return text;
        }
        Assert.Fail("Text not found under: " + parentName);
        return null!;
    }

    private static RuntimeLegalAction Action(
        string actionId,
        string type,
        object? source,
        object? target,
        string? cardId = null,
        IReadOnlyDictionary<string, object?>? payload = null)
    {
        return new RuntimeLegalAction
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            SnapshotRevision = 0,
            ActionId = actionId,
            Type = type,
            Actor = 0,
            SourceId = source,
            TargetId = target,
            CardId = cardId,
            Payload = payload ?? new Dictionary<string, object?>(),
        };
    }

    private sealed class HotSeatSession : IRuntimeSession
    {
        public List<int> RequestedViewers { get; } = new List<int>();

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
        {
            RequestedViewers.Add(viewerPlayerIndex);
            return new RuntimeSnapshotEnvelope
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                MatchId = "match_hotseat",
                SnapshotRevision = 0,
                Turn = 1,
                Phase = "ACTION",
                CurrentPlayer = 1,
                ViewerPlayerId = "player_" + viewerPlayerIndex,
                Players = new[]
                {
                    new RuntimePlayerSnapshot
                    {
                        PlayerId = "player_0",
                        Hand = viewerPlayerIndex == 0
                            ? new[] { new RuntimeCardSnapshot { EntityId = 7, CardId = "only_viewer_0" } }
                            : System.Array.Empty<RuntimeCardSnapshot>(),
                    },
                    new RuntimePlayerSnapshot
                    {
                        PlayerId = "player_1",
                        Hand = viewerPlayerIndex == 1
                            ? new[] { new RuntimeCardSnapshot { EntityId = 8, CardId = "only_viewer_1" } }
                            : System.Array.Empty<RuntimeCardSnapshot>(),
                    },
                },
                LegalActions = System.Array.Empty<RuntimeLegalAction>(),
            };
        }

        public RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            throw new System.NotSupportedException();
        }

        public void Close() { }
    }

    private sealed class ButtonSession : IRuntimeSession
    {
        private RuntimeSnapshotEnvelope _snapshot;
        private readonly RuntimeActionSubmission _submission;

        public ButtonSession(
            RuntimeSnapshotEnvelope snapshot,
            RuntimeActionSubmission submission)
        {
            _snapshot = snapshot;
            _submission = submission;
        }

        public RuntimeGameAction? LastAction { get; private set; }

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex) => _snapshot;

        public RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            LastAction = action;
            _snapshot = _submission.Snapshot;
            return _submission;
        }

        public void Close() { }
    }

    private sealed class ViewerButtonSession : IRuntimeSession
    {
        private readonly RuntimeSnapshotEnvelope[] _snapshots;

        public ViewerButtonSession(
            RuntimeSnapshotEnvelope viewer0,
            RuntimeSnapshotEnvelope viewer1)
        {
            _snapshots = new[] { viewer0, viewer1 };
        }

        public List<int> RequestedViewers { get; } = new List<int>();

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
        {
            RequestedViewers.Add(viewerPlayerIndex);
            return _snapshots[viewerPlayerIndex];
        }

        public RuntimeActionSubmission Submit(RuntimeGameAction action) =>
            throw new System.NotSupportedException();

        public void Close() { }
    }

    private sealed class SnapshotSession : IRuntimeSession
    {
        private readonly RuntimeSnapshotEnvelope _snapshot;

        public SnapshotSession(RuntimeSnapshotEnvelope snapshot)
        {
            _snapshot = snapshot;
        }

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
        {
            return _snapshot;
        }

        public RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            throw new System.NotSupportedException();
        }

        public void Close() { }
    }

    private static void SetBootstrapAdapter(RuntimeBootstrap bootstrap, RuntimeAdapter? adapter)
    {
        var field = typeof(RuntimeBootstrap).GetField(
            "<Adapter>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field!.SetValue(bootstrap, adapter);
    }
}
}
