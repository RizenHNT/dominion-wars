#nullable disable

using System;
using System.Globalization;
using DominionWars.Unity.Runtime;
using UnityEngine;

namespace DominionWars.Unity.UI
{

/// <summary>
/// Reusable uGUI card face for snapshot-visible cards.
///
/// This component owns presentation only. RuntimeCardDisplayModel supplies
/// every displayed value, while RuntimeContentResolver owns stable art IDs and
/// their fallback chain. It never decides visibility, legality, targeting,
/// phase progression, or any other gameplay rule.
/// </summary>
public enum RuntimeCardFaceMode
{
    Full,
    Compact,
}

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class RuntimeCardFaceView : MonoBehaviour
{
    public const string DefaultSkinId = "default";
    public const float FullWidth = 176f;
    public const float FullHeight = 248f;
    public const float CompactWidth = 112f;
    public const float CompactHeight = 158f;

    private static readonly Color FrameFill = Hex("162631");
    private static readonly Color FrameBorder = Hex("7294A3");
    private static readonly Color PanelFill = Hex("10202A");
    private static readonly Color PanelBorder = Hex("456572");
    private static readonly Color TextPrimary = Hex("F3F7F8");
    private static readonly Color TextMuted = Hex("A9BEC6");
    private static readonly Color PunishAccent = Hex("D78368");
    private static readonly Color AttackAccent = Hex("F0A066");
    private static readonly Color HealthAccent = Hex("72D39B");
    private static readonly Color PlaceholderFill = Hex("233B48");

    [SerializeField]
    private RuntimeCardFaceMode _mode = RuntimeCardFaceMode.Full;

    [SerializeField]
    private string _skinId = DefaultSkinId;

    private bool _built;
    private bool _diagnosticsVisible;
    private bool _usingArtFallback;
    private RuntimeCardDisplayModel _boundCard;
    private RuntimeContentResolver _contentResolver;

    private RectTransform _cardRoot;
    private UnityEngine.UI.Image _frameImage;
    private UnityEngine.UI.Outline _frameOutline;
    private RectTransform _header;
    private RectTransform _titleRoot;
    private UnityEngine.UI.Text _titleText;
    private UnityEngine.UI.Text _metaText;
    private RectTransform _costBadge;
    private UnityEngine.UI.Text _costLabel;
    private UnityEngine.UI.Text _costValue;
    private RectTransform _punishBadge;
    private UnityEngine.UI.Text _punishLabel;
    private UnityEngine.UI.Text _punishValue;
    private RectTransform _artPanel;
    private UnityEngine.UI.RawImage _artImage;
    private UnityEngine.UI.Text _artFallbackText;
    private RectTransform _rulesPanel;
    private UnityEngine.UI.Text _rulesText;
    private RectTransform _statsRoot;
    private RectTransform _attackBadge;
    private UnityEngine.UI.Text _attackLabel;
    private UnityEngine.UI.Text _attackValue;
    private RectTransform _healthBadge;
    private UnityEngine.UI.Text _healthLabel;
    private UnityEngine.UI.Text _healthValue;
    private UnityEngine.UI.Text _identityText;
    private RectTransform _interactionMarker;
    private UnityEngine.UI.Text _interactionMarkerText;
    private RectTransform _disabledVeil;

    /// <summary>Card data currently bound to this face, or null before Bind.</summary>
    public RuntimeCardDisplayModel BoundCard { get { return _boundCard; } }

    /// <summary>The layout variant used by this face.</summary>
    public RuntimeCardFaceMode Mode { get { return _mode; } }

    /// <summary>The optional skin ID used for card-art resolution.</summary>
    public string SkinId { get { return _skinId; } }

    public RectTransform CardRoot { get { EnsureBuilt(); return _cardRoot; } }
    public UnityEngine.UI.Text TitleText { get { EnsureBuilt(); return _titleText; } }
    public UnityEngine.UI.Text MetaText { get { EnsureBuilt(); return _metaText; } }
    public RectTransform CostBadge { get { EnsureBuilt(); return _costBadge; } }
    public UnityEngine.UI.Text CostLabel { get { EnsureBuilt(); return _costLabel; } }
    public UnityEngine.UI.Text CostValue { get { EnsureBuilt(); return _costValue; } }
    public UnityEngine.UI.Text PunishLabel { get { EnsureBuilt(); return _punishLabel; } }
    public UnityEngine.UI.Text PunishValue { get { EnsureBuilt(); return _punishValue; } }
    public RectTransform ArtPanel { get { EnsureBuilt(); return _artPanel; } }
    public UnityEngine.UI.RawImage ArtImage { get { EnsureBuilt(); return _artImage; } }
    public UnityEngine.UI.Text ArtFallbackText { get { EnsureBuilt(); return _artFallbackText; } }
    public UnityEngine.UI.Text RulesText { get { EnsureBuilt(); return _rulesText; } }
    public RectTransform StatsRoot { get { EnsureBuilt(); return _statsRoot; } }
    public UnityEngine.UI.Text AttackLabel { get { EnsureBuilt(); return _attackLabel; } }
    public UnityEngine.UI.Text AttackValue { get { EnsureBuilt(); return _attackValue; } }
    public UnityEngine.UI.Text HealthLabel { get { EnsureBuilt(); return _healthLabel; } }
    public UnityEngine.UI.Text HealthValue { get { EnsureBuilt(); return _healthValue; } }
    public UnityEngine.UI.Text IdentityText { get { EnsureBuilt(); return _identityText; } }
    public RectTransform InteractionMarker { get { EnsureBuilt(); return _interactionMarker; } }
    public UnityEngine.UI.Text InteractionMarkerText { get { EnsureBuilt(); return _interactionMarkerText; } }
    public RectTransform DisabledVeil { get { EnsureBuilt(); return _disabledVeil; } }
    public bool DiagnosticsVisible { get { return _diagnosticsVisible; } }
    public bool IsUsingArtFallback
    {
        get
        {
            EnsureBuilt();
            return _usingArtFallback;
        }
    }

    /// <summary>
    /// Creates a card-face GameObject under a uGUI parent. The caller can bind
    /// it to a visible model later; no catalog or gameplay lookup is performed
    /// by construction.
    /// </summary>
    public static RuntimeCardFaceView Build(
        RectTransform parent,
        string name,
        RuntimeCardFaceMode mode = RuntimeCardFaceMode.Full)
    {
        if (parent == null) throw new ArgumentNullException(nameof(parent));

        var objectName = string.IsNullOrWhiteSpace(name) ? "RuntimeCardFace" : name;
        var cardObject = new GameObject(objectName, typeof(RectTransform));
        cardObject.transform.SetParent(parent, false);
        var view = cardObject.AddComponent<RuntimeCardFaceView>();
        view._mode = mode;
        view.EnsureBuilt();
        view.ApplyMode();
        return view;
    }

    private void Awake()
    {
        EnsureBuilt();
        ApplyMode();
    }

    /// <summary>Switches between readable full and zone-friendly compact layouts.</summary>
    public void SetMode(RuntimeCardFaceMode mode)
    {
        _mode = mode;
        EnsureBuilt();
        ApplyMode();
    }

    /// <summary>Convenience wrapper for region renderers that expose a bool.</summary>
    public void SetCompact(bool compact)
    {
        SetMode(compact ? RuntimeCardFaceMode.Compact : RuntimeCardFaceMode.Full);
    }

    /// <summary>
    /// Shows or hides the card's technical identity and art fallback labels.
    /// These labels are diagnostic-only and can never be enabled in a
    /// non-Editor, non-development build.
    /// </summary>
    public void SetDiagnosticsVisible(bool visible)
    {
        _diagnosticsVisible = visible && (Application.isEditor || Debug.isDebugBuild);
        EnsureBuilt();
        if (_identityText != null)
            _identityText.gameObject.SetActive(_diagnosticsVisible);
        if (_artFallbackText != null)
            _artFallbackText.gameObject.SetActive(_diagnosticsVisible && _usingArtFallback);
        if (_costBadge != null)
            _costBadge.gameObject.SetActive(_diagnosticsVisible);
    }

    /// <summary>
    /// Binds one already-authorized visible card model. The model, rather than
    /// this component, remains the source of truth for all displayed values.
    /// </summary>
    public void Bind(
        RuntimeCardDisplayModel card,
        RuntimeContentResolver contentResolver = null,
        string skinId = null)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));

        EnsureBuilt();
        _boundCard = card;
        _contentResolver = contentResolver;
        if (skinId != null) _skinId = string.IsNullOrWhiteSpace(skinId) ? DefaultSkinId : skinId;

        _titleText.text = Display(card.Name, RuntimeCardDisplayModel.UnknownCard);
        _metaText.text = BuildMetaText(card);
        _costValue.text = DisplayNumber(card.DeclaredCost);
        _punishValue.text = DisplayNumber(card.PrintedPunish);
        _rulesText.text = BuildRulesText(card);
        var runtimeStats = UsesRuntimeStats(card.Zone);
        var showStats = card.IsMinion &&
            (runtimeStats ? card.HasCurrentStats : card.HasPrintedStats);
        _attackValue.text = showStats
            ? DisplayNumber(runtimeStats ? card.CurrentAttack : card.PrintedAttack, string.Empty)
            : string.Empty;
        _healthValue.text = showStats
            ? DisplayNumber(runtimeStats ? card.CurrentHealth : card.PrintedHealth, string.Empty)
            : string.Empty;
        _statsRoot.gameObject.SetActive(showStats);
        _identityText.text = BuildIdentityText(card);
        ApplyFactionAccent(card.Faction);
        ApplyCardArt(card);
        ApplyMode();
        SetDiagnosticsVisible(_diagnosticsVisible);
        SetInteractionState(false, false, true);
    }

    /// <summary>
    /// Applies presentation-only legal-source/target emphasis. It consumes the
    /// caller's already-advertised state and never derives legality itself.
    /// </summary>
    public void SetInteractionState(bool sourceHighlighted, bool targetHighlighted, bool interactable)
    {
        EnsureBuilt();
        var accent = _boundCard == null ? FrameBorder : AccentForFaction(_boundCard.Faction);
        _frameOutline.effectColor = sourceHighlighted
            ? Hex("F1C75B")
            : targetHighlighted ? Hex("55D7E5") : accent;
        _frameOutline.effectDistance = sourceHighlighted || targetHighlighted
            ? new Vector2(4f, 4f)
            : new Vector2(2f, 2f);

        _interactionMarker.gameObject.SetActive(sourceHighlighted || targetHighlighted);
        _interactionMarkerText.text = sourceHighlighted ? "DRAG" : "TARGET";
        _interactionMarkerText.color = sourceHighlighted ? Hex("F1C75B") : Hex("55D7E5");
        _disabledVeil.gameObject.SetActive(!interactable);
    }

    /// <summary>
    /// Clears the binding while retaining the generated hierarchy. A visible
    /// neutral placeholder remains, so a recycled face cannot show stale data.
    /// </summary>
    public void Clear()
    {
        EnsureBuilt();
        _boundCard = null;
        _contentResolver = null;
        _titleText.text = RuntimeCardDisplayModel.UnknownCard;
        _metaText.text = RuntimeCardDisplayModel.Unavailable;
        _costValue.text = RuntimeCardDisplayModel.Unavailable;
        _punishValue.text = RuntimeCardDisplayModel.Unavailable;
        _rulesText.text = "RULES " + RuntimeCardDisplayModel.Unavailable;
        _attackValue.text = string.Empty;
        _healthValue.text = string.Empty;
        _statsRoot.gameObject.SetActive(false);
        _identityText.text = "ID " + RuntimeCardDisplayModel.Unavailable;
        ApplyFactionAccent(string.Empty);
        ApplyArtTexture(null);
        ApplyMode();
        SetDiagnosticsVisible(_diagnosticsVisible);
        SetInteractionState(false, false, true);
    }

    private void EnsureBuilt()
    {
        if (_built) return;

        _cardRoot = GetComponent<RectTransform>();
        if (_cardRoot == null)
            _cardRoot = gameObject.AddComponent<RectTransform>();

        _cardRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _cardRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _cardRoot.pivot = new Vector2(0.5f, 0.5f);
        _cardRoot.anchoredPosition = Vector2.zero;

        _frameImage = EnsureImage(_cardRoot, FrameFill, true);
        _frameOutline = _cardRoot.GetComponent<UnityEngine.UI.Outline>();
        if (_frameOutline == null) _frameOutline = _cardRoot.gameObject.AddComponent<UnityEngine.UI.Outline>();
        _frameOutline.effectColor = FrameBorder;
        _frameOutline.effectDistance = new Vector2(2f, 2f);
        _frameOutline.useGraphicAlpha = false;
        SetLayout(_cardRoot, 88f, FullWidth, 0f, 124f, FullHeight, 0f);

        _header = EnsureRect(_cardRoot, "CardHeader");
        SetAnchors(_header, new Vector2(0.04f, 0.76f), new Vector2(0.96f, 0.97f));
        EnsureImage(_header, PanelFill, false);

        var stripe = EnsureRect(_header, "FactionStripe");
        SetAnchors(stripe, new Vector2(0f, 0.88f), new Vector2(1f, 1f));
        EnsureImage(stripe, FrameBorder, false);

        _titleRoot = EnsureRect(_header, "CardTitle");
        SetAnchors(_titleRoot, new Vector2(0.06f, 0.40f), new Vector2(0.94f, 0.87f));
        _titleText = EnsureText(_titleRoot, "Title", 15, TextPrimary);
        _titleText.alignment = TextAnchor.MiddleCenter;
        _titleText.fontStyle = FontStyle.Bold;
        _titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _titleText.verticalOverflow = VerticalWrapMode.Truncate;

        var metaRoot = EnsureRect(_header, "CardMeta");
        SetAnchors(metaRoot, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.38f));
        _metaText = EnsureText(metaRoot, "Meta", 9, TextMuted);
        _metaText.alignment = TextAnchor.MiddleCenter;
        _metaText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _metaText.verticalOverflow = VerticalWrapMode.Truncate;

        _costBadge = EnsureRect(_cardRoot, "CardCostBadge");
        SetAnchors(_costBadge, new Vector2(0.04f, 0.66f), new Vector2(0.28f, 0.75f));
        EnsureImage(_costBadge, PanelFill, false);
        EnsureOutline(_costBadge, FrameBorder);
        _costLabel = EnsureText(EnsureRect(_costBadge, "CostLabel"), "Value", 8, TextMuted);
        SetAnchors(_costLabel.rectTransform, new Vector2(0.04f, 0.52f), new Vector2(0.96f, 0.98f));
        _costLabel.text = "COST";
        _costLabel.alignment = TextAnchor.MiddleCenter;
        _costValue = EnsureText(EnsureRect(_costBadge, "CostValue"), "Value", 14, TextPrimary);
        SetAnchors(_costValue.rectTransform, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.58f));
        _costValue.alignment = TextAnchor.MiddleCenter;
        _costValue.fontStyle = FontStyle.Bold;

        _punishBadge = EnsureRect(_cardRoot, "CardPunishBadge");
        SetAnchors(_punishBadge, new Vector2(0.72f, 0.66f), new Vector2(0.96f, 0.75f));
        EnsureImage(_punishBadge, PanelFill, false);
        EnsureOutline(_punishBadge, PunishAccent);
        _punishLabel = EnsureText(EnsureRect(_punishBadge, "PunishLabel"), "Value", 8, PunishAccent);
        SetAnchors(_punishLabel.rectTransform, new Vector2(0.04f, 0.52f), new Vector2(0.96f, 0.98f));
        _punishLabel.text = "PUNISH";
        _punishLabel.alignment = TextAnchor.MiddleCenter;
        _punishValue = EnsureText(EnsureRect(_punishBadge, "PunishValue"), "Value", 14, TextPrimary);
        SetAnchors(_punishValue.rectTransform, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.58f));
        _punishValue.alignment = TextAnchor.MiddleCenter;
        _punishValue.fontStyle = FontStyle.Bold;

        _artPanel = EnsureRect(_cardRoot, "CardArtPanel");
        SetAnchors(_artPanel, new Vector2(0.05f, 0.40f), new Vector2(0.95f, 0.64f));
        EnsureImage(_artPanel, PlaceholderFill, false);
        EnsureOutline(_artPanel, PanelBorder);
        var artRoot = EnsureRect(_artPanel, "CardArt");
        SetAnchors(artRoot, new Vector2(0.025f, 0.025f), new Vector2(0.975f, 0.975f));
        _artImage = artRoot.GetComponent<UnityEngine.UI.RawImage>();
        if (_artImage == null) _artImage = artRoot.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        _artImage.color = Color.white;
        _artImage.raycastTarget = false;
        var aspect = artRoot.GetComponent<UnityEngine.UI.AspectRatioFitter>();
        if (aspect == null) aspect = artRoot.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
        aspect.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
        aspect.aspectRatio = 1f;
        _artFallbackText = EnsureText(EnsureRect(_artPanel, "ArtFallback"), "Label", 10, TextMuted);
        SetAnchors(_artFallbackText.rectTransform, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f));
        _artFallbackText.text = "ILLUSTRATION\nPLACEHOLDER";
        _artFallbackText.alignment = TextAnchor.MiddleCenter;
        _artFallbackText.verticalOverflow = VerticalWrapMode.Overflow;
        _artFallbackText.gameObject.SetActive(false);

        _rulesPanel = EnsureRect(_cardRoot, "CardRulesPanel");
        SetAnchors(_rulesPanel, new Vector2(0.05f, 0.16f), new Vector2(0.95f, 0.37f));
        EnsureImage(_rulesPanel, PanelFill, false);
        EnsureOutline(_rulesPanel, PanelBorder);
        _rulesText = EnsureText(EnsureRect(_rulesPanel, "RulesSummary"), "Text", 10, TextPrimary);
        SetAnchors(_rulesText.rectTransform, new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.92f));
        _rulesText.alignment = TextAnchor.UpperLeft;
        _rulesText.verticalOverflow = VerticalWrapMode.Truncate;

        _statsRoot = EnsureRect(_cardRoot, "CardStats");
        SetAnchors(_statsRoot, new Vector2(0.05f, 0.055f), new Vector2(0.95f, 0.145f));
        _attackBadge = BuildStatBadge(_statsRoot, "AttackBadge", "ATK", AttackAccent, out _attackLabel, out _attackValue);
        SetAnchors(_attackBadge, new Vector2(0f, 0f), new Vector2(0.48f, 1f));
        _healthBadge = BuildStatBadge(_statsRoot, "HealthBadge", "HP", HealthAccent, out _healthLabel, out _healthValue);
        SetAnchors(_healthBadge, new Vector2(0.52f, 0f), new Vector2(1f, 1f));

        var identityRoot = EnsureRect(_cardRoot, "CardIdentity");
        SetAnchors(identityRoot, new Vector2(0.06f, 0.005f), new Vector2(0.94f, 0.052f));
        _identityText = EnsureText(identityRoot, "Identity", 8, TextMuted);
        _identityText.alignment = TextAnchor.MiddleCenter;
        _identityText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _identityText.verticalOverflow = VerticalWrapMode.Truncate;
        _identityText.gameObject.SetActive(false);

        _interactionMarker = EnsureRect(_cardRoot, "CardInteractionMarker");
        SetAnchors(_interactionMarker, new Vector2(0.30f, 0.665f), new Vector2(0.70f, 0.75f));
        EnsureImage(_interactionMarker, PanelFill, false);
        _interactionMarkerText = EnsureText(_interactionMarker, "Label", 9, Hex("F1C75B"));
        _interactionMarkerText.alignment = TextAnchor.MiddleCenter;
        _interactionMarkerText.fontStyle = FontStyle.Bold;
        _interactionMarker.gameObject.SetActive(false);

        _disabledVeil = EnsureRect(_cardRoot, "DisabledVeil");
        SetAnchors(_disabledVeil, Vector2.zero, Vector2.one);
        EnsureImage(_disabledVeil, new Color(0.02f, 0.04f, 0.05f, 0.34f), false);
        _disabledVeil.SetAsLastSibling();
        _disabledVeil.gameObject.SetActive(false);

        _built = true;
        ApplyArtTexture(null);
        _statsRoot.gameObject.SetActive(false);
        SetDiagnosticsVisible(_diagnosticsVisible);
    }

    private void ApplyMode()
    {
        if (!_built) return;

        var compact = _mode == RuntimeCardFaceMode.Compact;
        var layout = _cardRoot.GetComponent<UnityEngine.UI.LayoutElement>();
        if (layout == null) layout = _cardRoot.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        layout.minWidth = compact ? 72f : 88f;
        layout.preferredWidth = compact ? CompactWidth : FullWidth;
        layout.flexibleWidth = 0f;
        layout.minHeight = compact ? 104f : 124f;
        layout.preferredHeight = compact ? CompactHeight : FullHeight;
        layout.flexibleHeight = 0f;

        _titleText.fontSize = compact ? 11 : 15;
        _metaText.fontSize = compact ? 7 : 9;
        _costLabel.fontSize = compact ? 6 : 8;
        _costValue.fontSize = compact ? 10 : 14;
        _punishLabel.fontSize = compact ? 6 : 8;
        _punishValue.fontSize = compact ? 10 : 14;
        _rulesText.fontSize = compact ? 8 : 10;
        _attackLabel.fontSize = compact ? 6 : 8;
        _attackValue.fontSize = compact ? 10 : 14;
        _healthLabel.fontSize = compact ? 6 : 8;
        _healthValue.fontSize = compact ? 10 : 14;
        _identityText.fontSize = compact ? 6 : 8;
        _artFallbackText.fontSize = compact ? 7 : 10;

        _rulesPanel.gameObject.SetActive(true);
        if (compact)
        {
            SetAnchors(_artPanel, new Vector2(0.05f, 0.43f), new Vector2(0.95f, 0.64f));
            SetAnchors(_rulesPanel, new Vector2(0.05f, 0.22f), new Vector2(0.95f, 0.41f));
            SetAnchors(_statsRoot, new Vector2(0.05f, 0.085f), new Vector2(0.95f, 0.20f));
            SetAnchors(_identityText.rectTransform, new Vector2(0.06f, 0.015f), new Vector2(0.94f, 0.07f));
        }
        else
        {
            SetAnchors(_artPanel, new Vector2(0.05f, 0.40f), new Vector2(0.95f, 0.64f));
            SetAnchors(_statsRoot, new Vector2(0.05f, 0.055f), new Vector2(0.95f, 0.145f));
            SetAnchors(_identityText.rectTransform, new Vector2(0.06f, 0.005f), new Vector2(0.94f, 0.052f));
        }
    }

    private void ApplyCardArt(RuntimeCardDisplayModel card)
    {
        Texture2D texture = null;
        if (_contentResolver != null)
        {
            var requestedArtId = string.IsNullOrWhiteSpace(card.ArtId)
                ? "card_art_" + (string.IsNullOrWhiteSpace(card.StableId) ? "missing" : card.StableId)
                : card.ArtId;
            try
            {
                texture = _contentResolver.GetCardArtForSkin(
                    string.IsNullOrWhiteSpace(_skinId) ? DefaultSkinId : _skinId,
                    string.IsNullOrWhiteSpace(card.StableId) ? "missing_card" : card.StableId,
                    requestedArtId);
            }
            catch (Exception)
            {
                // Presentation must remain alive if an optional skin/art
                // provider is unavailable or malformed. The art panel's own
                // text fallback remains visible in this case.
                texture = null;
            }
        }

        ApplyArtTexture(texture);
    }

    private void ApplyArtTexture(Texture2D texture)
    {
        if (_artImage == null || _artFallbackText == null) return;

        _artImage.texture = texture;
        _artImage.enabled = texture != null;
        var generatedPlaceholder = texture == null ||
            (texture.name != null && texture.name.IndexOf("placeholder", StringComparison.OrdinalIgnoreCase) >= 0);
        _usingArtFallback = generatedPlaceholder;
        _artFallbackText.gameObject.SetActive(generatedPlaceholder && _diagnosticsVisible);
        _artFallbackText.text = texture == null
            ? "ILLUSTRATION\nPLACEHOLDER"
            : "ILLUSTRATION\nPLACEHOLDER";
    }

    private void ApplyFactionAccent(string faction)
    {
        var accent = AccentForFaction(faction);
        var stripe = _header.Find("FactionStripe");
        if (stripe != null)
        {
            var image = stripe.GetComponent<UnityEngine.UI.Image>();
            if (image != null) image.color = accent;
        }

        if (_frameOutline != null)
            _frameOutline.effectColor = accent;
        if (_costBadge != null)
        {
            var outline = _costBadge.GetComponent<UnityEngine.UI.Outline>();
            if (outline != null) outline.effectColor = accent;
        }
    }

    private static RectTransform BuildStatBadge(
        RectTransform parent,
        string name,
        string label,
        Color accent,
        out UnityEngine.UI.Text labelText,
        out UnityEngine.UI.Text valueText)
    {
        var badge = EnsureRect(parent, name);
        EnsureImage(badge, PanelFill, false);
        EnsureOutline(badge, accent);

        var labelRoot = EnsureRect(badge, name + "Label");
        SetAnchors(labelRoot, new Vector2(0.04f, 0.50f), new Vector2(0.96f, 0.98f));
        labelText = EnsureText(labelRoot, "Value", 8, accent);
        labelText.text = label;
        labelText.alignment = TextAnchor.MiddleCenter;

        var valueRoot = EnsureRect(badge, name + "Value");
        SetAnchors(valueRoot, new Vector2(0.04f, 0.01f), new Vector2(0.96f, 0.58f));
        valueText = EnsureText(valueRoot, "Value", 14, TextPrimary);
        valueText.alignment = TextAnchor.MiddleCenter;
        valueText.fontStyle = FontStyle.Bold;
        return badge;
    }

    private static UnityEngine.UI.Image EnsureImage(
        RectTransform rect,
        Color color,
        bool raycastTarget)
    {
        var image = rect.GetComponent<UnityEngine.UI.Image>();
        if (image == null) image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = raycastTarget;
        return image;
    }

    private static UnityEngine.UI.Outline EnsureOutline(RectTransform rect, Color color)
    {
        var outline = rect.GetComponent<UnityEngine.UI.Outline>();
        if (outline == null) outline = rect.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(1f, 1f);
        outline.useGraphicAlpha = false;
        return outline;
    }

    private static RectTransform EnsureRect(Transform parent, string name)
    {
        var existing = parent.Find(name) as RectTransform;
        if (existing != null) return existing;

        var objectRect = new GameObject(name, typeof(RectTransform));
        objectRect.transform.SetParent(parent, false);
        return objectRect.GetComponent<RectTransform>();
    }

    private static UnityEngine.UI.Text EnsureText(
        RectTransform parent,
        string name,
        int size,
        Color color)
    {
        var textRoot = parent.Find(name) as RectTransform;
        if (textRoot == null)
        {
            textRoot = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            textRoot.SetParent(parent, false);
        }

        var text = textRoot.GetComponent<UnityEngine.UI.Text>();
        if (text == null) text = textRoot.gameObject.AddComponent<UnityEngine.UI.Text>();
        text.font = Resources.GetBuiltinResource<Font>(RuntimeBattlePanelDefaults.LegacyBuiltinFontResource);
        text.fontSize = size;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        text.supportRichText = false;
        text.alignment = TextAnchor.UpperLeft;
        SetAnchors(text.rectTransform, Vector2.zero, Vector2.one);
        return text;
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetLayout(
        RectTransform rect,
        float minWidth,
        float preferredWidth,
        float flexibleWidth,
        float minHeight,
        float preferredHeight,
        float flexibleHeight)
    {
        var layout = rect.GetComponent<UnityEngine.UI.LayoutElement>();
        if (layout == null) layout = rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        layout.minWidth = minWidth;
        layout.preferredWidth = preferredWidth;
        layout.flexibleWidth = flexibleWidth;
        layout.minHeight = minHeight;
        layout.preferredHeight = preferredHeight;
        layout.flexibleHeight = flexibleHeight;
    }

    private static string BuildMetaText(RuntimeCardDisplayModel card)
    {
        var sealedSuffix = card.IsSealed ? " · SEALED" : string.Empty;
        return Display(card.Type) + " · " + Display(card.Faction) + sealedSuffix;
    }

    private static string BuildRulesText(RuntimeCardDisplayModel card)
    {
        var rules = Display(card.RulesText);
        var keywords = card.Keywords == null || card.Keywords.Count == 0
            ? string.Empty
            : "\n[" + string.Join(" · ", card.Keywords) + "]";
        return "RULES " + rules + keywords;
    }

    private static bool UsesRuntimeStats(RuntimeCardZone zone)
    {
        return zone == RuntimeCardZone.OwnField ||
            zone == RuntimeCardZone.OpponentField ||
            zone == RuntimeCardZone.OwnLeader ||
            zone == RuntimeCardZone.OpponentLeader;
    }

    private static string BuildIdentityText(RuntimeCardDisplayModel card)
    {
        var stableId = Display(card.StableId);
        var zone = Display(card.ZoneLabel);
        var entity = card.EntityId > 0
            ? card.EntityId.ToString(CultureInfo.InvariantCulture)
            : RuntimeCardDisplayModel.Unavailable;
        return "ID " + stableId + " · ENTITY " + entity + " · " + zone;
    }

    private static string DisplayNumber(
        int? value,
        string fallback = RuntimeCardDisplayModel.Unavailable)
    {
        return value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : fallback;
    }

    private static string Display(string value, string fallback = RuntimeCardDisplayModel.Unavailable)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static Color AccentForFaction(string faction)
    {
        if (string.Equals(faction, "烈焰帝国", StringComparison.Ordinal)) return Hex("E36D78");
        if (string.Equals(faction, "机械遗迹", StringComparison.Ordinal)) return Hex("63D7E5");
        if (string.Equals(faction, "深海联盟", StringComparison.Ordinal)) return Hex("559AD0");
        if (string.Equals(faction, "古木圣地", StringComparison.Ordinal)) return Hex("67D39B");
        return FrameBorder;
    }

    private static Color Hex(string value)
    {
        return ColorUtility.TryParseHtmlString("#" + value, out var color)
            ? color
            : Color.magenta;
    }
}
}
