using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Data;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Events;
using DominionWars.Engine.Model;
using DominionWars.Engine.Setup;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// Acceptance tests for the ninth player action, ROLLBACK, added by the
/// approved contract revision 1.31-player-rollback. The authoritative rules
/// are RULES.md §12.4 (回滚 moves one chosen commit-queue card back to hand and
/// cannot rewind an already resolved punish draw) and §13's glossary entry
/// 费用不返还 — so the action costs nothing, produces no punish draw, and opens
/// no punish response window.
/// </summary>
[TestFixture]
public sealed class RollbackActionTests
{
    [Test]
    public void IsNotAdvertisedWhenTheCommitQueueIsEmpty()
    {
        var state = CreateActionState(out var flow);

        var actions = flow.GetLegalActions(state, 0);

        Assert.Multiple(() =>
        {
            Assert.That(actions, Is.Not.Empty, "the sweep must still observe the ordinary phase actions");
            Assert.That(actions.Select(item => item.Type), Does.Not.Contain(LegalActionGenerator.Rollback));
        });
    }

    [Test]
    public void AdvertisesOneRollbackPerQueueCardWithTheQueueCardAsSource()
    {
        var state = CreateActionState(out var flow);
        var first = QueueCard(50);
        var second = QueueCard(51);
        state.GetPlayer(0).CommitQueue.Add(first);
        state.GetPlayer(0).CommitQueue.Add(second);

        var actions = flow.GetLegalActions(state, 0)
            .Where(item => item.Type == LegalActionGenerator.Rollback)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(actions, Has.Length.EqualTo(2), "one advertisement per legal queue card");
            Assert.That(actions.Select(item => item.ActionId),
                Is.EquivalentTo(new[] { "rollback_50", "rollback_51" }));
            Assert.That(actions.Select(item => item.SourceId),
                Is.EquivalentTo(new long?[] { 50, 51 }), "SourceId identifies the queue card the player chooses");
            Assert.That(actions.Select(item => item.Actor), Is.All.EqualTo(0));
            Assert.That(actions.Select(item => item.ReasonKey), Is.All.EqualTo("action.rollback"));
            Assert.That(actions.Select(item => item.CardId),
                Is.EquivalentTo(new[] { "queued_50", "queued_51" }));
            Assert.That(actions.SelectMany(item => item.Payload.Keys), Is.Empty,
                "the queue card travels in SourceId, so no selectedEntityIds payload is invented");
        });
    }

    [Test]
    public void DoesNotAdvertiseForTheOpponentsQueue()
    {
        var state = CreateActionState(out var flow);
        state.GetPlayer(1).CommitQueue.Add(QueueCard(60, ownerIndex: 1));

        var actions = flow.GetLegalActions(state, 0);

        Assert.That(actions.Select(item => item.Type), Does.Not.Contain(LegalActionGenerator.Rollback));
    }

    [Test]
    public void AdvertisesInEveryPhaseWhileTheQueueIsNotEmpty()
    {
        var state = CreateActionState(out var flow);
        state.GetPlayer(0).CommitQueue.Add(QueueCard(70));
        var generator = new LegalActionGenerator();
        var perPhase = new Dictionary<string, string[]>();

        foreach (var phase in new[]
        {
            TurnPhase.Start, TurnPhase.Ambush, TurnPhase.Action, TurnPhase.Discard, TurnPhase.End,
        })
        {
            flow.JumpTo(state, 0, phase);
            perPhase[phase] = generator.Generate(state, 0)
                .Where(item => item.Type == LegalActionGenerator.Rollback)
                .Select(item => item.ActionId)
                .ToArray();
        }

        Assert.That(perPhase.Values, Is.All.EquivalentTo(new[] { "rollback_70" }));
    }

    [Test]
    public void ResolvingTheAdvertisedRollbackMovesExactlyThatCardBackToHand()
    {
        var state = CreateActionState(out var flow, out var router);
        var chosen = QueueCard(80);
        var other = QueueCard(81);
        state.GetPlayer(0).CommitQueue.Add(chosen);
        state.GetPlayer(0).CommitQueue.Add(other);

        var advertised = flow.GetLegalActions(state, 0)
            .Single(item => item.ActionId == "rollback_80");
        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Rollback,
            advertised.ActionId,
            advertised.SourceId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.ReasonKey, Is.EqualTo("action.accepted"));
            Assert.That(state.GetPlayer(0).CommitQueue, Is.EqualTo(new[] { other }),
                "only the chosen card leaves the queue");
            Assert.That(state.GetPlayer(0).Hand, Is.EqualTo(new[] { chosen }));
            Assert.That(state.Events.Items.Select(item => item.EventType),
                Does.Contain("CARD_ROLLED_BACK"));
            Assert.That(state.Events.Items.Select(item => item.EventType),
                Does.Contain("ROLLBACK_DECLARED"));
        });
    }

    [Test]
    public void RollbackProducesNoPunishDrawAndNoResponseWindow()
    {
        var state = CreateActionState(out var flow, out var router);
        var queued = QueueCard(90);
        state.GetPlayer(0).CommitQueue.Add(queued);
        var opponentPunishCard = new CardInstance(91, 1, new CardDefinition("punish_me", "Punish Me"));
        state.GetPlayer(1).Deck.Add(opponentPunishCard);
        var opponentHandBefore = state.GetPlayer(1).Hand.Count;
        var opponentDeckBefore = state.GetPlayer(1).Deck.Count;
        var opponentLifeBefore = state.GetPlayer(1).Life;

        var advertised = flow.GetLegalActions(state, 0).Single(item => item.ActionId == "rollback_90");
        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Rollback,
            advertised.ActionId,
            advertised.SourceId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(advertised.Payload, Does.Not.ContainKey("punish"),
                "the rollback path is zero-cost: no punish value is advertised");
            Assert.That(state.GetPlayer(1).PunishDrawnThisTurn, Is.Zero);
            Assert.That(state.GetPlayer(1).PunishDeltaThisTurn, Is.Zero);
            Assert.That(state.GetPlayer(1).Hand, Has.Count.EqualTo(opponentHandBefore));
            Assert.That(state.GetPlayer(1).Deck, Has.Count.EqualTo(opponentDeckBefore));
            Assert.That(state.GetPlayer(1).Life, Is.EqualTo(opponentLifeBefore));
            Assert.That(state.GetPlayer(0).PunishDrawnThisTurn, Is.Zero);
            Assert.That(state.Events.Items.Select(item => item.EventType), Does.Not.Contain("PUNISH_DRAW"));
            Assert.That(state.Events.Items.Select(item => item.EventType), Does.Not.Contain("PUNISH_DELTA_APPLIED"));
            Assert.That(state.Events.Items.Select(item => item.EventType), Does.Not.Contain("CARD_DRAWN"));
        });
    }

    [Test]
    public void RejectsARollbackForACardThatIsNotInTheQueue()
    {
        var state = CreateActionState(out var flow, out var router);
        var queued = QueueCard(95);
        state.GetPlayer(0).CommitQueue.Add(queued);
        var fieldCard = new CardInstance(96, 0, new CardDefinition(
            "field_card", "Field Card", attack: 1, health: 2, isMinion: true));
        state.GetPlayer(0).Field.Add(fieldCard);
        var eventsBefore = state.Events.Count;

        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Rollback,
            "rollback_96",
            96));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonKey, Is.EqualTo("action.invalid_rollback_source"));
            Assert.That(state.GetPlayer(0).CommitQueue, Is.EqualTo(new[] { queued }),
                "the legal queue entry is untouched when a forged id is rejected");
            Assert.That(state.GetPlayer(0).Field, Is.EqualTo(new[] { fieldCard }));
            Assert.That(state.GetPlayer(0).Hand, Is.Empty);
            Assert.That(state.Events.Count, Is.EqualTo(eventsBefore), "a rejected rollback mutates nothing");
        });
    }

    [Test]
    public void RequiresTheQueueCardToBePresentForTheActionIdToMatch()
    {
        var state = CreateActionState(out _, out var router);
        state.GetPlayer(0).CommitQueue.Add(QueueCard(97));

        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Rollback,
            "rollback_999",
            97));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonKey, Is.EqualTo("action.id_mismatch"));
            Assert.That(state.GetPlayer(0).CommitQueue, Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    /// The pre-existing effect-only path (the <c>machine_recycler</c> commit
    /// effect) must keep working unchanged after ROLLBACK became a player
    /// action.
    /// </summary>
    [Test]
    public void ExistingEffectOnlyRollbackPathStillWorksUnchanged()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var queued = new CardInstance(98, 0, new CardDefinition("queued", "Queued"));
        state.Players[0].CommitQueue.Add(queued);
        var root = state.Events.Append("CARD_PLAYED");

        new EffectRuntime(state).Rollback(
            new EffectSpec(EffectNames.Rollback),
            new EffectContext(0, root.EventId, selectedTargetId: queued.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(state.Players[0].CommitQueue, Is.Empty);
            Assert.That(state.Players[0].Hand, Has.Exactly(1).EqualTo(queued));
            Assert.That(state.Events.Items[^1].EventType, Is.EqualTo("CARD_ROLLED_BACK"));
        });
    }

    /// <summary>
    /// Without an explicit selection the effect path keeps its single-card
    /// fallback and its own skip reason; the player action never relies on it.
    /// </summary>
    [Test]
    public void EffectPathStillSkipsAnAmbiguousSelectionInsteadOfGuessing()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        state.Players[0].CommitQueue.Add(new CardInstance(99, 0, new CardDefinition("a", "A")));
        state.Players[0].CommitQueue.Add(new CardInstance(100, 0, new CardDefinition("b", "B")));
        var root = state.Events.Append("CARD_PLAYED");

        new EffectRuntime(state).Rollback(
            new EffectSpec(EffectNames.Rollback),
            new EffectContext(0, root.EventId));

        Assert.Multiple(() =>
        {
            Assert.That(state.Players[0].CommitQueue, Has.Count.EqualTo(2));
            Assert.That(state.Players[0].Hand, Is.Empty);
            Assert.That(state.Events.Items[^1].EventType, Is.EqualTo("EFFECT_SKIPPED"));
            Assert.That(state.Events.Items[^1].Data["reasonKey"], Is.EqualTo("target.selection_required"));
        });
    }

    private static CardInstance QueueCard(long id, int ownerIndex = 0)
    {
        return new CardInstance(id, ownerIndex, new CardDefinition(
            "queued_" + id,
            "Queued " + id,
            attack: 1,
            health: 2,
            isMinion: true,
            faction: "机械遗迹",
            tags: new[] { "机械" }));
    }

    private static GameState CreateActionState(out TurnFlow flow, out TurnActionRouter router)
    {
        var state = CreateActionState(out flow);
        router = TurnActionRouter.CreateDefault(flow);
        return state;
    }

    private static GameState CreateActionState(out TurnFlow flow)
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        flow = TurnFlow.CreateDefault();
        flow.Advance(state, 0);
        Assert.That(flow.TryExecutePhaseAction(state, 0, TurnAction.SkipAmbush), Is.True);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
        return state;
    }
}

