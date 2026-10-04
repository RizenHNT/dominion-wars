using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DominionWars.Data;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;

namespace PlCsim
{
    /// <summary>
    /// In-memory card-library ablations.
    ///
    /// The engine contract is untouched: the harness only builds a different
    /// <see cref="CardCatalog"/> (same ids, same counts, one property changed)
    /// and feeds it to MatchSetup. Nothing under data/ is read differently or
    /// written; the deck JSON files and their card ids stay exactly as shipped.
    /// </summary>
    internal static class CatalogVariant
    {
        public const string Shipped = "shipped";
        public const string WoodNoRampant = "wood-no-rampant";
        public const string SeaNoLeaderPunish = "sea-no-leader-punish";
        public const string SeaNonActivatable = "sea-non-activatable";
        public const string FlameNonActivatable = "flame-non-activatable";
        public const string MachineNonActivatable = "machine-non-activatable";
        public const string WoodNonActivatable = "wood-non-activatable";

        /// <summary>
        /// The P'=0 tier test. Measured punishCost tiers in the shipped sea deck
        /// (12 activatable kinds x 3 copies = 36 copies), read from
        /// data\cards\sea.json:
        ///
        ///   punishCost 0 - 6 kinds, 18 copies - sea_crab (HEAL 2), sea_eel (BUFF +1/+0),
        ///                 sea_kraken (NO punish effects), sea_priest (HEAL 2),
        ///                 sea_tentacle (BUFF +1/+1), sea_tide (DISCARD_OPP_RANDOM 1)
        ///   punishCost 1 - 4 kinds, 12 copies - sea_abyss_call (SUMMON), sea_devour
        ///                 (DISCARD_OPP_RANDOM 1), sea_leviathan_young (DRAW 1),
        ///                 sea_pressure (DAMAGE 2)
        ///   punishCost 2 - 2 kinds,  6 copies - sea_punish_tsunami (DAMAGE 5),
        ///                 sea_sink (DESTROY)
        ///
        /// NOTE: the four-kind P'=0 set proposed by the planning lead omitted
        /// sea_priest and sea_eel and included sea_kraken, which carries no
        /// punish effects at all. Both readings are provided: the strict
        /// four-kind set and the complete punishCost==0 tier.
        /// </summary>
        public const string SeaClearP0Strict = "sea-clear-p0";          // 4 kinds / 12 copies (lead's list)
        public const string SeaClearP0Tier = "sea-clear-p0-tier";       // 6 kinds / 18 copies (real tier)
        public const string SeaOnlyP0Tier = "sea-only-p0-tier";         // inverse: only the 18 free copies stay
        public const string SeaClearZeroAndOne = "sea-clear-p0-p1";     // only P'=2 survives (6 copies)
        public const string SeaOnlyTwo = "sea-only-p2";                 // same target, stated as "keep P'=2"
        public const string SeaClearZero = "sea-clear-zero-cost";       // P'=0 and P'=1 survive (30 copies)
        public const string SeaOnlyOne = "sea-only-p1";                 // completes the 2^3 tier design (12 copies)
        /// <summary>Control: sea's leader keeps every effect but loses its GrantLife 25.</summary>
        public const string SeaNoGrantLife = "sea-no-grantlife";
        /// <summary>Control: every sea card's printed punish (what the opponent draws) halved.</summary>
        public const string SeaPunishCurveCheap = "sea-punish-curve-half";
        /// <summary>Control: every sea card costs the opponent nothing (punish 0).</summary>
        public const string SeaPunishCurveFree = "sea-punish-curve-zero";

        private static readonly string[] SeaP0Strict =
        {
            "sea_crab", "sea_tide", "sea_kraken", "sea_tentacle",
        };

        private static readonly string[] SeaP0Tier =
        {
            "sea_crab", "sea_eel", "sea_kraken", "sea_priest", "sea_tentacle", "sea_tide",
        };

        private static readonly string[] SeaP1Tier =
        {
            "sea_abyss_call", "sea_devour", "sea_leviathan_young", "sea_pressure",
        };

        private static readonly string[] SeaP2Tier =
        {
            "sea_punish_tsunami", "sea_sink",
        };

