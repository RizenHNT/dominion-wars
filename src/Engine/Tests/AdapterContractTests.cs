using System;
using DominionWars.Adapters;
using DominionWars.Engine.Events;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class AdapterContractTests
{
    [Test]
    public void SnapshotProjectionPopulatesRequiredRuntimeKitFields()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var snapshot = EngineProjectionAdapter.ToSnapshot(state, "match_test", 3, "ACTION");
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ContractVersion, Is.EqualTo(1));
            Assert.That(snapshot.MatchId, Is.EqualTo("match_test"));
            Assert.That(snapshot.Turn, Is.EqualTo(3));
            Assert.That(snapshot.Phase, Is.EqualTo("ACTION"));
            Assert.That(snapshot.Players, Has.Count.EqualTo(2));
            Assert.That(snapshot.Castle, Is.Not.Null);
            Assert.That(snapshot.LegalActions, Is.Not.Null);
        });
    }

    [Test]
    public void DamageEventMapsToApprovedUiEventNameAndStableIds()
    {
        var data = new System.Collections.Generic.Dictionary<string, object?>
        {
            ["source"] = 2L,
            ["target"] = 3L,
            ["amount"] = 4,
        };
        var gameEvent = new GameEvent(2, 1, "DAMAGE_DEALT", data);
        var projected = EngineProjectionAdapter.ToEvent(gameEvent, 4, "ACTION");
        Assert.Multiple(() =>
        {
            Assert.That(projected.EventId, Is.EqualTo("evt_000000000002"));
            Assert.That(projected.ParentEventId, Is.EqualTo("evt_000000000001"));
            Assert.That(projected.Type, Is.EqualTo("DAMAGE_APPLIED"));
            Assert.That(projected.SourceId, Is.EqualTo("entity_000000000002"));
            Assert.That(projected.TargetIds, Does.Contain("entity_000000000003"));
            Assert.That(projected.Amount, Is.EqualTo(4));
        });
    }

    [Test]
    public void ProjectedChildCanTraverseToProjectedRoot()
    {
        var game = new EffectTestFixture();
        game.Apply(
            DominionWars.Engine.Effects.EffectNames.Damage,
            "ENEMY_MINION",
            2,
            selectedTargetId: game.Enemy.InstanceId);
        var root = game.State.Events.Items[0];
        var child = game.State.Events.Items[1];
        var projectedRoot = EngineProjectionAdapter.ToEvent(root, 4, "ACTION");
        var projectedChild = EngineProjectionAdapter.ToEvent(child, 4, "ACTION");
        Assert.Multiple(() =>
        {
            Assert.That(projectedRoot.ParentEventId, Is.Null);
            Assert.That(projectedRoot.Type, Is.EqualTo("CARD_PLAYED"));
            Assert.That(projectedChild.ParentEventId, Is.EqualTo(projectedRoot.EventId));
        });
    }

    [Test]
    public void UnmappedInternalEventFailsClosed()
    {
        var gameEvent = new GameEvent(1, null, "BUFF_APPLIED");
        Assert.Throws<NotSupportedException>(() =>
            EngineProjectionAdapter.ToEvent(gameEvent, 1, "ACTION"));
    }

    [TestCase(null)]
    [TestCase(0)]
    [TestCase(2)]
    public void MissingOrIncompatibleContractVersionRejectsStartup(int? version)
    {
        Assert.Throws<StartupRejectException>(() => ContractVersionGuard.Validate(version));
    }

    [Test]
    public void ExpectedContractVersionIsAccepted()
    {
        Assert.DoesNotThrow(() => ContractVersionGuard.Validate(1));
    }
}
}
