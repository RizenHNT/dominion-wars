using System;
using System.Collections.Generic;
using System.Globalization;
using DominionWars.Unity.Runtime;
using UnityEngine;

namespace DominionWars.Unity.UI
{

/// <summary>
/// Programmatic, neutral uGUI shells for the local screen flow. The battle
/// shell is only a marker: RuntimeScreenFlow uses the existing
/// RuntimeBattlePanel as the real Battle root when one is available.
/// </summary>
public sealed class RuntimeScreenShellView
{
    private readonly Dictionary<RuntimeScreenId, RectTransform> _screenRoots =
        new Dictionary<RuntimeScreenId, RectTransform>();
    private readonly List<UnityEngine.UI.Button> _interactiveButtons =
        new List<UnityEngine.UI.Button>();
    private readonly List<UnityEngine.UI.Button> _player0DeckButtons =
        new List<UnityEngine.UI.Button>();
    private readonly List<UnityEngine.UI.Button> _player1DeckButtons =
        new List<UnityEngine.UI.Button>();
    private readonly List<GameObject> _generatedDeckOptionObjects =
        new List<GameObject>();
    private string _deckOptionsSignature = string.Empty;

    private RuntimeScreenShellView() { }

    public RectTransform Root { get; private set; }
    public RectTransform BootRoot { get; private set; }
    public RectTransform TitleRoot { get; private set; }
    public RectTransform MainMenuRoot { get; private set; }
    public RectTransform MatchSetupRoot { get; private set; }
    public RectTransform BattleLoadingRoot { get; private set; }
    public RectTransform BattleRoot { get; private set; }
    public RectTransform ResultRoot { get; private set; }
    public RectTransform CurrentShellRoot { get; private set; }

    public UnityEngine.UI.Button BootContinueButton { get; private set; }
    public UnityEngine.UI.Button TitleContinueButton { get; private set; }
    public UnityEngine.UI.Button MainMenuMatchSetupButton { get; private set; }
    public UnityEngine.UI.Button MainMenuBackButton { get; private set; }
    public UnityEngine.UI.Button MatchSetupStartButton { get; private set; }
    public UnityEngine.UI.Button MatchSetupBackButton { get; private set; }
    public UnityEngine.UI.Button BattleLoadingBackButton { get; private set; }
    public UnityEngine.UI.Button ResultRestartButton { get; private set; }
    public UnityEngine.UI.Button ResultReturnToMenuButton { get; private set; }
    public UnityEngine.UI.Text MatchSetupErrorText { get; private set; }
    public UnityEngine.UI.Text ResultWinnerText { get; private set; }
    public UnityEngine.UI.Text ResultReasonText { get; private set; }
    public string DebugResultOutcome { get; private set; }

    public IReadOnlyList<UnityEngine.UI.Button> InteractiveButtons => _interactiveButtons;
    public IReadOnlyList<UnityEngine.UI.Button> MatchSetupPlayer0DeckButtons => _player0DeckButtons;
    public IReadOnlyList<UnityEngine.UI.Button> MatchSetupPlayer1DeckButtons => _player1DeckButtons;

    public int VisibleShellRootCount
    {
        get
        {
            var count = 0;
            foreach (var root in _screenRoots.Values)
            {
                if (root != null && root.gameObject.activeSelf) count++;
            }

            return count;
        }
    }