/// <summary>
/// End-to-end gate for the same approved action: an advertised ROLLBACK has to
/// survive the real adapter boundary, not just the engine. The match is composed
/// by <see cref="MatchFactory"/> from the shipped card catalog and driven only
/// through <see cref="RuntimeMatchGateway"/>, so every submission is a real wire
/// <see cref="RuntimeGameAction"/> built from a viewer-safe advertisement and both
/// <see cref="RuntimeActionBoundary.Validate"/> and the turn router run.
/// </summary>
[TestFixture]
public sealed class RollbackGatewayRoundTripTests
{
    /// <summary>
    /// Assertions 1 and 2: the advertised action is accepted across the boundary,
    /// the chosen queue card moves back to hand, and no other card moves.
    /// </summary>
    [Test]
    public void AdvertisedRollbackSurvivesTheGatewayAndMovesOnlyTheChosenQueueCard()
    {
        var driver = GatewayDriver.Create();
        var start = driver.StartSnapshot;
        var action = driver.DriveToActionPhase();
        var chosen = driver.SeedFieldMechanical("machine_drone");
        var uncommitted = driver.SeedFieldMechanical("machine_drone");
        action = driver.Commit(action, chosen);
        var queued = action.Snapshot;

        var view = driver.OwnerView();
        var advertised = view.LegalActions.Single(item => item.ActionId == RollbackActionId(chosen));
        var opponentPunishBefore = driver.OpponentPunishDrawnThisTurn();
        var submission = driver.Submit(view, advertised);
        var after = driver.OwnerView();

        Assert.Multiple(() =>
        {
            Assert.That(start.Players[0].CommitQueue, Is.Empty,
                "the composed match starts with an empty commit queue");
            Assert.That(start.LegalActions.Select(item => item.Type),
                Does.Not.Contain(LegalActionGenerator.Rollback));
            Assert.That(queued.Players[0].CommitQueue, Has.Count.EqualTo(1));
            Assert.That(advertised.Type, Is.EqualTo(LegalActionGenerator.Rollback));
            Assert.That(AdvertisedEntityId(advertised), Is.EqualTo(chosen.InstanceId),
                "the advertisement identifies the queue card the player chooses");
            Assert.That(advertised.ReasonKey, Is.EqualTo("action.rollback"));
            Assert.That(submission.Result.Accepted, Is.True, submission.Result.ReasonKey);
            Assert.That(submission.Result.ResultingSnapshotRevision,
                Is.EqualTo(advertised.SnapshotRevision + 1));
            Assert.That(submission.Snapshot.SnapshotRevision,
                Is.EqualTo(submission.Result.ResultingSnapshotRevision));
            Assert.That(opponentPunishBefore, Is.EqualTo(1),
                "the earlier COMMIT's punish draw is what makes this match non-trivial");
            Assert.That(after.Players[0].CommitQueue, Is.Empty,
                "the chosen card left the queue and no other queue card remained");
            Assert.That(after.Players[0].Hand.Select(item => item.EntityId),
                Does.Contain(chosen.InstanceId), "the rollback card is back in hand");
            Assert.That(after.Players[0].Field.Select(item => item.EntityId),
                Does.Not.Contain(chosen.InstanceId));
            Assert.That(after.Players[0].Field.Select(item => item.EntityId),
                Does.Contain(uncommitted.InstanceId),
                "the minion that was never committed must not be moved by the rollback");
        });
    }

