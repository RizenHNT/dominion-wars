#if UNITY_INCLUDE_TESTS
#nullable enable annotations

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DominionWars.Unity.PlayMode
{

/// <summary>
/// Verifies the player-facing drag path through a real Canvas, GraphicRaycaster
/// and EventSystem. These tests deliberately advance one pointer step per
/// frame; they do not call a drop zone or a button callback directly.
/// </summary>
[TestFixture]
public sealed class RuntimeTargetDragEventSystemPlayModeTests
{
    private GameObject? _canvasObject;
    private RuntimeBattlePanel? _panel;
    private RuntimeAdapter? _adapter;
    private TestSession? _session;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _adapter?.Dispose();
        _adapter = null;
        if (_canvasObject != null)
        {
            UnityEngine.Object.Destroy(_canvasObject);
            _canvasObject = null;
            _panel = null;
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator HandPlayCardToCastleHighlightsAndSubmitsExactlyOnce()
    {
        var play = Action(
            "play_101_castle",
            "PLAY_CARD",
            101L,
            "castle",
            "flame_bolt");
        var initial = Snapshot(
            1,
            new[] { Card(101, "flame_bolt") },
            Array.Empty<RuntimeCardSnapshot>(),
            Array.Empty<RuntimeCardSnapshot>(),
            new[] { play });
        var resulting = Snapshot(
            2,
            Array.Empty<RuntimeCardSnapshot>(),
            Array.Empty<RuntimeCardSnapshot>(),
            Array.Empty<RuntimeCardSnapshot>(),
            Array.Empty<RuntimeLegalAction>());
        yield return BuildPanel(initial, resulting);

        var panel = _panel;
        Assert.That(panel, Is.Not.Null);
        var drag = FindDrag(play.ActionId);
        Assert.That(drag, Is.Not.Null);
        var castle = FindZone("castle");
        Assert.That(castle, Is.Not.Null);
        Assert.That(castle!.IsHighlighted, Is.False);

        var revisionBefore = _adapter!.Presentation.Snapshot!.SnapshotRevision;
        yield return BeginPointerDrag(drag!.gameObject);
        Assert.That(drag.IsDragging, Is.True);
        Assert.That(castle.IsHighlighted, Is.True,
            "A legal target must highlight after the drag starts.");

        yield return MovePointerAndUpdate(drag.gameObject, castle.gameObject);
        Assert.That(castle.IsHighlighted, Is.True,
            "The highlighted legal target must remain visible through the drag.");
        yield return ReleasePointerOnRaycastTarget(drag.gameObject, castle.gameObject);
        yield return null;

        Assert.That(_session!.Submissions, Has.Count.EqualTo(1));
        Assert.That(_session.Submissions[0].ActionId, Is.EqualTo(play.ActionId));
        Assert.That(_session.Submissions[0].TargetId, Is.EqualTo("castle"));
        Assert.That(_adapter.Presentation.Snapshot!.SnapshotRevision,
            Is.EqualTo(revisionBefore + 1));
        Assert.That(drag == null || drag.IsRetired, Is.True,
            "The source card must retire after the one accepted drop.");
        Assert.That(panel.Adapter, Is.SameAs(_adapter));
    }

    [UnityTest]
    public IEnumerator FieldAttackToOpponentCardHighlightsAndSubmitsExactlyOnce()
    {
        var attack = Action("attack_201_301", "ATTACK", 201L, 301L, "flame_unit");
        var initial = Snapshot(
            1,
            Array.Empty<RuntimeCardSnapshot>(),
            new[] { Card(201, "flame_unit", currentAttack: 4, currentHealth: 5) },
            new[] { Card(301, "wood_unit", owner: 1, currentAttack: 2, currentHealth: 3) },
            new[] { attack });
        var resulting = Snapshot(
            2,
            Array.Empty<RuntimeCardSnapshot>(),
            Array.Empty<RuntimeCardSnapshot>(),
            Array.Empty<RuntimeCardSnapshot>(),
            Array.Empty<RuntimeLegalAction>());
        yield return BuildPanel(initial, resulting);

        var drag = FindDrag(attack.ActionId);
        Assert.That(drag, Is.Not.Null);
        var target = FindCardZone(301L);
        Assert.That(target, Is.Not.Null);
        var revisionBefore = _adapter!.Presentation.Snapshot!.SnapshotRevision;

        yield return BeginPointerDrag(drag!.gameObject);
        Assert.That(drag.IsDragging, Is.True);
        Assert.That(target!.IsHighlighted, Is.True,
            "A legal opponent card target must highlight for ATTACK.");
        yield return MovePointerAndUpdate(drag.gameObject, target.gameObject);
        yield return ReleasePointerOnRaycastTarget(drag.gameObject, target.gameObject);
        yield return null;

        Assert.That(_session!.Submissions, Has.Count.EqualTo(1));
        Assert.That(_session.Submissions[0].ActionId, Is.EqualTo(attack.ActionId));
        Assert.That(_session.Submissions[0].SourceId, Is.EqualTo(201L));
        Assert.That(_session.Submissions[0].TargetId, Is.EqualTo(301L));
        Assert.That(_adapter.Presentation.Snapshot!.SnapshotRevision,
            Is.EqualTo(revisionBefore + 1));
    }

    [UnityTest]
    public IEnumerator IllegalDropRollsBackAndKeepsRevisionUnchanged()
    {
        var play = Action("play_101_castle", "PLAY_CARD", 101L, "castle", "flame_bolt");
        var initial = Snapshot(
            1,
            new[] { Card(101, "flame_bolt") },
            Array.Empty<RuntimeCardSnapshot>(),
            Array.Empty<RuntimeCardSnapshot>(),
            new[] { play });
        yield return BuildPanel(initial, Snapshot(
            2,
            Array.Empty<RuntimeCardSnapshot>(),
            Array.Empty<RuntimeCardSnapshot>(),
            Array.Empty<RuntimeCardSnapshot>(),
            Array.Empty<RuntimeLegalAction>()));

        var drag = FindDrag(play.ActionId);
        Assert.That(drag, Is.Not.Null);
        var cardRoot = (RectTransform)drag!.transform;
        var originalParent = cardRoot.parent;
        var originalPosition = cardRoot.anchoredPosition;
        var revisionBefore = _adapter!.Presentation.Snapshot!.SnapshotRevision;

        yield return BeginPointerDrag(drag.gameObject);
        var invalidPosition = new Vector2(3f, 3f);
        yield return MovePointerTo(drag.gameObject, invalidPosition);
        var eventSystem = EventSystem.current;
        Assert.That(eventSystem, Is.Not.Null);
        var hits = Raycast(eventSystem!, invalidPosition);
        Assert.That(
            hits.Any(hit => hit.gameObject.GetComponentInParent<RuntimeBattleDropZone>() != null),
            Is.False,
            "The invalid release point must not be a registered semantic drop target.");
        yield return EndPointerDrag(drag.gameObject, invalidPosition);
        yield return null;

        Assert.That(_session!.Submissions, Is.Empty);
        Assert.That(_adapter.Presentation.Snapshot!.SnapshotRevision,
            Is.EqualTo(revisionBefore));
        Assert.That(drag.IsDragging, Is.False);
        Assert.That(drag.IsRetired, Is.False);
        Assert.That(cardRoot.parent, Is.SameAs(originalParent));
        Assert.That(cardRoot.anchoredPosition, Is.EqualTo(originalPosition));
    }

    [UnityTest]
    public IEnumerator MultipleActionsWithTheSameTargetNeverSilentlyChooseOne()
    {
        var first = Action("play_101_castle_a", "PLAY_CARD", 101L, "castle", "flame_bolt");
        var second = Action("play_101_castle_b", "PLAY_CARD", 101L, "castle", "flame_bolt");
        var initial = Snapshot(
            1,
            new[] { Card(101, "flame_bolt") },
            Array.Empty<RuntimeCardSnapshot>(),
            Array.Empty<RuntimeCardSnapshot>(),
            new[] { first, second });
        yield return BuildPanel(initial, Snapshot(
            2,
            Array.Empty<RuntimeCardSnapshot>(),
            Array.Empty<RuntimeCardSnapshot>(),
            Array.Empty<RuntimeCardSnapshot>(),
            Array.Empty<RuntimeLegalAction>()));

        var drag = FindDrag(first.ActionId);
        Assert.That(drag, Is.Not.Null);
        var castle = FindZone("castle");
        Assert.That(castle, Is.Not.Null);
        var revisionBefore = _adapter!.Presentation.Snapshot!.SnapshotRevision;

        yield return BeginPointerDrag(drag!.gameObject);
        Assert.That(castle!.IsHighlighted, Is.False,
            "An unqualified target with two advertised actions must not look legal.");
        yield return MovePointerAndUpdate(drag.gameObject, castle.gameObject);
        Assert.That(castle.IsHighlighted, Is.False);
        yield return ReleasePointerOnRaycastTarget(drag.gameObject, castle.gameObject);
        yield return null;

        Assert.That(_session!.Submissions, Is.Empty);
        Assert.That(_adapter.Presentation.Snapshot!.SnapshotRevision,
            Is.EqualTo(revisionBefore));
        Assert.That(drag.IsRetired, Is.False);

        var directButtons = _canvasObject!.GetComponentsInChildren<Button>(true)
            .Where(button => button.gameObject.name == "Action_" + first.ActionId ||
                             button.gameObject.name == "Action_" + second.ActionId)
            .ToArray();
        Assert.That(directButtons, Has.Length.EqualTo(2),
            "Each complete advertised variant must remain an explicit choice.");
    }

    private IEnumerator BuildPanel(
        RuntimeSnapshotEnvelope initial,
        RuntimeSnapshotEnvelope resulting)
    {
        _session = new TestSession(initial, resulting);
        _adapter = new RuntimeAdapter(_session);
        _adapter.AcceptSnapshot(initial);

        _canvasObject = new GameObject(
            "RuntimeTargetDragEventSystemCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        var canvas = _canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = _canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        // The panel creates the real EventSystem if the test scene has none.
        _panel = _canvasObject.AddComponent<RuntimeBattlePanel>();
        _panel.SetFollowCurrentPlayer(false);
        _panel.Bind(_adapter);
        Canvas.ForceUpdateCanvases();
        yield return null;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(
            _canvasObject.transform.Find("RuntimeBattlePanelContent") as RectTransform);
        yield return null;
    }

    private RuntimeBattleCardDrag FindDrag(string actionId)
    {
        var drag = _canvasObject!.GetComponentsInChildren<RuntimeBattleCardDrag>(true)
            .FirstOrDefault(candidate => candidate.Actions.Any(action => action.ActionId == actionId));
        return drag!;
    }

    private RuntimeBattleDropZone FindZone(object targetId)
    {
        var zone = _canvasObject!.GetComponentsInChildren<RuntimeBattleDropZone>(true)
            .FirstOrDefault(candidate =>
                RuntimeBattlePanelActionModel.WireValuesEqual(candidate.TargetId, targetId));
        return zone!;
    }

    private RuntimeBattleDropZone FindCardZone(long entityId)
    {
        return _canvasObject!.GetComponentsInChildren<RuntimeBattleDropZone>(true)
            .FirstOrDefault(candidate =>
                RuntimeBattlePanelActionModel.WireValuesEqual(candidate.TargetId, entityId))!;
    }

    private static IEnumerator BeginPointerDrag(GameObject source)
    {
        var eventSystem = EventSystem.current;
        Assert.That(eventSystem, Is.Not.Null);
        var position = Center(source);
        var pointer = NewPointer(eventSystem!, position);
        pointer.pointerPress = source;
        pointer.pointerDrag = source;
        Assert.That(Raycast(eventSystem!, position), Has.Some.Property("gameObject").SameAs(source));
        ExecuteEvents.Execute(source, pointer, ExecuteEvents.pointerDownHandler);
        yield return null;
        Assert.That(ExecuteEvents.Execute(source, pointer, ExecuteEvents.beginDragHandler), Is.True);
        yield return null;
    }

    private static IEnumerator MovePointerAndUpdate(GameObject source, GameObject target)
    {
        var position = Center(target);
        yield return MovePointerTo(source, position);
        var eventSystem = EventSystem.current;
        Assert.That(eventSystem, Is.Not.Null);
        var pointer = NewPointer(eventSystem!, position);
        pointer.pointerDrag = source;
        pointer.pointerPress = source;
        var hit = Raycast(eventSystem!, position)
            .FirstOrDefault(result =>
                result.gameObject.GetComponentInParent<RuntimeBattleDropZone>() != null);
        Assert.That(hit.gameObject, Is.Not.Null,
            "The target must be discoverable by the real GraphicRaycaster.");
        ExecuteEvents.ExecuteHierarchy(hit.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
        ExecuteEvents.Execute(source, pointer, ExecuteEvents.dragHandler);
        yield return null;
    }

    private static IEnumerator ReleasePointerOnRaycastTarget(GameObject source, GameObject target)
    {
        var eventSystem = EventSystem.current;
        Assert.That(eventSystem, Is.Not.Null);
        var position = Center(target);
        var pointer = NewPointer(eventSystem!, position);
        pointer.pointerDrag = source;
        pointer.pointerPress = source;
        var hit = Raycast(eventSystem!, position)
            .FirstOrDefault(result =>
                result.gameObject.GetComponentInParent<RuntimeBattleDropZone>() != null);
        Assert.That(hit.gameObject, Is.Not.Null,
            "The release must use a real GraphicRaycaster hit.");
        ExecuteEvents.ExecuteHierarchy(hit.gameObject, pointer, ExecuteEvents.dropHandler);
        yield return null;
        ExecuteEvents.Execute(source, pointer, ExecuteEvents.endDragHandler);
        yield return null;
    }

    private static IEnumerator MovePointerTo(GameObject source, Vector2 position)
    {
        var eventSystem = EventSystem.current;
        Assert.That(eventSystem, Is.Not.Null);
        var pointer = NewPointer(eventSystem!, position);
        pointer.pointerDrag = source;
        pointer.pointerPress = source;
        ExecuteEvents.Execute(source, pointer, ExecuteEvents.dragHandler);
        yield return null;
    }

    private static IEnumerator EndPointerDrag(GameObject source, Vector2 position)
    {
        var eventSystem = EventSystem.current;
        Assert.That(eventSystem, Is.Not.Null);
        var pointer = NewPointer(eventSystem!, position);
        pointer.pointerDrag = source;
        pointer.pointerPress = source;
        ExecuteEvents.Execute(source, pointer, ExecuteEvents.endDragHandler);
        yield return null;
    }

    private static List<RaycastResult> Raycast(EventSystem eventSystem, Vector2 position)
    {
        var pointer = NewPointer(eventSystem, position);
        var hits = new List<RaycastResult>();
        eventSystem.RaycastAll(pointer, hits);
        return hits;
    }

    private static PointerEventData NewPointer(EventSystem eventSystem, Vector2 position)
    {
        return new PointerEventData(eventSystem)
        {
            position = position,
            pressPosition = position,
            button = PointerEventData.InputButton.Left,
        };
    }

    private static Vector2 Center(GameObject target)
    {
        var rect = target.GetComponent<RectTransform>();
        Assert.That(rect, Is.Not.Null);
        return RectTransformUtility.WorldToScreenPoint(
            null,
            rect!.TransformPoint(rect.rect.center));
    }

    private static RuntimeSnapshotEnvelope Snapshot(
        long revision,
        IReadOnlyList<RuntimeCardSnapshot> hand,
        IReadOnlyList<RuntimeCardSnapshot> ownField,
        IReadOnlyList<RuntimeCardSnapshot> opponentField,
        IReadOnlyList<RuntimeLegalAction> actions)
    {
        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = "match_target_drag_event_system",
            SnapshotRevision = revision,
            Turn = 1,
            Phase = "ACTION",
            CurrentPlayer = 0,
            ViewerPlayerId = "player_0",
            Players = new[]
            {
                new RuntimePlayerSnapshot
                {
                    PlayerId = "player_0",
                    Hand = hand,
                    Field = ownField,
                    HandCount = hand.Count,
                    FieldCount = ownField.Count,
                    Life = 30,
                },
                new RuntimePlayerSnapshot
                {
                    PlayerId = "player_1",
                    Field = opponentField,
                    FieldCount = opponentField.Count,
                    Life = 30,
                },
            },
            Castle = new RuntimeCastleSnapshot { Enabled = true, Health = 61 },
            LegalActions = actions,
        };
    }

    private static RuntimeCardSnapshot Card(
        long entityId,
        string cardId,
        int owner = 0,
        int? currentAttack = null,
        int? currentHealth = null)
    {
        return new RuntimeCardSnapshot
        {
            EntityId = entityId,
            CardId = cardId,
            OwnerPlayer = owner,
            CurrentAttack = currentAttack,
            CurrentHealth = currentHealth,
        };
    }

    private static RuntimeLegalAction Action(
        string actionId,
        string type,
        object source,
        object target,
        string cardId)
    {
        return new RuntimeLegalAction
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            SnapshotRevision = 1,
            ActionId = actionId,
            Type = type,
            Actor = 0,
            SourceId = source,
            TargetId = target,
            CardId = cardId,
            Payload = new Dictionary<string, object?>(),
        };
    }

    private sealed class TestSession : IRuntimeSession
    {
        private RuntimeSnapshotEnvelope _snapshot;
        private readonly RuntimeSnapshotEnvelope _resulting;

        public TestSession(RuntimeSnapshotEnvelope initial, RuntimeSnapshotEnvelope resulting)
        {
            _snapshot = initial;
            _resulting = resulting;
        }

        public List<RuntimeGameAction> Submissions { get; } = new List<RuntimeGameAction>();

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex) => _snapshot;

        public RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            Submissions.Add(action);
            _snapshot = _resulting;
            return new RuntimeActionSubmission(
                new RuntimeActionResult
                {
                    ContractVersion = ContractVersionGuard.ExpectedVersion,
                    MatchId = action.MatchId,
                    SnapshotRevision = action.SnapshotRevision,
                    ResultingSnapshotRevision = _resulting.SnapshotRevision,
                    ActionId = action.ActionId,
                    Accepted = true,
                    ReasonKey = "action.accepted",
                },
                Array.Empty<DominionWars.Engine.Events.GameEvent>(),
                _resulting);
        }

        public void Close() { }
    }
}
}
#endif
