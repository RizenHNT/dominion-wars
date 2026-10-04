#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DominionWars.Adapters;
using DominionWars.Unity.Runtime;
using UnityEngine;

namespace DominionWars.Unity.UI
{

/// <summary>
/// The small semantic vocabulary used by the neutral battle feedback layer.
/// This is presentation metadata only; it is not a gameplay action or rule.
/// </summary>
public enum RuntimeBattlePanelFeedbackKind
{
    None,
    CardPlayed,
    AttackDeclared,
    DamageApplied,
    CardsDrawn,
    AmbushSet,
    AmbushTriggered,
    Commit,
    Push,
    Pull,
    Punish,
    CastleDamaged,
    CastleBroken,
    PhaseChanged,
    TurnStarted,
    TurnEnded,
    PlayerSwitched,
    HealApplied,
    Death,
    GameOver,
}

/// <summary>
/// A renderer-safe cue projected from one adapter event. The cue intentionally
/// carries no event Data payload, so private/raw engine values cannot leak into
/// the feedback surface.
/// </summary>
public sealed class RuntimeBattlePanelFeedbackCue
{
    internal RuntimeBattlePanelFeedbackCue(
        string eventId,
        string eventType,
        long snapshotRevision,
        RuntimeBattlePanelFeedbackKind kind,
        string message,
        Color accent,
        int targetCount)
    {
        EventId = eventId ?? string.Empty;
        EventType = eventType ?? string.Empty;
        SnapshotRevision = snapshotRevision;
        Kind = kind;
        Message = message ?? string.Empty;
        Accent = accent;
        TargetCount = Math.Max(0, targetCount);
    }

    public string EventId { get; }
    public string EventType { get; }
    public long SnapshotRevision { get; }
    public RuntimeBattlePanelFeedbackKind Kind { get; }
    public string Message { get; }
    public Color Accent { get; }
    public int TargetCount { get; }
    public bool HasTarget => TargetCount > 0;
}

/// <summary>
/// Maps only events already exposed by RuntimeAdapter.Presentation.Events to
/// short, neutral visual cues. No snapshot delta is used to infer an event.
/// CARDS_DRAWN is accepted for forward compatibility. The
/// currently visible CARD_PULLED event remains distinct because it reports
/// the formal PULL action, not an ordinary draw.
/// </summary>
public static class RuntimeBattlePanelActionFeedbackModel
{
    private static readonly Color CardPlayedAccent = Hex("63D7E5");
    private static readonly Color AttackAccent = Hex("E36D78");
    private static readonly Color DamageAppliedAccent = Hex("F0A35B");
    private static readonly Color CardsDrawnAccent = Hex("E3B85A");
    private static readonly Color AmbushSetAccent = Hex("B18BE8");
    private static readonly Color AmbushTriggeredAccent = Hex("F05B9D");
    private static readonly Color CommitAccent = Hex("63D7E5");
    private static readonly Color PushAccent = Hex("67D39B");
    private static readonly Color PullAccent = Hex("8DBBE8");
    private static readonly Color PunishAccent = Hex("B18BE8");
    private static readonly Color CastleDamagedAccent = Hex("E8A15A");
    private static readonly Color CastleBrokenAccent = Hex("F05B68");
    private static readonly Color PhaseChangedAccent = Hex("67D39B");
    private static readonly Color TurnAccent = Hex("67D39B");
    private static readonly Color PlayerSwitchAccent = Hex("7FB3FF");
    private static readonly Color HealAccent = Hex("6BE0A2");
    private static readonly Color DeathAccent = Hex("D86678");
    private static readonly Color GameOverAccent = Hex("F4D35E");
    private static readonly RuntimeLocalizationResolver DefaultLocalizationResolver =
        new RuntimeLocalizationResolver();

    public static bool TryMap(
        RuntimeEventEnvelope eventEnvelope,
        out RuntimeBattlePanelFeedbackCue cue)
    {
        return TryMap(eventEnvelope, "en", DefaultLocalizationResolver, out cue);
    }

    /// <summary>
    /// Maps a public event with an explicit language while retaining the
    /// existing default resolver behavior for callers that do not own a
    /// localization source yet.
    /// </summary>
    public static bool TryMap(
        RuntimeEventEnvelope eventEnvelope,
        string language,
        out RuntimeBattlePanelFeedbackCue cue)
    {
        return TryMap(eventEnvelope, language, DefaultLocalizationResolver, out cue);
    }