    /// <summary>
    /// Assertion 3, the rule this revision implements: nothing anywhere on the
    /// wire path charges or refunds anything. The earlier COMMIT's punish draw is
    /// still there and the rollback adds none for either player.
    /// </summary>
    [Test]
    public void GatewayRollbackDrawsNoCardAndLeavesEveryOpponentCounterUntouched()
    {
        var driver = GatewayDriver.Create();
        var action = driver.DriveToActionPhase();
        var chosen = driver.SeedFieldMechanical("machine_drone");
        action = driver.Commit(action, chosen);
        var viewBefore = driver.OwnerView();
        var before = driver.OpponentCounters();
        var advertised = viewBefore.LegalActions.Single(item => item.ActionId == RollbackActionId(chosen));

        var submission = driver.Submit(viewBefore, advertised);
        var after = driver.OpponentCounters();
        var viewAfter = driver.OwnerView();

        Assert.Multiple(() =>
        {
            Assert.That(submission.Result.Accepted, Is.True, submission.Result.ReasonKey);
            Assert.That(before.PunishDrawnThisTurn, Is.EqualTo(1),
                "the fixture must prove the lifecycle punish chain is live: the earlier COMMIT drew exactly one");
            Assert.That(before.HandCount,
                Is.EqualTo(viewBefore.Players[1].HandCount),
                "the counter is read from the same state the snapshot projects");
            Assert.That(after.PunishDrawnThisTurn, Is.EqualTo(before.PunishDrawnThisTurn),
                "ROLLBACK draws no card for the opponent");
            Assert.That(after.PunishDeltaThisTurn, Is.EqualTo(before.PunishDeltaThisTurn),
                "ROLLBACK does not re-charge or refund the earlier COMMIT fee");
            Assert.That(after.HandCount, Is.EqualTo(before.HandCount));
            Assert.That(after.DeckCount, Is.EqualTo(before.DeckCount));
            Assert.That(after.Life, Is.EqualTo(before.Life));
            Assert.That(viewAfter.Players[1].HandCount, Is.EqualTo(viewBefore.Players[1].HandCount));
            Assert.That(viewAfter.Players[1].DeckCount, Is.EqualTo(viewBefore.Players[1].DeckCount));
            Assert.That(viewAfter.Players[1].Life, Is.EqualTo(viewBefore.Players[1].Life));
            Assert.That(viewAfter.Players[0].DeckCount, Is.EqualTo(viewBefore.Players[0].DeckCount),
                "the acting player's own deck is untouched too");
            Assert.That(submission.Events.Select(item => item.EventType), Does.Contain("CARD_ROLLED_BACK"));
            Assert.That(submission.Events.Select(item => item.EventType), Does.Not.Contain("PUNISH_DRAW"));
            Assert.That(submission.Events.Select(item => item.EventType), Does.Not.Contain("PUNISH_DELTA_APPLIED"));
            Assert.That(submission.Events.Select(item => item.EventType), Does.Not.Contain("CARD_DRAWN"));
        });
    }

