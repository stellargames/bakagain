namespace BakAgain.World.Encounters {
    using BakAgain.World.Collision;
    using BakAgain.World.Converters;
    using GameData.Resources.Combat;
    using UnityEngine;

    /// <summary>
    /// Puts the world camera where an <see cref="ArenaOrbit"/> says (Enhanced combat, TASK-972).
    /// The orbit is party-relative; this applies the party's place and heading and the BaK-to-Unity
    /// axis swap. Plain class on purpose: <c>WorldRuntime</c> rebuilds the arena's GameObjects on
    /// every redraw, so the pose lives with the runtime, not with them.
    /// </summary>
    public sealed class ArenaOrbitCamera {
        private Camera _camera;
        private ArenaOrbit _orbit;
        private int _partyX, _partyY, _rotation;
        private float _bottom, _top = 1f;

        public ArenaOrbit Orbit => _orbit;

        /// <summary>
        /// The screen band the board is framed into, as viewport fractions from the bottom (the HUD covers the
        /// rest): the board's centre is drawn at the band's middle instead of the screen's.
        /// </summary>
        public void FrameInto(float bottom, float top) {
            _bottom = bottom;
            _top = top;
        }

        public void Bind(Camera camera, ArenaOrbit orbit, int partyX = 0, int partyY = 0, int rotation = 0) {
            _camera = camera;
            _orbit = orbit;
            _partyX = partyX;
            _partyY = partyY;
            _rotation = rotation;
        }

        public void Apply() {
            if (_camera == null || _orbit == null) {
                return;
            }
            var eye = _orbit.EyePosition();
            Vector3 eyePos = ToWorld(eye.X, eye.Y, eye.Height);
            Vector3 target = ToWorld(_orbit.Centre.X, _orbit.Centre.Y, 0);
            _camera.transform.position = eyePos;
            _camera.transform.rotation = Quaternion.LookRotation(target - eyePos, Vector3.up);
            // Look below the centre by the angle that puts it at the band's middle (NDC bottom + top - 1).
            float degrees = LiftDegrees();
            if (degrees != 0f) {
                _camera.transform.rotation *= Quaternion.Euler(degrees, 0f, 0f);
            }
        }

        private float LiftDegrees() {
            float lift = _bottom + _top - 1f;
            return lift == 0f ? 0f : Mathf.Atan(lift * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// The orbit pitch at which the horizon is drawn <paramref name="fromTop"/> (fraction of the screen
        /// height) below the top edge: the camera looks down by the pitch plus the band's lift, and a level
        /// ray lands tan(look down) / tan(half the vertical lens) above the screen's middle.
        /// </summary>
        public double PitchForHorizon(float fromTop) {
            if (_camera == null) {
                return double.NaN;
            }
            float half = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return (Mathf.Atan((1f - (2f * fromTop)) * half) * Mathf.Rad2Deg) - LiftDegrees();
        }

        /// <summary>
        /// Whether the ground point (board frame) is drawn inside the framed band, kept
        /// <paramref name="margin"/> (viewport fraction) away from every edge.
        /// </summary>
        public bool InFrame(double x, double y, float margin) {
            if (_camera == null) {
                return true;
            }
            Vector3 v = _camera.WorldToViewportPoint(ToWorld(x, y, 0));
            return v.z > 0 && v.x >= margin && v.x <= 1 - margin && v.y >= _bottom + margin && v.y <= _top - margin;
        }

        /// <summary>
        /// The smallest distance at which every one of <paramref name="corners"/> (board frame, ground level)
        /// lands inside the framed band less the margins, the pitch and yaw being <paramref name="make"/>'s.
        /// Bisection: farther always shrinks the board toward the band's middle. <paramref name="widthOnly"/> fits
        /// the corners across the screen only (a trap: its depth may run off the top and bottom).
        /// </summary>
        public double FitDistance(System.Func<double, ArenaOrbit> make, (double X, double Y)[] corners,
            float marginX, float marginY, bool widthOnly = false) {
            ArenaOrbit keep = _orbit;
            double near = 100, far = 200000;
            for (int i = 0; i < 40; i++) {
                double mid = (near + far) / 2;
                _orbit = make(mid);
                Apply();
                bool fits = true;
                foreach (var c in corners) {
                    Vector3 v = _camera.WorldToViewportPoint(ToWorld(c.X, c.Y, 0));
                    fits &= v.z > 0 && v.x >= marginX && v.x <= 1 - marginX
                        && (widthOnly || (v.y >= _bottom + marginY && v.y <= _top - marginY));
                }
                if (fits) {
                    far = mid;
                } else {
                    near = mid;
                }
            }
            _orbit = keep;
            return far;
        }

        /// <summary>The board-frame point on the ground under a screen point, or null when the ray misses it.</summary>
        public (double Across, double Away)? BoardPointAt(Vector2 screen) {
            if (_camera == null || _orbit == null) {
                return null;
            }
            Ray ray = _camera.ScreenPointToRay(screen);
            var ground = new Plane(Vector3.up, ToWorld(_orbit.Centre.X, _orbit.Centre.Y, 0));
            if (!ground.Raycast(ray, out float hit)) {
                return null;
            }
            Vector3 p = ray.GetPoint(hit);
            int bakX = (int)System.Math.Round(p.x * BakCoordinateConverter.WorldScale) - _partyX;
            int bakY = (int)System.Math.Round(p.z * BakCoordinateConverter.WorldScale) - _partyY;
            (int across, int away) = ProximityMath.Rotate(bakX, bakY, -_rotation);
            return (across, away);
        }

        private Vector3 ToWorld(double across, double away, double height) {
            (int dx, int dy) = ProximityMath.Rotate((int)System.Math.Round(across), (int)System.Math.Round(away), _rotation);
            return BakCoordinateConverter.ConvertPosition(_partyX + dx, _partyY + dy, (int)System.Math.Round(height));
        }
    }
}
