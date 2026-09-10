using System.Collections.Generic;
using DominionWars.Adapters;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class RuntimeEventCursorTests
{
    [Test]
    public void AcceptsOrderedParentLinkedEvents()
    {
        var cursor = new RuntimeEventCursor();
        var first = Event(1, null, "PHASE_CHANGED");
        var second = Event(2, first.EventId, "PULL_DECLARED");

        Assert.Multiple(() =>
        {
            Assert.That(cursor.Accept(first).Accepted, Is.True);
            Assert.That(cursor.Accept(second).Accepted, Is.True);
            Assert.That(cursor.LastEventId, Is.EqualTo(second.EventId));
        });
    }

    [Test]
    public void RejectsDuplicateGapOutOfOrderAndMissingParent()
    {
        var cursor = new RuntimeEventCursor();
        var first = Event(1, null, "PHASE_CHANGED");
        Assert.That(cursor.Accept(first).Accepted, Is.True);

        Assert.That(cursor.Accept(first).ReasonKey, Is.EqualTo("event.duplicate"));
        Assert.That(cursor.Accept(Event(3, first.EventId, "TURN_CHANGED")).ReasonKey, Is.EqualTo("event.gap"));
        Assert.That(cursor.Accept(Event(1, null, "PHASE_CHANGED")).ReasonKey, Is.EqualTo("event.duplicate"));
        Assert.That(cursor.Accept(Event(2, "evt_000000000099", "TURN_CHANGED")).ReasonKey,
            Is.EqualTo("event.parent_missing"));
    }

    [Test]
    public void RejectsUnknownTypeInvalidPhaseAndSelfParent()
    {
        var cursor = new RuntimeEventCursor();
        Assert.That(cursor.Accept(Event(1, null, "INTERNAL_ONLY")).ReasonKey, Is.EqualTo("event.type_unknown"));
        var invalidPhase = Event(1, null, "PHASE_CHANGED");
        invalidPhase.Phase = "FUTURE";
        Assert.That(cursor.Accept(invalidPhase).ReasonKey, Is.EqualTo("event.phase_invalid"));
        var self = Event(1, "evt_000000000001", "PHASE_CHANGED");
        Assert.That(cursor.Accept(self).ReasonKey, Is.EqualTo("event.parent_missing"));
    }

    [Test]
    public void RejectsUnknownSchemaDataField()
    {
        var envelope = Event(1, null, "DAMAGE_APPLIED");
        envelope.Data = new Dictionary<string, object?>
        {
            ["source"] = 7L,
        };

        var result = new RuntimeEventCursor().Accept(envelope);

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.ReasonKey, Is.EqualTo("event.data_field_unknown"));
    }

    [Test]
    public void AcceptsGameOverOnlyWithMatchingTerminalOutcome()
    {
        var envelope = Event(1, null, "GAME_OVER");
        envelope.Phase = "OVER";
        envelope.ReasonKey = "win.royal_castle_break";
        envelope.Data = new Dictionary<string, object?>
        {
            ["winnerPlayerIndex"] = 0,
            ["reasonKey"] = "win.royal_castle_break",
        };

        var result = new RuntimeEventCursor().Accept(envelope);

        Assert.That(result.Accepted, Is.True);
    }

    [Test]
    public void RejectsInvalidGameOverBeforeAdvancingCursor()
    {
        var envelope = Event(1, null, "GAME_OVER");
        envelope.Phase = "ACTION";
        envelope.ReasonKey = "win.royal_castle_break";
        envelope.Data = new Dictionary<string, object?>
        {
            ["winnerPlayerIndex"] = 0,
            ["reasonKey"] = "win.royal_castle_break",
        };

        var cursor = new RuntimeEventCursor();
        var rejected = cursor.Accept(envelope);

        Assert.Multiple(() =>
        {
            Assert.That(rejected.Accepted, Is.False);
            Assert.That(rejected.ReasonKey, Is.EqualTo("event.game_over_phase_invalid"));
            Assert.That(cursor.LastEventId, Is.Null);
        });
    }

    [Test]
    public void RejectsGameOverWhenRootReasonDiffersFromDataReason()
    {
        var envelope = Event(1, null, "GAME_OVER");
        envelope.Phase = "OVER";
        envelope.ReasonKey = "win.royal_castle_break";
        envelope.Data = new Dictionary<string, object?>
        {
            ["winnerPlayerIndex"] = 0,
            ["reasonKey"] = "win.deck_cycles",
        };

        var result = new RuntimeEventCursor().Accept(envelope);

        Assert.That(result.ReasonKey, Is.EqualTo("event.game_over_reason_mismatch"));
    }

    private static RuntimeEventEnvelope Event(long number, string? parent, string type)
    {
        return new RuntimeEventEnvelope
        {
            EventId = "evt_" + number.ToString("D12"),
            ParentEventId = parent,
            Type = type,
            Turn = 1,
            Phase = "START",
            SnapshotRevision = 0,
            TargetIds = new[] { "castle" },
            Data = new Dictionary<string, object?>(),
        };
    }
}
}
