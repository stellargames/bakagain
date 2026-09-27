namespace BakAgain.World {
    using BakAgain.UI.InputCore;
    using UnityEngine;

    /// <summary>
    /// Simple WASD + mouse controller for exploring world scenes.
    /// Right-click and hold to look around. WASD to move. Space/Ctrl for up/down.
    ///
    /// Debug-only harness (see WorldTestState, editor-only): added via AddComponent, not resolved
    /// from the DI container, so it has no [Inject] scope of its own — it reads the static
    /// InputDriver.Pointer/Gameplay handles set at container build, the same pattern already used by
    /// other AddComponent'd siblings that aren't container-managed prefabs (TravelLayerHost,
    /// MenuLayerHost resolve UiDriver.Stack the same way).
    /// </summary>
    public class WorldExplorerController : MonoBehaviour {
        [Header("Movement Settings")]
        public float moveSpeed = 50f;
        public float fastMoveSpeed = 200f;
        public float upDownSpeed = 30f;

        [Header("Look Settings")]
        public float mouseSensitivity = 0.2f;
        public float minVerticalAngle = -89f;
        public float maxVerticalAngle = 89f;

        // Distance and yaw thresholds that re-roll the SCX U offset, mirroring the original
        // engine's per-step stripSampleRandomOffset update (set in MoveParty_impl@0x71902,
        // TurnParty_impl@0x71a88, HandleMoveEvent@0x71aae).
        [Header("Terrain Jitter")]
        public float jitterStepDistance = 5f;
        public float jitterTurnDegrees = 5f;

        private static readonly int s_ScxRandomOffsetId = Shader.PropertyToID("_ScxRandomOffset");

        private float _yaw;
        private float _pitch;
        private float _accumDistance;
        private float _jitterYawAnchor;

        private void Start() {
            // Initialize from current rotation
            var euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x;
            if (_pitch > 180f) _pitch -= 360f;
            _jitterYawAnchor = _yaw;
            JitterScxOffset();
        }

        private void Update() {
            IPointer pointer = InputDriver.Pointer;
            IGameplayInput gameplay = InputDriver.Gameplay;
            if (pointer == null || gameplay == null) return;

            // Right-click to look
            if (pointer.Secondary.IsDown) {
                var delta = pointer.Delta;
                _yaw += delta.x * mouseSensitivity;
                _pitch -= delta.y * mouseSensitivity;
                _pitch = Mathf.Clamp(_pitch, minVerticalAngle, maxVerticalAngle);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                if (Mathf.Abs(Mathf.DeltaAngle(_yaw, _jitterYawAnchor)) >= jitterTurnDegrees) {
                    _jitterYawAnchor = _yaw;
                    JitterScxOffset();
                }
            }

            // Movement — gameplay.Move.y = forward(+)/back(-) (W/S/up/down/stick-Y),
            // gameplay.Move.x = right(+)/left(-) strafe (D/A/stick-X), gameplay.Vertical = up(+1)/down(-1).
            float speed = gameplay.Run ? fastMoveSpeed : moveSpeed;
            Vector2 moveInput = gameplay.Move;
            var move = transform.forward * moveInput.y + transform.right * moveInput.x
                       + Vector3.up * gameplay.Vertical;

            if (move != Vector3.zero) {
                var step = move.normalized * (speed * Time.deltaTime);
                transform.position += step;
                _accumDistance += step.magnitude;
                if (_accumDistance >= jitterStepDistance) {
                    _accumDistance = 0f;
                    JitterScxOffset();
                }
            }

            // Scroll wheel to adjust speed
            float scroll = pointer.Scroll.y;
            if (scroll != 0) {
                moveSpeed = Mathf.Clamp(moveSpeed + scroll * 5f, 5f, 500f);
                fastMoveSpeed = moveSpeed * 4f;
            }
        }

        // Original: stripSampleRandomOffset = random() & 0x0F (0..15 source bytes added to
        // the SCX byte offset). 1 source byte ≈ 1 pixel in the 320-wide source → max ~0.047
        // normalized U shift. ClassicTerrain.shader reads this as _ScxRandomOffset.
        private static void JitterScxOffset() {
            int n = Random.Range(0, 16);
            Shader.SetGlobalFloat(s_ScxRandomOffsetId, n / 320f);
        }
    }
}
