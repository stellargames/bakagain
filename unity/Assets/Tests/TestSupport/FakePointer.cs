namespace BakAgain.UI.InputCore {
    using UnityEngine;

    /// <summary>Settable IPointer for tests + the InputDriver agent seam.</summary>
    public sealed class FakePointer : IPointer {
        public sealed class Button : IPointerButton {
            public bool IsDown { get; private set; }
            public bool PressedThisFrame { get; private set; }
            public bool ReleasedThisFrame { get; private set; }
            public void SetDown(bool down, bool pressedThisFrame = false, bool releasedThisFrame = false) {
                IsDown = down; PressedThisFrame = pressedThisFrame; ReleasedThisFrame = releasedThisFrame;
            }
        }
        public bool IsPresent { get; set; } = true;

        /// <summary>
        /// Defaults to <see cref="IsPresent"/>, so a test that never mentions it behaves exactly as
        /// before. Set it to model a touchscreen: <c>CanPoint</c> true, <c>IsPresent</c> false.
        /// </summary>
        public bool? CanPointOverride { get; set; }

        public bool CanPoint => CanPointOverride ?? IsPresent;
        public Vector2 ScreenPosition { get; set; }
        public Vector2 Delta { get; set; }
        public Vector2 Scroll { get; set; }
        public Button Primary { get; } = new Button();
        public Button Secondary { get; } = new Button();
        IPointerButton IPointer.Primary => Primary;
        IPointerButton IPointer.Secondary => Secondary;
    }
}
