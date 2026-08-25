using System;
using System.Collections.Generic;
using System.Text;
using DominionWars.Adapters;
using DominionWars.Unity.Runtime;
using UnityEngine;

namespace DominionWars.Unity.UI
{

/// <summary>
/// Runtime-bound uGUI tabletop. The view is a visual placeholder, but it is
/// intentionally shaped like a card table: face-up viewer cards, redacted
/// opponent backs, leader slots, mirrored piles, a central castle and a
/// secondary action/event rail. Snapshot and LegalActions remain the only
/// source of state; this component never derives game rules.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
[AddComponentMenu("Dominion Wars/Runtime Battle Panel")]
public sealed class RuntimeBattlePanel : MonoBehaviour
{
    private enum BindingMode
    {
        Automatic,
        ExplicitBootstrap,
        ExplicitAdapter,
    }

    [Header("Runtime binding")]
    [SerializeField] private RuntimeBootstrap bootstrap;
    [SerializeField] private bool findBootstrapOnStart = true;
    [SerializeField] private int viewerPlayerIndex;
    [SerializeField] private bool followCurrentPlayer = true;

    [Header("Programmatic canvas")]
    [SerializeField] private bool createCanvasIfMissing = true;
    [SerializeField] private bool createEventSystemIfMissing = true;

    private RuntimeAdapter _adapter;
    private UnityEngine.EventSystems.EventSystem _createdEventSystem;
    private RuntimeBattlePanelView _view;
    private UnityEngine.UI.Text _statusText;
    private bool _visualTreeReady;
    private long _renderedRevision = -1;
    private int _renderedEventCount = -1;
    private int _boundViewerPlayerIndex = -1;
    private string _lastActionStatus = string.Empty;
    private IReadOnlyList<RuntimeBattlePanelActionGroup> _actionGroups =
        Array.Empty<RuntimeBattlePanelActionGroup>();
    private BindingMode _bindingMode;

    public RuntimeAdapter Adapter => _adapter;
    public RuntimeBootstrap Bootstrap => bootstrap;
    public int ViewerPlayerIndex => viewerPlayerIndex;
    public bool FollowCurrentPlayer => followCurrentPlayer;
    public IReadOnlyList<RuntimeBattlePanelActionGroup> ActionGroups => _actionGroups;
    public string LastActionStatus => _lastActionStatus;

    /// <summary>
    /// Creates the neutral MVP panel after a scene loads when no panel was
    /// authored in the scene. This keeps the first slice runnable without a
    /// hand-written scene/prefab and remains removable by deleting this UI
    /// assembly or disabling the component.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeInstance()
    {
        if (UnityEngine.Object.FindFirstObjectByType<RuntimeBattlePanel>() != null) return;
        var panelObject = new GameObject(
            "DominionWarsRuntimeBattlePanel",
            typeof(RectTransform),
            typeof(UnityEngine.Canvas),
            typeof(UnityEngine.UI.CanvasScaler),
            typeof(UnityEngine.UI.GraphicRaycaster));
        panelObject.AddComponent<RuntimeBattlePanel>();
    }

    private void Awake()
    {
        EnsureInitialized();
        TryBindBootstrap();
        Render();
    }

    private void Update()
    {
        if (_adapter is null) TryBindBootstrap();
        var snapshot = _adapter?.Presentation.Snapshot;
        var eventCount = _adapter?.Presentation.Events.Count ?? -1;
        if (!_visualTreeReady || _adapter is null)
        {
            if (_renderedRevision != -1) Render();
            return;
        }
        if (snapshot is null || snapshot.SnapshotRevision != _renderedRevision || eventCount != _renderedEventCount)
        {
            Render();
        }
    }

    public void Bind(RuntimeBootstrap runtimeBootstrap)
    {
        if (runtimeBootstrap == null)
        {
            Unbind();
            return;
        }

        _bindingMode = BindingMode.ExplicitBootstrap;
        bootstrap = runtimeBootstrap;
        _adapter = runtimeBootstrap.Adapter;
        ResetBindingState();
        TryBindBootstrap();
        Render();
    }

    public void Bind(RuntimeAdapter runtimeAdapter)
    {
        if (runtimeAdapter == null)
            throw new ArgumentNullException(nameof(runtimeAdapter));

        _bindingMode = BindingMode.ExplicitAdapter;
        bootstrap = null;
        _adapter = runtimeAdapter;
        ResetBindingState();
        TryRefreshViewerSnapshot();
        Render();
    }

    /// <summary>
    /// Clears an explicit binding and resumes the configured automatic
    /// RuntimeBootstrap discovery on the next bind/render pass.
    /// </summary>
    public void Unbind()
    {
        _bindingMode = BindingMode.Automatic;
        bootstrap = null;
        _adapter = null;
        ResetBindingState();
        TryBindBootstrap();
        Render();
    }

