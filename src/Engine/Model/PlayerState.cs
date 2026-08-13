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
        Graveyard = new List<CardInstance>();
        UsedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public int PlayerIndex { get; }
    public int? Life { get; set; }
    public IList<CardInstance> Deck { get; }
    public IList<CardInstance> Hand { get; }
    public IList<CardInstance> Field { get; }
    public IList<CardInstance> Graveyard { get; }
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
    public bool DamagedThisCycle { get; set; }

    public CardInstance? Leader
    {
        get
        {
            foreach (var card in Field)
            {
                if (card.IsLeaderEntity)
                {
                    return card;
                }
            }

            return null;
        }
    }
}
}
