namespace BakAgain.Core.States.Debug {
    using BakAgain.UI.InputCore;
    using UnityEngine;

    /// <summary>
    /// Input handler for ModelDebugState.
    /// Handles keyboard, mouse, and UI input for model navigation.
    ///
    /// Debug-only harness (editor-only, see ModelDebugState/RootLifetimeScope): added via
    /// AddComponent + Initialize(), not resolved from the DI container, so it has no [Inject] scope
    /// of its own — it reads the static InputDriver.Pointer/ModelDebug handles set at container
    /// build, the same pattern already used by other AddComponent'd siblings that aren't
    /// container-managed prefabs (TravelLayerHost, MenuLayerHost resolve UiDriver.Stack the same
    /// way). The step/rotate keys are debug-tool-only intent (not gameplay movement), so they come
    /// from IModelDebugInput rather than IGameplayInput.
    /// </summary>
    public class ModelDebugInput : MonoBehaviour {
        private ModelDebugState _state;
        private bool _isDragging = false;
        private Vector2 _lastPointerPosition;

        public void Initialize(ModelDebugState state) {
            _state = state;
        }

        void Update() {
            if (_state == null) return;
            IPointer pointer = InputDriver.Pointer;
            if (pointer == null) return;

            IModelDebugInput debug = InputDriver.ModelDebug;
            if (debug != null) {
                // Keyboard navigation - model navigation
                if (debug.PrevModel) {
                    _state.PreviousModel();
                }
                if (debug.NextModel) {
                    _state.NextModel();
                }

                // Keyboard navigation - zone navigation
                if (debug.PrevZone) {
                    _state.PreviousZone();
                }
                if (debug.NextZone) {
                    _state.NextZone();
                }

                // Model rotation (arrow keys)
                float rotateSpeed = 90f * Time.deltaTime;
                if (debug.RotateLeft) {
                    _state.RotateModel(-rotateSpeed);
                }
                if (debug.RotateRight) {
                    _state.RotateModel(rotateSpeed);
                }
            }

            // Mouse orbit
            if (pointer.Primary.PressedThisFrame || pointer.Secondary.PressedThisFrame) {
                _isDragging = true;
                _lastPointerPosition = pointer.ScreenPosition;
            }
            if (pointer.Primary.ReleasedThisFrame || pointer.Secondary.ReleasedThisFrame) {
                _isDragging = false;
            }

            if (_isDragging) {
                Vector2 currentPosition = pointer.ScreenPosition;
                Vector2 delta = currentPosition - _lastPointerPosition;
                _lastPointerPosition = currentPosition;

                float orbitSpeed = 0.3f;
                _state.OrbitCamera(delta.x * orbitSpeed, delta.y * orbitSpeed);
            }

            // Zoom with scroll
            float scroll = pointer.Scroll.y;
            if (scroll != 0) {
                _state.ZoomCamera(scroll * 0.05f);
            }
        }
    }
}
