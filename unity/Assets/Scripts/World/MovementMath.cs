namespace BakAgain.World {
    using System;

    /// <summary>
    /// Pure travel-movement math, faithful to the original engine.
    /// Heading is the BaK ushort angle (65536 = 360°). BaK space: X=right, Y=forward.
    /// </summary>
    public static class MovementMath {
        /// <summary>Q14 fixed point — the scale the original's trig tables are in.</summary>
        private const int Q14 = 1 << 14;

        /// <summary>
        /// Forward step displacement — <c>worldmove_vec2_rotate_add_q14</c> (WORLDMOV.C:463).
        /// Backward = negative <paramref name="stepDistance"/>.
        /// </summary>
        /// <remarks>
        /// <b>The original FLOORS; this used to round, and the two engines drifted apart.</b> Its
        /// whole body is three lines:
        /// <code>
        ///   a = angle + R3D_DEG(90);
        ///   *out   += scale * r3d_tbl_cos(a) >> 0xe;
        ///   out[1] += scale * r3d_tbl_sin(a) >> 0xe;
        /// </code>
        /// — a Q14 integer multiply and an ARITHMETIC shift, which rounds toward negative infinity,
        /// and **no fractional remainder is carried between steps**. `Math.Round` on a double got
        /// the magnitude right and the direction wrong for negative axes: at yaw -26624 with a
        /// 100-unit step the ideal is (55.56, -83.15), the original lands on **(+55, -84)** and the
        /// rounding version landed on (+56, -83). About a unit per axis per step, which accumulates
        /// to a whole subcell in ~160 steps and quietly stops a same-input comparison from being one
        /// (TASK-421).
        ///
        /// <para>The +90° and the cos/sin assignment are the original's and are not a rearrangement:
        /// cos(θ+90°) = -sin θ and sin(θ+90°) = cos θ, so this is the same formula the old
        /// comment described — only the arithmetic differs.</para>
        ///
        /// <para>This also brings it into line with
        /// <see cref="Collision.ProximityMath.Rotate"/>, which was already Q14-and-shift.
        /// `HotspotGroundCheckTests` carried a comment noting the two disagreed and comparing them
        /// within a tolerance; they now agree by construction.</para>
        /// </remarks>
        public static (int dx, int dy) StepDelta(ushort heading, int stepDistance) {
            int a = (heading + 0x4000) & 0xFFFF;          // + 90 degrees, in BaK angle space
            double theta = a / 65536.0 * 2.0 * Math.PI;
            long cos = (long)Math.Round(Math.Cos(theta) * Q14);
            long sin = (long)Math.Round(Math.Sin(theta) * Q14);
            return ((int)(stepDistance * cos >> 14), (int)(stepDistance * sin >> 14));
        }

        /// <summary>Add (right) or subtract (left) the turn angle, wrapping in ushort space.</summary>
        public static ushort Turn(ushort heading, int turnAngle, bool left) {
            int delta = left ? -turnAngle : turnAngle;
            int wrapped = ((heading + delta) % 65536 + 65536) % 65536;
            return (ushort)wrapped;
        }
    }
}
