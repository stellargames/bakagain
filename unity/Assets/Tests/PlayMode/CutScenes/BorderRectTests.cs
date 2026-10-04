namespace BakAgain.Tests.PlayMode.CutScenes {
    using BakAgain.Graphics;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// The four edge rectangles of a cutscene border — <c>DrawingUtils.GetBorderRects</c>.
    /// </summary>
    /// <remarks>
    /// <b>The area's Width and Height are EXTENTS here</b>, unlike the clip area's, whose fields are
    /// maximum coordinates. Confirmed against the original: TTM 0xa114 and 0xa104 both call
    /// <c>draw_rect_filled(x, y, w, h)</c>, which derives its far edge as <c>x + w - 1</c>. That
    /// inclusive edge is the same span as the exclusive one a Unity Rect carries, so the port keeps
    /// extents and does not subtract.
    ///
    /// <para>The logic worth pinning is the SNAPPING. Scale arrives as float32, so a coordinate
    /// sitting exactly on a pixel boundary is a hair off once widened to double, and a bare
    /// floor/ceil then pushes it across — one pixel of border overhang, on every edge that happens to
    /// land on a boundary.</para>
    ///
    /// <para><b>The noise comes from the WIDENING, not from float arithmetic.</b> Computing the same
    /// product entirely in float gives the right answer at these magnitudes — 1000 x 0.7f rounds back
    /// to exactly 700f — so replacing the whole thing with <c>Mathf.FloorToInt(value * scale)</c>
    /// passes every test here. It is casting the scale to double that exposes 0.699999988, and the
    /// <c>Round(..., 3)</c> that puts it back. Anyone testing this has to mutate away the ROUND while
    /// keeping the cast; mutating to plain float arithmetic proves nothing, which cost a round trip
    /// to discover.</para>
    /// </remarks>
    public class BorderRectTests {
        private static Area Rect(int x, int y, int w, int h) => new Area(x, y, w, h);

        [Test]
        public void AtUnitScaleTheBorderSitsExactlyOnTheArea() {
            DrawingUtils.BorderRects b = DrawingUtils.GetBorderRects(Rect(100, 50, 400, 200), Vector2.one);

            Assert.AreEqual(100f, b.Top.x);
            Assert.AreEqual(50f, b.Top.y);
            Assert.AreEqual(400f, b.Top.width, "the top edge spans the whole area");
            Assert.AreEqual(100f, b.Left.x);
            Assert.AreEqual(200f, b.Left.height, "the left edge spans the whole area");
        }

        [Test]
        public void AONEVgaPixelLineIsFiveAcrossAndSixDown() {
            // The original draws a 1px border in a 320x200 space; keeping that weight is why the
            // thickness is the VGA scale rather than 1. A uniform thickness would be visibly thin
            // horizontally and would not match the source art.
            DrawingUtils.BorderRects b = DrawingUtils.GetBorderRects(Rect(0, 0, 400, 200), Vector2.one);

            Assert.AreEqual(GameData.Resources.Layout.OriginalPixel.Width, b.Left.width);
            Assert.AreEqual(GameData.Resources.Layout.OriginalPixel.Height, b.Top.height);
            Assert.AreNotEqual(b.Left.width, b.Top.height, "the two axes scale differently");
        }

        [Test]
        public void TheFarEdgesSitINSIDETheArea_NotBeyondIt() {
            // bottom and right are placed at (far - thickness), so the border is drawn within the
            // rectangle rather than hanging off it.
            DrawingUtils.BorderRects b = DrawingUtils.GetBorderRects(Rect(100, 50, 400, 200), Vector2.one);

            Assert.AreEqual(100f + 400f - GameData.Resources.Layout.OriginalPixel.Width, b.Right.x);
            Assert.AreEqual(50f + 200f - GameData.Resources.Layout.OriginalPixel.Height, b.Bottom.y);
            Assert.AreEqual(100f + 400f, b.Right.x + b.Right.width, "the right edge ends at the far side");
            Assert.AreEqual(50f + 200f, b.Bottom.y + b.Bottom.height, "the bottom edge ends at the far side");
        }

        [Test]
        public void ACoordinateEXACTLYOnAPixelBoundaryDoesNotDriftAcrossIt() {
            // *** The reason the scaling is done in double and rounded. *** 1525 x 1.2f is 1830 on
            // paper but 1830.0000727 once float32's 1.2 is widened, so a bare Ceiling returns 1831 —
            // one pixel of border overhang, on any edge that lands on a boundary. Snapping to 1/1000
            // absorbs the representation noise first.
            var scale = new Vector2(1.2f, 1.2f);
            DrawingUtils.BorderRects b = DrawingUtils.GetBorderRects(Rect(0, 0, 1525, 1000), scale);

            Assert.AreEqual(1830f, b.Top.width, "x2 must snap to 1830, not overshoot to 1831");
            Assert.AreEqual(1830f, b.Right.x + b.Right.width);
        }

        [Test]
        public void TheORIGINDriftsTheOtherWay_AndNeedsTheSameSnap() {
            // *** A different scale is needed to show this, which is the point. *** Ceil overshoots
            // when float32 rounds the product UP (1.2f); floor undershoots when it rounds DOWN.
            // 1000 x 0.7f is 699.99998, so a bare Floor gives 699 and the border starts a pixel
            // outside the area. My first attempt at this test reused the 1.2f scale and passed
            // against a naive implementation — it proved nothing.
            var scale = new Vector2(0.7f, 0.7f);
            DrawingUtils.BorderRects b = DrawingUtils.GetBorderRects(Rect(1000, 1000, 100, 100), scale);

            Assert.AreEqual(700f, b.Top.x, "x1 must snap to 700, not fall back to 699");
            Assert.AreEqual(700f, b.Left.x);
            Assert.AreEqual(700f, b.Top.y, "and the same on the other axis");
        }

        [Test]
        public void THICKNESSNEVERREACHESZERO_HoweverSmallTheScale() {
            // A border thinner than a pixel is an invisible border. Rounding alone would give 0 here
            // and the rectangle would simply not be drawn — the guard is what keeps it visible.
            var tiny = new Vector2(0.01f, 0.01f);
            DrawingUtils.BorderRects b = DrawingUtils.GetBorderRects(Rect(0, 0, 1000, 1000), tiny);

            Assert.GreaterOrEqual(b.Left.width, 1f);
            Assert.GreaterOrEqual(b.Top.height, 1f);
        }

        [Test]
        public void ScalingUpWidensTheLineToo() {
            // The thickness follows the scale, so a border does not thin out as the window grows.
            DrawingUtils.BorderRects small = DrawingUtils.GetBorderRects(Rect(0, 0, 400, 200), Vector2.one);
            DrawingUtils.BorderRects large =
                DrawingUtils.GetBorderRects(Rect(0, 0, 400, 200), new Vector2(2f, 2f));

            Assert.Greater(large.Left.width, small.Left.width);
            Assert.Greater(large.Top.height, small.Top.height);
        }

        [Test]
        public void AllFourEdgesShareTheSameCorner() {
            // Top and left both start at the area's origin, and bottom and right both end at its far
            // corner — so the four rectangles close rather than leaving a gap or overlapping oddly.
            DrawingUtils.BorderRects b = DrawingUtils.GetBorderRects(Rect(37, 11, 300, 150), new Vector2(1.5f, 1.5f));

            Assert.AreEqual(b.Top.x, b.Left.x, "top and left share the origin x");
            Assert.AreEqual(b.Top.y, b.Left.y, "top and left share the origin y");
            Assert.AreEqual(b.Top.x + b.Top.width, b.Right.x + b.Right.width, "right ends where top does");
            Assert.AreEqual(b.Left.y + b.Left.height, b.Bottom.y + b.Bottom.height, "bottom ends where left does");
        }
    }
}
