namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Book;
    using BakAgain.Graphics;
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using GameData.Resources.Dialog;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.TextCore.Text;
    using UnityEngine.UIElements;

    /// <summary>
    /// THE FENCE for the size at which UI text renders. Both of the facts it pins were violated by
    /// the derivation this fixture replaces — <c>Canonical.MenuFontSizePx = 8 * VgaScaleY</c>,
    /// which sized the dialog body at 48 canonical px and rendered it at roughly two-thirds of the
    /// original's scale (task-46).
    ///
    /// <para>That constant was a fabrication: there is no <b>8</b> anywhere in the original. The
    /// engine selects <c>game.fnt</c> once at boot (<c>InitializeGameHardware</c> @0x41a52 ->
    /// <c>fontSelect(pGameFont)</c>) and every UI surface draws in it; GAME.FNT's character CELL
    /// is <b>10</b> px tall, and its per-character advances are the <c>BakFontData.GameWidths</c>
    /// table. So the two things a correct size must reproduce are advance widths and cell height,
    /// and both are asserted below against those original numbers rather than against whatever
    /// constant the code currently holds.</para>
    ///
    /// <para><b>Why measure instead of comparing constants.</b> A test reading
    /// <c>label.style.fontSize == Canonical.GameFontSizePx</c> passes for ANY value of that
    /// constant — including the wrong one — because it restates the implementation. These tests
    /// run the real UI Toolkit text engine over the real font asset and compare the pixels it
    /// produces to the original's tables, so the size is pinned by the game data. Swap the font
    /// asset for one with different metrics and they go red, which is correct: the size is a
    /// property of the pair, not of the number.</para>
    /// </summary>
    [RequiresShippedGameData]
    public class GameTextMetricsTests {
        // The shared UI font: built at runtime from the player's GAME.FNT (GameFonts), the same
        // asset GameFonts.Install makes the panel's font.

        // GAME.FNT's character cell height (BakFontData.GameFontHeight). font_DrawWrappedTextBlock
        // @0x4b94e reads exactly this byte out of the selected font to set its per-line advance.
        private const int GameFontCellHeightVgaPx = 10;

        // Advance widths are whole VGA pixels in the original while the font asset's are
        // continuous, so no single size is exact for every glyph (task-45 measured the per-glyph
        // exact solutions spanning 54.9-56.3). Ten copies of one character amortise the rounding;
        // 3% then admits the real spread and still rejects the old 48 px, which renders 'M' 13%
        // narrow.
        private const float AdvanceTolerance = 0.03f;

        private FontAsset _font;

        [SetUp]
        public void BuildTheSharedUiFont() {
            using (new IsolatedResourceLocators()) {
                ResourceManagementInitializer.InitializeResourceManagement();
                GameFonts.Build();
            }
            _font = GameFonts.Game;
            Assert.That(_font, Is.Not.Null, "GameFonts.Build() produced no font from GAME.FNT.");
        }

        [Test]
        public void GameFontSize_ReproducesTheOriginalAdvanceWidths() {
            // One narrow, one middling, one wide glyph — a size that is merely close on average
            // still misses at the ends of the range.
            AssertAdvance('i');
            AssertAdvance('A');
            AssertAdvance('M');
        }

        /// <summary>
        /// The space, separately — it was the one glyph the asset got badly wrong (25/90 em, 24%
        /// under GAME.FNT's 4 px) while every other one measured within 2%, so the general
        /// advance test above passed straight over it and every line of UI text set its words too
        /// close together (task-80).
        ///
        /// <para>Measured as a DELTA between two <c>'M'</c> anchors because the text engine trims
        /// trailing whitespace: a string of bare spaces measures nothing at all.</para>
        /// </summary>
        [Test]
        public void GameFontSize_ReproducesTheOriginalSpaceAdvance() {
            const int Samples = 10;
            float expected = BakFontData.GetRawCharWidth(' ', BakFontData.GameFontIndex)
                             * Canonical.VgaScaleX;

            var probe = new Label();
            GameFontText.Apply(probe, GameFontText.AnchorX.Left, GameFontText.AnchorY.Top);
            float size = probe.style.fontSize.value.value;
            float measured =
                (Measure(size, "M" + new string(' ', Samples) + "M").x - Measure(size, "MM").x)
                / Samples;

            Assert.That(measured, Is.EqualTo(expected).Within(expected * AdvanceTolerance),
                "the space must advance " + expected + " canonical px — GAME.FNT's "
                + BakFontData.GetRawCharWidth(' ', BakFontData.GameFontIndex) + " px times the x"
                + Canonical.VgaScaleX + " horizontal factor. This is a property of the FONT ASSET's "
                + "space glyph; if it goes red, that glyph's horizontal advance was reverted.");
        }

        /// <summary>
        /// LEADING whitespace, separately again — because the two ways a text engine can lose it
        /// are different bugs and only this one is player-visible.
        ///
        /// <para>The original's tab is an advance of <c>0x14</c> raw px, which
        /// <see cref="DialogTextFormatter"/> expands into literal spaces; DDX narrative text is
        /// tab-indented per paragraph (<c>DIAL_Z00.json</c> alone has thousands of entries with a
        /// tab), so if the text engine strips or collapses whitespace at the START of a line then
        /// every narrative paragraph in the game renders flush left. That is invisible to
        /// <see cref="GameFontSize_ReproducesTheOriginalSpaceAdvance"/>, which measures spaces
        /// BETWEEN two anchors.</para>
        ///
        /// <para>Asserted through the formatter rather than against a literal five spaces, so the
        /// tab expansion and the width it must produce are pinned by the same test.</para>
        /// </summary>
        [Test]
        public void GameFontSize_KeepsTheIndentAtTheStartOfAParagraph() {
            string indented = DialogTextFormatter.Prepare("\tM", centered: false);
            Assert.That(indented, Does.StartWith(" "),
                "the formatter must expand the tab into leading spaces, or this test measures "
                + "nothing about the text engine");

            float expected = (indented.Length - 1)
                             * BakFontData.GetRawCharWidth(' ', BakFontData.GameFontIndex)
                             * Canonical.VgaScaleX;

            var probe = new Label();
            GameFontText.Apply(probe, GameFontText.AnchorX.Left, GameFontText.AnchorY.Top);
            float size = probe.style.fontSize.value.value;
            float measured = Measure(size, indented).x - Measure(size, "M").x;

            Assert.That(measured, Is.EqualTo(expected).Within(expected * AdvanceTolerance),
                "a paragraph's leading indent must survive to " + expected + " canonical px. If "
                + "this reads ~0 the text engine is trimming leading whitespace, and the fix is "
                + "to carry the indent as padding rather than as spaces — the advance itself is "
                + "correct and would be equally correct while being ignored.");
        }

        [Test]
        public void GameFontText_LinePitchIsTheOriginalCharacterCell() {
            // The vertical stretch is a transform, so it multiplies the laid-out pitch rather than
            // changing it — measure the pitch the text engine produces, then apply the stretch the
            // styling applies, and the product is what the player sees.
            var probe = new Label();
            GameFontText.Apply(probe, GameFontText.AnchorX.Left, GameFontText.AnchorY.Top);
            float stretch = probe.style.scale.value.value.y;

            float pitch = MeasuredLinePitch(probe.style.fontSize.value.value) * stretch;

            Assert.That(pitch,
                Is.EqualTo(GameFontCellHeightVgaPx * Canonical.VgaScaleY).Within(1f),
                "One line of game text must advance by GAME.FNT's 10 px character cell — "
                + GameFontCellHeightVgaPx * Canonical.VgaScaleY + " canonical px. The dialog body "
                + "rendered at 7.25 VGA rows instead of 10 for as long as it was sized by the "
                + "fabricated 8 * VgaScaleY (task-46).");
        }

        /// <summary>
        /// The connector. Both tests above pass while DialogPanelBuilder keeps its own private
        /// size — this is the one that fails if the dialog does not go through GameFontText.
        ///
        /// <para>It drives a real panel because the body only builds its line elements once it
        /// knows how wide its box is (see <c>GameTextBlock</c>); the styling under test lives on
        /// those lines. <see cref="DialogBodyLineFlowTests"/> owns the layout claims — this one
        /// only asks whether the line came out of the shared font owner.</para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheDialogBody_IsStyledByTheSharedGameFontOwner() {
            var reference = new Label();
            GameFontText.Apply(reference, GameFontText.AnchorX.Left, GameFontText.AnchorY.Top);

            var host = new GameObject("DialogBodyFontUnderTest");
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            Label body = null;
            try {
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = panelSettings;

                VisualElement panel = DialogPanelBuilder.BuildPanel(
                    new DialogEntry { Text = "Narrative body.", DialogType = DialogType.PlainWithoutBox },
                    BorderlessStyle(), new DialogLayout(), new Color[256]);
                panel.style.width = 1525f;
                panel.style.height = 450f;
                document.rootVisualElement.Add(panel);

                yield return null;
                yield return null;
                yield return null;

                body = panel.Q("BakDialogBody").Q<Label>(GameTextBlock.LineName);
                Assert.That(body, Is.Not.Null, "The builder produced no body line to measure.");
            } finally {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(panelSettings);
            }

            Assert.That(body.style.fontSize.value.value,
                Is.EqualTo(reference.style.fontSize.value.value).Within(0.001f),
                "The dialog body must render at the same size as every other game-font surface.");
            Assert.That(body.style.scale.value.value.y,
                Is.EqualTo(reference.style.scale.value.value.y).Within(0.001f),
                "The dialog body must carry the same aspect stretch — canonical space is "
                + "anisotropic (x" + Canonical.VgaScaleX + " horizontal against x"
                + Canonical.VgaScaleY + " vertical) while the font renderer scales isotropically, "
                + "so a width-correct size alone leaves the glyphs a sixth too short.");
        }

        // Width of ten copies of one character, against ten times its GAME.FNT advance converted
        // to canonical px by the horizontal factor.
        private void AssertAdvance(char ch) {
            const int Samples = 10;
            // The raw FNT advance, scaled by the VGA horizontal factor — the space UI text is laid
            // out in.
            float fntPx = BakFontData.GetRawCharWidth(ch, BakFontData.GameFontIndex);
            float expected = fntPx * Canonical.VgaScaleX * Samples;

            var probe = new Label();
            GameFontText.Apply(probe, GameFontText.AnchorX.Left, GameFontText.AnchorY.Top);
            float measured = Measure(probe.style.fontSize.value.value, new string(ch, Samples)).x;

            Assert.That(measured, Is.EqualTo(expected).Within(expected * AdvanceTolerance),
                "'" + ch + "' x" + Samples + " must be " + expected + " canonical px wide — its "
                + "GAME.FNT advance of " + fntPx + " px times the x" + Canonical.VgaScaleX
                + " horizontal factor.");
        }

        private float MeasuredLinePitch(float fontSize) {
            // Explicit line breaks, so the pitch is read off the text engine directly with no
            // dependence on where a wrap would have fallen.
            float one = Measure(fontSize, "M").y;
            float three = Measure(fontSize, "M\nM\nM").y;
            return (three - one) / 2f;
        }

        // Measured through the shared font owner rather than with a hand-set style, so whatever
        // GameFontText decides about whitespace is what these assertions see. A private
        // whiteSpace here would have kept them green through the 6.5 regression that deleted
        // every paragraph indent in the game.
        private Vector2 Measure(float fontSize, string text) {
            var label = new Label(text);
            GameFontText.Apply(label, GameFontText.AnchorX.Left, GameFontText.AnchorY.Top);
            label.style.unityFontDefinition = new StyleFontDefinition(_font);
            label.style.fontSize = fontSize;
            return label.MeasureTextSize(text, 100000f, VisualElement.MeasureMode.AtMost,
                0f, VisualElement.MeasureMode.Undefined);
        }

        private static DialogStyle BorderlessStyle() => new DialogStyle {
            FillPenColor = 0x00,
            BorderPenColor = 0x00,
            ShadowPenColor = 0x00,
            BodyTextPenColor = 0x0A,
            TextShadowPenSource = 0x02,
            DefaultArea = LayoutHint.PxRect(40f, 720f, 1525f, 450f),
            TextPadLeft = 40f,
            TextPadRight = 40f,
        };
    }
}
