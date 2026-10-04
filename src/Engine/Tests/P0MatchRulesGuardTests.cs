using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using DominionWars.Engine.Rules;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// 2026-09-11 (P0-1 night shift): the two guard assertions that were the only
/// unique coverage in the now-deleted <c>P0PioneerPunishTests.cs</c>. That file
/// was a near-duplicate of <c>P0NightShiftTests.cs</c> (its "mirror" case was in
/// fact the same setup as its first case, while its own doc comment claimed
/// otherwise), so it was removed and only these two contract guards were kept.
/// </summary>
[TestFixture]
public sealed class P0MatchRulesGuardTests
{
    [Test]
    public void MatchRulesRejectsNegativePioneerPunishValues()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new MatchRules(pioneerOpponentPunishBonus: -1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new MatchRules(pioneerSelfPunishDiscount: -1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new MatchRules(pioneerHandLimitBonus: -1));
        });
    }

    [Test]
    public void ShippedPioneerDefaultsMatchBalanceJson()
    {
        var rules = new MatchRules();
        Assert.Multiple(() =>
        {
            Assert.That(rules.PioneerOpponentPunishBonus, Is.EqualTo(1));
            Assert.That(rules.PioneerSelfPunishDiscount, Is.EqualTo(0));
            Assert.That(rules.PioneerHandLimitBonus, Is.EqualTo(2));
        });

        var balancePath = FindBalanceJson();
        if (balancePath is null)
        {
            Assert.Ignore("data/balance.json not found from the test working directory; built-in defaults asserted above only.");
        }

        var json = JObject.Parse(File.ReadAllText(balancePath));
        Assert.Multiple(() =>
        {
            Assert.That(
                (int)json["pioneerOpponentPunishBonus"]!,
                Is.EqualTo(rules.PioneerOpponentPunishBonus),
                "built-in default must equal the shipped balance.json value, or a failed load would silently change the rules");
            Assert.That(
                (int)json["pioneerSelfPunishDiscount"]!,
                Is.EqualTo(rules.PioneerSelfPunishDiscount));
            Assert.That(
                (int)json["pioneerHandLimitBonus"]!,
                Is.EqualTo(rules.PioneerHandLimitBonus));
        });
    }

    private static string? FindBalanceJson()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "data", "balance.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
}
