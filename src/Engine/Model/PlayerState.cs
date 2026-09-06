using System;
using System.Collections.Generic;

namespace DominionWars.Engine.Model
{

public sealed class PlayerState
{
    public PlayerState(int playerIndex, int? life = null)
    {
        if (playerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(playerIndex));
        }

        if (life < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(life));
        }

        PlayerIndex = playerIndex;
        Life = life;
        Deck = new List<CardInstance>();
        Hand = new List<CardInstance>();
        Field = new List<CardInstance>();
        LeaderZone = new List<CardInstance>();
        AmbushZone = new List<CardInstance>();
        Graveyard = new List<CardInstance>();
        CommitQueue = new List<CardInstance>();
        CloudStack = new List<CardInstance>();
        UsedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public int PlayerIndex { get; }
    public int? Life { get; set; }
    public IList<CardInstance> Deck { get; }
    public IList<CardInstance> Hand { get; }
    public IList<CardInstance> Field { get; }
    /// <summary>公开的非随从统领区域；统领不占用普通战场格。</summary>
    public IList<CardInstance> LeaderZone { get; }
    /// <summary>公开的伏击统领区域；伏击统领不占用普通战场格。</summary>
    public IList<CardInstance> AmbushZone { get; }
    public IList<CardInstance> Graveyard { get; }
    /// <summary>公开机械提交队列，顺序为最早提交在前。</summary>
    public IList<CardInstance> CommitQueue { get; }
    /// <summary>公开机械云端栈，列表末尾为栈顶。</summary>
    public IList<CardInstance> CloudStack { get; }
    public ISet<string> UsedTags { get; }

    public int PunishDeltaThisTurn { get; set; }
    public bool PunishToSelfDiscardThisTurn { get; set; }
    public bool ProtectedThisTurn { get; set; }
    public bool EffectsNegatedThisTurn { get; set; }
    public int SkipReshuffleCredits { get; set; }
    public int ReshuffleCount { get; set; }
    public int CycleWinCount { get; set; }
    public int TotalDiscarded { get; set; }
    public int PunishDrawnThisTurn { get; set; }
    /// <summary>At most one non-leader ambush may be set by this player each turn.</summary>
    public bool AmbushSetThisTurn { get; set; }
    /// <summary>A triggered FOCUS ambush suppresses this player's other ambushes until handoff.</summary>
    public bool AmbushFocusTriggeredThisTurn { get; set; }
    public bool DamagedThisCycle { get; set; }
    public int NoDamageTurns { get; set; }
    public int PullCount { get; set; }
    public int RootStacks { get; set; }
    public int RampantStacks
    {
        get => _rampantStacks;
        set
        {
            if (value is < 0 or > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _rampantStacks = value;
        }
    }

    private int _rampantStacks;

    /// <summary>
    /// Counts active leader entities across all three leader locations. A
    /// malformed state containing the same instance twice is intentionally
    /// counted twice so callers fail closed instead of silently selecting one.
    /// </summary>
    public int ActiveLeaderCount
    {
        get => CountActiveLeaders(Field)
            + CountActiveLeaders(LeaderZone)
            + CountActiveLeaders(AmbushZone);
    }

    public bool HasMultipleActiveLeaders => ActiveLeaderCount > 1;

    public CardInstance? Leader
    {
        get
        {
            CardInstance? candidate = null;
            foreach (var zone in new[] { Field, LeaderZone, AmbushZone })
            {
                foreach (var card in zone)
                {
                    if (!IsActiveLeader(card))
                    {
                        continue;
                    }

                    if (candidate is not null)
                    {
                        return null;
                    }

                    candidate = card;
                }
            }

            return candidate;
        }
    }

    private static int CountActiveLeaders(IList<CardInstance> zone)
    {
        var count = 0;
        foreach (var card in zone)
        {
            if (IsActiveLeader(card))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsActiveLeader(CardInstance card)
    {
        return card.IsLeaderEntity && card.Definition.IsLeader;
    }
}
}
