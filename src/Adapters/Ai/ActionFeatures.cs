using System;
using System.Collections.Generic;
using System.Globalization;

namespace DominionWars.Adapters.Ai
{

/// <summary>
/// Pure feature extraction for one advertised action.
///
/// The only inputs are the advertised <see cref="RuntimeLegalAction"/> and the
/// viewer-safe <see cref="RuntimeSnapshotEnvelope"/>: this type never receives,
/// reads, or references GameState (or any other authoritative engine type), so
/// it cannot derive a rule the advertisement did not already state. It also
/// mutates nothing, keeps no state between calls, and contains no RNG and no
/// clock, which is what makes a playstyle weight vector the only difference
/// between two AIs.
///
/// Every feature is a small non-negative integer. Amount features
/// (<see cref="Punish"/>, <see cref="DiscardCost"/>) are read from the
/// advertisement's own payload, which is published data rather than a derived
/// rule; an absent, malformed, or negative amount reads as 0 so a broken
/// advertisement can never turn a declared cost into a benefit.
///
/// Honest limits, because inventing features the wire data cannot support would
/// make the weight tables a lie:
/// <list type="bullet">
/// <item>The advertisement carries no effect list, so "does this card draw
/// cards?" is not computable here; card advantage is expressed only through the
/// costs that ARE advertised (hand spend, punish, punish-converted discard).</item>
/// <item><see cref="RuntimeCardSnapshot"/> has no leader-entity flag, so an
/// attack on an entity the snapshot lists on the opponent's board counts as a
/// board exchange (<see cref="Trade"/>) even when that entity is the opponent's
/// leader. Core attacks (enemy life, the Royal Castle, a leader core) are the
/// unambiguous <see cref="Face"/> case.</item>
/// <item>The engine advertises no PUSH player action (UPLOAD is an effect), but
/// PUSH is part of the lifecycle verb set so a future advertisement would not
/// silently lose its lifecycle feature.</item>
/// </list>
/// </summary>
public static class ActionFeatures
{
    // ---- Published feature names (also the weight-vector keys) -------------

    /// <summary>1 when the action is a lifecycle verb: PULL, PUSH, COMMIT, or ROLLBACK.</summary>
    public const string Lifecycle = "lifecycle";

    /// <summary>
    /// 1 when the actor's own cloud stack is non-empty, i.e. an advertised
    /// download can still resolve and so advances the actor's own pull win axis.
    /// This is the same viewer-safe read the shipped policy used to decide a
    /// download was "useful".
    /// </summary>
    public const string WinAxis = "win_axis";

    /// <summary>1 when the action is ROLLBACK, which moves a committed card back out of the queue.</summary>
    public const string ChainReverse = "chain_reverse";

    /// <summary>1 when the action plays a card from hand (PLAY_CARD).</summary>
    public const string Board = "board";

    /// <summary>1 when the action sets an ambush (SET_AMBUSH).</summary>
    public const string Ambush = "ambush";

    /// <summary>1 when the action spends a card from hand (PLAY_CARD, SET_AMBUSH, DISCARD).</summary>
    public const string SpendCard = "spend_card";

    /// <summary>1 when the action moves one of the actor's field cards out of the field (COMMIT).</summary>
    public const string SpendField = "spend_field";

    /// <summary>1 when the action is an ATTACK advertisement.</summary>
    public const string Damage = "damage";

    /// <summary>
    /// 1 when the attack target is a core rather than a board card: the enemy
    /// life, the shared Royal Castle, or a leader core. 0 for every non-attack
    /// and for an advertisement that names no target at all.
    /// </summary>
    public const string Face = "face";

    /// <summary>
    /// 1 when the attack target is an entity the snapshot lists on the
    /// opponent's board (field or leader zone), i.e. a board exchange. 0 for
    /// every non-attack and for an advertisement that names no target at all.
    /// </summary>
    public const string Trade = "trade";

    /// <summary>The advertisement's own punish amount (cards the opponent draws).</summary>
    public const string Punish = "punish";

