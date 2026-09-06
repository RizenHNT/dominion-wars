using System;

namespace DominionWars.Data.CardEditor
{
    /// <summary>
    /// Authoring lifecycle for a card document. This is editor metadata and is
    /// deliberately kept out of the runtime card JSON contract.
    /// </summary>
    public enum CardLifecycle
    {
        Draft,
        Ready,
        Approved,
        Deprecated
    }

    public static class CardLifecycleCodec
    {
        public static string ToWireValue(CardLifecycle lifecycle)
        {
            switch (lifecycle)
            {
                case CardLifecycle.Draft:
                    return "draft";
                case CardLifecycle.Ready:
                    return "ready";
                case CardLifecycle.Approved:
                    return "approved";
                case CardLifecycle.Deprecated:
                    return "deprecated";
                default:
                    throw new ArgumentOutOfRangeException(nameof(lifecycle), lifecycle, "Unknown card lifecycle.");
            }
        }

        public static bool TryParse(string? value, out CardLifecycle lifecycle)
        {
            switch (value)
            {
                case "draft":
                    lifecycle = CardLifecycle.Draft;
                    return true;
                case "ready":
                    lifecycle = CardLifecycle.Ready;
                    return true;
                case "approved":
                    lifecycle = CardLifecycle.Approved;
                    return true;
                case "deprecated":
                    lifecycle = CardLifecycle.Deprecated;
                    return true;
                default:
                    lifecycle = CardLifecycle.Approved;
                    return false;
            }
        }
    }
}
