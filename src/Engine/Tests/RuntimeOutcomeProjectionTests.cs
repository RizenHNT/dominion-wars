using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DominionWars.Adapters;
using DominionWars.Data;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Events;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;
using NewtonsoftJsonSerializationException = Newtonsoft.Json.JsonSerializationException;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class RuntimeOutcomeProjectionTests
{
    [Test]
    public void NonTerminalSnapshotUsesDefaultSafeOutcomeFields()
    {
        var state = new GameState(new PlayerState(0, 0), new PlayerState(1, 20))
        {
            CastleEnabled = true,
            CastleHealth = 0,
        };

        var snapshot = RuntimeSnapshotProjection.ToSnapshot(
            state,
            "match_outcome",
            0,
            0,
            TurnFlow.CreateDefault());

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Phase, Is.Not.EqualTo(TurnPhase.Over));
            Assert.That(snapshot.WinnerPlayerIndex, Is.Null);
            Assert.That(snapshot.ReasonKey, Is.Null);
        });
    }

    [Test]
    public void TerminalSnapshotProjectsOnlyEngineWinnerAndReason()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20))
        {
            WinnerPlayerIndex = 1,
            WinReason = "win.deck_cycles",
        };
        var flow = TurnFlow.CreateDefault();
        flow.JumpTo(state, 0, TurnPhase.Over);

        var snapshot = RuntimeSnapshotProjection.ToSnapshot(
            state,
            "match_outcome",
            4,
            0,
            flow);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Phase, Is.EqualTo(TurnPhase.Over));
            Assert.That(snapshot.WinnerPlayerIndex, Is.EqualTo(1));
            Assert.That(snapshot.ReasonKey, Is.EqualTo("win.deck_cycles"));
            Assert.That(snapshot.LegalActions, Is.Empty);
        });
    }

    [Test]
    public void OutcomeFieldsAreNotInferredBeforeEngineMarksOver()
    {
        var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 0))
        {
            WinnerPlayerIndex = 0,
            WinReason = "win.royal_castle_break",
        };
        var snapshot = RuntimeSnapshotProjection.ToSnapshot(
            state,
            "match_outcome",
            5,
            0,
            TurnFlow.CreateDefault());

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Phase, Is.Not.EqualTo(TurnPhase.Over));
            Assert.That(snapshot.WinnerPlayerIndex, Is.Null);
            Assert.That(snapshot.ReasonKey, Is.Null);
        });
    }

    [Test]
    public void RuntimeOutcomeFieldsAreOptionalAndRoundTrip()
    {
        var legacy = JsonSerializer.Deserialize<RuntimeSnapshotEnvelope>(
            "{\"MatchId\":\"match_legacy\"}")!;
        Assert.Multiple(() =>
        {
            Assert.That(legacy.WinnerPlayerIndex, Is.Null);
            Assert.That(legacy.ReasonKey, Is.Null);
        });

        var source = new RuntimeSnapshotEnvelope
        {
            MatchId = "match_outcome",
            Phase = TurnPhase.Over,
            WinnerPlayerIndex = 1,
            ReasonKey = "win.deck_cycles",
        };
        var copy = JsonSerializer.Deserialize<RuntimeSnapshotEnvelope>(
            JsonSerializer.Serialize(source))!;

        Assert.Multiple(() =>
        {
            Assert.That(copy.WinnerPlayerIndex, Is.EqualTo(1));
            Assert.That(copy.ReasonKey, Is.EqualTo("win.deck_cycles"));
        });
    }

    [Test]
    public void GameOverEventEmitsCanonicalWinnerAndReasonKeys()
    {
        var gameEvent = new GameEvent(
            20,
            null,
            "GAME_WON",
            new Dictionary<string, object?>
            {
                ["player"] = 1,
                ["reasonKey"] = "win.deck_cycles",
            });

        var projected = EngineProjectionAdapter.ToEvent(gameEvent, 3, TurnPhase.Over);

        Assert.Multiple(() =>
        {
            Assert.That(projected.Type, Is.EqualTo("GAME_OVER"));
            Assert.That(projected.ReasonKey, Is.EqualTo("win.deck_cycles"));
            Assert.That(projected.Data.Keys, Is.EquivalentTo(new[] { "winnerPlayerIndex", "reasonKey" }));
            Assert.That(projected.Data["winnerPlayerIndex"], Is.EqualTo(1));
            Assert.That(projected.Data["reasonKey"], Is.EqualTo("win.deck_cycles"));
            Assert.That(projected.Data.ContainsKey("player"), Is.False);
        });
    }

    [Test]
    public void GameOverEventAlreadyUsingCanonicalKeysRemainsStable()
    {
        var gameEvent = new GameEvent(
            21,
            null,
            "GAME_WON",
            new Dictionary<string, object?>
            {
                ["winnerPlayerIndex"] = 0,
                ["reasonKey"] = "win.royal_castle_break",
            });

        var projected = EngineProjectionAdapter.ToEvent(gameEvent, 3, TurnPhase.Over);

        Assert.Multiple(() =>
        {
            Assert.That(projected.Data.Keys, Is.EquivalentTo(new[] { "winnerPlayerIndex", "reasonKey" }));
            Assert.That(projected.Data["winnerPlayerIndex"], Is.EqualTo(0));
            Assert.That(projected.Data["reasonKey"], Is.EqualTo("win.royal_castle_break"));
        });
    }

    [Test]
    public void GateOfFateWinGameUsesStableAsciiReasonForEngineProjection()
    {
        var repository = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (repository is not null
            && !File.Exists(Path.Combine(repository.FullName, "data", "cards", "neutral.json")))
        {
            repository = repository.Parent;
        }

        Assume.That(repository, Is.Not.Null, "The repository data root is required.");
        var catalog = CardCatalog.LoadDirectory(Path.Combine(repository!.FullName, "data", "cards"));
        var gateDefinition = catalog.Cards["gate_of_fate"];
        var gateEffect = gateDefinition.AmbushEffects.Single();
        var leaderDefinition = new CardDefinition(
            "leader",
            "Leader",
            isMinion: true,
            isLeader: true);
        var state = new GameState(
            new PlayerState(0, 20),
            new PlayerState(1, 20),
            cardLibrary: catalog.Cards.Values.Concat(new[] { leaderDefinition }));
        var gate = new CardInstance(1, 0, gateDefinition);
        var leader0 = new CardInstance(2, 0, leaderDefinition) { IsLeaderEntity = true };
        var leader1 = new CardInstance(3, 1, leaderDefinition) { IsLeaderEntity = true };
        state.GetPlayer(0).AmbushZone.Add(gate);
        state.GetPlayer(0).Field.Add(leader0);
        state.GetPlayer(1).Field.Add(leader1);

        var root = state.Events.Append("CARD_PLAYED");
        var context = new EffectContext(0, root.EventId, sourceCard: gate, playedCard: gate);
        EffectDispatcher.CreateDefault(new EffectRuntime(state)).Apply(
            gateEffect,
            context);

        var snapshot = RuntimeSnapshotProjection.ToSnapshot(
            state,
            "match_gate",
            1,
            0,
            TurnFlow.CreateDefault());
        var gameEvent = state.Events.Items.Single(item => item.EventType == "GAME_WON");
        var projected = EngineProjectionAdapter.ToEvent(gameEvent, state.Turn.Number, state.Turn.PhaseId);

        Assert.Multiple(() =>
        {
            Assert.That(state.Turn.PhaseId, Is.EqualTo(TurnPhase.Over));
            Assert.That(state.WinnerPlayerIndex, Is.EqualTo(0));
            Assert.That(state.WinReason, Is.EqualTo("win.gate_of_fate"));
            Assert.That(snapshot.WinnerPlayerIndex, Is.EqualTo(0));
            Assert.That(snapshot.ReasonKey, Is.EqualTo("win.gate_of_fate"));
            Assert.That(projected.Data["winnerPlayerIndex"], Is.EqualTo(0));
            Assert.That(projected.Data["reasonKey"], Is.EqualTo("win.gate_of_fate"));
            Assert.That(projected.ReasonKey, Is.EqualTo("win.gate_of_fate"));
            Assert.That(projected.Data.Keys, Is.EquivalentTo(new[] { "winnerPlayerIndex", "reasonKey" }));
            Assert.That(snapshot.ReasonKey, Does.Match("^[a-z0-9][a-z0-9_.-]*$"));
        });
    }

    [Test]
    public void FreeTextWinReasonWithoutSourceUsesDeterministicCollisionSafeAsciiKey()
    {
        static string ApplyFreeText(string value)
        {
            var leaderDefinition = new CardDefinition("leader", "Leader", isMinion: true, isLeader: true);
            var state = new GameState(new PlayerState(0, 20), new PlayerState(1, 20));
            state.GetPlayer(0).Field.Add(new CardInstance(1, 0, leaderDefinition) { IsLeaderEntity = true });
            state.GetPlayer(1).Field.Add(new CardInstance(2, 1, leaderDefinition) { IsLeaderEntity = true });
            var root = state.Events.Append("CARD_PLAYED");
            var context = new EffectContext(0, root.EventId);
            EffectDispatcher.CreateDefault(new EffectRuntime(state)).Apply(
                new EffectSpec(EffectNames.WinGame, param: value),
                context);
            return state.WinReason!;
        }

        var first = ApplyFreeText("命运 A");
        var second = ApplyFreeText("命运 A");
        var different = ApplyFreeText("命运 B");
        var encodedSingle = ApplyFreeText("命");
        var encodedLiteral = ApplyFreeText("win.encoded.547d");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.Not.EqualTo(different));
            Assert.That(first, Does.StartWith("win.encoded."));
            Assert.That(encodedSingle, Is.EqualTo("win.encoded.547d"));
            Assert.That(encodedLiteral, Does.StartWith("win.encoded."));
            Assert.That(encodedLiteral, Is.Not.EqualTo("win.encoded.547d"));
            Assert.That(first, Does.Match("^[a-z0-9][a-z0-9_.-]*$"));
        });
    }

    [Test]
    public void RuntimeSnapshotValidationEnforcesTerminalOutcomeSemantics()
    {
        static RuntimeSnapshotEnvelope Snapshot(string phase, int? winner, string? reason)
        {
            return new RuntimeSnapshotEnvelope
            {
                MatchId = "match_validation",
                Phase = phase,
                ViewerPlayerId = "player_0",
                Players = new[]
                {
                    new RuntimePlayerSnapshot { PlayerId = "player_0" },
                    new RuntimePlayerSnapshot { PlayerId = "player_1" },
                },
                WinnerPlayerIndex = winner,
                ReasonKey = reason,
            };
        }

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => RuntimeSnapshotProjection.Validate(
                Snapshot(TurnPhase.Action, null, null)));
            Assert.DoesNotThrow(() => RuntimeSnapshotProjection.Validate(
                Snapshot(TurnPhase.Over, 1, "win.deck_cycles")));
            Assert.Throws<ArgumentException>(() => RuntimeSnapshotProjection.Validate(
                Snapshot(TurnPhase.Over, null, "win.deck_cycles")));
            Assert.Throws<ArgumentException>(() => RuntimeSnapshotProjection.Validate(
                Snapshot(TurnPhase.Over, 1, "命运之门开启")));
            Assert.Throws<ArgumentException>(() => RuntimeSnapshotProjection.Validate(
                Snapshot(TurnPhase.Action, 1, null)));
            Assert.Throws<ArgumentException>(() => RuntimeSnapshotProjection.Validate(
                Snapshot(TurnPhase.Action, null, "win.deck_cycles")));
        });
    }

    [Test]
    public void RuntimeWireSerializationUsesLowerCamelCaseAndRoundTripsOutcome()
    {
        var source = new RuntimeSnapshotEnvelope
        {
            MatchId = "match_outcome",
            Phase = TurnPhase.Over,
            WinnerPlayerIndex = 1,
            ReasonKey = "win.deck_cycles",
        };

        var json = RuntimeWireSerializer.Serialize(source);
        var copy = RuntimeWireSerializer.Deserialize<RuntimeSnapshotEnvelope>(json);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"contractVersion\""));
            Assert.That(json, Does.Contain("\"matchId\""));
            Assert.That(json, Does.Contain("\"winnerPlayerIndex\""));
            Assert.That(json, Does.Contain("\"reasonKey\""));
            Assert.That(json, Does.Not.Contain("\"ContractVersion\""));
            Assert.That(copy.WinnerPlayerIndex, Is.EqualTo(1));
            Assert.That(copy.ReasonKey, Is.EqualTo("win.deck_cycles"));
        });
    }

    [Test]
    public void RuntimeWireSerializationRejectsUnknownOutcomeFields()
    {
        Assert.Throws<NewtonsoftJsonSerializationException>(() => RuntimeWireSerializer.Deserialize<RuntimeSnapshotEnvelope>(
            "{\"matchId\":\"match_unknown\",\"unknownOutcome\":true}"));
    }
}
}
