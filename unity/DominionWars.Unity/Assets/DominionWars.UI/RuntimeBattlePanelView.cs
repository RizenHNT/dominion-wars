#nullable disable

using System;
using System.Text;
using DominionWars.Unity.Runtime;
using UnityEngine;

namespace DominionWars.Unity.UI
{

/// <summary>
/// Programmatic uGUI tabletop for the runtime battle slice.
///
/// This is an authored placeholder composition rather than a debug
/// inspector: the table has opponent/own hand lanes, mirrored pile rails, a
/// central neutral castle, and a compact action/event rail. All colour,
/// borders and card silhouettes are generated with built-in uGUI primitives;
/// no external art or package is required. RuntimeBattlePanel supplies the
/// authoritative snapshot values after this structure is built.
/// </summary>
public sealed class RuntimeBattlePanelView
{
    public RectTransform ContentRoot { get; private set; }
    public RectTransform HeaderRoot { get; private set; }
    public RectTransform BoardRoot { get; private set; }
    public RectTransform LeftRailRoot { get; private set; }
    public RectTransform MainBattleRoot { get; private set; }
    public RectTransform TargetZonesRoot { get; private set; }
    public RectTransform RightRailRoot { get; private set; }
    public RectTransform OpponentRoot { get; private set; }
    public RectTransform OpponentHandRoot { get; private set; }
    public RectTransform OpponentAmbushRoot { get; private set; }
    public RectTransform OpponentAmbushCardsRoot { get; private set; }
    public RectTransform OpponentFieldRoot { get; private set; }
    public RectTransform OpponentDeckRoot { get; private set; }
    public RectTransform OpponentGraveyardRoot { get; private set; }
    public RectTransform OpponentExileRoot { get; private set; }
    public RectTransform OpponentLeaderRoot { get; private set; }
    public RectTransform OpponentPhaseRoot { get; private set; }
    public RectTransform PhaseActionsRoot { get; private set; }
    public RectTransform PhaseActionsContent { get; private set; }
    public RectTransform CenterRoot { get; private set; }
    public RectTransform CenterPilesRoot { get; private set; }
    public RectTransform CastleRoot { get; private set; }
    public RectTransform CastleBarrierRoot { get; private set; }
    public RectTransform PhaseRoot { get; private set; }
    public RectTransform CommitCardsRoot { get; private set; }
    public RectTransform CommitDropSurface { get; private set; }
    public RectTransform CloudCardsRoot { get; private set; }
    public RectTransform MechanicalRoot { get; private set; }
    public RectTransform OwnRoot { get; private set; }
    public RectTransform OwnHandScrollRoot { get; private set; }
    public RectTransform OwnHandViewport { get; private set; }
    public UnityEngine.UI.ScrollRect OwnHandScroll { get; private set; }
    public UnityEngine.UI.Scrollbar OwnHandScrollbar { get; private set; }
    public RectTransform OwnHandRoot { get; private set; }
    public RectTransform OwnAmbushRoot { get; private set; }
    public RectTransform OwnAmbushCardsRoot { get; private set; }
    public RectTransform OwnAmbushDropSurface { get; private set; }
    public RectTransform OwnFieldRoot { get; private set; }
    public RectTransform OwnFieldDropSurface { get; private set; }
    public RectTransform OwnDeckRoot { get; private set; }
    public RectTransform OwnGraveyardRoot { get; private set; }
    public RectTransform OwnExileRoot { get; private set; }
    public RectTransform OwnLeaderRoot { get; private set; }
    public RectTransform OwnHandDropSurface { get; private set; }
    public RectTransform OwnPhaseRoot { get; private set; }
    public RectTransform DiscardActionsRoot { get; private set; }
    public RectTransform DiscardActionsContent { get; private set; }
    public RectTransform FooterRoot { get; private set; }
    public RectTransform ActionsArea { get; private set; }
    public RectTransform ActionsScrollRoot { get; private set; }
    public RectTransform ActionsViewport { get; private set; }
    public RectTransform EventsArea { get; private set; }
    public RectTransform EventsScrollRoot { get; private set; }
    public RectTransform EventsViewport { get; private set; }
    public RectTransform EventsContent { get; private set; }
    public RectTransform ActionsDrawerRoot { get; private set; }
    public RectTransform ActionsDrawerScrollRoot { get; private set; }
    public RectTransform ActionsDrawerViewport { get; private set; }
    public RectTransform ActionsDrawerContent { get; private set; }
    public RectTransform CardInspectRoot { get; private set; }
    public UnityEngine.UI.RawImage CardInspectArt { get; private set; }
    public UnityEngine.UI.Text CardInspectTitle { get; private set; }
    public UnityEngine.UI.Text CardInspectSubtitle { get; private set; }
    public UnityEngine.UI.Text CardInspectSummary { get; private set; }
    public UnityEngine.UI.Text CardInspectDetail { get; private set; }

    // Stable references retained for binding tests and concise tabletop
    // summaries. Dynamic cards/piles are children of the explicit roots above.
    public UnityEngine.UI.Text StatusText { get; private set; }
    public UnityEngine.UI.Button RecoveryButton { get; private set; }
    public RectTransform ReducedMotionToggleRoot { get; private set; }
    public UnityEngine.UI.Toggle ReducedMotionToggle { get; private set; }
    public RectTransform LanguageSelectorRoot { get; private set; }
    public UnityEngine.UI.Text LanguageSelectorLabel { get; private set; }
    public UnityEngine.UI.Button LanguageEnglishButton { get; private set; }
    public UnityEngine.UI.Button LanguageChineseButton { get; private set; }
    public UnityEngine.UI.Button LanguageJapaneseButton { get; private set; }
    public RectTransform FeedbackRoot { get; private set; }
    public UnityEngine.UI.Image FeedbackBackground { get; private set; }
    public UnityEngine.UI.Text FeedbackText { get; private set; }
    public UnityEngine.UI.Text MatchText { get; private set; }
    public UnityEngine.UI.Text OpponentText { get; private set; }
    public UnityEngine.UI.Text CastleText { get; private set; }
    public UnityEngine.UI.Text OwnText { get; private set; }
    public UnityEngine.UI.Text PhaseText { get; private set; }
    public UnityEngine.UI.Text EventsText { get; private set; }
    public RectTransform ActionsRoot { get; private set; }
    public UnityEngine.UI.Button MoreActionsButton { get; private set; }
    public UnityEngine.UI.Button ActionsDrawerCloseButton { get; private set; }
    public RectTransform PauseDrawerRoot { get; private set; }
    public UnityEngine.UI.Button PauseContinueButton { get; private set; }
    public UnityEngine.UI.Button PauseSettingsButton { get; private set; }
    public UnityEngine.UI.Button PauseMainMenuButton { get; private set; }
    public RectTransform PauseSettingsRoot { get; private set; }
    public UnityEngine.UI.Button PauseSettingsBackButton { get; private set; }
    public RectTransform DebugOverlayRoot { get; private set; }
    public UnityEngine.UI.Text DebugOverlayText { get; private set; }

    private static readonly Color TableBackground = Hex("071117");
    private static readonly Color PanelBorder = Hex("365568");
    private static readonly Color OpponentPanel = Hex("151B2B");
    private static readonly Color NeutralPanel = Hex("10232B");
    private static readonly Color OwnPanel = Hex("10251F");
    private static readonly Color ActionPanel = Hex("101E2C");
    private static readonly Color EventPanel = Hex("111A22");
    private static readonly Color Gold = Hex("E3B85A");
    private static readonly Color Cyan = Hex("63D7E5");
    private static readonly Color Green = Hex("67D39B");
    private static readonly Color Red = Hex("E36D78");
    private static readonly Color Muted = Hex("93A8B4");
    private static readonly Color PublicGoalText = Hex("D9F7FC");
    private bool _moreActionsAvailable;

    private RuntimeBattlePanelView() { }

    public static RuntimeBattlePanelView Build(Transform parent)
    {
        if (parent == null) throw new ArgumentNullException(nameof(parent));

        ConfigureCanvas(parent.GetComponentInParent<UnityEngine.Canvas>());
        var view = new RuntimeBattlePanelView();

        view.ContentRoot = CreateRect("RuntimeBattlePanelContent", parent);
        // Keep the full tabletop inside the compact 1280x720 safe frame. The
        // runtime layout contract starts its outer zones at three percent,
        // which also leaves enough room for outlines and drag highlights.
        view.ContentRoot.anchorMin = new Vector2(0.03f, 0.03f);
        view.ContentRoot.anchorMax = new Vector2(0.97f, 0.97f);
        view.ContentRoot.offsetMin = Vector2.zero;
        view.ContentRoot.offsetMax = Vector2.zero;
        AddPanelBackground(view.ContentRoot, TableBackground, PanelBorder, 0.96f);

        // Fine inset rail: it makes the three board zones read as one table.
        var tableRail = CreateRect("TableRail", view.ContentRoot);
        SetAnchors(tableRail, new Vector2(0.012f, 0.012f), new Vector2(0.988f, 0.988f));
        AddPanelBackground(tableRail, new Color(0.04f, 0.12f, 0.15f, 0.22f), new Color(0.15f, 0.35f, 0.40f, 0.30f), 0.45f);

        // Header: status, phase/turn/player, and match/revision.
        view.HeaderRoot = CreateRect("RuntimeBattlePanelHeader", view.ContentRoot);
        SetAnchors(view.HeaderRoot, new Vector2(0.018f, 0.905f), new Vector2(0.982f, 0.982f));
        AddPanelBackground(view.HeaderRoot, Hex("0D1D27"), PanelBorder, 0.98f);
        view.StatusText = CreateText(view.HeaderRoot, "RuntimeBattlePanelStatus", 18, Gold);
        SetAnchors(view.StatusText.rectTransform, new Vector2(0.016f, 0.05f), new Vector2(0.17f, 0.95f));
        view.StatusText.alignment = TextAnchor.MiddleLeft;
        view.StatusText.fontStyle = FontStyle.Bold;

        // The accessibility switch occupies a dedicated header slot. It is
        // deliberately outside both the board and the footer rails, so it
        // cannot cover a card/action target or the adapter event timeline.
        view.ReducedMotionToggleRoot = CreateRect(
            "RuntimeBattlePanelReducedMotion",
            view.HeaderRoot);
        SetAnchors(
            view.ReducedMotionToggleRoot,
            new Vector2(0.18f, 0.04f),
            new Vector2(0.28f, 0.96f));
        var toggleLayout = view.ReducedMotionToggleRoot.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        toggleLayout.minWidth = 112f;
        toggleLayout.preferredWidth = 120f;
        toggleLayout.minHeight = 44f;
        toggleLayout.preferredHeight = 44f;
        var toggleBackground = view.ReducedMotionToggleRoot.gameObject.AddComponent<UnityEngine.UI.Image>();
        toggleBackground.color = new Color(0.06f, 0.13f, 0.17f, 0.92f);
        toggleBackground.raycastTarget = true;
        var toggleOutline = view.ReducedMotionToggleRoot.gameObject.AddComponent<UnityEngine.UI.Outline>();
        toggleOutline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.72f);
        toggleOutline.effectDistance = new Vector2(1f, 1f);
        var reducedToggle = view.ReducedMotionToggleRoot.gameObject.AddComponent<UnityEngine.UI.Toggle>();
        reducedToggle.targetGraphic = toggleBackground;
        var toggleColors = reducedToggle.colors;
        toggleColors.normalColor = Color.white;
        toggleColors.highlightedColor = new Color(0.88f, 0.98f, 1f, 1f);
        toggleColors.pressedColor = new Color(0.70f, 0.91f, 0.95f, 1f);
        toggleColors.selectedColor = new Color(0.88f, 0.98f, 1f, 1f);
        reducedToggle.colors = toggleColors;
        var checkmarkRoot = CreateRect("Checkmark", view.ReducedMotionToggleRoot);
        SetAnchors(checkmarkRoot, new Vector2(0.05f, 0.18f), new Vector2(0.27f, 0.82f));
        var checkmark = checkmarkRoot.gameObject.AddComponent<UnityEngine.UI.Image>();
        checkmark.color = Green;
        checkmark.raycastTarget = false;
        reducedToggle.graphic = checkmark;
        var toggleLabel = CreateText(
            view.ReducedMotionToggleRoot,
            "ReducedMotionLabel",
            10,
            Muted);
        toggleLabel.text = "REDUCED\nMOTION";
        toggleLabel.alignment = TextAnchor.MiddleCenter;
        toggleLabel.raycastTarget = false;
        view.ReducedMotionToggle = reducedToggle;

