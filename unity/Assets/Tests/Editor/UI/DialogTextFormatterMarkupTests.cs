namespace BakAgain.Tests.Editor.UI {
    using BakAgain.UI;
    using GameData.Resources.Dialog;
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
            string raw = DialogTextRuns.FromMarkup("<hi/>one two");
            Assert.AreEqual("<color=#FF0000><i>one</i></color> two",
                DialogTextFormatter.Format(raw, 0, raw.Length, Palette(), 0));
        }

        [Test]
        public void ItalicWithoutAPenChangeEmitsNoColourTag() {
            // Pen still equals the body pen, so there is nothing to colour — only <i>.
            string raw = DialogTextRuns.FromMarkup("<i/>word");
            Assert.AreEqual("<i>word</i>",
                DialogTextFormatter.Format(raw, 0, raw.Length, Palette(), 0));
        }

        [Test]
        public void APenRemapColoursWithoutItalic() {
            // 0xF5 from body pen 1 remaps to 0x0B, and never sets italic.
            string raw = DialogTextRuns.FromMarkup("<shift/>word");
            Assert.AreEqual("<color=#00FF00>word</color>",
                DialogTextFormatter.Format(raw, 0, raw.Length, Palette(), 1));
        }

        [Test]
        public void ControlCodesNeverReachTheOutput() {
            // They are characters in the source; leaking one prints a stray glyph.
            string raw = DialogTextRuns.FromMarkup("a<reset/>b<i/>c<hi/>d<shift2/>e<shift/>f");
            string formatted = DialogTextFormatter.Format(raw, 0, raw.Length, Palette(), 0);
            foreach (char c in new[] { DialogTextRuns.Reset, DialogTextRuns.Italic,
                         DialogTextRuns.ItalicHighlight, DialogTextRuns.RemapTwice, DialogTextRuns.RemapOnce }) {
                Assert.IsFalse(formatted.Contains(c.ToString()), $"'{c}' leaked into the output");
            }
        }

        [Test]
        public void CentredSpeechKeepsTheNewlineThatPutsItBelowTheName() {
            // C61's poem (dialog 1600104) is "#Pug#\nArrayed in flame…" with CenterText. The newline
            // after the name is what drops the body a line below the pill (DIALOG.C:576-641); a
            // two-sided Trim ate it and drew the first line through the pill.
            Assert.AreEqual("\nArrayed,\nTo shriek",
                DialogTextFormatter.Prepare("\nArrayed,\n\tTo shriek\n", centered: true));
        }
    
        [Test]
        public void TrailingWhitespaceOnlyLinesAreKeptAsLines() {
            // 0x84 ends "facts:\n\t \n\t \n\t " — the space the assessment rows are drawn into.
            // The original's wrap counts them when it centres the block, so the text sits higher;
            // trimming them centred four lines instead of seven (TASK-742). The only such record.
            string prepared = DialogTextFormatter.Prepare("facts:\n\t \n\t \n\t ", centered: false);
            Assert.AreEqual(4, prepared.Split('\n').Length);
            Assert.AreEqual("plain", DialogTextFormatter.Prepare("plain \n", centered: false), "ordinary trailing whitespace still goes");
        }
    }
}
