using System;
using System.Collections.Generic;
using DominionWars.Engine.Turns;

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
            new EnfeebleEffect(),
            new BanishEffect(),
            new ControlEffect(),
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
            new AddRootEffect(),
            new AddRampantEffect(),
            new CommitEffect(),
            new PushEffect(),
            new PullEffect(),
            new RollbackEffect(),
        });
    }

    public void Apply(EffectSpec spec, EffectContext context)
    {
        ApplyInternal(spec, context, checkAll: true);
    }

    private void ApplyInternal(EffectSpec spec, EffectContext context, bool checkAll)
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

        // A card-authored condition is evaluated with the single shared
        // condition grammar before the effect resolves. An unsatisfied or
        // unknown condition skips the effect with an auditable reason instead
        // of silently resolving it unconditionally.
        if (!PunishConditionEvaluator.IsSatisfied(
                Runtime.State,
                context.SourcePlayerIndex,
                spec.Condition))
        {
            Runtime.EmitSkipped(context, spec.Action, "effect.condition_not_met");
            return;
        }

        effect.Apply(spec, context, this);
        if (checkAll)
        {
            Runtime.CheckAll(context);
        }
    }

    public void ApplyAll(IEnumerable<EffectSpec> specs, EffectContext context)
    {
        if (specs is null)
        {
            throw new ArgumentNullException(nameof(specs));
        }

        // Preserve any enclosing batch's deferral: a nested chain (for example a
        // passive hook window, EffectRuntime.Cards.cs) must not resolve deaths
        // while its parent batch is still applying, or the parent's remaining
        // parts would silently lose targets that already died in this batch.
        // The outermost ApplyAll still restores false and resolves exactly once.
        var previousDefer = context.DeferDeaths;
        context.DeferDeaths = true;
        try
        {
            foreach (var spec in specs)
            {
                ApplyInternal(spec, context, checkAll: false);
                if (context.Negated)
                {
                    break;
                }
            }
        }
        finally
        {
            context.DeferDeaths = previousDefer;
            Runtime.CheckAll(context);
        }
    }
}
}