        /// <summary>Kinds whose punishActivatable flag each P'-tier variant clears.</summary>
        private static readonly Dictionary<string, HashSet<string>> SeaPunishCostClearedKinds =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                // keep P'=1 and P'=2 activatable (any surviving activatable kind)
                [SeaClearP0Strict] = new HashSet<string>(SeaP0Strict, StringComparer.Ordinal),
                // keep P'=1 and P'=2 activatable
                [SeaClearP0Tier] = new HashSet<string>(SeaP0Tier, StringComparer.Ordinal),
                // keep ONLY P'=0 activatable
                [SeaOnlyP0Tier] = new HashSet<string>(SeaP1Tier.Concat(SeaP2Tier), StringComparer.Ordinal),
                // keep only P'=2 activatable
                [SeaClearZeroAndOne] = new HashSet<string>(SeaP0Tier.Concat(SeaP1Tier), StringComparer.Ordinal),
                [SeaOnlyTwo] = new HashSet<string>(SeaP0Tier.Concat(SeaP1Tier), StringComparer.Ordinal),
                // keep P'=0 and P'=1 activatable
                [SeaClearZero] = new HashSet<string>(SeaP2Tier, StringComparer.Ordinal),
                // keep only P'=1 activatable (completes the 2^3 design)
                [SeaOnlyOne] = new HashSet<string>(SeaP0Tier.Concat(SeaP2Tier), StringComparer.Ordinal),
            };

        /// <summary>
        /// S1 emulation: the sea deck with punishActivated density reduced to
        /// 30% (18 of 60 copies) or 20% (12 of 60).
        ///
        /// Cleared kinds are taken most-expensive-activation first, ranked by
        /// the PunishCost of the punish effect the copy would fire; the flag is
        /// cleared on whole kinds, i.e. 3 copies each.
        /// </summary>
        public const string SeaActivatable30 = "sea-activatable-30pct";
        public const string SeaActivatable20 = "sea-activatable-20pct";

