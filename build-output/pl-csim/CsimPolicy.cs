using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DominionWars.Engine;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;

namespace PlCsim
{
    /// <summary>
    /// Heuristic C# opponent policy for the simulation harness.
    ///
    /// It never invents actions: every choice is one of the actions the real
    /// LegalActionGenerator advertised for the current phase. It is a policy,
    /// not a rules authority, and it is deliberately simple so its bias is
    /// inspectable. It is NOT part of the shipped engine.
    /// </summary>
    internal sealed class CsimPolicy
    {
        private const string WoodFaction = "古木圣地";
        private const string MachineFaction = "机械遗迹";

        private int _turn = -1;
        private int _punishSpent;
        private int _cardsPlayed;
        private readonly HashSet<long> _committedThisTurn = new HashSet<long>();

        /// <summary>Cumulative punish budget per turn, mirroring a cautious player.</summary>
        public int PunishBudget(GameState state, PlayerState self)
        {
            var opponent = state.GetOpponent(self.PlayerIndex);
            var budget = 4 + state.Turn.Number / 4;
            if (opponent.Hand.Count >= 7) budget -= 2;
            return Math.Max(0, budget);
        }

        /// <summary>
        /// Alternative budget used for the policy-sensitivity run: it spends
        /// punish far more freely, which is the main strategic axis in this
        /// game (punish is the opponent's card advantage).
        /// </summary>
        public bool Aggressive { get; set; }

        /// <summary>
        /// Ablation switch: the named faction never plays a card carrying BUFF
        /// effects (it attacks with plain minions instead). Null = shipped
        /// behaviour for both factions.
        /// </summary>
        public string? NoBuffPlayForFaction { get; set; }

        /// <summary>
        /// Reporting hook. true = an advertised punish-activated play was
        /// rejected by the punish budget; false = one was taken.
        /// </summary>
        public Action<bool>? OnPunishActivation { get; set; }

        private int EffectiveBudget(GameState state, PlayerState self)
        {
            if (!Aggressive) return PunishBudget(state, self);
            var budget = 10 + state.Turn.Number / 2;
            if (state.GetOpponent(self.PlayerIndex).Hand.Count >= 7) budget -= 2;
            return Math.Max(0, budget);
        }

        public void NoteSubmission(LegalAction action, MatchActionResult submission)
        {
            if (!submission.Accepted) return;
            NoteAccepted(action);
        }

        /// <summary>
        /// Bookkeeping for an accepted action, callable when the caller only
        /// knows acceptance (the adapter gateway path returns a boolean rather
        /// than the harness's MatchActionResult).
        /// </summary>
        public void NoteAccepted(LegalAction action)
        {
            if (action.Type == LegalActionGenerator.PlayCard)
            {
                _cardsPlayed++;
                _punishSpent += PunishOf(action);
            }
            else if (action.Type == LegalActionGenerator.Commit && action.SourceId.HasValue)
            {
                _committedThisTurn.Add(action.SourceId.Value);
            }
        }

        public LegalAction? ChooseAmbush(GameState state, IReadOnlyList<LegalAction> actions)
        {
            LegalAction? best = null;
            var bestRank = -1;
            LegalAction? skip = null;
            foreach (var action in actions)
            {
                if (action.Type == TurnAction.SkipAmbush)
                {
                    skip ??= action;
                    continue;
                }

                var card = Find(state, action.SourceId);
                if (card is null) continue;
                var rank = AmbushRank(card.Definition) - PunishOf(action);
                if (rank > bestRank)
                {
                    bestRank = rank;
                    best = action;
                }
            }

            // Setting an ambush does not advance the phase by itself, so the
            // harness submits the chosen SET_AMBUSH and then SKIP_AMBUSH.
            return bestRank >= 0 ? best : skip;
        }

        private static int AmbushRank(CardDefinition definition)
        {
            var baseRank = definition.AmbushKind switch
            {
                "LOCKDOWN" => 30,
                "FOCUS" => 20,
                "NORMAL" => 10,
                _ => 10,
            };
            return baseRank + definition.AmbushEffects.Count;
        }

        public IReadOnlyList<long> ChooseDiscards(GameState state, int actor, IReadOnlyList<long> candidates, int required)
        {
            if (required <= 0 || candidates.Count == 0) return Array.Empty<long>();
            var player = state.GetPlayer(actor);
            var scored = new List<(long Id, int Value)>();
            foreach (var id in candidates)
            {
                CardInstance? card = null;
                foreach (var handCard in player.Hand)
                {
                    if (handCard.InstanceId == id) { card = handCard; break; }
                }

                scored.Add((id, card is null ? int.MaxValue / 2 : HandValue(card)));
            }

            return scored.OrderBy(pair => pair.Value).Take(required).Select(pair => pair.Id).ToArray();
        }

