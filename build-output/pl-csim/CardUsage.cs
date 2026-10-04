using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DominionWars.Engine.Events;
using DominionWars.Engine.Model;

namespace PlCsim
{
    /// <summary>
    /// One row of card-usage telemetry: what a card actually did in real
    /// matches, independent of what the model says about it.
    ///
    /// Every event the engine emits already carries the acting player index, so
    /// usage is attributed to (card id, faction) rather than to a playstyle
    /// seat: at 720 games per cell a faction appears as both P0 and P1, so a
    /// per-seat split would fragment the sample without adding information.
    /// "Kinds seen" is the denominator that makes the counts readable: a card
    /// with 0 plays out of 360 is dead, one with 0 plays out of 0 was never even
    /// drawn.
    /// </summary>
    internal sealed class CardUsage
    {
        public string CardId { get; set; } = string.Empty;
        public string Playstyle { get; set; } = string.Empty;
        public string Faction { get; set; } = string.Empty;

        public long Played;
        public long Committed;
        public long Pushed;
        public long Pulled;
        public long PunishActivated;
        public long RolledBack;
        public long WinsHolding;
        public long GamesWithFaction;
        public long GamesWonByFaction;

        /// <summary>Distinct matches in which this card id was observed in the faction's deck.</summary>
        public long MatchesDrawn;

        /// <summary>The deck copies of this card id in the faction's shipped deck.</summary>
        public int DeckCopies;

        public long TotalUses => Played + Committed + Pulled + PunishActivated;
    }

    internal sealed class CardUsageTable
    {
        private readonly Dictionary<string, CardUsage> _rows = new Dictionary<string, CardUsage>(StringComparer.Ordinal);

        public string Playstyle { get; set; } = string.Empty;

        public CardUsage Row(string cardId, string faction)
        {
            var key = cardId + "|" + faction;
            if (!_rows.TryGetValue(key, out var row))
            {
                row = new CardUsage { CardId = cardId, Faction = faction, Playstyle = Playstyle };
                _rows[key] = row;
            }

            return row;
        }

        public IEnumerable<CardUsage> Rows => _rows.Values;

        public void Merge(CardUsageTable other)
        {
            foreach (var row in other._rows.Values)
            {
                var mine = Row(row.CardId, row.Faction);
                mine.Played += row.Played;
                mine.Committed += row.Committed;
                mine.Pushed += row.Pushed;
                mine.Pulled += row.Pulled;
                mine.PunishActivated += row.PunishActivated;
                mine.RolledBack += row.RolledBack;
                mine.WinsHolding += row.WinsHolding;
                mine.GamesWithFaction += row.GamesWithFaction;
                mine.GamesWonByFaction += row.GamesWonByFaction;
                mine.MatchesDrawn += row.MatchesDrawn;
                mine.DeckCopies = Math.Max(mine.DeckCopies, row.DeckCopies);
            }
        }

        /// <summary>
        /// Scans one finished match's event log and credits every usage event to
        /// the card that caused it. Uses only published event data.
        /// </summary>
        public void Scan(GameState state, DeckSpec player0, DeckSpec player1)
        {
            var events = state.Events.Items;

            // Which faction is which seat, and who won.
            var factionBySeat = new[] { player0.Faction, player1.Faction };
            var winnerSeat = state.WinnerPlayerIndex ?? -1;

            // Card instance -> definition id, learned from the deck listing plus
            // every event that names a source card.
            var cardIdByInstance = new Dictionary<long, string>();

            for (var seat = 0; seat < 2; seat++)
            {
                var spec = seat == 0 ? player0 : player1;
                foreach (var pair in spec.Cards)
                {
                    Row(pair.Key, spec.Faction).DeckCopies = pair.Value;
                }

                Row(spec.Leader, spec.Faction).DeckCopies = Math.Max(1, Row(spec.Leader, spec.Faction).DeckCopies);

                var player = state.GetPlayer(seat);
                foreach (var card in player.Deck) cardIdByInstance[card.InstanceId] = card.Definition.Id;
                foreach (var card in player.Graveyard) cardIdByInstance[card.InstanceId] = card.Definition.Id;
                foreach (var card in player.CommitQueue) cardIdByInstance[card.InstanceId] = card.Definition.Id;
                foreach (var card in player.CloudStack) cardIdByInstance[card.InstanceId] = card.Definition.Id;
                foreach (var card in player.Field) cardIdByInstance[card.InstanceId] = card.Definition.Id;
                foreach (var card in player.LeaderZone) cardIdByInstance[card.InstanceId] = card.Definition.Id;
            }

            // Track which card ids each faction actually saw during the match.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var seat = 0; seat < 2; seat++)
            {
                var spec = seat == 0 ? player0 : player1;
                var player = state.GetPlayer(seat);
                foreach (var zone in new[] { player.Hand, player.Field, player.LeaderZone, player.Graveyard, player.CommitQueue, player.CloudStack, player.AmbushZone })
                {
                    foreach (var card in zone) seen.Add(spec.Faction + "|" + card.Definition.Id);
                }
            }

            foreach (var key in seen)
            {
                var split = key.Split('|');
                Row(split[1], split[0]).MatchesDrawn++;
            }

            foreach (var item in events)
            {
                var cardId = ResolveCardId(item, cardIdByInstance);
                var seat = ResolveActingSeat(item);
                if (cardId is null || seat < 0) continue;
                if (seat > 1) seat = seat; // guard for readability
                var faction = factionBySeat[seat];
                var row = Row(cardId, faction);

                switch (item.EventType)
                {
                    case "CARD_PLAYED":
                        row.Played++;
                        if (ReadBool(item, "punishActivated")) row.PunishActivated++;
                        break;
                    case "PUNISH_TRIGGERED":
                        row.PunishActivated++;
                        break;
                    case "CARD_COMMITTED":
                        row.Committed++;
                        break;
                    case "CARD_PUSHED":
                        row.Pushed++;
                        break;
                    case "CARD_PULLED":
                        row.Pulled++;
                        break;
                    case "CARD_ROLLED_BACK":
                        row.RolledBack++;
                        break;
                    case "AMBUSH_SET":
                        row.Played++;
                        break;
                }
            }

            // Wins "by the side holding it": credited to every card id present in
            // the winning faction's deck list, with a per-faction denominator so
            // the rate is comparable across factions.
            for (var seat = 0; seat < 2; seat++)
            {
                var spec = seat == 0 ? player0 : player1;
                foreach (var pair in spec.Cards) Row(pair.Key, spec.Faction).GamesWithFaction++;
                Row(spec.Leader, spec.Faction).GamesWithFaction++;
                if (winnerSeat == seat)
                {
                    foreach (var pair in spec.Cards) Row(pair.Key, spec.Faction).GamesWonByFaction++;
                    Row(spec.Leader, spec.Faction).GamesWonByFaction++;
                }
            }
        }

