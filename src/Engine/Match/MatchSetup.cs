using System;
using System.Collections.Generic;
using System.IO;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Randomness;
using DominionWars.Engine.Rules;

namespace DominionWars.Engine.Setup
{

/// <summary>Data-independent description of one player's prebuilt deck.</summary>
public sealed class MatchDeckSpec
{
    public MatchDeckSpec(
        string leaderId,
        IReadOnlyDictionary<string, int> cards,
        string? name = null,
        string? faction = null)
    {
        if (string.IsNullOrWhiteSpace(leaderId))
        {
            throw new ArgumentException("A leader id is required.", nameof(leaderId));
        }

        LeaderId = leaderId;
        Cards = cards ?? throw new ArgumentNullException(nameof(cards));
        Name = name ?? string.Empty;
        Faction = faction ?? string.Empty;
    }

    public string LeaderId { get; }
    public IReadOnlyDictionary<string, int> Cards { get; }
    public string Name { get; }
    public string Faction { get; }
}

/// <summary>
/// Explicit match-start parameters. Values are setup policy, not a second
/// rules engine; later modes can supply a different instance without changing
/// MatchSetup or the card runtime.
/// </summary>
public sealed class MatchSetupOptions
{
    public int FirstPlayerIndex { get; set; }
    public ulong Seed { get; set; } = 1;
    public int OpeningHandSize { get; set; } = 5;
    public int PlayerLife { get; set; } = 20;
    public bool CastleEnabled { get; set; } = true;
    public int CastleHealth { get; set; } = 75;
    public MatchRules Rules { get; set; } = new MatchRules();
}

/// <summary>
/// Builds a deterministic, data-backed GameState without making callers
/// manually populate zones. Card definitions remain owned by the Engine;
/// adapters can translate external deck formats into MatchDeckSpec.
/// </summary>
public static class MatchSetup
{
    public static GameState Create(
        MatchDeckSpec player0Deck,
        MatchDeckSpec player1Deck,
        IReadOnlyDictionary<string, CardDefinition> cardLibrary,
        MatchSetupOptions? options = null)
    {
        if (player0Deck is null) throw new ArgumentNullException(nameof(player0Deck));
        if (player1Deck is null) throw new ArgumentNullException(nameof(player1Deck));
        if (cardLibrary is null) throw new ArgumentNullException(nameof(cardLibrary));

        var setup = options ?? new MatchSetupOptions();
        ValidateOptions(setup);
        ValidateDeck(player0Deck, cardLibrary, "player0");
        ValidateDeck(player1Deck, cardLibrary, "player1");

        var state = new GameState(
            new PlayerState(0, setup.PlayerLife),
            new PlayerState(1, setup.PlayerLife),
            random: new Xoshiro256StarStar(setup.Seed),
            cardLibrary: cardLibrary.Values,
            rules: setup.Rules)
        {
            CurrentPlayerIndex = setup.FirstPlayerIndex,
            CastleEnabled = setup.CastleEnabled,
            CastleHealth = setup.CastleHealth,
        };

        var matchStarted = state.Events.Append("MATCH_STARTED", null, Data(
            "firstPlayer", setup.FirstPlayerIndex,
            "seed", setup.Seed,
            "castleEnabled", setup.CastleEnabled,
            "castleHealth", setup.CastleHealth));

        PopulateDeck(state, state.GetPlayer(0), player0Deck, cardLibrary);
        PopulateDeck(state, state.GetPlayer(1), player1Deck, cardLibrary);
        Shuffle(state.Players[0].Deck, state.Random);
        Shuffle(state.Players[1].Deck, state.Random);

        var runtime = new EffectRuntime(state);
        runtime.DrawOpeningHand(0, setup.OpeningHandSize, matchStarted.EventId);
        runtime.DrawOpeningHand(1, setup.OpeningHandSize, matchStarted.EventId);
        return state;
    }

    private static void ValidateOptions(MatchSetupOptions options)
    {
        if (options.FirstPlayerIndex is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options.FirstPlayerIndex));
        if (options.OpeningHandSize < 0)
            throw new ArgumentOutOfRangeException(nameof(options.OpeningHandSize));
        if (options.PlayerLife < 0)
            throw new ArgumentOutOfRangeException(nameof(options.PlayerLife));
        if (options.CastleHealth < 1)
            throw new ArgumentOutOfRangeException(nameof(options.CastleHealth));
        if (options.Rules is null)
            throw new ArgumentException("Match rules are required.", nameof(options));
    }

    private static void ValidateDeck(
        MatchDeckSpec deck,
        IReadOnlyDictionary<string, CardDefinition> library,
        string owner)
    {
        if (!library.TryGetValue(deck.LeaderId, out var leader) || !leader.IsLeader)
            throw InvalidDeck(owner, "leader is missing or is not a leader card");

        foreach (var entry in deck.Cards)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value < 1)
                throw InvalidDeck(owner, "cards must map ids to positive counts");
            if (!library.TryGetValue(entry.Key, out var definition))
                throw InvalidDeck(owner, "unknown card '" + entry.Key + "'");
            if (definition.IsLeader)
                throw InvalidDeck(owner, "leader cards must be supplied through leaderId");
        }
    }

    private static void PopulateDeck(
        GameState state,
        PlayerState player,
        MatchDeckSpec deck,
        IReadOnlyDictionary<string, CardDefinition> library)
    {
        foreach (var entry in deck.Cards)
        {
            for (var count = 0; count < entry.Value; count++)
            {
                player.Deck.Add(new CardInstance(
                    state.AllocateEntityId(), player.PlayerIndex, library[entry.Key]));
            }
        }

        player.Deck.Add(new CardInstance(
            state.AllocateEntityId(), player.PlayerIndex, library[deck.LeaderId]));
    }

    private static void Shuffle(IList<CardInstance> cards, IRandomSource random)
    {
        for (var index = cards.Count - 1; index > 0; index--)
        {
            var swap = random.NextInt(index + 1);
            var item = cards[index];
            cards[index] = cards[swap];
            cards[swap] = item;
        }
    }

    private static InvalidDataException InvalidDeck(string owner, string message)
    {
        return new InvalidDataException(owner + " deck: " + message);
    }

    private static IReadOnlyDictionary<string, object?> Data(params object?[] values)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < values.Length; index += 2)
            result[(string)values[index]!] = values[index + 1];
        return result;
    }
}
}
