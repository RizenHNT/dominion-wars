using System;
using System.Collections.Generic;
using DominionWars.Engine.Effects;

namespace DominionWars.Engine.Model
{

public sealed class CardDefinition
{
    private readonly IReadOnlyCollection<string> _keywords;
    private readonly IReadOnlyCollection<string> _tags;
    private readonly IReadOnlyList<EffectSpec> _onPlayEffects;
    private readonly IReadOnlyList<EffectSpec> _punishEffects;
    private readonly IReadOnlyList<EffectSpec> _ambushEffects;
    private readonly IReadOnlyList<EffectSpec> _chantEffects;
    private readonly IReadOnlyList<EffectSpec> _onOpponentDiscardEffects;
    private readonly IReadOnlyList<EffectSpec> _leaderEnterEffects;
    private readonly IReadOnlyList<EffectSpec> _leaderPunishEffects;
    private readonly IReadOnlyList<EffectSpec> _commitEffects;
    private readonly IReadOnlyList<EffectSpec> _pushEffects;
    private readonly IReadOnlyList<EffectSpec> _pullEffects;
    private readonly IReadOnlyList<LandmarkTierDefinition> _landmarkTiers;

    public CardDefinition(
        string id,
        string name,
        int attack = 0,
        int health = 0,
        bool isMinion = false,
        bool isLeader = false,
        int grantLife = 0,
        bool kingSlayer = false,
        IEnumerable<string>? keywords = null,
        IEnumerable<string>? vulnerabilities = null,
        string faction = "",
        string text = "",
        string? flavor = null,
        int cost = 0,
        string rarity = "",
        string? artId = null,
        IEnumerable<string>? tags = null,
        bool punishActivatable = false,
        int punishCost = 0,
        bool hasLeaderAbility = false,
        string? type = null,
        int punish = 0,
        string? punishCondition = null,
        IEnumerable<EffectSpec>? onPlayEffects = null,
        IEnumerable<EffectSpec>? punishEffects = null,
        string? ambushKind = null,
        string? ambushTrigger = null,
        IEnumerable<EffectSpec>? ambushEffects = null,
        int chant = 0,
        IEnumerable<EffectSpec>? chantEffects = null,
        int attacksPerTurn = 1,
        IEnumerable<EffectSpec>? onOpponentDiscardEffects = null,
        bool guard = false,
        IEnumerable<EffectSpec>? leaderEnterEffects = null,
        IEnumerable<EffectSpec>? leaderPunishEffects = null,
        string? leaderWinCondition = null,
        string? leaderWinText = null,
        int leaderDurability = 0,
        int leaderWinParam = 0,
        int commitCost = 0,
        int uploadCost = 0,
        int downloadCost = 0,
        IEnumerable<EffectSpec>? commitEffects = null,
        IEnumerable<EffectSpec>? pushEffects = null,
        IEnumerable<EffectSpec>? pullEffects = null,
        bool isLandmark = false,
        IEnumerable<LandmarkTierDefinition>? landmarkTiers = null)
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

