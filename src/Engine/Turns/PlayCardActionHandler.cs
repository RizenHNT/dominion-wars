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

    /// <summary>
    /// <c>EFFECT_SKIPPED</c> reason key for a punish response that T1
    /// (<see cref="DominionWars.Engine.Rules.MatchRules.MaxPunishResponsesPerRound"/>)
    /// removed from the round. Distinct from a decline, which emits nothing.
    /// </summary>
    internal const string PunishResponseLimitReasonKey = "rule.punish_response_limit";

    /// <summary>
    /// The skipped item is not an effect spec, so it has no
    /// <c>EffectNames</c> action; this local name keeps the shared
    /// <c>EFFECT_SKIPPED</c> shape without widening the Effects contract.
    /// </summary>
    private const string SuppressedPunishResponseAction = "PUNISH_RESPONSE";

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

        ResolvePrepared(
            state,
            player,
            card,
            preparation,
            root.EventId,
            0,
            true,
            new PunishRound(state.Rules.MaxPunishResponsesPerRound));
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
        bool topLevel,
        PunishRound round)
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
            new EffectRuntime(state).TriggerOpponentDiscardEffects(
                player.PlayerIndex,
                preparation.Discards.Count,
                new EffectContext(player.PlayerIndex, rootEventId));
            if (state.WinnerPlayerIndex.HasValue)
            {
                return;
            }
        }
        else if (preparation.Fizzle)
        {
            ExecuteMutation(state, _ =>
            {
                ConsumeTags(player, card);
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
            ResolvePunishResponses(state, drawn, rootEventId, chainDepth + 1, round);
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
        else if (!ambushResult.Negated
            && !state.WinnerPlayerIndex.HasValue
            && card.Definition.Chant == 0
            && preparation.Effects.Count == 0)
        {
            new EffectRuntime(state).EmitSkipped(actionContext, "CARD_EFFECTS", "effect.no_effects");
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
        int chainDepth,
        PunishRound round)
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

            // T1（data/balance.json: maxPunishResponsesPerRound，出厂 0 = 关闭）：
            // 同一根动作事件内的响应额度用尽后，剩余的可响应牌不再被询问、
            // 也不再被接受 —— 只是被"抑制"，并且照其它空发效果一样发出事件，
            // 让回放/计数器能把"被规则抑制"和"玩家自己放弃"区分开。
            // 额度为 0（默认）时 IsExhausted 恒为 false，本分支不改变任何行为。
            if (round.IsExhausted)
            {
                EmitPunishResponseSuppressed(state, owner, card, rootEventId);
                continue;
            }

            var cost = CardPlayRules.EffectivePunish(state, owner, card);
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
            round.AcceptedCount++;
            ResolvePrepared(state, owner, card, preparation, rootEventId, chainDepth, false, round);
            if (state.WinnerPlayerIndex.HasValue)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Reuses the normal punish-draw response chain for a mechanical
    /// lifecycle action. COMMIT and PULL are not card plays, but their
    /// lifecycle punishment still uses the same authoritative draw/response
    /// semantics and chain limit.
    /// </summary>
    /// <remarks>
    /// 先驱威压 (RULES §3.2) is deliberately NOT applied here. §3.2 promises +1
    /// to the punish value of the opponent's cards, which is the printed value
    /// paid when a card is played; §12.4 defines commitCost/downloadCost as
    /// explicit per-card punish-draw amounts for a lifecycle action that is not
    /// a card play. Measured: applying the modifier here flips the machine
    /// deck's six-download win line, so it stays an owner decision rather than
    /// being folded in silently.
    /// </remarks>
    internal bool ResolveLifecyclePunish(
        GameState state,
        int actorPlayerIndex,
        int amount,
        long rootEventId)
    {
        if (amount <= 0)
        {
            return !state.WinnerPlayerIndex.HasValue;
        }

        var opponent = state.GetOpponent(actorPlayerIndex);
        var drawn = new EffectRuntime(state).DrawForPunish(
            opponent.PlayerIndex,
            amount,
            rootEventId);
        // One lifecycle action is one root event, so it is one punish round.
        ResolvePunishResponses(
            state,
            drawn,
            rootEventId,
            1,
            new PunishRound(state.Rules.MaxPunishResponsesPerRound));
        return !state.WinnerPlayerIndex.HasValue;
    }

    /// <summary>
    /// Emits the same <c>EFFECT_SKIPPED</c> shape every other suppressed effect
    /// uses (see <see cref="EffectRuntime.EmitSkipped"/>), so a replay or a
    /// counter can tell a response that the round cap removed from a response
    /// the player merely declined. <c>target</c> is the suppressed drawn card;
    /// the parent event is still the round's root action event.
    /// </summary>
    private static void EmitPunishResponseSuppressed(
        GameState state,
        PlayerState owner,
        CardInstance card,
        long rootEventId)
    {
        // EffectContext requires the source card to be controlled by the source
        // player. A hand card is normally its owner's, but CONTROL can leave a
        // temporary controller behind, so the source card is only attached when
        // that invariant actually holds.
        var context = card.ControllerPlayerIndex == owner.PlayerIndex
            ? new EffectContext(owner.PlayerIndex, rootEventId, sourceCard: card)
            : new EffectContext(owner.PlayerIndex, rootEventId);
        new EffectRuntime(state).EmitSkipped(
            context,
            SuppressedPunishResponseAction,
            PunishResponseLimitReasonKey,
            card.InstanceId);
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

    /// <summary>
    /// Consumes a played card's tags so the one-card-per-tag-per-turn rule can
    /// throttle repeats. Growth has exactly one source of truth: the explicit
    /// ADD_ROOT / ADD_RAMPANT effects resolved from card data, never the tags.
    /// </summary>
    private static void ConsumeTags(PlayerState player, CardInstance card)
    {
        foreach (var tag in card.Definition.Tags)
        {
            player.UsedTags.Add(tag);
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

    /// <summary>
    /// One punish round: every response belonging to a single root action event,
    /// across the whole nesting of its response chain. <see cref="chainDepth"/> is
    /// per depth, not per round — one draw batch can take several responses at the
    /// same depth — so the accepted count is threaded here instead, which is the
    /// only place that can see a whole round.
    /// <para>
    /// <see cref="Limit"/> 0 means unlimited: <see cref="IsExhausted"/> is then
    /// always false and no code path or event changes.
    /// </para>
    /// </summary>
    private sealed class PunishRound
    {
        public PunishRound(int limit) => Limit = limit;

        public int Limit { get; }
        public int AcceptedCount { get; set; }
        public bool IsExhausted => Limit > 0 && AcceptedCount >= Limit;
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