    /// <summary>
    /// Assertion 4: the advertisement appears only while the queue is non-empty,
    /// observed through the gateway rather than through the generator directly.
    /// </summary>
    [Test]
    public void GatewayAdvertisesRollbackOnlyWhileTheQueueIsNotEmpty()
    {
        var driver = GatewayDriver.Create();
        var action = driver.DriveToActionPhase();
        var phaseSnapshot = action.Snapshot;
        var chosen = driver.SeedFieldMechanical("machine_drone");
        driver.SeedFieldMechanical("machine_drone");
        var seedSnapshot = driver.OwnerView();
        action = driver.Commit(action, chosen);
        var queuedSnapshot = action.Snapshot;
        var view = driver.OwnerView();
        var advertised = view.LegalActions.Single(item => item.ActionId == RollbackActionId(chosen));
        var rolledBack = driver.Submit(view, advertised);
        var drainedSnapshot = driver.OwnerView();

        Assert.Multiple(() =>
        {
            Assert.That(phaseSnapshot.Phase, Is.EqualTo("ACTION"));
            Assert.That(phaseSnapshot.Players[0].CommitQueueCount, Is.Zero);
            Assert.That(phaseSnapshot.LegalActions.Any(item => item.Type == LegalActionGenerator.Rollback),
                Is.False, "an empty commit queue advertises no rollback through the gateway");
            Assert.That(phaseSnapshot.Players[0].FieldCount, Is.Zero);
            Assert.That(seedSnapshot.Players[0].FieldCount, Is.EqualTo(2),
                "the two seeded minions are on the field before anything is committed");
            Assert.That(seedSnapshot.Players[0].CommitQueueCount, Is.Zero);
            Assert.That(seedSnapshot.LegalActions.Any(item => item.Type == LegalActionGenerator.Rollback),
                Is.False, "a filled field with an empty queue still advertises no rollback");
            Assert.That(queuedSnapshot.Players[0].CommitQueueCount, Is.EqualTo(1));
            Assert.That(queuedSnapshot.LegalActions.Select(item => item.Type),
                Does.Contain(LegalActionGenerator.Rollback));
            Assert.That(rolledBack.Result.Accepted, Is.True, rolledBack.Result.ReasonKey);
            Assert.That(drainedSnapshot.Players[0].CommitQueueCount, Is.Zero);
            Assert.That(drainedSnapshot.LegalActions.Any(item => item.Type == LegalActionGenerator.Rollback),
                Is.False, "once the queue is drained the action disappears again");
        });
    }

