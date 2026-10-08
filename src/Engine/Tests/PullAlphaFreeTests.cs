using System;
using System.Collections.Generic;
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
public sealed class PullAlphaFreeTests
{
    [Test]
    public void ProductionTitanPullUsesOneAdvertisedAndSettledFeeAcrossAlphaWindowAndDeparture()
    {
        var catalog = CardCatalog.LoadDirectory(Path.Combine(FindRepositoryRoot(), "data", "cards"));
        var alphaDefinition = catalog.Cards["machine_alpha"];
        var titanDefinition = catalog.Cards["machine_titan"];
        var carrierDefinition = catalog.Cards["machine_golem"];
        var state = new GameState(
            new PlayerState(0),
            new PlayerState(1),
            cardLibrary: catalog.Cards.Values);
        var owner = state.GetPlayer(0);
        var opponent = state.GetPlayer(1);
        var carrier = new CardInstance(100, 0, carrierDefinition);
        var alpha = new CardInstance(101, 0, alphaDefinition) { IsLeaderEntity = true };
        owner.Field.Add(carrier);

        // The other side has response-eligible cards. The first real response
        // window moves the production Alpha into its field to prove that the
        // action's already-announced fee is not recomputed after the window.
        for (var index = 0; index < 8; index++)
        {
            opponent.Deck.Add(new CardInstance(200 + index, 1, new CardDefinition(
                "response_" + index,
                "Response " + index,
                type: "PUNISH",
                punishActivatable: true,
                punishCondition: "ALWAYS")));
        }

        var responsePolicy = new MovingAlphaResponsePolicy(() => owner.Field.Add(alpha));
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow, null, responsePolicy);
        flow.Advance(state, 0);
        Assert.That(router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted, Is.True);

        Assert.That(alphaDefinition.IsLeader && alphaDefinition.IsMinion, Is.True,
            "the production machine_alpha definition is a real leader minion");
        Assert.That(titanDefinition.DownloadCost, Is.EqualTo(2),
            "the production machine_titan is the high-cost PULL case");

        var normalCostDefinition = catalog.Cards["machine_drone"];
        var firstNormalPull = new CardInstance(300, 0, normalCostDefinition);
        owner.CloudStack.Add(firstNormalPull);
        ExecutePull(state, flow, router, carrier, firstNormalPull, expectedAdvertisedCost: 1);
        Assert.That(owner.Field, Does.Contain(alpha),
            "the response callback changes Alpha state after the fee has been announced");

        var secondTitan = new CardInstance(301, 0, titanDefinition);
        owner.CloudStack.Add(secondTitan);
        ExecutePull(state, flow, router, carrier, secondTitan, expectedAdvertisedCost: 0);
        Assert.That(responsePolicy.DecisionCount, Is.EqualTo(1),
            "the free PULL adds no response decision beyond the preceding paid window");

        owner.Field.Remove(alpha);
        owner.Graveyard.Add(alpha);
        var thirdNormalPull = new CardInstance(302, 0, normalCostDefinition);
        owner.CloudStack.Add(thirdNormalPull);
        ExecutePull(state, flow, router, carrier, thirdNormalPull, expectedAdvertisedCost: 1);

