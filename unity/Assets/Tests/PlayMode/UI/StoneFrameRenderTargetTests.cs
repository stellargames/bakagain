namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.UI;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    public class StoneFrameRenderTargetTests {
        [Test]
        public void DrawingLeavesTheActiveRenderTargetAsItFoundIt() {
            // Graphics.Blit leaves its destination active; the next resize then released the frame's own
            // texture while it was active: "Releasing render texture that is set to be
            // RenderTexture.active!", once per combat-pack open.
            RenderTexture before = RenderTexture.active;
            var frame = new StoneFrame(new VisualElement());

            frame.Draw(new Rect(0f, 0f, 200f, 100f), 10f);

            Assert.AreSame(before, RenderTexture.active);
        }
    }
}
