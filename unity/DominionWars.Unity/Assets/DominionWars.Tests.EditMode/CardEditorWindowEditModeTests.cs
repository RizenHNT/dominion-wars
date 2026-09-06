#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using DominionWars.Unity.EditorTools.CardEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DominionWars.Unity.EditMode
{
    public sealed class CardEditorWindowEditModeTests
    {
        [Test]
        public void CardEditorAssetsImportAndWindowBindsRepository()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                CardEditorWindow.UxmlAssetPath);
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                CardEditorWindow.UssAssetPath);
            Assert.That(visualTree, Is.Not.Null, "Card Editor UXML did not import.");
            Assert.That(styleSheet, Is.Not.Null, "Card Editor USS did not import.");

            var window = ScriptableObject.CreateInstance<CardEditorWindow>();
            try
            {
                Assert.DoesNotThrow(() => window.CreateGUI());
                Assert.That(window.rootVisualElement.Q<ListView>("cardList"), Is.Not.Null);
                Assert.That(window.rootVisualElement.Q<ScrollView>("fieldsScroll"), Is.Not.Null);
                Assert.That(window.rootVisualElement.Q<Image>("artPreview"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void CardEditorArtPickerLoadsAssetAndShowsFallbackForProgrammaticArtwork()
        {
            var window = ScriptableObject.CreateInstance<CardEditorWindow>();
            try
            {
                window.Show();
                Assert.DoesNotThrow(() => window.CreateGUI());
                var picker = window.rootVisualElement
                    .Query<DropdownField>()
                    .ToList()
                    .SingleOrDefault(control => control.choices.Contains("card_art_flame_leader"));
                Assert.That(picker, Is.Not.Null, "Card Editor did not expose the content-library art picker.");

                picker!.value = "card_art_flame_leader";
                var preview = window.rootVisualElement.Q<Image>("artPreview");
                var fallback = window.rootVisualElement.Q<Label>("fallbackLabel");
                Assert.That(
                    preview.image,
                    Is.Not.Null,
                    "The selected file-backed card art did not preview. picker=" + picker.value + ", fallback=" + fallback.text);
                Assert.That(fallback.text, Is.EqualTo("Resolved from content library."));

                picker.value = "placeholder_card_art_neutral";
                Assert.That(preview.image == null, Is.True, "Programmatic fallback art should not invent a file preview.");
                Assert.That(fallback.text, Does.Contain("runtime fallback: placeholder_card_art_neutral"));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }
    }
}
#endif