        var declaredCosts = state.Events.Items
            .Where(item => item.EventType == "PULL_DECLARED")
            .Select(item => Convert.ToInt32(item.Data["punish"]))
            .ToArray();
        var settledCosts = state.Events.Items
            .Where(item => item.EventType == "CARD_PULLED")
            .Select(item => Convert.ToInt32(item.Data["cost"]))
            .ToArray();
        var pullRoots = state.Events.Items
            .Where(item => item.EventType == "PULL_DECLARED")
            .ToArray();
        var punishDrawsPerPull = pullRoots
            .Select(root => state.Events.Items.Count(item => item.EventType == "PUNISH_DRAW"
                && item.ParentEventId == root.EventId))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(declaredCosts, Is.EqualTo(new[] { 1, 0, 1 }));
            Assert.That(settledCosts, Is.EqualTo(declaredCosts),
                "PULL_DECLARED, payment, and CARD_PULLED use one action-local fee");
            Assert.That(punishDrawsPerPull, Is.EqualTo(new[] { 1, 0, 1 }),
                "only paid PULLs emit a punishment draw under their own declared root");
            Assert.That(opponent.PunishDrawnThisTurn, Is.EqualTo(2));
            Assert.That(responsePolicy.DecisionCount, Is.EqualTo(2));
            Assert.That(owner.PullCount, Is.EqualTo(3));
            Assert.That(owner.CloudStack, Is.Empty);
            Assert.That(owner.Graveyard, Does.Contain(firstNormalPull).And.Contain(secondTitan).And.Contain(thirdNormalPull));
        });
    }

    [Test]
    public void DirectPullEffectUsesActiveAlphaForTheReportedFeeWithoutAddingPayment()
    {
        var catalog = CardCatalog.LoadDirectory(Path.Combine(FindRepositoryRoot(), "data", "cards"));
        var state = new GameState(
            new PlayerState(0),
            new PlayerState(1),
            cardLibrary: catalog.Cards.Values);
        var owner = state.GetPlayer(0);
        var opponent = state.GetPlayer(1);
        var carrier = new CardInstance(400, 0, catalog.Cards["machine_golem"]);
        var alpha = new CardInstance(401, 0, catalog.Cards["machine_alpha"]) { IsLeaderEntity = true };
        var titan = new CardInstance(402, 0, catalog.Cards["machine_titan"]);
        owner.Field.Add(carrier);
        owner.Field.Add(alpha);
        owner.CloudStack.Add(titan);
        opponent.Deck.Add(new CardInstance(403, 1, new CardDefinition("direct_draw", "Direct Draw")));
        var root = state.Events.Append("TEST_PULL_EFFECT").EventId;
        var context = new EffectContext(
            0,
            root,
            sourceCard: carrier,
            selectedTargetId: carrier.InstanceId);

        new EffectRuntime(state).Pull(new EffectSpec(EffectNames.Pull), context);

        var pulled = state.Events.Items.Single(item => item.EventType == "CARD_PULLED");
        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToInt32(pulled.Data["cost"]), Is.Zero);
            Assert.That(opponent.Deck, Has.Count.EqualTo(1),
                "the direct effect path retains its existing no-lifecycle-payment responsibility");
            Assert.That(owner.PullCount, Is.EqualTo(1));
            Assert.That(owner.Graveyard, Does.Contain(titan));
            Assert.That(state.Events.Items.Any(item => item.EventType == "PUNISH_DRAW"), Is.False);
        });
    }

    private static void ExecutePull(
        GameState state,
        TurnFlow flow,
        TurnActionRouter router,
        CardInstance carrier,
        CardInstance top,
        int expectedAdvertisedCost)
    {
        var action = flow.GetLegalActions(state, 0)
            .Single(candidate => candidate.Type == LegalActionGenerator.Pull
                && candidate.SourceId == carrier.InstanceId
                && candidate.TargetId == top.InstanceId
                && candidate.Payload.TryGetValue("selectedEntityIds", out var selected)
                && ((IEnumerable<long>)selected!).Contains(carrier.InstanceId));
        Assert.That(Convert.ToInt32(action.Payload["punish"]), Is.EqualTo(expectedAdvertisedCost));
        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            action.ActionId,
            action.SourceId,
            action.TargetId?.ToString(),
            new[] { carrier.InstanceId }));
        Assert.That(result.Accepted, Is.True, result.ReasonKey);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docs", "RULES.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new AssertionException("Repository root was not found.");
    }

    private sealed class MovingAlphaResponsePolicy : IPunishResponsePolicy
    {
        private readonly Action _onFirstDecision;

        public MovingAlphaResponsePolicy(Action onFirstDecision)
        {
            _onFirstDecision = onFirstDecision;
        }

        public int DecisionCount { get; private set; }

        public PunishResponseDecision Decide(GameState state, CardInstance card, int effectivePunish, int chainDepth)
        {
            DecisionCount++;
            if (DecisionCount == 1)
            {
                _onFirstDecision();
            }

            return PunishResponseDecision.Decline();
        }
    }
}
}
