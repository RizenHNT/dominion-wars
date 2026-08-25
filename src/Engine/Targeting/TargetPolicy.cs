using System;
using System.Collections.Generic;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Targeting
{

/// <summary>
/// Configurable candidate projection for player-facing targeting. This policy
/// does not bypass card-specific resistance checks during effect resolution.
/// </summary>
public sealed class TargetPolicy
{
    public bool AllowEnemyMinions { get; set; } = true;
    public bool AllowEnemyLeader { get; set; } = true;
    /// <summary>The Royal Castle is shared; this only controls whether it is targetable.</summary>
    public bool AllowRoyalCastle { get; set; } = true;
    public bool AllowEnemyLife { get; set; } = true;

    public IReadOnlyList<TargetReference> GetEnemyCandidates(GameState state, int sourcePlayerIndex)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (sourcePlayerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePlayerIndex));
        }

        var enemy = state.GetOpponent(sourcePlayerIndex);
        if (enemy.HasMultipleActiveLeaders)
        {
            return Array.Empty<TargetReference>();
        }

        var result = new List<TargetReference>();
        foreach (var card in enemy.Field)
        {
            if (!card.IsAlive)
            {
                continue;
            }

            if (card.IsLeaderEntity)
            {
                if (AllowEnemyLeader)
                {
                    result.Add(TargetReference.ForEntity(card));
                }
            }
            else if (card.IsMinion && AllowEnemyMinions)
            {
                result.Add(TargetReference.ForEntity(card));
            }
        }

        if (AllowRoyalCastle && state.CastleEnabled && state.CastleHealth > 0)
        {
            result.Add(TargetReference.ForSharedCore(CoreTarget.RoyalCastle));
        }

        if (AllowEnemyLife && enemy.Leader is null && enemy.Life.HasValue)
        {
            result.Add(TargetReference.ForCore(CoreTarget.Life, enemy.PlayerIndex));
        }

        return result;
    }

    /// <summary>
    /// Parses a UI-stable candidate id only after checking it against this
    /// policy and the current state. Card effects still perform their own
    /// resistance and keyword checks when resolving a selected target.
    /// </summary>
    public bool TryResolveEnemyCandidate(
        GameState state,
        int sourcePlayerIndex,
        string targetId,
        out TargetReference? target)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            target = null;
            return false;
        }

        foreach (var candidate in GetEnemyCandidates(state, sourcePlayerIndex))
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

    /// <summary>
    /// Converts an approved stable UI id into the two fields consumed by
    /// <see cref="Effects.EffectContext"/>. Future action resolvers must call
    /// this instead of parsing ids independently.
    /// </summary>
    public bool TryResolveEnemySelection(
        GameState state,
        int sourcePlayerIndex,
        string targetId,
        out TargetSelection selection)
    {
        if (TryResolveEnemyCandidate(state, sourcePlayerIndex, targetId, out var target) && target is not null)
        {
            selection = target.EntityId.HasValue
                ? TargetSelection.ForEntity(target.EntityId.Value)
                : TargetSelection.ForCore(target.Core!.Value);
            return true;
        }

        selection = default;
        return false;
    }
}

public sealed class TargetReference
{
    private TargetReference(
        string id,
        TargetKind kind,
        int? ownerPlayerIndex,
        long? entityId,
        CoreTarget? coreTarget)
    {
        Id = id;
        Kind = kind;
        OwnerPlayerIndex = ownerPlayerIndex;
        EntityId = entityId;
        Core = coreTarget;
    }

    public string Id { get; }
    public TargetKind Kind { get; }
    public int? OwnerPlayerIndex { get; }
    public long? EntityId { get; }
    public CoreTarget? Core { get; }

    public static TargetReference ForEntity(CardInstance card)
    {
        if (card is null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        return new TargetReference(
            "entity_" + card.InstanceId.ToString("D12", System.Globalization.CultureInfo.InvariantCulture),
            card.IsLeaderEntity ? TargetKind.EnemyLeader : TargetKind.EnemyMinion,
            card.OwnerPlayerIndex,
            card.InstanceId,
            null);
    }

    public static TargetReference ForCore(CoreTarget coreTarget, int ownerPlayerIndex)
    {
        var id = coreTarget switch
        {
            CoreTarget.RoyalCastle => "core:shared_castle",
            CoreTarget.Leader => "core:leader",
            CoreTarget.Life => "core:player_" + ownerPlayerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":life",
            _ => throw new ArgumentOutOfRangeException(nameof(coreTarget)),
        };
        return new TargetReference(id, ToKind(coreTarget), ownerPlayerIndex, null, coreTarget);
    }

    public static TargetReference ForSharedCore(CoreTarget coreTarget)
    {
        if (coreTarget != CoreTarget.RoyalCastle)
        {
            throw new ArgumentException("Only the Royal Castle is a shared core target.", nameof(coreTarget));
        }

        return new TargetReference("core:shared_castle", TargetKind.RoyalCastle, null, null, coreTarget);
    }

    private static TargetKind ToKind(CoreTarget coreTarget)
    {
        return coreTarget switch
        {
            CoreTarget.RoyalCastle => TargetKind.RoyalCastle,
            CoreTarget.Leader => TargetKind.EnemyLeader,
            CoreTarget.Life => TargetKind.EnemyLife,
            _ => throw new ArgumentOutOfRangeException(nameof(coreTarget)),
        };
    }
}

public enum TargetKind
{
    EnemyMinion,
    EnemyLeader,
    RoyalCastle,
    EnemyLife,
}

/// <summary>Effect-ready target selection; exactly one member is populated.</summary>
public readonly struct TargetSelection
{
    private TargetSelection(long? entityId, CoreTarget? coreTarget)
    {
        EntityId = entityId;
        CoreTarget = coreTarget;
    }

    public long? EntityId { get; }
    public CoreTarget? CoreTarget { get; }

    public static TargetSelection ForEntity(long entityId)
    {
        if (entityId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entityId));
        }

        return new TargetSelection(entityId, null);
    }

    public static TargetSelection ForCore(CoreTarget coreTarget)
    {
        return new TargetSelection(null, coreTarget);
    }
}
}
