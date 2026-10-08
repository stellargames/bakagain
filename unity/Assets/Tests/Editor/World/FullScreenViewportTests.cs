namespace BakAgain.Tests.Editor.World {
    using BakAgain.World;
    using NUnit.Framework;
    using UnityEngine;

    public class FullScreenViewportTests {
        private static readonly Rect Stage = new Rect(240, 0, 1440, 1080);

        [Test]
        public void InactiveItIsTheTravelViewport() {
            var inner = new WorldViewport();
            var fs = new FullScreenViewport(inner, () => false, () => new Vector2(1920, 1080));
            Assert.AreEqual(inner.ToScreenRect(Stage), fs.ToScreenRect(Stage));
            Assert.AreEqual(inner.RenderTextureSize(Stage), fs.RenderTextureSize(Stage));
            Assert.AreEqual(inner.CanonicalRect, fs.CanonicalRect);
            Assert.AreEqual(inner.ViewportAspect, fs.ViewportAspect);
            Assert.AreEqual(inner.FocalLength, fs.FocalLength);
        }

        [Test]
        public void ActiveItIsTheWholeWindowAndFollowsAResize() {
            var size = new Vector2(1920, 1080);
            var fs = new FullScreenViewport(new WorldViewport(), () => true, () => size);
            Assert.AreEqual(new Rect(0, 0, 1920, 1080), fs.ToScreenRect(Stage));
            size = new Vector2(2560, 1080);
            Assert.AreEqual(new Vector2Int(2560, 1080), fs.RenderTextureSize(Stage));
        }
    }
}