    /// <summary>
    /// Assertion 5: the non-owner's viewer-scoped snapshot and event delta gain
    /// no hidden information from this action. The commit queue is public; hands
    /// are not, and the delivered delta stays exactly the redaction boundary's
    /// output for that viewer.
    /// </summary>
    [Test]
    public void OpponentViewerGainsNoHiddenInformationFromTheRollback()
    {
        var driver = GatewayDriver.Create();
        var action = driver.DriveToActionPhase();
        var chosen = driver.SeedFieldMechanical("machine_drone");
        action = driver.Commit(action, chosen);
        var ownerView = driver.OwnerView();
        // Both viewers must hold the same post-commit snapshot, so the
        // non-owner's own advertisement is a legal, already-shown choice.
        var opponentView = driver.OpponentView();
        var advertised = ownerView.LegalActions.Single(item => item.ActionId == RollbackActionId(chosen));
        var opponentVisibleBefore = VisibleCardIdentity(opponentView);
        var eventsBefore = driver.EngineEventCount();

        var submission = driver.Submit(opponentView, advertised);
        var opponentAfter = driver.OpponentView();

        Assert.Multiple(() =>
        {
            Assert.That(submission.Result.Accepted, Is.True, submission.Result.ReasonKey);
            Assert.That(opponentView.Players[0].CommitQueue.Select(item => item.EntityId),
                Is.EqualTo(ownerView.Players[0].CommitQueue.Select(item => item.EntityId)),
                "the caller may only submit through an advertisement it was actually shown");
            Assert.That(opponentView.LegalActions, Is.Empty,
                "a non-current viewer is offered no action at all, so no rollback either");
            Assert.That(opponentAfter.Players[0].Hand, Is.Empty,
                "the acting player's hand stays hidden from the non-owner viewer");
            Assert.That(opponentAfter.Players[0].CommitQueue, Is.Empty,
                "the rollback is visible to the opponent only as a zone count change");
            Assert.That(VisibleCardIdentity(opponentAfter), Is.EqualTo(opponentVisibleBefore),
                "the rollback reveals nothing the non-owner could not already see");
            Assert.That(submission.Events.Select(item => item.EventType),
                Does.Contain("ROLLBACK_DECLARED"));
            Assert.That(submission.Events.Select(item => item.EventType),
                Does.Contain("CARD_ROLLED_BACK"));
            Assert.That(submission.Events.Select(item => item.EventType),
                Is.EqualTo(RuntimeMatchGateway.GetViewerScopedEvents(
                        driver.EngineEventsFrom(eventsBefore), 1)
                    .Select(item => item.EventType)),
                "the delivered delta is exactly the redaction boundary's projection of the new events");
            Assert.That(submission.Events.All(item => !item.Data.ContainsKey("cardId")), Is.True,
                "no rollback event carries a card identity to the non-owner");
            Assert.That(submission.Events.Select(item => item.EventType), Does.Not.Contain("PUNISH_DRAW"));
        });
    }