    /// <summary>
    /// Selects which engine-projected viewer snapshot this technical panel
    /// consumes. The engine remains responsible for redaction and legality.
    /// </summary>
    public void SetViewerPlayerIndex(int playerIndex)
    {
        if (playerIndex is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(playerIndex));

        viewerPlayerIndex = playerIndex;
        _boundViewerPlayerIndex = -1;
        TryRefreshViewerSnapshot();
        Render();
    }

    /// <summary>
    /// Enables the local hot-seat handoff. When enabled, the next snapshot is
    /// requested for its current player so each turn exposes only that
    /// player's redacted viewer hand.
    /// </summary>
    public void SetFollowCurrentPlayer(bool enabled)
    {
        followCurrentPlayer = enabled;
        if (enabled)
        {
            _boundViewerPlayerIndex = -1;
            TryRefreshViewerSnapshot();
        }
        Render();
    }

    public void Refresh()
    {
        Render();
    }

    private void TryBindBootstrap()
    {
        if (_bindingMode == BindingMode.ExplicitAdapter)
        {
            TryRefreshViewerSnapshot();
            return;
        }

        if (_bindingMode == BindingMode.Automatic && bootstrap == null && findBootstrapOnStart)
            bootstrap = UnityEngine.Object.FindFirstObjectByType<RuntimeBootstrap>();

        var nextAdapter = bootstrap == null ? null : bootstrap.Adapter;
        if (!ReferenceEquals(_adapter, nextAdapter))
        {
            _adapter = nextAdapter;
            ResetBindingState();
        }
        TryRefreshViewerSnapshot();
    }

    private void ResetBindingState()
    {
        _boundViewerPlayerIndex = -1;
        _renderedRevision = -1;
        _renderedEventCount = -1;
        _lastActionStatus = string.Empty;
    }

    private bool TryRefreshViewerSnapshot()
    {
        if (_adapter is null || _boundViewerPlayerIndex == viewerPlayerIndex)
            return _adapter is not null;

        try
        {
            _adapter.RefreshSnapshot(viewerPlayerIndex);
            _boundViewerPlayerIndex = viewerPlayerIndex;
            return true;
        }
        catch (Exception exception)
        {
            _lastActionStatus = "Viewer snapshot refresh failed: " + exception.Message;
            return false;
        }
    }

    private void EnsureCanvas()
    {
        var canvas = GetComponentInParent<UnityEngine.Canvas>();
        if (canvas == null && createCanvasIfMissing)
        {
            canvas = GetComponent<UnityEngine.Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<UnityEngine.Canvas>();
        }

        // Auto-created panels already have their Canvas before Awake runs.
        // Configure that root as well as a Canvas added by this method.
        if (canvas != null && canvas.gameObject == gameObject)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            if (GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        var root = GetComponent<RectTransform>();
        if (root == null)
            throw new MissingComponentException("RuntimeBattlePanel requires a RectTransform root.");
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
    }

    private void EnsureEventSystem()
    {
        if (!createEventSystemIfMissing ||
            UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null)
            return;

        var eventSystemObject = new GameObject("DominionWarsEventSystem");
        _createdEventSystem = eventSystemObject.AddComponent<UnityEngine.EventSystems.EventSystem>();
        eventSystemObject.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }

    private void BuildVisualTree()
    {
        if (_visualTreeReady) return;
        _view = RuntimeBattlePanelView.Build(transform);
        _statusText = _view.StatusText;
        _visualTreeReady = true;
    }

    private void EnsureInitialized()
    {
        if (_visualTreeReady) return;
        EnsureCanvas();
        EnsureEventSystem();
        BuildVisualTree();
    }

    private void Render()
    {
        EnsureInitialized();
        TryBindBootstrap();
        if (_adapter is null)
        {
            SetUnavailable("RuntimeAdapter not ready. Attach RuntimeBootstrap or call Bind(adapter).");
            return;
        }

        var snapshot = _adapter.Presentation.Snapshot;
        if (snapshot is null)
        {
            SetUnavailable("RuntimeAdapter bound; waiting for the first snapshot.");
            return;
        }
        if (TryFollowCurrentPlayer(snapshot))
            snapshot = _adapter.Presentation.Snapshot;
        if (snapshot is null)
        {
            SetUnavailable("RuntimeAdapter bound; waiting for the current-player snapshot.");
            return;
        }
        if (!RuntimeBattlePanelPresentationModel.IsViewerSnapshot(snapshot, viewerPlayerIndex))
        {
            SetUnavailable("RuntimeAdapter snapshot does not match the selected viewer.");
            return;
        }

        _statusText.text = string.IsNullOrWhiteSpace(_lastActionStatus)
            ? "RuntimeAdapter ready"
            : _lastActionStatus;
        _view.MatchText.text = RuntimeBattlePanelPresentationModel.BuildMatchLine(snapshot);
        var opponent = RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, false);
        var own = RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, true);
        _view.OpponentText.text = RuntimeBattlePanelPresentationModel.BuildPlayerSection(opponent, false);
        _view.CastleText.text = BuildCastleCardText(snapshot);
        _view.OwnText.text = RuntimeBattlePanelPresentationModel.BuildPlayerSection(own, true);
        _view.PhaseText.text = RuntimeBattlePanelPresentationModel.BuildPhaseSummary(snapshot);
        RenderTable(snapshot, opponent, own);
        RenderActions(snapshot);
        _view.EventsText.text = RuntimeBattlePanelPresentationModel.BuildEvents(_adapter.Presentation.Events);
        _renderedRevision = snapshot.SnapshotRevision;
        _renderedEventCount = _adapter.Presentation.Events.Count;
    }

