namespace BakAgain.Tests.PlayMode.UI {
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
    /// THE FENCE for how flowing dialog text is laid out: at the original's line PITCH, and broken
    /// at the original's break POINTS. Both were UI Toolkit's before task-80 — the pitch was
    /// GAME.FNT's 10-row character cell where <c>RenderDialogText</c> asks for 11
    /// (<c>lineGapExtra = 1</c> at <c>0x490d1</c>), and the breaks were wherever the text engine
    /// happened to put them.
    ///
    /// <para><b>Why this measures the built tree rather than the builder's inputs.</b> Wrapping
    /// needs a resolved width, so it can only happen after layout; a test that checked
    /// <c>GameTextBlock</c> in isolation would assert against a width nothing had computed. These
    /// run a real UI Toolkit panel and read the geometry it produced.</para>
    ///
    /// <para>The throwaway <see cref="PanelSettings"/> has no theme font, so the text engine can
    /// measure nothing — which is exactly the point of the design under test. Our wrap uses
    /// GAME.FNT's own advance table, so the break points below are the game data's and hold with
    /// or without a font to render them.</para>
    /// </summary>
    public class DialogBodyLineFlowTests {
        // The shipped row-2 dialog box: VGA (13, 11, 294, 101) -> canonical, with field_9/field_A
        // text pads of 10 VGA px on each side. So the text box the original wraps inside is
        // 294 - 10 - 10 = 274 VGA px, and the panel below is that same box in canonical px.
        private const float PanelWidth = 1470f;
        private const float PanelHeight = 606f;
        private const float TextPad = 50f;
        private const int OriginalTextBoxVgaPx = 274;

        // Long enough to wrap several times inside the box above.
        private const string Body =
            "Left clicking on a member of your party will show you that person's inventory "
            + "and statistics. Right clicking will bring up more options for that character.";

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
        public IEnumerator TheBody_BreaksWhereTheOriginalBreaks_NotWhereTheTextEngineWould() {
            VisualElement body = null;
            yield return BuildAndFlow(result => body = result);

            // The wrap region is the box minus the style's text pads — the original shrinks its
            // rect the same way before laying the text out (0x49043-0x4905f).
            int maxWidth = Mathf.FloorToInt(body.resolvedStyle.width / GameData.Resources.Layout.OriginalPixel.Width);
            Assert.That(maxWidth, Is.EqualTo(OriginalTextBoxVgaPx).Within(1),
                "the body must wrap inside the original's text box (the 294 px row-2 box less its "
                + "two 10 px pads), not inside the whole panel");

            string prepared = DialogTextFormatter.Prepare(Body, centered: false);
            var expected = new List<string>();
            foreach (GameTextWrapper.Line line in GameTextWrapper.Wrap(prepared, maxWidth)) {
                expected.Add(prepared.Substring(line.Start, line.Length));
            }
            Assert.That(expected.Count, Is.GreaterThan(2),
                "the fixture text must actually wrap, or this test asserts nothing");

            List<string> rendered = RenderedLines(body);
            Assert.That(rendered, Is.EqualTo(expected),
                "the body's lines must be the ones font_WrapTextIntoLines would produce. One label "
                + "holding the whole block gives UI Toolkit's breaks instead, which is what this "
                + "replaced.");
        }

        [UnityTest]
        public IEnumerator TheBodyLines_SitElevenVgaRowsApart_NotTen() {
            VisualElement body = null;
            yield return BuildAndFlow(result => body = result);

            var tops = new List<float>();
            foreach (VisualElement child in body.Children()) {
                tops.Add(child.layout.y);
            }
            Assert.That(tops.Count, Is.GreaterThan(2), "the fixture text must actually wrap");

            // GAME.FNT's 10-row character cell (what font_DrawWrappedTextBlock reads at 0x4b94e)
            // plus RenderDialogText's lineGapExtra of 1 (0x490d1) — the sum the block advances by
            // at 0x4bb07.
            const int OriginalPitchVgaRows = 10 + GameFontText.DialogLineGapVgaRows;
            float expected = OriginalPitchVgaRows * GameData.Resources.Layout.OriginalPixel.Height;

            for (int i = 1; i < tops.Count; i++) {
                Assert.That(tops[i] - tops[i - 1], Is.EqualTo(expected).Within(0.01f),
                    "line " + i + " must sit " + expected + " canonical px below line " + (i - 1));
            }
            Assert.That(expected, Is.Not.EqualTo(GameFontText.LineHeightPx),
                "the leading must be a real one — a pitch equal to the bare character cell means "
                + "lineGapExtra was dropped, which is the 9%-tight text task-80 fixed");
        }

        [UnityTest]
        public IEnumerator ABorderedBoxCentresItsLines_TheWayTheOriginalDoes() {
            VisualElement body = null;
            yield return BuildAndFlow(result => body = result);

            // Every dialogTypeData row sets the 0x10 vertical-centre bit, and the block the
            // renderer centres is pitch * lineCount tall (0x4b9d8-0x4b9f8). The body element
            // spans the panel, so its lines must be centred within it.
            float blockHeight = 0f;
            foreach (VisualElement child in body.Children()) {
                blockHeight += child.layout.height;
            }
            float firstTop = float.NaN;
            foreach (VisualElement child in body.Children()) {
                firstTop = child.layout.y;
                break;
            }

            Assert.That(body.resolvedStyle.height, Is.EqualTo(PanelHeight).Within(0.5f),
                "a bordered body spans the panel so the centring has a box to centre in");
            Assert.That(firstTop, Is.EqualTo((PanelHeight - blockHeight) / 2f).Within(0.5f),
                "the stack of lines must sit at (boxHeight - pitch * lineCount) / 2");
        }

        // --- fixture --------------------------------------------------------------------------

        private IEnumerator BuildAndFlow(System.Action<VisualElement> report) {
            _go = new GameObject("DialogBodyFlowUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;

            VisualElement panel = DialogPanelBuilder.BuildPanel(
                new DialogEntry { Text = Body, DialogType = DialogType.Normal },
                BoxedStyle(), new DialogLayout(), new Color[256]);
            panel.style.left = 0f;
            panel.style.top = 0f;
            panel.style.width = PanelWidth;
            panel.style.height = PanelHeight;
            document.rootVisualElement.Add(panel);

            // Layout, then the geometry callback that flows the text, then layout of the lines it
            // added.
            yield return null;
            yield return null;
            yield return null;

            VisualElement body = panel.Q("BakDialogBody");
            Assert.IsNotNull(body, "the builder produced no body element");
            Assert.That(body.childCount, Is.GreaterThan(0),
                "the body never flowed — it stays empty until it knows how wide its box is, so a "
                + "zero here means the geometry callback never ran");
            report(body);
        }

        private static List<string> RenderedLines(VisualElement body) {
            var lines = new List<string>();
            foreach (VisualElement child in body.Children()) {
                lines.Add(((Label)child).text);
            }
            return lines;
        }

        // The default fallback row (2): bordered, black body text, no text shadow.
        private static DialogStyle BoxedStyle() => new DialogStyle {
            FillPenColor = 0x01,
            BorderPenColor = 0x01,
            ShadowPenColor = 0x04,
            BodyTextPenColor = 0x00,
            TextShadowPenSource = 0x00,
            DefaultArea = LayoutHint.PxRect(65f, 66f, PanelWidth, PanelHeight),
            TextPadLeft = TextPad,
            TextPadRight = TextPad,
        };
    }
}