    /// <summary>The advertisement's own punish-converted self-discard requirement, if it declares one.</summary>
    public const string DiscardCost = "discard_cost";

    /// <summary>1 when the action ends the ACTION phase (END_TURN).</summary>
    public const string EndTurn = "end_turn";

    private const string PayloadPunishKey = "punish";
    private const string PayloadDiscardCostKey = "discardRequired";

    private static readonly string[] FeatureNames =
    {
        Lifecycle,
        WinAxis,
        ChainReverse,
        Board,
        Ambush,
        SpendCard,
        SpendField,
        Damage,
        Face,
        Trade,
        Punish,
        DiscardCost,
        EndTurn,
    };

    private static readonly IReadOnlyList<string> FeatureNameList = Array.AsReadOnly(FeatureNames);

    /// <summary>Every published feature name, in a stable order.</summary>
    public static IReadOnlyList<string> Names => FeatureNameList;

    /// <summary>
    /// The complete feature vector of one advertised action as seen through one
    /// viewer-safe snapshot. Every published name is present (0 when it does not
    /// apply), so a weight table is always total over a returned vector.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Of(
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction action)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (action is null) throw new ArgumentNullException(nameof(action));

        var type = action.Type ?? string.Empty;
        var isPull = string.Equals(type, AdvertisedActionPolicy.PullAction, StringComparison.Ordinal);
        var isCommit = string.Equals(type, AdvertisedActionPolicy.CommitAction, StringComparison.Ordinal);
        var isRollback = string.Equals(type, AdvertisedActionPolicy.RollbackAction, StringComparison.Ordinal);
        var isPush = string.Equals(type, AdvertisedActionPolicy.PushAction, StringComparison.Ordinal);
        var isPlay = string.Equals(type, AdvertisedActionPolicy.PlayCardAction, StringComparison.Ordinal);
        var isAmbush = string.Equals(type, AdvertisedActionPolicy.SetAmbushAction, StringComparison.Ordinal);
        var isDiscard = string.Equals(type, AdvertisedActionPolicy.DiscardAction, StringComparison.Ordinal);
        var isAttack = string.Equals(type, AdvertisedActionPolicy.AttackAction, StringComparison.Ordinal);
        var isEndTurn = string.Equals(type, AdvertisedActionPolicy.EndTurnAction, StringComparison.Ordinal);

        var target = isAttack ? AttackTarget(snapshot, action) : AttackTargetKind.None;

        var features = new Dictionary<string, int>(FeatureNames.Length, StringComparer.Ordinal)
        {
            [Lifecycle] = isPull || isPush || isCommit || isRollback ? 1 : 0,
            [WinAxis] = isPull && OwnCloudStackCount(snapshot, action) > 0 ? 1 : 0,
            [ChainReverse] = isRollback ? 1 : 0,
            [Board] = isPlay ? 1 : 0,
            [Ambush] = isAmbush ? 1 : 0,
            [SpendCard] = isPlay || isAmbush || isDiscard ? 1 : 0,
            [SpendField] = isCommit ? 1 : 0,
            [Damage] = isAttack ? 1 : 0,
            [Face] = target == AttackTargetKind.Core ? 1 : 0,
            [Trade] = target == AttackTargetKind.BoardEntity ? 1 : 0,
            [Punish] = PayloadAmount(action.Payload, PayloadPunishKey),
            [DiscardCost] = PayloadAmount(action.Payload, PayloadDiscardCostKey),
            [EndTurn] = isEndTurn ? 1 : 0,
        };

