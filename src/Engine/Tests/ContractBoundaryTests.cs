using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Randomness;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class ContractBoundaryTests
{
    [Test]
    public void UnknownActionFailsClosed()
    {
        var game = new EffectTestFixture();
        Assert.Throws<UnknownActionException>(() =>
            game.Dispatcher.Apply(new EffectSpec("NOT_A_REAL_ACTION"), game.Context()));
    }

    [Test]
    public void DispatcherRegistersExactlyTheSchemaActions()
    {
        var game = new EffectTestFixture();
        var expected = ReadSchemaActions();
        Assert.That(game.Dispatcher.RegisteredActions, Is.EquivalentTo(EffectNames.All));
        Assert.That(expected, Is.SupersetOf(EffectNames.All));
        Assert.That(expected, Is.EquivalentTo(EffectNames.DeclaredActions));
        Assert.That(game.Dispatcher.RegisteredActions, Has.Count.EqualTo(EffectNames.DeclaredActions.Count));
    }

    [Test]
    public void DeletedPersistentAuraIsAbsentFromDispatcherAndSchema()
    {
        var game = new EffectTestFixture();
        Assert.Multiple(() =>
        {
            Assert.That(game.Dispatcher.RegisteredActions, Does.Not.Contain("DISABLE_ENEMY_LEADER"));
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                FindRepositoryRoot(), "data", "schema", "cards.schema.json")));
            var defs = document.RootElement.GetProperty("$defs");
            Assert.That(defs.TryGetProperty("PersistentEffectAction", out _), Is.False);
            Assert.That(defs.TryGetProperty("PersistentEffectSpec", out _), Is.False);
            var leaderProperties = defs.GetProperty("LeaderDef").GetProperty("properties");
            Assert.That(leaderProperties.TryGetProperty("persistentEffects", out _), Is.False);
        });
    }

    [Test]
    public void PlayerActionConstantsMatchRuntimeContract131()
    {
        var expected = new[]
        {
            LegalActionGenerator.PlayCard,
            TurnAction.SetAmbush,
            TurnAction.SkipAmbush,
            LegalActionGenerator.Attack,
            LegalActionGenerator.Commit,
            LegalActionGenerator.Pull,
            TurnAction.DiscardComplete,
            LegalActionGenerator.EndTurn,
        };

        Assert.Multiple(() =>
        {
            Assert.That(ReadRuntimeActionTypes(), Is.EquivalentTo(expected));
            Assert.That(expected, Does.Not.Contain("CHOOSE_TARGET"));
            Assert.That(expected, Does.Not.Contain("ACTIVATE_PUNISH"));
            Assert.That(expected, Does.Not.Contain("USE_LEADER_ABILITY"));
        });
    }

    [Test]
    public void LeaderEffectSlotsUseEffectSpec()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ReadSchemaArrayItemReference("LeaderDef", "enterEffects"),
                Is.EqualTo("#/$defs/EffectSpec"));
            Assert.That(ReadSchemaArrayItemReference("LeaderDef", "punishEffects"),
                Is.EqualTo("#/$defs/EffectSpec"));
            Assert.That(ReadSchemaArrayItemReference("Card", "punishEffects"),
                Is.EqualTo("#/$defs/EffectSpec"));
            Assert.That(ReadSchemaArrayItemReference("Card", "ambushEffects"),
                Is.EqualTo("#/$defs/EffectSpec"));
            Assert.That(ReadSchemaArrayItemReference("Card", "chantEffects"),
                Is.EqualTo("#/$defs/EffectSpec"));
            Assert.That(ReadSchemaArrayItemReference("Card", "onOpponentDiscardEffects"),
                Is.EqualTo("#/$defs/EffectSpec"));
        });
    }

    [Test]
    public void EngineAssemblyDoesNotReferenceUnityEngine()
    {
        var references = typeof(GameState).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .ToArray();
        Assert.That(references, Has.None.EqualTo("UnityEngine"));
    }

    [Test]
    public void XoshiroSequenceIsDeterministicForSameSeed()
    {
        var left = new Xoshiro256StarStar(42);
        var right = new Xoshiro256StarStar(42);
        var first = Enumerable.Range(0, 16).Select(_ => left.NextUInt64()).ToArray();
        var second = Enumerable.Range(0, 16).Select(_ => right.NextUInt64()).ToArray();
        Assert.That(first, Is.EqualTo(second));
    }

    private static IReadOnlyList<string> ReadSchemaActions()
    {
        var root = FindRepositoryRoot();
        var schemaPath = Path.Combine(root, "data", "schema", "cards.schema.json");
        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        return document.RootElement
            .GetProperty("$defs")
            .GetProperty("EffectAction")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
    }

    private static string ReadSchemaArrayItemReference(string definitionName, string propertyName)
    {
        var root = FindRepositoryRoot();
        var schemaPath = Path.Combine(root, "data", "schema", "cards.schema.json");
        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        return document.RootElement
            .GetProperty("$defs")
            .GetProperty(definitionName)
            .GetProperty("properties")
            .GetProperty(propertyName)
            .GetProperty("items")
            .GetProperty("$ref")
            .GetString()!;
    }

    private static IReadOnlyList<string> ReadRuntimeActionTypes()
    {
        var root = FindRepositoryRoot();
        var schemaPath = Path.Combine(root, "design", "runtime-kit-v1.31", "contracts", "schemas", "game_action.schema.json");
        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        return document.RootElement
            .GetProperty("properties")
            .GetProperty("type")
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