        private static readonly Dictionary<string, HashSet<string>> SeaClearedKinds =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                // 36 -> 18 copies (30%): 6 kinds x 3 copies cleared.
                [SeaActivatable30] = new HashSet<string>(StringComparer.Ordinal)
                {
                    "sea_leviathan_young", "sea_devour", "sea_punish_tsunami",
                    "sea_sink", "sea_pressure", "sea_abyss_call",
                },
                // 36 -> 12 copies (20%): 8 kinds x 3 copies cleared.
                [SeaActivatable20] = new HashSet<string>(StringComparer.Ordinal)
                {
                    "sea_leviathan_young", "sea_devour", "sea_punish_tsunami",
                    "sea_sink", "sea_pressure", "sea_abyss_call",
                    "sea_priest", "sea_eel",
                },
            };

        /// <summary>
        /// Machine weakness levers. The machine win axis is PULL_TOTAL_GE = 6,
        /// and both COMMIT and PULL route through
        /// PlayCardActionHandler.ResolveLifecyclePunish (amount drawn by the
        /// OPPONENT, then a response window at chainDepth 1), so the shipped
        /// deck's commitCost/downloadCost are a card tax paid to the opponent.
        /// </summary>
        public const string MachineDownloadFree = "machine-download-free";
        public const string MachineCommitFree = "machine-commit-free";
        public const string MachinePull5 = "machine-pull-5";
        public const string MachineDownloadFreePull5 = "machine-download-free-pull-5";

        private static bool IsMachineCard(CardDefinition d) =>
            string.Equals(d.Faction, "机械遗迹", StringComparison.Ordinal);

        /// <summary>The card ids whose punishActivatable flag a variant clears, for the report.</summary>
        public static string ClearedKindsFor(string variant)        {
            if (SeaPunishCostClearedKinds.TryGetValue(variant, out var tierKinds))
            {
                return string.Join(",", tierKinds.OrderBy(id => id, StringComparer.Ordinal));
            }

            return SeaClearedKinds.TryGetValue(variant, out var kinds)
                ? string.Join(",", kinds.OrderBy(id => id, StringComparer.Ordinal))
                : string.Empty;
        }

        /// <summary>Copies left punishActivatable by a variant (3 copies per kind).</summary>
        public static int ActivatableCopiesRemaining(string variant)
        {
            var cleared = 0;
            if (SeaPunishCostClearedKinds.TryGetValue(variant, out var tierKinds)) cleared = tierKinds.Count;
            else if (SeaClearedKinds.TryGetValue(variant, out var kinds)) cleared = kinds.Count;
            else return 36;
            return (12 - cleared) * 3;
        }


        public static CardCatalog Build(CardCatalog source, string variant)
        {
            if (string.Equals(variant, Shipped, StringComparison.Ordinal))
            {
                return source;
            }

            var cards = new Dictionary<string, CardDefinition>(StringComparer.Ordinal);
            foreach (var pair in source.Cards)
            {
                cards[pair.Key] = Transform(pair.Value, variant) ?? pair.Value;
            }

            return new CardCatalog(cards);
        }

        private static CardDefinition? Transform(CardDefinition definition, string variant)
        {
            switch (variant)
            {
                case WoodNoRampant:
                    // Ablation: the wood leader no longer supplies 疯长, so
                    // PlayerState.RampantStacks stays 0 and ApplyGrowth's
                    // multiplier (1 << rampant) stays 1 -> ordinary wood BUFFs
                    // are no longer amplified and never trigger the seal.
                    if (definition.Id == "wood_leader")
                    {
                        var trimmed = new List<EffectSpec>();
                        foreach (var effect in definition.LeaderEnterEffects)
                        {
                            if (string.Equals(effect.Action, EffectNames.AddRampant, StringComparison.Ordinal)) continue;
                            trimmed.Add(effect);
                        }

                        if (trimmed.Count == definition.LeaderEnterEffects.Count) return null;
                        return Clone(definition, leaderEnterEffects: trimmed);
                    }

                    return null;

                case SeaNoLeaderPunish:
                    // Ablation: sea's leader loses its entire punish-effect
                    // package, of which CONVERT_PUNISH_TO_DISCARD is one entry.
                    if (definition.Id != "sea_leader") return null;
                    return Clone(definition, leaderPunishEffects: Array.Empty<EffectSpec>());

                case SeaNonActivatable:
                    // Ablation: no sea card ever arrives punish-activated, so
                    // no sea card can ever answer inside a punish chain.
                    if (!string.Equals(definition.Faction, "深海联盟", StringComparison.Ordinal)) return null;
                    return Clone(definition, punishActivatable: false);

                case FlameNonActivatable:
                    // Control for SeaNonActivatable: the SAME flag is cleared on
                    // a different faction, so any win-rate movement cannot be
                    // attributed to sea losing a property of its own deck.
                    if (!string.Equals(definition.Faction, "烈焰帝国", StringComparison.Ordinal)) return null;
                    return Clone(definition, punishActivatable: false);

                case MachineNonActivatable:
                    if (!string.Equals(definition.Faction, "机械遗迹", StringComparison.Ordinal)) return null;
                    return Clone(definition, punishActivatable: false);

                case WoodNonActivatable:
                    if (!string.Equals(definition.Faction, "古木圣地", StringComparison.Ordinal)) return null;
                    return Clone(definition, punishActivatable: false);

                case SeaActivatable30:
                case SeaActivatable20:                    // S1: clear the flag only on the selected kinds.
                    if (!SeaClearedKinds[variant].Contains(definition.Id)) return null;
                    return Clone(definition, punishActivatable: false);

                case SeaClearP0Strict:
                case SeaClearP0Tier:
                case SeaOnlyP0Tier:
                case SeaClearZeroAndOne:
                case SeaOnlyTwo:
                case SeaClearZero:
                case SeaOnlyOne:
                    // P'-tier isolation: clear the flag only on the selected kinds.
                    if (!SeaPunishCostClearedKinds[variant].Contains(definition.Id)) return null;
                    return Clone(definition, punishActivatable: false);

                case SeaNoGrantLife:
                    // Control for "maybe it is the 25 life, not the flag".
                    if (definition.Id != "sea_leader") return null;
                    return Clone(definition, grantLife: 0);

                case SeaPunishCurveCheap:
                    // Control for "maybe it is sea's deck punish curve".
                    if (!string.Equals(definition.Faction, "深海联盟", StringComparison.Ordinal)
                        || definition.IsLeader)
                    {
                        return null;
                    }

                    return Clone(definition, punish: definition.Punish / 2);

                case SeaPunishCurveFree:
                    if (!string.Equals(definition.Faction, "深海联盟", StringComparison.Ordinal)
                        || definition.IsLeader)
                    {
                        return null;
                    }

                    return Clone(definition, punish: 0);

                case MachineDownloadFree:
                    // Remove the pull half of the tax: the opponent no longer
                    // draws when machine downloads.
                    if (!IsMachineCard(definition)) return null;
                    return Clone(definition, downloadCost: 0);

                case MachineCommitFree:
                    // Remove the commit half of the tax.
                    if (!IsMachineCard(definition)) return null;
                    return Clone(definition, commitCost: 0);

                case MachinePull5:
                    // Shorten the win axis by one step on both of machine's
                    // leader cards (the landmark carrier and the summoned alpha).
                    if (definition.Id != "machine_leader" && definition.Id != "machine_alpha") return null;
                    return Clone(definition, leaderWinParam: 5);

                case MachineDownloadFreePull5:
                    if (IsMachineCard(definition) && !definition.IsLeader)
                    {
                        return Clone(definition, downloadCost: 0);
                    }

                    if (definition.Id == "machine_leader" || definition.Id == "machine_alpha")
                    {
                        return Clone(definition, leaderWinParam: 5);
                    }

                    return null;

                default:
                    throw new ArgumentException("unknown ablation variant '" + variant + "'");
            }
        }

        private static CardDefinition Clone(
            CardDefinition d,
            IEnumerable<EffectSpec>? leaderEnterEffects = null,
            IEnumerable<EffectSpec>? leaderPunishEffects = null,
            bool? punishActivatable = null,
            int? grantLife = null,
            int? punish = null,
            int? downloadCost = null,
            int? commitCost = null,
            int? leaderWinParam = null)
        {
            return new CardDefinition(
                id: d.Id,
                name: d.Name,
                attack: d.Attack,
                health: d.Health,
                isMinion: d.IsMinion,
                isLeader: d.IsLeader,
                grantLife: grantLife ?? d.GrantLife,
                kingSlayer: d.KingSlayer,
                keywords: d.Keywords,
                vulnerabilities: d.Vulnerabilities,
                faction: d.Faction,
                text: d.Text,
                flavor: d.Flavor,
                cost: d.Cost,
                rarity: d.Rarity,
                artId: d.ArtId,
                tags: d.Tags,
                punishActivatable: punishActivatable ?? d.PunishActivatable,
                punishCost: d.PunishCost,
                hasLeaderAbility: d.HasLeaderAbility,
                type: d.Type,
                punish: punish ?? d.Punish,
                punishCondition: d.PunishCondition,
                onPlayEffects: d.OnPlayEffects,
                punishEffects: d.PunishEffects,
                ambushKind: d.AmbushKind,
                ambushTrigger: d.AmbushTrigger,
                ambushEffects: d.AmbushEffects,
                chant: d.Chant,
                chantEffects: d.ChantEffects,
                attacksPerTurn: d.AttacksPerTurn,
                onOpponentDiscardEffects: d.OnOpponentDiscardEffects,
                guard: d.Guard,
                leaderEnterEffects: leaderEnterEffects ?? d.LeaderEnterEffects,
                leaderPunishEffects: leaderPunishEffects ?? d.LeaderPunishEffects,
                leaderWinCondition: d.LeaderWinCondition,
                leaderWinText: d.LeaderWinText,
                leaderDurability: d.LeaderDurability,
                leaderWinParam: leaderWinParam ?? d.LeaderWinParam,
                commitCost: commitCost ?? d.CommitCost,
                uploadCost: d.UploadCost,
                downloadCost: downloadCost ?? d.DownloadCost,
                commitEffects: d.CommitEffects,
                pushEffects: d.PushEffects,
                pullEffects: d.PullEffects,
                isLandmark: d.IsLandmark,
                landmarkTiers: d.LandmarkTiers);
        }
    }
}
