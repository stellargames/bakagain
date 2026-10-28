namespace BakAgain.UI.InGame {
    using System;
    using BakAgain.UI.InputCore;
    using GameData.Resources.Layout;
    using UnityEngine;

    /// <summary>
    /// Enhanced combat's camera gestures (TASK-972): a drag on the world past the travel threshold
    /// orbits (MouseLookDriver's own gesture: right button, or a finger), the wheel over the world
    /// and a two-finger pinch zoom. Reports; the caller owns the camera.
    /// </summary>
    internal sealed class CombatCameraInput {
        private readonly MouseLookDriver _drag;
        private readonly IPointer _pointer;
        private readonly float _wheelStep;
        private float _pinch;

        public CombatCameraInput(IPointer pointer, EnhancedTravelLayout travel, EnhancedCombatLayout combat) {
            _pointer = pointer;
            _wheelStep = combat.WheelZoomStep;
            // One degree per pixel and no pitch damping: the orbit applies its own layout rate.
            _drag = new MouseLookDriver(pointer, new EnhancedTravelLayout {
                DragThresholdPx = travel.DragThresholdPx, DegreesPerPixel = 1f, PitchFraction = 1f });
        }

        public bool Dragging => _drag.Dragging;

        /// <summary>True for the click a drag's release would otherwise produce.</summary>
        public bool ConsumeClick() => _drag.ConsumeClick();

        /// <param name="orbit">Drag in pixels (x right, y up).</param>
        /// <param name="zoom">Distance factor: below 1 closer.</param>
        public void Tick(bool enabled, Func<Vector2, bool> startsOnWorld, Action<float, float> orbit, Action<float> zoom) {
            bool pinching = enabled && _pointer != null && _pointer.Fingers.Count >= 2;
            Vector2 d = _drag.Tick(enabled && !pinching, startsOnWorld);
            if (d != Vector2.zero) {
                orbit(d.x, d.y);
            }
            if (!enabled || _pointer == null) {
                _pinch = 0f;
                return;
            }
            if (pinching) {
                float span = Vector2.Distance(_pointer.Fingers[0].Position, _pointer.Fingers[1].Position);
                if (_pinch > 0f && span > 0f) {
                    zoom(_pinch / span);
                }
                _pinch = span;
                return;
            }
            _pinch = 0f;
            float wheel = _pointer.Scroll.y;
            if (wheel != 0f && _pointer.CanPoint && startsOnWorld(_pointer.ScreenPosition)) {
                zoom(wheel > 0f ? 1f / _wheelStep : _wheelStep);
            }
        }
    }
}
