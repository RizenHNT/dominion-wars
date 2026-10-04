using System;
using System.Collections.Generic;
using UnityEngine;

namespace DominionWars.Unity.UI
{

/// <summary>Presentation-only skin for the existing Boot, Title, and MainMenu shells.</summary>
public sealed class RuntimeMenuPresentation : MonoBehaviour
{
    private const string VolumeKey = "DominionWars.MenuMasterVolume";
    private const string MotionKey = "DominionWars.MenuReducedMotion";
    private const float StageWidth = 1600f, StageHeight = 900f;
    private static readonly Color Cobalt = new Color32(18, 84, 230, 255);
    private static readonly Color Red = new Color32(237, 56, 43, 255);
    private static readonly Color Yellow = new Color32(255, 226, 63, 255);
    private static readonly Color Paper = new Color32(244, 238, 216, 255);
    private static readonly Color Ink = new Color32(18, 24, 31, 255);
    private static Font _font;
    private RuntimeScreenShellView _view;
    private readonly List<RectTransform> _stages = new List<RectTransform>();
    private readonly List<UnityEngine.UI.Button> _buttons = new List<UnityEngine.UI.Button>();
    private readonly Dictionary<UnityEngine.UI.Button, bool> _hovered = new Dictionary<UnityEngine.UI.Button, bool>();
    private readonly Dictionary<UnityEngine.UI.Button, SlantedGraphic> _faces = new Dictionary<UnityEngine.UI.Button, SlantedGraphic>();
    private readonly Dictionary<UnityEngine.UI.Button, TriangleGraphic> _cursors = new Dictionary<UnityEngine.UI.Button, TriangleGraphic>();
    private readonly Dictionary<UnityEngine.UI.Button, SlantedGraphic[]> _hoverPlates = new Dictionary<UnityEngine.UI.Button, SlantedGraphic[]>();
    private RectTransform _mainStage, _activeStage, _settingsPanel;
    private CanvasGroup _settingsGroup;
    private UnityEngine.UI.Slider _volumeSlider;
    private UnityEngine.UI.Toggle _motionToggle;
    private UnityEngine.UI.Button _settingsButton, _settingsCloseButton;
    private bool _built, _reducedMenuAnimations;
    private float _transitionStarted, _settingsOpenedAt = -1f;

    public bool IsSettingsOpen { get; private set; }