    private static string RollbackActionId(CardInstance card)
    {
        return "rollback_" + card.InstanceId;
    }

    /// <summary>
    /// Reads the entity id out of an advertised source, which the boundary may
    /// carry as a bare number or as an <c>entity_…</c> string.
    /// </summary>
    private static long AdvertisedEntityId(RuntimeLegalAction action)
    {
        var text = Convert.ToString(action.SourceId, System.Globalization.CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(text))
        {
            return -1;
        }

        if (text.StartsWith("entity_", StringComparison.Ordinal))
        {
            text = text.Substring("entity_".Length);
        }

        return long.TryParse(text, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var id)
            ? id
            : -1;
    }

    /// <summary>
    /// Everything the non-owner viewer is allowed to see EXCEPT the public zone
    /// counts the action is supposed to change: hand size, field, deck, life and
    /// this viewer's own zones. The commit queue is public information, so a
    /// count change there is expected; nothing else may move.
    /// </summary>
    private static string VisibleCardIdentity(RuntimeSnapshotEnvelope snapshot)
    {
        var parts = new List<string>();
        foreach (var player in snapshot.Players)
        {
            parts.Add("hand:" + player.Hand.Count);
            parts.Add("deck:" + player.DeckCount);
            parts.Add("life:" + player.Life);
            parts.Add("graveyard:" + player.GraveyardCount);
            parts.Add("cloud:" + player.CloudStackCount);
            foreach (var card in player.Field)
            {
                parts.Add("field:" + card.CardId);
            }

            foreach (var card in player.Hand)
            {
                parts.Add("revealedHand:" + card.CardId);
            }
        }

        return string.Join("|", parts);
    }

