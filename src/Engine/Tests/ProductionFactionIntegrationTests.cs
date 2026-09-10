using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Data;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Setup;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class ProductionFactionIntegrationTests
{
    [Test]
    public void ProductionWoodDeckManifestsLeaderGrowsAndWinsThroughTheAdapterBoundary()
    {
        var production = LoadProduction();
        var state = CreateState(
            production.WoodDeck,
            production.MachineDeck,
            production.Catalog,
            firstPlayer: 1);
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        var wood = state.GetPlayer(0);
        var opponent = state.GetPlayer(1);

        var woodLeader = TakeFromDeck(wood, "wood_leader");
        var target = TakeFromDeck(wood, "wood_guard");
        var growthCards = new[]
        {
            TakeFromDeck(wood, "wood_growth"),
            TakeFromDeck(wood, "wood_growth"),
            TakeFromDeck(wood, "wood_growth"),
        };
        var opponentLeader = TakeFromDeck(opponent, "machine_leader");
        var manifestTrigger = TakeFromDeck(opponent, "machine_scan");

        // Keep the fixture data-backed while making the authoritative draw
        // order deterministic. The target starts just below 512 so the test
        // proves the production growth path reaches the frozen threshold;
        // it does not pretend the three-card fixture is a full balance run.
        wood.Deck.Clear();
        wood.Deck.Add(woodLeader);
        target.Health = 464;
        target.MaxHealth = 464;
        wood.Field.Add(target);
        foreach (var growth in growthCards)
        {
            wood.Hand.Add(growth);
        }

        opponentLeader.IsLeaderEntity = true;
        opponent.LeaderZone.Add(opponentLeader);
        opponent.Hand.Add(manifestTrigger);

        EnterAction(state, flow, router, 1);
        var manifestAction = flow.GetLegalActions(state, 1)
            .Single(action => action.Type == LegalActionGenerator.PlayCard
                && action.SourceId == manifestTrigger.InstanceId);
        var manifestResult = router.Execute(state, new GameActionRequest(
            1,
            LegalActionGenerator.PlayCard,
            manifestAction.ActionId,
            manifestAction.SourceId));

        Assert.Multiple(() =>
        {
            Assert.That(manifestResult.Accepted, Is.True);
            Assert.That(wood.Leader, Is.SameAs(woodLeader));
            Assert.That(wood.LeaderZone, Does.Contain(woodLeader));
            Assert.That(wood.RampantStacks, Is.EqualTo(3),
                "normal +1 and punish +2 are both supplied by the production leader");
            Assert.That(wood.Field.Count(card => card.Definition.Id == "wood_sapling"), Is.EqualTo(2));
            Assert.That(state.Events.Items.Any(item => item.EventType == "LEADER_MANIFESTED"), Is.True);
        });

        FinishTurn(state, flow, router, 1);
        EnterAction(state, flow, router, 0);
        var targetId = target.InstanceId;
        foreach (var growth in growthCards)
        {
            var legalActions = flow.GetLegalActions(state, 0);
            var growthAction = legalActions.FirstOrDefault(action => action.Type == LegalActionGenerator.PlayCard
                && action.SourceId == growth.InstanceId
                && action.TargetId == targetId);
            Assert.That(growthAction, Is.Not.Null,
                "missing growth action at health " + target.Health
                + "; phase=" + state.Turn.PhaseId
                + "; winner=" + state.WinnerPlayerIndex
                + "; legal=" + string.Join(",", legalActions.Select(action => action.ActionId)));
            var projectedGrowthAction = EngineProjectionAdapter.ToLegalActionDtos(new[] { growthAction! })
                .Single();
            Assert.That(projectedGrowthAction.TargetId,
                Is.EqualTo(EngineProjectionAdapter.ToEntityId(targetId)));
            var growthResult = router.Execute(state, new GameActionRequest(
                0,
                LegalActionGenerator.PlayCard,
                growthAction!.ActionId,
                growthAction.SourceId,
                projectedGrowthAction.TargetId));
            Assert.That(growthResult.Accepted, Is.True, growthResult.ReasonKey);

            if (growth != growthCards[^1])
            {
                // Wood's production copies share the same keyword tag, so
                // the authoritative one-card-per-tag rule requires a fresh
                // turn between each growth play.
                FinishTurn(state, flow, router, 0);
                PassTurn(state, flow, router, 1);
                EnterAction(state, flow, router, 0);
            }
        }

        var projected = EngineProjectionAdapter.ToEvents(
            state.Events.Items,
            state.Turn.Number,
            state.Turn.PhaseId);
        var snapshot = RuntimeSnapshotProjection.ToSnapshot(
            state,
            "match_production_wood",
            99,
            0,
            flow);
        var projectedTarget = snapshot.Players[0].Field.Single(card => card.EntityId == targetId);

        Assert.Multiple(() =>
        {
            Assert.That(target.Health, Is.EqualTo(512));
            Assert.That(target.MaxHealth, Is.EqualTo(512));
            Assert.That(target.Sealed, Is.True);
            Assert.That(target.Attack, Is.Zero);
            Assert.That(target.HasKeyword("嘲讽"), Is.False,
                "a sealed production card loses its printed keyword ability");
            Assert.That(new AttackTargetPolicy().CanAttack(target), Is.False);
            Assert.That(state.WinnerPlayerIndex, Is.EqualTo(0));
            Assert.That(state.WinReason, Is.EqualTo("win.giant_health_ge"));
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Over));
            Assert.That(projectedTarget.Sealed, Is.True);
            Assert.That(projectedTarget.CurrentHealth, Is.EqualTo(512));
            Assert.That(snapshot.WinnerPlayerIndex, Is.EqualTo(0));
            Assert.That(snapshot.ReasonKey, Is.EqualTo("win.giant_health_ge"));
            Assert.That(snapshot.Players[1].Hand, Is.Empty,
                "the v1.31 adapter keeps the opponent hand hidden");
            Assert.That(projected.Select(item => item.Type), Does.Contain("LEADER_MANIFESTED"));
            Assert.That(projected.Select(item => item.Type), Does.Contain("GAME_OVER"));
        });
    }

    [Test]
    public void ProductionMachineFactoryRemainsOnFieldForTwoEndsThenSummonsThreeDrones()
    {
        var production = LoadProduction();
        var state = CreateState(
            production.MachineDeck,
            production.WoodDeck,
            production.Catalog,
            firstPlayer: 0);
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        var machine = state.GetPlayer(0);

        var leader = TakeFromDeck(machine, "machine_leader");
        var factory = TakeFromDeck(machine, "machine_factory");
        leader.IsLeaderEntity = true;
        machine.LeaderZone.Add(leader);
        machine.Hand.Add(factory);

        EnterAction(state, flow, router, 0);
        var play = flow.GetLegalActions(state, 0)
            .Single(action => action.Type == LegalActionGenerator.PlayCard
                && action.SourceId == factory.InstanceId);
        Assert.That(router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.PlayCard,
            play.ActionId,
            play.SourceId)).Accepted, Is.True);

        Assert.That(factory.ChantRemaining, Is.EqualTo(2));
        Assert.That(machine.Field, Does.Contain(factory));
        var afterPlay = RuntimeSnapshotProjection.ToSnapshot(
            state,
            "match_machine_factory",
            1,
            0,
            flow);
        Assert.That(afterPlay.Players[0].Field
            .Single(card => card.CardId == "machine_factory")
            .ChantRemaining, Is.EqualTo(2));

        FinishTurn(state, flow, router, 0);
        PassTurn(state, flow, router, 1);
        EnterAction(state, flow, router, 0);
        Assert.That(factory.ChantRemaining, Is.EqualTo(1));

        FinishTurn(state, flow, router, 0);

        var droneCount = machine.Field.Count(card => card.Definition.Id == "machine_drone");
        Assert.Multiple(() =>
        {
            Assert.That(factory.ChantRemaining, Is.Zero);
            Assert.That(machine.Field, Does.Not.Contain(factory));
            Assert.That(machine.Graveyard, Does.Contain(factory));
            Assert.That(droneCount, Is.EqualTo(3));
            Assert.That(state.Events.Items.Count(item => item.EventType == "MINION_SUMMONED"),
                Is.GreaterThanOrEqualTo(3));
        });

        var resolved = RuntimeSnapshotProjection.ToSnapshot(
            state,
            "match_machine_factory",
            2,
            0,
            flow);
        Assert.That(resolved.Players[0].Field.Count(card => card.CardId == "machine_drone"),
            Is.EqualTo(3));
        Assert.That(resolved.Players[0].Field.Any(card => card.CardId == "machine_factory"),
            Is.False);
    }

    [Test]
    public void ProductionMachineDeckCompletesCommitPushPullLandmarkAndAlphaVictory()
    {
        var production = LoadProduction();
        var state = CreateState(
            production.MachineDeck,
            production.WoodDeck,
            production.Catalog,
            firstPlayer: 0);
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        var machine = state.GetPlayer(0);
        var opponent = state.GetPlayer(1);

        var landmark = TakeFromDeck(machine, "machine_leader");
        var firstCommit = TakeFromDeck(machine, "machine_drone");
        var secondCommit = TakeFromDeck(machine, "machine_spark");
        var carrier = TakeFromDeck(machine, "machine_golem");
        var target = TakeFromDeck(machine, "machine_wall");
        var opponentLeader = TakeFromDeck(opponent, "wood_leader");

        landmark.IsLeaderEntity = true;
        machine.LeaderZone.Add(landmark);
        machine.Field.Add(firstCommit);
        machine.Field.Add(secondCommit);
        machine.Field.Add(carrier);
        machine.Field.Add(target);
        opponentLeader.IsLeaderEntity = true;
        opponent.LeaderZone.Add(opponentLeader);

        EnterAction(state, flow, router, 0);
        var commitIds = new[] { firstCommit.InstanceId, secondCommit.InstanceId };
        foreach (var sourceId in commitIds)
        {
            var commit = flow.GetLegalActions(state, 0)
                .Single(action => action.Type == LegalActionGenerator.Commit
                    && action.SourceId == sourceId);
            var result = router.Execute(state, new GameActionRequest(
                0,
                LegalActionGenerator.Commit,
                commit.ActionId,
                commit.SourceId));
            Assert.That(result.Accepted, Is.True);
        }

        Assert.Multiple(() =>
        {
            Assert.That(machine.CommitQueue.Select(card => card.InstanceId), Is.EqualTo(commitIds));
            Assert.That(firstCommit.Definition.CommitCost, Is.EqualTo(1));
            Assert.That(secondCommit.Definition.CommitCost, Is.EqualTo(1));
            Assert.That(firstCommit.Definition.UploadCost, Is.Zero);
            Assert.That(secondCommit.Definition.UploadCost, Is.Zero);
            Assert.That(machine.Field, Does.Contain(carrier));
            Assert.That(machine.Field, Does.Contain(target));
        });

        FinishTurn(state, flow, router, 0);
        Assert.Multiple(() =>
        {
            Assert.That(machine.CommitQueue, Is.Empty);
            Assert.That(machine.CloudStack.Select(card => card.InstanceId), Is.EqualTo(commitIds),
                "the end-phase PUSH preserves FIFO queue order");
        });

        PassTurn(state, flow, router, 1);
        EnterAction(state, flow, router, 0);
        var firstPull = ExecutePull(state, flow, router, landmark, secondCommit, target);
        Assert.That(firstPull.Accepted, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(machine.Graveyard, Does.Contain(secondCommit));
            Assert.That(machine.CloudStack.Select(card => card.InstanceId), Is.EqualTo(new[] { firstCommit.InstanceId }));
            Assert.That(target.Attack, Is.EqualTo(target.Definition.Attack + 1));
            Assert.That(target.Health, Is.EqualTo(target.Definition.Health + 1));
            Assert.That(landmark.LandmarkPullCount, Is.EqualTo(1));
        });

        var secondPull = ExecutePull(state, flow, router, landmark, firstCommit, carrier);
        Assert.That(secondPull.Accepted, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(machine.Graveyard, Does.Contain(firstCommit));
            Assert.That(machine.CloudStack, Is.Empty);
            Assert.That(landmark.LandmarkPullCount, Is.EqualTo(2));
            Assert.That(landmark.ChantRemaining, Is.EqualTo(1));
            Assert.That(landmark.PendingLandmarkSummonCardId, Is.EqualTo("machine_alpha"));
        });

        FinishTurn(state, flow, router, 0);
        var alpha = machine.Field.Single(card => card.Definition.Id == "machine_alpha");
        Assert.Multiple(() =>
        {
            Assert.That(machine.Leader, Is.SameAs(alpha));
            Assert.That(machine.LeaderZone, Is.Empty);
            Assert.That(landmark, Is.EqualTo(machine.Graveyard.Single(card => card.InstanceId == landmark.InstanceId)));
            Assert.That(alpha.Definition.LeaderWinCondition, Is.EqualTo("PULL_TOTAL_GE"));
            Assert.That(alpha.Definition.LeaderWinParam, Is.EqualTo(6));
        });

        // Four further production mechanical minions make the remaining
        // successful PULLs explicit. They are placed on the public cloud only
        // after the committed pair has demonstrated FIFO PUSH/LIFO PULL.
        foreach (var cardId in new[] { "machine_blaster", "machine_titan", "machine_recycler", "machine_assembler" })
        {
            machine.CloudStack.Add(TakeFromDeck(machine, cardId));
        }

        PassTurn(state, flow, router, 1);
        EnterAction(state, flow, router, 0);
        foreach (var expectedTop in machine.CloudStack.Reverse().ToArray())
        {
            var pull = ExecutePull(state, flow, router, alpha, expectedTop, target);
            Assert.That(pull.Accepted, Is.True);
            if (state.WinnerPlayerIndex.HasValue)
            {
                break;
            }

            if (machine.CloudStack.Count > 0)
            {
                // PULL is an action-phase operation; keep the same turn for
                // this chain while the cloud still has cards.
                continue;
            }
        }

        var projected = EngineProjectionAdapter.ToEvents(
            state.Events.Items,
            state.Turn.Number,
            state.Turn.PhaseId);
        var snapshot = RuntimeSnapshotProjection.ToSnapshot(
            state,
            "match_production_machine",
            123,
            0,
            flow);
        var rawPushed = state.Events.Items
            .Where(item => item.EventType == "CARD_PUSHED")
            .Select(item => (long)item.Data["target"]!)
            .Take(2)
            .ToArray();
        var rawPulled = state.Events.Items
            .Where(item => item.EventType == "CARD_PULLED")
            .Select(item => (long)item.Data["target"]!)
            .Take(2)
            .ToArray();
        var firstPush = state.Events.Items.First(item => item.EventType == "CARD_PUSHED");
        var pullDeclarations = state.Events.Items
            .Where(item => item.EventType == "PULL_DECLARED")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(state.WinnerPlayerIndex, Is.EqualTo(0));
            Assert.That(state.WinReason, Is.EqualTo("win.pull_total_ge"));
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Over));
            Assert.That(machine.PullCount, Is.EqualTo(6));
            Assert.That(machine.CloudStack, Is.Empty);
            Assert.That(machine.Graveyard, Does.Contain(firstCommit));
            Assert.That(rawPushed, Is.EqualTo(commitIds));
            Assert.That(rawPulled, Is.EqualTo(new[] { secondCommit.InstanceId, firstCommit.InstanceId }));
            Assert.That(firstPush.Data["punish"], Is.EqualTo(0),
                "ordinary production PUSH uses its approved zero default");
            Assert.That(pullDeclarations, Has.Length.EqualTo(6));
            // 每次 PULL 的惩罚抽牌量 = 被下载卡的 downloadCost（M1 差异化后 titan=2，其余=1）
            var pullPunishes = pullDeclarations
                .Select(item => Convert.ToInt32(item.Data["punish"]))
                .ToArray();
            Assert.That(pullPunishes.All(value => value is 1 or 2), Is.True,
                "PULL punish comes from the pulled card's downloadCost");
            Assert.That(pullPunishes.Sum(), Is.EqualTo(7),
                "five PULLs at downloadCost 1 plus machine_titan at downloadCost 2");
            // 惩罚抽牌每次结算只发一条事件，幅度写入事件的 count 载荷，
            // 因此事件数 = 结算次数 = 2 次 COMMIT + 6 次 PULL = 8，而不是抽到的总张数（7 张）。
            Assert.That(state.Events.Items.Count(item => item.EventType == "PUNISH_DRAW"), Is.EqualTo(8),
                "two COMMIT punish draws plus six PULL punish draws");
            Assert.That(projected.Select(item => item.Type), Does.Contain("CARD_COMMITTED"));
            Assert.That(projected.Select(item => item.Type), Does.Contain("CARD_PUSHED"));
            Assert.That(projected.Select(item => item.Type), Does.Contain("CARD_PULLED"));
            Assert.That(projected.Select(item => item.Type), Does.Contain("GAME_OVER"));
            Assert.That(snapshot.WinnerPlayerIndex, Is.EqualTo(0));
            Assert.That(snapshot.ReasonKey, Is.EqualTo("win.pull_total_ge"));
            Assert.That(snapshot.Players[0].PullCount, Is.EqualTo(6));
            Assert.That(snapshot.Players[0].Field.Any(card => card.CardId == "machine_alpha"), Is.True);
        });
    }

    private static GameActionResult ExecutePull(
        GameState state,
        TurnFlow flow,
        TurnActionRouter router,
        CardInstance carrier,
        CardInstance top,
        CardInstance selectedTarget)
    {
        var action = flow.GetLegalActions(state, 0)
            .Single(candidate => candidate.Type == LegalActionGenerator.Pull
                && candidate.SourceId == carrier.InstanceId
                && candidate.TargetId == top.InstanceId
                && SelectedTarget(candidate) == selectedTarget.InstanceId);
        return router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Pull,
            action.ActionId,
            action.SourceId,
            EngineProjectionAdapter.ToEntityId(action.TargetId!.Value),
            new[] { selectedTarget.InstanceId }));
    }

    private static long SelectedTarget(LegalAction action)
    {
        return ((IEnumerable<long>)action.Payload["selectedEntityIds"]!).Single();
    }

    private static void EnterAction(
        GameState state,
        TurnFlow flow,
        TurnActionRouter router,
        int playerIndex)
    {
        Assert.That(state.CurrentPlayerIndex, Is.EqualTo(playerIndex));
        // A completed discard already advances the incoming player into
        // AMBUSH; the initial fixture is still at START.  Accept both
        // authoritative turn states so this helper never advances twice.
        if (state.Turn.PhaseId == TurnPhase.Start)
        {
            flow.Advance(state, playerIndex);
        }
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Ambush));
        var skip = flow.GetLegalActions(state, playerIndex)
            .Single(action => action.Type == TurnAction.SkipAmbush);
        var result = router.Execute(state, new GameActionRequest(
            playerIndex,
            TurnAction.SkipAmbush,
            skip.ActionId));
        Assert.That(result.Accepted, Is.True);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
    }

    private static void PassTurn(
        GameState state,
        TurnFlow flow,
        TurnActionRouter router,
        int playerIndex)
    {
        EnterAction(state, flow, router, playerIndex);
        FinishTurn(state, flow, router, playerIndex);
    }

    private static void FinishTurn(
        GameState state,
        TurnFlow flow,
        TurnActionRouter router,
        int playerIndex)
    {
        var end = flow.GetLegalActions(state, playerIndex)
            .Single(action => action.Type == LegalActionGenerator.EndTurn);
        var endResult = router.Execute(state, new GameActionRequest(
            playerIndex,
            LegalActionGenerator.EndTurn,
            end.ActionId));
        Assert.That(endResult.Accepted, Is.True);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Discard));

        var discard = flow.GetLegalActions(state, playerIndex)
            .Single(action => action.Type == TurnAction.DiscardComplete);
        var required = Convert.ToInt32(discard.Payload["requiredCount"]);
        var candidates = ((IEnumerable<long>)discard.Payload["candidateIds"]!)
            .Take(required)
            .ToArray();
        var discardResult = router.Execute(state, new GameActionRequest(
            playerIndex,
            TurnAction.DiscardComplete,
            discard.ActionId,
            selectedEntityIds: candidates));
        Assert.That(discardResult.Accepted, Is.True);
    }

    private static CardInstance TakeFromDeck(PlayerState player, string cardId)
    {
        var card = player.Deck.FirstOrDefault(candidate => candidate.Definition.Id == cardId);
        Assert.That(card, Is.Not.Null, player.PlayerIndex + ": missing production card " + cardId);
        player.Deck.Remove(card!);
        return card!;
    }

    private static GameState CreateState(
        DeckDefinition player0Deck,
        DeckDefinition player1Deck,
        CardCatalog catalog,
        int firstPlayer)
    {
        return MatchSetup.Create(
            new MatchDeckSpec(player0Deck.Leader, player0Deck.Cards, player0Deck.Name, player0Deck.Faction),
            new MatchDeckSpec(player1Deck.Leader, player1Deck.Cards, player1Deck.Name, player1Deck.Faction),
            catalog.Cards,
            new MatchSetupOptions
            {
                Seed = 20260909,
                FirstPlayerIndex = firstPlayer,
                OpeningHandSize = 0,
                CastleEnabled = false,
            });
    }

    private static (CardCatalog Catalog, DeckDefinition WoodDeck, DeckDefinition MachineDeck) LoadProduction()
    {
        var root = FindRepositoryRoot();
        var catalog = CardCatalog.LoadDirectory(Path.Combine(root, "data", "cards"));
        var decks = DeckLoader.LoadDirectory(Path.Combine(root, "data", "decks"));
        return (
            catalog,
            decks.Single(deck => deck.Leader == "wood_leader"),
            decks.Single(deck => deck.Leader == "machine_leader"));
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
}
}