    /// <summary>
    /// Maps a public event through the caller's presentation resolver. The
    /// resolver only supplies copy; event ordering, severity and target
    /// presence still come from the authoritative envelope.
    /// </summary>
    public static bool TryMap(
        RuntimeEventEnvelope eventEnvelope,
        string language,
        RuntimeLocalizationResolver localizationResolver,
        out RuntimeBattlePanelFeedbackCue cue)
    {
        cue = null;
        if (eventEnvelope == null) return false;
        if (localizationResolver == null)
            throw new ArgumentNullException(nameof(localizationResolver));

        var eventType = NormalizeType(eventEnvelope.Type);
        var kind = KindFor(eventEnvelope, eventType);
        if (kind == RuntimeBattlePanelFeedbackKind.None) return false;

        var targetCount = eventEnvelope.TargetIds == null ? 0 : eventEnvelope.TargetIds.Count;
        var missingRequiredTarget = RequiresTarget(kind) && targetCount == 0;
        var message = MessageFor(
            eventEnvelope,
            kind,
            eventType,
            missingRequiredTarget,
            localizationResolver,
            language);
        cue = new RuntimeBattlePanelFeedbackCue(
            eventEnvelope.EventId,
            eventType,
            eventEnvelope.SnapshotRevision,
            kind,
            message,
            AccentFor(kind),
            targetCount);
        return true;
    }

