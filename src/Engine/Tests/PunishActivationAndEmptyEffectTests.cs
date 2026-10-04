using System.IO;
using System.Linq;
using DominionWars.Data;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class PunishActivationAndEmptyEffectTests
{
    private static string DataRoot
    {
        get
        {
            var directory = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(directory)
                && !Directory.Exists(Path.Combine(directory, "data", "cards")))
            {
                directory = Directory.GetParent(directory)?.FullName;
            }

            return Path.Combine(directory!, "data");
        }
    }

    // RULES.md:57: printed punish alone does not grant the explicit punish-activation ability.
    [Test]
    public void PunishDrawLeavesOrdinaryPrintedPunishCardForItsNormalOnPlay()
    {
        var state = CreateActionState(out var flow, out var router, out var responsePolicy);
        var woodCards = CardCatalog.LoadDirectory(Path.Combine(DataRoot, "cards"));
        var response = new CardInstance(20, 1, woodCards.Cards["wood_bear"]);
        var friendly = new CardInstance(21, 1, new CardDefinition(
            "friendly_target", "Friendly Target", attack: 1, health: 2, isMinion: true));
        state.GetPlayer(1).Deck.Add(response);
        state.GetPlayer(1).Field.Add(friendly);
        for (var id = 100; id < 108; id++)
        {
            state.GetPlayer(0).Deck.Add(new CardInstance(id, 0, new CardDefinition("draw_" + id, "Draw")));
        }

        var feeder = new CardInstance(10, 0, new CardDefinition(
            "feeder",
            "Feeder",
            punish: 1,
            onPlayEffects: new[] { new EffectSpec(EffectNames.Draw, amount: 1) }));
        state.GetPlayer(0).Hand.Add(feeder);

        var feedResult = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: feeder.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(feedResult.Accepted, Is.True);
            Assert.That(responsePolicy.CallCount, Is.Zero,
                "printed punish alone must not activate a response window");
            Assert.That(state.GetPlayer(1).Hand, Does.Contain(response));
            Assert.That(state.GetPlayer(1).Field, Does.Not.Contain(response));
            Assert.That(response.PunishActivated, Is.False);
            Assert.That(state.GetPlayer(1).RootStacks, Is.Zero,
                "the normal on-play growth must not resolve during the punish window");
            Assert.That(state.Events.Items.Any(item => item.EventType == "PUNISH_TRIGGERED"), Is.False);
        });

        Assert.That(router.Execute(state, new GameActionRequest(0, LegalActionGenerator.EndTurn)).Accepted, Is.True);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Discard));
        Assert.That(router.Execute(state, new GameActionRequest(
            0, TurnAction.DiscardComplete, "discard_0_0")).Accepted, Is.True);
        Assert.That(state.CurrentPlayerIndex, Is.EqualTo(1));
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Ambush));
        Assert.That(router.Execute(state, new GameActionRequest(1, TurnAction.SkipAmbush)).Accepted, Is.True);

        var targetId = "entity_" + friendly.InstanceId.ToString("D12");
        var playAction = flow.GetLegalActions(state, 1).Single(action =>
            action.Type == LegalActionGenerator.PlayCard
            && action.SourceId == response.InstanceId
            && action.TargetId == friendly.InstanceId);
        var normalPlay = router.Execute(state, new GameActionRequest(
            1,
            LegalActionGenerator.PlayCard,
            playAction.ActionId,
            response.InstanceId,
            targetId));

        Assert.Multiple(() =>
        {
            Assert.That(normalPlay.Accepted, Is.True);
            Assert.That(state.GetPlayer(1).RootStacks, Is.EqualTo(2));
            Assert.That(state.GetPlayer(1).Field, Does.Contain(response));
            Assert.That(responsePolicy.CallCount, Is.Zero);
            Assert.That(state.Events.Items.Any(item =>
                item.EventType == "EFFECT_SKIPPED"
                && item.Data.TryGetValue("reasonKey", out var reason)
                && Equals(reason, "effect.no_effects")), Is.False);
        });
    }

    [Test]
    public void SuccessfulNonChantEmptyPlayEffectsEmitInternalAuditSkip()
    {
        var state = CreateActionState(out _, out var router, out _);
        var card = new CardInstance(30, 0, new CardDefinition("empty_spell", "Empty Spell", type: "SPELL"));
        state.GetPlayer(0).Hand.Add(card);

        var result = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: card.InstanceId));

        var play = state.Events.Items.Single(item => item.EventType == "CARD_PLAYED");
        var audit = state.Events.Items.Single(item =>
            item.EventType == "EFFECT_SKIPPED"
            && item.Data.TryGetValue("reasonKey", out var reason)
            && Equals(reason, "effect.no_effects"));
        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(audit.ParentEventId, Is.EqualTo(play.EventId));
            Assert.That(audit.Data["action"], Is.EqualTo("CARD_EFFECTS"));
            Assert.That(state.GetPlayer(0).Graveyard, Does.Contain(card));
        });
    }

    [Test]
    public void ChantAndNegatedPlayDoNotEmitEmptyEffectAudit()
    {
        var chantState = CreateActionState(out _, out var chantRouter, out _);
        var chant = new CardInstance(40, 0, new CardDefinition(
            "empty_chant", "Empty Chant", type: "SPELL", chant: 1));
        chantState.GetPlayer(0).Hand.Add(chant);
        var chantResult = chantRouter.Execute(chantState, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: chant.InstanceId));

        var negatedState = CreateActionState(out _, out var negatedRouter, out _);
        var negatedSpell = new CardInstance(41, 0, new CardDefinition(
            "negated_empty_spell", "Negated Empty Spell", type: "SPELL"));
        var negateAmbush = new CardInstance(42, 1, new CardDefinition(
            "negate_ambush",
            "Negate Ambush",
            type: "AMBUSH",
            ambushKind: "FOCUS",
            ambushTrigger: "OPPONENT_PLAYS_SPELL",
            ambushEffects: new[] { new EffectSpec(EffectNames.Negate) }));
        negatedState.GetPlayer(0).Hand.Add(negatedSpell);
        negatedState.GetPlayer(1).AmbushZone.Add(negateAmbush);
        var negatedResult = negatedRouter.Execute(negatedState, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: negatedSpell.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(chantResult.Accepted, Is.True);
            Assert.That(chant.ChantRemaining, Is.EqualTo(1));
            Assert.That(negatedResult.Accepted, Is.True);
            Assert.That(negatedState.GetPlayer(1).Graveyard, Does.Contain(negateAmbush));
            Assert.That(HasEmptyEffectsAudit(chantState), Is.False);
            Assert.That(HasEmptyEffectsAudit(negatedState), Is.False);
        });
    }

    [Test]
    public void WinnerDeclaredByTriggeredAmbushDoesNotEmitEmptyEffectAudit()
    {
        var state = CreateActionState(out _, out var router, out _);
        AddLeader(state, 0, 50);
        AddLeader(state, 1, 51);
        var spell = new CardInstance(52, 0, new CardDefinition("empty_spell", "Empty Spell", type: "SPELL"));
        var winningAmbush = new CardInstance(53, 1, new CardDefinition(
            "winning_ambush",
            "Winning Ambush",
            type: "AMBUSH",
            ambushKind: "FOCUS",
            ambushTrigger: "OPPONENT_PLAYS_SPELL",
            ambushEffects: new[] { new EffectSpec(EffectNames.WinGame) }));
        state.GetPlayer(0).Hand.Add(spell);
        state.GetPlayer(1).AmbushZone.Add(winningAmbush);

        var result = router.Execute(state, new GameActionRequest(
            0, LegalActionGenerator.PlayCard, sourceEntityId: spell.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.WinnerPlayerIndex, Is.EqualTo(1));
            Assert.That(HasEmptyEffectsAudit(state), Is.False);
        });
    }

    private static bool HasEmptyEffectsAudit(GameState state)
    {
        return state.Events.Items.Any(item =>
            item.EventType == "EFFECT_SKIPPED"
            && item.Data.TryGetValue("reasonKey", out var reason)
            && Equals(reason, "effect.no_effects"));
    }

    private static void AddLeader(GameState state, int ownerIndex, long instanceId)
    {
        var leader = new CardInstance(instanceId, ownerIndex, new CardDefinition(
            "test_leader_" + ownerIndex, "Test Leader", isLeader: true))
        {
            IsLeaderEntity = true,
        };
        state.GetPlayer(ownerIndex).LeaderZone.Add(leader);
    }

    private static GameState CreateActionState(
        out TurnFlow flow,
        out TurnActionRouter router,
        out AcceptPunishPolicy policy)
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        flow = TurnFlow.CreateDefault();
        policy = new AcceptPunishPolicy();
        router = TurnActionRouter.CreateDefault(flow, punishResponses: policy);
        flow.Advance(state, 0);
        Assert.That(router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted, Is.True);
        return state;
    }

    private sealed class AcceptPunishPolicy : IPunishResponsePolicy
    {
        public int CallCount { get; private set; }

        public PunishResponseDecision Decide(GameState state, CardInstance card, int effectivePunish, int chainDepth)
        {
            CallCount++;
            return PunishResponseDecision.Accept();
        }
    }
}
}
