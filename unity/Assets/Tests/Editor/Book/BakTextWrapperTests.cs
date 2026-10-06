namespace BakAgain.Tests.Editor.Book {
    using BakAgain.Tests.TestSupport;
    using BakAgain.Book;
    using NUnit.Framework;

    public class BakTextWrapperTests {

        // Measures text with the game font, which is built from the player's own files (TASK-662).

        [NUnit.Framework.OneTimeSetUp]

        public void RequireShippedGameData() => ShippedGameData.RequireOrIgnore();

        [Test]
        public void WrapText_ShortLine_NoBreak() {
            string result = BakTextWrapper.WrapText("Hello world", 534, 504);
            Assert.AreEqual("Hello world", result);
        }

        [Test]
        public void WrapText_BreaksAtWordBoundary() {
            // "One by one he tended" at width 100 should break before a word
            string text = "One by one he tended";
            string result = BakTextWrapper.WrapText(text, 100, 100);
            // Each line should be <= 100px using BOOK.FNT widths
            string[] lines = result.Split('\n');
            Assert.Greater(lines.Length, 1, "Text should wrap to multiple lines");
            foreach (string line in lines) {
                int w = MeasureWidth(line);
                Assert.LessOrEqual(w, 100, $"Line \"{line}\" is {w}px, exceeds 100px");
            }
        }

        [Test]
        public void WrapText_BreaksAfterHyphen() {
            // "did---fine" should be able to break after the hyphens
            string text = "did---fine Seigneur Locklear replied";
            string result = BakTextWrapper.WrapText(text, 120, 120);
            // Should break after "---" since hyphen is breakable
            Assert.That(result, Does.Contain("\n"));
        }

        [Test]
        public void WrapText_BreaksAfterPeriod() {
            // Period is a breakable character
            string text = "salve. His practiced hand was steady";
            string result = BakTextWrapper.WrapText(text, 200, 200);
            string[] lines = result.Split('\n');
            // Should break after "salve." if text is too wide
            foreach (string line in lines) {
                Assert.LessOrEqual(MeasureWidth(line), 200);
            }
        }

        [Test]
        public void WrapText_SkipsLeadingSpacesOnContinuation() {
            string text = "word1 word2 word3";
            // Force break between word1 and word2
            // word1=45px, space=8, word2=49px, word3=49px
            // At width 60: "word1" fits (45), adding " word2" makes it 102 → break
            string result = BakTextWrapper.WrapText(text, 60, 60);
            string[] lines = result.Split('\n');
            Assert.Greater(lines.Length, 1);
            // Continuation lines should not start with spaces
            for (int i = 1; i < lines.Length; i++) {
                Assert.IsFalse(lines[i].StartsWith(" "),
                    $"Line {i} starts with space: \"{lines[i]}\"");
            }
        }

        [Test]
        public void WrapText_FirstLineNarrower() {
            // First line at 100px, subsequent at 534px
            string text = "This is a test of the first line being narrower than subsequent lines of text.";
            string result = BakTextWrapper.WrapText(text, 534, 100);
            string[] lines = result.Split('\n');
            Assert.Greater(lines.Length, 1);
            // First line should be <= 100px
            Assert.LessOrEqual(MeasureWidth(lines[0]), 100);
            // Subsequent lines can be wider (up to 534)
            if (lines.Length > 1)
                Assert.LessOrEqual(MeasureWidth(lines[1]), 534);
        }

        [Test]
        public void WrapText_C11BOK_FirstParagraph_MatchesOriginal() {
            // C11.BOK page 1, first paragraph (within reserved area).
            // Widths are canonical book px: 481 raw EGA px × EgaScaleX(2) = 962.
            string text = "lood soaked rags collected at the boy's feet.";
            string result = BakTextWrapper.WrapText(text, 962, 962);
            // This text is 708 canonical px (354 raw EGA × 2), fits in one line of 962.
            Assert.AreEqual(text, result, "Short paragraph should not wrap");
        }

        [Test]
        public void WrapText_C11BOK_SecondParagraph_LineBreaks() {
            // C11.BOK page 1, second paragraph. Canonical book px:
            // 534 raw EGA × 2 = 1068 available, 504 × 2 = 1008 first line.
            string text = "One by one he tended the wincing soldier's purple wounds, "
                + "stitched, salved, bandaged, did what little he could in the lee "
                + "of an old stone barn now serving as a field hospital.";
            string result = BakTextWrapper.WrapText(text, 1068, 1008);
            string[] lines = result.Split('\n');
            Assert.AreEqual(3, lines.Length, "Should wrap to exactly 3 lines");
            // Verify last characters of each line match original game
            Assert.That(lines[0], Does.EndWith(","), "Line 1 should end with comma");
            Assert.That(lines[1], Does.EndWith("lee"), "Line 2 should end with 'lee'");
            Assert.That(lines[2], Does.EndWith("hospital."), "Line 3 should end with 'hospital.'");
        }

        [Test]
        public void WrapText_C11BOK_ThirdParagraph_LineBreaks() {
            // Third paragraph of C11.BOK page 1. Canonical book px:
            // 534 raw EGA × 2 = 1068 available, 504 × 2 = 1008 first line.
            string text = "Fingers slick with alum ointment, he worked fervently to "
                + "tie off a catgut cord, then brushed the injury with a light "
                + "touch of healing salve. His practiced hand was steady, his "
                + "hazel eyes bright with concentration.";
            string result = BakTextWrapper.WrapText(text, 1068, 1008);
            string[] lines = result.Split('\n');
            Assert.AreEqual(4, lines.Length, "Should wrap to exactly 4 lines");
            Assert.That(lines[0], Does.EndWith("to"));
            Assert.That(lines[1], Does.EndWith("light"));
            Assert.That(lines[2], Does.EndWith("his"));
        }

        [Test]
        public void WrapText_ExplicitNewline_PreservesBreak() {
            string text = "Line one.\nLine two.";
            string result = BakTextWrapper.WrapText(text, 534, 504);
            Assert.AreEqual("Line one.\nLine two.", result);
        }

        [Test]
        public void WrapText_EmptyString_ReturnsEmpty() {
            string result = BakTextWrapper.WrapText("", 534, 504);
            Assert.AreEqual("", result);
        }

        [Test]
        public void BakFontData_SpaceWidth_IsCanonical() {
            // Space is 8 raw EGA px; GetCharWidth returns canonical book px (×EgaScaleX = 2).
            Assert.AreEqual(16, BakFontData.GetCharWidth(' ', 0));
        }

        [Test]
        public void BakFontData_M_IsCanonical() {
            // 'M' is 15 raw EGA px; GetCharWidth returns canonical book px (×EgaScaleX = 2).
            Assert.AreEqual(30, BakFontData.GetCharWidth('M', 0));
        }

        [Test]
        public void BakFontData_UnknownChar_ReturnsZero() {
            Assert.AreEqual(0, BakFontData.GetCharWidth('\t', 0));
        }

        private static int MeasureWidth(string text) {
            int w = 0;
            foreach (char ch in text)
                w += BakFontData.GetCharWidth(ch, 0);
            return w;
        }
    }
}
