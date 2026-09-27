namespace BakAgain.Tests.Editor.Graphics {
    using BakAgain.Graphics;
    using NUnit.Framework;
    using UnityEngine;

    public class DrawingUtilsTests {
        [Test]
        public void GetBorderRects_UnitScale_KeepsOriginalCoordinates() {
            // Canonical-space area at a canonical-resolution buffer (scale 1):
            // a 1-VGA-px line is 5 canonical px wide / 6 tall.
            var area = new Area(70, 60, 1455, 618);

            var border = DrawingUtils.GetBorderRects(area, Vector2.one);

            // Outer rect spans X 70..1525, Y 60..678.
            Assert.AreEqual(new Rect(70, 60, 1455, 6), border.Top, "Top");
            Assert.AreEqual(new Rect(70, 672, 1455, 6), border.Bottom, "Bottom");
            Assert.AreEqual(new Rect(70, 60, 5, 618), border.Left, "Left");
            Assert.AreEqual(new Rect(1520, 60, 5, 618), border.Right, "Right");
        }

        [Test]
        public void GetBorderRects_ScalesPositionSizeAndThickness() {
            // 1920x1080 buffer over the 1600x1200 canonical frame => scale (1.2, 0.9).
            var area = new Area(70, 60, 1455, 618);
            var scale = new Vector2(1.2f, 0.9f);

            var border = DrawingUtils.GetBorderRects(area, scale);

            // Outer rect: X floor(84)=84 .. ceil(1830)=1830 (w 1746),
            //             Y floor(54)=54 .. ceil(610.2)=611 (h 557).
            // Thickness: round(5*1.2)=6 horizontal, round(6*0.9)=5 vertical —
            // the same on-screen weight the VGA original had at this window size.
            Assert.AreEqual(new Rect(84, 54, 1746, 5), border.Top, "Top");
            Assert.AreEqual(new Rect(84, 606, 1746, 5), border.Bottom, "Bottom");
            Assert.AreEqual(new Rect(84, 54, 6, 557), border.Left, "Left");
            Assert.AreEqual(new Rect(1824, 54, 6, 557), border.Right, "Right");
        }

        [Test]
        public void GetBorderRects_ThicknessNeverBelowOnePixel() {
            // A deep down-scale (<1/6) must still produce at least a 1px line.
            var area = new Area(0, 0, 600, 600);
            var scale = new Vector2(0.05f, 0.05f);

            var border = DrawingUtils.GetBorderRects(area, scale);

            Assert.AreEqual(1, border.Top.height, "Top thickness floored to 1px");
            Assert.AreEqual(1, border.Left.width, "Left thickness floored to 1px");
        }
    }
}
