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
    public const string Rollback = "ROLLBACK";
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

            var effectivePunish = CardPlayRules.EffectivePunish(state, player, card);
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
            if (PullActionHandler.HasSupportedPullEffects(state, top))
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

                    var pullTargets = PullActionHandler.GetLegalTargets(state, playerIdx, top);
                    if (PullActionHandler.RequiresTargetSelection(top.Definition.PullEffects))
                    {
                        foreach (var pullTarget in pullTargets)
                        {
                            actions.Add(CreatePullAction(
                                source,
                                top,
                                pullTarget.InstanceId));
                        }
                    }
                    else
                    {
                        actions.Add(CreatePullAction(source, top));
                    }
                }
            }
        }

        foreach (var queued in player.CommitQueue)
        {
            // ROLLBACK is the 9th player action (RUNTIME_CONTRACT_1.31.md,
            // 1.31-player-rollback). The chosen queue card is carried by the
            // action id and SourceId, so no selectedEntityIds payload is
            // needed. Its punish value is deliberately 0: per RULES.md §12.4
            // "不能回溯已经发生的惩罚抽牌" plus the glossary's "费用不返还",
            // the earlier COMMIT's commitCost is neither re-charged nor
            // refunded, so this path produces no punish draw and opens no
            // punish response window.
            actions.Add(CreateRollbackAction(queued, playerIdx));
        }

        foreach (var source in player.Field)
        {
            if (EffectRuntime.IsMechanicalCard(source)
                && !source.IsLeaderEntity
                && !source.Definition.IsLeader
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
                        ["punish"] = source.Definition.CommitCost,
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

    private static LegalAction CreateRollbackAction(CardInstance queued, int playerIdx)
    {
        return new LegalAction
        {
            ActionId = RollbackActionHandler.CreateActionId(queued.InstanceId),
            Type = Rollback,
            Actor = playerIdx,
            SourceId = queued.InstanceId,
            CardId = queued.Definition.Id,
            ReasonKey = "action.rollback",
        };
    }

    private static LegalAction CreatePullAction(
        CardInstance source,
        CardInstance top,
        long? selectedTargetId = null)
    {
        var payload = new Dictionary<string, object?>();
        payload["punish"] = top.Definition.DownloadCost;
        if (selectedTargetId.HasValue)
        {
            payload["selectedEntityIds"] = new[] { selectedTargetId.Value };
        }

        return new LegalAction
        {
            ActionId = PullActionHandler.CreateActionId(
                source.InstanceId,
                top.InstanceId,
                selectedTargetId),
            Type = Pull,
            Actor = source.ControllerPlayerIndex,
            SourceId = source.InstanceId,
            TargetId = top.InstanceId,
            CardId = top.Definition.Id,
            ReasonKey = "action.pull",
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
