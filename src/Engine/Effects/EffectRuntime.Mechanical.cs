using System;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Effects
{

public sealed partial class EffectRuntime
{
    public void Control(EffectSpec spec, EffectContext context)
    {
        if (spec.Amount < 1)
        {
            EmitSkipped(context, spec.Action, "effect.invalid_amount");
            return;
        }

        var targets = _targets.Resolve(spec, context);
        if (targets.Count == 0)
        {
            EmitSkipped(context, spec.Action, "target.none");
            return;
        }

        var controller = State.GetPlayer(context.SourcePlayerIndex);
        foreach (var target in targets)
        {
            if (target.IsLeaderEntity)
            {
                EmitSkipped(context, spec.Action, "target.leader_immune", target.InstanceId);
                continue;
            }

            var previousController = State.GetPlayer(target.ControllerPlayerIndex);
            if (previousController.PlayerIndex == controller.PlayerIndex)
            {
                EmitSkipped(context, spec.Action, "target.already_controlled", target.InstanceId);
                continue;
            }

            Commit(_ =>
            {
                previousController.Field.Remove(target);
                controller.Field.Add(target);
                target.ControlledByPlayerIndex = controller.PlayerIndex;
                target.ControlTurnsRemaining = spec.Amount;
            });
            Emit("CONTROL_APPLIED", context, Data(
                "target", target.InstanceId,
                "controller", controller.PlayerIndex,
                "turns", spec.Amount));
        }
    }

    /// <summary>
    /// Moves the source card (or an explicitly selected card) into the public
    /// commit queue. The current engine has no independent mana resource, so
    /// the declared fee is exposed as event metadata until the approved cost
    /// model supplies a payment source; this method never invents one.
    /// </summary>
    public void CommitCard(EffectSpec spec, EffectContext context)
    {
        var card = ResolveMechanicalCard(spec, context);
        if (card is null)
        {
            EmitSkipped(context, spec.Action, "target.none");
            return;
        }

        if (!IsMechanicalCard(card))
        {
            EmitSkipped(context, spec.Action, "target.mechanical_card_required", card.InstanceId);
            return;
        }

        if (card.IsLeaderEntity || card.Definition.IsLeader)
        {
            EmitSkipped(context, spec.Action, "target.leader_immune", card.InstanceId);
            return;
        }

        var owner = State.GetPlayer(card.OwnerPlayerIndex);
        if (owner.CommitQueue.Contains(card) || owner.CloudStack.Contains(card))
        {
            EmitSkipped(context, spec.Action, "mechanical.card_already_committed", card.InstanceId);
            return;
        }

        Commit(_ =>
        {
            RemoveFromZones(card);
            owner.CommitQueue.Add(card);
        });
        Emit("CARD_COMMITTED", context, Data(
            "target", card.InstanceId,
            "owner", owner.PlayerIndex,
            "cost", card.Definition.CommitCost));

        if (card.Definition.CommitEffects.Count > 0 && !IsGameOver)
        {
            EffectDispatcher.CreateDefault(this).ApplyAll(
                card.Definition.CommitEffects,
                context.ForSource(owner.PlayerIndex, card));
        }
    }

    /// <summary>Pushes the owner's queue in FIFO order onto the cloud stack.</summary>
    public void Push(EffectSpec spec, EffectContext context)
    {
        var owner = State.GetPlayer(context.SourcePlayerIndex);
        PushQueue(owner, context);
    }

    internal void PushQueue(PlayerState owner, EffectContext context)
    {
        var pending = new List<CardInstance>(owner.CommitQueue);
        foreach (var card in pending)
        {
            if (!owner.CommitQueue.Remove(card))
            {
                continue;
            }

            owner.CloudStack.Add(card);
            Emit("CARD_PUSHED", context, Data(
                "target", card.InstanceId,
                "owner", owner.PlayerIndex,
                "cost", card.Definition.UploadCost));

            if (card.Definition.PushEffects.Count > 0 && !IsGameOver)
            {
                EffectDispatcher.CreateDefault(this).ApplyAll(
                    card.Definition.PushEffects,
                    context.ForSource(owner.PlayerIndex, card));
            }
        }
    }

    /// <summary>Pulls exactly the cloud-stack top and sends it to the graveyard.</summary>
    public void Pull(EffectSpec spec, EffectContext context)
    {
        var owner = State.GetPlayer(context.SourcePlayerIndex);
        if (owner.CloudStack.Count == 0)
        {
            EmitSkipped(context, spec.Action, "mechanical.cloud_empty");
            return;
        }

        var card = owner.CloudStack[owner.CloudStack.Count - 1];
        var carrier = ResolvePullCarrier(owner, context);
        if (carrier is null)
        {
            EmitSkipped(context, spec.Action, "target.download_carrier_required", card.InstanceId);
            return;
        }

        var pullDispatcher = EffectDispatcher.CreateDefault(this);
        foreach (var effect in card.Definition.PullEffects)
        {
            if (!pullDispatcher.RegisteredActions.Contains(effect.Action))
            {
                EmitSkipped(context, spec.Action, "action.unknown_effect", card.InstanceId);
                return;
            }
        }

        // PULL removes the visible stack top before resolving its payload.
        // Besides matching the rulebook order, this prevents a nested PULL
        // payload from observing and resolving the same card recursively.
        Commit(_ =>
        {
            owner.CloudStack.RemoveAt(owner.CloudStack.Count - 1);
        });

        if (card.Definition.PullEffects.Count > 0 && !IsGameOver)
        {
            var pullContext = new EffectContext(
                owner.PlayerIndex,
                context.RootEventId,
                sourceCard: carrier,
                playedCard: card,
                drawnCards: context.DrawnCards,
                selectedTargetId: carrier.InstanceId);
            pullDispatcher.ApplyAll(card.Definition.PullEffects, pullContext);
        }

        Commit(_ =>
        {
            card.ResetRuntimeState();
            owner.Graveyard.Add(card);
            owner.PullCount++;
        });
        Emit("CARD_PULLED", context, Data(
            "target", card.InstanceId,
            "owner", owner.PlayerIndex,
            "carrier", carrier.InstanceId,
            "cost", card.Definition.DownloadCost,
            "pullCount", owner.PullCount));
    }

    /// <summary>Returns one explicitly selected queue card, or the only card.</summary>
    public void Rollback(EffectSpec spec, EffectContext context)
    {
        var owner = State.GetPlayer(context.SourcePlayerIndex);
        if (owner.CommitQueue.Count == 0)
        {
            EmitSkipped(context, spec.Action, "mechanical.queue_empty");
            return;
        }

        CardInstance? card = null;
        if (context.SelectedTargetId.HasValue)
        {
            foreach (var candidate in owner.CommitQueue)
            {
                if (candidate.InstanceId == context.SelectedTargetId.Value)
                {
                    card = candidate;
                    break;
                }
            }
        }
        else if (owner.CommitQueue.Count == 1)
        {
            card = owner.CommitQueue[0];
        }

        if (card is null)
        {
            EmitSkipped(context, spec.Action, "target.selection_required");
            return;
        }

        Commit(_ =>
        {
            owner.CommitQueue.Remove(card);
            card.ResetRuntimeState();
            owner.Hand.Add(card);
        });
        Emit("CARD_ROLLED_BACK", context, Data(
            "target", card.InstanceId,
            "owner", owner.PlayerIndex));
    }

    private CardInstance? ResolveMechanicalCard(EffectSpec spec, EffectContext context)
    {
        if (string.Equals(spec.Target, "SELF", StringComparison.Ordinal))
        {
            var self = context.PlayedCard ?? context.SourceCard;
            return self is not null
                && State.GetPlayer(context.SourcePlayerIndex).Field.Contains(self)
                ? self
                : null;
        }

        if (context.SelectedTargetId.HasValue)
        {
            var selected = State.FindEntity(context.SelectedTargetId.Value);
            if (selected is not null && selected.OwnerPlayerIndex == context.SourcePlayerIndex)
            {
                return selected;
            }
        }

        return null;
    }

    private static bool IsMechanicalCard(CardInstance card)
    {
        return card.Definition.Tags.Contains("机械")
            || card.Definition.Tags.Contains("MECHANICAL")
            || string.Equals(card.Definition.Faction, "机械遗迹", StringComparison.Ordinal);
    }

    private CardInstance? ResolvePullCarrier(PlayerState owner, EffectContext context)
    {
        if (context.SelectedTargetId.HasValue)
        {
            var selected = State.FindEntity(context.SelectedTargetId.Value);
            return selected is not null
                && IsDownloadCarrier(owner, selected)
                ? selected
                : null;
        }

        if (context.PlayedCard is not null
            && IsDownloadCarrier(owner, context.PlayedCard))
        {
            return context.PlayedCard;
        }

        if (context.SourceCard is not null
            && IsDownloadCarrier(owner, context.SourceCard))
        {
            return context.SourceCard;
        }

        CardInstance? only = null;
        var candidates = new List<CardInstance>(owner.Field.Count + owner.LeaderZone.Count);
        candidates.AddRange(owner.Field);
        candidates.AddRange(owner.LeaderZone);
        foreach (var candidate in candidates)
        {
            if (!IsDownloadCarrier(owner, candidate))
            {
                continue;
            }

            if (only is not null)
            {
                return null;
            }

            only = candidate;
        }

        return only;
    }

    internal static bool IsDownloadCarrier(PlayerState owner, CardInstance card)
    {
        if (card.ControllerPlayerIndex != owner.PlayerIndex)
        {
            return false;
        }

        if (owner.Field.Contains(card) && IsMechanicalCard(card))
        {
            return true;
        }

        if (card.IsLeaderEntity && card.Definition.IsLeader)
        {
            return owner.LeaderZone.Contains(card)
                && card.Definition.IsLandmark
                && IsMechanicalCard(card);
        }

        return false;
    }

    private void RemoveFromZones(CardInstance card)
    {
        foreach (var player in State.Players)
        {
            player.Deck.Remove(card);
            player.Hand.Remove(card);
            player.Field.Remove(card);
            player.Graveyard.Remove(card);
            player.CommitQueue.Remove(card);
            player.CloudStack.Remove(card);
        }
    }
}
}
