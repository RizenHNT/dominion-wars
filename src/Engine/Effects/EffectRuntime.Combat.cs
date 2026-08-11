using System;
using System.Collections.Generic;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Effects
{

public sealed partial class EffectRuntime
{
    private static readonly HashSet<string> SupportedKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "嘲讽", "圣盾", "扰魔", "突袭",
    };

    public void Damage(EffectSpec spec, EffectContext context)
    {
        if (!TryPositiveAmount(spec, context, out var amount))
        {
            return;
        }

        if (spec.Target == "ENEMY_TARGET")
        {
            if (context.SelectedCoreTarget.HasValue)
            {
                DamageCore(context.SelectedCoreTarget.Value, amount, context, spec.Action);
            }
            else if (context.SelectedTargetId.HasValue)
            {
                DamageCards(spec, context, amount);
            }
            else
            {
                EmitSkipped(context, spec.Action, "target.selection_required");
            }

            return;
        }

        if (spec.Target == "ENEMY_FACE")
        {
            DamageEnemyCore(amount, context, spec.Action);
            return;
        }

        if (spec.Target == "ENEMY_PLAYER")
        {
            DamagePlayer(State.GetOpponent(context.SourcePlayerIndex), amount, context, spec.Action);
            return;
        }

        if (spec.Target == "SELF_PLAYER")
        {
            DamagePlayer(State.GetPlayer(context.SourcePlayerIndex), amount, context, spec.Action);
            return;
        }

        DamageCards(spec, context, amount);
    }

    public void Heal(EffectSpec spec, EffectContext context)
    {
        if (!TryPositiveAmount(spec, context, out var amount))
        {
            return;
        }

        if (spec.Target == "SELF_PLAYER")
        {
            var self = State.GetPlayer(context.SourcePlayerIndex);
            if (!self.Life.HasValue)
            {
                EmitSkipped(context, spec.Action, "target.no_life_pool");
                return;
            }

            Commit(_ => self.Life += amount);
            Emit("HEALED", context, Data(
                "target", $"player:{self.PlayerIndex}",
                "amount", amount,
                "source", context.SourceCard?.InstanceId));
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
            var before = target.Health;
            Commit(_ => target.Health = Math.Min(target.MaxHealth, target.Health + amount));
            Emit("HEALED", context, Data(
                "target", target.InstanceId,
                "amount", target.Health - before,
                "source", context.SourceCard?.InstanceId));
        }
    }

    public void Destroy(EffectSpec spec, EffectContext context)
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

            if (owner.ProtectedThisTurn)
            {
                EmitSkipped(context, spec.Action, "target.protected", target.InstanceId);
                continue;
            }

            Commit(_ =>
            {
                owner.Field.Remove(target);
                owner.Graveyard.Add(target);
            });
            Emit("MINION_DESTROYED", context, Data("target", target.InstanceId));
        }
    }

    public void Buff(EffectSpec spec, EffectContext context)
    {
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
                    target.Attack += spec.Amount;
                }

                if (mode == "hp" || mode == "both")
                {
                    target.Health += spec.Amount;
                    target.MaxHealth += spec.Amount;
                }
            });
            Emit("BUFF_APPLIED", context, Data(
                "target", target.InstanceId,
                "amount", spec.Amount,
                "mode", mode));
        }
    }

    public void GrantKeyword(EffectSpec spec, EffectContext context)
    {
        if (spec.Param is null || !SupportedKeywords.Contains(spec.Param))
        {
            EmitSkipped(context, spec.Action, "effect.invalid_keyword");
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
                target.Keywords.Add(spec.Param);
                if (spec.Param == "圣盾")
                {
                    target.Shield = true;
                }
            });
            Emit("KEYWORD_GRANTED", context, Data("target", target.InstanceId, "keyword", spec.Param));
        }
    }

    public void RestoreAttacks(EffectSpec spec, EffectContext context)
    {
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
                target.AttacksUsed = 0;
                target.SummonedThisTurn = false;
            });
            Emit("ATTACKS_RESTORED", context, Data("target", target.InstanceId));
        }
    }

    private void DamageCards(EffectSpec spec, EffectContext context, int amount)
    {
        var targets = _targets.Resolve(spec, context);
        if (targets.Count == 0)
        {
            EmitSkipped(context, spec.Action, "target.none");
            return;
        }

        foreach (var target in targets)
        {
            DamageCard(target, amount, context);
        }
    }

    private void DamageCard(CardInstance target, int amount, EffectContext context)
    {
        if (target.Shield)
        {
            Commit(_ => target.Shield = false);
            Emit("DAMAGE_DEALT", context, Data(
                "target", target.InstanceId,
                "amount", 0,
                "source", context.SourceCard?.InstanceId,
                "absorbedByShield", true));
            return;
        }

        Commit(_ => target.Health -= amount);
        Emit("DAMAGE_DEALT", context, Data(
            "target", target.InstanceId,
            "amount", amount,
            "source", context.SourceCard?.InstanceId));
    }

    private void DamageEnemyCore(int amount, EffectContext context, string action)
    {
        var legal = LegalEnemyCoreTargets(context);
        if (context.SelectedCoreTarget.HasValue)
        {
            DamageCore(context.SelectedCoreTarget.Value, amount, context, action);
            return;
        }

        if (legal.Count != 1)
        {
            EmitSkipped(context, action, legal.Count == 0 ? "target.none" : "target.selection_required");
            return;
        }

        DamageCore(legal[0], amount, context, action);
    }

    private void DamageCore(CoreTarget target, int amount, EffectContext context, string action)
    {
        var legal = LegalEnemyCoreTargets(context);
        if (!legal.Contains(target))
        {
            EmitSkipped(context, action, "target.invalid_core");
            return;
        }

        var enemy = State.GetOpponent(context.SourcePlayerIndex);
        switch (target)
        {
            case CoreTarget.RoyalCastle:
                ApplyCastleDamage(amount, context);
                break;
            case CoreTarget.Leader:
                DamageCard(enemy.Leader!, amount, context);
                break;
            case CoreTarget.Life:
                DamagePlayer(enemy, amount, context, action);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    private List<CoreTarget> LegalEnemyCoreTargets(EffectContext context)
    {
        var result = new List<CoreTarget>();
        var enemy = State.GetOpponent(context.SourcePlayerIndex);
        if (State.CastleEnabled && State.CastleHealth > 0)
        {
            result.Add(CoreTarget.RoyalCastle);
        }

        if (enemy.Leader is not null)
        {
            if (context.SourceCard is not null && context.SourceCard.Definition.KingSlayer)
            {
                result.Add(CoreTarget.Leader);
            }
        }
        else if (enemy.Life.HasValue)
        {
            result.Add(CoreTarget.Life);
        }

        return result;
    }

    private void DamagePlayer(PlayerState player, int amount, EffectContext context, string action)
    {
        if (!player.Life.HasValue)
        {
            EmitSkipped(context, action, "target.no_life_pool");
            return;
        }

        Commit(_ =>
        {
            player.Life = Math.Max(0, player.Life.Value - amount);
            player.DamagedThisCycle = true;
        });
        Emit("DAMAGE_DEALT", context, Data(
            "target", $"player:{player.PlayerIndex}",
            "amount", amount,
            "source", context.SourceCard?.InstanceId));
    }
}
}
