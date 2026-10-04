using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DominionWars.Adapters;
using DominionWars.Data;
using DominionWars.Engine.Model;
using DominionWars.Engine.Rules;
using DominionWars.Engine.Setup;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// Coverage for the balance-table loader that makes <c>data/balance.json</c> the
/// real configuration source for the .NET engine.
/// <para>
/// The contract under test is behaviour preservation: the shipped file must
/// produce rules identical to the built-in defaults, so enabling the loader
/// cannot change a single shipped match. The tests also pin that a changed
/// value does take effect, that a missing file and a corrupt file fall back
/// identically but are reported differently, and that unread keys are tolerated
/// rather than silently applied.
/// </para>
/// </summary>
[TestFixture]
public sealed class BalanceTableTests
{
    private readonly List<string> _diagnostics = new List<string>();
    private readonly List<string> _warnings = new List<string>();

    [SetUp]
    public void ResetSinks()
    {
        _diagnostics.Clear();
        _warnings.Clear();
    }

    /// <summary>
    /// 2026-09-11：本测试**改过一次语义**。它原本断言"发行平衡表逐字段等于内建默认值"，
    /// 那是用来证明"接上 loader 不改变行为"的。owner 随后**有意**把
    /// `maxPunishResponsesPerRound` 从 0（不限制）改成 1（一轮 1 张响应），
    /// 于是"逐字段相等"不再成立、也不再是想要的性质。
    /// 现在断言的是两件真正长期成立的事：
    ///   ① loader 能解析发行文件、且没有走回退；
    ///   ② 尚未被改动的四个字段仍等于内建默认值；而 T1 字段是**发行值 1**、
    ///      内建回退仍是 **0**（回退永远保持"不限制"这一保守语义）。
    /// </summary>
    [Test]
    public void ShippedFileLoadsAndReflectsTheOwnerConfiguredCap()
    {
        var path = BalanceTable.TryResolveShippedPath();
        Assert.That(path, Is.Not.Null, "the repository data/balance.json must be findable from the test directory");

        var load = BalanceTable.Load(path!, _warnings.Add, _diagnostics.Add);
        var builtIn = new MatchRules();

        Assert.Multiple(() =>
        {
            Assert.That(load.Status, Is.EqualTo(BalanceStatus.Loaded), "the shipped balance table must parse");
            Assert.That(load.IsLoaded, Is.True);
            Assert.That(load.UsedFallback, Is.False);
            Assert.That(load.Rules.HandLimit, Is.EqualTo(builtIn.HandLimit));
            Assert.That(load.Rules.PioneerHandLimitBonus, Is.EqualTo(builtIn.PioneerHandLimitBonus));
            Assert.That(load.Rules.PioneerOpponentPunishBonus, Is.EqualTo(builtIn.PioneerOpponentPunishBonus));
            Assert.That(load.Rules.PioneerSelfPunishDiscount, Is.EqualTo(builtIn.PioneerSelfPunishDiscount));

            Assert.That(
                load.Rules.MaxPunishResponsesPerRound,
                Is.EqualTo(1),
                "owner decision 2026-09-11: 发行平衡表把「一轮 1 张响应上限」打开");
            Assert.That(
                builtIn.MaxPunishResponsesPerRound,
                Is.Zero,
                "内建回退必须保持 0 = 不限制：文件缺失/损坏时不能悄悄改变规则语义");
        });
    }