        // Presentation-only event feedback lives in the unused middle of the
        // header. It is deliberately non-interactive so it cannot steal a
        // card/action pointer, and remains useful when a target is absent.
        view.FeedbackRoot = CreateRect("RuntimeBattlePanelFeedback", view.HeaderRoot);
        SetAnchors(view.FeedbackRoot, new Vector2(0.29f, 0.08f), new Vector2(0.71f, 0.92f));
        view.FeedbackBackground = view.FeedbackRoot.gameObject.AddComponent<UnityEngine.UI.Image>();
        view.FeedbackBackground.color = new Color(0.10f, 0.18f, 0.22f, 0.34f);
        view.FeedbackBackground.raycastTarget = false;
        view.FeedbackText = CreateText(view.FeedbackRoot, "RuntimeBattlePanelFeedbackText", 14, new Color(0.78f, 0.86f, 0.90f));
        view.FeedbackText.alignment = TextAnchor.MiddleCenter;
        view.FeedbackText.fontStyle = FontStyle.Bold;
        view.FeedbackText.text = "READY";

        var recoveryObject = new GameObject(
            "RuntimeBattlePanelRecoveryButton",
            typeof(RectTransform),
            typeof(UnityEngine.UI.Image),
            typeof(UnityEngine.UI.Button));
        recoveryObject.transform.SetParent(view.HeaderRoot, false);
        var recoveryRect = recoveryObject.GetComponent<RectTransform>();
        SetAnchors(recoveryRect, new Vector2(0.715f, 0.08f), new Vector2(0.815f, 0.92f));
        var recoveryImage = recoveryObject.GetComponent<UnityEngine.UI.Image>();
        recoveryImage.color = new Color(0.22f, 0.12f, 0.10f, 0.92f);
        recoveryImage.raycastTarget = true;
        var recoveryButton = recoveryObject.GetComponent<UnityEngine.UI.Button>();
        recoveryButton.targetGraphic = recoveryImage;
        recoveryButton.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        var recoveryLabel = CreateText(recoveryRect, "Label", 11, Color.white);
        recoveryLabel.text = "MENU";
        recoveryLabel.alignment = TextAnchor.MiddleCenter;
        recoveryLabel.raycastTarget = false;
        view.RecoveryButton = recoveryButton;

        view.MatchText = CreateText(view.HeaderRoot, "RuntimeBattlePanelMatch", 16, new Color(0.82f, 0.90f, 0.94f));
        SetAnchors(view.MatchText.rectTransform, new Vector2(0.825f, 0.05f), new Vector2(0.984f, 0.95f));
        view.MatchText.alignment = TextAnchor.MiddleRight;

        // BoardRoot is an anchored canvas for the lanes. The disabled layout
        // component is retained as a compatibility marker for older tests;
        // children use explicit anchors so they do not collapse into a debug
        // column at different aspect ratios.
        view.BoardRoot = CreateRect("RuntimeBattlePanelBoard", view.ContentRoot);
        SetAnchors(view.BoardRoot, new Vector2(0.012f, 0.145f), new Vector2(0.988f, 0.895f));
        var boardLayout = view.BoardRoot.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        boardLayout.enabled = false;
        boardLayout.childControlWidth = false;
        boardLayout.childControlHeight = false;
        boardLayout.childForceExpandWidth = false;
        boardLayout.childForceExpandHeight = false;

        BuildSideRails(view);
        BuildOpponentLane(view);
        BuildCenterLane(view);
        BuildOwnLane(view);
        BuildTargetLayer(view);
        BuildFooter(view);
        BuildPauseDrawer(view);
        BuildCardInspect(view);
        BuildDebugOverlay(view);
        ArrangeVisualLayers(view);

        return view;
    }

    private static void ArrangeVisualLayers(RuntimeBattlePanelView view)
    {
        // Keep advertised semantic surfaces underneath the authored lanes.
        // They are still raycastable in their own empty space, while a card
        // child remains the top hit when the two rectangles overlap.
        view.TargetZonesRoot.SetAsFirstSibling();
        view.OpponentHandRoot.SetAsLastSibling();
        // The hand cards now live under a masked horizontal viewport. Keep
        // the viewport above the semantic drop surface and the rest of the
        // own lane; the cards themselves remain its later content children.
        (view.OwnHandScrollRoot ?? view.OwnHandRoot).SetAsLastSibling();
        if (view.DiscardActionsRoot != null) view.DiscardActionsRoot.SetAsLastSibling();
        view.OwnFieldDropSurface.SetAsFirstSibling();
        view.OwnHandDropSurface.SetAsFirstSibling();
        view.CommitDropSurface.SetAsFirstSibling();
        view.OwnAmbushDropSurface.SetAsFirstSibling();

        // The event/feedback rails are presentation overlays, not gameplay
        // targets. Keep their existing rectangles, but make the sibling order
        // explicit so later content cannot accidentally cover their text.
        view.FeedbackRoot.SetAsLastSibling();
        view.EventsArea.SetAsLastSibling();
        view.ActionsArea.SetAsLastSibling();
        view.PauseDrawerRoot.SetAsLastSibling();
    }

    public void SetDebugOverlayVisible(bool visible)
    {
        if (DebugOverlayRoot == null) return;
        DebugOverlayRoot.gameObject.SetActive(
            visible && (Application.isEditor || Debug.isDebugBuild));
    }

    /// <summary>
    /// Opens the large presentation-only card reader. It is never populated
    /// from a hidden zone; RuntimeBattlePanel attaches inspection only to card
    /// roots that already exist in the viewer's public snapshot.
    /// </summary>
    public void ShowCardInspect(RuntimeCardInspectModel model, Texture2D art)
    {
        ShowCardInspect(model, art, null, "en");
    }

    /// <summary>
    /// Opens the reader with presentation labels from the shared resolver.
    /// Authored card content (name, rules, keywords and tag values) remains
    /// supplied by the card display model rather than being translated here.
    /// </summary>
    public void ShowCardInspect(
        RuntimeCardInspectModel model,
        Texture2D art,
        RuntimeLocalizationResolver localizationResolver,
        string language = "en")
    {
        // The pause surface owns input while it is open. Do not let a late
        // card click or snapshot callback reopen the reader over pause UI.
        if (model == null || CardInspectRoot == null || PauseMenuOpen) return;
        CardInspectTitle.text = model.Title;
        CardInspectSubtitle.text = model.TypeFactionLine;
        CardInspectSummary.text = BuildCardInspectSummary(model);
        CardInspectDetail.text = BuildCardInspectBody(model, localizationResolver, language);
        CardInspectArt.texture = art;
        CardInspectArt.enabled = art != null;
        CardInspectRoot.gameObject.SetActive(true);
        CardInspectRoot.SetAsLastSibling();

        // Every new inspection starts at the top of the canonical body. The
        // existing ScrollRect remains the only reader scroll surface; this
        // prevents a previous card's bottom position from hiding the new
        // card's effect heading after a snapshot/card switch.
        var inspectScroll = CardInspectRoot.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
        if (inspectScroll != null)
            inspectScroll.verticalNormalizedPosition = 1f;

        var content = CardInspectDetail.transform.parent as RectTransform;
        if (content != null)
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
    }

    private static string BuildCardInspectSummary(RuntimeCardInspectModel model)
    {
        var summary = model.PunishAndCostLine;
        var stats = !string.IsNullOrWhiteSpace(model.CurrentStatsLine)
            ? model.CurrentStatsLine
            : model.PrintedStatsLine;
        if (!string.IsNullOrWhiteSpace(stats)) summary += "\n" + stats;
        return summary;
    }

    private static string BuildCardInspectBody(
        RuntimeCardInspectModel model,
        RuntimeLocalizationResolver localizationResolver,
        string language)
    {
        var builder = new StringBuilder(256);
        AppendCardInspectSection(builder, CardLabel(localizationResolver, "card.effect", "EFFECT", language), model.RulesText);
        AppendCardInspectSection(builder, CardLabel(localizationResolver, "card.fees", "FEES", language), model.MechanicalFeesLine);
        AppendCardInspectSection(builder, CardLabel(localizationResolver, "card.goal", "GOAL", language), model.LeaderWinText);
        AppendCardInspectSection(builder, CardLabel(localizationResolver, "card.progress", "PROGRESS", language), model.ChantLine);
        AppendCardInspectSection(builder, CardLabel(localizationResolver, "card.landmark", "LANDMARK", language), model.LandmarkProgressLine);
        // These two contract lines already carry their canonical KEYWORDS/TAGS
        // prefix. Keep the source wording once rather than rendering a
        // duplicated reader heading around it.
        AppendCardInspectLine(builder, model.KeywordsLine);
        AppendCardInspectLine(builder, model.TagsLine);
        return builder.ToString();
    }

    private static string CardLabel(
        RuntimeLocalizationResolver localizationResolver,
        string localizationKey,
        string fallback,
        string language)
    {
        return localizationResolver == null
            ? fallback
            : localizationResolver.Get(localizationKey, language);
    }

    private static void AppendCardInspectLine(StringBuilder builder, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (builder.Length > 0) builder.Append('\n');
        builder.Append(value).Append('\n');
    }

    private static void AppendCardInspectSection(
        StringBuilder builder,
        string label,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (builder.Length > 0) builder.Append('\n');
        builder.Append(label).Append('\n').Append(value).Append('\n');
    }

    public void HideCardInspect()
    {
        if (CardInspectRoot != null) CardInspectRoot.gameObject.SetActive(false);
    }

    public void SetMoreActionsAvailable(bool available)
    {
        _moreActionsAvailable = available;
        if (!available) SetMoreActionsOpen(false);
        else if (MoreActionsButton != null)
            MoreActionsButton.gameObject.SetActive(!MoreActionsOpen);
    }

    public bool MoreActionsOpen => ActionsDrawerRoot != null && ActionsDrawerRoot.gameObject.activeSelf;

    public void SetMoreActionsOpen(bool open)
    {
        if (ActionsDrawerRoot == null) return;
        if (open && !_moreActionsAvailable) return;
        if (open && PauseMenuOpen)
            SetPauseMenuOpen(false);
        ActionsDrawerRoot.gameObject.SetActive(open);
        if (MoreActionsButton != null)
            MoreActionsButton.gameObject.SetActive(_moreActionsAvailable && !open);
    }

    public bool PauseMenuOpen => PauseDrawerRoot != null && PauseDrawerRoot.gameObject.activeSelf;

    public bool PauseSettingsOpen => PauseSettingsRoot != null && PauseSettingsRoot.gameObject.activeSelf;

    public void SetPauseMenuOpen(bool open)
    {
        if (PauseDrawerRoot == null) return;
        if (open && MoreActionsOpen)
            SetMoreActionsOpen(false);
        if (open)
        {
            HideCardInspect();
            PauseDrawerRoot.SetAsLastSibling();
        }
        PauseDrawerRoot.gameObject.SetActive(open);
        if (!open && PauseSettingsRoot != null)
            PauseSettingsRoot.gameObject.SetActive(false);
    }