    private void SetUnavailable(string message)
    {
        if (_statusText != null) _statusText.text = message;
        if (_view == null) return;
        if (_view.MatchText != null) _view.MatchText.text = "对局状态：无可用 presentation snapshot";
        if (_view.OpponentText != null) _view.OpponentText.text = "对手状态：等待 RuntimeAdapter";
        if (_view.CastleText != null) _view.CastleText.text = "共享王城：等待 snapshot";
        if (_view.OwnText != null) _view.OwnText.text = "己方状态：等待 RuntimeAdapter";
        if (_view.PhaseText != null) _view.PhaseText.text = "阶段：等待 snapshot";
        if (_view.EventsText != null) _view.EventsText.text = "事件摘要：等待 RuntimeAdapter";
        ClearActions();
        _actionGroups = Array.Empty<RuntimeBattlePanelActionGroup>();
        _renderedRevision = -1;
        _renderedEventCount = -1;
    }

    private bool TryFollowCurrentPlayer(RuntimeSnapshotEnvelope snapshot)
    {
        if (!followCurrentPlayer || _adapter is null || snapshot is null)
            return false;
        if (snapshot.CurrentPlayer is < 0 or > 1 || snapshot.CurrentPlayer == viewerPlayerIndex)
            return false;

        var previousViewer = viewerPlayerIndex;
        viewerPlayerIndex = snapshot.CurrentPlayer;
        _boundViewerPlayerIndex = -1;
        if (TryRefreshViewerSnapshot()) return true;

        // Keep the previously displayed viewer if the session cannot service
        // the handoff. The visible failure status is safer than guessing a
        // different player's hand.
        viewerPlayerIndex = previousViewer;
        _boundViewerPlayerIndex = -1;
        return false;
    }

    private void RenderTable(
        RuntimeSnapshotEnvelope snapshot,
        RuntimePlayerSnapshot opponent,
        RuntimePlayerSnapshot own)
    {
        ClearChildren(_view.OpponentHandRoot);
        ClearChildren(_view.OpponentFieldRoot);
        ClearChildren(_view.OwnHandRoot);
        ClearChildren(_view.OwnFieldRoot);
        ClearDropZones(_view.CastleRoot);
        ClearDropZones(_view.MechanicalRoot);

        var cardRoots = new List<CardRootRef>();
        if (opponent != null)
        {
            var backs = Math.Max(0, opponent.HandCount);
            // A very large count should remain readable rather than widening
            // over the side rails. The count remains authoritative in text.
            var visibleBacks = Math.Min(backs, 8);
            for (var index = 0; index < visibleBacks; index++)
            {
                var back = RuntimeBattlePanelView.CreateCardBack(
                    _view.OpponentHandRoot,
                    "OpponentHandBack_" + index,
                    false);
                back.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            }

            RenderPublicField(snapshot, opponent, _view.OpponentFieldRoot, cardRoots, false);
            SetPileCount(_view.OpponentDeckRoot, opponent.DeckCount);
            SetPileCount(_view.OpponentGraveyardRoot, opponent.GraveyardCount);
            SetPileCount(_view.OpponentExileRoot, null);
            SetQueueCount(_view.OpponentPhaseRoot, opponent.AmbushCount);
        }
        else
        {
            SetPileCount(_view.OpponentDeckRoot, null);
            SetPileCount(_view.OpponentGraveyardRoot, null);
            SetPileCount(_view.OpponentExileRoot, null);
            SetQueueCount(_view.OpponentPhaseRoot, null);
        }

        if (own != null)
        {
            RenderOwnCards(snapshot, own.Hand, _view.OwnHandRoot, cardRoots);
            RenderOwnCards(snapshot, own.Field, _view.OwnFieldRoot, cardRoots);
            SetPileCount(_view.OwnDeckRoot, own.DeckCount);
            SetPileCount(_view.OwnGraveyardRoot, own.GraveyardCount);
            SetPileCount(_view.OwnExileRoot, null);
            SetQueueCount(_view.OwnPhaseRoot, own.AmbushCount);
            SetQueueCount(FindDirectChild(_view.CenterPilesRoot, "CenterCloudMirror"), own.CloudStackCount);
            SetQueueCount(FindDirectChild(_view.CenterPilesRoot, "CenterCommitMirror"), own.CommitQueueCount);
            SetQueueCount(FindDirectChild(_view.MechanicalRoot, "CloudSlot"), own.CloudStackCount);
            SetQueueCount(FindDirectChild(_view.MechanicalRoot, "CommitQueueSlot"), own.CommitQueueCount);
        }
        else
        {
            SetPileCount(_view.OwnDeckRoot, null);
            SetPileCount(_view.OwnGraveyardRoot, null);
            SetPileCount(_view.OwnExileRoot, null);
            SetQueueCount(_view.OwnPhaseRoot, null);
            SetQueueCount(FindDirectChild(_view.CenterPilesRoot, "CenterCloudMirror"), null);
            SetQueueCount(FindDirectChild(_view.CenterPilesRoot, "CenterCommitMirror"), null);
            SetQueueCount(FindDirectChild(_view.MechanicalRoot, "CloudSlot"), null);
            SetQueueCount(FindDirectChild(_view.MechanicalRoot, "CommitQueueSlot"), null);
        }

        RuntimeBattlePanelView.FitCardStrip(_view.OpponentHandRoot);
        RuntimeBattlePanelView.FitCardStrip(_view.OpponentFieldRoot);
        RuntimeBattlePanelView.FitCardStrip(_view.OwnFieldRoot);
        RuntimeBattlePanelView.FitCardStrip(_view.OwnHandRoot);

        // Every advertised target gets a visible drop surface. Card targets
        // use their own frame; other wire targets use the neutral queue rail.
        RenderDropZones(snapshot, cardRoots);
    }

