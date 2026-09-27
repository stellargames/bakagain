namespace BakAgain.Tests.PlayMode.CutScenes {
    using BakAgain.Graphics;
    using GameData.Resources.Animation;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Clipping and orientation for a drawn cutscene image — <c>DrawingUtils.CalculateRects</c>.
    /// </summary>
    /// <remarks>
    /// <b>The clip rect is not a rect.</b> It abuses <see cref="Rect"/> as (xMin, yMin, xMax, yMax)
    /// with INCLUSIVE bounds, so <c>clipArea.width</c> holds a maximum x and not an extent. Both ends
    /// of that already say so in comments; these are the first tests that would fail if someone
    /// "fixed" it to mean a width.
    ///
    /// <para>The invariant worth having above any single case: <b>clipping must never stretch the
    /// image.</b> A clipped draw shows fewer pixels at the same size, so the destination's share of
    /// the full width must equal the source UV's — if those drift apart the picture is being scaled
    /// to fit the clip, which looks like a subtle art bug rather than a clipping one.</para>
    /// </remarks>
    public class DrawingClipTests {
        // (xMin, yMin, xMax, yMax), inclusive — the convention CutsceneState.Initialize uses.
        private static Rect Clip(int xMin, int yMin, int xMax, int yMax) =>
            new Rect(xMin, yMin, xMax, yMax);

        private const float Tolerance = 1e-4f;

        private static void AssertNoStretch(Rect source, Rect dest, float width, float height) {
            Assert.AreEqual(dest.width / width, Mathf.Abs(source.width), Tolerance,
                "horizontal: the destination's share of the image must equal the source UV's");
            Assert.AreEqual(dest.height / height, Mathf.Abs(source.height), Tolerance,
                "vertical: the destination's share of the image must equal the source UV's");
        }

        [Test]
        public void AnImageWellInsideTheClipIsDrawnWholeAndUnmoved() {
            var (source, dest) = DrawingUtils.CalculateRects(
                100, 50, 40, 20, Clip(0, 0, 1599, 1199), Orientation.Normal);

            Assert.AreEqual(100f, dest.x, Tolerance);
            Assert.AreEqual(50f, dest.y, Tolerance);
            Assert.AreEqual(40f, dest.width, Tolerance);
            Assert.AreEqual(20f, dest.height, Tolerance);
            Assert.AreEqual(1f, Mathf.Abs(source.width), Tolerance, "the whole image is shown");
            Assert.AreEqual(1f, Mathf.Abs(source.height), Tolerance);
        }

        [Test]
        public void THEMAXIMUMISINCLUSIVE_SoAnImageEndingExactlyOnItIsNotClipped() {
            // *** The +1 in CalculateRects. *** With an exclusive reading this loses its last column
            // and row — one pixel, on every image that touches the edge, which is exactly the kind of
            // thing nobody sees until a whole scene is built on it.
            var (source, dest) = DrawingUtils.CalculateRects(
                90, 40, 10, 10, Clip(0, 0, 99, 49), Orientation.Normal);

            Assert.AreEqual(10f, dest.width, Tolerance);
            Assert.AreEqual(10f, dest.height, Tolerance);
            Assert.AreEqual(1f, Mathf.Abs(source.width), Tolerance);
            Assert.AreEqual(1f, Mathf.Abs(source.height), Tolerance);
        }

        [Test]
        public void ClippingOnTheLEFTMovesTheDestinationAndADVANCESTheSource() {
            // Half the image is off the left edge: it must be drawn at the edge, at half width, and
            // showing the RIGHT half of the source — not the left half squeezed over.
            var (source, dest) = DrawingUtils.CalculateRects(
                -20, 50, 40, 20, Clip(0, 0, 1599, 1199), Orientation.Normal);

            Assert.AreEqual(0f, dest.x, Tolerance, "drawn at the clip edge");
            Assert.AreEqual(20f, dest.width, Tolerance, "half of it survives");
            Assert.AreEqual(0.5f, source.x, Tolerance, "and it is the right half of the image");
            AssertNoStretch(source, dest, 40, 20);
        }

        [Test]
        public void ClippingOnTheRIGHTShortensBothTogether() {
            var (source, dest) = DrawingUtils.CalculateRects(
                80, 50, 40, 20, Clip(0, 0, 99, 1199), Orientation.Normal);

            Assert.AreEqual(80f, dest.x, Tolerance, "the origin does not move");
            Assert.AreEqual(20f, dest.width, Tolerance, "clipped at xMax inclusive: 99 - 80 + 1");
            Assert.AreEqual(0f, source.x, Tolerance, "still starts at the image's left edge");
            AssertNoStretch(source, dest, 40, 20);
        }

        [Test]
        public void ClippingOnTheTOPMovesTheDestinationDown() {
            var (source, dest) = DrawingUtils.CalculateRects(
                100, -10, 40, 20, Clip(0, 0, 1599, 1199), Orientation.Normal);

            Assert.AreEqual(0f, dest.y, Tolerance);
            Assert.AreEqual(10f, dest.height, Tolerance);
            AssertNoStretch(source, dest, 40, 20);
        }

        [Test]
        public void AHorizontalFlipIsANEGATIVESourceWidth_NotAMovedOrigin() {
            // The flip is expressed by the sign, which is what the blit reads. A port that instead
            // swapped the source's x would flip nothing and silently draw the same picture.
            var (flipped, dest) = DrawingUtils.CalculateRects(
                100, 50, 40, 20, Clip(0, 0, 1599, 1199), Orientation.FlippedHorizontally);
            var (normal, _) = DrawingUtils.CalculateRects(
                100, 50, 40, 20, Clip(0, 0, 1599, 1199), Orientation.Normal);

            Assert.Less(flipped.width, 0f, "flipped horizontally means a negative source width");
            Assert.Greater(normal.width, 0f);
            Assert.AreEqual(40f, dest.width, Tolerance, "the destination is unaffected by the flip");
            AssertNoStretch(flipped, dest, 40, 20);
        }

        [Test]
        public void AVerticalFlipInvertsTheSourceHeightsSign() {
            var (flipped, dest) = DrawingUtils.CalculateRects(
                100, 50, 40, 20, Clip(0, 0, 1599, 1199), Orientation.FlippedVertically);
            var (normal, _) = DrawingUtils.CalculateRects(
                100, 50, 40, 20, Clip(0, 0, 1599, 1199), Orientation.Normal);

            Assert.AreEqual(-Mathf.Abs(normal.height), flipped.height, Tolerance);
            Assert.AreEqual(20f, dest.height, Tolerance);
        }

        [Test]
        public void AFlippedImageClippedOnTheLEFTStillShowsTheCorrectHalf() {
            // *** Where flip and clip interact, and the one most likely to be wrong. *** Clipping
            // takes the same region of the SCREEN either way; which part of the SOURCE that is
            // differs, and the no-stretch invariant has to hold regardless.
            var (source, dest) = DrawingUtils.CalculateRects(
                -20, 50, 40, 20, Clip(0, 0, 1599, 1199), Orientation.FlippedHorizontally);

            Assert.AreEqual(0f, dest.x, Tolerance);
            Assert.AreEqual(20f, dest.width, Tolerance);
            Assert.AreEqual(0.5f, source.x, Tolerance);
            AssertNoStretch(source, dest, 40, 20);
        }

        [Test]
        public void ClippingBothAxesAtOnceShortensBoth() {
            var (source, dest) = DrawingUtils.CalculateRects(
                -10, -5, 40, 20, Clip(0, 0, 1599, 1199), Orientation.Normal);

            Assert.AreEqual(0f, dest.x, Tolerance);
            Assert.AreEqual(0f, dest.y, Tolerance);
            Assert.AreEqual(30f, dest.width, Tolerance);
            Assert.AreEqual(15f, dest.height, Tolerance);
            AssertNoStretch(source, dest, 40, 20);
        }

        [Test]
        public void AnImageENTIRELYOutsideTheClipProducesNothingToDraw() {
            // Not an exception and not a full-size draw: a non-positive destination is how "nothing
            // survives" is expressed, and the caller's blit is a no-op on it.
            var (_, dest) = DrawingUtils.CalculateRects(
                200, 50, 40, 20, Clip(0, 0, 99, 1199), Orientation.Normal);

            Assert.LessOrEqual(dest.width, 0f);
        }
    }
}
