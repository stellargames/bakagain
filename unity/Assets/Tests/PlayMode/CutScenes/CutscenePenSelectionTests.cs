namespace BakAgain.Tests.PlayMode.CutScenes {
    using BakAgain.CutScenes;
    using BakAgain.CutScenes.AnimationCommands;
    using BakAgain.Graphics;
    using GameData.Resources.Animation.FrameCommands;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Which of <c>SetColors</c>' two pens each drawing command uses.
    /// </summary>
    /// <remarks>
    /// <b>TTM <c>0x2002</c> sets two different pens and the names do not tell you which is which.</b>
    /// Its first word goes to <c>bGfx_outline_color</c> and <c>bText_fg_color</c>; its second goes to
    /// <c>bGfx_fill_color</c>. Our model calls them <c>ForegroundColor</c> and
    /// <c>BackgroundColor</c> in that order, so <b>the "background" pen is the one a fill paints
    /// with</b> — which reads backwards and is exactly how both consumers came to be swapped.
    ///
    /// <para><c>0xa104</c> (FillArea) sets <c>bGfx_fill_enabled = 1</c> before
    /// <c>draw_rect_filled</c> and <c>0xa114</c> (DrawBorder) clears it, and the routine picks its
    /// pen from that flag — fill colour for the interior, outline colour for the edge.</para>
    ///
    /// <para><b>This was a live defect.</b> 131 of the 248 shipped <c>SetColors</c> are
    /// <c>(6, 0)</c>, and every one of the 131 <c>FillArea</c> commands that runs under a differing
    /// pair was painting pen 6 where the original paints pen 0. The border half is inverted too but
    /// invisible in shipped data: all 16 <c>DrawBorder</c> uses run under a pair whose pens are
    /// equal.</para>
    /// </remarks>
    public class CutscenePenSelectionTests {
        private const int FullPalette = 256;
        private const int OutlinePen = 6;   // SetColors' FIRST word, as the shipped scripts use it
        private const int FillPen = 3;      // its SECOND

        private GameObject _host;
        private CutsceneState _state;

        private static Color[] Palette() {
            var c = new Color[FullPalette];
            for (var i = 0; i < FullPalette; i++) {
                // The index is recoverable from the red channel, so a sampled pixel names its pen.
                c[i] = new Color(i / 255f, 0f, 0f, 1f);
            }
            return c;
        }

        [SetUp]
        public void SetUp() {
            _host = new GameObject("CutscenePenHost");
            RawImage raw = _host.AddComponent<RawImage>();
            raw.texture = new Texture2D(64, 64);
            _state = new CutsceneState(new UiImage(raw), Palette());
        }

        [TearDown]
        public void TearDown() {
            _state?.Dispose();
            if (_host != null) {
                Object.DestroyImmediate(_host);
            }
        }

        private void SetPens(int first, int second) =>
            new SetColors { ForegroundColor = first, BackgroundColor = second }.ToAction()(_state);

        private static int PenAt(Texture source, int x, int y) {
            var rt = (RenderTexture)source;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var probe = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            probe.ReadPixels(new Rect(x, y, 1, 1), 0, 0);
            probe.Apply();
            RenderTexture.active = previous;
            float r = probe.GetPixel(0, 0).r;
            Object.DestroyImmediate(probe);
            return Mathf.RoundToInt(r * 255f);
        }

        [Test]
        public void SetColorsPutsItsFirstWordInForegroundAndSecondInBackground() {
            SetPens(OutlinePen, FillPen);

            Assert.AreEqual(OutlinePen, _state.ForegroundColorIndex);
            Assert.AreEqual(FillPen, _state.BackgroundColorIndex);
        }

        [Test]
        public void AFillPaintsWithTheSECONDPen_theFillColour() {
            // *** THE REGRESSION TEST. *** Before this was fixed the interior came out in pen 6
            // (the outline pen) for every one of the 131 shipped fills under SetColors(6, 0).
            SetPens(OutlinePen, FillPen);
            new FillArea { X = 0, Y = 0, Width = Canonical.Width, Height = Canonical.Height }
                .ToAction()(_state);

            RenderTexture buffer = _state.CurrentIndexedBuffer;
            Assert.AreEqual(FillPen, PenAt(buffer, buffer.width / 2, buffer.height / 2),
                "the interior takes bGfx_fill_color, which is SetColors' second word");
        }

        [Test]
        public void ABorderDrawsWithTheFIRSTPen_theOutlineColour() {
            SetPens(OutlinePen, FillPen);
            new FillArea { X = 0, Y = 0, Width = Canonical.Width, Height = Canonical.Height }
                .ToAction()(_state);
            new DrawBorder { X = 0, Y = 0, Width = Canonical.Width, Height = Canonical.Height }
                .ToAction()(_state);

            // The fill above gives the border something to be distinguishable from: the edge must
            // now read as the outline pen while the middle still reads as the fill pen.
            RenderTexture buffer = _state.CurrentIndexedBuffer;
            Assert.AreEqual(OutlinePen, PenAt(buffer, 0, 0),
                "the edge takes bGfx_outline_color, which is SetColors' first word");
            Assert.AreEqual(FillPen, PenAt(buffer, buffer.width / 2, buffer.height / 2),
                "and the border did not repaint the interior");
        }

        [Test]
        public void TheTwoCommandsUseDIFFERENTPensAsTheOriginalDoes() {
            // The claim that actually matters, stated once so a future swap cannot pass by moving
            // both reads together.
            SetPens(OutlinePen, FillPen);

            Assert.AreNotEqual(_state.ForegroundColorIndex, _state.BackgroundColorIndex,
                "the fixture is only meaningful while the two pens differ");
        }
    }
}
