using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DominionWars.Adapters;
using DominionWars.Engine;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// P1/P2 correctness batch: hidden-information redaction, the previously dead
/// <see cref="EffectSpec.Condition"/>, the punish-delta lower bound, and the
/// player-facing reachability of ROLLBACK.
/// </summary>
[TestFixture]
public sealed class P1P2CorrectnessTests
{
    // ---------------------------------------------------------------- P1-1

    [Test]
    public void NonOwnerAmbushEventDeltaNeverCarriesTheAmbushCardId()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var ambush = new CardInstance(41, 0, HiddenAmbushDefinition("hidden_ambush"));
        state.GetPlayer(0).Hand.Add(ambush);
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        var gateway = new RuntimeMatchGateway("match_ambush_leak", state, flow, router);
        var initialization = gateway.Initialize(0);
        Assert.That(initialization.Snapshot.Phase, Is.EqualTo(TurnPhase.Ambush));

        var action = gateway.GetSnapshot(0).LegalActions.Single(item => item.Type == TurnAction.SetAmbush);
        var submission = gateway.Submit(new RuntimeGameAction
        {
            MatchId = "match_ambush_leak",
            SnapshotRevision = initialization.Snapshot.SnapshotRevision,
            ActionId = action.ActionId,
            Type = action.Type,
            Actor = 0,
            SourceId = action.SourceId,
            CardId = action.CardId,
            Payload = action.Payload,
        });

        // The owner keeps the truth...
        var setEvent = submission.Events.Single(item => item.EventType == "AMBUSH_SET");
        Assert.That(setEvent.Data["cardId"], Is.EqualTo("hidden_ambush"),
            "the ambush owner's own event delta must keep the card identity");

        // ...and the increment the transport boundary emits for a non-owner
        // carries no trace of the hidden card. The viewer-scoped projection is
        // fed straight into the 1.31 transport cursor: an event still carrying
        // cardId/sourceId is rejected there, so these assertions fail the moment
        // the boundary stops redacting.
        var viewer1Initialization = gateway.GetInitialization(1);
        var nonOwnerEvents = RuntimeMatchGateway.GetViewerScopedEvents(submission.Events, 1);
        var shippedAmbushSet = nonOwnerEvents.Single(item => item.EventType == "AMBUSH_SET");
        var cursor = new RuntimeEventCursor();
        var shipped = cursor.Accept(new RuntimeEventEnvelope
        {
            EventId = "evt_000000000001",
            Type = shippedAmbushSet.EventType,
            Turn = state.Turn.Number,
            Phase = TurnPhase.Ambush,
            SnapshotRevision = gateway.SnapshotRevision,
            Data = ShippedAmbushSetData(shippedAmbushSet),
        });

        Assert.Multiple(() =>
        {
            Assert.That(submission.Result.Accepted, Is.True, submission.Result.ReasonKey);
            Assert.That(
                shipped.Accepted,
                Is.True,
                "the non-owner event still carried a forbidden hidden-identity key: " + shipped.ReasonKey);
            Assert.That(ShippedAmbushSetData(shippedAmbushSet).Keys, Is.Empty,
                "a hidden ambush set has no schema-legal data field for a non-owner");
            Assert.That(JsonSerializer.Serialize(shippedAmbushSet), Does.Not.Contain("hidden_ambush"));
            Assert.That(JsonSerializer.Serialize(shippedAmbushSet), Does.Not.Contain("cardId"));
            Assert.That(JsonSerializer.Serialize(viewer1Initialization), Does.Not.Contain("hidden_ambush"));
            Assert.That(viewer1Initialization.Snapshot.Players[0].Ambush, Is.Empty);
        });