    /// <summary>
    /// A data-backed match composed by the production factory and driven only
    /// through the transport gateway, exactly as a client transport would. The
    /// driver keeps the engine state because the wire snapshot deliberately does
    /// not expose the punish counters that assertion 3 has to pin.
    /// </summary>
    private sealed class GatewayDriver
    {
        private const string MatchId = "match_rollback_gateway";

        private static MatchSetupOptions NewOptions()
        {
            return new MatchSetupOptions
            {
                Seed = 20260911,
                FirstPlayerIndex = 0,
                OpeningHandSize = 5,
                PlayerLife = 20,
                CastleEnabled = false,
            };
        }

        private readonly GameState _state;

        private GatewayDriver(GameState state, RuntimeMatchGateway gateway, RuntimeSnapshotEnvelope start)
        {
            _state = state;
            Gateway = gateway;
            StartSnapshot = start;
        }

        public RuntimeMatchGateway Gateway { get; }
        public RuntimeSnapshotEnvelope StartSnapshot { get; }

        public static GatewayDriver Create()
        {
            var root = FindRepositoryRoot();
            var catalog = CardCatalog.LoadDirectory(Path.Combine(root, "data", "cards"));
            var machineDeck = DataBackedDeck(
                "machine_rollback_fixture", "机械遗迹", "machine_leader", "machine_drone", 16);
            var woodDeck = DataBackedDeck(
                "wood_rollback_opponent", "古木圣地", "wood_leader", "wood_sapling", 24);
            Assert.That(machineDeck.Cards.Keys, Is.SubsetOf(catalog.Cards.Keys),
                "the fixture deck must be composed from the shipped card catalog");
            Assert.That(woodDeck.Cards.Keys, Is.SubsetOf(catalog.Cards.Keys));

            // Compose the match through the production factory so deck handling,
            // setup and validation are all the shipped ones...
            var composed = MatchFactory.CreateFromDecks(MatchId, catalog, machineDeck, woodDeck, NewOptions());
            var start = composed.GetSnapshot(0);
            Assert.That(start.Players[0].CommitQueue, Is.Empty);

            // ...then build the identical match (same seed, same options) for the
            // gateway this driver exercises, because the factory does not hand
            // back the GameState the punish counters live on.
            var state = MatchSetup.Create(
                new MatchDeckSpec(machineDeck.Leader, machineDeck.Cards, machineDeck.Name, machineDeck.Faction),
                new MatchDeckSpec(woodDeck.Leader, woodDeck.Cards, woodDeck.Name, woodDeck.Faction),
                catalog.Cards,
                NewOptions());
            var flow = TurnFlow.CreateDefault();
            var gateway = new RuntimeMatchGateway(
                MatchId, state, flow, TurnActionRouter.CreateDefault(flow));
            var initialization = gateway.Initialize(0);
            Assert.That(initialization.Accepted, Is.True, initialization.ReasonKey);
            return new GatewayDriver(state, gateway, start);
        }

        public RuntimeSnapshotEnvelope OwnerView()
        {
            return Gateway.GetSnapshot(0);
        }

        public RuntimeSnapshotEnvelope OpponentView()
        {
            return Gateway.GetSnapshot(1);
        }

        public (int PunishDrawnThisTurn, int PunishDeltaThisTurn, int HandCount, int DeckCount, int Life)
            OpponentCounters()
        {
            var opponent = _state.GetPlayer(1);
            var view = OwnerView();
            return (opponent.PunishDrawnThisTurn, opponent.PunishDeltaThisTurn,
                view.Players[1].HandCount, view.Players[1].DeckCount, view.Players[1].Life ?? 0);
        }

        public IReadOnlyList<GameEvent> EngineEvents()
        {
            return _state.Events.Items;
        }

        public int EngineEventCount()
        {
            return _state.Events.Count;
        }

        public IReadOnlyList<GameEvent> EngineEventsFrom(int index)
        {
            return _state.Events.Items.Skip(index).ToArray();
        }

