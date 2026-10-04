using System;
using System.Collections.Generic;
using System.Globalization;

namespace DominionWars.Adapters.Ai
{

/// <summary>
/// What ONE candidate move costs the actor in opponent card flow.
///
/// WHY THIS IS NOT PART OF THE STATE VECTOR: a state vector describes what is true
/// now. "What does playing THIS card hand my opponent" is action-dependent — it is a
/// property of a transition, not of a position. Summing the punish across a whole
/// hand produces a number with no decision meaning (a hand of punish 1/4/8 totalling
/// 13 says nothing about which of the three to play), so this type settles ONE move
/// at a time and the state evaluator stays free of it.
///
/// WHY IT DOES NOT RECOMPUTE THE RULE: the engine already resolves the cost and
/// advertises it. <c>LegalActionGenerator</c> computes
/// <c>CardPlayRules.EffectivePunish(state, player, card)</c> — which folds in the
/// printed value (or the activated punish cost), the pioneer modifier and the
/// turn's punish delta — and publishes the result as the advertisement's
/// <c>punish</c> payload entry. This type READS that number. Re-deriving it here
/// would create a second copy of a game rule in the adapter, which is exactly the
/// failure mode this project already ruled out for legality and victory.
/// </summary>
public sealed class MoveSettlement
{
    internal MoveSettlement(
        string actionId,
        string actionType,
        int? advertisedPunish,
        string source,
        string note)
    {
        ActionId = actionId ?? string.Empty;
        ActionType = actionType ?? string.Empty;
        AdvertisedPunish = advertisedPunish;
        Source = source ?? string.Empty;
        Note = note ?? string.Empty;
    }

    public string ActionId { get; }

    public string ActionType { get; }

    /// <summary>
    /// The engine's resolved punish for this move, or null when the advertisement
    /// does not carry one (a non-card-play move, or a move whose cost is not a
    /// punish transfer at all). Null means "not a punish transfer", NOT zero.
    /// </summary>
    public int? AdvertisedPunish { get; }

    /// <summary>Which field the value came from, for provenance.</summary>
    public string Source { get; }

    public string Note { get; }

    /// <summary>True when this move transfers card flow to the opponent.</summary>
    public bool TransfersCardFlow => AdvertisedPunish.HasValue && AdvertisedPunish.Value > 0;

    /// <summary>
    /// The opponent's hand size after this move, given their hand now. This is the
    /// direct, checkable consequence of the transfer: punish draws cards, so their
    /// options grow by exactly that many.
    /// </summary>
    public int? OpponentHandAfter(int opponentHandNow)
        => AdvertisedPunish.HasValue ? opponentHandNow + AdvertisedPunish.Value : (int?)null;

    public override string ToString()
        => ActionType + " " + ActionId + ": "
           + (AdvertisedPunish.HasValue
               ? "opponent draws " + AdvertisedPunish.Value.ToString(CultureInfo.InvariantCulture)
               : "no punish transfer")
           + (Note.Length > 0 ? " (" + Note + ")" : string.Empty);
}

/// <summary>
/// Settles the cost of candidate moves by reading the engine's own advertised value.
/// </summary>
public static class MoveSettlements
{

/// <summary>The advertisement payload key the engine publishes the resolved cost under.</summary>
public const string PunishPayloadKey = "punish";

/// <summary>
/// Settles one advertised move.
///
/// Reads <c>payload["punish"]</c>, which the engine computed. An advertisement
/// without that key is reported as "no punish transfer" rather than as costing zero,
/// because the two are different claims: a move with no punish key is not a card
/// play whose cost happens to be nil.
/// </summary>
public static MoveSettlement Settle(RuntimeLegalAction action)
{
    if (action is null) throw new ArgumentNullException(nameof(action));

    if (action.Payload is null || !action.Payload.ContainsKey(PunishPayloadKey))
    {
        return new MoveSettlement(
            action.ActionId,
            action.Type,
            null,
            string.Empty,
            "the advertisement carries no punish entry, so this move is not a punish transfer");
    }

    var raw = action.Payload[PunishPayloadKey];
    var value = AsInt(raw);
    if (!value.HasValue)
    {
        return new MoveSettlement(
            action.ActionId,
            action.Type,
            null,
            "payload.punish",
            "the advertisement's punish entry is not an integer (" + (raw?.GetType().Name ?? "null") + ")");
    }

    if (value.Value < 0)
    {
        return new MoveSettlement(
            action.ActionId,
            action.Type,
            null,
            "payload.punish",
            "the advertised punish is negative (" + value.Value.ToString(CultureInfo.InvariantCulture)
            + "), which is not a valid cost; refusing to treat it as a transfer");
    }

    return new MoveSettlement(
        action.ActionId,
        action.Type,
        value,
        "payload.punish (resolved by the engine)",
        value.Value == 0
            ? "the resolved cost is 0, so this move transfers no card flow"
            : "the opponent draws this many cards when this move resolves");
}

/// <summary>Settles a whole advertised action list, in the order supplied.</summary>
public static IReadOnlyList<MoveSettlement> SettleAll(IReadOnlyList<RuntimeLegalAction> actions)
{
    if (actions is null) throw new ArgumentNullException(nameof(actions));

    var result = new List<MoveSettlement>(actions.Count);
    foreach (var action in actions)
    {
        if (action is null) continue;
        result.Add(Settle(action));
    }

    return result;
}

/// <summary>
/// The advertised moves that actually transfer card flow, cheapest first. A caller
/// comparing moves wants this ordering, not the raw advertisement order.
/// </summary>
public static IReadOnlyList<MoveSettlement> CardFlowTransfers(IReadOnlyList<RuntimeLegalAction> actions)
{
    var settled = new List<MoveSettlement>();
    foreach (var settlement in SettleAll(actions))
    {
        if (settlement.TransfersCardFlow) settled.Add(settlement);
    }

    settled.Sort((left, right) =>
    {
        var byPunish = left.AdvertisedPunish!.Value.CompareTo(right.AdvertisedPunish!.Value);
        if (byPunish != 0) return byPunish;
        return string.Compare(left.ActionId, right.ActionId, StringComparison.Ordinal);
    });
    return settled;
}

private static int? AsInt(object? raw)
{
    switch (raw)
    {
        case int value: return value;
        case long value when value is >= int.MinValue and <= int.MaxValue: return (int)value;
        case short value: return value;
        case byte value: return value;
        case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
            return parsed;
        default: return null;
    }
}

}

}
