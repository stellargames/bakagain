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

        public ArenaOrbit Orbit => _orbit;

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
        }

        private Vector3 ToWorld(double across, double away, double height) {
            (int dx, int dy) = ProximityMath.Rotate((int)System.Math.Round(across), (int)System.Math.Round(away), _rotation);
            return BakCoordinateConverter.ConvertPosition(_partyX + dx, _partyY + dy, (int)System.Math.Round(height));
        }
    }
}
