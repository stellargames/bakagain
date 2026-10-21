namespace BakAgain.Tests.PlayMode.ResourceManagement {
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// A sprite built from game art must draw every opaque pixel, so its mesh is the full rectangle.
    /// </summary>
    /// <remarks>
    /// Measured 2026-10-21: the town's "Exit" cursor (POINTERG.BMX#4) was drawn without the lower
    /// right of its "t". Texture, CPU and GPU copies were all intact; Sprite.Create's default Tight
    /// mesh traced the alpha outline and left native columns 20-23, rows 2-9 outside it, and UI
    /// Toolkit draws a sprite through its mesh (TASK-908).
    /// </remarks>
    public class SpriteMeshCoversTheWholeImageTests {
        [Test]
        public void ASpriteFromGameArtHasAFullRectMesh() {
            // Pixel-art shaped: a block, a thin stroke and a lone pixel, the rest transparent.
            var texture = new Texture2D(120, 60, TextureFormat.RGBA32, false);
            var pixels = new Color32[120 * 60];
            for (int y = 0; y < 60; y++) {
                for (int x = 0; x < 120; x++) {
                    bool opaque = (x < 40 && y > 10 && y < 50) || (x >= 100 && x < 105 && y > 5) || (x >= 115 && y < 6);
                    pixels[y * 120 + x] = opaque ? new Color32(70, 130, 190, 255) : new Color32(0, 0, 0, 0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            MethodInfo create = typeof(BakAgain.ResourceManagement.Converters.SpriteUnityConverter)
                .GetMethod("CreateSprite", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(create, "SpriteUnityConverter.CreateSprite(Texture2D)");
            var sprite = (Sprite)create.Invoke(null, new object[] { texture });

            Assert.AreEqual(4, sprite.vertices.Length, "a full-rect quad, not an outline that can drop thin strokes");
            Rect uv = BoundsOf(sprite.uv);
            Assert.That(new[] { uv.xMin, uv.yMin, uv.xMax, uv.yMax }, Is.EqualTo(new[] { 0f, 0f, 1f, 1f }).Within(1e-5f),
                "the quad spans the whole image");
        }

        private static Rect BoundsOf(Vector2[] uv) {
            Vector2 min = uv[0], max = uv[0];
            foreach (Vector2 p in uv) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
