using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DominionWars.Adapters;
using DominionWars.Data;
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

    [Header("Accessibility")]
    [SerializeField] private bool reducedMotion;

    [Header("Diagnostics")]
    [SerializeField] private bool debugOverlayEnabled;

    private RuntimeAdapter _adapter;
    private UnityEngine.EventSystems.EventSystem _createdEventSystem;
    private RuntimeBattlePanelView _view;
    private UnityEngine.UI.Text _statusText;
    private RuntimeBattlePanelActionFeedback _actionFeedback;
    private RuntimeContentResolver _contentResolver;
    private CardCatalog _cardCatalog;
    private RuntimeContentContext _contentContext;
    private RuntimeContentResolverLease _contentResolverLease;
    private bool _ownsContentResolver;
    private bool _explicitContentBinding;
    private bool _contentResolutionAttempted;
    private bool _presentationFaulted;
    private bool _visualTreeReady;
    private long _renderedRevision = -1;
    private int _renderedEventCount = -1;
    private int _boundViewerPlayerIndex = -1;
    private string _lastActionStatus = string.Empty;
    private string _lastDiagnostic = string.Empty;
    private IReadOnlyList<RuntimeBattlePanelActionGroup> _actionGroups =
        Array.Empty<RuntimeBattlePanelActionGroup>();
    private long? _selectedCardEntityId;
    private RuntimeCardInspectInteraction _selectedCardInteraction;
    private BindingMode _bindingMode;

    public RuntimeAdapter Adapter => _adapter;
    public RuntimeBootstrap Bootstrap => bootstrap;
    public int ViewerPlayerIndex => viewerPlayerIndex;
    public bool FollowCurrentPlayer => followCurrentPlayer;
    public IReadOnlyList<RuntimeBattlePanelActionGroup> ActionGroups => _actionGroups;
    public long? SelectedCardEntityId => _selectedCardEntityId;
    public string LastActionStatus => _lastActionStatus;
    public bool DiagnosticsVisible => debugOverlayEnabled && IsDiagnosticsBuild;
    public string LastDiagnostic => _lastDiagnostic;
    public bool ReducedMotion => reducedMotion;
    public UnityEngine.UI.Toggle ReducedMotionToggle => _view?.ReducedMotionToggle;
    public UnityEngine.UI.Button RecoveryButton => _view?.RecoveryButton;
    public RuntimeBattlePanelActionFeedback ActionFeedback => _actionFeedback;
    public RuntimeContentResolver ContentResolver => _contentResolver;
    public CardCatalog CardCatalog => _cardCatalog;
    public RuntimeContentContext ContentContext => _contentContext;
    public RuntimeContentResolverOwnership ContentResolverOwnership =>
        _contentResolver == null
            ? RuntimeContentResolverOwnership.None
            : _contentResolverLease != null
                ? RuntimeContentResolverOwnership.Borrowed
                : RuntimeContentResolverOwnership.Owned;
    public event Action RecoveryRequested;

    private static bool IsDiagnosticsBuild => Application.isEditor || Debug.isDebugBuild;

    /// <summary>
    /// Enables the complete wire/adapter diagnostic overlay for local Editor
    /// or Development Build inspection. Production builds always keep it off.
    /// </summary>
    public void SetDebugOverlayEnabled(bool enabled)
    {
        debugOverlayEnabled = enabled && IsDiagnosticsBuild;
        if (_view != null)
        {
            _view.SetDebugOverlayVisible(DiagnosticsVisible);
            Render();
        }
    }

    /// <summary>
    /// Creates the neutral MVP panel after a scene loads when no panel was
    /// authored in the scene. This keeps the first slice runnable without a
    /// hand-written scene/prefab and remains removable by deleting this UI
    /// assembly or disabling the component.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeInstance()
    {
        EnsureRuntimeInstanceForScene();
    }

    /// <summary>
    /// Ensures that the production battle surface exists for the currently
    /// loaded scene. RuntimeInitializeOnLoadMethod(AfterSceneLoad) runs for
    /// the initial Player scene, but it is not a per-scene-load contract for
    /// scenes loaded later by a host or the Unity Test Runner. The screen flow
    /// calls this after each scene load so a reload cannot silently lose the
    /// presentation surface.
    /// </summary>
    internal static RuntimeBattlePanel EnsureRuntimeInstanceForScene()
    {
        var existing = UnityEngine.Object.FindObjectsByType<RuntimeBattlePanel>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (existing.Length > 0) return existing[0];

        var panelObject = new GameObject(
            "DominionWarsRuntimeBattlePanel",
            typeof(RectTransform),
            typeof(UnityEngine.Canvas),
            typeof(UnityEngine.UI.CanvasScaler),
            typeof(UnityEngine.UI.GraphicRaycaster));
        return panelObject.AddComponent<RuntimeBattlePanel>();
    }

    private void Awake()
    {
        EnsureInitialized();
        TryBindBootstrap();
        Render();
    }

    private void Update()
    {
        _actionFeedback?.Tick(Time.unscaledDeltaTime);
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
        _explicitContentBinding = false;
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
        _explicitContentBinding = false;
        bootstrap = null;
        _adapter = runtimeAdapter;
        ResetBindingState();
        if (_contentContext != null)
        {
            ReleasePresentationContent();
            _contentResolutionAttempted = false;
        }
        EnsurePresentationContent();
        TryRefreshViewerSnapshot();
        Render();
    }

    /// <summary>
    /// Binds presentation-only content services for tests or an authored
    /// front-end host. The resolver never enters the gameplay adapter and the
    /// optional card catalog is used only to read an artId from card data.
    /// </summary>
    public void BindContentResolver(RuntimeContentResolver resolver, CardCatalog cardCatalog = null)
    {
        if (resolver == null) throw new ArgumentNullException(nameof(resolver));
        if (!ReferenceEquals(_contentResolver, resolver) || _contentResolverLease != null)
            ReleasePresentationContent();
        _explicitContentBinding = true;
        _contentResolver = resolver;
        _ownsContentResolver = true;
        _cardCatalog = cardCatalog;
        _contentResolutionAttempted = true;
        _renderedRevision = -1;
        Render();
    }

    /// <summary>
    /// Clears an explicit binding and resumes the configured automatic
    /// RuntimeBootstrap discovery on the next bind/render pass.
    /// </summary>
    public void Unbind()
    {
        _bindingMode = BindingMode.Automatic;
        _explicitContentBinding = false;
        bootstrap = null;
        _adapter = null;
        ResetBindingState();
        if (_contentContext != null)
        {
            ReleasePresentationContent();
            _contentResolutionAttempted = false;
        }
        ClearUnavailablePresentation();
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

    /// <summary>
    /// Toggles the presentation-only reduced-motion path. It never changes
    /// the adapter snapshot, legal actions, or event stream.
    /// </summary>
    public void SetReducedMotion(bool enabled)
    {
        reducedMotion = enabled;
        EnsureInitialized();
        if (_view?.ReducedMotionToggle != null && _view.ReducedMotionToggle.isOn != enabled)
            _view.ReducedMotionToggle.SetIsOnWithoutNotify(enabled);
        _actionFeedback.SetReducedMotion(enabled);
    }

    public void Refresh()
    {
        Render();
    }

    /// <summary>
    /// Presentation-only recovery request. The screen-flow host owns session
    /// stop and navigation; this panel never restarts the authoritative match.
    /// </summary>
    public void RequestRecovery()
    {
        RecoveryRequested?.Invoke();
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

        if (!_explicitContentBinding)
            SyncPresentationContent();

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
        _lastDiagnostic = string.Empty;
        _selectedCardEntityId = null;
        _selectedCardInteraction = null;
        _presentationFaulted = false;
        _actionFeedback?.ResetForBinding();
    }

    private bool TryRefreshViewerSnapshot()
    {
        if (_adapter is null || _boundViewerPlayerIndex == viewerPlayerIndex)
            return _adapter is not null;

        try
        {
            _adapter.RefreshSnapshot(viewerPlayerIndex);
            _boundViewerPlayerIndex = viewerPlayerIndex;
            _presentationFaulted = false;
            return true;
        }
        catch (Exception exception)
        {
            RecordDiagnostic("Viewer snapshot refresh failed: " + exception);
            _lastActionStatus = RuntimeBattlePanelPresentationModel.Unavailable;
            _presentationFaulted = true;
            ClearUnavailablePresentation();
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
        if (!createEventSystemIfMissing)
            return;

        var existing = UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (existing != null)
        {
            // A scene can provide the EventSystem without an input module.
            // Keep that single compatible EventSystem and repair the missing
            // module rather than silently leaving every uGUI surface inert.
            if (existing.GetComponent<UnityEngine.EventSystems.BaseInputModule>() == null)
                existing.gameObject.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            return;
        }

        var eventSystemObject = new GameObject("DominionWarsEventSystem");
        _createdEventSystem = eventSystemObject.AddComponent<UnityEngine.EventSystems.EventSystem>();
        eventSystemObject.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }

    private void BuildVisualTree()
    {
        if (_visualTreeReady) return;
        _view = RuntimeBattlePanelView.Build(transform);
        _statusText = _view.StatusText;
        _actionFeedback = new RuntimeBattlePanelActionFeedback();
        _actionFeedback.Bind(_view);
        _actionFeedback.SetReducedMotion(reducedMotion);
        if (_view.ReducedMotionToggle != null)
        {
            _view.ReducedMotionToggle.SetIsOnWithoutNotify(reducedMotion);
            _view.ReducedMotionToggle.onValueChanged.AddListener(SetReducedMotion);
        }
        if (_view.RecoveryButton != null)
        {
            _view.RecoveryButton.interactable = false;
            _view.RecoveryButton.onClick.AddListener(RequestRecovery);
        }
        _visualTreeReady = true;
    }

    private void EnsureInitialized()
    {
        if (_visualTreeReady) return;
        EnsureCanvas();
        EnsureEventSystem();
        BuildVisualTree();
        EnsurePresentationContent();
    }

    private void EnsurePresentationContent()
    {
        if (_contentResolutionAttempted) return;

        // A panel may awaken before RuntimeBootstrap. Inspect the serialized
        // host (or discover it using the existing setting) so both execution
        // orders can enter the same context before the first card is drawn.
        if (!_explicitContentBinding)
        {
            var host = bootstrap;
            if (host == null && findBootstrapOnStart)
                host = UnityEngine.Object.FindFirstObjectByType<RuntimeBootstrap>();
            if (host != null && host.UseSharedContentContext &&
                TryBindSharedContent(host))
                return;
        }

        LoadLegacyPresentationContent();
    }

    private void SyncPresentationContent()
    {
        if (_explicitContentBinding) return;

        if (bootstrap != null && bootstrap.UseSharedContentContext)
        {
            if (TryBindSharedContent(bootstrap)) return;

            // A disposed host context must not leave the panel holding a
            // borrowed resolver. Keep the old placeholder path available.
            if (_contentContext != null)
            {
                ReleasePresentationContent();
                _contentResolutionAttempted = false;
            }
        }
        else if (_contentContext != null)
        {
            // The feature flag is opt-in. If a host is rebound to the legacy
            // path, release only the borrowed lease and reload the old
            // presentation services without changing any UI semantics.
            ReleasePresentationContent();
            _contentResolutionAttempted = false;
        }

        if (!_contentResolutionAttempted)
            LoadLegacyPresentationContent();
    }

    private bool TryBindSharedContent(RuntimeBootstrap host)
    {
        if (host == null || !host.UseSharedContentContext) return false;

        RuntimeContentContext context;
        try
        {
            context = host.SharedContentContext;
        }
        catch (Exception exception) when (IsExpectedContentFailure(exception))
        {
            return false;
        }

        if (context == null || context.IsDisposed) return false;

        if (!ReferenceEquals(_contentContext, context))
        {
            ReleasePresentationContent();
            _contentContext = context;
            _contentResolutionAttempted = true;
        }

        // The context intentionally does not cache failed loads. Retrying
        // here also lets a build/staging operation make content available
        // after the panel has already rendered its placeholder.
        if (_cardCatalog == null)
        {
            try
            {
                if (context.TryGetCardCatalog(out var catalog))
                    _cardCatalog = catalog;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        if (_contentResolverLease == null)
        {
            try
            {
                if (context.TryBorrowContentResolver(out var lease))
                {
                    // A missing content manifest may have caused the legacy
                    // placeholder resolver to be created by ResolveCardArt.
                    // Replace that owned fallback only once the shared
                    // context has a real resolver to lend.
                    if (_ownsContentResolver && _contentResolver != null)
                        _contentResolver.ClearTextureCache();
                    _contentResolver = lease.Resolver;
                    _contentResolverLease = lease;
                    _ownsContentResolver = false;
                }
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        return true;
    }

    private void LoadLegacyPresentationContent()
    {
        _contentResolutionAttempted = true;

        // Visual assets keep one packaged runtime root. In the Editor this may
        // be unavailable until build staging runs; its safe unavailable
        // instance still supplies generated card placeholders.
        RuntimeContentResolver.TryLoadFromStreamingAssets(out _contentResolver, true);

        // Card labels come from the same canonical data-root policy as the
        // runtime bootstrap. Editor play therefore reads repository data while
        // Players remain restricted to the owned StreamingAssets package.
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        var repositoryRoot = projectRoot is null
            ? null
            : Directory.GetParent(projectRoot)?.Parent?.FullName;
        var dataRoot = RuntimeDataRootPolicy.FindUsableRoot(
            RuntimeDataRootPolicy.ResolveDataCandidates(
                Application.isEditor,
                string.Empty,
                Path.Combine(Application.streamingAssetsPath, "data"),
                Path.Combine(Directory.GetCurrentDirectory(), "data"),
                projectRoot is null ? string.Empty : Path.Combine(projectRoot, "data"),
                repositoryRoot is null ? string.Empty : Path.Combine(repositoryRoot, "data")),
            requireGeneratedMarker: !Application.isEditor);
        if (string.IsNullOrWhiteSpace(dataRoot)) return;

        var cardsDirectory = Path.Combine(dataRoot, "cards");
        try
        {
            _cardCatalog = CardCatalog.LoadDirectory(cardsDirectory);
        }
        catch (Exception)
        {
            // Card data remains owned by the authoritative runtime. A broken
            // optional presentation lookup must never prevent the tabletop
            // from starting or submitting actions.
            _cardCatalog = null;
        }
    }

    private void ReleasePresentationContent()
    {
        _contentResolverLease?.Dispose();
        _contentResolverLease = null;
        if (_ownsContentResolver && _contentResolver != null)
            _contentResolver.ClearTextureCache();
        _contentResolver = null;
        _cardCatalog = null;
        _contentContext = null;
        _ownsContentResolver = false;
    }

    private static bool IsExpectedContentFailure(Exception exception)
    {
        return exception is ArgumentException ||
            exception is DirectoryNotFoundException ||
            exception is FileNotFoundException ||
            exception is InvalidDataException ||
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is NotSupportedException;
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

        if (_presentationFaulted)
        {
            SetUnavailable(string.IsNullOrWhiteSpace(_lastActionStatus)
                ? "RuntimeAdapter session unavailable. Return to menu to recover."
                : _lastActionStatus + " Return to menu to recover.");
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
        if (_presentationFaulted)
        {
            SetUnavailable(string.IsNullOrWhiteSpace(_lastActionStatus)
                ? "RuntimeAdapter session unavailable. Return to menu to recover."
                : _lastActionStatus + " Return to menu to recover.");
            return;
        }
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
            ? "READY"
            : _lastActionStatus;
        if (_view.RecoveryButton != null) _view.RecoveryButton.interactable = false;
        _view.MatchText.text = RuntimeBattlePanelPresentationModel.BuildMatchLine(snapshot);
        var opponent = RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, false);
        var own = RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, true);
        _view.OpponentText.text = RuntimeBattlePanelPresentationModel.BuildPlayerSection(opponent, false, _cardCatalog);
        _view.CastleText.text = BuildCastleCardText(snapshot, own, opponent);
        _view.OwnText.text = RuntimeBattlePanelPresentationModel.BuildPlayerSection(own, true, _cardCatalog);
        _view.PhaseText.text = RuntimeBattlePanelPresentationModel.BuildPhaseSummary(snapshot);
        RenderTable(snapshot, opponent, own);
        RenderActions(snapshot);
        _view.EventsText.text = RuntimeBattlePanelPresentationModel.BuildEvents(_adapter.Presentation.Events);
        RenderDebugOverlay(snapshot, own, opponent);
        // Snapshot/table/action rendering is complete before feedback consumes
        // the adapter event list. Feedback cannot delay or mutate gameplay.
        _actionFeedback?.Consume(_adapter.Presentation.Events);
        _renderedRevision = snapshot.SnapshotRevision;
        _renderedEventCount = _adapter.Presentation.Events.Count;
    }

    private void SetUnavailable(string message)
    {
        RecordDiagnostic(message);
        if (_statusText != null) _statusText.text = RuntimeBattlePanelPresentationModel.Unavailable;
        if (_view?.RecoveryButton != null) _view.RecoveryButton.interactable = true;
        ClearUnavailablePresentation();
        RenderDebugOverlay(null, null, null);
    }

    private void RecordDiagnostic(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (string.Equals(_lastDiagnostic, message, StringComparison.Ordinal)) return;

        _lastDiagnostic = message;
        if (IsDiagnosticsBuild)
            Debug.Log(message, this);
    }

    private void RenderDebugOverlay(
        RuntimeSnapshotEnvelope snapshot,
        RuntimePlayerSnapshot own,
        RuntimePlayerSnapshot opponent)
    {
        if (_view == null) return;

        var visible = DiagnosticsVisible;
        _view.SetDebugOverlayVisible(visible);
        if (!visible || _view.DebugOverlayText == null) return;

        var builder = new StringBuilder(512);
        if (!string.IsNullOrWhiteSpace(_lastDiagnostic))
            builder.Append("DIAGNOSTIC\n").Append(_lastDiagnostic).Append('\n');

        if (snapshot == null || _adapter == null)
        {
            if (builder.Length == 0) builder.Append("No snapshot.");
            _view.DebugOverlayText.text = builder.ToString();
            return;
        }

        builder.Append(RuntimeBattlePanelPresentationModel.BuildDebugMatchLine(snapshot)).Append('\n')
            .Append(RuntimeBattlePanelPresentationModel.BuildDebugPhaseSummary(snapshot)).Append('\n')
            .Append(RuntimeBattlePanelPresentationModel.BuildDebugPlayerSection(opponent, false)).Append('\n')
            .Append(RuntimeBattlePanelPresentationModel.BuildDebugPlayerSection(own, true)).Append('\n')
            .Append(RuntimeBattlePanelPresentationModel.BuildDebugEvents(_adapter.Presentation.Events));

        if (_actionGroups != null && _actionGroups.Count > 0)
        {
            builder.Append("\nACTIONS\n");
            foreach (var group in _actionGroups)
            {
                if (group == null || group.Actions == null) continue;
                foreach (var entry in group.Actions)
                {
                    if (entry == null) continue;
                    builder.Append(RuntimeBattlePanelActionModel.DescribeTechnical(
                        entry.LegalAction,
                        entry.State)).Append('\n');
                }
            }
        }

        _view.DebugOverlayText.text = builder.ToString();
    }

    private void ClearUnavailablePresentation()
    {
        ClearRootRaycastTarget();
        _selectedCardEntityId = null;
        _selectedCardInteraction = null;
        if (_view == null) return;
        ClearDynamicTablePresentation();
        if (_view.MatchText != null) _view.MatchText.text = "比赛状态：不可用";
        if (_view.OpponentText != null) _view.OpponentText.text = "对手状态：不可用";
        if (_view.CastleText != null) _view.CastleText.text = "共享王城：不可用";
        if (_view.OwnText != null) _view.OwnText.text = "己方状态：不可用";
        if (_view.PhaseText != null) _view.PhaseText.text = "阶段：不可用";
        if (_view.EventsText != null) _view.EventsText.text = "事件摘要：暂无事件";
        _actionFeedback?.Clear();
        RuntimeBattlePanelView.SetLeaderSlot(
            _view.OpponentLeaderRoot,
            string.Empty,
            string.Empty,
            string.Empty);
        RuntimeBattlePanelView.SetLeaderSlot(
            _view.OwnLeaderRoot,
            string.Empty,
            string.Empty,
            string.Empty);
        SetPileCount(_view.OpponentDeckRoot, null);
        SetPileCount(_view.OpponentGraveyardRoot, null);
        HideOptionalPile(_view.OpponentExileRoot);
        SetPileCount(_view.OwnDeckRoot, null);
        SetPileCount(_view.OwnGraveyardRoot, null);
        HideOptionalPile(_view.OwnExileRoot);
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
        ClearDynamicTablePresentation();
        BindLeaderSlot(_view.OpponentLeaderRoot, opponent);
        BindLeaderSlot(_view.OwnLeaderRoot, own);

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
            var visibleAmbushBacks = Math.Min(Math.Max(0, opponent.AmbushCount), 3);
            for (var index = 0; index < visibleAmbushBacks; index++)
            {
                var back = RuntimeBattlePanelView.CreateCardBack(
                    _view.OpponentAmbushCardsRoot,
                    "OpponentAmbushBack_" + index,
                    false);
                back.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            }
            SetPileCount(_view.OpponentDeckRoot, opponent.DeckCount);
            SetPileCount(_view.OpponentGraveyardRoot, opponent.GraveyardCount);
            HideOptionalPile(_view.OpponentExileRoot);
            SetQueueCount(_view.OpponentPhaseRoot, opponent.AmbushCount);
        }
        else
        {
            SetPileCount(_view.OpponentDeckRoot, null);
            SetPileCount(_view.OpponentGraveyardRoot, null);
            HideOptionalPile(_view.OpponentExileRoot);
            SetQueueCount(_view.OpponentPhaseRoot, null);
        }

        if (own != null)
        {
            RenderOwnCards(
                snapshot,
                own.Hand,
                _view.OwnHandRoot,
                cardRoots,
                RuntimeCardZone.OwnHand);
            RenderOwnCards(
                snapshot,
                own.Ambush,
                _view.OwnAmbushCardsRoot,
                cardRoots,
                RuntimeCardZone.OwnAmbush);
            RenderOwnCards(
                snapshot,
                own.Field,
                _view.OwnFieldRoot,
                cardRoots,
                RuntimeCardZone.OwnField);
            RenderOwnCards(
                snapshot,
                own.CommitQueue,
                _view.CommitCardsRoot,
                cardRoots,
                RuntimeCardZone.OwnCommitQueue);
            RenderOwnCards(
                snapshot,
                own.CloudStack,
                _view.CloudCardsRoot,
                cardRoots,
                RuntimeCardZone.OwnCloudStack);
            SetPileCount(_view.OwnDeckRoot, own.DeckCount);
            SetPileCount(_view.OwnGraveyardRoot, own.GraveyardCount);
            HideOptionalPile(_view.OwnExileRoot);
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
            HideOptionalPile(_view.OwnExileRoot);
            SetQueueCount(_view.OwnPhaseRoot, null);
            SetQueueCount(FindDirectChild(_view.CenterPilesRoot, "CenterCloudMirror"), null);
            SetQueueCount(FindDirectChild(_view.CenterPilesRoot, "CenterCommitMirror"), null);
            SetQueueCount(FindDirectChild(_view.MechanicalRoot, "CloudSlot"), null);
            SetQueueCount(FindDirectChild(_view.MechanicalRoot, "CommitQueueSlot"), null);
        }

        RuntimeBattlePanelView.FitCardStrip(_view.OpponentHandRoot);
        RuntimeBattlePanelView.FitCardStrip(_view.OpponentAmbushCardsRoot);
        RuntimeBattlePanelView.FitCardStrip(_view.OpponentFieldRoot);
        RuntimeBattlePanelView.FitCardStrip(_view.OwnFieldRoot);
        RuntimeBattlePanelView.FitCardStrip(_view.OwnHandRoot);
        RuntimeBattlePanelView.FitCardStrip(_view.OwnAmbushCardsRoot);
        RuntimeBattlePanelView.FitCardStrip(_view.CommitCardsRoot);
        RuntimeBattlePanelView.FitCardStrip(_view.CloudCardsRoot);

        // Every advertised target gets a visible drop surface. Card targets
        // use their own frame; known semantic roots and exact generic wire
        // surfaces handle non-card targets without borrowing the queue rail.
        RenderDropZones(snapshot, cardRoots);
    }

    private void ClearDynamicTablePresentation()
    {
        if (_view == null) return;

        // Dynamic card roots are about to be replaced. Any pinned card
        // selection would otherwise outlive its source entity and keep an old
        // action subset visible after a snapshot change (including a card
        // leaving the hand). The next click establishes a fresh selection.
        _selectedCardInteraction = null;
        _selectedCardEntityId = null;

        ClearChildren(_view.OpponentHandRoot);
        ClearChildren(_view.OpponentAmbushCardsRoot);
        ClearChildren(_view.OpponentFieldRoot);
        ClearDynamicLeaderCards(_view.OpponentLeaderRoot);
        ClearChildren(_view.OwnHandRoot);
        ClearChildren(_view.OwnAmbushCardsRoot);
        ClearChildren(_view.OwnFieldRoot);
        ClearChildren(_view.CommitCardsRoot);
        ClearChildren(_view.CloudCardsRoot);
        ClearDynamicLeaderCards(_view.OwnLeaderRoot);
        ClearChildren(_view.TargetZonesRoot);
        _view.HideCardInspect();
        ClearKnownSemanticDropZones();
        ClearRootRaycastTarget();
    }

    private static void ClearDynamicLeaderCards(RectTransform root)
    {
        if (root == null) return;
        for (var index = root.childCount - 1; index >= 0; index--)
        {
            var child = root.GetChild(index).gameObject;
            // LeaderTitle/Value/Status are the static slot labels. Any other
            // direct child is a dynamic leader card/art carrier from the
            // previous presentation snapshot and must not survive a clear.
            if (child.name == "LeaderTitle" ||
                child.name == "LeaderValue" ||
                child.name == "LeaderStatus")
                continue;

            if (Application.isPlaying)
            {
                child.SetActive(false);
                Destroy(child);
            }
            else DestroyImmediate(child);
        }
    }

    private void ClearKnownSemanticDropZones()
    {
        if (_view == null) return;

        // These roots are static structure but may carry a drop zone for the
        // previous snapshot. Clear both the component and its raycast state
        // before any new advertised target is mapped.
        ClearDropZones(_view.CastleRoot);
        ClearDropZones(_view.MechanicalRoot);
        ClearDropZones(_view.OpponentRoot);
        ClearDropZones(_view.OwnRoot);
        ClearDropZones(_view.OpponentLeaderRoot);
        ClearDropZones(_view.OwnLeaderRoot);
        ClearDropZones(_view.OpponentAmbushRoot);
        ClearDropZones(_view.OwnAmbushRoot);
    }

    private void BindLeaderSlot(
        RectTransform slot,
        RuntimePlayerSnapshot player)
    {
        RuntimeBattlePanelView.SetLeaderSlot(
            slot,
            BuildLeaderDisplayName(player),
            RuntimeBattlePanelPresentationModel.BuildLeaderLife(player),
            RuntimeBattlePanelPresentationModel.BuildLeaderStatus(player));
    }

    private string BuildLeaderDisplayName(RuntimePlayerSnapshot player)
    {
        // Leader identity is not a player-facing snapshot field. Keep the
        // stable generic label even when presentation card metadata is loaded;
        // the debug overlay remains the explicit identity inspection surface.
        return RuntimeBattlePanelPresentationModel.BuildLeaderName(player);
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
            var display = RuntimeCardDisplayModel.CreateVisible(
                card,
                viewer ? RuntimeCardZone.OwnField : RuntimeCardZone.OpponentField,
                _cardCatalog);
            var face = RuntimeCardFaceView.Build(
                parent,
                (viewer ? "OwnFieldCard_" : "OpponentFieldCard_") + index,
                RuntimeCardFaceMode.Compact);
            var art = ResolveCardArt(card);
            face.Bind(display, _contentResolver);
            face.SetDiagnosticsVisible(DiagnosticsVisible);
            face.SetInteractionState(
                sourceActions.Count > 0,
                targetHighlighted,
                viewer && sourceActions.Count > 0);
            var cardRoot = face.CardRoot;
            art = face.ArtImage.texture as Texture2D ?? art;
            ConfigureCardInspection(cardRoot, display, art, viewer);
            cardRoots.Add(new CardRootRef(card, cardRoot));
            ConfigureCardInteraction(cardRoot, sourceActions, targetHighlighted);
        }
    }

    private void RenderOwnCards(
        RuntimeSnapshotEnvelope snapshot,
        IReadOnlyList<RuntimeCardSnapshot> cards,
        RectTransform parent,
        List<CardRootRef> cardRoots,
        RuntimeCardZone zone)
    {
        if (cards == null) return;
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            if (card == null) continue;
            var sourceActions = FindSourceActions(snapshot, card.EntityId);
            var targetHighlighted = FindTargetActions(snapshot, card.EntityId).Count > 0;
            var display = RuntimeCardDisplayModel.CreateVisible(card, zone, _cardCatalog);
            var face = RuntimeCardFaceView.Build(
                parent,
                "OwnCard_" + index,
                RuntimeCardFaceMode.Compact);
            var art = ResolveCardArt(card);
            face.Bind(display, _contentResolver);
            face.SetDiagnosticsVisible(DiagnosticsVisible);
            face.SetInteractionState(
                sourceActions.Count > 0,
                targetHighlighted,
                sourceActions.Count > 0);
            var cardRoot = face.CardRoot;
            art = face.ArtImage.texture as Texture2D ?? art;
            ConfigureCardInspection(cardRoot, display, art, true);
            cardRoots.Add(new CardRootRef(card, cardRoot));
            ConfigureCardInteraction(cardRoot, sourceActions, targetHighlighted);
        }
    }

    private void ConfigureCardInspection(
        RectTransform cardRoot,
        RuntimeCardDisplayModel display,
        Texture2D art,
        bool allowActionSelection)
    {
        if (cardRoot == null || display == null || _view == null) return;
        var interaction = RuntimeCardInspectInteraction.Attach(
            cardRoot,
            RuntimeCardInspectModel.Build(display));
        interaction.InspectionRequested += (model, trigger) =>
        {
            // Hover is a transient reader only. A click pins the card and is
            // the explicit card selection that scopes the action rail.
            if (allowActionSelection && trigger == RuntimeCardInspectTrigger.Click)
                SelectCardForActions(interaction, model);
            _view.ShowCardInspect(model, art);
        };
        interaction.InspectionClosed += () => OnCardInspectionClosed(interaction);
    }

    private void SelectCardForActions(
        RuntimeCardInspectInteraction interaction,
        RuntimeCardInspectModel model)
    {
        if (interaction == null || model == null || model.Card == null) return;

        // Close the previously pinned card before showing the new one. The
        // close callback is identity-guarded, so it cannot clear the new
        // selection or hide the new reader after it is shown.
        var previous = _selectedCardInteraction;
        if (previous != null && !ReferenceEquals(previous, interaction))
        {
            _selectedCardInteraction = null;
            _selectedCardEntityId = null;
            previous.Close();
        }

        _selectedCardInteraction = interaction;
        // Entity ids are the only reliable bridge from a visible card to a
        // legal action source. Do not fall back to a card id, which could
        // identify multiple copies and would risk showing another card's
        // action.
        _selectedCardEntityId = model.Card.EntityId > 0
            ? model.Card.EntityId
            : (long?)null;
        RenderSelectedCardActions();
    }

    private void OnCardInspectionClosed(RuntimeCardInspectInteraction interaction)
    {
        if (ReferenceEquals(_selectedCardInteraction, interaction))
        {
            _selectedCardInteraction = null;
            _selectedCardEntityId = null;
            RenderSelectedCardActions();
        }
        _view?.HideCardInspect();
    }

    private void RenderSelectedCardActions()
    {
        if (!_visualTreeReady || _adapter == null) return;
        var snapshot = _adapter.Presentation.Snapshot;
        if (snapshot == null) return;
        RenderActions(snapshot);
        RenderDebugOverlay(
            snapshot,
            RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, true),
            RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, false));
    }

    private void ConfigureCardInteraction(
        RectTransform cardRoot,
        IReadOnlyList<RuntimeLegalAction> sourceActions,
        bool targetHighlighted)
    {
        var image = cardRoot.GetComponent<UnityEngine.UI.Image>();
        if (image == null) return;
        // Every face-up card remains raycastable for hover/click inspection.
        // Drag is still added only when the engine advertises a source action.
        image.raycastTarget = true;
        if (sourceActions.Count == 0) return;

        var drag = cardRoot.gameObject.AddComponent<RuntimeBattleCardDrag>();
        // RuntimeBattlePanel may own its Canvas directly. Resolve the direct
        // component first; relying only on GetComponentInParent here can
        // leave the drag source without a canvas identity, which would make a
        // safe release fallback unable to distinguish this panel from another
        // overlapping Canvas.
        var canvas = GetComponent<UnityEngine.Canvas>() ??
            GetComponentInParent<UnityEngine.Canvas>();
        drag.Configure(sourceActions, canvas, SubmitAdvertisedAction);

        // The card itself is now a drag source and inspect surface. Clicking
        // opens/pins details; the action rail remains the explicit accessible
        // click fallback, avoiding accidental plays while reading a card.
    }

    private void RenderDropZones(RuntimeSnapshotEnvelope snapshot, IReadOnlyList<CardRootRef> cardRoots)
    {
        for (var index = 0; index < cardRoots.Count; index++)
            ClearDropZones(cardRoots[index].Root);

        if (snapshot.LegalActions == null) return;

        // Allocate unknown wire targets in a deterministic order before
        // creating their surfaces. This keeps a target's presentation root
        // stable across equivalent snapshots without using the mechanical
        // queue as a catch-all drop target.
        var genericTargetKeys = new List<string>();
        for (var index = 0; index < snapshot.LegalActions.Count; index++)
        {
            var legal = snapshot.LegalActions[index];
            if (legal == null || legal.TargetId == null) continue;
            if (FindCardTargetRoot(cardRoots, legal.TargetId) != null) continue;
            if (ResolveKnownTargetRoot(snapshot, legal.TargetId) != null) continue;
            var key = TargetKey(legal.TargetId);
            if (!genericTargetKeys.Contains(key)) genericTargetKeys.Add(key);
        }
        genericTargetKeys.Sort(StringComparer.Ordinal);

        var genericRoots = new Dictionary<string, RectTransform>(StringComparer.Ordinal);
        for (var index = 0; index < genericTargetKeys.Count; index++)
        {
            var key = genericTargetKeys[index];
            genericRoots[key] = CreateGenericTargetRoot(
                key,
                index,
                genericTargetKeys.Count);
        }

        for (var index = 0; index < snapshot.LegalActions.Count; index++)
        {
            var legal = snapshot.LegalActions[index];
            if (legal == null) continue;
            if (legal.TargetId == null)
            {
                // Some advertised card actions intentionally have no wire
                // target. Their semantic zone carries the exact advertised
                // action id/type and submits that unchanged action with a null
                // target; the UI never manufactures a gameplay target.
                RectTransform semanticSurface = null;
                if (string.Equals(legal.Type, "PLAY_CARD", StringComparison.Ordinal))
                    semanticSurface = _view.OwnFieldRoot;
                else if (string.Equals(legal.Type, "SET_AMBUSH", StringComparison.Ordinal))
                    semanticSurface = _view.OwnAmbushRoot;
                else if (string.Equals(legal.Type, "COMMIT", StringComparison.Ordinal))
                    semanticSurface = _view.CommitCardsRoot;
                else if (string.Equals(legal.Type, "ROLLBACK", StringComparison.Ordinal))
                    semanticSurface = _view.OwnHandRoot;

                if (semanticSurface != null && legal.SourceId != null)
                {
                    AddDropZone(
                        semanticSurface,
                        legal.TargetId,
                        legal.ActionId,
                        legal.Type);
                }
                continue;
            }
            var targetRoot = FindCardTargetRoot(cardRoots, legal.TargetId) ??
                ResolveKnownTargetRoot(snapshot, legal.TargetId);
            if (targetRoot == null)
                genericRoots.TryGetValue(TargetKey(legal.TargetId), out targetRoot);
            if (targetRoot == null) continue;
            AddDropZone(targetRoot, legal.TargetId);
            if (targetRoot == _view.CastleRoot ||
                targetRoot == _view.OpponentRoot ||
                targetRoot == _view.OwnRoot)
                EnsureDropCue(targetRoot, snapshot);
        }
    }

    private void EnsureDropCue(RectTransform root, RuntimeSnapshotEnvelope snapshot)
    {
        if (root == null) return;

        var cue = root.Find("LegalDropCue") as RectTransform;
        var text = cue == null ? null : cue.GetComponent<UnityEngine.UI.Text>();
        if (text == null)
        {
            text = RuntimeBattlePanelView.CreateText(
                root,
                "LegalDropCue",
                10,
                new Color(0.98f, 0.76f, 0.30f, 1f));
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            cue = text.rectTransform;
            if (root == _view.CastleRoot)
                RuntimeBattlePanelView.SetAnchors(cue, new Vector2(0.04f, 0.14f), new Vector2(0.96f, 0.23f));
            else if (root == _view.OpponentRoot || root == _view.OwnRoot)
                RuntimeBattlePanelView.SetAnchors(cue, new Vector2(0.012f, 0.02f), new Vector2(0.29f, 0.15f));
            else
                RuntimeBattlePanelView.SetAnchors(cue, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.18f));
        }

        var targets = root.GetComponents<RuntimeBattleDropZone>();
        var labels = new List<string>();
        foreach (var zone in targets)
        {
            if (zone == null || !zone.enabled) continue;
            var label = DisplayDropTarget(zone.TargetId, snapshot);
            if (!labels.Contains(label)) labels.Add(label);
        }
        text.text = labels.Count == 0
            ? string.Empty
            : "DROP HERE\n" + string.Join(" / ", labels);
        text.gameObject.SetActive(labels.Count > 0);
    }

    private static string DisplayDropTarget(
        object targetId,
        RuntimeSnapshotEnvelope snapshot)
    {
        return RuntimeBattlePanelActionModel.DescribeTarget(
            targetId,
            snapshot,
            null,
            snapshot == null ? null : snapshot.CurrentPlayer) ?? "Target";
    }

    private RectTransform CreateGenericTargetRoot(string targetKey, int index, int total)
    {
        var columns = 2;
        var column = index % columns;
        var row = index / columns;
        var rows = Mathf.Max(1, Mathf.CeilToInt(total / (float)columns));
        // Keep generic semantic targets in the center band. Own/opponent
        // field and hand strips live in the upper/lower lanes, while the
        // side columns avoid the castle card itself. The surface remains
        // raycastable, but can no longer steal card drag/click hits.
        const float safeMinY = 0.37f;
        const float safeMaxY = 0.63f;
        var rowHeight = (safeMaxY - safeMinY) / rows;
        var minX = column == 0 ? 0.02f : 0.78f;
        var maxX = column == 0 ? 0.22f : 0.98f;
        var minY = safeMinY + rowHeight * row;
        var maxY = Mathf.Min(safeMaxY, minY + rowHeight - 0.02f);
        return RuntimeBattlePanelView.CreateLegalTargetSurface(
            _view.TargetZonesRoot,
            "LegalTarget_" + SanitizeName(targetKey),
            RuntimeBattlePanelActionModel.DescribeTarget(targetKey) ?? "Target",
            new Vector2(minX, minY),
            new Vector2(maxX, Mathf.Max(minY + 0.03f, maxY)));
    }

    private static RectTransform FindCardTargetRoot(
        IReadOnlyList<CardRootRef> cardRoots,
        object targetId)
    {
        for (var cardIndex = 0; cardIndex < cardRoots.Count; cardIndex++)
        {
            if (RuntimeBattlePanelActionModel.WireValuesEqual(
                targetId,
                cardRoots[cardIndex].Card.EntityId))
                return cardRoots[cardIndex].Root;
        }
        return null;
    }

    private RectTransform ResolveKnownTargetRoot(
        RuntimeSnapshotEnvelope snapshot,
        object targetId)
    {
        if (IsCastleTarget(targetId)) return _view.CastleRoot;
        var targetText = targetId as string;
        if (string.IsNullOrWhiteSpace(targetText) || snapshot.Players == null)
            return null;

        var viewer = RuntimeBattlePanelPresentationModel.FindPlayer(snapshot, true);
        for (var index = 0; index < snapshot.Players.Count; index++)
        {
            var player = snapshot.Players[index];
            if (player == null || string.IsNullOrWhiteSpace(player.PlayerId)) continue;
            var isViewer = ReferenceEquals(player, viewer);
            if (string.Equals(targetText, player.PlayerId, StringComparison.Ordinal) ||
                IsPlayerLifeTarget(targetText, player.PlayerId))
                return isViewer ? _view.OwnRoot : _view.OpponentRoot;

            if (TryGetLeaderPlayerId(targetText, out var leaderPlayerId) &&
                string.Equals(leaderPlayerId, player.PlayerId, StringComparison.Ordinal))
                return isViewer ? _view.OwnLeaderRoot : _view.OpponentLeaderRoot;
        }
        return null;
    }

    private static bool TryGetLeaderPlayerId(string target, out string playerId)
    {
        const string leaderPrefix = "leader_";
        if (target.StartsWith(leaderPrefix, StringComparison.Ordinal) && target.Length > leaderPrefix.Length)
        {
            playerId = "player_" + target.Substring(leaderPrefix.Length);
            return true;
        }

        const string corePrefix = "core:player_";
        const string coreSuffix = ":leader";
        if (target.StartsWith(corePrefix, StringComparison.Ordinal) &&
            target.EndsWith(coreSuffix, StringComparison.Ordinal) &&
            target.Length > corePrefix.Length + coreSuffix.Length)
        {
            playerId = target.Substring(5, target.Length - 5 - coreSuffix.Length);
            return true;
        }

        playerId = null;
        return false;
    }

    private static bool IsPlayerLifeTarget(string target, string playerId)
    {
        const string prefix = "core:";
        const string suffix = ":life";
        return target.Length > prefix.Length + suffix.Length &&
            target.StartsWith(prefix, StringComparison.Ordinal) &&
            target.EndsWith(suffix, StringComparison.Ordinal) &&
            string.Equals(
                target.Substring(prefix.Length, target.Length - prefix.Length - suffix.Length),
                playerId,
                StringComparison.Ordinal);
    }

    private static string TargetKey(object targetId)
    {
        return RuntimeBattlePanelActionModel.FormatWireValue(targetId);
    }

    private static void AddDropZone(RectTransform root, object targetId)
    {
        AddDropZone(root, targetId, null, null);
    }

    private static void AddDropZone(
        RectTransform root,
        object targetId,
        string actionId,
        string actionType)
    {
        if (root == null) return;
        var image = root.GetComponent<UnityEngine.UI.Image>();
        if (image == null)
        {
            image = root.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = Color.clear;
        }
        image.raycastTarget = true;
        var zones = root.GetComponents<RuntimeBattleDropZone>();
        for (var index = 0; index < zones.Length; index++)
        {
            if (zones[index].enabled &&
                RuntimeBattlePanelActionModel.WireValuesEqual(zones[index].TargetId, targetId) &&
                string.Equals(zones[index].ActionId, actionId, StringComparison.Ordinal) &&
                string.Equals(zones[index].ActionType, actionType, StringComparison.Ordinal))
                return;
        }
        var zone = root.gameObject.AddComponent<RuntimeBattleDropZone>();
        zone.Configure(targetId, actionId, actionType);
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

        var image = root.GetComponent<UnityEngine.UI.Image>();
        if (image != null)
        {
            image.raycastTarget =
                root.GetComponent<RuntimeBattleCardDrag>() != null ||
                root.GetComponent<RuntimeCardInspectInteraction>() != null;
        }

        var cue = root.Find("LegalDropCue") as RectTransform;
        if (cue != null)
        {
            var cueText = cue.GetComponent<UnityEngine.UI.Text>();
            if (cueText != null) cueText.text = string.Empty;
            cue.gameObject.SetActive(false);
        }
    }

    private void ClearRootRaycastTarget()
    {
        var rootImage = GetComponent<UnityEngine.UI.Image>();
        if (rootImage != null) rootImage.raycastTarget = false;

        if (_view == null) return;
        var contentImage = _view.ContentRoot == null
            ? null
            : _view.ContentRoot.GetComponent<UnityEngine.UI.Image>();
        if (contentImage != null) contentImage.raycastTarget = false;
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
            string.Equals(text, "core:shared_castle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "core", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "kingdom_core", StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayCardName(RuntimeCardSnapshot card)
    {
        return card == null || string.IsNullOrWhiteSpace(card.CardId) ? "—" : card.CardId;
    }

    private static string FormatCardNumber(int? value)
    {
        return value.HasValue ? value.Value.ToString() : "—";
    }

    private Texture2D ResolveCardArt(RuntimeCardSnapshot card)
    {
        var requestedId = card == null ? string.Empty : card.CardId;
        if (_cardCatalog != null && !string.IsNullOrWhiteSpace(requestedId) &&
            _cardCatalog.TryGetArtId(requestedId, out var artId))
        {
            requestedId = artId;
        }

        // The resolver owns compatibility aliases (including legacy leader
        // IDs); the panel never guesses a file path or adds a second mapping.
        if (_contentResolver == null)
        {
            RuntimeContentResolver.TryLoad(string.Empty, out _contentResolver, false);
            _ownsContentResolver = _contentResolver != null;
        }
        return _contentResolver.GetCardArt(requestedId);
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
        if (text != null)
            text.text = value.HasValue
                ? value.Value.ToString()
                : RuntimeBattlePanelPresentationModel.Unavailable;
    }

    private static void HideOptionalPile(RectTransform root)
    {
        if (root == null) return;
        root.gameObject.SetActive(false);
        var count = FindChildText(root, "PileCount");
        if (count != null) count.text = string.Empty;
        var summary = FindChildText(root, "PileSummary");
        if (summary != null) summary.text = string.Empty;
    }

    private static void SetPileSummary(RectTransform root, string value)
    {
        if (root == null) return;
        var text = FindChildText(root, "PileSummary");
        if (text != null)
            text.text = string.IsNullOrWhiteSpace(value)
                ? RuntimeBattlePanelPresentationModel.Unavailable
                : value;
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
        return BuildCastleCardText(snapshot, null, null);
    }

    private static string BuildCastleCardText(
        RuntimeSnapshotEnvelope snapshot,
        RuntimePlayerSnapshot own,
        RuntimePlayerSnapshot opponent)
    {
        if (snapshot == null || snapshot.Castle == null)
            return "LIFE  Unavailable\nTURN  Unavailable  ·  WIN COUNT OWN  " +
                CycleWinCount(own) + " / OPPONENT  " + CycleWinCount(opponent);
        var life = snapshot.Castle.Enabled
            ? snapshot.Castle.Health.ToString()
            : RuntimeBattlePanelPresentationModel.Unavailable;
        var turn = snapshot.Turn.ToString();
        return "LIFE  " + life +
            "\nTURN  " + turn + "  ·  WIN COUNT OWN  " + CycleWinCount(own) +
            " / OPPONENT  " + CycleWinCount(opponent);
    }

    private static string CycleWinCount(RuntimePlayerSnapshot player)
    {
        return player == null
            ? RuntimeBattlePanelPresentationModel.Unavailable
            : player.CycleWinCount.ToString();
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
        _actionGroups = RuntimeBattlePanelActionModel.BuildActionGroups(
            snapshot,
            _selectedCardEntityId);
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
                CreateActionButton(group.Actions[0], _view.ActionsRoot, snapshot);
            else
                CreateActionGroup(group, _view.ActionsRoot, snapshot);
        }
    }

    private void CreateActionButton(
        RuntimeBattlePanelActionEntry entry,
        RectTransform parent,
        RuntimeSnapshotEnvelope snapshot)
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
        label.text = RuntimeBattlePanelActionModel.Describe(
            legal,
            entry.State,
            _cardCatalog,
            snapshot,
            viewerPlayerIndex);
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
        RectTransform parent,
        RuntimeSnapshotEnvelope snapshot)
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

        // A targeted card action is already a complete, authoritative wire
        // action when each advertised variant has both source and target. The
        // compact action rail cannot show a heading, four toggles, and a
        // trailing submit button at once. Render those exact variants as
        // direct buttons so the first legal choice remains visible and
        // clickable; no target or source is inferred by the UI.
        if (ShouldRenderDirectTargetedVariants(group))
        {
            groupElement.preferredHeight =
                (group.Actions.Count * 44f) + Mathf.Max(0, group.Actions.Count - 1) * 3f;
            groupElement.minHeight = groupElement.preferredHeight;
            foreach (var entry in group.Actions)
                CreateActionButton(entry, groupObject, snapshot);
            return;
        }

        var heading = RuntimeBattlePanelView.CreateText(
            groupObject,
            "SelectionLabel",
            18,
            new Color(0.78f, 0.86f, 0.96f));
        heading.text = BuildActionGroupLabel(group, snapshot);
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
            CreateActionChoice(group, entry, optionsObject, toggleGroup, snapshot);

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
        submitLabel.text = "Confirm  " + RuntimeBattlePanelActionModel.Describe(
            group.SelectedAction.LegalAction,
            group.SelectedAction.State,
            _cardCatalog,
            snapshot,
            viewerPlayerIndex);
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

    private void CreateActionChoice(
        RuntimeBattlePanelActionGroup group,
        RuntimeBattlePanelActionEntry entry,
        RectTransform parent,
        UnityEngine.UI.ToggleGroup toggleGroup,
        RuntimeSnapshotEnvelope snapshot)
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
        label.text = RuntimeBattlePanelActionModel.Describe(
            legal,
            entry.State,
            _cardCatalog,
            snapshot,
            viewerPlayerIndex);
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

    private string BuildActionGroupLabel(
        RuntimeBattlePanelActionGroup group,
        RuntimeSnapshotEnvelope snapshot)
    {
        var first = group.Actions[0].LegalAction;
        var state = group.Actions[0].State;
        var label = RuntimeBattlePanelActionModel.Describe(
            first,
            state,
            _cardCatalog,
            snapshot,
            viewerPlayerIndex);
        if (group.RequiresSourceSelection) label += "\nChoose a card";
        if (group.RequiresTargetSelection) label += "\nChoose a target";
        return label;
    }

    private static bool ShouldRenderDirectTargetedVariants(RuntimeBattlePanelActionGroup group)
    {
        if (group == null || group.Actions.Count <= 1) return false;
        var first = group.Actions[0].LegalAction;
        if (!string.Equals(first.Type, "PLAY_CARD", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var entry in group.Actions)
        {
            var legal = entry.LegalAction;
            if (legal == null || legal.SourceId == null || legal.TargetId == null)
                return false;
        }
        return true;
    }

    private static Color ActionAccent(string actionType)
    {
        var type = actionType ?? string.Empty;
        if (type.IndexOf("PLAY", StringComparison.OrdinalIgnoreCase) >= 0) return Hex("245979");
        if (type.IndexOf("ATTACK", StringComparison.OrdinalIgnoreCase) >= 0) return Hex("713B48");
        if (type.IndexOf("COMMIT", StringComparison.OrdinalIgnoreCase) >= 0) return Hex("27636A");
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
            RecordDiagnostic("Action unavailable: RuntimeAdapter is not ready.");
            _lastActionStatus = RuntimeBattlePanelPresentationModel.Unavailable;
            Render();
            return;
        }
        var snapshot = _adapter.Presentation.Snapshot;
        if (snapshot is null)
        {
            RecordDiagnostic("Action unavailable: snapshot is not ready.");
            _lastActionStatus = RuntimeBattlePanelPresentationModel.Unavailable;
            Render();
            return;
        }

        try
        {
            var action = RuntimeBattlePanelActionModel.ToGameAction(legal, snapshot.MatchId);
            var validation = RuntimeActionBoundary.Validate(action, snapshot);
            if (!validation.Accepted)
            {
                RecordDiagnostic(
                    "Action rejected: " + validation.ReasonKey +
                    " | " + RuntimeBattlePanelActionModel.DescribeTechnical(
                        legal,
                        RuntimeBattlePanelActionModel.Evaluate(
                        legal,
                        snapshot.PendingPrompt)));
                _lastActionStatus = "Action rejected";
                RefreshAfterActionFailure();
                return;
            }

            var submission = _adapter.Submit(action);
            // Preserve the most important public cue before a hot-seat viewer
            // switch clears viewer-scoped event history. This does not retain
            // raw event payloads or expose the previous viewer's snapshot.
            _actionFeedback?.Consume(_adapter.Presentation.EventDelta);
            if (submission.Result.Accepted)
            {
                // A successful action invalidates the selected card/action
                // pairing. The next snapshot is authoritative and will
                // rebuild the card surfaces without a stale selection.
                _selectedCardInteraction = null;
                _selectedCardEntityId = null;
            }
            _boundViewerPlayerIndex = -1;
            var refreshed = TryRefreshViewerSnapshot();
            RecordDiagnostic(
                "Action " + (submission.Result.Accepted ? "accepted" : "rejected") +
                ": " + RuntimeBattlePanelActionModel.DescribeTechnical(
                    legal,
                    RuntimeBattlePanelActionModel.Evaluate(
                        legal,
                        snapshot.PendingPrompt)) +
                " | result=" + submission.Result.ReasonKey);
            _lastActionStatus = submission.Result.Accepted
                ? "Action accepted"
                : "Action rejected";
            if (!refreshed)
                RecordDiagnostic("Action result was received, but the next snapshot could not be refreshed.");
            Render();
        }
        catch (Exception exception)
        {
            RecordDiagnostic("Action submission failed: " + exception);
            _lastActionStatus = RuntimeBattlePanelPresentationModel.Unavailable;
            RefreshAfterActionFailure();
        }
    }

    private void RefreshAfterActionFailure()
    {
        _boundViewerPlayerIndex = -1;
        var refreshed = TryRefreshViewerSnapshot();
        if (!refreshed)
            RecordDiagnostic("Snapshot refresh after the action did not complete.");
        Render();
    }

    private void ClearActions()
    {
        if (_view is null || _view.ActionsRoot is null) return;
        for (var index = _view.ActionsRoot.childCount - 1; index >= 0; index--)
        {
            var child = _view.ActionsRoot.GetChild(index).gameObject;
            DisableActionInteraction(child);
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }

    private static void DisableActionInteraction(GameObject root)
    {
        var selectables = root.GetComponentsInChildren<UnityEngine.UI.Selectable>(true);
        for (var index = 0; index < selectables.Length; index++)
            selectables[index].interactable = false;

        var graphics = root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
        for (var index = 0; index < graphics.Length; index++)
            graphics[index].raycastTarget = false;

        root.SetActive(false);
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
        _actionFeedback?.Clear();
        _actionFeedback = null;
        // A shared resolver is borrowed from RuntimeBootstrap. Releasing the
        // lease is intentionally a no-op; only an owned legacy/manual
        // resolver may have its texture cache cleared here.
        ReleasePresentationContent();

        if (_createdEventSystem == null) return;
        if (Application.isPlaying) Destroy(_createdEventSystem.gameObject);
        else DestroyImmediate(_createdEventSystem.gameObject);
        _createdEventSystem = null;
    }

    private void OnDisable()
    {
        // There is no coroutine to stop; clearing the active state also
        // prevents a late frame from leaving a pulse on a hidden panel.
        _actionFeedback?.Clear();
    }
}
}