        // The owner still sees the set card and is never over-redacted.
        var viewer0Initialization = gateway.GetInitialization(0);
        Assert.Multiple(() =>
        {
            Assert.That(viewer0Initialization.Events.Single(item => item.EventType == "TURN_STARTED")
                .Data["player"], Is.EqualTo(0));
            Assert.That(gateway.GetSnapshot(0).Players[0].Ambush.Single().CardId,
                Is.EqualTo("hidden_ambush"));
            Assert.That(JsonSerializer.Serialize(viewer1Initialization),
                Does.Not.Contain("hidden_ambush"));
        });
    }

    [Test]
    public void AmbushTriggeredEventIsRedactedForTheOpponentOnly()
    {
        var data = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["player"] = 1,
            ["source"] = 41L,
            ["cardId"] = "hidden_ambush",
            ["kind"] = "NORMAL",
        };
        var triggered = new Engine.Events.GameEvent(9, 8, "AMBUSH_TRIGGERED", data);

        var owner = HiddenInformationRedaction.ToViewer(new[] { triggered }, 1).Single();
        var opponent = HiddenInformationRedaction.ToViewer(new[] { triggered }, 0).Single();

        Assert.Multiple(() =>
        {
            Assert.That(owner.Data["cardId"], Is.EqualTo("hidden_ambush"));
            Assert.That(owner.Data["source"], Is.EqualTo(41L));
            Assert.That(opponent.Data.Keys, Is.EquivalentTo(new[] { "player" }),
                "an opponent keeps only the owner label, never the hidden ambush identity");
            Assert.That(opponent.EventId, Is.EqualTo(triggered.EventId));
            Assert.That(opponent.ParentEventId, Is.EqualTo(triggered.ParentEventId));
        });
    }

    // ---------------------------------------------------------------- P1-3

    [Test]
    public void UnsatisfiedEffectConditionIsSkippedWithAnObservableReason()
    {
        var game = new EffectTestFixture();
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Damage, "ENEMY_PLAYER", 3, condition: "HAND_GE_1"),
            game.Context());

        var last = game.State.Events.Items[^1];
        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[1].Life, Is.EqualTo(20),
                "an unsatisfied effect condition must not resolve the effect");
            Assert.That(last.EventType, Is.EqualTo("EFFECT_SKIPPED"));
            Assert.That(last.Data["action"], Is.EqualTo(EffectNames.Damage));
            Assert.That(last.Data["reasonKey"], Is.EqualTo("effect.condition_not_met"));
        });
    }

    [Test]
    public void SatisfiedEffectConditionStillResolves()
    {
        var game = new EffectTestFixture();
        game.State.Players[0].Hand.Add(new CardInstance(50, 0, game.SoldierDefinition));
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Damage, "ENEMY_PLAYER", 3, condition: "HAND_GE_1"),
            game.Context());

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[1].Life, Is.EqualTo(17));
            Assert.That(game.State.Events.Items[^1].EventType, Is.Not.EqualTo("EFFECT_SKIPPED"));
        });
    }

    [Test]
    public void UnknownEffectConditionTokenFailsClosed()
    {
        var game = new EffectTestFixture();
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Damage, "ENEMY_PLAYER", 3, condition: "NO_SUCH_CONDITION"),
            game.Context());

        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[1].Life, Is.EqualTo(20));
            Assert.That(game.State.Events.Items[^1].Data["reasonKey"], Is.EqualTo("effect.condition_not_met"));
        });
    }

    [Test]
    public void EffectConditionReusesThePunishConditionGrammar()
    {
        var game = new EffectTestFixture();
        Assert.Multiple(() =>
        {
            Assert.That(PunishConditionEvaluator.IsSatisfied(game.State, 0, null), Is.True);
            Assert.That(PunishConditionEvaluator.IsSatisfied(game.State, 0, "ALWAYS"), Is.True);
            Assert.That(PunishConditionEvaluator.IsSatisfied(game.State, 0, "ENEMY_MINIONS_GE_2"), Is.True);
            Assert.That(PunishConditionEvaluator.IsSatisfied(game.State, 0, "ENEMY_MINIONS_GE_3"), Is.False);
            Assert.That(PunishConditionEvaluator.IsSatisfied(game.State, 0, "HAND_GE_1"), Is.False);
            Assert.That(PunishConditionEvaluator.IsSatisfied(game.State, 0, "SELF_LIFE_LE_20"), Is.True);
            Assert.That(PunishConditionEvaluator.IsSatisfied(game.State, 0, "SELF_LEADER_ON_FIELD"), Is.False);
        });
    }

    // ---------------------------------------------------------------- P2-1

    /// <summary>
    /// P2-1: the per-turn punish delta is clamped at zero when it is applied.
    /// Without that clamp a single oversized discount stays in the accumulator
    /// as a negative value and then silently cancels the printed punish of every
    /// later card in the same turn. The probe plays a card whose printed punish
    /// equals the opposing deck size, so the punish draw is unambiguous.
    /// </summary>
    [Test]
    public void NegativeDiscountDoesNotBleedIntoLaterCardsPrintedPunish()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var card = new CardInstance(60, 0, new CardDefinition(
            "delta_probe",
            "Delta Probe",
            punish: 2,
            onPlayEffects: new[] { new EffectSpec(EffectNames.Damage, "ENEMY_FACE", 1) }));
        state.Players[0].Hand.Add(card);
        state.Players[1].Deck.Add(new CardInstance(61, 1, new CardDefinition("opp_deck_a", "Opp Deck A")));
        state.Players[1].Deck.Add(new CardInstance(62, 1, new CardDefinition("opp_deck_b", "Opp Deck B")));

        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Ambush));
        var dispatcher = EffectDispatcher.CreateDefault(new EffectRuntime(state));

        // A discount worth far more than any punish in play, dispatched through
        // the production effect path.
        var deltaRoot = state.Events.Append("CARD_PLAYED");
        dispatcher.Apply(
            new EffectSpec(EffectNames.AddSelfPunishTurn, amount: -99),
            new EffectContext(0, deltaRoot.EventId, sourceCard: card, playedCard: card));

        Assert.That(router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush)).Accepted, Is.True);
        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Action));
        var playResult = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.PlayCard,
            sourceEntityId: card.InstanceId,
            targetId: "core:player_1:life"));
        Assert.That(playResult.Accepted, Is.True, playResult.ReasonKey);

        Assert.Multiple(() =>
        {
            Assert.That(state.Players[0].PunishDeltaThisTurn, Is.Zero,
                "the accumulated per-turn punish discount must not go below zero");
            var delta = state.Events.Items.Last(item => item.EventType == "PUNISH_DELTA_APPLIED");
            Assert.That(delta.Data["total"], Is.Zero,
                "the auditable delta event must report the clamped accumulator");

            // The card still pays its own printed punish (2): both cards the
            // opposing deck held move to the opposing hand.
            Assert.That(state.Players[1].Hand, Has.Count.EqualTo(2),
                "a negative discount must not cancel the card's printed punish");
            Assert.That(state.Players[1].Hand.Select(item => item.InstanceId),
                Is.EquivalentTo(new long[] { 61, 62 }));
            Assert.That(state.GetPlayer(1).PunishDrawnThisTurn, Is.GreaterThanOrEqualTo(0));
            Assert.That(state.Events.Items.Any(item => item.EventType == "PUNISH_DRAW"), Is.True);
            Assert.That(state.Players[1].Deck, Is.Empty);
            Assert.That(state.Players[1].Life, Is.EqualTo(19),
                "the play itself must still have resolved");
            Assert.That(state.Players[0].Hand, Is.Empty);
        });
    }

    /// <summary>
    /// The delta event reports both the requested amount and the clamped
    /// accumulator, so a discount that was cut off is auditable rather than
    /// silently absorbed.
    /// </summary>
    [Test]
    public void ApplyPunishDeltaClampsAtZeroAndReportsBothValues()
    {
        var game = new EffectTestFixture();
        game.State.Players[0].PunishDeltaThisTurn = 2;
        game.Apply(EffectNames.AddSelfPunishTurn, amount: -5);

        var delta = game.State.Events.Items.Last(item => item.EventType == "PUNISH_DELTA_APPLIED");
        Assert.Multiple(() =>
        {
            Assert.That(game.State.Players[0].PunishDeltaThisTurn, Is.Zero);
            Assert.That(delta.Data["requested"], Is.EqualTo(-5));
            Assert.That(delta.Data["amount"], Is.EqualTo(-2));
            Assert.That(delta.Data["total"], Is.Zero);
        });
    }

    // ---------------------------------------------------------------- P2-4
    // Re-pointed at the approved contract revision 1.31-player-rollback: the
    // two assertions this test used to make (ROLLBACK absent from the player
    // action enum, never advertised) described the gap, and are deliberately
    // inverted now that the owner approved ROLLBACK as the ninth player action.

    [Test]
    public void RollbackIsTheNinthPlayerActionWhileStayingInTheEffectVocabulary()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtimeActionTypes = ReadEnum(
            Path.Combine(repositoryRoot, "design", "runtime-kit-v1.31", "contracts", "schemas", "game_action.schema.json"));
        var effectActionTypes = ReadEffectActionEnum(
            Path.Combine(repositoryRoot, "data", "schema", "cards.schema.json"));

        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        state.Players[0].CommitQueue.Add(new CardInstance(71, 0, new CardDefinition("queued", "Queued")));
        Assert.That(state.Players[0].CommitQueue, Is.Not.Empty, "the fixture must make a rollback plausible");

        var advertisedWithQueue = GenerateAllPhases(state, 0);
        state.Players[0].CommitQueue.Clear();
        var advertisedWithoutQueue = GenerateAllPhases(state, 0);

        Assert.Multiple(() =>
        {
            Assert.That(runtimeActionTypes, Does.Contain("ROLLBACK"),
                "1.31-player-rollback grants ROLLBACK as the ninth player action");
            Assert.That(runtimeActionTypes, Has.Count.EqualTo(9));
            Assert.That(effectActionTypes, Does.Contain("ROLLBACK"),
                "ROLLBACK stays in the card-effect vocabulary as well");
            Assert.That(effectActionTypes, Does.Not.Contain("SET_AMBUSH"),
                "the two vocabularies are separate: SET_AMBUSH is player-only");
            Assert.That(advertisedWithQueue, Is.Not.Empty,
                "the sweep must still observe the ordinary phase actions");
            Assert.That(advertisedWithQueue, Does.Contain(LegalActionGenerator.EndTurn));
            Assert.That(advertisedWithQueue, Does.Contain("ROLLBACK"),
                "a non-empty commit queue must advertise the rollback");
            Assert.That(advertisedWithoutQueue, Does.Not.Contain("ROLLBACK"),
                "an empty commit queue must not advertise the rollback");
        });
    }

    /// <summary>
    /// Effect-only rollback path (the <c>machine_recycler</c> commit effect).
    /// Unchanged by contract revision 1.31-player-rollback, which only added a
    /// player-originated route into the same runtime movement.
    /// </summary>
    [Test]
    public void RollbackEffectStillMovesTheChosenQueueCardBackToHand()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
        var queued = new CardInstance(81, 0, new CardDefinition("queued", "Queued"));
        state.Players[0].CommitQueue.Add(queued);
        var root = state.Events.Append("CARD_PLAYED");
        var runtime = new EffectRuntime(state);

        runtime.Rollback(
            new EffectSpec(EffectNames.Rollback),
            new EffectContext(0, root.EventId, selectedTargetId: queued.InstanceId));

        Assert.Multiple(() =>
        {
            Assert.That(state.Players[0].CommitQueue, Is.Empty);
            Assert.That(state.Players[0].Hand, Has.Exactly(1).EqualTo(queued));
            Assert.That(state.Events.Items[^1].EventType, Is.EqualTo("CARD_ROLLED_BACK"));
        });
    }

    // ------------------------------------------------------------ helpers

    private static CardDefinition HiddenAmbushDefinition(string id) => new CardDefinition(
        id,
        "Hidden Ambush",
        type: "AMBUSH",
        ambushKind: "NORMAL",
        ambushTrigger: "OPPONENT_ATTACKS");

    /// <summary>
    /// Maps an engine AMBUSH_SET event onto its 1.31 transport data keys, using
    /// only the fields a viewer-safe event may carry. An event that still holds
    /// the hidden identity maps to <c>cardId</c>/<c>sourceId</c> here and is
    /// therefore rejected by <see cref="RuntimeEventCursor"/>.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ShippedAmbushSetData(
        Engine.Events.GameEvent gameEvent)
    {
        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var key in new[] { "amount", "cardId", "count", "reasonKey", "sourceId", "targetIds", "winnerPlayerIndex" })
        {
            if (gameEvent.Data.TryGetValue(key, out var value))
            {
                data[key] = value;
            }
        }

        return data;
    }

    private static IReadOnlyList<string> GenerateAllPhases(GameState state, int playerIndex)
    {
        var flow = TurnFlow.CreateDefault();
        var generator = new LegalActionGenerator();
        var types = new List<string>();
        foreach (var phase in new[]
        {
            TurnPhase.Start, TurnPhase.Ambush, TurnPhase.Action, TurnPhase.Discard, TurnPhase.End,
        })
        {
            flow.JumpTo(state, playerIndex, phase);
            foreach (var action in generator.Generate(state, playerIndex))
            {
                types.Add(action.Type);
            }
        }

        Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.End));
        Assert.That(flow.Route, Does.Contain(TurnPhase.Action));
        return types;
    }

    private static IReadOnlyList<string> ReadEnum(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .GetProperty("properties")
            .GetProperty("type")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadEffectActionEnum(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .GetProperty("$defs")
            .GetProperty("EffectAction")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
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
