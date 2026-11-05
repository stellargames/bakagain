namespace BakAgain.Tests.PlayMode.Graphics {
    using BakAgain.Graphics;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// A canonical area scaled to the buffer can come out a pixel wider than the buffer: 1600 x 0.6 is
    /// 960.00006 in floats, so AreaExtensions.Scale's ceiling gives 961. At 1280x720 every chapter scene
    /// then logged "Graphics.CopyTexture called with region not fitting in source element" per copy
    /// (seen in the Linux dev build, 2026-11-05). The copy clamps to both textures.
    /// </summary>
    public class CopyAreaTests {
        [Test]
        public void AnAreaOnePixelWiderThanTheBuffers_CopiesWithoutAnError() {
            var source = new RenderTexture(960, 720, 0);
            var destination = new RenderTexture(960, 720, 0);
            try {
                Drawing.CopyArea(source, destination, new Area { X = 0, Y = 0, Width = 961, Height = 720 });
                Drawing.CopyArea(source, destination, new Area { X = 900, Y = 700, Width = 100, Height = 40 });
            } finally {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(destination);
            }
        }
    }
}
