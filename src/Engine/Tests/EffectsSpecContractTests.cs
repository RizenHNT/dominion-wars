using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class EffectsSpecContractTests
{
    [Test]
    public void ContractContainsAllTwentyFourActionSections()
    {
        var contract = ReadContract();
        for (var index = 1; index <= 24; index++)
        {
            Assert.That(contract, Does.Contain($"### 3.{index} "), $"Missing action section 3.{index}");
        }
    }

    [Test]
    public void ContractNamesEveryRegisteredAction()
    {
        var contract = ReadContract();
        foreach (var action in EffectNames.All)
        {
            Assert.That(contract, Does.Contain($"### ").And.Contain(action), action);
        }
    }

    [Test]
    public void SchemaActionEnumMatchesDispatcherRegistry()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "data", "schema", "cards.schema.json")));
        var schemaActions = document.RootElement
            .GetProperty("$defs")
            .GetProperty("EffectAction")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();

        Assert.That(schemaActions, Is.EquivalentTo(EffectNames.All));
    }

    [Test]
    public void PersistentControlIsDocumentedOutsideOrdinaryActionSections()
    {
        var contract = ReadContract();
        Assert.Multiple(() =>
        {
            Assert.That(contract, Does.Contain("DISABLE_ENEMY_LEADER"));
            Assert.That(contract, Does.Contain("不属于这 24 个动作"));
            Assert.That(EffectNames.All, Does.Not.Contain("DISABLE_ENEMY_LEADER"));
        });
    }

    [Test]
    public void BuffZeroAmountEmitsContractFailureEvent()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Buff, "FRIENDLY_MINION", 0, "atk", game.Friendly.InstanceId);

        Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("EFFECT_SKIPPED"));
    }

    [Test]
    public void BuffNegativeAmountUsesDecisionBClampRules()
    {
        var game = new EffectTestFixture();
        game.Apply(EffectNames.Buff, "FRIENDLY_MINION", -99, "atk", game.Friendly.InstanceId);

        Assert.That(game.Friendly.Attack, Is.Zero);
    }

    [Test]
    public void BuffNegativeHealthCanRemainPendingInsideEffectChain()
    {
        var game = new EffectTestFixture();
        game.Friendly.Health = 1;
        game.Dispatcher.ApplyAll(
            new[]
            {
                new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", -3, "hp"),
                new EffectSpec(EffectNames.Heal, "FRIENDLY_MINION", 3),
            },
            game.Context(game.Friendly.InstanceId));

        Assert.That(game.State.Players[0].Field, Does.Contain(game.Friendly));
    }

    [Test]
    public void VulnerabilityWhitelistIsAppliedBeforeLeaderDamage()
    {
        var game = new EffectTestFixture();
        var immune = new CardDefinition("immune_contract", "Immune Contract Leader", 2, 8, isMinion: true, isLeader: true);
        var leader = new CardInstance(61, 1, immune) { IsLeaderEntity = true };
        game.State.Players[1].Field.Add(leader);
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Damage, "ENEMY_FACE", 3, kingSlayer: true),
            game.Context(selectedCoreTarget: CoreTarget.Leader));

        Assert.That(leader.Health, Is.EqualTo(8));
    }

    [Test]
    public void ExplicitEffectKingSlayerFlagTakesPrecedenceOverCardFallback()
    {
        var game = new EffectTestFixture();
        var legacy = new CardDefinition("legacy_contract", "Legacy Contract", 1, 2, isMinion: true, kingSlayer: true);
        var source = new CardInstance(62, 0, legacy);
        var leader = new CardInstance(63, 1, game.LeaderDefinition) { IsLeaderEntity = true };
        game.State.Players[0].Field.Add(source);
        game.State.Players[1].Field.Add(leader);
        var root = game.State.Events.Append("CARD_PLAYED");
        game.Dispatcher.Apply(
            new EffectSpec(EffectNames.Damage, "ENEMY_FACE", 3, kingSlayer: false),
            new EffectContext(0, root.EventId, source, playedCard: source, selectedCoreTarget: CoreTarget.Leader));

        Assert.That(leader.Health, Is.EqualTo(8));
    }

    [Test]
    public void InvalidTargetEmitsObservableFailureInsteadOfThrowing()
    {
        var game = new EffectTestFixture();
        Assert.DoesNotThrow(() => game.Apply(
            EffectNames.Damage, "ENEMY_MINION", 1, selectedTargetId: 999999));
        Assert.That(game.State.Events.Items[^1].EventType, Is.EqualTo("EFFECT_SKIPPED"));
    }

    [Test]
    public void EffectOrderIsTheArrayOrderDefinedByContract()
    {
        var game = new EffectTestFixture();
        game.Dispatcher.ApplyAll(
            new[]
            {
                new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", 2, "atk"),
                new EffectSpec(EffectNames.Buff, "FRIENDLY_MINION", -1, "atk"),
            },
            game.Context(game.Friendly.InstanceId));

        Assert.That(game.Friendly.Attack, Is.EqualTo(3));
    }

    private static string ReadContract()
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "effects.contract.md"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docs", "effects.contract.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new AssertionException("Repository root was not found.");
    }
}
}