        private static int HandValue(CardInstance card)
        {
            var definition = card.Definition;
            var value = definition.Punish * 2;
            if (definition.IsMinion) value += definition.Attack + definition.Health;
            value += definition.OnPlayEffects.Count * 2 + definition.PunishEffects.Count;
            if (string.Equals(definition.Type, "PUNISH", StringComparison.Ordinal) && !card.PunishActivated) value -= 100;
            return value;
        }

        public LegalAction? ChooseAction(GameState state, IReadOnlyList<LegalAction> actions)
        {
            var actor = state.CurrentPlayerIndex;
            var self = state.GetPlayer(actor);
            var opponent = state.GetOpponent(actor);
            if (_turn != state.Turn.Number)
            {
                _turn = state.Turn.Number;
                _punishSpent = 0;
                _cardsPlayed = 0;
                _committedThisTurn.Clear();
            }

            var budget = EffectiveBudget(state, self);
            LegalAction? best = null;
            var bestScore = double.NegativeInfinity;
            LegalAction? endTurn = null;

            foreach (var action in actions)
            {
                if (action.Type == LegalActionGenerator.EndTurn)
                {
                    endTurn ??= action;
                    continue;
                }

                double score;
                switch (action.Type)
                {
                    case LegalActionGenerator.PlayCard: score = ScorePlay(state, self, opponent, action, budget); break;
                    case LegalActionGenerator.Pull: score = ScorePull(state, self, action); break;
                    case LegalActionGenerator.Commit: score = ScoreCommit(state, self, action); break;
                    case LegalActionGenerator.Attack: score = ScoreAttack(state, self, action); break;
                    default: continue;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = action;
                }
            }

            // A play must beat the "hold" value; attacks/pulls/commits only need
            // to beat doing nothing (score > 0).
            if (best is not null && bestScore > 0.5)
            {
                if (best.Type == LegalActionGenerator.PlayCard && best.SourceId.HasValue)
                {
                    var chosen = Find(state, best.SourceId.Value);
                    if (chosen is not null && chosen.PunishActivated) OnPunishActivation?.Invoke(false);
                }

                return best;
            }

            return endTurn ?? best;
        }

