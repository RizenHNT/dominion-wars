using System;
using System.Collections.Generic;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Targeting;

namespace DominionWars.Engine.Turns
{

internal sealed class CardTargetValidator
{
    private readonly TargetPolicy _policy;

    public CardTargetValidator(TargetPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public TargetRequirement GetRequirement(IReadOnlyList<EffectSpec> effects)
    {
        var requirement = TargetRequirement.None;
        foreach (var effect in effects)
        {
            if (effect.Target is "ENEMY_MINION" or "ENEMY_SINGLE" or "SINGLE_ENEMY")
            {
                requirement = TargetRequirement.Entity;
            }
            else if (effect.Target == "ENEMY_TARGET")
            {
                requirement = TargetRequirement.AnyEnemy;
            }
            else if (effect.Target == "ENEMY_FACE")
            {
                requirement = TargetRequirement.Core;
            }
        }

        return requirement;
    }

    public IReadOnlyList<TargetReference> GetLegalTargets(
        GameState state,
        int sourcePlayerIndex,
        CardInstance sourceCard,
        IReadOnlyList<EffectSpec> effects)
    {
        var requirement = GetRequirement(effects);
        if (requirement == TargetRequirement.None)
        {
            return Array.Empty<TargetReference>();
        }

        var result = new List<TargetReference>();
        foreach (var candidate in _policy.GetEnemyCandidates(state, sourcePlayerIndex))
        {
            if (TryResolve(
                state,
                sourcePlayerIndex,
                candidate.Id,
                sourceCard,
                effects,
                requirement,
                out _))
            {
                result.Add(candidate);
            }
        }

        return result.AsReadOnly();
    }

    public bool TryResolve(
        GameState state,
        int sourcePlayerIndex,
        string targetId,
        CardInstance sourceCard,
        IReadOnlyList<EffectSpec> effects,
        TargetRequirement requirement,
        out TargetSelection selection)
    {
        if (!_policy.TryResolveEnemyCandidate(state, sourcePlayerIndex, targetId, out var target)
            || target is null)
        {
            selection = default;
            return false;
        }

        if (target.EntityId.HasValue)
        {
            var entity = state.FindEntity(target.EntityId.Value)!;
            if (entity.HasKeyword("扰魔") || entity.HasKeyword("WARD"))
            {
                selection = default;
                return false;
            }

            if (requirement == TargetRequirement.Core)
            {
                if (target.Kind != TargetKind.EnemyLeader || !CanAffectLeader(sourceCard, effects, entity))
                {
                    selection = default;
                    return false;
                }

                selection = TargetSelection.ForCore(CoreTarget.Leader);
                return true;
            }

            if (entity.IsLeaderEntity && !CanAffectLeader(sourceCard, effects, entity))
            {
                selection = default;
                return false;
            }

            selection = TargetSelection.ForEntity(entity.InstanceId);
            return true;
        }

        if (requirement == TargetRequirement.Entity || !target.Core.HasValue)
        {
            selection = default;
            return false;
        }

        selection = TargetSelection.ForCore(target.Core.Value);
        return true;
    }

    private static bool CanAffectLeader(
        CardInstance sourceCard,
        IReadOnlyList<EffectSpec> effects,
        CardInstance leader)
    {
        foreach (var effect in effects)
        {
            if ((effect.KingSlayer ?? sourceCard.Definition.KingSlayer)
                && ContainsOrdinal(leader.Definition.Vulnerabilities, effect.Action))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsOrdinal(IEnumerable<string> values, string expected)
    {
        foreach (var value in values)
        {
            if (string.Equals(value, expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

internal enum TargetRequirement
{
    None,
    Entity,
    AnyEnemy,
    Core,
}
}
