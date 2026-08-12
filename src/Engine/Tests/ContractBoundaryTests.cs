using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Randomness;
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
        Assert.That(game.Dispatcher.RegisteredActions, Is.EquivalentTo(expected));
        Assert.That(EffectNames.All, Is.EquivalentTo(expected));
        Assert.That(game.Dispatcher.RegisteredActions, Has.Count.EqualTo(24));
    }

    [Test]
    public void PersistentAuraIsNotAnOrdinaryDispatcherAction()
    {
        var game = new EffectTestFixture();
        Assert.Multiple(() =>
        {
            Assert.That(game.Dispatcher.RegisteredActions, Does.Not.Contain("DISABLE_ENEMY_LEADER"));
            Assert.That(ReadSchemaPersistentActions(), Is.EqualTo(new[] { "DISABLE_ENEMY_LEADER" }));
        });
    }

    [Test]
    public void PersistentAuraHasDedicatedSchemaSlots()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ReadSchemaArrayItemReference("LeaderDef", "persistentEffects"),
                Is.EqualTo("#/$defs/PersistentEffectSpec"));
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

    private static IReadOnlyList<string> ReadSchemaPersistentActions()
    {
        var root = FindRepositoryRoot();
        var schemaPath = Path.Combine(root, "data", "schema", "cards.schema.json");
        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        return document.RootElement
            .GetProperty("$defs")
            .GetProperty("PersistentEffectAction")
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