        return features;
    }

    /// <summary>
    /// One feature value; an unpublished name reads as 0. Callers that carry a
    /// weight table should validate its keys against <see cref="Names"/> (see
    /// <see cref="PlaystyleRegistry"/>) so a typo cannot silently become a
    /// no-op weight.
    /// </summary>
    public static int Value(
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction action,
        string feature)
    {
        if (feature is null) throw new ArgumentNullException(nameof(feature));
        var features = Of(snapshot, action);
        return features.TryGetValue(feature, out var value) ? value : 0;
    }

    private enum AttackTargetKind
    {
        None,
        Unresolved,
        Core,
        BoardEntity,
    }

    /// <summary>
    /// Classifies an advertised attack target using the snapshot alone. A
    /// non-numeric wire target (the enemy life <c>player_N</c>, the shared
    /// <c>castle</c>, or a legacy <c>core:</c> reference) is a core attack; a
    /// numeric target the snapshot lists on the opponent's board is a board
    /// exchange; any other numeric target is not something the viewer-safe
    /// snapshot can see, so it is treated as a core attack rather than guessed
    /// into a trade; an advertisement that names no target sets neither.
    /// </summary>
    private static AttackTargetKind AttackTarget(
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction action)
    {
        if (action.TargetId is null) return AttackTargetKind.Unresolved;
        if (!TryEntityId(action.TargetId, out var entityId)) return AttackTargetKind.Core;
        if (snapshot.Players is null || snapshot.Players.Count != 2) return AttackTargetKind.Core;
        var actor = action.Actor;
        if (actor is < 0 or > 1) return AttackTargetKind.Core;

        var opponent = snapshot.Players[1 - actor];
        if (ContainsEntity(opponent.Field, entityId)) return AttackTargetKind.BoardEntity;
        if (ContainsEntity(opponent.LeaderZone, entityId)) return AttackTargetKind.BoardEntity;
        return AttackTargetKind.Core;
    }

    /// <summary>
    /// The actor's public cloud stack depth, or 0 when the snapshot cannot
    /// answer. The engine only advertises actions for the player to act, so
    /// inside the policy's ordering path the actor is the snapshot's current
    /// player and this is the same read <see cref="AdvertisedActionPolicy"/>
    /// always used for a download.
    /// </summary>
    private static int OwnCloudStackCount(RuntimeSnapshotEnvelope snapshot, RuntimeLegalAction action)
    {
        if (snapshot.Players is null || snapshot.Players.Count != 2) return 0;
        var actor = action.Actor;
        if (actor is < 0 or > 1) return 0;
        return snapshot.Players[actor].CloudStackCount;
    }

    private static bool ContainsEntity(IReadOnlyList<RuntimeCardSnapshot>? cards, long entityId)
    {
        if (cards is null) return false;
        foreach (var card in cards)
        {
            if (card is not null && card.EntityId == entityId) return true;
        }

        return false;
    }

    private static bool TryEntityId(object? target, out long entityId)
    {
        entityId = 0;
        switch (target)
        {
            case null:
                return false;
            case long value:
                entityId = value;
                break;
            case int value:
                entityId = value;
                break;
            case short value:
                entityId = value;
                break;
            case byte value:
                entityId = value;
                break;
            case string text:
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    return false;
                entityId = parsed;
                break;
            case IConvertible convertible:
                // Covers the remaining wire shapes (float/double/decimal/uint,
                // and Newtonsoft's JValue), which all implement IConvertible.
                try
                {
                    entityId = convertible.ToInt64(CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    return false;
                }
                catch (InvalidCastException)
                {
                    return false;
                }
                catch (OverflowException)
                {
                    return false;
                }

                break;
            default:
                return false;
        }

        return entityId > 0;
    }

    private static int PayloadAmount(IReadOnlyDictionary<string, object?>? payload, string key)
    {
        if (payload is null || !payload.TryGetValue(key, out var raw) || raw is null) return 0;
        long amount;
        switch (raw)
        {
            case long value:
                amount = value;
                break;
            case int value:
                amount = value;
                break;
            case short value:
                amount = value;
                break;
            case byte value:
                amount = value;
                break;
            case IConvertible convertible:
                try
                {
                    amount = convertible.ToInt64(CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    return 0;
                }
                catch (InvalidCastException)
                {
                    return 0;
                }
                catch (OverflowException)
                {
                    return 0;
                }

                break;
            default:
                return 0;
        }

        if (amount <= 0) return 0;
        return amount > int.MaxValue ? int.MaxValue : (int)amount;
    }
}

}
