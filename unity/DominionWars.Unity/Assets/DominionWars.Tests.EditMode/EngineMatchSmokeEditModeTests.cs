#if UNITY_INCLUDE_TESTS
using DominionWars.Adapters;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using NUnit.Framework;
using System.Linq;

namespace DominionWars.Unity.EditMode
{
    public sealed class EngineMatchSmokeEditModeTests
    {
        [Test]
        public void EffectWindowProjectsThroughAdapter()
        {
            var leader = new CardDefinition("smoke_leader", "Smoke Leader", 2, 4, isMinion: true, isLeader: true,
                vulnerabilities: new[] { EffectNames.Damage });
            var minion = new CardDefinition("smoke_minion", "Smoke Minion", 1, 3, isMinion: true);
            var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20),
                cardLibrary: new[] { leader, minion });
            var own = new CardInstance(1, 0, leader) { IsLeaderEntity = true };
            var enemyLeader = new CardInstance(2, 1, leader) { IsLeaderEntity = true };
            var enemy = new CardInstance(3, 1, minion);
            state.Players[0].Field.Add(own);
            state.Players[1].Field.Add(enemyLeader);
            state.Players[1].Field.Add(enemy);
            var dispatcher = EffectDispatcher.CreateDefault(new EffectRuntime(state));
            var root = state.Events.Append("CARD_PLAYED");
            var context = new EffectContext(0, root.EventId, own, playedCard: own);

            dispatcher.Apply(new EffectSpec(EffectNames.Damage, "ENEMY_MINION", 1), context);
            dispatcher.Apply(new EffectSpec(EffectNames.Damage, "ENEMY_PLAYER", 20), context);

            var snapshot = EngineProjectionAdapter.ToSnapshot(state, "unity-smoke", 1, "OVER");
            var events = EngineProjectionAdapter.ToEvents(state.Events.Items, 1, "OVER");
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.ContractVersion, Is.EqualTo(1));
                Assert.That(state.WinnerPlayerIndex, Is.EqualTo(0));
                Assert.That(events.Last().Type, Is.EqualTo("GAME_OVER"));
                Assert.That(events.Select(item => item.EventId).Distinct().Count(), Is.EqualTo(events.Count));
            });
        }
    }
}
#endif
