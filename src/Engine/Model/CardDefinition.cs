using System;
using System.Collections.Generic;

namespace DominionWars.Engine.Model
{

public sealed class CardDefinition
{
    private readonly IReadOnlyCollection<string> _keywords;

    public CardDefinition(
        string id,
        string name,
        int attack = 0,
        int health = 0,
        bool isMinion = false,
        bool isLeader = false,
        int grantLife = 0,
        bool kingSlayer = false,
        IEnumerable<string>? keywords = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A card id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A card name is required.", nameof(name));
        }

        if (attack < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attack));
        }

        if (health < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(health));
        }

        if (grantLife < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(grantLife));
        }

        Id = id;
        Name = name;
        Attack = attack;
        Health = health;
        IsMinion = isMinion;
        IsLeader = isLeader;
        GrantLife = grantLife;
        KingSlayer = kingSlayer;

        var keywordSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (keywords is not null)
        {
            foreach (var keyword in keywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    keywordSet.Add(keyword);
                }
            }
        }

        _keywords = keywordSet;
    }

    public string Id { get; }
    public string Name { get; }
    public int Attack { get; }
    public int Health { get; }
    public bool IsMinion { get; }
    public bool IsLeader { get; }
    public int GrantLife { get; }
    public bool KingSlayer { get; }
    public IReadOnlyCollection<string> Keywords => _keywords;
}
}
