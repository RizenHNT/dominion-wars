#nullable disable

using System;
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
    public RectTransform RightRailRoot { get; private set; }
    public RectTransform OpponentRoot { get; private set; }
    public RectTransform OpponentHandRoot { get; private set; }
    public RectTransform OpponentFieldRoot { get; private set; }
    public RectTransform OpponentDeckRoot { get; private set; }
    public RectTransform OpponentGraveyardRoot { get; private set; }
    public RectTransform OpponentExileRoot { get; private set; }
    public RectTransform OpponentLeaderRoot { get; private set; }
    public RectTransform OpponentPhaseRoot { get; private set; }
    public RectTransform CenterRoot { get; private set; }
    public RectTransform CenterPilesRoot { get; private set; }
    public RectTransform CastleRoot { get; private set; }
    public RectTransform CastleBarrierRoot { get; private set; }
    public RectTransform PhaseRoot { get; private set; }
    public RectTransform MechanicalRoot { get; private set; }
    public RectTransform OwnRoot { get; private set; }
    public RectTransform OwnHandRoot { get; private set; }
    public RectTransform OwnFieldRoot { get; private set; }
    public RectTransform OwnDeckRoot { get; private set; }
    public RectTransform OwnGraveyardRoot { get; private set; }
    public RectTransform OwnExileRoot { get; private set; }
    public RectTransform OwnLeaderRoot { get; private set; }
    public RectTransform OwnPhaseRoot { get; private set; }
    public RectTransform FooterRoot { get; private set; }
    public RectTransform ActionsArea { get; private set; }
    public RectTransform ActionsScrollRoot { get; private set; }
    public RectTransform ActionsViewport { get; private set; }
    public RectTransform EventsArea { get; private set; }
    public RectTransform EventsScrollRoot { get; private set; }
    public RectTransform EventsViewport { get; private set; }
    public RectTransform EventsContent { get; private set; }

    // Stable references retained for binding tests and concise tabletop
    // summaries. Dynamic cards/piles are children of the explicit roots above.
    public UnityEngine.UI.Text StatusText { get; private set; }
    public UnityEngine.UI.Text MatchText { get; private set; }
    public UnityEngine.UI.Text OpponentText { get; private set; }
    public UnityEngine.UI.Text CastleText { get; private set; }
    public UnityEngine.UI.Text OwnText { get; private set; }
    public UnityEngine.UI.Text PhaseText { get; private set; }
    public UnityEngine.UI.Text EventsText { get; private set; }
    public RectTransform ActionsRoot { get; private set; }

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
        SetAnchors(view.StatusText.rectTransform, new Vector2(0.016f, 0.05f), new Vector2(0.28f, 0.95f));
        view.StatusText.alignment = TextAnchor.MiddleLeft;
        view.StatusText.fontStyle = FontStyle.Bold;

        view.MatchText = CreateText(view.HeaderRoot, "RuntimeBattlePanelMatch", 16, new Color(0.82f, 0.90f, 0.94f));
        SetAnchors(view.MatchText.rectTransform, new Vector2(0.73f, 0.05f), new Vector2(0.984f, 0.95f));
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
        BuildFooter(view);

        return view;
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
        view.OpponentPhaseRoot = CreateQueueSlot(view.LeftRailRoot, "OpponentPhaseAmbush", "PHASE", "—", new Color(0.93f, 0.65f, 0.74f), Vector2.zero, Vector2.zero);
        CreateFlexibleSpacer(view.LeftRailRoot, "LeftRailSpacer");

        view.CenterPilesRoot = CreateRect("CenterPiles", view.LeftRailRoot);
        AddPanelBackground(view.CenterPilesRoot, Hex("111F28"), new Color(0.31f, 0.61f, 0.70f, 0.6f), 0.96f);
        AddLayout(view.CenterPilesRoot, 104f, 112f, 0f, 56f, 96f, 1f);
        ConfigureVerticalRail(view.CenterPilesRoot, 2, 2, 3f);
        CreateQueueSlot(view.CenterPilesRoot, "CenterCloudMirror", "CLOUD", "—", new Color(0.40f, 0.72f, 0.82f), Vector2.zero, Vector2.zero);
        CreateQueueSlot(view.CenterPilesRoot, "CenterCommitMirror", "SUBMIT QUEUE", "—", new Color(0.42f, 0.77f, 0.60f), Vector2.zero, Vector2.zero);
        view.OwnPhaseRoot = CreateQueueSlot(view.LeftRailRoot, "OwnPhaseAmbush", "AMBUSH", "—", new Color(0.45f, 0.85f, 0.68f), Vector2.zero, Vector2.zero);

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
        view.OwnGraveyardRoot = CreatePile(view.RightRailRoot, "OwnGraveyard", "GRAVE", Vector2.zero, Vector2.zero, new Color(0.58f, 0.53f, 0.32f));
        view.OwnDeckRoot = CreatePile(view.RightRailRoot, "OwnDeck", "DECK", Vector2.zero, Vector2.zero, new Color(0.27f, 0.70f, 0.57f));
    }

    private static void BuildOpponentLane(RuntimeBattlePanelView view)
    {
        view.OpponentRoot = CreateRect("RuntimeBattlePanelOpponent", view.MainBattleRoot);
        SetAnchors(view.OpponentRoot, new Vector2(0f, 0.68f), new Vector2(1f, 1f));
        AddPanelBackground(view.OpponentRoot, OpponentPanel, new Color(0.55f, 0.33f, 0.52f, 0.85f), 0.98f);
        AddLayout(view.OpponentRoot, 96f, 128f, 0f, 0f, 0f, 1f);
        CreateZoneCaption(view.OpponentRoot, "OPPONENT SIDE  ·  PUBLIC BOARD", new Color(0.95f, 0.71f, 0.84f));
        view.OpponentText = CreateText(view.OpponentRoot, "RuntimeBattlePanelOpponent", 16, Color.white);
        SetAnchors(view.OpponentText.rectTransform, new Vector2(0.012f, 0.12f), new Vector2(0.16f, 0.86f));
        view.OpponentText.alignment = TextAnchor.UpperLeft;

        view.OpponentLeaderRoot = CreateLeaderSlot(view.OpponentRoot, "OpponentLeaderSlot", new Color(0.73f, 0.41f, 0.66f));
        SetAnchors(view.OpponentLeaderRoot, new Vector2(0.17f, 0.16f), new Vector2(0.29f, 0.86f));
        view.OpponentHandRoot = CreateCardStrip(view.OpponentRoot, "OpponentHandBacks", new Vector2(0.31f, 0.55f), new Vector2(0.99f, 0.98f), 52f, 88f, 4f);
        view.OpponentFieldRoot = CreateCardStrip(view.OpponentRoot, "OpponentField", new Vector2(0.31f, 0.05f), new Vector2(0.99f, 0.52f), 78f, 92f, 6f);
    }

    private static void BuildCenterLane(RuntimeBattlePanelView view)
    {
        view.CenterRoot = CreateRect("RuntimeBattlePanelCenter", view.MainBattleRoot);
        SetAnchors(view.CenterRoot, new Vector2(0f, 0.335f), new Vector2(1f, 0.665f));
        AddPanelBackground(view.CenterRoot, NeutralPanel, new Color(0.26f, 0.67f, 0.70f, 0.80f), 0.98f);
        AddLayout(view.CenterRoot, 128f, 192f, 0f, 320f, 560f, 1f);
        AddTableDivider(view.CenterRoot, 0.08f, new Color(Cyan.r, Cyan.g, Cyan.b, 0.32f));

        view.CastleRoot = CreateCardFrame(view.CenterRoot, "RuntimeBattlePanelCastle", Hex("2A261B"), Gold, 176f, 204f);
        SetAnchors(view.CastleRoot, new Vector2(0.27f, 0.06f), new Vector2(0.73f, 0.78f));
        view.CastleBarrierRoot = CreateRect("CastleBarrier", view.CastleRoot);
        SetAnchors(view.CastleBarrierRoot, new Vector2(0.05f, 0.62f), new Vector2(0.95f, 0.86f));
        AddPanelBackground(view.CastleBarrierRoot, Hex("162C38"), new Color(Cyan.r, Cyan.g, Cyan.b, 0.75f), 0.96f);
        var barrierLabel = CreateText(view.CastleBarrierRoot, "BarrierLabel", 16, Cyan);
        barrierLabel.text = "BARRIER  —";
        barrierLabel.alignment = TextAnchor.MiddleCenter;
        SetAnchors(barrierLabel.rectTransform, Vector2.zero, Vector2.one);
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

    }

    private static void BuildOwnLane(RuntimeBattlePanelView view)
    {
        view.OwnRoot = CreateRect("RuntimeBattlePanelOwn", view.MainBattleRoot);
        SetAnchors(view.OwnRoot, new Vector2(0f, 0f), new Vector2(1f, 0.32f));
        AddPanelBackground(view.OwnRoot, OwnPanel, new Color(0.25f, 0.70f, 0.52f, 0.82f), 0.98f);
        AddLayout(view.OwnRoot, 96f, 168f, 0f, 0f, 0f, 1f);
        CreateZoneCaption(view.OwnRoot, "YOUR SIDE  ·  FACE-UP HAND", new Color(0.60f, 0.95f, 0.76f));
        view.OwnText = CreateText(view.OwnRoot, "RuntimeBattlePanelOwn", 16, Color.white);
        SetAnchors(view.OwnText.rectTransform, new Vector2(0.012f, 0.13f), new Vector2(0.16f, 0.86f));
        view.OwnText.alignment = TextAnchor.UpperLeft;

        view.OwnLeaderRoot = CreateLeaderSlot(view.OwnRoot, "OwnLeaderSlot", new Color(0.35f, 0.79f, 0.61f));
        SetAnchors(view.OwnLeaderRoot, new Vector2(0.17f, 0.14f), new Vector2(0.29f, 0.84f));
        view.OwnFieldRoot = CreateCardStrip(view.OwnRoot, "OwnField", new Vector2(0.31f, 0.50f), new Vector2(0.99f, 0.96f), 88f, 112f, 6f);
        view.OwnHandRoot = CreateCardStrip(view.OwnRoot, "OwnHandFaceUp", new Vector2(0.31f, 0.02f), new Vector2(0.99f, 0.47f), 106f, 120f, 6f);
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
        RectTransform actionsViewport;
        RectTransform actionsRoot;
        view.ActionsScrollRoot = CreateScrollRoot(view.ActionsArea, "RuntimeBattlePanelActionsScrollRect", out actionsViewport, out actionsRoot);
        view.ActionsViewport = actionsViewport;
        view.ActionsRoot = actionsRoot;
        view.ActionsScrollRoot.GetComponent<UnityEngine.UI.ScrollRect>().scrollSensitivity = 32f;

        view.EventsArea = CreateRail(view.FooterRoot, "RuntimeBattlePanelEventsArea", "EVENTS  ·  RECENT ADAPTER", EventPanel, new Vector2(0.0f, 0.0f), new Vector2(0.69f, 1.0f));
        RectTransform eventsViewport;
        RectTransform eventsContent;
        view.EventsScrollRoot = CreateScrollRoot(view.EventsArea, "RuntimeBattlePanelEventsScrollRect", out eventsViewport, out eventsContent);
        view.EventsViewport = eventsViewport;
        view.EventsContent = eventsContent;
        view.EventsText = CreateText(view.EventsContent, "RuntimeBattlePanelEvents", 16, new Color(0.78f, 0.86f, 0.90f));
        view.EventsText.alignment = TextAnchor.UpperLeft;
        SetAnchors(view.EventsText.rectTransform, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.98f));
        SetLayout(view.EventsText.rectTransform, 68f, 100f, 0f);
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

    /// <summary>
    /// Builds a face-up card from wire-visible values. The caller supplies
    /// "—" for absent cost/attack/health; this helper never invents stats.
    /// </summary>
    public static RectTransform CreateCardFace(RectTransform parent, string name, string cardName, string entityLabel, string cost, string attack, string health, Color accent, bool sourceHighlighted, bool targetHighlighted, bool interactable)
    {
        var card = CreateCardFrame(parent, name, Hex("182630"), accent, 106f, 120f);
        var border = card.GetComponent<UnityEngine.UI.Outline>();
        border.effectColor = sourceHighlighted ? Gold : targetHighlighted ? Cyan : new Color(accent.r, accent.g, accent.b, 0.8f);
        border.effectDistance = sourceHighlighted || targetHighlighted ? new Vector2(3f, 3f) : new Vector2(1f, 1f);

        var topStripe = CreateRect("CardStripe", card);
        SetAnchors(topStripe, new Vector2(0.02f, 0.82f), new Vector2(0.98f, 0.97f));
        AddPanelBackground(topStripe, accent, accent, 1f);
        var costBadge = CreateRect("CardCost", card);
        SetAnchors(costBadge, new Vector2(0.05f, 0.69f), new Vector2(0.27f, 0.82f));
        AddPanelBackground(costBadge, Hex("0C1820"), accent, 1f);
        var costText = CreateText(costBadge, "Value", 16, Color.white);
        costText.text = DisplayStat(cost);
        costText.alignment = TextAnchor.MiddleCenter;
        SetAnchors(costText.rectTransform, Vector2.zero, Vector2.one);

        var nameText = CreateText(card, "CardName", 16, Color.white);
        nameText.text = string.IsNullOrWhiteSpace(cardName) ? "—" : cardName;
        nameText.alignment = TextAnchor.MiddleCenter;
        nameText.fontStyle = FontStyle.Bold;
        SetAnchors(nameText.rectTransform, new Vector2(0.05f, 0.48f), new Vector2(0.95f, 0.68f));
        var entityText = CreateText(card, "CardEntity", 14, Muted);
        entityText.text = DisplayStat(entityLabel);
        entityText.alignment = TextAnchor.MiddleCenter;
        SetAnchors(entityText.rectTransform, new Vector2(0.05f, 0.34f), new Vector2(0.95f, 0.47f));
        var statsText = CreateText(card, "CardStats", 16, Color.white);
        statsText.text = "ATK " + DisplayStat(attack) + "   HP " + DisplayStat(health);
        statsText.alignment = TextAnchor.MiddleCenter;
        statsText.fontStyle = FontStyle.Bold;
        SetAnchors(statsText.rectTransform, new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.27f));
        if (sourceHighlighted || targetHighlighted)
        {
            var marker = CreateText(card, "CardMarker", 14, sourceHighlighted ? Gold : Cyan);
            marker.text = sourceHighlighted ? "SOURCE" : "TARGET";
            marker.alignment = TextAnchor.MiddleCenter;
            marker.fontStyle = FontStyle.Bold;
            SetAnchors(marker.rectTransform, new Vector2(0.25f, 0.69f), new Vector2(0.96f, 0.82f));
        }
        if (!interactable)
        {
            var disabled = CreateRect("DisabledVeil", card);
            SetAnchors(disabled, Vector2.zero, Vector2.one);
            AddPanelBackground(disabled, new Color(0.02f, 0.04f, 0.05f, 0.42f), Color.clear, 1f);
        }
        return card;
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

    private static RectTransform CreateCardStrip(RectTransform parent, string name, Vector2 min, Vector2 max, float width, float height, float spacing)
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
        strip.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        SetLayout(strip, height, height, 0f);
        return strip;
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
        title.text = caption;
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
        SetAnchors(title.rectTransform, new Vector2(0.04f, 0.63f), new Vector2(0.96f, 0.94f));
        var value = CreateText(slot, "LeaderValue", 22, Color.white);
        value.text = "—";
        value.alignment = TextAnchor.MiddleCenter;
        SetAnchors(value.rectTransform, new Vector2(0.04f, 0.18f), new Vector2(0.96f, 0.61f));
        return slot;
    }

    private static RectTransform CreateQueueSlot(RectTransform parent, string name, string label, string value, Color accent, Vector2 min, Vector2 max)
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
        AddPanelBackground(slot, Hex("101C23"), accent, 0.96f);
        var title = CreateText(slot, "QueueLabel", 14, accent);
        title.text = label;
        title.alignment = TextAnchor.MiddleCenter;
        title.fontStyle = FontStyle.Bold;
        SetAnchors(title.rectTransform, new Vector2(0.04f, 0.48f), new Vector2(0.96f, 0.92f));
        var count = CreateText(slot, "QueueCount", 18, Color.white);
        count.text = value;
        count.alignment = TextAnchor.MiddleCenter;
        count.fontStyle = FontStyle.Bold;
        SetAnchors(count.rectTransform, new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.45f));
        return slot;
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

    private static RectTransform CreateScrollRoot(RectTransform parent, string name, out RectTransform viewport, out RectTransform content)
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
        layout.padding = new RectOffset(4, 4, 2, 2);
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

    private static string DisplayStat(string value) { return string.IsNullOrWhiteSpace(value) ? "—" : value; }

    private static Color Hex(string value)
    {
        if (ColorUtility.TryParseHtmlString("#" + value, out var color)) return color;
        return Color.magenta;
    }
}

internal sealed class RuntimeResponsiveCardLayout : UnityEngine.UI.HorizontalLayoutGroup
{
    public float PreferredCardWidth;
    public float PreferredCardHeight;
    public float BaseSpacing;

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
        var minimumWidth = Mathf.Min(40f, PreferredCardWidth);
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
