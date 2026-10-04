using System;
using System.Linq;
using System.Reflection;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// Prints the real constructor signatures of the engine model types the curve harness needs to
/// rewrite. Written because the alternative is guessing a 30-parameter constructor and trusting a
/// compile error to teach me the order — and because "read the real shape instead of assuming it"
/// is the lesson this session keeps re-learning.
/// </summary>
public sealed class ModelShapeDumpTests
{
    [Test]
    public void DumpsCardDefinitionConstructors()
    {
        var type = typeof(CardDefinition);
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .ToList();

        TestContext.Out.WriteLine("CardDefinition: {0} public constructors", constructors.Count);
        var index = 0;
        foreach (var constructor in constructors)
        {
            index++;
            var parameters = constructor.GetParameters()
                .Select(p => p.ParameterType.Name + " " + p.Name + (p.HasDefaultValue ? " = " + (p.DefaultValue ?? "null") : string.Empty));
            TestContext.Out.WriteLine("  #{0} ({1} params)", index, constructor.GetParameters().Length);
            foreach (var parameter in parameters)
            {
                TestContext.Out.WriteLine("      {0}", parameter);
            }
        }

        Assert.That(constructors, Is.Not.Empty, "CardDefinition must expose a public constructor");
    }
}
}
