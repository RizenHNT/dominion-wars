using System.Collections.Generic;
using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class PlayCardActionHandlerTests
{
    [Test]
    public void PlayMinionPaysPunishSummonsAndResolvesSelectedCastleDamage()
    {
        var state = CreateStateInActionPhase(out var flow, out var router);
        state.CastleEnabled = true;
        state.CastleHealth = 10;
        var definition = new CardDefinition(
            "minion",
            "Minion",
            attack: 2,
            health: 3,
            isMinion: true,
            punish: 1,
            onPlayEffects: new[] { new EffectSpec(EffectNames.Damage, "ENEMY_TARGET", 2) });
        var card = new CardInstance(10, 0, definition);
        state.GetPlayer(0).Hand.Add(card);
        state.GetPlayer(1).Deck.Add(new CardInstance(20, 1, new CardDefinition("drawn", "Drawn")));

        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.PlayCard,
            "play_10",
            10,
            "core:shared_castle"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.GetPlayer(0).Hand, Is.Empty);
            Assert.That(state.GetPlayer(0).Field.Single(), Is.SameAs(card));
            Assert.That(card.SummonedThisTurn, Is.True);
            Assert.That(state.GetPlayer(1).Hand, Has.Count.EqualTo(1));
            Assert.That(state.GetPlayer(1).PunishDrawnThisTurn, Is.EqualTo(1));
            Assert.That(state.CastleHealth, Is.EqualTo(8));
            Assert.That(state.Events.Items.Any(item => item.EventType == "PHASE_CHANGED"), Is.True);
            Assert.That(state.Events.Items.Any(item => item.EventType == "CARD_PLAYED"), Is.True);
            Assert.That(state.Events.Items.Any(item => item.EventType == "MINION_SUMMONED"), Is.True);
            Assert.That(state.Events.Items.Any(item => item.EventType == "CASTLE_DAMAGED"), Is.True);
        });
    }

    [Test]
    public void PunishAboveRemainingDeckFizzlesConsumesTagsAndForcesDiscard()
    {
        var state = CreateStateInActionPhase(out var flow, out var router);
        state.CastleEnabled = true;
        state.CastleHealth = 10;
        var definition = new CardDefinition(
            "fizzle",
            "Fizzle",
            punish: 2,
            tags: new[] { "burst" },
            onPlayEffects: new[] { new EffectSpec(EffectNames.Damage, "ENEMY_TARGET", 7) });
        var card = new CardInstance(11, 0, definition);
        state.GetPlayer(0).Hand.Add(card);
        state.GetPlayer(1).Deck.Add(new CardInstance(21, 1, new CardDefinition("only", "Only")));

        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.PlayCard,
            sourceEntityId: 11,
            targetId: "core:shared_castle"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.GetPlayer(0).Graveyard.Single(), Is.SameAs(card));
            Assert.That(state.GetPlayer(0).UsedTags, Contains.Item("burst"));
            Assert.That(state.GetPlayer(1).Deck, Has.Count.EqualTo(1));
            Assert.That(state.GetPlayer(1).Hand, Is.Empty);
            Assert.That(state.CastleHealth, Is.EqualTo(10));
            Assert.That(state.EndTurnRequested, Is.True);
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Discard));
            Assert.That(state.Events.Items.Any(item => item.EventType == "CASTLE_DAMAGED"), Is.False);
        });
    }

    [Test]
    public void FizzleDoesNotRequireTargetBecauseEffectsWillNotResolve()
    {
        var state = CreateStateInActionPhase(out _, out var router);
        var card = new CardInstance(17, 0, new CardDefinition(
            "fizzle_targeted",
            "Fizzle Targeted",
            punish: 1,
            onPlayEffects: new[] { new EffectSpec(EffectNames.Damage, "ENEMY_TARGET", 2) }));
        state.GetPlayer(0).Hand.Add(card);

        var result = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: 17));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.GetPlayer(0).Graveyard.Single(), Is.SameAs(card));
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Discard));
        });
    }

    [Test]
    public void PunishEqualToRemainingDeckDrawsAllAndKeepsActionPhase()
    {
        var state = CreateStateInActionPhase(out _, out var router);
        var definition = new CardDefinition("spell", "Spell", punish: 2);
        var card = new CardInstance(12, 0, definition);
        state.GetPlayer(0).Hand.Add(card);
        state.GetPlayer(1).Deck.Add(new CardInstance(22, 1, new CardDefinition("a", "A")));
        state.GetPlayer(1).Deck.Add(new CardInstance(23, 1, new CardDefinition("b", "B")));

        var result = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: 12));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.GetPlayer(1).Deck, Is.Empty);
            Assert.That(state.GetPlayer(1).Hand, Has.Count.EqualTo(2));
            Assert.That(state.GetPlayer(0).Graveyard.Single(), Is.SameAs(card));
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
        });
    }

    [Test]
    public void MissingOrInvalidRequiredTargetDoesNotMutateState()
    {
        var state = CreateStateInActionPhase(out _, out var router);
        var definition = new CardDefinition(
            "targeted",
            "Targeted",
            onPlayEffects: new[] { new EffectSpec(EffectNames.Damage, "ENEMY_TARGET", 2) });
        var card = new CardInstance(13, 0, definition);
        state.GetPlayer(0).Hand.Add(card);
        var eventCount = state.Events.Count;

        var missing = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: 13));
        var invalid = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: 13, targetId: "entity_999999999999"));

        Assert.Multiple(() =>
        {
            Assert.That(missing.ReasonKey, Is.EqualTo("action.target_required"));
            Assert.That(invalid.ReasonKey, Is.EqualTo("action.invalid_target"));
            Assert.That(state.GetPlayer(0).Hand.Single(), Is.SameAs(card));
            Assert.That(state.GetPlayer(0).Graveyard, Is.Empty);
            Assert.That(state.Events.Count, Is.EqualTo(eventCount));
        });
    }

    [Test]
    public void LegalActionsHideAmbushAndInactivePunishCardsDuringActionPhase()
    {
        var state = CreateStateInActionPhase(out var flow, out _);
        state.GetPlayer(0).Hand.Add(new CardInstance(14, 0, new CardDefinition(
            "ambush", "Ambush", type: "AMBUSH")));
        state.GetPlayer(0).Hand.Add(new CardInstance(15, 0, new CardDefinition(
            "punish", "Punish", type: "PUNISH", punishActivatable: true, punishCost: 2)));
        state.GetPlayer(0).Hand.Add(new CardInstance(16, 0, new CardDefinition(
            "normal", "Normal")));

        var plays = flow.GetLegalActions(state, 0)
            .Where(item => item.Type == LegalActionGenerator.PlayCard)
            .Select(item => item.SourceId)
            .ToArray();

        Assert.That(plays, Is.EqualTo(new long?[] { 16 }));
    }

    [Test]
    public void WardTargetIsRejectedBeforeAnyCardOrEventMutation()
    {
        var state = CreateStateInActionPhase(out _, out var router);
        var source = new CardInstance(30, 0, new CardDefinition(
            "source", "Source", onPlayEffects: new[] { new EffectSpec(EffectNames.Damage, "ENEMY_TARGET", 2) }));
        var ward = new CardInstance(31, 1, new CardDefinition(
            "ward", "Ward", 1, 3, isMinion: true, keywords: new[] { "扰魔" }));
        state.GetPlayer(0).Hand.Add(source);
        state.GetPlayer(1).Field.Add(ward);
        var before = state.Events.Count;

        var result = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: 30, targetId: "entity_000000000031"));

        Assert.Multiple(() =>
        {
            Assert.That(result.ReasonKey, Is.EqualTo("action.invalid_target"));
            Assert.That(state.GetPlayer(0).Hand.Single(), Is.SameAs(source));
            Assert.That(ward.Health, Is.EqualTo(3));
            Assert.That(state.Events.Count, Is.EqualTo(before));
        });
    }

    [Test]
    public void PunishResponsePolicyCanActivateDrawnCardDuringOpponentTurn()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var flow = TurnFlow.CreateDefault();
        var policy = new FixedPunishPolicy(PunishResponseDecision.Accept());
        var router = new TurnActionRouter(flow, new ITurnActionHandler[]
        {
            new PlayCardActionHandler(punishResponses: policy),
        });
        flow.Advance(state, 0);
        router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush));
        var original = new CardInstance(40, 0, new CardDefinition("original", "Original", punish: 1));
        var response = new CardInstance(41, 1, new CardDefinition(
            "response",
            "Response",
            attack: 2,
            health: 2,
            isMinion: true,
            punishActivatable: true,
            punishCost: 0,
            punishCondition: "ALWAYS",
            punishEffects: new[] { new EffectSpec(EffectNames.Damage, "ENEMY_PLAYER", 2) }));
        state.GetPlayer(0).Hand.Add(original);
        state.GetPlayer(1).Deck.Add(response);

        var result = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: 40));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(policy.CallCount, Is.EqualTo(1));
            Assert.That(state.GetPlayer(1).Field.Single(), Is.SameAs(response));
            Assert.That(state.GetPlayer(0).Life, Is.EqualTo(18));
            Assert.That(state.Events.Items.Any(item => item.EventType == "PUNISH_TRIGGERED"), Is.True);
        });
    }

    [Test]
    public void UnsatisfiedPunishConditionDoesNotAskPolicyOrActivateCard()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var flow = TurnFlow.CreateDefault();
        var policy = new FixedPunishPolicy(PunishResponseDecision.Accept());
        var router = new TurnActionRouter(flow, new ITurnActionHandler[]
        {
            new PlayCardActionHandler(punishResponses: policy),
        });
        flow.Advance(state, 0);
        router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush));
        state.GetPlayer(0).Hand.Add(new CardInstance(50, 0, new CardDefinition("original", "Original", punish: 1)));
        var response = new CardInstance(51, 1, new CardDefinition(
            "response", "Response", punishActivatable: true, punishCondition: "ENEMY_MINIONS_GE_1"));
        state.GetPlayer(1).Deck.Add(response);

        router.Execute(state, new GameActionRequest(0, LegalActionGenerator.PlayCard, sourceEntityId: 50));

        Assert.Multiple(() =>
        {
            Assert.That(policy.CallCount, Is.Zero);
            Assert.That(state.GetPlayer(1).Hand.Single(), Is.SameAs(response));
            Assert.That(state.Events.Items.Any(item => item.EventType == "PUNISH_TRIGGERED"), Is.False);
        });
    }

    [Test]
    public void ConvertedPunishUsesExplicitDiscardSelectionAndDoesNotDrawOpponent()
    {
        var state = CreateStateInActionPhase(out _, out var router);
        state.GetPlayer(0).PunishToSelfDiscardThisTurn = true;
        var played = new CardInstance(60, 0, new CardDefinition("played", "Played", punish: 2));
        var first = new CardInstance(61, 0, new CardDefinition("first", "First"));
        var second = new CardInstance(62, 0, new CardDefinition("second", "Second"));
        state.GetPlayer(0).Hand.Add(played);
        state.GetPlayer(0).Hand.Add(first);
        state.GetPlayer(0).Hand.Add(second);
        state.GetPlayer(1).Deck.Add(new CardInstance(63, 1, new CardDefinition("draw", "Draw")));

        var missing = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: 60));
        var accepted = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.PlayCard,
            sourceEntityId: 60,
            selectedEntityIds: new long[] { 61, 62 }));

        Assert.Multiple(() =>
        {
            Assert.That(missing.ReasonKey, Is.EqualTo("action.discard_selection_required"));
            Assert.That(accepted.Accepted, Is.True);
            Assert.That(state.GetPlayer(0).Graveyard.Select(item => item.InstanceId), Is.EquivalentTo(new long[] { 60, 61, 62 }));
            Assert.That(state.GetPlayer(0).TotalDiscarded, Is.EqualTo(2));
            Assert.That(state.GetPlayer(1).Deck, Has.Count.EqualTo(1));
            Assert.That(state.GetPlayer(1).Hand, Is.Empty);
            Assert.That(state.Events.Items.Single(item => item.EventType == "CARD_PLAYED").Data["fizzle"], Is.False);
        });
    }

    [Test]
    public void PunishChainResponseCanFizzleWithoutTargetAndDoesNotEndActiveTurn()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(
            flow,
            punishResponses: new FixedPunishPolicy(PunishResponseDecision.Accept()));
        flow.Advance(state, 0);
        router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush));
        state.GetPlayer(0).Hand.Add(new CardInstance(64, 0, new CardDefinition("original", "Original", punish: 1)));
        var response = new CardInstance(65, 1, new CardDefinition(
            "response",
            "Response",
            punishActivatable: true,
            punishCost: 1,
            punishEffects: new[] { new EffectSpec(EffectNames.Damage, "ENEMY_TARGET", 5) }));
        state.GetPlayer(1).Deck.Add(response);

        var result = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: 64));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.GetPlayer(1).Graveyard.Single(), Is.SameAs(response));
            Assert.That(state.EndTurnRequested, Is.False);
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
        });
    }

    [Test]
    public void PunishDrawnLeaderManifestsAndRunsEnterAndPunishEffects()
    {
        var state = CreateStateInActionPhase(out _, out var router);
        state.GetPlayer(0).Hand.Add(new CardInstance(70, 0, new CardDefinition("original", "Original", punish: 1)));
        var leader = new CardInstance(71, 1, new CardDefinition(
            "leader",
            "Leader",
            isLeader: true,
            grantLife: 25,
            leaderEnterEffects: new[] { new EffectSpec(EffectNames.GainLife, "SELF_PLAYER", 2) },
            leaderPunishEffects: new[] { new EffectSpec(EffectNames.Damage, "ENEMY_PLAYER", 3) }));
        state.GetPlayer(1).Deck.Add(leader);

        var result = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: 70));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.GetPlayer(1).Leader, Is.SameAs(leader));
            Assert.That(leader.SummonedThisTurn, Is.False, "A non-minion leader has no summoning sickness flag.");
            Assert.That(state.GetPlayer(1).Life, Is.EqualTo(27));
            Assert.That(state.GetPlayer(0).Life, Is.EqualTo(17));
            Assert.That(state.Events.Items.Any(item => item.EventType == "LEADER_MANIFESTED"), Is.True);
        });
    }

    [Test]
    public void PunishDrawnMinionLeaderCannotAttackOnManifestTurn()
    {
        var state = CreateStateInActionPhase(out _, out var router);
        state.GetPlayer(0).Hand.Add(new CardInstance(80, 0, new CardDefinition("original", "Original", punish: 1)));
        var leader = new CardInstance(81, 1, new CardDefinition(
            "leader_minion", "Leader Minion", attack: 3, health: 5, isMinion: true, isLeader: true));
        state.GetPlayer(1).Deck.Add(leader);

        router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: 80));

        Assert.Multiple(() =>
        {
            Assert.That(leader.IsLeaderEntity, Is.True);
            Assert.That(leader.SummonedThisTurn, Is.True);
            Assert.That(leader.AttacksUsed, Is.Zero);
        });
    }

    private static GameState CreateStateInActionPhase(
        out TurnFlow flow,
        out TurnActionRouter router)
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        flow = TurnFlow.CreateDefault();
        router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        Assert.That(router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted, Is.True);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
        return state;
    }

    private sealed class FixedPunishPolicy : IPunishResponsePolicy
    {
        private readonly PunishResponseDecision _decision;

        public FixedPunishPolicy(PunishResponseDecision decision)
        {
            _decision = decision;
        }

        public int CallCount { get; private set; }

        public PunishResponseDecision Decide(
            GameState state,
            CardInstance card,
            int effectivePunish,
            int chainDepth)
        {
            CallCount++;
            return _decision;
        }
    }
}
}
