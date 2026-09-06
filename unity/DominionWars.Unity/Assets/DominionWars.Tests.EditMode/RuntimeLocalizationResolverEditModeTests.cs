#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Reflection;
using DominionWars.Engine.Localization;
using DominionWars.Unity.Runtime;
using NUnit.Framework;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeLocalizationResolverEditModeTests
{
    [Test]
    public void SupportedLanguageAliasesNormalizeToEngineLanguages()
    {
        Assert.That(RuntimeLocalizationResolver.NormalizeLanguage("en"), Is.EqualTo("en"));
        Assert.That(RuntimeLocalizationResolver.NormalizeLanguage("en-US"), Is.EqualTo("en"));
        Assert.That(RuntimeLocalizationResolver.NormalizeLanguage("zh-CN"), Is.EqualTo("zh"));
        Assert.That(RuntimeLocalizationResolver.NormalizeLanguage("ZH_cn"), Is.EqualTo("zh"));
        Assert.That(RuntimeLocalizationResolver.NormalizeLanguage("jp"), Is.EqualTo("jp"));
        Assert.That(RuntimeLocalizationResolver.NormalizeLanguage("ja-JP"), Is.EqualTo("jp"));
        Assert.That(RuntimeLocalizationResolver.NormalizeLanguage("ja_jp"), Is.EqualTo("jp"));
        Assert.That(RuntimeLocalizationResolver.NormalizeLanguage("fr-FR"), Is.EqualTo("en"));
        Assert.That(RuntimeLocalizationResolver.NormalizeLanguage(" "), Is.EqualTo("en"));
    }

    [Test]
    public void ResolverReusesExistingEngineLocalizationAfterLanguageNormalization()
    {
        var resolver = new RuntimeLocalizationResolver(new Localization());

        Assert.That(resolver.Get("action.attack", "ja-JP"), Is.EqualTo("攻撃"));
        Assert.That(resolver.Get("ui.your_turn", "zh-CN"), Is.EqualTo("你的回合"));
        Assert.That(resolver.Get("result.victory", "en-US"), Is.EqualTo("You win"));
    }

    [Test]
    public void KnownSemanticTokensResolveToStableKeysAndLocalizedText()
    {
        var resolver = new RuntimeLocalizationResolver();

        var phase = resolver.ResolveSemantic(RuntimeSemanticKind.Phase, "ACTION", "ja-JP");
        Assert.That(phase.IsKnownSemantic, Is.True);
        Assert.That(phase.UsedFallback, Is.False);
        Assert.That(phase.LocalizationKey, Is.EqualTo("phase.action"));
        Assert.That(phase.Text, Is.EqualTo("行動フェイズ"));

        var action = resolver.ResolveSemantic(RuntimeSemanticKind.Action, "END_TURN", "zh-CN");
        Assert.That(action.IsKnownSemantic, Is.True);
        Assert.That(action.LocalizationKey, Is.EqualTo("action.endTurn"));
        Assert.That(action.Text, Is.EqualTo("结束回合"));

        var target = resolver.ResolveSemantic(RuntimeSemanticKind.Target, "castle", "en");
        Assert.That(target.IsKnownSemantic, Is.True);
        Assert.That(target.LocalizationKey, Is.EqualTo("status.castle"));
        Assert.That(target.Text, Is.EqualTo("Royal Castle"));

        var zone = resolver.ResolveSemantic(RuntimeSemanticKind.Zone, "HAND", "zh");
        Assert.That(zone.IsKnownSemantic, Is.True);
        Assert.That(zone.LocalizationKey, Is.EqualTo("zone.hand"));
        Assert.That(zone.Text, Is.EqualTo("手牌"));

        var status = resolver.ResolveSemantic(RuntimeSemanticKind.Status, "KING_SLAYER", "jp");
        Assert.That(status.IsKnownSemantic, Is.True);
        Assert.That(status.LocalizationKey, Is.EqualTo("status.kingSlayer"));
        Assert.That(status.Text, Is.EqualTo("王殺し"));
    }

    [Test]
    public void UnknownProtocolTokenFailsClosedWithoutEchoingTokenOrIdentity()
    {
        const string unknownToken = "entity_000000000042/player_9";
        var resolver = new RuntimeLocalizationResolver();

        var localized = resolver.ResolveSemantic(RuntimeSemanticKind.Event, unknownToken, "zh-CN");

        Assert.That(localized.IsKnownSemantic, Is.False);
        Assert.That(localized.UsedFallback, Is.True);
        Assert.That(localized.LocalizationKey, Is.EqualTo(RuntimeLocalizationResolver.FallbackLocalizationKey));
        Assert.That(localized.Text, Is.EqualTo("不可用"));
        Assert.That(localized.Text, Does.Not.Contain("entity_000000000042"));
        Assert.That(localized.Text, Does.Not.Contain("player_9"));
        Assert.That(localized.LocalizationKey, Does.Not.Contain("entity_000000000042"));
        Assert.That(localized.LocalizationKey, Does.Not.Contain("player_9"));

        Assert.That(
            resolver.TryResolveSemanticKey(RuntimeSemanticKind.Target, unknownToken, out var fallbackKey),
            Is.False);
        Assert.That(fallbackKey, Is.EqualTo(RuntimeLocalizationResolver.FallbackLocalizationKey));
    }

    [Test]
    public void InjectableTableCanSupplyUnfrozenSemanticCopyWithoutChangingProtocolKeys()
    {
        var entries = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["event.gameOver"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = "Match result",
                ["zh"] = "对局结果",
                ["jp"] = "対局結果",
            },
        };
        var table = new RuntimeLocalizationTable(entries);
        var resolver = new RuntimeLocalizationResolver(new Localization(), table);

        var localized = resolver.ResolveSemantic(RuntimeSemanticKind.Event, "GAME_OVER", "ja-JP");

        Assert.That(localized.IsKnownSemantic, Is.True);
        Assert.That(localized.UsedFallback, Is.False);
        Assert.That(localized.LocalizationKey, Is.EqualTo("event.gameOver"));
        Assert.That(localized.Text, Is.EqualTo("対局結果"));
    }

    [Test]
    public void RuntimeAssemblyDependsOnEngineButNotPresentationAssemblies()
    {
        var runtimeAssembly = typeof(RuntimeLocalizationResolver).GetTypeInfo().Assembly;
        var references = runtimeAssembly.GetReferencedAssemblies();

        Assert.That(HasReference(references, "DominionWars.Engine"), Is.True);
        Assert.That(HasReference(references, "DominionWars.UI"), Is.False);
        Assert.That(HasReference(references, "DominionWars.Unity.EditMode"), Is.False);
        Assert.That(HasReference(references, "DominionWars.Editor"), Is.False);
    }

    private static bool HasReference(IEnumerable<AssemblyName> references, string name)
    {
        foreach (var reference in references)
        {
            if (string.Equals(reference.Name, name, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
}