    /// <summary>
    /// Produces a bounded identity for de-duplication when an event lacks its
    /// normal stable EventId. It never reads Data and treats repeated malformed
    /// entries conservatively as one visual event.
    /// </summary>
    public static string StableEventKey(RuntimeEventEnvelope eventEnvelope)
    {
        if (eventEnvelope == null) return "null-event";
        if (!string.IsNullOrWhiteSpace(eventEnvelope.EventId))
            return "id:" + SafeToken(eventEnvelope.EventId, 96);

        var builder = new StringBuilder("anonymous:");
        builder.Append(SafeToken(eventEnvelope.Type, 48)).Append('|')
            .Append(eventEnvelope.Turn.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(SafeToken(eventEnvelope.Phase, 48)).Append('|')
            .Append(eventEnvelope.SnapshotRevision.ToString(CultureInfo.InvariantCulture)).Append('|');
        var targets = eventEnvelope.TargetIds;
        if (targets != null)
        {
            for (var index = 0; index < targets.Count; index++)
            {
                if (index > 0) builder.Append(',');
                builder.Append(SafeToken(ConvertTarget(targets[index]), 32));
            }
        }
        return builder.ToString();
    }

    private static RuntimeBattlePanelFeedbackKind KindFor(
        RuntimeEventEnvelope eventEnvelope,
        string eventType)
    {
        if (eventType == "CARD_PLAYED") return RuntimeBattlePanelFeedbackKind.CardPlayed;
        if (eventType == "ATTACK_DECLARED") return RuntimeBattlePanelFeedbackKind.AttackDeclared;
        if (eventType == "DAMAGE_APPLIED") return RuntimeBattlePanelFeedbackKind.DamageApplied;
        if (eventType == "CARDS_DRAWN" || eventType == "CARD_DRAWN")
            return RuntimeBattlePanelFeedbackKind.CardsDrawn;
        if (eventType == "TURN_STARTED") return RuntimeBattlePanelFeedbackKind.TurnStarted;
        if (eventType == "TURN_ENDED") return RuntimeBattlePanelFeedbackKind.TurnEnded;
        if (eventType == "TURN_CHANGED") return RuntimeBattlePanelFeedbackKind.PlayerSwitched;
        if (eventType == "AMBUSH_SET") return RuntimeBattlePanelFeedbackKind.AmbushSet;
        if (eventType == "AMBUSH_TRIGGERED") return RuntimeBattlePanelFeedbackKind.AmbushTriggered;
        if (eventType == "CARD_COMMITTED") return RuntimeBattlePanelFeedbackKind.Commit;
        if (eventType == "CARD_PUSHED") return RuntimeBattlePanelFeedbackKind.Push;
        if (eventType == "CARD_PULLED") return RuntimeBattlePanelFeedbackKind.Pull;
        if (eventType == "HEAL_APPLIED") return RuntimeBattlePanelFeedbackKind.HealApplied;
        if (eventType == "MINION_DESTROYED" || eventType == "MINION_DIED")
            return RuntimeBattlePanelFeedbackKind.Death;
        if (eventType.StartsWith("PUNISH_", StringComparison.Ordinal))
            return RuntimeBattlePanelFeedbackKind.Punish;
        if (eventType == "CASTLE_DAMAGED") return RuntimeBattlePanelFeedbackKind.CastleDamaged;
        if (eventType == "CASTLE_BROKEN") return RuntimeBattlePanelFeedbackKind.CastleBroken;
        if (eventType == "GAME_OVER") return RuntimeBattlePanelFeedbackKind.GameOver;
        if (eventType == "PHASE_CHANGED")
        {
            var phase = PhaseTokenFor(eventEnvelope);
            if (phase == "START") return RuntimeBattlePanelFeedbackKind.TurnStarted;
            if (phase == "END") return RuntimeBattlePanelFeedbackKind.TurnEnded;
            return RuntimeBattlePanelFeedbackKind.PhaseChanged;
        }
        return RuntimeBattlePanelFeedbackKind.None;
    }

    private static string MessageFor(
        RuntimeEventEnvelope eventEnvelope,
        RuntimeBattlePanelFeedbackKind kind,
        string eventType,
        bool missingRequiredTarget,
        RuntimeLocalizationResolver localizationResolver,
        string language)
    {
        if (kind == RuntimeBattlePanelFeedbackKind.DamageApplied)
            return DamageMessageFor(eventEnvelope, localizationResolver, language);
        if (kind == RuntimeBattlePanelFeedbackKind.CardsDrawn)
            return CardsDrawnMessageFor(eventEnvelope, localizationResolver, language);
        if (kind == RuntimeBattlePanelFeedbackKind.HealApplied)
            return HealMessageFor(eventEnvelope, localizationResolver, language);
        if (kind == RuntimeBattlePanelFeedbackKind.Death)
            return DeathMessageFor(eventEnvelope, localizationResolver, language);
        if (kind == RuntimeBattlePanelFeedbackKind.PlayerSwitched)
            return PlayerSwitchMessageFor(eventEnvelope, localizationResolver, language);

        var message = kind switch
        {
            RuntimeBattlePanelFeedbackKind.CardPlayed => EventText(
                localizationResolver, eventType, language, "CARD PLAYED"),
            RuntimeBattlePanelFeedbackKind.AttackDeclared => EventText(
                localizationResolver, eventType, language, "ATTACK DECLARED"),
            RuntimeBattlePanelFeedbackKind.AmbushSet => EventText(
                localizationResolver, eventType, language, "AMBUSH SET"),
            RuntimeBattlePanelFeedbackKind.AmbushTriggered => EventText(
                localizationResolver, eventType, language, "AMBUSH TRIGGERED"),
            RuntimeBattlePanelFeedbackKind.Commit => EventText(
                localizationResolver, eventType, language, "CARD COMMITTED"),
            RuntimeBattlePanelFeedbackKind.Push => EventText(
                localizationResolver, eventType, language, "CARD PUSHED"),
            RuntimeBattlePanelFeedbackKind.Pull => EventText(
                localizationResolver, eventType, language, "CARD PULLED"),
            // The event subtype is an internal protocol token. Keep the
            // player cue semantic and leave the complete subtype in the
            // diagnostic cue fields for an explicitly enabled debug view.
            RuntimeBattlePanelFeedbackKind.Punish => EventText(
                localizationResolver, eventType, language, "PUNISH"),
            RuntimeBattlePanelFeedbackKind.CastleDamaged => EventText(
                localizationResolver, eventType, language, "CASTLE DAMAGED"),
            RuntimeBattlePanelFeedbackKind.CastleBroken => EventText(
                localizationResolver, eventType, language, "CASTLE BROKEN"),
            RuntimeBattlePanelFeedbackKind.PhaseChanged => EventText(
                localizationResolver, eventType, language, "PHASE CHANGED"),
            RuntimeBattlePanelFeedbackKind.TurnStarted => EventText(
                localizationResolver, "TURN_STARTED", language, "TURN START"),
            RuntimeBattlePanelFeedbackKind.TurnEnded => EventText(
                localizationResolver, "TURN_ENDED", language, "TURN END"),
            RuntimeBattlePanelFeedbackKind.GameOver => EventText(
                localizationResolver, eventType, language, "GAME OVER"),
            _ => "EVENT",
        };
        if (!missingRequiredTarget) return message;

        return string.Concat(
            message,
            " · ",
            localizationResolver.Get("event.targetUnavailable", language));
    }

    private static string CardsDrawnMessageFor(
        RuntimeEventEnvelope eventEnvelope,
        RuntimeLocalizationResolver localizationResolver,
        string language)
    {
        if (!TryReadCount(eventEnvelope, out var count))
            return EventText(localizationResolver, "CARDS_DRAWN", language, "CARDS DRAWN");
        if (count == 1)
            return EventText(localizationResolver, "CARD_DRAWN", language, "CARD DRAWN");
        return string.Concat(
            EventText(localizationResolver, "CARDS_DRAWN", language, "CARDS DRAWN"),
            " ",
            count.ToString(CultureInfo.InvariantCulture));
    }

    private static string HealMessageFor(
        RuntimeEventEnvelope eventEnvelope,
        RuntimeLocalizationResolver localizationResolver,
        string language)
    {
        var hasAmount = TryReadAmount(eventEnvelope, out var amount);
        var label = EventText(localizationResolver, "HEAL_APPLIED", language, "HEAL");
        var to = localizationResolver.Get("event.to", language);
        var target = TargetLabelFor(eventEnvelope, localizationResolver, language);
        if (hasAmount && target.Length > 0)
            return string.Concat(label, " ", amount, " ", to, " ", target);
        if (hasAmount)
            return string.Concat(label, " ", amount);
        if (target.Length > 0)
            return string.Concat(label, " ", to, " ", target);
        return EventText(localizationResolver, "HEAL_APPLIED", language, "HEAL APPLIED");
    }

    private static string DeathMessageFor(
        RuntimeEventEnvelope eventEnvelope,
        RuntimeLocalizationResolver localizationResolver,
        string language)
    {
        var target = TargetLabelFor(eventEnvelope, localizationResolver, language);
        var label = EventText(localizationResolver, "MINION_DESTROYED", language, "MINION DEFEATED");
        return target.Length > 0
            ? string.Concat(label, " · ", target)
            : label;
    }

    private static string PlayerSwitchMessageFor(
        RuntimeEventEnvelope eventEnvelope,
        RuntimeLocalizationResolver localizationResolver,
        string language)
    {
        var player = EventText(localizationResolver, "TURN_CHANGED", language, "PLAYER");
        var turn = localizationResolver.Get("event.turnSuffix", language);
        if (TryReadPlayerIndex(eventEnvelope, out var playerIndex))
        {
            return string.Concat(
                player,
                " ",
                (playerIndex + 1).ToString(CultureInfo.InvariantCulture),
                " ",
                turn);
        }

        return localizationResolver.Get("event.playerSwitched", language);
    }

    private static string DamageMessageFor(
        RuntimeEventEnvelope eventEnvelope,
        RuntimeLocalizationResolver localizationResolver,
        string language)
    {
        var hasAmount = TryReadAmount(eventEnvelope, out var amount);
        var label = EventText(localizationResolver, "DAMAGE_APPLIED", language, "DAMAGE");
        var to = localizationResolver.Get("event.to", language);
        var target = TargetLabelFor(eventEnvelope, localizationResolver, language);

        if (hasAmount && target.Length > 0)
            return string.Concat(label, " ", amount, " ", to, " ", target);
        if (hasAmount)
            return string.Concat(label, " ", amount);
        if (target.Length > 0)
            return string.Concat(label, " ", to, " ", target);

        // A malformed or older event must not make the UI invent a value or
        // target. The event type itself still gives the player a clear cue.
        return localizationResolver.Get("event.damageAppliedFallback", language);
    }

    private static bool TryReadAmount(
        RuntimeEventEnvelope eventEnvelope,
        out string amount)
    {
        amount = string.Empty;
        if (eventEnvelope == null || eventEnvelope.Data == null ||
            !eventEnvelope.Data.TryGetValue("amount", out var raw) || raw == null)
            return false;

        if (!(raw is byte || raw is sbyte || raw is short || raw is ushort ||
              raw is int || raw is uint || raw is long || raw is ulong ||
              raw is float || raw is double || raw is decimal))
            return false;

        if (raw is float floatValue && (float.IsNaN(floatValue) || float.IsInfinity(floatValue)))
            return false;
        if (raw is double doubleValue && (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue)))
            return false;

        amount = Convert.ToString(raw, CultureInfo.InvariantCulture);
        return !string.IsNullOrWhiteSpace(amount);
    }

