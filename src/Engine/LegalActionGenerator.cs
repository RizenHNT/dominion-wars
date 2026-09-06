using System;
using System.Collections.Generic;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Targeting;
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
    private readonly CardTargetValidator _cardTargets;

    public LegalActionGenerator(
        AttackTargetPolicy? attackTargets = null,
        TargetPolicy? cardTargets = null)
    {
        _attackTargets = attackTargets ?? new AttackTargetPolicy();
        _cardTargets = new CardTargetValidator(cardTargets ?? new TargetPolicy());
    }

    public const string PlayCard = "PLAY_CARD";
    public const string Attack = "ATTACK";
    public const string Commit = "COMMIT";
    public const string Pull = "PULL";
    public const string EndTurn = "END_TURN";

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

            var effectivePunish = CardPlayRules.EffectivePunish(player, card);
            var playPayload = new Dictionary<string, object?>
            {
                ["punish"] = effectivePunish,
            };
            if (player.PunishToSelfDiscardThisTurn && effectivePunish > 0)
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
                    effectivePunish,
                    candidates.Count);
                playPayload["discardCandidateIds"] = candidates;
            }

            var effects = card.PunishActivated
                ? card.Definition.PunishEffects
                : card.Definition.OnPlayEffects;
            var targetRequirement = _cardTargets.GetRequirement(effects);
            var fizzle = !player.PunishToSelfDiscardThisTurn
                && effectivePunish > opponent.Deck.Count;
            if (!fizzle && targetRequirement != TargetRequirement.None)
            {
                foreach (var target in _cardTargets.GetLegalTargets(
                    state,
                    playerIdx,
                    card,
                    effects))
                {
                    actions.Add(CreatePlayAction(
                        card,
                        playerIdx,
                        playPayload,
                        target));
                }
                continue;
            }

            actions.Add(CreatePlayAction(card, playerIdx, playPayload));

        }

        if (player.CloudStack.Count > 0)
        {
            var top = player.CloudStack[player.CloudStack.Count - 1];
            if (top.Definition.DownloadCost == 0
                && PullActionHandler.HasSupportedPullEffects(state, top))
            {
                var pullSources = new List<CardInstance>(player.Field.Count + player.LeaderZone.Count);
                pullSources.AddRange(player.Field);
                pullSources.AddRange(player.LeaderZone);
                foreach (var source in pullSources)
                {
                    if (!EffectRuntime.IsDownloadCarrier(player, source))
                    {
                        continue;
                    }

                    actions.Add(new LegalAction
                    {
                        ActionId = $"pull_{source.InstanceId}_{top.InstanceId}",
                        Type = Pull,
                        Actor = playerIdx,
                        SourceId = source.InstanceId,
                        TargetId = top.InstanceId,
                        CardId = top.Definition.Id,
                        ReasonKey = "action.pull",
                    });
                }
            }
        }

        foreach (var source in player.Field)
        {
            if (EffectRuntime.IsMechanicalCard(source)
                && !source.IsLeaderEntity
                && !source.Definition.IsLeader
                && source.Definition.CommitCost == 0
                && CommitActionHandler.HasSupportedCommitEffects(state, source))
            {
                actions.Add(new LegalAction
                {
                    ActionId = $"commit_{source.InstanceId}",
                    Type = Commit,
                    Actor = playerIdx,
                    SourceId = source.InstanceId,
                    CardId = source.Definition.Id,
                    ReasonKey = "action.commit",
                    Payload = new Dictionary<string, object?>
                    {
                        ["commitCost"] = source.Definition.CommitCost,
                    },
                });
            }

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

    internal static string PlayActionId(long sourceId, string? targetReferenceId = null)
    {
        return string.IsNullOrWhiteSpace(targetReferenceId)
            ? $"play_{sourceId}"
            : $"play_{sourceId}_{targetReferenceId}";
    }

    private static LegalAction CreatePlayAction(
        CardInstance card,
        int playerIdx,
        IReadOnlyDictionary<string, object?> payload,
        TargetReference? target = null)
    {
        return new LegalAction
        {
            ActionId = PlayActionId(card.InstanceId, target?.Id),
            Type = PlayCard,
            Actor = playerIdx,
            SourceId = card.InstanceId,
            TargetId = target?.EntityId,
            TargetReferenceId = target?.EntityId.HasValue == true ? null : target?.Id,
            CardId = card.Definition.Id,
            ReasonKey = "action.play_card",
            Payload = payload,
        };
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
