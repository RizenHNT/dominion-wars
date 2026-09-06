#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DominionWars.Data;
using DominionWars.Data.CardEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

[assembly: InternalsVisibleTo("DominionWars.Unity.EditMode")]

namespace DominionWars.Unity.EditorTools.CardEditor
{
    /// <summary>
    /// Schema-driven authoring window for the current card data contract.
    ///
    /// The window edits CardDocument JSON only. It does not contain gameplay
    /// legality, effect dispatch, targeting, or balance rules; those remain
    /// owned by the schema and the authoritative data/runtime layers.
    /// </summary>
    public sealed class CardEditorWindow : EditorWindow
    {
        internal const string MenuPath = "Dominion Wars/Card Editor";
        public const string UxmlAssetPath =
            "Assets/DominionWars.Editor/CardEditor/CardEditorWindow.uxml";
        public const string UssAssetPath =
            "Assets/DominionWars.Editor/CardEditor/CardEditorWindow.uss";

        private const string CardsDirectory = "data/cards";
        private const string SchemaPath = "data/schema/cards.schema.json";
        private const string ContentManifestPath = "data/content/manifests/content.manifest.json";
        private const string ContentRootPath = "data/content";
        private const string DraftDirectory = "data/card-editor/drafts";
        private const string StatusAll = "All statuses";
        private const string NoArtwork = "(none)";

        private readonly CardDocumentStore _store = new CardDocumentStore();
        private readonly Stack<EditorSnapshot> _undo = new Stack<EditorSnapshot>();
        private readonly Stack<EditorSnapshot> _redo = new Stack<EditorSnapshot>();
        private readonly Dictionary<string, VisualElement> _fieldControls =
            new Dictionary<string, VisualElement>(StringComparer.Ordinal);

        private VisualElement _fieldsScroll;
        private ListView _cardList;
        private TextField _searchField;
        private DropdownField _statusFilter;
        private Label _librarySummary;
        private Label _selectedTitle;
        private Label _selectedMeta;
        private Label _selectedPath;
        private Label _lifecycleBadge;
        private Label _validationOutput;
        private Label _statusBar;
        private Label _artIdLabel;
        private Label _fallbackLabel;
        private TextField _savePathField;
        private Image _artPreview;
        private Button _cloneButton;
        private Button _undoButton;
        private Button _redoButton;
        private Button _validateButton;
        private Button _saveButton;

        private CardEditorMetadataRegistry _metadata;
        private CardDocumentIndex _index;
        private ContentCatalog _contentCatalog;
        private CardDocument _document;
        private List<CardDocument> _visibleDocuments = new List<CardDocument>();
        private string _repositoryRoot;
        private string _saveTargetPath;
        private string _loadedSnapshot;
        private Texture2D _previewTexture;
        private bool _suppressChanges;

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<CardEditorWindow>();
            window.titleContent = new GUIContent("Card Editor");
            window.minSize = new Vector2(900f, 620f);
            window.Show();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlAssetPath);
            if (visualTree is null)
            {
                rootVisualElement.Add(new Label(
                    "Card Editor UI asset is missing: " + UxmlAssetPath));
                return;
            }

