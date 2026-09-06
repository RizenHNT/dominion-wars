using System;
using System.Collections.Generic;
using System.Linq;
using DominionWars.Engine.Command;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Targeting;

namespace DominionWars.Engine.Turns
{

/// <summary>Resolves ACTION card plays and synchronous punish-response chains.</summary>
public sealed class PlayCardActionHandler : ITurnActionHandler
{
    internal const int DefaultChainLimit = 20;
    private readonly CardTargetValidator _targets;
    private readonly IPunishResponsePolicy _punishResponses;
    private readonly int _chainLimit;
    private readonly ICardCostModel _costModel;

    public PlayCardActionHandler(
        TargetPolicy? targetPolicy = null,
        IPunishResponsePolicy? punishResponses = null,
        int chainLimit = DefaultChainLimit)
        : this(targetPolicy, punishResponses, chainLimit, PunishOnlyCostModel.Instance)
    {
    }

    /// <summary>
    /// Extension overload for a future cost model. The original three-argument
    /// constructor remains unchanged for compiled Unity/client assemblies.
    /// </summary>
    public PlayCardActionHandler(
        TargetPolicy? targetPolicy,
        IPunishResponsePolicy? punishResponses,
        int chainLimit,
        ICardCostModel? costModel)
    {
        if (chainLimit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(chainLimit));
        }

        _targets = new CardTargetValidator(targetPolicy ?? new TargetPolicy());
        _punishResponses = punishResponses ?? new DeclinePunishResponsePolicy();
        _chainLimit = chainLimit;
        _costModel = costModel ?? PunishOnlyCostModel.Instance;
    }

    public bool CanHandle(string phaseId, string actionType)
    {
        return string.Equals(phaseId, TurnPhase.Action, StringComparison.Ordinal)
            && string.Equals(actionType, LegalActionGenerator.PlayCard, StringComparison.Ordinal);
    }

    public GameActionResult Execute(GameState state, GameActionRequest request, TurnFlow flow)
    {
        if (!request.SourceEntityId.HasValue)
        {
            return GameActionResult.Reject("action.source_required");
        }

        var player = state.GetPlayer(request.ActorPlayerIndex);
        var card = FindInHand(player, request.SourceEntityId.Value);
        if (card is null)
        {
            return GameActionResult.Reject("action.card_not_in_hand");
        }

        var legacyActionId = LegalActionGenerator.PlayActionId(card.InstanceId);
        var targetActionId = LegalActionGenerator.PlayActionId(card.InstanceId, request.TargetId);
        if (request.ActionId is not null
            && !string.Equals(request.ActionId, legacyActionId, StringComparison.Ordinal)
            && !string.Equals(request.ActionId, targetActionId, StringComparison.Ordinal))
        {
            return GameActionResult.Reject("action.id_mismatch");
        }

        var preparation = Prepare(
            state,
            player,
            card,
            request.TargetId,
            request.SelectedEntityIds);
        if (!preparation.Accepted)
        {
            return GameActionResult.Reject(preparation.ReasonKey!);
        }

        var root = state.Events.Append("CARD_PLAYED", null, Data(
            "player", player.PlayerIndex,
            "source", card.InstanceId,
            "cardId", card.Definition.Id,
            "punish", preparation.Cost,
            "fizzle", preparation.Fizzle));

        ResolvePrepared(state, player, card, preparation, root.EventId, 0, true);
        if (state.EndTurnRequested && !state.WinnerPlayerIndex.HasValue)
        {
            flow.Advance(state, request.ActorPlayerIndex);
        }

        return GameActionResult.Accept();
    }

