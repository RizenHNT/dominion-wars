using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DominionWars.Adapters;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class StressTests
{
    [Test]
    public void OneHundredEffectChainEntriesDoNotCrash()
    {
        var game = new EffectTestFixture();
        var effects = Enumerable.Range(0, 100)
            .Select(_ => new EffectSpec(EffectNames.Buff, "SELF", 1, "atk"))
            .ToArray();
        Assert.DoesNotThrow(() => game.Dispatcher.ApplyAll(effects, game.Context()));
        Assert.That(game.Source.Attack, Is.EqualTo(101));
    }

    [Test]
    public void TenNestedDispatchEntriesRemainStable()
    {
        var game = new EffectTestFixture();
        var context = game.Context();
        Assert.DoesNotThrow(() =>
        {
            for (var index = 0; index < 10; index++)
            {
                game.Dispatcher.Apply(new EffectSpec(EffectNames.GainLife, amount: 1), context);
            }
        });
        Assert.That(game.State.Players[0].Life, Is.EqualTo(30));
    }

    [Test]
    public void SnapshotJsonRoundTripIsStableForOneThousandIterations()
    {
        var snapshot = EngineProjectionAdapter.ToSnapshot(new GameState(), "stress", 2, "ACTION");
        var json = JsonSerializer.Serialize(snapshot);
        for (var index = 0; index < 1000; index++)
        {
            snapshot = JsonSerializer.Deserialize<SnapshotDto>(json)!;
            json = JsonSerializer.Serialize(snapshot);
        }
        Assert.That(snapshot.MatchId, Is.EqualTo("stress"));
        Assert.That(snapshot.Players, Has.Count.EqualTo(2));
    }

    [Test]
    public void OneHundredCardsMapWithoutLoss()
    {
        var player = new PlayerState(0);
        var definition = new CardDefinition("stress_card", "Stress Card", 1, 1, isMinion: true);
        for (var index = 1; index <= 100; index++)
        {
            player.Hand.Add(new CardInstance(index, 0, definition));
        }
        var state = new GameState(player, new PlayerState(1));
        var snapshot = EngineProjectionAdapter.ToSnapshot(state, "stress_cards", 0, "ACTION");
        Assert.That(snapshot.Players[0].Hand, Has.Count.EqualTo(100));
    }

    [Test]
    public void ConcurrentCardProjectionIsDeterministic()
    {
        var definition = new CardDefinition("parallel_card", "Parallel Card", 1, 1);
        var cards = Enumerable.Range(1, 100)
            .Select(index => new CardInstance(index, 0, definition))
            .ToArray();
        var mapped = new CardDto[100];
        Parallel.For(0, cards.Length, index =>
        {
            mapped[index] = EngineProjectionAdapter.ToCardDto(cards[index]);
        });
        Assert.That(mapped.Select(card => card.EntityId).Distinct().Count(), Is.EqualTo(100));
    }
}
}
