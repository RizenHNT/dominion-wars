#if UNITY_INCLUDE_TESTS
#nullable disable

using System;
using System.Collections.Generic;
using DominionWars.Adapters;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeBattlePanelActionFeedbackEditModeTests
{
    private readonly List<GameObject> _objects = new List<GameObject>();
    private readonly List<RuntimeAdapter> _adapters = new List<RuntimeAdapter>();

    [TestCase("CARD_PLAYED", RuntimeBattlePanelFeedbackKind.CardPlayed)]
    [TestCase("ATTACK_DECLARED", RuntimeBattlePanelFeedbackKind.AttackDeclared)]
    [TestCase("CARDS_DRAWN", RuntimeBattlePanelFeedbackKind.CardsDrawn)]
    [TestCase("AMBUSH_SET", RuntimeBattlePanelFeedbackKind.AmbushSet)]
    [TestCase("AMBUSH_TRIGGERED", RuntimeBattlePanelFeedbackKind.AmbushTriggered)]
    [TestCase("CARD_COMMITTED", RuntimeBattlePanelFeedbackKind.Commit)]
    [TestCase("CARD_PUSHED", RuntimeBattlePanelFeedbackKind.Push)]
    [TestCase("CARD_PULLED", RuntimeBattlePanelFeedbackKind.Pull)]
    [TestCase("PUNISH_ISSUED", RuntimeBattlePanelFeedbackKind.Punish)]
    [TestCase("PUNISH_DRAW", RuntimeBattlePanelFeedbackKind.Punish)]
    [TestCase("PUNISH_TRIGGERED", RuntimeBattlePanelFeedbackKind.Punish)]
    [TestCase("CASTLE_DAMAGED", RuntimeBattlePanelFeedbackKind.CastleDamaged)]
    [TestCase("CASTLE_BROKEN", RuntimeBattlePanelFeedbackKind.CastleBroken)]
    [TestCase("PHASE_CHANGED", RuntimeBattlePanelFeedbackKind.PhaseChanged)]
    public void VisibleEventTypesMapToNeutralFeedback(
        string type,
        RuntimeBattlePanelFeedbackKind expectedKind)
    {
        var eventEnvelope = Event("evt_" + type, type, true);

        var mapped = RuntimeBattlePanelActionFeedbackModel.TryMap(eventEnvelope, out var cue);

        Assert.That(mapped, Is.True);
        Assert.That(cue, Is.Not.Null);
        Assert.That(cue.Kind, Is.EqualTo(expectedKind));
        Assert.That(cue.EventType, Is.EqualTo(type));
        Assert.That(cue.Message, Does.Not.Contain("raw"));
    }

    [Test]
    public void MissingTargetUsesImmediateSafeFallback()
    {
        var eventEnvelope = Event("evt_missing_target", "ATTACK_DECLARED", false);

        Assert.That(RuntimeBattlePanelActionFeedbackModel.TryMap(eventEnvelope, out var cue), Is.True);
        Assert.That(cue.HasTarget, Is.False);
        Assert.That(cue.TargetCount, Is.Zero);
        Assert.That(cue.Message, Does.Contain("TARGET UNAVAILABLE"));
    }

    [TestCase("CARD_PLAYED")]
    [TestCase("PHASE_CHANGED")]
    [TestCase("CARDS_DRAWN")]
    [TestCase("PUNISH_DRAW")]
    [TestCase("CASTLE_DAMAGED")]
    [TestCase("CASTLE_BROKEN")]
    public void EventsWithImplicitOrNoTargetsDoNotAdvertiseMissingTarget(string type)
    {
        var eventEnvelope = Event("evt_no_target_" + type, type, false);

        Assert.That(RuntimeBattlePanelActionFeedbackModel.TryMap(eventEnvelope, out var cue), Is.True);
        Assert.That(cue.Message, Does.Not.Contain("TARGET UNAVAILABLE"));
    }

    [Test]
    public void UnknownNullDuplicateAndOutOfOrderEventsAreSafeAndDeduplicated()
    {
        var newer = Event("evt_000000000002", "ATTACK_DECLARED", true);
        newer.SnapshotRevision = 2;
        var older = Event("evt_000000000001", "CARD_PLAYED", true);
        older.SnapshotRevision = 1;
        var unknown = Event("evt_unknown", "PRIVATE_ENGINE_EVENT", true);
        var feedback = new RuntimeBattlePanelActionFeedback();

        feedback.Consume(new[] { newer, older, unknown, newer, null });
        feedback.Consume(new[] { older, unknown, null });

        Assert.That(feedback.ConsumedEventCount, Is.EqualTo(4));
        Assert.That(feedback.AppliedFeedbackCount, Is.EqualTo(2));
        Assert.That(feedback.CurrentCue, Is.Not.Null);
        Assert.That(feedback.CurrentCue.Kind, Is.EqualTo(RuntimeBattlePanelFeedbackKind.CardPlayed));
        Assert.That(feedback.CurrentMessage, Is.EqualTo("CARD PLAYED"));
    }

    [Test]
    public void AmbushTriggerOutranksPhaseChangeWithinOneSubmission()
    {
        var feedback = new RuntimeBattlePanelActionFeedback();

        feedback.Consume(new[]
        {
            Event("evt_ambush", "AMBUSH_TRIGGERED", false),
            Event("evt_phase", "PHASE_CHANGED", false),
        });

        Assert.That(feedback.CurrentCue.Kind, Is.EqualTo(RuntimeBattlePanelFeedbackKind.AmbushTriggered));
        Assert.That(feedback.CurrentMessage, Is.EqualTo("AMBUSH TRIGGERED"));
        Assert.That(feedback.AppliedFeedbackCount, Is.EqualTo(2));
    }

    [Test]
    public void ReducedMotionSettlesImmediatelyAndStandardModeUsesOnePulse()
    {
        var root = NewObject("FeedbackRoot");
        var view = RuntimeBattlePanelView.Build(root.transform);
        var feedback = new RuntimeBattlePanelActionFeedback();
        feedback.Bind(view);

        feedback.SetReducedMotion(false);
        feedback.Consume(new[] { Event("evt_motion_standard", "CARD_PLAYED", true) });
        var animatedScale = view.FeedbackRoot.localScale;

        Assert.That(feedback.ReducedMotion, Is.False);
        Assert.That(feedback.IsAnimating, Is.True);
        Assert.That(animatedScale.x, Is.GreaterThan(1.0f));
        Assert.That(view.FeedbackText.text, Is.EqualTo("CARD PLAYED"));

        feedback.Tick(RuntimeBattlePanelActionFeedback.StandardDurationSeconds);

        Assert.That(feedback.IsAnimating, Is.False);
        Assert.That(view.FeedbackRoot.localScale.x, Is.EqualTo(1.0f).Within(0.001f));

        feedback.SetReducedMotion(true);
        feedback.Consume(new[] { Event("evt_motion_reduced", "PHASE_CHANGED", true) });

        Assert.That(feedback.ReducedMotion, Is.True);
        Assert.That(feedback.IsAnimating, Is.False);
        Assert.That(view.FeedbackRoot.localScale.x, Is.EqualTo(1.0f).Within(0.001f));
        Assert.That(view.FeedbackText.text, Is.EqualTo("PHASE CHANGED"));
    }

    [Test]
    public void ClearRemovesActivePulseWithoutTouchingEventSource()
    {
        var root = NewObject("FeedbackClearRoot");
        var view = RuntimeBattlePanelView.Build(root.transform);
        var feedback = new RuntimeBattlePanelActionFeedback();
        feedback.Bind(view);
        var events = new[] { Event("evt_clear", "CASTLE_DAMAGED", true) };

        feedback.Consume(events);
        var eventIdBeforeClear = events[0].EventId;
        var revisionBeforeClear = events[0].SnapshotRevision;
        feedback.Clear();

        Assert.That(feedback.HasFeedback, Is.False);
        Assert.That(feedback.IsAnimating, Is.False);
        Assert.That(feedback.ConsumedEventCount, Is.EqualTo(1));
        Assert.That(events[0].EventId, Is.EqualTo(eventIdBeforeClear));
        Assert.That(events[0].SnapshotRevision, Is.EqualTo(revisionBeforeClear));
        Assert.That(view.FeedbackText.text, Is.EqualTo("READY"));
    }

    [Test]
    public void ReducedMotionToggleInvokesPanelApiWithoutMutatingPresentationSource()
    {
        var root = NewObject("ReducedMotionTogglePanelRoot");
        root.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var snapshot = Snapshot(0);
        var adapter = new RuntimeAdapter(new StaticSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        var eventEnvelope = Event("evt_000000000001", "CARD_PLAYED", true);
        adapter.ApplyEvents(new[] { eventEnvelope });
        _adapters.Add(adapter);

        var panel = root.AddComponent<RuntimeBattlePanel>();
        panel.Bind(adapter);

        var sourceSnapshot = adapter.Presentation.Snapshot;
        var sourceLegalActions = sourceSnapshot.LegalActions;
        var sourceEventId = adapter.Presentation.Events[0].EventId;
        var toggle = panel.ReducedMotionToggle;
        Assert.That(toggle, Is.Not.Null);
        Assert.That(toggle.isOn, Is.False);
        Assert.That(panel.ActionFeedback.IsAnimating, Is.True);

        // Unity Toggle.isOn dispatches onValueChanged, which is wired to the
        // panel's public SetReducedMotion API during visual-tree creation.
        toggle.isOn = true;

        Assert.That(panel.ReducedMotion, Is.True);
        Assert.That(panel.ActionFeedback.ReducedMotion, Is.True);
        Assert.That(panel.ActionFeedback.IsAnimating, Is.False);
        Assert.That(panel.ActionFeedback.CurrentMessage, Is.EqualTo("CARD PLAYED"));
        Assert.That(adapter.Presentation.Snapshot, Is.SameAs(sourceSnapshot));
        Assert.That(adapter.Presentation.Snapshot.LegalActions, Is.SameAs(sourceLegalActions));
        Assert.That(adapter.Presentation.Events, Has.Count.EqualTo(1));
        Assert.That(adapter.Presentation.Events[0], Is.SameAs(eventEnvelope));
        Assert.That(adapter.Presentation.Events[0].EventId, Is.EqualTo(sourceEventId));
    }

    [TearDown]
    public void TearDown()
    {
        for (var index = _adapters.Count - 1; index >= 0; index--)
            _adapters[index]?.Dispose();
        _adapters.Clear();
        for (var index = _objects.Count - 1; index >= 0; index--)
        {
            if (_objects[index] != null) UnityEngine.Object.DestroyImmediate(_objects[index]);
        }
        _objects.Clear();
    }

    private GameObject NewObject(string name)
    {
        var root = new GameObject(name, typeof(RectTransform));
        _objects.Add(root);
        return root;
    }

    private static RuntimeEventEnvelope Event(string id, string type, bool withTarget)
    {
        return new RuntimeEventEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            EventId = id,
            Type = type,
            Turn = 1,
            Phase = "ACTION",
            SnapshotRevision = 1,
            TargetIds = withTarget
                ? new object[] { "castle" }
                : Array.Empty<object>(),
        };
    }

    private static RuntimeSnapshotEnvelope Snapshot(long revision)
    {
        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = "match_feedback_toggle",
            SnapshotRevision = revision,
            Turn = 1,
            Phase = "ACTION",
            CurrentPlayer = 0,
            ViewerPlayerId = "player_0",
            Players = new[]
            {
                new RuntimePlayerSnapshot { PlayerId = "player_0" },
                new RuntimePlayerSnapshot { PlayerId = "player_1" },
            },
            Castle = new RuntimeCastleSnapshot { Enabled = true, Health = 10 },
            LegalActions = Array.Empty<RuntimeLegalAction>(),
        };
    }

    private sealed class StaticSession : IRuntimeSession
    {
        private readonly RuntimeSnapshotEnvelope _snapshot;

        public StaticSession(RuntimeSnapshotEnvelope snapshot)
        {
            _snapshot = snapshot;
        }

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex) => _snapshot;

        public RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            throw new NotSupportedException();
        }

        public void Close() { }
    }
}
}
#endif
