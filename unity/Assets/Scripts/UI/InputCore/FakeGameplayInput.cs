namespace BakAgain.UI.InputCore {
    using UnityEngine;
    public sealed class FakeGameplayInput : IGameplayInput {
        public Vector2 Move { get; set; }
        public Vector2 Look { get; set; }
        public bool Run { get; set; }
        public float Vertical { get; set; }
        public float Zoom { get; set; }
        public bool LeftShift { get; set; }
        public bool RightShift { get; set; }
    }
}