    public void SetPauseSettingsOpen(bool open)
    {
        if (PauseSettingsRoot == null) return;
        if (open) SetPauseMenuOpen(true);
        PauseSettingsRoot.gameObject.SetActive(open);
    }

    private static void BuildSideRails(RuntimeBattlePanelView view)
    {
        view.LeftRailRoot = CreateRect("RuntimeBattlePanelLeftRail", view.BoardRoot);
        SetAnchors(view.LeftRailRoot, new Vector2(0f, 0.01f), new Vector2(0.13f, 0.99f));
        AddPanelBackground(view.LeftRailRoot, Hex("0D1820"), PanelBorder, 0.96f);
        ConfigureVerticalRail(view.LeftRailRoot);
        CreateLayoutCaption(view.LeftRailRoot, "LEFT RAIL", Cyan);

        view.OpponentDeckRoot = CreatePile(view.LeftRailRoot, "OpponentDeck", "DECK", Vector2.zero, Vector2.zero, new Color(0.27f, 0.56f, 0.70f));
        view.OpponentGraveyardRoot = CreatePile(view.LeftRailRoot, "OpponentGraveyard", "GRAVE", Vector2.zero, Vector2.zero, new Color(0.55f, 0.43f, 0.66f));
        view.OpponentExileRoot = CreatePile(view.LeftRailRoot, "OpponentExile", "EXILE", Vector2.zero, Vector2.zero, new Color(0.45f, 0.65f, 0.70f));
        // The current snapshot has no Exile field. Retain the root as a
        // semantic/layout hook, but keep the absent zone off the player-facing
        // surface until the contract supplies an authoritative value.
        view.OpponentExileRoot.gameObject.SetActive(false);
        view.OpponentPhaseRoot = CreateQueueSlot(
            view.LeftRailRoot,
            "OpponentPhaseAmbush",
            "PHASE STATUS",
            "—",
            new Color(0.93f, 0.65f, 0.74f),
            Vector2.zero,
            Vector2.zero,
            statusOnly: true);
        CreateFlexibleSpacer(view.LeftRailRoot, "LeftRailSpacer");

        view.CenterPilesRoot = CreateRect("CenterPiles", view.LeftRailRoot);
        AddPanelBackground(view.CenterPilesRoot, Hex("111F28"), new Color(0.31f, 0.61f, 0.70f, 0.6f), 0.96f);
        AddLayout(view.CenterPilesRoot, 104f, 112f, 0f, 56f, 96f, 1f);
        ConfigureVerticalRail(view.CenterPilesRoot, 2, 2, 3f);
        CreateQueueSlot(view.CenterPilesRoot, "CenterCloudMirror", "CLOUD", "—", new Color(0.40f, 0.72f, 0.82f), Vector2.zero, Vector2.zero);
        CreateQueueSlot(view.CenterPilesRoot, "CenterCommitMirror", "SUBMIT QUEUE", "—", new Color(0.42f, 0.77f, 0.60f), Vector2.zero, Vector2.zero);
        view.OwnPhaseRoot = CreateQueueSlot(
            view.LeftRailRoot,
            "OwnPhaseAmbush",
            "AMBUSH QUEUE",
            "—",
            new Color(0.45f, 0.85f, 0.68f),
            Vector2.zero,
            Vector2.zero,
            statusOnly: true);
        view.PhaseActionsRoot = CreatePhaseActionsSurface(
            view.LeftRailRoot,
            "RuntimeBattlePanelPhaseActions",
            "PHASE ACTION",
            new Color(0.45f, 0.85f, 0.68f),
            out var phaseActionsContent);
        view.PhaseActionsContent = phaseActionsContent;
        view.PhaseActionsRoot.gameObject.SetActive(false);

        view.MainBattleRoot = CreateRect("RuntimeBattlePanelMainBattle", view.BoardRoot);
        SetAnchors(view.MainBattleRoot, new Vector2(0.14f, 0.01f), new Vector2(0.86f, 0.99f));

        view.RightRailRoot = CreateRect("RuntimeBattlePanelRightRail", view.BoardRoot);
        SetAnchors(view.RightRailRoot, new Vector2(0.87f, 0.01f), new Vector2(1f, 0.99f));
        AddPanelBackground(view.RightRailRoot, Hex("0D1820"), PanelBorder, 0.96f);
        ConfigureVerticalRail(view.RightRailRoot);
        CreateLayoutCaption(view.RightRailRoot, "RIGHT RAIL", Cyan);

        view.MechanicalRoot = CreateRect("MechanicalCloudCommit", view.RightRailRoot);
        AddPanelBackground(view.MechanicalRoot, Hex("17232D"), new Color(0.37f, 0.68f, 0.76f, 0.65f), 0.96f);
        AddLayout(view.MechanicalRoot, 164f, 176f, 0f, 56f, 96f, 1f);
        ConfigureVerticalRail(view.MechanicalRoot, 2, 2, 3f);
        CreateLayoutCaption(view.MechanicalRoot, "QUEUE / CLOUD", Cyan);
        CreateQueueSlot(view.MechanicalRoot, "CommitQueueMirror", "AMBUSH / SUBMIT", "—", new Color(0.93f, 0.65f, 0.74f), Vector2.zero, Vector2.zero);
        CreateQueueSlot(view.MechanicalRoot, "CloudSlot", "CLOUD", "—", new Color(0.40f, 0.72f, 0.82f), Vector2.zero, Vector2.zero);
        CreateQueueSlot(view.MechanicalRoot, "CommitQueueSlot", "COMMIT QUEUE", "—", new Color(0.42f, 0.77f, 0.60f), Vector2.zero, Vector2.zero);
        CreateFlexibleSpacer(view.RightRailRoot, "RightRailSpacer");
        view.OwnExileRoot = CreatePile(view.RightRailRoot, "OwnExile", "EXILE", Vector2.zero, Vector2.zero, new Color(0.45f, 0.65f, 0.70f));
        // See OpponentExileRoot above: keep the future hook without exposing
        // a fabricated count for a field that is not in the snapshot.
        view.OwnExileRoot.gameObject.SetActive(false);
        view.OwnGraveyardRoot = CreatePile(view.RightRailRoot, "OwnGraveyard", "GRAVE", Vector2.zero, Vector2.zero, new Color(0.58f, 0.53f, 0.32f));
        view.OwnDeckRoot = CreatePile(view.RightRailRoot, "OwnDeck", "DECK", Vector2.zero, Vector2.zero, new Color(0.27f, 0.70f, 0.57f));
    }

    private static void BuildOpponentLane(RuntimeBattlePanelView view)
    {
        view.OpponentRoot = CreateRect("RuntimeBattlePanelOpponent", view.MainBattleRoot);
        SetAnchors(view.OpponentRoot, new Vector2(0f, 0.65f), new Vector2(1f, 1f));
        AddPanelBackground(view.OpponentRoot, OpponentPanel, new Color(0.55f, 0.33f, 0.52f, 0.85f), 0.98f);
        AddLayout(view.OpponentRoot, 96f, 128f, 0f, 0f, 0f, 1f);
        CreateZoneCaption(view.OpponentRoot, "OPPONENT  ·  FIELD / HIDDEN HAND", new Color(0.95f, 0.71f, 0.84f));
        view.OpponentText = CreateText(view.OpponentRoot, "RuntimeBattlePanelOpponent", 16, Color.white);
        SetAnchors(view.OpponentText.rectTransform, new Vector2(0.012f, 0.48f), new Vector2(0.16f, 0.86f));
        view.OpponentText.alignment = TextAnchor.UpperLeft;

        view.OpponentLeaderRoot = CreateLeaderSlot(view.OpponentRoot, "OpponentLeaderSlot", new Color(0.73f, 0.41f, 0.66f));
        SetAnchors(view.OpponentLeaderRoot, new Vector2(0.17f, 0.16f), new Vector2(0.29f, 0.86f));
        view.OpponentAmbushRoot = CreateAmbushZone(
            view.OpponentRoot,
            "OpponentAmbushZone",
            "AMBUSH",
            new Color(0.93f, 0.65f, 0.74f),
            out var opponentAmbushCards);
        view.OpponentAmbushCardsRoot = opponentAmbushCards;
        view.OpponentHandRoot = CreateCardStrip(view.OpponentRoot, "OpponentHandBacks", new Vector2(0.31f, 0.52f), new Vector2(0.99f, 0.98f), 52f, 88f, 4f);
        // Reserve enough vertical room for a compact battlefield card to keep
        // its title and live ATK/HP legible at the 1280x720 safe frame. The
        // opponent hand remains a redacted strip above it, so this is a safe
        // overlap within the opponent lane rather than a new screen region.
        view.OpponentFieldRoot = CreateCardStrip(view.OpponentRoot, "OpponentField", new Vector2(0.31f, 0.04f), new Vector2(0.99f, 0.66f), 82f, 104f, 6f);
    }

    private static void BuildCenterLane(RuntimeBattlePanelView view)
    {
        view.CenterRoot = CreateRect("RuntimeBattlePanelCenter", view.MainBattleRoot);
        SetAnchors(view.CenterRoot, new Vector2(0f, 0.36f), new Vector2(1f, 0.64f));
        AddPanelBackground(view.CenterRoot, NeutralPanel, new Color(0.26f, 0.67f, 0.70f, 0.80f), 0.98f);
        AddLayout(view.CenterRoot, 128f, 192f, 0f, 320f, 560f, 1f);
        AddTableDivider(view.CenterRoot, 0.08f, new Color(Cyan.r, Cyan.g, Cyan.b, 0.32f));

        view.CastleRoot = CreateCardFrame(view.CenterRoot, "RuntimeBattlePanelCastle", Hex("2A261B"), Gold, 176f, 204f);
        SetAnchors(view.CastleRoot, new Vector2(0.27f, 0.06f), new Vector2(0.73f, 0.78f));
        // Castle currently exposes only enabled + health. Keep this named root
        // for a future contract-backed extension, but do not render a barrier
        // label, value, or placeholder on the production surface.
        view.CastleBarrierRoot = CreateRect("CastleBarrier", view.CastleRoot);
        SetAnchors(view.CastleBarrierRoot, new Vector2(0.05f, 0.62f), new Vector2(0.95f, 0.86f));
        view.CastleBarrierRoot.gameObject.SetActive(false);
        view.CastleText = CreateText(view.CastleRoot, "RuntimeBattlePanelCastle", 22, Color.white);
        view.CastleText.fontStyle = FontStyle.Bold;
        view.CastleText.alignment = TextAnchor.MiddleCenter;
        SetAnchors(view.CastleText.rectTransform, new Vector2(0.08f, 0.24f), new Vector2(0.92f, 0.58f));
        var castleTitle = CreateText(view.CastleRoot, "CastleTitle", 18, Gold);
        castleTitle.text = "KINGDOM CORE";
        castleTitle.alignment = TextAnchor.MiddleCenter;
        castleTitle.fontStyle = FontStyle.Bold;
        SetAnchors(castleTitle.rectTransform, new Vector2(0.05f, 0.87f), new Vector2(0.95f, 0.99f));
        var castleType = CreateText(view.CastleRoot, "CastleType", 16, Muted);
        castleType.text = "NEUTRAL LANDMARK  ·  SHARED";
        castleType.alignment = TextAnchor.MiddleCenter;
        SetAnchors(castleType.rectTransform, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.15f));