    private static string TargetLabelFor(
        RuntimeEventEnvelope eventEnvelope,
        RuntimeLocalizationResolver localizationResolver,
        string language)
    {
        var targets = eventEnvelope?.TargetIds;
        if (targets == null || targets.Count == 0) return string.Empty;
        if (targets.Count > 1)
        {
            var template = localizationResolver.Get("event.targets", language);
            return string.Format(
                CultureInfo.InvariantCulture,
                template,
                targets.Count);
        }

        var rawTarget = ConvertTarget(targets[0]);
        var localized = localizationResolver.ResolveSemantic(
            RuntimeSemanticKind.Target,
            rawTarget,
            language);
        if (localized.IsKnownSemantic && !string.IsNullOrWhiteSpace(localized.Text))
        {
            return RuntimeLocalizationResolver.NormalizeLanguage(language) == "en"
                ? localized.Text.ToUpperInvariant()
                : localized.Text;
        }

        var normalized = NormalizeType(rawTarget);
        if (normalized.StartsWith("PLAYER_", StringComparison.Ordinal))
        {
            var player = localizationResolver.Get("event.turnChanged", language);
            return RuntimeLocalizationResolver.NormalizeLanguage(language) == "en"
                ? player.ToUpperInvariant()
                : player;
        }
        if (normalized.StartsWith("LEADER_", StringComparison.Ordinal))
        {
            var leader = localizationResolver.Get("zone.leader", language);
            return RuntimeLocalizationResolver.NormalizeLanguage(language) == "en"
                ? leader.ToUpperInvariant()
                : leader;
        }

        // Entity ids are not player-facing names. Keep the cue readable
        // without leaking an internal identity that the event cannot label.
        return localizationResolver.Get("event.target", language);
    }