        private static bool HasBuff(IReadOnlyList<EffectSpec> effects)
        {
            foreach (var effect in effects)
            {
                if (effect is not null && string.Equals(effect.Action, EffectNames.Buff, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Leader faction, falling back to the faction of any owned leader card.</summary>
        public static string FactionOf(PlayerState player)
        {
            var leader = player.Leader;
            if (leader is not null) return leader.Definition.Faction;
            foreach (var zone in new[] { player.Deck, player.Hand, player.Field, player.LeaderZone, player.Graveyard })
            {
                foreach (var card in zone)
                {
                    if (card.Definition.IsLeader) return card.Definition.Faction;
                }
            }

            return "unknown";
        }

        private double ScorePlay(GameState state, PlayerState self, PlayerState opponent, LegalAction action, int budget)
        {
            if (!action.SourceId.HasValue) return double.NegativeInfinity;
            var card = Find(state, action.SourceId.Value);
            if (card is null) return double.NegativeInfinity;

            var cost = PunishOf(action);
            var fizzle = cost > opponent.Deck.Count;
            if (!fizzle && cost > 0)
            {
                if (_cardsPlayed >= 3)
                {
                    if (card.PunishActivated) OnPunishActivation?.Invoke(true);
                    return double.NegativeInfinity;
                }

                if (_punishSpent + cost > budget)
                {
                    if (card.PunishActivated) OnPunishActivation?.Invoke(true);
                    return double.NegativeInfinity;
                }
            }
            else if (_cardsPlayed >= 6)
            {
                // Free plays are still bounded; a hand-cycling loop between
                // zero-cost plays and effects that return cards must not run
                // unbounded inside one turn.
                return double.NegativeInfinity;
            }

            var effects = card.PunishActivated ? card.Definition.PunishEffects : card.Definition.OnPlayEffects;

            // Ablation switch: the named faction refuses BUFF-bearing cards.
            if (NoBuffPlayForFaction is not null
                && string.Equals(FactionOf(self), NoBuffPlayForFaction, StringComparison.Ordinal)
                && HasBuff(effects))
            {
                return double.NegativeInfinity;
            }

            var score = EffectValue(state, self, opponent, card, effects);
            if (card.Definition.IsMinion)
            {
                score += card.Definition.Attack + card.Definition.Health +
                         (card.Definition.AttacksPerTurn - 1) * 3;
            }

            score -= cost * (fizzle ? 0.1 : 1.0);
            if (card.PunishActivated) score += 1.5;
            if (self.Hand.Count <= 1) score += 1.0;
            return score;
        }

        private double ScorePull(GameState state, PlayerState self, LegalAction action)
        {
            if (!action.TargetId.HasValue) return double.NegativeInfinity;
            var top = state.FindEntity(action.TargetId.Value);
            var source = action.SourceId.HasValue ? state.FindEntity(action.SourceId.Value) : null;
            if (top is null) return double.NegativeInfinity;

            var score = 4.0;
            score += EffectValue(state, self, state.GetOpponent(self.PlayerIndex), source, top.Definition.PullEffects);
            var cost = PunishOf(action);
            score -= cost * 0.5;
            score += 6.0 - Math.Min(6, self.PullCount) * 0.5;
            if (string.Equals(top.Definition.Faction, MachineFaction, StringComparison.Ordinal)) score += 1.0;
            return score;
        }

        private double ScoreCommit(GameState state, PlayerState self, LegalAction action)
        {
            if (!action.SourceId.HasValue) return double.NegativeInfinity;
            if (_committedThisTurn.Contains(action.SourceId.Value)) return double.NegativeInfinity;
            var card = state.FindEntity(action.SourceId.Value);
            if (card is null) return double.NegativeInfinity;

            var score = 5.0 - (card.Attack + card.Health) * 0.25;
            score += EffectValue(state, self, state.GetOpponent(self.PlayerIndex), card, card.Definition.CommitEffects);
            var cost = PunishOf(action);
            score -= cost * 0.6;
            if (self.CommitQueue.Count > 3) score -= 2.0;
            return score;
        }

        private double ScoreAttack(GameState state, PlayerState self, LegalAction action)
        {
            if (!action.SourceId.HasValue) return double.NegativeInfinity;
            var attacker = state.FindEntity(action.SourceId.Value);
            if (attacker is null) return double.NegativeInfinity;

            if (!action.TargetId.HasValue)
            {
                // Face attack: the engine advertises core damage through
                // TARGET_LEADER / TARGET_LIFE / TARGET_CASTLE style ids via
                // TargetReferenceId; without an entity target this is a core hit.
                return 2.0 + attacker.Attack * 0.6;
            }

            var target = state.FindEntity(action.TargetId.Value);
            if (target is null) return 2.0 + attacker.Attack * 0.4;

            var lethal = attacker.Attack >= target.Health || target.Shield;
            var threat = Threat(target);
            var score = lethal ? threat * 0.9 + 3.0 : threat * 0.4;
            if (target.IsLeaderEntity) score += 6.0;
            if (!lethal && target.Attack >= attacker.Health && attacker.IsLeaderEntity) score -= 8.0;
            if (target.Shield) score -= 2.0;
            if (self.Leader is not null && self.HasMultipleActiveLeaders) score -= 4.0;
            return score;
        }

        private double EffectValue(
            GameState state,
            PlayerState self,
            PlayerState opponent,
            CardInstance? source,
            IReadOnlyList<EffectSpec> effects)
        {
            var score = 0.0;
            var opponentMinions = LivingMinions(opponent);
            foreach (var effect in effects)
            {
                if (effect is null) continue;
                var amount = effect.Amount;
                switch (effect.Action)
                {
                    case EffectNames.Damage:
                        score += DamageValue(effect, amount, opponentMinions.Count);
                        break;
                    case EffectNames.Destroy:
                    case EffectNames.Banish:
                        score += opponentMinions.Count == 0 ? 0 : 8.0 + opponentMinions.Max(Threat) * 0.3;
                        break;
                    case EffectNames.Heal:
                        score += amount * 0.7;
                        break;
                    case EffectNames.Draw:
                        score += 3.5 + amount * 0.5;
                        break;
                    case EffectNames.OppDraw:
                        score += amount * 0.5;
                        break;
                    case EffectNames.DiscardOppRandom:
                    case EffectNames.DiscardDrawn:
                        score += Math.Min(amount, opponent.Hand.Count) * 2.0;
                        break;
                    case EffectNames.Buff:
                        score += BuffValue(state, self, opponent, source, effect);
                        break;
                    case EffectNames.GrantKeyword:
                        score += 2.0;
                        break;
                    case EffectNames.Summon:
                        score += amount * 4.0;
                        break;
                    case EffectNames.SummonLeader:
                        score += 6.0;
                        break;
                    case EffectNames.EndTurn:
                        score += 4.0;
                        break;
                    case EffectNames.AddOppPunishTurn:
                        score += amount * 1.2;
                        break;
                    case EffectNames.AddSelfPunishTurn:
                        score -= amount * 0.5;
                        break;
                    case EffectNames.ConvertPunishToDiscard:
                        score += 2.5;
                        break;
                    case EffectNames.ProtectTurn:
                        score += 2.5;
                        break;
                    case EffectNames.Negate:
                        score += 2.0;
                        break;
                    case EffectNames.NegateEnemyEffectsTurn:
                        score += 4.0;
                        break;
                    case EffectNames.RestoreAttacks:
                        score += 3.0;
                        break;
                    case EffectNames.GainLife:
                        score += amount;
                        break;
                    case EffectNames.LoseLife:
                        score -= amount;
                        break;
                    case EffectNames.DamageCastle:
                        score += amount * 0.7;
                        break;
                    case EffectNames.AddRoot:
                        score += amount * (self.RootStacks >= 12 ? 1.0 : 2.5);
                        break;
                    case EffectNames.AddRampant:
                        score += amount * (3 - self.RampantStacks) * 5.0 + amount * 2.0;
                        break;
                    case EffectNames.Control:
                        score += 10.0;
                        break;
                    case EffectNames.Enfeeble:
                        score += amount * 1.5;
                        break;
                    case EffectNames.SkipReshuffle:
                        score += 1.5;
                        break;
                    case EffectNames.Rollback:
                        score += 2.0;
                        break;
                    case EffectNames.Commit:
                    case EffectNames.Push:
                    case EffectNames.Pull:
                        score += 1.0;
                        break;
                    case EffectNames.WinGame:
                        score += 1000.0;
                        break;
                    default:
                        score += 1.0;
                        break;
                }
            }

            return score;
        }

        private double BuffValue(
            GameState state,
            PlayerState self,
            PlayerState opponent,
            CardInstance? source,
            EffectSpec effect)
        {
            var amount = effect.Amount;
            var mode = effect.Param ?? "both";
            var rawMode = effect.Target is null ? mode : mode;
            var growthTarget = effect.Target is null
                || string.Equals(effect.Target, "SELF", StringComparison.Ordinal)
                || string.Equals(effect.Target, "FRIENDLY_MINION", StringComparison.Ordinal)
                || string.Equals(effect.Target, "ALL_FRIENDLY_MINIONS", StringComparison.Ordinal);
            var woodSource = string.Equals(source?.Definition.Faction, WoodFaction, StringComparison.Ordinal);
            var rootLayers = growthTarget && (rawMode == "root" || woodSource) ? self.RootStacks : 0;
            var rampantLayers = growthTarget && (rawMode == "rampant" || woodSource) ? self.RampantStacks : 0;

            var effective = amount;
            if (amount > 0 && (rootLayers != 0 || rampantLayers != 0))
            {
                var additive = amount + rootLayers;
                var multiplier = 1 << Math.Min(3, Math.Max(0, rampantLayers));
                effective = additive * multiplier;
            }

            var score = effective * 0.6;
            if (effective != amount) score += 1.0;
            if (!growthTarget) score += 3.0; // useful enemy debuff
            return score;
        }

        private double DamageValue(EffectSpec effect, int amount, int enemyMinionCount)
        {
            var target = effect.Target ?? string.Empty;
            if (target.Contains("ENEMY_FACE", StringComparison.Ordinal)) return amount * 1.1;
            if (target.StartsWith("ALL_", StringComparison.Ordinal))
            {
                return amount * Math.Max(1, enemyMinionCount) * 0.8;
            }

            if (target.StartsWith("ENEMY", StringComparison.Ordinal)) return amount * 0.9 + (target.Contains("MINION", StringComparison.Ordinal) ? 1.5 : 0);
            if (target.StartsWith("ALL_MINIONS", StringComparison.Ordinal)) return amount * 0.3;
            return amount * 0.2;
        }

        public static List<CardInstance> LivingMinions(PlayerState player)
        {
            var result = new List<CardInstance>();
            foreach (var card in player.Field)
            {
                if (card.IsMinion && card.IsAlive) result.Add(card);
            }

            return result;
        }

        public static int Threat(CardInstance card)
        {
            var score = card.Attack * 2 + Math.Max(0, card.Health);
            if (card.HasKeyword("嘲讽")) score += 4;
            if (card.HasKeyword("圣盾")) score += 3;
            score += (card.Definition.AttacksPerTurn - 1) * 4;
            if (card.IsLeaderEntity) score += 20;
            return score;
        }

        private static int PunishOf(LegalAction action)
        {
            if (action.Payload is not null
                && action.Payload.TryGetValue("punish", out var raw)
                && raw is not null)
            {
                return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            }

            return 0;
        }

        private static CardInstance? Find(GameState state, long? instanceId)
            => instanceId.HasValue ? state.FindEntity(instanceId.Value) : null;
    }
}
