#if UNITY_EDITOR
using System.Linq;
using System.Reflection;
using DominionWars.Data.CardEditor;
using DominionWars.Unity.EditorTools.CardEditor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace DominionWars.Unity.EditMode
{
    public sealed class CardEditorEffectSpecEditModeTests
    {
        [Test]
        public void OnPlayEffectsUsesSchemaDrivenStructuredControls()
        {
            var window = ScriptableObject.CreateInstance<CardEditorWindow>();
            try
            {
                window.Show();
                Assert.DoesNotThrow(() => window.CreateGUI());
                SelectCardWithEffect(window, "flame_strike");
                var editor = window.rootVisualElement.Q<VisualElement>("onPlayEffectsEditor");
                Assert.That(editor, Is.Not.Null);

                var row = editor!.Query<VisualElement>(className: "effect-row")
                    .ToList()
                    .FirstOrDefault();
                Assert.That(row, Is.Not.Null, "The selected fixture card should expose one on-play effect.");
                var dropdowns = row!.Query<DropdownField>().ToList();
                Assert.That(dropdowns.Count, Is.EqualTo(2), "Only schema enum fields should use dropdowns.");
                Assert.That(dropdowns[0].choices, Does.Contain("DAMAGE"));
                Assert.That(dropdowns[1].choices, Does.Contain("ENEMY_TARGET"));
                Assert.That(dropdowns[0].enabledSelf, Is.True);
                Assert.That(dropdowns[1].enabledSelf, Is.True);
                Assert.That(row.Query<IntegerField>().ToList().FirstOrDefault(), Is.Not.Null);
                Assert.That(row.Query<TextField>().ToList().Count, Is.GreaterThanOrEqualTo(2));
                var kingSlayer = row.Query<Toggle>().ToList().SingleOrDefault();
                Assert.That(kingSlayer, Is.Not.Null, "Boolean metadata must render a Toggle.");
                Assert.That(kingSlayer!.value, Is.True, "The existing true value must be read into the Toggle.");
                Assert.That(kingSlayer.enabledSelf, Is.True, "An existing true value must remain editable.");
                Assert.That(row.Q<Button>("onPlayEffectUpButton_0"), Is.Not.Null);
                Assert.That(row.Q<Button>("onPlayEffectDownButton_0"), Is.Not.Null);
                Assert.That(row.Q<Button>("onPlayEffectDeleteButton_0"), Is.Not.Null);
                Assert.That(editor.Q<Button>("onPlayEffectsAddButton"), Is.Not.Null);
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void KingSlayerToggleEditIsDirtyAndUndoable()
        {
            var window = ScriptableObject.CreateInstance<CardEditorWindow>();
            try
            {
                window.Show();
                Assert.DoesNotThrow(() => window.CreateGUI());
                SelectCardWithEffect(window, "flame_strike");

                var toggle = FindKingSlayerToggle(window);
                Assert.That(toggle.value, Is.True);
                toggle.value = false;

                Assert.That(window.rootVisualElement.Q<Button>("undoButton")!.enabledSelf, Is.True);
                Assert.That(
                    window.rootVisualElement.Q<Label>("validationOutput")!.text,
                    Does.Contain("Unsaved change"));

                window.ExecuteUndoCommand();
                var restored = FindKingSlayerToggle(window);
                Assert.That(restored.value, Is.True, "Undo must restore the original true value.");
                Assert.That(window.rootVisualElement.Q<Button>("redoButton")!.enabledSelf, Is.True);

                window.ExecuteRedoCommand();
                var redone = FindKingSlayerToggle(window);
                Assert.That(redone.value, Is.False, "Redo must reapply the edited false value.");
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void AmbushEffectsUsesTheSameSchemaDrivenStructuredControls()
        {
            var window = ScriptableObject.CreateInstance<CardEditorWindow>();
            try
            {
                window.Show();
                Assert.DoesNotThrow(() => window.CreateGUI());
                SelectCardWithEffect(window, "flame_ambush_counter");

                var editor = window.rootVisualElement.Q<VisualElement>("ambushEffectsEditor");
                Assert.That(editor, Is.Not.Null);
                var row = editor!.Query<VisualElement>(className: "effect-row")
                    .ToList()
                    .SingleOrDefault();
                Assert.That(row, Is.Not.Null, "The selected real card should expose one ambush effect.");

                var dropdowns = row!.Query<DropdownField>().ToList();
                Assert.That(dropdowns.Count, Is.EqualTo(2), "Ambush effects must reuse the EffectSpec enum controls.");
                Assert.That(dropdowns[0].choices, Does.Contain("DAMAGE"));
                Assert.That(dropdowns[1].choices, Does.Contain("ENEMY_MINION"));
                Assert.That(row.Query<IntegerField>().ToList().Single().value, Is.EqualTo(2));
                Assert.That(row.Q<Button>("ambushEffectUpButton_0"), Is.Not.Null);
                Assert.That(row.Q<Button>("ambushEffectDownButton_0"), Is.Not.Null);
                Assert.That(row.Q<Button>("ambushEffectDeleteButton_0"), Is.Not.Null);
                Assert.That(editor.Q<Button>("ambushEffectsAddButton"), Is.Not.Null);
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void AmbushEffectAmountEditSupportsUndoAndRedo()
        {
            var window = ScriptableObject.CreateInstance<CardEditorWindow>();
            try
            {
                window.Show();
                Assert.DoesNotThrow(() => window.CreateGUI());
                SelectCardWithEffect(window, "flame_ambush_counter");

                var amount = window.rootVisualElement
                    .Q<VisualElement>("ambushEffectsEditor")!
                    .Query<IntegerField>()
                    .ToList()
                    .Single();
                amount.value = 7;

                Assert.That(window.rootVisualElement.Q<Button>("undoButton")!.enabledSelf, Is.True);
                Assert.That(
                    window.rootVisualElement.Q<Label>("validationOutput")!.text,
                    Does.Contain("Unsaved change: ambushEffects."));

                window.ExecuteUndoCommand();
                Assert.That(
                    window.rootVisualElement.Q<VisualElement>("ambushEffectsEditor")!.Query<IntegerField>().ToList().Single().value,
                    Is.EqualTo(2));

                window.ExecuteRedoCommand();
                Assert.That(
                    window.rootVisualElement.Q<VisualElement>("ambushEffectsEditor")!.Query<IntegerField>().ToList().Single().value,
                    Is.EqualTo(7));
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void PunishEffectsUsesTheSameSchemaControlsAndKeepsPrintedPunishSeparate()
        {
            var window = ScriptableObject.CreateInstance<CardEditorWindow>();
            try
            {
                window.Show();
                Assert.DoesNotThrow(() => window.CreateGUI());
                SelectCardWithEffect(window, "flame_berserker");

                var document = GetDocument(window);
                var beforeOnPlay = TokenText(document, CardEffectSpecEditor.OnPlayEffectsField);
                var beforeAmbush = TokenText(document, CardEffectSpecEditor.AmbushEffectsField);
                var editor = window.rootVisualElement.Q<VisualElement>("punishEffectsEditor");
                Assert.That(editor, Is.Not.Null);
                var row = editor!.Query<VisualElement>(className: "effect-row")
                    .ToList()
                    .Single();
                var dropdowns = row.Query<DropdownField>().ToList();
                Assert.That(dropdowns.Count, Is.EqualTo(2),
                    "Punish effects must reuse the EffectSpec action/target controls.");
                Assert.That(dropdowns[0].choices, Does.Contain("DAMAGE"));
                Assert.That(dropdowns[1].choices, Does.Contain("ENEMY_FACE"));
                Assert.That(row.Query<IntegerField>().ToList().Single().value, Is.EqualTo(2));

                var amount = row.Query<IntegerField>().ToList().Single();
                amount.value = 7;

                Assert.That(TokenText(GetDocument(window), "punish"), Is.EqualTo("3"),
                    "Editing punishEffects must not edit the printed punish value.");
                Assert.That(TokenText(GetDocument(window), CardEffectSpecEditor.OnPlayEffectsField),
                    Is.EqualTo(beforeOnPlay));
                Assert.That(TokenText(GetDocument(window), CardEffectSpecEditor.AmbushEffectsField),
                    Is.EqualTo(beforeAmbush));
                Assert.That(window.rootVisualElement.Q<Label>("validationOutput")!.text,
                    Does.Contain("Unsaved change: punishEffects."));

                window.ExecuteUndoCommand();
                Assert.That(
                    window.rootVisualElement.Q<VisualElement>("punishEffectsEditor")!
                        .Query<IntegerField>().ToList().Single().value,
                    Is.EqualTo(2));
                Assert.That(TokenText(GetDocument(window), "punish"), Is.EqualTo("3"));

                window.ExecuteRedoCommand();
                Assert.That(
                    window.rootVisualElement.Q<VisualElement>("punishEffectsEditor")!
                        .Query<IntegerField>().ToList().Single().value,
                    Is.EqualTo(7));
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void EmptyPunishEffectsCanAddAndUndoBackToAnEmptyArray()
        {
            var window = ScriptableObject.CreateInstance<CardEditorWindow>();
            try
            {
                window.Show();
                Assert.DoesNotThrow(() => window.CreateGUI());
                SelectCardWithEffect(window, "flame_assassin");

                var editor = window.rootVisualElement.Q<VisualElement>("punishEffectsEditor");
                Assert.That(editor, Is.Not.Null);
                Assert.That(editor!.Query<VisualElement>(className: "effect-row").ToList(), Is.Empty);
                Assert.That(editor.Q<Button>("punishEffectsAddButton")!.enabledSelf, Is.True);

                InvokePrivate(window, "AddEffect", CardEffectSpecEditor.PunishEffectsField);
                Assert.That(EffectCount(GetDocument(window), CardEffectSpecEditor.PunishEffectsField), Is.EqualTo(1));
                Assert.That(
                    window.rootVisualElement.Q<VisualElement>("punishEffectsEditor")!
                        .Query<VisualElement>(className: "effect-row").ToList(),
                    Has.Count.EqualTo(1));

                window.ExecuteUndoCommand();
                var restored = GetDocument(window);
                Assert.That(EffectCount(restored, CardEffectSpecEditor.PunishEffectsField), Is.EqualTo(0));
                Assert.That(
                    window.rootVisualElement.Q<VisualElement>("punishEffectsEditor")!
                        .Query<VisualElement>(className: "effect-row").ToList(),
                    Is.Empty);
                Assert.That(TokenText(restored, CardEffectSpecEditor.PunishEffectsField), Is.EqualTo("[]"));
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void PunishEffectReorderChangesIdentityOrderAndSupportsUndoRedo()
        {
            var window = ScriptableObject.CreateInstance<CardEditorWindow>();
            try
            {
                window.Show();
                Assert.DoesNotThrow(() => window.CreateGUI());
                SelectCardWithEffect(window, "machine_punish_core");

                var document = GetDocument(window);
                var original = EffectEntries(document, CardEffectSpecEditor.PunishEffectsField);
                Assert.That(original, Has.Count.EqualTo(2));
                Assert.That(CardEffectSpecEditor.Read(document, CardEffectSpecEditor.PunishEffectsField)[0].Action,
                    Is.EqualTo("DAMAGE"));
                Assert.That(CardEffectSpecEditor.Read(document, CardEffectSpecEditor.PunishEffectsField)[1].Action,
                    Is.EqualTo("DRAW"));

                InvokePrivate(window, "MoveEffect", CardEffectSpecEditor.PunishEffectsField, 0, 1);
                var moved = EffectEntries(GetDocument(window), CardEffectSpecEditor.PunishEffectsField);
                Assert.That(CardEffectSpecEditor.Read(GetDocument(window), CardEffectSpecEditor.PunishEffectsField)[0].Action,
                    Is.EqualTo("DRAW"));
                Assert.That(CardEffectSpecEditor.Read(GetDocument(window), CardEffectSpecEditor.PunishEffectsField)[1].Action,
                    Is.EqualTo("DAMAGE"));

                window.ExecuteUndoCommand();
                Assert.That(EffectEntries(GetDocument(window), CardEffectSpecEditor.PunishEffectsField), Is.EqualTo(original));
                window.ExecuteRedoCommand();
                Assert.That(EffectEntries(GetDocument(window), CardEffectSpecEditor.PunishEffectsField), Is.EqualTo(moved));
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void AmbushEffectListOperationsPreserveOrderAndSupportUndo()
        {
            var window = ScriptableObject.CreateInstance<CardEditorWindow>();
            try
            {
                window.Show();
                Assert.DoesNotThrow(() => window.CreateGUI());
                SelectCardWithEffect(window, "flame_ambush_counter");

                var documentField = typeof(CardEditorWindow).GetField(
                    "_document",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(documentField, Is.Not.Null);
                var document = (CardDocument)documentField!.GetValue(window)!;
                InvokePrivate(window, "AddEffect", CardEffectSpecEditor.AmbushEffectsField);
                Assert.That(
                    window.rootVisualElement.Q<VisualElement>("ambushEffectsEditor")!
                        .Query<VisualElement>(className: "effect-row").ToList(),
                    Has.Count.EqualTo(2));

                InvokePrivate(window, "MoveEffect", CardEffectSpecEditor.AmbushEffectsField, 0, 1);
                Assert.That(EffectCount(document, CardEffectSpecEditor.AmbushEffectsField), Is.EqualTo(2));

                InvokePrivate(window, "DeleteEffect", CardEffectSpecEditor.AmbushEffectsField, 0);
                Assert.That(EffectCount(document, CardEffectSpecEditor.AmbushEffectsField), Is.EqualTo(1));
                Assert.That(window.rootVisualElement.Q<Button>("undoButton")!.enabledSelf, Is.True);
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void AmbushEffectReorderChangesEffectIdentityOrderAndSupportsUndoRedo()
        {
            var window = ScriptableObject.CreateInstance<CardEditorWindow>();
            try
            {
                window.Show();
                Assert.DoesNotThrow(() => window.CreateGUI());
                SelectCardWithEffect(window, "flame_ambush_counter");

                var document = GetDocument(window);
                InvokePrivate(window, "AddEffect", CardEffectSpecEditor.AmbushEffectsField);
                InvokePrivate(
                    window,
                    "ApplyEffectString",
                    CardEffectSpecEditor.AmbushEffectsField,
                    1,
                    CardEffectSpecEditor.ActionField,
                    "HEAL");
                InvokePrivate(
                    window,
                    "ApplyEffectString",
                    CardEffectSpecEditor.AmbushEffectsField,
                    1,
                    CardEffectSpecEditor.TargetField,
                    "FRIENDLY_MINION");

                var originalOrder = EffectEntries(document, CardEffectSpecEditor.AmbushEffectsField);
                Assert.That(originalOrder, Has.Count.EqualTo(2));
                var originalEffects = CardEffectSpecEditor.Read(document, CardEffectSpecEditor.AmbushEffectsField);
                Assert.That(originalEffects[0].Action, Is.EqualTo("DAMAGE"));
                Assert.That(originalEffects[0].Target, Is.EqualTo("ENEMY_MINION"));
                Assert.That(originalEffects[1].Action, Is.EqualTo("HEAL"));
                Assert.That(originalEffects[1].Target, Is.EqualTo("FRIENDLY_MINION"));

                InvokePrivate(window, "MoveEffect", CardEffectSpecEditor.AmbushEffectsField, 0, 1);
                var movedOrder = EffectEntries(document, CardEffectSpecEditor.AmbushEffectsField);
                var movedEffects = CardEffectSpecEditor.Read(document, CardEffectSpecEditor.AmbushEffectsField);
                Assert.That(movedEffects[0].Action, Is.EqualTo("HEAL"));
                Assert.That(movedEffects[0].Target, Is.EqualTo("FRIENDLY_MINION"));
                Assert.That(movedEffects[1].Action, Is.EqualTo("DAMAGE"));
                Assert.That(movedEffects[1].Target, Is.EqualTo("ENEMY_MINION"));

                window.ExecuteUndoCommand();
                Assert.That(
                    EffectEntries(GetDocument(window), CardEffectSpecEditor.AmbushEffectsField),
                    Is.EqualTo(originalOrder),
                    "Undo must restore the original action/target order.");

                window.ExecuteRedoCommand();
                Assert.That(
                    EffectEntries(GetDocument(window), CardEffectSpecEditor.AmbushEffectsField),
                    Is.EqualTo(movedOrder),
                    "Redo must restore the moved action/target order.");
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void EditingAmbushFieldsDoesNotChangeOnPlayEffects()
        {
            var window = ScriptableObject.CreateInstance<CardEditorWindow>();
            try
            {
                window.Show();
                Assert.DoesNotThrow(() => window.CreateGUI());
                SelectCardWithEffect(window, "flame_strike");

                var document = GetDocument(window);
                var onPlayBefore = EffectEntries(document, CardEffectSpecEditor.OnPlayEffectsField);
                Assert.That(onPlayBefore, Has.Count.GreaterThan(0), "The fixture card must have on-play effects.");

                InvokePrivate(window, "AddEffect", CardEffectSpecEditor.AmbushEffectsField);
                InvokePrivate(
                    window,
                    "ApplyEffectString",
                    CardEffectSpecEditor.AmbushEffectsField,
                    0,
                    CardEffectSpecEditor.ActionField,
                    "HEAL");
                InvokePrivate(
                    window,
                    "ApplyEffectString",
                    CardEffectSpecEditor.AmbushEffectsField,
                    0,
                    CardEffectSpecEditor.TargetField,
                    "FRIENDLY_MINION");
                InvokePrivate(
                    window,
                    "ApplyEffectInteger",
                    CardEffectSpecEditor.AmbushEffectsField,
                    0,
                    CardEffectSpecEditor.AmountField,
                    3);

                Assert.That(
                    EffectEntries(document, CardEffectSpecEditor.OnPlayEffectsField),
                    Is.EqualTo(onPlayBefore),
                    "Editing ambush effects must not mutate on-play effects.");
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        private static void SelectCardWithEffect(CardEditorWindow window, string cardId)
        {
            var list = window.rootVisualElement.Q<ListView>("cardList");
            Assert.That(list, Is.Not.Null);
            var index = list!.itemsSource
                .Cast<CardDocument>()
                .ToList()
                .FindIndex(document => document.Id == cardId);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "Fixture card was not indexed: " + cardId);
            list.SetSelection(index);
        }

        private static Toggle FindKingSlayerToggle(CardEditorWindow window)
        {
            var editor = window.rootVisualElement.Q<VisualElement>("onPlayEffectsEditor");
            Assert.That(editor, Is.Not.Null);
            var row = editor!.Query<VisualElement>(className: "effect-row").ToList().FirstOrDefault();
            Assert.That(row, Is.Not.Null);
            var toggle = row!.Query<Toggle>().ToList().SingleOrDefault();
            Assert.That(toggle, Is.Not.Null, "EffectSpec boolean field should render as a Toggle.");
            return toggle!;
        }

        private static void InvokePrivate(CardEditorWindow window, string methodName, params object[] arguments)
        {
            var method = typeof(CardEditorWindow).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "CardEditorWindow method was not found: " + methodName);
            method!.Invoke(window, arguments);
        }

        private static CardDocument GetDocument(CardEditorWindow window)
        {
            var documentField = typeof(CardEditorWindow).GetField(
                "_document",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(documentField, Is.Not.Null);
            return (CardDocument)documentField!.GetValue(window)!;
        }

        private static System.Collections.Generic.IReadOnlyList<string> EffectEntries(CardDocument document, string fieldName)
        {
            var effects = CardEffectSpecEditor.Read(document, fieldName);
            // Keep EditMode UI tests independent from the JSON token assembly.
            // Exact unknown-token preservation is covered by the .NET Data
            // test suite, where Newtonsoft.Json is an explicit dependency.
            return effects.Select(EffectSummary).ToList();
        }

        private static string EffectSummary(CardEffectSpecEditor effect)
        {
            return string.Join(
                "|",
                effect.IsObject ? "object" : "unsupported",
                effect.Action ?? "<null>",
                effect.Target ?? "<null>",
                effect.Amount?.ToString() ?? "<null>",
                effect.Param ?? "<null>",
                effect.Condition ?? "<null>",
                effect.KingSlayer?.ToString() ?? "<null>");
        }

        private static int EffectCount(CardDocument document, string fieldName)
        {
            return CardEffectSpecEditor.Read(document, fieldName).Count;
        }

        private static string TokenText(CardDocument document, string fieldName)
        {
            var getField = typeof(CardDocument).GetMethod("GetField");
            Assert.That(getField, Is.Not.Null);
            return getField!.Invoke(document, new object[] { fieldName })?.ToString();
        }

    }
}
#endif