        view.PhaseRoot = CreateRect("RuntimeBattlePanelCenterPhase", view.CenterRoot);
        SetAnchors(view.PhaseRoot, new Vector2(0.12f, 0.80f), new Vector2(0.88f, 0.98f));
        AddPanelBackground(view.PhaseRoot, Hex("183A48"), new Color(Cyan.r, Cyan.g, Cyan.b, 0.75f), 0.96f);
        view.PhaseText = CreateText(view.PhaseRoot, "RuntimeBattlePanelPhase", 18, Color.white);
        view.PhaseText.alignment = TextAnchor.MiddleCenter;
        view.PhaseText.fontStyle = FontStyle.Bold;
        SetAnchors(view.PhaseText.rectTransform, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.98f));

        view.CommitDropSurface = CreateSemanticDropSurface(
            view.CenterRoot,
            "CommitDropSurface",
            "DROP TO COMMIT",
            new Vector2(0.015f, 0.06f),
            new Vector2(0.255f, 0.76f));
        view.CommitCardsRoot = CreateCardStrip(
            view.CenterRoot,
            "CommitQueueCards",
            new Vector2(0.015f, 0.06f),
            new Vector2(0.255f, 0.76f),
            76f,
            104f,
            -34f);
        var commitLabel = CreateText(view.CenterRoot, "CommitQueueCardsLabel", 13, Green);
        commitLabel.text = "COMMIT  ·  FIRST →";
        commitLabel.fontStyle = FontStyle.Bold;
        commitLabel.alignment = TextAnchor.MiddleCenter;
        SetAnchors(commitLabel.rectTransform, new Vector2(0.015f, 0.76f), new Vector2(0.255f, 0.88f));

        view.CloudCardsRoot = CreateCardStrip(
            view.CenterRoot,
            "CloudStackCards",
            new Vector2(0.745f, 0.06f),
            new Vector2(0.985f, 0.76f),
            76f,
            104f,
            -34f);
        var cloudLabel = CreateText(view.CenterRoot, "CloudStackCardsLabel", 13, Cyan);
        cloudLabel.text = "CLOUD  ·  TOP →";
        cloudLabel.fontStyle = FontStyle.Bold;
        cloudLabel.alignment = TextAnchor.MiddleCenter;
        SetAnchors(cloudLabel.rectTransform, new Vector2(0.745f, 0.76f), new Vector2(0.985f, 0.88f));

    }

    private static void BuildOwnLane(RuntimeBattlePanelView view)
    {
        view.OwnRoot = CreateRect("RuntimeBattlePanelOwn", view.MainBattleRoot);
        SetAnchors(view.OwnRoot, new Vector2(0f, 0f), new Vector2(1f, 0.35f));
        AddPanelBackground(view.OwnRoot, OwnPanel, new Color(0.25f, 0.70f, 0.52f, 0.82f), 0.98f);
        AddLayout(view.OwnRoot, 96f, 168f, 0f, 0f, 0f, 1f);
        CreateZoneCaption(view.OwnRoot, "YOUR SIDE  ·  FIELD ABOVE / HAND BELOW", new Color(0.60f, 0.95f, 0.76f));
        view.OwnText = CreateText(view.OwnRoot, "RuntimeBattlePanelOwn", 16, Color.white);
        SetAnchors(view.OwnText.rectTransform, new Vector2(0.012f, 0.48f), new Vector2(0.16f, 0.86f));
        view.OwnText.alignment = TextAnchor.UpperLeft;

        view.OwnLeaderRoot = CreateLeaderSlot(view.OwnRoot, "OwnLeaderSlot", new Color(0.35f, 0.79f, 0.61f));
        SetAnchors(view.OwnLeaderRoot, new Vector2(0.17f, 0.14f), new Vector2(0.29f, 0.84f));
        view.OwnAmbushRoot = CreateAmbushZone(
            view.OwnRoot,
            "OwnAmbushZone",
            "DROP AMBUSH",
            new Color(0.45f, 0.85f, 0.68f),
            out var ownAmbushCards);
        view.OwnAmbushCardsRoot = ownAmbushCards;
        view.OwnAmbushDropSurface = CreateSemanticDropSurface(
            view.OwnAmbushRoot,
            "OwnAmbushDropSurface",
            string.Empty,
            Vector2.zero,
            Vector2.one);
        // Field and hand overlap slightly, like a physical tabletop. The hand
        // is created last and therefore fans in front without taking another
        // full debug row from the board.
        view.OwnFieldDropSurface = CreateSemanticDropSurface(
            view.OwnRoot,
            "OwnFieldDropSurface",
            "DROP TO FIELD",
            new Vector2(0.31f, 0.43f),
            new Vector2(0.99f, 0.98f));
        view.OwnFieldRoot = CreateCardStrip(view.OwnRoot, "OwnField", new Vector2(0.31f, 0.43f), new Vector2(0.99f, 0.98f), 92f, 112f, 6f);
        view.OwnHandDropSurface = CreateSemanticDropSurface(
            view.OwnRoot,
            "OwnHandDropSurface",
            "DROP TO HAND",
            new Vector2(0.31f, 0.02f),
            new Vector2(0.99f, 0.76f));
        view.OwnHandScrollRoot = CreateHorizontalCardScrollRoot(
            view.OwnRoot,
            "OwnHandFaceUpScrollRect",
            new Vector2(0.31f, 0.02f),
            new Vector2(0.99f, 0.76f),
            out var ownHandViewport,
            out var ownHandContent,
            out var ownHandScroll,
            out var ownHandScrollbar,
            112f,
            136f,
            6f,
            // Preserve the existing compact card's portrait proportion at
            // high hand counts; the ScrollRect handles overflow instead of
            // squeezing cards below a readable width.
            96f);
        view.OwnHandViewport = ownHandViewport;
        view.OwnHandRoot = ownHandContent;
        view.OwnHandScroll = ownHandScroll;
        view.OwnHandScrollbar = ownHandScrollbar;
        view.DiscardActionsRoot = CreateDiscardActionsSurface(
            view.OwnRoot,
            "RuntimeBattlePanelDiscardActions",
            out var discardActionsContent);
        view.DiscardActionsContent = discardActionsContent;
        view.DiscardActionsRoot.gameObject.SetActive(false);
    }

    private static void BuildTargetLayer(RuntimeBattlePanelView view)
    {
        // Target surfaces are a transparent semantic layer. It has no layout
        // group and no graphic of its own, so an advertised target can be
        // given a stable child surface without intercepting the card lanes.
        view.TargetZonesRoot = CreateRect("LegalTargetSurfaces", view.MainBattleRoot);
        SetAnchors(view.TargetZonesRoot, Vector2.zero, Vector2.one);
    }

    private static void BuildFooter(RuntimeBattlePanelView view)
    {
        view.FooterRoot = CreateRect("RuntimeBattlePanelFooter", view.ContentRoot);
        SetAnchors(view.FooterRoot, new Vector2(0.012f, 0.018f), new Vector2(0.988f, 0.132f));
        var footerLayout = view.FooterRoot.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        footerLayout.enabled = false;
        footerLayout.childControlWidth = false;
        footerLayout.childControlHeight = false;
        AddLayout(view.FooterRoot, 80f, 118f, 0f);

        view.ActionsArea = CreateRail(view.FooterRoot, "RuntimeBattlePanelActionsArea", "MAIN ACTIONS  ·  DRAG OR CLICK", ActionPanel, new Vector2(0.705f, 0.0f), new Vector2(1.0f, 1.0f));
        var actionsHeading = view.ActionsArea.Find("RuntimeBattlePanelActionsAreaTitle");
        if (actionsHeading != null) actionsHeading.gameObject.SetActive(false);
        RectTransform actionsViewport;
        RectTransform actionsRoot;
        // Keep a real safety reserve below the final action. The action rail is
        // intentionally compact at 1280x720, so the last button can be at the
        // clamped scroll position; two pixels of padding is not enough once
        // the button outline and Canvas scaling are applied.
        view.ActionsScrollRoot = CreateScrollRoot(
            view.ActionsArea,
            "RuntimeBattlePanelActionsScrollRect",
            out actionsViewport,
            out actionsRoot,
            8);
        SetAnchors(view.ActionsScrollRoot, new Vector2(0.012f, 0.04f), new Vector2(0.988f, 0.73f));
        view.ActionsViewport = actionsViewport;
        view.ActionsRoot = actionsRoot;
        view.ActionsScrollRoot.GetComponent<UnityEngine.UI.ScrollRect>().scrollSensitivity = 32f;

        view.MoreActionsButton = CreateFooterButton(
            view.ActionsArea,
            "RuntimeBattlePanelMoreActionsButton",
            "MORE ACTIONS",
            new Vector2(0.012f, 0.74f),
            new Vector2(0.988f, 0.98f),
            Hex("334858"),
            13);

        view.ActionsDrawerRoot = CreateRect("RuntimeBattlePanelActionsDrawer", view.ActionsArea);
        SetAnchors(view.ActionsDrawerRoot, new Vector2(0.012f, 0.03f), new Vector2(0.988f, 0.98f));
        AddPanelBackground(view.ActionsDrawerRoot, Hex("0A1823"), Cyan, 0.99f);
        var drawerBackground = view.ActionsDrawerRoot.GetComponent<UnityEngine.UI.Image>();
        if (drawerBackground != null) drawerBackground.raycastTarget = true;
        RectTransform drawerViewport;
        RectTransform drawerContent;
        view.ActionsDrawerScrollRoot = CreateScrollRoot(
            view.ActionsDrawerRoot,
            "RuntimeBattlePanelActionsDrawerScrollRect",
            out drawerViewport,
            out drawerContent,
            8);
        view.ActionsDrawerViewport = drawerViewport;
        view.ActionsDrawerContent = drawerContent;
        view.ActionsDrawerCloseButton = CreateFooterButton(
            view.ActionsDrawerRoot,
            "RuntimeBattlePanelActionsDrawerCloseButton",
            "CLOSE",
            new Vector2(0.012f, 0.76f),
            new Vector2(0.988f, 0.98f),
            Hex("3B2830"),
            11);
        view.ActionsDrawerRoot.gameObject.SetActive(false);
        view.SetMoreActionsAvailable(false);

        view.EventsArea = CreateRail(view.FooterRoot, "RuntimeBattlePanelEventsArea", "EVENT TIMELINE", EventPanel, new Vector2(0.0f, 0.0f), new Vector2(0.69f, 1.0f));
        RectTransform eventsViewport;
        RectTransform eventsContent;
        view.EventsScrollRoot = CreateScrollRoot(view.EventsArea, "RuntimeBattlePanelEventsScrollRect", out eventsViewport, out eventsContent);
        view.EventsViewport = eventsViewport;
        view.EventsContent = eventsContent;
        view.EventsText = CreateText(view.EventsContent, "RuntimeBattlePanelEvents", 12, new Color(0.78f, 0.86f, 0.90f));
        view.EventsText.alignment = TextAnchor.UpperLeft;
        view.EventsText.verticalOverflow = VerticalWrapMode.Overflow;
        SetAnchors(view.EventsText.rectTransform, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.98f));
        SetLayout(view.EventsText.rectTransform, 68f, 100f, 0f);
    }

    private static void BuildPauseDrawer(RuntimeBattlePanelView view)
    {
        view.PauseDrawerRoot = CreateRect("RuntimeBattlePanelPauseDrawer", view.ContentRoot);
        SetAnchors(view.PauseDrawerRoot, new Vector2(0.28f, 0.18f), new Vector2(0.72f, 0.82f));
        AddPanelBackground(view.PauseDrawerRoot, Hex("0A1720"), Gold, 0.99f);
        var pauseBackground = view.PauseDrawerRoot.GetComponent<UnityEngine.UI.Image>();
        if (pauseBackground != null) pauseBackground.raycastTarget = true;

        var pauseTitle = CreateText(view.PauseDrawerRoot, "PauseTitle", 22, Color.white);
        pauseTitle.text = "PAUSED";
        pauseTitle.alignment = TextAnchor.MiddleCenter;
        pauseTitle.fontStyle = FontStyle.Bold;
        SetAnchors(pauseTitle.rectTransform, new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.94f));

        view.PauseContinueButton = CreateFooterButton(
            view.PauseDrawerRoot,
            "RuntimeBattlePanelPauseContinueButton",
            "CONTINUE",
            new Vector2(0.10f, 0.57f),
            new Vector2(0.90f, 0.72f),
            Hex("27634A"));
        view.PauseSettingsButton = CreateFooterButton(
            view.PauseDrawerRoot,
            "RuntimeBattlePanelPauseSettingsButton",
            "SETTINGS",
            new Vector2(0.10f, 0.39f),
            new Vector2(0.90f, 0.54f),
            Hex("334858"));
        view.PauseMainMenuButton = CreateFooterButton(
            view.PauseDrawerRoot,
            "RuntimeBattlePanelPauseMainMenuButton",
            "MAIN MENU",
            new Vector2(0.10f, 0.21f),
            new Vector2(0.90f, 0.36f),
            Hex("633B43"));

        view.PauseSettingsRoot = CreateRect("RuntimeBattlePanelPauseSettings", view.PauseDrawerRoot);
        SetAnchors(view.PauseSettingsRoot, new Vector2(0.05f, 0.06f), new Vector2(0.95f, 0.94f));
        AddPanelBackground(view.PauseSettingsRoot, Hex("0C1C27"), Cyan, 0.99f);
        var settingsBackground = view.PauseSettingsRoot.GetComponent<UnityEngine.UI.Image>();
        if (settingsBackground != null) settingsBackground.raycastTarget = true;
        var settingsTitle = CreateText(view.PauseSettingsRoot, "SettingsTitle", 18, Color.white);
        settingsTitle.text = "SETTINGS";
        settingsTitle.alignment = TextAnchor.MiddleCenter;
        settingsTitle.fontStyle = FontStyle.Bold;
        SetAnchors(settingsTitle.rectTransform, new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.94f));

        view.LanguageSelectorRoot = CreateRect(
            "RuntimeBattlePanelLanguageSelector",
            view.PauseSettingsRoot);
        SetAnchors(
            view.LanguageSelectorRoot,
            new Vector2(0.08f, 0.52f),
            new Vector2(0.92f, 0.73f));
        view.LanguageSelectorLabel = CreateText(
            view.LanguageSelectorRoot,
            "LanguageSelectorLabel",
            12,
            Muted);
        view.LanguageSelectorLabel.text = "LANGUAGE";
        view.LanguageSelectorLabel.alignment = TextAnchor.MiddleLeft;
        view.LanguageSelectorLabel.fontStyle = FontStyle.Bold;
        SetAnchors(
            view.LanguageSelectorLabel.rectTransform,
            new Vector2(0f, 0f),
            new Vector2(0.24f, 1f));
        view.LanguageEnglishButton = CreateFooterButton(
            view.LanguageSelectorRoot,
            "RuntimeBattlePanelLanguageEnglish",
            "English",
            new Vector2(0.27f, 0.04f),
            new Vector2(0.49f, 0.96f),
            Hex("334858"),
            11);
        view.LanguageChineseButton = CreateFooterButton(
            view.LanguageSelectorRoot,
            "RuntimeBattlePanelLanguageChinese",
            "中文",
            new Vector2(0.51f, 0.04f),
            new Vector2(0.73f, 0.96f),
            Hex("334858"),
            11);
        view.LanguageJapaneseButton = CreateFooterButton(
            view.LanguageSelectorRoot,
            "RuntimeBattlePanelLanguageJapanese",
            "日本語",
            new Vector2(0.75f, 0.04f),
            new Vector2(1f, 0.96f),
            Hex("334858"),
            11);

        view.PauseSettingsBackButton = CreateFooterButton(
            view.PauseSettingsRoot,
            "RuntimeBattlePanelPauseSettingsBackButton",
            "BACK",
            new Vector2(0.10f, 0.08f),
            new Vector2(0.90f, 0.25f),
            Hex("334858"));

        // The existing real toggle is moved into the settings drawer rather
        // than duplicated. It remains the same presentation-only setting and
        // the panel keeps the same SetReducedMotion binding.
        if (view.ReducedMotionToggleRoot != null)
        {
            view.ReducedMotionToggleRoot.SetParent(view.PauseSettingsRoot, false);
            SetAnchors(view.ReducedMotionToggleRoot, new Vector2(0.12f, 0.29f), new Vector2(0.88f, 0.47f));
        }

        view.PauseSettingsRoot.gameObject.SetActive(false);
        view.PauseDrawerRoot.gameObject.SetActive(false);
    }

    private static void BuildCardInspect(RuntimeBattlePanelView view)
    {
        // A temporary reader is essential at 1280x720: cards can remain
        // compact on the table while hover/click exposes their full canonical
        // presentation metadata. Keep the root hit rectangle inside the left
        // rail. The visual surface may extend just up to the hand's left edge,
        // but its background/text are non-raycast presentation only; this
        // preserves the physical card's pointer path for a drag.
        view.CardInspectRoot = CreateRect("RuntimeCardInspect", view.ContentRoot);
        SetAnchors(view.CardInspectRoot, new Vector2(0.012f, 0.16f), new Vector2(0.135f, 0.89f));
        AddPanelBackground(view.CardInspectRoot, Hex("0A171F"), Gold, 0.99f);

        // The reader's old root is intentionally retained as the stable,
        // narrow interaction boundary. At its current width it is only about
        // 148/119 reference pixels at 1280/1024. This child is a bounded
        // presentation expansion: 2.88 times the root width ends just before
        // the MainBattleRoot hand/field cards (about x=.366 of ContentRoot),
        // without moving any tabletop lane or target surface. While the
        // reader is open this presentation surface may temporarily cover
        // the summary/leader/ambush area; closing or beginning a hand drag
        // returns the underlying controls to their normal state.
        var readerSurface = CreateRect("CardInspectSurface", view.CardInspectRoot);
        SetAnchors(readerSurface, Vector2.zero, new Vector2(2.88f, 1f));
        AddPanelBackground(readerSurface, Hex("0A171F"), Gold, 0.99f);

        view.CardInspectTitle = CreateText(readerSurface, "CardInspectTitle", 27, Color.white);
        view.CardInspectTitle.fontStyle = FontStyle.Bold;
        view.CardInspectTitle.alignment = TextAnchor.MiddleLeft;
        view.CardInspectTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
        view.CardInspectTitle.verticalOverflow = VerticalWrapMode.Overflow;
        SetAnchors(view.CardInspectTitle.rectTransform, new Vector2(0.04f, 0.86f), new Vector2(0.96f, 0.985f));

        view.CardInspectSubtitle = CreateText(readerSurface, "CardInspectSubtitle", 14, Cyan);
        view.CardInspectSubtitle.fontStyle = FontStyle.Bold;
        view.CardInspectSubtitle.alignment = TextAnchor.MiddleLeft;
        SetAnchors(view.CardInspectSubtitle.rectTransform, new Vector2(0.04f, 0.795f), new Vector2(0.96f, 0.855f));

        view.CardInspectSummary = CreateText(
            readerSurface,
            "CardInspectSummary",
            16,
            new Color(1f, 0.84f, 0.40f));
        view.CardInspectSummary.fontStyle = FontStyle.Bold;
        view.CardInspectSummary.alignment = TextAnchor.UpperLeft;
        view.CardInspectSummary.horizontalOverflow = HorizontalWrapMode.Wrap;
        view.CardInspectSummary.verticalOverflow = VerticalWrapMode.Overflow;
        SetAnchors(view.CardInspectSummary.rectTransform, new Vector2(0.04f, 0.695f), new Vector2(0.96f, 0.79f));

        var artRoot = CreateRect("CardInspectArt", readerSurface);
        SetAnchors(artRoot, new Vector2(0.04f, 0.46f), new Vector2(0.96f, 0.685f));
        AddPanelBackground(artRoot, Hex("132731"), PanelBorder, 0.98f);
        // A GameObject cannot host both Image and RawImage because both are
        // Graphic components. Keep the bordered panel on the parent and put
        // the replaceable artwork surface on a child.
        var artSurface = CreateRect("CardInspectArtSurface", artRoot);
        SetAnchors(artSurface, new Vector2(0.025f, 0.04f), new Vector2(0.975f, 0.96f));
        view.CardInspectArt = artSurface.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        view.CardInspectArt.color = Color.white;
        view.CardInspectArt.raycastTarget = false;

        RectTransform viewport;
        RectTransform content;
        var scrollRoot = CreateScrollRoot(
            readerSurface,
            "CardInspectScrollRect",
            out viewport,
            out content);
        SetAnchors(scrollRoot, new Vector2(0.03f, 0.035f), new Vector2(0.97f, 0.445f));
        scrollRoot.GetComponent<UnityEngine.UI.ScrollRect>().scrollSensitivity = 28f;

        // The visible detail body owns its full ScrollRect input surface.
        // Do not narrow the hit area back to the old rail: long effects must
        // scroll from any visible point in the expanded reader. The bounded
        // surface ends before the hand/field card start area, and the
        // existing card-drag close path gives those targets priority when a
        // drag begins.
        var viewportImage = viewport.GetComponent<UnityEngine.UI.Image>();
        if (viewportImage != null) viewportImage.raycastTarget = true;

        view.CardInspectDetail = CreateText(content, "CardInspectDetail", 16, new Color(0.88f, 0.92f, 0.94f));
        view.CardInspectDetail.alignment = TextAnchor.UpperLeft;
        view.CardInspectDetail.horizontalOverflow = HorizontalWrapMode.Wrap;
        view.CardInspectDetail.verticalOverflow = VerticalWrapMode.Overflow;
        view.CardInspectDetail.lineSpacing = 1.05f;
        var detailFitter = view.CardInspectDetail.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        detailFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        detailFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        view.CardInspectRoot.gameObject.SetActive(false);
    }

    private static void BuildDebugOverlay(RuntimeBattlePanelView view)
    {
        view.DebugOverlayRoot = CreateRect("RuntimeBattlePanelDebugOverlay", view.ContentRoot);
        SetAnchors(view.DebugOverlayRoot, new Vector2(0.145f, 0.16f), new Vector2(0.855f, 0.89f));
        AddPanelBackground(
            view.DebugOverlayRoot,
            new Color(0.015f, 0.025f, 0.035f, 0.98f),
            new Color(Cyan.r, Cyan.g, Cyan.b, 0.92f),
            0.99f);
        view.DebugOverlayText = CreateText(
            view.DebugOverlayRoot,
            "RuntimeBattlePanelDebugOverlayText",
            11,
            new Color(0.82f, 0.92f, 0.96f));
        SetAnchors(view.DebugOverlayText.rectTransform, new Vector2(0.018f, 0.018f), new Vector2(0.982f, 0.982f));
        view.DebugOverlayText.alignment = TextAnchor.UpperLeft;
        view.DebugOverlayText.horizontalOverflow = HorizontalWrapMode.Wrap;
        view.DebugOverlayText.verticalOverflow = VerticalWrapMode.Overflow;
        view.DebugOverlayRoot.gameObject.SetActive(false);
    }

    public static UnityEngine.UI.Text CreateText(RectTransform parent, string name, int size, Color color)
    {
        var objectText = new GameObject(name, typeof(RectTransform));
        objectText.transform.SetParent(parent, false);
        var text = objectText.AddComponent<UnityEngine.UI.Text>();
        text.font = Resources.GetBuiltinResource<Font>(RuntimeBattlePanelDefaults.LegacyBuiltinFontResource);
        text.fontSize = size;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        text.supportRichText = false;
        text.alignment = TextAnchor.UpperLeft;
        var rect = objectText.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return text;
    }

    public static RectTransform CreateRect(string name, Transform parent)
    {
        var objectRect = new GameObject(name, typeof(RectTransform));
        objectRect.transform.SetParent(parent, false);
        return objectRect.GetComponent<RectTransform>();
    }

    public static RectTransform CreateCardBack(RectTransform parent, string name, bool highlighted)
    {
        var card = CreateCardFrame(parent, name, Hex("152934"), highlighted ? Gold : new Color(0.28f, 0.55f, 0.66f), 52f, 88f);
        var inset = CreateRect("BackInset", card);
        SetAnchors(inset, new Vector2(0.14f, 0.10f), new Vector2(0.86f, 0.90f));
        AddPanelBackground(inset, Hex("1D3A47"), new Color(0.28f, 0.64f, 0.72f, 0.75f), 0.94f);
        var mark = CreateText(inset, "BackMark", 22, new Color(0.66f, 0.89f, 0.91f));
        mark.text = "*";
        mark.alignment = TextAnchor.MiddleCenter;
        SetAnchors(mark.rectTransform, Vector2.zero, Vector2.one);
        return card;
    }

    /// <summary>
    /// Adds or updates the optional presentation-only card illustration. The
    /// card frame and its text remain the same, so art replacement cannot
    /// change gameplay hit targets or the existing drag layout.
    /// </summary>
    public static void ApplyCardArt(RectTransform card, Texture2D texture)
    {
        if (card == null) return;
        var artTransform = card.Find("CardArt") as RectTransform;
        if (artTransform == null)
        {
            artTransform = CreateRect("CardArt", card);
            SetAnchors(artTransform, new Vector2(0.09f, 0.20f), new Vector2(0.91f, 0.49f));
            artTransform.SetAsFirstSibling();
        }

        var art = artTransform.GetComponent<UnityEngine.UI.RawImage>();
        if (art == null) art = artTransform.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        art.texture = texture;
        art.color = Color.white;
        art.raycastTarget = false;
        art.enabled = texture != null;
    }

    public static RectTransform CreateCardFrame(RectTransform parent, string name, Color fill, Color border, float width, float height)
    {
        var card = CreateRect(name, parent);
        var image = card.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = fill;
        image.raycastTarget = false;
        var outline = card.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = border;
        outline.effectDistance = new Vector2(1f, 1f);
        outline.useGraphicAlpha = false;
        var layout = card.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        layout.minWidth = Mathf.Min(40f, width);
        layout.preferredWidth = width;
        layout.flexibleWidth = 0f;
        layout.minHeight = Mathf.Min(44f, height);
        layout.preferredHeight = height;
        layout.flexibleHeight = 0f;
        return card;
    }

    public static void SetPreferredHeight(RectTransform rect, float height) { SetLayout(rect, height, height, 0f); }

    public static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    public static void SetLayout(RectTransform rect, float minHeight, float preferredHeight, float flexibleHeight, float minWidth = 0f, float preferredWidth = 0f, float flexibleWidth = 0f)
    {
        var element = rect.GetComponent<UnityEngine.UI.LayoutElement>();
        if (element == null) element = rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        element.minHeight = minHeight;
        element.preferredHeight = preferredHeight;
        element.flexibleHeight = flexibleHeight;
        if (minWidth > 0f) element.minWidth = minWidth;
        if (preferredWidth > 0f) element.preferredWidth = preferredWidth;
        element.flexibleWidth = flexibleWidth;
    }

    private static RectTransform CreateHorizontalCardScrollRoot(
        RectTransform parent,
        string name,
        Vector2 min,
        Vector2 max,
        out RectTransform viewport,
        out RectTransform content,
        out UnityEngine.UI.ScrollRect scroll,
        out UnityEngine.UI.Scrollbar scrollbar,
        float width,
        float height,
        float spacing,
        float minimumCardWidth)
    {
        var root = CreateRect(name, parent);
        SetAnchors(root, min, max);
        SetLayout(root, height, height, 0f);

        scroll = root.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.scrollSensitivity = 32f;
        scroll.horizontalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.horizontalScrollbarSpacing = 0f;

        viewport = CreateRect("Viewport", root);
        SetAnchors(viewport, Vector2.zero, Vector2.one);
        var viewportImage = viewport.gameObject.AddComponent<UnityEngine.UI.Image>();
        viewportImage.color = new Color(0.02f, 0.12f, 0.15f, 0.08f);
        // The hand and own-field lanes intentionally overlap. The viewport
        // clips the hand but must not become a transparent full-rectangle
        // pointer shield over a field card or its semantic drop surface.
        // Card graphics still receive pointer/drag events, which bubble to
        // this ScrollRect for wheel input; the visible scrollbar remains the
        // explicit blank-space browsing affordance.
        viewportImage.raycastTarget = false;
        var viewportMask = viewport.gameObject.AddComponent<UnityEngine.UI.Mask>();
        viewportMask.showMaskGraphic = false;

        content = CreateCardStrip(
            viewport,
            "OwnHandFaceUp",
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            width,
            height,
            spacing,
            minimumCardWidth,
            false);
        content.pivot = new Vector2(0f, 0.5f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(viewport.rect.width, 0f);
        var contentFitter = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        contentFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        contentFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;

        var scrollbarRoot = CreateRect("Scrollbar", root);
        SetAnchors(scrollbarRoot, new Vector2(0.02f, 0.01f), new Vector2(0.98f, 0.085f));
        var scrollbarBackground = scrollbarRoot.gameObject.AddComponent<UnityEngine.UI.Image>();
        scrollbarBackground.color = new Color(0.20f, 0.43f, 0.49f, 0.46f);
        scrollbarBackground.raycastTarget = true;
        scrollbar = scrollbarRoot.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
        scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.LeftToRight;
        scrollbar.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };

        var slidingArea = CreateRect("SlidingArea", scrollbarRoot);
        SetAnchors(slidingArea, new Vector2(0.02f, 0.18f), new Vector2(0.98f, 0.82f));
        var handle = CreateRect("Handle", slidingArea);
        SetAnchors(handle, Vector2.zero, Vector2.one);
        var handleImage = handle.gameObject.AddComponent<UnityEngine.UI.Image>();
        handleImage.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.94f);
        handleImage.raycastTarget = true;
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scrollbarRoot.SetAsLastSibling();

        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontalScrollbar = scrollbar;
        return root;
    }

    private static RectTransform CreateCardStrip(
        RectTransform parent,
        string name,
        Vector2 min,
        Vector2 max,
        float width,
        float height,
        float spacing,
        float minimumCardWidth = 40f,
        bool addMask = true)
    {
        var strip = CreateRect(name, parent);
        SetAnchors(strip, min, max);
        var layout = strip.gameObject.AddComponent<RuntimeResponsiveCardLayout>();
        layout.spacing = spacing;
        layout.padding = new RectOffset(4, 4, 2, 2);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.PreferredCardWidth = width;
        layout.PreferredCardHeight = height;
        layout.BaseSpacing = spacing;
        layout.MinimumCardWidth = minimumCardWidth;
        if (addMask) strip.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        SetLayout(strip, height, height, 0f);
        return strip;
    }

    private static RectTransform CreateAmbushZone(
        RectTransform parent,
        string name,
        string label,
        Color accent,
        out RectTransform cards)
    {
        var root = CreateRect(name, parent);
        SetAnchors(root, new Vector2(0.012f, 0.04f), new Vector2(0.155f, 0.44f));
        AddPanelBackground(root, Hex("101C23"), accent, 0.96f);
        var title = CreateText(root, name + "Label", 11, accent);
        title.text = label;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleCenter;
        SetAnchors(title.rectTransform, new Vector2(0.02f, 0.72f), new Vector2(0.98f, 0.98f));
        cards = CreateCardStrip(root, name + "Cards", new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.72f), 54f, 68f, -32f);
        return root;
    }

    public static void FitCardStrip(RectTransform strip)
    {
        if (strip == null) return;
        var layout = strip.GetComponent<RuntimeResponsiveCardLayout>();
        if (layout == null) return;

        // Child creation can happen before Canvas has resolved this strip's
        // anchored size. Never turn that transient zero rect into persistent
        // 1x1 LayoutElement values; the normal uGUI layout pass will call the
        // responsive layout again once its rect is valid.
        layout.RequestRefresh();
        if (strip.rect.width > 1f && strip.rect.height > 1f)
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(strip);
    }

    public static void FitScrollableCardStrip(
        RectTransform scrollRoot,
        RectTransform viewport,
        RectTransform content)
    {
        if (scrollRoot == null || viewport == null || content == null) return;
        var layout = content.GetComponent<RuntimeResponsiveCardLayout>();
        var scroll = scrollRoot.GetComponent<UnityEngine.UI.ScrollRect>();
        if (layout == null || scroll == null || viewport.rect.width <= 1f) return;

        var normalizedPosition = layout.BeginScrollRefit(scroll, viewport, content);
        var activeCount = 0;
        for (var index = 0; index < content.childCount; index++)
        {
            var child = content.GetChild(index).gameObject;
            if (!child.activeSelf) continue;
            var element = child.GetComponent<UnityEngine.UI.LayoutElement>();
            if (element != null && !element.ignoreLayout) activeCount++;
        }

        var availableWidth = viewport.rect.width - layout.padding.horizontal;
        var cardWidth = activeCount == 0
            ? 0f
            : Mathf.Min(
                layout.PreferredCardWidth,
                (availableWidth - layout.BaseSpacing * Mathf.Max(0, activeCount - 1)) /
                    activeCount);
        var minimumWidth = Mathf.Min(layout.MinimumCardWidth, layout.PreferredCardWidth);
        if (activeCount > 0 && cardWidth < minimumWidth)
            cardWidth = minimumWidth;

        var desiredWidth = activeCount == 0
            ? viewport.rect.width
            : layout.padding.horizontal +
              cardWidth * activeCount +
              layout.BaseSpacing * Mathf.Max(0, activeCount - 1);
        desiredWidth = Mathf.Max(viewport.rect.width, desiredWidth);

        var contentLayout = content.GetComponent<UnityEngine.UI.LayoutElement>();
        if (contentLayout == null) contentLayout = content.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        contentLayout.minWidth = desiredWidth;
        contentLayout.preferredWidth = desiredWidth;
        contentLayout.flexibleWidth = 0f;
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, desiredWidth);

        layout.RequestRefresh();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        if (activeCount == 0)
            scroll.horizontalNormalizedPosition = 0f;
        else
            scroll.horizontalNormalizedPosition = Mathf.Clamp01(normalizedPosition);
        layout.CompleteScrollRefit(
            scroll,
            viewport,
            content,
            activeCount == 0 ? 0f : normalizedPosition);
    }

    private static void ConfigureVerticalRail(RectTransform rail, int horizontalPadding = 4, int verticalPadding = 4, float spacing = 4f)
    {
        var layout = rail.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.padding = new RectOffset(horizontalPadding, horizontalPadding, verticalPadding, verticalPadding);
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private static void CreateLayoutCaption(RectTransform parent, string caption, Color color)
    {
        var title = CreateText(parent, parent.name + "Caption", 14, color);
        // Rail captions describe implementation geometry rather than game
        // state. Keep their layout element for the established rail size, but
        // do not expose the internal layout vocabulary to players.
        title.text = string.Empty;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleCenter;
        SetLayout(title.rectTransform, 22f, 22f, 0f, 48f, 80f, 1f);
    }

    private static void CreateFlexibleSpacer(RectTransform parent, string name)
    {
        var spacer = CreateRect(name, parent);
        SetLayout(spacer, 2f, 4f, 1f, 1f, 1f, 1f);
    }

    private static RectTransform CreatePile(RectTransform parent, string name, string label, Vector2 min, Vector2 max, Color accent)
    {
        var pile = CreateRect(name, parent);
        SetAnchors(pile, min, max);
        if (max == Vector2.zero)
        {
            var pileLayout = pile.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            pileLayout.minHeight = 48f;
            pileLayout.preferredHeight = 48f;
            pileLayout.minWidth = 56f;
            pileLayout.preferredWidth = 56f;
        }
        AddPanelBackground(pile, Hex("172630"), accent, 0.96f);
        var title = CreateText(pile, "PileLabel", 14, accent);
        title.text = label;
        title.alignment = TextAnchor.MiddleCenter;
        title.fontStyle = FontStyle.Bold;
        SetAnchors(title.rectTransform, new Vector2(0.03f, 0.66f), new Vector2(0.97f, 0.96f));
        var count = CreateText(pile, "PileCount", 18, Color.white);
        count.text = "—";
        count.alignment = TextAnchor.MiddleCenter;
        count.fontStyle = FontStyle.Bold;
        SetAnchors(count.rectTransform, new Vector2(0.03f, 0.20f), new Vector2(0.97f, 0.63f));
        var summary = CreateText(pile, "PileSummary", 10, Muted);
        summary.text = "—";
        summary.alignment = TextAnchor.MiddleCenter;
        summary.horizontalOverflow = HorizontalWrapMode.Wrap;
        summary.verticalOverflow = VerticalWrapMode.Truncate;
        SetAnchors(summary.rectTransform, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.18f));
        return pile;
    }

    private static RectTransform CreateLeaderSlot(RectTransform parent, string name, Color accent)
    {
        var slot = CreateRect(name, parent);
        AddPanelBackground(slot, Hex("17252A"), accent, 0.94f);
        var title = CreateText(slot, "LeaderTitle", 14, accent);
        title.text = "LEADER";
        title.alignment = TextAnchor.MiddleCenter;
        title.fontStyle = FontStyle.Bold;
        SetAnchors(title.rectTransform, new Vector2(0.04f, 0.77f), new Vector2(0.96f, 0.96f));
        var value = CreateText(slot, "LeaderValue", 22, Color.white);
        value.text = string.Empty;
        value.fontSize = 14;
        value.alignment = TextAnchor.MiddleCenter;
        // The public leader name is a single scan line. Best-fit keeps long
        // catalog names inside the narrow slot instead of wrapping over the
        // adjacent field; the inspect surface remains the full-detail path.
        value.horizontalOverflow = HorizontalWrapMode.Wrap;
        value.verticalOverflow = VerticalWrapMode.Truncate;
        value.resizeTextForBestFit = true;
        value.resizeTextMinSize = 10;
        value.resizeTextMaxSize = 14;
        // Keep enough vertical room for a public leader name plus its goal;
        // the snapshot may omit LeaderZone and the battle panel can fill this
        // from the selected deck's public metadata.
        value.fontSize = 14;
        SetAnchors(value.rectTransform, new Vector2(0.04f, 0.47f), new Vector2(0.96f, 0.74f));
        value.gameObject.SetActive(false);
        var status = CreateText(slot, "LeaderStatus", 11, Muted);
        status.text = string.Empty;
        status.alignment = TextAnchor.MiddleCenter;
        status.horizontalOverflow = HorizontalWrapMode.Wrap;
        status.verticalOverflow = VerticalWrapMode.Truncate;
        status.resizeTextForBestFit = true;
        status.resizeTextMinSize = 8;
        status.resizeTextMaxSize = 10;
        SetAnchors(status.rectTransform, new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.43f));
        status.gameObject.SetActive(false);
        return slot;
    }

    private static RectTransform CreateQueueSlot(
        RectTransform parent,
        string name,
        string label,
        string value,
        Color accent,
        Vector2 min,
        Vector2 max,
        bool statusOnly = false)
    {
        var slot = CreateRect(name, parent);
        SetAnchors(slot, min, max);
        if (max == Vector2.zero)
        {
            var slotLayout = slot.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            slotLayout.minHeight = 48f;
            slotLayout.preferredHeight = 48f;
            slotLayout.minWidth = 56f;
            slotLayout.preferredWidth = 56f;
        }
        // The two side phase slots are status mirrors, not controls. Keep
        // their count readable, but remove the framed button-like treatment
        // so the adjacent PhaseActions surface is the obvious action target.
        if (!statusOnly)
            AddPanelBackground(slot, Hex("101C23"), accent, 0.96f);

        var title = CreateText(slot, "QueueLabel", statusOnly ? 11 : 14, statusOnly ? Muted : accent);
        title.text = label;
        title.alignment = statusOnly ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
        title.fontStyle = FontStyle.Bold;
        SetAnchors(
            title.rectTransform,
            new Vector2(0.04f, statusOnly ? 0.69f : 0.48f),
            new Vector2(0.96f, statusOnly ? 0.96f : 0.92f));

        if (statusOnly)
        {
            var state = CreateText(slot, "QueueState", 10, Muted);
            state.text = "—";
            state.alignment = TextAnchor.MiddleLeft;
            state.verticalOverflow = VerticalWrapMode.Truncate;
            SetAnchors(state.rectTransform, new Vector2(0.04f, 0.42f), new Vector2(0.96f, 0.67f));
        }

        var count = CreateText(slot, "QueueCount", statusOnly ? 17 : 18, Color.white);
        count.text = value;
        count.alignment = statusOnly ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
        count.fontStyle = FontStyle.Bold;
        SetAnchors(
            count.rectTransform,
            new Vector2(0.04f, statusOnly ? 0.06f : 0.05f),
            new Vector2(0.96f, statusOnly ? 0.40f : 0.45f));
        return slot;
    }

    private static RectTransform CreatePhaseActionsSurface(
        RectTransform parent,
        string name,
        string titleText,
        Color accent,
        out RectTransform content)
    {
        var root = CreateRect(name, parent);
        AddPanelBackground(root, Hex("102A2A"), accent, 0.98f);
        var rootLayout = root.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        rootLayout.padding = new RectOffset(4, 4, 3, 3);
        rootLayout.spacing = 2f;
        rootLayout.childControlWidth = true;
        rootLayout.childControlHeight = true;
        rootLayout.childForceExpandWidth = true;
        rootLayout.childForceExpandHeight = false;
        SetLayout(root, 70f, 70f, 0f, 56f, 80f, 1f);

        var title = CreateText(root, "Title", 10, accent);
        title.text = titleText;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleCenter;
        SetLayout(title.rectTransform, 16f, 16f, 0f);

        content = CreateRect("Content", root);
        var contentLayout = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        contentLayout.spacing = 2f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        SetLayout(content, 44f, 44f, 1f);
        return root;
    }

    private static RectTransform CreateDiscardActionsSurface(
        RectTransform parent,
        string name,
        out RectTransform content)
    {
        // The panel sits immediately above the hand strip. It is inactive in
        // every other phase, so the normal field/hand composition is unchanged
        // and the button never occupies the bottom-right action rail.
        var root = CreateRect(name, parent);
        SetAnchors(root, new Vector2(0.31f, 0.77f), new Vector2(0.99f, 0.995f));
        AddPanelBackground(root, Hex("352A1B"), Gold, 0.98f);
        var rootImage = root.GetComponent<UnityEngine.UI.Image>();
        if (rootImage != null) rootImage.raycastTarget = true;

        RectTransform viewport;
        var scrollRoot = CreateScrollRoot(
            root,
            name + "ScrollRect",
            out viewport,
            out content,
            2);
        SetAnchors(scrollRoot, new Vector2(0.015f, 0.02f), new Vector2(0.985f, 0.98f));
        scrollRoot.GetComponent<UnityEngine.UI.ScrollRect>().scrollSensitivity = 32f;
        _ = viewport;
        return root;
    }

    private static RectTransform CreateRail(RectTransform parent, string name, string heading, Color fill, Vector2 min, Vector2 max)
    {
        var rail = CreateRect(name, parent);
        SetAnchors(rail, min, max);
        AddPanelBackground(rail, fill, PanelBorder, 0.98f);
        var title = CreateText(rail, name + "Title", 16, new Color(0.68f, 0.89f, 0.94f));
        title.text = heading;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleLeft;
        SetAnchors(title.rectTransform, new Vector2(0.018f, 0.72f), new Vector2(0.98f, 0.98f));
        return rail;
    }

    private static UnityEngine.UI.Button CreateFooterButton(
        RectTransform parent,
        string name,
        string labelText,
        Vector2 min,
        Vector2 max,
        Color fill,
        int fontSize = 14)
    {
        var buttonObject = CreateRect(name, parent);
        SetAnchors(buttonObject, min, max);
        var image = buttonObject.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = fill;
        image.raycastTarget = true;
        var outline = buttonObject.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.72f);
        outline.effectDistance = new Vector2(1f, 1f);
        var button = buttonObject.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        var label = CreateText(buttonObject, "Label", fontSize, Color.white);
        label.text = labelText;
        label.alignment = TextAnchor.MiddleCenter;
        label.fontStyle = FontStyle.Bold;
        label.raycastTarget = false;
        return button;
    }

    private static RectTransform CreateScrollRoot(
        RectTransform parent,
        string name,
        out RectTransform viewport,
        out RectTransform content,
        int contentBottomPadding = 2)
    {
        var root = CreateRect(name, parent);
        SetAnchors(root, new Vector2(0.012f, 0.04f), new Vector2(0.988f, 0.73f));
        SetLayout(root, 60f, 88f, 1f);
        var scroll = root.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        viewport = CreateRect("Viewport", root);
        SetAnchors(viewport, Vector2.zero, Vector2.one);
        var viewportImage = viewport.gameObject.AddComponent<UnityEngine.UI.Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.10f);
        viewportImage.raycastTarget = true;
        var mask = viewport.gameObject.AddComponent<UnityEngine.UI.Mask>();
        mask.showMaskGraphic = false;
        content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.padding = new RectOffset(4, 4, 2, contentBottomPadding);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = content;
        return root;
    }

    private static void CreateZoneCaption(RectTransform parent, string caption, Color color)
    {
        var title = CreateText(parent, parent.name + "Title", 18, color);
        title.text = caption;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleLeft;
        SetAnchors(title.rectTransform, new Vector2(0.012f, 0.89f), new Vector2(0.29f, 0.995f));
    }

    private static void AddTableDivider(RectTransform parent, float y, Color color)
    {
        var divider = CreateRect("TableDivider", parent);
        SetAnchors(divider, new Vector2(0.02f, y), new Vector2(0.98f, y + 0.008f));
        AddPanelBackground(divider, color, Color.clear, 1f);
    }

    private static void AddPanelBackground(RectTransform rect, Color color, Color border, float alpha)
    {
        var image = rect.gameObject.GetComponent<UnityEngine.UI.Image>();
        if (image == null) image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color(color.r, color.g, color.b, color.a * alpha);
        image.raycastTarget = false;
        if (border.a > 0f)
        {
            var outline = rect.gameObject.GetComponent<UnityEngine.UI.Outline>();
            if (outline == null) outline = rect.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = border;
            outline.effectDistance = new Vector2(1f, 1f);
            outline.useGraphicAlpha = false;
        }
    }

    /// <summary>
    /// Creates a semantic null-target surface in a zone's existing empty
    /// presentation layer. It starts disabled and non-raycastable; the panel
    /// enables it only when the snapshot advertises the corresponding action.
    /// Card strips are created as later siblings, so their card graphics stay
    /// above the surface anywhere the two rectangles overlap.
    /// </summary>
    public static RectTransform CreateSemanticDropSurface(
        RectTransform parent,
        string name,
        string label,
        Vector2 min,
        Vector2 max)
    {
        var surface = CreateRect(name, parent);
        SetAnchors(surface, min, max);
        AddPanelBackground(
            surface,
            new Color(0.10f, 0.22f, 0.28f, 0.22f),
            new Color(Cyan.r, Cyan.g, Cyan.b, 0.78f),
            0.96f);
        var image = surface.GetComponent<UnityEngine.UI.Image>();
        if (image != null) image.raycastTarget = false;

        if (!string.IsNullOrWhiteSpace(label))
        {
            var text = CreateText(surface, "TargetLabel", 11, Color.white);
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            SetAnchors(text.rectTransform, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f));
            text.gameObject.SetActive(false);
        }

        surface.gameObject.SetActive(false);
        return surface;
    }

    /// <summary>
    /// Creates a semantic drop surface for a wire target that is not a card,
    /// leader slot, player root, or shared castle. The caller owns the target
    /// id on RuntimeBattleDropZone; this helper only provides a visible,
    /// non-layout surface with a deterministic anchor.
    /// </summary>
    public static RectTransform CreateLegalTargetSurface(
        RectTransform parent,
        string name,
        string label,
        Vector2 min,
        Vector2 max)
    {
        var surface = CreateRect(name, parent);
        SetAnchors(surface, min, max);
        AddPanelBackground(
            surface,
            new Color(0.10f, 0.22f, 0.28f, 0.86f),
            new Color(Cyan.r, Cyan.g, Cyan.b, 0.78f),
            0.96f);
        var text = CreateText(surface, "TargetLabel", 11, Color.white);
        text.text = string.IsNullOrWhiteSpace(label) ? "TARGET" : label;
        text.alignment = TextAnchor.MiddleCenter;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        SetAnchors(text.rectTransform, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f));
        return surface;
    }

    /// <summary>
    /// Binds the canonical LeaderZone projection to an individual leader card
    /// slot. The panel owns the slot geometry; the snapshot presentation model
    /// owns the values. Missing values clear and hide their rows instead of
    /// becoming player-health guesses.
    /// </summary>
    public static void SetLeaderSlot(
        RectTransform slot,
        string name,
        string life,
        string status,
        string winText = null)
    {
        if (slot == null) return;

        var value = FindChildText(slot, "LeaderValue");
        if (value != null)
        {
            var valueText = string.Empty;
            if (IsAvailable(name)) valueText = name;
            // Leader life is not currently projected by the canonical battle
            // snapshot. Keep the optional legacy value readable when a test
            // or future adapter supplies it, but never add a placeholder.
            if (IsAvailable(life) && !IsAvailable(winText))
                valueText += (valueText.Length == 0 ? string.Empty : "\n") + "LIFE  " + life;
            // The compact slot is only about one hundred reference pixels
            // wide. Keep long public catalog names on one visible line while
            // preserving the high-contrast size for normal names.
            value.fontSize = IsAvailable(name) && name.Length > 7 ? 11 : 14;
            value.text = valueText;
            value.gameObject.SetActive(valueText.Length > 0);
        }

        var statusText = FindChildText(slot, "LeaderStatus");
        if (statusText != null)
        {
            if (IsAvailable(winText))
            {
                // Battle lanes need a quick public goal summary, not a
                // multi-line copy block. Parenthetical/detail clauses remain
                // available through the existing inspect surface. Public
                // landmark/chant progress is state, not goal prose, so keep
                // that short live line visible even when a goal is present.
                statusText.text = "目标  " + ShortenLeaderGoal(winText);
                var progress = ExtractLeaderProgress(status);
                if (progress.Length > 0)
                    statusText.text += "\n" + progress;
                statusText.color = PublicGoalText;
            }
            else
            {
                statusText.text = IsAvailable(status) ? "STATUS  " + status : string.Empty;
                statusText.color = Muted;
            }
            statusText.gameObject.SetActive(statusText.text.Length > 0);
        }
    }

    public static void SetQueueState(RectTransform root, string value)
    {
        if (root == null) return;
        var text = FindChildText(root, "QueueState");
        if (text != null)
            text.text = string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private static string ShortenLeaderGoal(string winText)
    {
        var summary = winText.Replace("\r", " ").Replace("\n", " ").Trim();
        var detailStart = summary.IndexOf('（');
        if (detailStart < 0) detailStart = summary.IndexOf('(');
        if (detailStart > 0) summary = summary.Substring(0, detailStart).Trim();

        // The slot is intentionally two lines at most at the 1280 reference
        // frame. This is presentation truncation only; inspect keeps the
        // authoritative full win text.
        const int maxSummaryCharacters = 20;
        if (summary.Length <= maxSummaryCharacters) return summary;
        return summary.Substring(0, maxSummaryCharacters - 1) + "…";
    }

    private static string ExtractLeaderProgress(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return string.Empty;
        var separator = status.IndexOf(" | ", StringComparison.Ordinal);
        if (separator < 0 || separator + 3 >= status.Length) return string.Empty;
        var progress = status.Substring(separator + 3).Trim();
        return progress.IndexOf("地标层数", StringComparison.Ordinal) >= 0 ||
            progress.IndexOf("吟唱剩余", StringComparison.Ordinal) >= 0
            ? progress
            : string.Empty;
    }

    private static void ConfigureCanvas(UnityEngine.Canvas canvas)
    {
        if (canvas == null) return;
        var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        if (scaler == null) scaler = canvas.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        if (canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
    }

    private static void AddLayout(RectTransform rect, float minHeight, float preferredHeight, float flexibleHeight, float minWidth = 0f, float preferredWidth = 0f, float flexibleWidth = 0f)
    {
        SetLayout(rect, minHeight, preferredHeight, flexibleHeight, minWidth, preferredWidth, flexibleWidth);
    }

    private static bool IsAvailable(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(
                value,
                RuntimeBattlePanelPresentationModel.Unavailable,
                StringComparison.Ordinal);
    }

    private static UnityEngine.UI.Text FindChildText(RectTransform root, string name)
    {
        if (root == null) return null;
        var texts = root.GetComponentsInChildren<UnityEngine.UI.Text>(true);
        for (var index = 0; index < texts.Length; index++)
        {
            if (texts[index].gameObject.name == name) return texts[index];
        }
        return null;
    }

    private static Color Hex(string value)
    {
        if (ColorUtility.TryParseHtmlString("#" + value, out var color)) return color;
        return Color.magenta;
    }

}

internal sealed class RuntimeResponsiveCardLayout : UnityEngine.UI.HorizontalLayoutGroup
{
    private UnityEngine.UI.ScrollRect _trackedScroll;
    private UnityEngine.Events.UnityAction<Vector2> _scrollValueChangedListener;
    private float _lastNormalizedPosition;
    private float _lastViewportWidth;
    private float _lastContentWidth;
    private bool _hasScrollPosition;
    private bool _isRefittingScroll;

    public float PreferredCardWidth;
    public float PreferredCardHeight;
    public float BaseSpacing;
    public float MinimumCardWidth = 40f;

    public float BeginScrollRefit(
        UnityEngine.UI.ScrollRect scroll,
        RectTransform viewport,
        RectTransform content)
    {
        if (_trackedScroll != scroll)
        {
            if (_trackedScroll != null && _scrollValueChangedListener != null)
                _trackedScroll.onValueChanged.RemoveListener(_scrollValueChangedListener);

            _trackedScroll = scroll;
            _scrollValueChangedListener = CaptureScrollPosition;
            _trackedScroll.onValueChanged.AddListener(_scrollValueChangedListener);
            _hasScrollPosition = false;
        }

        if (!_hasScrollPosition)
        {
            CaptureScrollPosition(viewport, content);
        }
        else if (HasSameScrollGeometry(viewport, content))
        {
            _lastNormalizedPosition = scroll.horizontalNormalizedPosition;
        }

        _isRefittingScroll = true;
        return Mathf.Clamp01(_lastNormalizedPosition);
    }

    public void CompleteScrollRefit(
        UnityEngine.UI.ScrollRect scroll,
        RectTransform viewport,
        RectTransform content,
        float normalizedPosition)
    {
        _trackedScroll = scroll;
        _lastNormalizedPosition = Mathf.Clamp01(normalizedPosition);
        _lastViewportWidth = viewport.rect.width;
        _lastContentWidth = content.rect.width;
        _hasScrollPosition = true;
        _isRefittingScroll = false;
    }

    private void CaptureScrollPosition(Vector2 _)
    {
        if (_isRefittingScroll || !_hasScrollPosition || _trackedScroll == null ||
            _trackedScroll.viewport == null || _trackedScroll.content == null)
            return;

        var viewport = _trackedScroll.viewport as RectTransform;
        var content = _trackedScroll.content as RectTransform;
        if (viewport == null || content == null || !HasSameScrollGeometry(viewport, content)) return;
        _lastNormalizedPosition = _trackedScroll.horizontalNormalizedPosition;
    }

    private void CaptureScrollPosition(RectTransform viewport, RectTransform content)
    {
        _lastNormalizedPosition = _trackedScroll.horizontalNormalizedPosition;
        _lastViewportWidth = viewport.rect.width;
        _lastContentWidth = content.rect.width;
        _hasScrollPosition = true;
    }

    private bool HasSameScrollGeometry(RectTransform viewport, RectTransform content)
    {
        return Mathf.Approximately(_lastViewportWidth, viewport.rect.width) &&
            Mathf.Approximately(_lastContentWidth, content.rect.width);
    }

    protected override void OnDestroy()
    {
        if (_trackedScroll != null && _scrollValueChangedListener != null)
            _trackedScroll.onValueChanged.RemoveListener(_scrollValueChangedListener);
        base.OnDestroy();
    }

    public void RequestRefresh()
    {
        SetDirty();
    }

    public override void CalculateLayoutInputHorizontal()
    {
        ApplyResponsiveCardSizes();
        base.CalculateLayoutInputHorizontal();
    }

    public override void CalculateLayoutInputVertical()
    {
        ApplyResponsiveCardSizes();
        base.CalculateLayoutInputVertical();
    }

    public override void SetLayoutHorizontal()
    {
        ApplyResponsiveCardSizes();
        base.SetLayoutHorizontal();
    }

    public override void SetLayoutVertical()
    {
        ApplyResponsiveCardSizes();
        base.SetLayoutVertical();
    }

    private void ApplyResponsiveCardSizes()
    {
        // A zero rect is expected while a newly-built Canvas hierarchy is in
        // its pre-layout state. Keep the card defaults intact until uGUI has
        // resolved real dimensions.
        if (rectTransform.rect.width <= 1f || rectTransform.rect.height <= 1f) return;

        var cards = new System.Collections.Generic.List<UnityEngine.UI.LayoutElement>();
        for (var index = 0; index < rectTransform.childCount; index++)
        {
            var child = rectTransform.GetChild(index).gameObject;
            if (!child.activeSelf) continue;
            var element = child.GetComponent<UnityEngine.UI.LayoutElement>();
            if (element != null && !element.ignoreLayout) cards.Add(element);
        }
        if (cards.Count == 0)
        {
            spacing = BaseSpacing;
            return;
        }

        var availableWidth = rectTransform.rect.width - padding.horizontal;
        var availableHeight = rectTransform.rect.height - padding.vertical;
        if (availableWidth <= 1f || availableHeight <= 1f) return;

        var widthWithBaseSpacing = (availableWidth - BaseSpacing * Mathf.Max(0, cards.Count - 1)) / cards.Count;
        var minimumWidth = Mathf.Min(MinimumCardWidth, PreferredCardWidth);
        var cardWidth = Mathf.Min(PreferredCardWidth, widthWithBaseSpacing);
        if (cardWidth < minimumWidth)
        {
            cardWidth = Mathf.Min(minimumWidth, availableWidth);
            spacing = cards.Count == 1
                ? 0f
                : (availableWidth - cardWidth * cards.Count) / (cards.Count - 1);
        }
        else
        {
            spacing = BaseSpacing;
        }

        var cardHeight = Mathf.Min(PreferredCardHeight, availableHeight);
        for (var index = 0; index < cards.Count; index++)
        {
            cards[index].minWidth = cardWidth;
            cards[index].preferredWidth = cardWidth;
            cards[index].flexibleWidth = 0f;
            cards[index].minHeight = cardHeight;
            cards[index].preferredHeight = cardHeight;
            cards[index].flexibleHeight = 0f;
        }
    }
}
}
