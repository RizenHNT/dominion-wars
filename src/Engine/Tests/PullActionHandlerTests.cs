using System;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class PullActionHandlerTests
{
    [Test]
    public void AdvertisesOnePullPerCarrierForOnlyTheCloudTop()
    {
        var state = CreateActionState(out var flow, out _);
        var firstCarrier = AddCarrier(state, 10, "carrier_a");
        var secondCarrier = AddCarrier(state, 11, "carrier_b");
        state.GetPlayer(0).Field.Add(new CardInstance(12, 0, new CardDefinition(
            "ordinary",
            "Ordinary",
            isMinion: true,
            health: 2)));
        var lower = new CardInstance(20, 0, new CardDefinition("lower", "Lower"));
        var top = new CardInstance(21, 0, new CardDefinition("top", "Top"));
        state.GetPlayer(0).CloudStack.Add(lower);
        state.GetPlayer(0).CloudStack.Add(top);

        var actions = flow.GetLegalActions(state, 0)
            .Where(action => action.Type == LegalActionGenerator.Pull)
            .ToArray();
        var snapshot = RuntimeSnapshotProjection.ToSnapshot(
            state,
            "match_pull",
            0,
            0,
            flow);
        var runtimePull = snapshot.LegalActions
            .First(action => action.Type == LegalActionGenerator.Pull);

        Assert.Multiple(() =>
        {
            Assert.That(actions, Has.Length.EqualTo(2));
            Assert.That(actions.Select(action => action.SourceId),
                Is.EquivalentTo(new long?[] { firstCarrier.InstanceId, secondCarrier.InstanceId }));
            Assert.That(actions.Select(action => action.TargetReferenceId),
                Is.All.Null);
            Assert.That(actions.Select(action => action.TargetId),
                Is.All.EqualTo(top.InstanceId));
            Assert.That(runtimePull.TargetId, Is.TypeOf<long>());
            Assert.That(runtimePull.TargetId, Is.EqualTo(top.InstanceId));
            Assert.That(actions.Select(action => action.ActionId),
                Is.EquivalentTo(new[] { "pull_10_21", "pull_11_21" }));
        });
    }

    [Test]
    public void AdvertisesOneExplicitPullVariantPerFriendlyMinionTarget()
    {
        var state = CreateActionState(out var flow, out _);
        var carrier = AddCarrier(state, 22, "carrier");
        var secondTarget = AddCarrier(state, 23, "second_target");
        var machineAlpha = new CardInstance(25, 0, new CardDefinition(
            "machine_alpha",
            "Machine Alpha",
            attack: 8,
            health: 10,
            isMinion: true,
            isLeader: true,
            faction: "机械遗迹"))
        {
            IsLeaderEntity = true,
        };
        // Minion leaders are deliberately stored in Field by LeaderZoneFor;
        // they must still not become FRIENDLY_MINION pull recipients.
        state.GetPlayer(0).Field.Add(machineAlpha);
        var top = new CardInstance(24, 0, new CardDefinition(
            "targeted_pull",
            "Targeted Pull",
            pullEffects: new[]
            {
                new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 1, "both"),
            }));
        state.GetPlayer(0).CloudStack.Add(top);

        var actions = PullAction(flow, state)
            .Where(action => action.SourceId == carrier.InstanceId)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(actions, Has.Length.EqualTo(2));
            Assert.That(actions.Select(action => action.ActionId), Is.EquivalentTo(new[]
            {
                "pull_22_24_22",
                "pull_22_24_23",
            }));
            Assert.That(actions.Select(action => action.TargetId), Is.All.EqualTo(top.InstanceId));
            Assert.That(actions.Select(action =>
                ((IEnumerable<long>)action.Payload["selectedEntityIds"]!).Single()),
                Is.EquivalentTo(new[] { carrier.InstanceId, secondTarget.InstanceId }));
            Assert.That(actions.Select(action =>
                ((IEnumerable<long>)action.Payload["selectedEntityIds"]!).Single()),
                Does.Not.Contain(machineAlpha.InstanceId));
        });
    }

    [Test]
    public void PullActionUsesExistingSettlementAndMovesTheTopToGraveyard()
    {
        var state = CreateActionState(out var flow, out var router);
        var carrier = AddCarrier(state, 30, "carrier", health: 2);
        var top = new CardInstance(31, 0, new CardDefinition(
            "downloaded",
            "Downloaded",
            pullEffects: new[]
            {
                new EffectSpec(EffectNames.Buff, "SELF", 2, "hp"),
            }));
        state.GetPlayer(0).CloudStack.Add(top);
        var advertised = PullAction(flow, state).Single();

        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            advertised.ActionId,
            advertised.SourceId,
            advertised.TargetId?.ToString()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.GetPlayer(0).CloudStack, Is.Empty);
            Assert.That(state.GetPlayer(0).Graveyard, Has.Exactly(1).EqualTo(top));
            Assert.That(state.GetPlayer(0).PullCount, Is.EqualTo(1));
            Assert.That(carrier.Health, Is.EqualTo(4));
            Assert.That(state.Events.Items.Count(item => item.EventType == "CARD_PULLED"), Is.EqualTo(1));
            var projected = EngineProjectionAdapter.ToEvents(
                state.Events.Items,
                state.Turn.Number,
                state.Turn.PhaseId)
                .Where(item => item.Type == "PULL_DECLARED" || item.Type == "CARD_PULLED")
                .ToArray();
            Assert.That(projected.Select(item => item.Type),
                Is.EqualTo(new[] { "PULL_DECLARED", "CARD_PULLED" }));
            Assert.That(projected[0].Data.Keys,
                Is.EquivalentTo(new[] { "sourceId", "targetIds" }));
            Assert.That(projected[1].Data.Keys,
                Is.EquivalentTo(new[] { "sourceId", "targetIds", "amount", "count" }));
        });
    }

    [Test]
    public void PullRequiresAndResolvesTheExplicitFriendlyMinionTarget()
    {
        var state = CreateActionState(out var flow, out var router);
        var carrier = AddCarrier(state, 25, "carrier");
        var target = AddCarrier(state, 26, "target");
        var top = new CardInstance(27, 0, new CardDefinition(
            "targeted_pull",
            "Targeted Pull",
            pullEffects: new[]
            {
                new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 1, "both"),
            }));
        state.GetPlayer(0).CloudStack.Add(top);
        var advertised = PullAction(flow, state)
            .Single(action => action.SourceId == carrier.InstanceId
                && action.Payload.ContainsKey("selectedEntityIds")
                && ((IEnumerable<long>)action.Payload["selectedEntityIds"]!).Single() == target.InstanceId);
        var selected = ((IEnumerable<long>)advertised.Payload["selectedEntityIds"]!).ToArray();

        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            advertised.ActionId,
            advertised.SourceId,
            advertised.TargetId?.ToString(),
            selected));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(target.Attack, Is.EqualTo(2));
            Assert.That(target.Health, Is.EqualTo(4));
            Assert.That(carrier.Attack, Is.EqualTo(1));
            Assert.That(carrier.Health, Is.EqualTo(3));
            Assert.That(state.GetPlayer(0).Graveyard, Does.Contain(top));
            Assert.That(state.GetPlayer(0).PullCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void PullRejectsMissingFriendlyMinionSelectionWithoutMutation()
    {
        var state = CreateActionState(out var flow, out var router);
        var carrier = AddCarrier(state, 28, "carrier");
        AddCarrier(state, 29, "target");
        var top = new CardInstance(30, 0, new CardDefinition(
            "targeted_pull",
            "Targeted Pull",
            pullEffects: new[]
            {
                new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 1, "both"),
            }));
        state.GetPlayer(0).CloudStack.Add(top);
        var beforeEvents = state.Events.Count;

        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            "pull_28_30",
            carrier.InstanceId,
            top.InstanceId.ToString()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonKey, Is.EqualTo("action.target_required"));
            Assert.That(state.Events.Count, Is.EqualTo(beforeEvents));
            Assert.That(state.GetPlayer(0).CloudStack, Has.Exactly(1).EqualTo(top));
            Assert.That(state.GetPlayer(0).PullCount, Is.Zero);
        });
    }

    [Test]
    public void PullRejectsNonTopTargetsBeforeAnyMutation()
    {
        var state = CreateActionState(out var flow, out var router);
        var carrier = AddCarrier(state, 40, "carrier");
        var lower = new CardInstance(41, 0, new CardDefinition("lower", "Lower"));
        var top = new CardInstance(42, 0, new CardDefinition("top", "Top"));
        state.GetPlayer(0).CloudStack.Add(lower);
        state.GetPlayer(0).CloudStack.Add(top);
        var beforeEvents = state.Events.Count;
        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            "pull_40_41",
            carrier.InstanceId,
            "entity_000000000041"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonKey, Is.EqualTo("action.invalid_pull_target"));
            Assert.That(state.Events.Count, Is.EqualTo(beforeEvents));
            Assert.That(state.GetPlayer(0).CloudStack, Is.EqualTo(new[] { lower, top }));
            Assert.That(state.GetPlayer(0).Graveyard, Is.Empty);
            Assert.That(state.GetPlayer(0).PullCount, Is.Zero);
        });
    }

    [Test]
    public void PullWithPositiveDownloadPunishDrawsBeforeResolution()
    {
        var state = CreateActionState(out var flow, out var router);
        var carrier = AddCarrier(state, 50, "carrier");
        var top = new CardInstance(51, 0, new CardDefinition(
            "costed",
            "Costed",
            downloadCost: 1));
        state.GetPlayer(0).CloudStack.Add(top);
        var punished = new CardInstance(52, 1, new CardDefinition("punished", "Punished"));
        state.GetPlayer(1).Deck.Add(punished);
        var advertised = PullAction(flow, state);
        var action = advertised.Single();

        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            action.ActionId,
            carrier.InstanceId,
            "entity_000000000051"));

        Assert.Multiple(() =>
        {
            Assert.That(advertised, Has.Length.EqualTo(1));
            Assert.That(action.Payload["punish"], Is.EqualTo(1));
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.GetPlayer(0).CloudStack, Is.Empty);
            Assert.That(state.GetPlayer(0).Graveyard, Has.Exactly(1).EqualTo(top));
            Assert.That(state.GetPlayer(0).PullCount, Is.EqualTo(1));
            Assert.That(state.GetPlayer(1).Hand, Has.Exactly(1).EqualTo(punished));
            Assert.That(state.GetPlayer(1).PunishDrawnThisTurn, Is.EqualTo(1));
            Assert.That(state.Events.Items.Any(item => item.EventType == "PUNISH_DRAW"), Is.True);
        });
    }

    [Test]
    public void LandmarkFirstPullAllowsPositiveDownloadPunishAndAdvancesItsCounter()
    {
        var state = CreateActionState(out var flow, out var router);
        var landmarkDefinition = new CardDefinition(
            "machine_landmark_free",
            "Machine Landmark",
            faction: "机械遗迹",
            type: "SPELL",
            isLeader: true,
            leaderWinCondition: "PULL_TOTAL_GE",
            leaderWinParam: 6,
            isLandmark: true,
            landmarkTiers: new[]
            {
                new LandmarkTierDefinition(1, "free pull"),
            });
        var landmark = new CardInstance(54, 0, landmarkDefinition)
        {
            IsLeaderEntity = true,
        };
        var top = new CardInstance(
            55,
            0,
            new CardDefinition("costed_landmark_pull", "Costed Landmark Pull", downloadCost: 2));
        state.GetPlayer(0).LeaderZone.Add(landmark);
        state.GetPlayer(0).CloudStack.Add(top);
        state.GetPlayer(1).Deck.Add(new CardInstance(56, 1, new CardDefinition("punished_a", "Punished A")));
        state.GetPlayer(1).Deck.Add(new CardInstance(57, 1, new CardDefinition("punished_b", "Punished B")));

        var advertised = PullAction(flow, state).Single();
        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            advertised.ActionId,
            advertised.SourceId,
            advertised.TargetId?.ToString()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(landmark.LandmarkPullCount, Is.EqualTo(1));
            Assert.That(state.GetPlayer(0).PullCount, Is.EqualTo(1));
            Assert.That(state.GetPlayer(0).CloudStack, Is.Empty);
            Assert.That(state.GetPlayer(0).Graveyard, Does.Contain(top));
            Assert.That(state.GetPlayer(1).PunishDrawnThisTurn, Is.EqualTo(2));
        });
    }

    [Test]
    public void LandmarkSecondPullStartsChantAndPromotesToAlphaAtEndPhase()
    {
        var landmarkDefinition = new CardDefinition(
            "machine_landmark_tiered",
            "Tiered Machine Landmark",
            faction: "机械遗迹",
            type: "SPELL",
            isLeader: true,
            leaderWinCondition: "PULL_TOTAL_GE",
            leaderWinParam: 6,
            isLandmark: true,
            landmarkTiers: new[]
            {
                new LandmarkTierDefinition(1, "free pull"),
                new LandmarkTierDefinition(2, "summon alpha", chant: 1, summonCardId: "machine_alpha_test"),
            });
        var alphaDefinition = new CardDefinition(
            "machine_alpha_test",
            "Machine Alpha",
            attack: 8,
            health: 10,
            isMinion: true,
            isLeader: true,
            faction: "机械遗迹",
            leaderWinCondition: "PULL_TOTAL_GE",
            leaderWinParam: 6);
        var state = new GameState(
            new PlayerState(0, 20),
            new PlayerState(1, 20),
            cardLibrary: new[] { landmarkDefinition, alphaDefinition });
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        Assert.That(router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted, Is.True);

        var landmark = new CardInstance(70, 0, landmarkDefinition)
        {
            IsLeaderEntity = true,
        };
        state.GetPlayer(0).LeaderZone.Add(landmark);
        state.GetPlayer(0).CloudStack.Add(new CardInstance(
            71,
            0,
            new CardDefinition("tier_one_pull", "Tier One Pull")));
        state.GetPlayer(0).CloudStack.Add(new CardInstance(
            72,
            0,
            new CardDefinition("tier_two_pull", "Tier Two Pull")));

        var first = PullAction(flow, state).Single();
        Assert.That(router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            first.ActionId,
            first.SourceId,
            first.TargetId?.ToString())).Accepted, Is.True);
        var second = PullAction(flow, state).Single();
        Assert.That(router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            second.ActionId,
            second.SourceId,
            second.TargetId?.ToString())).Accepted, Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(landmark.LandmarkPullCount, Is.EqualTo(2));
            Assert.That(landmark.ChantRemaining, Is.EqualTo(1));
            Assert.That(landmark.PendingLandmarkSummonCardId, Is.EqualTo("machine_alpha_test"));
            Assert.That(state.GetPlayer(0).LeaderZone, Does.Contain(landmark));
        });

        flow.JumpTo(state, 0, TurnPhase.End);
        flow.Advance(state, 0);

        Assert.Multiple(() =>
        {
            Assert.That(state.GetPlayer(0).Leader, Is.Not.Null);
            Assert.That(state.GetPlayer(0).Leader!.Definition.Id, Is.EqualTo("machine_alpha_test"));
            Assert.That(state.GetPlayer(0).Field.Single().Definition.Id, Is.EqualTo("machine_alpha_test"));
            Assert.That(state.GetPlayer(0).Graveyard, Does.Contain(landmark));
            Assert.That(state.GetPlayer(0).LeaderZone, Does.Not.Contain(landmark));
        });
    }

    [Test]
    public void UnknownPullEffectIsNotAdvertisedAndIsRejectedBeforeMutation()
    {
        var state = CreateActionState(out var flow, out var router);
        var carrier = AddCarrier(state, 52, "carrier");
        var top = new CardInstance(53, 0, new CardDefinition(
            "unknown_pull",
            "Unknown Pull",
            pullEffects: new[]
            {
                new EffectSpec("UNREGISTERED_PULL_EFFECT"),
            }));
        state.GetPlayer(0).CloudStack.Add(top);
        var beforeEvents = state.Events.Count;

        var advertised = PullAction(flow, state);
        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            "pull_52_53",
            carrier.InstanceId,
            top.InstanceId.ToString()));

        Assert.Multiple(() =>
        {
            Assert.That(advertised, Is.Empty);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonKey, Is.EqualTo("action.unknown_effect"));
            Assert.That(state.Events.Count, Is.EqualTo(beforeEvents));
            Assert.That(state.GetPlayer(0).CloudStack, Has.Exactly(1).EqualTo(top));
            Assert.That(state.GetPlayer(0).PullCount, Is.Zero);
        });
    }

    [Test]
    public void PullAdvertisesAndExecutesForAnExplicitMechanicalLandmark()
    {
        var state = CreateActionState(out var flow, out var router);
        var landmarkDefinition = new CardDefinition(
            "machine_landmark",
            "Machine Landmark",
            faction: "机械遗迹",
            type: "SPELL",
            isLeader: true,
            isLandmark: true);
        var landmark = new CardInstance(55, 0, landmarkDefinition)
        {
            IsLeaderEntity = true,
        };
        var top = new CardInstance(56, 0, new CardDefinition("landmark_pull", "Landmark Pull"));
        state.GetPlayer(0).LeaderZone.Add(landmark);
        state.GetPlayer(0).CloudStack.Add(top);

        var advertised = PullAction(flow, state).Single();
        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            advertised.ActionId,
            advertised.SourceId,
            advertised.TargetId?.ToString()));

        Assert.Multiple(() =>
        {
            Assert.That(advertised.SourceId, Is.EqualTo(landmark.InstanceId));
            Assert.That(result.Accepted, Is.True);
            Assert.That(state.GetPlayer(0).CloudStack, Is.Empty);
            Assert.That(state.GetPlayer(0).Graveyard, Has.Exactly(1).EqualTo(top));
            Assert.That(state.GetPlayer(0).PullCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void PullRejectsAnOrdinaryMechanicalLeaderThatIsNotAMarkedLandmark()
    {
        var state = CreateActionState(out var flow, out var router);
        var leader = new CardInstance(57, 0, new CardDefinition(
            "machine_leader",
            "Machine Leader",
            faction: "机械遗迹",
            type: "SPELL",
            isLeader: true))
        {
            IsLeaderEntity = true,
        };
        var top = new CardInstance(58, 0, new CardDefinition("leader_pull", "Leader Pull"));
        state.GetPlayer(0).LeaderZone.Add(leader);
        state.GetPlayer(0).CloudStack.Add(top);
        var beforeEvents = state.Events.Count;

        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            "pull_57_58",
            leader.InstanceId,
            "entity_000000000058"));

        Assert.Multiple(() =>
        {
            Assert.That(PullAction(flow, state), Is.Empty);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonKey, Is.EqualTo("action.invalid_pull_carrier"));
            Assert.That(state.Events.Count, Is.EqualTo(beforeEvents));
            Assert.That(state.GetPlayer(0).CloudStack, Has.Exactly(1).EqualTo(top));
        });
    }

    [Test]
    public void PullRequiresAMechanicalCarrierAndDoesNotTreatAnOrdinaryFieldCardAsOne()
    {
        var state = CreateActionState(out _, out var router);
        var ordinary = new CardInstance(60, 0, new CardDefinition(
            "ordinary",
            "Ordinary",
            isMinion: true,
            health: 2));
        var top = new CardInstance(61, 0, new CardDefinition("top", "Top"));
        state.GetPlayer(0).Field.Add(ordinary);
        state.GetPlayer(0).CloudStack.Add(top);
        var beforeEvents = state.Events.Count;

        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            "pull_60_61",
            ordinary.InstanceId,
            "entity_000000000061"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonKey, Is.EqualTo("action.invalid_pull_carrier"));
            Assert.That(state.Events.Count, Is.EqualTo(beforeEvents));
            Assert.That(state.GetPlayer(0).Field, Does.Contain(ordinary));
            Assert.That(state.GetPlayer(0).CloudStack, Has.Exactly(1).EqualTo(top));
        });
    }

    private static CardInstance AddCarrier(
        GameState state,
        long instanceId,
        string id,
        int health = 3)
    {
        var definition = new CardDefinition(
            id,
            id,
            attack: 1,
            health: health,
            isMinion: true,
            faction: "机械遗迹",
            tags: new[] { "机械" });
        var card = new CardInstance(instanceId, 0, definition);
        state.GetPlayer(0).Field.Add(card);
        return card;
    }

    private static IReadOnlyList<LegalAction> PullAction(TurnFlow flow, GameState state)
    {
        return flow.GetLegalActions(state, 0)
            .Where(action => action.Type == LegalActionGenerator.Pull)
            .ToArray();
    }

    private static GameState CreateActionState(
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
}
}
