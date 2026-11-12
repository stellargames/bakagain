namespace BakAgain.World {
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Enhanced travel (TASK-1056, owner: tilt only): standing right next to a chest or mound, the
    /// thing sank out of the bottom of the view (or under a HUD stone) and had to be stepped back from
    /// before it could be clicked. The view tilts down just enough to keep the foot of the nearest
    /// clickable thing ahead in sight. View only, like free look's pitch: nothing reads it as a rule.
    /// </summary>
    public static class NearObjectTilt {
        /// <summary>How far inside the bottom edge the foot is kept.</summary>
        public const float MarginDegrees = 6f;

        /// <summary>The most the view tilts for a thing at the party's feet.</summary>
        public const float MaxDegrees = 35f;

        /// <summary>
        /// Degrees to tilt down (0 = none) so every clickable thing in reach and inside the view's
        /// sides has its foot inside the view.
        /// </summary>
        /// <param name="eye">The camera position.</param>
        /// <param name="forward">The view's heading; only its flat part is used.</param>
        /// <param name="basePitchDown">The view's pitch without this tilt, degrees, down positive.</param>
        /// <param name="halfVertical">Half the vertical field of view, degrees.</param>
        /// <param name="halfHorizontal">Half the horizontal field of view, degrees.</param>
        /// <param name="things">Each clickable thing's foot and the flat distance it can be clicked from.</param>
        public static float DegreesFor(Vector3 eye, Vector3 forward, float basePitchDown, float halfVertical,
            float halfHorizontal, IEnumerable<(Vector3 Foot, float Reach)> things) {
            var flatForward = new Vector3(forward.x, 0f, forward.z);
            float tilt = 0f;
            foreach ((Vector3 foot, float reach) in things) {
                Vector3 d = foot - eye;
                var flat = new Vector3(d.x, 0f, d.z);
                float distance = flat.magnitude;
                if (distance > reach || distance < 1e-3f || Vector3.Angle(flatForward, flat) > halfHorizontal) {
                    continue;
                }
                float footDown = Mathf.Atan2(-d.y, distance) * Mathf.Rad2Deg;
                tilt = Mathf.Max(tilt, footDown + MarginDegrees - (basePitchDown + halfVertical));
            }
            return Mathf.Clamp(tilt, 0f, MaxDegrees);
        }
    }
}
