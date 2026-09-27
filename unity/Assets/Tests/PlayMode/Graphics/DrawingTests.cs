namespace BakAgain.Tests.PlayMode.Graphics
{
    using BakAgain.Graphics;
    using NUnit.Framework;
    using UnityEngine;

    public class DrawingUtilsTests
    {
        [Test]
        public void CalculateRects_ImageFullyWithinClipBounds_NoClipping()
        {
            // Arrange
            float x = 100;
            float y = 50;
            float width = 50;
            float height = 40;
            var clipArea = new Rect(15, 11, 303, 111); // xMin, yMin, xMax, yMax
            var orientation = Orientation.Normal;

            // Act
            var (sourceRect, destinationRect) = DrawingUtils.CalculateRects(x, y, width, height, clipArea, orientation);

            // Assert
            Assert.AreEqual(0f, sourceRect.x);
            Assert.AreEqual(0f, sourceRect.y);
            Assert.AreEqual(1f, sourceRect.width);
            Assert.AreEqual(1f, sourceRect.height);
            Assert.AreEqual(x, destinationRect.x);
            Assert.AreEqual(y, destinationRect.y);
            Assert.AreEqual(width, destinationRect.width);
            Assert.AreEqual(height, destinationRect.height);
        }

        [Test]
        public void CalculateRects_ImageClippedOnRight_PartialSourceRect()
        {
            // Arrange - using the example case
            float x = 262;
            float y = 28;
            float width = 72;
            float height = 84;
            var clipArea = new Rect(15, 11, 303, 111); // xMin, yMin, xMax, yMax
            var orientation = Orientation.Normal;

            // Act
            var (sourceRect, destinationRect) = DrawingUtils.CalculateRects(x, y, width, height, clipArea, orientation);

            // Assert
            Assert.AreEqual(0f, sourceRect.x);
            Assert.AreEqual(0f, sourceRect.y);
            Assert.AreEqual((304f - 262f) / width, sourceRect.width, 0.01f); // Should be ~0.583 (42/72)
            Assert.AreEqual(1f, sourceRect.height);
            Assert.AreEqual(262f, destinationRect.x);
            Assert.AreEqual(28f, destinationRect.y);
            Assert.AreEqual(42f, destinationRect.width); // 304 - 262 = 42
            Assert.AreEqual(84f, destinationRect.height);
        }

        [Test]
        public void CalculateRects_ImageClippedOnLeft_PartialSourceRect()
        {
            // Arrange
            float x = 0;
            float y = 50;
            float width = 50;
            float height = 40;
            var clipArea = new Rect(15, 11, 303, 111);
            var orientation = Orientation.Normal;

            // Act
            var (sourceRect, destinationRect) = DrawingUtils.CalculateRects(x, y, width, height, clipArea, orientation);

            // Assert
            Assert.AreEqual(15f / width, sourceRect.x);
            Assert.AreEqual(0f, sourceRect.y);
            Assert.AreEqual(35f / width, sourceRect.width); // (50 - 15) / 50
            Assert.AreEqual(1f, sourceRect.height);
            Assert.AreEqual(15f, destinationRect.x);
            Assert.AreEqual(50f, destinationRect.y);
            Assert.AreEqual(35f, destinationRect.width); // 50 - 15 = 35
            Assert.AreEqual(40f, destinationRect.height);
        }

        [Test]
        public void CalculateRects_ImageClippedBothSides_PartialSourceRect()
        {
            // Arrange
            float x = 0;
            float y = 50;
            float width = 400;
            float height = 40;
            var clipArea = new Rect(15, 11, 303, 111);
            var orientation = Orientation.Normal;

            // Act
            var (sourceRect, destinationRect) = DrawingUtils.CalculateRects(x, y, width, height, clipArea, orientation);

            // Assert
            Assert.AreEqual(15f / width, sourceRect.x);
            Assert.AreEqual(0f, sourceRect.y);
            Assert.AreEqual(289f / width, sourceRect.width); // (304 - 15) / 400 = 0.7225
            Assert.AreEqual(1f, sourceRect.height);
            Assert.AreEqual(15f, destinationRect.x);
            Assert.AreEqual(50f, destinationRect.y);
            Assert.AreEqual(289f, destinationRect.width); // 304 - 15 = 289
            Assert.AreEqual(40f, destinationRect.height);
        }

        [Test]
        public void CalculateRects_ImageClippedVertically_PartialSourceRect()
        {
            // Arrange
            float x = 100;
            float y = 0;
            float width = 50;
            float height = 150;
            var clipArea = new Rect(15, 11, 303, 111);
            var orientation = Orientation.Normal;

            // Act
            var (sourceRect, destinationRect) = DrawingUtils.CalculateRects(x, y, width, height, clipArea, orientation);

            // Assert
            Assert.AreEqual(0f, sourceRect.x);
            Assert.AreEqual(0.253333f, sourceRect.y, 0.01f); // 1 - (11 + 101)/150
            Assert.AreEqual(1f, sourceRect.width);
            Assert.AreEqual(0.673333f, sourceRect.height, 0.01f); // 101/150
            Assert.AreEqual(100f, destinationRect.x);
            Assert.AreEqual(11f, destinationRect.y);
            Assert.AreEqual(50f, destinationRect.width);
            Assert.AreEqual(101f, destinationRect.height); // 112 - 11 = 101 (inclusive bounds)
        }

        [Test]
        public void CalculateRects_ImageFlippedHorizontally_FlippedSourceRect()
        {
            // Arrange
            float x = 262;
            float y = 28;
            float width = 72;
            float height = 84;
            var clipArea = new Rect(15, 11, 303, 111);
            var orientation = Orientation.FlippedHorizontally;

            // Act
            var (sourceRect, destinationRect) = DrawingUtils.CalculateRects(x, y, width, height, clipArea, orientation);

            // Assert
            Assert.AreEqual(1f, sourceRect.x);
            Assert.AreEqual(0f, sourceRect.y);
            Assert.AreEqual(-(42f / width), sourceRect.width, 0.01f); // Should be ~-0.569 (-41/72)
            Assert.AreEqual(1f, sourceRect.height);
            Assert.AreEqual(262f, destinationRect.x);
            Assert.AreEqual(28f, destinationRect.y);
            Assert.AreEqual(42f, destinationRect.width); // 304 - 262 = 42 (inclusive bounds)
            Assert.AreEqual(84f, destinationRect.height);
        }

        // --- Orientation: the seven flipped and scaled draw commands --------------------------
        //
        // *** ~1,439 SHIPPED DRAWS ARE NOT Orientation.Normal AND NONE OF THEM WAS COVERED. ***
        // DrawImageFlippedHorizontally (528 uses), DrawImageScaled (415),
        // DrawImageFlippedHorizontallyScaled (367), DrawImageRotated180 (81) and three smaller
        // ones all funnel into one Drawing.DrawImage, distinguished by this enum alone. Every
        // CalculateRects test above passes Normal.
        //
        // The mapping from opcode to orientation is verified against the driver rather than the
        // command names: TTM sets flip_flags = (opcode & 0xf0) >> 4, and the blitter dispatches
        // through g_apfnMcgSpriteFlipDispatch (MCG.ASM:2394), a 4-slot table of
        // noflip / VFLIP / HFLIP / hvflip. So opcode 0xa514 is a vertical flip and 0xa524 a
        // horizontal one, which is what the extractor calls them.

        [Test]
        public void OurEnumNumbersTheFlipsTheOPPOSITEWayToTheTTMNIBBLE() {
            // *** DO NOT DERIVE Orientation FROM THE OPCODE. *** The driver's table is
            // 1 = vertical, 2 = horizontal; ours is 1 = horizontal, 2 = vertical. Nothing casts
            // between them today, and a cast would silently mirror 550 draws the wrong way while
            // leaving Normal and Rotated180 — the values that happen to agree — looking correct.
            Assert.AreEqual(1, (int)Orientation.FlippedHorizontally);
            Assert.AreEqual(2, (int)Orientation.FlippedVertically);
            Assert.AreEqual(3, (int)Orientation.Rotated180, "and 3 agrees by coincidence");
        }

        [Test]
        public void Rotated180IsExactlyTheTwoFlipsTogether() {
            // CalculateRects tests each axis with HasFlag, so this equality is what makes the
            // rotated commands work at all. Giving Rotated180 its own non-flag value would leave
            // both branches unentered and the image drawn unrotated.
            Assert.AreEqual(Orientation.FlippedHorizontally | Orientation.FlippedVertically,
                Orientation.Rotated180);
            Assert.IsTrue(Orientation.Rotated180.HasFlag(Orientation.FlippedHorizontally));
            Assert.IsTrue(Orientation.Rotated180.HasFlag(Orientation.FlippedVertically));
        }




        [Test]
        public void Rotated180ComposesTheTwoFLIPSRectsRatherThanHavingItsOwn() {
            // DrawingClipTests pins each single flip; nothing pinned the pair. Rotated180 must
            // come out as exactly the horizontal flip's x and the vertical flip's y.
            var clip = new Rect(0, 0, 1000, 1000);
            var (h, _) = DrawingUtils.CalculateRects(10, 20, 50, 40, clip, Orientation.FlippedHorizontally);
            var (v, _) = DrawingUtils.CalculateRects(10, 20, 50, 40, clip, Orientation.FlippedVertically);
            var (both, _) = DrawingUtils.CalculateRects(10, 20, 50, 40, clip, Orientation.Rotated180);

            Assert.AreEqual(h.x, both.x, "the rotated read takes its x from the horizontal flip");
            Assert.AreEqual(h.width, both.width);
            Assert.AreEqual(v.y, both.y, "and its y from the vertical one");
            Assert.AreEqual(v.height, both.height);
        }

    }
}
