using System;
using System.Collections.Generic;

namespace DominionWars.Engine.Model
{

public sealed class CardInstance
{
    public CardInstance(long instanceId, int ownerPlayerIndex, CardDefinition definition)
    {
        if (instanceId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(instanceId));
        }

        if (ownerPlayerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ownerPlayerIndex));
        }

        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        InstanceId = instanceId;
        OwnerPlayerIndex = ownerPlayerIndex;
        Attack = definition.Attack;
        Health = definition.Health;
        MaxHealth = definition.Health;
        Durability = definition.LeaderDurability;
        Keywords = new HashSet<string>(definition.Keywords, StringComparer.OrdinalIgnoreCase);
        Shield = Keywords.Contains("圣盾");
    }

    public long InstanceId { get; }
    public int OwnerPlayerIndex { get; }
    public CardDefinition Definition { get; }
    public int Attack { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public bool Shield { get; set; }
    public int AttacksUsed { get; set; }
    public bool SummonedThisTurn { get; set; }
    public bool PunishActivated { get; set; }
    public int ChantRemaining { get; set; }
    public int Durability { get; set; }
    public ISet<string> Keywords { get; }

    public bool IsMinion => Definition.IsMinion;
    public bool IsLeader => Definition.IsLeader;
    public bool IsLeaderEntity { get; set; }
    public bool IsAlive => Health > 0;

    public bool HasKeyword(string keyword)
    {
        return !string.IsNullOrWhiteSpace(keyword) && Keywords.Contains(keyword);
    }

    public void ResetRuntimeState()
    {
        Attack = Definition.Attack;
        Health = Definition.Health;
        MaxHealth = Definition.Health;
        Shield = false;
        AttacksUsed = 0;
        SummonedThisTurn = false;
        PunishActivated = false;
        ChantRemaining = 0;
        Durability = Definition.LeaderDurability;
        IsLeaderEntity = false;
        Keywords.Clear();
        foreach (var keyword in Definition.Keywords)
        {
            Keywords.Add(keyword);
        }

        Shield = Keywords.Contains("圣盾");
    }
}
}
