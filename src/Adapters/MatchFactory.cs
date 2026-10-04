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
        IRuntimeActionResultCache? resultCache = null,
        BalanceLoad? balance = null)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (player0Deck is null) throw new ArgumentNullException(nameof(player0Deck));
        if (player1Deck is null) throw new ArgumentNullException(nameof(player1Deck));

        var state = MatchSetup.Create(
            ToEngineDeck(player0Deck),
            ToEngineDeck(player1Deck),
            catalog.Cards,
            WithBalanceRules(options, balance));
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        return new RuntimeMatchGateway(matchId, state, flow, router, resultCache);
    }

    /// <summary>
    /// Overload that applies the shipped <c>data/balance.json</c> rules instead of
    /// the built-in <see cref="DominionWars.Engine.Rules.MatchRules"/> defaults.
    /// <para>
    /// The rules value object stays the authority; this method only supplies the
    /// rules the data boundary read, so the engine itself never performs file IO.
    /// The shipped file is pinned to the built-in defaults by
    /// <c>BalanceTableTests</c>, so this call is behaviour-preserving until the
    /// owner actually edits a rule value.
    /// </para>
    /// </summary>
    public static RuntimeMatchGateway CreateWithShippedRulesFromDecks(
        string matchId,
        CardCatalog catalog,
        DeckDefinition player0Deck,
        DeckDefinition player1Deck,
        MatchSetupOptions? options = null,
        IRuntimeActionResultCache? resultCache = null,
        Action<string>? diagnostic = null)
    {
        return CreateFromDecks(
            matchId,
            catalog,
            player0Deck,
            player1Deck,
            options,
            resultCache,
            BalanceTable.Shipped(diagnostic));
    }

    /// <summary>
    /// Supplies the balance-derived rules to <see cref="MatchSetup"/>. A read
    /// failure yields the built-in defaults inside the <see cref="BalanceLoad"/>,
    /// so this cannot turn a bad file into a broken match.
    /// </summary>
    private static MatchSetupOptions? WithBalanceRules(MatchSetupOptions? options, BalanceLoad? balance)
    {
        var rules = balance?.Rules;
        if (rules is null)
        {
            return options;
        }

        var merged = options ?? new MatchSetupOptions();
        merged.Rules = rules;
        return merged;
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
