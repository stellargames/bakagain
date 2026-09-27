namespace BakAgain.Tests.Editor.UI {
    using BakAgain.UI;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// The markup <see cref="DialogTextFormatter"/> emits over the decoded runs.
    /// </summary>
    /// <remarks>
    /// Which characters are styled how is decided by <c>GameData.Resources.Dialog.DialogTextRuns</c>
    /// and covered by its own tests. This fixture owns the half that stayed in Unity: pen to colour,
    /// and the tag pairs.
    /// </remarks>
    public class DialogTextFormatterMarkupTests {
        // Distinct, recognisable colours so a wrong pen shows up as a wrong hex string.
        private static Color[] Palette() {
            var p = new Color[16];
            p[0] = Color.black;
            p[5] = new Color(1f, 0f, 0f);       // FF0000
            p[0x0B] = new Color(0f, 1f, 0f);    // 00FF00
            return p;
        }

        [Test]
        public void UnstyledTextGetsNoTagsAtAll() {
            // The common case, and the reason merging adjacent unstyled runs cannot change output:
            // an unstyled run emits nothing.
            Assert.AreEqual("hello there",
                DialogTextFormatter.Format("hello there", 0, 11, Palette(), 0));
        }

        [Test]
        public void AHighlightedWordIsWrappedOnceWithColourOutsideItalic() {
            // Tag ORDER is pinned: <color> outside <i>, closed in the mirror order. One pair for the
            // whole word rather than per glyph.
            const string raw = "±one two";
            Assert.AreEqual("<color=#FF0000><i>one</i></color> two",
                DialogTextFormatter.Format(raw, 0, raw.Length, Palette(), 0));
        }

        [Test]
        public void ItalicWithoutAPenChangeEmitsNoColourTag() {
            // Pen still equals the body pen, so there is nothing to colour — only <i>.
            const string raw = "≤word";
            Assert.AreEqual("<i>word</i>",
                DialogTextFormatter.Format(raw, 0, raw.Length, Palette(), 0));
        }

        [Test]
        public void APenRemapColoursWithoutItalic() {
            // 0xF5 from body pen 1 remaps to 0x0B, and never sets italic.
            const string raw = "⌡word";
            Assert.AreEqual("<color=#00FF00>word</color>",
                DialogTextFormatter.Format(raw, 0, raw.Length, Palette(), 1));
        }

        [Test]
        public void ControlCodesNeverReachTheOutput() {
            // They are ordinary CP437 characters in the source; leaking one prints a stray glyph.
            string formatted = DialogTextFormatter.Format("a≡b≤c±d⌠e⌡f", 0, 11, Palette(), 0);
            foreach (char c in "≡≤±⌠⌡") {
                Assert.IsFalse(formatted.Contains(c.ToString()), $"'{c}' leaked into the output");
            }
        }
    }
}
