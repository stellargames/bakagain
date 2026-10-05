namespace BakAgain.Tests.PlayMode.UI.Localization {
    using BakAgain.UI;
    using NUnit.Framework;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>A button caption too wide for its rect shrinks to fit; one that fits is untouched (TASK-779).</summary>
    public class CaptionFitTests {
        private GameObject _go;
        private PanelSettings _panel;

        [TearDown]
        public void TearDown() {
            Object.Destroy(_go);
            Object.Destroy(_panel);
        }

        [UnityTest]
        public IEnumerator ACaptionWiderThanItsButtonShrinks_AndOneThatFitsDoesNot() {
            _go = new GameObject("CaptionFit");
            var doc = _go.AddComponent<UIDocument>();
            _panel = ScriptableObject.CreateInstance<PanelSettings>();
            doc.panelSettings = _panel;
            yield return null;

            var narrow = new VisualElement { style = { width = 120, height = 60, position = Position.Absolute } };
            var wide = new VisualElement { style = { width = 1200, height = 60, top = 100, position = Position.Absolute } };
            doc.rootVisualElement.Add(narrow);
            doc.rootVisualElement.Add(wide);
            Label squeezed = GameFontText.Caption(narrow, "[Stàrt Ñéw Gàmé ~~~~]");
            Label roomy = GameFontText.Caption(wide, "Restore");
            for (int i = 0; i < 10; i++) {
                yield return null;
            }

            float full = GameFontText.FontSizePx;
            Assert.Less(squeezed.resolvedStyle.fontSize, full, "the long caption was not shrunk");
            Assert.GreaterOrEqual(squeezed.resolvedStyle.fontSize, full * GameFontText.MinFitScale - 0.5f);
            Assert.AreEqual(full, roomy.resolvedStyle.fontSize, 0.5f, "a caption that fits keeps the font size");
        }
    }
}
