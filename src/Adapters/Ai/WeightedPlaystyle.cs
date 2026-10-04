using System;
using System.Collections.Generic;
using System.Globalization;
using DominionWars.Engine.Turns;

namespace DominionWars.Adapters.Ai
{

/// <summary>
/// The one concrete playstyle shape: score every advertised action as the
/// weighted sum of its pure feature vector and take the argmax, and carry the
/// stance this playstyle takes on the engine's optional punish
/// arrival/response (which is a different seam — see
/// <see cref="PunishResponseStance"/>).
///
/// <code>
/// score(action) = sum over features f:  weight[f] * ActionFeatures.Of(snapshot, action)[f]
/// winner        = argmax score
/// </code>
///
/// Determinism is a contract, not a hope. There is no RNG, no clock, and no
/// state between calls; the winner is decided by a total ordering, so the
/// result never depends on sort stability:
/// <list type="number">
/// <item>higher score first;</item>
/// <item>then the order the engine advertised (the action's position in
/// <see cref="RuntimeSnapshotEnvelope.LegalActions"/>, <c>int.MaxValue</c> for an
/// action that is not in that list);</item>
/// <item>then <see cref="RuntimeLegalAction.ActionId"/> ordinal;</item>
/// <item>then <see cref="RuntimeLegalAction.Type"/> ordinal.</item>
/// </list>
///
/// A playstyle can only reorder actions the engine already advertised: it
/// receives advertisements, returns one of them, and copies nothing. It never
/// sees GameState, never reads a rule out of card data, and never synthesises a
/// target, a card id, or a selectedEntityIds payload.
/// </summary>
public sealed class WeightedPlaystyle : IPlaystyle
{
    private readonly Dictionary<string, int> _weights;

    /// <summary>
    /// Builds a playstyle from an id, its punish-response stance, and its weight
    /// table. Every key must be a published feature name, so a misspelled weight
    /// fails loudly here instead of silently contributing nothing.
    /// </summary>
    public WeightedPlaystyle(
        string id,
        PunishResponseStance responseStance,
        IReadOnlyDictionary<string, int> weights)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A playstyle id is required.", nameof(id));
        if (weights is null) throw new ArgumentNullException(nameof(weights));

        _weights = new Dictionary<string, int>(weights.Count, StringComparer.Ordinal);
        foreach (var weight in weights)
        {
            if (string.IsNullOrWhiteSpace(weight.Key))
                throw new ArgumentException("A feature name is required.", nameof(weights));
            if (!IsPublishedFeature(weight.Key))
            {
                throw new ArgumentException(
                    "Unknown feature name '" + weight.Key
                    + "'; published names are: " + string.Join(", ", ActionFeatures.Names) + ".",
                    nameof(weights));
            }

            _weights[weight.Key] = weight.Value;
        }

        Id = id;
        ResponseStance = responseStance;
    }

    public string Id { get; }

    /// <summary>Feature name to weight; see <see cref="ActionFeatures.Names"/> for the key set.</summary>
    public IReadOnlyDictionary<string, int> Weights => _weights;

    /// <summary>
    /// How this playstyle treats the optional punish arrival/response the engine
    /// offers; applied through <c>IPunishResponsePolicy</c>, never through the
    /// weights.
    /// </summary>
    public PunishResponseStance ResponseStance { get; }

    /// <summary>
    /// The engine policy that implements this playstyle's response stance, ready
    /// to hand to <c>TurnActionRouter.CreateDefault(flow, punishResponses: …)</c>.
    /// </summary>
    public IPunishResponsePolicy PunishResponses => PunishResponseStances.PolicyFor(ResponseStance);

    /// <summary>
    /// The weighted score of one advertised action under this playstyle. Higher
    /// is preferred.
    /// </summary>
    public int Score(RuntimeSnapshotEnvelope snapshot, RuntimeLegalAction action)
        => WeightedScore(this, snapshot, action);

    /// <summary>
    /// Orders the advertisements this playstyle prefers first. Nulls are
    /// dropped; the returned list holds the same instances, in a total
    /// deterministic order, and is always the same length as the usable input.
    /// </summary>
    public IReadOnlyList<RuntimeLegalAction> Order(
        RuntimeSnapshotEnvelope snapshot,
        IReadOnlyList<RuntimeLegalAction> actions)
        => Order(this, snapshot, actions);

    /// <summary>
    /// Selects this playstyle's preferred advertisement: the argmax of the
    /// weighted score, with the documented tie-break. The caller decides which
    /// advertisements may be submitted at all (the policy filters the ones this
    /// boundary refuses); this method only ranks what it is given.
    /// </summary>
    public bool TryChoose(
        RuntimeSnapshotEnvelope snapshot,
        IReadOnlyList<RuntimeLegalAction> actions,
        out RuntimeLegalAction? chosen)
        => TryChoose(this, snapshot, actions, out chosen);

