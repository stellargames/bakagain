namespace BakAgain.Tests.PlayMode.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;

    public class PointerSeamTests {
        /// <summary>
        /// A touchscreen: it can point, and it does not hover.
        /// </summary>
        /// <remarks>
        /// <b>This pair IS the design</b> — see
        /// <c>docs/superpowers/specs/2026-09-21-touch-input-design.md</c>. If only one half holds,
        /// the split is wrong: both true is a mouse, both false is a pure gamepad, and
        /// <c>CanPoint</c> without <c>IsPresent</c> is the case that had no representation at all
        /// and made the whole UI a no-op on a touch device (TASK-67).
        /// </remarks>
        [Test]
        public void ATouchscreenCanPointWithoutHovering() {
            var touch = new FakePointer { IsPresent = false, CanPointOverride = true };

            Assert.IsTrue(touch.CanPoint, "a press carries its own position");
            Assert.IsFalse(touch.IsPresent, "there is nothing hovering to draw a cursor at");
        }

        /// <summary>
        /// <c>CanPoint</c> follows <c>IsPresent</c> unless a test says otherwise.
        /// </summary>
        /// <remarks>
        /// The default is what keeps every pre-existing FakePointer test meaning what it meant:
        /// a mouse answers both, a pure gamepad answers neither.
        /// </remarks>
        [Test]
        public void CanPointDefaultsToIsPresent() {
            Assert.IsTrue(new FakePointer { IsPresent = true }.CanPoint, "a mouse hovers and points");
            Assert.IsFalse(new FakePointer { IsPresent = false }.CanPoint, "a pad does neither");
        }

        [Test]
        public void FakePointer_ExposesSetValues() {
            var p = new FakePointer();
            p.IsPresent = true;
            p.ScreenPosition = new Vector2(120f, 80f);
            p.Primary.SetDown(true, pressedThisFrame: true);

            Assert.IsTrue(p.IsPresent);
            Assert.AreEqual(new Vector2(120f, 80f), p.ScreenPosition);
            Assert.IsTrue(p.Primary.IsDown);
            Assert.IsTrue(p.Primary.PressedThisFrame);
            Assert.IsFalse(p.Secondary.IsDown);
        }
    }
}
