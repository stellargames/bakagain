namespace BakAgain.Tests.UI {
    using BakAgain.Tests.TestSupport;
    using NUnit.Framework;
    using System.Collections;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// What an already-asked keyword topic looks like — the one thing TASK-146 could not close on
    /// reading, because a class with no rule behind it and a class with one look identical in code.
    /// </summary>
    /// <remarks>
    /// <b>The original builds it as widget type 8</b>, which shares <c>widget_draw_text_button</c>
    /// with type 6 and differs only in the UNPRESSED branch: text pen <c>0x0a</c> becomes pen
    /// <c>1</c> and the drop shadow is dropped (WIDGET.C:373-376). Same chrome, same fill, same
    /// edges — and the pressed branch does not read the type at all, which is why an asked topic
    /// still presses.
    ///
    /// <para><b>This has churned before.</b> The effect was once an inline <c>opacity = 0.6</c>
    /// standing in for "a different element kind", and the class carried no rule at all — so the
    /// inline style was the whole effect. Asserting the RESOLVED colours against the real theme is
    /// what makes the current rule provable rather than plausible.</para>
    ///
    /// <para>Both declarations live in one <c>.keyword-asked</c> block, so a matching selector
    /// carries <c>text-shadow: none</c> with the colour. <c>textShadow</c> is only on
    /// <c>computedStyle</c>, which returns by ref and cannot be read by reflection, so the colour
    /// is the observable and the shared selector is the argument.</para>
    /// </remarks>
    public class KeywordAskedStyleTests {
        // The theme's two text pens, from ClassicTheme.tss.
        private static readonly Color32 Parchment = new Color32(219, 199, 121, 255); // pen 0x0a
        private static readonly Color32 Shadow = new Color32(85, 69, 44, 255);       // pen 1

        private GameObject _host;
        private VisualElement _plain;
        private VisualElement _asked;

        /// <summary>
        /// <b>A frame has to elapse before <c>resolvedStyle</c> means anything.</b>
        /// </summary>
        /// <remarks>
        /// Reading it in the same frame the elements are created returns defaults — black text —
        /// because the panel has not run a style pass yet. The interactive Editor hid this: the
        /// probe that first verified the rule created the elements in one <c>execute_code</c> call
        /// and read them in another, so a frame had gone by. <b>The batch gate is what caught it</b>,
        /// which is the reverse of the usual direction (TASK-198) and worth the note.
        /// </remarks>
        private IEnumerator Build() {
            Setup();
            yield return null;
            yield return null;
        }

        private void Setup() {
            // The game's own PanelSettings, so the theme resolving here is the one the game uses.
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                AssetDatabase.GUIDToAssetPath("e1ac2bea2a13f4a4abb255bc71d7cc98"));
            Assert.NotNull(settings, "the shared PanelSettings asset");
            Assert.NotNull(settings.themeStyleSheet, "PanelSettings carries a theme");

            _host = new GameObject("~keywordStyleTest");
            UIDocument doc = _host.AddComponent<UIDocument>();
            doc.panelSettings = settings;

            var plain = new Button { text = "Rusalka" };
            plain.AddToClassList("text-button");
            var asked = new Button { text = "Rusalka" };
            asked.AddToClassList("text-button");
            asked.AddToClassList("keyword-asked");
            doc.rootVisualElement.Add(plain);
            doc.rootVisualElement.Add(asked);
            _plain = plain;
            _asked = asked;
        }

        [TearDown]
        public void TearDown() {
            if (_host != null) {
                Object.DestroyImmediate(_host);
            }
        }

        [UnityTest]
        public IEnumerator AnAskedTopicDropsToTheOtherTextPen() {
            yield return Build();
            Assert.AreEqual((Color)Parchment, _plain.resolvedStyle.color,
                "an unasked topic is pen 0x0a");
            Assert.AreEqual((Color)Shadow, _asked.resolvedStyle.color,
                "an asked one is pen 1");
            Assert.AreNotEqual(_plain.resolvedStyle.color, _asked.resolvedStyle.color);
        }

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator EverythingELSEAboutItIsUnchanged() {
            yield return Build();
            // "Same chrome, same fill, same edges." A dimmed or greyed-out button would fail here,
            // and dimming is exactly what this used to do.
            Assert.AreEqual(_plain.resolvedStyle.backgroundColor, _asked.resolvedStyle.backgroundColor);
            Assert.AreEqual(_plain.resolvedStyle.borderTopColor, _asked.resolvedStyle.borderTopColor);
            Assert.AreEqual(_plain.resolvedStyle.borderBottomColor, _asked.resolvedStyle.borderBottomColor);
            Assert.AreEqual(_plain.resolvedStyle.borderLeftColor, _asked.resolvedStyle.borderLeftColor);
            Assert.AreEqual(_plain.resolvedStyle.borderRightColor, _asked.resolvedStyle.borderRightColor);
            Assert.AreEqual(_plain.resolvedStyle.fontSize, _asked.resolvedStyle.fontSize);
            Assert.AreEqual(_plain.resolvedStyle.opacity, _asked.resolvedStyle.opacity,
                "not a dimmed button — opacity is untouched");
        }
    }
}
