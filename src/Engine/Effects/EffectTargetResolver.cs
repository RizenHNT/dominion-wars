using System;
using System.Collections.Generic;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Effects
{

internal sealed class EffectTargetResolver
{
    private const string WardKeyword = "扰魔";
    private readonly GameState _state;

    public EffectTargetResolver(GameState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public IReadOnlyList<CardInstance> Resolve(EffectSpec spec, EffectContext context)
    {
        var self = _state.GetPlayer(context.SourcePlayerIndex);
        var enemy = _state.GetOpponent(context.SourcePlayerIndex);
        var target = spec.Target ?? string.Empty;

        switch (target)
        {
            case "SELF":
                return ResolveSelf(self, context.SourceCard, context);
            case "ENEMY_TARGET":
                if (!context.SelectedTargetId.HasValue)
                {
                    return Array.Empty<CardInstance>();
                }

                return ResolveSingle(FilterEnemy(enemy.Field, spec, context), context.SelectedTargetId);
            case "ENEMY_MINION":
            case "ENEMY_SINGLE":
            case "SINGLE_ENEMY":
                return ResolveSingle(FilterEnemy(enemy.Field, spec, context), context.SelectedTargetId);
            case "FRIENDLY_MINION":
                return ResolveSingle(FilterFriendly(self.Field, context), context.SelectedTargetId);
            case "ANY_MINION":
                var any = FilterFriendly(self.Field, context);
                any.AddRange(FilterEnemy(enemy.Field, spec, context));
                return ResolveSingle(any, context.SelectedTargetId);
            case "ALL_ENEMY_MINIONS":
                return FilterEnemy(enemy.Field, spec, context, ignoreWard: true);
            case "ALL_FRIENDLY_MINIONS":
                return FilterFriendly(self.Field, context);
            case "ALL_MINIONS":
                var all = FilterFriendly(self.Field, context);
                all.AddRange(FilterEnemy(enemy.Field, spec, context, ignoreWard: true));
                return all;
            default:
                return Array.Empty<CardInstance>();
        }
    }

    private static IReadOnlyList<CardInstance> ResolveSelf(
        PlayerState owner,
        CardInstance? source,
        EffectContext context)
    {
        if (source is not null && source.IsMinion
            && (source.IsAlive || context.DeferDeaths)
            && owner.Field.Contains(source))
        {
            return new[] { source };
        }

        return Array.Empty<CardInstance>();
    }

    private static IReadOnlyList<CardInstance> ResolveSingle(
        IReadOnlyList<CardInstance> options,
        long? selectedTargetId)
    {
        if (selectedTargetId.HasValue)
        {
            foreach (var option in options)
            {
                if (option.InstanceId == selectedTargetId.Value)
                {
                    return new[] { option };
                }
            }

            return Array.Empty<CardInstance>();
        }

        return options.Count == 1 ? new[] { options[0] } : Array.Empty<CardInstance>();
    }

    private static List<CardInstance> FilterFriendly(
        IEnumerable<CardInstance> cards,
        EffectContext context)
    {
        var result = new List<CardInstance>();
        foreach (var card in cards)
        {
            if (card.IsMinion && (card.IsAlive || context.DeferDeaths))
            {
                result.Add(card);
            }
        }

        return result;
    }

    private static List<CardInstance> FilterEnemy(
        IEnumerable<CardInstance> cards,
        EffectSpec spec,
        EffectContext context,
        bool ignoreWard = false)
    {
        var result = new List<CardInstance>();
        foreach (var card in cards)
        {
            if (!card.IsMinion || (!card.IsAlive && !context.DeferDeaths))
            {
                continue;
            }

            if (!ignoreWard && HasWard(card))
            {
                continue;
            }

            if (card.IsLeaderEntity && !CanAffectLeader(card, spec, context))
            {
                continue;
            }

            result.Add(card);
        }

        return result;
    }

    private static bool HasWard(CardInstance card)
    {
        return card.HasKeyword(WardKeyword) || card.HasKeyword("WARD");
    }

    private static bool CanAffectLeader(CardInstance leader, EffectSpec spec, EffectContext context)
    {
        var sourceCanSlay = spec.KingSlayer ?? context.SourceCard?.Definition.KingSlayer ?? false;
        if (!sourceCanSlay)
        {
            return false;
        }

        foreach (var vulnerability in leader.Definition.Vulnerabilities)
        {
            if (string.Equals(vulnerability, spec.Action, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
}
