using System.Linq;
using DominionWars.Engine.Effects;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class WoodCounterEffectTests
{
    [Test]
    public void AddRootRaisesTheSourcePlayersPublicCounter()
    {
        var game = new EffectTestFixture();

        game.Apply(EffectNames.AddRoot, amount: 2);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].RootStacks, Is.EqualTo(2));
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("ROOT_STACKS_ADDED"));
            Assert.That(game.State.Events.Items[^1].Data["amount"], Is.EqualTo(2));
            Assert.That(game.State.Events.Items[^1].Data["total"], Is.EqualTo(2));
        });
    }

    [Test]
    public void AddRampantIsCappedAtThreeLayersAndReportsTheAppliedDelta()
    {
        var game = new EffectTestFixture();
        game.State.Players[0].RampantStacks = 2;

        game.Apply(EffectNames.AddRampant, amount: 3);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].RampantStacks, Is.EqualTo(3));
            Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("RAMPANT_STACKS_ADDED"));
            Assert.That(game.State.Events.Items[^1].Data["amount"], Is.EqualTo(1));
            Assert.That(game.State.Events.Items[^1].Data["requested"], Is.EqualTo(3));
            Assert.That(game.State.Events.Items[^1].Data["capped"], Is.EqualTo(true));
        });
    }

    [Test]
    public void CounterActionsRejectNonPositiveAmountsWithoutMutation()
    {
        var game = new EffectTestFixture();

        game.Apply(EffectNames.AddRoot, amount: 0);
        game.Apply(EffectNames.AddRampant, amount: -1);

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].RootStacks, Is.Zero);
            Assert.That(game.State.Players[0].RampantStacks, Is.Zero);
            Assert.That(game.State.Events.Items.Count(item => item.EventType == "EFFECT_SKIPPED"), Is.EqualTo(2));
        });
    }
}
}
