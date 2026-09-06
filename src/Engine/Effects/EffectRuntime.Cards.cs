using System;
using System.Collections.Generic;
using DominionWars.Engine.Model;
using DominionWars.Engine.Turns;

namespace DominionWars.Engine.Effects
{

public sealed partial class EffectRuntime
{
    internal void DrawOpeningHand(int playerIndex, int amount, long rootEventId)
    {
        if (playerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(playerIndex));
        }

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (!State.Events.IsRootEvent(rootEventId))
        {
            throw new InvalidOperationException("Opening draw needs an existing root event.");
        }

        if (amount == 0)
        {
            return;
        }

        DrawCards(State.GetPlayer(playerIndex), amount, new EffectContext(playerIndex, rootEventId), false);
    }

    internal void DrawForTurn(int playerIndex, int amount, long rootEventId)
    {
        if (playerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(playerIndex));
        }

        if (amount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (!State.Events.IsRootEvent(rootEventId))
        {
            throw new InvalidOperationException("Turn draw needs an existing root event.");
        }

        DrawCards(State.GetPlayer(playerIndex), amount, new EffectContext(playerIndex, rootEventId), false);
    }

    internal IReadOnlyList<CardInstance> DrawForPunish(int playerIndex, int amount, long rootEventId)
    {
        if (playerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(playerIndex));
        }

        if (amount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (!State.Events.IsRootEvent(rootEventId))
        {
            throw new InvalidOperationException("Punish draw needs an existing root event.");
        }

        return DrawCards(
            State.GetPlayer(playerIndex),
            amount,
            new EffectContext(playerIndex, rootEventId),
            true);
    }

    public void Draw(EffectSpec spec, EffectContext context)
    {
        DrawCards(State.GetPlayer(context.SourcePlayerIndex), Math.Max(1, spec.Amount), context, false);
    }

    public void OppDraw(EffectSpec spec, EffectContext context)
    {
        DrawCards(State.GetOpponent(context.SourcePlayerIndex), Math.Max(1, spec.Amount), context, false);
    }

    public void DiscardOpponentRandom(EffectSpec spec, EffectContext context)
    {
        var player = State.GetOpponent(context.SourcePlayerIndex);
        var amount = Math.Max(0, spec.Amount);
        var discarded = new List<long>();
        Commit(_ =>
        {
            for (var index = 0; index < amount && player.Hand.Count > 0; index++)
            {
                var selected = State.Random.NextInt(player.Hand.Count);
                var card = player.Hand[selected];
                player.Hand.RemoveAt(selected);
                player.Graveyard.Add(card);
                player.TotalDiscarded++;
                discarded.Add(card.InstanceId);
            }
        });
        Emit("CARDS_DISCARDED", context, Data(
            "player", player.PlayerIndex,
            "count", discarded.Count,
            "reasonKey", "effect.discard"));
    }

    public void DiscardDrawn(EffectSpec spec, EffectContext context)
    {
        var discarded = 0;
        Commit(_ =>
        {
            foreach (var card in context.DrawnCards)
            {
                var owner = State.GetPlayer(card.OwnerPlayerIndex);
                if (!owner.Hand.Remove(card))
                {
                    continue;
                }

                owner.Graveyard.Add(card);
                owner.TotalDiscarded++;
                discarded++;
            }
        });
        Emit("CARDS_DISCARDED", context, Data(
            "count", discarded,
            "reasonKey", "effect.abyss_consume"));
    }

    public void Summon(EffectSpec spec, EffectContext context)
    {
        var definition = FindDefinition(spec.Param);
        if (definition is null || !definition.IsMinion)
        {
            EmitSkipped(context, spec.Action, "effect.summon_invalid_card");
            return;
        }

        var owner = State.GetPlayer(context.SourcePlayerIndex);
        var amount = Math.Max(1, spec.Amount);
        for (var index = 0; index < amount; index++)
        {
            CardInstance? summoned = null;
            Commit(_ =>
            {
                summoned = new CardInstance(State.AllocateEntityId(), owner.PlayerIndex, definition)
                {
                    SummonedThisTurn = true,
                };
                owner.Field.Add(summoned);
            });
            Emit("MINION_SUMMONED", context, Data(
                "target", summoned!.InstanceId,
                "cardId", definition.Id));
        }
    }