        private static string? ResolveCardId(GameEvent item, Dictionary<long, string> byInstance)
        {
            if (item.Data is null) return null;
            if (item.Data.TryGetValue("cardId", out var raw) && raw is string text && text.Length > 0) return text;
            foreach (var key in new[] { "source", "target", "carrier" })
            {
                if (!item.Data.TryGetValue(key, out var value) || value is null) continue;
                long id;
                try
                {
                    id = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                }
                catch (Exception exception) when (exception is FormatException || exception is InvalidCastException || exception is OverflowException)
                {
                    continue;
                }

                if (byInstance.TryGetValue(id, out var cardId)) return cardId;
            }

            return null;
        }

        private static int ResolveActingSeat(GameEvent item)
        {
            if (item.Data is null) return -1;
            foreach (var key in new[] { "player", "owner" })
            {
                if (!item.Data.TryGetValue(key, out var raw) || raw is null) continue;
                try
                {
                    var seat = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                    if (seat is 0 or 1) return seat;
                }
                catch (Exception exception) when (exception is FormatException || exception is InvalidCastException || exception is OverflowException)
                {
                }
            }

            return -1;
        }

        private static bool ReadBool(GameEvent item, string key)
        {
            if (item.Data is null || !item.Data.TryGetValue(key, out var raw) || raw is null) return false;
            return raw is bool flag && flag;
        }
    }

    internal static class CardUsageCsv
    {
        public const string Header =
            "playstyle,card_id,faction,deck_copies,matches_drawn,played,committed,pushed,pulled,punish_activated,rolled_back,total_uses,matches_faction,games_won_by_faction,win_rate_holding";

        public static string Line(CardUsage row)
        {
            var rate = row.GamesWithFaction == 0
                ? 0.0
                : row.GamesWonByFaction / (double)row.GamesWithFaction;
            var parts = new[]
            {
                row.Playstyle,
                row.CardId,
                row.Faction,
                row.DeckCopies.ToString(CultureInfo.InvariantCulture),
                row.MatchesDrawn.ToString(CultureInfo.InvariantCulture),
                row.Played.ToString(CultureInfo.InvariantCulture),
                row.Committed.ToString(CultureInfo.InvariantCulture),
                row.Pushed.ToString(CultureInfo.InvariantCulture),
                row.Pulled.ToString(CultureInfo.InvariantCulture),
                row.PunishActivated.ToString(CultureInfo.InvariantCulture),
                row.RolledBack.ToString(CultureInfo.InvariantCulture),
                row.TotalUses.ToString(CultureInfo.InvariantCulture),
                row.GamesWithFaction.ToString(CultureInfo.InvariantCulture),
                row.GamesWonByFaction.ToString(CultureInfo.InvariantCulture),
                rate.ToString("F4", CultureInfo.InvariantCulture),
            };
            return string.Join(",", parts.Select(Escape));
        }

        public static string Write(IEnumerable<CardUsage> rows, string playstyleFilter)
        {
            var builder = new StringBuilder();
            builder.AppendLine(Header);
            foreach (var row in rows
                         .Where(r => playstyleFilter.Length == 0 || string.Equals(r.Playstyle, playstyleFilter, StringComparison.Ordinal))
                         .OrderBy(r => r.CardId, StringComparer.Ordinal)
                         .ThenBy(r => r.Playstyle, StringComparer.Ordinal))
            {
                builder.AppendLine(Line(row));
            }

            return builder.ToString();
        }

        private static string Escape(string value) =>
            value.IndexOfAny(new[] { ',', '"', '\n' }) < 0
                ? value
                : "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
