using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DominionWars.Engine.Command;
using DominionWars.Engine.Events;
using DominionWars.Engine.Randomness;

namespace DominionWars.Engine.Model
{

public sealed class GameState
{
    private readonly Dictionary<string, CardDefinition> _cardLibrary;
    private readonly IReadOnlyDictionary<string, CardDefinition> _readOnlyCardLibrary;
    private readonly IReadOnlyList<PlayerState> _players;
    private int _currentPlayerIndex;
    private int _castleHealth;
    private int? _winnerPlayerIndex;
    private long _nextEntityId = 1;

    public GameState()
        : this(new PlayerState(0), new PlayerState(1))
    {
    }

    public GameState(
        PlayerState player0,
        PlayerState player1,
        IRandomSource? random = null,
        IEnumerable<CardDefinition>? cardLibrary = null,
        ICommandBuffer? commandBuffer = null,
        EventLog? eventLog = null)
        : this(
            new[] { player0, player1 },
            random,
            cardLibrary,
            commandBuffer,
            eventLog)
    {
    }

    public GameState(
        IEnumerable<PlayerState> players,
        IRandomSource? random = null,
        IEnumerable<CardDefinition>? cardLibrary = null,
        ICommandBuffer? commandBuffer = null,
        EventLog? eventLog = null)
    {
        if (players is null)
        {
            throw new ArgumentNullException(nameof(players));
        }

        var playerList = new List<PlayerState>(players);
        if (playerList.Count != 2)
        {
            throw new ArgumentException("A game must contain exactly two players.", nameof(players));
        }

        if (playerList[0] is null || playerList[1] is null)
        {
            throw new ArgumentException("Players cannot be null.", nameof(players));
        }

        if (playerList[0].PlayerIndex != 0 || playerList[1].PlayerIndex != 1)
        {
            throw new ArgumentException(
                "Players must be supplied in stable player-index order (0, 1).",
                nameof(players));
        }

        _players = playerList.AsReadOnly();
        Random = random ?? new Xoshiro256StarStar(1);
        Commands = commandBuffer ?? new LocalCommandBuffer();
        Events = eventLog ?? new EventLog();
        Turn = new TurnState();
        CastleEnabled = false;
        _castleHealth = 75;

        _cardLibrary = new Dictionary<string, CardDefinition>(StringComparer.Ordinal);
        if (cardLibrary is not null)
        {
            foreach (var definition in cardLibrary)
            {
                AddDefinition(_cardLibrary, definition);
            }
        }

        _readOnlyCardLibrary = new ReadOnlyDictionary<string, CardDefinition>(_cardLibrary);
        SynchronizeNextEntityId();
    }

    public IReadOnlyList<PlayerState> Players => _players;
    public PlayerState CurrentPlayer => Players[CurrentPlayerIndex];
    public PlayerState Opponent => Players[1 - CurrentPlayerIndex];
    public IRandomSource Random { get; }
    public ICommandBuffer Commands { get; }
    public EventLog Events { get; }
    public EventLog EventLog => Events;
    public TurnState Turn { get; }
    public IReadOnlyDictionary<string, CardDefinition> CardLibrary => _readOnlyCardLibrary;

    public int CurrentPlayerIndex
    {
        get => _currentPlayerIndex;
        set
        {
            ValidatePlayerIndex(value, nameof(value));
            _currentPlayerIndex = value;
        }
    }

    public bool CastleEnabled { get; set; }

    public int CastleHealth
    {
        get => _castleHealth;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _castleHealth = value;
        }
    }

    public int? WinnerPlayerIndex
    {
        get => _winnerPlayerIndex;
        set
        {
            if (value.HasValue)
            {
                ValidatePlayerIndex(value.Value, nameof(value));
            }

            _winnerPlayerIndex = value;
        }
    }

    public string? WinReason { get; set; }
    public bool EndTurnRequested { get; set; }

    public PlayerState GetPlayer(int playerIndex)
    {
        ValidatePlayerIndex(playerIndex, nameof(playerIndex));
        return Players[playerIndex];
    }

    public PlayerState GetOpponent(int playerIndex)
    {
        ValidatePlayerIndex(playerIndex, nameof(playerIndex));
        return Players[1 - playerIndex];
    }

    public CardInstance? FindEntity(long instanceId)
    {
        foreach (var player in Players)
        {
            var found = FindInZone(player.Deck, instanceId)
                ?? FindInZone(player.Hand, instanceId)
                ?? FindInZone(player.Field, instanceId)
                ?? FindInZone(player.Graveyard, instanceId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    public bool TryFindEntity(long instanceId, out CardInstance? entity)
    {
        entity = FindEntity(instanceId);
        return entity is not null;
    }

    public CardDefinition? FindCardDefinition(string cardId)
    {
        if (cardId is null)
        {
            throw new ArgumentNullException(nameof(cardId));
        }

        return _cardLibrary.TryGetValue(cardId, out var definition) ? definition : null;
    }

    public bool TryFindCardDefinition(string cardId, out CardDefinition? definition)
    {
        if (cardId is null)
        {
            throw new ArgumentNullException(nameof(cardId));
        }

        return _cardLibrary.TryGetValue(cardId, out definition);
    }

    public void RegisterCardDefinition(CardDefinition definition)
    {
        Commands.Execute(this, new DelegateGameCommand(_ => AddDefinition(_cardLibrary, definition)));
    }

    public long AllocateEntityId()
    {
        SynchronizeNextEntityId();
        var allocated = _nextEntityId;
        checked
        {
            _nextEntityId++;
        }

        return allocated;
    }

    internal void Mutate(IGameCommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        command.Apply(this);
    }

    private static CardInstance? FindInZone(IList<CardInstance> zone, long instanceId)
    {
        foreach (var card in zone)
        {
            if (card.InstanceId == instanceId)
            {
                return card;
            }
        }

        return null;
    }

    private static void AddDefinition(
        IDictionary<string, CardDefinition> destination,
        CardDefinition definition)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (destination.ContainsKey(definition.Id))
        {
            throw new InvalidOperationException($"Duplicate card id '{definition.Id}'.");
        }

        destination.Add(definition.Id, definition);
    }

    private void SynchronizeNextEntityId()
    {
        long maximum = 0;
        foreach (var player in Players)
        {
            maximum = FindMaximumId(player.Deck, maximum);
            maximum = FindMaximumId(player.Hand, maximum);
            maximum = FindMaximumId(player.Field, maximum);
            maximum = FindMaximumId(player.Graveyard, maximum);
        }

        if (_nextEntityId <= maximum)
        {
            _nextEntityId = checked(maximum + 1);
        }
    }

    private static long FindMaximumId(IList<CardInstance> zone, long currentMaximum)
    {
        foreach (var card in zone)
        {
            if (card.InstanceId > currentMaximum)
            {
                currentMaximum = card.InstanceId;
            }
        }

        return currentMaximum;
    }

    private static void ValidatePlayerIndex(int playerIndex, string parameterName)
    {
        if (playerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
}
