using System;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Effects
{

public sealed partial class EffectRuntime
{
    internal void ResolveAttack(
        CardInstance attacker,
        long? targetEntityId,
        CoreTarget? coreTarget,
        long rootEventId,
        EffectContext? context = null)
    {
        if (attacker is null)
        {
            throw new ArgumentNullException(nameof(attacker));
        }

        if (!State.Events.IsRootEvent(rootEventId))
        {
            throw new InvalidOperationException("Attack resolution needs an existing root event.");
        }

        if (targetEntityId.HasValue == coreTarget.HasValue)
        {
            throw new ArgumentException("An attack needs exactly one entity or core target.");
        }

        context ??= new EffectContext(
            attacker.ControllerPlayerIndex,
            rootEventId,
            sourceCard: attacker,
            attacker: attacker);
        Commit(_ => attacker.AttacksUsed++);
        if (context.Negated)
        {
            CheckAll(context);
            return;
        }
        if (targetEntityId.HasValue)
        {
            ResolveEntityAttack(attacker, targetEntityId.Value, context);
        }
        else
        {
            ResolveCoreAttack(attacker, coreTarget!.Value, context);
        }

        CheckAll(context);
    }

    private void ResolveEntityAttack(
        CardInstance attacker,
        long targetEntityId,
        EffectContext context)
    {
        var target = State.FindEntity(targetEntityId)
            ?? throw new InvalidOperationException("The validated attack target disappeared.");
        if (target.IsMinion)
        {
            var retaliation = target.Attack;
            DamageCard(target, attacker.Attack, context);
            if (retaliation > 0)
            {
                DamageCard(attacker, retaliation, context.ForSource(target.ControllerPlayerIndex, target));
            }

            return;
        }

        DamageNonMinionLeader(target, attacker.Attack, context);
    }

    private void ResolveCoreAttack(
        CardInstance attacker,
        CoreTarget target,
        EffectContext context)
    {
        switch (target)
        {
            case CoreTarget.RoyalCastle:
                ApplyCastleDamage(attacker.Attack, context);
                break;
            case CoreTarget.Life:
                DamagePlayer(State.GetOpponent(attacker.ControllerPlayerIndex), attacker.Attack, context, "ATTACK");
                break;
            case CoreTarget.Leader:
                var leader = State.GetOpponent(attacker.ControllerPlayerIndex).Leader;
                if (leader is null)
                {
                    throw new InvalidOperationException("The validated leader target disappeared.");
                }

                DamageNonMinionLeader(leader, attacker.Attack, context);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    private void DamageNonMinionLeader(
        CardInstance leader,
        int amount,
        EffectContext context)
    {
        var owner = State.GetPlayer(leader.OwnerPlayerIndex);
        Commit(_ => owner.DamagedThisCycle = true);
        if (leader.Definition.LeaderDurability > 0)
        {
            Commit(_ => leader.Durability = Math.Max(0, leader.Durability - amount));
            Emit("DAMAGE_DEALT", context, Data(
                "target", leader.InstanceId,
                "amount", amount,
                "source", context.SourceCard?.InstanceId));
            return;
        }

        DamagePlayer(owner, amount, context, "ATTACK");
    }
}
}
