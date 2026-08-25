using System;
using System.Collections.Generic;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Targeting;

namespace DominionWars.Engine.Turns
{

public sealed class AttackTargetPolicy
{
    public bool CanAttack(CardInstance attacker)
    {
        return attacker is not null
            && attacker.IsMinion
            && attacker.IsAlive
            && !attacker.Sealed
            && attacker.Attack > 0
            && (!attacker.SummonedThisTurn || HasCharge(attacker))
            && attacker.AttacksUsed < attacker.Definition.AttacksPerTurn;
    }

    public IReadOnlyList<AttackTarget> GetLegalTargets(GameState state, CardInstance attacker)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (!CanAttack(attacker))
        {
            return Array.Empty<AttackTarget>();
        }

        var controller = attacker.ControllerPlayerIndex;
        var enemy = state.GetOpponent(controller);
        // A malformed state with more than one active leader has no
        // authoritative leader target.  Fail closed instead of falling
        // through to the player's life core (or exposing an arbitrary
        // leader) while the state is awaiting repair/rejection.
        if (enemy.HasMultipleActiveLeaders)
        {
            return Array.Empty<AttackTarget>();
        }

        var taunts = new List<CardInstance>();
        foreach (var card in enemy.Field)
        {
            if (card.IsMinion && card.IsAlive && HasTaunt(card))
            {
                taunts.Add(card);
            }
        }

        var result = new List<AttackTarget>();
        if (taunts.Count > 0)
        {
            foreach (var taunt in taunts)
            {
                result.Add(AttackTarget.ForEntity(taunt));
            }

            return result;
        }

        foreach (var card in enemy.Field)
        {
            if (card.IsMinion && !card.IsLeaderEntity && card.IsAlive)
            {
                result.Add(AttackTarget.ForEntity(card));
            }
        }

        var leader = enemy.Leader;
        if (leader is not null
            && !string.Equals(leader.Definition.Type, "AMBUSH", StringComparison.OrdinalIgnoreCase)
            && (!IsGuarded(leader, enemy) || attacker.Definition.KingSlayer))
        {
            result.Add(AttackTarget.ForEntity(leader));
        }

        if (state.CastleEnabled && state.CastleHealth > 0)
        {
            result.Add(AttackTarget.ForCore("core:shared_castle", CoreTarget.RoyalCastle));
        }

        if (leader is null && enemy.Life.HasValue)
        {
            result.Add(AttackTarget.ForCore(
                "core:player_" + enemy.PlayerIndex + ":life",
                CoreTarget.Life));
        }

        return result;
    }

    public bool TryResolve(
        GameState state,
        CardInstance attacker,
        string targetId,
        out AttackTarget? target)
    {
        foreach (var candidate in GetLegalTargets(state, attacker))
        {
            if (string.Equals(candidate.Id, targetId, StringComparison.Ordinal))
            {
                target = candidate;
                return true;
            }
        }

        target = null;
        return false;
    }

    private static bool HasTaunt(CardInstance card)
    {
        return card.HasKeyword("嘲讽") || card.HasKeyword("TAUNT");
    }

    private static bool HasCharge(CardInstance card)
    {
        return card.HasKeyword("突袭") || card.HasKeyword("CHARGE");
    }

    private static bool IsGuarded(CardInstance leader, PlayerState owner)
    {
        if (!leader.Definition.Guard)
        {
            return false;
        }

        foreach (var card in owner.Field)
        {
            if (card != leader && card.IsMinion && !card.IsLeaderEntity && card.IsAlive)
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class AttackTarget
{
    private AttackTarget(string id, long? entityId, CoreTarget? coreTarget)
    {
        Id = id;
        EntityId = entityId;
        CoreTarget = coreTarget;
    }

    public string Id { get; }
    public long? EntityId { get; }
    public CoreTarget? CoreTarget { get; }

    public static AttackTarget ForEntity(CardInstance card)
    {
        var reference = TargetReference.ForEntity(card);
        return new AttackTarget(reference.Id, card.InstanceId, null);
    }

    public static AttackTarget ForCore(string id, CoreTarget coreTarget)
    {
        return new AttackTarget(id, null, coreTarget);
    }
}
}