    public static RuntimeScreenShellView Build(RectTransform parent)
    {
        if (parent == null) throw new ArgumentNullException(nameof(parent));

        var view = new RuntimeScreenShellView
        {
            Root = CreateRect("RuntimeScreenFlowRoot", parent),
        };
        Stretch(view.Root);

        view.BootRoot = view.CreateScreen(RuntimeScreenId.Boot, "BootScreen", "BOOT");
        view.TitleRoot = view.CreateScreen(RuntimeScreenId.Title, "TitleScreen", "TITLE");
        view.MainMenuRoot = view.CreateScreen(RuntimeScreenId.MainMenu, "MainMenuScreen", "MAIN MENU");
        view.MatchSetupRoot = view.CreateScreen(RuntimeScreenId.MatchSetup, "MatchSetupScreen", "MATCH SETUP");
        view.BattleLoadingRoot = view.CreateScreen(RuntimeScreenId.BattleLoading, "BattleLoadingScreen", "BATTLE LOADING");
        view.BattleRoot = view.CreateScreen(RuntimeScreenId.Battle, "BattleScreen", "BATTLE");
        view.ResultRoot = view.CreateScreen(RuntimeScreenId.Result, "ResultScreen", "RESULT");

        view.BootContinueButton = view.CreateButton(view.BootRoot, "BootContinueButton", "CONTINUE", 0f);
        view.TitleContinueButton = view.CreateButton(view.TitleRoot, "TitleContinueButton", "ENTER", 0f);

        view.MainMenuMatchSetupButton = view.CreateButton(
            view.MainMenuRoot,
            "MainMenuMatchSetupButton",
            "MATCH SETUP",
            30f);
        view.MainMenuBackButton = view.CreateButton(view.MainMenuRoot, "MainMenuBackButton", "BACK", -42f);

        var player0Heading = CreateText(view.MatchSetupRoot, "MatchSetupPlayer0Heading", "PLAYER 1 DECK", 16, new Color(0.45f, 0.86f, 0.92f));
        SetCenteredRect(player0Heading.rectTransform, new Vector2(-480f, 101f), new Vector2(740f, 28f));
        player0Heading.fontStyle = FontStyle.Bold;
        var player1Heading = CreateText(view.MatchSetupRoot, "MatchSetupPlayer1Heading", "PLAYER 2 DECK", 16, new Color(0.98f, 0.70f, 0.33f));
        SetCenteredRect(player1Heading.rectTransform, new Vector2(480f, 101f), new Vector2(740f, 28f));
        player1Heading.fontStyle = FontStyle.Bold;
        view.MatchSetupErrorText = CreateText(view.MatchSetupRoot, "MatchSetupError", string.Empty, 14, new Color(1f, 0.48f, 0.43f));
        SetCenteredRect(view.MatchSetupErrorText.rectTransform, new Vector2(0f, -222f), new Vector2(1320f, 28f));

        view.MatchSetupStartButton = view.CreateButton(view.MatchSetupRoot, "MatchSetupStartButton", "START MATCH", -285f);
        view.MatchSetupBackButton = view.CreateButton(view.MatchSetupRoot, "MatchSetupBackButton", "BACK", -370f);

        view.ResultWinnerText = CreateText(view.ResultRoot, "ResultWinnerPlayerIndex", string.Empty, 20, Color.white);
        SetCenteredRect(view.ResultWinnerText.rectTransform, new Vector2(0f, 92f), new Vector2(820f, 40f));
        view.ResultReasonText = CreateText(view.ResultRoot, "ResultReasonKey", string.Empty, 16, new Color(0.72f, 0.76f, 0.80f));
        SetCenteredRect(view.ResultReasonText.rectTransform, new Vector2(0f, 52f), new Vector2(1200f, 32f));

        view.BattleLoadingBackButton = view.CreateButton(
            view.BattleLoadingRoot,
            "BattleLoadingBackButton",
            "BACK",
            0f);

        view.ResultRestartButton = view.CreateButton(view.ResultRoot, "ResultRestartButton", "RESTART", 30f);
        view.ResultReturnToMenuButton = view.CreateButton(
            view.ResultRoot,
            "ResultReturnToMenuButton",
            "RETURN TO MENU",
            -42f);

        view.SetVisibleScreen(RuntimeScreenId.Boot, false);
        return view;
    }

    public RectTransform GetScreenRoot(RuntimeScreenId screen)
    {
        return _screenRoots.TryGetValue(screen, out var root) ? root : null;
    }

    public void SetVisibleScreen(RuntimeScreenId screen, bool externalBattleRootAvailable)
    {
        foreach (var root in _screenRoots.Values)
        {
            if (root != null) root.gameObject.SetActive(false);
        }

        if (screen == RuntimeScreenId.Battle && externalBattleRootAvailable)
        {
            CurrentShellRoot = null;
            return;
        }

        var selected = GetScreenRoot(screen);
        if (selected == null)
            throw new ArgumentOutOfRangeException(nameof(screen), screen, "Unknown runtime screen.");

        selected.gameObject.SetActive(true);
        CurrentShellRoot = selected;
    }

