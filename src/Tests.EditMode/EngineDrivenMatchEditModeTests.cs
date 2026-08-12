#if UNITY_INCLUDE_TESTS
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Tests.EditMode
{

public sealed class EngineDrivenMatchEditModeTests
{
    [Test]
    public void EngineToAdapterMatchSmokeReachesGameOver()
    {
        var leaderDefinition = new CardDefinition(
            "smoke_leader", "Smoke Leader", 2, 4, isMinion: true, isLeader: true,
            vulnerabilities: new[] { EffectNames.Damage });
        var minionDefinition = new CardDefinition("smoke_minion", "Smoke Minion", 1, 3, isMinion: true);
        var state = new GameState(
            new PlayerState(0, 20),
            new PlayerState(1, 20),
            cardLibrary: new[] { leaderDefinition, minionDefinition });
        var ownLeader = new CardInstance(1, 0, leaderDefinition) { IsLeaderEntity = true };
        var enemyLeader = new CardInstance(2, 1, leaderDefinition) { IsLeaderEntity = true };
        var enemyMinion = new CardInstance(3, 1, minionDefinition);
        state.Players[0].Field.Add(ownLeader);
        state.Players[1].Field.Add(enemyLeader);
        state.Players[1].Field.Add(enemyMinion);

        var dispatcher = EffectDispatcher.CreateDefault(new EffectRuntime(state));
        var root = state.Events.Append("CARD_PLAYED");
        var context = new EffectContext(0, root.EventId, ownLeader, playedCard: ownLeader);
        dispatcher.Apply(new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 1), context);
        Assert.That(enemyMinion.Health, Is.EqualTo(2));

        dispatcher.Apply(new EffectSpec(EffectNames.Damage, "ENEMY_PLAYER", 20), context);
        Assert.That(state.WinnerPlayerIndex, Is.EqualTo(0));

        var snapshot = EngineProjectionAdapter.ToSnapshot(state, "editmode-smoke", 1, "OVER");
        var events = EngineProjectionAdapter.ToEvents(state.Events.Items, 1, "OVER");
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Phase, Is.EqualTo("OVER"));
            Assert.That(events.Last().Type, Is.EqualTo("GAME_OVER"));
            Assert.That(events.Select(item => item.EventId).Distinct().Count(), Is.EqualTo(events.Count));
            Assert.That(events.All(item => item.Turn == 1 && item.Phase == "OVER"), Is.True);
        });
    }
}
}
#endif
