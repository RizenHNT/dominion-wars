using System;
using System.Collections.Generic;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Effects
{

public sealed partial class EffectRuntime
{
    public void Enfeeble(EffectSpec spec, EffectContext context)
    {
        if (spec.Amount >= 0)
        {
            EmitSkipped(context, spec.Action, "effect.amount_must_be_negative");
            return;
        }

        var mode = string.IsNullOrWhiteSpace(spec.Param) ? "both" : spec.Param!.ToLowerInvariant();
        if (mode != "atk" && mode != "hp" && mode != "both")
        {
            EmitSkipped(context, spec.Action, "effect.invalid_param");
            return;
        }

        var targets = _targets.Resolve(spec, context);
        if (targets.Count == 0)
        {
            EmitSkipped(context, spec.Action, "target.none");
            return;
        }

        foreach (var target in targets)
        {
            Commit(_ =>
            {
                if (mode == "atk" || mode == "both")
                {
                    target.Attack = Math.Max(0, target.Attack + spec.Amount);
                }

                if (mode == "hp" || mode == "both")
                {
                    target.Health += spec.Amount;
                    target.MaxHealth = Math.Max(0, target.MaxHealth + spec.Amount);
                }
            });
            Emit("ENFEEBLE_APPLIED", context, Data(
                "target", target.InstanceId,
                "amount", spec.Amount,
                "mode", mode));
        }
    }

    public void Banish(EffectSpec spec, EffectContext context)
    {
        var targets = _targets.Resolve(spec, context);
        if (targets.Count == 0)
        {
            EmitSkipped(context, spec.Action, "target.none");
            return;
        }

        foreach (var target in targets)
        {
            var owner = State.GetPlayer(target.OwnerPlayerIndex);
            if (target.IsLeaderEntity)
            {
                EmitSkipped(context, spec.Action, "target.leader_immune", target.InstanceId);
                continue;
            }

            Commit(_ =>
            {
                owner.Hand.Remove(target);
                State.GetPlayer(target.ControllerPlayerIndex).Field.Remove(target);
                owner.Graveyard.Remove(target);
                owner.Deck.Remove(target);
                target.ResetRuntimeState();
                owner.Deck.Add(target);
                Shuffle(owner.Deck);
            });
            Emit("CARD_BANISHED", context, Data(
                "target", target.InstanceId,
                "owner", owner.PlayerIndex));
        }
    }

    private void Shuffle(IList<CardInstance> cards)
    {
        for (var index = cards.Count - 1; index > 0; index--)
        {
            var swap = State.Random.NextInt(index + 1);
            var item = cards[index];
            cards[index] = cards[swap];
            cards[swap] = item;
        }
    }
}
}