    private static string EventText(
        RuntimeLocalizationResolver localizationResolver,
        string eventType,
        string language,
        string fallback)
    {
        var localized = localizationResolver.ResolveSemantic(
            RuntimeSemanticKind.Event,
            eventType,
            language);
        return localized.IsKnownSemantic && !localized.UsedFallback &&
               !string.IsNullOrWhiteSpace(localized.Text)
            ? localized.Text
            : fallback;
    }

    private static bool RequiresTarget(RuntimeBattlePanelFeedbackKind kind)
    {
        return kind == RuntimeBattlePanelFeedbackKind.AttackDeclared ||
            kind == RuntimeBattlePanelFeedbackKind.Pull;
    }

    private static Color AccentFor(RuntimeBattlePanelFeedbackKind kind)
    {
        return kind switch
        {
            RuntimeBattlePanelFeedbackKind.CardPlayed => CardPlayedAccent,
            RuntimeBattlePanelFeedbackKind.AttackDeclared => AttackAccent,
            RuntimeBattlePanelFeedbackKind.DamageApplied => DamageAppliedAccent,
            RuntimeBattlePanelFeedbackKind.CardsDrawn => CardsDrawnAccent,
            RuntimeBattlePanelFeedbackKind.AmbushSet => AmbushSetAccent,
            RuntimeBattlePanelFeedbackKind.AmbushTriggered => AmbushTriggeredAccent,
            RuntimeBattlePanelFeedbackKind.Commit => CommitAccent,
            RuntimeBattlePanelFeedbackKind.Push => PushAccent,
            RuntimeBattlePanelFeedbackKind.Pull => PullAccent,
            RuntimeBattlePanelFeedbackKind.Punish => PunishAccent,
            RuntimeBattlePanelFeedbackKind.CastleDamaged => CastleDamagedAccent,
            RuntimeBattlePanelFeedbackKind.CastleBroken => CastleBrokenAccent,
            RuntimeBattlePanelFeedbackKind.PhaseChanged => PhaseChangedAccent,
            RuntimeBattlePanelFeedbackKind.TurnStarted => TurnAccent,
            RuntimeBattlePanelFeedbackKind.TurnEnded => TurnAccent,
            RuntimeBattlePanelFeedbackKind.PlayerSwitched => PlayerSwitchAccent,
            RuntimeBattlePanelFeedbackKind.HealApplied => HealAccent,
            RuntimeBattlePanelFeedbackKind.Death => DeathAccent,
            RuntimeBattlePanelFeedbackKind.GameOver => GameOverAccent,
            _ => Color.white,
        };
    }

    private static string PhaseTokenFor(RuntimeEventEnvelope eventEnvelope)
    {
        if (eventEnvelope?.Data != null &&
            eventEnvelope.Data.TryGetValue("to", out var rawTo) && rawTo != null)
        {
            return NormalizeType(Convert.ToString(rawTo, CultureInfo.InvariantCulture));
        }

        return NormalizeType(eventEnvelope?.Phase);
    }

    private static bool TryReadCount(RuntimeEventEnvelope eventEnvelope, out long count)
    {
        return TryReadInteger(eventEnvelope, "count", out count);
    }

    private static bool TryReadPlayerIndex(RuntimeEventEnvelope eventEnvelope, out long playerIndex)
    {
        if (TryReadInteger(eventEnvelope, "currentPlayer", out playerIndex) ||
            TryReadInteger(eventEnvelope, "player", out playerIndex) ||
            TryReadInteger(eventEnvelope, "playerIndex", out playerIndex))
        {
            return playerIndex >= 0 && playerIndex <= 1;
        }

        playerIndex = -1;
        return false;
    }