        if (cost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cost));
        }

        if (punishCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(punishCost));
        }

        if (punish < 0 || chant < 0 || attacksPerTurn < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(punish));
        }

        if (leaderDurability < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(leaderDurability));
        }

        if (leaderWinParam < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(leaderWinParam));
        }

        if (commitCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(commitCost));
        }

        if (uploadCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(uploadCost));
        }

        if (downloadCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(downloadCost));
        }

        Id = id;
        Name = name;
        Attack = attack;
        Health = health;
        IsMinion = isMinion;
        IsLeader = isLeader;
        GrantLife = grantLife;
        Faction = faction ?? string.Empty;
        Text = text ?? string.Empty;
        Flavor = flavor;
        Cost = cost;
        Rarity = rarity ?? string.Empty;
        ArtId = artId;
        PunishActivatable = punishActivatable;
        PunishCost = punishCost;
        HasLeaderAbility = hasLeaderAbility;
        Type = string.IsNullOrWhiteSpace(type) ? (isMinion ? "MINION" : "SPELL") : type!;
        Punish = punish;
        PunishCondition = punishCondition;
        AmbushKind = ambushKind;
        AmbushTrigger = ambushTrigger;
        Chant = chant;
        AttacksPerTurn = attacksPerTurn;
        Guard = guard;
        LeaderWinCondition = leaderWinCondition;
        LeaderWinText = leaderWinText;
        LeaderDurability = leaderDurability;
        LeaderWinParam = leaderWinParam;
        CommitCost = commitCost;
        UploadCost = uploadCost;
        DownloadCost = downloadCost;
        IsLandmark = isLandmark;
        // Legacy compatibility only. New data should mark the individual EffectSpec.
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

        var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (tags is not null)
        {
            foreach (var tag in tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    tagSet.Add(tag);
                }
            }
        }

        _tags = tagSet;

        var vulnerabilitySet = new HashSet<string>(StringComparer.Ordinal);
        if (vulnerabilities is not null)
        {
            foreach (var vulnerability in vulnerabilities)
            {
                if (!string.IsNullOrWhiteSpace(vulnerability))
                {
                    vulnerabilitySet.Add(vulnerability);
                }
            }
        }

        Vulnerabilities = vulnerabilitySet;
        _onPlayEffects = CopyEffects(onPlayEffects);
        _punishEffects = CopyEffects(punishEffects);
        _ambushEffects = CopyEffects(ambushEffects);
        _chantEffects = CopyEffects(chantEffects);
        _onOpponentDiscardEffects = CopyEffects(onOpponentDiscardEffects);
        _leaderEnterEffects = CopyEffects(leaderEnterEffects);
        _leaderPunishEffects = CopyEffects(leaderPunishEffects);
        _commitEffects = CopyEffects(commitEffects);
        _pushEffects = CopyEffects(pushEffects);
        _pullEffects = CopyEffects(pullEffects);
        _landmarkTiers = CopyLandmarkTiers(landmarkTiers);
    }

    public string Id { get; }
    public string Name { get; }
    public int Attack { get; }
    public int Health { get; }
    public bool IsMinion { get; }
    public bool IsLeader { get; }
    public int GrantLife { get; }
    public string Faction { get; }
    public string Text { get; }
    public string? Flavor { get; }
    public int Cost { get; }
    public string Rarity { get; }
    public string? ArtId { get; }
    public bool PunishActivatable { get; }
    public int PunishCost { get; }
    public bool HasLeaderAbility { get; }
    public string Type { get; }
    public int Punish { get; }
    public string? PunishCondition { get; }
    public string? AmbushKind { get; }
    public string? AmbushTrigger { get; }
    public int Chant { get; }
    public int AttacksPerTurn { get; }
    public bool Guard { get; }
    public string? LeaderWinCondition { get; }
    public string? LeaderWinText { get; }
    public int LeaderDurability { get; }
    public int LeaderWinParam { get; }
    /// <summary>Declared fee for moving a card into the public commit queue.</summary>
    public int CommitCost { get; }
    /// <summary>Declared fee for pushing a queued card into the cloud stack.</summary>
    public int UploadCost { get; }
    /// <summary>Declared fee for pulling this card from the cloud stack.</summary>
    public int DownloadCost { get; }
    /// <summary>Declarative landmark shape marker; it does not activate landmark rules.</summary>
    public bool IsLandmark { get; }
    /// <summary>Legacy card-level fallback. EffectSpec.KingSlayer takes precedence when present.</summary>
    public bool KingSlayer { get; }
    public IReadOnlyCollection<string> Keywords => _keywords;
    public IReadOnlyCollection<string> Tags => _tags;
    public IReadOnlyCollection<string> Vulnerabilities { get; }
    public IReadOnlyList<EffectSpec> OnPlayEffects => _onPlayEffects;
    public IReadOnlyList<EffectSpec> PunishEffects => _punishEffects;
    public IReadOnlyList<EffectSpec> AmbushEffects => _ambushEffects;
    public IReadOnlyList<EffectSpec> ChantEffects => _chantEffects;
    public IReadOnlyList<EffectSpec> OnOpponentDiscardEffects => _onOpponentDiscardEffects;
    public IReadOnlyList<EffectSpec> LeaderEnterEffects => _leaderEnterEffects;
    public IReadOnlyList<EffectSpec> LeaderPunishEffects => _leaderPunishEffects;
    public IReadOnlyList<EffectSpec> CommitEffects => _commitEffects;
    public IReadOnlyList<EffectSpec> PushEffects => _pushEffects;
    public IReadOnlyList<EffectSpec> PullEffects => _pullEffects;
    public IReadOnlyList<LandmarkTierDefinition> LandmarkTiers => _landmarkTiers;

    private static IReadOnlyList<EffectSpec> CopyEffects(IEnumerable<EffectSpec>? effects)
    {
        var copy = new List<EffectSpec>();
        if (effects is not null)
        {
            foreach (var effect in effects)
            {
                copy.Add(effect ?? throw new ArgumentException("Effect lists cannot contain null.", nameof(effects)));
            }
        }

        return copy.AsReadOnly();
    }

    private static IReadOnlyList<LandmarkTierDefinition> CopyLandmarkTiers(
        IEnumerable<LandmarkTierDefinition>? tiers)
    {
        var copy = new List<LandmarkTierDefinition>();
        if (tiers is not null)
        {
            foreach (var tier in tiers)
            {
                copy.Add(tier ?? throw new ArgumentException(
                    "Landmark tier lists cannot contain null.",
                    nameof(tiers)));
            }
        }

        return copy.AsReadOnly();
    }
}
}
