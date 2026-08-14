using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

/// <summary>
/// The resolved cost of one card play. MVP only consumes EffectivePunish;
/// ResourceCosts is a forward-compatible seam for expansion-set economies.
/// </summary>
public sealed class CardPlayCost
{
    public CardPlayCost(
        int effectivePunish,
        IReadOnlyDictionary<string, int>? resourceCosts = null)
    {
        if (effectivePunish < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(effectivePunish));
        }

        var resources = new Dictionary<string, int>(StringComparer.Ordinal);
        if (resourceCosts is not null)
        {
            foreach (var entry in resourceCosts)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    throw new ArgumentException("Resource cost names cannot be empty.", nameof(resourceCosts));
                }

                if (entry.Value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(resourceCosts));
                }

                resources.Add(entry.Key, entry.Value);
            }
        }

        EffectivePunish = effectivePunish;
        ResourceCosts = new ReadOnlyDictionary<string, int>(resources);
    }

    public int EffectivePunish { get; }

    /// <summary>Named expansion resources; empty until a resource system is approved.</summary>
    public IReadOnlyDictionary<string, int> ResourceCosts { get; }

    public bool HasUnsupportedResources => ResourceCosts.Count != 0;

    public static CardPlayCost PunishOnly(int effectivePunish)
        => new CardPlayCost(effectivePunish);
}

/// <summary>Resolves costs without making a particular economy part of the card handler.</summary>
public interface ICardCostModel
{
    CardPlayCost Evaluate(GameState state, PlayerState player, CardInstance card);
}

/// <summary>The current MVP cost policy: punish is the only active card-play cost.</summary>
public sealed class PunishOnlyCostModel : ICardCostModel
{
    public static PunishOnlyCostModel Instance { get; } = new PunishOnlyCostModel();

    private PunishOnlyCostModel()
    {
    }

    public CardPlayCost Evaluate(GameState state, PlayerState player, CardInstance card)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (card is null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        return CardPlayCost.PunishOnly(CardPlayRules.EffectivePunish(player, card));
    }
}
}