    public static RuntimeMenuPresentation Attach(RuntimeScreenShellView view)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));
        if (view.Root == null) throw new InvalidOperationException("Runtime screen root is unavailable.");
        var result = view.Root.GetComponent<RuntimeMenuPresentation>() ?? view.Root.gameObject.AddComponent<RuntimeMenuPresentation>();
        result.Configure(view);
        return result;
    }

    private void Configure(RuntimeScreenShellView view)
    {
        if (_built && ReferenceEquals(_view, view)) return;
        _view = view;
        _reducedMenuAnimations = PlayerPrefs.GetInt(MotionKey, 0) != 0;
        _stages.Add(BuildStage(view.BootRoot, "Boot", "统御战纪", "DOMINION WARS", false));
        _stages.Add(BuildStage(view.TitleRoot, "Title", "统御战纪", "DUEL / STRATEGY CARD GAME", false));
        _mainStage = BuildStage(view.MainMenuRoot, "MainMenu", "统御战纪", "SELECT OPERATION", true);
        _stages.Add(_mainStage);
        Prepare(view.BootContinueButton, _stages[0], new Vector2(0f, -180f), new Vector2(430f, 82f), "继续");
        Prepare(view.TitleContinueButton, _stages[1], new Vector2(0f, -180f), new Vector2(430f, 82f), "进入");
        Prepare(view.MainMenuMatchSetupButton, _mainStage, new Vector2(-450f, 150f), new Vector2(500f, 82f), "开始对局");
        Prepare(view.MainMenuBackButton, _mainStage, new Vector2(-410f, -140f), new Vector2(500f, 70f), "返回标题");
        _settingsButton = NewButton(view.MainMenuRoot, "MainMenuSettingsButton");
        Prepare(_settingsButton, _mainStage, new Vector2(-430f, 0f), new Vector2(500f, 82f), "设置");
        _settingsButton.onClick.AddListener(ShowSettings);
        CreateSettings(_mainStage);
        SetNavigation(view.BootContinueButton);
        SetNavigation(view.TitleContinueButton);
        SetNavigation(view.MainMenuMatchSetupButton, _settingsButton, view.MainMenuBackButton);
        _built = true;
    }

    private RectTransform BuildStage(RectTransform screen, string key, string title, string kicker, bool main)
    {
        if (screen == null) return null;
        StyleLegacyShell(screen);
        var stage = new GameObject("MenuStage_" + key, typeof(RectTransform)).GetComponent<RectTransform>();
        stage.SetParent(screen, false); Center(stage, Vector2.zero, new Vector2(StageWidth, StageHeight));
        var background = stage.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        background.texture = Resources.Load<Texture2D>("MenuArt/menu-terrazzo-v01");
        background.color = background.texture == null ? new Color(.09f, .12f, .16f, 1f) : Color.white;
        background.raycastTarget = false;
        Chip(stage, new Vector2(-575f, 300f), new Vector2(360f, 72f), Red, -8f);
        Chip(stage, new Vector2(480f, 285f), new Vector2(430f, 94f), Cobalt, 7f);
        Chip(stage, new Vector2(510f, -315f), new Vector2(280f, 58f), Yellow, -7f);
        Text(stage, "Kicker", kicker, 18, new Vector2(0f, 235f), main ? Cobalt : Yellow);
        Text(stage, "Title", title, main ? 96 : 68, main ? new Vector2(360f, 60f) : new Vector2(0f, 155f), main ? Ink : Paper);
        return stage;
    }

    private void CreateSettings(RectTransform stage)
    {
        var objectRoot = new GameObject("LocalMenuSettingsPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(UnityEngine.UI.Image));
        _settingsPanel = objectRoot.GetComponent<RectTransform>(); objectRoot.transform.SetParent(stage, false);
        Center(_settingsPanel, new Vector2(370f, 0f), new Vector2(720f, 660f));
        _settingsGroup = objectRoot.GetComponent<CanvasGroup>();
        var panelImage = objectRoot.GetComponent<UnityEngine.UI.Image>(); panelImage.color = Paper; panelImage.raycastTarget = true;
        Chip(_settingsPanel, new Vector2(-220f, 260f), new Vector2(230f, 48f), Red, -8f);
        Text(_settingsPanel, "SettingsHeading", "设置", 48, new Vector2(-220f, 220f), Ink);
        Text(_settingsPanel, "VolumeLabel", "主音量", 28, new Vector2(-220f, 72f), Ink);
        Text(_settingsPanel, "MotionLabel", "减少菜单动画", 28, new Vector2(-220f, -72f), Ink);
        _volumeSlider = Slider(_settingsPanel, new Vector2(100f, 72f)); _volumeSlider.onValueChanged.AddListener(ApplyVolume);
        _motionToggle = Toggle(_settingsPanel, new Vector2(100f, -72f)); _motionToggle.onValueChanged.AddListener(ApplyReducedMotion);
        _settingsCloseButton = NewButton(_settingsPanel, "SettingsCloseButton");
        Center(_settingsCloseButton.GetComponent<RectTransform>(), new Vector2(0f, -215f), new Vector2(260f, 64f));
        StyleButton(_settingsCloseButton, "关闭", Cobalt, Paper); _settingsCloseButton.onClick.AddListener(HideSettings);
        _settingsPanel.gameObject.SetActive(false);
    }

    public void ShowSettings()
    {
        if (_settingsPanel == null) return;
        _reducedMenuAnimations = PlayerPrefs.GetInt(MotionKey, 0) != 0;
        _volumeSlider.SetValueWithoutNotify(Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, AudioListener.volume)));
        _motionToggle.SetIsOnWithoutNotify(_reducedMenuAnimations);
        IsSettingsOpen = true; _settingsPanel.gameObject.SetActive(true); SetMenuButtonsInteractable(false);
        _settingsGroup.alpha = _reducedMenuAnimations ? 1f : 0f;
        _settingsPanel.localScale = _reducedMenuAnimations ? Vector3.one : Vector3.one * .96f;
        _settingsOpenedAt = _reducedMenuAnimations ? -1f : Time.unscaledTime;
        if (UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(_settingsCloseButton.gameObject);
    }

    public void HideSettings()
    {
        if (_settingsPanel == null) return;
        IsSettingsOpen = false; _settingsOpenedAt = -1f; _settingsPanel.gameObject.SetActive(false);
        _settingsGroup.alpha = 0f; _settingsPanel.localScale = Vector3.one; SetMenuButtonsInteractable(true);
        if (UnityEngine.EventSystems.EventSystem.current != null && _settingsButton != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(_settingsButton.gameObject);
    }

    private void ApplyVolume(float value)
    {
        value = Mathf.Clamp01(value); AudioListener.volume = value; PlayerPrefs.SetFloat(VolumeKey, value); PlayerPrefs.Save();
    }

    private void ApplyReducedMotion(bool enabled)
    {
        _reducedMenuAnimations = enabled; PlayerPrefs.SetInt(MotionKey, enabled ? 1 : 0); PlayerPrefs.Save();
        if (enabled && _settingsGroup != null) { _settingsGroup.alpha = 1f; _settingsPanel.localScale = Vector3.one; _settingsOpenedAt = -1f; }
    }

    private void Update()
    {
        if (!_built) return;
        var active = ActiveStage();
        if (active != _activeStage)
        {
            _activeStage = active; _transitionStarted = Time.unscaledTime;
            if (IsSettingsOpen && active != _mainStage) HideSettings();
            SelectDefault(active);
        }
        var scale = StageScale();
        foreach (var stage in _stages)
        {
            if (stage == null) continue;
            var animation = stage == active && !_reducedMenuAnimations ? Mathf.Lerp(.98f, 1f, Mathf.Clamp01((Time.unscaledTime - _transitionStarted) / .18f)) : 1f;
            stage.localScale = Vector3.one * scale * animation;
        }
        if (IsSettingsOpen && Input.GetKeyDown(KeyCode.Escape)) HideSettings();
        if (IsSettingsOpen && _settingsOpenedAt >= 0f && !_reducedMenuAnimations)
        {
            var t = Mathf.Clamp01((Time.unscaledTime - _settingsOpenedAt) / .16f);
            _settingsGroup.alpha = t; _settingsPanel.localScale = Vector3.one * Mathf.Lerp(.96f, 1f, t);
            if (t >= 1f) _settingsOpenedAt = -1f;
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var selected = UnityEngine.EventSystems.EventSystem.current == null ? null : UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
        foreach (var button in _buttons)
        {
            if (button == null) continue;
            var focused = (_hovered.TryGetValue(button, out var over) && over) || selected == button.gameObject;
            _faces[button].color = focused ? Cobalt : Paper;
            var text = button.GetComponentInChildren<UnityEngine.UI.Text>(true); if (text != null) text.color = focused ? Paper : Ink;
            button.transform.localScale = Vector3.one * (focused ? 1.04f : 1f);
            _cursors[button].gameObject.SetActive(focused);
            foreach (var plate in _hoverPlates[button]) plate.gameObject.SetActive(focused);
        }
    }

    private void SelectDefault(RectTransform active)
    {
        if (active == null || IsSettingsOpen || UnityEngine.EventSystems.EventSystem.current == null) return;
        var button = active == _stages[0] ? _view.BootContinueButton : active == _stages[1] ? _view.TitleContinueButton : _view.MainMenuMatchSetupButton;
        if (button != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(button.gameObject);
    }

    private RectTransform ActiveStage()
    {
        foreach (var stage in _stages) if (stage != null && stage.parent.gameObject.activeSelf) return stage;
        return null;
    }

    private float StageScale()
    {
        var root = _view == null ? null : _view.Root;
        var width = root == null || root.rect.width <= 0f ? 1920f : root.rect.width;
        var height = root == null || root.rect.height <= 0f ? 1080f : root.rect.height;
        return Mathf.Min(width / StageWidth, height / StageHeight);
    }

    private void Prepare(UnityEngine.UI.Button button, RectTransform stage, Vector2 position, Vector2 size, string label)
    {
        if (button == null || stage == null) return;
        button.transform.SetParent(stage, false); Center(button.GetComponent<RectTransform>(), position, size);
        _buttons.Add(button); _hovered[button] = false; StyleButton(button, label, Paper, Ink);
        var image = button.GetComponent<UnityEngine.UI.Image>(); if (image != null) { image.color = Color.clear; image.raycastTarget = true; }
        var face = button.gameObject.GetComponent<SlantedGraphic>() ?? button.gameObject.AddComponent<SlantedGraphic>(); face.color = Paper; face.raycastTarget = false; _faces[button] = face; button.targetGraphic = face;
        var shadow = button.GetComponent<UnityEngine.UI.Shadow>() ?? button.gameObject.AddComponent<UnityEngine.UI.Shadow>(); shadow.effectColor = new Color(0f, 0f, 0f, .82f); shadow.effectDistance = new Vector2(9f, -9f); shadow.useGraphicAlpha = true;
        var relay = button.GetComponent<MenuHoverRelay>() ?? button.gameObject.AddComponent<MenuHoverRelay>(); relay.Owner = this; relay.Target = button;
        var cursorObject = new GameObject("YellowTriangleCursor", typeof(RectTransform)); cursorObject.transform.SetParent(button.transform, false);
        var cursor = cursorObject.AddComponent<TriangleGraphic>(); cursor.color = Yellow; cursor.raycastTarget = false; Center(cursorObject.GetComponent<RectTransform>(), new Vector2(-size.x * .5f - 18f, 0f), new Vector2(28f, 24f)); cursorObject.SetActive(false); _cursors[button] = cursor;
        var redPlate = SlantChild(button.transform, "HoverRed", Red, new Vector2(-size.x * .5f + 42f, size.y * .5f - 5f), new Vector2(108f, 18f));
        var yellowPlate = SlantChild(button.transform, "HoverYellow", Yellow, new Vector2(size.x * .5f - 42f, -size.y * .5f + 5f), new Vector2(108f, 18f));
        redPlate.gameObject.SetActive(false); yellowPlate.gameObject.SetActive(false); _hoverPlates[button] = new[] { redPlate, yellowPlate };
    }

    private static UnityEngine.UI.Button NewButton(Transform parent, string name)
    {
        var o = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button)); o.transform.SetParent(parent, false);
        var button = o.GetComponent<UnityEngine.UI.Button>(); button.targetGraphic = o.GetComponent<UnityEngine.UI.Image>(); return button;
    }

    private static void StyleButton(UnityEngine.UI.Button button, string value, Color background, Color textColor)
    {
        var image = button.targetGraphic as UnityEngine.UI.Image; if (image != null) { image.color = background; image.raycastTarget = true; }
        button.transition = UnityEngine.UI.Selectable.Transition.None;
        var text = button.GetComponentInChildren<UnityEngine.UI.Text>(true);
        if (text == null) { var o = new GameObject("Label", typeof(RectTransform)); o.transform.SetParent(button.transform, false); text = o.AddComponent<UnityEngine.UI.Text>(); }
        text.font = CreateFont(); text.fontSize = 27; text.fontStyle = FontStyle.Bold; text.color = textColor; text.text = value; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; text.supportRichText = false;
        text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = new Vector2(16f, 0f); text.rectTransform.offsetMax = new Vector2(-16f, 0f);
    }

    private void SetMenuButtonsInteractable(bool value)
    {
        foreach (var button in _buttons) if (button != null) button.interactable = value;
        if (_settingsCloseButton != null) _settingsCloseButton.interactable = !value;
    }

    private static void SetNavigation(params UnityEngine.UI.Button[] buttons)
    {
        for (var i = 0; i < buttons.Length; i++) if (buttons[i] != null)
        {
            var n = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit };
            n.selectOnUp = buttons[Mathf.Max(0, i - 1)]; n.selectOnDown = buttons[Mathf.Min(buttons.Length - 1, i + 1)]; buttons[i].navigation = n;
        }
    }

    private static void StyleLegacyShell(RectTransform root)
    {
        var background = root.GetComponent<UnityEngine.UI.Image>(); if (background != null) { background.color = Color.clear; background.raycastTarget = false; }
        var heading = root.Find("ScreenTitle"); var subtitle = root.Find("ScreenSubtitle");
        if (heading != null) heading.gameObject.SetActive(false); if (subtitle != null) subtitle.gameObject.SetActive(false);
    }

    private static UnityEngine.UI.Text Text(Transform parent, string name, string value, int size, Vector2 position, Color color)
    {
        var o = new GameObject(name, typeof(RectTransform)); o.transform.SetParent(parent, false); var text = o.AddComponent<UnityEngine.UI.Text>();
        text.font = CreateFont(); text.fontSize = size; text.fontStyle = FontStyle.Bold; text.color = color; text.text = value; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; text.supportRichText = false;
        Center(text.rectTransform, position, new Vector2(1300f, size + 26f)); return text;
    }

    private static SlantedGraphic SlantChild(Transform parent, string name, Color color, Vector2 position, Vector2 size)
    {
        var o = new GameObject(name, typeof(RectTransform)); o.transform.SetParent(parent, false); var graphic = o.AddComponent<SlantedGraphic>(); graphic.color = color; graphic.raycastTarget = false; Center(o.GetComponent<RectTransform>(), position, size); return graphic;
    }

    private static void Chip(Transform parent, Vector2 position, Vector2 size, Color color, float angle)
    {
        var shadow = SlantChild(parent, "HardShadow", new Color(0f, 0f, 0f, .78f), position + new Vector2(12f, -12f), size); shadow.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        var front = SlantChild(parent, "ColorChip", color, position, size); front.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    private static UnityEngine.UI.Slider Slider(RectTransform parent, Vector2 position)
    {
        var o = new GameObject("MasterVolumeSlider", typeof(RectTransform), typeof(UnityEngine.UI.Slider), typeof(UnityEngine.UI.Image)); o.transform.SetParent(parent, false); Center(o.GetComponent<RectTransform>(), position, new Vector2(420f, 34f)); o.GetComponent<UnityEngine.UI.Image>().color = Ink;
        var fill = SlantChild(o.transform, "Fill", Cobalt, Vector2.zero, new Vector2(420f, 34f)); var handle = SlantChild(o.transform, "Handle", Yellow, new Vector2(0f, 0f), new Vector2(32f, 52f));
        var slider = o.GetComponent<UnityEngine.UI.Slider>(); slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle; slider.minValue = 0f; slider.maxValue = 1f; slider.value = 1f; return slider;
    }

    private static UnityEngine.UI.Toggle Toggle(RectTransform parent, Vector2 position)
    {
        var o = new GameObject("ReducedMenuMotionToggle", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Toggle)); o.transform.SetParent(parent, false); Center(o.GetComponent<RectTransform>(), position, new Vector2(420f, 54f));
        var image = o.GetComponent<UnityEngine.UI.Image>(); image.color = Ink; image.raycastTarget = true; var check = SlantChild(o.transform, "Checkmark", Yellow, new Vector2(-170f, 0f), new Vector2(28f, 28f));
        Text(o.transform, "Label", "REDUCE MENU MOTION", 20, new Vector2(55f, 0f), Paper); var toggle = o.GetComponent<UnityEngine.UI.Toggle>(); toggle.targetGraphic = image; toggle.graphic = check; toggle.isOn = false; return toggle;
    }

    private static Font CreateFont()
    {
        if (_font != null) return _font;
        try { _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 48); } catch (Exception) { }
        if (_font == null) _font = Resources.GetBuiltinResource<Font>(RuntimeBattlePanelDefaults.LegacyBuiltinFontResource);
        return _font;
    }

    private static void Center(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(.5f, .5f); rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size;
    }

    private void OnDisable()
    {
        if (_built && IsSettingsOpen) HideSettings();
    }

    private sealed class MenuHoverRelay : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public RuntimeMenuPresentation Owner; public UnityEngine.UI.Button Target;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) { if (Owner != null) Owner._hovered[Target] = true; }
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { if (Owner != null) Owner._hovered[Target] = false; }
    }

    private sealed class SlantedGraphic : UnityEngine.UI.Graphic
    {
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear(); var r = rectTransform.rect; var cut = Mathf.Min(18f, r.width * .18f);
            vh.AddVert(new Vector3(r.xMin + cut, r.yMax), color, Vector2.zero); vh.AddVert(new Vector3(r.xMax, r.yMax), color, Vector2.right);
            vh.AddVert(new Vector3(r.xMax - cut, r.yMin), color, Vector2.one); vh.AddVert(new Vector3(r.xMin, r.yMin), color, Vector2.up); vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 3);
        }
    }

    private sealed class TriangleGraphic : UnityEngine.UI.Graphic
    {
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear(); var r = rectTransform.rect; vh.AddVert(new Vector3(r.xMin, r.yMin), color, Vector2.zero); vh.AddVert(new Vector3(r.xMin, r.yMax), color, Vector2.up); vh.AddVert(new Vector3(r.xMax, r.yMin), color, Vector2.right); vh.AddTriangle(0, 1, 2);
        }
    }
}
}
