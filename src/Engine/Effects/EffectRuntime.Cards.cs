using System;
using System.Collections.Generic;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Effects
{

public sealed partial class EffectRuntime
{
    public void Draw(EffectSpec spec, EffectContext context)
    {
        DrawCards(State.GetPlayer(context.SourcePlayerIndex), Math.Max(1, spec.Amount), context);
    }

    public void OppDraw(EffectSpec spec, EffectContext context)
    {
        DrawCards(State.GetOpponent(context.SourcePlayerIndex), Math.Max(1, spec.Amount), context);
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
        CardInstance? oldLeader = null;
        CardInstance? newLeader = null;
        Commit(_ =>
        {
            oldLeader = owner.Leader;
            if (oldLeader is not null)
            {
                owner.Field.Remove(oldLeader);
                owner.Graveyard.Add(oldLeader);
            }

            newLeader = new CardInstance(State.AllocateEntityId(), owner.PlayerIndex, definition)
            {
                IsLeaderEntity = true,
            };
            owner.Field.Add(newLeader);
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

    private void DrawCards(PlayerState player, int amount, EffectContext context)
    {
        var drawn = new List<long>();
        for (var index = 0; index < amount && !IsGameOver; index++)
        {
            if (player.Deck.Count == 0 && !Reshuffle(player, context))
            {
                break;
            }

            CardInstance? card = null;
            Commit(_ =>
            {
                card = player.Deck[player.Deck.Count - 1];
                player.Deck.RemoveAt(player.Deck.Count - 1);
                if (card.Definition.IsLeader)
                {
                    card.IsLeaderEntity = true;
                    player.Field.Add(card);
                    if (card.Definition.GrantLife > 0)
                    {
                        player.Life = card.Definition.GrantLife;
                    }
                }
                else
                {
                    player.Hand.Add(card);
                    drawn.Add(card.InstanceId);
                }
            });

            if (card!.Definition.IsLeader)
            {
                Emit("LEADER_MANIFESTED", context, Data(
                    "player", player.PlayerIndex,
                    "target", card.InstanceId));
            }
        }

        Emit("CARDS_DRAWN", context, Data("player", player.PlayerIndex, "count", drawn.Count));
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
                State.GetOpponent(player.PlayerIndex).CycleWinCount++;
            }
        });
        Emit("DECK_CYCLED", context, Data(
            "player", player.PlayerIndex,
            "counted", counted));
        return true;
    }
}
}