            visualTree.CloneTree(rootVisualElement);
            BindControls();
            ReloadRepository(preserveSelection: false);
        }

        private void BindControls()
        {
            _fieldsScroll = rootVisualElement.Q<ScrollView>("fieldsScroll");
            _cardList = rootVisualElement.Q<ListView>("cardList");
            _searchField = rootVisualElement.Q<TextField>("searchField");
            _statusFilter = rootVisualElement.Q<DropdownField>("statusFilter");
            _librarySummary = rootVisualElement.Q<Label>("librarySummary");
            _selectedTitle = rootVisualElement.Q<Label>("selectedTitle");
            _selectedMeta = rootVisualElement.Q<Label>("selectedMeta");
            _selectedPath = rootVisualElement.Q<Label>("selectedPath");
            _lifecycleBadge = rootVisualElement.Q<Label>("lifecycleBadge");
            _validationOutput = rootVisualElement.Q<Label>("validationOutput");
            _statusBar = rootVisualElement.Q<Label>("statusBar");
            _artIdLabel = rootVisualElement.Q<Label>("artIdLabel");
            _fallbackLabel = rootVisualElement.Q<Label>("fallbackLabel");
            _savePathField = rootVisualElement.Q<TextField>("savePathField");
            _artPreview = rootVisualElement.Q<Image>("artPreview");
            _cloneButton = rootVisualElement.Q<Button>("cloneButton");
            _undoButton = rootVisualElement.Q<Button>("undoButton");
            _redoButton = rootVisualElement.Q<Button>("redoButton");
            _validateButton = rootVisualElement.Q<Button>("validateButton");
            _saveButton = rootVisualElement.Q<Button>("saveButton");

            rootVisualElement.Q<Button>("refreshButton").clicked +=
                () => ReloadRepository(preserveSelection: true);
            rootVisualElement.Q<Button>("newButton").clicked += CreateNewDocument;
            _cloneButton.clicked += CloneCurrentDocument;
            _undoButton.clicked += ExecuteUndoCommand;
            _redoButton.clicked += ExecuteRedoCommand;
            _validateButton.clicked += () => ValidateCurrent(showStatus: true);
            _saveButton.clicked += SaveCurrent;

            _searchField.RegisterValueChangedCallback(_ => RefreshCardList());
            _statusFilter.choices = new List<string>
            {
                StatusAll,
                CardLifecycleCodec.ToWireValue(CardLifecycle.Draft),
                CardLifecycleCodec.ToWireValue(CardLifecycle.Ready),
                CardLifecycleCodec.ToWireValue(CardLifecycle.Approved),
                CardLifecycleCodec.ToWireValue(CardLifecycle.Deprecated)
            };
            _statusFilter.SetValueWithoutNotify(StatusAll);
            _statusFilter.RegisterValueChangedCallback(_ => RefreshCardList());

            _cardList.selectionType = SelectionType.Single;
            _cardList.fixedItemHeight = 48f;
            _cardList.makeItem = () =>
            {
                var label = new Label();
                label.AddToClassList("card-list-item");
                return label;
            };
            _cardList.bindItem = (element, index) =>
            {
                if (index < 0 || index >= _visibleDocuments.Count) return;
                var document = _visibleDocuments[index];
                var label = (Label)element;
                label.text = FormatListItem(document);
                label.tooltip = document.SourcePath ?? "New in-memory card";
                label.EnableInClassList(
                    "status-draft", document.Lifecycle == CardLifecycle.Draft);
                label.EnableInClassList(
                    "status-ready", document.Lifecycle == CardLifecycle.Ready);
                label.EnableInClassList(
                    "status-approved", document.Lifecycle == CardLifecycle.Approved);
                label.EnableInClassList(
                    "status-deprecated", document.Lifecycle == CardLifecycle.Deprecated);
            };
            _cardList.selectionChanged += OnLibrarySelectionChanged;
        }

        private void ReloadRepository(bool preserveSelection)
        {
            var previousId = preserveSelection ? _document?.Id : null;
            try
            {
                _repositoryRoot = ResolveRepositoryRoot();
                var schema = Path.Combine(_repositoryRoot, SchemaPath.Replace('/', Path.DirectorySeparatorChar));
                var cards = Path.Combine(_repositoryRoot, CardsDirectory.Replace('/', Path.DirectorySeparatorChar));
                var manifest = Path.Combine(
                    _repositoryRoot, ContentManifestPath.Replace('/', Path.DirectorySeparatorChar));
                var contentRoot = Path.Combine(
                    _repositoryRoot, ContentRootPath.Replace('/', Path.DirectorySeparatorChar));

                _metadata = CardEditorMetadataRegistry.LoadFile(schema);
                _index = CardDocumentIndex.LoadDirectory(cards);
                _contentCatalog = null;
                try
                {
                    _contentCatalog = ContentCatalog.LoadFile(
                        manifest, contentRoot, production: false);
                }
                catch (Exception exception) when (
                    exception is InvalidDataException
                    || exception is IOException
                    || exception is UnauthorizedAccessException)
                {
                    SetStatus(
                        "Content manifest unavailable; art IDs remain editable as semantic IDs. " +
                        exception.Message,
                        warning: true);
                }

                _visibleDocuments = new List<CardDocument>(_index.Documents);
                RefreshCardList();

                if (!string.IsNullOrWhiteSpace(previousId)
                    && _index.TryGet(previousId, out var previous))
                {
                    SelectDocument(previous, confirmDiscard: false);
                }
                else if (_visibleDocuments.Count > 0)
                {
                    SelectDocument(_visibleDocuments[0], confirmDiscard: false);
                }
                else
                {
                    ClearDocument();
                }

                SetStatus(
                    "Loaded " + _index.Documents.Count + " cards. Schema-driven fields are ready.",
                    warning: false);
            }
            catch (Exception exception) when (
                exception is InvalidDataException
                || exception is IOException
                || exception is UnauthorizedAccessException
                || exception is DirectoryNotFoundException)
            {
                _metadata = null;
                _index = null;
                _contentCatalog = null;
                _visibleDocuments.Clear();
                ClearDocument();
                ShowValidationMessage(
                    "Card Editor could not load the repository data: " + exception.Message,
                    error: true);
                SetStatus("Repository data is unavailable; no card was changed.", warning: true);
            }
        }

        private static string ResolveRepositoryRoot()
        {
            var assetsDirectory = new DirectoryInfo(Application.dataPath);
            var projectDirectory = assetsDirectory.Parent;
            var repositoryDirectory = projectDirectory?.Parent?.Parent;
            if (repositoryDirectory is null)
            {
                throw new DirectoryNotFoundException(
                    "The Dominion Wars repository root could not be resolved from the Unity project.");
            }

            var root = repositoryDirectory.FullName;
            var schema = Path.Combine(root, SchemaPath.Replace('/', Path.DirectorySeparatorChar));
            var cards = Path.Combine(root, CardsDirectory.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(schema) || !Directory.Exists(cards))
            {
                throw new DirectoryNotFoundException(
                    "Expected data/schema/cards.schema.json and data/cards under " + root + ".");
            }

            return root;
        }

        private void RefreshCardList()
        {
            if (_cardList is null) return;
            if (_index is null)
            {
                _visibleDocuments = new List<CardDocument>();
                _cardList.itemsSource = _visibleDocuments;
                _cardList.Rebuild();
                UpdateLibrarySummary();
                return;
            }

            var lifecycle = ParseLifecycleFilter(_statusFilter?.value);
            var text = _searchField?.value;
            _visibleDocuments = new List<CardDocument>(
                _index.Search(new CardDocumentSearchQuery(text: text, lifecycle: lifecycle)));
            _cardList.itemsSource = _visibleDocuments;
            _cardList.Rebuild();
            UpdateLibrarySummary();

            if (_document is null) return;
            var selectedIndex = _visibleDocuments.FindIndex(
                candidate => string.Equals(candidate.Id, _document.Id, StringComparison.Ordinal)
                    && PathsEqual(candidate.SourcePath, _document.SourcePath));
            if (selectedIndex >= 0)
            {
                _cardList.SetSelectionWithoutNotify(new[] { selectedIndex });
            }
        }

        private void UpdateLibrarySummary()
        {
            if (_librarySummary is null) return;
            var total = _index?.Documents.Count ?? 0;
            _librarySummary.text = _visibleDocuments.Count + " of " + total + " cards";
        }

        private static CardLifecycle? ParseLifecycleFilter(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == StatusAll) return null;
            return CardLifecycleCodec.TryParse(value, out var lifecycle)
                ? lifecycle
                : (CardLifecycle?)null;
        }

        private void OnLibrarySelectionChanged(IEnumerable<object> selectedItems)
        {
            var source = selectedItems?.OfType<CardDocument>().FirstOrDefault();
            if (source is null) return;
            SelectDocument(source, confirmDiscard: true);
        }

        private void SelectDocument(CardDocument source, bool confirmDiscard)
        {
            if (source is null) return;
            if (confirmDiscard && !ConfirmDiscardIfNeeded()) return;

            _document = source.Clone();
            _saveTargetPath = _document.SourcePath;
            _loadedSnapshot = CaptureSnapshot(_document);
            _undo.Clear();
            _redo.Clear();
            BuildEditorFields();
            RefreshHeader();
            RefreshArtPreview();
            ShowValidationMessage("Select Validate to check this card.", error: false);
            var selectedIndex = _visibleDocuments.FindIndex(
                candidate => string.Equals(candidate.Id, _document.Id, StringComparison.Ordinal)
                    && PathsEqual(candidate.SourcePath, _document.SourcePath));
            if (selectedIndex >= 0)
                _cardList?.SetSelectionWithoutNotify(new[] { selectedIndex });
            UpdateButtonState();
        }

        private void ClearDocument()
        {
            _document = null;
            _saveTargetPath = null;
            _loadedSnapshot = null;
            _undo.Clear();
            _redo.Clear();
            _fieldsScroll?.Clear();
            RefreshHeader();
            RefreshArtPreview();
            ShowValidationMessage("No card is selected.", error: false);
            UpdateButtonState();
        }

        private bool ConfirmDiscardIfNeeded()
        {
            if (_document is null || string.Equals(
                    _loadedSnapshot, CaptureSnapshot(_document), StringComparison.Ordinal))
            {
                return true;
            }

            return EditorUtility.DisplayDialog(
                "Discard unsaved card changes?",
                "The selected card has unsaved in-memory changes. Switching cards will discard them.",
                "Discard",
                "Cancel");
        }

        private void CreateNewDocument()
        {
            if (!ConfirmDiscardIfNeeded()) return;
            if (_metadata is null)
            {
                SetStatus("Schema metadata is unavailable; a new card cannot be created.", warning: true);
                return;
            }

            var id = NextCardId("new_card");
            var faction = FirstAllowedValue("faction");
            var type = FirstAllowedValue("type");
            if (faction is null || type is null)
            {
                SetStatus("Schema has no faction/type choices; a new card was not created.", warning: true);
                return;
            }

            _document = CardDocument.CreateNew(
                id, "New Card", faction, type, CardLifecycle.Draft);
            _saveTargetPath = null;
            _loadedSnapshot = CaptureSnapshot(_document);
            _undo.Clear();
            _redo.Clear();
            BuildEditorFields();
            RefreshHeader();
            RefreshArtPreview();
            ShowValidationMessage(
                "New Draft card created in memory. Fill required fields, then Validate and Save.",
                error: false);
            SetStatus("New card " + id + " is not saved yet.", warning: false);
            UpdateButtonState();
        }

        private void CloneCurrentDocument()
        {
            if (_document is null)
            {
                SetStatus("Select a card before cloning it.", warning: true);
                return;
            }

            if (!ConfirmDiscardIfNeeded()) return;
            var clone = CardDocument.LoadJson(_document.ToCardFileJson());
            var newId = NextCardId((_document.Id ?? "card") + "_copy");
            clone.SetString("id", newId);
            clone.SetString("name", (_document.Name ?? "Card") + " Copy");
            clone.Lifecycle = CardLifecycle.Draft;
            _document = clone;
            _saveTargetPath = null;
            _loadedSnapshot = CaptureSnapshot(_document);
            _undo.Clear();
            _redo.Clear();
            BuildEditorFields();
            RefreshHeader();
            RefreshArtPreview();
            ShowValidationMessage(
                "Cloned as " + newId + ". This copy is a Draft until explicitly promoted.",
                error: false);
            SetStatus("Clone " + newId + " is not saved yet.", warning: false);
            UpdateButtonState();
        }

        private string NextCardId(string seed)
        {
            var candidate = seed;
            var suffix = 2;
            while (_index is not null && _index.TryGet(candidate, out _))
            {
                candidate = seed + "_" + suffix;
                suffix++;
            }

            return candidate;
        }

        private string FirstAllowedValue(string key)
        {
            return _metadata.TryGetField(key, out var field)
                && field.AllowedValues.Count > 0
                ? field.AllowedValues[0]
                : null;
        }

        private void BuildEditorFields()
        {
            if (_fieldsScroll is null) return;
            _fieldControls.Clear();
            _fieldsScroll.Clear();
            if (_document is null || _metadata is null) return;

            _suppressChanges = true;
            try
            {
                _fieldsScroll.Add(BuildLifecycleRow());
                foreach (var field in _metadata.Fields)
                {
                    var row = new VisualElement();
                    row.AddToClassList("field-row");
                    var label = new Label(field.Key + (field.Required ? " (required)" : string.Empty));
                    label.AddToClassList("field-label");
                    row.Add(label);
                    var control = BuildFieldControl(field);
                    row.Add(control);
                    var help = BuildFieldHelp(field);
                    if (!string.IsNullOrWhiteSpace(help))
                    {
                        var helpLabel = new Label(help);
                        helpLabel.AddToClassList("field-help");
                        row.Add(helpLabel);
                    }

                    _fieldControls[field.Key] = control;
                    _fieldsScroll.Add(row);
                }

                var unknownFields = _document.ToJsonObject().Properties()
                    .Select(property => property.Name)
                    .Where(name => !_metadata.IsKnownCardField(name))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
                if (unknownFields.Count > 0)
                {
                    var warning = new Label(
                        "Unknown fields are retained but not editable: " +
                        string.Join(", ", unknownFields));
                    warning.AddToClassList("field-help");
                    _fieldsScroll.Add(warning);
                }
            }
            finally
            {
                _suppressChanges = false;
            }
        }

        private VisualElement BuildLifecycleRow()
        {
            var row = new VisualElement();
            row.AddToClassList("field-row");
            var label = new Label("status (editor metadata)");
            label.AddToClassList("field-label");
            row.Add(label);
            var choices = new List<string>
            {
                CardLifecycleCodec.ToWireValue(CardLifecycle.Draft),
                CardLifecycleCodec.ToWireValue(CardLifecycle.Ready),
                CardLifecycleCodec.ToWireValue(CardLifecycle.Approved),
                CardLifecycleCodec.ToWireValue(CardLifecycle.Deprecated)
            };
            var control = new DropdownField();
            control.choices = choices;
            control.SetValueWithoutNotify(
                CardLifecycleCodec.ToWireValue(_document.Lifecycle));
            control.RegisterValueChangedCallback(change =>
            {
                if (_suppressChanges
                    || !CardLifecycleCodec.TryParse(change.newValue, out var lifecycle)
                    || lifecycle == _document.Lifecycle)
                {
                    return;
                }

                PushUndo();
                _document.Lifecycle = lifecycle;
                RefreshHeader();
                ShowValidationMessage(
                    "Lifecycle changed to " + change.newValue +
                    ". Runtime JSON remains schema-compatible.",
                    error: false);
            });
            row.Add(control);
            var help = new Label(
                "Saved runtime card JSON omits this editor-only field; Approved is required for production validation.");
            help.AddToClassList("field-help");
            row.Add(help);
            return row;
        }

        private VisualElement BuildFieldControl(CardFieldMetadata field)
        {
            var token = _document.GetField(field.Key);
            if (IsStructuredEffectField(field.Key))
            {
                return BuildEffectListControl(token, field.Key);
            }

            if (string.Equals(field.Key, "artId", StringComparison.Ordinal)
                && _contentCatalog is not null)
            {
                return BuildArtIdControl(field, token);
            }

            switch (field.ValueKind)
            {
                case CardFieldValueKind.String:
                    if (field.AllowedValues.Count > 0)
                    {
                        var choices = field.AllowedValues.ToList();
                        var current = StringTokenValue(token);
                        if (!string.IsNullOrEmpty(current)
                            && !choices.Contains(current, StringComparer.Ordinal))
                        {
                            choices.Insert(0, current);
                        }

                        var dropdown = new DropdownField();
                        dropdown.choices = choices;
                        dropdown.SetValueWithoutNotify(current ?? string.Empty);
                        dropdown.RegisterValueChangedCallback(change =>
                            ApplyStringChange(field, change.newValue));
                        return dropdown;
                    }

                    var text = new TextField();
                    text.multiline = IsMultiline(field.Key);
                    text.SetValueWithoutNotify(StringTokenValue(token) ?? string.Empty);
                    text.RegisterValueChangedCallback(change =>
                        ApplyStringChange(field, change.newValue));
                    if (text.multiline) text.AddToClassList("field-json");
                    return text;

                case CardFieldValueKind.Integer:
                    var integer = new IntegerField();
                    integer.SetValueWithoutNotify(IntegerTokenValue(token));
                    integer.RegisterValueChangedCallback(change =>
                        ApplyFieldChange(field.Key, new JValue(change.newValue)));
                    return integer;

                case CardFieldValueKind.Number:
                    var number = new FloatField();
                    number.SetValueWithoutNotify(FloatTokenValue(token));
                    number.RegisterValueChangedCallback(change =>
                        ApplyFieldChange(field.Key, new JValue(change.newValue)));
                    return number;

                case CardFieldValueKind.Boolean:
                    var toggle = new Toggle();
                    toggle.SetValueWithoutNotify(BooleanTokenValue(token));
                    toggle.RegisterValueChangedCallback(change =>
                        ApplyFieldChange(field.Key, new JValue(change.newValue)));
                    return toggle;

                case CardFieldValueKind.Array:
                    return BuildArrayControl(field, token);

                case CardFieldValueKind.Object:
                case CardFieldValueKind.Unknown:
                default:
                    return BuildJsonControl(field, token, expectArray: false);
            }
        }

        private static bool IsStructuredEffectField(string fieldName)
        {
            return string.Equals(fieldName, CardEffectSpecEditor.OnPlayEffectsField, StringComparison.Ordinal)
                || string.Equals(fieldName, CardEffectSpecEditor.AmbushEffectsField, StringComparison.Ordinal)
                || string.Equals(fieldName, CardEffectSpecEditor.PunishEffectsField, StringComparison.Ordinal);
        }

        private static string EffectListControlPrefix(string fieldName)
        {
            if (string.Equals(fieldName, CardEffectSpecEditor.OnPlayEffectsField, StringComparison.Ordinal))
                return "onPlay";
            if (string.Equals(fieldName, CardEffectSpecEditor.AmbushEffectsField, StringComparison.Ordinal))
                return "ambush";
            if (string.Equals(fieldName, CardEffectSpecEditor.PunishEffectsField, StringComparison.Ordinal))
                return "punish";
            return fieldName;
        }

        private static string EffectListTitle(string fieldName)
        {
            if (string.Equals(fieldName, CardEffectSpecEditor.OnPlayEffectsField, StringComparison.Ordinal))
                return "Effects that happen when this card is played";
            if (string.Equals(fieldName, CardEffectSpecEditor.AmbushEffectsField, StringComparison.Ordinal))
                return "Effects that happen when this card is revealed from ambush";
            return "Effects that happen when this card's punish response is accepted";
        }

        private static string EffectListEmptyText(string fieldName)
        {
            if (string.Equals(fieldName, CardEffectSpecEditor.OnPlayEffectsField, StringComparison.Ordinal))
                return "No play effects configured.";
            if (string.Equals(fieldName, CardEffectSpecEditor.AmbushEffectsField, StringComparison.Ordinal))
                return "No ambush effects configured.";
            return "No punish response effects configured.";
        }

        private VisualElement BuildEffectListControl(JToken token, string fieldName)
        {
            var controlPrefix = EffectListControlPrefix(fieldName);
            var root = new VisualElement { name = fieldName + "Editor" };
            root.AddToClassList("effect-editor");

            var header = new VisualElement();
            header.AddToClassList("effect-editor-header");
            var title = new Label(EffectListTitle(fieldName));
            title.AddToClassList("effect-editor-title");
            header.Add(title);

            var add = new Button(() => AddEffect(fieldName))
            {
                name = fieldName + "AddButton",
                text = "Add effect"
            };
            add.SetEnabled(token is null || token is JArray);
            header.Add(add);
            root.Add(header);

            if (token is not null && token is not JArray)
            {
                root.Add(BuildUnsupportedEffectJson(token, fieldName + " must be an array."));
                return root;
            }

            var array = token as JArray;
            if (array is null || array.Count == 0)
            {
                var empty = new Label(EffectListEmptyText(fieldName));
                empty.AddToClassList("effect-editor-help");
                root.Add(empty);
                return root;
            }

            for (var index = 0; index < array.Count; index++)
            {
                root.Add(BuildEffectRow(array, index, fieldName, controlPrefix));
            }

            return root;
        }

        private VisualElement BuildEffectRow(
            JArray array,
            int index,
            string fieldName,
            string controlPrefix)
        {
            var row = new VisualElement
            {
                name = controlPrefix + "EffectRow_" + index
            };
            row.AddToClassList("effect-row");

            var header = new VisualElement();
            header.AddToClassList("effect-row-header");
            var title = new Label("Effect " + (index + 1).ToString());
            title.AddToClassList("effect-row-title");
            header.Add(title);

            var up = new Button(() => MoveEffect(fieldName, index, -1))
            {
                name = controlPrefix + "EffectUpButton_" + index,
                text = "Up"
            };
            up.SetEnabled(index > 0);
            header.Add(up);

            var down = new Button(() => MoveEffect(fieldName, index, 1))
            {
                name = controlPrefix + "EffectDownButton_" + index,
                text = "Down"
            };
            down.SetEnabled(index < array.Count - 1);
            header.Add(down);

            var delete = new Button(() => DeleteEffect(fieldName, index))
            {
                name = controlPrefix + "EffectDeleteButton_" + index,
                text = "Delete"
            };
            header.Add(delete);
            row.Add(header);

            var raw = array[index];
            if (raw is not JObject)
            {
                row.Add(BuildUnsupportedEffectJson(
                    raw,
                    "This entry is not an object. It is retained read-only until the schema supports it."));
                return row;
            }

            var entry = CardEffectSpecEditor.FromJsonToken(raw);
            var actionField = GetEffectField(CardEffectSpecEditor.ActionField);
            var targetField = GetEffectField(CardEffectSpecEditor.TargetField);
            var amountField = GetEffectField(CardEffectSpecEditor.AmountField);
            var paramField = GetEffectField(CardEffectSpecEditor.ParamField);
            var conditionField = GetEffectField(CardEffectSpecEditor.ConditionField);
            var kingSlayerField = GetEffectField(CardEffectSpecEditor.KingSlayerField);

            var action = BuildEffectChoice(
                "Action",
                actionField,
                entry.Action,
                emptyLabel: null,
                enabled: entry.IsSupported(_metadata, out var unsupportedReason),
                onChanged: value => ApplyEffectString(
                    fieldName, index, CardEffectSpecEditor.ActionField, value));
            row.Add(action);

            var target = BuildEffectChoice(
                "Target",
                targetField,
                entry.Target,
                emptyLabel: "(not set)",
                enabled: string.IsNullOrWhiteSpace(unsupportedReason),
                onChanged: value => ApplyEffectString(
                    fieldName, index, CardEffectSpecEditor.TargetField, value));
            row.Add(target);

            row.Add(BuildEffectInteger(
                "Amount",
                amountField,
                entry.Amount,
                enabled: string.IsNullOrWhiteSpace(unsupportedReason),
                onChanged: value => ApplyEffectInteger(
                    fieldName, index, CardEffectSpecEditor.AmountField, value)));

            row.Add(BuildEffectText(
                "Parameter",
                paramField,
                entry.Param,
                enabled: string.IsNullOrWhiteSpace(unsupportedReason),
                onChanged: value => ApplyEffectString(
                    fieldName, index, CardEffectSpecEditor.ParamField, value)));

            row.Add(BuildEffectText(
                "Condition",
                conditionField,
                entry.Condition,
                enabled: string.IsNullOrWhiteSpace(unsupportedReason),
                onChanged: value => ApplyEffectString(
                    fieldName, index, CardEffectSpecEditor.ConditionField, value)));

            if (kingSlayerField?.ValueKind == CardFieldValueKind.Boolean)
            {
                row.Add(BuildEffectBoolean(
                    "Leader bypass",
                    kingSlayerField,
                    entry.KingSlayer,
                    enabled: string.IsNullOrWhiteSpace(unsupportedReason),
                    onChanged: value => ApplyEffectBoolean(fieldName, index, value)));
            }
            else
            {
                // Keep a schema-metadata failure visible and read-only rather
                // than inventing a second editor type for a missing definition.
                row.Add(BuildEffectChoice(
                    "Leader bypass",
                    kingSlayerField,
                    entry.KingSlayer.HasValue ? (entry.KingSlayer.Value ? "true" : "false") : "(not set)",
                    emptyLabel: "(not set)",
                    enabled: false,
                    onChanged: _ => { }));
            }

            var unsupportedProperties = entry.GetUnsupportedProperties(_metadata);
            if (unsupportedProperties.Count > 0)
            {
                var warning = new Label(
                    "Unsupported fields are retained read-only: " +
                    string.Join(", ", unsupportedProperties));
                warning.AddToClassList("effect-editor-warning");
                row.Add(warning);
            }

            if (!string.IsNullOrWhiteSpace(unsupportedReason))
            {
                var warning = new Label("Unsupported effect entry retained read-only: " + unsupportedReason);
                warning.AddToClassList("effect-editor-warning");
                row.Add(warning);
            }

            return row;
        }

        private VisualElement BuildEffectChoice(
            string label,
            CardFieldMetadata? field,
            string? current,
            string? emptyLabel,
            bool enabled,
            Action<string?> onChanged)
        {
            var container = new VisualElement();
            container.AddToClassList("effect-field");
            var control = new DropdownField(label);
            var choices = field?.AllowedValues?.ToList() ?? new List<string>();
            if (!string.IsNullOrWhiteSpace(emptyLabel)) choices.Insert(0, emptyLabel);

            var hasValue = !string.IsNullOrWhiteSpace(current)
                && choices.Contains(current, StringComparer.Ordinal);
            if (!hasValue && !string.IsNullOrWhiteSpace(current))
            {
                choices.Insert(0, current);
                control.SetEnabled(false);
            }
            else if (choices.Count == 0)
            {
                control.SetEnabled(false);
            }
            else
            {
                control.SetEnabled(enabled);
            }

            control.choices = choices;
            control.SetValueWithoutNotify(
                string.IsNullOrWhiteSpace(current)
                    ? (string.IsNullOrWhiteSpace(emptyLabel) ? string.Empty : emptyLabel)
                    : current);
            control.RegisterValueChangedCallback(change =>
            {
                if (_suppressChanges) return;
                onChanged(
                    string.Equals(change.newValue, emptyLabel, StringComparison.Ordinal)
                        ? null
                        : change.newValue);
            });
            container.Add(control);
            return container;
        }

        private VisualElement BuildEffectBoolean(
            string label,
            CardFieldMetadata field,
            bool? current,
            bool enabled,
            Action<bool?> onChanged)
        {
            var container = new VisualElement();
            container.AddToClassList("effect-field");

            var control = new Toggle(label);
            control.SetValueWithoutNotify(current ?? false);
            control.SetEnabled(enabled && field.ValueKind == CardFieldValueKind.Boolean);
            control.RegisterValueChangedCallback(change =>
            {
                if (!_suppressChanges) onChanged(change.newValue);
            });
            container.Add(control);

            // An omitted optional boolean is represented as the schema's
            // default-off state until the author changes it. The source JSON
            // is not rewritten merely by rebuilding the editor controls.
            if (!current.HasValue && !field.Required)
            {
                var help = new Label("Optional boolean; off is the default when omitted.");
                help.AddToClassList("effect-editor-help");
                container.Add(help);
            }

            return container;
        }

        private VisualElement BuildEffectInteger(
            string label,
            CardFieldMetadata? field,
            int? current,
            bool enabled,
            Action<int> onChanged)
        {
            var container = new VisualElement();
            container.AddToClassList("effect-field");
            var control = new IntegerField(label);
            control.SetValueWithoutNotify(current ?? 0);
            control.SetEnabled(enabled && field is not null);
            control.RegisterValueChangedCallback(change =>
            {
                if (!_suppressChanges) onChanged(change.newValue);
            });
            container.Add(control);
            if (field is not null && (field.Minimum.HasValue || field.Maximum.HasValue))
            {
                var help = new Label(
                    "Allowed range: " + (field.Minimum?.ToString() ?? "—") + " to " +
                    (field.Maximum?.ToString() ?? "—"));
                help.AddToClassList("effect-editor-help");
                container.Add(help);
            }
            return container;
        }

        private VisualElement BuildEffectText(
            string label,
            CardFieldMetadata? field,
            string? current,
            bool enabled,
            Action<string?> onChanged)
        {
            var container = new VisualElement();
            container.AddToClassList("effect-field");
            var control = new TextField(label);
            control.SetValueWithoutNotify(current ?? string.Empty);
            control.SetEnabled(enabled && field is not null);
            control.RegisterValueChangedCallback(change =>
            {
                if (!_suppressChanges) onChanged(change.newValue);
            });
            container.Add(control);
            if (field is not null && (field.MinLength.HasValue || field.MaxLength.HasValue))
            {
                var help = new Label(
                    "Allowed length: " + (field.MinLength?.ToString() ?? "0") + " to " +
                    (field.MaxLength?.ToString() ?? "∞"));
                help.AddToClassList("effect-editor-help");
                container.Add(help);
            }
            return container;
        }

        private VisualElement BuildUnsupportedEffectJson(JToken token, string message)
        {
            var container = new VisualElement();
            container.AddToClassList("effect-unsupported");
            var help = new Label(message);
            help.AddToClassList("effect-editor-warning");
            container.Add(help);
            var raw = new TextField("Original JSON")
            {
                multiline = true,
                isReadOnly = true
            };
            raw.SetValueWithoutNotify(token?.ToString(Formatting.Indented) ?? "null");
            container.Add(raw);
            return container;
        }

        private CardFieldMetadata? GetEffectField(string fieldName)
        {
            if (_metadata is not null
                && _metadata.TryGetDefinitionField(
                    CardEffectSpecEditor.DefinitionName,
                    fieldName,
                    out var field))
            {
                return field;
            }

            return null;
        }

        private void AddEffect(string fieldName)
        {
            if (_document is null || _metadata is null) return;
            if (!TryGetEffectsArray(fieldName, out var array)) return;

            array.Add(CardEffectSpecEditor.CreateNew(_metadata).Raw);
            ApplyEffectsArray(fieldName, array);
        }

        private void DeleteEffect(string fieldName, int index)
        {
            if (_document is null || !TryGetEffectsArray(fieldName, out var array)) return;
            if (index < 0 || index >= array.Count) return;

            array.RemoveAt(index);
            ApplyEffectsArray(fieldName, array);
        }

        private void MoveEffect(string fieldName, int index, int delta)
        {
            if (_document is null || !TryGetEffectsArray(fieldName, out var array)) return;
            var newIndex = index + delta;
            if (index < 0 || index >= array.Count || newIndex < 0 || newIndex >= array.Count) return;

            ApplyEffectsArray(fieldName, CardEffectSpecEditor.Reorder(array, index, newIndex));
        }

        private void ApplyEffectString(string effectListFieldName, int index, string fieldName, string? value)
        {
            ApplyEffectField(
                effectListFieldName,
                index,
                fieldName,
                value is null ? null : new JValue(value));
        }

        private void ApplyEffectInteger(string effectListFieldName, int index, string fieldName, int value)
        {
            ApplyEffectField(effectListFieldName, index, fieldName, new JValue(value));
        }

        private void ApplyEffectBoolean(string effectListFieldName, int index, bool? value)
        {
            ApplyEffectField(
                effectListFieldName,
                index,
                CardEffectSpecEditor.KingSlayerField,
                value.HasValue ? new JValue(value.Value) : null);
        }

        private void ApplyEffectField(
            string effectListFieldName,
            int index,
            string fieldName,
            JToken? value)
        {
            if (_document is null || _metadata is null) return;
            if (!TryGetEffectsArray(effectListFieldName, out var array)) return;
            if (index < 0 || index >= array.Count || array[index] is not JObject) return;

            var entry = CardEffectSpecEditor.FromJsonToken(array[index]!);
            if (!entry.TrySetField(_metadata, fieldName, value, out var error))
            {
                ShowValidationMessage("Effect change refused: " + error, error: true);
                return;
            }

            array[index] = entry.Raw;
            ApplyEffectsArray(effectListFieldName, array);
        }

        private bool TryGetEffectsArray(string fieldName, out JArray array)
        {
            array = null;
            if (_document is null) return false;

            var token = _document.GetField(fieldName);
            if (token is null)
            {
                array = new JArray();
                return true;
            }

            if (token is JArray source)
            {
                array = (JArray)source.DeepClone();
                return true;
            }

            ShowValidationMessage(
                fieldName + " is not an array; the original value remains unchanged.",
                error: true);
            return false;
        }

        private void ApplyEffectsArray(string fieldName, JArray array)
        {
            ApplyFieldChange(fieldName, array);
            BuildEditorFields();
        }

        private VisualElement BuildArtIdControl(CardFieldMetadata field, JToken token)
        {
            var choices = _contentCatalog.Assets.Values
                .Where(asset => string.Equals(asset.Kind, "card_art", StringComparison.Ordinal))
                .Select(asset => asset.Id)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            choices.Insert(0, NoArtwork);
            var current = StringTokenValue(token);
            var resolvedCurrent = current;
            if (!string.IsNullOrEmpty(current))
            {
                try
                {
                    resolvedCurrent = _contentCatalog.ResolveAlias(current);
                }
                catch (InvalidDataException)
                {
                    resolvedCurrent = current;
                }

                if (!choices.Contains(resolvedCurrent, StringComparer.Ordinal))
                    choices.Insert(1, resolvedCurrent);
            }

            var dropdown = new DropdownField();
            dropdown.choices = choices;
            dropdown.SetValueWithoutNotify(resolvedCurrent ?? NoArtwork);
            dropdown.RegisterValueChangedCallback(change =>
            {
                if (change.newValue == NoArtwork)
                    ApplyFieldChange(field.Key, null);
                else
                    ApplyFieldChange(field.Key, new JValue(change.newValue));
                RefreshArtPreview();
            });
            return dropdown;
        }

        private VisualElement BuildArrayControl(CardFieldMetadata field, JToken token)
        {
            var array = token as JArray;
            var isStringArray = array is not null && array.All(item =>
                item.Type == JTokenType.String);
            var values = isStringArray ? array.Values<string>().ToList() : null;
            if (array is not null && values is not null)
            {
                var text = new TextField();
                text.multiline = false;
                text.SetValueWithoutNotify(string.Join(", ", values));
                text.RegisterValueChangedCallback(change =>
                    ApplyStringArrayChange(field, change.newValue));
                return text;
            }

            return BuildJsonControl(field, token, expectArray: true);
        }

        private VisualElement BuildJsonControl(CardFieldMetadata field, JToken token, bool expectArray)
        {
            var text = new TextField();
            text.multiline = true;
            text.AddToClassList("field-json");
            text.SetValueWithoutNotify(
                token is null ? string.Empty : token.ToString(Formatting.Indented));
            text.RegisterValueChangedCallback(change =>
                ApplyJsonChange(field, change.newValue, expectArray));
            return text;
        }

        private static bool IsMultiline(string key)
        {
            return string.Equals(key, "text", StringComparison.Ordinal)
                || string.Equals(key, "flavor", StringComparison.Ordinal);
        }

        private static string BuildFieldHelp(CardFieldMetadata field)
        {
            var details = new List<string>
            {
                field.ValueKind.ToString().ToLowerInvariant()
            };
            if (field.Minimum.HasValue || field.Maximum.HasValue)
            {
                details.Add(
                    "range " + (field.Minimum?.ToString() ?? "—") + "…" +
                    (field.Maximum?.ToString() ?? "—"));
            }

            if (field.MinLength.HasValue || field.MaxLength.HasValue)
            {
                details.Add(
                    "length " + (field.MinLength?.ToString() ?? "—") + "…" +
                    (field.MaxLength?.ToString() ?? "—"));
            }

            if (field.AllowedValues.Count > 0)
                details.Add("choices " + field.AllowedValues.Count);
            if (field.ItemAllowedValues.Count > 0)
                details.Add("item choices " + field.ItemAllowedValues.Count);
            if (field.DefaultValue is not null)
                details.Add("default " + field.DefaultValue.ToString(Formatting.None));
            return string.Join(" · ", details);
        }

        private void ApplyStringChange(CardFieldMetadata field, string value)
        {
            if (string.IsNullOrEmpty(value) && !field.Required)
                ApplyFieldChange(field.Key, null);
            else
                ApplyFieldChange(field.Key, new JValue(value ?? string.Empty));
        }

        private void ApplyStringArrayChange(CardFieldMetadata field, string value)
        {
            var values = (value ?? string.Empty)
                .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToList();
            if (values.Count == 0 && !field.Required)
                ApplyFieldChange(field.Key, null);
            else
                ApplyFieldChange(field.Key, new JArray(values));
        }

        private void ApplyJsonChange(CardFieldMetadata field, string value, bool expectArray)
        {
            if (string.IsNullOrWhiteSpace(value) && !field.Required)
            {
                ApplyFieldChange(field.Key, null);
                return;
            }

            try
            {
                var token = JToken.Parse(value ?? string.Empty);
                if (expectArray && token.Type != JTokenType.Array)
                    throw new InvalidDataException("Expected a JSON array.");
                if (!expectArray && field.ValueKind == CardFieldValueKind.Object
                    && token.Type != JTokenType.Object)
                {
                    throw new InvalidDataException("Expected a JSON object.");
                }

                ApplyFieldChange(field.Key, token);
            }
            catch (Exception exception) when (
                exception is JsonException || exception is InvalidDataException)
            {
                ShowValidationMessage(
                    field.Key + " is not valid JSON: " + exception.Message,
                    error: true);
            }
        }

        private void ApplyFieldChange(string key, JToken value)
        {
            if (_document is null || _suppressChanges) return;
            var old = _document.GetField(key);
            if (value is null
                ? old is null
                : JToken.DeepEquals(old, value))
            {
                return;
            }

            PushUndo();
            if (value is null) _document.RemoveField(key);
            else _document.SetField(key, value);
            RefreshHeader();
            if (string.Equals(key, "artId", StringComparison.Ordinal))
                RefreshArtPreview();
            ShowValidationMessage("Unsaved change: " + key + ".", error: false);
            UpdateButtonState();
        }

        private void PushUndo()
        {
            if (_document is null) return;
            _undo.Push(CaptureSnapshotObject(_document));
            _redo.Clear();
            UpdateButtonState();
        }

        internal void ExecuteUndoCommand()
        {
            if (_document is null || _undo.Count == 0) return;
            _redo.Push(CaptureSnapshotObject(_document));
            RestoreSnapshot(_undo.Pop());
            SetStatus("Undo applied in memory. Save when the document is valid.", warning: false);
        }

        internal void ExecuteRedoCommand()
        {
            if (_document is null || _redo.Count == 0) return;
            _undo.Push(CaptureSnapshotObject(_document));
            RestoreSnapshot(_redo.Pop());
            SetStatus("Redo applied in memory. Save when the document is valid.", warning: false);
        }

        private EditorSnapshot CaptureSnapshotObject(CardDocument document)
        {
            return new EditorSnapshot(
                document.ToCardFileJson(Formatting.None),
                document.Lifecycle,
                document.SourcePath,
                document.BaselineHash);
        }

        private static string CaptureSnapshot(CardDocument document)
        {
            return document.ToEditorJson(Formatting.None);
        }

        private void RestoreSnapshot(EditorSnapshot snapshot)
        {
            _document = CardDocument.LoadJson(
                snapshot.Json,
                snapshot.SourcePath,
                snapshot.BaselineHash);
            _document.Lifecycle = snapshot.Lifecycle;
            BuildEditorFields();
            RefreshHeader();
            RefreshArtPreview();
            ShowValidationMessage("In-memory edit restored.", error: false);
            UpdateButtonState();
        }

        private CardValidationResult ValidateCurrent(bool showStatus)
        {
            if (_document is null || _metadata is null)
            {
                ShowValidationMessage("No card is selected.", error: true);
                return null;
            }

            var validator = new CardEditorValidator(_metadata);
            var result = validator.Validate(_document, _contentCatalog, _index);
            if (showStatus)
            {
                RenderValidation(result);
                SetStatus(
                    result.IsValid
                        ? "Authoring validation passed."
                        : "Validation found " + result.Errors.Count + " error(s).",
                    warning: !result.IsValid);
            }

            return result;
        }

        private void RenderValidation(CardValidationResult result)
        {
            if (result is null)
            {
                ShowValidationMessage("No validation result.", error: true);
                return;
            }

            if (result.Issues.Count == 0)
            {
                ShowValidationMessage("PASS — no schema, semantic, or asset issues.", error: false);
                return;
            }

            var lines = result.Issues.Select(issue =>
                issue.Severity.ToString().ToUpperInvariant() + " [" + issue.Code + "] " +
                issue.Message);
            ShowValidationMessage(string.Join("\n", lines), error: !result.IsValid);
        }

        private void SaveCurrent()
        {
            var result = ValidateCurrent(showStatus: false);
            if (_document is null || result is null || !result.IsValid) 
            {
                RenderValidation(result);
                SetStatus("Save refused until authoring validation passes.", warning: true);
                return;
            }

            if (_document.Lifecycle == CardLifecycle.Approved)
            {
                var production = new CardEditorValidator(_metadata).ValidateForProduction(
                    _document, _contentCatalog, _index);
                if (!production.IsValid)
                {
                    RenderValidation(production);
                    SetStatus(
                        "Approved card cannot be saved for production until blocking validation passes.",
                        warning: true);
                    return;
                }
            }

            var targetPath = ResolveSaveTargetPath();
            if (targetPath is null) return;
            try
            {
                var directory = Path.GetDirectoryName(targetPath);
                if (string.IsNullOrEmpty(directory)) throw new InvalidDataException(
                    "Card save directory could not be resolved.");
                Directory.CreateDirectory(directory);

                CardDocumentSaveResult saveResult;
                if (IsMultiCardSource(targetPath))
                {
                    saveResult = _store.SaveCollection(_document, targetPath);
                }
                else
                {
                    saveResult = _store.Save(_document, targetPath);
                }

                _saveTargetPath = targetPath;
                _loadedSnapshot = CaptureSnapshot(_document);
                _undo.Clear();
                _redo.Clear();
                RenderValidation(result);
                var backup = saveResult.BackupPath is null
                    ? "no backup needed"
                    : "backup " + ToRepositoryRelative(saveResult.BackupPath);
                SetStatus(
                    "Saved " + ToRepositoryRelative(saveResult.Path) + " (" + backup +
                    "). Lifecycle remains editor metadata.",
                    warning: false);

                if (IsProductionCardPath(targetPath))
                    ReloadRepository(preserveSelection: true);
                else
                    RefreshHeader();
                UpdateButtonState();
            }
            catch (CardDocumentConflictException exception)
            {
                ShowValidationMessage(
                    "Save refused because the file changed outside the editor. Reload before saving.\n" +
                    exception.Message,
                    error: true);
                SetStatus("Concurrent edit detected; no file was overwritten.", warning: true);
            }
            catch (Exception exception) when (
                exception is IOException
                || exception is UnauthorizedAccessException
                || exception is InvalidDataException)
            {
                ShowValidationMessage("Save failed: " + exception.Message, error: true);
                SetStatus("Save failed; the previous file remains recoverable.", warning: true);
            }
        }

        private string ResolveSaveTargetPath()
        {
            if (_document is null) return null;
            if (!string.IsNullOrWhiteSpace(_document.SourcePath))
                return Path.GetFullPath(_document.SourcePath);
            if (!string.IsNullOrWhiteSpace(_saveTargetPath))
                return Path.GetFullPath(_saveTargetPath);
            if (!ContentManifest.IsStableId(_document.Id ?? string.Empty))
            {
                SetStatus("Save path is withheld until cardId passes stable ID validation.", warning: true);
                return null;
            }

            var relativeDirectory = _document.Lifecycle == CardLifecycle.Approved
                ? CardsDirectory
                : DraftDirectory;
            return Path.Combine(
                _repositoryRoot,
                relativeDirectory.Replace('/', Path.DirectorySeparatorChar),
                _document.Id + ".json");
        }

        private bool IsMultiCardSource(string path)
        {
            if (_index is null) return false;
            var fullPath = Path.GetFullPath(path);
            return _index.Documents.Count(document =>
                PathsEqual(document.SourcePath, fullPath)) > 1;
        }

        private bool IsProductionCardPath(string path)
        {
            var cardsRoot = Path.GetFullPath(Path.Combine(
                _repositoryRoot, CardsDirectory.Replace('/', Path.DirectorySeparatorChar)));
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(cardsRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshHeader()
        {
            if (_selectedTitle is null) return;
            if (_document is null)
            {
                _selectedTitle.text = "No card selected";
                _selectedMeta.text = string.Empty;
                _selectedPath.text = string.Empty;
                _lifecycleBadge.text = "—";
                _savePathField.SetValueWithoutNotify(string.Empty);
                return;
            }

            _selectedTitle.text = string.IsNullOrWhiteSpace(_document.Name)
                ? (_document.Id ?? "Unnamed card")
                : _document.Name;
            _selectedMeta.text = (_document.Id ?? "<missing id>") + " · " +
                (_document.Faction ?? "<missing faction>") + " · " +
                (_document.Type ?? "<missing type>");
            var path = _document.SourcePath ?? _saveTargetPath;
            _selectedPath.text = path is null
                ? "Not saved"
                : ToRepositoryRelative(path);
            var lifecycle = CardLifecycleCodec.ToWireValue(_document.Lifecycle);
            _lifecycleBadge.text = lifecycle;
            _lifecycleBadge.EnableInClassList("status-draft", _document.Lifecycle == CardLifecycle.Draft);
            _lifecycleBadge.EnableInClassList("status-ready", _document.Lifecycle == CardLifecycle.Ready);
            _lifecycleBadge.EnableInClassList("status-approved", _document.Lifecycle == CardLifecycle.Approved);
            _lifecycleBadge.EnableInClassList(
                "status-deprecated", _document.Lifecycle == CardLifecycle.Deprecated);
            _savePathField.SetValueWithoutNotify(path is null ? "Not saved" : ToRepositoryRelative(path));
        }

        private void RefreshArtPreview()
        {
            if (_artPreview is null) return;
            if (_previewTexture is not null)
            {
                DestroyImmediate(_previewTexture);
                _previewTexture = null;
            }

            if (_document is null || string.IsNullOrWhiteSpace(_document.ArtId))
            {
                _artPreview.image = null;
                _artIdLabel.text = "artId: (none)";
                _fallbackLabel.text = "No artwork selected; runtime fallback remains safe.";
                return;
            }

            var requested = _document.ArtId;
            var resolved = requested;
            ContentAsset asset = null;
            try
            {
                if (_contentCatalog is null)
                {
                    _artPreview.image = null;
                    _artIdLabel.text = "artId: " + requested;
                    _fallbackLabel.text = "Content manifest unavailable; semantic ID will be validated later.";
                    return;
                }

                resolved = _contentCatalog.ResolveAlias(requested);
                _contentCatalog.Assets.TryGetValue(resolved, out asset);
                _artIdLabel.text = "artId: " + requested +
                    (string.Equals(requested, resolved, StringComparison.Ordinal)
                        ? string.Empty
                        : " → " + resolved);

                if (asset is not null
                    && string.Equals(asset.Kind, "card_art", StringComparison.Ordinal)
                    && string.Equals(asset.SourceType, "file", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(asset.RelativePath))
                {
                    var absolutePath = GetSafeContentFilePath(asset.RelativePath);
                    if (absolutePath is not null && File.Exists(absolutePath))
                    {
                        var bytes = File.ReadAllBytes(absolutePath);
                        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (texture.LoadImage(bytes, markNonReadable: true))
                        {
                            _previewTexture = texture;
                            _artPreview.image = texture;
                            _fallbackLabel.text = "Resolved from content library.";
                            return;
                        }

                        DestroyImmediate(texture);
                    }
                }

                var fallback = _contentCatalog.ResolveFallback(requested);
                _artPreview.image = null;
                _fallbackLabel.text = "Preview unavailable; runtime fallback: " + fallback;
            }
            catch (Exception exception) when (
                exception is InvalidDataException
                || exception is IOException
                || exception is UnauthorizedAccessException)
            {
                _artPreview.image = null;
                _artIdLabel.text = "artId: " + requested;
                _fallbackLabel.text = "Fallback preview unavailable: " + exception.Message;
            }
        }

        private string GetSafeContentFilePath(string relativePath)
        {
            var root = Path.GetFullPath(Path.Combine(
                _repositoryRoot, ContentRootPath.Replace('/', Path.DirectorySeparatorChar)));
            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(root, normalized));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Asset path escaped the content root.");
            }

            return full;
        }

        private void ShowValidationMessage(string message, bool error)
        {
            if (_validationOutput is null) return;
            _validationOutput.text = message ?? string.Empty;
            _validationOutput.EnableInClassList("validation-error", error);
            _validationOutput.EnableInClassList(
                "validation-success", !error && message is not null && message.StartsWith("PASS", StringComparison.Ordinal));
            _validationOutput.EnableInClassList(
                "validation-warning", !error && message is not null && !message.StartsWith("PASS", StringComparison.Ordinal));
        }

        private void SetStatus(string message, bool warning)
        {
            if (_statusBar is null) return;
            _statusBar.text = message ?? string.Empty;
            _statusBar.EnableInClassList("validation-warning", warning);
            _statusBar.EnableInClassList("validation-success", !warning);
        }

        private void UpdateButtonState()
        {
            var hasDocument = _document is not null;
            _cloneButton?.SetEnabled(hasDocument);
            _undoButton?.SetEnabled(hasDocument && _undo.Count > 0);
            _redoButton?.SetEnabled(hasDocument && _redo.Count > 0);
            _validateButton?.SetEnabled(hasDocument && _metadata is not null);
            _saveButton?.SetEnabled(hasDocument && _metadata is not null);
        }

        private string ToRepositoryRelative(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var full = Path.GetFullPath(path);
            if (!string.IsNullOrWhiteSpace(_repositoryRoot)
                && full.StartsWith(_repositoryRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return full.Substring(_repositoryRoot.Length + 1)
                    .Replace(Path.DirectorySeparatorChar, '/');
            }

            return full;
        }

        private static bool PathsEqual(string first, string second)
        {
            if (first is null || second is null) return first == second;
            try
            {
                return string.Equals(
                    Path.GetFullPath(first),
                    Path.GetFullPath(second),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void OnDisable()
        {
            if (_previewTexture is not null)
            {
                DestroyImmediate(_previewTexture);
                _previewTexture = null;
            }
        }

        private static string FormatListItem(CardDocument document)
        {
            return (document.Id ?? "<missing id>") + "\n" +
                (document.Name ?? "<unnamed>") + "  ·  " +
                CardLifecycleCodec.ToWireValue(document.Lifecycle);
        }

        private static string StringTokenValue(JToken token)
        {
            return token?.Type == JTokenType.String ? token.ToObject<string>() : null;
        }

        private static int IntegerTokenValue(JToken token)
        {
            return token?.Type == JTokenType.Integer ? token.ToObject<int>() : 0;
        }

        private static float FloatTokenValue(JToken token)
        {
            return token is not null && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                ? token.ToObject<float>()
                : 0f;
        }

        private static bool BooleanTokenValue(JToken token)
        {
            return token?.Type == JTokenType.Boolean && token.ToObject<bool>();
        }

        private readonly struct EditorSnapshot
        {
            public EditorSnapshot(
                string json,
                CardLifecycle lifecycle,
                string sourcePath,
                string baselineHash)
            {
                Json = json;
                Lifecycle = lifecycle;
                SourcePath = sourcePath;
                BaselineHash = baselineHash;
            }

            public string Json { get; }
            public CardLifecycle Lifecycle { get; }
            public string SourcePath { get; }
            public string BaselineHash { get; }
        }
    }
}
#endif
