using System;
using DominionWars.Engine.Events;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class EventLogTests
{
    [Test]
    public void RootAndChildEventsHaveMonotonicIds()
    {
        var log = new EventLog();
        var root = log.Append("CARD_PLAYED");
        var child = log.Append("DAMAGE_DEALT", root.EventId);
        var secondRoot = log.Append("TURN_FORCE_ENDED");

        Assert.Multiple(() =>
        {
            Assert.That(root.EventId, Is.EqualTo(1));
            Assert.That(child.EventId, Is.EqualTo(2));
            Assert.That(secondRoot.EventId, Is.EqualTo(3));
            Assert.That(log.LastEventId, Is.EqualTo(3));
            Assert.That(log.IsRootEvent(root.EventId), Is.True);
            Assert.That(log.IsRootEvent(child.EventId), Is.False);
            Assert.That(log.IsRootEvent(secondRoot.EventId), Is.True);
        });
    }

    [Test]
    public void ParentMustAlreadyExist()
    {
        var log = new EventLog();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            log.Append("DAMAGE_DEALT", parentEventId: 1));
    }

    [Test]
    public void ParentIdMustBePositive()
    {
        var log = new EventLog();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                log.Append("DAMAGE_DEALT", parentEventId: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                log.Append("DAMAGE_DEALT", parentEventId: -1));
        });
    }

    [Test]
    public void RootEventQueryRejectsUnknownIds()
    {
        var log = new EventLog();
        log.Append("CARD_PLAYED");

        Assert.Multiple(() =>
        {
            Assert.That(log.IsRootEvent(0), Is.False);
            Assert.That(log.IsRootEvent(2), Is.False);
            Assert.That(log.IsRootEvent(-1), Is.False);
        });
    }
}
}
