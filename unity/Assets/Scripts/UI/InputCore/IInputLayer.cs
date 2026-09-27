namespace BakAgain.UI.InputCore {
    // One participant in the input-ownership stack. A concrete layer wraps a UIDocument panel
    // (added in the follow-up plan); the stack only needs this contract to route intents.
    public interface IInputLayer {
        string Id { get; }
        CaptureMode CaptureMode { get; }
        // True if this passive layer currently wants keyboard/nav focus (ignored for Exclusive).
        bool WantsFocus { get; }
        // Handle a discrete intent routed to this layer. Returns true if consumed.
        bool HandleIntent(UiIntent intent);
        void OnPushed();
        void OnPopped();
        // active = this layer is currently interactable (no Exclusive layer sits above it).
        void OnActiveChanged(bool isActive);
    }
}
