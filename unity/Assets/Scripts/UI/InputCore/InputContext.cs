namespace BakAgain.UI.InputCore {
    using UnityEngine.InputSystem;

    public enum InputContextId { UI, Gameplay }

    // Enables exactly one InputActionMap at a time — the standard Input System "input context"
    // pattern. States request a context on enter so only one map owns the devices.
    public sealed class InputContext {
        private readonly InputActionAsset _asset;
        public InputContextId Current { get; private set; }

        public InputContext(InputActionAsset asset) {
            _asset = asset;
        }

        public void Switch(InputContextId id) {
            Enable("UI", id == InputContextId.UI);
            Enable("Player", id == InputContextId.Gameplay);
            Current = id;
        }

        private void Enable(string map, bool on) {
            InputActionMap m = _asset.FindActionMap(map);
            if (m == null) {
                return;
            }
            if (on) {
                m.Enable();
            } else {
                m.Disable();
            }
        }
    }
}
