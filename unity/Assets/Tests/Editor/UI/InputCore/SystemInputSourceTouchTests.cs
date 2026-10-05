namespace BakAgain.Tests.Editor.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.InputSystem;

    /// <summary>
    /// A finger is the primary button even when the device also reports a mouse (TASK-789). On the
    /// owner's phone the cast screen saw hover but no press: the button read only the mouse.
    /// </summary>
    public class SystemInputSourceTouchTests : InputTestFixture {
        [Test]
        public void ATouchPressesThePrimaryButton_WithAMousePresentToo() {
            InputSystem.AddDevice<Mouse>();
            InputSystem.AddDevice<Touchscreen>();
            var source = new SystemInputSource();
            try {
                IPointerButton primary = ((IPointer)source).Primary;

                BeginTouch(1, new Vector2(100, 100));

                Assert.IsTrue(primary.PressedThisFrame, "the touch was not seen as a press");
                Assert.IsTrue(primary.IsDown);

                EndTouch(1, new Vector2(100, 100));

                Assert.IsTrue(primary.ReleasedThisFrame);
                Assert.IsFalse(primary.IsDown);
            } finally {
                source.Dispose();
            }
        }

        [Test]
        public void TheMouseButtonStillPresses() {
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.AddDevice<Touchscreen>();
            var source = new SystemInputSource();
            try {
                Press(mouse.leftButton);
                Assert.IsTrue(((IPointer)source).Primary.PressedThisFrame);
            } finally {
                source.Dispose();
            }
        }
    }
}
