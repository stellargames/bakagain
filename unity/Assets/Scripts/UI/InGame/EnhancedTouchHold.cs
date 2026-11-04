namespace BakAgain.UI.InGame {
    using BakAgain.UI.InputCore;
    using UnityEngine;

    /// <summary>
    /// Enhanced combat's long press on the board (TASK-972): a still finger, read from the touch
    /// tracker's own press serial, fires once per press; the tap its release produces is eaten, and
    /// only that one — the swallow belongs to the press that fired, never to a later one.
    /// </summary>
    internal sealed class EnhancedTouchHold {
        private readonly TouchHoldDetector _hold;
        private int _serial = int.MinValue;
        private int _fired = int.MinValue;

        public EnhancedTouchHold(float holdSeconds, float slopPixels) =>
            _hold = new TouchHoldDetector(holdSeconds, slopPixels);

        /// <summary>Per frame. True once, when press <paramref name="serial"/> has been held still.</summary>
        public bool Tick(int serial, bool down, Vector2 position, float now) {
            if (serial != _serial) {
                _serial = serial;
                _hold.Reset();
            }
            if (!_hold.Tick(down, position, now)) {
                return false;
            }
            _fired = serial;
            return true;
        }

        /// <summary>A tap arrived from press <paramref name="serial"/>: true when it is a long press's release.</summary>
        public bool TakeTap(int serial) {
            bool eat = serial == _fired;
            _fired = int.MinValue;
            return eat;
        }

        public void Reset() => _hold.Reset();
    }
}
