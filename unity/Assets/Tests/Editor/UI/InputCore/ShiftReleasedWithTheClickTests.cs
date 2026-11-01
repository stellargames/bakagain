namespace BakAgain.Tests.Editor.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine.InputSystem;

    /// <summary>
    /// A Shift let go in the same frame as the click still counts (owner, 2026-11-01: Shift+click thrust).
    /// The world click fires on the button's release, and a quick Shift+click lets Shift go with the button:
    /// read as "not held", the swing became a thrust. Seen in the real Linux build through the build harness.
    /// </summary>
    public class ShiftReleasedWithTheClickTests : InputTestFixture {
        [Test]
        public void AShiftReleasedThisFrame_StillCounts_AndIsGoneTheNextFrame() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var source = new SystemInputSource();
            try {
                Press(keyboard.leftShiftKey);
                Assert.IsTrue(source.LeftShift, "a held Shift");
                Release(keyboard.leftShiftKey);
                Assert.IsTrue(source.LeftShift, "a Shift let go in the click's frame");
                InputSystem.Update();
                Assert.IsFalse(source.LeftShift, "a frame later Shift is up");
            } finally {
                source.Dispose();
            }
        }

        [Test]
        public void TheRightShift_Too() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var source = new SystemInputSource();
            try {
                Press(keyboard.rightShiftKey);
                Release(keyboard.rightShiftKey);
                Assert.IsTrue(source.RightShift);
            } finally {
                source.Dispose();
            }
        }
    }
}
