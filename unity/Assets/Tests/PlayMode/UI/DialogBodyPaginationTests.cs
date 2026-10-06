namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Tests.TestSupport;
    using System.Collections;
    using System.Collections.Generic;
    using BakAgain.Graphics;
    using BakAgain.UI;
    using GameData.Resources.Dialog;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// THE FENCE for long dialog records: the original PAGES them, it does not clip them.
    ///
    /// <para><c>dialog_render_text_with_tokens(record, text, fg, x, y, scroll_start)</c> draws from
    /// line <c>scroll_start</c>, as many as fit, and leaves <c>g_wTextWrapLinesDrawn</c> /
    /// <c>g_wTextWrapLinesRemaining</c> behind; the play loop (DIALOG.C:1361-1384) then does
    /// <c>i += drawn</c> and re-renders while any remain. Until 2026-09-12 the port laid every line
    /// out and let the panel's <c>overflow: hidden</c> cut the tail off, so the last sentence of a
    /// long record simply never appeared (TASK-389).</para>
    ///
    /// <para>The fixture is the record that proved it — <c>DIAL_Z27</c> offset 17958, id 2700045,
    /// the Mac Mordain Cadal dwarf. 467 characters, no branches, ONE authored entry: it is shown as
    /// two pages in the original, breaking mid-sentence at "Now go back the way ya come", which is
    /// what rules out "the original just authored it as two records".</para>
    ///
    /// <para>Built through a real UI Toolkit panel for the same reason
    /// <c>DialogBodyLineFlowTests</c> is: the wrap and the fit both need a RESOLVED box, and a test
    /// that poked <c>GameTextBlock</c> in isolation would be asserting against a geometry nothing
    /// computed.</para>
    /// </summary>
    public class DialogBodyPaginationTests {
        // Measures text with the game font, which is built from the player's own files (TASK-662).
        [NUnit.Framework.OneTimeSetUp]
        public void RequireShippedGameData() => ShippedGameData.RequireOrIgnore();

        // The shipped row-2 dialog box's width, as DialogBodyLineFlowTests uses it.
        private const float PanelWidth = 1470f;
        private const float TextPad = 50f;

        // Deliberately SHORT: five line pitches of room for a record that wraps to far more, so
        // there is more than one page to turn. 11 VGA rows * 6 = 66 canonical px per line.
        private const float ShortPanelHeight = 5f * 11f * GameData.Resources.Layout.OriginalPixel.Height;

        // DIAL_Z27:17958 (id 2700045), verbatim. @4 is the party-member token the formatter
        // resolves; it stays in place here because the wrap measures it at the original's metrics.
        private const string DwarfRecord =
            "\tSomeone ahead shouted.\n"
            + "\tHurrying down the corridor to see what the cause of the commotion was, @4 was "
            + "halted by an angry looking dwarf.\n"
            + "\t\"Na further, lad!\" the dwarf drawled. \"I don't care what Naddur may or may not "
            + "'ave told you about what is going on down 'ere. We've enough ta do ta clear out this "
            + "mess without yar interferin'. Now go back the way ya come and let us get about our "
            + "work.\"\n"
            + "\tWhen the dwarf couldn't be reasoned with, @4 loped back down the corridor.";

        private GameObject _go;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_go != null) {
                Object.DestroyImmediate(_go);
            }
            if (_panelSettings != null) {
                Object.DestroyImmediate(_panelSettings);
            }
        }

        [UnityTest]
        public IEnumerator AShortBox_DrawsOnlyWhatFits_AndReportsTheRest() {
            GameTextBlock body = null;
            yield return BuildAndFlow(result => body = result);

            // The block starts un-paginated, so what it has laid out right now IS the full wrap.
            int total = body.childCount;
            Assert.That(total, Is.GreaterThan(5),
                "the fixture must overflow a five-line box, or this test asserts nothing");

            body.Paginate = true;
            yield return null;

            Assert.That(body.LinesDrawn, Is.EqualTo(5),
                "a box five pitches tall draws five lines — the original's 'as many as fit'");
            Assert.That(body.LinesRemaining, Is.EqualTo(total - 5),
                "the rest must be REPORTED, not silently dropped: the play loop pages on this "
                + "count (g_wTextWrapLinesRemaining)");
            Assert.That(body.childCount, Is.EqualTo(5),
                "and only the drawn lines may exist as labels — laying all of them out and hiding "
                + "the overflow is exactly the defect this replaced");
        }

        [UnityTest]
        public IEnumerator TurningThePages_ShowsEveryLine_Once_AndInOrder() {
            GameTextBlock body = null;
            yield return BuildAndFlow(result => body = result);

            // Captured BEFORE pagination, from the same block: the full wrap through the same
            // formatting path, so the expectation cannot differ from the pages by markup.
            List<string> expected = RenderedLines(body);
            body.Paginate = true;
            yield return null;

            var seen = new List<string>();
            var guard = 0;
            while (true) {
                seen.AddRange(RenderedLines(body));
                if (body.LinesRemaining == 0) {
                    break;
                }
                Assert.That(++guard, Is.LessThan(20), "pagination did not terminate");
                body.AdvancePage();
                yield return null;
            }

            Assert.That(guard, Is.GreaterThan(0),
                "the fixture must take more than one page, or nothing about paging is tested");
            Assert.That(seen, Is.EqualTo(expected),
                "every wrapped line must be shown exactly once, in order — a page that advanced by "
                + "anything other than LinesDrawn would repeat or skip one");
        }

        [UnityTest]
        public IEnumerator WithoutPagination_EveryLineIsLaidOut_AsBefore() {
            GameTextBlock body = null;
            yield return BuildAndFlow(result => body = result);

            // The block only pages when something drives it: a renderer that clipped on its own,
            // with nobody calling AdvancePage, would LOSE the tail rather than hide it. Every
            // other user of GameTextBlock (the temple's quote) relies on this.
            Assert.That(body.childCount, Is.EqualTo(WrappedLineCount(body)),
                "an un-paginated block must still lay out every line");
            Assert.That(body.LinesRemaining, Is.EqualTo(0),
                "and report nothing remaining, so a caller that never opts in sees no pages");
        }

        [UnityTest]
        public IEnumerator TheShippedBox_SplitsTheDwarfRecord_WhereTheOriginalSplitsIt() {
            GameTextBlock body = null;
            yield return BuildAndFlow(result => body = result, ShippedRow2Height, ShippedRow2Style());

            int total = body.childCount;
            body.Paginate = true;
            yield return null;

            // Row 2 is VGA (13, 11, 294, 101) with 3-row insets top and bottom, so the original
            // lays the text out in 95 rows, and TEXTWRAP.C's "pitch * n - ls <= max_height" fits
            // EIGHT lines of 10+1. Measured in the original on this record: page 1 ends at "Now go
            // back the way ya come" — the eighth line — and page 2 carries the rest.
            //
            // Paging on the full 101-row panel instead gives nine, which is what this port did
            // until the insets and the trailing-gap rule were both read out of the source.
            Assert.That(body.LinesDrawn, Is.EqualTo(8),
                "the shipped row-2 box fits eight lines, not nine: 101 rows LESS the style's "
                + "field_7/field_8 insets of 3 each");
            Assert.That(body.LinesRemaining, Is.EqualTo(total - 8));

            string lastOfPage1 = ((Label)body[body.childCount - 1]).text;
            Assert.That(lastOfPage1, Does.Contain("Now go back the way ya come"),
                "and it must break where the original breaks — mid-sentence, which is the whole "
                + "reason this record is the fixture");

            body.AdvancePage();
            yield return null;
            Assert.That(body.LinesRemaining, Is.EqualTo(0), "the rest is one further page");
            string lastOfPage2 = ((Label)body[body.childCount - 1]).text;
            Assert.That(lastOfPage2, Does.Contain("back down the corridor"));
        }

        // --- fixture --------------------------------------------------------------------------

        // The shipped row-2 dialog box: VGA (13, 11, 294, 101) -> canonical, field_7/field_8 = 3.
        private const float ShippedRow2Height = 606f;
        private const float ShippedPadVga = 3f;

        private static DialogStyle ShippedRow2Style() {
            DialogStyle style = BoxedStyle();
            style.DefaultArea = LayoutHint.PxRect(65f, 66f, PanelWidth, ShippedRow2Height);
            style.TextPadTop = ShippedPadVga * GameData.Resources.Layout.OriginalPixel.Height;
            style.TextPadBottom = ShippedPadVga * GameData.Resources.Layout.OriginalPixel.Height;
            return style;
        }

        private IEnumerator BuildAndFlow(System.Action<GameTextBlock> report,
            float panelHeight = ShortPanelHeight, DialogStyle style = null) {
            _go = new GameObject("DialogBodyPaginationUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;

            VisualElement panel = DialogPanelBuilder.BuildPanel(
                new DialogEntry { Text = DwarfRecord, DialogType = DialogType.Normal },
                style ?? BoxedStyle(), new DialogLayout(), new Color[256]);
            panel.style.left = 0f;
            panel.style.top = 0f;
            panel.style.width = PanelWidth;
            panel.style.height = panelHeight;
            document.rootVisualElement.Add(panel);

            yield return null;
            yield return null;
            yield return null;

            var body = panel.Q("BakDialogBody") as GameTextBlock;
            Assert.IsNotNull(body, "the builder produced no body block");
            Assert.That(body.childCount, Is.GreaterThan(0),
                "the body never flowed — it stays empty until it knows how wide its box is");
            report(body);
        }

        // The wrap the block itself would produce, computed from the SAME resolved width so the
        // expectation cannot drift from the box under test.
        private static List<string> WrappedLines(VisualElement body) {
            int maxWidth = Mathf.FloorToInt(body.resolvedStyle.width / GameData.Resources.Layout.OriginalPixel.Width);
            string prepared = DialogTextFormatter.Prepare(DwarfRecord, centered: false);
            var lines = new List<string>();
            foreach (GameTextWrapper.Line line in GameTextWrapper.Wrap(prepared, maxWidth)) {
                lines.Add(prepared.Substring(line.Start, line.Length));
            }
            return lines;
        }

        private static int WrappedLineCount(VisualElement body) => WrappedLines(body).Count;

        private static List<string> RenderedLines(VisualElement body) {
            var lines = new List<string>();
            foreach (VisualElement child in body.Children()) {
                lines.Add(((Label)child).text);
            }
            return lines;
        }

        private static DialogStyle BoxedStyle() => new DialogStyle {
            FillPenColor = 0x01,
            BorderPenColor = 0x01,
            ShadowPenColor = 0x04,
            BodyTextPenColor = 0x00,
            TextShadowPenSource = 0x00,
            DefaultArea = LayoutHint.PxRect(65f, 66f, PanelWidth, ShortPanelHeight),
            TextPadLeft = TextPad,
            TextPadRight = TextPad,
        };
    }
}