    private PreparedPlay Prepare(
        GameState state,
        PlayerState player,
        CardInstance card,
        string? targetId,
        IReadOnlyList<long> selectedDiscardIds)
    {
        if (!CardPlayRules.CanPlay(player, card, out var reasonKey))
        {
            return PreparedPlay.Reject(reasonKey);
        }

        var effects = card.PunishActivated
            ? card.Definition.PunishEffects
            : card.Definition.OnPlayEffects;
        var resolvedCost = _costModel.Evaluate(state, player, card);
        if (resolvedCost is null)
        {
            return PreparedPlay.Reject("action.invalid_cost");
        }

        if (resolvedCost.HasUnsupportedResources)
        {
            return PreparedPlay.Reject("action.cost_system_unavailable");
        }

        var cost = resolvedCost.EffectivePunish;
        var fizzle = !player.PunishToSelfDiscardThisTurn
            && cost > state.GetOpponent(player.PlayerIndex).Deck.Count;
        var runtime = new EffectRuntime(state);
        var dispatcher = EffectDispatcher.CreateDefault(runtime);
        foreach (var effect in effects)
        {
            if (!fizzle && !dispatcher.RegisteredActions.Contains(effect.Action))
            {
                return PreparedPlay.Reject("action.unknown_effect");
            }
        }

        var requirement = _targets.GetRequirement(effects);
        TargetSelection selection = default;
        if (!fizzle && requirement != TargetRequirement.None && targetId is null)
        {
            return PreparedPlay.Reject("action.target_required");
        }

        if (!fizzle && targetId is not null
            && !_targets.TryResolve(state, player.PlayerIndex, targetId, card, effects, requirement, out selection))
        {
            return PreparedPlay.Reject("action.invalid_target");
        }

        var discards = new List<CardInstance>();
        if (player.PunishToSelfDiscardThisTurn && cost > 0)
        {
            var required = Math.Min(cost, Math.Max(0, player.Hand.Count - 1));
            var seen = new HashSet<long>();
            foreach (var id in selectedDiscardIds)
            {
                var selected = FindInHand(player, id);
                if (selected is null || selected == card || !seen.Add(id))
                {
                    return PreparedPlay.Reject("action.invalid_discard_selection");
                }

                discards.Add(selected);
            }

            if (discards.Count != required)
            {
                return PreparedPlay.Reject("action.discard_selection_required");
            }
        }

        return PreparedPlay.Accept(cost, fizzle, effects, selection, discards);
    }

    private void ResolvePrepared(
        GameState state,
        PlayerState player,
        CardInstance card,
        PreparedPlay preparation,
        long rootEventId,
        int chainDepth,
        bool topLevel)
    {
        var opponent = state.GetOpponent(player.PlayerIndex);
        if (player.PunishToSelfDiscardThisTurn && preparation.Cost > 0)
        {
            ExecuteMutation(state, _ =>
            {
                foreach (var discarded in preparation.Discards)
                {
                    player.Hand.Remove(discarded);
                    player.Graveyard.Add(discarded);
                    player.TotalDiscarded++;
                }
            });
            state.Events.Append("CARDS_DISCARDED", rootEventId, Data(
                "player", player.PlayerIndex,
                "count", preparation.Discards.Count,
                "reasonKey", "rule.punish_converted"));
        }
        else if (preparation.Fizzle)
        {
            ExecuteMutation(state, _ =>
            {
                ConsumeTags(player, card, grantGrowth: false);
                player.Hand.Remove(card);
                player.Graveyard.Add(card);
                if (topLevel)
                {
                    state.EndTurnRequested = true;
                }
            });
            return;
        }
        else if (preparation.Cost > 0)
        {
            var runtime = new EffectRuntime(state);
            var drawn = runtime.DrawForPunish(opponent.PlayerIndex, preparation.Cost, rootEventId);
            ResolvePunishResponses(state, drawn, rootEventId, chainDepth + 1);
            if (state.WinnerPlayerIndex.HasValue)
            {
                return;
            }
        }

        ExecuteMutation(state, _ =>
        {
            ConsumeTags(player, card);
            player.Hand.Remove(card);
            if (card.Definition.IsMinion)
            {
                card.SummonedThisTurn = true;
                player.Field.Add(card);
            }
            else if (card.Definition.Chant > 0)
            {
                card.ChantRemaining = card.Definition.Chant;
                player.Field.Add(card);
            }
        });

        if (card.Definition.IsMinion)
        {
            state.Events.Append("MINION_SUMMONED", rootEventId, Data(
                "player", player.PlayerIndex,
                "target", card.InstanceId,
                "cardId", card.Definition.Id));
        }

        var triggerKinds = new List<string> { "OPPONENT_PLAYS_CARD" };
        triggerKinds.Add(card.Definition.IsMinion
            ? "OPPONENT_SUMMONS"
            : "OPPONENT_PLAYS_SPELL");
        var actionContext = new EffectContext(
            player.PlayerIndex,
            rootEventId,
            sourceCard: card,
            playedCard: card,
            selectedTargetId: preparation.Selection.EntityId,
            selectedCoreTarget: preparation.Selection.CoreTarget);
        var ambushResult = new AmbushTriggerResolver().Resolve(
            state,
            player.PlayerIndex,
            triggerKinds,
            rootEventId,
            playedCard: card,
            actionContext: actionContext);

        if (!ambushResult.Negated
            && !state.WinnerPlayerIndex.HasValue
            && card.Definition.Chant == 0
            && preparation.Effects.Count > 0)
        {
            var dispatcher = EffectDispatcher.CreateDefault(new EffectRuntime(state));
            dispatcher.ApplyAll(preparation.Effects, actionContext);
        }

        if (!card.Definition.IsMinion && card.Definition.Chant == 0)
        {
            ExecuteMutation(state, _ => player.Graveyard.Add(card));
        }
    }

