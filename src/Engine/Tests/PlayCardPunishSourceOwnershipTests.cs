using System;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Rules;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// An accepted card is already being played while its punishment is paid.
/// Random hand discard must not also discard that resolving source. Reproduces
/// the sea_tide DISCARD_OPP_RANDOM response through the normal advertised phase
/// route, including the previously duplicated minion/spell ownership cases.
/// </summary>
[TestFixture]
public sealed class PlayCardPunishSourceOwnershipTests
{
    [TestCase("MINION")]
    [TestCase("SPELL")]
    [TestCase("CHANT")]
    public void RandomDiscardResponseCannotDiscardTheAcceptedPlaySource(string kind)
    {
        var source = Source(kind);
        var fixture = Create(source, DiscardResponse());

        var result = PlayAdvertised(fixture, source);

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(fixture.Policy.Calls, Is.EqualTo(1));
            Assert.That(fixture.Policy.PendingSourcesAbsentFromHand, Is.All.True,
                "during payment, the resolving source is not an opponent hand-discard candidate");
            Assert.That(fixture.Policy.PendingSourcesAbsentFromFieldAndGraveyard, Is.All.True,
                "a source is not summoned or discarded before the payment response finishes");
            Assert.That(fixture.State.GetPlayer(0).Hand, Does.Not.Contain(source));
            Assert.That(fixture.State.GetPlayer(0).Graveyard.Count(c => ReferenceEquals(c, source)),
                Is.EqualTo(kind == "SPELL" ? 1 : 0));
            Assert.That(fixture.State.GetPlayer(0).Field.Count(c => ReferenceEquals(c, source)),
                Is.EqualTo(kind == "SPELL" ? 0 : 1));
            Assert.That(fixture.State.GetPlayer(0).TotalDiscarded, Is.Zero,
                "no other hand card exists, so the response must discard zero cards");
            Assert.That(fixture.State.GetPlayer(0).PunishDeltaThisTurn,
                Is.EqualTo(kind == "CHANT" ? 0 : 1),
                "ordinary source effects resolve exactly once; chanting effects remain delayed");
            Assert.That(source.ChantRemaining, Is.EqualTo(kind == "CHANT" ? 1 : 0));
            Assert.That(fixture.State.Events.Items.Count(e => e.EventType == "PUNISH_TRIGGERED"),
                Is.EqualTo(1));
        });
        AssertUniqueZones(fixture.State);

        if (kind == "CHANT")
        {
            ExecuteAdvertised(fixture, LegalActionGenerator.EndTurn);
            ExecuteAdvertised(fixture, TurnAction.DiscardComplete);
            Assert.Multiple(() =>
            {
                Assert.That(fixture.State.GetPlayer(0).Field, Does.Not.Contain(source));
                Assert.That(fixture.State.GetPlayer(0).Graveyard.Count(c => ReferenceEquals(c, source)),
                    Is.EqualTo(1));
                Assert.That(fixture.State.Events.Items.Count(e => e.EventType == "PUNISH_DELTA_APPLIED"
                    && e.Data.TryGetValue("player", out var player) && Equals(player, 0)
                    && e.Data.TryGetValue("amount", out var amount) && Equals(amount, 1)), Is.EqualTo(1),
                    "the deferred effect resolves once before the normal handoff resets temporary counters");
                Assert.That(fixture.State.GetPlayer(0).PunishDeltaThisTurn, Is.Zero,
                    "the outgoing player's temporary punishment modifier resets at handoff");
            });
            AssertUniqueZones(fixture.State);
        }
    }

    [Test]
    public void RandomDiscardStillDiscardsAnotherHandCard()
    {
        var source = Source("MINION");
        var fixture = Create(source, DiscardResponse());
        var other = new CardInstance(3, 0, new CardDefinition("other", "Other"));
        fixture.State.GetPlayer(0).Hand.Add(other);

        Assert.That(PlayAdvertised(fixture, source).Accepted, Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.State.GetPlayer(0).Graveyard, Is.EqualTo(new[] { other }));
            Assert.That(fixture.State.GetPlayer(0).Field, Is.EqualTo(new[] { source }));
            Assert.That(fixture.State.GetPlayer(0).Hand, Is.Empty);
            Assert.That(fixture.State.GetPlayer(0).TotalDiscarded, Is.EqualTo(1));
            Assert.That(fixture.State.GetPlayer(0).PunishDeltaThisTurn, Is.EqualTo(1));
        });
        AssertUniqueZones(fixture.State);
    }

    [Test]
    public void NestedPaymentsKeepEveryResolvingSourceOutOfBothHands()
    {
        var source = Source("MINION");
        var firstResponse = new CardInstance(2, 1, new CardDefinition(
            "first_response", "First response", type: "SPELL", punishActivatable: true,
            punishCost: 1, tags: new[] { "first_response" },
            punishEffects: new[] { new EffectSpec(EffectNames.DiscardOppRandom, amount: 1) }));
        var fixture = Create(source, firstResponse, cap: 0);
        var nested = new CardInstance(3, 0, new CardDefinition(
            "nested_response", "Nested response", type: "SPELL", punishActivatable: true,
            tags: new[] { "nested_response" },
            punishEffects: new[] { new EffectSpec(EffectNames.DiscardOppRandom, amount: 1) }));
        fixture.State.GetPlayer(0).Deck.Add(nested);

        Assert.That(PlayAdvertised(fixture, source).Accepted, Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Policy.Calls, Is.EqualTo(2));
            Assert.That(fixture.Policy.PendingSourcesAbsentFromHand, Is.All.True);
            Assert.That(fixture.Policy.PendingSourcesAbsentFromFieldAndGraveyard, Is.All.True);
            Assert.That(fixture.State.GetPlayer(0).Field, Is.EqualTo(new[] { source }));
            Assert.That(fixture.State.GetPlayer(0).Graveyard, Is.EqualTo(new[] { nested }));
            Assert.That(fixture.State.GetPlayer(1).Graveyard, Is.EqualTo(new[] { firstResponse }));
            Assert.That(fixture.State.GetPlayer(0).TotalDiscarded, Is.Zero);
            Assert.That(fixture.State.GetPlayer(1).TotalDiscarded, Is.Zero);
            Assert.That(fixture.State.GetPlayer(0).PunishDeltaThisTurn, Is.EqualTo(1));
        });
        AssertUniqueZones(fixture.State);
    }

    [Test]
    public void InvalidTargetDoesNotRemoveTheSourceOrPayPunishment()
    {
        var source = new CardInstance(1, 0, new CardDefinition(
            "targeted", "Targeted", punish: 1,
            onPlayEffects: new[] { new EffectSpec(EffectNames.Damage, "FRIENDLY_MINION", amount: 1) }));
        var fixture = Create(source, DiscardResponse());
        var target = new CardInstance(3, 0, new CardDefinition("target", "Target", 1, 5, isMinion: true));
        fixture.State.GetPlayer(0).Field.Add(target);
        var advertised = fixture.Flow.GetLegalActions(fixture.State, 0).Single(a =>
            a.Type == LegalActionGenerator.PlayCard && a.SourceId == source.InstanceId);
        var result = fixture.Router.Execute(fixture.State, new GameActionRequest(
            0, advertised.Type, "play_" + source.InstanceId,
            advertised.SourceId, targetId: "entity_000000000999"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonKey, Is.EqualTo("action.invalid_target"));
            Assert.That(fixture.State.GetPlayer(0).Hand, Does.Contain(source));
            Assert.That(fixture.State.GetPlayer(1).Deck, Has.Count.EqualTo(1));
            Assert.That(fixture.Policy.Calls, Is.Zero);
            Assert.That(target.Health, Is.EqualTo(5));
            Assert.That(fixture.State.Events.Items.Any(e => e.EventType == "CARD_PLAYED"), Is.False);
        });
        AssertUniqueZones(fixture.State);
    }

    [Test]
    public void InvalidConvertedDiscardSelectionDoesNotRemoveTheSource()
    {
        var source = Source("MINION");
        var fixture = Create(source, DiscardResponse());
        fixture.State.GetPlayer(0).PunishToSelfDiscardThisTurn = true;
        var other = new CardInstance(3, 0, new CardDefinition("other", "Other"));
        fixture.State.GetPlayer(0).Hand.Add(other);
        var action = fixture.Flow.GetLegalActions(fixture.State, 0).Single(a =>
            a.Type == LegalActionGenerator.PlayCard && a.SourceId == source.InstanceId);

        var result = fixture.Router.Execute(fixture.State, new GameActionRequest(
            0, action.Type, action.ActionId, action.SourceId,
            selectedEntityIds: new[] { source.InstanceId }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonKey, Is.EqualTo("action.invalid_discard_selection"));
            Assert.That(fixture.State.GetPlayer(0).Hand, Is.EquivalentTo(new[] { source, other }));
            Assert.That(fixture.State.GetPlayer(0).Graveyard, Is.Empty);
            Assert.That(fixture.State.GetPlayer(1).Deck, Has.Count.EqualTo(1));
            Assert.That(fixture.Policy.Calls, Is.Zero);
            Assert.That(fixture.State.GetPlayer(0).PunishDeltaThisTurn, Is.Zero);
        });
        AssertUniqueZones(fixture.State);
    }

    [Test]
    public void WinningPaymentResponseRetiresTheUnresolvedSourceExactlyOnce()
    {
        var source = Source("MINION");
        var response = new CardInstance(2, 1, new CardDefinition(
            "winning_response", "Winning response", type: "SPELL", punishActivatable: true,
            punishEffects: new[] { new EffectSpec(EffectNames.WinGame) }));
        var fixture = Create(source, response);
        AddLeaders(fixture.State);

        Assert.That(PlayAdvertised(fixture, source).Accepted, Is.True);

        AssertRetiredWithoutResolving(fixture, source);
        Assert.That(fixture.Policy.Calls, Is.EqualTo(1));
    }

    [Test]
    public void WinningConvertedDiscardHookRetiresTheUnresolvedSourceExactlyOnce()
    {
        var source = Source("MINION");
        var fixture = Create(source, DiscardResponse());
        fixture.State.GetPlayer(0).PunishToSelfDiscardThisTurn = true;
        AddLeaders(fixture.State);
        var other = new CardInstance(3, 0, new CardDefinition("other", "Other"));
        fixture.State.GetPlayer(0).Hand.Add(other);
        fixture.State.GetPlayer(1).Field.Add(new CardInstance(4, 1, new CardDefinition(
            "discard_hook", "Discard hook", 1, 2, isMinion: true,
            onOpponentDiscardEffects: new[] { new EffectSpec(EffectNames.WinGame) })));
        var action = fixture.Flow.GetLegalActions(fixture.State, 0).Single(a =>
            a.Type == LegalActionGenerator.PlayCard && a.SourceId == source.InstanceId);

        var result = fixture.Router.Execute(fixture.State, new GameActionRequest(
            0, action.Type, action.ActionId, action.SourceId,
            selectedEntityIds: new[] { other.InstanceId }));

        Assert.That(result.Accepted, Is.True);
        AssertRetiredWithoutResolving(fixture, source);
        Assert.Multiple(() =>
        {
            Assert.That(fixture.State.GetPlayer(0).Graveyard.Count(c => ReferenceEquals(c, other)), Is.EqualTo(1));
            Assert.That(fixture.State.GetPlayer(0).TotalDiscarded, Is.EqualTo(1));
            Assert.That(fixture.Policy.Calls, Is.Zero);
        });
    }

    private static void AssertRetiredWithoutResolving(Fixture fixture, CardInstance source)
    {
        Assert.Multiple(() =>
        {
            Assert.That(fixture.State.WinnerPlayerIndex, Is.EqualTo(1));
            Assert.That(fixture.State.GetPlayer(0).Hand, Does.Not.Contain(source));
            Assert.That(fixture.State.GetPlayer(0).Field, Does.Not.Contain(source));
            Assert.That(fixture.State.GetPlayer(0).Graveyard.Count(c => ReferenceEquals(c, source)), Is.EqualTo(1));
            Assert.That(fixture.State.GetPlayer(0).PunishDeltaThisTurn, Is.Zero);
            Assert.That(fixture.State.Events.Items.Any(e => e.EventType == "MINION_SUMMONED"
                && e.Data.TryGetValue("target", out var id) && Equals(id, source.InstanceId)), Is.False);
        });
        AssertUniqueZones(fixture.State);
    }

    private static CardInstance Source(string kind)
    {
        var effects = new[] { new EffectSpec(EffectNames.AddSelfPunishTurn, amount: 1) };
        return new CardInstance(1, 0, new CardDefinition(
            "ordinary_" + kind, "Ordinary " + kind, attack: kind == "MINION" ? 2 : 0,
            health: kind == "MINION" ? 2 : 0, isMinion: kind == "MINION",
            type: kind == "MINION" ? "MINION" : "SPELL", punish: 1,
            tags: new[] { "source" }, onPlayEffects: kind == "CHANT" ? null : effects,
            chant: kind == "CHANT" ? 1 : 0, chantEffects: kind == "CHANT" ? effects : null));
    }

    private static CardInstance DiscardResponse() => new CardInstance(2, 1, new CardDefinition(
        "discard_response", "Discard response", type: "SPELL", punishActivatable: true,
        punishEffects: new[] { new EffectSpec(EffectNames.DiscardOppRandom, amount: 1) }));

    private static Fixture Create(CardInstance source, CardInstance response, int cap = 1)
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20),
            rules: new MatchRules(maxPunishResponsesPerRound: cap));
        state.GetPlayer(0).Deck.Add(source);
        state.GetPlayer(1).Deck.Add(response);
        var flow = TurnFlow.CreateDefault();
        var policy = new ObservingResponsePolicy(source);
        var fixture = new Fixture(state, flow,
            TurnActionRouter.CreateDefault(flow, punishResponses: policy), policy);
        flow.Advance(state, 0);
        ExecuteAdvertised(fixture, TurnAction.SkipAmbush);
        Assert.That(state.GetPlayer(0).Hand, Does.Contain(source));
        return fixture;
    }

    private static GameActionResult PlayAdvertised(Fixture fixture, CardInstance source)
    {
        var action = fixture.Flow.GetLegalActions(fixture.State, 0).Single(a =>
            a.Type == LegalActionGenerator.PlayCard && a.SourceId == source.InstanceId);
        return fixture.Router.Execute(fixture.State, new GameActionRequest(
            0, action.Type, action.ActionId, action.SourceId));
    }

    private static void ExecuteAdvertised(Fixture fixture, string type)
    {
        var actor = fixture.State.CurrentPlayerIndex;
        var action = fixture.Flow.GetLegalActions(fixture.State, actor).Single(a => a.Type == type);
        var result = fixture.Router.Execute(fixture.State, new GameActionRequest(
            actor, action.Type, action.ActionId, action.SourceId));
        Assert.That(result.Accepted, Is.True, result.ReasonKey);
    }

    private static void AddLeaders(GameState state)
    {
        foreach (var player in state.Players)
        {
            player.LeaderZone.Add(new CardInstance(100 + player.PlayerIndex, player.PlayerIndex,
                new CardDefinition("leader_" + player.PlayerIndex, "Leader", isLeader: true))
            { IsLeaderEntity = true });
        }
    }

    private static void AssertUniqueZones(GameState state)
    {
        var instances = state.Players.SelectMany(player => new[]
        {
            player.Hand, player.Deck, player.Field, player.LeaderZone, player.AmbushZone,
            player.Graveyard, player.CommitQueue, player.CloudStack,
        }).SelectMany(zone => zone).ToArray();
        Assert.That(instances.GroupBy(c => c.InstanceId).Where(g => g.Count() > 1), Is.Empty,
            "each card instance must occur exactly once across all persistent player zones");
    }

    private sealed class Fixture
    {
        public Fixture(GameState state, TurnFlow flow, TurnActionRouter router, ObservingResponsePolicy policy)
        { State = state; Flow = flow; Router = router; Policy = policy; }
        public GameState State { get; }
        public TurnFlow Flow { get; }
        public TurnActionRouter Router { get; }
        public ObservingResponsePolicy Policy { get; }
    }

    private sealed class ObservingResponsePolicy : IPunishResponsePolicy
    {
        private readonly List<CardInstance> _pending;
        public ObservingResponsePolicy(CardInstance source) => _pending = new List<CardInstance> { source };
        public int Calls { get; private set; }
        public List<bool> PendingSourcesAbsentFromHand { get; } = new List<bool>();
        public List<bool> PendingSourcesAbsentFromFieldAndGraveyard { get; } = new List<bool>();
        public PunishResponseDecision Decide(GameState state, CardInstance card, int effectivePunish, int chainDepth)
        {
            Calls++;
            PendingSourcesAbsentFromHand.Add(_pending.All(source =>
                !state.GetPlayer(source.OwnerPlayerIndex).Hand.Contains(source)));
            PendingSourcesAbsentFromFieldAndGraveyard.Add(_pending.All(source =>
                !state.GetPlayer(source.OwnerPlayerIndex).Field.Contains(source)
                && !state.GetPlayer(source.OwnerPlayerIndex).Graveyard.Contains(source)));
            _pending.Add(card);
            return PunishResponseDecision.Accept();
        }
    }
}
}
