using System.Collections.Generic;
using System.Linq;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// P0 contract sweep.
/// P0-6: a SET_AMBUSH advertisement must never be atomically unsatisfiable for
/// the engine's own resolver. When 惩罚转弃牌 is active the resolver requires
/// exactly <c>EffectivePunish</c> distinct other hand cards
/// (AmbushActionHandler.ResolveConvertedCost), so the advertisement must be
/// withheld when the candidate pool cannot pay it. SKIP_AMBUSH stays advertised
/// so the AMBUSH phase never becomes unplayable.
/// P0-7: 扎根 / 疯长 growth has exactly one source of truth, the explicit
/// ADD_ROOT / ADD_RAMPANT effects in card data. The tag branch that used to
/// grant the same stacks a second time is gone; tags are still consumed.
/// </summary>
[TestFixture]
public sealed class P0ContractSweepTests
{
    [Test]
    public void AmbushWithConvertedCostIsNotAdvertisedWhenHandCannotPayIt()
    {
        var state = CreateAmbushState(out var flow, out _);
        var player = state.GetPlayer(0);
        player.PunishToSelfDiscardThisTurn = true;
        var veil = Ambush(200, punish: 2);
        player.Hand.Add(veil);

        var advertised = flow.GetLegalActions(state, 0);

        Assert.Multiple(() =>
        {
            Assert.That(
                advertised.Where(item => item.Type == TurnAction.SetAmbush),
                Is.Empty,
                "The only other hand card is the source card, which the resolver excludes, "
                + "so this advertisement could never be satisfied.");
            Assert.That(
                advertised.Where(item => item.Type == TurnAction.SkipAmbush),
                Has.Exactly(1).Items,
                "SKIP_AMBUSH is the designed recovery path and must still be advertised.");
        });
    }