    private void RenderPublicField(
        RuntimeSnapshotEnvelope snapshot,
        RuntimePlayerSnapshot player,
        RectTransform parent,
        List<CardRootRef> cardRoots,
        bool viewer)
    {
        if (player.Field == null) return;
        for (var index = 0; index < player.Field.Count; index++)
        {
            var card = player.Field[index];
            if (card == null) continue;
            var sourceActions = viewer ? FindSourceActions(snapshot, card.EntityId) : new List<RuntimeLegalAction>();
            var targetHighlighted = FindTargetActions(snapshot, card.EntityId).Count > 0;
            var cardRoot = RuntimeBattlePanelView.CreateCardFace(
                parent,
                (viewer ? "OwnFieldCard_" : "OpponentFieldCard_") + index,
                DisplayCardName(card),
                DisplayEntity(card),
                "—",
                "—",
                "—",
                AccentForCard(card),
                sourceActions.Count > 0,
                targetHighlighted,
                viewer && sourceActions.Count > 0);
            cardRoots.Add(new CardRootRef(card, cardRoot));
            ConfigureCardInteraction(cardRoot, sourceActions, targetHighlighted);
        }
    }

    private void RenderOwnCards(
        RuntimeSnapshotEnvelope snapshot,
        IReadOnlyList<RuntimeCardSnapshot> cards,
        RectTransform parent,
        List<CardRootRef> cardRoots)
    {
        if (cards == null) return;
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            if (card == null) continue;
            var sourceActions = FindSourceActions(snapshot, card.EntityId);
            var targetHighlighted = FindTargetActions(snapshot, card.EntityId).Count > 0;
            var cardRoot = RuntimeBattlePanelView.CreateCardFace(
                parent,
                "OwnCard_" + index,
                DisplayCardName(card),
                DisplayEntity(card),
                "—",
                "—",
                "—",
                AccentForCard(card),
                sourceActions.Count > 0,
                targetHighlighted,
                sourceActions.Count > 0);
            cardRoots.Add(new CardRootRef(card, cardRoot));
            ConfigureCardInteraction(cardRoot, sourceActions, targetHighlighted);
        }
    }

    private void ConfigureCardInteraction(
        RectTransform cardRoot,
        IReadOnlyList<RuntimeLegalAction> sourceActions,
        bool targetHighlighted)
    {
        var image = cardRoot.GetComponent<UnityEngine.UI.Image>();
        if (image == null) return;
        image.raycastTarget = sourceActions.Count > 0 || targetHighlighted;
        if (sourceActions.Count == 0) return;

        var drag = cardRoot.gameObject.AddComponent<RuntimeBattleCardDrag>();
        var canvas = GetComponentInParent<UnityEngine.Canvas>();
        drag.Configure(sourceActions, canvas, SubmitAdvertisedAction);

        // Click is the fallback for a card with one unambiguous advertised
        // action. Multiple source/target variants stay in the action rail.
        var button = cardRoot.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        button.interactable = true;
        var colors = button.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0.06f);
        colors.highlightedColor = new Color(0.40f, 0.90f, 0.94f, 0.22f);
        colors.pressedColor = new Color(0.98f, 0.76f, 0.28f, 0.30f);
        button.colors = colors;
        button.onClick.AddListener(() => drag.TryClickFallback());
    }

    private void RenderDropZones(RuntimeSnapshotEnvelope snapshot, IReadOnlyList<CardRootRef> cardRoots)
    {
        for (var index = 0; index < cardRoots.Count; index++)
            ClearDropZones(cardRoots[index].Root);

        if (snapshot.LegalActions == null) return;
        for (var index = 0; index < snapshot.LegalActions.Count; index++)
        {
            var legal = snapshot.LegalActions[index];
            if (legal == null || legal.TargetId == null) continue;
            RectTransform targetRoot = null;
            for (var cardIndex = 0; cardIndex < cardRoots.Count; cardIndex++)
            {
                if (RuntimeBattlePanelActionModel.WireValuesEqual(
                    legal.TargetId,
                    cardRoots[cardIndex].Card.EntityId))
                {
                    targetRoot = cardRoots[cardIndex].Root;
                    break;
                }
            }
            if (targetRoot == null)
                targetRoot = IsCastleTarget(legal.TargetId) ? _view.CastleRoot : _view.MechanicalRoot;
            AddDropZone(targetRoot, legal.TargetId);
        }
        AddDropZone(_view.CastleRoot, "shared_castle");
    }

    private static void AddDropZone(RectTransform root, object targetId)
    {
        if (root == null) return;
        var image = root.GetComponent<UnityEngine.UI.Image>();
        if (image != null) image.raycastTarget = true;
        var zones = root.GetComponents<RuntimeBattleDropZone>();
        for (var index = 0; index < zones.Length; index++)
        {
            if (zones[index].enabled && RuntimeBattlePanelActionModel.WireValuesEqual(zones[index].TargetId, targetId)) return;
        }
        var zone = root.gameObject.AddComponent<RuntimeBattleDropZone>();
        zone.Configure(targetId);
    }

    private static void ClearDropZones(RectTransform root)
    {
        if (root == null) return;
        var zones = root.GetComponents<RuntimeBattleDropZone>();
        for (var index = zones.Length - 1; index >= 0; index--)
        {
            if (Application.isPlaying)
            {
                zones[index].enabled = false;
                Destroy(zones[index]);
            }
            else DestroyImmediate(zones[index]);
        }
    }

    private static List<RuntimeLegalAction> FindSourceActions(RuntimeSnapshotEnvelope snapshot, long sourceId)
    {
        var actions = new List<RuntimeLegalAction>();
        if (snapshot.LegalActions == null) return actions;
        for (var index = 0; index < snapshot.LegalActions.Count; index++)
        {
            var legal = snapshot.LegalActions[index];
            if (legal != null && RuntimeBattlePanelActionModel.WireValuesEqual(legal.SourceId, sourceId))
                actions.Add(legal);
        }
        return actions;
    }

    private static List<RuntimeLegalAction> FindTargetActions(RuntimeSnapshotEnvelope snapshot, long targetId)
    {
        var actions = new List<RuntimeLegalAction>();
        if (snapshot.LegalActions == null) return actions;
        for (var index = 0; index < snapshot.LegalActions.Count; index++)
        {
            var legal = snapshot.LegalActions[index];
            if (legal != null && RuntimeBattlePanelActionModel.WireValuesEqual(legal.TargetId, targetId))
                actions.Add(legal);
        }
        return actions;
    }

    private static bool IsCastleTarget(object target)
    {
        var text = target as string;
        if (text == null) return false;
        return string.Equals(text, "castle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "shared_castle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "core", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "kingdom_core", StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayCardName(RuntimeCardSnapshot card)
    {
        return card == null || string.IsNullOrWhiteSpace(card.CardId) ? "—" : card.CardId;
    }

    private static string DisplayEntity(RuntimeCardSnapshot card)
    {
        return card == null || card.EntityId <= 0 ? "—" : "#" + card.EntityId;
    }

    private static Color AccentForCard(RuntimeCardSnapshot card)
    {
        var id = card == null ? string.Empty : (card.CardId ?? string.Empty).ToLowerInvariant();
        if (id.Contains("wood")) return Hex("5BAE78");
        if (id.Contains("sea")) return Hex("5C9DD5");
        if (id.Contains("machine") || id.Contains("mech") || id.Contains("commit")) return Hex("B18AE0");
        if (id.Contains("flame") || id.Contains("fire")) return Hex("E88362");
        return Hex("6FAFC2");
    }

    private static void SetPileCount(RectTransform root, int? value)
    {
        if (root == null) return;
        var text = FindChildText(root, "PileCount");
        if (text != null) text.text = value.HasValue ? value.Value.ToString() : "—";
    }

    private static void SetQueueCount(RectTransform root, int? value)
    {
        if (root == null) return;
        var text = FindChildText(root, "QueueCount");
        if (text != null) text.text = value.HasValue ? value.Value.ToString() : "—";
    }

    private static UnityEngine.UI.Text FindChildText(RectTransform root, string name)
    {
        var texts = root.GetComponentsInChildren<UnityEngine.UI.Text>(true);
        for (var index = 0; index < texts.Length; index++)
            if (texts[index].gameObject.name == name) return texts[index];
        return null;
    }

    private static RectTransform FindDirectChild(RectTransform root, string name)
    {
        if (root == null) return null;
        var child = root.Find(name);
        return child == null ? null : child.GetComponent<RectTransform>();
    }

    private static string BuildCastleCardText(RuntimeSnapshotEnvelope snapshot)
    {
        if (snapshot == null || snapshot.Castle == null || !snapshot.Castle.Enabled)
            return "LIFE  —\nBARRIER  —\nTURN  —  ·  WIN COUNT  —";
        return "LIFE  " + snapshot.Castle.Health +
            "\nBARRIER  —" +
            "\nTURN  " + snapshot.Turn + "  ·  WIN COUNT  —";
    }

    private static void ClearChildren(RectTransform root)
    {
        if (root == null) return;
        for (var index = root.childCount - 1; index >= 0; index--)
        {
            var child = root.GetChild(index).gameObject;
            if (Application.isPlaying)
            {
                child.SetActive(false);
                Destroy(child);
            }
            else DestroyImmediate(child);
        }
    }

    private sealed class CardRootRef
    {
        public CardRootRef(RuntimeCardSnapshot card, RectTransform root) { Card = card; Root = root; }
        public RuntimeCardSnapshot Card { get; }
        public RectTransform Root { get; }
    }

    private void RenderActions(RuntimeSnapshotEnvelope snapshot)
    {
        ClearActions();
        _actionGroups = RuntimeBattlePanelActionModel.BuildActionGroups(snapshot);
        if (snapshot.LegalActions is null || snapshot.LegalActions.Count == 0 || _actionGroups.Count == 0)
        {
            CreateActionInfo("No legal actions advertised by the engine.", false);
            return;
        }

        foreach (var legal in snapshot.LegalActions)
        {
            if (legal is null)
            {
                CreateActionInfo("Unavailable action: snapshot entry is missing.", false);
            }
        }

        foreach (var group in _actionGroups)
        {
            if (group.Actions.Count == 1)
                CreateActionButton(group.Actions[0], _view.ActionsRoot);
            else
                CreateActionGroup(group, _view.ActionsRoot);
        }
    }

    private void CreateActionButton(
        RuntimeBattlePanelActionEntry entry,
        RectTransform parent)
    {
        var legal = entry.LegalAction;
        var buttonObject = RuntimeBattlePanelView.CreateRect(
            "Action_" + SanitizeName(string.IsNullOrWhiteSpace(legal.ActionId) ? "unknown" : legal.ActionId),
            parent);
        var buttonImage = buttonObject.gameObject.AddComponent<UnityEngine.UI.Image>();
        buttonImage.color = entry.State.Interactable
            ? ActionAccent(legal.Type)
            : new Color(0.18f, 0.18f, 0.20f, 1f);
        buttonImage.raycastTarget = true;
        var outline = buttonObject.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = entry.State.Interactable
            ? new Color(0.62f, 0.88f, 0.92f, 0.80f)
            : new Color(0.30f, 0.34f, 0.37f, 0.60f);
        outline.effectDistance = new Vector2(1f, 1f);
        var button = buttonObject.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = buttonImage;
        button.interactable = entry.State.Interactable;
        var label = RuntimeBattlePanelView.CreateText(
            buttonObject,
            "Label",
            16,
            entry.State.Interactable ? Color.white : new Color(0.64f, 0.64f, 0.67f));
        label.alignment = TextAnchor.MiddleCenter;
        label.fontStyle = FontStyle.Bold;
        label.text = BuildActionButtonLabel(entry);
        var element = buttonObject.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        element.minHeight = 44f;
        element.preferredHeight = 44f;
        if (entry.State.Interactable)
        {
            var captured = legal;
            button.onClick.AddListener(() => SubmitAdvertisedAction(captured));
        }
    }

    private void CreateActionGroup(
        RuntimeBattlePanelActionGroup group,
        RectTransform parent)
    {
        var groupObject = RuntimeBattlePanelView.CreateRect(
            "ActionGroup_" + SanitizeName(group.GroupKey),
            parent);
        var groupLayout = groupObject.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        groupLayout.spacing = 3f;
        groupLayout.childControlWidth = true;
        groupLayout.childControlHeight = true;
        groupLayout.childForceExpandWidth = true;
        groupLayout.childForceExpandHeight = false;

        var groupElement = groupObject.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        groupElement.preferredHeight = 92f + (group.Actions.Count * 46f);
        groupElement.minHeight = groupElement.preferredHeight;

        var heading = RuntimeBattlePanelView.CreateText(
            groupObject,
            "SelectionLabel",
            18,
            new Color(0.78f, 0.86f, 0.96f));
        heading.text = BuildActionGroupLabel(group);
        RuntimeBattlePanelView.SetPreferredHeight(heading.rectTransform, 22f);

        var optionsObject = RuntimeBattlePanelView.CreateRect("Options", groupObject);
        var optionsLayout = optionsObject.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        optionsLayout.spacing = 3f;
        optionsLayout.childControlWidth = true;
        optionsLayout.childControlHeight = true;
        optionsLayout.childForceExpandWidth = true;
        optionsLayout.childForceExpandHeight = false;
        var toggleGroup = optionsObject.gameObject.AddComponent<UnityEngine.UI.ToggleGroup>();
        toggleGroup.allowSwitchOff = false;
        var optionsElement = optionsObject.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        optionsElement.preferredHeight = group.Actions.Count * 46f;
        optionsElement.minHeight = optionsElement.preferredHeight;

        foreach (var entry in group.Actions)
            CreateActionChoice(group, entry, optionsObject, toggleGroup);

        var submitObject = RuntimeBattlePanelView.CreateRect("Submit", groupObject);
        var submitImage = submitObject.gameObject.AddComponent<UnityEngine.UI.Image>();
        submitImage.color = new Color(0.16f, 0.42f, 0.31f, 1f);
        submitImage.raycastTarget = true;
        var submitOutline = submitObject.gameObject.AddComponent<UnityEngine.UI.Outline>();
        submitOutline.effectColor = new Color(0.42f, 0.88f, 0.64f, 0.85f);
        submitOutline.effectDistance = new Vector2(1f, 1f);
        var submitButton = submitObject.gameObject.AddComponent<UnityEngine.UI.Button>();
        submitButton.targetGraphic = submitImage;
        submitButton.interactable = group.SelectedAction.State.Interactable;
        var submitLabel = RuntimeBattlePanelView.CreateText(
            submitObject,
            "Label",
            16,
            Color.white);
        submitLabel.alignment = TextAnchor.MiddleCenter;
        submitLabel.fontStyle = FontStyle.Bold;
        submitLabel.text = "SUBMIT  " + group.SelectedAction.LegalAction.Type;
        RuntimeBattlePanelView.SetPreferredHeight(submitLabel.rectTransform, 44f);
        var submitElement = submitObject.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        submitElement.preferredHeight = 44f;
        submitElement.minHeight = 44f;
        submitButton.onClick.AddListener(() =>
        {
            var selected = group.SelectedAction;
            if (selected is not null && selected.State.Interactable)
                SubmitAdvertisedAction(selected.LegalAction);
        });
    }

    private static void CreateActionChoice(
        RuntimeBattlePanelActionGroup group,
        RuntimeBattlePanelActionEntry entry,
        RectTransform parent,
        UnityEngine.UI.ToggleGroup toggleGroup)
    {
        var legal = entry.LegalAction;
        var choiceObject = RuntimeBattlePanelView.CreateRect(
            "Choice_" + SanitizeName(string.IsNullOrWhiteSpace(legal.ActionId) ? "unknown" : legal.ActionId),
            parent);
        var choiceImage = choiceObject.gameObject.AddComponent<UnityEngine.UI.Image>();
        choiceImage.color = entry.IsSelected
            ? new Color(0.18f, 0.38f, 0.56f, 1f)
            : new Color(0.10f, 0.19f, 0.28f, 1f);
        choiceImage.raycastTarget = true;
        var choiceOutline = choiceObject.gameObject.AddComponent<UnityEngine.UI.Outline>();
        choiceOutline.effectColor = entry.IsSelected
            ? new Color(0.98f, 0.76f, 0.30f, 0.95f)
            : new Color(0.32f, 0.50f, 0.58f, 0.80f);
        choiceOutline.effectDistance = new Vector2(1f, 1f);
        var toggle = choiceObject.gameObject.AddComponent<UnityEngine.UI.Toggle>();
        toggle.targetGraphic = choiceImage;
        toggle.group = toggleGroup;
        toggle.interactable = entry.State.Interactable;
        toggle.isOn = entry.IsSelected;
        var label = RuntimeBattlePanelView.CreateText(
            choiceObject,
            "Label",
            16,
            entry.State.Interactable ? Color.white : new Color(0.64f, 0.64f, 0.67f));
        label.alignment = TextAnchor.MiddleCenter;
        label.text = entry.Label;
        var element = choiceObject.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        element.preferredHeight = 44f;
        element.minHeight = 44f;
        var capturedActionId = legal.ActionId;
        toggle.onValueChanged.AddListener(isOn =>
        {
            if (!isOn) return;
            group.TrySelectAction(capturedActionId);
        });
    }

    private static string BuildActionGroupLabel(RuntimeBattlePanelActionGroup group)
    {
        var first = group.Actions[0].LegalAction;
        var label = string.IsNullOrWhiteSpace(first.Type) ? "UNKNOWN" : first.Type;
        if (!string.IsNullOrWhiteSpace(first.CardId)) label += " " + first.CardId;
        if (group.RequiresSourceSelection) label += " · 选择来源";
        if (group.RequiresTargetSelection) label += " · 选择目标";
        return label;
    }

    private static string BuildActionButtonLabel(RuntimeBattlePanelActionEntry entry)
    {
        var legal = entry.LegalAction;
        var type = string.IsNullOrWhiteSpace(legal.Type) ? "ACTION" : legal.Type;
        if (string.Equals(type, "PLAY_CARD", StringComparison.OrdinalIgnoreCase)) type = "PLAY";
        else if (string.Equals(type, "END_TURN", StringComparison.OrdinalIgnoreCase)) type = "END TURN";
        else if (string.Equals(type, "SKIP_AMBUSH", StringComparison.OrdinalIgnoreCase)) type = "SKIP AMBUSH";
        else if (string.Equals(type, "PULL", StringComparison.OrdinalIgnoreCase)) type = "PULL";
        else if (string.Equals(type, "ATTACK", StringComparison.OrdinalIgnoreCase)) type = "ATTACK";
        var suffix = string.IsNullOrWhiteSpace(legal.CardId) ? string.Empty : "  " + legal.CardId;
        return type + suffix;
    }

    private static Color ActionAccent(string actionType)
    {
        var type = actionType ?? string.Empty;
        if (type.IndexOf("PLAY", StringComparison.OrdinalIgnoreCase) >= 0) return Hex("245979");
        if (type.IndexOf("ATTACK", StringComparison.OrdinalIgnoreCase) >= 0) return Hex("713B48");
        if (type.IndexOf("PULL", StringComparison.OrdinalIgnoreCase) >= 0) return Hex("5A4279");
        if (type.IndexOf("END", StringComparison.OrdinalIgnoreCase) >= 0) return Hex("356B58");
        return Hex("334858");
    }

    private static string SanitizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(char.IsLetterOrDigit(character) || character == '_' || character == '-' ? character : '_');
        return builder.ToString();
    }

    private static Color Hex(string value)
    {
        if (ColorUtility.TryParseHtmlString("#" + value, out var color)) return color;
        return Color.magenta;
    }

    private void SubmitAdvertisedAction(RuntimeLegalAction legal)
    {
        if (_adapter is null)
        {
            _lastActionStatus = "Action unavailable: RuntimeAdapter is not ready.";
            Render();
            return;
        }
        var snapshot = _adapter.Presentation.Snapshot;
        if (snapshot is null)
        {
            _lastActionStatus = "Action unavailable: snapshot is not ready.";
            Render();
            return;
        }

        try
        {
            var action = RuntimeBattlePanelActionModel.ToGameAction(legal, snapshot.MatchId);
            var validation = RuntimeActionBoundary.Validate(action, snapshot);
            if (!validation.Accepted)
            {
                _lastActionStatus = "Action rejected: " + validation.ReasonKey;
                RefreshAfterActionFailure();
                return;
            }

            var submission = _adapter.Submit(action);
            _boundViewerPlayerIndex = -1;
            var refreshed = TryRefreshViewerSnapshot();
            _lastActionStatus = submission.Result.Accepted
                ? "Action accepted: " + legal.Type + " (" + DisplayReason(submission.Result.ReasonKey, "action.accepted") + ")"
                : "Action rejected: " + DisplayReason(submission.Result.ReasonKey, "action.rejected");
            if (!refreshed) _lastActionStatus += " | snapshot refresh failed";
            Render();
        }
        catch (Exception exception)
        {
            _lastActionStatus = "Action submission failed: " + exception.Message;
            RefreshAfterActionFailure();
        }
    }

    private void RefreshAfterActionFailure()
    {
        _boundViewerPlayerIndex = -1;
        var refreshed = TryRefreshViewerSnapshot();
        if (!refreshed) _lastActionStatus += " | snapshot refresh failed";
        Render();
    }

    private static string DisplayReason(string reason, string fallback)
    {
        return string.IsNullOrWhiteSpace(reason) ? fallback : reason;
    }

    private void ClearActions()
    {
        if (_view is null || _view.ActionsRoot is null) return;
        for (var index = _view.ActionsRoot.childCount - 1; index >= 0; index--)
        {
            var child = _view.ActionsRoot.GetChild(index).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }

    private void CreateActionInfo(string message, bool interactable)
    {
        var objectInfo = new GameObject("ActionInfo", typeof(RectTransform));
        objectInfo.transform.SetParent(_view.ActionsRoot, false);
        var label = RuntimeBattlePanelView.CreateText(
            objectInfo.GetComponent<RectTransform>(),
            "Label",
            15,
            new Color(0.70f, 0.70f, 0.73f));
        label.text = message;
        var element = objectInfo.AddComponent<UnityEngine.UI.LayoutElement>();
        element.preferredHeight = 34f;
        _ = interactable;
    }

    private void OnDestroy()
    {
        if (_createdEventSystem == null) return;
        if (Application.isPlaying) Destroy(_createdEventSystem.gameObject);
        else DestroyImmediate(_createdEventSystem.gameObject);
        _createdEventSystem = null;
    }
}
}
