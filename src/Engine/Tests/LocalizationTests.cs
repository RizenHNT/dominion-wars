using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DominionWars.Engine.Localization;
using NUnit.Framework;
using LocalizationService = DominionWars.Engine.Localization.Localization;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class LocalizationTests
{
    [Test]
    public void EnglishCardNameLoadsFromEmbeddedJson()
    {
        Assert.That(new LocalizationService().Get("card.wood_moon", "en"), Is.EqualTo("Wood Moon"));
    }

    [Test]
    public void ChineseCardNameLoads()
    {
        Assert.That(new LocalizationService().Get("card.sea_pressure", "zh"), Is.EqualTo("深海压力"));
    }

    [Test]
    public void JapaneseCardNameLoads()
    {
        Assert.That(new LocalizationService().Get("card.flame_strike", "jp"), Is.EqualTo("炎撃"));
    }

    [Test]
    public void ChineseUiTextLoads()
    {
        Assert.That(new LocalizationService().Get("ui.your_turn", "zh"), Is.EqualTo("你的回合"));
    }

    [Test]
    public void JapaneseActionTextLoads()
    {
        Assert.That(new LocalizationService().Get("action.attack", "jp"), Is.EqualTo("攻撃"));
    }

    [Test]
    public void MissingLanguageFallsBackToEnglish()
    {
        Assert.That(new LocalizationService().Get("error.invalid_target", "fr"), Is.EqualTo("Invalid target"));
    }

    [Test]
    public void MissingKeyFallsBackToKeyName()
    {
        Assert.That(new LocalizationService().Get("missing.key", "zh"), Is.EqualTo("missing.key"));
    }

    [Test]
    public void GetOrThrowRejectsMissingKey()
    {
        Assert.Throws<KeyNotFoundException>(() => new LocalizationService().GetOrThrow("missing.key"));
    }

    [Test]
    public void BlankKeyIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new LocalizationService().Get(" "));
    }

    [Test]
    public void ResourceLookupIsThreadSafe()
    {
        var localization = new LocalizationService();
        Parallel.For(0, 100, index =>
        {
            var language = index % 3 == 0 ? "en" : index % 3 == 1 ? "zh" : "jp";
            Assert.That(localization.Get("action.play_card", language), Is.Not.Empty);
        });
    }
}
}
