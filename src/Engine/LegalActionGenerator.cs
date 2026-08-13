using System;
using System.Collections.Generic;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;

namespace DominionWars.Engine
{

/// <summary>
/// Produces player-facing choices. EffectAction is reserved for resolution and
/// must not be used as a legal player action here.
/// </summary>
public sealed class LegalActionGenerator
{
    private readonly AttackTargetPolicy _attackTargets;

    public LegalActionGenerator(AttackTargetPolicy? attackTargets = null)
    {
        _attackTargets = attackTargets ?? new AttackTargetPolicy();
    }

    public const string PlayCard = "PLAY_CARD";
    public const string Attack = "ATTACK";
    public const string EndTurn = "END_TURN";
    public const string ActivatePunish = "ACTIVATE_PUNISH";
    public const string UseLeaderAbility = "USE_LEADER_ABILITY";

    public IReadOnlyList<LegalAction> Generate(GameState state, int playerIdx)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (playerIdx is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(playerIdx));
        }

        var actions = new List<LegalAction>();
        if (state.WinnerPlayerIndex.HasValue || state.CurrentPlayerIndex != playerIdx)
        {
            return actions;
        }

        var player = state.GetPlayer(playerIdx);
        var opponent = state.GetOpponent(playerIdx);

        foreach (var card in player.Hand)
        {
            if (!CardPlayRules.CanPlay(player, card, out _))
            {
                continue;
            }

            var playPayload = new Dictionary<string, object?>
            {
                ["punish"] = CardPlayRules.EffectivePunish(player, card),
            };
            if (player.PunishToSelfDiscardThisTurn && CardPlayRules.EffectivePunish(player, card) > 0)
            {
                var candidates = new List<long>();
                foreach (var discard in player.Hand)
                {
                    if (discard != card)
                    {
                        candidates.Add(discard.InstanceId);
                    }
                }

                playPayload["discardRequired"] = Math.Min(
                    CardPlayRules.EffectivePunish(player, card),
                    candidates.Count);
                playPayload["discardCandidateIds"] = candidates;
            }

            actions.Add(new LegalAction
            {
                ActionId = $"play_{card.InstanceId}",
                Type = PlayCard,
                Actor = playerIdx,
                SourceId = card.InstanceId,
                CardId = card.Definition.Id,
                ReasonKey = "action.play_card",
                Payload = playPayload,
            });

        }

        foreach (var source in player.Field)
        {
            foreach (var target in _attackTargets.GetLegalTargets(state, source))
            {
                actions.Add(new LegalAction
                {
                    ActionId = $"attack_{source.InstanceId}_{target.Id}",
                    Type = Attack,
                    Actor = playerIdx,
                    SourceId = source.InstanceId,
                    TargetReferenceId = target.Id,
                    TargetId = target.EntityId,
                    ReasonKey = "action.attack",
                });
            }

            if (source.IsLeaderEntity && source.Definition.HasLeaderAbility && source.IsAlive)
            {
                actions.Add(new LegalAction
                {
                    ActionId = $"leader_ability_{source.InstanceId}",
                    Type = UseLeaderAbility,
                    Actor = playerIdx,
                    SourceId = source.InstanceId,
                    CardId = source.Definition.Id,
                    ReasonKey = "action.use_leader_ability",
                });
            }
        }

        actions.Add(new LegalAction
        {
            ActionId = $"end_turn_{playerIdx}",
            Type = EndTurn,
            Actor = playerIdx,
            ReasonKey = "action.end_turn",
        });

        return actions;
    }

}

/// <summary>Engine-side player choice; adapters project it to a transport DTO.</summary>
public class LegalAction
{
    public string ActionId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Actor { get; set; }
    public long? SourceId { get; set; }
    public long? TargetId { get; set; }
    public string? TargetReferenceId { get; set; }
    public string? CardId { get; set; }
    public string? ReasonKey { get; set; }
    public IReadOnlyDictionary<string, object?> Payload { get; set; }
        = new Dictionary<string, object?>();
}
}
