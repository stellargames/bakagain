namespace BakAgain.UI.InGame {
    using System;
    using BakAgain.UI.InputCore;
    using GameData.Resources.Layout;
    using UnityEngine;

    /// <summary>
    /// Enhanced free look's gesture (spec 2026-10-08 §4): a press on the world that moves past a
    /// threshold becomes a look-drag; anything shorter stays a click. Mouse uses the right button,
    /// touch the finger. Polled, like ClassicMovementDriver — continuous input stays on IPointer.
    /// </summary>
    public sealed class MouseLookDriver {
        private readonly IPointer _pointer;
        private readonly EnhancedTravelLayout _layout;
        private bool _armed;
        private float _travelled;
        private int _ticks;
        private int _releasedAtTick = -1;

        public MouseLookDriver(IPointer pointer, EnhancedTravelLayout layout) {
            _pointer = pointer;
            _layout = layout;
        }

        public bool Dragging { get; private set; }
        public Vector2 DragStart { get; private set; }

        /// <summary>True once for the click a drag's release produces, so it is not also a click.</summary>
        /// <remarks>
        /// UI Toolkit may dispatch that click before or after this frame's <see cref="Tick"/>, so a
        /// drag still in progress counts, and a release counts only for the tick it happened on and
        /// the next — a release whose click never arrives (let go over the HUD) eats nothing later.
        /// </remarks>
        public bool ConsumeClick() {
            bool swallow = Dragging || (_releasedAtTick >= 0 && _ticks - _releasedAtTick <= 1);
            _releasedAtTick = -1;
            return swallow;
        }

        /// <returns>Yaw (x) and pitch (y) to apply this frame, in degrees.</returns>
        public Vector2 Tick(bool enabled, Func<Vector2, bool> startsOnWorld) {
            _ticks++;
            if (!enabled || _pointer == null || !_pointer.CanPoint) {
                _armed = Dragging = false;
                return Vector2.zero;
            }
            IPointerButton button = _pointer.IsPresent ? _pointer.Secondary : _pointer.Primary;
            if (button.PressedThisFrame) {
                _armed = startsOnWorld(_pointer.ScreenPosition);
                _travelled = 0f;
                DragStart = _pointer.ScreenPosition;
            }
            if (!button.IsDown) {
                if (Dragging) {
                    _releasedAtTick = _ticks;
                }
                _armed = Dragging = false;
                return Vector2.zero;
            }
            if (!_armed) {
                return Vector2.zero;
            }
            Vector2 d = _pointer.Delta;
            if (!Dragging) {
                _travelled += d.magnitude;
                if (_travelled < _layout.DragThresholdPx) {
                    return Vector2.zero;
                }
                Dragging = true;
            }
            return d * _layout.DegreesPerPixel;
        }
    }
}
