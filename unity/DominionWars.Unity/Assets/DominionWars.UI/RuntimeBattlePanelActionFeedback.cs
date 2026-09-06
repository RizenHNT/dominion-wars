#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DominionWars.Adapters;
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

    public static bool TryMap(
        RuntimeEventEnvelope eventEnvelope,
        out RuntimeBattlePanelFeedbackCue cue)
    {
        cue = null;
        if (eventEnvelope == null) return false;

        var eventType = NormalizeType(eventEnvelope.Type);
        var kind = KindFor(eventType);
        if (kind == RuntimeBattlePanelFeedbackKind.None) return false;

        var targetCount = eventEnvelope.TargetIds == null ? 0 : eventEnvelope.TargetIds.Count;
        var missingRequiredTarget = RequiresTarget(kind) && targetCount == 0;
        var message = MessageFor(kind, eventType, missingRequiredTarget);
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

    private static RuntimeBattlePanelFeedbackKind KindFor(string eventType)
    {
        if (eventType == "CARD_PLAYED") return RuntimeBattlePanelFeedbackKind.CardPlayed;
        if (eventType == "ATTACK_DECLARED") return RuntimeBattlePanelFeedbackKind.AttackDeclared;
        if (eventType == "CARDS_DRAWN" || eventType == "CARD_DRAWN")
            return RuntimeBattlePanelFeedbackKind.CardsDrawn;
        if (eventType == "AMBUSH_SET") return RuntimeBattlePanelFeedbackKind.AmbushSet;
        if (eventType == "AMBUSH_TRIGGERED") return RuntimeBattlePanelFeedbackKind.AmbushTriggered;
        if (eventType == "CARD_COMMITTED") return RuntimeBattlePanelFeedbackKind.Commit;
        if (eventType == "CARD_PUSHED") return RuntimeBattlePanelFeedbackKind.Push;
        if (eventType == "CARD_PULLED") return RuntimeBattlePanelFeedbackKind.Pull;
        if (eventType.StartsWith("PUNISH_", StringComparison.Ordinal))
            return RuntimeBattlePanelFeedbackKind.Punish;
        if (eventType == "CASTLE_DAMAGED") return RuntimeBattlePanelFeedbackKind.CastleDamaged;
        if (eventType == "CASTLE_BROKEN") return RuntimeBattlePanelFeedbackKind.CastleBroken;
        if (eventType == "PHASE_CHANGED") return RuntimeBattlePanelFeedbackKind.PhaseChanged;
        return RuntimeBattlePanelFeedbackKind.None;
    }

    private static string MessageFor(
        RuntimeBattlePanelFeedbackKind kind,
        string eventType,
        bool missingRequiredTarget)
    {
        var message = kind switch
        {
            RuntimeBattlePanelFeedbackKind.CardPlayed => "CARD PLAYED",
            RuntimeBattlePanelFeedbackKind.AttackDeclared => "ATTACK DECLARED",
            RuntimeBattlePanelFeedbackKind.CardsDrawn => "CARDS DRAWN",
            RuntimeBattlePanelFeedbackKind.AmbushSet => "AMBUSH SET",
            RuntimeBattlePanelFeedbackKind.AmbushTriggered => "AMBUSH TRIGGERED",
            RuntimeBattlePanelFeedbackKind.Commit => "CARD COMMITTED",
            RuntimeBattlePanelFeedbackKind.Push => "CARD PUSHED",
            RuntimeBattlePanelFeedbackKind.Pull => "CARD PULLED",
            // The event subtype is an internal protocol token. Keep the
            // player cue semantic and leave the complete subtype in the
            // diagnostic cue fields for an explicitly enabled debug view.
            RuntimeBattlePanelFeedbackKind.Punish => "PUNISH",
            RuntimeBattlePanelFeedbackKind.CastleDamaged => "CASTLE DAMAGED",
            RuntimeBattlePanelFeedbackKind.CastleBroken => "CASTLE BROKEN",
            RuntimeBattlePanelFeedbackKind.PhaseChanged => "PHASE CHANGED",
            _ => "EVENT",
        };
        return missingRequiredTarget ? message + " · TARGET UNAVAILABLE" : message;
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
            _ => Color.white,
        };
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
        if (events == null) return;

        RuntimeBattlePanelFeedbackCue preferredCue = null;
        for (var index = 0; index < events.Count; index++)
        {
            var eventEnvelope = events[index];
            var key = RuntimeBattlePanelActionFeedbackModel.StableEventKey(eventEnvelope);
            if (!_consumedEventKeys.Add(key)) continue;
            if (!RuntimeBattlePanelActionFeedbackModel.TryMap(eventEnvelope, out var cue))
                continue;

            AppliedFeedbackCount++;
            if (preferredCue == null || Priority(cue) >= Priority(preferredCue))
                preferredCue = cue;
        }

        if (preferredCue == null) return;
        _currentCue = preferredCue;
        ApplyCue(preferredCue);
    }

    private static int Priority(RuntimeBattlePanelFeedbackCue cue)
        => cue != null && cue.Kind == RuntimeBattlePanelFeedbackKind.AmbushTriggered ? 100 : 0;

    /// <summary>
    /// Turns the accessibility path on/off. Enabling Reduced Motion settles
    /// the current cue immediately and never changes event identity/history.
    /// </summary>
    public void SetReducedMotion(bool enabled)
    {
        if (_reducedMotion == enabled)
        {
            if (enabled && _currentCue != null) ApplyStaticCue(_currentCue);
            return;
        }

        _reducedMotion = enabled;
        if (_currentCue == null) return;

        // A settings change must not replay an already-consumed event. The
        // next genuinely new event will choose the currently active path.
        ApplyStaticCue(_currentCue);
    }

    /// <summary>
    /// Advances the one active visual state. No coroutine is allocated, and a
    /// panel can call this every frame without accumulating work.
    /// </summary>
    public void Tick(float unscaledDeltaSeconds)
    {
        if (_reducedMotion || _currentCue == null || _view == null) return;
        if (unscaledDeltaSeconds <= 0f) return;

        _elapsed = Mathf.Min(StandardDurationSeconds, _elapsed + unscaledDeltaSeconds);
        var progress = Mathf.Clamp01(_elapsed / StandardDurationSeconds);
        var eased = 1f - Mathf.SmoothStep(0f, 1f, progress);
        ApplyAnimatedProgress(_currentCue, eased);
        if (_elapsed >= StandardDurationSeconds)
            ResetAnimatedProperties(_currentCue);
    }

    /// <summary>
    /// Clears active visual state during disable/unbind while retaining the
    /// consumed identity set. This prevents old events replaying on re-enable.
    /// </summary>
    public void Clear()
    {
        _currentCue = null;
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
        if (_view == null) return;
        if (_reducedMotion) ApplyStaticCue(cue);
        else
        {
            _elapsed = 0f;
            ApplyAnimatedCue(cue);
        }
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