    /// <summary>
    /// Renders the canonical deck metadata supplied by RuntimeBootstrap. The
    /// shell only displays names and raises a selection callback; it does
    /// not inspect cards or reproduce any engine validation.
    /// </summary>
    public void SetMatchSetupDeckOptions(
        IReadOnlyList<RuntimeDeckOption> options,
        string selectedPlayer0DeckId,
        string selectedPlayer1DeckId,
        Action<int, string> onSelected)
    {
        options = options ?? Array.Empty<RuntimeDeckOption>();
        var signature = BuildDeckOptionsSignature(options);
        if (!string.Equals(signature, _deckOptionsSignature, StringComparison.Ordinal))
        {
            ClearGeneratedDeckOptions();
            for (var index = 0; index < options.Count; index++)
            {
                var option = options[index];
                if (option == null) continue;
                var y = 55f - index * 76f;
                var player0Button = CreateDeckOptionButton(
                    MatchSetupRoot,
                    "MatchSetupPlayer0Deck_" + option.Id,
                    option,
                    0,
                    -480f,
                    y,
                    onSelected);
                var player1Button = CreateDeckOptionButton(
                    MatchSetupRoot,
                    "MatchSetupPlayer1Deck_" + option.Id,
                    option,
                    1,
                    480f,
                    y,
                    onSelected);
                _player0DeckButtons.Add(player0Button);
                _player1DeckButtons.Add(player1Button);
            }

            _deckOptionsSignature = signature;
        }

        UpdateDeckOptionLabels(options, selectedPlayer0DeckId, selectedPlayer1DeckId);
    }

    public void SetMatchSetupError(string message)
    {
        if (MatchSetupErrorText != null)
            MatchSetupErrorText.text = string.IsNullOrWhiteSpace(message) ? string.Empty : message;
    }

    /// <summary>
    /// Displays the authoritative terminal fields without localizing,
    /// interpreting, or deriving an outcome in the shell.
    /// </summary>
    public void SetResultOutcome(int winnerPlayerIndex, string reasonKey)
    {
        if (ResultWinnerText != null)
            ResultWinnerText.text = winnerPlayerIndex is 0 or 1
                ? "PLAYER " + (winnerPlayerIndex + 1).ToString(CultureInfo.InvariantCulture) + " WINS"
                : "MATCH COMPLETE";
        if (ResultReasonText != null)
            ResultReasonText.text = "MATCH COMPLETE";
        DebugResultOutcome = winnerPlayerIndex.ToString(CultureInfo.InvariantCulture) +
            " | " + (reasonKey ?? string.Empty);
    }

    /// <summary>
    /// Restores the complete terminal wire outcome only for an explicitly
    /// enabled Editor/Development diagnostic surface.
    /// </summary>
    public void SetDebugResultOutcome(int winnerPlayerIndex, string reasonKey)
    {
        if (!(Application.isEditor || Debug.isDebugBuild)) return;
        if (ResultWinnerText != null)
            ResultWinnerText.text = winnerPlayerIndex.ToString(CultureInfo.InvariantCulture);
        if (ResultReasonText != null)
            ResultReasonText.text = reasonKey ?? string.Empty;
        DebugResultOutcome = winnerPlayerIndex.ToString(CultureInfo.InvariantCulture) +
            " | " + (reasonKey ?? string.Empty);
    }

    public void ClearResultOutcome()
    {
        if (ResultWinnerText != null) ResultWinnerText.text = string.Empty;
        if (ResultReasonText != null) ResultReasonText.text = string.Empty;
        DebugResultOutcome = string.Empty;
    }

    private RectTransform CreateScreen(RuntimeScreenId screen, string objectName, string title)
    {
        var root = CreateRect(objectName, Root);
        Stretch(root);

        var background = root.gameObject.AddComponent<UnityEngine.UI.Image>();
        background.color = new Color(0.055f, 0.065f, 0.075f, 0.96f);
        background.raycastTarget = false;

        var titleText = CreateText(root, "ScreenTitle", title, 30, Color.white);
        SetCenteredRect(titleText.rectTransform, new Vector2(0f, 190f), new Vector2(720f, 64f));

        var subtitle = CreateText(root, "ScreenSubtitle", string.Empty, 14, new Color(0.72f, 0.76f, 0.80f));
        SetCenteredRect(subtitle.rectTransform, new Vector2(0f, 132f), new Vector2(720f, 32f));

        _screenRoots[screen] = root;
        return root;
    }