        /// <summary>Submits an advertised action exactly as a client would.</summary>
        public RuntimeActionSubmission Submit(
            RuntimeSnapshotEnvelope snapshot,
            RuntimeLegalAction advertised)
        {
            return Gateway.Submit(new RuntimeGameAction
            {
                ContractVersion = advertised.ContractVersion,
                MatchId = snapshot.MatchId,
                SnapshotRevision = advertised.SnapshotRevision,
                ActionId = advertised.ActionId,
                Type = advertised.Type,
                Actor = advertised.Actor,
                SourceId = advertised.SourceId,
                TargetId = advertised.TargetId,
                CardId = advertised.CardId,
                Payload = advertised.Payload,
            });
        }

        public RuntimeActionSubmission DriveToActionPhase()
        {
            var ownerView = OwnerView();
            var skips = ownerView.LegalActions
                .Where(item => item.Type == TurnAction.SkipAmbush)
                .ToArray();
            Assert.That(skips, Has.Length.EqualTo(1), Describe(ownerView));
            var submission = Submit(ownerView, skips[0]);
            Assert.That(submission.Result.Accepted, Is.True, submission.Result.ReasonKey);
            Assert.That(submission.Snapshot.Phase, Is.EqualTo(TurnPhase.Action));
            return submission;
        }

        /// <summary>
        /// Moves one shipped mechanical minion from the deck onto the field so
        /// the commit queue can be filled deterministically. The card instance
        /// and its definition both come from the shipped catalog; only its zone
        /// is chosen by the fixture, because the engine allows one card play per
        /// turn and a two-minion fixture would otherwise need extra turns.
        /// </summary>
        public CardInstance SeedFieldMechanical(string cardId)
        {
            var owner = _state.GetPlayer(0);
            var card = owner.Deck.FirstOrDefault(candidate => candidate.Definition.Id == cardId);
            Assert.That(card, Is.Not.Null, "the fixture deck must contain " + cardId);
            owner.Deck.Remove(card!);
            owner.Field.Add(card!);
            return card!;
        }

        /// <summary>
        /// The opponent's punish counter. The wire snapshot deliberately does not
        /// expose it, so assertion 3 reads the authoritative engine state.
        /// </summary>
        public int OpponentPunishDrawnThisTurn()
        {
            return _state.GetPlayer(1).PunishDrawnThisTurn;
        }

        public RuntimeActionSubmission Commit(RuntimeActionSubmission current, CardInstance card)
        {
            var ownerView = OwnerView();
            var matches = ownerView.LegalActions
                .Where(item => item.Type == LegalActionGenerator.Commit
                    && SourceEntityId(item.SourceId) == card.InstanceId)
                .ToArray();
            Assert.That(matches, Has.Length.EqualTo(1), Describe(ownerView));
            var submission = Submit(ownerView, matches[0]);
            Assert.That(submission.Result.Accepted, Is.True, submission.Result.ReasonKey);
            return submission;
        }

        /// <summary>
        /// Reads the entity id out of an advertised source, which the boundary
        /// may carry as a bare number or as an <c>entity_…</c> string.
        /// </summary>
        private static long SourceEntityId(object? sourceId)
        {
            var text = Convert.ToString(sourceId, System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(text))
            {
                return -1;
            }

            if (text.StartsWith("entity_", StringComparison.Ordinal))
            {
                text = text.Substring("entity_".Length);
            }

            return long.TryParse(text, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var id)
                ? id
                : -1;
        }

        private static string Describe(RuntimeSnapshotEnvelope ownerView)
        {
            return "phase=" + ownerView.Phase
                + " current=" + ownerView.CurrentPlayer
                + " queueCount=" + ownerView.Players[0].CommitQueueCount
                + " fieldCount=" + ownerView.Players[0].FieldCount
                + " handCount=" + ownerView.Players[0].HandCount
                + " actions=[" + string.Join(", ", ownerView.LegalActions.Select(
                    item => item.Type + ":" + item.ActionId + ":" + Convert.ToString(
                        item.SourceId, System.Globalization.CultureInfo.InvariantCulture))) + "]";
        }

        private static DeckDefinition DataBackedDeck(
            string name,
            string faction,
            string leader,
            string cardId,
            int copies)
        {
            return new DeckDefinition(
                name,
                faction,
                leader,
                new Dictionary<string, int>(StringComparer.Ordinal) { [cardId] = copies });
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "data", "schema", "cards.schema.json")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
}