    [Test]
    public void ResolverRejectsTheAmbushAdvertisementThatTheGuardWithholds()
    {
        var state = CreateStateInAmbushPhase(out _, out var router);
        var player = state.GetPlayer(0);
        player.PunishToSelfDiscardThisTurn = true;
        var veil = Ambush(210, punish: 2);
        var onlyOther = new CardInstance(211, 0, new CardDefinition("only_other", "Only Other"));
        player.Hand.Add(veil);
        player.Hand.Add(onlyOther);

        var result = router.Execute(state, new GameActionRequest(
            0, TurnAction.SetAmbush, "set_ambush_210", veil.InstanceId,
            selectedEntityIds: new[] { onlyOther.InstanceId }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False,
                "Premise of P0-6: hand.Count - 1 < punish is unsatisfiable for the resolver.");
            Assert.That(result.ReasonKey, Is.EqualTo("action.discard_selection_invalid"));
            Assert.That(player.AmbushZone, Is.Empty);
            Assert.That(player.Hand, Has.Exactly(2).Items);
        });
    }

    [Test]
    public void AmbushWithConvertedCostIsStillAdvertisedWithItsPayloadWhenPayable()
    {
        var state = CreateStateInAmbushPhase(out var flow, out _);
        var player = state.GetPlayer(0);
        player.PunishToSelfDiscardThisTurn = true;
        var veil = Ambush(220, punish: 2);
        var first = new CardInstance(221, 0, new CardDefinition("first", "First"));
        var second = new CardInstance(222, 0, new CardDefinition("second", "Second"));
        player.Hand.Add(veil);
        player.Hand.Add(first);
        player.Hand.Add(second);

        var action = flow.GetLegalActions(state, 0).Single(item => item.Type == TurnAction.SetAmbush);
        var candidates = (List<long>)action.Payload!["discardCandidateIds"]!;

        Assert.Multiple(() =>
        {
            Assert.That(action.SourceId, Is.EqualTo(veil.InstanceId));
            Assert.That(action.Payload!["discardRequired"], Is.EqualTo(2));
            Assert.That(candidates, Is.EquivalentTo(new[] { first.InstanceId, second.InstanceId }),
                "The source card itself is never a candidate, exactly as the resolver requires.");
        });
    }

    [Test]
    public void GrowthTagsGrantNothingWhileTheExplicitEffectsGrantExactlyOnce()
    {
        var state = CreateStateInActionPhase(out _, out var router);
        var player = state.GetPlayer(0);
        var card = new CardInstance(230, 0, new CardDefinition(
            "tagged_growth",
            "Tagged Growth",
            type: "SPELL",
            tags: new[] { "扎根", "疯长" },
            onPlayEffects: new[]
            {
                new EffectSpec(EffectNames.AddRoot, "SELF_PLAYER", 2),
                new EffectSpec(EffectNames.AddRampant, "SELF_PLAYER", 1),
            }));
        player.Hand.Add(card);

        var result = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: card.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(player.Graveyard, Does.Contain(card));
            Assert.That(player.RootStacks, Is.EqualTo(2),
                "ADD_ROOT 2 only: a 扎根 tag must not add a third stack.");
            Assert.That(player.RampantStacks, Is.EqualTo(1),
                "ADD_RAMPANT 1 only: a 疯长 tag must not add a second stack.");
            Assert.That(player.UsedTags, Does.Contain("扎根"));
            Assert.That(player.UsedTags, Does.Contain("疯长"),
                "Tags are still consumed so that the one-card-per-tag rule still throttles.");
        });
    }

    [Test]
    public void GrowthTagsStillThrottleOneCardPerTagPerTurn()
    {
        var state = CreateStateInActionPhase(out _, out var router);
        var player = state.GetPlayer(0);
        var first = new CardInstance(240, 0, new CardDefinition(
            "root_first", "Root First", type: "SPELL", tags: new[] { "扎根" }));
        player.Hand.Add(first);
        player.Deck.Add(new CardInstance(241, 0, new CardDefinition("deck_card", "Deck Card")));
        player.Deck.Add(new CardInstance(242, 0, new CardDefinition("deck_card_2", "Deck Card 2")));

        var accepted = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: first.InstanceId));
        var second = new CardInstance(243, 0, new CardDefinition(
            "root_second", "Root Second", type: "SPELL", tags: new[] { "扎根" }));
        player.Hand.Add(second);
        var blocked = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: second.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(accepted.Accepted, Is.True);
            Assert.That(blocked.Accepted, Is.False);
            Assert.That(blocked.ReasonKey, Is.EqualTo("action.tag_already_used"));
            Assert.That(player.Hand, Does.Contain(second));
            Assert.That(player.RootStacks, Is.Zero);
        });
    }

    private static GameState CreateStateInAmbushPhase(out TurnFlow flow, out TurnActionRouter router)
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        flow = TurnFlow.CreateDefault();
        router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        return state;
    }

    private static GameState CreateAmbushState(out TurnFlow flow, out TurnActionRouter router)
        => CreateStateInAmbushPhase(out flow, out router);

    private static GameState CreateStateInActionPhase(out TurnFlow flow, out TurnActionRouter router)
    {
        var state = CreateStateInAmbushPhase(out flow, out router);
        Assert.That(router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted, Is.True);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
        foreach (var id in new long[] { 250, 251, 252, 253 })
        {
            state.GetPlayer(1).Deck.Add(new CardInstance(id, 1, new CardDefinition("draw_" + id, "Draw")));
        }

        return state;
    }

    private static CardInstance Ambush(long id, int punish = 0, string kind = "NORMAL")
    {
        return new CardInstance(id, 0, new CardDefinition(
            "ambush_" + id,
            "Ambush " + id,
            type: "AMBUSH",
            punish: punish,
            tags: new[] { "ambush_sweep" },
            ambushKind: kind,
            ambushTrigger: "OPPONENT_PLAYS_CARD"));
    }
}
}
