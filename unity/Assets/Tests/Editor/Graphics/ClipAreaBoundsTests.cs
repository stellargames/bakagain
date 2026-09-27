namespace BakAgain.Tests.Editor.Graphics {
    using BakAgain.Graphics;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// What the cutscene clip rectangle actually means.
    ///
    /// <para>The Rect's width/height slots hold <b>xMax/yMax</b>, inclusive — not extents. TTM
    /// <c>0x4004</c> writes its four parameters straight into
    /// <c>clip.xmin/ymin/xmax/ymax</c> (canassa SRC/SCRIPT/TTM.C), and
    /// <see cref="DrawingUtils.CalculateRects"/> is written to match. Nothing pinned it, and the
    /// field names say the opposite.</para>
    /// </summary>
    public class ClipAreaBoundsTests {
        // A clip of x 10..20, y 10..30 — NOT a 20x30 box at (10,10).
        private static Rect Clip(float xMax, float yMax) => new Rect(10, 10, xMax, yMax);

        [Test]
        public void TheClipsThirdSlotIsARightEDGE_NotAWidth() {
            // Draw a 100-wide image at x=0 against a clip whose xMax is 20. Read as an edge the
            // result is 21 columns (0..20 inclusive, minus the 10 clipped off the left). Read as a
            // width it would be 30, and the picture would spill ten columns past the clip.
            var (_, dest) = DrawingUtils.CalculateRects(0, 0, 100, 100, Clip(20, 30), Orientation.Normal);

            Assert.AreEqual(10, dest.x, "left edge pushed to the clip");
            Assert.AreEqual(11, dest.width, "(xMax + 1) - destX = 21 - 10");
        }

        [Test]
        public void TheClipsFourthSlotIsABottomEDGE_NotAHeight() {
            var (_, dest) = DrawingUtils.CalculateRects(0, 0, 100, 100, Clip(20, 30), Orientation.Normal);

            Assert.AreEqual(10, dest.y);
            Assert.AreEqual(21, dest.height, "(yMax + 1) - destY = 31 - 10");
        }

        [Test]
        public void TheBoundsAreInclusiveSoTheMaxColumnIsDrawn() {
            // An image sitting exactly on the clip's right edge still gets its one column. An
            // exclusive reading would drop it, which is the classic off-by-one on this command.
            var (_, dest) = DrawingUtils.CalculateRects(20, 10, 8, 8, Clip(20, 30), Orientation.Normal);

            Assert.AreEqual(20, dest.x);
            Assert.AreEqual(1, dest.width, "the xMax column itself is inside the clip");
        }

        [Test]
        public void AnImageWhollyPastTheClipGetsNothing() {
            var (_, dest) = DrawingUtils.CalculateRects(50, 10, 8, 8, Clip(20, 30), Orientation.Normal);

            Assert.LessOrEqual(dest.width, 0, "nothing of it is inside the clip");
        }

        [Test]
        public void AnImageInsideTheClipIsUntouched() {
            var (_, dest) = DrawingUtils.CalculateRects(12, 12, 4, 4, Clip(20, 30), Orientation.Normal);

            Assert.AreEqual(12, dest.x);
            Assert.AreEqual(12, dest.y);
            Assert.AreEqual(4, dest.width);
            Assert.AreEqual(4, dest.height);
        }

        [Test]
        public void TheFullFrameDefaultIsTheLastPixel_NotOnePast() {
            // CutsceneState's default clip is (0, 0, Canonical.Width - 1, Canonical.Height - 1) for
            // exactly this reason: as an edge, the last drawable pixel. Using Canonical.Width here
            // would allow one column beyond the frame.
            var full = new Rect(0, 0, Canonical.Width - 1, Canonical.Height - 1);
            var (_, dest) = DrawingUtils.CalculateRects(0, 0, Canonical.Width * 2, 10, full, Orientation.Normal);

            Assert.AreEqual(Canonical.Width, dest.width, "(width - 1) + 1 = the full frame, exactly");
        }
    }
}
