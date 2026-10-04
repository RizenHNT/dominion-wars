using System;
using System.Collections.Generic;

namespace DominionWars.Adapters
{

public sealed class SnapshotDto
{
    public int ContractVersion { get; set; } = 1;
    public string MatchId { get; set; } = string.Empty;
    public int Turn { get; set; }
    public string Phase { get; set; } = string.Empty;
    public int CurrentPlayer { get; set; }
    public IReadOnlyList<PlayerDto> Players { get; set; } = Array.Empty<PlayerDto>();
    public CastleDto Castle { get; set; } = new CastleDto();
    public IReadOnlyList<LegalActionDto> LegalActions { get; set; } = Array.Empty<LegalActionDto>();
    public IReadOnlyDictionary<string, object?>? PendingPrompt { get; set; }
    public IReadOnlyDictionary<string, object?> PresentationHints { get; set; }
        = new Dictionary<string, object?>();
}

public sealed class PlayerDto
{
    public string Id { get; set; } = string.Empty;
    public int? Life { get; set; }
    public int DeckCount { get; set; }
    public int HandCount { get; set; }
    public IReadOnlyList<string> FieldEntityIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<CardDto> Deck { get; set; } = Array.Empty<CardDto>();
    public IReadOnlyList<CardDto> Hand { get; set; } = Array.Empty<CardDto>();
    public IReadOnlyList<CardDto> Field { get; set; } = Array.Empty<CardDto>();
    public IReadOnlyList<CardDto> Graveyard { get; set; } = Array.Empty<CardDto>();
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
    public int NoDamageTurns { get; set; }
    public int PullCount { get; set; }
    public int RootStacks { get; set; }
    public int RampantStacks { get; set; }
    public int CommitQueueCount { get; set; }
    public int CloudStackCount { get; set; }
}

public sealed class CardDto
{
    public string EntityId { get; set; } = string.Empty;
    public string CardId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsMinion { get; set; }
    public bool IsLeader { get; set; }
    public bool IsLeaderEntity { get; set; }
    public int OwnerPlayer { get; set; }
    public string Faction { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Flavor { get; set; }
    public int Cost { get; set; }
    public string Rarity { get; set; } = string.Empty;
    public string? ArtId { get; set; }
    public int DefinitionAttack { get; set; }
    public int DefinitionHealth { get; set; }
    public int GrantLife { get; set; }
    public int DefinitionDurability { get; set; }
    public int Durability { get; set; }
    public string? LeaderWinCondition { get; set; }
    public int LeaderWinParam { get; set; }

    /// <summary>
    /// The victory objective the CARD DATA declares, published so a consumer never has
    /// to reconstruct it from <see cref="LeaderWinCondition"/>.
    ///
    /// Null when the card has not migrated. The AI's WinConditionCounters exists only
    /// because that reconstruction used to be the only option: it parses the id into a
    /// metric family and a comparison suffix, which is why it had to guess twice in one
    /// session (the plural "TURNS_GE", and "ROYAL_CASTLE_BREAK" with no suffix at all).
    /// With this field the consumer reads the metric instead of guessing it.
    /// </summary>
    public VictoryObjectiveDto? Victory { get; set; }

    public bool KingSlayer { get; set; }
    public IReadOnlyList<string> Vulnerabilities { get; set; } = Array.Empty<string>();
    public int Attack { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public bool Shield { get; set; }
    public int AttacksUsed { get; set; }
    public bool SummonedThisTurn { get; set; }
    public bool Sealed { get; set; }
    public IReadOnlyList<string> Keywords { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
}

/// <summary>
/// One victory objective as the CARD DATA declares it: a closed-set metric, a
/// direction, and a threshold. Deliberately free of condition names, so a consumer
/// can ask "how much further" without knowing whether the number came from discards,
/// pulls or a sealed minion's health.
///
/// This is the machine-readable form of the rule. The engine's own win evaluation
/// reads the same fields, so there is one source of truth rather than a name that
/// every consumer re-parses.
/// </summary>
public sealed class VictoryObjectiveDto
{
    /// <summary>One of VictoryMetric's closed set.</summary>
    public string Metric { get; set; } = string.Empty;

    /// <summary>INCREASE or DECREASE.</summary>
    public string Direction { get; set; } = string.Empty;

    /// <summary>The threshold the metric is compared against.</summary>
    public int Target { get; set; }
}

public sealed class CastleDto
{
    public bool Enabled { get; set; }
    public int Health { get; set; }
}

public sealed class LegalActionDto
{
    public int ContractVersion { get; set; } = 1;
    public string ActionId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Actor { get; set; }
    public string? SourceId { get; set; }
    public string? TargetId { get; set; }
    public string? CardId { get; set; }
    public string? ReasonKey { get; set; }
    public IReadOnlyDictionary<string, object?> Payload { get; set; }
        = new Dictionary<string, object?>();
}

public sealed class LegalAction : DominionWars.Engine.LegalAction
{
}

public class UiEventDto
{
    public int ContractVersion { get; set; } = 1;
    public string EventId { get; set; } = string.Empty;
    public string? ParentEventId { get; set; }
    public string Type { get; set; } = string.Empty;
    public int Turn { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string? SourceId { get; set; }
    public IReadOnlyList<string> TargetIds { get; set; } = Array.Empty<string>();
    public double? Amount { get; set; }
    public string? ReasonKey { get; set; }
    public IReadOnlyDictionary<string, object?> Data { get; set; }
        = new Dictionary<string, object?>();
}

public sealed class GameEventDto : UiEventDto
{
}
}
