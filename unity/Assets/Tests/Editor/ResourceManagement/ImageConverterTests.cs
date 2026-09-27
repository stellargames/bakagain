namespace BakAgain.Tests.Editor.ResourceManagement {
    using BakAgain.ResourceManagement.Converters;
    using GameData.Resources.Image;
    using NUnit.Framework;
    using UnityEngine;

    // Characterization tests pinning the exact pixel layout produced by
    // ImageConverter.ConvertToTexture, so the SetPixel -> SetPixels refactor
    // can be verified to preserve byte-for-byte output.
    public class ImageConverterTests {
        private static Color[] IndexPalette() {
            var colors = new Color[256];
            for (int i = 0; i < 256; i++) {
                colors[i] = new Color(i / 255f, 0f, 0f, 1f);
            }
            return colors;
        }

        private static BmImage MakeImage(int width, int height, byte[] data, ImageFlags flags = 0) {
            return new BmImage("TEST") {
                Width = width,
                Height = height,
                BitMapData = data,
                Flags = flags
            };
        }

        private static void AssertIndex(Texture2D tex, int x, int y, byte expectedIndex) {
            Color pixel = tex.GetPixel(x, y);
            Assert.AreEqual(expectedIndex / 255f, pixel.r, 0.0001f, $"Pixel ({x},{y}) index mismatch");
        }

        [Test]
        public void ConvertToTexture_ColumnMajor_MapsBytesTopRowFirstWithFlippedY() {
            // 2x2, default flags -> else branch: rows of BitMapData fill image
            // top-to-bottom, so byte 0 lands at the highest texture y.
            byte[] data = { 1, 2, 3, 4 };
            BmImage image = MakeImage(2, 2, data);

            Texture2D tex = ImageConverter.ConvertToTexture(image, IndexPalette());
            try {
                AssertIndex(tex, 0, 1, 1);
                AssertIndex(tex, 1, 1, 2);
                AssertIndex(tex, 0, 0, 3);
                AssertIndex(tex, 1, 0, 4);
            } finally {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void ConvertToTexture_ReversedRowColumn_MapsBytesColumnMajor() {
            // 2x2 with ReversedRowColumn -> if branch: column-major fill.
            byte[] data = { 1, 2, 3, 4 };
            BmImage image = MakeImage(2, 2, data, ImageFlags.ReversedRowColumn);

            Texture2D tex = ImageConverter.ConvertToTexture(image, IndexPalette());
            try {
                AssertIndex(tex, 0, 1, 1);
                AssertIndex(tex, 0, 0, 2);
                AssertIndex(tex, 1, 1, 3);
                AssertIndex(tex, 1, 0, 4);
            } finally {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void ConvertToTexture_TransparentIndex0_OnlyZerosBecomeTransparent() {
            byte[] data = { 0, 7, 0, 7 };
            BmImage image = MakeImage(2, 2, data);
            var palette = IndexPalette();

            Texture2D tex = ImageConverter.ConvertToTexture(image, palette, transparentIndex0: true);
            try {
                // index 0 -> alpha 0, non-zero index keeps palette alpha (1)
                Assert.AreEqual(0f, tex.GetPixel(0, 1).a, 0.0001f, "index-0 pixel should be transparent");
                Assert.AreEqual(1f, tex.GetPixel(1, 1).a, 0.0001f, "index-7 pixel should be opaque");
                Assert.AreEqual(0f, tex.GetPixel(0, 0).a, 0.0001f, "index-0 pixel should be transparent");
                Assert.AreEqual(1f, tex.GetPixel(1, 0).a, 0.0001f, "index-7 pixel should be opaque");
            } finally {
                Object.DestroyImmediate(tex);
            }
        }
    }
}
