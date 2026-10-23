namespace BakAgain.UI.InGame {
    using System;
    using BakAgain.UI.InputCore;
    using GameData.Resources.Layout;
    using UnityEngine;

    /// <summary>
    /// Enhanced free look's gesture (spec 2026-10-08 §4): a press on the world that moves past a
    /// threshold becomes a look-drag; anything shorter stays a click. Mouse uses the right button,
    /// touch the finger. Polled, like ClassicMovementDriver — continuous input stays on IPointer.
    /// On touch the look follows its OWN finger by id (TASK-967): one that went down on the world,
    /// whatever other finger (a held pad) went down before or after it.
    /// </summary>
    public sealed class MouseLookDriver {
        private readonly IPointer _pointer;
        private readonly EnhancedTravelLayout _layout;
        private bool _armed;
        private float _travelled;
        private int _ticks;
        private int _releasedAtTick = -1;
        private int _finger = -1;   // touch: the look finger's id, -1 for none
        private readonly System.Collections.Generic.HashSet<int> _seen = new System.Collections.Generic.HashSet<int>();
        private readonly System.Collections.Generic.HashSet<int> _seenNow = new System.Collections.Generic.HashSet<int>();

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
                _finger = -1;
                // A finger already down when the look comes back on did not go down on the world now.
                _seen.Clear();
                if (_pointer != null) {
                    foreach (PointerFinger f in _pointer.Fingers) {
                        _seen.Add(f.Id);
                    }
                }
                return Vector2.zero;
            }
            if (!_pointer.IsPresent) {
                return TickFinger(startsOnWorld);
            }
            IPointerButton button = _pointer.Secondary;
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
            return Follow(_pointer.Delta);
        }

        // Touch: a finger new this frame that lands on the world becomes the look finger, if there
        // is none; it looks until it lifts. A finger that went down anywhere else never looks.
        private Vector2 TickFinger(Func<Vector2, bool> startsOnWorld) {
            Vector2? delta = null;
            _seenNow.Clear();
            foreach (PointerFinger f in _pointer.Fingers) {
                _seenNow.Add(f.Id);
                if (_finger < 0 && !_seen.Contains(f.Id) && startsOnWorld(f.Position)) {
                    _finger = f.Id;
                    _armed = true;
                    _travelled = 0f;
                    DragStart = f.Position;
                }
                if (f.Id == _finger) {
                    delta = f.Delta;
                }
            }
            _seen.Clear();
            _seen.UnionWith(_seenNow);
            if (delta == null) {
                if (Dragging) {
                    _releasedAtTick = _ticks;
                }
                _finger = -1;
                _armed = Dragging = false;
                return Vector2.zero;
            }
            return Follow(delta.Value);
        }

        private Vector2 Follow(Vector2 d) {
            if (!Dragging) {
                _travelled += d.magnitude;
                if (_travelled < _layout.DragThresholdPx) {
                    return Vector2.zero;
                }
                Dragging = true;
            }
            return new Vector2(d.x, d.y * _layout.PitchFraction) * _layout.DegreesPerPixel;
        }
    }
}
