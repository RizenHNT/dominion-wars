using System.Linq;
using DominionWars.Adapters;
using DominionWars.Engine.Model;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

[TestFixture]
public sealed class ContractDtosTests
{
    private static CardDto CreateCard()
    {
        var definition = new CardDefinition(
            "metadata_card",
            "Metadata Card",
            3,
            4,
            isMinion: true,
            faction: "NORTH",
            text: "Draw one card.",
            flavor: "A reliable scout.",
            cost: 2,
            rarity: "RARE",
            artId: "art_metadata_card",
            tags: new[] { "SCOUT", "RANGED" });
        return EngineProjectionAdapter.ToCardDto(new CardInstance(11, 0, definition));
    }

    [Test]
    public void FactionMapsFromDefinition()
    {
        Assert.That(CreateCard().Faction, Is.EqualTo("NORTH"));
    }

    [Test]
    public void TextMapsFromDefinition()
    {
        Assert.That(CreateCard().Text, Is.EqualTo("Draw one card."));
    }

    [Test]
    public void FlavorMapsFromDefinition()
    {
        Assert.That(CreateCard().Flavor, Is.EqualTo("A reliable scout."));
    }

    [Test]
    public void CostMapsFromDefinition()
    {
        Assert.That(CreateCard().Cost, Is.EqualTo(2));
    }

    [Test]
    public void RarityMapsFromDefinition()
    {
        Assert.That(CreateCard().Rarity, Is.EqualTo("RARE"));
    }

    [Test]
    public void ArtIdMapsFromDefinition()
    {
        Assert.That(CreateCard().ArtId, Is.EqualTo("art_metadata_card"));
    }

    [Test]
    public void TagsMapDataDrivenAndIgnoreBlankEntries()
    {
        var card = CreateCard();
        Assert.That(card.Tags, Is.EquivalentTo(new[] { "SCOUT", "RANGED" }));
        Assert.That(card.Tags.Count(), Is.EqualTo(2));
    }
}
}
