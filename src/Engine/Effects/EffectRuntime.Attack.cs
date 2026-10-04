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

    /// <summary>
    /// Damage a NON-MINION leader by ATTACK.
    ///
    /// TWO OUTCOMES ONLY, and the middle one was REMOVED on 2026-09-12:
    ///
    ///   1. THE LEADER HAS A DURABILITY TRACK (`machine_leader` 8, `wood_leader` 20):
    ///      the attack reduces DURABILITY. That is the leader's own defeat track, so "beating
    ///      the leader" and "completing your own victory condition" stay the same thing.
    ///   2. THE LEADER HAS NO DURABILITY TRACK (`sea_leader` is the only one): the attack
    ///      resolves as a NO-OP and is reported as `EFFECT_SKIPPED`. Such a leader is not
    ///      defeated by attacking at all — it wins, or loses, through its own declared
    ///      objective or its own printed negative effects (owner ruling, 2026-09-12).
    ///
    /// WHAT WAS REMOVED: an `else { DamagePlayer(owner, ...) }` branch that spent the attack on
    /// the leader's OWNER's player life pool. That was the "attacking a minion drains a health
    /// bar" behaviour, and it is wrong under the current rules for two independent reasons:
    ///
    ///   * The player life pool is not a victory or defeat condition (docs/RULES.md §1, §7), so
    ///     draining it accomplishes nothing — the attack would be silently wasted.
    ///   * A durability-less non-minion leader was therefore ONLY reachable through its owner's
    ///     life pool, which made that pool its single point of vulnerability. With the life pool
    ///     now off by default that produced the absurd state "the attack lands, the pool drops,
    ///     and nothing happens".
    ///
    /// Keeping the branch would leave every ATTACK against such a leader doing invisible work.
    /// Removing it makes the answer explicit: no durability track means no defeat track.
    /// </summary>
    private void DamageNonMinionLeader(
        CardInstance leader,
        int amount,
        EffectContext context)
    {
        var owner = State.GetPlayer(leader.OwnerPlayerIndex);
        Commit(_ => owner.DamagedThisCycle = true);

        if (leader.Definition.LeaderDurability <= 0)
        {
            // No defeat track of its own: the attack has nothing to reduce. Reported rather than
            // silently swallowed, so a caller can see that the attack was legal but inert.
            EmitSkipped(context, "ATTACK", "target.leader_has_no_durability");
            return;
        }

        Commit(_ => leader.Durability = Math.Max(0, leader.Durability - amount));
        Emit("DAMAGE_DEALT", context, Data(
            "target", leader.InstanceId,
            "amount", amount,
            "source", context.SourceCard?.InstanceId));
    }
}
}
