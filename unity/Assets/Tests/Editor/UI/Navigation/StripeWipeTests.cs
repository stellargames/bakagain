namespace BakAgain.Tests.Editor.UI.Navigation {
    using BakAgain.UI.Navigation;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// The camp panel's stripe wipe (TASK-1109 row 8, Classic): screen_wipe_horizontal uncovers the new
    /// frame from the centre of the rect outwards to both sides (SCREEN.C:464-472).
    /// </summary>
    public class StripeWipeTests {
        private static readonly Rect Panel = new(65, 66, 1470, 606);

        [Test]
        public void TheOldFrameStillCoversTheWholeRectAtTheStart() {
            (Rect left, Rect right) = ScreenFade.StripeWipePieces(Panel, 0f);
            Assert.AreEqual(Panel.x, left.x);
            Assert.AreEqual(Panel.xMax, right.xMax, 0.001f);
            Assert.AreEqual(Panel.width, left.width + right.width, 0.001f);
        }

        [Test]
        public void HalfwayTheGapIsHalfTheRectAroundItsCentre() {
            (Rect left, Rect right) = ScreenFade.StripeWipePieces(Panel, 0.5f);
            Assert.AreEqual(Panel.center.x - Panel.width / 4f, left.xMax, 0.001f);
            Assert.AreEqual(Panel.center.x + Panel.width / 4f, right.x, 0.001f);
            Assert.AreEqual(Panel.y, right.y);
            Assert.AreEqual(Panel.height, right.height);
        }

        [Test]
        public void AtTheEndNothingOfTheOldFrameIsLeft() {
            (Rect left, Rect right) = ScreenFade.StripeWipePieces(Panel, 1f);
            Assert.AreEqual(0f, left.width, 0.001f);
            Assert.AreEqual(0f, right.width, 0.001f);
        }
    }
}
