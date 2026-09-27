namespace BakAgain.Tests.Editor.UI.InputCore {
    using System.Collections.Generic;
    using BakAgain.UI.InputCore;

    public sealed class FakeInputLayer : IInputLayer {
        public FakeInputLayer(string id, CaptureMode mode, bool wantsFocus = true) {
            Id = id;
            CaptureMode = mode;
            WantsFocus = wantsFocus;
        }

        public string Id { get; }
        public CaptureMode CaptureMode { get; }
        public bool WantsFocus { get; set; }
        public bool ConsumeIntents { get; set; } = true;
        public List<UiIntent> Received { get; } = new List<UiIntent>();
        public int PushedCount { get; private set; }
        public int PoppedCount { get; private set; }
        public bool? LastActive { get; private set; }

        public bool HandleIntent(UiIntent intent) {
            Received.Add(intent);
            return ConsumeIntents;
        }

        public void OnPushed() => PushedCount++;
        public void OnPopped() => PoppedCount++;
        public void OnActiveChanged(bool isActive) => LastActive = isActive;
    }
}
