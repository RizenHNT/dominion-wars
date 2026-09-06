using System;
using DominionWars.Adapters;
using DominionWars.Unity.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DominionWars.Unity.UI
{

/// <summary>
/// Single-scene presentation router. It owns neutral non-battle shells and
/// reuses the existing RuntimeBattlePanel for Battle; it does not own engine
/// rules or victory inference. Match construction is delegated to the
/// RuntimeMatchSetupOrchestrator boundary.
/// </summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class RuntimeScreenFlow : MonoBehaviour
{
    public const string DefaultObjectName = "DominionWarsRuntimeScreenFlow";
    public const string ReadyLogMessage =
        "Dominion Wars runtime screen flow ready: TITLE shell active.";

    private UnityEngine.EventSystems.EventSystem _createdEventSystem;
    private RuntimeScreenShellView _view;
    private RuntimeBattlePanel _battlePanel;
    private RuntimeBootstrap _bootstrap;
    private RuntimeMatchSetupOrchestrator _matchSetupOrchestrator;
    private RuntimeBattlePanel _wiredRecoveryPanel;
    private RuntimeAdapter _observedAdapter;
    private long _observedSnapshotRevision = -1;
    private string _selectedPlayer0DeckId = string.Empty;
    private string _selectedPlayer1DeckId = string.Empty;
    private bool _initialized;
    private bool _readyLogIssued;

    public RuntimeScreenId CurrentScreen { get; private set; } = RuntimeScreenId.Boot;
    public RuntimeScreenShellView View => _view;
    public RuntimeBattlePanel BattlePanel => _battlePanel;
    public RuntimeBootstrap Bootstrap => _bootstrap;
    public RuntimeMatchSetupOrchestrator MatchSetupOrchestrator => _matchSetupOrchestrator;
    public string SelectedPlayer0DeckId => _selectedPlayer0DeckId;
    public string SelectedPlayer1DeckId => _selectedPlayer1DeckId;
    public bool IsPresentationReady =>
        _initialized &&
        _view != null &&
        CurrentScreen == RuntimeScreenId.Title &&
        _view.TitleRoot != null &&
        _view.TitleRoot.gameObject.activeSelf &&
        _view.VisibleShellRootCount == 1;
    public event Action<RuntimeScreenIntent> IntentRaised;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureRuntimeInstance()
    {
        if (UnityEngine.Object.FindFirstObjectByType<RuntimeScreenFlow>() != null) return;

        var flowObject = new GameObject(
            DefaultObjectName,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler),
            typeof(UnityEngine.UI.GraphicRaycaster));
        DontDestroyOnLoad(flowObject);
        flowObject.AddComponent<RuntimeScreenFlow>();
    }

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Start()
    {
        // Leave one frame for the explicit Boot state, then present Title.
        if (CurrentScreen == RuntimeScreenId.Boot)
            Navigate(RuntimeScreenId.Title);
        LogReadyIfPresentationIsVisible();
        RuntimePlayerVisualSmoke.TryStartFromCommandLine();
    }

    private void LateUpdate()
    {
        if (_initialized)
        {
            EnsureEventSystem();
            Refresh();
            LogReadyIfPresentationIsVisible();
        }
    }

    public void Initialize()
    {
        if (_initialized) return;

        ConfigureCanvas();
        EnsureEventSystem();
        _view = RuntimeScreenShellView.Build(GetComponent<RectTransform>());
        WireControls();
        _initialized = true;
        CurrentScreen = RuntimeScreenId.Boot;
        ApplyScreen();
    }

    public void Navigate(RuntimeScreenId screen)
    {
        Initialize();
        CurrentScreen = screen;
        ApplyScreen();
    }

    public void ShowBattle()
    {
        Navigate(RuntimeScreenId.Battle);
    }

    public void ShowResult()
    {
        Initialize();
        _view.ClearResultOutcome();
        Navigate(RuntimeScreenId.Result);
    }

    public void RequestStartMatch()
    {
        RaiseIntent(RuntimeScreenIntent.StartMatch);
        DiscoverRuntimeHost();
        if (_matchSetupOrchestrator == null)
        {
            StayOnMatchSetupWithError("Match setup is unavailable.");
            return;
        }

        if (!_matchSetupOrchestrator.TryStartMatch(
                _selectedPlayer0DeckId,
                _selectedPlayer1DeckId,
                out var reasonKey))
        {
            StayOnMatchSetupWithError(ToSafeSetupError(reasonKey));
            return;
        }

        if (_bootstrap == null || _bootstrap.Adapter == null ||
            _bootstrap.Adapter.Presentation.Snapshot == null)
        {
            StayOnMatchSetupWithError("Match snapshot is unavailable.");
            return;
        }

        if (_battlePanel == null)
            _battlePanel = FindRuntimeBattlePanel();
        WireBattleRecovery();
        if (_battlePanel != null)
            _battlePanel.Bind(_bootstrap);
        _view.SetMatchSetupError(string.Empty);
        Navigate(RuntimeScreenId.Battle);
    }

    public void RequestBack()
    {
        RaiseIntent(RuntimeScreenIntent.Back);
        Navigate(BackTarget(CurrentScreen));
    }

    public void RequestRestart()
    {
        RaiseIntent(RuntimeScreenIntent.Restart);

        // A terminal adapter must never remain reachable through the panel
        // while the replacement is being built. Unbind alone is not enough:
        // an automatic panel can immediately rediscover the still-live old
        // bootstrap adapter. Stop it first, then let the new adapter own a
        // completely fresh presentation/event history.
        DiscoverRuntimeHost();
        if (_battlePanel == null)
            _battlePanel = FindRuntimeBattlePanel();
        StopRuntimeSession();
        if (_battlePanel != null)
            _battlePanel.Unbind();

        ResetObservedSnapshot();
        _view.ClearResultOutcome();
        Navigate(RuntimeScreenId.BattleLoading);

        if (_bootstrap == null || _matchSetupOrchestrator == null)
        {
            StayOnMatchSetupWithError("Match setup is unavailable.");
            return;
        }

        // These IDs are the last successfully started setup selection. The
        // bootstrap values are the safe fallback for sessions that started
        // outside this flow (for example, the legacy scene smoke path).
        var player0DeckId = string.IsNullOrWhiteSpace(_selectedPlayer0DeckId)
            ? _bootstrap.Player0DeckId
            : _selectedPlayer0DeckId;
        var player1DeckId = string.IsNullOrWhiteSpace(_selectedPlayer1DeckId)
            ? _bootstrap.Player1DeckId
            : _selectedPlayer1DeckId;

        if (!_matchSetupOrchestrator.TryStartMatch(
                player0DeckId,
                player1DeckId,
                out var reasonKey))
        {
            StayOnMatchSetupWithError(ToSafeSetupError(reasonKey));
            return;
        }

        if (_bootstrap.Adapter == null ||
            _bootstrap.Adapter.Presentation.Snapshot == null)
        {
            StayOnMatchSetupWithError("Match snapshot is unavailable.");
            return;
        }

        if (_battlePanel == null)
            _battlePanel = FindRuntimeBattlePanel();
        if (_battlePanel == null && Application.isPlaying)
            _battlePanel = RuntimeBattlePanel.EnsureRuntimeInstanceForScene();
        WireBattleRecovery();
        if (_battlePanel != null)
            _battlePanel.Bind(_bootstrap);

        _view.SetMatchSetupError(string.Empty);
        // The previous OVER snapshot has already been discarded. Resetting
        // the observation cursor also makes the first new-session refresh
        // incapable of replaying the old result screen.
        ResetObservedSnapshot();
        Navigate(RuntimeScreenId.Battle);
    }

    public void RequestReturnToMenu()
    {
        RaiseIntent(RuntimeScreenIntent.ReturnToMenu);
        // Stop the authoritative session first. RuntimeBattlePanel.Unbind then
        // clears its projected state without reattaching a live adapter.
        StopRuntimeSession();
        if (_battlePanel == null)
            _battlePanel = FindRuntimeBattlePanel();
        if (_battlePanel != null)
        {
            _battlePanel.Unbind();
            _battlePanel.gameObject.SetActive(false);
        }
        _view.ClearResultOutcome();
        ResetObservedSnapshot();
        Navigate(RuntimeScreenId.MainMenu);
    }

    private void StopRuntimeSession()
    {
        var runtimeBootstrap = _bootstrap;
        if (runtimeBootstrap == null)
            runtimeBootstrap = UnityEngine.Object.FindFirstObjectByType<RuntimeBootstrap>();
        runtimeBootstrap?.StopSession();
    }

    public void Refresh()
    {
        Initialize();
        DiscoverRuntimeHost();
        if (_battlePanel == null)
        {
            _battlePanel = FindRuntimeBattlePanel();
            if (_battlePanel == null && Application.isPlaying)
                _battlePanel = RuntimeBattlePanel.EnsureRuntimeInstanceForScene();
        }
        WireBattleRecovery();
        RefreshMatchSetupOptions();
        ObserveAuthoritativeResult();
        ApplyScreen();
    }

    private void ConfigureCanvas()
    {
        var root = GetComponent<RectTransform>();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        var canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        var scaler = GetComponent<UnityEngine.UI.CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
            gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
    }

    private void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null)
            return;

        var eventSystemObject = new GameObject("DominionWarsRuntimeEventSystem");
        _createdEventSystem = eventSystemObject.AddComponent<UnityEngine.EventSystems.EventSystem>();
        eventSystemObject.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }

    private void WireControls()
    {
        _view.BootContinueButton.onClick.AddListener(() => Navigate(RuntimeScreenId.Title));
        _view.TitleContinueButton.onClick.AddListener(() => Navigate(RuntimeScreenId.MainMenu));
        _view.MainMenuMatchSetupButton.onClick.AddListener(() => Navigate(RuntimeScreenId.MatchSetup));
        _view.MainMenuBackButton.onClick.AddListener(RequestBack);
        _view.MatchSetupStartButton.onClick.AddListener(RequestStartMatch);
        _view.MatchSetupBackButton.onClick.AddListener(RequestBack);
        _view.BattleLoadingBackButton.onClick.AddListener(RequestBack);
        _view.ResultRestartButton.onClick.AddListener(RequestRestart);
        _view.ResultReturnToMenuButton.onClick.AddListener(RequestReturnToMenu);
    }

    private void WireBattleRecovery()
    {
        if (_battlePanel == null || ReferenceEquals(_wiredRecoveryPanel, _battlePanel)) return;
        if (_wiredRecoveryPanel != null)
            _wiredRecoveryPanel.RecoveryRequested -= HandleBattleRecovery;
        _battlePanel.RecoveryRequested += HandleBattleRecovery;
        _wiredRecoveryPanel = _battlePanel;
    }

    private void HandleBattleRecovery()
    {
        RequestReturnToMenu();
    }

    private void ApplyScreen()
    {
        if (!_initialized || _view == null) return;

        if (_battlePanel == null)
            _battlePanel = FindRuntimeBattlePanel();

        var isBattle = CurrentScreen == RuntimeScreenId.Battle;
        if (_battlePanel != null)
            _battlePanel.gameObject.SetActive(isBattle);

        _view.SetVisibleScreen(CurrentScreen, isBattle && _battlePanel != null);
    }

    private void DiscoverRuntimeHost()
    {
        var nextBootstrap = UnityEngine.Object.FindFirstObjectByType<RuntimeBootstrap>();
        if (ReferenceEquals(_bootstrap, nextBootstrap)) return;

        _bootstrap = nextBootstrap;
        _matchSetupOrchestrator = _bootstrap == null
            ? null
            : new RuntimeMatchSetupOrchestrator(_bootstrap);
        if (_bootstrap == null)
        {
            _selectedPlayer0DeckId = string.Empty;
            _selectedPlayer1DeckId = string.Empty;
            return;
        }

        _selectedPlayer0DeckId = _bootstrap.Player0DeckId;
        _selectedPlayer1DeckId = _bootstrap.Player1DeckId;
    }

    private void RefreshMatchSetupOptions()
    {
        if (_view == null) return;
        if (_matchSetupOrchestrator == null)
        {
            _view.SetMatchSetupDeckOptions(
                Array.Empty<RuntimeDeckOption>(),
                _selectedPlayer0DeckId,
                _selectedPlayer1DeckId,
                HandleDeckSelected);
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(_selectedPlayer0DeckId))
                _selectedPlayer0DeckId = _bootstrap.Player0DeckId;
            if (string.IsNullOrWhiteSpace(_selectedPlayer1DeckId))
                _selectedPlayer1DeckId = _bootstrap.Player1DeckId;
            _view.SetMatchSetupDeckOptions(
                _matchSetupOrchestrator.DeckOptions,
                _selectedPlayer0DeckId,
                _selectedPlayer1DeckId,
                HandleDeckSelected);
        }
        catch (Exception)
        {
            // Only a safe presentation message reaches the shell. The source
            // loader remains the authority and its details stay out of UI.
            _view.SetMatchSetupDeckOptions(
                Array.Empty<RuntimeDeckOption>(),
                _selectedPlayer0DeckId,
                _selectedPlayer1DeckId,
                HandleDeckSelected);
            _view.SetMatchSetupError("Deck options are unavailable.");
        }
    }

    private void HandleDeckSelected(int playerIndex, string deckId)
    {
        if (playerIndex == 0) _selectedPlayer0DeckId = deckId;
        else if (playerIndex == 1) _selectedPlayer1DeckId = deckId;
        else return;

        _view.SetMatchSetupError(string.Empty);
        RefreshMatchSetupOptions();
    }

    private void StayOnMatchSetupWithError(string message)
    {
        _view.SetMatchSetupError(message);
        CurrentScreen = RuntimeScreenId.MatchSetup;
        ApplyScreen();
    }

    private static string ToSafeSetupError(string reasonKey)
    {
        switch (reasonKey)
        {
            case "match.deck_selection_invalid":
                return "Choose two different available decks.";
            case "match.data_unavailable":
                return "Deck data is unavailable.";
            case "match.data_invalid":
                return "Deck data is unavailable.";
            case "match.snapshot_unavailable":
                return "Match snapshot is unavailable.";
            default:
                return "Match could not be started.";
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _battlePanel = RuntimeBattlePanel.EnsureRuntimeInstanceForScene();
        _bootstrap = null;
        _matchSetupOrchestrator = null;
        ResetObservedSnapshot();
        _selectedPlayer0DeckId = string.Empty;
        _selectedPlayer1DeckId = string.Empty;
        _readyLogIssued = false;
        if (!_initialized) return;

        // A scene reload starts a fresh presentation flow. The engine host is
        // responsible for constructing any replacement session/panel.
        Navigate(RuntimeScreenId.Title);
        LogReadyIfPresentationIsVisible();
    }

    private static RuntimeBattlePanel FindRuntimeBattlePanel()
    {
        var panels = UnityEngine.Object.FindObjectsByType<RuntimeBattlePanel>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        return panels.Length == 0 ? null : panels[0];
    }

    private void LogReadyIfPresentationIsVisible()
    {
        if (_readyLogIssued || !IsPresentationReady) return;
        _readyLogIssued = true;
        Debug.Log(ReadyLogMessage);
    }

    private void ObserveAuthoritativeResult()
    {
        var adapter = _bootstrap == null ? null : _bootstrap.Adapter;
        var snapshot = adapter == null ? null : adapter.Presentation.Snapshot;
        if (!ReferenceEquals(adapter, _observedAdapter) ||
            (snapshot != null && snapshot.SnapshotRevision != _observedSnapshotRevision))
        {
            _observedAdapter = adapter;
            _observedSnapshotRevision = snapshot == null ? -1 : snapshot.SnapshotRevision;
            if (snapshot != null && IsAuthoritativeOutcome(snapshot))
                ShowAuthoritativeResult(snapshot);
        }
    }

    private static bool IsAuthoritativeOutcome(RuntimeSnapshotEnvelope snapshot)
    {
        return snapshot != null &&
            string.Equals(snapshot.Phase, "OVER", StringComparison.Ordinal) &&
            snapshot.WinnerPlayerIndex.HasValue &&
            snapshot.WinnerPlayerIndex.Value >= 0 &&
            snapshot.WinnerPlayerIndex.Value <= 1 &&
            !string.IsNullOrWhiteSpace(snapshot.ReasonKey);
    }

    private void ShowAuthoritativeResult(RuntimeSnapshotEnvelope snapshot)
    {
        _view.SetResultOutcome(snapshot.WinnerPlayerIndex.Value, snapshot.ReasonKey);
        Navigate(RuntimeScreenId.Result);
    }

    private void ResetObservedSnapshot()
    {
        _observedAdapter = null;
        _observedSnapshotRevision = -1;
    }

    private void RaiseIntent(RuntimeScreenIntent intent)
    {
        IntentRaised?.Invoke(intent);
    }

    private static RuntimeScreenId BackTarget(RuntimeScreenId screen)
    {
        switch (screen)
        {
            case RuntimeScreenId.MainMenu:
                return RuntimeScreenId.Title;
            case RuntimeScreenId.MatchSetup:
                return RuntimeScreenId.MainMenu;
            case RuntimeScreenId.BattleLoading:
                return RuntimeScreenId.MatchSetup;
            case RuntimeScreenId.Battle:
            case RuntimeScreenId.Result:
                return RuntimeScreenId.MainMenu;
            case RuntimeScreenId.Boot:
                return RuntimeScreenId.Title;
            case RuntimeScreenId.Title:
            default:
                return RuntimeScreenId.MainMenu;
        }
    }

    private void OnDestroy()
    {
        IntentRaised = null;
        if (_wiredRecoveryPanel != null)
            _wiredRecoveryPanel.RecoveryRequested -= HandleBattleRecovery;
        _wiredRecoveryPanel = null;
        if (_createdEventSystem == null) return;

        if (Application.isPlaying)
            Destroy(_createdEventSystem.gameObject);
        else
            DestroyImmediate(_createdEventSystem.gameObject);
        _createdEventSystem = null;
    }
}
}
