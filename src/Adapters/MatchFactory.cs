using System;
using DominionWars.Data;
using DominionWars.Engine.Setup;
using DominionWars.Engine.Turns;

namespace DominionWars.Adapters
{

/// <summary>
/// Composes the data boundary with the engine setup boundary. It owns no game
/// rules; it only translates loaded JSON definitions into a runtime gateway.
/// </summary>
public static class MatchFactory
{
    public static RuntimeMatchGateway CreateFromDecks(
        string matchId,
        CardCatalog catalog,
        DeckDefinition player0Deck,
        DeckDefinition player1Deck,
        int seed,
        int firstPlayerIndex,
        int openingHandSize,
        int playerLife,
        bool castleEnabled,
        int castleHealth,
        IRuntimeActionResultCache? resultCache = null)
    {
        return CreateFromDecks(
            matchId,
            catalog,
            player0Deck,
            player1Deck,
            new MatchSetupOptions
            {
                Seed = unchecked((ulong)(uint)seed),
                FirstPlayerIndex = firstPlayerIndex,
                OpeningHandSize = openingHandSize,
                PlayerLife = playerLife,
                CastleEnabled = castleEnabled,
                CastleHealth = castleHealth,
            },
            resultCache);
    }

    public static RuntimeMatchGateway CreateFromDecks(
        string matchId,
        CardCatalog catalog,
        DeckDefinition player0Deck,
        DeckDefinition player1Deck,
        MatchSetupOptions? options = null,
        IRuntimeActionResultCache? resultCache = null)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (player0Deck is null) throw new ArgumentNullException(nameof(player0Deck));
        if (player1Deck is null) throw new ArgumentNullException(nameof(player1Deck));

        var state = MatchSetup.Create(
            ToEngineDeck(player0Deck),
            ToEngineDeck(player1Deck),
            catalog.Cards,
            options);
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        return new RuntimeMatchGateway(matchId, state, flow, router, resultCache);
    }

    /// <summary>
    /// Creates a data-backed gateway and executes its one-time START
    /// lifecycle. The legacy CreateFromDecks overload remains setup-only.
    /// </summary>
    public static RuntimeMatchGateway CreateInitializedFromDecks(
        string matchId,
        CardCatalog catalog,
        DeckDefinition player0Deck,
        DeckDefinition player1Deck,
        MatchSetupOptions? options = null,
        IRuntimeActionResultCache? resultCache = null)
    {
        var gateway = CreateFromDecks(
            matchId,
            catalog,
            player0Deck,
            player1Deck,
            options,
            resultCache);
        var initialization = gateway.Initialize(0);
        if (!initialization.Accepted)
            throw new InvalidOperationException(initialization.ReasonKey);
        return gateway;
    }

    private static MatchDeckSpec ToEngineDeck(DeckDefinition deck)
    {
        return new MatchDeckSpec(deck.Leader, deck.Cards, deck.Name, deck.Faction);
    }
}
}
