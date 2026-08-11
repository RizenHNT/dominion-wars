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
    public IReadOnlyDictionary<string, object?> Payload { get; set; }
        = new Dictionary<string, object?>();
}

public sealed class GameEventDto
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
}