    private static bool TryReadInteger(
        RuntimeEventEnvelope eventEnvelope,
        string key,
        out long value)
    {
        value = 0;
        if (eventEnvelope?.Data == null ||
            !eventEnvelope.Data.TryGetValue(key, out var raw) || raw == null)
            return false;

        if (!(raw is byte || raw is sbyte || raw is short || raw is ushort ||
              raw is int || raw is uint || raw is long || raw is ulong))
            return false;

        try
        {
            value = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static string NormalizeType(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
    }

    private static string ConvertTarget(object value)
    {
        if (value == null) return "null";
        if (value is string text) return text;
        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        return value.ToString();
    }

    private static string SafeToken(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "missing";
        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        for (var index = 0; index < value.Length && builder.Length < maxLength; index++)
        {
            var character = value[index];
            builder.Append(char.IsControl(character) ? ' ' : character);
        }
        var result = builder.ToString().Trim();
        return result.Length == 0 ? "missing" : result;
    }

    private static Color Hex(string value)
    {
        return ColorUtility.TryParseHtmlString("#" + value, out var color)
            ? color
            : Color.magenta;
    }
}

/// <summary>
/// Presentation-only action feedback controller. It consumes the complete
/// adapter event list and remembers stable event identities, so repeated panel
/// renders cannot replay the same cue. Animation is advanced by Tick instead
/// of starting coroutines; at most one visual state can be active.
/// </summary>
public sealed class RuntimeBattlePanelActionFeedback
{
    public const float StandardDurationSeconds = 0.26f;
    private const float AnimatedScale = 1.04f;
    private const float AnimatedAlpha = 0.86f;
    private const float SettledAlpha = 0.34f;

    private readonly HashSet<string> _consumedEventKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly Queue<RuntimeBattlePanelFeedbackCue> _pendingCues =
        new Queue<RuntimeBattlePanelFeedbackCue>();
    private RuntimeBattlePanelView _view;
    private RuntimeBattlePanelFeedbackCue _currentCue;
    private float _elapsed;
    private bool _reducedMotion;
    private Vector3 _baseScale = Vector3.one;
    private Color _baseBackgroundColor = new Color(0.10f, 0.18f, 0.22f, SettledAlpha);
    private Color _baseTextColor = new Color(0.78f, 0.86f, 0.90f);

    public bool ReducedMotion => _reducedMotion;
    public RuntimeBattlePanelFeedbackCue CurrentCue => _currentCue;
    public string CurrentMessage => _currentCue?.Message ?? string.Empty;
    public string LastEventId => _currentCue?.EventId ?? string.Empty;
    public bool HasFeedback => _currentCue != null;
    public bool IsAnimating => !_reducedMotion && _currentCue != null && _elapsed < StandardDurationSeconds;
    public int ConsumedEventCount => _consumedEventKeys.Count;
    public int AppliedFeedbackCount { get; private set; }
    public int PendingCueCount => _pendingCues.Count;
    public float AnimationRemainingSeconds =>
        IsAnimating ? Mathf.Max(0f, StandardDurationSeconds - _elapsed) : 0f;

    /// <summary>
    /// Binds the feedback surface without touching the runtime adapter. A
    /// null view is allowed for model-only tests and safe headless hosts.
    /// </summary>
    public void Bind(RuntimeBattlePanelView view)
    {
        if (ReferenceEquals(_view, view))
        {
            CaptureBaseVisuals();
            return;
        }

        ResetVisuals();
        _view = view;
        CaptureBaseVisuals();
        ResetVisuals();
    }

    /// <summary>
    /// Consumes only new identities from Presentation.Events. The caller may
    /// supply a repeated, sparse, out-of-order, null-containing or unknown
    /// list; no entry is allowed to throw or mutate gameplay state.
    /// </summary>
    public void Consume(IReadOnlyList<RuntimeEventEnvelope> events)
    {
        Consume(events, null, "en");
    }

    /// <summary>
    /// Consumes new events using the panel's current presentation language.
    /// The overload keeps the old English/default path intact for headless and
    /// existing callers while preventing the live panel from dropping its
    /// language when it projects the same public event list into a pulse.
    /// </summary>
    public void Consume(
        IReadOnlyList<RuntimeEventEnvelope> events,
        RuntimeLocalizationResolver localizationResolver,
        string language = "en")
    {
        if (events == null) return;

        var incoming = new List<RuntimeBattlePanelFeedbackCue>();
        for (var index = 0; index < events.Count; index++)
        {
            var eventEnvelope = events[index];
            var key = RuntimeBattlePanelActionFeedbackModel.StableEventKey(eventEnvelope);
            if (!_consumedEventKeys.Add(key)) continue;
            var mapped = localizationResolver == null
                ? RuntimeBattlePanelActionFeedbackModel.TryMap(eventEnvelope, language, out var cue)
                : RuntimeBattlePanelActionFeedbackModel.TryMap(
                    eventEnvelope,
                    language,
                    localizationResolver,
                    out cue);
            if (!mapped)
                continue;

            AppliedFeedbackCount++;
            incoming.Add(cue);
        }

        if (incoming.Count == 0) return;

        // Revision is authoritative ordering. A malformed caller may provide
        // an older suffix after a newer event; stable insertion sorting keeps
        // revisions monotonic without changing order among same-revision
        // events. Ambush/castle/game-over are promoted only within the first
        // revision so their existing presentation priority is retained.
        StableSortByRevision(incoming);
        PromoteFirstRevisionPriority(incoming);
        for (var index = 0; index < incoming.Count; index++)
            _pendingCues.Enqueue(incoming[index]);

        if (_currentCue == null)
            ActivateNextCue();
        if (_reducedMotion)
            DrainPendingCues();
    }

    private static int Priority(RuntimeBattlePanelFeedbackCue cue)
    {
        if (cue == null) return 0;
        if (cue.Kind == RuntimeBattlePanelFeedbackKind.GameOver) return 120;
        if (cue.Kind == RuntimeBattlePanelFeedbackKind.AmbushTriggered) return 110;
        if (cue.Kind == RuntimeBattlePanelFeedbackKind.CastleBroken) return 100;
        if (cue.Kind == RuntimeBattlePanelFeedbackKind.Death) return 80;
        if (cue.Kind == RuntimeBattlePanelFeedbackKind.DamageApplied) return 50;
        return 0;
    }

    private static void StableSortByRevision(List<RuntimeBattlePanelFeedbackCue> cues)
    {
        for (var index = 1; index < cues.Count; index++)
        {
            var candidate = cues[index];
            var cursor = index - 1;
            while (cursor >= 0 && cues[cursor].SnapshotRevision > candidate.SnapshotRevision)
            {
                cues[cursor + 1] = cues[cursor];
                cursor--;
            }

            cues[cursor + 1] = candidate;
        }
    }

    private static void PromoteFirstRevisionPriority(List<RuntimeBattlePanelFeedbackCue> cues)
    {
        if (cues.Count < 2) return;

        var firstRevision = cues[0].SnapshotRevision;
        var bestIndex = 0;
        for (var index = 1; index < cues.Count; index++)
        {
            if (cues[index].SnapshotRevision != firstRevision) break;
            if (Priority(cues[index]) > Priority(cues[bestIndex]))
                bestIndex = index;
        }

        if (bestIndex == 0) return;
        var preferred = cues[bestIndex];
        cues.RemoveAt(bestIndex);
        cues.Insert(0, preferred);
    }

    /// <summary>
    /// Turns the accessibility path on/off. Enabling Reduced Motion settles
    /// the current cue immediately and never changes event identity/history.
    /// </summary>
    public void SetReducedMotion(bool enabled)
    {
        if (_reducedMotion == enabled)
        {
            if (enabled)
            {
                if (_currentCue != null) ApplyStaticCue(_currentCue);
                DrainPendingCues();
            }
            return;
        }

        _reducedMotion = enabled;
        if (_currentCue == null)
        {
            if (enabled) DrainPendingCues();
            return;
        }

        // A settings change must not replay an already-consumed event. The
        // next genuinely new event will choose the currently active path.
        ApplyStaticCue(_currentCue);
        if (enabled) DrainPendingCues();
    }

    /// <summary>
    /// Advances the one active visual state. No coroutine is allocated, and a
    /// panel can call this every frame without accumulating work.
    /// </summary>
    public void Tick(float unscaledDeltaSeconds)
    {
        if (_reducedMotion || _currentCue == null) return;
        if (unscaledDeltaSeconds <= 0f) return;

        _elapsed = Mathf.Min(StandardDurationSeconds, _elapsed + unscaledDeltaSeconds);
        var progress = Mathf.Clamp01(_elapsed / StandardDurationSeconds);
        var eased = 1f - Mathf.SmoothStep(0f, 1f, progress);
        ApplyAnimatedProgress(_currentCue, eased);
        if (_elapsed >= StandardDurationSeconds)
        {
            if (!ActivateNextCue())
                ResetAnimatedProperties(_currentCue);
        }
    }

    /// <summary>
    /// Skips only the presentation pulse. It never removes an event identity
    /// or changes the adapter/event source, and it advances at most one cue.
    /// </summary>
    public bool SkipCurrentCue()
    {
        if (_currentCue == null) return false;
        if (ActivateNextCue()) return true;

        _currentCue = null;
        _elapsed = 0f;
        ResetVisuals();
        return true;
    }

    /// <summary>
    /// Clears active visual state during disable/unbind while retaining the
    /// consumed identity set. This prevents old events replaying on re-enable.
    /// </summary>
    public void Clear()
    {
        _currentCue = null;
        _pendingCues.Clear();
        _elapsed = 0f;
        ResetVisuals();
    }

    /// <summary>
    /// Starts a fresh match/binding identity space. This is separate from
    /// Clear so disabling a panel does not replay its old event list.
    /// </summary>
    public void ResetForBinding()
    {
        Clear();
        _consumedEventKeys.Clear();
        AppliedFeedbackCount = 0;
    }

    private void ApplyCue(RuntimeBattlePanelFeedbackCue cue)
    {
        if (_reducedMotion) ApplyStaticCue(cue);
        else
        {
            _elapsed = 0f;
            ApplyAnimatedCue(cue);
        }
    }

    private bool ActivateNextCue()
    {
        if (_pendingCues.Count == 0) return false;

        _currentCue = _pendingCues.Dequeue();
        ApplyCue(_currentCue);
        return true;
    }

    private void DrainPendingCues()
    {
        if (_pendingCues.Count == 0)
        {
            if (_currentCue != null) ApplyStaticCue(_currentCue);
            return;
        }

        var selected = _currentCue;
        while (_pendingCues.Count > 0)
        {
            var candidate = _pendingCues.Dequeue();
            if (selected == null ||
                Priority(candidate) > Priority(selected) ||
                (Priority(candidate) == Priority(selected) &&
                 candidate.SnapshotRevision >= selected.SnapshotRevision))
            {
                selected = candidate;
            }
        }

        _currentCue = selected;
        if (_currentCue != null) ApplyStaticCue(_currentCue);
    }

    private void ApplyStaticCue(RuntimeBattlePanelFeedbackCue cue)
    {
        _elapsed = StandardDurationSeconds;
        if (_view == null) return;

        if (_view.FeedbackRoot != null)
            _view.FeedbackRoot.localScale = _baseScale;
        if (_view.FeedbackText != null)
        {
            _view.FeedbackText.text = cue.Message;
            _view.FeedbackText.color = cue.Accent;
        }
        if (_view.FeedbackBackground != null)
        {
            var color = cue.Accent;
            color.a = SettledAlpha;
            _view.FeedbackBackground.color = color;
        }
    }

    private void ApplyAnimatedCue(RuntimeBattlePanelFeedbackCue cue)
    {
        if (_view == null) return;

        if (_view.FeedbackText != null)
        {
            _view.FeedbackText.text = cue.Message;
            _view.FeedbackText.color = cue.Accent;
        }
        if (_view.FeedbackBackground != null)
        {
            var color = cue.Accent;
            color.a = AnimatedAlpha;
            _view.FeedbackBackground.color = color;
        }
        if (_view.FeedbackRoot != null)
            _view.FeedbackRoot.localScale = _baseScale * AnimatedScale;
    }

    private void ApplyAnimatedProgress(RuntimeBattlePanelFeedbackCue cue, float progress)
    {
        if (_view == null) return;

        if (_view.FeedbackRoot != null)
            _view.FeedbackRoot.localScale = Vector3.LerpUnclamped(
                _baseScale,
                _baseScale * AnimatedScale,
                progress);
        if (_view.FeedbackBackground != null)
        {
            var color = cue.Accent;
            color.a = Mathf.Lerp(SettledAlpha, AnimatedAlpha, progress);
            _view.FeedbackBackground.color = color;
        }
    }

    private void ResetAnimatedProperties(RuntimeBattlePanelFeedbackCue cue)
    {
        if (_view == null) return;
        if (_view.FeedbackRoot != null)
            _view.FeedbackRoot.localScale = _baseScale;
        if (_view.FeedbackBackground != null)
        {
            var color = cue.Accent;
            color.a = SettledAlpha;
            _view.FeedbackBackground.color = color;
        }
    }

    private void CaptureBaseVisuals()
    {
        if (_view == null) return;
        if (_view.FeedbackRoot != null)
            _baseScale = _view.FeedbackRoot.localScale;
        if (_view.FeedbackBackground != null)
            _baseBackgroundColor = _view.FeedbackBackground.color;
        if (_view.FeedbackText != null)
            _baseTextColor = _view.FeedbackText.color;
    }

    private void ResetVisuals()
    {
        if (_view == null) return;
        if (_view.FeedbackRoot != null)
            _view.FeedbackRoot.localScale = _baseScale;
        if (_view.FeedbackBackground != null)
            _view.FeedbackBackground.color = _baseBackgroundColor;
        if (_view.FeedbackText != null)
        {
            _view.FeedbackText.text = "READY";
            _view.FeedbackText.color = _baseTextColor;
        }
    }
}
}