    /// <summary>Order one advertisement set under any playstyle.</summary>
    public static IReadOnlyList<RuntimeLegalAction> Order(
        IPlaystyle playstyle,
        RuntimeSnapshotEnvelope snapshot,
        IReadOnlyList<RuntimeLegalAction> actions)
    {
        if (playstyle is null) throw new ArgumentNullException(nameof(playstyle));
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (actions is null) throw new ArgumentNullException(nameof(actions));

        var pending = new List<RuntimeLegalAction>(actions.Count);
        foreach (var action in actions)
        {
            if (action is not null) pending.Add(action);
        }

        var sorted = new List<RuntimeLegalAction>(pending.Count);
        while (pending.Count > 0)
        {
            var best = 0;
            for (var index = 1; index < pending.Count; index++)
            {
                if (Precedes(playstyle, snapshot, pending[index], pending[best])) best = index;
            }

            sorted.Add(pending[best]);
            pending.RemoveAt(best);
        }

        return sorted;
    }

    /// <summary>Pick the argmax under any playstyle, with the documented tie-break.</summary>
    public static bool TryChoose(
        IPlaystyle playstyle,
        RuntimeSnapshotEnvelope snapshot,
        IReadOnlyList<RuntimeLegalAction> actions,
        out RuntimeLegalAction? chosen)
    {
        if (playstyle is null) throw new ArgumentNullException(nameof(playstyle));
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (actions is null) throw new ArgumentNullException(nameof(actions));

        chosen = null;
        foreach (var action in actions)
        {
            if (action is null) continue;
            if (chosen is null || Precedes(playstyle, snapshot, action, chosen)) chosen = action;
        }

        return chosen is not null;
    }

    /// <summary>
    /// The weighted sum of one action's feature vector. Iterating the feature
    /// vector (rather than the weight table) means an unpublished weight key
    /// cannot contribute, and the constructor already refuses those.
    /// </summary>
    public static int WeightedScore(
        IPlaystyle playstyle,
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction action)
    {
        if (playstyle is null) throw new ArgumentNullException(nameof(playstyle));
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (action is null) throw new ArgumentNullException(nameof(action));

        var features = ActionFeatures.Of(snapshot, action);
        var total = 0;
        foreach (var feature in features)
        {
            if (!playstyle.Weights.TryGetValue(feature.Key, out var weight)) continue;
            total += weight * feature.Value;
        }

        return total;
    }

    /// <summary>
    /// The playstyle's total order over two advertisements: score descending,
    /// then advertised position, then ActionId and Type. Every comparison is
    /// total, so the result never depends on sort stability.
    /// </summary>
    public static bool Precedes(
        IPlaystyle playstyle,
        RuntimeSnapshotEnvelope snapshot,
        RuntimeLegalAction left,
        RuntimeLegalAction right)
    {
        if (playstyle is null) throw new ArgumentNullException(nameof(playstyle));
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (left is null) throw new ArgumentNullException(nameof(left));
        if (right is null) throw new ArgumentNullException(nameof(right));

        var leftScore = WeightedScore(playstyle, snapshot, left);
        var rightScore = WeightedScore(playstyle, snapshot, right);
        if (leftScore != rightScore) return leftScore > rightScore;

        var advertised = AdvertisedIndex(snapshot, left).CompareTo(AdvertisedIndex(snapshot, right));
        if (advertised != 0) return advertised < 0;

        var id = string.Compare(left.ActionId, right.ActionId, StringComparison.Ordinal);
        if (id != 0) return id < 0;
        return string.Compare(left.Type, right.Type, StringComparison.Ordinal) < 0;
    }

    /// <summary>
    /// A stable provenance fingerprint of a playstyle's identity and weights
    /// (FNV-1a over the id and the feature weights in name order). Reports that
    /// quote a win rate must be able to state which weight vector produced it.
    /// </summary>
    public static string WeightFingerprint(IPlaystyle playstyle)
    {
        if (playstyle is null) throw new ArgumentNullException(nameof(playstyle));

        var keys = new List<string>(playstyle.Weights.Keys);
        keys.Sort(StringComparer.Ordinal);

        var hash = 14695981039346656037UL;
        hash = Mix(hash, playstyle.Id);
        foreach (var key in keys)
        {
            hash = Mix(hash, "|");
            hash = Mix(hash, key);
            hash = Mix(hash, "=");
            hash = Mix(hash, playstyle.Weights[key].ToString(CultureInfo.InvariantCulture));
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static bool IsPublishedFeature(string name)
    {
        foreach (var feature in ActionFeatures.Names)
        {
            if (string.Equals(feature, name, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static ulong Mix(ulong hash, string text)
    {
        foreach (var character in text)
        {
            hash ^= character;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    private static int AdvertisedIndex(RuntimeSnapshotEnvelope snapshot, RuntimeLegalAction action)
    {
        var actions = snapshot.LegalActions;
        if (actions is null) return int.MaxValue;
        for (var index = 0; index < actions.Count; index++)
        {
            if (ReferenceEquals(actions[index], action)) return index;
        }

        return int.MaxValue;
    }
}

}