    public void SummonLeader(EffectSpec spec, EffectContext context)
    {
        var definition = FindDefinition(spec.Param);
        if (definition is null || !definition.IsLeader)
        {
            EmitSkipped(context, spec.Action, "effect.leader_not_found");
            return;
        }

        var owner = State.GetPlayer(context.SourcePlayerIndex);
        if (owner.HasMultipleActiveLeaders)
        {
            return;
        }

        CardInstance? oldLeader = null;
        CardInstance? newLeader = null;
        Commit(_ =>
        {
            oldLeader = owner.Leader;
            if (oldLeader is not null)
            {
                RemoveReferences(owner.Field, oldLeader);
                RemoveReferences(owner.LeaderZone, oldLeader);
                RemoveReferences(owner.AmbushZone, oldLeader);
                owner.Graveyard.Add(oldLeader);
            }

            newLeader = new CardInstance(State.AllocateEntityId(), owner.PlayerIndex, definition)
            {
                IsLeaderEntity = true,
            };
            LeaderZoneFor(owner, newLeader).Add(newLeader);
            if (definition.GrantLife > 0)
            {
                owner.Life = definition.GrantLife;
            }
        });
        Emit("LEADER_REPLACED", context, Data(
            "oldLeader", oldLeader?.InstanceId,
            "newLeader", newLeader!.InstanceId,
            "cardId", definition.Id));
    }

    private IReadOnlyList<CardInstance> DrawCards(
        PlayerState player,
        int amount,
        EffectContext context,
        bool byPunish)
    {
        var drawn = new List<long>();
        var drawnCards = new List<CardInstance>();
        for (var index = 0; index < amount && !IsGameOver; index++)
        {
            if (player.Deck.Count == 0 && !Reshuffle(player, context))
            {
                break;
            }

            var topCard = player.Deck[^1];
            var activeLeader = player.Leader;
            if (topCard.Definition.IsLeader
                && (player.HasMultipleActiveLeaders
                    || (activeLeader is not null && !ReferenceEquals(activeLeader, topCard))))
            {
                break;
            }

            CardInstance? card = null;
            Commit(_ =>
            {
                card = player.Deck[player.Deck.Count - 1];
                player.Deck.RemoveAt(player.Deck.Count - 1);
                if (!card.Definition.IsLeader)
                {
                    card.PunishActivated = byPunish
                        && (card.Definition.PunishActivatable
                            || string.Equals(card.Definition.Type, "PUNISH", StringComparison.Ordinal));
                    player.Hand.Add(card);
                    drawn.Add(card.InstanceId);
                    drawnCards.Add(card);
                }

                if (byPunish)
                {
                    player.PunishDrawnThisTurn++;
                }
            });

            if (card!.Definition.IsLeader)
            {
                ManifestLeader(player, card, context, byPunish);
            }
        }

        Emit(byPunish ? "PUNISH_DRAW" : "CARDS_DRAWN", context, Data(
            "player", player.PlayerIndex,
            "count", drawn.Count,
            "byPunish", byPunish));
        if (drawnCards.Count > 0 && !IsGameOver)
        {
            new AmbushTriggerResolver().Resolve(
                State,
                player.PlayerIndex,
                new[] { "OPPONENT_DRAWS" },
                context.RootEventId,
                drawnCards: drawnCards.AsReadOnly());
        }
        return drawnCards.AsReadOnly();
    }

