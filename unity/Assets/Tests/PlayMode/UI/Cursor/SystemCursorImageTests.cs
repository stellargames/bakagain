namespace BakAgain.Tests.PlayMode.UI.Cursor {
    using BakAgain.UI.Cursor;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// The image handed to the system cursor on desktop (TASK-1084, forum p=40): the pointer sprite
    /// scaled to its on-screen size, the right way up, its transparent pixels still transparent.
    /// </summary>
    [Category("Graphics")] // reads back a GPU blit: CI runs these in the job with graphics
    public class SystemCursorImageTests {
        [Test]
        public void ThePointerIsScaledToItsScreenSizeTheRightWayUpWithItsTransparencyKept() {
            // A 4x2 sheet; the "sprite" is its right 2x2 half: top-left pixel opaque red, the rest clear.
            var sheet = new Texture2D(4, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            sheet.SetPixels(new Color[8]);           // all clear
            sheet.SetPixel(2, 1, Color.red);         // Texture2D rows run bottom-up: (2,1) is the half's top-left
            sheet.SetPixel(0, 1, Color.green);       // the other half, which must not leak in
            sheet.Apply();
            Texture2D cursor = null;
            try {
                cursor = CursorManager.BuildSystemCursor(sheet, new Rect(2, 0, 2, 2), 6, 6);

                Assert.AreEqual(6, cursor.width);
                Assert.AreEqual(6, cursor.height);
                Assert.AreEqual(TextureFormat.RGBA32, cursor.format, "SetCursor wants RGBA32");
                Assert.AreEqual(1, cursor.mipmapCount, "SetCursor wants no mip chain");
                Color topLeft = cursor.GetPixel(1, 4), bottomRight = cursor.GetPixel(4, 1);
                Assert.Greater(topLeft.r, 0.9f, "the sprite's top-left pixel stays at the top left");
                Assert.Greater(topLeft.a, 0.9f);
                Assert.Less(bottomRight.a, 0.1f, "a clear pixel stays clear, or the pointer gets a box");
                Assert.Less(cursor.GetPixel(0, 5).g, 0.1f, "nothing from outside the sprite's rect");
            } finally {
                Object.DestroyImmediate(sheet);
                if (cursor != null) Object.DestroyImmediate(cursor);
            }
        }
    }
}
