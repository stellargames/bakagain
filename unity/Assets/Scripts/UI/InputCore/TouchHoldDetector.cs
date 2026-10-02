namespace BakAgain.UI.InputCore {
    using UnityEngine;

    /// <summary>
    /// A still finger for <c>holdSeconds</c> — Android's own long-press timeout, 0.5 s — fires once
    /// per press (spec 2026-09-29-android-touch-aids-design.md). Moving beyond the slop cancels it.
    /// </summary>
    public sealed class TouchHoldDetector {
        private readonly float _hold;
        private readonly float _slop;
        private bool _down;
        private bool _fired;
        private bool _cancelled;
        private float _start;
        private Vector2 _origin;

        public TouchHoldDetector(float holdSeconds = 0.5f, float slopPixels = 20f) {
            _hold = holdSeconds;
            _slop = slopPixels;
        }

        /// <summary>Forgets an unfinished press — for the frames nobody ticks this, so their release is never seen.</summary>
        public void Reset() {
            _down = false;
            _fired = false;
            _cancelled = false;
        }

        /// <param name="now">Real-clock seconds (e.g. Time.realtimeSinceStartup), never game time.</param>
        public bool Tick(bool down, Vector2 screenPos, float now) {
            if (!down) {
                _down = false;
                return false;
            }
            if (!_down) {
                _down = true;
                _fired = false;
                _cancelled = false;
                _start = now;
                _origin = screenPos;
                return false;
            }
            if (_fired || _cancelled) {
                return false;
            }
            if ((screenPos - _origin).sqrMagnitude > _slop * _slop) {
                _cancelled = true;
                return false;
            }
            if (now - _start < _hold) {
                return false;
            }
            _fired = true;
            return true;
        }
    }
}
