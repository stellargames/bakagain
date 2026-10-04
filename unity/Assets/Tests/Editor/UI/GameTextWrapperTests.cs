namespace BakAgain.Tests.Editor.UI {
    using System.Collections.Generic;
    using BakAgain.UI;
    using GameData.Resources.Dialog;
    using NUnit.Framework;

    /// <summary>
    /// The fence for <see cref="GameTextWrapper"/> — our port of <c>font_WrapTextIntoLines</c>
    /// (<c>0x4b74c</c>), the routine every dialog in the original breaks its lines with.
    ///
    /// <para><b>Widths come from the game data, not from this file.</b> Every case derives its box
    /// width by measuring a prefix of its own input with <see cref="GameTextWrapper.Measure"/>, so
    /// the numbers say "wide enough for exactly this much" rather than restating GAME.FNT's table.
    /// The behaviour under test is where the break FALLS, which is the half the engine decides.</para>
    /// </summary>
    public class GameTextWrapperTests {
        // --- fitting and the plain space break ----------------------------------------------

        [Test]
        public void TextThatFits_IsOneLine() {
            Assert.That(Lines("Hello world", Width("Hello world")), Is.EqualTo(new[] { "Hello world" }));
        }

        [Test]
        public void TheBreakSpace_IsOnNeitherLine() {
            // Wide enough for "aaa " but not the 'b' after it, so the line must break at that
            // space — and the space itself belongs to neither line. The original walks back over
            // the break run before recording the end (0x4b850-0x4b868); an implementation that
            // stopped at the first break point instead would leave "aaa " with its trailing space,
            // which right-aligned and centred lines would then visibly mis-centre.
            Assert.That(Lines("aaa bbb", Width("aaa ")), Is.EqualTo(new[] { "aaa", "bbb" }));
        }

        [Test]
        public void AWholeRunOfSpaces_IsSkipped_NotTurnedIntoBlankLines() {
            Assert.That(Lines("aaa     bbb", Width("aaa     ")), Is.EqualTo(new[] { "aaa", "bbb" }));
        }

        [Test]
        public void AWordTooWideForTheBox_IsCutWhereItOverflows() {
            // No break point anywhere on the line, so there is nothing to walk back to and the
            // original cuts mid-word (the var_1 branch at 0x4b830). Four characters fit.
            Assert.That(Lines("aaaaaaaa", Width("aaaa")), Is.EqualTo(new[] { "aaaa", "aaaa" }));
        }

        [Test]
        public void ACharacterThatExactlyFills_TheBox_Fits() {
            // `if (width + advance > maxWidth) break` — greater, not greater-or-equal (0x4b79f).
            Assert.That(Lines("abcd efgh", Width("abcd")), Is.EqualTo(new[] { "abcd", "efgh" }));
        }

        // --- hard newlines --------------------------------------------------------------------

        [Test]
        public void AnExplicitNewline_EndsTheLine_AndIsConsumed() {
            Assert.That(Lines("ab\ncd", 1000), Is.EqualTo(new[] { "ab", "cd" }));
        }

        [Test]
        public void ConsecutiveNewlines_KeepTheBlankLineBetweenThem() {
            // The paragraph break in narrative dialog text. Each '\n' ends a line, so the middle
            // one yields an empty range — which must survive as a line, or the vertical rhythm of
            // every multi-paragraph dialog collapses.
            Assert.That(Lines("ab\n\ncd", 1000), Is.EqualTo(new[] { "ab", string.Empty, "cd" }));
        }

        // --- the ellipsis rule ----------------------------------------------------------------

        [Test]
        public void ALetterAfterAnEllipsis_IsABreakPoint() {
            // The surprising half of sub_ovr145_0 (0x4b71b-0x4b744): a break is allowed before a
            // LETTER that follows three literal '.' characters. It is the only break point in
            // this string — there is no space in it at all — so a wrapper that only knew about
            // spaces would cut "Thenlongword" mid-word instead.
            const string Text = "Wait...Thenlong";
            Assert.That(Lines(Text, Width("Wait...Thenlo")),
                Is.EqualTo(new[] { "Wait...", "Thenlong" }));
        }

        [Test]
        public void TheEllipsisBreak_NeedsMoreThanThreeCharactersBehindIt() {
            // `if (at - lineStart <= 3) return 0` (0x4b716). Here the letter after the dots sits
            // at index 3, so the rule cannot fire and the line is cut where it overflows.
            const string Text = "...Thenlongword";
            Assert.That(Lines(Text, Width("...Thenlo")),
                Is.EqualTo(new[] { "...Thenlo", "ngword" }));
        }

        [Test]
        public void TheEllipsisIsNotADotRule_TwoDotsDoNotBreak() {
            // Exactly three dots, not "one or more" — the original unrolls a three-iteration
            // check. Two dots must leave the string unbreakable.
            const string Text = "Wait..Thenlongword";
            Assert.That(Lines(Text, Width("Wait..Thenlo")),
                Is.EqualTo(new[] { "Wait..Thenlo", "ngword" }));
        }

        // --- the inline formatting codes ------------------------------------------------------

        [Test]
        public void InlineFormattingCodes_OccupyNoWidth_AndBreakNothing() {
            // DDX's per-word italic/highlight markers are bytes >= 0x80, which getCharMetrics
            // (0x1613e) sends down its out-of-range branch for a width of 0 — so they must not
            // move a single break. The two strings differ only by the markers and must wrap
            // identically. If they were measured at some fallback width instead, the marked-up
            // string would break earlier.
            const string Plain = "Into a Dark Night";
            string Marked = DialogTextRuns.FromMarkup("<hi/>Into <hi/>a <hi/>Dark <hi/>Night");
            int width = Width("Into a Dark ");

            List<string> plain = Lines(Plain, width);
            List<string> marked = Lines(Marked, width);

            Assert.That(plain, Is.EqualTo(new[] { "Into a Dark", "Night" }));
            Assert.That(Strip(marked), Is.EqualTo(plain),
                "the markers must not have moved the break");
        }

        [Test]
        public void AFormattingCode_IsNotABreakPoint() {
            // Not a space and not a letter, so sub_ovr145_0 rejects it. With no other break point
            // in the line the wrap must fall back to cutting where it overflowed.
            string Text = DialogTextRuns.FromMarkup("aaaa<hi/>aaaa");
            Assert.That(Strip(Lines(Text, Width("aaaa"))), Is.EqualTo(new[] { "aaaa", "aaaa" }));
        }

        // --- the line budget ------------------------------------------------------------------

        [Test]
        public void TheLineCount_IsCappedAtTheOriginalsArraySize() {
            // 90 entries (the `push 5Ah` at 0x4b966). Text past the cap is simply not laid out.
            string text = string.Join("\n", new string[GameTextWrapper.MaxLines + 20]);
            Assert.That(GameTextWrapper.Wrap(text, 1000).Count, Is.EqualTo(GameTextWrapper.MaxLines));
        }

        [Test]
        public void ACallerCanAskForFewerLines() {
            Assert.That(GameTextWrapper.Wrap("a\nb\nc\nd", 1000, maxLines: 2).Count, Is.EqualTo(2));
        }

        // --- degenerate input -----------------------------------------------------------------

        [Test]
        public void EmptyInput_ProducesNoLines() {
            Assert.That(GameTextWrapper.Wrap(string.Empty, 100), Is.Empty);
            Assert.That(GameTextWrapper.Wrap(null, 100), Is.Empty);
        }

        [Test]
        public void AZeroWidthBox_TerminatesOnTheLineBudget() {
            // Nothing can ever fit, so every line ends empty at the same character and the scan
            // never advances — in the original too (it re-enters at 0x4b75e with `di` unchanged).
            // What stops it is the line budget, and nothing else, which is the whole reason this
            // is asserted rather than assumed: GameTextBlock refuses to flow a box narrower than
            // one VGA pixel precisely because this is where such a box would land.
            Assert.That(GameTextWrapper.Wrap("abc", 0).Count, Is.EqualTo(GameTextWrapper.MaxLines));
        }

        // --- measuring ------------------------------------------------------------------------

        [Test]
        public void Measure_IsThePlainSumOfTheAdvanceTable() {
            // getStringWidthInPixels (0x15be5) adds nothing between characters, so a string's
            // width is exactly its parts'.
            Assert.That(GameTextWrapper.Measure("abcdef", 0, 6),
                Is.EqualTo(GameTextWrapper.Measure("abc", 0, 3) + GameTextWrapper.Measure("def", 0, 3)));
        }

        // --- helpers --------------------------------------------------------------------------

        private static int Width(string prefix) => GameTextWrapper.Measure(prefix, 0, prefix.Length);

        private static List<string> Lines(string text, int maxWidth) {
            var result = new List<string>();
            foreach (GameTextWrapper.Line line in GameTextWrapper.Wrap(text, maxWidth)) {
                result.Add(text.Substring(line.Start, line.Length));
            }
            return result;
        }

        private static List<string> Strip(List<string> lines) {
            var result = new List<string>();
            foreach (string line in lines) {
                result.Add(line.Replace(DialogTextRuns.ItalicHighlight.ToString(), string.Empty));
            }
            return result;
        }
    }
}
