using System;
using System.Linq;
using System.Reflection;
using DominionWars.Engine.Rules;

namespace PlCsim
{
    /// <summary>
    /// Builds the <see cref="MatchRules"/> the harness hands to MatchSetup, so a
    /// run can set the ENGINE's T1 rule
    /// (<c>MatchRules.MaxPunishResponsesPerRound</c>) rather than only emulating
    /// it in the policy.
    ///
    /// The pre-P0 engine copy has no such parameter, so everything here goes
    /// through reflection and the project compiles against either engine. When
    /// the engine exposes the parameter the harness supplies the value
    /// explicitly; asking for a non-zero cap on an engine that lacks the
    /// parameter fails loudly instead of silently measuring an uncapped match -
    /// the same contract the pioneer-bonus switch uses.
    ///
    /// This file deliberately lives in the harness only: nothing under src\ is
    /// read or written, and no balance file is loaded. The harness supplies
    /// every MatchRules value itself, so each report must name the cap it used.
    /// </summary>
    internal static class MatchRulesFactory
    {
        public static bool SupportsEngineCap { get; private set; }
        public static bool SupportsPioneerBonus { get; private set; }

        /// <summary>The cap the constructed MatchRules actually carries, read back from the property.</summary>
        public static int? EffectiveEngineCap { get; private set; }

        public static int? EffectivePioneerBonus { get; private set; }

        public static string Describe { get; private set; } = "unresolved";

        public static MatchRules Create(SimOptions options)
        {
            var constructor = SelectConstructor(out var parameterNames);
            var parameters = constructor.GetParameters();
            var arguments = new object?[parameters.Length];
            var assignment = new string[parameters.Length];
            var hasCapParameter = false;

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (string.Equals(parameter.Name, "maxPunishResponsesPerRound", StringComparison.Ordinal))
                {
                    hasCapParameter = true;
                    arguments[i] = options.MaxPunishResponsesPerRound;
                    assignment[i] = $"maxPunishResponsesPerRound={arguments[i]}";
                    continue;
                }

                arguments[i] = DefaultOf(parameter);
                assignment[i] = $"{parameter.Name}={arguments[i]}";
            }

            var rules = (MatchRules)constructor.Invoke(arguments);
            var engineCap = ReadIntProperty(rules, "MaxPunishResponsesPerRound");
            var engineBonus = ReadIntProperty(rules, "PioneerOpponentPunishBonus");

            if (!hasCapParameter && options.MaxPunishResponsesPerRound != 0)
            {
                throw new NotSupportedException(
                    "--max-punish-responses-per-round " + options.MaxPunishResponsesPerRound +
                    " was requested but this engine's MatchRules constructor (" +
                    string.Join(", ", parameterNames) + ") has no maxPunishResponsesPerRound parameter. " +
                    "The rule cannot be expressed against this engine copy, and it must not be approximated " +
                    "by the policy-level --max-responses-per-round flag without saying so.");
            }

            if (hasCapParameter && !engineCap.HasValue)
            {
                throw new NotSupportedException(
                    "The engine's MatchRules constructor accepts maxPunishResponsesPerRound but exposes no " +
                    "readable MaxPunishResponsesPerRound property, so the applied cap cannot be verified.");
            }

            SupportsEngineCap = hasCapParameter && engineCap.HasValue;
            SupportsPioneerBonus = engineBonus.HasValue;
            EffectiveEngineCap = engineCap;
            EffectivePioneerBonus = engineBonus;
            Describe = $"ctor({string.Join(", ", assignment)})" +
                       $"; MaxPunishResponsesPerRound={(engineCap.HasValue ? engineCap.Value.ToString() : "absent")}" +
                       $"; PioneerOpponentPunishBonus={(engineBonus.HasValue ? engineBonus.Value.ToString() : "absent")}";
            return rules;
        }

        private static object? DefaultOf(ParameterInfo parameter)
        {
            if (parameter.HasDefaultValue && parameter.DefaultValue is not null)
            {
                return parameter.DefaultValue;
            }

            return parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
        }

        /// <summary>
        /// Picks the widest public constructor. The P0 engine also exposes
        /// shorter overloads, and the shortest one would silently drop the
        /// pioneer hand-limit bonus this harness has always used.
        /// </summary>
        private static ConstructorInfo SelectConstructor(out string[] parameterNames)
        {
            var constructors = typeof(MatchRules)
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(c => c.GetParameters().Length)
                .ToArray();
            if (constructors.Length == 0)
            {
                throw new NotSupportedException("MatchRules exposes no public constructor in this engine copy.");
            }

            var selected = constructors.Last();
            parameterNames = selected.GetParameters().Select(p => p.Name ?? "?").ToArray();
            return selected;
        }

        private static int? ReadIntProperty(MatchRules rules, string name)
        {
            var property = typeof(MatchRules).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property is null || property.PropertyType != typeof(int)) return null;
            return (int)property.GetValue(rules)!;
        }
    }
}
