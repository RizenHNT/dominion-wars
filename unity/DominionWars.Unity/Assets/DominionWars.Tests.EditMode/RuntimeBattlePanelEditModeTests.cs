#nullable enable annotations

using System.Collections.Generic;
using System.Reflection;
using DominionWars.Adapters;
using DominionWars.Engine.Events;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeBattlePanelEditModeTests
{
    [Test]
    public void TemporaryUiUsesUnity6LegacyBuiltinFontResource()
    {
        Assert.That(RuntimeBattlePanelDefaults.LegacyBuiltinFontResource, Is.EqualTo("LegacyRuntime.ttf"));
    }

    [Test]
    public void PlayerLookupUsesSnapshotViewerIdForTopAndBottomRoles()
    {
        var snapshot = new RuntimeSnapshotEnvelope
        {
            ViewerPlayerId = "player_1",
            Players = new[]
            {
                new RuntimePlayerSnapshot { PlayerId = "player_0" },
                new RuntimePlayerSnapshot { PlayerId = "player_1" },
            },
        };

        var opponent = RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, false);
        var own = RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, true);

        Assert.That(opponent, Is.Not.Null);
        Assert.That(opponent!.PlayerId, Is.EqualTo("player_0"));
        Assert.That(own, Is.Not.Null);
        Assert.That(own!.PlayerId, Is.EqualTo("player_1"));
    }

    [Test]
    public void MissingViewerIdDoesNotGuessLocalPlayer()
    {
        var snapshot = new RuntimeSnapshotEnvelope
        {
            ViewerPlayerId = string.Empty,
            Players = new[] { new RuntimePlayerSnapshot { PlayerId = "player_0" } },
        };

        Assert.That(RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, false), Is.Null);
        Assert.That(RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, true), Is.Null);
    }

    [Test]
    public void ViewerSnapshotIdentityMustMatchSelectedPlayer()
    {
        var snapshot = PanelSnapshot(0, "AMBUSH");

        Assert.That(RuntimeBattlePanelPresentationModel.IsViewerSnapshot(snapshot, 0), Is.True);
        Assert.That(RuntimeBattlePanelPresentationModel.IsViewerSnapshot(snapshot, 1), Is.False);
        Assert.That(RuntimeBattlePanelPresentationModel.IsViewerSnapshot(snapshot, -1), Is.False);
    }

    [Test]
    public void OpponentHandSummaryNeverRendersCardIdentity()
    {
        var opponent = new RuntimePlayerSnapshot
        {
            PlayerId = "player_0",
            HandCount = 1,
            Hand = new[]
            {
                new RuntimeCardSnapshot { CardId = "secret_card", EntityId = 19 },
            },
        };

        var text = RuntimeBattlePanelPresentationModel.BuildPlayerSection(opponent, false);

        Assert.That(text, Does.Contain("隐藏（仅数量可见）"));
        Assert.That(text, Does.Not.Contain("secret_card"));
        Assert.That(text, Does.Not.Contain("#19"));
    }

    [Test]
    public void OwnCardSummaryUsesOnlyVisibleWireCardFields()
    {
        var own = new RuntimePlayerSnapshot
        {
            PlayerId = "player_1",
            HandCount = 1,
            FieldCount = 1,
            Hand = new[]
            {
                new RuntimeCardSnapshot { CardId = "visible_card", EntityId = 12, Sealed = true },
            },
            Field = new[]
            {
                new RuntimeCardSnapshot { CardId = "field_card", EntityId = 13 },
            },
        };

        var text = RuntimeBattlePanelPresentationModel.BuildPlayerSection(own, true);

        Assert.That(text, Does.Contain("visible_card#12 [sealed]"));
        Assert.That(text, Does.Contain("field_card#13"));
    }

    [Test]
    public void DisabledCastleRendersSafePlaceholder()
    {
        var text = RuntimeBattlePanelPresentationModel.BuildCastleSummary(
            new RuntimeCastleSnapshot { Enabled = false, Health = 999 });

        Assert.That(text, Is.EqualTo("共享王城：未启用"));
    }

    [Test]
    public void LegalActionCopiesWireFieldsWithoutUiInference()
    {
        var payload = new Dictionary<string, object?>
        {
            ["selectedEntityIds"] = new[] { 7L },
        };
        var legal = new RuntimeLegalAction
        {
            ContractVersion = 1,
            SnapshotRevision = 4,
            ActionId = "act_play_0001",
            Type = "PLAY_CARD",
            Actor = 0,
            SourceId = 7L,
            TargetId = "castle",
            CardId = "flame_scout",
            Payload = payload,
        };

        var state = RuntimeBattlePanelActionModel.Evaluate(legal);
        var action = RuntimeBattlePanelActionModel.ToGameAction(legal, "match_fixture");

        Assert.That(state.Interactable, Is.True);
        Assert.That(action.MatchId, Is.EqualTo("match_fixture"));
        Assert.That(action.Type, Is.EqualTo(legal.Type));
        Assert.That(action.SourceId, Is.EqualTo(legal.SourceId));
        Assert.That(action.TargetId, Is.EqualTo(legal.TargetId));
        Assert.That(action.Payload, Is.SameAs(payload));
    }

    [Test]
    public void AdvertisedSkipAmbushReasonDoesNotDisableDirectAction()
    {
        var legal = new RuntimeLegalAction
        {
            ActionId = "skip_ambush_0",
            Type = "SKIP_AMBUSH",
            Actor = 0,
            SnapshotRevision = 1,
            ReasonKey = "action.skip_ambush",
            Payload = new Dictionary<string, object?>(),
        };

        var state = RuntimeBattlePanelActionModel.Evaluate(legal);

        Assert.That(state.Interactable, Is.True);
        Assert.That(state.ReasonKey, Is.Empty);
    }

    [Test]
    public void AdvertisedSkipAmbushSubmissionRefreshesSnapshotPhaseAndRevision()
    {
        var initial = PanelSnapshot(0, "AMBUSH");
        var legal = new RuntimeLegalAction
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            SnapshotRevision = initial.SnapshotRevision,
            ActionId = "skip_ambush_refresh",
            Type = "SKIP_AMBUSH",
            Actor = 0,
            ReasonKey = "action.skip_ambush",
            Payload = new Dictionary<string, object?>(),
        };
        initial.LegalActions = new[] { legal };

        var resulting = PanelSnapshot(1, "ACTION");
        var submission = new RuntimeActionSubmission(
            new RuntimeActionResult
            {
                ContractVersion = ContractVersionGuard.ExpectedVersion,
                MatchId = initial.MatchId,
                SnapshotRevision = initial.SnapshotRevision,
                ResultingSnapshotRevision = resulting.SnapshotRevision,
                ActionId = legal.ActionId,
                Accepted = true,
                ReasonKey = "action.accepted",
            },
            System.Array.Empty<GameEvent>(),
            resulting);
        var adapter = new RuntimeAdapter(new SubmissionSession(submission));
        adapter.AcceptSnapshot(initial);

        // This is the same pure conversion invoked by the uGUI Button
        // callback; RuntimeAdapter remains the authority for acceptance and
        // the resulting snapshot.
        var action = RuntimeBattlePanelActionModel.ToGameAction(legal, initial.MatchId);
        var result = adapter.Submit(action);

        Assert.That(result.Result.Accepted, Is.True);
        Assert.That(adapter.Presentation.Snapshot, Is.SameAs(resulting));
        Assert.That(adapter.Presentation.Snapshot!.SnapshotRevision, Is.EqualTo(1));
        Assert.That(adapter.Presentation.Snapshot.Phase, Is.EqualTo("ACTION"));
        Assert.That(action.ActionId, Is.EqualTo(legal.ActionId));
        Assert.That(action.SnapshotRevision, Is.EqualTo(initial.SnapshotRevision));
    }

    [Test]
    public void BindingBootstrapReplacesOldAdapterAndWaitsForUnreadyAdapter()
    {
        GameObject panelObject = null!;
        GameObject firstBootstrapObject = null!;
        GameObject secondBootstrapObject = null!;
        RuntimeAdapter firstAdapter = null!;
        RuntimeAdapter secondAdapter = null!;
        try
        {
            panelObject = new GameObject("RuntimeBattlePanelBindingTest", typeof(RectTransform));
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();

            firstBootstrapObject = new GameObject("FirstRuntimeBootstrap");
            firstBootstrapObject.SetActive(false);
            var firstBootstrap = firstBootstrapObject.AddComponent<RuntimeBootstrap>();
            firstAdapter = new RuntimeAdapter(new SnapshotSession(PanelSnapshot(0, "START")));
            SetBootstrapAdapter(firstBootstrap, firstAdapter);

            panel.Bind(firstBootstrap);
            Assert.That(panel.Adapter, Is.SameAs(firstAdapter));
            Assert.That(panel.Bootstrap, Is.SameAs(firstBootstrap));

            secondBootstrapObject = new GameObject("SecondRuntimeBootstrap");
            secondBootstrapObject.SetActive(false);
            var secondBootstrap = secondBootstrapObject.AddComponent<RuntimeBootstrap>();

            // Binding a bootstrap before its session is ready must detach the
            // old adapter rather than continuing to render the old match.
            SetBootstrapAdapter(secondBootstrap, null);
            panel.Bind(secondBootstrap);
            Assert.That(panel.Adapter, Is.Null);
            Assert.That(panel.Bootstrap, Is.SameAs(secondBootstrap));

            secondAdapter = new RuntimeAdapter(new SnapshotSession(PanelSnapshot(0, "START")));
            SetBootstrapAdapter(secondBootstrap, secondAdapter);
            panel.Bind(secondBootstrap);
            Assert.That(panel.Adapter, Is.SameAs(secondAdapter));
            Assert.That(panel.Adapter, Is.Not.SameAs(firstAdapter));
        }
        finally
        {
            firstAdapter?.Dispose();
            secondAdapter?.Dispose();
            if (panelObject != null) Object.DestroyImmediate(panelObject);
            if (firstBootstrapObject != null) Object.DestroyImmediate(firstBootstrapObject);
            if (secondBootstrapObject != null) Object.DestroyImmediate(secondBootstrapObject);
        }
    }

    [Test]
    public void ExplicitMissingTargetIsDisabledWithReason()
    {
        var legal = ActionWithPayload("requiresTarget", true);

        var state = RuntimeBattlePanelActionModel.Evaluate(legal);

        Assert.That(state.Interactable, Is.False);
        Assert.That(state.ReasonKey, Is.EqualTo("action.target_required"));
    }

    [Test]
    public void ExplicitMissingSelectionIsDisabledWithReason()
    {
        var legal = ActionWithPayload("requiresSelection", true);

        var state = RuntimeBattlePanelActionModel.Evaluate(legal);

        Assert.That(state.Interactable, Is.False);
        Assert.That(state.ReasonKey, Is.EqualTo("action.selection_required"));
    }

    [Test]
    public void NullTargetWithoutExplicitRequirementRemainsAdvertised()
    {
        var legal = new RuntimeLegalAction
        {
            ActionId = "act_end_0001",
            Type = "END_TURN",
            Actor = 0,
            TargetId = null,
            Payload = new Dictionary<string, object?>(),
        };

        var state = RuntimeBattlePanelActionModel.Evaluate(legal, new { kind = "choose_target" });

        Assert.That(state.Interactable, Is.True);
        Assert.That(state.ReasonKey, Is.Empty);
    }

    [Test]
    public void ExplicitTargetAndSelectionRequirementsRemainTheOnlyUiBlockers()
    {
        var targetRequired = RuntimeBattlePanelActionModel.Evaluate(ActionWithPayload("requiresTarget", true));
        var selectionRequired = RuntimeBattlePanelActionModel.Evaluate(ActionWithPayload("requiresSelection", true));

        Assert.That(targetRequired.Interactable, Is.False);
        Assert.That(targetRequired.ReasonKey, Is.EqualTo("action.target_required"));
        Assert.That(selectionRequired.Interactable, Is.False);
        Assert.That(selectionRequired.ReasonKey, Is.EqualTo("action.selection_required"));
    }

    private static RuntimeLegalAction ActionWithPayload(string key, object value)
    {
        return new RuntimeLegalAction
        {
            ActionId = "act_prompt_0001",
            Type = "PLAY_CARD",
            Actor = 0,
            Payload = new Dictionary<string, object?> { [key] = value },
        };
    }

    private static RuntimeSnapshotEnvelope PanelSnapshot(long revision, string phase)
    {
        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = "match_panel",
            SnapshotRevision = revision,
            Turn = 1,
            Phase = phase,
            CurrentPlayer = 0,
            ViewerPlayerId = "player_0",
            Players = new[]
            {
                new RuntimePlayerSnapshot { PlayerId = "player_0" },
                new RuntimePlayerSnapshot { PlayerId = "player_1" },
            },
            Castle = new RuntimeCastleSnapshot(),
            LegalActions = System.Array.Empty<RuntimeLegalAction>(),
        };
    }

    private sealed class SubmissionSession : IRuntimeSession
    {
        private readonly RuntimeActionSubmission _submission;

        public SubmissionSession(RuntimeActionSubmission submission)
        {
            _submission = submission;
        }

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
        {
            return _submission.Snapshot;
        }

        public RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            return _submission;
        }

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
