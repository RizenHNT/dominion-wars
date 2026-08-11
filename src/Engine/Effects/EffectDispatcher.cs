using System;
using System.Collections.Generic;

namespace DominionWars.Engine.Effects
{

public sealed class EffectDispatcher : IEffectDispatcher
{
    private readonly IReadOnlyDictionary<string, IEffect> _effects;

    public EffectDispatcher(EffectRuntime runtime, IEnumerable<IEffect> effects)
    {
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        if (effects is null)
        {
            throw new ArgumentNullException(nameof(effects));
        }

        var lookup = new Dictionary<string, IEffect>(StringComparer.Ordinal);
        foreach (var effect in effects)
        {
            if (!lookup.TryAdd(effect.ActionName, effect))
            {
                throw new ArgumentException($"Duplicate effect action '{effect.ActionName}'.", nameof(effects));
            }
        }

        _effects = lookup;
    }

    public EffectRuntime Runtime { get; }

    public IReadOnlyCollection<string> RegisteredActions => (IReadOnlyCollection<string>)_effects.Keys;

    public static EffectDispatcher CreateDefault(EffectRuntime runtime)
    {
        return new EffectDispatcher(runtime, new IEffect[]
        {
            new DamageEffect(),
            new HealEffect(),
            new DrawEffect(),
            new OppDrawEffect(),
            new DiscardOppRandomEffect(),
            new DiscardDrawnEffect(),
            new DestroyEffect(),
            new BuffEffect(),
            new GrantKeywordEffect(),
            new SummonEffect(),
            new SummonLeaderEffect(),
            new EndTurnEffect(),
            new AddOppPunishTurnEffect(),
            new AddSelfPunishTurnEffect(),
            new ConvertPunishToDiscardEffect(),
            new ProtectTurnEffect(),
            new NegateEffect(),
            new NegateEnemyEffectsTurnEffect(),
            new SkipReshuffleEffect(),
            new RestoreAttacksEffect(),
            new GainLifeEffect(),
            new LoseLifeEffect(),
            new DamageCastleEffect(),
            new WinGameEffect(),
        });
    }

    public void Apply(EffectSpec spec, EffectContext context)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (!Runtime.State.Events.IsRootEvent(context.RootEventId))
        {
            throw new InvalidOperationException(
                $"Effect context root event '{context.RootEventId}' does not exist or is not a root event.");
        }

        if (!_effects.TryGetValue(spec.Action, out var effect))
        {
            throw new UnknownActionException(spec.Action);
        }

        if (Runtime.IsGameOver)
        {
            Runtime.EmitSkipped(context, spec.Action, "game.already_over");
            return;
        }

        if (Runtime.IsSourceEffectNegated(context))
        {
            Runtime.EmitSkipped(context, spec.Action, "effect.source_negated");
            return;
        }

        if (context.Negated && spec.Action != EffectNames.Negate)
        {
            if (!context.NegationObserved)
            {
                Runtime.EmitNegated(context);
                context.NegationObserved = true;
            }

            return;
        }

        effect.Apply(spec, context, this);
        Runtime.CheckAll(context);
    }

    public void ApplyAll(IEnumerable<EffectSpec> specs, EffectContext context)
    {
        if (specs is null)
        {
            throw new ArgumentNullException(nameof(specs));
        }

        foreach (var spec in specs)
        {
            Apply(spec, context);
            if (context.Negated)
            {
                break;
            }
        }
    }
}
}