    private void ResolvePunishResponses(
        GameState state,
        IReadOnlyList<CardInstance> drawn,
        long rootEventId,
        int chainDepth)
    {
        if (chainDepth > _chainLimit || state.WinnerPlayerIndex.HasValue)
        {
            return;
        }

        foreach (var card in drawn)
        {
            var owner = state.GetPlayer(card.OwnerPlayerIndex);
            if (!owner.Hand.Contains(card)
                || !card.PunishActivated
                || !PunishConditionEvaluator.IsSatisfied(state, card)
                || !CardPlayRules.TagsFree(owner, card))
            {
                continue;
            }

            var cost = CardPlayRules.EffectivePunish(owner, card);
            var decision = _punishResponses.Decide(state, card, cost, chainDepth);
            if (!decision.Activate)
            {
                continue;
            }

            var preparation = Prepare(state, owner, card, decision.TargetId, decision.SelectedDiscardIds);
            if (!preparation.Accepted)
            {
                continue;
            }

            state.Events.Append("PUNISH_TRIGGERED", rootEventId, Data(
                "player", owner.PlayerIndex,
                "source", card.InstanceId,
                "chainDepth", chainDepth));
            ResolvePrepared(state, owner, card, preparation, rootEventId, chainDepth, false);
            if (state.WinnerPlayerIndex.HasValue)
            {
                return;
            }
        }
    }

    private static CardInstance? FindInHand(PlayerState player, long instanceId)
    {
        foreach (var candidate in player.Hand)
        {
            if (candidate.InstanceId == instanceId)
            {
                return candidate;
            }
        }

        return null;
    }

    private static void ConsumeTags(PlayerState player, CardInstance card, bool grantGrowth = true)
    {
        foreach (var tag in card.Definition.Tags)
        {
            player.UsedTags.Add(tag);
            if (!grantGrowth)
            {
                continue;
            }

            if (string.Equals(tag, "扎根", StringComparison.Ordinal))
            {
                player.RootStacks = checked(player.RootStacks + 1);
            }
            else if (string.Equals(tag, "疯长", StringComparison.Ordinal))
            {
                player.RampantStacks = Math.Min(3, player.RampantStacks + 1);
            }
        }
    }

    private static void ExecuteMutation(GameState state, Action<GameState> mutation)
    {
        state.Commands.Execute(state, new PlayCardCommand(mutation));
    }

    private static IReadOnlyDictionary<string, object?> Data(params object?[] values)
    {
        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < values.Length; index += 2)
        {
            data[(string)values[index]!] = values[index + 1];
        }

        return data;
    }

    private sealed class PlayCardCommand : IGameCommand
    {
        private readonly Action<GameState> _mutation;
        public PlayCardCommand(Action<GameState> mutation) => _mutation = mutation;
        public void Apply(GameState state) => _mutation(state);
    }

    private sealed class PreparedPlay
    {
        private PreparedPlay(
            bool accepted,
            string? reasonKey,
            int cost,
            bool fizzle,
            IReadOnlyList<EffectSpec>? effects,
            TargetSelection selection,
            IReadOnlyList<CardInstance>? discards)
        {
            Accepted = accepted;
            ReasonKey = reasonKey;
            Cost = cost;
            Fizzle = fizzle;
            Effects = effects ?? Array.Empty<EffectSpec>();
            Selection = selection;
            Discards = discards ?? Array.Empty<CardInstance>();
        }

        public bool Accepted { get; }
        public string? ReasonKey { get; }
        public int Cost { get; }
        public bool Fizzle { get; }
        public IReadOnlyList<EffectSpec> Effects { get; }
        public TargetSelection Selection { get; }
        public IReadOnlyList<CardInstance> Discards { get; }

        public static PreparedPlay Reject(string reasonKey)
            => new PreparedPlay(false, reasonKey, 0, false, null, default, null);

        public static PreparedPlay Accept(
            int cost,
            bool fizzle,
            IReadOnlyList<EffectSpec> effects,
            TargetSelection selection,
            IReadOnlyList<CardInstance> discards)
            => new PreparedPlay(true, null, cost, fizzle, effects, selection, discards);
    }

}
}