    private bool ManifestLeader(
        PlayerState player,
        CardInstance leader,
        EffectContext context,
        bool byPunish)
    {
        if (player.HasMultipleActiveLeaders)
        {
            return false;
        }

        var existingLeader = player.Leader;
        if (existingLeader is not null && !ReferenceEquals(existingLeader, leader))
        {
            return false;
        }

        var destination = LeaderZoneFor(player, leader);
        var alreadyManifested = leader.IsLeaderEntity
            && (ContainsReference(player.Field, leader)
                || ContainsReference(player.LeaderZone, leader)
                || ContainsReference(player.AmbushZone, leader));

        Commit(_ =>
        {
            RemoveReferences(player.Hand, leader);
            RemoveReferences(player.Deck, leader);
            RemoveReferences(player.Graveyard, leader);
            RemoveReferences(player.CommitQueue, leader);
            RemoveReferences(player.CloudStack, leader);
            RemoveReferences(player.Field, leader);
            RemoveReferences(player.LeaderZone, leader);
            RemoveReferences(player.AmbushZone, leader);

            if (!alreadyManifested)
            {
                leader.ResetRuntimeState();
                leader.IsLeaderEntity = true;
                leader.ChantRemaining = leader.Definition.Chant;
                leader.SummonedThisTurn = leader.Definition.IsMinion;
                leader.AttacksUsed = 0;
                if (leader.Definition.GrantLife > 0)
                {
                    player.Life = leader.Definition.GrantLife;
                }
            }

            destination.Add(leader);
        });

        if (alreadyManifested)
        {
            return true;
        }

        Emit("LEADER_MANIFESTED", context, Data(
            "player", player.PlayerIndex,
            "target", leader.InstanceId));
        var leaderContext = new EffectContext(
            player.PlayerIndex,
            context.RootEventId,
            sourceCard: leader,
            playedCard: context.PlayedCard,
            drawnCards: context.DrawnCards);
        var dispatcher = EffectDispatcher.CreateDefault(this);
        if (leader.Definition.LeaderEnterEffects.Count > 0)
        {
            dispatcher.ApplyAll(leader.Definition.LeaderEnterEffects, leaderContext);
        }

        if (byPunish && leader.Definition.LeaderPunishEffects.Count > 0 && !IsGameOver)
        {
            dispatcher.ApplyAll(leader.Definition.LeaderPunishEffects, leaderContext);
        }

        return true;
    }

    private static IList<CardInstance> LeaderZoneFor(PlayerState player, CardInstance leader)
    {
        if (leader.Definition.IsMinion)
        {
            return player.Field;
        }

        return string.Equals(leader.Definition.Type, "AMBUSH", StringComparison.OrdinalIgnoreCase)
            ? player.AmbushZone
            : player.LeaderZone;
    }

    private static bool ContainsReference(IList<CardInstance> zone, CardInstance card)
    {
        foreach (var candidate in zone)
        {
            if (ReferenceEquals(candidate, card))
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveReferences(IList<CardInstance> zone, CardInstance card)
    {
        for (var index = zone.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(zone[index], card))
            {
                zone.RemoveAt(index);
            }
        }
    }

    private bool Reshuffle(PlayerState player, EffectContext context)
    {
        if (player.Graveyard.Count == 0)
        {
            EmitSkipped(context, EffectNames.Draw, "deck.empty");
            return false;
        }

        var counted = false;
        Commit(_ =>
        {
            foreach (var card in player.Graveyard)
            {
                card.ResetRuntimeState();
                player.Deck.Add(card);
            }

            player.Graveyard.Clear();
            for (var index = player.Deck.Count - 1; index > 0; index--)
            {
                var selected = State.Random.NextInt(index + 1);
                var temporary = player.Deck[index];
                player.Deck[index] = player.Deck[selected];
                player.Deck[selected] = temporary;
            }

            if (player.SkipReshuffleCredits > 0)
            {
                player.SkipReshuffleCredits--;
            }
            else
            {
                counted = true;
                player.ReshuffleCount++;
                player.CycleWinCount++;
            }
        });
        Emit("DECK_CYCLED", context, Data(
            "player", player.PlayerIndex,
            "counted", counted));
        if (counted && player.CycleWinCount >= State.ReshuffleLossThreshold)
        {
            DeclareWinner(player.PlayerIndex, "win.deck_cycles", context);
        }

        return true;
    }
}
}