    [Test]
    public void LoadedFieldsEqualTheMatchRulesFieldList()
    {
        var path = BalanceTable.TryResolveShippedPath();
        Assert.That(path, Is.Not.Null);
        var load = BalanceTable.Load(path!, _warnings.Add, _diagnostics.Add);
        var properties = typeof(MatchRules)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                properties,
                Is.EquivalentTo(BalanceLoad.RuleKeyByField.Keys),
                "every MatchRules field must have exactly one balance.json key here; add the field in both places or neither");
            Assert.That(properties, Has.Length.EqualTo(5), "the .NET MatchRules surface is five fields today");
            Assert.That(load.IsLoaded, Is.True);
        });
    }

    [Test]
    public void ShippedUnreadKeysAreReportedNotApplied()
    {
        var path = BalanceTable.TryResolveShippedPath();
        Assert.That(path, Is.Not.Null);
        var load = BalanceTable.Load(path!, _warnings.Add, _diagnostics.Add);

        Assert.Multiple(() =>
        {
            Assert.That(load.Message, Does.Contain("no MatchRules counterpart"));
            Assert.That(_warnings, Is.Not.Empty, "every documented-but-unread key must be reported");
            Assert.That(_warnings.Any(message => message.Contains("openingHand")), Is.True);
            Assert.That(_warnings.Any(message => message.Contains("chainLimit")), Is.True);
        });
    }

    [Test]
    public void ChangedValueActuallyChangesTheRules()
    {
        WithBalanceFile(
            "{ \"handLimit\": 6, \"maxPunishResponsesPerRound\": 1 }",
            path =>
            {
                var load = BalanceTable.Load(path, _warnings.Add, _diagnostics.Add);
                Assert.Multiple(() =>
                {
                    Assert.That(load.Status, Is.EqualTo(BalanceStatus.Loaded));
                    Assert.That(
                        load.Rules.MaxPunishResponsesPerRound,
                        Is.EqualTo(1),
                        "editing data must be able to turn the T1 cap on");
                    Assert.That(load.Rules.HandLimit, Is.EqualTo(6));
                    Assert.That(
                        load.Rules.PioneerOpponentPunishBonus,
                        Is.EqualTo(new MatchRules().PioneerOpponentPunishBonus),
                        "an absent key keeps the built-in default");
                });
            });
    }

    [Test]
    public void MissingFileFallsBackIdenticallyAndIsReported()
    {
        var path = Path.Combine(ScratchDirectory(), "balance_table_tests_missing_" + Guid.NewGuid().ToString("N") + ".json");

        var load = BalanceTable.Load(path, _warnings.Add, _diagnostics.Add);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.False);
            Assert.That(load.Status, Is.EqualTo(BalanceStatus.MissingFile));
            Assert.That(load.UsedFallback, Is.True);
            Assert.That(load.Exception, Is.Null);
            Assert.That(load.Rules.HandLimit, Is.EqualTo(new MatchRules().HandLimit));
            Assert.That(load.Rules.PioneerHandLimitBonus, Is.EqualTo(new MatchRules().PioneerHandLimitBonus));
            Assert.That(load.Rules.PioneerOpponentPunishBonus, Is.EqualTo(new MatchRules().PioneerOpponentPunishBonus));
            Assert.That(load.Rules.PioneerSelfPunishDiscount, Is.EqualTo(new MatchRules().PioneerSelfPunishDiscount));
            Assert.That(load.Rules.MaxPunishResponsesPerRound, Is.EqualTo(new MatchRules().MaxPunishResponsesPerRound));
            Assert.That(_diagnostics, Has.Count.EqualTo(1), "a missing file is reported exactly once");
            Assert.That(
                _diagnostics[0],
                Does.Contain("与 data/balance.json 一致"),
                "the missing-file report must state that the fallback is known identical to disk");
        });
    }

    [Test]
    public void CorruptFileFallsBackIdenticallyWithoutThrowing()
    {
        WithBalanceFile(
            "{ \"handLimit\": 8, ",
            path =>
            {
                BalanceLoad load = null!;
                Assert.DoesNotThrow(() => load = BalanceTable.Load(path, _warnings.Add, _diagnostics.Add));

                Assert.Multiple(() =>
                {
                    Assert.That(load.Status, Is.EqualTo(BalanceStatus.Corrupt));
                    Assert.That(load.UsedFallback, Is.True);
                    Assert.That(load.Exception, Is.Not.Null);
                    Assert.That(load.Rules.HandLimit, Is.EqualTo(new MatchRules().HandLimit));
                    Assert.That(load.Rules.PioneerHandLimitBonus, Is.EqualTo(new MatchRules().PioneerHandLimitBonus));
                    Assert.That(load.Rules.PioneerOpponentPunishBonus, Is.EqualTo(new MatchRules().PioneerOpponentPunishBonus));
                    Assert.That(load.Rules.PioneerSelfPunishDiscount, Is.EqualTo(new MatchRules().PioneerSelfPunishDiscount));
                    Assert.That(load.Rules.MaxPunishResponsesPerRound, Is.EqualTo(new MatchRules().MaxPunishResponsesPerRound));
                    Assert.That(_diagnostics, Has.Count.EqualTo(2), "a corrupt table reports the failure and the fallback");
                    Assert.That(
                        _diagnostics[0],
                        Does.Contain("读取/解析失败"),
                        "the corrupt report must be distinguishable from the missing-file report");
                    Assert.That(
                        _diagnostics[1],
                        Does.Contain("无法确认与磁盘数据一致"),
                        "the corrupt fallback must state that it is not confirmable against disk");
                });
            });
    }

    [Test]
    public void CorruptFileThatIsNotAnObjectFallsBackIdentically()
    {
        WithBalanceFile(
            "[1, 2, 3]",
            path =>
            {
                var load = BalanceTable.Load(path, _warnings.Add, _diagnostics.Add);
                Assert.Multiple(() =>
                {
                    Assert.That(load.Status, Is.EqualTo(BalanceStatus.Corrupt));
                    Assert.That(load.Rules, Is.Not.Null);
                    Assert.That(load.Rules.HandLimit, Is.EqualTo(new MatchRules().HandLimit));
                });
            });
    }

    [Test]
    public void UnknownKeysAreToleratedAndReported()
    {
        WithBalanceFile(
            "{ \"handLimit\": 8, \"pioneerHandLimitBonus\": 2, \"pioneerOpponentPunishBonus\": 1, \"pioneerSelfPunishDiscount\": 0, \"maxPunishResponsesPerRound\": 0, \"someFutureKnob\": 3, \"nested\": { \"a\": 1 } }",
            path =>
            {
                var load = BalanceTable.Load(path, _warnings.Add, _diagnostics.Add);

                Assert.Multiple(() =>
                {
                    Assert.That(load.Status, Is.EqualTo(BalanceStatus.Loaded), "unknown keys must not make the table corrupt");
                    Assert.That(load.Rules.HandLimit, Is.EqualTo(8));
                    Assert.That(load.Message, Does.Contain("someFutureKnob"));
                    Assert.That(load.Message, Does.Contain("nested"));
                    Assert.That(_warnings.Any(message => message.Contains("someFutureKnob")), Is.True);
                    Assert.That(_diagnostics, Is.Empty, "an unknown key is not a fallback");
                });
            });
    }

    [Test]
    public void NegativeValueIsRejectedTheSameWayMatchRulesRejectsIt()
    {
        var direct = Assert.Throws<ArgumentOutOfRangeException>(
            () => new MatchRules(maxPunishResponsesPerRound: -1));

        WithBalanceFile(
            "{ \"maxPunishResponsesPerRound\": -1 }",
            path =>
            {
                var load = BalanceTable.Load(path, _warnings.Add, _diagnostics.Add);
                Assert.Multiple(() =>
                {
                    Assert.That(load.Status, Is.EqualTo(BalanceStatus.Corrupt));
                    Assert.That(load.Exception, Is.Not.Null);
                    Assert.That(load.Rules.MaxPunishResponsesPerRound, Is.Zero);
                    Assert.That(direct!.ParamName, Is.EqualTo("maxPunishResponsesPerRound"));
                    Assert.That(
                        load.Message,
                        Does.Contain("outside its allowed range"),
                        "the loader must reject a value the engine constructor would reject");
                });
            });
    }

    [Test]
    public void NonNumericValueIsRejectedInsteadOfThrowingLater()
    {
        WithBalanceFile(
            "{ \"handLimit\": \"eight\" }",
            path =>
            {
                var load = BalanceTable.Load(path, _warnings.Add, _diagnostics.Add);
                Assert.Multiple(() =>
                {
                    Assert.That(load.Status, Is.EqualTo(BalanceStatus.Corrupt));
                    Assert.That(load.Message, Does.Contain("must be a whole number"));
                    Assert.That(load.Rules.HandLimit, Is.EqualTo(new MatchRules().HandLimit));
                });
            });
    }

    [Test]
    public void ShippedCacheIsStableAndNonThrowing()
    {
        var first = BalanceTable.Shipped(_diagnostics.Add);
        var second = BalanceTable.Shipped(_diagnostics.Add);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(first.Rules, Is.Not.Null);
            Assert.That(first.Rules.HandLimit, Is.EqualTo(new MatchRules().HandLimit));
        });
    }

    [Test]
    public void MatchFactoryAppliesTheBalanceTableToAMatchSetupOptionsWithoutRules()
    {
        var gateway = CreateGatewayFromShippedRules();
        var shipped = BalanceTable.Shipped();

        Assert.Multiple(() =>
        {
            // 契约不是"等于内建默认值"，而是"等于发行平衡表读出来的规则" —— 后者才是
            // 数据源。2026-09-11 owner 把 T1 打开后，"等于默认值"已不再成立。
            Assert.That(GatewayState(gateway).Rules.HandLimit, Is.EqualTo(shipped.Rules.HandLimit));
            Assert.That(
                GatewayState(gateway).Rules.MaxPunishResponsesPerRound,
                Is.EqualTo(shipped.Rules.MaxPunishResponsesPerRound),
                "MatchFactory 必须把 balance.json 的规则送进真实对局");
            Assert.That(
                GatewayState(gateway).Rules.MaxPunishResponsesPerRound,
                Is.EqualTo(1),
                "发行配置是一轮 1 张响应上限；若这里变成 0，说明接线断了");
        });
    }

    [Test]
    public void MatchFactoryHonoursAChangedBalanceValue()
    {
        WithBalanceFile(
            "{ \"maxPunishResponsesPerRound\": 1 }",
            path =>
            {
                var gateway = CreateGatewayFromLoadedBalance(
                    BalanceTable.Load(path, _warnings.Add, _diagnostics.Add));

                Assert.That(
                    GatewayState(gateway).Rules.MaxPunishResponsesPerRound,
                    Is.EqualTo(1),
                    "a data-only edit must reach a real match without a code change");
            });
    }

    private static string ScratchDirectory()
    {
        // Kept out of the repository root: balance-loader scratch files belong
        // in the build output area, never next to data/balance.json.
        var directory = Path.Combine(
            FindRepositoryRoot(),
            "build-output",
            "pl-bal",
            "scratch",
            TestContext.CurrentContext.Test.Name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static RuntimeMatchGateway CreateGatewayFromLoadedBalance(BalanceLoad balance)
    {
        var (catalog, decks) = LoadProductionData();
        return MatchFactory.CreateFromDecks(
            "match_balance_wiring",
            catalog,
            decks[0],
            decks[1],
            new MatchSetupOptions { Seed = 4242 },
            null,
            balance);
    }

    private static RuntimeMatchGateway CreateGatewayFromShippedRules()
    {
        var (catalog, decks) = LoadProductionData();
        return MatchFactory.CreateWithShippedRulesFromDecks(
            "match_balance_wiring",
            catalog,
            decks[0],
            decks[1],
            new MatchSetupOptions { Seed = 4242 });
    }

    private static (CardCatalog Catalog, IReadOnlyList<DeckDefinition> Decks) LoadProductionData()
    {
        var root = FindRepositoryRoot();
        return (
            CardCatalog.LoadDirectory(Path.Combine(root, "data", "cards")),
            DeckLoader.LoadDirectory(Path.Combine(root, "data", "decks")));
    }

    private static GameState GatewayState(RuntimeMatchGateway gateway)
    {
        var field = typeof(RuntimeMatchGateway).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null, "the gateway must still own a GameState for this wiring assertion");
        return (GameState)field!.GetValue(gateway)!;
    }

    private static void WithBalanceFile(string json, Action<string> assert)
    {
        var path = Path.Combine(ScratchDirectory(), "balance_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        try
        {
            assert(path);
        }
        finally
        {
            File.Delete(path);
        }
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