    private UnityEngine.UI.Button CreateButton(
        RectTransform parent,
        string objectName,
        string label,
        float verticalOffset,
        float width = 360f,
        float height = 72f)
    {
        var buttonObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(UnityEngine.UI.Image),
            typeof(UnityEngine.UI.Button));
        buttonObject.transform.SetParent(parent, false);

        var rect = buttonObject.GetComponent<RectTransform>();
        SetCenteredRect(rect, new Vector2(0f, verticalOffset), new Vector2(width, height));

        var image = buttonObject.GetComponent<UnityEngine.UI.Image>();
        image.color = new Color(0.16f, 0.18f, 0.20f, 1f);
        image.raycastTarget = true;

        var button = buttonObject.GetComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        button.navigation = new UnityEngine.UI.Navigation
        {
            mode = UnityEngine.UI.Navigation.Mode.None,
        };

        var text = CreateText(rect, "Label", label, 18, Color.white);
        Stretch(text.rectTransform);
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;

        _interactiveButtons.Add(button);
        return button;
    }

    private UnityEngine.UI.Button CreateDeckOptionButton(
        RectTransform parent,
        string objectName,
        RuntimeDeckOption option,
        int playerIndex,
        float horizontalOffset,
        float verticalOffset,
        Action<int, string> onSelected)
    {
        var button = CreateButton(parent, objectName, option.DisplayName, verticalOffset, 740f, 72f);
        var rect = button.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(horizontalOffset, verticalOffset);
        var capturedId = option.Id;
        button.onClick.AddListener(() => onSelected?.Invoke(playerIndex, capturedId));
        _generatedDeckOptionObjects.Add(button.gameObject);
        return button;
    }

    private void UpdateDeckOptionLabels(
        IReadOnlyList<RuntimeDeckOption> options,
        string selectedPlayer0DeckId,
        string selectedPlayer1DeckId)
    {
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            if (option == null || index >= _player0DeckButtons.Count || index >= _player1DeckButtons.Count)
                continue;

            SetDeckOptionLabel(
                _player0DeckButtons[index],
                option,
                string.Equals(option.Id, selectedPlayer0DeckId, StringComparison.Ordinal));
            SetDeckOptionLabel(
                _player1DeckButtons[index],
                option,
                string.Equals(option.Id, selectedPlayer1DeckId, StringComparison.Ordinal));
        }
    }

    private static void SetDeckOptionLabel(
        UnityEngine.UI.Button button,
        RuntimeDeckOption option,
        bool selected)
    {
        var label = button.GetComponentInChildren<UnityEngine.UI.Text>(true);
        if (label == null) return;
        label.fontSize = 14;
        label.text = (selected ? "✓ " : "  ") + option.DisplayName +
            "\n" + option.Faction;
    }

    private void ClearGeneratedDeckOptions()
    {
        _player0DeckButtons.Clear();
        _player1DeckButtons.Clear();
        foreach (var optionObject in _generatedDeckOptionObjects)
        {
            if (optionObject == null) continue;
            var button = optionObject.GetComponent<UnityEngine.UI.Button>();
            if (button != null) _interactiveButtons.Remove(button);
            optionObject.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(optionObject);
            else UnityEngine.Object.DestroyImmediate(optionObject);
        }

        _generatedDeckOptionObjects.Clear();
    }

    private static string BuildDeckOptionsSignature(IReadOnlyList<RuntimeDeckOption> options)
    {
        if (options == null || options.Count == 0) return string.Empty;
        var signature = string.Empty;
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            if (option == null) continue;
            signature += option.Id + "|" + option.DisplayName + "|" + option.Faction + "|" + option.Leader + ";";
        }

        return signature;
    }

    private static UnityEngine.UI.Text CreateText(
        RectTransform parent,
        string objectName,
        string textValue,
        int fontSize,
        Color color)
    {
        var textObject = new GameObject(objectName, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        var text = textObject.AddComponent<UnityEngine.UI.Text>();
        text.font = Resources.GetBuiltinResource<Font>(RuntimeBattlePanelDefaults.LegacyBuiltinFontResource);
        text.fontSize = fontSize;
        text.color = color;
        text.text = textValue;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        text.supportRichText = false;
        return text;
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        var objectRect = new GameObject(objectName, typeof(RectTransform));
        objectRect.transform.SetParent(parent, false);
        return objectRect.GetComponent<RectTransform>();
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetCenteredRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
}
